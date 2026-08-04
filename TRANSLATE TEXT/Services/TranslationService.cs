using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using TranslateText.Core;
using TranslateText.Models;

namespace TranslateText.Services
{
    /// <summary>
    /// Resolves AEC glossary entries and calls the configured translation endpoint.
    /// Successful results are cached; failed requests are never cached as translations.
    /// </summary>
    public static class TranslationService
    {
        private const int MaxCacheEntries = 4096;
        private const int MaxApiAttempts = 3;
        private const int RetryDelayMilliseconds = 1000;

        private static readonly HttpClient HttpClient;
        private static readonly ConcurrentDictionary<string, string> Cache =
            new ConcurrentDictionary<string, string>();
        private static readonly ConcurrentQueue<string> CacheOrder =
            new ConcurrentQueue<string>();
        private static readonly ConcurrentDictionary<string, Lazy<Task<string>>> InFlight =
            new ConcurrentDictionary<string, Lazy<Task<string>>>();
        private static readonly string[] UserAgents =
        {
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/121.0"
        };

        private static int _userAgentIndex = -1;

        static TranslationService()
        {
            var handler = new HttpClientHandler
            {
                MaxConnectionsPerServer = TranslationBatchProcessor.DefaultMaxConcurrency
            };
            HttpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
            HttpClient.DefaultRequestHeaders.ExpectContinue = false;
        }

        /// <summary>
        /// Translates one formatted source string after protecting AutoCAD format codes.
        /// Concurrent callers for the same text and language pair share one request.
        /// </summary>
        public static async Task<string> ProcessAsync(
            string input,
            string sourceLanguage,
            string targetLanguage,
            SemaphoreSlim semaphore)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;
            if (semaphore == null) throw new ArgumentNullException(nameof(semaphore));

            string cacheKey = string.Concat(
                sourceLanguage,
                "\u001F",
                targetLanguage,
                "\u001F",
                input);
            if (Cache.TryGetValue(cacheKey, out string cachedResult)) return cachedResult;

            Lazy<Task<string>> pending = InFlight.GetOrAdd(
                cacheKey,
                _ => new Lazy<Task<string>>(
                    () => ProcessUncachedAsync(
                        input,
                        sourceLanguage,
                        targetLanguage,
                        semaphore,
                        cacheKey),
                    LazyThreadSafetyMode.ExecutionAndPublication));

            try
            {
                return await pending.Value.ConfigureAwait(false);
            }
            finally
            {
                var entry = new KeyValuePair<string, Lazy<Task<string>>>(cacheKey, pending);
                ((ICollection<KeyValuePair<string, Lazy<Task<string>>>>)InFlight).Remove(entry);
            }
        }

        private static async Task<string> ProcessUncachedAsync(
            string input,
            string sourceLanguage,
            string targetLanguage,
            SemaphoreSlim semaphore,
            string cacheKey)
        {
            MaskResult maskResult = FormatProtector.MaskText(input);

            if (FormatProtector.IsAllTags(maskResult.MaskedText))
            {
                CacheResult(cacheKey, input);
                return input;
            }

            string glossaryResult = AecGlossary.Lookup(
                maskResult.MaskedText,
                sourceLanguage,
                targetLanguage);
            if (!string.IsNullOrEmpty(glossaryResult))
            {
                string restoredGlossaryText = FormatProtector.UnmaskText(
                    glossaryResult,
                    maskResult.Codes);
                CacheResult(cacheKey, restoredGlossaryText);
                return restoredGlossaryText;
            }

            string translatedText = await TranslateApiAsync(
                maskResult.MaskedText,
                sourceLanguage,
                targetLanguage,
                semaphore).ConfigureAwait(false);
            string finalText = FormatProtector.UnmaskText(translatedText, maskResult.Codes);
            CacheResult(cacheKey, finalText);
            return finalText;
        }

        private static void CacheResult(string key, string value)
        {
            if (!Cache.TryAdd(key, value))
            {
                Cache[key] = value;
                return;
            }

            CacheOrder.Enqueue(key);
            while (Cache.Count > MaxCacheEntries && CacheOrder.TryDequeue(out string oldestKey))
                Cache.TryRemove(oldestKey, out string ignored);
        }

        private static async Task<string> TranslateApiAsync(
            string text,
            string sourceLanguage,
            string targetLanguage,
            SemaphoreSlim semaphore)
        {
            await semaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                string apiKey = AppSettings.LoadApiKey();
                Exception lastException = null;

                for (int attempt = 0; attempt < MaxApiAttempts; attempt++)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(apiKey))
                        {
                            return await CallCloudTranslationApiAsync(
                                text,
                                sourceLanguage,
                                targetLanguage,
                                apiKey).ConfigureAwait(false);
                        }

                        return await CallFreeTranslateApiAsync(
                            text,
                            sourceLanguage,
                            targetLanguage).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (IsRetryableTranslationFailure(exception))
                    {
                        lastException = exception;
                        if (attempt < MaxApiAttempts - 1)
                        {
                            await Task.Delay(
                                RetryDelayMilliseconds * (attempt + 1)).ConfigureAwait(false);
                        }
                    }
                }

                throw new InvalidOperationException(
                    $"Translation API failed after {MaxApiAttempts} attempts.",
                    lastException);
            }
            finally
            {
                semaphore.Release();
            }
        }

        private static bool IsRetryableTranslationFailure(Exception exception)
        {
            return exception is HttpRequestException ||
                exception is TaskCanceledException ||
                exception is InvalidDataException;
        }

        private static async Task<string> CallCloudTranslationApiAsync(
            string text,
            string sourceLanguage,
            string targetLanguage,
            string apiKey)
        {
            string url =
                $"https://translation.googleapis.com/language/translate/v2?key={apiKey}";
            var payload = new Dictionary<string, string>
            {
                ["q"] = text,
                ["target"] = targetLanguage,
                ["format"] = "text"
            };
            if (!string.Equals(sourceLanguage, "auto", StringComparison.OrdinalIgnoreCase))
                payload["source"] = sourceLanguage;

            string jsonPayload = new JavaScriptSerializer().Serialize(payload);
            using (var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json"))
            using (HttpResponseMessage response = await HttpClient
                .PostAsync(url, content)
                .ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return ParseCloudTranslationJson(json);
            }
        }

        private static string ParseCloudTranslationJson(string json)
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                var root = serializer.DeserializeObject(json) as Dictionary<string, object>;
                if (root == null ||
                    !root.TryGetValue("data", out object dataValue) ||
                    !(dataValue is Dictionary<string, object> data) ||
                    !data.TryGetValue("translations", out object translationsValue) ||
                    !(translationsValue is object[] translations) ||
                    translations.Length == 0 ||
                    !(translations[0] is Dictionary<string, object> translation) ||
                    !translation.TryGetValue("translatedText", out object translatedValue))
                {
                    throw new InvalidDataException(
                        "Google Cloud Translation response did not contain a translation.");
                }

                string translatedText = Convert.ToString(translatedValue);
                if (string.IsNullOrWhiteSpace(translatedText))
                {
                    throw new InvalidDataException(
                        "Google Cloud Translation returned an empty translation.");
                }

                return WebUtility.HtmlDecode(translatedText);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(
                    "Google Cloud Translation returned invalid JSON.",
                    exception);
            }
        }

        private static async Task<string> CallFreeTranslateApiAsync(
            string text,
            string sourceLanguage,
            string targetLanguage)
        {
            string url =
                "https://translate.googleapis.com/translate_a/single" +
                $"?client=gtx&sl={sourceLanguage}&tl={targetLanguage}&dt=t" +
                $"&q={System.Web.HttpUtility.UrlEncode(text)}";

            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.Add("User-Agent", GetNextUserAgent());
                using (HttpResponseMessage response = await HttpClient
                    .SendAsync(request)
                    .ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return ParseFreeTranslateJson(json);
                }
            }
        }

        private static string ParseFreeTranslateJson(string json)
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                var result = new StringBuilder();
                var root = serializer.DeserializeObject(json) as object[];
                if (root != null && root.Length > 0 && root[0] is object[] sentences)
                {
                    foreach (object sentenceValue in sentences)
                    {
                        if (!(sentenceValue is object[] sentence) || sentence.Length == 0)
                            continue;
                        if (sentence[0] is string translatedSegment)
                            result.Append(translatedSegment);
                    }
                }

                if (result.Length == 0)
                {
                    throw new InvalidDataException(
                        "Google Translate response did not contain a translation.");
                }
                return result.ToString();
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(
                    "Google Translate returned invalid JSON.",
                    exception);
            }
        }

        private static string GetNextUserAgent()
        {
            int index = Interlocked.Increment(ref _userAgentIndex) & int.MaxValue;
            return UserAgents[index % UserAgents.Length];
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using TranslateText.Core;
using TranslateText.Models;

namespace TranslateText.Services
{
    /// <summary>
    /// Dịch vụ dịch thuật: Check Cache → Tra từ điển AEC → Gọi Google Translate API.
    /// Sử dụng HttpClient static + Semaphore để giới hạn concurrent request.
    /// Hỗ trợ API Key tùy chọn (lưu trong Registry) để dùng Google Cloud Translation thay vì scraping.
    /// </summary>
    public static class TranslationService
    {
        private const int MaxCacheEntries = 4096;
        private static readonly HttpClient _httpClient;
        private static int _userAgentIndex = -1;

        // Cache is bounded so long AutoCAD sessions cannot grow memory without limit.
        private static readonly ConcurrentDictionary<string, string> _cache
            = new ConcurrentDictionary<string, string>();
        private static readonly ConcurrentQueue<string> _cacheOrder = new ConcurrentQueue<string>();

        // Concurrent callers for the same text/language pair share one request.
        private static readonly ConcurrentDictionary<string, Lazy<Task<string>>> _inFlight
            = new ConcurrentDictionary<string, Lazy<Task<string>>>();

        // Danh sách User-Agent để giả lập trình duyệt, tránh bị Google chặn
        private static readonly string[] _userAgents = new string[]
        {
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/121.0"
        };

        static TranslationService()
        {
            ServicePointManager.DefaultConnectionLimit = 100;
            ServicePointManager.Expect100Continue = false;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        }

        private static string GetNextUserAgent()
        {
            int index = Interlocked.Increment(ref _userAgentIndex) & int.MaxValue;
            return _userAgents[index % _userAgents.Length];
        }

        /// <summary>
        /// Hàm xử lý chính: Check Cache → Mask → Translate → Unmask
        /// </summary>
        public static async Task<string> ProcessAsync(string input, string sl, string tl, SemaphoreSlim semaphore)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;

            string cacheKey = string.Concat(sl, "\u001F", tl, "\u001F", input);
            if (_cache.TryGetValue(cacheKey, out string cachedResult)) return cachedResult;

            var pending = _inFlight.GetOrAdd(
                cacheKey,
                _ => new Lazy<Task<string>>(
                    () => ProcessUncachedAsync(input, sl, tl, semaphore, cacheKey),
                    LazyThreadSafetyMode.ExecutionAndPublication));

            try
            {
                return await pending.Value.ConfigureAwait(false);
            }
            finally
            {
                var entry = new KeyValuePair<string, Lazy<Task<string>>>(cacheKey, pending);
                ((ICollection<KeyValuePair<string, Lazy<Task<string>>>>)_inFlight).Remove(entry);
            }
        }

        private static async Task<string> ProcessUncachedAsync(
            string input, string sl, string tl, SemaphoreSlim semaphore, string cacheKey)
        {
            var maskResult = FormatProtector.MaskText(input);

            if (FormatProtector.IsAllTags(maskResult.MaskedText))
            {
                CacheResult(cacheKey, input);
                return input;
            }

            string aecDirectResult = AecGlossary.Lookup(maskResult.MaskedText, sl, tl);
            if (!string.IsNullOrEmpty(aecDirectResult))
            {
                string dictText = FormatProtector.UnmaskText(aecDirectResult, maskResult.Codes);
                CacheResult(cacheKey, dictText);
                return dictText;
            }

            string translatedRaw = await TranslateApiAsync(maskResult.MaskedText, sl, tl, semaphore);
            string finalText = FormatProtector.UnmaskText(translatedRaw, maskResult.Codes);
            CacheResult(cacheKey, finalText);
            return finalText;
        }

        private static void CacheResult(string key, string value)
        {
            if (!_cache.TryAdd(key, value))
            {
                _cache[key] = value;
                return;
            }

            _cacheOrder.Enqueue(key);
            while (_cache.Count > MaxCacheEntries && _cacheOrder.TryDequeue(out string oldestKey))
            {
                _cache.TryRemove(oldestKey, out string ignored);
            }
        }

        private static async Task<string> TranslateApiAsync(string text, string sl, string tl, SemaphoreSlim semaphore)
        {
            // Sử dụng Semaphore để giới hạn số lượng request gửi đi cùng lúc (tránh lỗi 429)
            await semaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                int retryDelay = 1000;
                int maxRetries = 3;
                string apiKey = AppSettings.LoadApiKey();

                for (int i = 0; i < maxRetries; i++)
                {
                    try
                    {
                        string jsonResponse;
                        if (!string.IsNullOrEmpty(apiKey))
                        {
                            // Dùng Google Cloud Translation API (cần API Key)
                            jsonResponse = await CallCloudTranslationApiAsync(text, sl, tl, apiKey).ConfigureAwait(false);
                        }
                        else
                        {
                            // Dùng Google Translate free scraping (fallback)
                            jsonResponse = await CallFreeTranslateApiAsync(text, sl, tl).ConfigureAwait(false);
                        }

                        if (!string.IsNullOrEmpty(jsonResponse))
                            return jsonResponse;

                        break;
                    }
                    catch (HttpRequestException ex) when (ex.Message.Contains("429") || ex.Message.Contains("TooManyRequests"))
                    {
                        if (i < maxRetries - 1)
                            await Task.Delay(retryDelay * (i + 1)).ConfigureAwait(false);
                    }
                    catch
                    {
                        if (i < maxRetries - 1)
                            await Task.Delay(retryDelay).ConfigureAwait(false);
                    }
                }
                return text;
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <summary>
        /// Gọi Google Cloud Translation API với API Key (ổn định, không bị chặn).
        /// </summary>
        private static async Task<string> CallCloudTranslationApiAsync(string text, string sl, string tl, string apiKey)
        {
            string url = $"https://translation.googleapis.com/language/translate/v2?key={apiKey}";

            var payload = new
            {
                q = text,
                source = sl == "auto" ? null : sl,
                target = tl,
                format = "text"
            };

            string jsonPayload = JsonSerializer.Serialize(payload);
            using (var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json"))
            using (var response = await _httpClient.PostAsync(url, content).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return ParseCloudTranslationJson(json, text);
            }
        }

        /// <summary>
        /// Parse response từ Google Cloud Translation API.
        /// </summary>
        private static string ParseCloudTranslationJson(string json, string original)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    var data = doc.RootElement.GetProperty("data");
                    var translations = data.GetProperty("translations");
                    if (translations.GetArrayLength() > 0)
                    {
                        string translatedText = translations[0].GetProperty("translatedText").GetString();
                        return System.Net.WebUtility.HtmlDecode(translatedText);
                    }
                }
            }
            catch
            {
                // Fallback
            }
            return original;
        }

        /// <summary>
        /// Gọi Google Translate free API (scraping) - không cần API Key.
        /// </summary>
        private static async Task<string> CallFreeTranslateApiAsync(string text, string sl, string tl)
        {
            string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={sl}&tl={tl}&dt=t&q={System.Web.HttpUtility.UrlEncode(text)}";

            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.Add("User-Agent", GetNextUserAgent());
                using (var response = await _httpClient.SendAsync(request).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return ParseFreeTranslateJson(json, text);
                }
            }
        }

        /// <summary>
        /// Parse response từ Google Translate free API (dùng System.Text.Json thay vì parser thủ công).
        /// </summary>
        private static string ParseFreeTranslateJson(string json, string original)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    var sb = new StringBuilder();
                    var root = doc.RootElement;

                    if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                    {
                        var sentences = root[0];
                        if (sentences.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in sentences.EnumerateArray())
                            {
                                if (item.ValueKind == JsonValueKind.Array && item.GetArrayLength() > 0)
                                {
                                    var seg = item[0];
                                    if (seg.ValueKind == JsonValueKind.String)
                                    {
                                        sb.Append(seg.GetString());
                                    }
                                }
                            }
                        }
                    }

                    string result = sb.ToString();
                    return string.IsNullOrWhiteSpace(result) ? original : result;
                }
            }
            catch
            {
                return original;
            }
        }
    }
}

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using HoangTam.AutoCAD.Tools.Core;

namespace HoangTam.AutoCAD.Tools.Network
{
    public static class GoogleTranslator
    {
        private static readonly Random _rnd = new Random();
        private static readonly string[] _userAgents = new string[]
        {
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/121.0"
        };

        static GoogleTranslator()
        {
            ServicePointManager.DefaultConnectionLimit = 50;
            ServicePointManager.Expect100Continue = false;
        }

        public static string GetRandomUserAgent() => _userAgents[_rnd.Next(_userAgents.Length)];

        public static async Task<string> TranslateAsync(HttpClient client, string input, string sl, string tl)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;

            // --- SỬA ĐỔI: Luôn gọi Dictionary xử lý trước ---
            // AecGlobalDictionary giờ đã đủ thông minh để xử lý "auto"
            string textToProcess = AecGlobalDictionary.ApplyTerminology(input, sl, tl);
            // ------------------------------------------------

            // MASKING
            var maskResult = FormatProtector.MaskText(textToProcess);
            if (FormatProtector.IsAllTags(maskResult.MaskedText)) return input;

            string textToTranslate = maskResult.MaskedText;
            int retryDelay = 2000;

            for (int i = 0; i < 5; i++)
            {
                try
                {
                    string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={sl}&tl={tl}&dt=t&q={System.Web.HttpUtility.UrlEncode(textToTranslate)}";
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Add("User-Agent", GetRandomUserAgent());

                    var response = await client.SendAsync(request).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        string translatedRaw = ParseResultStrict(json, textToTranslate);

                        // 3. UNMASKING (Khôi phục mã AutoCAD)
                        return FormatProtector.UnmaskText(translatedRaw, maskResult.Codes);
                    }

                    if ((int)response.StatusCode == 429)
                    {
                        await Task.Delay(retryDelay).ConfigureAwait(false);
                        retryDelay *= 2;
                    }
                    else break;
                }
                catch { await Task.Delay(retryDelay).ConfigureAwait(false); }
            }
            return input;
        }

        private static string ParseResultStrict(string json, string original)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                int depth = 0; bool inString = false;
                bool isTransSegment = false;
                for (int i = 0; i < json.Length; i++)
                {
                    char c = json[i];
                    if (inString)
                    {
                        if (c == '\\' && i + 1 < json.Length) { i++; continue; }
                        if (c == '"')
                        {
                            inString = false; if (depth == 3 && isTransSegment)
                            {
                                int end = i, start = i - 1;
                                while (start >= 0) { if (json[start] == '"' && (start == 0 || json[start - 1] != '\\')) break; start--; }
                                if (start >= 0) { sb.Append(Regex.Unescape(json.Substring(start + 1, end - start - 1))); isTransSegment = false; }
                            }
                        }
                        continue;
                    }
                    if (c == '[') { depth++; if (depth == 3) isTransSegment = true; }
                    else if (c == ']') { if (depth == 2) return sb.ToString(); depth--; }
                    else if (c == '"') inString = true;
                }
                return string.IsNullOrWhiteSpace(sb.ToString()) ? original : sb.ToString();
            }
            catch { return original; }
        }
    }
}
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using HoangTam.AutoCAD.Tools.Core;
using HoangTam.AutoCAD.Tools.Models;

namespace HoangTam.AutoCAD.Tools.Services
{
    public static class TranslationService
    {
        private static readonly HttpClient _httpClient;
        private static readonly Random _rnd = new Random();

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

        private static string GetRandomUserAgent() => _userAgents[_rnd.Next(_userAgents.Length)];

        /// <summary>
        /// Hàm xử lý chính: Mask -> Translate -> Unmask
        /// Tuyệt đối KHÔNG gọi Dictionary ở bước đầu tiên.
        /// </summary>
        public static async Task<string> ProcessAsync(string input, string sl, string tl, SemaphoreSlim semaphore)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;

            // BƯỚC 1: Masking (Bảo vệ mã AutoCAD)
            // Chỉ bảo vệ mã, giữ nguyên nội dung text để Google hiểu ngữ cảnh
            var maskResult = FormatProtector.MaskText(input);

            // Nếu chỉ toàn mã (ví dụ "\P"), trả về luôn
            if (FormatProtector.IsAllTags(maskResult.MaskedText)) return input;

            // BƯỚC 2: Gọi Google Translate API
            // Lúc này text vẫn là "MẶT BẰNG CẤP ĐIỆN..." (kèm các tag [ID:x])
            // Google sẽ dịch cả cụm này thành "POWER OUTLET LAYOUT..." (hoặc tương tự)
            string translatedRaw = await TranslateApiAsync(maskResult.MaskedText, sl, tl, semaphore);

            // BƯỚC 3: Unmasking (Khôi phục mã)
            string finalText = FormatProtector.UnmaskText(translatedRaw, maskResult.Codes);

            return finalText;
        }

        private static async Task<string> TranslateApiAsync(string text, string sl, string tl, SemaphoreSlim semaphore)
        {
            // Sử dụng Semaphore để giới hạn số lượng request gửi đi cùng lúc (tránh lỗi 429)
            await semaphore.WaitAsync();
            try
            {
                int retryDelay = 1000;
                int maxRetries = 3; // Thử lại tối đa 3 lần

                for (int i = 0; i < maxRetries; i++)
                {
                    try
                    {
                        // client=gtx là endpoint public ổn định nhất của Google Translate
                        string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={sl}&tl={tl}&dt=t&q={System.Web.HttpUtility.UrlEncode(text)}";

                        var request = new HttpRequestMessage(HttpMethod.Get, url);
                        request.Headers.Add("User-Agent", GetRandomUserAgent());

                        // ConfigureAwait(false) giúp tránh Deadlock UI thread của AutoCAD
                        var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

                        if (response.IsSuccessStatusCode)
                        {
                            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            return ParseResultStrict(json, text);
                        }

                        // Nếu bị chặn (Too Many Requests), chờ tăng dần thời gian rồi thử lại
                        if ((int)response.StatusCode == 429)
                        {
                            await Task.Delay(retryDelay * (i + 1)).ConfigureAwait(false);
                        }
                        else
                        {
                            // Các lỗi khác (404, 500...) thì dừng luôn
                            break;
                        }
                    }
                    catch
                    {
                        // Lỗi mạng, chờ rồi thử lại
                        await Task.Delay(retryDelay).ConfigureAwait(false);
                    }
                }
                // Nếu thất bại hết số lần thử, trả về text đã mask (không dịch được nhưng không crash)
                return text;
            }
            finally
            {
                // Luôn giải phóng Semaphore
                semaphore.Release();
            }
        }

        // Hàm phân tích JSON thủ công để lấy kết quả dịch
        private static string ParseResultStrict(string json, string original)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                int depth = 0;
                bool inString = false;
                bool isTransSegment = false;

                for (int i = 0; i < json.Length; i++)
                {
                    char c = json[i];
                    if (inString)
                    {
                        if (c == '\\' && i + 1 < json.Length) { i++; continue; }
                        if (c == '"')
                        {
                            inString = false;
                            if (depth == 3 && isTransSegment)
                            {
                                int end = i, start = i - 1;
                                while (start >= 0) { if (json[start] == '"' && (start == 0 || json[start - 1] != '\\')) break; start--; }
                                if (start >= 0)
                                {
                                    string seg = json.Substring(start + 1, end - start - 1);
                                    sb.Append(Regex.Unescape(seg));
                                    isTransSegment = false;
                                }
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
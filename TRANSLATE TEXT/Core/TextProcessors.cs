using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HoangTam.AutoCAD.Tools.Models;

namespace HoangTam.AutoCAD.Tools.Core
{
    public enum EncodingType { Auto = 0, Unicode = 1, VNI = 2, TCVN3 = 3 }

    public static class FormatProtector
    {
        // Regex bảo vệ các mã đặc biệt của AutoCAD
        private static readonly Regex _regexCodes = new Regex(
            @"(\\U\+[0-9a-fA-F]{4})|" +            // Unicode Escapes
            @"(\\[ACFHQTWacfhqtw][^;]*;)|" +       // MText Formatting
            @"(\\S[^;]*;)|" +                      // Stacking
            @"(%%[cdpCDPuoUO])|" +                 // Standard Codes
            @"(%%[0-9]{3})|" +                     // ASCII codes
            @"(\\P)|" +                            // Newline
            @"(\\[LloOkK])|" +                     // Underline/Strike
            @"(\\[\\{}])|" +                       // Escape chars
            @"({|})",                              // Braces
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static MaskResult MaskText(string input)
        {
            var result = new MaskResult();
            if (string.IsNullOrEmpty(input)) { result.MaskedText = input; return result; }

            int index = 0;
            result.MaskedText = _regexCodes.Replace(input, m =>
            {
                result.Codes.Add(m.Value);
                return $" [ID:{index++}] ";
            });
            return result;
        }

        public static string UnmaskText(string translated, List<string> codes)
        {
            if (string.IsNullOrEmpty(translated) || codes == null || codes.Count == 0) return translated?.Trim();

            // Khôi phục mã từ placeholder
            for (int i = 0; i < codes.Count; i++)
            {
                translated = Regex.Replace(translated, $@"\[\s*ID\s*:\s*{i}\s*\]", match => codes[i], RegexOptions.IgnoreCase);
            }

            // Cleanup whitespace do dịch thuật sinh ra
            translated = Regex.Replace(translated, @"\s*(\\P)\s*", @"\P", RegexOptions.None);
            translated = Regex.Replace(translated, @"(\\[A-Za-z0-9]+[^;]*;)\s+", @"$1", RegexOptions.IgnoreCase);
            translated = Regex.Replace(translated, @"(\\[LloOkK])\s+", @"$1", RegexOptions.IgnoreCase);
            translated = Regex.Replace(translated, @"(%%[cdpCDPuoUO])\s+", @"$1", RegexOptions.IgnoreCase);

            string[] lines = Regex.Split(translated, @"\\P", RegexOptions.None);
            for (int j = 0; j < lines.Length; j++) lines[j] = lines[j].Trim();
            return string.Join("\\P", lines);
        }

        public static bool IsAllTags(string text)
        {
            string clean = Regex.Replace(text, @"\[ID:\d+\]", "");
            return string.IsNullOrWhiteSpace(clean) || Regex.IsMatch(clean, @"^[\d\W]+$");
        }
    }

    public static class VnCharset
    {
        private static readonly Dictionary<string, string> VniToUni = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> UniToVni = new Dictionary<string, string>();
        private static readonly Dictionary<char, char> TcvnToUni = new Dictionary<char, char>();
        private static readonly Dictionary<char, char> UniToTcvn = new Dictionary<char, char>();
        private static readonly HashSet<char> UnicodeVietnameseChars = new HashSet<char>();

        static VnCharset() { InitializeMaps(); }

        private static void InitializeMaps()
        {
            // Init Unicode Chars
            string[] uni = {
                "á", "à", "ả", "ã", "ạ", "ă", "ắ", "ằ", "ẳ", "ẵ", "ặ", "â", "ấ", "ầ", "ẩ", "ẫ", "ậ",
                "é", "è", "ẻ", "ẽ", "ẹ", "ê", "ế", "ề", "ể", "ễ", "ệ",
                "í", "ì", "ỉ", "ĩ", "ị",
                "ó", "ò", "ỏ", "õ", "ọ", "ô", "ố", "ồ", "ổ", "ỗ", "ộ", "ơ", "ớ", "ờ", "ở", "ỡ", "ợ",
                "ú", "ù", "ủ", "ũ", "ụ", "ư", "ứ", "ừ", "ử", "ữ", "ự",
                "ý", "ỳ", "ỷ", "ỹ", "ỵ", "đ",
                "Á", "À", "Ả", "Ã", "Ạ", "Ă", "Ắ", "Ằ", "Ẳ", "Ẵ", "Ặ", "Â", "Ấ", "Ầ", "Ẩ", "Ẫ", "Ậ",
                "É", "È", "Ẻ", "Ẽ", "Ẹ", "Ê", "Ế", "Ề", "Ể", "Ễ", "Ệ",
                "Í", "Ì", "Ỉ", "Ĩ", "Ị",
                "Ó", "Ò", "Ỏ", "Õ", "Ọ", "Ô", "Ố", "Ồ", "Ổ", "Ỗ", "Ộ", "Ơ", "Ớ", "Ờ", "Ở", "Ỡ", "Ợ",
                "Ú", "Ù", "Ủ", "Ũ", "Ụ", "Ư", "Ứ", "Ừ", "Ử", "Ữ", "Ự",
                "Ý", "Ỳ", "Ỷ", "Ỹ", "Ỵ", "Đ"
            };

            // Init VNI Hex
            string[] vni = {
                "a\u00D9", "a\u00D8", "a\u00DB", "a\u00D5", "a\u00CF",
                "\u00E6", "\u00E6\u00D9", "\u00E6\u00D8", "\u00E6\u00DB", "\u00E6\u00D5", "\u00E6\u00CF",
                "\u00E2", "\u00E2\u00D9", "\u00E2\u00D8", "\u00E2\u00DB", "\u00E2\u00D5", "\u00E2\u00CF",
                "e\u00D9", "e\u00D8", "e\u00DB", "e\u00D5", "e\u00CF",
                "\u00EA", "\u00EA\u00D9", "\u00EA\u00D8", "\u00EA\u00DB", "\u00EA\u00D5", "\u00EA\u00CF",
                "i\u00D9", "i\u00D8", "i\u00DB", "i\u00D5", "i\u00CF",
                "o\u00D9", "o\u00D8", "o\u00DB", "o\u00D5", "o\u00CF",
                "\u00F4", "\u00F4\u00D9", "\u00F4\u00D8", "\u00F4\u00DB", "\u00F4\u00D5", "\u00F4\u00CF",
                "\u00F6", "\u00F6\u00D9", "\u00F6\u00D8", "\u00F6\u00DB", "\u00F6\u00D5", "\u00F6\u00CF",
                "u\u00D9", "u\u00D8", "u\u00DB", "u\u00D5", "u\u00CF",
                "\u00F9", "\u00F9\u00D9", "\u00F9\u00D8", "\u00F9\u00DB", "\u00F9\u00D5", "\u00F9\u00CF",
                "y\u00D9", "y\u00D8", "y\u00DB", "y\u00D5", "\u00EE", "d\u00F1",
                "A\u00D9", "A\u00D8", "A\u00DB", "A\u00D5", "A\u00CF",
                "\u00A1", "\u00A1\u00D9", "\u00A1\u00D8", "\u00A1\u00DB", "\u00A1\u00D5", "\u00A1\u00CF",
                "\u00A2", "\u00A2\u00D9", "\u00A2\u00D8", "\u00A2\u00DB", "\u00A2\u00D5", "\u00A2\u00CF",
                "E\u00D9", "E\u00D8", "E\u00DB", "E\u00D5", "E\u00CF",
                "\u00A3", "\u00A3\u00D9", "\u00A3\u00D8", "\u00A3\u00DB", "\u00A3\u00D5", "\u00A3\u00CF",
                "I\u00D9", "I\u00D8", "I\u00DB", "I\u00D5", "I\u00CF",
                "O\u00D9", "O\u00D8", "O\u00DB", "O\u00D5", "O\u00CF",
                "\u00A4", "\u00A4\u00D9", "\u00A4\u00D8", "\u00A4\u00DB", "\u00A4\u00D5", "\u00A4\u00CF",
                "\u00A5", "\u00A5\u00D9", "\u00A5\u00D8", "\u00A5\u00DB", "\u00A5\u00D5", "\u00A5\u00CF",
                "U\u00D9", "U\u00D8", "U\u00DB", "U\u00D5", "U\u00CF",
                "\u00A6", "\u00A6\u00D9", "\u00A6\u00D8", "\u00A6\u00DB", "\u00A6\u00D5", "\u00A6\u00CF",
                "Y\u00D9", "Y\u00D8", "Y\u00DB", "Y\u00D5", "\u00A7", "D\u00D1"
            };

            // Init TCVN3 (ABC) Full
            char[] tcvn = {
                '\u00B8', '\u00B5', '\u00B6', '\u00B7', '\u00B9',
                '\u00A8', '\u00BE', '\u00BB', '\u00BC', '\u00BD', '\u00C6',
                '\u00A9', '\u00CA', '\u00C7', '\u00C8', '\u00C9', '\u00CB',
                '\u00D0', '\u00CC', '\u00CE', '\u00CF', '\u00D1',
                '\u00AA', '\u00D5', '\u00D2', '\u00D3', '\u00D4', '\u00D6',
                '\u00DD', '\u00D7', '\u00D8', '\u00DC', '\u00DE',
                '\u00E3', '\u00DF', '\u00E1', '\u00E2', '\u00E4',
                '\u00AB', '\u00E8', '\u00E5', '\u00E6', '\u00E7', '\u00E9',
                '\u00AC', '\u00ED', '\u00EA', '\u00EB', '\u00EC', '\u00EE',
                '\u00F3', '\u00EF', '\u00F1', '\u00F2', '\u00F4',
                '\u00AD', '\u00F8', '\u00F5', '\u00F6', '\u00F7', '\u00F9',
                '\u00FD', '\u00FA', '\u00FB', '\u00FC', '\u00FE', '\u00AE',
                '\u00B8', '\u00B5', '\u00B6', '\u00B7', '\u00B9',
                '\u00A1', '\u00BE', '\u00BB', '\u00BC', '\u00BD', '\u00C6',
                '\u00A2', '\u00CA', '\u00C7', '\u00C8', '\u00C9', '\u00CB',
                '\u00D0', '\u00CC', '\u00CE', '\u00CF', '\u00D1',
                '\u00A3', '\u00D5', '\u00D2', '\u00D3', '\u00D4', '\u00D6',
                '\u00DD', '\u00D7', '\u00D8', '\u00DC', '\u00DE',
                '\u00E3', '\u00DF', '\u00E1', '\u00E2', '\u00E4',
                '\u00A4', '\u00E8', '\u00E5', '\u00E6', '\u00E7', '\u00E9',
                '\u00A5', '\u00ED', '\u00EA', '\u00EB', '\u00EC', '\u00EE',
                '\u00F3', '\u00EF', '\u00F1', '\u00F2', '\u00F4',
                '\u00A6', '\u00F8', '\u00F5', '\u00F6', '\u00F7', '\u00F9',
                '\u00FD', '\u00FA', '\u00FB', '\u00FC', '\u00FE', '\u00A7'
            };

            // Mapping Logic
            for (int i = 0; i < uni.Length && i < vni.Length; i++)
            {
                if (!VniToUni.ContainsKey(vni[i])) VniToUni.Add(vni[i], uni[i]);
                if (!UniToVni.ContainsKey(uni[i])) UniToVni.Add(uni[i], vni[i]);
                foreach (char c in uni[i]) UnicodeVietnameseChars.Add(c);
            }

            for (int i = 0; i < uni.Length && i < tcvn.Length; i++)
            {
                char u = uni[i][0], t = tcvn[i];
                if (!TcvnToUni.ContainsKey(t)) TcvnToUni.Add(t, u);
                if (!UniToTcvn.ContainsKey(u)) UniToTcvn.Add(u, t);
                UnicodeVietnameseChars.Add(u);
            }
        }

        public static string Convert(string input, EncodingType source, EncodingType target)
        {
            if (string.IsNullOrEmpty(input) || source == target) return input;
            string unicodeTemp = input;

            if (source == EncodingType.Auto) source = DetectEncoding(input);

            if (source == EncodingType.TCVN3) unicodeTemp = TcvnToUnicode(input);
            else if (source == EncodingType.VNI) unicodeTemp = VniToUnicode(input);

            if (target == EncodingType.Unicode) return unicodeTemp;
            if (target == EncodingType.TCVN3) return UnicodeToTcvn(unicodeTemp);
            if (target == EncodingType.VNI) return UnicodeToVni(unicodeTemp);

            return unicodeTemp;
        }

        private static string VniToUnicode(string input)
        {
            var keys = VniToUni.Keys.OrderByDescending(k => k.Length);
            StringBuilder sb = new StringBuilder(input);
            foreach (var k in keys) sb.Replace(k, VniToUni[k]);
            return sb.ToString();
        }

        private static string TcvnToUnicode(string input)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in input) sb.Append(TcvnToUni.ContainsKey(c) ? TcvnToUni[c] : c);
            return sb.ToString();
        }

        private static string UnicodeToVni(string input)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in input) sb.Append(UniToVni.ContainsKey(c.ToString()) ? UniToVni[c.ToString()] : c.ToString());
            return sb.ToString();
        }

        private static string UnicodeToTcvn(string input)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in input) sb.Append(UniToTcvn.ContainsKey(c) ? UniToTcvn[c] : c);
            return sb.ToString();
        }

        public static EncodingType DetectEncoding(string text)
        {
            if (string.IsNullOrEmpty(text)) return EncodingType.Unicode;
            string clean = Regex.Replace(text, @"\\[ACFHQTWacfhqtw][^;]*;", "");
            clean = Regex.Replace(clean, @"\\P", "");
            clean = Regex.Replace(clean, @"[{}]", "");
            clean = Regex.Replace(clean, @"\\[LloOkK]", "");

            int sTCVN = 0, sVNI = 0, sUni = 0;
            foreach (char c in clean) if (TcvnToUni.ContainsKey(c)) sTCVN++;
            foreach (string k in VniToUni.Keys)
            {
                if (k.Length < 2) continue;
                sVNI += (clean.Length - clean.Replace(k, "").Length) / k.Length;
            }
            foreach (char c in clean) if (UnicodeVietnameseChars.Contains(c)) sUni++;

            if (sVNI > sTCVN && sVNI >= sUni) return EncodingType.VNI;
            if (sTCVN > sVNI && sTCVN > sUni) return EncodingType.TCVN3;
            return EncodingType.Unicode;
        }
    }
}
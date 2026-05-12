using System.Collections.Generic;
using TranslateText.Models;

namespace TranslateText.Models
{
    /// <summary>
    /// Danh sách ngôn ngữ hỗ trợ bởi Google Translate API
    /// </summary>
    public static class LanguageList
    {
        public static List<LanguageItem> GetSupportedLanguages()
        {
            return new List<LanguageItem>
            {
                new LanguageItem { Code = "auto", Name = "Auto Detect" },
                new LanguageItem { Code = "vi", Name = "Vietnamese (Tiếng Việt)" },
                new LanguageItem { Code = "en", Name = "English" },
                new LanguageItem { Code = "ko", Name = "Korean (Hàn Quốc)" },
                new LanguageItem { Code = "ja", Name = "Japanese (Nhật Bản)" },
                new LanguageItem { Code = "zh-CN", Name = "Chinese Simplified (Trung Giản thể)" },
                new LanguageItem { Code = "zh-TW", Name = "Chinese Traditional (Trung Phồn thể)" },
                new LanguageItem { Code = "fr", Name = "French (Pháp)" },
                new LanguageItem { Code = "de", Name = "German (Đức)" },
                new LanguageItem { Code = "ru", Name = "Russian (Nga)" },
                new LanguageItem { Code = "es", Name = "Spanish (Tây Ban Nha)" },
                new LanguageItem { Code = "th", Name = "Thai (Thái Lan)" },
                new LanguageItem { Code = "lo", Name = "Lao (Lào)" },
                new LanguageItem { Code = "km", Name = "Khmer (Campuchia)" },
                new LanguageItem { Code = "id", Name = "Indonesian (Indonesia)" },
                new LanguageItem { Code = "ms", Name = "Malay (Malaysia)" },
                new LanguageItem { Code = "it", Name = "Italian (Ý)" },
                new LanguageItem { Code = "pt", Name = "Portuguese (Bồ Đào Nha)" },
                new LanguageItem { Code = "hi", Name = "Hindi (Ấn Độ)" },
                new LanguageItem { Code = "ar", Name = "Arabic (Ả Rập)" }
            };
        }
    }
}

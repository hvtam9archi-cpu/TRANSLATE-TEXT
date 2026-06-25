namespace TranslateText.Services
{
    /// <summary>
    /// Lưu/Đọc cài đặt người dùng vào Registry (Windows).
    /// Dùng cho lệnh CHANGETEXTSTYLE để nhớ lựa chọn lần cuối.
    /// Cũng lưu Google Cloud API Key để dùng Translation API chính thức.
    /// </summary>
    public static class AppSettings
    {
        private const string REG_PATH = @"Software\HoangTamAutoCADTools\TranslateText";

        public static void Save(string style, int targetEncodingIndex, int sourceEncodingIndex)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(REG_PATH))
                {
                    key.SetValue("TargetStyle", style);
                    key.SetValue("TargetEncodingIndex", targetEncodingIndex);
                    key.SetValue("SourceEncodingIndex", sourceEncodingIndex);
                }
            }
            catch { /* Registry write failed — non-critical, skip silently */ }
        }

        public static void Load(out string style, out int targetEncodingIndex, out int sourceEncodingIndex)
        {
            style = "";
            targetEncodingIndex = 0;
            sourceEncodingIndex = 0;
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(REG_PATH))
                {
                    if (key != null)
                    {
                        style = key.GetValue("TargetStyle", "").ToString();
                        targetEncodingIndex = System.Convert.ToInt32(key.GetValue("TargetEncodingIndex", 0));
                        sourceEncodingIndex = System.Convert.ToInt32(key.GetValue("SourceEncodingIndex", 0));
                    }
                }
            }
            catch { /* Registry read failed — non-critical, use defaults */ }
        }

        /// <summary>
        /// Lưu Google Cloud Translation API Key vào Registry.
        /// Nếu key rỗng, xóa giá trị cũ.
        /// </summary>
        public static void SaveApiKey(string apiKey)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(REG_PATH))
                {
                    if (string.IsNullOrEmpty(apiKey))
                        key.DeleteValue("GoogleApiKey", false);
                    else
                        key.SetValue("GoogleApiKey", apiKey);
                }
            }
            catch { /* Non-critical */ }
        }

        /// <summary>
        /// Đọc Google Cloud Translation API Key từ Registry.
        /// Trả về null/empty nếu chưa có.
        /// </summary>
        public static string LoadApiKey()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(REG_PATH))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("GoogleApiKey");
                        if (val != null) return val.ToString();
                    }
                }
            }
            catch { /* Non-critical */ }
            return null;
        }
    }
}

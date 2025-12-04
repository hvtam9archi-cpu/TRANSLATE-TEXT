using Microsoft.Win32;

namespace HoangTam.AutoCAD.Tools.Core
{
    public static class AppSettings
    {
        private const string REG_PATH = @"Software\HoangTamAutoCADTools";

        public static void SaveStyleSettings(string style, int tEncIdx, int sEncIdx)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(REG_PATH))
                {
                    if (key != null)
                    {
                        key.SetValue("TargetStyle", style ?? "");
                        key.SetValue("TargetEncodingIndex", tEncIdx);
                        key.SetValue("SourceEncodingIndex", sEncIdx);
                    }
                }
            }
            catch { /* Silent fail to avoid crash */ }
        }

        public static void LoadStyleSettings(out string style, out int tEncIdx, out int sEncIdx)
        {
            style = "";
            tEncIdx = 0;
            sEncIdx = 0;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(REG_PATH))
                {
                    if (key != null)
                    {
                        style = key.GetValue("TargetStyle", "").ToString();
                        tEncIdx = System.Convert.ToInt32(key.GetValue("TargetEncodingIndex", 0));
                        sEncIdx = System.Convert.ToInt32(key.GetValue("SourceEncodingIndex", 0));
                    }
                }
            }
            catch { /* Silent fail */ }
        }
    }
}
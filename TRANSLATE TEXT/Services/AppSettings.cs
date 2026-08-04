using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace TranslateText.Services
{
    /// <summary>
    /// Stores per-user plugin settings in the Windows registry.
    /// Registry failures are non-fatal but are recorded in TRACE output.
    /// </summary>
    public static class AppSettings
    {
        private const string RegistryPath =
            @"Software\HoangTamAutoCADTools\TranslateText";

        public static void Save(string style, int targetEncodingIndex, int sourceEncodingIndex)
        {
            try
            {
                using (RegistryKey key = OpenWritableKey())
                {
                    key.SetValue("TargetStyle", style ?? string.Empty);
                    key.SetValue("TargetEncodingIndex", targetEncodingIndex);
                    key.SetValue("SourceEncodingIndex", sourceEncodingIndex);
                }
            }
            catch (Exception exception) when (IsRegistryFailure(exception))
            {
                LogRegistryFailure("save text settings", exception);
            }
        }

        public static void Load(
            out string style,
            out int targetEncodingIndex,
            out int sourceEncodingIndex)
        {
            style = string.Empty;
            targetEncodingIndex = 0;
            sourceEncodingIndex = 0;

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    if (key == null) return;

                    style = Convert.ToString(key.GetValue("TargetStyle", string.Empty));
                    targetEncodingIndex = Convert.ToInt32(
                        key.GetValue("TargetEncodingIndex", 0));
                    sourceEncodingIndex = Convert.ToInt32(
                        key.GetValue("SourceEncodingIndex", 0));
                }
            }
            catch (Exception exception) when (IsRegistryFailure(exception))
            {
                style = string.Empty;
                targetEncodingIndex = 0;
                sourceEncodingIndex = 0;
                LogRegistryFailure("load text settings", exception);
            }
        }

        public static void SaveApiKey(string apiKey)
        {
            try
            {
                using (RegistryKey key = OpenWritableKey())
                {
                    if (string.IsNullOrEmpty(apiKey))
                        key.DeleteValue("GoogleApiKey", false);
                    else
                        key.SetValue("GoogleApiKey", apiKey);
                }
            }
            catch (Exception exception) when (IsRegistryFailure(exception))
            {
                LogRegistryFailure("save Google API key", exception);
            }
        }

        public static string LoadApiKey()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                    return Convert.ToString(key?.GetValue("GoogleApiKey"));
            }
            catch (Exception exception) when (IsRegistryFailure(exception))
            {
                LogRegistryFailure("load Google API key", exception);
                return null;
            }
        }

        private static RegistryKey OpenWritableKey()
        {
            RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            if (key == null)
                throw new InvalidOperationException("Could not open the plugin registry key.");
            return key;
        }

        private static bool IsRegistryFailure(Exception exception)
        {
            return exception is UnauthorizedAccessException ||
                exception is SecurityException ||
                exception is IOException ||
                exception is ObjectDisposedException ||
                exception is InvalidOperationException ||
                exception is FormatException ||
                exception is InvalidCastException ||
                exception is OverflowException;
        }

        private static void LogRegistryFailure(string operation, Exception exception)
        {
            Trace.WriteLine(
                $"[TranslateText] Could not {operation}: {exception.Message}");
        }
    }
}

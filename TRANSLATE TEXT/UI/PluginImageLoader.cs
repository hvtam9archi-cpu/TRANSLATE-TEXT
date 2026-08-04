using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TranslateText.UI
{
    /// <summary>
    /// Loads WPF images embedded in the plugin assembly through a Pack URI.
    /// </summary>
    internal static class PluginImageLoader
    {
        public static bool TryLoad(string resourcePath, out ImageSource image)
        {
            image = null;
            if (string.IsNullOrWhiteSpace(resourcePath)) return false;

            try
            {
                string assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
                string normalizedPath = resourcePath.Replace('\\', '/').TrimStart('/');
                var uri = new Uri(
                    $"pack://application:,,,/{assemblyName};component/{normalizedPath}",
                    UriKind.Absolute);
                image = BitmapFrame.Create(
                    uri,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                return true;
            }
            catch (Exception exception)
            {
                Trace.TraceWarning(
                    $"[TranslateText] Could not load image resource '{resourcePath}': {exception}");
                return false;
            }
        }
    }
}

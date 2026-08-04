using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;

namespace TranslateText.Services
{
    /// <summary>
    /// Indexes font files deployed next to the plugin. File-name lookup is immediate;
    /// TrueType metadata is read lazily only when a missing typeface must be resolved.
    /// </summary>
    internal sealed class EmbeddedFontCatalog
    {
        private static readonly string[] SupportedExtensions = { ".shx", ".ttf", ".otf", ".ttc" };

        private readonly List<string> _fontFiles;
        private readonly Dictionary<string, string> _filesByName;
        private readonly Lazy<Dictionary<string, string>> _filesByTypeface;

        public EmbeddedFontCatalog(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException("Font root is required.", nameof(rootDirectory));

            RootDirectory = Path.GetFullPath(rootDirectory);
            _fontFiles = new List<string>();
            _filesByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (Directory.Exists(RootDirectory))
            {
                var files = new List<string>(Directory.EnumerateFiles(
                    RootDirectory,
                    "*",
                    SearchOption.AllDirectories));
                files.Sort(StringComparer.OrdinalIgnoreCase);

                foreach (string file in files)
                {
                    if (!IsSupported(file)) continue;

                    string fullPath = Path.GetFullPath(file);
                    _fontFiles.Add(fullPath);
                    AddIfMissing(_filesByName, Path.GetFileName(fullPath), fullPath);
                }
            }

            _filesByTypeface = new Lazy<Dictionary<string, string>>(
                BuildTypefaceIndex,
                true);
        }

        public string RootDirectory { get; }
        public int Count => _fontFiles.Count;

        public bool TryFindFile(string requestedFont, out string fullPath)
        {
            fullPath = null;
            if (string.IsNullOrWhiteSpace(requestedFont)) return false;

            try
            {
                string fileName = Path.GetFileName(requestedFont.Trim().Trim('"'));
                if (_filesByName.TryGetValue(fileName, out fullPath)) return true;

                if (Path.HasExtension(fileName)) return false;

                foreach (string extension in SupportedExtensions)
                {
                    if (_filesByName.TryGetValue(fileName + extension, out fullPath)) return true;
                }
                return false;
            }
            catch (ArgumentException exception)
            {
                Trace.WriteLine(
                    $"[FindFont] Invalid requested font name '{requestedFont}': {exception.Message}");
                return false;
            }
        }

        public bool TryFindTypeface(string typeface, out string fullPath)
        {
            fullPath = null;
            string normalized = NormalizeTypeface(typeface);
            if (string.IsNullOrEmpty(normalized)) return false;

            if (_filesByTypeface.Value.TryGetValue(normalized, out fullPath)) return true;
            return normalized[0] == '@' &&
                _filesByTypeface.Value.TryGetValue(normalized.Substring(1), out fullPath);
        }

        private Dictionary<string, string> BuildTypefaceIndex()
        {
            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in _fontFiles)
            {
                string extension = Path.GetExtension(file);
                if (!extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".otf", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    var glyph = new GlyphTypeface(new Uri(file, UriKind.Absolute));
                    foreach (string familyName in glyph.FamilyNames.Values)
                        AddTypeface(index, familyName, file);
                    foreach (string familyName in glyph.Win32FamilyNames.Values)
                        AddTypeface(index, familyName, file);
                }
                catch (Exception exception) when (
                    exception is ArgumentException ||
                    exception is FileFormatException ||
                    exception is IOException ||
                    exception is NotSupportedException ||
                    exception is UnauthorizedAccessException)
                {
                    Trace.WriteLine(
                        $"[FindFont] Skipped unsupported font metadata '{file}': {exception.Message}");
                }
            }
            return index;
        }

        private static void AddTypeface(Dictionary<string, string> index, string typeface, string file)
        {
            string normalized = NormalizeTypeface(typeface);
            if (string.IsNullOrEmpty(normalized)) return;

            AddIfMissing(index, normalized, file);
            if (normalized[0] == '@') AddIfMissing(index, normalized.Substring(1), file);
        }

        private static string NormalizeTypeface(string typeface)
        {
            return string.IsNullOrWhiteSpace(typeface) ? null : typeface.Trim();
        }

        private static void AddIfMissing(Dictionary<string, string> index, string key, string value)
        {
            if (!string.IsNullOrEmpty(key) && !index.ContainsKey(key)) index.Add(key, value);
        }

        private static bool IsSupported(string path)
        {
            string extension = Path.GetExtension(path);
            foreach (string supportedExtension in SupportedExtensions)
            {
                if (extension.Equals(supportedExtension, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}

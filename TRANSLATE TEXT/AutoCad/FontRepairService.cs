using System;
using System.Collections.Generic;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.GraphicsInterface;
using TranslateText.Services;

namespace TranslateText.AutoCad
{
    /// <summary>
    /// Detects missing fonts used by selected AutoCAD text entities and repairs their
    /// text styles from the font catalog deployed with this plugin.
    /// </summary>
    public sealed class FontRepairService
    {
        private const uint FrPrivate = 0x10;

        private static readonly object CatalogSync = new object();
        private static readonly Dictionary<string, EmbeddedFontCatalog> Catalogs =
            new Dictionary<string, EmbeddedFontCatalog>(StringComparer.OrdinalIgnoreCase);
        private static readonly object PrivateFontSync = new object();
        private static readonly HashSet<string> PrivateFontFiles =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> PrivateTypefaces =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Lazy<HashSet<string>> InstalledTypefaces =
            new Lazy<HashSet<string>>(LoadInstalledTypefaces, true);
        private static readonly Regex InlineFontRegex = new Regex(
            @"\\(?<kind>[Ff])(?<font>[^;|]+)(?:\|[^;]*)?;",
            RegexOptions.Compiled);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int AddFontResourceEx(string fileName, uint flags, IntPtr reserved);

        public static string GetDeployedFontRoot()
        {
            string assemblyDirectory = Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location);
            return Path.Combine(assemblyDirectory ?? string.Empty, "Text Font");
        }

        public FontRepairResult Repair(
            Database database,
            IEnumerable<ObjectId> selectedIds,
            string fontRoot)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (selectedIds == null) throw new ArgumentNullException(nameof(selectedIds));

            var result = new FontRepairResult(fontRoot);
            EmbeddedFontCatalog catalog = GetCatalog(fontRoot);
            result.CatalogFontCount = catalog.Count;

            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                FontSelection selection = CollectFontSelection(transaction, selectedIds);
                result.TextStyleCount = selection.StyleIds.Count;

                foreach (ObjectId styleId in selection.StyleIds)
                {
                    var style = transaction.GetObject(styleId, OpenMode.ForRead) as TextStyleTableRecord;
                    if (style == null) continue;

                    try
                    {
                        RepairStyle(style, database, catalog, result);
                    }
                    catch (System.Exception exception)
                    {
                        result.ErrorCount++;
                        result.AddMessage(
                            $"Text Style \"{style.Name}\": lỗi khi kiểm tra font: {exception.Message}");
                    }
                }

                RepairInlineFontOverrides(
                    transaction,
                    selection.FormattedTextEntityIds,
                    database,
                    catalog,
                    result);

                transaction.Commit();
            }

            return result;
        }

        private static EmbeddedFontCatalog GetCatalog(string fontRoot)
        {
            string normalizedRoot = Path.GetFullPath(fontRoot);
            lock (CatalogSync)
            {
                if (!Catalogs.TryGetValue(normalizedRoot, out EmbeddedFontCatalog catalog))
                {
                    catalog = new EmbeddedFontCatalog(normalizedRoot);
                    Catalogs.Add(normalizedRoot, catalog);
                }
                return catalog;
            }
        }

        private static void RepairStyle(
            TextStyleTableRecord style,
            Database database,
            EmbeddedFontCatalog catalog,
            FontRepairResult result)
        {
            string fileName = style.FileName;
            FontDescriptor descriptor = style.Font;
            string typeface = descriptor.TypeFace;
            string extension = Path.GetExtension(fileName ?? string.Empty);

            bool isTrueType = extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".otf", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".ttc", StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(typeface) &&
                 !extension.Equals(".shx", StringComparison.OrdinalIgnoreCase));

            if (isTrueType)
                RepairTrueTypeFont(style, fileName, descriptor, database, catalog, result);
            else
                RepairShxFont(style, fileName, false, database, catalog, result);

            RepairShxFont(style, style.BigFontFileName, true, database, catalog, result);
        }

        private static void RepairTrueTypeFont(
            TextStyleTableRecord style,
            string fileName,
            FontDescriptor descriptor,
            Database database,
            EmbeddedFontCatalog catalog,
            FontRepairResult result)
        {
            string typeface = descriptor.TypeFace;
            if (IsFontFileAvailable(fileName, database, FindFileHint.TrueTypeFontFile) ||
                IsTypefaceAvailable(typeface))
            {
                return;
            }

            string requestedFont = !string.IsNullOrWhiteSpace(fileName) ? fileName : typeface;
            if (string.IsNullOrWhiteSpace(requestedFont)) return;
            result.MissingFontCount++;

            string bundledPath;
            bool found = catalog.TryFindFile(fileName, out bundledPath) ||
                catalog.TryFindTypeface(typeface, out bundledPath);
            if (!found)
            {
                AddUnresolved(style, requestedFont, result);
                return;
            }

            if (!RegisterPrivateTrueTypeFont(bundledPath, typeface))
            {
                result.UnresolvedFontCount++;
                result.AddMessage(
                    $"Text Style \"{style.Name}\": tìm thấy \"{requestedFont}\" nhưng Windows không nạp được file \"{bundledPath}\".");
                return;
            }

            if (!style.IsDependent)
            {
                if (!style.IsWriteEnabled) style.UpgradeOpen();
                style.Font = descriptor;
                style.FileName = bundledPath;
            }

            result.RepairedFontCount++;
            result.AddMessage(
                $"Text Style \"{style.Name}\": đã nạp font \"{requestedFont}\" từ \"{bundledPath}\".");
        }

        private static void RepairShxFont(
            TextStyleTableRecord style,
            string fileName,
            bool isBigFont,
            Database database,
            EmbeddedFontCatalog catalog,
            FontRepairResult result)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return;
            if (IsFontFileAvailable(fileName, database, FindFileHint.CompiledShapeFile)) return;

            result.MissingFontCount++;
            if (!catalog.TryFindFile(fileName, out string bundledPath))
            {
                AddUnresolved(style, fileName, result);
                return;
            }

            if (style.IsDependent)
            {
                result.UnresolvedFontCount++;
                result.AddMessage(
                    $"Text Style phụ thuộc \"{style.Name}\": tìm thấy \"{fileName}\" nhưng không thể sửa trực tiếp trong bản vẽ tham chiếu.");
                return;
            }

            if (!style.IsWriteEnabled) style.UpgradeOpen();
            if (isBigFont)
                style.BigFontFileName = bundledPath;
            else
                style.FileName = bundledPath;

            result.RepairedFontCount++;
            string kind = isBigFont ? "Big Font" : "font";
            result.AddMessage(
                $"Text Style \"{style.Name}\": đã khôi phục {kind} \"{fileName}\" từ \"{bundledPath}\".");
        }

        private static void RepairInlineFontOverrides(
            Transaction transaction,
            IEnumerable<ObjectId> entityIds,
            Database database,
            EmbeddedFontCatalog catalog,
            FontRepairResult result)
        {
            var resolutions = new Dictionary<string, InlineFontResolution>(
                StringComparer.OrdinalIgnoreCase);

            foreach (ObjectId entityId in entityIds)
            {
                Entity entity = transaction.GetObject(entityId, OpenMode.ForRead) as Entity;
                if (entity == null) continue;

                string contents = GetFormattedContents(entity);
                if (string.IsNullOrEmpty(contents)) continue;

                bool changed = false;
                string repairedContents = InlineFontRegex.Replace(contents, match =>
                {
                    string kind = match.Groups["kind"].Value;
                    string requestedFont = match.Groups["font"].Value.Trim();
                    string cacheKey = kind + "\u001F" + requestedFont;

                    if (!resolutions.TryGetValue(cacheKey, out InlineFontResolution resolution))
                    {
                        resolution = ResolveInlineFont(
                            kind,
                            requestedFont,
                            database,
                            catalog,
                            result);
                        resolutions.Add(cacheKey, resolution);
                    }

                    if (string.IsNullOrEmpty(resolution.ReplacementFont)) return match.Value;

                    System.Text.RegularExpressions.Group fontGroup = match.Groups["font"];
                    int relativeIndex = fontGroup.Index - match.Index;
                    changed = true;
                    return match.Value.Remove(relativeIndex, fontGroup.Length)
                        .Insert(relativeIndex, resolution.ReplacementFont);
                });

                if (!changed) continue;
                SetFormattedContents(entity, repairedContents);
            }
        }

        private static InlineFontResolution ResolveInlineFont(
            string kind,
            string requestedFont,
            Database database,
            EmbeddedFontCatalog catalog,
            FontRepairResult result)
        {
            string extension = Path.GetExtension(requestedFont);
            bool isShx = kind == "F" ||
                extension.Equals(".shx", StringComparison.OrdinalIgnoreCase);

            if (isShx)
            {
                if (IsFontFileAvailable(requestedFont, database, FindFileHint.CompiledShapeFile))
                    return InlineFontResolution.Available;
            }
            else if (IsFontFileAvailable(requestedFont, database, FindFileHint.TrueTypeFontFile) ||
                     IsTypefaceAvailable(requestedFont))
            {
                return InlineFontResolution.Available;
            }

            result.MissingFontCount++;
            string bundledPath;
            bool found = catalog.TryFindFile(requestedFont, out bundledPath) ||
                (!isShx && catalog.TryFindTypeface(requestedFont, out bundledPath));
            if (!found)
            {
                result.UnresolvedFontCount++;
                result.AddMessage(
                    $"MText font override: thiếu \"{requestedFont}\" và không tìm thấy trong dữ liệu plugin.");
                return InlineFontResolution.Unresolved;
            }

            if (!isShx)
            {
                if (!RegisterPrivateTrueTypeFont(bundledPath, requestedFont))
                {
                    result.UnresolvedFontCount++;
                    result.AddMessage(
                        $"MText font override: tìm thấy \"{requestedFont}\" nhưng Windows không nạp được \"{bundledPath}\".");
                    return InlineFontResolution.Unresolved;
                }

                result.RepairedFontCount++;
                result.AddMessage(
                    $"MText font override: đã nạp \"{requestedFont}\" từ \"{bundledPath}\".");
                return InlineFontResolution.Available;
            }

            result.RepairedFontCount++;
            result.AddMessage(
                $"MText font override: đã liên kết \"{requestedFont}\" tới \"{bundledPath}\".");
            return new InlineFontResolution(bundledPath);
        }

        private static string GetFormattedContents(Entity entity)
        {
            if (entity is MText mText) return mText.Contents;
            if (entity is MLeader mLeader && mLeader.ContentType == ContentType.MTextContent)
                return mLeader.MText.Contents;
            if (entity is Dimension dimension) return dimension.DimensionText;
            return null;
        }

        private static void SetFormattedContents(Entity entity, string contents)
        {
            if (!entity.IsWriteEnabled) entity.UpgradeOpen();

            if (entity is MText mText)
            {
                mText.Contents = contents;
            }
            else if (entity is MLeader mLeader && mLeader.ContentType == ContentType.MTextContent)
            {
                MText leaderText = mLeader.MText;
                leaderText.Contents = contents;
                mLeader.MText = leaderText;
            }
            else if (entity is Dimension dimension)
            {
                dimension.DimensionText = contents;
            }
        }

        private static void AddUnresolved(
            TextStyleTableRecord style,
            string requestedFont,
            FontRepairResult result)
        {
            result.UnresolvedFontCount++;
            result.AddMessage(
                $"Text Style \"{style.Name}\": thiếu font \"{requestedFont}\" và không tìm thấy trong dữ liệu plugin.");
        }

        private static bool IsFontFileAvailable(
            string fileName,
            Database database,
            FindFileHint hint)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return false;
            if (Path.IsPathRooted(fileName) && File.Exists(fileName)) return true;

            try
            {
                HostApplicationServices host = HostApplicationServices.Current;
                if (host == null) return false;
                string resolvedPath = host.FindFile(fileName, database, hint);
                return !string.IsNullOrWhiteSpace(resolvedPath);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsTypefaceAvailable(string typeface)
        {
            if (string.IsNullOrWhiteSpace(typeface)) return false;
            string normalized = typeface.Trim();

            lock (PrivateFontSync)
            {
                if (PrivateTypefaces.Contains(normalized)) return true;
            }

            return InstalledTypefaces.Value.Contains(normalized) ||
                (normalized[0] == '@' &&
                 InstalledTypefaces.Value.Contains(normalized.Substring(1)));
        }

        private static bool RegisterPrivateTrueTypeFont(string fileName, string typeface)
        {
            string fullPath = Path.GetFullPath(fileName);
            lock (PrivateFontSync)
            {
                if (PrivateFontFiles.Contains(fullPath)) return true;
                if (AddFontResourceEx(fullPath, FrPrivate, IntPtr.Zero) <= 0) return false;

                PrivateFontFiles.Add(fullPath);
                if (!string.IsNullOrWhiteSpace(typeface))
                {
                    string normalized = typeface.Trim();
                    PrivateTypefaces.Add(normalized);
                    if (normalized[0] == '@') PrivateTypefaces.Add(normalized.Substring(1));
                }
                return true;
            }
        }

        private static HashSet<string> LoadInstalledTypefaces()
        {
            var typefaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var installedFonts = new InstalledFontCollection())
                {
                    foreach (System.Drawing.FontFamily family in installedFonts.Families)
                        typefaces.Add(family.Name);
                }
            }
            catch
            {
                // If enumeration is unavailable, file-based AutoCAD resolution still works.
            }
            return typefaces;
        }

        private static FontSelection CollectFontSelection(
            Transaction transaction,
            IEnumerable<ObjectId> selectedIds)
        {
            var selection = new FontSelection();
            var processedEntities = new HashSet<ObjectId>();
            var processedBlockDefinitions = new HashSet<ObjectId>();

            foreach (ObjectId id in selectedIds)
            {
                CollectFromEntity(
                    id,
                    transaction,
                    selection,
                    processedEntities,
                    processedBlockDefinitions);
            }
            return selection;
        }

        private static void CollectFromEntity(
            ObjectId entityId,
            Transaction transaction,
            FontSelection selection,
            HashSet<ObjectId> processedEntities,
            HashSet<ObjectId> processedBlockDefinitions)
        {
            if (entityId.IsNull || !entityId.IsValid || entityId.IsErased ||
                !processedEntities.Add(entityId))
            {
                return;
            }

            Entity entity;
            try
            {
                entity = transaction.GetObject(entityId, OpenMode.ForRead) as Entity;
            }
            catch
            {
                return;
            }
            if (entity == null) return;

            if (entity is DBText dbText)
                AddStyleId(selection.StyleIds, dbText.TextStyleId);
            else if (entity is MText mText)
            {
                AddStyleId(selection.StyleIds, mText.TextStyleId);
                selection.FormattedTextEntityIds.Add(entityId);
            }
            else if (entity is MLeader mLeader)
            {
                AddStyleId(selection.StyleIds, mLeader.TextStyleId);
                if (mLeader.ContentType == ContentType.MTextContent)
                    selection.FormattedTextEntityIds.Add(entityId);
            }
            else if (entity is Dimension dimension)
            {
                using (DimStyleTableRecord dimStyle = dimension.GetDimstyleData())
                    AddStyleId(selection.StyleIds, dimStyle.Dimtxsty);
                selection.FormattedTextEntityIds.Add(entityId);
            }

            if (!(entity is BlockReference blockReference)) return;

            foreach (ObjectId attributeId in blockReference.AttributeCollection)
            {
                CollectFromEntity(
                    attributeId,
                    transaction,
                    selection,
                    processedEntities,
                    processedBlockDefinitions);
            }

            ObjectId definitionId = blockReference.BlockTableRecord;
            if (definitionId.IsNull || !processedBlockDefinitions.Add(definitionId)) return;

            var definition = transaction.GetObject(definitionId, OpenMode.ForRead) as BlockTableRecord;
            if (definition == null || definition.IsFromExternalReference) return;

            foreach (ObjectId childId in definition)
            {
                CollectFromEntity(
                    childId,
                    transaction,
                    selection,
                    processedEntities,
                    processedBlockDefinitions);
            }
        }

        private static void AddStyleId(HashSet<ObjectId> styleIds, ObjectId styleId)
        {
            if (!styleId.IsNull && styleId.IsValid && !styleId.IsErased) styleIds.Add(styleId);
        }

        private sealed class FontSelection
        {
            public HashSet<ObjectId> StyleIds { get; } = new HashSet<ObjectId>();
            public HashSet<ObjectId> FormattedTextEntityIds { get; } = new HashSet<ObjectId>();
        }

        private sealed class InlineFontResolution
        {
            public static readonly InlineFontResolution Available = new InlineFontResolution(null);
            public static readonly InlineFontResolution Unresolved = new InlineFontResolution(null);

            public InlineFontResolution(string replacementFont)
            {
                ReplacementFont = replacementFont;
            }

            public string ReplacementFont { get; }
        }
    }

    public sealed class FontRepairResult
    {
        private readonly List<string> _messages = new List<string>();

        internal FontRepairResult(string fontRoot)
        {
            FontRoot = fontRoot;
        }

        public string FontRoot { get; }
        public int CatalogFontCount { get; internal set; }
        public int TextStyleCount { get; internal set; }
        public int MissingFontCount { get; internal set; }
        public int RepairedFontCount { get; internal set; }
        public int UnresolvedFontCount { get; internal set; }
        public int ErrorCount { get; internal set; }
        public IReadOnlyList<string> Messages => _messages;

        internal void AddMessage(string message)
        {
            _messages.Add(message);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Media;
using Autodesk.AutoCAD.DatabaseServices;
using TranslateText.Services;
using DiagnosticsTrace = System.Diagnostics.Trace;

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

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool RemoveFontResourceEx(
            string fileName,
            uint flags,
            IntPtr reserved);

        public static string GetDeployedFontRoot()
        {
            string assemblyDirectory = Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location);
            return Path.Combine(assemblyDirectory ?? string.Empty, "Text Font");
        }

        internal static void ReleasePrivateFonts()
        {
            lock (PrivateFontSync)
            {
                foreach (string fileName in PrivateFontFiles)
                {
                    if (!RemoveFontResourceEx(fileName, FrPrivate, IntPtr.Zero))
                    {
                        DiagnosticsTrace.WriteLine(
                            $"[FindFont] RemoveFontResourceEx failed for '{fileName}', Win32 error {Marshal.GetLastWin32Error()}.");
                    }
                }

                PrivateFontFiles.Clear();
                PrivateTypefaces.Clear();
            }
        }

        /// <summary>
        /// Reads immutable drawing data, resolves font files without an open transaction,
        /// then applies the prepared database changes in one short write transaction.
        /// </summary>
        public FontRepairResult Repair(
            Database database,
            IEnumerable<ObjectId> selectedIds,
            string fontRoot)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (selectedIds == null) throw new ArgumentNullException(nameof(selectedIds));
            if (string.IsNullOrWhiteSpace(fontRoot))
                throw new ArgumentException("Font root is required.", nameof(fontRoot));

            var result = new FontRepairResult(fontRoot);
            EmbeddedFontCatalog catalog = GetCatalog(fontRoot);
            result.CatalogFontCount = catalog.Count;

            FontInspectionSnapshot snapshot = ReadSnapshot(database, selectedIds);
            result.TextStyleCount = snapshot.TextStyles.Count;

            FontRepairPlan plan = BuildRepairPlan(snapshot, database, catalog, result);
            ApplyRepairPlan(database, plan);
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

        private static FontInspectionSnapshot ReadSnapshot(
            Database database,
            IEnumerable<ObjectId> selectedIds)
        {
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                FontSelection selection = CollectFontSelection(
                    database,
                    transaction,
                    selectedIds);
                var snapshot = new FontInspectionSnapshot();

                foreach (ObjectId styleId in selection.StyleIds)
                {
                    var style = transaction.GetObject(
                        styleId,
                        OpenMode.ForRead) as TextStyleTableRecord;
                    if (style == null) continue;

                    snapshot.TextStyles.Add(new TextStyleSnapshot(
                        styleId,
                        style.Name,
                        style.FileName,
                        style.BigFontFileName,
                        style.Font.TypeFace,
                        style.IsDependent));
                }

                foreach (ObjectId entityId in selection.FormattedTextEntityIds)
                {
                    var entity = transaction.GetObject(entityId, OpenMode.ForRead) as Entity;
                    if (entity == null) continue;

                    string contents = GetFormattedContents(entity);
                    if (!string.IsNullOrEmpty(contents))
                        snapshot.FormattedTexts.Add(new FormattedTextSnapshot(entityId, contents));
                }

                return snapshot;
            }
        }

        private static FontRepairPlan BuildRepairPlan(
            FontInspectionSnapshot snapshot,
            Database database,
            EmbeddedFontCatalog catalog,
            FontRepairResult result)
        {
            var plan = new FontRepairPlan();
            foreach (TextStyleSnapshot style in snapshot.TextStyles)
            {
                var update = new TextStyleUpdate(style.Id);
                ResolveStyleRepair(style, update, database, catalog, result);
                if (update.HasChanges) plan.TextStyleUpdates.Add(update);
            }

            ResolveInlineFontOverrides(
                snapshot.FormattedTexts,
                plan,
                database,
                catalog,
                result);
            return plan;
        }

        private static void ResolveStyleRepair(
            TextStyleSnapshot style,
            TextStyleUpdate update,
            Database database,
            EmbeddedFontCatalog catalog,
            FontRepairResult result)
        {
            string extension = GetExtensionOrEmpty(style.FileName);
            bool isTrueType = extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".otf", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".ttc", StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(style.Typeface) &&
                 !extension.Equals(".shx", StringComparison.OrdinalIgnoreCase));

            if (isTrueType)
                ResolveTrueTypeFont(style, update, database, catalog, result);
            else
                ResolveShxFont(style, update, style.FileName, false, database, catalog, result);

            ResolveShxFont(
                style,
                update,
                style.BigFontFileName,
                true,
                database,
                catalog,
                result);
        }

        private static void ResolveTrueTypeFont(
            TextStyleSnapshot style,
            TextStyleUpdate update,
            Database database,
            EmbeddedFontCatalog catalog,
            FontRepairResult result)
        {
            bool typefaceAvailable = IsTypefaceAvailable(style.Typeface);
            bool fileAvailable = IsFontFileAvailable(
                style.FileName,
                database,
                FindFileHint.TrueTypeFontFile);
            if (typefaceAvailable ||
                (fileAvailable && !IsPathInsideRoot(style.FileName, catalog.RootDirectory)))
            {
                return;
            }

            string requestedFont = !string.IsNullOrWhiteSpace(style.FileName)
                ? style.FileName
                : style.Typeface;
            if (string.IsNullOrWhiteSpace(requestedFont)) return;
            result.MissingFontCount++;

            bool found = catalog.TryFindFile(style.FileName, out string bundledPath) ||
                catalog.TryFindTypeface(style.Typeface, out bundledPath);
            if (!found)
            {
                AddUnresolved(style.Name, requestedFont, result);
                return;
            }

            if (!RegisterPrivateTrueTypeFont(bundledPath, style.Typeface))
            {
                result.UnresolvedFontCount++;
                result.AddMessage(
                    $"Text Style \"{style.Name}\": tìm thấy \"{requestedFont}\" nhưng Windows không nạp được file \"{bundledPath}\".");
                return;
            }

            if (!style.IsDependent) update.SetMainFontFile(bundledPath);

            result.RepairedFontCount++;
            result.AddMessage(
                $"Text Style \"{style.Name}\": đã nạp font \"{requestedFont}\" từ \"{bundledPath}\".");
        }

        private static void ResolveShxFont(
            TextStyleSnapshot style,
            TextStyleUpdate update,
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
                AddUnresolved(style.Name, fileName, result);
                return;
            }

            if (style.IsDependent)
            {
                result.UnresolvedFontCount++;
                result.AddMessage(
                    $"Text Style phụ thuộc \"{style.Name}\": tìm thấy \"{fileName}\" nhưng không thể sửa trực tiếp trong bản vẽ tham chiếu.");
                return;
            }

            if (isBigFont)
                update.SetBigFontFile(bundledPath);
            else
                update.SetMainFontFile(bundledPath);

            result.RepairedFontCount++;
            string kind = isBigFont ? "Big Font" : "font";
            result.AddMessage(
                $"Text Style \"{style.Name}\": đã khôi phục {kind} \"{fileName}\" từ \"{bundledPath}\".");
        }

        private static void ResolveInlineFontOverrides(
            IEnumerable<FormattedTextSnapshot> formattedTexts,
            FontRepairPlan plan,
            Database database,
            EmbeddedFontCatalog catalog,
            FontRepairResult result)
        {
            var resolutions = new Dictionary<string, InlineFontResolution>(
                StringComparer.OrdinalIgnoreCase);

            foreach (FormattedTextSnapshot text in formattedTexts)
            {
                bool changed = false;
                string repairedContents = InlineFontRegex.Replace(text.Contents, match =>
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

                if (changed)
                {
                    plan.FormattedTextUpdates.Add(new FormattedTextUpdate(
                        text.Id,
                        repairedContents));
                }
            }
        }

        private static InlineFontResolution ResolveInlineFont(
            string kind,
            string requestedFont,
            Database database,
            EmbeddedFontCatalog catalog,
            FontRepairResult result)
        {
            string extension = GetExtensionOrEmpty(requestedFont);
            bool isShx = kind == "F" ||
                extension.Equals(".shx", StringComparison.OrdinalIgnoreCase);

            if (isShx)
            {
                if (IsFontFileAvailable(requestedFont, database, FindFileHint.CompiledShapeFile))
                    return InlineFontResolution.Available;
            }
            else
            {
                bool typefaceAvailable = IsTypefaceAvailable(requestedFont);
                bool fileAvailable = IsFontFileAvailable(
                    requestedFont,
                    database,
                    FindFileHint.TrueTypeFontFile);
                if (typefaceAvailable ||
                    (fileAvailable &&
                     !IsPathInsideRoot(requestedFont, catalog.RootDirectory)))
                {
                    return InlineFontResolution.Available;
                }
            }

            result.MissingFontCount++;
            bool found = catalog.TryFindFile(requestedFont, out string bundledPath) ||
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

        private static void ApplyRepairPlan(Database database, FontRepairPlan plan)
        {
            if (!plan.HasDatabaseChanges) return;

            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                foreach (TextStyleUpdate update in plan.TextStyleUpdates)
                {
                    ValidateObjectId(update.Id, database, "Text Style");
                    var style = transaction.GetObject(
                        update.Id,
                        OpenMode.ForRead) as TextStyleTableRecord;
                    if (style == null)
                        throw new InvalidOperationException("Text Style không còn tồn tại.");
                    if (style.IsDependent)
                        throw new InvalidOperationException(
                            $"Text Style phụ thuộc \"{style.Name}\" không thể được sửa.");

                    style.UpgradeOpen();
                    if (update.HasMainFontFile) style.FileName = update.MainFontFile;
                    if (update.HasBigFontFile) style.BigFontFileName = update.BigFontFile;
                }

                foreach (FormattedTextUpdate update in plan.FormattedTextUpdates)
                {
                    ValidateObjectId(update.Id, database, "formatted text");
                    var entity = transaction.GetObject(update.Id, OpenMode.ForRead) as Entity;
                    if (entity == null)
                        throw new InvalidOperationException("Đối tượng text không còn tồn tại.");

                    SetFormattedContents(entity, update.Contents);
                }

                transaction.Commit();
            }
        }

        private static void ValidateObjectId(ObjectId id, Database database, string objectKind)
        {
            if (id.IsNull || !id.IsValid || id.IsErased || id.Database != database)
            {
                throw new InvalidOperationException(
                    $"ObjectId của {objectKind} không còn hợp lệ trong bản vẽ hiện tại.");
            }
        }

        private static string GetFormattedContents(Entity entity)
        {
            if (entity is MText mText) return mText.Contents;
            if (entity is MLeader mLeader && mLeader.ContentType == ContentType.MTextContent)
            {
                using (MText leaderText = mLeader.MText)
                    return leaderText?.Contents;
            }
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
                using (MText leaderText = mLeader.MText)
                {
                    if (leaderText == null)
                        throw new InvalidOperationException("MLeader không còn MText content.");
                    leaderText.Contents = contents;
                    mLeader.MText = leaderText;
                }
            }
            else if (entity is Dimension dimension)
            {
                dimension.DimensionText = contents;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Không hỗ trợ cập nhật formatted text cho {entity.GetType().Name}.");
            }
        }

        private static void AddUnresolved(
            string styleName,
            string requestedFont,
            FontRepairResult result)
        {
            result.UnresolvedFontCount++;
            result.AddMessage(
                $"Text Style \"{styleName}\": thiếu font \"{requestedFont}\" và không tìm thấy trong dữ liệu plugin.");
        }

        private static bool IsFontFileAvailable(
            string fileName,
            Database database,
            FindFileHint hint)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return false;

            try
            {
                if (Path.IsPathRooted(fileName) && File.Exists(fileName)) return true;
                HostApplicationServices host = HostApplicationServices.Current;
                if (host == null) return false;
                string resolvedPath = host.FindFile(fileName, database, hint);
                return !string.IsNullOrWhiteSpace(resolvedPath);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception exception)
            {
                DiagnosticsTrace.WriteLine(
                    $"[FindFont] AutoCAD could not resolve '{fileName}' ({exception.ErrorStatus}): {exception.Message}");
                return false;
            }
            catch (IOException exception)
            {
                DiagnosticsTrace.WriteLine(
                    $"[FindFont] I/O error while resolving '{fileName}': {exception.Message}");
                return false;
            }
            catch (UnauthorizedAccessException exception)
            {
                DiagnosticsTrace.WriteLine(
                    $"[FindFont] Access denied while resolving '{fileName}': {exception.Message}");
                return false;
            }
            catch (ArgumentException exception)
            {
                DiagnosticsTrace.WriteLine(
                    $"[FindFont] Invalid font path '{fileName}': {exception.Message}");
                return false;
            }
        }

        private static string GetExtensionOrEmpty(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;
            try
            {
                return Path.GetExtension(fileName);
            }
            catch (ArgumentException exception)
            {
                DiagnosticsTrace.WriteLine(
                    $"[FindFont] Invalid font name '{fileName}': {exception.Message}");
                return string.Empty;
            }
        }

        private static bool IsPathInsideRoot(string fileName, string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(fileName) ||
                string.IsNullOrWhiteSpace(rootDirectory))
            {
                return false;
            }

            try
            {
                if (!Path.IsPathRooted(fileName)) return false;
                string root = Path.GetFullPath(rootDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;
                string file = Path.GetFullPath(fileName);
                return file.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException exception)
            {
                DiagnosticsTrace.WriteLine(
                    $"[FindFont] Invalid font path '{fileName}': {exception.Message}");
                return false;
            }
            catch (NotSupportedException exception)
            {
                DiagnosticsTrace.WriteLine(
                    $"[FindFont] Unsupported font path '{fileName}': {exception.Message}");
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
                if (AddFontResourceEx(fullPath, FrPrivate, IntPtr.Zero) <= 0)
                {
                    DiagnosticsTrace.WriteLine(
                        $"[FindFont] AddFontResourceEx failed for '{fullPath}', Win32 error {Marshal.GetLastWin32Error()}.");
                    return false;
                }

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
                foreach (FontFamily family in Fonts.SystemFontFamilies)
                {
                    if (!string.IsNullOrWhiteSpace(family.Source))
                        typefaces.Add(family.Source.TrimStart('#'));
                    foreach (string familyName in family.FamilyNames.Values)
                        typefaces.Add(familyName);
                }
            }
            catch (System.Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is NotSupportedException ||
                exception is InvalidOperationException ||
                exception is ArgumentException ||
                exception is COMException)
            {
                DiagnosticsTrace.WriteLine(
                    $"[FindFont] Could not enumerate installed Windows fonts: {exception.Message}");
            }
            return typefaces;
        }

        private static FontSelection CollectFontSelection(
            Database database,
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
                    database,
                    transaction,
                    selection,
                    processedEntities,
                    processedBlockDefinitions);
            }
            return selection;
        }

        private static void CollectFromEntity(
            ObjectId entityId,
            Database database,
            Transaction transaction,
            FontSelection selection,
            HashSet<ObjectId> processedEntities,
            HashSet<ObjectId> processedBlockDefinitions)
        {
            if (entityId.IsNull || !entityId.IsValid || entityId.IsErased ||
                entityId.Database != database || !processedEntities.Add(entityId))
            {
                return;
            }

            var entity = transaction.GetObject(entityId, OpenMode.ForRead) as Entity;
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
                    database,
                    transaction,
                    selection,
                    processedEntities,
                    processedBlockDefinitions);
            }

            ObjectId definitionId = blockReference.BlockTableRecord;
            if (definitionId.IsNull || definitionId.Database != database ||
                !processedBlockDefinitions.Add(definitionId))
            {
                return;
            }

            var definition = transaction.GetObject(
                definitionId,
                OpenMode.ForRead) as BlockTableRecord;
            if (definition == null || definition.IsFromExternalReference) return;

            foreach (ObjectId childId in definition)
            {
                CollectFromEntity(
                    childId,
                    database,
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

        private sealed class FontInspectionSnapshot
        {
            public List<TextStyleSnapshot> TextStyles { get; } = new List<TextStyleSnapshot>();
            public List<FormattedTextSnapshot> FormattedTexts { get; } =
                new List<FormattedTextSnapshot>();
        }

        private sealed class TextStyleSnapshot
        {
            public TextStyleSnapshot(
                ObjectId id,
                string name,
                string fileName,
                string bigFontFileName,
                string typeface,
                bool isDependent)
            {
                Id = id;
                Name = name;
                FileName = fileName;
                BigFontFileName = bigFontFileName;
                Typeface = typeface;
                IsDependent = isDependent;
            }

            public ObjectId Id { get; }
            public string Name { get; }
            public string FileName { get; }
            public string BigFontFileName { get; }
            public string Typeface { get; }
            public bool IsDependent { get; }
        }

        private sealed class FormattedTextSnapshot
        {
            public FormattedTextSnapshot(ObjectId id, string contents)
            {
                Id = id;
                Contents = contents;
            }

            public ObjectId Id { get; }
            public string Contents { get; }
        }

        private sealed class FontRepairPlan
        {
            public List<TextStyleUpdate> TextStyleUpdates { get; } =
                new List<TextStyleUpdate>();
            public List<FormattedTextUpdate> FormattedTextUpdates { get; } =
                new List<FormattedTextUpdate>();
            public bool HasDatabaseChanges =>
                TextStyleUpdates.Count > 0 || FormattedTextUpdates.Count > 0;
        }

        private sealed class TextStyleUpdate
        {
            public TextStyleUpdate(ObjectId id)
            {
                Id = id;
            }

            public ObjectId Id { get; }
            public string MainFontFile { get; private set; }
            public string BigFontFile { get; private set; }
            public bool HasMainFontFile { get; private set; }
            public bool HasBigFontFile { get; private set; }
            public bool HasChanges => HasMainFontFile || HasBigFontFile;

            public void SetMainFontFile(string fileName)
            {
                MainFontFile = fileName;
                HasMainFontFile = true;
            }

            public void SetBigFontFile(string fileName)
            {
                BigFontFile = fileName;
                HasBigFontFile = true;
            }
        }

        private sealed class FormattedTextUpdate
        {
            public FormattedTextUpdate(ObjectId id, string contents)
            {
                Id = id;
                Contents = contents;
            }

            public ObjectId Id { get; }
            public string Contents { get; }
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
        public IReadOnlyList<string> Messages => _messages;

        internal void AddMessage(string message)
        {
            _messages.Add(message);
        }
    }
}

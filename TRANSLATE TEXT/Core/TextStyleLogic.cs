using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using TranslateText.Models;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using DiagnosticsTrace = System.Diagnostics.Trace;

namespace TranslateText.Core
{
    /// <summary>
    /// Logic xử lý đổi Text Style + chuyển đổi encoding cho các Entity.
    /// Tách riêng khỏi Commands.cs theo kiến trúc workflow.
    /// </summary>
    public class TextStyleLogic
    {
        private static readonly Regex FontOverrideRegex = new Regex(
            @"\\[Ff][^;]*;",
            RegexOptions.Compiled);
        private static readonly Regex UnicodeEscapeRegex = new Regex(
            @"\\?U\+([0-9A-Fa-f]{4})",
            RegexOptions.Compiled);

        private readonly HashSet<ObjectId> _processedIds = new HashSet<ObjectId>();

        /// <summary>
        /// Reset danh sách đã xử lý — gọi trước mỗi lần chạy lệnh.
        /// </summary>
        public void Reset()
        {
            _processedIds.Clear();
        }

        /// <summary>
        /// Xử lý từng Entity: Đổi TextStyle + Chuyển đổi encoding.
        /// Hỗ trợ DBText, MText, MLeader, BlockReference (Attributes), Dimension, AttributeDefinition.
        /// </summary>
        public bool ProcessEntity(Entity entity, ObjectId styleId, EncodingType sourceEncoding,
            EncodingType targetEncoding, TextCaseOption textCase, Transaction transaction)
        {
            if (_processedIds.Contains(entity.ObjectId)) return false;

            try
            {
                bool supported = entity is DBText ||
                    entity is MText ||
                    entity is BlockReference ||
                    entity is Dimension ||
                    (entity is MLeader leader && leader.ContentType == ContentType.MTextContent);
                if (!supported) return false;

                // A block reference itself does not change; only its attributes do.
                if (!(entity is BlockReference) && !entity.IsWriteEnabled) entity.UpgradeOpen();

                if (entity is DBText dbText)
                {
                    dbText.TextStyleId = styleId;
                    dbText.TextString = TextCaseHelper.ApplyCaseSafe(
                        VnCharset.Convert(dbText.TextString, sourceEncoding, targetEncoding), textCase);
                    _processedIds.Add(entity.ObjectId);
                    return true;
                }
                else if (entity is MText mText)
                {
                    mText.TextStyleId = styleId;
                    // Remove font override để áp dụng style mới
                    string content = CleanMTextContent(mText.Contents);
                    mText.Contents = TextCaseHelper.ApplyCaseSafe(
                        VnCharset.Convert(content, sourceEncoding, targetEncoding), textCase);
                    _processedIds.Add(entity.ObjectId);
                    return true;
                }
                else if (entity is MLeader mLeader && mLeader.ContentType == ContentType.MTextContent)
                {
                    using (MText leaderText = mLeader.MText)
                    {
                        if (leaderText == null) return false;
                        leaderText.TextStyleId = styleId;
                        string content = CleanMTextContent(leaderText.Contents);
                        leaderText.Contents = TextCaseHelper.ApplyCaseSafe(
                            VnCharset.Convert(content, sourceEncoding, targetEncoding), textCase);
                        mLeader.MText = leaderText;
                    }
                    mLeader.TextStyleId = styleId;
                    _processedIds.Add(entity.ObjectId);
                    return true;
                }
                else if (entity is BlockReference blockRef)
                {
                    bool hasAttribute = false;
                    foreach (ObjectId attId in blockRef.AttributeCollection)
                    {
                        AttributeReference attRef = transaction.GetObject(attId, OpenMode.ForWrite) as AttributeReference;
                        if (attRef != null && !_processedIds.Contains(attId))
                        {
                            attRef.TextStyleId = styleId;
                            attRef.TextString = TextCaseHelper.ApplyCaseSafe(
                                VnCharset.Convert(attRef.TextString, sourceEncoding, targetEncoding), textCase);
                            _processedIds.Add(attId);
                            hasAttribute = true;
                        }
                    }
                    _processedIds.Add(entity.ObjectId);
                    return hasAttribute;
                }
                else if (entity is Dimension dimension)
                {
                    // Apply a text-style override without replacing the dimension style itself.
                    using (DimStyleTableRecord dimStyle = dimension.GetDimstyleData())
                    {
                        dimStyle.Dimtxsty = styleId;
                        dimension.SetDimstyleData(dimStyle);
                    }
                    if (!string.IsNullOrEmpty(dimension.DimensionText))
                        dimension.DimensionText = TextCaseHelper.ApplyCaseSafe(
                            VnCharset.Convert(dimension.DimensionText, sourceEncoding, targetEncoding), textCase);
                    _processedIds.Add(entity.ObjectId);
                    return true;
                }
                else if (entity is AttributeDefinition attDef)
                {
                    attDef.TextStyleId = styleId;
                    attDef.TextString = TextCaseHelper.ApplyCaseSafe(
                        VnCharset.Convert(attDef.TextString, sourceEncoding, targetEncoding), textCase);
                    _processedIds.Add(entity.ObjectId);
                    return true;
                }
            }
            catch (Exception ex)
            {
                DiagnosticsTrace.TraceError($"[ChangeTextStyle.ProcessEntity] {ex}");
                // Log nhưng không crash toàn bộ lệnh
                AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                    $"\n[ProcessEntity] Warning: {ex.Message}");
            }
            return false;
        }

        private static string CleanMTextContent(string content)
        {
            string cleaned = FontOverrideRegex.Replace(content, string.Empty);
            return UnicodeEscapeRegex.Replace(cleaned, match =>
                ((char)int.Parse(
                    match.Groups[1].Value,
                    System.Globalization.NumberStyles.HexNumber)).ToString());
        }
    }
}

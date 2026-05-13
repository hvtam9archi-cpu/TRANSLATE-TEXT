using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using TranslateText.Models;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TranslateText.Core
{
    /// <summary>
    /// Logic xử lý đổi Text Style + chuyển đổi encoding cho các Entity.
    /// Tách riêng khỏi Commands.cs theo kiến trúc workflow.
    /// </summary>
    public class TextStyleLogic
    {
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
                if (!entity.IsWriteEnabled) entity.UpgradeOpen();

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
                    string content = Regex.Replace(mText.Contents, @"\\[Ff][^;]*;", "");
                    content = Regex.Replace(content, @"\\?U\+([0-9A-Fa-f]{4})", m =>
                        ((char)int.Parse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber)).ToString());
                    mText.Contents = TextCaseHelper.ApplyCaseSafe(
                        VnCharset.Convert(content, sourceEncoding, targetEncoding), textCase);
                    _processedIds.Add(entity.ObjectId);
                    return true;
                }
                else if (entity is MLeader mLeader && mLeader.ContentType == ContentType.MTextContent)
                {
                    MText leaderText = mLeader.MText;
                    leaderText.TextStyleId = styleId;
                    string content = Regex.Replace(leaderText.Contents, @"\\[Ff][^;]*;", "");
                    content = Regex.Replace(content, @"\\?U\+([0-9A-Fa-f]{4})", m =>
                        ((char)int.Parse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber)).ToString());
                    leaderText.Contents = TextCaseHelper.ApplyCaseSafe(
                        VnCharset.Convert(content, sourceEncoding, targetEncoding), textCase);
                    mLeader.MText = leaderText;
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
                    dimension.DimensionStyle = styleId;
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
                // Log nhưng không crash toàn bộ lệnh
                AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                    $"\n[ProcessEntity] Warning: {ex.Message}");
            }
            return false;
        }
    }
}

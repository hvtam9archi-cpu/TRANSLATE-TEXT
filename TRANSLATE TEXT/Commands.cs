using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using TranslateText.Core;
using TranslateText.Models;
using TranslateText.Services;
using TranslateText.UI;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

// Đăng ký lệnh cho AutoCAD
[assembly: CommandClass(typeof(TranslateText.Commands))]

namespace TranslateText
{
    /// <summary>
    /// Entry point duy nhất chứa tất cả [CommandMethod].
    /// Không viết Logic tại đây — chỉ gọi đến các class Logic/Services.
    /// </summary>
    public class Commands
    {
        // Nhớ lựa chọn lần cuối của người dùng (Session-level)
        private static string _lastSourceLang = "auto";
        private static string _lastTargetLang = "vi";
        private static string _lastTextStyle = "Keep Original";
        private static TextCaseOption _lastTranslateTextCase = TextCaseOption.None;
        private static TextCaseOption _lastChangeStyleTextCase = TextCaseOption.None;

        // ========================================================================================
        // LỆNH 1: TRANSLATETEXT — Dịch thuật text trong bản vẽ AutoCAD
        // ========================================================================================

        [CommandMethod("TRANSLATETEXT", CommandFlags.UsePickSet)]
        public async void TranslateTextCmd()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor editor = doc.Editor;
            Database database = doc.Database;

            try
            {
                // 1. Đọc danh sách Text Style từ bản vẽ
                List<string> styleNames = new List<string> { "Keep Original" };
                using (Transaction transaction = doc.TransactionManager.StartTransaction())
                {
                    TextStyleTable textStyleTable = (TextStyleTable)transaction.GetObject(database.TextStyleTableId, OpenMode.ForRead);
                    foreach (ObjectId id in textStyleTable)
                    {
                        TextStyleTableRecord record = (TextStyleTableRecord)transaction.GetObject(id, OpenMode.ForRead);
                        styleNames.Add(record.Name);
                    }
                    transaction.Commit();
                }

                // 2. Hiển thị WPF Dialog
                string selectedStyleName;
                var window = new TranslateWindow(_lastSourceLang, _lastTargetLang, styleNames, _lastTextStyle, _lastTranslateTextCase);
                AcApp.ShowModalWindow(window);

                if (!window.IsConfirmed) return;
                _lastSourceLang = window.SelectedSourceCode;
                _lastTargetLang = window.SelectedTargetCode;
                _lastTextStyle = window.SelectedTextStyle;
                _lastTranslateTextCase = window.SelectedTextCase;
                selectedStyleName = window.SelectedTextStyle;

                // 3. Chọn đối tượng Text/MText/MLeader/Block
                var selectionFilter = new SelectionFilter(new TypedValue[]
                {
                    new TypedValue((int)DxfCode.Operator, "<OR"),
                    new TypedValue((int)DxfCode.Start, "TEXT"),
                    new TypedValue((int)DxfCode.Start, "MTEXT"),
                    new TypedValue((int)DxfCode.Start, "MULTILEADER"),
                    new TypedValue((int)DxfCode.Start, "INSERT"),
                    new TypedValue((int)DxfCode.Operator, "OR>")
                });

                PromptSelectionResult selectionResult = editor.GetSelection(selectionFilter);
                if (selectionResult.Status != PromptStatus.OK) return;

                // 4. Đọc dữ liệu text từ các entity (Decouple ra POCO)
                List<TextEntityData> dataList = new List<TextEntityData>();
                HashSet<ObjectId> processedBlockDefs = new HashSet<ObjectId>();

                using (Transaction transaction = doc.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId objectId in selectionResult.Value.GetObjectIds())
                    {
                        Entity entity = transaction.GetObject(objectId, OpenMode.ForRead) as Entity;
                        if (entity == null) continue;

                        if (entity is DBText dbText)
                        {
                            dataList.Add(new TextEntityData { Id = objectId, OriginalText = dbText.TextString });
                        }
                        else if (entity is MText mText)
                        {
                            dataList.Add(new TextEntityData { Id = objectId, OriginalText = mText.Contents });
                        }
                        else if (entity is MLeader mLeader && mLeader.ContentType == ContentType.MTextContent)
                        {
                            dataList.Add(new TextEntityData { Id = objectId, OriginalText = mLeader.MText.Contents });
                        }
                        else if (entity is BlockReference blockRef)
                        {
                            // A. Xử lý Attribute (Instance level — giá trị khác nhau mỗi block)
                            foreach (ObjectId attId in blockRef.AttributeCollection)
                            {
                                AttributeReference attRef = transaction.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                                if (attRef != null && !attRef.IsConstant)
                                {
                                    dataList.Add(new TextEntityData { Id = attId, OriginalText = attRef.TextString, IsAttribute = true });
                                }
                            }

                            // B. Xử lý Block Definition (Chỉ 1 lần mỗi loại Block để tối ưu)
                            ObjectId blockTableRecordId = blockRef.BlockTableRecord;
                            if (!processedBlockDefs.Contains(blockTableRecordId))
                            {
                                processedBlockDefs.Add(blockTableRecordId);
                                BlockTableRecord blockRecord = (BlockTableRecord)transaction.GetObject(blockTableRecordId, OpenMode.ForRead);
                                foreach (ObjectId subId in blockRecord)
                                {
                                    Entity subEntity = transaction.GetObject(subId, OpenMode.ForRead) as Entity;
                                    if (subEntity is DBText subTxt)
                                        dataList.Add(new TextEntityData { Id = subId, OriginalText = subTxt.TextString });
                                    else if (subEntity is MText subMtext)
                                        dataList.Add(new TextEntityData { Id = subId, OriginalText = subMtext.Contents });
                                }
                            }
                        }
                    }
                    transaction.Commit();
                }

                editor.WriteMessage($"\nProcessing {dataList.Count} objects (Optimized Blocks & Languages)...");

                // 5. Dịch thuật bất đồng bộ (chạy trên thread pool, không block UI Thread)
                using (SemaphoreSlim semaphore = new SemaphoreSlim(8))
                {
                    var tasks = dataList.Select(async item =>
                    {
                        string translated = await TranslationService.ProcessAsync(
                            item.OriginalText, _lastSourceLang, _lastTargetLang, semaphore);
                        item.ProcessedText = TextCaseHelper.ApplyCaseSafe(translated, _lastTranslateTextCase);
                    });
                    await Task.WhenAll(tasks);
                }

                // 6. Ghi kết quả về AutoCAD (phải trên Main Thread + DocumentLock)
                using (DocumentLock docLock = doc.LockDocument())
                using (Transaction transaction = doc.TransactionManager.StartTransaction())
                {
                    ObjectId targetStyleId = ObjectId.Null;
                    if (selectedStyleName != "Keep Original")
                    {
                        TextStyleTable textStyleTable = (TextStyleTable)transaction.GetObject(database.TextStyleTableId, OpenMode.ForRead);
                        if (textStyleTable.Has(selectedStyleName))
                            targetStyleId = textStyleTable[selectedStyleName];
                    }

                    int successCount = 0;
                    foreach (var item in dataList)
                    {
                        if (string.IsNullOrEmpty(item.ProcessedText) || item.OriginalText == item.ProcessedText) continue;

                        Entity entity = transaction.GetObject(item.Id, OpenMode.ForWrite) as Entity;
                        if (entity == null) continue;

                        if (entity is DBText dbText)
                        {
                            dbText.TextString = item.ProcessedText;
                            if (targetStyleId != ObjectId.Null) dbText.TextStyleId = targetStyleId;
                        }
                        else if (entity is MText mText)
                        {
                            mText.Contents = item.ProcessedText;
                            if (targetStyleId != ObjectId.Null) mText.TextStyleId = targetStyleId;
                        }
                        else if (entity is MLeader mLeader)
                        {
                            MText leaderText = mLeader.MText;
                            leaderText.Contents = item.ProcessedText;
                            if (targetStyleId != ObjectId.Null) leaderText.TextStyleId = targetStyleId;
                            mLeader.MText = leaderText;
                        }
                        else if (entity is AttributeReference attRef)
                        {
                            attRef.TextString = item.ProcessedText;
                            if (targetStyleId != ObjectId.Null) attRef.TextStyleId = targetStyleId;
                        }
                        else if (entity is AttributeDefinition attDef)
                        {
                            attDef.TextString = item.ProcessedText;
                            if (targetStyleId != ObjectId.Null) attDef.TextStyleId = targetStyleId;
                        }

                        successCount++;
                    }
                    transaction.Commit();
                    editor.WriteMessage($"\nDone! Translated {successCount} items.");
                }
                editor.Regen();
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\n[TranslateText] Error: {ex.Message}");
            }
        }

        // ========================================================================================
        // LỆNH 2: CHANGETEXTSTYLE — Chuyển đổi mã font tiếng Việt (Unicode/VNI/TCVN3)
        // ========================================================================================

        [CommandMethod("CHANGETEXTSTYLE")]
        public void ChangeTextStyleCmd()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database database = doc.Database;
            Editor editor = doc.Editor;

            try
            {
                // 1. Đọc danh sách Text Style (lọc bỏ annotative styles chứa "|")
                List<string> styleList = new List<string>();
                using (Transaction transaction = database.TransactionManager.StartTransaction())
                {
                    TextStyleTable textStyleTable = (TextStyleTable)transaction.GetObject(database.TextStyleTableId, OpenMode.ForRead);
                    foreach (ObjectId id in textStyleTable)
                    {
                        TextStyleTableRecord record = (TextStyleTableRecord)transaction.GetObject(id, OpenMode.ForRead);
                        if (!record.Name.Contains("|"))
                            styleList.Add(record.Name);
                    }
                    styleList.Sort();
                    transaction.Commit();
                }

                if (styleList.Count == 0)
                {
                    editor.WriteMessage("\nNo Text Style found!");
                    return;
                }

                // 2. Hiển thị WPF Dialog
                AppSettings.Load(out string savedStyle, out int savedTgt, out int savedSrc);

                var window = new ChangeStyleWindow(styleList, savedStyle, savedTgt, savedSrc, _lastChangeStyleTextCase);
                AcApp.ShowModalWindow(window);

                if (!window.IsConfirmed) return;

                string targetStyleName = window.TargetStyle;
                EncodingType sourceEncoding = window.SourceEncoding;
                EncodingType targetEncoding = window.TargetEncoding;
                _lastChangeStyleTextCase = window.SelectedTextCase;
                AppSettings.Save(targetStyleName, window.SelectedTargetIndex, window.SelectedSourceIndex);

                // 3. Chọn đối tượng
                PromptSelectionOptions selectionOptions = new PromptSelectionOptions
                {
                    MessageForAdding = "\nSelect Text/Block to Change Style:"
                };
                PromptSelectionResult selectionResult = editor.GetSelection(selectionOptions);
                if (selectionResult.Status != PromptStatus.OK) return;

                // 4. Xử lý — delegate logic sang TextStyleLogic
                var logic = new TextStyleLogic();
                HashSet<ObjectId> processedBlockDefs = new HashSet<ObjectId>();

                using (Transaction transaction = database.TransactionManager.StartTransaction())
                {
                    TextStyleTable textStyleTable = (TextStyleTable)transaction.GetObject(database.TextStyleTableId, OpenMode.ForRead);

                    if (!string.IsNullOrEmpty(targetStyleName) && textStyleTable.Has(targetStyleName))
                    {
                        ObjectId targetStyleId = textStyleTable[targetStyleName];
                        int processedCount = 0;

                        foreach (SelectedObject selectedObject in selectionResult.Value)
                        {
                            Entity entity = transaction.GetObject(selectedObject.ObjectId, OpenMode.ForWrite) as Entity;
                            if (entity == null) continue;

                            // Xử lý entity trực tiếp + Attributes
                            if (logic.ProcessEntity(entity, targetStyleId, sourceEncoding, targetEncoding, _lastChangeStyleTextCase, transaction))
                                processedCount++;

                            // Xử lý Block Definition (1 lần mỗi loại block)
                            if (entity is BlockReference blockRef)
                            {
                                ObjectId blockRecordId = blockRef.BlockTableRecord;
                                if (!processedBlockDefs.Contains(blockRecordId))
                                {
                                    processedBlockDefs.Add(blockRecordId);
                                    BlockTableRecord blockRecord = (BlockTableRecord)transaction.GetObject(blockRecordId, OpenMode.ForRead);
                                    foreach (ObjectId subId in blockRecord)
                                    {
                                        Entity subEntity = transaction.GetObject(subId, OpenMode.ForWrite) as Entity;
                                        if (subEntity != null)
                                            logic.ProcessEntity(subEntity, targetStyleId, sourceEncoding, targetEncoding, _lastChangeStyleTextCase, transaction);
                                    }
                                }
                            }
                        }

                        transaction.Commit();
                        editor.WriteMessage($"\nDone. Processed {processedCount} items (plus unique block definitions).");
                        editor.Regen();
                    }
                }
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\n[ChangeTextStyle] Error: {ex.Message}");
            }
        }
    }
}

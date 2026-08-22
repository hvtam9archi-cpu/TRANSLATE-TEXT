using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using TranslateText.AutoCad;
using TranslateText.Core;
using TranslateText.Models;
using TranslateText.Services;
using TranslateText.UI;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using DiagnosticsTrace = System.Diagnostics.Trace;

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
        public void TranslateTextCmd()
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
                PromptSelectionResult selectionResult =
                    TextSelectionInteraction.GetTextSelection(
                        editor,
                        "\nSelect Text/MText/MLeader/Block to translate:",
                        includeDimensions: false);
                if (selectionResult.Status != PromptStatus.OK) return;

                // 4. Đọc dữ liệu text từ các entity (Decouple ra POCO)
                var entityRepository = new TranslationEntityRepository();
                List<TextEntityData> dataList;

                using (Transaction transaction = doc.TransactionManager.StartTransaction())
                {
                    dataList = entityRepository.Read(transaction, selectionResult.Value.GetObjectIds());
                    transaction.Commit();
                }

                editor.WriteMessage($"\nProcessing {dataList.Count} objects (Optimized Blocks & Languages)...");

                // 5. Dịch trên background thread kèm cửa sổ tiến trình modeless
                //    — AutoCAD không bị khóa trong lúc chờ API dịch thuật.
                int uniqueCount = CountUniqueTexts(dataList);
                var progressWindow = new TranslateProgressWindow(uniqueCount);
                var cancellationTokenSource = new CancellationTokenSource();

                // Đóng cửa sổ tiến trình (✕ hoặc Cancel) cũng là yêu cầu hủy.
                progressWindow.Closed += (s, e) => cancellationTokenSource.Cancel();

                var batchProcessor = new TranslationBatchProcessor();
                IProgress<int> progress = new UiThreadProgress(completed =>
                    progressWindow.Dispatcher.BeginInvoke(
                        new Action(() => progressWindow.ReportCompleted(completed))));

                AcApp.ShowModelessWindow(progressWindow);

                Task<TranslationBatchResult> translateTask = Task.Run(
                    () => batchProcessor.ProcessAsync(
                        dataList,
                        _lastSourceLang,
                        _lastTargetLang,
                        _lastTranslateTextCase,
                        cancellationTokenSource.Token,
                        progress),
                    cancellationTokenSource.Token);

                // 6. Khi AutoCAD rảnh trở lại (main thread), nhận kết quả và ghi vào bản vẽ.
                EventHandler idleHandler = null;
                idleHandler = (sender, e) =>
                {
                    if (!translateTask.IsCompleted) return;
                    AcApp.Idle -= idleHandler;
                    FinishTranslation(
                        doc,
                        editor,
                        entityRepository,
                        dataList,
                        selectedStyleName,
                        progressWindow,
                        translateTask);
                };
                AcApp.Idle += idleHandler;
            }
            catch (System.Exception ex)
            {
                DiagnosticsTrace.TraceError($"[TRANSLATETEXT] {ex}");
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

                // 3. Chọn đối tượng (dùng filter chung để loại entity không hỗ trợ ngay từ đầu)
                PromptSelectionResult selectionResult =
                    TextSelectionInteraction.GetTextSelection(
                        editor,
                        "\nSelect Text/MText/MLeader/Dimension/Block to Change Style:",
                        includeDimensions: true);
                if (selectionResult.Status != PromptStatus.OK) return;

                // 4. Xử lý — delegate logic sang TextStyleLogic
                var logic = new TextStyleLogic();
                HashSet<ObjectId> processedBlockDefs = new HashSet<ObjectId>();

                using (Transaction transaction = database.TransactionManager.StartTransaction())
                {
                    TextStyleTable textStyleTable = (TextStyleTable)transaction.GetObject(database.TextStyleTableId, OpenMode.ForRead);

                    if (!textStyleTable.Has(targetStyleName))
                    {
                        editor.WriteMessage(
                            $"\n[ChangeTextStyle] Target style \"{targetStyleName}\" no longer exists. Nothing changed.");
                        return;
                    }

                    ObjectId targetStyleId = textStyleTable[targetStyleName];
                    int processedCount = 0;

                    foreach (SelectedObject selectedObject in selectionResult.Value)
                    {
                        Entity entity = transaction.GetObject(selectedObject.ObjectId, OpenMode.ForRead) as Entity;
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
                                    Entity subEntity = transaction.GetObject(subId, OpenMode.ForRead) as Entity;
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
            catch (System.Exception ex)
            {
                DiagnosticsTrace.TraceError($"[CHANGETEXTSTYLE] {ex}");
                editor.WriteMessage($"\n[ChangeTextStyle] Error: {ex.Message}");
            }
        }

        // ========================================================================================
        // HELPERS — Chạy trên main thread của AutoCAD
        // ========================================================================================

        private static int CountUniqueTexts(List<TextEntityData> items)
        {
            var uniqueTexts = new HashSet<string>(StringComparer.Ordinal);
            foreach (TextEntityData item in items)
            {
                if (item != null) uniqueTexts.Add(item.OriginalText ?? string.Empty);
            }
            return uniqueTexts.Count;
        }

        /// <summary>
        /// Nhận kết quả dịch thuật và ghi vào bản vẽ. Chạy trên main thread
        /// (Application.Idle) nên phải khóa document trước khi mở transaction.
        /// </summary>
        private static void FinishTranslation(
            Document doc,
            Editor editor,
            TranslationEntityRepository entityRepository,
            List<TextEntityData> dataList,
            string selectedStyleName,
            TranslateProgressWindow progressWindow,
            Task<TranslationBatchResult> translateTask)
        {
            try
            {
                progressWindow.Close();

                if (translateTask.IsCanceled || IsCancellation(translateTask))
                {
                    editor.WriteMessage("\n[TranslateText] Cancelled.");
                    return;
                }

                if (translateTask.IsFaulted)
                {
                    System.Exception exception = translateTask.Exception?.GetBaseException();
                    DiagnosticsTrace.TraceError($"[TRANSLATETEXT.Apply] {exception}");
                    editor.WriteMessage($"\n[TranslateText] Error: {exception?.Message}");
                    return;
                }

                TranslationBatchResult batchResult = translateTask.Result;

                editor.WriteMessage(
                    $"\nTranslated {batchResult.UniqueTextCount} unique strings for {batchResult.ItemCount} objects.");
                if (batchResult.FailedTextCount > 0)
                {
                    editor.WriteMessage(
                        $"\n[TranslateText] {batchResult.FailedTextCount} unique strings could not be translated.");
                    foreach (string failureMessage in batchResult.FailureMessages)
                        editor.WriteMessage("\n[TranslateText] " + failureMessage);
                }

                using (DocumentLock documentLock = doc.LockDocument())
                using (Transaction transaction = doc.Database.TransactionManager.StartTransaction())
                {
                    ObjectId targetStyleId = ObjectId.Null;
                    if (selectedStyleName != "Keep Original")
                    {
                        TextStyleTable textStyleTable = (TextStyleTable)transaction.GetObject(
                            doc.Database.TextStyleTableId, OpenMode.ForRead);
                        if (textStyleTable.Has(selectedStyleName))
                            targetStyleId = textStyleTable[selectedStyleName];
                    }

                    int successCount = entityRepository.Write(transaction, dataList, targetStyleId);
                    transaction.Commit();
                    editor.WriteMessage($"\nDone! Translated {successCount} items.");
                }
                editor.Regen();
            }
            catch (System.Exception ex)
            {
                DiagnosticsTrace.TraceError($"[TRANSLATETEXT.Apply] {ex}");
                editor.WriteMessage($"\n[TranslateText] Error: {ex.Message}");
            }
        }

        private static bool IsCancellation(Task task)
        {
            return task.Exception?.GetBaseException() is OperationCanceledException;
        }

        /// <summary>
        /// IProgress<int> đẩy callback về UI thread của cửa sổ tiến trình,
        /// không phụ thuộc SynchronizationContext hiện hành của main thread AutoCAD.
        /// </summary>
        private sealed class UiThreadProgress : IProgress<int>
        {
            private readonly Action<int> _report;

            public UiThreadProgress(Action<int> report)
            {
                _report = report ?? throw new ArgumentNullException(nameof(report));
            }

            void IProgress<int>.Report(int value) => _report(value);
        }
    }
}
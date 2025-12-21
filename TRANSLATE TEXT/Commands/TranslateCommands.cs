using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using HoangTam.AutoCAD.Tools.Extensions;
using HoangTam.AutoCAD.Tools.Models;
using HoangTam.AutoCAD.Tools.Services;
using HoangTam.AutoCAD.Tools.UI; // Yêu cầu có Forms.cs trong project
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

[assembly: CommandClass(typeof(HoangTam.AutoCAD.Tools.Commands.TranslateCommands))]

namespace HoangTam.AutoCAD.Tools.Commands
{
    public class TranslateCommands
    {
        // Lưu trạng thái lựa chọn của phiên làm việc
        private static string _lastSource = "auto";
        private static string _lastTarget = "vi";
        private static string _lastStyle = "Keep Original";

        [CommandMethod("TRANSLATETEXT", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public async void TranslateTextCmd()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            try
            {
                // PHẦN 1: Chuẩn bị danh sách TextStyle
                List<string> styles = new List<string> { "Keep Original" };
                using (Transaction tr = doc.TransactionManager.StartTransaction())
                {
                    var table = (TextStyleTable)tr.GetObject(doc.Database.TextStyleTableId, OpenMode.ForRead);
                    foreach (ObjectId id in table)
                    {
                        var rec = (TextStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
                        // Loại bỏ các style hệ thống (thường chứa ký tự |)
                        if (!rec.Name.Contains("|")) styles.Add(rec.Name);
                    }
                    tr.Commit();
                }

                // PHẦN 2: Hiển thị Form cấu hình
                using (var form = new LanguageSelectionForm(_lastSource, _lastTarget, styles, _lastStyle))
                {
                    if (Application.ShowModalDialog(form) != System.Windows.Forms.DialogResult.OK) return;
                    _lastSource = form.SelectedSourceCode;
                    _lastTarget = form.SelectedTargetCode;
                    _lastStyle = form.SelectedTextStyle;
                }

                // PHẦN 3: Lấy đối tượng được chọn
                // Hỗ trợ chọn trước (PickFirst) hoặc chọn sau
                PromptSelectionResult selRes = ed.SelectImplied();
                if (selRes.Status != PromptStatus.OK)
                    selRes = ed.GetSelection();

                if (selRes.Status != PromptStatus.OK) return;

                List<TextEntityData> dataList = new List<TextEntityData>();

                // PHẦN 4: Đọc dữ liệu (Tách biệt logic Đọc để Transaction ngắn nhất có thể)
                using (Transaction tr = doc.TransactionManager.StartTransaction())
                {
                    HashSet<ObjectId> processedDefs = new HashSet<ObjectId>();
                    foreach (ObjectId id in selRes.Value.GetObjectIds())
                    {
                        Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        if (ent is BlockReference blkRef)
                        {
                            // Xử lý Attributes trong Block
                            foreach (ObjectId attId in blkRef.AttributeCollection)
                            {
                                var att = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                                if (att != null && !att.IsConstant)
                                    dataList.Add(new TextEntityData { Id = attId, OriginalText = att.TextString, IsAttribute = true });
                            }

                            // Xử lý Text trong Block Definition (Dịch 1 lần cho tất cả block instance)
                            // Lưu ý: Chỉ nên dùng khi người dùng thực sự muốn dịch nội dung gốc của Block
                            /* ObjectId btrId = blkRef.BlockTableRecord;
                            if (!processedDefs.Contains(btrId)) {
                                processedDefs.Add(btrId);
                                // Code đọc Block Definition ở đây nếu cần...
                            }
                            */
                        }
                        else
                        {
                            string text = ent.GetTextContent();
                            // Chỉ thêm nếu có text và không phải là số thuần túy
                            if (!string.IsNullOrEmpty(text) && !IsNumericOnly(text))
                                dataList.Add(new TextEntityData { Id = id, OriginalText = text, IsAttribute = false });
                        }
                    }
                    tr.Commit();
                }

                if (dataList.Count == 0)
                {
                    ed.WriteMessage("\nNo translatable text found.");
                    return;
                }

                // PHẦN 5: Xử lý Dịch (Bất đồng bộ - Không treo AutoCAD)
                ed.WriteMessage($"\nTranslating {dataList.Count} items... Please wait.");

                // Giới hạn 8 luồng song song để tối ưu tốc độ mà không spam server Google
                using (var sem = new SemaphoreSlim(8))
                {
                    var tasks = dataList.Select(async item =>
                    {
                        // Gọi Service dịch thuật
                        item.ProcessedText = await TranslationService.ProcessAsync(
                            item.OriginalText, _lastSource, _lastTarget, sem);
                    });
                    await Task.WhenAll(tasks);
                }

                // PHẦN 6: Ghi dữ liệu (Mở Transaction mới để ghi)
                int count = 0;
                // Bắt buộc Lock Document khi ghi từ context async
                using (DocumentLock docLock = doc.LockDocument())
                using (Transaction tr = doc.TransactionManager.StartTransaction())
                {
                    ObjectId targetStyleId = ObjectId.Null;
                    // Lấy Style ID nếu người dùng chọn đổi style
                    if (_lastStyle != "Keep Original")
                    {
                        var tst = (TextStyleTable)tr.GetObject(doc.Database.TextStyleTableId, OpenMode.ForRead);
                        if (tst.Has(_lastStyle)) targetStyleId = tst[_lastStyle];
                    }

                    foreach (var item in dataList)
                    {
                        // Chỉ ghi nếu có kết quả dịch và khác text gốc
                        if (string.IsNullOrEmpty(item.ProcessedText) || item.OriginalText == item.ProcessedText) continue;

                        try
                        {
                            if (item.Id.IsErased) continue;
                            Entity ent = tr.GetObject(item.Id, OpenMode.ForWrite) as Entity;
                            ent?.SetTextContent(item.ProcessedText, targetStyleId);
                            count++;
                        }
                        catch { /* Bỏ qua lỗi cục bộ để không dừng cả quá trình */ }
                    }
                    tr.Commit();
                }

                ed.WriteMessage($"\nDone. Translated {count} items successfully.");
                ed.Regen();
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nFatal Error: {ex.Message}");
            }
        }

        // Helper check số thuần túy (VD: "123.45") để không dịch
        private bool IsNumericOnly(string text)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(text, @"^[\d\.\,\-\s]+$");
        }
    }
}
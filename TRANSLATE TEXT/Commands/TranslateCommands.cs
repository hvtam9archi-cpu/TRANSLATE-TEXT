using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using HoangTam.AutoCAD.Tools.Core;
using HoangTam.AutoCAD.Tools.Extensions;
using HoangTam.AutoCAD.Tools.Network;
using HoangTam.AutoCAD.Tools.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

[assembly: CommandClass(typeof(HoangTam.AutoCAD.Tools.Commands.TranslateCommands))]

namespace HoangTam.AutoCAD.Tools.Commands
{
    public class TranslateCommands
    {
        private class TextData
        {
            public ObjectId Id { get; set; }
            public string Original { get; set; }
            public string Translated { get; set; }
        }

        private static string _lastSource = "auto";
        private static string _lastTarget = "vi";
        private static string _lastStyle = "Keep Original";

        [CommandMethod("TRANSLATETEXT", CommandFlags.UsePickSet)]
        public async void TranslateTextCmd()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            try
            {
                List<string> styles = new List<string> { "Keep Original" };
                using (Transaction tr = doc.TransactionManager.StartTransaction())
                {
                    var table = (TextStyleTable)tr.GetObject(doc.Database.TextStyleTableId, OpenMode.ForRead);
                    foreach (ObjectId id in table)
                    {
                        var rec = (TextStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
                        styles.Add(rec.Name);
                    }
                    tr.Commit();
                }

                using (var form = new LanguageSelectionForm(_lastSource, _lastTarget, styles, _lastStyle))
                {
                    if (Application.ShowModalDialog(form) != System.Windows.Forms.DialogResult.OK) return;
                    _lastSource = form.SelectedSourceCode;
                    _lastTarget = form.SelectedTargetCode;
                    _lastStyle = form.SelectedTextStyle;
                }

                var filter = new SelectionFilter(new TypedValue[] {
                    new TypedValue((int)DxfCode.Operator, "<OR"),
                    new TypedValue((int)DxfCode.Start, "TEXT"),
                    new TypedValue((int)DxfCode.Start, "MTEXT"),
                    new TypedValue((int)DxfCode.Start, "MULTILEADER"),
                    new TypedValue((int)DxfCode.Start, "INSERT"),
                    new TypedValue((int)DxfCode.Operator, "OR>")
                });

                PromptSelectionResult selRes = ed.GetSelection(filter);
                if (selRes.Status != PromptStatus.OK) return;

                List<TextData> dataList = new List<TextData>();
                HashSet<ObjectId> processedBlockDefs = new HashSet<ObjectId>();

                using (Transaction tr = doc.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in selRes.Value.GetObjectIds())
                    {
                        Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        if (ent is BlockReference blkRef)
                        {
                            foreach (ObjectId attId in blkRef.AttributeCollection)
                            {
                                var att = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                                if (att != null && !att.IsConstant)
                                    dataList.Add(new TextData { Id = attId, Original = att.TextString });
                            }

                            if (!processedBlockDefs.Contains(blkRef.BlockTableRecord))
                            {
                                processedBlockDefs.Add(blkRef.BlockTableRecord);
                                var btr = (BlockTableRecord)tr.GetObject(blkRef.BlockTableRecord, OpenMode.ForRead);
                                foreach (ObjectId subId in btr)
                                {
                                    var subEnt = tr.GetObject(subId, OpenMode.ForRead) as Entity;
                                    string text = subEnt?.GetTextContent();
                                    if (!string.IsNullOrEmpty(text))
                                        dataList.Add(new TextData { Id = subId, Original = text });
                                }
                            }
                        }
                        else
                        {
                            string text = ent.GetTextContent();
                            if (!string.IsNullOrEmpty(text))
                                dataList.Add(new TextData { Id = id, Original = text });
                        }
                    }
                    tr.Commit();
                }

                ed.WriteMessage($"\nTranslating {dataList.Count} items...");

                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) })
                using (var sem = new SemaphoreSlim(8))
                {
                    var tasks = dataList.Select(async item =>
                    {
                        await sem.WaitAsync();
                        try
                        {
                            item.Translated = await GoogleTranslator.TranslateAsync(client, item.Original, _lastSource, _lastTarget);
                        }
                        finally { sem.Release(); }
                    });
                    await Task.WhenAll(tasks);
                }

                int updatedCount = 0;
                using (DocumentLock docLock = doc.LockDocument())
                using (Transaction tr = doc.TransactionManager.StartTransaction())
                {
                    ObjectId targetStyleId = ObjectId.Null;
                    if (_lastStyle != "Keep Original")
                    {
                        var tst = (TextStyleTable)tr.GetObject(doc.Database.TextStyleTableId, OpenMode.ForRead);
                        if (tst.Has(_lastStyle)) targetStyleId = tst[_lastStyle];
                    }

                    foreach (var item in dataList)
                    {
                        if (string.IsNullOrEmpty(item.Translated) || item.Original == item.Translated) continue;
                        try
                        {
                            Entity ent = tr.GetObject(item.Id, OpenMode.ForWrite) as Entity;
                            ent?.SetTextContent(item.Translated, targetStyleId);
                            updatedCount++;
                        }
                        catch { }
                    }
                    tr.Commit();
                }

                ed.WriteMessage($"\nDone! Translated {updatedCount} objects.");
                ed.Regen();
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nFatal Error: {ex.Message}");
            }
        }
    }
}
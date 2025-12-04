using System.Collections.Generic;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using HoangTam.AutoCAD.Tools.Core;
using HoangTam.AutoCAD.Tools.Extensions;
using HoangTam.AutoCAD.Tools.UI;

[assembly: CommandClass(typeof(HoangTam.AutoCAD.Tools.Commands.StyleCommands))]

namespace HoangTam.AutoCAD.Tools.Commands
{
    public class StyleCommands
    {
        [CommandMethod("CHANGETEXTSTYLE")]
        public void ChangeTextStyle()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                List<string> styleList = new List<string>();
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    TextStyleTable tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                    foreach (ObjectId id in tst)
                    {
                        var tsr = (TextStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
                        if (!tsr.Name.Contains("|")) styleList.Add(tsr.Name);
                    }
                    styleList.Sort();
                    tr.Commit();
                }

                if (styleList.Count == 0) { ed.WriteMessage("\nNo Text Style found!"); return; }

                AppSettings.LoadStyleSettings(out string savedStyle, out int savedTgtIdx, out int savedSrcIdx);

                string targetStyleName;
                EncodingType srcEnc, tgtEnc;
                int selectedTgtIdx, selectedSrcIdx;

                using (var form = new TextStyleForm(styleList, savedStyle, savedTgtIdx, savedSrcIdx))
                {
                    if (Application.ShowModalDialog(form) != System.Windows.Forms.DialogResult.OK) return;
                    targetStyleName = form.TargetStyle;
                    srcEnc = form.SourceEncoding;
                    tgtEnc = form.TargetEncoding;
                    selectedTgtIdx = form.SelectedTargetIndex;
                    selectedSrcIdx = form.SelectedSourceIndex;
                }

                AppSettings.SaveStyleSettings(targetStyleName, selectedTgtIdx, selectedSrcIdx);

                PromptSelectionOptions pso = new PromptSelectionOptions { MessageForAdding = "\nSelect Text/Block to Change Style:" };
                PromptSelectionResult psr = ed.GetSelection(pso);
                if (psr.Status != PromptStatus.OK) return;

                HashSet<ObjectId> processedBlockDefs = new HashSet<ObjectId>();
                HashSet<ObjectId> processedIds = new HashSet<ObjectId>();
                int count = 0;

                using (DocumentLock docLock = doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    ObjectId targetStyleId = ObjectId.Null;
                    TextStyleTable tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                    if (!string.IsNullOrEmpty(targetStyleName) && tst.Has(targetStyleName))
                    {
                        targetStyleId = tst[targetStyleName];
                    }

                    foreach (SelectedObject so in psr.Value)
                    {
                        Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForWrite) as Entity;
                        if (ent == null) continue;

                        if (ent is BlockReference blkRef)
                        {
                            foreach (ObjectId attId in blkRef.AttributeCollection)
                            {
                                var att = tr.GetObject(attId, OpenMode.ForWrite) as Entity;
                                if (ProcessSingleEntity(att, targetStyleId, srcEnc, tgtEnc, processedIds)) count++;
                            }

                            ObjectId btrId = blkRef.BlockTableRecord;
                            if (!processedBlockDefs.Contains(btrId))
                            {
                                processedBlockDefs.Add(btrId);
                                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                                foreach (ObjectId subId in btr)
                                {
                                    var subEnt = tr.GetObject(subId, OpenMode.ForWrite) as Entity;
                                    ProcessSingleEntity(subEnt, targetStyleId, srcEnc, tgtEnc, processedIds);
                                }
                            }
                        }
                        else
                        {
                            if (ProcessSingleEntity(ent, targetStyleId, srcEnc, tgtEnc, processedIds)) count++;
                        }
                    }
                    tr.Commit();
                }

                ed.WriteMessage($"\nDone. Processed {count} items.");
                ed.Regen();
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nError: " + ex.Message);
            }
        }

        private bool ProcessSingleEntity(Entity ent, ObjectId styleId, EncodingType src, EncodingType tgt, HashSet<ObjectId> processedIds)
        {
            if (ent == null || processedIds.Contains(ent.ObjectId)) return false;

            string originalText = ent.GetTextContent();
            if (originalText == null) return false;

            // Fix lỗi ambiguous ContentType ở đây bằng namespace đầy đủ
            if (ent is MText || (ent is MLeader ml && ml.ContentType == Autodesk.AutoCAD.DatabaseServices.ContentType.MTextContent))
            {
                originalText = Regex.Replace(originalText, @"\\[Ff][^;]*;", "");
                originalText = Regex.Replace(originalText, @"\\?U\+([0-9A-Fa-f]{4})",
                    m => ((char)int.Parse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber)).ToString());
            }

            string convertedText = VnCharset.Convert(originalText, src, tgt);
            ent.SetTextContent(convertedText, styleId != ObjectId.Null ? styleId : (ObjectId?)null);

            processedIds.Add(ent.ObjectId);
            return true;
        }
    }
}
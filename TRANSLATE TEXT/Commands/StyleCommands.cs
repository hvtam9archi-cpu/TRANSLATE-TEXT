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
                // 1. Load Styles
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

                // 2. UI
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

                // 3. Selection
                PromptSelectionOptions pso = new PromptSelectionOptions { MessageForAdding = "\nSelect Text/Block to Change Style:" };
                PromptSelectionResult psr = ed.GetSelection(pso);
                if (psr.Status != PromptStatus.OK) return;

                // 4. Process & Write (Combined for speed since no network IO)
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

                    HashSet<ObjectId> processedBlockDefs = new HashSet<ObjectId>();
                    foreach (ObjectId objId in psr.Value.GetObjectIds())
                    {
                        Entity ent = tr.GetObject(objId, OpenMode.ForWrite) as Entity;
                        if (ent == null) continue;

                        if (ent is BlockReference blkRef)
                        {
                            // Process Attributes
                            foreach (ObjectId attId in blkRef.AttributeCollection)
                            {
                                var att = tr.GetObject(attId, OpenMode.ForWrite) as Entity;
                                if (ProcessEntity(att, targetStyleId, srcEnc, tgtEnc)) count++;
                            }
                            // Process Block Def
                            ObjectId btrId = blkRef.BlockTableRecord;
                            if (!processedBlockDefs.Contains(btrId))
                            {
                                processedBlockDefs.Add(btrId);
                                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                                foreach (ObjectId subId in btr)
                                {
                                    var subEnt = tr.GetObject(subId, OpenMode.ForWrite) as Entity;
                                    ProcessEntity(subEnt, targetStyleId, srcEnc, tgtEnc);
                                }
                            }
                        }
                        else
                        {
                            if (ProcessEntity(ent, targetStyleId, srcEnc, tgtEnc)) count++;
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

        private bool ProcessEntity(Entity ent, ObjectId styleId, EncodingType src, EncodingType tgt)
        {
            if (ent == null) return false;

            // Clean MText formatting before conversion
            string originalText = ent.GetTextContent();
            if (originalText == null) return false;

            if (ent is MText || (ent is MLeader ml && ml.ContentType == ContentType.MTextContent))
            {
                originalText = Regex.Replace(originalText, @"\\[Ff][^;]*;", "");
                originalText = Regex.Replace(originalText, @"\\?U\+([0-9A-Fa-f]{4})",
                    m => ((char)int.Parse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber)).ToString());
            }

            string convertedText = VnCharset.Convert(originalText, src, tgt);

            // Chỉ set nếu có thay đổi hoặc cần đổi style
            if (convertedText != originalText || styleId != ObjectId.Null)
            {
                ent.SetTextContent(convertedText, styleId);
                return true;
            }
            return false;
        }
    }
}
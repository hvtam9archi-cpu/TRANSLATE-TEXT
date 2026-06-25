using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using TranslateText.Models;

namespace TranslateText.AutoCad
{
    /// <summary>
    /// Isolates AutoCAD entity traversal and persistence from command orchestration.
    /// Transactions are owned by the caller so no database object escapes its valid scope.
    /// </summary>
    public sealed class TranslationEntityRepository
    {
        public List<TextEntityData> Read(Transaction transaction, IEnumerable<ObjectId> selectedIds)
        {
            var items = new List<TextEntityData>();
            var processedBlockDefinitions = new HashSet<ObjectId>();

            foreach (ObjectId objectId in selectedIds)
            {
                Entity entity = transaction.GetObject(objectId, OpenMode.ForRead) as Entity;
                if (entity == null) continue;

                AddEntity(items, entity, objectId);

                if (!(entity is BlockReference blockReference)) continue;

                foreach (ObjectId attributeId in blockReference.AttributeCollection)
                {
                    var attribute = transaction.GetObject(attributeId, OpenMode.ForRead) as AttributeReference;
                    if (attribute != null && !attribute.IsConstant)
                    {
                        items.Add(new TextEntityData
                        {
                            Id = attributeId,
                            OriginalText = attribute.TextString,
                            IsAttribute = true
                        });
                    }
                }

                ObjectId definitionId = blockReference.BlockTableRecord;
                if (!processedBlockDefinitions.Add(definitionId)) continue;

                var definition = (BlockTableRecord)transaction.GetObject(definitionId, OpenMode.ForRead);
                foreach (ObjectId childId in definition)
                {
                    Entity child = transaction.GetObject(childId, OpenMode.ForRead) as Entity;
                    if (child != null) AddEntity(items, child, childId);
                }
            }

            return items;
        }

        public int Write(Transaction transaction, IEnumerable<TextEntityData> items, ObjectId targetStyleId)
        {
            int successCount = 0;
            foreach (TextEntityData item in items)
            {
                if (string.IsNullOrEmpty(item.ProcessedText) || item.OriginalText == item.ProcessedText)
                    continue;

                Entity entity = transaction.GetObject(item.Id, OpenMode.ForWrite) as Entity;
                if (entity == null) continue;

                if (!Apply(entity, item.ProcessedText, targetStyleId)) continue;
                successCount++;
            }
            return successCount;
        }

        private static void AddEntity(List<TextEntityData> items, Entity entity, ObjectId id)
        {
            if (entity is DBText dbText)
            {
                items.Add(new TextEntityData { Id = id, OriginalText = dbText.TextString });
            }
            else if (entity is MText mText)
            {
                items.Add(new TextEntityData { Id = id, OriginalText = mText.Contents });
            }
            else if (entity is MLeader mLeader && mLeader.ContentType == ContentType.MTextContent)
            {
                items.Add(new TextEntityData { Id = id, OriginalText = mLeader.MText.Contents });
            }
        }

        private static bool Apply(Entity entity, string text, ObjectId targetStyleId)
        {
            if (entity is DBText dbText)
            {
                dbText.TextString = text;
                if (!targetStyleId.IsNull) dbText.TextStyleId = targetStyleId;
                return true;
            }

            if (entity is MText mText)
            {
                mText.Contents = text;
                if (!targetStyleId.IsNull) mText.TextStyleId = targetStyleId;
                return true;
            }

            if (entity is MLeader mLeader)
            {
                MText leaderText = mLeader.MText;
                leaderText.Contents = text;
                if (!targetStyleId.IsNull) leaderText.TextStyleId = targetStyleId;
                mLeader.MText = leaderText;
                return true;
            }

            return false;
        }
    }
}

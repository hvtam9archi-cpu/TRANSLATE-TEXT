using Autodesk.AutoCAD.DatabaseServices;

namespace HoangTam.AutoCAD.Tools.Extensions
{
    public static class EntityExtensions
    {
        // Lấy text từ Entity bất kỳ (chỉ hỗ trợ loại có text)
        public static string GetTextContent(this Entity ent)
        {
            switch (ent)
            {
                // Phải đưa các class con (Derived) lên trước class cha (Base: DBText)
                case AttributeDefinition ad: return ad.TextString;
                case AttributeReference ar: return ar.TextString;
                case DBText t: return t.TextString;

                case MText mt: return mt.Contents;
                // Chỉ định rõ namespace để tránh conflict với System.Net.Mime
                case MLeader ml when ml.ContentType == Autodesk.AutoCAD.DatabaseServices.ContentType.MTextContent: return ml.MText.Contents;
                case Dimension dim: return dim.DimensionText;
                default: return null;
            }
        }

        // Set text và Style (nếu cần)
        public static void SetTextContent(this Entity ent, string content, ObjectId? styleId = null)
        {
            if (content == null) return;

            // Update Style nếu có
            if (styleId.HasValue && styleId.Value != ObjectId.Null)
            {
                try { ((dynamic)ent).TextStyleId = styleId.Value; } catch { }
            }

            // Update Text
            switch (ent)
            {
                // Reorder: Derived types first
                case AttributeDefinition ad: ad.TextString = content; break;
                case AttributeReference ar: ar.TextString = content; break;
                case DBText t: t.TextString = content; break;

                case MText mt: mt.Contents = content; break;
                case MLeader ml:
                    var mText = ml.MText;
                    mText.Contents = content;
                    if (styleId.HasValue) mText.TextStyleId = styleId.Value;
                    ml.MText = mText;
                    break;
                case Dimension dim: dim.DimensionText = content; break;
            }
        }
    }
}
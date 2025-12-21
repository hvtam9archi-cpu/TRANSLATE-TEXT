using Autodesk.AutoCAD.DatabaseServices;

namespace HoangTam.AutoCAD.Tools.Extensions
{
    public static class EntityExtensions
    {
        // Extension method: Lấy nội dung text từ Entity
        public static string GetTextContent(this Entity ent)
        {
            switch (ent)
            {
                case AttributeDefinition ad: return ad.TextString;
                case AttributeReference ar: return ar.TextString;
                case DBText t: return t.TextString;
                case MText mt: return mt.Contents;
                // MLeader có thể chứa MText hoặc Block, chỉ lấy nếu là MText
                case MLeader ml when ml.ContentType == ContentType.MTextContent: return ml.MText.Contents;
                case Dimension dim: return dim.DimensionText; // Text override của Dimension
                default: return null;
            }
        }

        // Extension method: Gán nội dung text cho Entity
        public static void SetTextContent(this Entity ent, string content, ObjectId? styleId = null)
        {
            if (content == null) return;

            // 1. Cập nhật Text Style nếu được yêu cầu
            if (styleId.HasValue && styleId.Value != ObjectId.Null)
            {
                try
                {
                    // Sử dụng dynamic để gán TextStyleId cho các loại đối tượng khác nhau mà không cần cast nhiều lần
                    if (ent is AttributeDefinition || ent is AttributeReference || ent is DBText || ent is MText)
                    {
                        ((dynamic)ent).TextStyleId = styleId.Value;
                    }
                    else if (ent is MLeader ml && ml.ContentType == ContentType.MTextContent)
                    {
                        MText mt = ml.MText;
                        mt.TextStyleId = styleId.Value;
                        ml.MText = mt; // Phải gán lại MText cho MLeader
                    }
                }
                catch { /* Bỏ qua nếu đối tượng không hỗ trợ đổi style */ }
            }

            // 2. Cập nhật nội dung Text
            switch (ent)
            {
                case AttributeDefinition ad: ad.TextString = content; break;
                case AttributeReference ar: ar.TextString = content; break;
                case DBText t: t.TextString = content; break;
                case MText mt: mt.Contents = content; break;
                case MLeader ml:
                    var mText = ml.MText;
                    mText.Contents = content;
                    ml.MText = mText;
                    break;
                case Dimension dim: dim.DimensionText = content; break;
            }
        }
    }
}
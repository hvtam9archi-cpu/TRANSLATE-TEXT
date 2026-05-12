using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;

namespace TranslateText.Models
{
    /// <summary>
    /// Model lưu trữ kết quả Masking text (bảo vệ mã định dạng AutoCAD)
    /// </summary>
    public class MaskResult
    {
        public string MaskedText { get; set; }
        public List<string> Codes { get; set; } = new List<string>();
    }

    /// <summary>
    /// Model lưu trữ dữ liệu Text entity để xử lý dịch thuật
    /// Decouple: Giảm thiểu sự phụ thuộc vào Transaction
    /// </summary>
    public class TextEntityData
    {
        public ObjectId Id { get; set; }
        public string OriginalText { get; set; }
        public string ProcessedText { get; set; }
        public bool IsAttribute { get; set; }
        public string Handle { get; set; }
    }

    /// <summary>
    /// Model cho mỗi ngôn ngữ trong danh sách chọn
    /// </summary>
    public class LanguageItem
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public override string ToString() => Name;
    }
}
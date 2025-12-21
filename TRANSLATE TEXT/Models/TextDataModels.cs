using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;

namespace HoangTam.AutoCAD.Tools.Models
{
    /// <summary>
    /// Model lưu trữ kết quả Masking text (bảo vệ mã định dạng)
    /// </summary>
    public class MaskResult
    {
        public string MaskedText { get; set; }
        public List<string> Codes { get; set; } = new List<string>();
    }

    /// <summary>
    /// Model lưu trữ dữ liệu Text của entity để xử lý
    /// Decouple: Giảm thiểu sự phụ thuộc trực tiếp vào Transaction khi xử lý Logic
    /// </summary>
    public class TextEntityData
    {
        public ObjectId Id { get; set; }
        public string OriginalText { get; set; }
        public string ProcessedText { get; set; }
        public bool IsAttribute { get; set; }
        // Handle string để log nếu cần thiết mà không giữ ObjectId reference lâu
        public string Handle { get; set; }
    }
}
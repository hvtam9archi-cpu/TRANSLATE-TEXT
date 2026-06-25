namespace TranslateText
{
    /// <summary>
    /// Undo được quản lý tự động bởi Transaction.Commit() trong AutoCAD.
    /// Class này giữ để tương thích, các phương thức hiện là no-op.
    /// </summary>
    public static class UndoHelper
    {
        public static void Begin(Autodesk.AutoCAD.ApplicationServices.Document doc) { }
        public static void End(Autodesk.AutoCAD.ApplicationServices.Document doc) { }
    }
}

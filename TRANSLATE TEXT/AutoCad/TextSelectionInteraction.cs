using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace TranslateText.AutoCad
{
    /// <summary>
    /// Owns command-line selection prompts for text-related commands.
    /// </summary>
    internal static class TextSelectionInteraction
    {
        public static PromptSelectionResult GetTextSelection(
            Editor editor,
            string message,
            bool includeDimensions)
        {
            if (editor == null) throw new ArgumentNullException(nameof(editor));

            var filterValues = new List<TypedValue>
            {
                new TypedValue((int)DxfCode.Operator, "<OR"),
                new TypedValue((int)DxfCode.Start, "TEXT"),
                new TypedValue((int)DxfCode.Start, "MTEXT"),
                new TypedValue((int)DxfCode.Start, "ATTRIB"),
                new TypedValue((int)DxfCode.Start, "ATTDEF"),
                new TypedValue((int)DxfCode.Start, "MULTILEADER"),
                new TypedValue((int)DxfCode.Start, "INSERT")
            };
            if (includeDimensions)
                filterValues.Add(new TypedValue((int)DxfCode.Start, "DIMENSION"));
            filterValues.Add(new TypedValue((int)DxfCode.Operator, "OR>"));

            var options = new PromptSelectionOptions
            {
                MessageForAdding = message
            };
            return editor.GetSelection(options, new SelectionFilter(filterValues.ToArray()));
        }
    }
}

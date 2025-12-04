using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using HoangTam.AutoCAD.Tools.Core;

namespace HoangTam.AutoCAD.Tools.UI
{
    public class LanguageItem
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public override string ToString() => Name;
    }

    public static class LanguageList
    {
        public static List<LanguageItem> GetSupportedLanguages()
        {
            return new List<LanguageItem>
            {
                new LanguageItem { Code = "auto", Name = "Auto Detect" },
                new LanguageItem { Code = "vi", Name = "Vietnamese (Tiếng Việt)" },
                new LanguageItem { Code = "en", Name = "English" },
                new LanguageItem { Code = "ko", Name = "Korean (Hàn Quốc)" },
                new LanguageItem { Code = "ja", Name = "Japanese (Nhật Bản)" },
                new LanguageItem { Code = "zh-CN", Name = "Chinese Simplified (Trung Giản thể)" },
                new LanguageItem { Code = "zh-TW", Name = "Chinese Traditional (Trung Phồn thể)" },
                new LanguageItem { Code = "fr", Name = "French (Pháp)" },
                new LanguageItem { Code = "de", Name = "German (Đức)" },
                new LanguageItem { Code = "ru", Name = "Russian (Nga)" },
                new LanguageItem { Code = "es", Name = "Spanish (Tây Ban Nha)" },
                new LanguageItem { Code = "th", Name = "Thai (Thái Lan)" }
            };
        }
    }

    public class LanguageSelectionForm : Form
    {
        public string SelectedSourceCode { get; private set; }
        public string SelectedTargetCode { get; private set; }
        public string SelectedTextStyle { get; private set; }

        private readonly ComboBox cbSource, cbTarget, cbStyle;

        public LanguageSelectionForm(string defaultSource, string defaultTarget, List<string> styleList, string defaultStyle)
        {
            this.Text = "Translate Tool (Pro)";
            this.Size = new Size(400, 260);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false; this.MinimizeBox = false;

            int pad = 20, lblW = 100, cbW = 230, top = 20;

            this.Controls.Add(new Label { Text = "Source Lang:", Left = pad, Top = top, Width = lblW });
            cbSource = CreateLangCombo(pad + lblW, top - 3, cbW, true);
            SetComboValue(cbSource, defaultSource);
            this.Controls.Add(cbSource);

            top += 40;
            this.Controls.Add(new Label { Text = "Target Lang:", Left = pad, Top = top, Width = lblW });
            cbTarget = CreateLangCombo(pad + lblW, top - 3, cbW, false);
            SetComboValue(cbTarget, defaultTarget);
            this.Controls.Add(cbTarget);

            top += 40;
            this.Controls.Add(new Label { Text = "Text Style:", Left = pad, Top = top, Width = lblW });
            cbStyle = new ComboBox { Left = pad + lblW, Top = top - 3, Width = cbW, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (string s in styleList) cbStyle.Items.Add(s);
            if (cbStyle.Items.Contains(defaultStyle)) cbStyle.SelectedItem = defaultStyle;
            else if (cbStyle.Items.Count > 0) cbStyle.SelectedIndex = 0;
            this.Controls.Add(cbStyle);

            top += 50;
            Button btnOk = new Button { Text = "Translate", Left = 130, Top = top, DialogResult = DialogResult.OK, Width = 100 };
            btnOk.Click += (s, e) => {
                SelectedSourceCode = ((LanguageItem)cbSource.SelectedItem).Code;
                SelectedTargetCode = ((LanguageItem)cbTarget.SelectedItem).Code;
                SelectedTextStyle = cbStyle.SelectedItem.ToString();
            };

            Button btnCancel = new Button { Text = "Cancel", Left = 240, Top = top, DialogResult = DialogResult.Cancel, Width = 100 };

            this.Controls.AddRange(new Control[] { btnOk, btnCancel });
            this.AcceptButton = btnOk; this.CancelButton = btnCancel;
        }

        private ComboBox CreateLangCombo(int left, int top, int width, bool includeAuto)
        {
            var cb = new ComboBox { Left = left, Top = top, Width = width, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var lang in LanguageList.GetSupportedLanguages())
            {
                if (!includeAuto && lang.Code == "auto") continue;
                cb.Items.Add(lang);
            }
            return cb;
        }

        private void SetComboValue(ComboBox cb, string code)
        {
            foreach (LanguageItem item in cb.Items)
                if (item.Code == code) { cb.SelectedItem = item; return; }
            if (cb.Items.Count > 0) cb.SelectedIndex = 0;
        }
    }

    public class TextStyleForm : Form
    {
        public string TargetStyle { get; private set; }
        public EncodingType TargetEncoding { get; private set; }
        public EncodingType SourceEncoding { get; private set; }
        public int SelectedTargetIndex => cbTargetEncoding.SelectedIndex;
        public int SelectedSourceIndex => cbSourceEncoding.SelectedIndex;

        private readonly ComboBox cbTargetStyle, cbTargetEncoding, cbSourceEncoding;

        public TextStyleForm(List<string> styleNames, string savedStyle, int savedTgtIdx, int savedSrcIdx)
        {
            this.Text = "Change Text Style (Combined)";
            this.Size = new Size(380, 260);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            int lblW = 110, cbW = 200, left = 130, gap = 40, top = 20;

            this.Controls.Add(new Label { Text = "Target Style:", Left = 20, Top = top, Width = lblW });
            cbTargetStyle = new ComboBox { Left = left, Top = top - 2, Width = cbW, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var s in styleNames) cbTargetStyle.Items.Add(s);
            int idx = cbTargetStyle.FindStringExact(savedStyle);
            cbTargetStyle.SelectedIndex = idx != -1 ? idx : (cbTargetStyle.Items.Count > 0 ? 0 : -1);
            this.Controls.Add(cbTargetStyle);

            top += gap;
            this.Controls.Add(new Label { Text = "Target Encoding:", Left = 20, Top = top, Width = lblW });
            cbTargetEncoding = new ComboBox { Left = left, Top = top - 2, Width = cbW, DropDownStyle = ComboBoxStyle.DropDownList };
            cbTargetEncoding.Items.AddRange(new object[] { "Unicode (Default)", "VNI Windows", "TCVN3 (ABC)" });
            cbTargetEncoding.SelectedIndex = (savedTgtIdx >= 0 && savedTgtIdx < 3) ? savedTgtIdx : 0;
            this.Controls.Add(cbTargetEncoding);

            top += gap;
            this.Controls.Add(new Label { Text = "Source Encoding:", Left = 20, Top = top, Width = lblW });
            cbSourceEncoding = new ComboBox { Left = left, Top = top - 2, Width = cbW, DropDownStyle = ComboBoxStyle.DropDownList };
            cbSourceEncoding.Items.AddRange(new object[] { "Auto Detect", "Unicode", "VNI Windows", "TCVN3 (ABC)" });
            cbSourceEncoding.SelectedIndex = (savedSrcIdx >= 0 && savedSrcIdx < 4) ? savedSrcIdx : 0;
            this.Controls.Add(cbSourceEncoding);

            top += gap + 15;
            Button btnOk = new Button { Text = "OK", Left = left, Top = top, Width = 90, DialogResult = DialogResult.OK };
            btnOk.Click += (s, e) =>
            {
                TargetStyle = cbTargetStyle.SelectedItem?.ToString();
                TargetEncoding = (EncodingType)(cbTargetEncoding.SelectedIndex + 1);
                SourceEncoding = (EncodingType)cbSourceEncoding.SelectedIndex;
            };

            Button btnCancel = new Button { Text = "Cancel", Left = left + 110, Top = top, Width = 90, DialogResult = DialogResult.Cancel };
            this.Controls.AddRange(new Control[] { btnOk, btnCancel });
            this.AcceptButton = btnOk; this.CancelButton = btnCancel;
        }
    }
}
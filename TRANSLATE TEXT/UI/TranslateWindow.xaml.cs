using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using TranslateText.Models;

namespace TranslateText.UI
{
    public partial class TranslateWindow : Window
    {
        public string SelectedSourceCode { get; private set; }
        public string SelectedTargetCode { get; private set; }
        public string SelectedTextStyle { get; private set; }
        public TranslateText.Core.TextCaseOption SelectedTextCase { get; private set; }
        public bool IsConfirmed { get; private set; }

        public TranslateWindow(string defaultSource, string defaultTarget, List<string> styleList, string defaultStyle, TranslateText.Core.TextCaseOption defaultTextCase)
        {
            InitializeComponent();
            if (PluginImageLoader.TryLoad(
                "Resource/IconRibbon_TranslateText_32px.ico",
                out System.Windows.Media.ImageSource icon))
            {
                imgIcon.Source = icon;
            }

            // Populate Source Language
            var languages = LanguageList.GetSupportedLanguages();
            foreach (var lang in languages)
            {
                cbSource.Items.Add(lang);
            }
            SetComboValue(cbSource, defaultSource);

            // Populate Target Language (Loại bỏ "auto")
            foreach (var lang in languages)
            {
                if (lang.Code != "auto")
                {
                    cbTarget.Items.Add(lang);
                }
            }
            SetComboValue(cbTarget, defaultTarget);

            // Populate Text Styles
            foreach (string style in styleList)
            {
                cbStyle.Items.Add(style);
            }
            if (cbStyle.Items.Contains(defaultStyle))
                cbStyle.SelectedItem = defaultStyle;
            else if (cbStyle.Items.Count > 0)
                cbStyle.SelectedIndex = 0;

            // Populate Text Case
            cbTextCase.Items.Add("Keep Original (None)");
            cbTextCase.Items.Add("Sentence case");
            cbTextCase.Items.Add("lowercase");
            cbTextCase.Items.Add("UPPERCASE");
            cbTextCase.Items.Add("Title Case");
            cbTextCase.Items.Add("tOGGLE cASE");
            cbTextCase.SelectedIndex = (int)defaultTextCase;
        }

        private void SetComboValue(System.Windows.Controls.ComboBox cb, string code)
        {
            foreach (var item in cb.Items)
            {
                if (item is LanguageItem lang && lang.Code == code)
                {
                    cb.SelectedItem = item;
                    return;
                }
            }
            if (cb.Items.Count > 0) cb.SelectedIndex = 0;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            Close();
        }

        private void BtnTranslate_Click(object sender, RoutedEventArgs e)
        {
            if (cbSource.SelectedItem is LanguageItem sourceLang)
                SelectedSourceCode = sourceLang.Code;

            if (cbTarget.SelectedItem is LanguageItem targetLang)
                SelectedTargetCode = targetLang.Code;

            SelectedTextStyle = cbStyle.SelectedItem?.ToString() ?? "Keep Original";
            SelectedTextCase = (TranslateText.Core.TextCaseOption)cbTextCase.SelectedIndex;
            IsConfirmed = true;
            Close();
        }
    }
}

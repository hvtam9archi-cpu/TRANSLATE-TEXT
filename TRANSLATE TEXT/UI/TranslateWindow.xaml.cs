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
        public bool IsConfirmed { get; private set; }

        public TranslateWindow(string defaultSource, string defaultTarget, List<string> styleList, string defaultStyle)
        {
            InitializeComponent();

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

            // Apply ComboBox styling
            ApplyComboBoxStyle(cbSource);
            ApplyComboBoxStyle(cbTarget);
            ApplyComboBoxStyle(cbStyle);
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

        private void ApplyComboBoxStyle(System.Windows.Controls.ComboBox cb)
        {
            cb.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2B2D32"));
            cb.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E8EAED"));
            cb.BorderBrush = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#333640"));
            cb.FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
            cb.FontSize = 12;
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
            IsConfirmed = true;
            Close();
        }
    }
}

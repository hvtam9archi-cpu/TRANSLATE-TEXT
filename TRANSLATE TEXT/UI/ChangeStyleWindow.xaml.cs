using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using TranslateText.Core;

namespace TranslateText.UI
{
    public partial class ChangeStyleWindow : Window
    {
        public string TargetStyle { get; private set; }
        public EncodingType TargetEncoding { get; private set; }
        public EncodingType SourceEncoding { get; private set; }
        public int SelectedTargetIndex => cbTargetEncoding.SelectedIndex;
        public int SelectedSourceIndex => cbSourceEncoding.SelectedIndex;
        public TranslateText.Core.TextCaseOption SelectedTextCase { get; private set; }
        public bool IsConfirmed { get; private set; }

        public ChangeStyleWindow(List<string> styleNames, string savedStyle, int savedTgtIdx, int savedSrcIdx, TranslateText.Core.TextCaseOption defaultTextCase)
        {
            InitializeComponent();
            LoadWindowIcon("IconRibbon_ChangeText_32px.ico");

            // Populate Target Style
            foreach (var styleName in styleNames)
            {
                cbTargetStyle.Items.Add(styleName);
            }
            if (cbTargetStyle.Items.Count > 0)
            {
                int idx = -1;
                if (!string.IsNullOrEmpty(savedStyle))
                    idx = cbTargetStyle.Items.IndexOf(savedStyle);
                cbTargetStyle.SelectedIndex = idx != -1 ? idx : 0;
            }

            // Populate Target Encoding
            cbTargetEncoding.Items.Add("Unicode (Default)");
            cbTargetEncoding.Items.Add("VNI Windows");
            cbTargetEncoding.Items.Add("TCVN3 (ABC)");
            cbTargetEncoding.SelectedIndex = (savedTgtIdx >= 0 && savedTgtIdx < cbTargetEncoding.Items.Count) ? savedTgtIdx : 0;

            // Populate Source Encoding
            cbSourceEncoding.Items.Add("Auto Detect");
            cbSourceEncoding.Items.Add("Unicode");
            cbSourceEncoding.Items.Add("VNI Windows");
            cbSourceEncoding.Items.Add("TCVN3 (ABC)");
            cbSourceEncoding.SelectedIndex = (savedSrcIdx >= 0 && savedSrcIdx < cbSourceEncoding.Items.Count) ? savedSrcIdx : 0;

            // Populate Text Case
            cbTextCase.Items.Add("Keep Original (None)");
            cbTextCase.Items.Add("Sentence case");
            cbTextCase.Items.Add("lowercase");
            cbTextCase.Items.Add("UPPERCASE");
            cbTextCase.Items.Add("Title Case");
            cbTextCase.Items.Add("tOGGLE cASE");
            cbTextCase.SelectedIndex = (int)defaultTextCase;
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

        private void LoadWindowIcon(string iconFileName)
        {
            try
            {
                string assemblyDir = System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                string iconPath = System.IO.Path.Combine(assemblyDir, "Resource", iconFileName);
                if (System.IO.File.Exists(iconPath))
                {
                    var uri = new System.Uri(iconPath, System.UriKind.Absolute);
                    imgIcon.Source = System.Windows.Media.Imaging.BitmapFrame.Create(
                        uri, System.Windows.Media.Imaging.BitmapCreateOptions.None,
                        System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                }
            }
            catch { }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (cbTargetStyle.SelectedItem != null)
                TargetStyle = cbTargetStyle.SelectedItem.ToString();

            // Map Dropdown Index to Enum correctly
            // Index 0 = Unicode(1), Index 1 = VNI(2), Index 2 = TCVN3(3)
            TargetEncoding = (EncodingType)(cbTargetEncoding.SelectedIndex + 1);

            // Source: Index 0 = Auto(0), Index 1 = Unicode(1), Index 2 = VNI(2), Index 3 = TCVN3(3)
            SourceEncoding = (EncodingType)cbSourceEncoding.SelectedIndex;

            SelectedTextCase = (TranslateText.Core.TextCaseOption)cbTextCase.SelectedIndex;

            IsConfirmed = true;
            Close();
        }
    }
}

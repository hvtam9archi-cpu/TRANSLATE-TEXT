using System;
using System.Windows;
using System.Windows.Input;

namespace TranslateText.UI
{
    /// <summary>
    /// Modeless progress window shown while translation runs in the background.
    /// UI-only: no AutoCAD API access here; the command orchestrates everything.
    /// </summary>
    public partial class TranslateProgressWindow : Window
    {
        private int _totalItems;
        private bool _isCancelled;

        public bool IsCancelled => _isCancelled;

        public TranslateProgressWindow(int totalUniqueTexts)
        {
            InitializeComponent();
            _totalItems = Math.Max(totalUniqueTexts, 1);

            if (PluginImageLoader.TryLoad(
                "Resource/IconRibbon_TranslateText_32px.ico",
                out System.Windows.Media.ImageSource icon))
            {
                imgIcon.Source = icon;
            }

            progressBar.Maximum = _totalItems;
            progressBar.Value = 0;
            tbStatus.Text = $"Translating 0/{_totalItems}...";
        }

        /// <summary>
        /// Called from the UI thread (via Progress<int>) after each unique string finishes.
        /// </summary>
        public void ReportCompleted(int completedCount)
        {
            if (_isCancelled) return;
            progressBar.Value = Math.Min(completedCount, _totalItems);
            tbStatus.Text = $"Translating {completedCount}/{_totalItems}...";
        }

        public void SetFinished(string message)
        {
            tbStatus.Text = message;
            btnCancel.IsEnabled = false;
            btnClose.IsEnabled = false;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _isCancelled = true;
            btnCancel.IsEnabled = false;
            tbStatus.Text = "Cancelling...";
            Close();
        }
    }
}
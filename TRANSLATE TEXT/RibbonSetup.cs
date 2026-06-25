using System;
using System.IO;
using System.Reflection;
using System.Windows.Input;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;

namespace TranslateText
{
    /// <summary>
    /// Tự động chèn Ribbon Panel "Text Tools" vào Tab chung "TH Tools".
    /// Nếu Tab đã tồn tại (do plugin khác tạo), sẽ tái sử dụng — không tạo mới.
    /// Implements IExtensionApplication để AutoCAD tự gọi Initialize() khi load DLL.
    /// </summary>
    public class RibbonSetup : IExtensionApplication
    {
        private const string TabId = "TH_TOOLS_TAB";
        private const string TabTitle = "TH Tools";
        private const string PanelId = "TRANSLATE_TEXT_PANEL";

        public void Initialize()
        {
            // Đăng ký Idle để chờ Ribbon sẵn sàng
            Application.Idle += Application_Idle;
            // Đăng ký SystemVariableChanged để bắt đổi Workspace → vẽ lại Ribbon
            Application.SystemVariableChanged += Application_SystemVariableChanged;
        }

        public void Terminate()
        {
            // Hủy đăng ký event khi unload plugin
            Application.Idle -= Application_Idle;
            Application.SystemVariableChanged -= Application_SystemVariableChanged;
        }

        private void Application_Idle(object sender, EventArgs e)
        {
            // Chỉ gọi CreateRibbon khi Ribbon đã sẵn sàng
            if (ComponentManager.Ribbon != null)
            {
                Application.Idle -= Application_Idle;
                CreateRibbon();
            }
        }

        private void Application_SystemVariableChanged(object sender, SystemVariableChangedEventArgs e)
        {
            // Khi đổi Workspace (WSCURRENT), vẽ lại Ribbon nếu cần
            if (e.Name.Equals("WSCURRENT", StringComparison.OrdinalIgnoreCase)
                && ComponentManager.Ribbon != null)
            {
                CreateRibbon();
            }
        }

        private void CreateRibbon()
        {
            try
            {
                RibbonControl ribbon = ComponentManager.Ribbon;
                if (ribbon == null) return;

                // 1. Tìm hoặc Tạo Tab "TH Tools" (chia sẻ với các plugin khác)
                RibbonTab tab = ribbon.FindTab(TabId);
                if (tab == null)
                {
                    tab = new RibbonTab { Title = TabTitle, Id = TabId };
                    ribbon.Tabs.Add(tab);
                }

                // 2. Kiểm tra Panel đã tồn tại chưa (tránh duplicate khi NETLOAD lại hoặc đổi Workspace)
                foreach (RibbonPanel existingPanel in tab.Panels)
                {
                    if (existingPanel.Source.Id == PanelId)
                        return; // Panel đã có, không cần tạo lại
                }

                // 3. Tạo Panel "Text Tools"
                RibbonPanelSource panelSource = new RibbonPanelSource { Title = "Text Tools", Id = PanelId };
                RibbonPanel panel = new RibbonPanel { Source = panelSource };

                var commandHandler = new TranslateRibbonCommandHandler();

                // 4. Button "Translate Text" — Nút lớn (Large) với icon từ file .ico
                RibbonButton btnTranslate = new RibbonButton
                {
                    Text = "\nTranslate\nText",
                    ShowText = true,
                    ShowImage = true,
                    Size = RibbonItemSize.Large,
                    Orientation = System.Windows.Controls.Orientation.Vertical,
                    LargeImage = LoadRibbonIcon(32, "IconRibbon_TranslateText_32px.ico"),
                    Image = LoadRibbonIcon(16, "IconRibbon_TranslateText_32px.ico"),
                    CommandParameter = "TRANSLATETEXT",
                    CommandHandler = commandHandler
                };

                // 5. Button "Change Style" — Nút lớn (Large) với icon
                RibbonButton btnChangeStyle = new RibbonButton
                {
                    Text = "\nChange\nStyle",
                    ShowText = true,
                    ShowImage = true,
                    Size = RibbonItemSize.Large,
                    Orientation = System.Windows.Controls.Orientation.Vertical,
                    LargeImage = LoadRibbonIcon(32, "IconRibbon_ChangeText_32px.ico"),
                    Image = LoadRibbonIcon(16, "IconRibbon_ChangeText_32px.ico"),
                    CommandParameter = "CHANGETEXTSTYLE",
                    CommandHandler = commandHandler
                };

                // 6. Thêm các button vào Panel
                panelSource.Items.Add(btnTranslate);
                panelSource.Items.Add(btnChangeStyle);

                tab.Panels.Add(panel);
                tab.IsActive = true;
            }
            catch (System.Exception ex)
            {
                Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                    $"\n[TranslateText] Error loading ribbon: {ex.Message}\n");
            }
        }

        /// <summary>
        /// Load icon từ file .ico trong thư mục Resource (cạnh DLL).
        /// Dùng cho Ribbon, Command Line và Dynamic Input.
        /// </summary>
        private System.Windows.Media.ImageSource LoadRibbonIcon(int size, string iconFileName = "IconRibbon_TranslateText_32px.ico")
        {
            try
            {
                string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string iconPath = Path.Combine(assemblyDir, "Resource", iconFileName);

                if (File.Exists(iconPath))
                {
                    var uri = new Uri(iconPath, UriKind.Absolute);
                    var icon = System.Windows.Media.Imaging.BitmapFrame.Create(
                        uri, System.Windows.Media.Imaging.BitmapCreateOptions.None,
                        System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                    return icon;
                }
            }
            catch
            {
                // Fallback: nếu không load được icon, dùng icon sinh tự động
            }

            // Fallback text dựa trên tên icon
            string fallbackText = iconFileName.Contains("Change", StringComparison.OrdinalIgnoreCase) ? "CS" : "TT";
            return GenerateIcon(fallbackText, size);
        }

        /// <summary>
        /// Tạo icon WPF bằng DrawingVisual (không cần file ảnh ngoài).
        /// Sử dụng Accent Color (#2563EB) làm nền, chữ trắng Bold.
        /// </summary>
        private System.Windows.Media.ImageSource GenerateIcon(string text, int size)
        {
            var visual = new System.Windows.Media.DrawingVisual();
            using (var drawingContext = visual.RenderOpen())
            {
                // Nền bo góc với màu Accent
                var accentBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(37, 99, 235));

                // Vẽ nền
                drawingContext.DrawRoundedRectangle(
                    accentBrush, null,
                    new System.Windows.Rect(0, 0, size, size),
                    size * 0.15, size * 0.15);

                // Vẽ viền trắng mỏng
                drawingContext.DrawRoundedRectangle(
                    null,
                    new System.Windows.Media.Pen(System.Windows.Media.Brushes.White, size > 20 ? 0.8 : 0.5),
                    new System.Windows.Rect(0.5, 0.5, size - 1, size - 1),
                    size * 0.15, size * 0.15);

                // Chữ trên icon
                double fontSize = size > 20 ? 13 : 8;
                string displayText = text.Length > 2 ? text.Substring(0, 2) : text;

                var formattedText = new System.Windows.Media.FormattedText(
                    displayText,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Windows.FlowDirection.LeftToRight,
                    new System.Windows.Media.Typeface(
                        new System.Windows.Media.FontFamily("Segoe UI"),
                        System.Windows.FontStyles.Normal,
                        System.Windows.FontWeights.Bold,
                        System.Windows.FontStretches.Normal),
                    fontSize,
                    System.Windows.Media.Brushes.White,
                    1.0);

                // Căn giữa text trong icon
                double textX = (size - formattedText.Width) / 2;
                double textY = (size - formattedText.Height) / 2;
                drawingContext.DrawText(formattedText, new System.Windows.Point(textX, textY));
            }

            var renderTarget = new System.Windows.Media.Imaging.RenderTargetBitmap(
                size, size, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            renderTarget.Render(visual);
            return renderTarget;
        }
    }

    /// <summary>
    /// Handler xử lý click Ribbon Button → gửi lệnh đến AutoCAD command line.
    /// </summary>
    public class TranslateRibbonCommandHandler : ICommand
    {
        public bool CanExecute(object parameter) => true;

#pragma warning disable CS0067
        public event EventHandler CanExecuteChanged;
#pragma warning restore CS0067

        public void Execute(object parameter)
        {
            if (parameter is RibbonButton button && button.CommandParameter is string commandName)
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;

                // Hủy lệnh đang chạy (nếu có), sau đó gửi tên lệnh riêng biệt
                // Tách riêng cancel và command name để tránh bị nuốt ký tự đầu
                doc.SendStringToExecute("\x1B\x1B", true, false, false);
                doc.SendStringToExecute(commandName + "\n", true, false, false);
            }
        }
    }
}

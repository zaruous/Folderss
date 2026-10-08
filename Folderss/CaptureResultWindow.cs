using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Folderss.Services;
using Microsoft.Win32;

namespace Folderss
{
    /// <summary>
    /// 캡쳐 결과 팝업(비모달). 열리면 이미지를 클립보드에 복사한다.
    /// <c>저장</c>은 활성 폴더 패널에 <c>캡쳐_yyyyMMdd_HHmmss.png</c>로 바로 저장하고, 옆 <c>▾</c>는 다른 이름·형식(PNG/JPEG/BMP)으로 저장한다.
    /// </summary>
    public sealed class CaptureResultWindow : Window
    {
        private readonly BitmapSource _image;
        private readonly Func<string> _getQuickSaveFolder;
        private readonly Action<string> _onSaved;
        private readonly TextBlock _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis };

        /// <param name="getQuickSaveFolder">빠른 저장 폴더. 저장할 수 없으면 사용자에게 알리고 null.</param>
        /// <param name="onSaved">저장한 파일 경로를 받아 폴더 패널을 갱신한다.</param>
        public CaptureResultWindow(BitmapSource image, Func<string> getQuickSaveFolder, Action<string> onSaved)
        {
            _image = image;
            _getQuickSaveFolder = getQuickSaveFolder;
            _onSaved = onSaved;

            Title = string.Format("캡쳐 결과 — {0} × {1}", image.PixelWidth, image.PixelHeight);
            SizeToContent = SizeToContent.WidthAndHeight;
            MinWidth = 420;
            MinHeight = 160;
            MaxWidth = SystemParameters.WorkArea.Width * 0.85;
            MaxHeight = SystemParameters.WorkArea.Height * 0.85;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            UseLayoutRounding = true;
            SetResourceReference(BackgroundProperty, "WindowBackground");
            SetResourceReference(ForegroundProperty, "PrimaryText");
            SetResourceReference(FontFamilyProperty, "AppFontFamily");
            FontSize = 13;

            var save = new Button { Content = "저장", Padding = new Thickness(10, 3, 10, 3), ToolTip = "활성 폴더 패널에 PNG로 바로 저장" };
            save.Click += (s, e) => QuickSave();
            var saveAs = new Button { Content = "▾", Padding = new Thickness(5, 3, 5, 3), MinWidth = 0, Margin = new Thickness(-1, 0, 6, 0), ToolTip = "다른 이름으로 저장… (PNG / JPEG / BMP)" };
            saveAs.Click += (s, e) => SaveAs();
            var copy = new Button { Content = "복사", Padding = new Thickness(10, 3, 10, 3), ToolTip = "클립보드에 다시 복사" };
            copy.Click += (s, e) => Copy();
            var close = new Button { Content = "닫기", Padding = new Thickness(10, 3, 10, 3), IsCancel = true };
            close.Click += (s, e) => Close();
            _status.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");

            var toolbar = new DockPanel { Margin = new Thickness(8), LastChildFill = true };
            DockPanel.SetDock(close, Dock.Right);
            toolbar.Children.Add(close);
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(save);
            left.Children.Add(saveAs);
            left.Children.Add(copy);
            DockPanel.SetDock(left, Dock.Left);
            toolbar.Children.Add(left);
            toolbar.Children.Add(_status);

            var root = new DockPanel();
            DockPanel.SetDock(toolbar, Dock.Top);
            root.Children.Add(toolbar);
            root.Children.Add(new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Image { Source = image, Stretch = System.Windows.Media.Stretch.None, Margin = new Thickness(8, 0, 8, 8) }
            });
            Content = root;

            Loaded += (s, e) => Copy();
        }

        private void Copy()
        {
            var data = new DataObject();
            data.SetImage(_image);
            // 실패(다른 프로그램이 클립보드 점유)는 ClipboardService가 알린다.
            _status.Text = ClipboardService.TrySetDataObject(data) ? "클립보드에 복사했습니다." : "클립보드에 복사하지 못했습니다.";
        }

        private void QuickSave()
        {
            var folder = _getQuickSaveFolder();
            if (folder == null)
                return;
            Save(ScreenCaptureService.NextCapturePath(folder, DateTime.Now, File.Exists));
        }

        private void SaveAs()
        {
            var dialog = new SaveFileDialog
            {
                Title = "캡쳐 저장",
                FileName = Path.GetFileName(ScreenCaptureService.NextCapturePath(string.Empty, DateTime.Now, _ => false)),
                Filter = "PNG 이미지 (*.png)|*.png|JPEG 이미지 (*.jpg)|*.jpg;*.jpeg|BMP 이미지 (*.bmp)|*.bmp",
                DefaultExt = ".png",
                AddExtension = true,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) == true)
                Save(dialog.FileName);
        }

        private void Save(string path)
        {
            var format = ScreenCaptureService.FormatFromPath(path);
            if (format == null)
            {
                MessageBox.Show(this, "PNG, JPG, BMP 확장자로만 저장할 수 있습니다.\n" + path, "캡쳐 저장", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BitmapEncoder encoder;
            switch (format)
            {
                case "jpeg": encoder = new JpegBitmapEncoder { QualityLevel = 95 }; break;
                case "bmp": encoder = new BmpBitmapEncoder(); break;
                default: encoder = new PngBitmapEncoder(); break;
            }
            encoder.Frames.Add(BitmapFrame.Create(_image));

            try
            {
                // 임시 파일에 쓴 뒤 교체 — 실패해도 반쪽 파일이 남거나 덮어쓰려던 기존 파일이 깨지지 않는다.
                SettingsFile.Write(path, temporaryPath =>
                {
                    using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write))
                        encoder.Save(stream);
                });
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is NotSupportedException)
            {
                MessageBox.Show(this, exception.Message + "\n" + path, "캡쳐를 저장하지 못했습니다", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _status.Text = "저장했습니다: " + path;
            _status.ToolTip = path;
            _onSaved?.Invoke(path);
        }
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Folderss.Services;
using Microsoft.Win32;

namespace Folderss
{
    /// <summary>
    /// 캡쳐 결과 팝업(비모달). 열리면 이미지를 클립보드에 복사한다.
    /// <c>저장</c>은 활성 폴더 패널에 <c>캡쳐_yyyyMMdd_HHmmss.png</c>로 바로 저장하고, 옆 <c>▾</c>는 다른 이름·형식(PNG/JPEG/BMP)으로 저장한다.
    /// 둘째 줄은 편집 도구(<see cref="CaptureEditor"/>): 선택(이동·핸들 크기 변경·Delete)·사각형·타원·화살표·텍스트·자르기,
    /// 색·굵기·글자 크기, 크기 조절, 되돌리기(Ctrl+Z).
    /// 저장·복사는 편집 결과를 한 장으로 합친 이미지다. 편집 후 저장·복사 없이 닫으면 확인한다.
    /// </summary>
    public sealed class CaptureResultWindow : Window
    {
        private static readonly Color[] Palette =
        {
            Color.FromRgb(0xE5, 0x39, 0x35), Color.FromRgb(0xFB, 0x8C, 0x00), Color.FromRgb(0xFD, 0xD8, 0x35),
            Color.FromRgb(0x43, 0xA0, 0x47), Color.FromRgb(0x1E, 0x88, 0xE5), Colors.Black, Colors.White
        };

        private readonly CaptureEditor _editor;
        private readonly Func<string> _getQuickSaveFolder;
        private readonly Action<string> _onSaved;
        private readonly TextBlock _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly List<(ToggleButton Button, CaptureTool Tool)> _toolButtons = new List<(ToggleButton, CaptureTool)>();
        private readonly List<(Button Button, Color Color)> _colorButtons = new List<(Button, Color)>();
        private readonly Button _undo;
        private bool _dirty;

        /// <param name="getQuickSaveFolder">빠른 저장 폴더. 저장할 수 없으면 사용자에게 알리고 null.</param>
        /// <param name="onSaved">저장한 파일 경로를 받아 폴더 패널을 갱신한다.</param>
        public CaptureResultWindow(BitmapSource image, Func<string> getQuickSaveFolder, Action<string> onSaved)
        {
            _editor = new CaptureEditor(image) { StrokeColor = Palette[0] };
            _getQuickSaveFolder = getQuickSaveFolder;
            _onSaved = onSaved;

            SizeToContent = SizeToContent.WidthAndHeight;
            MinWidth = 640;
            MinHeight = 200;
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
            var copy = new Button { Content = "복사", Padding = new Thickness(10, 3, 10, 3), ToolTip = "편집 결과를 클립보드에 복사" };
            copy.Click += (s, e) => Copy();
            var close = new Button { Content = "닫기", Padding = new Thickness(10, 3, 10, 3), IsCancel = true };
            close.Click += (s, e) => Close();
            _status.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");

            var toolbar = new DockPanel { Margin = new Thickness(8, 8, 8, 4), LastChildFill = true };
            DockPanel.SetDock(close, Dock.Right);
            toolbar.Children.Add(close);
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(save);
            left.Children.Add(saveAs);
            left.Children.Add(copy);
            DockPanel.SetDock(left, Dock.Left);
            toolbar.Children.Add(left);
            toolbar.Children.Add(_status);

            var tools = new WrapPanel { Margin = new Thickness(8, 0, 8, 8) };
            AddTool(tools, "↖ 선택", CaptureTool.Select, "도형을 눌러 선택 · 끌어서 이동 · 핸들로 크기 변경(화살표는 양 끝, 텍스트는 이동만) · Delete로 삭제");
            AddTool(tools, "▭ 사각형", CaptureTool.Rectangle, "드래그해서 사각형");
            AddTool(tools, "◯ 타원", CaptureTool.Ellipse, "드래그해서 타원");
            AddTool(tools, "↗ 화살표", CaptureTool.Arrow, "시작점에서 끝점으로 드래그");
            AddTool(tools, "T 텍스트", CaptureTool.Text, "클릭한 곳에 글자 입력 · Enter 확정, Shift+Enter 줄바꿈, Esc 취소");
            AddTool(tools, "✂ 자르기", CaptureTool.Crop, "남길 영역을 드래그 (놓으면 바로 잘림, 도형은 위치를 맞춰 남음, 되돌리기 가능)");
            AddSeparator(tools);
            foreach (var color in Palette)
                AddColor(tools, color);
            AddSeparator(tools);
            tools.Children.Add(AddChoice("굵기", new[] { 2, 3, 5, 8 }, 3, value => _editor.StrokeThickness = value));
            tools.Children.Add(AddChoice("글자", new[] { 14, 18, 24, 32, 48 }, 18, value => _editor.TextSize = value));
            AddSeparator(tools);
            var resize = new Button { Content = "크기 조절…", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(2, 0, 2, 0), ToolTip = "이미지 크기를 바꿉니다 (도형·텍스트도 같은 배율로, 그 뒤에도 선택해 고칠 수 있음)" };
            resize.Click += (s, e) => ResizeImage();
            tools.Children.Add(resize);
            _undo = new Button { Content = "↶ 되돌리기", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(2, 0, 2, 0), IsEnabled = false, ToolTip = "되돌리기 (Ctrl+Z)" };
            _undo.Click += (s, e) => _editor.Undo();
            tools.Children.Add(_undo);
            UpdateColorButtons();

            var header = new StackPanel();
            header.Children.Add(toolbar);
            header.Children.Add(tools);
            var root = new DockPanel();
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);
            root.Children.Add(new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Border { Margin = new Thickness(8, 0, 8, 8), Child = _editor, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }
            });
            Content = root;

            _editor.Changed += (s, e) =>
            {
                _dirty = true;
                UpdateTitle();
            };
            UpdateTitle();
            Loaded += (s, e) =>
            {
                Copy();
                _dirty = false;
            };
            PreviewKeyDown += (s, e) =>
            {
                // 텍스트 입력 중의 Ctrl+Z·Delete는 입력 상자 자체 동작에 맡긴다.
                if (Keyboard.FocusedElement is TextBox)
                    return;
                if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    _editor.Undo();
                    e.Handled = true;
                }
                else if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None && _editor.DeleteSelected())
                {
                    e.Handled = true;
                }
            };
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (e.Cancel || !_dirty)
                return;
            var answer = MessageBox.Show(this, "편집한 내용을 저장하거나 복사하지 않았습니다. 그래도 닫을까요?", "캡쳐 결과", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
                e.Cancel = true;
        }

        private void UpdateTitle()
        {
            Title = string.Format("캡쳐 결과 — {0} × {1}", _editor.PixelWidth, _editor.PixelHeight);
            _undo.IsEnabled = _editor.CanUndo;
        }

        private void AddTool(Panel panel, string text, CaptureTool tool, string tip)
        {
            var button = new ToggleButton { Content = new TextBlock { Text = text, Margin = new Thickness(8, 3, 8, 3) }, ToolTip = tip, Margin = new Thickness(1, 0, 1, 0) };
            button.SetResourceReference(StyleProperty, "CompactToggleButtonStyle");
            // 같은 도구를 다시 누르면 꺼진다(도구 없음 = 보기만).
            button.Click += (s, e) => SelectTool(button.IsChecked == true ? tool : CaptureTool.None);
            _toolButtons.Add((button, tool));
            panel.Children.Add(button);
        }

        private void SelectTool(CaptureTool tool)
        {
            _editor.Tool = tool;
            foreach (var item in _toolButtons)
                item.Button.IsChecked = item.Tool == tool;
        }

        private void AddColor(Panel panel, Color color)
        {
            var button = new Button
            {
                Content = new Border { Width = 16, Height = 16, Background = new SolidColorBrush(color), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) },
                BorderThickness = new Thickness(2),
                ToolTip = "도형·텍스트 색"
            };
            button.SetResourceReference(StyleProperty, "CompactToolButtonStyle");
            button.Click += (s, e) =>
            {
                _editor.StrokeColor = color;
                UpdateColorButtons();
            };
            _colorButtons.Add((button, color));
            panel.Children.Add(button);
        }

        private void UpdateColorButtons()
        {
            foreach (var item in _colorButtons)
            {
                if (item.Color == _editor.StrokeColor)
                    item.Button.SetResourceReference(BorderBrushProperty, "AccentBrush");
                else
                    item.Button.BorderBrush = Brushes.Transparent;
            }
        }

        private static FrameworkElement AddChoice(string label, int[] values, int selected, Action<int> apply)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 4, 0) };
            panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            var combo = new ComboBox { ItemsSource = values, SelectedItem = selected, MinWidth = 56, Padding = new Thickness(6, 2, 6, 2) };
            combo.SelectionChanged += (s, e) =>
            {
                if (combo.SelectedItem is int value)
                    apply(value);
            };
            panel.Children.Add(combo);
            return panel;
        }

        private static void AddSeparator(Panel panel)
        {
            var line = new Border { Width = 1, Margin = new Thickness(6, 4, 6, 4) };
            line.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
            panel.Children.Add(line);
        }

        private void ResizeImage()
        {
            var dialog = new CaptureResizeDialog(_editor.PixelWidth, _editor.PixelHeight) { Owner = this };
            if (dialog.ShowDialog() == true && (dialog.NewWidth != _editor.PixelWidth || dialog.NewHeight != _editor.PixelHeight))
                _editor.Resize(dialog.NewWidth, dialog.NewHeight);
        }

        private void Copy()
        {
            var data = new DataObject();
            data.SetImage(_editor.Render());
            // 실패(다른 프로그램이 클립보드 점유)는 ClipboardService가 알린다.
            if (ClipboardService.TrySetDataObject(data))
            {
                _status.Text = "클립보드에 복사했습니다.";
                _dirty = false;
            }
            else
            {
                _status.Text = "클립보드에 복사하지 못했습니다.";
            }
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

            BitmapSource image = _editor.Render();
            BitmapEncoder encoder;
            switch (format)
            {
                case "jpeg":
                    // JPEG에는 알파가 없다 — 편집 결과(Pbgra32)를 24비트로 바꿔 넘긴다.
                    image = new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);
                    encoder = new JpegBitmapEncoder { QualityLevel = 95 };
                    break;
                case "bmp": encoder = new BmpBitmapEncoder(); break;
                default: encoder = new PngBitmapEncoder(); break;
            }
            encoder.Frames.Add(BitmapFrame.Create(image));

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

            _dirty = false;
            _status.Text = "저장했습니다: " + path;
            _status.ToolTip = path;
            _onSaved?.Invoke(path);
        }
    }

    /// <summary>캡쳐 크기 조절: 너비·높이(픽셀)와 비율 유지(기본 켬). 한 변을 고치면 다른 변을 비율대로 맞춘다.</summary>
    public sealed class CaptureResizeDialog : GitDialogBase
    {
        private readonly int _originalWidth;
        private readonly int _originalHeight;
        private readonly TextBox _width;
        private readonly TextBox _height;
        private readonly CheckBox _keepRatio;
        private bool _syncing;

        public int NewWidth { get; private set; }
        public int NewHeight { get; private set; }

        public CaptureResizeDialog(int width, int height) : base("크기 조절", 320)
        {
            _originalWidth = width;
            _originalHeight = height;
            AddText(string.Format("현재 {0} × {1} 픽셀", width, height), secondary: true);
            AddLabel("너비");
            _width = AddNumber(width);
            AddLabel("높이");
            _height = AddNumber(height);
            _keepRatio = AddCheck("비율 유지", true);
            _width.TextChanged += (s, e) => Sync(_width, _height, _originalWidth, _originalHeight);
            _height.TextChanged += (s, e) => Sync(_height, _width, _originalHeight, _originalWidth);
            AddButtons("적용");
            Loaded += (s, e) =>
            {
                _width.Focus();
                _width.SelectAll();
            };
        }

        private TextBox AddNumber(int value)
        {
            var box = new TextBox { Text = value.ToString() };
            Body.Children.Add(box);
            return box;
        }

        private void Sync(TextBox changed, TextBox other, int changedOriginal, int otherOriginal)
        {
            if (_syncing || _keepRatio?.IsChecked != true || !int.TryParse(changed.Text, out var value) || value < 1)
                return;
            _syncing = true;
            other.Text = ScreenCaptureService.ProportionalSide(value, changedOriginal, otherOriginal).ToString();
            _syncing = false;
        }

        protected override Task<string> ValidateAsync()
        {
            if (!int.TryParse(_width.Text, out var width) || !int.TryParse(_height.Text, out var height) ||
                !ScreenCaptureService.IsValidSide(width) || !ScreenCaptureService.IsValidSide(height))
            {
                return Task.FromResult(string.Format("너비와 높이는 1~{0} 사이의 정수여야 합니다.", ScreenCaptureService.MaxImageSide));
            }
            NewWidth = width;
            NewHeight = height;
            return Task.FromResult<string>(null);
        }
    }
}

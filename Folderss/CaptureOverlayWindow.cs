using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Folderss.Services;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Folderss
{
    /// <summary>
    /// 영역 지정 캡쳐 오버레이. 먼저 찍어 둔 전체 화면(<see cref="CaptureVirtualScreen"/>)을 모든 모니터를 덮는 창에 깔고
    /// 반투명 막을 씌운 뒤, 드래그한 영역만 원래 밝기로 보여 준다. 마우스를 놓으면 그 영역을 <see cref="Result"/>로 잘라 닫는다.
    /// Esc·오른쪽 클릭은 취소(<see cref="Result"/> = null).
    /// 정지 화면 위에서 고르므로 오버레이 자신이 찍히지 않고, 자르는 위치는 화면 좌표가 아니라 보이는 그림 기준(비율)이라
    /// 모니터 배율이 달라도 고른 그림 그대로 잘린다. 단, 앱이 시스템 DPI 모드라 배율이 다른 보조 모니터는 Windows가 늘린 해상도로 찍힐 수 있다.
    /// </summary>
    public sealed class CaptureOverlayWindow : Window
    {
        private readonly BitmapSource _screen;
        private readonly Canvas _canvas = new Canvas { Background = Brushes.Transparent };
        private readonly RectangleGeometry _hole = new RectangleGeometry();
        private readonly Rectangle _border = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0x3D, 0x9B, 0xFF)),
            StrokeThickness = 1,
            Visibility = Visibility.Collapsed
        };
        private readonly TextBlock _sizeLabel = new TextBlock
        {
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0)),
            Padding = new Thickness(4, 1, 4, 1),
            FontSize = 12,
            Visibility = Visibility.Collapsed
        };
        private Point _start;
        private bool _dragging;

        public BitmapSource Result { get; private set; }

        public CaptureOverlayWindow(BitmapSource screen)
        {
            _screen = screen;
            Title = "화면 캡쳐";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            // 최대화는 한 모니터만 덮으므로 가상 화면 전체 크기로 직접 놓는다(왼쪽·위쪽 모니터는 음수 좌표).
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
            Background = Brushes.Black;
            Cursor = Cursors.Cross;

            var dim = new Path
            {
                Fill = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)),
                Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, Width, Height)), _hole),
                IsHitTestVisible = false
            };
            var hint = new TextBlock
            {
                Text = "드래그해서 캡쳐할 영역을 고르세요 · Esc 또는 오른쪽 클릭: 취소",
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0)),
                Padding = new Thickness(10, 5, 10, 5),
                FontSize = 13,
                IsHitTestVisible = false
            };
            // 안내문은 주 모니터 위쪽 가운데(가상 화면에서 주 모니터 원점은 0,0).
            hint.Loaded += (s, e) => Canvas.SetLeft(hint, -Left + (SystemParameters.PrimaryScreenWidth - hint.ActualWidth) / 2);
            Canvas.SetTop(hint, -Top + 24);
            _border.IsHitTestVisible = false;
            _sizeLabel.IsHitTestVisible = false;

            _canvas.Children.Add(dim);
            _canvas.Children.Add(_border);
            _canvas.Children.Add(_sizeLabel);
            _canvas.Children.Add(hint);

            var root = new Grid();
            root.Children.Add(new Image { Source = screen, Stretch = Stretch.Fill });
            root.Children.Add(_canvas);
            Content = root;

            Loaded += (s, e) =>
            {
                Activate();
                Keyboard.Focus(this);
            };
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                    Close();
            };
            MouseRightButtonUp += (s, e) => Close();
            _canvas.MouseLeftButtonDown += Canvas_MouseLeftButtonDown;
            _canvas.MouseMove += Canvas_MouseMove;
            _canvas.MouseLeftButtonUp += Canvas_MouseLeftButtonUp;
        }

        /// <summary>
        /// 모든 모니터(가상 화면)를 지금 모습 그대로 찍는다. 이미지 DPI를 시스템 배율로 맞춰 결과 창에서 1:1 픽셀로 보이게 한다.
        /// 잠긴 화면·보안 데스크톱(UAC)에서는 예외가 난다 — 호출하는 쪽이 알린다.
        /// </summary>
        public static BitmapSource CaptureVirtualScreen()
        {
            var bounds = Forms.SystemInformation.VirtualScreen;
            using (var bitmap = new Drawing.Bitmap(bounds.Width, bounds.Height, Drawing.Imaging.PixelFormat.Format32bppRgb))
            {
                using (var graphics = Drawing.Graphics.FromImage(bitmap))
                    graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, Drawing.CopyPixelOperation.SourceCopy);

                var data = bitmap.LockBits(
                    new Drawing.Rectangle(0, 0, bounds.Width, bounds.Height),
                    Drawing.Imaging.ImageLockMode.ReadOnly,
                    Drawing.Imaging.PixelFormat.Format32bppRgb);
                try
                {
                    var dpi = 96.0 * bounds.Width / SystemParameters.VirtualScreenWidth;
                    var source = BitmapSource.Create(
                        bounds.Width, bounds.Height, dpi, dpi, PixelFormats.Bgr32, null,
                        data.Scan0, data.Stride * bounds.Height, data.Stride);
                    source.Freeze();
                    return source;
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }
            }
        }

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _start = e.GetPosition(_canvas);
            _dragging = true;
            _canvas.CaptureMouse();
            UpdateSelection(_start);
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragging)
                UpdateSelection(e.GetPosition(_canvas));
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging)
                return;
            _dragging = false;
            _canvas.ReleaseMouseCapture();

            var end = e.GetPosition(_canvas);
            var rect = ToPixelRect(end);
            if (!ScreenCaptureService.IsSelectable(rect.Width, rect.Height))
            {
                // 클릭만 했거나 너무 작으면 선택을 지우고 다시 고르게 둔다.
                UpdateSelection(_start);
                return;
            }

            var cropped = new CroppedBitmap(_screen, new Int32Rect(rect.X, rect.Y, rect.Width, rect.Height));
            cropped.Freeze();
            Result = cropped;
            Close();
        }

        private (int X, int Y, int Width, int Height) ToPixelRect(Point end)
        {
            return ScreenCaptureService.ToPixelRect(
                _start.X, _start.Y, end.X, end.Y,
                _canvas.ActualWidth, _canvas.ActualHeight, _screen.PixelWidth, _screen.PixelHeight);
        }

        private void UpdateSelection(Point end)
        {
            var area = new Rect(_start, end);
            _hole.Rect = area;
            _border.Width = area.Width;
            _border.Height = area.Height;
            Canvas.SetLeft(_border, area.X);
            Canvas.SetTop(_border, area.Y);
            _border.Visibility = area.Width > 0 && area.Height > 0 ? Visibility.Visible : Visibility.Collapsed;

            var rect = ToPixelRect(end);
            _sizeLabel.Text = rect.Width + " × " + rect.Height;
            Canvas.SetLeft(_sizeLabel, area.X);
            Canvas.SetTop(_sizeLabel, area.Y >= 22 ? area.Y - 22 : area.Bottom + 4);
            _sizeLabel.Visibility = _border.Visibility;
        }
    }
}

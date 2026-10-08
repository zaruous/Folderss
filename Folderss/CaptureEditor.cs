using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Folderss.Services;

namespace Folderss
{
    public enum CaptureTool { None, Rectangle, Ellipse, Arrow, Text, Crop }

    /// <summary>
    /// 캡쳐 결과 편집면. 이미지 위에 도형·텍스트를 WPF 요소로 올려 두고, 저장·복사·자르기·크기 조절 때 한 장으로 합친다(<see cref="Render"/>).
    /// 좌표는 이미지 픽셀 = 편집면 DIP이고, 화면에는 캡쳐 DPI만큼 줄여(LayoutTransform) 실제 픽셀 1:1로 보인다.
    /// 되돌리기는 바꾸기 전 (이미지, 주석 목록)을 쌓는다. 자르기·크기 조절은 주석을 이미지에 합친 뒤 적용한다.
    /// </summary>
    public sealed class CaptureEditor : Grid
    {
        // 합쳐서 내보내는 층: [0] 이미지 + 주석들.
        private readonly Canvas _surface = new Canvas { ClipToBounds = true };
        // 그리는 중인 도형·자르기 틀·텍스트 입력 상자. 내보내지 않는다.
        private readonly Canvas _overlay = new Canvas { Background = Brushes.Transparent, ClipToBounds = true };
        private readonly Image _image = new Image { Stretch = Stretch.Fill };
        private readonly Stack<(BitmapSource Bitmap, UIElement[] Annotations)> _undo = new Stack<(BitmapSource, UIElement[])>();
        private BitmapSource _bitmap;
        private CaptureTool _tool;
        private Shape _preview;
        private Point _start;
        private TextBox _textBox;

        public Color StrokeColor { get; set; } = Colors.Red;
        public double StrokeThickness { get; set; } = 3;
        public double TextSize { get; set; } = 18;

        /// <summary>이미지·주석이 바뀌었다(크기, 되돌리기 가능 여부 갱신용).</summary>
        public event EventHandler Changed;

        public int PixelWidth => _bitmap.PixelWidth;
        public int PixelHeight => _bitmap.PixelHeight;
        public bool CanUndo => _undo.Count > 0;

        public CaptureEditor(BitmapSource bitmap)
        {
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
            // 1:1 표시와 합치기에서 다시 샘플링하지 않도록.
            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
            var scale = bitmap.DpiX > 0 ? 96.0 / bitmap.DpiX : 1.0;
            LayoutTransform = new ScaleTransform(scale, scale);

            _surface.Children.Add(_image);
            Children.Add(_surface);
            Children.Add(_overlay);
            SetBitmap(bitmap);

            _overlay.MouseLeftButtonDown += Overlay_MouseLeftButtonDown;
            _overlay.MouseMove += Overlay_MouseMove;
            _overlay.MouseLeftButtonUp += Overlay_MouseLeftButtonUp;
            _overlay.LostMouseCapture += (s, e) => CancelDrag();
        }

        public CaptureTool Tool
        {
            get { return _tool; }
            set
            {
                CommitText();
                CancelDrag();
                _tool = value;
                _overlay.Cursor = value == CaptureTool.None ? null : value == CaptureTool.Text ? Cursors.IBeam : Cursors.Cross;
            }
        }

        /// <summary>이미지와 주석을 한 장으로 합친다(입력 중인 텍스트는 먼저 확정). 주석이 없으면 이미지 그대로.</summary>
        public BitmapSource Render()
        {
            CommitText();
            if (_surface.Children.Count == 1)
                return _bitmap;

            _surface.UpdateLayout();
            var bitmap = new RenderTargetBitmap(PixelWidth, PixelHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(_surface);
            bitmap.Freeze();
            return bitmap;
        }

        public void Undo()
        {
            CommitText();
            if (_undo.Count == 0)
                return;
            var state = _undo.Pop();
            SetBitmap(state.Bitmap);
            RemoveAnnotations();
            foreach (var annotation in state.Annotations)
                _surface.Children.Add(annotation);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>주석을 합친 이미지를 <paramref name="width"/>×<paramref name="height"/>로 다시 그린다(고품질 보간).</summary>
        public void Resize(int width, int height)
        {
            var flat = Render();
            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            using (var context = visual.RenderOpen())
                context.DrawImage(flat, new Rect(0, 0, width, height));
            var resized = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            resized.Render(visual);
            resized.Freeze();
            ReplaceBitmap(resized);
        }

        private void Crop(Point a, Point b)
        {
            var rect = ScreenCaptureService.ToPixelRect(a.X, a.Y, b.X, b.Y, PixelWidth, PixelHeight, PixelWidth, PixelHeight);
            if (!ScreenCaptureService.IsSelectable(rect.Width, rect.Height))
                return;
            var cropped = new CroppedBitmap(Render(), new Int32Rect(rect.X, rect.Y, rect.Width, rect.Height));
            cropped.Freeze();
            ReplaceBitmap(cropped);
        }

        private void ReplaceBitmap(BitmapSource bitmap)
        {
            PushUndo();
            RemoveAnnotations();
            SetBitmap(bitmap);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void AddAnnotation(UIElement annotation)
        {
            PushUndo();
            _surface.Children.Add(annotation);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void PushUndo()
        {
            _undo.Push((_bitmap, _surface.Children.Cast<UIElement>().Skip(1).ToArray()));
        }

        private void RemoveAnnotations()
        {
            while (_surface.Children.Count > 1)
                _surface.Children.RemoveAt(1);
        }

        private void SetBitmap(BitmapSource bitmap)
        {
            _bitmap = bitmap;
            _image.Source = bitmap;
            _image.Width = _surface.Width = _overlay.Width = bitmap.PixelWidth;
            _image.Height = _surface.Height = _overlay.Height = bitmap.PixelHeight;
        }

        private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 입력 상자 안 클릭은 상자가 처리한다(IsAncestorOf는 Visual이 아니면 예외라 형식부터 본다).
            if (_textBox != null && e.OriginalSource is Visual source && _textBox.IsAncestorOf(source))
                return;

            var point = e.GetPosition(_overlay);
            if (_tool == CaptureTool.Text)
            {
                // 입력 중이면 바깥 클릭은 확정만 한다(바로 새 입력을 열지 않음).
                if (_textBox != null)
                    CommitText();
                else
                    BeginText(point);
                e.Handled = true;
                return;
            }
            if (_tool == CaptureTool.None)
                return;

            _start = point;
            _preview = CreatePreview();
            _overlay.Children.Add(_preview);
            UpdatePreview(point);
            _overlay.CaptureMouse();
            e.Handled = true;
        }

        private void Overlay_MouseMove(object sender, MouseEventArgs e)
        {
            if (_preview != null)
                UpdatePreview(e.GetPosition(_overlay));
        }

        private void Overlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var preview = _preview;
            if (preview == null)
                return;
            // 캡처 해제가 LostMouseCapture → CancelDrag를 부르므로 먼저 비운다.
            _preview = null;
            _overlay.ReleaseMouseCapture();
            _overlay.Children.Remove(preview);

            var end = e.GetPosition(_overlay);
            if (_tool == CaptureTool.Crop)
            {
                Crop(_start, end);
                return;
            }

            var min = ScreenCaptureService.MinSelectionPixels;
            var tooSmall = _tool == CaptureTool.Arrow
                ? (end - _start).Length < min
                : Math.Abs(end.X - _start.X) < min || Math.Abs(end.Y - _start.Y) < min;
            if (!tooSmall)
                AddAnnotation(preview);
        }

        private void CancelDrag()
        {
            if (_preview == null)
                return;
            _overlay.Children.Remove(_preview);
            _preview = null;
        }

        private Shape CreatePreview()
        {
            var brush = new SolidColorBrush(StrokeColor);
            brush.Freeze();
            switch (_tool)
            {
                case CaptureTool.Ellipse:
                    return new Ellipse { Stroke = brush, StrokeThickness = StrokeThickness };
                case CaptureTool.Arrow:
                    return new Path
                    {
                        Stroke = brush,
                        Fill = brush,
                        StrokeThickness = StrokeThickness,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeLineJoin = PenLineJoin.Round
                    };
                case CaptureTool.Crop:
                    return new Rectangle
                    {
                        Stroke = new SolidColorBrush(Color.FromRgb(0x3D, 0x9B, 0xFF)),
                        StrokeThickness = 1,
                        StrokeDashArray = new DoubleCollection { 4, 3 },
                        Fill = new SolidColorBrush(Color.FromArgb(0x30, 0x3D, 0x9B, 0xFF))
                    };
                default:
                    return new Rectangle { Stroke = brush, StrokeThickness = StrokeThickness };
            }
        }

        private void UpdatePreview(Point end)
        {
            if (_preview is Path path)
            {
                path.Data = ArrowGeometry(_start, end);
                return;
            }
            var rect = new Rect(_start, end);
            Canvas.SetLeft(_preview, rect.X);
            Canvas.SetTop(_preview, rect.Y);
            _preview.Width = rect.Width;
            _preview.Height = rect.Height;
        }

        private Geometry ArrowGeometry(Point start, Point end)
        {
            var head = ScreenCaptureService.ArrowHead(start.X, start.Y, end.X, end.Y, StrokeThickness);
            var tip = new StreamGeometry();
            using (var context = tip.Open())
            {
                context.BeginFigure(end, true, true);
                context.LineTo(new Point(head.LeftX, head.LeftY), true, false);
                context.LineTo(new Point(head.RightX, head.RightY), true, false);
            }
            var group = new GeometryGroup();
            group.Children.Add(new LineGeometry(start, new Point(head.BaseX, head.BaseY)));
            group.Children.Add(tip);
            group.Freeze();
            return group;
        }

        private void BeginText(Point point)
        {
            var brush = new SolidColorBrush(StrokeColor);
            brush.Freeze();
            var box = new TextBox
            {
                Foreground = brush,
                CaretBrush = brush,
                BorderBrush = brush,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0),
                FontSize = TextSize,
                FontWeight = FontWeights.SemiBold,
                MinWidth = 30,
                AcceptsReturn = true,
                VerticalContentAlignment = VerticalAlignment.Top
            };
            Canvas.SetLeft(box, point.X);
            Canvas.SetTop(box, point.Y);
            box.PreviewKeyDown += TextBox_PreviewKeyDown;
            box.LostKeyboardFocus += (s, e) => CommitText();
            box.Loaded += (s, e) => box.Focus();
            _textBox = box;
            _overlay.Children.Add(box);
        }

        private void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Enter 확정, Shift+Enter 줄바꿈(AcceptsReturn), Esc 취소.
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                CommitText();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelText();
                e.Handled = true;
            }
        }

        /// <summary>입력 상자의 글자를 같은 위치·글꼴의 TextBlock으로 바꿔 주석에 넣는다. 빈 글자는 버린다.</summary>
        private void CommitText()
        {
            var box = _textBox;
            if (box == null)
                return;
            // 상자를 치우면 LostKeyboardFocus가 다시 이곳을 부르므로 먼저 비운다.
            _textBox = null;

            TextBlock block = null;
            if (!string.IsNullOrWhiteSpace(box.Text))
            {
                // 첫 글자의 상자 안 위치로 맞춰 확정 전후 글자가 움직이지 않게 한다(테마 TextBox 여백과 무관).
                box.UpdateLayout();
                var first = box.GetRectFromCharacterIndex(0);
                block = new TextBlock
                {
                    Text = box.Text,
                    Foreground = box.Foreground,
                    FontFamily = box.FontFamily,
                    FontSize = box.FontSize,
                    FontWeight = box.FontWeight
                };
                Canvas.SetLeft(block, Canvas.GetLeft(box) + (first.IsEmpty ? 0 : first.X));
                Canvas.SetTop(block, Canvas.GetTop(box) + (first.IsEmpty ? 0 : first.Y));
            }
            _overlay.Children.Remove(box);
            if (block != null)
                AddAnnotation(block);
        }

        private void CancelText()
        {
            var box = _textBox;
            if (box == null)
                return;
            _textBox = null;
            _overlay.Children.Remove(box);
        }
    }
}

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
    public enum CaptureTool { None, Select, Rectangle, Ellipse, Arrow, Text, Crop }

    /// <summary>
    /// 캡쳐 결과 편집면. 흰 페이지 위에 캡쳐 이미지와 도형이 놓인다. 상태는 (페이지 크기, 도형 목록)이고
    /// 도형은 바뀌지 않는 값(<see cref="CaptureAnnotation"/>)이다. 캡쳐 이미지도 목록의 첫 항목(<see cref="CaptureAnnotationKind.Image"/>,
    /// 자리만 값, 비트맵은 <see cref="_bitmap"/> 하나로 고정)이라 다른 도형처럼 선택·이동·핸들 크기 변경이 되고, 항상 맨 아래에 그려진다.
    /// 화면 요소는 목록에서 매번 다시 만들고, 저장·복사 때만 페이지 한 장으로 합친다(<see cref="Render"/>).
    /// 자르기·크기 조절은 페이지 크기만 바꾸고 이미지·도형은 위치·크기를 맞춰 옮기므로(비트맵은 다시 샘플링하지 않음), 그 뒤에도 선택해 고칠 수 있다.
    /// 되돌리기는 바꾸기 전 (페이지 크기, 도형 배열)을 쌓는다 — 값이 불변이라 나중 편집이 기록을 바꾸지 않는다.
    /// 좌표는 페이지 픽셀 = 편집면 DIP이고, 화면에는 캡쳐 DPI만큼 줄여(LayoutTransform) 실제 픽셀 1:1로 보인다.
    /// </summary>
    public sealed class CaptureEditor : Grid
    {
        private enum DragMode { None, Draw, Crop, Move, Handle }

        // 화면 기준 핸들 크기·집기 여유(DIP). 이미지 픽셀로는 표시 배율로 나눈다.
        private const double HandleScreenSize = 8;
        private const double HitScreenTolerance = 6;

        // 합쳐서 내보내는 층(페이지): 흰 바탕 + [0] 캡쳐 이미지 + 도형 요소들.
        private readonly Canvas _surface = new Canvas { ClipToBounds = true, Background = Brushes.White };
        // 그리는 중 미리보기·자르기 틀·선택 표시·텍스트 입력 상자. 내보내지 않는다.
        private readonly Canvas _overlay = new Canvas { Background = Brushes.Transparent, ClipToBounds = true };
        private readonly Stack<(int Width, int Height, CaptureAnnotation[] Annotations)> _undo = new Stack<(int, int, CaptureAnnotation[])>();
        private readonly List<UIElement> _selectionVisuals = new List<UIElement>();
        private readonly double _displayScale;
        private readonly BitmapSource _bitmap;
        private int _pageWidth;
        private int _pageHeight;
        // [0]은 항상 캡쳐 이미지(Kind == Image). 새 도형은 뒤에 붙고, 이미지는 지우지 않는다.
        private CaptureAnnotation[] _annotations;
        private CaptureTool _tool;
        private int _selected = -1;
        private DragMode _drag;
        private Point _start;
        private CaptureHandle _handle;
        private CaptureAnnotation _draft;
        private UIElement _preview;
        private TextBox _textBox;

        public Color StrokeColor { get; set; } = Colors.Red;
        public double StrokeThickness { get; set; } = 3;
        public double TextSize { get; set; } = 18;

        /// <summary>이미지·도형이 바뀌었다(크기, 되돌리기 가능 여부 갱신용).</summary>
        public event EventHandler Changed;

        /// <summary>페이지(저장·복사 결과) 크기(픽셀).</summary>
        public int PixelWidth => _pageWidth;
        public int PixelHeight => _pageHeight;
        public bool CanUndo => _undo.Count > 0;

        public CaptureEditor(BitmapSource bitmap)
        {
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
            _displayScale = bitmap.DpiX > 0 ? 96.0 / bitmap.DpiX : 1.0;
            LayoutTransform = new ScaleTransform(_displayScale, _displayScale);

            Children.Add(_surface);
            Children.Add(_overlay);
            _bitmap = bitmap;
            // 처음에는 페이지 = 캡쳐 크기, 이미지는 페이지를 꽉 채운다(편집하지 않으면 저장·복사 결과가 캡쳐 그대로).
            _annotations = new[] { new CaptureAnnotation(CaptureAnnotationKind.Image, 0, 0, bitmap.PixelWidth, bitmap.PixelHeight, 0, 0) };
            SetPageSize(bitmap.PixelWidth, bitmap.PixelHeight);
            ShowAnnotations(_annotations);

            _overlay.MouseLeftButtonDown += Overlay_MouseLeftButtonDown;
            _overlay.MouseMove += Overlay_MouseMove;
            _overlay.MouseLeftButtonUp += Overlay_MouseLeftButtonUp;
            _overlay.LostMouseCapture += (s, e) => CancelDrag();
        }

        private double HitTolerance => HitScreenTolerance / _displayScale;

        public CaptureTool Tool
        {
            get { return _tool; }
            set
            {
                CommitText();
                CancelDrag();
                Select(-1);
                _tool = value;
                _overlay.Cursor = value == CaptureTool.None || value == CaptureTool.Select ? null
                    : value == CaptureTool.Text ? Cursors.IBeam : Cursors.Cross;
            }
        }

        /// <summary>페이지(흰 바탕 + 이미지 + 도형)를 한 장으로 합친다(입력 중인 텍스트는 먼저 확정). 아무것도 바꾸지 않았으면 캡쳐 그대로.</summary>
        public BitmapSource Render()
        {
            CommitText();
            if (IsUnchanged())
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
            CancelDrag();
            if (_undo.Count == 0)
                return;
            var state = _undo.Pop();
            SetPageSize(state.Width, state.Height);
            _annotations = state.Annotations;
            Select(-1);
            ShowAnnotations(_annotations);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>선택한 도형을 지운다. 지웠으면 true. 캡쳐 이미지는 지우지 않는다.</summary>
        public bool DeleteSelected()
        {
            if (_selected < 0 || _drag != DragMode.None || _annotations[_selected].Kind == CaptureAnnotationKind.Image)
                return false;
            var index = _selected;
            Apply(_pageWidth, _pageHeight, _annotations.Where((_, i) => i != index).ToArray());
            return true;
        }

        /// <summary>
        /// 처음 열 때 흰 배경을 보이는 영역(<paramref name="viewWidth"/>×<paramref name="viewHeight"/>, 화면 DIP)만큼 넓힌다.
        /// 캡쳐보다 작게 하지 않고, 이미지는 왼쪽 위 그대로. 열린 상태로 보므로 되돌리기·변경 알림을 남기지 않는다.
        /// </summary>
        public void ExpandPageToView(double viewWidth, double viewHeight)
        {
            SetPageSize(ScreenCaptureService.InitialPageSide(viewWidth, _displayScale, _pageWidth),
                ScreenCaptureService.InitialPageSide(viewHeight, _displayScale, _pageHeight));
        }

        /// <summary>흰 배경(페이지)만 <paramref name="width"/>×<paramref name="height"/>로 바꾼다. 이미지·도형은 그 자리·크기 그대로(왼쪽 위 기준).</summary>
        public void ResizePage(int width, int height)
        {
            CommitText();
            Apply(width, height, _annotations);
        }

        /// <summary>페이지를 <paramref name="width"/>×<paramref name="height"/>로 바꾸고 이미지·도형도 같은 배율로 늘린다.</summary>
        public void Resize(int width, int height)
        {
            CommitText();
            var scaleX = (double)width / PixelWidth;
            var scaleY = (double)height / PixelHeight;
            Apply(width, height, _annotations.Select(a => a.Transform(0, 0, scaleX, scaleY)).ToArray());
        }

        private void Crop(Point a, Point b)
        {
            var rect = ScreenCaptureService.ToPixelRect(a.X, a.Y, b.X, b.Y, PixelWidth, PixelHeight, PixelWidth, PixelHeight);
            if (!ScreenCaptureService.IsSelectable(rect.Width, rect.Height))
                return;
            // 페이지만 줄이고 이미지·도형은 자른 원점만큼 옮긴다. 밖으로 나간 부분은 보이지 않을 뿐 지우지 않는다(되돌리기 없이도 다시 끌어올 수 있다).
            Apply(rect.Width, rect.Height, _annotations.Select(x => x.Transform(-rect.X, -rect.Y, 1, 1)).ToArray());
        }

        /// <summary>되돌리기에 지금 상태를 쌓고 새 상태로 바꾼다.</summary>
        private void Apply(int width, int height, CaptureAnnotation[] annotations)
        {
            _undo.Push((_pageWidth, _pageHeight, _annotations));
            SetPageSize(width, height);
            _annotations = annotations;
            Select(-1);
            ShowAnnotations(_annotations);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void SetPageSize(int width, int height)
        {
            _pageWidth = width;
            _pageHeight = height;
            _surface.Width = _overlay.Width = width;
            _surface.Height = _overlay.Height = height;
        }

        /// <summary>페이지가 캡쳐 크기 그대로이고 이미지가 제자리에 꽉 차 있으며 도형이 없는가(= 캡쳐 원본을 그대로 내보내도 됨).</summary>
        private bool IsUnchanged()
        {
            if (_annotations.Length != 1 || _pageWidth != _bitmap.PixelWidth || _pageHeight != _bitmap.PixelHeight)
                return false;
            var image = _annotations[0];
            return image.Left == 0 && image.Top == 0 && image.Width == _bitmap.PixelWidth && image.Height == _bitmap.PixelHeight;
        }

        /// <summary>이미지·도형 요소를 목록에서 다시 만든다(끄는 중에는 바뀐 사본 목록을 보여 준다).</summary>
        private void ShowAnnotations(IReadOnlyList<CaptureAnnotation> annotations)
        {
            _surface.Children.Clear();
            foreach (var annotation in annotations)
                _surface.Children.Add(CreateElement(annotation));
        }

        private CaptureAnnotation[] WithSelectedReplaced(CaptureAnnotation replacement)
        {
            var copy = (CaptureAnnotation[])_annotations.Clone();
            copy[_selected] = replacement;
            return copy;
        }

        private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 입력 상자 안 클릭은 상자가 처리한다(IsAncestorOf는 Visual이 아니면 예외라 형식부터 본다).
            if (_textBox != null && e.OriginalSource is Visual source && _textBox.IsAncestorOf(source))
                return;

            var point = e.GetPosition(_overlay);
            switch (_tool)
            {
                case CaptureTool.None:
                    return;
                case CaptureTool.Text:
                    // 입력 중이면 바깥 클릭은 확정만 한다(바로 새 입력을 열지 않음).
                    if (_textBox != null)
                        CommitText();
                    else
                        BeginText(point);
                    e.Handled = true;
                    return;
                case CaptureTool.Select:
                    if (!BeginSelectDrag(point))
                        return;
                    break;
                case CaptureTool.Crop:
                    _drag = DragMode.Crop;
                    _preview = new Rectangle
                    {
                        Stroke = new SolidColorBrush(Color.FromRgb(0x3D, 0x9B, 0xFF)),
                        StrokeThickness = 1 / _displayScale,
                        StrokeDashArray = new DoubleCollection { 4, 3 },
                        Fill = new SolidColorBrush(Color.FromArgb(0x30, 0x3D, 0x9B, 0xFF))
                    };
                    _overlay.Children.Add(_preview);
                    break;
                default:
                    _drag = DragMode.Draw;
                    break;
            }

            _start = point;
            UpdateDrag(point);
            _overlay.CaptureMouse();
            e.Handled = true;
        }

        /// <summary>선택 도구: 선택한 도형의 핸들 → 핸들 끌기, 도형 → 선택 후 이동, 빈 곳 → 선택 해제. 끌기를 시작하면 true.</summary>
        private bool BeginSelectDrag(Point point)
        {
            if (_selected >= 0)
            {
                var handle = _annotations[_selected].HitHandle(point.X, point.Y, HitTolerance);
                if (handle != CaptureHandle.None)
                {
                    _drag = DragMode.Handle;
                    _handle = handle;
                    return true;
                }
            }

            Select(CaptureAnnotation.TopmostHit(_annotations, point.X, point.Y, HitTolerance));
            if (_selected < 0)
                return false;
            _drag = DragMode.Move;
            return true;
        }

        private void Overlay_MouseMove(object sender, MouseEventArgs e)
        {
            var point = e.GetPosition(_overlay);
            if (_drag != DragMode.None)
                UpdateDrag(point);
            else if (_tool == CaptureTool.Select)
                _overlay.Cursor = HoverCursor(point);
        }

        private void UpdateDrag(Point point)
        {
            switch (_drag)
            {
                case DragMode.Draw:
                    _draft = NewAnnotation(_start, point);
                    ReplacePreview(CreateElement(_draft.Normalized()));
                    break;
                case DragMode.Crop:
                    var rect = new Rect(_start, point);
                    Canvas.SetLeft(_preview, rect.X);
                    Canvas.SetTop(_preview, rect.Y);
                    ((Rectangle)_preview).Width = rect.Width;
                    ((Rectangle)_preview).Height = rect.Height;
                    break;
                case DragMode.Move:
                    _draft = _annotations[_selected].Translate(point.X - _start.X, point.Y - _start.Y);
                    ShowDraft();
                    break;
                case DragMode.Handle:
                    _draft = _annotations[_selected].DragHandle(_handle, point.X, point.Y);
                    ShowDraft();
                    break;
            }
        }

        private void ShowDraft()
        {
            ShowAnnotations(WithSelectedReplaced(_draft));
            DrawSelection(_draft.Normalized());
        }

        private void Overlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var mode = _drag;
            if (mode == DragMode.None)
                return;
            var end = e.GetPosition(_overlay);
            UpdateDrag(end);
            var draft = _draft;
            // 캡처 해제가 LostMouseCapture → CancelDrag를 부르므로 끄는 상태부터 비운다.
            _drag = DragMode.None;
            _draft = null;
            _overlay.ReleaseMouseCapture();
            ReplacePreview(null);

            switch (mode)
            {
                case DragMode.Crop:
                    Crop(_start, end);
                    break;
                case DragMode.Draw:
                    if (!IsTooSmall(draft))
                        Apply(_pageWidth, _pageHeight, _annotations.Concat(new[] { draft.Normalized() }).ToArray());
                    break;
                case DragMode.Move:
                case DragMode.Handle:
                    var index = _selected;
                    if (end == _start || (mode == DragMode.Handle && IsTooSmall(draft)))
                    {
                        // 제자리 클릭이거나 핸들로 너무 작게 줄였으면 원래대로.
                        ShowAnnotations(_annotations);
                        DrawSelection(_annotations[index]);
                        break;
                    }
                    var replaced = WithSelectedReplaced(draft.Normalized());
                    Apply(_pageWidth, _pageHeight, replaced);
                    Select(index);
                    break;
            }
        }

        private static bool IsTooSmall(CaptureAnnotation annotation)
        {
            var min = ScreenCaptureService.MinSelectionPixels;
            if (annotation.Kind == CaptureAnnotationKind.Arrow)
                return Math.Sqrt(annotation.Width * annotation.Width + annotation.Height * annotation.Height) < min;
            return annotation.Width < min || annotation.Height < min;
        }

        private void CancelDrag()
        {
            if (_drag == DragMode.None)
                return;
            var wasEditing = _drag == DragMode.Move || _drag == DragMode.Handle;
            _drag = DragMode.None;
            _draft = null;
            ReplacePreview(null);
            if (wasEditing)
            {
                ShowAnnotations(_annotations);
                DrawSelection(_selected >= 0 ? _annotations[_selected] : null);
            }
        }

        private void ReplacePreview(UIElement preview)
        {
            if (_preview != null)
                _overlay.Children.Remove(_preview);
            _preview = preview;
            if (preview != null)
                _overlay.Children.Add(preview);
        }

        private CaptureAnnotation NewAnnotation(Point start, Point end)
        {
            var kind = _tool == CaptureTool.Ellipse ? CaptureAnnotationKind.Ellipse
                : _tool == CaptureTool.Arrow ? CaptureAnnotationKind.Arrow
                : CaptureAnnotationKind.Rectangle;
            return new CaptureAnnotation(kind, start.X, start.Y, end.X, end.Y, ToArgb(StrokeColor), StrokeThickness);
        }

        private void Select(int index)
        {
            _selected = index;
            DrawSelection(index >= 0 ? _annotations[index] : null);
        }

        /// <summary>선택 표시(점선 상자 + 핸들)를 다시 그린다. 내보내지 않는 층에 그린다.</summary>
        private void DrawSelection(CaptureAnnotation annotation)
        {
            foreach (var visual in _selectionVisuals)
                _overlay.Children.Remove(visual);
            _selectionVisuals.Clear();
            if (annotation == null)
                return;

            var accent = new SolidColorBrush(Color.FromRgb(0x3D, 0x9B, 0xFF));
            var line = 1 / _displayScale;
            if (annotation.Kind != CaptureAnnotationKind.Arrow)
            {
                var pad = annotation.Thickness / 2 + 2 * line;
                var box = new Rectangle
                {
                    Stroke = accent,
                    StrokeThickness = line,
                    StrokeDashArray = new DoubleCollection { 4, 3 },
                    Width = annotation.Width + pad * 2,
                    Height = annotation.Height + pad * 2,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(box, annotation.Left - pad);
                Canvas.SetTop(box, annotation.Top - pad);
                _selectionVisuals.Add(box);
            }

            var size = HandleScreenSize / _displayScale;
            foreach (var handle in annotation.Handles())
            {
                var square = new Rectangle { Width = size, Height = size, Fill = Brushes.White, Stroke = accent, StrokeThickness = line, IsHitTestVisible = false };
                Canvas.SetLeft(square, handle.X - size / 2);
                Canvas.SetTop(square, handle.Y - size / 2);
                _selectionVisuals.Add(square);
            }
            foreach (var visual in _selectionVisuals)
                _overlay.Children.Add(visual);
        }

        private Cursor HoverCursor(Point point)
        {
            if (_selected >= 0)
            {
                switch (_annotations[_selected].HitHandle(point.X, point.Y, HitTolerance))
                {
                    case CaptureHandle.TopLeft:
                    case CaptureHandle.BottomRight: return Cursors.SizeNWSE;
                    case CaptureHandle.TopRight:
                    case CaptureHandle.BottomLeft: return Cursors.SizeNESW;
                    case CaptureHandle.Top:
                    case CaptureHandle.Bottom: return Cursors.SizeNS;
                    case CaptureHandle.Left:
                    case CaptureHandle.Right: return Cursors.SizeWE;
                    case CaptureHandle.Start:
                    case CaptureHandle.End: return Cursors.Cross;
                }
            }
            return CaptureAnnotation.TopmostHit(_annotations, point.X, point.Y, HitTolerance) >= 0 ? Cursors.SizeAll : null;
        }

        private static uint ToArgb(Color color)
        {
            return (uint)color.A << 24 | (uint)color.R << 16 | (uint)color.G << 8 | color.B;
        }

        private static SolidColorBrush ToBrush(uint argb)
        {
            var brush = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
            brush.Freeze();
            return brush;
        }

        private UIElement CreateElement(CaptureAnnotation annotation)
        {
            var brush = ToBrush(annotation.Argb);
            FrameworkElement element;
            switch (annotation.Kind)
            {
                case CaptureAnnotationKind.Image:
                    var image = new Image { Source = _bitmap, Stretch = Stretch.Fill, Width = annotation.Width, Height = annotation.Height };
                    // 캡쳐 크기 그대로면 1:1 표시·합치기에서 다시 샘플링하지 않고, 늘리거나 줄였으면 고품질 보간.
                    var original = annotation.Width == _bitmap.PixelWidth && annotation.Height == _bitmap.PixelHeight;
                    RenderOptions.SetBitmapScalingMode(image, original ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
                    element = image;
                    break;
                case CaptureAnnotationKind.Arrow:
                    return new Path
                    {
                        Stroke = brush,
                        Fill = brush,
                        StrokeThickness = annotation.Thickness,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeLineJoin = PenLineJoin.Round,
                        Data = ArrowGeometry(annotation)
                    };
                case CaptureAnnotationKind.Text:
                    element = new TextBlock { Text = annotation.Text, Foreground = brush, FontSize = annotation.FontSize, FontWeight = FontWeights.SemiBold };
                    break;
                case CaptureAnnotationKind.Ellipse:
                    element = new Ellipse { Stroke = brush, StrokeThickness = annotation.Thickness, Width = annotation.Width, Height = annotation.Height };
                    break;
                default:
                    element = new Rectangle { Stroke = brush, StrokeThickness = annotation.Thickness, Width = annotation.Width, Height = annotation.Height };
                    break;
            }
            Canvas.SetLeft(element, annotation.Left);
            Canvas.SetTop(element, annotation.Top);
            return element;
        }

        private static Geometry ArrowGeometry(CaptureAnnotation arrow)
        {
            var start = new Point(arrow.X1, arrow.Y1);
            var end = new Point(arrow.X2, arrow.Y2);
            var head = ScreenCaptureService.ArrowHead(start.X, start.Y, end.X, end.Y, arrow.Thickness);
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

        /// <summary>입력 상자의 글자를 같은 위치·글꼴의 텍스트 도형으로 넣는다. 빈 글자는 버린다.</summary>
        private void CommitText()
        {
            var box = _textBox;
            if (box == null)
                return;
            // 상자를 치우면 LostKeyboardFocus가 다시 이곳을 부르므로 먼저 비운다.
            _textBox = null;

            CaptureAnnotation text = null;
            if (!string.IsNullOrWhiteSpace(box.Text))
            {
                // 첫 글자의 상자 안 위치로 맞춰 확정 전후 글자가 움직이지 않게 한다(테마 TextBox 여백과 무관).
                box.UpdateLayout();
                var first = box.GetRectFromCharacterIndex(0);
                var left = Canvas.GetLeft(box) + (first.IsEmpty ? 0 : first.X);
                var top = Canvas.GetTop(box) + (first.IsEmpty ? 0 : first.Y);
                // 선택·이동에 쓸 상자 크기는 표시할 글꼴(편집면에서 물려받는 것과 같은 입력 상자 글꼴)로 잰다.
                var measure = new TextBlock { Text = box.Text, FontFamily = box.FontFamily, FontSize = box.FontSize, FontWeight = box.FontWeight };
                measure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                text = new CaptureAnnotation(CaptureAnnotationKind.Text, left, top,
                    left + measure.DesiredSize.Width, top + measure.DesiredSize.Height,
                    ToArgb(((SolidColorBrush)box.Foreground).Color), 0, box.FontSize, box.Text);
            }
            _overlay.Children.Remove(box);
            if (text != null)
                Apply(_pageWidth, _pageHeight, _annotations.Concat(new[] { text }).ToArray());
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

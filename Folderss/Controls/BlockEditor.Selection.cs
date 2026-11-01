using Folderss.Services;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Folderss.Controls
{
    /// <summary>
    /// 블록 편집기 선택·이동(#75 "선택·이동"):
    /// <list type="bullet">
    /// <item>글자를 끌어 선택하다 다른 블록으로 넘어가면 블록 단위 선택으로 바꾼다(Notion 방식).</item>
    /// <item>좌우·아래 빈 여백에서 끌면 사각 영역에 걸친 블록을 선택한다.</item>
    /// <item><c>Shift+클릭</c>: 지금 블록(또는 선택 시작 블록)부터 누른 블록까지 선택.</item>
    /// <item>선택한 여러 블록 중 하나의 손잡이를 끌면 선택 전체를 함께 옮긴다(실행 취소 한 단계).</item>
    /// <item><c>Ctrl+Home</c>/<c>Ctrl+End</c> 문서 처음·끝, <c>PageUp</c>/<c>PageDown</c> 한 화면 떨어진 블록(Shift는 블록 선택 확장).</item>
    /// </list>
    /// 선택은 기존 블록 선택(<see cref="_selAnchor"/>~<see cref="_selActive"/>, 연속 범위 + 자식)을 그대로 쓴다. 끄는 동안 위치 계산은 패널의 높이 누적으로만 해
    /// 행을 새로 만들지 않고, 화면에 있는 행의 선택 표시만 바꾼다.
    /// </summary>
    public sealed partial class BlockEditor
    {
        private enum MouseSelectMode { None, Marquee, CrossBlock }

        private const double MarqueeThreshold = 4;
        private const double AutoScrollEdge = 28;

        private MouseSelectMode _mouseSelect;
        private Point _marqueeStart;          // 누른 자리(패널 좌표) — 끌지 않고 떼면 이 자리를 클릭한 것으로
        private double _marqueeStartY;        // 누른 자리(내용 y) — 끄는 중 스크롤해도 시작점이 같은 내용에 붙어 있게
        private bool _marqueeMoved;
        private int _crossAnchor = -1;
        private MarqueeAdorner _marqueeAdorner;
        private DispatcherTimer _autoScrollTimer;
        private bool _dragSelection;          // 손잡이로 선택한 여러 블록을 함께 끄는 중

        /// <summary>생성자에서 한 번 부른다.</summary>
        private void InitializeSelection()
        {
            _panel.MouseMove += (s, e) =>
            {
                if (_mouseSelect != MouseSelectMode.None && _panel.IsMouseCaptured)
                    UpdateMouseSelect(e.GetPosition(_panel));
            };
            _panel.MouseLeftButtonUp += (s, e) =>
            {
                if (_mouseSelect == MouseSelectMode.None)
                    return;
                e.Handled = true;
                EndMouseSelect(true);
            };
            // 창 전환 등으로 캡처를 잃으면 선택은 그대로 두고 끄기만 끝낸다.
            // LostMouseCapture는 버블 이벤트라 패널 안 입력란·손잡이가 캡처를 놓아도 여기로 올라온다 — 패널 자신의 것만 본다
            // (글자 끌기 → 블록 선택 전환에서 TextBox가 캡처를 놓을 때 막 시작한 선택이 끝나 버리지 않게).
            _panel.LostMouseCapture += (s, e) =>
            {
                if (ReferenceEquals(e.OriginalSource, _panel))
                    EndMouseSelect(false);
            };
            PreviewMouseMove += OnCrossBlockPreviewMouseMove;
            PreviewMouseLeftButtonDown += OnShiftClickPreview;
        }

        // ── 사각 영역 선택 ─────────────────────────────────────────────────

        /// <summary>패널 빈 여백(행 밖)을 눌렀다. 끌면 사각 영역 선택, 그냥 떼면 <see cref="BlockPanel.ClickAt"/>.</summary>
        private void BeginMarquee(Point position)
        {
            CloseSlash();
            _mouseSelect = MouseSelectMode.Marquee;
            _marqueeStart = position;
            _marqueeStartY = _panel.ToContentY(position.Y);
            _marqueeMoved = false;
            _panel.CaptureMouse();
        }

        private void UpdateMouseSelect(Point position)
        {
            if (_mouseSelect == MouseSelectMode.Marquee)
            {
                if (!_marqueeMoved)
                {
                    var startNow = new Point(_marqueeStart.X, _panel.ToPanelY(_marqueeStartY));
                    if (Math.Abs(position.X - startNow.X) < MarqueeThreshold && Math.Abs(position.Y - startNow.Y) < MarqueeThreshold)
                        return;
                    _marqueeMoved = true;
                    // 입력란에서 빠져나와 블록 선택 키(Del·Ctrl+C·Shift+↑↓)가 먹게
                    if (!IsKeyboardFocused)
                        Keyboard.Focus(this);
                    StartAutoScroll();
                }
                var y = _panel.ToContentY(position.Y);
                int first, last;
                if (_panel.SpanRange(_marqueeStartY, y, _marqueeStart.X, position.X, out first, out last))
                {
                    // 위로 끌면 시작점이 아래쪽 끝 — Shift+↑↓로 이어서 넓힐 때 방향이 맞게
                    if (y >= _marqueeStartY)
                        SetMouseSelection(first, last);
                    else
                        SetMouseSelection(last, first);
                }
                else if (HasBlockSelection)
                {
                    ClearSelection();
                }
                ShowMarquee(position);
            }
            else if (_mouseSelect == MouseSelectMode.CrossBlock)
            {
                var target = _panel.IndexAtPoint(position);
                if (target >= 0 && _crossAnchor >= 0 && _crossAnchor < _blocks.Count)
                    SetMouseSelection(_crossAnchor, target);
            }
        }

        private void EndMouseSelect(bool released)
        {
            if (_mouseSelect == MouseSelectMode.None)
                return;
            var mode = _mouseSelect;
            var moved = _marqueeMoved;
            _mouseSelect = MouseSelectMode.None;
            _marqueeMoved = false;
            _crossAnchor = -1;
            StopAutoScroll();
            HideMarquee();
            if (_panel.IsMouseCaptured)
                _panel.ReleaseMouseCapture();
            if (released && mode == MouseSelectMode.Marquee && !moved)
                _panel.ClickAt(_marqueeStart);
            else if (HasBlockSelection)
                BringRowIntoView(_selActive);
        }

        /// <summary>마우스로 고른 범위를 블록 선택으로. 스크롤은 하지 않는다(끄는 중 자동 스크롤과 다투지 않게).</summary>
        private void SetMouseSelection(int anchor, int active)
        {
            if (anchor < 0 || active < 0 || anchor >= _blocks.Count || active >= _blocks.Count)
                return;
            if (_selAnchor == anchor && _selActive == active)
                return;
            _selAnchor = anchor;
            _selActive = active;
            UpdateSelectionVisuals();
        }

        private void ShowMarquee(Point position)
        {
            var layer = AdornerLayer.GetAdornerLayer(_panel);
            if (layer == null)
                return;
            if (_marqueeAdorner == null)
                _marqueeAdorner = new MarqueeAdorner(_panel);
            if (_marqueeAdorner.Parent == null)
                layer.Add(_marqueeAdorner);
            var start = new Point(_marqueeStart.X, _panel.ToPanelY(_marqueeStartY));
            var rect = new Rect(start, position);
            rect.Intersect(new Rect(_panel.RenderSize));
            _marqueeAdorner.SetRect(rect.IsEmpty ? Rect.Empty : rect);
        }

        private void HideMarquee()
        {
            if (_marqueeAdorner == null || _marqueeAdorner.Parent == null)
                return;
            AdornerLayer.GetAdornerLayer(_panel)?.Remove(_marqueeAdorner);
        }

        // 위·아래 가장자리 가까이 끌고 있으면 마우스를 움직이지 않아도 계속 스크롤하며 선택을 넓힌다.
        private void StartAutoScroll()
        {
            if (_autoScrollTimer == null)
            {
                _autoScrollTimer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher) { Interval = TimeSpan.FromMilliseconds(40) };
                _autoScrollTimer.Tick += (s, e) => AutoScrollTick();
            }
            _autoScrollTimer.Start();
        }

        private void StopAutoScroll()
        {
            _autoScrollTimer?.Stop();
        }

        private void AutoScrollTick()
        {
            if (_mouseSelect == MouseSelectMode.None)
            {
                StopAutoScroll();
                return;
            }
            var y = Mouse.GetPosition(_scroll).Y;
            var height = _scroll.ActualHeight;
            double delta = 0;
            if (y < AutoScrollEdge)
                delta = -Math.Min(60, 6 + (AutoScrollEdge - y) * 0.6);
            else if (y > height - AutoScrollEdge)
                delta = Math.Min(60, 6 + (y - (height - AutoScrollEdge)) * 0.6);
            if (delta == 0)
                return;
            var before = _panel.VerticalOffset;
            _panel.SetVerticalOffset(before + delta);
            if (Math.Abs(_panel.VerticalOffset - before) > 0.01)
                UpdateMouseSelect(Mouse.GetPosition(_panel));
        }

        // ── 글자 끌기 → 블록 선택 ─────────────────────────────────────────

        /// <summary>
        /// TextBox에서 글자를 끌어 선택하는 중(마우스를 그 TextBox가 잡고 있음) 다른 블록으로 넘어가면 블록 선택으로 바꾸고, 그 뒤로는 패널이 마우스를 잡아 범위를 넓힌다.
        /// </summary>
        private void OnCrossBlockPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_mouseSelect != MouseSelectMode.None || e.LeftButton != MouseButtonState.Pressed)
                return;
            var box = Mouse.Captured as TextBox;
            var row = box?.Tag as BlockRow;
            if (row == null)
                return;
            var from = IndexOf(row);
            if (from < 0)
                return;
            // 행 안에서 움직이는 동안은 글자 선택 그대로(표의 셀 사이 이동도 같은 행)
            var inRow = e.GetPosition(row.Root);
            if (inRow.Y >= 0 && inRow.Y <= row.Root.ActualHeight && inRow.X >= 0 && inRow.X <= row.Root.ActualWidth)
                return;
            var target = _panel.IndexAtPoint(e.GetPosition(_panel));
            if (target < 0 || target == from)
                return;

            e.Handled = true;
            _mouseSelect = MouseSelectMode.CrossBlock;
            _crossAnchor = from;
            box.ReleaseMouseCapture();
            box.SelectionLength = 0;
            Keyboard.Focus(this);
            CloseSlash();
            _panel.CaptureMouse();
            SetMouseSelection(from, target);
            StartAutoScroll();
        }

        // ── Shift+클릭 범위 선택 ───────────────────────────────────────────

        private void OnShiftClickPreview(object sender, MouseButtonEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Shift || _mouseSelect != MouseSelectMode.None || _dragRow != null)
                return;
            // 패널 안(행·여백)만. / 메뉴·팝업은 제외한다. Run 같은 ContentElement에서도 안전하게 올라간다.
            if (!ReferenceEquals(ElementTree.FindAncestor<BlockPanel>(e.OriginalSource as DependencyObject), _panel))
                return;

            int anchor;
            var editing = !HasBlockSelection;
            if (!editing)
                anchor = _selAnchor;
            else if (_focusedRow != null && _focusedRow.Root.IsKeyboardFocusWithin)
                anchor = IndexOf(_focusedRow);
            else
                return;
            if (anchor < 0)
                return;
            var target = _panel.IndexAtPoint(e.GetPosition(_panel));
            if (target < 0)
                return;
            // 편집 중인 블록 안을 Shift+클릭하면 글자 선택 넓히기(TextBox 기본 동작)
            if (editing && target == anchor)
                return;
            e.Handled = true;
            SelectBlocks(anchor, target);
        }

        // ── 여러 블록 끌어 옮기기 ──────────────────────────────────────────

        /// <summary>블록 선택 범위(자식 포함) 안에 있는지.</summary>
        private bool IsInBlockSelection(int index)
        {
            int start, end;
            GetSelectionRange(out start, out end);
            return index >= 0 && index >= start && index < end;
        }

        /// <summary>손잡이로 끄는 범위 [start, end): 선택한 여러 블록 중 하나를 끌면 선택 전체, 아니면 그 블록과 자식.</summary>
        private void GetDragRange(BlockRow row, out int start, out int end)
        {
            start = IndexOf(row);
            if (start < 0)
            {
                end = -1;
                return;
            }
            if (_dragSelection && IsInBlockSelection(start))
            {
                GetSelectionRange(out start, out end);
                return;
            }
            end = BlockOperations.SubtreeEnd(_blocks, start);
        }

        /// <summary>끌어 놓기: [start, end)를 한 번에 옮기고 한 단계로 기록한다. 여러 블록이면 옮긴 뒤에도 선택을 유지한다.</summary>
        private void MoveDraggedBlocks(BlockRow row, DropTarget drop)
        {
            int start, end;
            GetDragRange(row, out start, out end);
            if (start < 0 || !CanDrop(start, end, drop) || MovesFrontMatter(start, drop.Index))
                return;
            var keepSelection = _dragSelection && HasBlockSelection;
            var count = end - start;
            var insertAt = BlockSelection.MoveRangeTo(_blocks, start, end, drop.Index, drop.Column);
            if (insertAt < 0)
                return;
            BlockOperations.NormalizeIndents(_blocks);
            _selAnchor = _selActive = -1;
            SyncRows();
            if (keepSelection)
                SelectBlocks(insertAt, insertAt + count - 1);
            else
                FocusRow(insertAt, int.MaxValue);
            RecordChange();
        }

        // ── 문서 처음·끝, 한 화면 이동 ─────────────────────────────────────

        /// <summary>
        /// <c>Ctrl+Home</c>/<c>Ctrl+End</c> 문서 처음·끝 블록, <c>PageUp</c>/<c>PageDown</c> 한 화면 떨어진 블록. 편집 중이면 그 블록에 커서,
        /// 블록 선택 중이면 선택을 옮긴다. Shift를 함께 누르면 지금 블록(선택 시작)부터 블록 선택을 넓힌다.
        /// </summary>
        private bool HandleNavigationKey(Key key, ModifierKeys mods)
        {
            if (_slashPopup.IsOpen || _blocks.Count == 0)
                return false;
            var extend = (mods & ModifierKeys.Shift) != 0;
            var rest = mods & ~ModifierKeys.Shift;
            var docEdge = (key == Key.Home || key == Key.End) && rest == ModifierKeys.Control;
            var page = (key == Key.PageUp || key == Key.PageDown) && rest == ModifierKeys.None;
            if (!docEdge && !page)
                return false;

            var blockMode = HasBlockSelection && !(Keyboard.FocusedElement is TextBox);
            int from;
            if (blockMode)
            {
                from = _selActive;
            }
            else
            {
                var row = (Keyboard.FocusedElement as TextBox)?.Tag as BlockRow;
                from = IndexOf(row);
                if (from < 0)
                    return false;
            }

            var direction = key == Key.Home || key == Key.PageUp ? -1 : 1;
            int target;
            if (docEdge)
            {
                target = direction < 0 ? NextVisible(-1, 1) : NextVisible(_blocks.Count, -1);
            }
            else
            {
                target = _panel.PageTargetFrom(from, direction);
                // 화면도 한 화면 넘겨 옮긴 블록이 비슷한 높이에 보이게
                _panel.SetVerticalOffset(_panel.VerticalOffset + direction * _panel.PageDistance);
            }
            if (target < 0)
                return true;

            if (blockMode || extend)
            {
                var anchor = blockMode ? (extend ? _selAnchor : target) : from;
                SelectBlocks(anchor, target);
                return true;
            }
            // 문서 끝은 마지막 블록 글자 끝, 그 밖은 블록 처음
            FocusRow(target, docEdge && direction > 0 ? int.MaxValue : 0);
            return true;
        }

        /// <summary>사각 영역 선택 표시(패널 위 장식 층, 마우스를 받지 않음).</summary>
        private sealed class MarqueeAdorner : Adorner
        {
            private Rect _rect = Rect.Empty;

            public MarqueeAdorner(UIElement adorned) : base(adorned)
            {
                IsHitTestVisible = false;
            }

            public void SetRect(Rect rect)
            {
                _rect = rect;
                InvalidateVisual();
            }

            protected override void OnRender(DrawingContext drawingContext)
            {
                if (_rect.IsEmpty)
                    return;
                var accent = (AdornedElement as FrameworkElement)?.TryFindResource("AccentBrush") as SolidColorBrush;
                var color = accent?.Color ?? Color.FromRgb(0x3B, 0x82, 0xF6);
                var fill = new SolidColorBrush(Color.FromArgb(0x30, color.R, color.G, color.B));
                var stroke = new Pen(new SolidColorBrush(Color.FromArgb(0xA0, color.R, color.G, color.B)), 1);
                fill.Freeze();
                stroke.Freeze();
                drawingContext.DrawRectangle(fill, stroke, _rect);
            }
        }
    }
}

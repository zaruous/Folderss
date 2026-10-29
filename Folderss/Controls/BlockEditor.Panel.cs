using Folderss.Models;
using Folderss.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Folderss.Controls
{
    public sealed partial class BlockEditor
    {
        /// <summary>끌어 놓을 자리: <see cref="Index"/> 앞에 넣고, 열 묶음 안이면 그 열(<see cref="EditorBlock.AttrColumn"/> 값, 묶음 밖이면 null).</summary>
        internal sealed class DropTarget
        {
            public int Index;
            public string Column;
            // 표시선 위치(패널 내용 좌표, 왼쪽은 본문 열 기준)
            public double Y;
            public double X;
            public double Width;
        }

        /// <summary>
        /// 블록 편집기의 가상화 패널(ScrollViewer의 IScrollInfo).
        /// <list type="bullet">
        /// <item>화면에 보이는 블록과 위·아래 반 화면 여유분, 그리고 포커스·<c>/</c> 메뉴·끌기 중인 행만 <see cref="BlockRow"/>를 만든다.
        /// 화면에서 벗어난 행은 숨겨 두었다가(최대 <see cref="PoolLimit"/>개) 다른 블록에 다시 묶어 쓴다.</item>
        /// <item>행이 없는 블록의 높이는 종류·글자 수로 추정하고, 한 번 측정한 높이는 (내용·폭이 같으면) 기억한다.
        /// 블록 위치(<see cref="_tops"/>)는 높이 합으로 구하며 구조나 높이가 바뀔 때만 다시 계산한다(O(블록 수) 덧셈).</item>
        /// <item>스크롤 위치는 "어느 블록의 몇 px 아래"(앵커)로 기억한다. 위쪽 블록의 추정 높이가 실제 높이로 바뀌어도 보던 내용이 흔들리지 않는다.</item>
        /// <item>여러 열 묶음은 세로 흐름에서 한 덩어리다: 묶음 안 블록의 <see cref="_tops"/>는 모두 묶음 위쪽 y(검색·앵커용)이고,
        /// 실제 위치·폭은 열마다 쌓은 <see cref="_rowTop"/>·<see cref="_rowX"/>·<see cref="_rowW"/>에 둔다. 묶음 높이는 가장 긴 열.</item>
        /// </list>
        /// </summary>
        private sealed class BlockPanel : Panel, IScrollInfo
        {
            private const double TopMargin = 28;
            private const double BottomPad = 160;
            private const double SideLeft = 8;
            private const double SideRight = 36;
            private const double ColumnGap = 16;
            private const int PoolLimit = 80;

            private readonly BlockEditor _owner;
            private readonly Dictionary<long, HeightEntry> _heights = new Dictionary<long, HeightEntry>();
            private readonly Dictionary<long, BlockRow> _realized = new Dictionary<long, BlockRow>();
            private readonly Stack<BlockRow> _pool = new Stack<BlockRow>();
            private readonly List<int> _arranged = new List<int>();
            private readonly Border _dropLine;
            private double[] _tops = new double[1];       // 세로 흐름 위치(묶음 안은 묶음 위쪽) — 단조 증가, 검색·앵커·전체 높이
            private double[] _rowTop = new double[0];     // 블록의 실제 위쪽 y
            private double[] _rowH = new double[0];
            private double[] _rowX = new double[0];       // 열 안에서의 왼쪽(_columnLeft 기준)·폭
            private double[] _rowW = new double[0];
            private bool _topsDirty = true;
            private double _columnWidth = -1;
            private double _columnLeft;
            private double _offset;
            private long _anchorId = -1;
            private double _anchorDelta;
            private Size _viewport = new Size(800, 600);
            private double _extent;
            private DropTarget _drop;
            private int _dropIndent;

            private sealed class HeightEntry
            {
                public double Height;
                public string Text;
                public BlockKind Kind;
                public int Indent;
                public double Width;
                public int Columns;
                public string Source;
                public string AttrKey;
            }

            public IEnumerable<BlockRow> RealizedRows => _realized.Values;

            public BlockPanel(BlockEditor owner)
            {
                _owner = owner;
                Background = Brushes.Transparent;
                ClipToBounds = true;
                _dropLine = new Border { Height = 3, CornerRadius = new CornerRadius(1.5), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
                _dropLine.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                Children.Add(_dropLine);
                MouseLeftButtonDown += Panel_MouseLeftButtonDown;
            }

            public bool FullWidth { get; set; }

            // ── 행 관리 ────────────────────────────────────────────────────

            /// <summary>새 문서: 모든 행을 거두고 높이 기억·스크롤을 처음으로.</summary>
            public void Reset()
            {
                foreach (var row in _realized.Values.ToList())
                    Release(row);
                _heights.Clear();
                _offset = 0;
                _anchorId = -1;
                _anchorDelta = 0;
                _topsDirty = true;
                InvalidateMeasure();
                ScrollOwner?.InvalidateScrollInfo();
            }

            /// <summary>구조가 바뀐 뒤: 화면에 있는 행만 바뀐 블록에 다시 묶고, 위치를 다시 계산하게 한다.</summary>
            public void SyncRealized()
            {
                foreach (var pair in _realized.ToList())
                {
                    int index;
                    if (!_owner._indexById.TryGetValue(pair.Key, out index) || !_owner.IsVisibleIndex(index))
                    {
                        Release(pair.Value);
                        continue;
                    }
                    var row = pair.Value;
                    var block = _owner._blocks[index];
                    if (!ReferenceEquals(row.Block, block))
                        row.Rebind(block);
                    else
                        row.SyncFromBlock();
                    _owner.ApplyRowContext(row, index);
                }
                _topsDirty = true;
                InvalidateMeasure();
            }

            public BlockRow GetRealized(int index)
            {
                if (index < 0 || index >= _owner._blocks.Count)
                    return null;
                BlockRow row;
                var block = _owner._blocks[index];
                return _realized.TryGetValue(block.Id, out row) && ReferenceEquals(row.Block, block) ? row : null;
            }

            /// <summary>블록의 행을 돌려준다. 행이 없거나 화면 밖이면 스크롤해 만든다.</summary>
            public BlockRow EnsureRealized(int index)
            {
                var row = GetRealized(index);
                if (row != null && IsInViewport(index))
                    return row;
                ScrollIndexIntoView(index);
                _owner.UpdateLayout();
                return GetRealized(index);
            }

            public void ApplySelection(int start, int end)
            {
                foreach (var row in _realized.Values)
                {
                    var index = _owner.IndexOf(row);
                    row.SetSelected(index >= start && index < end);
                }
            }

            /// <summary>화면에 걸친 행(포커스 행 포함)을 문서 순서로. 접근성 트리의 자식.</summary>
            public List<BlockRow> RowsInViewport(BlockRow pinned)
            {
                var result = new List<KeyValuePair<int, BlockRow>>();
                foreach (var index in _arranged)
                {
                    var row = GetRealized(index);
                    if (row != null && (IsInViewport(index) || ReferenceEquals(row, pinned)))
                        result.Add(new KeyValuePair<int, BlockRow>(index, row));
                }
                return result.OrderBy(p => p.Key).Select(p => p.Value).ToList();
            }

            private BlockRow Acquire(int index)
            {
                var block = _owner._blocks[index];
                BlockRow row;
                if (_pool.Count > 0)
                {
                    row = _pool.Pop();
                    row.Rebind(block);
                    row.Root.Visibility = Visibility.Visible;
                }
                else
                {
                    row = new BlockRow(_owner, block);
                    Children.Add(row.Root);
                }
                _realized[block.Id] = row;
                _owner.ApplyRowContext(row, index);
                return row;
            }

            private void Release(BlockRow row)
            {
                BlockRow current;
                if (_realized.TryGetValue(row.Block.Id, out current) && ReferenceEquals(current, row))
                    _realized.Remove(row.Block.Id);
                if (ReferenceEquals(_owner._slashRow, row))
                    _owner.CloseSlash();
                row.Root.Visibility = Visibility.Collapsed;
                row.ResetTransient();
                // 입력란(TextBox)을 만든 행은 입력기(TSF)에 등록돼 있어 다시 쓰면 내용 바꿀 때 멈출 수 있으므로 버린다.
                if (_pool.Count < PoolLimit && !row.HasEditors)
                    _pool.Push(row);
                else
                    Children.Remove(row.Root);
            }

            /// <summary>화면 밖이어도 거두면 안 되는 행: 키보드 포커스·<c>/</c> 메뉴·끌기 중인 행.</summary>
            private IEnumerable<BlockRow> PinnedRows()
            {
                if (_owner._focusedRow != null && _owner._focusedRow.Root.IsKeyboardFocusWithin)
                    yield return _owner._focusedRow;
                if (_owner._slashRow != null)
                    yield return _owner._slashRow;
                if (_owner._dragRow != null)
                    yield return _owner._dragRow;
            }

            // ── 높이·위치 ──────────────────────────────────────────────────

            private void ComputeTops()
            {
                var blocks = _owner._blocks;
                var n = blocks.Count;
                if (_tops.Length != n + 1)
                {
                    _tops = new double[n + 1];
                    _rowTop = new double[n];
                    _rowH = new double[n];
                    _rowX = new double[n];
                    _rowW = new double[n];
                }
                var y = 0.0;
                var i = 0;
                while (i < n)
                {
                    if (InColumnGroup(i))
                    {
                        // 열 묶음: 열마다 아래로 쌓고, 묶음은 가장 긴 열만큼 차지한다
                        var end = _owner._colEnd[i];
                        double[] lefts, widths;
                        ColumnGeometry(i, out lefts, out widths);
                        var bottoms = new double[lefts.Length];
                        for (var c = 0; c < bottoms.Length; c++)
                            bottoms[c] = y;
                        for (var k = i; k < end; k++)
                        {
                            var c = _owner._colIndex[k];
                            _tops[k] = y;
                            _rowTop[k] = bottoms[c];
                            _rowX[k] = lefts[c];
                            _rowW[k] = widths[c];
                            _rowH[k] = HeightOf(k, widths[c]);
                            bottoms[c] += _rowH[k];
                        }
                        y = bottoms.Max();
                        i = end;
                        continue;
                    }
                    _tops[i] = y;
                    _rowTop[i] = y;
                    _rowX[i] = 0;
                    _rowW[i] = _columnWidth;
                    _rowH[i] = HeightOf(i, _columnWidth);
                    y += _rowH[i];
                    i++;
                }
                _tops[n] = y;
                _topsDirty = false;
            }

            private bool InColumnGroup(int index)
            {
                return _owner._colStart.Length == _owner._blocks.Count && _owner._colStart[index] >= 0;
            }

            /// <summary>
            /// 열 묶음(<paramref name="start"/>에서 시작)의 열마다 왼쪽·폭(<see cref="_columnLeft"/> 기준). 행마다 손잡이 자리(<see cref="GutterWidth"/>)가 있으므로
            /// 손잡이를 뺀 본문 폭을 너비 비율로 나눈다.
            /// </summary>
            private void ColumnGeometry(int start, out double[] lefts, out double[] widths)
            {
                var ratios = _owner._colRatios[start];
                var count = ratios.Length;
                lefts = new double[count];
                widths = new double[count];
                var content = Math.Max(count * 60, _columnWidth - count * GutterWidth - (count - 1) * ColumnGap);
                var x = 0.0;
                for (var c = 0; c < count; c++)
                {
                    lefts[c] = x;
                    widths[c] = GutterWidth + content * ratios[c];
                    x += widths[c] + ColumnGap;
                }
            }

            private void EnsureTops()
            {
                if (_topsDirty || _tops.Length != _owner._blocks.Count + 1)
                    ComputeTops();
            }

            private double HeightAt(int index)
            {
                return _rowH[index];
            }

            private int ColumnsAt(int index)
            {
                return index < _owner._tableColumns.Length ? _owner._tableColumns[index] : 0;
            }

            /// <summary>표 모양(열 수와 마지막 행 여부). 높이 기억이 이것까지 같을 때만 다시 쓴다.</summary>
            private int TableShapeAt(int index)
            {
                return ColumnsAt(index) * 2 + (index < _owner._tableLast.Length && _owner._tableLast[index] ? 1 : 0);
            }

            private double HeightOf(int index, double width)
            {
                if (!_owner.IsVisibleIndex(index))
                    return 0;
                var block = _owner._blocks[index];
                HeightEntry entry;
                if (_heights.TryGetValue(block.Id, out entry) && Matches(entry, block, TableShapeAt(index), width))
                    return entry.Height;
                var height = Estimate(block, index, width);
                _heights[block.Id] = NewEntry(block, index, height, width);
                return height;
            }

            private static bool Matches(HeightEntry entry, EditorBlock block, int columns, double width)
            {
                return entry.Kind == block.Kind && entry.Indent == block.Indent && entry.Columns == columns &&
                       Math.Abs(entry.Width - width) < 0.5 && string.Equals(entry.Text, block.Text) &&
                       string.Equals(entry.Source, block.Source) && string.Equals(entry.AttrKey, GetHeightAttrKey(block));
            }

            private static string GetHeightAttrKey(EditorBlock block)
            {
                if (block.Attributes == null)
                    return null;
                return (block.GetAttribute(EditorBlock.AttrWidth) ?? string.Empty) + "|" +
                       (block.GetAttribute(EditorBlock.AttrHeading) ?? string.Empty) + "|" +
                       (block.GetAttribute(EditorBlock.AttrDescription) ?? string.Empty);
            }

            private HeightEntry NewEntry(EditorBlock block, int index, double height, double width)
            {
                return new HeightEntry
                {
                    Height = height,
                    Text = block.Text,
                    Kind = block.Kind,
                    Indent = block.Indent,
                    Width = width,
                    Columns = TableShapeAt(index),
                    Source = block.Source,
                    AttrKey = GetHeightAttrKey(block),
                };
            }

            /// <summary>행을 만들지 않은 블록의 높이 추정. 정확할 필요는 없다(화면에 들어오면 실제 높이로 바뀌고 앵커가 위치를 지킨다).</summary>
            private double Estimate(EditorBlock block, int index, double rowWidth)
            {
                var width = (rowWidth > 0 ? rowWidth : 860) - GutterWidth - block.Indent * IndentWidth;
                switch (block.Kind)
                {
                    case BlockKind.Divider:
                        return 29;
                    case BlockKind.Image:
                        Size size;
                        var path = _owner.ResolveImagePath(block.Source);
                        if (path != null && BlockImageLoader.TryGetSize(path, out size) && size.Width > 0)
                        {
                            double customW;
                            var maxW = double.TryParse(block.GetAttribute(EditorBlock.AttrWidth), out customW) && customW > 0
                                ? Math.Min(width, customW)
                                : Math.Min(width, size.Width);
                            return maxW * size.Height / size.Width + 50;
                        }
                        return 260;
                    case BlockKind.Toc:
                        var headingCount = 0;
                        for (var i = 0; i < _owner._blocks.Count; i++)
                            if (_owner._blocks[i].GetHeadingLevel() > 0)
                                headingCount++;
                        return Math.Max(1, headingCount) * 24 + 44;
                    case BlockKind.Math:
                        return EstimateLines(block.Text, 16, width - 30, false) * 28 + 36;
                    case BlockKind.File:
                    case BlockKind.Page:
                        return 58;
                    case BlockKind.Bookmark:
                        return 76;
                    case BlockKind.TableRow:
                        var columns = Math.Max(1, ColumnsAt(index));
                        var cellWidth = Math.Max(30, width / columns - 14);
                        var lines = 1;
                        foreach (var cell in BlockMarkdown.SplitCells(block.Text))
                            lines = Math.Max(lines, EstimateLines(cell, 15, cellWidth - 22, false));
                        // 표의 마지막 행은 아래에 행 추가(+) 줄이 붙는다
                        return lines * 20 + 9 + (TableShapeAt(index) % 2 == 1 ? TableAddRowHeight : 0);
                    case BlockKind.Heading1:
                        return EstimateLines(block.Text, 28, width, false) * 37 + 30;
                    case BlockKind.Heading2:
                        return EstimateLines(block.Text, 22, width, false) * 29 + 24;
                    case BlockKind.Heading3:
                        return EstimateLines(block.Text, 18, width, false) * 24 + 17;
                    case BlockKind.Code:
                        return EstimateLines(block.Text, 13.5, width - 30, true) * 18 + 52;
                    case BlockKind.Raw:
                        return EstimateLines(block.Text, 13, width - 30, true) * 17.3 + 34;
                    case BlockKind.Callout:
                        return EstimateLines(block.Text, 15, width - 60, false) * 20 + 36;
                    case BlockKind.Quote:
                        return EstimateLines(block.Text, 15, width - 17, false) * 20 + 8;
                    case BlockKind.Bulleted:
                    case BlockKind.Numbered:
                    case BlockKind.Todo:
                    case BlockKind.Toggle:
                        var th = block.GetToggleHeadingLevel();
                        if (th == 1) return EstimateLines(block.Text, 24, width - 26, false) * 33 + 22;
                        if (th == 2) return EstimateLines(block.Text, 20, width - 26, false) * 27 + 18;
                        if (th == 3) return EstimateLines(block.Text, 17, width - 26, false) * 23 + 14;
                        return EstimateLines(block.Text, 15, width - 26, false) * 20 + 8;
                    default:
                        return EstimateLines(block.Text, 15, width, false) * 20 + 8;
                }
            }

            private static int EstimateLines(string text, double fontSize, double width, bool monospace)
            {
                width = Math.Max(40, width);
                var lines = 0;
                var lineWidth = 0.0;
                foreach (var c in text ?? string.Empty)
                {
                    if (c == '\n')
                    {
                        lines += Math.Max(1, (int)Math.Ceiling(lineWidth / width));
                        lineWidth = 0;
                        continue;
                    }
                    lineWidth += monospace ? fontSize * 0.6 : c >= 0x1100 ? fontSize : fontSize * 0.52;
                }
                return lines + Math.Max(1, (int)Math.Ceiling(lineWidth / width));
            }

            /// <summary><paramref name="contentY"/>(맨 위 여백 제외) 위치의 블록: tops[i] ≤ y 인 가장 큰 i. 열 묶음 안이면 묶음 첫 블록.</summary>
            private int IndexAtY(double contentY)
            {
                var n = _owner._blocks.Count;
                if (n == 0)
                    return -1;
                int lo = 0, hi = n - 1;
                while (lo < hi)
                {
                    var mid = (lo + hi + 1) / 2;
                    if (_tops[mid] <= contentY)
                        lo = mid;
                    else
                        hi = mid - 1;
                }
                return InColumnGroup(lo) ? _owner._colStart[lo] : lo;
            }

            /// <summary>열 묶음(<paramref name="start"/>) 안에서 x(<see cref="_columnLeft"/> 기준) 아래의 열 번호.</summary>
            private int ColumnAtX(int start, double x)
            {
                double[] lefts, widths;
                ColumnGeometry(start, out lefts, out widths);
                var column = 0;
                for (var c = 1; c < lefts.Length; c++)
                    if (x >= lefts[c] - ColumnGap / 2)
                        column = c;
                return column;
            }

            /// <summary>열 묶음 안 <paramref name="column"/>열의 블록 범위 [first, end).</summary>
            private void ColumnRange(int start, int column, out int first, out int end)
            {
                var groupEnd = _owner._colEnd[start];
                first = start;
                while (first < groupEnd && _owner._colIndex[first] < column)
                    first++;
                end = first;
                while (end < groupEnd && _owner._colIndex[end] == column)
                    end++;
            }

            private void UpdateAnchor()
            {
                var index = IndexAtY(_offset - TopMargin);
                if (index < 0)
                {
                    _anchorId = -1;
                    return;
                }
                _anchorId = _owner._blocks[index].Id;
                _anchorDelta = _offset - TopMargin - _tops[index];
            }

            private void ResolveOffset()
            {
                var n = _owner._blocks.Count;
                int anchor;
                if (_anchorId >= 0 && _owner._indexById.TryGetValue(_anchorId, out anchor) && anchor < n)
                    _offset = TopMargin + _tops[anchor] + _anchorDelta;
                _extent = TopMargin + _tops[n] + BottomPad;
                var max = Math.Max(0, _extent - _viewport.Height);
                if (_offset > max || _offset < 0 || double.IsNaN(_offset))
                {
                    _offset = Math.Max(0, Math.Min(max, double.IsNaN(_offset) ? 0 : _offset));
                    UpdateAnchor();
                }
            }

            private bool IsInViewport(int index)
            {
                EnsureTops();
                if (index < 0 || index >= _owner._blocks.Count)
                    return false;
                var top = TopMargin + _rowTop[index] - _offset;
                return top + HeightAt(index) > 0 && top < _viewport.Height;
            }

            public void ScrollIndexIntoView(int index)
            {
                if (index < 0 || index >= _owner._blocks.Count)
                    return;
                EnsureTops();
                var top = TopMargin + _rowTop[index];
                var bottom = top + HeightAt(index);
                if (top < _offset)
                    SetVerticalOffset(top - 24);
                else if (bottom > _offset + _viewport.Height)
                    SetVerticalOffset(Math.Min(top - 24, bottom - _viewport.Height + 24));
            }

            // ── 레이아웃 ───────────────────────────────────────────────────

            protected override Size MeasureOverride(Size availableSize)
            {
                var width = double.IsInfinity(availableSize.Width) ? 900 : availableSize.Width;
                var height = double.IsInfinity(availableSize.Height) ? 700 : availableSize.Height;
                _viewport = new Size(width, height);

                var available = Math.Max(120, width - SideLeft - SideRight);
                var column = FullWidth ? available : Math.Min(available, ReadableWidth + GutterWidth);
                if (Math.Abs(column - _columnWidth) > 0.5)
                {
                    _columnWidth = column;
                    _topsDirty = true;
                }
                _columnLeft = SideLeft + (available - column) / 2;

                // 실제 높이가 추정과 다르면 위치가 바뀌어 보이는 범위도 바뀔 수 있어 몇 번 반복한다.
                for (var pass = 0; pass < 4; pass++)
                {
                    EnsureTops();
                    ResolveOffset();
                    Realize();

                    var changed = false;
                    foreach (var index in _arranged)
                    {
                        var row = GetRealized(index);
                        if (row == null)
                            continue;
                        row.Root.Measure(new Size(_rowW[index], double.PositiveInfinity));
                        var measured = row.Root.DesiredSize.Height;
                        if (Math.Abs(measured - HeightAt(index)) > 0.5)
                            changed = true;
                        _heights[row.Block.Id] = NewEntry(row.Block, index, measured, _rowW[index]);
                    }
                    if (!changed)
                        break;
                    _topsDirty = true;
                }
                if (_topsDirty)
                {
                    ComputeTops();
                    ResolveOffset();
                }

                _dropLine.Measure(new Size(width, 3));
                ScrollOwner?.InvalidateScrollInfo();
                return _viewport;
            }

            /// <summary>보이는 범위(위·아래 반 화면 여유)와 고정 행만 남기고 나머지 행은 거둔다.</summary>
            private void Realize()
            {
                var blocks = _owner._blocks;
                var n = blocks.Count;
                _arranged.Clear();
                var needed = new HashSet<long>();
                var buffer = _viewport.Height * 0.5;
                var from = _offset - TopMargin - buffer;
                var to = _offset - TopMargin + _viewport.Height + buffer;
                var first = Math.Max(0, IndexAtY(from));
                for (var i = first; i < n && _tops[i] < to; i++)
                {
                    // 열 묶음 안에서는 실제 위치로 거른다(긴 열의 화면 밖 블록은 만들지 않음)
                    if (!_owner.IsVisibleIndex(i) || _rowTop[i] + _rowH[i] < from || _rowTop[i] > to)
                        continue;
                    _arranged.Add(i);
                    needed.Add(blocks[i].Id);
                }
                foreach (var pinned in PinnedRows())
                {
                    var index = _owner.IndexOf(pinned);
                    BlockRow realized;
                    if (index < 0 || !_owner.IsVisibleIndex(index) || needed.Contains(pinned.Block.Id) ||
                        !_realized.TryGetValue(pinned.Block.Id, out realized) || !ReferenceEquals(realized, pinned))
                        continue;
                    _arranged.Add(index);
                    needed.Add(pinned.Block.Id);
                }

                foreach (var pair in _realized.ToList())
                    if (!needed.Contains(pair.Key))
                        Release(pair.Value);
                foreach (var index in _arranged)
                    if (GetRealized(index) == null)
                        Acquire(index);
            }

            protected override Size ArrangeOverride(Size finalSize)
            {
                EnsureTops();
                foreach (var index in _arranged)
                {
                    var row = GetRealized(index);
                    if (row == null)
                        continue;
                    var y = TopMargin + _rowTop[index] - _offset;
                    row.Root.Arrange(new Rect(_columnLeft + _rowX[index], y, _rowW[index], row.Root.DesiredSize.Height));
                }

                if (_drop != null && _dropLine.Visibility == Visibility.Visible)
                {
                    var y = TopMargin + _drop.Y - _offset;
                    var x = _columnLeft + _drop.X + GutterWidth + _dropIndent * IndentWidth;
                    _dropLine.Arrange(new Rect(x, y - 1.5, Math.Max(0, _columnLeft + _drop.X + _drop.Width - x), 3));
                }
                else
                {
                    _dropLine.Arrange(new Rect(0, 0, 0, 0));
                }
                return finalSize;
            }

            // ── 끌어 옮기기 표시 ───────────────────────────────────────────

            /// <summary>
            /// 패널 좌표 <paramref name="point"/>에 놓으면 들어갈 자리(그 블록 앞). 블록 아래쪽 절반이면 다음 보이는 블록 앞.
            /// 열 묶음 위면 x로 열을 고르고 그 열 안에서 자리를 찾는다(열 끝이면 그 열 마지막 블록 뒤).
            /// </summary>
            public DropTarget DropTargetAt(Point point)
            {
                EnsureTops();
                var n = _owner._blocks.Count;
                var contentY = point.Y + _offset - TopMargin;
                var x = point.X - _columnLeft;
                if (n == 0 || contentY >= _tops[n])
                    return new DropTarget { Index = n, Y = _tops[n], Width = _columnWidth };
                var index = Math.Max(0, IndexAtY(contentY));
                if (InColumnGroup(index))
                {
                    var column = ColumnAtX(index, x);
                    int first, end;
                    ColumnRange(index, column, out first, out end);
                    double[] lefts, widths;
                    ColumnGeometry(index, out lefts, out widths);
                    var target = new DropTarget { Index = end, X = lefts[column], Width = widths[column], Column = _owner._blocks[first].GetAttribute(EditorBlock.AttrColumn) };
                    target.Y = _tops[index];
                    for (var k = first; k < end; k++)
                    {
                        if (!_owner.IsVisibleIndex(k))
                            continue;
                        if (contentY < _rowTop[k] + _rowH[k] / 2)
                        {
                            target.Index = k;
                            target.Y = _rowTop[k];
                            return target;
                        }
                        target.Y = _rowTop[k] + _rowH[k];
                    }
                    return target;
                }
                while (index < n && !_owner.IsVisibleIndex(index))
                    index++;
                if (index < n && contentY > _rowTop[index] + HeightAt(index) / 2)
                {
                    index = _owner.NextVisible(index, 1);
                    if (index < 0)
                        index = n;
                }
                return new DropTarget { Index = index, Y = _tops[Math.Min(index, n)], Width = _columnWidth };
            }

            public void ShowDropLine(DropTarget target, int indent)
            {
                _drop = target;
                _dropIndent = indent;
                _dropLine.Visibility = Visibility.Visible;
                InvalidateArrange();
            }

            public void HideDropLine()
            {
                _drop = null;
                _dropLine.Visibility = Visibility.Collapsed;
                InvalidateArrange();
            }

            private void Panel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            {
                // 행 밖(좌우 여백·문서 아래·열 사이)을 누른 경우만 온다. 행 안은 행이 처리한다.
                e.Handled = true;
                EnsureTops();
                var n = _owner._blocks.Count;
                var position = e.GetPosition(this);
                var contentY = position.Y + _offset - TopMargin;
                if (n == 0 || contentY >= _tops[n])
                {
                    _owner.OnBottomClick();
                    return;
                }
                var index = Math.Max(0, IndexAtY(contentY));
                if (InColumnGroup(index))
                {
                    // 그 열에서 y에 걸친 블록, 없으면 그 열 마지막 블록
                    int first, end;
                    ColumnRange(index, ColumnAtX(index, position.X - _columnLeft), out first, out end);
                    index = Math.Max(first, end - 1);
                    for (var k = first; k < end; k++)
                    {
                        if (contentY < _rowTop[k] + _rowH[k])
                        {
                            index = k;
                            break;
                        }
                    }
                }
                if (_owner.IsEditable(index))
                    _owner.FocusRow(index, int.MaxValue, int.MaxValue);
                else if (_owner.IsVisibleIndex(index))
                    _owner.SelectBlocks(index, index);
            }

            // ── IScrollInfo ────────────────────────────────────────────────

            public bool CanVerticallyScroll { get; set; } = true;
            public bool CanHorizontallyScroll { get; set; }
            public double ExtentWidth => _viewport.Width;
            public double ExtentHeight => _extent;
            public double ViewportWidth => _viewport.Width;
            public double ViewportHeight => _viewport.Height;
            public double HorizontalOffset => 0;
            public double VerticalOffset => _offset;
            public ScrollViewer ScrollOwner { get; set; }

            public void LineUp() => SetVerticalOffset(_offset - 40);
            public void LineDown() => SetVerticalOffset(_offset + 40);
            public void PageUp() => SetVerticalOffset(_offset - _viewport.Height * 0.9);
            public void PageDown() => SetVerticalOffset(_offset + _viewport.Height * 0.9);
            public void MouseWheelUp() => SetVerticalOffset(_offset - 48);
            public void MouseWheelDown() => SetVerticalOffset(_offset + 48);
            public void LineLeft() { }
            public void LineRight() { }
            public void PageLeft() { }
            public void PageRight() { }
            public void MouseWheelLeft() { }
            public void MouseWheelRight() { }
            public void SetHorizontalOffset(double offset) { }

            public void SetVerticalOffset(double offset)
            {
                if (double.IsNaN(offset))
                    return;
                EnsureTops();
                _extent = TopMargin + _tops[_owner._blocks.Count] + BottomPad;
                offset = Math.Max(0, Math.Min(offset, Math.Max(0, _extent - _viewport.Height)));
                if (Math.Abs(offset - _offset) < 0.01)
                    return;
                _offset = offset;
                UpdateAnchor();
                InvalidateMeasure();
                ScrollOwner?.InvalidateScrollInfo();
            }

            public Rect MakeVisible(Visual visual, Rect rectangle)
            {
                if (visual == null || !IsAncestorOf(visual))
                    return Rect.Empty;
                if (rectangle.IsEmpty && visual is UIElement)
                    rectangle = new Rect(((UIElement)visual).RenderSize);
                var bounds = visual.TransformToAncestor(this).TransformBounds(rectangle);
                var delta = 0.0;
                if (bounds.Top < 0)
                    delta = bounds.Top;
                else if (bounds.Bottom > _viewport.Height)
                    delta = Math.Min(bounds.Top, bounds.Bottom - _viewport.Height);
                if (Math.Abs(delta) > 0.5)
                    SetVerticalOffset(_offset + delta);
                bounds.Offset(0, -delta);
                return bounds;
            }
        }
    }
}

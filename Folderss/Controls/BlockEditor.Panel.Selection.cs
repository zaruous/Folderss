using Folderss.Services;
using System;
using System.Windows;

namespace Folderss.Controls
{
    public sealed partial class BlockEditor
    {
        // 가상화 패널의 위치 계산 중 선택·이동(#75)에 쓰는 것. 계산은 BlockSelection(단위 테스트)에 맡긴다.
        private sealed partial class BlockPanel
        {
            /// <summary>패널 y → 내용 y(맨 위 여백 제외, 스크롤 무관). 끄는 중 스크롤해도 시작점을 같은 자리로 두려고 쓴다.</summary>
            public double ToContentY(double panelY) => panelY + _offset - TopMargin;

            /// <summary>내용 y → 지금 패널 y.</summary>
            public double ToPanelY(double contentY) => contentY - _offset + TopMargin;

            /// <summary>
            /// 패널 좌표 아래의 보이는 블록(열 묶음이면 x로 고른 열에서 y에 걸친 블록). 문서 위면 첫 블록, 아래면 마지막 보이는 블록. 블록이 없으면 -1.
            /// </summary>
            public int IndexAtPoint(Point position)
            {
                EnsureTops();
                var n = _owner._blocks.Count;
                if (n == 0)
                    return -1;
                var contentY = ToContentY(position.Y);
                if (contentY >= _tops[n])
                    return _owner.NextVisible(n, -1);
                var index = Math.Max(0, IndexAtY(contentY));
                if (InColumnGroup(index))
                {
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
                return VisibleAtOrBefore(index);
            }

            /// <summary>
            /// 사각 영역 선택: 내용 y 구간에 걸친 블록 범위. 가로로 본문(손잡이 포함) 폭에 닿지 않거나 문서 위·아래 빈 곳뿐이면 false.
            /// 열 묶음은 한 덩어리로 넣는다(선택은 연속 범위).
            /// </summary>
            public bool SpanRange(double contentY1, double contentY2, double x1, double x2, out int first, out int last)
            {
                first = last = -1;
                EnsureTops();
                var n = _owner._blocks.Count;
                if (Math.Max(x1, x2) < _columnLeft || Math.Min(x1, x2) > _columnLeft + _columnWidth)
                    return false;
                if (!BlockSelection.SpanRange(_tops, n, contentY1, contentY2, out first, out last))
                    return false;
                if (InColumnGroup(first))
                    first = _owner._colStart[first];
                if (InColumnGroup(last))
                    last = _owner._colEnd[last] - 1;
                first = VisibleAtOrBefore(first);
                last = VisibleAtOrBefore(last);
                return first >= 0 && last >= 0;
            }

            /// <summary>PageUp(-1)/PageDown(+1): <paramref name="from"/>에서 한 화면(뷰포트의 90%) 떨어진 보이는 블록.</summary>
            public int PageTargetFrom(int from, int direction)
            {
                EnsureTops();
                var n = _owner._blocks.Count;
                var target = BlockSelection.PageTarget(_tops, n, from, PageDistance, direction, _owner.IsVisibleIndex);
                if (target >= 0 && InColumnGroup(target))
                {
                    // 열 묶음 안 블록은 세로 위치가 모두 묶음 위쪽이다. 묶음 밖에서 들어오면 묶음 첫 블록, 묶음 안에서는 묶음을 벗어난다.
                    var groupStart = _owner._colStart[target];
                    var groupEnd = _owner._colEnd[target];
                    if (from >= groupStart && from < groupEnd)
                        target = direction > 0 ? (groupEnd < n ? groupEnd : groupEnd - 1) : (from != groupStart ? groupStart : Math.Max(0, groupStart - 1));
                    else
                        target = groupStart;
                }
                return direction > 0 ? VisibleAtOrAfter(target) : VisibleAtOrBefore(target);
            }

            /// <summary>PageUp/PageDown 한 번에 움직이는 거리.</summary>
            public double PageDistance => Math.Max(40, _viewport.Height * 0.9);

            private int VisibleAtOrBefore(int index)
            {
                if (index < 0)
                    return -1;
                if (_owner.IsVisibleIndex(index))
                    return index;
                var previous = _owner.NextVisible(index, -1);
                return previous >= 0 ? previous : _owner.NextVisible(index, 1);
            }

            private int VisibleAtOrAfter(int index)
            {
                if (index < 0 || _owner.IsVisibleIndex(index))
                    return index;
                var next = _owner.NextVisible(index, 1);
                return next >= 0 ? next : VisibleAtOrBefore(index);
            }
        }
    }
}

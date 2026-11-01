using Folderss.Models;
using System;
using System.Collections.Generic;

namespace Folderss.Services
{
    /// <summary>
    /// 블록 편집기 선택·이동 계산(#75 "선택·이동"). WPF를 쓰지 않아 단위 테스트한다.
    /// 위치 배열 <c>tops</c>는 가상화 패널의 세로 흐름 위치다 — 길이 n+1, 단조 증가(같을 수 있음), <c>tops[i]</c>는 블록 i의 위쪽 y,
    /// <c>tops[n]</c>은 문서 끝. 접힌 블록은 높이 0이라 앞뒤 블록과 같은 값을 가진다.
    /// </summary>
    public static class BlockSelection
    {
        /// <summary><paramref name="y"/> 위치의 블록: <c>tops[i] ≤ y</c>인 가장 큰 i(맨 위보다 위면 0). 블록이 없으면 -1.</summary>
        public static int IndexAt(IList<double> tops, int count, double y)
        {
            if (count <= 0)
                return -1;
            int lo = 0, hi = count - 1;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) / 2;
                if (tops[mid] <= y)
                    lo = mid;
                else
                    hi = mid - 1;
            }
            return lo;
        }

        /// <summary>
        /// 세로 구간 [<paramref name="y1"/>, <paramref name="y2"/>](순서 무관)에 걸친 블록 범위 [first, last]. 구간이 문서 위나 아래 빈 곳에만 있으면 false.
        /// 사각 영역 선택에 쓴다 — 선택은 연속 범위라 세로 구간만 본다.
        /// </summary>
        public static bool SpanRange(IList<double> tops, int count, double y1, double y2, out int first, out int last)
        {
            first = last = -1;
            if (count <= 0)
                return false;
            var lo = Math.Min(y1, y2);
            var hi = Math.Max(y1, y2);
            if (hi < tops[0] || lo >= tops[count])
                return false;
            first = IndexAt(tops, count, Math.Max(lo, tops[0]));
            last = IndexAt(tops, count, Math.Min(hi, tops[count]));
            // 끝이 문서 끝(tops[count])에 닿으면 IndexAt은 마지막 블록을 준다. 높이 0인 블록이 끝에 걸리면 앞쪽으로 당긴다.
            while (last > first && tops[last] >= hi)
                last--;
            return true;
        }

        /// <summary>
        /// 한 화면 이동(PageUp/PageDown) 목표 블록. <paramref name="from"/> 위쪽에서 <paramref name="direction"/> 방향으로 <paramref name="page"/>만큼 떨어진 자리의 블록.
        /// 같은 블록이면(블록 하나가 화면보다 큼) 한 블록 넘어간다. 보이지 않는 블록은 <paramref name="isVisible"/>로 건너뛴다(앞쪽 보이는 블록으로).
        /// </summary>
        public static int PageTarget(IList<double> tops, int count, int from, double page, int direction, Func<int, bool> isVisible = null)
        {
            if (count <= 0)
                return -1;
            from = Math.Max(0, Math.Min(from, count - 1));
            var y = tops[from] + direction * Math.Max(1, page);
            var target = y >= tops[count] ? count - 1 : IndexAt(tops, count, y);
            if (target == from)
                target = Math.Max(0, Math.Min(count - 1, from + direction));
            if (isVisible != null)
            {
                var probe = target;
                while (probe >= 0 && !isVisible(probe))
                    probe--;
                if (probe < 0)
                {
                    probe = target;
                    while (probe < count && !isVisible(probe))
                        probe++;
                }
                if (probe >= count)
                    return from;
                target = probe;
            }
            return target;
        }

        /// <summary>
        /// [start, end) 블록(선택한 여러 블록 + 자식)을 <paramref name="dropIndex"/> 블록 앞(옮기기 전 위치 기준)으로 옮긴다. 한 번에 옮기므로 실행 취소도 한 단계.
        /// 들여쓰기는 범위의 가장 얕은 블록을 놓을 자리 블록 수준에 맞춰 함께 옮기고(상대 깊이 유지), 모든 블록을 <paramref name="column"/> 열로(묶음 밖이면 null) 둔다.
        /// 들여쓰기 규칙은 호출한 쪽이 <see cref="BlockOperations.NormalizeIndents"/>로 맞춘다. 옮긴 범위의 새 시작 위치, 범위 안에 놓거나 범위가 잘못되면 -1.
        /// </summary>
        public static int MoveRangeTo(List<EditorBlock> blocks, int start, int end, int dropIndex, string column)
        {
            if (start < 0 || end > blocks.Count || start >= end || dropIndex < 0 || dropIndex > blocks.Count)
                return -1;
            if (dropIndex > start && dropIndex < end)
                return -1;
            var moving = blocks.GetRange(start, end - start);
            blocks.RemoveRange(start, end - start);
            var insertAt = dropIndex > start ? dropIndex - (end - start) : dropIndex;
            var targetIndent = insertAt < blocks.Count ? blocks[insertAt].Indent : 0;
            var top = int.MaxValue;
            foreach (var block in moving)
                top = Math.Min(top, block.Indent);
            var delta = targetIndent - top;
            foreach (var block in moving)
            {
                block.Indent = Math.Max(0, block.Indent + delta);
                block.SetAttribute(EditorBlock.AttrColumn, column);
            }
            blocks.InsertRange(insertAt, moving);
            return insertAt;
        }
    }
}

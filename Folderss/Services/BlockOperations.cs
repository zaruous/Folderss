using Folderss.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Folderss.Services
{
    /// <summary>
    /// 블록 목록 구조 연산(하위 트리, 들여쓰기 규칙, 이동, 번호 표시, 접힘). 블록 편집기 컨트롤과 Markdown 쓰기가 함께 쓰며
    /// WPF를 쓰지 않아 단위 테스트한다. 중첩은 <see cref="EditorBlock.Indent"/>만으로 나타낸다 — 블록 i의 자식은
    /// i 바로 뒤에서 i보다 깊은 블록들이다.
    /// </summary>
    public static class BlockOperations
    {
        /// <summary>블록 <paramref name="index"/>와 그 자식들이 끝나는 위치(포함하지 않음).</summary>
        public static int SubtreeEnd(IList<EditorBlock> blocks, int index)
        {
            var indent = blocks[index].Indent;
            var end = index + 1;
            while (end < blocks.Count && blocks[end].Indent > indent)
                end++;
            return end;
        }

        /// <summary>범위 끝 블록의 자식까지 포함하도록 범위를 늘린 끝 위치.</summary>
        public static int ExtendToSubtrees(IList<EditorBlock> blocks, int start, int end)
        {
            var result = end;
            for (var i = start; i < end; i++)
                result = Math.Max(result, SubtreeEnd(blocks, i));
            return result;
        }

        /// <summary>
        /// 앞 블록 아래로 한 단계 들여쓸 수 있는지. 앞 블록이 목록·토글이면 그 자식이 되고, 앞 블록이 이미 더 깊으면
        /// 그 조상(목록·토글)의 자식이 된다. 문단 아래 자식은 Markdown으로 나타낼 수 없어 허용하지 않는다.
        /// </summary>
        public static bool CanIndent(IList<EditorBlock> blocks, int index)
        {
            if (index <= 0 || index >= blocks.Count)
                return false;
            var previous = blocks[index - 1];
            var target = blocks[index].Indent + 1;
            return previous.Indent >= target || (previous.Indent == target - 1 && EditorBlock.IsContainer(previous.Kind));
        }

        /// <summary>
        /// 들여쓰기를 규칙에 맞게 고친다: 첫 블록은 0, 그 뒤는 앞 블록 + (앞 블록이 목록·토글이면 1, 아니면 0)까지.
        /// 블록을 지우거나 옮긴 뒤 부모를 잃은 자식이 생기면 이것으로 끌어올린다.
        /// </summary>
        public static void NormalizeIndents(IList<EditorBlock> blocks)
        {
            for (var i = 0; i < blocks.Count; i++)
            {
                var max = i == 0 ? 0 : blocks[i - 1].Indent + (EditorBlock.IsContainer(blocks[i - 1].Kind) ? 1 : 0);
                if (blocks[i].Indent > max)
                    blocks[i].Indent = max;
                if (blocks[i].Indent < 0)
                    blocks[i].Indent = 0;
            }
        }

        /// <summary>
        /// [start, end) 범위(자식 포함)를 형제 하나만큼 위(-1)·아래(+1)로 옮긴다. 부모의 첫 자식을 위로 올리면 부모 앞으로 나가며
        /// 부모와 같은 수준이 되고, 마지막 자식을 내리면 부모 다음 형제 뒤로 나간다. 옮긴 범위의 새 시작 위치, 못 옮기면 -1.
        /// </summary>
        public static int MoveRange(List<EditorBlock> blocks, int start, int end, int direction)
        {
            if (start < 0 || end > blocks.Count || start >= end)
                return -1;
            var top = Enumerable.Range(start, end - start).Min(i => blocks[i].Indent);

            if (direction < 0)
            {
                if (start == 0)
                    return -1;
                var k = start - 1;
                while (k > 0 && blocks[k].Indent > top)
                    k--;
                var delta = Math.Min(0, blocks[k].Indent - top);
                var moving = blocks.GetRange(start, end - start);
                var column = blocks[k].GetAttribute(EditorBlock.AttrColumn);
                foreach (var block in moving)
                {
                    block.Indent += delta;
                    block.SetAttribute(EditorBlock.AttrColumn, column);   // 넘어간 블록의 열로(열 경계·묶음 경계를 넘으면 그 열에 들어가거나 묶음에서 빠짐)
                }
                blocks.RemoveRange(start, end - start);
                blocks.InsertRange(k, moving);
                return k;
            }

            if (end >= blocks.Count)
                return -1;
            var next = blocks[end];
            var shift = Math.Min(0, next.Indent - top);
            var nextEnd = SubtreeEnd(blocks, end);
            var range = blocks.GetRange(start, end - start);
            foreach (var block in range)
            {
                block.Indent += shift;
                block.SetAttribute(EditorBlock.AttrColumn, next.GetAttribute(EditorBlock.AttrColumn));
            }
            blocks.RemoveRange(start, end - start);
            var insertAt = nextEnd - (end - start);
            blocks.InsertRange(insertAt, range);
            return insertAt;
        }

        /// <summary>
        /// 목록 앞에 표시할 글자. 번호 목록은 수준별로 1. → a. → i. 순서(Notion과 같음), 글머리 기호는 • → ◦ → ▪.
        /// 번호는 같은 수준에 번호 목록이 아닌 블록이 끼면 1부터 다시 센다(Markdown 쓰기와 같은 규칙).
        /// </summary>
        public static string[] ListLabels(IList<EditorBlock> blocks)
        {
            var numbers = ListNumbers(blocks);
            var labels = new string[blocks.Count];
            for (var i = 0; i < blocks.Count; i++)
                labels[i] = FormatListLabel(blocks[i], numbers[i]);
            return labels;
        }

        /// <summary>
        /// 번호 목록 블록의 번호(그 밖은 0). 큰 문서에서 구조가 바뀔 때마다 다시 세므로 문자열·컬렉션을 만들지 않는다.
        /// 표시 글자는 화면에 보이는 블록만 <see cref="FormatListLabel"/>로 만든다.
        /// </summary>
        public static int[] ListNumbers(IList<EditorBlock> blocks)
        {
            var numbers = new int[blocks.Count];
            var counters = new List<int>();   // 수준별 카운터
            for (var i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                var indent = Math.Max(0, block.Indent);
                if (counters.Count > indent + 1)
                    counters.RemoveRange(indent + 1, counters.Count - indent - 1);
                while (counters.Count <= indent)
                    counters.Add(0);

                if (block.Kind == BlockKind.Numbered)
                    numbers[i] = ++counters[indent];
                else if (!(block.Kind == BlockKind.Paragraph && IsBlank(block.Text)))
                    counters[indent] = 0;
            }
            return numbers;
        }

        public static string FormatListLabel(EditorBlock block, int number)
        {
            if (block.Kind == BlockKind.Numbered)
                return NumberLabel(number, block.Indent) + ".";
            if (block.Kind == BlockKind.Bulleted)
                return BulletGlyphs[block.Indent % BulletGlyphs.Length];
            return null;
        }

        private static bool IsBlank(string text)
        {
            foreach (var c in text ?? string.Empty)
                if (!char.IsWhiteSpace(c))
                    return false;
            return true;
        }

        /// <summary>
        /// 표 구조: 같은 들여쓰기로 이어진 표 행이 한 표이고 첫 행이 머리글이다. <paramref name="columns"/>는 표 행마다 그 표의 열 수
        /// (표 안에서 가장 많은 셀 수), 표가 아닌 블록은 0.
        /// </summary>
        public static void TableLayout(IList<EditorBlock> blocks, out int[] columns, out bool[] header)
        {
            columns = new int[blocks.Count];
            header = new bool[blocks.Count];
            var i = 0;
            while (i < blocks.Count)
            {
                if (blocks[i].Kind != BlockKind.TableRow)
                {
                    i++;
                    continue;
                }
                var start = i;
                var count = 0;
                while (i < blocks.Count && blocks[i].Kind == BlockKind.TableRow && blocks[i].Indent == blocks[start].Indent)
                {
                    count = Math.Max(count, BlockMarkdown.CountCells(blocks[i].Text));
                    i++;
                }
                header[start] = true;
                for (var k = start; k < i; k++)
                    columns[k] = count;
            }
        }

        private static readonly string[] BulletGlyphs = { "•", "◦", "▪" };

        private static string NumberLabel(int n, int indent)
        {
            switch (indent % 3)
            {
                case 1: return ToLetters(n);
                case 2: return ToRoman(n);
                default: return n.ToString();
            }
        }

        private static string ToLetters(int n)
        {
            var result = string.Empty;
            while (n > 0)
            {
                n--;
                result = (char)('a' + n % 26) + result;
                n /= 26;
            }
            return result;
        }

        private static string ToRoman(int n)
        {
            if (n <= 0 || n >= 4000)
                return n.ToString();
            int[] values = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            string[] numerals = { "m", "cm", "d", "cd", "c", "xc", "l", "xl", "x", "ix", "v", "iv", "i" };
            var result = string.Empty;
            for (var i = 0; i < values.Length; i++)
                while (n >= values[i])
                {
                    result += numerals[i];
                    n -= values[i];
                }
            return result;
        }

        /// <summary>접힌 토글의 자식을 숨긴 화면 표시 여부.</summary>
        public static bool[] VisibleFlags(IList<EditorBlock> blocks)
        {
            var visible = new bool[blocks.Count];
            var hiddenBelow = int.MaxValue;
            for (var i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                if (block.Indent > hiddenBelow)
                    continue;
                hiddenBelow = int.MaxValue;
                visible[i] = true;
                if (block.Kind == BlockKind.Toggle && block.Collapsed)
                    hiddenBelow = block.Indent;
            }
            return visible;
        }

        // ── 여러 열 배치 ───────────────────────────────────────────────────

        /// <summary>한 묶음에 둘 수 있는 가장 많은 열 수.</summary>
        public const int MaxColumns = 5;

        /// <summary>
        /// 여러 열 배치(<see cref="EditorBlock.AttrColumn"/>)를 정리하고 위치마다 묶음 정보를 돌려준다. 정리한 결과는 블록 속성에 다시 쓴다.
        /// <list type="number">
        /// <item>같은 묶음의 두 블록 사이에 열 속성이 없는 블록이 끼면 앞 블록의 열에 넣는다(묶음 안에서 붙여넣거나 옮겨 온 블록).</item>
        /// <item>같은 묶음id가 떨어져 다시 나오면 뒤쪽은 묶음에서 뺀다.</item>
        /// <item>열 번호는 앞 블록보다 작아지지 않게 하고(작으면 앞 블록의 열) 0부터 빈틈없이 다시 매긴다 — 빈 열은 사라진다.</item>
        /// <item>열이 하나뿐인 묶음은 푼다.</item>
        /// <item>너비 비율(<see cref="EditorBlock.AttrColumnRatio"/>)은 묶음 첫 블록에만, 열 수와 맞을 때만 남긴다.</item>
        /// </list>
        /// <paramref name="groupStart"/>·<paramref name="groupEnd"/>는 그 블록이 든 묶음의 [시작, 끝)(묶음 밖은 -1),
        /// <paramref name="column"/>은 열 번호, <paramref name="ratios"/>는 묶음 시작 위치에만 열마다 너비 비율(합 1).
        /// </summary>
        public static void ColumnLayout(IList<EditorBlock> blocks, out int[] groupStart, out int[] groupEnd, out int[] column, out double[][] ratios)
        {
            var n = blocks.Count;
            var ids = new string[n];
            var raw = new int[n];
            for (var i = 0; i < n; i++)
                ParseColumn(blocks[i].GetAttribute(EditorBlock.AttrColumn), out ids[i], out raw[i]);

            for (var i = 1; i < n; i++)
            {
                if (ids[i] != null || ids[i - 1] == null)
                    continue;
                var j = i;
                while (j < n && ids[j] == null)
                    j++;
                if (j < n && ids[j] == ids[i - 1])
                {
                    for (var k = i; k < j; k++)
                    {
                        ids[k] = ids[i - 1];
                        raw[k] = raw[i - 1];
                    }
                }
                i = j;
            }

            groupStart = new int[n];
            groupEnd = new int[n];
            column = new int[n];
            ratios = new double[n][];
            for (var i = 0; i < n; i++)
                groupStart[i] = groupEnd[i] = -1;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var pos = 0;
            while (pos < n)
            {
                if (ids[pos] == null)
                {
                    pos++;
                    continue;
                }
                var start = pos;
                var id = ids[pos];
                while (pos < n && ids[pos] == id)
                    pos++;
                var end = pos;
                if (!seen.Add(id))
                {
                    for (var k = start; k < end; k++)
                        ids[k] = null;
                    continue;
                }

                var count = 0;
                var previous = int.MinValue;
                for (var k = start; k < end; k++)
                {
                    var value = Math.Max(raw[k], previous);
                    if (value != previous)
                        count++;
                    previous = value;
                    column[k] = count - 1;
                }
                if (count < 2)
                {
                    for (var k = start; k < end; k++)
                        ids[k] = null;
                    continue;
                }
                for (var k = start; k < end; k++)
                {
                    groupStart[k] = start;
                    groupEnd[k] = end;
                }
                ratios[start] = ParseColumnRatio(blocks[start].GetAttribute(EditorBlock.AttrColumnRatio), count) ?? EqualRatios(count);
            }

            for (var k = 0; k < n; k++)
            {
                var inGroup = groupStart[k] >= 0;
                var value = inGroup ? ids[k] + "/" + column[k].ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
                if (blocks[k].GetAttribute(EditorBlock.AttrColumn) != value)
                    blocks[k].SetAttribute(EditorBlock.AttrColumn, value);
                var ratio = blocks[k].GetAttribute(EditorBlock.AttrColumnRatio);
                if (ratio != null && (groupStart[k] != k || ParseColumnRatio(ratio, column[groupEnd[k] - 1] + 1) == null))
                    blocks[k].SetAttribute(EditorBlock.AttrColumnRatio, null);
            }
        }

        /// <summary><c>묶음id/열번호</c>를 나눈다. 형식이 틀리면 id는 null.</summary>
        public static void ParseColumn(string value, out string id, out int column)
        {
            id = null;
            column = 0;
            if (string.IsNullOrEmpty(value))
                return;
            var slash = value.LastIndexOf('/');
            int parsed;
            if (slash <= 0 || !int.TryParse(value.Substring(slash + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out parsed))
                return;
            id = value.Substring(0, slash);
            column = parsed;
        }

        /// <summary>너비 비율 <c>1:2</c>를 합 1인 비율로. 열 수와 다르거나 틀린 값이면 null.</summary>
        public static double[] ParseColumnRatio(string value, int count)
        {
            if (string.IsNullOrEmpty(value) || count < 2)
                return null;
            var parts = value.Split(':');
            if (parts.Length != count)
                return null;
            var result = new double[count];
            var sum = 0.0;
            for (var i = 0; i < count; i++)
            {
                double part;
                if (!double.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out part) || part <= 0 || part > 100)
                    return null;
                result[i] = part;
                sum += part;
            }
            for (var i = 0; i < count; i++)
                result[i] /= sum;
            return result;
        }

        private static double[] EqualRatios(int count)
        {
            return Enumerable.Repeat(1.0 / count, count).ToArray();
        }

        /// <summary>새 열 묶음 id(문서 안에서만 구분하면 되므로 짧게).</summary>
        public static string NewColumnGroupId()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        /// <summary>
        /// [<paramref name="start"/>, <paramref name="end"/>) 블록(자식 포함)을 <paramref name="count"/>열로 나란히 놓는다.
        /// 최상위 블록(하위 트리 단위)을 앞에서부터 열에 고르게 나누고, 블록이 열 수보다 적으면 남는 열에 빈 문단을 둔다.
        /// 넣은 빈 문단 수를 돌려준다. 정리는 <see cref="ColumnLayout"/>이 한다.
        /// </summary>
        public static int MakeColumns(List<EditorBlock> blocks, int start, int end, int count)
        {
            count = Math.Max(2, Math.Min(MaxColumns, count));
            end = ExtendToSubtrees(blocks, start, end);
            var units = new List<int>();
            for (var i = start; i < end; i = SubtreeEnd(blocks, i))
                units.Add(i);
            var id = NewColumnGroupId();
            var indent = blocks[start].Indent;
            for (var u = 0; u < units.Count; u++)
            {
                var column = units.Count >= count ? u * count / units.Count : u;
                var unitEnd = u + 1 < units.Count ? units[u + 1] : end;
                for (var k = units[u]; k < unitEnd; k++)
                {
                    blocks[k].SetAttribute(EditorBlock.AttrColumn, id + "/" + column.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    blocks[k].SetAttribute(EditorBlock.AttrColumnRatio, null);
                }
            }
            var added = 0;
            for (var column = units.Count; column < count; column++)
            {
                var filler = new EditorBlock(BlockKind.Paragraph, string.Empty, indent);
                filler.SetAttribute(EditorBlock.AttrColumn, id + "/" + column.ToString(System.Globalization.CultureInfo.InvariantCulture));
                blocks.Insert(end + added, filler);
                added++;
            }
            return added;
        }

        /// <summary>
        /// 열 묶음 안의 [<paramref name="start"/>, <paramref name="end"/>) 블록을 옆 열(<paramref name="direction"/> -1 왼쪽, +1 오른쪽)의 끝으로 옮긴다.
        /// 오른쪽 끝 열에서 오른쪽이면 새 열을 만든다(<see cref="MaxColumns"/>까지). 옮긴 블록의 새 시작 위치를 돌려주고, 옮기지 못하면 -1.
        /// <paramref name="groupStart"/>·<paramref name="groupEnd"/>·<paramref name="column"/>은 옮기기 전 <see cref="ColumnLayout"/> 결과.
        /// </summary>
        public static int MoveToColumn(List<EditorBlock> blocks, int start, int end, int direction, int[] groupStart, int[] groupEnd, int[] column)
        {
            if (start < 0 || start >= blocks.Count || groupStart[start] < 0)
                return -1;
            var gs = groupStart[start];
            var ge = groupEnd[start];
            end = Math.Min(ExtendToSubtrees(blocks, start, end), ge);
            var current = column[start];
            var columns = column[ge - 1] + 1;
            var target = current + Math.Sign(direction);
            if (target < 0 || target >= MaxColumns || (target >= columns && columns >= MaxColumns))
                return -1;
            string id;
            int ignored;
            ParseColumn(blocks[start].GetAttribute(EditorBlock.AttrColumn), out id, out ignored);
            if (id == null)
                return -1;

            // 목표 열의 끝(오른쪽 새 열이면 묶음 끝) 바로 뒤
            var insertBefore = ge;
            for (var k = gs; k < ge; k++)
            {
                if (column[k] > target)
                {
                    insertBefore = k;
                    break;
                }
            }
            var moving = blocks.GetRange(start, end - start);
            foreach (var block in moving)
                block.SetAttribute(EditorBlock.AttrColumn, id + "/" + target.ToString(System.Globalization.CultureInfo.InvariantCulture));
            blocks.RemoveRange(start, end - start);
            if (insertBefore > start)
                insertBefore -= end - start;
            blocks.InsertRange(insertBefore, moving);
            return insertBefore;
        }

        /// <summary><paramref name="index"/>가 든 열 묶음을 푼다(블록은 그 순서대로 세로로 남는다).</summary>
        public static void UngroupColumns(IList<EditorBlock> blocks, int index, int[] groupStart, int[] groupEnd)
        {
            if (index < 0 || index >= blocks.Count || groupStart[index] < 0)
                return;
            for (var k = groupStart[index]; k < groupEnd[index]; k++)
            {
                blocks[k].SetAttribute(EditorBlock.AttrColumn, null);
                blocks[k].SetAttribute(EditorBlock.AttrColumnRatio, null);
            }
        }
    }
}

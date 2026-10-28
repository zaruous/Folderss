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
                foreach (var block in moving)
                    block.Indent += delta;
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
                block.Indent += shift;
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
    }
}

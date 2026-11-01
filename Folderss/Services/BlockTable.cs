using Folderss.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Folderss.Services
{
    /// <summary>
    /// 블록 편집기 표 연산(#75): 행·열 삽입·삭제·이동, 열 정렬·너비, 머리글 표시. 표는 같은 들여쓰기로 이어진 <see cref="BlockKind.TableRow"/>
    /// 블록 [start, end)이고, 표 전체 속성은 첫 행(Markdown 머리글)에 둔다:
    /// <list type="bullet">
    /// <item><see cref="EditorBlock.TableAlign"/> — 열 정렬(Markdown 구분 행 <c>:--</c>·<c>:-:</c>·<c>--:</c>로 저장)</item>
    /// <item><see cref="EditorBlock.AttrColWidths"/>(<c>colwidths</c>) — 열 너비(사이드카)</item>
    /// <item><see cref="EditorBlock.AttrNoHeader"/>(<c>noheader</c>) — 머리글 표시 끄기(사이드카)</item>
    /// </list>
    /// 첫 행이 바뀌는 연산(맨 위 행 삽입·첫 행 삭제·첫 행 이동)은 이 속성들을 새 첫 행으로 옮긴다(<see cref="TransferHeader"/>).
    /// WPF를 쓰지 않아 테스트 프로젝트에서 소스 링크로 검증한다.
    /// </summary>
    public static class BlockTable
    {
        public const string AlignLeft = "l";
        public const string AlignCenter = "c";
        public const string AlignRight = "r";

        /// <summary>열 너비 최솟값(px). 끌어서 이보다 좁게 만들 수 없다.</summary>
        public const double MinColumnWidth = 40;

        /// <summary>첫 행에 두는 표 전체 사이드카 속성.</summary>
        public static readonly string[] HeaderAttributes = { EditorBlock.AttrColWidths, EditorBlock.AttrNoHeader };

        /// <summary><paramref name="index"/>가 속한 표의 [start, end). 표 행이 아니면 start = end = index.</summary>
        public static void Range(IList<EditorBlock> blocks, int index, out int start, out int end)
        {
            start = end = index;
            if (index < 0 || index >= blocks.Count || blocks[index].Kind != BlockKind.TableRow)
                return;
            var indent = blocks[index].Indent;
            while (start > 0 && blocks[start - 1].Kind == BlockKind.TableRow && blocks[start - 1].Indent == indent)
                start--;
            end = index + 1;
            while (end < blocks.Count && blocks[end].Kind == BlockKind.TableRow && blocks[end].Indent == indent)
                end++;
        }

        /// <summary>표의 열 수(가장 많은 셀 수, 최소 1).</summary>
        public static int ColumnCount(IList<EditorBlock> blocks, int start, int end)
        {
            var count = 1;
            for (var i = start; i < end; i++)
                count = Math.Max(count, BlockMarkdown.CountCells(blocks[i].Text));
            return count;
        }

        /// <summary>쉼표 목록을 <paramref name="count"/>개로 맞춘다(모자라면 빈 값, 넘치면 자름).</summary>
        public static List<string> SplitList(string value, int count)
        {
            var list = string.IsNullOrEmpty(value) ? new List<string>() : value.Split(',').Select(v => v.Trim()).ToList();
            while (list.Count < count)
                list.Add(string.Empty);
            if (list.Count > count)
                list.RemoveRange(count, list.Count - count);
            return list;
        }

        private static List<string> Cells(EditorBlock row, int columns)
        {
            var cells = BlockMarkdown.SplitCells(row.Text).ToList();
            while (cells.Count < columns)
                cells.Add(string.Empty);
            return cells;
        }

        /// <summary>열 너비 목록을 속성으로. 모두 자동이면 속성을 지운다.</summary>
        private static void SetWidthList(EditorBlock header, List<string> widths)
        {
            header.SetAttribute(EditorBlock.AttrColWidths, widths.All(w => w.Length == 0) ? null : string.Join(",", widths));
        }

        // ── 열 ──────────────────────────────────────────────────────────

        /// <summary><paramref name="at"/>(0 ~ 열 수) 위치에 빈 열을 넣는다. 정렬·너비도 같은 자리에 빈 값(기본·자동)이 들어간다.</summary>
        public static void InsertColumn(IList<EditorBlock> blocks, int start, int end, int at)
        {
            if (start >= end)
                return;
            var columns = ColumnCount(blocks, start, end);
            at = Math.Max(0, Math.Min(at, columns));
            for (var i = start; i < end; i++)
            {
                var cells = Cells(blocks[i], columns);
                cells.Insert(at, string.Empty);
                blocks[i].Text = BlockMarkdown.JoinCells(cells);
            }
            var header = blocks[start];
            var aligns = SplitList(header.TableAlign, columns);
            aligns.Insert(at, string.Empty);
            header.TableAlign = string.Join(",", aligns);
            if (header.GetAttribute(EditorBlock.AttrColWidths) != null)
            {
                var widths = SplitList(header.GetAttribute(EditorBlock.AttrColWidths), columns);
                widths.Insert(at, string.Empty);
                SetWidthList(header, widths);
            }
        }

        /// <summary><paramref name="column"/>번 열을 지운다. 열이 하나뿐이면 지우지 않고 false.</summary>
        public static bool DeleteColumn(IList<EditorBlock> blocks, int start, int end, int column)
        {
            if (start >= end)
                return false;
            var columns = ColumnCount(blocks, start, end);
            if (columns <= 1 || column < 0 || column >= columns)
                return false;
            for (var i = start; i < end; i++)
            {
                var cells = Cells(blocks[i], columns);
                cells.RemoveAt(column);
                blocks[i].Text = BlockMarkdown.JoinCells(cells);
            }
            var header = blocks[start];
            var aligns = SplitList(header.TableAlign, columns);
            aligns.RemoveAt(column);
            header.TableAlign = string.Join(",", aligns);
            if (header.GetAttribute(EditorBlock.AttrColWidths) != null)
            {
                var widths = SplitList(header.GetAttribute(EditorBlock.AttrColWidths), columns);
                widths.RemoveAt(column);
                SetWidthList(header, widths);
            }
            return true;
        }

        /// <summary><paramref name="from"/>번 열을 <paramref name="to"/>번 자리로 옮긴다(셀·정렬·너비 함께). 바뀌지 않으면 false.</summary>
        public static bool MoveColumn(IList<EditorBlock> blocks, int start, int end, int from, int to)
        {
            if (start >= end)
                return false;
            var columns = ColumnCount(blocks, start, end);
            if (from < 0 || from >= columns || to < 0 || to >= columns || from == to)
                return false;
            for (var i = start; i < end; i++)
                blocks[i].Text = BlockMarkdown.JoinCells(Move(Cells(blocks[i], columns), from, to));
            var header = blocks[start];
            header.TableAlign = string.Join(",", Move(SplitList(header.TableAlign, columns), from, to));
            if (header.GetAttribute(EditorBlock.AttrColWidths) != null)
                SetWidthList(header, Move(SplitList(header.GetAttribute(EditorBlock.AttrColWidths), columns), from, to));
            return true;
        }

        private static List<string> Move(List<string> list, int from, int to)
        {
            var item = list[from];
            list.RemoveAt(from);
            list.Insert(to, item);
            return list;
        }

        /// <summary>열 정렬(<see cref="AlignLeft"/>·<see cref="AlignCenter"/>·<see cref="AlignRight"/>, 빈 값은 기본)을 머리글에 넣는다.</summary>
        public static void SetColumnAlign(EditorBlock header, int columns, int column, string align)
        {
            if (header == null || column < 0 || column >= columns)
                return;
            var aligns = SplitList(header.TableAlign, columns);
            aligns[column] = align == AlignLeft || align == AlignCenter || align == AlignRight ? align : string.Empty;
            header.TableAlign = string.Join(",", aligns);
        }

        /// <summary>열 정렬 값(<c>l</c>·<c>c</c>·<c>r</c>·빈 값).</summary>
        public static string GetColumnAlign(EditorBlock header, int column)
        {
            if (header == null || column < 0)
                return string.Empty;
            var aligns = (header.TableAlign ?? string.Empty).Split(',');
            return column < aligns.Length ? aligns[column].Trim() : string.Empty;
        }

        /// <summary>열 너비(px)들. 자동인 열은 0.</summary>
        public static double[] ParseWidths(string value, int columns)
        {
            var result = new double[Math.Max(0, columns)];
            if (string.IsNullOrEmpty(value))
                return result;
            var parts = value.Split(',');
            for (var i = 0; i < result.Length && i < parts.Length; i++)
            {
                double width;
                if (double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out width) && width > 0)
                    result[i] = Math.Max(MinColumnWidth, width);
            }
            return result;
        }

        /// <summary><paramref name="column"/>번 열 너비를 정한다. <paramref name="width"/>가 0 이하면 자동으로 되돌린다.</summary>
        public static void SetColumnWidth(EditorBlock header, int columns, int column, double width)
        {
            if (header == null || column < 0 || column >= columns)
                return;
            var widths = SplitList(header.GetAttribute(EditorBlock.AttrColWidths), columns);
            widths[column] = width > 0 ? Math.Round(Math.Max(MinColumnWidth, width)).ToString(CultureInfo.InvariantCulture) : string.Empty;
            SetWidthList(header, widths);
        }

        // ── 머리글 ──────────────────────────────────────────────────────

        public static bool IsHeaderHidden(EditorBlock header)
        {
            return header != null && header.GetAttribute(EditorBlock.AttrNoHeader) == "true";
        }

        /// <summary>머리글 표시를 켜거나 끈다(Markdown은 그대로 — 첫 행은 늘 머리글로 저장).</summary>
        public static void SetHeaderHidden(EditorBlock header, bool hidden)
        {
            header?.SetAttribute(EditorBlock.AttrNoHeader, hidden ? "true" : null);
        }

        /// <summary>표 전체 속성(정렬·<see cref="HeaderAttributes"/>)을 <paramref name="from"/>에서 <paramref name="to"/>로 옮긴다.</summary>
        public static void TransferHeader(EditorBlock from, EditorBlock to)
        {
            if (from == null || to == null || ReferenceEquals(from, to))
                return;
            to.TableAlign = from.TableAlign;
            from.TableAlign = string.Empty;
            foreach (var key in HeaderAttributes)
            {
                to.SetAttribute(key, from.GetAttribute(key));
                from.SetAttribute(key, null);
            }
        }

        // ── 행 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 표 [start, …)의 <paramref name="at"/> 위치에 <paramref name="row"/>를 넣는다. 맨 위(<paramref name="at"/> = start)면 새 행이 머리글이 되므로
        /// 표 전체 속성을 옮긴다.
        /// </summary>
        public static void InsertRow(IList<EditorBlock> blocks, int start, int at, EditorBlock row)
        {
            blocks.Insert(at, row);
            if (at == start && at + 1 < blocks.Count)
                TransferHeader(blocks[at + 1], row);
        }

        /// <summary>표 [start, end)의 <paramref name="index"/> 행을 지운다. 첫 행이면 표 전체 속성을 다음 행(새 머리글)으로 옮긴다.</summary>
        public static void DeleteRow(IList<EditorBlock> blocks, int start, int end, int index)
        {
            if (index < start || index >= end)
                return;
            if (index == start && end - start > 1)
                TransferHeader(blocks[start], blocks[start + 1]);
            blocks.RemoveAt(index);
        }

        /// <summary>
        /// 표 [start, end) 안에서 <paramref name="from"/> 행을 <paramref name="to"/> 자리(옮긴 뒤 위치)로 옮긴다. 첫 행이 바뀌면 표 전체 속성은
        /// 새 첫 행으로 옮긴다(정렬·너비는 열의 속성이라 행을 따라가지 않음). 바뀌지 않으면 false.
        /// </summary>
        public static bool MoveRow(IList<EditorBlock> blocks, int start, int end, int from, int to)
        {
            if (from < start || from >= end || to < start || to >= end || from == to)
                return false;
            var oldHeader = blocks[start];
            var row = blocks[from];
            blocks.RemoveAt(from);
            blocks.Insert(to, row);
            if (!ReferenceEquals(blocks[start], oldHeader))
                TransferHeader(oldHeader, blocks[start]);
            return true;
        }
    }
}

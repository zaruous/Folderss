using Folderss.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Folderss.Services
{
    /// <summary>
    /// 표 셀 안 줄바꿈(블록 편집기 Ctrl+Enter). Markdown 표 행은 한 줄이라 HTML 태그를 섞지 않고(#78)
    /// 줄바꿈 자리는 공백으로 쓰고(<see cref="BlockMarkdown.JoinCells"/>가 이미 줄바꿈을 공백으로 바꿈 — 글자 수가 같다),
    /// 어느 공백이 줄바꿈인지만 표 행 블록 속성 <see cref="Attribute"/>로 사이드카에 둔다.
    /// 값: <c>해시:위치,위치;…</c> — 셀마다 앞뒤 공백을 뺀 글의 해시(<see cref="TextMarks.Hash"/>)와 그 글 첫 글자 기준 위치.
    /// 셀 글이 다른 곳에서 바뀌면 해시가 맞지 않아 그 셀 줄바꿈만 버린다(공백으로 보인다). 열 순서에 묶지 않아 열 추가·삭제에도 따라간다.
    /// 편집 중 셀 앞뒤의 줄바꿈도 위치(음수·길이 이상)로 남겨 입력란 글이 그대로 돌아오게 하고, Markdown이 앞뒤 공백을 버리면 다시 읽을 때 무시된다.
    /// </summary>
    public static class TableCellBreaks
    {
        public const string Attribute = "cellbreaks";

        /// <summary>편집·표시용 셀 글: 블록 셀 글(탭 구분)에서 줄바꿈 자리 공백을 <c>\n</c>으로 바꾼다.</summary>
        public static string[] EditTexts(EditorBlock row)
        {
            var cells = BlockMarkdown.SplitCells(row.Text);
            var entries = Parse(row.GetAttribute(Attribute));
            if (entries.Count == 0)
                return cells;
            for (var i = 0; i < cells.Length; i++)
                cells[i] = Apply(cells[i], entries);
            return cells;
        }

        /// <summary>
        /// 입력란 글(줄바꿈 포함)로 속성을 다시 정한다. 블록 본문은 호출자가 같은 글로 <see cref="BlockMarkdown.JoinCells"/>해 넣는다.
        /// 줄바꿈이 하나도 없으면 속성을 지운다.
        /// </summary>
        public static void Update(EditorBlock row, IEnumerable<string> editTexts)
        {
            var entries = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var text in editTexts)
            {
                // 본문과 같은 변환(JoinCells: \r 삭제, 탭·줄바꿈 → 공백). 위치는 이 글 기준.
                var cell = (text ?? string.Empty).Replace("\r", string.Empty);
                if (cell.IndexOf('\n') < 0)
                    continue;
                var model = BlockMarkdown.JoinCells(new[] { cell });
                var trimmed = model.Trim();
                var lead = model.Length - model.TrimStart().Length;
                var offsets = new List<string>();
                for (var i = 0; i < cell.Length; i++)
                    if (cell[i] == '\n')
                        offsets.Add((i - lead).ToString(CultureInfo.InvariantCulture));
                entries[TextMarks.Hash(trimmed)] = string.Join(",", offsets);
            }
            row.SetAttribute(Attribute, entries.Count == 0 ? null : string.Join(";", entries.Select(e => e.Key + ":" + e.Value)));
        }

        private static string Apply(string cell, Dictionary<string, int[]> entries)
        {
            int[] offsets;
            if (!entries.TryGetValue(TextMarks.Hash(cell.Trim()), out offsets))
                return cell;
            var lead = cell.Length - cell.TrimStart().Length;
            var chars = cell.ToCharArray();
            foreach (var offset in offsets)
            {
                var at = lead + offset;
                if (at >= 0 && at < chars.Length && chars[at] == ' ')
                    chars[at] = '\n';
            }
            return new string(chars);
        }

        /// <summary>속성 값을 읽는다. 잘못된 항목은 버린다.</summary>
        private static Dictionary<string, int[]> Parse(string value)
        {
            var result = new Dictionary<string, int[]>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(value))
                return result;
            foreach (var entry in value.Split(';'))
            {
                var colon = entry.IndexOf(':');
                if (colon <= 0)
                    continue;
                var offsets = new List<int>();
                foreach (var part in entry.Substring(colon + 1).Split(','))
                {
                    int offset;
                    if (int.TryParse(part, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out offset))
                        offsets.Add(offset);
                }
                if (offsets.Count > 0)
                    result[entry.Substring(0, colon)] = offsets.ToArray();
            }
            return result;
        }
    }
}

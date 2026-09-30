using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Folderss.Models;

namespace Folderss.Services
{
    /// <summary>보고서의 diff 배치.</summary>
    public enum DiffReportLayout
    {
        /// <summary>왼쪽(변경 전) | 오른쪽(변경 후) 두 열. 삭제 줄과 추가 줄을 같은 행에 짝짓는다.</summary>
        SideBySide,
        /// <summary>한 열에 -/+ 줄을 차례로(unified).</summary>
        Inline
    }

    public sealed class DiffReportInfo
    {
        public string Title { get; set; }
        /// <summary>왼쪽(변경 전) 표시 이름. 두 파일 비교는 전체 경로, Git 비교는 null(파일마다 헤더에 경로가 나온다).</summary>
        public string LeftLabel { get; set; }
        public string RightLabel { get; set; }
        public DiffReportLayout Layout { get; set; }
        /// <summary>보고서를 만들 때 쓴 문맥 범위(변경점만 / 앞뒤 10줄 / 전체 파일). 표시용.</summary>
        public GitDiffViewMode Scope { get; set; }
        public DateTime GeneratedAt { get; set; }
    }

    /// <summary>양옆 배치의 한 행. <see cref="FullWidth"/>가 있으면 헤더·hunk·안내처럼 두 열을 합친 행이다.</summary>
    public sealed class DiffReportRow
    {
        public GitDiffLine Left { get; set; }
        public GitDiffLine Right { get; set; }
        public GitDiffLine FullWidth { get; set; }
    }

    /// <summary>
    /// unified diff 텍스트를 외부 파일 없이 열리는 HTML 한 장으로 만든다(스타일 내장, 스크립트 없음).
    /// 파싱은 화면과 같은 <see cref="GitOutputParser.ParseDiff"/>를 쓴다 — 줄 번호·분류가 diff 창과 같다.
    /// </summary>
    public static class DiffHtmlReport
    {
        /// <summary>보고서에 넣는 최대 줄 수. 넘으면 파서가 자르고 안내 줄을 붙인다(화면 한도보다 크게).</summary>
        public const int MaxLines = 100000;

        public static string LayoutName(DiffReportLayout layout) => layout == DiffReportLayout.SideBySide ? "양옆 비교" : "한 줄 보기";

        public static string ScopeName(GitDiffViewMode scope)
        {
            switch (scope)
            {
                case GitDiffViewMode.Context10: return "변경점 + 앞뒤 10줄";
                case GitDiffViewMode.FullFile: return "전체 파일";
                default: return "변경점만";
            }
        }

        /// <summary>
        /// 연속한 삭제 묶음과 그 뒤 추가 묶음을 순서대로 짝짓는다(남는 쪽은 반대편이 빈다). 문맥 줄은 양쪽에 같은 줄,
        /// 헤더·hunk·안내 줄은 전체 폭 행이다. 추가 뒤에 삭제가 오면 새 묶음으로 본다.
        /// </summary>
        public static List<DiffReportRow> PairRows(IReadOnlyList<GitDiffLine> lines)
        {
            var rows = new List<DiffReportRow>();
            var removed = new List<GitDiffLine>();
            var added = new List<GitDiffLine>();

            void Flush()
            {
                for (var i = 0; i < Math.Max(removed.Count, added.Count); i++)
                    rows.Add(new DiffReportRow { Left = i < removed.Count ? removed[i] : null, Right = i < added.Count ? added[i] : null });
                removed.Clear();
                added.Clear();
            }

            foreach (var line in lines)
            {
                switch (line.Kind)
                {
                    case GitDiffLineKind.Removed:
                        if (added.Count > 0)
                            Flush();
                        removed.Add(line);
                        break;
                    case GitDiffLineKind.Added:
                        added.Add(line);
                        break;
                    case GitDiffLineKind.Context:
                        Flush();
                        rows.Add(new DiffReportRow { Left = line, Right = line });
                        break;
                    default:
                        Flush();
                        rows.Add(new DiffReportRow { FullWidth = line });
                        break;
                }
            }
            Flush();
            return rows;
        }

        public static string Build(string diffText, DiffReportInfo info)
        {
            var lines = GitOutputParser.ParseDiff(diffText ?? string.Empty, MaxLines);
            var html = new StringBuilder(Math.Max(4096, (diffText?.Length ?? 0) * 2));
            var title = string.IsNullOrWhiteSpace(info.Title) ? "diff 보고서" : info.Title;

            html.Append("<!DOCTYPE html>\n<html lang=\"ko\">\n<head>\n<meta charset=\"utf-8\">\n")
                .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
                .Append("<title>").Append(Encode(title)).Append("</title>\n<style>\n").Append(Css).Append("</style>\n</head>\n<body>\n");

            html.Append("<header>\n<h1>").Append(Encode(title)).Append("</h1>\n<dl>\n");
            if (!string.IsNullOrEmpty(info.LeftLabel))
                Meta(html, "왼쪽 (변경 전)", info.LeftLabel);
            if (!string.IsNullOrEmpty(info.RightLabel))
                Meta(html, "오른쪽 (변경 후)", info.RightLabel);
            Meta(html, "형식", LayoutName(info.Layout) + " · " + ScopeName(info.Scope));
            Meta(html, "요약", string.Format("파일 {0}개 · 추가 {1}줄 · 삭제 {2}줄",
                lines.Count(l => l.Kind == GitDiffLineKind.Header && l.Text.StartsWith("+++ ", StringComparison.Ordinal)),
                lines.Count(l => l.Kind == GitDiffLineKind.Added),
                lines.Count(l => l.Kind == GitDiffLineKind.Removed)));
            Meta(html, "생성", info.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            html.Append("</dl>\n</header>\n<main>\n");

            if (lines.Count == 0)
                html.Append("<p class=\"empty\">차이가 없습니다.</p>\n");
            else if (info.Layout == DiffReportLayout.SideBySide)
                AppendSideBySide(html, lines);
            else
                AppendInline(html, lines);

            html.Append("</main>\n</body>\n</html>\n");
            return html.ToString();
        }

        private static void AppendSideBySide(StringBuilder html, IReadOnlyList<GitDiffLine> lines)
        {
            html.Append("<table class=\"diff side\">\n<colgroup><col class=\"num\"><col class=\"code\"><col class=\"num\"><col class=\"code\"></colgroup>\n");
            foreach (var row in PairRows(lines))
            {
                if (row.FullWidth != null)
                {
                    AppendFullWidth(html, row.FullWidth, 4);
                    continue;
                }
                html.Append("<tr>");
                AppendCell(html, row.Left, row.Left?.OldLine, row.Left == null ? "blank" : row.Left.Kind == GitDiffLineKind.Removed ? "del" : "ctx");
                AppendCell(html, row.Right, row.Right?.NewLine, row.Right == null ? "blank" : row.Right.Kind == GitDiffLineKind.Added ? "add" : "ctx");
                html.Append("</tr>\n");
            }
            html.Append("</table>\n");
        }

        private static void AppendInline(StringBuilder html, IReadOnlyList<GitDiffLine> lines)
        {
            html.Append("<table class=\"diff inline\">\n<colgroup><col class=\"num\"><col class=\"num\"><col class=\"sign\"><col class=\"code\"></colgroup>\n");
            foreach (var line in lines)
            {
                if (line.Kind != GitDiffLineKind.Added && line.Kind != GitDiffLineKind.Removed && line.Kind != GitDiffLineKind.Context)
                {
                    AppendFullWidth(html, line, 4);
                    continue;
                }
                var css = line.Kind == GitDiffLineKind.Added ? "add" : line.Kind == GitDiffLineKind.Removed ? "del" : "ctx";
                var sign = line.Kind == GitDiffLineKind.Added ? "+" : line.Kind == GitDiffLineKind.Removed ? "−" : string.Empty;
                html.Append("<tr class=\"").Append(css).Append("\"><td class=\"num\">").Append(line.OldLine)
                    .Append("</td><td class=\"num\">").Append(line.NewLine)
                    .Append("</td><td class=\"sign\">").Append(sign)
                    .Append("</td><td class=\"code\">").Append(Encode(Body(line))).Append("</td></tr>\n");
            }
            html.Append("</table>\n");
        }

        private static void AppendCell(StringBuilder html, GitDiffLine line, int? number, string css)
        {
            html.Append("<td class=\"num ").Append(css).Append("\">").Append(number)
                .Append("</td><td class=\"").Append(css).Append("\">").Append(line == null ? string.Empty : Encode(Body(line))).Append("</td>");
        }

        private static void AppendFullWidth(StringBuilder html, GitDiffLine line, int columns)
        {
            var css = line.Kind == GitDiffLineKind.Hunk ? "hunk" : line.Kind == GitDiffLineKind.Header ? "hdr" : "meta";
            html.Append("<tr class=\"").Append(css).Append("\"><td colspan=\"").Append(columns).Append("\">")
                .Append(Encode(line.Text.TrimEnd('\r'))).Append("</td></tr>\n");
        }

        /// <summary>내용 줄의 앞 기호(+, -, 공백)를 뗀 본문. CR은 지운다(CRLF 파일).</summary>
        private static string Body(GitDiffLine line)
        {
            var text = line.Text ?? string.Empty;
            if (text.Length > 0 && (text[0] == '+' || text[0] == '-' || text[0] == ' '))
                text = text.Substring(1);
            return text.TrimEnd('\r');
        }

        private static void Meta(StringBuilder html, string name, string value)
        {
            html.Append("<dt>").Append(Encode(name)).Append("</dt><dd>").Append(Encode(value)).Append("</dd>\n");
        }

        private static string Encode(string text) => WebUtility.HtmlEncode(text ?? string.Empty);

        private const string Css = @":root { color-scheme: light dark; --bg:#ffffff; --fg:#1f2328; --muted:#656d76; --line:#d0d7de;
  --add:#e6ffec; --addnum:#ccffd8; --del:#ffebe9; --delnum:#ffd7d5; --hunk:#ddf4ff; --hdr:#f6f8fa; --blank:#f6f8fa; }
@media (prefers-color-scheme: dark) { :root { --bg:#0d1117; --fg:#e6edf3; --muted:#8d96a0; --line:#30363d;
  --add:#12261e; --addnum:#1c4428; --del:#25171c; --delnum:#542426; --hunk:#121d2f; --hdr:#161b22; --blank:#161b22; } }
* { box-sizing: border-box; }
body { margin: 0; padding: 16px; background: var(--bg); color: var(--fg); font: 14px/1.5 'Segoe UI', 'Malgun Gothic', sans-serif; }
h1 { font-size: 18px; margin: 0 0 8px; word-break: break-all; }
dl { display: grid; grid-template-columns: max-content 1fr; gap: 2px 12px; margin: 0 0 16px; font-size: 13px; }
dt { color: var(--muted); } dd { margin: 0; word-break: break-all; }
.empty { color: var(--muted); }
table.diff { width: 100%; border-collapse: collapse; table-layout: fixed; border: 1px solid var(--line);
  font: 12px/1.45 Consolas, 'D2Coding', monospace; }
col.num { width: 4.5em; } col.sign { width: 1.5em; }
td { padding: 0 6px; vertical-align: top; white-space: pre-wrap; overflow-wrap: anywhere; tab-size: 4; }
td.num { color: var(--muted); text-align: right; user-select: none; white-space: nowrap; }
table.side td.num { border-left: 1px solid var(--line); }
td.add, tr.add td { background: var(--add); } td.num.add, tr.add td.num { background: var(--addnum); }
td.del, tr.del td { background: var(--del); } td.num.del, tr.del td.num { background: var(--delnum); }
td.blank { background: var(--blank); }
tr.hunk td { background: var(--hunk); color: var(--muted); }
tr.hdr td { background: var(--hdr); font-weight: 600; border-top: 1px solid var(--line); }
tr.meta td { color: var(--muted); font-style: italic; }
@media print { body { padding: 0; } tr { break-inside: avoid; } }
";
    }
}

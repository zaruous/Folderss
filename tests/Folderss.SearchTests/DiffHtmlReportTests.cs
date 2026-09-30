using Folderss.Models;
using Folderss.Services;
using System;
using System.Linq;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>diff 텍스트 → HTML 보고서(양옆/한 줄). 파서 결과를 짝짓는 규칙과 이스케이프를 확인한다.</summary>
    public sealed class DiffHtmlReportTests
    {
        private const string Diff =
            "diff --git a/x.txt b/x.txt\n" +
            "--- a/x.txt\n" +
            "+++ b/x.txt\n" +
            "@@ -1,4 +1,3 @@\n" +
            " same\n" +
            "-old1\n" +
            "-old2\n" +
            "+new1\n" +
            " tail <script>alert(1)</script>\n" +
            "-gone\n";

        private static DiffReportInfo Info(DiffReportLayout layout) => new DiffReportInfo
        {
            Title = "a ↔ b",
            LeftLabel = @"C:\왼쪽\x.txt",
            RightLabel = @"D:\오른쪽\x.txt",
            Layout = layout,
            Scope = GitDiffViewMode.ChangesOnly,
            GeneratedAt = new DateTime(2026, 9, 30, 12, 0, 0)
        };

        [Fact]
        public void PairRows_PairsRemovedWithAdded_AndKeepsContextOnBothSides()
        {
            var rows = DiffHtmlReport.PairRows(GitOutputParser.ParseDiff(Diff));

            // 헤더 3줄 + hunk = 전체 폭 4행
            Assert.Equal(4, rows.Count(r => r.FullWidth != null));
            var body = rows.Where(r => r.FullWidth == null).ToList();
            Assert.Equal(("same", "same"), (body[0].Left.Text.Substring(1), body[0].Right.Text.Substring(1)));
            Assert.Equal(("-old1", "+new1"), (body[1].Left.Text, body[1].Right.Text));
            Assert.Equal("-old2", body[2].Left.Text);
            Assert.Null(body[2].Right);   // 짝이 없는 삭제 줄은 오른쪽이 빈다
            Assert.Same(body[3].Left, body[3].Right);   // 문맥 줄은 양쪽에 같은 줄
            Assert.Equal(((int?)4, (int?)3), (body[3].Left.OldLine, body[3].Left.NewLine));
            Assert.Equal("-gone", body[4].Left.Text);
            Assert.Null(body[4].Right);
        }

        [Fact]
        public void PairRows_AddedThenRemoved_StartsNewBlock()
        {
            var lines = GitOutputParser.ParseDiff("@@ -1,2 +1,2 @@\n+A\n-a\n+B\n");
            var rows = DiffHtmlReport.PairRows(lines).Where(r => r.FullWidth == null).ToList();
            Assert.Equal(2, rows.Count);
            Assert.Null(rows[0].Left);
            Assert.Equal("+A", rows[0].Right.Text);
            Assert.Equal(("-a", "+B"), (rows[1].Left.Text, rows[1].Right.Text));
        }

        [Theory]
        [InlineData(DiffReportLayout.SideBySide, "양옆 비교")]
        [InlineData(DiffReportLayout.Inline, "한 줄 보기")]
        public void Build_EscapesContent_AndShowsLabelsAndSummary(DiffReportLayout layout, string layoutName)
        {
            var html = DiffHtmlReport.Build(Diff, Info(layout));

            Assert.StartsWith("<!DOCTYPE html>", html);
            Assert.Contains("<meta charset=\"utf-8\">", html);
            Assert.DoesNotContain("<script>alert(1)</script>", html);
            Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
            Assert.Contains(@"C:\왼쪽\x.txt", html);
            Assert.Contains(@"D:\오른쪽\x.txt", html);
            Assert.Contains(layoutName, html);
            Assert.Contains("변경점만", html);
            Assert.Contains("추가 1줄", html);
            Assert.Contains("삭제 3줄", html);
            Assert.Contains("파일 1개", html);
            Assert.Contains("2026-09-30 12:00", html);
        }

        [Fact]
        public void Build_SideBySide_PutsPairOnOneRow()
        {
            var html = DiffHtmlReport.Build(Diff, Info(DiffReportLayout.SideBySide));
            // old1(삭제)과 new1(추가)이 같은 행에 있다.
            var row = html.Split("<tr").Single(r => r.Contains("old1"));
            Assert.Contains("new1", row);
            Assert.Contains("class=\"del\"", row);
            Assert.Contains("class=\"add\"", row);
        }

        [Fact]
        public void Build_Empty_SaysNoDifference()
        {
            var html = DiffHtmlReport.Build(string.Empty, Info(DiffReportLayout.Inline));
            Assert.Contains("차이가 없습니다", html);
        }
    }
}

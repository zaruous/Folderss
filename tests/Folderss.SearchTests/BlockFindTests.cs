using Folderss.Models;
using Folderss.Services;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>블록 편집기 찾기·바꾸기, 제목 앵커, 외부 변경 뒤 스크롤 앵커, 링크 해석(#75 찾기·외부 변경).</summary>
    public sealed class BlockFindTests
    {
        private static EditorBlock B(BlockKind kind, string text, int indent = 0)
        {
            return new EditorBlock(kind, text, indent);
        }

        private static List<EditorBlock> Doc(params EditorBlock[] blocks)
        {
            return blocks.ToList();
        }

        // ── 찾기 ───────────────────────────────────────────────────────────

        [Fact]
        public void FindAll_FindsEveryOccurrenceInDocumentOrder()
        {
            var doc = Doc(B(BlockKind.Heading1, "Apple pie"), B(BlockKind.Paragraph, "an apple and APPLE"), B(BlockKind.Code, "apple()"));
            var matches = BlockFind.FindAll(doc, "apple", false);
            Assert.Equal(new[] { "0:0+5", "1:3+5", "1:13+5", "2:0+5" }, matches.Select(m => m.ToString()).ToArray());
        }

        [Fact]
        public void FindAll_MatchCase_IsOrdinal()
        {
            var doc = Doc(B(BlockKind.Paragraph, "an apple and APPLE"));
            Assert.Single(BlockFind.FindAll(doc, "APPLE", true));
            Assert.Equal(2, BlockFind.FindAll(doc, "APPLE", false).Count);
        }

        [Fact]
        public void FindAll_DoesNotOverlapAndSkipsDividerAndToc()
        {
            var doc = Doc(B(BlockKind.Paragraph, "aaaa"), B(BlockKind.Divider, "aa"), B(BlockKind.Toc, "aa"));
            var matches = BlockFind.FindAll(doc, "aa", true);
            Assert.Equal(new[] { "0:0+2", "0:2+2" }, matches.Select(m => m.ToString()).ToArray());
        }

        [Fact]
        public void FindAll_TableRow_DoesNotCrossCellBoundary()
        {
            var doc = Doc(B(BlockKind.TableRow, "ab\tcd\tab"));
            Assert.Equal(new[] { "0:0+2", "0:6+2" }, BlockFind.FindAll(doc, "ab", true).Select(m => m.ToString()).ToArray());
            Assert.Empty(BlockFind.FindAll(doc, "b\tc", true));
        }

        [Fact]
        public void FindAll_EmptyQuery_ReturnsNothing()
        {
            Assert.Empty(BlockFind.FindAll(Doc(B(BlockKind.Paragraph, "x")), string.Empty, false));
        }

        [Fact]
        public void NextMatch_WrapsAroundBothDirections()
        {
            var matches = new List<BlockMatch> { new BlockMatch(1, 0, 1), new BlockMatch(1, 5, 1), new BlockMatch(4, 2, 1) };
            Assert.Equal(1, BlockFind.NextMatch(matches, 1, 0, true));          // 지금 결과 다음
            Assert.Equal(0, BlockFind.NextMatch(matches, 1, 0, true, true));    // 그 자리에서 시작하는 결과 포함
            Assert.Equal(0, BlockFind.NextMatch(matches, 4, 2, true));          // 끝 → 처음
            Assert.Equal(2, BlockFind.NextMatch(matches, 1, 0, false));         // 처음 → 끝
            Assert.Equal(1, BlockFind.NextMatch(matches, 3, 0, false));
            Assert.Equal(-1, BlockFind.NextMatch(new List<BlockMatch>(), 0, 0, true));
        }

        // ── 바꾸기 ─────────────────────────────────────────────────────────

        [Fact]
        public void ReplaceOne_ReplacesOnlyThatMatchAndReturnsEnd()
        {
            var doc = Doc(B(BlockKind.Paragraph, "cat cat"));
            var match = BlockFind.FindAll(doc, "cat", true)[1];
            Assert.Equal(9, BlockFind.ReplaceOne(doc, match, "cat", "tiger", true));
            Assert.Equal("cat tiger", doc[0].Text);
        }

        [Fact]
        public void ReplaceOne_StaleMatch_DoesNothing()
        {
            var doc = Doc(B(BlockKind.Paragraph, "cat"));
            var match = BlockFind.FindAll(doc, "cat", true)[0];
            doc[0].Text = "dog";
            Assert.Equal(-1, BlockFind.ReplaceOne(doc, match, "cat", "x", true));
            Assert.Equal("dog", doc[0].Text);
        }

        [Fact]
        public void ReplaceAll_ReplacesEveryMatchAndReportsRange()
        {
            var doc = Doc(B(BlockKind.Paragraph, "keep"), B(BlockKind.Paragraph, "a-a"), B(BlockKind.Paragraph, "none"), B(BlockKind.Bulleted, "A"));
            int first, last;
            var count = BlockFind.ReplaceAll(doc, "a", "bb", false, out first, out last);
            Assert.Equal(3, count);
            Assert.Equal(new[] { "keep", "bb-bb", "none", "bb" }, doc.Select(b => b.Text).ToArray());
            Assert.Equal(1, first);
            Assert.Equal(3, last);
        }

        [Fact]
        public void ReplaceAll_ReplacementContainingQuery_DoesNotLoop()
        {
            var doc = Doc(B(BlockKind.Paragraph, "a a"));
            int first, last;
            Assert.Equal(2, BlockFind.ReplaceAll(doc, "a", "aa", true, out first, out last));
            Assert.Equal("aa aa", doc[0].Text);
        }

        [Fact]
        public void ReplaceAll_TableRow_KeepsCellStructure()
        {
            var doc = Doc(B(BlockKind.TableRow, "x\ty"));
            int first, last;
            BlockFind.ReplaceAll(doc, "x", "1\t2", true, out first, out last);
            Assert.Equal("1 2\ty", doc[0].Text);
            Assert.Equal(2, BlockMarkdown.SplitCells(doc[0].Text).Length);
        }

        [Fact]
        public void ReplaceAll_NoMatch_ReturnsZero()
        {
            var doc = Doc(B(BlockKind.Paragraph, "x"));
            int first, last;
            Assert.Equal(0, BlockFind.ReplaceAll(doc, "y", "z", true, out first, out last));
            Assert.Equal(-1, first);
        }

        [Fact]
        public void CellOffset_AndTextOffset_RoundTrip()
        {
            const string text = "ab\tcde\tf";
            int cell, offset;
            BlockFind.CellOffset(text, 4, out cell, out offset);
            Assert.Equal(1, cell);
            Assert.Equal(1, offset);
            Assert.Equal(4, BlockFind.TextOffset(text, cell, offset));
            BlockFind.CellOffset(text, 7, out cell, out offset);
            Assert.Equal(2, cell);
            Assert.Equal(0, offset);
            Assert.Equal(7, BlockFind.TextOffset(text, 2, 0));
        }

        // ── 제목 앵커 ──────────────────────────────────────────────────────

        [Theory]
        [InlineData("설치 방법", "설치-방법")]
        [InlineData("C++ & C# (2024)", "c--c-2024")]
        [InlineData("**굵은** 제목", "굵은-제목")]
        [InlineData("[링크](http://x.y) 제목", "링크-제목")]
        [InlineData("Snake_case-Name", "snake_case-name")]
        public void Slugify_FollowsMarkdownViewerRule(string heading, string slug)
        {
            Assert.Equal(slug, BlockFind.Slugify(heading));
        }

        [Fact]
        public void FindHeading_HandlesDuplicatesEncodingAndToggleHeadings()
        {
            var toggle = B(BlockKind.Toggle, "접는 제목");
            toggle.SetAttribute(EditorBlock.AttrHeading, "2");
            var doc = Doc(
                B(BlockKind.Heading1, "FAQ"),
                B(BlockKind.Paragraph, "본문 FAQ"),
                B(BlockKind.Heading2, "FAQ"),
                toggle,
                B(BlockKind.Heading3, "설치 방법"));
            Assert.Equal(0, BlockFind.FindHeading(doc, "faq"));
            Assert.Equal(2, BlockFind.FindHeading(doc, "#faq-1"));
            Assert.Equal(3, BlockFind.FindHeading(doc, "접는-제목"));
            Assert.Equal(4, BlockFind.FindHeading(doc, "%EC%84%A4%EC%B9%98-%EB%B0%A9%EB%B2%95"));
            Assert.Equal(4, BlockFind.FindHeading(doc, "설치 방법"));   // 손으로 쓴 앵커도 같은 규칙으로 맞춤
            Assert.Equal(-1, BlockFind.FindHeading(doc, "없는-제목"));
        }

        // ── 스크롤 앵커 ────────────────────────────────────────────────────

        [Fact]
        public void MapAnchor_FindsSameBlockAfterInsertAbove()
        {
            var anchor = B(BlockKind.Paragraph, "보던 문단");
            var reloaded = Doc(B(BlockKind.Paragraph, "새 줄 1"), B(BlockKind.Paragraph, "새 줄 2"), B(BlockKind.Heading1, "제목"),
                B(BlockKind.Paragraph, "앞"), B(BlockKind.Paragraph, "보던 문단"), B(BlockKind.Paragraph, "뒤"));
            Assert.Equal(4, BlockFind.MapAnchor(anchor, 2, reloaded));
        }

        [Fact]
        public void MapAnchor_PrefersNearestDuplicate()
        {
            var anchor = B(BlockKind.Paragraph, "같음");
            var reloaded = Doc(B(BlockKind.Paragraph, "같음"), B(BlockKind.Paragraph, "x"), B(BlockKind.Paragraph, "x"),
                B(BlockKind.Paragraph, "x"), B(BlockKind.Paragraph, "x"), B(BlockKind.Paragraph, "같음"));
            Assert.Equal(5, BlockFind.MapAnchor(anchor, 4, reloaded));
            Assert.Equal(0, BlockFind.MapAnchor(anchor, 1, reloaded));
        }

        [Fact]
        public void MapAnchor_ChangedBlock_FallsBackToClampedIndex()
        {
            var anchor = B(BlockKind.Paragraph, "지워진 문단");
            var reloaded = Doc(B(BlockKind.Paragraph, "a"), B(BlockKind.Paragraph, "b"));
            Assert.Equal(1, BlockFind.MapAnchor(anchor, 7, reloaded));
            Assert.Equal(-1, BlockFind.MapAnchor(anchor, 0, new List<EditorBlock>()));
        }

        // ── 링크 ───────────────────────────────────────────────────────────

        [Fact]
        public void LinkAt_FindsMarkdownAngleAndBareLinks()
        {
            const string text = "보기 [문서](docs/a.md#설치) 와 <https://e.com/x> 그리고 https://f.com/y. 끝 ![그림](img/p.png)";
            Assert.Equal("docs/a.md#설치", BlockFind.LinkAt(text, text.IndexOf("문서")));
            Assert.Equal("docs/a.md#설치", BlockFind.LinkAt(text, text.IndexOf("a.md")));
            Assert.Equal("https://e.com/x", BlockFind.LinkAt(text, text.IndexOf("e.com")));
            Assert.Equal("https://f.com/y", BlockFind.LinkAt(text, text.IndexOf("f.com")));
            Assert.Equal("img/p.png", BlockFind.LinkAt(text, text.IndexOf("그림")));
            Assert.Null(BlockFind.LinkAt(text, 0));
            Assert.Null(BlockFind.LinkAt(text, text.IndexOf("끝")));
        }

        [Fact]
        public void LinkAt_AngleBracketTargetAndTitle()
        {
            Assert.Equal("my file.md", BlockFind.LinkAt("[a](<my file.md>)", 1));
            Assert.Equal("b.md", BlockFind.LinkAt("[a](b.md \"제목\")", 1));
            Assert.Equal("https://www.x.org", BlockFind.LinkAt("go www.x.org now", 5));
        }

        [Fact]
        public void ResolveLocalLink_RelativeEncodedAndWeb()
        {
            var baseDir = Path.Combine(Path.GetTempPath(), "blockfind-docs");
            Assert.Equal(Path.GetFullPath(Path.Combine(baseDir, "sub", "my file.md")),
                BlockFind.ResolveLocalLink(baseDir, "sub/my%20file.md#제목"));
            Assert.Equal(Path.GetFullPath(Path.Combine(baseDir, "a.md")), BlockFind.ResolveLocalLink(baseDir, "a.md?x=1"));
            Assert.Null(BlockFind.ResolveLocalLink(baseDir, "https://example.com/a.md"));
            Assert.Null(BlockFind.ResolveLocalLink(baseDir, "mailto:a@b.c"));
            Assert.Null(BlockFind.ResolveLocalLink(baseDir, "#제목"));
            Assert.Equal("제목", BlockFind.ExtractFragment("a.md#제목"));
            Assert.Null(BlockFind.ExtractFragment("a.md"));
        }
    }
}

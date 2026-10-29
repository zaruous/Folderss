using Folderss.Models;
using Folderss.Services;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>블록 편집기(Notion 스타일) 문서 ↔ Markdown 변환과 블록 구조 연산.</summary>
    public sealed class BlockMarkdownTests
    {
        private static string Md(params string[] lines)
        {
            return string.Join("\n", lines) + "\n";
        }

        private static EditorBlock B(BlockKind kind, string text = "", int indent = 0)
        {
            return new EditorBlock(kind, text, indent);
        }

        [Fact]
        public void NormalizedDocument_RoundTripsUnchanged()
        {
            var markdown = Md(
                "---",
                "title: 테스트",
                "---",
                "",
                "# 제목 1",
                "",
                "본문 **굵게** 문단",
                "두 번째 줄",
                "",
                "## 제목 2",
                "",
                "- 글머리",
                "  - 중첩",
                "    - 더 깊게",
                "- [ ] 할 일",
                "- [x] 끝낸 일",
                "1. 하나",
                "2. 둘",
                "   - 번호 아래 글머리",
                "",
                "> 인용",
                "> 두 줄",
                "",
                "> [!TIP]",
                "> 팁 내용",
                "",
                "```csharp",
                "var x = 1;",
                "```",
                "",
                "---",
                "",
                "| a | b |",
                "| --- | --- |",
                "| 1 | 2 |",
                "",
                "<details open>",
                "<summary>토글 제목</summary>",
                "",
                "토글 안 문단",
                "",
                "- 토글 안 목록",
                "",
                "</details>");

            var blocks = BlockMarkdown.Parse(markdown);

            Assert.Equal(new[]
            {
                BlockKind.Raw, BlockKind.Heading1, BlockKind.Paragraph, BlockKind.Heading2,
                BlockKind.Bulleted, BlockKind.Bulleted, BlockKind.Bulleted, BlockKind.Todo, BlockKind.Todo,
                BlockKind.Numbered, BlockKind.Numbered, BlockKind.Bulleted,
                BlockKind.Quote, BlockKind.Callout, BlockKind.Code, BlockKind.Divider, BlockKind.TableRow, BlockKind.TableRow,
                BlockKind.Toggle, BlockKind.Paragraph, BlockKind.Bulleted,
            }, blocks.Select(b => b.Kind));
            Assert.Equal(new[] { 0, 0, 0, 0, 0, 1, 2, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 1 }, blocks.Select(b => b.Indent));
            Assert.Equal("본문 **굵게** 문단\n두 번째 줄", blocks[2].Text);
            Assert.False(blocks[7].Checked);
            Assert.True(blocks[8].Checked);
            Assert.Equal("인용\n두 줄", blocks[12].Text);
            Assert.Equal("TIP", blocks[13].CalloutType);
            Assert.Equal("팁 내용", blocks[13].Text);
            Assert.Equal("csharp", blocks[14].Language);
            Assert.Equal("var x = 1;", blocks[14].Text);
            Assert.Equal("a\tb", blocks[16].Text);
            Assert.Equal("1\t2", blocks[17].Text);
            Assert.Equal("토글 제목", blocks[18].Text);
            Assert.False(blocks[18].Collapsed);

            Assert.Equal(markdown, BlockMarkdown.Serialize(blocks));
        }

        [Fact]
        public void Parse_CommonVariants_AreNormalizedOnWrite()
        {
            var blocks = BlockMarkdown.Parse("Title\r\n=====\r\n\r\n* one\r\n+ two\r\n\r\n3) three\r\n\r\n***\r\n");

            Assert.Equal(new[] { BlockKind.Heading1, BlockKind.Bulleted, BlockKind.Bulleted, BlockKind.Numbered, BlockKind.Divider },
                blocks.Select(b => b.Kind));
            Assert.Equal(Md("# Title", "", "- one", "- two", "1. three", "", "---"), BlockMarkdown.Serialize(blocks));
        }

        [Fact]
        public void ListItem_ContinuationLine_JoinsItemText()
        {
            var blocks = BlockMarkdown.Parse(Md("- first line", "  second line", "- next"));

            Assert.Equal(2, blocks.Count);
            Assert.Equal("first line\nsecond line", blocks[0].Text);
            Assert.Equal(Md("- first line", "  second line", "- next"), BlockMarkdown.Serialize(blocks));
        }

        [Fact]
        public void ChildrenOfListItems_UseParentContentColumn()
        {
            var blocks = new List<EditorBlock>
            {
                B(BlockKind.Numbered, "번호"),
                B(BlockKind.Bulleted, "자식", 1),
                B(BlockKind.Paragraph, "자식 문단", 1),
                B(BlockKind.Todo, "할 일", 0),
                B(BlockKind.Code, "x", 1),
            };

            var markdown = BlockMarkdown.Serialize(blocks);

            Assert.Equal(Md("1. 번호", "   - 자식", "", "   자식 문단", "", "- [ ] 할 일", "", "  ```", "  x", "  ```"), markdown);
            var reparsed = BlockMarkdown.Parse(markdown);
            Assert.Equal(blocks.Select(b => b.Kind), reparsed.Select(b => b.Kind));
            Assert.Equal(blocks.Select(b => b.Indent), reparsed.Select(b => b.Indent));
            Assert.Equal(blocks.Select(b => b.Text), reparsed.Select(b => b.Text));
        }

        [Fact]
        public void NumberedList_RestartsAfterOtherBlockAtSameLevel()
        {
            var blocks = new List<EditorBlock>
            {
                B(BlockKind.Numbered, "a"),
                B(BlockKind.Numbered, "b"),
                B(BlockKind.Bulleted, "child", 1),
                B(BlockKind.Numbered, "c"),
                B(BlockKind.Paragraph, "break"),
                B(BlockKind.Numbered, "d"),
            };

            Assert.Equal(Md("1. a", "2. b", "   - child", "3. c", "", "break", "", "1. d"), BlockMarkdown.Serialize(blocks));
            Assert.Equal(new[] { "1.", "2.", "◦", "3.", null, "1." }, BlockOperations.ListLabels(blocks));
        }

        [Fact]
        public void EmptyParagraphs_AreNotWritten()
        {
            var blocks = new List<EditorBlock> { B(BlockKind.Paragraph, "a"), B(BlockKind.Paragraph), B(BlockKind.Paragraph, "  "), B(BlockKind.Paragraph, "b"), B(BlockKind.Paragraph) };

            Assert.Equal(Md("a", "", "b"), BlockMarkdown.Serialize(blocks));
            Assert.Equal(string.Empty, BlockMarkdown.Serialize(new List<EditorBlock> { B(BlockKind.Paragraph) }));
        }

        [Fact]
        public void Code_WithFenceInside_UsesLongerFence()
        {
            var code = B(BlockKind.Code, "before\n```\ninside\n```");
            code.Language = "md";

            var markdown = BlockMarkdown.Serialize(new List<EditorBlock> { code });

            Assert.StartsWith("````md\n", markdown);
            var reparsed = BlockMarkdown.Parse(markdown).Single();
            Assert.Equal(BlockKind.Code, reparsed.Kind);
            Assert.Equal(code.Text, reparsed.Text);
            Assert.Equal("md", reparsed.Language);
        }

        [Fact]
        public void Toggle_CollapsedAndNested_RoundTrips()
        {
            var blocks = new List<EditorBlock>
            {
                new EditorBlock(BlockKind.Toggle, "바깥") { Collapsed = true },
                B(BlockKind.Paragraph, "안쪽 문단", 1),
                new EditorBlock(BlockKind.Toggle, "안쪽 토글", 1) { Collapsed = false },
                B(BlockKind.Bulleted, "가장 안쪽", 2),
                B(BlockKind.Paragraph, "토글 밖"),
            };

            var markdown = BlockMarkdown.Serialize(blocks);
            var reparsed = BlockMarkdown.Parse(markdown);

            Assert.Contains("<details>\n<summary>바깥</summary>", markdown);
            Assert.Contains("<details open>\n<summary>안쪽 토글</summary>", markdown);
            Assert.Equal(blocks.Select(b => b.Kind), reparsed.Select(b => b.Kind));
            Assert.Equal(blocks.Select(b => b.Indent), reparsed.Select(b => b.Indent));
            Assert.Equal(blocks.Select(b => b.Text), reparsed.Select(b => b.Text));
            Assert.True(reparsed[0].Collapsed);
            Assert.False(reparsed[2].Collapsed);
        }

        [Fact]
        public void Toggle_SingleLineDetails_IsRead()
        {
            var blocks = BlockMarkdown.Parse("<details><summary>요약</summary>본문</details>\n");

            Assert.Equal(BlockKind.Toggle, blocks[0].Kind);
            Assert.Equal("요약", blocks[0].Text);
            Assert.True(blocks[0].Collapsed);
        }

        [Fact]
        public void UnknownConstructs_ArePreservedAsTextOrRaw()
        {
            var markdown = Md("#### 네 번째 제목", "", "<div align=\"center\">", "  <img src=\"a.png\">", "</div>", "", "    indented code");

            var blocks = BlockMarkdown.Parse(markdown);

            Assert.Equal(new[] { BlockKind.Paragraph, BlockKind.Raw, BlockKind.Raw }, blocks.Select(b => b.Kind));
            Assert.Equal(markdown, BlockMarkdown.Serialize(blocks));
        }

        [Theory]
        [InlineData("# ", BlockKind.Heading1, false)]
        [InlineData("## ", BlockKind.Heading2, false)]
        [InlineData("### ", BlockKind.Heading3, false)]
        [InlineData("- ", BlockKind.Bulleted, false)]
        [InlineData("* ", BlockKind.Bulleted, false)]
        [InlineData("1. ", BlockKind.Numbered, false)]
        [InlineData("12) ", BlockKind.Numbered, false)]
        [InlineData("[] ", BlockKind.Todo, false)]
        [InlineData("[x] ", BlockKind.Todo, true)]
        [InlineData("> ", BlockKind.Quote, false)]
        [InlineData("```", BlockKind.Code, false)]
        [InlineData("$$", BlockKind.Math, false)]
        [InlineData("$$ ", BlockKind.Math, false)]
        [InlineData("[TOC] ", BlockKind.Toc, false)]
        [InlineData("[toc] ", BlockKind.Toc, false)]
        [InlineData("---", BlockKind.Divider, false)]
        public void Shortcut_MatchesNotionMarkdownTriggers(string prefix, BlockKind expected, bool expectedChecked)
        {
            BlockKind kind;
            bool isChecked;
            Assert.True(BlockMarkdown.TryMatchShortcut(prefix, out kind, out isChecked));
            Assert.Equal(expected, kind);
            Assert.Equal(expectedChecked, isChecked);
        }

        [Theory]
        [InlineData("#")]
        [InlineData("#### ")]
        [InlineData("-")]
        [InlineData("a ")]
        [InlineData("1.")]
        public void Shortcut_IgnoresIncompletePrefixes(string prefix)
        {
            BlockKind kind;
            bool isChecked;
            Assert.False(BlockMarkdown.TryMatchShortcut(prefix, out kind, out isChecked));
        }

        // ── BlockOperations ────────────────────────────────────────────

        private static string Shape(IEnumerable<EditorBlock> blocks)
        {
            return string.Join(",", blocks.Select(b => b.Text + b.Indent));
        }

        [Fact]
        public void MoveRange_SwapsWithSiblingSubtree()
        {
            var blocks = new List<EditorBlock> { B(BlockKind.Bulleted, "A"), B(BlockKind.Bulleted, "a", 1), B(BlockKind.Bulleted, "B"), B(BlockKind.Bulleted, "C") };

            Assert.Equal(0, BlockOperations.MoveRange(blocks, 2, 3, -1));
            Assert.Equal("B0,A0,a1,C0", Shape(blocks));

            Assert.Equal(2, BlockOperations.MoveRange(blocks, 1, 3, 1));
            Assert.Equal("B0,C0,A0,a1", Shape(blocks));

            Assert.Equal(-1, BlockOperations.MoveRange(blocks, 0, 1, -1));
            Assert.Equal(-1, BlockOperations.MoveRange(blocks, 2, 4, 1));
        }

        [Fact]
        public void MoveRange_LeavesParentAtEdges()
        {
            var up = new List<EditorBlock> { B(BlockKind.Bulleted, "P"), B(BlockKind.Bulleted, "c", 1), B(BlockKind.Bulleted, "d", 1) };
            Assert.Equal(0, BlockOperations.MoveRange(up, 1, 2, -1));
            Assert.Equal("c0,P0,d1", Shape(up));

            var down = new List<EditorBlock> { B(BlockKind.Bulleted, "P"), B(BlockKind.Bulleted, "c", 1), B(BlockKind.Bulleted, "Q") };
            Assert.Equal(2, BlockOperations.MoveRange(down, 1, 2, 1));
            Assert.Equal("P0,Q0,c0", Shape(down));
        }

        [Fact]
        public void CanIndent_OnlyUnderListOrToggle()
        {
            Assert.True(BlockOperations.CanIndent(new List<EditorBlock> { B(BlockKind.Bulleted, "a"), B(BlockKind.Paragraph, "b") }, 1));
            Assert.True(BlockOperations.CanIndent(new List<EditorBlock> { B(BlockKind.Toggle, "a"), B(BlockKind.Paragraph, "b") }, 1));
            Assert.False(BlockOperations.CanIndent(new List<EditorBlock> { B(BlockKind.Paragraph, "a"), B(BlockKind.Paragraph, "b") }, 1));
            Assert.True(BlockOperations.CanIndent(new List<EditorBlock> { B(BlockKind.Bulleted, "a"), B(BlockKind.Paragraph, "x", 1), B(BlockKind.Bulleted, "b") }, 2));
            Assert.False(BlockOperations.CanIndent(new List<EditorBlock> { B(BlockKind.Bulleted, "a") }, 0));
        }

        [Fact]
        public void NormalizeIndents_ClampsOrphanedChildren()
        {
            var blocks = new List<EditorBlock> { B(BlockKind.Paragraph, "p", 1), B(BlockKind.Paragraph, "x", 2), B(BlockKind.Bulleted, "b"), B(BlockKind.Bulleted, "y", 3) };

            BlockOperations.NormalizeIndents(blocks);

            Assert.Equal("p0,x0,b0,y1", Shape(blocks));
        }

        [Fact]
        public void ListLabels_UseNotionLevelStyles()
        {
            var blocks = new List<EditorBlock>
            {
                B(BlockKind.Numbered, "1"), B(BlockKind.Numbered, "2"),
                B(BlockKind.Numbered, "a", 1), B(BlockKind.Numbered, "b", 1),
                B(BlockKind.Numbered, "i", 2), B(BlockKind.Numbered, "ii", 2),
                B(BlockKind.Bulleted, "dot", 1),
            };

            Assert.Equal(new[] { "1.", "2.", "a.", "b.", "i.", "ii.", "◦" }, BlockOperations.ListLabels(blocks));
        }

        [Fact]
        public void VisibleFlags_HideCollapsedToggleChildren()
        {
            var blocks = new List<EditorBlock>
            {
                new EditorBlock(BlockKind.Toggle, "t") { Collapsed = true },
                B(BlockKind.Paragraph, "child", 1),
                B(BlockKind.Bulleted, "grandchild", 2),
                B(BlockKind.Paragraph, "after"),
                new EditorBlock(BlockKind.Toggle, "open") { Collapsed = false },
                B(BlockKind.Paragraph, "shown", 1),
            };

            Assert.Equal(new[] { true, false, false, true, true, true }, BlockOperations.VisibleFlags(blocks));
            Assert.Equal(3, BlockOperations.SubtreeEnd(blocks, 0));
            Assert.Equal(4, BlockOperations.ExtendToSubtrees(blocks, 3, 4));
        }
        // ── 표·이미지 ──────────────────────────────────────────────────

        [Fact]
        public void Table_ParsesCellsAlignmentAndEscapedPipes()
        {
            var blocks = BlockMarkdown.Parse(Md("이름 | 값 | 비고", ":--- | ---: | :---:", "a \\| b | 1", "c | 2 | 끝 |"));

            Assert.Equal(3, blocks.Count);
            Assert.All(blocks, b => Assert.Equal(BlockKind.TableRow, b.Kind));
            Assert.Equal("이름\t값\t비고", blocks[0].Text);
            Assert.Equal("l,r,c", blocks[0].TableAlign);
            Assert.Equal("a | b\t1", blocks[1].Text);
            Assert.Equal("c\t2\t끝", blocks[2].Text);

            // 정규화: 앞뒤 파이프, 열 수 맞춤(짧은 행은 빈 셀), 셀 안 파이프 이스케이프, 정렬 유지
            Assert.Equal(Md("| 이름 | 값 | 비고 |", "| :--- | ---: | :---: |", "| a \\| b | 1 | |", "| c | 2 | 끝 |"), BlockMarkdown.Serialize(blocks));
        }

        [Fact]
        public void Table_NeedsPipedDelimiterRowWithMatchingCells()
        {
            Assert.Equal(BlockKind.Raw, BlockMarkdown.Parse(Md("| a | b |", "| 1 | 2 |"))[0].Kind);
            Assert.NotEqual(BlockKind.TableRow, BlockMarkdown.Parse(Md("| a |", "---"))[0].Kind);
            Assert.NotEqual(BlockKind.TableRow, BlockMarkdown.Parse(Md("a | b", "| --- |"))[0].Kind);
        }

        [Fact]
        public void Table_EndsAtBlankOrPipelessLine_AndSeparatesFromNeighbours()
        {
            var blocks = BlockMarkdown.Parse(Md("앞 문단", "", "| h |", "| - |", "| x |", "뒤 문단"));

            Assert.Equal(new[] { BlockKind.Paragraph, BlockKind.TableRow, BlockKind.TableRow, BlockKind.Paragraph }, blocks.Select(b => b.Kind));
            Assert.Equal(Md("앞 문단", "", "| h |", "| --- |", "| x |", "", "뒤 문단"), BlockMarkdown.Serialize(blocks));
        }

        [Fact]
        public void Table_LayoutGivesColumnsAndHeaderPerTable()
        {
            var blocks = new List<EditorBlock>
            {
                B(BlockKind.TableRow, "a\tb"),
                B(BlockKind.TableRow, "1\t2\t3"),
                B(BlockKind.Paragraph, "p"),
                B(BlockKind.TableRow, "x"),
            };
            int[] columns;
            bool[] header;
            BlockOperations.TableLayout(blocks, out columns, out header);

            Assert.Equal(new[] { 3, 3, 0, 1 }, columns);
            Assert.Equal(new[] { true, false, false, true }, header);
        }

        [Fact]
        public void Image_OnOwnLine_IsImageBlock()
        {
            var blocks = BlockMarkdown.Parse(Md("![다이어그램](images/arch.png)", "", "![](<my pics/a b.png> \"제목\")", "", "글 중간 ![x](y.png) 이미지"));

            Assert.Equal(new[] { BlockKind.Image, BlockKind.Image, BlockKind.Paragraph }, blocks.Select(b => b.Kind));
            Assert.Equal("다이어그램", blocks[0].Text);
            Assert.Equal("images/arch.png", blocks[0].Source);
            Assert.Equal("my pics/a b.png", blocks[1].Source);
            Assert.Equal(Md("![다이어그램](images/arch.png)", "", "![](<my pics/a b.png>)", "", "글 중간 ![x](y.png) 이미지"), BlockMarkdown.Serialize(blocks));
        }

        [Fact]
        public void ListNumbers_MatchLabels()
        {
            var blocks = new List<EditorBlock>
            {
                B(BlockKind.Numbered, "a"), B(BlockKind.Paragraph, ""), B(BlockKind.Numbered, "b"),
                B(BlockKind.Numbered, "c", 1), B(BlockKind.Paragraph, "x"), B(BlockKind.Numbered, "d"),
            };

            Assert.Equal(new[] { 1, 0, 2, 1, 0, 1 }, BlockOperations.ListNumbers(blocks));
        }

        [Fact]
        public void SameContent_ComparesEveryField()
        {
            var a = new EditorBlock(BlockKind.Image, "alt") { Source = "a.png" };
            var b = a.Clone();
            Assert.True(a.SameContent(b));
            Assert.Equal(a.Id, b.Id);
            b.Source = "b.png";
            Assert.False(a.SameContent(b));
            Assert.NotEqual(a.Id, a.Duplicate().Id);
        }

        [Fact]
        public void Toc_Math_File_Bookmark_RoundTripAndParseVariants()
        {
            var markdown = Md(
                "[TOC]",
                "",
                "$$",
                "E = mc^2",
                "$$",
                "",
                "$$ \\frac{a}{b} $$",
                "",
                "[📎 설계서.pdf](docs/spec.pdf)",
                "",
                "[🔖 Folderss 저장소](https://github.com/example/folderss)",
                "",
                "<!-- toc -->");

            var blocks = BlockMarkdown.Parse(markdown);

            Assert.Equal(new[]
            {
                BlockKind.Toc,
                BlockKind.Math,
                BlockKind.Math,
                BlockKind.File,
                BlockKind.Bookmark,
                BlockKind.Toc,
            }, blocks.Select(b => b.Kind));

            Assert.Equal("E = mc^2", blocks[1].Text);
            Assert.Equal("\\frac{a}{b}", blocks[2].Text);
            Assert.Equal("설계서.pdf", blocks[3].Text);
            Assert.Equal("docs/spec.pdf", blocks[3].Source);
            Assert.Equal("Folderss 저장소", blocks[4].Text);
            Assert.Equal("https://github.com/example/folderss", blocks[4].Source);

            var serialized = BlockMarkdown.Serialize(blocks);
            Assert.Equal(Md(
                "[TOC]",
                "",
                "$$",
                "E = mc^2",
                "$$",
                "",
                "$$",
                "\\frac{a}{b}",
                "$$",
                "",
                "[📎 설계서.pdf](docs/spec.pdf)",
                "",
                "[🔖 Folderss 저장소](https://github.com/example/folderss)",
                "",
                "[TOC]"), serialized);
        }
    }
}

using Folderss.Models;
using Folderss.Services;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>블록 편집기 사이드카(#78 D안): Markdown 밖 속성을 문서 옆 JSON에 저장하고 다시 붙이기.</summary>
    public sealed class BlockSidecarTests
    {
        private static EditorBlock B(BlockKind kind, string text, string background = null)
        {
            var block = new EditorBlock(kind, text);
            if (background != null)
                block.SetAttribute("background", background);
            return block;
        }

        /// <summary>저장(Markdown + 사이드카) 후 다시 읽은 블록 목록.</summary>
        private static List<EditorBlock> SaveAndReload(List<EditorBlock> blocks, out string sidecar, out int unmatched, System.Func<string, string> editMarkdown = null)
        {
            var markdown = BlockMarkdown.Serialize(blocks);
            sidecar = BlockSidecar.Serialize(blocks, markdown, "문서.md");
            if (editMarkdown != null)
                markdown = editMarkdown(markdown);
            var reread = BlockMarkdown.Parse(markdown);
            unmatched = BlockSidecar.Apply(reread, sidecar);
            return reread;
        }

        [Fact]
        public void NoAttributes_NoSidecar()
        {
            var blocks = new List<EditorBlock> { B(BlockKind.Heading1, "제목"), B(BlockKind.Paragraph, "본문") };

            Assert.Null(BlockSidecar.Serialize(blocks, BlockMarkdown.Serialize(blocks), "문서.md"));
        }

        [Fact]
        public void Attributes_RoundTrip_AndMarkdownStaysPure()
        {
            var blocks = new List<EditorBlock>
            {
                B(BlockKind.Heading1, "제목", "blue"),
                B(BlockKind.Paragraph, "본문"),
                B(BlockKind.TableRow, "a\tb", "yellow"),
                B(BlockKind.TableRow, "1\t2"),
            };
            blocks[1].SetAttribute("color", "red");

            string sidecar;
            int unmatched;
            var reread = SaveAndReload(blocks, out sidecar, out unmatched);

            Assert.DoesNotContain("blue", BlockMarkdown.Serialize(blocks));
            Assert.Contains("\"version\": 1", sidecar);
            Assert.Contains("\"preview\": \"제목\"", sidecar);   // 한글이 \uXXXX로 바뀌지 않음
            Assert.Equal(0, unmatched);
            Assert.Equal("blue", reread[0].GetAttribute("background"));
            Assert.Equal("red", reread[1].GetAttribute("color"));
            Assert.Equal("yellow", reread[2].GetAttribute("background"));
            Assert.Null(reread[3].Attributes);
        }

        [Fact]
        public void EmptyParagraphs_DoNotShiftIndexes()
        {
            var blocks = new List<EditorBlock> { B(BlockKind.Paragraph, "a"), B(BlockKind.Paragraph, ""), B(BlockKind.Paragraph, "b", "green") };

            string sidecar;
            int unmatched;
            var reread = SaveAndReload(blocks, out sidecar, out unmatched);

            Assert.Equal(0, unmatched);
            Assert.Equal("green", reread.Single(b => b.Text == "b").GetAttribute("background"));
        }

        [Fact]
        public void ExternalInsertBefore_ReattachesByContent()
        {
            var blocks = new List<EditorBlock> { B(BlockKind.Paragraph, "첫째"), B(BlockKind.Paragraph, "둘째", "pink") };

            string sidecar;
            int unmatched;
            var reread = SaveAndReload(blocks, out sidecar, out unmatched, md => "# 다른 도구에서 넣은 제목\n\n새 문단\n\n" + md);

            Assert.Equal(0, unmatched);
            Assert.Equal("pink", reread.Single(b => b.Text == "둘째").GetAttribute("background"));
            Assert.Null(reread.Single(b => b.Text == "새 문단").Attributes);
        }

        [Fact]
        public void DuplicateContent_UsesPreviousBlockToDisambiguate()
        {
            var blocks = new List<EditorBlock>
            {
                B(BlockKind.Paragraph, "앞 A"), B(BlockKind.Paragraph, "같은 문장"),
                B(BlockKind.Paragraph, "앞 B"), B(BlockKind.Paragraph, "같은 문장", "red"),
            };

            string sidecar;
            int unmatched;
            var reread = SaveAndReload(blocks, out sidecar, out unmatched, md => "끼워 넣은 문단\n\n" + md);

            Assert.Equal(0, unmatched);
            var same = reread.Where(b => b.Text == "같은 문장").ToList();
            Assert.Null(same[0].Attributes);
            Assert.Equal("red", same[1].GetAttribute("background"));
        }

        [Fact]
        public void ChangedText_IsReportedAsUnmatched()
        {
            var blocks = new List<EditorBlock> { B(BlockKind.Paragraph, "원래 문장", "gray"), B(BlockKind.Paragraph, "그대로", "blue") };

            string sidecar;
            int unmatched;
            var reread = SaveAndReload(blocks, out sidecar, out unmatched, md => md.Replace("원래 문장", "고친 문장"));

            Assert.Equal(1, unmatched);
            Assert.Null(reread[0].Attributes);
            Assert.Equal("blue", reread[1].GetAttribute("background"));
        }

        [Fact]
        public void UnreadableOrNewerSidecar_ReturnsUnreadable()
        {
            var blocks = BlockMarkdown.Parse("본문\n");

            Assert.Equal(BlockSidecar.Unreadable, BlockSidecar.Apply(blocks, "{ 깨진 JSON"));
            Assert.Equal(BlockSidecar.Unreadable, BlockSidecar.Apply(blocks, "{\"version\": 99, \"blocks\": []}"));
            Assert.Equal(0, BlockSidecar.Apply(blocks, null));
            Assert.Null(blocks[0].Attributes);
        }

        [Fact]
        public void Attributes_ParticipateInCloneAndSameContent()
        {
            var a = B(BlockKind.Paragraph, "x", "red");
            var clone = a.Clone();

            Assert.True(a.SameContent(clone));
            clone.SetAttribute("background", null);
            Assert.Null(clone.Attributes);
            Assert.False(a.SameContent(clone));
            Assert.Equal("red", a.GetAttribute("background"));   // 사본을 고쳐도 원본은 그대로
            Assert.Equal("doc.md.meta.json", BlockSidecar.PathFor("doc.md"));
        }

        [Fact]
        public void MediaAndBlockAttributes_RoundTripThroughSidecar()
        {
            var toggleHeading = new EditorBlock(BlockKind.Toggle, "아키텍처 개요");
            toggleHeading.SetAttribute(EditorBlock.AttrHeading, "2");

            var callout = new EditorBlock(BlockKind.Callout, "주의할 점") { CalloutType = "WARNING" };
            callout.SetAttribute(EditorBlock.AttrIcon, "🔥");

            var image = new EditorBlock(BlockKind.Image, "다이어그램") { Source = "images/arch.png" };
            image.SetAttribute(EditorBlock.AttrWidth, "480");
            image.SetAttribute(EditorBlock.AttrAlign, "center");

            var file = new EditorBlock(BlockKind.File, "설계서.pdf") { Source = "docs/spec.pdf" };
            file.SetAttribute(EditorBlock.AttrSize, "1.4 MB");

            var bookmark = new EditorBlock(BlockKind.Bookmark, "Folderss GitHub") { Source = "https://github.com/example/folderss" };
            bookmark.SetAttribute(EditorBlock.AttrTitle, "Folderss - Dual Pane File Manager");
            bookmark.SetAttribute(EditorBlock.AttrDescription, "Windows WPF dual-pane file manager.");

            var blocks = new List<EditorBlock>
            {
                toggleHeading,
                new EditorBlock(BlockKind.Paragraph, "토글 내부 문단", 1),
                callout,
                image,
                file,
                bookmark,
            };

            string sidecar;
            int unmatched;
            var reread = SaveAndReload(blocks, out sidecar, out unmatched);

            Assert.Equal(0, unmatched);
            Assert.Equal("2", reread[0].GetAttribute(EditorBlock.AttrHeading));
            Assert.Equal(2, reread[0].GetToggleHeadingLevel());
            Assert.Equal(2, reread[0].GetHeadingLevel());
            Assert.Equal("🔥", reread[2].GetAttribute(EditorBlock.AttrIcon));
            Assert.Equal("480", reread[3].GetAttribute(EditorBlock.AttrWidth));
            Assert.Equal("center", reread[3].GetAttribute(EditorBlock.AttrAlign));
            Assert.Equal("1.4 MB", reread[4].GetAttribute(EditorBlock.AttrSize));
            Assert.Equal("Folderss - Dual Pane File Manager", reread[5].GetAttribute(EditorBlock.AttrTitle));
            Assert.Equal("Windows WPF dual-pane file manager.", reread[5].GetAttribute(EditorBlock.AttrDescription));
        }

        /// <summary>#75 표: 열 너비(colwidths)·머리글 끄기(noheader)는 표의 첫 행에 붙어 왕복하고, 정렬은 Markdown 구분 행으로 남는다.</summary>
        [Fact]
        public void TableAttributes_RoundTrip_OnFirstRow()
        {
            var header = new EditorBlock(BlockKind.TableRow, "이름\t크기\t날짜") { TableAlign = "l,r,c" };
            BlockTable.SetColumnWidth(header, 3, 0, 180);
            BlockTable.SetColumnWidth(header, 3, 2, 96);
            BlockTable.SetHeaderHidden(header, true);
            var blocks = new List<EditorBlock>
            {
                new EditorBlock(BlockKind.Paragraph, "표 앞"),
                header,
                new EditorBlock(BlockKind.TableRow, "a.txt\t1 KB\t2026-11-01"),
                new EditorBlock(BlockKind.TableRow, "b.txt\t2 KB\t2026-11-02"),
            };

            string sidecar;
            int unmatched;
            var reread = SaveAndReload(blocks, out sidecar, out unmatched);

            Assert.Equal(0, unmatched);
            Assert.DoesNotContain("noheader", BlockMarkdown.Serialize(blocks));
            Assert.Contains("\"colwidths\": \"180,,96\"", sidecar);
            Assert.Equal("180,,96", reread[1].GetAttribute(EditorBlock.AttrColWidths));
            Assert.Equal("true", reread[1].GetAttribute(EditorBlock.AttrNoHeader));
            Assert.True(BlockTable.IsHeaderHidden(reread[1]));
            Assert.Equal("l,r,c", reread[1].TableAlign);
            Assert.Null(reread[2].Attributes);
            Assert.Null(reread[3].Attributes);
        }

        // ── #76 9번: 줄 안 밑줄·글자 색·형광펜(marks) ──────────────────────────

        private static EditorBlock Marked(BlockKind kind, string text, params TextMark[] marks)
        {
            var block = new EditorBlock(kind, text);
            TextMarks.Set(block, marks);
            return block;
        }

        /// <summary>marks는 보이는 글자 기준으로 저장되고 같은 블록에 다시 붙는다. Markdown에는 아무 표식도 없다(다른 도구에서 열면 그냥 글자).</summary>
        [Fact]
        public void TextMarks_RoundTrip_SameBlock_MarkdownUntouched()
        {
            var blocks = new List<EditorBlock>
            {
                new EditorBlock(BlockKind.Heading1, "제목"),
                Marked(BlockKind.Paragraph, "**굵은** 밑줄 노랑",
                    new TextMark(TextMarkKind.Underline, null, 3, 5),
                    new TextMark(TextMarkKind.Highlight, "yellow", 6, 8),
                    new TextMark(TextMarkKind.Color, "red", 0, 2)),
                Marked(BlockKind.Bulleted, "목록 항목", new TextMark(TextMarkKind.Color, "blue", 0, 2)),
            };
            var before = BlockMarkdown.Serialize(blocks);

            string sidecar;
            int unmatched;
            var reread = SaveAndReload(blocks, out sidecar, out unmatched);

            Assert.Equal(0, unmatched);
            var plainBlocks = blocks.Select(b => new EditorBlock(b.Kind, b.Text)).ToList();
            Assert.Equal(BlockMarkdown.Serialize(plainBlocks), before);   // 속성이 없을 때와 같은 Markdown
            Assert.DoesNotContain("yellow", before);
            Assert.DoesNotContain("<u>", before);
            Assert.Contains("\"marks\":", sidecar);
            var paragraph = reread[1];
            Assert.Equal("**굵은** 밑줄 노랑", paragraph.Text);
            var marks = TextMarks.Get(paragraph);
            Assert.Equal(3, marks.Count);
            Assert.Contains(marks, m => m.Kind == TextMarkKind.Underline && m.Start == 3 && m.End == 5);
            Assert.Contains(marks, m => m.Kind == TextMarkKind.Highlight && m.Value == "yellow" && m.Start == 6 && m.End == 8);
            Assert.Contains(marks, m => m.Kind == TextMarkKind.Color && m.Value == "red" && m.Start == 0 && m.End == 2);
            Assert.Equal("blue", TextMarks.Get(reread[2]).Single().Value);
            Assert.Equal(paragraph.GetAttribute(TextMarks.Attribute), blocks[1].GetAttribute(TextMarks.Attribute));
        }

        /// <summary>marks 값의 보이는 글자 해시가 본문과 맞지 않으면(사이드카를 손으로 고침·형식 바뀜) 칠하지 않고 버리며, 붙이지 못한 속성으로 센다.</summary>
        [Fact]
        public void TextMarks_HashMismatch_DroppedAndCounted()
        {
            var blocks = new List<EditorBlock> { Marked(BlockKind.Paragraph, "가나다라", new TextMark(TextMarkKind.Underline, null, 0, 2)) };
            blocks[0].SetAttribute("color", "red");
            var markdown = BlockMarkdown.Serialize(blocks);
            var sidecar = BlockSidecar.Serialize(blocks, markdown, "문서.md");
            var value = blocks[0].GetAttribute(TextMarks.Attribute);
            var tampered = sidecar.Replace(value, "00000000|u:0-2");

            var reread = BlockMarkdown.Parse(markdown);
            var unmatched = BlockSidecar.Apply(reread, tampered);

            Assert.Equal(1, unmatched);
            Assert.Null(reread[0].GetAttribute(TextMarks.Attribute));
            Assert.Equal("red", reread[0].GetAttribute("color"));   // 다른 속성은 그대로
        }

        /// <summary>다른 도구에서 그 블록 본문을 고치면 블록 해시가 달라져 marks도 붙지 않는다(엉뚱한 글자에 칠하지 않음). 다른 블록의 marks는 그대로.</summary>
        [Fact]
        public void TextMarks_ExternalEdit_DropsOnlyChangedBlock()
        {
            var blocks = new List<EditorBlock>
            {
                Marked(BlockKind.Paragraph, "첫 문단 색", new TextMark(TextMarkKind.Color, "green", 0, 1)),
                Marked(BlockKind.Paragraph, "둘째 문단 밑줄", new TextMark(TextMarkKind.Underline, null, 3, 5)),
            };

            string sidecar;
            int unmatched;
            var reread = SaveAndReload(blocks, out sidecar, out unmatched, md => md.Replace("둘째 문단", "바뀐 둘째 문단"));

            Assert.Equal(1, unmatched);
            Assert.Equal("green", TextMarks.Get(reread[0]).Single().Value);
            Assert.Empty(TextMarks.Get(reread[1]));
            Assert.Null(reread[1].GetAttribute(TextMarks.Attribute));
        }

        /// <summary>저장하며 보이는 글자가 달라지면(줄 끝 공백 정리 등) 다시 읽은 글자 기준으로 옮겨 저장해 다시 붙는다.</summary>
        [Fact]
        public void TextMarks_AnchoredToRereadText()
        {
            var blocks = new List<EditorBlock> { Marked(BlockKind.Paragraph, "  앞 공백 글자", new TextMark(TextMarkKind.Highlight, "pink", 4, 6)) };

            string sidecar;
            int unmatched;
            var reread = SaveAndReload(blocks, out sidecar, out unmatched);

            Assert.Equal(0, unmatched);
            var plain = TextMarks.PlainOf(reread[0]);
            var mark = TextMarks.Get(reread[0]).Single();
            Assert.Equal("공백", plain.Substring(mark.Start, mark.End - mark.Start));
        }

        /// <summary>표 셀·코드 블록은 marks 대상이 아니다(종류가 바뀌면 지움, 저장하지 않음).</summary>
        [Fact]
        public void TextMarks_NotStoredForNonTargetKinds()
        {
            var block = Marked(BlockKind.Paragraph, "코드로 바꿈", new TextMark(TextMarkKind.Underline, null, 0, 2));
            Assert.NotNull(block.GetAttribute(TextMarks.Attribute));

            block.Kind = BlockKind.Code;

            Assert.Null(block.GetAttribute(TextMarks.Attribute));
            Assert.Null(BlockSidecar.Serialize(new List<EditorBlock> { block }, BlockMarkdown.Serialize(new List<EditorBlock> { block }), "문서.md"));
        }
    }
}

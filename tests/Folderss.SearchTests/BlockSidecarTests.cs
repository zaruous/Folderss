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
    }
}

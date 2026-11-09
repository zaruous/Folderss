using Folderss.Models;
using Folderss.Services;
using System.Collections.Generic;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>
    /// 표 셀 안 줄바꿈(Ctrl+Enter): Markdown에는 태그 없이 공백으로 쓰고, 어느 공백이 줄바꿈인지는 사이드카 속성(cellbreaks)에 둔다(#78).
    /// </summary>
    public sealed class TableCellBreaksTests
    {
        /// <summary>셀 입력란에서 고친 것처럼 본문·속성을 바꾼다(BlockEditor.OnCellTextChanged와 같은 순서).</summary>
        private static EditorBlock Edited(params string[] cells)
        {
            var row = new EditorBlock(BlockKind.TableRow, string.Empty);
            row.Text = BlockMarkdown.JoinCells(cells);
            TableCellBreaks.Update(row, cells);
            return row;
        }

        private static List<EditorBlock> SaveAndReload(List<EditorBlock> blocks, System.Func<string, string> editMarkdown = null)
        {
            var markdown = BlockMarkdown.Serialize(blocks);
            var sidecar = BlockSidecar.Serialize(blocks, markdown, "문서.md");
            if (editMarkdown != null)
                markdown = editMarkdown(markdown);
            var reread = BlockMarkdown.Parse(markdown);
            BlockSidecar.Apply(reread, sidecar);
            return reread;
        }

        [Fact]
        public void Breaks_RoundTripThroughSidecar_AndMarkdownHasNoTags()
        {
            var blocks = new List<EditorBlock> { Edited("머리", "열"), Edited("첫 줄\n둘째 줄\n\n넷째", "c") };

            var markdown = BlockMarkdown.Serialize(blocks);
            Assert.Contains("| 첫 줄 둘째 줄  넷째 | c |", markdown);
            Assert.DoesNotContain("<", markdown);

            var reread = SaveAndReload(blocks);
            Assert.Equal(new[] { "첫 줄\n둘째 줄\n\n넷째", "c" }, TableCellBreaks.EditTexts(reread[1]));
            Assert.Equal(new[] { "머리", "열" }, TableCellBreaks.EditTexts(reread[0]));
        }

        [Fact]
        public void NoBreaks_NoAttribute()
        {
            var row = Edited("a\nb", "c");
            Assert.NotNull(row.GetAttribute(TableCellBreaks.Attribute));

            TableCellBreaks.Update(row, new[] { "a b", "c" });
            Assert.Null(row.GetAttribute(TableCellBreaks.Attribute));
        }

        [Fact]
        public void CellChangedOutside_BreaksAreDropped_OtherCellsKept()
        {
            var blocks = new List<EditorBlock> { Edited("h1", "h2"), Edited("x\ny", "p\nq") };

            // 다른 편집기에서 첫 셀만 고침 → 그 셀 줄바꿈만 버리고(공백으로 보임) 둘째 셀은 유지
            var reread = SaveAndReload(blocks, md => md.Replace("| x y |", "| x z |"));
            var texts = TableCellBreaks.EditTexts(reread[1]);
            Assert.Equal("x z", texts[0]);
        }

        [Fact]
        public void BreakAtCellEdge_StaysWhileEditing_AndIsDroppedOnReload()
        {
            // 셀 끝에서 Ctrl+Enter를 누른 직후(아직 다음 줄을 안 씀) 입력란 글이 그대로 돌아와야 한다 — 아니면 화면 갱신이 줄바꿈을 지운다.
            var row = Edited("a\n", "\nb");
            Assert.Equal(new[] { "a\n", "\nb" }, TableCellBreaks.EditTexts(row));

            // Markdown은 셀 앞뒤 공백을 버리므로 다시 읽으면 가장자리 줄바꿈은 없다.
            var reread = SaveAndReload(new List<EditorBlock> { Edited("h", "h"), row });
            Assert.Equal(new[] { "a", "b" }, TableCellBreaks.EditTexts(reread[1]));
        }

        [Fact]
        public void TrailingSpaceInCell_DoesNotLoseRowAttributes()
        {
            // 셀 끝 공백(가장자리 줄바꿈 포함)은 저장하면 사라진다. 그래도 같은 행의 다른 속성(열 너비, 다른 셀 줄바꿈)은 남아야 한다.
            var header = Edited("a\n", "b");
            header.SetAttribute("colwidths", "120,");
            var body = Edited("x\ny ", "z");

            var reread = SaveAndReload(new List<EditorBlock> { header, body });

            Assert.Equal("120,", reread[0].GetAttribute("colwidths"));
            Assert.Equal(new[] { "x\ny", "z" }, TableCellBreaks.EditTexts(reread[1]));
        }

        [Fact]
        public void SameTextInTwoCells_BothGetTheirBreaks()
        {
            var row = Edited("a\nb", "a\nb");
            Assert.Equal(new[] { "a\nb", "a\nb" }, TableCellBreaks.EditTexts(row));
        }
    }
}

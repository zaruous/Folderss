using Folderss.Models;
using Folderss.Services;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>블록 편집기 여러 열 배치(#77): 사이드카 열 속성 정리 규칙·열 만들기·옮기기, 페이지 링크 블록 Markdown.</summary>
    public sealed class BlockColumnTests
    {
        private static EditorBlock P(string text, string column = null)
        {
            var block = new EditorBlock(BlockKind.Paragraph, text);
            if (column != null)
                block.SetAttribute(EditorBlock.AttrColumn, column);
            return block;
        }

        private static string Columns(List<EditorBlock> blocks)
        {
            return string.Join(" ", blocks.Select(b => b.GetAttribute(EditorBlock.AttrColumn) ?? "-"));
        }

        private static void Layout(List<EditorBlock> blocks, out int[] start, out int[] end, out int[] column, out double[][] ratios)
        {
            BlockOperations.ColumnLayout(blocks, out start, out end, out column, out ratios);
        }

        [Fact]
        public void Layout_GroupsConsecutiveBlocks_AndRenumbersColumns()
        {
            var blocks = new List<EditorBlock> { P("앞"), P("a", "g/0"), P("b", "g/0"), P("c", "g/3"), P("뒤") };
            int[] start, end, column;
            double[][] ratios;
            Layout(blocks, out start, out end, out column, out ratios);

            Assert.Equal("- g/0 g/0 g/1 -", Columns(blocks));
            Assert.Equal(new[] { -1, 1, 1, 1, -1 }, start);
            Assert.Equal(new[] { -1, 4, 4, 4, -1 }, end);
            Assert.Equal(new[] { 0, 0, 0, 1, 0 }, column);
            Assert.Equal(new[] { 0.5, 0.5 }, ratios[1]);
        }

        [Fact]
        public void Layout_BlockBetweenMembers_JoinsPreviousColumn()
        {
            var blocks = new List<EditorBlock> { P("a", "g/0"), P("붙여넣음"), P("b", "g/1") };
            int[] start, end, column;
            double[][] ratios;
            Layout(blocks, out start, out end, out column, out ratios);
            Assert.Equal("g/0 g/0 g/1", Columns(blocks));
        }

        [Fact]
        public void Layout_SingleColumnOrSeparatedGroup_IsDissolved()
        {
            var single = new List<EditorBlock> { P("a", "g/1"), P("b", "g/1") };
            int[] start, end, column;
            double[][] ratios;
            Layout(single, out start, out end, out column, out ratios);
            Assert.Equal("- -", Columns(single));
            Assert.All(start, s => Assert.Equal(-1, s));

            // 다른 묶음을 사이에 두고 같은 묶음id가 다시 나오면(복사·외부 편집) 뒤쪽은 묶음에서 뺀다
            var separated = new List<EditorBlock> { P("a", "g/0"), P("b", "g/1"), P("x", "h/0"), P("y", "h/1"), P("c", "g/0"), P("d", "g/1") };
            Layout(separated, out start, out end, out column, out ratios);
            Assert.Equal("g/0 g/1 h/0 h/1 - -", Columns(separated));
        }

        [Fact]
        public void Layout_DecreasingColumn_StaysInPreviousColumn()
        {
            var blocks = new List<EditorBlock> { P("a", "g/0"), P("b", "g/1"), P("c", "g/0") };
            int[] start, end, column;
            double[][] ratios;
            Layout(blocks, out start, out end, out column, out ratios);
            Assert.Equal("g/0 g/1 g/1", Columns(blocks));
        }

        [Fact]
        public void Layout_Ratio_KeptOnlyOnFirstBlockWhenCountMatches()
        {
            var blocks = new List<EditorBlock> { P("a", "g/0"), P("b", "g/1") };
            blocks[0].SetAttribute(EditorBlock.AttrColumnRatio, "1:3");
            blocks[1].SetAttribute(EditorBlock.AttrColumnRatio, "1:1");
            int[] start, end, column;
            double[][] ratios;
            Layout(blocks, out start, out end, out column, out ratios);
            Assert.Equal(new[] { 0.25, 0.75 }, ratios[0]);
            Assert.Equal("1:3", blocks[0].GetAttribute(EditorBlock.AttrColumnRatio));
            Assert.Null(blocks[1].GetAttribute(EditorBlock.AttrColumnRatio));

            blocks[0].SetAttribute(EditorBlock.AttrColumnRatio, "1:2:3");
            Layout(blocks, out start, out end, out column, out ratios);
            Assert.Null(blocks[0].GetAttribute(EditorBlock.AttrColumnRatio));
            Assert.Equal(new[] { 0.5, 0.5 }, ratios[0]);
        }

        [Fact]
        public void MakeColumns_SplitsBlocksEvenly_AndFillsMissingColumns()
        {
            var blocks = new List<EditorBlock> { P("a"), P("b"), P("c"), P("d") };
            Assert.Equal(0, BlockOperations.MakeColumns(blocks, 0, 4, 2));
            int[] start, end, column;
            double[][] ratios;
            Layout(blocks, out start, out end, out column, out ratios);
            Assert.Equal(new[] { 0, 0, 1, 1 }, column);

            var one = new List<EditorBlock> { P("a"), P("밖") };
            Assert.Equal(2, BlockOperations.MakeColumns(one, 0, 1, 3));
            Layout(one, out start, out end, out column, out ratios);
            Assert.Equal(new[] { "a", "", "", "밖" }, one.Select(b => b.Text));
            Assert.Equal(new[] { 0, 1, 2, 0 }, column);
            Assert.Equal(-1, start[3]);
        }

        [Fact]
        public void MakeColumns_KeepsChildrenWithParent()
        {
            var blocks = new List<EditorBlock>
            {
                new EditorBlock(BlockKind.Bulleted, "부모"),
                new EditorBlock(BlockKind.Bulleted, "자식", 1),
                P("둘째"),
            };
            BlockOperations.MakeColumns(blocks, 0, 3, 2);
            int[] start, end, column;
            double[][] ratios;
            Layout(blocks, out start, out end, out column, out ratios);
            Assert.Equal(new[] { 0, 0, 1 }, column);
        }

        [Fact]
        public void MoveToColumn_MovesToEndOfNeighbor_AndCreatesNewColumnOnRight()
        {
            var blocks = new List<EditorBlock> { P("a", "g/0"), P("b", "g/0"), P("c", "g/1") };
            int[] start, end, column;
            double[][] ratios;
            Layout(blocks, out start, out end, out column, out ratios);

            var at = BlockOperations.MoveToColumn(blocks, 0, 1, 1, start, end, column);
            Layout(blocks, out start, out end, out column, out ratios);
            Assert.Equal(2, at);
            Assert.Equal(new[] { "b", "c", "a" }, blocks.Select(b => b.Text));
            Assert.Equal(new[] { 0, 1, 1 }, column);

            at = BlockOperations.MoveToColumn(blocks, 2, 3, 1, start, end, column);
            Layout(blocks, out start, out end, out column, out ratios);
            Assert.Equal(new[] { 0, 1, 2 }, column);

            Assert.Equal(-1, BlockOperations.MoveToColumn(blocks, 0, 1, -1, start, end, column));
            at = BlockOperations.MoveToColumn(blocks, 2, 3, -1, start, end, column);
            Layout(blocks, out start, out end, out column, out ratios);
            Assert.Equal(new[] { "b", "c", "a" }, blocks.Select(b => b.Text));
            Assert.Equal(new[] { 0, 1, 1 }, column);
        }

        [Fact]
        public void MoveToColumn_LastBlockOfColumnLeaves_ColumnDisappears()
        {
            var blocks = new List<EditorBlock> { P("a", "g/0"), P("b", "g/1"), P("c", "g/2") };
            int[] start, end, column;
            double[][] ratios;
            Layout(blocks, out start, out end, out column, out ratios);
            BlockOperations.MoveToColumn(blocks, 1, 2, -1, start, end, column);
            Layout(blocks, out start, out end, out column, out ratios);
            Assert.Equal(new[] { "a", "b", "c" }, blocks.Select(b => b.Text));
            Assert.Equal(new[] { 0, 0, 1 }, column);
        }

        [Fact]
        public void MoveRange_TakesColumnOfBlockItPasses()
        {
            var blocks = new List<EditorBlock> { P("밖"), P("a", "g/0"), P("b", "g/1"), P("c", "g/1") };
            // 오른쪽 열 첫 블록을 위로 → 왼쪽 열 끝
            BlockOperations.MoveRange(blocks, 2, 3, -1);
            Assert.Equal("- g/0 g/0 g/1", Columns(blocks));
            Assert.Equal(new[] { "밖", "b", "a", "c" }, blocks.Select(b => b.Text));
            // 묶음 첫 블록을 위로 → 묶음 밖
            BlockOperations.MoveRange(blocks, 1, 2, -1);
            Assert.Equal("- - g/0 g/1", Columns(blocks));
            // 묶음 밖 블록을 아래로 → 넘어간 블록의 열
            BlockOperations.MoveRange(blocks, 1, 2, 1);
            Assert.Equal(new[] { "b", "a", "밖", "c" }, blocks.Select(t => t.Text));
            Assert.Equal("- g/0 g/0 g/1", Columns(blocks));
        }

        [Fact]
        public void Ungroup_RemovesColumnAttributes()
        {
            var blocks = new List<EditorBlock> { P("a", "g/0"), P("b", "g/1") };
            blocks[0].SetAttribute(EditorBlock.AttrColumnRatio, "1:2");
            int[] start, end, column;
            double[][] ratios;
            Layout(blocks, out start, out end, out column, out ratios);
            BlockOperations.UngroupColumns(blocks, 1, start, end);
            Assert.All(blocks, b => Assert.Null(b.Attributes));
        }

        [Fact]
        public void Columns_RoundTripThroughSidecar_MarkdownStaysSequential()
        {
            var blocks = new List<EditorBlock> { P("제목 밖"), P("왼쪽", "g/0"), P("오른쪽", "g/1"), P("아래 밖") };
            blocks[1].SetAttribute(EditorBlock.AttrColumnRatio, "2:1");
            var markdown = BlockMarkdown.Serialize(blocks);
            Assert.Equal("제목 밖\n\n왼쪽\n\n오른쪽\n\n아래 밖\n", markdown);

            var sidecar = BlockSidecar.Serialize(blocks, markdown, "문서.md");
            var reread = BlockMarkdown.Parse(markdown);
            Assert.Equal(0, BlockSidecar.Apply(reread, sidecar));
            Assert.Equal("- g/0 g/1 -", Columns(reread));
            Assert.Equal("2:1", reread[1].GetAttribute(EditorBlock.AttrColumnRatio));
        }

        [Fact]
        public void PageLink_ParsesAndSerializes()
        {
            var blocks = BlockMarkdown.Parse("[📄 회의록 2026](회의록/2026-10.md)\n\n[📄](<하위 페이지/새 문서.md>)\n");
            Assert.Equal(new[] { BlockKind.Page, BlockKind.Page }, blocks.Select(b => b.Kind));
            Assert.Equal("회의록 2026", blocks[0].Text);
            Assert.Equal("회의록/2026-10.md", blocks[0].Source);
            Assert.Equal("", blocks[1].Text);
            Assert.Equal("하위 페이지/새 문서.md", blocks[1].Source);
            Assert.Equal("[📄 회의록 2026](회의록/2026-10.md)\n\n[📄](<하위 페이지/새 문서.md>)\n", BlockMarkdown.Serialize(blocks));
            Assert.True(EditorBlock.IsLinkCard(BlockKind.Page));
        }

        [Fact]
        public void PageLink_SourceChange_IsPartOfSidecarHash()
        {
            var page = new EditorBlock(BlockKind.Page, "문서") { Source = "a.md" };
            page.SetAttribute(EditorBlock.AttrColumn, "g/0");
            var blocks = new List<EditorBlock> { page, P("옆", "g/1") };
            var markdown = BlockMarkdown.Serialize(blocks);
            var sidecar = BlockSidecar.Serialize(blocks, markdown, "문서.md");
            var reread = BlockMarkdown.Parse(markdown.Replace("(a.md)", "(b.md)"));
            Assert.Equal(1, BlockSidecar.Apply(reread, sidecar));
        }
    }
}

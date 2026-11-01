using Folderss.Models;
using Folderss.Services;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>블록 편집기 선택·이동(#75): 위치로 블록 찾기, 사각 영역 범위, 한 화면 이동 목표, 여러 블록 한 번에 옮기기.</summary>
    public sealed class BlockSelectionTests
    {
        // 블록 4개: 높이 20, 30, 0(접힘), 50 → 문서 끝 100
        private static readonly double[] Tops = { 0, 20, 50, 50, 100 };

        private static EditorBlock P(string text, int indent = 0) => new EditorBlock(BlockKind.Paragraph, text, indent);

        private static EditorBlock B(string text, int indent = 0) => new EditorBlock(BlockKind.Bulleted, text, indent);

        private static string Texts(List<EditorBlock> blocks) => string.Join(" ", blocks.Select(b => b.Text + (b.Indent > 0 ? ":" + b.Indent : "")));

        [Fact]
        public void IndexAt_FindsBlockContainingY_AndSkipsZeroHeightBeforeNext()
        {
            Assert.Equal(0, BlockSelection.IndexAt(Tops, 4, -5));
            Assert.Equal(0, BlockSelection.IndexAt(Tops, 4, 19.9));
            Assert.Equal(1, BlockSelection.IndexAt(Tops, 4, 20));
            Assert.Equal(3, BlockSelection.IndexAt(Tops, 4, 50));   // 높이 0인 2번은 건너뜀
            Assert.Equal(3, BlockSelection.IndexAt(Tops, 4, 500));
            Assert.Equal(-1, BlockSelection.IndexAt(new double[] { 0 }, 0, 10));
        }

        [Fact]
        public void SpanRange_CoversBlocksTouchedByVerticalSpan_InEitherDirection()
        {
            int first, last;
            Assert.True(BlockSelection.SpanRange(Tops, 4, 10, 60, out first, out last));
            Assert.Equal(0, first);
            Assert.Equal(3, last);

            Assert.True(BlockSelection.SpanRange(Tops, 4, 45, 25, out first, out last));
            Assert.Equal(1, first);
            Assert.Equal(1, last);

            // 아래 빈 곳에서 위로 끌면 마지막 블록부터
            Assert.True(BlockSelection.SpanRange(Tops, 4, 140, 70, out first, out last));
            Assert.Equal(3, first);
            Assert.Equal(3, last);
        }

        [Fact]
        public void SpanRange_EndingExactlyOnNextBlockTop_DoesNotIncludeIt()
        {
            int first, last;
            Assert.True(BlockSelection.SpanRange(Tops, 4, 5, 20, out first, out last));
            Assert.Equal(0, first);
            Assert.Equal(0, last);
        }

        [Fact]
        public void SpanRange_OnlyEmptySpaceAboveOrBelow_SelectsNothing()
        {
            int first, last;
            Assert.False(BlockSelection.SpanRange(Tops, 4, 120, 200, out first, out last));
            Assert.False(BlockSelection.SpanRange(Tops, 4, -30, -5, out first, out last));
            Assert.False(BlockSelection.SpanRange(new double[] { 0 }, 0, 0, 10, out first, out last));
        }

        [Fact]
        public void PageTarget_MovesAboutOnePage_AndAtLeastOneBlock()
        {
            var tops = Enumerable.Range(0, 101).Select(i => i * 20.0).ToArray();   // 100블록 × 20
            Assert.Equal(25, BlockSelection.PageTarget(tops, 100, 10, 300, 1));
            Assert.Equal(0, BlockSelection.PageTarget(tops, 100, 10, 300, -1));
            Assert.Equal(99, BlockSelection.PageTarget(tops, 100, 95, 300, 1));
            Assert.Equal(99, BlockSelection.PageTarget(tops, 100, 99, 300, 1));

            // 블록 하나가 화면보다 크면 한 블록 넘어간다
            var tall = new double[] { 0, 1000, 1020, 1040 };
            Assert.Equal(1, BlockSelection.PageTarget(tall, 3, 0, 300, 1));
        }

        [Fact]
        public void PageTarget_SkipsHiddenBlocks()
        {
            var tops = Enumerable.Range(0, 11).Select(i => i * 20.0).ToArray();
            var hidden = new HashSet<int> { 5, 6 };
            Assert.Equal(4, BlockSelection.PageTarget(tops, 10, 0, 120, 1, i => !hidden.Contains(i)));
        }

        [Fact]
        public void MoveRangeTo_MovesSeveralBlocksDown_KeepingOrderAndRelativeIndent()
        {
            var blocks = new List<EditorBlock> { B("a"), B("a1", 1), P("b"), P("c"), P("d") };
            var at = BlockSelection.MoveRangeTo(blocks, 0, 3, 4, null);   // a, a1, b → d 앞
            Assert.Equal(1, at);
            Assert.Equal("c a a1:1 b d", Texts(blocks));
        }

        [Fact]
        public void MoveRangeTo_MovesUp_AndToEnd()
        {
            var blocks = new List<EditorBlock> { P("a"), P("b"), P("c"), P("d") };
            Assert.Equal(0, BlockSelection.MoveRangeTo(blocks, 2, 4, 0, null));
            Assert.Equal("c d a b", Texts(blocks));

            Assert.Equal(2, BlockSelection.MoveRangeTo(blocks, 0, 2, 4, null));
            Assert.Equal("a b c d", Texts(blocks));
        }

        [Fact]
        public void MoveRangeTo_AlignsShallowestBlockToDropLevel()
        {
            // 목록 자식 x(1), y(1)를 최상위 문단 앞으로 → 둘 다 0
            var blocks = new List<EditorBlock> { B("p"), B("x", 1), B("y", 1), P("q") };
            Assert.Equal(1, BlockSelection.MoveRangeTo(blocks, 1, 3, 3, null));
            Assert.Equal("p x y q", Texts(blocks));

            // 최상위 블록을 목록 자식(1) 앞에 놓으면 그 수준으로
            blocks = new List<EditorBlock> { B("p"), B("c", 1), P("m"), P("n") };
            Assert.Equal(1, BlockSelection.MoveRangeTo(blocks, 2, 4, 1, null));
            Assert.Equal("p m:1 n:1 c:1", Texts(blocks));
        }

        [Fact]
        public void MoveRangeTo_IntoRangeOrInvalid_ReturnsMinusOneAndLeavesBlocks()
        {
            var blocks = new List<EditorBlock> { P("a"), P("b"), P("c"), P("d") };
            Assert.Equal(-1, BlockSelection.MoveRangeTo(blocks, 1, 3, 2, null));
            Assert.Equal(-1, BlockSelection.MoveRangeTo(blocks, 3, 2, 0, null));
            Assert.Equal(-1, BlockSelection.MoveRangeTo(blocks, 0, 1, 9, null));
            Assert.Equal("a b c d", Texts(blocks));
        }

        [Fact]
        public void MoveRangeTo_SetsDropColumnOnAllMovedBlocks()
        {
            var blocks = new List<EditorBlock> { P("a"), P("b"), P("c") };
            blocks[2].SetAttribute(EditorBlock.AttrColumn, "g/1");
            BlockSelection.MoveRangeTo(blocks, 0, 2, 2, "g/1");
            Assert.Equal(new[] { "g/1", "g/1", "g/1" }, blocks.Select(b => b.GetAttribute(EditorBlock.AttrColumn)).ToArray());

            BlockSelection.MoveRangeTo(blocks, 0, 2, 3, null);
            Assert.Null(blocks[1].GetAttribute(EditorBlock.AttrColumn));
            Assert.Null(blocks[2].GetAttribute(EditorBlock.AttrColumn));
        }
    }
}

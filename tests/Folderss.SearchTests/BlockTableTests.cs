using Folderss.Models;
using Folderss.Services;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>블록 편집기 표 조작(#75): 행·열 삽입·삭제·이동, 열 정렬·너비, 머리글 표시와 Markdown 왕복.</summary>
    public sealed class BlockTableTests
    {
        /// <summary>문단 + 3열 표(머리글 a|b|c, 1|2|3, 4|5|6) + 문단. 표는 [1, 4).</summary>
        private static List<EditorBlock> Doc()
        {
            return new List<EditorBlock>
            {
                new EditorBlock(BlockKind.Paragraph, "앞"),
                new EditorBlock(BlockKind.TableRow, "a\tb\tc") { TableAlign = "l,c,r" },
                new EditorBlock(BlockKind.TableRow, "1\t2\t3"),
                new EditorBlock(BlockKind.TableRow, "4\t5\t6"),
                new EditorBlock(BlockKind.Paragraph, "뒤"),
            };
        }

        private static string[] Texts(List<EditorBlock> blocks, int start = 1, int end = 4)
        {
            return blocks.Skip(start).Take(end - start).Select(b => b.Text.Replace('\t', '|')).ToArray();
        }

        [Fact]
        public void Range_FindsTableAroundAnyRow()
        {
            var blocks = Doc();
            int start, end;

            BlockTable.Range(blocks, 2, out start, out end);
            Assert.Equal(1, start);
            Assert.Equal(4, end);

            BlockTable.Range(blocks, 0, out start, out end);
            Assert.Equal(0, start);
            Assert.Equal(0, end);
            Assert.Equal(3, BlockTable.ColumnCount(blocks, 1, 4));
        }

        [Fact]
        public void InsertColumn_ShiftsCellsAlignAndWidths()
        {
            var blocks = Doc();
            blocks[1].SetAttribute(EditorBlock.AttrColWidths, "100,,200");

            BlockTable.InsertColumn(blocks, 1, 4, 1);

            Assert.Equal(new[] { "a||b|c", "1||2|3", "4||5|6" }, Texts(blocks));
            Assert.Equal("l,,c,r", blocks[1].TableAlign);
            Assert.Equal("100,,,200", blocks[1].GetAttribute(EditorBlock.AttrColWidths));
        }

        [Fact]
        public void InsertColumn_AtEnd_AndShortRowsArePadded()
        {
            var blocks = Doc();
            blocks[3].Text = "4";

            BlockTable.InsertColumn(blocks, 1, 4, 3);

            Assert.Equal(new[] { "a|b|c|", "1|2|3|", "4|||" }, Texts(blocks));
            Assert.Equal("l,c,r,", blocks[1].TableAlign);
            Assert.Null(blocks[1].GetAttribute(EditorBlock.AttrColWidths));
        }

        [Fact]
        public void DeleteColumn_RemovesCellsAlignAndWidths_ButKeepsLastColumn()
        {
            var blocks = Doc();
            blocks[1].SetAttribute(EditorBlock.AttrColWidths, ",150,");

            Assert.True(BlockTable.DeleteColumn(blocks, 1, 4, 1));
            Assert.Equal(new[] { "a|c", "1|3", "4|6" }, Texts(blocks));
            Assert.Equal("l,r", blocks[1].TableAlign);
            Assert.Null(blocks[1].GetAttribute(EditorBlock.AttrColWidths));   // 남은 열이 모두 자동이면 속성을 지움

            Assert.True(BlockTable.DeleteColumn(blocks, 1, 4, 0));
            Assert.False(BlockTable.DeleteColumn(blocks, 1, 4, 0));
            Assert.Equal(new[] { "c", "3", "6" }, Texts(blocks));
        }

        [Fact]
        public void MoveColumn_MovesCellsAlignAndWidthsTogether()
        {
            var blocks = Doc();
            blocks[1].SetAttribute(EditorBlock.AttrColWidths, "80,,");

            Assert.True(BlockTable.MoveColumn(blocks, 1, 4, 0, 2));

            Assert.Equal(new[] { "b|c|a", "2|3|1", "5|6|4" }, Texts(blocks));
            Assert.Equal("c,r,l", blocks[1].TableAlign);
            Assert.Equal(",,80", blocks[1].GetAttribute(EditorBlock.AttrColWidths));
            Assert.False(BlockTable.MoveColumn(blocks, 1, 4, 1, 1));
            Assert.False(BlockTable.MoveColumn(blocks, 1, 4, 0, 3));
        }

        [Fact]
        public void SetColumnAlign_And_Get()
        {
            var blocks = Doc();

            BlockTable.SetColumnAlign(blocks[1], 3, 0, BlockTable.AlignRight);
            BlockTable.SetColumnAlign(blocks[1], 3, 2, "잘못된 값");

            Assert.Equal("r,c,", blocks[1].TableAlign);
            Assert.Equal("r", BlockTable.GetColumnAlign(blocks[1], 0));
            Assert.Equal(string.Empty, BlockTable.GetColumnAlign(blocks[1], 2));
            Assert.Equal(string.Empty, BlockTable.GetColumnAlign(blocks[1], 7));
        }

        [Fact]
        public void ColumnWidths_ParseClampAndReset()
        {
            var header = new EditorBlock(BlockKind.TableRow, "a\tb\tc");

            BlockTable.SetColumnWidth(header, 3, 1, 151.6);
            BlockTable.SetColumnWidth(header, 3, 2, 10);   // 최솟값으로
            Assert.Equal(",152,40", header.GetAttribute(EditorBlock.AttrColWidths));
            Assert.Equal(new double[] { 0, 152, 40 }, BlockTable.ParseWidths(header.GetAttribute(EditorBlock.AttrColWidths), 3));
            Assert.Equal(new double[] { 0, 0 }, BlockTable.ParseWidths("x,-3", 2));

            BlockTable.SetColumnWidth(header, 3, 1, 0);
            BlockTable.SetColumnWidth(header, 3, 2, 0);
            Assert.Null(header.GetAttribute(EditorBlock.AttrColWidths));
        }

        [Fact]
        public void HeaderHidden_Toggles()
        {
            var header = new EditorBlock(BlockKind.TableRow, "a");
            Assert.False(BlockTable.IsHeaderHidden(header));

            BlockTable.SetHeaderHidden(header, true);
            Assert.True(BlockTable.IsHeaderHidden(header));
            Assert.Equal("true", header.GetAttribute(EditorBlock.AttrNoHeader));

            BlockTable.SetHeaderHidden(header, false);
            Assert.Null(header.Attributes);
        }

        [Fact]
        public void InsertRow_AboveHeader_MovesTableProperties()
        {
            var blocks = Doc();
            blocks[1].SetAttribute(EditorBlock.AttrColWidths, "90,,");
            blocks[1].SetAttribute(EditorBlock.AttrNoHeader, "true");
            blocks[1].SetAttribute("background", "yellow");   // 행 자체 속성은 그대로

            var created = new EditorBlock(BlockKind.TableRow, "\t\t");
            BlockTable.InsertRow(blocks, 1, 1, created);

            Assert.Same(created, blocks[1]);
            Assert.Equal("l,c,r", created.TableAlign);
            Assert.Equal("90,,", created.GetAttribute(EditorBlock.AttrColWidths));
            Assert.Equal("true", created.GetAttribute(EditorBlock.AttrNoHeader));
            Assert.Equal(string.Empty, blocks[2].TableAlign);
            Assert.Null(blocks[2].GetAttribute(EditorBlock.AttrColWidths));
            Assert.Equal("yellow", blocks[2].GetAttribute("background"));
        }

        [Fact]
        public void InsertRow_Below_KeepsHeader()
        {
            var blocks = Doc();

            BlockTable.InsertRow(blocks, 1, 3, new EditorBlock(BlockKind.TableRow, "x\ty\tz"));

            Assert.Equal(new[] { "a|b|c", "1|2|3", "x|y|z", "4|5|6" }, Texts(blocks, 1, 5));
            Assert.Equal("l,c,r", blocks[1].TableAlign);
        }

        [Fact]
        public void DeleteRow_Header_MovesTablePropertiesToNextRow()
        {
            var blocks = Doc();
            blocks[1].SetAttribute(EditorBlock.AttrColWidths, ",70,");

            BlockTable.DeleteRow(blocks, 1, 4, 1);

            Assert.Equal(new[] { "1|2|3", "4|5|6" }, Texts(blocks, 1, 3));
            Assert.Equal("l,c,r", blocks[1].TableAlign);
            Assert.Equal(",70,", blocks[1].GetAttribute(EditorBlock.AttrColWidths));
        }

        [Fact]
        public void MoveRow_WithinTable_KeepsPropertiesOnFirstRow()
        {
            var blocks = Doc();
            blocks[1].SetAttribute(EditorBlock.AttrNoHeader, "true");

            Assert.True(BlockTable.MoveRow(blocks, 1, 4, 3, 1));   // 마지막 행을 맨 위로

            Assert.Equal(new[] { "4|5|6", "a|b|c", "1|2|3" }, Texts(blocks));
            Assert.Equal("l,c,r", blocks[1].TableAlign);
            Assert.Equal("true", blocks[1].GetAttribute(EditorBlock.AttrNoHeader));
            Assert.Equal(string.Empty, blocks[2].TableAlign);
            Assert.Null(blocks[2].Attributes);

            Assert.True(BlockTable.MoveRow(blocks, 1, 4, 2, 3));   // 첫 행이 그대로면 속성도 그대로
            Assert.Equal(new[] { "4|5|6", "1|2|3", "a|b|c" }, Texts(blocks));
            Assert.Equal("l,c,r", blocks[1].TableAlign);

            Assert.False(BlockTable.MoveRow(blocks, 1, 4, 1, 4));   // 표 밖으로는 안 됨
            Assert.False(BlockTable.MoveRow(blocks, 1, 4, 2, 2));
        }

        [Fact]
        public void Markdown_RoundTrip_AfterColumnOpsAndAlignment()
        {
            var blocks = Doc();
            BlockTable.MoveColumn(blocks, 1, 4, 2, 0);          // c|a|b, 정렬 r,l,c
            BlockTable.InsertColumn(blocks, 1, 4, 3);           // 끝에 빈 열
            BlockTable.SetColumnAlign(blocks[1], 4, 3, BlockTable.AlignCenter);
            BlockTable.SetColumnWidth(blocks[1], 4, 0, 120);    // 사이드카 — Markdown에는 안 나옴
            BlockTable.SetHeaderHidden(blocks[1], true);

            var markdown = BlockMarkdown.Serialize(blocks);

            Assert.Contains("| c | a | b | |", markdown);
            Assert.Contains("| ---: | :--- | :---: | :---: |", markdown);
            Assert.DoesNotContain("120", markdown);
            Assert.DoesNotContain("noheader", markdown);

            var reread = BlockMarkdown.Parse(markdown);
            Assert.Equal(blocks.Select(b => b.Kind), reread.Select(b => b.Kind));
            Assert.Equal(Texts(blocks), Texts(reread));
            Assert.Equal("r,l,c,c", reread[1].TableAlign);
            Assert.Equal(markdown, BlockMarkdown.Serialize(reread));
        }
    }
}

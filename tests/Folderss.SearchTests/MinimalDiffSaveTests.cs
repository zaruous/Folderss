using Folderss.Models;
using Folderss.Services;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace Folderss.SearchTests
{
    /// <summary>블록 편집기 최소 변경 저장(#81 3단계, <see cref="MarkdownSource"/>): 무편집 저장은 바이트 동일, 한 블록 편집은 그 블록 줄만 바뀜.</summary>
    public sealed class MinimalDiffSaveTests
    {
        private readonly ITestOutputHelper _output;

        public MinimalDiffSaveTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static string Lf(params string[] lines)
        {
            return string.Join("\n", lines) + "\n";
        }

        /// <summary>남이 손으로 쓴, 정규형이 아닌 Markdown(목록 *·+, __강조__, Setext, 빈 줄 여러 개, 정렬 공백 표, 탭 들여쓰기, HTML 블록 등).</summary>
        private static readonly string Messy = Lf(
            "Title",
            "=====",
            "",
            "",
            "Some __bold__ and *italic* text,",
            "wrapped by hand.   ",
            "",
            "Sub title",
            "---------",
            "",
            "* star one",
            "* star two",
            "    * nested four spaces",
            "    * nested sibling",
            "* star three",
            "",
            "",
            "+ plus item",
            "",
            "3) third",
            "4) fourth",
            "",
            "1. one",
            "1. one again",
            "1. and again",
            "",
            "| Name   | Value |",
            "|:-------|------:|",
            "| alpha  |     1 |",
            "| beta   |    22 |",
            "",
            "-\ttab item",
            "\t-\ttab child",
            "",
            "<div align=\"center\">",
            "  <img src=\"logo.png\">",
            "</div>",
            "",
            "~~~python",
            "print('x')",
            "~~~",
            "",
            "> quote line",
            "continued lazily",
            "",
            "> [!WARNING]",
            "> careful",
            "",
            "<details>",
            "<summary>More</summary>",
            "",
            "Hidden *text*",
            "",
            "</details>",
            "",
            "***",
            "",
            "## Closing ##",
            "",
            "Last paragraph.");

        private static string Save(MarkdownSource source, IList<EditorBlock> blocks)
        {
            return source.Save(blocks).Text;
        }

        // ── 무편집 저장 ────────────────────────────────────────────────────

        public static IEnumerable<object[]> UneditedSamples()
        {
            yield return new object[] { Messy };
            yield return new object[] { Messy.Replace("\n", "\r\n") };
            yield return new object[] { "﻿" + Messy };
            yield return new object[] { Messy.TrimEnd('\n') };                       // 끝 줄바꿈 없음
            yield return new object[] { Messy + "\n\n\n" };                         // 끝 빈 줄 여러 개
            yield return new object[] { "\n\n" + Messy };                           // 앞 빈 줄
            yield return new object[] { "a\r\nb\n\nc\r\n" };                         // 줄 끝 섞임
            yield return new object[] { "---\r\ntitle: x\r\n# 주석\r\n---\r\n\r\n본문\r\n" };
            yield return new object[] { "<details><summary>한 줄</summary>본문</details>\n" };
            yield return new object[] { "<details>\n<summary>안 닫힘</summary>\n\n본문\n" };
            yield return new object[] { "<details>\n<summary>t</summary>\n\n본문</details>\n" };
            yield return new object[] { "```\n안 닫힌 코드\n\n\n" };
            yield return new object[] { "\n\n   \n" };
            yield return new object[] { string.Empty };
            yield return new object[] { "$$\nx^2\n$$\n\n$$ y $$\n\n[TOC]\n\n![a](b.png)\n\n[📎 파일](a.zip)\n" };
        }

        [Theory]
        [MemberData(nameof(UneditedSamples))]
        public void Unedited_SaveIsByteIdentical(string original)
        {
            var blocks = MarkdownSource.Parse(original, out var source);
            var result = source.Save(blocks);
            Assert.Equal(original, result.Text);
            Assert.Equal(0, result.Mode);
        }

        [Fact]
        public void Unedited_AttributesOnlyChange_KeepsMarkdownIdentical()
        {
            var blocks = MarkdownSource.Parse(Messy, out var source);
            blocks[3].SetAttribute("background", "yellow");   // 사이드카 속성은 Markdown에 영향 없음
            Assert.Equal(Messy, Save(source, blocks));
        }

        // ── 한 블록 편집 ───────────────────────────────────────────────────

        /// <summary>원문과 결과의 줄 차이(앞뒤 공통 줄을 뺀 가운데)를 원문 줄 범위·새 줄 목록으로.</summary>
        private static (int start, string[] removed, string[] added) Diff(string before, string after)
        {
            var a = before.Split('\n');
            var b = after.Split('\n');
            var head = 0;
            while (head < a.Length && head < b.Length && a[head] == b[head])
                head++;
            var tail = 0;
            while (tail < a.Length - head && tail < b.Length - head && a[a.Length - 1 - tail] == b[b.Length - 1 - tail])
                tail++;
            return (head, a.Skip(head).Take(a.Length - head - tail).ToArray(), b.Skip(head).Take(b.Length - head - tail).ToArray());
        }

        private static int IndexOf(List<EditorBlock> blocks, string text)
        {
            var index = blocks.FindIndex(b => b.Text == text);
            Assert.True(index >= 0, "블록 없음: " + text);
            return index;
        }

        [Fact]
        public void EditParagraph_OnlyThatParagraphChanges()
        {
            var blocks = MarkdownSource.Parse(Messy, out var source);
            var p = blocks.First(b => b.Kind == BlockKind.Paragraph && b.Text.StartsWith("Some"));
            p.Text = "Some __bold__ text, edited.";
            var saved = Save(source, blocks);
            var d = Diff(Messy, saved);
            Assert.Equal(new[] { "Some __bold__ and *italic* text,", "wrapped by hand.   " }, d.removed);
            Assert.Equal(new[] { "Some __bold__ text, edited." }, d.added);
        }

        [Fact]
        public void EditStarListItem_KeepsMarkerAndNeighbours()
        {
            var blocks = MarkdownSource.Parse(Messy, out var source);
            blocks[IndexOf(blocks, "star two")].Text = "star 2";
            var d = Diff(Messy, Save(source, blocks));
            Assert.Equal(new[] { "* star two" }, d.removed);
            Assert.Equal(new[] { "* star 2" }, d.added);
        }

        [Fact]
        public void EditNestedFourSpaceItem_KeepsIndentation()
        {
            var blocks = MarkdownSource.Parse(Messy, out var source);
            blocks[IndexOf(blocks, "nested sibling")].Text = "nested sibling!";
            var saved = Save(source, blocks);
            var d = Diff(Messy, saved);
            Assert.Equal(new[] { "    * nested sibling" }, d.removed);
            Assert.Equal(new[] { "    * nested sibling!" }, d.added);
            Assert.True(MarkdownSource.SameStructure(BlockMarkdown.Parse(saved), blocks));
        }

        [Fact]
        public void InsertListItem_FollowsSiblingStyle()
        {
            var blocks = MarkdownSource.Parse(Messy, out var source);
            var at = IndexOf(blocks, "nested four spaces");
            blocks.Insert(at + 1, new EditorBlock(BlockKind.Bulleted, "inserted", blocks[at].Indent));
            var saved = Save(source, blocks);
            var d = Diff(Messy, saved);
            Assert.Empty(d.removed);
            Assert.Equal(new[] { "    * inserted" }, d.added);
            Assert.True(MarkdownSource.SameStructure(BlockMarkdown.Parse(saved), blocks));
        }

        [Fact]
        public void DeleteBlock_NeighboursUntouched()
        {
            var blocks = MarkdownSource.Parse(Messy, out var source);
            blocks.RemoveAt(IndexOf(blocks, "plus item"));
            var saved = Save(source, blocks);
            var d = Diff(Messy, saved);
            Assert.Contains("+ plus item", d.removed);
            Assert.True(d.removed.All(l => l.Length == 0 || l == "+ plus item"), string.Join("|", d.removed));
            Assert.True(d.added.All(l => l.Length == 0), string.Join("|", d.added));
            Assert.True(MarkdownSource.SameStructure(BlockMarkdown.Parse(saved), blocks));
        }

        [Fact]
        public void EditTableCell_OnlyThatRowChanges()
        {
            var blocks = MarkdownSource.Parse(Messy, out var source);
            var row = blocks.First(b => b.Kind == BlockKind.TableRow && b.Text.StartsWith("beta"));
            row.Text = BlockMarkdown.JoinCells(new[] { "beta", "23" });
            var d = Diff(Messy, Save(source, blocks));
            Assert.Equal(new[] { "| beta   |    22 |" }, d.removed);
            Assert.Equal(new[] { "| beta | 23 |" }, d.added);
        }

        [Fact]
        public void EditOrderedItems_KeepStartNumberDelimiterAndOtherNumbers()
        {
            var original = Lf("3) third", "4) fourth", "", "text", "", "1. one", "1. one again", "1. and again");
            var blocks = MarkdownSource.Parse(original, out var source);
            blocks[IndexOf(blocks, "third")].Text = "THIRD";
            blocks[IndexOf(blocks, "and again")].Text = "AND AGAIN";
            var saved = Save(source, blocks);
            Assert.Equal(Lf("3) THIRD", "4) fourth", "", "text", "", "1. one", "1. one again", "3. AND AGAIN"), saved);
        }

        [Fact]
        public void InsertIntoOneOneOneList_DoesNotRenumberOthers()
        {
            var original = Lf("intro", "", "1. one", "1. one again", "1. and again", "", "outro");
            var blocks = MarkdownSource.Parse(original, out var source);
            var at = IndexOf(blocks, "one");
            blocks.Insert(at + 1, new EditorBlock(BlockKind.Numbered, "new", blocks[at].Indent));
            var d = Diff(original, Save(source, blocks));
            Assert.Empty(d.removed);
            Assert.Equal(new[] { "2. new" }, d.added);
        }

        [Fact]
        public void EditSetextHeading_StaysSetext()
        {
            var blocks = MarkdownSource.Parse(Messy, out var source);
            blocks[IndexOf(blocks, "Sub title")].Text = "Sub title 2";
            var d = Diff(Messy, Save(source, blocks));
            Assert.Equal(new[] { "Sub title" }, d.removed);
            Assert.Equal(new[] { "Sub title 2" }, d.added);
        }

        [Fact]
        public void EditToggleChild_OnlyChildLineChanges()
        {
            var blocks = MarkdownSource.Parse(Messy, out var source);
            blocks[IndexOf(blocks, "Hidden *text*")].Text = "Hidden text";
            var d = Diff(Messy, Save(source, blocks));
            Assert.Equal(new[] { "Hidden *text*" }, d.removed);
            Assert.Equal(new[] { "Hidden text" }, d.added);
        }

        [Fact]
        public void CollapseToggle_OnlyOpeningLinesChange()
        {
            var md = Lf("<details open>", "<summary>T</summary>", "", "body", "", "</details>", "", "after");
            var blocks = MarkdownSource.Parse(md, out var source);
            blocks[0].Collapsed = true;
            var d = Diff(md, Save(source, blocks));
            Assert.Equal(new[] { "<details open>" }, d.removed);
            Assert.Equal(new[] { "<details>" }, d.added);
        }

        [Fact]
        public void EditHtmlBlockAndCode_OnlyTheirLines()
        {
            var blocks = MarkdownSource.Parse(Messy, out var source);
            var code = blocks.First(b => b.Kind == BlockKind.Code);
            code.Text = "print('y')";
            var d = Diff(Messy, Save(source, blocks));
            Assert.Equal(new[] { "~~~python", "print('x')", "~~~" }, d.removed);
            Assert.Equal(new[] { "```python", "print('y')", "```" }, d.added);
        }

        [Fact]
        public void Crlf_EditedBlockUsesCrlf_RestByteIdentical()
        {
            var original = Messy.Replace("\n", "\r\n");
            var blocks = MarkdownSource.Parse(original, out var source);
            blocks[IndexOf(blocks, "star two")].Text = "star 2";
            var saved = Save(source, blocks);
            Assert.Equal(original.Replace("* star two\r\n", "* star 2\r\n"), saved);
            Assert.DoesNotContain("\n", saved.Replace("\r\n", string.Empty));
        }

        [Fact]
        public void NoFinalNewline_KeptWhenLastBlockEdited()
        {
            var original = Messy.TrimEnd('\n');
            var blocks = MarkdownSource.Parse(original, out var source);
            blocks[blocks.Count - 1].Text = "Last paragraph!";
            Assert.Equal(original.Replace("Last paragraph.", "Last paragraph!"), Save(source, blocks));
        }

        [Fact]
        public void AppendAfterLastLineWithoutNewline()
        {
            var blocks = MarkdownSource.Parse("a\n\nb", out var source);
            blocks.Add(new EditorBlock(BlockKind.Paragraph, "c"));
            Assert.Equal("a\n\nb\n\nc", Save(source, blocks));
        }

        [Fact]
        public void FrontMatterEdit_OnlyThatLine_CrlfKept()
        {
            var original = "---\r\ntitle: x\r\n# 주석\r\ndraft: true\r\n---\r\n\r\n* 본문\r\n";
            var blocks = MarkdownSource.Parse(original, out var source);
            blocks[0].Text = blocks[0].Text.Replace("draft: true", "draft: false");
            Assert.Equal(original.Replace("draft: true", "draft: false"), Save(source, blocks));
        }

        [Fact]
        public void RevertEdit_IsByteIdenticalAgain()
        {
            var blocks = MarkdownSource.Parse(Messy, out var source);
            var snapshot = blocks.Select(b => b.Clone()).ToList();   // 실행 취소 스냅샷과 같은 방식(같은 Id)
            blocks[IndexOf(blocks, "star two")].Text = "changed";
            blocks.RemoveAt(IndexOf(blocks, "plus item"));
            Assert.NotEqual(Messy, Save(source, blocks));
            var result = source.Save(snapshot);
            Assert.Equal(Messy, result.Text);
            Assert.Equal(0, result.Mode);
        }

        [Fact]
        public void KindChangeThatWouldMerge_FallsBackSafely()
        {
            // 목록 가운데 항목을 문단으로 바꾸면 원문 구분자(빈 줄 없음)로는 앞 항목에 붙어 버린다 → 안전장치가 묶음을 다시 쓴다.
            var original = Lf("* a", "* b", "* c", "", "end");
            var blocks = MarkdownSource.Parse(original, out var source);
            blocks[1].Kind = BlockKind.Paragraph;
            var result = source.Save(blocks);
            Assert.NotEqual(1, result.Mode);
            Assert.True(MarkdownSource.SameStructure(BlockMarkdown.Parse(result.Text), blocks), result.Text);
            Assert.EndsWith("\nend\n", result.Text);
        }

        [Fact]
        public void OutdentNestedItem_StructureMatches()
        {
            var blocks = MarkdownSource.Parse(Messy, out var source);
            var i = IndexOf(blocks, "nested sibling");
            blocks[i].Indent--;
            var saved = Save(source, blocks);
            Assert.True(MarkdownSource.SameStructure(BlockMarkdown.Parse(saved), blocks), saved);
        }

        [Fact]
        public void MovedBlock_KeepsOriginalTextAndStructure()
        {
            var blocks = MarkdownSource.Parse(Messy, out var source);
            var last = blocks[blocks.Count - 1];
            blocks.RemoveAt(blocks.Count - 1);
            blocks.Insert(0, last);
            var saved = Save(source, blocks);
            Assert.StartsWith("Last paragraph.\n\nTitle\n=====\n", saved);
            Assert.True(MarkdownSource.SameStructure(BlockMarkdown.Parse(saved), blocks), saved);
        }

        [Fact]
        public void NewDocument_IsCanonical()
        {
            var source = MarkdownSource.Empty();
            var blocks = new List<EditorBlock>
            {
                new EditorBlock(BlockKind.Heading1, "제목"),
                new EditorBlock(BlockKind.Bulleted, "a"),
                new EditorBlock(BlockKind.Numbered, "b"),
            };
            Assert.Equal(BlockMarkdown.Serialize(blocks), Save(source, blocks));
        }

        // ── 무작위 편집에도 구조가 맞는지 ───────────────────────────────────

        [Fact]
        public void RandomEdits_AlwaysPreserveStructure()
        {
            var random = new System.Random(81);
            var kinds = new[] { BlockKind.Paragraph, BlockKind.Bulleted, BlockKind.Numbered, BlockKind.Todo, BlockKind.Heading2, BlockKind.Quote };
            var modes = new int[4];
            for (var round = 0; round < 300; round++)
            {
                var blocks = MarkdownSource.Parse(Messy, out var source);
                var edits = 1 + random.Next(3);
                for (var e = 0; e < edits; e++)
                {
                    var i = random.Next(blocks.Count);
                    switch (random.Next(5))
                    {
                        case 0: blocks[i].Text = blocks[i].Kind == BlockKind.TableRow ? BlockMarkdown.JoinCells(new[] { "x", "y" }) : "edit " + round; break;
                        case 1: if (blocks[i].Kind != BlockKind.TableRow && blocks[i].Kind != BlockKind.Toggle) blocks[i].Kind = kinds[random.Next(kinds.Length)]; break;
                        case 2: blocks.RemoveAt(i); break;
                        case 3: blocks.Insert(i, new EditorBlock(kinds[random.Next(kinds.Length)], "new " + round, blocks[i].Indent)); break;
                        case 4: if (i > 0 && blocks[i].Indent > 0) blocks[i].Indent--; break;
                    }
                    if (blocks.Count == 0)
                        break;
                }
                BlockOperations.NormalizeIndents(blocks);
                var result = source.Save(blocks);
                modes[result.Mode]++;
                var saved = result.Text;
                var reread = BlockMarkdown.Parse(saved);
                var canonical = BlockMarkdown.Parse(BlockMarkdown.Serialize(blocks));
                Assert.True(MarkdownSource.SameStructure(reread, blocks) || MarkdownSource.SameStructure(reread, canonical),
                    "round " + round + "\n" + saved);
            }
            _output.WriteLine("mode 0/1/2/3 = {0}/{1}/{2}/{3}", modes[0], modes[1], modes[2], modes[3]);
        }

        // ── 구간 검증·내용 비교 건너뛰기(저장 성능 최적화) ─────────────────────

        /// <summary>저장 글을 다시 읽은 구조가 저장하려는 블록(또는 정규형을 다시 읽은 것)과 같아야 한다 — 바뀐 구간만 검증해도 문서 전체 검증과 같은 결론이어야 함.</summary>
        private static void AssertReadsBack(MarkdownSaveResult result, IList<EditorBlock> blocks, string context)
        {
            var reread = BlockMarkdown.Parse(result.Text);
            Assert.True(MarkdownSource.SameStructure(reread, blocks)
                        || MarkdownSource.SameStructure(reread, BlockMarkdown.Parse(BlockMarkdown.Serialize(blocks))),
                context + " mode " + result.Mode + "\n" + result.Text);
        }

        [Fact]
        public void MovedBomFirstPiece_IsVerifiedLikeWholeDocument()
        {
            // 원문 첫 조각(BOM 포함)이 뒤로 밀리면 BOM이 문서 중간에 남는다 — 그 구간만 따로 읽으면 BOM이 떨어져 같아 보이므로 전체 검증으로 판단해야 한다.
            var blocks = MarkdownSource.Parse("﻿Title\n=====\n\npara\n", out var source);
            blocks.Insert(0, new EditorBlock(BlockKind.Heading2, "new"));
            var result = source.Save(blocks);
            AssertReadsBack(result, blocks, "bom");
            Assert.NotEqual(1, result.Mode);
        }

        [Fact]
        public void DividerAtStart_LaterDashLineWouldCloseFrontMatter_IsVerifiedLikeWholeDocument()
        {
            // 첫 줄 --- 는 뒤쪽 --- (구분선·Setext 밑줄)까지 front matter로 읽힐 수 있다.
            var blocks = MarkdownSource.Parse("A\n===\n\nB\n---\n\nC\n", out var source);
            blocks.Insert(0, new EditorBlock(BlockKind.Divider, string.Empty));
            var result = source.Save(blocks);
            AssertReadsBack(result, blocks, "divider");
        }

        [Fact]
        public void UnclosedFenceAtEnd_BlockAppendedAfterIt_IsVerified()
        {
            var blocks = MarkdownSource.Parse("para\n\n```py\nunclosed\n\nstill code\n", out var source);
            blocks.Add(new EditorBlock(BlockKind.Paragraph, "after"));
            var result = source.Save(blocks);
            AssertReadsBack(result, blocks, "fence");
            Assert.EndsWith("after\n", result.Text);
        }

        [Fact]
        public void DeletedUntrackedOneLineToggle_DoesNotComeBack()
        {
            // 줄을 나눌 수 없는 토글은 원문 자리가 기록되지 않아 이웃 조각의 원문 구분자 안에 들어 있다 — 지우면 그 자리를 원문 그대로 둘 수 없다.
            var blocks = MarkdownSource.Parse("a\n\n<details><summary>t</summary>body</details>\n\nb\n", out var source);
            blocks.RemoveAll(x => x.Kind == BlockKind.Toggle || x.Text == "body");
            blocks[0].Text = "a2";
            var result = source.Save(blocks);
            AssertReadsBack(result, blocks, "toggle");
            Assert.DoesNotContain("details", result.Text);
        }

        [Fact]
        public void ChangedBlockWithSameCanonicalText_KeepsOriginalBytes()
        {
            // 내용이 바뀌었어도 정규형 글이 같으면 원문 그대로(읽을 때 글을 읽을 때 내용·문맥으로 다시 만들어 비교).
            const string original = "#    Title   \n\n```  py  \ncode\n```\n";
            var blocks = MarkdownSource.Parse(original, out var source);
            blocks[0].Kind = BlockKind.Paragraph;
            blocks[0].Text = "# Title";
            blocks[1].Language = " py ";
            var result = source.Save(blocks);
            Assert.Equal(0, result.Mode);
            Assert.Same(original, result.Text);
        }

        [Fact]
        public void RandomStructuralEdits_SavedTextAlwaysReadsBack()
        {
            var docs = new[]
            {
                Messy, Messy.Replace("\n", "\r\n"), "﻿" + Messy.TrimEnd('\n'),
                "---\ntitle: x\n---\n\n# H\n\npara\n\n---\n\nafter\n\n```\nfence\n\n\ninner\n```\n\ntail\n",
                "---\n\npara\n\n---\n\nmore\n",
                "para\n\n- a\n- b\n\n```py\nunclosed\n\nstill code\n",
                "a\n\n<details>\n<summary>s</summary>\n\nchild\n\n- li\n",
                Lf("<details>", "<summary>A</summary>", "", "a1", "", "</details>", "", "top", "", "  <details>", "  <summary>C</summary>", "", "  c1", "", "  </details>", "", "end"),
                Lf("- a", "", "- b", "", "  b child", "", "para", "lazy", "", "| a | b |", "|---|---|", "| 1 | 2 |", "", "| c | d |", "|---|---|", "| 3 | 4 |", "", "    indented code", "", "tail"),
                "a\rb\r\rc\r\n\r\n- x\r- y\r\rend",
            };
            var tricky = new[] { "```", "<details>", "</details>", "---", "# x", "| a | b |", "|---|---|", "    code", "- item", "$$", "1. n", "> q", "===", "<div>", "a\nb", "x\n\ny", "" };
            var kinds = new[] { BlockKind.Paragraph, BlockKind.Bulleted, BlockKind.Numbered, BlockKind.Todo, BlockKind.Heading2, BlockKind.Quote, BlockKind.Code, BlockKind.Toggle };
            var random = new System.Random(8181);
            var modes = new int[4];
            foreach (var doc in docs)
            {
                for (var session = 0; session < 40; session++)
                {
                    var blocks = MarkdownSource.Parse(doc, out var source);
                    var history = new List<List<EditorBlock>>();
                    for (var step = 0; step < 8; step++)
                    {
                        for (var e = random.Next(4); e > 0 && blocks.Count > 0; e--)
                        {
                            var i = random.Next(blocks.Count);
                            var b = blocks[i];
                            switch (random.Next(10))
                            {
                                case 0: b.Text = b.Kind == BlockKind.TableRow ? BlockMarkdown.JoinCells(new[] { "x", "y", "z" }.Take(1 + random.Next(3))) : b.Text + " e"; break;
                                case 1: if (b.Kind != BlockKind.TableRow && b.Kind != BlockKind.Toggle && b.Kind != BlockKind.FrontMatter) b.Kind = kinds[random.Next(kinds.Length)]; break;
                                case 2: blocks.RemoveAt(i); break;
                                case 3: blocks.Insert(i, new EditorBlock(kinds[random.Next(kinds.Length)], "new " + step, b.Indent)); break;
                                case 4: if (b.Indent > 0) b.Indent--; else if (i > 0) b.Indent++; break;
                                case 5: b.Collapsed = !b.Collapsed; b.Checked = !b.Checked; break;
                                case 6: if (i + 1 < blocks.Count) { blocks.RemoveAt(i); blocks.Insert(random.Next(blocks.Count + 1), b); } break;
                                case 7: if (history.Count > 0) { var h = history[random.Next(history.Count)]; blocks.Clear(); blocks.AddRange(h.Select(x => x.Clone())); } break;
                                default: if (b.Kind != BlockKind.FrontMatter) b.Text = tricky[random.Next(tricky.Length)]; break;
                            }
                        }
                        BlockOperations.NormalizeIndents(blocks);
                        var result = source.Save(blocks);
                        modes[result.Mode]++;
                        if (result.Mode == 0)
                            Assert.Same(source.Text, result.Text);
                        AssertReadsBack(result, blocks, "session " + session + " step " + step);
                        history.Add(blocks.Select(x => x.Clone()).ToList());
                    }
                }
            }
            _output.WriteLine("mode 0/1/2/3 = {0}/{1}/{2}/{3}", modes[0], modes[1], modes[2], modes[3]);
        }

        // ── 사이드카(#78)·줄 번호 ───────────────────────────────────────────

        [Fact]
        public void Sidecar_AttributesStillReattachAfterMinimalSave()
        {
            var blocks = MarkdownSource.Parse(Messy, out var source);
            var target = IndexOf(blocks, "star three");
            blocks[target].SetAttribute("background", "yellow");
            blocks[IndexOf(blocks, "star two")].Text = "star 2";
            var saved = Save(source, blocks);
            var sidecar = BlockSidecar.Serialize(blocks, saved, "a.md");
            Assert.NotNull(sidecar);

            var reread = MarkdownSource.Parse(saved, out _);
            Assert.Equal(0, BlockSidecar.Apply(reread, sidecar));
            Assert.Equal("yellow", reread[IndexOf(reread, "star three")].GetAttribute("background"));
        }

        [Fact]
        public void BlockLines_MatchSavedText()
        {
            var blocks = MarkdownSource.Parse(Messy, out var source);
            blocks[IndexOf(blocks, "star two")].Text = "star 2";
            blocks.Insert(0, new EditorBlock(BlockKind.Paragraph, "front"));
            var result = source.Save(blocks);
            var lines = result.Text.Split('\n');
            Assert.Equal("front", lines[result.BlockLines[0] - 1]);
            Assert.Equal("Title", lines[result.BlockLines[1] - 1]);
            Assert.Equal("* star 2", lines[result.BlockLines[IndexOf(blocks, "star 2")] - 1]);
            Assert.Equal("| beta   |    22 |", lines[result.BlockLines[blocks.FindIndex(b => b.Text.StartsWith("beta"))] - 1]);
            Assert.Equal("Last paragraph.", lines[result.BlockLines[blocks.Count - 1] - 1]);
            // 줄 번호 → 블록(미리보기 원문 편집 → 블록 편집 전환)
            var at = IndexOf(blocks, "nested sibling");
            Assert.Equal(at, MarkdownViewSwitch.BlockAtLine(result.BlockLines, result.BlockLines[at]));
        }

        // ── 성능(1만 줄) ───────────────────────────────────────────────────

        [Fact]
        public void LargeDocument_Performance()
        {
            var sb = new StringBuilder();
            var lineCountBuilt = 0;
            for (var s = 0; lineCountBuilt < 10000; s++)
            {
                sb.Append("Section ").Append(s).Append("\n=========\n\n");
                sb.Append("Paragraph with __bold__ text number ").Append(s).Append(",\nwrapped line.\n\n\n");
                sb.Append("* item a\n* item b\n    * nested\n\n");
                sb.Append("| h1 | h2 |\n|:--|--:|\n| 1  | 2  |\n\n");
                sb.Append("```\ncode ").Append(s).Append("\n```\n\n");
                lineCountBuilt += 19;
            }
            var original = sb.ToString().Replace("\n", "\r\n");
            var lineCount = original.Split('\n').Length;

            MarkdownSource.Parse(original, out _);   // 데우기
            var memoryBefore = System.GC.GetTotalMemory(true);
            var plain = BlockMarkdown.Parse(original);
            var memoryPlain = System.GC.GetTotalMemory(true) - memoryBefore;
            System.GC.KeepAlive(plain);
            plain = null;
            memoryBefore = System.GC.GetTotalMemory(true);
            var watch = Stopwatch.StartNew();
            var blocks = MarkdownSource.Parse(original, out var source);
            var parseCapture = watch.Elapsed.TotalMilliseconds;
            var memoryWithSource = System.GC.GetTotalMemory(true) - memoryBefore;
            watch.Restart();
            BlockMarkdown.Parse(original);
            var parseOnly = watch.Elapsed.TotalMilliseconds;

            // 데우기 후 측정
            source.Save(blocks);
            watch.Restart();
            var unchanged = source.Save(blocks);
            var saveUnchanged = watch.Elapsed.TotalMilliseconds;
            Assert.Same(original, unchanged.Text);

            blocks[blocks.Count / 2].Text = "edited in the middle";
            watch.Restart();
            var edited = source.Save(blocks);
            var saveEdited = watch.Elapsed.TotalMilliseconds;
            Assert.Equal(1, edited.Mode);
            Assert.Equal(1, edited.RewrittenPieces);

            watch.Restart();
            var canonical = BlockMarkdown.Serialize(blocks);
            var serialize = watch.Elapsed.TotalMilliseconds;

            _output.WriteLine("lines {0}, blocks {1}, chars {2}", lineCount, blocks.Count, original.Length);
            _output.WriteLine("memory: blocks only {0:N0} B, blocks + source map {1:N0} B", memoryPlain, memoryWithSource);
            _output.WriteLine("parse only {0:F1}ms, parse+capture {1:F1}ms", parseOnly, parseCapture);
            _output.WriteLine("save unchanged {0:F1}ms, save one edit {1:F1}ms (canonical Serialize {2:F1}ms)", saveUnchanged, saveEdited, serialize);
            Assert.True(saveEdited < 2000, "저장이 너무 느림: " + saveEdited);

            // 같은 출처로 계속 저장(편집기처럼): 다른 문단 하나 더 고치기
            blocks[blocks.FindIndex(blocks.Count / 3, b => b.Kind == BlockKind.Paragraph)].Text = "edited again";
            watch.Restart();
            var again = source.Save(blocks);
            var saveAgain = watch.Elapsed.TotalMilliseconds;
            Assert.Equal(1, again.Mode);
            Assert.Equal(2, again.RewrittenPieces);
            _output.WriteLine("save second edit {0:F1}ms", saveAgain);
        }
    }
}

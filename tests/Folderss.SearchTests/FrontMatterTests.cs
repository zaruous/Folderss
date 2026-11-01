using Folderss.Models;
using Folderss.Services;
using System.Linq;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>블록 편집기 front matter 속성 표(#81): 해석, Markdown 왕복 불변, 속성 줄 단위 편집.</summary>
    public sealed class FrontMatterTests
    {
        private static string L(params string[] lines)
        {
            return string.Join("\n", lines);
        }

        private const string Sample =
            "---\n" +
            "# 문서 정보\n" +
            "title: 블록 편집기\n" +
            "subtitle: \"따옴표: 값\"   # 부제\n" +
            "author: 'O''Brien'\n" +
            "draft: true\n" +
            "empty:\n" +
            "\n" +
            "tags: [a, \"b, c\", d]\n" +
            "aliases:\n" +
            "  - one\n" +
            "  - \"two\"\n" +
            "---";

        // ── 해석 ───────────────────────────────────────────────────────────

        [Fact]
        public void Parse_ScalarsAndLists()
        {
            var doc = FrontMatter.Parse(Sample);

            Assert.True(doc.IsTable);
            Assert.Equal(new[] { "title", "subtitle", "author", "draft", "empty", "tags", "aliases" }, doc.Entries.Select(e => e.Key));
            Assert.Equal("블록 편집기", doc.Entries[0].Value);
            Assert.Equal("따옴표: 값", doc.Entries[1].Value);
            Assert.Equal("O'Brien", doc.Entries[2].Value);
            Assert.Equal("true", doc.Entries[3].Value);
            Assert.Equal(FrontMatterValueKind.Scalar, doc.Entries[4].Kind);
            Assert.Equal(string.Empty, doc.Entries[4].Value);
            Assert.Equal(FrontMatterValueKind.List, doc.Entries[5].Kind);
            Assert.Equal(new[] { "a", "b, c", "d" }, doc.Entries[5].Items);
            Assert.Equal(FrontMatterValueKind.List, doc.Entries[6].Kind);
            Assert.Equal(new[] { "one", "two" }, doc.Entries[6].Items);
            Assert.Equal(9, doc.Entries[6].FirstLine);
            Assert.Equal(3, doc.Entries[6].LineCount);
        }

        [Fact]
        public void Parse_BlockListAtKeyIndent()
        {
            var doc = FrontMatter.Parse(L("---", "tags:", "- x", "- y", "---"));

            Assert.True(doc.IsTable);
            Assert.Equal(FrontMatterValueKind.List, doc.Entries[0].Kind);
            Assert.Equal(new[] { "x", "y" }, doc.Entries[0].Items);
        }

        [Theory]
        [InlineData("author:\n  name: Kim\n  email: k@x.y")]          // 중첩 맵
        [InlineData("summary: |\n  첫 줄\n  둘째 줄")]                  // 블록 스칼라
        [InlineData("summary: >-\n  접힌\n  글")]
        [InlineData("items:\n  - name: a\n  - name: b")]                // 맵의 목록
        [InlineData("base: &anchor value")]                             // 앵커
        [InlineData("copy: *anchor")]                                   // 별칭
        [InlineData("typed: !!str 123")]                                // 태그
        [InlineData("map: {a: 1, b: 2}")]                               // 흐름 맵
        [InlineData("nested: [a, [b, c]]")]                             // 중첩 흐름 목록
        [InlineData("text: \"닫히지 않은\n  따옴표\"")]                  // 여러 줄 따옴표
        [InlineData("esc: \"\\u0041\"")]                                // 다시 쓰면 바뀌는 이스케이프
        [InlineData("list:\n  - a\n\n  - b")]                          // 사이 빈 줄
        public void Parse_ComplexValue_IsShownAsRaw(string body)
        {
            var doc = FrontMatter.Parse("---\nfirst: 1\n" + body + "\nlast: 2\n---");

            Assert.True(doc.IsTable);
            Assert.Equal(3, doc.Entries.Count);
            Assert.Equal("first", doc.Entries[0].Key);
            Assert.Equal(FrontMatterValueKind.Complex, doc.Entries[1].Kind);
            Assert.Equal("last", doc.Entries[2].Key);
            Assert.Equal("2", doc.Entries[2].Value);
            // 원문 표시는 키 뒤 글자 그대로
            var afterKey = body.Substring(body.IndexOf(':') + 1);
            afterKey = afterKey.StartsWith("\n") ? afterKey.Substring(1) : afterKey.TrimStart(' ');
            Assert.Equal(afterKey, doc.Entries[1].RawValue);
            // 편집하지 않는다
            Assert.Null(FrontMatter.SetValue("---\nfirst: 1\n" + body + "\nlast: 2\n---", 1, doc.Entries[1].Key, "x"));
        }

        [Theory]
        [InlineData("---\n- a\n- b\n---")]          // 최상위가 목록
        [InlineData("---\n  indented: 1\n---")]     // 들여쓴 최상위
        [InlineData("---\njust text\n---")]         // 키 없는 줄
        [InlineData("---\nkey:value\n---")]         // 콜론 뒤 공백 없음(맵이 아님)
        public void Parse_NotAMapping_FallsBackToSource(string text)
        {
            var doc = FrontMatter.Parse(text);

            Assert.False(doc.IsTable);
            Assert.Empty(doc.Entries);
            Assert.Equal(text.Substring(4, text.Length - 8), doc.Body);
            Assert.Null(FrontMatter.AddEntry(text, "new"));
        }

        [Theory]
        [InlineData("---\n---", true)]
        [InlineData("---\na: 1\n...", true)]
        [InlineData("---  \na: 1\n---", true)]
        [InlineData("---\na: 1", false)]
        [InlineData("a: 1\n---", false)]
        [InlineData("---\na: 1\n---\n", false)]
        public void LooksLikeFrontMatter(string text, bool expected)
        {
            Assert.Equal(expected, FrontMatter.LooksLikeFrontMatter(text));
        }

        // ── Markdown 왕복 불변 ─────────────────────────────────────────────

        [Theory]
        [InlineData(Sample)]
        [InlineData("---\n---")]
        [InlineData("---\ntitle: x\n...")]
        [InlineData("---\ntitle:   공백 많은 값   \n\n\n# 주석만\ndate: 2026-11-01T10:00:00+09:00\n---")]
        [InlineData("---\nauthor:\n  name: Kim\nsummary: |\n  첫 줄\n\n  빈 줄 뒤\n---")]
        [InlineData("---\n- 최상위 목록\n- 두 번째\n---")]
        [InlineData("---\n\t탭: 들여쓰기\n키:\t값\n---")]
        public void FrontMatter_RoundTripsByteForByte(string frontMatter)
        {
            var markdown = frontMatter + "\n\n# 제목\n\n본문\n";

            var blocks = BlockMarkdown.Parse(markdown);

            Assert.Equal(BlockKind.FrontMatter, blocks[0].Kind);
            Assert.Equal(frontMatter, blocks[0].Text);
            Assert.Equal(markdown, BlockMarkdown.Serialize(blocks));
        }

        [Fact]
        public void FrontMatter_WithCrLf_KeepsLinesExactly()
        {
            var markdown = "---\r\ntitle: \"x\"  # c\r\ntags: [a,b]\r\n---\r\n\r\n본문\r\n";

            var blocks = BlockMarkdown.Parse(markdown);

            // 뷰어가 읽은 줄바꿈(CRLF)으로 되돌려 저장한다.
            Assert.Equal(markdown, BlockMarkdown.Serialize(blocks).Replace("\n", "\r\n"));
        }

        [Fact]
        public void FrontMatter_OnlyAtDocumentStart()
        {
            var blocks = BlockMarkdown.Parse("본문\n\n---\ntitle: x\n---\n");

            Assert.DoesNotContain(blocks, b => b.Kind == BlockKind.FrontMatter);
        }

        [Fact]
        public void SidecarHash_TreatsFrontMatterAsRaw()
        {
            // 예전에는 front matter를 Raw로 읽었다 — 그때 저장한 사이드카가 그대로 맞아야 한다.
            var text = "---\ntitle: x\n---";

            Assert.Equal(BlockSidecar.Hash(new EditorBlock(BlockKind.Raw, text)), BlockSidecar.Hash(new EditorBlock(BlockKind.FrontMatter, text)));
        }

        // ── 편집: 바꾼 속성의 줄만 ─────────────────────────────────────────

        private static void AssertOnlyLinesChanged(string before, string after, params int[] changedLines)
        {
            var a = before.Split('\n');
            var b = after.Split('\n');
            Assert.Equal(a.Length, b.Length);
            for (var i = 0; i < a.Length; i++)
            {
                if (changedLines.Contains(i))
                    Assert.NotEqual(a[i], b[i]);
                else
                    Assert.Equal(a[i], b[i]);
            }
        }

        [Fact]
        public void SetValue_ChangesOnlyThatLine()
        {
            var after = FrontMatter.SetValue(Sample, 0, "title", "새 제목");

            AssertOnlyLinesChanged(Sample, after, 2);
            Assert.Equal("title: 새 제목", after.Split('\n')[2]);
        }

        [Fact]
        public void SetValue_KeepsQuoteStyleAndComment()
        {
            var after = FrontMatter.SetValue(Sample, 1, "subtitle", "다른 \"부제\"");
            Assert.Equal("subtitle: \"다른 \\\"부제\\\"\"   # 부제", after.Split('\n')[3]);
            AssertOnlyLinesChanged(Sample, after, 3);

            after = FrontMatter.SetValue(Sample, 2, "author", "It's me");
            Assert.Equal("author: 'It''s me'", after.Split('\n')[4]);
        }

        [Theory]
        [InlineData("plain", "plain")]
        [InlineData("a: b", "\"a: b\"")]
        [InlineData("# 해시로 시작", "\"# 해시로 시작\"")]
        [InlineData("값 #주석처럼", "\"값 #주석처럼\"")]
        [InlineData("[괄호]", "\"[괄호]\"")]
        [InlineData("- 대시", "\"- 대시\"")]
        [InlineData(" 앞 공백", "\" 앞 공백\"")]
        [InlineData("-5", "-5")]
        [InlineData("http://x.y/a?b=c", "http://x.y/a?b=c")]
        public void SetValue_PlainValueIsQuotedOnlyWhenNeeded(string value, string written)
        {
            var after = FrontMatter.SetValue("---\nk: v\n---", 0, "k", value);

            Assert.Equal("---\nk: " + written + "\n---", after);
            Assert.Equal(value, FrontMatter.Parse(after).Entries[0].Value);
        }

        [Fact]
        public void SetValue_EmptyAndFromEmpty()
        {
            Assert.Equal("---\nk:\n---", FrontMatter.SetValue("---\nk: v\n---", 0, "k", string.Empty));
            Assert.Equal("---\nk: 새 값\n---", FrontMatter.SetValue("---\nk:\n---", 0, "k", "새 값"));
            Assert.Equal("---\nk: \"\"\n---", FrontMatter.SetValue("---\nk: \"v\"\n---", 0, "k", string.Empty));
        }

        [Fact]
        public void SetValue_NewLinesBecomeSpaces()
        {
            Assert.Equal("---\nk: a b\n---", FrontMatter.SetValue("---\nk: v\n---", 0, "k", "a\r\nb"));
        }

        [Fact]
        public void Edit_WithWrongKeyOrIndex_IsRejected()
        {
            Assert.Null(FrontMatter.SetValue(Sample, 0, "subtitle", "x"));
            Assert.Null(FrontMatter.SetValue(Sample, 99, "title", "x"));
            Assert.Null(FrontMatter.SetValue(Sample, 5, "tags", "x"));      // 목록은 SetItems로
            Assert.Null(FrontMatter.SetItems(Sample, 0, "title", new[] { "x" }));
            Assert.Null(FrontMatter.RemoveEntry(Sample, 1, "title"));
        }

        [Fact]
        public void SetItems_FlowList_KeepsOriginalTokens()
        {
            var after = FrontMatter.SetItems(Sample, 5, "tags", new[] { "b, c", "a", "새 항목", "x,y" });

            AssertOnlyLinesChanged(Sample, after, 8);
            Assert.Equal("tags: [\"b, c\", a, 새 항목, \"x,y\"]", after.Split('\n')[8]);
            Assert.Equal(new[] { "b, c", "a", "새 항목", "x,y" }, FrontMatter.Parse(after).Entries[5].Items);
        }

        [Fact]
        public void SetItems_BlockList_KeepsIndentAndTokens()
        {
            var after = FrontMatter.SetItems(Sample, 6, "aliases", new[] { "two", "three" });

            Assert.Equal(Sample.Replace("  - one\n  - \"two\"\n", "  - \"two\"\n  - three\n"), after);
        }

        [Fact]
        public void SetItems_BlockList_CanGrowAndShrink()
        {
            var text = L("---", "tags:", "- a", "after: 1", "---");

            Assert.Equal(L("---", "tags:", "- a", "- b", "- c", "after: 1", "---"), FrontMatter.SetItems(text, 0, "tags", new[] { "a", "b", "c" }));
            Assert.Equal(L("---", "tags: []", "after: 1", "---"), FrontMatter.SetItems(text, 0, "tags", new string[0]));
        }

        [Fact]
        public void RenameKey_ChangesOnlyKey()
        {
            var after = FrontMatter.RenameKey(Sample, 1, "subtitle", "부제");

            AssertOnlyLinesChanged(Sample, after, 3);
            Assert.Equal("부제: \"따옴표: 값\"   # 부제", after.Split('\n')[3]);
            Assert.Equal("\"a: b\": [a, \"b, c\", d]", FrontMatter.RenameKey(Sample, 5, "tags", "a: b").Split('\n')[8]);
        }

        [Fact]
        public void RenameKey_RejectsEmptyOrDuplicate()
        {
            Assert.Null(FrontMatter.RenameKey(Sample, 0, "title", "  "));
            Assert.Null(FrontMatter.RenameKey(Sample, 0, "title", "draft"));
            Assert.Equal(Sample, FrontMatter.RenameKey(Sample, 0, "title", "title"));
        }

        [Fact]
        public void AddEntry_InsertsBeforeClosingLine()
        {
            var after = FrontMatter.AddEntry(Sample, FrontMatter.UniqueKey(Sample, "title"));

            Assert.Equal(Sample.Substring(0, Sample.Length - 3) + "title2:\n---", after);
            Assert.Equal("title2", FrontMatter.Parse(after).Entries.Last().Key);
            Assert.Null(FrontMatter.AddEntry(Sample, "draft"));
            Assert.Equal("---\n새: 값\n---", FrontMatter.AddEntry("---\n---", "새", "값"));
            Assert.Equal("---\na: 1\nb:\n...", FrontMatter.AddEntry("---\na: 1\n...", "b"));
        }

        [Fact]
        public void RemoveEntry_RemovesOnlyItsLines()
        {
            var after = FrontMatter.RemoveEntry(Sample, 6, "aliases");
            Assert.Equal(Sample.Replace("aliases:\n  - one\n  - \"two\"\n", string.Empty), after);

            // 위 주석·빈 줄은 남는다
            after = FrontMatter.RemoveEntry(Sample, 0, "title");
            Assert.Equal(Sample.Replace("title: 블록 편집기\n", string.Empty), after);
        }

        [Fact]
        public void RemoveEntry_ComplexValueRemovesAllItsLines()
        {
            var text = L("---", "a: 1", "nested:", "  x: 1", "  y: 2", "b: 2", "---");

            Assert.Equal(L("---", "a: 1", "b: 2", "---"), FrontMatter.RemoveEntry(text, 1, "nested"));
        }

        [Fact]
        public void ListInput_RoundTrips()
        {
            var items = new[] { "a", "b, c", "\"인용\"" };

            var input = FrontMatter.FormatListForEdit(items);

            Assert.Equal("a, \"b, c\", \"\\\"인용\\\"\"", input);
            Assert.Equal(items, FrontMatter.SplitListInput(input));
            Assert.Equal(new[] { "x", "y" }, FrontMatter.SplitListInput(" x ,, y , "));
            Assert.Empty(FrontMatter.SplitListInput("   "));
        }

        [Fact]
        public void EditedDocument_ChangesOnlyEditedLineInMarkdown()
        {
            var markdown = Sample + "\n\n# 제목\n\n본문\n";
            var blocks = BlockMarkdown.Parse(markdown);

            blocks[0].Text = FrontMatter.SetValue(blocks[0].Text, 3, "draft", "false");

            Assert.Equal(markdown.Replace("draft: true", "draft: false"), BlockMarkdown.Serialize(blocks));
        }
    }
}

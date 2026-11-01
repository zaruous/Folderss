using Folderss.Models;
using Folderss.Services;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>블록 편집기 줄 안 밑줄·글자 색·형광펜(#76 9번): 범위 이동·나누기·합치기·직렬화·해시 검증.</summary>
    public sealed class TextMarksTests
    {
        private static TextMark U(int s, int e) => new TextMark(TextMarkKind.Underline, null, s, e);
        private static TextMark C(string name, int s, int e) => new TextMark(TextMarkKind.Color, name, s, e);
        private static TextMark H(string name, int s, int e) => new TextMark(TextMarkKind.Highlight, name, s, e);

        private static string Str(IEnumerable<TextMark> marks) => string.Join(";", marks.Select(m => m.ToString()));

        private static EditorBlock Block(string text, params TextMark[] marks)
        {
            var block = new EditorBlock(BlockKind.Paragraph, text);
            TextMarks.Set(block, marks);
            return block;
        }

        /// <summary>범위 글자(보이는 글자 기준).</summary>
        private static string Covered(EditorBlock block, TextMarkKind kind)
        {
            var plain = TextMarks.PlainOf(block);
            return string.Join("|", TextMarks.Get(block).Where(m => m.Kind == kind).Select(m => plain.Substring(m.Start, m.End - m.Start)));
        }

        // ── 값 형식 ──────────────────────────────────────────────────────

        [Fact]
        public void Format_HashPipeRanges_InKindOrder()
        {
            var value = TextMarks.Format(new[] { H("yellow", 6, 11), U(0, 4), C("red", 2, 3) }, "hello world!");

            Assert.Equal(TextMarks.Hash("hello world!") + "|u:0-4;c=red:2-3;h=yellow:6-11", value);
            Assert.Equal(8, TextMarks.Hash("x").Length);
            Assert.Null(TextMarks.Format(new TextMark[0], "abc"));
        }

        [Fact]
        public void TryParse_RoundTrip()
        {
            var value = TextMarks.Format(new[] { U(0, 4), H("yellow", 6, 11) }, "hello world!");
            List<TextMark> marks;

            Assert.True(TextMarks.TryParse(value, "hello world!", out marks));
            Assert.Equal("u:0-4;h=yellow:6-11", Str(marks));
        }

        [Theory]
        [InlineData("hello world?")]          // 본문이 바뀜 → 해시 다름
        [InlineData("")]
        public void TryParse_HashMismatch_Rejected(string plain)
        {
            var value = TextMarks.Format(new[] { U(0, 4) }, "hello world!");
            List<TextMark> marks;

            Assert.False(TextMarks.TryParse(value, plain, out marks));
            Assert.Empty(marks);
        }

        [Theory]
        [InlineData("|u:0-1")]
        [InlineData("{h}|u:0")]
        [InlineData("{h}|u:3-1")]
        [InlineData("{h}|x:0-1")]
        [InlineData("{h}|c=RED:0-1")]
        [InlineData("{h}|c=red:0-99")]       // 글자 밖
        [InlineData("{h}|")]
        [InlineData("{h}")]
        public void TryParse_Malformed_Rejected(string pattern)
        {
            List<TextMark> marks;
            Assert.False(TextMarks.TryParse(pattern.Replace("{h}", TextMarks.Hash("abcdef")), "abcdef", out marks));
        }

        // ── 본문 변경에 따른 이동(Adjust) ────────────────────────────────────

        [Theory]
        // 범위 안 입력 → 늘어남
        [InlineData("abcdef", "abXcdef", "u:1-4", "u:1-5")]
        // 범위 끝에서 입력 → 이어짐(Notion처럼)
        [InlineData("abcdef", "abcdXef", "u:1-4", "u:1-5")]
        // 범위 시작(줄 처음 아님)에서 입력 → 따라가지 않고 밀림
        [InlineData("abcdef", "aXbcdef", "u:1-4", "u:2-5")]
        // 범위 앞 입력 → 밀림, 뒤 입력 → 그대로
        [InlineData("abcdef", "Xabcdef", "u:2-4", "u:3-5")]
        [InlineData("abcdef", "abcdefX", "u:1-3", "u:1-3")]
        // 줄 처음 입력 → 바로 뒤 글자의 범위를 따름(서식 입력란이 첫 조각에 넣는 것과 같음)
        [InlineData("abcdef", "Xabcdef", "u:0-2", "u:0-3")]
        // 지우기 → 줄어듦, 범위 전체를 지우면 사라짐
        [InlineData("abcdef", "abef", "u:1-5", "u:1-3")]
        [InlineData("abcdef", "aef", "u:1-3", "")]
        [InlineData("abcdef", "abcf", "u:0-2;c=red:3-5", "u:0-2")]
        // 바꾸기(다른 길이) → 바꾼 첫 글자의 범위를 따름
        [InlineData("say cat now", "say tiger now", "h=yellow:4-7", "h=yellow:4-9")]
        [InlineData("say cat now", "say tiger now", "u:0-3", "u:0-3")]
        // 같은 길이로 바꾸기 → 글자마다 그대로
        [InlineData("a\nb\nc", "a b c", "c=red:2-3", "c=red:2-3")]
        public void Adjust_MovesRanges(string oldPlain, string newPlain, string before, string after)
        {
            var parsed = Parse(before, oldPlain);

            Assert.Equal(after, Str(TextMarks.Adjust(parsed, oldPlain, newPlain)));
        }

        private static List<TextMark> Parse(string items, string plain)
        {
            List<TextMark> marks;
            Assert.True(TextMarks.TryParse(TextMarks.Hash(plain) + "|" + items, plain, out marks));
            return marks;
        }

        [Fact]
        public void Edit_InsertIntoEmpty_NoMarks()
        {
            Assert.Empty(TextMarks.Edit(new TextMark[0], 0, 0, 0, 3));
        }

        // ── 나누기·합치기·붙여넣기 ─────────────────────────────────────────

        [Fact]
        public void SliceConcat_SplitThenJoin_SameMarks()
        {
            const string plain = "밑줄 빨강 노랑";
            var marks = new List<TextMark> { U(0, 2), C("red", 3, 5), H("yellow", 4, 8) };

            var head = TextMarks.Slice(marks, 0, 4);
            var tail = TextMarks.Slice(marks, 4, plain.Length);
            var joined = TextMarks.Concat(head, 4, tail, plain.Length - 4);

            Assert.Equal("u:0-2;c=red:3-4", Str(head));
            Assert.Equal("c=red:0-1;h=yellow:0-4", Str(tail));
            Assert.Equal(Str(TextMarks.Normalize(marks, plain.Length)), Str(joined));
        }

        [Fact]
        public void Concat_TailDoesNotInheritHeadEnd()
        {
            // 합치기: 앞 블록 끝이 밑줄이어도 뒤 블록 글자는 제 범위만(본문 변경 규칙의 "끝에서 이어짐"을 쓰지 않음)
            var joined = TextMarks.Concat(new[] { U(0, 3) }, 3, new TextMark[0], 3);

            Assert.Equal("u:0-3", Str(joined));
        }

        [Fact]
        public void Replace_PasteKeepsOwnMarksOnly()
        {
            // "abcdef"의 [2,4)를 밑줄 없는 "XYZ"(1~2 빨강)로
            var result = TextMarks.Replace(new[] { U(0, 6) }, 6, 2, 4, new[] { C("red", 1, 2) }, 3);

            Assert.Equal("u:0-2;u:5-7;c=red:3-4", Str(result));
        }

        // ── 적용·확인 ────────────────────────────────────────────────────

        [Fact]
        public void Apply_ToggleAndOverride()
        {
            var marks = TextMarks.Apply(new TextMark[0], 10, TextMarkKind.Underline, string.Empty, 2, 6);
            Assert.True(TextMarks.Covers(marks, 10, TextMarkKind.Underline, 2, 6));
            Assert.False(TextMarks.Covers(marks, 10, TextMarkKind.Underline, 1, 6));

            marks = TextMarks.Apply(marks, 10, TextMarkKind.Underline, null, 3, 4);
            Assert.Equal("u:2-3;u:4-6", Str(marks));

            marks = TextMarks.Apply(marks, 10, TextMarkKind.Color, "red", 0, 10);
            marks = TextMarks.Apply(marks, 10, TextMarkKind.Color, "blue", 5, 7);
            Assert.True(TextMarks.Covers(marks, 10, TextMarkKind.Color, 5, 7, "blue"));
            Assert.False(TextMarks.Covers(marks, 10, TextMarkKind.Color, 4, 7, "blue"));
            Assert.True(TextMarks.Covers(marks, 10, TextMarkKind.Color, 4, 7));
            Assert.Equal("u:2-3;u:4-6;c=red:0-5;c=blue:5-7;c=red:7-10", Str(marks));
        }

        // ── 조각(InlineRun) ↔ 범위, Markdown 왕복 ──────────────────────────

        [Theory]
        [InlineData("**굵은 글자** 보통 `코드 조각` 끝", 1, 11)]   // 코드 조각이 범위 경계에서 나뉨
        [InlineData("[링크 글자](https://a.b) *기울임* ~~취소~~", 2, 12)]
        [InlineData("수식 $x+y$ 과 &amp; 원문 <b>태그</b>", 0, 22)]
        [InlineData("`a` `b` 코드 둘", 0, 5)]
        public void MarkedRuns_SerializeToSameMarkdown(string markdown, int start, int end)
        {
            var runs = InlineMarkdown.ParseForEditing(markdown);
            var plain = InlineMarkdown.ToPlain(runs);
            var marks = new List<TextMark> { U(start, end), C("red", start + 1, end), H("yellow", start, start + 2) };

            var marked = TextMarks.ApplyToRuns(runs, marks);

            Assert.Equal(InlineMarkdown.Serialize(runs), InlineMarkdown.Serialize(marked));   // Markdown에는 표식 없음
            Assert.Equal(plain, InlineMarkdown.ToPlain(marked));
            Assert.Equal(Str(TextMarks.Normalize(marks, plain.Length)), Str(TextMarks.FromRuns(marked)));
        }

        [Fact]
        public void MarkedRuns_OverlapBold_KeepsBoth()
        {
            var runs = InlineMarkdown.ParseForEditing("가**나다**라");
            var marked = TextMarks.ApplyToRuns(runs, new[] { U(1, 3) });

            var nada = marked.Where(r => r.Text == "나다").Single();
            Assert.True(nada.Has(InlineFormat.Bold));
            Assert.True(nada.Underline);
            Assert.False(marked.First().Underline);

            // 서식 적용(굵게 해제)은 범위를 지우지 않는다
            var unbold = InlineMarkdown.ApplyFormat(marked, 0, 4, InlineFormat.Bold, false);
            Assert.Equal("가나다라", InlineMarkdown.Serialize(unbold));
            Assert.Equal("u:1-3", Str(TextMarks.FromRuns(unbold)));
        }

        [Fact]
        public void DisplayMathRun_IsAtomic()
        {
            // 표시용 읽기(Parse)에서는 수식 조각이 하나 — 나누지 않고 첫 글자의 범위를 따른다.
            var runs = InlineMarkdown.Parse("a $x$ b");
            var marked = TextMarks.ApplyToRuns(runs, new[] { C("blue", 2, 5) });

            var math = marked.Single(r => r.Kind == InlineRunKind.Math);
            Assert.Equal("blue", math.TextColor);
            Assert.Equal("x", math.Text);
        }

        // ── 블록 본문이 바뀌는 경로 ────────────────────────────────────────

        [Fact]
        public void BlockText_Typing_ShiftsMarks()
        {
            var block = Block("hello **world**", U(6, 11));
            Assert.Equal("world", Covered(block, TextMarkKind.Underline));

            block.Text = "oh hello **world**";   // 앞에 입력

            Assert.Equal("world", Covered(block, TextMarkKind.Underline));
            block.Text = "oh hello **world**!!";   // 끝에서 이어 입력 → 범위가 이어짐
            Assert.Equal("world!!", Covered(block, TextMarkKind.Underline));
        }

        [Fact]
        public void BlockText_SlashCommandRemoval_ShiftsMarks()
        {
            var block = Block("앞 /제목 뒤 글자", H("yellow", 8, 10));
            Assert.Equal("글자", Covered(block, TextMarkKind.Highlight));

            block.Text = "앞  뒤 글자";   // "/제목" 지움

            Assert.Equal("글자", Covered(block, TextMarkKind.Highlight));
        }

        [Fact]
        public void FindReplaceAll_MarkedBlock_EachMatchMovesExactly()
        {
            var blocks = new List<EditorBlock> { Block("cat a cat b cat", H("yellow", 6, 7), U(10, 11)) };
            Assert.Equal("a", "cat a cat b cat".Substring(4, 1));

            int first, last;
            var count = BlockFind.ReplaceAll(blocks, "cat", "tiger", false, out first, out last);

            Assert.Equal(3, count);
            Assert.Equal("tiger a tiger b tiger", blocks[0].Text);
            Assert.Equal("t", Covered(blocks[0], TextMarkKind.Highlight).Substring(0, 1));
            Assert.Equal("tiger", Covered(blocks[0], TextMarkKind.Highlight));   // 둘째 cat의 a(6)에 칠했던 범위 → 바뀐 첫 글자 범위를 따라 tiger 전체
        }

        [Fact]
        public void FindReplaceAll_MarkedBlock_OtherWordsKeepMarks()
        {
            var blocks = new List<EditorBlock> { Block("x foo y foo z", C("red", 6, 7), U(12, 13)) };

            int first, last;
            BlockFind.ReplaceAll(blocks, "foo", "barbaz", false, out first, out last);

            Assert.Equal("x barbaz y barbaz z", blocks[0].Text);
            Assert.Equal("y", Covered(blocks[0], TextMarkKind.Color));
            Assert.Equal("z", Covered(blocks[0], TextMarkKind.Underline));
        }

        [Fact]
        public void BlockText_ExternalPathWithMismatchedMarks_Dropped()
        {
            var block = Block("abcdef", U(0, 3));
            block.SetAttribute(TextMarks.Attribute, "deadbeef|u:0-3");   // 다른 글자 기준 값

            block.Text = "abcdefg";

            Assert.Null(block.GetAttribute(TextMarks.Attribute));
        }

        [Fact]
        public void Split_HeadAutoTruncates_TailGetsSlice()
        {
            // 편집기 SplitBlock: 앞 블록은 본문 변경으로 잘리고, 뒤 블록은 커서 뒤 범위를 받는다.
            var block = Block("앞부분**굵은 뒤**", U(1, 6));
            var plain = TextMarks.PlainOf(block);   // "앞부분굵은 뒤"
            var marks = TextMarks.Get(block);
            string head, tail;
            InlineMarkdown.SplitAt(block.Text, 4, out head, out tail);

            block.Text = head;
            var created = new EditorBlock(BlockKind.Paragraph, tail);
            TextMarks.Set(created, TextMarks.Slice(marks, 4, plain.Length), plain.Substring(4));

            Assert.Equal("부분굵", Covered(block, TextMarkKind.Underline));
            Assert.Equal("은 뒤", TextMarks.PlainOf(created));
            Assert.Equal("은 ", Covered(created, TextMarkKind.Underline));
        }

        [Fact]
        public void Merge_ConcatKeepsBothSides()
        {
            var a = Block("첫째 끝", U(3, 4));
            var b = Block("둘째", C("red", 0, 2));
            var headPlain = TextMarks.PlainOf(a);
            var headMarks = TextMarks.Get(a);
            var tailPlain = TextMarks.PlainOf(b);

            a.Text = InlineMarkdown.Concat(a.Text, b.Text);   // 자동 이동은 "끝에서 이어짐"으로 뒤 글자에도 밑줄을 칠하지만
            TextMarks.Set(a, TextMarks.Concat(headMarks, headPlain.Length, TextMarks.Get(b), tailPlain.Length), headPlain + tailPlain);   // 합치기는 따로 정함

            Assert.Equal("끝", Covered(a, TextMarkKind.Underline));
            Assert.Equal("둘째", Covered(a, TextMarkKind.Color));
        }

        [Fact]
        public void CloneDuplicate_KeepMarks_SameContentSeesChange()
        {
            var block = Block("abc", U(0, 1));
            var clone = block.Clone();
            var copy = block.Duplicate();

            Assert.True(clone.SameContent(block));
            Assert.Equal(block.GetAttribute(TextMarks.Attribute), copy.GetAttribute(TextMarks.Attribute));

            TextMarks.Set(block, new[] { U(0, 2) });
            Assert.False(clone.SameContent(block));   // 범위만 바뀌어도 실행 취소 기록에 남음
        }

        [Fact]
        public void TurnIntoHeading_KeepsMarks_TurnIntoCode_Drops()
        {
            var block = Block("제목 될 글자", C("purple", 0, 2));

            block.Kind = BlockKind.Heading2;
            Assert.Equal("제목", Covered(block, TextMarkKind.Color));

            block.Kind = BlockKind.Code;
            Assert.Null(block.GetAttribute(TextMarks.Attribute));
        }

        [Fact]
        public void SetText_DoesNotAutoAdjust()
        {
            var block = Block("abc", U(2, 3));

            TextMarks.SetText(block, "abcd", new[] { U(3, 4) }, "abcd");

            Assert.Equal("d", Covered(block, TextMarkKind.Underline));
        }

        // ── HTML 붙여넣기 ─────────────────────────────────────────────────

        [Fact]
        public void Html_UnderlineColorMark_BecomeLineMarks()
        {
            var html = "<p>보통 <u>밑줄</u> <span style=\"color:#e03e2d\">빨강</span> <mark>형광</mark> <b><span style=\"background-color: rgb(255, 240, 0)\">굵노랑</span></b></p>";
            var lineMarks = new List<string>();

            var markdown = ClipboardMarkdown.FromHtml(html, lineMarks);

            Assert.Equal("보통 밑줄 빨강 형광 **굵노랑**", markdown);   // Markdown에는 표식 없음
            Assert.Single(lineMarks);
            List<TextMark> marks;
            var plain = InlineMarkdown.ToPlain(markdown);
            Assert.True(TextMarks.TryParse(lineMarks[0], plain, out marks));
            string Of(TextMarkKind kind, string value = null) => string.Join("|", marks.Where(m => m.Kind == kind && (value == null || m.Value == value)).Select(m => plain.Substring(m.Start, m.End - m.Start)));
            Assert.Equal("밑줄", Of(TextMarkKind.Underline));
            Assert.Equal("빨강", Of(TextMarkKind.Color, "red"));
            Assert.Equal("형광|굵노랑", Of(TextMarkKind.Highlight, "yellow"));
        }

        [Fact]
        public void Html_MultipleBlocks_MarksPerLine()
        {
            var lineMarks = new List<string>();
            var markdown = ClipboardMarkdown.FromHtml("<h2>제목 <u>밑</u></h2><p>보통</p><pre>code</pre><li><font color=\"blue\">파랑</font></li>", lineMarks);

            Assert.Equal("## 제목 밑\n\n보통\n\n```\ncode\n```\n\n- 파랑", markdown);
            Assert.Equal(4, lineMarks.Count);
            Assert.NotNull(lineMarks[0]);
            Assert.Null(lineMarks[1]);
            Assert.Null(lineMarks[2]);
            var blocks = BlockMarkdown.Parse(markdown);
            List<TextMark> marks;
            Assert.True(TextMarks.TryParse(lineMarks[0], TextMarks.PlainOf(blocks[0]), out marks));
            Assert.Equal("u:3-4", Str(marks));
            Assert.True(TextMarks.TryParse(lineMarks[3], TextMarks.PlainOf(blocks[3]), out marks));
            Assert.Equal("c=blue:0-2", Str(marks));
        }

        [Fact]
        public void Html_DefaultColors_Ignored()
        {
            // Google 문서처럼 모든 글자에 검정 글자색·투명 바탕을 붙여도 범위로 만들지 않는다.
            var lineMarks = new List<string>();
            ClipboardMarkdown.FromHtml("<span style=\"color:#000000;background-color:transparent\">글자</span><span style=\"color:rgba(255,0,0,0.05)\">흐림</span>", lineMarks);

            Assert.Single(lineMarks);
            Assert.Null(lineMarks[0]);
        }

        [Theory]
        [InlineData("#ff0000", false, "red")]
        [InlineData("rgb(224, 62, 45)", false, "red")]
        [InlineData("#ffa500", false, "orange")]
        [InlineData("#8b4513", false, "brown")]
        [InlineData("yellow", true, "yellow")]
        [InlineData("#fbf3db", true, "yellow")]
        [InlineData("#00aa00", false, "green")]
        [InlineData("#1e90ff", false, "blue")]
        [InlineData("#800080", false, "purple")]
        [InlineData("hotpink", false, "pink")]
        [InlineData("#808080", false, "gray")]
        [InlineData("#000", false, null)]
        [InlineData("white", true, null)]
        [InlineData("transparent", true, null)]
        [InlineData("inherit", false, null)]
        public void NearestColorName_Maps(string css, bool background, string expected)
        {
            Assert.Equal(expected, ClipboardMarkdown.NearestColorName(css, background));
        }
    }
}

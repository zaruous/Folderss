using Folderss.Services;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>블록 편집기 "다음 입력 서식"(대기 서식, #76): 선택 없이 서식 키 뒤 입력, 입력 단축 변환 뒤 이어 쓰기, 범위 경계 규칙과의 우선순위.</summary>
    public sealed class PendingInlineFormatTests
    {
        /// <summary>
        /// 서식 입력란 입력 흉내: 글자는 <see cref="PendingInlineFormat.BaseRun"/> 조각(줄 처음이면 뒤 조각, 그 밖은 앞 조각)에 들어가고 범위도 그 조각을 따른다.
        /// 대기 서식이 있으면 들어간 글자 범위에 적용한다.
        /// </summary>
        private static List<InlineRun> Type(List<InlineRun> runs, int caret, string text, PendingInlineFormat pending = null)
        {
            var basis = PendingInlineFormat.BaseRun(runs, caret);
            var inserted = basis != null ? basis.Clone() : new InlineRun();
            inserted.Text = text;
            var result = InlineMarkdown.Replace(runs, caret, caret, new[] { inserted });
            return pending != null ? pending.Apply(result, caret, caret + text.Length) : result;
        }

        private static List<InlineRun> P(string markdown) => InlineMarkdown.ParseForEditing(markdown);
        private static string Md(List<InlineRun> runs) => InlineMarkdown.Serialize(runs);
        private static int Len(List<InlineRun> runs) => InlineMarkdown.ToPlain(runs).Length;

        [Fact]
        public void CtrlB_WithoutSelection_NextTypedTextIsBold_ThenContinues()
        {
            var runs = P("가나다라마");
            var pending = new PendingInlineFormat();
            pending.Toggle(runs, 5, InlineFormat.Bold);

            runs = Type(runs, 5, "x", pending);
            runs = Type(runs, 6, "y");   // 대기 서식은 한 번 적용 — 다음 글자는 앞 조각(굵게)을 잇는다

            Assert.Equal("가나다라마**xy**", Md(runs));
        }

        [Fact]
        public void CtrlB_AgainAtEndOfBold_NextTextIsNormal()
        {
            var runs = P("가나다라마**xy**");
            var pending = new PendingInlineFormat();
            Assert.True(pending.Effective(runs, 7, InlineFormat.Bold));
            pending.Toggle(runs, 7, InlineFormat.Bold);

            runs = Type(runs, 7, "z", pending);
            runs = Type(runs, 8, "w");

            Assert.Equal("가나다라마**xy**zw", Md(runs));
        }

        [Fact]
        public void InputRuleConversion_FollowingTextIsNormal()
        {
            var runs = P("시작 \\*\\*굵\\*\\*");
            List<InlineRun> converted;
            int caret;
            Assert.True(InlineMarkdown.TryInputRule(runs, Len(runs), out converted, out caret));
            Assert.Equal("시작 **굵**", Md(converted));

            var pending = PendingInlineFormat.AfterClosedFormat(converted, caret);
            Assert.NotNull(pending);
            runs = Type(converted, caret, "뒤", pending);
            runs = Type(runs, caret + 1, "글");

            Assert.Equal("시작 **굵**뒤글", Md(runs));
        }

        [Theory]
        [InlineData("시작 *기울*", "시작 *기울*뒤")]
        [InlineData("시작 ~~취소~~", "시작 ~~취소~~뒤")]
        [InlineData("시작 `코드`", "시작 `코드`뒤")]
        [InlineData("[글](u)", "[글](u)뒤")]
        [InlineData("시작 ***둘***", "시작 ***둘***뒤")]
        public void AfterClosedFormat_TurnsOffEveryFormatAndLinkBeforeCaret(string markdown, string expected)
        {
            var runs = P(markdown);
            var caret = Len(runs);
            var pending = PendingInlineFormat.AfterClosedFormat(runs, caret);
            Assert.NotNull(pending);
            Assert.Equal(expected, Md(Type(runs, caret, "뒤", pending)));
        }

        [Fact]
        public void AfterClosedFormat_PlainBefore_IsNull()
        {
            Assert.Null(PendingInlineFormat.AfterClosedFormat(P("보통 글자"), 5));
            Assert.Null(PendingInlineFormat.AfterClosedFormat(P("보통"), 0));
        }

        [Fact]
        public void ToggleTwice_BackToNormal_NoChange()
        {
            var runs = P("abc");
            var pending = new PendingInlineFormat();
            pending.Toggle(runs, 3, InlineFormat.Bold);
            pending.Toggle(runs, 3, InlineFormat.Bold);

            Assert.False(pending.Effective(runs, 3, InlineFormat.Bold));
            Assert.Equal("abcd", Md(Type(runs, 3, "d", pending)));
        }

        [Fact]
        public void SeveralKeys_Accumulate()
        {
            var runs = P("abc");
            var pending = new PendingInlineFormat();
            pending.Toggle(runs, 3, InlineFormat.Bold);
            pending.Toggle(runs, 3, InlineFormat.Italic);
            pending.Toggle(runs, 3, InlineFormat.Strike);

            Assert.Equal("abc~~***d***~~", Md(Type(runs, 3, "d", pending)));
        }

        [Fact]
        public void Code_OnInMiddleOfText()
        {
            var runs = P("ab");
            var pending = new PendingInlineFormat();
            pending.Toggle(runs, 1, InlineFormat.Code);

            Assert.Equal("a`x`b", Md(Type(runs, 1, "x", pending)));
        }

        [Fact]
        public void LineStart_FollowsFirstRun()
        {
            var runs = P("**굵게** 뒤");
            Assert.True(PendingInlineFormat.BaseRun(runs, 0).Has(InlineFormat.Bold));
            var pending = new PendingInlineFormat();
            Assert.True(pending.Effective(runs, 0, InlineFormat.Bold));
            pending.Toggle(runs, 0, InlineFormat.Bold);   // 줄 처음 굵은 글자 앞에서 끄기

            Assert.Equal("x**굵게** 뒤", Md(Type(runs, 0, "x", pending)));
        }

        [Fact]
        public void Composition_ManyCharacters_AppliedToWholeRange()
        {
            // 입력기 조합: 조합이 끝난 뒤 들어온 글자 범위 전체에 한 번 적용(조합 중에는 다시 그리지 않음)
            var runs = P("앞 ");
            var pending = new PendingInlineFormat();
            pending.Toggle(runs, 2, InlineFormat.Bold);
            var typed = InlineMarkdown.Replace(runs, 2, 2, new[] { new InlineRun { Text = "한글" } });

            Assert.Equal("앞 **한글**", Md(pending.Apply(typed, 2, 4)));
        }

        // ── 범위(밑줄·색) — 대기 서식이 경계 규칙보다 우선 ─────────────────

        private static string Covered(List<InlineRun> runs, TextMarkKind kind)
        {
            var plain = InlineMarkdown.ToPlain(runs);
            return string.Join("|", TextMarks.FromRuns(runs).Where(m => m.Kind == kind).Select(m => plain.Substring(m.Start, m.End - m.Start)));
        }

        private static List<InlineRun> Underlined(string plain, int start, int end)
        {
            var runs = P(plain);
            return TextMarks.ApplyToRuns(runs, new[] { new TextMark(TextMarkKind.Underline, null, start, end) });
        }

        [Fact]
        public void Underline_Pending_OnPlainText()
        {
            var runs = P("abc");
            var pending = new PendingInlineFormat();
            pending.ToggleUnderline(runs, 3);
            runs = Type(runs, 3, "d", pending);
            runs = Type(runs, 4, "e");

            Assert.Equal("de", Covered(runs, TextMarkKind.Underline));
            Assert.Equal("abcde", Md(runs));   // Markdown에는 표식 없음
        }

        [Fact]
        public void BoundaryRule_WithoutPending_RangeEndContinues()
        {
            var runs = Underlined("abc", 0, 3);
            Assert.Equal("abcd", Covered(Type(runs, 3, "d"), TextMarkKind.Underline));
        }

        [Fact]
        public void Pending_Underline_OverridesBoundaryRule()
        {
            var runs = Underlined("abc", 0, 3);
            var pending = new PendingInlineFormat();
            Assert.True(pending.EffectiveUnderline(runs, 3));
            pending.ToggleUnderline(runs, 3);   // 범위 끝에서 Ctrl+U → 끄기

            Assert.Equal("abc", Covered(Type(runs, 3, "d", pending), TextMarkKind.Underline));
        }

        [Fact]
        public void Pending_BoldOnly_LeavesRangeToBoundaryRule()
        {
            var runs = Underlined("abc", 0, 3);
            var pending = new PendingInlineFormat();
            pending.Toggle(runs, 3, InlineFormat.Bold);
            var result = Type(runs, 3, "d", pending);

            Assert.Equal("abc**d**", Md(result));
            Assert.Equal("abcd", Covered(result, TextMarkKind.Underline));   // 대기 서식에 정하지 않은 밑줄은 경계 규칙(이어짐)
        }

        [Fact]
        public void Pending_Colors()
        {
            var runs = P("ab");
            var pending = new PendingInlineFormat();
            pending.SetColor(false, "red");
            pending.SetColor(true, "yellow");
            var result = Type(runs, 2, "cd", pending);

            Assert.Equal("cd", Covered(result, TextMarkKind.Color));
            Assert.Equal("cd", Covered(result, TextMarkKind.Highlight));
            Assert.Equal("red", result.Last().TextColor);
        }

        [Fact]
        public void Pending_ColorClear_OverridesInheritedColor()
        {
            var runs = TextMarks.ApplyToRuns(P("ab"), new[] { new TextMark(TextMarkKind.Color, "red", 0, 2) });
            var pending = new PendingInlineFormat();
            pending.SetColor(false, null);

            Assert.Equal("ab", Covered(Type(runs, 2, "c", pending), TextMarkKind.Color));
        }

        [Fact]
        public void Like_ReplacingSelection_FollowsFirstReplacedCharacter()
        {
            // 선택 "굵밑" 덮어 입력 → 첫 글자(굵게·밑줄·빨강) 모양. WPF가 넣은 글자는 앞 조각(보통)을 따랐다고 흉내낸다.
            var runs = TextMarks.ApplyToRuns(P("앞 **굵**밑 뒤"), new[] { new TextMark(TextMarkKind.Underline, null, 2, 4), new TextMark(TextMarkKind.Color, "red", 2, 3) });
            var pending = PendingInlineFormat.Like(InlineMarkdown.RunAt(runs, 2, false));
            var deleted = InlineMarkdown.Replace(runs, 2, 4, new InlineRun[0]);
            var typed = InlineMarkdown.Replace(deleted, 2, 2, new[] { new InlineRun { Text = "XY" } });
            var result = pending.Apply(typed, 2, 4);

            Assert.Equal("앞 **XY** 뒤", Md(result));
            Assert.Equal("XY", Covered(result, TextMarkKind.Underline));
            Assert.Equal("XY", Covered(result, TextMarkKind.Color));
        }

        [Fact]
        public void Like_LinkAndPlain()
        {
            var link = PendingInlineFormat.Like(P("[글](u)")[0]);
            var typed = InlineMarkdown.Replace(P("앞 "), 2, 2, new[] { new InlineRun { Text = "새" } });
            Assert.Equal("앞 [새](u)", Md(link.Apply(typed, 2, 3)));

            var plain = PendingInlineFormat.Like(P("보통")[0]);
            var boldTyped = InlineMarkdown.Replace(P("앞 "), 2, 2, new[] { new InlineRun { Text = "새", Format = InlineFormat.Bold } });
            Assert.Equal("앞 새", Md(plain.Apply(boldTyped, 2, 3)));
            Assert.Null(PendingInlineFormat.Like(null));
        }

        [Fact]
        public void IsEmpty()
        {
            Assert.True(new PendingInlineFormat().IsEmpty);
            var p = new PendingInlineFormat();
            p.Set(InlineFormat.Bold, false);
            Assert.False(p.IsEmpty);
        }
    }
}

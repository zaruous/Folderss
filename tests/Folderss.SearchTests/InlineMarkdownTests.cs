using Folderss.Models;
using Folderss.Services;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>블록 편집기 줄 안 서식(#76): 줄 안 Markdown 파서·직렬화와 보이는 글자 기준 편집 연산.</summary>
    public sealed class InlineMarkdownTests
    {
        private static string Show(List<InlineRun> runs)
        {
            return string.Join("|", runs.Select(r =>
            {
                var f = "";
                if (r.Has(InlineFormat.Bold)) f += "B";
                if (r.Has(InlineFormat.Italic)) f += "I";
                if (r.Has(InlineFormat.Strike)) f += "S";
                if (r.Has(InlineFormat.Code)) f += "C";
                if (r.Kind == InlineRunKind.Raw) f += "R";
                if (r.Kind == InlineRunKind.Math) f += "M";
                if (r.Link != null) f += "L(" + r.Link + ")";
                return (f.Length > 0 ? f + ":" : "") + r.Text;
            }));
        }

        [Theory]
        [InlineData("평범한 글자", "평범한 글자")]
        [InlineData("**굵게**", "B:굵게")]
        [InlineData("__굵게__", "B:굵게")]
        [InlineData("*기울임*", "I:기울임")]
        [InlineData("_기울임_", "I:기울임")]
        [InlineData("~~취소~~", "S:취소")]
        [InlineData("`코드`", "C:코드")]
        [InlineData("앞 **굵게** 뒤", "앞 |B:굵게| 뒤")]
        [InlineData("**굵게**는 조사", "B:굵게|는 조사")]
        [InlineData("***둘 다***", "BI:둘 다")]
        [InlineData("**굵게 *안 기울임* 끝**", "B:굵게 |BI:안 기울임|B: 끝")]
        [InlineData("*기울임 **안 굵게** 끝*", "I:기울임 |BI:안 굵게|I: 끝")]
        [InlineData("~~**취소 굵게**~~", "BS:취소 굵게")]
        [InlineData("[링크](https://a.com)", "L(https://a.com):링크")]
        [InlineData("[**굵은** 링크](a.md#제목)", "BL(a.md#제목):굵은|L(a.md#제목): 링크")]
        [InlineData("**[굵은 링크](u)**", "BL(u):굵은 링크")]
        [InlineData("`**코드 안 표식**`", "C:**코드 안 표식**")]
        [InlineData("``a`b``", "C:a`b")]
        [InlineData("\\*별표\\* 글자", "*별표* 글자")]
        [InlineData("snake_case_name", "snake_case_name")]
        [InlineData("2 * 3 * 4", "2 * 3 * 4")]
        [InlineData("**닫히지 않음", "**닫히지 않음")]
        [InlineData("수식 $E=mc^2$ 끝", "수식 |M:E=mc^2| 끝")]
        [InlineData("$5 ~ $10", "$5 ~ $10")]
        [InlineData("![그림](a.png) 뒤", "R:![그림](a.png)| 뒤")]
        [InlineData("줄<br>바꿈 &amp; 엔터티", "줄|R:<br>|바꿈 |R:&amp;| 엔터티")]
        [InlineData("<https://a.com>", "L(https://a.com):https://a.com")]
        [InlineData("[각주][1]", "[각주][1]")]
        [InlineData("[[위키]]", "[[위키]]")]
        public void Parse_ReadsFormats(string markdown, string expected)
        {
            Assert.Equal(expected, Show(InlineMarkdown.Parse(markdown)));
        }

        [Theory]
        [InlineData("평범한 글자")]
        [InlineData("**굵게**")]
        [InlineData("__굵게__")]
        [InlineData("*기울임*")]
        [InlineData("_기울임_")]
        [InlineData("~~취소~~")]
        [InlineData("~취소~")]
        [InlineData("`코드`")]
        [InlineData("``a`b``")]
        [InlineData("`` `a` ``")]
        [InlineData("앞 **굵게** 뒤 *기울임* `코드` ~~취소~~")]
        [InlineData("***둘 다***")]
        [InlineData("**굵게 *안 기울임* 끝**")]
        [InlineData("~~**취소 굵게**~~")]
        [InlineData("[링크](https://a.com)")]
        [InlineData("[**굵은** 링크](a.md#제목)")]
        [InlineData("[제목 있는 링크](u \"제목\")")]
        [InlineData("[꺾쇠 주소](<a b.md>)")]
        [InlineData("**[굵은 링크](u)**")]
        [InlineData("`**코드 안 표식**`")]
        [InlineData("\\*별표\\* 글자")]
        [InlineData("snake_case_name")]
        [InlineData("2 * 3 * 4")]
        [InlineData("**닫히지 않음")]
        [InlineData("수식 $E=mc^2$ 끝")]
        [InlineData("$5 ~ $10")]
        [InlineData("![그림](a.png) 뒤")]
        [InlineData("줄<br>바꿈 &amp; 엔터티")]
        [InlineData("<https://a.com> 자동 링크")]
        [InlineData("[각주][1] [^1] [[위키]]")]
        [InlineData("경로 C:\\temp\\a.txt")]
        [InlineData("https://example.com/a_b_c 맨 URL")]
        [InlineData("여러 줄\n둘째 줄 **굵게**")]
        public void NormalizedText_RoundTripsUnchanged(string markdown)
        {
            Assert.Equal(markdown, InlineMarkdown.Serialize(InlineMarkdown.Parse(markdown)));
            Assert.Equal(markdown, InlineMarkdown.Serialize(InlineMarkdown.ParseForEditing(markdown)));
        }

        [Fact]
        public void BlockMarkdown_FormattedDocument_RoundTripsThroughInlineModel()
        {
            var doc = "# 제목 **굵게**\n\n문단 *기울임* 과 `코드` 와 [링크](https://a.com)\n\n- 목록 ~~취소~~\n- [ ] 할 일 **중요**\n\n> 인용 _기울임_\n\n| 머리 | **굵은 머리** |\n| --- | --- |\n| `셀 코드` | [셀 링크](b.md) |\n";
            var blocks = BlockMarkdown.Parse(doc);
            foreach (var block in blocks)
            {
                if (EditorBlock.IsVerbatim(block.Kind))
                    continue;
                var cells = block.Text.Split(EditorBlock.CellSeparator);
                block.Text = string.Join(EditorBlock.CellSeparator.ToString(), cells.Select(c => InlineMarkdown.Serialize(InlineMarkdown.ParseForEditing(c))));
            }
            Assert.Equal(BlockMarkdown.Serialize(BlockMarkdown.Parse(doc)), BlockMarkdown.Serialize(blocks));
        }

        private static InlineRun T(string text, InlineFormat f = InlineFormat.None, string link = null)
        {
            return new InlineRun { Text = text, Format = f, Link = link };
        }

        public static IEnumerable<object[]> RunLists()
        {
            yield return new object[] { new List<InlineRun> { T("2 * 3") } };
            yield return new object[] { new List<InlineRun> { T("**literal**") } };
            yield return new object[] { new List<InlineRun> { T("a "), T("굵게 ", InlineFormat.Bold), T("뒤") } };
            yield return new object[] { new List<InlineRun> { T("굵게 ", InlineFormat.Bold), T("둘다", InlineFormat.Bold | InlineFormat.Italic), T(" 기울임", InlineFormat.Italic) } };
            yield return new object[] { new List<InlineRun> { T("코드`안", InlineFormat.Code) } };
            yield return new object[] { new List<InlineRun> { T("`앞뒤`", InlineFormat.Code) } };
            yield return new object[] { new List<InlineRun> { T("[대괄호]", InlineFormat.None, "u") } };
            yield return new object[] { new List<InlineRun> { T("경로\\"), T("굵게", InlineFormat.Bold) } };
            yield return new object[] { new List<InlineRun> { T("$5 그리고 $x$") } };
            yield return new object[] { new List<InlineRun> { T("&amp; <b>") } };
            yield return new object[] { new List<InlineRun> { T("![아님](x)") } };
            yield return new object[] { new List<InlineRun> { T("굵은 "), T("링크", InlineFormat.Bold, "a b.md") } };
            yield return new object[] { new List<InlineRun> { T("snake_case", InlineFormat.Italic) } };
            yield return new object[] { new List<InlineRun> { T("한글", InlineFormat.Bold), T("조사") } };
            yield return new object[] { new List<InlineRun> { T("취소", InlineFormat.Strike), T("~물결~") } };
        }

        [Theory]
        [MemberData(nameof(RunLists))]
        public void AnyRuns_SerializeAndParseBack(List<InlineRun> runs)
        {
            var md = InlineMarkdown.Serialize(runs);
            Assert.True(InlineMarkdown.Equivalent(InlineMarkdown.Normalize(runs), InlineMarkdown.ParseForEditing(md)), md + " → " + Show(InlineMarkdown.ParseForEditing(md)));
            Assert.Equal(InlineMarkdown.ToPlain(runs), InlineMarkdown.ToPlain(InlineMarkdown.ParseForEditing(md)));
        }

        [Fact]
        public void Serialize_MovesEdgeSpacesOutsideMarkers()
        {
            Assert.Equal("a **굵게** 뒤", InlineMarkdown.Serialize(new List<InlineRun> { T("a "), T("굵게 ", InlineFormat.Bold), T("뒤") }));
        }

        [Fact]
        public void Serialize_EscapesLiteralMarkersOnlyWhenNeeded()
        {
            Assert.Equal("2 * 3", InlineMarkdown.Serialize(new List<InlineRun> { T("2 * 3") }));
            Assert.Equal("\\*\\*literal\\*\\*", InlineMarkdown.Serialize(new List<InlineRun> { T("**literal**") }));
        }

        [Fact]
        public void SplitAt_KeepsFormatOnBothSides()
        {
            string head, tail;
            InlineMarkdown.SplitAt("앞 **굵은글** 뒤", 4, out head, out tail);
            Assert.Equal("앞 **굵은**", head);
            Assert.Equal("**글** 뒤", tail);
        }

        [Fact]
        public void Concat_MergesSameFormat()
        {
            Assert.Equal("**ab**", InlineMarkdown.Concat("**a**", "**b**"));
            Assert.Equal("평문 이어 붙이기", InlineMarkdown.Concat("평문 ", "이어 붙이기"));
        }

        [Fact]
        public void ApplyFormat_TogglesRange()
        {
            var runs = InlineMarkdown.ParseForEditing("가나다라");
            var bold = InlineMarkdown.ApplyFormat(runs, 1, 3, InlineFormat.Bold, true);
            Assert.Equal("가**나다**라", InlineMarkdown.Serialize(bold));
            Assert.True(InlineMarkdown.HasFormat(bold, 1, 3, InlineFormat.Bold));
            Assert.False(InlineMarkdown.HasFormat(bold, 0, 3, InlineFormat.Bold));
            var off = InlineMarkdown.ApplyFormat(bold, 1, 2, InlineFormat.Bold, false);
            Assert.Equal("가나**다**라", InlineMarkdown.Serialize(off));
            var code = InlineMarkdown.ApplyFormat(runs, 0, 4, InlineFormat.Code, true);
            Assert.Equal("`가나다라`", InlineMarkdown.Serialize(code));
        }

        [Fact]
        public void ApplyLink_AndLinkRange()
        {
            var runs = InlineMarkdown.ApplyLink(InlineMarkdown.ParseForEditing("여기를 누르세요"), 0, 3, "https://a.com");
            Assert.Equal("[여기를](https://a.com) 누르세요", InlineMarkdown.Serialize(runs));
            int s, e;
            InlineRun link;
            Assert.True(InlineMarkdown.LinkRange(runs, 1, out s, out e, out link));
            Assert.Equal(0, s);
            Assert.Equal(3, e);
            Assert.Equal("https://a.com", link.Url);
            var removed = InlineMarkdown.ApplyLink(runs, s, e, null);
            Assert.Equal("여기를 누르세요", InlineMarkdown.Serialize(removed));
        }

        [Theory]
        [InlineData("앞 **굵게**", 8, "앞 **굵게**", 4)]
        [InlineData("~~취소~~", 6, "~~취소~~", 2)]
        [InlineData("`코드`", 4, "`코드`", 2)]
        [InlineData("말 *기울임*", 7, "말 *기울임*", 5)]
        [InlineData("말 _기울임_", 7, "말 _기울임_", 5)]
        public void InputRule_ConvertsTypedMarkers(string typed, int caret, string expectedMarkdown, int expectedCaret)
        {
            var runs = new List<InlineRun> { T(typed) };
            List<InlineRun> result;
            int newCaret;
            Assert.True(InlineMarkdown.TryInputRule(runs, caret, out result, out newCaret));
            Assert.Equal(expectedMarkdown, InlineMarkdown.Serialize(result));
            Assert.Equal(expectedCaret, newCaret);
        }

        [Theory]
        [InlineData("2*3*", 4)]
        [InlineData("snake_case_", 11)]
        [InlineData("** 공백**", 7)]
        [InlineData("**", 2)]
        [InlineData("****", 4)]
        public void InputRule_IgnoresNonFormatting(string typed, int caret)
        {
            List<InlineRun> result;
            int newCaret;
            Assert.False(InlineMarkdown.TryInputRule(new List<InlineRun> { T(typed) }, caret, out result, out newCaret));
        }

        [Fact]
        public void InputRule_NotInsideCode()
        {
            List<InlineRun> result;
            int newCaret;
            var runs = new List<InlineRun> { T("**a**", InlineFormat.Code) };
            Assert.False(InlineMarkdown.TryInputRule(runs, 5, out result, out newCaret));
        }

        [Fact]
        public void ToPlain_ShowsVisibleText()
        {
            Assert.Equal("굵게 링크 코드 $x$", InlineMarkdown.ToPlain("**굵게** [링크](u) `코드` $x$"));
        }

        [Fact]
        public void InputRule_MathAndLink()
        {
            List<InlineRun> result;
            int newCaret;
            var math = new List<InlineRun> { T("값 $x^2$") };
            Assert.True(InlineMarkdown.TryInputRule(math, 7, out result, out newCaret));
            Assert.Equal("값 $x^2$", InlineMarkdown.Serialize(result));
            Assert.Equal(InlineRunKind.Raw, result.Last().Kind);
            var link = new List<InlineRun> { T("보기 [문서](a.md)") };
            Assert.True(InlineMarkdown.TryInputRule(link, 13, out result, out newCaret));
            Assert.Equal("보기 [문서](a.md)", InlineMarkdown.Serialize(result));
            Assert.Equal("a.md", result.Last().Link);
            Assert.Equal(5, newCaret);
            Assert.False(InlineMarkdown.TryInputRule(new List<InlineRun> { T("금액 $5 ~ $") }, 10, out result, out newCaret));
        }

        [Theory]
        [InlineData("앞 **굵게** 뒤", 2, 4)]   // '굵' → 원문 4
        [InlineData("앞 **굵게** 뒤", 4, 8)]   // 굵게 뒤 → 닫는 표식 앞(원문 8)이 아니라 다음 글자 ' '(원문 8)
        [InlineData("평문", 1, 1)]
        [InlineData("\\*별\\*", 1, 2)]
        public void PlainToSource_MapsVisibleOffsets(string markdown, int plain, int source)
        {
            Assert.Equal(source, InlineMarkdown.PlainToSource(markdown, plain));
            Assert.Equal(plain, InlineMarkdown.SourceToPlain(markdown, source));
        }

        [Theory]
        [InlineData("<b>굵게</b> 그리고 <i>기울임</i>", "**굵게** 그리고 *기울임*")]
        [InlineData("<strong>a</strong><em>b</em><del>c</del><code>d*e</code>", "**a***b*~~c~~`d*e`")]
        [InlineData("<a href=\"https://a.com/?x=1&amp;y=2\">링크</a>", "[링크](https://a.com/?x=1&y=2)")]
        [InlineData("<span style=\"font-weight:700\">구글 굵게</span>", "**구글 굵게**")]
        [InlineData("<b style=\"font-weight:normal;\" id=\"docs-internal-guid-1\"><span style=\"font-weight:700;\">굵게</span><span style=\"font-weight:400;\"> 보통</span></b>", "**굵게** 보통")]
        [InlineData("<i><span style=\"font-style:normal\">바로</span> 기울임</i>", "바로 *기울임*")]
        [InlineData("<p>첫 문단</p><p>둘째 <b>문단</b></p>", "첫 문단\n\n둘째 **문단**")]
        [InlineData("<h2>제목</h2><ul><li>하나</li><li>둘</li></ul>", "## 제목\n\n- 하나\n\n- 둘")]
        [InlineData("2 * 3 &lt;tag&gt;", "2 * 3 \\<tag>")]
        [InlineData("Version:0.9\r\nStartHTML:0\r\n<html><body><!--StartFragment--><b>조각</b><!--EndFragment--></body></html>", "**조각**")]
        public void ClipboardHtml_ToMarkdown(string html, string expected)
        {
            Assert.Equal(expected, ClipboardMarkdown.FromHtml(html));
        }

        [Fact]
        public void Merge_JoinsAdjacentCodeRuns_FromEditing()
        {
            // 서식 입력란에서 코드 안에 입력하면 조각이 나뉜다(울타리 정보가 없는 새 조각). 따로 쓰면 `a``b`가 되어 다시 읽을 때 글자가 바뀐다.
            var runs = new List<InlineRun>
            {
                new InlineRun { Text = "코드 ", Format = InlineFormat.Code, CodeFence = 1 },
                new InlineRun { Text = "**a**", Format = InlineFormat.Code },
                new InlineRun { Text = "안", Format = InlineFormat.Code, CodeFence = 1 },
                new InlineRun { Text = " 끝" },
            };
            var markdown = InlineMarkdown.Serialize(runs);
            Assert.Equal("`코드 **a**안` 끝", markdown);
            Assert.Equal("코드 **a**안 끝", InlineMarkdown.ToPlain(markdown));
        }

        [Fact]
        public void BareUrl_DetectedOnlyForDisplay()
        {
            var display = InlineMarkdown.Parse("주소 https://a.com/x. 끝", bareUrls: true);
            Assert.Equal("주소 |L(https://a.com/x):https://a.com/x|. 끝", Show(display));
            Assert.Equal("주소 https://a.com/x. 끝", InlineMarkdown.Serialize(display));
        }
    }
}

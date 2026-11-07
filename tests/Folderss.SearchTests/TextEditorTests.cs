using System;
using System.IO;
using System.Linq;
using System.Text;
using Folderss.Services;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>
    /// 일반 텍스트 편집기(builtin:text)의 UI 밖 계산: 인코딩 감지·저장 검사(<see cref="TextFileEncoding"/>),
    /// 찾기/바꾸기(<see cref="TextSearch"/>), 보기 설정 저장(<see cref="TextEditorSettings"/>).
    /// </summary>
    public sealed class TextEditorTests
    {
        // 공급자를 테스트에서 따로 등록하지 않는다 — TextFileEncoding이 스스로 등록해야 한다
        // (정적 초기화 순서 때문에 등록 전에 CP949를 만들어 형식 초기화가 실패한 적이 있음).
        private static Encoding Cp949 => TextFileEncoding.Cp949;

        // ── 인코딩 감지 ──────────────────────────────────────────────────

        [Fact]
        public void Decode_Utf8WithoutBom_IsUtf8()
        {
            var text = TextFileEncoding.Decode(new UTF8Encoding(false).GetBytes("한글 abc"), out var encoding);
            Assert.Equal("한글 abc", text);
            Assert.Equal("UTF-8", TextFileEncoding.DisplayName(encoding));
        }

        [Fact]
        public void Decode_Utf8Bom_KeepsBomForSave_AndStripsFromText()
        {
            var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("가나")).ToArray();
            var text = TextFileEncoding.Decode(bytes, out var encoding);
            Assert.Equal("가나", text);
            Assert.Equal("UTF-8 BOM", TextFileEncoding.DisplayName(encoding));
        }

        [Theory]
        [InlineData(false, "UTF-16 LE")]
        [InlineData(true, "UTF-16 BE")]
        public void Decode_Utf16Bom(bool bigEndian, string expected)
        {
            var encoding = new UnicodeEncoding(bigEndian, true);
            var bytes = encoding.GetPreamble().Concat(encoding.GetBytes("줄1\r\n줄2")).ToArray();
            var text = TextFileEncoding.Decode(bytes, out var detected);
            Assert.Equal("줄1\r\n줄2", text);
            Assert.Equal(expected, TextFileEncoding.DisplayName(detected));
        }

        [Fact]
        public void Decode_InvalidUtf8_FallsBackToCp949()
        {
            // BOM 없는 CP949 한글은 UTF-8로 읽으면 깨진다(이전 Text 뷰어의 문제).
            var bytes = Cp949.GetBytes("안녕하세요 로그");
            var text = TextFileEncoding.Decode(bytes, out var encoding);
            Assert.Equal("안녕하세요 로그", text);
            Assert.Equal("CP949", TextFileEncoding.DisplayName(encoding));
        }

        [Fact]
        public void Decode_AsciiOnly_IsUtf8()
        {
            TextFileEncoding.Decode(Encoding.ASCII.GetBytes("plain text"), out var encoding);
            Assert.Equal("UTF-8", TextFileEncoding.DisplayName(encoding));
        }

        [Fact]
        public void DecodeAs_ReinterpretsAndSkipsMatchingBom()
        {
            var bytes = Cp949.GetBytes("한글");
            Assert.Equal("한글", TextFileEncoding.DecodeAs(bytes, Cp949));
            var bom = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("x")).ToArray();
            Assert.Equal("x", TextFileEncoding.DecodeAs(bom, new UTF8Encoding(false)));
        }

        [Fact]
        public void FindUnencodable_Cp949_ReportsMissingCharacters()
        {
            Assert.Null(TextFileEncoding.FindUnencodable("한글 abc", Cp949));
            var lost = TextFileEncoding.FindUnencodable("한글 😀 abc ✓", Cp949);
            Assert.NotNull(lost);
            Assert.Contains("😀", lost);
            Assert.Null(TextFileEncoding.FindUnencodable("😀", new UTF8Encoding(false)));
        }

        [Fact]
        public void SameEncoding_ComparesCodePageAndBom()
        {
            Assert.True(TextFileEncoding.SameEncoding(new UTF8Encoding(false), new UTF8Encoding(false)));
            Assert.False(TextFileEncoding.SameEncoding(new UTF8Encoding(false), new UTF8Encoding(true)));
            Assert.False(TextFileEncoding.SameEncoding(new UTF8Encoding(false), Cp949));
        }

        [Fact]
        public void Choices_RoundTripDisplayNames()
        {
            var names = TextFileEncoding.Choices.Select(c => TextFileEncoding.DisplayName(c.Value)).ToList();
            Assert.Equal(new[] { "UTF-8", "UTF-8 BOM", "UTF-16 LE", "UTF-16 BE", "CP949" }, names);
        }

        // ── 줄 끝 ──────────────────────────────────────────────────────────

        [Theory]
        [InlineData("a\r\nb\r\nc", "\r\n")]
        [InlineData("a\nb\nc", "\n")]
        [InlineData("a\nb\r\nc\n", "\n")]
        [InlineData("한 줄", "\r\n")]
        [InlineData("", "\r\n")]
        public void DetectNewLine_UsesMajority(string text, string expected)
        {
            Assert.Equal(expected, TextFileEncoding.DetectNewLine(text));
        }

        [Theory]
        [InlineData("", 0)]
        [InlineData("abc", 3)]
        [InlineData("ab\r\nabcd\nx", 4)]
        [InlineData("a\rbcd\r\n", 3)]
        [InlineData("\r\n\r\n", 0)]
        public void MaxLineLength_ExcludesLineEnds(string text, int expected)
        {
            Assert.Equal(expected, TextFileEncoding.MaxLineLength(text));
        }

        [Fact]
        public void NormalizeNewLines_ConvertsMixedEndings()
        {
            Assert.Equal("a\nb\nc\nd", TextFileEncoding.NormalizeNewLines("a\r\nb\rc\nd", "\n"));
            Assert.Equal("a\r\nb\r\nc", TextFileEncoding.NormalizeNewLines("a\nb\r\nc", "\r\n"));
        }

        // ── 찾기 ───────────────────────────────────────────────────────────

        private static TextSearchOptions Options(string query, bool matchCase = false, bool wholeWord = false, bool regex = false)
        {
            return new TextSearchOptions { Query = query, MatchCase = matchCase, WholeWord = wholeWord, UseRegex = regex };
        }

        private static int[] Offsets(string text, TextSearchOptions options)
        {
            var re = TextSearch.BuildRegex(options, out var error);
            Assert.Null(error);
            return TextSearch.FindAll(text, re, out _).Select(m => m.Offset).ToArray();
        }

        [Fact]
        public void FindAll_LiteralIsEscaped_AndCaseInsensitiveByDefault()
        {
            Assert.Equal(new[] { 0, 7 }, Offsets("a.b(1) A.B(1)", Options("a.b(1)")));
            Assert.Empty(Offsets("axb(1)", Options("a.b(1)")));   // '.'은 아무 글자가 아니라 마침표
            Assert.Equal(new[] { 0 }, Offsets("a.b(1) A.B(1)", Options("a.b(1)", matchCase: true)));
        }

        [Fact]
        public void FindAll_WholeWord_RespectsKoreanAndUnderscore()
        {
            // 단어 경계는 글자(한글 포함)·숫자·밑줄 기준
            // "item"(0) ○, "items"(5) ×, "_item"(11) ×, "item"(17) ○
            Assert.Equal(new[] { 0, 17 }, Offsets("item items _item item", Options("item", wholeWord: true)));
            Assert.Equal(new[] { 0 }, Offsets("값 값이 값x", Options("값", wholeWord: true)));
        }

        [Fact]
        public void FindAll_Regex_SkipsEmptyMatches_AndIsMultiline()
        {
            Assert.Equal(new[] { 1 }, Offsets("a1b", Options(@"\d*", regex: true)));
            Assert.Empty(Offsets("abc", Options("^", regex: true)));
            Assert.Equal(new[] { 0, 4 }, Offsets("ab1\ncd2", Options(@"^\w", regex: true)));
        }

        [Fact]
        public void BuildRegex_InvalidPattern_ReturnsError()
        {
            var re = TextSearch.BuildRegex(Options("(abc", regex: true), out var error);
            Assert.Null(re);
            Assert.False(string.IsNullOrEmpty(error));
            Assert.Null(TextSearch.BuildRegex(Options(string.Empty), out error));
            Assert.Null(error);
        }

        [Fact]
        public void FindAll_StopsAtMaxMatches()
        {
            var text = new string('a', TextSearch.MaxMatches + 10);
            var re = TextSearch.BuildRegex(Options("a", matchCase: true), out _);
            var found = TextSearch.FindAll(text, re, out var truncated);
            Assert.True(truncated);
            Assert.Equal(TextSearch.MaxMatches, found.Count);
        }

        [Fact]
        public void NextAndPrevious_WrapAround()
        {
            var matches = new[] { new TextMatch(2, 1), new TextMatch(10, 1), new TextMatch(20, 1) };
            Assert.Equal(1, TextSearch.NextIndex(matches, 3));
            Assert.Equal(1, TextSearch.NextIndex(matches, 10));
            Assert.Equal(0, TextSearch.NextIndex(matches, 21));     // 끝을 지나면 처음으로
            Assert.Equal(1, TextSearch.PreviousIndex(matches, 20));
            Assert.Equal(2, TextSearch.PreviousIndex(matches, 2));  // 처음 앞이면 끝으로
            Assert.Equal(-1, TextSearch.NextIndex(Array.Empty<TextMatch>(), 0));
            Assert.Equal(1, TextSearch.IndexAt(matches, 10, 1));
            Assert.Equal(-1, TextSearch.IndexAt(matches, 10, 2));
        }

        // ── 바꾸기 ─────────────────────────────────────────────────────────

        [Fact]
        public void ReplacementAt_OnlyWhenSelectionIsAMatch()
        {
            var options = Options("foo");
            var re = TextSearch.BuildRegex(options, out _);
            Assert.Equal("bar", TextSearch.ReplacementAt("xx foo", 3, 3, re, options, "bar"));
            Assert.Null(TextSearch.ReplacementAt("xx foo", 2, 3, re, options, "bar"));   // 선택이 일치가 아님
        }

        [Fact]
        public void ReplacementAt_Regex_UsesGroupsAndContext()
        {
            var options = Options(@"(?<=id=)(\d+)", regex: true);
            var re = TextSearch.BuildRegex(options, out _);
            // lookbehind는 선택 앞 글자를 봐야 한다(선택만 잘라 맞추면 실패).
            Assert.Equal("[42]", TextSearch.ReplacementAt("id=42", 3, 2, re, options, "[$1]"));
        }

        [Fact]
        public void BuildReplacements_RegexWithEscapes()
        {
            var options = Options(@"(\w+)=(\w+)", regex: true);
            var re = TextSearch.BuildRegex(options, out _);
            var list = TextSearch.BuildReplacements("a=1;b=2", re, options, @"$2:$1\n", out var truncated);
            Assert.False(truncated);
            Assert.Equal(new[] { "1:a\n", "2:b\n" }, list.Select(p => p.Value).ToArray());
            Assert.Equal(new[] { 0, 4 }, list.Select(p => p.Key.Offset).ToArray());
        }

        [Fact]
        public void BuildReplacements_LiteralDollarIsNotSubstituted()
        {
            var options = Options("x");
            var re = TextSearch.BuildRegex(options, out _);
            var list = TextSearch.BuildReplacements("x x", re, options, "$1\\n", out _);
            Assert.All(list, p => Assert.Equal("$1\\n", p.Value));
        }

        // ── 보기 설정 ──────────────────────────────────────────────────────

        [Fact]
        public void Settings_RoundTrip_AndClampFontSize()
        {
            var path = Path.Combine(Path.GetTempPath(), "folderss-text-editor-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                new TextEditorSettings { WordWrap = true, FontSize = 18, ShowWhitespace = true }.Save(path);
                var loaded = TextEditorSettings.Load(path);
                Assert.True(loaded.WordWrap);
                Assert.True(loaded.ShowWhitespace);
                Assert.Equal(18, loaded.FontSize);

                File.WriteAllText(path, "{\"FontSize\": 500}");
                Assert.Equal(TextEditorSettings.MaxFontSize, TextEditorSettings.Load(path).FontSize);
                File.WriteAllText(path, "{ broken");
                Assert.Equal(TextEditorSettings.DefaultFontSize, TextEditorSettings.Load(path).FontSize);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}

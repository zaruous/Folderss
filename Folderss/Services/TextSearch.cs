using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Folderss.Services
{
    /// <summary>찾기 조건. 대소문자 구분·단어 단위·정규식.</summary>
    public sealed class TextSearchOptions
    {
        public string Query { get; set; }
        public bool MatchCase { get; set; }
        public bool WholeWord { get; set; }
        public bool UseRegex { get; set; }
    }

    /// <summary>찾은 위치(글 안의 시작 위치·길이).</summary>
    public struct TextMatch
    {
        public TextMatch(int offset, int length)
        {
            Offset = offset;
            Length = length;
        }

        public int Offset { get; }
        public int Length { get; }
        public int EndOffset => Offset + Length;
    }

    /// <summary>
    /// 일반 텍스트 편집기의 찾기·바꾸기 계산(UI와 분리 — <c>TextSearchTests</c>).
    /// 모든 조건을 정규식 하나로 바꿔 처리한다. 큰 파일에서 화면이 멈추지 않게 찾는 개수와 정규식 실행 시간에 상한을 둔다.
    /// </summary>
    public static class TextSearch
    {
        /// <summary>한 번에 찾는 최대 개수. 넘으면 결과에 <c>truncated</c>를 표시한다(강조·이동은 찾은 데까지).</summary>
        public const int MaxMatches = 100000;

        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

        /// <summary>조건으로 정규식을 만든다. 검색어가 비었으면 null, 정규식이 잘못됐으면 <paramref name="error"/>에 이유.</summary>
        public static Regex BuildRegex(TextSearchOptions options, out string error)
        {
            error = null;
            if (options == null || string.IsNullOrEmpty(options.Query))
                return null;

            var pattern = options.UseRegex ? options.Query : Regex.Escape(options.Query);
            if (options.WholeWord)
                pattern = @"(?<![\p{L}\p{N}_])(?:" + pattern + @")(?![\p{L}\p{N}_])";
            var regexOptions = RegexOptions.CultureInvariant | RegexOptions.Multiline;
            if (!options.MatchCase)
                regexOptions |= RegexOptions.IgnoreCase;
            try
            {
                return new Regex(pattern, regexOptions, RegexTimeout);
            }
            catch (ArgumentException ex)
            {
                error = ex.Message;
                return null;
            }
        }

        /// <summary>
        /// 글 전체에서 찾는다. 빈 일치(예: <c>^</c>)는 건너뛴다(강조·바꾸기 대상이 아님).
        /// 정규식이 시간을 넘기면 그때까지 찾은 것만 돌려주고 <paramref name="truncated"/>를 켠다.
        /// </summary>
        public static List<TextMatch> FindAll(string text, Regex regex, out bool truncated)
        {
            truncated = false;
            var result = new List<TextMatch>();
            if (regex == null || string.IsNullOrEmpty(text))
                return result;
            try
            {
                for (var m = regex.Match(text); m.Success; m = m.NextMatch())
                {
                    if (m.Length == 0)
                        continue;
                    if (result.Count >= MaxMatches)
                    {
                        truncated = true;
                        break;
                    }
                    result.Add(new TextMatch(m.Index, m.Length));
                }
            }
            catch (RegexMatchTimeoutException)
            {
                truncated = true;
            }
            return result;
        }

        /// <summary>
        /// 글 <paramref name="text"/>의 [<paramref name="offset"/>, +<paramref name="length"/>) 구간(현재 선택)을 바꿀 글.
        /// 정규식이면 <c>$1</c>·<c>${name}</c> 치환과 <c>\n</c>·<c>\t</c>를 해석한다. 앞뒤 문맥(lookbehind·단어 경계)을 보도록 전체 글에서 맞춘다.
        /// 그 구간이 지금 조건의 일치가 아니면 null(선택이 일치가 아니면 바꾸지 않음).
        /// </summary>
        public static string ReplacementAt(string text, int offset, int length, Regex regex, TextSearchOptions options, string replacement)
        {
            if (regex == null || text == null || length <= 0 || offset < 0 || offset + length > text.Length)
                return null;
            Match m;
            try
            {
                m = regex.Match(text, offset);
            }
            catch (RegexMatchTimeoutException)
            {
                return null;
            }
            if (!m.Success || m.Index != offset || m.Length != length)
                return null;
            return Substitute(m, options, replacement);
        }

        /// <summary>모두 바꾸기: 앞에서부터 (위치, 바꿀 글) 목록. 개수·시간 상한은 <see cref="FindAll"/>과 같다.</summary>
        public static List<KeyValuePair<TextMatch, string>> BuildReplacements(string text, Regex regex, TextSearchOptions options, string replacement, out bool truncated)
        {
            truncated = false;
            var result = new List<KeyValuePair<TextMatch, string>>();
            if (regex == null || string.IsNullOrEmpty(text))
                return result;
            try
            {
                for (var m = regex.Match(text); m.Success; m = m.NextMatch())
                {
                    if (m.Length == 0)
                        continue;
                    if (result.Count >= MaxMatches)
                    {
                        truncated = true;
                        break;
                    }
                    result.Add(new KeyValuePair<TextMatch, string>(new TextMatch(m.Index, m.Length), Substitute(m, options, replacement)));
                }
            }
            catch (RegexMatchTimeoutException)
            {
                truncated = true;
            }
            return result;
        }

        private static string Substitute(Match m, TextSearchOptions options, string replacement)
        {
            replacement = replacement ?? string.Empty;
            return options != null && options.UseRegex ? m.Result(UnescapeReplacement(replacement)) : replacement;
        }

        /// <summary>바꾸기 글의 <c>\n</c>·<c>\r</c>·<c>\t</c>·<c>\\</c>를 실제 글자로(정규식 모드에서만 쓴다).</summary>
        public static string UnescapeReplacement(string replacement)
        {
            if (string.IsNullOrEmpty(replacement) || replacement.IndexOf('\\') < 0)
                return replacement;
            var sb = new System.Text.StringBuilder(replacement.Length);
            for (int i = 0; i < replacement.Length; i++)
            {
                var c = replacement[i];
                if (c == '\\' && i + 1 < replacement.Length)
                {
                    var next = replacement[i + 1];
                    switch (next)
                    {
                        case 'n': sb.Append('\n'); i++; continue;
                        case 'r': sb.Append('\r'); i++; continue;
                        case 't': sb.Append('\t'); i++; continue;
                        case '\\': sb.Append('\\'); i++; continue;
                    }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>정렬된 일치 목록에서 <paramref name="offset"/> 이후(포함) 첫 일치 번호. 없으면 처음으로 돌아가 0, 목록이 비면 -1.</summary>
        public static int NextIndex(IReadOnlyList<TextMatch> matches, int offset)
        {
            if (matches == null || matches.Count == 0)
                return -1;
            int lo = 0, hi = matches.Count;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (matches[mid].Offset < offset)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            return lo < matches.Count ? lo : 0;
        }

        /// <summary><paramref name="offset"/>보다 앞에서 시작하는 마지막 일치 번호. 없으면 끝으로 돌아가 마지막, 목록이 비면 -1.</summary>
        public static int PreviousIndex(IReadOnlyList<TextMatch> matches, int offset)
        {
            if (matches == null || matches.Count == 0)
                return -1;
            int lo = 0, hi = matches.Count;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (matches[mid].Offset < offset)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            return lo > 0 ? lo - 1 : matches.Count - 1;
        }

        /// <summary><paramref name="offset"/>에서 시작하는 일치 번호(현재 선택이 몇 번째인지 표시용). 없으면 -1.</summary>
        public static int IndexAt(IReadOnlyList<TextMatch> matches, int offset, int length)
        {
            if (matches == null || matches.Count == 0)
                return -1;
            var i = NextIndex(matches, offset);
            return i >= 0 && matches[i].Offset == offset && matches[i].Length == length ? i : -1;
        }
    }
}

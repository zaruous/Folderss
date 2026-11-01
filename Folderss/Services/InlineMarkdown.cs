using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Folderss.Services
{
    /// <summary>줄 안 서식. 코드는 다른 서식과 겹칠 수 있다(<c>**`x`**</c>).</summary>
    [Flags]
    public enum InlineFormat
    {
        None = 0,
        Bold = 1,
        Italic = 2,
        Strike = 4,
        Code = 8,
    }

    public enum InlineRunKind
    {
        /// <summary>보이는 글자. 직렬화할 때 필요한 만큼 이스케이프한다.</summary>
        Text,
        /// <summary>해석하지 않는 Markdown 원문(이미지 <c>![..](..)</c>, HTML 태그, 엔터티 등). 그대로 쓴다.</summary>
        Raw,
        /// <summary>줄 안 수식 <c>$...$</c>. <see cref="InlineRun.Text"/>는 LaTeX.</summary>
        Math,
    }

    /// <summary>
    /// 줄 안 Markdown의 한 조각(같은 서식이 이어진 글자). 문단은 조각의 평면 목록이다 — 표시(TextBlock Inline), 편집(RichTextBox),
    /// 서식 적용·입력 단축 변환이 모두 이 목록으로 오간다. 표식 모양(<c>**</c>/<c>__</c>)·코드 울타리는 원문 왕복을 위해 기억한다.
    /// </summary>
    public sealed class InlineRun
    {
        public string Text = string.Empty;
        public InlineFormat Format;
        public InlineRunKind Kind;

        /// <summary>링크 주소(<c>[글자](주소)</c>의 주소, 쓴 그대로 — <c>&lt;...&gt;</c> 포함 가능). 링크가 아니면 null.</summary>
        public string Link;

        /// <summary>링크 제목(따옴표 포함 원문, 예: <c>"제목"</c>). 없으면 null.</summary>
        public string LinkTitle;

        /// <summary>링크 바깥에서 연 서식(<c>**[글자](u)**</c>의 굵게) — 원문 왕복용. 편집기에서 새로 만든 링크는 안쪽(<c>[**글자**](u)</c>).</summary>
        public InlineFormat LinkOuter;

        /// <summary><c>&lt;https://...&gt;</c> 자동 링크.</summary>
        public bool Autolink;

        /// <summary>표시 전용: 글자 그대로의 URL(맨 URL). 직렬화는 글자 그대로.</summary>
        public bool BareUrl;

        public string BoldMarker = "**";
        public string ItalicMarker = "*";
        public string StrikeMarker = "~~";

        /// <summary>코드 울타리 길이(0이면 자동)와 앞뒤 공백 덧붙임 — 원문 왕복용.</summary>
        public int CodeFence;
        public bool CodePad;

        /// <summary>범위 속성(#76 9번, Markdown 밖 — 사이드카 <c>marks</c>): 밑줄. 직렬화(<see cref="InlineMarkdown.Serialize"/>)는 무시한다.</summary>
        public bool Underline;

        /// <summary>범위 속성: 글자 색 이름(<see cref="TextMarks.ColorNames"/>). 없으면 null.</summary>
        public string TextColor;

        /// <summary>범위 속성: 배경색(형광펜) 이름. 없으면 null.</summary>
        public string Highlight;

        /// <summary>범위 속성(밑줄·글자 색·배경색)이 하나라도 있는지.</summary>
        public bool HasMarks => Underline || TextColor != null || Highlight != null;

        public bool Has(InlineFormat f) => (Format & f) == f;

        /// <summary>열 링크 주소(<c>&lt;&gt;</c> 벗김, 자동 링크 메일은 mailto:).</summary>
        public string Url
        {
            get
            {
                if (Link == null)
                    return null;
                var url = Link.Length >= 2 && Link[0] == '<' && Link[Link.Length - 1] == '>' ? Link.Substring(1, Link.Length - 2) : Link;
                if (Autolink && url.IndexOf(':') < 0 && url.IndexOf('@') > 0)
                    url = "mailto:" + url;
                return url;
            }
        }

        public InlineRun Clone()
        {
            return (InlineRun)MemberwiseClone();
        }

        /// <summary>글자만 다르고 나머지(서식·링크·표식 모양)가 같은지 — 이웃 조각을 합칠 수 있는지.</summary>
        public bool SameStyle(InlineRun other)
        {
            return other != null && Kind == other.Kind && Format == other.Format && Link == other.Link && LinkTitle == other.LinkTitle &&
                   Autolink == other.Autolink && BareUrl == other.BareUrl && LinkOuter == other.LinkOuter &&
                   Underline == other.Underline && TextColor == other.TextColor && Highlight == other.Highlight &&
                   (!Has(InlineFormat.Bold) || BoldMarker == other.BoldMarker) &&
                   (!Has(InlineFormat.Italic) || ItalicMarker == other.ItalicMarker) &&
                   (!Has(InlineFormat.Strike) || StrikeMarker == other.StrikeMarker) &&
                   (!Has(InlineFormat.Code) || (CodeFence == other.CodeFence && CodePad == other.CodePad));
        }

        public override string ToString()
        {
            return Kind + ":" + Format + (Link != null ? "@" + Link : "") + ":" + Text;
        }
    }

    /// <summary>
    /// 블록 편집기 줄 안 Markdown(#76): 굵게 <c>**</c>·기울임 <c>*</c>·취소선 <c>~~</c>·코드 <c>`</c>·링크 <c>[글자](주소)</c>를 조각 목록으로 읽고 다시 쓴다.
    /// CommonMark의 강조 규칙(왼쪽·오른쪽 붙음, 3의 배수 규칙, 밑줄의 단어 안 금지)과 GFM 취소선을 따른다. 이미지·HTML·엔터티는 원문 조각,
    /// 줄 안 수식 <c>$...$</c>(Pandoc 규칙)은 수식 조각으로 보존한다. WPF 없음(테스트 프로젝트에서 소스 링크).
    /// 왕복: 정규형 문자열 s는 <c>Serialize(Parse(s)) == s</c>, 아무 조각 목록 r은 <c>Parse(Serialize(r))</c>가 r과 같은 서식이 되도록 필요한 만큼만 이스케이프한다.
    /// </summary>
    public static class InlineMarkdown
    {
        private const string Punctuation = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";

        private static readonly Regex HtmlTag = new Regex(@"\G(?:<[A-Za-z][A-Za-z0-9-]*(?:\s+[A-Za-z_:][A-Za-z0-9_.:-]*(?:\s*=\s*(?:[^\s""'=<>`]+|'[^']*'|""[^""]*""))?)*\s*/?>|</[A-Za-z][A-Za-z0-9-]*\s*>|<!--[\s\S]*?-->)", RegexOptions.Compiled);
        private static readonly Regex UriAutolink = new Regex(@"\G<([A-Za-z][A-Za-z0-9+.-]{1,31}:[^\s<>]*)>", RegexOptions.Compiled);
        private static readonly Regex EmailAutolink = new Regex(@"\G<([A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*)>", RegexOptions.Compiled);
        private static readonly Regex Entity = new Regex(@"\G&(?:[A-Za-z][A-Za-z0-9]{1,31}|#[0-9]{1,7}|#[xX][0-9a-fA-F]{1,6});", RegexOptions.Compiled);
        private static readonly Regex LinkInput = new Regex(@"(?<![!\\])\[([^\[\]\n]+)\]\(([^()\s]+)\)$", RegexOptions.Compiled);
        private static readonly Regex BareUrlPattern = new Regex(@"(?:https?://|www\.)[^\s<>()\[\]""']+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static bool IsPunctuation(char c)
        {
            return Punctuation.IndexOf(c) >= 0 || char.IsPunctuation(c) || char.IsSymbol(c);
        }

        private static bool IsAsciiPunctuation(char c) => Punctuation.IndexOf(c) >= 0;

        /// <summary>서식 표식이 하나도 없을 법한 글자인지(빠른 길 — 표시할 때 파서를 건너뜀).</summary>
        public static bool IsPlain(string text)
        {
            if (string.IsNullOrEmpty(text))
                return true;
            foreach (var c in text)
            {
                switch (c)
                {
                    case '*': case '_': case '~': case '`': case '[': case ']': case '\\': case '<': case '&': case '$': case '!':
                        return false;
                }
            }
            return true;
        }

        // ── 읽기 ──────────────────────────────────────────────────────────

        private enum TokenType { Text, Delim, Code, Raw, Math, Autolink, Bracket, LinkEnd }

        private sealed class Token
        {
            public TokenType Type;
            public string Text = string.Empty;
            public char Char;
            public int Count;
            public int OrigCount;
            public bool CanOpen;
            public bool CanClose;
            public bool Active = true;
            public bool Image;
            public int SourceStart;
            public int CodeFence;
            public bool CodePad;
            public string Link;
            public string LinkTitle;
            // 짝지은 강조: 이 구분자(여는 쪽)에서 시작하는 서식들(안쪽부터)
            public List<KeyValuePair<InlineFormat, string>> Opens;
            public List<KeyValuePair<InlineFormat, string>> Closes;
        }

        /// <summary>줄 안 Markdown을 조각 목록으로 읽는다. <paramref name="bareUrls"/>면 맨 URL도 링크 조각으로 표시한다(표시 전용).</summary>
        public static List<InlineRun> Parse(string text, bool bareUrls = false)
        {
            var tokens = Tokenize(text ?? string.Empty);
            ProcessEmphasis(tokens, 0, tokens.Count);
            var runs = Flatten(tokens);
            if (bareUrls)
                runs = DetectBareUrls(runs);
            return Merge(runs);
        }

        /// <summary>편집기용: 수식 조각을 원문(<c>$...$</c>) 조각으로 바꿔 읽는다(편집할 때는 원문으로 보여 줌).</summary>
        public static List<InlineRun> ParseForEditing(string text)
        {
            var runs = Parse(text);
            foreach (var run in runs)
            {
                if (run.Kind == InlineRunKind.Math)
                {
                    run.Kind = InlineRunKind.Raw;
                    run.Text = "$" + run.Text + "$";
                }
            }
            return Merge(runs);
        }

        private static List<Token> Tokenize(string s)
        {
            var tokens = new List<Token>();
            var text = new StringBuilder();
            var textStart = 0;
            Action flush = () =>
            {
                if (text.Length > 0)
                {
                    tokens.Add(new Token { Type = TokenType.Text, Text = text.ToString(), SourceStart = textStart });
                    text.Clear();
                }
            };

            var i = 0;
            while (i < s.Length)
            {
                var c = s[i];
                if (text.Length == 0)
                    textStart = i;
                if (c == '\\' && i + 1 < s.Length && IsAsciiPunctuation(s[i + 1]))
                {
                    text.Append(s[i + 1]);
                    i += 2;
                    continue;
                }
                if (c == '`')
                {
                    var n = RunLength(s, i, '`');
                    var close = FindBacktickRun(s, i + n, n);
                    if (close >= 0)
                    {
                        flush();
                        var content = s.Substring(i + n, close - i - n).Replace("\r\n", " ").Replace('\n', ' ');
                        var pad = content.Length >= 2 && content[0] == ' ' && content[content.Length - 1] == ' ' && content.Trim(' ').Length > 0;
                        if (pad)
                            content = content.Substring(1, content.Length - 2);
                        tokens.Add(new Token { Type = TokenType.Code, Text = content, CodeFence = n, CodePad = pad, SourceStart = i });
                        i = close + n;
                        continue;
                    }
                    text.Append(s, i, n);
                    i += n;
                    continue;
                }
                if (c == '<')
                {
                    var m = UriAutolink.Match(s, i);
                    if (!m.Success)
                        m = EmailAutolink.Match(s, i);
                    if (m.Success)
                    {
                        flush();
                        tokens.Add(new Token { Type = TokenType.Autolink, Text = m.Groups[1].Value, SourceStart = i });
                        i += m.Length;
                        continue;
                    }
                    m = HtmlTag.Match(s, i);
                    if (m.Success)
                    {
                        flush();
                        tokens.Add(new Token { Type = TokenType.Raw, Text = m.Value, SourceStart = i });
                        i += m.Length;
                        continue;
                    }
                }
                if (c == '&')
                {
                    var m = Entity.Match(s, i);
                    if (m.Success)
                    {
                        flush();
                        tokens.Add(new Token { Type = TokenType.Raw, Text = m.Value, SourceStart = i });
                        i += m.Length;
                        continue;
                    }
                }
                if (c == '$')
                {
                    int end;
                    if (TryMath(s, i, out end))
                    {
                        flush();
                        tokens.Add(new Token { Type = TokenType.Math, Text = s.Substring(i + 1, end - i - 1), SourceStart = i });
                        i = end + 1;
                        continue;
                    }
                    var n = RunLength(s, i, '$');
                    text.Append(s, i, n);
                    i += n;
                    continue;
                }
                if (c == '*' || c == '_' || c == '~')
                {
                    flush();
                    var n = RunLength(s, i, c);
                    var before = i > 0 ? s[i - 1] : ' ';
                    var after = i + n < s.Length ? s[i + n] : ' ';
                    var beforeWs = char.IsWhiteSpace(before);
                    var afterWs = char.IsWhiteSpace(after);
                    var beforeP = IsPunctuation(before);
                    var afterP = IsPunctuation(after);
                    var left = !afterWs && (!afterP || beforeWs || beforeP);
                    var right = !beforeWs && (!beforeP || afterWs || afterP);
                    bool canOpen, canClose;
                    if (c == '_')
                    {
                        canOpen = left && (!right || beforeP);
                        canClose = right && (!left || afterP);
                    }
                    else
                    {
                        canOpen = left;
                        canClose = right;
                    }
                    if (c == '~' && n > 2)
                        canOpen = canClose = false;
                    tokens.Add(new Token { Type = TokenType.Delim, Char = c, Count = n, OrigCount = n, CanOpen = canOpen, CanClose = canClose, Text = new string(c, n), SourceStart = i });
                    i += n;
                    continue;
                }
                if (c == '!' && i + 1 < s.Length && s[i + 1] == '[')
                {
                    flush();
                    tokens.Add(new Token { Type = TokenType.Bracket, Image = true, Text = "![", SourceStart = i });
                    i += 2;
                    continue;
                }
                if (c == '[')
                {
                    flush();
                    tokens.Add(new Token { Type = TokenType.Bracket, Text = "[", SourceStart = i });
                    i++;
                    continue;
                }
                if (c == ']')
                {
                    flush();
                    if (TryCloseBracket(s, ref i, tokens))
                        continue;
                    tokens.Add(new Token { Type = TokenType.Text, Text = "]", SourceStart = i });
                    i++;
                    continue;
                }
                text.Append(c);
                i++;
            }
            flush();
            // 짝 없는 [ 는 글자
            foreach (var t in tokens)
                if (t.Type == TokenType.Bracket)
                    t.Type = TokenType.Text;
            return tokens;
        }

        private static int RunLength(string s, int i, char c)
        {
            var n = 0;
            while (i + n < s.Length && s[i + n] == c)
                n++;
            return n;
        }

        private static int FindBacktickRun(string s, int from, int n)
        {
            var i = from;
            while (i < s.Length)
            {
                var k = s.IndexOf('`', i);
                if (k < 0)
                    return -1;
                var len = RunLength(s, k, '`');
                if (len == n)
                    return k;
                i = k + len;
            }
            return -1;
        }

        /// <summary>
        /// 줄 안 수식(Pandoc 규칙): 여는 <c>$</c> 바로 뒤·닫는 <c>$</c> 바로 앞은 공백이 아니고 닫는 <c>$</c> 바로 뒤는 숫자가 아니어야 한다
        /// ("$5 ~ $10" 같은 금액은 수식이 아님). <c>\$</c>는 글자이고 줄바꿈을 넘지 않는다.
        /// </summary>
        public static bool TryMath(string s, int open, out int close)
        {
            close = -1;
            if (open > 0 && s[open - 1] == '\\')
                return false;
            if (open + 1 >= s.Length || char.IsWhiteSpace(s[open + 1]) || s[open + 1] == '$')
                return false;
            if (open > 0 && s[open - 1] == '$')
                return false;
            for (var k = s.IndexOf('$', open + 2); k >= 0; k = s.IndexOf('$', k + 1))
            {
                if (s.IndexOf('\n', open + 1, k - open - 1) >= 0)
                    return false;
                if (s[k - 1] == '\\' || char.IsWhiteSpace(s[k - 1]) || (k + 1 < s.Length && char.IsDigit(s[k + 1])))
                    continue;
                close = k;
                return true;
            }
            return false;
        }

        /// <summary>문자열에서 첫 줄 안 수식의 여는·닫는 <c>$</c> 위치(수식 표시용, 코드 안도 따지지 않는 단순 검색).</summary>
        public static bool FindMath(string s, int from, out int start, out int end)
        {
            start = end = -1;
            if (string.IsNullOrEmpty(s))
                return false;
            for (var open = s.IndexOf('$', from); open >= 0; open = s.IndexOf('$', open + 1))
            {
                int close;
                if (TryMath(s, open, out close))
                {
                    start = open;
                    end = close;
                    return true;
                }
            }
            return false;
        }

        private static bool TryCloseBracket(string s, ref int i, List<Token> tokens)
        {
            var openerIndex = -1;
            for (var k = tokens.Count - 1; k >= 0; k--)
            {
                if (tokens[k].Type == TokenType.Bracket)
                {
                    openerIndex = k;
                    break;
                }
            }
            if (openerIndex < 0)
                return false;
            var opener = tokens[openerIndex];
            if (!opener.Active)
            {
                opener.Type = TokenType.Text;
                return false;
            }

            string dest, title;
            int end;
            if (!TryLinkTail(s, i + 1, out dest, out title, out end))
            {
                opener.Type = TokenType.Text;
                return false;
            }

            if (opener.Image)
            {
                var src = s.Substring(opener.SourceStart, end - opener.SourceStart);
                tokens.RemoveRange(openerIndex, tokens.Count - openerIndex);
                tokens.Add(new Token { Type = TokenType.Raw, Text = src, SourceStart = opener.SourceStart });
                i = end;
                return true;
            }

            // 링크 글자 안의 강조를 먼저 짝짓고, 링크 안에는 링크가 없으므로 앞의 [ 를 끈다.
            ProcessEmphasis(tokens, openerIndex + 1, tokens.Count);
            opener.Type = TokenType.Text;
            opener.Text = string.Empty;
            opener.Link = dest;
            opener.LinkTitle = title;
            opener.Char = '[';
            tokens.Add(new Token { Type = TokenType.LinkEnd, Text = string.Empty, SourceStart = i });
            for (var k = 0; k < openerIndex; k++)
                if (tokens[k].Type == TokenType.Bracket && !tokens[k].Image)
                    tokens[k].Active = false;
            i = end;
            return true;
        }

        /// <summary><c>](</c> 뒤 주소·제목·<c>)</c>를 읽는다. <paramref name="at"/>은 <c>(</c> 위치.</summary>
        private static bool TryLinkTail(string s, int at, out string dest, out string title, out int end)
        {
            dest = title = null;
            end = -1;
            if (at >= s.Length || s[at] != '(')
                return false;
            var i = at + 1;
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t'))
                i++;
            var destStart = i;
            if (i < s.Length && s[i] == '<')
            {
                var k = i + 1;
                while (k < s.Length && s[k] != '>' && s[k] != '<' && s[k] != '\n')
                {
                    if (s[k] == '\\' && k + 1 < s.Length)
                        k++;
                    k++;
                }
                if (k >= s.Length || s[k] != '>')
                    return false;
                i = k + 1;
            }
            else
            {
                var depth = 0;
                while (i < s.Length)
                {
                    var c = s[i];
                    if (c == '\\' && i + 1 < s.Length && IsAsciiPunctuation(s[i + 1]))
                    {
                        i += 2;
                        continue;
                    }
                    if (char.IsWhiteSpace(c) || char.IsControl(c))
                        break;
                    if (c == '(')
                        depth++;
                    else if (c == ')')
                    {
                        if (depth == 0)
                            break;
                        depth--;
                    }
                    i++;
                }
                if (depth != 0)
                    return false;
            }
            dest = s.Substring(destStart, i - destStart);
            var afterDest = i;
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t'))
                i++;
            if (i < s.Length && i > afterDest && (s[i] == '"' || s[i] == '\'' || s[i] == '('))
            {
                var closeChar = s[i] == '(' ? ')' : s[i];
                var k = i + 1;
                while (k < s.Length && s[k] != closeChar)
                {
                    if (s[k] == '\\' && k + 1 < s.Length)
                        k++;
                    k++;
                }
                if (k >= s.Length)
                    return false;
                title = s.Substring(i, k - i + 1);
                i = k + 1;
                while (i < s.Length && (s[i] == ' ' || s[i] == '\t'))
                    i++;
            }
            if (i >= s.Length || s[i] != ')')
                return false;
            if (dest.Length == 0 && title == null && destStart == at + 1)
            {
                // [글자]() — CommonMark에서도 빈 주소 링크
            }
            end = i + 1;
            return true;
        }

        /// <summary>CommonMark "process emphasis": [from, to) 구간의 구분자를 짝짓는다. 짝지은 서식은 구분자 토큰에 기록한다.</summary>
        private static void ProcessEmphasis(List<Token> tokens, int from, int to)
        {
            // 여는 쪽 하한(같은 문자·닫는 쪽 성질별) — 단순화: 매번 처음까지 찾되 결과는 같다.
            for (var closer = from; closer < to; closer++)
            {
                var c = tokens[closer];
                if (c.Type != TokenType.Delim || !c.Active || !c.CanClose || c.Count == 0)
                    continue;
                while (c.Count > 0)
                {
                    var openerIndex = -1;
                    for (var k = closer - 1; k >= from; k--)
                    {
                        var o = tokens[k];
                        if (o.Type != TokenType.Delim || !o.Active || o.Char != c.Char || !o.CanOpen || o.Count == 0)
                            continue;
                        if (c.Char == '~')
                        {
                            if (o.Count != c.Count)
                                continue;
                        }
                        else if ((o.CanClose || c.CanOpen) && (o.OrigCount + c.OrigCount) % 3 == 0 &&
                                 !(o.OrigCount % 3 == 0 && c.OrigCount % 3 == 0))
                        {
                            continue;
                        }
                        openerIndex = k;
                        break;
                    }
                    if (openerIndex < 0)
                        break;

                    var opener = tokens[openerIndex];
                    var use = c.Char == '~' ? c.Count : (c.Count >= 2 && opener.Count >= 2 ? 2 : 1);
                    var format = c.Char == '~' ? InlineFormat.Strike : use == 2 ? InlineFormat.Bold : InlineFormat.Italic;
                    var marker = new string(c.Char, use);
                    if (opener.Opens == null)
                        opener.Opens = new List<KeyValuePair<InlineFormat, string>>();
                    if (c.Closes == null)
                        c.Closes = new List<KeyValuePair<InlineFormat, string>>();
                    opener.Opens.Add(new KeyValuePair<InlineFormat, string>(format, marker));
                    c.Closes.Add(new KeyValuePair<InlineFormat, string>(format, marker));
                    opener.Count -= use;
                    c.Count -= use;
                    // 사이의 구분자는 글자로
                    for (var k = openerIndex + 1; k < closer; k++)
                        if (tokens[k].Type == TokenType.Delim)
                            tokens[k].Active = false;
                }
            }
            for (var k = from; k < to; k++)
                if (tokens[k].Type == TokenType.Delim)
                    tokens[k].Active = false;
        }

        private sealed class OpenFormat
        {
            public InlineFormat Format;
            public string Marker;
        }

        private static List<InlineRun> Flatten(List<Token> tokens)
        {
            var runs = new List<InlineRun>();
            var stack = new List<OpenFormat>();
            string link = null, linkTitle = null;
            var inLink = false;
            var linkOuter = InlineFormat.None;

            Func<InlineRun> make = () =>
            {
                var run = new InlineRun();
                foreach (var f in stack)
                {
                    run.Format |= f.Format;
                    if (f.Format == InlineFormat.Bold) run.BoldMarker = f.Marker;
                    else if (f.Format == InlineFormat.Italic) run.ItalicMarker = f.Marker;
                    else if (f.Format == InlineFormat.Strike) run.StrikeMarker = f.Marker;
                }
                if (inLink)
                {
                    run.Link = link;
                    run.LinkTitle = linkTitle;
                    run.LinkOuter = linkOuter;
                }
                return run;
            };

            foreach (var t in tokens)
            {
                switch (t.Type)
                {
                    case TokenType.Delim:
                    {
                        // 여는 구분자: 남은 글자(바깥쪽) → 서식 열기(바깥 것부터 = 나중에 짝지은 것부터).
                        // 닫는 구분자: 서식 닫기(안쪽 = 먼저 짝지은 것부터) → 남은 글자.
                        if (t.Closes != null)
                        {
                            foreach (var close in t.Closes)
                            {
                                var at = stack.FindLastIndex(f => f.Format == close.Key && f.Marker == close.Value);
                                if (at >= 0)
                                    stack.RemoveAt(at);
                            }
                        }
                        if (t.Count > 0)
                        {
                            var run = make();
                            run.Text = new string(t.Char, t.Count);
                            runs.Add(run);
                        }
                        if (t.Opens != null)
                        {
                            for (var k = t.Opens.Count - 1; k >= 0; k--)
                                stack.Add(new OpenFormat { Format = t.Opens[k].Key, Marker = t.Opens[k].Value });
                        }
                        break;
                    }
                    case TokenType.Text:
                    {
                        if (t.Char == '[' && t.Link != null)
                        {
                            inLink = true;
                            link = t.Link;
                            linkTitle = t.LinkTitle;
                            linkOuter = InlineFormat.None;
                            foreach (var f in stack)
                                linkOuter |= f.Format;
                            break;
                        }
                        if (t.Text.Length == 0)
                            break;
                        var run = make();
                        run.Text = t.Text;
                        runs.Add(run);
                        break;
                    }
                    case TokenType.LinkEnd:
                        inLink = false;
                        link = linkTitle = null;
                        break;
                    case TokenType.Code:
                    {
                        var run = make();
                        run.Format |= InlineFormat.Code;
                        run.Text = t.Text;
                        run.CodeFence = t.CodeFence;
                        run.CodePad = t.CodePad;
                        runs.Add(run);
                        break;
                    }
                    case TokenType.Raw:
                    {
                        var run = make();
                        run.Kind = InlineRunKind.Raw;
                        run.Text = t.Text;
                        runs.Add(run);
                        break;
                    }
                    case TokenType.Math:
                    {
                        var run = make();
                        run.Kind = InlineRunKind.Math;
                        run.Text = t.Text;
                        runs.Add(run);
                        break;
                    }
                    case TokenType.Autolink:
                    {
                        var run = make();
                        run.Text = t.Text;
                        run.Link = t.Text;
                        run.LinkTitle = null;
                        run.Autolink = true;
                        runs.Add(run);
                        break;
                    }
                }
            }
            return runs;
        }

        private static List<InlineRun> DetectBareUrls(List<InlineRun> runs)
        {
            var result = new List<InlineRun>();
            foreach (var run in runs)
            {
                if (run.Kind != InlineRunKind.Text || run.Link != null || run.Has(InlineFormat.Code) || run.Text.IndexOf("://", StringComparison.Ordinal) < 0 && run.Text.IndexOf("www.", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    result.Add(run);
                    continue;
                }
                var pos = 0;
                foreach (Match m in BareUrlPattern.Matches(run.Text))
                {
                    var url = m.Value.TrimEnd('.', ',', ';', ':', '!', '?');
                    if (url.Length == 0)
                        continue;
                    if (m.Index > pos)
                    {
                        var before = run.Clone();
                        before.Text = run.Text.Substring(pos, m.Index - pos);
                        result.Add(before);
                    }
                    var link = run.Clone();
                    link.Text = url;
                    link.Link = url.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + url : url;
                    link.BareUrl = true;
                    result.Add(link);
                    pos = m.Index + url.Length;
                }
                if (pos < run.Text.Length)
                {
                    var after = run.Clone();
                    after.Text = run.Text.Substring(pos);
                    result.Add(after);
                }
            }
            return result;
        }

        /// <summary>이웃한 같은 모양 조각을 합치고 빈 조각을 버린다.</summary>
        public static List<InlineRun> Merge(IEnumerable<InlineRun> runs)
        {
            var result = new List<InlineRun>();
            foreach (var run in runs)
            {
                if (run == null || string.IsNullOrEmpty(run.Text) && run.Kind == InlineRunKind.Text && !run.Has(InlineFormat.Code))
                    continue;
                var last = result.Count > 0 ? result[result.Count - 1] : null;
                // 붙은 코드 조각도 잇는다 — 코드 안에서 입력하면 새 Run이 생겨 조각이 나뉘는데, 따로 쓰면(`a``b`) 다시 읽을 때
                // 한 코드로 합쳐져 글자가 바뀐다. 읽기로는 붙은 코드 조각이 생기지 않으므로(`a``b`는 코드 하나) 울타리 모양만 무시하고 잇는다.
                if (last != null && last.Kind == InlineRunKind.Text && run.Kind == InlineRunKind.Text && !run.Autolink && !last.Autolink &&
                    (run.Has(InlineFormat.Code) ? last.Has(InlineFormat.Code) && SameIgnoringCodeFence(last, run) : last.SameStyle(run)))
                {
                    var merged = last.Clone();
                    merged.Text = last.Text + run.Text;
                    result[result.Count - 1] = merged;
                    continue;
                }
                result.Add(run.Clone());
            }
            return result;
        }

        private static bool SameIgnoringCodeFence(InlineRun a, InlineRun b)
        {
            var c = b.Clone();
            c.CodeFence = a.CodeFence;
            c.CodePad = a.CodePad;
            return a.SameStyle(c);
        }

        // ── 쓰기 ──────────────────────────────────────────────────────────

        /// <summary>조각 목록을 줄 안 Markdown으로 쓴다. 다시 읽었을 때 같은 서식이 되도록 필요한 글자만 이스케이프한다.</summary>
        public static string Serialize(IList<InlineRun> runs)
        {
            // 범위 속성(밑줄·색)은 Markdown에 쓰지 않는다 — 빼고, 범위 경계에서 나뉜 조각(코드 포함)을 다시 잇는다.
            if (runs.Any(r => r != null && r.HasMarks))
                runs = JoinSplitPieces(TextMarks.Strip(runs.Where(r => r != null)));
            var normalized = Normalize(runs);
            if (normalized.Count == 0)
                return string.Empty;
            // 이스케이프는 필요한 만큼만: 없이 → 강조 표식(* _ ~) 말고 나머지 특수 글자만 → 전부. 다시 읽어 같은 서식이 되는 첫 결과.
            var minimal = Write(normalized, false);
            if (Equivalent(Parse(minimal), normalized))
                return minimal;
            try
            {
                _escapeEmphasis = false;
                var partial = Write(normalized, true);
                if (Equivalent(Parse(partial), normalized))
                    return partial;
            }
            finally
            {
                _escapeEmphasis = true;
            }
            return Write(normalized, true);
        }

        [ThreadStatic]
        private static bool _escapeEmphasisSet;
        [ThreadStatic]
        private static bool _escapeEmphasisValue;

        /// <summary>전부 이스케이프할 때 강조 표식(* _ ~)도 이스케이프할지(기본 true).</summary>
        private static bool _escapeEmphasis
        {
            get => !_escapeEmphasisSet || _escapeEmphasisValue;
            set
            {
                _escapeEmphasisSet = true;
                _escapeEmphasisValue = value;
            }
        }

        /// <summary>범위 경계에서 나뉜 같은 모양 조각(코드·원문 포함)을 잇는다.</summary>
        private static List<InlineRun> JoinSplitPieces(List<InlineRun> runs)
        {
            var result = new List<InlineRun>(runs.Count);
            foreach (var run in runs)
            {
                var last = result.Count > 0 ? result[result.Count - 1] : null;
                if (last != null && last.Kind == run.Kind && run.Kind != InlineRunKind.Math && !last.Autolink && !run.Autolink && last.SameStyle(run))
                {
                    last.Text += run.Text;
                    continue;
                }
                result.Add(run.Clone());
            }
            return result;
        }

        /// <summary>서식 경계의 공백을 서식 밖으로 빼고(<c>**a **</c>는 닫히지 않으므로) 이웃 조각을 합친다.</summary>
        private static string StripAngle(string link)
        {
            return link != null && link.Length >= 2 && link[0] == '<' && link[link.Length - 1] == '>' ? link.Substring(1, link.Length - 2) : link;
        }

        public static List<InlineRun> Normalize(IList<InlineRun> input)
        {
            var runs = Merge(input.Where(r => r != null).Select(r =>
            {
                if (!r.BareUrl)
                    return r;
                var plain = r.Clone();
                plain.BareUrl = false;
                plain.Link = null;
                return plain;
            }));
            const InlineFormat edge = InlineFormat.Bold | InlineFormat.Italic | InlineFormat.Strike;
            var result = new List<InlineRun>();
            for (var i = 0; i < runs.Count; i++)
            {
                var run = runs[i];
                if (run.Kind != InlineRunKind.Text || run.Has(InlineFormat.Code) || (run.Format & edge) == 0)
                {
                    result.Add(run);
                    continue;
                }
                var prev = i > 0 ? runs[i - 1] : null;
                var next = i + 1 < runs.Count ? runs[i + 1] : null;
                var starting = run.Format & edge & ~(prev != null && prev.Link == run.Link ? prev.Format : InlineFormat.None);
                var ending = run.Format & edge & ~(next != null && next.Link == run.Link ? next.Format : InlineFormat.None);
                var text = run.Text;
                var lead = 0;
                while (lead < text.Length && char.IsWhiteSpace(text[lead]))
                    lead++;
                if (lead == text.Length)
                {
                    var blank = run.Clone();
                    blank.Format &= ~(starting | ending);
                    result.Add(blank);
                    continue;
                }
                var trail = 0;
                while (trail < text.Length - lead && char.IsWhiteSpace(text[text.Length - 1 - trail]))
                    trail++;
                if (lead > 0 && starting != 0)
                {
                    var head = run.Clone();
                    head.Text = text.Substring(0, lead);
                    head.Format &= ~starting;
                    result.Add(head);
                }
                else
                {
                    lead = 0;
                }
                if (trail == 0 || ending == 0)
                    trail = 0;
                var core = run.Clone();
                core.Text = text.Substring(lead, text.Length - lead - trail);
                result.Add(core);
                if (trail > 0)
                {
                    var tail = run.Clone();
                    tail.Text = text.Substring(text.Length - trail);
                    tail.Format &= ~ending;
                    result.Add(tail);
                }
            }
            return Merge(result);
        }

        private const InlineFormat EdgeFormats = InlineFormat.Bold | InlineFormat.Italic | InlineFormat.Strike;

        /// <summary>쓰기 단위: 조각 하나, 또는 링크 하나(같은 주소로 이어진 조각들 — 링크 바깥에서 연 서식은 단위의 서식).</summary>
        private sealed class Unit
        {
            public InlineFormat Want;
            public InlineRun Marker;   // 표식 모양을 정할 조각
            public int From, To;
            public bool IsLink;
        }

        private static string Write(List<InlineRun> runs, bool escapeAll)
        {
            var units = new List<Unit>();
            var i = 0;
            while (i < runs.Count)
            {
                var run = runs[i];
                if (run.Link == null)
                {
                    units.Add(new Unit { Want = run.Format & EdgeFormats, Marker = run, From = i, To = i + 1 });
                    i++;
                    continue;
                }
                var k = i + 1;
                while (k < runs.Count && runs[k].Link == run.Link && runs[k].LinkTitle == run.LinkTitle && !runs[k].Autolink && !run.Autolink)
                    k++;
                var outer = EdgeFormats;
                for (var j = i; j < k; j++)
                    outer &= runs[j].Format & runs[j].LinkOuter;
                units.Add(new Unit { Want = outer, Marker = run, From = i, To = k, IsLink = true });
                i = k;
            }
            var sb = new StringBuilder();
            WriteUnits(sb, runs, units, escapeAll, false);
            return sb.ToString();
        }

        private static void WriteLink(StringBuilder sb, List<InlineRun> runs, Unit unit, bool escapeAll)
        {
            var run = runs[unit.From];
            if (run.Autolink && (run.Format & ~unit.Want) == InlineFormat.None && run.Kind == InlineRunKind.Text && unit.To == unit.From + 1 &&
                (run.Text == run.Link || "mailto:" + run.Text == run.Link))
            {
                sb.Append('<').Append(run.Text).Append('>');
                return;
            }
            var inner = new List<InlineRun>();
            var units = new List<Unit>();
            for (var j = unit.From; j < unit.To; j++)
            {
                var c = runs[j].Clone();
                c.Format &= ~unit.Want;
                c.Link = null;
                inner.Add(c);
                units.Add(new Unit { Want = c.Format & EdgeFormats, Marker = c, From = inner.Count - 1, To = inner.Count });
            }
            sb.Append('[');
            WriteUnits(sb, inner, units, escapeAll, true);
            sb.Append("](");
            var dest = run.Link;
            if (dest.Length > 0 && dest[0] != '<' && NeedsAngle(dest))
                dest = "<" + dest + ">";
            sb.Append(dest);
            if (!string.IsNullOrEmpty(run.LinkTitle))
                sb.Append(' ').Append(run.LinkTitle);
            sb.Append(')');
        }

        private static bool NeedsAngle(string dest)
        {
            var depth = 0;
            foreach (var c in dest)
            {
                if (char.IsWhiteSpace(c) || c == '<')
                    return true;
                if (c == '(') depth++;
                else if (c == ')' && --depth < 0) return true;
            }
            return depth != 0;
        }

        private static readonly InlineFormat[] FormatOrder = { InlineFormat.Strike, InlineFormat.Bold, InlineFormat.Italic };

        private static void WriteUnits(StringBuilder sb, List<InlineRun> runs, List<Unit> units, bool escapeAll, bool inLink)
        {
            var stack = new List<InlineRun>();   // 그 서식을 연 조각(표식 모양용)
            var stackFormats = new List<InlineFormat>();
            var pendingSpace = string.Empty;     // 앞 글자 끝 공백 — 닫는 표식이 오면 그 뒤에 쓴다(공백 뒤 표식은 닫히지 않음)
            for (var u = 0; u < units.Count; u++)
            {
                var unit = units[u];
                var want = unit.Want;
                // 닫기: 스택 아래부터 계속 원하는 동안 유지, 그 위는 닫는다.
                var keep = 0;
                while (keep < stackFormats.Count && (want & stackFormats[keep]) != 0 && SameMarker(stack[keep], unit.Marker, stackFormats[keep]))
                    keep++;
                for (var k = stackFormats.Count - 1; k >= keep; k--)
                {
                    sb.Append(MarkerOf(stack[k], stackFormats[k]));
                    stack.RemoveAt(k);
                    stackFormats.RemoveAt(k);
                }
                sb.Append(pendingSpace);
                pendingSpace = string.Empty;
                // 열기: 오래 이어지는 서식이 바깥
                var open = new List<InlineFormat>();
                foreach (var f in FormatOrder)
                    if ((want & f) != 0 && !stackFormats.Contains(f))
                        open.Add(f);
                if (open.Count > 1)
                    open = open.OrderByDescending(f => Extent(runs, units, u, f)).ThenBy(f => Array.IndexOf(FormatOrder, f)).ToList();
                var leaf = unit.IsLink ? null : runs[unit.From];
                var plainLeaf = leaf != null && leaf.Kind == InlineRunKind.Text && !leaf.Has(InlineFormat.Code);
                if (plainLeaf && open.Count > 0)
                {
                    // 여는 표식 바로 뒤 공백은 열리지 않으므로 공백을 표식 앞에 쓴다(서식이 겹쳐 다시 여는 경우).
                    var lead = 0;
                    while (lead < leaf.Text.Length - 1 && char.IsWhiteSpace(leaf.Text[lead]))
                        lead++;
                    if (lead > 0)
                    {
                        sb.Append(leaf.Text, 0, lead);
                        leaf = leaf.Clone();
                        leaf.Text = leaf.Text.Substring(lead);
                    }
                }
                foreach (var f in open)
                {
                    sb.Append(MarkerOf(unit.Marker, f));
                    stack.Add(unit.Marker);
                    stackFormats.Add(f);
                }
                if (unit.IsLink)
                {
                    WriteLink(sb, runs, unit, escapeAll);
                }
                else if (plainLeaf && stackFormats.Count > 0)
                {
                    var trail = 0;
                    while (trail < leaf.Text.Length - 1 && char.IsWhiteSpace(leaf.Text[leaf.Text.Length - 1 - trail]))
                        trail++;
                    if (trail > 0)
                    {
                        pendingSpace = leaf.Text.Substring(leaf.Text.Length - trail);
                        leaf = leaf.Clone();
                        leaf.Text = leaf.Text.Substring(0, leaf.Text.Length - trail);
                    }
                    WriteLeaf(sb, leaf, escapeAll, inLink);
                }
                else
                {
                    WriteLeaf(sb, leaf, escapeAll, inLink);
                }
            }
            for (var k = stackFormats.Count - 1; k >= 0; k--)
                sb.Append(MarkerOf(stack[k], stackFormats[k]));
            sb.Append(pendingSpace);
        }

        private static bool SameMarker(InlineRun a, InlineRun b, InlineFormat f)
        {
            return MarkerOf(a, f) == MarkerOf(b, f);
        }

        private static int Extent(List<InlineRun> runs, List<Unit> units, int from, InlineFormat f)
        {
            var n = 0;
            for (var u = from; u < units.Count && (units[u].Want & f) != 0; u++)
                for (var j = units[u].From; j < units[u].To; j++)
                    n += runs[j].Text.Length + 1;
            return n;
        }

        private static string MarkerOf(InlineRun run, InlineFormat f)
        {
            switch (f)
            {
                case InlineFormat.Bold: return string.IsNullOrEmpty(run.BoldMarker) ? "**" : run.BoldMarker;
                case InlineFormat.Italic: return string.IsNullOrEmpty(run.ItalicMarker) ? "*" : run.ItalicMarker;
                case InlineFormat.Strike: return string.IsNullOrEmpty(run.StrikeMarker) ? "~~" : run.StrikeMarker;
                default: return string.Empty;
            }
        }

        private static void WriteLeaf(StringBuilder sb, InlineRun run, bool escapeAll, bool inLink)
        {
            if (run.Kind == InlineRunKind.Raw)
            {
                sb.Append(run.Text);
                return;
            }
            if (run.Kind == InlineRunKind.Math)
            {
                sb.Append('$').Append(run.Text).Append('$');
                return;
            }
            if (run.Has(InlineFormat.Code))
            {
                var text = run.Text;
                var longest = 0;
                for (var k = 0; k < text.Length;)
                {
                    if (text[k] != '`') { k++; continue; }
                    var n = RunLength(text, k, '`');
                    longest = Math.Max(longest, n);
                    k += n;
                }
                var fence = run.CodeFence > 0 && !ContainsRun(text, '`', run.CodeFence) ? run.CodeFence : longest + 1;
                var pad = run.CodePad || text.Length > 0 && (text[0] == '`' || text[text.Length - 1] == '`') ||
                          text.Length >= 2 && text[0] == ' ' && text[text.Length - 1] == ' ' && text.Trim(' ').Length > 0;
                var marker = new string('`', fence);
                sb.Append(marker);
                if (pad) sb.Append(' ');
                sb.Append(text);
                if (pad) sb.Append(' ');
                sb.Append(marker);
                return;
            }
            sb.Append(Escape(run.Text, escapeAll, inLink));
        }

        private static bool ContainsRun(string text, char c, int length)
        {
            for (var k = 0; k < text.Length;)
            {
                if (text[k] != c) { k++; continue; }
                var n = RunLength(text, k, c);
                if (n == length)
                    return true;
                k += n;
            }
            return false;
        }

        private static string Escape(string text, bool all, bool inLink)
        {
            var sb = new StringBuilder(text.Length + 4);
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '\\')
                {
                    // 뒤가 문장부호이거나 조각 끝(다음 조각의 표식을 이스케이프하지 않게)이면 \\ 로
                    if (i + 1 >= text.Length || IsAsciiPunctuation(text[i + 1]))
                        sb.Append('\\');
                    sb.Append(c);
                    continue;
                }
                if (all)
                {
                    switch (c)
                    {
                        case '*': case '_': case '~':
                            if (_escapeEmphasis)
                                sb.Append('\\');
                            break;
                        case '`': case '[': case ']': case '<': case '&': case '$':
                            sb.Append('\\');
                            break;
                        case '!':
                            if (i + 1 < text.Length && text[i + 1] == '[')
                                sb.Append('\\');
                            break;
                    }
                }
                else if (inLink && (c == '[' || c == ']'))
                {
                    sb.Append('\\');
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>두 조각 목록이 같은 글자·서식·링크인지(표식 모양·코드 울타리는 보지 않음).</summary>
        public static bool Equivalent(IList<InlineRun> a, IList<InlineRun> b)
        {
            var x = Canon(a);
            var y = Canon(b);
            if (x.Count != y.Count)
                return false;
            for (var i = 0; i < x.Count; i++)
            {
                var p = x[i];
                var q = y[i];
                if (p.Text != q.Text || p.Kind != q.Kind || p.Format != q.Format || StripAngle(p.Link) != StripAngle(q.Link) || (p.LinkTitle ?? "") != (q.LinkTitle ?? ""))
                    return false;
            }
            return true;
        }

        private static List<InlineRun> Canon(IList<InlineRun> runs)
        {
            // 공백에 걸린 굵게·기울임·취소선은 보이지 않으므로 비교에서 뺀다(서식 경계 공백은 표식 밖으로 나감).
            var split = new List<InlineRun>();
            foreach (var r in runs)
            {
                if (r.Kind != InlineRunKind.Text || r.Has(InlineFormat.Code) || (r.Format & EdgeFormats) == 0)
                {
                    split.Add(r);
                    continue;
                }
                var start = 0;
                while (start < r.Text.Length)
                {
                    var ws = char.IsWhiteSpace(r.Text[start]);
                    var end = start;
                    while (end < r.Text.Length && char.IsWhiteSpace(r.Text[end]) == ws)
                        end++;
                    var piece = r.Clone();
                    piece.Text = r.Text.Substring(start, end - start);
                    if (ws)
                        piece.Format &= ~EdgeFormats;
                    split.Add(piece);
                    start = end;
                }
            }
            return Merge(split.Select(r =>
            {
                var c = r.Clone();
                c.BoldMarker = "**";
                c.ItalicMarker = "*";
                c.StrikeMarker = "~~";
                c.CodeFence = 0;
                c.CodePad = false;
                c.Autolink = false;
                c.LinkOuter = InlineFormat.None;
                c.Underline = false;
                c.TextColor = null;
                c.Highlight = null;
                if (c.BareUrl)
                {
                    c.BareUrl = false;
                    c.Link = null;
                }
                return c;
            }));
        }

        // ── 보이는 글자 기준 연산(편집기) ───────────────────────────────────

        /// <summary>보이는 글자(수식은 원문, 원문 조각은 그대로).</summary>
        public static string ToPlain(IList<InlineRun> runs)
        {
            var sb = new StringBuilder();
            foreach (var r in runs)
                sb.Append(r.Kind == InlineRunKind.Math ? "$" + r.Text + "$" : r.Text);
            return sb.ToString();
        }

        /// <summary>줄 안 Markdown의 보이는 글자(편집기 기준).</summary>
        public static string ToPlain(string markdown)
        {
            return IsPlain(markdown) ? markdown ?? string.Empty : ToPlain(ParseForEditing(markdown));
        }

        /// <summary>
        /// 보이는 글자 위치 → 원문 위치 대응(길이 = 보이는 글자 수 + 1, 마지막은 원문 길이). 보이는 글자를 원문에서 차례로 찾는 근사라
        /// 표식과 같은 글자를 이스케이프 없이 쓴 드문 경우에는 한두 글자 어긋날 수 있다(찾기 결과 표시용).
        /// </summary>
        public static int[] PlainToSourceMap(string markdown)
        {
            markdown = markdown ?? string.Empty;
            var plain = ToPlain(markdown);
            var map = new int[plain.Length + 1];
            if (plain == markdown)
            {
                for (var i = 0; i <= plain.Length; i++)
                    map[i] = i;
                return map;
            }
            var j = 0;
            for (var i = 0; i < plain.Length; i++)
            {
                var k = j < markdown.Length ? markdown.IndexOf(plain[i], j) : -1;
                if (k < 0)
                    k = Math.Min(j, markdown.Length);
                map[i] = k;
                j = Math.Min(markdown.Length, k + 1);
            }
            map[plain.Length] = markdown.Length;
            return map;
        }

        /// <summary>원문 위치를 보이는 글자 위치로(그 앞에 있는 보이는 글자 수).</summary>
        public static int SourceToPlain(string markdown, int sourceOffset)
        {
            var map = PlainToSourceMap(markdown);
            var count = 0;
            while (count < map.Length - 1 && map[count] < sourceOffset)
                count++;
            return count;
        }

        /// <summary>보이는 글자 위치를 원문 위치로.</summary>
        public static int PlainToSource(string markdown, int plainOffset)
        {
            var map = PlainToSourceMap(markdown);
            return map[Math.Max(0, Math.Min(plainOffset, map.Length - 1))];
        }

        /// <summary>보이는 글자 [start, end) 구간의 조각 사본.</summary>
        public static List<InlineRun> Slice(IList<InlineRun> runs, int start, int end)
        {
            var result = new List<InlineRun>();
            var pos = 0;
            foreach (var r in runs)
            {
                var len = r.Text.Length;
                var a = Math.Max(start, pos);
                var b = Math.Min(end, pos + len);
                if (b > a || (len == 0 && pos >= start && pos < end))
                {
                    var c = r.Clone();
                    c.Text = r.Text.Substring(a - pos, Math.Max(0, b - a));
                    result.Add(c);
                }
                pos += len;
            }
            return result;
        }

        /// <summary>보이는 글자 위치에서 둘로 나눈 Markdown.</summary>
        public static void SplitAt(string markdown, int plainOffset, out string head, out string tail)
        {
            var runs = ParseForEditing(markdown);
            var len = ToPlain(runs).Length;
            plainOffset = Math.Max(0, Math.Min(len, plainOffset));
            head = Serialize(Slice(runs, 0, plainOffset));
            tail = Serialize(Slice(runs, plainOffset, len));
        }

        /// <summary>두 줄 안 Markdown을 잇는다(<c>**a**</c> + <c>**b**</c> → <c>**ab**</c>).</summary>
        public static string Concat(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b ?? string.Empty;
            if (string.IsNullOrEmpty(b)) return a;
            if (IsPlain(a) && IsPlain(b))
                return a + b;
            var runs = ParseForEditing(a);
            runs.AddRange(ParseForEditing(b));
            return Serialize(runs);
        }

        /// <summary>[start, end) 구간이 모두 서식 <paramref name="f"/>인지(빈 구간이면 그 자리 앞 글자 기준).</summary>
        public static bool HasFormat(IList<InlineRun> runs, int start, int end, InlineFormat f)
        {
            if (end <= start)
            {
                var at = RunAt(runs, start, true);
                return at != null && at.Has(f);
            }
            var any = false;
            foreach (var r in Slice(runs, start, end))
            {
                if (r.Text.Length == 0)
                    continue;
                any = true;
                if (!r.Has(f))
                    return false;
            }
            return any;
        }

        /// <summary>보이는 글자 위치의 조각(<paramref name="before"/>면 그 위치 앞 글자의 조각).</summary>
        public static InlineRun RunAt(IList<InlineRun> runs, int offset, bool before)
        {
            var pos = 0;
            InlineRun last = null;
            foreach (var r in runs)
            {
                var len = r.Text.Length;
                if (before ? offset > pos && offset <= pos + len : offset >= pos && offset < pos + len)
                    return r;
                pos += len;
                last = r;
            }
            return before && offset >= pos ? last : null;
        }

        /// <summary>[start, end) 구간에 서식을 켜거나 끈다. 코드를 켜면 그 구간은 원문·수식이 아닌 글자로.</summary>
        public static List<InlineRun> ApplyFormat(IList<InlineRun> runs, int start, int end, InlineFormat f, bool on)
        {
            return Transform(runs, start, end, r =>
            {
                if (on)
                {
                    r.Format |= f;
                    if (f == InlineFormat.Code && r.Kind != InlineRunKind.Text)
                    {
                        if (r.Kind == InlineRunKind.Math)
                            r.Text = "$" + r.Text + "$";
                        r.Kind = InlineRunKind.Text;
                    }
                }
                else
                {
                    r.Format &= ~f;
                }
                if (f == InlineFormat.Code)
                {
                    r.CodeFence = 0;
                    r.CodePad = false;
                }
            });
        }

        /// <summary>[start, end) 구간을 링크로(주소가 비면 링크 해제).</summary>
        public static List<InlineRun> ApplyLink(IList<InlineRun> runs, int start, int end, string url)
        {
            return Transform(runs, start, end, r =>
            {
                r.Link = string.IsNullOrEmpty(url) ? null : url;
                r.LinkTitle = null;
                r.Autolink = false;
                r.BareUrl = false;
            });
        }

        /// <summary>보이는 글자 위치의 링크 구간(같은 주소로 이어진 조각들). 없으면 false.</summary>
        public static bool LinkRange(IList<InlineRun> runs, int offset, out int start, out int end, out InlineRun link)
        {
            start = end = -1;
            link = null;
            var pos = 0;
            var spans = new List<Tuple<int, int, InlineRun>>();
            foreach (var r in runs)
            {
                spans.Add(Tuple.Create(pos, pos + r.Text.Length, r));
                pos += r.Text.Length;
            }
            for (var i = 0; i < spans.Count; i++)
            {
                var s = spans[i];
                if (s.Item3.Link == null || offset < s.Item1 || offset > s.Item2)
                    continue;
                var a = i;
                while (a > 0 && spans[a - 1].Item3.Link == s.Item3.Link && spans[a - 1].Item3.LinkTitle == s.Item3.LinkTitle)
                    a--;
                var b = i;
                while (b + 1 < spans.Count && spans[b + 1].Item3.Link == s.Item3.Link && spans[b + 1].Item3.LinkTitle == s.Item3.LinkTitle)
                    b++;
                start = spans[a].Item1;
                end = spans[b].Item2;
                link = s.Item3;
                return true;
            }
            return false;
        }

        private static List<InlineRun> Transform(IList<InlineRun> runs, int start, int end, Action<InlineRun> change)
        {
            var result = new List<InlineRun>();
            var pos = 0;
            foreach (var r in runs)
            {
                var len = r.Text.Length;
                var a = Math.Max(start, pos);
                var b = Math.Min(end, pos + len);
                if (b <= a)
                {
                    result.Add(r.Clone());
                    pos += len;
                    continue;
                }
                // 원문·수식 조각은 쪼개지 않는다(통째로 바꿈)
                if (r.Kind != InlineRunKind.Text)
                {
                    var whole = r.Clone();
                    change(whole);
                    result.Add(whole);
                    pos += len;
                    continue;
                }
                if (a > pos)
                {
                    var head = r.Clone();
                    head.Text = r.Text.Substring(0, a - pos);
                    result.Add(head);
                }
                var mid = r.Clone();
                mid.Text = r.Text.Substring(a - pos, b - a);
                change(mid);
                result.Add(mid);
                if (b < pos + len)
                {
                    var tail = r.Clone();
                    tail.Text = r.Text.Substring(b - pos);
                    result.Add(tail);
                }
                pos += len;
            }
            return Merge(result);
        }

        /// <summary>보이는 글자 [start, end)를 지우고 그 자리에 조각들을 넣는다(넣는 글자는 앞 글자의 서식을 따르지 않음).</summary>
        public static List<InlineRun> Replace(IList<InlineRun> runs, int start, int end, IList<InlineRun> insert)
        {
            var len = ToPlain(runs).Length;
            var result = Slice(runs, 0, start);
            result.AddRange(insert.Select(r => r.Clone()));
            result.AddRange(Slice(runs, end, len));
            return Merge(result);
        }

        /// <summary>
        /// 보이는 글자 [start, end)를 글자 <paramref name="value"/>로 바꾼다(찾아 바꾸기). 새 글자는 바뀌는 첫 글자의 조각 모양(서식·링크·표식 모양·원문 종류·범위)을 따른다 —
        /// 빈 구간이면 앞 글자(줄 처음이면 뒤 글자). 바꾼 구간 밖 조각은 그대로.
        /// </summary>
        public static List<InlineRun> ReplaceText(IList<InlineRun> runs, int start, int end, string value)
        {
            var length = ToPlain(runs).Length;
            start = Math.Max(0, Math.Min(start, length));
            end = Math.Max(start, Math.Min(end, length));
            if (string.IsNullOrEmpty(value))
                return Replace(runs, start, end, new InlineRun[0]);
            var basis = end > start || start == 0 ? RunAt(runs, start, false) : RunAt(runs, start, true);
            var inserted = basis != null ? basis.Clone() : new InlineRun();
            if (inserted.Kind == InlineRunKind.Math)
                inserted.Kind = InlineRunKind.Raw;   // 편집용 읽기에는 없음(수식은 원문 조각) — 다른 조각 목록에서 와도 글자 그대로
            inserted.Text = value;
            inserted.BareUrl = false;
            return Replace(runs, start, end, new[] { inserted });
        }

        // ── 입력 중 서식 단축 ─────────────────────────────────────────────

        /// <summary>
        /// 방금 <paramref name="caret"/> 바로 앞에 닫는 표식을 입력했을 때 <c>**글**</c>·<c>__글__</c>·<c>*글*</c>·<c>_글_</c>·<c>~~글~~</c>·<c>`코드`</c>를
        /// 서식으로 바꾼 조각 목록과 새 커서 위치. 표식과 글자가 모두 서식 없는 일반 글자일 때만(코드·링크·원문 안은 그대로).
        /// </summary>
        public static bool TryInputRule(IList<InlineRun> runs, int caret, out List<InlineRun> result, out int newCaret)
        {
            result = null;
            newCaret = caret;
            var plain = ToPlain(runs);
            if (caret <= 1 || caret > plain.Length)
                return false;
            var last = plain[caret - 1];
            if (last == '$')
            {
                // $수식$ → 줄 안 수식(원문 조각 — 표시할 때 수식으로 그림)
                var open = caret >= 2 ? plain.LastIndexOf('$', caret - 2) : -1;
                int close;
                if (open < 0 || !TryMath(plain, open, out close) || close != caret - 1 || !AllPlainText(runs, open, caret))
                    return false;
                result = Replace(runs, open, caret, new[] { new InlineRun { Kind = InlineRunKind.Raw, Text = plain.Substring(open, caret - open) } });
                newCaret = caret;
                return true;
            }
            if (last == ')')
            {
                // [글자](주소) → 링크
                var m = LinkInput.Match(plain.Substring(0, caret));
                if (!m.Success || !AllPlainText(runs, m.Index, caret))
                    return false;
                var label = m.Groups[1].Value;
                result = Replace(runs, m.Index, caret, new[] { new InlineRun { Text = label, Link = m.Groups[2].Value } });
                newCaret = m.Index + label.Length;
                return true;
            }
            string[] markers;
            switch (last)
            {
                case '*': markers = new[] { "**", "*" }; break;
                case '_': markers = new[] { "__", "_" }; break;
                case '~': markers = new[] { "~~" }; break;
                case '`': markers = new[] { "`" }; break;
                default: return false;
            }
            foreach (var marker in markers)
            {
                var m = marker.Length;
                var closeStart = caret - m;
                if (closeStart < 1 || string.CompareOrdinal(plain, closeStart, marker, 0, m) != 0)
                    continue;
                // 닫는 표식 바로 앞이 같은 표식 글자면(***) 이 길이로 보지 않는다
                if (closeStart > 0 && plain[closeStart - 1] == last && m == 1 && marker != "`")
                    continue;
                // 가장 가까운 여는 표식 하나만 본다(먼 표식까지 묶지 않음)
                var openStart = plain.LastIndexOf(marker, closeStart - 1, StringComparison.Ordinal);
                if (openStart < 0)
                    continue;
                var contentStart = openStart + m;
                var content = closeStart - contentStart;
                if (content <= 0 ||
                    (openStart > 0 && plain[openStart - 1] == last) ||
                    (marker != "`" && plain[contentStart] == last) ||
                    char.IsWhiteSpace(plain[contentStart]) || char.IsWhiteSpace(plain[closeStart - 1]) ||
                    (marker != "`" && openStart > 0 && !char.IsWhiteSpace(plain[openStart - 1]) && !IsPunctuation(plain[openStart - 1])) ||
                    plain.IndexOf('\n', openStart, caret - openStart) >= 0 ||
                    !AllPlainText(runs, openStart, caret))
                    continue;
                var format = last == '`' ? InlineFormat.Code : last == '~' ? InlineFormat.Strike : m == 2 ? InlineFormat.Bold : InlineFormat.Italic;
                var inner = ApplyFormat(Slice(runs, contentStart, closeStart), 0, content, format, true);
                foreach (var r in inner)
                {
                    if (format == InlineFormat.Bold) r.BoldMarker = marker;
                    else if (format == InlineFormat.Italic) r.ItalicMarker = marker;
                    else if (format == InlineFormat.Strike) r.StrikeMarker = marker;
                }
                result = Replace(runs, openStart, caret, inner);
                newCaret = caret - 2 * m;
                return true;
            }
            return false;
        }

        private static bool AllPlainText(IList<InlineRun> runs, int start, int end)
        {
            foreach (var r in Slice(runs, start, end))
                if (r.Kind != InlineRunKind.Text || r.Has(InlineFormat.Code) || r.Link != null)
                    return false;
            return true;
        }
    }
}

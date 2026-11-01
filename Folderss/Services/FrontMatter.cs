using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Folderss.Services
{
    /// <summary>front matter 속성 값의 모양.</summary>
    public enum FrontMatterValueKind
    {
        /// <summary>한 줄 값(<c>key: value</c>, 따옴표 값, 빈 값).</summary>
        Scalar,
        /// <summary>단순 목록(<c>key: [a, b]</c> 또는 다음 줄들의 <c>- a</c>).</summary>
        List,
        /// <summary>편집기가 해석하지 않는 YAML(중첩 맵, 여러 줄 블록 스칼라, 앵커·태그 등). 원문으로만 보여 준다.</summary>
        Complex,
    }

    /// <summary>front matter 속성 하나(최상위 키 하나와 그 값 줄들).</summary>
    public sealed class FrontMatterEntry
    {
        /// <summary>따옴표를 벗긴 키.</summary>
        public string Key { get; internal set; } = string.Empty;

        public FrontMatterValueKind Kind { get; internal set; }

        /// <summary><see cref="FrontMatterValueKind.Scalar"/>의 값(따옴표·이스케이프를 푼 글자, 빈 값이면 빈 문자열).</summary>
        public string Value { get; internal set; } = string.Empty;

        /// <summary><see cref="FrontMatterValueKind.List"/>의 항목(따옴표를 푼 글자).</summary>
        public IReadOnlyList<string> Items { get; internal set; } = Array.Empty<string>();

        /// <summary><see cref="FrontMatterValueKind.Complex"/>일 때 키 뒤의 원문(같은 줄 나머지 + 이어지는 줄).</summary>
        public string RawValue { get; internal set; } = string.Empty;

        /// <summary>블록 원문에서 이 속성이 시작하는 줄 번호(0 = 여는 <c>---</c>).</summary>
        public int FirstLine { get; internal set; }

        /// <summary>이 속성이 차지하는 줄 수(키 줄 + 이어지는 줄).</summary>
        public int LineCount { get; internal set; }

        // 다시 쓸 때 쓰는 원문 정보
        internal string Head = string.Empty;        // 키 + 콜론까지(키 따옴표·콜론 앞 공백 그대로)
        internal string Spacing = " ";              // 콜론과 값 사이 공백
        internal string Comment = string.Empty;     // 값 뒤 주석(앞 공백 포함)
        internal char Quote;                        // 스칼라 값 따옴표('"' / '\'' / 없으면 '\0')
        internal bool FlowList;                     // [a, b] 형태
        internal string ItemIndent = string.Empty;  // 블록 목록 "- " 앞 들여쓰기
        internal List<string> ItemTokens = new List<string>();   // 항목 원문(따옴표 포함) — 바뀌지 않은 항목은 그대로 다시 쓴다
    }

    /// <summary><see cref="FrontMatter.Parse"/> 결과.</summary>
    public sealed class FrontMatterDocument
    {
        /// <summary>최상위가 <c>key: value</c> 줄로만 되어 있어 속성 표로 보여 줄 수 있는지. 아니면 원문 그대로 보여 준다.</summary>
        public bool IsTable { get; internal set; }

        public IReadOnlyList<FrontMatterEntry> Entries { get; internal set; } = Array.Empty<FrontMatterEntry>();

        /// <summary>여는·닫는 구분 줄 사이 원문(원문 표시용).</summary>
        public string Body { get; internal set; } = string.Empty;
    }

    /// <summary>
    /// 블록 편집기 front matter 블록(문서 맨 앞 <c>---</c> … <c>---</c>)의 속성 표 해석과 줄 단위 편집. YAML 라이브러리 없이 필요한 범위만 읽는다:
    /// 최상위 <c>key: 스칼라</c>, 따옴표 값, 단순 목록(<c>[a, b]</c>·<c>- a</c>). 중첩 맵·여러 줄 블록 스칼라·앵커 등은
    /// <see cref="FrontMatterValueKind.Complex"/>로 원문을 보여 주고 편집하지 않는다.
    /// <para>
    /// 편집은 그 속성의 줄만 다시 쓰고 나머지 줄(주석·빈 줄·순서·다른 키의 따옴표)은 글자 그대로 둔다 — 편집하지 않으면 원문이 바뀌지 않는다.
    /// 줄바꿈은 <c>\n</c>(블록 본문 규칙). WPF를 쓰지 않아 테스트 프로젝트에서 소스 링크로 검증한다.
    /// </para>
    /// </summary>
    public static class FrontMatter
    {
        // 최상위 키 줄: 키(따옴표 키 또는 지시 문자로 시작하지 않는 일반 키) + 콜론 + (공백 + 나머지 | 끝)
        private static readonly Regex KeyLine = new Regex(
            @"^(?<head>(?<key>""(?:[^""\\]|\\.)*""|'(?:[^']|'')*'|[^\s#'""\-\[\]{}?:,&*!|>%@`][^\t]*?)[ \t]*:)(?:(?<sp>[ \t]+)(?<rest>.*)|[ \t]*)$");

        private static readonly Regex ItemLine = new Regex(@"^(?<indent>[ \t]*)-(?:(?<sp>[ \t]+)(?<item>.*)|[ \t]*)$");

        private const string Indicators = "-?:,[]{}#&*!|>'\"%@`";

        /// <summary>블록 본문이 front matter 모양(첫 줄 <c>---</c>, 마지막 줄 <c>---</c> 또는 <c>...</c>, 두 줄 이상)인지.</summary>
        public static bool LooksLikeFrontMatter(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            var lines = text.Split('\n');
            if (lines.Length < 2 || lines[0].TrimEnd() != "---")
                return false;
            var last = lines[lines.Length - 1].TrimEnd();
            return last == "---" || last == "...";
        }

        public static FrontMatterDocument Parse(string text)
        {
            var doc = new FrontMatterDocument();
            if (!LooksLikeFrontMatter(text))
            {
                doc.Body = text ?? string.Empty;
                return doc;
            }

            var lines = text.Split('\n');
            doc.Body = string.Join("\n", lines, 1, lines.Length - 2);
            var entries = new List<FrontMatterEntry>();
            var valid = true;
            var end = lines.Length - 1;   // 닫는 줄
            var i = 1;
            while (i < end)
            {
                var line = lines[i];
                if (IsTrivia(line))
                {
                    i++;
                    continue;
                }
                var m = KeyLine.Match(line);
                if (!m.Success)
                {
                    valid = false;   // 최상위 목록·들여쓴 줄·해석할 수 없는 줄
                    break;
                }

                // 이어지는 줄: 들여쓴 줄, 0열 "- " 항목, 그 사이의 빈 줄·주석(뒤에 이어지는 줄이 더 있을 때만)
                var last = i;
                for (var k = i + 1; k < end; k++)
                {
                    if (IsContinuation(lines[k]))
                        last = k;
                    else if (!IsTrivia(lines[k]))
                        break;
                }

                entries.Add(ReadEntry(lines, i, last, m));
                i = last + 1;
            }

            doc.IsTable = valid;
            doc.Entries = valid ? entries : new List<FrontMatterEntry>();
            return doc;
        }

        private static bool IsTrivia(string line)
        {
            var t = line.Trim();
            return t.Length == 0 || t.StartsWith("#", StringComparison.Ordinal);
        }

        private static bool IsContinuation(string line)
        {
            if (line.Trim().Length == 0)
                return false;
            if (line[0] == ' ' || line[0] == '\t')
                return true;
            return line == "-" || line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("-\t", StringComparison.Ordinal);
        }

        private static FrontMatterEntry ReadEntry(string[] lines, int first, int last, Match m)
        {
            var entry = new FrontMatterEntry
            {
                FirstLine = first,
                LineCount = last - first + 1,
                Head = m.Groups["head"].Value,
                Key = UnquoteKey(m.Groups["key"].Value),
            };
            if (m.Groups["sp"].Success)
                entry.Spacing = m.Groups["sp"].Value;

            string value, comment;
            SplitComment(m.Groups["rest"].Success ? m.Groups["rest"].Value : string.Empty, out value, out comment);
            entry.Comment = comment;

            var rawValue = new StringBuilder(m.Groups["rest"].Success ? m.Groups["rest"].Value : string.Empty);
            for (var k = first + 1; k <= last; k++)
                rawValue.Append('\n').Append(lines[k]);
            entry.RawValue = rawValue.ToString().TrimStart('\n');

            if (last > first)
            {
                if (value.Length == 0 && TryReadBlockList(lines, first + 1, last, entry))
                    entry.Kind = FrontMatterValueKind.List;
                else
                    entry.Kind = FrontMatterValueKind.Complex;
                return entry;
            }

            if (value.Length > 0 && value[0] == '[')
            {
                List<string> tokens;
                if (TrySplitFlow(value, out tokens))
                {
                    var items = new List<string>();
                    foreach (var token in tokens)
                    {
                        string item;
                        char quote;
                        if (!TryReadScalar(token, false, out item, out quote))
                        {
                            entry.Kind = FrontMatterValueKind.Complex;
                            return entry;
                        }
                        items.Add(item);
                    }
                    entry.Kind = FrontMatterValueKind.List;
                    entry.FlowList = true;
                    entry.Items = items;
                    entry.ItemTokens = tokens;
                    return entry;
                }
                entry.Kind = FrontMatterValueKind.Complex;
                return entry;
            }

            string scalar;
            char q;
            if (TryReadScalar(value, true, out scalar, out q))
            {
                entry.Kind = FrontMatterValueKind.Scalar;
                entry.Value = scalar;
                entry.Quote = q;
            }
            else
            {
                entry.Kind = FrontMatterValueKind.Complex;
            }
            return entry;
        }

        /// <summary>키 다음 줄들이 같은 들여쓰기의 <c>- 스칼라</c> 항목만으로 되어 있으면 목록으로 읽는다(사이 빈 줄·주석·항목 주석이 있으면 아님).</summary>
        private static bool TryReadBlockList(string[] lines, int from, int to, FrontMatterEntry entry)
        {
            string indent = null;
            var items = new List<string>();
            var tokens = new List<string>();
            for (var k = from; k <= to; k++)
            {
                var m = ItemLine.Match(lines[k]);
                if (!m.Success)
                    return false;
                var itemIndent = m.Groups["indent"].Value;
                if (indent == null)
                    indent = itemIndent;
                else if (indent != itemIndent)
                    return false;
                var token = m.Groups["item"].Success ? m.Groups["item"].Value.TrimEnd() : string.Empty;
                string value, comment;
                SplitComment(token, out value, out comment);
                if (comment.Length > 0)
                    return false;
                string item;
                char quote;
                if (!TryReadScalar(value, false, out item, out quote))
                    return false;
                items.Add(item);
                tokens.Add(value);
            }
            entry.ItemIndent = indent ?? string.Empty;
            entry.Items = items;
            entry.ItemTokens = tokens;
            return true;
        }

        /// <summary>
        /// 한 줄 스칼라를 읽는다(따옴표 값은 풀고, 일반 값은 그대로). 흐름 모음(<c>[</c>·<c>{</c>), 블록 스칼라(<c>|</c>·<c>&gt;</c>),
        /// 앵커·별칭·태그(<c>&amp;</c>·<c>*</c>·<c>!</c>)와 맵처럼 보이는 값(<c>a: b</c>)은 해석하지 않는다.
        /// </summary>
        private static bool TryReadScalar(string token, bool allowColon, out string value, out char quote)
        {
            value = string.Empty;
            quote = '\0';
            token = token.Trim();
            if (token.Length == 0)
                return true;
            var c = token[0];
            if (c == '"')
            {
                var sb = new StringBuilder();
                for (var i = 1; i < token.Length; i++)
                {
                    var ch = token[i];
                    if (ch == '\\')
                    {
                        // 다시 쓸 때 같은 글자로 돌아오는 이스케이프만 읽는다(\x·\u 등은 해석하지 않음).
                        if (i + 1 >= token.Length || "nt\"\\/ ".IndexOf(token[i + 1]) < 0)
                            return false;
                        var n = token[++i];
                        sb.Append(n == 'n' ? '\n' : n == 't' ? '\t' : n);
                        continue;
                    }
                    if (ch == '"')
                    {
                        if (i != token.Length - 1)
                            return false;
                        value = sb.ToString();
                        quote = '"';
                        return true;
                    }
                    sb.Append(ch);
                }
                return false;
            }
            if (c == '\'')
            {
                var sb = new StringBuilder();
                for (var i = 1; i < token.Length; i++)
                {
                    var ch = token[i];
                    if (ch == '\'')
                    {
                        if (i + 1 < token.Length && token[i + 1] == '\'')
                        {
                            sb.Append('\'');
                            i++;
                            continue;
                        }
                        if (i != token.Length - 1)
                            return false;
                        value = sb.ToString();
                        quote = '\'';
                        return true;
                    }
                    sb.Append(ch);
                }
                return false;
            }
            if ("[]{}|>&*!%@`".IndexOf(c) >= 0)
                return false;
            if ((c == '-' || c == '?' || c == ':') && (token.Length == 1 || token[1] == ' ' || token[1] == '\t'))
                return false;
            if (!allowColon && (token.Contains(": ") || token.EndsWith(":", StringComparison.Ordinal)))
                return false;
            value = token;
            return true;
        }

        /// <summary>값 뒤 주석(따옴표 밖, 공백 다음의 <c>#</c>)을 떼어 낸다. <paramref name="comment"/>는 앞 공백을 포함한다.</summary>
        private static void SplitComment(string rest, out string value, out string comment)
        {
            var quote = '\0';
            for (var i = 0; i < rest.Length; i++)
            {
                var ch = rest[i];
                if (quote != '\0')
                {
                    if (quote == '"' && ch == '\\')
                        i++;
                    else if (ch == quote)
                        quote = '\0';
                    continue;
                }
                if ((ch == '"' || ch == '\'') && (i == 0 || rest[i - 1] == ' ' || rest[i - 1] == '\t' || rest[i - 1] == '[' || rest[i - 1] == ','))
                {
                    quote = ch;
                    continue;
                }
                if (ch == '#' && (i == 0 || rest[i - 1] == ' ' || rest[i - 1] == '\t'))
                {
                    var valueEnd = i;
                    while (valueEnd > 0 && (rest[valueEnd - 1] == ' ' || rest[valueEnd - 1] == '\t'))
                        valueEnd--;
                    value = rest.Substring(0, valueEnd);
                    comment = rest.Substring(valueEnd);
                    return;
                }
            }
            value = rest.TrimEnd();
            comment = rest.Substring(value.Length).Trim().Length == 0 ? string.Empty : rest.Substring(value.Length);
        }

        /// <summary><c>[a, "b, c"]</c>를 최상위 쉼표로 나눈 항목 원문 목록. 안에 다른 흐름 모음이 있으면 실패.</summary>
        private static bool TrySplitFlow(string value, out List<string> tokens)
        {
            tokens = null;
            if (value.Length < 2 || value[0] != '[' || value[value.Length - 1] != ']')
                return false;
            List<string> parts;
            if (!TrySplitTopLevel(value.Substring(1, value.Length - 2), false, out parts))
                return false;
            // [a, b,] 의 마지막 빈 항목은 무시
            if (parts.Count > 0 && parts[parts.Count - 1].Length == 0)
                parts.RemoveAt(parts.Count - 1);
            if (parts.Any(p => p.Length == 0))
                return false;
            tokens = parts;
            return true;
        }

        private static bool TrySplitTopLevel(string inner, bool lenient, out List<string> parts)
        {
            parts = new List<string>();
            var sb = new StringBuilder();
            var quote = '\0';
            for (var i = 0; i < inner.Length; i++)
            {
                var ch = inner[i];
                if (quote != '\0')
                {
                    sb.Append(ch);
                    if (quote == '"' && ch == '\\' && i + 1 < inner.Length)
                        sb.Append(inner[++i]);
                    else if (ch == quote)
                        quote = '\0';
                    continue;
                }
                if ((ch == '"' || ch == '\'') && sb.ToString().Trim().Length == 0)
                {
                    quote = ch;
                    sb.Append(ch);
                    continue;
                }
                if (!lenient && "[]{}".IndexOf(ch) >= 0)
                    return false;
                if (ch == ',')
                {
                    parts.Add(sb.ToString().Trim());
                    sb.Clear();
                    continue;
                }
                sb.Append(ch);
            }
            if (quote != '\0' && !lenient)
                return false;
            if (sb.ToString().Trim().Length > 0 || parts.Count > 0)
                parts.Add(sb.ToString().Trim());
            return true;
        }

        private static string UnquoteKey(string key)
        {
            string value;
            char quote;
            if ((key.StartsWith("\"", StringComparison.Ordinal) || key.StartsWith("'", StringComparison.Ordinal)) &&
                TryReadScalar(key, true, out value, out quote))
                return value;
            return key.TrimEnd();
        }

        // ── 편집 ───────────────────────────────────────────────────────────

        /// <summary>
        /// <paramref name="entryIndex"/>번째 스칼라 속성 값을 바꾼 본문. 원래 따옴표 형식을 지키고(일반 값이 그대로 쓸 수 없는 글자면 큰따옴표),
        /// 키·콜론 뒤 공백·값 뒤 주석은 그대로 둔다. 속성이 없거나 키가 다르거나 스칼라가 아니면 null.
        /// </summary>
        public static string SetValue(string text, int entryIndex, string expectedKey, string value)
        {
            var entry = Find(text, entryIndex, expectedKey);
            if (entry == null || entry.Kind != FrontMatterValueKind.Scalar)
                return null;
            value = OneLine(value);
            string formatted;
            if (entry.Quote == '"')
                formatted = DoubleQuote(value);
            else if (entry.Quote == '\'')
                formatted = "'" + value.Replace("'", "''") + "'";
            else
                formatted = value.Length == 0 ? string.Empty : FormatPlain(value, false);
            return ReplaceLines(text, entry.FirstLine, entry.LineCount, new[] { ComposeLine(entry, formatted) });
        }

        /// <summary>
        /// <paramref name="entryIndex"/>번째 목록 속성 항목을 바꾼 본문. <c>[a, b]</c>는 한 줄로, <c>- a</c> 목록은 같은 들여쓰기로 다시 쓴다.
        /// 원래 있던 항목은 원문(따옴표 포함) 그대로 쓴다. 블록 목록이 비면 <c>key: []</c>. 목록이 아니면 null.
        /// </summary>
        public static string SetItems(string text, int entryIndex, string expectedKey, IList<string> items)
        {
            var entry = Find(text, entryIndex, expectedKey);
            if (entry == null || entry.Kind != FrontMatterValueKind.List)
                return null;
            items = (items ?? new List<string>()).Select(OneLine).Where(s => s.Length > 0).ToList();
            var originals = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < entry.Items.Count && i < entry.ItemTokens.Count; i++)
                if (!originals.ContainsKey(entry.Items[i]))
                    originals[entry.Items[i]] = entry.ItemTokens[i];
            Func<string, string> token = item =>
            {
                string original;
                return originals.TryGetValue(item, out original) ? original : FormatPlain(item, entry.FlowList);
            };

            string[] replacement;
            if (entry.FlowList || items.Count == 0)
            {
                replacement = new[] { ComposeLine(entry, "[" + string.Join(", ", items.Select(token)) + "]") };
            }
            else
            {
                var list = new List<string> { entry.Head + entry.Comment };
                foreach (var item in items)
                    list.Add(entry.ItemIndent + "- " + token(item));
                replacement = list.ToArray();
            }
            return ReplaceLines(text, entry.FirstLine, entry.LineCount, replacement);
        }

        /// <summary>속성 이름을 바꾼 본문(키 줄의 키 부분만 바꾼다). 이름이 비었거나 다른 속성과 겹치면 null.</summary>
        public static string RenameKey(string text, int entryIndex, string expectedKey, string newKey)
        {
            newKey = OneLine(newKey).Trim();
            var doc = Parse(text);
            if (!doc.IsTable || entryIndex < 0 || entryIndex >= doc.Entries.Count || doc.Entries[entryIndex].Key != expectedKey || newKey.Length == 0)
                return null;
            if (newKey == expectedKey)
                return text;
            for (var i = 0; i < doc.Entries.Count; i++)
                if (i != entryIndex && doc.Entries[i].Key == newKey)
                    return null;
            var entry = doc.Entries[entryIndex];
            var lines = text.Split('\n');
            var line = lines[entry.FirstLine];
            var keyMatch = KeyLine.Match(line);
            var keyGroup = keyMatch.Groups["key"];
            lines[entry.FirstLine] = FormatKey(newKey) + line.Substring(keyGroup.Index + keyGroup.Length);
            return string.Join("\n", lines);
        }

        /// <summary>닫는 줄 바로 앞에 새 속성(<c>key:</c> 또는 <c>key: value</c>)을 더한 본문. 속성 표로 읽을 수 없거나 이름이 겹치면 null.</summary>
        public static string AddEntry(string text, string key, string value = "")
        {
            key = OneLine(key).Trim();
            var doc = Parse(text);
            if (!doc.IsTable || key.Length == 0 || doc.Entries.Any(e => e.Key == key))
                return null;
            value = OneLine(value);
            var lines = text.Split('\n').ToList();
            lines.Insert(lines.Count - 1, FormatKey(key) + ":" + (value.Length > 0 ? " " + FormatPlain(value, false) : string.Empty));
            return string.Join("\n", lines);
        }

        /// <summary><paramref name="entryIndex"/>번째 속성 줄들을 지운 본문(위아래 주석·빈 줄은 둔다). 없거나 키가 다르면 null.</summary>
        public static string RemoveEntry(string text, int entryIndex, string expectedKey)
        {
            var entry = Find(text, entryIndex, expectedKey);
            return entry == null ? null : ReplaceLines(text, entry.FirstLine, entry.LineCount, new string[0]);
        }

        /// <summary><paramref name="baseName"/>, <paramref name="baseName"/>2, 3 … 중 아직 없는 속성 이름.</summary>
        public static string UniqueKey(string text, string baseName)
        {
            var doc = Parse(text);
            var used = new HashSet<string>(doc.Entries.Select(e => e.Key), StringComparer.Ordinal);
            if (!used.Contains(baseName))
                return baseName;
            for (var n = 2; ; n++)
                if (!used.Contains(baseName + n))
                    return baseName + n;
        }

        /// <summary>목록 편집 입력(쉼표 구분, 따옴표로 쉼표를 담을 수 있음)을 항목으로 나눈다. 빈 항목은 뺀다.</summary>
        public static List<string> SplitListInput(string input)
        {
            List<string> parts;
            TrySplitTopLevel(OneLine(input), true, out parts);
            var result = new List<string>();
            foreach (var part in parts)
            {
                string value;
                char quote;
                var item = TryReadScalar(part, true, out value, out quote) ? value : part.Trim();
                if (item.Length > 0)
                    result.Add(item);
            }
            return result;
        }

        /// <summary>목록 항목을 편집 입력 글자(쉼표 구분)로. 쉼표·따옴표가 든 항목은 큰따옴표로 감싼다.</summary>
        public static string FormatListForEdit(IEnumerable<string> items)
        {
            return string.Join(", ", (items ?? Enumerable.Empty<string>()).Select(item =>
                item.IndexOf(',') >= 0 || item.StartsWith("\"", StringComparison.Ordinal) || item.StartsWith("'", StringComparison.Ordinal)
                    ? DoubleQuote(item)
                    : item));
        }

        private static FrontMatterEntry Find(string text, int entryIndex, string expectedKey)
        {
            var doc = Parse(text);
            if (!doc.IsTable || entryIndex < 0 || entryIndex >= doc.Entries.Count)
                return null;
            var entry = doc.Entries[entryIndex];
            return entry.Key == expectedKey ? entry : null;
        }

        private static string ComposeLine(FrontMatterEntry entry, string formatted)
        {
            if (formatted.Length == 0)
                return entry.Head + (entry.Comment.Length > 0 ? EnsureLeadingSpace(entry.Comment) : string.Empty);
            return entry.Head + entry.Spacing + formatted + (entry.Comment.Length > 0 ? EnsureLeadingSpace(entry.Comment) : string.Empty);
        }

        private static string EnsureLeadingSpace(string comment)
        {
            return comment[0] == ' ' || comment[0] == '\t' ? comment : " " + comment;
        }

        private static string ReplaceLines(string text, int first, int count, IEnumerable<string> replacement)
        {
            var lines = text.Split('\n').ToList();
            lines.RemoveRange(first, count);
            lines.InsertRange(first, replacement);
            return string.Join("\n", lines);
        }

        private static string OneLine(string value)
        {
            return (value ?? string.Empty).Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
        }

        /// <summary>일반 값으로 써도 같은 글자로 읽히면 그대로, 아니면 큰따옴표. <paramref name="inFlow"/>면 흐름 목록 안이라 쉼표·괄호도 피한다.</summary>
        private static string FormatPlain(string value, bool inFlow)
        {
            return IsPlainSafe(value, inFlow) ? value : DoubleQuote(value);
        }

        private static bool IsPlainSafe(string value, bool inFlow)
        {
            if (value.Length == 0 || value != value.Trim())
                return false;
            var c = value[0];
            if ("[]{}|>&*!%@`#,'\"".IndexOf(c) >= 0)
                return false;
            if ((c == '-' || c == '?' || c == ':') && (value.Length == 1 || value[1] == ' ' || value[1] == '\t'))
                return false;
            if (value.Contains(": ") || value.Contains(" #") || value.EndsWith(":", StringComparison.Ordinal) || value.Contains('\t'))
                return false;
            if (inFlow && value.IndexOfAny(new[] { ',', '[', ']', '{', '}' }) >= 0)
                return false;
            return value.All(ch => !char.IsControl(ch));
        }

        private static string FormatKey(string key)
        {
            if (IsPlainSafe(key, false) && key.IndexOf(':') < 0 && key.IndexOf('#') < 0 && Indicators.IndexOf(key[0]) < 0)
                return key;
            return DoubleQuote(key);
        }

        private static string DoubleQuote(string value)
        {
            var sb = new StringBuilder("\"");
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\n': sb.Append("\\n"); break;
                    default: sb.Append(ch); break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}

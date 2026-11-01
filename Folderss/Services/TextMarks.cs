using Folderss.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Folderss.Services
{
    /// <summary>줄 안 범위 속성 종류(#76 9번): Markdown으로 나타낼 수 없어 사이드카에 둔다.</summary>
    public enum TextMarkKind
    {
        /// <summary>밑줄.</summary>
        Underline,
        /// <summary>글자 색(블록 색과 같은 9가지 이름).</summary>
        Color,
        /// <summary>배경색(형광펜, 같은 9가지 이름).</summary>
        Highlight,
    }

    /// <summary>범위 속성 하나: 보이는 글자 [Start, End)에 <see cref="Kind"/>(색이면 <see cref="Value"/> 이름).</summary>
    public sealed class TextMark
    {
        public TextMarkKind Kind;
        public string Value;
        public int Start;
        public int End;

        public TextMark() { }

        public TextMark(TextMarkKind kind, string value, int start, int end)
        {
            Kind = kind;
            Value = kind == TextMarkKind.Underline ? null : value;
            Start = start;
            End = end;
        }

        public override string ToString()
        {
            return TextMarks.FormatItem(this);
        }
    }

    /// <summary>
    /// 블록 편집기 줄 안 밑줄·글자 색·형광펜(#76 9번)을 블록 속성 <c>marks</c> 하나로 사이드카(#78)에 저장한다. Markdown에는 아무 표식도 넣지 않는다.
    /// <para>
    /// 값 형식: <c>해시|범위;범위…</c>. 범위는 <c>u:시작-끝</c>(밑줄), <c>c=색:시작-끝</c>(글자 색), <c>h=색:시작-끝</c>(배경색) — 예: <c>9f2c41aa|u:0-4;h=yellow:6-11</c>.
    /// 위치는 편집기의 <b>보이는 글자</b>(<see cref="InlineMarkdown.ToPlain(string)"/> — 서식 표식을 뺀 글자, 줄 안 수식은 원문 <c>$…$</c>, UTF-16 단위) 기준이고,
    /// 해시는 그 보이는 글자의 SHA-256 앞 8자다. 보이는 글자가 해시와 다르면(외부 편집·모르는 경로로 본문이 바뀜) 범위를 통째로 버린다 — 엉뚱한 글자에 칠하지 않는다.
    /// </para>
    /// <para>
    /// 편집 중에는 <see cref="EditorBlock.Text"/>가 바뀔 때마다 이전·새 보이는 글자를 비교해 범위를 옮긴다(<see cref="Adjust"/>): 바뀐 구간 앞은 그대로, 뒤는 밀고,
    /// 지운 글자의 범위는 줄인다. 새로 들어간 글자는 바꾼 첫 글자의 범위를, 끼워 넣었으면 바로 앞 글자의 범위를(끝에서 입력하면 이어짐 — Notion과 같음),
    /// 줄 처음이면 바로 뒤 글자의 범위를 따른다(서식 입력란 RichTextBox가 입력 글자를 넣는 Run과 같은 규칙).
    /// 나누기·합치기·붙여넣기처럼 글자가 다른 블록으로 옮겨 가는 경로는 편집기가 <see cref="Slice"/>·<see cref="Concat"/>·<see cref="Replace"/>로 따로 정한다.
    /// </para>
    /// WPF 없음(테스트 프로젝트에서 소스 링크).
    /// </summary>
    public static class TextMarks
    {
        /// <summary>블록 속성 키.</summary>
        public const string Attribute = "marks";

        /// <summary>색 이름(블록 색과 같은 Notion 팔레트). 실제 색은 테마 밝기로 고른다.</summary>
        public static readonly string[] ColorNames = { "gray", "brown", "orange", "yellow", "green", "blue", "purple", "pink", "red" };

        private static readonly List<TextMark> Empty = new List<TextMark>();

        /// <summary>범위 속성을 받는 블록 종류인지: 줄 안 서식 블록(문단·제목·목록·인용·콜아웃·토글 등). 표 셀·코드·원문·수식·front matter·캡션은 아니다.</summary>
        public static bool IsTarget(BlockKind kind)
        {
            return EditorBlock.HasInlineFormatting(kind) && kind != BlockKind.TableRow;
        }

        /// <summary>보이는 글자의 해시(SHA-256 앞 8자, 소문자).</summary>
        public static string Hash(string plain)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plain ?? string.Empty));
            return Convert.ToHexString(bytes, 0, 4).ToLowerInvariant();
        }

        // ── 글자별 모양 ──────────────────────────────────────────────────

        private struct CharMarks
        {
            public bool Underline;
            public string Color;
            public string Highlight;

            public bool IsEmpty => !Underline && Color == null && Highlight == null;
        }

        private static CharMarks[] ToChars(IEnumerable<TextMark> marks, int length)
        {
            var chars = new CharMarks[Math.Max(0, length)];
            if (marks == null)
                return chars;
            foreach (var m in marks)
            {
                if (m == null)
                    continue;
                var a = Math.Max(0, m.Start);
                var b = Math.Min(chars.Length, m.End);
                for (var i = a; i < b; i++)
                {
                    switch (m.Kind)
                    {
                        case TextMarkKind.Underline: chars[i].Underline = true; break;
                        case TextMarkKind.Color: chars[i].Color = m.Value; break;
                        case TextMarkKind.Highlight: chars[i].Highlight = m.Value; break;
                    }
                }
            }
            return chars;
        }

        private static List<TextMark> FromChars(CharMarks[] chars)
        {
            var result = new List<TextMark>();
            AddRanges(result, chars, TextMarkKind.Underline, c => c.Underline ? string.Empty : null);
            AddRanges(result, chars, TextMarkKind.Color, c => c.Color);
            AddRanges(result, chars, TextMarkKind.Highlight, c => c.Highlight);
            return result;
        }

        private static void AddRanges(List<TextMark> result, CharMarks[] chars, TextMarkKind kind, Func<CharMarks, string> value)
        {
            var i = 0;
            while (i < chars.Length)
            {
                var v = value(chars[i]);
                if (v == null)
                {
                    i++;
                    continue;
                }
                var start = i;
                while (i < chars.Length && value(chars[i]) == v)
                    i++;
                result.Add(new TextMark(kind, v, start, i));
            }
        }

        /// <summary>겹침·이웃을 정리한 범위(종류 순 — 밑줄, 글자 색, 배경색 — 그 안에서 위치 순). 같은 종류가 겹치면 뒤의 것이 이긴다.</summary>
        public static List<TextMark> Normalize(IEnumerable<TextMark> marks, int length)
        {
            return FromChars(ToChars(marks, length));
        }

        // ── 편집 연산(보이는 글자 기준) ──────────────────────────────────────

        /// <summary>
        /// 본문이 <paramref name="oldPlain"/>에서 <paramref name="newPlain"/>으로 바뀌었을 때 범위를 옮긴다. 바뀐 구간은 앞뒤 같은 글자를 뺀 한 곳으로 본다.
        /// 새 글자의 범위: 같은 길이로 바꾸면 글자마다 그대로, 아니면 바꾼(지운) 글자가 있으면 그 첫 글자, 끼워 넣기면 바로 앞 글자, 줄 처음이면 바로 뒤 글자를 따른다.
        /// </summary>
        public static List<TextMark> Adjust(IList<TextMark> marks, string oldPlain, string newPlain)
        {
            oldPlain = oldPlain ?? string.Empty;
            newPlain = newPlain ?? string.Empty;
            if (marks == null || marks.Count == 0)
                return new List<TextMark>();
            if (oldPlain == newPlain)
                return Normalize(marks, newPlain.Length);
            var min = Math.Min(oldPlain.Length, newPlain.Length);
            var prefix = 0;
            while (prefix < min && oldPlain[prefix] == newPlain[prefix])
                prefix++;
            var suffix = 0;
            while (suffix < min - prefix && oldPlain[oldPlain.Length - 1 - suffix] == newPlain[newPlain.Length - 1 - suffix])
                suffix++;
            return Edit(marks, oldPlain.Length, prefix, oldPlain.Length - prefix - suffix, newPlain.Length - prefix - suffix);
        }

        /// <summary>보이는 글자 <paramref name="at"/>에서 <paramref name="removed"/>자를 지우고 <paramref name="added"/>자를 넣은 뒤의 범위(새 글자 규칙은 <see cref="Adjust"/>).</summary>
        public static List<TextMark> Edit(IList<TextMark> marks, int oldLength, int at, int removed, int added)
        {
            var old = ToChars(marks, oldLength);
            at = Math.Max(0, Math.Min(at, old.Length));
            removed = Math.Max(0, Math.Min(removed, old.Length - at));
            added = Math.Max(0, added);
            CharMarks inherit;
            if (removed > 0)
                inherit = old[at];
            else if (at > 0)
                inherit = old[at - 1];
            else if (old.Length > 0)
                inherit = old[0];
            else
                inherit = default(CharMarks);
            var result = new CharMarks[old.Length - removed + added];
            Array.Copy(old, 0, result, 0, at);
            // 같은 길이로 바꾸면(제목 전환의 줄바꿈 → 공백, 같은 길이 바꾸기) 글자마다 제 범위를 지킨다.
            for (var i = 0; i < added; i++)
                result[at + i] = removed == added ? old[at + i] : inherit;
            Array.Copy(old, at + removed, result, at + added, old.Length - at - removed);
            return FromChars(result);
        }

        /// <summary>[start, end) 구간 범위(0부터 다시 셈).</summary>
        public static List<TextMark> Slice(IList<TextMark> marks, int start, int end)
        {
            var result = new List<TextMark>();
            if (marks == null || end <= start)
                return result;
            foreach (var m in marks)
            {
                var a = Math.Max(start, m.Start);
                var b = Math.Min(end, m.End);
                if (b > a)
                    result.Add(new TextMark(m.Kind, m.Value, a - start, b - start));
            }
            return Normalize(result, end - start);
        }

        /// <summary>앞 글자(길이 <paramref name="headLength"/>)의 범위와 뒤 글자의 범위를 잇는다. 이음매에서 같은 범위면 하나로 합친다.</summary>
        public static List<TextMark> Concat(IList<TextMark> head, int headLength, IList<TextMark> tail, int tailLength)
        {
            var all = new List<TextMark>();
            if (head != null)
                all.AddRange(head.Select(m => new TextMark(m.Kind, m.Value, Math.Min(m.Start, headLength), Math.Min(m.End, headLength))));
            if (tail != null)
                all.AddRange(tail.Select(m => new TextMark(m.Kind, m.Value, headLength + m.Start, headLength + Math.Min(m.End, tailLength))));
            return Normalize(all, headLength + tailLength);
        }

        /// <summary>[start, end)를 지우고 그 자리에 길이 <paramref name="insertLength"/>의 글자(범위 <paramref name="insert"/>)를 넣는다(붙여넣기 — 넣는 글자는 앞 글자 범위를 따르지 않음).</summary>
        public static List<TextMark> Replace(IList<TextMark> marks, int length, int start, int end, IList<TextMark> insert, int insertLength)
        {
            start = Math.Max(0, Math.Min(start, length));
            end = Math.Max(start, Math.Min(end, length));
            var head = Slice(marks, 0, start);
            var tail = Slice(marks, end, length);
            var joined = Concat(head, start, insert, insertLength);
            return Concat(joined, start + insertLength, tail, length - end);
        }

        /// <summary>[start, end)에 범위를 켜거나(<paramref name="value"/> — 밑줄은 아무 값, 색은 이름) 끈다(null). 다른 종류는 그대로.</summary>
        public static List<TextMark> Apply(IList<TextMark> marks, int length, TextMarkKind kind, string value, int start, int end)
        {
            var chars = ToChars(marks, length);
            start = Math.Max(0, start);
            end = Math.Min(length, end);
            for (var i = start; i < end; i++)
            {
                switch (kind)
                {
                    case TextMarkKind.Underline: chars[i].Underline = value != null; break;
                    case TextMarkKind.Color: chars[i].Color = value; break;
                    case TextMarkKind.Highlight: chars[i].Highlight = value; break;
                }
            }
            return FromChars(chars);
        }

        /// <summary>[start, end) 글자가 모두 그 범위인지(<paramref name="value"/>가 null이 아니면 그 색인지까지). 빈 구간이면 false.</summary>
        public static bool Covers(IList<TextMark> marks, int length, TextMarkKind kind, int start, int end, string value = null)
        {
            if (end <= start)
                return false;
            var chars = ToChars(marks, length);
            start = Math.Max(0, start);
            end = Math.Min(length, end);
            if (end <= start)
                return false;
            for (var i = start; i < end; i++)
            {
                var v = kind == TextMarkKind.Underline ? (chars[i].Underline ? string.Empty : null) : kind == TextMarkKind.Color ? chars[i].Color : chars[i].Highlight;
                if (v == null || (value != null && kind != TextMarkKind.Underline && v != value))
                    return false;
            }
            return true;
        }

        /// <summary>두 범위 목록이 같은지(정리한 뒤 비교).</summary>
        public static bool SameMarks(IList<TextMark> a, IList<TextMark> b, int length)
        {
            var x = Normalize(a ?? Empty, length);
            var y = Normalize(b ?? Empty, length);
            if (x.Count != y.Count)
                return false;
            for (var i = 0; i < x.Count; i++)
            {
                if (x[i].Kind != y[i].Kind || x[i].Value != y[i].Value || x[i].Start != y[i].Start || x[i].End != y[i].End)
                    return false;
            }
            return true;
        }

        // ── 값 쓰기·읽기 ────────────────────────────────────────────────

        /// <summary>속성 값(<c>해시|범위;…</c>). 범위가 없으면 null(속성을 지움).</summary>
        public static string Format(IList<TextMark> marks, string plain)
        {
            plain = plain ?? string.Empty;
            var normalized = Normalize(marks ?? Empty, plain.Length);
            if (normalized.Count == 0)
                return null;
            return Hash(plain) + "|" + string.Join(";", normalized.Select(FormatItem));
        }

        internal static string FormatItem(TextMark m)
        {
            var range = m.Start.ToString(CultureInfo.InvariantCulture) + "-" + m.End.ToString(CultureInfo.InvariantCulture);
            switch (m.Kind)
            {
                case TextMarkKind.Underline: return "u:" + range;
                case TextMarkKind.Color: return "c=" + m.Value + ":" + range;
                default: return "h=" + m.Value + ":" + range;
            }
        }

        /// <summary>
        /// 속성 값을 읽는다. 형식이 틀리거나, 해시가 <paramref name="plain"/>과 다르거나, 범위가 글자 밖이면 false(범위를 쓰지 않음).
        /// </summary>
        public static bool TryParse(string value, string plain, out List<TextMark> marks)
        {
            marks = new List<TextMark>();
            if (string.IsNullOrEmpty(value))
                return false;
            plain = plain ?? string.Empty;
            var bar = value.IndexOf('|');
            if (bar <= 0 || value.Substring(0, bar) != Hash(plain))
                return false;
            var body = value.Substring(bar + 1);
            if (body.Length == 0)
                return false;
            foreach (var item in body.Split(';'))
            {
                TextMark mark;
                if (!TryParseItem(item, out mark) || mark.End > plain.Length)
                {
                    marks.Clear();
                    return false;
                }
                marks.Add(mark);
            }
            marks = Normalize(marks, plain.Length);
            return true;
        }

        private static bool TryParseItem(string item, out TextMark mark)
        {
            mark = null;
            var colon = item.LastIndexOf(':');
            if (colon <= 0)
                return false;
            var head = item.Substring(0, colon);
            var range = item.Substring(colon + 1);
            var dash = range.IndexOf('-');
            int start, end;
            if (dash <= 0 ||
                !int.TryParse(range.Substring(0, dash), NumberStyles.None, CultureInfo.InvariantCulture, out start) ||
                !int.TryParse(range.Substring(dash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out end) ||
                end <= start)
                return false;
            if (head == "u")
            {
                mark = new TextMark(TextMarkKind.Underline, null, start, end);
                return true;
            }
            if (head.Length > 2 && head[1] == '=' && (head[0] == 'c' || head[0] == 'h'))
            {
                var name = head.Substring(2);
                if (!name.All(ch => ch >= 'a' && ch <= 'z'))
                    return false;
                mark = new TextMark(head[0] == 'c' ? TextMarkKind.Color : TextMarkKind.Highlight, name, start, end);
                return true;
            }
            return false;
        }

        // ── 블록 ─────────────────────────────────────────────────────────

        /// <summary>블록 본문의 보이는 글자(범위 위치 기준).</summary>
        public static string PlainOf(EditorBlock block)
        {
            return InlineMarkdown.ToPlain(block?.Text ?? string.Empty);
        }

        /// <summary>블록의 범위(대상 종류가 아니거나, 없거나, 본문과 맞지 않으면 빈 목록).</summary>
        public static List<TextMark> Get(EditorBlock block)
        {
            return Get(block, null);
        }

        /// <summary><see cref="Get(EditorBlock)"/> — 보이는 글자를 이미 알면 넘겨 다시 읽지 않는다.</summary>
        public static List<TextMark> Get(EditorBlock block, string plain)
        {
            var value = block?.GetAttribute(Attribute);
            if (value == null || !IsTarget(block.Kind))
                return new List<TextMark>();
            List<TextMark> marks;
            return TryParse(value, plain ?? PlainOf(block), out marks) ? marks : new List<TextMark>();
        }

        /// <summary>
        /// 블록에 범위를 넣는다. <paramref name="basisPlain"/>은 범위를 계산한 보이는 글자 — 블록 본문의 보이는 글자와 다르면(Markdown으로 다시 쓰며 글자가 달라진 드문 경우)
        /// 그 차이만큼 옮겨 넣는다. 범위가 없으면 속성을 지운다.
        /// </summary>
        public static void Set(EditorBlock block, IList<TextMark> marks, string basisPlain = null)
        {
            if (block == null)
                return;
            if (marks == null || marks.Count == 0 || !IsTarget(block.Kind))
            {
                block.SetAttribute(Attribute, null);
                return;
            }
            var plain = PlainOf(block);
            var list = basisPlain != null && basisPlain != plain ? Adjust(marks, basisPlain, plain) : Normalize(marks, plain.Length);
            block.SetAttribute(Attribute, Format(list, plain));
        }

        /// <summary>본문을 바꾸면서 범위를 정한다(본문 변경에 따른 자동 옮기기 없이 — 서식 입력란처럼 범위를 이미 아는 경로).</summary>
        public static void SetText(EditorBlock block, string text, IList<TextMark> marks, string basisPlain)
        {
            block.SetTextOnly(text);
            if ((marks == null || marks.Count == 0) && block.GetAttribute(Attribute) == null)
                return;
            Set(block, marks, basisPlain);
        }

        /// <summary>
        /// <see cref="EditorBlock.Text"/>가 바뀔 때(범위 속성이 있을 때만) 부른다: 이전 본문 기준으로 맞는 범위를 새 본문으로 옮긴다.
        /// 이전 본문과 맞지 않던 범위·대상이 아닌 종류는 지운다.
        /// </summary>
        internal static void OnTextChanging(EditorBlock block, string oldText, string newText)
        {
            var value = block.GetAttribute(Attribute);
            if (value == null)
                return;
            if (!IsTarget(block.Kind))
            {
                block.SetAttribute(Attribute, null);
                return;
            }
            var oldPlain = InlineMarkdown.ToPlain(oldText ?? string.Empty);
            List<TextMark> marks;
            if (!TryParse(value, oldPlain, out marks))
            {
                block.SetAttribute(Attribute, null);
                return;
            }
            var newPlain = InlineMarkdown.ToPlain(newText ?? string.Empty);
            block.SetAttribute(Attribute, Format(Adjust(marks, oldPlain, newPlain), newPlain));
        }

        /// <summary>범위 속성이 본문과 맞는지 확인하고, 맞지 않으면 지운다. 지웠으면 true(사이드카를 다시 붙일 때 "붙이지 못한 속성"으로 셈).</summary>
        public static bool DropIfInvalid(EditorBlock block)
        {
            var value = block?.GetAttribute(Attribute);
            if (value == null)
                return false;
            List<TextMark> marks;
            if (IsTarget(block.Kind) && TryParse(value, PlainOf(block), out marks))
                return false;
            block.SetAttribute(Attribute, null);
            return true;
        }

        // ── 조각(InlineRun) ↔ 범위 ──────────────────────────────────────

        private static int RunLength(InlineRun r)
        {
            return r.Kind == InlineRunKind.Math ? r.Text.Length + 2 : r.Text.Length;
        }

        /// <summary>조각에 담긴 범위(<see cref="InlineRun.Underline"/>·<see cref="InlineRun.TextColor"/>·<see cref="InlineRun.Highlight"/>)를 보이는 글자 범위로.</summary>
        public static List<TextMark> FromRuns(IList<InlineRun> runs)
        {
            var result = new List<TextMark>();
            if (runs == null)
                return result;
            var pos = 0;
            var any = false;
            foreach (var r in runs)
            {
                var len = RunLength(r);
                if (len > 0 && r.HasMarks)
                {
                    any = true;
                    if (r.Underline) result.Add(new TextMark(TextMarkKind.Underline, null, pos, pos + len));
                    if (r.TextColor != null) result.Add(new TextMark(TextMarkKind.Color, r.TextColor, pos, pos + len));
                    if (r.Highlight != null) result.Add(new TextMark(TextMarkKind.Highlight, r.Highlight, pos, pos + len));
                }
                pos += len;
            }
            return any ? Normalize(result, pos) : result;
        }

        /// <summary>범위를 뺀 조각 사본(범위가 없으면 그대로 사본).</summary>
        public static List<InlineRun> Strip(IEnumerable<InlineRun> runs)
        {
            return runs.Select(r =>
            {
                var c = r.Clone();
                c.Underline = false;
                c.TextColor = null;
                c.Highlight = null;
                return c;
            }).ToList();
        }

        /// <summary>
        /// 조각에 범위를 입힌 새 목록(경계에서 조각을 나눔). 글자·원문 조각은 나누고, 수식 조각(표시용)은 나누지 않고 첫 글자의 범위를 따른다.
        /// 원래 조각에 있던 범위는 지운다.
        /// </summary>
        public static List<InlineRun> ApplyToRuns(IList<InlineRun> runs, IList<TextMark> marks)
        {
            var total = runs.Sum(RunLength);
            var chars = ToChars(marks, total);
            var result = new List<InlineRun>(runs.Count);
            var pos = 0;
            foreach (var r in runs)
            {
                var len = RunLength(r);
                if (len == 0 || r.Kind == InlineRunKind.Math)
                {
                    var whole = r.Clone();
                    var c = len > 0 ? chars[pos] : default(CharMarks);
                    SetMarks(whole, c);
                    result.Add(whole);
                    pos += len;
                    continue;
                }
                var i = 0;
                while (i < len)
                {
                    var c = chars[pos + i];
                    var j = i + 1;
                    while (j < len && SameChar(chars[pos + j], c))
                        j++;
                    var piece = r.Clone();
                    piece.Text = r.Text.Substring(i, j - i);
                    SetMarks(piece, c);
                    result.Add(piece);
                    i = j;
                }
                pos += len;
            }
            return result;
        }

        private static bool SameChar(CharMarks a, CharMarks b)
        {
            return a.Underline == b.Underline && a.Color == b.Color && a.Highlight == b.Highlight;
        }

        private static void SetMarks(InlineRun run, CharMarks c)
        {
            run.Underline = c.Underline;
            run.TextColor = c.Color;
            run.Highlight = c.Highlight;
        }
    }
}

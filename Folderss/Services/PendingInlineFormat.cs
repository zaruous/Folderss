using System.Collections.Generic;

namespace Folderss.Services
{
    /// <summary>
    /// 블록 편집기 서식 입력란의 "다음 입력 서식"(#76 대기 서식). 선택 없이 <c>Ctrl+B/I/E/Shift+S/U</c>(·색)를 누르거나 입력 단축(<c>**굵**</c>)을 변환한 뒤,
    /// 그 자리에 들어오는 글자에 켜거나 끌 서식을 들고 있다가 입력된 글자 범위에 적용한다(<see cref="Apply"/>). WPF RichTextBox의 선택 서식(springload)은
    /// 코드로 내용을 넣은 문서에서 무시되므로 쓰지 않는다. WPF 없음(테스트 프로젝트에서 소스 링크).
    /// <para>
    /// 우선순위: 대기 서식에 정한 속성만 새 글자에 강제한다. 정하지 않은 속성(서식·링크·밑줄·색)은 입력 자리의 보통 규칙
    /// (서식은 앞 조각을 이음, 범위는 <see cref="TextMarks.Edit"/>의 "끝에서 입력하면 이어짐")을 그대로 따른다 — 같은 속성에서는 대기 서식이 이긴다.
    /// </para>
    /// </summary>
    public sealed class PendingInlineFormat
    {
        /// <summary>새 글자에 켤 서식.</summary>
        public InlineFormat On;

        /// <summary>새 글자에서 끌 서식.</summary>
        public InlineFormat Off;

        /// <summary>새 글자의 링크를 끈다(링크 끝에서 이어 쓰는 글자가 링크에 들어가지 않게).</summary>
        public bool LinkOff;

        /// <summary>새 글자를 이 링크(원문 주소)로(null이면 정하지 않음).</summary>
        public string Link;

        /// <summary>새 글자의 밑줄(null이면 정하지 않음 — 범위 경계 규칙대로).</summary>
        public bool? Underline;

        /// <summary>글자 색을 정했는지와 그 이름(null이면 지움).</summary>
        public bool ColorSet;
        public string Color;

        /// <summary>배경색(형광펜)을 정했는지와 그 이름(null이면 지움).</summary>
        public bool HighlightSet;
        public string Highlight;

        public bool IsEmpty => On == InlineFormat.None && Off == InlineFormat.None && !LinkOff && Link == null && Underline == null && !ColorSet && !HighlightSet;

        /// <summary>
        /// <paramref name="caret"/>에 글자를 넣으면 들어갈 조각(입력란은 줄 처음·줄바꿈 뒤면 뒤 조각, 그 밖은 앞 조각에 글자를 넣는다). 없으면 null.
        /// </summary>
        public static InlineRun BaseRun(IList<InlineRun> runs, int caret)
        {
            if (runs == null || runs.Count == 0)
                return null;
            var plain = InlineMarkdown.ToPlain(runs);
            if (caret <= 0 || (caret <= plain.Length && plain[caret - 1] == '\n'))
                return InlineMarkdown.RunAt(runs, caret, false);
            return InlineMarkdown.RunAt(runs, caret, true);
        }

        /// <summary>대기 서식을 더한 뒤 그 자리 새 글자의 서식이 켜지는지.</summary>
        public bool Effective(IList<InlineRun> runs, int caret, InlineFormat format)
        {
            if ((On & format) == format)
                return true;
            if ((Off & format) == format)
                return false;
            var run = BaseRun(runs, caret);
            return run != null && run.Kind == InlineRunKind.Text && run.Has(format);
        }

        /// <summary>대기 서식을 더한 뒤 그 자리 새 글자에 밑줄이 있는지.</summary>
        public bool EffectiveUnderline(IList<InlineRun> runs, int caret)
        {
            if (Underline.HasValue)
                return Underline.Value;
            var run = BaseRun(runs, caret);
            return run != null && run.Underline;
        }

        /// <summary>서식 키를 한 번 더 누름: 지금(대기 서식 포함) 상태의 반대로 정한다.</summary>
        public void Toggle(IList<InlineRun> runs, int caret, InlineFormat format)
        {
            Set(format, !Effective(runs, caret, format));
        }

        public void Set(InlineFormat format, bool on)
        {
            if (on)
            {
                On |= format;
                Off &= ~format;
            }
            else
            {
                Off |= format;
                On &= ~format;
            }
        }

        public void ToggleUnderline(IList<InlineRun> runs, int caret)
        {
            Underline = !EffectiveUnderline(runs, caret);
        }

        public void SetColor(bool highlight, string name)
        {
            if (highlight)
            {
                HighlightSet = true;
                Highlight = name;
            }
            else
            {
                ColorSet = true;
                Color = name;
            }
        }

        /// <summary>
        /// 입력 단축 변환·링크 넣기 직후: <paramref name="caret"/> 앞 조각의 서식(굵게·기울임·취소선·코드)과 링크를 이어 쓰는 글자에서 끈다(Notion과 같음).
        /// 밑줄·색 범위는 정하지 않는다(범위 경계 규칙대로). 끌 것이 없으면 null.
        /// </summary>
        public static PendingInlineFormat AfterClosedFormat(IList<InlineRun> runs, int caret)
        {
            var before = caret > 0 ? InlineMarkdown.RunAt(runs, caret, true) : null;
            if (before == null || before.Kind != InlineRunKind.Text)
                return null;
            var off = before.Format & (InlineFormat.Bold | InlineFormat.Italic | InlineFormat.Strike | InlineFormat.Code);
            if (off == InlineFormat.None && before.Link == null)
                return null;
            return new PendingInlineFormat { Off = off, LinkOff = before.Link != null };
        }

        /// <summary>
        /// 선택한 글자를 덮어 입력할 때: 새 글자가 바뀌는 첫 글자(<paramref name="first"/>)의 서식·링크·범위를 그대로 따르게 한다
        /// (찾아 바꾸기 <see cref="InlineMarkdown.ReplaceText"/>·<see cref="TextMarks.Edit"/>와 같은 규칙). WPF는 지운 조각의 글자 장식·바탕을 이어 주지 않는다.
        /// </summary>
        public static PendingInlineFormat Like(InlineRun first)
        {
            if (first == null)
                return null;
            const InlineFormat all = InlineFormat.Bold | InlineFormat.Italic | InlineFormat.Strike | InlineFormat.Code;
            var on = first.Format & all;
            return new PendingInlineFormat
            {
                On = on,
                Off = all & ~on,
                Link = first.Link,
                LinkOff = first.Link == null,
                Underline = first.Underline,
                ColorSet = true,
                Color = first.TextColor,
                HighlightSet = true,
                Highlight = first.Highlight,
            };
        }

        /// <summary>보이는 글자 [start, end)(방금 입력한 글자)에 대기 서식을 적용한 조각 목록.</summary>
        public List<InlineRun> Apply(IList<InlineRun> runs, int start, int end)
        {
            var result = new List<InlineRun>(runs);
            if (end <= start)
                return result;
            foreach (var f in new[] { InlineFormat.Bold, InlineFormat.Italic, InlineFormat.Strike, InlineFormat.Code })
            {
                if ((On & f) == f)
                    result = InlineMarkdown.ApplyFormat(result, start, end, f, true);
                else if ((Off & f) == f)
                    result = InlineMarkdown.ApplyFormat(result, start, end, f, false);
            }
            if (Link != null)
                result = InlineMarkdown.ApplyLink(result, start, end, Link);
            else if (LinkOff)
                result = InlineMarkdown.ApplyLink(result, start, end, null);
            if (Underline.HasValue || ColorSet || HighlightSet)
            {
                var length = InlineMarkdown.ToPlain(result).Length;
                var marks = TextMarks.FromRuns(result);
                if (Underline.HasValue)
                    marks = TextMarks.Apply(marks, length, TextMarkKind.Underline, Underline.Value ? string.Empty : null, start, end);
                if (ColorSet)
                    marks = TextMarks.Apply(marks, length, TextMarkKind.Color, Color, start, end);
                if (HighlightSet)
                    marks = TextMarks.Apply(marks, length, TextMarkKind.Highlight, Highlight, start, end);
                result = TextMarks.ApplyToRuns(TextMarks.Strip(result), marks);
            }
            return result;
        }
    }
}

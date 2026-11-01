using Folderss.Services;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Folderss.Controls
{
    /// <summary>
    /// 줄 안 서식(#76) 조각(<see cref="InlineRun"/>) ↔ WPF Inline. 표시용 TextBlock(링크는 <see cref="Span"/> — Hyperlink는 누르기를 먹어 편집 진입을 막음)과
    /// 편집용 RichTextBox(링크는 <see cref="Hyperlink"/>, 조각 원형은 Run.Tag)에 같은 모양으로 그린다. 색은 테마 리소스 참조(DynamicResource)라 테마를 바꾸면 따라간다.
    /// </summary>
    internal static class BlockInlineRenderer
    {
        public static readonly FontFamily CodeFont = new FontFamily("Cascadia Mono, Consolas, D2Coding, Malgun Gothic");

        // 가상화로 행을 다시 묶을 때 같은 글자를 다시 읽지 않게(문단 글자 → 조각, 읽기 전용으로만 씀).
        private static readonly Dictionary<string, List<InlineRun>> ParseCache = new Dictionary<string, List<InlineRun>>();
        private const int ParseCacheLimit = 4096;

        private static List<InlineRun> ParseDisplay(string text)
        {
            List<InlineRun> runs;
            if (ParseCache.TryGetValue(text, out runs))
                return runs;
            runs = InlineMarkdown.Parse(text, bareUrls: true);
            if (ParseCache.Count >= ParseCacheLimit)
                ParseCache.Clear();
            ParseCache[text] = runs;
            return runs;
        }

        /// <summary>
        /// 표시용 TextBlock 내용을 채운다. <paramref name="formatted"/>면 줄 안 Markdown을 서식으로(표식이 없을 법한 글자는 Text 하나로 — 빠른 길),
        /// 아니면 글자 그대로. 같은 글자·크기로 이미 그렸으면 아무것도 하지 않는다(가상화로 행을 다시 묶을 때·표 행 동기화 때).
        /// </summary>
        public static void PopulateDisplay(System.Windows.Controls.TextBlock target, string text, double fontSize, bool formatted)
        {
            text = text ?? string.Empty;
            if (!formatted || InlineMarkdown.IsPlain(text))
            {
                if (target.Tag != null || target.Inlines.Count > 1 || target.Text != text)
                {
                    target.Tag = null;
                    target.Inlines.Clear();
                    target.Text = text;
                }
                return;
            }
            var key = new DisplayKey(text, fontSize);
            if (key.Equals(target.Tag))
                return;
            target.Inlines.Clear();
            var start = 0;
            foreach (var run in ParseDisplay(text))
            {
                var length = run.Kind == InlineRunKind.Math ? run.Text.Length + 2 : run.Text.Length;
                target.Inlines.Add(CreateDisplayInline(run, fontSize, new DisplayPos(start, length, run.Kind == InlineRunKind.Math)));
                start += length;
            }
            target.Tag = key;
        }

        /// <summary>표시 조각이 편집기(보이는 글자)에서 차지하는 자리 — 표시 글자를 누른 자리에 편집 커서를 두기 위함.</summary>
        private sealed class DisplayPos
        {
            public DisplayPos(int start, int length, bool atomic) { Start = start; Length = length; Atomic = atomic; }
            public int Start { get; }
            public int Length { get; }
            public bool Atomic { get; }
        }

        /// <summary>표시용 TextBlock의 점에 해당하는 편집기 커서 위치(보이는 글자 기준). 모르면 -1.</summary>
        public static int DisplayOffsetAt(System.Windows.Controls.TextBlock target, Point point)
        {
            TextPointer pointer;
            try
            {
                pointer = target.GetPositionFromPoint(point, true);
            }
            catch (InvalidOperationException)
            {
                return -1;
            }
            if (pointer == null)
                return -1;
            for (DependencyObject element = pointer.Parent; element != null && !(element is System.Windows.Controls.TextBlock); element = ElementTree.GetParent(element))
            {
                if (element is TextElement te && te.Tag is DisplayPos pos)
                {
                    if (pos.Atomic)
                        return pos.Start;
                    var within = element is Run run ? run.ContentStart.GetOffsetToPosition(pointer) : 0;
                    return pos.Start + Math.Max(0, Math.Min(pos.Length, within));
                }
            }
            // 서식 없는 글자(Text 하나) — 글자 위치 그대로
            if (pointer.Parent is Run plain)
                return Math.Max(0, plain.ContentStart.GetOffsetToPosition(pointer));
            var adjacent = pointer.GetAdjacentElement(LogicalDirection.Forward) as TextElement;
            if (adjacent?.Tag is DisplayPos next)
                return next.Start;
            return -1;
        }

        private sealed class DisplayKey
        {
            private readonly string _text;
            private readonly double _size;
            public DisplayKey(string text, double size) { _text = text; _size = size; }
            public override bool Equals(object obj) => obj is DisplayKey k && k._text == _text && k._size == _size;
            public override int GetHashCode() => _text.GetHashCode();
        }

        /// <summary>테마가 바뀌어도 리소스 참조로 따라가므로 다시 그릴 필요는 없다. 강제로 다시 그리게 표시 기억을 지운다.</summary>
        public static void InvalidateDisplay(System.Windows.Controls.TextBlock target)
        {
            if (target.Tag is DisplayKey)
                target.Tag = null;
        }

        private static Inline CreateDisplayInline(InlineRun run, double fontSize, DisplayPos pos)
        {
            if (run.Kind == InlineRunKind.Math)
            {
                var math = BlockMathRenderer.CreateInlineMathSpan(run.Text, fontSize);
                math.Tag = pos;
                ApplyFormat(math, run, true);
                return math;
            }
            var inline = new Run(ToDisplayText(run.Text)) { Tag = pos };
            if (run.Kind == InlineRunKind.Raw)
                inline.SetResourceReference(TextElement.ForegroundProperty, "SecondaryText");
            ApplyFormat(inline, run, true);
            if (run.Link == null)
                return inline;
            var link = new Span(inline) { Tag = new LinkTag(run.Url) };
            StyleLink(link);
            link.ToolTip = run.Url + "\nCtrl+클릭: 열기";
            return link;
        }

        private static string ToDisplayText(string text)
        {
            // 표시에서는 \n 을 줄바꿈으로(Run은 \n 을 그대로 줄바꿈으로 그린다)
            return text;
        }

        private static void StyleLink(TextElement link)
        {
            link.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
            if (link is Inline inline)
                inline.TextDecorations = TextDecorations.Underline;
        }

        private static void ApplyFormat(Inline inline, InlineRun run, bool display)
        {
            if (run.Has(InlineFormat.Bold))
                inline.FontWeight = FontWeights.Bold;
            if (run.Has(InlineFormat.Italic))
                inline.FontStyle = FontStyles.Italic;
            if (run.Has(InlineFormat.Strike))
                inline.TextDecorations = TextDecorations.Strikethrough;
            if (run.Has(InlineFormat.Code))
            {
                inline.FontFamily = CodeFont;
                inline.SetResourceReference(TextElement.BackgroundProperty, "SurfaceBackground");
                if (display)
                    inline.SetResourceReference(TextElement.ForegroundProperty, "PrimaryText");
            }
        }

        /// <summary>표시용 링크 조각의 주소(<see cref="Span.Tag"/>).</summary>
        public sealed class LinkTag
        {
            public LinkTag(string url) { Url = url; }
            public string Url { get; }
        }

        /// <summary>표시용 TextBlock에서 누른 글자의 링크 주소(없으면 null). OriginalSource는 Run(ContentElement)이라 <see cref="ElementTree"/>로 올라간다.</summary>
        public static string DisplayLinkAt(DependencyObject source)
        {
            for (var element = source; element != null; element = ElementTree.GetParent(element))
            {
                if (element is Span span && span.Tag is LinkTag tag)
                    return tag.Url;
                if (element is System.Windows.Controls.TextBlock)
                    return null;
            }
            return null;
        }

        // ── 편집기(RichTextBox) ───────────────────────────────────────────

        /// <summary>편집기 문단을 조각 목록으로 채운다. 조각마다 Run 하나(링크는 Hyperlink로 감쌈), Run.Tag에 조각 원형(표식 모양·원문 종류).</summary>
        public static void BuildEditorInlines(InlineCollection target, IList<InlineRun> runs)
        {
            target.Clear();
            Hyperlink link = null;
            InlineRun linkRun = null;
            foreach (var run in runs)
            {
                var parts = (run.Text ?? string.Empty).Split('\n');
                var container = target;
                if (run.Link != null)
                {
                    if (link == null || linkRun.Link != run.Link || linkRun.LinkTitle != run.LinkTitle)
                    {
                        link = new Hyperlink { Tag = new LinkInfo(run.Link, run.LinkTitle, run.LinkOuter, run.Autolink) };
                        StyleLink(link);
                        link.ToolTip = run.Url + "\nCtrl+클릭: 열기";
                        target.Add(link);
                        linkRun = run;
                    }
                    container = link.Inlines;
                }
                else
                {
                    link = null;
                    linkRun = null;
                }
                for (var i = 0; i < parts.Length; i++)
                {
                    if (i > 0)
                        container.Add(new LineBreak());
                    if (parts[i].Length == 0 && parts.Length > 1)
                        continue;
                    var inline = new Run(parts[i]) { Tag = Prototype(run) };
                    if (run.Kind == InlineRunKind.Raw)
                        inline.SetResourceReference(TextElement.ForegroundProperty, "SecondaryText");
                    ApplyFormat(inline, run, false);
                    container.Add(inline);
                }
            }
        }

        private static InlineRun Prototype(InlineRun run)
        {
            var proto = run.Clone();
            proto.Text = string.Empty;
            return proto;
        }

        /// <summary>편집기 Hyperlink의 링크 정보.</summary>
        public sealed class LinkInfo
        {
            public LinkInfo(string link, string title, InlineFormat outer, bool autolink)
            {
                Link = link;
                Title = title;
                Outer = outer;
                Autolink = autolink;
            }
            public string Link { get; }
            public string Title { get; }
            public InlineFormat Outer { get; }
            public bool Autolink { get; }
        }

        /// <summary>
        /// 편집기 문서를 조각 목록으로 읽는다. 서식은 Run과 그 위 요소의 지역 값(FontWeight·FontStyle·TextDecorations·코드 글꼴)으로,
        /// 링크는 감싼 Hyperlink로, 표식 모양·원문 여부는 Run.Tag 원형으로 정한다(입력으로 새로 생긴 Run은 원형이 없어 일반 글자).
        /// 문단이 여럿이면(붙여넣기 등) 줄바꿈으로 잇는다.
        /// </summary>
        public static List<InlineRun> ReadRuns(FlowDocument document)
        {
            var runs = new List<InlineRun>();
            var first = true;
            foreach (var block in document.Blocks)
            {
                if (!first)
                    runs.Add(new InlineRun { Text = "\n" });
                first = false;
                if (block is Paragraph paragraph)
                    ReadInlines(paragraph.Inlines, runs);
                else
                    runs.Add(new InlineRun { Text = new TextRange(block.ContentStart, block.ContentEnd).Text.Replace("\r\n", "\n") });
            }
            return InlineMarkdown.Merge(runs);
        }

        private static void ReadInlines(InlineCollection inlines, List<InlineRun> runs)
        {
            foreach (var inline in inlines)
            {
                if (inline is Run run)
                {
                    if (run.Text.Length > 0)
                        runs.Add(ReadRun(run));
                }
                else if (inline is LineBreak)
                {
                    runs.Add(new InlineRun { Text = "\n" });
                }
                else if (inline is Span span)
                {
                    ReadInlines(span.Inlines, runs);
                }
                else if (inline is InlineUIContainer)
                {
                    // 붙여넣은 그림 등은 글자로 나타낼 수 없어 버린다.
                }
            }
        }

        private static InlineRun ReadRun(Run run)
        {
            var proto = run.Tag as InlineRun;
            var result = proto != null ? proto.Clone() : new InlineRun();
            result.Text = run.Text.Replace("\r\n", "\n").Replace('\r', '\n');
            result.Format = InlineFormat.None;
            result.Link = null;
            result.LinkTitle = null;
            result.LinkOuter = InlineFormat.None;
            result.Autolink = false;
            result.BareUrl = false;

            bool? bold = null, italic = null;
            var strike = false;
            var code = false;
            for (DependencyObject element = run; element != null && !(element is Paragraph) && !(element is FlowDocument); element = LogicalTreeHelper.GetParent(element))
            {
                var te = element as TextElement;
                if (te == null)
                    break;
                if (bold == null && te.ReadLocalValue(TextElement.FontWeightProperty) is FontWeight weight)
                    bold = weight.ToOpenTypeWeight() >= FontWeights.SemiBold.ToOpenTypeWeight();
                if (italic == null && te.ReadLocalValue(TextElement.FontStyleProperty) is FontStyle style)
                    italic = style == FontStyles.Italic || style == FontStyles.Oblique;
                if (te is Inline inline && inline.ReadLocalValue(Inline.TextDecorationsProperty) is TextDecorationCollection decorations)
                {
                    foreach (var d in decorations)
                        if (d.Location == TextDecorationLocation.Strikethrough)
                            strike = true;
                }
                if (!code && te.ReadLocalValue(TextElement.FontFamilyProperty) is FontFamily family && family.Source == CodeFont.Source)
                    code = true;
                if (element is Hyperlink hyperlink && result.Link == null)
                {
                    if (hyperlink.Tag is LinkInfo info)
                    {
                        result.Link = info.Link;
                        result.LinkTitle = info.Title;
                        result.LinkOuter = info.Outer;
                        result.Autolink = info.Autolink;
                    }
                    else if (hyperlink.NavigateUri != null)
                    {
                        result.Link = hyperlink.NavigateUri.OriginalString;
                    }
                }
            }
            if (bold == true) result.Format |= InlineFormat.Bold;
            if (italic == true) result.Format |= InlineFormat.Italic;
            if (strike) result.Format |= InlineFormat.Strike;
            if (code)
            {
                result.Format |= InlineFormat.Code;
                if (result.Kind != InlineRunKind.Text)
                    result.Kind = InlineRunKind.Text;
            }
            if (result.Kind == InlineRunKind.Math)
                result.Kind = InlineRunKind.Raw;
            return result;
        }

        /// <summary>조각 목록의 보이는 글자(편집기 기준).</summary>
        public static string Plain(IList<InlineRun> runs)
        {
            var sb = new StringBuilder();
            foreach (var r in runs)
                sb.Append(r.Text);
            return sb.ToString();
        }
    }
}

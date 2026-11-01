using Folderss.Services;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Folderss.Controls
{
    /// <summary>
    /// 블록 편집기 입력란 공통 모양(#76). 위치는 모두 "보이는 글자" 기준 — TextBox는 원문 그대로, <see cref="RichBlockBox"/>는 표식을 뺀 글자.
    /// TextBox는 같은 이름의 공개 멤버가 있어 그대로 맞는다(<see cref="BlockTextBox"/>).
    /// </summary>
    internal interface IBlockInput : IInputElement
    {
        string Text { get; }
        int CaretIndex { get; set; }
        int SelectionStart { get; set; }
        int SelectionLength { get; set; }
        string SelectedText { get; }
        void Select(int start, int length);
        Rect GetRectFromCharacterIndex(int charIndex);
        Rect GetRectFromCharacterIndex(int charIndex, bool trailingEdge);
        int GetCharacterIndexFromPoint(Point point, bool snapToText);
        int LineCount { get; }
        int GetLineIndexFromCharacterIndex(int charIndex);
        int GetCharacterIndexFromLineIndex(int lineIndex);
        string GetLineText(int lineIndex);
        bool IsVisible { get; }
        double ActualHeight { get; }
        object Tag { get; set; }
        void BringIntoView();
        void BringIntoView(Rect targetRectangle);
        Point TranslatePoint(Point point, UIElement relativeTo);
        bool IsInactiveSelectionHighlightEnabled { get; set; }
    }

    /// <summary>원문을 그대로 편집하는 입력란(코드·원문·수식·캡션·표 셀·메타).</summary>
    internal sealed class BlockTextBox : TextBox, IBlockInput
    {
        public BlockTextBox()
        {
            // 하위 형식은 TextBox 암시적 스타일을 받지 않으므로 같은 스타일을 참조한다.
            SetResourceReference(StyleProperty, typeof(TextBox));
        }
    }

    /// <summary>
    /// 서식이 보이는 입력란(#76) — 줄 안 Markdown을 표식 없이 굵게·기울임·취소선·코드·링크로 보여 주고 편집한다.
    /// 편집기 전체에 하나만 두고 편집하는 행으로 옮겨 붙인다(RichTextBox도 입력기 TSF에 등록되므로). 내용은 화면에서 뗀 상태에서 넣는다.
    /// 문서 모양은 언제나 문단 하나 + 조각마다 Run(링크는 Hyperlink) — 서식 적용·입력 단축 변환은 WPF 서식 명령 대신 조각 목록을 고쳐 다시 그린다.
    /// </summary>
    internal sealed class RichBlockBox : RichTextBox, IBlockInput
    {
        private string _plain;
        private string _markdown;
        private List<InlineRun> _runs;
        private int _composing;

        public RichBlockBox()
        {
            Document = new FlowDocument { PagePadding = new Thickness(0) };
            Document.Blocks.Add(new Paragraph { Margin = new Thickness(0) });
            IsUndoEnabled = false;
            AcceptsReturn = true;
            AcceptsTab = false;
            IsDocumentEnabled = false;
            BorderThickness = new Thickness(0);
            Background = Brushes.Transparent;
            Padding = new Thickness(0, 3, 0, 3);
            // RichTextBox는 글자 앞에 5px 안쪽 여백을 두어(TextBox는 2px) 표시용 글자(왼쪽 2px)와 3px 어긋난다 — 같은 자리·같은 줄 폭이 되게 맞춘다.
            Margin = new Thickness(-3, 0, -3, 0);
            VerticalContentAlignment = VerticalAlignment.Top;
            FocusVisualStyle = null;
            SetResourceReference(CaretBrushProperty, "PrimaryText");
            SetResourceReference(SelectionBrushProperty, "SelectionBrush");
            SetResourceReference(ForegroundProperty, "PrimaryText");

            // WPF 기본 서식 명령(Ctrl+B/I/U, 정렬, 글자 크기, 목록 등)은 끈다 — 서식은 편집기가 조각 목록으로 다룬다.
            foreach (var command in new ICommand[]
            {
                EditingCommands.ToggleBold, EditingCommands.ToggleItalic, EditingCommands.ToggleUnderline,
                EditingCommands.AlignCenter, EditingCommands.AlignJustify, EditingCommands.AlignLeft, EditingCommands.AlignRight,
                EditingCommands.IncreaseFontSize, EditingCommands.DecreaseFontSize, EditingCommands.ToggleBullets, EditingCommands.ToggleNumbering,
                EditingCommands.IncreaseIndentation, EditingCommands.DecreaseIndentation, EditingCommands.ToggleSubscript, EditingCommands.ToggleSuperscript,
                EditingCommands.EnterParagraphBreak,
            })
            {
                CommandBindings.Add(new CommandBinding(command, (s, e) => e.Handled = true, (s, e) =>
                {
                    e.CanExecute = false;
                    e.Handled = true;
                }));
            }

            AddHandler(TextCompositionManager.PreviewTextInputStartEvent, new TextCompositionEventHandler((s, e) => _composing++), true);
            AddHandler(TextCompositionManager.PreviewTextInputEvent, new TextCompositionEventHandler((s, e) => _composing = 0), true);
            LostKeyboardFocus += (s, e) => _composing = 0;
        }

        /// <summary>입력기 조합 중인지(조합을 깨지 않도록 이때는 내용을 다시 그리지 않음).</summary>
        public bool IsComposing => _composing > 0;

        /// <summary>마지막 내용 변경의 보이는 글자 위치·추가·삭제 길이(TextChanged 처리용).</summary>
        public int LastChangeOffset { get; private set; }
        public int LastAddedLength { get; private set; }
        public int LastRemovedLength { get; private set; }

        /// <summary>보이는 글자(LineBreak는 \n).</summary>
        public string Text => _plain ?? (_plain = BlockInlineRenderer.Plain(Runs));

        /// <summary>지금 내용의 조각 목록(읽기 전용으로 쓸 것 — 고치려면 사본을 만들어 <see cref="SetRuns"/>).</summary>
        public List<InlineRun> Runs => _runs ?? (_runs = BlockInlineRenderer.ReadRuns(Document));

        /// <summary>지금 내용의 줄 안 Markdown.</summary>
        public string Markdown => _markdown ?? (_markdown = InlineMarkdown.Serialize(Runs));

        /// <summary>
        /// 내용을 줄 안 Markdown으로 바꾼다(TextChanged는 한 번). <paramref name="marks"/>는 줄 안 범위 속성 값(밑줄·글자 색·형광펜, <see cref="TextMarks"/>) —
        /// 보이는 글자와 맞지 않으면 칠하지 않는다.
        /// </summary>
        public void SetMarkdown(string markdown, string marks = null)
        {
            markdown = markdown ?? string.Empty;
            var runs = InlineMarkdown.ParseForEditing(markdown);
            List<TextMark> parsed;
            if (marks != null && TextMarks.TryParse(marks, InlineMarkdown.ToPlain(runs), out parsed))
                runs = TextMarks.ApplyToRuns(runs, parsed);
            Rebuild(runs);
            _markdown = markdown;
        }

        /// <summary>조각 목록으로 다시 그리고 선택을 둔다.</summary>
        public void SetRuns(IList<InlineRun> runs, int selectionStart, int selectionLength)
        {
            Rebuild(runs);
            Select(selectionStart, selectionLength);
        }

        private void Rebuild(IList<InlineRun> runs)
        {
            BeginChange();
            try
            {
                Document.Blocks.Clear();
                var paragraph = new Paragraph { Margin = new Thickness(0) };
                BlockInlineRenderer.BuildEditorInlines(paragraph.Inlines, runs);
                Document.Blocks.Add(paragraph);
            }
            finally
            {
                _runs = null;
                _markdown = null;
                _plain = null;
                EndChange();
            }
        }

        protected override void OnTextChanged(TextChangedEventArgs e)
        {
            var oldPlain = _plain;
            _runs = null;
            _markdown = null;
            _plain = null;
            if (oldPlain != null)
            {
                var now = Text;
                var prefix = 0;
                var max = Math.Min(oldPlain.Length, now.Length);
                while (prefix < max && oldPlain[prefix] == now[prefix])
                    prefix++;
                var suffix = 0;
                while (suffix < max - prefix && oldPlain[oldPlain.Length - 1 - suffix] == now[now.Length - 1 - suffix])
                    suffix++;
                LastChangeOffset = prefix;
                LastAddedLength = now.Length - prefix - suffix;
                LastRemovedLength = oldPlain.Length - prefix - suffix;
            }
            else
            {
                LastChangeOffset = 0;
                LastAddedLength = Text.Length;
                LastRemovedLength = 0;
            }
            base.OnTextChanged(e);
        }

        // ── 보이는 글자 위치 ↔ TextPointer ────────────────────────────────

        /// <summary>보이는 글자 위치의 TextPointer. 서식 경계에서는 앞 조각 끝(앞 서식을 이어 입력).</summary>
        public TextPointer PointerAt(int offset)
        {
            var remaining = Math.Max(0, offset);
            TextPointer last = null;
            var first = true;
            foreach (var block in Document.Blocks)
            {
                if (!first)
                {
                    if (remaining == 0)
                        return last;
                    remaining--;
                }
                first = false;
                if (block is Paragraph paragraph)
                {
                    var found = FindPointer(paragraph.Inlines, ref remaining, ref last);
                    if (found != null)
                        return found;
                    if (last == null)
                        last = paragraph.ContentStart;
                }
                last = block.ContentEnd;
            }
            return last ?? Document.ContentStart;
        }

        private static TextPointer FindPointer(InlineCollection inlines, ref int remaining, ref TextPointer last)
        {
            foreach (var inline in inlines)
            {
                if (inline is Run run)
                {
                    var len = run.Text.Length;
                    if (remaining <= len)
                        return run.ContentStart.GetPositionAtOffset(remaining);
                    remaining -= len;
                    last = run.ContentEnd;
                }
                else if (inline is LineBreak lb)
                {
                    if (remaining == 0)
                        return last ?? lb.ElementStart;
                    remaining--;
                    last = lb.ElementEnd;
                }
                else if (inline is Span span)
                {
                    var found = FindPointer(span.Inlines, ref remaining, ref last);
                    if (found != null)
                        return found;
                }
            }
            return null;
        }

        /// <summary>TextPointer의 보이는 글자 위치.</summary>
        public int OffsetOf(TextPointer pointer)
        {
            if (pointer == null)
                return 0;
            var count = 0;
            var first = true;
            foreach (var block in Document.Blocks)
            {
                if (!first)
                    count++;
                first = false;
                if (pointer.CompareTo(block.ContentEnd) <= 0)
                {
                    if (block is Paragraph paragraph)
                    {
                        int within;
                        if (FindOffset(paragraph.Inlines, pointer, out within))
                            return count + within;
                        return count + within;
                    }
                    return count;
                }
                count += BlockLength(block);
            }
            return count;
        }

        private static bool FindOffset(InlineCollection inlines, TextPointer pointer, out int offset)
        {
            offset = 0;
            foreach (var inline in inlines)
            {
                if (inline is Run run)
                {
                    if (pointer.CompareTo(run.ContentStart) <= 0)
                        return true;
                    if (pointer.CompareTo(run.ContentEnd) <= 0)
                    {
                        offset += run.ContentStart.GetOffsetToPosition(pointer);
                        return true;
                    }
                    offset += run.Text.Length;
                }
                else if (inline is LineBreak lb)
                {
                    if (pointer.CompareTo(lb.ElementStart) <= 0)
                        return true;
                    offset++;
                }
                else if (inline is Span span)
                {
                    if (pointer.CompareTo(span.ElementStart) <= 0)
                        return true;
                    int inner;
                    if (FindOffset(span.Inlines, pointer, out inner))
                    {
                        offset += inner;
                        return true;
                    }
                    offset += inner;
                }
            }
            return false;
        }

        private static int BlockLength(Block block)
        {
            if (block is Paragraph paragraph)
            {
                var length = 0;
                CountLength(paragraph.Inlines, ref length);
                return length;
            }
            return new TextRange(block.ContentStart, block.ContentEnd).Text.Replace("\r\n", "\n").Length;
        }

        private static void CountLength(InlineCollection inlines, ref int length)
        {
            foreach (var inline in inlines)
            {
                if (inline is Run run) length += run.Text.Length;
                else if (inline is LineBreak) length++;
                else if (inline is Span span) CountLength(span.Inlines, ref length);
            }
        }

        // ── IBlockInput ──────────────────────────────────────────────────

        public int CaretIndex
        {
            get => OffsetOf(CaretPosition);
            set
            {
                var p = PointerAt(Math.Max(0, Math.Min(value, Text.Length)));
                Selection.Select(p, p);
                CaretPosition = p;
            }
        }

        public int SelectionStart
        {
            get => OffsetOf(Selection.Start);
            set => Select(value, 0);
        }

        public int SelectionLength
        {
            get => Selection.IsEmpty ? 0 : OffsetOf(Selection.End) - OffsetOf(Selection.Start);
            set => Select(SelectionStart, value);
        }

        public string SelectedText
        {
            get
            {
                var start = SelectionStart;
                var length = SelectionLength;
                var text = Text;
                start = Math.Max(0, Math.Min(start, text.Length));
                return text.Substring(start, Math.Max(0, Math.Min(length, text.Length - start)));
            }
        }

        public void Select(int start, int length)
        {
            var len = Text.Length;
            start = Math.Max(0, Math.Min(start, len));
            var end = Math.Max(start, Math.Min(start + Math.Max(0, length), len));
            var a = PointerAt(start);
            var b = end == start ? a : PointerAt(end);
            Selection.Select(a, b);
        }

        public Rect GetRectFromCharacterIndex(int charIndex)
        {
            return GetRectFromCharacterIndex(charIndex, false);
        }

        public Rect GetRectFromCharacterIndex(int charIndex, bool trailingEdge)
        {
            var len = Text.Length;
            charIndex = Math.Max(0, Math.Min(charIndex, len));
            if (trailingEdge)
            {
                var after = Math.Min(len, charIndex + 1);
                return PointerAt(after).GetCharacterRect(LogicalDirection.Backward);
            }
            var pointer = PointerAt(charIndex);
            // 글자 앞(앞 조각 끝이 아니라 다음 글자 쪽)
            var forward = pointer.GetInsertionPosition(LogicalDirection.Forward) ?? pointer;
            return forward.GetCharacterRect(LogicalDirection.Forward);
        }

        public int GetCharacterIndexFromPoint(Point point, bool snapToText)
        {
            var pointer = GetPositionFromPoint(point, snapToText);
            if (pointer == null)
                return -1;
            var offset = OffsetOf(pointer);
            // TextBox와 같게: 글자 칸 안을 누르면 그 글자(앞쪽) 위치
            return Math.Max(0, Math.Min(offset, Math.Max(0, Text.Length - 1)));
        }

        /// <summary>줄 시작 위치(보이는 글자) 목록. 레이아웃 전이면 첫 줄만.</summary>
        private List<int> LineStarts()
        {
            var starts = new List<int> { 0 };
            var pointer = Document.ContentStart.GetLineStartPosition(0);
            if (pointer == null)
                return starts;
            for (var guard = 0; guard < 10000; guard++)
            {
                int moved;
                pointer = pointer.GetLineStartPosition(1, out moved);
                if (pointer == null || moved != 1)
                    break;
                var offset = OffsetOf(pointer);
                if (offset <= starts[starts.Count - 1])
                    break;
                starts.Add(offset);
            }
            return starts;
        }

        public int LineCount => LineStarts().Count;

        public int GetLineIndexFromCharacterIndex(int charIndex)
        {
            // 커서 위치면 커서 쪽(줄 끝/다음 줄 처음)을 그대로 따른다
            TextPointer pointer = charIndex == CaretIndex ? CaretPosition : PointerAt(charIndex);
            int moved;
            var start = pointer.GetLineStartPosition(-100000, out moved);
            return start == null ? 0 : -moved;
        }

        public int GetCharacterIndexFromLineIndex(int lineIndex)
        {
            var starts = LineStarts();
            return lineIndex >= 0 && lineIndex < starts.Count ? starts[lineIndex] : -1;
        }

        public string GetLineText(int lineIndex)
        {
            var starts = LineStarts();
            if (lineIndex < 0 || lineIndex >= starts.Count)
                return null;
            var text = Text;
            var end = lineIndex + 1 < starts.Count ? starts[lineIndex + 1] : text.Length;
            return text.Substring(starts[lineIndex], Math.Max(0, end - starts[lineIndex]));
        }
    }
}

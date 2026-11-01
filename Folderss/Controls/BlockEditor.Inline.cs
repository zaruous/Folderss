using Folderss.Models;
using Folderss.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Folderss.Controls
{
    /// <summary>
    /// 줄 안 서식 WYSIWYG(#76): 서식 입력란(<see cref="RichBlockBox"/>) 하나를 편집하는 행으로 옮겨 붙이고, 서식 적용(Ctrl+B/I/E/Shift+S),
    /// 링크 주소 팝업(Ctrl+K), 글자 선택 시 떠 있는 서식 도구막대, 입력 중 Markdown 서식 단축(<c>**글**</c> 등), 서식 붙여넣기(HTML·RTF)를 맡는다.
    /// 서식은 WPF 서식 명령 대신 조각 목록(<see cref="InlineRun"/>)을 고쳐 다시 그리므로 Markdown 왕복이 늘 같은 규칙(<see cref="InlineMarkdown"/>)을 탄다.
    /// </summary>
    public sealed partial class BlockEditor
    {
        /// <summary>편집기 안 복사·붙여넣기용 클립보드 형식(선택 글자의 줄 안 Markdown).</summary>
        private const string InlineMarkdownFormat = "Folderss.InlineMarkdown";

        /// <summary>
        /// 편집기 안 복사·붙여넣기용 줄 안 범위(밑줄·색, #76 9번) 클립보드 형식. 첫 줄은 함께 담은 Markdown 글자의 해시(<see cref="TextMarks.Hash"/> — 다른 프로그램이
        /// 글자만 바꿔 넣었으면 쓰지 않음), 그 뒤로 Markdown 블록마다 한 줄씩 <c>marks</c> 값(없으면 빈 줄). 바깥 프로그램에는 Markdown 글자만 간다.
        /// </summary>
        private const string TextMarksFormat = "Folderss.TextMarks";

        private RichBlockBox _rich;
        private BlockRow _richRow;
        private bool _applyingInlineEdit;
        private bool _forwardingMouse;   // 표시 글자 누르기를 서식 입력란에 넘기는 중(행·패널 처리기는 무시)

        private Popup _formatBar;
        private readonly Dictionary<InlineFormat, Border> _formatButtons = new Dictionary<InlineFormat, Border>();
        private Border _linkButton;

        private Popup _linkPopup;
        private TextBox _linkUrlBox;
        private BlockRow _linkRow;
        private int _linkStart;
        private int _linkEnd;
        private bool _linkInsert;

        // ── 서식 입력란 옮겨 붙이기 ─────────────────────────────────────────

        /// <summary>서식 입력란을 <paramref name="row"/>에 붙인다(다른 행에 붙어 있으면 떼어 옴).</summary>
        private RichBlockBox AttachRich(BlockRow row)
        {
            var box = EnsureRich();
            if (ReferenceEquals(_richRow, row) && ReferenceEquals(row.Rich, box))
                return box;
            HideFormatBar();
            if (_richRow != null)
            {
                var old = _richRow;
                _richRow = null;
                old.DetachRich();
            }
            else if (box.Parent is Panel parent)
            {
                parent.Children.Remove(box);
            }
            _richRow = row;
            row.AttachRich(box);
            return box;
        }

        /// <summary>서식 입력란을 붙어 있는 행에서 뗀다(행을 거두거나 다른 블록에 다시 묶을 때·종류가 원문 블록으로 바뀔 때).</summary>
        private void ReleaseRich()
        {
            var row = _richRow;
            if (row == null)
                return;
            HideFormatBar();
            _richRow = null;
            row.DetachRich();
        }

        private RichBlockBox EnsureRich()
        {
            if (_rich != null)
                return _rich;
            var box = new RichBlockBox();
            box.TextChanged += (s, e) =>
            {
                if (box.Tag is BlockRow row && ReferenceEquals(row, _richRow))
                    OnEditorTextChanged(row, e);
                UpdateFormatBarSoon();
            };
            box.GotKeyboardFocus += (s, e) =>
            {
                if (box.Tag is BlockRow row)
                    OnEditorFocused(row);
            };
            box.LostKeyboardFocus += (s, e) =>
            {
                if (box.Tag is BlockRow row)
                    OnEditorBlurred(row);
                UpdateFormatBarSoon();
            };
            box.SelectionChanged += (s, e) =>
            {
                if (box.Tag is BlockRow row)
                    OnEditorSelectionChanged(row);
                UpdateFormatBarSoon();
            };
            box.PreviewMouseLeftButtonUp += (s, e) => UpdateFormatBarSoon();
            DataObject.AddPastingHandler(box, (s, e) =>
            {
                if (box.Tag is BlockRow row)
                    OnRichPasting(row, box, e);
            });
            DataObject.AddCopyingHandler(box, OnRichCopying);
            CommandManager.AddPreviewCanExecuteHandler(box, (s, e) =>
            {
                if (e.Command == ApplicationCommands.Paste && box.Tag is BlockRow row && CanPasteMedia(row))
                {
                    e.CanExecute = true;
                    e.Handled = true;
                }
            });
            CommandManager.AddPreviewExecutedHandler(box, (s, e) =>
            {
                if (e.Command == ApplicationCommands.Paste && box.Tag is BlockRow row && CanPasteMedia(row))
                {
                    e.Handled = true;
                    PasteMedia(IndexOf(row));
                }
            });
            _rich = box;
            return box;
        }

        /// <summary>표시 글자를 누른 마우스를 방금 붙인 서식 입력란에 넘긴다 — 끌어 선택이 바로 이어지게.</summary>
        private void ForwardMouseDown(RichBlockBox box, MouseButtonEventArgs e)
        {
            if (box == null || !box.IsVisible || e.ButtonState != MouseButtonState.Pressed)
                return;
            box.UpdateLayout();
            // 입력란(TextEditor)은 Mouse.MouseDown 클래스 처리기로 누르기를 받아 커서를 두고 마우스를 잡는다.
            var args = new MouseButtonEventArgs(e.MouseDevice, e.Timestamp, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent, Source = box };
            _forwardingMouse = true;
            try
            {
                box.RaiseEvent(args);
            }
            finally
            {
                _forwardingMouse = false;
            }
            // 입력란이 받지 않았으면(레이아웃 차이 등) 커서는 FocusRow가 둔 자리 그대로.
        }

        // ── 블록 본문 ↔ 입력란 글자 ─────────────────────────────────────────

        /// <summary>블록 본문의 보이는 글자 수(서식 블록은 표식을 뺀 글자, 그 밖은 원문).</summary>
        private static int PlainLength(EditorBlock block)
        {
            return EditorBlock.HasInlineFormatting(block.Kind) && block.Kind != BlockKind.TableRow
                ? InlineMarkdown.ToPlain(block.Text).Length
                : block.Text.Length;
        }

        /// <summary>두 블록 본문을 잇는다(서식 블록이면 같은 서식이 붙도록 <see cref="InlineMarkdown.Concat"/>).</summary>
        private static string JoinText(EditorBlock target, string tail)
        {
            return EditorBlock.HasInlineFormatting(target.Kind) && target.Kind != BlockKind.TableRow
                ? InlineMarkdown.Concat(target.Text, tail)
                : target.Text + tail;
        }

        /// <summary>
        /// 입력란의 선택을 지운 본문과 커서 앞·뒤 본문(Markdown). <paramref name="selectionStart"/>는 보이는 글자 기준,
        /// <paramref name="isEmpty"/>는 선택을 지운 뒤 글자가 하나도 없는지.
        /// </summary>
        private static void ReadSplit(BlockRow row, IBlockInput editor, out string whole, out string head, out string tail, out int selectionStart, out bool isEmpty)
        {
            selectionStart = editor.SelectionStart;
            if (editor is RichBlockBox rich)
            {
                var runs = rich.Runs;
                var length = InlineMarkdown.ToPlain(runs).Length;
                var end = Math.Min(length, selectionStart + rich.SelectionLength);
                var remaining = InlineMarkdown.Replace(runs, selectionStart, end, new InlineRun[0]);
                var remainingLength = InlineMarkdown.ToPlain(remaining).Length;
                whole = InlineMarkdown.Serialize(remaining);
                head = InlineMarkdown.Serialize(InlineMarkdown.Slice(remaining, 0, selectionStart));
                tail = InlineMarkdown.Serialize(InlineMarkdown.Slice(remaining, selectionStart, remainingLength));
                isEmpty = remainingLength == 0;
                return;
            }
            whole = editor.Text.Remove(selectionStart, editor.SelectionLength);
            head = whole.Substring(0, selectionStart);
            tail = whole.Substring(selectionStart);
            isEmpty = whole.Length == 0;
        }

        /// <summary>입력란 글자 [start, end)를 지운 본문(Markdown).</summary>
        private static string TextWithout(IBlockInput editor, int start, int end)
        {
            if (editor is RichBlockBox rich)
                return InlineMarkdown.Serialize(InlineMarkdown.Replace(rich.Runs, start, end, new InlineRun[0]));
            return editor.Text.Remove(start, Math.Max(0, end - start));
        }

        /// <summary>블록 본문의 보이는 글자 위치에 글자를 넣은 본문.</summary>
        private static string InsertPlain(EditorBlock block, int at, string value)
        {
            if (!EditorBlock.HasInlineFormatting(block.Kind) || block.Kind == BlockKind.TableRow)
                return block.Text.Insert(Math.Min(at, block.Text.Length), value);
            var runs = InlineMarkdown.ParseForEditing(block.Text);
            var length = InlineMarkdown.ToPlain(runs).Length;
            at = Math.Max(0, Math.Min(at, length));
            return InlineMarkdown.Serialize(InlineMarkdown.Replace(runs, at, at, new[] { new InlineRun { Text = value } }));
        }

        /// <summary>찾기: 입력란 글자 위치 → 블록 본문(원문) 위치.</summary>
        private static int EditorToSource(BlockRow row, int offset)
        {
            return row.UsesRich ? InlineMarkdown.PlainToSource(row.Block.Text, offset) : offset;
        }

        /// <summary>찾기: 블록 본문(원문) 위치 → 입력란 글자 위치.</summary>
        private static int SourceToEditor(BlockRow row, int offset)
        {
            return row.UsesRich ? InlineMarkdown.SourceToPlain(row.Block.Text, offset) : offset;
        }

        // ── 줄 안 범위(밑줄·글자 색·형광펜, #76 9번) ─────────────────────────

        /// <summary>보이는 글자와 그 기준 범위(나누기·합치기·붙여넣기에서 블록 사이로 옮길 때).</summary>
        private sealed class MarkedPlain
        {
            public List<TextMark> Marks = new List<TextMark>();
            public string Plain;
        }

        /// <summary>서식 입력란 내용(Markdown + 조각의 범위)을 블록에 넣는다(범위는 본문 변경으로 옮기지 않고 입력란 그대로).</summary>
        private static void SetBlockFromRich(EditorBlock block, RichBlockBox rich)
        {
            var runs = rich.Runs;
            List<TextMark> marks = null;
            foreach (var r in runs)
            {
                if (r.HasMarks)
                {
                    marks = TextMarks.FromRuns(runs);
                    break;
                }
            }
            TextMarks.SetText(block, rich.Markdown, marks, rich.Text);
        }

        /// <summary>입력란 범위가 블록 범위와 다른지(되돌리기·외부 변경 뒤 입력란을 다시 채울지).</summary>
        private static bool RichMarksDiffer(RichBlockBox rich, EditorBlock block)
        {
            var runs = rich.Runs;
            var hasRunMarks = runs.Any(r => r.HasMarks);
            if (!hasRunMarks && block.GetAttribute(TextMarks.Attribute) == null)
                return false;
            var plain = rich.Text;
            return !TextMarks.SameMarks(TextMarks.FromRuns(runs), TextMarks.Get(block, plain), plain.Length);
        }

        /// <summary>블록 본문에서 선택 [start, start+length)를 지운 뒤 커서 뒤 글자의 범위(나누기·여러 줄 붙여넣기에서 뒤 블록으로 가는 것).</summary>
        private static MarkedPlain TailMarks(EditorBlock block, int start, int length)
        {
            var result = new MarkedPlain();
            if (!TextMarks.IsTarget(block.Kind) || block.GetAttribute(TextMarks.Attribute) == null)
                return result;
            var plain = TextMarks.PlainOf(block);
            var marks = TextMarks.Get(block, plain);
            start = Math.Max(0, Math.Min(start, plain.Length));
            var end = Math.Max(start, Math.Min(start + Math.Max(0, length), plain.Length));
            var rest = plain.Remove(start, end - start);
            var remaining = TextMarks.Edit(marks, plain.Length, start, end - start, 0);
            result.Marks = TextMarks.Slice(remaining, start, rest.Length);
            result.Plain = rest.Substring(start);
            return result;
        }

        /// <summary>
        /// 블록 끝에 본문을 잇고(<see cref="JoinText"/>) 범위도 잇는다 — 뒤 글자는 앞 글자 범위를 따르지 않고 제 범위(<paramref name="tailMarks"/>, 기준 글자 <paramref name="tailPlain"/>)를 가진다.
        /// </summary>
        private static void JoinBlock(EditorBlock target, string tailText, IList<TextMark> tailMarks, string tailPlain)
        {
            var hasTail = tailMarks != null && tailMarks.Count > 0;
            if (!TextMarks.IsTarget(target.Kind) || (!hasTail && target.GetAttribute(TextMarks.Attribute) == null))
            {
                target.Text = JoinText(target, tailText);
                return;
            }
            var headPlain = TextMarks.PlainOf(target);
            var headMarks = TextMarks.Get(target, headPlain);
            target.Text = JoinText(target, tailText);
            tailPlain = tailPlain ?? InlineMarkdown.ToPlain(tailText);
            TextMarks.Set(target, TextMarks.Concat(headMarks, headPlain.Length, tailMarks, tailPlain.Length), headPlain + tailPlain);
        }

        /// <summary>붙여넣은 블록(방금 Markdown에서 읽음)에 블록별 범위 값을 붙인다. 블록 수가 다르면 붙이지 않고, 본문과 맞지 않는 값은 버린다.</summary>
        private static void ApplyPastedMarks(IList<EditorBlock> parsed, IList<string> blockMarks)
        {
            if (blockMarks == null || blockMarks.All(string.IsNullOrEmpty))
                return;
            var written = parsed.Where(BlockMarkdown.IsWritten).ToList();
            if (written.Count != blockMarks.Count)
                return;
            for (var i = 0; i < written.Count; i++)
            {
                if (string.IsNullOrEmpty(blockMarks[i]))
                    continue;
                written[i].SetAttribute(TextMarks.Attribute, blockMarks[i]);
                TextMarks.DropIfInvalid(written[i]);
            }
        }

        /// <summary>복사할 블록들의 범위를 클립보드 자료에 더한다(범위가 하나도 없으면 더하지 않음).</summary>
        private static void SetClipboardMarks(IDataObject data, string markdown, IList<EditorBlock> blocks)
        {
            var values = blocks.Where(BlockMarkdown.IsWritten).Select(b => b.GetAttribute(TextMarks.Attribute) != null ? TextMarks.Format(TextMarks.Get(b), TextMarks.PlainOf(b)) : null).ToList();
            SetClipboardMarks(data, markdown, values);
        }

        private static void SetClipboardMarks(IDataObject data, string markdown, IList<string> values)
        {
            if (values.All(string.IsNullOrEmpty))
                return;
            try
            {
                data.SetData(TextMarksFormat, TextMarks.Hash(markdown) + "\n" + string.Join("\n", values.Select(v => v ?? string.Empty)));
            }
            catch (InvalidOperationException)
            {
            }
        }

        /// <summary>클립보드의 블록별 범위 값(편집기에서 복사한 것이고 글자가 <paramref name="markdown"/>과 같을 때만). 없으면 null.</summary>
        private static List<string> ClipboardBlockMarks(IDataObject data, string markdown)
        {
            try
            {
                if (data == null || markdown == null || !data.GetDataPresent(TextMarksFormat))
                    return null;
                var value = data.GetData(TextMarksFormat) as string;
                if (string.IsNullOrEmpty(value))
                    return null;
                var lines = value.Replace("\r\n", "\n").Split('\n');
                if (lines[0] != TextMarks.Hash(markdown) && lines[0] != TextMarks.Hash(markdown.Replace("\r\n", "\n")))
                    return null;
                return lines.Skip(1).Select(l => l.Length == 0 ? null : l).ToList();
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                return null;
            }
        }

        private static List<string> ClipboardBlockMarks(string markdown)
        {
            try
            {
                return ClipboardBlockMarks(Clipboard.GetDataObject(), markdown);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                return null;
            }
        }

        /// <summary>
        /// 밑줄(<c>Ctrl+U</c>·도구막대 U): 선택이 모두 밑줄이면 끄고 아니면 켠다. 선택이 없으면 다음에 입력할 글자의 밑줄을 바꾼다. 실행 취소 한 단계.
        /// </summary>
        private bool ToggleUnderline(BlockRow row)
        {
            var box = row?.Rich;
            if (box == null)
                return false;
            var start = box.SelectionStart;
            var length = box.SelectionLength;
            var runs = box.Runs;
            var plainLength = InlineMarkdown.ToPlain(runs).Length;
            var marks = TextMarks.FromRuns(runs);
            if (length == 0)
            {
                var before = InlineMarkdown.RunAt(runs, start, true);
                var on = !(before != null && before.Underline);
                ApplySpringDecoration(box, TextDecorationLocation.Underline, on);
                UpdateFormatBarSoon();
                return true;
            }
            var turnOn = !TextMarks.Covers(marks, plainLength, TextMarkKind.Underline, start, start + length);
            ApplyMarks(row, box, TextMarks.Apply(marks, plainLength, TextMarkKind.Underline, turnOn ? string.Empty : null, start, start + length), start, length);
            return true;
        }

        /// <summary>선택 글자의 글자 색(<paramref name="highlight"/>면 배경색)을 바꾼다(null이면 지움). 선택이 없으면 하지 않는다. 실행 취소 한 단계.</summary>
        private bool SetTextMarkColor(BlockRow row, bool highlight, string name)
        {
            var box = row?.Rich;
            if (box == null || box.SelectionLength == 0)
                return false;
            var start = box.SelectionStart;
            var length = box.SelectionLength;
            var runs = box.Runs;
            var plainLength = InlineMarkdown.ToPlain(runs).Length;
            var marks = TextMarks.Apply(TextMarks.FromRuns(runs), plainLength, highlight ? TextMarkKind.Highlight : TextMarkKind.Color, name, start, start + length);
            ApplyMarks(row, box, marks, start, length);
            return true;
        }

        /// <summary>선택 글자의 글자 색·배경색을 모두 지운다(밑줄은 그대로).</summary>
        private bool ClearTextMarkColors(BlockRow row)
        {
            var box = row?.Rich;
            if (box == null || box.SelectionLength == 0)
                return false;
            var start = box.SelectionStart;
            var length = box.SelectionLength;
            var runs = box.Runs;
            var plainLength = InlineMarkdown.ToPlain(runs).Length;
            var marks = TextMarks.Apply(TextMarks.FromRuns(runs), plainLength, TextMarkKind.Color, null, start, start + length);
            marks = TextMarks.Apply(marks, plainLength, TextMarkKind.Highlight, null, start, start + length);
            ApplyMarks(row, box, marks, start, length);
            return true;
        }

        private void ApplyMarks(BlockRow row, RichBlockBox box, IList<TextMark> marks, int selectionStart, int selectionLength)
        {
            ApplyRuns(row, box, TextMarks.ApplyToRuns(TextMarks.Strip(box.Runs), marks), selectionStart, selectionLength);
        }

        /// <summary>선택 없이 밑줄·취소선 키: 다음 입력 글자의 글자 장식(다른 장식은 그대로 둠).</summary>
        private static void ApplySpringDecoration(RichBlockBox box, TextDecorationLocation location, bool on)
        {
            var selection = box.Selection;
            var current = selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
            var result = new TextDecorationCollection();
            if (current != null)
            {
                foreach (var d in current)
                    if (d.Location != location)
                        result.Add(d);
            }
            if (on)
                result.Add(location == TextDecorationLocation.Underline ? TextDecorations.Underline[0] : TextDecorations.Strikethrough[0]);
            result.Freeze();
            selection.ApplyPropertyValue(Inline.TextDecorationsProperty, result);
        }

        // ── 서식 적용 ─────────────────────────────────────────────────────

        /// <summary>
        /// Ctrl+B/I/E/Shift+S·도구막대: 선택 글자의 서식을 켜거나(하나라도 없으면) 끈다(모두 있으면). 선택이 없으면 다음에 입력할 글자의 서식을 바꾼다.
        /// 실행 취소 한 단계.
        /// </summary>
        private bool ToggleInlineFormat(BlockRow row, InlineFormat format)
        {
            var box = row?.Rich;
            if (box == null)
                return false;
            var start = box.SelectionStart;
            var length = box.SelectionLength;
            var runs = box.Runs;
            if (length == 0)
            {
                ApplySpringFormat(box, format, !InlineMarkdown.HasFormat(runs, start, start, format));
                UpdateFormatBarSoon();
                return true;
            }
            var on = !InlineMarkdown.HasFormat(runs, start, start + length, format);
            ApplyRuns(row, box, InlineMarkdown.ApplyFormat(runs, start, start + length, format, on), start, length);
            return true;
        }

        /// <summary>선택 없이 서식 키를 누름: 다음에 입력할 글자의 서식(WPF 선택 서식 — 입력하면 그 서식의 새 Run이 생김).</summary>
        private static void ApplySpringFormat(RichBlockBox box, InlineFormat format, bool on)
        {
            var selection = box.Selection;
            switch (format)
            {
                case InlineFormat.Bold:
                    selection.ApplyPropertyValue(TextElement.FontWeightProperty, on ? FontWeights.Bold : FontWeights.Normal);
                    break;
                case InlineFormat.Italic:
                    selection.ApplyPropertyValue(TextElement.FontStyleProperty, on ? FontStyles.Italic : FontStyles.Normal);
                    break;
                case InlineFormat.Strike:
                    ApplySpringDecoration(box, TextDecorationLocation.Strikethrough, on);
                    break;
                case InlineFormat.Code:
                    if (on)
                    {
                        selection.ApplyPropertyValue(TextElement.FontFamilyProperty, BlockInlineRenderer.CodeFont);
                        selection.ApplyPropertyValue(TextElement.BackgroundProperty, Application.Current?.TryFindResource("SurfaceBackground") as Brush);
                    }
                    else
                    {
                        selection.ApplyPropertyValue(TextElement.FontFamilyProperty, box.FontFamily);
                        selection.ApplyPropertyValue(TextElement.BackgroundProperty, null);
                    }
                    break;
            }
        }

        /// <summary>조각 목록으로 다시 그리고 실행 취소 한 단계로 기록한다(입력 묶음과 섞이지 않게 앞뒤로 끊음).</summary>
        private void ApplyRuns(BlockRow row, RichBlockBox box, IList<InlineRun> runs, int selectionStart, int selectionLength)
        {
            _typingRow = null;
            _applyingInlineEdit = true;
            try
            {
                box.SetRuns(runs, selectionStart, selectionLength);
            }
            finally
            {
                _applyingInlineEdit = false;
            }
            // SetRuns의 TextChanged가 본문(과 줄 안 범위)을 갱신하고 기록했다. 다음 입력은 새 단계로.
            if (row.Block.Text != box.Markdown || RichMarksDiffer(box, row.Block))
            {
                SetBlockFromRich(row.Block, box);
                RecordChange();
            }
            _typingRow = null;
            UpdateFormatBarSoon();
        }

        // ── 입력 중 서식 단축 ──────────────────────────────────────────────

        /// <summary>
        /// 닫는 표식을 막 입력했으면(<c>**글**</c>·<c>*글*</c>·<c>~~글~~</c>·<c>`코드`</c>·<c>$수식$</c>·<c>[글](주소)</c>) 이벤트가 끝난 뒤 서식으로 바꾼다.
        /// 입력한 글자는 이미 기록됐고 변환은 따로 한 단계라 Ctrl+Z 한 번이면 입력한 표식 글자로 돌아간다. 입력기 조합 중이면 하지 않는다.
        /// </summary>
        private void TryInlineInputRule(BlockRow row, RichBlockBox box, int caret)
        {
            if (_applyingInlineEdit || box.IsComposing)
                return;
            var text = box.Text;
            if (caret < 2 || caret > text.Length || "*_~`$)".IndexOf(text[caret - 1]) < 0)
                return;   // 닫는 표식이 아니면(대부분의 입력) 조각을 훑지 않는다
            List<InlineRun> result;
            int newCaret;
            if (!InlineMarkdown.TryInputRule(box.Runs, caret, out result, out newCaret))
                return;
            Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
            {
                if (!ReferenceEquals(row.Rich, box) || box.IsComposing)
                    return;
                ApplyRuns(row, box, result, newCaret, 0);
                // 변환한 서식은 닫혔으므로 이어 쓰는 글자는 서식 없이(Notion과 같음)
                ClearSpringFormat(box);
            }));
        }

        private static void ClearSpringFormat(RichBlockBox box)
        {
            var selection = box.Selection;
            if (!selection.IsEmpty)
                return;
            selection.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
            selection.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Normal);
            selection.ApplyPropertyValue(Inline.TextDecorationsProperty, new TextDecorationCollection());
            selection.ApplyPropertyValue(TextElement.FontFamilyProperty, box.FontFamily);
            selection.ApplyPropertyValue(TextElement.BackgroundProperty, null);
        }

        // ── 붙여넣기·복사 ─────────────────────────────────────────────────

        /// <summary>
        /// 서식 입력란 붙여넣기: 편집기에서 복사한 줄 안 Markdown → HTML(브라우저·Word 등) → RTF → 글자(Markdown으로 읽음) 순으로 고른다.
        /// 여러 줄이면 블록으로(<see cref="PasteBlocks"/>), 한 줄이면 선택 자리에 서식 조각으로 넣는다. WPF 기본 서식 붙여넣기(글꼴·크기·표 등)는 쓰지 않는다.
        /// </summary>
        private void OnRichPasting(BlockRow row, RichBlockBox box, DataObjectPastingEventArgs e)
        {
            List<string> blockMarks;
            var markdown = ClipboardToMarkdown(e.DataObject, out blockMarks);
            e.CancelCommand();
            if (string.IsNullOrEmpty(markdown))
                return;
            markdown = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
            if (markdown.IndexOf('\n') >= 0)
            {
                PasteBlocks(row, markdown, blockMarks);
                return;
            }
            var start = box.SelectionStart;
            var end = start + box.SelectionLength;
            var inserted = InlineMarkdown.ParseForEditing(markdown);
            // 붙여넣는 글자는 제 범위(편집기에서 복사한 밑줄·색, HTML의 u·color·mark)만 가진다 — 앞 글자 범위를 따르지 않음.
            List<TextMark> insertedMarks;
            if (blockMarks != null && blockMarks.Count == 1 && TextMarks.TryParse(blockMarks[0], InlineMarkdown.ToPlain(inserted), out insertedMarks))
                inserted = TextMarks.ApplyToRuns(inserted, insertedMarks);
            var runs = InlineMarkdown.Replace(box.Runs, start, end, inserted);
            ApplyRuns(row, box, runs, start + InlineMarkdown.ToPlain(inserted).Length, 0);
        }

        /// <summary>클립보드 내용을 Markdown으로(서식 우선). 없으면 null.</summary>
        internal static string ClipboardToMarkdown(IDataObject data)
        {
            List<string> ignored;
            return ClipboardToMarkdown(data, out ignored);
        }

        /// <summary>
        /// 클립보드 내용을 Markdown으로(서식 우선). 없으면 null. <paramref name="blockMarks"/>는 Markdown 블록마다 줄 안 범위 값(편집기에서 복사한 범위,
        /// HTML의 밑줄·색·형광펜) — 없으면 null.
        /// </summary>
        internal static string ClipboardToMarkdown(IDataObject data, out List<string> blockMarks)
        {
            blockMarks = null;
            try
            {
                if (data.GetDataPresent(InlineMarkdownFormat))
                {
                    var inline = data.GetData(InlineMarkdownFormat) as string;
                    blockMarks = ClipboardBlockMarks(data, inline);
                    return inline;
                }
                if (data.GetDataPresent(DataFormats.Html))
                {
                    var html = data.GetData(DataFormats.Html) as string;
                    if (!string.IsNullOrWhiteSpace(html))
                    {
                        var lineMarks = new List<string>();
                        var converted = ClipboardMarkdown.FromHtml(html, lineMarks);
                        if (converted.Length > 0)
                        {
                            blockMarks = lineMarks.Any(v => v != null) ? lineMarks : null;
                            return converted;
                        }
                    }
                }
                if (data.GetDataPresent(DataFormats.Rtf))
                {
                    var rtf = data.GetData(DataFormats.Rtf) as string;
                    var converted = RtfToMarkdown(rtf);
                    if (!string.IsNullOrEmpty(converted))
                        return converted;
                }
                if (data.GetDataPresent(DataFormats.UnicodeText))
                {
                    var text = data.GetData(DataFormats.UnicodeText) as string;
                    blockMarks = ClipboardBlockMarks(data, text);
                    return text;
                }
                if (data.GetDataPresent(DataFormats.Text))
                    return data.GetData(DataFormats.Text) as string;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
            }
            return null;
        }

        /// <summary>RTF → Markdown: WPF로 FlowDocument에 읽은 뒤 문단마다 서식(굵게·기울임·취소선·고정폭 글꼴 = 코드·링크)을 조각으로 읽는다.</summary>
        internal static string RtfToMarkdown(string rtf)
        {
            if (string.IsNullOrWhiteSpace(rtf))
                return null;
            try
            {
                var document = new FlowDocument();
                var range = new TextRange(document.ContentStart, document.ContentEnd);
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(rtf)))
                    range.Load(stream, DataFormats.Rtf);
                var lines = new List<string>();
                foreach (var block in document.Blocks)
                {
                    if (block is Paragraph paragraph)
                    {
                        var runs = new List<InlineRun>();
                        CollectRtfRuns(paragraph.Inlines, runs);
                        var md = InlineMarkdown.Serialize(runs).Trim();
                        if (md.Length > 0)
                            lines.Add(md);
                    }
                    else
                    {
                        var text = new TextRange(block.ContentStart, block.ContentEnd).Text.Trim();
                        if (text.Length > 0)
                            lines.Add(text);
                    }
                }
                return string.Join("\n\n", lines);
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private static void CollectRtfRuns(InlineCollection inlines, List<InlineRun> runs)
        {
            foreach (var inline in inlines)
            {
                if (inline is Run run)
                {
                    if (run.Text.Length == 0)
                        continue;
                    var r = new InlineRun { Text = run.Text };
                    if (run.FontWeight.ToOpenTypeWeight() >= FontWeights.SemiBold.ToOpenTypeWeight())
                        r.Format |= InlineFormat.Bold;
                    if (run.FontStyle == FontStyles.Italic || run.FontStyle == FontStyles.Oblique)
                        r.Format |= InlineFormat.Italic;
                    var decorations = run.TextDecorations;
                    if (decorations != null && decorations.Any(d => d.Location == TextDecorationLocation.Strikethrough))
                        r.Format |= InlineFormat.Strike;
                    var family = run.FontFamily?.Source ?? string.Empty;
                    if (family.IndexOf("Mono", StringComparison.OrdinalIgnoreCase) >= 0 || family.IndexOf("Consolas", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        family.IndexOf("Courier", StringComparison.OrdinalIgnoreCase) >= 0)
                        r.Format |= InlineFormat.Code;
                    var link = ElementTree.FindAncestor<Hyperlink>(run);
                    if (link?.NavigateUri != null)
                        r.Link = link.NavigateUri.OriginalString;
                    runs.Add(r);
                }
                else if (inline is LineBreak)
                {
                    runs.Add(new InlineRun { Text = "\n" });
                }
                else if (inline is Span span)
                {
                    CollectRtfRuns(span.Inlines, runs);
                }
            }
        }

        /// <summary>서식 입력란 복사·잘라내기: 글자 자리에 선택 글자의 줄 안 Markdown을 넣는다(편집기 안에서 붙이면 서식 그대로, 밖에서는 Markdown 글자).</summary>
        private void OnRichCopying(object sender, DataObjectCopyingEventArgs e)
        {
            var box = sender as RichBlockBox;
            if (box == null || e.IsDragDrop)
                return;
            var start = box.SelectionStart;
            var slice = InlineMarkdown.Slice(box.Runs, start, start + box.SelectionLength);
            var markdown = InlineMarkdown.Serialize(slice);
            try
            {
                e.DataObject.SetData(DataFormats.UnicodeText, markdown);
                e.DataObject.SetData(DataFormats.Text, markdown);
                e.DataObject.SetData(InlineMarkdownFormat, markdown);
            }
            catch (InvalidOperationException)
            {
            }
            // 밑줄·색은 편집기 안에서만(바깥에는 Markdown 글자 그대로). 범위 값은 붙일 때 다시 읽는 보이는 글자 기준.
            var marks = TextMarks.FromRuns(slice);
            if (marks.Count > 0)
            {
                var basis = InlineMarkdown.ToPlain(slice);
                var plain = InlineMarkdown.ToPlain(markdown);
                SetClipboardMarks(e.DataObject, markdown, new[] { TextMarks.Format(basis == plain ? marks : TextMarks.Adjust(marks, basis, plain), plain) });
            }
        }

        // ── 링크 주소 팝업(Ctrl+K) ────────────────────────────────────────

        /// <summary>
        /// Ctrl+K·도구막대 링크: 커서가 링크 안이면 그 링크, 아니면 선택 글자의 주소를 고치는 팝업. 선택이 없으면 주소를 그대로 링크 글자로 넣는다.
        /// </summary>
        private bool ShowLinkEditor(BlockRow row)
        {
            var box = row?.Rich;
            if (box == null)
                return false;
            var start = box.SelectionStart;
            var end = start + box.SelectionLength;
            var runs = box.Runs;
            int linkStart, linkEnd;
            InlineRun link;
            string url = null;
            if (InlineMarkdown.LinkRange(runs, start, out linkStart, out linkEnd, out link) && (end == start || end <= linkEnd))
            {
                start = linkStart;
                end = linkEnd;
                url = link.Url;
            }
            _linkRow = row;
            _linkStart = start;
            _linkEnd = end;
            _linkInsert = start == end;
            HideFormatBar();
            EnsureLinkPopup();
            if (url == null)
            {
                url = string.Empty;
                try
                {
                    var clip = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : string.Empty;
                    if (Uri.IsWellFormedUriString(clip, UriKind.Absolute) && clip.IndexOf('\n') < 0)
                        url = clip;
                }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                }
            }
            _linkUrlBox.Text = url;
            Rect rect;
            try
            {
                rect = box.GetRectFromCharacterIndex(start);
            }
            catch (InvalidOperationException)
            {
                rect = Rect.Empty;
            }
            _linkPopup.PlacementTarget = box;
            _linkPopup.PlacementRectangle = rect.IsEmpty ? new Rect(0, 0, 0, box.ActualHeight) : rect;
            _linkPopup.IsOpen = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                _linkUrlBox.Focus();
                _linkUrlBox.SelectAll();
            }));
            return true;
        }

        private void EnsureLinkPopup()
        {
            if (_linkPopup != null)
                return;
            _linkUrlBox = new TextBox { Width = 300, Margin = new Thickness(0, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "링크 주소 (Enter 적용 · Esc 취소)" };
            _linkUrlBox.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    ApplyLinkEditor(_linkUrlBox.Text.Trim());
                }
                else if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    CloseLinkEditor(true);
                }
            };
            var apply = PopupButton("적용", "Enter", () => ApplyLinkEditor(_linkUrlBox.Text.Trim()));
            var open = PopupButton("열기", "주소 열기", () =>
            {
                var url = _linkUrlBox.Text.Trim();
                CloseLinkEditor(true);
                if (url.Length > 0)
                    LinkOpenRequested?.Invoke(this, url);
            });
            var remove = PopupButton("제거", "링크 해제(글자는 남김)", () => ApplyLinkEditor(string.Empty));
            var label = new TextBlock { Text = "🔗", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 6, 0) };
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(label);
            panel.Children.Add(_linkUrlBox);
            panel.Children.Add(apply);
            panel.Children.Add(open);
            panel.Children.Add(remove);
            var frame = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(6), Child = panel };
            frame.SetResourceReference(Border.BackgroundProperty, "SurfaceBackground");
            frame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            frame.SetResourceReference(TextElement.FontFamilyProperty, "AppFontFamily");
            frame.SetResourceReference(TextElement.ForegroundProperty, "PrimaryText");
            _linkPopup = new Popup
            {
                AllowsTransparency = true,
                StaysOpen = false,
                Placement = PlacementMode.Bottom,
                Child = frame,
            };
            _linkPopup.Closed += (s, e) => _linkRow = null;
        }

        private static Button PopupButton(string text, string tip, Action click)
        {
            var button = new Button { Content = text, ToolTip = tip, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 0) };
            button.Click += (s, e) => click();
            return button;
        }

        private void ApplyLinkEditor(string url)
        {
            var row = _linkRow;
            var box = row?.Rich;
            CloseLinkEditor(false);
            if (box == null || IndexOf(row) < 0)
                return;
            box.Focus();
            var runs = box.Runs;
            var length = InlineMarkdown.ToPlain(runs).Length;
            var start = Math.Min(_linkStart, length);
            var end = Math.Min(_linkEnd, length);
            if (_linkInsert)
            {
                if (url.Length == 0)
                {
                    box.CaretIndex = start;
                    return;
                }
                var inserted = new[] { new InlineRun { Text = url, Link = url } };
                ApplyRuns(row, box, InlineMarkdown.Replace(runs, start, start, inserted), start + url.Length, 0);
                ClearSpringFormat(box);
                return;
            }
            ApplyRuns(row, box, InlineMarkdown.ApplyLink(runs, start, end, url.Length == 0 ? null : url), start, end - start);
        }

        private void CloseLinkEditor(bool refocus)
        {
            var row = _linkRow;
            if (_linkPopup != null)
                _linkPopup.IsOpen = false;
            if (refocus && row?.Rich != null)
            {
                row.Rich.Focus();
                row.Rich.Select(_linkStart, _linkEnd - _linkStart);
            }
        }

        // ── 떠 있는 서식 도구막대 ─────────────────────────────────────────

        private bool _formatBarPending;

        private void UpdateFormatBarSoon()
        {
            if (_formatBarPending)
                return;
            _formatBarPending = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _formatBarPending = false;
                UpdateFormatBar();
            }));
        }

        /// <summary>서식 입력란에서 글자를 선택하면(마우스를 뗀 뒤) 선택 위에 도구막대를 띄운다. 버튼은 포커스를 가져가지 않는다.</summary>
        private void UpdateFormatBar()
        {
            var row = _richRow;
            var box = row?.Rich;
            if (box == null || !box.IsKeyboardFocused || box.Selection.IsEmpty || Mouse.LeftButton == MouseButtonState.Pressed || !box.IsVisible)
            {
                HideFormatBar();
                return;
            }
            EnsureFormatBar();
            var start = box.SelectionStart;
            var end = start + box.SelectionLength;
            var runs = box.Runs;
            foreach (var pair in _formatButtons)
                SetButtonActive(pair.Value, InlineMarkdown.HasFormat(runs, start, end, pair.Key));
            int ls, le;
            InlineRun link;
            SetButtonActive(_linkButton, InlineMarkdown.LinkRange(runs, start, out ls, out le, out link));
            var marks = TextMarks.FromRuns(runs);
            var plainLength = InlineMarkdown.ToPlain(runs).Length;
            SetButtonActive(_underlineButton, TextMarks.Covers(marks, plainLength, TextMarkKind.Underline, start, end));
            SetButtonActive(_colorButton, _colorPalette.Visibility == Visibility.Visible ||
                TextMarks.Covers(marks, plainLength, TextMarkKind.Color, start, end) || TextMarks.Covers(marks, plainLength, TextMarkKind.Highlight, start, end));
            UpdateColorPalette(marks, plainLength, start, end);
            Rect rect;
            try
            {
                rect = box.GetRectFromCharacterIndex(start);
            }
            catch (InvalidOperationException)
            {
                return;
            }
            if (rect.IsEmpty)
                return;
            _formatBar.PlacementTarget = box;
            _formatBar.PlacementRectangle = new Rect(rect.X, rect.Y - 4, 1, rect.Height);
            if (_formatBar.IsOpen)
            {
                // 위치 다시 계산
                _formatBar.HorizontalOffset += 0.01;
                _formatBar.HorizontalOffset -= 0.01;
            }
            else
            {
                _formatBar.IsOpen = true;
            }
        }

        private void HideFormatBar()
        {
            if (_formatBar != null && _formatBar.IsOpen)
            {
                _formatBar.IsOpen = false;
                _colorPalette.Visibility = Visibility.Collapsed;
            }
        }

        private Border _underlineButton;
        private Border _colorButton;
        private StackPanel _colorPalette;
        private readonly List<Tuple<Border, string, bool>> _colorSwatches = new List<Tuple<Border, string, bool>>();

        private void EnsureFormatBar()
        {
            if (_formatBar != null)
                return;
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            _formatButtons[InlineFormat.Bold] = FormatButton(panel, "B", "굵게 (Ctrl+B)", b => b.FontWeight = FontWeights.Bold, () => ToggleInlineFormat(_richRow, InlineFormat.Bold));
            _formatButtons[InlineFormat.Italic] = FormatButton(panel, "I", "기울임 (Ctrl+I)", b => b.FontStyle = FontStyles.Italic, () => ToggleInlineFormat(_richRow, InlineFormat.Italic));
            _underlineButton = FormatButton(panel, "U", "밑줄 (Ctrl+U)", b => b.TextDecorations = TextDecorations.Underline, () => ToggleUnderline(_richRow));
            _formatButtons[InlineFormat.Strike] = FormatButton(panel, "S", "취소선 (Ctrl+Shift+S)", b => b.TextDecorations = TextDecorations.Strikethrough, () => ToggleInlineFormat(_richRow, InlineFormat.Strike));
            _formatButtons[InlineFormat.Code] = FormatButton(panel, "<>", "코드 (Ctrl+E)", b => b.FontFamily = BlockInlineRenderer.CodeFont, () => ToggleInlineFormat(_richRow, InlineFormat.Code));
            _linkButton = FormatButton(panel, "🔗", "링크 (Ctrl+K)", null, () => ShowLinkEditor(_richRow));
            _colorButton = FormatButton(panel, "A▾", "글자 색·배경색 (선택한 글자만 — 블록 전체 색은 블록 메뉴 > 색)", b => b.TextDecorations = TextDecorations.Underline, () =>
            {
                _colorPalette.Visibility = _colorPalette.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                UpdateFormatBar();
            });
            _colorPalette = CreateColorPalette();
            var layout = new StackPanel();
            layout.Children.Add(panel);
            layout.Children.Add(_colorPalette);
            var frame = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(3), Child = layout };
            frame.SetResourceReference(Border.BackgroundProperty, "SurfaceBackground");
            frame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            frame.SetResourceReference(TextElement.FontFamilyProperty, "AppFontFamily");
            _formatBar = new Popup
            {
                AllowsTransparency = true,
                StaysOpen = true,
                Focusable = false,
                Placement = PlacementMode.Top,
                Child = frame,
            };
            _scroll.ScrollChanged += (s, e) =>
            {
                if (e.VerticalChange != 0)
                    HideFormatBar();
            };
        }

        /// <summary>도구막대 아래 색 고르기: 글자 색 줄(기본 + 9가지), 배경색 줄(기본 + 9가지), 모두 지우기. 선택한 글자에만 칠한다.</summary>
        private StackPanel CreateColorPalette()
        {
            var palette = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(2, 4, 2, 2) };
            palette.Children.Add(PaletteRow("글자 색", false));
            palette.Children.Add(PaletteRow("배경색", true));
            var clear = new TextBlock { Text = "글자 색·배경색 지우기", FontSize = 12, Margin = new Thickness(6, 3, 6, 3) };
            clear.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
            var clearButton = new Border { CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand, Background = Brushes.Transparent, Child = clear, Margin = new Thickness(0, 2, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Focusable = false };
            clearButton.MouseEnter += (s, e) => clearButton.SetResourceReference(Border.BackgroundProperty, "ControlHoverBrush");
            clearButton.MouseLeave += (s, e) => clearButton.Background = Brushes.Transparent;
            clearButton.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                ClearTextMarkColors(_richRow);
            };
            palette.Children.Add(clearButton);
            return palette;
        }

        private StackPanel PaletteRow(string title, bool highlight)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
            var label = new TextBlock { Text = title, FontSize = 11, Width = 44, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 2, 0) };
            label.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            row.Children.Add(label);
            row.Children.Add(ColorSwatch(null, highlight, "기본"));
            for (var i = 0; i < BlockColorNames.Length; i++)
                row.Children.Add(ColorSwatch(BlockColorNames[i], highlight, BlockColorLabels[i] + (highlight ? " 배경" : "")));
            return row;
        }

        /// <summary>색 견본 하나(글자 색은 그 색의 "A", 배경색은 그 바탕의 "A"). 누르면 선택 글자에 칠한다(<paramref name="name"/>이 null이면 그 종류를 지움).</summary>
        private Border ColorSwatch(string name, bool highlight, string tip)
        {
            var letter = new TextBlock { Text = "A", FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var swatch = new Border
            {
                Width = 20,
                Height = 20,
                Margin = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = tip,
                Focusable = false,
                Child = letter,
            };
            swatch.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            swatch.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                SetTextMarkColor(_richRow, highlight, name);
            };
            _colorSwatches.Add(Tuple.Create(swatch, name, highlight));
            return swatch;
        }

        /// <summary>견본 색을 지금 테마로 칠하고, 선택 글자가 모두 그 색이면 테두리를 강조한다(도구막대는 편집기 리소스 밖이라 직접 칠함).</summary>
        private void UpdateColorPalette(IList<TextMark> marks, int plainLength, int start, int end)
        {
            if (_colorPalette == null || _colorPalette.Visibility != Visibility.Visible)
                return;
            foreach (var item in _colorSwatches)
            {
                var swatch = item.Item1;
                var name = item.Item2;
                var highlight = item.Item3;
                var letter = (TextBlock)swatch.Child;
                if (highlight)
                {
                    letter.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
                    if (name == null)
                        swatch.SetResourceReference(Border.BackgroundProperty, "PanelBackground");
                    else
                        swatch.Background = BlockColorBrush(name, true);
                }
                else
                {
                    swatch.SetResourceReference(Border.BackgroundProperty, "PanelBackground");
                    if (name == null)
                        letter.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
                    else
                        letter.Foreground = BlockColorBrush(name, false);
                }
                var kind = highlight ? TextMarkKind.Highlight : TextMarkKind.Color;
                var active = name != null && TextMarks.Covers(marks, plainLength, kind, start, end, name);
                swatch.BorderThickness = new Thickness(active ? 2 : 1);
                if (active)
                    swatch.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                else
                    swatch.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            }
        }

        private static Border FormatButton(Panel panel, string text, string tip, Action<TextBlock> style, Action click)
        {
            var label = new TextBlock { Text = text, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
            style?.Invoke(label);
            var button = new Border
            {
                MinWidth = 28,
                Height = 26,
                Padding = new Thickness(6, 0, 6, 0),
                CornerRadius = new CornerRadius(4),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = tip,
                Focusable = false,
                Child = label,
            };
            button.MouseEnter += (s, e) =>
            {
                if (!(button.Tag is bool active && active))
                    button.SetResourceReference(Border.BackgroundProperty, "ControlHoverBrush");
            };
            button.MouseLeave += (s, e) => SetButtonActive(button, button.Tag is bool active && active);
            button.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;   // 입력란 포커스·선택을 그대로 둔다
                click();
            };
            panel.Children.Add(button);
            return button;
        }

        private static void SetButtonActive(Border button, bool active)
        {
            if (button == null)
                return;
            button.Tag = active;
            if (active)
                button.SetResourceReference(Border.BackgroundProperty, "ControlPressedBrush");
            else
                button.Background = Brushes.Transparent;
        }

        // ── 링크 열기(Ctrl+클릭) ──────────────────────────────────────────

        /// <summary>서식 입력란에서 누른 자리의 링크 주소(링크 조각 또는 맨 URL). 없으면 null.</summary>
        private static string RichLinkAt(RichBlockBox box, Point point)
        {
            var pointer = box.GetPositionFromPoint(point, false);
            if (pointer == null)
                return null;
            var offset = box.OffsetOf(pointer);
            int s, e;
            InlineRun link;
            if (InlineMarkdown.LinkRange(box.Runs, offset, out s, out e, out link) && offset < e)
                return link.Url;
            var text = box.Text;
            return BlockFind.LinkAt(text, Math.Min(offset, text.Length));
        }
    }
}

using Folderss.Models;
using Folderss.Services;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Folderss.Controls
{
    /// <summary>
    /// front matter 블록(<see cref="BlockKind.FrontMatter"/>)을 Notion 페이지 속성처럼 속성 표(키 | 값)로 보여 주고 편집한다(#81).
    /// <list type="bullet">
    /// <item>해석·편집은 순수 로직 <see cref="FrontMatter"/> — 편집한 속성의 줄만 다시 쓰고 나머지 줄(주석·순서·따옴표)은 그대로.</item>
    /// <item>표는 그 블록 행이 화면에 만들어질 때만 그리고(가상화), 본문이 바뀔 때만 다시 그린다. 입력란은 누른 칸 하나에만 만든다(입력기 TSF 잠금 회피).</item>
    /// <item>편집 한 번이 실행 취소 한 단계(그 블록 하나만 기록). 원문 편집으로 바꾸면 블록이 잠시 <see cref="BlockKind.Raw"/>가 되고,
    /// 입력란을 벗어나면 다시 속성 표가 된다(보기 전환이라 저장할 변경으로 세지 않는다).</item>
    /// </list>
    /// </summary>
    public sealed partial class BlockEditor
    {
        private const string NewPropertyName = "속성";

        /// <summary>속성 표 칸을 편집 중인 행(입력란이 열려 있음). 없으면 null.</summary>
        private BlockRow _frontMatterEditingRow;

        /// <summary>속성 표에서 편집 중인 칸이 있으면 적용한다(저장·Markdown 가져오기 전에 — 보이는 대로 저장되게).</summary>
        private void CommitFrontMatterEditing()
        {
            _frontMatterEditingRow?.CommitFrontMatterEdit();
        }

        /// <summary>
        /// 붙여넣는 블록은 늘 다른 블록 뒤에 들어가므로 front matter가 될 수 없다 — 원문 블록으로 둔다
        /// (그대로 저장되는 것은 같고, 문서 중간에 속성 표가 보이지 않게).
        /// </summary>
        private static void PastedFrontMatterAsRaw(List<EditorBlock> blocks)
        {
            foreach (var block in blocks)
                if (block.Kind == BlockKind.FrontMatter)
                    block.Kind = BlockKind.Raw;
        }

        /// <summary>속성 표에서 바꾼 front matter 본문을 적용하고 그 블록 하나만 실행 취소 기록에 남긴다.</summary>
        private void ApplyFrontMatterEdit(BlockRow row, string text)
        {
            if (text == null || text == row.Block.Text || IndexOf(row) < 0)
                return;
            row.Block.Text = text;
            row.SyncFromBlock();
            _panel.InvalidateMeasure();
            // 글자 입력 기록 경로(그 블록만 비교)를 쓰되, 앞뒤 입력과 한 단계로 묶이지 않게 한다.
            _typingRow = null;
            RecordChange(row);
            _typingRow = null;
        }

        /// <summary>
        /// front matter 보기(속성 표 ↔ 원문)를 바꾼다. 본문은 그대로라 저장할 변경이 아니므로 기록하지 않고,
        /// 기록 기준 사본의 종류도 함께 바꿔 다음 기록이 이 블록을 바뀐 구간으로 잡지 않게 한다.
        /// </summary>
        private void SetFrontMatterView(BlockRow row, BlockKind kind)
        {
            var index = IndexOf(row);
            if (index < 0 || row.Block.Kind == kind)
                return;
            row.Block.Kind = kind;
            if (_committed.Count == _blocks.Count && _committed[index].Id == row.Block.Id)
            {
                var committed = _committed[index].Clone();
                committed.Kind = kind;
                _committed[index] = committed;
            }
            _typingRow = null;
            row.SyncFromBlock();
            _panel.InvalidateMeasure();
        }

        /// <summary>속성 표의 `원문 편집`: 블록을 원문 입력란으로 바꾸고 커서를 둔다.</summary>
        private void EditFrontMatterSource(BlockRow row)
        {
            var index = IndexOf(row);
            if (index < 0)
                return;
            if (HasBlockSelection)
                ClearSelection();
            SetFrontMatterView(row, BlockKind.Raw);
            FocusRow(index, 0);
        }

        /// <summary>원문으로 편집하던 front matter 입력란을 벗어나면, 아직 맨 앞에 있고 front matter 모양이면 속성 표로 돌린다.</summary>
        private void ScheduleFrontMatterTable(BlockRow row)
        {
            if (row.Block.Kind != BlockKind.Raw)
                return;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (row.Block.Kind != BlockKind.Raw || IndexOf(row) != 0 || row.IsMainEditorFocused ||
                    !FrontMatter.LooksLikeFrontMatter(row.Block.Text))
                    return;
                // 창 전환으로 포커스를 잃은 것이면 돌아왔을 때 계속 편집하도록 그대로 둔다.
                var window = Window.GetWindow(this);
                if (window != null && !window.IsActive)
                    return;
                if (_focusedRow == row)
                    _focusedRow = null;
                SetFrontMatterView(row, BlockKind.FrontMatter);
            }));
        }

        private sealed partial class BlockRow
        {
            private StackPanel _fmPanel;
            private string _fmRendered;                 // 표를 그린 본문(같으면 다시 그리지 않음)
            private readonly List<Border> _fmKeyCells = new List<Border>();
            private readonly List<Border> _fmValueCells = new List<Border>();
            private readonly List<FrontMatterEntry> _fmEntries = new List<FrontMatterEntry>();
            private TextBox _fmEditor;                  // 지금 편집 중인 칸의 입력란(없으면 null)
            private int _fmEditIndex = -1;
            private bool _fmEditKey;

            /// <summary>본문 입력란에 키보드 포커스가 있는지(입력란을 새로 만들지 않는다).</summary>
            public bool IsMainEditorFocused => _mainEditor != null && _mainEditor.IsKeyboardFocusWithin;

            /// <summary>이 블록이 문서 맨 앞의 front matter 모양 원문인지(원문 편집 중 안내용).</summary>
            private bool IsFrontMatterSource()
            {
                return _owner._blocks.Count > 0 && ReferenceEquals(_owner._blocks[0], Block) && FrontMatter.LooksLikeFrontMatter(Block.Text);
            }

            private StackPanel EnsureFrontMatterPanel()
            {
                if (_fmPanel != null)
                    return _fmPanel;
                _fmPanel = new StackPanel();
                Grid.SetColumn(_fmPanel, 1);
                Grid.SetRow(_fmPanel, 1);
                _bodyGrid.Children.Add(_fmPanel);
                return _fmPanel;
            }

            /// <summary>본문이 바뀌었으면 속성 표를 다시 그린다. 편집 중이던 칸은 닫힌다(실행 취소 등으로 본문이 바뀐 경우).</summary>
            public void RebuildFrontMatter()
            {
                var panel = EnsureFrontMatterPanel();
                if (_fmRendered != null && _fmRendered == Block.Text)
                    return;
                _fmRendered = Block.Text;
                CloseFrontMatterEditor();
                panel.Children.Clear();
                _fmKeyCells.Clear();
                _fmValueCells.Clear();
                _fmEntries.Clear();

                var doc = FrontMatter.Parse(Block.Text);
                panel.Children.Add(CreateFrontMatterHeader(doc.IsTable));

                if (!doc.IsTable)
                {
                    var note = new TextBlock
                    {
                        Text = "속성 표로 읽을 수 없는 front matter라 원문 그대로 보여 줍니다.",
                        FontSize = 12,
                        Margin = new Thickness(2, 0, 2, 4),
                        TextWrapping = TextWrapping.Wrap,
                    };
                    note.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
                    panel.Children.Add(note);
                    panel.Children.Add(CreateCodeText(doc.Body, "PrimaryText"));
                    return;
                }

                for (var i = 0; i < doc.Entries.Count; i++)
                    panel.Children.Add(CreateFrontMatterRow(i, doc.Entries[i]));

                if (doc.Entries.Count == 0)
                {
                    var empty = new TextBlock
                    {
                        Text = "속성이 없습니다. `+ 속성 추가`로 더할 수 있습니다.",
                        FontSize = 12,
                        Margin = new Thickness(4, 2, 4, 2),
                    };
                    empty.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
                    panel.Children.Add(empty);
                }
            }

            private UIElement CreateFrontMatterHeader(bool editable)
            {
                var header = new DockPanel { Margin = new Thickness(2, 0, 2, 4), LastChildFill = true };
                var source = CreateLinkButton("원문 편집", "front matter YAML 원문을 직접 고칩니다. 입력란을 벗어나면 속성 표로 돌아갑니다.",
                    () => _owner.EditFrontMatterSource(this));
                DockPanel.SetDock(source, Dock.Right);
                header.Children.Add(source);
                if (editable)
                {
                    var add = CreateLinkButton("+ 속성 추가", "새 속성을 front matter 끝에 더합니다.", AddFrontMatterProperty);
                    DockPanel.SetDock(add, Dock.Right);
                    header.Children.Add(add);
                }
                var title = new TextBlock
                {
                    Text = "속성",
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "문서 맨 앞 YAML front matter. 저장할 때 원문 그대로 쓰고, 고친 속성의 줄만 바뀝니다.",
                };
                title.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
                header.Children.Add(title);
                return header;
            }

            private static Border CreateLinkButton(string text, string tooltip, Action onClick)
            {
                var label = new TextBlock { Text = text, FontSize = 12 };
                label.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                var button = new Border
                {
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(4, 0, 0, 0),
                    CornerRadius = new CornerRadius(3),
                    Background = Brushes.Transparent,
                    Cursor = Cursors.Hand,
                    ToolTip = tooltip,
                    Child = label,
                };
                button.MouseEnter += (s, e) => button.SetResourceReference(Border.BackgroundProperty, "ControlHoverBrush");
                button.MouseLeave += (s, e) => button.Background = Brushes.Transparent;
                button.MouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    onClick();
                };
                return button;
            }

            private static TextBlock CreateCodeText(string text, string brushKey)
            {
                var code = new TextBlock
                {
                    Text = text,
                    FontFamily = CodeFont,
                    FontSize = 12.5,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(2, 1, 2, 1),
                };
                code.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
                return code;
            }

            private UIElement CreateFrontMatterRow(int index, FrontMatterEntry entry)
            {
                var row = new Grid { Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 1) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var keyText = new TextBlock
                {
                    Text = entry.Key,
                    FontSize = 13.5,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = entry.Key + " — 클릭해 이름 바꾸기",
                };
                keyText.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
                var keyCell = CreateCell(keyText);
                keyCell.MouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    BeginFrontMatterEdit(index, true);
                };
                row.Children.Add(keyCell);

                var valueCell = CreateCell(CreateValueView(entry));
                Grid.SetColumn(valueCell, 1);
                valueCell.MouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    if (entry.Kind == FrontMatterValueKind.Complex)
                        _owner.EditFrontMatterSource(this);
                    else
                        BeginFrontMatterEdit(index, false);
                };
                row.Children.Add(valueCell);

                var remove = new TextBlock
                {
                    Text = "×",
                    FontSize = 14,
                    Width = 22,
                    TextAlignment = TextAlignment.Center,
                    Cursor = Cursors.Hand,
                    Background = Brushes.Transparent,
                    ToolTip = "속성 삭제",
                    Opacity = 0,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 3, 0, 0),
                };
                remove.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
                remove.MouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    RemoveFrontMatterProperty(index);
                };
                Grid.SetColumn(remove, 2);
                row.Children.Add(remove);

                row.MouseEnter += (s, e) =>
                {
                    row.SetResourceReference(Panel.BackgroundProperty, "RowHoverBrush");
                    remove.Opacity = 1;
                };
                row.MouseLeave += (s, e) =>
                {
                    row.Background = Brushes.Transparent;
                    remove.Opacity = 0;
                };

                _fmKeyCells.Add(keyCell);
                _fmValueCells.Add(valueCell);
                _fmEntries.Add(entry);
                return row;
            }

            private static Border CreateCell(UIElement child)
            {
                return new Border
                {
                    Padding = new Thickness(4, 3, 6, 3),
                    Background = Brushes.Transparent,
                    Cursor = Cursors.IBeam,
                    Child = child,
                };
            }

            private static UIElement CreateValueView(FrontMatterEntry entry)
            {
                switch (entry.Kind)
                {
                    case FrontMatterValueKind.List:
                        if (entry.Items.Count == 0)
                            return CreateEmptyValue();
                        var wrap = new WrapPanel();
                        foreach (var item in entry.Items)
                        {
                            var chipText = new TextBlock { Text = item, FontSize = 12.5 };
                            chipText.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
                            var chip = new Border
                            {
                                Padding = new Thickness(6, 1, 6, 1),
                                Margin = new Thickness(0, 0, 4, 2),
                                CornerRadius = new CornerRadius(3),
                                Child = chipText,
                            };
                            chip.SetResourceReference(Border.BackgroundProperty, "ControlBackground");
                            wrap.Children.Add(chip);
                        }
                        wrap.ToolTip = "목록 — 클릭해 쉼표로 구분해 고치기";
                        return wrap;

                    case FrontMatterValueKind.Complex:
                        var code = CreateCodeText(entry.RawValue, "SecondaryText");
                        code.ToolTip = "속성 표로 편집할 수 없는 YAML(중첩·여러 줄 등) — 클릭하면 원문 편집으로 바뀝니다.";
                        return code;

                    default:
                        if (entry.Value.Length == 0)
                            return CreateEmptyValue();
                        var text = new TextBlock { Text = entry.Value, FontSize = 14, TextWrapping = TextWrapping.Wrap };
                        text.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
                        return text;
                }
            }

            private static TextBlock CreateEmptyValue()
            {
                var empty = new TextBlock { Text = "비어 있음", FontSize = 13.5 };
                empty.SetResourceReference(TextBlock.ForegroundProperty, "DisabledTextBrush");
                return empty;
            }

            // ── 칸 편집 ────────────────────────────────────────────────────

            /// <summary><paramref name="index"/>번째 속성의 이름(<paramref name="key"/>) 또는 값 칸에 입력란을 만든다. 편집 중인 다른 칸은 먼저 적용한다.</summary>
            private void BeginFrontMatterEdit(int index, bool key)
            {
                if (_fmEditor != null)
                {
                    if (_fmEditIndex == index && _fmEditKey == key)
                        return;
                    CommitFrontMatterEdit();
                }
                if (index < 0 || index >= _fmEntries.Count)
                    return;
                var entry = _fmEntries[index];
                if (!key && entry.Kind == FrontMatterValueKind.Complex)
                    return;
                if (_owner.HasBlockSelection)
                    _owner.ClearSelection();

                var initial = key ? entry.Key
                    : entry.Kind == FrontMatterValueKind.List ? FrontMatter.FormatListForEdit(entry.Items)
                    : entry.Value;
                // 화면에 붙이기 전에 내용을 넣는다(CreateTextBox). 편집기 키 처리가 본문 입력란으로 보지 않게 Tag는 비운다.
                var box = CreateTextBox(initial);
                box.Tag = null;
                box.AcceptsReturn = false;
                box.AcceptsTab = false;
                box.FontSize = key ? 13.5 : 14;
                box.SetResourceReference(TextElement.FontFamilyProperty, "AppFontFamily");
                box.SetResourceReference(TextElement.ForegroundProperty, "PrimaryText");
                box.SetResourceReference(Control.BackgroundProperty, "ControlBackground");
                box.ToolTip = key ? "속성 이름 (Enter 적용, Esc 취소)"
                    : entry.Kind == FrontMatterValueKind.List ? "쉼표로 구분 (쉼표가 든 항목은 \"따옴표\"로 감싸기). Enter 적용, Esc 취소"
                    : "Enter 적용, Esc 취소, Tab 다음 칸";
                box.PreviewKeyDown += FrontMatterEditor_PreviewKeyDown;
                box.LostKeyboardFocus += (s, e) =>
                {
                    if (ReferenceEquals(_fmEditor, box))
                        CommitFrontMatterEdit();
                };

                _fmEditor = box;
                _fmEditIndex = index;
                _fmEditKey = key;
                var cell = key ? _fmKeyCells[index] : _fmValueCells[index];
                cell.Child = box;
                cell.Padding = new Thickness(2, 0, 4, 0);
                _owner._frontMatterEditingRow = this;
                if (!box.IsVisible)
                    box.UpdateLayout();
                box.Focus();
                box.SelectAll();
            }

            private void FrontMatterEditor_PreviewKeyDown(object sender, KeyEventArgs e)
            {
                if (!ReferenceEquals(sender, _fmEditor))
                    return;
                var key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
                switch (key)
                {
                    case Key.Enter:
                        // 한글 조합 중 Enter는 조합을 끝낸 뒤 적용한다.
                        e.Handled = true;
                        if (e.Key == Key.ImeProcessed)
                            _owner.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => FinishFrontMatterEdit(true)));
                        else
                            FinishFrontMatterEdit(true);
                        break;
                    case Key.Escape:
                        e.Handled = true;
                        FinishFrontMatterEdit(false);
                        break;
                    case Key.Tab:
                        if (e.Key == Key.ImeProcessed)
                            break;
                        e.Handled = true;
                        MoveFrontMatterEdit(Keyboard.Modifiers == ModifierKeys.Shift ? -1 : 1);
                        break;
                }
            }

            /// <summary>Enter(적용)·Esc(취소) 뒤에는 front matter 블록을 선택해 키보드로 계속 다룰 수 있게 한다.</summary>
            private void FinishFrontMatterEdit(bool commit)
            {
                if (_fmEditor == null)
                    return;
                if (commit)
                    CommitFrontMatterEdit();
                else
                    CloseFrontMatterEditor(true);
                var index = _owner.IndexOf(this);
                if (index >= 0)
                    _owner.SelectBlocks(index, index);
            }

            /// <summary>Tab·Shift+Tab: 적용하고 다음·이전 칸(이름 → 값 → 다음 속성 이름, 복잡한 값은 건너뜀)으로.</summary>
            private void MoveFrontMatterEdit(int direction)
            {
                var slot = _fmEditIndex * 2 + (_fmEditKey ? 0 : 1);
                CommitFrontMatterEdit();
                for (slot += direction; slot >= 0 && slot < _fmEntries.Count * 2; slot += direction)
                {
                    var isKey = slot % 2 == 0;
                    if (!isKey && _fmEntries[slot / 2].Kind == FrontMatterValueKind.Complex)
                        continue;
                    BeginFrontMatterEdit(slot / 2, isKey);
                    return;
                }
                var index = _owner.IndexOf(this);
                if (index >= 0)
                    _owner.SelectBlocks(index, index);
            }

            /// <summary>입력란 내용을 본문에 적용한다(바뀐 속성의 줄만). 이름이 비었거나 겹치면 적용하지 않고 원래대로 둔다.</summary>
            public void CommitFrontMatterEdit()
            {
                var box = _fmEditor;
                if (box == null)
                    return;
                var index = _fmEditIndex;
                var key = _fmEditKey;
                _fmEditor = null;
                _fmEditIndex = -1;
                ReleaseEditingRow();
                if (index < 0 || index >= _fmEntries.Count)
                {
                    _fmRendered = null;
                    RebuildFrontMatter();
                    return;
                }

                var entry = _fmEntries[index];
                string next;
                if (key)
                    next = FrontMatter.RenameKey(Block.Text, index, entry.Key, box.Text);
                else if (entry.Kind == FrontMatterValueKind.List)
                    next = FrontMatter.SetItems(Block.Text, index, entry.Key, FrontMatter.SplitListInput(box.Text));
                else
                    next = FrontMatter.SetValue(Block.Text, index, entry.Key, box.Text);

                // 입력란을 걷어 내려면 표를 다시 그린다(본문이 바뀌면 ApplyFrontMatterEdit → SyncFromBlock이 그린다).
                _fmRendered = null;
                if (next != null && next != Block.Text)
                    _owner.ApplyFrontMatterEdit(this, next);
                else
                    RebuildFrontMatter();
            }

            /// <summary>편집 중인 칸을 적용하지 않고 닫는다. <paramref name="redraw"/>면 표를 다시 그려 원래 칸을 되살린다.</summary>
            private void CloseFrontMatterEditor(bool redraw = false)
            {
                if (_fmEditor == null)
                    return;
                _fmEditor = null;
                _fmEditIndex = -1;
                ReleaseEditingRow();
                if (redraw)
                {
                    _fmRendered = null;
                    RebuildFrontMatter();
                }
            }

            private void ReleaseEditingRow()
            {
                if (ReferenceEquals(_owner._frontMatterEditingRow, this))
                    _owner._frontMatterEditingRow = null;
            }

            private void AddFrontMatterProperty()
            {
                if (_fmEditor != null)
                    CommitFrontMatterEdit();
                var next = FrontMatter.AddEntry(Block.Text, FrontMatter.UniqueKey(Block.Text, NewPropertyName));
                if (next == null)
                    return;
                _owner.ApplyFrontMatterEdit(this, next);
                // 새 속성은 맨 끝 행 — 이름 칸을 바로 편집한다.
                if (Block.Kind == BlockKind.FrontMatter && _fmEntries.Count > 0)
                    BeginFrontMatterEdit(_fmEntries.Count - 1, true);
            }

            private void RemoveFrontMatterProperty(int index)
            {
                if (_fmEditor != null)
                    CloseFrontMatterEditor();
                if (index < 0 || index >= _fmEntries.Count)
                    return;
                var next = FrontMatter.RemoveEntry(Block.Text, index, _fmEntries[index].Key);
                if (next != null)
                    _owner.ApplyFrontMatterEdit(this, next);
                else
                {
                    _fmRendered = null;
                    RebuildFrontMatter();
                }
            }
        }
    }
}

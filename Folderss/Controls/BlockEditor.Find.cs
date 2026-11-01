using Folderss.Models;
using Folderss.Services;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;

namespace Folderss.Controls
{
    /// <summary>
    /// 블록 편집기 찾기·바꾸기(<c>Ctrl+F</c>·<c>Ctrl+H</c>·<c>F3</c>/<c>Shift+F3</c>), 제목 앵커 이동, 링크 <c>Ctrl+클릭</c>, 외부 변경 뒤 스크롤 위치 복원(#75).
    /// <para>
    /// 가상화(화면에 보이는 블록만 행 생성) 때문에 화면이 아니라 모델(<see cref="_blocks"/>)에서 찾는다(<see cref="BlockFind"/>).
    /// 지금 결과만 그 행의 입력란 선택으로 강조하고(그 행에만 TextBox — 다른 결과는 화면에 칠하지 않음), 결과 목록은 문서 버전(<see cref="_version"/>)이
    /// 바뀐 뒤 찾기를 쓸 때나 입력이 잠깐 멈췄을 때만 다시 만든다 — 글자 입력 경로에는 아무것도 더하지 않는다.
    /// </para>
    /// </summary>
    public sealed partial class BlockEditor
    {
        private static readonly TimeSpan FindRefreshIdle = TimeSpan.FromMilliseconds(300);

        private Border _findBar;
        private TextBox _findBox;
        private TextBox _replaceBox;
        private TextBlock _findCount;
        private CheckBox _findMatchCase;
        private FrameworkElement _replaceRow;
        private Button _replaceToggle;
        private DispatcherTimer _findRefreshTimer;

        private List<BlockMatch> _matches = new List<BlockMatch>();
        private long _matchesVersion = -1;
        private string _matchesQuery;
        private bool _matchesCase;
        private int _currentMatch = -1;
        // 다음 찾기 시작점: 마지막으로 보여 준(또는 바꾼) 결과 위치. inclusive면 그 위치에서 시작하는 결과도 고른다(바꾼 직후).
        private int _findCursorBlock = -1;
        private int _findCursorOffset;
        private bool _findCursorInclusive;
        private IBlockInput _findHighlightEditor;

        /// <summary>본문 링크를 <c>Ctrl+클릭</c>했다(Markdown 링크 주소 원문). 뷰어가 열거나 문서 안 제목으로 옮긴다.</summary>
        public event EventHandler<string> LinkOpenRequested;

        /// <summary>찾기 막대가 열려 있는지.</summary>
        public bool IsFindOpen => _findBar != null && _findBar.Visibility == Visibility.Visible;

        /// <summary>찾기 결과 수(문서가 바뀌었으면 다시 센다). 하네스 검사용.</summary>
        public int FindMatchCount
        {
            get
            {
                EnsureMatches();
                return _matches.Count;
            }
        }

        /// <summary>지금 보여 준 결과 번호(0부터, 없으면 -1). 하네스 검사용.</summary>
        public int CurrentFindMatch => _currentMatch;

        /// <summary>생성자에서 부른다: 찾기 막대를 스크롤 영역 위에 겹쳐 놓고 링크 Ctrl+클릭을 받는다.</summary>
        private void InitializeFind()
        {
            var scroll = Content as UIElement;
            Content = null;
            var root = new Grid();
            if (scroll != null)
                root.Children.Add(scroll);
            root.Children.Add(CreateFindBar());
            Content = root;

            PreviewMouseLeftButtonDown += OnLinkPreviewMouseDown;
        }

        private Border CreateFindBar()
        {
            _findBox = new TextBox { Width = 200, Margin = new Thickness(0, 0, 4, 0), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "찾을 내용 (Enter 다음 · Shift+Enter 이전 · Esc 닫기)" };
            _findBox.TextChanged += (s, e) => OnFindQueryChanged();
            _replaceBox = new TextBox { Width = 200, Margin = new Thickness(0, 0, 4, 0), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "바꿀 내용 (Enter 바꾸기 · Ctrl+Alt+Enter 모두 바꾸기)" };

            _findCount = new TextBlock { MinWidth = 64, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 6, 0), FontSize = 11 };
            _findCount.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");

            _findMatchCase = new CheckBox { Content = "Aa", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), ToolTip = "대/소문자 구분", Focusable = false };
            _findMatchCase.SetResourceReference(ForegroundProperty, "PrimaryText");
            _findMatchCase.Click += (s, e) => OnFindQueryChanged();

            var previous = CreateFindButton("↑", "이전 (Shift+F3)", () => FindNext(false));
            var next = CreateFindButton("↓", "다음 (F3)", () => FindNext(true));
            var close = CreateFindButton("✕", "닫기 (Esc)", () => CloseFind(true));
            _replaceToggle = CreateFindButton("⇄", "바꾸기 보이기/숨기기 (Ctrl+H)", () => SetReplaceVisible(_replaceRow.Visibility != Visibility.Visible));

            var findRow = new StackPanel { Orientation = Orientation.Horizontal };
            findRow.Children.Add(_replaceToggle);
            findRow.Children.Add(_findBox);
            findRow.Children.Add(_findCount);
            findRow.Children.Add(_findMatchCase);
            findRow.Children.Add(previous);
            findRow.Children.Add(next);
            findRow.Children.Add(close);

            var replaceOne = CreateFindButton("바꾸기", "지금 결과 바꾸기 (Enter)", ReplaceCurrent);
            var replaceAll = CreateFindButton("모두 바꾸기", "모두 바꾸기 (Ctrl+Alt+Enter) — 실행 취소 한 번에 되돌림", ReplaceAllMatches);
            var replaceRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
            // 찾기 줄의 ⇄ 버튼 폭만큼 들여 입력란 줄을 맞춘다.
            replaceRow.Children.Add(new FrameworkElement { Width = 30 });
            replaceRow.Children.Add(_replaceBox);
            replaceRow.Children.Add(replaceOne);
            replaceRow.Children.Add(replaceAll);
            _replaceRow = replaceRow;

            var stack = new StackPanel();
            stack.Children.Add(findRow);
            stack.Children.Add(replaceRow);

            _findBar = new Border
            {
                Child = stack,
                Padding = new Thickness(6),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                // 세로 스크롤바를 가리지 않게 오른쪽을 비운다.
                Margin = new Thickness(0, 4, 24, 0),
                Visibility = Visibility.Collapsed,
            };
            _findBar.SetResourceReference(Border.BackgroundProperty, "SurfaceBackground");
            _findBar.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            return _findBar;
        }

        private static Button CreateFindButton(string text, string tooltip, Action action)
        {
            var button = new Button { Content = text, ToolTip = tooltip, MinWidth = 26, Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 4, 0), Focusable = false };
            button.Click += (s, e) => action();
            return button;
        }

        // ── 키 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// <see cref="OnPreviewKeyDown"/> 맨 앞에서 부른다. 찾기 단축키를 처리하고, 찾기 막대 입력란에서 누른 키는 편집기 키 처리로 넘기지 않는다(true).
        /// </summary>
        private bool HandleFindKey(KeyEventArgs e, Key key, ModifierKeys mods)
        {
            var inFind = _findBar != null && _findBar.IsKeyboardFocusWithin;
            if (mods == ModifierKeys.Control && key == Key.F)
            {
                OpenFind(false);
                e.Handled = true;
                return true;
            }
            if (mods == ModifierKeys.Control && key == Key.H)
            {
                OpenFind(true);
                e.Handled = true;
                return true;
            }
            if (key == Key.F3 && (mods == ModifierKeys.None || mods == ModifierKeys.Shift))
            {
                FindNext(mods == ModifierKeys.None);
                e.Handled = true;
                return true;
            }
            if (!inFind)
                return false;

            if (key == Key.Escape && mods == ModifierKeys.None)
            {
                CloseFind(true);
                e.Handled = true;
                return true;
            }
            if (e.Key == Key.Enter)
            {
                if (_replaceBox.IsKeyboardFocused && mods == (ModifierKeys.Control | ModifierKeys.Alt))
                    ReplaceAllMatches();
                else if (_replaceBox.IsKeyboardFocused && mods == ModifierKeys.None)
                    ReplaceCurrent();
                else
                    FindNext(mods != ModifierKeys.Shift);
                e.Handled = true;
                return true;
            }
            // 저장은 찾기 막대에서도 된다. 나머지(Ctrl+Z 등)는 찾기 입력란이 직접 처리한다.
            return !(mods == ModifierKeys.Control && key == Key.S);
        }

        // ── 열기·닫기 ──────────────────────────────────────────────────────

        /// <summary>찾기 막대를 연다(<paramref name="replace"/>면 바꾸기 줄까지). 입력란에서 한 줄을 선택해 두었으면 그 글자로 찾는다.</summary>
        public void OpenFind(bool replace)
        {
            if (_findBar == null)
                return;
            CloseSlash();

            // 시작점: 지금 커서(선택 시작). 선택한 한 줄 글자는 검색어로.
            var editor = Keyboard.FocusedElement as IBlockInput;
            var row = editor?.Tag as BlockRow;
            var index = IndexOf(row);
            string selected = null;
            if (index >= 0)
            {
                _findCursorBlock = index;
                _findCursorOffset = ToBlockOffset(row, editor, editor.SelectionStart);
                _findCursorInclusive = true;
                if (editor.SelectionLength > 0 && editor.SelectedText.IndexOfAny(new[] { '\r', '\n', EditorBlock.CellSeparator }) < 0)
                    selected = editor.SelectedText;
            }

            var wasOpen = IsFindOpen;
            _findBar.Visibility = Visibility.Visible;
            if (replace)
                SetReplaceVisible(true);
            else if (!wasOpen)
                SetReplaceVisible(false);

            if (selected != null && selected != _findBox.Text)
                _findBox.Text = selected;   // OnFindQueryChanged가 첫 결과로 옮긴다
            else
                UpdateFindCount();

            EnsureFindRefreshTimer().Start();
            var target = replace && _findBox.Text.Length > 0 && selected == null && wasOpen ? _replaceBox : _findBox;
            target.Focus();
            target.SelectAll();
        }

        private void SetReplaceVisible(bool visible)
        {
            _replaceRow.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            _replaceToggle.Content = visible ? "⇅" : "⇄";
        }

        /// <summary>찾기 막대를 닫는다. <paramref name="focusEditor"/>면 지금 결과(없으면 마지막 커서 블록)의 입력란으로 커서를 돌린다.</summary>
        public void CloseFind(bool focusEditor)
        {
            if (!IsFindOpen)
                return;
            _findBar.Visibility = Visibility.Collapsed;
            _findRefreshTimer?.Stop();
            var highlighted = _findHighlightEditor;
            SetHighlightEditor(null);
            if (!focusEditor)
                return;
            if (highlighted != null && highlighted.IsVisible)
            {
                var start = highlighted.SelectionStart;
                var length = highlighted.SelectionLength;
                highlighted.Focus();
                highlighted.Select(start, length);
            }
            else if (_focusedRow != null && IndexOf(_focusedRow) >= 0)
            {
                FocusRow(IndexOf(_focusedRow), _focusedRow.CurrentCaret, _focusedRow.ActiveColumn);
            }
            else
            {
                Focus();
            }
        }

        private DispatcherTimer EnsureFindRefreshTimer()
        {
            if (_findRefreshTimer != null)
                return _findRefreshTimer;
            // 찾기 막대가 열린 동안 문서가 바뀌면 결과 수를 맞춘다. 입력이 잠깐 멈췄을 때만 센다(입력 경로에 비용을 더하지 않게).
            _findRefreshTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(400) };
            _findRefreshTimer.Tick += (s, e) =>
            {
                if (!IsFindOpen)
                {
                    _findRefreshTimer.Stop();
                    return;
                }
                if (_matchesVersion != _version && DateTime.UtcNow - _typingAt >= FindRefreshIdle)
                    UpdateFindCount();
            };
            return _findRefreshTimer;
        }

        // ── 찾기 ───────────────────────────────────────────────────────────

        private void OnFindQueryChanged()
        {
            EnsureMatches();
            if (_matches.Count > 0)
            {
                // 입력하는 대로 시작점에서 가장 가까운 다음 결과로 옮긴다.
                var block = _findCursorBlock >= 0 ? _findCursorBlock : 0;
                ShowMatch(BlockFind.NextMatch(_matches, block, _findCursorBlock >= 0 ? _findCursorOffset : 0, true, true), false);
            }
            else
            {
                SetHighlightEditor(null);
                UpdateFindCount();
            }
        }

        /// <summary>검색어·대소문자·문서 버전이 그대로면 이전 결과를 쓴다. 다시 만들면 마지막 결과 자리를 이어 받는다.</summary>
        private void EnsureMatches()
        {
            var query = _findBox?.Text ?? string.Empty;
            var matchCase = _findMatchCase?.IsChecked == true;
            if (_matchesVersion == _version && _matchesQuery == query && _matchesCase == matchCase)
                return;
            _matches = BlockFind.FindAll(_blocks, query, matchCase);
            _matchesVersion = _version;
            _matchesQuery = query;
            _matchesCase = matchCase;
            _currentMatch = -1;
            if (!_findCursorInclusive && _findCursorBlock >= 0)
            {
                for (var i = 0; i < _matches.Count; i++)
                {
                    if (_matches[i].Block == _findCursorBlock && _matches[i].Start == _findCursorOffset)
                    {
                        _currentMatch = i;
                        break;
                    }
                }
            }
        }

        /// <summary>다음(<paramref name="forward"/>)·이전 결과로. 검색어가 없으면 찾기 막대를 연다.</summary>
        public void FindNext(bool forward)
        {
            if (_findBar == null)
                return;
            if (string.IsNullOrEmpty(_findBox.Text))
            {
                OpenFind(false);
                return;
            }
            if (!IsFindOpen)
            {
                _findBar.Visibility = Visibility.Visible;
                EnsureFindRefreshTimer().Start();
            }
            EnsureMatches();
            if (_matches.Count == 0)
            {
                UpdateFindCount();
                return;
            }

            // 입력란에 커서가 있으면 그 자리에서, 아니면 마지막으로 보여 준 결과에서 이어 찾는다.
            int block, offset;
            var inclusive = false;
            var editor = Keyboard.FocusedElement as IBlockInput;
            var row = editor?.Tag as BlockRow;
            var rowIndex = IndexOf(row);
            if (rowIndex >= 0)
            {
                block = rowIndex;
                offset = ToBlockOffset(row, editor, editor.SelectionStart);
                inclusive = forward && editor.SelectionLength == 0;
            }
            else if (_currentMatch >= 0)
            {
                block = _matches[_currentMatch].Block;
                offset = _matches[_currentMatch].Start;
            }
            else if (_findCursorBlock >= 0)
            {
                block = _findCursorBlock;
                offset = _findCursorOffset;
                inclusive = _findCursorInclusive;
            }
            else
            {
                block = forward ? -1 : _blocks.Count;
                offset = 0;
            }
            ShowMatch(BlockFind.NextMatch(_matches, block, offset, forward, inclusive), rowIndex >= 0);
        }

        /// <summary>결과로 스크롤하고 그 행의 입력란에서 결과 글자를 선택해 강조한다. <paramref name="focusEditor"/>면 커서도 그리로 옮긴다.</summary>
        private void ShowMatch(int matchIndex, bool focusEditor)
        {
            if (matchIndex < 0 || matchIndex >= _matches.Count)
                return;
            var match = _matches[matchIndex];
            _currentMatch = matchIndex;
            _findCursorBlock = match.Block;
            _findCursorOffset = match.Start;
            _findCursorInclusive = false;

            RevealBlock(match.Block);
            var row = _panel.EnsureRealized(match.Block);
            if (row == null)
            {
                UpdateFindCount();
                return;
            }
            var start = match.Start;
            var column = 0;
            if (row.Block.Kind == BlockKind.TableRow)
                BlockFind.CellOffset(row.Block.Text, match.Start, out column, out start);
            var editor = row.EditorForColumn(column);
            if (editor == null)
            {
                UpdateFindCount();
                return;
            }
            if (!editor.IsVisible)
                UpdateLayout();

            SetHighlightEditor(editor);
            if (editor is RichBlockBox)
            {
                // 서식 블록: 찾기는 본문(Markdown)에서 하므로 표식을 뺀 글자 위치로 바꾼다
                var end = SourceToEditor(row, match.Start + match.Length);
                start = SourceToEditor(row, match.Start);
                match = new BlockMatch(match.Block, start, Math.Max(0, end - start));
            }
            var length = Math.Min(match.Length, Math.Max(0, editor.Text.Length - start));
            if (focusEditor || !IsFindOpen)
                editor.Focus();
            editor.Select(Math.Min(start, editor.Text.Length), length);
            ScheduleCaretIntoView(editor);
            UpdateFindCount();
        }

        /// <summary>찾기 입력란에 포커스가 있어도 결과 선택이 보이게 한다. 이전 입력란은 원래대로.</summary>
        private void SetHighlightEditor(IBlockInput editor)
        {
            if (ReferenceEquals(_findHighlightEditor, editor))
                return;
            if (_findHighlightEditor != null)
                _findHighlightEditor.IsInactiveSelectionHighlightEnabled = false;
            _findHighlightEditor = editor;
            if (editor != null)
                editor.IsInactiveSelectionHighlightEnabled = true;
        }

        private void UpdateFindCount()
        {
            if (_findCount == null)
                return;
            EnsureMatches();
            if (string.IsNullOrEmpty(_findBox.Text))
                _findCount.Text = string.Empty;
            else if (_matches.Count == 0)
                _findCount.Text = "결과 없음";
            else if (_currentMatch >= 0)
                _findCount.Text = string.Format("{0}/{1}", _currentMatch + 1, _matches.Count);
            else
                _findCount.Text = string.Format("{0}개", _matches.Count);
        }

        /// <summary>입력란의 글자 위치를 블록 본문 위치로. 표 행은 포커스를 받은 셀(<see cref="BlockRow.ActiveColumn"/>) 기준으로 탭 구분 위치로 바꾼다.</summary>
        private static int ToBlockOffset(BlockRow row, IBlockInput editor, int offset)
        {
            if (row.Block.Kind != BlockKind.TableRow)
                return editor is RichBlockBox ? EditorToSource(row, offset) : offset;
            return BlockFind.TextOffset(row.Block.Text, row.ActiveColumn, offset);
        }

        /// <summary>접힌 토글 안 블록이면 감싼 토글을 편다(보기 상태라 저장할 변경으로 세지 않음 — <see cref="ToggleCollapse"/>와 같음).</summary>
        private void RevealBlock(int index)
        {
            if (index < 0 || index >= _blocks.Count || IsVisibleIndex(index))
                return;
            var indent = _blocks[index].Indent;
            var changed = false;
            for (var i = index - 1; i >= 0 && indent > 0; i--)
            {
                var block = _blocks[i];
                if (block.Indent >= indent)
                    continue;
                if (block.Kind == BlockKind.Toggle && block.Collapsed)
                {
                    block.Collapsed = false;
                    changed = true;
                }
                indent = block.Indent;
            }
            if (changed)
                RefreshStructure();
        }

        // ── 바꾸기 ─────────────────────────────────────────────────────────

        /// <summary>지금 결과를 바꾸고 다음 결과로. 지금 결과가 없으면(또는 문서가 바뀌어 그 자리에 없으면) 다음 결과를 보여 주기만 한다.</summary>
        public void ReplaceCurrent()
        {
            if (_findBar == null || string.IsNullOrEmpty(_findBox.Text))
                return;
            var query = _findBox.Text;
            var matchCase = _findMatchCase.IsChecked == true;
            EnsureMatches();
            if (_currentMatch < 0 || _currentMatch >= _matches.Count || !BlockFind.IsMatchAt(_blocks, _matches[_currentMatch], query, matchCase))
            {
                FindNext(true);
                return;
            }

            var match = _matches[_currentMatch];
            var end = BlockFind.ReplaceOne(_blocks, match, query, _replaceBox.Text, matchCase);
            if (end < 0)
                return;
            _typingRow = null;
            SyncRows();
            RecordChange();
            _findCursorBlock = match.Block;
            _findCursorOffset = end;
            _findCursorInclusive = true;
            _currentMatch = -1;

            EnsureMatches();
            if (_matches.Count == 0)
            {
                SetHighlightEditor(null);
                UpdateFindCount();
                return;
            }
            ShowMatch(BlockFind.NextMatch(_matches, match.Block, end, true, true), false);
        }

        /// <summary>모든 결과를 바꾼다. 실행 취소 한 단계(바뀐 블록 구간만 기록).</summary>
        public void ReplaceAllMatches()
        {
            if (_findBar == null || string.IsNullOrEmpty(_findBox.Text))
                return;
            int first, last;
            var count = BlockFind.ReplaceAll(_blocks, _findBox.Text, _replaceBox.Text, _findMatchCase.IsChecked == true, out first, out last);
            if (count == 0)
            {
                UpdateFindCount();
                return;
            }
            _typingRow = null;
            SyncRows();
            RecordChange();
            SetHighlightEditor(null);
            _currentMatch = -1;
            _findCursorBlock = first;
            _findCursorOffset = 0;
            _findCursorInclusive = true;
            EnsureMatches();
            _findCount.Text = string.Format("{0}개 바꿈", count);
        }

        // ── 제목 앵커·스크롤 앵커 ─────────────────────────────────────────

        /// <summary><c>#제목</c> 앵커(GitHub 규칙, <see cref="BlockFind.Slugify"/>)의 제목 블록으로 옮겨 맨 위에 보이고 커서를 둔다. 제목이 없으면 false.</summary>
        public bool ScrollToHeading(string fragment)
        {
            var index = BlockFind.FindHeading(_blocks, fragment);
            if (index < 0)
                return false;
            RevealBlock(index);
            _panel.ScrollToAnchor(index, -8);
            UpdateLayout();
            FocusRow(index, 0);
            return true;
        }

        /// <summary>화면 맨 위 블록(복사본)과 그 위치·블록 안 거리. 외부 변경으로 다시 읽기 전에 부르고 <see cref="RestoreScrollAnchor"/>에 넘긴다.</summary>
        public int CaptureScrollAnchor(out EditorBlock anchor, out double delta)
        {
            var index = _panel.GetTopAnchor(out delta);
            anchor = index >= 0 && index < _blocks.Count ? _blocks[index].Clone() : null;
            return index;
        }

        /// <summary>다시 읽은 문서에서 같은 블록(종류·본문이 같은 가장 가까운 블록)을 찾아 보던 위치로 돌아간다.</summary>
        public void RestoreScrollAnchor(EditorBlock anchor, int oldIndex, double delta)
        {
            if (oldIndex < 0)
                return;
            var index = BlockFind.MapAnchor(anchor, oldIndex, _blocks);
            if (index < 0)
                return;
            var found = anchor != null && _blocks[index].Kind == anchor.Kind && _blocks[index].Text == anchor.Text;
            RevealBlock(index);
            _panel.ScrollToAnchor(index, found ? delta : 0);
        }

        // ── 링크 Ctrl+클릭 ─────────────────────────────────────────────────

        private void OnLinkPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.Handled || Keyboard.Modifiers != ModifierKeys.Control)
                return;
            var source = e.OriginalSource as DependencyObject;
            if (source == null || (_findBar != null && ElementTree.IsWithin(source, _findBar)))
                return;

            string link = null;
            var rich = ElementTree.FindAncestor<RichBlockBox>(source);
            var box = rich == null ? ElementTree.FindAncestor<TextBox>(source) : null;
            if (rich != null)
            {
                if (!(rich.Tag is BlockRow))
                    return;
                link = RichLinkAt(rich, e.GetPosition(rich));
            }
            else if (box != null)
            {
                if (!(box.Tag is BlockRow))
                    return;
                var at = box.GetCharacterIndexFromPoint(e.GetPosition(box), true);
                if (at >= 0)
                    link = BlockFind.LinkAt(box.Text, at);
            }
            else
            {
                var text = ElementTree.FindAncestor<TextBlock>(source);
                if (text != null && ElementTree.FindAncestor<BlockEditor>(text) == this)
                    link = BlockInlineRenderer.DisplayLinkAt(source);
                if (link == null && text != null && ElementTree.FindAncestor<BlockEditor>(text) == this)
                {
                    var position = text.GetPositionFromPoint(e.GetPosition(text), true);
                    if (position != null)
                        link = BlockFind.LinkAt(new TextRange(text.ContentStart, text.ContentEnd).Text,
                            new TextRange(text.ContentStart, position).Text.Length);
                }
            }
            if (string.IsNullOrWhiteSpace(link))
                return;
            e.Handled = true;
            LinkOpenRequested?.Invoke(this, link);
        }
    }
}

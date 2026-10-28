using Folderss.Models;
using Folderss.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Folderss.Controls
{
    /// <summary>
    /// Notion식 블록 편집기. 문서는 <see cref="EditorBlock"/>의 평면 목록이다.
    /// <list type="bullet">
    /// <item>입력: Enter 블록 나누기, Backspace(맨 앞) 텍스트로 전환·내어쓰기·앞 블록에 합치기, Tab/Shift+Tab 들여쓰기, ↑↓←→ 블록 사이 이동.</item>
    /// <item>문단 맨 앞 Markdown 표식(<c># </c>, <c>- </c>, <c>1. </c>, <c>[] </c>, <c>&gt; </c>, <c>```</c>, <c>---</c>)은 블록 종류로 바뀐다(<see cref="BlockMarkdown.TryMatchShortcut"/>).</item>
    /// <item><c>/</c> 명령 메뉴, 왼쪽 <c>+</c>·<c>⋮⋮</c> 손잡이(끌어 옮기기·블록 메뉴), Esc 블록 선택(Shift+↑↓ 확장, Del 삭제, Ctrl+C Markdown 복사).</item>
    /// <item>Ctrl+Shift+↑↓ 블록 이동, Ctrl+D 복제, Ctrl+Alt+0~8 블록 전환, Ctrl+Enter 할 일 체크·토글 접기, Ctrl+B/I/E/K 줄 안 서식(Markdown 표식으로 감쌈).</item>
    /// <item>표는 행마다 블록(셀 입력란, Tab·Enter로 셀 이동·행 추가, 엑셀 붙여넣기), 이미지는 백그라운드에서 화면 폭에 맞춰 디코딩한다.</item>
    /// </list>
    /// 큰 문서용 구조:
    /// <list type="bullet">
    /// <item>가상화(<see cref="BlockPanel"/>): 화면에 보이는 블록(앞뒤 여유분 포함)만 행(TextBox)을 만들고 나머지는 높이만 추정·기억한다.
    /// 스크롤 위치는 블록 기준(앵커)이라 추정 높이가 실제 높이로 바뀌어도 보던 곳이 흔들리지 않는다.</item>
    /// <item>실행 취소는 바뀐 구간만 기록한다(<see cref="HistoryEntry"/>). 입력 한 번에 문서 전체를 복사하지 않는다.</item>
    /// </list>
    /// 줄 안 서식은 WYSIWYG가 아니라 Markdown 원문으로 보인다. 색은 테마 리소스(DynamicResource)를 따른다.
    /// </summary>
    public sealed partial class BlockEditor : UserControl
    {
        private const double GutterWidth = 52;
        private const double IndentWidth = 26;
        private const double ReadableWidth = 860;
        private const double TableAddColumnWidth = 22;
        private const double TableAddRowHeight = 20;
        private const int UndoLimit = 500;
        private static readonly TimeSpan TypingMergeWindow = TimeSpan.FromMilliseconds(1500);
        private static readonly FontFamily CodeFont = new FontFamily("Cascadia Mono, Consolas, D2Coding, Malgun Gothic");

        private readonly List<EditorBlock> _blocks = new List<EditorBlock>();
        private readonly BlockPanel _panel;
        private readonly ScrollViewer _scroll;
        private readonly Popup _slashPopup;
        private readonly StackPanel _slashItems = new StackPanel();

        // 구조 정보. 블록 목록·종류·접힘이 바뀌면 RefreshStructure가 O(블록 수)로 다시 계산한다(할당 최소).
        private bool[] _visible = new bool[0];
        private int[] _numbers = new int[0];
        private int[] _tableColumns = new int[0];
        private bool[] _tableHeader = new bool[0];
        private int[] _tableStart = new int[0];
        private bool[] _tableLast = new bool[0];
        private readonly Dictionary<long, int> _indexById = new Dictionary<long, int>();

        // 실행 취소: 마지막 기록 시점 문서의 동결 사본(_committed)과 바뀐 구간 기록
        private readonly List<HistoryEntry> _undo = new List<HistoryEntry>();
        private readonly List<HistoryEntry> _redo = new List<HistoryEntry>();
        private List<EditorBlock> _committed = new List<EditorBlock>();
        private int _committedFocus = -1;
        private int _committedCaret;
        private int _committedColumn;
        private long _versionCounter;
        private long _version;
        private long _savedVersion;
        private bool _lastRaisedModified;
        private BlockRow _typingRow;
        private DateTime _typingAt;

        private bool _suppressTextChanged;
        private BlockRow _focusedRow;
        private TextBox _caretScrollTarget;

        private int _selAnchor = -1;
        private int _selActive = -1;

        private BlockRow _slashRow;
        private int _slashStart;
        private List<SlashCommand> _slashMatches = new List<SlashCommand>();
        private int _slashIndex;

        private BlockRow _dragRow;
        private Point _dragStart;
        private bool _dragging;
        private int _dropIndex = -1;

        /// <summary>저장하지 않은 변경 여부가 바뀌었다.</summary>
        public event EventHandler ModifiedChanged;

        /// <summary>Ctrl+S를 눌렀다. 저장은 소유자(뷰어)가 한다.</summary>
        public event EventHandler SaveRequested;

        public BlockEditor()
        {
            Focusable = true;
            FocusVisualStyle = null;
            SetResourceReference(BackgroundProperty, "PanelBackground");

            _panel = new BlockPanel(this);

            // 세로 스크롤바는 항상 표시한다. Auto면 다시 측정될 때마다 스크롤바 없는 폭·있는 폭으로 두 번 측정해
            // 보이는 행 전체가 줄 배치를 다시 계산한다. 스크롤은 BlockPanel(IScrollInfo)이 블록 단위로 직접 처리한다.
            _scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                CanContentScroll = true,
                Focusable = false,
                Content = _panel,
            };
            _scroll.ScrollChanged += (s, e) =>
            {
                if (e.VerticalChange != 0)
                    CloseSlash();
            };
            Content = _scroll;

            _slashPopup = CreateSlashPopup();
            IsVisibleChanged += (s, e) =>
            {
                if (!IsVisible)
                    CloseSlash();
            };

            SetMarkdown(string.Empty);
        }

        // ── 공개 API ───────────────────────────────────────────────────────

        public bool IsModified => _version != _savedVersion;

        /// <summary>본문 폭을 읽기 좋은 너비로 제한하지 않고 창 너비 전체를 쓴다(Notion의 전체 너비).</summary>
        public bool FullWidth
        {
            get { return _panel.FullWidth; }
            set
            {
                _panel.FullWidth = value;
                _panel.InvalidateMeasure();
            }
        }

        /// <summary>이미지 블록의 상대 경로 기준 폴더(문서 파일이 있는 폴더).</summary>
        public string BaseDirectory { get; set; }

        public int BlockCount => _blocks.Count;

        /// <summary>
        /// 문서를 Markdown에서 새로 읽는다. <paramref name="sidecarJson"/>은 Markdown 밖 속성(블록 색 등)을 담은 사이드카(<see cref="BlockSidecar"/>).
        /// 실행 취소 기록은 지우고 저장된 상태로 본다.
        /// </summary>
        public void SetMarkdown(string markdown, string sidecarJson = null)
        {
            CloseSlash();
            _selAnchor = _selActive = -1;
            _focusedRow = null;
            _typingRow = null;
            _blocks.Clear();
            _blocks.AddRange(BlockMarkdown.Parse(markdown));
            UnmatchedAttributes = BlockSidecar.Apply(_blocks, sidecarJson);
            if (_blocks.Count == 0)
                _blocks.Add(new EditorBlock(BlockKind.Paragraph));

            _panel.Reset();
            RefreshStructure();

            _undo.Clear();
            _redo.Clear();
            _committed = _blocks.Select(b => b.Clone()).ToList();
            _committedFocus = -1;
            _committedCaret = _committedColumn = 0;
            _version = _savedVersion = ++_versionCounter;
            RaiseModified();
        }

        public string GetMarkdown()
        {
            return BlockMarkdown.Serialize(_blocks);
        }

        /// <summary>
        /// 함께 저장할 사이드카 JSON. Markdown 밖 속성이 하나도 없으면 null(사이드카 파일을 지우면 됨).
        /// <paramref name="savedMarkdown"/>은 같이 저장하는 <see cref="GetMarkdown"/> 결과.
        /// </summary>
        public string GetSidecar(string savedMarkdown, string fileName)
        {
            return BlockSidecar.Serialize(_blocks, savedMarkdown, fileName);
        }

        /// <summary>
        /// 마지막 <see cref="SetMarkdown"/>에서 사이드카 속성 중 블록에 다시 붙이지 못한 수(다른 도구에서 본문이 바뀐 블록).
        /// 사이드카를 읽을 수 없으면 <see cref="BlockSidecar.Unreadable"/>.
        /// </summary>
        public int UnmatchedAttributes { get; private set; }

        /// <summary>테마가 바뀌었다: 테마 밝기에 따라 고르는 색(블록 글자색·배경색)을 다시 칠한다. 나머지 색은 DynamicResource라 저절로 바뀐다.</summary>
        public void RefreshTheme()
        {
            _panel.SyncRealized();
        }

        public void MarkSaved()
        {
            _savedVersion = _version;
            // 저장 뒤 입력이 저장 전 입력과 한 단계로 묶이면 실행 취소로 저장 시점에 돌아올 수 없다.
            _typingRow = null;
            RaiseModified();
        }

        public void FocusStart()
        {
            FocusRow(NextEditable(-1, 1), 0);
        }

        public void Undo()
        {
            ApplyHistory(_undo, _redo);
        }

        public void Redo()
        {
            ApplyHistory(_redo, _undo);
        }

        // ── 실행 취소 ──────────────────────────────────────────────────────

        /// <summary>
        /// 바뀐 구간 하나. 적용하면 <see cref="_committed"/>·<see cref="_blocks"/>의 [Start, Start+AfterCount) 구간을 <see cref="Before"/>로 바꾼다.
        /// 적용할 때 반대 방향 기록을 만들어 다른 쪽 목록(실행 취소 ↔ 다시 실행)에 넣는다.
        /// </summary>
        private sealed class HistoryEntry
        {
            public int Start;
            public List<EditorBlock> Before;
            public int AfterCount;
            public long VersionBefore;
            public long VersionAfter;
            public int FocusBefore;
            public int CaretBefore;
            public int ColumnBefore;
            public int FocusAfter;
            public int CaretAfter;
            public int ColumnAfter;
        }

        /// <summary>
        /// 문서가 바뀐 뒤 부른다. <paramref name="typingRow"/>는 글자 입력(그 블록 하나만 바뀜) — 문서를 훑지 않고 그 블록만 기록하며,
        /// 직전 입력과 같은 블록이고 잠깐 사이면 한 단계로 묶는다. 그 밖의 변경은 이전 기록과 비교해 바뀐 구간만 남긴다(O(블록 수) 비교, 할당은 바뀐 블록만).
        /// </summary>
        private void RecordChange(BlockRow typingRow = null)
        {
            var focus = IndexOf(_focusedRow);
            var caret = focus >= 0 ? _focusedRow.Editor.CaretIndex : 0;
            var column = focus >= 0 ? _focusedRow.ActiveColumn : 0;
            var now = DateTime.UtcNow;

            HistoryEntry entry;
            var merged = false;
            var index = typingRow != null ? IndexOf(typingRow) : -1;
            if (index >= 0 && _committed.Count == _blocks.Count && _committed[index].Id == typingRow.Block.Id)
            {
                var before = _committed[index];
                _committed[index] = typingRow.Block.Clone();
                merged = ReferenceEquals(typingRow, _typingRow) && now - _typingAt < TypingMergeWindow && _undo.Count > 0 &&
                         _undo[_undo.Count - 1].Start == index && _undo[_undo.Count - 1].AfterCount == 1;
                entry = merged ? _undo[_undo.Count - 1] : new HistoryEntry
                {
                    Start = index,
                    Before = new List<EditorBlock> { before },
                    AfterCount = 1,
                };
            }
            else
            {
                var next = Freeze(_blocks, _committed);
                int start, beforeCount, afterCount;
                Diff(_committed, next, out start, out beforeCount, out afterCount);
                if (beforeCount == 0 && afterCount == 0)
                {
                    _committedFocus = focus;
                    _committedCaret = caret;
                    _committedColumn = column;
                    return;
                }
                entry = new HistoryEntry { Start = start, Before = _committed.GetRange(start, beforeCount), AfterCount = afterCount };
                _committed = next;
                typingRow = null;
            }

            if (!merged)
            {
                entry.VersionBefore = _version;
                entry.FocusBefore = _committedFocus;
                entry.CaretBefore = _committedCaret;
                entry.ColumnBefore = _committedColumn;
                _undo.Add(entry);
                if (_undo.Count > UndoLimit)
                    _undo.RemoveAt(0);
            }
            _version = ++_versionCounter;
            entry.VersionAfter = _version;
            entry.FocusAfter = focus;
            entry.CaretAfter = caret;
            entry.ColumnAfter = column;
            _redo.Clear();
            _typingRow = typingRow;
            _typingAt = now;
            _committedFocus = focus;
            _committedCaret = caret;
            _committedColumn = column;
            RaiseModified();
        }

        /// <summary>현재 블록의 동결 사본 목록. 내용이 같은 블록은 이전 사본을 그대로 쓴다.</summary>
        private static List<EditorBlock> Freeze(List<EditorBlock> blocks, List<EditorBlock> previous)
        {
            var byId = new Dictionary<long, EditorBlock>(previous.Count);
            foreach (var frozen in previous)
                byId[frozen.Id] = frozen;
            var result = new List<EditorBlock>(blocks.Count);
            foreach (var block in blocks)
            {
                EditorBlock frozen;
                result.Add(byId.TryGetValue(block.Id, out frozen) && frozen.SameContent(block) ? frozen : block.Clone());
            }
            return result;
        }

        /// <summary>앞뒤로 같은(같은 사본 객체) 블록을 빼고 남는 가운데 구간.</summary>
        private static void Diff(List<EditorBlock> before, List<EditorBlock> after, out int start, out int beforeCount, out int afterCount)
        {
            var min = Math.Min(before.Count, after.Count);
            start = 0;
            while (start < min && ReferenceEquals(before[start], after[start]))
                start++;
            var suffix = 0;
            while (suffix < min - start && ReferenceEquals(before[before.Count - 1 - suffix], after[after.Count - 1 - suffix]))
                suffix++;
            beforeCount = before.Count - start - suffix;
            afterCount = after.Count - start - suffix;
        }

        private void ApplyHistory(List<HistoryEntry> from, List<HistoryEntry> to)
        {
            if (from.Count == 0)
                return;
            var entry = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);

            CloseSlash();
            _selAnchor = _selActive = -1;
            var current = _committed.GetRange(entry.Start, entry.AfterCount);
            _committed.RemoveRange(entry.Start, entry.AfterCount);
            _committed.InsertRange(entry.Start, entry.Before);
            _blocks.RemoveRange(entry.Start, entry.AfterCount);
            _blocks.InsertRange(entry.Start, entry.Before.Select(b => b.Clone()));

            to.Add(new HistoryEntry
            {
                Start = entry.Start,
                Before = current,
                AfterCount = entry.Before.Count,
                VersionBefore = entry.VersionAfter,
                VersionAfter = entry.VersionBefore,
                FocusBefore = entry.FocusAfter,
                CaretBefore = entry.CaretAfter,
                ColumnBefore = entry.ColumnAfter,
                FocusAfter = entry.FocusBefore,
                CaretAfter = entry.CaretBefore,
                ColumnAfter = entry.ColumnBefore,
            });

            _version = entry.VersionBefore;
            _committedFocus = entry.FocusBefore;
            _committedCaret = entry.CaretBefore;
            _committedColumn = entry.ColumnBefore;
            _typingRow = null;
            // 되돌린 블록은 Id가 같으므로 화면에 있던 행(TextBox)을 그대로 다시 쓴다.
            SyncRows();
            FocusRow(entry.FocusBefore, entry.CaretBefore, entry.ColumnBefore);
            RaiseModified();
        }

        private void RaiseModified()
        {
            var modified = IsModified;
            if (modified == _lastRaisedModified)
                return;
            _lastRaisedModified = modified;
            ModifiedChanged?.Invoke(this, EventArgs.Empty);
        }

        // ── 구조 동기화 ────────────────────────────────────────────────────

        /// <summary><see cref="_blocks"/>를 바꾼 뒤 부른다. 구조 정보를 다시 계산하고 화면에 있는 행만 맞춘다.</summary>
        private void SyncRows()
        {
            RefreshStructure();
        }

        /// <summary>
        /// 보이기(접힌 토글의 자식 숨김)·번호·표 구조·Id → 위치 표를 다시 계산하고, 화면에 만들어진 행만 다시 묶는다.
        /// 행이 없는 블록은 화면에 들어올 때 <see cref="ApplyRowContext"/>로 같은 정보를 받는다.
        /// </summary>
        private void RefreshStructure()
        {
            _visible = BlockOperations.VisibleFlags(_blocks);
            _numbers = BlockOperations.ListNumbers(_blocks);
            BlockOperations.TableLayout(_blocks, out _tableColumns, out _tableHeader);
            if (_tableStart.Length != _blocks.Count)
                _tableStart = new int[_blocks.Count];
            for (var i = 0; i < _blocks.Count; i++)
                _tableStart[i] = _tableHeader[i] || i == 0 ? i : _tableColumns[i] > 0 ? _tableStart[i - 1] : i;
            if (_tableLast.Length != _blocks.Count)
                _tableLast = new bool[_blocks.Count];
            for (var i = 0; i < _blocks.Count; i++)
                _tableLast[i] = _tableColumns[i] > 0 && (i + 1 >= _blocks.Count || _tableColumns[i + 1] == 0 || _tableHeader[i + 1]);
            _indexById.Clear();
            for (var i = 0; i < _blocks.Count; i++)
                _indexById[_blocks[i].Id] = i;
            _panel.SyncRealized();
        }

        /// <summary>화면에 만든 행에 그 위치의 번호·들여쓰기·선택·표 정보를 준다.</summary>
        private void ApplyRowContext(BlockRow row, int index)
        {
            var block = _blocks[index];
            if (block.Kind == BlockKind.TableRow && index < _tableColumns.Length)
                row.SetTable(_tableColumns[index], _tableHeader[index], _blocks[_tableStart[index]].TableAlign, _tableLast[index]);
            row.SetLabel(BlockOperations.FormatListLabel(block, index < _numbers.Length ? _numbers[index] : 0));
            row.ApplyIndent();
            int selStart, selEnd;
            GetSelectionRange(out selStart, out selEnd);
            row.SetSelected(index >= selStart && index < selEnd);
        }

        // ── 포커스·커서 ────────────────────────────────────────────────────

        private int IndexOf(BlockRow row)
        {
            if (row == null)
                return -1;
            int index;
            if (_indexById.TryGetValue(row.Block.Id, out index) && index < _blocks.Count && ReferenceEquals(_blocks[index], row.Block))
                return index;
            return _blocks.IndexOf(row.Block);
        }

        private bool IsVisibleIndex(int index)
        {
            return index >= 0 && index < _blocks.Count && (index >= _visible.Length || _visible[index]);
        }

        private bool IsEditable(int index)
        {
            return IsVisibleIndex(index) && _blocks[index].Kind != BlockKind.Divider;
        }

        private int NextEditable(int from, int direction)
        {
            for (var i = from + direction; i >= 0 && i < _blocks.Count; i += direction)
                if (IsEditable(i))
                    return i;
            return -1;
        }

        private int NextVisible(int from, int direction)
        {
            for (var i = from + direction; i >= 0 && i < _blocks.Count; i += direction)
                if (IsVisibleIndex(i))
                    return i;
            return -1;
        }

        /// <summary>
        /// 블록에 커서를 둔다. 글자 수보다 큰 위치는 끝, 표 행은 <paramref name="column"/> 셀. 화면 밖이면 스크롤해 행을 만든다.
        /// 구분선처럼 입력란이 없으면 블록을 선택한다.
        /// </summary>
        private void FocusRow(int index, int caret, int column = 0)
        {
            if (index < 0 || index >= _blocks.Count)
                return;
            if (!IsEditable(index))
            {
                if (IsVisibleIndex(index))
                    SelectBlocks(index, index);
                return;
            }

            var row = _panel.EnsureRealized(index);
            if (row == null)
                return;
            FocusEditor(row.EditorForColumn(column), caret);
        }

        private void FocusEditor(TextBox editor, int caret)
        {
            if (!editor.IsVisible)
                UpdateLayout();
            editor.Focus();
            editor.SelectionLength = 0;
            editor.CaretIndex = Math.Max(0, Math.Min(caret, editor.Text.Length));
            ScheduleCaretIntoView(editor);
        }

        private void ScheduleCaretIntoView(TextBox editor)
        {
            var pending = _caretScrollTarget != null;
            _caretScrollTarget = editor;
            if (pending)
                return;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                editor = _caretScrollTarget;
                _caretScrollTarget = null;
                if (editor == null || !editor.IsVisible)
                    return;
                var rect = editor.GetRectFromCharacterIndex(Math.Min(editor.CaretIndex, editor.Text.Length));
                if (rect.IsEmpty)
                    editor.BringIntoView();
                else
                    editor.BringIntoView(new Rect(0, rect.Top - 12, 1, rect.Height + 24));
            }));
        }

        private static bool IsOnFirstLine(TextBox editor)
        {
            return editor.GetLineIndexFromCharacterIndex(editor.CaretIndex) <= 0;
        }

        private static bool IsOnLastLine(TextBox editor)
        {
            var line = editor.GetLineIndexFromCharacterIndex(editor.CaretIndex);
            return line < 0 || line >= editor.LineCount - 1;
        }

        /// <summary>위·아래 블록으로 커서를 옮기며 가로 위치를 유지한다(표 행이면 그 위치의 셀).</summary>
        private void FocusVertical(BlockRow row, int direction)
        {
            var target = NextEditable(IndexOf(row), direction);
            var source = row.Editor;
            if (target < 0)
            {
                // 첫·마지막 블록에서는 그 블록의 처음·끝으로
                source.CaretIndex = direction < 0 ? 0 : source.Text.Length;
                return;
            }

            double xInSource = 0;
            try
            {
                var sourceRect = source.GetRectFromCharacterIndex(source.CaretIndex);
                xInSource = sourceRect.IsEmpty ? 0 : sourceRect.X;
            }
            catch (ArgumentOutOfRangeException)
            {
            }
            var sourceInPanel = source.TranslatePoint(new Point(xInSource, 0), _panel).X;

            var targetRow = _panel.EnsureRealized(target);
            if (targetRow == null)
                return;
            var destination = targetRow.EditorAtPanelX(sourceInPanel, _panel);
            var caret = direction < 0 ? destination.Text.Length : 0;
            try
            {
                var x = _panel.TranslatePoint(new Point(sourceInPanel, 0), destination).X;
                var line = direction < 0 ? destination.LineCount - 1 : 0;
                if (line >= 0)
                {
                    var lineStart = destination.GetCharacterIndexFromLineIndex(line);
                    var lineText = destination.GetLineText(line) ?? string.Empty;
                    var lineEnd = lineStart + lineText.TrimEnd('\r', '\n').Length;
                    var lineRect = destination.GetRectFromCharacterIndex(lineStart);
                    var index = destination.GetCharacterIndexFromPoint(new Point(Math.Max(0, x), lineRect.Top + lineRect.Height / 2), true);
                    if (index >= 0)
                    {
                        var leading = destination.GetRectFromCharacterIndex(index).X;
                        var trailing = destination.GetRectFromCharacterIndex(index, true).X;
                        if (x > (leading + trailing) / 2)
                            index++;
                        caret = Math.Max(lineStart, Math.Min(index, lineEnd));
                    }
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                // 레이아웃 전이면 줄 정보를 못 얻는다. 처음·끝으로 간다.
            }
            FocusEditor(destination, caret);
        }

        // ── 키 입력 ────────────────────────────────────────────────────────

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Handled)
                return;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var mods = Keyboard.Modifiers;

            if (mods == ModifierKeys.Control && key == Key.S)
            {
                SaveRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }
            if (mods == ModifierKeys.Control && key == Key.Z)
            {
                Undo();
                e.Handled = true;
                return;
            }
            if ((mods == ModifierKeys.Control && key == Key.Y) || (mods == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.Z))
            {
                Redo();
                e.Handled = true;
                return;
            }

            if (HasBlockSelection && !(Keyboard.FocusedElement is TextBox))
            {
                HandleSelectionKey(e, key, mods);
                return;
            }

            var editor = Keyboard.FocusedElement as TextBox;
            var row = editor?.Tag as BlockRow;
            if (row == null || !ReferenceEquals(row.Editor, editor))
                return;

            // 한글 조합 중 Enter는 IME가 먼저 받는다(Key.ImeProcessed). 조합한 글자가 들어간 뒤에 블록을 나눈다.
            if (e.Key == Key.ImeProcessed && e.ImeProcessedKey == Key.Enter && mods == ModifierKeys.None &&
                !EditorBlock.IsVerbatim(row.Block.Kind))
            {
                e.Handled = true;
                var slashOpen = _slashRow == row && _slashPopup.IsOpen;
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    if (IndexOf(row) < 0)
                        return;
                    if (slashOpen)
                    {
                        UpdateSlashQuery();
                        if (_slashRow == row)
                            ExecuteSlash(_slashIndex);
                    }
                    else if (row.Block.Kind == BlockKind.TableRow)
                    {
                        InsertTableRow(IndexOf(row) + 1, row.ActiveColumn);
                    }
                    else
                    {
                        SplitBlock(row);
                    }
                }));
                return;
            }

            if (_slashRow == row && _slashPopup.IsOpen && HandleSlashKey(key, mods))
            {
                e.Handled = true;
                return;
            }

            if (row.Block.Kind == BlockKind.TableRow && HandleTableKey(row, key, mods))
            {
                e.Handled = true;
                return;
            }

            if (HandleEditorKey(row, key, mods))
                e.Handled = true;
        }

        private bool HandleEditorKey(BlockRow row, Key key, ModifierKeys mods)
        {
            var editor = row.Editor;
            var block = row.Block;
            var verbatim = EditorBlock.IsVerbatim(block.Kind);
            var ctrl = ModifierKeys.Control;
            var ctrlShift = ModifierKeys.Control | ModifierKeys.Shift;
            var ctrlAlt = ModifierKeys.Control | ModifierKeys.Alt;

            switch (key)
            {
                case Key.Enter:
                    if (mods == ModifierKeys.None && !verbatim)
                    {
                        SplitBlock(row);
                        return true;
                    }
                    if (mods == ctrl)
                    {
                        ToggleState(row);
                        return true;
                    }
                    // 제목은 한 줄
                    return mods == ModifierKeys.Shift && IsHeading(block.Kind);

                case Key.Back:
                    if (mods == ModifierKeys.None && editor.SelectionLength == 0 && editor.CaretIndex == 0)
                    {
                        BackspaceAtStart(row);
                        return true;
                    }
                    return false;

                case Key.Delete:
                    if (mods == ModifierKeys.None && editor.SelectionLength == 0 && editor.CaretIndex == editor.Text.Length)
                    {
                        MergeNext(row);
                        return true;
                    }
                    return false;

                case Key.Up:
                case Key.Down:
                    var direction = key == Key.Up ? -1 : 1;
                    if (mods == ctrlShift)
                    {
                        MoveBlocks(IndexOf(row), IndexOf(row), direction);
                        return true;
                    }
                    if (mods == ModifierKeys.None && (direction < 0 ? IsOnFirstLine(editor) : IsOnLastLine(editor)))
                    {
                        FocusVertical(row, direction);
                        return true;
                    }
                    if (mods == ModifierKeys.Shift && (direction < 0
                            ? editor.SelectionStart == 0 && IsOnFirstLine(editor)
                            : editor.SelectionStart + editor.SelectionLength == editor.Text.Length && IsOnLastLine(editor)))
                    {
                        var index = IndexOf(row);
                        var next = NextVisible(index, direction);
                        SelectBlocks(index, next < 0 ? index : next);
                        return true;
                    }
                    return false;

                case Key.Left:
                    if (mods == ModifierKeys.None && editor.SelectionLength == 0 && editor.CaretIndex == 0)
                    {
                        var previous = NextEditable(IndexOf(row), -1);
                        if (previous >= 0)
                            FocusRow(previous, int.MaxValue);
                        return true;
                    }
                    return false;

                case Key.Right:
                    if (mods == ModifierKeys.None && editor.SelectionLength == 0 && editor.CaretIndex == editor.Text.Length)
                    {
                        var next = NextEditable(IndexOf(row), 1);
                        if (next >= 0)
                            FocusRow(next, 0);
                        return true;
                    }
                    return false;

                case Key.Tab:
                    if (verbatim && mods == ModifierKeys.None)
                        return false;
                    if (mods == ModifierKeys.None || mods == ModifierKeys.Shift)
                    {
                        var index = IndexOf(row);
                        IndentBlocks(index, index, mods == ModifierKeys.Shift ? -1 : 1);
                        return true;
                    }
                    return false;

                case Key.Escape:
                    if (mods == ModifierKeys.None)
                    {
                        var index = IndexOf(row);
                        SelectBlocks(index, index);
                        return true;
                    }
                    return false;

                case Key.A:
                    if (mods == ctrl && editor.SelectionLength == editor.Text.Length)
                    {
                        SelectBlocks(0, _blocks.Count - 1);
                        return true;
                    }
                    return false;

                case Key.D:
                    if (mods == ctrl)
                    {
                        var index = IndexOf(row);
                        DuplicateBlocks(index, index);
                        return true;
                    }
                    return false;

                case Key.B:
                    return mods == ctrl && !verbatim && WrapSelection(row, "**");
                case Key.I:
                    return mods == ctrl && !verbatim && WrapSelection(row, "*");
                case Key.E:
                    return mods == ctrl && !verbatim && WrapSelection(row, "`");
                case Key.S:
                    return mods == ctrlShift && !verbatim && WrapSelection(row, "~~");
                case Key.K:
                    return mods == ctrl && !verbatim && InsertLink(row);
            }

            BlockKind kind;
            if (mods == ctrlAlt && TryGetTurnIntoKind(key, out kind))
            {
                var index = IndexOf(row);
                TurnInto(index, index, kind);
                return true;
            }
            return false;
        }

        /// <summary>Notion과 같은 Ctrl+Alt+숫자 블록 전환: 0 텍스트, 1~3 제목, 4 할 일, 5 글머리, 6 번호, 7 토글, 8 코드.</summary>
        private static bool TryGetTurnIntoKind(Key key, out BlockKind kind)
        {
            kind = BlockKind.Paragraph;
            int digit;
            if (key >= Key.D0 && key <= Key.D9)
                digit = key - Key.D0;
            else if (key >= Key.NumPad0 && key <= Key.NumPad9)
                digit = key - Key.NumPad0;
            else
                return false;

            switch (digit)
            {
                case 0: kind = BlockKind.Paragraph; return true;
                case 1: kind = BlockKind.Heading1; return true;
                case 2: kind = BlockKind.Heading2; return true;
                case 3: kind = BlockKind.Heading3; return true;
                case 4: kind = BlockKind.Todo; return true;
                case 5: kind = BlockKind.Bulleted; return true;
                case 6: kind = BlockKind.Numbered; return true;
                case 7: kind = BlockKind.Toggle; return true;
                case 8: kind = BlockKind.Code; return true;
                default: return false;
            }
        }

        private static bool IsHeading(BlockKind kind)
        {
            return kind == BlockKind.Heading1 || kind == BlockKind.Heading2 || kind == BlockKind.Heading3;
        }

        // ── 블록 편집 연산 ─────────────────────────────────────────────────

        /// <summary>
        /// Enter. 빈 목록·인용·토글은 텍스트로(들여쓴 목록은 먼저 내어쓰기), 맨 앞이면 위에 빈 블록, 아니면 커서 뒤 글자를 새 블록으로.
        /// 목록은 같은 종류가 이어지고, 펼친 토글·자식 있는 블록에서는 새 블록이 첫 자식이 된다.
        /// </summary>
        private void SplitBlock(BlockRow row)
        {
            var index = IndexOf(row);
            var block = row.Block;
            var editor = row.Editor;
            var selectionStart = editor.SelectionStart;
            var text = editor.Text.Remove(selectionStart, editor.SelectionLength);

            if (text.Length == 0 && block.Kind != BlockKind.Paragraph && !IsHeading(block.Kind))
            {
                if (EditorBlock.IsListItem(block.Kind) && block.Indent > 0)
                    IndentBlocks(index, index, -1);
                else
                    TurnInto(index, index, BlockKind.Paragraph);
                return;
            }

            var continues = EditorBlock.IsListItem(block.Kind) ? block.Kind : BlockKind.Paragraph;
            if (selectionStart == 0 && text.Length > 0)
            {
                block.Text = text;
                _blocks.Insert(index, new EditorBlock(continues, string.Empty, block.Indent));
                SyncRows();
                FocusRow(index + 1, 0);
                RecordChange();
                return;
            }

            block.Text = text.Substring(0, selectionStart);
            var tail = text.Substring(selectionStart);
            var subtreeEnd = BlockOperations.SubtreeEnd(_blocks, index);
            EditorBlock created;
            int insertAt;
            if (block.Kind == BlockKind.Toggle && block.Collapsed)
            {
                created = new EditorBlock(BlockKind.Toggle, tail, block.Indent);
                insertAt = subtreeEnd;
            }
            else if (block.Kind == BlockKind.Toggle || subtreeEnd > index + 1)
            {
                var firstChild = subtreeEnd > index + 1 ? _blocks[index + 1] : null;
                var kind = firstChild != null && EditorBlock.IsListItem(firstChild.Kind) ? firstChild.Kind : BlockKind.Paragraph;
                created = new EditorBlock(kind, tail, block.Indent + 1);
                insertAt = index + 1;
            }
            else
            {
                created = new EditorBlock(continues, tail, block.Indent);
                insertAt = index + 1;
            }

            _blocks.Insert(insertAt, created);
            BlockOperations.NormalizeIndents(_blocks);
            SyncRows();
            FocusRow(insertAt, 0);
            RecordChange();
        }

        /// <summary>
        /// 맨 앞에서 Backspace: 텍스트가 아닌 블록은 텍스트로(코드·원문은 비었을 때만), 들여쓴 텍스트는 내어쓰기,
        /// 앞이 구분선이면 구분선 삭제, 아니면 앞 블록 끝에 합친다.
        /// </summary>
        private void BackspaceAtStart(BlockRow row)
        {
            var index = IndexOf(row);
            var block = row.Block;

            if (block.Kind == BlockKind.Image)
            {
                // 이미지는 텍스트로 바꾸면 경로를 잃으므로 블록을 선택한다(한 번 더 누르면 삭제).
                SelectBlocks(index, index);
                return;
            }
            if (block.Kind != BlockKind.Paragraph)
            {
                if (!EditorBlock.IsVerbatim(block.Kind) || block.Text.Length == 0)
                    TurnInto(index, index, BlockKind.Paragraph);
                return;
            }
            if (block.Indent > 0)
            {
                IndentBlocks(index, index, -1);
                return;
            }
            if (index > 0 && _blocks[index - 1].Kind == BlockKind.Divider)
            {
                _blocks.RemoveAt(index - 1);
                SyncRows();
                FocusRow(index - 1, 0);
                RecordChange();
                return;
            }

            var previous = NextEditable(index, -1);
            if (previous < 0)
                return;
            var target = _blocks[previous];
            if (target.Kind == BlockKind.TableRow || target.Kind == BlockKind.Image)
            {
                // 표·이미지에는 글자를 합치지 않는다. 빈 문단이면 지우고 앞 블록 끝으로 간다.
                if (block.Text.Length == 0)
                {
                    _blocks.RemoveAt(index);
                    SyncRows();
                    FocusRow(previous, int.MaxValue, int.MaxValue);
                    RecordChange();
                }
                else
                {
                    FocusRow(previous, int.MaxValue, int.MaxValue);
                }
                return;
            }
            var caret = target.Text.Length;
            target.Text += block.Text;
            _blocks.RemoveAt(index);
            BlockOperations.NormalizeIndents(_blocks);
            SyncRows();
            FocusRow(previous, caret);
            RecordChange();
        }

        /// <summary>끝에서 Delete: 다음 블록(구분선이면 구분선만 지움)을 이 블록 끝에 합친다.</summary>
        private void MergeNext(BlockRow row)
        {
            var index = IndexOf(row);
            var caret = row.Block.Text.Length;
            if (index + 1 < _blocks.Count && _blocks[index + 1].Kind == BlockKind.Divider)
            {
                _blocks.RemoveAt(index + 1);
            }
            else
            {
                var next = NextEditable(index, 1);
                if (next < 0 || _blocks[next].Kind == BlockKind.TableRow || _blocks[next].Kind == BlockKind.Image)
                    return;
                row.Block.Text += _blocks[next].Text;
                _blocks.RemoveAt(next);
            }
            BlockOperations.NormalizeIndents(_blocks);
            SyncRows();
            FocusRow(index, caret);
            RecordChange();
        }

        /// <summary>[first, last] 블록(자식 포함)을 한 단계 들여쓰기(+1)·내어쓰기(-1).</summary>
        private void IndentBlocks(int first, int last, int delta)
        {
            if (first < 0)
                return;
            var end = BlockOperations.ExtendToSubtrees(_blocks, first, last + 1);
            if (delta > 0 && !BlockOperations.CanIndent(_blocks, first))
                return;
            if (delta < 0 && Enumerable.Range(first, end - first).Any(i => _blocks[i].Indent == 0))
                return;

            var caret = _focusedRow != null ? _focusedRow.Editor.CaretIndex : 0;
            var focusIndex = IndexOf(_focusedRow);
            for (var i = first; i < end; i++)
                _blocks[i].Indent += delta;
            BlockOperations.NormalizeIndents(_blocks);
            SyncRows();
            if (HasBlockSelection)
                UpdateSelectionVisuals();
            else
                FocusRow(focusIndex, caret);
            RecordChange();
        }

        /// <summary>[first, last] 블록(자식 포함)을 형제 하나만큼 위·아래로 옮긴다.</summary>
        private void MoveBlocks(int first, int last, int direction)
        {
            if (first < 0)
                return;
            var end = BlockOperations.ExtendToSubtrees(_blocks, first, last + 1);
            var caret = _focusedRow != null ? _focusedRow.Editor.CaretIndex : 0;
            var focusBlock = HasBlockSelection ? null : _focusedRow?.Block;
            var newStart = BlockOperations.MoveRange(_blocks, first, end, direction);
            if (newStart < 0)
                return;
            BlockOperations.NormalizeIndents(_blocks);
            if (HasBlockSelection)
            {
                var shift = newStart - first;
                _selAnchor += shift;
                _selActive += shift;
            }
            SyncRows();
            if (focusBlock != null)
                FocusRow(_blocks.IndexOf(focusBlock), caret);
            else
                BringRowIntoView(_selActive);
            RecordChange();
        }

        /// <summary>[first, last] 블록(자식 포함)을 바로 아래에 복제한다.</summary>
        private void DuplicateBlocks(int first, int last)
        {
            var end = BlockOperations.ExtendToSubtrees(_blocks, first, last + 1);
            var copies = _blocks.GetRange(first, end - first).Select(b => b.Duplicate()).ToList();
            _blocks.InsertRange(end, copies);
            SyncRows();
            if (HasBlockSelection)
                SelectBlocks(end, end + (last - first));
            else
                FocusRow(end, _blocks[end].Text.Length);
            RecordChange();
        }

        private void DeleteBlocks(int first, int last)
        {
            var end = BlockOperations.ExtendToSubtrees(_blocks, first, last + 1);
            _blocks.RemoveRange(first, end - first);
            if (_blocks.Count == 0)
                _blocks.Add(new EditorBlock(BlockKind.Paragraph));
            BlockOperations.NormalizeIndents(_blocks);
            _selAnchor = _selActive = -1;
            SyncRows();
            var focus = NextEditable(Math.Min(first, _blocks.Count), -1);
            if (focus < 0)
                focus = NextEditable(-1, 1);
            FocusRow(focus, int.MaxValue);
            RecordChange();
        }

        /// <summary>[first, last] 블록의 종류를 바꾼다. 구분선으로 바꾸면 내용을 버리고 아래에 빈 문단을 둔다.</summary>
        private void TurnInto(int first, int last, BlockKind kind, bool isChecked = false)
        {
            if (first < 0)
                return;
            var caret = _focusedRow != null ? _focusedRow.Editor.CaretIndex : 0;
            var focusBlock = HasBlockSelection ? null : _focusedRow?.Block;
            EditorBlock afterDivider = null;
            for (var i = first; i <= last && i < _blocks.Count; i++)
            {
                var block = _blocks[i];
                var previousKind = block.Kind;
                if (kind == BlockKind.Divider)
                {
                    block.Text = string.Empty;
                    if (i == last)
                    {
                        afterDivider = new EditorBlock(BlockKind.Paragraph, string.Empty, block.Indent);
                        _blocks.Insert(i + 1, afterDivider);
                    }
                }
                block.Kind = kind;
                if (kind == BlockKind.Todo && previousKind != BlockKind.Todo)
                    block.Checked = isChecked;
                if (kind == BlockKind.Toggle && previousKind != BlockKind.Toggle)
                    block.Collapsed = false;
                if (IsHeading(kind))
                    block.Text = block.Text.Replace("\r\n", " ").Replace('\n', ' ');
            }
            BlockOperations.NormalizeIndents(_blocks);
            SyncRows();
            if (afterDivider != null)
            {
                _selAnchor = _selActive = -1;
                FocusRow(_blocks.IndexOf(afterDivider), 0);
            }
            else if (focusBlock != null)
                FocusRow(_blocks.IndexOf(focusBlock), caret);
            else
                UpdateSelectionVisuals();
            RecordChange();
        }

        /// <summary>Ctrl+Enter: 할 일 체크, 토글 접기·펴기, 코드·원문 블록에서는 아래에 새 문단.</summary>
        private void ToggleState(BlockRow row)
        {
            var block = row.Block;
            switch (block.Kind)
            {
                case BlockKind.Todo:
                    block.Checked = !block.Checked;
                    row.RefreshState();
                    RecordChange();
                    break;
                case BlockKind.Toggle:
                    ToggleCollapse(row);
                    break;
                case BlockKind.Code:
                case BlockKind.Raw:
                    var index = IndexOf(row);
                    var at = BlockOperations.SubtreeEnd(_blocks, index);
                    _blocks.Insert(at, new EditorBlock(BlockKind.Paragraph, string.Empty, block.Indent));
                    SyncRows();
                    FocusRow(at, 0);
                    RecordChange();
                    break;
            }
        }

        /// <summary>토글 접기·펴기. 보기 상태라 저장할 변경으로 세지 않는다(저장하면 그때 상태가 <c>open</c> 속성으로 들어감).</summary>
        private void ToggleCollapse(BlockRow row)
        {
            row.Block.Collapsed = !row.Block.Collapsed;
            row.RefreshState();
            RefreshStructure();
            if (row.Editor.IsKeyboardFocused)
                return;
            FocusRow(IndexOf(row), int.MaxValue);
        }

        private void CycleCallout(BlockRow row)
        {
            var types = BlockMarkdown.CalloutTypes;
            var current = Array.IndexOf(types, BlockMarkdown.NormalizeCalloutType(row.Block.CalloutType));
            row.Block.CalloutType = types[(current + 1) % types.Length];
            row.RefreshState();
            RecordChange();
        }

        private void ToggleChecked(BlockRow row)
        {
            row.Block.Checked = !row.Block.Checked;
            row.RefreshState();
            RecordChange();
        }

        /// <summary>선택 영역을 Markdown 표식으로 감싼다. 이미 감싸져 있으면 표식을 벗긴다.</summary>
        private bool WrapSelection(BlockRow row, string marker)
        {
            var editor = row.Editor;
            var start = editor.SelectionStart;
            var length = editor.SelectionLength;
            var text = editor.Text;
            _typingRow = null;
            if (start >= marker.Length && start + length + marker.Length <= text.Length &&
                string.CompareOrdinal(text, start - marker.Length, marker, 0, marker.Length) == 0 &&
                string.CompareOrdinal(text, start + length, marker, 0, marker.Length) == 0)
            {
                editor.Text = text.Remove(start + length, marker.Length).Remove(start - marker.Length, marker.Length);
                editor.Select(start - marker.Length, length);
            }
            else
            {
                editor.Text = text.Insert(start + length, marker).Insert(start, marker);
                editor.Select(start + marker.Length, length);
            }
            _typingRow = null;
            return true;
        }

        /// <summary>Ctrl+K: 선택 글자를 <c>[글자](https://)</c> 링크로 만들고 주소 부분을 선택한다.</summary>
        private bool InsertLink(BlockRow row)
        {
            var editor = row.Editor;
            var start = editor.SelectionStart;
            var label = editor.SelectedText.Length > 0 ? editor.SelectedText : "링크";
            const string url = "https://";
            _typingRow = null;
            editor.Text = editor.Text.Remove(start, editor.SelectionLength).Insert(start, "[" + label + "](" + url + ")");
            editor.Select(start + label.Length + 3, url.Length);
            _typingRow = null;
            return true;
        }

        /// <summary>왼쪽 + 버튼: 아래에 빈 블록을 만들고 <c>/</c> 메뉴를 연다(이 블록이 빈 문단이면 그대로 쓴다).</summary>
        private void AddBlockBelow(BlockRow row)
        {
            var index = IndexOf(row);
            if (row.Block.Kind == BlockKind.TableRow)
            {
                InsertTableRow(index + 1, 0);
                return;
            }
            int target;
            if (row.Block.Kind == BlockKind.Paragraph && row.Block.Text.Length == 0)
            {
                row.Block.Text = "/";
                target = index;
            }
            else
            {
                target = BlockOperations.SubtreeEnd(_blocks, index);
                _blocks.Insert(target, new EditorBlock(BlockKind.Paragraph, "/", row.Block.Indent));
            }
            BlockOperations.NormalizeIndents(_blocks);
            _selAnchor = _selActive = -1;
            SyncRows();
            FocusRow(target, 1);
            RecordChange();
            var slashRow = _panel.GetRealized(target);
            if (slashRow != null)
                OpenSlash(slashRow, 0);
        }

        /// <summary>문서 아래 빈 곳을 누르면 마지막 블록으로(비어 있지 않으면 새 문단을 만들어) 들어간다.</summary>
        private void OnBottomClick()
        {
            var last = _blocks.Count - 1;
            if (last >= 0 && _blocks[last].Kind == BlockKind.Paragraph && _blocks[last].Indent == 0 && _blocks[last].Text.Length == 0 && IsEditable(last))
            {
                FocusRow(last, 0);
            }
            else
            {
                _blocks.Add(new EditorBlock(BlockKind.Paragraph));
                SyncRows();
                FocusRow(_blocks.Count - 1, 0);
                RecordChange();
            }
        }

        // ── 입력란 이벤트 ──────────────────────────────────────────────────

        private void OnEditorTextChanged(BlockRow row, TextChangedEventArgs e)
        {
            if (_suppressTextChanged)
                return;
            var editor = row.Editor;
            var wasBlank = string.IsNullOrWhiteSpace(row.Block.Text);
            row.Block.Text = editor.Text;
            row.UpdatePlaceholder();
            RecordChange(row);
            // 빈 문단은 번호 목록 번호를 끊지 않으므로, 비었다가 글자가 생기거나 그 반대면 번호를 다시 센다.
            if (row.Block.Kind == BlockKind.Paragraph && wasBlank != string.IsNullOrWhiteSpace(row.Block.Text))
                RefreshStructure();
            if (editor.IsKeyboardFocused)
                ScheduleCaretIntoView(editor);

            TextChange inserted = null;
            foreach (var change in e.Changes)
                if (change.AddedLength > 0)
                    inserted = change;

            if (_slashRow == row)
            {
                // TextChanged 시점에는 커서가 아직 옮겨지지 않았을 수 있어 이벤트가 끝난 뒤 검색어를 다시 읽는다.
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(UpdateSlashQuery));
            }
            else if (inserted != null && !EditorBlock.IsVerbatim(row.Block.Kind) && row.Block.Kind != BlockKind.Image && inserted.AddedLength == 1)
            {
                var at = inserted.Offset;
                if (at < editor.Text.Length && editor.Text[at] == '/' && (at == 0 || char.IsWhiteSpace(editor.Text[at - 1])))
                    OpenSlash(row, at);
            }

            if (inserted != null)
                TryAutoFormat(row, inserted);
        }

        /// <summary>문단 맨 앞에 Markdown 표식을 입력하면 블록 종류를 바꾼다. 실행 취소하면 입력한 표식 글자로 돌아간다.</summary>
        private void TryAutoFormat(BlockRow row, TextChange inserted)
        {
            var block = row.Block;
            if (block.Kind != BlockKind.Paragraph && block.Kind != BlockKind.Bulleted)
                return;
            // 커서 대신 방금 들어간 글자 위치로 판단한다(TextChanged 때 커서가 아직 안 옮겨졌을 수 있음).
            var editor = row.Editor;
            var prefixEnd = inserted.Offset + inserted.AddedLength;
            if (prefixEnd > 6 || prefixEnd > editor.Text.Length)
                return;

            var prefix = editor.Text.Substring(0, prefixEnd);
            BlockKind kind;
            bool isChecked;
            if (!BlockMarkdown.TryMatchShortcut(prefix, out kind, out isChecked))
                return;
            if (block.Kind == BlockKind.Bulleted && kind != BlockKind.Todo)
                return;
            if (kind == BlockKind.Divider && editor.Text != prefix)
                return;

            CloseSlash();
            // TextChanged 처리 중에 같은 TextBox의 내용을 바꾸지 않도록 이벤트가 끝난 뒤 적용한다.
            Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => ApplyShortcut(row, kind, isChecked, prefix)));
        }

        private void ApplyShortcut(BlockRow row, BlockKind kind, bool isChecked, string prefix)
        {
            var index = IndexOf(row);
            if (index < 0 || !row.Block.Text.StartsWith(prefix, StringComparison.Ordinal))
                return;
            var block = row.Block;
            block.Text = block.Text.Substring(prefix.Length);
            if (kind == BlockKind.Divider)
            {
                TurnInto(index, index, BlockKind.Divider);
                return;
            }
            _focusedRow = row;
            row.SyncFromBlock();
            row.Editor.CaretIndex = 0;
            TurnInto(index, index, kind, isChecked);
        }

        private void OnEditorFocused(BlockRow row)
        {
            _focusedRow = row;
            if (HasBlockSelection)
                ClearSelection();
            row.UpdatePlaceholder();
        }

        private void OnEditorBlurred(BlockRow row)
        {
            row.UpdatePlaceholder();
            if (_slashRow != row)
                return;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (_slashRow == row && !row.Editor.IsKeyboardFocused)
                    CloseSlash();
            }));
        }

        private void OnEditorSelectionChanged(BlockRow row)
        {
            if (_suppressTextChanged)
                return;
            if (_slashRow == row)
                UpdateSlashQuery();
            if (row.Editor.IsKeyboardFocused && Mouse.LeftButton != MouseButtonState.Pressed)
                ScheduleCaretIntoView(row.Editor);
        }

        /// <summary>여러 줄 텍스트를 붙여넣으면 Markdown으로 읽어 블록으로 넣는다(코드·원문 블록 안에서는 그대로 붙여넣음).</summary>
        private void OnEditorPasting(BlockRow row, DataObjectPastingEventArgs e)
        {
            if (EditorBlock.IsVerbatim(row.Block.Kind) || !e.DataObject.GetDataPresent(DataFormats.UnicodeText))
                return;
            var text = e.DataObject.GetData(DataFormats.UnicodeText) as string;
            if (string.IsNullOrEmpty(text) || text.IndexOf('\n') < 0)
                return;
            e.CancelCommand();
            PasteBlocks(row, text);
        }

        private void PasteBlocks(BlockRow row, string markdown)
        {
            var parsed = BlockMarkdown.Parse(markdown);
            if (parsed.Count == 0)
                return;

            var index = IndexOf(row);
            var block = row.Block;
            var editor = row.Editor;
            var selectionStart = editor.SelectionStart;
            var current = editor.Text.Remove(selectionStart, editor.SelectionLength);
            var head = current.Substring(0, selectionStart);
            var tail = current.Substring(selectionStart);

            foreach (var item in parsed)
                item.Indent += block.Indent;

            var inserted = new List<EditorBlock>();
            if (current.Length == 0 && block.Kind == BlockKind.Paragraph)
            {
                CopyContent(parsed[0], block);
                inserted.AddRange(parsed.Skip(1));
            }
            else if (parsed[0].Kind == BlockKind.Paragraph)
            {
                block.Text = head + parsed[0].Text;
                inserted.AddRange(parsed.Skip(1));
            }
            else
            {
                block.Text = head;
                inserted.AddRange(parsed);
            }

            var last = inserted.Count > 0 ? inserted[inserted.Count - 1] : block;
            var caret = last.Text.Length;
            if (tail.Length > 0)
            {
                if (last.Kind == BlockKind.Divider)
                {
                    last = new EditorBlock(BlockKind.Paragraph, string.Empty, last.Indent);
                    inserted.Add(last);
                    caret = 0;
                }
                last.Text += tail;
            }

            _blocks.InsertRange(index + 1, inserted);
            BlockOperations.NormalizeIndents(_blocks);
            SyncRows();
            FocusRow(_blocks.IndexOf(last), caret);
            RecordChange();
        }

        private static void CopyContent(EditorBlock source, EditorBlock target)
        {
            target.Kind = source.Kind;
            target.Text = source.Text;
            target.Indent = source.Indent;
            target.Checked = source.Checked;
            target.Collapsed = source.Collapsed;
            target.Language = source.Language;
            target.CalloutType = source.CalloutType;
        }

        // ── 접근성 ─────────────────────────────────────────────────────────

        /// <summary>
        /// UI Automation 클라이언트(IME·입력 도우미·화면 낭독기 등)가 붙어 있으면 WPF는 레이아웃이 바뀔 때마다 만들어진 자동화 트리 전체를
        /// 다시 훑는다. 블록 전부를 자식으로 내보내면 1,600블록 문서를 여는 데 10초 넘게 걸려서, 가상화 목록처럼
        /// 화면에 보이는 블록과 포커스 블록만 자식으로 내보낸다. 포커스된 입력란의 이벤트·텍스트 패턴은 그대로다.
        /// </summary>
        protected override AutomationPeer OnCreateAutomationPeer()
        {
            return new BlockEditorAutomationPeer(this);
        }

        private List<AutomationPeer> GetAutomationChildren()
        {
            var peers = new List<AutomationPeer>();
            foreach (var row in _panel.RowsInViewport(_focusedRow))
            {
                var peer = UIElementAutomationPeer.CreatePeerForElement(row.AutomationTarget);
                if (peer != null)
                    peers.Add(peer);
            }
            return peers;
        }

        private sealed class BlockEditorAutomationPeer : FrameworkElementAutomationPeer
        {
            public BlockEditorAutomationPeer(BlockEditor owner) : base(owner) { }

            protected override AutomationControlType GetAutomationControlTypeCore()
            {
                return AutomationControlType.Document;
            }

            protected override string GetClassNameCore()
            {
                return nameof(BlockEditor);
            }

            protected override string GetNameCore()
            {
                var name = base.GetNameCore();
                return string.IsNullOrEmpty(name) ? "블록 편집기" : name;
            }

            protected override List<AutomationPeer> GetChildrenCore()
            {
                return ((BlockEditor)Owner).GetAutomationChildren();
            }
        }

        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
        {
            base.OnPreviewMouseWheel(e);
            if (e.Handled)
                return;
            // 블록 TextBox가 휠을 먹지 않도록 바깥 스크롤로 넘긴다.
            _panel.SetVerticalOffset(_panel.VerticalOffset - e.Delta * 0.4);
            e.Handled = true;
        }

        // ── 블록 선택 ──────────────────────────────────────────────────────

        private bool HasBlockSelection => _selAnchor >= 0 && _selActive >= 0;

        private void GetSelectionRange(out int start, out int end)
        {
            if (!HasBlockSelection || _selAnchor >= _blocks.Count || _selActive >= _blocks.Count)
            {
                start = end = -1;
                return;
            }
            start = Math.Min(_selAnchor, _selActive);
            end = BlockOperations.ExtendToSubtrees(_blocks, start, Math.Max(_selAnchor, _selActive) + 1);
        }

        private void SelectBlocks(int anchor, int active)
        {
            if (anchor < 0 || active < 0 || anchor >= _blocks.Count || active >= _blocks.Count)
                return;
            CloseSlash();
            _selAnchor = anchor;
            _selActive = active;
            if (!IsKeyboardFocused)
                Keyboard.Focus(this);
            UpdateSelectionVisuals();
            BringRowIntoView(active);
        }

        private void ClearSelection()
        {
            _selAnchor = _selActive = -1;
            UpdateSelectionVisuals();
        }

        private void UpdateSelectionVisuals()
        {
            int start, end;
            GetSelectionRange(out start, out end);
            _panel.ApplySelection(start, end);
        }

        private void BringRowIntoView(int index)
        {
            if (index >= 0 && index < _blocks.Count)
                _panel.ScrollIndexIntoView(index);
        }

        private void HandleSelectionKey(KeyEventArgs e, Key key, ModifierKeys mods)
        {
            var first = Math.Min(_selAnchor, _selActive);
            var last = Math.Max(_selAnchor, _selActive);
            var ctrl = ModifierKeys.Control;

            // 처리하는 키와, 문서 탭 전환으로 올라가면 안 되는 이동 키만 막는다(Alt+F4 등은 그대로 둔다).
            switch (key)
            {
                case Key.Home:
                case Key.End:
                case Key.PageUp:
                case Key.PageDown:
                case Key.Left:
                case Key.Right:
                case Key.Escape:
                case Key.Enter:
                case Key.Up:
                case Key.Down:
                case Key.Delete:
                case Key.Back:
                case Key.Tab:
                    e.Handled = true;
                    break;
                case Key.C:
                case Key.X:
                case Key.V:
                case Key.D:
                case Key.A:
                    e.Handled = mods == ctrl;
                    break;
            }

            switch (key)
            {
                case Key.Escape:
                    ClearSelection();
                    return;
                case Key.Enter:
                    if (_blocks[_selActive].Kind == BlockKind.Divider)
                    {
                        var at = _selActive + 1;
                        _blocks.Insert(at, new EditorBlock(BlockKind.Paragraph, string.Empty, _blocks[_selActive].Indent));
                        _selAnchor = _selActive = -1;
                        SyncRows();
                        FocusRow(at, 0);
                        RecordChange();
                    }
                    else
                    {
                        var target = _selActive;
                        ClearSelection();
                        FocusRow(target, int.MaxValue);
                    }
                    return;
                case Key.Up:
                case Key.Down:
                    var direction = key == Key.Up ? -1 : 1;
                    if (mods == (ctrl | ModifierKeys.Shift))
                    {
                        MoveBlocks(first, last, direction);
                        return;
                    }
                    var next = NextVisible(_selActive, direction);
                    if (next < 0)
                        return;
                    if (mods == ModifierKeys.Shift)
                        _selActive = next;
                    else
                        _selAnchor = _selActive = next;
                    UpdateSelectionVisuals();
                    BringRowIntoView(next);
                    return;
                case Key.Delete:
                case Key.Back:
                    DeleteBlocks(first, last);
                    return;
                case Key.Tab:
                    IndentBlocks(first, last, mods == ModifierKeys.Shift ? -1 : 1);
                    return;
                case Key.C:
                case Key.X:
                    if (mods == ctrl)
                    {
                        CopyBlocks(first, last);
                        if (key == Key.X)
                            DeleteBlocks(first, last);
                    }
                    return;
                case Key.V:
                    if (mods == ctrl)
                        PasteAfterSelection(last);
                    return;
                case Key.D:
                    if (mods == ctrl)
                        DuplicateBlocks(first, last);
                    return;
                case Key.A:
                    if (mods == ctrl)
                        SelectBlocks(0, _blocks.Count - 1);
                    return;
            }

            BlockKind kind;
            if (mods == (ctrl | ModifierKeys.Alt) && TryGetTurnIntoKind(key, out kind))
            {
                e.Handled = true;
                TurnInto(first, last, kind);
            }
        }

        private void CopyBlocks(int first, int last)
        {
            var end = BlockOperations.ExtendToSubtrees(_blocks, first, last + 1);
            var markdown = BlockMarkdown.Serialize(_blocks.GetRange(first, end - first));
            ClipboardService.TrySetDataObject(new DataObject(DataFormats.UnicodeText, markdown));
        }

        private void PasteAfterSelection(int last)
        {
            string text;
            if (!ClipboardService.TryGetText(out text) || string.IsNullOrEmpty(text))
                return;
            var end = BlockOperations.ExtendToSubtrees(_blocks, last, last + 1);
            var parsed = BlockMarkdown.Parse(text);
            if (parsed.Count == 0)
                return;
            var indent = _blocks[last].Indent;
            foreach (var block in parsed)
                block.Indent += indent;
            _blocks.InsertRange(end, parsed);
            BlockOperations.NormalizeIndents(_blocks);
            SyncRows();
            SelectBlocks(end, end + parsed.Count - 1);
            RecordChange();
        }

        // ── 블록 손잡이: 끌어 옮기기·메뉴 ─────────────────────────────────

        private void HandleMouseDown(BlockRow row, UIElement handle, MouseButtonEventArgs e)
        {
            _dragRow = row;
            _dragStart = e.GetPosition(_panel);
            _dragging = false;
            _dropIndex = -1;
            handle.CaptureMouse();
            e.Handled = true;
        }

        private void HandleMouseMove(UIElement handle, MouseEventArgs e)
        {
            if (_dragRow == null || !handle.IsMouseCaptured)
                return;
            var position = e.GetPosition(_panel);
            if (!_dragging && (Math.Abs(position.Y - _dragStart.Y) > 4 || Math.Abs(position.X - _dragStart.X) > 4))
            {
                _dragging = true;
                CloseSlash();
            }
            if (!_dragging)
                return;

            // 위·아래 가장자리 가까이 끌면 스크롤
            var inViewport = e.GetPosition(_scroll).Y;
            if (inViewport < 30)
                _panel.SetVerticalOffset(_panel.VerticalOffset - 12);
            else if (inViewport > _scroll.ActualHeight - 30)
                _panel.SetVerticalOffset(_panel.VerticalOffset + 12);

            UpdateDropIndicator(position);
        }

        private void HandleMouseUp(BlockRow row, UIElement handle, MouseButtonEventArgs e)
        {
            var wasDragging = _dragging;
            var drop = _dropIndex;
            EndDrag(handle);
            e.Handled = true;
            if (wasDragging)
                DropBlock(row, drop);
            else
                ShowBlockMenu(row, handle);
        }

        private void EndDrag(UIElement handle)
        {
            _dragging = false;
            _dragRow = null;
            _dropIndex = -1;
            _panel.HideDropLine();
            if (handle.IsMouseCaptured)
                handle.ReleaseMouseCapture();
        }

        private void UpdateDropIndicator(Point position)
        {
            var start = IndexOf(_dragRow);
            var end = BlockOperations.SubtreeEnd(_blocks, start);
            var drop = _panel.DropIndexAt(position.Y);
            if (drop >= start && drop <= end)
            {
                _dropIndex = -1;
                _panel.HideDropLine();
                return;
            }
            _dropIndex = drop;
            _panel.ShowDropLine(drop, drop < _blocks.Count ? _blocks[drop].Indent : 0);
        }

        private void DropBlock(BlockRow row, int drop)
        {
            var start = IndexOf(row);
            if (start < 0 || drop < 0)
                return;
            var end = BlockOperations.SubtreeEnd(_blocks, start);
            if (drop >= start && drop <= end)
                return;

            var moving = _blocks.GetRange(start, end - start);
            _blocks.RemoveRange(start, end - start);
            var insertAt = drop > start ? drop - (end - start) : drop;
            var targetIndent = insertAt < _blocks.Count ? _blocks[insertAt].Indent : 0;
            var delta = targetIndent - moving[0].Indent;
            foreach (var block in moving)
                block.Indent = Math.Max(0, block.Indent + delta);
            _blocks.InsertRange(insertAt, moving);
            BlockOperations.NormalizeIndents(_blocks);
            _selAnchor = _selActive = -1;
            SyncRows();
            FocusRow(insertAt, int.MaxValue);
            RecordChange();
        }

        private void ShowBlockMenu(BlockRow row, UIElement target)
        {
            var index = IndexOf(row);
            SelectBlocks(index, index);

            var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };
            Func<int> first = () => Math.Min(_selAnchor, _selActive);
            Func<int> last = () => Math.Max(_selAnchor, _selActive);
            Func<bool> valid = () => HasBlockSelection;

            menu.Items.Add(CreateMenuItem("삭제", "Del", () => { if (valid()) DeleteBlocks(first(), last()); }));
            menu.Items.Add(CreateMenuItem("복제", "Ctrl+D", () => { if (valid()) DuplicateBlocks(first(), last()); }));
            menu.Items.Add(CreateMenuItem("위로 이동", "Ctrl+Shift+↑", () => { if (valid()) MoveBlocks(first(), last(), -1); }));
            menu.Items.Add(CreateMenuItem("아래로 이동", "Ctrl+Shift+↓", () => { if (valid()) MoveBlocks(first(), last(), 1); }));
            menu.Items.Add(new Separator());

            var turnInto = new MenuItem { Header = "전환" };
            foreach (var command in SlashCommands.Where(c => c.Kind.HasValue))
            {
                var kind = command.Kind.Value;
                turnInto.Items.Add(CreateMenuItem(command.Title, command.Hint, () => { if (valid()) TurnInto(first(), last(), kind); }));
            }
            menu.Items.Add(turnInto);
            menu.Items.Add(CreateColorMenu(first, last, valid));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("Markdown으로 복사", "Ctrl+C", () => { if (valid()) CopyBlocks(first(), last()); }));
            if (row.Block.Kind == BlockKind.TableRow)
            {
                menu.Items.Add(new Separator());
                menu.Items.Add(CreateMenuItem("아래에 행 추가", "Enter", () => InsertTableRow(IndexOf(row) + 1, 0)));
                menu.Items.Add(CreateMenuItem("오른쪽 끝에 열 추가", string.Empty, () => AddTableColumn(IndexOf(row))));
                menu.Items.Add(CreateMenuItem("마지막 열 삭제", string.Empty, () => RemoveLastTableColumn(IndexOf(row))));
            }
            menu.IsOpen = true;
        }

        private static MenuItem CreateMenuItem(string header, string gesture, Action action)
        {
            var item = new MenuItem { Header = header, InputGestureText = gesture ?? string.Empty };
            item.Click += (s, e) => action();
            return item;
        }

        // ── / 명령 메뉴 ────────────────────────────────────────────────────

        private sealed class SlashCommand
        {
            public string Title;
            public string Icon;
            public string Hint;
            public BlockKind? Kind;
            public string[] Keywords;
            public Action<BlockEditor, BlockRow, int> Run;

            public bool Matches(string query)
            {
                var q = Compact(query);
                if (q.Length == 0)
                    return true;
                return Compact(Title).Contains(q) || Keywords.Any(k => k.StartsWith(q, StringComparison.OrdinalIgnoreCase) || Compact(k).Contains(q));
            }

            private static string Compact(string value)
            {
                return (value ?? string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
            }
        }

        private static SlashCommand BlockCommand(string title, string icon, string hint, BlockKind kind, params string[] keywords)
        {
            return new SlashCommand { Title = title, Icon = icon, Hint = hint, Kind = kind, Keywords = keywords };
        }

        private static readonly SlashCommand[] SlashCommands =
        {
            BlockCommand("텍스트", "T", "Ctrl+Alt+0", BlockKind.Paragraph, "text", "paragraph", "plain", "본문"),
            BlockCommand("제목 1", "H1", "#", BlockKind.Heading1, "h1", "heading1", "title", "큰제목"),
            BlockCommand("제목 2", "H2", "##", BlockKind.Heading2, "h2", "heading2", "subtitle", "중간제목"),
            BlockCommand("제목 3", "H3", "###", BlockKind.Heading3, "h3", "heading3", "소제목"),
            BlockCommand("글머리 기호 목록", "•", "-", BlockKind.Bulleted, "bullet", "list", "ul", "목록"),
            BlockCommand("번호 매기기 목록", "1.", "1.", BlockKind.Numbered, "numbered", "number", "ol", "번호"),
            BlockCommand("할 일 목록", "☐", "[]", BlockKind.Todo, "todo", "task", "check", "checkbox", "할일", "체크"),
            BlockCommand("토글", "▸", "Ctrl+Alt+7", BlockKind.Toggle, "toggle", "details", "collapse", "접기"),
            BlockCommand("인용", "❝", ">", BlockKind.Quote, "quote", "blockquote"),
            BlockCommand("콜아웃", "💡", "", BlockKind.Callout, "callout", "note", "tip", "warning", "알림", "강조"),
            BlockCommand("코드", "</>", "```", BlockKind.Code, "code", "snippet", "codeblock"),
            BlockCommand("구분선", "—", "---", BlockKind.Divider, "divider", "hr", "line", "separator", "구분"),
            new SlashCommand
            {
                Title = "표", Icon = "▦", Hint = "Enter 행 추가", Keywords = new[] { "table", "grid", "표", "테이블" },
                Run = (editor, row, at) => editor.InsertTable(row),
            },
            new SlashCommand
            {
                Title = "이미지", Icon = "🖼", Hint = "![](경로)", Keywords = new[] { "image", "img", "picture", "이미지", "그림" },
                Run = (editor, row, at) => editor.InsertImage(row),
            },
            new SlashCommand
            {
                Title = "오늘 날짜", Icon = "📅", Hint = "", Keywords = new[] { "date", "today", "날짜", "오늘" },
                Run = (editor, row, at) => editor.InsertText(row, at, DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            },
        };

        private Popup CreateSlashPopup()
        {
            var header = new TextBlock { Text = "블록", FontSize = 11, Margin = new Thickness(8, 4, 8, 4) };
            header.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            var scroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Focusable = false,
                MaxHeight = 320,
                Content = _slashItems,
            };
            var stack = new StackPanel();
            stack.Children.Add(header);
            stack.Children.Add(scroller);

            var frame = new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4),
                MinWidth = 280,
                Child = stack,
            };
            frame.SetResourceReference(Border.BackgroundProperty, "SurfaceBackground");
            frame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            frame.SetResourceReference(TextElement.FontFamilyProperty, "AppFontFamily");

            return new Popup
            {
                AllowsTransparency = true,
                StaysOpen = true,
                Placement = PlacementMode.Bottom,
                Focusable = false,
                Child = frame,
            };
        }

        private void OpenSlash(BlockRow row, int slashIndex)
        {
            _slashRow = row;
            _slashStart = slashIndex;
            _slashIndex = 0;
            // 커서와 방금 입력한 글자의 화면 위치는 입력 처리·레이아웃이 끝난 뒤에 정확하다.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (_slashRow != row || !row.Editor.IsVisible)
                    return;
                UpdateSlashQuery();
                if (_slashRow != row)
                    return;
                var rect = row.Editor.GetRectFromCharacterIndex(Math.Min(_slashStart, row.Editor.Text.Length));
                _slashPopup.PlacementTarget = row.Editor;
                _slashPopup.PlacementRectangle = rect.IsEmpty ? new Rect(0, 0, 0, row.Editor.ActualHeight) : rect;
                _slashPopup.IsOpen = true;
            }));
        }

        private void UpdateSlashQuery()
        {
            var row = _slashRow;
            if (row == null)
                return;
            var editor = row.Editor;
            var text = editor.Text;
            var caret = editor.CaretIndex;
            if (_slashStart >= text.Length || text[_slashStart] != '/' || caret <= _slashStart)
            {
                CloseSlash();
                return;
            }
            var query = text.Substring(_slashStart + 1, caret - _slashStart - 1);
            if (query.IndexOf('\n') >= 0 || query.Length > 24)
            {
                CloseSlash();
                return;
            }
            _slashMatches = SlashCommands.Where(c => c.Matches(query)).ToList();
            if (_slashMatches.Count == 0)
            {
                CloseSlash();
                return;
            }
            _slashIndex = Math.Max(0, Math.Min(_slashIndex, _slashMatches.Count - 1));
            RenderSlashItems();
        }

        private void RenderSlashItems()
        {
            _slashItems.Children.Clear();
            for (var i = 0; i < _slashMatches.Count; i++)
            {
                var command = _slashMatches[i];
                var index = i;

                var icon = new Border
                {
                    Width = 30,
                    Height = 30,
                    CornerRadius = new CornerRadius(4),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 10, 0),
                    Child = new TextBlock { Text = command.Icon, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                };
                icon.SetResourceReference(Border.BackgroundProperty, "ControlBackground");
                icon.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

                var title = new TextBlock { Text = command.Title, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
                title.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
                var hint = new TextBlock { Text = command.Hint, VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Margin = new Thickness(12, 0, 0, 0) };
                hint.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(title, 1);
                Grid.SetColumn(hint, 2);
                grid.Children.Add(icon);
                grid.Children.Add(title);
                grid.Children.Add(hint);

                var item = new Border { Padding = new Thickness(6, 4, 10, 4), CornerRadius = new CornerRadius(4), Child = grid, Cursor = Cursors.Hand };
                item.MouseEnter += (s, e) =>
                {
                    _slashIndex = index;
                    HighlightSlashItem();
                };
                item.MouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    ExecuteSlash(index);
                };
                _slashItems.Children.Add(item);
            }
            HighlightSlashItem();
        }

        private void HighlightSlashItem()
        {
            for (var i = 0; i < _slashItems.Children.Count; i++)
            {
                var item = (Border)_slashItems.Children[i];
                if (i == _slashIndex)
                {
                    item.SetResourceReference(Border.BackgroundProperty, "ControlHoverBrush");
                    item.BringIntoView();
                }
                else
                {
                    item.Background = Brushes.Transparent;
                }
            }
        }

        private bool HandleSlashKey(Key key, ModifierKeys mods)
        {
            if (mods != ModifierKeys.None)
                return false;
            switch (key)
            {
                case Key.Up:
                    _slashIndex = (_slashIndex - 1 + _slashMatches.Count) % _slashMatches.Count;
                    HighlightSlashItem();
                    return true;
                case Key.Down:
                    _slashIndex = (_slashIndex + 1) % _slashMatches.Count;
                    HighlightSlashItem();
                    return true;
                case Key.Enter:
                case Key.Tab:
                    ExecuteSlash(_slashIndex);
                    return true;
                case Key.Escape:
                    CloseSlash();
                    return true;
            }
            return false;
        }

        private void ExecuteSlash(int index)
        {
            var row = _slashRow;
            if (row == null || index < 0 || index >= _slashMatches.Count)
                return;
            var command = _slashMatches[index];
            var editor = row.Editor;
            var caret = Math.Max(editor.CaretIndex, _slashStart);
            var at = _slashStart;
            var text = editor.Text.Remove(at, Math.Min(caret, editor.Text.Length) - at);
            CloseSlash();

            row.Block.Text = text;
            row.SyncFromBlock();
            _focusedRow = row;
            editor.CaretIndex = at;

            if (command.Run != null)
            {
                command.Run(this, row, at);
                return;
            }
            ApplyBlockCommand(row, command.Kind.Value);
        }

        /// <summary>블록이 비어 있으면 그 블록을 바꾸고, 내용이 있으면 아래에 새 블록을 만든다(Notion과 같음).</summary>
        private void ApplyBlockCommand(BlockRow row, BlockKind kind)
        {
            var index = IndexOf(row);
            if (row.Block.Text.Trim().Length == 0)
            {
                row.Block.Text = string.Empty;
                row.SyncFromBlock();
                TurnInto(index, index, kind);
                return;
            }

            var at = BlockOperations.SubtreeEnd(_blocks, index);
            var created = new EditorBlock(kind, string.Empty, row.Block.Indent);
            _blocks.Insert(at, created);
            if (kind == BlockKind.Divider)
                _blocks.Insert(at + 1, new EditorBlock(BlockKind.Paragraph, string.Empty, row.Block.Indent));
            BlockOperations.NormalizeIndents(_blocks);
            SyncRows();
            FocusRow(kind == BlockKind.Divider ? at + 1 : at, 0);
            RecordChange();
        }

        private void InsertText(BlockRow row, int at, string value)
        {
            var text = row.Block.Text.Insert(Math.Min(at, row.Block.Text.Length), value);
            row.Block.Text = text;
            row.SyncFromBlock();
            FocusRow(IndexOf(row), at + value.Length);
            RecordChange();
        }

        private void CloseSlash()
        {
            _slashRow = null;
            _slashMatches = new List<SlashCommand>();
            if (_slashPopup != null && _slashPopup.IsOpen)
                _slashPopup.IsOpen = false;
        }
    }
}

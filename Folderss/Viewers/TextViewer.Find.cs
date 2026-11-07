using Folderss.Services;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using TextSearch = Folderss.Services.TextSearch;   // System.Windows.Controls.TextSearch와 구분

namespace Folderss.Viewers
{
    /// <summary>
    /// 일반 텍스트 편집기의 찾기/바꾸기(Ctrl+F/H, F3)와 줄 이동(Ctrl+G).
    /// <list type="bullet">
    /// <item>입력하는 대로 찾는다(입력이 멈추면 백그라운드에서 전체를 찾고, 찾기를 연 위치 다음 첫 결과로 이동). 계산은 <see cref="TextSearch"/>.</item>
    /// <item>모든 결과를 강조하되 화면에 보이는 것만 그린다(<see cref="SearchResultRenderer"/>). 찾기 막대가 열린 동안 편집하면 다시 찾는다.</item>
    /// <item>모두 바꾸기는 한 번의 편집 묶음이라 Ctrl+Z 한 번으로 되돌린다.</item>
    /// </list>
    /// </summary>
    public partial class TextViewer
    {
        private readonly SearchResultRenderer _searchRenderer = new SearchResultRenderer();
        private readonly DispatcherTimer _searchTimer = new DispatcherTimer();
        private List<TextMatch> _matches = new List<TextMatch>();
        private bool _matchesTruncated;
        private Regex _regex;
        private string _regexError;
        private int _searchVersion;
        private bool _searchStale = true;    // 마지막 계산 뒤 검색어·조건·문서가 바뀜
        private bool _jumpAfterSearch;       // 다음 계산이 끝나면 찾기 시작 위치 다음 결과로 이동
        private int _findAnchor;             // 입력하는 대로 찾기의 시작 위치(찾기를 연 곳)

        private void InitializeFind()
        {
            _searchTimer.Tick += async (s, e) =>
            {
                _searchTimer.Stop();
                await RunSearchAsync();
            };
            Editor.TextChanged += (s, e) =>
            {
                _searchStale = true;
                if (FindBar.Visibility == Visibility.Visible)
                    ScheduleSearch(false);
            };
        }

        private TextSearchOptions CurrentSearchOptions()
        {
            return new TextSearchOptions
            {
                Query = FindBox.Text,
                MatchCase = MatchCaseToggle.IsChecked == true,
                WholeWord = WholeWordToggle.IsChecked == true,
                UseRegex = RegexToggle.IsChecked == true,
            };
        }

        // ── 막대 열기·닫기 ────────────────────────────────────────────────

        private void OpenFind(bool replace)
        {
            if (_filePath == null || _tooLarge || _loading)
                return;
            GotoBar.Visibility = Visibility.Collapsed;
            FindBar.Visibility = Visibility.Visible;
            SetReplaceVisible(replace && !Editor.IsReadOnly);

            // 한 줄 안의 짧은 선택이면 검색어로 쓴다.
            var selection = Editor.TextArea.Selection;
            if (!selection.IsEmpty && selection.Length <= 200)
            {
                var selected = Editor.SelectedText;
                if (selected.IndexOf('\n') < 0 && selected.IndexOf('\r') < 0)
                    FindBox.Text = selected;
            }
            _findAnchor = selection.IsEmpty ? Editor.CaretOffset : selection.SurroundingSegment.Offset;

            if (replace && ReplaceBox.Visibility == Visibility.Visible && !string.IsNullOrEmpty(FindBox.Text))
            {
                ReplaceBox.Focus();
                ReplaceBox.SelectAll();
            }
            else
            {
                FindBox.Focus();
                FindBox.SelectAll();
            }
            ScheduleSearch(false);
        }

        private void SetReplaceVisible(bool visible)
        {
            ReplaceToggle.IsChecked = visible;
            ReplaceToggle.IsEnabled = !Editor.IsReadOnly;
            ReplaceBox.Visibility = ReplaceButtons.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ReplaceToggle_Click(object sender, RoutedEventArgs e)
        {
            SetReplaceVisible(ReplaceToggle.IsChecked == true && !Editor.IsReadOnly);
        }

        private void CloseBars_Click(object sender, RoutedEventArgs e)
        {
            CloseBars();
        }

        private void CloseBars()
        {
            FindBar.Visibility = Visibility.Collapsed;
            GotoBar.Visibility = Visibility.Collapsed;
            _searchRenderer.Matches = Array.Empty<TextMatch>();
            _searchRenderer.CurrentIndex = -1;
            Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
            Editor.TextArea.Focus();
        }

        /// <summary>다른 문서를 읽었다(다시 읽기·인코딩 바꿔 열기). 이전 결과를 버린다.</summary>
        private void OnDocumentReplacedForFind()
        {
            _searchVersion++;
            _matches = new List<TextMatch>();
            _searchStale = true;
            _searchRenderer.Matches = _matches;
            _searchRenderer.CurrentIndex = -1;
            if (FindBar.Visibility == Visibility.Visible)
                ScheduleSearch(false);
        }

        // ── 찾기 계산 ─────────────────────────────────────────────────────

        private void FindBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ScheduleSearch(true);
        }

        private void SearchOption_Click(object sender, RoutedEventArgs e)
        {
            ScheduleSearch(true);
        }

        private void ScheduleSearch(bool jump)
        {
            _searchStale = true;
            _jumpAfterSearch |= jump;
            var length = Editor.Document?.TextLength ?? 0;
            _searchTimer.Interval = TimeSpan.FromMilliseconds(length > 5 * 1024 * 1024 ? 300 : 100);
            _searchTimer.Stop();
            _searchTimer.Start();
        }

        /// <summary>입력이 멈춘 뒤 백그라운드에서 찾는다. 그 사이 다시 바뀌었으면 결과를 버린다.</summary>
        private async Task RunSearchAsync()
        {
            var version = ++_searchVersion;
            var options = CurrentSearchOptions();
            var regex = TextSearch.BuildRegex(options, out var error);
            if (regex == null || Editor.Document == null)
            {
                ApplySearchResult(regex, error, new List<TextMatch>(), false);
                return;
            }

            var snapshot = Editor.Document.CreateSnapshot();   // 편집과 무관하게 읽을 수 있는 복사본(빠름)
            bool truncated = false;
            var found = await Task.Run(() => TextSearch.FindAll(snapshot.Text, regex, out truncated));
            if (version != _searchVersion || _disposed)
                return;
            ApplySearchResult(regex, null, found, truncated);
        }

        /// <summary>F3·바꾸기처럼 바로 결과가 필요할 때: 오래된 결과면 지금 다시 찾는다(진행 중인 계산은 버림).</summary>
        private void EnsureSearchCurrent()
        {
            if (!_searchStale && !_searchTimer.IsEnabled)
                return;
            _searchTimer.Stop();
            _searchVersion++;
            var options = CurrentSearchOptions();
            var regex = TextSearch.BuildRegex(options, out var error);
            var truncated = false;
            var found = regex == null || Editor.Document == null
                ? new List<TextMatch>()
                : TextSearch.FindAll(Editor.Document.Text, regex, out truncated);
            ApplySearchResult(regex, error, found, truncated);
        }

        private void ApplySearchResult(Regex regex, string error, List<TextMatch> found, bool truncated)
        {
            _regex = regex;
            _regexError = error;
            _matches = found;
            _matchesTruncated = truncated;
            _searchStale = false;
            if (FindBar.Visibility == Visibility.Visible)
                _searchRenderer.Matches = found;

            if (_jumpAfterSearch && found.Count > 0)
            {
                _jumpAfterSearch = false;
                SelectMatch(TextSearch.NextIndex(found, _findAnchor), false);
                return;
            }
            _jumpAfterSearch = false;
            var selection = Editor.TextArea.Selection;
            _searchRenderer.CurrentIndex = selection.IsEmpty ? -1
                : TextSearch.IndexAt(found, selection.SurroundingSegment.Offset, selection.Length);
            UpdateMatchInfo();
            Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        }

        private void UpdateMatchInfo()
        {
            MatchInfoText.ToolTip = null;
            if (_regexError != null)
            {
                MatchInfoText.Text = "정규식 오류";
                MatchInfoText.ToolTip = _regexError;
                return;
            }
            if (string.IsNullOrEmpty(FindBox.Text))
            {
                MatchInfoText.Text = string.Empty;
                return;
            }
            if (_matches.Count == 0)
            {
                MatchInfoText.Text = "결과 없음";
                return;
            }
            var more = _matchesTruncated ? "+" : string.Empty;
            var current = _searchRenderer.CurrentIndex;
            MatchInfoText.Text = current >= 0
                ? string.Format("{0:N0} / {1:N0}{2}", current + 1, _matches.Count, more)
                : string.Format("{0:N0}개{1}", _matches.Count, more);
            if (_matchesTruncated)
                MatchInfoText.ToolTip = string.Format("결과가 많아 처음 {0:N0}개까지만 찾았습니다.", TextSearch.MaxMatches);
        }

        // ── 이동 ───────────────────────────────────────────────────────────

        private void FindNext_Click(object sender, RoutedEventArgs e)
        {
            FindStep(1);
        }

        private void FindPrevious_Click(object sender, RoutedEventArgs e)
        {
            FindStep(-1);
        }

        private void FindStep(int direction)
        {
            if (string.IsNullOrEmpty(FindBox.Text))
            {
                OpenFind(false);
                return;
            }
            EnsureSearchCurrent();
            if (_matches.Count == 0)
            {
                UpdateMatchInfo();
                return;
            }
            var selection = Editor.TextArea.Selection;
            var start = selection.IsEmpty ? Editor.CaretOffset : selection.SurroundingSegment.Offset;
            var index = direction > 0
                ? TextSearch.NextIndex(_matches, selection.IsEmpty ? start : start + 1)
                : TextSearch.PreviousIndex(_matches, start);
            SelectMatch(index, true);
        }

        /// <summary>결과로 이동해 선택한다. <paramref name="focusEditor"/>가 아니면 입력 포커스는 찾기 입력란에 둔다(입력하는 대로 찾기).</summary>
        private void SelectMatch(int index, bool focusEditor)
        {
            if (index < 0 || index >= _matches.Count)
                return;
            var match = _matches[index];
            Editor.Select(match.Offset, match.Length);
            var location = Editor.Document.GetLocation(match.Offset);
            Editor.ScrollTo(location.Line, location.Column);
            _findAnchor = match.Offset;
            _searchRenderer.CurrentIndex = index;
            if (FindBar.Visibility == Visibility.Visible)
                _searchRenderer.Matches = _matches;
            UpdateMatchInfo();
            Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
            if (focusEditor && FindBar.Visibility != Visibility.Visible)
                Editor.TextArea.Focus();
        }

        // ── 바꾸기 ─────────────────────────────────────────────────────────

        private void ReplaceOne_Click(object sender, RoutedEventArgs e)
        {
            ReplaceOne();
        }

        private void ReplaceAll_Click(object sender, RoutedEventArgs e)
        {
            ReplaceAll();
        }

        /// <summary>현재 선택이 결과면 바꾸고 다음 결과로, 아니면 다음 결과로 이동만 한다.</summary>
        private void ReplaceOne()
        {
            if (Editor.IsReadOnly || Editor.Document == null)
                return;
            EnsureSearchCurrent();
            if (_regex == null)
                return;

            var selection = Editor.TextArea.Selection;
            if (!selection.IsEmpty && selection.Segments is ICollection<ICSharpCode.AvalonEdit.Editing.SelectionSegment> segments && segments.Count == 1)
            {
                var start = selection.SurroundingSegment.Offset;
                var length = selection.Length;
                var replacement = TextSearch.ReplacementAt(Editor.Document.Text, start, length, _regex, CurrentSearchOptions(), ReplaceBox.Text);
                if (replacement != null)
                {
                    Editor.Document.Replace(start, length, replacement);
                    var after = start + replacement.Length;
                    Editor.Select(after, 0);
                    EnsureSearchCurrent();
                    if (_matches.Count > 0)
                        SelectMatch(TextSearch.NextIndex(_matches, after), false);
                    else
                        UpdateMatchInfo();
                    return;
                }
            }
            FindStep(1);
        }

        private void ReplaceAll()
        {
            if (Editor.IsReadOnly || Editor.Document == null)
                return;
            var options = CurrentSearchOptions();
            var regex = TextSearch.BuildRegex(options, out var error);
            if (regex == null)
            {
                _regexError = error;
                UpdateMatchInfo();
                return;
            }

            var document = Editor.Document;
            var replacements = TextSearch.BuildReplacements(document.Text, regex, options, ReplaceBox.Text, out var truncated);
            if (replacements.Count == 0)
            {
                MatchInfoText.Text = "결과 없음";
                return;
            }

            // 뒤에서부터 바꿔 앞 위치가 밀리지 않게 한다. 한 묶음이라 Ctrl+Z 한 번으로 되돌린다.
            document.BeginUpdate();
            try
            {
                for (int i = replacements.Count - 1; i >= 0; i--)
                    document.Replace(replacements[i].Key.Offset, replacements[i].Key.Length, replacements[i].Value);
            }
            finally
            {
                document.EndUpdate();
            }

            EnsureSearchCurrent();
            MatchInfoText.Text = string.Format("{0:N0}개 바꿈{1}", replacements.Count, truncated ? " (최대치 — 다시 누르면 계속)" : string.Empty);
        }

        private void FindBar_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var mods = Keyboard.Modifiers;

            if (key == Key.Enter)
            {
                if (ReferenceEquals(sender, ReplaceBox))
                {
                    if (mods == (ModifierKeys.Control | ModifierKeys.Alt))
                        ReplaceAll();
                    else
                        ReplaceOne();
                }
                else
                    FindStep(mods == ModifierKeys.Shift ? -1 : 1);
                e.Handled = true;
                return;
            }

            if (mods == ModifierKeys.Alt)
            {
                ToggleButton toggle = key == Key.C ? MatchCaseToggle : key == Key.W ? WholeWordToggle : key == Key.R ? RegexToggle : null;
                if (toggle != null)
                {
                    toggle.IsChecked = toggle.IsChecked != true;
                    ScheduleSearch(true);
                    e.Handled = true;
                }
            }
        }

        // ── 줄 이동 ────────────────────────────────────────────────────────

        private void OpenGoto()
        {
            if (_filePath == null || _tooLarge || _loading || Editor.Document == null)
                return;
            FindBar.Visibility = Visibility.Collapsed;
            _searchRenderer.Matches = Array.Empty<TextMatch>();
            Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
            GotoBar.Visibility = Visibility.Visible;
            GotoHint.Text = string.Format("1 – {0:N0}줄 (줄:열도 가능)", Editor.Document.LineCount);
            GotoBox.Text = Editor.TextArea.Caret.Line.ToString();
            GotoBox.Focus();
            GotoBox.SelectAll();
        }

        private void GotoBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;
            e.Handled = true;
            int line, column;
            if (!TryParseLineColumn(GotoBox.Text, out line, out column))
            {
                GotoHint.Text = "줄 번호를 숫자로 입력하세요 (예: 120 또는 120:5)";
                return;
            }
            var document = Editor.Document;
            line = Math.Max(1, Math.Min(line, document.LineCount));
            column = Math.Max(1, Math.Min(column, document.GetLineByNumber(line).Length + 1));
            CloseBars();
            Editor.TextArea.Caret.Position = new TextViewPosition(line, column);
            Editor.TextArea.ClearSelection();
            Editor.ScrollTo(line, column);
        }

        /// <summary><c>120</c> 또는 <c>120:5</c>(줄:열). 숫자 묶음 구분 쉼표는 무시한다.</summary>
        internal static bool TryParseLineColumn(string text, out int line, out int column)
        {
            line = column = 1;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            var parts = text.Replace(",", string.Empty).Trim().Split(':');
            if (parts.Length > 2 || !int.TryParse(parts[0].Trim(), out line))
                return false;
            if (parts.Length == 2 && !int.TryParse(parts[1].Trim(), out column))
                return false;
            return true;
        }
    }
}

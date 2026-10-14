using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Folderss.Controls;
using Folderss.Models;
using Folderss.Services;

namespace Folderss
{
    /// <summary>
    /// 폴더 비교 창(비모달). 좌우에 폴더 패널(<see cref="FolderBrowser"/>, 안전 모드)을 두고 가운데 분할선으로 너비를 조절한다.
    /// 각 패널은 자기 폴더의 실제 상태만 보이고 비교 표시(<see cref="CompareOverlay"/>)를 겹친다. 기준 아래에서 한쪽이 이동하면 반대쪽도 따라간다.
    /// 파일 작업(&gt;/&lt; 이동, 붙여넣기 복사, 삭제)은 모두 작업 이력(<see cref="FileOperationJournal"/>)을 거쳐 기록되고 최근부터 되돌린다.
    /// 이력에 남지 않는 작업(셸 우클릭 메뉴·끌어다 놓기·잘라내기)은 패널 안전 모드가 막는다.
    /// </summary>
    public sealed class DirectoryCompareWindow : Window
    {
        public static readonly string HistoryDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Folderss", "compare-history");

        // 작업 이력은 앱 전체에 하나 — 창마다 두면 같은 파일에 번갈아 쓰며 "최근 작업"이 창마다 달라진다. 파일 작업도 한 번에 하나.
        private static FileOperationJournal _journal;
        private static bool _journalBusy;
        private static event EventHandler JournalChanged;

        private readonly FolderBrowser _leftPanel = new FolderBrowser();
        private readonly FolderBrowser _rightPanel = new FolderBrowser();
        private readonly Action<string, string> _openFileCompare;
        private DirectoryCompareOptions _options;
        private CompareResult _result;
        private CancellationTokenSource _run;
        private FolderBrowser _activePanel;
        private bool _closed;
        private bool _syncing;
        private bool _following;
        private bool _skipCountConfirm;
        private string _notice;

        private readonly Button _compareButton = new Button { Content = "⟳ 비교", MinWidth = 70 };
        private readonly Button _cancelButton = new Button { Content = "■ 중지", MinWidth = 60, IsEnabled = false, Margin = new Thickness(6, 0, 0, 0) };
        private readonly CheckBox _hideSame = new CheckBox
        {
            Content = "차이만 보기",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            ToolTip = "같은 항목을 숨깁니다(양쪽 패널)"
        };
        private readonly ToggleButton _historyToggle = new ToggleButton { Content = "☰ 작업 이력", IsChecked = true, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
        private readonly TextBlock _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock _summary = new TextBlock { Margin = new Thickness(2, 6, 2, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly Grid _body = new Grid();
        private readonly ListBox _historyList = new ListBox();
        private readonly TextBox _historyDetail = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 140, FontSize = 12 };
        private readonly TextBlock _historyWarnings = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Visibility = Visibility.Collapsed };
        private readonly Button _undoButton = new Button { Content = "↩ 최근 작업 되돌리기", Margin = new Thickness(0, 6, 0, 0) };
        private readonly Button _skipButton = new Button { Content = "최근 작업 넘기기", Margin = new Thickness(0, 4, 0, 0), ToolTip = "되돌릴 수 없는 최근 작업을 되돌리지 않고 넘겨, 그 앞 작업을 되돌릴 수 있게 합니다(파일은 그대로)" };
        private readonly Button _moveRight = new Button { Content = "▶", ToolTip = "왼쪽에서 고른 항목을 오른쪽 폴더로 이동", Width = 30, Height = 34 };
        private readonly Button _moveLeft = new Button { Content = "◀", ToolTip = "오른쪽에서 고른 항목을 왼쪽 폴더로 이동", Width = 30, Height = 34, Margin = new Thickness(0, 6, 0, 0) };
        private ColumnDefinition _leftColumn;
        private ColumnDefinition _rightColumn;
        private ColumnDefinition _historySplitterColumn;
        private ColumnDefinition _historyColumn;
        private FrameworkElement _historyPanel;
        private FrameworkElement _historySplitter;

        /// <param name="openFileCompare">같은 이름의 파일 한 쌍을 두 파일 비교 창으로 연다(왼쪽, 오른쪽 전체 경로).</param>
        public DirectoryCompareWindow(string left, string right, Action<string, string> openFileCompare)
        {
            _openFileCompare = openFileCompare;
            _options = CompareSettingsService.Load();

            Width = 1500;
            Height = 820;
            MinWidth = 1000;
            MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            SetResourceReference(BackgroundProperty, "WindowBackground");
            SetResourceReference(ForegroundProperty, "PrimaryText");
            SetResourceReference(FontFamilyProperty, "AppFontFamily");
            FontSize = 13;

            var root = new DockPanel { Margin = new Thickness(8) };
            var toolbar = BuildToolbar();
            DockPanel.SetDock(toolbar, Dock.Top);
            root.Children.Add(toolbar);
            _summary.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            DockPanel.SetDock(_summary, Dock.Bottom);
            root.Children.Add(_summary);
            BuildBody();
            _body.Margin = new Thickness(0, 8, 0, 0);
            root.Children.Add(_body);
            Content = root;

            SetupPanel(_leftPanel);
            SetupPanel(_rightPanel);
            var fallback = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _leftPanel.Initialize(string.IsNullOrEmpty(left) ? fallback : left);
            _rightPanel.Initialize(string.IsNullOrEmpty(right) ? (string.IsNullOrEmpty(left) ? fallback : left) : right);
            _activePanel = _leftPanel;
            _leftPanel.SetActive(true);
            ApplyDisplayOptions();
            UpdateTitle();

            JournalChanged += OnJournalChanged;
            PreviewKeyDown += Window_PreviewKeyDown;
            Loaded += (s, e) =>
            {
                RefreshHistory();
                StartCompare();
            };
            Closed += (s, e) =>
            {
                _closed = true;
                _run?.Cancel();
                JournalChanged -= OnJournalChanged;
            };
        }

        private static FileOperationJournal Journal
        {
            get
            {
                if (_journal != null)
                    return _journal;
                _journal = new FileOperationJournal(HistoryDirectory, new WindowsRecycleBin());
                try
                {
                    _journal.Load();
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    _journal.LoadWarnings.Add("작업 이력을 읽지 못했습니다: " + ex.Message);
                }
                return _journal;
            }
        }

        // ── 화면 구성 ──────────────────────────────────────────────

        private DockPanel BuildToolbar()
        {
            var row = new DockPanel();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };

            _compareButton.ToolTip = "두 패널의 현재 폴더를 저장된 옵션으로 비교합니다 (F5, 옵션은 ▾)";
            _compareButton.Click += (s, e) => StartCompare();
            var options = new Button { ToolTip = "비교 옵션 (판정 기준·범위·표시·이동 확인)" };
            options.SetResourceReference(StyleProperty, "OptionArrowButton");
            options.Click += (s, e) => EditOptions();
            _cancelButton.Click += (s, e) => _run?.Cancel();
            _hideSame.SetResourceReference(ForegroundProperty, "PrimaryText");
            _hideSame.Click += (s, e) =>
            {
                _options.HideSame = _hideSame.IsChecked == true;
                SaveOptions();
                ApplyDisplayOptions();
            };
            var previous = new Button { Content = "▲ 이전 차이", Margin = new Thickness(12, 0, 0, 0), ToolTip = "선택한 패널에서 이전 차이 항목" };
            previous.Click += (s, e) => SelectDifference(false);
            var next = new Button { Content = "▼ 다음 차이", Margin = new Thickness(6, 0, 0, 0), ToolTip = "선택한 패널에서 다음 차이 항목" };
            next.Click += (s, e) => SelectDifference(true);
            var swap = new Button { Content = "⇄ 좌우 바꾸기", Margin = new Thickness(12, 0, 0, 0), ToolTip = "두 패널의 폴더를 맞바꿔 다시 비교합니다" };
            swap.Click += (s, e) => Swap();
            _historyToggle.SetResourceReference(StyleProperty, "CompactToggleButtonStyle");
            _historyToggle.Click += (s, e) => SetHistoryVisible(_historyToggle.IsChecked == true);

            foreach (var element in new UIElement[] { _compareButton, options, _cancelButton, _hideSame, previous, next, swap, _historyToggle })
                buttons.Children.Add(element);
            DockPanel.SetDock(buttons, Dock.Left);
            row.Children.Add(buttons);
            row.Children.Add(_status);
            return row;
        }

        private void BuildBody()
        {
            _leftColumn = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 200 };
            _rightColumn = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 200 };
            _historySplitterColumn = new ColumnDefinition { Width = new GridLength(6) };
            _historyColumn = new ColumnDefinition { Width = new GridLength(300), MinWidth = 180 };
            _body.ColumnDefinitions.Add(_leftColumn);
            _body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _body.ColumnDefinitions.Add(_rightColumn);
            _body.ColumnDefinitions.Add(_historySplitterColumn);
            _body.ColumnDefinitions.Add(_historyColumn);

            Grid.SetColumn(_leftPanel, 0);
            _body.Children.Add(_leftPanel);

            // 가운데 분할선: 끌어서 좌우 너비 조절, 두 번 누르면 반반. 이동 버튼은 분할선 위에 얹는다.
            var splitter = new GridSplitter
            {
                Width = 40,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                ResizeDirection = GridResizeDirection.Columns,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                ToolTip = "끌어서 좌우 너비 조절 · 두 번 누르면 반반"
            };
            splitter.SetResourceReference(BackgroundProperty, "SurfaceBackground");
            splitter.MouseDoubleClick += (s, e) =>
            {
                _leftColumn.Width = new GridLength(1, GridUnitType.Star);
                _rightColumn.Width = new GridLength(1, GridUnitType.Star);
            };
            Grid.SetColumn(splitter, 1);
            _body.Children.Add(splitter);
            var moveButtons = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            _moveRight.Click += (s, e) => MoveSelected(fromLeft: true);
            _moveLeft.Click += (s, e) => MoveSelected(fromLeft: false);
            moveButtons.Children.Add(_moveRight);
            moveButtons.Children.Add(_moveLeft);
            Grid.SetColumn(moveButtons, 1);
            _body.Children.Add(moveButtons);

            Grid.SetColumn(_rightPanel, 2);
            _body.Children.Add(_rightPanel);

            var historySplitter = new GridSplitter
            {
                Width = 6,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ResizeDirection = GridResizeDirection.Columns,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext
            };
            historySplitter.SetResourceReference(BackgroundProperty, "BorderBrush");
            Grid.SetColumn(historySplitter, 3);
            _body.Children.Add(historySplitter);
            _historySplitter = historySplitter;

            _historyPanel = BuildHistoryPanel();
            Grid.SetColumn(_historyPanel, 4);
            _body.Children.Add(_historyPanel);
        }

        private FrameworkElement BuildHistoryPanel()
        {
            var border = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(8) };
            border.SetResourceReference(Border.BackgroundProperty, "PanelBackground");
            border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            var panel = new DockPanel();

            var header = new TextBlock { Text = "작업 이력", FontWeight = FontWeights.SemiBold };
            var hint = new TextBlock
            {
                Text = "이 창에서 한 이동·복사·삭제. 최근 작업부터 되돌립니다. 7일 보관.",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 6)
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            _historyWarnings.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0x6C, 0x75));
            var top = new StackPanel();
            top.Children.Add(header);
            top.Children.Add(hint);
            top.Children.Add(_historyWarnings);
            DockPanel.SetDock(top, Dock.Top);
            panel.Children.Add(top);

            var bottom = new StackPanel();
            _undoButton.Click += (s, e) => UndoLatest();
            _skipButton.Click += (s, e) => SkipLatest();
            bottom.Children.Add(_historyDetail);
            bottom.Children.Add(_undoButton);
            bottom.Children.Add(_skipButton);
            DockPanel.SetDock(bottom, Dock.Bottom);
            panel.Children.Add(bottom);

            _historyList.SelectionChanged += (s, e) => ShowHistoryDetail();
            _historyList.Margin = new Thickness(0, 0, 0, 6);
            panel.Children.Add(_historyList);
            border.Child = panel;
            return border;
        }

        private void SetHistoryVisible(bool visible)
        {
            _historyPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            _historySplitter.Visibility = _historyPanel.Visibility;
            _historyColumn.MinWidth = visible ? 180 : 0;
            _historyColumn.Width = visible ? new GridLength(300) : new GridLength(0);
            _historySplitterColumn.Width = visible ? new GridLength(6) : new GridLength(0);
        }

        private void SetupPanel(FolderBrowser panel)
        {
            panel.SafeMode = true;
            panel.MinWidth = 200;
            panel.Activated += (s, e) => SetActivePanel(panel);
            panel.PathChanged += Panel_PathChanged;
            panel.SelectedItemsChanged += Panel_SelectedItemsChanged;
            panel.FileOpenRequested += (s, path) => OpenFile(panel, path);
            panel.PasteRequested += (s, e) => PasteInto(panel);
            panel.ContextMenuRequested += (s, e) => ShowPanelMenu(panel);
        }

        private void SetActivePanel(FolderBrowser panel)
        {
            if (ReferenceEquals(_activePanel, panel))
                return;
            _activePanel = panel;
            _leftPanel.SetActive(ReferenceEquals(panel, _leftPanel));
            _rightPanel.SetActive(ReferenceEquals(panel, _rightPanel));
        }

        private FolderBrowser Other(FolderBrowser panel) => ReferenceEquals(panel, _leftPanel) ? _rightPanel : _leftPanel;

        private bool IsLeft(FolderBrowser panel) => ReferenceEquals(panel, _leftPanel);

        private void UpdateTitle()
        {
            Title = "폴더 비교 - " + DisplayName(_leftPanel.CurrentPath) + " ↔ " + DisplayName(_rightPanel.CurrentPath);
        }

        private static string DisplayName(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "(선택 안 함)";
            var name = Path.GetFileName(path.TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(name) ? path : name;
        }

        // ── 비교와 표시 ────────────────────────────────────────────

        private async void StartCompare()
        {
            var left = _leftPanel.CurrentPath;
            var right = _rightPanel.CurrentPath;
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
                return;
            if (string.Equals(Path.GetFullPath(left).TrimEnd('\\', '/'), Path.GetFullPath(right).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                _status.Text = "양쪽이 같은 폴더입니다. 한쪽 패널의 경로를 바꾼 뒤 비교하세요.";
                return;
            }

            _run?.Cancel();
            var run = new CancellationTokenSource();
            _run = run;
            _compareButton.IsEnabled = false;
            _cancelButton.IsEnabled = true;
            _status.Text = "비교 중…";
            var progress = new Progress<int>(folders =>
            {
                if (ReferenceEquals(_run, run))
                    _status.Text = "비교 중… 폴더 " + folders.ToString("N0") + "개";
            });

            try
            {
                var result = await DirectoryCompareService.CompareAsync(left, right, _options.Clone(), progress, run.Token);
                if (!ReferenceEquals(_run, run) || _closed)
                    return;
                _result = result;
                _notice = null;
                _status.Text = Summarize(result);
                UpdateTitle();
                ApplyOverlays();
            }
            catch (Exception ex)
            {
                if (ReferenceEquals(_run, run) && !_closed)
                    _status.Text = "비교하지 못했습니다: " + ex.Message;
            }
            finally
            {
                if (ReferenceEquals(_run, run))
                {
                    _compareButton.IsEnabled = true;
                    _cancelButton.IsEnabled = false;
                    _run = null;
                }
                run.Dispose();
            }
        }

        public static string Summarize(CompareResult result)
        {
            var text = string.Format("같음 {0:N0} · 다름 {1:N0} · 왼쪽만 {2:N0} · 오른쪽만 {3:N0}",
                result.SameCount, result.DifferentCount, result.LeftOnlyCount, result.RightOnlyCount);
            if (result.TypeMismatchCount > 0)
                text += string.Format(" · 유형 다름 {0:N0}", result.TypeMismatchCount);
            if (result.UnknownCount > 0)
                text += string.Format(" · 비교 불가 {0:N0}", result.UnknownCount);
            if (!result.IsComplete)
                text += "  (중지됨 — 일부 결과)";
            else if (!result.HasDifferences && result.UnknownCount == 0)
                text += "  — 차이 없음";
            return text;
        }

        public static string DescribeOptions(DirectoryCompareOptions options)
        {
            var parts = new List<string>
            {
                options.UseContentHash
                    ? "기준: 내용 해시"
                    : "기준: 크기·수정 시각(±" + options.TimestampTolerance.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture) + "초)",
                options.ApplyIgnoreRules ? "ignore 규칙 적용" : "ignore 규칙 무시",
                !options.Recursive ? "하위 폴더 안 봄" : options.MaxDepth > 0 ? "하위 폴더 " + options.MaxDepth + "단계까지" : "하위 폴더 전체",
                options.IncludeHidden ? "숨김 파일 포함" : "숨김 파일 제외"
            };
            return string.Join(" · ", parts);
        }

        /// <summary>두 패널에 현재 폴더 기준의 비교 표시를 다시 입힌다. 기준 밖이면 표시를 뺀다.</summary>
        private void ApplyOverlays()
        {
            ApplyOverlay(_leftPanel, CompareSide.Left);
            ApplyOverlay(_rightPanel, CompareSide.Right);
            UpdateSummary();
        }

        private void ApplyOverlay(FolderBrowser panel, CompareSide side)
        {
            var map = CompareOverlay.EntriesFor(_result, side, panel.CurrentPath);
            if (map == null)
            {
                panel.SetCompareOverlay(null);
                return;
            }
            var directionColors = _options.DirectionColors;
            var tolerance = _result.Options?.TimestampTolerance ?? _options.TimestampTolerance;
            panel.SetCompareOverlay(item =>
            {
                map.TryGetValue(item.Name, out var entry);
                var marker = CompareOverlay.Describe(entry, side, directionColors, tolerance);
                item.CompareMark = marker.Mark;
                item.CompareText = marker.Text;
                item.CompareTip = marker.Tip;
            });
        }

        private void ApplyDisplayOptions()
        {
            _hideSame.IsChecked = _options.HideSame;
            foreach (var panel in new[] { _leftPanel, _rightPanel })
            {
                panel.HideCompareSame = _options.HideSame;
                panel.SetTreeViewVisible(_options.ShowFolderTree);
                panel.SetPreviewVisible(_options.ShowPreview);
            }
            if (_result != null)
                ApplyOverlays();
        }

        private void UpdateSummary()
        {
            if (_result == null)
            {
                _summary.Text = _notice ?? string.Empty;
                return;
            }
            var relative = CompareOverlay.GetRelativeFolder(_result.LeftRoot, _leftPanel.CurrentPath);
            var text = "기준: " + _result.LeftRoot + "  ↔  " + _result.RightRoot
                       + (relative == null ? string.Empty : "  ·  현재: \\" + relative)
                       + "  ·  " + DescribeOptions(_result.Options ?? _options);
            _summary.Text = _notice == null ? text : _notice + "  ·  " + text;
        }

        private void Panel_PathChanged(object sender, EventArgs e)
        {
            var panel = (FolderBrowser)sender;
            if (!_syncing && _result != null)
            {
                var fromLeft = IsLeft(panel);
                var relative = CompareOverlay.GetRelativeFolder(fromLeft ? _result.LeftRoot : _result.RightRoot, panel.CurrentPath);
                if (relative == null)
                {
                    _notice = "⚠ 비교 기준 밖 — [비교]를 누르면 두 패널의 현재 폴더로 다시 비교합니다";
                }
                else
                {
                    var otherRoot = fromLeft ? _result.RightRoot : _result.LeftRoot;
                    var target = relative.Length == 0 ? otherRoot : Path.Combine(otherRoot, relative);
                    var other = Other(panel);
                    if (Directory.Exists(target))
                    {
                        _notice = null;
                        if (!string.Equals(other.CurrentPath?.TrimEnd('\\', '/'), target.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                        {
                            _syncing = true;
                            try { other.NavigateTo(target); }
                            finally { _syncing = false; }
                        }
                    }
                    else
                    {
                        _notice = "⚠ 반대편에 이 폴더가 없습니다: \\" + relative;
                    }
                }
            }
            ApplyOverlays();
            UpdateTitle();
        }

        private void Panel_SelectedItemsChanged(object sender, EventArgs e)
        {
            var panel = (FolderBrowser)sender;
            if (_following || !panel.IsKeyboardFocusWithin)
                return;
            var selected = panel.SelectedItems;
            if (selected.Count != 1)
                return;
            FollowSelection(panel, selected[0].Name);
        }

        /// <summary>반대쪽 패널에서 같은 이름을 골라 보이게 한다(동작 대상은 고른 쪽만).</summary>
        private void FollowSelection(FolderBrowser panel, string name)
        {
            _following = true;
            try { Other(panel).SelectByName(name); }
            finally { _following = false; }
        }

        private void SelectDifference(bool forward)
        {
            var panel = _activePanel ?? _leftPanel;
            if (!panel.SelectNextDifference(forward))
            {
                _status.Text = forward ? "아래쪽에 차이 항목이 없습니다." : "위쪽에 차이 항목이 없습니다.";
                return;
            }
            var selected = panel.SelectedItem;
            if (selected != null)
                FollowSelection(panel, selected.Name);
            panel.FocusFileList();
        }

        private void Swap()
        {
            var left = _leftPanel.CurrentPath;
            var right = _rightPanel.CurrentPath;
            _syncing = true;
            try
            {
                _leftPanel.NavigateTo(right);
                _rightPanel.NavigateTo(left);
            }
            finally
            {
                _syncing = false;
            }
            _result = null;
            ApplyOverlays();
            StartCompare();
        }

        private void EditOptions()
        {
            var dialog = new DirectoryCompareOptionsDialog(_options) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;
            var before = _options;
            _options = dialog.Result;
            SaveOptions();
            ApplyDisplayOptions();
            if (CriteriaChanged(before, _options))
                StartCompare();
        }

        private static bool CriteriaChanged(DirectoryCompareOptions a, DirectoryCompareOptions b)
        {
            return a.UseContentHash != b.UseContentHash || a.TimestampTolerance != b.TimestampTolerance ||
                   a.ApplyIgnoreRules != b.ApplyIgnoreRules || a.Recursive != b.Recursive ||
                   a.MaxDepth != b.MaxDepth || a.IncludeHidden != b.IncludeHidden;
        }

        private void SaveOptions()
        {
            try
            {
                CompareSettingsService.Save(_options);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "비교 옵션을 저장하지 못했습니다. 이번 창에만 적용합니다.\n\n" + ex.Message, "폴더 비교",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ── 파일 열기·메뉴·키 ──────────────────────────────────────

        /// <summary>반대쪽 현재 폴더에 같은 이름 파일이 있으면 두 파일 비교, 없으면 기본 프로그램으로 연다.</summary>
        private void OpenFile(FolderBrowser panel, string path)
        {
            var counterpart = CounterpartFile(panel, path);
            if (counterpart != null && _openFileCompare != null)
            {
                if (IsLeft(panel))
                    _openFileCompare(path, counterpart);
                else
                    _openFileCompare(counterpart, path);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "파일을 열 수 없습니다", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string CounterpartFile(FolderBrowser panel, string path)
        {
            var otherFolder = Other(panel).CurrentPath;
            if (string.IsNullOrEmpty(otherFolder))
                return null;
            var candidate = Path.Combine(otherFolder, Path.GetFileName(path));
            return File.Exists(candidate) ? candidate : null;
        }

        private void ShowPanelMenu(FolderBrowser panel)
        {
            var selected = panel.SelectedItems.Select(item => item.FullPath).ToList();
            var menu = new ContextMenu { PlacementTarget = panel };

            if (selected.Count > 0)
            {
                Add(menu, IsLeft(panel) ? "▶ 오른쪽 폴더로 이동" : "◀ 왼쪽 폴더로 이동", () => MoveSelected(IsLeft(panel)));
                Add(menu, "복사 (Ctrl+C)", panel.CopySelectedToClipboard);
            }
            Add(menu, "붙여넣기 (Ctrl+V) — 복사", () => PasteInto(panel), ClipboardService.ContainsFileDropList());
            if (selected.Count > 0)
            {
                Add(menu, "삭제 — 휴지통 (Delete)", () => DeleteSelected(panel, permanently: false));
                menu.Items.Add(new Separator());
                if (selected.Count == 1 && File.Exists(selected[0]) && CounterpartFile(panel, selected[0]) != null)
                    Add(menu, "반대쪽 같은 이름 파일과 비교", () => OpenFile(panel, selected[0]));
                var pair = panel.GetSelectedFilePair();
                if (pair != null && _openFileCompare != null)
                    Add(menu, "선택한 두 파일 비교", () => _openFileCompare(pair[0], pair[1]));
                Add(menu, "경로 복사", () => ClipboardService.TrySetDataObject(string.Join(Environment.NewLine, selected)));
                Add(menu, "탐색기에서 위치 열기", () => Reveal(selected[0], select: true));
            }
            else
            {
                Add(menu, "탐색기에서 이 폴더 열기", () => Reveal(panel.CurrentPath, select: false));
            }
            menu.IsOpen = true;
        }

        private static void Add(ContextMenu menu, string header, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            item.Click += (s, e) => action();
            menu.Items.Add(item);
        }

        private void Reveal(string path, bool select)
        {
            try
            {
                var arguments = select ? "/select,\"" + path + "\"" : "\"" + path + "\"";
                Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "탐색기로 열기", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None)
            {
                StartCompare();
                e.Handled = true;
                return;
            }
            if (e.Key != Key.Delete || Keyboard.FocusedElement is TextBoxBase)
                return;
            var panel = _leftPanel.IsKeyboardFocusWithin ? _leftPanel : _rightPanel.IsKeyboardFocusWithin ? _rightPanel : null;
            if (panel == null)
                return;
            DeleteSelected(panel, permanently: (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            e.Handled = true;
        }

        // ── 파일 작업(모두 작업 이력을 거친다) ─────────────────────

        private bool IsPinLocked(IEnumerable<string> sources, string destination)
        {
            var owner = Owner as MainWindow;
            if (owner == null)
                return false;
            return (destination != null && owner.IsDestinationPinLocked(destination)) ||
                   (sources != null && sources.Any(owner.IsPathPinLocked));
        }

        private async void MoveSelected(bool fromLeft)
        {
            var source = fromLeft ? _leftPanel : _rightPanel;
            var target = Other(source);
            var paths = source.SelectedItems.Select(item => item.FullPath).Where(Exists).ToList();
            var destination = target.CurrentPath;
            if (paths.Count == 0)
            {
                _status.Text = (fromLeft ? "왼쪽" : "오른쪽") + " 패널에서 이동할 항목을 고르세요.";
                return;
            }
            if (string.IsNullOrEmpty(destination) || !Directory.Exists(destination))
                return;
            if (string.Equals(source.CurrentPath?.TrimEnd('\\', '/'), destination.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                _status.Text = "양쪽이 같은 폴더라 이동할 수 없습니다.";
                return;
            }
            if (IsPinLocked(paths, destination))
            {
                MainWindow.ShowPinLockedMessage();
                return;
            }

            var folders = paths.Where(Directory.Exists).ToList();
            if (folders.Count > 0 && !_skipCountConfirm)
            {
                var threshold = _options.MoveConfirmThreshold;
                var count = FileOperationJournal.CountFiles(folders, Math.Max(threshold, 1));
                if (count >= threshold)
                {
                    var dialog = new CompareConfirmDialog("폴더 이동 확인",
                        string.Format("폴더 {0}개(하위 파일 {1}개{2})를 {3}로 이동합니다.", folders.Count, count.ToString("N0"),
                            count >= Math.Max(threshold, 1) ? " 이상" : string.Empty, destination),
                        folders.Select(Path.GetFileName).ToList(), null,
                        "확인하였습니다 — 이 창을 닫을 때까지 폴더 이동 개수 확인을 다시 묻지 않음", required: false, okText: "이동")
                    { Owner = this };
                    if (dialog.ShowDialog() != true)
                        return;
                    if (dialog.Checked)
                        _skipCountConfirm = true;
                }
            }

            var conflicts = FileOperationJournal.FindConflicts(paths, destination);
            if (conflicts.Count > 0)
            {
                var lines = conflicts.Select(conflict => DescribeConflict(conflict, paths)).ToList();
                // 교체 확인은 "확인하였습니다"로 생략하지 않는다 — 대상 항목이 휴지통으로 가는 경고다.
                var dialog = new CompareConfirmDialog("같은 이름 교체 확인",
                    string.Format("{0}에 같은 이름 {1}개가 있습니다. 교체하면 대상의 옛 항목은 휴지통으로 보내고, 작업 이력에서 되돌릴 수 있습니다.",
                        destination, conflicts.Count),
                    lines, "휴지통이 없는 위치(네트워크·이동식 드라이브)는 교체하지 않고 그 항목만 실패로 남깁니다.", null, required: false, okText: "교체하며 이동")
                { Owner = this };
                if (dialog.ShowDialog() != true)
                    return;
            }

            JournalOperation op = null;
            if (await RunJournalAsync(() => op = Journal.Move(paths, destination), "이동"))
                ReportItemErrors(op, "이동");
        }

        private static string DescribeConflict(string conflict, IList<string> sources)
        {
            var name = Path.GetFileName(conflict);
            if (Directory.Exists(conflict))
                return name + "  — 폴더: 대상 폴더 안의 항목도 모두 휴지통으로";
            var source = sources.FirstOrDefault(path => string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase));
            if (source != null && File.Exists(source) && File.GetLastWriteTimeUtc(conflict) > File.GetLastWriteTimeUtc(source))
                return name + "  — ⚠ 대상이 더 최신";
            return name;
        }

        private async void PasteInto(FolderBrowser panel)
        {
            if (!ClipboardService.TryGetFileDropList(out var dropList) || dropList == null || dropList.Count == 0)
                return;
            var files = dropList.Cast<string>().ToList();
            if ((Owner as MainWindow)?.IsCutPending(files) == true || IsExplorerCut())
            {
                MessageBox.Show(this, "잘라낸 항목은 폴더 비교 창에 붙여넣지 않습니다(원본 삭제가 작업 이력 밖에서 일어나기 때문).\n복사(Ctrl+C) 후 붙여넣거나, 비교 창의 ▶ ◀ 이동을 쓰세요.",
                    "붙여넣기", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var destination = panel.CurrentPath;
            if (string.IsNullOrEmpty(destination) || !Directory.Exists(destination))
                return;
            if (IsPinLocked(null, destination))
            {
                MainWindow.ShowPinLockedMessage();
                return;
            }

            JournalOperation op = null;
            if (await RunJournalAsync(() => op = Journal.Copy(files, destination), "복사"))
                ReportItemErrors(op, "복사");
        }

        /// <summary>탐색기에서 잘라낸 클립보드인가("Preferred DropEffect"에 이동 비트). 읽지 못하면 복사로 본다(복사는 원본을 바꾸지 않는다).</summary>
        private static bool IsExplorerCut()
        {
            try
            {
                if (Clipboard.GetData("Preferred DropEffect") is MemoryStream stream && stream.Length >= 4)
                {
                    var bytes = new byte[4];
                    stream.Read(bytes, 0, 4);
                    return (BitConverter.ToInt32(bytes, 0) & (int)DragDropEffects.Move) != 0;
                }
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
            }
            return false;
        }

        private async void DeleteSelected(FolderBrowser panel, bool permanently)
        {
            var paths = panel.SelectedItems.Select(item => item.FullPath).Where(Exists).ToList();
            if (paths.Count == 0)
                return;
            if (IsPinLocked(paths, null))
            {
                MainWindow.ShowPinLockedMessage();
                return;
            }

            var names = paths.Select(Path.GetFileName).ToList();
            var dialog = permanently
                ? new CompareConfirmDialog("영구 삭제", string.Format("{0}개 항목을 영구 삭제합니다.", paths.Count), names,
                    "휴지통을 거치지 않아 작업 이력에서 되돌릴 수 없습니다.", "되돌릴 수 없다는 것을 이해했습니다", required: true, okText: "영구 삭제")
                : new CompareConfirmDialog("삭제", string.Format("{0}개 항목을 휴지통으로 보냅니다. 작업 이력에서 되돌릴 수 있습니다.", paths.Count), names,
                    null, null, required: false, okText: "휴지통으로");
            dialog.Owner = this;
            if (dialog.ShowDialog() != true)
                return;

            JournalOperation op = null;
            if (await RunJournalAsync(() => op = Journal.Delete(paths, permanently), "삭제"))
                ReportItemErrors(op, "삭제");
        }

        /// <summary>작업 이력 작업을 백그라운드에서 한 번에 하나만 실행하고, 끝나면 패널·비교·이력을 새로 고친다.</summary>
        private async Task<bool> RunJournalAsync(Action action, string title)
        {
            if (_journalBusy)
            {
                MessageBox.Show(this, "다른 파일 작업이 진행 중입니다. 끝난 뒤 다시 시도하세요.", title, MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
            _journalBusy = true;
            JournalChanged?.Invoke(this, EventArgs.Empty);
            _status.Text = title + " 중…";
            try
            {
                await Task.Run(action);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, title + "하지 못했습니다.\n\n" + ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            finally
            {
                _journalBusy = false;
                JournalChanged?.Invoke(this, EventArgs.Empty);
                if (!_closed)
                {
                    _leftPanel.RefreshItems();
                    _rightPanel.RefreshItems();
                    StartCompare();
                }
            }
        }

        private void ReportItemErrors(JournalOperation op, string title)
        {
            var failed = op?.Items.Where(item => !item.Succeeded).ToList();
            if (failed == null || failed.Count == 0 || _closed)
                return;
            MessageBox.Show(this,
                string.Format("{0}개 항목을 {1}하지 못했습니다.\n\n{2}", failed.Count, title,
                    string.Join("\n", failed.Take(10).Select(item => Path.GetFileName(item.Source) + ": " + item.Error))),
                title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

        // ── 작업 이력 패널 ─────────────────────────────────────────

        private void OnJournalChanged(object sender, EventArgs e) => RefreshHistory();

        private void RefreshHistory()
        {
            var journal = Journal;
            var selectedId = (_historyList.SelectedItem as ListBoxItem)?.Tag is JournalOperation selected ? selected.Id : null;
            _historyList.Items.Clear();
            if (_journalBusy)
            {
                _historyList.Items.Add(new ListBoxItem { Content = "파일 작업 중…", IsEnabled = false });
            }
            else
            {
                foreach (var op in journal.Operations.Reverse())
                {
                    var item = new ListBoxItem { Content = DescribeOperation(op), Tag = op };
                    _historyList.Items.Add(item);
                    if (op.Id == selectedId)
                        _historyList.SelectedItem = item;
                }
            }

            _historyWarnings.Text = string.Join("\n", journal.LoadWarnings);
            _historyWarnings.Visibility = journal.LoadWarnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            var target = _journalBusy ? null : journal.UndoTarget;
            _undoButton.IsEnabled = target != null;
            _skipButton.IsEnabled = target != null;
            _undoButton.ToolTip = target == null ? "되돌릴 작업이 없습니다" : "되돌리기: " + DescribeOperation(target);
            _moveRight.IsEnabled = _moveLeft.IsEnabled = !_journalBusy;
            ShowHistoryDetail();
        }

        private static string DescribeOperation(JournalOperation op)
        {
            var kind = op.Kind == JournalOperationKind.Move ? "이동"
                : op.Kind == JournalOperationKind.Copy ? "복사"
                : op.Kind == JournalOperationKind.Delete ? "삭제(휴지통)"
                : "영구 삭제";
            var failed = op.Items.Count(item => !item.Succeeded);
            var state = op.State == JournalOperationState.Undone ? "  ↩ 되돌림"
                : op.State == JournalOperationState.Skipped ? "  (넘김)"
                : op.State == JournalOperationState.Interrupted ? "  ⚠ 중단됨 — 확인 필요"
                : op.Kind == JournalOperationKind.DeletePermanently ? "  (되돌리기 불가)"
                : string.Empty;
            var where = op.Items.Select(item => item.Destination ?? item.Source).FirstOrDefault(path => path != null);
            return string.Format("{0:MM-dd HH:mm:ss}  {1} {2}개{3}{4}\n{5}",
                op.StartedUtc.ToLocalTime(), kind, op.Items.Count,
                failed > 0 ? " (실패 " + failed + ")" : string.Empty, state,
                where == null ? string.Empty : Path.GetDirectoryName(where));
        }

        private void ShowHistoryDetail()
        {
            if (!((_historyList.SelectedItem as ListBoxItem)?.Tag is JournalOperation op))
            {
                _historyDetail.Text = "항목을 고르면 자세한 내용이 보입니다.";
                return;
            }
            var lines = op.Items.Select(item =>
            {
                var mark = !item.Succeeded ? "✗" : item.Reverted ? "↩" : "✓";
                var text = mark + " " + item.Source;
                if (item.Destination != null)
                    text += "\n    → " + item.Destination;
                if (item.ReplacedPath != null)
                    text += "\n    (교체: 옛 항목을 휴지통으로)";
                if (item.Error != null)
                    text += "\n    " + item.Error;
                return text;
            });
            _historyDetail.Text = string.Join("\n", lines) + (op.UndoError != null ? "\n되돌리기 오류: " + op.UndoError : string.Empty);
        }

        private async void UndoLatest()
        {
            var target = Journal.UndoTarget;
            if (target == null || _journalBusy)
                return;
            var problems = Journal.CheckUndo(target);
            if (problems.Count > 0)
            {
                MessageBox.Show(this,
                    "이 작업은 되돌릴 수 없습니다(아무것도 바꾸지 않았습니다).\n\n" + string.Join("\n", problems.Take(10))
                    + "\n\n[최근 작업 넘기기]로 이 작업을 넘기면 그 앞 작업을 되돌릴 수 있습니다.",
                    "되돌리기", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (MessageBox.Show(this, "다음 작업을 되돌립니다.\n\n" + DescribeOperation(target), "되돌리기",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK)
                return;
            await RunJournalAsync(() => Journal.Undo(target), "되돌리기");
        }

        private void SkipLatest()
        {
            var target = Journal.UndoTarget;
            if (target == null || _journalBusy)
                return;
            if (MessageBox.Show(this, "다음 작업을 되돌리지 않고 넘깁니다(파일은 그대로). 넘기면 이 작업은 다시 되돌릴 수 없습니다.\n\n" + DescribeOperation(target),
                    "최근 작업 넘기기", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK)
                return;
            try
            {
                Journal.Skip(target);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                MessageBox.Show(this, "작업 이력에 기록하지 못했습니다.\n\n" + ex.Message, "최근 작업 넘기기", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            JournalChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 폴더 비교 창의 확인 대화상자. 목록(앞 20개)과 경고, 선택 체크를 담는다.
    /// <paramref name="required"/>면 체크해야 확인 버튼이 켜진다(영구 삭제 등 되돌릴 수 없는 선택).
    /// </summary>
    public sealed class CompareConfirmDialog : GitDialogBase
    {
        private readonly CheckBox _check;

        public bool Checked => _check?.IsChecked == true;

        public CompareConfirmDialog(string title, string message, IList<string> items, string warning, string checkText, bool required, string okText)
            : base(title, 520)
        {
            AddText(message);
            if (items != null && items.Count > 0)
            {
                var list = string.Join("\n", items.Take(20).Select(item => "  " + item))
                           + (items.Count > 20 ? "\n  … 외 " + (items.Count - 20) + "개" : string.Empty);
                AddText(list, secondary: true, top: 8);
            }
            if (warning != null)
                AddText("⚠ " + warning, warning: true, top: 8);
            if (checkText != null)
                _check = AddCheck(checkText, false);
            var ok = AddButtons(okText);
            if (required && _check != null)
            {
                ok.IsEnabled = false;
                _check.Checked += (s, e) => ok.IsEnabled = true;
                _check.Unchecked += (s, e) => ok.IsEnabled = false;
            }
        }
    }

    /// <summary>폴더 비교 옵션. 처음 값은 권장값(가장 안전한 쪽)이다.</summary>
    public sealed class DirectoryCompareOptionsDialog : GitDialogBase
    {
        private readonly RadioButton _byTime;
        private readonly RadioButton _byHash;
        private readonly TextBox _tolerance;
        private readonly CheckBox _ignore;
        private readonly CheckBox _recursive;
        private readonly TextBox _maxDepth;
        private readonly CheckBox _hidden;
        private readonly RadioButton _statusColors;
        private readonly RadioButton _directionColors;
        private readonly CheckBox _tree;
        private readonly CheckBox _preview;
        private readonly CheckBox _hideSame;
        private readonly TextBox _threshold;

        public DirectoryCompareOptions Result { get; private set; }

        public DirectoryCompareOptionsDialog(DirectoryCompareOptions current) : base("폴더 비교 옵션", 480)
        {
            current = current ?? new DirectoryCompareOptions();

            AddLabel("판정 기준");
            _byTime = AddRadio("criteria", "크기와 수정 시각 (권장)", "빠릅니다. 크기가 같고 수정 시각 차이가 허용 오차 안이면 같음.", !current.UseContentHash);
            _byHash = AddRadio("criteria", "내용 해시 (SHA-256)", "크기가 같은 파일의 내용을 모두 읽어 비교합니다. 정확하지만 느립니다. 수정 시각은 보지 않습니다.", current.UseContentHash);
            _tolerance = AddNumber("수정 시각 허용 오차(초)", current.TimestampTolerance.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture),
                "FAT·네트워크 드라이브는 시각을 2초 단위로 저장해 기본 2초입니다.");

            AddLabel("범위");
            _ignore = AddCheck(".gitignore / .folderssignore 규칙에 걸리는 항목 제외 (권장)", current.ApplyIgnoreRules);
            _recursive = AddCheck("하위 폴더까지 비교 (권장)", current.Recursive);
            _maxDepth = AddNumber("최대 깊이 (0 = 무제한)", current.MaxDepth.ToString(CultureInfo.InvariantCulture), null);
            _hidden = AddCheck("숨김·시스템 파일 포함", current.IncludeHidden);

            AddLabel("표시 (다시 비교하지 않음)");
            _statusColors = AddRadio("colors", "상태별 색 (권장)", "다름 노랑 · 이쪽만 초록 · 유형 다름 빨강", !current.DirectionColors);
            _directionColors = AddRadio("colors", "방향 색 (Beyond Compare식)", "다른 파일 중 수정 시각이 늦은 쪽 빨강(최신), 이른 쪽 회색(오래됨).", current.DirectionColors);
            var directionWarning = AddText("⚠ 복사·압축 해제·git checkout은 수정 시각을 새로 써서, 옛 내용이 '최신'으로 보일 수 있습니다. 이동 전에 내용을 확인하세요.",
                warning: true, top: 4);
            _tree = AddCheck("폴더 트리 보이기", current.ShowFolderTree);
            _preview = AddCheck("내용 미리보기 보이기", current.ShowPreview);
            _hideSame = AddCheck("차이만 보기 (같은 항목 숨김)", current.HideSame);

            AddLabel("이동");
            _threshold = AddNumber("폴더 이동 확인 기준: 하위 파일 수", current.MoveConfirmThreshold.ToString(CultureInfo.InvariantCulture),
                "이 수 이상이면 이동 전에 확인합니다. 0이면 폴더 이동은 늘 확인. 같은 이름 교체 확인은 항상 뜹니다.");

            _byHash.Checked += (s, e) => _tolerance.IsEnabled = false;
            _byTime.Checked += (s, e) => _tolerance.IsEnabled = true;
            _tolerance.IsEnabled = !current.UseContentHash;
            _recursive.Click += (s, e) => _maxDepth.IsEnabled = _recursive.IsChecked == true;
            _maxDepth.IsEnabled = current.Recursive;
            void UpdateWarning() => directionWarning.Visibility = _directionColors.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            _directionColors.Checked += (s, e) => UpdateWarning();
            _statusColors.Checked += (s, e) => UpdateWarning();
            UpdateWarning();

            var reset = new Button { Content = "권장값으로", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
            reset.Click += (s, e) => Apply(new DirectoryCompareOptions());
            Body.Children.Add(reset);
            AddButtons("확인");
        }

        private void Apply(DirectoryCompareOptions options)
        {
            _byTime.IsChecked = !options.UseContentHash;
            _byHash.IsChecked = options.UseContentHash;
            _tolerance.Text = options.TimestampTolerance.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture);
            _ignore.IsChecked = options.ApplyIgnoreRules;
            _recursive.IsChecked = options.Recursive;
            _maxDepth.Text = options.MaxDepth.ToString(CultureInfo.InvariantCulture);
            _maxDepth.IsEnabled = options.Recursive;
            _hidden.IsChecked = options.IncludeHidden;
            _statusColors.IsChecked = !options.DirectionColors;
            _directionColors.IsChecked = options.DirectionColors;
            _tree.IsChecked = options.ShowFolderTree;
            _preview.IsChecked = options.ShowPreview;
            _hideSame.IsChecked = options.HideSame;
            _threshold.Text = options.MoveConfirmThreshold.ToString(CultureInfo.InvariantCulture);
        }

        private TextBox AddNumber(string label, string value, string hint)
        {
            var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var box = new TextBox { Text = value, Width = 70, HorizontalAlignment = HorizontalAlignment.Left };
            row.Children.Add(text);
            row.Children.Add(box);
            Body.Children.Add(row);
            if (hint != null)
                AddText(hint, secondary: true, top: 2);
            return box;
        }

        protected override Task<string> ValidateAsync()
        {
            if (!double.TryParse(_tolerance.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || seconds < 0 || seconds > 86400)
                return Task.FromResult("수정 시각 허용 오차는 0 ~ 86400 사이의 초로 입력하세요.");
            if (!int.TryParse(_maxDepth.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var depth) || depth < 0)
                return Task.FromResult("최대 깊이는 0 이상의 정수로 입력하세요 (0 = 무제한).");
            if (!int.TryParse(_threshold.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var threshold) || threshold < 0)
                return Task.FromResult("폴더 이동 확인 기준은 0 이상의 정수로 입력하세요.");
            Result = new DirectoryCompareOptions
            {
                UseContentHash = _byHash.IsChecked == true,
                TimestampTolerance = TimeSpan.FromSeconds(seconds),
                ApplyIgnoreRules = _ignore.IsChecked == true,
                Recursive = _recursive.IsChecked == true,
                MaxDepth = depth,
                IncludeHidden = _hidden.IsChecked == true,
                DirectionColors = _directionColors.IsChecked == true,
                ShowFolderTree = _tree.IsChecked == true,
                ShowPreview = _preview.IsChecked == true,
                HideSame = _hideSame.IsChecked == true,
                MoveConfirmThreshold = threshold
            };
            return Task.FromResult<string>(null);
        }
    }
}

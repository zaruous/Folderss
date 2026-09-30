using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Folderss.Controls;
using Folderss.Models;
using Folderss.Services;

namespace Folderss
{
    /// <summary>
    /// 기준 폴더 아래의 여러 Git 저장소를 보여 주고, 선택한 저장소 하나에 대해 스테이지·커밋·브랜치·로그·pull/push를 한다.
    /// 여러 저장소에 한꺼번에 하는 동작은 작업 트리를 바꾸지 않는 상태 조회와 fetch뿐이다(일괄 pull/push는 부분 실패 복구가 어려움).
    /// 모든 명령과 stderr는 아래 출력 영역에 남긴다.
    /// </summary>
    public partial class GitWindow : Window
    {
        public sealed class RepositoryRow : INotifyPropertyChanged
        {
            private GitStatusSnapshot _snapshot;
            private string _error;

            public GitRepositoryInfo Info { get; }
            public string RootPath => Info.RootPath;
            public string Title => Info.IsAncestor ? "▲ " + Info.DisplayPath + "  (상위 저장소)" : Info.DisplayPath;

            public GitStatusSnapshot Snapshot
            {
                get => _snapshot;
                set { _snapshot = value; _error = null; Notify(); }
            }

            public string Error
            {
                get => _error;
                set { _error = value; Notify(); }
            }

            public string Summary
            {
                get
                {
                    if (_error != null)
                        return "⚠ " + _error;
                    if (_snapshot == null)
                        return "읽는 중…";

                    var parts = new List<string> { _snapshot.BranchDisplay };
                    if (_snapshot.Upstream == null && !_snapshot.IsDetached)
                        parts.Add("upstream 없음");
                    if (_snapshot.Ahead > 0) parts.Add("↑" + _snapshot.Ahead);
                    if (_snapshot.Behind > 0) parts.Add("↓" + _snapshot.Behind);
                    if (_snapshot.ConflictCount > 0) parts.Add("⚠ 충돌 " + _snapshot.ConflictCount);
                    var changed = _snapshot.Entries.Count - _snapshot.ConflictCount;
                    parts.Add(changed > 0 ? "● 변경 " + changed : "깨끗함");
                    return string.Join("  ", parts);
                }
            }

            public RepositoryRow(GitRepositoryInfo info) { Info = info; }

            public event PropertyChangedEventHandler PropertyChanged;

            private void Notify()
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Snapshot)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));
            }
        }

        private readonly string _basePath;
        private readonly Func<string, int> _countModifiedDocumentsUnder;
        private readonly Action<string> _openFile;
        private readonly Action _openGitSettings;
        private readonly ObservableCollection<RepositoryRow> _rows = new ObservableCollection<RepositoryRow>();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private CancellationTokenSource _operation;
        private GitSettings _settings;
        private int _busyCount;
        private List<GitBranchInfo> _branches = new List<GitBranchInfo>();

        /// <param name="basePath">탐색 기준 폴더.</param>
        /// <param name="countModifiedDocumentsUnder">폴더 아래 경로로 열린 미저장 뷰어 개수 — 브랜치 전환·pull 전 경고용.</param>
        /// <param name="openFile">파일 더블클릭 시 기존 뷰어로 연다.</param>
        /// <param name="openGitSettings">설정 창의 Git 탭을 연다. 저장되면 메인 창이 <see cref="ApplySettings"/>로 알려 준다.</param>
        public GitWindow(string basePath, GitSettings settings, Func<string, int> countModifiedDocumentsUnder,
            Action<string> openFile, Action openGitSettings)
        {
            InitializeComponent();
            _basePath = basePath;
            _countModifiedDocumentsUnder = countModifiedDocumentsUnder ?? (_ => 0);
            _openFile = openFile;
            _openGitSettings = openGitSettings;
            _settings = (settings ?? new GitSettings()).Clone();
            GitCommandRunner.ConfiguredGitPath = _settings.GitExecutablePath;

            foreach (var view in new[] { ChangesDiff, RemoteDiff, LogDiff })
            {
                view.ExternalToolRequested += DiffView_ExternalToolRequested;
                view.ViewModeChanged += DiffView_ViewModeChanged;
                view.ViewMode = _settings.DiffViewMode;
            }

            Title = "Git — " + basePath;
            BasePathText.Text = "기준: " + basePath;
            BasePathText.ToolTip = basePath;
            RepoList.ItemsSource = _rows;
            SectionNav.SelectedIndex = 0;
            UpdateButtons();
        }

        private RepositoryRow SelectedRow => RepoList.SelectedItem as RepositoryRow;

        // ── 수명·진행 상태 ─────────────────────────────────────────────────────

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (!await CheckGitAsync())
            {
                Close();
                return;
            }
            await ScanAsync();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            // 창을 닫으면 진행 중인 탐색·git 프로세스를 모두 끝낸다(러너가 프로세스 트리를 종료).
            _lifetime.Cancel();
        }

        private async Task<bool> CheckGitAsync()
        {
            if (GitCommandRunner.FindGit() == null)
            {
                MessageBox.Show(this,
                    "git 실행 파일을 찾을 수 없습니다.\nGit for Windows를 설치하고 PATH에 등록한 뒤 다시 여세요.",
                    "Git", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            try
            {
                var result = await GitCommandRunner.RunAsync(null, new[] { "--version" }, GitCommandRunner.QueryTimeout, _lifetime.Token, readOnly: true);
                var version = GitCommandRunner.ParseVersion(result.StdOut);
                AppendOutput("$ git --version\n" + result.StdOut.Trim());
                if (version != null && version < GitCommandRunner.MinimumVersion)
                {
                    MessageBox.Show(this,
                        string.Format("git {0} 이상이 필요합니다 (현재 {1}). Git for Windows를 업데이트하세요.", GitCommandRunner.MinimumVersion, version),
                        "Git", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "git을 실행할 수 없습니다.\n" + ex.Message, "Git", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        /// <summary>진행 중 표시와 버튼 잠금을 걸고 작업을 돌린다. 취소 버튼은 이 토큰을 끊는다.</summary>
        private async Task RunBusyAsync(string status, Func<CancellationToken, Task> work)
        {
            if (_busyCount > 0)
                return;

            _busyCount++;
            _operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            StatusText.Text = status;
            UpdateButtons();
            try
            {
                await work(_operation.Token);
            }
            catch (OperationCanceledException)
            {
                if (!_lifetime.IsCancellationRequested)
                    AppendOutput("(취소됨)");
            }
            catch (Exception ex)
            {
                AppendOutput("오류: " + ex.Message);
            }
            finally
            {
                _operation.Dispose();
                _operation = null;
                _busyCount--;
                if (!_lifetime.IsCancellationRequested)
                {
                    StatusText.Text = string.Format("저장소 {0}개", _rows.Count);
                    UpdateButtons();
                }
            }
        }

        private void UpdateButtons()
        {
            var idle = _busyCount == 0;
            var row = SelectedRow;
            var snapshot = row?.Snapshot;
            var hasRepo = row != null && snapshot != null;

            RescanButton.IsEnabled = idle;
            FetchAllButton.IsEnabled = idle && _rows.Count > 0;
            OptionsButton.IsEnabled = idle;
            CancelButton.IsEnabled = !idle;
            PullButton.IsEnabled = idle && hasRepo;
            PushButton.IsEnabled = idle && hasRepo;
            DetailRoot.IsEnabled = idle && hasRepo;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _operation?.Cancel();
        }

        private void AppendOutput(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;
            OutputBox.AppendText(text.TrimEnd() + Environment.NewLine);
            OutputBox.ScrollToEnd();
        }

        private void AppendResult(GitRepositoryInfo repo, GitResult result)
        {
            var builder = new StringBuilder();
            builder.Append("$ ").Append(result.CommandLine).Append("    [").Append(repo.DisplayPath).Append(']');
            if (!string.IsNullOrWhiteSpace(result.StdOut))
                builder.AppendLine().Append(result.StdOut.TrimEnd());
            if (!string.IsNullOrWhiteSpace(result.StdErr))
                builder.AppendLine().Append(result.StdErr.TrimEnd());
            if (!result.Success)
                builder.AppendLine().Append("→ 실패 (종료 코드 ").Append(result.ExitCode).Append(')');
            AppendOutput(builder.ToString());
        }

        // ── 탐색·상태 ─────────────────────────────────────────────────────────

        private Task ScanAsync()
        {
            return RunBusyAsync("저장소 찾는 중…", async token =>
            {
                _rows.Clear();
                ClearDetail();

                var found = 0;
                var statusTasks = new List<Task>();
                var excluded = _settings.ExcludedFolders.ToArray();
                var depth = _settings.ScanDepth;

                // 찾는 즉시 목록에 넣고 상태 읽기를 시작한다(동시 실행 수는 러너가 제한).
                var result = await Task.Run(() => GitRepositoryScanner.Scan(_basePath, depth, excluded, info =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        var row = new RepositoryRow(info);
                        _rows.Add(row);
                        found++;
                        StatusText.Text = string.Format("저장소 찾는 중… {0}개", found);
                        statusTasks.Add(RefreshStatusAsync(row, token));
                        if (_rows.Count == 1)
                            RepoList.SelectedIndex = 0;
                    });
                }, token), token);

                await Task.WhenAll(statusTasks);

                AppendOutput(string.Format("탐색 완료: 저장소 {0}개 (깊이 {1}, 제외: {2}){3}",
                    result.Repositories.Count, depth,
                    excluded.Length == 0 ? "없음" : string.Join(", ", excluded),
                    result.InaccessibleCount > 0 ? string.Format(", 접근할 수 없는 폴더 {0}개 건너뜀", result.InaccessibleCount) : string.Empty));
                if (result.Repositories.Count == 0)
                    AppendOutput("기준 폴더 아래에서 .git을 찾지 못했습니다. 옵션에서 탐색 깊이·제외 폴더를 확인하세요.");
            });
        }

        private async Task RefreshStatusAsync(RepositoryRow row, CancellationToken token)
        {
            var result = await GitCommandRunner.RunAsync(row.RootPath,
                GitOutputParser.StatusArguments,
                GitCommandRunner.QueryTimeout, token, readOnly: true);

            if (result.Success)
            {
                row.Snapshot = GitOutputParser.ParseStatusV2(result.StdOut);
            }
            else
            {
                row.Error = FirstLine(result.StdErr) ?? "상태를 읽지 못했습니다";
                AppendResult(row.Info, result);
            }

            if (ReferenceEquals(row, SelectedRow))
            {
                ShowChanges(row);
                UpdateButtons();
            }
        }

        private static string FirstLine(string text)
        {
            return text?.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0);
        }

        private async void Rescan_Click(object sender, RoutedEventArgs e)
        {
            await ScanAsync();
        }

        private async void FetchAll_Click(object sender, RoutedEventArgs e)
        {
            var rows = _rows.ToList();
            await RunBusyAsync(string.Format("fetch 중… ({0}개)", rows.Count), async token =>
            {
                var failed = 0;
                await Task.WhenAll(rows.Select(async row =>
                {
                    var result = await GitCommandRunner.RunAsync(row.RootPath, new[] { "fetch", "--prune" }, GitCommandRunner.NetworkTimeout, token);
                    AppendResult(row.Info, result);
                    if (!result.Success)
                        failed++;
                    await RefreshStatusAsync(row, token);
                }));
                AppendOutput(string.Format("전체 fetch 완료: 성공 {0}, 실패 {1}", rows.Count - failed, failed));
            });
            await ReloadSelectedDetailAsync();
        }

        // ── 선택 저장소 상세 ──────────────────────────────────────────────────

        private async void RepoList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateButtons();
            await ReloadSelectedDetailAsync();
        }

        private void SectionNav_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var tag = (SectionNav.SelectedItem as ListBoxItem)?.Tag as string;
            ChangesSection.Visibility = tag == "Changes" ? Visibility.Visible : Visibility.Collapsed;
            BranchesSection.Visibility = tag == "Branches" ? Visibility.Visible : Visibility.Collapsed;
            RemoteSection.Visibility = tag == "Remote" ? Visibility.Visible : Visibility.Collapsed;
            LogSection.Visibility = tag == "Log" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ClearDetail()
        {
            UnstagedList.ItemsSource = null;
            StagedList.ItemsSource = null;
            UnstagedTree.ItemsSource = null;
            StagedTree.ItemsSource = null;
            UnchangedList.ItemsSource = null;
            UnchangedTree.ItemsSource = null;
            UnchangedHeader.Text = "변경 없음";
            BranchList.ItemsSource = null;
            LogList.ItemsSource = null;
            OutgoingList.ItemsSource = null;
            IncomingList.ItemsSource = null;
            UnstagedHeader.Text = "변경됨";
            StagedHeader.Text = "스테이지됨";
            OutgoingHeader.Text = "보낼 커밋 (push)";
            IncomingHeader.Text = "받을 커밋 (pull)";
            RemoteSummaryText.Text = string.Empty;
            ChangesDiff.Clear();
            RemoteDiff.Clear();
            LogDiff.Clear();
        }

        private void ShowChanges(RepositoryRow row)
        {
            // 목록을 새로 만들면 선택이 풀리므로, 옛 diff가 남아 현재 상태처럼 보이지 않게 비운다.
            ChangesDiff.Clear();
            var snapshot = row?.Snapshot;
            if (snapshot == null)
            {
                UnstagedList.ItemsSource = null;
                StagedList.ItemsSource = null;
                UnstagedTree.ItemsSource = null;
                StagedTree.ItemsSource = null;
                return;
            }

            // 충돌·추적 안 됨·작업 트리 변경은 왼쪽, 인덱스 변경은 오른쪽. 둘 다 바뀐 파일(MM)은 양쪽에 보인다.
            var unstaged = snapshot.Entries.Where(entry => entry.IsUnstaged)
                .OrderBy(entry => entry.IsConflicted ? 0 : entry.IsUntracked ? 2 : 1)
                .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var staged = snapshot.Entries.Where(entry => entry.IsStaged)
                .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            UnstagedList.ItemsSource = unstaged;
            StagedList.ItemsSource = staged;
            UnstagedTree.ItemsSource = GitChangeTree.Build(unstaged);
            StagedTree.ItemsSource = GitChangeTree.Build(staged);

            if (UnchangedToggle.IsChecked == true)
                LoadUnchanged(row);
            UnstagedHeader.Text = string.Format("변경됨 ({0})", unstaged.Count);
            StagedHeader.Text = string.Format("스테이지됨 ({0})", staged.Count);
        }

        private async Task ReloadSelectedDetailAsync()
        {
            var row = SelectedRow;
            ClearDetail();
            if (row == null)
                return;

            ShowChanges(row);
            try
            {
                await LoadBranchesAndLogAsync(row, _lifetime.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                // async void 이벤트에서 부르므로 여기서 막지 않으면 앱이 종료된다.
                AppendOutput("오류: " + ex.Message);
            }
        }

        private async Task LoadBranchesAndLogAsync(RepositoryRow row, CancellationToken token)
        {
            var branchTask = GitCommandRunner.RunAsync(row.RootPath,
                new[] { "for-each-ref", GitOutputParser.BranchFormat, "refs/heads", "refs/remotes" },
                GitCommandRunner.QueryTimeout, token, readOnly: true);

            var logArgs = new List<string> { "log", "--topo-order", "-z", "-n", _settings.LogLimit.ToString(), GitOutputParser.LogFormat };
            if (_settings.LogAllBranches)
                logArgs.Add("--all");
            var logTask = row.Snapshot?.HeadOid == null && !_settings.LogAllBranches
                ? Task.FromResult<GitResult>(null)   // 최초 커밋 전: log가 실패하므로 부르지 않는다
                : GitCommandRunner.RunAsync(row.RootPath, logArgs, GitCommandRunner.QueryTimeout, token, readOnly: true);

            var branchResult = await branchTask;
            var logResult = await logTask;

            // 기다리는 사이 다른 저장소를 골랐으면 버린다.
            if (!ReferenceEquals(row, SelectedRow))
                return;

            if (branchResult.Success)
            {
                _branches = GitOutputParser.ParseBranches(branchResult.StdOut)
                    .OrderBy(b => b.IsRemote).ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();
                BranchList.ItemsSource = _branches;
            }
            else
            {
                AppendResult(row.Info, branchResult);
            }

            if (logResult != null)
            {
                // 커밋이 하나도 없는 저장소의 log 실패는 정상이므로 출력하지 않는다.
                if (logResult.Success)
                {
                    var commits = GitOutputParser.ParseLog(logResult.StdOut);
                    var graph = GitGraphLayout.Compute(commits);
                    var maxLanes = 1;
                    for (var i = 0; i < commits.Count; i++)
                    {
                        commits[i].Graph = graph[i];
                        maxLanes = Math.Max(maxLanes, graph[i].LaneCount);
                    }
                    GraphColumn.Width = GitGraphCell.WidthFor(maxLanes);
                    LogList.ItemsSource = commits;
                }
                else if (row.Snapshot?.HeadOid != null)
                    AppendResult(row.Info, logResult);
            }

            await LoadRemoteAsync(row, token);
        }

        // ── diff ────────────────────────────────────────────────────────────

        /// <summary>선택 저장소에서 diff 명령을 돌려 <paramref name="view"/>에 보인다. 조회 전용이라 진행 중 잠금과 무관하게 돈다.</summary>
        private async Task LoadDiffAsync(GitDiffView view, GitDiffRequest diff)
        {
            var row = SelectedRow;
            if (row == null)
                return;

            var request = view.BeginLoad(diff);
            try
            {
                var fallback = ResolveFallbackEncoding();
                var mode = view.ViewMode;
                var result = await GitCommandRunner.RunAsync(row.RootPath, GitDiffCommands.WithViewMode(diff.Arguments, mode),
                    GitCommandRunner.QueryTimeout, _lifetime.Token, readOnly: true, fallbackEncoding: fallback);
                if (GitDiffCommands.IsSuccess(result, diff.NoIndex))
                {
                    // UTF-16/32(BOM) 파일은 git이 바이너리로 보이므로 BOM으로 읽어 텍스트로 다시 비교해 끼운다.
                    var text = await GitEncodingDiff.ExpandAsync(row.RootPath, diff, result.StdOut, _settings.IgnoreWhitespace, fallback,
                        _lifetime.Token, mode);
                    view.Complete(request, text, diff.EmptyMessage);
                }
                else if (view.Fail(request, FirstLine(result.StdErr) ?? "차이를 읽지 못했습니다."))
                {
                    AppendResult(row.Info, result);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                // async void 이벤트에서 부르므로 여기서 막는다.
                view.Fail(request, ex.Message);
            }
        }

        private async void DiffView_ViewModeChanged(object sender, EventArgs e)
        {
            // 보고 있던 비교를 새 보기 모드로 다시 불러온다(제목·외부 도구 대상은 그대로).
            var view = (GitDiffView)sender;
            if (view.CurrentRequest != null)
                await LoadDiffAsync(view, view.CurrentRequest);
        }

        private System.Text.Encoding ResolveFallbackEncoding()
        {
            try
            {
                return GitTextDecoder.Resolve(_settings.FallbackEncoding);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException)
            {
                return null;   // 코드 페이지를 못 쓰는 환경이면 대체 해석 없이 UTF-8로만 읽는다.
            }
        }

        private static GitDiffRequest WithTitle(GitDiffRequest request, string title, string emptyMessage = null)
        {
            request.Title = title;
            if (emptyMessage != null)
                request.EmptyMessage = emptyMessage;
            return request;
        }

        private async void UnstagedList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (UnstagedList.SelectedItems.Count != 1)
            {
                if (UnstagedList.IsKeyboardFocusWithin)
                    ChangesDiff.Clear(UnstagedList.SelectedItems.Count == 0 ? "파일을 선택하면 차이를 보여 줍니다." : "파일 하나를 선택하면 차이를 보여 줍니다.");
                return;
            }

            await ShowUnstagedDiffAsync((GitStatusEntry)UnstagedList.SelectedItem);
        }

        private async Task ShowUnstagedDiffAsync(GitStatusEntry entry)
        {
            if (entry.IsUntracked)
            {
                if (entry.Path.EndsWith("/", StringComparison.Ordinal))
                {
                    ChangesDiff.Clear("추적 안 되는 폴더입니다. 폴더 안 파일은 스테이지한 뒤 볼 수 있습니다.");
                    return;
                }
                await LoadDiffAsync(ChangesDiff, WithTitle(GitDiffCommands.Untracked(entry.Path, _settings.IgnoreWhitespace),
                    "추적 안 됨 (새 파일): " + entry.Path));
                return;
            }

            await LoadDiffAsync(ChangesDiff, WithTitle(GitDiffCommands.WorkTree(entry.Path, _settings.IgnoreWhitespace),
                (entry.IsConflicted ? "충돌 (작업 트리): " : "작업 트리 ↔ 인덱스: ") + entry.Path));
        }

        private async void StagedList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (StagedList.SelectedItems.Count != 1)
            {
                if (StagedList.IsKeyboardFocusWithin)
                    ChangesDiff.Clear(StagedList.SelectedItems.Count == 0 ? "파일을 선택하면 차이를 보여 줍니다." : "파일 하나를 선택하면 차이를 보여 줍니다.");
                return;
            }

            await ShowStagedDiffAsync((GitStatusEntry)StagedList.SelectedItem);
        }

        private Task ShowStagedDiffAsync(GitStatusEntry entry)
        {
            return LoadDiffAsync(ChangesDiff, WithTitle(GitDiffCommands.Staged(entry.Path, entry.OriginalPath, _settings.IgnoreWhitespace),
                "인덱스 ↔ HEAD (커밋될 내용): " + entry.Path));
        }

        // ── 변경 사항 트리 보기 ──────────────────────────────────────────────

        private bool IsChangesTreeMode => ChangesTreeToggle.IsChecked == true;

        private void ChangesTreeToggle_Click(object sender, RoutedEventArgs e)
        {
            var tree = IsChangesTreeMode;
            UnstagedList.Visibility = StagedList.Visibility = tree ? Visibility.Collapsed : Visibility.Visible;
            UnstagedTree.Visibility = StagedTree.Visibility = tree ? Visibility.Visible : Visibility.Collapsed;
            UnchangedList.Visibility = tree ? Visibility.Collapsed : Visibility.Visible;
            UnchangedTree.Visibility = tree ? Visibility.Visible : Visibility.Collapsed;
            // 두 보기의 선택은 따로라, 전환하면 오른쪽 diff가 어느 선택의 것인지 헷갈리지 않게 비운다.
            UnstagedList.UnselectAll();
            StagedList.UnselectAll();
            UnchangedList.UnselectAll();
            ChangesDiff.Clear();
        }

        // ── 변경 없는 파일 ─────────────────────────────────────────────────

        /// <summary>미리보기로 읽을 최대 파일 크기.</summary>
        private const long MaxPreviewBytes = 10 * 1024 * 1024;

        private void UnchangedToggle_Click(object sender, RoutedEventArgs e)
        {
            var on = UnchangedToggle.IsChecked == true;
            UnchangedSplitterRow.Height = on ? new GridLength(8) : new GridLength(0);
            UnchangedRow.Height = on ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            UnchangedRow.MinHeight = on ? 60 : 0;
            UnchangedSplitter.Visibility = UnchangedPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (on)
            {
                LoadUnchanged(SelectedRow);
            }
            else
            {
                UnchangedList.ItemsSource = null;
                UnchangedTree.ItemsSource = null;
            }
        }

        /// <summary>추적 파일(ls-files)에서 변경 목록을 뺀 것을 채운다. 조회 전용이라 진행 중 잠금과 무관하게 돈다.</summary>
        private async void LoadUnchanged(RepositoryRow row)
        {
            UnchangedList.ItemsSource = null;
            UnchangedTree.ItemsSource = null;
            UnchangedHeader.Text = "변경 없음 (읽는 중…)";
            if (row?.Snapshot == null)
            {
                UnchangedHeader.Text = "변경 없음";
                return;
            }

            try
            {
                var result = await GitCommandRunner.RunAsync(row.RootPath, GitOutputParser.TrackedFilesArguments,
                    GitCommandRunner.QueryTimeout, _lifetime.Token, readOnly: true);
                if (!ReferenceEquals(row, SelectedRow) || UnchangedToggle.IsChecked != true)
                    return;
                if (!result.Success)
                {
                    UnchangedHeader.Text = "변경 없음 (읽지 못함)";
                    AppendResult(row.Info, result);
                    return;
                }

                var unchanged = GitOutputParser.UnchangedEntries(GitOutputParser.ParseNulList(result.StdOut), row.Snapshot)
                    .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                UnchangedList.ItemsSource = unchanged;
                UnchangedTree.ItemsSource = GitChangeTree.Build(unchanged);
                UnchangedHeader.Text = string.Format("변경 없음 ({0})", unchanged.Count);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                // async void라 여기서 막는다.
                UnchangedHeader.Text = "변경 없음 (읽지 못함)";
                AppendOutput("변경 없는 파일 목록을 읽지 못했습니다: " + ex.Message);
            }
        }

        private void UnchangedList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (UnchangedList.SelectedItem is GitStatusEntry entry)
                ShowUnchangedContent(entry);
        }

        private void UnchangedTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var node = UnchangedTree.SelectedItem as GitChangeNode;
            if (node == null)
                return;
            if (node.IsFolder)
                ChangesDiff.Clear(string.Format("폴더 {0} — 변경 없는 파일 {1}개", node.Path, node.Entries.Count()));
            else
                ShowUnchangedContent(node.Entry);
        }

        /// <summary>변경 없는 파일은 차이가 없으므로 작업 트리 파일 내용을 줄 번호와 함께 보인다(BOM 규칙으로 디코딩).</summary>
        private async void ShowUnchangedContent(GitStatusEntry entry)
        {
            var row = SelectedRow;
            if (row == null)
                return;

            var title = "변경 없음 (현재 내용): " + entry.Path;
            var full = Path.Combine(row.RootPath, entry.Path.Replace('/', Path.DirectorySeparatorChar));
            var fallback = ResolveFallbackEncoding();
            ChangesDiff.ShowLines(title, new List<GitDiffLine>(), "불러오는 중…");
            try
            {
                var (lines, message) = await Task.Run(() =>
                {
                    if (!File.Exists(full))
                        return (new List<GitDiffLine>(), "작업 트리에 파일이 없습니다.");
                    if (new FileInfo(full).Length > MaxPreviewBytes)
                        return (new List<GitDiffLine>(), "파일이 너무 커서(10MB 초과) 미리 보지 않습니다. 더블클릭해 여세요.");
                    var bytes = File.ReadAllBytes(full);
                    // NUL이 있는데 UTF-16/32 BOM이 아니면 바이너리로 본다(git과 같은 기준: 앞 8000바이트).
                    if (!GitTextDecoder.HasWideBom(bytes) && Array.IndexOf(bytes, (byte)0, 0, Math.Min(bytes.Length, 8000)) >= 0)
                        return (new List<GitDiffLine>(), "바이너리 파일입니다.");
                    return (GitOutputParser.ContentLines(GitTextDecoder.DecodeFile(bytes, fallback), GitDiffView.MaxLines), "빈 파일입니다.");
                });

                // 기다리는 사이 다른 항목을 골랐으면 버린다.
                var current = (IsChangesTreeMode ? (UnchangedTree.SelectedItem as GitChangeNode)?.Entry : UnchangedList.SelectedItem as GitStatusEntry);
                if (ReferenceEquals(current, entry))
                    ChangesDiff.ShowLines(title, lines, message);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                ChangesDiff.ShowLines(title, new List<GitDiffLine>(), "파일을 읽지 못했습니다: " + ex.Message);
            }
        }

        private async void UnstagedTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var node = UnstagedTree.SelectedItem as GitChangeNode;
            if (node == null)
                return;
            if (node.IsFolder)
            {
                ChangesDiff.Clear(string.Format("폴더 {0} — 파일 {1}개. 스테이지하면 이 폴더 아래 전체가 대상입니다.", node.Path, node.Entries.Count()));
                return;
            }
            await ShowUnstagedDiffAsync(node.Entry);
        }

        private async void StagedTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var node = StagedTree.SelectedItem as GitChangeNode;
            if (node == null)
                return;
            if (node.IsFolder)
            {
                ChangesDiff.Clear(string.Format("폴더 {0} — 파일 {1}개. 언스테이지하면 이 폴더 아래 전체가 대상입니다.", node.Path, node.Entries.Count()));
                return;
            }
            await ShowStagedDiffAsync(node.Entry);
        }

        private void ChangeTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // 폴더 더블클릭은 TreeView 기본 동작(접기/펴기)에 맡기고, 파일만 연다.
            var node = (sender as TreeView)?.SelectedItem as GitChangeNode;
            if (node?.Entry != null)
                OpenEntry(node.Entry);
        }

        /// <summary>스테이지/언스테이지 대상: 평면 보기는 선택한 항목들, 트리 보기는 선택한 노드(폴더면 그 아래 전체).</summary>
        private List<GitStatusEntry> SelectedChangeEntries(ListBox list, TreeView tree)
        {
            if (IsChangesTreeMode)
                return (tree.SelectedItem as GitChangeNode)?.Entries.ToList() ?? new List<GitStatusEntry>();
            return list.SelectedItems.Cast<GitStatusEntry>().ToList();
        }

        private async void LogList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            await ShowCommitDiffAsync(LogDiff, LogList.SelectedItem as GitCommitInfo);
        }

        private async void RemoteCommitList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var list = (ListBox)sender;
            if (!list.IsKeyboardFocusWithin && list.SelectedItem == null)
                return;
            await ShowCommitDiffAsync(RemoteDiff, list.SelectedItem as GitCommitInfo);
        }

        private Task ShowCommitDiffAsync(GitDiffView view, GitCommitInfo commit)
        {
            if (commit == null)
            {
                view.Clear("커밋을 선택하면 변경을 보여 줍니다.");
                return Task.CompletedTask;
            }

            var basis = commit.Parents.Length == 0 ? "최초 커밋" : commit.Parents.Length > 1 ? "병합 커밋 — 첫 부모 기준" : null;
            var title = string.Format("{0}  {1}  ({2}, {3}{4})", commit.ShortHash, commit.Subject, commit.Author, commit.TimeText,
                basis == null ? string.Empty : ", " + basis);
            return LoadDiffAsync(view, WithTitle(GitDiffCommands.Commit(commit.Hash, commit.Parents, _settings.IgnoreWhitespace), title));
        }

        // ── 원격 비교 ────────────────────────────────────────────────────────

        private const int RemoteLogLimit = 500;

        /// <summary>현재 브랜치와 upstream의 차이(보낼/받을 커밋)를 읽는다. 원격 추적 브랜치 기준이라 최신은 fetch 뒤에 보인다.</summary>
        private async Task LoadRemoteAsync(RepositoryRow row, CancellationToken token)
        {
            var snapshot = row.Snapshot;
            OutgoingList.ItemsSource = null;
            IncomingList.ItemsSource = null;
            RemoteDiff.Clear("커밋을 선택하거나 위 버튼으로 전체 차이를 보세요.");

            var hasUpstream = snapshot != null && !snapshot.IsDetached && snapshot.Upstream != null;
            OutgoingDiffButton.IsEnabled = hasUpstream;
            IncomingDiffButton.IsEnabled = hasUpstream;
            WorkTreeDiffButton.IsEnabled = hasUpstream;

            if (snapshot == null)
                return;
            if (snapshot.IsDetached)
            {
                RemoteSummaryText.Text = "detached HEAD 상태라 비교할 upstream이 없습니다.";
                return;
            }
            if (snapshot.Upstream == null)
            {
                RemoteSummaryText.Text = string.Format("'{0}' 브랜치에 upstream이 없습니다. push하면 설정할 수 있습니다.", snapshot.Branch);
                return;
            }

            RemoteSummaryText.Text = "비교 중…";
            var outgoingTask = GitCommandRunner.RunAsync(row.RootPath, GitDiffCommands.LogRange(GitDiffCommands.Upstream, "HEAD", RemoteLogLimit),
                GitCommandRunner.QueryTimeout, token, readOnly: true);
            var incomingTask = GitCommandRunner.RunAsync(row.RootPath, GitDiffCommands.LogRange("HEAD", GitDiffCommands.Upstream, RemoteLogLimit),
                GitCommandRunner.QueryTimeout, token, readOnly: true);
            var outgoing = await outgoingTask;
            var incoming = await incomingTask;

            if (!ReferenceEquals(row, SelectedRow))
                return;

            if (!outgoing.Success || !incoming.Success)
            {
                // 원격에서 브랜치가 지워졌거나(upstream gone) 아직 fetch 전인 경우.
                RemoteSummaryText.Text = string.Format("{0} 와(과) 비교할 수 없습니다: {1}", snapshot.Upstream,
                    FirstLine(outgoing.Success ? incoming.StdErr : outgoing.StdErr) ?? "알 수 없는 오류");
                OutgoingDiffButton.IsEnabled = IncomingDiffButton.IsEnabled = WorkTreeDiffButton.IsEnabled = false;
                return;
            }

            var outgoingCommits = GitOutputParser.ParseLog(outgoing.StdOut);
            var incomingCommits = GitOutputParser.ParseLog(incoming.StdOut);
            OutgoingList.ItemsSource = outgoingCommits;
            IncomingList.ItemsSource = incomingCommits;
            OutgoingHeader.Text = string.Format("보낼 커밋 (push) — {0}{1}개", outgoingCommits.Count, outgoingCommits.Count >= RemoteLogLimit ? "+" : string.Empty);
            IncomingHeader.Text = string.Format("받을 커밋 (pull) — {0}{1}개", incomingCommits.Count, incomingCommits.Count >= RemoteLogLimit ? "+" : string.Empty);
            RemoteSummaryText.Text = string.Format("{0} ↔ {1}   (원격 상태는 마지막 fetch 기준입니다. 최신으로 비교하려면 'fetch 후 비교')",
                snapshot.Branch, snapshot.Upstream);
        }

        private async void FetchSelected_Click(object sender, RoutedEventArgs e)
        {
            await RunOnSelectedAsync("fetch 중…", new[] { "fetch", "--prune" }, GitCommandRunner.NetworkTimeout);
        }

        private async void OutgoingDiff_Click(object sender, RoutedEventArgs e)
        {
            await LoadDiffAsync(RemoteDiff, WithTitle(GitDiffCommands.Range(GitDiffCommands.Upstream + "...HEAD", _settings.IgnoreWhitespace),
                "보낼 변경 전체: upstream과 갈라진 뒤 로컬에서 커밋한 변경 (@{u}...HEAD)", "보낼 변경이 없습니다."));
        }

        private async void IncomingDiff_Click(object sender, RoutedEventArgs e)
        {
            await LoadDiffAsync(RemoteDiff, WithTitle(GitDiffCommands.Range("HEAD..." + GitDiffCommands.Upstream, _settings.IgnoreWhitespace),
                "받을 변경 전체: 갈라진 뒤 upstream에 커밋된 변경 (HEAD...@{u})", "받을 변경이 없습니다."));
        }

        private async void WorkTreeVsUpstream_Click(object sender, RoutedEventArgs e)
        {
            await LoadDiffAsync(RemoteDiff, WithTitle(GitDiffCommands.WorkTreeAgainst(GitDiffCommands.Upstream, _settings.IgnoreWhitespace),
                "작업 트리 ↔ upstream: 커밋 안 한 수정 포함, 추적 안 되는 파일 제외 (@{u})", "로컬 작업 트리와 upstream이 같습니다."));
        }

        /// <summary>선택 저장소에 쓰기 명령을 실행하고, 끝나면 상태·브랜치·로그를 다시 읽는다.</summary>
        private Task RunOnSelectedAsync(string status, IEnumerable<string> args, TimeSpan timeout, string standardInput = null,
            Action<GitResult> onDone = null)
        {
            var row = SelectedRow;
            if (row == null)
                return Task.CompletedTask;

            var argList = args.ToList();
            return RunBusyAsync(status, async token =>
            {
                GitResult result;
                try
                {
                    result = await GitCommandRunner.RunAsync(row.RootPath, argList, timeout, token, standardInput: standardInput);
                }
                finally
                {
                    // 취소돼도 상태는 다시 읽는다(명령이 일부만 적용됐을 수 있음).
                    await RefreshStatusAsync(row, _lifetime.Token);
                }
                AppendResult(row.Info, result);
                onDone?.Invoke(result);
                if (ReferenceEquals(row, SelectedRow))
                    await LoadBranchesAndLogAsync(row, _lifetime.Token);
            });
        }

        // ── 스테이지·커밋 ─────────────────────────────────────────────────────

        private static string ToPathspecInput(IEnumerable<GitStatusEntry> entries, bool includeOriginal)
        {
            var paths = new List<string>();
            foreach (var entry in entries)
            {
                paths.Add(entry.Path);
                if (includeOriginal && !string.IsNullOrEmpty(entry.OriginalPath))
                    paths.Add(entry.OriginalPath);
            }
            // 경로는 NUL 구분으로 표준 입력에 넘긴다(명령줄 길이 제한·특수 문자 회피).
            return string.Join("\0", paths.Distinct()) + "\0";
        }

        private async void Stage_Click(object sender, RoutedEventArgs e)
        {
            var entries = SelectedChangeEntries(UnstagedList, UnstagedTree);
            if (entries.Count == 0)
                return;
            await RunOnSelectedAsync("스테이지 중…",
                new[] { "add", "--pathspec-from-file=-", "--pathspec-file-nul" },
                GitCommandRunner.QueryTimeout, ToPathspecInput(entries, false));
        }

        private async void StageAll_Click(object sender, RoutedEventArgs e)
        {
            await RunOnSelectedAsync("전체 스테이지 중…", new[] { "add", "-A" }, GitCommandRunner.QueryTimeout);
        }

        private async void Unstage_Click(object sender, RoutedEventArgs e)
        {
            var entries = SelectedChangeEntries(StagedList, StagedTree);
            if (entries.Count == 0 || SelectedRow?.Snapshot == null)
                return;

            if (SelectedRow.Snapshot.HeadOid == null)
            {
                // 최초 커밋 전에는 되돌릴 HEAD가 없어 restore --staged가 실패한다 — 인덱스에서만 뺀다.
                var args = new List<string> { "rm", "--cached", "-r", "-q", "--" };
                args.AddRange(entries.Select(entry => entry.Path));
                await RunOnSelectedAsync("언스테이지 중…", args, GitCommandRunner.QueryTimeout);
                return;
            }

            // 이름 변경은 원래 경로도 넣어야 양쪽이 모두 풀린다.
            await RunOnSelectedAsync("언스테이지 중…",
                new[] { "restore", "--staged", "--pathspec-from-file=-", "--pathspec-file-nul" },
                GitCommandRunner.QueryTimeout, ToPathspecInput(entries, true));
        }

        private async void UnstageAll_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedRow?.Snapshot == null)
                return;
            var args = SelectedRow.Snapshot.HeadOid == null
                ? new[] { "rm", "--cached", "-r", "-q", "--", "." }
                : new[] { "restore", "--staged", "--", "." };
            await RunOnSelectedAsync("전체 언스테이지 중…", args, GitCommandRunner.QueryTimeout);
        }

        private void CommitMessageBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                Commit_Click(sender, e);
            }
        }

        private async void Commit_Click(object sender, RoutedEventArgs e)
        {
            var snapshot = SelectedRow?.Snapshot;
            if (snapshot == null || _busyCount > 0)
                return;

            var message = CommitMessageBox.Text;
            if (string.IsNullOrWhiteSpace(message))
            {
                MessageBox.Show(this, "커밋 메시지를 입력하세요.", "커밋", MessageBoxButton.OK, MessageBoxImage.Information);
                CommitMessageBox.Focus();
                return;
            }
            if (snapshot.StagedCount == 0)
            {
                MessageBox.Show(this, "스테이지된 변경이 없습니다.", "커밋", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (snapshot.ConflictCount > 0)
            {
                MessageBox.Show(this, "충돌이 해결되지 않은 파일이 있어 커밋할 수 없습니다.", "커밋", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 메시지는 UTF-8 임시 파일로 넘긴다(여러 줄·따옴표·한글 안전, 편집기 안 뜸).
            var messageFile = Path.Combine(Path.GetTempPath(), "folderss-commit-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                File.WriteAllText(messageFile, message.Replace("\r\n", "\n"), new UTF8Encoding(false));
                await RunOnSelectedAsync("커밋 중…", new[] { "commit", "-F", messageFile }, GitCommandRunner.NetworkTimeout,
                    onDone: result =>
                    {
                        if (result.Success)
                            CommitMessageBox.Clear();
                    });
            }
            catch (Exception ex)
            {
                AppendOutput("커밋 메시지 파일을 만들 수 없습니다: " + ex.Message);
            }
            finally
            {
                try { File.Delete(messageFile); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        private void ChangeList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if ((sender as ListBox)?.SelectedItem is GitStatusEntry entry)
                OpenEntry(entry);
        }

        private void OpenEntry(GitStatusEntry entry)
        {
            var row = SelectedRow;
            if (entry == null || row == null || _openFile == null)
                return;

            var path = Path.Combine(row.RootPath, entry.Path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path))
                _openFile(path);
        }

        // ── 브랜치 ──────────────────────────────────────────────────────────

        /// <summary>작업 트리를 바꾸는 명령 전에, 이 저장소 파일을 저장하지 않은 뷰어 탭이 있으면 묻는다.</summary>
        private bool ConfirmNoUnsavedDocuments(string action)
        {
            var row = SelectedRow;
            var count = row == null ? 0 : _countModifiedDocumentsUnder(row.RootPath);
            if (count == 0)
                return true;

            return MessageBox.Show(this,
                string.Format("이 저장소의 파일을 저장하지 않은 문서 탭이 {0}개 있습니다.\n{1}하면 디스크의 파일이 바뀌어 편집 내용과 어긋날 수 있습니다. 계속할까요?", count, action),
                "Git", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
        }

        private async void SwitchBranch_Click(object sender, RoutedEventArgs e)
        {
            var branch = BranchList.SelectedItem as GitBranchInfo;
            if (branch == null || branch.IsCurrent)
                return;
            if (!ConfirmNoUnsavedDocuments("브랜치를 전환"))
                return;

            // 원격 브랜치는 같은 이름의 추적 로컬 브랜치를 만들어 전환한다(이미 있으면 git이 거부 → 출력에 표시).
            var args = branch.IsRemote
                ? new[] { "switch", "--track", branch.Name }
                : new[] { "switch", branch.Name };
            await RunOnSelectedAsync("브랜치 전환 중…", args, GitCommandRunner.QueryTimeout);
        }

        private void BranchList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            SwitchBranch_Click(sender, e);
        }

        private async void NewBranch_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedRow == null)
                return;

            var prompt = new PromptWindow("새 브랜치", "새 브랜치 이름을 입력하세요. 현재 커밋에서 만들고 전환합니다.") { Owner = this };
            if (prompt.ShowDialog() != true)
                return;

            var name = prompt.Value.Trim();
            if (name.Length == 0)
                return;
            // '-'로 시작하면 옵션으로 해석되므로 막는다. 나머지 이름 규칙은 git(check-ref-format)이 검사한다.
            if (name.StartsWith("-", StringComparison.Ordinal))
            {
                MessageBox.Show(this, "브랜치 이름은 '-'로 시작할 수 없습니다.", "새 브랜치", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            await RunOnSelectedAsync("브랜치 만드는 중…", new[] { "switch", "-c", name }, GitCommandRunner.QueryTimeout);
        }

        private async void DeleteBranch_Click(object sender, RoutedEventArgs e)
        {
            var branch = BranchList.SelectedItem as GitBranchInfo;
            if (branch == null)
                return;
            if (branch.IsRemote)
            {
                MessageBox.Show(this, "원격 브랜치는 여기서 삭제하지 않습니다.", "브랜치 삭제", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (branch.IsCurrent)
            {
                MessageBox.Show(this, "현재 브랜치는 삭제할 수 없습니다.", "브랜치 삭제", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show(this,
                    string.Format("로컬 브랜치 '{0}'을(를) 삭제할까요?\n병합되지 않은 브랜치는 git이 거부합니다(강제 삭제 없음).", branch.Name),
                    "브랜치 삭제", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;

            await RunOnSelectedAsync("브랜치 삭제 중…", new[] { "branch", "-d", "--", branch.Name }, GitCommandRunner.QueryTimeout);
        }

        // ── pull / push ─────────────────────────────────────────────────────

        private async void Pull_Click(object sender, RoutedEventArgs e)
        {
            var snapshot = SelectedRow?.Snapshot;
            if (snapshot == null)
                return;
            if (snapshot.IsDetached)
            {
                MessageBox.Show(this, "detached HEAD 상태에서는 pull할 수 없습니다. 브랜치로 전환하세요.", "pull", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!ConfirmNoUnsavedDocuments("pull"))
                return;

            var args = new List<string> { "pull" };
            switch (_settings.PullMode)
            {
                case GitPullMode.FastForwardOnly: args.Add("--ff-only"); break;
                case GitPullMode.Merge: args.Add("--no-rebase"); break;
                case GitPullMode.Rebase: args.Add("--rebase"); break;
            }

            await RunOnSelectedAsync("pull 중…", args, GitCommandRunner.NetworkTimeout, onDone: result =>
            {
                if (result.Success)
                    return;
                if (_settings.PullMode == GitPullMode.FastForwardOnly)
                    AppendOutput("fast-forward로 받을 수 없습니다(로컬과 원격이 갈라짐). 옵션에서 pull 방식을 바꾸거나 콘솔에서 병합하세요.");
                else
                    AppendOutput("병합·rebase가 중간에 멈췄다면 충돌을 콘솔/IDE에서 해결하세요 (git status로 확인, 취소는 git merge --abort / git rebase --abort).");
            });
        }

        private async void Push_Click(object sender, RoutedEventArgs e)
        {
            var row = SelectedRow;
            var snapshot = row?.Snapshot;
            if (snapshot == null)
                return;
            if (snapshot.IsDetached || string.IsNullOrEmpty(snapshot.Branch))
            {
                MessageBox.Show(this, "현재 브랜치가 없어(detached HEAD) push할 수 없습니다.", "push", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (snapshot.Upstream != null)
            {
                await RunOnSelectedAsync("push 중…", new[] { "push" }, GitCommandRunner.NetworkTimeout);
                return;
            }

            // upstream이 없으면 원격을 골라 -u로 연결한다. 원격이 여러 개면 origin 우선, 없으면 첫 번째.
            GitResult remotes;
            try
            {
                remotes = await GitCommandRunner.RunAsync(row.RootPath, new[] { "remote" }, GitCommandRunner.QueryTimeout, _lifetime.Token, readOnly: true);
            }
            catch (Exception ex)
            {
                if (!(ex is OperationCanceledException))
                    AppendOutput("오류: " + ex.Message);
                return;
            }
            var names = remotes.StdOut.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
            if (names.Count == 0)
            {
                MessageBox.Show(this, "이 저장소에 원격(remote)이 없습니다. 콘솔에서 git remote add로 먼저 등록하세요.", "push", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var remote = names.Contains("origin") ? "origin" : names[0];
            if (MessageBox.Show(this,
                    string.Format("'{0}' 브랜치에 upstream이 없습니다.\n{1}/{0}(으)로 push하고 upstream으로 설정할까요?", snapshot.Branch, remote),
                    "push", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            await RunOnSelectedAsync("push 중…", new[] { "push", "-u", remote, snapshot.Branch }, GitCommandRunner.NetworkTimeout);
        }

        // ── 외부 비교 도구 ─────────────────────────────────────────────────────

        private async void DiffView_ExternalToolRequested(object sender, EventArgs e)
        {
            var request = (sender as GitDiffView)?.CurrentRequest;
            var row = SelectedRow;
            if (request == null || row == null)
                return;

            var mode = _settings.DiffToolMode;
            if (mode == GitDiffToolMode.None)
            {
                if (MessageBox.Show(this, "외부 비교 도구가 지정되어 있지 않습니다.\n설정 > Git에서 도구를 지정할까요?", "외부 비교 도구",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    _openGitSettings?.Invoke();
                return;
            }

            try
            {
                if (mode == GitDiffToolMode.Custom && !File.Exists(_settings.DiffToolPath))
                {
                    MessageBox.Show(this, "지정한 비교 도구 실행 파일이 없습니다:\n" + _settings.DiffToolPath + "\n\n설정 > Git에서 경로를 확인하세요.",
                        "외부 비교 도구", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (mode == GitDiffToolMode.GitConfig && !await HasConfiguredDiffToolAsync(row))
                {
                    MessageBox.Show(this, "git 설정에 difftool이 없습니다 (diff.tool / merge.tool).\n" +
                        "git config --global diff.tool <도구>로 설정하거나, 설정 > Git에서 '직접 지정'을 고르세요.",
                        "외부 비교 도구", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var args = GitDiffCommands.ExternalTool(request, mode, _settings.DiffToolPath, _settings.DiffToolArguments);
                AppendOutput("외부 비교 도구 실행: " + GitCommandRunner.FormatCommandLine(args) + "    [" + row.Info.DisplayPath + "]");

                // 도구 창을 닫을 때까지 git difftool이 끝나지 않으므로: 시간 제한 없음, 동시 실행 제한 밖,
                // Git 창을 닫아도 도구가 같이 꺼지지 않도록 창 수명 토큰을 쓰지 않는다.
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var result = await GitCommandRunner.RunAsync(row.RootPath, args, Timeout.InfiniteTimeSpan, CancellationToken.None, throttle: false);
                watch.Stop();

                if (!result.Success || !string.IsNullOrWhiteSpace(result.StdErr))
                    AppendResult(row.Info, result);
                else if (watch.Elapsed < TimeSpan.FromSeconds(2))
                    AppendOutput("비교 도구가 바로 종료됐습니다. 도구 창이 비어 있거나 파일을 못 찾으면, 창을 닫을 때까지 기다리도록 인수를 설정하세요 " +
                                 "(git은 도구가 끝나면 임시 파일을 지웁니다. VS Code: --wait, WinMerge: 단일 인스턴스 옵션 끄기).");
            }
            catch (Exception ex)
            {
                AppendOutput("외부 비교 도구를 실행하지 못했습니다: " + ex.Message);
            }
        }

        private async Task<bool> HasConfiguredDiffToolAsync(RepositoryRow row)
        {
            foreach (var key in new[] { "diff.tool", "merge.tool" })
            {
                var result = await GitCommandRunner.RunAsync(row.RootPath, new[] { "config", "--get", key },
                    GitCommandRunner.QueryTimeout, _lifetime.Token, readOnly: true);
                if (result.Success && !string.IsNullOrWhiteSpace(result.StdOut))
                    return true;
            }
            return false;
        }

        // ── 설정 ────────────────────────────────────────────────────────────

        private void Options_Click(object sender, RoutedEventArgs e)
        {
            _openGitSettings?.Invoke();
        }

        /// <summary>설정 창에서 저장한 값을 반영한다. 탐색·git 경로가 바뀌면 다시 찾고, 표시 옵션만 바뀌면 상세만 다시 읽는다.</summary>
        public async void ApplySettings(GitSettings settings)
        {
            if (settings == null || _lifetime.IsCancellationRequested)
                return;

            var previous = _settings;
            _settings = settings.Clone();
            GitCommandRunner.ConfiguredGitPath = _settings.GitExecutablePath;

            if (_busyCount > 0)
            {
                AppendOutput("설정이 바뀌었습니다. 진행 중인 작업이 끝나면 '다시 찾기'로 반영하세요.");
                return;
            }

            try
            {
                if (!string.Equals(previous.GitExecutablePath, _settings.GitExecutablePath, StringComparison.OrdinalIgnoreCase))
                {
                    if (await CheckGitAsync())
                        await ScanAsync();
                    return;
                }

                var scanChanged = previous.ScanDepth != _settings.ScanDepth
                    || !previous.ExcludedFolders.SequenceEqual(_settings.ExcludedFolders, StringComparer.OrdinalIgnoreCase);
                if (scanChanged)
                {
                    await ScanAsync();
                    return;
                }

                if (previous.DiffViewMode != _settings.DiffViewMode)
                {
                    // 기본 보기가 바뀌면 세 diff 창 모두 새 기본값으로 맞춘다(창에서 따로 바꿔 둔 값은 덮어씀).
                    ChangesDiff.ViewMode = RemoteDiff.ViewMode = LogDiff.ViewMode = _settings.DiffViewMode;
                }

                if (previous.LogLimit != _settings.LogLimit || previous.LogAllBranches != _settings.LogAllBranches
                    || previous.IgnoreWhitespace != _settings.IgnoreWhitespace || previous.FallbackEncoding != _settings.FallbackEncoding
                    || previous.DiffViewMode != _settings.DiffViewMode)
                    await ReloadSelectedDetailAsync();
            }
            catch (Exception ex)
            {
                AppendOutput("설정 반영 중 오류: " + ex.Message);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Folderss.Controls;
using Folderss.Models;
using Folderss.Services;

namespace Folderss
{
    /// <summary>
    /// 기준 폴더 아래의 여러 Git 저장소를 보여 주고, 선택한 저장소 하나에 대해 스테이지·커밋·브랜치·로그·pull/push를 한다.
    /// 여러 저장소에 한꺼번에 하는 동작은 상태 조회와 툴바 `전체` 그룹의 fetch·pull·push(<see cref="RunOnAllAsync"/> — 저장소마다 독립 실행, 성공·실패·건너뜀 요약).
    /// 일괄 pull/push는 부분 실패 복구가 어려우므로 pull은 설정의 방식(기본 fast-forward만)으로, push는 upstream으로만 보내고
    /// detached HEAD·upstream 없는 저장소는 건너뛴다(<see cref="GitSyncCommands.BatchSkipReason"/>).
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

        /// <summary>탐색 기준 폴더. 창 안의 "변경…" 버튼으로 바꿀 수 있다(<see cref="ChangeBasePath_Click"/>).</summary>
        private string _basePath;
        private readonly Func<string, int> _countModifiedDocumentsUnder;
        private readonly Action<string> _openFile;
        private readonly Action<string> _openSettings;
        private readonly ObservableCollection<RepositoryRow> _rows = new ObservableCollection<RepositoryRow>();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private CancellationTokenSource _operation;
        private GitSettings _settings;
        private DiffSettings _diff;
        private int _busyCount;
        private List<GitBranchInfo> _branches = new List<GitBranchInfo>();

        /// <summary>브랜치 그래프를 읽은(읽는 중인) 저장소. 전체 히스토리를 걸으므로 브랜치 탭을 볼 때만 읽는다.</summary>
        private RepositoryRow _branchGraphRow;

        /// <summary>환경 변수 <c>FOLDERSS_GIT_TIMING=1</c>이면 git 명령·화면 갱신 시간을 출력창에 ⏱ 줄로 남긴다(실측용).</summary>
        private static readonly bool TimingEnabled = Environment.GetEnvironmentVariable("FOLDERSS_GIT_TIMING") == "1";

        /// <summary>네이티브 libgit2를 불러오지 못했으면 true — 이후 조회는 LibGit2Sharp를 건너뛰고 git CLI로만 읽는다.</summary>
        private static bool _libGit2Unavailable;

        /// <param name="basePath">탐색 기준 폴더.</param>
        /// <param name="countModifiedDocumentsUnder">폴더 아래 경로로 열린 미저장 뷰어 개수 — 브랜치 전환·pull 전 경고용.</param>
        /// <param name="openFile">파일 더블클릭 시 기존 뷰어로 연다.</param>
        /// <param name="diffSettings">diff 보기·공백·인코딩·외부 도구(설정 창의 비교 탭).</param>
        /// <param name="openSettings">설정 창의 해당 탭("Git", "Diff")을 연다. 저장되면 메인 창이 <see cref="ApplySettings"/>로 알려 준다.</param>
        public GitWindow(string basePath, GitSettings settings, DiffSettings diffSettings, Func<string, int> countModifiedDocumentsUnder,
            Action<string> openFile, Action<string> openSettings)
        {
            InitializeComponent();
            _basePath = basePath;
            _countModifiedDocumentsUnder = countModifiedDocumentsUnder ?? (_ => 0);
            _openFile = openFile;
            _openSettings = openSettings;
            _settings = (settings ?? new GitSettings()).Clone();
            _diff = (diffSettings ?? new DiffSettings()).Clone();
            GitCommandRunner.ConfiguredGitPath = _settings.GitExecutablePath;
            ApplyWindowState(GitWindowStateService.Load());

            foreach (var view in new[] { ChangesDiff, RemoteDiff, LogDiff, StashDiff, BranchDiff })
            {
                view.ExternalToolRequested += DiffView_ExternalToolRequested;
                view.ViewModeChanged += DiffView_ViewModeChanged;
                view.ViewMode = _diff.DiffViewMode;
                view.TextLoader = LoadReportTextAsync;
            }

            ShowBasePath();
            RepoList.ItemsSource = _rows;
            SectionNav.SelectedIndex = 0;
            UpdateButtons();
            if (TimingEnabled)
                GitCommandRunner.Completed += GitCommand_Completed;
        }

        // ── 실측 계측 ─────────────────────────────────────────────────────────

        private void GitCommand_Completed(string repository, GitResult result)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // 다른 Git 창의 명령은 찍지 않는다(이벤트가 정적이라 모든 창에 온다).
                var row = repository == null ? null : _rows.FirstOrDefault(r => string.Equals(r.RootPath, repository, StringComparison.OrdinalIgnoreCase));
                if (repository != null && row == null)
                    return;
                Timing(string.Format("{0} ms (대기 {1} ms)  {2}{3}", result.ElapsedMilliseconds, result.WaitMilliseconds,
                    result.CommandLine, row == null ? string.Empty : "    [" + row.Info.DisplayPath + "]"));
            }));
        }

        private void Timing(string text)
        {
            if (TimingEnabled)
                AppendOutput("⏱ " + text);
        }

        /// <summary>
        /// 조회를 git 프로세스 없이 LibGit2Sharp로 읽는다(<see cref="GitLibReader"/>). 실패하면 Ok=false — 호출자가 git CLI로 다시 읽는다.
        /// 지원하지 않는 경우(링크된 워킹트리, upstream 없음 등)는 조용히, 그 밖의 예외는 출력창에 알리고 넘어간다.
        /// </summary>
        private async Task<(bool Ok, T Value)> ReadLibAsync<T>(RepositoryRow row, string what, Func<T> read)
        {
            if (_libGit2Unavailable)
                return (false, default);
            var watch = Stopwatch.StartNew();
            try
            {
                var value = await Task.Run(read);
                Timing(string.Format("{0} ms  LibGit2Sharp {1}    [{2}]", watch.ElapsedMilliseconds, what, row.Info.DisplayPath));
                return (true, value);
            }
            catch (NotSupportedException ex)
            {
                Timing(string.Format("LibGit2Sharp {0} 건너뜀({1}) → git으로    [{2}]", what, ex.Message, row.Info.DisplayPath));
            }
            catch (Exception ex) when (ex is TypeInitializationException || ex is DllNotFoundException || ex is BadImageFormatException)
            {
                // 네이티브 libgit2(runtimes\<rid>\native\git2-*.dll)를 못 불러오면 다시 시도해도 같다. 앱을 다시 켤 때까지 git CLI만 쓴다.
                _libGit2Unavailable = true;
                AppendOutput("LibGit2Sharp를 불러오지 못해 git 명령으로만 읽습니다(느림). runtimes 폴더의 git2 DLL이 함께 배포됐는지 확인하세요: "
                    + (ex.InnerException ?? ex).Message);
            }
            catch (Exception ex)
            {
                AppendOutput(string.Format("LibGit2Sharp로 {0}을(를) 읽지 못해 git으로 다시 읽습니다: {1}    [{2}]", what, ex.Message, row.Info.DisplayPath));
            }
            return (false, default);
        }

        private RepositoryRow SelectedRow => RepoList.SelectedItem as RepositoryRow;

        // ── 수명·진행 상태 ─────────────────────────────────────────────────────

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // git이 있는지는 파일 확인이라 바로 본다. 버전 확인은 git 프로세스라(회사 PC에서 1~2초) 탐색과 동시에 돌리고
            // 맞지 않으면 그때 창을 닫는다. 탐색·상태 읽기는 LibGit2Sharp라 git을 기다릴 필요가 없다.
            if (!CheckGitExists())
            {
                Close();
                return;
            }
            var versionCheck = CheckGitVersionAsync();
            var scan = ScanAsync();
            if (!await versionCheck)
            {
                Close();
                return;
            }
            await scan;
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            // 창을 닫으면 진행 중인 탐색·git 프로세스를 모두 끝낸다(러너가 프로세스 트리를 종료).
            _lifetime.Cancel();
            GitCommandRunner.Completed -= GitCommand_Completed;
        }

        /// <summary>git 실행 파일 확인 후 버전 확인(설정에서 git 경로를 바꿨을 때 — 사용자가 바꾼 것이라 순서대로 기다린다).</summary>
        private async Task<bool> CheckGitAsync() => CheckGitExists() && await CheckGitVersionAsync();

        /// <summary>git 실행 파일이 있는지(파일 확인이라 바로 끝남). 없으면 안내하고 false.</summary>
        private bool CheckGitExists()
        {
            if (GitCommandRunner.FindGit() != null)
                return true;
            MessageBox.Show(this,
                "git 실행 파일을 찾을 수 없습니다.\nGit for Windows를 설치하고 PATH에 등록한 뒤 다시 여세요.",
                "Git", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        /// <summary><c>git --version</c>으로 최소 버전을 확인한다. 안 맞거나 실행할 수 없으면 안내하고 false.</summary>
        private async Task<bool> CheckGitVersionAsync()
        {
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

            var hasRows = idle && _rows.Count > 0;
            RescanButton.IsEnabled = ChangeBasePathButton.IsEnabled = idle;
            FetchAllButton.IsEnabled = FetchAllOptionsButton.IsEnabled = hasRows;
            PullAllButton.IsEnabled = PullAllOptionsButton.IsEnabled = hasRows;
            PushAllButton.IsEnabled = hasRows;
            OptionsButton.IsEnabled = idle;
            CancelButton.IsEnabled = !idle;
            FetchButton.IsEnabled = FetchOptionsButton.IsEnabled = idle && hasRepo;
            PullButton.IsEnabled = PullOptionsButton.IsEnabled = idle && hasRepo;
            PushButton.IsEnabled = PushOptionsButton.IsEnabled = idle && hasRepo;
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
                var scanWatch = Stopwatch.StartNew();

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
                Timing(string.Format("탐색+상태 읽기 전체 {0} ms (저장소 {1}개)", scanWatch.ElapsedMilliseconds, result.Repositories.Count));

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
            var lib = await ReadLibAsync(row, "status", () => GitLibReader.ReadStatus(row.RootPath));
            if (lib.Ok)
            {
                row.Snapshot = lib.Value;
            }
            else
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

        /// <summary>창 제목과 목록 위 "기준: …" 줄을 현재 기준 폴더로 맞춘다.</summary>
        private void ShowBasePath()
        {
            Title = "Git — " + _basePath;
            BasePathText.Text = "기준: " + _basePath;
            BasePathText.ToolTip = _basePath;
        }

        /// <summary>
        /// 기준 폴더를 폴더 선택 대화상자로 바꾸고 그 아래 저장소를 다시 찾는다.
        /// 같은 폴더를 고르면 아무것도 하지 않는다(다시 읽으려면 "다시 찾기"). 드라이브 루트는 메인 창처럼 한 번 확인한다.
        /// </summary>
        private async void ChangeBasePath_Click(object sender, RoutedEventArgs e)
        {
            if (_busyCount > 0)
                return;

            string picked;
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Git 저장소를 찾을 기준 폴더",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false,
                SelectedPath = Directory.Exists(_basePath) ? _basePath : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            })
            {
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return;
                picked = dialog.SelectedPath;
            }

            if (string.IsNullOrWhiteSpace(picked) || !Directory.Exists(picked))
            {
                MessageBox.Show(this, "폴더를 찾을 수 없습니다.", "Git", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var fullPath = Path.GetFullPath(picked);
            if (string.Equals(NormalizePath(fullPath), NormalizePath(_basePath), StringComparison.OrdinalIgnoreCase))
                return;

            // 드라이브 루트 전체 탐색은 수 분 걸릴 수 있어 한 번 묻는다(메인 창의 Git 열기와 같은 규칙).
            if (string.Equals(fullPath.TrimEnd('\\'), (Path.GetPathRoot(fullPath) ?? string.Empty).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                && MessageBox.Show(this, string.Format("드라이브 루트({0}) 전체에서 저장소를 찾습니다. 오래 걸릴 수 있습니다. 계속할까요?", fullPath),
                    "Git", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;

            _basePath = fullPath;
            ShowBasePath();
            AppendOutput("기준 폴더 변경: " + fullPath);
            await ScanAsync();
        }

        private async void FetchAll_Click(object sender, RoutedEventArgs e)
        {
            await FetchAllAsync(new GitFetchOptions());
        }

        private async void FetchAllOptions_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new GitFetchDialog(string.Format("목록의 저장소 {0}개 모두에서 fetch합니다.", _rows.Count)) { Owner = this };
            if (dialog.ShowDialog() == true)
                await FetchAllAsync(dialog.Options);
        }

        private Task FetchAllAsync(GitFetchOptions options)
        {
            return RunOnAllAsync("fetch", GitSyncCommands.Fetch(options), skipReason: null);
        }

        private async void PullAll_Click(object sender, RoutedEventArgs e)
        {
            await PullAllAsync(new GitPullOptions { Mode = _settings.PullMode });
        }

        private async void PullAllOptions_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new GitPullDialog(
                string.Format("목록의 저장소 {0}개 모두에서 pull합니다. detached HEAD·upstream 없는 저장소는 건너뜁니다.\n" +
                              "병합·rebase가 충돌로 멈추면 저장소마다 따로 해결해야 합니다.", _rows.Count),
                new GitPullOptions { Mode = _settings.PullMode }) { Owner = this };
            if (dialog.ShowDialog() == true)
                await PullAllAsync(dialog.Options);
        }

        private async Task PullAllAsync(GitPullOptions options)
        {
            if (!ConfirmNoUnsavedDocumentsUnderAll("전체 pull"))
                return;
            await RunOnAllAsync("pull", GitSyncCommands.Pull(options), row => GitSyncCommands.BatchSkipReason(row.Snapshot));
        }

        /// <summary>전체 push: 저장소마다 현재 브랜치를 upstream으로. upstream이 없으면 건너뛴다(-u는 단일 push에서만).</summary>
        private async void PushAll_Click(object sender, RoutedEventArgs e)
        {
            await RunOnAllAsync("push", new[] { "push" }, row => GitSyncCommands.BatchSkipReason(row.Snapshot));
        }

        /// <summary>
        /// 목록의 모든 저장소에 같은 명령을 동시에 실행하고(저장소마다 독립), 끝나면 성공·실패·건너뜀 수를 요약한다.
        /// 취소돼도 저장소마다 상태는 다시 읽는다(pull은 일부만 적용됐을 수 있음).
        /// </summary>
        /// <param name="skipReason">건너뛸 저장소면 이유, 아니면 null. null이면 모두 실행.</param>
        private async Task RunOnAllAsync(string verb, IEnumerable<string> args, Func<RepositoryRow, string> skipReason)
        {
            var rows = _rows.ToList();
            var argList = args.ToList();
            await RunBusyAsync(string.Format("{0} 중… ({1}개)", verb, rows.Count), async token =>
            {
                int failed = 0, skipped = 0;
                await Task.WhenAll(rows.Select(async row =>
                {
                    var reason = skipReason?.Invoke(row);
                    if (reason != null)
                    {
                        skipped++;
                        AppendOutput(string.Format("{0} 건너뜀: {1}    [{2}]", verb, reason, row.Info.DisplayPath));
                        return;
                    }
                    GitResult result;
                    try
                    {
                        result = await GitCommandRunner.RunAsync(row.RootPath, argList, GitCommandRunner.NetworkTimeout, token);
                    }
                    finally
                    {
                        await RefreshStatusAsync(row, _lifetime.Token);
                    }
                    AppendResult(row.Info, result);
                    if (!result.Success)
                        failed++;
                }));
                AppendOutput(string.Format("전체 {0} 완료: 성공 {1}, 실패 {2}, 건너뜀 {3}", verb, rows.Count - failed - skipped, failed, skipped));
            });
            await ReloadSelectedDetailAsync();
        }

        // ── 선택 저장소 fetch ──────────────────────────────────────────────────

        private async void Fetch_Click(object sender, RoutedEventArgs e)
        {
            await RunOnSelectedAsync("fetch 중…", GitSyncCommands.Fetch(new GitFetchOptions()), GitCommandRunner.NetworkTimeout);
        }

        private async void FetchOptions_Click(object sender, RoutedEventArgs e)
        {
            var row = SelectedRow;
            if (row == null)
                return;
            var dialog = new GitFetchDialog(string.Format("'{0}'에서 fetch합니다.", row.Info.DisplayPath)) { Owner = this };
            if (dialog.ShowDialog() == true)
                await RunOnSelectedAsync("fetch 중…", GitSyncCommands.Fetch(dialog.Options), GitCommandRunner.NetworkTimeout);
        }

        // ── 선택 저장소 상세 ──────────────────────────────────────────────────

        private async void RepoList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateButtons();
            await ReloadSelectedDetailAsync();
        }

        private void RepoList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // ListBox는 오른쪽 버튼으로 선택하지 않으므로 메뉴가 열리기 전에 누른 저장소를 선택한다.
            if (ItemsControl.ContainerFromElement(RepoList, e.OriginalSource as DependencyObject) is ListBoxItem item && !item.IsSelected)
                item.IsSelected = true;
        }

        private void RepoList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            // 빈 곳을 눌렀거나 작업 중이면 메뉴를 열지 않는다.
            if (SelectedRow == null || _busyCount > 0)
                e.Handled = true;
        }

        // ── 저장소 설정(git config) ──────────────────────────────────────────

        /// <summary>
        /// 선택 저장소의 글로벌·로컬 설정을 읽어 <see cref="GitConfigDialog"/>로 보여 주고, 바뀐 값만 <c>git config</c>로 저장한다.
        /// 글로벌 파일이 아직 없으면(종료 코드 128 "unable to read config file") 빈 설정으로 본다.
        /// </summary>
        private async void RepoConfig_Click(object sender, RoutedEventArgs e)
        {
            var row = SelectedRow;
            if (row == null || _busyCount > 0)
                return;

            List<GitConfigEntry> global = null, local = null;
            await RunBusyAsync("설정 읽는 중…", async token =>
            {
                global = await ReadConfigAsync(row, GitConfigScope.Global, token);
                local = await ReadConfigAsync(row, GitConfigScope.Local, token);
            });
            if (global == null || local == null)
                return; // 읽기 실패·취소 — 원인은 출력 영역에 남음

            var dialog = new GitConfigDialog(row.Info.DisplayPath, row.RootPath, global, local) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            var changes = dialog.Changes;
            if (changes.Count == 0)
            {
                AppendOutput("바뀐 설정이 없습니다.    [" + row.Info.DisplayPath + "]");
                return;
            }

            await RunBusyAsync("설정 저장 중…", async token =>
            {
                foreach (var change in changes)
                {
                    var result = await GitCommandRunner.RunAsync(row.RootPath, GitConfigCommands.Apply(change), GitCommandRunner.QueryTimeout, token);
                    AppendResult(row.Info, result);
                    if (!result.Success)
                    {
                        AppendOutput("설정 저장을 중단했습니다. 뒤의 항목은 적용되지 않았습니다.");
                        return;
                    }
                }
                AppendOutput(string.Format("설정 {0}개를 저장했습니다.", changes.Count));
            });
        }

        private async Task<List<GitConfigEntry>> ReadConfigAsync(RepositoryRow row, GitConfigScope scope, CancellationToken token)
        {
            var result = await GitCommandRunner.RunAsync(row.RootPath, GitConfigCommands.List(scope), GitCommandRunner.QueryTimeout, token, readOnly: true);
            if (result.Success)
                return GitConfigCommands.ParseList(result.StdOut);
            if (scope == GitConfigScope.Global && result.ExitCode == 128 && result.StdErr.Contains("unable to read config file"))
                return new List<GitConfigEntry>();
            AppendResult(row.Info, result);
            throw new InvalidOperationException((scope == GitConfigScope.Global ? "글로벌" : "로컬") + " 설정을 읽지 못했습니다.");
        }

        private void SectionNav_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var tag = (SectionNav.SelectedItem as ListBoxItem)?.Tag as string;
            ChangesSection.Visibility = tag == "Changes" ? Visibility.Visible : Visibility.Collapsed;
            BranchesSection.Visibility = tag == "Branches" ? Visibility.Visible : Visibility.Collapsed;
            RemoteSection.Visibility = tag == "Remote" ? Visibility.Visible : Visibility.Collapsed;
            LogSection.Visibility = tag == "Log" ? Visibility.Visible : Visibility.Collapsed;
            WorktreesSection.Visibility = tag == "Worktrees" ? Visibility.Visible : Visibility.Collapsed;
            StashSection.Visibility = tag == "Stash" ? Visibility.Visible : Visibility.Collapsed;
            if (tag == "Branches" && IsLoaded)
                EnsureBranchGraph();
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
            BranchGraphList.ItemsSource = null;
            _branchGraphRow = null;
            _fillingBranchBases = true;
            BranchCompareBaseCombo.ItemsSource = null;
            _fillingBranchBases = false;
            ClearBranchFiles(BranchFilesHint);
            WorktreeList.ItemsSource = null;
            _worktrees = new List<GitWorktreeInfo>();
            StashList.ItemsSource = null;
            StashDiff.Clear("stash를 선택하면 변경을 보여 줍니다.");
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
            ClearLogFiles("커밋을 선택하면 바뀐 파일을 보여 줍니다.");
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

            // 변경됨: 수정된 파일과 새 파일(추적 안 됨)을 한 목록에 경로순으로 섞어 보이고, 충돌만 맨 위.
            // 스테이지됨: 인덱스 변경. 둘 다 바뀐 파일(MM, AM)은 양쪽에 보이며 배지는 각 목록 기준(ForSide).
            var unstaged = snapshot.Entries.Where(entry => entry.IsUnstaged)
                .Select(entry => entry.ForSide(GitChangeSide.WorkTree))
                .OrderBy(entry => entry.IsConflicted ? 0 : 1)
                .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var staged = snapshot.Entries.Where(entry => entry.IsStaged)
                .Select(entry => entry.ForSide(GitChangeSide.Index))
                .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            UnstagedList.ItemsSource = unstaged;
            StagedList.ItemsSource = staged;
            UnstagedTree.ItemsSource = GitChangeTree.Build(unstaged);
            StagedTree.ItemsSource = GitChangeTree.Build(staged);

            if (UnchangedToggle.IsChecked == true)
                LoadUnchanged(row);
            UnstagedHeader.Text = FormatListHeader("변경됨", unstaged);
            StagedHeader.Text = FormatListHeader("스테이지됨", staged);
        }

        /// <summary>"변경됨 (5) — 수정 3 · 새 파일 2"</summary>
        private static string FormatListHeader(string title, List<GitStatusEntry> entries)
        {
            return entries.Count == 0
                ? title + " (0)"
                : string.Format("{0} ({1}) — {2}", title, entries.Count, GitStatusEntry.Summarize(entries));
        }

        private async Task ReloadSelectedDetailAsync()
        {
            var row = SelectedRow;
            ClearDetail();
            if (row == null)
                return;

            var watch = Stopwatch.StartNew();
            ShowChanges(row);
            try
            {
                await LoadBranchesAndLogAsync(row, _lifetime.Token);
                // 도중에 다른 저장소를 골랐으면 중간에 끝난 것이라 찍지 않는다.
                if (ReferenceEquals(row, SelectedRow))
                    Timing(string.Format("저장소 선택 → 상세 읽기 전체 {0} ms    [{1}]", watch.ElapsedMilliseconds, row.Info.DisplayPath));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                // async void 이벤트에서 부르므로 여기서 막지 않으면 앱이 종료된다.
                AppendOutput("오류: " + ex.Message);
            }
            finally
            {
                EndDetailLoadingStatus(row);
            }
        }

        /// <summary>
        /// 저장소(워킹트리)를 골라 상세를 읽는 동안 단계를 상태 줄에 보인다. 링크된 워킹트리는 LibGit2Sharp로 못 읽어 git CLI로 읽으므로 느리다.
        /// 다른 작업(<see cref="RunBusyAsync"/>) 중이면 그 상태를 덮지 않고, 그새 다른 저장소를 골랐으면 그쪽이 표시한다.
        /// </summary>
        private void ShowDetailLoadingStatus(RepositoryRow row, string stage)
        {
            if (_busyCount > 0 || !ReferenceEquals(row, SelectedRow))
                return;
            var branch = row.Snapshot?.Branch;
            StatusText.Text = string.Format("{0}{1} 불러오는 중… {2}",
                row.Info.DisplayPath, string.IsNullOrEmpty(branch) ? string.Empty : " [" + branch + "]", stage);
        }

        private void EndDetailLoadingStatus(RepositoryRow row)
        {
            if (_busyCount > 0 || !ReferenceEquals(row, SelectedRow) || _lifetime.IsCancellationRequested)
                return;
            StatusText.Text = string.Format("저장소 {0}개", _rows.Count);
        }

        private async Task LoadBranchesAndLogAsync(RepositoryRow row, CancellationToken token)
        {
            var skipLog = row.Snapshot?.HeadOid == null && !_settings.LogAllBranches;   // 최초 커밋 전: log가 실패하므로 부르지 않는다
            var limit = _settings.LogLimit;
            var all = _settings.LogAllBranches;
            ShowDetailLoadingStatus(row, "브랜치·로그");
            var libBranches = await ReadLibAsync(row, "브랜치", () => GitLibReader.ReadBranches(row.RootPath));
            var libLog = skipLog
                ? (Ok: false, Value: (List<GitCommitInfo>)null)
                : await ReadLibAsync(row, "로그", () => GitLibReader.ReadLog(row.RootPath, limit, all));

            // LibGit2Sharp로 못 읽은 것만 git CLI로 읽는다.
            var branchTask = libBranches.Ok
                ? Task.FromResult<GitResult>(null)
                : GitCommandRunner.RunAsync(row.RootPath,
                    new[] { "for-each-ref", GitOutputParser.BranchFormat, "refs/heads", "refs/remotes" },
                    GitCommandRunner.QueryTimeout, token, readOnly: true);

            var logArgs = new List<string> { "log", "--topo-order", "-z", "-n", limit.ToString(), GitOutputParser.LogFormat };
            if (all)
                logArgs.Add("--all");
            var logTask = skipLog || libLog.Ok
                ? Task.FromResult<GitResult>(null)
                : GitCommandRunner.RunAsync(row.RootPath, logArgs, GitCommandRunner.QueryTimeout, token, readOnly: true);

            var branchResult = await branchTask;
            var logResult = await logTask;

            // 기다리는 사이 다른 저장소를 골랐으면 버린다.
            if (!ReferenceEquals(row, SelectedRow))
                return;

            if (libBranches.Ok || branchResult.Success)
            {
                _branches = (libBranches.Ok ? libBranches.Value : GitOutputParser.ParseBranches(branchResult.StdOut))
                    .OrderBy(b => b.IsRemote).ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();
                ShowBranchList();
            }
            else
            {
                AppendResult(row.Info, branchResult);
            }

            if (libLog.Ok || logResult != null)
            {
                // 커밋이 하나도 없는 저장소의 log 실패는 정상이므로 출력하지 않는다.
                if (libLog.Ok || logResult.Success)
                {
                    var logWatch = Stopwatch.StartNew();
                    var commits = libLog.Ok ? libLog.Value : GitOutputParser.ParseLog(logResult.StdOut);
                    var graph = GitGraphLayout.Compute(commits);
                    var maxLanes = 1;
                    for (var i = 0; i < commits.Count; i++)
                    {
                        commits[i].Graph = graph[i];
                        maxLanes = Math.Max(maxLanes, graph[i].LaneCount);
                    }
                    GraphColumn.Width = GitGraphCell.WidthFor(maxLanes);
                    var parseMs = logWatch.ElapsedMilliseconds;
                    LogList.ItemsSource = commits;
                    if (TimingEnabled)
                    {
                        // Loaded 우선순위는 레이아웃·렌더 다음에 돈다 → 목록을 그리는 데 걸린 시간의 근사치.
                        logWatch.Restart();
                        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                            Timing(string.Format("로그 {0}개: 파싱+그래프 {1} ms, 그리기 {2} ms", commits.Count, parseMs, logWatch.ElapsedMilliseconds))));
                    }
                }
                else if (row.Snapshot?.HeadOid != null)
                    AppendResult(row.Info, logResult);
            }

            if (BranchesSection.Visibility == Visibility.Visible && BranchList.ItemsSource != null && !ReferenceEquals(row, _branchGraphRow))
            {
                ShowDetailLoadingStatus(row, "브랜치 그래프");
                await LoadBranchGraphAsync(row, token);
            }
            ShowDetailLoadingStatus(row, "원격 비교");
            await LoadRemoteAsync(row, token);
            ShowDetailLoadingStatus(row, "워킹트리 목록");
            await LoadWorktreesAsync(row, token);
            ShowDetailLoadingStatus(row, "stash");
            await LoadStashesAsync(row, token);
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
                var (result, text) = await RunDiffAsync(row, diff, view.ViewMode, _lifetime.Token);
                if (text != null)
                {
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

        /// <summary>
        /// diff 실행 + UTF-16/32(BOM) 재비교. 실패하면 Text가 null이고 Result에 원인이 있다.
        /// 파일·커밋을 고를 때마다 도는 조회라 git 프로세스 없이 libgit2로 만들고(<see cref="GitLibDiff"/>), 같게 못 만드는 경우만 git CLI로 돌린다.
        /// </summary>
        private async Task<(GitResult Result, string Text)> RunDiffAsync(RepositoryRow row, GitDiffRequest diff, GitDiffViewMode mode,
            CancellationToken token)
        {
            var fallback = ResolveFallbackEncoding();
            var lib = await ReadLibAsync(row, "diff", () => GitLibDiff.Read(row.RootPath, diff, mode, fallback));
            GitResult result = null;
            var output = lib.Value;
            if (!lib.Ok)
            {
                result = await GitCommandRunner.RunAsync(row.RootPath, GitDiffCommands.WithViewMode(diff.Arguments, mode),
                    GitCommandRunner.QueryTimeout, token, readOnly: true, fallbackEncoding: fallback);
                if (!GitDiffCommands.IsSuccess(result, diff.NoIndex))
                    return (result, null);
                output = result.StdOut;
            }
            token.ThrowIfCancellationRequested();
            // UTF-16/32(BOM) 파일은 git이 바이너리로 보이므로 BOM으로 읽어 텍스트로 다시 비교해 끼운다.
            var text = await GitEncodingDiff.ExpandAsync(row.RootPath, diff, output, _diff.IgnoreWhitespace, fallback, token, mode);
            return (result, text);
        }

        /// <summary>HTML 보고서용: 보고 있는 비교를 보고서 범위(문맥)로 다시 읽는다. 실패는 예외.</summary>
        private async Task<string> LoadReportTextAsync(GitDiffRequest diff, GitDiffViewMode mode, CancellationToken token)
        {
            var row = SelectedRow ?? throw new InvalidOperationException("저장소를 선택하세요.");
            var (result, text) = await RunDiffAsync(row, diff, mode, token);
            return text ?? throw new InvalidOperationException(FirstLine(result.StdErr) ?? "차이를 읽지 못했습니다.");
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
                return GitTextDecoder.Resolve(_diff.FallbackEncoding);
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
                await LoadDiffAsync(ChangesDiff, WithTitle(GitDiffCommands.Untracked(entry.Path, _diff.IgnoreWhitespace),
                    "추적 안 됨 (새 파일): " + entry.Path));
                return;
            }

            await LoadDiffAsync(ChangesDiff, WithTitle(GitDiffCommands.WorkTree(entry.Path, _diff.IgnoreWhitespace),
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
            return LoadDiffAsync(ChangesDiff, WithTitle(GitDiffCommands.Staged(entry.Path, entry.OriginalPath, _diff.IgnoreWhitespace),
                "인덱스 ↔ HEAD (커밋될 내용): " + entry.Path));
        }

        // ── 변경 사항 트리 보기 ──────────────────────────────────────────────

        private bool IsChangesTreeMode => ChangesTreeToggle.IsChecked == true;

        private void ChangesTreeToggle_Click(object sender, RoutedEventArgs e)
        {
            ApplyChangesTreeMode();
            SaveWindowState();
        }

        private void ApplyChangesTreeMode()
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

        // ── 끌어 놓기: 변경됨 → 스테이지됨 = 스테이지, 스테이지됨 → 변경됨 = 언스테이지 ─────

        private const string ChangeDragFormat = "Folderss.GitChangeDrag";

        private sealed class ChangeDragData
        {
            public bool FromStaged { get; set; }
            public List<GitStatusEntry> Entries { get; set; }
        }

        private Point? _dragStart;
        private ListBoxItem _pendingSingleSelect;
        private TreeViewItem _pendingTreeSingleSelect;
        /// <summary>트리별 Shift+클릭 범위의 기준점(마지막으로 누른 노드).</summary>
        private readonly Dictionary<TreeView, GitChangeNode> _treeAnchors = new Dictionary<TreeView, GitChangeNode>();

        private void ChangeDrag_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = null;
            _pendingSingleSelect = null;
            _pendingTreeSingleSelect = null;
            var source = e.OriginalSource as DependencyObject;

            if (sender is ListBox list)
            {
                // 항목 위에서만 끌기 시작(스크롤바·빈 곳 제외).
                if (!(ItemsControl.ContainerFromElement(list, source) is ListBoxItem item))
                    return;
                _dragStart = e.GetPosition(this);
                // 여러 개 선택한 상태에서 선택된 항목을 누르면 ListBox가 곧바로 그 하나만 남긴다 —
                // 끌기로 여러 개를 옮길 수 있게 선택 변경을 놓을 때까지 미룬다.
                if (e.ClickCount == 1 && Keyboard.Modifiers == ModifierKeys.None && item.IsSelected && list.SelectedItems.Count > 1)
                {
                    _pendingSingleSelect = item;
                    item.Focus();
                    e.Handled = true;
                }
                return;
            }

            // 트리: 노드 위에서만(스크롤바·펼침 단추 제외). TreeView는 단일 선택뿐이라 Ctrl/Shift+클릭은 IsMarked로 직접 처리한다.
            for (var node = source; node != null && !(node is TreeView); node = System.Windows.Media.VisualTreeHelper.GetParent(node))
            {
                if (node is System.Windows.Controls.Primitives.ScrollBar || node is System.Windows.Controls.Primitives.ToggleButton)
                    return;
                if (node is TreeViewItem item)
                {
                    HandleTreeClick((TreeView)sender, item, e);
                    return;
                }
            }
        }

        private void HandleTreeClick(TreeView tree, TreeViewItem item, MouseButtonEventArgs e)
        {
            var target = item.DataContext as GitChangeNode;
            if (target == null || e.ClickCount != 1)
            {
                _dragStart = e.GetPosition(this);
                return;
            }

            var modifiers = Keyboard.Modifiers;
            if (modifiers == ModifierKeys.Control || modifiers == ModifierKeys.Shift)
            {
                if (modifiers == ModifierKeys.Control)
                {
                    target.IsMarked = !target.IsMarked;
                    _treeAnchors[tree] = target;
                }
                else
                {
                    _treeAnchors.TryGetValue(tree, out var anchor);
                    var range = new HashSet<GitChangeNode>(GitChangeTree.Range(VisibleTreeNodes(tree), anchor, target));
                    foreach (var node in TreeNodes(tree))
                        node.IsMarked = range.Contains(node);
                }
                // TreeView가 누른 노드를 단일 선택으로 바꾸지 않게 막는다(포커스만 트리로).
                e.Handled = true;
                if (!tree.IsKeyboardFocusWithin)
                    tree.Focus();
                ChangesDiff.Clear("파일 하나를 선택하면 차이를 보여 줍니다.");
                return;
            }

            _dragStart = e.GetPosition(this);
            if (modifiers != ModifierKeys.None)
                return;
            // 여러 개 고른 상태에서 고른 노드를 누르면 끌기로 함께 옮길 수 있게 "그 노드만 선택"을 놓을 때까지 미룬다.
            if (target.IsMarked && TreeNodes(tree).Count(node => node.IsMarked) > 1)
            {
                _pendingTreeSingleSelect = item;
                e.Handled = true;
                if (!tree.IsKeyboardFocusWithin)
                    tree.Focus();
                return;
            }
            MarkOnly(tree, target);
        }

        /// <summary>트리의 모든 노드(접힌 폴더 안 포함).</summary>
        private static IEnumerable<GitChangeNode> TreeNodes(TreeView tree)
        {
            return GitChangeTree.AllNodes((tree.ItemsSource as IEnumerable<GitChangeNode>) ?? Enumerable.Empty<GitChangeNode>());
        }

        /// <summary>Shift+클릭 범위용: 화면에 보이는(펼쳐진) 노드를 위에서 아래 순서로.</summary>
        private static List<GitChangeNode> VisibleTreeNodes(ItemsControl parent)
        {
            var result = new List<GitChangeNode>();
            foreach (var item in parent.Items)
            {
                if (!(item is GitChangeNode node))
                    continue;
                result.Add(node);
                if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem container && container.IsExpanded)
                    result.AddRange(VisibleTreeNodes(container));
            }
            return result;
        }

        /// <summary>일반 클릭·키보드 이동: 그 노드 하나만 고른다.</summary>
        private void MarkOnly(TreeView tree, GitChangeNode target)
        {
            foreach (var node in TreeNodes(tree))
                node.IsMarked = ReferenceEquals(node, target);
            _treeAnchors[tree] = target;
        }

        private void ChangeDrag_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // 끌지 않고 놓았으면 미뤄 둔 "그 항목만 선택"을 한다.
            if (_pendingSingleSelect != null && sender is ListBox list)
            {
                var item = _pendingSingleSelect;
                list.UnselectAll();
                item.IsSelected = true;
            }
            if (_pendingTreeSingleSelect?.DataContext is GitChangeNode node && sender is TreeView tree)
            {
                MarkOnly(tree, node);
                _pendingTreeSingleSelect.IsSelected = true;
            }
            _dragStart = null;
            _pendingSingleSelect = null;
            _pendingTreeSingleSelect = null;
        }

        private void ChangeDrag_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragStart == null || e.LeftButton != MouseButtonState.Pressed)
                return;
            var delta = e.GetPosition(this) - _dragStart.Value;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            _dragStart = null;
            _pendingSingleSelect = null;
            _pendingTreeSingleSelect = null;
            var fromStaged = ReferenceEquals(sender, StagedList) || ReferenceEquals(sender, StagedTree);
            var entries = fromStaged ? SelectedChangeEntries(StagedList, StagedTree) : SelectedChangeEntries(UnstagedList, UnstagedTree);
            if (entries.Count == 0 || _busyCount > 0)
                return;

            var data = new DataObject(ChangeDragFormat, new ChangeDragData { FromStaged = fromStaged, Entries = entries });
            try
            {
                DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
            }
            finally
            {
                // 창 밖에 놓거나 Esc로 취소하면 DragLeave가 안 올 수 있어 끝날 때 항상 지운다.
                ShowDropHighlight(null);
            }
        }

        /// <summary>강조할 영역(null이면 모두 끔).</summary>
        private void ShowDropHighlight(object target)
        {
            UnstagedDropHighlight.Visibility = ReferenceEquals(target, UnstagedDropArea) ? Visibility.Visible : Visibility.Collapsed;
            StagedDropHighlight.Visibility = ReferenceEquals(target, StagedDropArea) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ChangeDrop_DragLeave(object sender, DragEventArgs e)
        {
            // 영역 안의 자식 사이를 옮겨 다닐 때도 DragLeave가 오므로, 포인터가 실제로 영역을 벗어났을 때만 끈다.
            var area = (FrameworkElement)sender;
            var point = e.GetPosition(area);
            if (point.X < 0 || point.Y < 0 || point.X >= area.ActualWidth || point.Y >= area.ActualHeight)
                ShowDropHighlight(null);
        }

        /// <summary>반대쪽 목록에서 온 끌기만 받는다(같은 목록·다른 프로그램의 파일은 거부).</summary>
        private ChangeDragData AcceptedDrag(object target, DragEventArgs e)
        {
            var data = e.Data.GetDataPresent(ChangeDragFormat) ? e.Data.GetData(ChangeDragFormat) as ChangeDragData : null;
            var toStaged = ReferenceEquals(target, StagedDropArea);
            return data != null && data.FromStaged != toStaged && _busyCount == 0 ? data : null;
        }

        private void ChangeDrop_DragOver(object sender, DragEventArgs e)
        {
            var accepted = AcceptedDrag(sender, e) != null;
            e.Effects = accepted ? DragDropEffects.Move : DragDropEffects.None;
            ShowDropHighlight(accepted ? sender : null);
            e.Handled = true;
        }

        private async void ChangeDrop_Drop(object sender, DragEventArgs e)
        {
            var data = AcceptedDrag(sender, e);
            e.Handled = true;
            ShowDropHighlight(null);
            if (data == null)
                return;
            if (data.FromStaged)
                await UnstageAsync(data.Entries);
            else
                await StageAsync(data.Entries);
        }

        // ── 보기 상태 저장(트리 보기·분할선 위치) ─────────────────────────────

        private void ApplyWindowState(GitWindowState state)
        {
            ChangesTreeToggle.IsChecked = state.ChangesTreeMode;
            ApplyChangesTreeMode();
            ChangesListColumn.Width = new GridLength(state.ChangesListWidth);
            UnstagedListRow.Height = new GridLength(state.UnstagedWeight, GridUnitType.Star);
            StagedListRow.Height = new GridLength(state.StagedWeight, GridUnitType.Star);
            BranchRemoteToggle.IsChecked = state.BranchesShowRemote;
        }

        private void ChangesSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            SaveWindowState();
        }

        private void SaveWindowState()
        {
            // GridSplitter는 * 행을 * 값으로 바꿔 두지만, 픽셀로 바뀐 경우에도 실제 높이로 비율을 남긴다.
            static double Weight(RowDefinition row) => row.Height.IsStar ? row.Height.Value : row.ActualHeight;
            var state = new GitWindowState
            {
                ChangesTreeMode = IsChangesTreeMode,
                ChangesListWidth = Math.Clamp(ChangesListColumn.ActualWidth > 0 ? ChangesListColumn.ActualWidth : ChangesListColumn.Width.Value,
                    GitWindowState.MinListWidth, GitWindowState.MaxListWidth),
                UnstagedWeight = Math.Clamp(Weight(UnstagedListRow), GitWindowState.MinWeight, GitWindowState.MaxWeight),
                StagedWeight = Math.Clamp(Weight(StagedListRow), GitWindowState.MinWeight, GitWindowState.MaxWeight),
                BranchesShowRemote = BranchRemoteToggle.IsChecked == true
            };
            try
            {
                GitWindowStateService.Save(state);
            }
            catch (Exception ex)
            {
                AppendOutput("보기 상태 저장 실패 (" + GitWindowStateService.DefaultPath + "): " + ex.Message);
            }
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
                    return PreviewLines(File.ReadAllBytes(full), fallback);
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
            MarkOnly(UnstagedTree, node);
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
            MarkOnly(StagedTree, node);
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

        /// <summary>
        /// 변경 사항 목록·트리: Ctrl+C는 폴더 패널처럼 파일 복사(FileDrop) + 이름 텍스트, Ctrl+Shift+C는 전체 경로 텍스트만(여러 개면 한 줄에 하나).
        /// 삭제된 파일처럼 디스크에 없는 항목은 FileDrop에서 빼고 이름 텍스트에만 넣는다.
        /// </summary>
        private void ChangeCopy_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var modifiers = Keyboard.Modifiers;
            if (e.Key != Key.C || (modifiers != ModifierKeys.Control && modifiers != (ModifierKeys.Control | ModifierKeys.Shift)))
                return;
            var root = SelectedRow?.RootPath;
            var paths = SelectedCopyPaths(sender);
            if (root == null || paths.Count == 0)
                return;
            e.Handled = true;
            if (modifiers.HasFlag(ModifierKeys.Shift))
            {
                ClipboardService.TrySetDataObject(GitChangeTree.ClipboardText(paths, root, fullPath: true));
                return;
            }

            var data = new DataObject();
            var files = new System.Collections.Specialized.StringCollection();
            files.AddRange(GitChangeTree.FullPaths(paths, root).Where(path => File.Exists(path) || Directory.Exists(path)).ToArray());
            if (files.Count > 0)
                data.SetFileDropList(files);
            data.SetText(GitChangeTree.ClipboardText(paths, root, fullPath: false), TextDataFormat.UnicodeText);
            // 폴더 패널에서 같은 파일을 잘라 둔 상태가 남으면 붙여넣기가 이동이 되므로 지운다(폴더 패널 복사와 같음).
            if (ClipboardService.TrySetDataObject(data))
                (Application.Current.MainWindow as MainWindow)?.ClearCutStateFromClipboard();
        }

        /// <summary>복사할 저장소 기준 경로: 목록은 선택 항목, 변경됨·스테이지됨 트리는 고른 노드(폴더는 폴더 자신), 변경 없음 트리는 선택 노드.</summary>
        private List<string> SelectedCopyPaths(object sender)
        {
            if (sender is ListBox list)
                return list.SelectedItems.OfType<GitStatusEntry>().Select(entry => entry.Path).ToList();
            var tree = (TreeView)sender;
            if (ReferenceEquals(tree, UnchangedTree))
                return tree.SelectedItem is GitChangeNode node ? new List<string> { node.Path } : new List<string>();
            return TreeNodes(tree).Where(node => node.IsMarked).Select(node => node.Path).ToList();
        }

        /// <summary>
        /// 저장소·브랜치·보낼/받을 커밋·stash·워킹트리·로그 목록: Ctrl+C는 보이는 행 텍스트를 복사한다(여러 개면 한 줄에 하나).
        /// ListBox·ListView는 DataGrid와 달리 기본 복사가 없다. 고른 것이 없으면 키를 가로채지 않는다.
        /// </summary>
        private void RowCopy_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.C || Keyboard.Modifiers != ModifierKeys.Control)
                return;
            var list = (ListBox)sender;
            var lines = list.SelectedItems.Cast<object>().Select(item => RowCopyText(list, item)).ToList();
            if (lines.Count == 0)
                return;
            e.Handled = true;
            ClipboardService.TrySetDataObject(string.Join(Environment.NewLine, lines));
        }

        /// <summary>
        /// 보이는 행 그대로: 저장소는 이름·요약 두 줄을 탭으로, 로그는 글자 열(그래프 제외)을 탭으로(표계산 붙여넣기),
        /// 나머지는 <see cref="ItemsControl.DisplayMemberPath"/> 값. 정렬용 앞뒤 공백(현재 브랜치가 아닌 줄의 들여쓰기)은 뺀다.
        /// </summary>
        private static string RowCopyText(ListBox list, object item)
        {
            if (item is RepositoryRow row)
                return row.Title + "\t" + row.Summary;
            var paths = list is ListView { View: GridView grid }
                ? grid.Columns.Select(column => (column.DisplayMemberBinding as Binding)?.Path.Path).Where(path => path != null)
                : new[] { list.DisplayMemberPath };
            return string.Join("\t", paths.Select(path => Convert.ToString(item.GetType().GetProperty(path)?.GetValue(item)))).Trim(' ');
        }

        /// <summary>스테이지/언스테이지 대상: 평면 보기는 선택한 항목들, 트리 보기는 고른 노드들(폴더면 그 아래 전체).</summary>
        private List<GitStatusEntry> SelectedChangeEntries(ListBox list, TreeView tree)
        {
            if (IsChangesTreeMode)
                return GitChangeTree.MarkedEntries((tree.ItemsSource as IEnumerable<GitChangeNode>) ?? Enumerable.Empty<GitChangeNode>());
            return list.SelectedItems.Cast<GitStatusEntry>().ToList();
        }

        private async void LogList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            LogResetButton.IsEnabled = LogBranchButton.IsEnabled = LogCheckoutButton.IsEnabled = LogList.SelectedItem != null;
            await LoadCommitFilesAsync(LogList.SelectedItem as GitCommitInfo);
        }

        // ── 로그: 선택 커밋의 파일 목록 → 고른 파일 하나의 diff ─────────────────

        /// <summary>파일 목록이 보이고 있는 커밋. 파일을 고르면 이 커밋 기준으로 diff를 만든다.</summary>
        private GitCommitInfo _logFilesCommit;

        private void ClearLogFiles(string diffMessage)
        {
            _logFilesCommit = null;
            LogFileList.ItemsSource = null;
            LogFileTree.ItemsSource = null;
            LogFilesHeader.Text = "파일";
            LogFilesHeader.ToolTip = null;
            LogDiff.Clear(diffMessage);
        }

        private async Task LoadCommitFilesAsync(GitCommitInfo commit)
        {
            var row = SelectedRow;
            ClearLogFiles("커밋을 선택하면 바뀐 파일을 보여 줍니다.");
            if (commit == null || row == null)
                return;

            LogFilesHeader.Text = "파일 (불러오는 중…)";
            try
            {
                // 커밋을 고를 때마다 도는 조회라 git 프로세스 없이 libgit2로 읽고, 못 읽으면 git CLI로.
                var lib = await ReadLibAsync(row, "커밋 파일 목록", () => GitLibReader.ReadCommitFiles(row.RootPath, commit.Hash, commit.Parents));
                var result = lib.Ok
                    ? null
                    : await GitCommandRunner.RunAsync(row.RootPath, GitDiffCommands.CommitFiles(commit.Hash, commit.Parents),
                        GitCommandRunner.QueryTimeout, _lifetime.Token, readOnly: true);
                // 기다리는 사이 다른 커밋·저장소를 골랐으면 버린다.
                if (!ReferenceEquals(commit, LogList.SelectedItem) || !ReferenceEquals(row, SelectedRow))
                    return;
                if (!lib.Ok && !result.Success)
                {
                    LogFilesHeader.Text = "파일 (읽지 못함)";
                    AppendResult(row.Info, result);
                    return;
                }

                var entries = lib.Ok ? lib.Value : GitOutputParser.ParseNameStatus(result.StdOut);
                _logFilesCommit = commit;
                LogFileList.ItemsSource = entries;
                LogFileTree.ItemsSource = GitChangeTree.Build(entries);
                var basis = commit.Parents.Length == 0 ? "최초 커밋" : commit.Parents.Length > 1 ? "병합 커밋 — 첫 부모 기준" : null;
                LogFilesHeader.Text = string.Format("파일 {0}{1}", entries.Count, basis == null ? string.Empty : " (" + basis + ")");
                LogFilesHeader.ToolTip = GitStatusEntry.Summarize(entries);
                LogDiff.Clear(entries.Count == 0 ? "바뀐 파일이 없습니다." : "파일을 선택하면 내용을 보여 줍니다.");
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void LogFilesTreeToggle_Click(object sender, RoutedEventArgs e)
        {
            var tree = LogFilesTreeToggle.IsChecked == true;
            LogFileList.Visibility = tree ? Visibility.Collapsed : Visibility.Visible;
            LogFileTree.Visibility = tree ? Visibility.Visible : Visibility.Collapsed;
            // 두 보기의 선택은 따로라, 전환하면 diff가 어느 선택의 것인지 헷갈리지 않게 비운다.
            LogFileList.UnselectAll();
            if (_logFilesCommit != null)
                LogDiff.Clear("파일을 선택하면 내용을 보여 줍니다.");
        }

        private async void LogFileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LogFileList.SelectedItem is GitStatusEntry entry)
                await ShowCommitFileDiffAsync(entry);
        }

        private async void LogFileTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var node = LogFileTree.SelectedItem as GitChangeNode;
            if (node == null)
                return;
            if (node.IsFolder)
            {
                LogDiff.Clear(string.Format("폴더 {0} — 파일 {1}개", node.Path, node.Entries.Count()));
                return;
            }
            await ShowCommitFileDiffAsync(node.Entry);
        }

        /// <summary>
        /// 로그·브랜치 비교 파일 목록·트리: Ctrl+C는 이름, Ctrl+Shift+C는 전체 경로를 텍스트로만 복사한다(트리 폴더는 폴더 자신).
        /// 변경 사항 탭과 달리 파일 복사(FileDrop)는 하지 않는다 — 작업 트리 파일은 그 커밋의 내용이 아니기 때문.
        /// </summary>
        private void LogFileCopy_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var modifiers = Keyboard.Modifiers;
            if (e.Key != Key.C || (modifiers != ModifierKeys.Control && modifiers != (ModifierKeys.Control | ModifierKeys.Shift)))
                return;
            var root = SelectedRow?.RootPath;
            var path = sender is TreeView tree ? (tree.SelectedItem as GitChangeNode)?.Path : ((sender as ListBox)?.SelectedItem as GitStatusEntry)?.Path;
            if (root == null || string.IsNullOrEmpty(path))
                return;
            e.Handled = true;
            ClipboardService.TrySetDataObject(GitChangeTree.ClipboardText(new[] { path }, root, fullPath: modifiers.HasFlag(ModifierKeys.Shift)));
        }

        /// <summary>고른 파일을 보인다: 기본은 그 커밋 시점 코드 내용, <c>diff 보기</c>를 켜면 변경.</summary>
        private Task ShowCommitFileDiffAsync(GitStatusEntry entry)
        {
            var commit = _logFilesCommit;
            if (commit == null)
                return Task.CompletedTask;
            var title = string.Format("{0}  {1}: {2}", commit.ShortHash, entry.StatusLabel, entry.PathText);
            if (LogFilesDiffToggle.IsChecked != true)
                return ShowRevisionContentAsync(LogDiff, commit.Hash, entry, title + "  — 코드");
            return LoadDiffAsync(LogDiff, WithTitle(GitDiffCommands.CommitFile(commit.Hash, commit.Parents, entry.Path, entry.OriginalPath,
                _diff.IgnoreWhitespace), title));
        }

        private async void LogFilesDiffToggle_Click(object sender, RoutedEventArgs e)
        {
            var entry = SelectedFileEntry(LogFilesTreeToggle, LogFileList, LogFileTree);
            if (entry != null)
                await ShowCommitFileDiffAsync(entry);
        }

        // ── 로그·브랜치 비교 파일: 리비전 시점 코드 내용 ────────────────────────

        /// <summary>파일 목록(평면) 또는 트리에서 고른 파일. 트리의 폴더면 null.</summary>
        private static GitStatusEntry SelectedFileEntry(CheckBox treeToggle, ListBox list, TreeView tree)
        {
            return treeToggle.IsChecked == true
                ? (tree.SelectedItem as GitChangeNode) is GitChangeNode node && !node.IsFolder ? node.Entry : null
                : list.SelectedItem as GitStatusEntry;
        }

        /// <summary>
        /// <paramref name="revision"/> 시점의 파일 내용을 줄 번호와 함께 보인다(BOM 규칙으로 디코딩, 10MB 초과·바이너리는 안내만).
        /// 내용은 git에 저장된 그대로다(core.autocrlf 등 체크아웃 변환 없음). 삭제된 파일은 내용이 없으므로 diff 보기를 안내한다.
        /// </summary>
        private async Task ShowRevisionContentAsync(GitDiffView view, string revision, GitStatusEntry entry, string title)
        {
            var row = SelectedRow;
            if (row == null)
                return;
            if (entry.Kind == GitChangeKind.Deleted)
            {
                view.ShowLines(title, new List<GitDiffLine>(), "삭제된 파일입니다. 'diff 보기'를 켜면 이전 내용을 볼 수 있습니다.");
                return;
            }

            view.ShowLines(title, new List<GitDiffLine>(), "불러오는 중…");
            // 읽는 사이 다른 파일·커밋·폴더를 고르거나 diff로 바꾸면 이 번호가 바뀐다 → 늦게 온 내용은 버린다.
            var id = view.CurrentRequestId;
            try
            {
                var lib = await ReadLibAsync(row, "파일 내용", () => GitLibReader.ReadBlob(row.RootPath, revision, entry.Path, MaxPreviewBytes));
                var (found, bytes) = lib.Ok ? lib.Value : await ReadBlobCliAsync(row, revision, entry.Path);
                var fallback = ResolveFallbackEncoding();
                var (lines, message) = !found ? (new List<GitDiffLine>(), "이 리비전에서 파일을 찾지 못했습니다.")
                    : bytes == null ? (new List<GitDiffLine>(), "파일이 너무 커서(10MB 초과) 미리 보지 않습니다.")
                    : await Task.Run(() => PreviewLines(bytes, fallback));
                if (view.CurrentRequestId == id)
                    view.ShowLines(title, lines, message);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                // async void 이벤트에서 부르므로 여기서 막는다.
                if (view.CurrentRequestId == id)
                    view.ShowLines(title, new List<GitDiffLine>(), "내용을 읽지 못했습니다: " + ex.Message);
            }
        }

        /// <summary><see cref="GitLibReader.ReadBlob"/>의 git CLI 대체(<c>cat-file -s</c>로 크기 확인 후 <c>cat-file blob</c>). 크기 조회 실패는 파일 없음으로 본다.</summary>
        private async Task<(bool Found, byte[] Bytes)> ReadBlobCliAsync(RepositoryRow row, string revision, string path)
        {
            var spec = revision + ":" + path;
            var size = await GitCommandRunner.RunAsync(row.RootPath, new[] { "cat-file", "-s", spec }, GitCommandRunner.QueryTimeout, _lifetime.Token, readOnly: true);
            if (!size.Success)
                return (false, null);
            if (!long.TryParse(size.StdOut.Trim(), out var length) || length > MaxPreviewBytes)
                return (true, null);
            var blob = await GitCommandRunner.RunAsync(row.RootPath, new[] { "cat-file", "blob", spec }, GitCommandRunner.QueryTimeout, _lifetime.Token,
                readOnly: true, rawOutput: true);
            if (!blob.Success)
                throw new InvalidOperationException(FirstLine(blob.StdErr) ?? "cat-file 실패");
            return (true, blob.StdOutBytes);
        }

        /// <summary>파일 바이트를 줄 목록으로. NUL이 있는데 UTF-16/32 BOM이 아니면 바이너리로 본다(git과 같은 기준: 앞 8000바이트).</summary>
        private static (List<GitDiffLine> Lines, string Message) PreviewLines(byte[] bytes, Encoding fallback)
        {
            if (!GitTextDecoder.HasWideBom(bytes) && Array.IndexOf(bytes, (byte)0, 0, Math.Min(bytes.Length, 8000)) >= 0)
                return (new List<GitDiffLine>(), "바이너리 파일입니다.");
            return (GitOutputParser.ContentLines(GitTextDecoder.DecodeFile(bytes, fallback), GitDiffView.MaxLines), "빈 파일입니다.");
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
            return LoadDiffAsync(view, WithTitle(GitDiffCommands.Commit(commit.Hash, commit.Parents, _diff.IgnoreWhitespace), title));
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
            List<GitCommitInfo> outgoingCommits, incomingCommits;
            var lib = await ReadLibAsync(row, "upstream 비교", () => GitLibReader.ReadUpstreamRanges(row.RootPath, RemoteLogLimit));
            if (lib.Ok)
            {
                if (!ReferenceEquals(row, SelectedRow))
                    return;
                (outgoingCommits, incomingCommits) = lib.Value;
            }
            else
            {
                // upstream이 사라졌으면 LibGit2Sharp가 건너뛰고 여기서 git의 오류 문구를 그대로 보여 준다.
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

                outgoingCommits = GitOutputParser.ParseLog(outgoing.StdOut);
                incomingCommits = GitOutputParser.ParseLog(incoming.StdOut);
            }
            OutgoingList.ItemsSource = outgoingCommits;
            IncomingList.ItemsSource = incomingCommits;
            OutgoingHeader.Text = string.Format("보낼 커밋 (push) — {0}{1}개", outgoingCommits.Count, outgoingCommits.Count >= RemoteLogLimit ? "+" : string.Empty);
            IncomingHeader.Text = string.Format("받을 커밋 (pull) — {0}{1}개", incomingCommits.Count, incomingCommits.Count >= RemoteLogLimit ? "+" : string.Empty);
            RemoteSummaryText.Text = string.Format("{0} ↔ {1}   (원격 상태는 마지막 fetch 기준입니다. 최신으로 비교하려면 'fetch 후 비교')",
                snapshot.Branch, snapshot.Upstream);
        }

        private async void FetchSelected_Click(object sender, RoutedEventArgs e)
        {
            await RunOnSelectedAsync("fetch 중…", GitSyncCommands.Fetch(new GitFetchOptions()), GitCommandRunner.NetworkTimeout);
        }

        private async void FetchSelectedOptions_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedRow == null)
                return;
            var dialog = new GitFetchDialog("이 저장소에서 fetch한 뒤 다시 비교합니다: " + SelectedRow.Info.DisplayPath) { Owner = this };
            if (dialog.ShowDialog() == true)
                await RunOnSelectedAsync("fetch 중…", GitSyncCommands.Fetch(dialog.Options), GitCommandRunner.NetworkTimeout);
        }

        private async void OutgoingDiff_Click(object sender, RoutedEventArgs e)
        {
            await LoadDiffAsync(RemoteDiff, WithTitle(GitDiffCommands.Range(GitDiffCommands.Upstream + "...HEAD", _diff.IgnoreWhitespace),
                "보낼 변경 전체: upstream과 갈라진 뒤 로컬에서 커밋한 변경 (@{u}...HEAD)", "보낼 변경이 없습니다."));
        }

        private async void IncomingDiff_Click(object sender, RoutedEventArgs e)
        {
            await LoadDiffAsync(RemoteDiff, WithTitle(GitDiffCommands.Range("HEAD..." + GitDiffCommands.Upstream, _diff.IgnoreWhitespace),
                "받을 변경 전체: 갈라진 뒤 upstream에 커밋된 변경 (HEAD...@{u})", "받을 변경이 없습니다."));
        }

        private async void WorkTreeVsUpstream_Click(object sender, RoutedEventArgs e)
        {
            await LoadDiffAsync(RemoteDiff, WithTitle(GitDiffCommands.WorkTreeAgainst(GitDiffCommands.Upstream, _diff.IgnoreWhitespace),
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
                var watch = Stopwatch.StartNew();
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
                Timing(string.Format("작업 전체(명령+다시 읽기) {0} ms  {1}", watch.ElapsedMilliseconds, result.CommandLine));
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
            await StageAsync(SelectedChangeEntries(UnstagedList, UnstagedTree));
        }

        private async Task StageAsync(List<GitStatusEntry> entries)
        {
            if (entries.Count == 0)
                return;
            await RunOnSelectedAsync("스테이지 중…",
                new[] { "add", "--pathspec-from-file=-", "--pathspec-file-nul" },
                GitCommandRunner.QueryTimeout, ToPathspecInput(entries, false));
        }

        private async void Restore_Click(object sender, RoutedEventArgs e)
        {
            var entries = SelectedChangeEntries(UnstagedList, UnstagedTree);
            var snapshot = SelectedRow?.Snapshot;
            if (entries.Count == 0 || snapshot == null)
                return;

            var dialog = new GitRestoreDialog(entries, snapshot.HeadOid != null) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;
            var targets = dialog.Targets;
            if (targets.Count == 0 || !ConfirmNoUnsavedDocuments("되돌리기"))
                return;

            await RunOnSelectedAsync("되돌리는 중…", GitRestoreCommands.Arguments(dialog.Mode),
                GitCommandRunner.QueryTimeout, GitRestoreCommands.PathspecInput(targets));
        }

        /// <summary>변경됨 목록·트리에서 Delete: 고른 파일(트리 폴더면 그 아래 전체)을 확인 후 휴지통으로 보낸다.</summary>
        private async void UnstagedChanges_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Delete || Keyboard.Modifiers != ModifierKeys.None)
                return;
            e.Handled = true;

            var row = SelectedRow;
            if (row == null || _busyCount > 0)
                return;
            var entries = SelectedChangeEntries(UnstagedList, UnstagedTree);
            var files = GitRestoreCommands.DeletableFiles(row.RootPath, entries);
            if (files.Count == 0)
                return;

            var root = Path.GetFullPath(row.RootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var names = files.Take(10).Select(file => "  " + file.Substring(root.Length)).ToList();
            if (files.Count > names.Count)
                names.Add(string.Format("  … 외 {0}개", files.Count - names.Count));
            var tracked = GitRestoreCommands.DeletableFiles(row.RootPath, entries.Where(entry => !entry.IsUntracked)).Count;
            var message = string.Format("파일 {0}개를 휴지통으로 보낼까요?\n\n{1}", files.Count, string.Join("\n", names));
            if (tracked > 0)
                message += string.Format("\n\n⚠ 이 중 {0}개는 git이 추적하는 파일입니다. 지우면 \"삭제됨\" 변경이 되고, 커밋하지 않은 수정은 휴지통에서만 되살릴 수 있습니다.", tracked);
            if (MessageBox.Show(this, message, "파일 삭제", MessageBoxButton.YesNo,
                    tracked > 0 ? MessageBoxImage.Warning : MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;
            if (!ConfirmNoUnsavedDocuments("삭제"))
                return;

            await RunBusyAsync("삭제 중…", async _ =>
            {
                var errors = new List<string>();
                foreach (var file in files)
                {
                    try
                    {
                        FileOperationService.MoveToRecycleBin(file);
                    }
                    catch (OperationCanceledException)
                    {
                        break; // 셸 진행 창에서 취소
                    }
                    catch (Exception ex)
                    {
                        errors.Add(file.Substring(root.Length) + ": " + ex.Message);
                    }
                }
                foreach (var error in errors)
                    AppendOutput("삭제 실패 — " + error);
                await RefreshStatusAsync(row, _lifetime.Token);
            });
        }

        private async void StageAll_Click(object sender, RoutedEventArgs e)
        {
            await RunOnSelectedAsync("전체 스테이지 중…", new[] { "add", "-A" }, GitCommandRunner.QueryTimeout);
        }

        private async void Unstage_Click(object sender, RoutedEventArgs e)
        {
            await UnstageAsync(SelectedChangeEntries(StagedList, StagedTree));
        }

        private async Task UnstageAsync(List<GitStatusEntry> entries)
        {
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
            await CommitAsync(new GitCommitOptions());
        }

        private async void CommitOptions_Click(object sender, RoutedEventArgs e)
        {
            var row = SelectedRow;
            if (row?.Snapshot == null || _busyCount > 0)
                return;

            // 직전 커밋 제목과 push 여부(upstream에 이미 있는지 = 보낼 커밋이 0개)
            string lastSubject = null;
            if (row.Snapshot.HeadOid != null)
            {
                // async void라 예외가 나가면 앱이 종료된다(git 경로 오류, 창 닫힘 취소).
                try
                {
                    var last = await GitCommandRunner.RunAsync(row.RootPath, new[] { "log", "-1", "--format=%s" }, GitCommandRunner.QueryTimeout, _lifetime.Token, readOnly: true);
                    lastSubject = last.Success ? last.StdOut.Trim() : null;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "git을 실행할 수 없습니다.\n" + ex.Message, "Git", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            var headPushed = row.Snapshot.Upstream != null && row.Snapshot.Ahead == 0;
            var dialog = new GitCommitOptionsDialog(lastSubject, headPushed, string.IsNullOrWhiteSpace(CommitMessageBox.Text)) { Owner = this };
            if (dialog.ShowDialog() == true)
                await CommitAsync(dialog.Options);
        }

        private async Task CommitAsync(GitCommitOptions options)
        {
            var snapshot = SelectedRow?.Snapshot;
            if (snapshot == null || _busyCount > 0)
                return;

            var message = CommitMessageBox.Text;
            // amend는 메시지가 비면 직전 메시지를 그대로 쓴다.
            if (string.IsNullOrWhiteSpace(message) && !options.Amend)
            {
                MessageBox.Show(this, "커밋 메시지를 입력하세요.", "커밋", MessageBoxButton.OK, MessageBoxImage.Information);
                CommitMessageBox.Focus();
                return;
            }
            // amend(메시지만 고치기)와 빈 커밋은 스테이지된 변경이 없어도 된다.
            if (snapshot.StagedCount == 0 && !options.Amend && !options.AllowEmpty)
            {
                MessageBox.Show(this, "스테이지된 변경이 없습니다.", "커밋", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (snapshot.ConflictCount > 0)
            {
                MessageBox.Show(this, "충돌이 해결되지 않은 파일이 있어 커밋할 수 없습니다.", "커밋", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 메시지는 UTF-8 임시 파일로 넘긴다(여러 줄·따옴표·한글 안전, 편집기 안 뜸). amend + 빈 메시지면 파일 없이 --no-edit.
            var keepMessage = options.Amend && string.IsNullOrWhiteSpace(message);
            var messageFile = keepMessage ? null : Path.Combine(Path.GetTempPath(), "folderss-commit-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                if (messageFile != null)
                    File.WriteAllText(messageFile, message.Replace("\r\n", "\n"), new UTF8Encoding(false));
                await RunOnSelectedAsync(options.Amend ? "직전 커밋 고치는 중…" : "커밋 중…", GitSyncCommands.Commit(messageFile, options), GitCommandRunner.NetworkTimeout,
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
                if (messageFile != null)
                {
                    try { File.Delete(messageFile); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
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
            return ConfirmNoUnsavedDocumentsUnder(row == null ? null : row.RootPath, "이 저장소의", action);
        }

        /// <summary>목록의 모든 저장소를 덮는 폴더(상위 저장소가 있으면 그 루트, 아니면 기준 폴더) 아래에서 센다 — 중첩 저장소를 두 번 세지 않는다.</summary>
        private bool ConfirmNoUnsavedDocumentsUnderAll(string action)
        {
            var ancestor = _rows.FirstOrDefault(r => r.Info.IsAncestor);
            return ConfirmNoUnsavedDocumentsUnder(ancestor != null ? ancestor.RootPath : _basePath, "목록 저장소들의", action);
        }

        private bool ConfirmNoUnsavedDocumentsUnder(string folder, string whose, string action)
        {
            var count = folder == null ? 0 : _countModifiedDocumentsUnder(folder);
            if (count == 0)
                return true;

            return MessageBox.Show(this,
                string.Format("{0} 파일을 저장하지 않은 문서 탭이 {1}개 있습니다.\n{2}하면 디스크의 파일이 바뀌어 편집 내용과 어긋날 수 있습니다. 계속할까요?", whose, count, action),
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

        // ── 브랜치 그래프 ───────────────────────────────────────────────────

        /// <summary>목록에는 로컬 브랜치만, "원격 포함"을 켜면 원격 브랜치도. 대화상자(업데이트·시작점)는 계속 <see cref="_branches"/> 전체를 쓴다.</summary>
        private void ShowBranchList()
        {
            var showRemote = BranchRemoteToggle.IsChecked == true;
            BranchList.ItemsSource = _branches.Where(b => showRemote || !b.IsRemote).ToList();
            ShowBranchCompareBases();
        }

        private void BranchRemoteToggle_Click(object sender, RoutedEventArgs e)
        {
            SaveWindowState();
            if (BranchList.ItemsSource == null)
                return;
            ShowBranchList();
            _branchGraphRow = null;
            EnsureBranchGraph();
        }

        /// <summary>브랜치 탭을 열었을 때 아직 이 저장소의 그래프를 읽지 않았으면 읽는다. 브랜치 목록을 읽기 전이면 목록을 읽은 뒤 읽는다.</summary>
        private async void EnsureBranchGraph()
        {
            var row = SelectedRow;
            if (row == null || ReferenceEquals(row, _branchGraphRow) || BranchList.ItemsSource == null)
                return;
            try
            {
                await LoadBranchGraphAsync(row, _lifetime.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                // async void라 여기서 막지 않으면 앱이 종료된다.
                AppendOutput("오류: " + ex.Message);
            }
        }

        /// <summary>
        /// 보이는 브랜치의 끝·HEAD·갈라진 지점만 남긴 요약 그래프(<see cref="GitBranchGraph"/>)를 그리고, 목록의 ↑↓(HEAD 대비)를 채운다.
        /// 로그와 달리 개수 제한이 없다 — 오래된 브랜치 끝이 빠지면 안 된다.
        /// </summary>
        private async Task LoadBranchGraphAsync(RepositoryRow row, CancellationToken token)
        {
            _branchGraphRow = row;
            var showRemote = BranchRemoteToggle.IsChecked == true;
            var shown = _branches.Where(b => showRemote || !b.IsRemote).ToList();
            var tips = shown.Select(b => b.TipHash).Where(h => !string.IsNullOrEmpty(h)).Distinct(StringComparer.Ordinal).ToList();
            var headOid = row.Snapshot?.HeadOid;
            if (tips.Count == 0 && headOid == null)
            {
                BranchGraphList.ItemsSource = null;
                return;
            }

            GitBranchGraph.Result graph;
            var lib = await ReadLibAsync(row, "브랜치 그래프", () => GitLibReader.ReadBranchGraph(row.RootPath, tips));
            if (lib.Ok)
            {
                graph = lib.Value;
            }
            else
            {
                var result = await GitCommandRunner.RunAsync(row.RootPath, GitBranchGraph.CliArguments(showRemote, headOid != null),
                    GitCommandRunner.QueryTimeout, token, readOnly: true);
                if (!result.Success)
                {
                    if (ReferenceEquals(row, SelectedRow))
                        AppendResult(row.Info, result);
                    return;
                }
                graph = GitBranchGraph.Build(GitOutputParser.ParseLog(result.StdOut), tips, headOid);
            }

            // 기다리는 사이 저장소·탭 상태·원격 표시가 바뀌었으면 버린다(바뀐 쪽이 다시 읽는다).
            if (!ReferenceEquals(row, SelectedRow) || !ReferenceEquals(row, _branchGraphRow) || showRemote != (BranchRemoteToggle.IsChecked == true))
                return;

            // 브랜치 열: 그 커밋을 가리키는 보이는 브랜치들. 브랜치가 아닌 행은 갈라진 지점이거나 detached HEAD다.
            var names = shown.Where(b => !string.IsNullOrEmpty(b.TipHash))
                .GroupBy(b => b.TipHash, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(b => b.IsCurrent ? "● " + b.Name : b.Name)), StringComparer.Ordinal);
            foreach (var commit in graph.Rows)
            {
                commit.Refs = names.TryGetValue(commit.Hash, out var label) ? label
                    : graph.ForkPoints.Contains(commit.Hash) ? "(갈라진 지점)"
                    : commit.Hash == headOid ? "● HEAD (detached)"
                    : string.Empty;
            }

            var layout = GitGraphLayout.Compute(graph.Rows);
            var maxLanes = 1;
            for (var i = 0; i < graph.Rows.Count; i++)
            {
                graph.Rows[i].Graph = layout[i];
                maxLanes = Math.Max(maxLanes, layout[i].LaneCount);
            }
            BranchGraphColumn.Width = GitGraphCell.WidthFor(maxLanes);
            BranchGraphList.ItemsSource = graph.Rows;

            foreach (var branch in _branches)
            {
                if (!branch.IsCurrent && branch.TipHash != null && graph.Divergence.TryGetValue(branch.TipHash, out var d))
                {
                    branch.Ahead = d.Ahead;
                    branch.Behind = d.Behind;
                }
                else
                {
                    branch.Ahead = branch.Behind = null;
                }
            }
            BranchList.Items.Refresh();
            SelectBranchInGraph();
        }

        private async void BranchList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectBranchInGraph();
            await LoadBranchFilesAsync();
        }

        /// <summary>목록에서 고른 브랜치의 끝 커밋을 그래프에서 선택해 보이게 한다.</summary>
        private void SelectBranchInGraph()
        {
            if (!(BranchList.SelectedItem is GitBranchInfo branch) || !(BranchGraphList.ItemsSource is List<GitCommitInfo> rows))
                return;
            var target = rows.FirstOrDefault(c => c.Hash == branch.TipHash);
            BranchGraphList.SelectedItem = target;
            if (target != null)
                BranchGraphList.ScrollIntoView(target);
        }

        // ── 브랜치 비교: 기준 브랜치 ↔ 목록에서 고른 브랜치의 파일 목록 → 고른 파일 하나의 diff ──

        private const string BranchFilesHint = "브랜치를 선택하면 기준 브랜치와 비교해 바뀐 파일을 보여 줍니다.";

        /// <summary>사용자가 기준으로 고른 현재가 아닌 브랜치 이름과 그 저장소. null이면 현재 브랜치를 따라간다. 상세를 다시 읽어도(ClearDetail) 유지.</summary>
        private string _branchBaseChoice;
        private RepositoryRow _branchBaseRow;

        /// <summary>
        /// true면 비교 방향을 뒤집는다(대상 → 기준). 목록 선택(버튼 대상)을 바꾸지 않으려고 콤보·목록은 그대로 두고 비교만 뒤집는다 —
        /// 기준이 목록에 없는 원격 브랜치·detached HEAD여도 된다.
        /// </summary>
        private bool _branchCompareReversed;

        /// <summary>코드로 기준 콤보를 채우는 중 — 사용자 선택으로 기록하지 않는다.</summary>
        private bool _fillingBranchBases;

        /// <summary>파일 목록이 보이고 있는 비교(기준 해시, 대상 해시, 갈라진 지점부터 여부, 제목용 이름). 파일을 고르면 이 기준으로 diff를 만든다.</summary>
        private (string From, string To, bool SinceMergeBase, string Label)? _branchFiles;

        /// <summary>
        /// 기준 후보: 모든 브랜치(원격 포함, 목록 필터와 무관) + detached HEAD. 같은 저장소에서 현재가 아닌 브랜치를 기준으로 골라 뒀으면 이름으로 유지하고,
        /// 아니면 현재 브랜치 — 브랜치를 전환하면 기준도 새 현재 브랜치를 따라간다.
        /// </summary>
        private void ShowBranchCompareBases()
        {
            var row = SelectedRow;
            var previous = ReferenceEquals(row, _branchBaseRow) ? _branchBaseChoice : null;
            var bases = new List<GitBranchInfo>();
            if (row?.Snapshot != null && row.Snapshot.IsDetached && row.Snapshot.HeadOid != null)
                bases.Add(new GitBranchInfo { Name = "HEAD (detached)", TipHash = row.Snapshot.HeadOid, IsCurrent = true });
            bases.AddRange(_branches);
            _fillingBranchBases = true;
            BranchCompareBaseCombo.ItemsSource = bases;
            BranchCompareBaseCombo.SelectedItem = bases.FirstOrDefault(b => b.Name == previous) ?? bases.FirstOrDefault(b => b.IsCurrent);
            _fillingBranchBases = false;
        }

        private async void BranchCompare_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || _fillingBranchBases)
                return;
            if (ReferenceEquals(sender, BranchCompareBaseCombo))
            {
                var selected = BranchCompareBaseCombo.SelectedItem as GitBranchInfo;
                _branchBaseChoice = selected != null && !selected.IsCurrent ? selected.Name : null;
                _branchBaseRow = SelectedRow;
            }
            await LoadBranchFilesAsync();
        }

        private async void BranchCompareSwap_Click(object sender, RoutedEventArgs e)
        {
            _branchCompareReversed = !_branchCompareReversed;
            BranchCompareSwapButton.Content = _branchCompareReversed ? "⇄ 반대" : "⇄";
            await LoadBranchFilesAsync();
        }

        private void ClearBranchFiles(string diffMessage)
        {
            _branchFiles = null;
            BranchFileList.ItemsSource = null;
            BranchFileTree.ItemsSource = null;
            BranchFilesHeader.Text = "파일";
            BranchFilesHeader.ToolTip = null;
            BranchDiff.Clear(diffMessage);
        }

        private async Task LoadBranchFilesAsync()
        {
            var row = SelectedRow;
            var target = BranchList.SelectedItem as GitBranchInfo;
            var baseBranch = BranchCompareBaseCombo.SelectedItem as GitBranchInfo;
            var sinceMergeBase = BranchCompareModeCombo.SelectedIndex != 1;
            var reversed = _branchCompareReversed;
            BranchCompareTargetText.Text = target?.Name ?? "(목록에서 브랜치 선택)";
            ClearBranchFiles(BranchFilesHint);
            if (row == null || target == null || baseBranch == null)
                return;
            if (string.IsNullOrEmpty(target.TipHash) || string.IsNullOrEmpty(baseBranch.TipHash))
            {
                BranchDiff.Clear("커밋을 가리키지 않는 브랜치입니다.");
                return;
            }
            if (target.TipHash == baseBranch.TipHash)
            {
                BranchDiff.Clear(string.Format("'{0}'과(와) '{1}'은(는) 같은 커밋을 가리킵니다.", baseBranch.Name, target.Name));
                return;
            }

            BranchFilesHeader.Text = "파일 (불러오는 중…)";
            try
            {
                var (fromBranch, toBranch) = reversed ? (target, baseBranch) : (baseBranch, target);
                var from = fromBranch.TipHash;
                var to = toBranch.TipHash;
                var lib = await ReadLibAsync(row, "브랜치 비교 파일 목록", () => GitLibReader.ReadRevisionFiles(row.RootPath, from, to, sinceMergeBase));
                var result = lib.Ok
                    ? null
                    : await GitCommandRunner.RunAsync(row.RootPath, GitDiffCommands.RevisionFiles(from, to, sinceMergeBase),
                        GitCommandRunner.QueryTimeout, _lifetime.Token, readOnly: true);
                // 기다리는 사이 저장소·대상·기준·방식이 바뀌었으면 버린다(바뀐 쪽이 다시 읽는다).
                if (!ReferenceEquals(row, SelectedRow) || !ReferenceEquals(target, BranchList.SelectedItem)
                    || !ReferenceEquals(baseBranch, BranchCompareBaseCombo.SelectedItem) || sinceMergeBase != (BranchCompareModeCombo.SelectedIndex != 1)
                    || reversed != _branchCompareReversed)
                    return;
                if (!lib.Ok && !result.Success)
                {
                    BranchFilesHeader.Text = "파일 (읽지 못함)";
                    AppendResult(row.Info, result);
                    return;
                }

                var entries = lib.Ok ? lib.Value : GitOutputParser.ParseNameStatus(result.StdOut);
                var label = fromBranch.Name + (sinceMergeBase ? " ... " : " → ") + toBranch.Name;
                _branchFiles = (from, to, sinceMergeBase, label);
                BranchFileList.ItemsSource = entries;
                BranchFileTree.ItemsSource = GitChangeTree.Build(entries);
                // 방향을 뒤집을 수 있어 어느 쪽의 변경인지 제목에 늘 보인다.
                BranchFilesHeader.Text = string.Format("파일 {0}  ({1})", entries.Count, label);
                BranchFilesHeader.ToolTip = GitStatusEntry.Summarize(entries);
                BranchDiff.Clear(entries.Count == 0 ? "바뀐 파일이 없습니다." : "파일을 선택하면 내용을 보여 줍니다.");
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void BranchFilesTreeToggle_Click(object sender, RoutedEventArgs e)
        {
            var tree = BranchFilesTreeToggle.IsChecked == true;
            BranchFileList.Visibility = tree ? Visibility.Collapsed : Visibility.Visible;
            BranchFileTree.Visibility = tree ? Visibility.Visible : Visibility.Collapsed;
            // 두 보기의 선택은 따로라, 전환하면 diff가 어느 선택의 것인지 헷갈리지 않게 비운다.
            BranchFileList.UnselectAll();
            if (_branchFiles != null)
                BranchDiff.Clear("파일을 선택하면 내용을 보여 줍니다.");
        }

        private async void BranchFileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BranchFileList.SelectedItem is GitStatusEntry entry)
                await ShowBranchFileDiffAsync(entry);
        }

        private async void BranchFileTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var node = BranchFileTree.SelectedItem as GitChangeNode;
            if (node == null)
                return;
            if (node.IsFolder)
            {
                BranchDiff.Clear(string.Format("폴더 {0} — 파일 {1}개", node.Path, node.Entries.Count()));
                return;
            }
            await ShowBranchFileDiffAsync(node.Entry);
        }

        private Task ShowBranchFileDiffAsync(GitStatusEntry entry)
        {
            if (_branchFiles == null)
                return Task.CompletedTask;
            var files = _branchFiles.Value;
            var title = string.Format("{0}  {1}: {2}", files.Label, entry.StatusLabel, entry.PathText);
            if (BranchFilesDiffToggle.IsChecked != true)
                return ShowRevisionContentAsync(BranchDiff, files.To, entry, title + "  — 비교 대상 쪽 코드");
            return LoadDiffAsync(BranchDiff, WithTitle(GitDiffCommands.RevisionFile(files.From, files.To, files.SinceMergeBase, entry.Path, entry.OriginalPath,
                _diff.IgnoreWhitespace), title));
        }

        private async void BranchFilesDiffToggle_Click(object sender, RoutedEventArgs e)
        {
            var entry = SelectedFileEntry(BranchFilesTreeToggle, BranchFileList, BranchFileTree);
            if (entry != null)
                await ShowBranchFileDiffAsync(entry);
        }

        private async void NewBranch_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedRow == null)
                return;

            // 브랜치 목록에서 고른 브랜치가 있으면 그것을 기본 시작점으로.
            var selected = BranchList.SelectedItem as GitBranchInfo;
            await CreateBranchWithDialogAsync(null, selected != null && !selected.IsCurrent ? selected.Name : null);
        }

        /// <summary>시작점 후보: 현재 HEAD, (있으면) 로그에서 고른 커밋, 로컬 브랜치, 원격 브랜치.</summary>
        private List<GitDialogBase.Choice> StartPointChoices(GitCommitInfo commit)
        {
            var head = SelectedRow?.Snapshot?.BranchDisplay;
            var choices = new List<GitDialogBase.Choice>
            {
                new GitDialogBase.Choice { Text = "현재 HEAD" + (string.IsNullOrEmpty(head) ? string.Empty : " (" + head + ")"), Value = null }
            };
            if (commit != null)
                choices.Add(new GitDialogBase.Choice { Text = "커밋 " + commit.ShortHash + "  " + commit.Subject, Value = commit.Hash });
            foreach (var branch in _branches.Where(b => !b.IsCurrent))
                choices.Add(new GitDialogBase.Choice { Text = (branch.IsRemote ? "원격 " : "브랜치 ") + branch.Name, Value = branch.Name });
            return choices;
        }

        /// <summary>git 규칙(check-ref-format)으로 브랜치 이름 검사. 쓸 수 있으면 null.</summary>
        private async Task<string> ValidateBranchNameAsync(string name)
        {
            var row = SelectedRow;
            if (row == null)
                return "저장소를 선택하세요.";
            var result = await GitCommandRunner.RunAsync(row.RootPath, GitRefCommands.CheckBranchName(name), GitCommandRunner.QueryTimeout, _lifetime.Token, readOnly: true);
            if (!result.Success)
                return "브랜치 이름으로 쓸 수 없습니다: " + name + "\n(공백, ~ ^ : ? * [ \\, '-'로 시작, '..' 등은 쓸 수 없습니다)";
            if (_branches.Any(b => !b.IsRemote && b.Name == name))
                return "같은 이름의 브랜치가 이미 있습니다: " + name;
            return null;
        }

        private async Task CreateBranchWithDialogAsync(GitCommitInfo commit, string preferredStart)
        {
            var choices = StartPointChoices(commit);
            var index = commit != null ? 1 : Math.Max(0, choices.FindIndex(c => c.Value != null && c.Value == preferredStart));
            var dialog = new GitBranchDialog(choices, index, ValidateBranchNameAsync) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;
            if (dialog.SwitchAfter && !ConfirmNoUnsavedDocuments("브랜치를 전환"))
                return;
            await RunOnSelectedAsync(dialog.SwitchAfter ? "브랜치 만들고 전환하는 중…" : "브랜치 만드는 중…",
                GitRefCommands.CreateBranch(dialog.BranchName, dialog.StartPoint, dialog.SwitchAfter), GitCommandRunner.QueryTimeout);
        }

        // ── 로그: reset · revert · 브랜치 · 체크아웃 ──────────────────────────

        private GitCommitInfo SelectedCommit => LogList.SelectedItem as GitCommitInfo;

        private void LogList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            // 커밋을 고르지 않았거나 작업 중이면 메뉴를 열지 않는다.
            if (SelectedCommit == null || _busyCount > 0)
                e.Handled = true;
        }

        private async void LogReset_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedCommit != null)
                await ResetToCommitAsync(SelectedCommit, 0);
        }

        /// <param name="droppedCommits">대상에 없고 현재 HEAD에만 있는 커밋 수(대화상자 경고용).</param>
        private async Task ResetToCommitAsync(GitCommitInfo commit, int droppedCommits)
        {
            var snapshot = SelectedRow?.Snapshot;
            if (snapshot == null)
                return;

            var uncommitted = snapshot.Entries.Count(entry => !entry.IsUntracked);
            var dialog = new GitResetDialog(commit, snapshot.IsDetached ? "detached HEAD" : snapshot.Branch, uncommitted, droppedCommits) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;
            if (dialog.Mode == GitResetMode.Hard && !ConfirmNoUnsavedDocuments("hard reset"))
                return;

            await RunOnSelectedAsync("reset 중…", GitRefCommands.Reset(dialog.Mode, commit.Hash), GitCommandRunner.QueryTimeout,
                onDone: result =>
                {
                    if (result.Success)
                        AppendOutput("되돌린 커밋은 git reflog로 찾을 수 있습니다. 이전 위치로 돌아가려면 reflog의 해시로 다시 reset하세요.");
                });
        }

        private async void LogRevert_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedCommit != null)
                await RevertCommitAsync(SelectedCommit.Hash);
        }

        /// <summary>커밋을 되돌리는 새 커밋(revert). 병합 여부는 로그 목록이 아닌 커밋을 다시 읽어 판단한다(목록 부모는 단순화됐을 수 있음).</summary>
        private async Task RevertCommitAsync(string hash)
        {
            var row = SelectedRow;
            var snapshot = row?.Snapshot;
            if (snapshot == null)
                return;
            var commit = await ReadCommitAsync(row, hash);
            if (commit == null || !ReferenceEquals(row, SelectedRow))
                return;

            var uncommitted = snapshot.Entries.Count(entry => !entry.IsUntracked);
            var dialog = new GitRevertDialog(commit, snapshot.IsDetached ? "detached HEAD" : snapshot.Branch, uncommitted) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;
            if (!ConfirmNoUnsavedDocuments("revert"))
                return;

            await RunOnSelectedAsync("revert 중…", GitRefCommands.Revert(commit.Hash, commit.Parents.Length > 1, dialog.NoCommit), GitCommandRunner.QueryTimeout,
                onDone: result =>
                {
                    if (!result.Success)
                        AppendOutput("revert가 충돌로 멈췄다면 콘솔/IDE에서 해결한 뒤 커밋하거나 git revert --abort로 취소하세요 (git status로 확인).");
                    else if (dialog.NoCommit)
                        AppendOutput("되돌린 변경이 스테이지되었습니다. 메시지를 쓰고 커밋하세요.");
                });
        }

        /// <summary>커밋 하나(부모·제목)를 읽는다. 실패하면 출력에 알리고 null.</summary>
        private async Task<GitCommitInfo> ReadCommitAsync(RepositoryRow row, string hash)
        {
            var result = await GitCommandRunner.RunAsync(row.RootPath, new[] { "log", "-1", "-z", GitOutputParser.LogFormat, hash, "--" },
                GitCommandRunner.QueryTimeout, _lifetime.Token, readOnly: true);
            if (!result.Success)
            {
                AppendResult(row.Info, result);
                return null;
            }
            return GitOutputParser.ParseLog(result.StdOut).FirstOrDefault();
        }

        // ── 브랜치 우클릭: reset · revert ─────────────────────────────────────

        private void BranchList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var branch = BranchList.SelectedItem as GitBranchInfo;
            if (branch == null || string.IsNullOrEmpty(branch.TipHash) || _busyCount > 0)
            {
                e.Handled = true;
                return;
            }
            // 현재 브랜치로 reset은 HEAD를 옮기지 않는다(변경 버리기는 변경 탭에서).
            BranchResetMenuItem.IsEnabled = !branch.IsCurrent;
            // revert는 현재 브랜치에 이미 들어 있는 커밋만(이 브랜치에만 있는 커밋 0개). 그 밖은 거꾸로 적용할 변경이 HEAD에 없어 충돌한다.
            // Ahead는 브랜치 그래프를 읽은 뒤에 채워지므로 그 전에는 꺼 둔다.
            BranchRevertMenuItem.IsEnabled = branch.IsCurrent || branch.Ahead == 0;
        }

        private async void BranchReset_Click(object sender, RoutedEventArgs e)
        {
            var row = SelectedRow;
            var branch = BranchList.SelectedItem as GitBranchInfo;
            if (row == null || branch == null || branch.IsCurrent || string.IsNullOrEmpty(branch.TipHash))
                return;
            var commit = await ReadCommitAsync(row, branch.TipHash);
            if (commit != null && ReferenceEquals(row, SelectedRow))
                await ResetToCommitAsync(commit, branch.Behind ?? 0);
        }

        private async void BranchRevert_Click(object sender, RoutedEventArgs e)
        {
            var branch = BranchList.SelectedItem as GitBranchInfo;
            if (branch != null && !string.IsNullOrEmpty(branch.TipHash) && (branch.IsCurrent || branch.Ahead == 0))
                await RevertCommitAsync(branch.TipHash);
        }

        private async void LogBranch_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedCommit != null)
                await CreateBranchWithDialogAsync(SelectedCommit, null);
        }

        private async void LogCheckout_Click(object sender, RoutedEventArgs e)
        {
            var commit = SelectedCommit;
            if (commit == null)
                return;
            var dialog = new GitCheckoutDialog(commit, ValidateBranchNameAsync) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;
            if (!ConfirmNoUnsavedDocuments("체크아웃"))
                return;

            var args = dialog.NewBranchName != null
                ? GitRefCommands.CreateBranch(dialog.NewBranchName, commit.Hash, switchAfter: true)
                : GitRefCommands.CheckoutDetached(commit.Hash);
            await RunOnSelectedAsync("체크아웃 중…", args, GitCommandRunner.QueryTimeout);
        }

        private void LogCopyHash_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedCommit == null)
                return;
            ClipboardService.TrySetDataObject(SelectedCommit.Hash);
        }

        // ── 워킹트리 ──────────────────────────────────────────────────────────

        private List<GitWorktreeInfo> _worktrees = new List<GitWorktreeInfo>();

        private async Task LoadWorktreesAsync(RepositoryRow row, CancellationToken token)
        {
            var lib = await ReadLibAsync(row, "워킹트리", () => GitLibReader.ReadWorktrees(row.RootPath));
            if (lib.Ok)
            {
                if (!ReferenceEquals(row, SelectedRow))
                    return;
                _worktrees = lib.Value;
                WorktreeList.ItemsSource = _worktrees;
                return;
            }

            var result = await GitCommandRunner.RunAsync(row.RootPath, GitRefCommands.WorktreeList, GitCommandRunner.QueryTimeout, token, readOnly: true);
            if (!ReferenceEquals(row, SelectedRow))
                return;
            if (!result.Success)
            {
                AppendResult(row.Info, result);
                return;
            }
            _worktrees = GitOutputParser.ParseWorktrees(result.StdOut);
            WorktreeList.ItemsSource = _worktrees;
        }

        private static string NormalizePath(string path)
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        /// <summary>저장소 목록에서 경로가 같은 행을 찾고, 없으면 만들어 넣는다(상태도 읽음).</summary>
        private RepositoryRow EnsureRepositoryRow(string path)
        {
            var normalized = NormalizePath(path);
            var row = _rows.FirstOrDefault(r => string.Equals(NormalizePath(r.RootPath), normalized, StringComparison.OrdinalIgnoreCase));
            if (row != null)
                return row;

            var relative = Path.GetRelativePath(_basePath, normalized);
            row = new RepositoryRow(new GitRepositoryInfo
            {
                RootPath = normalized,
                DisplayPath = relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? normalized : relative,
                IsGitFile = File.Exists(Path.Combine(normalized, ".git"))
            });
            _rows.Add(row);
            _ = RefreshStatusSafeAsync(row);
            return row;
        }

        private async Task RefreshStatusSafeAsync(RepositoryRow row)
        {
            try
            {
                await RefreshStatusAsync(row, _lifetime.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppendOutput("상태를 읽지 못했습니다: " + ex.Message);
            }
        }

        private async void WorktreeAdd_Click(object sender, RoutedEventArgs e)
        {
            var row = SelectedRow;
            if (row == null)
                return;

            // 다른 워킹트리에서 이미 체크아웃한 브랜치는 고를 수 없게 뺀다(git도 거부).
            var used = new HashSet<string>(_worktrees.Where(w => w.Branch != null).Select(w => w.Branch), StringComparer.Ordinal);
            var free = _branches.Where(b => !b.IsRemote && !used.Contains(b.Name))
                .Select(b => new GitDialogBase.Choice { Text = b.Name, Value = b.Name }).ToList();
            var dialog = new GitWorktreeDialog(row.RootPath, StartPointChoices(SelectedCommit), free, ValidateBranchNameAsync) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            var path = dialog.WorktreePath;
            await RunOnSelectedAsync("워킹트리 만드는 중…", GitRefCommands.WorktreeAdd(path, dialog.NewBranch, dialog.Commitish), GitCommandRunner.QueryTimeout,
                onDone: result =>
                {
                    if (result.Success)
                        EnsureRepositoryRow(path);
                });
        }

        private void WorktreeOpen_Click(object sender, RoutedEventArgs e)
        {
            var worktree = WorktreeList.SelectedItem as GitWorktreeInfo;
            if (worktree == null || worktree.IsBare || worktree.IsPrunable)
                return;
            RepoList.SelectedItem = EnsureRepositoryRow(worktree.Path);
        }

        private void WorktreeList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            WorktreeOpen_Click(sender, e);
        }

        private async void WorktreeRemove_Click(object sender, RoutedEventArgs e)
        {
            var worktree = RemovableWorktree();
            if (worktree == null)
                return;

            var unsaved = _countModifiedDocumentsUnder(worktree.Path);
            if (MessageBox.Show(this,
                    string.Format("워킹트리 폴더를 지웁니다:\n{0}\n\n커밋 안 한 변경이 있으면 git이 거부합니다(강제 제거는 ▾ 옵션). 브랜치는 지우지 않습니다.{1}\n계속할까요?",
                        worktree.Path, unsaved > 0 ? string.Format("\n\n⚠ 이 폴더의 파일을 저장하지 않은 문서 탭이 {0}개 있습니다.", unsaved) : string.Empty),
                    "워킹트리 제거", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;
            await RemoveWorktreeAsync(worktree.Path, force: false);
        }

        private async void WorktreeRemoveOptions_Click(object sender, RoutedEventArgs e)
        {
            var worktree = RemovableWorktree();
            if (worktree == null)
                return;
            var unsaved = _countModifiedDocumentsUnder(worktree.Path);
            var dialog = new GitForceConfirmDialog("워킹트리 제거 옵션",
                "워킹트리 폴더를 지웁니다(브랜치는 남김):\n" + worktree.Path + (unsaved > 0 ? string.Format("\n\n⚠ 이 폴더의 파일을 저장하지 않은 문서 탭이 {0}개 있습니다.", unsaved) : string.Empty),
                "커밋 안 한 변경이 있어도 강제 제거 (--force)",
                "그 워킹트리의 커밋 안 한 변경과 추적 안 되는 파일이 모두 삭제되며 복구할 수 없습니다.",
                "제거") { Owner = this };
            if (dialog.ShowDialog() == true)
                await RemoveWorktreeAsync(worktree.Path, dialog.Force);
        }

        /// <summary>제거할 수 있는 워킹트리인지 확인하고 안 되면 이유를 알린다.</summary>
        private GitWorktreeInfo RemovableWorktree()
        {
            var worktree = WorktreeList.SelectedItem as GitWorktreeInfo;
            if (worktree == null)
                return null;
            if (worktree.IsMain)
            {
                MessageBox.Show(this, "주 작업 트리는 제거할 수 없습니다.", "워킹트리 제거", MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }
            if (worktree.IsPrunable)
            {
                MessageBox.Show(this, "폴더가 이미 없습니다. 'prune'으로 기록을 정리하세요.", "워킹트리 제거", MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }
            return worktree;
        }

        private async Task RemoveWorktreeAsync(string path, bool force)
        {
            await RunOnSelectedAsync(force ? "워킹트리 강제 제거 중…" : "워킹트리 제거 중…", GitRefCommands.WorktreeRemove(path, force), GitCommandRunner.QueryTimeout,
                onDone: result =>
                {
                    if (!result.Success)
                        return;
                    var normalized = NormalizePath(path);
                    var stale = _rows.FirstOrDefault(r => string.Equals(NormalizePath(r.RootPath), normalized, StringComparison.OrdinalIgnoreCase));
                    if (stale != null && !ReferenceEquals(stale, SelectedRow))
                        _rows.Remove(stale);
                });
        }

        // ── stash ─────────────────────────────────────────────────────────────

        private GitStashInfo SelectedStash => StashList.SelectedItem as GitStashInfo;

        private async Task LoadStashesAsync(RepositoryRow row, CancellationToken token)
        {
            var lib = await ReadLibAsync(row, "stash", () => GitLibReader.ReadStashes(row.RootPath));
            if (lib.Ok)
            {
                if (!ReferenceEquals(row, SelectedRow))
                    return;
                StashList.ItemsSource = lib.Value;
                StashDiff.Clear("stash를 선택하면 변경을 보여 줍니다.");
                return;
            }

            var result = await GitCommandRunner.RunAsync(row.RootPath, GitStashCommands.List, GitCommandRunner.QueryTimeout, token, readOnly: true);
            if (!ReferenceEquals(row, SelectedRow))
                return;
            if (!result.Success)
            {
                AppendResult(row.Info, result);
                return;
            }
            StashList.ItemsSource = GitOutputParser.ParseStashes(result.StdOut);
            StashDiff.Clear("stash를 선택하면 변경을 보여 줍니다.");
        }

        private async void StashList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var stash = SelectedStash;
            StashApplyButton.IsEnabled = StashApplyOptionsButton.IsEnabled = StashDropButton.IsEnabled = stash != null;
            if (stash == null)
                return;

            // stash 커밋의 첫 부모(만든 시점의 HEAD) 기준 diff = 추적 파일 변경. 새 파일(-u)은 세 번째 부모에 따로 있다.
            var request = GitDiffCommands.Commit(stash.Hash, stash.Parents.Take(1).ToArray(), _diff.IgnoreWhitespace);
            request.Title = stash.Ref + "  " + stash.Subject;
            await LoadDiffAsync(StashDiff, request);

            if (stash.HasUntracked && ReferenceEquals(stash, SelectedStash))
            {
                try
                {
                    var files = await GitCommandRunner.RunAsync(SelectedRow.RootPath, GitStashCommands.UntrackedFiles(stash.Hash),
                        GitCommandRunner.QueryTimeout, _lifetime.Token, readOnly: true);
                    if (files.Success && ReferenceEquals(stash, SelectedStash))
                    {
                        var names = GitOutputParser.ParseNulList(files.StdOut);
                        request.Title = string.Format("{0}  {1}   (+ 새 파일 {2}개: {3}{4})", stash.Ref, stash.Subject, names.Count,
                            string.Join(", ", names.Take(5)), names.Count > 5 ? " …" : string.Empty);
                        await LoadDiffAsync(StashDiff, request);
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    AppendOutput("stash의 새 파일 목록을 읽지 못했습니다: " + ex.Message);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        /// <summary>변경 사항 목록에서 고른 파일(평면·트리 모두, 변경됨·스테이지됨 합쳐 중복 제거).</summary>
        private List<string> SelectedChangePaths()
        {
            return SelectedChangeEntries(UnstagedList, UnstagedTree)
                .Concat(SelectedChangeEntries(StagedList, StagedTree))
                .SelectMany(entry => string.IsNullOrEmpty(entry.OriginalPath) ? new[] { entry.Path } : new[] { entry.Path, entry.OriginalPath })
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private async void StashPush_Click(object sender, RoutedEventArgs e)
        {
            await StashPushAsync(new GitStashPushOptions(), null);
        }

        private async void StashPushOptions_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedRow?.Snapshot == null)
                return;
            var paths = SelectedChangePaths();
            var dialog = new GitStashPushDialog(paths.Count) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            // 고른 파일에 새 파일(추적 안 됨)이 있는데 -u를 끄면 git이 "알 수 없는 경로"로 거부하므로 미리 알린다.
            var selectedUntracked = SelectedChangeEntries(UnstagedList, UnstagedTree).Any(entry => entry.IsUntracked);
            if (dialog.SelectedOnly && selectedUntracked && !dialog.Options.IncludeUntracked)
            {
                MessageBox.Show(this, "고른 파일에 새 파일(추적 안 됨)이 있습니다. '추적 안 되는 새 파일도 함께 (-u)'를 켜고 다시 하세요.",
                    "stash", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            await StashPushAsync(dialog.Options, dialog.SelectedOnly ? paths : null);
        }

        private async Task StashPushAsync(GitStashPushOptions options, List<string> paths)
        {
            var snapshot = SelectedRow?.Snapshot;
            if (snapshot == null)
                return;
            if (snapshot.Entries.Count == 0)
            {
                MessageBox.Show(this, "stash에 넣을 변경이 없습니다.", "stash", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!ConfirmNoUnsavedDocuments("stash 저장(작업 트리 되돌림)"))
                return;

            var stdin = paths == null ? null : string.Join("\0", paths) + "\0";
            await RunOnSelectedAsync("stash 저장 중…", GitStashCommands.Push(options, usePathspecStdin: paths != null), GitCommandRunner.QueryTimeout, stdin);
        }

        private async void StashApply_Click(object sender, RoutedEventArgs e)
        {
            var stash = SelectedStash;
            if (stash == null || !ConfirmNoUnsavedDocuments("stash 적용"))
                return;
            await RunStashApplyAsync(stash, GitStashApplyMode.Apply, false, null);
        }

        private async void StashApplyOptions_Click(object sender, RoutedEventArgs e)
        {
            var stash = SelectedStash;
            if (stash == null)
                return;
            var dialog = new GitStashApplyDialog(stash, ValidateBranchNameAsync) { Owner = this };
            if (dialog.ShowDialog() != true || !ConfirmNoUnsavedDocuments("stash 적용"))
                return;
            await RunStashApplyAsync(stash, dialog.Mode, dialog.RestoreIndex, dialog.BranchName);
        }

        private Task RunStashApplyAsync(GitStashInfo stash, GitStashApplyMode mode, bool restoreIndex, string branchName)
        {
            var status = mode == GitStashApplyMode.Pop ? "stash 꺼내는 중…" : mode == GitStashApplyMode.Branch ? "stash를 새 브랜치로 꺼내는 중…" : "stash 적용 중…";
            return RunOnSelectedAsync(status, GitStashCommands.Apply(stash.Ref, mode, restoreIndex, branchName), GitCommandRunner.QueryTimeout,
                onDone: result =>
                {
                    if (!result.Success && mode != GitStashApplyMode.Branch)
                        AppendOutput("적용 중 충돌이 났다면 충돌 파일을 해결하세요. stash는 삭제되지 않고 남아 있습니다" +
                                     (mode == GitStashApplyMode.Pop ? "(pop이어도 충돌 시에는 남김)." : "."));
                });
        }

        private async void StashDrop_Click(object sender, RoutedEventArgs e)
        {
            var stash = SelectedStash;
            if (stash == null)
                return;
            if (MessageBox.Show(this, string.Format("{0}을(를) 삭제할까요?\n{1}\n\n삭제한 stash는 되살리기 어렵습니다.", stash.Ref, stash.Subject),
                    "stash 삭제", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;
            await RunOnSelectedAsync("stash 삭제 중…", GitStashCommands.Drop(stash.Ref), GitCommandRunner.QueryTimeout);
        }

        private async void StashDropOptions_Click(object sender, RoutedEventArgs e)
        {
            var count = (StashList.ItemsSource as List<GitStashInfo>)?.Count ?? 0;
            if (SelectedRow == null || count == 0)
                return;
            var stash = SelectedStash;
            var dialog = new GitForceConfirmDialog("stash 삭제 옵션",
                stash != null ? "선택한 stash를 삭제합니다: " + stash.Ref + "  " + stash.Subject : "stash를 고르지 않았습니다. 아래를 켜면 모두 삭제합니다.",
                string.Format("모든 stash {0}개 삭제 (git stash clear)", count),
                "모든 stash가 지워지며 되살리기 어렵습니다.",
                "삭제") { Owner = this };
            if (dialog.ShowDialog() != true)
                return;
            if (dialog.Force)
                await RunOnSelectedAsync("모든 stash 삭제 중…", GitStashCommands.Clear, GitCommandRunner.QueryTimeout);
            else if (stash != null)
                await RunOnSelectedAsync("stash 삭제 중…", GitStashCommands.Drop(stash.Ref), GitCommandRunner.QueryTimeout);
        }

        private async void WorktreePrune_Click(object sender, RoutedEventArgs e)
        {
            await RunOnSelectedAsync("워킹트리 기록 정리 중…", GitRefCommands.WorktreePrune, GitCommandRunner.QueryTimeout);
        }

        /// <summary>삭제할 수 있는 로컬 브랜치인지 확인하고 안 되면 이유를 알린다.</summary>
        private GitBranchInfo DeletableBranch()
        {
            var branch = BranchList.SelectedItem as GitBranchInfo;
            if (branch == null)
                return null;
            if (branch.IsRemote)
            {
                MessageBox.Show(this, "원격 브랜치는 삭제 옆 ▾에서 확인한 뒤 삭제할 수 있습니다.", "브랜치 삭제", MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }
            if (branch.IsCurrent)
            {
                MessageBox.Show(this, "현재 브랜치는 삭제할 수 없습니다.", "브랜치 삭제", MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }
            return branch;
        }

        private async void DeleteBranch_Click(object sender, RoutedEventArgs e)
        {
            var branch = DeletableBranch();
            if (branch == null)
                return;
            if (MessageBox.Show(this,
                    string.Format("로컬 브랜치 '{0}'을(를) 삭제할까요?\n병합되지 않은 브랜치는 git이 거부합니다(강제 삭제는 ▾ 옵션).", branch.Name),
                    "브랜치 삭제", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;

            await RunOnSelectedAsync("브랜치 삭제 중…", GitSyncCommands.DeleteBranch(branch.Name, force: false), GitCommandRunner.QueryTimeout);
        }

        private async void DeleteBranchOptions_Click(object sender, RoutedEventArgs e)
        {
            if (BranchList.SelectedItem is GitBranchInfo remoteBranch && remoteBranch.IsRemote)
            {
                await DeleteRemoteBranchAsync(remoteBranch);
                return;
            }
            var branch = DeletableBranch();
            if (branch == null)
                return;
            var dialog = new GitForceConfirmDialog("브랜치 삭제 옵션",
                string.Format("로컬 브랜치 '{0}'을(를) 삭제합니다. 기본은 병합된 브랜치만 지웁니다(git branch -d).", branch.Name),
                "병합 안 된 커밋이 있어도 강제 삭제 (git branch -D)",
                "병합 안 된 커밋은 어느 브랜치에서도 가리키지 않게 되어 git reflog로만 찾을 수 있습니다.",
                "삭제") { Owner = this };
            if (dialog.ShowDialog() != true)
                return;
            await RunOnSelectedAsync(dialog.Force ? "브랜치 강제 삭제 중…" : "브랜치 삭제 중…",
                GitSyncCommands.DeleteBranch(branch.Name, dialog.Force), GitCommandRunner.QueryTimeout);
        }

        /// <summary>원격 브랜치 삭제(push --delete). 경고와 확인 체크를 거친 뒤에만 실행한다.</summary>
        private async Task DeleteRemoteBranchAsync(GitBranchInfo branch)
        {
            var row = SelectedRow;
            if (row == null)
                return;
            var remotes = await GetRemotesAsync(row);
            if (remotes == null)
                return;
            var split = GitSyncCommands.SplitRemoteBranch(branch.Name, remotes);
            if (split == null)
            {
                MessageBox.Show(this, string.Format("'{0}'에 맞는 원격을 찾지 못했습니다(원격이 지워졌을 수 있음). fetch --prune으로 정리하세요.", branch.Name),
                    "원격 브랜치 삭제", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var (remote, name) = split.Value;
            var tracking = _branches.Where(b => !b.IsRemote && b.Upstream == branch.Name).Select(b => b.Name).ToList();
            var dialog = new GitRemoteBranchDeleteDialog(remote, name, tracking) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;
            await RunOnSelectedAsync("원격 브랜치 삭제 중…", GitSyncCommands.DeleteRemoteBranch(remote, name), GitCommandRunner.NetworkTimeout);
        }

        // ── 브랜치 업데이트 (base ← compare) ─────────────────────────────────

        /// <summary>기본: 현재 브랜치 ← 목록에서 고른 브랜치, fast-forward만.</summary>
        private async void UpdateBranch_Click(object sender, RoutedEventArgs e)
        {
            var snapshot = SelectedRow?.Snapshot;
            if (snapshot == null)
                return;
            var compare = BranchList.SelectedItem as GitBranchInfo;
            if (snapshot.IsDetached || string.IsNullOrEmpty(snapshot.Branch))
            {
                MessageBox.Show(this, "detached HEAD 상태입니다. 브랜치로 전환하거나 ▾에서 바꿀 브랜치를 고르세요.", "브랜치 업데이트", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (compare == null || compare.IsCurrent)
            {
                MessageBox.Show(this, string.Format("목록에서 '{0}'을(를) 맞출 기준 브랜치를 고르세요.", snapshot.Branch), "브랜치 업데이트", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            await UpdateBranchAsync(new GitBranchUpdateOptions
            {
                Base = snapshot.Branch,
                Compare = compare.Name,
                CompareIsRemote = compare.IsRemote,
                BaseIsCurrent = true
            });
        }

        private async void UpdateBranchOptions_Click(object sender, RoutedEventArgs e)
        {
            var snapshot = SelectedRow?.Snapshot;
            if (snapshot == null)
                return;
            var current = snapshot.IsDetached ? null : snapshot.Branch;
            var selected = BranchList.SelectedItem as GitBranchInfo;
            var compare = selected != null && !selected.IsCurrent ? selected.Name : snapshot.Upstream;
            var dialog = new GitBranchUpdateDialog(_branches, current, current, compare, snapshot.Entries.Count(entry => !entry.IsUntracked)) { Owner = this };
            if (dialog.ShowDialog() == true)
                await UpdateBranchAsync(dialog.Options);
        }

        private async Task UpdateBranchAsync(GitBranchUpdateOptions options)
        {
            // 체크아웃 안 된 브랜치만 바뀌면 작업 트리는 그대로라 열린 문서를 확인할 필요가 없다.
            if (options.BaseIsCurrent && !ConfirmNoUnsavedDocuments("브랜치 업데이트"))
                return;

            await RunOnSelectedAsync("브랜치 업데이트 중…", GitSyncCommands.UpdateBranch(options), GitCommandRunner.QueryTimeout, onDone: result =>
            {
                if (result.Success)
                {
                    if (options.Mode == GitBranchUpdateMode.Squash)
                        AppendOutput("squash한 변경이 스테이지되었습니다. 메시지를 쓰고 커밋하세요.");
                    else if (options.Mode == GitBranchUpdateMode.Overwrite)
                        AppendOutput("버린 커밋은 git reflog로 찾을 수 있습니다.");
                    return;
                }
                if (options.Mode == GitBranchUpdateMode.FastForwardOnly)
                    AppendOutput(string.Format("fast-forward할 수 없습니다('{0}'과(와) '{1}'이(가) 갈라졌거나, 다른 워킹트리에 체크아웃됨). 업데이트 옆 ▾에서 다른 방식을 고르세요.", options.Base, options.Compare));
                else if (options.Mode != GitBranchUpdateMode.Overwrite)
                    AppendOutput("병합·rebase가 중간에 멈췄다면 충돌을 콘솔/IDE에서 해결하세요 (git status로 확인, 취소는 git merge --abort / git rebase --abort).");
            });
        }

        // ── pull / push ─────────────────────────────────────────────────────

        private async void Pull_Click(object sender, RoutedEventArgs e)
        {
            await PullAsync(new GitPullOptions { Mode = _settings.PullMode });
        }

        private async void PullOptions_Click(object sender, RoutedEventArgs e)
        {
            var snapshot = SelectedRow?.Snapshot;
            if (snapshot == null)
                return;
            var dialog = new GitPullDialog(string.Format("'{0}' ← {1}", snapshot.BranchDisplay, snapshot.Upstream ?? "upstream 없음"),
                new GitPullOptions { Mode = _settings.PullMode }) { Owner = this };
            if (dialog.ShowDialog() == true)
                await PullAsync(dialog.Options);
        }

        private async Task PullAsync(GitPullOptions options)
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

            await RunOnSelectedAsync("pull 중…", GitSyncCommands.Pull(options), GitCommandRunner.NetworkTimeout, onDone: result =>
            {
                if (result.Success)
                    return;
                if (options.Mode == GitPullMode.FastForwardOnly)
                    AppendOutput("fast-forward로 받을 수 없습니다(로컬과 원격이 갈라짐). pull 옆 ▾에서 병합·rebase를 고르거나 콘솔에서 병합하세요.");
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
            var names = await GetRemotesAsync(row);
            if (names == null)
                return;
            var remote = names.Contains("origin") ? "origin" : names[0];
            if (MessageBox.Show(this,
                    string.Format("'{0}' 브랜치에 upstream이 없습니다.\n{1}/{0}(으)로 push하고 upstream으로 설정할까요?", snapshot.Branch, remote),
                    "push", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            await RunOnSelectedAsync("push 중…",
                GitSyncCommands.Push(new GitPushOptions { Remote = remote, Branch = snapshot.Branch, SetUpstream = true }), GitCommandRunner.NetworkTimeout);
        }

        /// <summary>원격 이름 목록. 없거나 못 읽으면 알리고 null.</summary>
        private async Task<List<string>> GetRemotesAsync(RepositoryRow row)
        {
            GitResult remotes;
            try
            {
                remotes = await GitCommandRunner.RunAsync(row.RootPath, GitSyncCommands.Remotes, GitCommandRunner.QueryTimeout, _lifetime.Token, readOnly: true);
            }
            catch (Exception ex)
            {
                if (!(ex is OperationCanceledException))
                    AppendOutput("오류: " + ex.Message);
                return null;
            }
            var names = remotes.StdOut.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
            if (names.Count == 0)
            {
                MessageBox.Show(this, "이 저장소에 원격(remote)이 없습니다. 콘솔에서 git remote add로 먼저 등록하세요.", "push", MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }
            return names;
        }

        private async void PushOptions_Click(object sender, RoutedEventArgs e)
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
            var remotes = await GetRemotesAsync(row);
            if (remotes == null)
                return;
            var dialog = new GitPushDialog(snapshot.Branch, snapshot.Upstream, remotes) { Owner = this };
            if (dialog.ShowDialog() == true)
                await RunOnSelectedAsync("push 중…", GitSyncCommands.Push(dialog.Options), GitCommandRunner.NetworkTimeout);
        }

        // ── 외부 비교 도구 ─────────────────────────────────────────────────────

        private async void DiffView_ExternalToolRequested(object sender, EventArgs e)
        {
            var request = (sender as GitDiffView)?.CurrentRequest;
            var row = SelectedRow;
            if (request == null || row == null)
                return;

            try
            {
                var run = await DiffToolLauncher.RunAsync(this, request, _diff, row.RootPath, () => _openSettings?.Invoke("Diff"),
                    line => AppendOutput(line + "    [" + row.Info.DisplayPath + "]"), _lifetime.Token);
                if (run == null)
                    return;
                if (!run.Succeeded)
                    AppendResult(row.Info, run.Result);
                else if (run.ExitedQuickly)
                    AppendOutput(DiffToolLauncher.QuickExitHint);
            }
            catch (Exception ex)
            {
                AppendOutput("외부 비교 도구를 실행하지 못했습니다: " + ex.Message);
            }
        }

        // ── 설정 ────────────────────────────────────────────────────────────

        private void Options_Click(object sender, RoutedEventArgs e)
        {
            _openSettings?.Invoke("Git");
        }

        /// <summary>설정 창에서 저장한 값을 반영한다. 탐색·git 경로가 바뀌면 다시 찾고, 표시 옵션만 바뀌면 상세만 다시 읽는다.</summary>
        public async void ApplySettings(GitSettings settings, DiffSettings diffSettings)
        {
            if (settings == null || diffSettings == null || _lifetime.IsCancellationRequested)
                return;

            var previous = _settings;
            var previousDiff = _diff;
            _settings = settings.Clone();
            _diff = diffSettings.Clone();
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

                if (previousDiff.DiffViewMode != _diff.DiffViewMode)
                {
                    // 기본 보기가 바뀌면 세 diff 창 모두 새 기본값으로 맞춘다(창에서 따로 바꿔 둔 값은 덮어씀).
                    ChangesDiff.ViewMode = RemoteDiff.ViewMode = LogDiff.ViewMode = StashDiff.ViewMode = BranchDiff.ViewMode = _diff.DiffViewMode;
                }

                if (previous.LogLimit != _settings.LogLimit || previous.LogAllBranches != _settings.LogAllBranches
                    || previousDiff.IgnoreWhitespace != _diff.IgnoreWhitespace || previousDiff.FallbackEncoding != _diff.FallbackEncoding
                    || previousDiff.DiffViewMode != _diff.DiffViewMode)
                    await ReloadSelectedDetailAsync();
            }
            catch (Exception ex)
            {
                AppendOutput("설정 반영 중 오류: " + ex.Message);
            }
        }
    }
}

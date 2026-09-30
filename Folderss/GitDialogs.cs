using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Folderss.Models;
using Folderss.Services;

namespace Folderss
{
    /// <summary>Git 창의 선택 대화상자 공통 틀: 테마 색, 제목·설명·버튼 배치, 비동기 확인(이름 검사 등).</summary>
    public abstract class GitDialogBase : Window
    {
        protected readonly StackPanel Body = new StackPanel { Margin = new Thickness(16) };
        private Button _ok;

        protected GitDialogBase(string title, double width)
        {
            Title = title;
            Width = width;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            SetResourceReference(BackgroundProperty, "WindowBackground");
            SetResourceReference(ForegroundProperty, "PrimaryText");
            SetResourceReference(FontFamilyProperty, "AppFontFamily");
            FontSize = 13;
            Content = Body;
        }

        protected TextBlock AddText(string text, bool secondary = false, bool warning = false, double top = 0)
        {
            var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0) };
            if (warning)
                block.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0x6C, 0x75));
            else if (secondary)
                block.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            if (secondary)
                block.FontSize = 12;
            Body.Children.Add(block);
            return block;
        }

        protected void AddLabel(string text)
        {
            Body.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
        }

        protected RadioButton AddRadio(string group, string text, string hint, bool isChecked)
        {
            var radio = new RadioButton { GroupName = group, Content = text, IsChecked = isChecked, Margin = new Thickness(0, 8, 0, 0) };
            radio.SetResourceReference(ForegroundProperty, "PrimaryText");
            Body.Children.Add(radio);
            if (hint != null)
            {
                var block = AddText(hint, secondary: true);
                block.Margin = new Thickness(20, 2, 0, 0);
            }
            return radio;
        }

        protected CheckBox AddCheck(string text, bool isChecked)
        {
            var check = new CheckBox { Content = text, IsChecked = isChecked, Margin = new Thickness(0, 10, 0, 0) };
            check.SetResourceReference(ForegroundProperty, "PrimaryText");
            Body.Children.Add(check);
            return check;
        }

        /// <summary>확인·취소 버튼. 확인은 <see cref="ValidateAsync"/>가 null(오류 없음)을 돌려줄 때만 닫는다.</summary>
        protected Button AddButtons(string okText)
        {
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            _ok = new Button { Content = okText, IsDefault = true, MinWidth = 90 };
            _ok.Click += Ok_Click;
            var cancel = new Button { Content = "취소", IsCancel = true, MinWidth = 75 };
            buttons.Children.Add(_ok);
            buttons.Children.Add(cancel);
            Body.Children.Add(buttons);
            return _ok;
        }

        /// <summary>입력 검사. 오류 문구를 돌려주면 창을 닫지 않고 알린다.</summary>
        protected virtual Task<string> ValidateAsync() => Task.FromResult<string>(null);

        private async void Ok_Click(object sender, RoutedEventArgs e)
        {
            _ok.IsEnabled = false;
            try
            {
                var error = await ValidateAsync();
                if (error != null)
                {
                    MessageBox.Show(this, error, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                if (IsLoaded && DialogResult != true)
                    _ok.IsEnabled = true;
            }
        }

        /// <summary>콤보 항목: 표시 글자와 git에 넘길 값(리비전 이름·해시). 값이 null이면 "현재 HEAD".</summary>
        public sealed class Choice
        {
            public string Text { get; set; }
            public string Value { get; set; }
            public override string ToString() => Text;
        }
    }

    /// <summary>reset 모드 선택. hard는 경고와 확인 체크를 거쳐야 실행된다.</summary>
    public sealed class GitResetDialog : GitDialogBase
    {
        private readonly RadioButton _soft;
        private readonly RadioButton _mixed;
        private readonly RadioButton _hard;
        private readonly TextBlock _hardWarning;
        private readonly CheckBox _hardConfirm;
        private readonly Button _ok;
        private readonly int _uncommitted;

        public GitResetMode Mode => _soft.IsChecked == true ? GitResetMode.Soft : _hard.IsChecked == true ? GitResetMode.Hard : GitResetMode.Mixed;

        public GitResetDialog(GitCommitInfo target, string currentBranch, int uncommittedCount) : base("reset", 520)
        {
            _uncommitted = uncommittedCount;
            AddText(string.Format("{0}의 HEAD를 {1} \"{2}\"(으)로 옮깁니다.", currentBranch ?? "현재 HEAD", target.ShortHash, target.Subject));

            AddLabel("모드");
            _soft = AddRadio("reset", "soft — 커밋만 되돌림", "HEAD만 옮기고, 되돌린 커밋의 변경은 스테이지된 채로 남습니다.", false);
            _mixed = AddRadio("reset", "mixed — 커밋과 스테이지를 되돌림 (git 기본값)", "변경은 작업 트리에 그대로 남고 스테이지만 풀립니다.", true);
            _hard = AddRadio("reset", "hard — 작업 트리까지 되돌림", "커밋 안 한 변경과 되돌린 커밋의 변경이 모두 사라집니다.", false);

            _hardWarning = AddText(uncommittedCount > 0
                ? string.Format("⚠ 커밋 안 한 변경 {0}개가 삭제되며 되돌릴 수 없습니다.", uncommittedCount)
                : "⚠ 되돌린 커밋의 변경이 작업 트리에서 사라집니다.", warning: true, top: 10);
            _hardConfirm = AddCheck("위 내용을 이해했고 hard reset을 실행합니다", false);

            AddText("되돌린 커밋은 한동안 git reflog로 찾을 수 있습니다. 이미 push한 커밋 뒤로 옮기면 원격과 갈라져 강제 푸시가 필요하며, 이 앱은 강제 푸시를 하지 않습니다.",
                secondary: true, top: 12);
            _ok = AddButtons("reset");

            foreach (var radio in new[] { _soft, _mixed, _hard })
                radio.Checked += (s, e) => UpdateHard();
            _hardConfirm.Checked += (s, e) => UpdateHard();
            _hardConfirm.Unchecked += (s, e) => UpdateHard();
            UpdateHard();
        }

        private void UpdateHard()
        {
            var hard = _hard.IsChecked == true;
            _hardWarning.Visibility = _hardConfirm.Visibility = hard ? Visibility.Visible : Visibility.Collapsed;
            if (_ok != null)
                _ok.IsEnabled = !hard || _hardConfirm.IsChecked == true;
        }
    }

    /// <summary>새 브랜치: 이름, 시작점(현재 HEAD / 커밋 / 로컬·원격 브랜치), 만든 뒤 전환 여부.</summary>
    public sealed class GitBranchDialog : GitDialogBase
    {
        private readonly TextBox _name;
        private readonly ComboBox _start;
        private readonly CheckBox _switch;
        private readonly Func<string, Task<string>> _validateName;

        public string BranchName => _name.Text.Trim();
        public string StartPoint => (_start.SelectedItem as Choice)?.Value;
        public bool SwitchAfter => _switch.IsChecked == true;

        /// <param name="validateName">git 규칙으로 이름을 검사해 오류 문구(없으면 null)를 돌려준다.</param>
        public GitBranchDialog(IList<Choice> startPoints, int defaultIndex, Func<string, Task<string>> validateName) : base("새 브랜치", 480)
        {
            _validateName = validateName;
            AddLabel("브랜치 이름");
            _name = new TextBox();
            Body.Children.Add(_name);

            AddLabel("시작점");
            _start = new ComboBox { ItemsSource = startPoints, SelectedIndex = Math.Max(0, defaultIndex) };
            Body.Children.Add(_start);

            _switch = AddCheck("만든 뒤 이 브랜치로 전환", true);
            AddText("원격 브랜치에서 만들면 그 원격 브랜치를 upstream으로 추적합니다.", secondary: true, top: 8);
            AddButtons("만들기");
            Loaded += (s, e) => _name.Focus();
        }

        protected override Task<string> ValidateAsync()
        {
            if (BranchName.Length == 0)
                return Task.FromResult("브랜치 이름을 입력하세요.");
            return _validateName(BranchName);
        }
    }

    /// <summary>커밋 체크아웃: 이 커밋에서 새 브랜치를 만들어 전환(권장) 또는 브랜치 없이 이동(detached).</summary>
    public sealed class GitCheckoutDialog : GitDialogBase
    {
        private readonly RadioButton _newBranch;
        private readonly TextBox _name;
        private readonly Func<string, Task<string>> _validateName;

        /// <summary>새 브랜치 이름. detached로 이동하면 null.</summary>
        public string NewBranchName => _newBranch.IsChecked == true ? _name.Text.Trim() : null;

        public GitCheckoutDialog(GitCommitInfo commit, Func<string, Task<string>> validateName) : base("커밋 체크아웃", 500)
        {
            _validateName = validateName;
            AddText(string.Format("{0} \"{1}\"(으)로 작업 트리를 옮깁니다.", commit.ShortHash, commit.Subject));

            _newBranch = AddRadio("checkout", "이 커밋에서 새 브랜치를 만들어 전환 (권장)", null, true);
            _name = new TextBox { Margin = new Thickness(20, 4, 0, 0) };
            Body.Children.Add(_name);
            var detached = AddRadio("checkout", "브랜치 없이 이 커밋으로 이동 (detached HEAD)",
                "둘러보기용입니다. 이 상태에서 한 커밋은 다른 브랜치로 옮기면 찾기 어려워집니다(reflog로만 복구).", false);

            _newBranch.Checked += (s, e) => _name.IsEnabled = true;
            detached.Checked += (s, e) => _name.IsEnabled = false;
            AddButtons("체크아웃");
            Loaded += (s, e) => _name.Focus();
        }

        protected override Task<string> ValidateAsync()
        {
            if (NewBranchName == null)
                return Task.FromResult<string>(null);
            if (NewBranchName.Length == 0)
                return Task.FromResult("새 브랜치 이름을 입력하세요.");
            return _validateName(NewBranchName);
        }
    }

    /// <summary>워킹트리 추가: 폴더, 새 브랜치(이름+시작점) 또는 기존 브랜치.</summary>
    public sealed class GitWorktreeDialog : GitDialogBase
    {
        private readonly TextBox _path;
        private readonly RadioButton _new;
        private readonly TextBox _name;
        private readonly ComboBox _start;
        private readonly ComboBox _existing;
        private readonly string _repoRoot;
        private readonly Func<string, Task<string>> _validateName;
        private bool _pathEdited;

        public string WorktreePath => Path.GetFullPath(_path.Text.Trim().Trim('"'));
        public string NewBranch => _new.IsChecked == true ? _name.Text.Trim() : null;
        public string Commitish => _new.IsChecked == true ? (_start.SelectedItem as Choice)?.Value : (_existing.SelectedItem as Choice)?.Value;

        /// <param name="freeBranches">다른 워킹트리에서 체크아웃하지 않은 로컬 브랜치(같은 브랜치는 두 곳에 못 둠).</param>
        public GitWorktreeDialog(string repoRoot, IList<Choice> startPoints, IList<Choice> freeBranches, Func<string, Task<string>> validateName)
            : base("새 워킹트리", 540)
        {
            _repoRoot = repoRoot;
            _validateName = validateName;

            _new = AddRadio("wt", "새 브랜치로", null, true);
            var newPanel = new StackPanel { Margin = new Thickness(20, 4, 0, 0) };
            _name = new TextBox();
            _start = new ComboBox { ItemsSource = startPoints, SelectedIndex = 0, Margin = new Thickness(0, 6, 0, 0) };
            newPanel.Children.Add(new TextBlock { Text = "브랜치 이름" });
            newPanel.Children.Add(_name);
            newPanel.Children.Add(new TextBlock { Text = "시작점", Margin = new Thickness(0, 6, 0, 0) });
            newPanel.Children.Add(_start);
            Body.Children.Add(newPanel);

            var existingRadio = AddRadio("wt", "기존 브랜치로", freeBranches.Count == 0 ? "다른 곳에서 체크아웃하지 않은 로컬 브랜치가 없습니다." : null, false);
            existingRadio.IsEnabled = freeBranches.Count > 0;
            _existing = new ComboBox { ItemsSource = freeBranches, SelectedIndex = freeBranches.Count > 0 ? 0 : -1, Margin = new Thickness(20, 4, 0, 0), IsEnabled = false };
            Body.Children.Add(_existing);

            AddLabel("폴더 (비어 있거나 없는 폴더)");
            var pathRow = new DockPanel();
            var browse = new Button { Content = "찾아보기…", Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(browse, Dock.Right);
            _path = new TextBox();
            pathRow.Children.Add(browse);
            pathRow.Children.Add(_path);
            Body.Children.Add(pathRow);
            AddText("만들면 Git 창 저장소 목록에 추가됩니다. 워킹트리 폴더는 탐색기에서 지우지 말고 워킹트리 탭의 '제거'를 쓰세요.", secondary: true, top: 8);
            AddButtons("만들기");

            _new.Checked += (s, e) => { newPanel.IsEnabled = true; _existing.IsEnabled = false; SuggestPath(); };
            existingRadio.Checked += (s, e) => { newPanel.IsEnabled = false; _existing.IsEnabled = true; SuggestPath(); };
            _name.TextChanged += (s, e) => SuggestPath();
            _existing.SelectionChanged += (s, e) => SuggestPath();
            _path.TextChanged += (s, e) => { if (_path.IsKeyboardFocusWithin) _pathEdited = true; };
            browse.Click += (s, e) => Browse();
            SuggestPath();
            Loaded += (s, e) => _name.Focus();
        }

        /// <summary>직접 고치기 전까지는 저장소 옆에 "&lt;저장소&gt;-&lt;브랜치&gt;" 폴더를 제안한다.</summary>
        private void SuggestPath()
        {
            if (_pathEdited || _path == null)
                return;
            var branch = _new.IsChecked == true ? _name.Text.Trim() : (_existing.SelectedItem as Choice)?.Value ?? string.Empty;
            var safe = new string(branch.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == '/' ? '-' : c).ToArray());
            var parent = Path.GetDirectoryName(_repoRoot.TrimEnd(Path.DirectorySeparatorChar)) ?? _repoRoot;
            _path.Text = Path.Combine(parent, Path.GetFileName(_repoRoot.TrimEnd(Path.DirectorySeparatorChar)) + (safe.Length > 0 ? "-" + safe : "-worktree"));
        }

        private void Browse()
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "워킹트리를 만들 폴더(비어 있거나 새 폴더)" })
            {
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    _path.Text = dialog.SelectedPath;
                    _pathEdited = true;
                }
            }
        }

        protected override async Task<string> ValidateAsync()
        {
            var raw = _path.Text.Trim().Trim('"');
            if (raw.Length == 0)
                return "폴더를 지정하세요.";
            string full;
            try
            {
                full = Path.GetFullPath(raw);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return "폴더 경로가 올바르지 않습니다: " + ex.Message;
            }
            if (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any())
                return "비어 있지 않은 폴더입니다. 새 폴더나 빈 폴더를 지정하세요.";
            if (File.Exists(full))
                return "같은 이름의 파일이 있습니다.";

            if (_new.IsChecked == true)
            {
                if (NewBranch.Length == 0)
                    return "새 브랜치 이름을 입력하세요.";
                return await _validateName(NewBranch);
            }
            return Commitish == null ? "기존 브랜치를 고르세요." : null;
        }
    }

    /// <summary>pull 옵션(▾). 방식 기본값은 설정 > Git.</summary>
    public sealed class GitPullDialog : GitDialogBase
    {
        private readonly RadioButton[] _modes;
        private readonly CheckBox _autoStash;

        private static readonly (GitPullMode Mode, string Text, string Hint)[] Modes =
        {
            (GitPullMode.FastForwardOnly, "fast-forward만 (--ff-only)", "갈라졌으면 아무것도 바꾸지 않고 실패합니다. 가장 안전합니다."),
            (GitPullMode.Merge, "병합 (--no-rebase)", "갈라졌으면 병합 커밋을 만듭니다. 충돌 나면 병합 중 상태로 남습니다."),
            (GitPullMode.Rebase, "rebase (--rebase)", "내 커밋을 받아 온 커밋 위로 다시 쌓습니다. 충돌 나면 rebase 중 상태로 남습니다."),
            (GitPullMode.UseGitConfig, "git 설정 따름", "옵션 없이 git pull (pull.rebase / pull.ff 설정 적용).")
        };

        public GitPullOptions Options => new GitPullOptions
        {
            Mode = Modes[Array.FindIndex(_modes, r => r.IsChecked == true)].Mode,
            AutoStash = _autoStash.IsChecked == true
        };

        public GitPullDialog(string branchText, GitPullOptions defaults) : base("pull 옵션", 500)
        {
            AddText(branchText);
            AddLabel("방식");
            _modes = Modes.Select(m => AddRadio("pull", m.Text, m.Hint, m.Mode == defaults.Mode)).ToArray();
            _autoStash = AddCheck("커밋 안 한 변경을 잠시 보관했다가 되돌리기 (--autostash)", defaults.AutoStash);
            AddText("병합·rebase에서 작업 트리가 깨끗하지 않아 거부될 때 씁니다. 되돌릴 때 충돌이 나면 변경은 stash에 남습니다.", secondary: true, top: 2);
            AddButtons("pull");
        }
    }

    /// <summary>push 옵션(▾): 원격, upstream 설정, 태그. 강제 푸시는 없다.</summary>
    public sealed class GitPushDialog : GitDialogBase
    {
        private readonly ComboBox _remote;
        private readonly CheckBox _upstream;
        private readonly CheckBox _tags;
        private readonly string _branch;

        public GitPushOptions Options => new GitPushOptions
        {
            Remote = _remote.SelectedItem as string,
            Branch = _branch,
            SetUpstream = _upstream.IsChecked == true,
            FollowTags = _tags.IsChecked == true
        };

        public GitPushDialog(string branch, string upstream, IList<string> remotes) : base("push 옵션", 480)
        {
            _branch = branch;
            AddText(string.Format("브랜치 '{0}'을(를) 보냅니다. 현재 upstream: {1}", branch, upstream ?? "없음"));
            AddLabel("원격");
            var defaultRemote = upstream != null && upstream.Contains('/') ? upstream.Substring(0, upstream.IndexOf('/')) : remotes.Contains("origin") ? "origin" : remotes.FirstOrDefault();
            _remote = new ComboBox { ItemsSource = remotes, SelectedItem = defaultRemote };
            Body.Children.Add(_remote);
            _upstream = AddCheck("이 원격 브랜치를 upstream으로 설정 (-u)", upstream == null);
            _tags = AddCheck("보내는 커밋의 주석 태그도 함께 (--follow-tags)", false);
            AddText("강제 푸시는 원격의 다른 사람 커밋을 덮어쓸 수 있어 이 앱에서 제공하지 않습니다.", secondary: true, top: 10);
            AddButtons("push");
        }

        protected override Task<string> ValidateAsync()
        {
            return Task.FromResult(_remote.SelectedItem == null ? "원격을 고르세요." : null);
        }
    }

    /// <summary>fetch 옵션(▾): prune, 전체 원격, 태그.</summary>
    public sealed class GitFetchDialog : GitDialogBase
    {
        private readonly CheckBox _prune;
        private readonly CheckBox _all;
        private readonly CheckBox _tags;

        public GitFetchOptions Options => new GitFetchOptions
        {
            Prune = _prune.IsChecked == true,
            AllRemotes = _all.IsChecked == true,
            Tags = _tags.IsChecked == true
        };

        public GitFetchDialog(string target) : base("fetch 옵션", 460)
        {
            var defaults = new GitFetchOptions();
            AddText(target);
            _prune = AddCheck("원격에서 지워진 브랜치 정리 (--prune)", defaults.Prune);
            _all = AddCheck("모든 원격에서 받기 (--all)", defaults.AllRemotes);
            _tags = AddCheck("모든 태그 받기 (--tags)", defaults.Tags);
            AddText("fetch는 작업 트리를 바꾸지 않습니다.", secondary: true, top: 10);
            AddButtons("fetch");
        }
    }

    /// <summary>커밋 옵션(▾): amend, sign-off, 빈 커밋.</summary>
    public sealed class GitCommitOptionsDialog : GitDialogBase
    {
        private readonly CheckBox _amend;
        private readonly CheckBox _signOff;
        private readonly CheckBox _allowEmpty;

        public GitCommitOptions Options => new GitCommitOptions
        {
            Amend = _amend.IsChecked == true,
            SignOff = _signOff.IsChecked == true,
            AllowEmpty = _allowEmpty.IsChecked == true
        };

        /// <param name="headPushed">직전 커밋이 이미 upstream에 있는지(amend하면 원격과 갈라짐).</param>
        public GitCommitOptionsDialog(string lastSubject, bool headPushed, bool messageEmpty) : base("커밋 옵션", 500)
        {
            _amend = AddCheck("직전 커밋 고치기 (--amend)", false);
            var amendHint = AddText(string.Format("직전 커밋 \"{0}\"에 스테이지된 변경을 합칩니다.{1}", lastSubject ?? "(없음)",
                messageEmpty ? " 메시지 칸이 비어 있으면 직전 메시지를 그대로 씁니다." : " 메시지 칸의 내용으로 메시지를 바꿉니다."), secondary: true, top: 2);
            amendHint.Margin = new Thickness(20, 2, 0, 0);
            var pushedWarning = AddText("⚠ 직전 커밋은 이미 push되었습니다. 고치면 원격과 갈라져 강제 푸시가 필요해집니다(이 앱은 강제 푸시를 하지 않음).", warning: true, top: 4);
            pushedWarning.Visibility = Visibility.Collapsed;
            _amend.Checked += (s, e) => pushedWarning.Visibility = headPushed ? Visibility.Visible : Visibility.Collapsed;
            _amend.Unchecked += (s, e) => pushedWarning.Visibility = Visibility.Collapsed;
            _amend.IsEnabled = lastSubject != null;

            _signOff = AddCheck("Signed-off-by 줄 추가 (-s)", false);
            _allowEmpty = AddCheck("변경 없이도 커밋 (--allow-empty)", false);
            AddButtons("커밋");
        }
    }

    /// <summary>되돌릴 수 없는 강제 옵션의 확인: 경고 문구 + 확인 체크를 해야 실행 버튼이 켜진다.</summary>
    public sealed class GitForceConfirmDialog : GitDialogBase
    {
        private readonly CheckBox _force;
        private readonly CheckBox _confirm;

        /// <summary>강제 옵션을 켰는지. 끄고 확인하면 일반(안전한) 동작.</summary>
        public bool Force => _force.IsChecked == true;

        public GitForceConfirmDialog(string title, string description, string forceText, string warning, string okText) : base(title, 500)
        {
            AddText(description);
            _force = AddCheck(forceText, false);
            var warningText = AddText("⚠ " + warning, warning: true, top: 6);
            _confirm = AddCheck("되돌릴 수 없다는 것을 이해했습니다", false);
            var ok = AddButtons(okText);

            void Update()
            {
                var force = _force.IsChecked == true;
                warningText.Visibility = _confirm.Visibility = force ? Visibility.Visible : Visibility.Collapsed;
                ok.IsEnabled = !force || _confirm.IsChecked == true;
            }
            _force.Checked += (s, e) => Update();
            _force.Unchecked += (s, e) => Update();
            _confirm.Checked += (s, e) => Update();
            _confirm.Unchecked += (s, e) => Update();
            Update();
        }
    }

    /// <summary>stash 저장 옵션(▾): 메시지, 새 파일 포함, 스테이지 유지, 선택한 파일만.</summary>
    public sealed class GitStashPushDialog : GitDialogBase
    {
        private readonly TextBox _message;
        private readonly CheckBox _untracked;
        private readonly CheckBox _keepIndex;
        private readonly CheckBox _selectedOnly;

        public GitStashPushOptions Options => new GitStashPushOptions
        {
            Message = _message.Text,
            IncludeUntracked = _untracked.IsChecked == true,
            KeepIndex = _keepIndex.IsChecked == true
        };

        public bool SelectedOnly => _selectedOnly.IsChecked == true;

        /// <param name="selectedCount">변경 사항 목록에서 고른 파일 수(0이면 "선택한 파일만"을 끈다).</param>
        public GitStashPushDialog(int selectedCount) : base("stash 저장 옵션", 480)
        {
            AddText("변경을 stash에 넣고 작업 트리를 마지막 커밋 상태로 되돌립니다.");
            AddLabel("메시지 (비우면 git 기본: WIP on <브랜치>)");
            _message = new TextBox();
            Body.Children.Add(_message);
            _untracked = AddCheck("추적 안 되는 새 파일도 함께 (-u)", false);
            _keepIndex = AddCheck("스테이지된 변경은 작업 트리에 그대로 두기 (--keep-index)", false);
            _selectedOnly = AddCheck(selectedCount > 0 ? string.Format("변경 사항에서 고른 파일 {0}개만", selectedCount) : "변경 사항에서 고른 파일만 (고른 파일 없음)", false);
            _selectedOnly.IsEnabled = selectedCount > 0;
            AddText(".gitignore에 걸린 파일은 넣지 않습니다.", secondary: true, top: 8);
            AddButtons("저장");
            Loaded += (s, e) => _message.Focus();
        }
    }

    /// <summary>stash 적용 옵션(▾): 적용 / 적용 후 삭제 / 새 브랜치로, 스테이지 상태 복원.</summary>
    public sealed class GitStashApplyDialog : GitDialogBase
    {
        private readonly RadioButton _apply;
        private readonly RadioButton _pop;
        private readonly RadioButton _branch;
        private readonly TextBox _branchName;
        private readonly CheckBox _index;
        private readonly Func<string, Task<string>> _validateName;

        public GitStashApplyMode Mode => _pop.IsChecked == true ? GitStashApplyMode.Pop : _branch.IsChecked == true ? GitStashApplyMode.Branch : GitStashApplyMode.Apply;
        public bool RestoreIndex => _index.IsChecked == true;
        public string BranchName => _branchName.Text.Trim();

        public GitStashApplyDialog(GitStashInfo stash, Func<string, Task<string>> validateName) : base("stash 적용 옵션", 500)
        {
            _validateName = validateName;
            AddText(stash.Ref + "  " + stash.Subject);
            _apply = AddRadio("stash", "적용 (apply) — stash는 남겨 둠", null, true);
            _pop = AddRadio("stash", "적용 후 삭제 (pop)", "충돌이 나면 git이 stash를 지우지 않고 남깁니다.", false);
            _branch = AddRadio("stash", "새 브랜치로 꺼내기 (stash branch)",
                "stash를 만든 커밋에서 새 브랜치를 만들어 전환하고 적용합니다. 지금 브랜치가 많이 바뀌어 충돌할 때 안전합니다. 성공하면 stash는 삭제됩니다.", false);
            _branchName = new TextBox { Margin = new Thickness(20, 4, 0, 0), IsEnabled = false };
            Body.Children.Add(_branchName);
            _index = AddCheck("스테이지 상태까지 되살리기 (--index)", false);

            _branch.Checked += (s, e) => { _branchName.IsEnabled = true; _index.IsEnabled = false; };
            _apply.Checked += (s, e) => { _branchName.IsEnabled = false; _index.IsEnabled = true; };
            _pop.Checked += (s, e) => { _branchName.IsEnabled = false; _index.IsEnabled = true; };
            AddButtons("실행");
        }

        protected override Task<string> ValidateAsync()
        {
            if (Mode != GitStashApplyMode.Branch)
                return Task.FromResult<string>(null);
            if (BranchName.Length == 0)
                return Task.FromResult("새 브랜치 이름을 입력하세요.");
            return _validateName(BranchName);
        }
    }
}

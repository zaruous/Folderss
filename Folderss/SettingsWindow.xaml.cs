using Folderss.Models;
using Folderss.Plugins;
using Folderss.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace Folderss
{
    public class ViewerMappingItem : System.ComponentModel.INotifyPropertyChanged
    {
        private string _viewerKey;
        public string Extension { get; set; }
        public string DefaultViewerKey { get; set; }
        public string DefaultViewerDisplayName { get; set; }
        public bool IsBuiltInDefault { get; set; }
        public string ViewerKey
        {
            get { return _viewerKey; }
            set { _viewerKey = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ViewerKey))); }
        }
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }

    public class PluginListItem
    {
        public PluginManifest Manifest { get; set; }
        public string Name { get { return Manifest.DisplayName; } }
        public string Version { get { return Manifest.Version; } }
        public string Id { get { return Manifest.Id; } }
        public string Status { get; set; }
    }

    public class ViewerOption
    {
        public string Key { get; set; }
        public string DisplayName { get; set; }
    }

    public class ConsoleProfileOption
    {
        public string Key { get; set; }
        public string DisplayName { get; set; }
    }

    public partial class SettingsWindow : Window
    {
        private readonly KeyBindingService _service;
        private readonly ViewerConfigService _viewerConfig;
        private readonly ObservableCollection<KeyBindingEntry> _workingBindings;
        private readonly ObservableCollection<ViewerMappingItem> _workingMappings;
        private readonly ObservableCollection<OpenWithEntry> _workingOpenWith;
        private readonly ConsoleSettings _workingConsoleSettings;
        private readonly ObservableCollection<ConsoleCommandProfile> _workingConsoleProfiles;
        private readonly ObservableCollection<ConsoleProfileOption> _consoleProfileOptions;
        private string _editingOpenWithId;
        private string _editingConsoleProfileKey;
        private bool _initializingTheme;
        private readonly AppTheme _originalTheme;
        private readonly GitSettings _workingGit;
        public IReadOnlyList<ViewerOption> ViewerOptions { get; }

        /// <summary>저장을 누른 뒤의 Git 설정(파일 저장이 실패해도 이번 실행에는 적용할 값). 저장 전이면 null.</summary>
        public GitSettings SavedGitSettings { get; private set; }

        /// <summary>저장을 누른 뒤의 비교(diff) 설정. 저장 전이면 null.</summary>
        public DiffSettings SavedDiffSettings { get; private set; }

        private readonly DiffSettings _workingDiff;

        private const int GitTabIndex = 5;
        private const int DiffTabIndex = 6;

        private static readonly (GitBaseFolderMode Value, string Text)[] GitBaseFolderChoices =
        {
            (GitBaseFolderMode.SelectedFolderFirst, "선택한 폴더 우선 (없으면 현재 폴더)"),
            (GitBaseFolderMode.CurrentFolder, "항상 패널의 현재 폴더")
        };

        private static readonly (GitPullMode Value, string Text)[] GitPullChoices =
        {
            (GitPullMode.FastForwardOnly, "fast-forward만 (--ff-only, 권장)"),
            (GitPullMode.Merge, "병합 허용 (--no-rebase)"),
            (GitPullMode.Rebase, "rebase (--rebase)"),
            (GitPullMode.UseGitConfig, "git 설정 따름 (pull.rebase / pull.ff)")
        };

        private static readonly (GitFallbackEncoding Value, string Text)[] DiffEncodingChoices =
        {
            (GitFallbackEncoding.None, "사용 안 함 — BOM 없으면 UTF-8 (기본)"),
            (GitFallbackEncoding.Cp949, "CP949 (EUC-KR)"),
            (GitFallbackEncoding.SystemAnsi, "시스템 기본 코드 페이지 (한국어 Windows: CP949)")
        };

        private static readonly (GitDiffViewMode Value, string Text)[] DiffViewChoices =
        {
            (GitDiffViewMode.ChangesOnly, "변경점만 (git 기본 문맥, 보통 3줄)"),
            (GitDiffViewMode.Context10, "변경점 + 앞뒤 10줄"),
            (GitDiffViewMode.FullFile, "전체 파일 (변경 줄은 색으로 표시)")
        };

        private static readonly (GitDiffToolMode Value, string Text)[] DiffToolChoices =
        {
            (GitDiffToolMode.None, "사용 안 함 (내장 diff만)"),
            (GitDiffToolMode.GitConfig, "git 설정의 difftool 사용"),
            (GitDiffToolMode.Custom, "직접 지정 (실행 파일 + 인수)")
        };

        public SettingsWindow(KeyBindingService service) : this(service, new ViewerConfigService()) { }

        public SettingsWindow(KeyBindingService service, ViewerConfigService viewerConfig, GitSettings gitSettings = null,
            DiffSettings diffSettings = null)
        {
            _service = service;
            _viewerConfig = viewerConfig;
            ViewerOptions = ViewerConfigService.GetViewerKeys()
                .Select(key => new ViewerOption { Key = key, DisplayName = ToViewerDisplayName(key) })
                .ToList();
            _workingBindings = new ObservableCollection<KeyBindingEntry>(
                service.Bindings.Select(b => b.Clone()));

            _workingMappings = new ObservableCollection<ViewerMappingItem>(
                viewerConfig.GetMappingRows()
                    .Select(row => new ViewerMappingItem
                    {
                        Extension = row.Extension,
                        ViewerKey = row.ViewerKey,
                        DefaultViewerKey = row.DefaultViewerKey,
                        DefaultViewerDisplayName = string.IsNullOrEmpty(row.DefaultViewerKey)
                            ? "System Default"
                            : ToViewerDisplayName(row.DefaultViewerKey),
                        IsBuiltInDefault = row.IsBuiltInDefault
                    }));

            _workingOpenWith = new ObservableCollection<OpenWithEntry>(
                OpenWithService.GetAll().Select(e => e.Clone()));
            _workingConsoleSettings = ConsoleSettingsService.Load().Clone();
            _workingConsoleProfiles = new ObservableCollection<ConsoleCommandProfile>(
                _workingConsoleSettings.CustomProfiles.Select(profile => profile.Clone()));
            _consoleProfileOptions = new ObservableCollection<ConsoleProfileOption>();

            _originalTheme = ThemeManager.CurrentTheme;
            _workingGit = (gitSettings ?? GitSettingsService.Load()).Clone();
            _workingDiff = (diffSettings ?? DiffSettingsService.Load()).Clone();

            InitializeComponent();
            DataContext = this;

            ShortcutList.ItemsSource = _workingBindings;
            ViewerMappingList.ItemsSource = _workingMappings;
            OpenWithList.ItemsSource = _workingOpenWith;
            NewViewerCombo.SelectedValue = ViewerConfigService.SystemDefaultKey;
            ConsoleProfileList.ItemsSource = _workingConsoleProfiles;
            ConsoleDefaultProfileCombo.ItemsSource = _consoleProfileOptions;
            ConsoleFontSizeBox.Text = _workingConsoleSettings.FontSize.ToString();
            RefreshConsoleProfileOptions();
            ConsoleDefaultProfileCombo.SelectedValue = _workingConsoleSettings.PreferredProfileKey;
            ConsoleProfileShellKindCombo.SelectedIndex = 0;
            ClearConsoleProfileForm();
            ClearOpenWithForm();

            _initializingTheme = true;
            BlackThemeRadio.IsChecked      = ThemeManager.CurrentTheme == AppTheme.Black;
            LightThemeRadio.IsChecked      = ThemeManager.CurrentTheme == AppTheme.Light;
            NordThemeRadio.IsChecked       = ThemeManager.CurrentTheme == AppTheme.Nord;
            CatppuccinThemeRadio.IsChecked = ThemeManager.CurrentTheme == AppTheme.Catppuccin;
            SolarizedThemeRadio.IsChecked  = ThemeManager.CurrentTheme == AppTheme.Solarized;
            DraculaThemeRadio.IsChecked    = ThemeManager.CurrentTheme == AppTheme.Dracula;
            GitHubThemeRadio.IsChecked     = ThemeManager.CurrentTheme == AppTheme.GitHub;
            _initializingTheme = false;

            InitializeGitPanel();
            InitializeDiffPanel();
            InitializePluginPages();

            TabNav.SelectedIndex = 0;
        }

        /// <summary>지정한 탭(ListBoxItem Tag)을 연다. 예: Git 창의 설정 버튼 → "Git".</summary>
        public void SelectTab(string tag)
        {
            var item = TabNav.Items.OfType<ListBoxItem>().FirstOrDefault(i => (i.Tag as string) == tag);
            if (item != null)
                TabNav.SelectedItem = item;
        }

        // ── Git ─────────────────────────────────────────────────────────────

        private void InitializeGitPanel()
        {
            GitPathBox.Text = _workingGit.GitExecutablePath;
            var saved = GitCommandRunner.ConfiguredGitPath;
            GitCommandRunner.ConfiguredGitPath = string.Empty;
            var detected = GitCommandRunner.FindGit();
            GitCommandRunner.ConfiguredGitPath = saved;
            GitDetectedText.Text = detected != null ? "자동 탐색 결과: " + detected : "자동 탐색으로 git을 찾지 못했습니다. Git for Windows(2.26 이상)를 설치하거나 경로를 지정하세요.";

            FillCombo(GitBaseFolderCombo, GitBaseFolderChoices.Select(c => c.Text), Array.FindIndex(GitBaseFolderChoices, c => c.Value == _workingGit.BaseFolderMode));
            FillCombo(GitPullModeCombo, GitPullChoices.Select(c => c.Text), Array.FindIndex(GitPullChoices, c => c.Value == _workingGit.PullMode));
            GitScanDepthBox.Text = _workingGit.ScanDepth.ToString();
            GitExcludedBox.Text = string.Join(Environment.NewLine, _workingGit.ExcludedFolders);
            GitLogLimitBox.Text = _workingGit.LogLimit.ToString();
            GitLogAllCheck.IsChecked = _workingGit.LogAllBranches;
        }

        // ── 비교(diff) ──────────────────────────────────────────────────────

        private void InitializeDiffPanel()
        {
            FillCombo(DiffViewModeCombo, DiffViewChoices.Select(c => c.Text), Array.FindIndex(DiffViewChoices, c => c.Value == _workingDiff.DiffViewMode));
            DiffIgnoreWhitespaceCheck.IsChecked = _workingDiff.IgnoreWhitespace;
            FillCombo(DiffFallbackEncodingCombo, DiffEncodingChoices.Select(c => c.Text), Array.FindIndex(DiffEncodingChoices, c => c.Value == _workingDiff.FallbackEncoding));
            FillCombo(DiffToolPresetCombo, DiffSettingsService.DiffToolPresets.Select(p => p.Name), 0);
            DiffToolPathBox.Text = _workingDiff.DiffToolPath;
            DiffToolArgsBox.Text = _workingDiff.DiffToolArguments;
            FillCombo(DiffToolModeCombo, DiffToolChoices.Select(c => c.Text), Array.FindIndex(DiffToolChoices, c => c.Value == _workingDiff.DiffToolMode));
            UpdateDiffToolPanels();
        }

        private static void FillCombo(ComboBox combo, IEnumerable<string> items, int selectedIndex)
        {
            combo.ItemsSource = items.ToList();
            combo.SelectedIndex = Math.Max(0, selectedIndex);
        }

        private void DiffToolModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateDiffToolPanels();
        }

        private void UpdateDiffToolPanels()
        {
            if (DiffCustomToolPanel == null || DiffGitConfigToolHint == null)
                return;
            var mode = DiffToolChoices[Math.Max(0, DiffToolModeCombo.SelectedIndex)].Value;
            DiffCustomToolPanel.Visibility = mode == GitDiffToolMode.Custom ? Visibility.Visible : Visibility.Collapsed;
            DiffGitConfigToolHint.Visibility = mode == GitDiffToolMode.GitConfig ? Visibility.Visible : Visibility.Collapsed;
        }

        private void DiffToolPresetApply_Click(object sender, RoutedEventArgs e)
        {
            var index = DiffToolPresetCombo.SelectedIndex;
            if (index < 0)
                return;
            var preset = DiffSettingsService.DiffToolPresets[index];
            var path = preset.ResolvePath();
            DiffToolPathBox.Text = path;
            DiffToolArgsBox.Text = preset.Arguments;
            if (!File.Exists(path) || !string.IsNullOrEmpty(preset.Note))
            {
                MessageBox.Show(this,
                    (File.Exists(path) ? string.Empty : preset.Name + "을(를) 기본 설치 위치에서 찾지 못했습니다. 실행 파일 경로를 직접 지정하세요.\n")
                    + (preset.Note ?? string.Empty),
                    "외부 비교 도구", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void GitPathBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Title = "git 실행 파일 선택", Filter = "git.exe|git.exe|실행 파일 (*.exe)|*.exe", CheckFileExists = true };
            if (dlg.ShowDialog(this) == true)
                GitPathBox.Text = dlg.FileName;
        }

        private void DiffToolBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Title = "비교 도구 실행 파일 선택", Filter = "실행 파일 (*.exe)|*.exe|모든 파일 (*.*)|*.*", CheckFileExists = true };
            if (dlg.ShowDialog(this) == true)
                DiffToolPathBox.Text = dlg.FileName;
        }

        private bool GitSettingsError(string message, Control focus)
        {
            MessageBox.Show(this, message, "Git 설정 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            TabNav.SelectedIndex = GitTabIndex;
            focus.Focus();
            (focus as TextBox)?.SelectAll();
            return false;
        }

        /// <summary>Git 탭 입력을 검증해 <see cref="_workingGit"/>에 반영한다. 잘못된 값이 있으면 Git 탭을 열고 false.</summary>
        private bool TryCollectGitSettings()
        {
            var gitPath = GitPathBox.Text.Trim().Trim('"');
            if (gitPath.Length > 0 && !File.Exists(gitPath))
                return GitSettingsError("지정한 git 실행 파일이 없습니다. 비우면 자동으로 찾습니다.", GitPathBox);

            if (!int.TryParse(GitScanDepthBox.Text.Trim(), out var depth) || depth < GitSettingsService.MinScanDepth || depth > GitSettingsService.MaxScanDepth)
                return GitSettingsError(string.Format("탐색 깊이는 {0}~{1} 사이 숫자여야 합니다.", GitSettingsService.MinScanDepth, GitSettingsService.MaxScanDepth), GitScanDepthBox);

            if (!int.TryParse(GitLogLimitBox.Text.Trim(), out var logLimit) || logLimit < GitSettingsService.MinLogLimit || logLimit > GitSettingsService.MaxLogLimit)
                return GitSettingsError(string.Format("로그 최대 개수는 {0}~{1} 사이 숫자여야 합니다.", GitSettingsService.MinLogLimit, GitSettingsService.MaxLogLimit), GitLogLimitBox);

            var excluded = GitExcludedBox.Text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(name => name.Trim())
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (excluded.Any(name => name.IndexOfAny(new[] { '\\', '/' }) >= 0))
                return GitSettingsError("제외 폴더에는 경로가 아니라 폴더 이름만 적으세요 (예: node_modules).", GitExcludedBox);

            _workingGit.GitExecutablePath = gitPath;
            _workingGit.BaseFolderMode = GitBaseFolderChoices[Math.Max(0, GitBaseFolderCombo.SelectedIndex)].Value;
            _workingGit.PullMode = GitPullChoices[Math.Max(0, GitPullModeCombo.SelectedIndex)].Value;
            _workingGit.ScanDepth = depth;
            _workingGit.ExcludedFolders = excluded;
            _workingGit.LogLimit = logLimit;
            _workingGit.LogAllBranches = GitLogAllCheck.IsChecked == true;
            return true;
        }

        /// <summary>비교 탭 입력을 검증해 <see cref="_workingDiff"/>에 반영한다. 잘못된 값이 있으면 비교 탭을 열고 false.</summary>
        private bool TryCollectDiffSettings()
        {
            var toolMode = DiffToolChoices[Math.Max(0, DiffToolModeCombo.SelectedIndex)].Value;
            var toolPath = DiffToolPathBox.Text.Trim().Trim('"');
            if (toolMode == GitDiffToolMode.Custom && !File.Exists(toolPath))
            {
                MessageBox.Show(this, "외부 비교 도구 실행 파일이 없습니다. 경로를 지정하거나 사용 방식을 바꾸세요.", "비교 설정 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                TabNav.SelectedIndex = DiffTabIndex;
                DiffToolPathBox.Focus();
                DiffToolPathBox.SelectAll();
                return false;
            }

            _workingDiff.DiffViewMode = DiffViewChoices[Math.Max(0, DiffViewModeCombo.SelectedIndex)].Value;
            _workingDiff.IgnoreWhitespace = DiffIgnoreWhitespaceCheck.IsChecked == true;
            _workingDiff.FallbackEncoding = DiffEncodingChoices[Math.Max(0, DiffFallbackEncodingCombo.SelectedIndex)].Value;
            _workingDiff.DiffToolMode = toolMode;
            _workingDiff.DiffToolPath = toolPath;
            _workingDiff.DiffToolArguments = DiffToolArgsBox.Text.Trim();
            return true;
        }

        private void TabNav_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ShortcutsPanel == null) return;

            var item = TabNav.SelectedItem as System.Windows.Controls.ListBoxItem;
            var tag = item?.Tag?.ToString();

            ShortcutsPanel.Visibility = tag == "Shortcuts" ? Visibility.Visible : Visibility.Collapsed;
            ThemePanel.Visibility     = tag == "Theme"     ? Visibility.Visible : Visibility.Collapsed;
            ViewersPanel.Visibility   = tag == "Viewers"   ? Visibility.Visible : Visibility.Collapsed;
            OpenWithPanel.Visibility  = tag == "OpenWith"  ? Visibility.Visible : Visibility.Collapsed;
            ConsolePanel.Visibility   = tag == "Console"   ? Visibility.Visible : Visibility.Collapsed;
            GitPanel.Visibility       = tag == "Git"       ? Visibility.Visible : Visibility.Collapsed;
            DiffPanel.Visibility      = tag == "Diff"      ? Visibility.Visible : Visibility.Collapsed;
            PluginsPanel.Visibility   = tag == "Plugins"   ? Visibility.Visible : Visibility.Collapsed;
            foreach (var page in _pluginPages)
                page.Panel.Visibility = ReferenceEquals(item, page.NavItem) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AddViewer_Click(object sender, RoutedEventArgs e)
        {
            var ext = NewExtBox.Text.Trim();
            if (!ext.StartsWith(".")) ext = "." + ext;
            var viewerKey = NewViewerCombo.SelectedValue as string;
            if (string.IsNullOrWhiteSpace(ext) || string.IsNullOrWhiteSpace(viewerKey)) return;

            var existing = _workingMappings.FirstOrDefault(m =>
                string.Equals(m.Extension, ext, System.StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                existing.ViewerKey = viewerKey;
            else
                _workingMappings.Add(new ViewerMappingItem { Extension = ext, ViewerKey = viewerKey });

            NewExtBox.Text = ".ext";
            NewViewerCombo.SelectedValue = ViewerConfigService.SystemDefaultKey;
        }

        private void RemoveViewer_Click(object sender, RoutedEventArgs e)
        {
            var ext = (string)((FrameworkElement)sender).Tag;
            var item = _workingMappings.FirstOrDefault(m =>
                string.Equals(m.Extension, ext, System.StringComparison.OrdinalIgnoreCase));
            if (item == null)
                return;

            if (item.IsBuiltInDefault)
                item.ViewerKey = ViewerConfigService.SystemDefaultKey;
            else
                _workingMappings.Remove(item);
        }

        private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_initializingTheme) return;
            var rb = (System.Windows.Controls.RadioButton)sender;
            if (rb.Tag == null) return;
            AppTheme theme;
            if (System.Enum.TryParse<AppTheme>(rb.Tag.ToString(), out theme))
                ThemeManager.ApplyTheme(theme);
        }

        private void OpenChangeBinding(KeyBindingEntry entry)
        {
            var capture = new KeyCaptureWindow(_workingBindings, entry.CommandId) { Owner = this };
            if (capture.ShowDialog() == true)
            {
                entry.Key = capture.CapturedKey;
                entry.Modifiers = capture.CapturedModifiers;
            }
        }

        private void ChangeBinding_Click(object sender, RoutedEventArgs e)
        {
            var entry = (KeyBindingEntry)((FrameworkElement)sender).DataContext;
            OpenChangeBinding(entry);
        }

        private void ShortcutList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ShortcutList.SelectedItem is KeyBindingEntry entry)
                OpenChangeBinding(entry);
        }

        private void ResetDefaults_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "모든 단축키를 기본값으로 초기화하시겠습니까?",
                "기본값으로 초기화",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            var defaults = KeyBindingService.GetDefaults();
            _workingBindings.Clear();
            foreach (var d in defaults)
                _workingBindings.Add(d);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var duplicates = _workingBindings
                .Where(b => b.Key != Key.None)
                .GroupBy(b => new { b.Key, b.Modifiers })
                .Where(g => g.Count() > 1)
                .SelectMany(g => g)
                .Select(b => b.DisplayName)
                .ToList();

            if (duplicates.Any())
            {
                var names = string.Join(", ", duplicates);
                MessageBox.Show(
                    "동일한 단축키가 여러 항목에 지정되어 있습니다:\n" + names + "\n\n충돌을 해결한 후 저장하세요.",
                    "단축키 충돌",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            int fontSize;
            var fontSizeText = ConsoleFontSizeBox.Text.Trim();
            if (!int.TryParse(fontSizeText, out fontSize) ||
                fontSize < ConsoleSettingsService.MinFontSize ||
                fontSize > ConsoleSettingsService.MaxFontSize)
            {
                MessageBox.Show(
                    string.Format(
                        "콘솔 폰트 크기는 {0}에서 {1} 사이의 숫자로 입력하세요.",
                        ConsoleSettingsService.MinFontSize,
                        ConsoleSettingsService.MaxFontSize),
                    "콘솔 설정 오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                TabNav.SelectedIndex = 4;
                ConsoleFontSizeBox.Focus();
                ConsoleFontSizeBox.SelectAll();
                return;
            }

            var preferredProfileKey = ConsoleDefaultProfileCombo.SelectedValue as string;
            if (string.IsNullOrWhiteSpace(preferredProfileKey))
            {
                MessageBox.Show(
                    "콘솔 디폴트 커맨드라인을 선택하세요.",
                    "콘솔 설정 오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                TabNav.SelectedIndex = 4;
                ConsoleDefaultProfileCombo.Focus();
                return;
            }

            if (!TryCollectGitSettings() || !TryCollectDiffSettings())
                return;

            _workingConsoleSettings.FontSize = fontSize;
            _workingConsoleSettings.PreferredProfileKey = preferredProfileKey;
            _workingConsoleSettings.CustomProfiles = _workingConsoleProfiles
                .Select(profile => profile.Clone())
                .ToList();

            // 각 저장을 독립적으로 시도하고 실패를 모아 한 번에 알린다. 서비스 인스턴스는 메인 창과 공유되므로
            // 파일 쓰기가 실패해도 이번 실행 중에는 변경이 적용되어 있고, 다음 실행 때 복원되지 않는다는 점만 알리면 된다.
            // 저장 서비스는 예외를 삼키지 않아야 한다 — 삼키면 다른 PC에서 권한·보안 프로그램 문제로 저장이 안 될 때 원인을 알 수 없다.
            var failures = new List<string>();
            TrySave(failures, "단축키", "keybindings.xml", () => _service.Save(_workingBindings));
            TrySave(failures, "뷰어 매핑", "viewer-config.json", () => _viewerConfig.ReplaceMappings(
                _workingMappings.Select(item => new KeyValuePair<string, string>(item.Extension, item.ViewerKey))));
            TrySave(failures, "열기 프로그램", "open-with.xml", () => OpenWithService.Save(_workingOpenWith));
            TrySave(failures, "콘솔", "console-settings.xml", () => ConsoleSettingsService.Save(_workingConsoleSettings));
            TrySave(failures, "테마", "theme.txt", ThemeManager.SaveCurrentTheme);
            TrySave(failures, "Git", "git-settings.xml", () => GitSettingsService.Save(_workingGit));
            TrySave(failures, "비교", "diff-settings.xml", () => DiffSettingsService.Save(_workingDiff));
            foreach (var page in _pluginPages.Where(p => p.ViewCreated))
                TrySave(failures, "플러그인: " + page.Title, page.PluginId, page.Page.Save);
            SavedGitSettings = _workingGit.Clone();
            SavedDiffSettings = _workingDiff.Clone();

            if (failures.Count > 0)
            {
                MessageBox.Show(
                    "다음 설정을 파일에 저장하지 못했습니다.\n\n" + string.Join("\n", failures) +
                    "\n\n저장 위치: " + SettingsDirectory +
                    "\n\n변경 내용은 이번 실행 중에는 적용되지만 다음 실행 시 복원되지 않을 수 있습니다." +
                    "\n위 폴더의 쓰기 권한과 백신·보안 프로그램의 차단 여부를 확인하세요.",
                    "설정 저장 실패",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            DialogResult = true;
        }

        private static string SettingsDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Folderss");
            }
        }

        private static void TrySave(List<string> failures, string label, string fileName, Action save)
        {
            try
            {
                save();
            }
            catch (Exception ex)
            {
                failures.Add(string.Format("- {0} ({1}): {2}", label, fileName, ex.Message));
            }
        }

        private void OpenWithList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var entry = OpenWithList.SelectedItem as OpenWithEntry;
            if (entry == null) return;
            _editingOpenWithId = entry.Id;
            OpenWithNameBox.Text = entry.Name;
            OpenWithDescBox.Text = entry.Description;
            OpenWithExeBox.Text = entry.ExecutablePath;
            OpenWithArgsBox.Text = entry.Arguments;
            OpenWithMaskBox.Text = entry.ExtensionMask;
        }

        private void OpenWithNew_Click(object sender, RoutedEventArgs e)
        {
            OpenWithList.SelectedItem = null;
            ClearOpenWithForm();
        }

        private void OpenWithSaveEntry_Click(object sender, RoutedEventArgs e)
        {
            var name = OpenWithNameBox.Text.Trim();
            var exe = OpenWithExeBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(exe))
            {
                MessageBox.Show("이름과 실행 파일 경로는 필수입니다.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_editingOpenWithId != null)
            {
                var existing = _workingOpenWith.FirstOrDefault(x => x.Id == _editingOpenWithId);
                if (existing != null)
                {
                    existing.Name = name;
                    existing.Description = OpenWithDescBox.Text.Trim();
                    existing.ExecutablePath = exe;
                    existing.Arguments = OpenWithArgsBox.Text;
                    existing.ExtensionMask = string.IsNullOrWhiteSpace(OpenWithMaskBox.Text) ? "*" : OpenWithMaskBox.Text.Trim();
                    // Refresh ListView
                    var idx = _workingOpenWith.IndexOf(existing);
                    _workingOpenWith.RemoveAt(idx);
                    _workingOpenWith.Insert(idx, existing);
                    OpenWithList.SelectedItem = existing;
                    return;
                }
            }

            var entry = new OpenWithEntry
            {
                Name = name,
                Description = OpenWithDescBox.Text.Trim(),
                ExecutablePath = exe,
                Arguments = OpenWithArgsBox.Text,
                ExtensionMask = string.IsNullOrWhiteSpace(OpenWithMaskBox.Text) ? "*" : OpenWithMaskBox.Text.Trim()
            };
            _workingOpenWith.Add(entry);
            _editingOpenWithId = entry.Id;
            OpenWithList.SelectedItem = entry;
        }

        private void OpenWithDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_editingOpenWithId == null) return;
            var existing = _workingOpenWith.FirstOrDefault(x => x.Id == _editingOpenWithId);
            if (existing == null) return;
            _workingOpenWith.Remove(existing);
            ClearOpenWithForm();
        }

        private void OpenWithBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "실행 파일 선택",
                Filter = "실행 파일 (*.exe)|*.exe|모든 파일 (*.*)|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog(this) == true)
                OpenWithExeBox.Text = dlg.FileName;
        }

        private void ClearOpenWithForm()
        {
            _editingOpenWithId = null;
            OpenWithNameBox.Text = "";
            OpenWithDescBox.Text = "";
            OpenWithExeBox.Text = "";
            OpenWithArgsBox.Text = "\"{0}\"";
            OpenWithMaskBox.Text = "*";
        }

        private void RefreshConsoleProfileOptions()
        {
            var selectedKey = ConsoleDefaultProfileCombo == null ? null : ConsoleDefaultProfileCombo.SelectedValue as string;
            var temporarySettings = _workingConsoleSettings.Clone();
            temporarySettings.CustomProfiles = _workingConsoleProfiles.Select(profile => profile.Clone()).ToList();

            _consoleProfileOptions.Clear();
            foreach (var profile in ConsoleSessionService.GetAvailableProfiles(temporarySettings))
            {
                _consoleProfileOptions.Add(new ConsoleProfileOption
                {
                    Key = profile.Key,
                    DisplayName = profile.DisplayName
                });
            }

            if (ConsoleDefaultProfileCombo == null)
                return;

            var resolvedKey = !string.IsNullOrWhiteSpace(selectedKey)
                ? selectedKey
                : _workingConsoleSettings.PreferredProfileKey;
            if (_consoleProfileOptions.Any(option => option.Key == resolvedKey))
                ConsoleDefaultProfileCombo.SelectedValue = resolvedKey;
            else if (_consoleProfileOptions.Count > 0)
                ConsoleDefaultProfileCombo.SelectedValue = _consoleProfileOptions[0].Key;
        }

        private void ConsoleProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var profile = ConsoleProfileList.SelectedItem as ConsoleCommandProfile;
            if (profile == null) return;

            _editingConsoleProfileKey = profile.Key;
            ConsoleProfileNameBox.Text = profile.DisplayName;
            ConsoleProfileExeBox.Text = profile.FileName;
            ConsoleProfileArgsBox.Text = profile.Arguments;

            foreach (ComboBoxItem item in ConsoleProfileShellKindCombo.Items)
            {
                if ((item.Tag as string) == profile.ShellKind)
                {
                    ConsoleProfileShellKindCombo.SelectedItem = item;
                    return;
                }
            }

            ConsoleProfileShellKindCombo.SelectedIndex = 0;
        }

        private void ConsoleProfileNew_Click(object sender, RoutedEventArgs e)
        {
            ConsoleProfileList.SelectedItem = null;
            ClearConsoleProfileForm();
        }

        private void ConsoleProfileSave_Click(object sender, RoutedEventArgs e)
        {
            var displayName = ConsoleProfileNameBox.Text.Trim();
            var fileName = ConsoleProfileExeBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(fileName))
            {
                MessageBox.Show("표시 이름과 실행 파일 경로는 필수입니다.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var shellKind = (ConsoleProfileShellKindCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            if (string.IsNullOrWhiteSpace(shellKind))
                shellKind = ConsoleShellKind.WindowsPowerShell.ToString();

            if (_editingConsoleProfileKey != null)
            {
                var existing = _workingConsoleProfiles.FirstOrDefault(profile => profile.Key == _editingConsoleProfileKey);
                if (existing != null)
                {
                    existing.DisplayName = displayName;
                    existing.FileName = fileName;
                    existing.Arguments = ConsoleProfileArgsBox.Text;
                    existing.ShellKind = shellKind;

                    var index = _workingConsoleProfiles.IndexOf(existing);
                    _workingConsoleProfiles.RemoveAt(index);
                    _workingConsoleProfiles.Insert(index, existing);
                    ConsoleProfileList.SelectedItem = existing;
                    RefreshConsoleProfileOptions();
                    return;
                }
            }

            var profile = new ConsoleCommandProfile
            {
                Key = "custom:" + System.Guid.NewGuid().ToString("N"),
                DisplayName = displayName,
                FileName = fileName,
                Arguments = ConsoleProfileArgsBox.Text,
                ShellKind = shellKind,
                IsBuiltIn = false
            };
            _workingConsoleProfiles.Add(profile);
            _editingConsoleProfileKey = profile.Key;
            ConsoleProfileList.SelectedItem = profile;
            RefreshConsoleProfileOptions();
        }

        private void ConsoleProfileDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_editingConsoleProfileKey == null) return;

            var existing = _workingConsoleProfiles.FirstOrDefault(profile => profile.Key == _editingConsoleProfileKey);
            if (existing == null) return;

            _workingConsoleProfiles.Remove(existing);
            RefreshConsoleProfileOptions();
            ClearConsoleProfileForm();
        }

        private void ConsoleProfileBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "콘솔 실행 파일 선택",
                Filter = "실행 파일 (*.exe)|*.exe|모든 파일 (*.*)|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog(this) == true)
                ConsoleProfileExeBox.Text = dlg.FileName;
        }

        private void ClearConsoleProfileForm()
        {
            _editingConsoleProfileKey = null;
            ConsoleProfileNameBox.Text = "";
            ConsoleProfileExeBox.Text = "";
            ConsoleProfileArgsBox.Text = "";
            ConsoleProfileShellKindCombo.SelectedIndex = 0;
        }

        private static string ToViewerDisplayName(string key)
        {
            switch (key)
            {
                case ViewerConfigService.SystemDefaultKey: return "System Default";
                case ViewerConfigService.BuiltInMarkdownKey: return "Markdown";
                case ViewerConfigService.BuiltInMonacoKey: return "Monaco";
                case ViewerConfigService.BuiltInTextKey: return "Text";
                default: return key;
            }
        }

        // ── 플러그인 ────────────────────────────────────────────────────────

        private sealed class PluginPageEntry
        {
            public string PluginId;
            public string Title;
            public IPluginSettingsPage Page;
            public ListBoxItem NavItem;
            public FrameworkElement Panel;
            public bool ViewCreated;
        }

        private readonly List<PluginPageEntry> _pluginPages = new List<PluginPageEntry>();

        /// <summary>
        /// 플러그인 목록을 채우고, 설정 탭이 있다고 선언한(plugin.json hasSettings) 플러그인을 로드해 탭을 붙인다.
        /// 플러그인 코드(로드·Title·CreateView) 실패는 그 탭/행에만 표시하고 설정 창은 계속 연다.
        /// </summary>
        private void InitializePluginPages()
        {
            var items = RefreshPluginList();
            foreach (var row in items.Where(r => r.Manifest.HasSettings))
            {
                string error;
                var loaded = PluginManager.TryLoad(row.Manifest, out error);
                if (loaded == null)
                {
                    row.Status = "로드 실패: " + error;
                    continue;
                }
                row.Status = "로드됨";
                foreach (var page in loaded.Host.SettingsPages)
                    AddPluginPage(row.Manifest, page);
            }
            PluginList.Items.Refresh();
        }

        private void AddPluginPage(PluginManifest manifest, IPluginSettingsPage page)
        {
            var entry = new PluginPageEntry { PluginId = manifest.Id, Page = page };
            try { entry.Title = page.Title; } catch { }
            if (string.IsNullOrWhiteSpace(entry.Title))
                entry.Title = manifest.DisplayName;

            FrameworkElement view;
            try
            {
                view = page.CreateView();
                entry.ViewCreated = view != null;
                if (view == null)
                    view = new TextBlock { Text = "설정 화면이 없습니다 (CreateView()가 null)." };
            }
            catch (Exception ex)
            {
                view = new TextBlock { Text = "플러그인 설정 화면을 만들지 못했습니다.\n\n" + ex.GetType().Name + ": " + ex.Message, TextWrapping = TextWrapping.Wrap };
            }

            entry.Panel = new Border { Margin = new Thickness(24, 16, 24, 16), Child = view, Visibility = Visibility.Collapsed };
            entry.NavItem = new ListBoxItem { Content = new TextBlock { Text = entry.Title }, ToolTip = manifest.Id };
            ContentHost.Children.Add(entry.Panel);
            TabNav.Items.Add(entry.NavItem);
            _pluginPages.Add(entry);
        }

        private List<PluginListItem> RefreshPluginList()
        {
            var errors = new List<string>();
            var items = PluginManager.ListInstalled(errors)
                .Select(m => new PluginListItem { Manifest = m, Status = PluginManager.IsLoaded(m.Id) ? "로드됨" : string.Empty })
                .ToList();
            PluginList.ItemsSource = items;
            PluginErrorText.Text = errors.Count == 0 ? string.Empty : string.Format("읽지 못한 zip {0}개", errors.Count);
            PluginErrorText.ToolTip = errors.Count == 0 ? null : string.Join("\n", errors);
            return items;
        }

        private void PluginBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Title = "플러그인 찾기", Filter = "플러그인 zip (*.zip)|*.zip" };
            if (dialog.ShowDialog(this) != true)
                return;

            PluginManifest manifest;
            try
            {
                manifest = PluginPackage.ReadManifest(dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show("플러그인 파일이 아닙니다.\n\n" + ex.Message, "플러그인 찾기", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var exists = File.Exists(PluginPackage.GetPackagePath(PluginManager.PluginsDirectory, manifest.Id));
            var message = string.Format(
                "{0} {1} ({2})\n\n플러그인은 Folderss와 같은 권한으로 실행되어 파일을 읽고 쓰거나 프로그램을 실행할 수 있습니다. " +
                "신뢰할 수 있는 출처의 플러그인만 추가하세요.{3}\n\n추가할까요?",
                manifest.DisplayName, manifest.Version, manifest.Id,
                exists ? "\n\n같은 ID의 플러그인이 이미 있어 교체합니다." : string.Empty);
            if (MessageBox.Show(message, "플러그인 추가", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;

            try
            {
                PluginPackage.Install(dialog.FileName, PluginManager.PluginsDirectory);
            }
            catch (Exception ex)
            {
                MessageBox.Show("플러그인을 추가하지 못했습니다.\n\n" + ex.Message + "\n\n저장 위치: " + PluginManager.PluginsDirectory,
                    "플러그인 추가", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            RefreshPluginList();
            if (PluginManager.IsLoaded(manifest.Id))
                MessageBox.Show("이미 로드된 플러그인이라 교체한 버전은 다시 시작한 뒤에 적용됩니다.", "플러그인 추가",
                    MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void PluginRemove_Click(object sender, RoutedEventArgs e)
        {
            var row = PluginList.SelectedItem as PluginListItem;
            if (row == null)
                return;
            if (MessageBox.Show(string.Format("{0}({1})을 제거할까요?\n플러그인 설정·데이터(plugin-data)는 남겨 둡니다.", row.Name, row.Id),
                    "플러그인 제거", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;

            try
            {
                File.Delete(row.Manifest.PackagePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show("플러그인을 제거하지 못했습니다.\n\n" + ex.Message, "플러그인 제거", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            RefreshPluginList();
            if (PluginManager.IsLoaded(row.Id))
                MessageBox.Show("이미 로드된 플러그인은 다시 시작할 때까지 메모리에 남아 있습니다.", "플러그인 제거",
                    MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (ThemeManager.CurrentTheme != _originalTheme)
                ThemeManager.ApplyTheme(_originalTheme);
            Close();
        }
    }
}

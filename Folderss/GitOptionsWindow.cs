using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Folderss.Services;

namespace Folderss
{
    /// <summary>
    /// Git 창 옵션 대화상자: 기준 폴더 규칙, pull 방식, 저장소 탐색 깊이·제외 폴더, 로그 개수·범위.
    /// 확인을 누르면 <see cref="Result"/>에 새 설정을 담는다(저장은 호출 측).
    /// </summary>
    public sealed class GitOptionsWindow : Window
    {
        private readonly ComboBox _baseFolder;
        private readonly ComboBox _pullMode;
        private readonly TextBox _scanDepth;
        private readonly TextBox _excluded;
        private readonly TextBox _logLimit;
        private readonly CheckBox _logAll;
        private readonly GitSettings _original;

        public GitSettings Result { get; private set; }

        private static readonly (GitBaseFolderMode Mode, string Text)[] BaseFolderChoices =
        {
            (GitBaseFolderMode.SelectedFolderFirst, "선택한 폴더 우선 (없으면 현재 폴더)"),
            (GitBaseFolderMode.CurrentFolder, "항상 패널의 현재 폴더")
        };

        private static readonly (GitPullMode Mode, string Text)[] PullChoices =
        {
            (GitPullMode.FastForwardOnly, "fast-forward만 (--ff-only, 권장)"),
            (GitPullMode.Merge, "병합 허용 (--no-rebase)"),
            (GitPullMode.Rebase, "rebase (--rebase)"),
            (GitPullMode.UseGitConfig, "git 설정 따름 (pull.rebase / pull.ff)")
        };

        public GitOptionsWindow(GitSettings settings)
        {
            _original = (settings ?? new GitSettings()).Clone();

            Title = "Git 옵션";
            Width = 480;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            SetResourceReference(BackgroundProperty, "WindowBackground");
            SetResourceReference(ForegroundProperty, "PrimaryText");
            SetResourceReference(FontFamilyProperty, "AppFontFamily");
            FontSize = 13;

            var root = new StackPanel { Margin = new Thickness(16) };

            _baseFolder = AddCombo(root, "기준 폴더", BaseFolderChoices.Select(c => c.Text),
                Array.FindIndex(BaseFolderChoices, c => c.Mode == _original.BaseFolderMode),
                "메뉴에서 Git 창을 열 때 저장소를 찾기 시작할 폴더입니다.");

            _pullMode = AddCombo(root, "pull 방식", PullChoices.Select(c => c.Text),
                Array.FindIndex(PullChoices, c => c.Mode == _original.PullMode),
                "fast-forward만: 갈라졌으면 아무것도 바꾸지 않고 실패합니다.\n병합·rebase: 충돌이 나면 저장소가 병합/rebase 중 상태로 남으며, 해결은 콘솔/IDE에서 해야 합니다.");

            _scanDepth = AddText(root, string.Format("탐색 깊이 ({0}~{1}, 기준 폴더 = 0)", GitSettingsService.MinScanDepth, GitSettingsService.MaxScanDepth),
                _original.ScanDepth.ToString(), false,
                "깊을수록 오래 걸립니다. 드라이브 루트나 네트워크 드라이브에서는 작게 두세요.");

            _excluded = AddText(root, "탐색 제외 폴더 이름 (한 줄에 하나)",
                string.Join(Environment.NewLine, _original.ExcludedFolders), true,
                "이 이름의 폴더에는 들어가지 않습니다(대/소문자 무시). .git 폴더와 링크 폴더는 항상 제외됩니다.");

            _logLimit = AddText(root, string.Format("로그 최대 개수 ({0}~{1})", GitSettingsService.MinLogLimit, GitSettingsService.MaxLogLimit),
                _original.LogLimit.ToString(), false, null);

            _logAll = new CheckBox
            {
                Content = "로그에 모든 브랜치 포함 (--all)",
                IsChecked = _original.LogAllBranches,
                Margin = new Thickness(0, 4, 0, 0)
            };
            _logAll.SetResourceReference(ForegroundProperty, "PrimaryText");
            root.Children.Add(_logAll);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            var reset = new Button { Content = "기본값", MinWidth = 75 };
            reset.Click += (s, e) => Fill(new GitSettings());
            var ok = new Button { Content = "확인", IsDefault = true, MinWidth = 75 };
            ok.Click += Ok_Click;
            var cancel = new Button { Content = "취소", IsCancel = true, MinWidth = 75 };
            buttons.Children.Add(reset);
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);

            Content = root;
        }

        private static TextBlock Label(string text)
        {
            return new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) };
        }

        private static void AddHint(Panel root, string hint)
        {
            if (string.IsNullOrEmpty(hint))
                return;
            var block = new TextBlock { Text = hint, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 3, 0, 0) };
            block.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            root.Children.Add(block);
        }

        private static ComboBox AddCombo(Panel root, string label, IEnumerable<string> items, int selectedIndex, string hint)
        {
            root.Children.Add(Label(label));
            var combo = new ComboBox { ItemsSource = items.ToList(), SelectedIndex = Math.Max(0, selectedIndex) };
            root.Children.Add(combo);
            AddHint(root, hint);
            return combo;
        }

        private static TextBox AddText(Panel root, string label, string value, bool multiline, string hint)
        {
            root.Children.Add(Label(label));
            var box = new TextBox { Text = value };
            if (multiline)
            {
                box.AcceptsReturn = true;
                box.Height = 90;
                box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            }
            root.Children.Add(box);
            AddHint(root, hint);
            return box;
        }

        private void Fill(GitSettings settings)
        {
            _baseFolder.SelectedIndex = Array.FindIndex(BaseFolderChoices, c => c.Mode == settings.BaseFolderMode);
            _pullMode.SelectedIndex = Array.FindIndex(PullChoices, c => c.Mode == settings.PullMode);
            _scanDepth.Text = settings.ScanDepth.ToString();
            _excluded.Text = string.Join(Environment.NewLine, settings.ExcludedFolders);
            _logLimit.Text = settings.LogLimit.ToString();
            _logAll.IsChecked = settings.LogAllBranches;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!TryReadInt(_scanDepth, GitSettingsService.MinScanDepth, GitSettingsService.MaxScanDepth, "탐색 깊이", out var depth)
                || !TryReadInt(_logLimit, GitSettingsService.MinLogLimit, GitSettingsService.MaxLogLimit, "로그 최대 개수", out var logLimit))
                return;

            var excluded = _excluded.Text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(name => name.Trim())
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (excluded.Any(name => name.IndexOfAny(new[] { '\\', '/' }) >= 0))
            {
                MessageBox.Show(this, "제외 폴더에는 경로가 아니라 폴더 이름만 적으세요 (예: node_modules).", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                _excluded.Focus();
                return;
            }

            Result = new GitSettings
            {
                BaseFolderMode = BaseFolderChoices[Math.Max(0, _baseFolder.SelectedIndex)].Mode,
                PullMode = PullChoices[Math.Max(0, _pullMode.SelectedIndex)].Mode,
                ScanDepth = depth,
                ExcludedFolders = excluded,
                LogLimit = logLimit,
                LogAllBranches = _logAll.IsChecked == true
            };
            DialogResult = true;
        }

        private bool TryReadInt(TextBox box, int min, int max, string name, out int value)
        {
            if (int.TryParse(box.Text.Trim(), out value) && value >= min && value <= max)
                return true;

            MessageBox.Show(this, string.Format("{0}는 {1}~{2} 사이 숫자여야 합니다.", name, min, max), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            box.Focus();
            box.SelectAll();
            return false;
        }
    }
}

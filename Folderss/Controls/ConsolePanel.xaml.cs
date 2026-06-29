using Folderss.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasyWindowsTerminalControl;

namespace Folderss.Controls
{
    public partial class ConsolePanel : UserControl, IDisposable
    {
        public class ConsoleTab : INotifyPropertyChanged
        {
            private string _title;
            public string Title
            {
                get => _title;
                set { _title = value; OnPropertyChanged(nameof(Title)); }
            }

            public EasyTerminalControl Terminal { get; set; }
            public string ProfileKey { get; set; }
            public bool IsAddTab { get; set; }

            public event PropertyChangedEventHandler PropertyChanged;
            protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private ConsoleSettings _settings;
        private List<ConsoleCommandProfile> _profiles = new List<ConsoleCommandProfile>();
        private bool _updatingShellSelection;
        private bool _disposed;
        private int _tabCounter = 0;
        private ObservableCollection<ConsoleTab> _tabs = new ObservableCollection<ConsoleTab>();
        private ConsoleTab _addTabItem;
        private bool _allowAddTabActivation;
        private bool _restoringConsoleTabSelection;
        private static readonly PropertyInfo EasyTerminalThemeGetter =
            typeof(EasyTerminalControl).GetProperty("Theme", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo EasyTerminalSetThemeMethod =
            typeof(EasyTerminalControl).GetMethod("SetTheme", BindingFlags.Instance | BindingFlags.NonPublic);

        public event EventHandler MaximizeRequested;
        public event EventHandler MinimizeRequested;

        public Func<string> ActiveDirectoryProvider { get; set; }

        private ConsoleTab ActiveTab => ConsoleTabs.SelectedItem as ConsoleTab;

        public ConsolePanel()
        {
            InitializeComponent();

            ReloadProfiles();

            // '+ 새 콘솔' 역할을 할 가짜 탭(가장 마지막에 항상 상주) 인스턴스 초기화
            _addTabItem = new ConsoleTab
            {
                Title = "+ 새 콘솔",
                IsAddTab = true,
                Terminal = null
            };

            ConsoleTabs.ItemsSource = _tabs;

            IsVisibleChanged += (s, e) =>
            {
                if ((bool)e.NewValue)
                {
                    FocusCommandBox();
                }
            };

            Loaded += (s, e) =>
            {
                if (_tabs.Count == 0)
                {
                    // 1단계. 가짜 탭을 리스트 맨 끝에 추가해 둡니다.
                    _tabs.Add(_addTabItem);
                    // 2단계. 구성에 맞게 첫 번째 실제 셸 탭을 생성 (자동으로 가짜 탭 앞에 안착)
                    var configured = GetPreferredProfile();
                    AddConsoleTab(configured.Key, GetActiveDirectory());
                }
            };
        }

        public void EnsureStarted()
        {
            var active = ActiveTab;
            if (active != null && !active.IsAddTab && (active.Terminal.ConPTYTerm == null || !active.Terminal.ConPTYTerm.TermProcIsStarted))
            {
                StartSelectedShell(active, GetActiveDirectory());
            }
        }

        public void FocusCommandBox()
        {
            var active = ActiveTab;
            if (active != null && !active.IsAddTab)
            {
                active.Terminal?.Focus();
            }
        }

        private static bool IsBareHomeOrEnd(KeyEventArgs e)
        {
            return Keyboard.Modifiers == ModifierKeys.None &&
                   (e.Key == Key.Home || e.Key == Key.End);
        }

        private static bool IsBareHomeOrEndPressed()
        {
            return Keyboard.Modifiers == ModifierKeys.None &&
                   (Keyboard.IsKeyDown(Key.Home) || Keyboard.IsKeyDown(Key.End));
        }

        private static string GetTerminalSequence(Key key)
        {
            if (key == Key.Home)
                return "\u001b[H";
            if (key == Key.End)
                return "\u001b[F";
            return null;
        }

        private void ForwardNavigationKeyToActiveTerminal(Key key)
        {
            var active = ActiveTab;
            if (active == null || active.IsAddTab || active.Terminal == null)
                return;

            var sequence = GetTerminalSequence(key);
            if (sequence == null)
                return;

            active.Terminal.Focus();
            active.Terminal.ConPTYTerm?.WriteToTerm(sequence);
        }

        private void RestoreConsoleTabSelection(ConsoleTab target)
        {
            if (target == null)
                return;

            _restoringConsoleTabSelection = true;
            try
            {
                ConsoleTabs.SelectedItem = target;
            }
            finally
            {
                _restoringConsoleTabSelection = false;
            }

            Dispatcher.BeginInvoke(new Action(FocusCommandBox), System.Windows.Threading.DispatcherPriority.Input);
        }

        public ConsoleTab AddConsoleTab(string profileKey, string workingDirectory)
        {
            RefreshSettings(profileKey);
            _tabCounter++;
            var profile = GetProfile(profileKey) ?? GetPreferredProfile();

            var terminal = new EasyTerminalControl
            {
                Focusable = true,
                StartupCommandLine = ConsoleSessionService.BuildCommandLine(profile, workingDirectory)
            };
            ApplyTerminalAppearance(terminal);

            // WPF 포커스 탐색기가 Tab 키를 빼앗아 콤보박스 등으로 튀지 않도록 제어 차단
            KeyboardNavigation.SetTabNavigation(terminal, KeyboardNavigationMode.None);
            KeyboardNavigation.SetDirectionalNavigation(terminal, KeyboardNavigationMode.None);

            // Tab 키 Preview 단계에서 WPF 기본 동작을 억제하고 PTY 파이프로 다이렉트 전송
            terminal.PreviewKeyDown += (s, args) =>
            {
                if (args.Key == Key.Tab)
                {
                    args.Handled = true;
                    terminal.ConPTYTerm?.WriteToTerm("\t");
                    return;
                }

                if (IsBareHomeOrEnd(args))
                {
                    args.Handled = true;
                    var sequence = GetTerminalSequence(args.Key);
                    if (sequence != null)
                        terminal.ConPTYTerm?.WriteToTerm(sequence);
                }
            };

            var dpd = DependencyPropertyDescriptor.FromProperty(
                EasyTerminalControl.ConPTYTermProperty,
                typeof(EasyTerminalControl));
            if (dpd != null)
            {
                dpd.AddValueChanged(terminal, (s, e) =>
                {
                    var pty = terminal.ConPTYTerm;
                    if (pty != null)
                    {
                        pty.TermReady -= Pty_TermReady;
                        pty.TermReady += Pty_TermReady;
                    }
                });
            }

            var tab = new ConsoleTab
            {
                Title = $"{profile.DisplayName} ({_tabCounter})",
                Terminal = terminal,
                ProfileKey = profile.Key,
                IsAddTab = false
            };

            // 고정 추가 탭('+ 새 콘솔') 바로 앞 인덱스에 새 콘솔 탭을 삽입합니다.
            int insertIndex = Math.Max(0, _tabs.Count - 1);
            _tabs.Insert(insertIndex, tab);
            ConsoleTabs.SelectedItem = tab;

            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await terminal.RestartTerm(null, true);
                    ApplyTerminalAppearance(terminal);
                    UpdateStatus();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("터미널을 시작할 수 없습니다: " + ex.Message, "터미널 에러", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);

            return tab;
        }

        public void CloseTab(ConsoleTab tab)
        {
            if (tab == null || tab.IsAddTab) return;

            try
            {
                tab.Terminal.ConPTYTerm?.Process?.Kill(true);
            }
            catch { }

            // 만약 현재 닫으려는 탭이 활성 탭 상태라면, 포커스가 '+ 새 콘솔'로 튀어서 원하지 않는 새 탭이 자동 생성되는 것을 방지합니다.
            if (ConsoleTabs.SelectedItem == tab)
            {
                int index = _tabs.IndexOf(tab);
                if (index > 0)
                {
                    // 왼쪽의 실제 탭으로 포커스를 명시적으로 이동시킵니다.
                    ConsoleTabs.SelectedItem = _tabs[index - 1];
                }
                else if (_tabs.Count > 2)
                {
                    // 첫 번째 실제 탭으로 이동시킵니다 (가짜 탭 제외)
                    ConsoleTabs.SelectedItem = _tabs[0];
                }
            }

            _tabs.Remove(tab);

            // 실제 콘솔 탭이 모두 삭제되고 가짜 추가 탭(1개)만 남았을 경우, PowerShell 실제 탭을 자동으로 채워 줍니다.
            if (_tabs.Count <= 1)
            {
                var configured = GetPreferredProfile();
                AddConsoleTab(configured.Key, GetActiveDirectory());
            }
        }

        private void ReloadProfiles()
        {
            _settings = ConsoleSettingsService.Load();
            _profiles = ConsoleSessionService.GetAvailableProfiles(_settings).ToList();
            ShellCombo.ItemsSource = _profiles;
            ShellCombo.DisplayMemberPath = "DisplayName";
            ShellCombo.SelectedValuePath = "Key";
        }

        private void RefreshSettings(string preferredProfileKey = null)
        {
            var currentSelection = preferredProfileKey
                ?? (ShellCombo == null ? null : ShellCombo.SelectedValue as string)
                ?? (ActiveTab == null ? null : ActiveTab.ProfileKey);
            ReloadProfiles();

            var resolvedProfile = GetProfile(currentSelection) ?? GetPreferredProfile();
            SelectConfiguredShell(resolvedProfile.Key);
        }

        private void SelectConfiguredShell(string profileKey)
        {
            _updatingShellSelection = true;
            try
            {
                ShellCombo.SelectedValue = profileKey;
            }
            finally
            {
                _updatingShellSelection = false;
            }
        }

        private string GetActiveDirectory()
        {
            var path = ActiveDirectoryProvider == null ? null : ActiveDirectoryProvider();
            return string.IsNullOrWhiteSpace(path)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : path;
        }

        private ConsoleCommandProfile GetProfile(string profileKey)
        {
            return _profiles.FirstOrDefault(profile =>
                string.Equals(profile.Key, profileKey, StringComparison.OrdinalIgnoreCase));
        }

        private ConsoleCommandProfile GetPreferredProfile()
        {
            return ConsoleSessionService.GetPreferredProfile(_settings);
        }

        private void Pty_TermReady(object sender, EventArgs e)
        {
            // 상속받아 깨끗하게 켜지도록 문자열 명령을 생략합니다.
        }

        private void ApplyTerminalAppearance(EasyTerminalControl terminal)
        {
            if (terminal == null)
                return;

            var fontSize = ConsoleSettingsService.ClampFontSize(_settings.FontSize);
            terminal.FontSize = fontSize;
            terminal.FontSizeWhenSettingTheme = fontSize;

            if (EasyTerminalSetThemeMethod == null)
                return;

            try
            {
                var theme = EasyTerminalThemeGetter == null
                    ? null
                    : EasyTerminalThemeGetter.GetValue(terminal, null);
                EasyTerminalSetThemeMethod.Invoke(terminal, new[] { theme });
            }
            catch
            {
                // 내부 테마 재적용 실패가 콘솔 시작 자체를 막지 않도록 무시한다.
            }
        }

        private async void StartSelectedShell(ConsoleTab tab, string workingDirectory, string targetProfileKey = null)
        {
            if (tab == null || tab.IsAddTab) return;

            RefreshSettings(targetProfileKey ?? tab.ProfileKey);
            var profile = GetProfile(targetProfileKey ?? (ShellCombo.SelectedValue as string))
                ?? GetPreferredProfile();

            var cmd = ConsoleSessionService.BuildCommandLine(profile, workingDirectory);
            tab.Terminal.StartupCommandLine = cmd;
            ApplyTerminalAppearance(tab.Terminal);
            tab.ProfileKey = profile.Key;
            tab.Title = $"{profile.DisplayName} ({_tabCounter})";

            try
            {
                await tab.Terminal.RestartTerm(null, true);
                ApplyTerminalAppearance(tab.Terminal);

                _settings.PreferredProfileKey = profile.Key;
                ConsoleSettingsService.Save(_settings);
                UpdateStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show("터미널을 시작할 수 없습니다: " + ex.Message, "터미널 에러", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateStatus()
        {
            var active = ActiveTab;
            bool isStarted = active != null && !active.IsAddTab && active.Terminal.ConPTYTerm != null && active.Terminal.ConPTYTerm.TermProcIsStarted;
            StatusText.Text = isStarted ? "실행 중" : string.Empty;
        }

        private void ShellCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingShellSelection || ActiveTab == null || ActiveTab.IsAddTab)
                return;

            var active = ActiveTab;
            var profileKey = ShellCombo.SelectedValue as string;
            if (string.Equals(active.ProfileKey, profileKey, StringComparison.OrdinalIgnoreCase))
                return;

            var result = MessageBox.Show(
                "셸을 변경하면 현재 콘솔 세션이 재시작됩니다.",
                "콘솔 셸 변경",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.OK)
            {
                SelectConfiguredShell(active.ProfileKey);
                return;
            }

            StartSelectedShell(active, GetActiveDirectory(), profileKey);
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            MaximizeRequested?.Invoke(this, EventArgs.Empty);
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            MinimizeRequested?.Invoke(this, EventArgs.Empty);
        }

        public void UpdateMaximizeButton(bool isMaximized)
        {
            MaximizeButton.Content = isMaximized ? "❐" : "□";
            MaximizeButton.ToolTip = isMaximized ? "이전 크기로 복원" : "최대화";
        }

        private void ConsolePanel_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                FocusCommandBox();
        }

        private void ConsoleTabs_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            var tabItem = FindAncestor<TabItem>(source);
            var tab = tabItem == null ? null : tabItem.DataContext as ConsoleTab;
            _allowAddTabActivation = tab != null && tab.IsAddTab;
        }

        private void ConsoleTabs_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!IsBareHomeOrEnd(e))
                return;

            e.Handled = true;
            ForwardNavigationKeyToActiveTerminal(e.Key);
        }

        private void ConsoleTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_restoringConsoleTabSelection)
                return;

            var active = ActiveTab;
            if (active == null) return;

            var previous = e.RemovedItems.OfType<ConsoleTab>().FirstOrDefault(tab => tab != null && !tab.IsAddTab)
                ?? _tabs.FirstOrDefault(tab => !tab.IsAddTab && !ReferenceEquals(tab, active));

            if (IsBareHomeOrEndPressed() && previous != null && !ReferenceEquals(active, previous))
            {
                RestoreConsoleTabSelection(previous);
                return;
            }

            if (active.IsAddTab)
            {
                if (!_allowAddTabActivation)
                {
                    RestoreConsoleTabSelection(previous);
                    return;
                }

                _allowAddTabActivation = false;
                AddConsoleTab(GetPreferredProfile().Key, GetActiveDirectory());
                return;
            }

            _allowAddTabActivation = false;
            SelectConfiguredShell(active.ProfileKey);
            UpdateStatus();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                FocusCommandBox();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                var match = current as T;
                if (match != null)
                    return match;

                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var tab = btn?.Tag as ConsoleTab;
            CloseTab(tab);
        }

        private void RenameTab_Click(object sender, RoutedEventArgs e)
        {
            var item = sender as MenuItem;
            var tab = item?.Tag as ConsoleTab;
            if (tab == null || tab.IsAddTab) return;

            var prompt = new PromptWindow("이름 변경", "새 이름을 입력하세요.", tab.Title);
            prompt.Owner = Window.GetWindow(this);
            if (prompt.ShowDialog() == true)
            {
                var newName = prompt.Value.Trim();
                if (!string.IsNullOrWhiteSpace(newName))
                {
                    tab.Title = newName;
                }
            }
        }

        private void CloseTabMenu_Click(object sender, RoutedEventArgs e)
        {
            var item = sender as MenuItem;
            var tab = item?.Tag as ConsoleTab;
            CloseTab(tab);
        }

        private void CloseOtherTabs_Click(object sender, RoutedEventArgs e)
        {
            var item = sender as MenuItem;
            var tab = item?.Tag as ConsoleTab;
            if (tab == null || tab.IsAddTab) return;

            var targets = _tabs.Where(t => t != tab && !t.IsAddTab).ToList();
            foreach (var target in targets)
            {
                CloseTab(target);
            }
        }

        private void CloseRightTabs_Click(object sender, RoutedEventArgs e)
        {
            var item = sender as MenuItem;
            var tab = item?.Tag as ConsoleTab;
            if (tab == null || tab.IsAddTab) return;

            int targetIndex = _tabs.IndexOf(tab);
            if (targetIndex < 0) return;

            var targets = _tabs.Where((t, idx) => idx > targetIndex && !t.IsAddTab).ToList();
            foreach (var target in targets)
            {
                CloseTab(target);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            foreach (var tab in _tabs)
            {
                if (tab.IsAddTab) continue;
                try
                {
                    tab.Terminal.ConPTYTerm?.Process?.Kill(true);
                }
                catch { }
            }
            _tabs.Clear();
        }
    }
}

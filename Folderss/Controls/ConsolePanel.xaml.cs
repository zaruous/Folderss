using Folderss.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
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
            public ConsoleShellKind ShellKind { get; set; }
            public bool IsAddTab { get; set; }

            public event PropertyChangedEventHandler PropertyChanged;
            protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private ConsoleSettings _settings;
        private bool _updatingShellSelection;
        private bool _disposed;
        private int _tabCounter = 0;
        private ObservableCollection<ConsoleTab> _tabs = new ObservableCollection<ConsoleTab>();
        private ConsoleTab _addTabItem;

        public Func<string> ActiveDirectoryProvider { get; set; }

        private ConsoleTab ActiveTab => ConsoleTabs.SelectedItem as ConsoleTab;

        public ConsolePanel()
        {
            InitializeComponent();

            _settings = ConsoleSettingsService.Load();
            ShellCombo.ItemsSource = ConsoleSessionService.GetAvailableShells();

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
                    var configured = ConsoleSessionService.ParseShellKind(_settings.PreferredShellKind);
                    AddConsoleTab(configured, GetActiveDirectory());
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

        public ConsoleTab AddConsoleTab(ConsoleShellKind kind, string workingDirectory)
        {
            _tabCounter++;
            var shell = ConsoleSessionService.GetAvailableShells().FirstOrDefault(s => s.Kind == kind)
                ?? ConsoleSessionService.GetAvailableShells().First();

            var terminal = new EasyTerminalControl
            {
                Focusable = true,
                StartupCommandLine = BuildCommandLine(shell)
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
                Title = $"{shell.DisplayName} ({_tabCounter})",
                Terminal = terminal,
                ShellKind = kind,
                IsAddTab = false
            };

            if (!string.IsNullOrEmpty(workingDirectory) && System.IO.Directory.Exists(workingDirectory))
            {
                try
                {
                    System.IO.Directory.SetCurrentDirectory(workingDirectory);
                }
                catch { }
            }

            // 고정 추가 탭('+ 새 콘솔') 바로 앞 인덱스에 새 콘솔 탭을 삽입합니다.
            int insertIndex = Math.Max(0, _tabs.Count - 1);
            _tabs.Insert(insertIndex, tab);
            ConsoleTabs.SelectedItem = tab;

            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await terminal.RestartTerm(null, true);
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
                var configured = ConsoleSessionService.ParseShellKind(_settings.PreferredShellKind);
                AddConsoleTab(configured, GetActiveDirectory());
            }
        }

        private void SelectConfiguredShell(ConsoleShellKind kind)
        {
            _updatingShellSelection = true;
            try
            {
                ShellCombo.SelectedValue = kind;
            }
            finally
            {
                _updatingShellSelection = false;
            }
        }

        private ConsoleShellKind GetSelectedShellKind()
        {
            if (ShellCombo.SelectedValue is ConsoleShellKind)
                return (ConsoleShellKind)ShellCombo.SelectedValue;
            return ConsoleShellKind.WindowsPowerShell;
        }

        private string GetActiveDirectory()
        {
            var path = ActiveDirectoryProvider == null ? null : ActiveDirectoryProvider();
            return string.IsNullOrWhiteSpace(path)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : path;
        }

        private string BuildCommandLine(ConsoleShellInfo shell)
        {
            var cmd = shell.FileName;
            if (!string.IsNullOrWhiteSpace(shell.Arguments))
            {
                cmd += " " + shell.Arguments;
            }
            return cmd;
        }

        private void Pty_TermReady(object sender, EventArgs e)
        {
            // 상속받아 깨끗하게 켜지도록 문자열 명령을 생략합니다.
        }

        private async void StartSelectedShell(ConsoleTab tab, string workingDirectory)
        {
            if (tab == null || tab.IsAddTab) return;

            var shellKind = GetSelectedShellKind();
            var shell = ConsoleSessionService.GetAvailableShells().FirstOrDefault(s => s.Kind == shellKind)
                ?? ConsoleSessionService.GetAvailableShells().First();

            var cmd = BuildCommandLine(shell);
            tab.Terminal.StartupCommandLine = cmd;
            tab.ShellKind = shellKind;
            tab.Title = $"{shell.DisplayName} ({_tabCounter})";

            try
            {
                if (!string.IsNullOrEmpty(workingDirectory) && System.IO.Directory.Exists(workingDirectory))
                {
                    try
                    {
                        System.IO.Directory.SetCurrentDirectory(workingDirectory);
                    }
                    catch { }
                }

                await tab.Terminal.RestartTerm(null, true);

                _settings.PreferredShellKind = shellKind.ToString();
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
            StatusText.Text = isStarted ? "실행 중" : "종료됨";
        }

        private void ShellCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingShellSelection || ActiveTab == null || ActiveTab.IsAddTab)
                return;

            var active = ActiveTab;
            var shellKind = GetSelectedShellKind();
            if (active.ShellKind == shellKind)
                return;

            var result = MessageBox.Show(
                "셸을 변경하면 현재 콘솔 세션이 재시작됩니다.",
                "콘솔 셸 변경",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.OK)
            {
                SelectConfiguredShell(active.ShellKind);
                return;
            }

            StartSelectedShell(active, GetActiveDirectory());
        }

        private void SetCurrentFolder_Click(object sender, RoutedEventArgs e)
        {
            var active = ActiveTab;
            if (active == null || active.IsAddTab) return;

            EnsureStarted();
            var directory = GetActiveDirectory();
            var shellKind = GetSelectedShellKind();
            if (shellKind == ConsoleShellKind.CommandPrompt)
            {
                active.Terminal.ConPTYTerm?.WriteToTerm($"cd /d \"{directory}\"\r\n");
            }
            else
            {
                active.Terminal.ConPTYTerm?.WriteToTerm($"Set-Location -LiteralPath '{directory.Replace("'", "''")}'\r\n");
            }
            UpdateStatus();
            FocusCommandBox();
        }

        private void ExternalTerminal_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ConsoleSessionService.LaunchExternalTerminal(
                    GetSelectedShellKind(),
                    GetActiveDirectory(),
                    null);
            }
            catch (Exception ex)
            {
                MessageBox.Show("외부 터미널을 열 수 없습니다: " + ex.Message, "에러", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            FocusCommandBox();
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            var active = ActiveTab;
            if (active == null || active.IsAddTab) return;

            var shellKind = GetSelectedShellKind();
            if (shellKind == ConsoleShellKind.CommandPrompt)
            {
                active.Terminal.ConPTYTerm?.WriteToTerm("cls\r\n");
            }
            else
            {
                active.Terminal.ConPTYTerm?.WriteToTerm("Clear-Host\r\n");
            }
            FocusCommandBox();
        }

        private void Restart_Click(object sender, RoutedEventArgs e)
        {
            var active = ActiveTab;
            if (active != null && !active.IsAddTab)
            {
                StartSelectedShell(active, GetActiveDirectory());
            }
            FocusCommandBox();
        }

        private void ConsolePanel_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                FocusCommandBox();
        }

        private void ConsoleTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var active = ActiveTab;
            if (active == null) return;

            if (active.IsAddTab)
            {
                // '+ 새 콘솔' 탭이 활성화된 경우, 즉시 새 실제 탭을 기동하고 탈출합니다.
                var configured = ConsoleSessionService.ParseShellKind(_settings.PreferredShellKind);
                AddConsoleTab(configured, GetActiveDirectory());
                return;
            }

            SelectConfiguredShell(active.ShellKind);
            UpdateStatus();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                FocusCommandBox();
            }), System.Windows.Threading.DispatcherPriority.Input);
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

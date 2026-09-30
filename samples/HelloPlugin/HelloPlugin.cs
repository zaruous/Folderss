using Folderss.Plugins;
using System.Windows;
using System.Windows.Controls;

namespace HelloPlugin
{
    /// <summary>팝업에 폴더 패널을 띄우고, 시작 폴더를 설정 탭에서 바꾸는 예제.</summary>
    public sealed class HelloPlugin : IFolderssPlugin
    {
        private const string StartPathKey = "startPath";
        private IPluginManager _manager;

        public void Initialize(IPluginManager manager)
        {
            _manager = manager;
            manager.AddSettingsPage(new SettingsPage(manager));
        }

        public FrameworkElement CreateView()
        {
            var panel = _manager.CreateFolderPanel(_manager.GetSetting(StartPathKey));
            var header = new TextBlock { Margin = new Thickness(8), Text = panel.CurrentPath };
            panel.PathChanged += (sender, args) => header.Text = panel.CurrentPath;

            // 본체 설정(읽기 전용) 예
            string theme;
            _manager.GetAppSettings().TryGetValue("theme", out theme);
            var info = new TextBlock { Margin = new Thickness(8, 0, 8, 8), Text = "본체 테마: " + theme };

            // 종료 감지 확인용: 누르면 본체가 끝나고 plugin-log.txt에 기록된다 (다음 시작 때 알림).
            var tests = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 8, 8) };
            var shutdown = new Button { Content = "테스트: Application.Shutdown", Margin = new Thickness(0, 0, 6, 0) };
            shutdown.Click += (sender, args) => Application.Current.Shutdown();
            var exit = new Button { Content = "테스트: Environment.Exit" };
            exit.Click += (sender, args) => System.Environment.Exit(3);
            tests.Children.Add(shutdown);
            tests.Children.Add(exit);

            var root = new DockPanel();
            DockPanel.SetDock(header, Dock.Top);
            DockPanel.SetDock(info, Dock.Top);
            DockPanel.SetDock(tests, Dock.Top);
            root.Children.Add(header);
            root.Children.Add(info);
            root.Children.Add(tests);
            root.Children.Add(panel.View);
            return root;
        }

        private sealed class SettingsPage : IPluginSettingsPage
        {
            private readonly IPluginManager _manager;
            private TextBox _startPath;

            public SettingsPage(IPluginManager manager)
            {
                _manager = manager;
            }

            public string Title { get { return "Hello 플러그인"; } }

            public FrameworkElement CreateView()
            {
                _startPath = new TextBox { Text = _manager.GetSetting(StartPathKey) ?? string.Empty };
                var root = new StackPanel();
                root.Children.Add(new TextBlock { Text = "시작 폴더 (비우면 사용자 폴더)", Margin = new Thickness(0, 0, 0, 4) });
                root.Children.Add(_startPath);
                return root;
            }

            public void Save()
            {
                var value = _startPath.Text.Trim();
                _manager.SetSetting(StartPathKey, value.Length == 0 ? null : value);
            }
        }
    }
}

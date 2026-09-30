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

            var root = new DockPanel();
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);
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

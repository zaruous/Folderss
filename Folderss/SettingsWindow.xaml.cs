using Folderss.Models;
using Folderss.Services;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Folderss
{
    public class ViewerMappingItem : System.ComponentModel.INotifyPropertyChanged
    {
        private string _viewerKey;
        public string Extension { get; set; }
        public string ViewerKey
        {
            get { return _viewerKey; }
            set { _viewerKey = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ViewerKey))); }
        }
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }

    public partial class SettingsWindow : Window
    {
        private readonly KeyBindingService _service;
        private readonly ViewerConfigService _viewerConfig;
        private readonly ObservableCollection<KeyBindingEntry> _workingBindings;
        private readonly ObservableCollection<ViewerMappingItem> _workingMappings;
        private bool _initializingTheme;
        private readonly AppTheme _originalTheme;

        public SettingsWindow(KeyBindingService service) : this(service, new ViewerConfigService()) { }

        public SettingsWindow(KeyBindingService service, ViewerConfigService viewerConfig)
        {
            _service = service;
            _viewerConfig = viewerConfig;
            _workingBindings = new ObservableCollection<KeyBindingEntry>(
                service.Bindings.Select(b => b.Clone()));

            _workingMappings = new ObservableCollection<ViewerMappingItem>(
                viewerConfig.GetAllMappings()
                    .OrderBy(kv => kv.Key)
                    .Select(kv => new ViewerMappingItem { Extension = kv.Key, ViewerKey = kv.Value }));

            _originalTheme = ThemeManager.CurrentTheme;

            InitializeComponent();

            ShortcutList.ItemsSource = _workingBindings;
            ViewerMappingList.ItemsSource = _workingMappings;

            _initializingTheme = true;
            BlackThemeRadio.IsChecked      = ThemeManager.CurrentTheme == AppTheme.Black;
            LightThemeRadio.IsChecked      = ThemeManager.CurrentTheme == AppTheme.Light;
            NordThemeRadio.IsChecked       = ThemeManager.CurrentTheme == AppTheme.Nord;
            CatppuccinThemeRadio.IsChecked = ThemeManager.CurrentTheme == AppTheme.Catppuccin;
            SolarizedThemeRadio.IsChecked  = ThemeManager.CurrentTheme == AppTheme.Solarized;
            DraculaThemeRadio.IsChecked    = ThemeManager.CurrentTheme == AppTheme.Dracula;
            GitHubThemeRadio.IsChecked     = ThemeManager.CurrentTheme == AppTheme.GitHub;
            _initializingTheme = false;

            TabNav.SelectedIndex = 0;
        }

        private void TabNav_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ShortcutsPanel == null) return;

            var item = TabNav.SelectedItem as System.Windows.Controls.ListBoxItem;
            var tag = item?.Tag?.ToString();

            ShortcutsPanel.Visibility = tag == "Shortcuts" ? Visibility.Visible : Visibility.Collapsed;
            ThemePanel.Visibility     = tag == "Theme"     ? Visibility.Visible : Visibility.Collapsed;
            ViewersPanel.Visibility   = tag == "Viewers"   ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AddViewer_Click(object sender, RoutedEventArgs e)
        {
            var ext = NewExtBox.Text.Trim();
            if (!ext.StartsWith(".")) ext = "." + ext;
            var viewerItem = NewViewerCombo.SelectedItem as ComboBoxItem;
            var viewerKey = viewerItem?.Content?.ToString();
            if (string.IsNullOrWhiteSpace(ext) || string.IsNullOrWhiteSpace(viewerKey)) return;

            var existing = _workingMappings.FirstOrDefault(m =>
                string.Equals(m.Extension, ext, System.StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                existing.ViewerKey = viewerKey;
            else
                _workingMappings.Add(new ViewerMappingItem { Extension = ext, ViewerKey = viewerKey });

            NewExtBox.Text = ".ext";
        }

        private void RemoveViewer_Click(object sender, RoutedEventArgs e)
        {
            var ext = (string)((FrameworkElement)sender).Tag;
            var item = _workingMappings.FirstOrDefault(m =>
                string.Equals(m.Extension, ext, System.StringComparison.OrdinalIgnoreCase));
            if (item != null)
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

            _service.Save(_workingBindings);

            // Save viewer mappings
            var currentMappings = _viewerConfig.GetAllMappings();
            foreach (var key in currentMappings.Keys.ToList())
                _viewerConfig.RemoveMapping(key);
            foreach (var item in _workingMappings)
                _viewerConfig.SetMapping(item.Extension, item.ViewerKey);

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (ThemeManager.CurrentTheme != _originalTheme)
                ThemeManager.ApplyTheme(_originalTheme);
            Close();
        }
    }
}

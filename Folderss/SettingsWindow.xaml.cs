using Folderss.Models;
using Folderss.Services;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

    public class ViewerOption
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
        private string _editingOpenWithId;
        private bool _initializingTheme;
        private readonly AppTheme _originalTheme;
        public IReadOnlyList<ViewerOption> ViewerOptions { get; }

        public SettingsWindow(KeyBindingService service) : this(service, new ViewerConfigService()) { }

        public SettingsWindow(KeyBindingService service, ViewerConfigService viewerConfig)
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

            _originalTheme = ThemeManager.CurrentTheme;

            InitializeComponent();
            DataContext = this;

            ShortcutList.ItemsSource = _workingBindings;
            ViewerMappingList.ItemsSource = _workingMappings;
            OpenWithList.ItemsSource = _workingOpenWith;
            NewViewerCombo.SelectedValue = ViewerConfigService.SystemDefaultKey;
            ConsoleMaxLinesBox.Text = _workingConsoleSettings.MaxOutputLineCount.ToString("N0");
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
            OpenWithPanel.Visibility  = tag == "OpenWith"  ? Visibility.Visible : Visibility.Collapsed;
            ConsolePanel.Visibility   = tag == "Console"   ? Visibility.Visible : Visibility.Collapsed;
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

            int maxOutputLineCount;
            var maxOutputLineText = ConsoleMaxLinesBox.Text.Replace(",", "").Trim();
            if (!int.TryParse(maxOutputLineText, out maxOutputLineCount) ||
                maxOutputLineCount < ConsoleSettingsService.MinOutputLineCount ||
                maxOutputLineCount > ConsoleSettingsService.MaxOutputLineCount)
            {
                MessageBox.Show(
                    string.Format(
                        "최대 출력 줄 수는 {0:N0}에서 {1:N0} 사이의 숫자로 입력하세요.",
                        ConsoleSettingsService.MinOutputLineCount,
                        ConsoleSettingsService.MaxOutputLineCount),
                    "콘솔 설정 오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                TabNav.SelectedIndex = 4;
                ConsoleMaxLinesBox.Focus();
                ConsoleMaxLinesBox.SelectAll();
                return;
            }
            _workingConsoleSettings.MaxOutputLineCount = maxOutputLineCount;

            _service.Save(_workingBindings);

            // Save viewer mappings
            var currentMappings = _viewerConfig.GetAllMappings();
            foreach (var key in currentMappings.Keys.ToList())
                _viewerConfig.RemoveMapping(key);
            foreach (var item in _workingMappings)
                _viewerConfig.SetMapping(item.Extension, item.ViewerKey);

            // Save open-with entries
            OpenWithService.Save(_workingOpenWith);

            // Save console settings
            ConsoleSettingsService.Save(_workingConsoleSettings);

            DialogResult = true;
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

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (ThemeManager.CurrentTheme != _originalTheme)
                ThemeManager.ApplyTheme(_originalTheme);
            Close();
        }
    }
}

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
            _workingConsoleProfiles = new ObservableCollection<ConsoleCommandProfile>(
                _workingConsoleSettings.CustomProfiles.Select(profile => profile.Clone()));
            _consoleProfileOptions = new ObservableCollection<ConsoleProfileOption>();

            _originalTheme = ThemeManager.CurrentTheme;

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

            _workingConsoleSettings.FontSize = fontSize;
            _workingConsoleSettings.PreferredProfileKey = preferredProfileKey;
            _workingConsoleSettings.CustomProfiles = _workingConsoleProfiles
                .Select(profile => profile.Clone())
                .ToList();

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

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (ThemeManager.CurrentTheme != _originalTheme)
                ThemeManager.ApplyTheme(_originalTheme);
            Close();
        }
    }
}

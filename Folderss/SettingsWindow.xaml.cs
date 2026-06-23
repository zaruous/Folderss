using Folderss.Models;
using Folderss.Services;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Folderss
{
    public partial class SettingsWindow : Window
    {
        private readonly KeyBindingService _service;
        private readonly ObservableCollection<KeyBindingEntry> _workingBindings;
        private bool _initializingTheme;
        private readonly AppTheme _originalTheme;

        public SettingsWindow(KeyBindingService service)
        {
            _service = service;
            _workingBindings = new ObservableCollection<KeyBindingEntry>(
                service.Bindings.Select(b => b.Clone()));

            _originalTheme = ThemeManager.CurrentTheme;

            InitializeComponent();

            ShortcutList.ItemsSource = _workingBindings;

            _initializingTheme = true;
            BlackThemeRadio.IsChecked = ThemeManager.CurrentTheme == AppTheme.Black;
            LightThemeRadio.IsChecked = ThemeManager.CurrentTheme == AppTheme.Light;
            _initializingTheme = false;

            TabNav.SelectedIndex = 0;
        }

        private void TabNav_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ShortcutsPanel == null) return;

            var item = TabNav.SelectedItem as System.Windows.Controls.ListBoxItem;
            var tag = item?.Tag?.ToString();

            ShortcutsPanel.Visibility = tag == "Shortcuts" ? Visibility.Visible : Visibility.Collapsed;
            ThemePanel.Visibility = tag == "Theme" ? Visibility.Visible : Visibility.Collapsed;
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

        private void ChangeBinding_Click(object sender, RoutedEventArgs e)
        {
            var entry = (KeyBindingEntry)((FrameworkElement)sender).DataContext;
            var capture = new KeyCaptureWindow { Owner = this };
            if (capture.ShowDialog() == true)
            {
                entry.Key = capture.CapturedKey;
                entry.Modifiers = capture.CapturedModifiers;
            }
        }

        private void ResetDefaults_Click(object sender, RoutedEventArgs e)
        {
            var defaults = KeyBindingService.GetDefaults();
            _workingBindings.Clear();
            foreach (var d in defaults)
                _workingBindings.Add(d);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            _service.Save(_workingBindings);
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

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

        public SettingsWindow(KeyBindingService service)
        {
            _service = service;
            _workingBindings = new ObservableCollection<KeyBindingEntry>(
                service.Bindings.Select(b => b.Clone()));

            InitializeComponent();

            ShortcutList.ItemsSource = _workingBindings;
            TabNav.SelectedIndex = 0;
        }

        private void TabNav_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Only one tab for now; add Visibility switching here when more tabs are added.
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
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

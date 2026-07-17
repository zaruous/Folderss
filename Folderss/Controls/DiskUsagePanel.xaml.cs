using Folderss.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace Folderss.Controls
{
    public partial class DiskUsagePanel : UserControl
    {
        private readonly ObservableCollection<Models.DriveUsageInfo> _drives;

        public DiskUsagePanel()
        {
            InitializeComponent();
            _drives = new ObservableCollection<Models.DriveUsageInfo>();
            DriveList.ItemsSource = _drives;
            Refresh();
        }

        public void Refresh()
        {
            _drives.Clear();
            foreach (var drive in DiskUsageService.GetDriveUsage())
                _drives.Add(drive);
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            Refresh();
        }
    }
}

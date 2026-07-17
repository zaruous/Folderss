using Folderss.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace Folderss.Controls
{
    /// <summary>
    /// 즐겨찾기 열 상단에 도킹되는 컴팩트한 디스크 사용량 뷰.
    /// 상세 화면은 <see cref="DiskUsagePanel"/>(문서 탭) 참고.
    /// </summary>
    public partial class DiskUsageMiniPanel : UserControl
    {
        private readonly ObservableCollection<Models.DriveUsageInfo> _drives;

        public DiskUsageMiniPanel()
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

        private void UserControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue)
                Refresh();
        }
    }
}

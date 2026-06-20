using Folderss.Models;
using Folderss.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;

namespace Folderss.Controls
{
    public partial class FolderBrowser : UserControl
    {
        private const int HistoryLimit = 10;
        private readonly ObservableCollection<FileSystemItem> _items = new ObservableCollection<FileSystemItem>();
        private readonly Stack<string> _backHistory = new Stack<string>();
        private readonly Stack<string> _forwardHistory = new Stack<string>();
        private int _previewRequestId;

        public event EventHandler Activated;
        public event EventHandler PathChanged;

        public string CurrentPath { get; private set; }
        public bool IsActive { get; private set; }

        public FileSystemItem SelectedItem
        {
            get { return FileList.SelectedItem as FileSystemItem; }
        }

        public IList<FileSystemItem> SelectedItems
        {
            get { return FileList.SelectedItems.Cast<FileSystemItem>().ToList(); }
        }

        public FolderBrowser()
        {
            InitializeComponent();
            FileList.ItemsSource = _items;
        }

        public void Initialize(string initialPath)
        {
            NavigateTo(initialPath, false);
        }

        public void SetActive(bool active)
        {
            IsActive = active;
            PaneBorder.SetResourceReference(
                Border.BorderBrushProperty,
                active ? "AccentBrush" : "BorderBrush");
            PaneBorder.BorderThickness = new Thickness(1);
        }

        public void RefreshItems()
        {
            if (string.IsNullOrWhiteSpace(CurrentPath))
                return;

            try
            {
                var entries = new List<FileSystemItem>();
                var directory = new DirectoryInfo(CurrentPath);

                foreach (var childDirectory in directory.EnumerateDirectories())
                {
                    entries.Add(new FileSystemItem
                    {
                        Name = childDirectory.Name,
                        FullPath = childDirectory.FullName,
                        IsDirectory = true,
                        ModifiedAt = childDirectory.LastWriteTime
                    });
                }

                foreach (var file in directory.EnumerateFiles())
                {
                    entries.Add(new FileSystemItem
                    {
                        Name = file.Name,
                        FullPath = file.FullName,
                        IsDirectory = false,
                        Size = file.Length,
                        ModifiedAt = file.LastWriteTime
                    });
                }

                _items.Clear();
                foreach (var item in entries.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
                    _items.Add(item);

                ApplyFilter();
                StatusText.Text = string.Format("{0:N0}개 항목", _items.Count);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("이 폴더에 접근할 권한이 없습니다.", "Folderss", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "폴더를 열 수 없습니다", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void NavigateTo(string path, bool addHistory = true)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "잘못된 경로", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!Directory.Exists(fullPath))
            {
                MessageBox.Show("폴더가 존재하지 않습니다.", "Folderss", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (addHistory && !string.IsNullOrWhiteSpace(CurrentPath) &&
                !string.Equals(CurrentPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                PushHistory(_backHistory, CurrentPath);
                _forwardHistory.Clear();
            }

            CurrentPath = fullPath;
            PathBox.Text = fullPath;
            SearchBox.Text = string.Empty;
            RefreshItems();

            var handler = PathChanged;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        private void ApplyFilter()
        {
            var view = CollectionViewSource.GetDefaultView(FileList.ItemsSource);
            var query = SearchBox.Text.Trim();
            view.Filter = item =>
            {
                var fileItem = item as FileSystemItem;
                return fileItem != null &&
                       (query.Length == 0 || fileItem.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0);
            };
            view.Refresh();
            FilteredCountText.Text = query.Length == 0
                ? string.Format("{0:N0}개", _items.Count)
                : string.Format("{0:N0}/{1:N0}개", view.Cast<object>().Count(), _items.Count);
        }

        private void Activate()
        {
            var handler = Activated;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = SelectedItem;
            if (item == null)
                return;

            if (item.IsDirectory)
            {
                NavigateTo(item.FullPath);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true });
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "파일을 열 수 없습니다", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var requestId = ++_previewRequestId;
            ResetPreview();

            var selected = SelectedItems;
            if (selected.Count == 0)
            {
                PreviewSummaryText.Text = string.Empty;
                return;
            }

            if (selected.Count > 1)
            {
                PreviewEmptyText.Text = string.Format("{0}개 항목이 선택되었습니다.", selected.Count);
                PreviewSummaryText.Text = string.Format("{0}개 선택", selected.Count);
                MetadataName.Text = string.Format("{0}개 항목", selected.Count);
                MetadataSize.Text = string.Format("{0:0.00} MB", selected.Where(entry => !entry.IsDirectory).Sum(entry => entry.Size) / 1024d / 1024d);
                return;
            }

            var item = selected[0];
            PreviewSummaryText.Text = item.IsDirectory
                ? item.Name
                : string.Format("{0}  /  {1:0.00} MB", item.Name, item.Size / 1024d / 1024d);

            try
            {
                var metadata = await Task.Run(() => FilePreviewService.ReadMetadata(item.FullPath));
                if (requestId != _previewRequestId)
                    return;

                ApplyMetadata(metadata);

                if (item.IsDirectory)
                {
                    PreviewEmptyText.Text = "폴더는 내용 미리보기를 지원하지 않습니다.";
                    return;
                }

                if (FilePreviewService.IsImage(item.FullPath))
                {
                    ShowImagePreview(item.FullPath);
                    return;
                }

                var text = await Task.Run(() =>
                {
                    bool truncated;
                    return FilePreviewService.ReadTextPreview(item.FullPath, out truncated);
                });
                if (requestId != _previewRequestId)
                    return;

                PreviewEmptyText.Visibility = Visibility.Collapsed;
                TextPreview.Text = text;
                TextPreviewScroll.Visibility = Visibility.Visible;
            }
            catch (UnauthorizedAccessException)
            {
                ShowPreviewMessage("이 파일을 읽을 권한이 없습니다.");
            }
            catch (IOException exception)
            {
                ShowPreviewMessage("파일을 읽을 수 없습니다.\n" + exception.Message);
            }
            catch (Exception exception)
            {
                ShowPreviewMessage("미리보기를 표시할 수 없습니다.\n" + exception.Message);
            }
        }

        private void ShowImagePreview(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 1600;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();

                ImagePreview.Source = bitmap;
            }

            PreviewEmptyText.Visibility = Visibility.Collapsed;
            ImagePreviewContainer.Visibility = Visibility.Visible;
        }

        private void ApplyMetadata(FileMetadata metadata)
        {
            MetadataName.Text = metadata.Name;
            MetadataType.Text = metadata.Type;
            MetadataSize.Text = metadata.SizeText;
            MetadataCreated.Text = metadata.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
            MetadataModified.Text = metadata.ModifiedAt.ToString("yyyy-MM-dd HH:mm:ss");
            MetadataPermissions.Text = metadata.Permissions;
            MetadataAttributes.Text = metadata.Attributes;
        }

        private void ResetPreview()
        {
            PreviewEmptyText.Text = "파일을 선택하면 내용을 미리 볼 수 있습니다.";
            PreviewEmptyText.Visibility = Visibility.Visible;
            TextPreviewScroll.Visibility = Visibility.Collapsed;
            TextPreview.Text = string.Empty;
            ImagePreviewContainer.Visibility = Visibility.Collapsed;
            ImagePreview.Source = null;
            MetadataName.Text = string.Empty;
            MetadataType.Text = string.Empty;
            MetadataSize.Text = string.Empty;
            MetadataCreated.Text = string.Empty;
            MetadataModified.Text = string.Empty;
            MetadataPermissions.Text = string.Empty;
            MetadataAttributes.Text = string.Empty;
        }

        private void ShowPreviewMessage(string message)
        {
            PreviewEmptyText.Text = message;
            PreviewEmptyText.Visibility = Visibility.Visible;
            TextPreviewScroll.Visibility = Visibility.Collapsed;
            ImagePreviewContainer.Visibility = Visibility.Collapsed;
        }

        private void FileList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            Activate();

            var container = FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject);
            if (container == null)
                return;

            if (!container.IsSelected)
            {
                FileList.SelectedItems.Clear();
                container.IsSelected = true;
            }

            container.Focus();
        }

        private void FileList_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var selectedPaths = SelectedItems.Select(item => item.FullPath).ToList();
            if (selectedPaths.Count == 0)
                return;

            var window = Window.GetWindow(this);
            if (window == null)
                return;

            var screenPoint = PointToScreen(e.GetPosition(this));
            try
            {
                ShellContextMenuService.Show(
                    new WindowInteropHelper(window).Handle,
                    selectedPaths,
                    (int)screenPoint.X,
                    (int)screenPoint.Y);
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "컨텍스트 메뉴를 열 수 없습니다", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                RefreshItems();
            }

            e.Handled = true;
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                var match = current as T;
                if (match != null)
                    return match;

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (_backHistory.Count == 0)
                return;

            _forwardHistory.Push(CurrentPath);
            TrimHistory(_forwardHistory);
            NavigateTo(_backHistory.Pop(), false);
        }

        private void Forward_Click(object sender, RoutedEventArgs e)
        {
            if (_forwardHistory.Count == 0)
                return;

            PushHistory(_backHistory, CurrentPath);
            NavigateTo(_forwardHistory.Pop(), false);
        }

        private void Up_Click(object sender, RoutedEventArgs e)
        {
            var parent = Directory.GetParent(CurrentPath);
            if (parent != null)
                NavigateTo(parent.FullName);
        }

        private void PathBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                NavigateTo(PathBox.Text);
                FileList.Focus();
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SearchHintText != null)
                SearchHintText.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            if (ClearSearchButton != null)
                ClearSearchButton.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Collapsed : Visibility.Visible;

            if (FileList != null && FileList.ItemsSource != null)
                ApplyFilter();
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Clear();
            SearchBox.Focus();
        }

        private void Pane_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            Activate();
        }

        private void Pane_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Activate();
        }

        private static void PushHistory(Stack<string> history, string path)
        {
            history.Push(path);
            TrimHistory(history);
        }

        private static void TrimHistory(Stack<string> history)
        {
            if (history.Count <= HistoryLimit)
                return;

            var newestFirst = history.ToArray();
            history.Clear();
            for (var index = Math.Min(HistoryLimit, newestFirst.Length) - 1; index >= 0; index--)
                history.Push(newestFirst[index]);
        }

    }
}

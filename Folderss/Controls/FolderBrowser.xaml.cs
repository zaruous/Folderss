using Folderss.Models;
using Folderss.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
using System.Windows.Threading;

namespace Folderss.Controls
{
    public partial class FolderBrowser : UserControl
    {
        private const int HistoryLimit = 10;
        private readonly ObservableCollection<FileSystemItem> _items = new ObservableCollection<FileSystemItem>();
        private readonly Stack<string> _backHistory = new Stack<string>();
        private readonly Stack<string> _forwardHistory = new Stack<string>();
        private int _previewRequestId;
        private bool _showHidden;
        private string _sortProperty = "Name";
        private bool _sortAscending = true;
        private bool _updatingDriveCombo;
        private FileSystemWatcher _watcher;
        private DispatcherTimer _refreshDebounce;
        private Point _dragStartPoint;
        private bool _dragPending;

        public event EventHandler Activated;
        public event EventHandler PathChanged;
        public event EventHandler<string> FileOpenRequested;

        public string CurrentPath { get; private set; }
        public bool IsActive { get; private set; }
        public HashSet<string> CutPaths { get; set; }

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
            FileList.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(ColumnHeader_Click));
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, Copy_Executed, Copy_CanExecute));
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Cut, Cut_Executed, Cut_CanExecute));
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, Paste_Executed, Paste_CanExecute));
            PopulateDrives();
            var view = CollectionViewSource.GetDefaultView(_items);
            view.SortDescriptions.Add(new SortDescription("IsDirectory", ListSortDirection.Descending));
            view.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
        }

        private void FolderBrowser_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateSortHeaders();
        }

        private void FolderBrowser_Unloaded(object sender, RoutedEventArgs e)
        {
            StopWatcher();
            _refreshDebounce?.Stop();
        }

        private void StartWatcher(string path)
        {
            StopWatcher();
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return;

            try
            {
                _watcher = new FileSystemWatcher(path)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    EnableRaisingEvents = true
                };
                _watcher.Created += OnFileSystemChanged;
                _watcher.Deleted += OnFileSystemChanged;
                _watcher.Renamed += OnFileSystemChanged;
                _watcher.Error += OnWatcherError;
            }
            catch
            {
                _watcher = null;
            }
        }

        private void StopWatcher()
        {
            if (_watcher == null)
                return;
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

        private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(ScheduleRefresh), DispatcherPriority.Background);
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(StopWatcher), DispatcherPriority.Background);
        }

        private void ScheduleRefresh()
        {
            if (_refreshDebounce == null)
            {
                _refreshDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                _refreshDebounce.Tick += (s, e) =>
                {
                    _refreshDebounce.Stop();
                    RefreshItems();
                };
            }
            _refreshDebounce.Stop();
            _refreshDebounce.Start();
        }

        private void PopulateDrives()
        {
            foreach (var drive in DriveInfo.GetDrives())
                DriveComboBox.Items.Add(drive.RootDirectory.FullName);
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
                    if (!_showHidden && (childDirectory.Attributes & FileAttributes.Hidden) != 0)
                        continue;
                    entries.Add(new FileSystemItem
                    {
                        Name = childDirectory.Name,
                        FullPath = childDirectory.FullName,
                        IsDirectory = true,
                        ModifiedAt = childDirectory.LastWriteTime,
                        IsCut = CutPaths != null && CutPaths.Contains(childDirectory.FullName)
                    });
                }

                foreach (var file in directory.EnumerateFiles())
                {
                    if (!_showHidden && (file.Attributes & FileAttributes.Hidden) != 0)
                        continue;
                    entries.Add(new FileSystemItem
                    {
                        Name = file.Name,
                        FullPath = file.FullName,
                        IsDirectory = false,
                        Size = file.Length,
                        ModifiedAt = file.LastWriteTime,
                        IsCut = CutPaths != null && CutPaths.Contains(file.FullName)
                    });
                }

                _items.Clear();
                foreach (var item in entries)
                    _items.Add(item);

                ApplyFilter();
                UpdateStatusText();
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
            SyncDriveComboBox();
            RefreshItems();
            StartWatcher(fullPath);

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

        private void UpdateStatusText()
        {
            StatusText.Text = string.Format("{0:N0}개 항목", _items.Count);
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

            var handler = FileOpenRequested;
            if (handler != null)
            {
                handler(this, item.FullPath);
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

        private void FileList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                FileList_MouseDoubleClick(sender, null);
                e.Handled = true;
            }
            else if (e.Key == Key.Back)
            {
                NavigateUp();
                e.Handled = true;
            }
        }

        private void FileList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(FileList);
            var container = FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject);
            _dragPending = container != null;
            if (container != null && !container.IsSelected)
            {
                FileList.SelectedItems.Clear();
                container.IsSelected = true;
            }
        }

        private void FileList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragPending || e.LeftButton != MouseButtonState.Pressed)
                return;

            var currentPoint = e.GetPosition(FileList);
            if (Math.Abs(currentPoint.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(currentPoint.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            _dragPending = false;
            var paths = SelectedItems
                .Select(item => item.FullPath)
                .Where(path => File.Exists(path) || Directory.Exists(path))
                .ToArray();
            if (paths.Length == 0)
                return;

            var data = new DataObject(DataFormats.FileDrop, paths);
            DragDrop.DoDragDrop(FileList, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }

        private void FileList_DragOver(object sender, DragEventArgs e)
        {
            var paths = GetDroppedPaths(e.Data);
            var destination = GetDropDestination(e.OriginalSource as DependencyObject);
            if (paths.Length == 0 || string.IsNullOrWhiteSpace(destination) || !Directory.Exists(destination))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.Effects = GetDropEffect(e, paths, destination);
            StatusText.Text = string.Format("{0}: {1}", GetDropEffectLabel(e.Effects), destination);
            e.Handled = true;
        }

        private void FileList_DragLeave(object sender, DragEventArgs e)
        {
            UpdateStatusText();
        }

        private void FileList_Drop(object sender, DragEventArgs e)
        {
            var paths = GetDroppedPaths(e.Data);
            var destination = GetDropDestination(e.OriginalSource as DependencyObject);
            var effect = GetDropEffect(e, paths, destination);
            e.Effects = effect;
            e.Handled = true;

            if (paths.Length == 0 || effect == DragDropEffects.None ||
                string.IsNullOrWhiteSpace(destination) || !Directory.Exists(destination))
            {
                UpdateStatusText();
                return;
            }

            if (effect == DragDropEffects.Copy)
            {
                var answer = MessageBox.Show(
                    string.Format(
                        "{0}개 항목을 다음 폴더로 복사하시겠습니까?\n\n{1}",
                        paths.Length,
                        destination),
                    "파일 복사 확인",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.Yes);
                if (answer != MessageBoxResult.Yes)
                {
                    e.Effects = DragDropEffects.None;
                    UpdateStatusText();
                    return;
                }
            }

            var errors = new List<string>();
            foreach (var source in paths)
            {
                try
                {
                    if (effect == DragDropEffects.Link)
                        FileOperationService.CreateShortcut(source, destination);
                    else if (effect == DragDropEffects.Move)
                        FileOperationService.Move(source, destination);
                    else
                        FileOperationService.Copy(source, destination);
                }
                catch (Exception exception)
                {
                    errors.Add(Path.GetFileName(source) + ": " + exception.Message);
                }
            }

            RefreshItems();
            if (errors.Count > 0)
            {
                MessageBox.Show(
                    string.Join(Environment.NewLine, errors),
                    GetDropEffectLabel(effect) + " 오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private string GetDropDestination(DependencyObject originalSource)
        {
            var container = FindAncestor<ListViewItem>(originalSource);
            var item = container == null ? null : container.DataContext as FileSystemItem;
            return item != null && item.IsDirectory ? item.FullPath : CurrentPath;
        }

        private static string[] GetDroppedPaths(IDataObject data)
        {
            if (data == null || !data.GetDataPresent(DataFormats.FileDrop))
                return new string[0];

            var paths = data.GetData(DataFormats.FileDrop) as string[];
            return paths == null
                ? new string[0]
                : paths.Where(path => File.Exists(path) || Directory.Exists(path)).ToArray();
        }

        private static DragDropEffects GetDropEffect(DragEventArgs e, IEnumerable<string> paths, string destination)
        {
            if (string.IsNullOrWhiteSpace(destination))
                return DragDropEffects.None;

            var allowed = e.AllowedEffects;
            var modifiers = Keyboard.Modifiers;
            if ((modifiers & ModifierKeys.Alt) != 0 && (allowed & DragDropEffects.Link) != 0)
                return DragDropEffects.Link;
            if ((modifiers & ModifierKeys.Control) != 0 && (allowed & DragDropEffects.Copy) != 0)
                return DragDropEffects.Copy;
            if ((modifiers & ModifierKeys.Shift) != 0 && (allowed & DragDropEffects.Move) != 0)
                return paths.All(path => !IsSameDirectory(Path.GetDirectoryName(path), destination))
                    ? DragDropEffects.Move
                    : DragDropEffects.None;

            if (paths.All(path => IsSameDirectory(Path.GetDirectoryName(path), destination)))
                return DragDropEffects.None;

            if ((allowed & DragDropEffects.Copy) != 0)
                return DragDropEffects.Copy;
            if ((allowed & DragDropEffects.Move) != 0)
                return DragDropEffects.Move;
            if ((allowed & DragDropEffects.Link) != 0)
                return DragDropEffects.Link;
            return DragDropEffects.None;
        }

        private static bool IsSameDirectory(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
                return false;

            return string.Equals(
                NormalizeDirectoryPath(first),
                NormalizeDirectoryPath(second),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeDirectoryPath(string path)
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
                ? root
                : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static string GetDropEffectLabel(DragDropEffects effect)
        {
            if (effect == DragDropEffects.Link)
                return "바로가기 만들기";
            if (effect == DragDropEffects.Move)
                return "이동";
            if (effect == DragDropEffects.Copy)
                return "복사";
            return "드롭할 수 없음";
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
            {
                FileList.SelectedItems.Clear();
                return;
            }

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
            {
                if (string.IsNullOrWhiteSpace(CurrentPath))
                    return;
                selectedPaths.Add(CurrentPath);
            }

            var window = Window.GetWindow(this);
            if (window == null)
                return;

            var matchingEntries = OpenWithService.GetMatchingEntries(selectedPaths);
            var customItems = matchingEntries
                .Select(entry =>
                {
                    var captured = entry;
                    var capturedPaths = selectedPaths.ToList();
                    return new ShellContextMenuService.CustomMenuItem
                    {
                        Label = "Open with " + entry.Name,
                        Invoke = () => OpenWithService.Launch(captured, capturedPaths)
                    };
                })
                .ToList();

            var screenPoint = PointToScreen(e.GetPosition(this));
            try
            {
                ShellContextMenuService.Show(
                    new WindowInteropHelper(window).Handle,
                    selectedPaths,
                    (int)screenPoint.X,
                    (int)screenPoint.Y,
                    customItems.Count > 0 ? customItems : null);
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

        public void NavigateUp()
        {
            var parent = Directory.GetParent(CurrentPath);
            if (parent != null)
                NavigateTo(parent.FullName);
        }

        public void NavigateBack()
        {
            if (_backHistory.Count == 0)
                return;
            _forwardHistory.Push(CurrentPath);
            TrimHistory(_forwardHistory);
            NavigateTo(_backHistory.Pop(), false);
        }

        public void NavigateForward()
        {
            if (_forwardHistory.Count == 0)
                return;
            PushHistory(_backHistory, CurrentPath);
            NavigateTo(_forwardHistory.Pop(), false);
        }

        public void SelectAndScrollTo(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;

            var folder = Path.GetDirectoryName(filePath);
            var fileName = Path.GetFileName(filePath);

            if (!string.IsNullOrWhiteSpace(folder) &&
                !string.Equals(CurrentPath, folder, StringComparison.OrdinalIgnoreCase))
            {
                NavigateTo(folder);
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                var item = _items.FirstOrDefault(i =>
                    string.Equals(i.Name, fileName, StringComparison.OrdinalIgnoreCase));
                if (item == null)
                    return;

                FileList.SelectedItem = item;
                FileList.ScrollIntoView(item);
                FileList.Focus();
            }), DispatcherPriority.Background);
        }

        public void FocusFileList()
        {
            FileList.Focus();
        }

        private void FocusFirstVisibleFileItem()
        {
            var view = CollectionViewSource.GetDefaultView(FileList.ItemsSource);
            var firstVisibleItem = view.Cast<object>().OfType<FileSystemItem>().FirstOrDefault();
            if (firstVisibleItem == null)
            {
                FileList.Focus();
                return;
            }

            FileList.SelectedItem = firstVisibleItem;
            FileList.ScrollIntoView(firstVisibleItem);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                FileList.UpdateLayout();
                var container = FileList.ItemContainerGenerator.ContainerFromItem(firstVisibleItem) as ListViewItem;
                if (container != null)
                {
                    container.Focus();
                    return;
                }

                FileList.Focus();
            }), DispatcherPriority.Input);
        }

        private void Copy_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = SelectedItems.Count > 0;
            e.Handled = true;
        }

        public void CopySelectedToClipboard()
        {
            var paths = SelectedItems
                .Select(item => item.FullPath)
                .Where(path => File.Exists(path) || Directory.Exists(path))
                .ToList();
            if (paths.Count == 0)
                return;

            var pathsCollection = new System.Collections.Specialized.StringCollection();
            foreach (var path in paths)
                pathsCollection.Add(path);

            Clipboard.SetFileDropList(pathsCollection);
            var window = Window.GetWindow(this) as Folderss.MainWindow;
            if (window != null)
                window.ClearCutStateFromClipboard();
        }

        private void Copy_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            CopySelectedToClipboard();
            e.Handled = true;
        }

        private void Cut_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = SelectedItems.Count > 0;
            e.Handled = true;
        }

        public void CutSelectedToClipboard()
        {
            var selected = SelectedItems
                .Where(item => File.Exists(item.FullPath) || Directory.Exists(item.FullPath))
                .ToList();
            if (selected.Count == 0)
                return;

            var pathsCollection = new System.Collections.Specialized.StringCollection();
            foreach (var item in selected)
                pathsCollection.Add(item.FullPath);

            Clipboard.SetFileDropList(pathsCollection);

            var window = Window.GetWindow(this) as Folderss.MainWindow;
            if (window != null)
                window.SetCutStateFromClipboard(selected.Select(item => item.FullPath));
        }

        private void Cut_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            CutSelectedToClipboard();
            e.Handled = true;
        }

        private void Paste_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = Clipboard.ContainsFileDropList();
            e.Handled = true;
        }

        private void Paste_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var window = Window.GetWindow(this) as MainWindow;
            if (window != null && window.TryPasteFromClipboardInto(this))
                e.Handled = true;
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

        private void FolderBrowser_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text))
                return;

            if (PathBox.IsKeyboardFocusWithin || SearchBox.IsKeyboardFocusWithin)
                return;

            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0)
                return;

            if (!e.Text.Any(ch => !char.IsControl(ch)))
                return;

            SearchBox.Focus();
            SearchBox.CaretIndex = SearchBox.Text.Length;
            SearchBox.AppendText(e.Text);
            SearchBox.CaretIndex = SearchBox.Text.Length;
            e.Handled = true;
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

        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Down)
                return;

            FocusFirstVisibleFileItem();
            e.Handled = true;
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Clear();
            SearchBox.Focus();
        }

        private void DriveComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingDriveCombo)
                return;
            var selected = DriveComboBox.SelectedItem as string;
            if (selected != null)
                NavigateTo(selected);
        }

        private void SyncDriveComboBox()
        {
            if (string.IsNullOrWhiteSpace(CurrentPath))
                return;
            var root = Path.GetPathRoot(CurrentPath);
            _updatingDriveCombo = true;
            DriveComboBox.SelectedItem = root;
            _updatingDriveCombo = false;
        }

        private void ToggleHidden_Changed(object sender, RoutedEventArgs e)
        {
            _showHidden = ShowHiddenButton.IsChecked == true;
            RefreshItems();
        }

        private void ColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            var header = e.OriginalSource as GridViewColumnHeader;
            if (header == null || header.Role == GridViewColumnHeaderRole.Padding || header.Tag == null)
                return;

            var property = header.Tag.ToString();
            if (_sortProperty == property)
                _sortAscending = !_sortAscending;
            else
            {
                _sortProperty = property;
                _sortAscending = true;
            }

            var view = CollectionViewSource.GetDefaultView(FileList.ItemsSource);
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription("IsDirectory", ListSortDirection.Descending));
            view.SortDescriptions.Add(new SortDescription(_sortProperty,
                _sortAscending ? ListSortDirection.Ascending : ListSortDirection.Descending));
            view.Refresh();
            UpdateSortHeaders();
        }

        private void UpdateSortHeaders()
        {
            var gridView = FileList.View as GridView;
            if (gridView == null) return;

            foreach (var column in gridView.Columns)
            {
                var colHeader = column.Header as GridViewColumnHeader;
                if (colHeader == null || colHeader.Tag == null) continue;

                var tag = colHeader.Tag.ToString();
                var label = GetBaseHeaderLabel(tag);
                colHeader.Content = tag == _sortProperty
                    ? label + (_sortAscending ? " ↑" : " ↓")
                    : label;
            }
        }

        private static string GetBaseHeaderLabel(string property)
        {
            switch (property)
            {
                case "Name": return "이름";
                case "ModifiedAt": return "수정한 날짜";
                case "Kind": return "유형";
                case "Size": return "크기";
                default: return property;
            }
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

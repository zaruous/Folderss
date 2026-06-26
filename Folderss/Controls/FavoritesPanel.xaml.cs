using Folderss.Models;
using Folderss.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Folderss.Controls
{
    public partial class FavoritesPanel : UserControl
    {
        private readonly FavoritesConfiguration _configuration;
        private Point _dragStartPoint;
        private FavoriteLocation _draggedFavorite;

        public event EventHandler AddCurrentRequested;
        public event EventHandler<FavoriteNavigateEventArgs> NavigateRequested;

        public FavoritesPanel()
        {
            InitializeComponent();
            _configuration = FavoritesService.Load();
            FavoritesTree.ItemsSource = _configuration.Groups;
        }

        public bool CopySelectedFavoritePath()
        {
            var favorite = FavoritesTree.SelectedItem as FavoriteLocation;
            if (favorite == null)
                return false;

            Clipboard.SetText(favorite.Path);
            return true;
        }

        public bool AddFavorite(string path, string displayName = null)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return false;

            var group = GetSelectedGroup() ?? _configuration.Groups.First();
            if (group.Favorites.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(
                    string.Format("'{0}' 그룹에 이미 등록된 폴더입니다.", group.Name),
                    "Folderss",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return false;
            }

            var name = string.IsNullOrWhiteSpace(displayName) ? new DirectoryInfo(path).Name : displayName.Trim();
            if (string.IsNullOrWhiteSpace(name))
                name = path;

            var favorite = new FavoriteLocation { Name = name, Path = path };
            group.Favorites.Add(favorite);
            Save();
            SelectItem(favorite);
            return true;
        }

        private void AddGroup_Click(object sender, RoutedEventArgs e)
        {
            var prompt = new PromptWindow("즐겨찾기 그룹 추가", "새 그룹 이름을 입력하세요.")
            {
                Owner = Window.GetWindow(this)
            };
            if (prompt.ShowDialog() != true)
                return;

            var name = prompt.Value.Trim();
            if (string.IsNullOrWhiteSpace(name))
                return;

            if (_configuration.Groups.Any(group => string.Equals(group.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            {
                MessageBox.Show("같은 이름의 그룹이 이미 있습니다.", "Folderss", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var newGroup = new FavoriteGroup { Name = name };
            _configuration.Groups.Add(newGroup);
            Save();
            SelectItem(newGroup);
        }

        private void AddCurrent_Click(object sender, RoutedEventArgs e)
        {
            var handler = AddCurrentRequested;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        private void FavoritesTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var favorite = FavoritesTree.SelectedItem as FavoriteLocation;
            if (favorite == null)
                return;

            if (!Directory.Exists(favorite.Path))
            {
                MessageBox.Show("즐겨찾기 폴더가 존재하지 않습니다.", "Folderss", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var handler = NavigateRequested;
            if (handler != null)
                handler(this, new FavoriteNavigateEventArgs(favorite.Path));
        }

        private void FavoritesTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(FavoritesTree);
            var container = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
            _draggedFavorite = container == null ? null : container.DataContext as FavoriteLocation;
            if (container != null)
                container.Focus();
        }

        private void FavoritesTree_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _draggedFavorite == null)
                return;

            var currentPosition = e.GetPosition(FavoritesTree);
            if (Math.Abs(currentPosition.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(currentPosition.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            var favorite = _draggedFavorite;
            _draggedFavorite = null;
            DragDrop.DoDragDrop(FavoritesTree, favorite, DragDropEffects.Move);
        }

        private void FavoritesTree_DragOver(object sender, DragEventArgs e)
        {
            var favorite = e.Data.GetData(typeof(FavoriteLocation)) as FavoriteLocation;
            var targetGroup = GetDropTargetGroup(e.OriginalSource as DependencyObject);
            var sourceGroup = FindGroup(favorite);

            e.Effects = favorite != null && targetGroup != null && sourceGroup != null &&
                        !ReferenceEquals(sourceGroup, targetGroup)
                ? DragDropEffects.Move
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void FavoritesTree_Drop(object sender, DragEventArgs e)
        {
            var favorite = e.Data.GetData(typeof(FavoriteLocation)) as FavoriteLocation;
            var targetContainer = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
            var targetItem = targetContainer == null ? null : targetContainer.DataContext;
            var targetFavorite = targetItem as FavoriteLocation;
            var targetGroup = targetItem as FavoriteGroup ?? FindGroup(targetFavorite);
            var sourceGroup = FindGroup(favorite);

            if (favorite == null || targetGroup == null || sourceGroup == null ||
                ReferenceEquals(sourceGroup, targetGroup))
            {
                e.Handled = true;
                return;
            }

            if (targetGroup.Favorites.Any(item =>
                string.Equals(item.Path, favorite.Path, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(
                    string.Format("'{0}' 그룹에 같은 폴더가 이미 있습니다.", targetGroup.Name),
                    "Folderss",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                e.Handled = true;
                return;
            }

            sourceGroup.Favorites.Remove(favorite);
            var targetIndex = targetFavorite == null
                ? targetGroup.Favorites.Count
                : targetGroup.Favorites.IndexOf(targetFavorite);
            if (targetIndex < 0)
                targetIndex = targetGroup.Favorites.Count;

            targetGroup.Favorites.Insert(targetIndex, favorite);
            Save();
            SelectItem(favorite);
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void FavoritesTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var container = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
            if (container == null)
            {
                e.Handled = true;
                return;
            }

            container.IsSelected = true;
            container.Focus();
        }

        private FavoriteGroup GetDropTargetGroup(DependencyObject source)
        {
            var container = FindAncestor<TreeViewItem>(source);
            if (container == null)
                return null;

            var group = container.DataContext as FavoriteGroup;
            return group ?? FindGroup(container.DataContext as FavoriteLocation);
        }

        private void FavoritesTree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var favorite = FavoritesTree.SelectedItem as FavoriteLocation;
            var group = FavoritesTree.SelectedItem as FavoriteGroup;
            if (favorite == null && group == null)
            {
                e.Handled = true;
                return;
            }

            var isFavorite = favorite != null;
            OpenInExplorerMenuItem.Visibility = isFavorite ? Visibility.Visible : Visibility.Collapsed;
            OpenTerminalMenuItem.Visibility = isFavorite ? Visibility.Visible : Visibility.Collapsed;
            MoveToGroupMenuItem.Visibility = isFavorite ? Visibility.Visible : Visibility.Collapsed;
            FavoriteActionsSeparator.Visibility = isFavorite ? Visibility.Visible : Visibility.Collapsed;
            NewFolderMenuItem.Visibility = isFavorite ? Visibility.Visible : Visibility.Collapsed;
            NewFileMenuItem.Visibility = isFavorite ? Visibility.Visible : Visibility.Collapsed;
            NewItemSeparator.Visibility = isFavorite ? Visibility.Visible : Visibility.Collapsed;
            RemoveMenuItem.Header = isFavorite ? "즐겨찾기 삭제" : "그룹 삭제";
            MoveToGroupMenuItem.IsEnabled = isFavorite && _configuration.Groups.Count > 1;

            if (isFavorite)
            {
                var owner = FindGroup(favorite);
                var index = owner == null ? -1 : owner.Favorites.IndexOf(favorite);
                MoveUpMenuItem.IsEnabled = index > 0;
                MoveDownMenuItem.IsEnabled = owner != null && index >= 0 && index < owner.Favorites.Count - 1;
            }
            else
            {
                var index = _configuration.Groups.IndexOf(group);
                MoveUpMenuItem.IsEnabled = index > 0;
                MoveDownMenuItem.IsEnabled = index >= 0 && index < _configuration.Groups.Count - 1;
            }

            BuildMoveToGroupMenu(favorite);
        }

        private void NewFolder_Click(object sender, RoutedEventArgs e)
        {
            var favorite = FavoritesTree.SelectedItem as FavoriteLocation;
            if (favorite == null)
                return;

            if (!Directory.Exists(favorite.Path))
            {
                MessageBox.Show("즐겨찾기 폴더가 존재하지 않습니다.", "Folderss", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var prompt = new PromptWindow("새 폴더", "만들 폴더 이름을 입력하세요.", "새 폴더")
            {
                Owner = Window.GetWindow(this)
            };
            if (prompt.ShowDialog() != true)
                return;

            try
            {
                FileOperationService.CreateDirectory(favorite.Path, prompt.Value);
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "폴더를 만들 수 없습니다", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void NewFile_Click(object sender, RoutedEventArgs e)
        {
            var favorite = FavoritesTree.SelectedItem as FavoriteLocation;
            if (favorite == null)
                return;

            if (!Directory.Exists(favorite.Path))
            {
                MessageBox.Show("즐겨찾기 폴더가 존재하지 않습니다.", "Folderss", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var prompt = new PromptWindow("새 파일", "만들 파일 이름을 입력하세요.", "새 파일.txt")
            {
                Owner = Window.GetWindow(this)
            };
            if (prompt.ShowDialog() != true)
                return;

            try
            {
                FileOperationService.CreateFile(favorite.Path, prompt.Value);
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "파일을 만들 수 없습니다", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenInExplorer_Click(object sender, RoutedEventArgs e)
        {
            var favorite = FavoritesTree.SelectedItem as FavoriteLocation;
            if (favorite == null)
                return;

            if (!Directory.Exists(favorite.Path))
            {
                MessageBox.Show("즐겨찾기 폴더가 존재하지 않습니다.", "Folderss", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + favorite.Path + "\"")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "Explorer를 열 수 없습니다", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenPowerShell_Click(object sender, RoutedEventArgs e)
        {
            OpenTerminal("powershell.exe", "PowerShell");
        }

        private void OpenCmd_Click(object sender, RoutedEventArgs e)
        {
            OpenTerminal("cmd.exe", "CMD");
        }

        private void OpenTerminal(string executable, string displayName)
        {
            var favorite = FavoritesTree.SelectedItem as FavoriteLocation;
            if (favorite == null)
                return;

            if (!Directory.Exists(favorite.Path))
            {
                MessageBox.Show("즐겨찾기 폴더가 존재하지 않습니다.", "Folderss", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(executable)
                {
                    WorkingDirectory = favorite.Path,
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, displayName + "을 열 수 없습니다", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void FavoritesTree_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F2)
            {
                Rename_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.C &&
                (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                if (CopySelectedFavoritePath())
                {
                    e.Handled = true;
                }
            }
        }

        private void Rename_Click(object sender, RoutedEventArgs e)
        {
            var selected = FavoritesTree.SelectedItem;
            var favorite = selected as FavoriteLocation;
            var group = selected as FavoriteGroup;
            if (favorite == null && group == null)
                return;

            var currentName = favorite != null ? favorite.Name : group.Name;
            var prompt = new PromptWindow("이름 변경", "새 이름을 입력하세요.", currentName)
            {
                Owner = Window.GetWindow(this)
            };
            if (prompt.ShowDialog() != true)
                return;

            var name = prompt.Value.Trim();
            if (string.IsNullOrWhiteSpace(name))
                return;

            if (group != null &&
                _configuration.Groups.Any(item => !ReferenceEquals(item, group) &&
                    string.Equals(item.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            {
                MessageBox.Show("같은 이름의 그룹이 이미 있습니다.", "Folderss", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (favorite != null)
                favorite.Name = name;
            else
                group.Name = name;

            Save();
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            var favorite = FavoritesTree.SelectedItem as FavoriteLocation;
            var group = FavoritesTree.SelectedItem as FavoriteGroup;
            if (favorite == null && group == null)
                return;

            var message = favorite != null
                ? string.Format("'{0}' 즐겨찾기를 삭제하시겠습니까?", favorite.Name)
                : string.Format("'{0}' 그룹과 포함된 즐겨찾기 {1}개를 삭제하시겠습니까?", group.Name, group.Favorites.Count);
            var title = favorite != null ? "즐겨찾기 삭제" : "그룹 삭제";
            var answer = MessageBox.Show(
                message,
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
                return;

            if (favorite != null)
            {
                var owner = FindGroup(favorite);
                if (owner != null)
                    owner.Favorites.Remove(favorite);
            }
            else
            {
                _configuration.Groups.Remove(group);
                if (_configuration.Groups.Count == 0)
                    _configuration.Groups.Add(new FavoriteGroup { Name = "기본" });
            }

            Save();
        }

        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            MoveSelected(-1);
        }

        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            MoveSelected(1);
        }

        private void MoveSelected(int offset)
        {
            var favorite = FavoritesTree.SelectedItem as FavoriteLocation;
            if (favorite != null)
            {
                var group = FindGroup(favorite);
                if (group == null)
                    return;

                var oldFavoriteIndex = group.Favorites.IndexOf(favorite);
                var newFavoriteIndex = oldFavoriteIndex + offset;
                if (newFavoriteIndex < 0 || newFavoriteIndex >= group.Favorites.Count)
                    return;

                group.Favorites.Move(oldFavoriteIndex, newFavoriteIndex);
                Save();
                return;
            }

            var selectedGroup = FavoritesTree.SelectedItem as FavoriteGroup;
            if (selectedGroup == null)
                return;

            var oldIndex = _configuration.Groups.IndexOf(selectedGroup);
            var newIndex = oldIndex + offset;
            if (newIndex < 0 || newIndex >= _configuration.Groups.Count)
                return;

            _configuration.Groups.Move(oldIndex, newIndex);
            Save();
        }

        private void BuildMoveToGroupMenu(FavoriteLocation favorite)
        {
            MoveToGroupMenuItem.Items.Clear();
            if (favorite == null)
                return;

            var currentGroup = FindGroup(favorite);
            foreach (var group in _configuration.Groups)
            {
                var item = new MenuItem
                {
                    Header = group.Name,
                    Tag = group,
                    IsEnabled = !ReferenceEquals(group, currentGroup)
                };
                item.Click += MoveToGroup_Click;
                MoveToGroupMenuItem.Items.Add(item);
            }
        }

        private void MoveToGroup_Click(object sender, RoutedEventArgs e)
        {
            var favorite = FavoritesTree.SelectedItem as FavoriteLocation;
            var targetGroup = (sender as MenuItem)?.Tag as FavoriteGroup;
            var currentGroup = FindGroup(favorite);
            if (favorite == null || targetGroup == null || currentGroup == null || ReferenceEquals(currentGroup, targetGroup))
                return;

            if (targetGroup.Favorites.Any(item => string.Equals(item.Path, favorite.Path, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(
                    string.Format("'{0}' 그룹에 같은 폴더가 이미 있습니다.", targetGroup.Name),
                    "Folderss",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            currentGroup.Favorites.Remove(favorite);
            targetGroup.Favorites.Add(favorite);
            Save();
            SelectItem(favorite);
        }

        private FavoriteGroup GetSelectedGroup()
        {
            var group = FavoritesTree.SelectedItem as FavoriteGroup;
            if (group != null)
                return group;

            return FindGroup(FavoritesTree.SelectedItem as FavoriteLocation);
        }

        private FavoriteGroup FindGroup(FavoriteLocation favorite)
        {
            if (favorite == null)
                return null;

            return _configuration.Groups.FirstOrDefault(group => group.Favorites.Contains(favorite));
        }

        private void SelectItem(object item)
        {
            FavoritesTree.UpdateLayout();
            var group = item as FavoriteGroup ?? FindGroup(item as FavoriteLocation);
            if (group == null)
                return;

            var groupContainer = FavoritesTree.ItemContainerGenerator.ContainerFromItem(group) as TreeViewItem;
            if (groupContainer == null)
                return;

            groupContainer.IsExpanded = true;
            groupContainer.UpdateLayout();

            var target = item is FavoriteGroup
                ? groupContainer
                : groupContainer.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
            if (target != null)
            {
                target.IsSelected = true;
                target.BringIntoView();
            }
        }

        private void Save()
        {
            try
            {
                FavoritesService.Save(_configuration);
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "즐겨찾기를 저장할 수 없습니다", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
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
    }

    public sealed class FavoriteNavigateEventArgs : EventArgs
    {
        public string Path { get; private set; }

        public FavoriteNavigateEventArgs(string path)
        {
            Path = path;
        }
    }
}

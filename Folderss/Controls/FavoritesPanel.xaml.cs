using Folderss.Models;
using Folderss.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Folderss.Controls
{
    public partial class FavoritesPanel : UserControl
    {
        private readonly ObservableCollection<FavoriteLocation> _favorites =
            new ObservableCollection<FavoriteLocation>();

        public event EventHandler AddCurrentRequested;
        public event EventHandler<FavoriteNavigateEventArgs> NavigateRequested;

        public FavoritesPanel()
        {
            InitializeComponent();
            foreach (var favorite in FavoritesService.Load())
                _favorites.Add(favorite);
            FavoritesList.ItemsSource = _favorites;
        }

        public bool AddFavorite(string path, string displayName = null)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return false;

            if (_favorites.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("이미 즐겨찾기에 등록된 폴더입니다.", "Folderss", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            var name = string.IsNullOrWhiteSpace(displayName) ? new DirectoryInfo(path).Name : displayName.Trim();
            if (string.IsNullOrWhiteSpace(name))
                name = path;

            _favorites.Add(new FavoriteLocation { Name = name, Path = path });
            Save();
            FavoritesList.SelectedIndex = _favorites.Count - 1;
            return true;
        }

        private void AddCurrent_Click(object sender, RoutedEventArgs e)
        {
            var handler = AddCurrentRequested;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        private void FavoritesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var favorite = FavoritesList.SelectedItem as FavoriteLocation;
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

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            var favorite = FavoritesList.SelectedItem as FavoriteLocation;
            if (favorite == null)
                return;

            _favorites.Remove(favorite);
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
            var favorite = FavoritesList.SelectedItem as FavoriteLocation;
            if (favorite == null)
                return;

            var oldIndex = _favorites.IndexOf(favorite);
            var newIndex = oldIndex + offset;
            if (newIndex < 0 || newIndex >= _favorites.Count)
                return;

            _favorites.Move(oldIndex, newIndex);
            FavoritesList.SelectedItem = favorite;
            Save();
        }

        private void Save()
        {
            try
            {
                FavoritesService.Save(_favorites);
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "즐겨찾기를 저장할 수 없습니다", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
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

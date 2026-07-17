using Folderss.Models;
using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Folderss.Controls
{
    /// <summary>즐겨찾기 패널과 별도 레이어로, 그 위쪽에 항상 고정 표시되는 특수 바로가기(예: 디스크 사용량) 목록.</summary>
    public partial class PinnedShortcutsPanel : UserControl
    {
        private INotifyCollectionChanged _observedSource;

        public event EventHandler<FavoriteNavigateEventArgs> NavigateRequested;

        public PinnedShortcutsPanel()
        {
            InitializeComponent();
        }

        public void SetItemsSource(ObservableCollection<FavoriteLocation> items)
        {
            if (_observedSource != null)
                _observedSource.CollectionChanged -= Items_CollectionChanged;

            List.ItemsSource = items;
            _observedSource = items;
            if (_observedSource != null)
                _observedSource.CollectionChanged += Items_CollectionChanged;

            UpdateVisibility(items);
        }

        private void Items_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateVisibility(sender as ICollection);
        }

        private void UpdateVisibility(ICollection items)
        {
            List.Visibility = items != null && items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Item_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var favorite = (sender as FrameworkElement)?.DataContext as FavoriteLocation;
            if (favorite == null || !favorite.IsSpecial)
                return;

            var handler = NavigateRequested;
            if (handler != null)
                handler(this, new FavoriteNavigateEventArgs(null, false, favorite.SpecialKind));
        }
    }
}

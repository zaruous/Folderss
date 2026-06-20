using Folderss.Models;
using Folderss.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Folderss.Controls
{
    public partial class SearchPanel : UserControl
    {
        public event EventHandler<SearchNavigateEventArgs> NavigateRequested;
        public event EventHandler HideRequested;

        private readonly ObservableCollection<SearchResult> _results = new ObservableCollection<SearchResult>();
        private CancellationTokenSource _cts;
        private string _rootPath;
        private int _resultCount;

        public SearchPanel()
        {
            InitializeComponent();
            var view = CollectionViewSource.GetDefaultView(_results);
            view.GroupDescriptions.Add(new PropertyGroupDescription("FileName"));
            ResultList.ItemsSource = view;
        }

        public void SetRootPath(string path)
        {
            _rootPath = path;
        }

        public void FocusSearchBox()
        {
            QueryBox.Focus();
            QueryBox.SelectAll();
        }

        private void SearchPanel_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CancelSearch();
                HideRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        }

        private void QueryBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                StartSearch();
                e.Handled = true;
            }
        }

        private void QueryBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsInitialized) return;
            if (string.IsNullOrEmpty(QueryBox.Text))
            {
                CancelSearch();
                _results.Clear();
                _resultCount = 0;
                StatusText.Text = "검색어를 입력하고 Enter를 누르세요.";
            }
        }

        private void Option_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized) return;
            CancelSearch();
        }

        private void ScopeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsInitialized) return;
            CancelSearch();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CancelSearch();
        }

        private void ResultList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var container = FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject);
            if (container == null)
            {
                ResultList.SelectedItems.Clear();
                return;
            }
            if (!container.IsSelected)
            {
                ResultList.SelectedItems.Clear();
                container.IsSelected = true;
            }
            container.Focus();
        }

        private void ResultList_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            ShowResultContextMenu(e);
        }

        private void ResultList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ShowResultContextMenu(e);
        }

        private void ShowResultContextMenu(MouseButtonEventArgs e)
        {
            var result = ResultList.SelectedItem as SearchResult;
            if (result == null || string.IsNullOrWhiteSpace(result.FilePath))
                return;

            var window = Window.GetWindow(this);
            if (window == null) return;

            var screenPoint = ResultList.PointToScreen(e.GetPosition(ResultList));
            try
            {
                ShellContextMenuService.Show(
                    new WindowInteropHelper(window).Handle,
                    new[] { result.FilePath },
                    (int)screenPoint.X,
                    (int)screenPoint.Y);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "컨텍스트 메뉴를 열 수 없습니다", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void StartSearch()
        {
            var query = QueryBox.Text;
            if (string.IsNullOrEmpty(query) || string.IsNullOrWhiteSpace(_rootPath))
                return;

            CancelSearch();
            _results.Clear();
            _resultCount = 0;

            var caseSensitive = CaseToggle.IsChecked == true;
            var useRegex = RegexToggle.IsChecked == true;
            var selectedItem = ScopeCombo.SelectedItem as ComboBoxItem;
            var recursive = selectedItem != null && (string)selectedItem.Tag == "recursive";

            if (useRegex)
            {
                try { new System.Text.RegularExpressions.Regex(query); }
                catch (Exception ex)
                {
                    StatusText.Text = "정규식 오류: " + ex.Message;
                    return;
                }
            }

            var cts = new CancellationTokenSource();
            _cts = cts;
            var token = cts.Token;
            CancelButton.Visibility = Visibility.Visible;
            StatusText.Text = "검색 중…";

            var progress = new Progress<SearchResult>(result =>
            {
                _results.Add(result);
                _resultCount++;
                StatusText.Text = string.Format("{0}건 발견됨", _resultCount);
            });

            try
            {
                await SearchService.SearchAsync(_rootPath, query, recursive, caseSensitive, useRegex, progress, token);
                StatusText.Text = _resultCount == 0
                    ? "검색 결과가 없습니다."
                    : string.Format("검색 완료 — {0}건 발견됨", _resultCount);
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = string.Format("검색 취소됨 — {0}건 발견됨", _resultCount);
            }
            catch (Exception ex)
            {
                StatusText.Text = "검색 오류: " + ex.Message;
            }
            finally
            {
                if (CancelButton != null)
                    CancelButton.Visibility = Visibility.Collapsed;
                if (ReferenceEquals(_cts, cts))
                {
                    _cts = null;
                    cts.Dispose();
                }
            }
        }

        private void CancelSearch()
        {
            var cts = _cts;
            _cts = null;
            cts?.Cancel();
            cts?.Dispose();
            if (CancelButton != null)
                CancelButton.Visibility = Visibility.Collapsed;
        }

        private static T FindAncestor<T>(DependencyObject dep) where T : DependencyObject
        {
            while (dep != null)
            {
                if (dep is T target) return target;
                dep = VisualTreeHelper.GetParent(dep);
            }
            return null;
        }
    }

    public sealed class SearchNavigateEventArgs : EventArgs
    {
        public string Path { get; }
        public SearchNavigateEventArgs(string path) { Path = path; }
    }
}

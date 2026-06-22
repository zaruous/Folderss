using Folderss.Models;
using Folderss.Services;
using System;
using System.Collections.Generic;
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

        private readonly List<SearchResult> _allResults = new List<SearchResult>();
        private readonly ObservableCollection<SearchResult> _results = new ObservableCollection<SearchResult>();
        private const int PageSize = 100;
        private int _currentPage;
        private CancellationTokenSource _cts;
        private string _rootPath;

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
                _allResults.Clear();
                _currentPage = 0;
                UpdatePaginationControls();
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
            _allResults.Clear();
            _currentPage = 0;
            UpdatePaginationControls();

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
                _allResults.Add(result);
                if (_allResults.Count <= PageSize)
                    _results.Add(result);
                StatusText.Text = string.Format("검색 중… {0}건 발견됨", _allResults.Count);
                if (_allResults.Count == PageSize + 1)
                    UpdatePaginationControls();
            });

            try
            {
                await SearchService.SearchAsync(_rootPath, query, recursive, caseSensitive, useRegex, progress, token);
                StatusText.Text = _allResults.Count == 0
                    ? "검색 결과가 없습니다."
                    : FormatStatus();
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = _allResults.Count == 0
                    ? "검색 취소됨"
                    : string.Format("검색 취소됨 — {0}", FormatStatus());
            }
            catch (Exception ex)
            {
                StatusText.Text = "검색 오류: " + ex.Message;
            }
            finally
            {
                if (CancelButton != null)
                    CancelButton.Visibility = Visibility.Collapsed;
                UpdatePaginationControls();
                if (ReferenceEquals(_cts, cts))
                {
                    _cts = null;
                    cts.Dispose();
                }
            }
        }

        private void PrevButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 0)
                ShowPage(_currentPage - 1);
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            int totalPages = (_allResults.Count + PageSize - 1) / PageSize;
            if (_currentPage < totalPages - 1)
                ShowPage(_currentPage + 1);
        }

        private void ShowPage(int page)
        {
            _currentPage = page;
            _results.Clear();
            int start = page * PageSize;
            int end = Math.Min(start + PageSize, _allResults.Count);
            for (int i = start; i < end; i++)
                _results.Add(_allResults[i]);
            UpdatePaginationControls();
            StatusText.Text = FormatStatus();
            if (ResultList.Items.Count > 0)
                ResultList.ScrollIntoView(ResultList.Items[0]);
        }

        private void UpdatePaginationControls()
        {
            int totalPages = (_allResults.Count + PageSize - 1) / PageSize;
            PrevButton.Visibility = _currentPage > 0 ? Visibility.Visible : Visibility.Collapsed;
            NextButton.Visibility = _currentPage < totalPages - 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        private string FormatStatus()
        {
            int total = _allResults.Count;
            int totalPages = (total + PageSize - 1) / PageSize;
            if (totalPages <= 1)
                return string.Format("검색 완료 — {0}건 발견됨", total);
            int start = _currentPage * PageSize + 1;
            int end = Math.Min((_currentPage + 1) * PageSize, total);
            return string.Format("{0}-{1} / {2}건 (페이지 {3}/{4})", start, end, total, _currentPage + 1, totalPages);
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

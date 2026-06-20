using Folderss.Models;
using Folderss.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Folderss.Controls
{
    public partial class SearchPanel : UserControl
    {
        public event EventHandler<SearchNavigateEventArgs> NavigateRequested;

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

        private void QueryBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                StartSearch();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelSearch();
                e.Handled = true;
            }
        }

        private void QueryBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsInitialized) return;
            // 검색어가 비워지면 결과도 초기화
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

        private void ResultList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var result = ResultList.SelectedItem as SearchResult;
            if (result == null || string.IsNullOrWhiteSpace(result.FolderPath))
                return;

            NavigateRequested?.Invoke(this, new SearchNavigateEventArgs(result.FolderPath));
        }

        private void StartSearch()
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

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            CancelButton.Visibility = Visibility.Visible;
            StatusText.Text = "검색 중…";

            var progress = new Progress<SearchResult>(result =>
            {
                _results.Add(result);
                _resultCount++;
                StatusText.Text = string.Format("{0}건 발견됨", _resultCount);
            });

            SearchService.SearchAsync(_rootPath, query, recursive, caseSensitive, useRegex, progress, token)
                .ContinueWith(t =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        CancelButton.Visibility = Visibility.Collapsed;
                        if (t.IsCanceled)
                            StatusText.Text = string.Format("검색 취소됨 — {0}건 발견됨", _resultCount);
                        else if (t.IsFaulted)
                            StatusText.Text = "검색 오류: " + (t.Exception?.InnerException?.Message ?? "알 수 없는 오류");
                        else
                            StatusText.Text = _resultCount == 0
                                ? "검색 결과가 없습니다."
                                : string.Format("검색 완료 — {0}건 발견됨", _resultCount);
                    });
                });
        }

        private void CancelSearch()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
            if (CancelButton != null)
                CancelButton.Visibility = Visibility.Collapsed;
        }
    }

    public sealed class SearchNavigateEventArgs : EventArgs
    {
        public string Path { get; }
        public SearchNavigateEventArgs(string path) { Path = path; }
    }
}

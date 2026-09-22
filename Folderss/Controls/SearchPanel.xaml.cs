using Folderss.Models;
using Folderss.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Folderss.Controls
{
    public partial class SearchPanel : UserControl
    {
        public event EventHandler<SearchNavigateEventArgs> NavigateRequested;
        public event EventHandler HideRequested;

        /// <summary>'현재 폴더' 버튼 — 고정을 풀고 활성 패널 폴더를 다시 받아오도록 호스트에 요청한다.</summary>
        public event EventHandler ActivePaneRootRequested;

        /// <summary>'패널에 필터 적용' 버튼 — 현재 결과 파일 집합을 활성 폴더 패널의 목록 필터로 쓰도록 호스트에 요청한다.</summary>
        public event EventHandler<SearchFilterEventArgs> ApplyFilterRequested;

        private readonly List<SearchResult> _allResults = new List<SearchResult>();
        private readonly ObservableCollection<SearchResult> _results = new ObservableCollection<SearchResult>();
        private const int PageSize = 100;
        private const int ContentColumnIndex = 1;
        private int _currentPage;
        private CancellationTokenSource _cts;
        private string _rootPath;

        // 사용자가 '폴더 선택…'으로 대상 폴더를 직접 고른 상태. 이때는 활성 패널 변경을 따라가지 않는다.
        private bool _rootPinned;

        public SearchPanel()
        {
            InitializeComponent();
            var view = CollectionViewSource.GetDefaultView(_results);
            view.GroupDescriptions.Add(new PropertyGroupDescription("FileName"));
            ResultList.ItemsSource = view;
        }

        /// <summary>
        /// 활성 폴더 패널을 따라가기 위한 경로 갱신. 사용자가 폴더를 직접 고른 뒤에는 무시한다
        /// — 그렇지 않으면 검색 창이 포커스를 받을 때마다 사용자가 고른 폴더가 덮어써진다.
        /// </summary>
        public void SetRootPath(string path)
        {
            if (_rootPinned)
                return;

            ApplyRootPath(path);
        }

        /// <summary>고정을 풀고 활성 패널 폴더로 되돌린다.</summary>
        public void FollowActivePaneRoot(string path)
        {
            _rootPinned = false;
            ApplyRootPath(path);
        }

        private void ApplyRootPath(string path)
        {
            _rootPath = path;
            RootPathBox.Text = path ?? string.Empty;
            UseActivePaneButton.IsEnabled = _rootPinned;
        }

        private void BrowseRootButton_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new Forms.FolderBrowserDialog())
            {
                dialog.Description = "검색할 폴더를 선택하세요.";
                dialog.ShowNewFolderButton = false;
                if (!string.IsNullOrWhiteSpace(_rootPath) && Directory.Exists(_rootPath))
                    dialog.SelectedPath = _rootPath;

                if (dialog.ShowDialog() != Forms.DialogResult.OK)
                    return;

                CancelSearch();
                _rootPinned = true;
                ApplyRootPath(dialog.SelectedPath);
                NotifyResearchRequired();
            }
        }

        private void UseActivePaneButton_Click(object sender, RoutedEventArgs e)
        {
            CancelSearch();
            ActivePaneRootRequested?.Invoke(this, EventArgs.Empty);
            NotifyResearchRequired();
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
            NotifyResearchRequired();
        }

        private void ScopeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsInitialized) return;
            CancelSearch();
            NotifyResearchRequired();
        }

        /// <summary>
        /// 옵션·범위를 바꿔도 자동 재검색은 하지 않는다(대상 폴더가 크면 비용이 크다).
        /// 다만 이전 결과가 그대로 남아 새 옵션의 결과로 오해되기 쉽고, 이때 포커스가 콤보박스에 있어
        /// 그 자리에서 Enter를 눌러도 검색이 시작되지 않으므로 무엇을 해야 하는지 명시한다.
        /// </summary>
        private void NotifyResearchRequired()
        {
            if (_allResults.Count == 0)
                return;

            StatusText.Text = "검색 조건이 바뀌었습니다 — 검색어 입력란을 클릭하고 Enter를 눌러 다시 검색하세요.";
        }

        private void ContentColumnToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized) return;
            UpdateContentColumnVisibility();
        }

        /// <summary>
        /// <see cref="GridViewColumn"/>에는 Visibility가 없어서, 컬럼을 컬렉션에서 빼고 넣는 방식으로 표시를 전환한다.
        /// 다시 넣을 때는 원래 자리(줄 / 내용 / 경로의 가운데)로 복원한다.
        /// </summary>
        private void UpdateContentColumnVisibility()
        {
            var gridView = ResultList.View as GridView;
            if (gridView == null || ContentColumn == null)
                return;

            var show = ContentColumnToggle.IsChecked == true;
            var index = gridView.Columns.IndexOf(ContentColumn);

            if (show && index < 0)
                gridView.Columns.Insert(Math.Min(ContentColumnIndex, gridView.Columns.Count), ContentColumn);
            else if (!show && index >= 0)
                gridView.Columns.Remove(ContentColumn);
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
            var result = ResultList.SelectedItem as SearchResult;
            if (result == null || string.IsNullOrWhiteSpace(result.FilePath))
                return;

            NavigateRequested?.Invoke(this, new SearchNavigateEventArgs(result.FilePath));
            e.Handled = true;
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
            if (string.IsNullOrEmpty(query))
                return;

            // 대상 폴더가 없으면 조용히 끝내지 않는다. 아무 반응이 없으면 사용자가 원인을 알 수 없다.
            if (string.IsNullOrWhiteSpace(_rootPath))
            {
                StatusText.Text = "검색할 폴더가 없습니다. 폴더 패널에서 폴더를 연 뒤 다시 시도하세요.";
                return;
            }

            CancelSearch();
            _results.Clear();
            _allResults.Clear();
            _currentPage = 0;
            UpdatePaginationControls();
            ApplyFilterButton.Visibility = Visibility.Collapsed;

            var caseSensitive = CaseToggle.IsChecked == true;
            var useRegex = RegexToggle.IsChecked == true;
            var selectedItem = ScopeCombo.SelectedItem as ComboBoxItem;
            var recursive = selectedItem != null && (string)selectedItem.Tag == "recursive";
            var targetItem = TargetCombo.SelectedItem as ComboBoxItem;
            var target = targetItem != null && (string)targetItem.Tag == "filename"
                ? SearchTarget.FileName
                : SearchTarget.Content;

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
                await SearchService.SearchAsync(_rootPath, query, recursive, caseSensitive, useRegex, target, progress, token);
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
                if (ApplyFilterButton != null)
                    ApplyFilterButton.Visibility = _allResults.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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

        private void ApplyFilterButton_Click(object sender, RoutedEventArgs e)
        {
            if (_allResults.Count == 0)
                return;

            var files = _allResults
                .Select(result => result.FilePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var handler = ApplyFilterRequested;
            if (handler != null)
                handler(this, new SearchFilterEventArgs(_rootPath, files, QueryBox.Text.Trim()));
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

    public sealed class SearchFilterEventArgs : EventArgs
    {
        /// <summary>검색 대상 폴더. 활성 패널이 다른 폴더를 보고 있으면 호스트가 이 폴더로 이동한 뒤 필터를 건다.</summary>
        public string RootPath { get; }
        /// <summary>결과 파일 전체 경로(중복 제거).</summary>
        public IReadOnlyList<string> FilePaths { get; }
        /// <summary>패널 배너에 보여 줄 설명(검색어).</summary>
        public string Description { get; }

        public SearchFilterEventArgs(string rootPath, IReadOnlyList<string> filePaths, string description)
        {
            RootPath = rootPath;
            FilePaths = filePaths;
            Description = description;
        }
    }

    public sealed class SearchNavigateEventArgs : EventArgs
    {
        public string Path { get; }
        public SearchNavigateEventArgs(string path) { Path = path; }
    }
}

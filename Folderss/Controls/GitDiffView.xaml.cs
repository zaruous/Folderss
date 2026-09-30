using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Folderss.Models;
using Folderss.Services;

namespace Folderss.Controls
{
    /// <summary>
    /// unified diff를 줄 단위(변경 전/후 줄 번호, 추가·삭제 색)로 보이는 읽기 전용 뷰. Git 창의 변경 사항·원격 비교·로그와
    /// 두 파일 비교 창(<see cref="FileCompareWindow"/>)이 함께 쓴다.
    /// 비동기 로드는 <see cref="BeginLoad"/>가 준 번호로 <see cref="Complete"/>해야 반영된다 — 빠르게 다른 항목을 고르면 늦게 온 옛 결과는 버린다.
    /// </summary>
    public partial class GitDiffView : UserControl
    {
        /// <summary>한 번에 그리는 최대 줄 수. 넘으면 자르고 안내 줄을 붙인다.</summary>
        public const int MaxLines = 20000;

        private int _requestId;

        /// <summary>현재 보이는 비교. 외부 도구 버튼이 같은 비교를 열 때 쓴다.</summary>
        public GitDiffRequest CurrentRequest { get; private set; }

        /// <summary>"외부 도구로 비교"를 눌렀다. 실행은 저장소·설정을 아는 Git 창이 한다.</summary>
        public event EventHandler ExternalToolRequested;

        /// <summary>사용자가 보기 모드를 바꿨다. Git 창이 <see cref="CurrentRequest"/>를 새 모드로 다시 불러온다.</summary>
        public event EventHandler ViewModeChanged;

        private bool _settingMode;

        /// <summary>
        /// 비교를 주어진 범위(보기 모드)로 다시 읽어 diff 텍스트를 돌려준다(실패는 예외). 호스트 창이 설정하며,
        /// 있으면 "HTML 보고서…" 버튼이 보인다 — 보고서 범위가 화면 보기 모드와 다를 수 있어서 호스트가 다시 실행한다.
        /// </summary>
        public Func<GitDiffRequest, GitDiffViewMode, CancellationToken, Task<string>> TextLoader { get; set; }

        /// <summary>
        /// 변경점만 / 문맥 10줄 / 전체 파일. 코드에서 바꿀 때는 <see cref="ViewModeChanged"/>를 내지 않는다(다시 불러오기는 호출 측 몫).
        /// </summary>
        public GitDiffViewMode ViewMode
        {
            get => (GitDiffViewMode)Math.Max(0, ViewModeCombo.SelectedIndex);
            set
            {
                _settingMode = true;
                ViewModeCombo.SelectedIndex = (int)value;
                _settingMode = false;
            }
        }

        public GitDiffView()
        {
            InitializeComponent();
            ViewMode = GitDiffViewMode.ChangesOnly;
            Clear();
        }

        public void Clear(string message = "항목을 선택하면 차이를 보여 줍니다.")
        {
            _requestId++;
            SetRequest(null);
            TitleText.Text = string.Empty;
            LineList.ItemsSource = null;
            ShowMessage(message);
        }

        public int BeginLoad(GitDiffRequest request)
        {
            _requestId++;
            SetRequest(request);
            var title = request?.Title ?? string.Empty;
            TitleText.Text = title;
            TitleText.ToolTip = title;
            LineList.ItemsSource = null;
            ShowMessage("불러오는 중…");
            return _requestId;
        }

        /// <summary>diff가 아닌 줄 목록(변경 없는 파일 내용 등)을 바로 보인다. 외부 도구·보기 모드는 숨긴다.</summary>
        public void ShowLines(string title, System.Collections.Generic.List<GitDiffLine> lines, string emptyMessage)
        {
            _requestId++;
            SetRequest(null);
            TitleText.Text = title;
            TitleText.ToolTip = title;
            LineList.ItemsSource = lines;
            ShowMessage(lines.Count == 0 ? emptyMessage : null);
        }

        /// <summary>diff 텍스트를 반영한다. 더 새 요청이 있었으면 false를 돌려주고 무시한다.</summary>
        public bool Complete(int requestId, string diffText, string emptyMessage = "차이가 없습니다.")
        {
            if (requestId != _requestId)
                return false;

            var lines = GitOutputParser.ParseDiff(diffText, MaxLines);
            LineList.ItemsSource = lines;
            ShowMessage(lines.Count == 0 ? emptyMessage : null);
            return true;
        }

        public bool Fail(int requestId, string message)
        {
            if (requestId != _requestId)
                return false;
            LineList.ItemsSource = null;
            ShowMessage(message);
            return true;
        }

        private void SetRequest(GitDiffRequest request)
        {
            CurrentRequest = request;
            // 외부 도구로 열 수 없는 비교(추적 안 됨, 최초 커밋)는 버튼을 숨긴다.
            ExternalToolButton.Visibility = request?.ExternalSelector != null ? Visibility.Visible : Visibility.Collapsed;
            // 추적 안 되는 파일(빈 쪽 ↔ 파일)은 원래 전체가 추가로 보이므로 보기 모드가 의미 없다. 두 파일 비교(--no-index)는 보인다.
            ViewModeCombo.Visibility = request != null && !(request.NoIndex && request.OldSide == null) ? Visibility.Visible : Visibility.Collapsed;
            ReportButton.Visibility = request != null && TextLoader != null ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ViewModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_settingMode && CurrentRequest != null)
                ViewModeChanged?.Invoke(this, EventArgs.Empty);
        }

        private async void ReportButton_Click(object sender, RoutedEventArgs e)
        {
            ReportButton.IsEnabled = false;
            try
            {
                await DiffReportExporter.ExportAsync(Window.GetWindow(this), CurrentRequest, ViewMode, TextLoader);
            }
            finally
            {
                ReportButton.IsEnabled = true;
            }
        }

        private void ExternalToolButton_Click(object sender, RoutedEventArgs e)
        {
            ExternalToolRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ShowMessage(string message)
        {
            MessageText.Text = message ?? string.Empty;
            MessageText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void LineList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.C || Keyboard.Modifiers != ModifierKeys.Control || LineList.SelectedItems.Count == 0)
                return;

            // 선택 순서가 아니라 화면 순서로 복사한다.
            var selected = LineList.SelectedItems.Cast<GitDiffLine>().ToHashSet();
            var text = string.Join("\r\n", LineList.Items.Cast<GitDiffLine>().Where(selected.Contains).Select(line => line.Text));
            try
            {
                Clipboard.SetText(text);
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // 다른 프로그램이 클립보드를 잡고 있으면 조용히 실패한다(사용자가 다시 누르면 됨).
            }
            e.Handled = true;
        }
    }
}

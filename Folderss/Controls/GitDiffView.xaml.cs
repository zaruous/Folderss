using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Folderss.Models;
using Folderss.Services;

namespace Folderss.Controls
{
    /// <summary>
    /// unified diff를 줄 단위(변경 전/후 줄 번호, 추가·삭제 색)로 보이는 읽기 전용 뷰. Git 창의 변경 사항·원격 비교·로그에서 쓴다.
    /// 비동기 로드는 <see cref="BeginLoad"/>가 준 번호로 <see cref="Complete"/>해야 반영된다 — 빠르게 다른 항목을 고르면 늦게 온 옛 결과는 버린다.
    /// </summary>
    public partial class GitDiffView : UserControl
    {
        /// <summary>한 번에 그리는 최대 줄 수. 넘으면 자르고 안내 줄을 붙인다.</summary>
        public const int MaxLines = 20000;

        private int _requestId;

        public GitDiffView()
        {
            InitializeComponent();
            Clear();
        }

        public void Clear(string message = "항목을 선택하면 차이를 보여 줍니다.")
        {
            _requestId++;
            TitleText.Text = string.Empty;
            LineList.ItemsSource = null;
            ShowMessage(message);
        }

        public int BeginLoad(string title)
        {
            _requestId++;
            TitleText.Text = title;
            TitleText.ToolTip = title;
            LineList.ItemsSource = null;
            ShowMessage("불러오는 중…");
            return _requestId;
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

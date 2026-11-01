using Folderss.Services;
using Folderss.Viewers;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace Folderss.Controls
{
    /// <summary>
    /// <c>.md</c>·<c>.markdown</c> 탭의 블록 편집 ↔ 미리보기(Markdown 뷰어) 전환(#81).
    /// <list type="bullet">
    /// <item>확장자 매핑은 바꾸지 않고 이 탭의 뷰어만 바꾼다. 탭(<c>LayoutDocument</c>)·ContentId는 그대로라 패널 잠금·세션 항목이 유지된다.</item>
    /// <item>저장하지 않은 변경이 있으면 "저장 후 전환 / 취소"를 묻는다(편집 내용을 다른 뷰어로 넘기지 않음).</item>
    /// <item>보던 위치는 제목 앵커 → 줄 번호로 넘기고(<see cref="MarkdownViewSwitch"/>), 고른 보기는 파일별로 기억한다(<see cref="MarkdownViewSettings"/>).</item>
    /// <item>이전 뷰어는 <see cref="IDisposable"/>로 해제한다(Markdown 뷰어는 WebView2까지).</item>
    /// </list>
    /// </summary>
    public partial class ViewerHost
    {
        /// <summary>지금 보기(<c>blocks</c>/<c>preview</c>). 전환할 수 없는 탭이면 null.</summary>
        public string CurrentView => CanSwitchView ? MarkdownViewSwitch.ToView(_currentViewerKey) : null;

        /// <summary>전환 버튼을 보이는 탭인지: <c>.md</c>·<c>.markdown</c>이고 지금 뷰어가 블록 편집기나 Markdown 뷰어.</summary>
        public bool CanSwitchView =>
            _currentViewer is IMarkdownViewSwitchable &&
            MarkdownViewSwitch.IsMarkdownExtension(Path.GetExtension(_currentFilePath ?? string.Empty)) &&
            MarkdownViewSwitch.IsSwitchableKey(_currentViewerKey);

        private void BlocksView_Click(object sender, RoutedEventArgs e)
        {
            _ = SwitchViewAsync(MarkdownViewSwitch.ViewBlocks);
        }

        private void PreviewView_Click(object sender, RoutedEventArgs e)
        {
            _ = SwitchViewAsync(MarkdownViewSwitch.ViewPreview);
        }

        /// <summary>이 탭을 <paramref name="view"/>(<c>blocks</c>/<c>preview</c>)로 바꾼다. 바꿨으면 true(이미 그 보기·취소·저장 실패는 false).</summary>
        public async Task<bool> SwitchViewAsync(string view)
        {
            var targetKey = MarkdownViewSwitch.ToViewerKey(view);
            var switchable = _currentViewer as IMarkdownViewSwitchable;
            if (_switchingView || targetKey == null || switchable == null || !CanSwitchView ||
                string.Equals(targetKey, _currentViewerKey, StringComparison.OrdinalIgnoreCase))
            {
                UpdateViewSwitchButtons();
                return false;
            }

            var filePath = _currentFilePath;
            var viewName = view == MarkdownViewSwitch.ViewBlocks ? "블록 편집" : "미리보기";
            _switchingView = true;
            UpdateViewSwitchButtons();
            try
            {
                if (_isModified)
                {
                    var message = string.Format(
                        "'{0}'에 저장하지 않은 변경이 있습니다.\n\n저장한 뒤 {1}(으)로 전환할까요?\n(취소하면 전환하지 않고 지금 보기에 남습니다.)",
                        Path.GetFileName(filePath), viewName);
                    var owner = Window.GetWindow(this);
                    var answer = owner != null
                        ? MessageBox.Show(owner, message, "보기 전환", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel)
                        : MessageBox.Show(message, "보기 전환", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
                    if (answer != MessageBoxResult.OK)
                        return false;
                    if (!await switchable.SaveForSwitchAsync() || !ReferenceEquals(switchable, _currentViewer))
                        return false;
                }

                if (!File.Exists(filePath))
                {
                    MessageBox.Show("파일이 존재하지 않습니다.", "Folderss", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                var position = await switchable.CaptureViewPositionAsync();
                // 기다리는 사이 탭이 닫히거나 다른 파일을 열었으면 그만둔다.
                if (!ReferenceEquals(switchable, _currentViewer))
                    return false;

                var viewer = ViewerConfigService.CreateViewer(targetKey);
                if (viewer == null)
                    return false;

                try
                {
                    MarkdownViewSettings.Shared.RememberAndSave(filePath, view);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // 파일별 보기 기억은 편의 기능이라 저장하지 못해도 전환은 한다(다음 실행에서 기억만 빠짐). 원인은 디버그 출력에 남긴다.
                    Trace.WriteLine("markdown-view.json 저장 실패: " + ex);
                }

                var wasModified = _isModified;
                DetachCurrentViewer();
                if (wasModified)
                    ModifiedChanged?.Invoke(this, false);   // 탭 제목의 ' *'를 지운다(저장했으므로)
                AttachViewer(filePath, viewer, targetKey);
                (viewer as IMarkdownViewSwitchable)?.RestoreViewPositionWhenReady(position);
                return true;
            }
            finally
            {
                _switchingView = false;
                UpdateViewSwitchButtons();
            }
        }

        private void UpdateViewSwitchButtons()
        {
            var show = CanSwitchView;
            ViewSwitchPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            ViewSwitchPanel.IsEnabled = !_switchingView;
            BlocksViewButton.IsChecked = show && string.Equals(_currentViewerKey, ViewerConfigService.BuiltInBlocksKey, StringComparison.OrdinalIgnoreCase);
            PreviewViewButton.IsChecked = show && string.Equals(_currentViewerKey, ViewerConfigService.BuiltInMarkdownKey, StringComparison.OrdinalIgnoreCase);
        }
    }
}

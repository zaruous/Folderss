using Folderss.Services;
using System;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Folderss.Viewers
{
    /// <summary>블록 편집 ↔ 미리보기 전환(#81): 저장, 보던 위치(맨 위 블록 → 제목·줄 번호) 잡기·되돌리기.</summary>
    public sealed partial class BlockEditorViewer : IMarkdownViewSwitchable
    {
        public Task<bool> SaveForSwitchAsync()
        {
            if (_tooLarge || !_editor.IsModified)
                return Task.FromResult(true);
            Save();   // 실패하면 Save가 오류 창을 띄우고 미저장 상태로 남는다
            return Task.FromResult(!_editor.IsModified);
        }

        public Task<MarkdownViewPosition> CaptureViewPositionAsync()
        {
            return Task.FromResult(_tooLarge || _filePath == null ? null : _editor.CaptureViewPosition());
        }

        public void RestoreViewPositionWhenReady(MarkdownViewPosition position)
        {
            if (position == null || _tooLarge)
                return;
            // 편집기가 화면에 붙고 한 번 측정된 뒤에 옮겨야 가상화 패널이 위치를 계산한다(ConsumePendingAnchorLater와 같은 시점).
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (_disposed || _tooLarge || _filePath == null)
                    return;
                _editor.RestoreViewPosition(position);
            }));
        }
    }
}

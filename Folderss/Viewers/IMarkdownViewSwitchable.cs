using Folderss.Services;
using System.Threading.Tasks;

namespace Folderss.Viewers
{
    /// <summary>
    /// <c>.md</c> 탭에서 블록 편집 ↔ 미리보기로 바꿀 수 있는 뷰어(블록 편집기·Markdown 뷰어). <see cref="Folderss.Controls.ViewerHost"/>가 전환할 때 쓴다(#81).
    /// </summary>
    public interface IMarkdownViewSwitchable
    {
        /// <summary>지금 편집 내용을 파일에 저장한다. 저장해서 미저장 변경이 없어졌으면 true(실패하면 뷰어가 오류를 보여 주고 false).</summary>
        Task<bool> SaveForSwitchAsync();

        /// <summary>보던 위치(제목 앵커·줄 번호). 알 수 없으면 null.</summary>
        Task<MarkdownViewPosition> CaptureViewPositionAsync();

        /// <summary>
        /// <see cref="IFileViewer.Load"/> 직후 부른다. 화면·페이지가 준비되면 이 위치로 옮긴다
        /// (다른 문서의 <c>파일.md#제목</c> 링크로 넘어온 이동이 있으면 그쪽이 먼저다).
        /// </summary>
        void RestoreViewPositionWhenReady(MarkdownViewPosition position);
    }
}

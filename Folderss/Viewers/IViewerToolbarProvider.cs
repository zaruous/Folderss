using System.Windows;

namespace Folderss.Viewers
{
    /// <summary>
    /// 탭 툴바(<c>ViewerHost</c> 위쪽 줄 오른쪽)에 뷰어 전용 컨트롤을 넣는 뷰어. 예: Monaco 언어 선택(#82).
    /// <c>ViewerHost</c>가 뷰어를 붙일 때 <see cref="ToolbarContent"/>를 툴바 칸에 넣고, 뗄 때 비운다.
    /// </summary>
    public interface IViewerToolbarProvider
    {
        /// <summary>툴바에 넣을 요소. 없으면 null. 뷰어 수명 동안 같은 인스턴스를 돌려준다.</summary>
        UIElement ToolbarContent { get; }
    }
}

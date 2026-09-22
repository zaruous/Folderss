// ViewerConfigService.Resolve()는 WPF 뷰어 컨트롤을 생성한다. 이 테스트 프로젝트는 net8.0(WPF 없음)이고
// 매핑의 로드·저장 규칙만 검증하므로, 뷰어 타입을 빈 스텁으로 대체해 소스 링크가 컴파일되게 한다.
namespace Folderss.Viewers
{
    public interface IFileViewer { }

    public sealed class TextViewer : IFileViewer { }

    public sealed class MarkdownViewer : IFileViewer { }

    public sealed class MonacoViewer : IFileViewer { }
}

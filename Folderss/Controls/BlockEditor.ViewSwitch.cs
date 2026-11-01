using Folderss.Services;

namespace Folderss.Controls
{
    /// <summary>
    /// 블록 편집 ↔ 미리보기(Markdown 뷰어) 전환에서 보던 위치를 잡고 되돌린다(#81). 위치 대응 규칙은 <see cref="MarkdownViewSwitch"/>.
    /// </summary>
    public sealed partial class BlockEditor
    {
        /// <summary>화면 맨 위 블록의 제목 앵커·줄 번호. 문서가 비었으면 null.</summary>
        public MarkdownViewPosition CaptureViewPosition()
        {
            double delta;
            var index = _panel.GetTopAnchor(out delta);
            return MarkdownViewSwitch.FromBlock(_blocks, index);
        }

        /// <summary>다른 보기에서 잡은 위치(제목·줄 번호)에 맞는 블록을 화면 맨 위로 올린다. 찾았으면 true.</summary>
        public bool RestoreViewPosition(MarkdownViewPosition position)
        {
            var index = MarkdownViewSwitch.ToBlock(_blocks, position);
            if (index < 0 || index >= _blocks.Count)
                return false;
            RevealBlock(index);
            _panel.ScrollToAnchor(index, index == 0 ? 0 : -8);
            return true;
        }
    }
}

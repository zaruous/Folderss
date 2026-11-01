using Folderss.Services;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Folderss.Viewers
{
    /// <summary>
    /// 블록 편집 ↔ 미리보기 전환(#81). 위치는 페이지(<c>markdown-app.js</c>)가 잡는다:
    /// 미리보기·분할 화면은 화면 맨 위의 제목 앵커, 원문 편집 화면은 맨 위 줄 번호.
    /// </summary>
    public partial class MarkdownViewer : IMarkdownViewSwitchable
    {
        private MarkdownViewPosition _pendingViewPosition;

        public async Task<bool> SaveForSwitchAsync()
        {
            if (!_modified)
                return true;
            if (_disposed || !_webViewReady || !_pageReady || WebView.CoreWebView2 == null)
                return false;
            string content;
            try
            {
                var json = await WebView.CoreWebView2.ExecuteScriptAsync("app.currentContent()");
                content = JsonSerializer.Deserialize<string>(json);
            }
            catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException || ex is System.Runtime.InteropServices.COMException)
            {
                return false;
            }
            return content != null && Save(content);
        }

        public async Task<MarkdownViewPosition> CaptureViewPositionAsync()
        {
            if (_disposed || !_webViewReady || !_pageReady || WebView.CoreWebView2 == null)
                return null;
            try
            {
                var json = await WebView.CoreWebView2.ExecuteScriptAsync("app.getViewPosition()");
                using (var document = JsonDocument.Parse(json))
                {
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                        return null;
                    JsonElement value;
                    var position = new MarkdownViewPosition();
                    if (root.TryGetProperty("heading", out value) && value.ValueKind == JsonValueKind.String)
                        position.HeadingId = value.GetString();
                    if (root.TryGetProperty("line", out value) && value.ValueKind == JsonValueKind.Number)
                        position.Line = Math.Max(0, (int)value.GetDouble());
                    position.PreferLine = root.TryGetProperty("mode", out value) && value.ValueKind == JsonValueKind.String &&
                                          value.GetString() == "edit";
                    return position;
                }
            }
            catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException || ex is System.Runtime.InteropServices.COMException)
            {
                return null;
            }
        }

        public void RestoreViewPositionWhenReady(MarkdownViewPosition position)
        {
            // 페이지가 문서를 연 뒤(CallAppOpen)에 적용한다. 이미 열려 있으면 바로.
            _pendingViewPosition = position;
            if (_webViewReady && _pageReady && _pendingContent == null)
                _ = ApplyPendingViewPositionAsync();
        }

        private async Task ApplyPendingViewPositionAsync()
        {
            var position = _pendingViewPosition;
            _pendingViewPosition = null;
            if (position == null || _disposed || !_webViewReady || !_pageReady)
                return;
            var script = string.Format("app.setViewPosition({0},{1})",
                position.HeadingId != null ? JsonSerializer.Serialize(position.HeadingId) : "null",
                Math.Max(0, position.Line));
            try
            {
                await WebView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.Runtime.InteropServices.COMException)
            {
                // 그사이 탭이 닫혀 WebView2가 해제됐으면 옮길 곳이 없다.
            }
        }
    }
}

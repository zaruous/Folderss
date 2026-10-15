# 마크다운 탭을 닫을 때 WebView2 해제

- 상태: Done

## 요구사항

마크다운 탭을 여러 번 열고 닫아도 WebView2 브라우저 프로세스와 메모리가 쌓이지 않아야 한다.

## 원인 분석 또는 설계

- `MarkdownViewer.Dispose`가 이벤트 구독만 풀고 `WebView`(WPF WebView2 컨트롤)를 해제하지 않았다.
  WPF WebView2는 화면에서 빠져도 스스로 정리되지 않아 창을 닫을 때까지 렌더러가 남는다.
- 해제 뒤 들어올 수 있는 호출(테마 변경 등 `ExecuteScriptAsync`)과, 초기화(`EnsureCoreWebView2Async`)를 기다리는 사이 탭이 닫히는 경우를 막아야 한다.

## 구현 내용

- `Dispose`: `_webViewReady`, `_pageReady`를 내리고 `WebView.Dispose()` 호출. 기존 호출부는 이 플래그를 확인하므로 해제된 컨트롤을 건드리지 않는다.
- `InitializeWebViewAsync`: 초기화 대기 후(성공·실패 모두) `_disposed`면 바로 반환.

## 변경 파일

- `Folderss/Viewers/MarkdownViewer.xaml.cs`
- `docs/items/markdown-viewer-webview-dispose.md`

## 검증

- `dotnet build .\Folderss.sln -c Debug` — 오류 0개.
- 앱에서 확인 필요: 마크다운 탭 10여 개를 열고 닫은 뒤 작업 관리자에서 `msedgewebview2.exe` 프로세스 수가 줄어드는지,
  탭을 열자마자 바로 닫아도 오류가 없는지, 앱 종료 시 오류가 없는지.
- 참고: `MonacoViewer`의 같은 문제는 `monaco-viewer-save-message-dispose.md`에서 고쳤다.

## 변경 이력

- 2026-10-15: 수정 구현, 빌드 확인. 앱 수동 확인 대기.
- 2026-10-15: 사용자 앱 확인 결과 이슈 없음 → Done.

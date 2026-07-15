# Monaco 뷰어 Ctrl+F 검색 충돌

- 상태: Ready for Verification (빌드는 사용자 환경에서 확인 필요)

## 요구사항

Monaco 뷰어(코드/텍스트 편집기)에서 `Ctrl+F`를 누르면 앱 전역 내용 검색(`ShowSearch`, 폴더 패널용)이 먼저 처리되어
Monaco 자체의 파일 내 검색(Find, `actions.find`)이 열리지 않는 문제를 수정한다.

## 원인 분석 또는 설계

`MainWindow.Window_PreviewKeyDown`에서 `ShowSearch` 단축키가 `TryHandleActiveViewerShortcut` 이후이긴 하나,
`MonacoViewer`가 `IViewerShortcutHandler`를 구현하지 않아 `TryHandleActiveViewerShortcut`가 항상 `false`를 반환했다.
그 결과 실행 흐름이 전역 `ShowSearch` 처리 분기까지 내려가 `ShowSearchPanel()`이 호출되고 `e.Handled = true`로 처리되어
WebView2(Monaco)가 네이티브 키다운을 받지 못했다.
`MarkdownViewer`가 동일한 문제를 `IViewerShortcutHandler.HandleShortcut`에서 `ShowSearch`를 가로채 `app.openFind()`를
직접 호출하는 방식으로 해결한 선례(`docs/items/markdown-panel-ctrl-f-search.md`)를 그대로 따른다.

## 구현 내용

- `MonacoViewer`가 `IViewerShortcutHandler`를 구현하도록 추가했다.
- `HandleShortcut`에서 `ShowSearch` 단축키를 가로채, WebView에 포커스를 준 뒤 `app.openFind()`를 호출하고 `true`를 반환한다.
- `monaco-app.html`의 `app` 객체에 `openFind`를 추가해 에디터에 포커스를 준 뒤 Monaco 내장 액션 `actions.find`를 실행한다.

## 변경 파일

- `Folderss/Viewers/MonacoViewer.xaml.cs`
- `Folderss/Viewers/Resources/monaco-app.html`
- `docs/items/monaco-viewer-ctrl-f-search-conflict.md`

## 검증

- 이 세션은 Linux 원격 실행 환경이라 .NET SDK가 없어 `dotnet build`를 직접 실행하지 못했다.
- Windows 개발 환경에서 `dotnet build .\Folderss.sln -c Debug` 실행 및 Monaco 뷰어 포커스 상태에서 `Ctrl+F` 동작 확인 필요.

## 변경 이력

- 2026-07-15: 원인 분석 및 수정 구현. 빌드 검증은 Windows 환경에서 별도 확인 필요.

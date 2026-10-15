# Monaco 뷰어 저장 안전성·메시지 파싱·WebView2 해제

- 상태: Done

## 요구사항

마크다운 뷰어에서 고친 세 가지 문제가 Monaco(코드) 뷰어에도 같은 방식으로 있으므로 함께 고친다.

1. 저장 도중 실패해도 원본 파일이 사라지지 않아야 한다.
2. WebView 메시지를 JSON 파서로 읽어야 한다.
3. 탭을 닫으면 WebView2를 해제해야 한다.

## 원인 분석 또는 설계

- `MonacoViewer.Save`가 `File.Delete(원본)` → `File.Move(임시, 원본)` 순서로 교체해, 이동이 실패하면 원본이 없어졌다.
- `OnWebMessageReceived`가 직접 만든 `ExtractJsonString`으로 JSON을 훑었다(이 구현은 `\uXXXX`는 처리했지만, 키 순서·공백·중첩에 취약).
  `modified` 값도 문자열 포함 검사로 판정했다.
- `Dispose`가 이벤트만 풀고 WPF WebView2 컨트롤을 해제하지 않아, 탭을 닫아도 렌더러가 창을 닫을 때까지 남았다.
- 마크다운 뷰어와 같은 방법으로 고친다(`markdown-viewer-save-and-message-parsing.md`, `markdown-viewer-webview-dispose.md`).

## 구현 내용

- `Save`: `SettingsFile.WriteAllText(_filePath, content, _encoding)`(임시 파일 → 덮어쓰기 이동).
- `OnWebMessageReceived`: `JsonDocument.Parse` + `GetJsonString`. `ExtractJsonString` 삭제.
- `Dispose`: `_webViewReady = false` 후 `WebView.Dispose()`.
- `InitializeWebViewAsync`: 초기화를 기다린 뒤(성공·실패 모두) `_disposed`면 바로 반환.

## 변경 파일

- `Folderss/Viewers/MonacoViewer.xaml.cs`
- `docs/items/monaco-viewer-save-message-dispose.md`

## 검증

- `dotnet build .\Folderss.sln -c Debug` — 오류 0개.
- 앱에서 확인 필요: 코드 파일(`.cs`, `.json` 등) 편집 후 Ctrl+S 저장 → 내용·인코딩 유지, `.tmp`가 남지 않음.
  저장 후 탭 제목의 ` *` 표시가 사라지는지. 코드 탭 여러 개를 열고 닫은 뒤 `msedgewebview2.exe` 수, 탭을 열자마자 닫아도 오류 없는지.

## 변경 이력

- 2026-10-15: 수정 구현, 빌드 확인. 앱 수동 확인 대기.
- 2026-10-15: 사용자 앱 확인 결과 이슈 없음 → Done.

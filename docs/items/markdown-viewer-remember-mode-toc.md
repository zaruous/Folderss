# 마크다운 뷰어 보기 모드·목차 상태 기억

- 상태: Done

## 요구사항

1. 파일을 다시 열면(세션 복원 포함) 그 파일에서 마지막으로 쓰던 보기 모드(Edit/Split/Preview)로 열려야 한다.
2. 목차 표시 여부와 너비를 바꾸면 다음에 여는 문서와 앱 재시작 후에도 유지돼야 한다.

## 원인 분석 또는 설계

- C#이 `app.open`에 항상 `preview`(대용량은 `edit`)를 넘겨, 파일을 열 때마다 Preview로 돌아갔다. 목차 상태도 매번 기본값이었다.
- 상태는 WebView2 페이지의 `localStorage`(`folderss.markdown.prefs`)에 둔다. WebView2 사용자 데이터 폴더(`%LOCALAPPDATA%\Folderss\WebView2Cache`)에
  저장돼 앱을 다시 켜도 유지되고, 모든 마크다운 탭이 같은 출처(`folderss-viewer`)라 공유된다. 설정 창 항목을 늘리지 않는 화면 상태라 C# 설정 파일에 넣지 않았다.
- 모드는 파일 경로별(최근 200개), 목차 표시·너비는 공통. 대용량 파일은 성능 보호를 위해 기억된 모드보다 C#이 정한 Edit를 따른다.

## 구현 내용

- `MarkdownViewer.CallAppOpen`: `app.open`의 다섯 번째 인자로 파일 경로를 넘긴다.
- `markdown-app.html`
  - `loadPrefs`, `updatePrefs`, `rememberMode`, `rememberedMode` 추가(저장소 접근 실패 시 기본값으로 동작).
  - `setMode`(툴바에서 모드 변경)가 현재 파일의 모드를 기억. `app.open`이 기억된 모드를 복원.
  - `toggleToc`가 표시 여부를, 목차 리사이즈 핸들 놓을 때 너비를 저장. 페이지 시작 시 `applySavedLayout`으로 적용.
  - 목차 표시 반영을 `applyTocVisibility`로 묶었다.

## 변경 파일

- `Folderss/Viewers/MarkdownViewer.xaml.cs`
- `Folderss/Viewers/Resources/markdown-app.html`
- `README.md`
- `docs/items/markdown-viewer-remember-mode-toc.md`

## 검증

- 헤드리스 Edge: `a.md`에서 Split으로 바꾸고 목차를 숨기면 저장값 `{"modes":{"C:/docs/a.md":"split"},"tocVisible":false}`.
  다른 파일 `b.md`는 Preview, `a.md`를 다시 열면 Split, 대용량으로 열면 Edit.
- 앱에서 확인 필요: 모드를 바꾼 탭을 닫고 다시 열기, 앱 재시작 후 세션 복원 탭의 모드, 목차 너비 유지.

## 변경 이력

- 2026-10-15: 수정 구현, 헤드리스 테스트 통과. 앱 수동 확인 대기.
- 2026-10-15: 사용자 앱 확인 결과 이슈 없음 → Done.

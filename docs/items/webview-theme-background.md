# WebView2 뷰어 로딩 중 배경색을 테마에 맞추기

- 상태: Ready for Verification

## 요구사항

마크다운·Monaco·Text 뷰어 탭을 열 때, 페이지가 그려지기 전에 흰색이나 다른 테마 색이 잠깐 보였다가 바뀌는 깜빡임을 없앤다.

## 원인 분석 또는 설계

- WebView2는 페이지가 그려지기 전과 페이지 배경이 투명한 동안 컨트롤 기본 배경색(지정하지 않으면 흰색)을 보여 준다.
- 페이지도 테마를 받기 전(`app.open` 전)에는 고정 색으로 그렸다.
  - `markdown-app.html`: `<body class="dark ...">`로 어두운 테마·툴바를 먼저 그림 → 밝은 테마에서 어두운 화면이 먼저 보임.
  - `monaco-app.html`: `body { background: #1e1e1e }` 고정 → 밝은 테마에서도 편집기가 뜨기 전 어두운 화면.
  - `text-app.html`: `<body class="dark">`.
- C#이 현재 테마를 알고 있으므로 WebView 기본 배경색(`WebView2.DefaultBackgroundColor`)을 테마 색으로 지정하고,
  페이지는 테마를 받기 전에는 배경을 비워(투명) 그 색이 보이게 한다.

## 구현 내용

- `MarkdownViewer`, `MonacoViewer`, `TextViewer`의 `ApplyTheme`에서 `WebView.DefaultBackgroundColor = PageBackground(theme)`.
  `PageBackground`는 각 페이지의 테마 배경과 같은 색(마크다운·Text는 테마별 `--bg`, Monaco는 `folderss-dark` #0e1013 / `folderss-light` #ffffff).
  `ViewerHost`가 `Load` 직후 `ApplyTheme`을 부르므로 WebView 초기화 전부터 적용된다.
- `markdown-app.html`: 처음 `body` 클래스를 `theme-pending`으로 두고 이때는 배경 투명·`#mdv-root` 숨김. `app.open`이 테마 클래스를 붙이면 풀린다.
- `monaco-app.html`: `body` 배경을 투명으로(편집기는 자기 배경을 그림).
- `text-app.html`: 처음 클래스를 `theme-pending`으로, `body` 배경 기본값을 투명(`var(--bg, transparent)`)으로.

## 변경 파일

- `Folderss/Viewers/MarkdownViewer.xaml.cs`, `MonacoViewer.xaml.cs`, `TextViewer.xaml.cs`
- `Folderss/Viewers/Resources/markdown-app.html`, `monaco-app.html`, `text-app.html`
- `docs/items/webview-theme-background.md`

## 검증

- `dotnet build .\Folderss.sln -c Debug` — 오류 0개.
- 앱에서 확인 필요: 밝은 테마(Light·GitHub)와 어두운 테마에서 각각 마크다운·코드·텍스트 탭을 열 때 다른 색이 먼저 비치지 않는지,
  열린 뒤 화면(툴바·본문·편집기)이 이전과 같은지, HTML·PDF 내보내기 배경이 이전과 같은지.

## 변경 이력

- 2026-10-16: 구현, 빌드 확인. 앱 확인 대기.

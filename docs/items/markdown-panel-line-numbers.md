# 마크다운 패널 라인번호 표시

- 상태: Ready for Verification

## 요구사항

마크다운 패널의 Edit/Split 모드 편집 영역에 라인번호를 표시한다.

## 원인 분석 또는 설계

`MarkdownViewer`의 편집 영역(`markdown-app.html`의 `#editor-textarea`)은 일반 `<textarea>`라서 라인번호 표기 기능이 없었다.
`<textarea>`는 소프트 랩(자동 줄바꿈) 경계를 JS에서 조회할 수 있는 API가 없어, 자동 줄바꿈을 켜둔 채로는 라인번호와 실제 줄을 정확히 맞출 수 없다.
따라서 편집 영역의 자동 줄바꿈을 끄고(`wrap="off"`, 가로 스크롤 허용) 논리적 줄과 시각적 줄을 1:1로 고정한 뒤, 왼쪽에 별도의 라인번호 거터(gutter)를 두고 편집 영역과 세로 스크롤을 동기화하는 방식으로 구현했다.

## 구현 내용

- `#editor-pane`을 세로 배치에서 가로 배치로 변경하고 `#editor-gutter`(라인번호 표시 영역)를 `#editor-textarea` 앞에 추가했다.
- `#editor-textarea`에 `wrap="off"`를 지정해 자동 줄바꿈을 끄고, 긴 줄은 textarea 자체의 가로 스크롤로 처리한다.
- `updateLineNumbers()`를 추가해 `editor.value`의 줄 수를 계산하고, 줄 수가 바뀔 때만 거터 텍스트와 너비(`ch` 단위, 자릿수 기반)를 갱신한다. 매 입력마다 전체 거터를 다시 그리지 않도록 줄 수가 변하지 않으면 텍스트 갱신을 건너뛴다.
- 편집 영역 `input`, `keydown`(Tab 삽입) 이벤트와 `app.open()`, `app.reloadContent()`에서 `updateLineNumbers()`를 호출해 파일 최초 로드·외부 변경 재로드·직접 입력 시 모두 라인번호가 갱신되도록 했다.
- 편집 영역 `scroll` 이벤트에서 거터의 `scrollTop`을 편집 영역과 동기화해 세로 스크롤 시 라인번호가 항상 해당 줄과 나란히 보이도록 했다.
- Preview 모드에서는 기존과 동일하게 `#editor-pane` 전체가 숨겨지므로 거터도 함께 숨겨진다.

## 변경 파일

- `Folderss/Viewers/Resources/markdown-app.html`
- `README.md`
- `docs/items/markdown-panel-line-numbers.md`

## 검증

- Chromium(Playwright)으로 `markdown-app.html`을 직접 로드해 검증:
  - 200줄 문서를 `app.open()`으로 로드 후 거터 줄 수가 200으로 일치.
  - 편집 영역 `scrollTop`을 500으로 설정하면 거터 `scrollTop`도 500으로 동기화.
  - 커서를 끝으로 이동해 새 줄을 입력하면 거터 줄 수가 201로 갱신.
  - 거터 너비가 자릿수에 맞춰 `ch` 단위로 자동 조정됨을 확인.
  - Split/Edit 모드에서 다크·라이트 테마 스크린샷으로 라인번호와 본문 줄이 정확히 정렬됨을 시각 확인.
- 이 원격 실행 환경은 Linux 기반이라 `dotnet build`(net8.0-windows, WPF)를 직접 수행할 수 없다. C#/XAML 변경은 없고 `markdown-app.html`(WebView2에 로드되는 정적 리소스)만 수정했으므로 Windows 환경에서 `dotnet build .\Folderss.sln -c Debug`로 최종 빌드 확인이 필요하다.

## 변경 이력

- 2026-07-06: 요청 접수, 로컬 아이템 문서 생성.
- 2026-07-06: `markdown-app.html`에 라인번호 거터 구현 및 Chromium 기반 렌더링·스크롤 동기화 검증 완료. Windows 빌드 검증은 대기 중.

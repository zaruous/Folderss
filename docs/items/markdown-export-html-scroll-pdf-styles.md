# 마크다운 패널 내보내기 버그 수정 (HTML 스크롤 / PDF 스타일 누락)

- 상태: Ready for Verification

## 요구사항

1. 마크다운 패널에서 HTML로 내보낸 파일을 브라우저로 열면 스크롤이 되지 않는다 → 스크롤 가능해야 한다.
2. PDF로 내보내면 테이블 선(테두리)과 코드 블록 등 스타일이 누락된다 → 미리보기와 유사한 스타일로 출력돼야 한다.

## 원인 분석 또는 설계

### HTML 내보내기 스크롤 없음

- `buildStandaloneHtml()`(`markdown-app.html`)이 `renderedContentStyles()`로 페이지의 모든 CSS 규칙을
  내보낸 문서에 그대로 복사하는데, 여기에 앱 레이아웃용 `html, body { height: 100%; overflow: hidden; }`
  리셋이 포함되어 내보낸 문서 전체의 스크롤이 차단됐다.
- 복사 시 `#preview-pane` → `.operation-preview` 치환 때문에 `#preview-pane::after`(하단 45vh 여백)와
  `overflow-y: auto`도 함께 딸려 들어왔다.
- `operationStyles()`는 복사된 CSS **뒤에** 삽입되므로, 같은 특이도의 나중 규칙으로 덮어쓰면 해결된다.

### PDF 내보내기 스타일 누락

- PDF는 `PrintToPdfAsync`(print 미디어) 기반이고, 인쇄 시에는 미리보기 DOM을 `#operation-surface`의
  `.operation-preview`로 복제해 출력한다. 그런데 마크다운 프로즈 스타일(테이블 테두리, 코드 블록,
  인용문 등)이 전부 `#preview-pane` ID 스코프라서 복제된 콘텐츠에는 아무 스타일도 적용되지 않았다.
- 추가로 WebView2 `PrintToPdfAsync`의 기본 인쇄 설정은 배경을 인쇄하지 않아(`ShouldPrintBackgrounds=false`)
  표 헤더/코드 블록 배경이 PDF에서 빠졌다.

## 구현 내용

- `markdown-app.html` `@media print` 블록 — `#operation-surface .operation-preview` 스코프의
  인쇄용(흑백 지면 기준) 프로즈 스타일 추가: 제목, 표(테두리/헤더 배경), 코드 블록(`pre`/`code`),
  인용문, 목록, 수평선, front-matter 박스. TOC 복제본(`.operation-toc .toc-item`)의 링크·들여쓰기
  스타일도 추가. `print-color-adjust: exact`로 배경색 보존.
- `markdown-app.html` `operationStyles()` — 내보낸 HTML에서 `html,body{height:auto;overflow:auto}`,
  `.operation-preview{overflow:visible}`, `.operation-preview::after{content:none}`으로 복사된 앱
  레이아웃 규칙을 무효화. 아울러 하드코딩된 라이트 색상들을 `var(--border,#ddd)` 형태의 테마 변수
  기반으로 바꿔, 다크 계열 테마로 내보내도 본문 색(`#24292f`)이 다크 배경과 충돌하지 않게 정리.
- `MarkdownViewer.xaml.cs` `ExportPdfAsync()` — `CreatePrintSettings()`로 `ShouldPrintBackgrounds=true`
  설정 후 `PrintToPdfAsync(path, printSettings)` 호출.

## 변경 파일

- `Folderss/Viewers/Resources/markdown-app.html`
- `Folderss/Viewers/MarkdownViewer.xaml.cs`

## 검증

- [ ] 빌드 성공 확인 (`dotnet build .\Folderss.sln -c Debug`) — 이번 작업 환경(Linux)에는 dotnet SDK가 없어 미실행
- [ ] 표/코드 블록/TOC가 있는 md를 HTML로 내보내기 → 브라우저에서 세로 스크롤 동작 확인
- [ ] 다크/라이트 테마 각각에서 HTML 내보내기 → 본문 색과 배경이 테마와 일치하는지 확인
- [ ] 같은 md를 PDF로 내보내기 → 테이블 테두리, 표 헤더 배경, 코드 블록 배경/테두리, 인용문 좌측 선 표시 확인
- [ ] 우클릭 `인쇄`(printContent)에서도 동일한 스타일 적용 확인 (같은 print CSS 경로 사용)

## 후속 요구사항 (2026-07-28)

1. HTML 내보내기: 목차가 있으면 마크다운 뷰어처럼 목차를 유지할 것.
2. PDF 내보내기: 목차가 있어도 제거하고 본문만 내보낼 것.

### 구현 내용

- `createOperationSurface(options)` — `{ includeToc: false }` 옵션 추가. `exportPdf()`가 이 옵션으로
  호출해 PDF에서는 목차를 제외. `beforeprint`/`printContent()`(우클릭 인쇄)는 옵션 없이 호출되어
  기존처럼 목차 포함(이벤트 객체가 넘어와도 `includeToc !== false` 판정으로 안전).
- 목차 클론의 앵커는 JS 핸들러(`data-target`) 기반이라 내보낸 HTML에서 동작하지 않으므로,
  복제 시 `href="#h-N"` 실제 앵커로 변환(`active` 클래스 제거 포함). 본문 제목 id(`h-N`)는
  innerHTML 복사로 그대로 유지되므로 네이티브 앵커 이동이 동작.
- `operationStyles()` — `.operation-toc`를 `position:sticky;top:0;max-height:100vh;overflow-y:auto`
  사이드바로 스타일링해 스크롤 중에도 뷰어처럼 목차가 화면에 고정. 들여쓰기(toc-h2~h6),
  말줄임, hover 색상, `html{scroll-behavior:smooth}` 추가.

## 검증 (추가분)

- [ ] 제목이 여러 개인 md를 Preview 모드(목차 표시 상태)에서 HTML로 내보내기 →
      목차 사이드바가 표시되고, 스크롤해도 화면 왼쪽에 고정되는지 확인
- [ ] 내보낸 HTML에서 목차 항목 클릭 → 해당 제목으로 이동하는지 확인
- [ ] 같은 문서를 PDF로 내보내기 → 목차 없이 본문만 출력되는지 확인
- [ ] 우클릭 `인쇄`는 기존대로 목차가 포함되는지 확인 (요구 범위: PDF만 제거)

## 변경 이력

- 2026-07-28: 초기 구현 (HTML 내보내기 스크롤 복원, print/PDF 프로즈 스타일 추가, PDF 배경 인쇄 활성화)
- 2026-07-28: HTML 내보내기 목차 유지(sticky + 실제 앵커) / PDF 내보내기 목차 제거

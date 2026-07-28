# 마크다운 패널 내보내기 버그 수정 (HTML 스크롤 / PDF 스타일 누락)

- 상태: 확인 완료 (2026-07-28, 사용자 검증)

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

## 재수정 (2026-07-28) — PDF에 목차가 여전히 포함되는 버그 / sticky 미동작

### 원인

- **PDF 목차 잔존**: `exportPdf()`가 목차 없는 surface를 만들어도, `PrintToPdfAsync`가 인쇄를
  시작하며 `beforeprint` 이벤트를 발화시키면 `window.addEventListener('beforeprint',
  createOperationSurface)` 리스너가 surface를 **목차 포함으로 재빌드**해 덮어썼다.
- **sticky 미동작**: `operationStyles()`가 `body{overflow:auto}`를 줘서 body가 별도 스크롤
  컨테이너가 되었고, 실제 스크롤은 뷰포트(html)에서 일어나므로 목차의 `position:sticky`가
  전혀 걸리지 않았다(스크롤 시 목차가 그대로 밀려 올라감).

### 구현 내용

- `_printSurfaceReady` 플래그 추가 — `exportPdf()`가 surface 구성 후 true로 설정하고,
  `beforeprint` 리스너는 이 플래그가 서 있으면 재빌드를 건너뛴다.
- `clearOperationSurface()` 도입 — surface 비우기 + 플래그 초기화를 한 곳으로 통일.
  `afterprint`, `buildStandaloneHtml()`(HTML 내보내기 후), `shareContent()`에서 호출하고
  `app.clearOperationSurface`로 노출. C# `ExportPdfAsync`의 finally도 innerHTML 직접 비우기
  대신 이 API를 호출해 플래그가 반드시 초기화되게 함.
- 내보낸 HTML 스크롤을 `html{overflow:auto}` + `body{overflow:visible}`로 변경해 sticky가
  뷰포트 기준으로 동작하도록 수정.

### 검증 (Playwright + Chromium 자동 테스트, 17/17 통과)

- [x] 내보낸 HTML: 문서 스크롤 가능, 목차 `position:sticky` 적용, 중간까지 스크롤 후에도
      목차가 화면 안에 고정(top:0), 목차 클릭 시 해당 제목으로 이동, 표 테두리 적용
- [x] exportPdf 직후 surface에 목차 없음 + 본문 있음, **beforeprint 발화 후에도 목차 없음**
- [x] print 미디어에서 표 테두리와 surface 표시 확인, 실제 PDF 생성 확인
- [x] `clearOperationSurface()` 후 일반 인쇄(beforeprint)는 기존대로 목차 포함,
      afterprint 후 surface 비워짐
- [x] Windows 실빌드에서 수동 확인 — 사용자 확인 완료 (2026-07-28)

## 변경 이력

- 2026-07-28: 초기 구현 (HTML 내보내기 스크롤 복원, print/PDF 프로즈 스타일 추가, PDF 배경 인쇄 활성화)
- 2026-07-28: HTML 내보내기 목차 유지(sticky + 실제 앵커) / PDF 내보내기 목차 제거
- 2026-07-28: beforeprint 재빌드로 PDF에 목차가 남던 버그 수정, body overflow로 sticky가 깨지던 문제 수정 (브라우저 자동 테스트 추가 검증)

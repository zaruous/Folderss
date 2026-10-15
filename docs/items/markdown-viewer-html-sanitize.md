# 마크다운 뷰어 본문 HTML 정화 및 링크 실행 확인

- 상태: Done

## 요구사항

마크다운 뷰어에서 외부에서 받은 `.md` 파일을 열거나 링크를 클릭했을 때 로컬 프로그램·스크립트가 실행되거나
열린 파일이 덮어써지지 않도록 막는다.

1. 마크다운 본문에 섞인 HTML의 스크립트(`<img onerror=...>`, `<script>`, `javascript:` 링크 등)가 실행되지 않아야 한다.
2. 링크가 실행 파일(`.exe`, `.bat`, `.ps1`, `.lnk` 등)을 가리키면 바로 실행하지 말고 확인을 받아야 한다.

## 원인 분석 또는 설계

- `markdown-app.html`이 `marked.parse()` 결과를 그대로 `innerHTML`에 넣는다. marked v11에는 HTML 정화 기능이 없고,
  `marked.use({ allowDangerousHtml: false })`는 marked에 없는 옵션이라 아무 효과가 없었다.
- 페이지 CSP가 `script-src 'unsafe-inline'`이라 인라인 이벤트 핸들러가 실행된다. 실행된 스크립트는
  `chrome.webview.postMessage`로 `open-link`(로컬 파일 `Process.Start(UseShellExecute)`)나 `save-request`(열린 파일 덮어쓰기)를 보낼 수 있다.
  → 받은 문서를 미리보기로 열기만 해도 임의 프로그램이 실행될 수 있었다.
- 일반 링크 `[설치](./setup.bat)`도 클릭 한 번에 확인 없이 실행됐다.
- CSP에서 `'unsafe-inline'`을 빼는 방식은 툴바의 `onclick` 속성과 인라인 `<script>`를 모두 옮겨야 해서 범위가 커진다.
  렌더링 결과를 DOMPurify로 거르는 방식을 택했다.
- 실행 형식 판정은 폴더 비교 창이 이미 쓰는 `FileOperationService.IsExecutable`과 같은 확인창 패턴을 재사용한다.

## 구현 내용

- `Viewers/Resources/purify.min.js` 추가 (DOMPurify 3.2.6, jsdelivr·cdnjs 두 곳의 SHA-256 일치 확인:
  `89e1fa7647cb495370d3a997ace4387f5d15d9f4c5af12352c53daa400956287`). `Viewers\Resources\**\*.*` Content 와일드카드로 출력 폴더에 복사된다.
- `markdown-app.html`
  - 효과 없던 `allowDangerousHtml` 옵션 제거.
  - `sanitizeHtml()` 추가: `DOMPurify.sanitize(html, SANITIZE_OPTIONS)`. `renderPreview`에서 `marked.parse` 결과에 적용.
  - `ALLOWED_URI_REGEXP`는 DOMPurify 기본값에 `file:`와 Windows 드라이브 경로(`C:\`, `C:/`, marked가 인코딩한 `C:%5C`)를 더해 로컬 절대 경로 링크를 유지한다.
- `MarkdownViewer.xaml.cs`
  - `HandleLinkClick`: `.md`가 아닌 로컬 파일이 `FileOperationService.IsExecutable`이면 `ConfirmRunExecutable` 확인창(경고 아이콘, 기본값 "아니요")을 띄우고, "예"일 때만 실행한다.

## 변경 파일

- `Folderss/Viewers/Resources/purify.min.js` (신규)
- `Folderss/Viewers/Resources/markdown-app.html`
- `Folderss/Viewers/MarkdownViewer.xaml.cs`
- `docs/architecture.md`
- `docs/items/markdown-viewer-html-sanitize.md`

## 검증

- `dotnet build .\Folderss.sln -c Debug` — 오류 0개, Exit 0.
- 헤드리스 Edge에서 번들된 `marked.min.js` + `purify.min.js`와 같은 설정으로 렌더링 테스트:
  - 차단 확인: `<img src=x onerror>`, `<script>`, `[a](javascript:...)`, `<svg>` 안 `javascript:` 링크·스크립트, `<iframe>`, `<details ontoggle>` → 이벤트 속성·스크립트·위험 URI가 모두 제거되고 실행된 스크립트 없음.
  - 유지 확인: 상대 경로·`../`·`#앵커`·`https`·`mailto`·`file:///`·`C:\`·`C:/`·공백 포함 `<C:\my docs\a.md>` 링크, `folderss-doc-asset`·`data:` 이미지, 작업 목록 체크박스, 표, 코드 블록 `language-*` 클래스, `<kbd>`·`<b>` 같은 일반 HTML.
- 앱에서 직접 확인 필요:
  - 위 공격 예시를 넣은 `.md`를 열었을 때 아무것도 실행되지 않는지
  - Mermaid·KaTeX·코드 강조가 이전과 같이 표시되는지
  - `[x](./run.bat)` 클릭 시 확인창이 뜨고 "아니요"면 실행되지 않는지, `.png`·`.pdf`·폴더 링크는 확인 없이 열리는지

## 변경 이력

- 2026-10-15: 원인 분석 및 수정 구현, 빌드·렌더링 테스트 완료. 앱 수동 확인 대기.
- 2026-10-15: 후속으로 CSP `'unsafe-inline'` 제거(`markdown-viewer-csp-no-inline-script.md`), 본문 HTML `<img>` 경로 처리(`markdown-viewer-raw-html-img.md`).
- 2026-10-15: 사용자 앱 확인 결과 이슈 없음 → Done.

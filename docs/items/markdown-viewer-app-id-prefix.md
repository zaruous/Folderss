# 마크다운 제목 앵커가 앱 화면 요소 id와 겹치는 문제 해결

- 상태: Done

## 요구사항

`## Main`, `## Toolbar` 같은 제목도 GitHub와 같은 앵커(`#main`, `#toolbar`)를 가져야 하고, 앱 화면 스타일·동작과 충돌하지 않아야 한다.

## 원인 분석 또는 설계

- 페이지의 앱 요소 id(`root`, `toolbar`, `main`, `preview-pane` 등)가 소문자라 같은 제목 슬러그와 겹쳤다.
  겹치면 앱 CSS(`#toolbar { display:flex; height:34px }` 등)가 제목에 적용되고 `getElementById`가 엉뚱한 요소를 찾으므로,
  이전 수정(`markdown-viewer-heading-anchor-links.md`)에서는 이런 제목에 `-1`을 붙여 피했다(GitHub와 다른 앵커).
- 제목 슬러그는 항상 소문자이므로, 소문자로만 된 앱 요소 id에 `mdv-` 접두사를 붙이면 일반 문서 제목과 겹칠 일이 없다.
  대소문자가 섞인 id(`btnEdit`, `findBar` 등)는 소문자 슬러그와 겹칠 수 없어 그대로 둔다.

## 구현 내용

- `markdown-app.html`(HTML·CSS), `markdown-app.js`에서 `root`, `toolbar`, `main`, `toc-panel`, `toc-handle`, `content-area`,
  `editor-pane`, `editor-gutter`, `editor-textarea`, `split-handle`, `preview-pane`, `operation-surface`를 `mdv-` 접두사 id로 바꿨다(HTML 113곳, JS 35곳).
- HTML 내보내기의 CSS 선택자 변환(`#mdv-preview-pane` → `.operation-preview`)도 함께 바뀌었다.
- `uniqueSlug`의 앱 요소 충돌 검사는 안전장치로 남겼다(`mdv-` 접두사 제목에만 해당).

## 변경 파일

- `Folderss/Viewers/Resources/markdown-app.html`
- `Folderss/Viewers/Resources/markdown-app.js`
- `docs/items/markdown-viewer-app-id-prefix.md`

## 검증

- CSP 적용 헤드리스 Edge: `# Main` → `main`, `## Toolbar` → `toolbar`(GitHub와 같음), `#main` 제목의 `display`는 `block`(앱 레이아웃 스타일 미적용),
  `#mdv-root`·`#mdv-toolbar` 정상, 툴바·검색·목차·내보내기 정상, HTML 내보내기에 `.operation-preview h1` 스타일 포함.
- 앱에서 확인 필요: 화면 배치(툴바·목차·편집기·분할 핸들), 인쇄·PDF 출력 모양이 이전과 같은지.

## 변경 이력

- 2026-10-15: 구현, 헤드리스 테스트 통과. 앱 수동 확인 대기.
- 2026-10-15: 사용자 앱 확인 결과 이슈 없음 → Done.

# 마크다운 편집 중 Mermaid 다이어그램 다시 그리기 줄이기

- 상태: Done

## 요구사항

Edit·Split 모드에서 글자를 입력할 때마다 문서의 모든 Mermaid 다이어그램이 다시 그려지며 깜빡이고 느려지는 문제를 줄인다.

## 원인 분석 또는 설계

- 입력 300ms 뒤 `renderPreview`가 미리보기 전체를 새로 만들고, 바뀌지 않은 다이어그램까지 `mermaid.run`으로 다시 그렸다.
- "테마 + 다이어그램 원문"을 키로 그린 SVG를 기억해 두고, 같은 키면 SVG를 바로 넣고 `mermaid.run` 대상에서 뺀다.
- `app.open`이 `mermaid.initialize`(테마)를 렌더링 뒤에 불러 첫 화면이 이전 테마 색으로 그려지던 문제도 함께 고쳤다(캐시에 잘못된 색이 남지 않도록).

## 구현 내용

- `markdown-app.html`
  - `_mermaidCache`(Map, 100개 넘으면 비움), `mermaidTheme()` 추가.
  - `renderPreview`: 캐시가 있으면 SVG를 넣고 `data-processed` 표시, 없으면 `mermaid.run` 후 SVG가 생긴 노드만 캐시에 저장.
  - `app.open`: `mermaid.initialize`를 `renderPreview` 앞으로 옮겼다.

## 변경 파일

- `Folderss/Viewers/Resources/markdown-app.html`
- `docs/items/markdown-viewer-mermaid-cache.md`

## 검증

- 헤드리스 Edge: 첫 렌더링 후 SVG 생성·캐시 1개, 다시 `renderPreview` 하면 비동기 대기 없이 즉시 SVG가 표시됨.
- 앱에서 확인 필요: 다이어그램이 있는 문서를 Split 모드로 편집할 때 다이어그램이 깜빡이지 않는지, 다이어그램 원문을 고치면 바로 반영되는지, 테마 변경 시 색이 바뀌는지.

## 변경 이력

- 2026-10-15: 수정 구현, 헤드리스 테스트 통과. 앱 수동 확인 대기.
- 2026-10-15: 사용자 앱 확인 결과 이슈 없음 → Done.

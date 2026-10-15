# 마크다운 뷰어 검색 개수·이동 불일치 수정

- 상태: Done

## 요구사항

Preview·Split 모드에서 검색 결과 개수("3 / 10")와 화면에 하이라이트된 개수가 달라 다음/이전 이동이 엉키는 문제를 고친다.
Mermaid 다이어그램과 KaTeX 수식 안의 글자를 하이라이트하다가 렌더링이 깨지지 않아야 한다.

## 원인 분석 또는 설계

- 개수와 이동 위치는 편집기 원문(`editor.value`)에서 셌고, 하이라이트는 렌더링된 미리보기 텍스트에서 찾았다.
  `**굵게**`, 링크 URL, 코드 펜스 정보 등은 원문에만 있어 두 개수가 달라지고, 같은 `_findIndex`로 두 배열을 가리켜 어긋났다.
- 하이라이트가 SVG(Mermaid)·`.katex` 안의 텍스트 노드에도 `<mark>`를 넣어 렌더링을 깨뜨릴 수 있었다.
- 기준을 한쪽으로 고정한다: Preview는 렌더링된 하이라이트, Edit·Split은 편집기 원문. Split의 미리보기 하이라이트는 표시만 한다.

## 구현 내용

- `markdown-app.html`
  - `updateFindHighlights`: Preview면 `_findMatches = _findHits`, 아니면 원문 일치 목록. 첫 결과 활성화는 Preview에서만.
  - `setActiveFindHit`: Preview면 미리보기 하이라이트 이동, 아니면 편집기 선택.
  - `highlightTextNodes`: `svg`, `.mermaid-wrap`, `.katex` 안은 제외.

## 변경 파일

- `Folderss/Viewers/Resources/markdown-app.html`
- `docs/items/markdown-viewer-find-preview-count.md`

## 검증

- 헤드리스 Edge에서 실제 `markdown-app.html` 로드 후 확인: 원문에 `bold`가 5번(굵게·일반·링크 URL·Mermaid·KaTeX), 화면 글자에 2번 있는 문서에서
  Preview `1 / 2`, Edit `1 / 5`(다음 이동 시 편집기 선택 위치 정상), Split `1 / 5`, Mermaid·KaTeX 안 `<mark>` 0개.
- 앱에서 확인 필요: Ctrl+F 후 세 모드에서 Enter/Shift+Enter 이동.

## 변경 이력

- 2026-10-15: 수정 구현, 헤드리스 테스트 통과. 앱 수동 확인 대기.
- 2026-10-15: 사용자 앱 확인 결과 이슈 없음 → Done.

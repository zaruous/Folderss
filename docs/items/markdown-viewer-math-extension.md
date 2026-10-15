# 마크다운 수식 처리 개선 (금액 오인식·밑줄 깨짐)

- 상태: Done

## 요구사항

1. "$5 and $10" 같은 금액 표기가 수식으로 바뀌지 않아야 한다.
2. `$a_1 + b_1$`처럼 수식 안의 `_`, `*`, `\`가 마크다운 강조·이스케이프로 바뀌어 수식이 깨지지 않아야 한다.

## 원인 분석 또는 설계

- KaTeX `auto-render`가 marked가 만든 HTML 위에서 `$`를 찾아, 짝이 맞는 `$` 사이를 모두 수식으로 처리했다.
- marked가 먼저 처리하므로 수식 안의 `_1 + b_` 가 `<em>`으로 바뀌고, `\(`, `\[`는 마크다운 이스케이프로 `\`가 사라져 이 구분자들은 사실상 동작하지 않았다.
- marked 확장(`mathBlock`, `mathInline`)으로 수식 구간을 먼저 떼어 `data-tex`에 원문을 담고, DOMPurify 정화 뒤 `katex.render`로 그린다.
  `$…$`는 Pandoc 규칙(여는 `$` 뒤·닫는 `$` 앞이 공백이 아니고, 닫는 `$` 뒤에 숫자가 없음)을 따른다.

## 구현 내용

- `markdown-app.html`
  - `mathBlock`(블록): `$$…$$`, `\[…\]` → `<div class="math-display" data-tex>`.
  - `mathInline`(인라인): `$$…$$`, `\[…\]` → `<span class="math-display">`, `$…$`, `\(…\)` → `<span class="math-inline">`.
  - `renderMath`: `[data-tex]` 요소를 `katex.render(..., { displayMode, throwOnError: false })`. 실패하면 원문 수식이 그대로 보인다.
  - `renderMathInElement` 호출과 `katex-auto-render.min.js` 로드 제거, 파일 삭제.
- 코드 스팬·코드 블록 안의 `$`, `\$` 이스케이프는 marked 기본 처리대로 글자 그대로 남는다.

## 변경 파일

- `Folderss/Viewers/Resources/markdown-app.html`
- `Folderss/Viewers/Resources/katex-auto-render.min.js` (삭제)
- `README.md`
- `docs/items/markdown-viewer-math-extension.md`

## 검증

- 헤드리스 Edge에서 실제 `markdown-app.html`로 확인:
  - `$a_1 + b_1$` → 인라인 수식, 안에 `<em>` 없음. `\(x^2\)` → 인라인 수식.
  - `$$\n\sum_{i=1}^n i\n$$` → 블록 수식(`.katex-display`). 문단 안 `$$y$$` → `<p>` 안의 디스플레이 수식.
  - `money $5 and $10 here`, `\$5 and $x$5`, `` `$x$` `` → 글자 그대로.
- 앱에서 확인 필요: 기존 문서의 수식 표시, PDF·HTML 내보내기에서 수식 표시.

## 변경 이력

- 2026-10-15: 수정 구현, 헤드리스 테스트 통과. 앱 수동 확인 대기.
- 2026-10-15: 사용자 앱 확인 결과 이슈 없음 → Done.

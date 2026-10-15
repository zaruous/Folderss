# 마크다운 뷰어 Mermaid·KaTeX·highlight.js 필요할 때만 불러오기

- 상태: Ready for Verification

## 요구사항

마크다운 탭을 열 때 문서에 필요 없는 큰 라이브러리를 읽지 않아 첫 로딩과 메모리를 줄인다.

## 원인 분석 또는 설계

- 페이지가 열릴 때마다 Mermaid(3.2MB), KaTeX(271KB), highlight.js(120KB)를 모두 읽고 실행했다. 다이어그램·수식·코드가 없는 문서도 같다.
- 헤드리스 Edge 측정: 스크립트 준비 약 2.0초·JS 힙 15MB → 지연 로딩 시 약 0.85초·1MB.
- 렌더링 후 문서에 코드 블록(`pre code`), 수식(`[data-tex]`), Mermaid 블록이 있을 때만 해당 스크립트를 `<script>`로 추가해 불러온다(CSP `script-src 'self'`로 허용).
  한 번 불러오면 이후 렌더링은 동기로 처리해 기존처럼 깜빡임 없이 그린다.

## 구현 내용

- `markdown-app.html`: `mermaid.min.js`, `katex.min.js`, `highlight.min.js` 고정 로드 제거.
- `markdown-app.js`
  - `LAZY_LIBS`, `loadLib`(한 번만 불러옴, 실패 시 다음 렌더링에서 재시도), `withLib(name, renderId, fn)`(이미 있으면 즉시, 없으면 불러온 뒤 실행).
  - `_renderSeq`로 불러오는 사이 다시 그려졌으면 지난 결과에는 적용하지 않는다. 늦게 적용된 뒤 검색 중이면 하이라이트를 다시 칠한다.
  - `configureMermaid()`: Mermaid를 불러온 직후와 테마 변경 시 테마 설정(아직 안 불러왔으면 건너뜀).
  - `renderPreview`: 코드 강조·Mermaid·수식 처리를 각각 `withLib`로 감쌌다. 코드 강조 대상에서 Mermaid 코드 블록은 뺐다.

## 변경 파일

- `Folderss/Viewers/Resources/markdown-app.html`
- `Folderss/Viewers/Resources/markdown-app.js`
- `docs/architecture.md`
- `docs/items/markdown-viewer-lazy-libs.md`

## 검증

- CSP를 적용한 헤드리스 Edge(로컬 HTTP 서버):
  - 일반 문서: 추가 스크립트 없음(`marked`, `purify`, `markdown-app.js`만).
  - 코드·수식·Mermaid 문서: 처음엔 미적용 → 불러온 뒤 모두 적용, 이후 다시 그리기는 즉시(동기) 적용, 검색 `1 / 1` 정상, 테마 변경 후 Mermaid 정상, CSP 위반 없음.
- 앱 재시작 3회 측정(`README.md` 탭, SmartScreen 평판 검사를 끈 상태에서 비교): 고정 로드 약 1,415ms → 지연 로딩 약 1,285ms
  (페이지 준비 370 → 320ms, 첫 렌더링 110 → 30ms). 다이어그램이 없는 문서는 탭당 메모리 약 12MB 절약(헤드리스 측정).
- 알려진 제약: 코드·수식·다이어그램이 있는 문서를 처음 열 때 해당 부분이 잠깐 늦게(라이브러리를 읽는 동안) 적용된다.

## 변경 이력

- 2026-10-16: 구현, 헤드리스·앱 측정 완료. 앱 체감 확인 대기.

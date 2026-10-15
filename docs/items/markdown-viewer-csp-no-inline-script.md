# 마크다운 뷰어 CSP에서 인라인 스크립트 허용 제거

- 상태: Done

## 요구사항

마크다운 뷰어 페이지의 CSP에서 `script-src 'unsafe-inline'`을 빼, 정화(DOMPurify)를 우회하는 HTML이 있더라도 인라인 스크립트·이벤트 속성이 실행되지 않게 한다(심층 방어).

## 원인 분석 또는 설계

- 페이지 스크립트 전체가 `markdown-app.html` 안의 인라인 `<script>`였고, 툴바 버튼이 `onclick="..."` 속성을 써서 `'unsafe-inline'`을 뺄 수 없었다.
- 스크립트를 외부 파일로 옮기고 버튼은 `addEventListener`로 연결한다.
- C#의 `ExecuteScriptAsync`는 페이지 CSP의 영향을 받지 않으므로 `app.*` 호출은 그대로 동작한다.
- 스타일은 KaTeX·Mermaid·미리보기가 인라인 스타일을 쓰므로 `style-src 'unsafe-inline'`은 유지한다.

## 구현 내용

- `markdown-app.html`의 인라인 스크립트를 `Viewers/Resources/markdown-app.js`로 옮겼다(전역 함수·`app` 객체 그대로).
- 툴바·검색 막대·내보내기 메뉴 버튼의 `onclick` 속성을 없애고 id를 붙여, `markdown-app.js` 끝의 "Toolbar wiring"에서 연결한다
  (내보내기 버튼에 `btnExport`, `btnExportHtml`, `btnExportPdf` id 추가).
- CSP: `script-src 'self' https://folderss-viewer` (`'unsafe-inline'` 제거).

## 변경 파일

- `Folderss/Viewers/Resources/markdown-app.html`
- `Folderss/Viewers/Resources/markdown-app.js` (신규)
- `docs/architecture.md`
- `docs/items/markdown-viewer-csp-no-inline-script.md`

## 검증

- CSP를 그대로 적용한 헤드리스 Edge(로컬 HTTP 서버로 제공):
  - Edit/Split/Preview, 검색 열기·닫기, 내보내기 메뉴, 목차 토글 버튼이 모두 동작.
  - 일부러 넣은 `onclick` 속성은 실행되지 않음(`script-src-attr inline` 위반으로 차단) — 그 외 CSP 위반 없음.
  - Mermaid SVG, highlight.js 강조, KaTeX, 목록 이어 쓰기, HTML 내보내기 문자열 생성 정상.
- 앱에서 확인 필요: 툴바 버튼 전부, 오른쪽 메뉴의 인쇄·다른 이름으로 저장·공유, PDF/HTML 내보내기.

## 변경 이력

- 2026-10-15: 구현, CSP 적용 헤드리스 테스트 통과. 앱 수동 확인 대기.
- 2026-10-15: 사용자 앱 확인 결과 이슈 없음 → Done.

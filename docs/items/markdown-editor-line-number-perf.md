# 마크다운 편집기 줄 번호 갱신 비용 줄이기

- 상태: Done

## 요구사항

수 MB 마크다운 문서를 편집할 때 키 입력마다 생기는 지연을 줄인다.

## 원인 분석 또는 설계

`updateLineNumbers`가 입력 이벤트마다 `editor.value.split('\n')`으로 줄 수를 셌다. 줄마다 문자열을 새로 만들어
큰 문서에서는 키 입력마다 큰 배열과 수십만 개의 문자열이 생겼다. 줄 번호 거터는 줄 수가 바뀔 때만 다시 만들므로, 세는 비용만 줄이면 된다.

## 구현 내용

- `markdown-app.html` `countLines`: `indexOf('\n')` 반복으로 개행 수만 센다(새 문자열 없음). `updateLineNumbers`가 이를 사용.

## 변경 파일

- `Folderss/Viewers/Resources/markdown-app.html`
- `docs/items/markdown-editor-line-number-perf.md`

## 검증

- 헤드리스 Edge: `countLines('')` = 1, `countLines('a\nb\n')` = 3 (`split('\n').length`와 같은 값).
- 앱에서 확인 필요: 5MB 가까운 문서를 Edit 모드에서 입력할 때 반응 속도.

## 변경 이력

- 2026-10-15: 구현, 헤드리스 테스트 통과. 앱 수동 확인 대기.
- 2026-10-15: 사용자 앱 확인 결과 이슈 없음 → Done.

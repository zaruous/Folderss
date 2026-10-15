# 테마 변경 시 마크다운 미리보기 스크롤 위치 유지

- 상태: Done

## 요구사항

마크다운 탭이 열린 상태에서 앱 테마를 바꿔도 미리보기가 맨 위로 돌아가지 않아야 한다.

## 원인 분석 또는 설계

`app.setTheme`이 Mermaid 색을 바꾸려고 `renderPreview()`로 미리보기 전체를 다시 그리면서 스크롤 위치가 초기화됐다.
외부 변경 다시 읽기(`reloadContent`)처럼 다시 그리기 전후로 `scrollTop`을 저장·복원한다.

## 구현 내용

- `markdown-app.html` `app.setTheme`: `renderPreview()` 전에 미리보기 `scrollTop`을 저장했다가 복원.

## 변경 파일

- `Folderss/Viewers/Resources/markdown-app.html`
- `docs/items/markdown-viewer-theme-scroll.md`

## 검증

- 앱에서 확인 필요: 긴 문서를 중간까지 내린 뒤 테마 변경 → 위치 유지.
- 알려진 제약: Mermaid는 비동기로 그려져 다이어그램이 많은 문서는 높이가 바뀌며 위치가 약간 어긋날 수 있다. 검색 중이면 첫 결과로 다시 이동한다.

## 변경 이력

- 2026-10-15: 수정 구현. 앱 수동 확인 대기.
- 2026-10-15: 사용자 앱 확인 결과 이슈 없음 → Done.

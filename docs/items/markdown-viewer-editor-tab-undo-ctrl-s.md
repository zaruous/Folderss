# 마크다운 편집기 Tab 입력 후 실행 취소 불가·Preview에서 Ctrl+S 안 됨 수정

- 상태: Done

## 요구사항

1. 편집기에서 Tab을 누른 뒤에도 Ctrl+Z로 이전 편집을 되돌릴 수 있어야 한다.
2. Edit에서 고친 뒤 Preview로 바꿔도 Ctrl+S로 저장돼야 한다.

## 원인 분석 또는 설계

- Tab 처리에서 `editor.value = ...`로 값을 통째로 바꿔 브라우저의 실행 취소 기록이 모두 지워졌다.
  `document.execCommand('insertText')`는 실행 취소 기록에 남고 `input` 이벤트도 발생시켜 수정 표시·줄 번호·미리보기가 그대로 갱신된다.
- Ctrl+S는 편집기 `keydown`에서만 받아, 포커스가 미리보기에 있으면 동작하지 않았다.

## 구현 내용

- `markdown-app.html`
  - Tab(수정자 키 없음): `document.execCommand('insertText', false, '    ')`.
  - Ctrl+S 처리를 편집기에서 문서 전체 `keydown`으로 옮겼다.

## 변경 파일

- `Folderss/Viewers/Resources/markdown-app.html`
- `docs/items/markdown-viewer-editor-tab-undo-ctrl-s.md`

## 검증

- 헤드리스 Edge: Tab 입력 후 `undo`로 원문 복원 확인, `modified` 메시지 1회 전송, Preview 모드에서 Ctrl+S 시 `save-request` 전송 확인.
- 앱에서 확인 필요: 실제 키 입력으로 Tab → Ctrl+Z, Preview에서 Ctrl+S 저장.

## 변경 이력

- 2026-10-15: 수정 구현, 헤드리스 테스트 통과. 앱 수동 확인 대기.
- 2026-10-15: 사용자 앱 확인 결과 이슈 없음 → Done.

# 마크다운 편집기 목록 이어 쓰기·여러 줄 들여쓰기

- 상태: Done

## 요구사항

1. 목록 항목 끝에서 Enter를 누르면 다음 줄에 같은 목록 표시가 자동으로 붙어야 한다(`-`, `*`, `+`, 번호 `1.`/`1)`, 작업 목록 `- [ ]`). 번호는 1 증가.
2. 표시만 있는 빈 항목에서 Enter를 누르면 표시를 지우고 목록을 끝낸다.
3. 여러 줄을 선택하고 Tab을 누르면 각 줄을 들여쓰고, Shift+Tab은 각 줄을 한 단계(공백 최대 4칸 또는 탭 1개) 내어쓴다. 한 줄에서는 Tab이 공백 4칸, Shift+Tab은 그 줄을 내어쓴다.
4. 모든 동작은 Ctrl+Z로 되돌릴 수 있어야 하고, 한글 입력(IME 조합) 중 Enter에는 끼어들지 않아야 한다.

## 원인 분석 또는 설계

- 편집기는 `<textarea>`라 이런 편집 보조가 없었고, Tab은 선택 영역을 공백 4칸으로 덮어썼다.
- 실행 취소 기록을 남기려고 바꿀 범위를 선택한 뒤 `document.execCommand('insertText')`로 바꾼다(`replaceRange`).
- 한글 조합 중 Enter는 조합 확정용이므로 `e.isComposing`(또는 keyCode 229)이면 처리하지 않는다.
- 줄 중간에서 Enter를 누르면(뒤에 글자가 남아 있으면) 기본 줄바꿈을 따른다.

## 구현 내용

- `markdown-app.html`
  - `indentSelection(outdent)`, `continueList()`, `replaceRange`, `lineStartOf` 추가. `LIST_ITEM` 정규식으로 목록 표시 인식.
  - 편집기 `keydown`: Tab/Shift+Tab → `indentSelection`, Enter → `continueList`(처리했을 때만 기본 동작 취소).

## 변경 파일

- `Folderss/Viewers/Resources/markdown-app.html`
- `docs/keyboard-shortcuts.md`
- `README.md`
- `docs/items/markdown-editor-list-indent-helpers.md`

## 검증

- 헤드리스 Edge:
  - `- item`+Enter → `- item\n- `, `9. nine` → `10. `, `  - [x] done` → `  - [ ] `, 빈 항목 `- ` → 표시 삭제.
  - 줄 중간 Enter·일반 문장 Enter·IME 조합 중 Enter → 기본 동작(가로채지 않음).
  - `a\nb\nc`에서 두 줄 선택 후 Tab → `    a\n    b\nc`(선택 유지), Shift+Tab → 원래대로. `      x`에서 Shift+Tab → `  x`.
  - 한 줄 Tab → 공백 4칸, Ctrl+Z로 복원.
- 앱에서 확인 필요: 실제 키보드·한글 입력으로 목록 작성, 여러 줄 들여쓰기 후 Ctrl+Z.

## 변경 이력

- 2026-10-15: 구현, 헤드리스 테스트 통과. 앱 수동 확인 대기.
- 2026-10-15: 사용자 앱 확인 결과 이슈 없음 → Done.

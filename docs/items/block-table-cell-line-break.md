# 블록 편집기 표 셀 안 줄바꿈 (Ctrl+Enter)

- 상태: Ready for Verification — 단위 테스트 통과, Windows 빌드·실제 키 입력 확인 대기
- 시작일: 2026-11-09

## 요구사항

- 블록 편집기(`builtin:blocks`, C# 편집기) 표 셀에서 `Enter`는 지금처럼 아래에 행 추가. `Ctrl+Enter`는 셀 안에서 줄바꿈.
- (사용자 결정) 규칙 #78대로 Markdown에 `<br>` 같은 HTML 태그를 넣지 않는다.

## 설계

- GFM 표 행은 한 줄이라 줄바꿈을 Markdown으로 나타낼 수 없다 → 줄바꿈 자리는 **공백**으로 쓰고, 어느 공백이 줄바꿈인지만 표 행 블록 속성 `cellbreaks`(사이드카)에 둔다.
- `BlockMarkdown.JoinCells`가 이미 셀의 `\n`을 공백으로 바꾼다 — 글자 수가 같아 찾기·서식(Ctrl+B)·커서 위치 계산이 그대로 맞는다. 블록 본문은 늘 Markdown과 같은 글이라 사이드카 블록 해시·최소 변경 저장(#81)에 영향 없음.
- 값 `셀해시:위치,위치;…` — 셀 앞뒤 공백을 뺀 글의 해시(`TextMarks.Hash`)와 그 글 첫 글자 기준 위치.
  - 열 번호에 묶지 않음: 열 추가·삭제·이동에도 따라간다. 같은 글의 두 셀은 같은 줄바꿈을 가진다(글이 같으면 위치도 유효).
  - 셀 글이 다른 경로(다른 편집기, 찾아 바꾸기, 붙여넣기 등)로 바뀌면 해시가 달라 그 셀의 줄바꿈만 버린다 → 공백으로 보인다(글은 잃지 않음).
  - 편집 중 셀 끝·앞의 줄바꿈도 위치(길이 이상·음수)로 남겨 입력란 글이 그대로 돌아오게 함 — 안 그러면 셀 끝에서 Ctrl+Enter 직후 화면 갱신(`SyncCells`)이 줄바꿈을 지운다. Markdown은 셀 앞뒤 공백을 버리므로 다시 읽으면 무시된다.

## 구현

- `Services/TableCellBreaks.cs` — `EditTexts`(본문 + 속성 → 줄바꿈 넣은 셀 글), `Update`(입력란 글 → 속성).
- `Controls/BlockEditor.Table.cs` — `HandleTableKey` Ctrl+Enter → `InsertCellBreak`, `OnCellTextChanged`가 본문(`JoinCells`)과 `cellbreaks`를 함께 갱신.
- `Controls/BlockEditor.Row.cs` — `SyncCells`(표시·입력란)·`EnsureCellEditor`가 `EditTexts` 사용, `CellEditTexts()` 추가. 표시는 `BlockInlineRenderer`가 이미 `\n`을 줄바꿈으로 그림.
- `Controls/BlockEditor.cs` — 한글 조합 중 Ctrl+Enter(`Key.ImeProcessed`)는 조합 확정 뒤 줄바꿈.
- 테스트 `TableCellBreaksTests`(6): 사이드카 왕복·Markdown에 태그 없음, 줄바꿈 없으면 속성 삭제, 밖에서 고친 셀만 버림, 가장자리 줄바꿈(편집 중 유지·다시 읽으면 무시), 셀 끝 공백이 같은 행 `colwidths`를 잃지 않음, 같은 글 두 셀.

## 결정 / 한계

- 다른 Markdown 프로그램(GitHub, Markdown 뷰어 탭 등)에서는 줄바꿈이 공백으로 보인다 — 태그를 쓰지 않는 대가.
- 여러 줄 글을 셀에 붙여넣으면 지금처럼 줄마다 새 행이 된다(엑셀 붙여넣기 유지). 줄바꿈이 있는 셀을 입력란에서 복사해 다른 셀에 붙여넣어도 행으로 나뉜다.
- 셀 줄바꿈만 바꾼 저장(공백 하나를 골라 Ctrl+Enter)은 Markdown은 그대로, 사이드카만 바뀐다.
- `Shift+Enter`는 계속 막는다(셀 줄바꿈은 Ctrl+Enter 하나).

## 검증

- [x] `dotnet test tests/Folderss.SearchTests --filter TableCellBreaksTests` 6개 통과, 전체 1,195 통과·5 건너뜀·0 실패
- [x] Linux `EnableWindowsTargeting` 빌드: 오류는 기존 `ConsolePanel` `Microsoft.Terminal` 1건뿐(변경 전과 같음)
- [ ] Windows `dotnet build .\Folderss.sln -c Debug` 오류 0
- [ ] 실제 확인(사람):
  1. 표 셀에서 `Enter` → 아래 행 추가(이전과 같음)
  2. `Ctrl+Enter` → 셀 안 줄바꿈, 행 높이 늘어남. 셀 끝에서 Ctrl+Enter 후 이어 입력
  3. 한글 조합 중 `Ctrl+Enter` → 글자 확정 뒤 줄바꿈
  4. 셀을 벗어나면 표시에도 줄바꿈, `↑↓`는 셀 첫·마지막 줄에서만 행 이동
  5. 저장 → `.md`에는 공백, `.md.meta.json`에 `cellbreaks` → 다시 열면 줄바꿈 복원
  6. `Ctrl+Z`로 줄바꿈 되돌리기
  7. 다른 편집기에서 그 셀 글을 고치고 다시 열기 → 그 셀만 공백, 상태 줄에 이상 없음

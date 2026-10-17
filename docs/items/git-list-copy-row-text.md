# Git 창 나머지 목록 Ctrl+C 행 복사

- 상태: Done

## 요구사항

- Git 창에서 변경 사항 목록·트리 외의 목록(저장소, 브랜치, 보낼/받을 커밋, stash, 워킹트리, 로그)은 항목을 고르고 `Ctrl+C`를 눌러도 클립보드에 아무것도 복사되지 않는다.
- `Ctrl+C`로 화면에 보이는 행 텍스트를 복사한다(사용자 선택: 보이는 행만 — 해시·브랜치 이름 같은 핵심 값 복사나 `Ctrl+Shift+C`는 두지 않음).

## 원인 분석 또는 설계

- 원인: WPF `ListBox`·`ListView`는 `DataGrid`와 달리 기본 복사 동작이 없다. 변경 사항 목록·트리는 `ChangeCopy_PreviewKeyDown`(git-change-list-copy-name-path)으로 직접 처리했지만 다른 목록에는 처리가 없었다(회귀 아님).
- 보이는 행: `DisplayMemberPath`가 있는 목록은 그 속성 값, 로그(`ListView` + `GridView`)는 `DisplayMemberBinding`이 있는 열(그래프 열 제외)을 탭으로 이어 표계산 프로그램에 열별로 붙게 한다.
  열 정의를 그대로 읽으므로 로그 열을 바꾸면 복사도 따라간다. 저장소 목록은 두 줄 템플릿이라 이름(`Title`)·요약(`Summary`)을 탭으로 잇는다.
- 정렬용 앞 공백(현재 브랜치가 아닌 줄의 들여쓰기 `"   "`)은 뺀다. 앞뒤 공백만 빼고 탭은 남겨 빈 열(참조 없음 등)이 있어도 열 수가 같다.
- 고른 것이 없으면 키를 가로채지 않는다. `Ctrl` 단독일 때만 처리한다(`Ctrl+Shift+C` 등은 무시). 왼쪽 섹션 탭(`SectionNav`)은 대상 아님.

## 구현 내용

- `GitWindow.xaml` — `RepoList`, `BranchList`, `OutgoingList`, `IncomingList`, `StashList`, `WorktreeList`, `LogList`에 `PreviewKeyDown="RowCopy_PreviewKeyDown"`.
- `GitWindow.xaml.cs` — `RowCopy_PreviewKeyDown`(선택 항목마다 한 줄, `ClipboardService.TrySetDataObject`), `RowCopyText`.

## 변경 파일

- `Folderss/GitWindow.xaml`
- `Folderss/GitWindow.xaml.cs`
- `README.md`
- `docs/items/git-list-copy-row-text.md`

## 검증

- Windows `dotnet build .\Folderss.sln -c Debug`: 오류 0, 추가한 줄에서 새 경고 없음.
- 단위 테스트 없음: 테스트 프로젝트(`net8.0`)는 WPF 창 코드를 컴파일하지 않는다.
- Windows 수동 확인(Release 빌드, 사용자 확인 완료):
  1. 로그에서 커밋을 고르고 `Ctrl+C` → 메모장에 `해시 제목 참조 작성자 날짜`(탭 구분), 엑셀에 붙여넣으면 5열.
  2. 브랜치(현재/다른 브랜치), 보낼/받을 커밋, stash, 워킹트리에서 `Ctrl+C` → 보이는 행 그대로(앞 들여쓰기 없음).
  3. 왼쪽 저장소 목록에서 `Ctrl+C` → `경로<탭>요약`.
  4. 변경 사항 목록·트리의 `Ctrl+C`/`Ctrl+Shift+C`, diff 보기·커밋 메시지·출력 영역의 `Ctrl+C`는 기존대로.

## 변경 이력

- 2026-10-17: 요청 접수(사용자가 Release 빌드 확인 중 발견), 복사 내용은 "보이는 행만"으로 사용자 확인. 구현·빌드 완료, Windows 확인 대기.
- 2026-10-17: Release 빌드에서 사용자 확인 완료(Done).

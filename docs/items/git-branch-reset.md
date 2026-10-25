# Git 창 브랜치 우클릭 reset

- 상태: In Progress (Windows에서 UI 확인 필요)

## 요구사항

- Git 창 브랜치 탭에서 브랜치를 고르고 우클릭으로 reset할 수 있게 한다.
- 사용 목적: 원격과 갈라져(예: ↓3 ↑4) 충돌이 난 상황에서 내 브랜치를 원격과 똑같이 맞추기.
- revert는 넣지 않는다(되돌리는 커밋이 더 생겨 이 목적과 맞지 않음).

## 원인 분석 또는 설계

- reset은 로그 탭(`이 커밋으로 reset…`)에만 있었고 브랜치 목록에는 우클릭 메뉴가 없었다.
- "현재 브랜치를 여기로 reset…" — 현재 브랜치(HEAD)를 고른 브랜치의 끝 커밋(목록에 보이던 해시)으로 옮긴다. 로그 탭과 같은 `GitResetDialog`(기본 mixed, hard는 경고·확인 체크)를 쓴다.
- 고른 브랜치에 없고 현재 브랜치에만 있는 커밋 수(목록의 ↓)가 있으면 "브랜치에서 빠진다"고 대화상자에 경고한다. ↓는 브랜치 그래프를 읽은 뒤에 채워지므로 그 전에는 경고가 없다.
- 현재 브랜치 자신은 HEAD가 안 움직이므로 메뉴를 끈다.
- 충돌 중(merge 진행 중) 실제 git 확인: `reset --hard origin/x`는 MERGE_HEAD까지 정리되어 원격과 똑같아진다. `--mixed`는 HEAD는 맞지만 작업 트리에 충돌 표시가 남는다 → 이 목적이면 hard.
- 원격 브랜치는 마지막 fetch 기준이므로 먼저 fetch해야 한다. 기존 `업데이트 ▾ → 덮어쓰기`도 현재 브랜치에 같은 `reset --hard`를 한다(우클릭은 같은 일을 하는 짧은 경로).

## 구현 내용

- `GitDialogs.cs`: `GitResetDialog`에 `droppedCommits` 경고(선택 인수).
- `GitWindow.xaml(.cs)`: 브랜치 목록 `ContextMenu`(`BranchList_ContextMenuOpening`, `BranchReset_Click`), 로그 reset과 공용 `ResetToCommitAsync`.

## 변경 파일

- `Folderss/GitDialogs.cs`, `Folderss/GitWindow.xaml(.cs)`, `README.md`

## 검증

- reset 인수는 기존 `GitRefCommands.Reset`(테스트 `RealGit_ResetModes_MoveHead_AndKeepOrDropChanges`) 그대로.
- 앱 빌드(`EnableWindowsTargeting`)에서 새 코드 오류 없음(ConsolePanel `Microsoft.Terminal` 오류는 Linux 전용으로 변경 전과 같음).
- WPF 우클릭 메뉴·대화상자 동작은 Windows에서 확인 필요.

## 변경 이력

- 2026-10-25: 브랜치 우클릭 reset 추가(revert는 제외하기로 함).

# Git 창 브랜치 우클릭 reset · revert

- 상태: In Progress (Windows에서 UI 확인 필요)

## 요구사항

- Git 창 브랜치 탭에서 브랜치를 고르고 우클릭 메뉴로 reset·revert를 할 수 있게 한다.

## 원인 분석 또는 설계

- reset은 로그 탭(`이 커밋으로 reset…`)에만 있었고 revert는 없었다. 브랜치 목록에는 우클릭 메뉴가 없었다.
- **reset**: "현재 브랜치를 여기로 reset…" — 현재 브랜치(HEAD)를 고른 브랜치 끝 커밋으로 옮긴다. 로그 탭과 같은 `GitResetDialog`(기본 mixed, hard는 경고·확인 체크)를 쓴다.
  고른 브랜치에 없고 현재 브랜치에만 있는 커밋 수(목록의 ↓)가 있으면 "브랜치에서 빠진다"고 대화상자에 경고한다. 현재 브랜치 자신은 HEAD가 안 움직이므로 메뉴를 끈다.
- **revert**: 커밋 단위 명령이라 브랜치에서는 "이 브랜치의 마지막 커밋 revert…"로 정의했다. 결과 커밋은 항상 **현재 브랜치**에 생긴다.
  현재 브랜치에 이미 들어 있는 커밋만 의미가 있으므로(아니면 되돌릴 변경이 HEAD에 없어 충돌) 현재 브랜치이거나 ↑가 0인 브랜치에서만 켠다. ↑는 브랜치 그래프를 읽은 뒤 채워지므로 그 전에는 꺼진다.
  임의 커밋 revert가 필요하므로 로그 탭 우클릭에도 `이 커밋 revert…`를 같이 넣었다.
- revert 옵션(`GitRevertDialog`): 기본 "바로 커밋"(`--no-edit`, 편집기 없이), 해제하면 `--no-commit`(스테이지만). 병합 커밋은 첫 부모 기준 `-m 1`로 고정(병합으로 들어온 변경을 되돌림 — 가장 흔한 의도).
  병합 여부는 로그 목록 대신 `git log -1`로 커밋을 다시 읽어 판단한다(목록 부모가 단순화됐을 수 있음).
- 충돌로 멈추면 출력에 해결/`git revert --abort` 안내만 한다(merge·rebase 충돌 처리와 같은 수준 — 앱 안에 continue/abort 버튼은 없음).

## 구현 내용

- `Services/GitRefCommands.cs`: `Revert(commit, isMerge, noCommit)`.
- `GitDialogs.cs`: `GitRevertDialog`, `GitResetDialog`에 `droppedCommits` 경고(선택 인수).
- `GitWindow.xaml(.cs)`: 브랜치 목록 `ContextMenu`(`BranchList_ContextMenuOpening`, `BranchReset_Click`, `BranchRevert_Click`), 로그 우클릭 `LogRevert_Click`,
  공용 `ResetToCommitAsync`/`RevertCommitAsync`/`ReadCommitAsync`.

## 변경 파일

- `Folderss/Services/GitRefCommands.cs`, `Folderss/GitDialogs.cs`, `Folderss/GitWindow.xaml(.cs)`
- `tests/Folderss.SearchTests/GitTests.cs`
- `README.md`, `docs/architecture.md`

## 검증

- `dotnet test tests/Folderss.SearchTests --filter FullyQualifiedName~GitTests` (Linux, 실제 git): 85 통과.
  - `RealGit_Revert_AddsUndoCommit_NoCommitStages_AndMergeUsesFirstParent`: 되돌리는 커밋 추가·작업 트리 복원, `--no-commit`은 HEAD 유지·스테이지,
    병합 커밋은 `-m` 없으면 git 거부 / `-m 1`이면 병합으로 들어온 파일만 빠짐.
- 앱 빌드(`EnableWindowsTargeting`)에서 새 코드 오류 없음(기존 ConsolePanel `Microsoft.Terminal` 오류는 Linux 전용 문제로 변경 전과 같음).
- WPF 우클릭 메뉴·대화상자 동작은 Windows에서 확인 필요.

## 변경 이력

- 2026-10-25: 브랜치 우클릭 reset·revert, 로그 우클릭 revert 추가.

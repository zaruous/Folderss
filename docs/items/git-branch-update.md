# Git 창 브랜치 업데이트 (base ← compare)

- 상태: Ready for Verification

## 요구사항

- PR 머지처럼 두 브랜치를 골라, 한 브랜치를 기준으로 다른 브랜치를 맞추는 기능.
- 버튼은 기본 방식(fast-forward만)으로 바로 실행하고, 방식은 ▾ 대화상자에서 고른다.

## 원인 분석 또는 설계

- 이름은 `업데이트`(GitHub PR의 "Update branch"). "동기화"는 pull+push로 오해되고 기존 `GitSyncCommands`(pull·push·fetch)와 겹쳐 피했다.
- 방향은 PR식: **base(바뀜, 로컬) ← compare(그대로, 로컬·원격 추적)**. 처음 열 때 base = 현재 브랜치. `⇄`로 바꿀 수 있다(compare가 로컬일 때만).
- 방식: fast-forward만(기본) / 병합 커밋(`merge --no-ff`) / squash(`merge --squash`, 스테이지만) / rebase / 덮어쓰기.
- 병합·squash·rebase는 git이 base를 체크아웃해야 하므로 **base가 현재 브랜치일 때만** 허용한다. 자동 전환은 충돌·더러운 작업 트리·다른 워킹트리 체크아웃에서 사용자가 모르는 브랜치에 남는 위험이 있어 넣지 않았다.
- 체크아웃 안 된 base의 fast-forward는 `fetch . refs/...:refs/heads/<base>`(+ 없는 refspec = fast-forward만, 체크아웃된 브랜치는 git이 거부)로 작업 트리를 건드리지 않는다.
- 덮어쓰기는 현재 브랜치면 `reset --hard`(커밋 안 한 변경 수를 경고), 아니면 `branch -f`. reset hard처럼 경고 + 확인 체크를 거쳐야 실행된다.
- compare는 전체 참조 이름(`refs/heads/…`, `refs/remotes/…`)으로 넘겨 같은 이름의 태그·경로와 헷갈리지 않게 한다.
- 원격 추적 브랜치는 마지막 fetch 기준이다. fetch는 자동으로 하지 않고(네트워크·인증 실패를 이 기능이 떠안지 않게) 대화상자에 안내만 한다. 원격은 바꾸지 않는다.

## 구현 내용

- `Services/GitSyncCommands.cs`: `GitBranchUpdateMode`, `GitBranchUpdateOptions`, `NeedsCurrentBase`, `UpdateBranch`(현재 브랜치가 아닌 base에 병합·squash·rebase, 같은 브랜치끼리면 `InvalidOperationException`).
- `GitDialogs.cs`: `GitBranchUpdateDialog` — base/compare 콤보, `⇄`, 바뀌는 쪽 안내 문구, 방식 라디오, 덮어쓰기 경고·확인 체크, `ValidateAsync`로 조건 검사.
- `GitWindow.xaml(.cs)`: 브랜치 탭 `업데이트`(현재 브랜치 ← 목록에서 고른 브랜치, fast-forward만) + `▾`(대화상자, compare 기본값은 고른 브랜치 또는 upstream). base가 현재 브랜치일 때만 저장 안 한 문서를 확인한다.

## 변경 파일

- `Folderss/Services/GitSyncCommands.cs`
- `Folderss/GitDialogs.cs`
- `Folderss/GitWindow.xaml`, `Folderss/GitWindow.xaml.cs`
- `tests/Folderss.SearchTests/GitTests.cs`
- `README.md`, `docs/architecture.md`

## 검증

- `dotnet test tests/Folderss.SearchTests` (Linux, 실제 git): 248 통과, 1 건너뜀. 새 테스트 4개
  - 현재 브랜치·체크아웃 안 된 브랜치·원격 추적 브랜치 기준 fast-forward
  - 갈라졌을 때 fast-forward 실패, base 그대로(현재/비현재 모두)
  - 병합 커밋(부모 2개), squash(HEAD 그대로·스테이지만), rebase(일직선), 덮어쓰기(현재: 커밋 안 한 변경까지 사라짐 / 비현재)
  - 비현재 base에 병합·squash·rebase, 같은 브랜치끼리 거부
- WPF UI(대화상자 동작·배치)는 Windows에서 확인 필요. Linux에서 `EnableWindowsTargeting`으로 컴파일 시 이번 변경 파일 오류 없음(기존 `EasyWindowsTerminalControl` 참조 오류 1건만).

## 변경 이력

- 2026-10-09: 브랜치 업데이트(base ← compare) 버튼과 ▾ 대화상자 추가.

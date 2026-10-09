# Git 창 원격 브랜치 삭제 (▾ 확인 후)

- 상태: Ready for Verification

## 요구사항

- 브랜치 탭에서 원격 브랜치도 삭제할 수 있게 한다. 삭제 옆 ▾ 대화상자에서 확인한 뒤에만 지운다.

## 원인 분석 또는 설계

- 원격 브랜치 삭제는 안전한 기본 동작이 없으므로 `삭제` 버튼은 원격 브랜치에 대해 ▾를 쓰라고 안내만 한다. 실제 삭제는 ▾ 대화상자에서만 한다.
- 기존 `GitForceConfirmDialog`는 "강제 옵션을 끄면 일반 동작"이라는 구조라 맞지 않아, 경고를 늘 보이고 확인 체크를 해야 버튼이 켜지는 `GitRemoteBranchDeleteDialog`를 따로 만들었다.
- 명령은 `git push --delete -- <원격> <브랜치>`. 성공하면 git이 로컬 추적 참조도 지우고, 로컬 브랜치는 그대로 둔다.
- 원격 이름에 '/'가 들어갈 수 있으므로, `origin/feature/x` 같은 이름은 `git remote` 목록에서 가장 길게 맞는 원격으로 나눈다(`SplitRemoteBranch`).
- 대화상자에는 그 원격 브랜치를 upstream으로 쓰는 로컬 브랜치를 함께 보여 준다(삭제 뒤 upstream이 사라짐).
- 서버가 기본 브랜치나 보호된 브랜치 삭제를 거부하면 git 오류가 출력에 그대로 남고 아무것도 바뀌지 않는다.

## 구현 내용

- `Services/GitSyncCommands.cs`: `DeleteRemoteBranch`, `SplitRemoteBranch`.
- `GitDialogs.cs`: `GitRemoteBranchDeleteDialog`.
- `GitWindow.xaml.cs`: `DeleteBranchOptions_Click`에서 원격 브랜치를 고른 경우 `DeleteRemoteBranchAsync`로 분기(네트워크 타임아웃). `삭제` 버튼은 원격 브랜치에 대해 ▾를 쓰라고 안내만 한다.

## 변경 파일

- `Folderss/Services/GitSyncCommands.cs`, `Folderss/GitDialogs.cs`, `Folderss/GitWindow.xaml(.cs)`
- `tests/Folderss.SearchTests/GitTests.cs`
- `README.md`, `docs/architecture.md`

## 검증

- `dotnet test tests/Folderss.SearchTests` (Linux, 실제 git): 250 통과, 1 건너뜀.
  - `SplitRemoteBranch_PicksLongestMatchingRemote`: `origin/feature/x` 나누기, 원격 이름에 '/'가 있는 경우, 맞는 원격이 없는 경우
  - `RealGit_DeleteRemoteBranch_RemovesItFromRemoteOnly`: bare 원격에서 삭제됨, 추적 참조 정리, 로컬 브랜치 유지, 이미 없는 브랜치는 실패
- WPF 대화상자 동작은 Windows에서 확인 필요. 인증이 필요한 실제 원격(GitHub 등)과 보호 브랜치 거부는 측정하지 않았다.

## 변경 이력

- 2026-10-09: 원격 브랜치 삭제(▾ 확인 후) 추가.

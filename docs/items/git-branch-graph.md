# Git 창 브랜치 탭: 요약 그래프 + 현재 브랜치 대비 ↑↓

- 상태: Ready for Verification

## 요구사항

- 브랜치 탭에 브랜치 이름만 나열되어 어느 쪽이 최신인지, 어떤 브랜치가 어디에 병합됐는지 알 수 없다.
- 리비전 그래프로 브랜치 관계를 보여 준다. 미리보기 세 안(목록 보강 / 요약 그래프 / 전체 그래프) 중 요약 그래프로 결정.
- 기본은 로컬 브랜치만, 원격 브랜치는 옵션(브랜치 탭 체크박스)으로 본다.
- 같은 커밋을 가리키는 브랜치가 여럿일 때 버튼 대상이 모호해지지 않도록 목록과 그래프를 나란히 두고, 버튼은 목록 선택 기준을 유지한다.

## 원인 분석 또는 설계

- "최신"을 커밋 날짜로 판단하면 rebase·시계 차이로 틀릴 수 있어, 조상 관계로 센 HEAD 대비 앞섬/뒤처짐을 기준으로 보인다.
- `git log --simplify-by-decoration`만 쓰면 갈라진 지점이 사라져 브랜치가 훨씬 오래된 커밋에서 갈라진 것처럼 보인다.
  그래서 각 브랜치 끝과 HEAD의 merge-base(topo 순서에서 처음 만나는 공통 조상 = 가장 가까운 것)를 함께 남긴다.
- LibGit2Sharp에는 simplify-by-decoration이 없고, 그와 같은 git 명령도 없다(분기점을 남기므로). 그래서 줄이기는 순수 함수
  `GitBranchGraph.Build`로 만들고 LibGit2Sharp 경로·git CLI 경로가 모두 이 함수를 거치게 해 결과를 맞춘다.
- 로그 개수 제한(`LogLimit`)을 쓰면 오래된 브랜치 끝이 빠지므로 제한 없이 전체를 걷는다. 비용이 크므로 브랜치 탭을 볼 때만 읽는다.

## 구현 내용

- `Services/GitBranchGraph.cs` — `Build(commits, tips, head)`: 남길 커밋(끝·HEAD·갈라진 지점), 다시 이은 부모, `ForkPoints`, 끝별 `Divergence`(앞섬, 뒤처짐).
  목록 밖 부모(shallow 경계)는 버린다. `CliArguments(includeRemotes, hasHead)`: CLI 대체 경로 인수(`--branches`/`--remotes`로 골라 명령줄 길이 제한 회피).
- `GitLibReader.ReadBranchGraph` — 커밋마다 해시·부모만 만들어 `Build`에 넘기고, 남은 행에만 제목·작성자·시각을 채운다(`TopoOrder` 재사용).
- `GitBranchInfo.TipHash`(`ReadBranches`, `BranchFormat`에 `%(objectname)` 추가), `Ahead`/`Behind` → `DisplayText`에 `↑n ↓m`.
- `GitWindowState.BranchesShowRemote`(git-window.xml `branchRemotes`, 기본 false).
- `GitWindow.xaml` — 브랜치 탭: 툴바에 `원격 포함` 체크박스, 본문을 `BranchList` | 분할선 | `BranchGraphList`(로그와 같은 그래프 열 + 브랜치·제목·날짜).
- `GitWindow.xaml.cs` — `ShowBranchList`(원격 필터), `EnsureBranchGraph`/`LoadBranchGraphAsync`(브랜치 탭일 때만, 기다리는 사이 저장소·원격 표시가 바뀌면 버림),
  그래프 행의 브랜치 열(같은 커밋의 브랜치는 `, `로 묶음, `(갈라진 지점)`, detached HEAD), 목록 선택 시 그 끝 행 선택.

## 범위 밖 / 주의

- 그래프는 보기 전용이다. 전환·삭제·업데이트는 왼쪽 목록 선택이 대상. `(갈라진 지점)` 행은 선택해도 아무 작업이 없다.
- 원격 브랜치는 기본으로 목록에서도 숨는다 — 원격 브랜치로 전환(추적 브랜치 생성)하거나 원격 브랜치를 삭제하려면 `원격 포함`을 켠다.
  업데이트·새 브랜치 대화상자의 브랜치 후보는 지금처럼 원격까지 모두 보인다.
- 원격 브랜치는 마지막 fetch 때의 상태다.
- 태그는 그래프에 넣지 않았다.
- 큰 저장소(수십만 커밋)에서는 전체를 걸으므로 느릴 수 있다. 브랜치마다 조상 집합을 계산하므로 원격 브랜치가 많으면 더 든다. 측정 전.
- 같은 시각 커밋의 topo 동점 순서는 LibGit2Sharp와 CLI 경로에서 다를 수 있다(레인 배치만 달라짐, 행·부모·↑↓는 같음).

## 변경 파일

- `Folderss/Services/GitBranchGraph.cs` (새 파일)
- `Folderss/Services/GitLibReader.cs`, `Folderss/Services/GitOutputParser.cs`, `Folderss/Services/GitWindowStateService.cs`
- `Folderss/Models/GitModels.cs`
- `Folderss/GitWindow.xaml`, `Folderss/GitWindow.xaml.cs`
- `tests/Folderss.SearchTests/Folderss.SearchTests.csproj`, `GitTests.cs`, `GitLibReaderTests.cs`
- `README.md`, `docs/architecture.md`, `docs/done/DONE.md`

## 검증

- 단위 테스트(`dotnet test tests/Folderss.SearchTests`, Linux): 389개 통과, 3개 skip(기존).
  - `BranchGraph_KeepsTipsAndForkPoints_AndRelinksParents` — 미리보기와 같은 구조(갈라진 feature, develop에 병합된 브랜치, main에만 병합된 hotfix)에서
    중간 커밋(병합 커밋 포함)은 접히고 갈라진 지점 두 개가 남으며 부모가 다시 이어짐.
  - `BranchGraph_CountsAheadBehindAgainstHead` — 끝별 ↑↓. 병합된 브랜치는 ↑0.
  - `BranchGraph_WithoutHead_KeepsOnlyTips`, `BranchGraph_ParentOutsideList_IsDropped`(shallow 경계).
  - `GitLibReaderTests.BranchGraph_MatchesCli_LocalAndRemote` — 복잡한 실제 저장소에서 LibGit2Sharp 경로와 CLI 경로의 행·부모·갈라진 지점·↑↓가 같음(로컬만/원격 포함).
  - `Branches_MatchCli`에 `TipHash` 비교 추가, `WindowState_RoundTrips…`에 `BranchesShowRemote` 추가.
- Linux에서 `-p:EnableWindowsTargeting=true`로 앱 컴파일: 기존부터 있던 `ConsolePanel`의 `Microsoft.Terminal`(Windows 전용 패키지) 오류 1건 외 새 오류 없음.
  Windows에서 `dotnet build .\Folderss.sln -c Debug` 확인 필요.
- Windows 수동 확인 필요:
  1. 브랜치 탭: 왼쪽 로컬 브랜치 목록에 `↑n ↓m`(현재 브랜치는 없음), 오른쪽 그래프에 브랜치 끝과 `(갈라진 지점)` 행.
  2. `원격 포함` 켜기/끄기 → 목록·그래프에 원격 브랜치가 생기고 사라짐. 창을 다시 열어도 유지.
  3. 목록에서 브랜치를 고르면 그래프의 그 행이 선택되고 보이게 스크롤. 전환·삭제·업데이트가 지금처럼 목록 선택 기준으로 동작.
  4. 다른 탭에 있다가 저장소를 바꾼 뒤 브랜치 탭을 열면 그때 읽음. 브랜치 전환 등 작업 후 그래프·↑↓가 새로 고쳐짐.
  5. 큰 저장소에서 브랜치 탭을 열 때 걸리는 시간(출력창 타이밍 로그 `LibGit2Sharp 브랜치 그래프`).
  6. detached HEAD에서 `● HEAD (detached)` 행과 모든 브랜치의 ↑↓.

## 변경 이력

- 2026-10-20: 미리보기 세 안 중 요약 그래프로 구현. 원격 브랜치는 브랜치 탭 체크박스, 버튼 대상은 목록 선택 유지.

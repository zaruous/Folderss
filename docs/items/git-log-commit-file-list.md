# Git 창 로그: 커밋 파일 목록 + 파일별 diff

- 상태: Ready for Verification

## 요구사항

- Git 창 로그 탭에서 커밋을 고르면 아래에 커밋 전체 diff가 한꺼번에 나와, 파일이 많으면 원하는 파일을 찾기 어렵다.
- 아래 영역에 그 커밋에서 바뀐 파일 목록을 보이고(`트리로 보기`로 폴더 트리 전환), 고른 파일 하나의 diff만 보인다.

## 원인 분석 또는 설계

- 아래 영역을 좌우로 나눈다: 왼쪽 파일 목록(평면 `ListBox` / 트리 `TreeView`, 변경 사항 탭과 같은 `GitEntryTemplate`·`GitChangeTreeStyle`·`GitChangeTree.Build` 재사용), 오른쪽 `LogDiff`.
- 파일 목록: `git diff-tree -r -z --no-commit-id --name-status -M <첫 부모> <hash> --`(최초 커밋은 `--root <hash>`) — 커밋 diff(`GitDiffCommands.Commit`)와 같은 기준(병합 커밋은 첫 부모).
  상태 글자는 `GitStatusEntry.IndexState`에 넣고 `Side = Index`로 두어 상태 아이콘(수정·추가·삭제·이름 변경)을 그대로 쓴다.
- 파일 diff: `GitDiffCommands.CommitFile` — `Commit`에 경로를 붙인 것. 이름 변경은 원래 경로도 넘겨야 이름 변경으로 짝지어진다(`Staged`와 같은 방식). libgit2 경로(`GitLibDiff`)는 `Paths`로 같은 결과.
- 파일을 고르기 전에는 diff를 비워 두고 "파일을 선택하면 차이를 보여 줍니다."를 보인다. 트리에서 폴더를 고르면 폴더 경로·파일 개수만 보인다.

## 구현 내용

- `GitDiffCommands.CommitFiles`, `GitDiffCommands.CommitFile` 추가.
- `GitOutputParser.ParseNameStatus` 추가(`-z` name-status, 이름 변경·복사는 경로 두 개).
- `GitLibReader.ReadCommitFiles` 추가 — 처음엔 git CLI만 썼으나 커밋 클릭마다 git.exe를 띄워 느려서 libgit2 우선으로 바꿈.
- `GitWindow.xaml` — 로그 아래 영역을 파일 목록(+`트리로 보기`) | 분할선 | `LogDiff`로.
- 파일 목록·트리 `Ctrl+C` 이름 / `Ctrl+Shift+C` 전체 경로 텍스트 복사(`LogFileCopy_PreviewKeyDown`, `GitChangeTree.ClipboardText` 재사용). 변경 사항 탭과 달리 파일 복사(FileDrop)는 하지 않는다 — 작업 트리 파일은 그 커밋의 내용이 아니다.
- `GitWindow.xaml.cs` — `LoadCommitFilesAsync`(기다리는 사이 다른 커밋·저장소를 고르면 결과 버림), `ShowCommitFileDiffAsync`, 목록·트리 선택 처리, 저장소 바뀔 때 `ClearLogFiles`.

## 범위 밖 / 주의

- 파일 목록은 `GitLibReader.ReadCommitFiles`(LibGit2Sharp, 프로세스 없음)로 먼저 읽고, 실패하면 git CLI(`CommitFiles`)로 읽는다. diff도 libgit2 우선.
  이름 변경 감지는 git `-M`과 같은 기준(유사도 50%, 한도 `diff.renameLimit` 또는 1000). 유사도 계산식이 git과 미세하게 달라 경계값(50% 근처) 파일은 드물게 이름 변경/삭제+추가가 갈릴 수 있다.
- 커밋 전체 diff를 한꺼번에 보는 보기, 커밋 전체를 외부 도구 폴더 비교(`--dir-diff`)로 여는 기능은 로그 탭에서 빠졌다(파일 하나씩 외부 도구로는 열림). 원격 비교 탭의 보낼/받을 커밋은 그대로 커밋 전체 diff.
- 로그 파일 목록의 `트리로 보기`는 창을 다시 열면 꺼진 상태로 시작한다(저장 안 함).

## 변경 파일

- `Folderss/Services/GitDiffCommands.cs`
- `Folderss/Services/GitOutputParser.cs`
- `Folderss/GitWindow.xaml`
- `Folderss/GitWindow.xaml.cs`
- `tests/Folderss.SearchTests/GitTests.cs`
- `tests/Folderss.SearchTests/GitLibDiffTests.cs`
- `README.md`, `docs/architecture.md`, `docs/done/DONE.md`

## 검증

- 단위 테스트(`dotnet test tests/Folderss.SearchTests --filter "FullyQualifiedName~GitTests|FullyQualifiedName~GitLibDiffTests"`, Linux): 87개 통과(기존 85 + 신규 2).
  - `ParseNameStatus_ReadsStatusPathAndRename` — 수정·이름 변경(원래 경로)·추가(한글 경로)·삭제.
  - `RealGit_CommitFiles_ListAndPerFileDiff` — 실제 git으로 수정·이름 변경·삭제·하위 폴더 추가 목록, 파일 하나 diff에 다른 파일이 섞이지 않음, 이름 변경 diff, 최초 커밋.
  - `GitLibReaderTests.CommitFiles_MatchCli` — 커밋 5개(최초·이름 변경 100%/80%·유사도 기준 아래 삭제+추가·실행 권한·바이너리·한글 경로·병합)에서 libgit2 목록이 CLI와 순서·상태까지 같음.
  - `CommitsAndUpstreamRanges_MatchCli`에 `CommitFile`(최초·일반·병합 커밋) libgit2 ↔ CLI 동일 비교 추가.
- Linux에서 `-p:EnableWindowsTargeting=true`로 앱 컴파일: 기존부터 있던 `ConsolePanel`의 `Microsoft.Terminal`(Windows 전용 패키지) 오류 1건 외 새 오류 없음.
  Windows에서 `dotnet build .\Folderss.sln -c Debug` 확인 필요.
- Windows 수동 확인 필요:
  1. 로그에서 커밋 클릭 → 왼쪽에 파일 목록과 `파일 N` 제목, 오른쪽 "파일을 선택하면…". 파일 클릭 → 그 파일 diff만.
  2. `트리로 보기` 켜고 끄기, 폴더 클릭 시 안내 문구, 파일 클릭 시 diff.
  3. 이름 변경 파일은 `rename from/to`와 내용 차이, 삭제 파일은 전체 삭제로 보임. 최초 커밋·병합 커밋도 확인.
  4. 다른 저장소를 고르면 파일 목록·diff가 비워짐. 보기 모드·공백 무시 변경 시 보고 있는 파일 diff가 다시 불러와짐.
  5. 파일·폴더를 고르고 `Ctrl+C` → 이름, `Ctrl+Shift+C` → 전체 경로가 붙여넣어짐(목록·트리 모두).

## 변경 이력

- 2026-10-20: 요청 접수(로그 클릭 시 전체 diff 대신 파일 목록 + 선택 파일 diff, 트리 보기). 구현·단위 테스트 완료, Windows 빌드·수동 확인 대기.
- 2026-10-20: 파일 목록 클립보드 복사 추가 요청 → 텍스트(이름/전체 경로)만 복사로 구현.
- 2026-10-20: 파일 목록 로딩이 느림(git.exe 실행) → libgit2 우선 + CLI 대체로 변경. Git 테스트 102개 통과.

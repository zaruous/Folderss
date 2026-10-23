# Git 창 브랜치 탭: 브랜치 간 바뀐 파일 목록 + 파일별 diff

- 상태: Ready for Verification

## 요구사항

- 브랜치 탭에서 브랜치를 골라도 현재 브랜치와 어떤 파일이 다른지 알 수 없다.
- 기본은 현재 브랜치와 비교하되, 서로 다른 두 브랜치끼리도 바뀐 파일 목록을 볼 수 있어야 한다.

## 원인 분석 또는 설계

- 비교 대상은 왼쪽 목록에서 고른 브랜치(버튼 대상과 같음), 기준은 새 `기준` 콤보(기본 현재 브랜치, detached면 `HEAD (detached)`).
  기준 후보는 목록 필터(`원격 포함`)와 무관하게 모든 로컬·원격 브랜치.
- "바뀐 파일"의 뜻이 두 가지라 하나로 고정하지 않고 `방식` 콤보로 고른다(CLAUDE.md "선택지가 여러 개인 기능" 규칙). 되돌릴 수 없는 작업이 아닌 보기 전용이라 대화상자 대신 콤보.
  - `갈라진 뒤 대상의 변경`(기본) — `git diff 기준...대상`: merge-base ↔ 대상. 대상 브랜치에서만 바뀐 파일(GitHub compare·PR과 같은 기준). 기준 쪽에서만 바뀐 파일은 빠진다.
  - `두 브랜치 끝 비교` — `git diff 기준 대상`: 두 끝의 트리 차이. 양쪽에서 바뀐 파일이 모두 보인다(방향: 기준 → 대상).
- `⇄` 버튼: 비교 방향만 뒤집는다(대상 → 기준). 대상을 병합한 기준과 비교하면 `기준...대상`이 비는데(대상에만 있는 변경 없음 — 정상),
  뒤집으면 기준 쪽 변경이 보인다. 목록 선택을 바꾸는 방식은 쓰지 않았다 — 목록 선택은 전환·삭제·업데이트 버튼 대상이라 모르는 사이에 바뀌면 위험하고,
  기준이 목록에 없는 원격 브랜치(`원격 포함` 꺼짐)·detached HEAD면 고를 수도 없다. 파일 제목에 `A ... B`/`A → B`로 방향을 늘 보인다.
- 리비전은 브랜치 이름 대신 `GitBranchInfo.TipHash`를 넘긴다 — 같은 이름의 태그·파일과 헷갈리지 않게.
- 로그 탭의 커밋 파일 목록 구조(파일 목록 | diff, `트리로 보기`, `Ctrl+C` 복사)를 그대로 재사용.

## 구현 내용

- `GitDiffCommands.RevisionFiles`(`diff -z --name-status -M [A...B | A B] --`), `GitDiffCommands.RevisionFile`(파일 하나 diff, 이름 변경은 원래 경로 포함,
  `OldSideMergeBaseOf`/`OldSide`로 libgit2 경로와 UTF-16 재비교가 같은 기준을 쓰게 함).
- `GitLibReader.ReadRevisionFiles` — libgit2로 같은 목록(갈라진 지점은 `FindMergeBase`). 트리 비교 부분은 `ReadCommitFiles`와 공유(`CompareTrees`로 뺌, 동작 변화 없음).
  공통 조상이 없으면 `NotSupportedException` → CLI(git도 실패하면 출력창에 오류).
- `GitWindow.xaml` — 브랜치 탭을 위(목록 | 그래프) / 아래(기준·방식 + 파일 목록 | `BranchDiff`)로 나눔.
- `GitWindow.xaml.cs` — `ShowBranchCompareBases`(같은 저장소에서 사용자가 현재가 아닌 브랜치를 기준으로 골랐으면 `_branchBaseChoice`로 기억해 상세를 다시 읽어도 유지, 아니면 현재 브랜치 → 브랜치를 전환하면 기준도 따라감. 코드로 채울 때는 `_fillingBranchBases`로 선택 기록을 막음),
  `LoadBranchFilesAsync`(기다리는 사이 저장소·대상·기준·방식이 바뀌면 결과 버림), 파일·트리 선택 → `ShowBranchFileDiffAsync`.
  `LogFileCopy_PreviewKeyDown`은 sender의 목록/트리를 쓰도록 바꿔 두 탭이 함께 씀.

## 범위 밖 / 주의

- 비교는 커밋끼리다. 커밋 안 한 작업 트리 변경은 들어가지 않는다(작업 트리 ↔ upstream은 원격 비교 탭).
- 원격 브랜치는 마지막 fetch 때의 상태.
- `기준`·`방식`·`⇄` 선택은 저장하지 않는다(창을 다시 열면 현재 브랜치·갈라진 뒤 변경).
- 같은 커밋을 가리키는 두 브랜치는 목록을 읽지 않고 "같은 커밋" 안내만 보인다.
- 큰 저장소에서 `두 브랜치 끝 비교`는 바뀐 파일이 많을 수 있고, 브랜치를 고를 때마다 읽는다(libgit2라 git 프로세스는 띄우지 않음). 측정 전.

## 변경 파일

- `Folderss/Services/GitDiffCommands.cs`, `Folderss/Services/GitLibReader.cs`
- `Folderss/GitWindow.xaml`, `Folderss/GitWindow.xaml.cs`
- `tests/Folderss.SearchTests/GitLibReaderTests.cs`, `GitLibDiffTests.cs`
- `README.md`, `docs/architecture.md`

## 검증

- 단위 테스트(`dotnet test tests/Folderss.SearchTests --filter "FullyQualifiedName~Git"`, Linux): 167개 통과.
  - `GitLibReaderTests.RevisionFiles_MatchCli_BothModes` — 수정·이름 변경·한글 경로 추가가 있는 feature와 다른 파일을 고친 main에서, 양방향 × 두 방식 모두
    libgit2 목록이 CLI(`RevisionFiles` + `ParseNameStatus`)와 같음. 갈라진 뒤 방식은 feature 쪽 3개만, 두 끝 방식은 main에서만 바뀐 파일까지 4개.
    feature를 main에 병합한 뒤 `main...feature`는 비고 뒤집은 `feature...main`은 main 쪽 변경을 보임(⇄).
  - `GitLibDiffTests.CommitsAndUpstreamRanges_MatchCli`에 `RevisionFile`(두 끝·갈라진 지점·공백 무시) libgit2 ↔ CLI 동일 비교 추가.
- Linux에서 `-p:EnableWindowsTargeting=true`로 앱 컴파일: 기존부터 있던 `ConsolePanel`의 `Microsoft.Terminal`(Windows 전용 패키지) 오류 1건 외 새 오류 없음.
  Windows에서 `dotnet build .\Folderss.sln -c Debug` 확인 필요.
- Windows 수동 확인 필요:
  1. 브랜치 탭 아래에 `기준 [현재 브랜치] ↔ (목록에서 브랜치 선택)`. 목록에서 다른 브랜치를 고르면 `파일 N`과 목록, 파일을 고르면 그 파일 diff.
  2. `기준`을 다른 브랜치로 바꾸면 두 브랜치 사이 목록으로 바뀜. `방식`을 `두 브랜치 끝 비교`로 바꾸면 기준 쪽에서만 바뀐 파일도 보임.
  3. 현재 브랜치를 고르면 "같은 커밋" 안내. `트리로 보기`, 폴더 클릭 안내, `Ctrl+C`/`Ctrl+Shift+C` 복사.
  4. 브랜치 전환 후 기준이 새 현재 브랜치로 바뀜(다른 브랜치를 기준으로 골라 뒀으면 유지). 다른 저장소를 고르면 비워짐.
  5. `⇄`를 누르면 버튼이 `⇄ 반대`, 파일 제목의 방향이 바뀌고 목록 선택은 그대로. 병합된 브랜치에서 빈 목록 → 기준 쪽 변경이 보임.
  6. 보기 모드·공백 무시 변경 시 보고 있는 파일 diff 다시 불러옴, 외부 도구 열기.

## 변경 이력

- 2026-10-23: 요청 접수(브랜치 선택 시 현재 브랜치와 바뀐 파일, 다른 브랜치끼리도). 구현·단위 테스트 완료, Windows 빌드·수동 확인 대기.
- 2026-10-23: 병합된 브랜치에서 갈라진 뒤 방식이 비어 보이는 문제 → 방향 뒤집기 `⇄` 추가(목록 선택은 바꾸지 않음).

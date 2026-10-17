# Git 창·두 파일 비교 diff를 libgit2(xdiff)로

- 상태: Ready for Verification — 구현·비교 테스트 완료, Windows 화면 확인 대기

## 요구사항

- 2026-10-17 요청: PR #58 diff 방식 벤치마크를 Windows에서 점검한 결과, git.exe를 띄우는 비용이 diff 계산보다 훨씬 크다. git의 diff 처리 방식만 가져와 프로세스 없이 쓰는 방향으로, 라이브러리 전환을 diff부터 한다(사용자 선택: "diff부터").
- fetch·pull·push·커밋 등 쓰기·네트워크는 git.exe로 남긴다.

## 원인 분석 또는 설계

### 측정 (이 PC: Windows 11, Ryzen 5 5600X, AhnLab V3 켜짐)

- 프로세스 생성 자체가 느리다: `cmd /c exit` 108ms, `whoami.exe` 516ms, `cmd\git.exe --version` 1,114ms, `mingw64\bin\git.exe --version` 702ms(각 7회 중앙값).
- `tools/DiffBench` 20가지 경우 합계(`tools/DiffBench/results-windows.md`): git CLI 21,353ms, **libgit2 버퍼 155ms**(git 대비 기하평균 234배), 직접 Histogram 600ms, LibGit2Sharp(blob 쓰기) 9,093ms.
  - libgit2 버퍼는 1만 줄 전부 다름 7ms, 줄 끝만 다름 3.5ms로 최악 경우도 빠르다. 변경 줄 수는 19/20건 git CLI와 같다(나머지 1건은 아래 줄 끝).
- 그래서 직접 구현(Histogram 등)이 아니라 git과 같은 엔진(libgit2의 xdiff)을 프로세스 없이 쓴다.

### 방식

- `GitLibDiff.Read`: `GitDiffRequest`의 변경 전/후 출처(`OldSide`/`NewSide`: 인덱스 `:`, 작업 트리, 리비전, 빈 쪽)로 libgit2 함수를 고른다.
  - 인덱스 ↔ 작업 트리: `git_diff_index_to_workdir` (추적 안 됨은 `INCLUDE_UNTRACKED | RECURSE_UNTRACKED_DIRS | SHOW_UNTRACKED_CONTENT`)
  - 리비전 ↔ 인덱스(스테이지): `git_diff_tree_to_index` (커밋 전 HEAD는 빈 트리)
  - 리비전 ↔ 작업 트리(`diff @{u}`): `git_diff_tree_to_workdir_with_index`
  - 리비전 ↔ 리비전(커밋·`A...B`): `git_diff_tree_to_tree` (`A...B`의 변경 전 = merge-base)
  - 두 파일(`--no-index`): `git_patch_from_buffers`
  - 공통: `git_diff_find_similar(RENAMES)`(=`-M`) → `git_diff_to_buf(PATCH)`
- LibGit2Sharp의 diff API는 내용을 UTF-8로만 풀어(잘못된 바이트는 대체 문자) CP949 대체 해석을 할 수 없다. 그래서 LibGit2Sharp가 배포하는 같은 네이티브 DLL(`git2-5853918`)의 함수를 P/Invoke로 직접 불러 바이트로 받고, CLI 경로와 같은 `GitTextDecoder.Decode`로 푼다. 리비전 해석·사전 검사는 LibGit2Sharp로 한다.
- `git_diff_options`는 C# 구조체로 옮기지 않는다. `git_diff_options_init`이 채운 메모리에서 앞쪽 필드(flags, pathspec, context_lines, interhunk_lines)만 고치고, 기본값(version 1, ignore_submodules -1, context_lines 3)으로 자리를 확인해 다르면 CLI로 돌린다.
- git CLI와 같게 만들려고 맞춘 것:
  - indent heuristic: git은 2.14부터 기본 켬, libgit2는 기본 끔 → 켠다(`diff.indentHeuristic=false`면 끔).
  - 사용자 설정: `diff.context`(보기 모드 `-U`가 우선), `diff.interHunkContext`, `diff.algorithm`(minimal·patience는 맞추고 histogram은 CLI로).
  - 헤더의 경로 표기: libgit2는 비ASCII 경로를 `"…\353\221\220…"`처럼 8진수로 감싸고, git(`core.quotepath=false`)은 한글을 그대로 두며 이름에 공백이 있으면 `---`/`+++` 줄 끝에 탭을 붙인다 → `NormalizeHeaders`가 헤더 줄만 git 규칙으로 다시 감싼다.
- CLI로 돌리는 경우(`NotSupportedException`): 충돌 중(인덱스·작업 트리 비교, git은 combined diff), `GitLibReader` status와 같은 조건(서브모듈·sparse·인덱스 v3+·`filter=`), `diff=` 속성(textconv·funcname 드라이버), `diff.algorithm=histogram`, 리비전 없음·공통 조상 없음, 네이티브 DLL을 못 불러옴. UTF-16/32 BOM 재비교(`GitEncodingDiff`)는 지금처럼 git으로 한다(드묾).

### git CLI와 다른 점 (의도했거나 남은 차이)

- **두 파일 비교의 줄 끝**: git `--no-index`는 `core.autocrlf=true`(Git for Windows 기본)에서 LF와 CRLF만 다른 파일을 "차이 없음"으로 본다. 내장 비교는 줄 끝을 바꾸지 않으므로 그대로 차이로 보인다(공백 무시를 켜면 같게 본다). 이전에는 줄 끝 차이가 화면에 보이지 않았다.
- **이름 변경 유사도 숫자**: `similarity index` 값이 libgit2와 git의 계산식 차이로 1%쯤 다를 수 있다(예: git 94%, libgit2 95%). 본문은 같다. 50% 경계 근처에서는 이름 변경으로 볼지가 갈릴 수 있다.
- **index 줄 해시 길이**: libgit2는 `core.abbrev`(없으면 7자리)를 쓰고, git은 객체 수에 따라 자동으로 늘린다(큰 저장소에서 8자리 이상). 헤더의 `index abc1234..def5678` 길이만 다를 수 있다.
- 하위 폴더의 `.gitattributes`는 검사하지 않는다(status와 같은 한계).

## 구현 내용

- `Folderss/Services/GitLibDiff.cs` (신규): 위 방식, `NormalizeHeaders`, 네이티브 선언.
- `GitDiffRequest.Paths`·`IgnoreWhitespace`: libgit2로 같은 diff를 만들 대상 경로와 공백 무시. `GitDiffCommands`의 각 요청이 채운다.
- `GitWindow.RunDiffAsync`: `ReadLibAsync(row, "diff", GitLibDiff.Read)` 먼저, 안 되면 기존 CLI. 이후 BOM 재비교는 그대로.
- `FileCompareWindow.LoadTextAsync`: `GitLibDiff.Read` 먼저, 예외면 기존 CLI. 내장 비교는 git이 없어도 된다(외부 도구는 git 필요).
- `GitLibReader.EnsureStatusSupported`를 `internal`로 해 같은 조건을 쓴다.
- `tools/DiffBench`: `libgit2 버퍼` 엔진(`git_patch_from_buffers`) 추가, Windows 결과 `results-windows.md`.

## 변경 파일

- `Folderss/Services/GitLibDiff.cs`, `Folderss/Services/GitDiffCommands.cs`, `Folderss/Services/GitLibReader.cs`
- `Folderss/GitWindow.xaml.cs`, `Folderss/FileCompareWindow.cs`
- `tests/Folderss.SearchTests/GitLibDiffTests.cs`, `tests/Folderss.SearchTests/Folderss.SearchTests.csproj`
- `tools/DiffBench/Engines.cs`, `tools/DiffBench/Program.cs`, `tools/DiffBench/DiffBench.csproj`, `tools/DiffBench/results-windows.md`
- `docs/architecture.md`, `README.md`, `docs/items/diff-engine-benchmark.md`, `docs/items/git-performance.md`, `docs/items/git-diff-libgit2.md`

## 검증

- `GitLibDiffTests` 7개 통과(Windows, git 2.53): 같은 요청을 git CLI(Git 창 인수 그대로)와 libgit2로 만들어 출력 텍스트를 글자 단위로 비교(유사도 숫자만 제외).
  - 작업 트리: 보기 모드 3가지, 개행 없는 끝, 공백만 바뀜(-w 있음·없음), 삭제, 바이너리, CP949(대체 인코딩), indent heuristic이 갈리는 예(git t4061).
  - 스테이지: 이름 변경, 새 파일, 한글·공백 파일명, 첫 커밋 전. 추적 안 됨: 루트·한글 하위 폴더.
  - 커밋: 최초·일반·병합(첫 부모), `@{u}...HEAD`·`HEAD...@{u}`, 작업 트리 ↔ `@{u}`(-w 포함).
  - 두 파일: 보기 모드 3가지, -w, 같은 내용, 빈 파일, 바이너리, 한글·공백 폴더 경로. 줄 끝만 다름은 차이로 보이고 -w면 같음.
  - CLI로 돌림: 충돌 파일, `diff.algorithm=histogram`, `filter=` 속성. 네이티브 DLL 이름이 LibGit2Sharp와 같은지.
- indent heuristic 플래그를 끈 채 돌리면 t4061 예에서 실패하는 것을 확인한 뒤 되돌렸다(플래그 값 검증).
- Windows 화면에서 확인할 것: Git 창에서 변경 파일·커밋·stash·원격 비교를 고를 때 diff가 바로 뜨는지(`FOLDERSS_GIT_TIMING=1`이면 출력창에 `LibGit2Sharp diff` 시간), 두 파일 비교, 보기 모드 전환, HTML 보고서.

## 변경 이력

- 2026-10-17: 요청 접수, 구현·비교 테스트 완료(Ready for Verification).
- 2026-10-17: Release 빌드(`FOLDERSS_GIT_TIMING=1`)에서 사용자 확인 — Git 창 diff가 확실히 빨라짐. 두 파일 비교·보기 모드·HTML 보고서 확인은 남음.

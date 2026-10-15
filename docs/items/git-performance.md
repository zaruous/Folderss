# Git 창 속도 개선 — 실측 먼저

- 상태: In Progress — 1단계(조회를 LibGit2Sharp로, status 제외) 구현·테스트 완료, Windows 화면 확인 대기

## 요구사항

- 2026-10-15 사용자 요청: "깃 기능 속도 개선 방법은 있는지?" → 개선 전에 실측부터. 1단계로 계측 코드만 넣는다(동작 변경 없음).
- 같은 날 LibGit2Sharp를 썼을 때의 차이도 비교 요청.

## 결론 (Windows 실측)

- 회사 Windows PC에서 **Git 창의 시간은 거의 전부 git 프로세스를 띄우는 비용**이다. git 내부 작업은 1회 0.18초 이하인데, 프로세스 1회 실행이 1.4초 걸린다.
  git과 무관한 `cmd.exe /c exit`도 약 1초 걸리므로 PC 환경(보안 프로그램의 프로세스 검사로 추정, 미확인) 문제다.
- 그래서 Linux에서 내린 "LibGit2Sharp로 바꿀 이유가 없다"는 결론이 뒤집힌다. 프로세스 수를 줄이거나 없애는 개선만 의미가 있다.
- 저장소 선택: 현재 **6.2초** → `mingw64` git 직접 실행 + 동시 실행 **1.0초** → LibGit2Sharp **0.013초**.

## Windows 실측 (2026-10-15)

- 환경: Windows 11 Enterprise 10.0.26200, Git for Windows 2.53.0, .NET 8, LibGit2Sharp 0.32.0
- 대상: Gargoyle (`main`, 커밋 3,214개, 추적 파일 4,880개, 추적 안 되는 파일 51개, commit-graph 없음, 인덱스 오래된 상태)
- 방법: 콘솔 측정 프로그램이 앱의 `GitCommandRunner.cs`를 그대로 링크해 Git 창의 호출 순서를 재현(조회 전용 `GIT_OPTIONAL_LOCKS=0`이라 저장소를 바꾸지 않음).
  CLI는 3회, LibGit2Sharp는 5회 반복한 중앙값. 보안 검사로 보이는 4~5초짜리 튐이 가끔 있었다.
- 차트: https://claude.ai/artifact/MR7pGN5jE6LhCGks6dW6Pr (비공개, 소유자만 열람)

### 프로세스 1회 비용

| 실행 | 1회 |
|---|---|
| `cmd\git.exe --version` (앱이 PATH에서 찾는 것 — 실제 git을 한 번 더 띄우는 래퍼) | 1,428 ms |
| `where.exe /?` | 1,186 ms |
| `cmd.exe /c exit` | 976 ms |
| `mingw64\bin\git.exe --version` (실제 git 직접) | 591~752 ms |
| `usr\bin\true.exe` (msys) | 305 ms |
| `git status` 내부 실제 작업 (`GIT_TRACE_PERFORMANCE`, 그중 `refresh index` 160 ms) | 175 ms |

### 장면별

| 장면 | 방식 | 중앙값 | 현재 대비 |
|---|---|---|---|
| 저장소 선택 → 상세 읽기 | 현재 (`cmd\git.exe`, 순차) | 6,167 ms | — |
| | A 동시 실행 | 3,497 ms | −43% |
| | A + B | 1,962 ms | −68% |
| | `mingw64` git 직접 | 2,859 ms | −54% |
| | 직접 + A | 1,877 ms | −70% |
| | 직접 + A + B | 1,042 ms | −83% |
| | LibGit2Sharp (매번 열기) | 30 ms | −99.5% |
| | LibGit2Sharp (열어 두고 재사용) | 13 ms | −99.8% |
| 쓰기 작업 뒤 다시 읽기 (명령 자체 제외) | 현재 | 6,547 ms | — |
| | A 동시 실행 | 7,419 ms (최소 4,726) | 편차 큼 |
| | `mingw64` git 직접 | 3,632 ms | −45% |
| | 직접 + A | 2,748 ms | −58% |
| | LibGit2Sharp (재사용) | 118 ms | −98% |
| 스테이지 뒤 `status`만 (C) | 현재 | 1,277 ms | — |
| | `mingw64` git 직접 | 815 ms | −36% |
| | LibGit2Sharp (재사용) | 107 ms | −92% |

- LibGit2Sharp 첫 호출(네이티브 DLL 로드 + 저장소 열기)은 40~180 ms.
- 쓰기·네트워크(commit, pull, push 등)는 어느 방식이든 git 실행 파일을 써야 하므로 명령당 1~2초는 남는다.

## Linux 사전 측정 (2026-10-15, 참고용)

앱의 git 호출 순서를 Python으로 재현해 쟀다(각 20~40회 중앙값). 프로세스 1회가 약 2 ms라 결과의 성격이 Windows와 다르다.

| 저장소 | 장면 | CLI 현재 | 비고 |
|---|---|---|---|
| Folderss (커밋 134, 파일 344) | 저장소 선택 | 13 ms | |
| Gargoyle | 저장소 선택 | 31 ms | commit-graph + A면 8.7 ms, LibGit2Sharp 재사용 9 ms |
| Gargoyle | 쓰기 작업 뒤 다시 읽기 | 49~55 ms | LibGit2Sharp 61 ms(status가 더 느림) |
| Gargoyle | `status` — 인덱스 최신 | 18~21 ms | LibGit2Sharp 22 ms |
| Gargoyle | `status` — 인덱스 오래됨 | 211~616 ms | LibGit2Sharp 827 ms |

- 발견: 조회는 `GIT_OPTIONAL_LOCKS=0`이라 status가 인덱스 갱신을 저장하지 못한다. 인덱스가 오래되면 다른 git 쓰기가 있을 때까지 status마다 같은 비용을 다시 낸다.
  LibGit2Sharp도 같다. 고치려면 IDE와의 `index.lock` 경합 위험을 감수해야 한다. Windows에서는 프로세스 비용에 묻혀 우선순위가 낮다.

## 개선 후보

| 후보 | 내용 | 효과(Windows, 저장소 선택) | 위험 |
|---|---|---|---|
| ① | 조회 명령만 `mingw64\bin\git.exe` 직접 실행, 쓰기·네트워크는 지금처럼 `cmd\git.exe` | −54% | `cmd\git.exe`가 해 주는 환경 설정(PATH에 `usr\bin` 등)이 빠짐. 조회는 hook·ssh·인증이 안 끼어 낮음. 설정에서 git 경로를 직접 지정한 경우는 건드리지 않아야 함 |
| A | 선택 상세 6개 조회를 동시에 | −43% | 낮음. 동시 4개 제한 |
| B | worktree·stash는 탭을 열 때만 | A와 합쳐 −68% | 탭 열 때 잠깐 빈 목록 |
| C | 스테이지류는 status만 다시 읽기 | 스테이지 뒤 6.5초 → 1.3초 | 판단이 틀리면 화면이 실제와 어긋남 |
| ③ | 조회는 LibGit2Sharp(저장소 열어 두기), 쓰기·네트워크는 CLI | −99.8% | 팩 파일을 열어 둬 콘솔 `git gc`/repack과 충돌 가능, status가 콘솔 git과 미묘하게 다를 수 있음(autocrlf·필터·sparse·safe.directory), Repository는 스레드 안전하지 않음, 파서·테스트 두 벌, 네이티브 DLL 배포. CLI와 결과가 같은지 비교 테스트가 먼저 필요 |
| D | `-uall` → `normal`(옵션) | 미미 | 추적 안 되는 폴더가 한 줄로 접힘 — 동작 변경 |
| E | commit-graph·untrackedCache 안내 | 미미(Windows) | 사용자 저장소 설정 변경 |

## 결정과 구현 (2026-10-15)

- 사용자 결정: ③ LibGit2Sharp. 1단계는 **status 제외**(status는 XY·이름 변경·충돌·서브모듈·줄바꿈이 콘솔 git과 어긋날 위험이 커서 비교 테스트를 충분히 만든 뒤 2단계).
- `Services/GitLibReader.cs`: 브랜치, 로그(HEAD/`--all`), upstream 보낼·받을 커밋, stash, 워킹트리를 같은 모델로 읽는다.
  - 저장소는 호출마다 열고 닫는다(열어 두면 팩 파일 잠금으로 콘솔 `git gc`와 충돌, Repository는 스레드 안전하지 않음).
  - 로그 순서: libgit2의 위상 정렬은 merge 뒤 첫 부모 줄기를 먼저 내서 git과 달랐다. git `sort_in_topological_order`(GRAPH_ORDER, LIFO)를 그대로 옮겼다.
  - 참조 표시(`%D`): HEAD·heads·remotes·tags·`refs/stash`만, 참조 이름 역순, HEAD가 맨 앞(`HEAD -> 브랜치`), 태그는 `tag: `. git 2.53에서 확인.
  - 워킹트리: 링크된 워킹트리가 없을 때만(주 작업 트리 하나). 링크된 워킹트리의 잠김·prune 판정은 git 내부 규칙이라 CLI로 읽는다.
- `GitWindow.ReadLibAsync`: LibGit2Sharp로 읽고, 실패하면 기존 CLI 경로로 다시 읽는다. 예상된 경우(`NotSupportedException`: 링크된 워킹트리, upstream 없음·사라짐)는 조용히, 그 밖의 예외는 출력창에 알린다.
- 검증
  - `GitLibReaderTests`(8개, 실제 git으로 만든 저장소): 원격·갈라진 커밋·merge·태그·원격 HEAD·사라진 upstream·notes·사용자 참조·여러 줄/한글 메시지·stash 2종·detached·최초 커밋 전·링크된 워킹트리.
    처음엔 로그 순서 2건 실패 → git 알고리즘 이식 후 전부 통과.
  - 실제 저장소 비교(Gargoyle, Folderss, Folderss-oracle-db-helper): 브랜치·로그 300개(HEAD/`--all`)·stash·워킹트리 모두 CLI와 같음. 세 저장소 모두 upstream과 차이가 없어 보낼/받을 커밋은 0개끼리 비교(차이 있는 경우는 테스트에서 확인).
  - 이 PC에서 저장소 선택 상세 전부(LibGit2Sharp): Gargoyle 141 ms, Folderss 83 ms, oracle-db-helper 43 ms(링크된 워킹트리가 있어 워킹트리만 CLI → +1.4초). 현재 6.2초 대비 −98%.
    처음 잰 13 ms보다 큰 것은 git과 같은 순서를 위해 전체 히스토리를 C#에서 정렬하고, 호출마다 저장소를 열고 닫기 때문.
- 남는 것: status(1회 약 1.3초)는 그대로라 창을 열 때·쓰기 작업 뒤에는 여전히 초 단위. 2단계 후보.

## 변경 파일

- `Services/GitCommandRunner.cs` — `GitResult.WaitMilliseconds`/`ElapsedMilliseconds`, `Completed` 이벤트
- `Services/GitLibReader.cs` (신규) — LibGit2Sharp 조회
- `GitWindow.xaml.cs` — `FOLDERSS_GIT_TIMING` 확인, `⏱` 출력, `ReadLibAsync`로 브랜치·로그·upstream 비교·워킹트리·stash 읽기(실패 시 CLI)
- `Folderss.csproj`, 테스트 `.csproj` — `LibGit2Sharp` 0.32.0
- 테스트: `GitTests.RealGit_Completed_ReportsRepositoryAndElapsedTime`, `GitLibReaderTests`

## 계측 사용법

1. 환경 변수를 켜고 Folderss를 실행한다.
   ```powershell
   $env:FOLDERSS_GIT_TIMING = "1"
   .\Folderss\bin\Debug\net8.0-windows\Folderss.exe
   ```
2. Git 창에서 평소처럼 쓴다(저장소 선택, 스테이지, 커밋, pull 등).
3. 출력창의 `⏱` 줄을 복사한다.

| ⏱ 줄 | 뜻 |
|---|---|
| `N ms (대기 W ms)  git ...  [저장소]` | 명령 하나. N=프로세스 시작~종료·출력 읽기, W=동시 실행 제한(4개) 대기 |
| `저장소 선택 → 상세 읽기 전체 N ms` | 저장소를 고른 뒤 브랜치·로그·원격 비교·worktree·stash를 다 읽기까지 |
| `작업 전체(명령+다시 읽기) N ms  git ...` | 스테이지·커밋 등 쓰기 명령 + status·상세 다시 읽기 |
| `N ms  LibGit2Sharp 브랜치/로그/…  [저장소]` | LibGit2Sharp 조회 하나. `건너뜀(…) → git으로`이면 그 조회는 CLI로 읽었다 |
| `로그 N개: 파싱+그래프 P ms, 그리기 R ms` | git 밖(C#) 비용. 그리기는 Loaded 우선순위 콜백까지의 근사치 |
| `탐색+상태 읽기 전체 N ms` | 창을 열 때 저장소 탐색과 모든 저장소 status |

환경 변수가 없으면 아무것도 찍지 않는다(이벤트 구독도 안 함).

## 한계

- 측정 프로그램은 저장소 밖(세션 임시 폴더)에 있다. 앱 안의 실제 체감은 `⏱` 계측으로 다시 확인해야 한다.
- 반복 횟수가 적고(3~5회) 이 PC 한 대 결과다. 보안 프로그램이 없는 PC에서는 ①·A·B의 이득이 훨씬 작다.
- 그리기 시간은 Loaded 우선순위 콜백까지라 근사치다. 가상화된 목록이라 보이는 행만 그린다.
- 변경 목록(`ShowChanges`)·diff 표시의 C# 비용은 재지 않는다(git 명령 시간은 찍힘).
- Git 창을 여러 개 열면 각 창은 자기 목록에 있는 저장소 명령만 찍는다. 같은 저장소를 두 창이 보고 있으면 양쪽에 찍힌다.

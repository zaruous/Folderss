# Git 연동 (선택 폴더 하위 다중 저장소) — 상세설계서

- 상태: Todo

## 요구사항

### 사용자 요청 (원문 요지)

- 메뉴에서 실행하면 **활성 패널에서 선택한 디렉터리**를 기준으로 Git 기능을 쓴다.
- 실사용에서는 선택한 폴더의 **하위 트리에 `.git`이 여러 개** 있다고 가정해야 한다.
- 1차 후보 기능: **상태 확인, 스테이지, 커밋, 푸시, 풀, 브랜치, 로그, 브랜치 그래프**.

### 기능 (제안)

1. `⋯ 메뉴 > Git 저장소 보기…`를 누르면 기준 폴더(아래 "기준 폴더 결정") 아래를 백그라운드로 훑어 Git 저장소 목록을 만든다.
2. 기준 폴더가 이미 어떤 저장소 **안**에 있으면(상위에 `.git`) 그 저장소도 목록 맨 위에 "상위 저장소"로 넣는다.
3. 저장소 목록 각 행: 기준 폴더 기준 상대 경로, 현재 브랜치(또는 `detached @abc1234`), upstream 대비 ahead/behind, 변경 파일 수(스테이지됨/안 됨/추적 안 됨/충돌).
4. 목록에서 저장소 하나를 고르면 오른쪽에서 그 저장소만 다룬다.
   - **변경 사항**: 파일별 상태, 스테이지/언스테이지(개별·전체), 커밋 메시지 입력, 커밋(`amend`는 범위 밖).
   - **브랜치**: 로컬/원격 브랜치 목록, 전환, 새 브랜치 만들기, 병합된 브랜치 삭제(`-d`만).
   - **로그**: 최근 N개(기본 300) 커밋 — 해시, 제목, 작성자, 날짜, 참조(브랜치/태그) 표시.
   - **그래프**: 로그 목록 왼쪽에 브랜치 레인 그래프(2단계).
5. 툴바: 새로 고침, fetch, pull, push. **다중 저장소 일괄 동작은 상태 새로 고침과 fetch만** 허용한다(아래 리스크 R4).
6. 모든 git 명령의 표준 오류는 하단 출력 영역에 그대로 남긴다(실패 원인 파악 — 설정 저장 실패 메시지 정책과 같은 취지).

### 비기능

- 저장소 탐색·`git` 실행은 모두 백그라운드, 취소 가능. 탐색 중에도 찾은 저장소부터 목록에 들어온다.
- 저장소 수십 개에서도 UI가 멈추지 않는다(동시 `git` 프로세스 수 제한, 기본 4).
- git이 응답하지 않아도(인증 대기 등) 앱이 멈추지 않는다(타임아웃·프로세스 트리 종료).
- 앱이 대신 인증 정보를 저장하지 않는다(Git Credential Manager/ssh-agent에 맡김).

### 범위 밖 (1차)

- merge/rebase/cherry-pick/stash, **충돌 해결 UI**, 강제 푸시, 태그·원격 관리, clone, 서브모듈 update.
- 파일 diff 보기 — `docs/아이디어.md`의 "Monaco 기반 Diff 뷰어"와 묶는 것이 맞다.
- `FolderBrowser` 목록의 Git 상태 오버레이 — `docs/아이디어.md` "Git 작업 트리 상태 오버레이"(이 항목의 `GitCommandRunner`·파서를 재사용할 수 있음).

## 기준 폴더 결정 (가정 — 미결 Q1)

| 활성 패널 상태 | 기준 폴더 |
|---|---|
| 폴더 하나 선택 | 선택한 폴더 |
| 파일 선택 / 여러 항목 선택 / 선택 없음 | 패널의 `CurrentPath` |

`MainWindow.OpenInExplorer_Click`의 "폴더를 선택했으면 그 폴더, 선택이 없으면 현재 폴더" 규칙과 맞춘 것이다.

## 백엔드 선택: git CLI vs LibGit2Sharp

| | git CLI (`Process`) — **권장** | LibGit2Sharp |
|---|---|---|
| push/pull 인증 | 사용자 환경 그대로(GCM, ssh-agent, Pageant) | HTTPS만 직접 구현, **SSH 사실상 불가** |
| 사용자 설정·hook·LFS | 그대로 적용 | hook·LFS 미지원, 설정 일부만 |
| 배포 | Git for Windows 설치 필요 | 네이티브 DLL 동봉(수 MB) |
| 성능 | 프로세스 기동 비용(저장소당 수십 ms) | 인프로세스, 빠름 |
| 동작 일치 | 사용자가 콘솔에서 치는 git과 동일 | 미묘하게 다름(예: 줄바꿈·필터) |

push/pull이 요구사항에 있으므로 인증 문제 때문에 CLI를 권장한다. git이 없으면 메뉴 실행 시 "Git for Windows가 필요합니다" 안내 후 종료.
실행 파일 탐색: `PATH` → `%ProgramFiles%\Git\cmd\git.exe` → `%LOCALAPPDATA%\Programs\Git\cmd\git.exe`.

## 저장소 탐색 — `Services/GitRepositoryScanner.cs` (신규, 순수 System.IO)

- 판정: 폴더 안에 `.git` **폴더** 또는 `.git` **파일**(워크트리·서브모듈 — `gitdir: <path>`)이 있으면 저장소 루트.
  `IgnoreRuleSet.LoadFor`가 이미 같은 판정(`Directory.Exists || File.Exists`)을 쓴다.
- 저장소를 찾아도 **그 안으로 계속 내려간다** — 중첩 저장소(서브모듈, 무관한 하위 저장소)도 목록에 보이도록. `.git` 폴더 자체는 들어가지 않는다.
- 건너뛸 폴더: `.git`, `node_modules`, `bin`, `obj`, `.vs`, `packages` (고정 목록 — 미결 Q5). reparse point(junction/심볼릭 링크)는 따라가지 않는다(순환 방지, `IgnoreRuleSet`/`SearchService`와 같은 정책).
- 최대 깊이 기본 6. 접근 거부 폴더는 조용히 건너뛰고 "탐색 불가 N개"로 집계.
- 상위 저장소: 기준 폴더에서 드라이브 루트까지 올라가며 첫 `.git`.
- bare 저장소(`HEAD`+`objects`만 있는 폴더)는 찾지 않는다.
- 결과는 `IProgress<GitRepositoryInfo>`로 하나씩 흘려 보내 목록에 바로 반영.

## 명령 실행 — `Services/GitCommandRunner.cs` (신규)

- `ProcessStartInfo.ArgumentList` 사용(문자열 조합 금지 — 경로·브랜치명 인젝션/따옴표 문제 차단). `UseShellExecute=false`, `CreateNoWindow=true`.
- 항상 `git -C <repo> -c core.quotepath=false -c color.ui=false ...`, 출력은 UTF-8로 읽는다(한글 경로·메시지).
- 환경 변수: `GIT_TERMINAL_PROMPT=0`(터미널 입력 대기로 멈추는 것 방지, GCM GUI 창은 계속 뜸), 상태 조회에는 `GIT_OPTIONAL_LOCKS=0`(IDE 등과 index.lock 경합 방지), `LC_ALL=C`(stderr 문구 파싱이 필요한 곳만).
- 타임아웃: 조회 30초, fetch/pull/push 5분. 취소·타임아웃 시 `Process.Kill(entireProcessTree: true)`.
- 동시 실행 제한 `SemaphoreSlim(4)`. 같은 저장소에 쓰기 명령은 직렬화(저장소별 잠금).
- 결과: `GitResult { ExitCode, StdOut, StdErr, Duration }`. 0이 아니면 호출 측이 stderr를 출력 영역에 표시.

### 명령 매핑

| 기능 | 명령 | 비고 |
|---|---|---|
| 상태 | `status --porcelain=v2 --branch -z --untracked-files=normal` | 브랜치·upstream·ahead/behind·파일 상태를 한 번에. `-z`로 공백·한글 경로 안전 |
| 스테이지 | `add -- <paths...>` | 경로가 많으면 `--pathspec-from-file=- --pathspec-file-nul`로 stdin 전달(명령줄 길이 32K 제한) |
| 언스테이지 | `restore --staged -- <paths...>` | git 2.23+. 최초 커밋 전 저장소는 `rm --cached`로 분기 |
| 커밋 | `commit -F <임시파일>` | 메시지를 UTF-8 임시 파일로(여러 줄·따옴표 안전). 빈 메시지·스테이지 없음은 실행 전 차단 |
| 브랜치 목록 | `for-each-ref --format=... refs/heads refs/remotes` | |
| 전환 / 생성 | `switch <b>` / `switch -c <b>` | 원격 브랜치 선택 시 `switch --track` |
| 삭제 | `branch -d <b>` | `-D`(강제)는 제공 안 함 |
| fetch | `fetch --prune` | |
| pull | `pull --ff-only` | 기본은 fast-forward만. 갈라졌으면 실패 메시지 + "콘솔에서 처리" 안내 (미결 Q4) |
| push | `push` / upstream 없으면 확인 후 `push -u origin <b>` | 강제 푸시 없음 |
| 로그+그래프 | `log --topo-order -n 300 -z --format=%H%x1f%P%x1f%an%x1f%at%x1f%D%x1f%s` (+ `--all` 토글) | 그래프는 `--graph` ASCII를 파싱하지 않고 부모 해시로 직접 레인 계산 |

## 브랜치 그래프 (2단계)

- 입력: 커밋 목록(topo 순서)과 각 커밋의 부모 해시.
- 레인 할당: 활성 레인 배열에 "다음에 기다리는 해시"를 두고, 커밋이 오면 해당 레인을 차지 → 첫 부모는 같은 레인 이어받기, 나머지 부모는 빈 레인 또는 새 레인. 한 행에 여러 레인이 같은 해시를 기다리면 병합선.
- 출력: 행마다 `(노드 레인, 위로 이어지는 선들, 아래로 이어지는 선들)` → `DrawingVisual`/`Canvas`로 행 높이 고정 그리기(가상화된 `ListView`와 행 단위로 맞춤).
- 순수 로직(`GitGraphLayout`)이라 단위 테스트 가능. 레인 폭 상한(예: 12) 초과 시 잘라 표시.
- 1단계에서는 로그 목록 + 참조 배지만 제공하고 그래프는 뺀다(미결 Q3).

## UI 설계

`LayoutDocument`(탭) 하나로 연다 — ContentId `git:<기준 폴더>`. 같은 기준 폴더로 다시 열면 기존 탭 활성화.
CLAUDE.md 규칙 준수: `+ 새 패널` 앞에 삽입 후 끝으로 재정렬, 생성 직후 `ApplyPanelLockState`, 제목은 `SetDocumentTitle`. 세션 복원에는 1차에서 **포함하지 않는다**(재시작 시 자동 탐색 비용).

```
┌ Git: D:\work ───────────────────────────────────────────────────────────┐
│ [⟳ 새로 고침] [⇣ fetch 전체] | 선택 저장소: [⇣ pull] [⇡ push]  탐색 중… 12개 │
├───────────────────────────┬─────────────────────────────────────────────┤
│ 저장소                     │ [변경 사항] [브랜치] [로그]                    │
│ ▲ (상위) D:\work  main     │  스테이지됨 (2)              [전체 언스테이지] │
│ api        main ↑2 ● 5     │   M Services/Foo.cs                          │
│ web        dev  ↓3         │   A docs/new.md                              │
│ libs\core  (detached)      │  변경됨 (3)                   [전체 스테이지]  │
│ tools\x    main ⚠ 충돌 1    │   M README.md                                │
│                           │  추적 안 됨 (1)                               │
│                           │   ? tmp.txt                                  │
│                           │  ┌ 커밋 메시지 ───────────────┐ [커밋]         │
│                           │  └──────────────────────────┘               │
├───────────────────────────┴─────────────────────────────────────────────┤
│ 출력: $ git -C D:\work\api push   → rejected (non-fast-forward) ...       │
└─────────────────────────────────────────────────────────────────────────┘
```

- 파일 더블클릭: 기존 뷰어로 파일 열기(diff 아님).
- 저장소 행 우클릭: "패널에서 열기", "콘솔에서 열기"(기존 `ConsolePanel`, 범위 밖 작업의 탈출구).
- 테마: 상태 색은 새 리소스 키 없이 기존 `AccentBrush`/`SecondaryText` 등으로 먼저 구현. 전용 색이 필요해지면 모든 테마에 동일 키 추가(CLAUDE.md 테마 패턴).

## 데이터 모델 — `Models/GitModels.cs` (신규)

- `GitRepositoryInfo { RootPath, RelativePath, IsAncestor, IsWorktreeOrSubmodule }`
- `GitStatusSnapshot { Branch, Upstream, Ahead, Behind, IsDetached, Entries }`
- `GitStatusEntry { Path, OriginalPath, IndexState, WorkTreeState, IsUntracked, IsConflicted }`
- `GitCommitInfo { Hash, Parents, Author, Time, Refs, Subject }`

## 테스트 계획

`tests/Folderss.SearchTests`에 소스 링크로 추가(WPF 비의존 코드만).

1. `GitRepositoryScanner` — 임시 폴더에 `.git` 폴더/파일·중첩·reparse point·깊이 제한 구성 후 찾은 목록 검증.
2. porcelain v2 파서 — 고정 출력 문자열(한글 경로, 공백, rename `2` 레코드, 충돌 `u` 레코드, 최초 커밋 전 `# branch.oid (initial)`) → `GitStatusSnapshot`.
3. `GitGraphLayout` — 선형, 분기, 병합, 문어발 병합 케이스의 레인 번호 검증.
4. 통합(선택): 실제 `git init`으로 저장소 2개 만들어 add→commit→status, 로컬 bare 원격으로 push/pull. git이 없으면 `SkippableFact`로 건너뜀.
5. 수동: Windows에서 GCM HTTPS 푸시, SSH(키 암호 + agent 없음) 푸시가 멈추지 않고 오류로 끝나는지.

## 구현 단계 (제안)

1. **1단계**: 탐색 + 상태 + 스테이지/언스테이지 + 커밋 + fetch/pull(ff-only)/push + 브랜치 목록·전환·생성 + 로그 목록.
2. **2단계**: 브랜치 그래프, 로그 커밋 선택 시 변경 파일 목록.
3. 후속(별도 항목): diff 뷰어, 패널 상태 오버레이, 세션 복원.

## 변경 파일 (예정)

- 신규: `Services/GitRepositoryScanner.cs`, `Services/GitCommandRunner.cs`, `Services/GitStatusParser.cs`, `Models/GitModels.cs`, `Controls/GitPanel.xaml/.cs`, (2단계) `Services/GitGraphLayout.cs`
- 수정: `MainWindow.xaml`(메뉴), `MainWindow.xaml.cs`(탭 생성·기준 폴더 결정), `Folderss.csproj`(필요 시 `<Page>`), `tests/.../Folderss.SearchTests.csproj`, `docs/architecture.md`, `README.md`

## 리스크

| # | 리스크 | 영향 | 대응 |
|---|---|---|---|
| R1 | Git for Windows 미설치·구버전(`switch`/`restore`는 2.23+) | 기능 전체 불가 | 시작 시 `git --version` 확인, 2.23 미만이면 안내 |
| R2 | 인증 대기로 프로세스가 끝나지 않음(SSH 키 암호, GCM 창이 뒤에 숨음) | 버튼이 영원히 "진행 중" | `GIT_TERMINAL_PROMPT=0`, 타임아웃, 취소 버튼. SSH는 agent 사용 전제로 문서화 |
| R3 | 탐색 대상이 드라이브 루트·네트워크 드라이브·거대한 트리 | 탐색 수 분, I/O 부하 | 깊이 제한·제외 목록·취소, 기준 폴더가 드라이브 루트면 확인 창 |
| R4 | 다중 저장소 **일괄 pull/push/commit** | 한 번 클릭으로 여러 저장소 상태를 바꿈, 부분 실패 시 복구 어려움 | 1차는 일괄 동작을 status/fetch로 제한 |
| R5 | pull로 병합·충돌 발생 | 충돌 해결 UI 없음 → 반쯤 병합된 저장소 방치 | `--ff-only` 고정. 충돌 저장소는 ⚠ 표시 + 콘솔 열기 안내 |
| R6 | 브랜치 전환 시 열린 뷰어 탭의 미저장 편집 | 디스크 파일이 바뀌어 편집 내용과 어긋남 | 전환 전 해당 저장소 경로의 미저장 뷰어가 있으면 경고(`ViewerHost.IsModified` 활용). 저장된 탭은 기존 파일 감시 재로드 프롬프트가 처리 |
| R7 | IDE(VS/VS Code)와 동시 사용 시 `index.lock` 경합 | 커밋·스테이지 실패 | 조회는 `GIT_OPTIONAL_LOCKS=0`, 쓰기 실패 시 stderr 그대로 표시(자동 lock 삭제 금지) |
| R8 | 대형 저장소 `status` 비용(수 초) × 저장소 수 | 목록 채우기 지연 | 동시 4개 제한, 저장소별 점진 갱신, 자동 주기 갱신 없음(수동 + 작업 후) |
| R9 | 줄바꿈·인코딩(커밋 메시지 한글, `i18n.commitEncoding` 비 UTF-8 설정) | 메시지 깨짐 | 파일로 전달, 저장소 설정은 건드리지 않음 |
| R10 | 워크트리/서브모듈 `.git` 파일, WSL 경로(`\\wsl$`) | 잘못된 루트·느린 I/O | `.git` 파일도 루트로 인정, WSL 경로는 1차 비지원으로 명시 |
| R11 | 범위 팽창 — 그래프·diff·충돌 해결까지 가면 사실상 Git 클라이언트 | 일정·품질 | 단계 분리, 범위 밖 작업은 "콘솔에서 열기"로 위임 |

## 미결 사항 (사용자 확인 필요)

- **Q1** 기준 폴더: 위 표(폴더 선택 시 그 폴더, 아니면 현재 폴더)로 괜찮은가? 아니면 항상 현재 폴더?
- **Q2** UI 형태: 문서 탭(`LayoutDocument`, 제안) vs 별도 창 vs 하단 도킹 패널.
- **Q3** 브랜치 그래프를 1단계에 넣을지(작업량 큼) 2단계로 미룰지.
- **Q4** pull 방식: `--ff-only`(제안) / 병합 / rebase 중 무엇을 기본으로 할지, 사용자 git 설정(`pull.rebase`)을 따를지.
- **Q5** 탐색 깊이·제외 폴더를 설정 창에 노출할지, 고정값으로 시작할지(제안: 고정값).
- **Q6** 다중 저장소 일괄 pull/push가 실제로 필요한가(R4). 필요하면 확인 창 + 저장소별 결과 요약 방식으로.
- **Q7** git CLI 의존(Git for Windows 필수)을 받아들일 수 있는가.

## 변경 이력

- 2026-09-29: 요청 접수, 설계서 초안 작성(상태 Todo). 구현 전 미결 사항 확인 필요.

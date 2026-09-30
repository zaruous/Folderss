# Git 연동 (선택 폴더 하위 다중 저장소) — 상세설계서

- 상태: Ready for Verification

## 요구사항

### 사용자 요청 (원문 요지)

- 메뉴에서 실행하면 **활성 패널에서 선택한 디렉터리**를 기준으로 Git 기능을 쓴다.
- 실사용에서는 선택한 폴더의 **하위 트리에 `.git`이 여러 개** 있다고 가정해야 한다.
- 1차 후보 기능: **상태 확인, 스테이지, 커밋, 푸시, 풀, 브랜치, 로그, 브랜치 그래프**.

### 추가 요구사항 (2026-09-29)

- git은 **필수 설치**로 한다(Q7 확정). 없거나 2.26 미만이면 창을 열 때 안내하고 닫는다.
- 설계서의 선택지(미결 사항)를 **대화상자(팝업)** 로 구현해 사용자가 직접 고른다.
  - Git 기능 자체도 별도 팝업 창(`GitWindow`, 비모달)으로 연다(Q2 → 별도 창).
  - `옵션…` 대화상자(`GitOptionsWindow`)에서 기준 폴더 규칙(Q1), pull 방식(Q4), 탐색 깊이·제외 폴더(Q5), 로그 개수·범위를 고른다.

### 추가 요구사항 (2026-09-30)

- git diff와 **로컬 ↔ 원격(upstream) 차이 비교**를 Git 창에 넣는다.
  - 변경 사항: 파일 선택 시 diff (작업 트리 ↔ 인덱스 / 인덱스 ↔ HEAD / 추적 안 되는 새 파일 전체).
  - 원격 비교 탭: 보낼 커밋(`@{u}..HEAD`)·받을 커밋(`HEAD..@{u}`) 목록과 커밋별 diff, 보낼/받을 변경 전체 diff(merge-base 기준 `...`),
    커밋 안 한 수정까지 포함한 작업 트리 ↔ upstream diff, 이 저장소만 fetch 후 다시 비교.
  - 로그: 커밋 선택 시 그 커밋의 diff.
- 표시 방식(가정): unified diff를 줄 번호·추가/삭제 색으로 보이는 창 안 읽기 전용 뷰. 좌우 나란히 보기는 이번 범위 밖.

### 추가 요구사항 (2026-09-30, 2차)

- diff의 CP949 한글 깨짐 수정(확인됨).
- **설정 창에 Git 옵션 패널** 추가 — 기존 Git 창 옵션 대화상자의 항목을 옮기고 한 곳에서만 편집.
- git diff 시 사용자가 쓰는 **외부 비교 도구 exe 지정** 기능.
- 그 밖에 필요한 옵션 추가(판단): git 실행 파일 경로, 공백 무시, 대체 인코딩, 외부 도구 사용 방식(없음 / git 설정 / 직접 지정)과 프리셋.

### 추가 요구사항 (2026-09-30, 3차)

- 인코딩 기본은 **UTF-8**. 파일에 매직넘버(BOM)가 있으면 그것으로 인코딩을 정하고, 없을 때 UTF-8. (CP949 대체는 선택 옵션으로 남기되 기본 끔 — 가정)
- **브랜치 그래프** 추가.

### 추가 요구사항 (2026-09-30, 4차)

- 변경 사항은 현재의 평면 목록을 유지하되, **트리로도 볼 수 있게 전환** 기능 추가.
- 가정: 전환은 변경됨·스테이지됨에 함께 적용, 기본 평면, 창 단위(저장 안 함). 트리에서 폴더를 스테이지/언스테이지하면 아래 전체. 트리는 단일 선택.

### 추가 요구사항 (2026-09-30, 5차)

- 변경점을 볼 때 **전체 코드를 볼지, 변경점만 볼지** 등의 보기 옵션.
- 구현 선택: 변경점만(git 기본 문맥) / 문맥 10줄 / 전체 파일 3가지. diff 창마다 바로 전환 + 설정 > Git에 기본값.

### 추가 요구사항 (2026-09-30, 6차)

- 문제: 커밋되지 않은(추적 안 되는) 파일이 안 보여 git add할 항목을 확인할 수 없음 — 원인: `--untracked-files=normal`은 새 폴더를
  `dir/` 한 줄로 접어, 안의 파일을 개별로 보거나 고를 수 없었다.
- 요청: 변경 안 된 파일도 볼 수 있게("Unchanged" 구역).

### 추가 요구사항 (2026-09-30, 7차)

- 의도 재확인: "수정된 파일과 새로운 파일을 같이 보고 싶다, 상태는 표시해 달라".
- 구현: 변경됨 목록에서 수정·새 파일을 경로순으로 섞어 보이고(충돌만 위), 줄마다 한글 상태 배지(색), 목록 제목에 종류별 개수.

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
- ~~파일 diff 보기~~ → 2026-09-30 추가 요구사항으로 범위에 포함(아래). 좌우 나란히(side-by-side) Monaco diff는 여전히 `docs/아이디어.md` 후속.
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
| R1 | Git for Windows 미설치·구버전(`switch`/`restore`는 2.23+, `--pathspec-from-file`은 2.26+) | 기능 전체 불가 | 창을 열 때 `git --version` 확인, 2.26 미만이면 안내 |
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
| R12 | diff 인코딩 | 내용 오독 | 규칙 확정(3차): BOM이 있으면 그 인코딩, 없으면 UTF-8. CP949 등은 선택적 대체(기본 끔). UTF-16/32는 텍스트로 다시 비교 |
| R13 | 거대 diff(수십 MB 생성 파일·minified) | 메모리·30초 타임아웃 | 표시는 2만 줄로 자름. 출력 자체는 전부 읽으므로 극단적인 경우 타임아웃 |
| R15 | 외부 도구가 바로 종료(단일 인스턴스·`--wait` 누락) | git이 임시 파일을 지워 도구에 빈 파일/없는 파일 표시 | 2초 안 종료 시 안내 문구, 설정 창 힌트, 프리셋에 VS Code `--wait` |
| R16 | 외부 도구 인수는 git의 셸(sh)이 해석 | 역슬래시·`$`가 든 사용자 인수가 바뀔 수 있음 | 자리표시자는 자동 따옴표, 실행 파일은 작은따옴표. 힌트에 "공백 값은 큰따옴표" 명시 |
| R17 | 대체 인코딩 오판 | UTF-8로도 유효한 CP949 바이트열(드묾)은 UTF-8로 읽힘 | 줄 단위 판정으로 영향 최소화, "사용 안 함" 선택 가능 |
| R18 | `--untracked-files=all` + 무시되지 않은 거대한 새 폴더(빌드 산출물·node_modules 미무시) | 목록 수만 개·status 지연 | .gitignore 권장. 필요하면 "새 폴더 접기" 옵션 추가 후보 |
| R14 | 원격 비교가 오래된 상태 | fetch 전 원격 추적 브랜치 기준이라 "받을 커밋 0"이 실제와 다를 수 있음 | 요약 문구로 명시 + `fetch 후 비교` 버튼 |

## 미결 사항 (사용자 확인 필요)

- **Q1** 기준 폴더: 위 표(폴더 선택 시 그 폴더, 아니면 현재 폴더)로 괜찮은가? 아니면 항상 현재 폴더?
- **Q2** UI 형태: 문서 탭(`LayoutDocument`, 제안) vs 별도 창 vs 하단 도킹 패널.
- **Q3** 브랜치 그래프를 1단계에 넣을지(작업량 큼) 2단계로 미룰지.
- **Q4** pull 방식: `--ff-only`(제안) / 병합 / rebase 중 무엇을 기본으로 할지, 사용자 git 설정(`pull.rebase`)을 따를지.
- **Q5** 탐색 깊이·제외 폴더를 설정 창에 노출할지, 고정값으로 시작할지(제안: 고정값).
- **Q6** 다중 저장소 일괄 pull/push가 실제로 필요한가(R4). 필요하면 확인 창 + 저장소별 결과 요약 방식으로.
- **Q7** git CLI 의존(Git for Windows 필수)을 받아들일 수 있는가.

## 결정 사항 (미결 사항 처리)

| 항목 | 결정 |
|---|---|
| Q1 기준 폴더 | 옵션으로 선택 — 기본 "선택한 폴더 우선", 대안 "항상 현재 폴더" |
| Q2 UI 형태 | 별도 비모달 창(`GitWindow`). 도킹 탭이 아니므로 `ApplyPanelLockState`·세션 복원 대상이 아니다 |
| Q3 그래프 | 2026-09-30(3차) 추가 — `GitGraphLayout` + `GitGraphCell` |
| Q4 pull 방식 | 옵션으로 선택 — 기본 `--ff-only`, 대안 `--no-rebase` / `--rebase` / git 설정 따름 |
| Q5 탐색 깊이·제외 | 옵션으로 선택 — 기본 깊이 6, 제외 `node_modules bin obj .vs packages` |
| Q6 일괄 pull/push | 제공 안 함(일괄은 상태 조회·fetch만). 옵션으로도 열지 않음 — 리스크 R4 |
| Q7 git 의존 | 필수 설치로 확정 |

## 구현 내용

- `⋯ 메뉴 > Git 저장소…` → `MainWindow.ShowGit_Click`: 옵션의 기준 폴더 규칙으로 폴더를 정하고(드라이브 루트면 확인), `GitWindow`를 비모달로 연다.
- `GitWindow`
  - 열 때 git 존재·버전(2.26+) 확인 → 저장소 탐색(백그라운드, 찾는 즉시 행 추가) → 저장소별 `status --porcelain=v2 --branch -z` (동시 4개).
  - 툴바: 다시 찾기, 전체 fetch(`fetch --prune`), 선택 저장소 pull(옵션 방식)/push(upstream 없으면 확인 후 `push -u <origin|첫 원격> <branch>`), 옵션, 취소.
  - 변경 사항: 변경됨(충돌→수정→추적 안 됨 순)/스테이지됨 목록, 선택·전체 스테이지(`add --pathspec-from-file` / `add -A`),
    언스테이지(`restore --staged`, 최초 커밋 전은 `rm --cached`), 커밋(`commit -F <UTF-8 임시 파일>`, `Ctrl+Enter`). 빈 메시지·스테이지 없음·충돌 남음은 실행 전 차단.
  - 브랜치: `for-each-ref` 목록, 전환(`switch` / 원격은 `switch --track`), 새 브랜치(`switch -c`, `-`로 시작 금지), 삭제(`branch -d --`, 현재·원격 브랜치 차단).
  - 로그: `log --topo-order -z -n <개수> [--all]`.
  - 브랜치 전환·pull 전에 그 저장소 파일을 연 미저장 뷰어 탭 수를 세어 경고(R6).
  - 모든 명령·stdout·stderr·종료 코드를 출력 영역에 남김. 창을 닫으면 진행 중인 git 프로세스 트리를 종료.
- `GitOptionsWindow`: 위 옵션을 고르는 모달 대화상자(기본값 버튼, 숫자 범위·제외 폴더 이름 검증). 저장은 `GitSettingsService.Save` →
  `SettingsFile.Write`(원자적 쓰기). 저장 실패는 "설정 저장 실패" 메시지로 알리고 이번 창에만 적용. 탐색 옵션이 바뀌면 다시 탐색.
- `GitCommandRunner`: `ArgumentList` 전달, `-c core.quotepath=false -c color.ui=false`, UTF-8 출력, `GIT_TERMINAL_PROMPT=0`,
  `GIT_MERGE_AUTOEDIT=no`, 조회는 `GIT_OPTIONAL_LOCKS=0`, 타임아웃(조회 30초 / 네트워크·커밋 5분) 시 프로세스 트리 종료 후 안내 문구.

## 변경 파일

- 신규: `Folderss/Models/GitModels.cs`, `Folderss/Services/GitRepositoryScanner.cs`, `Folderss/Services/GitCommandRunner.cs`,
  `Folderss/Services/GitOutputParser.cs`, `Folderss/Services/GitSettingsService.cs`, `Folderss/GitWindow.xaml(.cs)`, `Folderss/GitOptionsWindow.cs`,
  `tests/Folderss.SearchTests/GitTests.cs`
- 수정: `Folderss/MainWindow.xaml`(메뉴), `Folderss/MainWindow.xaml.cs`(`ShowGit_Click`, `CountModifiedDocumentsUnder`),
  `tests/Folderss.SearchTests/Folderss.SearchTests.csproj`(소스 링크), `README.md`, `docs/architecture.md`

## 구현 내용 — diff·원격 비교 (2026-09-30)

- `Controls/GitDiffView`: 가상화 ListBox로 줄마다 변경 전/후 줄 번호와 추가(반투명 초록)·삭제(반투명 빨강) 배경. 테마 키를 새로 만들지 않고
  반투명 색을 깔아 어두운·밝은 테마 모두에서 읽히게 했다. 2만 줄 초과는 생략 안내. `BeginLoad`/`Complete` 요청 번호로 늦게 온 결과 무시. `Ctrl+C` 복사.
- `GitOutputParser.ParseDiff`: 헤더/hunk/문맥/추가/삭제/메타 분류와 줄 번호. hunk 밖 `---`/`+++`만 헤더, combined diff(`@@@`)는 두 글자 표시로 분류.
- `GitDiffCommands`: 명령 인수 한 곳(`--no-color --no-ext-diff -M`). 추적 안 됨은 `diff --no-index -- /dev/null <p>`(종료 코드 1 허용).
  병합 커밋은 첫 부모 기준 `diff <p1> <hash>`, 최초 커밋은 `show --format=`.
- `GitWindow`: 변경 사항 탭을 "왼쪽 목록(변경됨/스테이지됨) + 오른쪽 diff"로 재배치, `원격 비교` 탭 추가, 로그 탭 아래에 커밋 diff.
  상태를 새로 읽어 목록이 바뀌면 옛 diff를 비운다. upstream 없음·detached·upstream gone은 요약 문구로 알리고 비교 버튼을 끈다.

## 구현 내용 — 설정 패널·외부 비교 도구·인코딩 (2026-09-30, 2차)

- `GitOptionsWindow` 삭제 → `SettingsWindow` Git 탭(`GitPanel`). Git 창 `설정…` 버튼은 설정 창의 Git 탭을 연다.
  저장 시 `TrySave("Git", "git-settings.xml")`로 실패를 모으고, 저장한 값은 열린 Git 창 모두에 `ApplySettings`로 즉시 반영.
- 설정 항목: git 실행 파일(자동 탐색 결과 표시), 기준 폴더, pull 방식, 탐색 깊이·제외, 로그 개수·범위, 공백 무시(`-w`),
  대체 인코딩(시스템 코드 페이지 / CP949 / 없음), 외부 비교 도구(사용 안 함 / git 설정 difftool / 직접 지정 + 프리셋 5종 + 찾아보기).
- 외부 비교 도구: diff 창 제목 옆 `외부 도구로 비교` 버튼(외부로 열 수 없는 비교에서는 숨김). `git difftool`로 실행해 작업 트리·스테이지·
  커밋·원격 범위를 git이 처리하고, 여러 파일 비교는 `--dir-diff`. 사용자 `.gitconfig`는 건드리지 않음.
  "git 설정" 모드는 `diff.tool`/`merge.tool`이 없으면 실행 전에 안내.
- 인코딩: `GitTextDecoder` — 전체가 UTF-8이면 그대로, 아니면 줄 단위로 엄격 UTF-8 실패 줄만 대체 인코딩.
- git 경로를 지정했는데 파일이 없으면 자동 탐색으로 대신하지 않고 오류(다른 버전으로 조용히 실행되는 것 방지).

## 구현 내용 — BOM 인코딩 판정·브랜치 그래프 (2026-09-30, 3차)

- 대체 인코딩 기본값 `SystemAnsi` → `None`(BOM 없으면 UTF-8). 설정 창 선택지 순서·문구 변경. 이미 저장된 git-settings.xml의 값은 그대로 유지된다.
- `GitTextDecoder.DetectBom/HasWideBom/DecodeFile/DescribeBom`: UTF-8/UTF-16 LE·BE/UTF-32 LE·BE BOM 판정, BOM 제외 디코딩.
- `GitEncodingDiff`: UTF-16/32 BOM 파일의 "Binary files … differ"를 양쪽 BOM 디코딩 → `diff --no-index`로 텍스트 diff로 교체, 머리에 `# 인코딩: UTF-16 LE → UTF-16 LE` 표시.
  인코딩·BOM만 바뀌고 내용이 같으면 그렇게 표시. 작업 트리·스테이지·추적 안 됨·커밋·원격 범위 diff 모두 적용(`GitDiffRequest.OldSide/NewSide`).
- 모든 diff 인수에 `--src-prefix=a/ --dst-prefix=b/`(사용자 diff.noprefix 대응).
- 브랜치 그래프: `GitGraphLayout`(레인 계산), `GitGraphCell`(그리기), 로그 목록 첫 열 "그래프"(레인 수에 맞춰 폭 조정).

## 구현 내용 — 변경 사항 트리 보기 (2026-09-30, 4차)

- `GitChangeTree.Build`: 경로를 폴더 노드로 묶고, 파일 없이 폴더 하나만 가진 폴더 체인을 합친다(`src/app`). 폴더 먼저·이름순.
  추적 안 되는 폴더(`dir/`)는 폴더 노드가 아닌 항목으로 둔다. 이름 변경은 `new.cs ← a/old.cs`로 표시.
- `GitWindow`: `트리로 보기` 체크박스, 변경됨·스테이지됨 각각 TreeView 추가(기본 펼침, 폴더 옆 파일 수). 파일 선택 시 diff, 폴더 선택 시 대상 개수 안내,
  파일 더블클릭 시 열기(폴더는 접기/펴기). 스테이지·언스테이지 버튼은 보기 모드에 따라 대상을 고른다. 전환 시 선택·diff를 비운다.

- (4차 보완) 변경됨·스테이지됨 사이 간격(8px)을 `GridSplitter`(Rows, PreviousAndNext)로 바꿔 두 목록 높이를 끌어서 조절. 각 목록 최소 높이 60.

## 구현 내용 — diff 보기 모드 (2026-09-30, 5차)

- `GitDiffViewMode`(ChangesOnly/Context10/FullFile) 설정 추가(`diffView` 속성 저장). `GitDiffCommands.ContextOption/WithViewMode`.
- `GitDiffView` 제목 줄에 보기 선택 상자(변경 사항·원격 비교·로그 diff 모두). 바꾸면 현재 비교를 다시 불러옴. 설정 저장 시 열린 창의 세 diff 모두 새 기본값으로.
- 전체 파일은 `-U1000000` — 한 hunk로 파일 전체가 나오고, 표시 한도(2만 줄)를 넘으면 뒤는 잘린다.

## 구현 내용 — 새 폴더 안 파일 표시·변경 없는 파일 보기 (2026-09-30, 6차)

- 상태 조회를 `--untracked-files=all`로(인수는 `GitOutputParser.StatusArguments` 한 곳). 새 폴더 안 파일이 하나씩 나오고 개별 add·diff 가능.
- `변경 없는 파일 보기` 체크박스 + 세 번째 목록(평면/트리 모두, 스플리터로 높이 조절). `ls-files` − 상태 목록 = 변경 없음.
  선택 시 현재 내용 미리보기(줄 번호, BOM 규칙 디코딩, 10MB·바이너리 제외), 더블클릭으로 열기. 스테이지 대상 아님.

## 구현 내용 — 상태 배지·수정/새 파일 함께 보기 (2026-09-30, 7차)

- `GitChangeSide`/`GitChangeKind`, `GitStatusEntry.Side/Kind/StatusLabel/PathText/ForSide/Summarize`, `GitChangeNode.StatusLabel/Kind/LabelText`.
- 배지: 수정(주황)·새 파일/추가(초록)·삭제(빨강)·이름 변경/복사(파랑)·충돌(빨강 굵게)·변경 없음(회색). 평면·트리·변경 없음 목록 공통 템플릿.
- 정렬 변경: 변경됨은 충돌 → 경로순(이전: 충돌 → 수정 → 새 파일로 종류별 분리).

## 검증

- `dotnet test tests/Folderss.SearchTests` (Linux, git 2.43): 전체 48개 중 통과 47, 건너뜀 1(기존 권한 테스트). 신규 `GitTests` 11개 통과 —
  탐색(중첩·`.git` 파일·제외·깊이·상위 저장소·취소), porcelain v2(한글·공백 경로, rename, 충돌, untracked, initial, detached), log, for-each-ref, 버전 파싱,
  실제 git으로 init→add(pathspec stdin)→commit(-F)→status/branch/log 왕복, 저장소 아닌 폴더에서 실패가 예외 아닌 stderr로 오는지.
- 앱 빌드: Linux에서 `-p:EnableWindowsTargeting=true`로 XAML 마크업 컴파일·C# 컴파일 확인. 신규 파일 오류·경고 없음
  (고의 오류 삽입으로 신규 파일이 실제 컴파일 대상임을 확인). 기존 `ConsolePanel.xaml.cs(58)` 터미널 컨트롤 참조 오류 1건은 Linux 환경 한계로 기존에도 발생.
- diff 추가분: `GitTests` 5개 추가, 전체 53개 중 통과 52·건너뜀 1(기존). 파서(헤더 vs hunk 안 `---`/`+++`, 줄 번호, 메타, 바이너리, combined, 자르기),
  실제 git으로 작업 트리/스테이지/추적 안 됨 diff, bare 원격 + 클론 2개로 보낼·받을 커밋과 방향별 diff가 서로 섞이지 않는지, 작업 트리 ↔ upstream, 최초·일반 커밋 diff.
- 2차 추가분: `GitTests` 8개 추가, 전체 61개 중 통과 60·건너뜀 1(기존). CP949/UTF-8 혼합 줄 디코딩, 실제 git으로 CP949 파일 diff,
  공백 무시, 도구 명령 따옴표 처리(공백 경로·작은따옴표·자리표시자 누락), 모드별 difftool 인수, **실제 `git difftool` 실행**
  (공백이 든 경로의 가짜 도구 스크립트가 파일 모드에서 양쪽 내용을, 폴더 모드에서 양쪽 목록을 받는지), 설정 저장·복원, 없는 git 경로 처리.
- 3차 추가분: `GitTests` 7개 추가, 전체 68개 중 통과 67·건너뜀 1(기존). 그래프(선형, 분기·병합, 두 끝·문어발 병합·목록 밖 부모,
  실제 git 로그에서 행 사이 선이 끊기지 않는지), BOM 5종 판정·BOM 없음=UTF-8, 실제 git으로 UTF-16 LE 파일 작업 트리·커밋 diff가 텍스트로 보이고
  진짜 바이너리는 그대로인지, 사용자 `diff.noprefix=true`에서도 a/·b/ 경로가 유지되는지.
- 4차 추가분: `GitTests` 2개 추가(폴더 묶기·체인 합침·정렬·추적 안 되는 폴더·폴더 아래 항목 수, 이름 변경 표시·빈 입력), 전체 70개 중 통과 69·건너뜀 1.
- 5차 추가분: `GitTests` 2개 추가 + 설정 왕복에 항목 추가. 실제 git으로 40줄 파일의 20번째 줄 변경 시 변경점만(사용자 diff.context=1 → 19~21줄) /
  문맥 10줄(10~30줄) / 전체 파일(1~40줄)이 정확히 나오는지, 인수 삽입 위치(경로 `--` 앞). 전체 72개 중 통과 71·건너뜀 1.
- 6차 추가분: `GitTests` 3개 추가 — 실제 git에서 새 폴더(하위 폴더 포함) 안 파일이 하나씩 나오고 .gitignore 대상은 빠지는지,
  변경 없음 = 추적 파일 − (수정·이름 변경 전후), 충돌 단계 중복 제거, 내용 줄 번호·한도. 전체 75개 중 통과 74·건너뜀 1.
- 7차 추가분: `GitTests` 2개 추가 — 같은 파일의 목록별 상태(AM: 스테이지 "추가"/작업 트리 "수정"), 삭제·이름 변경·충돌·새 파일·변경 없음 배지,
  이름 변경 경로 표시, 사본이 원본을 바꾸지 않음, 종류별 개수 문자열·순서, 트리 노드 배지. 전체 77개 중 통과 76·건너뜀 1.
- **미검증 (Windows 수동 확인 필요)**: 상태 배지 색·정렬, 변경 없는 파일 목록·미리보기, diff 보기 모드 전환, 트리 보기 전환·폴더 스테이지, 그래프 선이 행 사이에서 이어져 보이는지(ListViewItem 템플릿 여백), 설정 창 Git 탭 배치, 실제 WinMerge/Beyond Compare/VS Code 실행(특히 Windows 경로의 셸 해석), diff 색·줄 번호 표시, 긴 diff 스크롤 성능, 창 레이아웃·테마 색, 실제 조작 흐름, GCM HTTPS push, SSH(agent 없음) push가 멈추지 않고 타임아웃/오류로 끝나는지,
  드라이브 루트 확인 창, 미저장 탭 경고.

## 변경 이력

- 2026-09-29: 요청 접수, 설계서 초안 작성(상태 Todo). 구현 전 미결 사항 확인 필요.
- 2026-09-29: git 필수 설치 확정, 선택지를 옵션 대화상자로 구현. `GitWindow`·`GitOptionsWindow`·서비스 4종·테스트 추가(그래프 제외). 상태 Ready for Verification.
- 2026-09-30: diff·원격 비교 요구 추가. 변경 사항 파일 diff, 원격 비교 탭(보낼/받을 커밋·방향별 diff·작업 트리 ↔ upstream), 로그 커밋 diff 구현. 테스트 5개 추가.
- 2026-09-30: 설정 창 Git 탭(옵션 대화상자 대체), 외부 비교 도구(difftool, 직접 지정·프리셋·git 설정), git 경로·공백 무시·대체 인코딩(CP949 깨짐 수정) 추가. 테스트 8개 추가.
- 2026-09-30: 인코딩 규칙을 "BOM 있으면 그 인코딩, 없으면 UTF-8"로 확정(대체 인코딩 기본 끔), UTF-16/32 BOM 파일 텍스트 diff, 브랜치 그래프 추가. 테스트 7개 추가.
- 2026-09-30: 변경 사항 평면 ↔ 트리 보기 전환 추가. 테스트 2개 추가.
- 2026-09-30: 변경됨·스테이지됨 사이 높이 조절 스플리터 추가.
- 2026-09-30: diff 보기 모드(변경점만/문맥 10줄/전체 파일) 추가. 테스트 2개 추가.
- 2026-09-30: 새 폴더 안 파일 개별 표시(--untracked-files=all), 변경 없는 파일 보기 추가. 테스트 3개 추가.
- 2026-09-30: 변경됨에 수정·새 파일을 함께(경로순), 상태 배지와 목록 제목 개수 추가. 테스트 2개 추가.

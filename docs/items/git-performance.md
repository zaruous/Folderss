# Git 창 속도 개선 — 실측 먼저

- 상태: In Progress — 계측 코드 추가, Windows 실측 대기

## 요구사항

- 2026-10-15 사용자 요청: "깃 기능 속도 개선 방법은 있는지?" → 개선 전에 실측부터. 1단계로 계측 코드만 넣는다(동작 변경 없음).

## 계측 사용법

1. 환경 변수를 켜고 Folderss를 실행한다.
   ```bat
   set FOLDERSS_GIT_TIMING=1
   Folderss.exe
   ```
2. Git 창에서 평소처럼 쓴다(저장소 선택, 스테이지, 커밋, pull 등).
3. 출력창의 `⏱` 줄을 복사한다.

| ⏱ 줄 | 뜻 |
|---|---|
| `N ms (대기 W ms)  git ...  [저장소]` | 명령 하나. N=프로세스 시작~종료·출력 읽기, W=동시 실행 제한(4개) 대기 |
| `저장소 선택 → 상세 읽기 전체 N ms` | 저장소를 고른 뒤 브랜치·로그·원격 비교·worktree·stash를 다 읽기까지 |
| `작업 전체(명령+다시 읽기) N ms  git ...` | 스테이지·커밋 등 쓰기 명령 + status·상세 다시 읽기 |
| `로그 N개: 파싱+그래프 P ms, 그리기 R ms` | git 밖(C#) 비용. 그리기는 Loaded 우선순위 콜백까지의 근사치 |
| `탐색+상태 읽기 전체 N ms` | 창을 열 때 저장소 탐색과 모든 저장소 status |

환경 변수가 없으면 아무것도 찍지 않는다(이벤트 구독도 안 함).

## Linux 사전 측정 (2026-10-15, 참고용)

앱의 git 호출 순서를 Python으로 재현해 쟀다(각 20~40회 중앙값). Windows는 프로세스 기동이 수십 배 느려 절대값은 다르다.

| 저장소 | 장면 | 현재 | 비고 |
|---|---|---|---|
| Folderss (커밋 134, 파일 344) | 저장소 선택 | 13 ms | 프로세스 기동이 대부분 |
| Gargoyle (커밋 3,214, 파일 4,880) | 저장소 선택 | 31 ms | commit-graph 있으면 17 ms |
| Gargoyle | 쓰기 작업 뒤 다시 읽기 | 55 ms | |
| Gargoyle | `status` — 인덱스 최신 | 21 ms | |
| Gargoyle | `status` — 인덱스 오래됨(클론·mtime 변경 직후) | **211 ms** | `refresh index` 210 ms |

- 발견: 조회는 `GIT_OPTIONAL_LOCKS=0`이라 status가 인덱스 갱신을 저장하지 못한다. 인덱스가 오래되면 다른 git 쓰기가 있을 때까지 status마다 같은 비용을 다시 낸다.
  고치려면 IDE와의 `index.lock` 경합 위험을 감수해야 하므로 실측 후 결정.

## 개선 후보 (실측 후 결정)

| 후보 | 내용 | 위험 |
|---|---|---|
| A | 선택 상세의 원격 비교·worktree·stash를 동시에 | 낮음. 동시 4개 제한에 걸림 |
| B | worktree·stash는 탭을 열 때만 | 탭 열 때 잠깐 빈 목록 |
| C | 스테이지류는 status만 다시 읽기 | 판단이 틀리면 화면이 실제와 어긋남 |
| D | `-uall` → `normal`(옵션) | 추적 안 되는 폴더가 한 줄로 접힘 — 동작 변경 |
| E | commit-graph·untrackedCache 안내 | 사용자 저장소 설정 변경 |
| — | 인덱스 갱신 저장(`readOnly` 정책) | IDE와 `index.lock` 경합 |

## 변경 파일

- `Services/GitCommandRunner.cs` — `GitResult.WaitMilliseconds`/`ElapsedMilliseconds`, `Completed` 이벤트
- `GitWindow.xaml.cs` — `FOLDERSS_GIT_TIMING` 확인, `⏱` 출력(명령별·선택 상세·쓰기 작업·로그 파싱/그리기·탐색)
- 테스트: `GitTests.RealGit_Completed_ReportsRepositoryAndElapsedTime`

## 한계

- 그리기 시간은 Loaded 우선순위 콜백까지라 근사치다. 가상화된 목록이라 보이는 행만 그린다.
- 변경 목록(`ShowChanges`)·diff 표시의 C# 비용은 재지 않는다(git 명령 시간은 찍힘).
- Git 창을 여러 개 열면 각 창은 자기 목록에 있는 저장소 명령만 찍는다. 같은 저장소를 두 창이 보고 있으면 양쪽에 찍힌다.

# diff 방식 비교 — git CLI(현재)와 후보 5가지

- 상태: Ready for Verification — Linux 측정 완료, Windows 측정 대기

## 요구사항

- 2026-10-16 요청: diff를 git.exe에 맡기는 현재 방식과, 직접 구현·외부 라이브러리를 포함한 후보 5가지를 속도·성능 면에서 비교한다. 비교용 테스트 파일을 만들고 20가지 경우로 측정한다.

## 원인 분석 또는 설계

### 후보

| 방식 | 종류 | 비고 |
|---|---|---|
| git CLI (현재) | 외부 프로세스 | 앱과 같은 인수 `git diff --no-index …`. git 설치 필요 |
| LibGit2Sharp 0.32 | 라이브러리(네이티브 libgit2) | 이미 앱 의존성. 두 버퍼를 바로 비교하는 API(`git_diff_buffers`)를 노출하지 않아, 임시 bare 저장소에 blob을 써서 비교해야 함 |
| DiffPlex 1.9 | 라이브러리(관리 코드, Apache-2.0) | Myers. 3-way 차이·병합(`ThreeWayDiffer`)도 들어 있음 |
| diff-match-patch 3.0 | 라이브러리(관리 코드, Apache-2.0) | Google 알고리즘의 NuGet 판. 판마다 대상이 최신 .NET 하나뿐(4.0=net9, 5.0=net10)이라 .NET 8 앱은 3.0에 묶임. 줄 단위 API가 없어 줄→문자 변환을 직접 해야 하고, 고유 줄 약 6만 3천 개를 넘으면 쓸 수 없음 |
| 직접 Myers | 직접 구현 | O(ND), 선형 공간(가운데 뱀 분할) |
| 직접 Histogram | 직접 구현 | 드문 줄을 기준점으로 나누고, 기준점이 없으면 Myers(JGit 방식 단순화) |

### 테스트 20가지 (`tools/DiffBench/Cases.cs`, 고정 씨앗)

같은 파일(100줄·10만 줄), 1줄 수정(1천·10만 줄), 끝·앞 추가, 가운데 삭제·삽입, 곳곳 1%·10%·50% 수정, 전부 다름(1천·1만 줄, 최악), 덩어리 이동, 순서 뒤섞기, 중괄호·빈 줄이 많은 코드, 긴 줄(2천 자), CRLF↔LF, 들여쓰기만 변경, 한글. `--write-cases <폴더>`로 파일로 내보낼 수 있다.

### 측정 방법

- 두 파일을 디스크에 둔 상태에서 파일 읽기부터 결과까지를 잰다(앱의 두 파일 비교와 같은 조건).
- 첫 실행(JIT·검증)은 빼고 최대 5회의 중앙값을 쓴다.
- 결과 품질은 변경 줄 수(삭제+추가)로 본다. 관리 코드 방식은 결과로 옛 파일을 새 파일로 재구성해 정확성을 확인했다.
- 직접 구현은 `--selftest`로 확인했다. 무작위 2만 건 재구성 정확, Myers는 LCS 동적계획법과 비교해 2만 건 모두 최소였다.

## 결과 (Linux, 4 CPU, .NET 8, git 2.43) — 전체 표: `tools/DiffBench/results-linux.md`

| 방식 | 20건 합계 (ms) | git CLI 대비 기하평균 | 가장 빠름 | 지원 안 함 |
|---|---:|---:|---:|---:|
| git CLI (현재) | 314 | 1.0배 | 5 | 0 |
| LibGit2Sharp | 1,111 | 0.5배 | 1 | 0 |
| DiffPlex | 1,683 | 0.8배 | 0 | 0 |
| diff-match-patch | 1,619 | 1.0배 | 2 | 2 (10만 줄) |
| 직접 Myers | 1,661 | 1.0배 | 6 | 0 |
| 직접 Histogram | 1,175 | 1.4배 | 6 | 0 |

### 해석

1. **보통 수정(1줄·블록·곳곳 1%·이동·한글·들여쓰기)에서는 프로세스 안 방식이 2~3배 빠르다.** 1만 줄 기준 git 8~15ms, 관리 코드 방식은 3~6ms 정도다. git의 비용 대부분은 프로세스를 띄우는 고정 비용이다. 100줄짜리 같은 파일도 git은 3.8ms, 관리 코드는 0.1ms가 걸린다.
2. **최악 경우에는 git이 압도적이다.** 전부 다름 1만 줄, 50% 수정, CRLF↔LF 경우 git은 11~20ms이고, Myers 계열(DiffPlex·diff-match-patch·직접 Myers)은 180~790ms로 최대 약 40배 느리다.
   - 원인(추정, xdiff 소스 기준): git의 xdiff는 한쪽에만 있는 줄을 Myers 전에 걸러 내고, 비용 상한을 넘으면 최소성을 포기하는 휴리스틱을 쓴다. 관리 코드 후보들은 O(ND)를 끝까지 계산한다.
   - 직접 Histogram은 50% 수정(24ms)과 뒤섞기(1.4ms)에서는 빠르다. 하지만 전부 다름과 CRLF 경우는 기준점이 없어 Myers로 넘어가 똑같이 느리다.
3. **같은 10만 줄 파일은 git이 5ms, 관리 코드 방식은 80~124ms다.** 관리 코드는 파일 전체를 문자열로 읽고 줄로 나누는 비용(할당 70~97MB)이 크다. 바이트가 같은지 먼저 확인하면 피할 수 있다.
4. **LibGit2Sharp는 기대와 달리 가장 느린 편이다(기하평균 0.5배).** 엔진은 git과 같은 xdiff지만, 비교할 때마다 두 파일을 압축해 blob으로 저장소에 써야 하기 때문이다. 같은 10만 줄 파일에 271ms가 걸렸다. 이 래퍼 API로는 피할 방법이 없다.
5. **결과 품질은 사실상 같다.** 20건 중 19건의 변경 줄 수가 모든 방식에서 같았다. 순서를 뒤섞은 경우만 다르다.
   - Myers 계열 3,826줄(최소)
   - git·libgit2 3,852줄(휴리스틱)
   - Histogram 3,976줄
6. **CRLF↔LF는 모든 방식이 1만 줄 전부를 다르다고 본다.** 앱의 "공백 무시"(`-w`)를 대체하려면 프로세스 안 방식도 같은 정규화를 직접 넣어야 한다.

### 주의 — 이 결과만으로 결론 내리면 안 되는 점

- **Linux 측정이다.** Linux에서 git 프로세스를 띄우는 비용은 약 3.5ms다. Windows의 `CreateProcess`는 보통 이보다 훨씬 비싸고, 백신 실시간 검사가 끼면 더 늘어난다. 그래서 1번(보통 수정) 차이는 Windows에서 더 커지고, 2번(최악) 차이는 줄어들 가능성이 크다. Windows 측정이 필요하다: `cd tools\DiffBench` → `dotnet run -c Release` → `results.md`.
- ms 단위가 작은 값(<1ms)은 측정 잡음이 크다. 순위보다 자릿수 차이로 읽어야 한다.
- 메모리 표는 관리 힙만 잰 값이다. git(별도 프로세스)과 libgit2(네이티브)의 메모리는 잡히지 않는다.

### 선택지에 대한 판단 (결정 전 참고)

- **git CLI 유지**: 최악 경우에 강하고 결과 품질도 검증돼 있다. 대신 git 설치가 필요하고, 매번 프로세스를 띄우는 고정 비용이 든다.
- **관리 코드로 교체**:
  - 대상은 직접 Histogram이거나, 3-way 병합까지 노린다면 DiffPlex다.
  - 그대로는 최악 경우가 수백 ms라 git보다 크게 느리다. 교체하려면 xdiff처럼 한쪽에만 있는 줄을 미리 걸러 내고 비용 상한을 두는 처리, 바이트 동일 선검사, 공백 무시가 함께 필요하다.
  - 화면(`GitDiffView`)은 unified 텍스트를 받으므로, 결과를 unified 형식으로 만드는 코드도 필요하다.
- **diff-match-patch**: .NET 판 고정 문제와 6만 3천 줄 한계 때문에 비추천.
- **LibGit2Sharp**: blob 쓰기 비용 때문에 비추천.

## 구현 내용

- `tools/DiffBench`(앱 솔루션 밖 콘솔): 테스트 20가지, 방식 6가지, 측정·검증·보고서(`results.md`), `--selftest`, `--write-cases`.

## 변경 파일

- `tools/DiffBench/*` (신규), `tools/DiffBench/results-linux.md`(이번 측정 결과), `docs/items/diff-engine-benchmark.md`
- `docs/reports/diff-engine-benchmark.html` — HTML 보고서. `tools/DiffBench/gen_report.py`가 결과 표(`results*.md`)와 `report_template.html`로 만든다: `python tools/DiffBench/gen_report.py <results.md> <out.html> tools/DiffBench/report_template.html`

## 검증

- `--selftest`: 무작위 2만 건, 직접 구현 2개 재구성 정확, Myers 최소 2만/2만.
- 전체 측정: 20 × 6 = 120개 조합. 관리 코드 방식 결과는 모두 재구성 검증 통과. diff-match-patch 2건은 지원 안 함(10만 줄).
- 미완: Windows에서 같은 측정.

## 변경 이력

- 2026-10-16: 요청 접수, 벤치마크 작성·Linux 측정(Ready for Verification). 앱 코드는 바꾸지 않음.
- 2026-10-16: 사용자 요청으로 HTML 보고서 추가(결론·요약 차트·경우별 히트맵·품질·메모리·방식별 판단).

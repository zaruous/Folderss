# Git 창 보낼/받을 커밋 순서가 콘솔 git과 다름

- 상태: Ready for Verification

## 요구사항

- CI `test`의 `GitLibReaderTests.UpstreamRanges_MatchCli`가 실패한다(PR #60에서 두 번 연속, 이 환경의 master에서도 매번 실패).
- Git 창 원격 탭의 보낼 커밋(`@{u}..HEAD`)·받을 커밋(`HEAD..@{u}`) 순서가 `git log a..b`와 같아야 한다.

## 원인 분석 또는 설계

- 원인: `GitLibReader.Range`가 libgit2 `CommitSortStrategies.Time`으로 정렬했다. 같은 초에 만든 커밋끼리는 git과 순서가 달라지고, 부모가 자식보다 먼저 나오기도 한다
  (실패 로그: `729bd6d`가 자식 `cd79320`보다 앞에 옴). 테스트 저장소는 커밋을 빠르게 연달아 만들어 날짜가 같은 경우가 많다.
- 테스트를 고치는 것(날짜를 1초씩 늘리기·정렬 후 비교)은 실제 앱의 순서 차이를 덮을 뿐이라, 앱 쪽을 git과 같게 고친다.
- git의 `a..b` 기본 출력 순서: 커밋 날짜 우선순위 큐에서 꺼내는 순서, 날짜가 같으면 먼저 넣은 것 먼저. 커밋을 꺼내면 범위 안의 부모를 부모 순서대로 넣는다.

## 구현 내용

- `GitLibReader.Range` — 범위 커밋 집합은 libgit2(`SortBy = None`)로 구하고, 순서는 새 `DateOrder`로 git과 같게 만든 뒤 `limit`만큼 자른다.

## 변경 파일

- `Folderss/Services/GitLibReader.cs`
- `docs/items/git-upstream-range-commit-order.md`
- `tests/Folderss.SearchTests/GitLibReaderTests.cs` — `UpstreamRanges_EmptyWhenUpToDate`(PR #58에서 가져옴)

## 검증

- 재현: 수정 전 `UpstreamRanges_MatchCli` 3회 연속 실패(Linux) → 수정 후 `GitLibReaderTests` 13개 3회 연속 통과.
- 전체 테스트(Linux): 362 통과 / 2 실패(`DirectoryCompareServiceTests` 2개 — 변경 전에도 이 환경에서만 실패, Windows CI에서는 통과).
- Linux 컴파일: 기존 `ConsolePanel` `Microsoft.Terminal` 오류 1건 외 새 오류 없음.
- Windows: Git 창 원격 탭에서 보낼/받을 커밋 목록 순서가 `git log @{u}..HEAD` / `git log HEAD..@{u}`와 같은지 확인.

## 변경 이력

- 2026-10-16: PR #60 CI 실패에서 발견. 수정·테스트 완료.
- 2026-10-17: PR #58이 같은 문제를 위상 순서(`--topo-order`/`TopoOrder`)로 따로 고쳐 충돌. 콘솔 `git log a..b`와 같은 이 방식으로 통일하고,
  PR #58의 CLI `--topo-order`·문서 항목(`git-upstream-range-order.md`)은 빼고 빈 범위 테스트만 남김.

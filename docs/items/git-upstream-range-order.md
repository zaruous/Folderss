# Git 창 원격 비교 — 같은 초 커밋의 순서가 조회 경로마다 다름

- 상태: Ready for Verification — 구현·단위 테스트 완료, Windows 화면 확인 대기

## 요구사항

- Git 창 "원격과 비교"의 보낼 커밋(`@{u}..HEAD`)·받을 커밋(`HEAD..@{u}`) 목록은 두 경로로 읽는다.
  - 기본: LibGit2Sharp(`GitLibReader.ReadUpstreamRanges`)
  - 실패 시 대체: git CLI(`GitDiffCommands.LogRange`)
- 두 경로의 결과가 같아야 한다(`GitLibReaderTests`가 지키는 규칙). 그런데 커밋 시각이 같은 초인 커밋끼리는 순서가 달랐다.
- `GitLibReaderTests.UpstreamRanges_MatchCli`가 이 불일치를 잡아 Linux에서 매번 실패했다(3/3). 처음에는 테스트 문제로 오진했으나, 픽스처는 시각 동점을 일부러 만든다.

## 원인 분석 또는 설계

- CLI는 `git log A..B`(기본 날짜 순서)이고, LibGit2Sharp 경로는 `CommitSortStrategies.Time`이다. 둘 다 날짜순이지만 동점 처리 규칙이 다르다.
- 메인 로그는 이미 CLI `--topo-order`와 그것을 그대로 옮긴 `GitLibReader.TopoOrder`로 두 경로를 일치시켰다(동점 포함 테스트 통과). 원격 비교도 같은 방식으로 맞춘다.
  - git의 기본 날짜 순서 걷기(우선순위 큐·삽입 순서)를 LibGit2Sharp 쪽에 따로 옮기는 안은, 복잡하고 검증 부담이 커서 택하지 않았다.
- 화면 변화: 원격 비교 목록이 날짜순 대신 위상 순서(메인 로그와 같은 규칙)로 보인다. 시각이 서로 다른 일반 커밋에서는 대부분 같은 순서다.
- 비용: 시각 정렬은 앞에서부터 한도만큼만 읽을 수 있었지만, 위상 순서는 범위 전체를 읽은 뒤 정렬한다. CLI `--topo-order`도 같다. upstream과 수천 커밋 벌어진 경우 조금 느려질 수 있다.

## 구현 내용

- `GitDiffCommands.LogRange`: `--topo-order` 추가.
- `GitLibReader.Range`: 범위 커밋을 모은 뒤 `TopoOrder`로 정렬한다. 범위가 비면 빈 목록을 돌려준다(`TopoOrder`는 시작 커밋을 늘 내보내므로 이 분기가 필요).
- `docs/architecture.md` 원격 비교 명령 설명 갱신.

## 변경 파일

- `Folderss/Services/GitDiffCommands.cs`, `Folderss/Services/GitLibReader.cs`
- `tests/Folderss.SearchTests/GitLibReaderTests.cs` — `UpstreamRanges_EmptyWhenUpToDate` 추가
- `docs/architecture.md`

## 검증

- 수정 전: `UpstreamRanges_MatchCli` 3회 연속 실패. 커밋 집합은 같고 순서만 다름.
- 수정 후: `GitLibReaderTests` 13건 3회 연속 통과. 전체 333 통과·3 건너뜀·0 실패.
- 새 테스트 `UpstreamRanges_EmptyWhenUpToDate`: 빈 범위 분기를 빼면 실패하는 것을 확인한 뒤 되돌렸다.
- 앱 본체 Linux 컴파일: 기존 콘솔 패널 오류 1종 외에 새 오류 없음.
- Windows에서 확인할 것: Git 창 원격 비교 목록이 표시되는지, 순서가 메인 로그 그래프와 어긋나지 않는지.

## 변경 이력

- 2026-10-15: 테스트 실패 분석 중 앱의 실제 불일치로 확인, 두 경로 모두 위상 순서로 통일(Ready for Verification).

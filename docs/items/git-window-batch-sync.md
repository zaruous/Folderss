# Git 창 툴바: 전체 pull/push 추가, 전체·선택 저장소 버튼 그룹화

- 상태: Ready for Verification

## 요구사항

- Git 처리 팝업(`GitWindow`)에 `전체 fetch`만 있던 것을 `전체 pull`·`전체 push`까지 둔다.
- "전체" 버튼들을 한 그룹으로 묶고, 선택한 저장소에만 적용되는 버튼(fetch·pull·push)을 다른 그룹으로 묶어 대상이 한눈에 구분되게 한다. 선택 저장소용 `fetch`는 새로 추가한다.

## 원인 분석 또는 설계

- 기존 설계 메모(`GitWindow.xaml.cs` 머리 주석, README)는 일괄 동작을 status·fetch로 제한했다(부분 실패 복구가 어렵다는 이유). 사용자 요청으로 pull/push를 추가하되 위험을 줄이는 쪽으로 기본값을 둔다.
  - 전체 pull 기본은 설정 > Git의 pull 방식(기본 `--ff-only`)이라 갈라진 저장소는 아무것도 바꾸지 않고 실패만 남는다. ▾ 대화상자에서 병합·rebase를 고르면 충돌 시 저장소마다 따로 해결해야 한다는 문구를 보인다.
  - 전체 push는 현재 브랜치를 upstream으로만 보낸다(`git push`). upstream이 없거나 detached HEAD인 저장소는 건너뛰고 `-u`를 붙이지 않는다(원격 선택은 저장소마다 다른 결정이라 단일 push에서만 묻는다). 강제 푸시는 없다.
  - 건너뛸지 판단하는 규칙은 `GitSyncCommands.BatchSkipReason(snapshot)`(순수 함수)로 두고 테스트한다.
- 일괄 실행은 기존 전체 fetch처럼 저장소마다 동시에 돌고(`Task.WhenAll`), 끝나면 `성공/실패/건너뜀` 수를 출력 영역에 요약한다. 전체 fetch·pull·push가 같은 `RunOnAllAsync`를 쓴다.
- 전체 pull 전에는 단일 pull과 같이 저장하지 않은 문서 탭을 확인한다(기준 폴더 또는 상위 저장소 아래 전체).
- 툴바는 `전체` / `선택 저장소` 두 테두리 그룹(이름표 + 버튼)으로 나누고, 버튼 이름은 그룹 안에서 `fetch`·`pull`·`push`로 통일한다.

## 구현 내용

- `Folderss/Services/GitSyncCommands.cs` — `BatchSkipReason`.
- `Folderss/GitWindow.xaml` — 툴바 그룹(`GitToolbarGroup`·`GitToolbarGroupLabel` 스타일), `PullAllButton`·`PullAllOptionsButton`·`PushAllButton`, `FetchButton`·`FetchOptionsButton`.
- `Folderss/GitWindow.xaml.cs` — `RunOnAllAsync`(전체 fetch를 이것으로 옮김), `PullAll_Click`·`PullAllOptions_Click`·`PushAll_Click`, `Fetch_Click`·`FetchOptions_Click`, `UpdateButtons` 활성 조건.
- `tests/Folderss.SearchTests/GitTests.cs` — `BatchSkipReason` 테스트.
- `README.md`, `docs/architecture.md` — 일괄 동작 설명 갱신.

## 변경 파일

- `Folderss/GitWindow.xaml`
- `Folderss/GitWindow.xaml.cs`
- `Folderss/Services/GitSyncCommands.cs`
- `tests/Folderss.SearchTests/GitTests.cs`
- `README.md`
- `docs/architecture.md`
- `docs/items/git-window-batch-sync.md` (신규)

## 검증

- [x] `dotnet build Folderss.sln -c Debug -p:EnableWindowsTargeting=true`(리눅스) — 새 오류 없음. 기존부터 리눅스에서만 나는 `Microsoft.Terminal` 참조 오류 1건은 변경 전과 동일 (Windows에서 `dotnet build .\Folderss.sln -c Debug` Exit 0 확인은 사용자)
- [x] `dotnet test tests/Folderss.SearchTests` — 251 통과(`BatchSkipReason_SkipsUnreadDetachedAndNoUpstream_AllowsTrackedBranch` 포함), 1 건너뜀(git 없음)
- [ ] Windows에서 Git 창을 열어 그룹 배치·전체 pull/push·선택 저장소 fetch 동작 확인 (사용자)

## 변경 이력

- 2026-10-12 요청 접수, 설계·구현 착수.
- 2026-10-12 구현·빌드·테스트 완료, 사용자 확인 대기.

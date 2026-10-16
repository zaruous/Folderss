# 폴더 패널 트리뷰 열림 상태 유지

- 상태: Ready for Verification

## 요구사항

- 폴더 패널에서 트리뷰를 연 상태로 프로그램을 종료하면, 다시 켤 때 그 패널에 트리뷰가 열린 상태로 복원되어야 한다.

## 원인 분석 또는 설계

- 원인: 트리뷰 표시 여부(`FolderBrowser._treeViewVisible`)는 메모리에만 있고 `session.xml`(`SessionState`)에 저장하지 않았다. 패널은 시작할 때마다 트리뷰가 닫힌 상태로 만들어진다.
- 저장 대상: 트리뷰가 열린 패널마다 패널 키, 트리 루트 폴더, 고정(📌) 여부.
  - 트리를 연 뒤 하위 폴더로 이동하면 루트는 처음 폴더로 남아 있으므로(고정 시) 현재 폴더와 루트가 다를 수 있어 루트를 따로 저장한다.
  - 트리 너비(스플리터)는 지금도 열 때마다 220으로 시작하므로 저장하지 않는다(요청 범위 밖).
- 패널 식별: 도킹 탭 ContentId 기반 키 — `left-folder`, `right-folder`, `folder-panel|<패널 ID>`(폴더를 옮겨도 그대로, 패널 잠금 키와 같은 방식). 왼쪽·오른쪽은 패널 잠금 대상이 아니라 `GetPanelLockKey`를 쓰지 않고 직접 만든다.
- 복원 시점: 레이아웃 복원·`ApplyPanelLockStates()` 직후. 루트 폴더가 없어졌으면 현재 폴더를 루트로 연다.
- 예전 `session.xml`(항목 없음)은 빈 목록으로 읽힌다.

## 구현 내용

- `SessionState.TreeViews`(`FolderTreeViewState`: `PanelKey`, `RootPath`, `Pinned`).
- `FolderBrowser.TreeRootPath`, `FolderBrowser.RestoreTreeView(rootPath, pinned)`.
- `MainWindow.SaveSessionState` — 트리뷰가 열린 패널을 저장. `MainWindow.RestoreTreeViewStates`, `GetFolderPanelKey` 추가.

## 한계

- 레이아웃 복원에 실패해 기본 배치로 시작하면 추가 패널은 새 ID로 만들어지므로 왼쪽·오른쪽 패널만 트리뷰가 복원된다.
- (기존 동작, 이번 범위 밖) 패널 최대화 후 되돌리면 최대화하지 않은 추가 패널은 `ResolveDockContent`로 새로 만들어져 트리뷰가 닫힌다.

## 변경 파일

- `Folderss/Services/SessionStateService.cs`
- `Folderss/Controls/FolderBrowser.xaml.cs`
- `Folderss/MainWindow.xaml.cs`
- `tests/Folderss.SearchTests/SessionStateTests.cs`, `tests/Folderss.SearchTests/Folderss.SearchTests.csproj`(SessionStateService.cs 링크)
- `README.md`
- `docs/items/folder-panel-tree-view-restore.md`

## 검증

- 단위 테스트 `SessionStateTests`(저장·읽기 왕복, 예전 session.xml 호환): 구현 전 컴파일 실패 확인 → 구현 후 통과.
- 전체 테스트(Linux): 363 통과 / 3 실패. 실패 3개(`DirectoryCompareServiceTests` 2, `GitLibReaderTests.UpstreamRanges_MatchCli`)는 변경 전 master에서도 이 환경에서 똑같이 실패(이번 변경과 무관, CI에서는 통과).
- Linux `EnableWindowsTargeting` 컴파일: 기존 `ConsolePanel` `Microsoft.Terminal` 오류 1건 외 새 오류 없음.
- Windows 수동 확인 필요:
  1. 왼쪽 패널 트리뷰 열기 → 종료 → 실행: 왼쪽에 트리뷰가 열려 있고 📌 고정 상태.
  2. 트리뷰를 연 뒤 하위 폴더로 이동 → 종료 → 실행: 트리 루트는 처음 폴더, 현재 폴더는 하위 폴더, 트리에서 현재 폴더 선택.
  3. 📌 해제 → 종료 → 실행: 트리뷰 열림, 고정 해제 상태.
  4. 추가 패널(＋ 새 패널)에서 트리뷰 열기 → 종료 → 실행: 그 패널만 트리뷰 열림.
  5. 트리뷰 닫고 종료 → 실행: 닫힌 상태.
  6. 트리 루트 폴더를 지운 뒤 실행: 현재 폴더 루트로 트리뷰 열림, 오류 없음.
  7. 트레이로 숨겼다가 트레이 메뉴로 종료해도 같은 결과.

## 변경 이력

- 2026-10-16: 요청 접수, 구현·단위 테스트 완료, Windows 확인 대기.

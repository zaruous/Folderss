# 트리뷰 새로고침 시 선택 폴더 하위만 갱신

- 상태: Ready for Verification

## 요구사항

트리뷰의 새로고침(↻) 버튼을 누르면 전체 트리를 다시 만들지 말고, 트리에서 선택한 폴더의 하위 트리만 갱신해야 한다.

## 원인 분석 또는 설계

기존 `RefreshTreeView_Click`은 `RebuildFolderTree()`를 호출해 루트부터 트리를 전부 지우고 다시 만들었다. 이 때문에 루트 이외에 펼쳐 둔 다른 폴더의 펼침 상태가 모두 사라지고, 큰 트리에서는 불필요한 디스크 열람이 발생했다.

- `FolderTree.SelectedItem`(선택한 `TreeViewItem`)의 `Tag` 경로가 유효하면 기존 쉘 메뉴 후처리에서 쓰던 `ReloadTreeChildren(item)`으로 해당 노드의 자식만 다시 로드한다. 선택 노드 자체와 그 형제·상위 노드는 그대로 유지된다.
- 선택 항목이 없거나(트리에 아직 선택이 없는 경우) 선택 폴더가 더 이상 존재하지 않으면 기존처럼 `RebuildFolderTree()`로 전체 재구성한다. 삭제된 폴더 노드는 부모를 다시 읽어야 사라지므로 이 경우만 전체 재구성을 유지했다.
- 갱신 대상 노드의 하위 폴더들이 펼쳐져 있던 상태는 보존하지 않는다(자식 노드가 새로 만들어지므로 접힌 상태로 다시 생성됨). 선택 노드 자신의 펼침 상태는 그대로다.
- 선택 노드가 접힌 상태여도 자식을 즉시 로드한다. 이후 펼칠 때 `EnsureTreeChildrenLoaded`는 이미 실제 자식(`Tag != null`)이 있으므로 재로드하지 않는다.

## 구현 내용

- `RefreshTreeView_Click` — 선택 노드가 있고 경로가 존재하면 `ReloadTreeChildren(selected)`만 호출, 그 외에는 `RebuildFolderTree()` 유지
- 새로고침 버튼 ToolTip을 동작에 맞게 변경

## 변경 파일

- `Folderss/Controls/FolderBrowser.xaml` — 새로고침 버튼 ToolTip
- `Folderss/Controls/FolderBrowser.xaml.cs` — `RefreshTreeView_Click`
- `README.md` — 현재 기능 목록 반영
- `docs/items/tree-view-selective-refresh.md`

## 검증

- [ ] `dotnet build .\Folderss.sln -c Debug` 성공 (`Exit: 0`) — 작업 환경(Linux, dotnet SDK 없음)에서 빌드 불가, Windows 개발 환경에서 확인 필요
- [ ] 트리에서 A/B 폴더를 선택하고 다른 가지 A/C를 펼친 뒤, 탐색기에서 A/B 아래에 폴더를 추가하고 ↻ 클릭 → A/B 아래에 새 폴더가 나타나고 A/C의 펼침 상태는 유지됨
- [ ] A/B 아래 폴더를 탐색기에서 삭제 후 ↻ 클릭 → A/B 아래에서만 사라짐
- [ ] 선택 폴더 자체를 탐색기에서 삭제 후 ↻ 클릭 → 전체 트리 재구성(기존 동작)으로 삭제된 노드가 사라짐
- [ ] 하위 폴더가 없어 화살표가 없던 폴더를 선택하고 하위 폴더를 만든 뒤 ↻ 클릭 → 펼침 화살표가 나타나고 펼치면 새 폴더가 보임

## 변경 이력

- 2026-09-22: 초기 구현

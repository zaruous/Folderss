# 파일트리 복사 시 폴더 이름/실제 파일 복사 지원

- 상태: Ready for Verification

## 요구사항

파일트리(왼쪽 `FolderTree`)에서 폴더를 선택한 상태로 복사하면 폴더 이름이 클립보드에 텍스트로 복사되어야 한다.
동시에 가능하면 실제 파일(폴더) 복사도 지원해, 이 앱이나 탐색기에 붙여넣었을 때 실제 폴더가 복사되도록 한다.

## 원인 분석 또는 설계

- 기존 `Copy_CanExecute`/`CopySelectedToClipboard`는 `SelectedItems`(오른쪽 `FileList`의 선택 항목)만 참조해서,
  파일트리에 포커스가 있고 오른쪽 목록에 선택 항목이 없을 때는 Ctrl+C가 아무 동작도 하지 않았다.
- 해결: 오른쪽 목록 선택이 없고 파일트리(`FolderTree`)에 키보드 포커스가 있으면, 선택된 트리 노드의 경로를 복사 대상으로 사용한다.
- 폴더 이름(텍스트)과 실제 경로(FileDrop)를 하나의 `DataObject`에 함께 담아 `Clipboard.SetDataObject`로 설정하면,
  텍스트 붙여넣기(폴더 이름)와 파일 붙여넣기(실제 복사) 모두 동일한 복사 동작으로 지원된다.

## 구현 내용

- `GetFolderTreeCopyPath()` — 오른쪽 목록 선택이 없고 `FolderTree.IsKeyboardFocusWithin`일 때 선택된 `TreeViewItem.Tag`(경로) 반환
- `Copy_CanExecute`에 `GetFolderTreeCopyPath() != null` 조건 추가
- `CopySelectedToClipboard()`에서 트리 복사 대상이 있으면 `CopyFolderTreeSelectionToClipboard()`로 위임
- `CopyFolderTreeSelectionToClipboard(path)` — 폴더 이름을 `SetText`, 실제 경로를 `SetFileDropList`로 같은 `DataObject`에 설정 후 `Clipboard.SetDataObject(dataObject, true)`

## 변경 파일

- `Folderss/Controls/FolderBrowser.xaml.cs`

## 검증

- [ ] 파일트리에서 폴더 선택 후 Ctrl+C → 메모장 등에 붙여넣기 시 폴더 이름 텍스트 확인
- [ ] 파일트리에서 폴더 선택 후 Ctrl+C → 이 앱의 다른 패널 또는 탐색기에 붙여넣기 시 실제 폴더 복사 확인
- [ ] 오른쪽 파일 목록에서 항목 선택 후 Ctrl+C → 기존 동작(다중 파일 복사) 유지 확인
- [ ] 빌드 성공 확인 (Windows/MSBuild 환경에서 확인 필요 — 이 세션은 Linux 환경이라 직접 빌드 불가)

## 변경 이력

- 2026-07-14: 초기 구현

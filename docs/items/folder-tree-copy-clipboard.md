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
- MainWindow의 전역 `CopyClipboard` 단축키 처리(`Window_PreviewKeyDown`)는 `ActivePane.SelectedItems.Count > 0`일 때만 `e.Handled`를 설정하므로,
  파일트리 포커스처럼 오른쪽 목록 선택이 없는 경우엔 이벤트가 그대로 통과해 `ApplicationCommands.Copy`의 기본 제스처 변환을 거쳐
  `FolderBrowser`의 `CommandBinding`(`Copy_Executed`)이 정상적으로 실행됨을 확인 — 별도 라우팅 수정 불필요.

### 추가 검토: 동일 단축키(Ctrl+C, `CopyClipboard`)를 쓰는 다른 복사 기능

- `FavoritesPanel.CopySelectedFavoritePath()`도 같은 `CopyClipboard` 단축키로 호출되지만 기존엔 `Clipboard.SetText(favorite.Path)`만 사용해
  텍스트 복사만 지원하고 실제 파일 복사는 불가능했다.
- 즐겨찾기는 폴더뿐 아니라 파일도 등록 가능(`FavoriteLocation.IsFile`)하므로 파일트리와 동일하게 `DataObject`에
  텍스트(기존 동작 유지 위해 즐겨찾기 전체 경로 텍스트)와 `FileDropList`(실제 경로)를 함께 담아 `Clipboard.SetDataObject`로 교체.
  경로 텍스트는 기존 동작(전체 경로)을 유지해 회귀 없음 — 폴더트리처럼 이름만으로 바꾸지 않음.
- 대상 경로가 더 이상 존재하지 않는 즐겨찾기(끊어진 항목)는 기존처럼 텍스트만 복사하도록 폴백 유지.
- `MainWindow.CopyToClipboard()`/`CutToClipboard()`(MainWindow.xaml.cs)는 어디서도 호출되지 않는 미사용 코드로 확인 — 실제 단축키 경로가 아니므로 이번 수정 대상에서 제외.

## 변경 파일

- `Folderss/Controls/FolderBrowser.xaml.cs`
- `Folderss/Controls/FavoritesPanel.xaml.cs`

## 검증

- [ ] 파일트리에서 폴더 선택 후 Ctrl+C → 메모장 등에 붙여넣기 시 폴더 이름 텍스트 확인
- [ ] 파일트리에서 폴더 선택 후 Ctrl+C → 이 앱의 다른 패널 또는 탐색기에 붙여넣기 시 실제 폴더 복사 확인
- [ ] 오른쪽 파일 목록에서 항목 선택 후 Ctrl+C → 기존 동작(다중 파일 복사) 유지 확인
- [ ] 즐겨찾기 패널에서 항목 선택 후 Ctrl+C → 텍스트 붙여넣기 시 기존처럼 전체 경로 텍스트 확인
- [ ] 즐겨찾기 패널에서 항목 선택 후 Ctrl+C → 탐색기/다른 패널에 붙여넣기 시 실제 파일·폴더 복사 확인
- [ ] 끊어진(존재하지 않는 경로) 즐겨찾기에서 Ctrl+C → 기존처럼 텍스트만 복사되고 오류 없음 확인
- [ ] 빌드 성공 확인 (Windows/MSBuild 환경에서 확인 필요 — 이 세션은 Linux 환경이라 직접 빌드 불가)

## 변경 이력

- 2026-07-14: 초기 구현 (파일트리 복사)
- 2026-07-14: 즐겨찾기 패널의 동일 단축키(Ctrl+C) 복사 기능도 같은 방식(DataObject 텍스트+FileDrop)으로 교체

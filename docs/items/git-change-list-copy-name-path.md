# Git 창 변경 사항 목록·트리 이름/경로 복사

- 상태: Ready for Verification

## 요구사항

- Git 창 변경 사항에서 항목을 고르고 `Ctrl+C`를 눌러도 파일 이름이 복사되지 않는다(평면 목록·트리 보기 모두).
- `Ctrl+C`는 파일 이름, `Ctrl+Shift+C`는 전체 경로를 클립보드에 복사한다.
- 트리에서 폴더 노드를 고르면 폴더 이름 / 폴더 전체 경로(폴더 패널 트리와 같은 방식, 아래 파일 전부가 아님).

## 원인 분석 또는 설계

- 원인: Git 창 변경 사항 목록(`ListBox`)·트리(`TreeView`)에 복사 처리가 아예 없었다. 폴더 패널의 복사(`FolderBrowser.CopySelectedToClipboard`)는 폴더 패널에만 묶여 있다.
- Git 창 단축키는 다른 키(`Ctrl+Enter` 커밋, `Delete`)처럼 창 안에서 직접 처리한다(설정 창 키 바인딩 대상 아님).
- 텍스트만 넣는다(FileDrop 없음). 삭제된 파일 등 디스크에 없는 항목도 있고, 탐색기에 붙여넣어 파일이 복사되면 안 되기 때문.
- 대상: 변경됨·스테이지됨·변경 없음의 평면 목록과 트리. 변경됨·스테이지됨 트리는 다중 선택(`IsMarked`)한 노드 전부, 변경 없음 트리는 선택 노드.

## 구현 내용

- `GitChangeTree.ClipboardText(relativePaths, rootPath, fullPath)` — 한 줄에 하나, 같은 경로 중복 제거, 추적 안 되는 폴더(`dir/`)의 끝 슬래시 제거. 이름은 마지막 경로 조각, 전체 경로는 저장소 루트 + 상대 경로.
- `GitWindow.xaml` — 6개 목록·트리에 `PreviewKeyDown="ChangeCopy_PreviewKeyDown"`.
- `GitWindow.xaml.cs` — `ChangeCopy_PreviewKeyDown`, `SelectedCopyPaths`. 고른 것이 없으면 키를 가로채지 않는다.

## 변경 파일

- `Folderss/Services/GitChangeTree.cs`
- `Folderss/GitWindow.xaml`
- `Folderss/GitWindow.xaml.cs`
- `tests/Folderss.SearchTests/GitTests.cs`
- `README.md`
- `docs/items/git-change-list-copy-name-path.md`

## 검증

- 단위 테스트 `ChangeTree_ClipboardText_NamesOrFullPaths_OnePerLine`: 구현 전 컴파일 실패 확인 → 구현 후 Git 테스트 74개 통과(Linux).
- Linux `EnableWindowsTargeting` 컴파일: 기존 `ConsolePanel` `Microsoft.Terminal` 오류 1건 외 새 오류 없음. Windows `dotnet build` 확인 필요.
- Windows 수동 확인 필요:
  1. 평면 목록에서 파일 2개 선택 → `Ctrl+C` → 메모장에 이름 2줄. `Ctrl+Shift+C` → 전체 경로 2줄.
  2. 트리 보기에서 파일/폴더 노드 `Ctrl+클릭`으로 골라 같은 확인(폴더는 폴더 이름/경로).
  3. 변경 없음 목록·트리에서도 동작. 아무것도 고르지 않으면 아무 일 없음.
  4. 커밋 메시지 입력란·diff 보기의 `Ctrl+C`는 기존대로(이 처리와 무관).

## 변경 이력

- 2026-10-16: 요청 접수, 대상(Git 창)·폴더 노드 처리(폴더 이름/경로) 사용자 확인. 구현·단위 테스트 완료, Windows 확인 대기.

# 폴더 트리에서 Ctrl+Shift+C로 폴더 전체 경로 복사

- 상태: Done

## 요구사항

폴더 패널 왼쪽 트리뷰(`FolderTree`)에서 폴더를 선택한 뒤 `Ctrl+Shift+C`를 누르면 그 폴더의 전체 경로를 클립보드에 복사한다.

## 원인 분석 또는 설계

- 트리에서 `Ctrl+C`는 이미 폴더 자체를 복사한다(FileDrop + 폴더 이름 텍스트, [[folder-tree-copy-clipboard]]). 경로 복사는 그와 구분해 **경로 텍스트만** 담는다.
  FileDrop을 같이 넣으면 탐색기에 붙여넣었을 때 폴더가 통째로 복사되므로 넣지 않는다.
- 다른 단축키처럼 `KeyBindingService`에 `CopyFolderPath`(폴더 경로 복사)로 등록해 `설정 > 단축키`에서 바꿀 수 있게 했다. 기존 사용자도 `Load`의 기본값 병합으로 새 항목이 생긴다.
- 처리 위치는 `MainWindow.Window_PreviewKeyDown`의 패널 단축키 구간(텍스트 입력·뷰어·콘솔·즐겨찾기 포커스는 그 앞에서 걸러짐).
  트리에 키보드 포커스가 있는 패널만 복사하고, 파일 목록에 포커스가 있으면 아무 것도 하지 않는다(키를 넘김).
- 클립보드 쓰기는 `ClipboardService.TrySetDataObject`(점유 실패 시 경고). 성공하면 다른 복사와 같이 잘라내기 표시를 지운다.

## 구현 내용

- `Services/KeyBindingService.cs` — 기본값 `CopyFolderPath` = `Ctrl+Shift+C`.
- `Controls/FolderBrowser.xaml.cs` — `CopyFolderTreePathToClipboard()`: 트리 포커스 + 선택 항목의 `Tag`(전체 경로)를 텍스트로 복사. 트리 포커스가 없거나 선택이 없으면 false.
- `MainWindow.xaml.cs` — `kb.Matches(e, "CopyFolderPath")`에서 모든 폴더 패널에 위 메서드를 시도해 하나라도 true면 처리.

## 변경 파일

- `Folderss/Services/KeyBindingService.cs`
- `Folderss/Controls/FolderBrowser.xaml.cs`
- `Folderss/MainWindow.xaml.cs`
- `README.md`, `docs/keyboard-shortcuts.md`

## 한계

- 파일 목록에서 선택한 파일·폴더의 경로 복사는 이번 범위가 아니다(요청 범위: 트리).
- 선택한 폴더가 그 사이 삭제됐어도 존재 여부를 확인하지 않고 경로 글자를 복사한다.
- 트리 우클릭 메뉴는 이번에 바꾸지 않았다(메뉴 항목·`InputGestureText` 없음).

## 검증

- [x] `dotnet test tests/Folderss.SearchTests` 통과 (본체 WPF 빌드는 Linux에서 기존 `ConsolePanel` 터미널 참조 오류로 확인 불가 → PR 빌드(Windows)에서 확인)
- [ ] 트리에서 폴더 선택 → `Ctrl+Shift+C` → 메모장에 붙여넣으면 전체 경로(예: `C:\work\project\src`)
- [ ] 드라이브 루트 선택 시 `C:\`
- [ ] 같은 상태에서 탐색기에 붙여넣기 → 폴더가 복사되지 않음
- [ ] 파일 목록 포커스에서 `Ctrl+Shift+C` → 아무 동작 없음
- [ ] 경로 입력란·뷰어·콘솔 포커스에서 `Ctrl+Shift+C` → 기존 동작 그대로
- [ ] `Ctrl+X`로 잘라낸 뒤 `Ctrl+Shift+C` → 잘라내기 표시가 사라짐
- [ ] `설정 > 단축키`에 "폴더 경로 복사"가 보이고 다른 키로 바꾸면 그 키로 동작

## 변경 이력

- 2026-10-14: `v1.8.4` 태그에 포함(PR #53, 브랜치 `ccr-48e9cc42-k8vulr`, 머지 `991516a`). 태그 릴리스 기준으로 완료(Done) 처리.

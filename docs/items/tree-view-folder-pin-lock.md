# 트리뷰 폴더 고정 및 구조 변경 잠금

- 상태: Ready for Verification

## 요구사항

트리뷰에 폴더 고정 버튼을 추가하고, 고정을 선택하면 현재 폴더의 구조를 바꾸지 못하게 한다.

- 트리뷰 헤더에 고정(📌) 토글 버튼 제공
- 고정 시 해당 폴더(트리 루트) 및 하위의 구조 변경 작업(생성·삭제·이름변경·이동·붙여넣기·드래그 앤 드롭)을 앱 안에서 차단
- 고정 해제 또는 트리뷰 닫기 시 잠금 해제
- 트리뷰를 열면 고정 토글이 기본으로 선택된 상태여야 함 (추가 요청)

## 원인 분석 또는 설계

`FolderBrowser`의 트리뷰는 탐색 시점마다 루트를 재고정하는 구조라서, 고정 개념을 넣으려면 (1) 트리 루트 유지와 (2) 구조 변경 차단 두 가지를 함께 설계해야 한다.

- 고정 상태는 패널(`FolderBrowser`) 인스턴스별로 관리하고, 고정 시점의 트리 루트 경로를 정규화해 `_pinnedPath`에 저장
- 잠금 판정은 두 가지로 분리
  - `IsPinLockedPath(path)`: 고정 폴더 자신·하위 항목·고정 폴더를 포함하는 상위 경로. 삭제·이름변경·이동 소스 차단용
  - `IsPinLockedDestination(dir)`: 고정 폴더 자신 또는 하위 폴더. 생성·복사·이동·붙여넣기 대상 차단용
- 파일 작업 진입점이 여러 패널에 걸쳐 있으므로(반대편 패널로 이동, 즐겨찾기에서 새 폴더 등) `MainWindow`가 모든 `FolderBrowser`를 순회하는 `IsPathPinLocked` / `IsDestinationPinLocked` 헬퍼를 제공하고, 각 작업 진입점에서 이를 검사
- 고정 중에는 일반 탐색으로 이동해도 트리 루트를 재고정하지 않고 선택만 동기화
- 잠금은 앱 내부 작업에만 적용된다. Windows 쉘 컨텍스트 메뉴나 외부 프로그램에 의한 변경은 차단할 수 없다 (드래그로 고정 폴더 밖 외부 앱에 떨어뜨리는 이동은 허용 효과에서 Move를 제외해 방지)

## 구현 내용

- 트리뷰 헤더에 고정(📌) `ToggleButton` 추가 (`CompactToggleButtonStyle`), 고정 시 트리 루트 경로 텍스트에 📌 접두어 표시
- 트리뷰를 열면(`SetTreeViewVisible(true)`) 고정을 기본으로 켜고, 토글로 해제 가능
- `FolderBrowser`에 `_pinnedPath` 상태, `IsFolderPinned`/`PinnedPath` 속성, `IsPinLockedPath`/`IsPinLockedDestination` 판정 메서드 추가
- 고정 중 일반 탐색 시 트리 루트 유지(`SyncTreeAfterNavigation`), 트리뷰 닫기 시 자동 고정 해제
- `MainWindow`에 전 패널 순회 잠금 판정 헬퍼와 공용 안내 메시지(`ShowPinLockedMessage`) 추가
- 차단 지점
  - `MainWindow.DeleteSelected` — 선택 항목에 잠금 경로 포함 시 차단
  - `MainWindow.Rename_Click` — 잠금 경로 이름 변경 차단
  - `MainWindow.NewFolder_Click` / `NewFile_Click` — 잠금 대상 폴더 내 생성 차단
  - `MainWindow.ExecuteTransfer` — 잠금 대상으로 복사·이동, 잠금 경로의 이동 차단
  - `MainWindow.TryPasteFromClipboardInto` — 잠금 대상 붙여넣기, 잘라내기 원본이 잠금 경로인 붙여넣기 차단
  - `FolderBrowser.FileList_DragOver` / `FileList_Drop` — 잠금 대상 드롭, 잠금 경로의 이동 드롭 차단 (드래그 중 상태 표시줄 안내)
  - `FolderBrowser.FileList_PreviewMouseMove` — 잠금 경로 드래그 시작 시 허용 효과에서 Move 제외 (외부 앱으로의 이동 방지)
  - `FavoritesPanel.NewFolder_Click` / `NewFile_Click` — 즐겨찾기 폴더가 잠금 대상이면 생성 차단

## 변경 파일

- `Folderss/Controls/FolderBrowser.xaml` — 트리뷰 헤더 고정 토글 버튼 추가
- `Folderss/Controls/FolderBrowser.xaml.cs` — 고정 상태 관리, 잠금 판정, 트리 루트 유지, 드래그 앤 드롭 차단
- `Folderss/MainWindow.xaml.cs` — 전 패널 잠금 판정 헬퍼, 삭제·이름변경·생성·이동·붙여넣기 차단
- `Folderss/Controls/FavoritesPanel.xaml.cs` — 즐겨찾기 새 폴더/새 파일 생성 차단
- `README.md` — 현재 기능 목록에 폴더 고정 반영
- `docs/architecture.md` — FolderBrowser 설명 보강
- `docs/items/tree-view-folder-pin-lock.md`

## 검증

- [ ] `dotnet build .\Folderss.sln -c Debug` 성공 (`Exit: 0`) — 작업 환경(Linux, 네트워크 정책상 SDK 설치 불가)에서 빌드 불가, Windows 개발 환경에서 확인 필요
- [ ] 트리뷰를 열면 📌 토글이 기본 선택 상태로 표시되고 잠금이 즉시 적용
- [ ] 트리뷰 헤더 📌 클릭 시 고정 해제/재고정, 고정 시 루트 경로 텍스트에 📌 표시
- [ ] 고정 상태에서 새 폴더/새 파일/이름 변경/삭제 시 안내 후 차단
- [ ] 고정 폴더로 반대편 패널 복사·이동, 붙여넣기, 드래그 앤 드롭 차단
- [ ] 고정 폴더 하위 항목을 다른 폴더로 이동(잘라내기+붙여넣기, 드래그 이동) 차단, 복사는 허용
- [ ] 고정 상태에서 하위 폴더 탐색 시 트리 루트 유지
- [ ] 고정 해제 또는 트리뷰 닫기 후 정상 동작 복귀

## 변경 이력

- 2026-07-02: 작업 항목 생성, 고정 버튼과 구조 변경 잠금 구현
- 2026-07-02: 추가 요청 반영 — 트리뷰를 열 때 고정 토글을 기본 선택 상태로 처리

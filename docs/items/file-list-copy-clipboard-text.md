# 파일 목록에서 복사 시 텍스트 에디터 붙여넣기 지원

- 상태: Ready for Verification

## 요구사항

폴더 패널 오른쪽 파일 목록(`FileList`)에서 파일/폴더를 선택해 Ctrl+C(복사) 또는 Ctrl+X(잘라내기)를 하면,
다른 시스템(탐색기 등 파일 시스템)에 붙여넣을 때는 실제 파일이 복사되고, 텍스트 에디터에 붙여넣을 때는
텍스트(파일/폴더 이름, 여러 개면 줄바꿈 구분)가 붙여넣어져야 한다.

## 원인 분석 또는 설계

- 기존 `CopySelectedToClipboard()` / `CutSelectedToClipboard()`(`FolderBrowser.xaml.cs`)는 `Clipboard.SetFileDropList()`만
  호출해 FileDrop 포맷만 클립보드에 담았다. 이 경우 탐색기 등 파일 붙여넣기는 되지만, 텍스트 포맷이 전혀 없어
  텍스트 에디터에 붙여넣으면 아무 것도 붙여지지 않았다.
- 왼쪽 폴더트리 복사(`CopyFolderTreeSelectionToClipboard`, [[folder-tree-copy-clipboard]])에서 이미 사용 중인 패턴대로,
  하나의 `DataObject`에 `SetFileDropList`(실제 경로)와 `SetText`(파일/폴더 이름)를 함께 설정한 뒤
  `Clipboard.SetDataObject(dataObject, true)`로 교체하면 두 붙여넣기 방식을 모두 지원할 수 있다.
- 텍스트 내용은 사용자 확인 결과 "파일/폴더 이름만"(전체 경로 아님)로 결정, 여러 파일 선택 시 `Environment.NewLine`으로 구분.
- 잘라내기(Ctrl+X)도 동일한 문제가 있어 같은 방식으로 함께 수정(사용자 확인 완료). 잘라내기 상태 관리(`SetCutStateFromClipboard`)는
  `DataObject` 교체와 무관하게 기존 로직 그대로 유지.

## 구현 내용

- `BuildNameListText(IEnumerable<string> paths)` 헬퍼 추가 — 각 경로의 `Path.GetFileName`을 줄바꿈으로 join.
- `CopySelectedToClipboard()` — `Clipboard.SetFileDropList` 단독 호출을 `DataObject`(FileDropList + 이름 텍스트) +
  `Clipboard.SetDataObject(dataObject, true)`로 교체.
- `CutSelectedToClipboard()` — 동일하게 `DataObject`(FileDropList + 이름 텍스트)로 교체.

## 변경 파일

- `Folderss/Controls/FolderBrowser.xaml.cs`

## 검증

- [x] 빌드 성공 확인 (`dotnet build .\Folderss.sln -c Debug`, 경고만 존재(기존 CA1416), 오류 0)
- [ ] 파일 목록에서 파일 1개 선택 후 Ctrl+C → 탐색기에 붙여넣기 시 실제 파일 복사 확인
- [ ] 파일 목록에서 파일 여러 개 선택 후 Ctrl+C → 텍스트 에디터에 붙여넣기 시 파일명 목록(줄바꿈 구분) 확인
- [ ] 파일 목록에서 항목 선택 후 Ctrl+X → 탐색기에 붙여넣기 시 실제 파일 이동 확인 및 잘라내기 표시(반투명 등) 기존 동작 유지 확인
- [ ] 파일 목록에서 항목 선택 후 Ctrl+X → 텍스트 에디터에 붙여넣기 시 파일명 텍스트 확인

## 변경 이력

- 2026-07-24: 초기 구현 (파일 목록 복사/잘라내기에 이름 텍스트 포맷 추가)

# 폴더 패널 텍스트 경로에 붙여넣기 기능 추가

- 상태: Ready for Verification

## 요구사항

폴더 패널의 경로 텍스트박스(PathBox)에 Ctrl+V로 경로를 붙여넣을 수 있어야 한다.
- 클립보드에 텍스트가 있으면 텍스트를 붙여넣기
- 클립보드에 파일 드롭 리스트가 있으면 첫 번째 항목의 경로를 붙여넣기 (파일이면 상위 디렉터리 경로)

## 원인 분석

FolderBrowser UserControl에 등록된 `ApplicationCommands.Paste` CommandBinding이 PathBox에 포커스가 있을 때도 붙여넣기 명령을 가로채서 파일 붙여넣기(파일 복사/이동)로 처리하려 함. 클립보드에 FileDropList가 없으면 CanExecute가 false가 되어 붙여넣기가 비활성화됨. PathBox 자체에는 별도의 붙여넣기 처리 로직이 없었음.

## 구현 내용

PathBox에 `PreviewKeyDown` 이벤트 핸들러를 추가하여 Ctrl+V를 UserControl의 CommandBinding보다 먼저 가로챔:
1. 클립보드에 FileDropList가 있으면 → 첫 번째 항목의 경로를 PathBox에 텍스트로 삽입 (파일 경로면 상위 폴더 경로로 변환)
2. 클립보드에 텍스트가 있으면 → 선택 영역을 대체하며 텍스트 삽입
3. `e.Handled = true`로 이벤트를 소비하여 UserControl의 파일 붙여넣기 로직이 실행되지 않도록 함

## 변경 파일

- `Folderss/Controls/FolderBrowser.xaml` — PathBox에 `PreviewKeyDown` 이벤트 핸들러 추가
- `Folderss/Controls/FolderBrowser.xaml.cs` — `PathBox_PreviewKeyDown` 메서드 추가

## 검증

- [ ] 텍스트 경로를 복사 후 PathBox에 Ctrl+V → 경로 텍스트 붙여넣기 확인
- [ ] 파일 탐색기에서 파일 복사 후 PathBox에 Ctrl+V → 파일의 상위 폴더 경로 붙여넣기 확인
- [ ] 파일 탐색기에서 폴더 복사 후 PathBox에 Ctrl+V → 폴더 경로 붙여넣기 확인
- [ ] 붙여넣기 후 Enter → 해당 경로로 이동 확인
- [ ] PathBox 외부(FileList)에서 Ctrl+V → 기존 파일 붙여넣기 동작 유지 확인

## 변경 이력

- 2026-06-30: 초기 구현

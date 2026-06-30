# 뷰어 패널 리프레시 버튼 추가

- 상태: Ready for Verification

## 요구사항

파일 뷰어 패널에서 파일 내용을 다시 읽는 리프레시 버튼이 필요하다.

## 설계

ViewerHost에 상단 툴바를 추가하고, 현재 파일 경로 표시와 리프레시(↻) 버튼을 배치.
리프레시 클릭 시 현재 뷰어의 `Load()` 메서드를 다시 호출하여 파일 내용을 갱신.

## 구현 내용

1. `ViewerHost.xaml` — DockPanel 레이아웃으로 변경, 상단에 툴바 Border 추가
   - 파일 경로 TextBlock (SecondaryText, 11pt, 말줄임)
   - 리프레시 버튼 (↻ 아이콘, 28x24)
2. `ViewerHost.xaml.cs`:
   - `_currentFilePath` 필드 추가
   - `OpenFile()` 에서 파일 경로 저장 및 표시
   - `Refresh_Click` — 파일 존재 확인 후 `_currentViewer.Load()` 재호출

## 변경 파일

- `Folderss/Controls/ViewerHost.xaml`
- `Folderss/Controls/ViewerHost.xaml.cs`

## 검증

- [ ] 뷰어 탭 상단에 파일 경로와 리프레시 버튼 표시
- [ ] 리프레시 버튼 클릭 시 파일 내용 갱신
- [ ] 외부에서 파일 수정 후 리프레시 → 변경된 내용 반영
- [ ] 파일 삭제 후 리프레시 → 경고 메시지 표시

## 변경 이력

- 2026-06-30: 초기 구현

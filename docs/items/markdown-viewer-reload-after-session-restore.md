# 세션 복원으로 열린 마크다운 탭에서 외부 변경 확인창이 뜨지 않는 문제 수정

- 상태: Verified (2026-07-14 사용자 확인 완료)

## 요구사항

마크다운 파일이 열린 상태로 프로그램을 종료한 뒤 다시 실행하면(세션 복원으로 탭이 다시 열림),
이후 외부에서 파일이 변경되어도 "파일이 외부에서 변경되었습니다" 확인창이 뜨지 않는다. 이를 수정한다.

## 원인 분석 또는 설계

- `dock-layout.xml`에는 탭의 `IsSelected`만 저장되고 `IsActive`(포커스 상태)는 저장되지 않는다.
- 세션 복원 시 흐름:
  1. 레이아웃 역직렬화 콜백이 `CreateViewerHost(filePath, isActive: false)`로 뷰어 생성
  2. `AttachRestoredViewerDocuments()`가 `viewerHost.SetActive(document.IsActive)` 호출 — 복원 직후 `IsActive`는 항상 false
  3. `MarkdownViewer._isActive`가 false로 남아, 외부 변경은 `_pendingExternalReload`로 보류만 되고 확인창은 안 뜸
  4. WebView2 내부 클릭은 별도 HWND로 가서 AvalonDock 포커스 추적(IsActive)에 잡히지 않으므로,
     사용자가 탭 헤더를 직접 클릭하지 않는 한 `IsActive`가 true가 되지 않아 확인창이 영영 안 뜸
- 확인창을 보류하는 목적은 "화면에 안 보이는 백그라운드 탭"에서 안 띄우려는 것이므로,
  판정 기준을 포커스(`IsActive`)가 아니라 **선택되어 보이는 탭(`IsSelected`) 또는 포커스**로 바꾼다.

## 구현 내용

- `MainWindow.xaml.cs`
  - `UpdateViewerActivation(document, viewerHost)` 헬퍼 추가 — `document.IsActive || document.IsSelected`를 `SetActive`에 전달
  - `AttachViewerDocument()` — `IsActiveChanged`에 더해 `IsSelectedChanged`도 구독, 둘 다 헬퍼 호출
  - `AttachRestoredViewerDocuments()` — 복원 후 `UpdateViewerActivation`으로 초기 상태 반영 (선택된 탭이면 즉시 활성)
  - 일반 열기 경로(`OpenViewerTab`)도 동일 헬퍼로 통일

## 변경 파일

- `Folderss/MainWindow.xaml.cs`

## 검증

- [x] 마크다운 파일 탭이 열린 상태로 종료 → 재실행 → 외부에서 파일 수정 → 확인창이 정상적으로 뜸
- [ ] 재실행 후 뷰어 탭이 선택되지 않은 상태(다른 탭이 선택됨)면 확인창이 뜨지 않고, 해당 탭을 선택하는 순간 확인창이 뜸
- [ ] 기존 동작 유지: 앱 실행 중 파일을 새로 열어 보는 탭에서 외부 변경 시 확인창 정상 표시
- [x] 빌드 성공 확인 (`dotnet build` Debug/Release 모두 오류 0개)

## 변경 이력

- 2026-07-14: 초기 구현 (IsSelected 기반 활성 판정 추가)

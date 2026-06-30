# 뷰어 파일 변경 감지 및 리로드 확인 메시지

- 상태: Ready for Verification

## 요구사항

마크다운 뷰어와 모나코(코드) 뷰어에서 파일이 외부에서 변경되면 내용을 다시 읽을지 확인 메시지를 표시해야 한다.

## 설계

- 두 뷰어 모두 FileSystemWatcher로 열린 파일의 변경을 감시
- 변경 감지 시 DispatcherTimer로 디바운스 후 확인 다이얼로그 표시
- 편집 중(modified)인 경우 편집 내용이 사라진다는 경고 포함
- 사용자가 "예"를 선택하면 리로드, "아니오"면 무시

## 구현 내용

### MarkdownViewer
- 기존 FileWatcher 유지, `ReloadExternalFileAsync`에서 자동 리로드 대신 확인 다이얼로그 추가
- `_modified` 상태에서도 리로드 확인 가능 (기존에는 수정 중이면 리로드 건너뜀)

### MonacoViewer
- FileSystemWatcher, DispatcherTimer 추가
- `IDisposable` 구현하여 watcher 정리
- `Load()` 에서 watcher 시작, `_lastLoadedContent` 추적
- `Save()` 에서 `_lastLoadedContent` 갱신 (자기 저장은 변경으로 감지 안 됨)
- `PromptAndReloadAsync()` — 내용 비교 후 확인 다이얼로그 표시, 승인 시 `CallAppOpen` 재호출

## 변경 파일

- `Folderss/Viewers/MarkdownViewer.xaml.cs`
- `Folderss/Viewers/MonacoViewer.xaml.cs`

## 검증

- [ ] 마크다운 뷰어: 외부에서 파일 수정 → 확인 메시지 표시 → "예" 시 리로드
- [ ] 마크다운 뷰어: 편집 중 외부 변경 → 편집 내용 손실 경고 포함 메시지 → "아니오" 시 유지
- [ ] 모나코 뷰어: 외부에서 파일 수정 → 확인 메시지 표시 → "예" 시 리로드
- [ ] 모나코 뷰어: 편집 중 외부 변경 → 경고 메시지 → "아니오" 시 편집 내용 유지
- [ ] 뷰어 내에서 저장 후 → 자기 저장은 변경 감지 안 됨
- [ ] 뷰어 탭 닫기 시 watcher 정리 확인

## 변경 이력

- 2026-06-30: 초기 구현

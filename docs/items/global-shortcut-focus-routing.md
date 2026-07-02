# 전역 단축키 포커스 라우팅 버그 수정

- 상태: Ready for Verification

## 요구사항

마크다운 패널에서 Ctrl+C 후 Ctrl+V를 누르면 "고정된 폴더입니다" 메시지가 뜬다.
뷰어(마크다운 등)에서 편집 중일 때는 해당 뷰어 중심으로 키 입력이 처리되어야 하며,
폴더 패널용 파일 단축키가 실행되면 안 된다.

## 원인 분석 또는 설계

### 재현 메커니즘

1. 마크다운 뷰어는 WebView2 기반이며, WebView2는 액셀러레이터 키(Ctrl+C/V/X, Delete, F2, F5 등)를 WPF 쪽으로 전달한다 → `MainWindow.Window_PreviewKeyDown`이 발화.
2. 텍스트 입력 가드가 `Keyboard.FocusedElement is TextBox`뿐이라 WebView2는 통과.
3. Ctrl+C → `CopyClipboard` 분기 → `ActivePane`(마지막 활성 폴더 패널)의 선택 파일을 클립보드에 복사. 사용자가 복사하려던 텍스트가 아닌 파일 목록이 클립보드에 들어가고, `e.Handled = true`로 뷰어의 텍스트 복사도 차단될 수 있음.
4. Ctrl+V → `PasteFromClipboard()` → 클립보드의 파일 목록을 `ActivePane.CurrentPath`에 붙여넣기 시도 → 폴더 고정 상태라 안내 메시지 표시. 고정이 아니면 조용히 파일이 복제된다(잠복 버그가 고정 기능으로 드러난 것).

### 문제점 정리

1. **블랙리스트 가드의 허점** — `is TextBox` 체크는 WPF `TextBox`만 인식. WebView2(Markdown/Monaco/Text 뷰어), `RichTextBox` 등 다른 텍스트 입력 대상은 통과. `Rename`(F2), `NewFolder`/`NewFile`, `Move`, `Refresh` 분기에는 이 가드조차 없음.
2. **ActivePane과 키보드 포커스의 분리** — ActivePane은 마지막 활성 폴더 패널일 뿐, 포커스가 뷰어·즐겨찾기 등 다른 곳에 있어도 파일 단축키가 ActivePane으로 라우팅됨.
3. **위험 단축키 노출** — 같은 구조로 마크다운 편집 중 Delete가 파일 삭제 확인창을, F2가 이름변경 다이얼로그를 띄울 수 있음.
4. **무조건적인 `e.Handled = true`** — 분기에 들어가면 실제 동작 여부와 무관하게 키를 삼켜 뷰어의 기본 처리를 방해.
5. **뷰어 활성 판정의 취약성** — `TryHandleActiveViewerShortcut`은 `LayoutDocument.IsActive`에 의존. WebView2 네이티브 HWND에 포커스가 있을 때 AvalonDock 활성 상태와 어긋날 수 있음.

### 수정 계획

**1단계 — 파일 단축키를 화이트리스트 방식으로 전환 (핵심)**

- 판정 헬퍼 추가:
  ```csharp
  private bool ShouldHandlePaneShortcut()
  {
      // 텍스트 입력 중이면 제외 (TextBox → TextBoxBase로 확장: PathBox/SearchBox 포함)
      if (Keyboard.FocusedElement is TextBoxBase || Keyboard.FocusedElement is PasswordBox)
          return false;
      // 파일 단축키는 키보드 포커스가 폴더 패널 내부일 때만
      return GetFolderBrowsers().Any(pane => pane.IsKeyboardFocusWithin);
  }
  ```
- 단축키 분류:
  - **폴더 패널 전용** (위 판정 통과 시에만): Rename, Delete, Move, CopyClipboard, CutClipboard, PasteClipboard, NewFolder, NewFile, Refresh, RefreshAlt, NavigateBack/Forward/Up
  - **즐겨찾기 분기**: Rename/CopyClipboard의 FavoritesPanel 분기는 기존대로 `FavoritesPanel.IsKeyboardFocusWithin`으로만 진입
  - **전역 유지**: AddPanel, SwitchPaneLeft/Right, ShowSearch, PanelMaximize (ShowSearch는 뷰어가 `TryHandleActiveViewerShortcut`에서 선점하는 기존 구조 유지)

**2단계 — 뷰어/콘솔 포커스 조기 차단 보강**

- `IsViewerFocused()` 추가: 열린 문서들의 `ViewerHost.IsKeyboardFocusWithin` 검사 (WebView2 WPF 컨트롤은 IKeyboardInputSink로 참여하므로 포커스 시 true가 기대됨). 뷰어 포커스면 `TryHandleActiveViewerShortcut` 이후 파일 단축키 블록 전체를 건너뜀 — 화이트리스트와 이중 안전망.
- `TryHandleActiveViewerShortcut`의 문서 활성 판정에 `viewerHost.IsKeyboardFocusWithin` 폴백 추가.

**3단계 — `e.Handled` 정리**

- 각 분기에서 실제로 동작을 수행했을 때만 `e.Handled = true` 설정 (예: CopyClipboard에서 선택 항목이 없으면 키를 삼키지 않음).

**4단계 — 검증 (Windows 필요)**

- 마크다운/Monaco/Text 뷰어 편집 중 Ctrl+C/V/X가 텍스트 편집으로만 동작하고 파일 작업이 실행되지 않음
- 뷰어 편집 중 Delete/F2/F5/Ctrl+N 계열이 파일 작업을 트리거하지 않음
- 폴더 패널 포커스 시 모든 기존 단축키 정상 동작
- PathBox/SearchBox 입력 중 Delete/F2 등이 파일 작업을 트리거하지 않음 (TextBoxBase 가드)
- 즐겨찾기 패널 F2/Ctrl+C 기존 동작 유지
- 콘솔 포커스 시 기존 차단 동작 유지

### 리스크 / 확인 필요 사항

- WebView2 네이티브 HWND에 포커스가 있을 때 폴더 패널의 `IsKeyboardFocusWithin`이 확실히 false인지 런타임 확인 필요. WPF 포커스가 마지막 요소에 남는 경우가 있으면 2단계의 `IsViewerFocused()` 조기 차단이 안전망 역할을 한다 (이중 가드 설계 이유).
- 단축키를 폴더 패널 포커스 필수로 바꾸면, 기존에 "창 어디서나 F5로 새로고침" 같은 사용 습관이 있었다면 동작이 달라짐 — 파일 목록 갱신은 FileSystemWatcher가 이미 자동 처리하므로 영향 적다고 판단.

## 구현 내용

- `Window_PreviewKeyDown`을 3단 구조로 재구성
  1. 전역 단축키(AddPanel, SwitchPaneLeft/Right, ShowSearch, PanelMaximize)는 포커스 위치와 무관하게 처리
  2. 즐겨찾기 패널 포커스 시 즐겨찾기 전용 단축키(Rename, CopyClipboard)만 처리하고 파일 작업 블록으로 넘기지 않음 — 즐겨찾기 포커스 중 Delete가 ActivePane 파일을 삭제하던 잠복 버그도 함께 제거
  3. 파일 관리 단축키(Rename, Delete, Move, 복사/잘라내기/붙여넣기, 새 폴더/파일, 새로고침, 뒤로/앞으로/상위)는 `IsViewerFocused()`가 아니고 `ShouldHandlePaneShortcut()`을 통과할 때만 처리
- `ShouldHandlePaneShortcut()` 추가 — 화이트리스트 판정: 포커스 요소가 `TextBoxBase`/`PasswordBox`가 아니고, 키보드 포커스가 폴더 패널(`FolderBrowser`) 내부일 때만 true. 기존 `is TextBox` 블랙리스트 가드 대체 (F2가 PathBox 입력 중 이름변경 다이얼로그를 띄우던 문제도 함께 해결)
- `IsViewerFocused()` 추가 — 열린 뷰어 문서(`ViewerHost.IsKeyboardFocusWithin`) 포커스 시 파일 단축키 블록 전체 차단 (WebView2 이중 안전망)
- `TryHandleActiveViewerShortcut`에 키보드 포커스 기준 뷰어 탐색 폴백 추가 — `LayoutDocument.IsActive`가 네이티브 HWND 포커스와 어긋나는 경우 대비
- `e.Handled`는 실제 동작 수행 시에만 설정 (복사/잘라내기는 선택 항목이 있을 때, 붙여넣기는 파일 붙여넣기가 실제 수행됐을 때)
- 미사용이 된 `PasteFromClipboard()` 제거

## 변경 파일

- `Folderss/MainWindow.xaml.cs` — `Window_PreviewKeyDown` 재구성, `ShouldHandlePaneShortcut`/`IsViewerFocused` 추가, `TryHandleActiveViewerShortcut` 폴백, `PasteFromClipboard` 제거
- `docs/items/global-shortcut-focus-routing.md`

## 검증

- [ ] `dotnet build .\Folderss.sln -c Debug` 성공 (`Exit: 0`) — 작업 환경(Linux, 네트워크 정책상 SDK 설치 불가)에서 빌드 불가, Windows 개발 환경에서 확인 필요
- [ ] 마크다운/Monaco/Text 뷰어 편집 중 Ctrl+C/V/X가 텍스트 편집으로만 동작하고 파일 복사·붙여넣기가 실행되지 않음 (고정 폴더 메시지 미표시)
- [ ] 뷰어 편집 중 Delete/F2/F5가 파일 삭제·이름변경·새로고침을 트리거하지 않음
- [ ] 폴더 패널(파일 목록) 포커스 시 모든 파일 단축키 정상 동작
- [ ] PathBox/SearchBox 입력 중 Delete/F2/Ctrl+C가 파일 작업을 트리거하지 않고 텍스트 편집으로 동작
- [ ] 즐겨찾기 패널 포커스 시 F2(이름변경)·Ctrl+C(경로 복사) 정상, Delete가 파일을 삭제하지 않음
- [ ] 콘솔 포커스 시 기존 차단 동작 유지
- [ ] AddPanel·패널 전환·Ctrl+F 검색·F11 최대화는 포커스 위치와 무관하게 동작

## 변경 이력

- 2026-07-02: 마크다운 패널 Ctrl+C/V 오동작 신고 접수, 원인 분석 및 수정 계획 수립
- 2026-07-02: 계획 승인, 화이트리스트 방식 포커스 라우팅 구현

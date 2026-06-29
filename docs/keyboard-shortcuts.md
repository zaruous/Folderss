# Folderss 키보드 단축키 매핑표

## 범례

| 구분 | 의미 |
|------|------|
| **커스텀** | 사용자가 설정 창에서 변경 가능 (`KeyBindingService` 등록) |
| **하드코딩** | 코드에 고정되어 변경 불가 |
| **OS/WPF** | WPF `ApplicationCommands` 또는 OS 기본 동작 |

---

## 1. 사용자 정의 가능 단축키 (KeyBindingService)

설정 경로: `%LOCALAPPDATA%\Folderss\keybindings.xml`

| CommandId | 표시 이름 | 기본 키 | 처리 위치 | 동작 |
|-----------|-----------|---------|-----------|------|
| `Rename` | 이름 변경 | `F2` | MainWindow | 선택된 항목 이름 변경 (즐겨찾기 패널 포커스 시 즐겨찾기 이름 변경) |
| `Refresh` | 새로 고침 | `F5` | MainWindow | 양쪽 패널 새로 고침 |
| `Move` | 이동 | `F6` | MainWindow | 선택 항목을 반대 패널로 이동 |
| `Delete` | 삭제 | `Delete` | MainWindow | 휴지통 이동 (Shift 누르면 영구 삭제) |
| `RefreshAlt` | 새로 고침 (대체) | `Ctrl+R` | MainWindow | 양쪽 패널 새로 고침 (대체키) |
| `NewFolder` | 새 폴더 | `Ctrl+Shift+N` | MainWindow | 활성 패널에 새 폴더 생성 |
| `NewFile` | 새 파일 | `Ctrl+N` | MainWindow | 활성 패널에 새 파일 생성 |
| `AddPanel` | 폴더 패널 추가 | `Ctrl+T` | MainWindow | 새 폴더 브라우저 탭 추가 |
| `CopyClipboard` | 복사 (클립보드) | `Ctrl+C` | MainWindow | 선택 항목 클립보드 복사; 즐겨찾기 패널에서는 경로 복사 |
| `CutClipboard` | 잘라내기 | `Ctrl+X` | MainWindow | 선택 항목 잘라내기 |
| `PasteClipboard` | 붙여넣기 | `Ctrl+V` | MainWindow | 클립보드 항목 붙여넣기 |
| `NavigateBack` | 뒤로 | `Alt+←` | MainWindow | 활성 패널 히스토리 뒤로 |
| `NavigateForward` | 앞으로 | `Alt+→` | MainWindow | 활성 패널 히스토리 앞으로 |
| `NavigateUp` | 상위 폴더 | `Alt+↑` | MainWindow | 활성 패널 상위 폴더 이동 |
| `SwitchPaneLeft` | 왼쪽 패널 전환 | `Ctrl+Shift+←` | MainWindow | 이전 폴더 패널로 포커스 전환 |
| `SwitchPaneRight` | 오른쪽 패널 전환 | `Ctrl+Shift+→` | MainWindow | 다음 폴더 패널로 포커스 전환 |
| `ShowSearch` | 내용 검색 | `Ctrl+F` | MainWindow | 파일 내용 검색 창 열기 |
| `PanelMaximize` | 패널 최대화 토글 | `F11` | MainWindow | 활성 패널/문서 최대화 토글 |

---

## 2. 하드코딩 단축키

### FolderBrowser (파일 목록)

| 키 | 동작 | 비고 |
|----|------|------|
| `Enter` | 선택된 항목 열기 (더블클릭과 동일) | 하드코딩 |
| `Backspace` | 상위 폴더로 이동 | 하드코딩 |

### FavoritesPanel (즐겨찾기 패널)

MainWindow에서 `Rename`, `CopyClipboard` KeyBindingService 매핑으로 통합 처리.

### SearchPanel (검색 패널)

| 키 | 동작 | 비고 |
|----|------|------|
| `Escape` | 검색 패널 닫기 | 하드코딩 |
| `Enter` | 검색 실행 | 하드코딩 (검색 입력란에서) |

### ConsolePanel (콘솔)

| 키 | 동작 | 비고 |
|----|------|------|
| `Escape` | 명령 입력란으로 포커스 | 하드코딩 |
| `Tab` | 터미널에 Tab 키 전달 | 하드코딩 |
| `Home`/`End` | 터미널에 네비게이션 키 전달 | 하드코딩 |

### FolderBrowser SearchBox (파일 필터링)

| 키 | 동작 | 비고 |
|----|------|------|
| `↓` | 검색 결과 파일 목록으로 포커스 이동 | 하드코딩 |

---

## 3. OS/WPF 기본 단축키

FolderBrowser에서 `ApplicationCommands` CommandBinding으로 등록:

| 키 | 동작 | 비고 |
|----|------|------|
| `Ctrl+C` | 파일/폴더 클립보드 복사 | WPF CommandBinding |
| `Ctrl+X` | 파일/폴더 잘라내기 | WPF CommandBinding |
| `Ctrl+V` | 클립보드 붙여넣기 | WPF CommandBinding |

---

## 4. 뷰어 단축키

### MarkdownViewer

| 키 | 동작 | 비고 |
|----|------|------|
| `Ctrl+F` | 마크다운 내 텍스트 검색 (find) | KeyBindingService `ShowSearch` 매핑 사용 |

### MonacoViewer / TextViewer

자체 키보드 핸들러 없음. Monaco 에디터 내장 단축키(Ctrl+F 검색 등)는 WebView2 내에서 자체 처리.

---

## 5. 버그 및 불일치 사항 (리뷰 결과)

| # | 제목 | 심각도 | 상태 |
|---|------|--------|------|
| BUG-1 | `Ctrl+X`/`Ctrl+V` 커스텀 매핑 미적용 | 중 | **수정 완료** |
| BUG-2 | `Ctrl+N` (새 파일) KeyBindingService 미등록 | 낮 | **수정 완료** |
| BUG-3 | MarkdownViewer `Ctrl+F` 하드코딩 | 낮 | **수정 완료** |
| BUG-4 | FavoritesPanel `F2`/`Ctrl+C` 하드코딩 | 중 | **수정 완료** |
| BUG-5 | `docs/architecture.md` 문서 불일치 | - | **수정 완료** |

### BUG-1: `Ctrl+X` / `Ctrl+V` 커스텀 매핑 미적용 — 수정 완료

- **증상**: `KeyBindingService`에 `CutClipboard`, `PasteClipboard`가 등록되어 있으나 `MainWindow.Window_PreviewKeyDown`에서 미사용
- **수정**: `FolderBrowser`에 `CopySelectedToClipboard()`, `CutSelectedToClipboard()` 공개 메서드 추가. `MainWindow`에서 `kb.Matches(e, "CutClipboard")`, `kb.Matches(e, "PasteClipboard")` 핸들러 추가. `CopyClipboard`도 FavoritesPanel 외 일반 파일 복사까지 처리하도록 확장

### BUG-2: `Ctrl+N` (새 파일) KeyBindingService 미등록 — 수정 완료

- **증상**: `MainWindow.xaml.cs`에서 `e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control`로 하드코딩
- **수정**: `KeyBindingService.GetDefaults()`에 `NewFile` (`Ctrl+N`) 항목 추가. `MainWindow`에서 `kb.Matches(e, "NewFile")`로 교체

### BUG-3: MarkdownViewer `Ctrl+F` 하드코딩 — 수정 완료

- **증상**: `MarkdownViewer.HandleShortcut()`에서 `e.Key != Key.F` 직접 비교
- **수정**: `IViewerShortcutHandler.HandleShortcut` 시그니처에 `KeyBindingService` 파라미터 추가. `MarkdownViewer`에서 `kb.Matches(e, "ShowSearch")`로 교체. `ViewerHost` → `MainWindow` 호출 체인 업데이트

### BUG-4: FavoritesPanel `F2` / `Ctrl+C` 하드코딩 — 수정 완료

- **증상**: `FavoritesPanel.FavoritesTree_PreviewKeyDown()`에서 키 직접 비교
- **수정**: `FavoritesPanel`의 `PreviewKeyDown` 핸들러 제거. `MainWindow`의 `Rename` 핸들러가 `FavoritesPanel.IsKeyboardFocusWithin` 시 `FavoritesPanel.RenameSelected()` 호출하도록 변경. `CopyClipboard` 핸들러에서 즐겨찾기/파일 목록 모두 처리

### BUG-5: `docs/architecture.md` 문서 불일치 — 수정 완료

- **증상**: 클래스명 `KeybindingManager` (실제: `KeyBindingService`), 저장 형식 `JSON` (실제: `XML`)
- **수정**: 올바른 이름과 형식으로 문서 갱신

---

## 6. 미구현 제안 단축키

파일 관리자에서 일반적으로 사용되는 단축키 중 현재 미구현 항목:

| 제안 키 | 동작 | 우선순위 | 비고 |
|---------|------|---------|------|
| `Ctrl+A` | 전체 선택 | 높음 | 대부분의 파일 관리자 기본 기능 |
| `Ctrl+W` | 현재 탭(패널) 닫기 | 높음 | `Ctrl+T`(추가)와 쌍을 이루는 기능 |
| `Ctrl+L` / `F4` | 경로 입력란 포커스 | 중간 | Windows 탐색기, 브라우저 공통 |
| `Ctrl+D` | 즐겨찾기에 현재 폴더 추가 | 중간 | 브라우저 북마크 패턴 |
| `Ctrl+Shift+C` | 파일 경로 복사 | 중간 | VS Code 등에서 자주 사용 |
| `Ctrl+Z` | 실행 취소 (마지막 파일 작업) | 낮음 | 구현 복잡도 높음 |
| `Ctrl+H` | 숨김 파일 표시 토글 | 낮음 | 편의 기능 |
| `Space` | 빠른 미리보기 (Quick Look) | 낮음 | macOS Finder 스타일 |

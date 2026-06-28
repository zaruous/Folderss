# DONE

기존 릴리스 완료 이력입니다. 현재 아이템의 상태와 상세 내용은 `docs/items/`에서 관리합니다.

---

## v1.6.1 (2026-06-27)

### 사용자 확인 완료

- Markdown 컨텍스트 메뉴 본문 영역 처리 — 인쇄·다른 이름으로 저장·공유가 현재 보기 모드의 전체 본문을 사용하도록 수정하고 사용자 확인 완료.

### Markdown 뷰어 파일 변경 자동 반영

- `MarkdownViewer.xaml.cs` — `FileSystemWatcher`와 300ms 디바운스를 추가해 열린 Markdown 파일이 외부에서 변경되면 디스크 내용을 다시 읽고 WebView 뷰어에 반영.
- `MarkdownViewer.xaml.cs`, `ViewerHost.xaml.cs`, `MainWindow.xaml.cs` — 비활성 Markdown 탭은 변경 감지만 기록하고, 탭이 활성화될 때 한 번만 재로드/재렌더링하도록 최적화.
- `markdown-app.html` — 현재 보기 모드를 유지하면서 내용만 교체하는 `app.reloadContent()` API 추가.
- `MainWindow.xaml.cs` — 저장된 AvalonDock 레이아웃의 `viewer|...` ContentId를 복원해 프로그램 재실행 후 Markdown 뷰어 탭이 다시 열리도록 처리.
- `MainWindow.xaml.cs` — 이미 열린 폴더 패널이나 파일 뷰어를 다시 열 때 중복 탭을 만들지 않고 기존 탭으로 포커스 이동.
- `MainWindow.xaml.cs` — Markdown 뷰어 탭을 F11로 패널 최대화 후 복원할 때 폴더 패널이 잘못 주입되어 흰 화면이 되는 문제 수정.
- `MainWindow.xaml.cs` — 포커스가 문서 탭 밖으로 이동한 상태에서 F11 패널 최대화/복원을 반복해도 실제 최대화된 콘텐츠를 보존하고 활성 문서 fallback으로 레이아웃을 안정적으로 전환하도록 수정.
- `MainWindow.xaml.cs` — F11 패널 최대화/복원을 레이아웃 직렬화·역직렬화 방식에서 AvalonDock `LayoutDocument.IsMaximized` 토글 방식으로 전환해 폴더/Markdown 콘텐츠 재부모화 문제를 제거.
- `ViewerHost.xaml.cs` — 뷰어 교체/닫기 시 `IDisposable` 뷰어를 정리해 파일 감시자가 남지 않도록 처리.
- `MainWindow.xaml.cs` — Markdown 뷰어 탭 닫기와 실제 앱 종료 시 뷰어 리소스를 정리하도록 처리.
- `markdown-app.html` — 목차의 하단 항목 클릭 시 미리보기 스크롤 컨테이너 기준으로 이동하고, 마지막 헤딩도 상단 근처까지 스크롤될 수 있도록 하단 여유 공간과 TOC 활성 표시 기준을 보정.
- README와 아키텍처 문서에 Markdown 뷰어 파일 변경 자동 반영 기능을 반영.

---

## v1.6.0 (2026-06-24)

### 문서 정리

- 완료된 마크다운 뷰어 구현계획 상세 문서를 `docs/done/마크다운뷰어_구현계획_완료.md`에서 관리.
- `docs/todo/TODO.md`에서 완료된 항목을 제거하고 현재 미완료 항목 없음 상태로 정리.

### Open With 컨텍스트 메뉴

- `Models/OpenWithEntry.cs` — 신규 모델. Id(GUID), Name, Description, ExecutablePath, Arguments(`{0}` = 경로), ExtensionMask(`*`/`folder`/`.txt,.cs`) 필드.
- `Services/OpenWithService.cs` — 정적 서비스. XML 저장(`%LOCALAPPDATA%\Folderss\open-with.xml`). `GetMatchingEntries(paths)`: 경로 목록의 확장자와 마스크 매칭. `Launch(entry, paths)`: `{0}`을 공백 구분 따옴표 경로로 치환 후 `Process.Start`. `Save(entries)`: 설정 창에서 일괄 저장.
- `Services/ShellContextMenuService.cs` — `Show()` 시그니처에 `IList<CustomMenuItem> customItems = null` 추가. `QueryContextMenu` 후 구분선 + 커스텀 항목(`MF_STRING`, ID 0x8000+) 삽입. `TrackPopupMenuEx` 반환값이 커스텀 범위이면 `Invoke()` 호출, 셸 범위이면 기존 `InvokeCommand` 호출.
- `Controls/FolderBrowser.xaml.cs` — 우클릭 시 `OpenWithService.GetMatchingEntries()`로 매칭 항목 조회 후 `CustomMenuItem` 리스트 생성, `ShellContextMenuService.Show()`에 전달.
- `SettingsWindow.xaml` — 좌측 네비에 "열기 프로그램" 탭 추가. OpenWithPanel 그리드: 항목 ListView + 인라인 편집 폼(이름/설명/실행파일/인수/마스크) + 새 항목·저장·삭제 버튼.
- `SettingsWindow.xaml.cs` — `_workingOpenWith` ObservableCollection, 폼 CRUD 핸들러, 파일 찾기 다이얼로그(`Microsoft.Win32.OpenFileDialog`), 저장 시 `OpenWithService.Save()` 호출.

### 파일 컴포넌트

- `Controls/FolderBrowser.xaml.cs` — `FileSystemWatcher` 기반 변경 감지와 400ms 디바운스를 적용해 현재 폴더 항목 변경 시 목록을 갱신.
- `Controls/FolderBrowser.xaml.cs` — 파일 목록 빈 영역 우클릭 시 현재 폴더 기준 Windows 쉘 컨텍스트 메뉴가 열리도록 처리.

### 파일 내용 검색

- `MainWindow.xaml.cs` — `Ctrl+F` 파일 내용 검색 패널을 같은 단축키로 다시 숨길 수 있도록 토글 처리.
- `SearchPanel.xaml.cs` — `Esc` 입력 시 검색 패널 숨김 처리.

---

## v1.5.1 (2026-06-24)

### "+" 새 패널 탭 클릭 시 빈 화면 버그 수정

- `MainWindow.xaml.cs` — `TogglePanelMaximize()` F11 복원 경로에 `EnsureAddPanelTab()` 호출 추가. `XmlLayoutSerializer.Deserialize()` 이후 "+" 탭 이벤트 핸들러가 소실될 수 있는 상태를 보정.
- `MainWindow.xaml.cs` — `EnsureAddPanelTab()` 내부에서 "+" 탭이 이미 `IsActive=true`인 경우 인접 폴더 패널로 포커스를 전환. 다음 "+" 클릭 시 `IsActiveChanged`가 정상 발화하도록 초기화.
- 재현 경로: F11 최대화 후 복원 또는 "+" 탭이 활성화된 상태의 세션 복원 이후 "+" 클릭 시 빈 화면이 나올 수 있는 케이스.

### F11 폴더 패널 최대화 토글

- `KeyBindingService.cs` — `PanelMaximize` (F11) 기본 바인딩 추가.
- `MainWindow.xaml.cs` — `TogglePanelMaximize()` 메서드 추가.
  - 최대화 시: 현재 레이아웃 XML을 메모리에 저장 후 활성 패널만 남긴 최소 레이아웃으로 교체.
  - 복원 시: 저장된 XML을 `XmlLayoutSerializer`로 역직렬화, 최대화됐던 FolderBrowser 인스턴스를 그대로 재연결(`_activePane` 참조 보존).
  - `Window_PreviewKeyDown`에 `PanelMaximize` 분기 추가.
- `MainWindow.xaml` — 상태바 힌트 텍스트에 "F11 패널최대화" 추가.
- 사용: F11 → 현재 활성 폴더 패널이 DockManager 전체 영역을 점유. 다시 F11 → 원래 레이아웃 복원.

---

## v1.5.0 (2026-06-23)

### 마크다운 뷰어 Phase 01–04 — 전체 구현

- **Phase 01 — TextViewer + WebView2 공통 인프라**
  - `Microsoft.Web.WebView2` 1.0.2739.15 NuGet 추가.
  - `Viewers/Resources/` 에 `text-app.html`, `highlight.min.js`, `themes/hljs-*.css` 포함.
  - `TextViewer.xaml/.cs`: WebView2 초기화, 가상 호스트 매핑(`folderss-viewer`), 외부 URL 차단, `JsonString()` 이스케이프 유틸. BOM 인코딩 감지.
  - `ViewerConfigService.Resolve()`: `builtin:text` → `TextViewer` 인스턴스 반환.

- **Phase 02 — MarkdownViewer**
  - `Viewers/Resources/` 에 `markdown-app.html`, `marked.min.js`, `mermaid.min.js`, `katex.min.js/.css`, `katex-auto-render.min.js` 포함.
  - `markdown-app.html`: CSS 변수 기반 6+테마, Preview/Edit/Split 3모드 전환, 왼쪽 TOC(IntersectionObserver 현재 헤딩 강조), 드래그 핸들 리사이즈, YAML front matter 박스, 300ms 디바운스 실시간 미리보기.
  - `MarkdownViewer.xaml/.cs`: `WebMessageReceived` → `modified`/`save-request`/`export-html`/`export-pdf`/`open-link` 처리. `File.Replace` 원자 저장. `DetectEncoding()` BOM 감지.
  - `ViewerConfigService.Resolve()`: `builtin:markdown` → `MarkdownViewer` 인스턴스 반환.

- **Phase 02-E — Export**
  - `markdown-app.html`: `[Export ▾]` 드롭다운 → `exportHtml()` / `exportPdf()`.
  - `MarkdownViewer.xaml.cs`: `export-html` postMessage → `SaveFileDialog` → HTML 파일 저장. `export-pdf` → `PrintToPdfAsync`.

- **Phase 03 — Edit + Split 모드**
  - `markdown-app.html` 내부에 `app.setMode('edit'|'split'|'preview')` 구현.
  - Split 모드: 에디터 ↔ 프리뷰 CSS flex + 드래그 핸들. Edit 모드: TOC 숨김.
  - Ctrl+S → `postMessage({type:'save-request'})`, Tab 키 4-space 삽입.

- **Phase 04 — 설정 창 뷰어 탭**
  - `SettingsWindow.xaml`: **뷰어** 탭 추가 — 확장자↔뷰어 ListView, 추가/삭제 버튼.
  - `SettingsWindow.xaml.cs`: `ViewerMappingItem` 뷰모델, 저장 시 `ViewerConfigService` 반영.
  - `MainWindow.xaml.cs`: `SettingsWindow` 생성 시 `_viewerConfigService` 전달.

---

### 마크다운 뷰어 Phase 00 — 뷰어 프레임워크 스켈레톤

- `Viewers/IFileViewer.cs` 생성: `IFileViewer` 인터페이스, `ViewerCapabilities` Flags enum, `ExportFormat` enum.
- `Services/ViewerConfigService.cs` 생성: 확장자 ↔ 뷰어 키 매핑, JSON 저장·복원 (`viewer-config.json`).
  Phase 01/02 뷰어 구현 전까지 `Resolve()`는 null 반환.
- `Controls/ViewerHost.xaml/.cs` 생성: `IFileViewer.View`를 `ContentControl`에 호스팅하는 래퍼.
  `CanOpen()` / `OpenFile()` / `ApplyTheme()` 제공.
- `FolderBrowser.xaml.cs`: `FileOpenRequested` 이벤트 추가. 더블클릭 시 핸들러가 있으면 이벤트를 먼저 발생시키고, 없으면 기존 `Process.Start` 폴백.
- `MainWindow.xaml.cs`: `_viewerConfigService` 필드 추가. `AttachFolderBrowser()`에서 `FileOpenRequested` 구독. `Browser_FileOpenRequested` 핸들러: 뷰어가 있으면 새 `LayoutDocument`로 열고, 없으면 `Process.Start` 폴백.
- `docs/architecture.md`: Viewers 디렉터리, ViewerHost, ViewerConfigService 항목 추가.

---

## v1.4.0 (2026-06-23)

### 개발 가이드 문서 정비

- `CLAUDE.md` 생성: 기능별 수정 체크리스트, AvalonDock 주의사항, 문서 작성 규칙 정의.
- `docs/architecture.md` 생성: 파일 구조, 서비스 역할, 확장 포인트 상세 참조 문서.
- `docs/todo/`, `docs/done/` 기반 개발 요청 관리 워크플로 확립.

### 테마 5개 추가 및 크래시 수정

- Nord, Catppuccin Mocha, Solarized Dark, Dracula, GitHub Primer 테마 추가.
- 각 테마별 XAML 팔레트, AppTheme enum, MainWindow 메뉴, SettingsWindow RadioButton 등록.
- `IsThemeDictionary()` 하드코딩 문제 수정 → `Enum.GetNames()`로 신규 테마 자동 인식.
- 신규 XAML 파일 `.csproj` `<Page>` 미등록으로 인한 런타임 크래시 수정.

### 블랙 테마 UI 버그 수정

- ContextMenu 테두리 두꺼움: ControlTemplate 재정의로 WPF 기본 드롭섀도 제거.
- 폴더패널 탭 X(닫기) 버튼 검정색: AvalonDock 기본 템플릿이 색상 하드코딩하는 문제를
  `LayoutDocumentTabItem` ControlTemplate 완전 재정의로 해결.
- `Controls.xaml`에서 선언 순서 문제로 인한 `{StaticResource}` → `{DynamicResource}` 수정.

### 단축키 시스템 및 설정 창

- `KeybindingManager` 서비스 도입: 기본 매핑 + JSON 커스터마이징 저장.
- 설정 창에 단축키 탭 추가, `KeyCaptureWindow` 팝업 구현.
- 설정 창에 테마 탭 추가: RadioButton 즉시 적용 + 취소 시 원복.
- UX 개선 (코드 리뷰 반영): Settings, KeyCapture 화면.

### F5 키 동작 변경

- F5 키 동작을 반대편 패널로 복사 → 양쪽 패널 새로고침으로 변경.

---

## v1.1.0

### 폴더 컴포넌트 드래그앤드롭

- 폴더 컴포넌트에서 선택한 파일과 폴더를 다른 패널 및 외부 프로그램으로 드래그할 수 있도록 구현.
- Windows Explorer 등 외부 프로그램의 파일 드롭을 받아 현재 폴더 또는 드롭한 하위 폴더로 복사하도록 구현.
- 기본 드롭은 복사로 처리하고 작업 전에 Yes/No 확인 대화상자를 표시.
- `Ctrl` 드롭은 복사, `Shift` 드롭은 이동, `Alt` 드롭은 Windows 바로가기(`.lnk`) 생성으로 처리.
- 동일 폴더 이동과 자기 자신 또는 하위 폴더로의 재귀 복사·이동을 방지.

### 즐겨찾기 컨텍스트 메뉴

- 즐겨찾기 항목 우클릭 시 프로그램 전용 컨텍스트 메뉴 표시.
- `Explorer에서 폴더 열기` 기능 추가.
- `즐겨찾기 삭제` 기능과 삭제 확인 대화상자 추가.
- 빈 목록 영역에서는 컨텍스트 메뉴가 열리지 않도록 처리.

### 검증

- .NET Framework 4.8 Debug 구성 MSBuild 성공.
- 빌드 오류 0개. 기존 `SearchPanel.NavigateRequested` 미사용 경고만 확인.

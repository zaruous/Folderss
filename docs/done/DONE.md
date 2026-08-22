# DONE

기존 릴리스 완료 이력입니다. 현재 아이템의 상태와 상세 내용은 `docs/items/`에서 관리합니다.

버전 섹션은 실제 git 태그(= `AssemblyInfo.cs` 버전)를 기준으로 합니다.
과거에 이 문서의 번호가 태그와 별개로 매겨져 `v1.4.0` 이후 구간이 실제 태그보다 앞서 나가 있었으므로,
v1.6.0 작업 시점에 각 항목의 커밋을 `git tag --contains`로 대조해 실제 릴리스 태그 번호로 정정했습니다
(예: 이전 문서의 `v1.6.5`·`v1.6.4` 항목은 실제로 `v1.5.8`에 포함됨). 항목 내용은 그대로입니다.

---

## v1.6.1 (2026-08-22)

### 폴더 패널 그리드의 폴더 아이콘 구분 개선

- `Models/FileSystemItem.cs` — `Icon`의 디렉터리 분기 반환값을 `📁`에서 `📂`로 변경. 닫힌 폴더 이모지는 목록의 파일 아이콘(`📄`, `🖼`, `📦`)과 글리프 실루엣이 비슷해 아이콘 열(폭 38)에서 폴더와 파일이 한눈에 구분되지 않았다.
- `Controls/FolderBrowser.xaml`의 첫 번째 `GridViewColumn`(`DisplayMemberBinding="{Binding Icon}"`)이 이 프로퍼티를 유일하게 바인딩하므로 파일 목록 그리드 전체에 한 번에 반영된다.
- 폴더 트리(`FolderBrowser.GetTreeItemHeader`)와 즐겨찾기 패널(`FavoritesPanel.xaml`)은 항목이 모두 폴더라 파일과 혼동될 여지가 없어 기존 `📁`를 유지했다.
- `Properties/AssemblyInfo.cs` — `AssemblyVersion`/`AssemblyFileVersion`을 `1.6.1.0`으로 갱신.

---

## v1.6.0 (2026-07-30)

### 문서 탭 패널 잠금 기능

- `Services/PanelLockService.cs` (신규) — 잠금 키 목록을 `%LOCALAPPDATA%\Folderss\panel-locks.xml`에 저장·복원. 토글 시 즉시 기록하고, 임시 파일 교체 방식으로 저장 중 중단에도 파일이 손상되지 않게 처리.
- `MainWindow.xaml.cs` — 문서 탭 우클릭 메뉴에 체크 가능한 `패널 잠금` 항목 추가. 잠금 시 `LayoutDocument.CanClose = false`로 탭 X 버튼(템플릿의 `CanExecute` 연동)과 컨텍스트 메뉴 `닫기`를 막고, 탭 제목에 `🔒 ` 접두사를 붙인다.
- 잠금 키는 `ContentId` 기반(`GetPanelLockKey`) — 폴더 패널은 `folder-panel|<패널 ID>`(폴더 이동에도 유지), 뷰어 탭은 `viewer|<정규화 경로>`(같은 파일 재오픈 시 유지), 그 밖의 닫기 가능 탭은 `ContentId` 그대로라 새 탭 종류가 추가돼도 별도 등록이 필요 없다. 고정 탭(`left-folder`, `right-folder`, `add-folder-panel`)은 대상에서 제외.
- `ApplyPanelLockStates()`를 레이아웃 복원 직후·`F11` 최대화 복원 후·도킹 배치 초기화 후에 호출. AvalonDock이 `CanClose`를 레이아웃 XML에 함께 직렬화하므로 잠금 파일이 단일 기준이 되도록 잠김/해제를 양방향으로 재설정하고, 고정 탭은 항상 `CanClose = false`로 강제한다.
- `LayoutContent.Close()`는 `CanClose`를 검사하지 않으므로 코드에서 직접 닫는 경로를 점검 — `CloseConsoleDocument()`에 `CanClose` 확인 추가(콘솔 패널 내부 닫기 버튼 우회 차단), `다른/왼쪽/오른쪽 탭 닫기`는 기존 확인 유지.
- 탭 제목 갱신 경로(`FolderBrowser_PathChanged`, `CreateViewerHost`의 `TitleChanged`)를 `SetDocumentTitle()`로 통일해 제목이 바뀌어도 잠금 표시가 유지되게 처리.
- `README.md`, `docs/architecture.md`, `CLAUDE.md`(새 문서 탭 추가 시 `ApplyPanelLockState`/`SetDocumentTitle` 호출 규칙) 반영.

---

## v1.5.8 (2026-07-26)

### 파일 메타데이터에 경로 표시 추가

- `Controls/FolderBrowser.xaml` — 선택된 항목 메타데이터 목록 맨 위(이름 위)에 `경로` 라벨 행 추가(`MetadataPath`, TextWrapping).
- `Services/FilePreviewService.cs` — `FileMetadata.FullPath` 프로퍼티 추가, `ReadMetadata()`가 `info.FullName`을 채움.
- `Controls/FolderBrowser.xaml.cs` — `ApplyMetadata()`에서 경로 표시, 초기화 시 함께 비움.

### 즐겨찾기 도크의 디스크 사용량 고정 바로가기 레이어 제거

- `Controls/PinnedShortcutsPanel.xaml/.cs` 삭제 — `DiskUsageMiniPanel` 도입으로 즐겨찾기 도크 안 "디스크 사용량" 고정 바로가기 레이어가 중복되어 제거. 도크 콘텐츠를 `FavoritesPanel` 단독으로 되돌리고(`ResolveDockContent`/`BuildDefaultDockLayout`/`CreateFavoritesDock` 동기화), `FavoritesPanel.PinnedItems`/`PinDiskUsageShortcut`과 `ShowDiskUsage_Click`의 고정 호출도 삭제.
- `FavoritesConfiguration.Pinned`, `FavoriteLocation.IsSpecial`/`SpecialKind`는 기존 `favorites.xml` 호환을 위해 모델에만 유지(UI 미사용).

### 즐겨찾기 위 디스크 사용량 미니 패널 추가

- `Controls/DiskUsageMiniPanel.xaml/.cs` (신규) — 드라이브별 얇은 사용량 바 + 남은 용량(GB) + 상세 툴팁의 컴팩트 뷰. `IsVisibleChanged`로 표시될 때 자동 새로 고침, 컨텍스트 메뉴로 수동 새로 고침.
- `MainWindow.xaml` — 즐겨찾기 열(DockWidth 230)을 세로 `LayoutPanel`로 재구성. 위쪽 `LayoutAnchorablePane`(DockHeight 170)에 `disk-usage-mini` 앵커러블(CanClose=False, CanHide/CanAutoHide=True) 배치. `보기` 메뉴에 `디스크 사용량 미니 패널` 항목 추가.
- `MainWindow.xaml.cs` — `ResolveDockContent`에 `disk-usage-mini` 등록, `BuildDefaultDockLayout()` 즐겨찾기 열 세로 구성, `EnsureDiskUsageMiniDock()`으로 구버전 저장 레이아웃 복원 후 자동 삽입(가로 패널이면 세로 컬럼으로 감싸기), `ShowDiskUsageMini_Click`으로 숨김 후 재표시.
- 스크린샷으로 레이아웃 확인 완료(기존 dock-layout.xml에서 자동 마이그레이션 동작 검증).

### 디스크 사용량 레이어 도입 후 시작 크래시 수정

- `MainWindow.xaml`, `MainWindow.xaml.cs` — 고정 바로가기 분리 커밋(0a93c22)에서 `FavoritesPanel`이 `PinnedShortcutsPanel`과 함께 익명 `Grid`로 감싸져 즐겨찾기 앵커러블의 콘텐츠가 Grid로 바뀌었는데, `ResolveDockContent("favorites")`·`BuildDefaultDockLayout()`·`CreateFavoritesDock()`는 여전히 `FavoritesPanel`을 도킹 콘텐츠로 할당해 `DockManager.Layout` 설정 시 `InvalidOperationException`("지정한 요소가 이미 다른 요소의 논리 자식입니다")으로 시작 즉시 크래시. 저장 레이아웃 복원 실패(조용히 catch) 후 폴백 `BuildDefaultDockLayout()`에서 unhandled로 종료되는 구조였음. Grid에 `x:Name="FavoritesDockContent"`를 부여하고 세 곳 모두 이를 도킹 콘텐츠로 사용하도록 수정.
- 교훈: 도킹 앵커러블의 XAML 콘텐츠 구조를 바꾸면 `ResolveDockContent`와 레이아웃 재구성 코드(`BuildDefaultDockLayout`, `CreateFavoritesDock`)도 같은 요소를 반환하도록 함께 갱신해야 함.

---

## v1.5.7 (2026-07-15)

### 문서 탭 컨텍스트 메뉴에 "탐색기로 열기" 추가

- `MainWindow.xaml.cs` — 문서 탭(`DockManager_PreviewMouseRightButtonDown`) 우클릭 메뉴에 "탐색기로 열기" 항목 추가. `GetDocumentPathForExplorer()`가 탭 콘텐츠(`FolderBrowser`/`ViewerHost`)에서 경로를 얻어, `OpenPathInExplorer()`가 `explorer.exe /select,"<경로>"`로 상위 폴더에서 해당 폴더/파일이 선택된 상태로 탐색기를 연다. 폴더 패널 탭은 `CurrentPath`, Markdown 등 파일 뷰어 탭은 `ViewerHost.CurrentFilePath`를 사용하며, `+ 새 패널` 탭처럼 경로가 없는 탭에는 메뉴 항목이 표시되지 않음.
- `Controls/ViewerHost.xaml.cs` — 현재 열린 파일 경로를 노출하는 `CurrentFilePath` 프로퍼티 추가.

### Markdown 뷰어 외부 변경 반영 시 스크롤 위치 유지

- `markdown-app.html` — `app.reloadContent()`가 내용을 교체하기 전에 미리보기(`#preview-pane`)와 편집기(`editor-textarea`) 스크롤 위치를 저장해두었다가, 렌더링 이후 복원하도록 수정. 기존에는 `preview.innerHTML` 교체로 미리보기 컨테이너가 잠깐 비워지면서 스크롤이 항상 맨 위로 초기화되는 문제가 있었음(파일 외부 변경 확인창에서 "예"를 눌러 반영할 때마다 발생).

---

## v1.5.3 (2026-06-30)

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

## v1.5.2 (2026-06-30)

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

## v1.4.5 (2026-06-26)

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

## v1.4.1 (2026-06-23)

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

## v1.1.0 (2026-06-21)

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

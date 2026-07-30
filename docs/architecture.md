# Folderss 아키텍처 참조

## 디렉터리 구조

```
Folderss/
├── Controls/
│   ├── FolderBrowser.xaml/.cs      — 핵심 파일 브라우저 컨트롤 (패널 재사용 단위, 선택적 좌측 트리뷰·폴더 고정 잠금 포함)
│   ├── FavoritesPanel.xaml/.cs     — 즐겨찾기 패널
│   ├── SearchPanel.xaml/.cs        — 파일 검색 패널 (내용/파일명 대상 선택, 확장자 필터, 대/소문자·정규식·범위 옵션)
│   ├── ConsolePanel.xaml/.cs       — ConPTY 기반 내장 터미널 패널
│   ├── DiskUsagePanel.xaml/.cs     — 드라이브별 디스크 사용량 패널 (가로바, GB 단위 총량/사용량/여유)
│   ├── DiskUsageMiniPanel.xaml/.cs — 즐겨찾기 열 상단 도킹용 컴팩트 디스크 사용량 뷰 (얇은 바, 남은 용량, 툴팁 상세)
│   └── ViewerHost.xaml/.cs         — 파일 뷰어 컨테이너 (IFileViewer 래퍼)
├── Viewers/
│   ├── MarkdownViewer.xaml/.cs     — Markdown 미리보기·편집·내보내기 + 활성 탭 중심 파일 변경 감시
│   ├── MonacoViewer.xaml/.cs       — Monaco 기반 코드/텍스트 편집기
│   ├── TextViewer.xaml/.cs         — 읽기 전용 텍스트 뷰어
│   └── IFileViewer.cs              — 뷰어 인터페이스 + ViewerCapabilities/ExportFormat enum
├── Models/
│   ├── FileSystemItem.cs           — 파일·폴더 뷰모델
│   ├── FavoriteLocation.cs         — 즐겨찾기 그룹·항목 모델, 그룹 위쪽 고정 바로가기(FavoritesConfiguration.Pinned) 포함
│   ├── DriveUsageInfo.cs           — 드라이브 총량·사용량·여유 공간(GB, 비율) 모델
│   ├── SearchResult.cs             — 파일 검색 결과 모델
│   └── SearchTarget.cs             — 검색 대상 enum (Content/FileName)
├── Services/
│   ├── FileOperationService.cs     — 복사·이동·삭제·이름변경·새 폴더
│   ├── FilePreviewService.cs       — 텍스트·이미지 미리보기 + 메타데이터
│   ├── SearchService.cs            — 파일 검색 (내용/파일명 대상, 확장자 필터, 대/소문자, 정규식)
│   ├── DockLayoutService.cs        — AvalonDock 레이아웃 저장·복원
│   ├── SessionStateService.cs      — 열린 폴더 경로 세션 저장·복원
│   ├── PanelLockService.cs         — 문서 탭(패널) 닫기 잠금 상태 저장·복원
│   ├── FavoritesService.cs         — 즐겨찾기 목록 저장·복원
│   ├── DiskUsageService.cs         — `DriveInfo.GetDrives()` 기반 준비된 드라이브 사용량 조회
│   ├── KeybindingManager.cs        — 단축키 매핑 및 커스터마이징
│   ├── ViewerConfigService.cs      — 확장자 ↔ 뷰어 매핑 (viewer-config.json 저장)
│   ├── OpenWithService.cs          — 확장자 마스크 기반 외부 열기 프로그램 저장·실행
│   ├── ConsoleSettingsService.cs   — 콘솔 기본 프로필/사용자 정의 프로필 설정 저장
│   ├── ConsoleSessionService.cs    — 기본 셸 탐색, 프로필 해석, 외부 터미널 실행 관리
│   ├── ShellContextMenuService.cs  — Windows 쉘 우클릭 컨텍스트 메뉴
│   └── ThemeManager.cs             — 테마 전환 및 저장
├── Converters/
│   └── FractionToStarConverter.cs  — 0~1 비율 → Star `GridLength` 변환 (디스크 사용량 가로바)
├── Themes/
│   ├── Black.xaml                  — 블랙 테마 색상 팔레트
│   ├── Light.xaml                  — 라이트 테마 색상 팔레트
│   ├── Nord.xaml                   — Nord 테마
│   ├── Catppuccin.xaml             — Catppuccin Mocha 테마
│   ├── Solarized.xaml              — Solarized Dark 테마
│   ├── Dracula.xaml                — Dracula 테마
│   ├── GitHub.xaml                 — GitHub (Primer Light) 테마
│   └── Controls.xaml               — 공통 컨트롤 스타일 (모든 테마 공유)
├── MainWindow.xaml/.cs             — 메인 창, AvalonDock 호스트, 전역 단축키
├── SettingsWindow.xaml/.cs         — 설정 창 (테마, 단축키, 뷰어, 열기 프로그램, 콘솔)
├── KeyCaptureWindow.cs             — 단축키 입력 캡처 팝업
├── AboutWindow.cs                  — 정보 창
├── PromptWindow.cs                 — 이름 변경·새 폴더 입력 다이얼로그
├── App.xaml/.cs                    — 앱 진입점, 테마 초기 로드
└── docs/
    ├── architecture.md             — 코드 구조와 확장 포인트 참조
    ├── PROJECT.md                  — 로컬 개발 아이템 운용 규칙
    ├── items/                      — 현재 개발 아이템 상태와 상세 내용의 단일 기준
    └── done/DONE.md                — 기존 릴리스 완료 이력
```

---

## 서비스 역할 상세

### ThemeManager
- `AppTheme` enum으로 지원 테마 정의
- `Themes/<이름>.xaml` 파일을 `ResourceDictionary`로 교체하는 방식으로 런타임 전환
- `%LOCALAPPDATA%\Folderss\theme.txt`에 마지막 테마 저장
- **테마 추가 시 연관 파일**: ThemeManager.cs, MainWindow.xaml+cs, SettingsWindow.xaml+cs

### MarkdownViewer 이미지 리소스 해석
- 미리보기 HTML은 `https://folderss-viewer/markdown-app.html`(가상 호스트 → `Viewers/Resources`)에서 로드되므로,
  마크다운 본문의 상대 경로 이미지(`![](img/foo.png)`, `![](../assets/foo.png)` 등)를 그대로 두면
  현재 파일 기준이 아니라 `Resources` 폴더 기준으로 요청되어 항상 깨진다.
- `marked.use({ renderer: { image ... } })`로 스킴이 없는 이미지 href를 `https://folderss-doc-asset/resolve?p=<encodeURIComponent(href)>`로 치환한다 (`http(s):`, `data:`, `file:`, `ftp:`, `mailto:`만 원본 유지).
- `MarkdownViewer.OnWebResourceRequested`가 `folderss-doc-asset` 호스트 요청을 가로채, 쿼리의 원본 경로 문자열을
  `ResolveLocalLinkPath`(파일 내 링크 클릭과 동일한 해석 로직)에 그대로 넘겨 현재 열려 있는 `.md` 파일 기준
  절대 경로로 해석한 뒤 `File.ReadAllBytes`로 읽어 응답한다.
- 쿼리 문자열에 원본 경로를 담아 브라우저의 URL 정규화(가상 호스트 폴더 매핑은 상위 폴더로 못 올라감)를 우회하므로
  `../`로 상위 폴더의 이미지를 참조해도 정상 동작한다.
- CSP `img-src`에 `folderss-doc-asset`과 `data:`, `http(s):`을 추가해 로컬 리졸브 이미지·데이터 URI·원격 이미지를 모두 허용한다.
- 알려진 제약: "HTML로 내보내기"(`exportHtml`)로 저장한 독립 HTML 파일은 앱 밖에서 열리므로 `folderss-doc-asset` 스킴이 동작하지 않아
  상대 경로 이미지가 다시 깨진다 (미리보기·인쇄/PDF 내보내기는 같은 WebView2 세션 안에서 렌더링되므로 영향 없음).

### KeyBindingService
- 기본 단축키 매핑을 코드에서 정의
- 사용자 커스터마이징을 XML로 `%LOCALAPPDATA%\Folderss\keybindings.xml`에 저장
- `kb.Matches(e, "CommandId")` 패턴으로 MainWindow PreviewKeyDown에서 사용
- **단축키 추가 시 연관 파일**: KeyBindingService.cs, MainWindow.xaml.cs, SettingsWindow

### SearchPanel / SearchService
- `Ctrl+F`(`ShowSearch`)로 여는 별도 팝업 창(`파일 검색`)에서 실행, `MainWindow.ShowSearchPanel()` 참고
- `TargetCombo`로 검색 대상을 `내용 검색`/`파일명 검색` 중 선택 (`SearchTarget.Content` / `SearchTarget.FileName`)
- `ExtBox`에 `cs, txt`처럼 콤마/세미콜론/공백으로 구분해 확장자 필터 입력, 비우면 전체 확장자 대상
- `SearchService.SearchAsync`가 확장자 필터를 정규화(`.` 접두사 보정)한 뒤 대상에 따라 파일 내용 라인 단위 스캔(`ScanFile`) 또는 파일명만 비교(`ScanFileName`)로 분기
- 결과는 `SearchResult.LineNumber == 0`이면 파일명 검색 결과로 취급해 목록에서 줄 번호 칸을 비움
- `CaseToggle`(대/소문자), `RegexToggle`(정규식), `ScopeCombo`(현재 폴더만/하위 폴더 포함)는 검색 대상과 무관하게 공통 적용

### DiskUsagePanel / DiskUsageService / DiskUsageMiniPanel
- `보기 > 디스크 사용량 보기` 클릭 시 `MainWindow.ShowDiskUsage_Click`이 `ShowDiskUsagePanel()`로 문서 탭을 연다.
- (제거됨) 과거 `PinnedShortcutsPanel`이 즐겨찾기 도크 상단 레이어로 `디스크 사용량` 고정 바로가기를 표시했으나, `DiskUsageMiniPanel` 도입으로 중복이라 삭제됨. `FavoritesConfiguration.Pinned` 컬렉션과 `FavoriteLocation.IsSpecial`/`SpecialKind`는 기존 `favorites.xml` 호환을 위해 모델에만 남아 있고 UI에서는 사용하지 않는다.
- 문서 탭은 `OpenViewerTab`과 동일한 패턴(첫 `LayoutDocumentPane`, `+ 새 패널` 앞에 삽입 후 `MoveAddPanelTabToEnd()`)을 따르며 `ContentId == "disk-usage"`로 단일 인스턴스만 유지한다.
- `DiskUsageService.GetDriveUsage()`는 `DriveInfo.GetDrives()`에서 `IsReady`인 드라이브만 조회하고, `DiskUsagePanel`은 각 드라이브를 GB 단위 총량/사용량/여유 공간과 사용 비율 기반 가로바(두 개의 Star `ColumnDefinition`, `FractionToStarConverter`)로 표시한다.
- `DiskUsageMiniPanel`은 즐겨찾기 열 상단에 도킹되는 컴팩트 버전이다. `MainWindow.xaml`에서 즐겨찾기 열이 세로 `LayoutPanel`로 구성되며, 위쪽 `LayoutAnchorablePane`(DockHeight 170)에 `disk-usage-mini` 앵커러블(CanClose=False, CanHide/CanAutoHide=True)로 배치된다. 패널이 보일 때(`IsVisibleChanged`)와 컨텍스트 메뉴 `새로 고침`에서 갱신한다. 구버전 저장 레이아웃에는 이 앵커러블이 없으므로 복원 성공 후 `MainWindow.EnsureDiskUsageMiniDock()`이 즐겨찾기 팬 위에 자동 삽입하고, `보기 > 디스크 사용량 미니 패널` 메뉴로 숨긴 뒤 다시 표시할 수 있다.

### 개발 아이템 문서
- 현재 개발 아이템은 GitHub Project가 아니라 `docs/items/<항목>.md`에서 관리한다.
- 상태 값은 `Todo`, `In Progress`, `Ready for Verification`, `Done`만 사용한다.
- 요구사항, 설계 또는 원인 분석, 구현 내용, 변경 파일, 검증, 변경 이력은 항목 파일 본문에 직접 기록한다.
- `docs/PROJECT.md`가 로컬 아이템 파일의 필수 형식과 상태 전환 규칙을 정의한다.

### PanelLockService / MainWindow 패널 잠금 흐름
- 문서 탭 우클릭 메뉴의 `패널 잠금`으로 탭 단위 닫기 잠금을 토글한다 (`MainWindow.TogglePanelLock`).
- 잠금 키는 `MainWindow.GetPanelLockKey(LayoutDocument)`가 `ContentId`에서 만든다.
  - 폴더 패널: `folder-panel|<패널 ID>` — `ContentId`의 경로 부분을 제외해 패널에서 폴더를 이동해도 잠금이 유지된다.
  - 뷰어 탭: `viewer|<정규화된 소문자 파일 경로>` — 같은 파일을 다시 열면 잠긴 상태로 열린다.
  - 그 밖의 탭(`console`, `disk-usage` 등): `ContentId` 그대로. 새로 추가되는 닫기 가능 탭도 별도 등록 없이 잠금 대상이 된다.
  - `left-folder`, `right-folder`, `add-folder-panel`은 항상 닫을 수 없는 고정 탭이라 `null`을 반환해 메뉴에 나타나지 않는다.
- 상태는 `%LOCALAPPDATA%\Folderss\panel-locks.xml`에 잠금 키 목록으로 저장되며, 토글할 때마다 즉시 기록한다.
- `MainWindow.ApplyPanelLockStates()`가 레이아웃 복원 직후, 패널 최대화 복원 후, 도킹 배치 초기화 후에 잠금 상태를
  `LayoutDocument.CanClose`와 탭 제목의 `🔒 ` 접두사에 반영한다. 저장된 레이아웃 XML에 `CanClose`가 함께 직렬화되더라도
  잠금 파일이 단일 기준이 되도록 양방향으로 다시 설정한다.
- 닫기 가능한 문서 탭을 새로 만드는 코드는 `LayoutDocument` 생성 직후 `ApplyPanelLockState(document)`를 호출해야 한다
  (`OpenViewerTab`, `ShowConsolePanel`, `ShowDiskUsagePanel` 참고).
- 탭 제목을 갱신하는 코드는 `SetDocumentTitle(content, title)`을 사용해야 잠금 표시가 유지된다 (`FolderBrowser_PathChanged`, `CreateViewerHost`의 `TitleChanged`).
- `LayoutContent.Close()`는 `CanClose`를 검사하지 않으므로, 코드에서 직접 닫는 경로는 `CanClose`를 먼저 확인해야 한다
  (`CloseTabsExcept`/`CloseTabsToLeft`/`CloseTabsToRight`, `CloseConsoleDocument`).
- 잠금 표시가 실시간으로 반영되도록 토글 후 `CommandManager.InvalidateRequerySuggested()`로 탭 닫기 버튼의 `CanExecute`를 다시 평가한다
  (`Controls.xaml`의 `LayoutDocumentTabItem` 템플릿에서 닫기 버튼 `Visibility`가 `IsEnabled`에 묶여 있다).

### DockLayoutService / SessionStateService
- 창을 트레이로 숨기기 전과 실제 종료 전에 자동 저장하고 다음 실행 시 복원
- 레이아웃은 임시 파일에 직렬화한 뒤 교체해 저장 중 중단으로 기존 파일이 손상되지 않도록 처리
- 저장 위치: `%LOCALAPPDATA%\Folderss\dock-layout.xml`, `dock-layout.xml.version`, `session.xml`
- 버전 정보가 없는 기존 레이아웃에서 콘솔이 왼쪽 폴더 탭에 합쳐진 경우에만 한 번 하단 패널로 이관
### UpdateService / MainWindow 업데이트 흐름
- GitHub 최신 릴리스 정보를 조회하고 설치형 자산(`.exe`, `.msi`)과 zip 배포본을 구분한다.
- zip 배포본은 `Folderss.exe`, `Folderss.dll`, `Folderss.deps.json`, `Folderss.runtimeconfig.json`이 함께 있는 패키지 디렉터리만 유효한 업데이트 대상으로 본다.
- 현재 실행 중인 프로세스 경로는 `Assembly.Location`이 아니라 실제 프로세스 실행 파일 경로를 사용해야 한다.
- zip 업데이트는 앱 종료 후 설치 디렉터리 전체를 교체하고 다시 실행한다.
- 설치형 자산은 현재 폴더를 덮어쓰지 않고 앱 종료 후 설치 프로그램 자체를 실행한다.

### ConsoleSettingsService
- 콘솔 설정을 `%LOCALAPPDATA%\Folderss\console-settings.xml`에 저장
- 기본 제공 프로필(`PowerShell 7`, `Windows PowerShell`, `명령 프롬프트`)과 사용자 정의 프로필을 함께 관리
- 기본 프로필 키와 사용자 정의 실행 파일/인수 정보를 저장
- 레거시 `PreferredShellKind` 값이 있으면 현재 `PreferredProfileKey`로 변환해 로드

### ConsoleSessionService / ConsolePanel
- `보기 > 콘솔` 메뉴와 왼쪽 폴더 아래의 `ContentId="console"` 문서 패널로 표시
- `EasyWindowsTerminalControl`과 ConPTY를 사용해 실제 터미널 세션을 표시
- `+ 새 콘솔` 탭으로 여러 세션을 추가할 수 있다
- 활성 폴더 기준 시작 및 실행 중 `현재 폴더로 이동` 지원
- 기본 셸 3종과 사용자 정의 프로필을 동일한 선택 UI에서 전환
- 외부 터미널 버튼은 현재 선택한 프로필 기준으로 별도 콘솔 창을 연다
- 콘솔 폰트 크기 설정 UI는 존재하지만 현재 런타임 반영에는 추가 보완이 필요하다

---

## 주요 확장 포인트

### 테마 추가
1. `Themes/<이름>.xaml` 생성 — `Black.xaml` 구조 복사, 색상만 교체
2. `AppTheme` enum 값 추가 (`ThemeManager.cs`)
3. 진입점 3곳 등록: MainWindow 메뉴, MainWindow 핸들러, SettingsWindow RadioButton
4. 자세한 체크리스트는 `CLAUDE.md` 참조

### 새 패널/컨트롤 추가
- AvalonDock `LayoutAnchorable`(고정 패널) 또는 `LayoutDocument`(탭 문서)로 추가
- `MainWindow.xaml`의 `<layout:LayoutRoot>` 아래에 선언하거나 코드에서 동적 생성
- `DockLayoutService`가 자동으로 저장·복원 처리

### 새 설정 항목 추가
- `SettingsWindow.xaml`에 UI 추가
- 해당 서비스의 저장/로드 로직 업데이트
- 설정 창 취소 시 원복 로직 필요 여부 검토 (`_originalTheme` 패턴 참고)

---

## AvalonDock 스타일링 주의사항

### StaticResource vs DynamicResource
`Controls.xaml` 내에서 선언 순서가 중요:
- 앞에 위치한 스타일이 뒤에 선언된 리소스를 `{StaticResource}`로 참조하면 **런타임 크래시**
- 이 경우 `{DynamicResource}`로 변경

### LayoutDocumentTabItem 닫기 버튼
AvalonDock 기본 `Generic.xaml`이 닫기 버튼 Path 색상을 하드코딩.
`Style.Resources`로는 재정의 불가 → `Controls.xaml`에 완전한 `ControlTemplate` 재정의 포함됨.

### ContextMenu 테두리
WPF 기본 `ContextMenu`는 `SystemDropShadowChrome`으로 테두리가 두껍게 보임.
`Controls.xaml`의 `<Style TargetType="ContextMenu">` ControlTemplate으로 해결됨.

---

## 데이터 흐름

```
사용자 입력
    → MainWindow.Window_KeyDown (전역 단축키)
    → FolderBrowser.KeyDown (패널 내 단축키)
    → FileOperationService (파일 작업)
    → FolderBrowser.Refresh() (목록 갱신)

테마 전환
    → MenuItem/RadioButton 클릭
    → ThemeManager.ApplyTheme()
    → App.Resources.MergedDictionaries 교체
    → 모든 DynamicResource 바인딩 자동 갱신
```


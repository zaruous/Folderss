# DONE

기존 릴리스 완료 이력입니다. 현재 아이템의 상태와 상세 내용은 `docs/items/`에서 관리합니다.

버전 섹션은 실제 git 태그(= `AssemblyInfo.cs` 버전)를 기준으로 합니다.
과거에 이 문서의 번호가 태그와 별개로 매겨져 `v1.4.0` 이후 구간이 실제 태그보다 앞서 나가 있었으므로,
v1.6.0 작업 시점에 각 항목의 커밋을 `git tag --contains`로 대조해 실제 릴리스 태그 번호로 정정했습니다
(예: 이전 문서의 `v1.6.5`·`v1.6.4` 항목은 실제로 `v1.5.8`에 포함됨). 항목 내용은 그대로입니다.

---

## v1.8.1 (2026-10-05)

### 설정 > 플러그인 탭에 `업데이트` 버튼 (2026-10-08)

- `SettingsWindow.xaml` — `GitHub에서 설치…`와 `제거` 사이에 `업데이트` 버튼. `제거`처럼 선택 행이 없으면 아무것도 하지 않는다.
- `SettingsWindow.PluginUpdate_Click` — `PluginSourceStore`에 기록된 출처가 GitHub(`PluginGitHubSource.TryParseSourceKey`)일 때만 `releases/latest`를 조회. 로컬 zip·기록 없음은 `플러그인 찾기…`/`GitHub에서 설치…`로 교체하라고 안내만 한다.
- 버전 비교 `PluginGitHubSource.IsSameVersion`: 태그 `v1.2.0`과 manifest `1.2.0`, `1.2`와 `1.2.0`을 같게 본다. 태그가 같으면 받지 않고, 태그 형식이 달라(날짜 등) 비교가 안 되면 받은 zip의 `plugin.json` 버전으로 다시 비교해 같으면 교체하지 않는다. 받은 zip의 id가 선택한 플러그인과 다르면 중단.
- 받기 전에 `현재 → 새 버전` 확인 창(기본 아니오, 최대 100MB를 받으므로). 교체는 기존 `InstallPluginPackage`를 그대로 타서 출처 동일 → 일반 교체 확인, 로드된 플러그인은 재시작 안내.
- zip 선택·내려받기는 `DownloadReleaseZipAsync`로 빼서 GitHub 설치와 공유. 내려받는 동안 `GitHub에서 설치…`·`업데이트` 둘 다 비활성.
- 테스트: `PluginGitHubSourceTests` — `TryParseSourceKey`(출처 키 왕복, local/기록 없음/다른 호스트/https 주소 거부), `IsSameVersion`.
- 한계: 최신 릴리스가 현재보다 낮아도(수동으로 Pre-release를 설치한 경우) 버전만 보여 주고 막지는 않는다. GitHub API 한도(시간당 60회)는 업데이트 확인마다 1회 쓴다.

### 트레이 아이콘 메뉴에 플러그인 / 플러그인 관리 노출 (2026-10-05)

- `MainWindow.xaml.cs` `InitTrayIcon` — 트레이 오른쪽 클릭 메뉴: `열기 / 플러그인 ▸ / 플러그인 관리… / 종료`. `플러그인 ▸`은 열 때마다(`DropDownOpening`) `PluginManager.ListInstalled`로 다시 채워 ⋯ 메뉴와 같은 목록·읽기 오류·"플러그인 관리…"를 보인다(빈 하위 메뉴는 ▸가 안 보여 자리표시 항목을 둠).
- 본체 창이 트레이에 숨어 있을 때 플러그인 창은 소유자 없이 연다(숨은 창을 Owner로 두면 플러그인 창도 같이 숨음). `플러그인 관리…`는 설정 창이 본체를 소유자로 쓰므로 본체 창을 먼저 보인 뒤 연다.

### 클립보드 점유 시 앱 크래시 방지 (#30 코멘트 2026-10-02)

- `Services/ClipboardService.cs` (신규) — `TrySetDataObject`: `Clipboard.SetDataObject(data, true)`의 `ExternalException`(`CLIPBRD_E_CANT_OPEN`)을 잡아 경고 창을 띄우고 false 반환. WPF는 내부에서 OleSetClipboard/OleFlushClipboard를 이미 10회×100ms 재시도하므로 추가 재시도는 두지 않음(1초 넘게 다른 프로그램이 점유한 경우).
- `Controls/FolderBrowser.xaml.cs` — 복사(파일·폴더 트리)·잘라내기를 이 헬퍼로 바꿈. 실패하면 잘라내기 상태를 설정하지 않음(예전 클립보드 내용이 이동되는 것 방지). `FavoritesPanel` 경로 복사, `ConsolePanel` Ctrl+C 텍스트 복사도 동일 적용.
- 조용히 넘기지 않고 알리는 이유: 복사가 안 된 줄 모르고 붙여넣으면 예전 클립보드의 파일이 복사·이동됨.
- 붙여넣기 쪽도 보호: `TryGetFileDropList`/`TryGetText`(읽기 실패 시 경고), `ContainsFileDropList`(붙여넣기 명령 사용 가능 판단용, 자주 불려 경고 없이 false). `MainWindow.TryPasteFromClipboardInto`, `FolderBrowser` 붙여넣기 CanExecute·경로창 Ctrl+V에 적용.
- `MainWindow.TryPasteFromClipboardInto` — 잘라내기 뒤 다른 프로그램(탐색기 등)에서 복사한 파일을 붙여넣으면 `_isCut` 플래그만 보고 **이동**하던 버그. 클립보드 파일 목록이 잘라낸 목록과 같을 때만 이동하고, 다르면 복사하며 남은 잘라내기 표시를 지움.
- 등록(OleSetClipboard)은 됐고 Flush만 실패한 경우(#30 로그가 이 경우)는 `Clipboard.IsCurrent`로 확인해 성공으로 처리. 이 앱이 실행 중인 동안은 붙여넣기가 되고, 앱을 닫으면 내용이 사라질 수 있음.

### Git 창 커밋 ▾ 크래시 방지, Git 화면 클립보드 실패 알림

- `GitWindow.CommitOptions_Click` — 직전 커밋 제목 조회(`git log -1`)가 `async void` 안에서 보호 없이 실행돼, git 경로 오류(`FileNotFoundException`)나 창 닫힘 취소 시 앱이 종료될 수 있던 문제. 다른 핸들러와 같은 방식(취소는 중단, 그 밖은 경고 후 중단)으로 감쌈.
- `GitWindow` 해시 복사, `GitDiffView` Ctrl+C — 클립보드 실패를 조용히 무시하던 것을 `ClipboardService.TrySetDataObject`로 바꿔 알림.

## v1.8.0 (2026-10-02)

### 플러그인 (`docs/items/plugin-system.md`, `docs/items/plugin-github-install.md`)

- 설정 > 플러그인(zip 등록·제거), ⋯ 메뉴 > 플러그인 팝업. 플러그인별 `AssemblyLoadContext`, 실행 시점 로드, 본체 설정 읽기 전용 제공, 설정 탭 추가, 비정상 종료 감지·로그. 개발 가이드·주문서 튜토리얼 문서.
- `GitHub에서 설치…` — 공개 저장소 최신 정식 릴리스의 zip을 받아 설치(zip 여러 개면 선택, digest SHA-256 비교, 100MB 제한). 설치 출처 기록(`plugins\sources.json`)과 같은 ID 다른 출처 경고(확인 체크). 설치 전 플러그인 계약 참조 버전이 본체보다 높으면 거부.

### Git 창 (`docs/items/git-integration.md`)

- 선택 폴더 하위 다중 저장소 상태·스테이지·커밋·브랜치·로그·fetch/pull/push, diff·원격 비교, 브랜치 그래프, 인코딩 규칙(BOM 우선·기본 UTF-8), 설정 창 Git 탭·외부 비교 도구.
- reset·브랜치 생성/체크아웃·워킹트리, stash, 파일 되돌리기(git restore), 기본 버튼 + ▾ 옵션 대화상자, 평면/트리 보기, 끌어 놓기 스테이지/언스테이지, diff 보기 모드.

### 두 파일 비교 (`docs/items/folder-panel-file-compare.md`)

- 폴더 패널에서 선택한 두 파일 비교(Git diff 뷰 재사용). 비교 설정 분리, 외부 도구·HTML 보고서 공용화.

### 기타

- `GitWindow` — 변경됨 목록·트리에서 `Delete`로 고른 파일(다중 선택, 트리 폴더면 그 아래 전체)을 확인 창(기본 아니오) 후 휴지통으로 보냄. 대상은 `GitRestoreCommands.DeletableFiles`(디스크에 있는 파일만, 폴더·서브모듈·저장소 밖 제외). 추적 중인 파일이 섞이면 경고 아이콘·문구. 영구 삭제(Shift+Delete)는 두지 않음.
- `Controls/FolderBrowser.xaml.cs` — 폴더 트리 우클릭 시 트리가 접히던 버그 수정. 원인은 클릭 토글이 아니라 셸 컨텍스트 메뉴 후 `RefreshTreeAfterShellAction`이 부모의 자식 노드를 새로 만들면서(루트면 트리 전체 재생성) 펼침 상태가 사라진 것. 새로고침 전 펼친 경로를 모아(`CollectExpandedPaths`) 새로고침 후 다시 펼침(`RestoreExpandedPaths`). 이름이 바뀐 폴더는 경로가 달라져 접힌 상태로 남음.

### WebView2 초기화 실패 시 앱 크래시 방지 (#30)

- `Viewers/MarkdownViewer`·`TextViewer`·`MonacoViewer` — `CoreWebView2Environment.CreateAsync`/`EnsureCoreWebView2Async`를 기존 try/catch 안으로 옮김. 절전·장시간 유휴 후 처음 로드되는 뷰어에서 `RPC_E_DISCONNECTED`(COMException)가 `async void OnLoaded`로 올라가 앱 전체가 종료되던 문제를 해당 뷰어의 오류 표시로 대체.
- 원인(브라우저 프로세스 연결 끊김) 자체는 해결하지 않음. 탭을 다시 열면 새로 초기화됨. 이미 초기화된 뷰어의 `ExecuteScriptAsync` 경로와 `ProcessFailed` 미처리는 남아 있음.
- `Viewers/MonacoViewer` — 외부 변경 확인창이 떠 있는 동안 타이머가 다시 돌아 확인창이 겹쳐 뜨고, 확인 후 `CoreWebView2`가 null이라 `CallAppOpen`에서 `NullReferenceException`으로 종료되던 문제(#30 코멘트). `MarkdownViewer`와 같은 `_reloadPromptOpen` 가드 추가, 확인 후 탭이 닫혔으면 중단, `CoreWebView2`가 없으면 스크립트 대신 대기 내용으로 보관, 확인 뒤 최신 내용을 다시 읽음.

### PR 테스트 빌드 + preview 버전 표기

- `.github/workflows/pr-build.yml` (신규) — PR마다 Release publish 후 Actions 아티팩트(7일)로 업로드. 빌드 때만 `AssemblyInfo.cs`에 `AssemblyInformationalVersion("<버전>-preview.pr<번호>+<sha7>")`를 추가하고, `Folderss.dll` ProductVersion으로 반영 여부를 검사.
- `AboutWindow.cs` — `AssemblyInformationalVersion`이 있으면 우선 표시. 일반·릴리스 빌드에는 이 특성이 없으므로 표시 변화 없음. 업데이트 비교(`UpdateService`)는 숫자 `AssemblyVersion`을 그대로 사용.

---

## v1.7.0 (2026-09-23)

### 설정 저장 안정성 개선 (`docs/items/settings-save-reliability.md`)

- `Services/ViewerConfigService.cs` — 저장된 뷰어 매핑이 `LegacyDefaultMappings`와 같으면 무조건 버려 `.txt → Text`, `.sql → Monaco` 같은 사용자 선택이 재시작 후 사라지던 버그 수정. `viewer-config.json`에 `version: 2`를 기록하고, 버전 표기가 없으면서 현재 기본값과 같은 항목이 있는 파일(Monaco 도입 전 전체 덤프)에만 legacy 정리를 적용(`IsLegacyFullDump`). 뷰어 매핑은 `ReplaceMappings`로 한 번에 교체해 파일을 한 번만 쓴다. 테스트용 경로 주입 생성자 추가.
- `Services/SettingsFile.cs` (신규) — 설정 파일 원자적 쓰기 헬퍼(임시 파일 → `File.Move(temp, target, true)`). 단축키·뷰어·열기 프로그램·콘솔·테마 저장 5곳 통일. `KeyBindingService`의 `File.Replace`(백신·비NTFS에서 실패해 앱 종료) 제거.
- `SettingsWindow.xaml.cs` — 저장 서비스가 예외를 던지고 `Save_Click`이 항목별 `TrySave`로 독립 시도한 뒤 실패 목록(항목·파일명·예외·저장 폴더)을 `설정 저장 실패` 메시지 하나로 보고. 빈 `catch { }`로 삼키던 뷰어·열기 프로그램·콘솔·테마 저장 실패가 드러남.
- `tests/Folderss.SearchTests/ViewerConfigServiceTests.cs` (신규, 11건) + WPF 뷰어 스텁. Cursor(composer-2.5) 코드 리뷰 반영.

### Cursor 제안 소규모 기능 5건 (`docs/items/cursor-small-features.md`)

- `Services/IgnoreRuleSet.cs` (신규) — gitignore 문법 부분집합 매처. `FolderBrowser` 필터 바 `ignore` 토글로 `.gitignore`/`.folderssignore` 규칙에 직접 걸리는 항목 숨김(저장소 안에서는 `.git`도). 테스트 12건.
- 심볼릭 링크·junction 표시(🔗 아이콘, 행 툴팁, 메타정보 "링크 대상") — `FileSystemItem.IsLink/LinkTarget`, `FilePreviewService.GetLinkTarget`. 메인 메뉴 `반대편 패널에 링크 만들기` — `FileOperationService.CreateLink`(심볼릭 링크 우선, 권한 없으면 폴더는 junction).
- 검색 창 `패널에 필터 적용` — `FolderBrowser.ApplySearchResultFilter`로 결과 파일과 결과를 품은 폴더만 표시, 배너 `해제`·검색 루트 밖 이동으로 해제.
- 뷰어 미저장 표시 — `ViewerHost.IsModified`, 탭 제목 ` *`, `LayoutDocument.Closing`과 앱 종료 시 확인.
- 콘솔 폰트 크기 — 설정 저장 시 `ConsolePanel.ApplySettings()`로 열린 탭에 즉시 반영 (프로필별 작업 폴더 기억은 보류, `docs/아이디어.md`).

### 그 밖

- `Controls/SearchPanel.xaml` — 검색 창 기본 선택을 `파일명 검색`·`하위 폴더 포함`으로 변경 (`docs/items/search-panel-default-options.md`).
- `Controls/FolderBrowser.xaml.cs` — 트리뷰 새로고침(↻)이 선택한 폴더의 하위 트리만 갱신 (`docs/items/tree-view-selective-refresh.md`, v1.6.2 이후 커밋).
- `README.md` — v1.6.2 기준 전체 최신화 (`docs/items/readme-refresh-v1.6.2.md`). `docs/items/directory-compare.md` 상세설계서, `docs/아이디어.md` 추가.
- `Properties/AssemblyInfo.cs` — `1.7.0.0`.

---

## v1.6.2 (2026-09-22)

### 검색 창 개선 (`docs/items/search-panel-recursive-and-pattern-fixes.md`)

- `Services/SearchService.cs` — 하위 폴더 포함 검색이 접근 거부 폴더 하나로 통째로 끊기던 버그 수정(폴더 단위 순회, reparse point 순환 회피). 파일명 검색에 `*.cs`, `report?.txt` 와일드카드 패턴(별도 확장자 필터 대체).
- `Controls/SearchPanel.xaml/.cs` — 결과 목록 `내용` 컬럼 토글, 대상 폴더 상시 표시와 `폴더 선택…`/`현재 폴더`, 검색 창이 열린 채 폴더를 옮기면 옛 폴더를 검색하던 문제 수정(`Activated`마다 루트 갱신).
- `tests/Folderss.SearchTests` (신규) — 검색 로직 xUnit 회귀 테스트, `net8.0` 단독 실행.

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

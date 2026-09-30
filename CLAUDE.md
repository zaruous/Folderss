# Folderss — Claude 개발 가이드

## 프로젝트 개요

Windows WPF 듀얼 패널 파일 관리자. .NET 8 (`net8.0-windows`), AvalonDock 4.74.

**빌드**
```powershell
dotnet build .\Folderss.sln -c Debug
```
(SDK 스타일 프로젝트이므로 VS2022 `MSBuild.exe` 직접 실행은 `Microsoft.NET.Sdk` 리졸버 오류가 날 수 있음 — `dotnet build` 사용)

**테마 저장 경로**: `%LOCALAPPDATA%\Folderss\theme.txt`  
**세션/레이아웃**: `%LOCALAPPDATA%\Folderss\session.xml`, `dock-layout.xml`

---

## 작업 원칙

### 작업 전 반드시 확인
- `docs/architecture.md` — 연관 파일 목록과 확장 포인트
- `docs/dev-log.md` — 최근 변경 이력과 결정 사항

### 작업 후 반드시 수행
1. 빌드 성공 확인 (`Exit: 0`)
2. 아래 체크리스트 해당 항목 통과
3. `docs/dev-log.md`에 작업 로그 추가
4. README.md 관련 섹션 업데이트 (기능 추가/변경 시)

---

## 기능별 수정 체크리스트

### 테마 추가 시
- [ ] `Themes/<이름>.xaml` — 색상 팔레트 파일 생성
- [ ] `Folderss.csproj` — `<Page Include="Themes\<이름>.xaml">` 항목 추가 (누락 시 런타임 크래시)
- [ ] `Services/ThemeManager.cs` — `AppTheme` enum에 값 추가
- [ ] `MainWindow.xaml` — 테마 ContextMenu에 MenuItem 추가
- [ ] `MainWindow.xaml.cs` — 클릭 핸들러 + `UpdateThemeMenuChecks()` 업데이트
- [ ] `SettingsWindow.xaml` — 테마 탭 RadioButton 추가 (색상 스워치 포함)
- [ ] `SettingsWindow.xaml.cs` — 생성자 초기화 블록에 `IsChecked` 추가
- [ ] `README.md` — 테마 섹션 업데이트

### 단축키 추가 시
- [ ] `Services/KeybindingManager.cs` — 기본 매핑 등록
- [ ] `MainWindow.xaml.cs` — `Window_KeyDown` 핸들러에 `kb.Matches(e, "키명")` 추가
- [ ] `MainWindow.xaml` — 메뉴 `InputGestureText` 업데이트 (해당 메뉴 있으면)
- [ ] `SettingsWindow.xaml.cs` — 설정 창에서 키 표시 이름 등록 (필요 시)
- [ ] `README.md` — 단축키 표 업데이트

### 설정 항목 추가 시
- [ ] `SettingsWindow.xaml` — UI 컨트롤 추가
- [ ] `SettingsWindow.xaml.cs` — 초기화 및 저장 로직 추가. 저장은 `Save_Click`의 `TrySave(...)` 목록에 등록해 실패가 `설정 저장 실패` 메시지에 모이게 함
- [ ] 설정 저장 서비스(해당 서비스) 업데이트. 파일 쓰기는 `SettingsFile.Write`/`WriteAllText`(임시 파일 후 교체)를 쓰고, 예외를 빈 `catch { }`로 삼키지 말고 던져야 함 (다른 PC에서 권한·보안 프로그램 문제로 저장이 안 될 때 원인 파악 불가)

### 뷰어 추가 시
- [ ] `Viewers/<Name>Viewer.xaml/.cs` — `IFileViewer` 구현 (WebView2 기반은 `TextViewer` 참고)
- [ ] `.csproj` — `<Page>` 및 `<Compile>` 항목 추가
- [ ] `Services/ViewerConfigService.cs` — `Resolve()` switch에 `"builtin:<name>"` 케이스 등록
- [ ] `SettingsWindow.xaml` — 뷰어 탭 `NewViewerCombo`에 ComboBoxItem 추가
- [ ] `Viewers/Resources/` — HTML/JS/CSS 리소스 추가 시 `.csproj` `<Content>` 항목도 추가
- [ ] `docs/architecture.md` — 뷰어 목록 업데이트

### 새 서비스/컨트롤 추가 시
- [ ] `docs/architecture.md` — 서비스·컨트롤 목록 업데이트
- [ ] `README.md` — 아키텍처 요약 업데이트

### 플러그인 계약 변경 시
- [ ] `Folderss.PluginContract/PluginContracts.cs` — 기존 멤버 삭제·시그니처 변경은 이미 배포된 플러그인을 깨뜨린다. 추가 위주로 하고, 깨지는 변경이면 계약 `<Version>` 주 버전을 올린다
- [ ] `Services/PluginManager.cs` — `PluginHost`(IPluginManager 구현) 갱신
- [ ] `samples/HelloPlugin` — 예제가 계속 빌드되는지 확인 (`dotnet build samples/HelloPlugin`)
- [ ] 플러그인에 보여 줄 본체 설정 키를 추가·변경하면 `Services/PluginAppSettings.cs`, 계약 `GetAppSettings` 주석, `PluginSessionTests.AppSettings_ExposesDocumentedKeysOnly`를 함께 고친다 (키 이름 변경은 깨지는 변경)
- [ ] `README.md` 플러그인 섹션, `docs/architecture.md` PluginManager 절

### 태그/릴리스 생성 시
- [ ] `Properties/AssemblyInfo.cs` — `AssemblyVersion`, `AssemblyFileVersion`을 태그 버전에 맞게 갱신
- [ ] 정보 창(`AboutWindow`)이 실제 어셈블리 버전을 표시하는지 확인

---

## 주요 패턴

### 테마 XAML 구조
모든 테마 파일은 아래 키를 동일하게 정의해야 함 (`Black.xaml` 참고):
```
WindowBackground, PanelBackground, SurfaceBackground, ControlBackground
ControlHoverBrush, ControlPressedBrush, BorderBrush
PrimaryText, SecondaryText, DisabledTextBrush
AccentBrush, AccentHoverBrush, SelectionBrush, RowHoverBrush
```
SystemColors 오버라이드 4쌍도 반드시 포함.

### AvalonDock 스타일 주의사항
- `Controls.xaml`에서 `{StaticResource}` 참조 시 선언 순서가 중요함.
  앞에 선언된 스타일에서 뒤에 선언된 리소스를 참조할 때는 `{DynamicResource}` 사용.
- `LayoutDocumentTabItem`은 AvalonDock 기본 템플릿이 색상을 하드코딩하므로
  `Style.Resources`가 아닌 완전한 `ControlTemplate`으로만 재정의 가능.
- `+ 새 패널`(`add-folder-panel`) 탭은 폴더 컴포넌트 추가용 고정 탭이므로 항상 같은 문서 탭 영역의 오른쪽 끝에 있어야 함.
  파일/Markdown 링크 클릭으로 뷰어 탭을 추가할 때도 새 탭은 `+ 새 패널` 앞에 삽입하고, 추가 후 `+ 새 패널`을 다시 끝으로 정렬해야 함.
- 닫기 가능한 새 문서 탭(`LayoutDocument`)을 만들면 생성 직후 `ApplyPanelLockState(document)`를 호출해
  저장된 패널 잠금(`PanelLockService`)을 반영해야 한다. 탭 제목을 갱신할 때는 `SetDocumentTitle()`을 써야
  잠금 표시(`🔒 `)가 유지된다. `LayoutContent.Close()`는 `CanClose`를 검사하지 않으므로,
  코드에서 탭을 직접 닫는 경로는 `CanClose`를 먼저 확인해야 잠금이 우회되지 않는다.
- `MainWindow.xaml`의 앵커러블/문서 콘텐츠 구조를 바꾸면(예: 패널을 컨테이너로 감싸기)
  `ResolveDockContent()`, `BuildDefaultDockLayout()`, `CreateFavoritesDock()` 등 코드에서 도킹 콘텐츠를
  할당하는 곳도 반드시 같은 최상위 요소를 사용하도록 갱신해야 함. 불일치 시 자식 요소가 이미 다른
  논리 부모를 가진 상태로 도킹에 붙어 시작 시 `InvalidOperationException` 크래시 발생.

### 선택지가 여러 개인 기능은 대화상자로
- 한 기능에 여러 방식·옵션이 있으면(예: reset soft/mixed/hard, 브랜치 시작점·전환 여부, 체크아웃 방식) 임의로 하나를 고정하지 말고
  **선택 대화상자**로 고르게 한다. 기본값은 가장 안전한 선택으로 두고, 되돌릴 수 없는 선택(데이터 삭제 등)은 경고 문구와 확인 체크를 거치게 한다.
- 자주 쓰는 기능은 기본값을 설정 창에 두고 대화상자에서 그 값으로 시작한다(매번 같은 선택을 반복하지 않게).
- **버튼은 기본 옵션으로 바로 실행하고, 옵션 대화상자는 버튼 옆 `▾`(GitWindow의 `OptionArrowButton` 스타일)를 누를 때만 연다.**
  매번 대화상자를 띄우지 않는다. 처음부터 고를 것이 본질인 기능(reset 모드 등)만 버튼이 곧바로 대화상자를 연다.
- 강제 옵션(`-D`, `--force` 등)은 ▾ 대화상자에서 기본 꺼짐 + 경고 + 확인 체크(`GitForceConfirmDialog`). 원격의 남의 작업을 덮을 수 있는 강제 푸시는 두지 않는다.
- Git 대화상자는 `GitDialogs.cs`의 `GitDialogBase`(테마 색, 버튼, 닫기 전 비동기 검사 `ValidateAsync`)를 상속해 만든다.

### ContextMenu 스타일
WPF 기본 `ContextMenu`는 `SystemDropShadowChrome`으로 테두리가 두껍게 보임.
`Controls.xaml`에 `ControlTemplate` 재정의가 있으므로 새 ContextMenu 추가 시 별도 스타일 불필요.

---

## 문서 구조

```
docs/
├── architecture.md   — 파일 구조, 서비스 역할, 확장 포인트 상세
├── todo/TODO.md      — 미완료 개발 요청 목록
└── done/DONE.md      — 완료된 작업 아카이브 (버전별)
```

### 개발 요청 처리 워크플로

1. **요청 접수 시** → `docs/todo/TODO.md`에 항목 추가
   ```markdown
   - [ ] <작업 제목> — <간략한 설명>
   ```

2. **작업 완료 시** → `docs/todo/TODO.md`에서 `[x]`로 체크 후 `docs/done/DONE.md`로 이동
   - DONE.md는 버전 섹션(`## vX.Y.Z`) 아래에 기술 형식으로 기록
   - 주요 결정이나 주의사항이 있으면 한두 줄 추가

3. **작업 중 놓친 연관 파일이 있었다면** → `CLAUDE.md` 체크리스트 해당 항목에 추가

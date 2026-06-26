# Folderss — Claude 개발 가이드

## 프로젝트 개요

Windows WPF 듀얼 패널 파일 관리자. .NET Framework 4.8, AvalonDock 4.74.

**빌드**
```powershell
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' .\Folderss.sln /p:Configuration=Debug /v:minimal
```

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
- [ ] `SettingsWindow.xaml.cs` — 초기화 및 저장 로직 추가
- [ ] 설정 저장 서비스(해당 서비스) 업데이트

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

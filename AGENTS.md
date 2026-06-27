# Folderss — Agent 개발 가이드

## Codebase Knowledge Graph

이 프로젝트는 코드 탐색에 codebase-memory-mcp 지식 그래프를 우선 사용한다.

1. `search_graph` — 함수, 클래스, 라우트, 변수 검색
2. `trace_path` — 호출 및 피호출 경로 추적
3. `get_code_snippet` — 특정 함수나 클래스 소스 확인
4. `query_graph` — 복합 패턴 Cypher 조회
5. `get_architecture` — 프로젝트 구조 요약

문자열 리터럴, 오류 메시지, 설정값, 비코드 파일을 찾거나 그래프 결과가 충분하지 않을 때만 `rg` 등 파일 검색을 사용한다.

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
- [GitHub Project #1](https://github.com/users/zaruous/projects/1) — 현재 아이템과 상태의 단일 기준
- `docs/PROJECT.md` — Project 연결 및 상태 관리 규칙

### 작업 후 반드시 수행
1. 빌드 성공 확인 (`Exit: 0`)
2. 아래 체크리스트 해당 항목 통과
3. GitHub Project item 본문과 상태를 정확히 반영
4. README.md 관련 섹션 업데이트 (기능 추가/변경 시)

### 작업 상태 및 기록 원칙

- [Folderss Project #1](https://github.com/users/zaruous/projects/1)을 아이템 상태의 단일 기준(source of truth)으로 사용한다.
- 요청 접수 시 `Todo`, 개발 착수 시 `In Progress`, 개발과 자체 검증 완료 후 `Ready for Verification`, 사용자 최종 확인 후에만 `Done`으로 관리한다.
- 코드 작성과 빌드 성공은 **개발 완료**이지 **최종 완료**가 아니다. 사용자의 명시적인 확인 전에는 절대로 `Done`으로 변경하지 않는다.
- 요구사항, 원인 분석 또는 설계, 구현 내용, 변경 파일, 검증, 변경 이력은 Project item 본문에 직접 기록한다. 로컬 상세 문서 링크로 대체하지 않는다.
- 상태 전환 시 Project의 `Status` 필드만 변경한다.
- 사용자의 추가 수정 요청이 있으면 같은 Project item 본문에 요구사항, 구현 및 검증 이력을 보완한다.
- 사용자가 "완료", "확인", "문제없음" 등으로 최종 결과를 승인한 경우에만 Project 상태를 `Done`으로 변경한다.
- 작업 상태, 사용자 확인 여부와 상태 전환일은 GitHub Project에서만 관리한다. 로컬 상태 인덱스를 만들거나 병행 관리하지 않는다.
- 요구사항, 원인 분석, 구현 내용, 변경 파일, 검증 방법과 결과는 Project item 본문에서 관리한다.
- 여러 버그나 기능이 독립적으로 확인될 수 있으면 각각 별도 항목과 상세 문서로 분리한다.
- 사용자가 별도로 문서화를 요청하지 않아도 이 상태 관리와 상세 기록을 생략하지 않는다.
- GitHub 인증이나 네트워크 문제로 Project 조회·갱신이 실패하면 사용자에게 동기화 실패를 명시하고 `docs/items/`에서 임시 관리한다.
- 연결 실패 중에는 항목별 `docs/items/<항목>.md` 파일을 만들고 `제목`, `내용`, `상태`를 반드시 기록한다.
- 임시 파일의 `상태`는 `Todo`, `In Progress`, `Ready for Verification`, `Done` 중 하나만 사용한다.
- GitHub 연결이 복구되면 임시 항목을 Project에 생성 또는 병합하고 상태와 내용을 동기화한 뒤 해당 `docs/items/` 파일을 제거한다.
- GitHub Project와 `docs/items/`를 동시에 기준으로 사용하지 않는다. 정상 연결 시에는 Project가 유일한 기준이고 `docs/items/`는 연결 장애 시에만 사용하는 임시 저장소다.

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
├── PROJECT.md        — GitHub Project 연결 및 운용 규칙
├── items/            — GitHub 연결 장애 시에만 사용하는 임시 항목 저장소
└── done/DONE.md      — 기존 릴리스 완료 이력(아이템 상태 관리에 사용하지 않음)
```

### 개발 요청 처리 워크플로(사용자가 요청시에만 사용할것)

1. **요청 접수 / 미착수**
   - Project #1에 draft item을 생성하고 `Status=Todo`로 지정한다.
   - Project item 본문에 상세 문서 필수 항목을 직접 작성한다.

2. **작업 착수 / 개발 및 자체 검증**
   - Project item의 `Status`를 `In Progress`로 변경한다.
   - Project item 본문의 구현 내용과 변경 이력을 갱신한다.

3. **개발 및 자체 검증 완료**
   - 빌드·테스트가 끝나면 Project item의 `Status`를 `Ready for Verification`으로 변경한다.
   - 코드 작성과 빌드 성공만으로 DONE 처리하지 않는다.

4. **사용자 최종 확인**
   - 사용자가 결과를 명시적으로 승인한 경우에만 Project item의 `Status`를 `Done`으로 변경한다.

5. **Project item 본문 필수 항목**
   ```markdown
   # <작업 제목>
   ## 요구사항
   ## 원인 분석 또는 설계
   ## 구현 내용
   ## 변경 파일
   ## 검증
   ## 변경 이력
   ```

6. **GitHub 연결 실패 시 임시 항목 형식**
   ```markdown
   # <제목>

   - 상태: Todo | In Progress | Ready for Verification | Done

   ## 내용

   <요구사항, 구현 및 검증 내용>
   ```
   - 연결 복구 후 Project에 동기화하고 임시 파일을 제거한다.

7. **작업 중 놓친 연관 파일이 있었다면** → `AGENTS.md` 체크리스트 해당 항목에 추가

# DONE

완료된 항목은 `docs/todo/TODO.md`에서 체크한 뒤 이 파일로 옮깁니다.

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

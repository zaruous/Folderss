# Folderss 아키텍처 참조

## 디렉터리 구조

```
Folderss/
├── Controls/
│   ├── FolderBrowser.xaml/.cs      — 핵심 파일 브라우저 컨트롤 (패널 재사용 단위)
│   └── FavoritesPanel.xaml/.cs     — 즐겨찾기 패널
├── Models/
│   ├── FileSystemItem.cs           — 파일·폴더 뷰모델
│   └── FavoriteLocation.cs         — 즐겨찾기 그룹·항목 모델
├── Services/
│   ├── FileOperationService.cs     — 복사·이동·삭제·이름변경·새 폴더
│   ├── FilePreviewService.cs       — 텍스트·이미지 미리보기 + 메타데이터
│   ├── DockLayoutService.cs        — AvalonDock 레이아웃 저장·복원
│   ├── SessionStateService.cs      — 열린 폴더 경로 세션 저장·복원
│   ├── FavoritesService.cs         — 즐겨찾기 목록 저장·복원
│   ├── KeybindingManager.cs        — 단축키 매핑 및 커스터마이징
│   ├── ShellContextMenuService.cs  — Windows 쉘 우클릭 컨텍스트 메뉴
│   └── ThemeManager.cs             — 테마 전환 및 저장
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
├── SettingsWindow.xaml/.cs         — 설정 창 (테마 탭 + 단축키 탭)
├── KeyCaptureWindow.cs             — 단축키 입력 캡처 팝업
├── AboutWindow.cs                  — 정보 창
├── PromptWindow.cs                 — 이름 변경·새 폴더 입력 다이얼로그
└── App.xaml/.cs                    — 앱 진입점, 테마 초기 로드
```

---

## 서비스 역할 상세

### ThemeManager
- `AppTheme` enum으로 지원 테마 정의
- `Themes/<이름>.xaml` 파일을 `ResourceDictionary`로 교체하는 방식으로 런타임 전환
- `%LOCALAPPDATA%\Folderss\theme.txt`에 마지막 테마 저장
- **테마 추가 시 연관 파일**: ThemeManager.cs, MainWindow.xaml+cs, SettingsWindow.xaml+cs

### KeybindingManager
- 기본 단축키 매핑을 코드에서 정의
- 사용자 커스터마이징을 JSON으로 `%LOCALAPPDATA%\Folderss\keybindings.json`에 저장
- `kb.Matches(e, "CommandId")` 패턴으로 MainWindow KeyDown에서 사용
- **단축키 추가 시 연관 파일**: KeybindingManager.cs, MainWindow.xaml.cs, SettingsWindow

### DockLayoutService / SessionStateService
- 앱 종료 시 자동 저장, 다음 실행 시 자동 복원
- 저장 위치: `%LOCALAPPDATA%\Folderss\dock-layout.xml`, `session.xml`

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

# 개발 작업 이력

최신 작업이 위에 오도록 추가합니다.

---

## 2026-06-23 | 설정 창 테마 탭에 5개 신규 테마 등록

**요청**: 신규 테마 5개를 설정 창 테마 목록에도 표시
**변경 파일**: `SettingsWindow.xaml`, `SettingsWindow.xaml.cs`
**내용**:
- Nord, Catppuccin, Solarized Dark, Dracula, GitHub RadioButton 추가
- 각 항목에 테마 대표색 스워치(28×28 Border) 표시
- 생성자 초기화 블록에 `IsChecked` 바인딩 추가
**주의사항**: 테마 추가 시 SettingsWindow도 함께 수정해야 함. CLAUDE.md 체크리스트 참조.

---

## 2026-06-23 | 테마 5개 추가 (Nord, Catppuccin, Solarized, Dracula, GitHub)

**요청**: 인기 테마 5개 추가
**변경 파일**: `Themes/Nord.xaml`, `Themes/Catppuccin.xaml`, `Themes/Solarized.xaml`,
`Themes/Dracula.xaml`, `Themes/GitHub.xaml`, `Services/ThemeManager.cs`,
`MainWindow.xaml`, `MainWindow.xaml.cs`
**내용**:
- 각 테마 색상 팔레트 XAML 생성 (Black.xaml 구조 동일)
- AppTheme enum에 5개 값 추가
- MainWindow 컨텍스트 메뉴에 구분선 + 5개 MenuItem 추가
- 클릭 핸들러 5개 + UpdateThemeMenuChecks() 업데이트
**누락 사항**: SettingsWindow를 별도 요청으로 후속 처리 → 이후 체크리스트에 반영

---

## 2026-06-23 | 블랙 테마 UI 버그 2건 수정

**요청**: 1) 블랙 테마 메뉴 테두리 두꺼움 2) 폴더패널 탭 X 버튼이 검정색으로 안 보임
**변경 파일**: `Themes/Controls.xaml`
**주요 결정**:
- ContextMenu: WPF 기본 드롭섀도 → ControlTemplate 재정의로 1px 테두리만 남김
- LayoutDocumentTabItem 닫기 버튼: AvalonDock 기본 템플릿이 색상 하드코딩
  → `Style.Resources` 방식 제거, 완전한 ControlTemplate으로 교체
  → `DockPaneCloseGlyphStyle`을 `{DynamicResource}`로 참조 (StaticResource 순서 문제 해결)
**주의사항**: Controls.xaml에서 StaticResource 참조 시 선언 순서 반드시 확인

---

## 2026-06-23 | master 브랜치 머지 (claude/folder-component-f5-key-mto91f)

**요청**: 현재 브랜치를 master에 머지
**변경 파일**: `MainWindow.xaml.cs` (충돌 해결)
**충돌 해결**:
- master: `Ctrl+N` NewFile 하드코딩 + `Ctrl+T` AddPanel 하드코딩
- 브랜치: AddPanel을 `kb.Matches(e, "AddPanel")`로 교체
- 결과: Ctrl+N NewFile 하드코딩 유지 + kb.Matches AddPanel 채택
  (NewFile은 kb 시스템에 미등록 상태이므로 하드코딩 유지)

---

## 2026-06-22 | UX 개선 (Settings, KeyCapture 화면)

**커밋**: b2e76fd
**변경 파일**: `SettingsWindow.xaml`, `KeyCaptureWindow.cs`
**내용**: 설정 창 및 단축키 캡처 화면 UX 개선 (코드 리뷰 반영)

---

## 2026-06-22 | 설정 창 테마 탭 추가

**커밋**: 76bf8c8
**변경 파일**: `SettingsWindow.xaml`, `SettingsWindow.xaml.cs`
**내용**: 설정 창에 테마 전환 탭 추가. 블랙/라이트 RadioButton, 즉시 적용 및 취소 시 원복.

---

## 2026-06-22 | 설정 메뉴 및 단축키 맵핑 기능 구현

**커밋**: 1187cf1
**변경 파일**: `Services/KeybindingManager.cs`, `SettingsWindow.xaml/.cs`, `KeyCaptureWindow.cs`
**내용**: 단축키 커스터마이징 설정 창 구현. KeybindingManager 서비스 도입.

---

## 2026-06-22 | F5 키 동작 변경 (복사 → 새로고침)

**커밋**: e402130
**변경 파일**: `MainWindow.xaml.cs`
**내용**: F5 키 동작을 반대편 패널로 복사에서 양쪽 패널 새로고침으로 변경

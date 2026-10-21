# 테마 추가: 핑크·오렌지 + 기타 (Sakura, Sunset, Tokyo Night, One Dark, Monokai, Gruvbox)

- 상태: Ready for Verification

## 요구사항

`Themes/`에 핑크·오렌지 계열 스킨을 추가하고, 그 밖에도 여러 스킨을 추가한다.

## 원인 분석 또는 설계

- 요청에 밝기·이름이 정해지지 않아 다음과 같이 정했다 (바꿀 수 있음).
  - 핑크 → **Sakura**: 밝은 테마(파스텔 핑크 바탕, 진한 핑크 강조색)
  - 오렌지 → **Sunset**: 어두운 테마(따뜻한 갈색 바탕, 오렌지 강조색)
  - 기타 → 널리 쓰이는 편집기 팔레트 **Tokyo Night, One Dark, Monokai, Gruvbox Dark** (모두 어두운 테마)
- 테마 XAML은 `Dracula.xaml`을 틀로 14개 색만 바꿨다. 폴더 비교 행 배경은 어두운 테마 알파 `4D`, 밝은 테마(Sakura) `33`(Light와 같음).
- `CLAUDE.md` 체크리스트에 없던 연관 파일: 마크다운·텍스트·Monaco 뷰어가 테마 이름으로 페이지 색과 밝음/어두움을 고른다.
  - 등록하지 않으면 마크다운·텍스트 뷰어는 `dark` 색으로 보이고, `text-app.html`은 `DARK_THEMES`에 없는 이름을 **밝은** 코드 하이라이트로 처리한다.
  - Monaco는 편집기 테마가 light/dark 두 가지뿐이라 Sakura만 `light`로 보내고 나머지는 기본 dark를 쓴다.
- 테마가 13개가 되어 설정 창(높이 540) 테마 탭이 넘치므로 라디오 목록을 `ScrollViewer`로 감쌌다.
- `Folderss.csproj`는 SDK 스타일이라 `Themes/*.xaml`이 자동으로 `Page`에 포함되어 따로 추가하지 않았다(기존 7개 테마도 csproj 항목 없음).

## 구현 내용

- 테마 팔레트 6개 추가, `AppTheme` enum 끝에 6개 값 추가(기존 값 순서 유지 → `theme.txt`는 이름으로 저장하므로 호환).
- 메인 메뉴 `테마` 하위 메뉴 항목·클릭 핸들러·`UpdateThemeMenuChecks()` 추가.
- 설정 창 테마 탭 라디오(색 견본 포함)·초기화 추가, 목록 스크롤.
- 뷰어: `PageBackground()`·`ThemeName()` 케이스, `markdown-app.html`·`text-app.html`의 `body.<이름>` 색 토큰, `mermaidTheme()`·`LIGHT_THEMES`/`DARK_THEMES` 분류.
- 대비: 패널 바탕 위 기본/보조 글자, 선택 행 글자 모두 4.5:1 이상(One Dark 선택 색을 `#354055`로 조정). 비활성 글자는 기존 테마와 같은 수준(2.3~3.3).

## 변경 파일

- `Folderss/Themes/Sakura.xaml`, `Sunset.xaml`, `TokyoNight.xaml`, `OneDark.xaml`, `Monokai.xaml`, `Gruvbox.xaml` (신규)
- `Folderss/Services/ThemeManager.cs`
- `Folderss/MainWindow.xaml`, `MainWindow.xaml.cs`
- `Folderss/SettingsWindow.xaml`, `SettingsWindow.xaml.cs`
- `Folderss/Viewers/MarkdownViewer.xaml.cs`, `TextViewer.xaml.cs`, `MonacoViewer.xaml.cs`
- `Folderss/Viewers/Resources/markdown-app.html`, `markdown-app.js`, `text-app.html`
- `README.md`, `docs/architecture.md`, `docs/plugin-development.md`, `CLAUDE.md`, `AGENTS.md`(테마 추가 체크리스트에 뷰어·플러그인 문서 항목 추가)

## 검증

- Linux 컨테이너에서 `dotnet build Folderss/Folderss.csproj -p:EnableWindowsTargeting=true`: 오류는 `ConsolePanel.xaml.cs`의
  `Microsoft.Terminal` 참조 1건뿐이고, 변경 전 코드에서도 같은 오류가 난다(Windows 전용 패키지). 그 외 컴파일 오류 없음.
  **Windows에서 `dotnet build .\Folderss.sln -c Debug` 확인 필요.**
- 새 테마 6개 모두 `Black.xaml`과 정의 키 집합이 같은지 스크립트로 비교 — 일치.
- 앱에서 확인 필요: 각 테마 전환(메뉴·설정 창), 설정 창 테마 탭 스크롤, 취소 시 원래 테마 복귀, 재시작 후 유지,
  마크다운·텍스트·코드 뷰어 색(Sakura는 밝은 하이라이트·Mermaid), 폴더 비교 창 행 색.

## 변경 이력

- 2026-10-21: 6개 테마 추가, 뷰어 테마 매핑·설정 창 스크롤, 문서·체크리스트 갱신.

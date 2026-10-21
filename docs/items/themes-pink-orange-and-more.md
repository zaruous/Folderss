# 테마 추가: 핑크·오렌지 + 기타 (Sakura, Sunset, Tokyo Night, One Dark, Monokai, Gruvbox) + Themes 폴더 자동 인식

- 상태: Ready for Verification

## 요구사항

`Themes/`에 핑크·오렌지 계열 스킨을 추가하고, 그 밖에도 여러 스킨을 추가한다.

2차 요청: 테마 파일만 추가해도 목록에 나오도록 `Themes/` 폴더 자동 인식으로 개선.

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

### 2차: Themes 폴더 자동 인식

- 기존에는 테마 이름이 `AppTheme` enum, 메뉴·설정 창 XAML, 뷰어 C# switch, 뷰어 HTML CSS 클래스·JS 목록에 각각 하드코딩되어
  테마 하나에 10개 넘는 파일을 고쳐야 했다.
- 범위 결정: **빌드 시 포함되는 `Folderss/Themes/` 폴더**를 인식한다(파일 추가 → 다시 빌드). 실행 폴더에 XAML을 넣는 런타임 로드는
  깨진 XAML로 앱이 죽을 수 있어 검증 코드가 더 필요하므로 하지 않았다.
- 실행 시 테마 파일 이름은 어셈블리 리소스 `Folderss.g.resources`의 `themes/*.baml` 키로 얻는데 소문자라서,
  저장·플러그인 값의 대소문자(`Black`, `GitHub` 등)를 지키려고 테마 XAML에 `ThemeName`을 둔다(없으면 소문자 파일 이름).
- 밝은/어두운 구분은 키로 두지 않고 `PanelBackgroundColor` 밝기로 계산한다.
- 웹 뷰어는 테마별 CSS 클래스 대신 C#이 테마 XAML 색으로 만든 `{ light, colors }`를 받아 `body.themed` CSS 변수로 쓴다.
  내보낸 HTML은 문서 스타일시트를 복사하므로 이 규칙도 함께 들어간다.

## 구현 내용

- 테마 팔레트 6개 추가, `AppTheme` enum 끝에 6개 값 추가(기존 값 순서 유지 → `theme.txt`는 이름으로 저장하므로 호환).
- 메인 메뉴 `테마` 하위 메뉴 항목·클릭 핸들러·`UpdateThemeMenuChecks()` 추가.
- 설정 창 테마 탭 라디오(색 견본 포함)·초기화 추가, 목록 스크롤.
- 뷰어: `PageBackground()`·`ThemeName()` 케이스, `markdown-app.html`·`text-app.html`의 `body.<이름>` 색 토큰, `mermaidTheme()`·`LIGHT_THEMES`/`DARK_THEMES` 분류.
- 대비: 패널 바탕 위 기본/보조 글자, 선택 행 글자 모두 4.5:1 이상(One Dark 선택 색을 `#354055`로 조정). 비활성 글자는 기존 테마와 같은 수준(2.3~3.3).

### 2차 구현

- `ThemeManager`: `AppTheme` enum 삭제 → `ThemeInfo`(Name, DisplayName, Order, IsLight, Resources, GetColor). `Themes`가 어셈블리 리소스에서
  테마를 찾고(Controls 제외), 읽기 실패·필수 색 누락 파일은 건너뜀. theme.txt는 이름으로 저장·대소문자 무시로 읽음(기존 파일 호환).
- 테마 XAML 13개 맨 위에 `ThemeName`·`ThemeDisplayName`·`ThemeOrder` 추가(기존 메뉴 이름·순서 유지).
- `MainWindow`: 테마 메뉴 항목·클릭 핸들러 13개 삭제 → `BuildThemeMenu()`가 생성. 기존 `블랙/라이트` 아래 구분선은 없어짐.
- `SettingsWindow`: 라디오 13개 삭제 → `BuildThemeList()`가 생성(색 견본 = 테마의 `WindowBackgroundColor`).
- 뷰어: `IFileViewer.ApplyTheme(ThemeInfo)`. 새 `Viewers/ViewerTheme.cs`가 페이지 색 JSON·WebView 바탕색을 만듦.
  `markdown-app.html`/`text-app.html`의 테마별 CSS 클래스와 `DARK_THEMES`/`LIGHT_THEMES` 삭제, `applyTheme()`가 `<style id="folderss-theme">`를 채움.
  Monaco는 `IsLight`로 light/dark만 보냄.
- `PluginManager`: 플러그인 `theme` 값 = `CurrentTheme.Name`(기존 값과 같음).
- 테스트 `ThemeFileTests`: 모든 테마가 `Black.xaml`과 같은 키를 갖는지, 이름이 겹치지 않는지.
- 뷰어 색 매핑(테마 XAML → 페이지): bg=PanelBackground, panel=SurfaceBackground, border=Border, fg=PrimaryText, fg2=SecondaryText,
  accent=Accent, hover=ControlHover, sel=Selection, line-num=DisabledText, scroll=Border.
  **기존 테마의 뷰어 색이 일부 바뀜**: 바탕색은 Black 마크다운(#1e1e1e → #0e1013, 앱·Monaco와 같아짐) 외에는 동일하지만,
  툴바·강조색·보조 글자 등은 이제 앱 테마 색을 따른다(예: Dracula 마크다운 강조색 보라 #bd93f9 → 앱 강조색 핑크 #ff79c6).

## 변경 파일

- `Folderss/Themes/Sakura.xaml`, `Sunset.xaml`, `TokyoNight.xaml`, `OneDark.xaml`, `Monokai.xaml`, `Gruvbox.xaml` (신규)
- `Folderss/Services/ThemeManager.cs`
- `Folderss/MainWindow.xaml`, `MainWindow.xaml.cs`
- `Folderss/SettingsWindow.xaml`, `SettingsWindow.xaml.cs`
- `Folderss/Viewers/MarkdownViewer.xaml.cs`, `TextViewer.xaml.cs`, `MonacoViewer.xaml.cs`
- `Folderss/Viewers/Resources/markdown-app.html`, `markdown-app.js`, `text-app.html`
- `README.md`, `docs/architecture.md`, `docs/plugin-development.md`, `CLAUDE.md`, `AGENTS.md`(테마 추가 체크리스트에 뷰어·플러그인 문서 항목 추가)

- 2차: `Folderss/Services/ThemeManager.cs`, `Folderss/Themes/*.xaml`(13개), `MainWindow.xaml(.cs)`, `SettingsWindow.xaml(.cs)`,
  `Folderss/Viewers/ViewerTheme.cs`(신규), `IFileViewer.cs`, `MarkdownViewer/TextViewer/MonacoViewer.xaml.cs`, `Controls/ViewerHost.xaml.cs`,
  `Viewers/Resources/markdown-app.html`·`markdown-app.js`·`text-app.html`, `Services/PluginManager.cs`,
  `tests/Folderss.SearchTests/ThemeFileTests.cs`(신규)·`.csproj`, `CLAUDE.md`, `AGENTS.md`, `README.md`, `docs/architecture.md`, `docs/plugin-development.md`

## 검증

- Linux 컨테이너에서 `dotnet build Folderss/Folderss.csproj -p:EnableWindowsTargeting=true`: 오류는 `ConsolePanel.xaml.cs`의
  `Microsoft.Terminal` 참조 1건뿐이고, 변경 전 코드에서도 같은 오류가 난다(Windows 전용 패키지). 그 외 컴파일 오류 없음.
  **Windows에서 `dotnet build .\Folderss.sln -c Debug` 확인 필요.**
- 새 테마 6개 모두 `Black.xaml`과 정의 키 집합이 같은지 스크립트로 비교 — 일치.
- 앱에서 확인 필요: 각 테마 전환(메뉴·설정 창), 설정 창 테마 탭 스크롤, 취소 시 원래 테마 복귀, 재시작 후 유지,
  마크다운·텍스트·코드 뷰어 색(Sakura는 밝은 하이라이트·Mermaid), 폴더 비교 창 행 색.

### 2차 검증

- Linux 빌드: 원래 나던 `ConsolePanel` 오류 한 줄만 임시로 막은 복사본에서 C# 컴파일 성공(이후 Windows 전용 conpty.dll 복사 단계에서만 실패).
  원본은 기존과 같은 `ConsolePanel` 오류 1건뿐. **Windows 빌드 확인 필요.**
- `dotnet test tests/Folderss.SearchTests`: 391 통과, 3 건너뜀, 실패 0. `ThemeFileTests`는 키 하나를 뺀 임시 테마 파일로 실패하는 것도 확인 후 삭제.
- `node --check`로 `markdown-app.js`, `text-app.html` 스크립트 문법 확인.
- 앱에서 확인 필요: 메뉴·설정 창에 13개 테마가 순서대로 나오는지, 전환·설정 창 취소·재시작 후 유지(기존 theme.txt 포함),
  마크다운·텍스트·Monaco 뷰어 색과 Sakura 등 밝은 테마의 코드 하이라이트·Mermaid, 마크다운 HTML·PDF 내보내기 색.

## 변경 이력

- 2026-10-21: 6개 테마 추가, 뷰어 테마 매핑·설정 창 스크롤, 문서·체크리스트 갱신.
- 2026-10-21: Themes 폴더 자동 인식으로 개선(enum·하드코딩 목록 제거, 뷰어 색을 테마 XAML에서 생성, 테마 파일 테스트 추가).

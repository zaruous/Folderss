# 테마 추가: 핑크·오렌지 + 기타 (Sakura, Sunset, Peach, Tokyo Night, One Dark, Monokai, Gruvbox) + Themes 폴더 자동 인식(시작 시 로딩)

- 상태: Done

## 요구사항

`Themes/`에 핑크·오렌지 계열 스킨을 추가하고, 그 밖에도 여러 스킨을 추가한다.

2차 요청: 테마 파일만 추가해도 목록에 나오도록 `Themes/` 폴더 자동 인식으로 개선.

3차 요청: 빌드 없이 프로그램을 시작할 때 테마를 읽도록(실행 폴더 `Themes\`).

4차 요청: 추가 스킨 색감과 마크다운 패널 배경 개선, AS-IS/TO-BE 렌더링 비교로 확인.

5차 요청: 오렌지 테마(Sunset)에 오렌지 색이 많이 부족하다. → 후보 비교 후 사용자 선택: **Sunset은 어두운 오렌지(A안)로 교체 + 밝은 오렌지(B안)는 새 테마 Peach로 추가**.

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

### 3차: 시작 시 실행 폴더 Themes\ 로딩

- "프로그램 가동 시 로딩"을 **실행 폴더 `Themes\*.xaml`을 시작할 때 읽기(빌드 불필요)**로 해석했다. 기본 테마는 그대로 빌드에 포함해,
  외부 폴더가 없거나 파일이 모두 깨져도 앱이 테마 없이 시작되지 않게 했다.
- 위험: 외부 XAML은 임의 객체를 만들 수 있다(예: `ObjectDataProvider`로 `Process.Start`). 실행 폴더라 exe와 같은 신뢰 수준이지만
  "테마 파일"로 받아 넣기 쉬우므로 **허용 목록 검사**(`ThemeFileValidator`)를 거친 파일만 `XamlReader.Parse`로 읽는다.
  허용: 최상위 ResourceDictionary 바로 아래의 Color·SolidColorBrush·FontFamily·sys:String·sys:Int32, 속성은 x:Key와 일반 속성,
  태그 확장은 `{StaticResource 이름}`·`{x:Static SystemColors.이름}`만. DTD 금지.
- 키 검사: 기본 테마 Black의 키 전체(테마 정보 키 제외)를 기준으로, 빠진 키나 Color가 아닌 색 키가 있으면 뺀다.
- 실패한 외부 파일은 조용히 빠지면 원인을 알 수 없으므로 `LoadErrors`에 모아 메인 창이 시작 후 한 번 경고 창으로 알린다.
- 같은 `ThemeName`이면 외부 테마가 기본 테마를 대신한다(기본 테마를 고쳐 쓰는 용도). 외부끼리 같은 이름이면 뒤 파일을 오류로 뺀다.
- 실행 중 파일을 바꾸면 다시 시작해야 반영된다(감시하지 않음).

### 4차: 색감·마크다운 패널 개선

- 실제 `markdown-app.html`을 Chromium에서 테마 색으로 렌더링해 비교(왼쪽 앱 영역은 테마 색으로 흉내 낸 HTML).
- 발견: 마크다운 코드 블록에 구문 색 없음(hljs 테마 CSS 미로드, 기존 문제), 목차/본문 사이 4px 테두리 색 막대, 목차 바탕이 본문과 다름,
  Sakura 전체 분홍으로 위계 없음, Sunset 탁한 갈색, Monokai 형광 연두, Gruvbox 노란 강조색이 튐.

### 5차: 오렌지 부족

- 원인: 4차에서 탁한 갈색을 피하려고 바탕·컨트롤·테두리·보조 글자를 거의 무채색 차콜(`#1E1B1A` 등)로 바꿔, 오렌지가 강조색(포커스 테두리·일부 아이콘) 한 곳에만 남았다.
  선택 행 `#4A3020`도 어두운 갈색이라 오렌지로 읽히지 않았다.
- 테마 키는 14색으로 고정(외부 테마 검사·`ThemeFileTests`)이라 새 키 없이 색만 바꿨다.
- 실제 앱(Release 빌드 복사본, 격리 프로필, 실행 폴더 `Themes\`에 후보 넣기)을 찍어 현재/A/B를 비교했다.
  - A안(어두운 바탕 유지): 바탕은 채도를 올린 아주 어두운 오렌지(4차의 탁한 중간 갈색과 다름), 선택 행 `#8A3F10`·호버·테두리·상단 바·보조 글자(복숭아색 `#F0B48A`)로 오렌지를 넓힘.
  - B안(밝은 테마, Sakura와 같은 구조): 복숭아빛 창 바탕, 따뜻한 흰 패널, 오렌지 선택·호버, 강조색은 흰 바탕 대비 4.5:1을 넘도록 `#C2410C`.
- 사용자 선택: Sunset = A안, B안 = 새 테마 `Peach`(밝은 오렌지, 순서 95 — Sunset 바로 뒤). 폴더 비교 행 알파는 밝은 테마 규칙대로 `33`.

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

### 3차 구현

- `Services/ThemeFileValidator.cs`(신규, 순수 로직), `ThemeManager`: `ExternalThemesDirectory`, `LoadErrors`, `LoadExternalThemes`, Black 기준 키 검사(`RequiredColorKeys` 대체).
- `MainWindow`: `LoadErrors`가 있으면 시작 후(ApplicationIdle) `ReportThemeLoadErrors` 경고 창.
- 테스트: 기본 테마 13개가 외부 검사를 통과(복사해 고쳐 쓸 수 있음), 허용 내용 4종 통과, `ObjectDataProvider`·병합 사전·`x:Static`(SystemColors 외)·`Binding`·`x:Class`·DTD·다른 최상위 요소 거부.

### 4차 구현

- `markdown-app.html`/`.js`: hljs 테마 CSS 링크(밝기에 따라 `applyTheme`에서 전환), 목차 리사이즈·분할 핸들을 4px 잡는 영역 + 1px 선,
  목차 바탕 `var(--bg)`, 인쇄·PDF에서는 코드 색 끔(흰 바탕).
- 팔레트: Sakura(흰 읽기 바탕, 강조 #C2185B), Sunset(따뜻한 차콜, 선택 #4A3020), Monokai(강조 #FFD866), Gruvbox(강조 #8EC07C). Tokyo Night·One Dark는 유지.
- 대비: 4개 테마 본문·보조·선택 행·링크 모두 4.5:1 이상(최저 Sakura 링크 5.9).

### 5차 구현

- `Sunset.xaml` 14색 교체: 창 `#17100B`, 패널 `#1E1510`, 상단 바 `#2B1B10`, 컨트롤 `#352216`, 호버 `#4E2D16`, 누름 `#663819`, 테두리 `#5A3820`,
  글자 `#FFF1E6`/보조 `#F0B48A`/비활성 `#8F6B53`, 강조 `#FF7A1A`/`#FF9A4D`, 선택 `#8A3F10`, 행 호버 `#2E1C11`.
- `Peach.xaml` 신규(`ThemeName` Peach, 표시 이름 `Peach (밝은 오렌지)`, 순서 95): 창 `#FBE6D3`, 패널 `#FFFBF7`, 상단 바 `#FFF0E2`, 컨트롤 `#FFFFFF`,
  호버 `#FFE1C6`, 누름 `#FFCFA6`, 테두리 `#F0C9A6`, 글자 `#2A1608`/보조 `#7A4A2A`/비활성 `#B8957E`, 강조 `#C2410C`/`#9A3412`, 선택 `#FFCFA3`, 행 호버 `#FFF0E3`.
- 대비(패널 바탕 기준): Sunset 본문 16.2·보조 9.9·선택 행 6.8·강조 6.9, Peach 본문 16.8·보조 7.2·선택 행 12.1·강조 5.0 — 모두 4.5:1 이상.

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
- 5차: `Folderss/Themes/Sunset.xaml`, `Folderss/Themes/Peach.xaml`(신규), `README.md`, `docs/architecture.md`, `docs/plugin-development.md`

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

### 3차 검증

- `dotnet test tests/Folderss.SearchTests --filter ThemeFileTests`: 13 통과.
- Linux 빌드(ConsolePanel 한 줄 막은 복사본): C# 컴파일 성공.
- 앱에서 확인 필요: 실행 폴더 `Themes\`에 기본 테마를 복사·이름 변경해 넣고 시작 → 메뉴에 나오는지, 키를 지운 파일·`ObjectDataProvider`를 넣은 파일이
  경고 창에 이유와 함께 나오고 목록에서 빠지는지, `ThemeName`을 기본 테마와 같게 하면 대신하는지.

### 4차 검증

- `ThemeFileTests` 13 통과, `node --check markdown-app.js` 통과.
- AS-IS(master)/TO-BE 렌더링 이미지 6장으로 사용자 확인 요청. 앱에서 확인 필요: 코드 블록 색, 목차 구분선, PDF 내보내기 코드 가독성.

### 5차 검증

- `dotnet build .\Folderss.sln -c Release`: 오류 0. `dotnet test tests/Folderss.SearchTests --filter ThemeFileTests`: 13 통과(Peach 포함 — 테스트 출력 `Themes\`에 복사됨).
- Release 빌드 복사본을 격리 프로필로 실행해 Sunset·Peach 화면 촬영(파일 목록 선택 행 포함) — 후보 비교 때와 같은 색으로 나옴.
- 앱에서 확인 필요: 메뉴·설정 창에서 Peach가 Sunset 바로 뒤에 나오는지, Peach에서 마크다운·텍스트·Monaco 뷰어가 밝은 하이라이트로 바뀌는지, 폴더 비교 창 행 색.

## 변경 이력

- 2026-10-21: 6개 테마 추가, 뷰어 테마 매핑·설정 창 스크롤, 문서·체크리스트 갱신.
- 2026-10-21: Themes 폴더 자동 인식으로 개선(enum·하드코딩 목록 제거, 뷰어 색을 테마 XAML에서 생성, 테마 파일 테스트 추가).
- 2026-10-21: 시작 시 실행 폴더 Themes\ 로딩 추가(허용 목록 검사, 실패 파일 알림).
- 2026-10-22: 추가 스킨 4종 색감 조정, 마크다운 코드 구문 색·목차 구분선·목차 바탕 개선.
- 2026-10-22: Sunset 오렌지 강화(어두운 오렌지), 밝은 오렌지 테마 Peach 추가.
- 2026-10-22: 사용자 확인 완료 → Done (v1.8.9).

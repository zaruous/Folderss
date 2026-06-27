# Folderss

Windows용 듀얼 패널 파일 관리자입니다. WPF와 `net8.0-windows` 기반으로 구현했으며, AvalonDock을 활용한 자유로운 패널 도킹·탭·분리 창을 지원합니다.

현재 버전: **v1.4.5**

![Folderss 스크린샷](<docs/images/program image.png>)

## 개요

경로 탐색, 검색, 파일 목록은 재사용 가능한 `Controls/FolderBrowser` 컴포넌트 하나로 구성되며, 이 컴포넌트를 AvalonDock 레이아웃 위에 여러 개 배치합니다. 왼쪽에는 즐겨찾기 패널, 아래에는 선택 파일 미리보기·메타정보 영역이 붙어 있습니다. 파일을 열 때는 확장자 매핑에 따라 Markdown, Monaco, Text 뷰어를 별도 탭으로 띄우거나 Windows 기본 프로그램으로 넘깁니다.

### 아키텍처 요약

```
Folderss/
├── Controls/
│   ├── FolderBrowser       — 핵심 파일 브라우저 컨트롤 (패널 재사용 단위)
│   ├── FavoritesPanel      — 즐겨찾기 패널
│   ├── SearchPanel         — 파일 내용 검색 패널
│   ├── ConsolePanel        — ConPTY 기반 내장 터미널 패널
│   └── ViewerHost          — 내장 파일 뷰어 호스트
├── Viewers/
│   ├── MarkdownViewer      — Markdown 미리보기·편집·내보내기
│   ├── MonacoViewer        — 코드/텍스트 편집기
│   ├── TextViewer          — 읽기 전용 텍스트 뷰어
│   └── IFileViewer         — 뷰어 공통 인터페이스
├── Models/
│   ├── FileSystemItem      — 파일·폴더 뷰모델
│   ├── FavoriteLocation    — 즐겨찾기 그룹·항목 모델
│   ├── KeyBindingEntry     — 단축키 설정 모델
│   ├── OpenWithEntry       — 사용자 지정 열기 프로그램 모델
│   └── SearchResult        — 내용 검색 결과 모델
├── Services/
│   ├── FileOperationService    — 복사·이동·삭제·이름변경·새 폴더
│   ├── FilePreviewService      — 텍스트·이미지 미리보기 + 메타데이터
│   ├── DockLayoutService       — AvalonDock 레이아웃 저장·복원 (XML)
│   ├── SessionStateService     — 열린 폴더 경로 세션 저장·복원 (XML)
│   ├── FavoritesService        — 즐겨찾기 목록 저장·복원
│   ├── KeyBindingService       — 단축키 기본값·사용자 설정 저장
│   ├── SearchService           — 파일 내용 검색
│   ├── ViewerConfigService     — 확장자별 내장 뷰어 매핑
│   ├── OpenWithService         — 사용자 지정 Open With 항목 저장·실행
│   ├── ConsoleSettingsService  — 콘솔 패널 설정 저장
│   ├── ConsoleSessionService   — 콘솔 프로필/외부 터미널 실행 관리
│   ├── UpdateService           — GitHub 최신 릴리스 확인·다운로드
│   ├── ShellContextMenuService — Windows 쉘 우클릭 컨텍스트 메뉴
│   └── ThemeManager            — 테마 전환 및 저장
├── Themes/
│   ├── Black / Light / Nord / Catppuccin / Solarized / Dracula / GitHub
│   └── Controls.xaml           — 공통 컨트롤 스타일
├── MainWindow                  — 메인 창 및 AvalonDock 호스트
├── SettingsWindow              — 테마·단축키·뷰어·열기 프로그램·콘솔 설정
├── KeyCaptureWindow            — 단축키 입력 캡처 팝업
├── AboutWindow                 — 버전 정보 창
└── PromptWindow                — 이름 변경·새 폴더 입력 다이얼로그
```

### 설계 특징

- **`FolderBrowser` 재사용**: 좌·우 기본 패널과 추가 패널 모두 동일한 컨트롤 인스턴스를 사용합니다.
- **내장 뷰어 시스템**: `IFileViewer` 인터페이스와 `ViewerHost`를 통해 Markdown, Monaco, Text 뷰어를 AvalonDock 문서 탭으로 엽니다.
- **사용자 지정 Open With**: 파일·폴더 확장자 마스크에 맞는 외부 프로그램을 메인 메뉴와 Windows 컨텍스트 메뉴 상단에 노출합니다.
- **비동기 미리보기**: 파일 미리보기는 `Task.Run`으로 백그라운드에서 읽고, 요청 ID 비교로 레이스 컨디션을 방지합니다.
- **이동 이력**: 뒤로·앞으로 이동 이력을 패널별 스택으로 관리하며 최대 10개까지 유지합니다.
- **세션 복원**: 창을 트레이로 숨기기 직전과 실제 종료 직전에 열린 폴더 경로·활성 패널(`session.xml`)과 도킹 배치(`dock-layout.xml`)를 `%LOCALAPPDATA%\Folderss\`에 저장하고 다음 실행 시 복원합니다.
- **사용자 설정**: 테마(`theme.txt`), 단축키(`keybindings.xml`), 뷰어 매핑(`viewer-config.json`), 열기 프로그램(`open-with.xml`), 콘솔 설정(`console-settings.xml`)을 사용자별로 저장합니다.
- **테마**: `ResourceDictionary` 교체 방식으로 런타임에 즉시 전환합니다.

## 현재 기능

- 좌우 듀얼 패널과 크기 조절
- 경로 직접 입력
- 뒤로, 앞으로, 상위 폴더 이동
- 현재 폴더 이름 검색
- 파일 실행
- 파일 및 폴더의 Windows 컨텍스트 메뉴
- 확장자·폴더별 사용자 지정 `다음으로 열기` 프로그램
- 반대편 패널로 복사 및 이동
- Explorer 및 다른 패널과의 파일 드래그 앤 드롭
- 이름 변경, 새 폴더, 휴지통 삭제
- 다중 선택 파일 작업
- 클립보드 복사, 잘라내기, 붙여넣기
- Black, Light, Nord, Catppuccin, Solarized, Dracula, GitHub 테마 실시간 전환 및 사용자 설정 저장
- 설정 창에서 단축키와 확장자별 뷰어 매핑 변경
- 설정 창에서 사용자 지정 열기 프로그램 등록 및 확장자 마스크 관리
- 설정 창에서 콘솔 디폴트 커맨드라인과 추가 실행 항목 관리
- `보기 > 콘솔` 하단 터미널 패널에서 PowerShell 7, Windows PowerShell, 명령 프롬프트 실행
- AvalonDock 기반 패널 도킹, 탭, 분리 창, 자동 숨김
- `F11`로 현재 폴더 패널 최대화 및 복원
- 도킹 배치 자동 저장 및 복원
- 그룹별 즐겨찾기 구성과 사용자별 저장
- 즐겨찾기 그룹 간 드래그 앤 드롭 이동
- 폴더 목록 하단의 선택 파일 미리보기와 메타정보
- 파일 내용 검색 (대/소문자 구분, 정규식, 하위 폴더 탐색 옵션)
- 내장 Markdown, Monaco, Text 뷰어 탭
- GitHub 최신 릴리스 기반 업데이트 확인 및 설치 파일 다운로드

## 내장 뷰어

확장자별 뷰어 매핑은 `설정 > 뷰어`에서 변경할 수 있으며 `%LOCALAPPDATA%\Folderss\viewer-config.json`에 저장됩니다.

| 뷰어 | 기본 확장자 | 주요 기능 |
|---|---|---|
| Markdown | `.md`, `.markdown` | 미리보기·편집·분할 보기, 활성 탭 중심 파일 변경 자동 반영, TOC, front matter 표시, Mermaid, KaTeX, HTML/PDF 내보내기 |
| Monaco | `.json`, `.xml` | Monaco Editor 기반 편집, 구문 강조, 대용량 파일 모드 |
| Text | 사용자 매핑 | 읽기 전용 텍스트 보기와 구문 강조 |

매핑되지 않은 확장자는 Windows 기본 프로그램으로 열립니다.

## 열기 프로그램

`설정 > 열기 프로그램`에서 파일이나 폴더를 열 외부 프로그램을 등록할 수 있습니다. 등록한 항목은 선택한 파일/폴더와 확장자 마스크가 맞을 때 메인 메뉴의 `다음으로 열기`와 Windows 컨텍스트 메뉴 상단에 표시됩니다.

- 확장자 마스크 `*`: 모든 파일/폴더
- 확장자 마스크 `folder`: 폴더
- 확장자 마스크 `.txt,.cs`: 특정 확장자 목록
- 인수의 `{0}`은 선택한 파일/폴더 경로 목록으로 치환됩니다.

## 콘솔

`보기 > 콘솔`에서 하단 터미널 패널을 열 수 있습니다. 콘솔은 `EasyWindowsTerminalControl`과 ConPTY를 사용하며 기본 항목으로 PowerShell 7, Windows PowerShell, 명령 프롬프트를 제공합니다. 설정 창에서는 기본 커맨드라인과 사용자 정의 실행 항목을 관리할 수 있습니다.

- 콘솔은 현재 활성 폴더를 작업 디렉터리로 시작합니다.
- 콘솔은 탭 방식으로 여러 세션을 열 수 있고 `+ 새 콘솔` 탭으로 새 세션을 추가합니다.
- `현재 폴더로 이동`은 실행 중인 셸을 활성 폴더 위치로 이동합니다.
- `외부 터미널` 버튼으로 현재 선택한 프로필 기준의 외부 콘솔도 열 수 있습니다.
- 콘솔 폰트 크기 설정 UI는 있으나 현재 런타임 반영에는 추가 보완이 필요합니다.

## 파일 미리보기

- 이미지 파일은 미리보기 영역 크기에 맞춰 표시합니다.
- 일반 파일은 처음 10KB까지 텍스트로 표시하며 바이너리 파일은 안내 문구를 표시합니다.
- 이름, 형식, MB 단위 크기, 생성·수정일자, 현재 사용자 권한과 파일 특성을 표시합니다.
- 목록과 미리보기 사이, 내용과 메타정보 사이의 분할선을 드래그해 크기를 조절할 수 있습니다.

## 도킹과 즐겨찾기

- 폴더 패널 탭을 드래그해 좌우·상하로 도킹하거나 별도 창으로 분리할 수 있습니다.
- 즐겨찾기 패널은 도킹, 자동 숨김, 분리 및 닫기를 지원합니다.
- `보기 > 즐겨찾기 표시`로 닫힌 즐겨찾기를 다시 표시합니다.
- 폴더 아이콘 버튼으로 즐겨찾기 그룹을 추가할 수 있습니다.
- 즐겨찾기의 `+` 버튼은 선택한 그룹에 현재 활성 폴더를 추가합니다.
- 그룹과 즐겨찾기 항목은 컨텍스트 메뉴에서 이름 변경, 순서 이동 및 삭제할 수 있습니다.
- 즐겨찾기 항목은 컨텍스트 메뉴 또는 드래그 앤 드롭으로 다른 그룹에 이동할 수 있습니다.
- 기존 즐겨찾기 설정은 최초 실행 시 `기본` 그룹으로 자동 이관됩니다.
- 툴바의 `+ 패널`, `파일 > 폴더 패널 추가` 또는 `Ctrl+T`로 폴더 패널을 추가할 수 있습니다.
- 추가한 폴더 패널도 이동·탭 병합·분리할 수 있으며 다음 실행 시 복원됩니다.
- 각 폴더 패널의 뒤로·앞으로 이동 내역은 최근 10개까지 유지됩니다.
- 앱 종료 시 열려 있던 폴더 위치와 활성 폴더를 저장하고 다음 실행 시 복원합니다.
- 도킹 배치는 창을 닫아 트레이로 숨길 때 즉시 저장되며, 다음 실행 시 패널의 분할 방향과 크기를 함께 복원합니다.

## 테마

`설정` 메뉴 또는 `설정 > 테마`에서 다음 테마를 전환할 수 있습니다. 마지막 선택은 사용자별로 저장됩니다.

- Black
- Light
- Nord
- Catppuccin Mocha
- Solarized Dark
- Dracula
- GitHub Primer Light

테마 색상과 공통 컨트롤 스타일은 다음 파일로 분리되어 있습니다.

```text
Folderss\Themes\Black.xaml
Folderss\Themes\Light.xaml
Folderss\Themes\Nord.xaml
Folderss\Themes\Catppuccin.xaml
Folderss\Themes\Solarized.xaml
Folderss\Themes\Dracula.xaml
Folderss\Themes\GitHub.xaml
Folderss\Themes\Controls.xaml
```

## 사용자 설정 파일

사용자별 설정은 `%LOCALAPPDATA%\Folderss\` 아래에 저장됩니다.

| 파일 | 내용 |
|---|---|
| `theme.txt` | 마지막 선택 테마 |
| `keybindings.xml` | 사용자 단축키 |
| `viewer-config.json` | 확장자별 뷰어 매핑 |
| `open-with.xml` | 사용자 지정 열기 프로그램 |
| `console-settings.xml` | 콘솔 설정(기본 프로필, 사용자 정의 프로필 등) |
| `favorites.xml` | 즐겨찾기 그룹과 항목 |
| `session.xml` | 열린 폴더 패널과 활성 패널 |
| `dock-layout.xml` | AvalonDock 패널 배치 |
| `dock-layout.xml.version` | 레이아웃 호환성 버전 |

## 단축키

### 탐색

| 단축키 | 동작 |
|---|---|
| `Alt+←` | 뒤로 가기 |
| `Alt+→` | 앞으로 가기 |
| `Alt+↑` | 상위 폴더 |
| `Ctrl+Shift+←` | 이전 패널로 전환 |
| `Ctrl+Shift+→` | 다음 패널로 전환 |

### 파일 작업

| 단축키 | 동작 |
|---|---|
| `F2` | 이름 변경 |
| `F5` | 새로 고침 |
| `F6` | 반대편 패널로 이동 |
| `Delete` | 휴지통으로 이동 |
| `Shift+Delete` | 영구 삭제 |
| `Ctrl+C` | 선택 항목 클립보드 복사 |
| `Ctrl+X` | 선택 항목 클립보드 잘라내기 |
| `Ctrl+V` | 클립보드에서 붙여넣기 |
| `Ctrl+Shift+N` | 새 폴더 |

### 보기

| 단축키 | 동작 |
|---|---|
| `Ctrl+R` | 양쪽 패널 새로 고침 |
| `Ctrl+T` | 새 폴더 패널 추가 |
| `F11` | 현재 폴더 패널 최대화/복원 |
| `Ctrl+F` | 파일 내용 검색 패널 열기/닫기 |

기본 단축키는 `설정 > 단축키`에서 변경하거나 기본값으로 초기화할 수 있습니다.

## 빌드

### 요구 사항

- Windows 10 이상
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (내장 뷰어 사용 시 필요)
- 다음 중 하나
  - Visual Studio 2022 이상 (워크로드: **.NET 데스크톱 개발**)
  - [Visual Studio Build Tools](https://visualstudio.microsoft.com/visual-studio-build-tools/) (구성 요소: **MSBuild**)

주요 NuGet 패키지는 SDK 스타일 프로젝트 기준으로 복원됩니다.

- `Dirkster.AvalonDock` 4.74.1
- `EasyWindowsTerminalControl` 1.0.36
- `CI.Microsoft.Windows.Console.ConPTY` 1.22.250314001
- `Microsoft.Web.WebView2` 1.0.2739.15

### Visual Studio에서 빌드

1. `Folderss.sln`을 Visual Studio에서 열기
2. 솔루션 탐색기에서 `Folderss` 프로젝트 우클릭 → **빌드**
3. 또는 `Ctrl+Shift+B`로 전체 솔루션 빌드

### MSBuild(커맨드라인)로 빌드

`dotnet build`를 기본 빌드 명령으로 사용합니다.

**Debug 빌드**

```powershell
dotnet build .\Folderss.sln -c Debug
```

**Release 빌드**

```
dotnet build .\Folderss.sln -c Release
```


### 빌드 결과물

| 구성 | 경로 |
|---|---|
| Debug | `Folderss\bin\Debug\net8.0-windows\Folderss.exe` |
| Release | `Folderss\bin\Release\net8.0-windows\Folderss.exe` |

## 실행

빌드 없이 바로 실행하려면 최신 Release 바이너리를 사용합니다.

```text
Folderss\bin\Release\net8.0-windows\Folderss.exe
```

> 기본 삭제는 Windows 휴지통으로 이동합니다. `Shift+Delete`는 휴지통을 거치지 않고 영구 삭제합니다.

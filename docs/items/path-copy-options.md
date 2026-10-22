# 경로 복사 옵션 (전체 경로·이름·상위 폴더·상대 경로·UNC·따옴표)

- 상태: Ready for Verification

## 요구사항

- 선택한 파일·폴더의 경로를 여러 형식으로 클립보드에 복사한다. 제안 당시 예시: 전체 경로, 이름만, 상대 경로, 따옴표 포함, UNC 경로.
- 현재는 트리뷰 `Ctrl+Shift+C`(폴더 전체 경로 텍스트만)와 즐겨찾기 `Ctrl+C`(경로)만 있고, 파일 목록에는 경로 복사 명령이 없다.
- 파일 목록 `Ctrl+C`(실제 파일 FileDrop + 이름 텍스트)는 그대로 둔다.
- 여러 개를 고르면 한 줄에 하나. 복사 후 상태 표시(예: "경로 3개 복사함").

## 원인 분석 또는 설계

### 참고 제품 조사

| 제품 | 동작 | 출처 |
|---|---|---|
| Windows 11 탐색기 | 우클릭 `경로로 복사`(Windows 10은 Shift+우클릭 확장 메뉴), 명령 모음 `경로 복사`, 단축키 `Ctrl+Shift+C`(빌드 22463부터). 항상 **큰따옴표로 감싼** 전체 경로, 여러 개는 한 줄에 하나. 매핑된 네트워크 드라이브도 드라이브 문자 경로 그대로(UNC로 바꾸지 않음 — 사용자 불만이 많고 Microsoft 답변도 `net use`로 확인하라는 수준) | elevenforum "Copy Path of Drive, File, or Folder in Windows 11", windowslatest 2021-12-27, Microsoft Q&A "Copy UNC network path (not drive letter) for paths on mapped drives" |
| VS Code | 탐색기 우클릭 `Copy Path`·`Copy Relative Path`(같은 메뉴 그룹 `6_copypath`). 단축키 Windows `Shift+Alt+C`(전체), `Ctrl+K Ctrl+Shift+C`(상대). 상대 경로는 작업 영역 폴더 기준. 여러 개는 `\r\n`(Windows)로 줄마다 하나. 따옴표 없음. 구분자는 설정 `explorer.copyPathSeparator`·`explorer.copyRelativePathSeparator`(`/`, `\`, `auto` — 기본 `auto` = OS 기본) | microsoft/vscode `fileCommands.ts`, `fileActions.contribution.ts`, `files.contribution.ts` |
| JetBrains IDE | `Copy Paths` = `Ctrl+Shift+C`(절대 경로), `Copy Reference` = `Ctrl+Alt+Shift+C`. 우클릭 `Copy/Paste Special` 하위 메뉴에 경로 형식이 모여 있음 | IntelliJ IDEA Windows 기본 키맵 도움말 |
| Total Commander | `cm_CopyNamesToClip`(이름), `cm_CopyFullNamesToClip`(전체 경로), `cm_CopyNetNamesToClip`("Copy names with UNC path" — 매핑된 드라이브를 `\\서버\공유`로) 등 형식별 명령. 기본 단축키는 없고 사용자가 지정. `/` 구분자 변형은 기본 기능에 없어 요청·애드온(Lst2Clip)으로 처리 | ghisler.ch 포럼 명령 목록·Lst2Clip 설명 |
| Directory Opus | `Clipboard COPYNAMES=` 인수: (없음)=전체 경로, `nopaths`=이름, `path`=상위 폴더, `unc`=매핑 드라이브 → UNC(로컬이면 효과 없음), `quote`=모두 따옴표, `capsemantics`=탐색기 "경로로 복사"와 같게(줄마다 따옴표), `url`, `wsl`, `single`(한 줄에 공백 구분). 기본은 한 줄에 하나 | Directory Opus 12 매뉴얼 Clipboard |
| Path Copy Copy (탐색기 확장) | 우클릭 하위 메뉴에 이름·전체 경로·상위 폴더 경로·UNC 등. 여러 개는 줄마다 하나. 따옴표는 형식이 아니라 전역 옵션 | clechasseur/pathcopycopy 위키 Usage·Settings |

조사에서 얻은 공통점:
1. 형식마다 **명령(메뉴 항목) 하나** — 하위 메뉴에 모은다(VS Code 메뉴 그룹, Path Copy Copy·JetBrains 하위 메뉴, Opus 인수).
2. 여러 개는 **줄마다 하나**(CRLF). 한 줄 공백 구분은 Opus 옵션뿐.
3. 따옴표: 탐색기만 기본으로 감싼다. VS Code·JetBrains·TC·Opus 기본은 따옴표 없음 → 따옴표는 별도 형식.
4. UNC는 "매핑된 네트워크 드라이브일 때만 의미 있음"(Opus `unc`는 로컬에서 효과 없음, TC도 네트워크 이름). 탐색기는 지원하지 않는다.
5. `/` 구분자는 VS Code가 메뉴 항목이 아니라 **설정**으로 둔다.
6. 단축키 `Ctrl+Shift+C`는 탐색기(따옴표 전체 경로)·JetBrains(절대 경로)가 쓰고, 이 앱도 트리뷰에서 이미 같은 키로 따옴표 없는 전체 경로를 복사한다.

### 설계

- **형식** (`Services/PathCopyFormatter.cs`, 순수 로직):

  | 형식 | 예 (`C:\work\my app\a.txt`) | 메뉴에 보일 때 |
  |---|---|---|
  | 전체 경로 | `C:\work\my app\a.txt` | 항상 |
  | 전체 경로 (따옴표) | `"C:\work\my app\a.txt"` — 탐색기 "경로로 복사"와 같음(공백 없어도 감쌈) | 항상 |
  | 이름 | `a.txt` (드라이브 루트·공유 루트는 이름이 없어 경로 그대로) | 항상 |
  | 상위 폴더 경로 | `C:\work\my app` (여러 개가 같은 폴더면 한 줄) | 항상 |
  | 상대 경로 (저장소 기준) | `src\a.txt` — 항목을 담은 폴더부터 위로 `.git`(폴더 또는 파일)을 찾은 Git 저장소 루트 기준(VS Code의 작업 영역 기준과 같은 역할) | 저장소 안일 때. 메뉴 이름에 저장소 이름 |
  | 상대 경로 (반대편 패널 기준) | `..\docs\a.txt` — 반대편 폴더 패널(⋯ 메뉴 "반대편 패널에 링크 만들기"와 같은 패널)의 현재 폴더 기준. 마크다운 링크 등에 씀 | 다른 폴더 패널이 있고 같은 드라이브일 때. 메뉴 이름에 그 폴더 이름 |
  | UNC 경로 | `Z:\a.txt` → `\\server\share\a.txt` (`WNetGetUniversalName`, 안 되면 `WNetGetConnection` + 나머지 경로) | 매핑된 네트워크 드라이브의 항목일 때 |

- **여러 개**: 목록에 보이는 순서로 한 줄에 하나(CRLF), 같은 줄은 한 번만, 끝 줄바꿈 없음.
- **선택이 없으면** 현재 폴더(빈 곳 우클릭 셸 메뉴가 현재 폴더를 대상으로 하는 것과 같음).
- **대체 규칙**: 쓸 수 없는 형식(저장소 밖의 저장소 기준, 다른 드라이브의 반대편 기준, UNC로 못 바꾼 경로, 상위 폴더가 없는 루트)은 그 항목만 전체 경로로 복사하고 상태 표시에 개수를 알린다. 메뉴는 쓸 수 없는 형식을 아예 보이지 않으므로 주로 단축키 기본 형식에서 생긴다.
- **복사 내용**: 텍스트만(FileDrop 없음 — 탐색기에 붙여넣어도 파일이 복사되지 않음, 기존 트리 경로 복사와 같음). 복사 후 잘라내기 표시를 지운다(기존 복사 동작과 같음).
- **메뉴 위치**
  - 파일 목록 우클릭: 앱이 Windows 셸 메뉴 맨 위에 자기 항목을 넣는 방식(`선택한 두 파일 비교`, `Open with …`)을 따라 `경로 복사 ▸` 하위 메뉴를 넣는다. `ShellContextMenuService.CustomMenuItem`에 `Children`(하위 메뉴, `Label == null`이면 구분선)을 추가했다.
  - 트리뷰 우클릭: 같은 하위 메뉴(대상 = 우클릭한 폴더). 트리 `Ctrl+Shift+C`와 같은 폴더 패널 안의 명령이라 맞춘다.
  - ⋯ 메인 메뉴: `경로 복사 ▸`(활성 패널의 선택, 없으면 현재 폴더). 열 때마다 다시 채운다(조건부 항목·단축키 표시).
  - 즐겨찾기: 넣지 않는다(이미 `Ctrl+C`가 전체 경로 복사, 즐겨찾기는 파일 작업 영역이 아님).
- **단축키**: 기존 `CopyFolderPath`(`Ctrl+Shift+C`, "폴더 경로 복사")를 "경로 복사"로 넓힌다. 트리에 포커스 → 트리 선택 폴더(기존과 같음), 파일 목록 포커스 → 선택 항목(없으면 현재 폴더). CommandId는 그대로 둬 사용자가 바꾼 `keybindings.xml`이 계속 맞는다. 형식은 설정의 기본 형식(기본값 = 전체 경로: 기존 트리 동작·VS Code·JetBrains와 같음. 탐색기처럼 따옴표를 원하면 설정에서 바꿈). 메뉴의 해당 항목 옆에 현재 단축키를 표시한다.
- **설정**(설정 창 `경로 복사` 탭, `%LOCALAPPDATA%\Folderss\path-copy-settings.xml`, `Services/PathCopySettingsService.cs`):
  - 단축키 형식: 위 7가지 중 하나(기본 전체 경로).
  - 경로 구분자: `\`(Windows 기본, 기본값) / `/` 상대 경로만 / `/` 모든 경로 — VS Code의 두 구분자 설정을 한 상자로 합침. 이름 형식에는 영향 없음.
  - 저장은 `SettingsFile.Write`(임시 파일 후 교체), 실패는 예외 → `Save_Click`의 `TrySave` 목록.
- **상태 표시**: 그 패널 아래 상태 줄에 4초 동안 "전체 경로 3개 복사함"(대체가 있으면 "· 1개는 전체 경로로 대신"). 셸 메뉴 뒤 목록 새로 고침이 상태 줄을 덮지 않도록 디스패처 뒤로 미뤄 표시한다.

### 사용자 확인이 필요한 결정

| 결정 | 고른 것 | 대안 |
|---|---|---|
| `Ctrl+Shift+C` 기본 형식 | 따옴표 없는 전체 경로(기존 트리 동작·VS Code·JetBrains) | 탐색기처럼 따옴표 포함 — 설정에서 바꿀 수 있음 |
| 상대 경로 기준 | 저장소 루트 + 반대편 패널 두 가지를 조건부로 모두 제공 | 하나만(저장소 루트) 두기, 현재 폴더 기준(파일 목록에서는 이름과 같아 제외) |
| `/` 구분자 | 메뉴 항목이 아니라 설정(VS Code 방식), 기본 `\` | 하위 메뉴에 `/` 변형 항목 추가, 상대 경로만 기본 `/` |
| 선택이 없을 때 | 현재 폴더 경로 | 아무것도 하지 않음 |
| 즐겨찾기 메뉴 | 넣지 않음 | 즐겨찾기 우클릭에도 같은 하위 메뉴 |
| 로컬 드라이브의 UNC | 메뉴에 안 보임(관리 공유 `\\PC\C$`로 바꾸지 않음 — 관리자 권한 필요) | 관리 공유로 변환하는 옵션 |
| 단축키 이름 | 기존 "폴더 경로 복사" → "경로 복사"(ID 유지) | 새 CommandId를 따로 두기(같은 키라 충돌) |
| 우클릭 메뉴의 "전체 경로 (따옴표)" | 유지 | 실제 확인에서 Windows 11 셸 메뉴 자체에 탐색기 `경로로 복사(A)`가 이미 들어 있음을 봤다(같은 결과). ⋯ 메뉴·단축키 형식과 목록을 맞추려고 남겼으나, 우클릭 하위 메뉴에서만 뺄 수도 있음 |
| 하위 메뉴 위치 | 셸 메뉴 맨 위 앱 항목의 마지막(두 파일/폴더 비교·Open with 다음) | 앱 항목의 맨 앞 |

## 구현 내용

- `Services/PathCopyFormatter.cs`(순수 로직): `PathCopyFormat` 7가지, `PathCopySeparator` 3가지, `PathCopyContext`(저장소 찾기·반대편 폴더·UNC 해석 함수·구분자), `Format`(줄마다 하나·중복 제거·대체 개수), `IsAvailable`(메뉴 표시 여부), `StatusMessage`, `FindRepositoryRoot`, `CombineUnc`, Windows 경로 도우미(`GetRootLength`·`TrimTrailingSeparators`·`GetName`·`GetParent`·`GetRelativePath`, `\\?\`·UNC 루트 처리). System.IO.Path를 쓰지 않아 테스트가 OS와 무관하다. 해석 함수가 예외를 던지면 실패로 보고 대체한다.
- `Services/PathCopySettingsService.cs`: `path-copy-settings.xml`(`shortcutFormat`, `separator` 속성) 읽기·쓰기, `Current`. 모르는 값은 항목별 기본값, 쓰기는 `SettingsFile.Write`, 실패는 예외.
- `Services/PathCopyService.cs`: `CreateContext`(저장소 = `.git` 폴더/파일, UNC = `WNetGetUniversalName` → `WNetGetConnection` + `CombineUnc`, 네트워크 드라이브일 때만), `Copy`(텍스트만 `ClipboardService.TrySetDataObject`), `GetMenuEntries`(조건부 항목, 저장소·반대편 폴더 이름, 단축키 표시, 조건부 묶음 앞 구분선), `BuildShellMenu`(Win32 `&` → `&&`, 단축키는 탭 뒤), `FillMenu`(WPF `_` → `__`), `ShortcutTextProvider`.
- `Services/ShellContextMenuService.cs`: `CustomMenuItem.Children`(하위 메뉴)·`Label == null`(구분선) 지원. `InsertCustomItem`이 `CreatePopupMenu` + `MF_POPUP`으로 붙이고, 명령 번호를 하위 메뉴 안 항목까지 순서대로 매겨 `customActions[n]`로 실행한다(기존 평면 항목도 같은 경로).
- `Controls/FolderBrowser.PathCopy.cs`(partial, 새 파일): `OppositeFolderProvider`, `GetPathCopyTargets`(목록 순서, 한 번 훑기, 없으면 현재 폴더), `CopyPathForShortcut`, `FillPathCopyMenu`, `BuildPathCopyShellMenu`, `CopyPaths`(잘라내기 표시 해제), `ShowTransientStatus`(디스패처 뒤로 미뤄 4초 표시 후 원래 문구, 그 사이 다른 문구로 바뀌었으면 그대로).
- `Controls/FolderBrowser.xaml.cs`: 파일 목록·트리 우클릭에서 `경로 복사 ▸` 추가(각 3줄), 옛 `CopyFolderTreePathToClipboard` 제거(단축키 처리 이동).
- `MainWindow.xaml(.cs)`: ⋯ 메뉴 `경로 복사`(열 때 `FillPathCopyMenu`), `AttachFolderBrowser`에서 `OppositeFolderProvider`, 시작 시 `ShortcutTextProvider`, `CopyFolderPath` 처리를 `CopyPathForShortcut`으로.
- `Services/KeyBindingService.cs`: `CopyFolderPath` 표시 이름 "경로 복사"(ID 유지 — `Load`가 기본값 표시 이름을 쓰므로 기존 사용자도 새 이름으로 보임).
- `SettingsWindow.xaml(.cs)`: `경로 복사` 탭(네트워크 다음, 기존 탭 번호 상수에 영향 없음) — 단축키 형식·구분자 콤보, 예시 미리 보기(`Z:\Folderss\docs\read me.md`를 가정 조건으로 변환), `Save_Click`의 `TrySave("경로 복사", "path-copy-settings.xml")`.
- 문서: README(기능·설정·설정 파일 표·단축키 표·아키텍처 요약), `docs/architecture.md`(파일 목록·서비스·"경로 복사" 절·설정 창 탭), `docs/keyboard-shortcuts.md`(`CopyFolderPath` 행, 미구현 제안에서 제거), AGENTS.md·CLAUDE.md "단축키 추가 시"에 `docs/keyboard-shortcuts.md` 추가(이번에 체크리스트에 없던 연관 파일).

## 변경 파일

- `Folderss/Services/PathCopyFormatter.cs` (새 파일)
- `Folderss/Services/PathCopyService.cs` (새 파일)
- `Folderss/Services/PathCopySettingsService.cs` (새 파일)
- `Folderss/Controls/FolderBrowser.PathCopy.cs` (새 파일)
- `Folderss/Controls/FolderBrowser.xaml.cs`
- `Folderss/Services/ShellContextMenuService.cs`
- `Folderss/Services/KeyBindingService.cs`
- `Folderss/MainWindow.xaml`, `Folderss/MainWindow.xaml.cs`
- `Folderss/SettingsWindow.xaml`, `Folderss/SettingsWindow.xaml.cs`
- `tests/Folderss.SearchTests/PathCopyFormatterTests.cs` (새 파일), `tests/Folderss.SearchTests/Folderss.SearchTests.csproj`
- `README.md`, `docs/architecture.md`, `docs/keyboard-shortcuts.md`, `AGENTS.md`, `CLAUDE.md`
- `docs/items/path-copy-options.md`

## 검증

- 빌드: `dotnet build .\Folderss.sln -c Debug` → 오류 0(Exit 0). 새·바꾼 파일에서 새 경고 없음(기존 CA1416만).
- 단위 테스트: `dotnet test tests/Folderss.SearchTests` → 전체 484개 중 통과 480, 건너뜀 4(기존 건너뜀 — git difftool·접근 거부·심볼릭 링크 조건), 실패 0.
  경로 복사 테스트 79개(`PathCopyFormatterTests`, `PathCopySettingsServiceTests`): 전체 경로·끝 구분자, 따옴표(공백 없어도), 이름(드라이브·공유 루트는 경로 그대로), 상위 폴더(루트는 대체), 여러 줄 CRLF·순서·중복·빈 값, 저장소 기준(대소문자 무시, 저장소 밖 대체, 저장소 루트 자신, 실제 `.git` 폴더·`.git` 파일), 반대편 패널 기준(`..`, 드라이브 루트·공유·다른 드라이브·없음·상대 기준), UNC(매핑 변환, 실패·예외 시 원래 경로, 이미 UNC면 메뉴 숨김, `CombineUnc`), 구분자(상대만 `/`·모두 `/`·이름 제외·대체는 `\` 유지), 300자 넘는 한글·공백 경로, `\\?\` 접두사, 루트 길이, 메뉴 표시 조건, 설정 저장·읽기·잘못된 값·쓰기 실패 예외.
- 실제 실행(격리 프로필 — `USERPROFILE`·`LOCALAPPDATA`를 스크래치 폴더로, 가짜 Git 저장소 `work\샘플 저장소`(.git 폴더) 왼쪽 `docs`·오른쪽 `src`, UIA 패턴만 사용, 클립보드는 시작 전 텍스트를 저장하고 끝나면 복원 — 복원 후 원래 텍스트와 같음 확인):
  - ⋯ 메뉴 > 경로 복사 하위 메뉴: `전체 경로 / 전체 경로 (따옴표) / 이름 / 상위 폴더 경로 / 상대 경로 (저장소 '샘플 저장소' 기준) / 상대 경로 (반대편 패널 'src' 기준)`(로컬 드라이브라 UNC 없음), `전체 경로` 옆 `Ctrl+Shift+C`.
  - 파일 2개 선택 후 각 형식 복사 → 클립보드 내용 기대값과 일치 14건 통과: 전체 경로 2줄, 따옴표 2줄, 이름 2줄, 상위 폴더 1줄, `docs\read me.md`·`docs\한글 문서.md`, `..\docs\…`, 선택 없음 → 현재 폴더·`docs`.
  - 상태 줄 "전체 경로 2개 복사함" 표시.
  - 설정 창 `경로 복사` 탭에서 구분자 "/ 상대 경로만", 단축키 형식 "전체 경로 (따옴표)" → 저장 → `path-copy-settings.xml` = `<pathCopy shortcutFormat="QuotedFullPath" separator="SlashForRelative" />`, 이후 저장소 기준 `docs/read me.md`, 전체 경로는 `\` 유지, 메뉴의 단축키 표시가 `전체 경로 (따옴표)` 옆으로 옮겨짐(스크린샷).
  - 셸 우클릭 메뉴: 메인 창에 마우스 메시지를 보내는 방식으로는 메뉴가 뜨지 않아(입력 주입 금지로 실제 우클릭 불가), 빌드한 `Folderss.dll`을 별도 pwsh에서 불러 `PathCopyService.BuildShellMenu` + `ShellContextMenuService.Show`를 그대로 실행하는 하네스로 확인. 셸 메뉴 맨 위에 `경로 복사 ▸`, 하위 메뉴 6항목(구분선 포함), 하네스의 메뉴 창에만 ↓→↓↓Enter 메시지를 보내 `이름` 선택 → 결과 `Name` / `read me.md⏎한글 문서.md` / "이름 2개 복사함", 클립보드 일치.
  - 비교: 기존 `보기` 하위 메뉴도 같은 WPF 기본 하위 메뉴 모양(항목 테두리)임을 확인 — 이번 변경으로 생긴 모양이 아님.
  - 스크린샷(스크래치 폴더): `01-main-menu-path-copy.png`, `02-status-after-copy.png`, `03-settings-path-copy-tab.png`, `04-main-menu-after-settings.png`, `05-shell-context-menu-path-copy.png`, `07-existing-view-submenu-for-comparison.png`. 팝업은 화면 영역 복사로 찍어 왼쪽 위에 다른 창의 회색 사각형이 겹쳐 보일 수 있다(앱 UI 아님).
- 사용자 확인 필요(이번에 직접 못 함):
  1. 실제 마우스 우클릭: 파일 목록·트리뷰에서 `경로 복사 ▸`가 셸 메뉴 맨 위에 보이고 항목이 동작하는지, 복사 후 상태 줄 표시.
  2. `Ctrl+Shift+C`(키 입력 주입 금지로 미실행): 파일 목록 포커스 → 선택 항목 경로(여러 줄), 선택 없음 → 현재 폴더, 트리 포커스 → 트리 폴더(기존과 같음), 경로 입력란·필터 입력란에서는 복사하지 않음.
  3. 매핑된 네트워크 드라이브(`net use Z: \\서버\공유`)에서 `UNC 경로` 항목이 보이고 `\\서버\공유\…`로 복사되는지(이 PC에 매핑된 드라이브가 없어 단위 테스트로만 확인).
  4. 탐색기에 붙여넣어도 파일이 복사되지 않음, 잘라내기 표시가 사라짐.

### 알려진 한계

- 셸 우클릭 메뉴는 Windows 메뉴라 하위 메뉴 항목에 설명(툴팁)이 없고, 기준 폴더는 이름(`'src'`)만 보인다(⋯ 메뉴도 같음).
- 즐겨찾기·폴더 비교 창·검색 결과 창에는 넣지 않았다. 플러그인 팝업 패널은 반대편 패널이 없어 그 형식이 빠진다.
- 선택이 아주 많을 때(수천 개) UNC 형식은 항목마다 `WNetGetUniversalName`을 부른다.
- 존재 여부는 확인하지 않고 경로 글자를 복사한다(기존 트리 경로 복사와 같음).

## 변경 이력

- 2026-10-23: 요청 접수·착수. 참고 제품 조사와 설계 기록.
- 2026-10-23: 구현·단위 테스트(79개)·전체 테스트·격리 프로필 실제 실행(⋯ 메뉴·설정 창·셸 메뉴 하네스) 완료. `Ready for Verification`.

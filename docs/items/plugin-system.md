# 플러그인 기능 (설정 > 플러그인, ⋯ 메뉴 > 플러그인)

- 상태: Ready for Verification

## 요구사항

1. 설정 > 플러그인 메뉴를 두고, `플러그인 찾기`로 플러그인(압축 파일)을 등록한다.
2. ⋯ 메뉴에 플러그인 메뉴 항목을 추가하고, 등록된 플러그인을 선택하면 그때 실제 압축 파일을 로드한다.
3. 현재 범위: 팝업으로 로드하는 것과 메인 프로그램의 폴더 패널을 노출하는 것.

### 2차 요청 (2026-09-30)

1. 플러그인 매니저를 제공해 폴더 패널을 생성하는 함수를 제공한다.
2. 설정 정보를 제공하는 함수를 제공한다.
3. 플러그인 로드가 실패해도 메인 애플리케이션은 죽으면 안 된다.
4. 플러그인이 필요하면 플러그인 매니저로 설정 팝업에 탭을 추가할 수 있다.

### 3차 요청 (2026-09-30)

1. 애플리케이션에 설정된 정보를 읽기 전용으로, 필요 시 참고할 수 있게 제공한다. 다른 플러그인의 설정은 제외.
2. 메뉴 이름은 플러그인이 설정한 정보(plugin.json)만으로 만들고, 실제 로드는 사용자가 플러그인을 직접 실행하는 시점에 한다.
3. (한계 인지)
4. 팝업 방식 유지.
5. 플러그인이 메인 프로세스를 죽이려는 경우 막을 수 있는지, 불가능하면 종료 감지·로깅이 가능한지 확인한다.

### 4차 요청 (2026-09-30)

- 플러그인 개발자용 개발 방법 문서(Markdown)와 별도 튜토리얼 문서 작성. 튜토리얼은 화면에 타이틀과 그리드를 배치한 "주문서" 콘텐츠.

## 원인 분석 또는 설계

### 3차 설계
- **본체 설정**: `GetAppSettings()` — 키를 명시한 문자열 사전(테마, Git, 비교, 콘솔 스칼라 값). 리플렉션 자동 노출은 속성 이름 변경이 플러그인을
  깨고 새 민감 값이 자동 노출되는 위험이 있어 쓰지 않았다. 열기 프로그램·단축키·뷰어 매핑·콘솔 사용자 프로필은 넣지 않았다(필요 시 추가).
  다른 플러그인 설정은 API로 주지 않는다. 단, 같은 권한의 코드라 파일을 직접 읽는 것은 막을 수 없다.
- **로드 시점**: 2차의 "설정 창을 열 때 hasSettings 플러그인 로드"를 없앴다. 설정 탭은 이번 실행에서 이미 실행한 플러그인만, 아직이면 plugin.json 이름의 안내 탭.
- **종료를 막을 수 있는가** (같은 프로세스 기준):

  | 종료 경로 | 막기 | 감지·기록 |
  |---|---|---|
  | 메인 창 `Close()` | 이미 트레이 숨기기라 종료 안 됨 | 불필요 |
  | `Application.Shutdown()` | 불가(취소 불가한 종료) | 가능 — 사용자 종료 표시 없이 `Exit` 도달. 호출자는 스택에 안 남아 로드된 플러그인을 용의자로 기록 |
  | `Environment.Exit()` | 불가 | 가능 — `ProcessExit`가 `Exit` 없이 옴. 스택에서 플러그인 판별(최선) |
  | 별도 스레드 처리되지 않은 예외 | 불가(.NET Core는 종료 강제) | 가능 — `UnhandledException`에서 예외 스택으로 판별 |
  | UI 스레드 예외 | 가능(2차에서 구현) | 가능 |
  | `Process.Kill`·`FailFast`·스택 오버플로·네이티브 크래시·`TerminateProcess` | 불가 | 그 순간은 불가. 다음 시작 때 남은 기록으로 "비정상 종료 + 로드된 플러그인" 감지 |

  완전히 막으려면 플러그인을 별도 프로세스에서 실행해야 하는데, 그러면 WPF 폴더 패널을 플러그인 화면에 직접 넣을 수 없어 이번 요구와 맞지 않는다.

### 형식: .NET DLL + 계약 DLL
- WPF 폴더 패널(`FolderBrowser`)을 플러그인 화면에 넣으려면 같은 프로세스의 WPF 코드여야 한다(HTML/WebView2 플러그인은 불가).
- 계약은 별도 프로젝트 `Folderss.PluginContract`. 플러그인이 `Folderss.exe`를 직접 참조하면 본체를 고칠 때마다 깨진다.

### 가정 (확인 필요)
- **플러그인 찾기** = 로컬 zip 파일 선택. 온라인 검색·마켓은 범위 밖.
- **설정 정보** = 플러그인 자기 설정(키/값, `plugin-data\<id>\settings.json`)과 데이터 폴더. 본체 설정(Git·테마 등)은 노출하지 않았다 — 본체 내부 모델이 계약에 들어가게 되기 때문. 필요하면 읽기 전용 항목을 골라 추가.
- **팝업** = 비모달 창(Git 창 방식), 선택할 때마다 새 창.
- **설정 탭과 지연 로드의 충돌**: "선택할 때 로드"만 하면 설정 탭은 그 플러그인을 한 번 연 뒤에만 보인다. 그래서 `plugin.json`에 `hasSettings: true`를 선언한 플러그인만 설정 창을 열 때 로드한다.
- **추가·제거 적용 시점**: 설정 창 `저장`과 무관하게 즉시. 로드된 플러그인의 교체·제거는 재시작 후(WPF 어셈블리는 언로드 불가).

### 위험과 한계
- 플러그인은 Folderss와 같은 권한으로 실행된다(서명 검증 없음). 추가 시 경고 후 확인(기본 `아니요`).
- "본체가 죽지 않음"은 로드·호출 경계와 UI 스레드 예외까지만 보장. 플러그인이 만든 스레드의 예외, StackOverflow, 네이티브 크래시, `Environment.Exit`은 같은 프로세스라 막을 수 없다(완전 격리는 별도 프로세스가 필요 — 이 경우 WPF 폴더 패널을 직접 넘길 수 없음).
- UI 스레드 예외 판별은 예외 스택에 플러그인 어셈블리 프레임이 있는지로 한다. 플러그인이 본체 콜백 안에서 잘못된 값을 넘겨 본체 코드에서만 예외가 나면 본체 예외로 취급된다(기존처럼 종료).

## 구현 내용

- `Folderss.PluginContract`: `IFolderssPlugin`(Initialize/CreateView), `IPluginManager`(PluginId, PluginDirectory, DataDirectory, Get/SetSetting, GetAllSettings, CreateFolderPanel, AddSettingsPage), `IFolderPanel`, `IPluginSettingsPage`.
- `PluginPackage`: manifest 검증(id 형식, assembly 상대 경로·존재), `plugins\<id>.zip` 등록·목록, `plugins\extracted\<id>-<해시>` 해제, Zip Slip 차단.
- `PluginSettingsStore`: 키/값 JSON, `SettingsFile` 사용, 쓰기 실패는 예외.
- `PluginManager`: 플러그인별 `AssemblyLoadContext`(계약 DLL은 본체 것), 로드 캐시, 팝업 창, `PluginHost`, `PluginFolderPanel`(파일 열기 → 메인 창 뷰어 탭), `TryHandleUnhandled`.
- `App`: `DispatcherUnhandledException`에서 플러그인 발 예외만 처리.
- `MainWindow`: ⋯ 메뉴 `플러그인` 하위 메뉴(열 때마다 목록 갱신, `플러그인 관리…`로 설정 탭 이동).
- `SettingsWindow`: `플러그인` 탭(목록·찾기·제거), 플러그인 설정 탭 동적 추가, 저장 시 `TrySave`로 플러그인 `Save` 호출.
- `samples/HelloPlugin`: 설정한 시작 폴더로 폴더 패널 팝업 + 시작 폴더 설정 탭. 빌드 시 `HelloPlugin.zip` 생성.

### 3차
- 계약 `IPluginManager.GetAppSettings()` 추가 (인터페이스는 본체만 구현하므로 기존 플러그인 호환).
- `PluginAppSettings`(키 목록), `PluginSessionRecord`/`PluginSessionLog`(기록·수거·로그) 신규.
- `PluginManager`: `GetLoaded`, `InstallExitHooks`, `MarkUserRequestedExit`, `OnApplicationExit`, `ReportPreviousAbnormalExits`, 처리한 UI 예외도 로그.
- `App`: 종료 감시 설치, `OnExit`, 시작 후(ApplicationIdle) 이전 비정상 종료 알림. `MainWindow.Window_Closing`: 확인을 지난 정상 종료 표시.
- `SettingsWindow`: 플러그인 로드 제거, 이미 로드한 플러그인 탭 / 안내 탭.
- `HelloPlugin`: 본체 테마 표시, 종료 감지 확인용 `Application.Shutdown`·`Environment.Exit` 버튼.

### 4차
- `docs/plugin-development.md`: 수명 주기, 프로젝트 설정(프로젝트/파일 참조, zip 생성 타깃), plugin.json 키, `IFolderssPlugin`/`IPluginManager`/`IFolderPanel`/`IPluginSettingsPage`,
  `GetAppSettings` 키, 테마 리소스 키, 오류 처리·보호 범위, 디버깅, 제약, 배포 체크리스트.
- `docs/plugin-tutorial-order-form.md`: 타이틀 + `DataGrid`(품목·수량·단가·금액, 금액 자동 계산, 행 추가·삭제, 테마 연결) 주문서 플러그인 단계별 작성·등록·실행·문제 해결.
- 결정: 튜토리얼 코드는 저장소 샘플로 넣지 않고 문서에만 둔다(요청 범위). 대신 문서의 코드 블록을 그대로 뽑아 문서에 적힌 위치에서 빌드해 검증.
- UI는 코드로 구성하는 방식만 안내. 전용 로드 영역에서 XAML `UserControl` 리소스 로드는 미검증이라 "원리상 가능, 미검증"으로 적었다.

## 변경 파일

- `Folderss.PluginContract/` (신규), `Folderss.sln`, `Folderss/Folderss.csproj`
- `Folderss/Services/PluginManager.cs`, `PluginPackage.cs`, `PluginSettingsStore.cs` (신규)
- `Folderss/App.xaml.cs`, `MainWindow.xaml(.cs)`, `SettingsWindow.xaml(.cs)`
- `samples/HelloPlugin/` (신규)
- `tests/Folderss.SearchTests/PluginPackageTests.cs` (신규), `Folderss.SearchTests.csproj`
- `README.md`, `docs/architecture.md`, `CLAUDE.md`, `AGENTS.md`
- 3차: `Folderss/Services/PluginAppSettings.cs`, `PluginSessionRecord.cs` (신규), `tests/Folderss.SearchTests/PluginSessionTests.cs` (신규)

## 검증

- `dotnet test tests/Folderss.SearchTests`: 118 통과, 1 건너뜀(기존). 플러그인 테스트 17개 추가.
  - Zip Slip 테스트는 경로 검사를 빼면 실패함을 확인(검사가 실제로 막고 있음).
- Linux에서 `dotnet build Folderss.sln -p:EnableWindowsTargeting=true`: 새 오류 없음. 남은 오류 1건(`ConsolePanel.xaml.cs` `Microsoft.Terminal`)은 변경 전에도 Linux에서만 나는 기존 오류.
- `dotnet build samples/HelloPlugin`: 성공, zip에 `plugin.json` + `HelloPlugin.dll`만 포함(계약 DLL 미포함) 확인.
- **Windows에서 사용자 확인 필요** (로더·WPF 동작은 여기서 실행 불가):
  1. `dotnet build .\Folderss.sln -c Debug` 성공
  2. `dotnet build .\samples\HelloPlugin` → 설정 > 플러그인 > 플러그인 찾기…로 `bin\Debug\net8.0-windows\HelloPlugin.zip` 등록
  3. 설정 창을 다시 열면 `Hello 플러그인` 탭이 보이고, 시작 폴더를 입력·저장
  4. ⋯ 메뉴 > 플러그인 > Hello 플러그인 → 팝업에 그 폴더 패널, 파일 더블클릭 시 메인 창 뷰어 탭으로 열림
  5. 잘못된 zip(plugin.json 없음, type 오타)을 넣어도 메시지만 뜨고 앱 유지

- 3차: `dotnet test` 124 통과, 1 건너뜀(기존). 추가 6개(종료 기록 수거·삭제·설명, 로그, 본체 설정 키 목록). Linux 빌드 새 오류 없음, HelloPlugin 빌드 성공.
- **3차 Windows 확인 필요**:
  1. HelloPlugin을 실행하기 전 설정 창 → `Hello 플러그인` 안내 탭만 보이고 로드되지 않음(목록 상태 비어 있음)
  2. 실행 후 설정 창을 다시 열면 실제 설정 탭, 팝업에 `본체 테마: <현재 테마>`
  3. 팝업의 `테스트: Environment.Exit` → 종료 → 다시 시작하면 경고(원인: WPF 종료 절차 없이…, 호출한 플러그인: sample.hello 또는 "스택에서 찾지 못함"), `plugin-log.txt` 기록
  4. `테스트: Application.Shutdown` → 같은 방식으로 "Application.Shutdown이 호출됨" 알림
  5. 작업 관리자로 Folderss 강제 종료 → 다시 시작하면 "기록 없음 — 강제 종료로 추정" 알림
  6. 플러그인 실행 후 ⋯ > 종료로 정상 종료 → 다시 시작해도 알림 없음

- 4차: 튜토리얼 문서의 코드 블록 4개(csproj, plugin.json, OrderItem.cs, OrderFormPlugin.cs)를 추출해 `samples/OrderFormPlugin`에서 빌드 성공,
  zip에 `plugin.json` + `OrderFormPlugin.dll`만 포함, 본체 `PluginPackage.ReadManifest`로 검증 통과(`tutorial.order-form | 주문서`). 확인 후 임시 폴더 삭제.
  **Windows 확인 필요**: 튜토리얼 6단계 체크 항목(금액 재계산, 행 추가, 테마 전환 시 그리드 색, 창 두 개).

## 변경 이력

- 2026-09-30: 요청 접수, 설계 제안(형식 A/B, 폴더 패널 노출 방식 질의)
- 2026-09-30: 2차 요청 반영해 구현, Ready for Verification
- 2026-09-30: 3차 요청(본체 설정 읽기 전용, 실행 시점 로드, 종료 감지·로깅) 반영, Ready for Verification
- 2026-09-30: 4차 요청(개발 가이드·주문서 튜토리얼 문서) 작성

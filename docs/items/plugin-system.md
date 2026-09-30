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

## 원인 분석 또는 설계

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

## 변경 파일

- `Folderss.PluginContract/` (신규), `Folderss.sln`, `Folderss/Folderss.csproj`
- `Folderss/Services/PluginManager.cs`, `PluginPackage.cs`, `PluginSettingsStore.cs` (신규)
- `Folderss/App.xaml.cs`, `MainWindow.xaml(.cs)`, `SettingsWindow.xaml(.cs)`
- `samples/HelloPlugin/` (신규)
- `tests/Folderss.SearchTests/PluginPackageTests.cs` (신규), `Folderss.SearchTests.csproj`
- `README.md`, `docs/architecture.md`, `CLAUDE.md`, `AGENTS.md`

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

## 변경 이력

- 2026-09-30: 요청 접수, 설계 제안(형식 A/B, 폴더 패널 노출 방식 질의)
- 2026-09-30: 2차 요청 반영해 구현, Ready for Verification

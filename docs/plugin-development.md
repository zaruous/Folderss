# Folderss 플러그인 개발 가이드

Folderss 플러그인은 **WPF 화면을 가진 .NET 8 DLL**을 zip으로 묶은 것입니다.
사용자가 `⋯ 메뉴 > 플러그인`에서 실행하면 Folderss가 zip을 풀어 DLL을 로드하고, 플러그인이 만든 화면을 팝업 창에 띄웁니다.

처음이라면 [주문서 튜토리얼](plugin-tutorial-order-form.md)을 먼저 따라 해 보세요. 이 문서는 참고용 설명서입니다.

---

## 1. 한눈에 보기

```
OrderFormPlugin.zip
├── plugin.json            ← 플러그인 정보 (메뉴 이름, 진입점)
└── OrderFormPlugin.dll    ← IFolderssPlugin 구현
```

| 시점 | Folderss가 하는 일 | 플러그인 코드 실행 |
|---|---|---|
| 설정 > 플러그인 > `플러그인 찾기…` | `plugin.json`을 검증하고 zip을 `%LOCALAPPDATA%\Folderss\plugins\<id>.zip`으로 복사 | 없음 |
| `⋯ 메뉴 > 플러그인` 열기 | 등록된 zip의 `plugin.json`에서 `name`을 읽어 메뉴를 만든다 | 없음 |
| 설정 창 열기 | 이미 실행한 플러그인은 등록한 설정 탭, 아직 실행하지 않았고 `hasSettings: true`면 안내 탭 | 이미 실행한 경우 `IPluginSettingsPage`의 `Title`·`CreateView()`만 |
| **메뉴에서 플러그인 실행 (처음)** | zip 해제 → DLL 로드 → 인스턴스 생성 → `Initialize(manager)` → `CreateView()` → 팝업 | `Initialize`, `CreateView` |
| 메뉴에서 다시 실행 | 로드된 인스턴스를 그대로 쓴다 → `CreateView()` → 새 팝업 | `CreateView` |
| Folderss 종료 | 로드된 플러그인은 종료할 때까지 메모리에 남는다 | 없음 (종료 알림 없음) |

**플러그인 DLL은 사용자가 메뉴에서 직접 실행할 때만 로드됩니다.** 메뉴 이름과 설정 창 안내 탭은 `plugin.json`만 읽어 만듭니다.

---

## 2. 준비물

- .NET 8 SDK
- Windows (WPF, `net8.0-windows`)
- 계약 DLL `Folderss.PluginContract.dll`. 둘 중 하나로 참조합니다.
  - Folderss 소스가 있으면 `Folderss.PluginContract\Folderss.PluginContract.csproj`를 프로젝트 참조
  - 소스가 없으면 Folderss 설치 폴더의 `Folderss.PluginContract.dll`을 파일 참조

---

## 3. 프로젝트 설정 (`.csproj`)

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>disable</Nullable>
  </PropertyGroup>

  <!-- (A) Folderss 소스가 있을 때: 프로젝트 참조 -->
  <ItemGroup>
    <ProjectReference Include="..\..\Folderss.PluginContract\Folderss.PluginContract.csproj" Private="false" />
  </ItemGroup>

  <!-- (B) 설치본만 있을 때: 파일 참조 (A 대신 사용)
  <ItemGroup>
    <Reference Include="Folderss.PluginContract">
      <HintPath>C:\Program Files\Folderss\Folderss.PluginContract.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
  -->

  <!-- 빌드할 때마다 bin\<구성>\net8.0-windows\<프로젝트>.zip 생성 -->
  <Target Name="PackPlugin" AfterTargets="Build">
    <PropertyGroup>
      <PluginStage>$(IntermediateOutputPath)plugin-stage\</PluginStage>
    </PropertyGroup>
    <RemoveDir Directories="$(PluginStage)" />
    <Copy SourceFiles="plugin.json;$(TargetPath)" DestinationFolder="$(PluginStage)" />
    <ZipDirectory SourceDirectory="$(PluginStage)" DestinationFile="$(OutDir)$(AssemblyName).zip" Overwrite="true" />
  </Target>

</Project>
```

- **`Private="false"`는 꼭 넣으세요.** 계약 DLL을 zip에 넣지 않기 위해서입니다. 넣더라도 Folderss는 본체의 계약 DLL을 쓰므로 동작은 같지만, zip 크기만 늘어납니다.
- 다른 NuGet 패키지를 쓰면 그 DLL도 zip에 넣어야 합니다. `Copy`의 `SourceFiles`에 추가하거나, `$(OutDir)`의 DLL을 통째로 복사하세요. 플러그인 폴더의 DLL은 그 플러그인 전용 로드 영역에 올라갑니다.

---

## 4. `plugin.json`

zip **루트**에 있어야 합니다. 키 대/소문자는 구분하지 않고, 주석과 끝 쉼표를 허용합니다.

```json
{
  "id": "tutorial.order-form",
  "name": "주문서",
  "version": "1.0.0",
  "description": "타이틀과 그리드로 만든 주문서 튜토리얼",
  "assembly": "OrderFormPlugin.dll",
  "type": "OrderFormPlugin.OrderFormPlugin",
  "hasSettings": false
}
```

| 키 | 필수 | 설명 |
|---|---|---|
| `id` | ✔ | 고유 ID. 영문·숫자로 시작하고, 영문·숫자·`.`·`_`·`-`만 써서 최대 64자. 등록 파일 이름(`<id>.zip`)과 설정 폴더 이름으로 쓰인다. **한 번 배포하면 바꾸지 마세요.** 바꾸면 다른 플러그인으로 취급되어 설정이 이어지지 않습니다 |
| `name` |  | `⋯ 메뉴 > 플러그인`의 메뉴 이름이자 팝업 창 제목. 없으면 `id` |
| `version` |  | 표시용 (설정 > 플러그인 목록, 메뉴 툴팁) |
| `description` |  | 메뉴 툴팁 |
| `assembly` | ✔ | 진입점 DLL의 zip 내 상대 경로 (`.dll`, `..` 불가). 예: `OrderFormPlugin.dll`, `bin/OrderFormPlugin.dll` |
| `type` | ✔ | `IFolderssPlugin`을 구현한 클래스의 **네임스페이스 포함 전체 이름** |
| `hasSettings` |  | `true`면 아직 실행하지 않은 플러그인도 설정 창에 안내 탭이 보인다 (로드하지는 않음) |

같은 `id`의 zip을 다시 등록하면 교체됩니다. 이미 로드된 플러그인이면 교체한 버전은 **재시작 후** 적용됩니다.

---

## 5. 진입점: `IFolderssPlugin`

```csharp
using Folderss.Plugins;
using System.Windows;

public sealed class MyPlugin : IFolderssPlugin      // public, 인수 없는 public 생성자 필요
{
    private IPluginManager _manager;

    public void Initialize(IPluginManager manager)  // 처음 로드할 때 한 번
    {
        _manager = manager;
        // 설정 탭이 필요하면: manager.AddSettingsPage(new MySettingsPage(manager));
    }

    public FrameworkElement CreateView()             // 메뉴에서 실행할 때마다
    {
        return new TextBlock { Text = "Hello" };     // 팝업 창의 Content가 된다
    }
}
```

- `CreateView()`는 실행할 때마다 **새 요소**를 돌려주세요. 이미 다른 창에 붙은 요소를 다시 돌려주면 WPF가 예외를 냅니다(요소는 부모를 하나만 가질 수 있음).
- 팝업 창은 Folderss가 만듭니다. 제목은 `name`, 크기는 900×600(최소 320×240)이고, 메인 창 가운데에 비모달로 뜨며 배경·글자색·글꼴은 현재 테마를 따릅니다. 지금은 플러그인이 창 크기나 제목을 정할 수 없습니다.
- 모든 호출은 UI 스레드에서 옵니다. 오래 걸리는 작업은 `Task.Run` 등으로 넘기고, 결과는 `Dispatcher`로 UI에 반영하세요.

---

## 6. 본체가 제공하는 기능: `IPluginManager`

`Initialize`에서 받습니다. 플러그인마다 별도 인스턴스입니다.

| 멤버 | 설명 |
|---|---|
| `PluginId` | `plugin.json`의 `id` |
| `PluginDirectory` | zip을 푼 폴더. 함께 넣은 리소스 파일(이미지, JSON 등)을 여기서 읽는다. **쓰지 마세요** — zip을 교체하면 새 폴더에 풀려서, 여기 쓴 파일은 따라오지 않는다. 쓰기는 `DataDirectory`에 |
| `DataDirectory` | 플러그인 전용 쓰기 폴더 `%LOCALAPPDATA%\Folderss\plugin-data\<id>\`. 처음 접근할 때 만들어진다 |
| `GetSetting(key)` / `SetSetting(key, value)` / `GetAllSettings()` | 플러그인 자기 설정(문자열 키/값). `SetSetting`은 바로 `settings.json`에 저장하고, 실패하면 예외를 던진다. `value`가 `null`이면 지운다 |
| `GetAppSettings()` | 본체 설정의 **읽기 전용 복사본** (아래 표). 부를 때마다 저장된 값을 새로 읽는다. 다른 플러그인의 설정은 들어 있지 않다 |
| `CreateFolderPanel(path)` | Folderss와 같은 폴더 패널을 만든다 (아래 7절) |
| `AddSettingsPage(page)` | 설정 창에 탭을 추가한다 (아래 8절). `Initialize`에서 호출 |

### `GetAppSettings()` 키

| 키 | 예 |
|---|---|
| `theme` | `Black`, `Light`, `Nord`, `Catppuccin`, `Solarized`, `Dracula`, `GitHub` |
| `git.executablePath` | `C:\Program Files\Git\cmd\git.exe` (비어 있으면 자동 탐색) |
| `git.baseFolderMode`, `git.pullMode` | 선택 값의 이름. 예: `FastForwardOnly` |
| `git.scanDepth`, `git.logLimit` | 숫자 문자열 |
| `git.excludedFolders` | 줄바꿈(`\n`)으로 구분한 목록 |
| `git.logAllBranches`, `diff.ignoreWhitespace` | `true` / `false` |
| `diff.fallbackEncoding`, `diff.viewMode`, `diff.toolMode`, `diff.toolPath`, `diff.toolArguments` | 비교 설정 |
| `console.preferredProfileKey`, `console.fontSize` | 콘솔 설정 |

값은 모두 문자열입니다. 키가 없을 수 있다고 가정하고 `TryGetValue`로 읽으세요.

---

## 7. 폴더 패널: `IFolderPanel`

```csharp
var panel = _manager.CreateFolderPanel(@"C:\work");   // 없는 경로면 사용자 폴더
root.Children.Add(panel.View);                        // 화면에 붙이기
panel.PathChanged += (s, e) => title.Text = panel.CurrentPath;
var selected = panel.SelectedPaths;                   // 선택한 항목 전체 경로
panel.NavigateTo(@"D:\");
```

- 파일을 더블클릭하면 메인 창의 뷰어 탭으로 열립니다.
- 메인 창의 "활성 패널"과는 연결되지 않습니다. `⋯ 메뉴` 명령은 팝업 속 패널에 적용되지 않고, 두 파일 비교 메뉴도 동작하지 않습니다.
- `View`는 한 곳에만 붙일 수 있습니다. 팝업마다 `CreateFolderPanel`을 새로 부르세요.

---

## 8. 설정 탭: `IPluginSettingsPage`

```csharp
public void Initialize(IPluginManager manager)
{
    _manager = manager;
    manager.AddSettingsPage(new MySettingsPage(manager));
}

sealed class MySettingsPage : IPluginSettingsPage
{
    private readonly IPluginManager _manager;
    private TextBox _box;
    public MySettingsPage(IPluginManager manager) { _manager = manager; }

    public string Title => "내 플러그인";                  // 설정 창 왼쪽 목록 이름

    public FrameworkElement CreateView()                   // 설정 창을 열 때마다 새로 만든다
    {
        _box = new TextBox { Text = _manager.GetSetting("greeting") ?? "" };
        return _box;
    }

    public void Save()                                     // 설정 창 [저장]을 누를 때
    {
        _manager.SetSetting("greeting", _box.Text);        // 실패하면 예외 → "설정 저장 실패"에 표시
    }
}
```

- 탭은 **플러그인을 한 번 실행한 뒤** 설정 창을 열어야 보입니다(그 전에는 로드하지 않음). 사용자가 헷갈리지 않게 `plugin.json`에 `"hasSettings": true`를 두면 실행 전에도 안내 탭이 보입니다.
- 설정 창에서 `취소`를 누르면 `Save`가 호출되지 않습니다. 편집 중인 값은 화면 요소에만 두고, `Save`에서 저장하세요.

---

## 9. 테마 맞추기

팝업 창의 배경·글자색은 자동으로 테마를 따르지만, 컨트롤 색은 직접 연결해야 하는 경우가 있습니다(특히 `DataGrid`는 Folderss 테마 스타일이 없어서 밝은 기본 모양이 나옵니다).
색을 고정값으로 넣지 말고 **Folderss 테마 리소스 키**에 `DynamicResource`로 연결하세요. 그래야 테마를 바꿀 때 함께 바뀝니다.

```csharp
grid.SetResourceReference(Control.BackgroundProperty, "PanelBackground");
grid.SetResourceReference(Control.ForegroundProperty, "PrimaryText");
```

사용 가능한 키: `WindowBackground`, `PanelBackground`, `SurfaceBackground`, `ControlBackground`, `ControlHoverBrush`, `ControlPressedBrush`,
`BorderBrush`, `PrimaryText`, `SecondaryText`, `DisabledTextBrush`, `AccentBrush`, `AccentHoverBrush`, `SelectionBrush`, `RowHoverBrush`, `AppFontFamily`

---

## 10. 오류 처리와 Folderss 보호

| 상황 | 결과 |
|---|---|
| zip·`plugin.json` 오류, `type` 오타, DLL 누락 | 등록·실행 시 메시지. Folderss는 계속 실행 |
| `Initialize`·`CreateView` 예외 | "플러그인을 열지 못했습니다" 메시지. 다음 실행 때 다시 시도 |
| 설정 탭 `CreateView` 예외 | 그 탭에 오류 문구 표시 |
| 설정 탭 `Save` 예외 | "설정 저장 실패" 메시지에 함께 표시 |
| 플러그인 이벤트 처리기 등 UI 스레드 예외 | 한 번 알리고(같은 플러그인의 이후 오류는 알리지 않음) 계속 실행. `plugin-log.txt`에 매번 기록 |
| 플러그인이 만든 **별도 스레드**의 처리되지 않은 예외 | **Folderss가 종료됩니다**(.NET 규칙). 원인은 로그에 남고 다음 시작 때 알림 |
| `Application.Current.Shutdown()`, `Environment.Exit()` | 막을 수 없습니다. 로그에 남고 다음 시작 때 알림 |
| 스택 오버플로, `Environment.FailFast`, 프로세스 강제 종료, 네이티브 크래시 | 막을 수 없습니다. 다음 시작 때 "비정상 종료 + 로드된 플러그인"으로 알림 |

그래서 플러그인 개발자는 다음을 지켜야 합니다.

- `Task.Run`, `Thread`, 타이머 콜백 안에서는 **반드시 `try/catch`** 하세요. `async void` 이벤트 처리기도 마찬가지입니다.
- `Application.Current.Shutdown()`, `Environment.Exit()`, `MainWindow.Close()` 를 부르지 마세요. 참고로 메인 창 `Close()`는 트레이로 숨기기만 합니다.
- 팝업 창을 닫고 싶으면 `Window.GetWindow(view)?.Close()`를 쓰세요.

로그와 파일 위치 (`%LOCALAPPDATA%\Folderss\` 기준):

| 경로 | 내용 |
|---|---|
| `plugins\<id>.zip` | 등록된 플러그인 |
| `plugins\extracted\<id>-<해시>\` | 압축을 푼 폴더 (= `PluginDirectory`) |
| `plugin-data\<id>\settings.json` | `SetSetting`으로 저장한 값 |
| `plugin-log.txt` | 플러그인 오류, 비정상 종료 기록 |

---

## 11. 디버깅

1. 플러그인을 빌드하고 zip을 등록합니다(처음 한 번).
2. Visual Studio에서 **디버그 > 프로세스에 연결**로 `Folderss.exe`에 붙습니다.
3. `⋯ 메뉴 > 플러그인`에서 실행하면 중단점에 걸립니다. 압축을 푼 폴더의 DLL이 로드되므로, 빌드 폴더에 `.pdb`가 있으면 심볼이 잡힙니다.
   심볼이 안 잡히면 `Copy`의 `SourceFiles`에 `$(OutDir)$(AssemblyName).pdb`를 추가해 zip에 넣으세요.

**코드를 고친 뒤에는** 다시 빌드한 zip을 등록하고 **Folderss를 재시작**해야 합니다. 이미 로드된 DLL은 내릴 수 없기 때문입니다.

---

## 12. 제약과 주의

- **권한**: 플러그인은 Folderss와 같은 권한으로 실행됩니다. 파일 시스템 접근, 프로그램 실행이 모두 가능하고 서명 검증도 없습니다. 사용자는 등록할 때 경고를 봅니다.
- **언로드 불가**: 한 번 로드하면 종료할 때까지 남습니다. 종료 알림(정리 콜백)도 없으니, 저장이 필요한 데이터는 바뀔 때마다 저장하세요.
- **UI 구성**: 검증된 방식은 **코드로 컨트롤을 만드는 것**입니다(예제와 튜토리얼이 모두 이 방식). XAML `UserControl`도 원리상 가능하지만, 전용 로드 영역에서의 XAML 리소스 로드는 아직 검증하지 않았습니다.
- **계약 버전**: 계약에는 멤버가 추가될 수 있습니다. 기존 멤버를 지우거나 바꾸는 변경은 계약 주 버전(현재 1)을 올릴 때만 합니다.
  - 낮은 계약으로 빌드한 플러그인은 높은 Folderss에서 동작합니다. **높은 계약으로 빌드한 플러그인은 낮은 Folderss에서 아예 로드되지 않습니다**
    (새 멤버를 쓰지 않아도 마찬가지 — .NET이 낮은 버전으로 대체하지 않음). Folderss는 설치할 때 이를 확인해 거부하고 업데이트를 안내합니다.
  - 그래서 플러그인은 **필요한 가장 낮은 계약으로 빌드**하고, 새 계약이 필요하면 그 계약을 담은 Folderss가 배포된 뒤에 릴리스하세요.
- **본체 내부 형식을 쓰지 마세요**: `Folderss.exe`를 참조해 `FolderBrowser` 등 본체 클래스를 직접 쓰면, 본체가 바뀔 때마다 깨집니다. 계약 인터페이스만 쓰세요.

---

## 13. 배포 전 체크리스트

- [ ] zip 루트에 `plugin.json`과 `assembly` DLL이 있다 (계약 DLL은 없어도 됨)
- [ ] `type`이 네임스페이스 포함 전체 이름이고, 그 클래스가 `public`이며 인수 없는 생성자가 있다
- [ ] `id`가 규칙에 맞고, 이전 버전과 같다
- [ ] `CreateView()`가 매번 새 요소를 돌려준다
- [ ] 백그라운드 작업에 `try/catch`가 있다
- [ ] 다른 테마(Black/Light)로 바꿔도 글자가 읽힌다
- [ ] 설정 탭을 쓴다면 `hasSettings: true`, `Save`에서 저장하고 실패는 예외로 던진다
- [ ] 계약 사본을 필요 이상으로 올리지 않았다 (높은 계약으로 빌드하면 낮은 Folderss에서 설치되지 않음)
- [ ] `GitHub에서 설치…`로 배포한다면: 공개 저장소의 **정식 릴리스**(Pre-release·초안 아님)에 플러그인 zip을 첨부했다.
      사용자는 최신 정식 릴리스를 받으므로, 잘못 낸 릴리스는 삭제하거나 Pre-release로 바꾸면 이전 릴리스가 다시 최신이 된다

## 참고

- 예제: `samples/HelloPlugin` (폴더 패널, 본체 설정 읽기, 설정 탭, 종료 감지 테스트 버튼)
- 튜토리얼: [주문서 플러그인 만들기](plugin-tutorial-order-form.md)
- 계약 원본: `Folderss.PluginContract/PluginContracts.cs`
- 본체 구현: `docs/architecture.md`의 "PluginManager / PluginPackage" 절

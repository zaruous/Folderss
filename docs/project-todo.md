# GitHub Project Todo Items

- Source: https://github.com/users/zaruous/projects/1
- Downloaded: 2026-06-27 10:37:25 +09:00
- Status: Ready for Verification
- Count: 1

---

- hterm과 같은 라이브리럴 활용한 터미널 구현으로 검토할것. 2026-06-27 11:01


## 콘솔 메뉴 추가 방안 및 구현 계획

- Project item ID: PVTI_lAHOBuWMKc4ALrh3zgw_llQ

# 콘솔 메뉴 추가 방안 및 구현 계획

## 1. 목적

Folderss 안에서 현재 폴더를 작업 디렉터리로 사용하는 PowerShell 또는 명령 프롬프트를 실행하고, 출력 확인과 명령 입력을 별도 창 전환 없이 수행할 수 있게 한다.

이 문서에서 말하는 **시스템 메뉴**는 `MainWindow.xaml`의 상단 `MainContextMenu`를 뜻한다. 1차 구현은 Windows 10/11과 .NET Framework 4.8을 대상으로 한다.

## 2. 권장안

`보기 > 콘솔` 메뉴로 하단 AvalonDock 패널을 표시하거나 활성화한다. 콘솔 패널은 앱과 수명이 같은 단일 세션을 사용한다. 기본 셸은 Windows에 포함된 Windows PowerShell 5.1(`powershell.exe`)로 하고, 명령 프롬프트(`cmd.exe`)와 설치된 PowerShell 7 이상(`pwsh.exe`)도 선택할 수 있게 한다.

```text
MainContextMenu > 보기 > 콘솔
                 │
                 ▼
       MainWindow.ShowConsolePanel()
                 │
                 ▼
     LayoutAnchorable (ContentId: console)
                 │
                 ▼
          ConsolePanel (WPF UI)
                 │
                 ▼
       ConsoleSessionService
                 │
                 ▼
선택한 셸의 표준 입력/출력/오류 리디렉션
```

### 이 방식을 선택하는 이유

- 기존 즐겨찾기 패널과 동일한 `LayoutAnchorable` 패턴을 재사용할 수 있다.
- 패널의 위치, 크기, 숨김 상태를 기존 `DockLayoutService`로 저장·복원할 수 있다.
- 프로세스 제어와 화면 표시를 분리해 종료, 재시작, 출력 테스트가 단순해진다.
- 외부 터미널 실행만 제공하는 방식보다 앱 내부 작업 흐름을 유지할 수 있다.

## 3. 사용자 동작

### 기본 흐름

1. 사용자가 `보기 > 콘솔`을 선택한다.
2. 숨겨진 콘솔 패널이 있으면 표시하고 포커스를 입력란으로 이동한다.
3. 세션이 없거나 종료되었으면 활성 폴더를 작업 디렉터리로 선택된 셸을 시작한다.
4. 사용자가 명령을 입력하고 `Enter`를 누르면 표준 입력으로 전송한다.
5. 표준 출력과 표준 오류를 수신 순서대로 출력 영역에 추가한다.

### UI 구성

- 읽기 전용 출력 영역: 명령 결과, 오류, 종료 안내 표시
- 한 줄 명령 입력란: `Enter` 실행, `Up/Down` 명령 이력 탐색
- 셸 선택: `Windows PowerShell`, `PowerShell 7`(설치된 경우), `명령 프롬프트`
- 도구 버튼: `지우기`, `재시작`, `현재 폴더로 이동`
- 외부 터미널 버튼: TTY가 필요한 프로그램 실행용
- 상태 표시: `실행 중`, `종료됨`, 현재 작업 디렉터리

출력은 설정 가능한 최대 줄 수를 두고 오래된 줄부터 제거해 장시간 실행 시 메모리 증가를 제한한다. 기본값은 5,000줄이며 `설정 > 콘솔`에서 변경한다.

## 4. 작업 디렉터리 정책

- 최초 시작 경로는 `MainWindow.ActivePane.CurrentPath`를 사용한다.
- 경로가 없거나 접근할 수 없으면 사용자 프로필 폴더로 대체한다.
- 폴더 패널 이동만으로 실행 중인 콘솔의 작업 디렉터리를 암묵적으로 바꾸지 않는다.
- `현재 폴더로 이동`은 셸별 명령을 사용한다.
  - PowerShell: `Set-Location -LiteralPath '<경로>'`(작은따옴표는 두 번 이스케이프)
  - 명령 프롬프트: `cd /d "<경로>"`

암묵적 동기화를 피하는 이유는 실행 중인 빌드나 스크립트의 작업 문맥이 사용자의 파일 탐색에 의해 예기치 않게 바뀌는 것을 막기 위해서다.

## 5. 설계 상세

### `ConsoleSessionService`

새 파일 `Services/ConsoleSessionService.cs`에 프로세스 수명과 스트림 처리를 둔다. 셸 종류는 `ConsoleShellKind` 열거형으로 관리하고 실행 파일 탐색과 셸별 인수를 한 곳에서 결정한다.

- `GetAvailableShells()`: 실행 가능한 셸 목록 반환
- `Start(ConsoleShellKind shell, string workingDirectory)`: `ProcessStartInfo` 구성 및 프로세스 시작
- `SendCommand(string command)`: 명령과 개행을 표준 입력에 기록
- `Stop()`: 입력 스트림을 닫고 제한 시간 후에도 남으면 프로세스 종료
- `Restart(ConsoleShellKind shell, string workingDirectory)`: 기존 프로세스 정리 후 선택한 셸로 새 세션 시작
- 이벤트: `OutputReceived`, `ErrorReceived`, `Exited`, `StateChanged`
- `IDisposable` 구현 및 중복 종료 방지

프로세스 설정은 다음을 사용한다.

```text
FileName               = powershell.exe / pwsh.exe / %COMSPEC%
UseShellExecute        = false
CreateNoWindow         = true
RedirectStandardInput  = true
RedirectStandardOutput = true
RedirectStandardError  = true
WorkingDirectory       = 검증된 활성 폴더
```

셸 탐색 우선순위는 다음과 같다.

1. 사용자 설정으로 저장된 셸이 현재 실행 가능하면 사용
2. Windows PowerShell 5.1(`powershell.exe`)
3. 명령 프롬프트(`%COMSPEC%`, 없으면 `cmd.exe`)

PowerShell 7(`pwsh.exe`)은 Windows 기본 구성 요소가 아니므로 `PATH` 또는 알려진 설치 경로에서 발견된 경우에만 선택 목록에 표시한다. 셸 변경 시 실행 중인 프로세스와 상태가 사라진다는 확인을 받은 뒤 새 세션을 시작한다.

PowerShell 시작 시 `-NoLogo -NoProfile -NoExit`을 기본 인수로 사용한다. 재현 가능한 시작과 사용자 프로필 스크립트의 예기치 않은 출력·지연을 피하기 위한 정책이며, 향후 설정에서 프로필 로드 여부를 선택할 수 있게 확장한다.

마지막으로 선택한 셸과 최대 출력 줄 수는 `%LOCALAPPDATA%\Folderss\console-settings.xml`에 저장한다. 최대 출력 줄 수의 기본값은 5,000줄이다. 저장된 실행 파일이 제거되었으면 위 우선순위로 대체하고 설정을 갱신한다.

표준 출력과 오류는 `BeginOutputReadLine()` 및 `BeginErrorReadLine()`으로 비동기 수신한다. 이벤트는 작업 스레드에서 발생하므로 `ConsolePanel.Dispatcher`를 통해 UI를 갱신한다. UI 스레드에서 `ReadToEnd()`나 `WaitForExit()`를 호출하지 않는다.

### `ConsolePanel`

새 파일 `Controls/ConsolePanel.xaml`과 `Controls/ConsolePanel.xaml.cs`에 화면과 상호작용을 둔다.

- 서비스 이벤트 구독과 해제
- 출력 버퍼 및 최대 줄 수 관리
- `ConsoleSettingsService.MaxOutputLineCount`를 사용한 출력 줄 제한
- 입력 전송과 명령 이력 관리
- 재시작 확인 및 상태 표시
- 테마 리소스(`WindowBackground`, `PrimaryTextBrush`, `BorderBrush`, `AccentBrush`) 사용
- 패널이 닫혀도 세션 유지, 앱 종료 시에만 정리

1차 구현은 ANSI/VT 이스케이프 시퀀스와 커서 기반 전체 화면 앱을 지원하지 않는다. 색상 제어 문자는 제거하거나 일반 텍스트로 표시한다. PowerShell 명령과 파이프라인은 실행할 수 있지만 PSReadLine 기반 편집, 대화형 선택 UI, `vim` 같은 TTY 프로그램을 완전히 지원하지 않는다. 완전한 터미널 호환성이 필요하면 후속 단계에서 ConPTY 기반으로 교체한다.

`claude`, `codex`, `gemini`, `vim`, `ssh`처럼 TTY가 필요한 명령은 표준 입출력 리디렉션 콘솔에서 실패하므로, 입력 시 외부 터미널로 실행을 넘긴다.

### `MainWindow`

- `MainWindow.xaml`의 `보기` 하위에 `콘솔` 메뉴 항목 추가
- 기본 레이아웃 하단에 `ContentId="console"`인 `LayoutAnchorable` 추가
- `ShowConsole_Click`에서 숨김/도킹/플로팅 상태를 `FindDock("console")`로 찾아 `Show()` 및 활성화
- `ResolveDockContent`에 `console` 매핑을 추가해 저장된 도킹 레이아웃 복원
- `Window_Closing`에서 콘솔 세션을 해제
- 메뉴를 열 때 콘솔 표시 상태와 체크 표시 동기화

기본 크기는 하단 높이 220px를 권장한다. `CanHide=True`, `CanClose=True`, `CanAutoHide=True`로 기존 패널 사용성을 따른다.

### 단축키

1차 구현에서는 메뉴 접근만 제공한다. 단축키가 필요하면 2차 단계에서 다음을 함께 변경한다.

- `KeyBindingService.GetDefaults()`에 `ShowConsole` 추가
- `MainWindow.Window_PreviewKeyDown`에 명령 처리 추가
- 설정 창 단축키 목록에 자동 노출되는지 확인
- 기본값 후보: `Ctrl+``. 한국어 IME와 키보드 배열별 입력을 반드시 검증하고 충돌 시 기본값을 지정하지 않는다.

## 6. 변경 대상 파일

| 파일 | 변경 내용 |
|---|---|
| `Folderss/MainWindow.xaml` | 보기 메뉴와 하단 콘솔 도킹 패널 추가 |
| `Folderss/MainWindow.xaml.cs` | 표시, 초기화, 레이아웃 복원, 종료 처리 |
| `Folderss/Controls/ConsolePanel.xaml` | 출력·입력·도구 모음 UI |
| `Folderss/Controls/ConsolePanel.xaml.cs` | UI 이벤트, 출력 버퍼, 명령 이력 |
| `Folderss/Services/ConsoleSessionService.cs` | PowerShell/cmd 탐색, 프로세스 및 스트림 수명 관리 |
| `Folderss/Services/ConsoleSettingsService.cs` | 기본 셸 선택, 최대 출력 줄 수 저장 및 복원 |
| `Folderss/SettingsWindow.xaml` | 콘솔 설정 항목과 최대 출력 줄 수 입력 추가 |
| `Folderss/SettingsWindow.xaml.cs` | 콘솔 설정 로드, 검증, 저장 처리 |
| `Folderss/Folderss.csproj` | 신규 XAML/코드 파일 등록 |
| `Folderss/Services/KeyBindingService.cs` | 선택 사항: `ShowConsole` 단축키 등록 |
| `README.md` | 사용자 기능과 제한 사항 안내 |
| `docs/architecture.md` | 콘솔 패널과 세션 서비스 역할 반영 |

## 7. 구현 단계

### 1단계: 프로세스 계층

- [x] `ConsoleSessionService` 구현
- [x] 작업 디렉터리 검증 및 폴백 구현
- [x] Windows PowerShell, PowerShell 7, 명령 프롬프트 탐색 및 선택 구현
- [x] 마지막 셸 선택 저장과 사용할 수 없는 셸의 폴백 구현
- [x] 설정 창에 `콘솔` 항목과 최대 출력 줄 수 설정 추가
- [x] 최대 출력 줄 수 기본값 5,000줄 저장/복원 구현
- [x] 출력/오류 비동기 수신, 종료 및 재시작 구현
- [x] 중복 시작, 이미 종료된 프로세스, 앱 종료 경합 처리

### 2단계: 콘솔 UI

- [x] `ConsolePanel` XAML과 코드비하인드 구현
- [x] 명령 실행, 이력 탐색, 출력 지우기 구현
- [x] 설정된 최대 출력 줄 수 제한과 자동 스크롤 구현
- [x] 실행 상태에 따른 버튼과 입력 활성화 처리
- [x] TTY 필요 명령 감지 및 외부 터미널 실행 연결

### 3단계: 메뉴 및 도킹 통합

- [x] `보기 > 콘솔` 메뉴 추가
- [x] 하단 `LayoutAnchorable`과 `ContentId="console"` 추가
- [x] 숨김 패널 재표시 및 입력 포커스 처리
- [x] `ResolveDockContent`와 도킹 레이아웃 저장·복원 연결
- [x] 앱 종료 시 세션 정리

### 4단계: 품질 및 문서화

- [ ] 모든 테마에서 가독성 확인
- [x] 디버그 빌드 확인
- [x] README와 아키텍처 문서 갱신
- [ ] 필요 시 사용자 지정 단축키 추가

## 8. 검증 항목

### 기능

- [ ] 메뉴 선택 시 콘솔이 하단에 열리고 입력란에 포커스가 간다.
- [ ] `설정 > 콘솔`의 기본 최대 출력 줄 수가 5,000으로 표시되고 저장 후 복원된다.
- [ ] `echo`, `dir`, 오류 명령의 출력과 오류가 멈춤 없이 표시된다.
- [ ] `claude` 같은 TTY 필요 명령은 내장 콘솔 오류 대신 외부 터미널에서 실행된다.
- [ ] 한글, 공백, 특수문자가 포함된 경로에서 PowerShell `Set-Location`과 cmd `cd /d`가 동작한다.
- [ ] Windows PowerShell과 명령 프롬프트를 전환할 수 있다.
- [ ] PowerShell 7은 설치된 환경에서만 선택 항목으로 나타난다.
- [ ] 다른 드라이브로 작업 디렉터리를 변경할 수 있다.
- [ ] 숨겼다가 다시 표시해도 기존 세션과 출력이 유지된다.
- [ ] 종료된 셸을 재시작할 수 있다.
- [ ] 도킹, 자동 숨김, 플로팅 배치가 재시작 후 복원된다.

### 안정성

- [ ] 대량 출력 중 UI가 멈추지 않고 메모리가 계속 증가하지 않는다.
- [ ] stdout/stderr 동시 출력에서 교착이 발생하지 않는다.
- [ ] 앱 종료 시 백그라운드 `powershell.exe`, `pwsh.exe`, `cmd.exe`가 남지 않는다.
- [ ] 콘솔 프로세스가 먼저 종료되어도 앱이 예외 없이 계속 동작한다.
- [ ] 잘못되었거나 삭제된 활성 폴더에서 안전한 폴백 경로로 시작한다.

### 회귀

- [ ] 즐겨찾기와 검색 패널 표시가 기존대로 동작한다.
- [ ] 기본 도킹 배치 초기화 및 저장된 레이아웃 복원이 동작한다.
- [ ] 파일 패널 전역 단축키가 콘솔 입력 중 오작동하지 않는다.

## 9. 주요 위험과 대응

| 위험 | 대응 |
|---|---|
| 프로세스 출력 이벤트가 UI 종료 후 도착 | 이벤트 해제, disposed 플래그, Dispatcher 종료 상태 확인 |
| stdout/stderr 리디렉션 교착 | 두 스트림 모두 비동기 수신하고 UI 스레드 대기 금지 |
| 콘솔 입력 중 전역 단축키가 명령 입력을 가로챔 | `TextBox` 포커스 시 기존 전역 명령 처리 제외 정책 확인 및 보강 |
| 자식 프로세스가 앱 종료 후 잔류 | 정상 종료 시도 후 제한 시간 종료, 필요하면 Windows Job Object를 후속 적용 |
| 실제 터미널과 다른 동작 | 1차 범위를 라인 기반 셸로 명시하고 ConPTY 적용은 별도 과제로 분리 |
| 저장된 이전 레이아웃에 콘솔 항목이 없음 | 메뉴 실행 시 패널을 찾지 못하면 기본 하단 패널을 동적으로 생성하는 복구 경로 제공 |

## 10. 완료 기준

- `보기 > 콘솔`에서 단일 콘솔 패널을 열고 재표시할 수 있다.
- 활성 폴더 기준으로 기본 Windows PowerShell이 시작되며 셸 선택, 명령 입력, 출력, 오류, 재시작이 동작한다.
- UI 멈춤과 프로세스 잔류 없이 앱을 종료할 수 있다.
- 도킹 레이아웃 복원과 전체 테마에 회귀가 없다.
- 위 검증 항목과 빌드를 통과하고 사용자 문서가 갱신되어 있다.

## 11. 구현 기록

- 2026-06-27: 설정 창 `콘솔` 항목과 `ConsoleSettingsService`를 추가하고 최대 출력 줄 수 기본값 5,000줄을 저장/복원하도록 구현.
- 2026-06-27: `ConsoleSessionService`와 `ConsolePanel`을 추가해 Windows PowerShell, PowerShell 7, 명령 프롬프트 기반 라인 콘솔을 하단 도킹 패널로 표시하도록 구현.
- 2026-06-27: `보기 > 콘솔`, `ContentId="console"` 레이아웃 복원, 숨김 패널 재표시, 앱 종료 시 세션 정리를 연결.
- 2026-06-27: Debug MSBuild 성공. 실제 셸 상호작용과 도킹 복원은 사용자 확인 대기.
- 2026-06-27: `claude` 실행 시 TTY 미지원으로 오류가 발생하는 검증 결과를 반영해 TTY 필요 명령을 외부 터미널로 실행하도록 보완.

## 12. ConPTY 기반 터미널 전환 설계

### 12.1 목적

현재의 라인 기반 콘솔을 `ConPTY` 기반의 실제 터미널 패널로 전환한다. 목표는 `cmd`, `powershell`, `pwsh`뿐 아니라 `vim`, `ssh` 같은 TTY 의존 프로그램도 앱 내부에서 자연스럽게 동작시키는 것이다.

### 12.2 설계 요약

```text
MainWindow
  -> LayoutAnchorable(ContentId: console)
    -> ConsolePanel (WPF shell)
      -> WebView2
        -> xterm.js
          -> JS bridge
            -> ConPtySessionService
              -> CreatePseudoConsole / ResizePseudoConsole
              -> powershell.exe / pwsh.exe / cmd.exe
```

- UI 렌더링은 `xterm.js`가 담당한다.
- 프로세스 생명주기와 입출력 파이프는 `ConPtySessionService`가 담당한다.
- 기존 `ConsoleSessionService`는 라인 콘솔 fallback 또는 제거 대상으로 본다.
- `ConsolePanel`은 셸 선택, 상태 표시, 외부 터미널 버튼만 남기고 본문을 WebView2로 교체한다.

### 12.3 서비스 인터페이스 초안

```csharp
public enum TerminalSessionState
{
    NotStarted,
    Starting,
    Running,
    Stopped,
    Failed
}

public sealed class ConPtySessionService : IDisposable
{
    public event EventHandler<string> OutputReceived;
    public event EventHandler<TerminalSessionState> StateChanged;
    public event EventHandler<int> ExitReceived;

    public TerminalSessionState State { get; }
    public ConsoleShellKind ShellKind { get; }
    public string WorkingDirectory { get; }
    public int Columns { get; }
    public int Rows { get; }

    public void Start(ConsoleShellKind shellKind, string workingDirectory, int columns, int rows);
    public void WriteInput(string text);
    public void WriteInput(byte[] buffer, int offset, int count);
    public void Resize(int columns, int rows);
    public void ChangeDirectory(string path);
    public void Restart();
    public void Stop();
    public void Dispose();
}
```

필수 네이티브 연동은 다음 수준으로 분리한다.

```csharp
internal struct COORD
{
    public short X;
    public short Y;
}

internal struct STARTUPINFOEX
{
    public STARTUPINFO StartupInfo;
    public IntPtr lpAttributeList;
}

internal struct STARTUPINFO
{
    public int cb;
    public string lpReserved;
    public string lpDesktop;
    public string lpTitle;
    public int dwX;
    public int dwY;
    public int dwXSize;
    public int dwYSize;
    public int dwXCountChars;
    public int dwYCountChars;
    public int dwFillAttribute;
    public int dwFlags;
    public short wShowWindow;
    public short cbReserved2;
    public IntPtr lpReserved2;
    public IntPtr hStdInput;
    public IntPtr hStdOutput;
    public IntPtr hStdError;
}

internal struct PROCESS_INFORMATION
{
    public IntPtr hProcess;
    public IntPtr hThread;
    public int dwProcessId;
    public int dwThreadId;
}

internal static class ConPtyNative
{
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern int CreatePseudoConsole(COORD size, IntPtr hInput, IntPtr hOutput, uint dwFlags, out IntPtr phPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern void ResizePseudoConsole(IntPtr hPC, COORD size);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern void ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CreatePipe(out IntPtr hReadPipe, out IntPtr hWritePipe, IntPtr lpPipeAttributes, int nSize);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool CreateProcessW(
        string lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string lpCurrentDirectory,
        ref STARTUPINFOEX lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool InitializeProcThreadAttributeList(
        IntPtr lpAttributeList,
        int dwAttributeCount,
        int dwFlags,
        ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList,
        uint dwFlags,
        IntPtr Attribute,
        IntPtr lpValue,
        IntPtr cbSize,
        IntPtr lpPreviousValue,
        IntPtr lpReturnSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);
}
```

`CreatePseudoConsole` 계열은 바이트 스트림과 VT 시퀀스를 처리하는 전제이므로, 문자열 출력 로그를 누적하는 현재 구조와 직접 호환시키지 않는다.

- 실제 자식 프로세스 생성 시에는 `STARTUPINFOEX`와 `PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE` 조합으로 pseudo console 핸들을 전달한다.
- 구현 순서는 일반적으로 `CreatePipe` -> `CreatePseudoConsole` -> attribute list 초기화 -> `CreateProcessW` -> 핸들 정리 순서가 된다.

### 12.4 ConsolePanel 화면 설계

```text
┌──────────────────────────────────────────────────────────────────┐
│ 셸 선택  현재 폴더로 이동  재시작  지우기  외부 터미널           │
├──────────────────────────────────────────────────────────────────┤
│ 상태: Running | cwd: D:\work\folderss                           │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│                   WebView2 + xterm.js 터미널                     │
│                                                                  │
├──────────────────────────────────────────────────────────────────┤
│ 옵션: 스크롤백 / 글꼴 크기 / 복사 / 붙여넣기 / 테마               │
└──────────────────────────────────────────────────────────────────┘
```

- 기존 `CommandBox`는 제거하거나 개발자용 보조 입력으로 축소한다.
- 출력은 `TextBox`가 아니라 WebView2 안의 `xterm.js`가 렌더링한다.
- `ConsolePanel`은 현재 폴더와 셸 상태만 전달하고, 키 입력은 JS 브리지를 통해 세션 서비스로 보낸다.
- 패널을 닫아도 세션은 유지하고, 앱 종료 시에만 정리한다.

### 12.5 변경 파일 초안

| 파일 | 역할 |
|---|---|
| `Folderss/Controls/ConsolePanel.xaml` | WebView2 호스트와 상단 툴바 구성 |
| `Folderss/Controls/ConsolePanel.xaml.cs` | WebView2 초기화, 브리지, 상태 표시 |
| `Folderss/Services/ConPtySessionService.cs` | ConPTY 세션, 입출력, 리사이즈, 종료 관리 |
| `Folderss/Services/ConPtyNative.cs` | P/Invoke 래퍼와 네이티브 구조체 정의 |
| `Folderss/Controls/Resources/console-app.html` | xterm.js 로딩 진입점 |
| `Folderss/Controls/Resources/console-app.js` | 터미널 렌더링과 메시지 브리지 |
| `Folderss/Controls/Resources/console-app.css` | 터미널 외형과 레이아웃 스타일 |
| `Folderss/Folderss.csproj` | 새 리소스와 서비스 등록 |

### 12.6 검증 기준

- `cmd`, `powershell`, `pwsh`가 내부 터미널에서 정상 동작해야 한다.
- `vim`, `ssh` 같은 TTY 프로그램이 full-screen 동작해야 한다.
- ANSI 색상과 커서 이동이 깨지지 않아야 한다.
- 패널 크기 변경 시 터미널 레이아웃이 즉시 따라와야 한다.
- 패널을 닫았다 다시 열어도 세션이 유지되어야 한다.
- 앱 종료 시 자식 프로세스가 남지 않아야 한다.


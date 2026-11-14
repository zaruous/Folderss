# 본체 크래시 로그 (crash-log.txt)

- 상태: Ready for Verification (v1.9.3 포함)
- 계기: 블록 편집기(.md, `builtin:blocks`)에서 Ctrl+X 잘라내기 중 앱 종료(2026-11-14 07:57, Release 1.9.2.0).
  이벤트 뷰어에는 `Application Error` 1000 / WER 1001(예외 코드 0xe0434352 = 처리되지 않은 .NET 예외)만 있고
  `.NET Runtime` 1026(스택) 항목이 없었으며, WER 덤프(.mdmp)는 임시 폴더에서 이미 지워져 원인을 알 수 없었다.
  기존 `plugin-log.txt`는 플러그인을 로드한 실행·플러그인 예외만 기록한다.

## 구현
- `Services/CrashLog.cs`
  - `Install()`: `AppDomain.UnhandledException`(종료 훅), `TaskScheduler.UnobservedTaskException`(앱은 계속 실행, 기록만).
  - `WriteDispatcherException()`: `App.DispatcherUnhandledException`에서 플러그인 예외가 아니어서 처리되지 않을 때(곧 종료) 부른다.
    같은 예외가 AppDomain 훅으로 다시 오면 참조 비교로 한 번만 기록.
  - 기록 내용: 시각(ms)·본체 버전·pid·CLR·OS, 스레드(UI 여부), 키보드 포커스 요소 타입, 내부 예외 단계별 종류·메시지·HResult·Data,
    `Exception.ToString()` 전체, 기록 시점 호출 스택.
  - `FileOptions.WriteThrough`로 바로 디스크에 쓰고, 5MB를 넘으면 `crash-log.txt.old`로 교체. 기록 실패는 원래 예외를 가리지 않게 삼킨다(종료 경로).
- `App.xaml.cs`: `OnStartup` 맨 앞에서 `CrashLog.Install()` — 테마 적용 등 시작 단계 예외도 기록.

## 결정
- 본체 예외 동작은 바꾸지 않는다(기록 후 종료). 예외를 삼켜 계속 실행하면 편집 중 문서 상태가 깨진 채 저장될 수 있다.
- 다음 시작 때 알림은 넣지 않았다(필요하면 추가).

## 검증
- `dotnet build .\Folderss.sln -c Debug` — 오류 0, Exit 0.
- 빌드된 `Folderss.dll`을 불러 `CrashLog.Write`로 예외를 기록 → 예외 단계·스택·기록 시점 스택이 기록됨을 확인(확인 후 파일 삭제).
## 크래시 정보 창 (⋯ > 보기 > 크래시 정보…)
- `CrashReportWindow`: crash-log.txt(+.old), plugin-log.txt(+.old), 이벤트 뷰어 Application 로그의 Folderss.exe 항목
  (Application Error 1000 / WER 1001 / .NET Runtime 1026, 최근 30일, 최대 3000건 검사), 덤프 파일(`Folderss.exe*.dmp` — LocalDumps 폴더와 `%LOCALAPPDATA%\CrashDumps`)을
  최신순 한 목록으로. 왼쪽 목록(여러 개 선택) · 오른쪽 상세(고정폭, 읽기 전용).
- 버튼: 새로 고침, 선택 항목 복사(상세에서 일부를 골랐으면 그 부분), 전체 복사, 로그 폴더 열기, 덤프 보존 설정 명령 복사(LocalDumps 키 없을 때만).
- 복사 형식: 항목마다 `### 시각 [출처] 요약` 머리 줄 + 전체 내용 — 이슈·대화에 그대로 붙여넣기.
- 파싱은 `Services/CrashReportParser.cs`(순수 로직, `CrashReportParserTests`).
- `보기` 하위 메뉴는 `Tag="global"`이 아니라 플러그인 퍼스펙티브에서는 꺼진다(기존 보기 메뉴와 같음).

### 창 검증
- `dotnet test tests/Folderss.SearchTests --filter CrashReportParserTests` — 4개 통과.
- 빌드된 DLL에서 수집(`Collect`)을 실제 데이터로 실행 → 57개 항목: 07:57 크래시의 WER 1001·Application Error 1000,
  08:17 plugin-log 비정상 종료(pid 42372, 원인 기록 없음), 08:20 crash-log(콘솔 터미널 `TermPTY.ReadOutputLoop`의 관찰 안 된 Task 예외 — 크래시 아님),
  **덤프 `%LOCALAPPDATA%\CrashDumps\Folderss.exe.42372.dmp`(07:57:36, 55MB)** 가 함께 보임.

## 07:57 크래시 관찰
- 크래시한 실행(pid 42372, 2026-11-13 06:40:08 시작)은 플러그인을 로드해 기존 `AppDomain.UnhandledException` 훅이 있었는데도 원인이 기록되지 않았고 .NET Runtime 1026도 없다
  → 관리 예외가 네이티브 경계를 넘어 런타임의 처리되지 않은 예외 처리 없이 종료된 것으로 보인다. 이런 크래시는 crash-log.txt도 못 잡으므로 덤프로 확인해야 한다.
- 위 덤프를 `dotnet-dump analyze` → `pe -nested`, `clrstack`으로 분석해 Ctrl+X 원인을 찾는 것이 다음 단계.

## 원인 확정 — 실행 중 .NET 런타임 교체
- 덤프(`%LOCALAPPDATA%\CrashDumps\Folderss.exe.42372.dmp`)의 UTF-16 문자열: `The type initializer for 'MS.Internal.Text.InternalEncoding' threw an exception.`,
  `System.Text.Encoding.CodePages`, `…\Microsoft.NETCore.App\8.0.33\…`.
- 타임라인: 11/13 06:40 Folderss 시작(.NET 8.0.33) → 06:57~07:00 업데이트가 8.0.34 설치·**8.0.33 폴더 제거**(MsiInstaller 1034/11724)
  → 11/14 07:57 블록 편집기에서 처음 Ctrl+X → WPF RichTextBox 복사(RTF 변환)가 `InternalEncoding` 정적 생성자에서 아직 안 불러온
  `System.Text.Encoding.CodePages.dll`을 지워진 폴더에서 찾다 실패 → 종료. 기록 훅도 새 어셈블리를 못 불러와 아무것도 남기지 못한 것으로 본다.
- Ctrl+X 코드 버그가 아니다.

## 런타임 교체 감지·재시작 안내
- `Services/RuntimeWatch.cs`(순수 로직, `RuntimeWatchTests`): 감시할 런타임 파일(앱 폴더 안 = self-contained면 제외), 사라진 파일, 폴더 이름의 버전.
- `MainWindow.RuntimeWatch.cs`: `Window_Loaded`에서 시작. 1분마다 `System.Private.CoreLib`(NETCore.App)·`PresentationFramework`(WindowsDesktop.App) 파일 존재 확인.
  사라지면 crash-log에 기록하고 "다시 시작할까요?"(예/아니요). 예 → `RestartRequested` + `ExitApp()`(저장 안 한 문서·파일 작업 확인 그대로, 취소하면 재시작도 취소)
  → `App.OnExit` 끝에서 같은 실행 파일을 다시 띄움(apphost가 지금 설치된 런타임 선택). 아니요 → 30분 뒤 다시 알림.
  감지·안내 경로는 이미 로드된 어셈블리만 쓴다. 재시작용 `System.Diagnostics.Process`는 감시 시작 때 미리 불러 둔다.
- 미리 불러오기(감지 전 1분 사이에도 죽지 않게):
  - `App.OnStartup`: `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` — RichTextBox 복사·잘라내기의 `InternalEncoding`이 쓰는 어셈블리.
  - `CrashLog.Install`: 한가할 때 기록 경로(Build·스택 줄 번호·파일 쓰기)를 한 번 실행.
- 결정: 시스템 .NET 자동 업데이트는 끄지 않는다(보안). self-contained 배포는 크기 증가로 보류.

- 남은 것: Release 빌드로 Ctrl+X 크래시를 재현해 `crash-log.txt`로 원인 확인 → 잘라내기 크래시 수정은 별도 작업.

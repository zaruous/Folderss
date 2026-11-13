# 본체 크래시 로그 (crash-log.txt)

- 상태: Ready for Verification
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
- 남은 것: Release 빌드로 Ctrl+X 크래시를 재현해 `crash-log.txt`로 원인 확인 → 잘라내기 크래시 수정은 별도 작업.

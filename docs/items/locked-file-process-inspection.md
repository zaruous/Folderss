# 잠긴 파일의 사용 프로세스 확인과 종료

- 상태: Done

## 요구사항

- 사용자가 파일 또는 폴더를 지우려는데 다른 프로세스가 사용 중이라 실패한 경우, 어떤 프로세스가 관련됐는지 확인할 수 있어야 한다.
- 파일 패널에서 선택 항목의 사용 프로세스를 필요할 때 직접 조회할 수도 있어야 한다.
- 사용자가 원하면 확인된 프로세스에 정상 종료를 요청하고, 종료 후 원래 파일 작업을 재시도할 수 있어야 한다.
- 조회되지 않음, 권한 부족, 네트워크 파일, 다른 종류의 삭제 실패를 잠금으로 단정하지 않고 이유를 구분해 안내한다.
- zip 가상 경로에서는 조회/재시도를 적절히 막고, 폴더의 경우 Windows Restart Manager 미지원 제한을 명확히 안내한다.
- 시스템 핵심 프로세스, Windows 서비스, Folderss 자신은 종료할 수 없도록 안전하게 차단한다.
- 프로세스 종료 요청 전 PID 및 프로세스 시작 시각을 재검증해 PID 재사용 오작동을 방지한다.
- 삭제 재시도는 사용자의 명시적 클릭 시에만 기존 대기열 경로(`QueueFileDelete`)를 통해 실행한다.

## 원인 분석 또는 설계

- Windows Restart Manager P/Invoke(`RmStartSession`, `RmRegisterResources`, `RmGetList`, `RmEndSession`)를 활용하여 대상 파일을 점유 중인 프로세스 목록을 백그라운드 스레드에서 비동기 조회. 세션은 `finally`에서 반드시 종료 보장.
- `RmGetList` 호출 시 `ERROR_MORE_DATA`(234) 버퍼 부족 재시도 루프를 구성.
- 공유 위반(`ERROR_SHARING_VIOLATION` 32) 및 잠금 위반(`ERROR_LOCK_VIOLATION` 33)과 접근 거부(`ERROR_ACCESS_DENIED` 5)를 예외의 Win32 에러 코드(`HResult` 및 `Win32Exception`) 기반으로 명확히 구분.
- 폴더 경로는 Restart Manager에 직접 등록할 수 없으므로, 하위 파일 중 열기 시도(`FileShare.None`)가 실패하는 잠긴 파일을 탐색하여 진단하거나 폴더 자체 제한을 사용자에게 명확히 안내.
- 프로세스 안전성 판정:
  - Folderss 본체(`Environment.ProcessId`): 종료 비활성화
  - 시스템 핵심 프로세스(PID <= 4, `RM_APP_TYPE.RmCritical`, `csrss`, `services`, `lsass` 등): 종료 비활성화
  - Windows 서비스(`RM_APP_TYPE.RmService` 또는 서비스 단축명 보유): 종료 비활성화
  - 일반 UI 애플리케이션: `Process.CloseMainWindow()`로 정상 종료 요청 및 비동기 대기.
  - 강제 종료(`Process.Kill()`): 일반 애플리케이션에 한해 확인 경고 대화상자를 거쳐 실행.
- 재시도 연동:
  - `MainWindow.QueueFileDelete`를 대화상자의 재시도 콜백으로 전달하여, 사용자가 명시적으로 클릭했을 때만 기존 파일 작업 대기열에 추가되어 실행 취소 및 진행률 이력을 정상 유지.

## 구현 내용

1. **`Folderss/Services/FileLockService.cs`**:
   - Win32 에러 코드 추출 및 오류 분류 로직(`IsSharingOrLockViolation`, `IsAccessDenied`, `ClassifyError`).
   - Restart Manager API 정의 및 세션 관리(`InspectAsync`, `QueryRestartManager`).
   - 프로세스 안전성 판정(`EvaluateProcess`) 및 PID/시작시각 재검증 기반 프로세스 정상 종료(`RequestCloseProcessAsync`) 및 강제 종료(`ForceKillProcess`).
   - 폴더 하위 잠긴 파일 감지 헬퍼(`FindLockedFilesInDirectory`, `IsFileLocked`).
   - zip 아카이브 내부 가상 경로 식별 및 차단(`IsArchiveVirtualPath`).
2. **`Folderss/FileLockInspectionWindow.cs`**:
   - 파일 경로, 점유 프로세스 목록(PID, 프로세스명, 종류, 경로), 조회 시각 스냅숏 표시.
   - 비동기 새로고침(`F5`), 정상 종료 요청, 강제 종료(확인 경고창), 삭제 재시도 버튼 제공.
   - 테마 리소스(`WindowBackground`, `PanelBackground`, `PrimaryText` 등) 적용.
3. **`Folderss/Services/FileJobs.cs` & `FileJobQueue.cs`**:
   - `FileJobFailureItem` 및 `FileJob.FailureItems` 모델 추가.
   - 삭제 및 이동 실패 시 발생한 예외의 Win32 오류 코드 및 잠금 위반 여부를 작업 결과 스냅숏에 기록.
4. **진입점 연결**:
   - `FolderBrowser.xaml.cs`: 우클릭 컨텍스트 메뉴에 `사용 중인 프로세스 확인…` 추가 및 `InspectLockingProcessesRequested` 이벤트 연결.
   - `MainWindow.xaml` & `MainWindow.xaml.cs`: 메인 메뉴 `⋯`에 `사용 중인 프로세스 확인…` 메뉴 추가 및 브라우저 이벤트 구독.
   - `MainWindow.FileJobs.cs`: 삭제/이동 작업 종료 시 잠금 위반 실패 항목이 있으면 "사용 중인 프로세스를 확인하시겠습니까?" 대화상자 노출 및 진입점 제공.
   - `FileJobsWindow.cs`: 파일 작업 진행률 창에서 잠금 위반 실패 항목이 있는 작업 행에 `[잠금 확인…]` 버튼 노출.
5. **단위 테스트**:
   - `tests/Folderss.SearchTests/FileLockServiceTests.cs`:
     - 오류 분류(공유 위반 32, 잠금 위반 33, 접근 거부 5, UnauthorizedAccessException) 구분 테스트.
     - 프로세스 안전성 평가(Folderss 자신, PID 4 시스템, csrss, Windows 서비스, 일반 앱) 테스트.
     - zip 가상 경로 차단 테스트.
     - 실제 Windows 환경에서 임시 파일 생성 및 `FileShare.None` 배타적 잠금 상태에서 Restart Manager를 통한 현재 프로세스 감지 테스트.

## 변경 파일

- `Folderss/Services/FileLockService.cs` — Restart Manager P/Invoke, 오류 분류, 프로세스 안전 판정 및 종료 서비스 신규 추가.
- `Folderss/FileLockInspectionWindow.cs` — 사용 중인 프로세스 조회 및 종료/재시도 대화상자 신규 추가.
- `Folderss/Services/FileJobs.cs` — `FileJobFailureItem` 정의 및 `FileJob`/`FileJobSnapshot`에 실패 상세 항목 목록 추가.
- `Folderss/Services/FileJobQueue.cs` — 삭제/이동 실패 시 경로 및 예외 정보를 `ItemFailed`에 전달.
- `Folderss/Controls/FolderBrowser.xaml.cs` — 컨텍스트 메뉴 `사용 중인 프로세스 확인…` 및 `InspectLockingProcessesRequested` 이벤트 추가.
- `Folderss/MainWindow.xaml` — 메인 `⋯` 메뉴에 `사용 중인 프로세스 확인…` 추가.
- `Folderss/MainWindow.xaml.cs` — 브라우저 이벤트 핸들러 및 메인 메뉴 핸들러 추가.
- `Folderss/MainWindow.FileJobs.cs` — 잠금 위반 실패 알림 대화상자 연결 및 `ShowFileLockInspectionWindow` 구현.
- `Folderss/FileJobsWindow.cs` — 작업 행에 잠금 실패 시 `[잠금 확인…]` 버튼 노출 및 대화상자 연결.
- `tests/Folderss.SearchTests/Folderss.SearchTests.csproj` — `FileLockService.cs` 소스 링크 추가.
- `tests/Folderss.SearchTests/FileLockServiceTests.cs` — 단위 및 Windows 통합 테스트 신규 작성.
- `docs/items/locked-file-process-inspection.md` — 상세 구현 내용, 변경 파일, 검증 기록 갱신.
- `docs/architecture.md` — 서비스 및 창 목록 갱신.
- `README.md` — 기능 설명 갱신.

## 검증

1. **빌드 검증**:
   - `dotnet build .\Folderss.sln -c Debug` -> 빌드 성공 (Exit: 0, 경고 6개, 오류 0개).
2. **단위 및 통합 테스트**:
   - `dotnet test tests/Folderss.SearchTests/Folderss.SearchTests.csproj --filter FileLockServiceTests`
     - 4개 테스트 모두 통과 (Exit: 0, 195ms).
     - 오류 분류(32/33 vs 5), 안전성 판정(본체/시스템/서비스/일반), 가상 경로 차단, 실제 Windows 파일 배타 잠금 검출 검증 완료.
   - `dotnet test tests/Folderss.SearchTests/Folderss.SearchTests.csproj --filter FileJobQueueTests`
     - 45개 테스트 모두 통과 (Exit: 0, 888ms). 기존 파일 대기열 및 이력 동작 영향 없음 확인.

## 변경 이력

- 2026-11-08: 사용자 제안을 Todo 아이템으로 등록하고 조회·종료·재시도 범위를 정리.
- 2026-11-08: `FileLockService`, `FileLockInspectionWindow` 구현, 대기열 실패 상세 정보 연동, 폴더 패널/메인 메뉴/작업 창 진입점 연결, 단위 테스트 작성 및 통과 확인 (Ready for Verification으로 전환).

### 2026-11-08 코드 리뷰 후 보완

- 요구사항: 강제 종료 확인 체크, 폴더의 여러 잠금 후보 조회, 다른 사용자 세션의 종료 차단을 보완한다.
- 구현: 기존 `GitForceConfirmDialog`를 재사용해 강제 옵션을 기본 꺼짐으로 두고 확인 체크 후에만 실행한다. 폴더에서 찾은 후보 최대 10개를 Restart Manager 한 세션에 모두 등록한다. 조회 시 세션 ID가 다르거나 불명확하면 종료 버튼을 막고, 정상/강제 종료 직전에도 실제 프로세스 세션을 재확인한다.
- 변경 파일: `FileLockService.cs`, `FileLockInspectionWindow.cs`, `FileLockServiceTests.cs`, `README.md`, `docs/architecture.md`, 본 문서.
- 검증: `dotnet test tests/Folderss.SearchTests/Folderss.SearchTests.csproj --filter "FileLockServiceTests|FileJobQueueTests"` — 50개 통과, Exit: 0. 다른/알 수 없는 세션의 종료 차단 회귀 테스트 포함.
- 빌드: 실행 중 앱의 출력 파일 잠금을 피하려고 `dotnet build .\Folderss.sln -c Debug -p:OutputPath=D:\git\tmp\folderss-lock-review-build\` 실행 — Exit: 0, 오류 0개, 경고 7개.
- 제한: 폴더 탐색은 기존의 깊이 8 및 후보 10개 제한을 유지하고 결과 메시지에 표시한다. 이번 보완의 UI 조작 및 실제 타 사용자 세션 종료 시도는 수행하지 않았다.
- 2026-11-08: 사용자 최종 승인에 따라 상태를 Done으로 전환.

# 콘솔 패널 Home End 단독 입력의 탭 이동 차단

- 상태: Done

## 요구사항

콘솔 패널에서 `Home` 또는 `End`를 보조키 없이 단독으로 입력할 때 콘솔 탭 패널이 이동하지 않도록 해야 한다.

## 원인 분석 또는 설계

- 콘솔 패널은 WPF `TabControl`을 사용하므로, `Home`/`End`가 상위 탭 컨트롤의 기본 탐색 키로 처리될 수 있다.
- 활성 콘솔 터미널 내부에서 키 입력이 bubbling 되거나, 탭 헤더가 포커스를 잡은 상태에서 `Home`/`End`를 누르면 탭 선택이 이동할 수 있다.
- 따라서 터미널 내부 `PreviewKeyDown`과 탭 컨트롤 `PreviewKeyDown` 양쪽에서 `Home`/`End` 단독 입력을 가로채고, 탭 이동 대신 활성 콘솔 세션으로 키를 전달해야 한다.
- 실제 테스트에서 상위 키 차단만으로는 탭 선택 변경이 완전히 막히지 않아, `SelectionChanged` 단계에서도 `Home`/`End`로 발생한 선택 변경을 감지해 이전 실제 콘솔 탭으로 복구해야 한다.

## 구현 내용

- 콘솔 터미널 내부와 콘솔 탭 컨트롤에서 `Home`/`End` 단독 입력을 가로채 탭 이동을 차단한다.
- 가로챈 키는 활성 콘솔 세션으로 직접 전달해 기존 콘솔 키 입력 의미를 유지한다.
- `ConsoleTabs.SelectionChanged`에서 `Home`/`End`로 인한 선택 변경이 감지되면 이전 실제 콘솔 탭으로 즉시 되돌린다.
- `+ 새 콘솔` 탭은 마우스로 직접 클릭한 경우에만 새 탭을 열고, 키보드 탐색으로 선택되면 이전 탭으로 복구한다.

## 변경 파일

- `docs/items/console-panel-home-end-tab-navigation.md`
- `Folderss/Controls/ConsolePanel.xaml`
- `Folderss/Controls/ConsolePanel.xaml.cs`
- `README.md`

## 검증

- 구현 전
- `dotnet build .\Folderss.sln -c Debug`
- 결과: 현재 실행 중인 `D:\git\cshap\Folderss\Folderss\bin\Debug\net8.0-windows\Folderss.exe` 프로세스(`PID 50432`)가 출력 파일을 잠가 `MSB3027`, `MSB3021`로 실패
- `dotnet build .\Folderss.sln -c Debug`
- 결과: Exit 0, 오류 0개
- 기존 경고: NuGet 취약성 데이터 조회 실패 `NU1900`, Windows 전용 API 분석 경고 `CA1416`, `WebClient` 사용 경고 `SYSLIB0014`

## 변경 이력

- 2026-06-28: 요청 접수, 로컬 아이템 문서 생성.
- 2026-06-28: 작업 시작, 콘솔 탭 기본 탐색 키 차단 방식으로 구현 진행.
- 2026-06-28: 구현 완료. Debug 빌드는 실행 중인 `Folderss.exe` 파일 잠금으로 재검증 대기.
- 2026-06-28: `Home`/`End`로 탭 선택이 실제 변경되는 경로를 막기 위해 선택 복구 로직을 추가하고 Debug 빌드 재검증 완료.
- 2026-06-28: 사용자 확인 완료, 상태를 `Done`으로 변경.

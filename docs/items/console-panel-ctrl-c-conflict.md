# 콘솔 패널 Ctrl+C 단축키 충돌 해소 (선택 인식 복사)

- 상태: Ready for Verification

## 요구사항

콘솔 패널(터미널)에 포커스가 있을 때 Ctrl+C를 누르면 앱의 "복사" 단축키와 터미널의 "실행 취소(SIGINT)"가
서로 겹치는 문제가 있다. Windows Terminal/VS Code 통합 터미널처럼 선택된 텍스트가 있으면 복사, 없으면
기존처럼 터미널 인터럽트로 동작하도록 개선한다. (tmux식 모드 전환 방식은 이 앱의 마우스 중심 GUI 상호작용과
맞지 않아 채택하지 않음 — 대화로 검토 후 결정.)

## 원인 분석 또는 설계

- `MainWindow.Window_PreviewKeyDown`은 `IsConsoleFocused()`일 때 가장 먼저 return하므로 앱의 전역
  `CopyClipboard` 단축키 처리 자체는 원래도 콘솔에 도달하지 않았다. 다만 WPF 라우티드 커맨드(`ApplicationCommands.Copy`)의
  키 제스처 변환이 이벤트 라우팅 경로 더 안쪽(터미널 컨트롤 자체)에서 일어날 여지가 있어, 터미널 쪽에서 Ctrl+C를
  명시적으로 소비하지 않으면 여전히 충돌할 수 있다.
- 기존에 Tab/Home/End 키를 `terminal.PreviewKeyDown`에서 가로채 PTY로 직접 전달하는 동일한 패턴이 이미 있었음
  (`docs/items/console-panel-home-end-tab-navigation.md`) — Ctrl+C도 같은 자리에서 동일한 방식으로 처리.
- **API 제약**: 이 프로젝트가 쓰는 `EasyWindowsTerminalControl`(NuGet) 패키지는 공식 문서/소스에 선택 텍스트 조회용
  공개 API(`SelectedText` 등)가 없는 것으로 조사됨. 이 코드베이스가 이미 `Theme` 같은 비공개 멤버를 리플렉션으로 우회해
  쓰고 있는 전례가 있어(`ApplyTerminalAppearance`), 동일한 방식으로 선택 텍스트 프로퍼티를 리플렉션 탐색하도록 구현.
  후보 프로퍼티 이름을 여러 개 시도하고, 못 찾으면 항상 안전하게 "선택 없음"으로 폴백한다(예외 없이 컴파일/실행 가능).

## 구현 내용

- `ResolveSelectedTextProperty(Type)` — 후보 이름(`SelectedText`, `SelectionText`, `CurrentSelectionText`, `GetSelectedText`) 중
  `string` 타입의 공개/비공개 인스턴스 프로퍼티를 리플렉션으로 탐색
- `TryGetTerminalSelectedText(EasyTerminalControl)` — 먼저 `EasyTerminalControl` 자체에서, 없으면 `terminal.ConPTYTerm`의
  런타임 타입에서 탐색. 리플렉션 예외는 전부 삼켜서 선택 없음으로 처리
- `terminal.PreviewKeyDown`에 Ctrl+C 분기 추가 — 선택 텍스트가 있으면 `Clipboard.SetText`로 복사,
  없으면 기존처럼 `ConPTYTerm.WriteToTerm("\x03")`(SIGINT)로 전달. 항상 `args.Handled = true`로 앱 단축키로 안 넘어가게 함

## 변경 파일

- `Folderss/Controls/ConsolePanel.xaml.cs`

## 검증

- [ ] **중요**: 리플렉션으로 찾는 프로퍼티 이름이 실제 `EasyWindowsTerminalControl` 1.0.36 DLL에 존재하는지 Windows 환경에서
      확인 필요 (이 세션은 Linux라 DLL을 직접 열어볼 수 없었음). 존재하지 않으면 항상 "선택 없음" 폴백으로 동작해 실행 취소만
      되고 선택 복사는 안 되는 상태가 됨 — 그 경우 실제 멤버 목록을 확인해 후보 이름을 갱신해야 함.
- [ ] 콘솔에서 텍스트 미선택 상태로 Ctrl+C → 실행 중인 프로그램이 정상적으로 취소(인터럽트)됨
- [ ] 콘솔에서 텍스트를 드래그 선택 후 Ctrl+C → 클립보드에 선택 텍스트가 복사되고 프로그램은 취소되지 않음
- [ ] 콘솔 이외 패널(파일트리/파일목록)에서는 기존 Ctrl+C 복사 동작이 그대로 유지됨
- [ ] 빌드 성공 확인 (Windows/MSBuild 환경에서 확인 필요 — 이 세션은 Linux 환경이라 직접 빌드 불가)

## 변경 이력

- 2026-07-14: 초기 구현 (리플렉션 기반 선택 텍스트 탐색 + Ctrl+C 분기)

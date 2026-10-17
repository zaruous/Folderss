# 외부 비교 도구: 두 파일 비교 바로 열기, 설정해도 안 되던 문제

- 상태: Done — Release 빌드에서 사용자 확인 완료

## 요구사항

- 2026-10-17 사용자 보고: 설정 > 비교에서 외부 비교 도구를 고르고 파일을 비교해도 앱의 내장 비교 창이 뜬다. 설정해도 외부 도구가 안 먹히는 것 같다.
- 사용자 결정: 폴더 패널의 ⋯ 메뉴 `두 파일 비교…`와 우클릭 `선택한 두 파일 비교`는 외부 도구를 골랐으면 그 도구로 비교한다.
- 추가 요청: 설정 창 아래 `[적용] [저장] [취소]` — 적용은 저장과 같지만 창을 닫지 않는다(모든 설정 탭 공통).
- 추가 요청: 비교 설정을 열면 저장된 도구에 맞는 프리셋이 선택된 상태로 보인다(지금은 늘 첫 항목 WinMerge).

## 원인 분석 또는 설계

1. **설계상 동작**: 외부 도구 설정은 diff 창의 `외부 도구로 비교` 버튼이 쓸 도구만 정했다. `MainWindow.OpenFileCompare`는 설정과 무관하게 늘 `FileCompareWindow`를 열었다.
2. **가짜 실패(버그)**: `DiffToolLauncher`가 "stderr가 비어 있어야 성공"으로 판정했다. Git for Windows 기본 `core.autocrlf=true`에서 LF 파일이면 `git difftool`이 도구를 정상으로 띄운 뒤에도 stderr에 `warning: in the working copy of '…', LF will be replaced by CRLF …`를 낸다(이 PC에서 같은 인수로 재현: 종료 코드 1, 도구는 원래 두 파일을 받음). 그래서 도구가 떠도 "외부 비교 도구가 실패했습니다"가 나왔다. 이 경로를 검증하는 실제 difftool 테스트는 Linux·맥 전용이라 잡히지 않았다.
3. **Beyond Compare 빈 비교(버그)**: 프리셋이 `BCompare.exe`였다(이 PC 설정도 같음). Beyond Compare 문서와 git의 `bc` 도구 정의는 버전 관리 연동에 비교를 닫을 때까지 기다리는 `BComp.exe`를 쓴다. `BCompare.exe`는 이미 떠 있는 창에 비교를 넘기고 바로 끝날 수 있어, Git 창 비교(임시 파일)는 git이 도구 종료 직후 임시 파일을 지워 빈 비교가 된다. 두 파일 비교는 원래 파일을 넘기므로 이 문제는 없다. (문서 기준 판단, 화면 재현은 하지 않음)

### 바꾼 동작

- `OpenFileCompare`(⋯ 메뉴·우클릭·폴더 비교 창의 `반대쪽 같은 이름 파일과 비교`·`⇆ 파일 비교` 공통): 외부 도구가 지정돼 있으면 `DiffToolLauncher`로 바로 연다(`git difftool --no-index`, 원래 두 파일). 사전 검사에서 막히거나(실행 파일 없음 등) 실패하면 알린 뒤 내장 비교 창을 연다.
- Git 창은 그대로 내장 diff를 보이고 `외부 도구로 비교` 버튼이 도구를 쓴다.
- 성공 판정: `GitDiffCommands.IsToolRunSuccess` — 종료 코드 성공 + stderr에 `warning:` 줄 말고 없음.
- Beyond Compare: 프리셋을 `BComp.exe`로. 저장된 `BCompare.exe`도 실행 때 같은 폴더의 `BComp.exe`로 바꾼다(`DiffSettingsService.ResolveToolPath`, 없으면 원래 경로).

## 구현 내용

- `MainWindow.OpenFileCompare` → `OpenExternalFileCompare` / `OpenBuiltInFileCompare`
- `DiffToolLauncher`: `IsToolRunSuccess`, `ResolveToolPath` 사용, 빠른 종료 안내에 Beyond Compare 추가
- `GitDiffCommands.IsToolRunSuccess`, `DiffSettingsService.ResolveToolPath`, Beyond Compare 프리셋
- 설정 창 비교 탭: 외부 도구를 지정하면 두 파일 비교가 바로 열린다는 안내, Beyond Compare 안내
- 설정 창 `적용`: `Save_Click` 본문을 `SaveAll()`로 나눠 저장·적용이 같이 쓴다. 적용은 `Applied` 이벤트로 알리고 `MainWindow.ApplySavedSettings`가
  저장 후 닫을 때와 같이 콘솔·열린 Git 창·비교 창에 반영한다. 취소가 되돌릴 테마 기준(`_originalTheme`)도 적용 때의 테마로 바꾼다.
- 프리셋 선택: `DiffSettingsService.FindPresetIndex`(실행 파일 이름으로, BCompare.exe도 Beyond Compare, 없으면 -1 = 비움)

## 변경 파일

- `Folderss/MainWindow.xaml.cs`, `Folderss/DiffToolLauncher.cs`, `Folderss/SettingsWindow.xaml`, `Folderss/SettingsWindow.xaml.cs`
- `Folderss/Services/GitDiffCommands.cs`, `Folderss/Services/DiffSettingsService.cs`
- `tests/Folderss.SearchTests/GitTests.cs`
- `README.md`, `docs/architecture.md`, `docs/items/diff-external-tool-direct.md`

## 검증

- 단위 테스트: `IsToolRunSuccess_IgnoresGitWarnings_ButNotErrors`(이 PC에서 나온 실제 경고 문구), `ResolveToolPath_UsesWaitingBeyondCompare`, `FindPresetIndex_MatchesSavedExecutable`, 기존 외부 도구 테스트 통과.
- Windows 화면에서 확인할 것:
  1. 설정 > 비교 > 직접 지정(Beyond Compare)에서 폴더 패널 우클릭 `선택한 두 파일 비교`·⋯ 메뉴 `두 파일 비교…` → 내장 창 없이 Beyond Compare가 열리고 실패 메시지가 없다.
  2. 외부 도구를 `사용 안 함`으로 바꾸면 내장 비교 창이 열린다.
  3. 실행 파일 경로를 없는 파일로 바꾸면 안내 후 내장 비교 창이 열린다.
  4. Git 창의 `외부 도구로 비교`에서 Beyond Compare에 내용이 보인다(빈 비교가 아님).
  5. 설정 창 `적용`: 창이 닫히지 않고, 외부 도구를 바꾼 뒤 적용하면 바로 두 파일 비교에 반영된다. 테마를 바꿔 적용한 뒤 취소해도 적용한 테마가 남는다.
  6. 설정 창을 다시 열면 비교 탭 프리셋이 Beyond Compare로 선택돼 있다.

## 변경 이력

- 2026-10-17: 사용자 보고·결정, 원인 분석, 구현·단위 테스트 완료.
- 2026-10-17: 추가 요청 — 설정 창 `적용` 버튼, 저장된 도구의 프리셋 선택. 구현·단위 테스트 완료.
- 2026-10-17: Release 빌드에서 사용자 확인 완료(Done).

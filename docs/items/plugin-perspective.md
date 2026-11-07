# 플러그인 퍼스펙티브 (메인 창에 플러그인 표시)

- 상태: Done — master 머지 확인 (이전: Ready for Verification — 빌드·단위 테스트 완료, 실제 창 확인 대기)
- 상태 전환일: 2026-11-07

## 요구사항 (2026-11-01)

1. 플러그인을 팝업이 아니라 메인 창 루트에 탭처럼 띄울 수 있게 한다 (이클립스 Java SE / Java EE 퍼스펙티브 전환처럼 작은 전환 버튼).
2. 기본은 지금처럼 팝업. 설정 > 플러그인에서 플러그인별로 표시 방식을 바꿀 수 있다.

## 설계

- **전환 버튼 위치**: 제목 표시줄 `⋯` 버튼 왼쪽 (`PerspectiveBar`). 열린 플러그인 퍼스펙티브가 하나라도 있을 때만 보인다 —
  `[📁 파일] [🔌 플러그인A ×] …`. 이클립스의 오른쪽 위 퍼스펙티브 스위처와 같은 자리.
- **화면 교체**: `DockManager`는 옮기지 않고 같은 행에 겹친 `PerspectiveHost`(Grid)에 플러그인 뷰를 넣은 뒤 `Visibility`만 바꾼다.
  DockManager를 다른 부모로 옮기면 논리 부모 충돌·레이아웃 복원 문제가 생긴다. 하단 상태 표시줄도 파일 퍼스펙티브에서만 보인다.
- **뷰 수명**: 메인 탭 모드는 `CreateView()`를 처음 열 때 한 번만 부르고, 퍼스펙티브를 닫을 때(× 또는 우클릭 닫기)까지 유지한다.
  팝업 모드는 지금처럼 열 때마다 새로 만든다.
- **표시 방식 저장**: 본체 파일 `plugins\display.json` (id → `popup`/`main`, `PluginDisplayStore`). 플러그인 자기 `settings.json`에
  두지 않는다 — 플러그인이 `SetSetting`으로 바꿀 수 있으면 안 된다. 기본(기록 없음) = 팝업. 설정 창 `저장`에서 `TrySave`로 저장.
  바꾼 방식은 다음에 실행할 때부터 적용(이미 열린 퍼스펙티브는 닫을 때까지 유지).
- **단축키**: `NextPerspective`(기본 Ctrl+F8, 이클립스와 같음) — 열린 퍼스펙티브를 차례로 전환. 플러그인 퍼스펙티브가 보이는 동안에는
  본체 단축키(패널 추가·패널 전환·검색·최대화·파일 작업)를 처리하지 않고 플러그인에 넘긴다 — 숨겨진 폴더 패널에 작업이 가지 않게.
- **⋯ 메뉴**: 플러그인 퍼스펙티브가 보이는 동안 파일·패널 대상 항목은 비활성화(`Tag="global"`이 아닌 항목). 숨겨진 패널의
  선택을 지우는 사고를 막는다.
- **파일 열기 연동**: 플러그인 폴더 패널에서 파일을 열면(`OpenViewerTab`) 파일 퍼스펙티브로 전환한다.
- **예외 격리**: `PluginManager.TryHandleUnhandled`가 플러그인 예외로 판별하면 `PluginFaulted`로 알리고, 메인 창은 그 플러그인
  퍼스펙티브를 떼어 내고 파일 퍼스펙티브로 돌아간다(레이아웃 예외 반복으로 메인 창이 멈추지 않게).
- **세션 복원**: 이번 범위에서는 열린 퍼스펙티브를 기억하지 않는다. 시작 때 플러그인을 로드하지 않는다는 지연 로드 원칙 유지.
- **계약 변경 없음**: `IFolderssPlugin.CreateView()` 그대로 사용.

## 구현

- `Services/PluginDisplayStore.cs` — `plugins\display.json` 읽기·쓰기(`SettingsFile.WriteAllText`, 실패는 예외). 기본값(팝업)은 기록하지 않음, 알 수 없는 값은 버림, 깨진 파일은 `Load`가 `InvalidDataException`, `Get`은 팝업.
- `Services/PluginManager.cs` — `CreatePluginView`(로드 + `CreateView`, 실패 메시지) 분리, `ShowPluginWindow`는 이를 사용. `GetDisplayMode`, `PluginFaulted` 이벤트(`TryHandleUnhandled`에서 매번 발생).
- `MainWindow.Perspectives.cs` — `RunPlugin`(메뉴·트레이 공통), `ShowPerspective`/`ShowNextPerspective`/`ClosePerspective`, 전환 버튼(`CompactToggleButtonStyle`, 우클릭: 팝업 창으로 열기·닫기), `UpdateMainMenuForPerspective`.
- `MainWindow.xaml` — 제목 표시줄 `PerspectiveBar`, `PerspectiveHost`(DockManager와 같은 행), `StatusBarBorder` 이름, ⋯ 메뉴 전역 항목 `Tag="global"`(파일 작업 진행 창·플러그인·테마·화면 캡쳐·설정·정보·업데이트·종료).
- `MainWindow.xaml.cs` — 생성자에서 `InitializePerspectives`, ⋯ 메뉴 열 때 항목 끄기, `OpenViewerTab` 앞 파일 퍼스펙티브 전환, `Window_PreviewKeyDown` 맨 앞 `NextPerspective`·플러그인 퍼스펙티브면 반환, 플러그인 메뉴 열린 항목 체크.
- `Services/KeyBindingService.cs` — `NextPerspective` 기본 `Ctrl+F8`.
- `SettingsWindow` — 플러그인 목록에 `표시 방식` 콤보 열(`PluginDisplayOptions`), 작업 사본 `_workingPluginDisplay`, `Save_Click`의 `TrySave("플러그인 표시 방식", …)`.
- 문서: README 플러그인 절·서비스 목록, `docs/architecture.md`(파일 목록·PluginManager 절), `docs/keyboard-shortcuts.md`, `docs/plugin-development.md`(수명 표·`Window.GetWindow(view).Close()` 주의).

## 결정

- 전환 버튼은 메뉴줄이 없는 창이라 제목 표시줄 `⋯` 왼쪽(이클립스 오른쪽 위 스위처 자리). 플러그인 퍼스펙티브가 없으면 숨김 — 플러그인을 안 쓰는 사용자 화면은 그대로.
- 퍼스펙티브를 닫을 때 `Dispose`를 부르지 않음 — 팝업도 부르지 않고, 플러그인이 같은 뷰 인스턴스를 다시 돌려줄 수 있음.
- 표시 방식은 설정 창 `저장` 때 기록(다른 설정과 같은 실패 보고). 플러그인 추가·제거처럼 즉시 반영하지 않음.

- (2026-11-01 사용자 확인 후) `팝업 창으로 열기` → `팝업 창으로 옮기기`: 퍼스펙티브를 닫고 `CreateView()`를 다시 부르던 것을 같은 뷰를 팝업 창으로 옮기도록 바꿈
  (`PluginManager.ShowViewWindow`). 원인: 다시 만들면 상태가 사라지고, "창 하나만" 규칙의 플러그인(AutoClick)은 기존 뷰가 열려 있다고 보고 새 창을 바로 닫음.
  옮긴 뒤 메인 창이 포커스를 빼앗지 않게 퍼스펙티브 포커스 이동은 메인 창이 활성일 때만.
- 플러그인 코드가 메인 창 `Close()`를 부르면(`Window.GetWindow(view).Close()` — 팝업용 플러그인의 "자기 창 닫기") 트레이로 숨기지 않고 그 플러그인 퍼스펙티브만 닫음
  (`Window_Closing` → `TryClosePerspectiveForPluginClose`, 호출 스택에서 플러그인 판별 `PluginManager.FindCallingPluginId`).
- 알려진 비호환: AutoClick처럼 `Loaded`에서 `Window.GetWindow(this)`를 자기 창으로 잡아 크기·Topmost·숨기기·전역 단축키(HWND)를 다루는 플러그인은
  퍼스펙티브에서 메인 창을 바꾼다. 본체에서 막을 수 없어 `plugin.json` `"popupOnly": true` 선언을 추가(사용자 선택 1번):
  `PluginManifest.PopupOnly` → `RunPlugin`은 항상 팝업, 설정 창 표시 방식 콤보 비활성(툴팁으로 이유), 예전에 저장한 `main` 기록은 다음 저장 때 지움.
  AutoClick 저장소의 `plugin.json`에 `"popupOnly": true`를 넣어 다시 배포해야 적용된다(이미 설치된 zip에는 없음).

## 검증

- [x] `dotnet build .\Folderss.sln -c Debug` 오류 0
- [x] `PluginDisplayStoreTests`(4) 추가, 전체 단위 테스트 1,128개: 통과 1,124·건너뜀 4·실패 0
- [x] `dotnet build samples/HelloPlugin` 오류 0 (계약 변경 없음)
- [ ] 실제 창 확인(사람):
  1. 설정 > 플러그인에서 한 플러그인을 `메인 창 (퍼스펙티브)`로 저장 → ⋯ 메뉴 > 플러그인 실행 → 메인 창이 플러그인 화면으로 바뀌고 제목 표시줄에 `[📁 파일] [🔌 이름 ×]`
  2. `Ctrl+F8`·버튼으로 오가도 플러그인 화면 상태(입력값·스크롤) 유지, 파일 화면의 도킹 배치·뷰어 탭 그대로
  3. 플러그인 화면에서 `F2`·`Delete`·`Ctrl+T`·`F11`이 숨겨진 패널에 작용하지 않음, ⋯ 메뉴 파일 항목 비활성
  4. 플러그인 안 폴더 패널에서 파일 열기 → 파일 화면으로 전환되어 뷰어 탭
  5. `×`·오른쪽 클릭 닫기 → 파일 화면으로, 버튼 줄 사라짐. `팝업 창으로 열기` 동작
  6. 플러그인 화면인 채로 창 닫기(트레이) → 다시 열기 → 다음 실행 때 도킹 배치가 깨지지 않음
  7. 팝업으로 둔 플러그인은 이전과 같이 팝업

## 후속 후보

- 선택 인터페이스 `IPluginViewLifecycle { Activated(); Deactivated(); }` — 숨겨질 때 타이머·폴링을 멈추게(계약 추가).
- `plugin.json` `preferredDisplay` — 플러그인이 권하는 기본 표시 방식.
- 열린 퍼스펙티브 세션 복원(탭만 복원, 누를 때 로드).
- 세 번째 방식: AvalonDock 문서 탭.

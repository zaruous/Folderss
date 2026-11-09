# Git을 메인 창 퍼스펙티브로 띄우기

- 상태: Ready for Verification — 단위 테스트 통과, Windows 빌드·실제 창 확인 대기
- 시작일: 2026-11-09

## 요구사항

1. Git은 지금 팝업으로만 열린다. 플러그인처럼 메인 창 퍼스펙티브로도 띄울 수 있게 한다.
2. 설정에서 플러그인과 같이 팝업/메인을 고른다. Git은 기본 기능이므로 플러그인 목록에 기본 항목으로 항상 보인다.

## 사용자 확인 (2026-11-09)

- 노출 범위: **설정 > 플러그인 목록에만** 기본 행으로 둔다. ⋯ > 플러그인 하위 메뉴에는 넣지 않는다(`Git 저장소…`와 중복).
- 메인 창에 Git이 이미 열려 있을 때 다시 실행: **기존 화면으로 전환만**. 기준 폴더는 화면 안 `변경…`으로 바꾼다(작성 중인 커밋 메시지·선택 유지).

## 설계

- `GitWindow`(3,375줄 Window)를 `GitView`(UserControl)로 바꾸고, `GitWindow`는 뷰를 담는 얇은 팝업 창으로 남긴다.
  창 내용(Content)만 떼어 메인 창에 붙이는 방식은 버림 — 보이지 않은 창을 `Owner`로 쓰면 `ShowDialog`가 예외를 내고(대화상자 20곳), `Loaded`가 오지 않아 탐색이 시작되지 않는다.
- 창에 묶여 있던 부분만 바꿈: 대화상자·메시지 주인 `this` → `OwnerWindow`(`Window.GetWindow(this)`), `Close()` → `CloseRequested`,
  `Title` → 뷰 속성 + `TitleChanged`, `Window_Loaded` → `View_Loaded`(처음 한 번만), `Window_Closed` → `Shutdown()`(담는 쪽이 부름).
- 표시 방식 저장: 플러그인과 같은 `plugins\display.json`, 키 `builtin:git`(`PluginDisplayStore.BuiltinGitId`). 플러그인 ID 규칙(`[A-Za-z0-9._-]`)에 `:`가 없어 겹치지 않는다.
- 퍼스펙티브 코드(`MainWindow.Perspectives.cs`)는 항목에 `Icon`·`Closed`(닫을 때 정리)·`MoveToPopup`(팝업으로 옮기기)을 두어 플러그인·Git이 같이 쓴다.
  Git은 닫을 때 `GitView.Shutdown`(진행 중인 git 프로세스 종료), 팝업으로 옮기면 같은 뷰를 새 `GitWindow`에 넣는다(정리하지 않음). 앱 종료 때 `ShutdownPerspectives`.

## 구현

- `Folderss/GitView.xaml/.cs` — 기존 `GitWindow` 내용(UserControl). 위 창 의존 부분만 변경.
- `Folderss/GitWindow.xaml/.cs` — 팝업 창(크기·최소 크기·가운데 표시는 기존 값 그대로), 제목 따라가기, 닫을 때 `Shutdown`.
- `Folderss/MainWindow.xaml.cs` — `ShowGit_Click`: 열린 Git 퍼스펙티브가 있으면 전환, 아니면 표시 방식에 따라 팝업/퍼스펙티브. `ApplySavedSettings`가 퍼스펙티브 Git에도 설정 반영. 종료 때 `ShutdownPerspectives`.
- `Folderss/MainWindow.Perspectives.cs` — `OpenGitPerspective`, `OpenPerspective`(공통), `DetachPerspective`/`ClosePerspective` 분리.
- `Folderss/SettingsWindow.xaml.cs` — 플러그인 목록 맨 위 `Git (기본 기능)` 행(`IsBuiltIn`), 제거·업데이트는 안내만.
- `Folderss/Services/PluginDisplayStore.cs` — `BuiltinGitId`.
- 테스트: `PluginDisplayStoreTests.BuiltinGit_RoundTrips_AndCannotBeAPluginId`.

## 결정 / 위험

- 퍼스펙티브 Git은 하나만. 여러 폴더를 동시에 보려면 팝업 방식을 써야 한다.
- Git 퍼스펙티브가 보이는 동안 ⋯ 메뉴의 `Git 저장소…`는 꺼진다(기존 규칙: `Tag="global"`이 아닌 항목). 파일 화면으로 돌아가면 다시 켜진다.
- 퍼스펙티브에서 파일을 더블클릭하면 `OpenViewerTab`이 파일 화면으로 전환한다(플러그인과 같음).
- 바꾼 표시 방식은 다음 실행부터 적용(이미 열린 Git은 닫을 때까지 유지) — 플러그인과 같음.
- git 기록상 `GitWindow.xaml.cs`가 새 내용으로 바뀌고 `GitView.xaml.cs`가 추가된 것으로 보인다. 이전 이력은 `git log --follow`보다 `git log -- Folderss/GitWindow.xaml.cs`(이 커밋 이전)로 본다.

## 검증

- [x] `dotnet test tests/Folderss.SearchTests --filter PluginDisplayStoreTests` — 5개 통과(새 테스트 포함)
- [x] Linux에서 `dotnet build Folderss/Folderss.csproj -p:EnableWindowsTargeting=true`: XAML 생성(`GitView.g.cs`·`GitWindow.g.cs`) 통과, C# 오류는 `ConsolePanel`의
      `Microsoft.Terminal` 참조 1건뿐 — 변경 전 코드에서도 같은 오류(Linux 환경에서 Windows 전용 패키지 참조 문제)
- [ ] Windows `dotnet build .\Folderss.sln -c Debug` 오류 0
- [ ] 실제 창 확인(사람):
  1. 기본(팝업): ⋯ > Git 저장소… → 이전과 같은 팝업, 제목 `Git — 경로`, fetch·커밋 등 대화상자가 Git 창 위에 뜸
  2. 설정 > 플러그인 맨 위 `Git (기본 기능)` → `메인 창 (퍼스펙티브)` 저장 → Git 저장소… → 메인 창이 Git 화면, 제목 표시줄 `[📁 파일] [⎇ Git ×]`
  3. `Ctrl+F8`로 오가도 커밋 메시지·선택 유지, 대화상자(fetch/pull/push)가 메인 창 위에 뜸
  4. 파일 화면에서 다른 폴더로 Git 저장소… → 기존 Git 화면으로 전환(기준 폴더 그대로)
  5. 오른쪽 클릭 > 팝업 창으로 옮기기 → 같은 상태로 팝업, 탐색이 다시 돌지 않음. 팝업 닫기 → git 프로세스 정리
  6. `×` 닫기 → 진행 중 작업 취소, 파일 화면으로
  7. Git 화면에서 설정… 저장 → 퍼스펙티브 Git에도 반영
  8. `Git (기본 기능)` 행에서 제거·업데이트 → 안내만, 아무것도 지우지 않음
  9. git이 없는 PC에서 메인 창 방식 → 안내 후 퍼스펙티브가 닫히고 파일 화면

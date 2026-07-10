# 뷰어(마크다운 등) 편집 중 Home End 입력의 문서 탭 이동 차단

- 상태: Done

## 요구사항

마크다운 패널 Edit/Split 모드에서 편집 중 `Home`/`End`를 누르면 문서 탭이 이동해버려(캐럿도 움직이지 않음) 편집이 불편하다. 편집 중에는 캐럿 이동으로 동작해야 한다.

## 원인 분석 또는 설계

- WebView2 WPF 래퍼는 브라우저가 받은 키 입력을 `AcceleratorKeyPressed` → WPF `PreviewKeyDown`(터널링) → `KeyDown`(버블링) 순으로 WPF 트리에 재발생시키고, 라우팅이 끝난 뒤 `KeyEventArgs.Handled` 값을 브라우저에 되돌려준다. `Handled == true`이면 브라우저는 해당 키의 기본 동작(캐럿 이동 등)을 수행하지 않는다.
- 마크다운 뷰어는 AvalonDock 문서 탭 영역(`LayoutDocumentPaneControl`)에 호스팅되는데, 이 컨트롤은 WPF `TabControl` 파생이다. WPF `TabControl.OnKeyDown`은 보조키 여부와 무관하게 `Home` = 첫 탭, `End` = 마지막 탭 선택을 기본 처리한다.
- 따라서 편집 중 `End`를 누르면 버블링된 `KeyDown`이 문서 탭 컨트롤에 도달해 마지막 탭(`+ 새 패널`)으로 전환되고, 탭이 `Handled` 처리하므로 브라우저 캐럿도 움직이지 않는다. `Shift+Home`(줄 선택), `Ctrl+Home`(문서 처음) 등 보조키 조합도 동일하게 빼앗긴다.
- 콘솔 패널에서 같은 원인의 문제를 처리한 전례가 있다([console-panel-home-end-tab-navigation.md](console-panel-home-end-tab-navigation.md)).
- 해결: 버블링 경로에서 문서 탭 컨트롤보다 앞에 있는 `ViewerHost`가 WebView2 발생 `Home`/`End` `KeyDown`을 `Handled = true`로 차단해 탭 이동을 막는다. 다만 그대로 두면 래퍼가 브라우저 기본 동작까지 막아버리므로, 라우팅의 마지막 노드인 `MainWindow`가 `handledEventsToo` 핸들러로 `Handled`를 `false`로 되돌려 브라우저가 캐럿 이동을 정상 수행하게 한다.

## 구현 내용

- `ViewerHost.xaml.cs` — 생성자에서 `KeyDown` 핸들러(`SuppressDocumentTabNavigation`)를 등록. `e.OriginalSource`가 `WebView2`인 `Home`/`End`(보조키 조합 포함)를 `Handled = true`로 차단해 AvalonDock 문서 탭 전환을 방지.
- `MainWindow.xaml.cs` — 생성자에서 `handledEventsToo: true`로 `KeyDown` 핸들러(`RestoreWebViewNavigationKey`)를 등록. WebView2 발생 `Home`/`End`의 `Handled`를 라우팅 종료 직전에 `false`로 되돌려 WebView2 래퍼가 브라우저 기본 캐럿 동작을 수행하도록 허용.
- 이 방식은 `ViewerHost`에 호스팅되는 모든 WebView2 기반 뷰어(Markdown, Text, Monaco)에 공통 적용된다.

## 변경 파일

- `Folderss/Controls/ViewerHost.xaml.cs`
- `Folderss/MainWindow.xaml.cs`
- `docs/items/viewer-home-end-tab-navigation.md`

## 검증

- `dotnet build .\Folderss.sln -c Debug` — Exit 0, 오류 0개 (기존 경고 NU1900/CA1416/SYSLIB0014만 존재).
- WebView2 WPF 래퍼(1.0.2739.15)를 디컴파일해 `CoreWebView2Controller_AcceleratorKeyPressed`가 라우팅 종료 후의 `Handled` 값을 그대로 브라우저에 반환함을 확인 — `Handled` 복원 시 브라우저가 기본 동작을 수행한다.
- 수동 확인 필요: 마크다운 Edit/Split 모드에서 `Home`/`End`/`Shift+End`/`Ctrl+Home` 입력 시 탭이 이동하지 않고 캐럿이 정상 동작하는지.

## 변경 이력

- 2026-07-11: 요청 접수, 원인 분석(AvalonDock 문서 탭 = TabControl의 Home/End 기본 탐색) 및 구현·빌드 완료. 사용자 확인 대기.
- 2026-07-11: v1.5.5 릴리스에 포함, 상태를 `Done`으로 변경.

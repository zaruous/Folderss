# 마크다운 패널 Ctrl+F 검색

- 상태: Ready for Verification

## 내용

## 요구사항

마크다운 패널에서 `Ctrl+F`를 누르면 앱 전역 내용 검색 단축키가 먼저 처리되어 마크다운 패널 내부 검색이 보이지 않는 문제를 수정한다.
추가로 Edit, Split, Preview 세 모드에서 현재 선택된 모드에 맞게 검색이 동작해야 한다.

추가 요구사항:

- WebView 내부 검색 팝업은 렌더링 시점 때문에 늦게 표시되어 사용성이 나쁘므로 제거한다.
- 마크다운 패널이 포커스된 상태에서 `Ctrl+F`를 누르면 WPF 전용 검색창을 즉시 표시한다.
- 검색창 UI는 WPF가 담당하고, WebView는 검색 결과 하이라이트와 이전/다음 이동만 담당한다.

추가 변경 요구사항:

- WPF 전용 검색창이 표시되지 않는 문제가 있어 해당 방식은 제거한다.
- 대안으로 Markdown HTML 툴바의 `Edit`, `Split`, `Preview` 옆에 검색 버튼을 추가한다.
- 검색 버튼 클릭 시 HTML 내부 검색창을 표시한다.

## 원인 분석 또는 설계

`MainWindow.Window_PreviewKeyDown`에서 `ShowSearch` 단축키가 전역으로 처리되어 활성 뷰어가 `Ctrl+F`를 처리할 기회를 받지 못했다. 뷰어별 단축키 처리 인터페이스를 추가하고, 전역 단축키 처리 전에 활성 뷰어에 먼저 위임하도록 설계했다.
추가로 Markdown 검색 갱신 시점마다 `textarea`의 selection을 자동으로 다시 잡아 Edit 모드의 입력 커서가 의도치 않게 이동하는 문제가 있었다. 검색 결과 계산과 커서 이동을 분리해야 했다.

## 구현 내용

- `IViewerShortcutHandler`를 추가해 뷰어가 단축키를 직접 처리할 수 있게 했다.
- `ViewerHost`가 현재 뷰어의 단축키 처리 결과를 전달하게 했다.
- `MainWindow`에서 활성 문서가 뷰어인 경우 전역 단축키보다 먼저 뷰어 단축키를 처리한다.
- `MarkdownViewer`가 `Ctrl+F`를 처리해 WebView 내부 `app.openFind()`를 호출한다.
- 마크다운 HTML 앱에 자체 검색 바, 결과 하이라이트, 다음/이전 이동, 닫기 동작을 추가했다.
- 검색 결과를 Markdown 원문 기준으로 관리해 Edit/Split에서는 textarea 선택과 스크롤을 이동하고, Preview/Split에서는 미리보기 하이라이트를 갱신한다.
- WebView 내부 검색 팝업을 제거하고 `MarkdownViewer.xaml`에 WPF 전용 검색창을 추가했다.
- `Ctrl+F`는 WebView 렌더링 상태와 무관하게 WPF 검색창을 즉시 표시한다.
- WPF 검색창 입력, 이전/다음, 닫기 동작을 `app.search()`, `app.findNext()`, `app.findPrevious()`, `app.clearSearch()`로 전달한다.
- HTML 앱은 검색 UI를 갖지 않고 검색 결과 하이라이트와 위치 이동, 결과 카운트 메시지만 담당한다.
- WPF 전용 검색창 방식은 표시 문제가 있어 제거했다.
- Markdown HTML 툴바의 `Edit`, `Split`, `Preview` 옆에 `Search` 버튼을 추가했다.
- `Search` 버튼 또는 `Ctrl+F`로 HTML 내부 검색 바를 열고, 검색 바에서 입력·Enter·Shift+Enter·Esc·닫기 버튼을 처리한다.
- 검색 결과 카운트는 HTML 검색 바 내부에 표시한다.
- 검색 결과 갱신 시에는 편집 커서를 자동으로 건드리지 않고, `다음/이전` 동작에서만 selection을 이동한다.
- Edit 모드에서는 `다음/이전` 이동 시에만 textarea 포커스를 넘기고, 검색어 입력 중에는 검색창 포커스를 유지한다.

추가 요청:

- 폴더 패널의 `Ctrl+C`, `Ctrl+X`, `Ctrl+V`가 파일 작업과 텍스트 입력을 더 일관되게 구분하도록 정리한다.

## 변경 파일

- `Folderss/Viewers/IFileViewer.cs`
- `Folderss/Controls/ViewerHost.xaml.cs`
- `Folderss/MainWindow.xaml.cs`
- `Folderss/Viewers/MarkdownViewer.xaml.cs`
- `Folderss/Viewers/MarkdownViewer.xaml`
- `Folderss/Viewers/Resources/markdown-app.html`
- `Folderss/Controls/FolderBrowser.xaml.cs`
- `Folderss/MainWindow.xaml.cs`

## 검증

- `dotnet build .\Folderss.sln -c Debug`
- 결과: Exit 0, 오류 0개
- 기존 경고: NuGet 취약성 데이터 조회 실패 `NU1900`, Windows 전용 API 분석 경고 `CA1416`, `WebClient` 사용 경고 `SYSLIB0014`
- `dotnet build .\Folderss.sln -c Debug`
- 결과: Exit 0, 오류 0개
- 기존 경고: NuGet 취약성 데이터 조회 실패 `NU1900`, Windows 전용 API 분석 경고 `CA1416`, `WebClient` 사용 경고 `SYSLIB0014`
- `dotnet build .\Folderss.sln -c Debug`
- 결과: Exit 0, 오류 0개
- 기존 경고: NuGet 취약성 데이터 조회 실패 `NU1900`, Windows 전용 API 분석 경고 `CA1416`, `WebClient` 사용 경고 `SYSLIB0014`
- `dotnet build .\Folderss.sln -c Debug`
- 결과: Exit 0, 오류 0개
- 기존 경고: NuGet 취약성 데이터 조회 실패 `NU1900`, Windows 전용 API 분석 경고 `CA1416`, `WebClient` 사용 경고 `SYSLIB0014`

## 변경 이력

- 2026-06-28: 구현 및 Debug 빌드 검증 완료. 로컬 항목 파일에 기록.
- 2026-06-28: Edit/Split/Preview 모드별 검색 동작 보완 및 Debug 빌드 재검증.
- 2026-06-28: WebView 내부 검색 팝업을 제거하고 WPF 전용 검색창으로 전환. Debug 빌드 재검증 완료.
- 2026-06-28: WPF 전용 검색창을 제거하고 Markdown HTML 툴바의 `Search` 버튼과 내부 검색 바 방식으로 전환. Debug 빌드 재검증 완료.
- 2026-06-28: Edit 모드에서 검색 갱신 시 커서가 이동하는 문제를 수정하고 Debug 빌드 재검증 완료.
- 2026-06-28: Edit 모드 검색 이동 시에만 textarea 포커스를 넘기도록 조정.
- 2026-06-28: 폴더 패널 공용 단축키를 WPF 명령 라우팅으로 정리하는 작업을 함께 진행.
- 2026-06-28: 폴더 패널 `Copy`/`Cut`/`Paste`를 `FolderBrowser` 명령 바인딩으로 분리하고, 즐겨찾기 복사 예외를 유지하도록 마무리.

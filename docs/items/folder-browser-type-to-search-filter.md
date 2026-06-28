# 폴더 패널 일반 입력을 파일검색 필터로 자동 연결

- 상태: Done

## 요구사항

폴더 컴포넌트에서 일반적인 텍스트나 숫자를 입력하면 파일검색 필터에 텍스트가 입력되고 즉시 필터링되어야 한다.
파일검색 필터(`파일 및 폴더 이름`)가 활성화된 상태에서 `↓`를 누르면 폴더 컴포넌트의 파일 목록 그리드로 포커스를 내려야 한다.

## 원인 분석 또는 설계

- 폴더 패널의 파일 목록은 `SearchBox_TextChanged`에서 자동 필터링되지만, 현재는 사용자가 검색 박스에 직접 포커스를 옮겨야만 동작한다.
- 패널 내부의 일반 문자 입력을 `PreviewTextInput` 단계에서 받아 `SearchBox`로 전달하면 별도 검색 진입 없이 즉시 필터링할 수 있다.
- 경로 입력(`PathBox`)이나 검색 입력(`SearchBox`)처럼 사용자가 직접 타이핑 중인 편집 컨트롤은 기존 동작을 유지해야 한다.
- 검색 필터에서 `↓` 처리 시 `ListView.Focus()`만으로는 실제 항목 컨테이너에 키보드 포커스가 이동하지 않을 수 있다.
- 따라서 필터 결과의 첫 번째 표시 항목을 선택한 뒤 `ScrollIntoView()`와 `Dispatcher`를 이용해 생성된 `ListViewItem` 컨테이너까지 직접 포커스를 줘야 한다.

## 구현 내용

- `FolderBrowser` 루트에서 일반 문자열 입력을 가로채 검색 필터로 전달하는 입력 라우팅을 추가한다.
- 경로 입력창과 검색 입력창처럼 편집 중인 컨트롤은 예외 처리한다.
- `PreviewTextInput` 이벤트를 루트 `UserControl`에 연결하고, `Control`/`Alt`/`Windows` 조합과 제어 문자는 제외한다.
- 일반 입력이 들어오면 `SearchBox` 끝에 문자열을 추가하고 즉시 포커스를 이동시켜 기존 `TextChanged` 기반 필터링을 그대로 재사용한다.
- README의 폴더 패널 기능 설명에 자동 검색 입력 동작을 반영했다.
- 검색 필터가 활성화된 상태에서 `↓`를 누르면 필터링된 파일 목록으로 포커스를 내려보내는 키 처리도 함께 추가한다.
- 검색 필터의 방향키 처리는 `PreviewKeyDown` 단계에서 가로채고, 첫 번째 표시 항목 컨테이너를 직접 포커스하도록 보완한다.

## 변경 파일

- `docs/items/folder-browser-type-to-search-filter.md`
- `Folderss/Controls/FolderBrowser.xaml`
- `Folderss/Controls/FolderBrowser.xaml.cs`
- `README.md`

## 검증

- `dotnet build .\Folderss.sln -c Debug`
- 결과: Exit 0, 오류 0개
- 기존 경고: NuGet 취약성 데이터 조회 실패 `NU1900`, Windows 전용 API 분석 경고 `CA1416`, `WebClient` 사용 경고 `SYSLIB0014`
- `dotnet build .\Folderss.sln -c Debug`
- 결과: 현재 실행 중인 `D:\git\cshap\Folderss\Folderss\bin\Debug\net8.0-windows\Folderss.exe` 프로세스(`PID 49092`)가 출력 파일을 잠가 `MSB3027`, `MSB3021`로 실패
- `dotnet build .\Folderss.sln -c Debug`
- 결과: Exit 0, 오류 0개
- 현재 경고: NuGet 취약성 데이터 조회 실패 `NU1900`
- `dotnet build .\Folderss.sln -c Debug`
- 결과: Exit 0, 오류 0개
- 기존 경고: NuGet 취약성 데이터 조회 실패 `NU1900`, Windows 전용 API 분석 경고 `CA1416`, `WebClient` 사용 경고 `SYSLIB0014`

## 변경 이력

- 2026-06-28: 요청 접수, 로컬 아이템 문서 생성.
- 2026-06-28: 작업 시작, 입력 라우팅 방식으로 구현 진행.
- 2026-06-28: 폴더 패널 일반 입력을 파일검색 필터로 자동 연결하고 Debug 빌드 검증 완료.
- 2026-06-28: 검색 필터에서 `↓`로 파일 목록 포커스를 이동하는 추가 수정 진행.
- 2026-06-28: 추가 수정 구현 완료. Debug 빌드는 실행 중인 `Folderss.exe` 파일 잠금으로 재검증 대기.
- 2026-06-28: 실행 중인 앱 종료 후 Debug 빌드 재검증 완료, 상태를 `Ready for Verification`로 갱신.
- 2026-06-28: `↓` 포커스 이동이 실제 목록 항목으로 넘어가지 않는 문제를 재수정.
- 2026-06-28: `PreviewKeyDown`과 `ListViewItem` 직접 포커스 방식으로 수정 후 Debug 빌드 재검증 완료.
- 2026-06-28: 사용자 확인 완료, 상태를 `Done`으로 변경.

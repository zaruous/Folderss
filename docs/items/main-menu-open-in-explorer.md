# 메인 메뉴에 탐색기로 열기 추가

- 상태: Ready for Verification

## 요구사항

프로그램 메인 메뉴(제목 표시줄 "⋯" 버튼의 ContextMenu)에서 현재 선택한 폴더를
Windows 탐색기로 여는 메뉴 항목과 기능을 추가한다.

추가 요청:
- 메뉴 항목 위치를 "보기" 메뉴 항목 하단(다음)으로 이동한다.
- 파일을 선택한 상태에서 실행하면 그 파일의 상위 디렉토리를 열되, 탐색기에서
  해당 파일이 선택된 모습으로 띄운다.

## 원인 분석 또는 설계

- 기존에는 `FavoritesPanel`에만 "탐색기로 열기" 기능이 있었고(`FavoritesPanel.xaml.cs`
  `OpenInExplorer_Click`), 메인 메뉴(`MainWindow.xaml`의 `MainContextMenu`)와
  `FolderBrowser` 패널에는 동일 기능이 없었다.
- 메인 메뉴는 파일/편집/보기 같은 최상위 메뉴 그룹 없이 단일 `ContextMenu`로
  구성되어 있으므로, 폴더 조작 관련 항목들(새로 고침 등) 옆에 항목을 추가한다.
- 대상 경로는 활성 패널(`ActivePane`) 기준으로 결정한다. 선택된 항목이 폴더이면
  그 폴더를, 아니면(파일 선택 또는 선택 없음) 현재 패널이 열고 있는 폴더
  (`ActivePane.CurrentPath`)를 연다.
- 탐색기 실행은 `FavoritesPanel.OpenInExplorer_Click`과 동일하게
  `Process.Start(new ProcessStartInfo("explorer.exe", "\"<path>\"") { UseShellExecute = true })`
  패턴을 그대로 사용한다.

## 구현 내용

- `MainWindow.xaml`: `MainContextMenu`의 "새로 고침" 항목 다음에
  `MenuItem Header="탐색기로 열기" Click="OpenInExplorer_Click"` 추가.
- `MainWindow.xaml.cs`: `OpenInExplorer_Click` 핸들러 추가.
  - `ActivePane.SelectedItem`이 폴더면 그 경로, 아니면 `ActivePane.CurrentPath` 사용.
  - 경로가 없거나 존재하지 않으면 오류 메시지 박스 표시.
  - `explorer.exe`를 `UseShellExecute = true`로 실행, 예외 시 메시지 박스 표시.

추가 수정:
- `MainWindow.xaml`: "탐색기로 열기" `MenuItem`을 "보기" 서브메뉴 `MenuItem` 바로
  다음(테마 메뉴 앞)으로 이동.
- `MainWindow.xaml.cs`: 선택 항목이 파일(`!IsDirectory`)이면
  `explorer.exe /select,"<파일경로>"` 인자로 실행해 상위 폴더에서 해당 파일이
  선택된 상태로 열리도록 분기 추가. 폴더 선택 또는 선택 없음인 경우는 기존과
  동일하게 폴더 경로를 그대로 연다.

## 변경 파일

- `Folderss/MainWindow.xaml`
- `Folderss/MainWindow.xaml.cs`

## 검증

- 코드 리뷰: `FavoritesPanel.xaml.cs`의 기존 "탐색기로 열기" 구현과 동일한 패턴으로
  작성, `FileSystemItem.IsDirectory`/`FullPath`, `FolderBrowser.CurrentPath`/`SelectedItem`
  프로퍼티 시그니처 확인 완료.
- 주의: 현재 작업 환경(Linux 컨테이너)에는 MSBuild/Windows가 없어 실제 빌드
  (`Folderss.sln` `/p:Configuration=Debug`)를 실행해 `Exit: 0`을 확인하지 못했다.
  Windows 환경에서 빌드 및 실제 동작(선택 폴더/파일 상태에서 메뉴 클릭) 확인이 필요하다.

## 변경 이력

- 최초 구현: 메인 메뉴에 "탐색기로 열기" 항목과 핸들러 추가.
- 추가 수정: 메뉴 위치를 "보기" 하단으로 이동, 파일 선택 시 `/select,`로 상위
  폴더에서 파일이 선택된 상태로 열리도록 변경.

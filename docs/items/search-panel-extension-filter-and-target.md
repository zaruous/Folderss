# 검색 팝업 확장자 필터 및 검색 대상(내용/파일명) 선택

- 상태: Ready for Verification (빌드는 사용자 환경에서 확인 필요)

## 요구사항

`Ctrl+F`로 여는 파일 검색 팝업(`SearchPanel`)에 다음 기능을 추가한다.

- 확장자 필터: 지정한 확장자를 가진 파일만 검색 대상으로 포함 (비우면 전체 확장자)
- 검색 대상 선택: 파일 내용 검색과 파일명 검색 중 선택

## 원인 분석 또는 설계

기존 `SearchService.SearchAsync`는 항상 파일 내용을 라인 단위로 스캔했고, 확장자 필터가 없어 전체 파일을 대상으로 했다.
`SearchTarget` enum(`Content`/`FileName`)을 추가하고, `SearchService`가 대상에 따라 기존 라인 스캔(`ScanFile`) 또는
파일명만 비교하는 새 경로(`ScanFileName`)로 분기하도록 했다. 확장자 필터는 `Directory.EnumerateFiles` 순회 시
공통으로 적용해 두 대상 모두에서 동작하게 했다. 파일명 검색 결과는 라인 정보가 없으므로 `LineNumber = 0`으로
표시하고, 결과 목록에서는 0을 빈 칸으로 표시하도록 UI 트리거를 추가했다.

## 구현 내용

- `Folderss/Models/SearchTarget.cs` 추가 (`Content`, `FileName`).
- `SearchService.SearchAsync`에 `SearchTarget target`, `string extensionFilter` 파라미터 추가.
  - `ParseExtensions`로 `cs, txt` 같은 입력을 `{ ".cs", ".txt" }` 집합으로 정규화 (콤마/세미콜론/공백 구분, `.` 접두사 보정).
  - 파일명 검색 전용 `ScanFileName` 추가 (파일을 열지 않고 이름만 비교, 대/소문자·정규식 옵션 공통 적용).
- `SearchPanel.xaml`에 검색 대상 `ComboBox`(`TargetCombo`: 내용 검색/파일명 검색)와 확장자 필터 `TextBox`(`ExtBox`, watermark 포함) 추가.
- `SearchPanel.xaml.cs`에서 `TargetCombo`/`ExtBox` 값을 읽어 `SearchService.SearchAsync` 호출에 전달. `ExtBox_TextChanged`에서 진행 중인 검색을 취소(옵션 변경 시 재검색 유도).
- 결과 목록 `줄` 컬럼에서 `LineNumber == 0`(파일명 검색 결과)이면 빈 칸으로 표시하는 `DataTrigger` 추가.
- 검색 창 제목을 `파일 내용 검색` → `파일 검색`으로 변경 (두 대상을 모두 포괄).
- `README.md`, `docs/architecture.md`, `docs/keyboard-shortcuts.md`의 관련 설명을 새 기능에 맞게 갱신.

## 변경 파일

- `Folderss/Models/SearchTarget.cs`
- `Folderss/Services/SearchService.cs`
- `Folderss/Controls/SearchPanel.xaml`
- `Folderss/Controls/SearchPanel.xaml.cs`
- `Folderss/MainWindow.xaml.cs`
- `README.md`
- `docs/architecture.md`
- `docs/keyboard-shortcuts.md`
- `docs/items/search-panel-extension-filter-and-target.md`

## 검증

- 이 세션은 Linux 원격 실행 환경이라 .NET SDK가 없어 `dotnet build`를 직접 실행하지 못했다.
- Windows 개발 환경에서 `dotnet build .\Folderss.sln -c Debug` 실행 및 아래 시나리오 수동 확인 필요.
  - 확장자 필터에 `cs`만 입력 후 검색 → `.cs` 파일만 결과에 나타나는지
  - 검색 대상을 `파일명 검색`으로 전환 후 검색 → 파일 내용이 아니라 파일명 일치 기준으로 결과가 나오는지, 줄 번호 칸이 비어 있는지
  - 확장자 필터를 비운 상태 → 기존과 동일하게 전체 확장자 대상으로 동작하는지 (회귀 확인)

## 변경 이력

- 2026-07-15: 확장자 필터, 검색 대상(내용/파일명) 선택 기능 구현. 빌드 검증은 Windows 환경에서 별도 확인 필요.

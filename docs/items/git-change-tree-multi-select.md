# Git 창 변경 사항 트리 보기 다중 선택

- 상태: Ready for Verification

## 요구사항

- Git 창 변경 사항의 `트리로 보기`에서 파일을 여러 개 고를 수 없다(평면 목록은 `Ctrl`/`Shift` 클릭이 된다).
- 변경됨·스테이지됨 트리에서 `Ctrl+클릭`(하나씩 더하기/빼기)과 `Shift+클릭`(범위)으로 여러 노드를 골라
  스테이지·언스테이지·되돌리기·`Delete`(휴지통)·끌어 놓기·stash "고른 파일만"에 쓸 수 있어야 한다.
- 변경 없음 트리는 보기 전용이라 대상이 아니다.

## 원인 분석 또는 설계

- WPF `TreeView`는 단일 선택만 지원한다. `SelectedChangeEntries`도 트리에서는 `tree.SelectedItem` 하나만 썼다.
- 다중 선택은 노드의 `GitChangeNode.IsMarked`(INotifyPropertyChanged)로 따로 관리하고, 표시도 이것으로만 한다.
  - 변경됨·스테이지됨 트리는 `GitChangeMultiTreeStyle`을 써서 TreeView 자체 선택 강조(`HighlightBrushKey` 계열)를 투명으로 바꾸고,
    항목 템플릿의 `IsMarked` 트리거가 `SelectionBrush` 배경을 칠한다. 변경 없음 트리는 기존 스타일 그대로(기본 선택 강조 유지).
- 클릭 처리(`ChangeDrag_PreviewMouseLeftButtonDown` → `HandleTreeClick`):
  - `Ctrl+클릭`: 그 노드 토글, 기준점 갱신. TreeView가 단일 선택으로 바꾸지 않게 이벤트를 막는다.
  - `Shift+클릭`: 화면에 보이는(펼쳐진) 순서에서 기준점~누른 노드만 고른다(`GitChangeTree.Range`). 기준점이 없거나 안 보이면 누른 노드 하나.
  - 수정키 없는 클릭: 그 노드 하나만. 여러 개 고른 상태에서 고른 노드를 누르면 평면 목록처럼 "하나만 선택"을 놓을 때까지 미뤄 끌기로 함께 옮길 수 있게 한다.
  - 키보드 이동 등 TreeView 자체 선택이 바뀌면 그 노드 하나만 고른다(`SelectedItemChanged` → `MarkOnly`).
- 대상 파일: `GitChangeTree.MarkedEntries` — 고른 노드의 파일 전부(폴더면 그 아래 전체), 폴더와 그 안 파일을 함께 골라도 한 번만.
- 여러 개 고르면 오른쪽 diff는 평면 목록과 같은 문구("파일 하나를 선택하면 차이를 보여 줍니다.")로 비운다.

## 구현 내용

- `GitChangeNode.IsMarked` 추가(변경 알림).
- `GitChangeTree.AllNodes`, `MarkedEntries`, `Range` 추가(순수 로직, 단위 테스트).
- `GitWindow.xaml` — `GitChangeTreeItemStyle` 분리, 항목 템플릿에 `IsMarked` 강조, `GitChangeMultiTreeStyle`을 변경됨·스테이지됨 트리에 적용.
- `GitWindow.xaml.cs` — `HandleTreeClick`, `MarkOnly`, `VisibleTreeNodes`, `TreeNodes`, 놓을 때 미룬 단일 선택 처리,
  `SelectedChangeEntries` 트리 분기를 `MarkedEntries`로 변경.
- 펼침 단추(▸)를 누를 때는 선택을 바꾸지 않는다.

## 변경 파일

- `Folderss/Models/GitModels.cs`
- `Folderss/Services/GitChangeTree.cs`
- `Folderss/GitWindow.xaml`
- `Folderss/GitWindow.xaml.cs`
- `tests/Folderss.SearchTests/GitTests.cs`
- `README.md`
- `docs/items/git-change-tree-multi-select.md`

## 검증

- 단위 테스트(`dotnet test tests/Folderss.SearchTests --filter GitTests`, Linux): 73개 통과.
  - `ChangeTree_MarkedEntries_UnionsMarkedNodes_WithoutDuplicates` — 폴더+그 안 파일 중복 제거, 트리 순서.
  - `ChangeTree_Range_IsInclusive_InEitherDirection` — 양방향 범위, 같은 노드, 기준점 없음.
  - 구현 전 컴파일 실패(멤버 없음)를 확인한 뒤 구현.
- Linux에서 `-p:EnableWindowsTargeting=true`로 앱 컴파일: 기존부터 있던 `ConsolePanel`의 `Microsoft.Terminal`(Windows 전용 패키지) 오류 1건 외 새 오류 없음.
  Windows에서 `dotnet build .\Folderss.sln -c Debug` 확인 필요.
- WPF 마우스 동작은 이 환경에서 실행할 수 없어 Windows에서 수동 확인 필요:
  1. 트리 보기 변경됨에서 A 클릭 → `Ctrl`+C 클릭: A, C 강조. `Ctrl`+C 다시: C만 해제.
  2. A 클릭 → `Shift`+D 클릭: A~D(펼쳐진 순서) 강조. 접힌 폴더 안 파일은 범위에 안 들어감(폴더 노드 자체는 들어감 → 그 아래 전체 대상).
  3. 여러 개 고른 뒤 `스테이지 ↓`, `되돌리기…`, `Delete`가 고른 파일 전부에 적용. 스테이지됨에서 `↑ 언스테이지`도 같음.
  4. 여러 개 고른 상태에서 고른 노드를 끌어 반대쪽 목록에 놓으면 전부 옮겨짐. 끌지 않고 클릭만 하면 그 노드 하나만 남음.
  5. 수정키 없는 클릭, 방향키 이동, 더블클릭 열기, 펼침 단추 접기/펴기가 기존처럼 동작. 테마 전환 후에도 강조색이 보이고 기본 파란 선택 강조가 겹치지 않음.
  6. 변경 없음 트리는 기존처럼 단일 선택·기본 강조.

## 변경 이력

- 2026-10-16: 요청 접수(트리일 때 멀티 선택 불가). 범위: `Ctrl`+`Shift` 클릭, 변경됨·스테이지됨 트리. 구현·단위 테스트 완료, Windows 빌드·수동 확인 대기.

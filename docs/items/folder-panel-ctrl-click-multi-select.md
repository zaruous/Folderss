# 폴더 패널 Ctrl 클릭 개별 다중 선택

- 상태: In Progress

## 요구사항

- 폴더 패널 파일 목록에서 `Shift` 클릭으로 범위 선택은 되지만 `Ctrl` 클릭으로 떨어진 파일을 하나씩 골라 선택하는 기능이 동작하지 않는다.
- 두 파일 비교(`선택한 두 파일 비교`)에 서로 떨어진 파일 두 개를 고르기 위해 필요하다.

## 원인 분석 또는 설계

- `FileList`는 `SelectionMode="Extended"`라 WPF ListView 자체가 `Ctrl` 클릭 토글, `Shift` 클릭 범위 선택을 지원한다.
- 그런데 `FolderBrowser.FileList_PreviewMouseLeftButtonDown`(드래그 시작용 핸들러)이 수정키와 무관하게 "클릭한 항목이 미선택이면 선택을 모두 지우고 그 항목만 선택"한다.
  - `Preview` 이벤트라 ListView의 기본 처리보다 먼저 실행된다.
  - `Ctrl` 클릭 시: 핸들러가 기존 선택을 지우고 클릭 항목을 선택 → 이어서 ListView의 `Ctrl` 토글이 방금 선택된 항목을 다시 해제. 결과적으로 기존 선택은 사라지고 새 항목도 선택되지 않는다.
  - `Shift` 클릭 시: 핸들러가 지워도 ListView가 앵커부터 범위를 다시 선택하므로 겉보기에는 동작한다.
- 수정: `Ctrl`이 눌린 경우 이 핸들러는 드래그 시작점만 기록하고 선택을 건드리지 않는다. 나머지(수정키 없는 클릭)는 기존대로 둔다.

## 구현 내용

- `FileList_PreviewMouseLeftButtonDown`에서 `Keyboard.Modifiers`에 `Control`이 포함되면 선택 변경 없이 반환한다.

## 변경 파일

- `Folderss/Controls/FolderBrowser.xaml.cs`
- `README.md`
- `docs/items/folder-panel-ctrl-click-multi-select.md`

## 검증

- 작업 환경(Linux, dotnet 없음)에서는 WPF 빌드와 마우스 동작 확인이 불가해 자체 검증을 못 했다. Windows에서 아래를 확인해야 한다.
  1. `dotnet build .\Folderss.sln -c Debug` 성공.
  2. 파일 목록에서 A 클릭 → `Ctrl`+C 클릭: A, C 두 개가 선택됨. `Ctrl`+C 다시 클릭: C만 해제됨.
  3. A, C 선택 상태에서 우클릭 > `선택한 두 파일 비교` 메뉴가 보이고 비교 창이 열림.
  4. `Shift` 클릭 범위 선택, 수정키 없는 클릭(단일 선택), 선택 항목 드래그 앤 드롭이 기존과 같이 동작함.
- WPF 마우스 상호작용은 현재 테스트 프로젝트(`tests/Folderss.SearchTests`, net8.0 서비스 단위 테스트)로 재현할 수 없어 자동 테스트는 추가하지 않았다.

## 변경 이력

- 2026-10-08: 요청 접수, 원인 분석, 수정 반영. Windows 빌드·수동 확인 대기.

# 검색 팝업 기본 선택을 파일명 검색·하위 폴더 포함으로 변경

- 상태: Ready for Verification

## 요구사항

`Ctrl+F` 검색 팝업을 열었을 때 검색 대상은 `파일명 검색`, 범위는 `하위 폴더 포함`이 기본으로 선택되어 있어야 한다.
(기존 기본값: `내용 검색`, `현재 폴더만`)

## 원인 분석 또는 설계

- 기본 선택은 `Controls/SearchPanel.xaml`의 `TargetCombo`·`ScopeCombo` 안 `ComboBoxItem`의 `IsSelected="True"`로만 정해진다. 코드 비하인드는 실행 시점에 `SelectedItem.Tag`(`filename` / `recursive`)를 읽기만 하므로 XAML의 `IsSelected` 위치만 옮기면 된다.
- `ScopeCombo_SelectionChanged`는 `IsInitialized` 검사가 있어 초기 선택으로 인해 검색 취소나 상태 문구가 발생하지 않는다.
- 검색 창은 열려 있는 동안 상태를 유지하므로, 사용자가 바꾼 선택은 창을 닫기 전까지 그대로 남는다. 기본값은 창이 새로 만들어질 때만 적용된다.

## 구현 내용

- `Controls/SearchPanel.xaml`
  - `ScopeCombo`: `IsSelected="True"`를 `현재 폴더만`에서 `하위 폴더 포함`(`Tag="recursive"`)으로 이동
  - `TargetCombo`: `IsSelected="True"`를 `내용 검색`에서 `파일명 검색`(`Tag="filename"`)으로 이동
- `README.md`, `docs/architecture.md`에 기본 선택 명시

## 변경 파일

- `Folderss/Controls/SearchPanel.xaml`
- `README.md`
- `docs/architecture.md`
- `docs/items/search-panel-default-options.md` (신규)

## 검증

- [x] `dotnet build .\Folderss.sln -c Debug` — Exit 0
- [ ] 앱에서 `Ctrl+F`로 검색 창을 열어 `파일명 검색`·`하위 폴더 포함`이 선택된 상태인지 확인
- [ ] 검색어 `*.md` 입력 후 Enter → 하위 폴더까지 파일명 결과가 나오는지 확인

## 참고

- `내용` 컬럼 토글은 기존처럼 켜진 상태로 두었다. 파일명 검색 결과에서는 이 컬럼이 비어 있으므로, 기본으로 끄기를 원하면 `ContentColumnToggle`의 `IsChecked`를 함께 바꾸면 된다.

## 변경 이력

- 2026-09-22: 기본 선택 변경, 항목 생성 (Ready for Verification)

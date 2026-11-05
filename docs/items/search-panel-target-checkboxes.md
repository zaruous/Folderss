# 검색 팝업 검색 대상을 체크박스로 (파일명·파일 내용 동시 검색)

- 상태: Ready for Verification

## 요구사항

`Ctrl+F` 파일 검색 창의 검색 대상이 콤보(`내용 검색`/`파일명 검색`) 하나만 고르는 방식이라, 둘 다 보려면 콤보를 바꿔 두 번 검색해야 했다.
체크박스로 `파일명`·`파일 내용` 중 하나 또는 둘 다 골라 한 번에 검색할 수 있게 한다.

## 설계·결정

- `SearchTarget`을 `[Flags]`(`Content = 1`, `FileName = 2`)로 바꿨다. 값 0을 플래그로 쓰지 않으려고 기존 값(0/1)을 바꿨다.
  이 enum은 저장되거나 플러그인 계약에 노출되지 않으므로(검색 결과 `grep` 확인) 값 변경의 호환 문제는 없다.
- `SearchService.SearchAsync`는 파일마다 파일명 비교 → 내용 스캔 순으로 고른 대상만 실행한다. 순회는 한 번.
- 와일드카드(`*`, `?`)는 기존대로 파일명에만 적용하고, 내용은 `*`를 문자 그대로 찾는다. 그래서 정규식을 대상별로 따로 만든다(`BuildRegex(..., forFileName)`).
  둘 다 체크하고 `*.cs`를 찾으면 파일명은 패턴 일치, 내용은 `*.cs`라는 글자 일치다.
- 둘 다 체크하면 같은 파일에서 파일명 일치 1건(줄 0, `(파일명 일치)`)과 내용 일치 줄들이 함께 나온다. 결과는 파일명으로 묶이므로 한 묶음에 보인다.
  `패널에 필터 적용`은 기존처럼 경로 중복을 제거한다.
- 둘 다 끄면 검색하지 않고 상태 줄에 "검색 대상을 하나 이상 선택하세요"를 표시한다(체크를 강제로 되살리지 않음).
- 기본값은 기존과 같은 `파일명`만 체크.

## 위험·한계

- 둘 다 체크하면 모든 파일을 열어 읽으므로 파일명만 검색할 때보다 훨씬 느리다(내용 검색 단독과 같은 비용).
- 결과 목록 `내용` 컬럼 토글(`ContentColumnToggle`, 표시 문구 "내용")과 대상 체크박스 "파일 내용"이 이름이 비슷하다. 체크박스는 "파일 내용"으로 구분했다.

## 변경 파일

- `Folderss/Models/SearchTarget.cs`
- `Folderss/Services/SearchService.cs`
- `Folderss/Controls/SearchPanel.xaml`, `SearchPanel.xaml.cs`
- `tests/Folderss.SearchTests/SearchServiceTests.cs` — `Both_ReturnsFileNameAndContentMatches`, `Both_WildcardAppliesToFileNameOnly`
- `README.md`, `docs/architecture.md`, `docs/keyboard-shortcuts.md`

## 검증

- [x] 새 테스트 2건이 수정 전 실패, 수정 후 통과 확인
- [x] `dotnet test tests/Folderss.SearchTests` (Linux, .NET 8.0.427) — 실패 0, 통과 1142, 건너뜀 5
- [ ] `dotnet build .\Folderss.sln -c Debug` (Windows) — Linux에서는 `EnableWindowsTargeting`으로 컴파일을 시도했으나, 이번 변경과 무관한 `ConsolePanel.xaml.cs`의 `Microsoft.Terminal` 참조 오류 1건으로 끝까지 빌드되지 않음. `SearchPanel` 관련 오류는 없었음
- [ ] 앱에서 확인: 파일명만 / 파일 내용만 / 둘 다 / 둘 다 해제(안내 문구) 각각 검색, 체크 변경 시 "검색 조건이 바뀌었습니다" 안내

## 변경 이력

- 2026-11-04: 구현 (Ready for Verification)

# 폴더 비교 테스트 — Linux에서만 실패하던 2건

- 상태: Ready for Verification — 테스트 수정, Windows CI 확인 대기

## 요구사항

- `DirectoryCompareServiceTests`의 2건이 Linux에서만 실패해, 이 환경에서 회귀를 확인할 때마다 잡음이 됐다. 앱 동작과는 무관하다.

## 원인 분석 또는 설계

- `Folder_IsDifferentWhenAnyChildDiffers_AndNotCountedItself`: 순서 단언에서 경로를 `\` 그대로 썼다. 같은 테스트에 OS 구분자로 바꾸는 `P()`를 만들어 놓고 쓰지 않았다. Linux에서는 실제 경로가 `src/a.txt`라 `IndexOf`가 -1이 되어 실패했다.
- `IgnoreRules_SkipBothSidedMatches_AndNoteOneSidedRule`: Linux의 .NET은 점으로 시작하는 파일에 `Hidden` 속성을 붙인다(`.folderssignore` → `Hidden` 확인). 기본 옵션이 숨김 제외라 `.folderssignore` 항목이 결과에서 빠졌다. ignore 규칙 판정 자체는 정상이다.

## 구현 내용

- 순서 단언 2줄을 `P(...)`로 감쌌다.
- ignore 테스트의 첫 비교를 `IncludeHidden = true`로 실행한다. Windows에서는 점 파일이 숨김이 아니므로 결과가 같다.

## 변경 파일

- `tests/Folderss.SearchTests/DirectoryCompareServiceTests.cs`

## 검증

- Linux: 두 테스트 통과. 전체 333 통과·3 건너뜀·0 실패.
- Windows CI에서 계속 통과하는지 확인 필요.

## 변경 이력

- 2026-10-15: 원인 확인 후 테스트만 수정(Ready for Verification).

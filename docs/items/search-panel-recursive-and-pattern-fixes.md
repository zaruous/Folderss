# 검색 패널 하위 폴더 검색 버그 및 패턴 검색·내용 컬럼 토글

- 상태: Ready for Verification (Windows 실제 앱 동작은 사용자 환경에서 확인 필요)

## 요구사항

`Ctrl+F` 파일 검색 팝업(`SearchPanel`)에 대한 요청 3건.

1. 파일명 검색에서 `하위 폴더 포함`을 선택하면 검색 결과가 나오지 않는 버그 수정
2. 파일 내용(일치한 줄)이 결과 목록에 표시되는지를 옵션으로 켜고 끌 수 있게 할 것
3. 확장자 전용 입력란을 없애고, 검색어 입력란 하나로 패턴 검색을 지원할 것

## 원인 분석 또는 설계

### 1. 하위 폴더 포함 검색이 결과를 내지 못하던 원인

`SearchService.SearchAsync`는 `Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)`로 순회했다.
이 오버로드는 내부적으로 `EnumerationOptions.CompatibleRecursive`(= `IgnoreInaccessible = false`)를 쓰기 때문에
하위 폴더 중 하나라도 접근 권한이 없으면 열거자의 `MoveNext()` 단계에서 `UnauthorizedAccessException`이 난다.
기존 `try/catch`는 `foreach` **본문** 안에만 있어 열거자 자체의 예외를 막지 못했고, 예외가 `Task.Run` 밖으로
전파되어 `StartSearch`의 `catch (Exception)`에 걸려 결과 없이 `검색 오류: …`로 끝났다.

Windows 사용자 폴더에는 `%LOCALAPPDATA%\Application Data`처럼 자기 자신을 가리키면서 ACL로 접근이 막힌 정션이
기본 존재하므로, 사용자 폴더 아래에서 하위 폴더 검색을 하면 사실상 항상 이 경로를 탔다.

접근 거부만 무시하도록 `EnumerationOptions { IgnoreInaccessible = true }`로 바꾸면 이번엔 위와 같은 정션을
따라 들어가 순환에 빠질 수 있다(결과가 무한히 쌓여 메모리까지 위험). 접근 거부 차단이 지금까지 순환을 우연히
막아주고 있었기 때문에, 두 문제를 함께 처리해야 한다.

그래서 폴더 단위 스택 순회(`EnumerateFiles`)를 직접 구현했다.
- 폴더마다 `GetFiles`/`GetDirectories`를 각각 `try/catch`로 감싸 한 폴더의 실패가 순회 전체를 끊지 않게 한다.
- `FileAttributes.ReparsePoint`인 하위 폴더에는 들어가지 않아 정션·심볼릭 링크 순환을 차단한다.
- `yield return`은 `catch`가 있는 `try` 안에 둘 수 없으므로, 폴더 목록을 배열로 먼저 확정한 뒤 바깥에서 넘긴다.

### 2. 내용 컬럼 표시 토글

`GridViewColumn`에는 `Visibility`가 없어 스타일로 숨길 수 없다. `GridView.Columns`에서 컬럼을 제거하고
다시 삽입하는 방식으로 구현했다. 다시 넣을 때는 `줄 / 내용 / 경로` 순서가 유지되도록 원래 인덱스(1)에 삽입한다.

### 3. 확장자 필터 → 검색어 입력란의 와일드카드 패턴

`ExtBox`와 `ParseExtensions`를 제거하고, 검색어 자체가 `*`/`?`를 포함하면 와일드카드 패턴으로 해석한다.
적용 범위는 **파일명 검색 + 정규식 옵션 꺼짐**일 때로 한정했다.
- 확장자 필터를 대체하는 용도라 `*.cs`가 `a.cs.bak`에 걸리면 안 되므로 패턴은 양끝을 고정(`^…$`)한다.
- 와일드카드가 없으면 기존처럼 부분 일치를 유지한다(`report` → `monthly-report.txt`).
- 내용 검색에서 줄 텍스트에 와일드카드를 적용하는 것은 부자연스럽고 기존 동작을 깨므로 제외했다.

알려진 트레이드오프: 확장자 필터가 사라지면서 **내용 검색의 대상 파일을 좁힐 수단이 없어졌다.**
내용 검색은 이제 폴더 안 모든 파일의 앞 4KB를 읽어 바이너리 판별을 하므로 대용량 폴더에서 이전보다 느리다.
(요청에 따른 의도된 변경이며, 필요해지면 파일명 패턴을 내용 검색의 사전 필터로 함께 적용하는 방식으로 확장 가능)

## 구현 내용

- `Folderss/Services/SearchService.cs`
  - `SearchAsync`에서 `extensionFilter` 파라미터와 `ParseExtensions` 제거.
  - 접근 거부·순환 링크에 내성이 있는 폴더 단위 스택 순회 `EnumerateFiles` 추가. `IsReparsePoint`로 링크 폴더 제외.
  - `BuildRegex`/`HasWildcard`/`BuildWildcardPattern` 추가 — 파일명 검색의 와일드카드 패턴을 양끝 고정 정규식으로 변환.
- `Folderss/Controls/SearchPanel.xaml`
  - 확장자 필터 `ExtBox` 제거, `내용` 컬럼 표시 토글 `ContentColumnToggle` 추가(기본 켜짐).
  - 검색어 입력란 워터마크·툴팁을 패턴 안내로 변경, `내용` 컬럼에 `x:Name="ContentColumn"` 부여.
- `Folderss/Controls/SearchPanel.xaml.cs`
  - `ExtBox_TextChanged` 제거, `SearchAsync` 호출에서 확장자 인자 제거.
  - `ContentColumnToggle_Changed` / `UpdateContentColumnVisibility` 추가.
- `tests/Folderss.SearchTests` 신규 — 검색 로직 회귀 테스트(아래 검증 참고).

## 변경 파일

- `Folderss/Services/SearchService.cs`
- `Folderss/Controls/SearchPanel.xaml`
- `Folderss/Controls/SearchPanel.xaml.cs`
- `tests/Folderss.SearchTests/Folderss.SearchTests.csproj`
- `tests/Folderss.SearchTests/SearchServiceTests.cs`
- `README.md`
- `docs/architecture.md`
- `docs/keyboard-shortcuts.md`
- `docs/items/search-panel-recursive-and-pattern-fixes.md`

## 검증

검색 로직은 순수 `System.IO` 코드라 WPF 없이 검증할 수 있다. 본체(`net8.0-windows`)는 리눅스에서 실행할 수 없으므로
관련 소스만 링크한 `net8.0` 테스트 프로젝트를 추가했다. `Folderss.sln`에는 포함하지 않아 앱 빌드 절차는 그대로다.

```
dotnet test tests/Folderss.SearchTests
```

14개 테스트 통과. 수정 전 코드에 대해서는 아래 3개가 실패하는 것을 먼저 확인했다(재현 테스트).

| 테스트 | 수정 전 |
| --- | --- |
| `Recursive_WithInaccessibleSubdirectory_StillReturnsAccessibleMatches` | `UnauthorizedAccessException` — 접근 가능한 파일까지 포함해 결과 0건 |
| `Recursive_WithSymlinkLoop_TerminatesAndReportsEachFileOnce` | 같은 파일이 수십 번 중복 보고됨 |
| `Search_OnMissingRoot_Completes_WithoutResults` | `DirectoryNotFoundException` |

접근 거부 재현은 POSIX 권한에 의존하므로, 권한 검사를 우회하는 계정(root)이나 `chmod`가 없는 플랫폼에서는
해당 테스트가 자동으로 Skip된다(`TryDenyDirectoryListing`이 실제로 거부가 되는지 먼저 확인).
리눅스 컨테이너에서는 `capsh --drop=cap_dac_override,cap_dac_read_search`로 실제 실행해 확인했다.

빌드는 리눅스에서 `dotnet build Folderss.sln -c Debug -p:EnableWindowsTargeting=true`로 확인했다.
XAML 마크업 컴파일(`SearchPanel.g.cs`에 `ContentColumn`/`ContentColumnToggle` 필드 생성)까지 통과하며,
남는 오류는 `ConsolePanel.xaml.cs`의 `Microsoft.Terminal`(EasyWindowsTerminalControl의 Windows 전용 어셈블리)
하나뿐으로 **수정 전 트리에서도 동일하게 발생하는 환경 제약**이다.

Windows 개발 환경에서 아래를 추가로 확인 필요.
- `dotnet build .\Folderss.sln -c Debug` (Exit: 0)
- 사용자 폴더 등에서 `하위 폴더 포함` + `파일명 검색` → 결과가 나오는지 (기존 버그 재현 경로)
- `*.cs` 입력 시 `.cs` 파일만, `report?.txt` 입력 시 한 글자만 매칭되는지
- 와일드카드 없는 검색어가 기존처럼 부분 일치로 동작하는지 (회귀)
- `내용` 토글을 껐다 켰을 때 컬럼이 `줄 / 내용 / 경로` 순서로 복원되는지

## 변경 이력

- 2026-09-22: 하위 폴더 포함 검색 중단 버그 수정, 와일드카드 패턴 검색 도입(확장자 필터 제거),
  내용 컬럼 표시 토글 추가, 검색 로직 회귀 테스트 프로젝트 신규 추가.

# 좌우 패널 디렉터리 비교 (동기화 뷰) — 상세설계서

- 상태: Todo

## 요구사항

### 기능

1. 활성 패널과 반대편 패널이 보고 있는 두 폴더를 비교해, 각 패널의 목록 위에 **항목별 차이 상태**를 색·아이콘으로 겹쳐 보인다.
2. 상태는 다섯 가지다: **같음**, **다름**(크기 또는 수정 시각이 다름, 옵션으로 내용 해시), **왼콽에만**, **오른쪽에만**, **유형 다름**(한쪽은 폴더, 한쪽은 파일).
3. 하위 폴더는 그 안에 차이가 하나라도 있으면 **다름**으로 표시한다(재귀 비교). 재귀 깊이는 옵션이며 기본은 무제한.
4. 비교 모드 중 **"차이만 보기"** 토글로 같은 항목을 숨길 수 있다.
5. 비교 모드 중 한쪽 패널에서 하위 폴더로 내려가면 다른 쪽도 **같은 상대 경로**로 따라 내려간다(폴더가 없으면 빈 상태로 표시하고 "이 폴더는 반대편에 없음" 배너). 위로 올라갈 때도 같다. 비교 루트 밖으로 나가면 비교 모드가 해제된다.
6. 비교 결과 요약(같음/다름/왼쪽만/오른쪽만 개수)을 두 패널 사이가 아닌 **활성 패널 상태바**와 배너에 보인다.
7. 비교 옵션: 크기+수정 시각(기본), 내용 해시(SHA-256, 느림), 수정 시각 허용 오차(기본 2초 — FAT/네트워크 드라이브의 시각 해상도), 대소문자 무시(Windows 기본), ignore 규칙 적용(`IgnoreRuleSet`, 기본 켬), 숨김 파일 포함 여부(패널 설정 따라감).
8. 비교 결과 항목에서 기존 동작이 그대로 동작해야 한다: `F6` 이동, 반대편 복사, 삭제, 뷰어 열기. 특히 **"다름" 파일을 더블클릭하면 Diff 뷰어**로 여는 것은 후속(아이디어 문서의 Monaco Diff 뷰어)이며 이 항목에서는 기존처럼 뷰어/기본 프로그램으로 연다.
9. 비교는 백그라운드에서 돌고 취소할 수 있다. 진행 중에도 이미 판정된 항목부터 색이 들어온다.

### 비기능

- 10만 파일 트리에서 UI가 멈추지 않는다(스캔은 `Task.Run`, 결과 반영은 배치로 Dispatcher에 넘김).
- 접근 거부 폴더·순환 reparse point는 `SearchService.EnumerateFiles`와 같은 정책으로 건너뛰고 "비교 불가" 개수로 집계한다.
- 비교 모드는 세션·레이아웃에 **저장하지 않는다**(재시작하면 꺼짐). 옵션만 `compare-settings.xml`에 저장할지는 미결(아래 미결 사항).
- 기존 `FolderBrowser` 동작(필터, 정렬, 드래그 앤 드롭, 트리뷰 고정)과 충돌하지 않는다.

### 범위 밖

- 동기화 실행(복사·삭제 배치, dry-run) — `docs/아이디어.md`의 "단방향 폴더 동기화".
- 파일 단위 내용 diff 표시 — "Monaco 기반 Diff 뷰어".
- 세 폴더 이상 비교, 원격(FTP/SFTP) 비교.

## 용어

| 용어 | 뜻 |
|---|---|
| 비교 루트 | 비교를 시작한 시점의 왼쪽/오른콽 폴더 쌍. 이 아래에서만 비교 모드가 유지된다 |
| 상대 경로 | 비교 루트 기준 경로. 두 쪽 항목을 짝지우는 키(대소문자 무시) |
| 상태 | `Same`, `Different`, `LeftOnly`, `RightOnly`, `TypeMismatch`, `Unknown`(비교 불가) |
| 오버레이 | `FolderBrowser` 목록 행에 상태 색·아이콘을 겹쳐 그리는 것. 항목 모델(`FileSystemItem`)에 상태를 붙여 `DataTrigger`로 그린다 |

## 사용자 시나리오

1. **배포 전 확인** — 왼쪽 `src\out`, 오른쪽 배포 서버의 공유 폴더. 메뉴 `보기 > 디렉터리 비교`를 누르면 몇 초 뒤 새로 빌드된 DLL 5개가 "다름", 지운 설정 파일 1개가 "오른쪽에만"으로 빨갛게 보인다. "차이만 보기"를 켜 6개만 남긴 뒤 `F6`/복사로 처리하고, 비교 모드를 끈다.
2. **브랜치 작업 트리 비교** — 두 워크트리를 좌우에 열고 비교. `bin/`·`obj/`는 `.gitignore` 규칙으로 비교에서 빠져 노이즈가 없다. 하위 폴더 `Services`로 내려가면 반대쪽도 `Services`로 따라와 같은 위치를 본다.
3. **백업 검증** — 외장 디스크 백업 폴더와 원본. 수정 시각 허용 오차 2초 덕에 FAT32 디스크의 시각 반올림이 "다름"으로 오판되지 않는다. 내용 해시 옵션을 켜 확실히 확인한다(느림 경고).

## UI 설계

### 진입과 종료

```
메인 메뉴(⋯) > 보기
  ├─ 트리뷰
  ├─ 파일 내용 검색          Ctrl+F
  ├─ 디렉터리 비교            (체크 가능, 토글)   ← 신규
  │    ├─ 차이만 보기          (체크 가능, 비교 모드 중에만 활성)
  │    └─ 비교 옵션…           (다이얼로그)
  ├─ 즐겨찾기 표시
  └─ …
```

- `디렉터리 비교`를 켜면 활성 패널을 **왼쪽**, `TargetPane`을 **오른쪽**으로 잡는다(현재 `ExecuteTransfer`와 같은 규칙). 폴더 패널이 하나뿐이면 안내 후 종료.
- 단축키는 등록하지 않는다(`KeyBindingService`에 `CompareToggle` 후보만 문서화). 자주 쓰이면 추가.

### 패널 오버레이 (비교 모드 중 `FolderBrowser` 한쪽)

```
┌───────────────────────────────────────────────────────────────────────┐
│ ←  →  ↑  [C:\ ▼] [D:\work\proj\src                                  ] │
│ 필터 [ 파일 및 폴더 이름                       ]  12/40개  숨김  ignore │
│ ┃ 디렉터리 비교 — 반대편: D:\backup\proj\src   같음 28 · 다름 5 · 이쪽만 4 · 저쪽만 3 · 비교 불가 0   [차이만 ☐] [다시 비교] [해제] ┃ │
│──────────────────────────────────────────────────────────────────────│
│    │ 이름                     │ 수정한 날짜       │ 유형     │ 크기  │ 비교      │
│ 📂 │ Services                 │ 2026-09-20 11:02  │ 폴더     │       │ ≠ 다름    │  ← 하위에 차이 있음: 노란 배경
│ 📂 │ Themes                   │ 2026-09-01 09:10  │ 폴더     │       │ = 같음    │  ← 기본색
│ 📄 │ App.xaml.cs              │ 2026-09-22 22:40  │ CS 파일  │ 1 KB  │ ≠ 다름    │  ← 노란 배경
│ 📄 │ Notes.md                 │ 2026-09-22 10:00  │ MD 파일  │ 3 KB  │ ◀ 이쪽만  │  ← 초록 배경 (반대편에 없음)
│ 📄 │ old.config               │                   │          │       │ ▶ 저쪽만  │  ← 회색 이탤릭 자리표시자 행 (이쪽에 없음)
│ 📄 │ README                   │ 2026-08-30 12:00  │ 파일     │ 2 KB  │ ⚠ 유형 다름│  ← 반대편은 폴더: 빨간 배경
└───────────────────────────────────────────────────────────────────────┘
│ 40개 항목 · 비교: 다름 5, 이쪽만 4, 저쪽만 3                                │
```

- **배너**는 검색 결과 필터 배너(`ResultFilterBanner`)와 같은 자리(Grid.Row 2)에 두고, 둘이 동시에 켜지면 세로로 쌓인다.
- **비교 컬럼**은 비교 모드 중에만 `GridView.Columns`에 삽입한다(검색 패널의 `내용` 컬럼 토글과 같은 방식: `GridViewColumn`은 Visibility가 없어 넣고 뺀다). 정렬 헤더(`Tag="CompareStatus"`)도 지원해 차이 있는 것끼리 모을 수 있다.
- **자리표시자 행**("저쪽만"): 반대편에만 있는 항목을 이쪽 목록에 회색으로 넣어 두 목록의 행이 **같은 순서로 정렬**되게 한다. 자리표시자는 선택·열기·삭제가 불가하고(`FileSystemItem.IsPlaceholder`), 더블클릭 시 반대편 패널에서 그 항목을 선택한다.
- 색은 배경만 살짝 칠한다(텍스트 색은 테마 유지). 잘라내기(`IsCut`, Opacity 0.4)와 겹치면 둘 다 적용.

### 색 리소스 (모든 테마 `Themes\*.xaml`에 동일 키로 추가 — CLAUDE.md 테마 패턴)

| 키 | 용도 | Black 예시 |
|---|---|---|
| `CompareDifferentBrush` | 다름 | 노랑 계열, 알파 0x33 |
| `CompareLeftOnlyBrush` | 이쪽만 | 초록 계열, 알파 0x33 |
| `CompareRightOnlyBrush` | 저쪽만(자리표시자) | 회색, 알파 0x22 |
| `CompareTypeMismatchBrush` | 유형 다름 | 빨강 계열, 알파 0x33 |

`Black.xaml`을 기준으로 7개 테마 파일 모두에 4쌍을 넣는다. 누락된 테마에서는 `DynamicResource`가 비어 오버레이가 안 보이기만 하고 크래시는 없지만, 체크리스트에 넣어 강제한다.

### 비교 옵션 다이얼로그

```
┌ 비교 옵션 ───────────────────────────────┐
│ 판정 기준                                  │
│  (●) 크기와 수정 시각    ( ) 내용 해시(느림) │
│  수정 시각 허용 오차 [ 2 ] 초               │
│ 범위                                       │
│  [✓] .gitignore/.folderssignore 규칙 적용  │
│  [✓] 하위 폴더 재귀     최대 깊이 [ 0=무제한 ] │
│  [ ] 숨김 파일 포함 (패널 '숨김' 토글과 별개)  │
│                       [ 확인 ] [ 취소 ]      │
└────────────────────────────────────────────┘
```

옵션 변경 시 즉시 다시 비교한다.

### 동기 이동

- 한쪽 `FolderBrowser.PathChanged` → `MainWindow`가 상대 경로를 계산해 반대쪽 `NavigateTo(otherRoot + 상대 경로, addHistory: true)`. 재진입 방지 플래그(`_syncingCompareNavigation`)로 핑퐁을 막는다.
- 반대쪽에 그 폴더가 없으면 이동하지 않고 배너에 "반대편에 이 폴더가 없음"을 표시하며, 이쪽 목록은 전부 "이쪽만"이 된다.
- 어느 쪽이든 비교 루트 밖으로 나가면(`IsUnderResultFilterRoot`와 같은 판정) 비교 모드를 조용히 해제하고 상태바에 한 줄 알린다.

## 데이터 모델

```csharp
namespace Folderss.Models
{
    public enum CompareStatus { None, Same, Different, LeftOnly, RightOnly, TypeMismatch, Unknown }

    public sealed class CompareOptions
    {
        public bool UseContentHash { get; set; }          // 기본 false
        public TimeSpan TimestampTolerance { get; set; }  // 기본 2초
        public bool ApplyIgnoreRules { get; set; } = true;
        public bool Recursive { get; set; } = true;
        public int MaxDepth { get; set; }                 // 0 = 무제한
        public bool IncludeHidden { get; set; }
    }

    public sealed class CompareEntry
    {
        public string RelativePath { get; set; }          // 비교 루트 기준, 구분자 '\'
        public bool IsDirectory { get; set; }
        public CompareStatus Status { get; set; }
        public long? LeftSize { get; set; }   public DateTime? LeftModified { get; set; }
        public long? RightSize { get; set; }  public DateTime? RightModified { get; set; }
        public string Reason { get; set; }                // "크기 1,024 ≠ 2,048", "해시 불일치", "접근 거부" 등 툴팁용
    }

    public sealed class CompareResult
    {
        public string LeftRoot { get; set; }  public string RightRoot { get; set; }
        public CompareOptions Options { get; set; }
        public Dictionary<string, CompareEntry> Entries { get; }   // key = RelativePath (OrdinalIgnoreCase)
        public int SameCount, DifferentCount, LeftOnlyCount, RightOnlyCount, UnknownCount;
        public bool IsComplete { get; set; }              // 취소되면 false
    }
}
```

- `FileSystemItem`에 `CompareStatus CompareStatus { get; set; }`, `string CompareReason`, `bool IsPlaceholder`를 추가한다. 오버레이는 이 세 값만 본다.
- 폴더의 상태는 자식 상태의 **집계**다: 자식 중 `Different/LeftOnly/RightOnly/TypeMismatch`가 하나라도 있으면 `Different`, 전부 `Same`이면 `Same`, `Unknown`만 있으면 `Unknown`.

## 서비스 설계 — `Services/DirectoryCompareService.cs` (신규)

```csharp
public static class DirectoryCompareService
{
    public static Task<CompareResult> CompareAsync(
        string leftRoot, string rightRoot, CompareOptions options,
        IProgress<CompareEntry> progress, CancellationToken token);
}
```

알고리즘(폴더 단위 스택 순회, `SearchService.EnumerateFiles`와 같은 골격):

1. 스택에 상대 경로 `""`를 넣고 시작. 각 상대 경로에 대해 왼쪽·오른쪽 폴더의 직속 항목을 각각 `DirectoryInfo.EnumerateFileSystemInfos()`로 읽는다. 접근 거부·IO 오류는 그 폴더를 `Unknown`으로 집계하고 계속한다.
2. `options.ApplyIgnoreRules`면 각 쪽에서 `IgnoreRuleSet.LoadFor(폴더)`로 규칙을 읽고(폴더마다 새로 읽되 결과를 경로별로 캐시) 걸리는 항목은 양쪽 모두에서 제외한다. 한쪽만 ignore되는 경우(규칙 파일이 한쪽에만 있음)는 **비교 대상에서 빼지 않고** `Reason`에 "한쪽만 ignore"를 남긴다 — 규칙 파일 차이 자체가 발견하고 싶은 차이일 수 있다.
3. 이름(대소문자 무시)으로 짝을 맞춘다. 한쪽에만 있으면 `LeftOnly`/`RightOnly`. 유형이 다르면 `TypeMismatch`. reparse point(링크)는 대상이 아니라 **링크 자체**를 비교한다(`LinkTarget` 문자열 비교). 순환 방지를 위해 링크 폴더 안으로는 들어가지 않는다.
4. 파일 쌍: 크기가 다르면 `Different`. 같으면 `|left.LastWriteTimeUtc - right.LastWriteTimeUtc| > TimestampTolerance`면 `Different`, 아니면 `Same`. `UseContentHash`면 크기가 같은 쌍에 한해 SHA-256을 스트리밍으로 계산해 판정한다(시각 무시).
5. 폴더 쌍: 둘 다 폴더면 스택에 넣고(`MaxDepth` 검사) 자식 판정이 끝난 뒤 집계 상태를 매긴다. 후위 순회가 필요하므로 스택 항목에 "자식 진입 전/후" 표시를 둔다.
6. 판정된 `CompareEntry`는 즉시 `progress.Report`로 넘긴다. UI는 100ms 배치로 모아 반영한다.
7. `token`이 취소되면 `IsComplete = false`로 현재까지 결과를 돌려준다.

복잡도는 O(양쪽 항목 수). 해시 옵션은 I/O 지배적이므로 파일당 취소 지점을 둔다.

## FolderBrowser 연동

- `public void SetCompareOverlay(CompareResult result, string side)` — `side`는 `Left`/`Right`. 현재 폴더의 상대 경로를 계산해 `_items`의 각 `FileSystemItem`에 상태를 채우고, 반대편에만 있는 항목은 자리표시자 `FileSystemItem`을 `_items`에 추가한다. `RefreshItems()`가 목록을 다시 만들 때도 오버레이를 다시 입혀야 하므로 `_compareResult`/`_compareSide`를 필드로 들고 `RefreshItems` 끝에서 `ApplyCompareOverlay()`를 부른다.
- `public void ClearCompareOverlay()` — 상태를 지우고 자리표시자를 빼고 컬럼을 제거한다.
- `public bool ShowDifferencesOnly { get; set; }` — `ApplyFilter()`의 조건에 `CompareStatus != Same`를 추가한다(검색 결과 필터·이름 필터와 AND).
- 자리표시자 행 보호: `FileList_MouseDoubleClick`, `SelectedItems`(호출자가 삭제·이동에 쓰므로 자리표시자를 걸러서 반환), 드래그 시작, 컨텍스트 메뉴에서 `IsPlaceholder`를 제외한다. 이 지점들은 이번 항목에서 가장 회귀 위험이 큰 곳이라 테스트 체크리스트에 넣는다.
- 컬럼 삽입/제거는 `SearchPanel.UpdateContentColumnVisibility`와 같은 방식. 정렬은 `ColumnHeader_Click`의 `Tag="CompareStatus"` 분기 추가.
- 상태바(`UpdateStatusText`)에 비교 집계를 덧붙인다.

## MainWindow 연동

- 필드: `_compareResult`, `_compareLeft`(FolderBrowser), `_compareRight`, `_compareCts`, `_syncingCompareNavigation`, `_compareOptions`.
- `ToggleDirectoryCompare_Click`: 켜기 → 두 패널 확정, 옵션 로드, `StartCompareAsync()`. 끄기 → 취소, 두 패널 `ClearCompareOverlay()`, 이벤트 구독 해제.
- `StartCompareAsync()`: 배너 "비교 중…" 표시, `DirectoryCompareService.CompareAsync` 호출, `Progress<CompareEntry>`를 100ms 타이머로 모아 두 패널에 `SetCompareOverlay` 갱신. 완료 시 요약 갱신.
- 두 패널의 `PathChanged`를 구독해 동기 이동 처리(위 "동기 이동"). 어느 한쪽 패널이 닫히면(`LayoutDocument.Closed`) 비교 모드를 해제한다.
- `FileSystemWatcher`로 목록이 갱신되면 오버레이는 옛 결과를 그대로 입힌다. 배너에 "다시 비교" 버튼을 두고, 파일 작업(`RefreshBothPanes`) 뒤에는 자동으로 다시 비교할지 옵션(기본 켬, 결과가 1만 항목 이하일 때만 자동).
- `ExecuteTransfer`·`DeleteSelected`는 `SelectedItems`가 자리표시자를 걸러 주므로 변경 없음. 단, 이동 뒤 `RefreshBothPanes()`가 부르는 `RefreshItems()`가 오버레이를 다시 입히는지 확인.

## 설정·저장

- 비교 모드 자체는 저장하지 않는다.
- `CompareOptions`는 `%LOCALAPPDATA%\Folderss\compare-settings.xml`에 저장한다(신규 `CompareSettingsService`, `SettingsFile.Write` 사용, 예외 전파). 설정 창에는 넣지 않고 비교 옵션 다이얼로그에서만 바꾼다 — 설정 창의 `TrySave` 목록에 넣을 필요가 없다.

## 성능·안정성

- 스캔은 `Task.Run`. UI 갱신은 100ms 배치. 10만 항목에서 초기 판정 표시까지 1초 이내, 전체 완료는 디스크 속도에 따름.
- 접근 거부·순환 링크·긴 경로(260자 초과)는 `Unknown`으로 집계하고 계속한다. `Unknown` 개수를 배너에 보여 "비교 불가"가 숨겨지지 않게 한다.
- 결과 메모리: `CompareEntry` 하나당 수백 바이트. 100만 항목이면 수백 MB가 될 수 있으므로 `MaxDepth` 안내와 함께 50만 항목에서 경고 후 중단할지 묻는다.
- 비교 중 패널이 닫히거나 앱이 종료되면 `CancellationTokenSource`를 취소하고 대기하지 않는다.

## 테스트 계획

- `tests/Folderss.SearchTests`에 `DirectoryCompareServiceTests.cs` 추가(순수 System.IO라 링크 컴파일 가능). `CompareOptions`·`CompareEntry`·`CompareStatus`는 `Models/`에 두고 함께 링크한다.
  - 같음/다름(크기)/다름(시각, 허용 오차 경계 1.9초·2.1초)/이쪽만/저쪽만/유형 다름 각 1건
  - 하위 폴더 집계(자식 하나만 달라도 폴더는 다름)
  - ignore 규칙 적용 시 `bin/` 제외, 한쪽만 규칙 파일이 있을 때 `Reason` 기록
  - 해시 옵션: 크기 같고 내용 다른 파일 → 다름, 시각이 달라도 내용 같으면 같음
  - 접근 거부 폴더(POSIX chmod 000, `SkippableFact`) → `Unknown` 집계, 나머지 계속
  - 순환 심볼릭 링크에 들어가지 않음
  - 취소 토큰 → `IsComplete == false`, 예외 없음
- 수동 체크리스트: 자리표시자 행에서 삭제·이동·드래그·컨텍스트 메뉴가 동작하지 않는지, 동기 이동 핑퐁이 없는지, 비교 루트 밖으로 나가면 해제되는지, 7개 테마에서 4색이 보이는지, F11 최대화·도킹 배치 초기화 후에도 오버레이가 남거나 깨지지 않는지.

## 구현 단계

1. **모델·서비스·테스트** — `CompareStatus`/`CompareOptions`/`CompareEntry`/`CompareResult`, `DirectoryCompareService`, 단위 테스트. UI 없이 `dotnet test`로 검증. (규모: 중의 40%)
2. **오버레이 표시** — `FileSystemItem` 확장, 테마 4색 × 7파일, `FolderBrowser.SetCompareOverlay/ClearCompareOverlay`, 비교 컬럼 삽입, 자리표시자 보호. 메뉴 토글과 `MainWindow.StartCompareAsync`로 한 번 비교해 표시까지. (30%)
3. **동기 이동·차이만 보기·배너** — `PathChanged` 연동, 루트 밖 이탈 해제, 배너 버튼(다시 비교·해제·차이만). (15%)
4. **옵션 다이얼로그·설정 저장** — `CompareOptionsWindow`, `CompareSettingsService`. (10%)
5. **문서** — README(기능·테마 키), `docs/architecture.md`(서비스·확장 포인트), CLAUDE.md 테마 체크리스트에 4색 키 추가, 이 항목 파일 갱신. (5%)

## 변경 파일 (예정)

- 신규: `Models/CompareStatus.cs`(enum + Options + Entry + Result), `Services/DirectoryCompareService.cs`, `Services/CompareSettingsService.cs`, `CompareOptionsWindow.xaml/.cs`, `tests/Folderss.SearchTests/DirectoryCompareServiceTests.cs`
- 수정: `Models/FileSystemItem.cs`, `Controls/FolderBrowser.xaml/.cs`, `MainWindow.xaml/.cs`, `Themes/*.xaml`(7개), `Folderss.csproj`(창 Page 항목), `tests/Folderss.SearchTests/Folderss.SearchTests.csproj`, `README.md`, `docs/architecture.md`, `CLAUDE.md`

## 리스크

- **자리표시자 행**이 기존 파일 작업 경로에 새어 들어가면 존재하지 않는 경로로 삭제·이동을 시도한다. `SelectedItems`에서 거르는 것으로 막되, `FileList.SelectedItems`를 직접 읽는 곳(`FileList_MouseRightButtonUp`, 드래그 시작)을 전수 점검해야 한다.
- **동기 이동**은 트리뷰 고정(`PinnedPath`)과 상호작용한다. 고정된 패널은 루트 밖으로 못 나가므로, 반대쪽이 밖으로 나가면 비교 모드만 해제하고 고정은 건드리지 않는다.
- **테마 키 누락**은 런타임 크래시가 아니라 색이 안 보이는 증상으로만 드러난다. 체크리스트와 시작 시 리소스 키 존재 검사(Debug 빌드 assert)로 보완.
- 대용량 트리에서 `Dictionary<string, CompareEntry>` 메모리. 50만 항목 경고로 완화.

## 미결 사항

- 비교 컬럼 대신 이름 앞 기호(≠ ◀ ▶ ⚠)만으로 충분한가? 컬럼은 정렬이 가능해 채택했지만 폭을 차지한다. 첫 구현은 컬럼으로 하고 피드백을 받는다.
- "저쪽만" 자리표시자를 이쪽 목록에 넣는 방식이 혼란스러울 수 있다(없는 파일이 보임). 대안은 반대편 패널에서만 초록으로 표시하는 것. 자리표시자는 두 목록의 행을 정렬시켜 눈으로 대조하기 쉬워 채택했고, 회색 이탤릭으로 구분한다.
- `CompareOptions` 저장을 설정 창에도 노출할지. 비교 옵션 다이얼로그 하나로 시작한다.

## 변경 이력

- 2026-09-22: Cursor 제안을 바탕으로 상세설계서 작성 (Todo)

# 폴더 비교 창 — 좌우 폴더 패널 방식 재설계

- 상태: In Progress

## 요구사항

### 배경

- 2026-10-14에 전용 비교 창 1차 구현(커밋 `69298e2`)을 했다. 결과를 한 목록에 표로 늘어놓는 방식이다.
- 2026-10-15 사용자 확인 결과 마음에 들지 않음: "Beyond Compare의 폴더 비교 방식 조사가 덜 됐다", "폴더 패널을 좌우 두 개 배치해서 비교하는 방식이 좋다".
- Beyond Compare(이하 BC) 공식 도움말을 조사해 좌우 정렬 트리(전용 컨트롤) 방식으로 재설계했다(커밋 `99074a9`).
- 2026-10-15 사용자 결정으로 다시 방향을 정함:
  - 비교 창 안에 **기존 폴더 패널(`FolderBrowser`) 두 개를 좌우로 배치**하고 그 위에 비교 상태를 겹친다(전용 트리 컨트롤을 새로 만들지 않음).
  - 목록은 폴더 패널처럼 **현재 폴더 하나만** 보이고, 한쪽에서 하위 폴더로 들어가면 반대쪽도 같은 상대 경로로 **따라 이동**한다(BC식 트리 펼침 아님).

### 기능 요구사항

1. **비교 창 = 좌우 폴더 패널**: 왼쪽·오른쪽에 `FolderBrowser`를 하나씩 두고, 경로 입력란·뒤로/앞으로/상위·이름 필터·폴더 트리 같은 패널 기능을 그대로 쓴다.
2. **비교 상태 겹치기**: 각 패널 목록 행에 상태(같음·다름·이쪽만·저쪽만·유형 다름·비교 불가)를 색과 `비교` 열로 보인다. 폴더 행은 하위 내용의 집계 상태.
3. **줄 맞춤**: 반대쪽에만 있는 항목은 이쪽 목록에 **자리표시자 행**(회색, 이름만)으로 넣어 두 목록의 같은 줄이 같은 이름이 되게 한다. 두 목록은 같이 스크롤되고 정렬도 함께 바뀐다.
4. **동기 이동**: 비교 기준 폴더 아래에서 한쪽이 하위·상위 폴더로 이동하면 반대쪽도 같은 상대 경로로 이동한다. 기준 밖으로 나가면 동기 이동을 멈추고, `비교`를 다시 누르면 두 패널의 현재 경로가 새 기준이 된다.
5. **차이만 보기**: 같은 항목을 양쪽에서 함께 숨긴다(줄 맞춤 유지).
6. **파일 동작**: 패널의 기존 동작(열기, 우클릭 메뉴, 끌어다 놓기, 복사/잘라내기/붙여넣기)이 비교 창 안에서도 동작한다. 그리고 F5 반대쪽으로 복사, F6 반대쪽으로 이동, Delete 삭제를 비교 창에서 처리한다. 동작 후에는 다시 비교한다.
7. 양쪽에 있는 파일을 더블클릭하면 두 파일 비교 창을 연다(1차와 같음).
8. 비교는 백그라운드에서 돌고 중지할 수 있다. 옵션은 1차의 `비교 ▾`(권장값 기본)를 그대로 쓴다.
9. **분할선**: 두 패널 사이에 세로 분할선(`GridSplitter`)을 두어 좌우 너비를 끌어서 조절한다. 처음은 반반이고, 분할선을 두 번 누르면 반반으로 돌아간다.
10. **표시 방식 옵션**: 아래 "표시 옵션"의 선택 사항은 고정하지 않고 `비교 ▾` 옵션 대화상자에서 고른다. 처음 값은 가장 안전한 쪽이고 `compare-settings.xml`에 저장한다(프로젝트 규칙: 선택지가 여러 개면 대화상자, 기본값은 가장 안전한 선택).

### 범위 밖 (이번 재설계에서 하지 않음)

- BC식 트리 펼침(여러 단계를 한 화면에 줄 맞춤), 동기화 프리셋(갱신·미러), 세분 필터(왼쪽 최신 등). 이 창이 자리 잡은 뒤 필요하면 별도 항목으로 판단.
- 메인 창의 좌우 폴더 패널에 직접 겹치는 방식 — `folder-panel-compare-mode.md`. 이 창에서 만드는 오버레이를 그대로 쓸 수 있으므로, 이 창 완성 후 필요 여부를 다시 판단.

## 원인 분석 또는 설계

### 화면 배치

```
┌ 폴더 비교 ──────────────────────────────────────────────────────────────────────┐
│ [⟳ 비교][▾] [■ 중지]  [☑ 차이만 보기]  [▲ 이전 차이][▼ 다음 차이]  [⇄ 좌우 바꾸기]         │
│ ┌ 왼쪽 폴더 패널 (FolderBrowser) ───────┐ ┃ ┌ 오른쪽 폴더 패널 (FolderBrowser) ──────┐ │
│ │ ← → ↑ [D:\work\proj\src            ] │ ┃ │ ← → ↑ [D:\backup\proj\src          ] │ │
│ │ 이름          수정한 날짜   크기 비교  │ ┃ │ 이름          수정한 날짜   크기 비교  │ │
│ │ 📂 Services   10-14 22:40       ≠    │ ┃ │ 📂 Services   10-01 09:10       ≠    │ │
│ │ 📄 App.cs     10-14 22:40  1KB  ≠    │ ┃ │ 📄 App.cs     10-01 09:10  1KB  ≠    │ │
│ │ 📄 New.cs     10-14 21:00  3KB  ◀    │ ┃ │    (New.cs)                     ◀    │ │ ← 자리표시자
│ │    (Old.cs)                     ▶    │ ┃ │ 📄 Old.cs     09-01 10:00  5KB  ▶    │ │
│ └──────────────────────────────────────┘ ┃ └──────────────────────────────────────┘ │
│ 기준: D:\work\proj\src ↔ D:\backup\proj\src · 현재: \ · 같음 28 · 다름 5 · 왼쪽만 4 · 오른쪽만 3 │
└─────────────────────────────────────────────────────────────────────────────────┘
```

### 구성 요소

| 구성 | 처리 |
|---|---|
| `DirectoryCompareService`, `OptionArrowButton`, 진입 메뉴 | 1차 그대로 재사용 |
| `DirectoryCompareOptions`, `CompareSettingsService`, `DirectoryCompareOptionsDialog` | **확장**: 표시 옵션 5개 추가(아래 "표시 옵션") |
| `CompareOverlay`(신규, 순수 로직) | `CompareResult` + 비교 기준 + 현재 상대 경로 → 이쪽 목록에 입힐 상태와 자리표시자 목록. UI 없이 단위 테스트 |
| `FileSystemItem` | `CompareStatus`, `CompareReason`, `IsPlaceholder` 추가 |
| `FolderBrowser` | 오버레이 적용·해제, `비교` 열 삽입, 차이만 보기 필터, 자리표시자 보호, 호스트 인터페이스 사용 |
| `IFolderBrowserHost`(신규) | 패널이 메인 창에 기대던 기능(잠금 검사, 붙여넣기, 잘라내기 표시)을 인터페이스로 분리. `MainWindow`가 구현하고, 비교 창은 소유자 `MainWindow`에 위임 |
| `DirectoryCompareWindow` | **재작성**: 좌우 `FolderBrowser`, 툴바, 동기 이동·스크롤·정렬, F5/F6/Delete |
| `DirectoryCompareView`, 폴더 비교에서의 `ComparePairHeader` | 삭제 / 폴더 비교에서 제거(두 파일 비교 창은 계속 사용) |

### 꼭 손대야 하는 기존 결합 (코드 확인 결과)

- `FolderBrowser`는 `Window.GetWindow(this) as MainWindow`로 메인 창을 찾는다(끌어다 놓기 `FileList_Drop` 경로, 붙여넣기 `Paste_Executed`, 잘라내기 표시, 패널 잠금 검사 등 7곳). 비교 창 안에서는 null이라 **놓기·붙여넣기가 조용히 아무것도 안 한다**. → `IFolderBrowserHost`로 바꾼다. 이 단계에서 메인 창 동작이 바뀌면 안 된다.
- F5/F6/Delete는 `MainWindow.Window_KeyDown` → `ExecuteTransfer(move)`/`DeleteSelected`이며 `ActivePane`/`TargetPane`에 묶여 있다. → `ExecuteTransfer(source, target, move)`처럼 패널을 인수로 받게 바꾸고 기존 호출은 `ActivePane, TargetPane`을 넘긴다. 비교 창은 소유자 메인 창의 같은 함수를 호출한다(확인 문구·잠금 검사 중복 방지).
- `NavigateTo`는 없는 폴더면 메시지 상자를 띄운다. 동기 이동은 반대쪽에 폴더가 있는지 먼저 확인하고 부른다.
- 우클릭 메뉴는 Windows 셸 메뉴(`ShellContextMenuService`)라 자리표시자의 없는 경로가 넘어가면 안 된다.

### 자리표시자 보호 (가장 큰 회귀 위험)

자리표시자는 실제로 없는 경로다. 다음에서 모두 걸러야 한다.

- `SelectedItems`·`SelectedItem`(메인 창의 복사·이동·삭제가 이것을 씀), `GetSelectedFilePair`/`GetSelectedFolderPair`
- 더블클릭·Enter 열기, 끌어다 놓기 시작, 셸 우클릭 메뉴, 복사/잘라내기(`Copy_Executed`, `Cut_Executed`), 이름 바꾸기, 미리보기
- `FileList.SelectedItems`를 직접 읽는 곳 전수 점검
- 자리표시자를 더블클릭하면 반대쪽 패널에서 그 항목을 선택한다(아무 동작 안 하는 것보다 낫다).

### 줄 맞춤 규칙

- 짝짓기 키는 이름(대소문자 무시). 두 목록은 같은 정렬(폴더 먼저 → 같은 열 기준)이어야 줄이 맞으므로, 비교 중 한쪽 열 머리글을 누르면 반대쪽도 같은 정렬로 바꾼다.
- 이름 필터(패널 검색란)를 한쪽에만 넣으면 줄이 어긋난다. → 비교 중에는 양쪽에 같은 필터를 적용한다.
- 차이만 보기는 **줄(짝)의 상태**로 거른다(한쪽 기준으로 거르면 어긋남).
- 스크롤: 두 `ListView`의 `ScrollViewer` 세로 위치를 묶는다. 선택: 한쪽 행을 고르면 반대쪽 같은 줄도 선택해 보이게 한다(동작 대상은 고른 쪽만).

### 동기 이동 규칙

- 처음 비교할 때 두 패널 경로가 비교 기준(루트) 쌍이 된다.
- 한쪽 `PathChanged` → 루트 기준 상대 경로 계산 → 반대쪽 같은 상대 경로가 있으면 `NavigateTo`. 재진입 방지 플래그로 핑퐁을 막는다.
- 반대쪽에 그 폴더가 없을 때의 처리 — 미결 사항 2.
- 어느 쪽이든 루트 밖으로 나가면 동기 이동과 오버레이를 멈추고 상태 줄에 "기준 밖 — 비교를 누르면 현재 경로로 다시 비교"를 보인다.

### 분할선

- 창 본문을 3열 `Grid`(왼쪽 `*` | 분할선 6px | 오른쪽 `*`)로 두고 가운데에 `GridSplitter`(`ResizeBehavior=PreviousAndNext`)를 놓는다. 각 패널 최소 너비 200px.
- 분할선을 두 번 누르면 두 열을 `1*`/`1*`로 되돌린다. 비율은 저장하지 않는다(창을 열 때마다 반반) — 저장이 필요하면 옵션에 추가.
- 패널 너비가 달라져도 줄 맞춤은 세로 위치만 묶으므로 영향이 없다. 가로 스크롤은 묶지 않는다.

### 표시 옵션 (`비교 ▾` 대화상자)

1차 옵션(판정 기준·허용 오차·ignore·깊이·숨김) 아래에 "표시" 묶음을 추가한다. `DirectoryCompareOptions`에 속성을 더하고 `CompareSettingsService`가 함께 저장한다. 바꾸면 다시 스캔하지 않고 오버레이만 다시 입힌다(판정 기준이 아니므로).

| 옵션 | 선택지 | 처음 값 | 이유 |
|---|---|---|---|
| 상태 색 | 상태별 색(다름·이쪽만·저쪽만·유형 다름) / 방향 색(최신 빨강·오래됨 회색·한쪽만 보라, BC식) | 상태별 색 | 방향 색은 수정 시각 기준이라 복사·git checkout으로 시각만 바뀐 파일을 "최신"으로 잘못 보인다. 방향 색을 고르면 대화상자에 이 경고를 보인다 |
| 반대쪽에 폴더가 없을 때 | 반대쪽은 그대로 두고 알림만 / 반대쪽을 "없는 폴더"(자리표시자만)로 보임 | 알림만 | "없는 폴더" 상태는 줄은 맞지만 그 패널의 붙여넣기·새 폴더·놓기를 막아야 해서 잘못된 경로로 쓰기를 시도할 여지가 있다 |
| 폴더 트리 | 보임 / 숨김 | 숨김 | 비교 창은 좌우 두 패널이라 너비가 좁다 |
| 미리보기 | 보임 / 숨김 | 숨김 | 같은 이유 |
| 같은 항목 숨기기(차이만 보기) | 켬 / 끔 | 끔 | 처음 열 때 전체를 보여 줘야 "같음"도 확인된다. 툴바 체크로도 바꿀 수 있고 마지막 값을 기억 |

- 옵션마다 코드 경로가 둘씩 생긴다. 특히 "반대쪽에 폴더가 없을 때"의 "없는 폴더" 쪽은 자리표시자 보호 지점에 붙여넣기·새 폴더·놓기 대상 차단이 더해진다. 테스트(1단계)와 화면 확인(4단계) 목록에 두 선택지를 모두 넣는다.

### 비교 실행

- 1차 서비스는 기준 아래 전체를 한 번에 판정하고 끝날 때 결과를 준다. 오버레이는 결과에서 현재 폴더의 직속 항목만 골라 입힌다. 하위 폴더로 이동해도 다시 스캔하지 않는다.
- 파일 변경 감시(`FileSystemWatcher`)로 목록이 바뀌면 오버레이는 옛 결과를 입힌다. 이때 상태 줄에 "변경됨 — 다시 비교" 표시. F5/F6/Delete 뒤에는 자동으로 다시 비교한다.
- 큰 트리에서는 결과가 늦게 나온다(점진 반영은 1차 서비스에 없음). 우선 진행 표시(읽은 폴더 수)로 두고, 느리면 별도로 판단.

## 구현 단계

단계마다 빌드·테스트 후 커밋한다. 2·4·5단계는 Windows 화면 확인이 필요하다.

1. **오버레이 순수 로직 + 테스트** — `CompareOverlay`. 완료 조건: 아래 테스트 통과.
   - 양쪽 행 목록이 같은 키 순서로 나옴(이쪽만/저쪽만 자리표시자 포함)
   - 이름 대소문자만 다른 항목은 한 줄
   - 하위 폴더 상대 경로에서 직속 항목만 나옴, 폴더 집계 상태
   - 차이만 보기 적용 후에도 양쪽 줄 수·순서 같음
2. **호스트 분리** — `IFolderBrowserHost`, `ExecuteTransfer(source, target, move)`·`DeleteSelected(pane, …)` 인수화. 완료 조건: 빌드·기존 테스트 통과, 메인 창 끌어다 놓기·붙여넣기·F5/F6/Delete·잠금 동작 변화 없음(Windows 확인).
3. **FolderBrowser 오버레이** — `FileSystemItem` 필드, 오버레이 적용·해제(`RefreshItems` 뒤 다시 입힘), `비교` 열, 차이만 보기, 자리표시자 보호, 테마 색 키(`Themes/*.xaml` 8개).
4. **비교 창 재작성** — 좌우 패널과 가운데 분할선, 툴바, 표시 옵션 5개(대화상자·저장·적용), 동기 이동·스크롤·정렬·필터 맞춤, F5/F6/Delete → 다시 비교, 이전/다음 차이, 좌우 바꾸기.
5. **정리·문서** — `DirectoryCompareView` 삭제, README·architecture, CLAUDE.md 테마 체크리스트에 색 키 추가.

## 미결 사항 (착수 전 사용자 확인)

- 상태 색, 반대쪽에 폴더가 없을 때, 폴더 트리·미리보기는 사용자 결정으로 옵션이 됐다(위 "표시 옵션").
- **1차 화면 처리**: 실행 옵션이 아니라 배포 결정이다. 다음 태그 전에 2차가 끝나지 않으면 1차(표 목록)를 그대로 낼지 메뉴를 숨길지.

## 조사 자료 (Beyond Compare 공식 도움말)

이번 방향에서는 트리 펼침·동기화 프리셋을 범위 밖으로 뺐다. 색 의미·줄 맞춤·필터 등 참고용으로 남긴다.

- 화면·색 의미: https://www.scootersoftware.com/v5help/dir_understanding_the_display.html
- 표시 필터: https://www.scootersoftware.com/v5help/dir_filtering_the_view.html
- 비교 방법: https://www.scootersoftware.com/v5help/dir_how_to_compare.html
- 차이 맞추기: https://www.scootersoftware.com/v5help/dir_reconciling_differences.html
- 동기화: https://www.scootersoftware.com/v5help/dir_sync_how_to_sync.html
- 트리 펼침 방식 설계 원문: `git show 99074a9:docs/items/directory-compare.md`
- 패널 겹치기 방식 처음 설계(자리표시자·동기 이동·리스크): `git show 195295f:docs/items/directory-compare.md`

## 구현 내용

- 1차(2026-10-14, 커밋 `69298e2`): 표 목록 방식 전용 창. 서비스·옵션·옵션 대화상자·진입 메뉴·`ComparePairHeader`·공용 `OptionArrowButton`.
- 2차(좌우 폴더 패널 방식): 미착수.

## 변경 파일

- 1차: `Folderss/Models/DirectoryCompareModels.cs`, `Folderss/Services/DirectoryCompareService.cs`, `Folderss/Services/CompareSettingsService.cs`, `Folderss/Controls/ComparePairHeader.cs`, `Folderss/Controls/DirectoryCompareView.cs`, `Folderss/DirectoryCompareWindow.cs`, `Folderss/FileCompareWindow.cs`, `Folderss/MainWindow.xaml(.cs)`, `Folderss/Controls/FolderBrowser.xaml.cs`, `Folderss/GitWindow.xaml`, `Folderss/Themes/Controls.xaml`, `tests/Folderss.SearchTests/DirectoryCompareServiceTests.cs`
- 2차 예정: 신규 `Services/CompareOverlay.cs`, `Controls/IFolderBrowserHost.cs`, 테스트. 수정 `Models/FileSystemItem.cs`, `Controls/FolderBrowser.xaml(.cs)`, `MainWindow.xaml.cs`, `DirectoryCompareWindow.cs`, `Themes/*.xaml` 8개. 삭제 `Controls/DirectoryCompareView.cs`

## 검증

- 1차: 빌드 성공, 단위 테스트 12건 통과(전체 260 통과·4 건너뜀). 사용자 화면 확인 결과 방식 불만족 → 재설계.
- 2차: 1단계 단위 테스트, 2단계 메인 창 회귀 확인, 3~4단계 Windows 화면 확인(줄 맞춤·자리표시자 보호·동기 이동·차이만 보기·F5/F6/Delete·8개 테마 색).

## 변경 이력

- 2026-09-22: Cursor 제안을 바탕으로 상세설계서 작성 (Todo) — 패널 오버레이 방식
- 2026-10-14: 사용자 결정으로 전용 비교 창(팝업) 방식으로 변경, 선택적 동작은 ▾ 옵션(권장값 기본)으로. 재사용 부분은 컨트롤로 분리. 구현·빌드·단위 테스트 완료, 사용자 확인 대기.
- 2026-10-15: 사용자 확인 중 좌우 폴더 패널 비교 방식 요청 — 다음 아이템 `folder-panel-compare-mode.md`로 분리 기록.
- 2026-10-15: 1차 결과 불만족("Beyond Compare 폴더 비교 조사 부족"). BC 공식 도움말 조사 후 좌우 정렬 트리·쪽별 방향 색·필터·동기화 프리셋으로 재설계(In Progress).
- 2026-10-15: 사용자 결정으로 방향 변경 — 비교 창 안에 기존 폴더 패널 두 개를 배치하고 비교 상태를 겹침, 현재 폴더만 보이고 동기 이동. 트리 펼침·동기화 프리셋은 범위 밖. 코드 확인으로 폴더 패널의 메인 창 결합(놓기·붙여넣기·F5/F6/Delete)을 분리 단계로 추가. 다음 작업은 미결 사항 확인 후 1단계.
- 2026-10-15: 사용자 요청 — 두 패널 사이에 분할선을 두어 너비 조절, 미결이던 선택 사항(상태 색·반대쪽에 폴더 없을 때·트리·미리보기)과 차이만 보기를 `비교 ▾` 표시 옵션으로(처음 값은 가장 안전한 쪽).

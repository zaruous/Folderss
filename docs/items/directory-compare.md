# 폴더 비교 창 (디렉터리 비교)

- 상태: Ready for Verification

## 요구사항

### 기능

1. 두 폴더를 **별도 비교 창(비모달 팝업)**에서 비교한다. 폴더 패널 화면은 건드리지 않는다.
2. 진입: ⋯ 메뉴 `폴더 비교…` — 활성 패널에서 폴더 두 개를 골랐으면 그 둘, 아니면 활성 패널 현재 폴더(왼쪽)와 반대편 패널 현재 폴더(오른쪽). 폴더 패널이 하나뿐이면 오른쪽은 비워 두고 창에서 고른다. 폴더 두 개를 골라 우클릭 > `선택한 두 폴더 비교`도 같다.
3. 상태 여섯 가지: **같음**, **다름**(크기 또는 수정 시각, 옵션으로 내용 해시), **왼쪽만**, **오른쪽만**, **유형 다름**(폴더↔파일, 한쪽만 링크), **비교 불가**(접근 거부 등).
4. 하위 폴더는 재귀 비교하고, 폴더 쌍은 자식 중 차이가 하나라도 있으면 **다름**.
5. 창 기능: 왼쪽/오른쪽 폴더 표시와 `…` 경로 고르기, `⇄ 좌우 바꾸기`, `비교`(다시 비교), `▾` 옵션, `중지`, `차이만 보기`, 요약(개수), 현재 옵션 한 줄 표시.
6. 결과 항목 동작: 양쪽에 있는 파일 더블클릭·Enter → 두 파일 비교 창. 우클릭 → 두 파일 비교, 왼쪽 → 오른쪽 복사, 오른쪽 → 왼쪽 복사, 탐색기로 열기(왼쪽/오른쪽).
7. 비교는 백그라운드에서 돌고 중지할 수 있다(중지하면 지금까지의 결과를 보인다).

### 선택적인 동작은 옵션으로 (권장값이 기본)

여러 방식 중 하나를 골라야 하는 것은 하나로 고정하지 않고 옵션으로 둔다. `비교` 버튼은 저장된 옵션으로 바로 실행하고, 옵션은 옆 `▾`를 누를 때만 연다(Git 창과 같은 규칙). 처음 값이 권장값이며 대화상자에 `권장값으로` 버튼이 있다.

| 옵션 | 권장(기본) | 다른 선택 |
|---|---|---|
| 판정 기준 | 크기와 수정 시각 | 내용 해시(SHA-256, 느림, 시각 무시) |
| 수정 시각 허용 오차 | 2초 (FAT·네트워크 드라이브 해상도) | 0 ~ 86400초 |
| ignore 규칙(.gitignore/.folderssignore) | 적용 | 무시 |
| 하위 폴더 | 전체 재귀 | 안 봄 / 최대 깊이 N |
| 숨김·시스템 파일 | 제외 | 포함 |
| 차이만 보기 | 켬 | 끔 (창의 체크박스, 저장 안 함) |

옵션은 `%LOCALAPPDATA%\Folderss\compare-settings.xml`에 저장한다.

### 범위 밖

- 동기화 실행(양쪽에 있는 폴더 병합, 삭제 포함 미러링, dry-run) — `docs/아이디어.md`의 "단방향 폴더 동기화".
- 세 폴더 이상 비교, 원격(FTP/SFTP) 비교.

## 원인 분석 또는 설계

### 방식 결정 (2026-10-14)

처음 설계(2026-09-22)는 좌우 폴더 패널 위에 비교 결과를 겹쳐 보이는 방식이었다. 사용자와 논의해 **전용 비교 창**으로 바꿨다.

- 폴더 패널(`FolderBrowser`)을 고치지 않아 회귀 위험이 없다(자리표시자 행이 복사·삭제 경로에 새는 위험, 동기 이동 핑퐁, 트리뷰 고정과의 상호작용이 모두 사라짐).
- 패널 배치와 무관하게 아무 두 폴더나 비교할 수 있다.
- 대신 창 안에 복사 동작을 새로 둔다. 복사는 파일 덮어쓰기와 한쪽에만 있는 폴더 통째 복사로 한정한다.

### 재사용 구조

재사용 가능한 부분은 컨트롤로 감쌌다.

- `Controls/ComparePairHeader` — "왼쪽 | ⇄ 좌우 바꾸기 | 오른쪽" 머리글(이름·위치, 선택적 `…`). 기존 `FileCompareWindow`의 머리글 코드를 옮겨 두 창이 같이 쓴다.
- `Controls/DirectoryCompareView` — 결과 목록(ListView). 표시와 선택만 맡고 동작은 이벤트(`CompareRequested`, `CopyRequested`, `RevealRequested`)로 올린다. 나중에 도킹 패널 등 다른 곳에 담을 수 있다.
- `OptionArrowButton` 스타일 — `GitWindow.xaml` 지역 리소스에서 `Themes/Controls.xaml` 공용으로 옮겼다.
- `DirectoryCompareService` — UI 없는 순수 System.IO. 단위 테스트로 검증한다.

### 판정 (`DirectoryCompareService.Compare`)

1. 폴더마다 양쪽 직속 항목을 읽어 이름(대소문자 무시)으로 짝짓는다. 화면 순서는 깊이 우선, 각 폴더 안에서 폴더 먼저·이름순, 폴더 항목이 자식보다 앞.
2. ignore 규칙을 적용하면 양쪽 모두 걸리는(또는 한쪽만 있고 그쪽에서 걸리는) 항목은 뺀다. 한쪽에서만 걸리면 비교에 남기고 사유에 적는다 — 규칙 파일 차이 자체가 찾고 싶은 차이일 수 있다.
3. 한쪽에만 있으면 왼쪽만/오른쪽만이고 안으로 들어가지 않는다(복사하면 통째로 간다).
4. 폴더↔파일, 한쪽만 링크면 유형 다름. 양쪽 다 링크면 `LinkTarget`을 비교하고 안으로 들어가지 않는다(순환 방지).
5. 파일: 크기가 다르면 다름. 같으면 해시 옵션일 때 SHA-256, 아니면 수정 시각 차이가 허용 오차를 넘으면 다름.
6. 폴더 쌍: 자식 집계(차이 있으면 다름, 비교 불가만 있으면 비교 불가). 하위를 보지 않는 옵션이면 비교 불가("하위 폴더는 비교하지 않음"). 폴더 쌍은 요약 개수에서 뺀다.
7. 폴더를 읽지 못하면 그 폴더는 비교 불가이고 나머지는 계속한다. 취소되면 예외 없이 `IsComplete = false`인 부분 결과를 돌려준다.

### 복사 (`DirectoryCompareService.CopyEntry`)

- 파일: 반대쪽 같은 상대 경로에 덮어쓴다. 없는 상위 폴더는 만든다.
- 폴더: 반대쪽에 없을 때만 통째로 복사한다. 양쪽에 있는 폴더는 메뉴가 비활성(병합은 동기화라 범위 밖), 유형 다름도 복사하지 않는다.
- 창은 실행 전 항목 목록(최대 10개)과 방향을 보이고, 덮어쓰는 파일이 있으면 "되돌릴 수 없음" 경고와 함께 확인을 받는다(기본 버튼 = 취소). 끝나면 다시 비교한다.

### 화면

```
┌ 폴더 비교 - src ↔ src ───────────────────────────────────────────────┐
│ 왼쪽                          ⇄ 좌우 바꾸기   오른쪽                   │
│ src                     […]                  src                 […] │
│ D:\work\proj                                 D:\backup\proj           │
│ [비교][▾][중지] ☑ 차이만 보기   같음 28 · 다름 5 · 왼쪽만 4 · 오른쪽만 3 │
│ 기준: 크기·수정 시각(±2초) · ignore 규칙 적용 · 하위 폴더 전체 · 숨김 제외 │
│ 이름                 │ 비교      │ 왼쪽 크기 │ 왼쪽 수정 │ 오른쪽 … │ 사유 │
│ 📁 Services          │ ≠ 다름    │           │ …         │          │ 하위에 차이 있음 │
│    📄 GitService.cs  │ ≠ 다름    │ 12 KB     │ …         │ 11 KB    │ 크기 … ≠ … │
│ 📄 Notes.md          │ ◀ 왼쪽만  │ 3 KB      │ …         │          │      │
└──────────────────────────────────────────────────────────────────────┘
```

상태 색은 행 배경이 아니라 `비교` 칸 배지에만 반투명으로 칠한다. 행 배경을 바꾸면 선택·hover 표시를 덮고, 반투명이라 모든 테마에서 읽혀 테마별 색 키가 필요 없다.

## 구현 내용

- 모델: `CompareStatus`, `DirectoryCompareOptions`(이름이 `System.Globalization.CompareOptions`와 겹쳐 접두어를 붙임), `CompareEntry`, `CompareResult`.
- 서비스: `DirectoryCompareService`(`Compare`/`CompareAsync`, `CanCopy`, `CopyEntry`), `CompareSettingsService`(Load/Save, 깨진 값은 그 값만 기본값).
- 컨트롤: `ComparePairHeader`, `DirectoryCompareView`.
- 창: `DirectoryCompareWindow`, `DirectoryCompareOptionsDialog`(`GitDialogBase` 상속).
- 진입: `MainWindow` ⋯ 메뉴 `폴더 비교…`(`CompareFolders_Click`), `FolderBrowser.GetSelectedFolderPair` + 우클릭 `선택한 두 폴더 비교`(`CompareFoldersRequested`).
- 리팩터링: `FileCompareWindow` 머리글을 `ComparePairHeader`로 교체, `OptionArrowButton` 스타일을 공용으로 이동.

## 변경 파일

- 신규: `Folderss/Models/DirectoryCompareModels.cs`, `Folderss/Services/DirectoryCompareService.cs`, `Folderss/Services/CompareSettingsService.cs`, `Folderss/Controls/ComparePairHeader.cs`, `Folderss/Controls/DirectoryCompareView.cs`, `Folderss/DirectoryCompareWindow.cs`, `tests/Folderss.SearchTests/DirectoryCompareServiceTests.cs`
- 수정: `Folderss/FileCompareWindow.cs`, `Folderss/MainWindow.xaml`, `Folderss/MainWindow.xaml.cs`, `Folderss/Controls/FolderBrowser.xaml.cs`, `Folderss/GitWindow.xaml`, `Folderss/Themes/Controls.xaml`, `tests/Folderss.SearchTests/Folderss.SearchTests.csproj`, `README.md`, `docs/architecture.md`

## 검증

- `dotnet build .\Folderss.sln -c Debug` 성공(오류 0).
- `dotnet test tests/Folderss.SearchTests` — `DirectoryCompareServiceTests` 12건: 상태 6종, 시각 허용 오차 경계(1.9초 같음 / 2.1초 다름), 해시 옵션, 폴더 집계와 개수, 대소문자 무시 짝짓기, 한쪽 폴더 미진입, ignore 규칙(양쪽 제외·한쪽만 사유), 하위 미비교 옵션, 취소 시 부분 결과, 복사(덮어쓰기·상위 생성·폴더 제한), 옵션 저장 왕복.
- Windows 앱에서 확인할 것(사용자):
  - [ ] ⋯ 메뉴 `폴더 비교…`로 좌우 패널 폴더 비교 창이 열리고 요약·배지 색이 보임 (여러 테마에서)
  - [ ] 폴더 두 개 우클릭 `선택한 두 폴더 비교`
  - [ ] `⇄ 좌우 바꾸기`, `…`로 폴더 바꾸기, `차이만 보기` 토글, `중지`
  - [ ] `▾` 옵션 변경 → 다시 비교, 앱 재시작 후 옵션 유지, `권장값으로`
  - [ ] 다름 파일 더블클릭 → 두 파일 비교 창 (머리글이 예전과 같게 보이고 `⇄ 좌우 바꾸기` 동작)
  - [ ] 복사(덮어쓰기 경고 포함), 탐색기로 열기
  - [ ] Git 창의 `▾` 버튼 모양이 예전과 같음(스타일 공용 이동)

## 변경 이력

- 2026-09-22: Cursor 제안을 바탕으로 상세설계서 작성 (Todo) — 패널 오버레이 방식
- 2026-10-14: 사용자 결정으로 전용 비교 창(팝업) 방식으로 변경, 선택적 동작은 ▾ 옵션(권장값 기본)으로. 재사용 부분은 컨트롤로 분리. 구현·빌드·단위 테스트 완료, 사용자 확인 대기.
- 2026-10-15: 사용자 확인 중 좌우 폴더 패널 비교 방식 요청 — 다음 아이템 `folder-panel-compare-mode.md`로 분리 기록.

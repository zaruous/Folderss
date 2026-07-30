# 문서 탭 패널 잠금 기능

- 상태: Done

## 요구사항

- `＋ 새 패널`로 추가한 폴더 패널 탭의 우클릭 컨텍스트 메뉴에 `패널 잠금` 기능을 추가한다.
- 잠긴 패널은 닫기가 비활성화되어야 한다.
- 프로그램을 종료하고 다시 실행해도 잠금 상태가 유지되는 구조여야 한다.
- Markdown 패널 등 닫기가 가능한 다른 패널들도 같은 기능과 메뉴를 공유해야 한다.

## 설계

### 잠금 상태 저장

`Services/PanelLockService.cs`(신규)가 잠금 키 목록을 `%LOCALAPPDATA%\Folderss\panel-locks.xml`에 저장한다.
`SessionStateService`와 같은 정적 서비스 + `XmlSerializer` 패턴을 따르고, 잠금을 토글할 때마다 즉시 기록해
비정상 종료에도 상태가 남게 한다. 저장은 임시 파일에 쓴 뒤 교체한다.

### 잠금 키

`dock-layout.xml`에 이미 `ContentId`가 그대로 보존되므로 `ContentId`를 기준으로 키를 만든다
(`MainWindow.GetPanelLockKey`).

| 탭 | 잠금 키 | 이유 |
|---|---|---|
| 폴더 패널 (`folder-panel\|<ID>\|<경로>`) | `folder-panel\|<ID>` | 패널에서 다른 폴더로 이동해도 잠금 유지 |
| 뷰어 탭 (`viewer\|<경로>`) | `viewer\|<정규화된 소문자 경로>` | 같은 파일을 다시 열면 잠긴 상태로 열림 |
| 그 외 (`console`, `disk-usage` 등) | `ContentId` 그대로 | 닫기 가능 탭이 추가되면 별도 등록 없이 잠금 대상 |
| `left-folder`, `right-folder`, `add-folder-panel` | 없음(`null`) | 원래 닫을 수 없는 고정 탭이라 메뉴 미노출 |

Markdown/Monaco/Text 뷰어는 모두 `viewer|` 탭이므로 폴더 패널과 동일한 메뉴·동작을 그대로 공유한다.

### 잠금 반영

- `LayoutDocument.CanClose = false` → `Controls.xaml`의 `LayoutDocumentTabItem` 템플릿에서 닫기 버튼
  `Visibility`가 `IsEnabled`(= `CloseCommand.CanExecute`)에 연결되어 있으므로 X 버튼이 사라지고,
  컨텍스트 메뉴 `닫기`도 비활성화된다.
- 잠금 여부를 한눈에 보이도록 탭 제목에 `🔒 ` 접두사를 붙인다. 제목은 레이아웃 XML에도 저장되므로
  반영할 때마다 접두사를 떼고 다시 붙여 중복을 막는다.
- `ApplyPanelLockStates()`를 레이아웃 복원 직후, 패널 최대화 복원 후, 도킹 배치 초기화 후에 호출한다.
  AvalonDock이 `CanClose`를 레이아웃 XML에 함께 직렬화하므로, 잠금 파일이 단일 기준이 되도록
  잠김/해제를 양방향으로 다시 설정한다. 고정 탭은 항상 `CanClose = false`로 강제한다.
- 패널 최대화 중에는 원래 닫기가 불가능하므로 잠금 해제 시에도 `CanClose`를 되돌리지 않는다.

### 코드에서 직접 닫는 경로

`LayoutContent.Close()`는 `CanClose`를 검사하지 않는다. 기존 `다른 탭 닫기`/`왼쪽·오른쪽 탭 닫기`는
이미 `CanClose`를 확인하고 있었고, 콘솔 패널 내부 닫기 버튼 경로(`CloseConsoleDocument`)에 확인을 추가했다.

## 구현 내용

1. `Services/PanelLockService.cs` (신규)
   - `IsLocked` / `SetLocked` / `Prune` + `PanelLockState`(`List<string> LockedPanels`) XML 직렬화
2. `MainWindow.xaml.cs`
   - `// ── Panel lock ──` 영역 추가: `GetPanelLockKey`, `IsPanelLocked`, `TogglePanelLock`,
     `ApplyPanelLockStates`, `ApplyPanelLockState`, `SetDocumentTitle`, `FormatDocumentTitle`,
     `StripLockedTitlePrefix`, `LockedTitlePrefix`, `FixedDocumentContentIds`
   - `DockManager_PreviewMouseRightButtonDown` — 잠글 수 있는 탭에 체크 가능한 `패널 잠금` 항목 추가
   - `ExtractPanelId`를 `TryGetFolderPanelId`(형식 불일치 시 `null`) + GUID 폴백으로 분리
   - `Window_Loaded` — `EnsureAddPanelTab()` 뒤에 `ApplyPanelLockStates()` 호출
   - `TogglePanelMaximize` 복원 경로 / `ResetDockLayout_Click`에 잠금 반영 (초기화 시 `Prune`으로 기록 정리)
   - `OpenViewerTab`, `ShowConsolePanel`, `ShowDiskUsagePanel` — 탭 생성 직후 `ApplyPanelLockState`
   - `FolderBrowser_PathChanged`, `CreateViewerHost`의 `TitleChanged` — `SetDocumentTitle`로 제목 갱신
   - `CloseConsoleDocument` — `CanClose` 확인 추가
3. 문서: `README.md`(패널 잠금 절, 서비스 목록, 설정 파일 표), `docs/architecture.md`(서비스 흐름)

## 변경 파일

- `Folderss/Services/PanelLockService.cs` (신규)
- `Folderss/MainWindow.xaml.cs`
- `README.md`
- `docs/architecture.md`
- `docs/items/panel-lock.md` (신규)

## 검증

- [x] `dotnet build .\Folderss.sln -c Debug` 성공 (오류 0)
- [x] `dotnet build .\Folderss.sln -c Release` 성공 (오류 0), 릴리스 빌드 실행 시 레이아웃 정상 복원(`dock-layout.restore-error.txt` 미생성)
- [x] 새 패널 탭 우클릭 → `패널 잠금` 표시, 클릭 시 체크 상태로 전환
- [x] 잠금 후 탭 X 버튼 사라짐, 컨텍스트 메뉴 `닫기` 비활성, 제목에 `🔒` 표시
- [x] 잠금 후 `다른 탭 닫기` / `왼쪽 탭 닫기` / `오른쪽 탭 닫기`에서 잠긴 탭이 남아 있음
- [x] 프로그램 종료 후 재실행 시 잠금 유지 (`%LOCALAPPDATA%\Folderss\panel-locks.xml`)
- [x] 잠금 해제 시 X 버튼과 `닫기` 복구, 제목의 `🔒` 제거
- [x] Markdown 뷰어 탭에서 동일 동작, 같은 파일을 닫고 다시 열어도 잠금 유지
- [x] 콘솔 탭 잠금 시 패널 내부 닫기 버튼으로도 닫히지 않음
- [x] 잠긴 폴더 패널에서 다른 폴더로 이동해도 잠금 유지
- [x] `F11` 패널 최대화/복원 후 잠금 상태 유지
- [x] 잠긴 탭을 별도 창으로 분리한 뒤 그 창을 닫아도 잠긴 탭이 닫히지 않음
- [x] `보기 > 도킹 배치 초기화` 후에도 오류 없이 동작

## 변경 이력

- 2026-07-30: 초기 구현 (PanelLockService 신규, 문서 탭 컨텍스트 메뉴 `패널 잠금` 추가)
- 2026-07-30: 릴리스 빌드 실행 확인 및 사용자 확인 완료 → 상태 Done

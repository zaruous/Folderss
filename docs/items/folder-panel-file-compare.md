# 폴더 패널 두 파일 비교

- 상태: Ready for Verification

## 요구사항

- 폴더 패널에서 파일 두 개를 선택한 경우 메뉴를 통해 두 파일을 비교한다.
- Git의 diff 기능을 재활용할지 별도 기능을 둘지 코드를 확인해 판단한다.

### 2차 요청 (2026-09-30)

1. 형상 관리(저장소)가 없는 파일도 비교할 수 있어야 한다.
2. 공통 컴포넌트로 만들 수 있으면 diff 설정을 diff 전용 옵션으로 승격한다(외부 diff 도구 사용 포함).
3. 공통 컴포넌트로 비교 내용을 HTML 보고서로 만드는 도구. 표시 형식은 팝업에서 고른다 — 양옆 비교(전체/변경점만), 한 줄(전체/변경점만) 등.
4. ⋯ 메뉴에 넣는다.
5. 비교하는 파일의 위치와 이름을 보인다.
6. 좌우 전환을 넣는다.

## 원인 분석 또는 설계

### 재활용 판단: Git diff 경로를 재사용한다

| 후보 | 장점 | 단점 |
|---|---|---|
| **Git diff 재사용** (`git diff --no-index` + `GitDiffView`) — 채택 | 이미 검증된 파서(`GitOutputParser.ParseDiff`)·뷰(줄 번호, 추가/삭제 색, 보기 모드, `Ctrl+C`, 2만 줄 제한)·대체 인코딩·UTF-16 BOM 재비교를 그대로 씀. 신규 코드가 작음. `--no-index`는 저장소 밖 파일도 됨 | git 설치 필요(없으면 오류 문구). unified 형식만(좌우 나란히 아님) |
| Monaco diff 에디터 (`docs/아이디어.md`) | 좌우 나란히, git 불필요 | 새 뷰어·HTML·WebView2 연동, 탭 `ContentId`·패널 잠금·레이아웃 복원 규칙까지 손봐야 함. 요청 범위보다 큼 |
| C# diff 알고리즘 직접 구현 | 의존성 없음 | 바퀴 재발명, 대용량 성능·정확성 검증 부담 |

`GitDiffView`는 Git 전용 상태를 갖지 않고 unified diff 텍스트만 받으므로(`BeginLoad`/`Complete`) 다른 창에 그대로 올릴 수 있다.

### 가정 (확인 필요)

- **메뉴 위치**: 파일 목록 우클릭(Windows 셸 메뉴) 맨 위 사용자 항목. 2차에서 ⋯ 메뉴에도 추가.
- **대상**: 같은 패널에서 선택한 정확히 두 개의 **파일**. 폴더가 섞이거나 세 개 이상이면 항목이 안 나온다. 좌우 패널에서 하나씩 고르는 비교, 폴더 비교(`directory-compare.md`)는 범위 밖.
- **왼쪽/오른쪽**: 선택 순서가 아니라 목록에 보이는 순서(위 = 왼쪽/변경 전). 2차에서 `⇄ 좌우 바꾸기` 추가.
- **표시 위치**: 문서 탭이 아니라 별도 비모달 창(Git 창과 같은 방식). 탭으로 두면 레이아웃 저장·복원, 패널 잠금, `+ 새 패널` 정렬 규칙을 함께 다뤄야 한다.
- **옵션**: 공백 무시·대체 인코딩·보기 모드 초기값은 설정을 따른다(2차에서 `설정 > 비교`로 분리).

## 2차 설계

- **형상 없는 파일**: 1차부터 `git diff --no-index`라 저장소 밖 파일도 비교된다(테스트 `RealGit_CompareTwoFiles_OutsideRepository`). git 실행 파일은 여전히 필요.
- **공통 컴포넌트**: diff 뷰(`GitDiffView`)는 이미 Git 창·파일 비교 창 공용. 여기에 보고서(`TextLoader` + `DiffReportExporter`)와
  외부 도구 실행(`DiffToolLauncher`)을 붙여 두 창이 같은 흐름을 쓴다. 클래스 이름의 `Git` 접두사(`GitDiffView`, `GitDiffViewMode` 등)는
  변경 범위를 줄이려고 그대로 뒀다.
- **설정 승격**: `DiffSettings`(diff-settings.xml) 신설, 설정 창 `비교` 탭. `GitSettings`에서 공백 무시·대체 인코딩·diff 보기·외부 도구를 뺐다.
  옛 git-settings.xml 값은 새 파일이 없을 때 읽어 이관한다.
- **보고서 형식**: 배치 2(양옆/한 줄) × 범위 3(변경점만/앞뒤 10줄/전체 파일). 팝업 기본값은 양옆 + 지금 보기 모드. 버튼이 바로 팝업을 여는 것은
  사용자가 요청한 "선택하는 팝업"이며 저장 위치도 매번 골라야 해서다(CLAUDE.md의 ▾ 규칙 예외: 처음부터 고를 것이 본질인 기능).
- **⋯ 메뉴**: `두 파일 비교…` — 활성 패널에서 파일 두 개를 골랐을 때만 열고, 아니면 안내만 한다(파일 선택 대화상자는 두지 않음).

## 구현 내용

- `GitDiffCommands.Files(old, new)` — `diff --no-index --no-color --no-ext-diff -M … -- <old> <new>`. 차이 있으면 종료 코드 1(`IsSuccess(noIndex)`).
- `GitEncodingDiff.ExpandFilesAsync` — `--no-index` 출력의 경로는 git이 앞의 `/`를 떼는 등 원래 경로로 되돌릴 수 없어서(`/tmp/x` → `a/tmp/x`),
  기존 `ExpandAsync`(저장소 상대 경로 해석)를 쓰지 않고 받은 두 경로를 직접 읽어 BOM 재비교. 임시 파일 비교 부분은 `DiffDecodedAsync`로 뽑아 두 경로가 공유.
- `GitDiffView` — 보기 모드 상자를 `NoIndex`면 무조건 숨기던 것을, 빈 쪽이 있는 비교(`OldSide == null`, 추적 안 됨)일 때만 숨기도록 변경.
- `FileCompareWindow`(신규, 코드 구성) — `GitDiffView`를 담은 비모달 창. 보기 모드 변경 시 다시 불러오고, 창을 닫으면 진행 중인 git을 취소.
- `FolderBrowser` — 우클릭 시 파일 두 개면 `선택한 두 파일 비교` 항목 추가, `CompareFilesRequested` 이벤트.
- `MainWindow` — 이벤트 연결, `CurrentGitSettings`로 비교 창을 연다.

### 2차

- `Services/DiffSettingsService.cs`(신규) — `DiffSettings`, 프리셋, 저장·이관. `GitSettingsService`에서 diff 항목 제거.
- `SettingsWindow` — `비교` 탭(`DiffPanel`) 신설, diff/외부 도구 컨트롤을 Git 탭에서 옮김. 저장은 `TrySave("비교", "diff-settings.xml")`.
- `DiffToolLauncher.cs`(신규) — Git 창의 외부 도구 실행 로직을 옮긴 공용 실행기. `GitDiffCommands.Files`에 `ExternalSelector`(`--no-index -- a b`).
- `Services/DiffHtmlReport.cs`(신규), `DiffReportDialog.cs`(신규, 대화상자 + `DiffReportExporter`), `GitDiffView`에 `HTML 보고서…` 버튼과 `TextLoader`.
- `GitWindow` — `DiffSettings` 사용, `RunDiffAsync`로 화면·보고서 공용 실행, `ApplySettings(git, diff)`, 설정 열기 콜백이 탭 이름을 받음.
- `FileCompareWindow` — 이름·위치 헤더, `⇄ 좌우 바꾸기`, 외부 도구, 보고서, `ApplySettings`.
- `FolderBrowser.GetSelectedFilePair`, `MainWindow` ⋯ 메뉴 `두 파일 비교…`·`OpenFileCompare`·`CurrentDiffSettings`.

## 변경 파일

- `Folderss/Services/GitDiffCommands.cs`
- `Folderss/Services/GitEncodingDiff.cs`
- `Folderss/Controls/GitDiffView.xaml.cs`
- `Folderss/Controls/FolderBrowser.xaml.cs`
- `Folderss/FileCompareWindow.cs` (신규)
- `Folderss/MainWindow.xaml.cs`
- `tests/Folderss.SearchTests/GitTests.cs`
- `README.md`, `docs/architecture.md`
- 2차: `Folderss/Services/DiffSettingsService.cs`(신규), `Folderss/Services/DiffHtmlReport.cs`(신규), `Folderss/DiffToolLauncher.cs`(신규),
  `Folderss/DiffReportDialog.cs`(신규), `Folderss/Services/GitSettingsService.cs`, `Folderss/SettingsWindow.xaml(.cs)`, `Folderss/GitWindow.xaml(.cs)`,
  `Folderss/Controls/GitDiffView.xaml(.cs)`, `Folderss/MainWindow.xaml`, `tests/Folderss.SearchTests/DiffHtmlReportTests.cs`(신규), 테스트 csproj

## 검증

- `dotnet test tests/Folderss.SearchTests` — 92 통과, 1 건너뜀(기존). 신규:
  - `RealGit_CompareTwoFiles_OutsideRepository` — 저장소 밖·공백/한글 경로, 줄 번호·추가/삭제, 전체 파일 모드, 같은 내용이면 종료 코드 0·빈 출력.
  - `RealGit_CompareTwoFiles_Utf16BomIsReDiffedAsText` — UTF-16 LE ↔ UTF-8 비교가 텍스트 diff로 바뀜, 진짜 바이너리는 그대로.
  - 구현 전에는 두 테스트가 컴파일 실패(`Files`/`ExpandFilesAsync` 없음)함을 확인.
- 앱 빌드: 리눅스 환경이라 `EnableWindowsTargeting`으로 컴파일만 확인(신규/변경 파일 오류 없음, `ConsolePanel`의 Windows 전용 패키지 오류는 기존 환경 문제). Windows 빌드와 실제 UI 동작(우클릭 메뉴 노출, 창 표시, 테마)은 사용자 확인 필요.

### 2차

- `dotnet test tests/Folderss.SearchTests` — 102 통과, 1 건너뜀(기존). 신규: `DiffSettings_RoundTrip_AndDefaults`,
  `DiffSettings_WithoutOwnFile_MigratesFromLegacyGitSettings`, `RealGit_CustomDifftool_ComparesTwoFilesOutsideRepository`(원래 경로가 도구에 넘어감),
  `ExternalTool_BuildsDifftoolArgs_PerModeAndTarget`(두 파일 인수), `DiffHtmlReportTests`(짝짓기, 이스케이프, 요약, 빈 diff).
- 실제 git 출력으로 만든 보고서 4종(양옆/한 줄 × 변경점만/전체)을 헤드리스 Chromium으로 열어 배치·색·이스케이프 확인.
- 앱 컴파일: 리눅스 `EnableWindowsTargeting` — 기존 `ConsolePanel` 환경 오류 외 없음. 설정 창·대화상자·메뉴의 실제 화면은 Windows에서 확인 필요.

## 알려진 제약

- git 미설치 시 비교 창에 "git 실행 파일을 찾을 수 없습니다…" 문구만 보인다.
- 줄바꿈만 다른 파일(CRLF ↔ LF)은 모든 줄이 바뀐 것으로 보인다(git 기본 동작).
- 앱 화면의 diff는 여전히 한 줄(unified)이며, 양옆 배치는 HTML 보고서에서만 된다.
- 이관 후 git-settings.xml을 한 번 저장하면 옛 diff 값은 그 파일에서 사라진다. 그때 diff-settings.xml 저장만 실패하면 다음 실행에 기본값이 된다(저장 실패 메시지로 알림).
- 보고서 HTML은 브라우저 인쇄로 PDF를 만들 수 있지만, 전체 파일 범위로 큰 파일을 넣으면 파일이 커진다(최대 10만 줄).

## 변경 이력

- 2026-09-30: 요청 접수, Git diff 재사용으로 구현, 자체 검증 → Ready for Verification.
- 2026-09-30: 2차 요청(비교 설정 분리, 외부 도구, HTML 보고서, ⋯ 메뉴, 위치·이름 표시, 좌우 바꾸기) 구현·자체 검증 → Ready for Verification.

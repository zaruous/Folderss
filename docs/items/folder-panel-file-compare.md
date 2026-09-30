# 폴더 패널 두 파일 비교

- 상태: Ready for Verification

## 요구사항

- 폴더 패널에서 파일 두 개를 선택한 경우 메뉴를 통해 두 파일을 비교한다.
- Git의 diff 기능을 재활용할지 별도 기능을 둘지 코드를 확인해 판단한다.

## 원인 분석 또는 설계

### 재활용 판단: Git diff 경로를 재사용한다

| 후보 | 장점 | 단점 |
|---|---|---|
| **Git diff 재사용** (`git diff --no-index` + `GitDiffView`) — 채택 | 이미 검증된 파서(`GitOutputParser.ParseDiff`)·뷰(줄 번호, 추가/삭제 색, 보기 모드, `Ctrl+C`, 2만 줄 제한)·대체 인코딩·UTF-16 BOM 재비교를 그대로 씀. 신규 코드가 작음. `--no-index`는 저장소 밖 파일도 됨 | git 설치 필요(없으면 오류 문구). unified 형식만(좌우 나란히 아님) |
| Monaco diff 에디터 (`docs/아이디어.md`) | 좌우 나란히, git 불필요 | 새 뷰어·HTML·WebView2 연동, 탭 `ContentId`·패널 잠금·레이아웃 복원 규칙까지 손봐야 함. 요청 범위보다 큼 |
| C# diff 알고리즘 직접 구현 | 의존성 없음 | 바퀴 재발명, 대용량 성능·정확성 검증 부담 |

`GitDiffView`는 Git 전용 상태를 갖지 않고 unified diff 텍스트만 받으므로(`BeginLoad`/`Complete`) 다른 창에 그대로 올릴 수 있다.

### 가정 (확인 필요)

- **메뉴 위치**: 파일 목록 우클릭(Windows 셸 메뉴) 맨 위 사용자 항목. 메인 메뉴(⋯)에는 넣지 않았다.
- **대상**: 같은 패널에서 선택한 정확히 두 개의 **파일**. 폴더가 섞이거나 세 개 이상이면 항목이 안 나온다. 좌우 패널에서 하나씩 고르는 비교, 폴더 비교(`directory-compare.md`)는 범위 밖.
- **왼쪽/오른쪽**: 선택 순서가 아니라 목록에 보이는 순서(위 = 왼쪽/변경 전). 바꾸기 버튼은 없다.
- **표시 위치**: 문서 탭이 아니라 별도 비모달 창(Git 창과 같은 방식). 탭으로 두면 레이아웃 저장·복원, 패널 잠금, `+ 새 패널` 정렬 규칙을 함께 다뤄야 한다.
- **옵션**: 공백 무시·대체 인코딩·보기 모드 초기값은 `설정 > Git` 값을 따른다(별도 설정 없음).

## 구현 내용

- `GitDiffCommands.Files(old, new)` — `diff --no-index --no-color --no-ext-diff -M … -- <old> <new>`. 차이 있으면 종료 코드 1(`IsSuccess(noIndex)`).
- `GitEncodingDiff.ExpandFilesAsync` — `--no-index` 출력의 경로는 git이 앞의 `/`를 떼는 등 원래 경로로 되돌릴 수 없어서(`/tmp/x` → `a/tmp/x`),
  기존 `ExpandAsync`(저장소 상대 경로 해석)를 쓰지 않고 받은 두 경로를 직접 읽어 BOM 재비교. 임시 파일 비교 부분은 `DiffDecodedAsync`로 뽑아 두 경로가 공유.
- `GitDiffView` — 보기 모드 상자를 `NoIndex`면 무조건 숨기던 것을, 빈 쪽이 있는 비교(`OldSide == null`, 추적 안 됨)일 때만 숨기도록 변경.
- `FileCompareWindow`(신규, 코드 구성) — `GitDiffView`를 담은 비모달 창. 보기 모드 변경 시 다시 불러오고, 창을 닫으면 진행 중인 git을 취소.
- `FolderBrowser` — 우클릭 시 파일 두 개면 `선택한 두 파일 비교` 항목 추가, `CompareFilesRequested` 이벤트.
- `MainWindow` — 이벤트 연결, `CurrentGitSettings`로 비교 창을 연다.

## 변경 파일

- `Folderss/Services/GitDiffCommands.cs`
- `Folderss/Services/GitEncodingDiff.cs`
- `Folderss/Controls/GitDiffView.xaml.cs`
- `Folderss/Controls/FolderBrowser.xaml.cs`
- `Folderss/FileCompareWindow.cs` (신규)
- `Folderss/MainWindow.xaml.cs`
- `tests/Folderss.SearchTests/GitTests.cs`
- `README.md`, `docs/architecture.md`

## 검증

- `dotnet test tests/Folderss.SearchTests` — 92 통과, 1 건너뜀(기존). 신규:
  - `RealGit_CompareTwoFiles_OutsideRepository` — 저장소 밖·공백/한글 경로, 줄 번호·추가/삭제, 전체 파일 모드, 같은 내용이면 종료 코드 0·빈 출력.
  - `RealGit_CompareTwoFiles_Utf16BomIsReDiffedAsText` — UTF-16 LE ↔ UTF-8 비교가 텍스트 diff로 바뀜, 진짜 바이너리는 그대로.
  - 구현 전에는 두 테스트가 컴파일 실패(`Files`/`ExpandFilesAsync` 없음)함을 확인.
- 앱 빌드: 리눅스 환경이라 `EnableWindowsTargeting`으로 컴파일만 확인(신규/변경 파일 오류 없음, `ConsolePanel`의 Windows 전용 패키지 오류는 기존 환경 문제). Windows 빌드와 실제 UI 동작(우클릭 메뉴 노출, 창 표시, 테마)은 사용자 확인 필요.

## 알려진 제약

- git 미설치 시 비교 창에 "git 실행 파일을 찾을 수 없습니다…" 문구만 보인다.
- 줄바꿈만 다른 파일(CRLF ↔ LF)은 모든 줄이 바뀐 것으로 보인다(git 기본 동작).
- `외부 도구로 비교` 버튼은 두 파일 비교에서 숨겨진다.

## 변경 이력

- 2026-09-30: 요청 접수, Git diff 재사용으로 구현, 자체 검증 → Ready for Verification.

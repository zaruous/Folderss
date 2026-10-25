# Git 창 로그·브랜치 비교: 파일을 고르면 코드 내용을 먼저, diff는 선택

- 상태: Ready for Verification

## 요구사항

- 로그 탭에서 바뀐 파일을 고르면 diff만 보여, 그 시점의 실제 코드(raw)를 볼 수 없다.
- 코드 보기가 기본이고 diff는 추가 기능으로(사용자 답변: "코드뷰가 우선이고 diff를 추가기능으로"). Git 창 안에서 본다.

## 원인 분석 또는 설계

- diff 칸의 `전체 파일` 보기 모드는 파일 전체를 보이지만 삭제 줄이 섞이고 +/− 색이 붙어 raw 내용이 아니다.
- 파일 목록 머리줄에 `diff 보기` 체크(기본 꺼짐). 꺼지면 고른 파일의 리비전 시점 내용을 diff 칸에 줄 번호와 함께(`GitDiffView.ShowLines`, 변경 없는 파일 보기와 같은 화면), 켜면 지금까지의 diff.
- 버전·범위는 사용자가 지정하지 않아 다음으로 정함:
  - 로그 탭: 그 커밋 시점(변경 후) 내용. 브랜치 비교 탭(같은 구조라 함께 적용): 비교 대상 쪽(`⇄`로 뒤집었으면 뒤집힌 대상) 끝의 내용.
  - 삭제된 파일은 그 시점에 내용이 없으므로 "`diff 보기`로 이전 내용" 안내만. 이전(부모) 버전 보기 버튼은 두지 않음.
- 내용은 git에 저장된 바이트 그대로(`cat-file blob`과 같음) — core.autocrlf·smudge 필터 등 체크아웃 변환은 적용하지 않는다(Git LFS 파일은 포인터 텍스트로 보임).
  디코딩·바이너리·10MB 한도는 변경 없는 파일 보기와 같은 규칙(`PreviewLines`로 뽑아 함께 씀).

## 구현 내용

- `GitLibReader.ReadBlob(root, revision, path, maxBytes)` — (Found, Bytes). 폴더·없는 경로는 Found=false, 한도 초과는 Bytes=null.
- `GitWindow.ShowRevisionContentAsync` — libgit2 우선, 실패 시 `cat-file -s` + `cat-file blob`(`ReadBlobCliAsync`).
  읽는 사이 다른 파일·커밋·폴더를 고르거나 diff로 바꾸면 늦게 온 내용을 버린다(`GitDiffView.CurrentRequestId` 비교 — Clear·BeginLoad·ShowLines마다 바뀜).
- `GitWindow.xaml` — 로그·브랜치 비교 파일 목록 머리줄에 `diff 보기` 체크(`LogFilesDiffToggle`, `BranchFilesDiffToggle`). 바꾸면 고른 파일을 다시 보인다.
- 안내 문구 "파일을 선택하면 차이를…" → "…내용을 보여 줍니다."(로그·브랜치 탭만).

## 범위 밖 / 주의

- 코드 색 강조 없음(diff 칸과 같은 단색 줄 목록). 편집·저장 없음(읽기 전용).
- `diff 보기` 체크는 저장하지 않는다(창을 다시 열면 코드 보기로 시작).
- 코드 보기에서는 diff 칸의 보기 모드·외부 도구·HTML 보고서 버튼이 숨는다(`ShowLines` 동작). diff로 바꾸면 다시 보인다.

## 변경 파일

- `Folderss/Services/GitLibReader.cs`, `Folderss/Controls/GitDiffView.xaml.cs`
- `Folderss/GitWindow.xaml`, `Folderss/GitWindow.xaml.cs`
- `tests/Folderss.SearchTests/GitLibReaderTests.cs`
- `README.md`, `docs/architecture.md`

## 검증

- 단위 테스트(`dotnet test tests/Folderss.SearchTests --filter "FullyQualifiedName~Git"`, Linux): 168개 통과.
  - `GitLibReaderTests.Blob_MatchesCatFile` — 한글 경로(CRLF 섞임)·바이너리·나중에 삭제된 파일에서 `ReadBlob` 바이트가 `git cat-file blob`과 같음.
    삭제 뒤·폴더 경로는 Found=false, 한도 초과는 Bytes=null.
- Linux에서 `-p:EnableWindowsTargeting=true`로 앱 컴파일: 기존 `ConsolePanel`(Windows 전용 패키지) 오류 1건 외 새 오류 없음. Windows 빌드 확인 필요.
- Windows 수동 확인 필요:
  1. 로그에서 커밋 → 파일 클릭 → 그 커밋 시점 코드가 줄 번호와 함께. 제목 끝 `— 코드`.
  2. `diff 보기` 켜기/끄기 → 같은 파일이 diff ↔ 코드로 바뀜. 켠 채로 다른 파일을 고르면 diff.
  3. 삭제된 파일은 안내 문구, 바이너리·10MB 초과는 안내. 한글·UTF-16 BOM 파일 디코딩.
  4. 큰 파일을 고른 직후 다른 커밋·파일·트리 폴더를 고르면 이전 파일 내용이 끼어들지 않음.
  5. 브랜치 탭 비교에서도 같은 동작(대상 쪽 내용, `⇄` 후에는 반대쪽).

## 변경 이력

- 2026-10-23: 요청 접수(로그 변경 파일의 raw 코드 보기). 표시 위치는 사용자 답변대로 Git 창 안 코드 보기 기본 + diff 선택. 구현·단위 테스트 완료, Windows 확인 대기.
- 2026-10-23: 코드 보기에서 줄 번호가 두 개(diff처럼 변경 전/후 열) 나오는 문제 → `ShowLines` 때만 `LineList.Tag = "SingleLineNumber"`로
  변경 전 번호 열을 접음(`GitDiffView.xaml` DataTrigger, 열 너비 Auto). diff(`Complete`)는 Tag를 비워 두 열 그대로. 같은 화면을 쓰는 '변경 없는 파일 보기'도 함께 고쳐짐.
  행마다 숨기지 않은 이유: diff의 추가/삭제 줄은 한쪽 번호가 비어 있어 행별로 접으면 정렬이 어긋난다. WPF 화면이라 단위 테스트 없음 — Windows에서 확인 필요.

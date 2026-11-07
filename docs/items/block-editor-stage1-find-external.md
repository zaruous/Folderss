# 블록 편집기 1단계: 찾기·바꾸기, 외부 변경 반영, 링크 Ctrl+클릭 (#75)

- 상태: Done — master 머지 확인 (이전: Ready for Verification)
- 상태 전환일: 2026-11-07

## 요구사항

이슈 #75(블록 편집기 1단계: 편집 조작) 중 **찾기·외부 변경** 항목. 같은 이슈의 "선택·이동"과 "표" 항목은 다른 브랜치에서 따로 구현한다.
#81(Markdown 뷰어와의 격차)의 `Ctrl+F` 문서 내 검색, 외부 변경 자동 반영(스크롤 유지), 로컬 파일 링크·`#제목`·`파일.md#제목` 이동도 이 작업으로 메운다.

- `Ctrl+F` 문서 내 검색 — 가상화 상태라 모델에서 찾고 결과 블록으로 스크롤·강조, `F3`/`Shift+F3` 다음·이전, 일치 개수 표시
- `Ctrl+H` 바꾸기(하나씩·모두). 모두 바꾸기는 실행 취소 한 단계
- 외부 변경 감지·반영 — 미저장 변경이 있으면 확인, 스크롤 위치(블록 앵커) 유지, 자기 저장 무시, 사이드카도 다시 읽기
- 링크 `Ctrl+클릭`(로컬 파일은 뷰어 탭, 실행 파일은 확인), 문서 안 `#제목`·`파일.md#제목` 이동

원칙: 가상화(보이는 블록만 행), 편집하는 행에만 TextBox(입력기 TSF 잠금 회피), 실행 취소는 변경 구간만 기록, 1만 줄 문서 글자 입력 중앙 ≤ 20ms.

## 원인 분석 또는 설계

- 순수 로직은 `Services/BlockFind`(WPF 없음, 테스트 프로젝트에 소스 링크): 찾기(`FindAll`, 겹치지 않게, 표 행은 셀 경계를 넘는 일치 제외, 구분선·목차 제외),
  다음·이전(`NextMatch`, 처음↔끝 순환), 바꾸기(`ReplaceOne`·`ReplaceAll`, 표 행은 바꿀 글자의 탭·줄바꿈을 공백으로), 셀 위치 변환, 제목 슬러그(`Slugify` — `markdown-app.js`와 같은 GitHub 규칙,
  줄 안 서식 원문에서 링크는 글자만, `**`·`__`·`~~` 제거), `FindHeading`(중복 `-1`, 퍼센트 인코딩, 손으로 쓴 앵커는 같은 규칙으로 한 번 더), `MapAnchor`, `LinkAt`, `ResolveLocalLink`(Markdown 뷰어와 같은 규칙).
- 강조: 지금 결과 하나만 그 행 입력란의 선택으로 보여 준다(`IsInactiveSelectionHighlightEnabled`). 모든 결과를 칠하려면 결과가 있는 행마다 TextBox나 Inline을 만들어야 해서 "편집하는 행에만 TextBox" 원칙과
  가상화 비용에 맞지 않는다 — 대신 `3/17` 개수 표시.
- 결과 목록 갱신: 문서 버전(`_version`, 실행 취소로 돌아가면 같은 값이므로 내용과 1:1)·검색어·대소문자가 같으면 다시 쓰고, 찾기 막대가 열려 있을 때만 400ms 타이머가
  "버전이 바뀌었고 입력이 300ms 멈췄을 때" 다시 센다. 글자 입력 경로에는 `HandleFindKey`의 비교 몇 개만 더해진다.
- 바꾸기는 모델을 고치고 `SyncRows()` + `RecordChange()` 한 번 — `RecordChange`가 앞뒤 같은 블록을 빼고 바뀐 구간만 기록하므로 모두 바꾸기도 실행 취소 한 단계·변경 구간만.
- 외부 변경: Markdown 뷰어 방식(FileSystemWatcher + 디바운스 + 디스크 내용 비교, 거부한 내용 기억, 확인창 재진입 막기, owner 지정, 확인 뒤 최신 내용 다시 읽기)을 따르되
  Markdown 뷰어와 같이 **항상 묻는다**(미저장 변경이 없으면 기본 '예', 있으면 경고·기본 '아니요'). 처음에는 #75 문구대로 미저장이 없으면 묻지 않았으나 사용자 결정으로 바꿈(2026-11-01). 사이드카도 같은 감시자(`Filters` 두 개)로 본다.
  스크롤은 다시 읽기 전 화면 맨 위 블록의 복사본·위치·블록 안 오프셋을 잡아 새 문서에서 종류·본문이 같은 가장 가까운 블록으로 돌아간다(새 블록은 Id가 다르므로).
- 링크: 다른 문서의 `파일.md#제목`은 Markdown 뷰어의 `PendingAnchors`(이번에 `internal`로 공유)에 넣어, 그 파일이 Markdown 뷰어로 열리든 블록 편집기로 열리든 제목으로 간다.

## 구현 내용

- `Folderss/Services/BlockFind.cs` (새 파일): 위 순수 로직.
- `Folderss/Controls/BlockEditor.Find.cs` (새 partial): 찾기 막대(테마 리소스 `SurfaceBackground`·`BorderBrush`·`SecondaryText`·`PrimaryText`, 기본 Button·TextBox 스타일),
  `OpenFind`/`CloseFind`/`FindNext`/`ReplaceCurrent`/`ReplaceAllMatches`, `HandleFindKey`, 접힌 토글 펴기(`RevealBlock`), `ScrollToHeading`, `CaptureScrollAnchor`/`RestoreScrollAnchor`,
  링크 `Ctrl+클릭`(`ElementTree.FindAncestor<TextBox>` → `LinkOpenRequested`). 하네스용 `IsFindOpen`·`FindMatchCount`·`CurrentFindMatch`.
- `Folderss/Controls/BlockEditor.cs`: 생성자에서 `InitializeFind()`, `OnPreviewKeyDown` 맨 앞에서 `HandleFindKey` — 두 줄만 연결.
- `Folderss/Controls/BlockEditor.Panel.cs`: `BlockPanel.GetTopAnchor`·`ScrollToAnchor`(앵커 Id·오프셋을 직접 두고 다음 측정에서 실제 높이로 위치를 정함).
- `Folderss/Viewers/BlockEditorViewer.cs`: partial로 바꾸고 `IViewerActivationAware`·`IDisposable` 구현, `Load`를 `ApplyContent`(앵커 유지 옵션)로 나눔, 저장 직후 `_diskText`·`_diskSidecar` 갱신,
  `HandleShortcut`에서 `ShowSearch` → `OpenFind`, 단축키 도움말 갱신.
- `Folderss/Viewers/BlockEditorViewer.External.cs` (새 partial): 파일 감시·다시 읽기·링크 열기·`PendingAnchors` 소비.
- `Folderss/Viewers/MarkdownViewer.xaml.cs`: `PendingAnchors`·`ReadAllTextAllowingWriters`를 `internal`로(동작 변경 없음).

## 변경 파일

- `Folderss/Services/BlockFind.cs`, `Folderss/Controls/BlockEditor.Find.cs`, `Folderss/Viewers/BlockEditorViewer.External.cs` (새 파일)
- `Folderss/Controls/BlockEditor.cs`, `Folderss/Controls/BlockEditor.Panel.cs`, `Folderss/Viewers/BlockEditorViewer.cs`, `Folderss/Viewers/MarkdownViewer.xaml.cs`
- `tests/Folderss.SearchTests/BlockFindTests.cs` (새 파일), `tests/Folderss.SearchTests/Folderss.SearchTests.csproj` (`BlockFind.cs` 소스 링크)
- `README.md`, `docs/architecture.md`, `docs/keyboard-shortcuts.md`, 이 문서

## 검증

- [x] `dotnet build .\Folderss.sln -c Debug` 오류 0, `dotnet build tests/Folderss.SearchTests` 오류 0
- [x] 단위 테스트 `dotnet test tests/Folderss.SearchTests --filter BlockFindTests` — 병합 후 전체 662개 중 통과 658·건너뜀 4·실패 0
  - 찾기: 문서 순서·대소문자·겹침 없음·구분선/목차 제외·표 셀 경계·빈 검색어, `NextMatch` 순환
  - 바꾸기: 하나(그 자리만·끝 위치·바뀐 뒤 무시), 모두(범위 보고·검색어를 포함한 바꿀 글자·표 셀 구조 유지·결과 없음), 셀 위치 왕복
  - 앵커: 슬러그 5가지(한글·기호·굵게·링크·밑줄), 제목 찾기(중복·토글 제목·퍼센트 인코딩·느슨한 일치·없음), `MapAnchor`(위에 삽입·가까운 중복·바뀐 블록)
  - 링크: Markdown·꺾쇠·맨 URL·이미지·제목 붙은 링크·`www.`, 로컬 경로 해석(상대·인코딩·질의·웹·mailto·`#`만)
- [x] WPF 하네스(스크래치 하네스, 병합 후 일괄) — 추가할 검사(결과는 아래 "병합 후 검증 결과"):
  1. `Ctrl+F` → 찾기 막대 열림, 검색어 입력 → `FindMatchCount`가 모델 일치 수와 같고 화면 밖 결과 블록으로 스크롤·그 행 입력란 선택 = 일치 글자
  2. `F3` 3번·`Shift+F3` 1번 → `CurrentFindMatch` 0→1→2→1, 마지막에서 `F3` → 0(순환)
  3. 접힌 토글 안 결과로 이동하면 토글이 펴짐(저장할 변경 아님 — `IsModified` 그대로)
  4. 표 셀 안 결과 → 그 셀 입력란에 선택
  5. `Ctrl+H` → 바꾸기 `Enter` 한 번 → 그 결과만 바뀜·다음 결과 선택, 모두 바꾸기 → 결과 0, `Ctrl+Z` 한 번으로 모두 되돌아감
  6. `Esc` → 막대 닫힘·지금 결과 입력란에 포커스, 찾기 입력란 `Ctrl+Z`가 문서를 되돌리지 않음
  7. 외부에서 파일 덮어쓰기(미저장 없음) → 확인창(기본 예) → 예 → 반영, 화면 맨 위 블록 유지 / 미저장 있음 → 확인창, 아니요 → 유지·같은 내용으로 다시 묻지 않음
  8. 편집기에서 저장 → 감시 이벤트가 와도 다시 읽지 않음(상태 줄에 `외부 변경 반영` 없음), 사이드카만 외부에서 바꿈 → 블록 색 반영
  9. `Ctrl+클릭`: `[a](#제목)` → 그 제목 블록이 맨 위·커서, `[b](other.md#제목)` → 새 탭에서 그 제목, `[c](tool.bat)` → 실행 확인창
  10. 성능: 1만 줄 문서에서 찾기 막대를 연 채 글자 입력 중앙 ≤ 20ms, 모두 바꾸기(결과 수천 개) 시간 기록

### 병합 후 검증 결과 (2026-11-01)

WPF 하네스(스크래치, 화면 밖 창 — 방식은 `block-editor-stage1-selection.md` 검증 절. 확인창은 하네스 UI 스레드의 메시지 상자만 찾아 답함): 찾기 34개·외부 변경/링크 22개 **모두 통과**, 고친 것 없음.

| # | 검사 | 결과 |
|---|---|---|
| 1 | `Ctrl+F` 막대·입력란 포커스, `사과` → 결과 5 = 모델 일치 수, 화면 밖 결과로 스크롤, 그 행 입력란 선택 = 일치 글자, 비활성 강조, `1/5` | 통과 |
| 2 | `F3`·`F3`·`Shift+F3` → 0→1→2→1, 마지막에서 `F3` → 0, 처음에서 `Shift+F3` → 4, 입력란 `Enter` 다음 | 통과 |
| 3 | 접힌 토글 안 결과 → 토글 펴짐, `IsModified` 그대로 | 통과 |
| 4 | 표 셀 결과 → 그 셀(열 1) 입력란 선택 | 통과 |
| 5 | `Ctrl+H` → 바꾸기 `Enter` 한 번 → 그 결과만·다음 결과 선택·결과 4 / `Ctrl+Alt+Enter` 모두 바꾸기 → 0, 표 셀 구조 유지 / `Esc` 뒤 `Ctrl+Z` 한 번에 모두 되돌림 | 통과 |
| 6 | `Esc` → 막대 닫힘·지금 결과 입력란 포커스·선택 유지, 다시 `Ctrl+F` → 선택 글자가 검색어, 찾기 입력란 `Ctrl+Z`는 문서 버전 그대로 | 통과 |
| 7 | (54e5f6d "항상 묻기" 기준으로 다시 실행) 미저장 없음: 외부에서 위에 문단 2개 삽입 → 확인창 1번(**Question·기본 예**, "다시 읽을까요?"만) → 예 → 반영, 맨 위 블록·블록 안 위치(`문단 50@7`) 유지·수정 아님 / 미저장 없음·아니요 → 화면 그대로, 같은 내용으로 다시 묻지 않음, 디스크가 편집기와 같아지면 묻지 않음 / 미저장 있음: 확인창(**Warning·기본 아니요**, "저장하지 않은 변경 내용이 사라집니다") → 아니요 → 편집 유지, 같은 내용 다시 저장해도 묻지 않음, 내용이 또 바뀌면 다시 묻고 예 → 다시 읽음 | 통과 |
| 8 | 편집기 저장 → 감시 이벤트가 와도 다시 읽지 않음(상태 줄 `외부 변경 반영` 없음, 확인창 없음) / 사이드카만 외부에서 바꿈 → (54e5f6d부터) 확인창 1번(Question·기본 예) → 예 → 블록 색 반영·수정 아님 | 통과 |
| 9 | `Ctrl+클릭` `[a](#끝-제목)` → 제목 블록이 맨 위(y=24)·커서, Ctrl 없는 클릭은 이동 없음 / `[b](other.md#목표-제목)` → `FileOpenRequested`·`PendingAnchors`, 새 뷰어로 열면 그 제목이 맨 위·앵커 소비 / `[c](tool.bat)` → "실행 확인" 창(아니요) | 통과 |
| 10 | 성능(아래) | 통과 |

- 성능(Debug, 11,424줄·블록 9,862개·표 행 8,301·이미지 60): 찾기 막대를 연 채 문단 글자 입력 **중앙 2.3ms**(최대 3.3ms), 결과 다시 세기 타이머가 섞여도 중앙 1.2ms(최대 22.5ms),
  입력이 멈춘 뒤 결과 다시 세기 1회 중앙 2.1ms, `BlockFind.FindAll`(결과 6,000) 1.9ms, `F3` 중앙 6.0ms, **모두 바꾸기 6,000개 23ms**, 그 실행 취소 41ms(한 번에 원래 문서).
- 같은 문서 스크롤(#79 기준 ≤ 100ms): 휠 120px 중앙 17.5ms, 한 화면(800px) 건너뛰기 중앙 54.7ms, **큰 폭(문서 7%씩) 점프 중앙 71.0ms(최대 96ms)**, 처음↔끝 중앙 31.6ms.
- 사람이 직접 볼 것: 찾기 막대 모양·테마 색(밝은·어두운 테마), 비활성 선택 강조가 눈에 띄는지, 실제 다른 편집기(메모장·VS Code)에서 저장할 때 반영 시점,
  확인창이 앱 앞에 뜨는지, 실제 탭에서 `파일.md#제목` 새 탭 열기(하네스는 뷰어 이벤트까지만 확인), 실행 확인에서 "예" 실행.

### #76 병합 후 회귀 (2026-11-01)

- 하네스 `harness76m`(`C:\Users\KYJ\AppData\Local\Temp\claude\D--git-cshap-Folderss\0e9a6dec-1b59-4430-8014-acdde638b328\scratchpad\harness76m\`) `find` 34·`external` 27 **모두 통과**(버그 1건 수정 후). 링크 `Ctrl+클릭` 시험은 표식 없이 보이는 링크 글자를 누르도록 바꾸고
  `[a]`·`[c]`는 **편집하지 않는 행의 표시 글자(Run)**, `[b]`는 **서식 입력란(RichTextBox)** 에서 눌렀다.
- 고친 버그(9228041): **표시 글자의 링크를 `Ctrl+클릭`하면 앱이 종료** — `OnLinkPreviewMouseDown`의 `_findBar.IsAncestorOf(e.OriginalSource)`가 Run(ContentElement)에서 `InvalidOperationException`.
  `ElementTree.IsWithin`으로 바꿈(CLAUDE.md "이벤트 원본에서 조상 찾기" 패턴).
- 서식 블록 안 찾기 결과 강조 위치(표식 뺀 글자 위치)·바꾸기 뒤 서식 유지는 `block-editor-stage2-inline-format.md` 검증 10.

## 변경 이력

- 2026-11-01: 구현, 앱·테스트 프로젝트 빌드 확인. 테스트·하네스 실행은 #75 다른 항목(선택·이동, 표)과 병합한 뒤 일괄 진행.
- 2026-11-01: 병합 후 하네스 56개(찾기 34·외부 변경/링크 22)·단위 테스트·1만 줄 성능 통과 (`Ready for Verification`, 사용자 확인 대기)

# 블록 편집기 1단계: 찾기·바꾸기, 외부 변경 반영, 링크 Ctrl+클릭 (#75)

- 상태: Ready for Verification
- 상태 전환일: 2026-11-01

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
  #75 문구대로 **미저장 변경이 없으면 묻지 않고 바로 반영**한다(Markdown 뷰어는 항상 묻는다 — 차이). 사이드카도 같은 감시자(`Filters` 두 개)로 본다.
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
- [ ] 단위 테스트 `dotnet test tests/Folderss.SearchTests --filter BlockFindTests` (병합 후 일괄 실행 예정 — 이번 작업에서는 실행하지 않음)
  - 찾기: 문서 순서·대소문자·겹침 없음·구분선/목차 제외·표 셀 경계·빈 검색어, `NextMatch` 순환
  - 바꾸기: 하나(그 자리만·끝 위치·바뀐 뒤 무시), 모두(범위 보고·검색어를 포함한 바꿀 글자·표 셀 구조 유지·결과 없음), 셀 위치 왕복
  - 앵커: 슬러그 5가지(한글·기호·굵게·링크·밑줄), 제목 찾기(중복·토글 제목·퍼센트 인코딩·느슨한 일치·없음), `MapAnchor`(위에 삽입·가까운 중복·바뀐 블록)
  - 링크: Markdown·꺾쇠·맨 URL·이미지·제목 붙은 링크·`www.`, 로컬 경로 해석(상대·인코딩·질의·웹·mailto·`#`만)
- [ ] WPF 하네스(스크래치 하네스, 병합 후 일괄) — 추가할 검사:
  1. `Ctrl+F` → 찾기 막대 열림, 검색어 입력 → `FindMatchCount`가 모델 일치 수와 같고 화면 밖 결과 블록으로 스크롤·그 행 입력란 선택 = 일치 글자
  2. `F3` 3번·`Shift+F3` 1번 → `CurrentFindMatch` 0→1→2→1, 마지막에서 `F3` → 0(순환)
  3. 접힌 토글 안 결과로 이동하면 토글이 펴짐(저장할 변경 아님 — `IsModified` 그대로)
  4. 표 셀 안 결과 → 그 셀 입력란에 선택
  5. `Ctrl+H` → 바꾸기 `Enter` 한 번 → 그 결과만 바뀜·다음 결과 선택, 모두 바꾸기 → 결과 0, `Ctrl+Z` 한 번으로 모두 되돌아감
  6. `Esc` → 막대 닫힘·지금 결과 입력란에 포커스, 찾기 입력란 `Ctrl+Z`가 문서를 되돌리지 않음
  7. 외부에서 파일 덮어쓰기(미저장 없음) → 묻지 않고 반영, 화면 맨 위 블록 유지 / 미저장 있음 → 확인창, 아니요 → 유지·같은 내용으로 다시 묻지 않음
  8. 편집기에서 저장 → 감시 이벤트가 와도 다시 읽지 않음(상태 줄에 `외부 변경 반영` 없음), 사이드카만 외부에서 바꿈 → 블록 색 반영
  9. `Ctrl+클릭`: `[a](#제목)` → 그 제목 블록이 맨 위·커서, `[b](other.md#제목)` → 새 탭에서 그 제목, `[c](tool.bat)` → 실행 확인창
  10. 성능: 1만 줄 문서에서 찾기 막대를 연 채 글자 입력 중앙 ≤ 20ms, 모두 바꾸기(결과 수천 개) 시간 기록

## 변경 이력

- 2026-11-01: 구현, 앱·테스트 프로젝트 빌드 확인. 테스트·하네스 실행은 #75 다른 항목(선택·이동, 표)과 병합한 뒤 일괄 진행.

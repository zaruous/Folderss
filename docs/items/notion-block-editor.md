# Notion 스타일 블록 편집기 (BlockEditor 컨트롤 + `builtin:blocks` 뷰어)

- 상태: Ready for Verification

## 요구사항

- Notion(`C:\Users\KYJ\AppData\Local\Programs\Notion\Notion.exe`)의 편집 기능을 분석해, C# 프로그램(Folderss)에 컨트롤을 추가해 만든다.
- (2026-10-29 추가) 대용량 문서 — 표·이미지가 든 약 1만 줄 문서에서 편집·행 추가가 버벅임 없이 빨라야 한다.

## 원인 분석 또는 설계

- **Notion.exe 분석**: v42.4.1 Electron 앱. `resources/app.asar`(138개 파일)에는 `.webpack/main`·`renderer`(탭·설정·활동 모니터·SQLite 브라우저 등 셸 UI),
  `better-sqlite3`, `@notionhq/desktop-native`만 있고 블록 편집기 코드는 없다(실행 때 notion.so에서 받음). 그래서 Notion 코드를 가져오지 않고(저작권),
  공개된 편집 동작을 기준으로 WPF로 새로 만들었다: 블록 단위 문서, `/` 명령 메뉴, 문단 앞 Markdown 입력 단축, 블록 손잡이(+, ⋮⋮ 끌기·메뉴),
  Esc 블록 선택, `Ctrl+Shift+↑↓` 이동, `Ctrl+D` 복제, `Ctrl+Alt+0~8` 전환, `Ctrl+Enter` 체크·접기, 번호 목록 수준별 `1.`→`a.`→`i.`.
- **어디에 붙일지**: Folderss의 확장 지점인 뷰어(`IFileViewer`)로 넣었다. 저장 형식은 Notion 전용 DB가 아니라 Markdown — 다른 도구·Git과 함께 쓸 수 있게.
  기본 `.md` 매핑(Markdown 뷰어)은 바꾸지 않고, `설정 > 뷰어`에서 원하는 확장자를 `Block Editor (Notion 스타일)`로 매핑한다.
- **WebView2 대신 WPF**: 요청이 "C# 버전 프로그램에 컨트롤 추가"라 코드로 만든 WPF 컨트롤(`Controls/BlockEditor.cs`)로 구현. 블록마다 TextBox 하나.
  줄 안 서식(굵게 등)은 WYSIWYG가 아니라 Markdown 표식으로 보인다(RichTextBox 블록 + FlowDocument ↔ Markdown 변환은 후속 후보).
- **Notion 블록 ↔ Markdown**: 토글 = `<details [open]><summary>`(GitHub이 그림), 콜아웃 = GitHub 알림 `> [!NOTE|TIP|IMPORTANT|WARNING|CAUTION]`,
  해석하지 않는 front matter·표·HTML·들여쓴 코드는 `Raw` 블록으로 원문 보존. 빈 문단은 Markdown으로 나타낼 수 없어 저장하지 않는다.
- **대용량(2차)**: 1차는 블록마다 TextBox를 만들어 열기·메모리가 블록 수에 비례하고(3,000블록 제한), 실행 취소가 입력마다 문서 전체를 복사했다. 2차에서
  - 가상화 패널(`BlockPanel`, IScrollInfo) — 보이는 블록만 행을 만들고 나머지는 높이 추정·기억, 스크롤은 블록 기준 앵커.
  - 변경분 실행 취소(`HistoryEntry`) — 동결 사본과 비교해 바뀐 구간만 기록, 글자 입력은 그 블록만.
  - 표 행 블록(`TableRow`)·이미지 블록(`Image`) — 표 행마다 가상화되어 수천 행 표도 행 추가 비용이 같다. 이미지는 백그라운드 디코딩·LRU 캐시.
  - 표시 전용 행 — 측정 중 "큰 폭 스크롤이 가끔 0.5~5초 멈춤"을 스택 샘플링으로 추적하니, IME 설치 환경에서 WPF가 화면에 붙은 TextBox 거의 전부(298개 중 292개)를
    입력기(TSF)에 등록하고, 재사용 행의 `Text`를 바꿀 때 `TextStore.RequestLock`으로 입력기와 프로세스 간 잠금을 기다리고 있었다. `IsInputMethodEnabled=false`로는
    등록이 없어지지 않아, 평소에는 TextBlock으로 그리고 마우스를 올리거나 커서가 들어온 행(표는 그 셀)에만 TextBox를 만들도록 바꿨다(등록 TextBox 292 → 1~3개).
- 상세 설계(가상화, 실행 취소, 성능 주의점)는 `docs/architecture.md`의 "블록 편집기" 절.

## 구현 내용

- `Models/EditorBlock.cs` — `BlockKind`(텍스트·제목1~3·글머리·번호·할 일·토글·인용·콜아웃·코드·구분선·원문), 블록 모델. `Id`는 `Clone`이 유지(실행 취소), `Duplicate`가 새로 발급.
- `Services/BlockMarkdown.cs` — Markdown 읽기(CommonMark 목록 중첩·이어지는 줄·Setext 제목·펜스·front matter·`<details>` 중첩)·쓰기(부모 내용 열 들여쓰기, 펜스 길이 자동),
  `TryMatchShortcut`(`# `, `## `, `### `, `- `/`* `/`+ `, `1. `/`1) `, `[] `/`[x] `, `> `/`" `, ` ``` `, `---`).
- `Services/BlockOperations.cs` — `SubtreeEnd`, `ExtendToSubtrees`, `CanIndent`, `NormalizeIndents`, `MoveRange`(형제 단위, 부모 경계에서 밖으로), `ListLabels`, `VisibleFlags`.
- `Controls/BlockEditor.cs` — 편집기 컨트롤. Enter 나누기(빈 목록은 내어쓰기→텍스트, 자식 있는 블록·펼친 토글은 첫 자식), 맨 앞 Backspace(텍스트로→내어쓰기→구분선 삭제→합치기),
  끝 Delete 합치기, ↑↓ 가로 위치 유지 이동, `/` 메뉴(한·영 키워드, 빈 블록은 전환·아니면 아래에 생성, `/날짜`), 여러 줄 붙여넣기 → 블록, 손잡이 끌어 옮기기(놓을 위치 선),
  블록 선택(Shift 확장, Del, Ctrl+C/X/V/D/A, Tab, Ctrl+Alt+숫자), 줄 안 서식 단축키, 문서 단위 실행 취소(같은 블록 입력 1.5초 묶음), 전체 너비, 테마 리소스(DynamicResource).
- (2차) `Controls/BlockEditor.Panel.cs`(가상화), `BlockEditor.Row.cs`(표시 전용 행·지연 TextBox·표 셀·이미지), `BlockEditor.Table.cs`(표 키·행/열 추가·삭제·엑셀 붙여넣기,
  `/표`·`/이미지`, 이미지 경로 해석), `BlockImageLoader.cs`. `EditorBlock`에 `TableRow`·`Image`, `Source`·`TableAlign`, `SameContent`. `BlockMarkdown`에 GFM 표·이미지 줄,
  `BlockOperations`에 `ListNumbers`(할당 없는 번호)·`TableLayout`.
- `Viewers/BlockEditorViewer.cs` — 툴바(저장·전체 너비·단축키 도움말·인코딩/줄바꿈/저장 상태), BOM·CP949 판별(`MarkdownViewer.DetectEncoding`을 `internal`로), 인코딩·CRLF 유지 저장,
  다시 읽기 때 미저장 확인, 이미지 기준 폴더 전달. 블록 수 제한 없음(32MB 초과 파일만 열지 않음).
- `Services/ViewerConfigService.cs` — `BuiltInBlocksKey = "builtin:blocks"`, `Resolve`, `GetViewerKeys`. `SettingsWindow.xaml.cs` — 표시 이름 `Block Editor (Notion 스타일)`.

## 범위 밖 / 주의

- 줄 안 서식은 Markdown 표식 그대로 보인다(WYSIWYG 아님). 블록을 가로지르는 마우스 텍스트 선택, 외부 변경 자동 반영, 문서 내 검색, 데이터베이스·페이지 링크 블록,
  원격 이미지 표시, 표 셀 안 줄바꿈은 없다.
- 저장하면 Markdown 형식이 정리된다(글머리 `-`, 번호 다시 매김, 빈 줄 하나, 빈 문단 생략) — 한 글자만 고쳐도 파일 전체 서식이 바뀔 수 있다.
- 토글 접기·펴기는 미저장 변경으로 세지 않지만, 저장하면 그 상태가 `open` 속성으로 들어간다.
- 표 행 추가(Enter) 50~65ms 중 편집기 처리는 5~6ms이고 나머지는 새 TextBox 포커스 후 WPF 그리기·입력기 처리(빈 TextBox 하나도 같음).
- 마우스를 올린 행에 TextBox를 만들므로 마우스로 많은 행을 훑으면 그 행들은 재사용되지 않는다(새로 만듦 — TextBlock 행이라 비용이 작다).

## 변경 파일

- `Folderss/Models/EditorBlock.cs`, `Folderss/Services/BlockMarkdown.cs`, `Folderss/Services/BlockOperations.cs` (신규)
- `Folderss/Controls/BlockEditor.cs`, `BlockEditor.Panel.cs`, `BlockEditor.Row.cs`, `BlockEditor.Table.cs`, `BlockImageLoader.cs`, `Folderss/Viewers/BlockEditorViewer.cs` (신규)
- `Folderss/Services/ViewerConfigService.cs`, `Folderss/SettingsWindow.xaml.cs`, `Folderss/Viewers/MarkdownViewer.xaml.cs`(`DetectEncoding` internal)
- `tests/Folderss.SearchTests/BlockMarkdownTests.cs`(신규), `Folderss.SearchTests.csproj`(소스 링크), `ViewerStubs.cs`
- `README.md`, `docs/architecture.md`, `CLAUDE.md`·`AGENTS.md`(뷰어 추가 체크리스트)

## 검증

- `dotnet build .\Folderss.sln -c Debug`: Exit 0, 새 파일 경고 없음.
- `dotnet test tests/Folderss.SearchTests`: 436 통과(건너뜀 4, 기존). `BlockMarkdownTests` 33개 — 정규형 문서 왕복 불변, 변형 문법 정규화, 이어지는 줄, 목록 자식 들여쓰기 열,
  번호 다시 매김, 빈 문단, 펜스 안 펜스, 토글 중첩·접힘, 한 줄 `<details>`, 원문 보존, 입력 단축, 이동·들여쓰기 규칙·정규화·번호 표시·접힘.
- WPF 하네스(스크래치, 합성 키·텍스트 입력으로 실제 창에서 실행) 49개 항목 통과: `# `→제목, `- `→글머리, Enter·Tab·빈 목록 Enter 내어쓰기→텍스트, `/할일`+Enter,
  Backspace 전환·합치기와 커서 위치, Undo/Redo, 중간 Enter 나누기, ↑↓ 이동, 블록 이동, Esc 선택+Del, 여러 줄 붙여넣기, 코드 블록 Ctrl+Enter, `---`·` ``` ` 자동 변환,
  저장 후 Undo로 미수정 복귀. 뷰어: BOM·CRLF 유지 저장, `*`→`-` 정규화, 수정 이벤트, 3,000블록 초과 거부. 다크·라이트 테마 화면 확인.
- 성능(1,600블록): 열기 약 2.5초, Enter 약 50ms, 입력 약 15ms, 실행 취소 약 15ms.
  처음에는 Enter·입력마다 약 1초, 편집 중 열기 13~26초였다 — 원인 두 가지(ScrollViewer `Auto` 이중 측정, UI Automation 전체 트리 갱신)를 렌더 중 스택 샘플링으로 찾아 고침.
- (2차) `dotnet test tests/Folderss.SearchTests`: 443 통과(건너뜀 4). 표 셀·정렬·이스케이프·열 맞춤, 표 판별 조건, 표 경계, `TableLayout`, 이미지 줄, `ListNumbers`, `SameContent` 추가.
- (2차) WPF 하네스 기능 63개 통과(표: `/표`, 셀 입력·Tab, Enter 행 추가, 마지막 셀 Tab 행 추가, 빈 행 Backspace 삭제, 엑셀 붙여넣기 → 행·열 확장, Markdown, Undo / 이미지 표시·캡션),
  뷰어(BOM·CRLF 유지) 통과.
- (2차) 대용량(11,424줄, 블록 9,862개 — 표 300개 + 2,000행 표, 이미지 60개, 384KB, Debug): 열기 135~170ms(만든 행 27개, +5MB), 문단·셀 글자 입력 중앙 15ms,
  표 Enter 행 추가 중앙 47~63ms, 2,000행 표 끝 행 추가 중앙 47ms, 문단 Enter 중앙 14ms, 실행 취소 약 25ms, 120px 스크롤 중앙 7~11ms, 한 화면 이동 약 30ms,
  큰 폭 점프 20번 27~68ms(TSF 수정 전에는 가끔 0.4~5.4초), 저장 직렬화 약 9ms, 다시 읽어 블록 수 일치(빈 문단 제외).
- Windows 수동 확인 필요: 실제 앱에서 `설정 > 뷰어` 매핑 → `.md` 열기, 한글 IME로 입력·Enter, 마우스 끌어 옮기기, 블록 메뉴, 탭 제목 ` *`·닫기 확인.

## 변경 이력

- 2026-10-28: 요청 접수(Notion.exe 편집 기능을 C# 컨트롤로). Notion.exe 구조 분석, 블록 편집기·뷰어 구현, 단위 테스트·하네스 검증, 성능 문제 2건 수정.
- 2026-10-29: 대용량 요청(표·이미지가 든 1만 줄 문서의 편집·행 추가). 가상화·변경분 실행 취소·표/이미지 블록·표시 전용 행(TSF 멈춤 해결), 3,000블록 제한 제거.
- 2026-10-29: 표 가장자리 `+` 버튼(오른쪽 열 추가, 마지막 행 아래 행 추가). 하네스 검사 6개 추가(표시 위치, 클릭 → 행·열 추가, 마지막 행 이동, Undo).
- 2026-10-29: 페이지·데이터베이스·공유 등 Notion 플랫폼 기능은 범위에서 빼 이슈 #74로 분리(추후 검토).
- 2026-10-29: #78 저장 형식은 D안(사이드카)으로 결정·구현 — `BlockSidecar`(문서.md.meta.json), `EditorBlock.Attributes`, 첫 사용처로 블록 글자색·배경색(블록 메뉴 "색").
  단위 테스트 8개(왕복·빈 문단 순번·외부 삽입·같은 내용 구분·본문 변경·읽기 실패), 화면 밖 하네스 17개(저장·다시 열기·외부 편집·사이드카 삭제·.bak 보관) 통과.
  남은 일: Folderss 파일 작업에서 사이드카 함께 처리.
- 2026-10-29: Notion 수준 완성도 작업을 주제별 이슈로 분리 — 묶음 #79, 1단계 편집 조작 #75, 2단계 글자 서식 WYSIWYG #76, 3단계 블록·미디어 #77, 저장 형식 결정 #78.

# 블록 편집기 2단계: 줄 안 서식 WYSIWYG (#76)

- 상태: Ready for Verification
- 상태 전환일: 2026-11-01

## 요구사항

이슈 #76(블록 편집기 2단계: 글자 서식 WYSIWYG). 지금까지 줄 안 서식은 `**굵게**`처럼 Markdown 표식 그대로 보였다.

1. 줄 안 Markdown 파서·직렬화(굵게·기울임·코드·취소선·링크, 이스케이프, 중첩, 링크 안 서식, 코드 안 표식) + 왕복 불변 단위 테스트. 줄 안 수식 `$…$` 보존
2. 편집하지 않는 행(표 셀 포함)을 Inline으로 그려 표식 숨김, 가상화 행 재사용 비용 최소화
3. 편집 입력란에서도 서식이 보이게 — RichTextBox vs TextBox + 서식 오버레이를 측정으로 결정. 편집하는 행에만 입력란(RichTextBox도 TSF 등록 → 하나만)
4. 글자 선택 시 떠 있는 서식 도구막대(굵게·기울임·취소선·코드·링크)
5. `Ctrl+B`/`I`/`E`/`Shift+S`/`K`가 표식 삽입 대신 서식 적용·해제
6. 입력 중 Markdown 서식 단축(`**글**`, `` `코드` ``, `~~글~~`) + `Ctrl+Z`로 변환 되돌림, 한글 조합 중에는 변환 안 함
7. 링크: 표시, `Ctrl+클릭` 열기(#75 로직 재사용), 주소 편집 팝업
8. 붙여넣기: HTML·RTF 서식 → 줄 안 Markdown
9. 밑줄·글자 색·형광펜 — #78 사이드카 형식 확장(무리 없으면 구현, 크면 설계만)

완료 기준: 서식 문서를 열고·편집·저장해도 Markdown 왕복이 같음(단위 테스트), 1만 줄 글자 입력 중앙 ≤ 20ms, 표 행 추가 ≤ 70ms, 큰 폭 스크롤 ≤ 100ms,
실행 취소는 변경 구간만, 테마는 DynamicResource.

## 원인 분석 또는 설계

### 3번 결정: RichTextBox(편집기에 하나, 행으로 옮겨 붙임)

후보 비교:

- **TextBox + 서식 오버레이**: TextBox는 범위별 글꼴을 못 쓴다. 오버레이는 글자 폭이 TextBox와 같아야 커서·선택이 맞으므로 폭이 바뀌는 굵게는 그릴 수 없고(가짜 굵게 테두리로 흉내만),
  표식 글자도 숨길 수 없다(폭 0 글자 불가) — "표식 없이 서식으로"라는 이슈 목표를 편집 중에는 채우지 못한다.
- **RichTextBox**: 표식을 숨긴 진짜 서식. 대신 TSF에 등록되므로 화면에 하나만 두고 편집하는 행으로 옮겨 붙이며, 내용은 떼어 낸 상태에서 넣는다(#75에서 겪은 TSF 잠금 지연 회피).

측정(스크래치 `harness76` `decide` — 화면 밖 창, 1만 1천 줄·블록 9,802개 서식 문서, Debug, 서식 문단 20곳 평균/중앙):

| 항목 | (A) 지금 방식 TextBox | (B) RichBlockBox 하나 옮겨 붙임 |
|---|---|---|
| 편집 행 진입(입력란 준비 + 포커스) | 중앙 66.3ms / 62.5ms (행마다 TextBox 새로 만듦) | 중앙 9.7ms / 11.1ms (첫 생성·JIT 49~56ms 한 번) |
| 글자 입력(서식 문단) | 중앙 15.5ms (표식 보임) | 중앙 8.3ms / 6.7ms (표식 숨김, 매 입력 Markdown 직렬화 + 실행 취소 기록 포함) |
| 조합 단계(글자 바꾸기 ㅎ→하→한 흉내) | 중앙 29.2ms / 15.3ms | 중앙 16.5ms / 19.1ms |
| 굵은 글자 안 입력 | — | 중앙 17.9ms / 16.5ms |
| 서식 적용(굵게, 다시 그리기) | — | 중앙 28.8ms / 30.6ms (명령 — 입력 경로 아님) |

(두 번 실행. 두 번째 A 글자 입력은 포커스가 입력란에 없어 0.1ms로 무효.) **결정: B.** 진입이 오히려 빠르고(TextBox를 새로 만들지 않음) 입력도 기준 안이다.

한글 IME 조합: 하네스는 사용자 화면·입력기를 건드리지 않으려고 화면 밖 비활성 창에서 돌므로 실제 TSF 조합을 일으킬 수 없다. 그래서 (1) 조합이 하는 일 — 조합 중 글자를 문서에서
바꿔 끼우며 매번 TextChanged — 을 흉내 내 단계당 비용(위 표)을 쟀고, (2) 조합을 깨지 않는 규칙을 코드로 지킨다: 조합 중에는 내용을 다시 그리지 않고(`TextChanged`는 Markdown만 읽음),
입력 단축 변환은 닫는 표식(ASCII — 조합을 끝냄)에만, `IsComposing`(`PreviewTextInputStart`~`PreviewTextInput`)이면 건너뛴다. 조합 중 Enter는 기존과 같이 `Key.ImeProcessed` 뒤에 나눈다.
실제 IME 확인은 아래 검증 목록(실행 화면).

### 모델·왕복

- `EditorBlock.Text`는 그대로 줄 안 Markdown 원문. `Services/InlineMarkdown`(WPF 없음)이 조각(`InlineRun`) 평면 목록으로 읽고 쓴다.
  - 읽기: CommonMark 강조 규칙(구분자 왼쪽·오른쪽 붙음, 밑줄은 단어 안 금지, 3의 배수 규칙), GFM 취소선(`~`·`~~` 같은 길이), 코드(울타리 길이·앞뒤 공백), 링크 `[글](주소 "제목")`·`<주소>`,
    백슬래시 이스케이프. 이미지 `![..](..)`·HTML 태그·엔터티는 **원문 조각**(그대로 씀), `$...$`(Pandoc 규칙 — 기존 `BlockMathRenderer`와 같음)는 **수식 조각**. 링크 안 링크 없음.
  - 쓰기: 서식 경계 공백을 표식 밖으로 빼고(`**a **`는 닫히지 않음), 오래 이어지는 서식을 바깥에 연다. 이스케이프는 "없이 → 강조 표식 말고 → 전부" 순으로 다시 읽어 같은 서식이 되는 첫 결과.
    표식 모양(`**`/`__`, `*`/`_`, `~~`/`~`)·코드 울타리·링크 바깥 서식(`**[a](u)**`)은 조각에 기억해 정규형 s는 `Serialize(Parse(s)) == s`.
  - 고치지 않은 블록은 다시 쓰지 않는다(본문 그대로). 서식 입력란에서 고친 블록만 이 규칙으로 다시 써진다.
  - 한계: 단어 안에서 서식이 엇갈리는 경우(굵게 끝과 기울임 시작이 글자 사이에서 겹침)는 CommonMark로 나타낼 수 없어 글자는 보존하되 표식이 글자로 남을 수 있다.
- 위치는 모두 "보이는 글자" 기준(편집기에서 수식은 원문 `$…$`로 보임). 나누기·합치기·잘라 붙이기는 `Slice`·`Replace`·`Concat`·`SplitAt`.

### 표시(2번)

- 서식 블록 판별 `EditorBlock.HasInlineFormatting`(원문 블록·이미지 캡션·카드 제목·구분선·목차 제외, 표 행은 셀마다).
- `BlockInlineRenderer.PopulateDisplay`: 표식 글자가 없을 법하면(`IsPlain`) 예전처럼 `Text` 하나, 아니면 조각마다 Run(굵게·기울임·취소선·코드 글꼴+배경, 원문 조각은 흐린 글자),
  링크는 `Span`(+`LinkTag`, 밑줄·강조색) — Hyperlink는 누르기를 받아 편집 진입을 막으므로 쓰지 않는다. 맨 URL도 링크 모양(표시 전용).
- 비용: 파서 결과는 글자별 캐시(4,096개, 넘치면 비움), TextBlock.Tag에 (글자, 크기) 키를 두어 같은 내용이면 다시 그리지 않음(가상화 재사용·표 행 동기화).

### 편집(3·5·6·7번)

- `RichBlockBox`(RichTextBox)는 편집기에 하나. 서식 블록 행은 마우스를 올려도 입력란을 만들지 않고, 커서가 들어올 때 `AttachRich`로 붙인다(이전 행은 `DetachRich` — 표시 글자를 지금 본문으로 다시 그림).
  표시 글자를 누르면 붙인 뒤 누른 자리(`DisplayOffsetAt`)에 커서를 두고 같은 누르기를 입력란에 넘겨(`Mouse.MouseDown`) 끌어 선택이 이어진다. 행을 거두거나 다른 블록에 묶을 때는 뗀다.
  RichTextBox 글자 앞 안쪽 여백(5px)이 TextBox(2px)와 달라 `Margin -3`으로 표시 글자와 같은 자리·같은 줄 폭(하네스 확인: 첫 글자 (54,3) = (54,3), 행 높이 26 = 26).
- 공통 모양 `IBlockInput`(TextBox와 같은 이름의 멤버) — 원문 입력란은 `BlockTextBox`(TextBox), 서식 입력란은 보이는 글자 기준으로 `CaretIndex`·`Select`·`GetRectFromCharacterIndex`·
  줄 API(`GetLineStartPosition`)를 구현. 그래서 커서 이동·실행 취소 기록·`FocusRow`·찾기 강조는 거의 그대로이고, 본문을 다루는 곳만 Markdown 규칙(`ReadSplit`·`JoinText`·`TextWithout`·`InsertPlain`).
  확인만 하는 곳은 `IsEditorFocused`·`CurrentCaret`(서식 입력란을 다른 행에서 가져오지 않음).
- 서식 키(5번): 서식 블록은 `ToggleInlineFormat` — 선택이 모두 그 서식이면 끄고 아니면 켬, 조각 목록을 고쳐 `SetRuns`로 다시 그림(`ApplyRuns`가 앞뒤로 입력 묶음을 끊어 실행 취소 한 단계).
  선택이 없으면 WPF 선택 서식(다음 입력 글자). 표 셀(TextBox)은 예전처럼 표식으로 감쌈. 전에는 모든 블록에서 `WrapSelection`으로 표식을 넣었다.
  RichTextBox 기본 서식 명령(Ctrl+B/I/U, 정렬 Ctrl+E/L/R/J, 글자 크기, 목록, 문단 나누기)은 CommandBinding으로 끔.
- 입력 단축(6번): 닫는 표식(`* _ ~ \` $ )`)을 친 순간 `InlineMarkdown.TryInputRule` — `**글**`·`__글__`·`*글*`·`_글_`(앞이 공백·문장부호일 때만, `2*3*` 무시)·`~~글~~`·`` `코드` ``·`$수식$`·`[글](주소)`.
  입력한 글자는 이미 기록됐고 변환은 이벤트 뒤에 따로 한 단계 → `Ctrl+Z` 한 번이면 입력한 표식 글자(본문에는 `\*\*글\*\*`)로. 조합 중이면 하지 않음.
- 링크(7번): 표시 Span·편집 Hyperlink(강조색·밑줄). `Ctrl+클릭`은 #75의 `OnLinkPreviewMouseDown` → `LinkOpenRequested` → `BlockEditorViewer.OpenLink` 그대로(서식 입력란은 `RichLinkAt`,
  표시 글자는 `DisplayLinkAt`). `Ctrl+K`·도구막대 🔗는 주소 팝업(주소 입력란·적용·열기·제거, 클립보드에 URL이 있으면 미리 채움).
- 도구막대(4번): 선택이 생기고 마우스를 뗀 뒤 선택 위 Popup(B·I·S·`<>`·🔗, 켜진 서식은 눌린 색), 버튼은 포커스를 가져가지 않음. 스크롤·선택 해제·포커스 잃음에 닫힘.

### 붙여넣기(8번)

- 편집기 형식(`Folderss.InlineMarkdown` — 편집기에서 복사한 선택의 줄 안 Markdown) → HTML(`ClipboardMarkdown.FromHtml`: CF_HTML 조각, `b/strong/i/em/s/del/code/kbd/a`, Google 문서식
  `style="font-weight:700"` 등, 문단·`h1~h3`·목록·인용·`pre`) → RTF(WPF로 FlowDocument에 읽어 굵게·기울임·취소선·고정폭 글꼴=코드·하이퍼링크) → 글자(Markdown으로 읽음, 예전과 같음) 순.
- 여러 줄이면 블록으로(`PasteBlocks`), 한 줄이면 선택 자리에 조각으로. WPF 기본 서식 붙여넣기(글꼴·크기·표·그림)는 쓰지 않는다.

### 9번 밑줄·글자 색·형광펜 — 설계만(미구현)

Markdown으로 나타낼 수 없으므로 #78 규칙대로 사이드카에 둔다. 블록 속성 하나로 범위를 담는 설계:

- 키 `marks`(블록 속성, `EditorBlock.Attributes`). 값: `보이는글자해시|범위;범위…`, 범위는 `u:시작-끝`(밑줄), `c=색:시작-끝`(글자 색), `h=색:시작-끝`(형광펜). 위치는 편집기 "보이는 글자" 기준,
  색 이름은 블록 색과 같은 9가지(실제 색은 테마 밝기로). 예: `9f2c41aa|u:0-4;h=yellow:6-11`.
- 앞의 해시는 그 범위를 계산한 보이는 글자의 해시 — 본문이 범위를 고치지 않는 경로(찾아 바꾸기, 외부 편집 등)로 바뀌면 해시가 달라 범위를 버린다(틀린 글자에 칠하지 않음). 사이드카의
  블록 해시 규칙(본문이 바뀐 블록의 속성은 버림)과 같은 방향.
- 조각(`InlineRun`)에 `Underline`·`Color`·`Highlight`를 더하고 Markdown 직렬화는 무시, `ExtractMarks`/`ApplyMarks`로 속성 ↔ 조각. 편집 중에는 Run.Tag 원형이 색 이름을 들고 다니고,
  입력란이 바뀔 때마다 본문과 함께 `marks`를 갱신(실행 취소·수정 표시는 속성이 `Clone`에 들어가 있어 그대로).
- **미구현 이유**: 범위가 본문 위치에 묶여 있어 본문을 바꾸는 모든 편집 경로 — 나누기(앞·뒤 블록에 범위 나누기), 합치기(뒤 범위 밀기), 붙여넣기, `/` 명령 글자 지우기, 날짜 넣기,
  찾아 바꾸기, 블록 전환, 표 셀 — 에서 범위를 같이 옮겨야 하고, 각 경로의 왕복 테스트(`BlockSidecarTests`)와 도구막대 색 고르기 UI까지 더하면 2단계 나머지와 맞먹는다.
  이번에는 1~8을 먼저 완성하고, 해시 검사로 "틀리게 칠하지 않는" 안전장치를 포함한 위 설계를 남긴다.

## 구현 내용

- `Services/InlineMarkdown.cs`(신규, 순수): 파서·직렬화, `Slice`/`Replace`/`Concat`/`SplitAt`/`ApplyFormat`/`ApplyLink`/`LinkRange`/`HasFormat`, `TryInputRule`, `PlainToSource`/`SourceToPlain`.
- `Services/ClipboardMarkdown.cs`(신규, 순수): HTML → Markdown.
- `Controls/BlockTextInput.cs`(신규): `IBlockInput`, `BlockTextBox`, `RichBlockBox`.
- `Controls/BlockInlineRenderer.cs`(신규): 표시·편집 Inline 그리기, 편집기 문서 읽기, 표시 글자 위치 → 커서, 표시 링크.
- `Controls/BlockEditor.Inline.cs`(신규): 서식 입력란 옮겨 붙이기, 서식 키, 입력 단축, 붙여넣기·복사, 링크 팝업, 도구막대.
- `Controls/BlockEditor.cs`·`.Row.cs`·`.Find.cs`·`.Selection.cs`·`.Panel.cs`: 입력란 형식 `IBlockInput`, 본문 처리 경로(나누기·합치기·붙여넣기·`/`·날짜·찾기)의 Markdown 규칙, 행 표시·입력란 전환.
- `Models/EditorBlock.cs`: `HasInlineFormatting`.
- `Viewers/BlockEditorViewer.cs`: 단축키 안내 문구.

## 변경 파일

- `Folderss/Services/InlineMarkdown.cs`, `Folderss/Services/ClipboardMarkdown.cs` (신규)
- `Folderss/Controls/BlockTextInput.cs`, `Folderss/Controls/BlockInlineRenderer.cs`, `Folderss/Controls/BlockEditor.Inline.cs` (신규)
- `Folderss/Controls/BlockEditor.cs`, `BlockEditor.Row.cs`, `BlockEditor.Find.cs`, `BlockEditor.Selection.cs`, `BlockEditor.Panel.cs`
- `Folderss/Models/EditorBlock.cs`, `Folderss/Viewers/BlockEditorViewer.cs`
- `tests/Folderss.SearchTests/InlineMarkdownTests.cs` (신규), `tests/Folderss.SearchTests/Folderss.SearchTests.csproj` (소스 링크 2개)
- `README.md`, `docs/architecture.md`, `docs/keyboard-shortcuts.md`, 이 문서

## 검증

- 빌드: `dotnet build .\Folderss.sln -c Debug` 성공, `dotnet build tests/Folderss.SearchTests` 성공.
- 단위 테스트(이번에 돌린 범위): `--filter InlineMarkdownTests|BlockMarkdownTests|BlockFindTests` 180개 통과. 전체 테스트·하네스는 병합 뒤 일괄.
  - `InlineMarkdownTests`: 읽기 29가지(중첩·이스케이프·링크 안 서식·코드 안 표식·수식·금액 `$5 ~ $10`·HTML·엔터티·자동 링크·참조 링크), 정규형 왕복 33가지(편집용 읽기 포함),
    블록 문서 전체 왕복(제목·문단·목록·할 일·인용·표 셀), 임의 조각 목록 → 쓰기 → 다시 읽기 15가지, 경계 공백, 최소 이스케이프, 나누기·잇기·서식 적용·링크 적용/해제,
    입력 단축(변환 5·무시 5·코드 안·수식·링크), 위치 대응, HTML 붙여넣기 8가지, 맨 URL.
- 결정용 측정·최소 동작 확인(스크래치 `C:\Users\KYJ\AppData\Local\Temp\claude\D--git-cshap-Folderss\0e9a6dec-1b59-4430-8014-acdde638b328\scratchpad\harness76\`, `harness76.exe <폴더> decide|smoke|perf76`):
  - `smoke` 18개 통과(측정 전제 확인): 열자마자 왕복, 표시 표식 숨김, 서식 입력란 형식, 굵은 글자 안 입력, Enter 나누기(앞 `앞 **굵은x**`·뒤 `**글** 뒤`), Backspace 합치기·커서,
    Ctrl+B 해제·적용, Ctrl+I 겹침(`***…***`), Undo, 입력 단축 `**새**` → 굵게, Ctrl+Z → `\*\*새\*\*`, 표 셀 표시. 표시 ↔ 입력란 첫 글자 위치·행 높이 같음.
  - `perf76`(통합 뒤, Debug, 화면 밖): #75 측정 문서(1만 1,424줄·블록 9,862개) — 문단 글자 입력 중앙 16.1ms, 편집 행 진입(커서 이동) 31.1ms, ↓ 다음 블록 45.7ms, 표 셀 입력 15.3ms,
    표 행 추가(Enter) 62.2ms, 휠 7.2ms, 한 화면 38.2ms, 큰 폭 점프 61.8ms, 처음↔끝 37.3ms. 서식 문서(모든 문단·목록·표 셀에 서식, 블록 9,802개) — 글자 입력 16.8ms,
    편집 행 진입 30.6ms, 한 화면 49.2ms, 큰 폭 점프 91.3ms(기준 100ms 안이지만 여유가 적음 — 화면에 들어오는 표 셀마다 Inline을 만듦), 처음↔끝 37.9ms.
- **병합 뒤 하네스로 돌릴 검사 목록**(화면 밖 창 방식은 `block-editor-stage1-selection.md` 검증 절, 입력 흉내는 `harness76/Sim.cs`):
  1. 표시: 굵게·기울임·취소선·코드·링크·수식·원문 조각(이미지·HTML·엔터티)·맨 URL이 표식 없이 보이는지, 표 셀, 원문 블록(코드·원문)·캡션은 원문 그대로, 테마 바꾸면 색 따라감.
  2. 입력란 전환: 표시 글자 클릭 → 그 자리 커서, 클릭 후 끌어 선택, ↑↓·←→로 블록 넘나들기(가로 위치 유지), 화면 밖으로 스크롤했다 돌아오기, 입력란이 늘 하나인지(`RichBlockBox` 인스턴스 수).
  3. 도구막대: 마우스 끌어 선택·Shift+화살표 선택 후 표시, 버튼 클릭이 포커스·선택을 유지하며 서식 적용·해제, 켜진 서식 표시, 스크롤·Esc·포커스 잃음에 닫힘.
  4. 단축키: `Ctrl+B/I/E/Shift+S` 선택 적용·해제·선택 없을 때 다음 입력 글자, 표 셀은 표식 감싸기, `Ctrl+U/L/R/J/[/]`가 아무것도 바꾸지 않음, 각 1단계 실행 취소.
  5. 입력 단축 변환 + Undo: `**글**`·`__글__`·`*글*`·`_글_`·`~~글~~`·`` `코드` ``·`$x$`·`[글](주소)` 변환, `2*3*`·`snake_case_`·`** 공백**` 무시, `Ctrl+Z` 한 번에 표식 글자로, 다시 실행.
  6. IME 조합(실행 화면, 실제 한글 입력기): 서식 글자 안·뒤에서 한글 입력, 조합 중 `Enter`(나누기)·`Ctrl+B`, 조합 직후 `*` 입력, 조합이 끊기거나 글자가 겹치지 않는지.
  7. 링크 팝업: `Ctrl+K` 선택 → 주소 입력 → 적용, 링크 안에서 `Ctrl+K` → 주소 고치기·제거·열기, 선택 없이 주소 넣기, `Esc` 취소 후 선택 복원, 클립보드 URL 미리 채움.
  8. 붙여넣기: 브라우저·Word·Google 문서 HTML(굵게·기울임·링크·코드·제목·목록), RTF(워드패드), 편집기 안 복사→붙여넣기 서식 유지, 여러 줄 → 블록, 이미지·파일 붙여넣기(기존) 그대로.
  9. 저장 왕복: 서식 문서 열기 → 저장(바뀐 블록 없으면 파일 그대로), 편집 → 저장 → 다시 열기 → 같은 Markdown, 사이드카 영향 없음, 외부 변경 반영 뒤 서식 입력란 상태.
  10. 찾기·바꾸기: 서식 블록 안 결과 강조 위치(표식 뺀 글자 위치), 바꾸기 뒤 서식 유지, `Ctrl+클릭` 링크(서식 입력란·표시 글자).
  11. 성능(1만 줄): 글자 입력 중앙 ≤ 20ms, 표 행 추가 ≤ 70ms, 큰 폭 스크롤 ≤ 100ms(서식 많은 문서 포함), 편집 행 진입·블록 넘나들기.
  12. 기존 회귀: #75 하네스 selection·table·find·external(입력란 형식이 `IBlockInput`으로 바뀌어 하네스의 `TextBox` 캐스트는 `Editor`가 `RichBlockBox`인 서식 블록에서 고쳐야 함).

## 남은 일

- 9번 밑줄·글자 색·형광펜: 위 설계(`marks`)로 구현 — 편집 경로별 범위 이동, `BlockSidecarTests` 왕복, 도구막대 `U`·색 고르기, `Ctrl+U`.
- 표 셀 편집 입력란의 WYSIWYG(지금은 셀 편집 중 표식 보임): 셀도 서식 입력란을 옮겨 쓰려면 셀 키 처리(`HandleTableKey`)·셀 붙여넣기를 같은 방식으로.
- 서식이 많은 큰 문서의 큰 폭 스크롤(91ms) 여유 확보: 표 셀 표시 Inline 재사용 또는 간단 서식(굵게만)의 빠른 길.

## 변경 이력

- 2026-11-01: 1~8 구현, 3번 측정으로 RichTextBox(하나) 결정, 9번 설계만 (`Ready for Verification`)

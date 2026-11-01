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
9. 밑줄·글자 색·형광펜 — #78 사이드카 형식 확장(무리 없으면 구현, 크면 설계만) → 구현: [block-editor-stage2-text-marks.md](block-editor-stage2-text-marks.md)

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

### 병합 후 검증 결과 (2026-11-01)

최신 master(#75·#81) 위 병합본. 단위 테스트 전체 848개 중 통과 844·건너뜀 4·실패 0(이번에 3개 추가). 스크래치 하네스
`C:\Users\KYJ\AppData\Local\Temp\claude\D--git-cshap-Folderss\0e9a6dec-1b59-4430-8014-acdde638b328\scratchpad\harness76m\`(harness81 + harness76 합침, 저장소 밖,
`harness76m.exe <폴더> inline,fm76,smoke,selection,table,find,external,fm,switch,perf76,perfm`) — 화면 밖 창(-20000), 실제 마우스·키보드·화면은 건드리지 않음.
떠 있는 도구막대·링크 팝업은 모니터 안으로 옮겨 그려지므로 시험 중에는 투명(Opacity 0, 클릭 통과)으로 두었다. 입력란은 `IBlockInput` 기준 리플렉션 감싸개로 다룸.
`inline` 160개 중 157 통과(실패 3 = 아래 springload), `fm76` 18·`smoke` 18 통과.

| # | 검사 | 결과 |
|---|---|---|
| 1 | 표시: 제목·문단 표식 숨김, 굵게·기울임·취소선·코드·링크 Run 서식, 수식은 그려짐(`$` 없음), 원문 조각(이미지·`<b>`·`&amp;`) 원문 글자·흐린 색, 맨 URL 링크 모양, 표 셀(코드·링크), 코드 블록·이미지 캡션은 원문, Black → Light 사전 교체 시 링크·코드 배경이 새 테마 리소스, 서식 입력란도 같은 서식 | 통과 |
| 2 | 표시 글자 클릭 → 그 행에 서식 입력란·누른 자리 커서, 표시 글자 누른 채 끌기 → 글자 선택(블록 선택 아님), ↓↑ 가로 위치 유지(157px = 157px), 끝에서 →·처음에서 ←, 11개 행 차례로 클릭해도 `RichTextBox` 하나·같은 인스턴스, 멀리 스크롤 뒤 돌아와도 본문·포커스 유지 | 통과(**수정 후** — 아래 2) |
| 3 | 마우스 끌어 선택 → 끄는 중 없음·뗀 뒤 도구막대, B 클릭 → 굵게·포커스·선택 유지·B 눌림(I 안 눌림), 다시 B 해제, S·`<>`, Undo 두 번, `Esc` 닫힘, `Shift+→` 선택 → 표시, 선택 해제·스크롤·포커스 잃음 → 닫힘 | 통과 |
| 4 | `Ctrl+B/I/E/Shift+S` 선택 적용·해제·선택 유지, 각 Undo 한 단계, 표 셀(BlockTextBox) `Ctrl+B` 표식 감싸기·Undo, `Ctrl+U/L/R/J/[/]` → Markdown·버전·글자 크기·밑줄·정렬 그대로 | 통과 |
| 4 | 선택 없이 `Ctrl+B` → 다음 입력 글자 굵게 | **실패**(아래 남은 문제 1) |
| 5 | `**글**`·`__글__`·`*글*`·`_글_`·`~~글~~`·`` `코드` ``·`$x$`·`[글](주소)` 변환 → `Ctrl+Z` 한 번에 표식 글자(이스케이프) → `Ctrl+Y` 다시 / `2*3*`·`snake_case_`·`** 공백**`·`a~b~`·`` x` `` 무시 / 코드 안 `**a**` 변환 안 함 | 통과(코드 안은 **수정 후** — 아래 3) |
| 5 | 변환 뒤 이어 쓰는 글자는 보통 | **실패**(남은 문제 1 — 굵게가 이어짐) |
| 6 | 조합 흉내(`PreviewTextInputStart` → 글자 바꿔 끼우기 3번 → `PreviewTextInput`): `IsComposing`, 문서 다시 그리지 않음(Inline 그대로), 굵은 글자 안 본문 따라감 / 조합 중 닫는 `*` → 변환 안 함 / 조합 끝난 뒤 `*` → 변환 / 확정 직후 `Enter` → 굵은 글자 안 나누기(`앞 **굵한**`·`**게** 뒤`) | 통과(실제 입력기는 사람 확인) |
| 7 | `Ctrl+K` 선택 → 팝업·주소 입력란 포커스 → `Enter` 적용, 링크 안 `Ctrl+K` 주소 고치기·열기(`LinkOpenRequested`)·제거(글자 남김), 선택 없이 주소 넣기, `Esc` → 바뀌지 않음·선택 복원, 문서의 기존 링크 주소, 클립보드 URL 미리 채움(사용자 클립보드 글자는 저장 뒤 복원) | 통과 |
| 8 | 브라우저 CF_HTML(굵게·기울임·취소선·링크·코드), Word HTML, Google 문서 HTML, 여러 줄 HTML → 제목·목록·문단 블록, RTF(굵게·기울임·취소선·고정폭=코드), 편집기 안 복사 → 붙여넣기 서식 유지, Markdown 글자, 붙여넣기 Undo 한 단계 | 통과(Google·RTF 취소선 **수정 후** — 아래 4·5). 붙여넣기 처리기(`OnRichPasting`)를 직접 불러 확인(실제 클립보드·이미지 붙여넣기는 사람 확인) |
| 9 | 서식 문서(LF·CRLF) 열고 서식 입력란을 여러 블록에 붙였다 떼도 수정 아님 → `Ctrl+S` 바이트 동일 / `__굵게__` 안 입력 → 저장 차이는 그 줄뿐(`__굵X게__`), 사이드카 블록 항목 그대로, 다시 열기 같은 Markdown·색 속성 붙음 / 입력란이 붙은 블록을 외부에서 바꿈 → 확인창 1번 → 반영, 입력란에 낡은 내용 없음·화면 글자 = 새 본문·수정 아님·이어 입력 | 통과 |
| 10 | 서식 블록 찾기 결과 6개, 강조 위치 = 표식 뺀 글자 위치(2·7·12·17), 문단은 서식 입력란·표 셀은 원문 입력란(`**사과**` 위치 2), 바꾸기 하나씩 → 굵게·기울임·링크 유지, 모두 바꾸기 서식 유지 / 링크 `Ctrl+클릭`(표시 글자·서식 입력란)은 `external`에서 | 통과 |
| 11 | 성능(아래) | 일부 초과(아래) |
| 12 | #75·#81 회귀: `smoke` 18·`selection` 53·`table` 76·`find` 34·`external` 27·`fm` 53·`switch` 53 | 모두 통과(**수정 후** — 아래 1) |

front matter 제외(`fm76`, YAML 값 `a *b* c`·`snake_case_name`·`"**굵게 아님**"`·`` `x` ~~y~~ ``·`[t](u)`): `HasInlineFormatting(FrontMatter)` false, 값 글자·값 칸 표시가 표식 그대로(굵게 Run 없음),
front matter 행에 RichTextBox 없음, `원문 편집` → `Raw`·입력란 `BlockTextBox`·YAML 원문 그대로, 원문 편집 중 `Ctrl+B`·`_` 입력이 서식으로 바뀌지 않음, 벗어나면 속성 표·본문 그대로·수정 아님,
값 칸 편집기 TextBox, 원문 편집에 들어갔다 나온 뒤 `Ctrl+S` → LF·CRLF·BOM 바이트 동일 — **18개 모두 통과**. (`` ` ``·`[`로 시작하는 값은 YAML 예약 문자라 복잡 값 — 원문 표시, 기존 설계대로.)

고친 버그(커밋 9228041):

1. **링크 표시 글자 `Ctrl+클릭` 크래시** — `OnLinkPreviewMouseDown`의 `_findBar.IsAncestorOf(source)`가 Run(ContentElement)에서 `InvalidOperationException`으로 앱 종료(#76 전에는 표시 글자가 TextBlock 하나라 드러나지 않음). `ElementTree.IsWithin`으로 바꿈.
2. **↑↓←→·표시 글자 클릭으로 옮겨 온 행이 포커스 행으로 기록되지 않음** — 서식 입력란을 포커스를 가진 채 옮겨 붙이면 `GotKeyboardFocus`가 다시 오지 않아 `_focusedRow`가 이전 행으로 남았다.
   그 결과 멀리 스크롤하면 지금 편집 행이 거둬져 포커스를 잃고(입력이 창으로 감), 실행 취소·블록 연산의 커서 복원도 이전 행 기준이 된다. `AttachRich`에서 포커스 행을 맞춤.
3. **인라인 코드 안 입력이 저장 후 글자를 바꿈** — 코드 안에서 친 글자는 새 Run(울타리 정보 없음)이 되어 `` `코드 ` `` + ``` ``**a**`` ``` + `` `안` ``로 따로 쓰였고, 다시 읽으면 한 코드 `코드 ``**a**``안`이 됨.
   `InlineMarkdown.Merge`가 붙은 코드 조각을 울타리 모양만 무시하고 잇게 함(읽기로는 붙은 코드 조각이 생기지 않음). 단위 테스트 `Merge_JoinsAdjacentCodeRuns_FromEditing`.
4. **Google 문서 HTML 붙여넣기가 모두 굵게** — Google 문서는 조각 전체를 `<b style="font-weight:normal">`로 감싼다. style의 `font-weight:normal/400`·`font-style:normal`이 서식을 끄게 함(안쪽 span이 바깥 서식을 끄는 경우 포함). 테스트 2가지 추가.
5. **RTF 붙여넣기 취소선 누락** — WPF RTF 읽기가 `\strike`를 감싼 Span에 둬서 Run에서 보이지 않았다(TextDecorations는 상속 안 됨). 위 요소의 취소선을 물려받게 함.

성능(Debug, 1만 1천 줄·블록 9,8xx개, 화면 밖 창). **이번 측정 내내 다른 프로그램(3D 게임) 부하가 있어** 같은 측정이 실행마다 2~3배 흔들렸다(예: #76 이전 빌드 `harness81 perf` 문단 입력 17.7ms ↔ 6.0ms).
그래서 #76 이전 빌드(harness81, #81까지)와 이번 빌드를 번갈아 2번씩 쟀다(중앙값):

| 항목 | #76 이전 빌드(#75 문서) | 이번 빌드 #75 문서(`perf76`) | 서식 문서(`perf76`) | 서식+front matter+이미지(`perfm`) | 모든 표 셀 4칸에 서식(`perfm`) |
|---|---|---|---|---|---|
| 문단(서식) 글자 입력 | 17.7 / 6.0 | 16.7 / 3.2 | 16.7 / 10.0 | 10.5 / 15.4 (첫 실행 30.5) | 6.9 / 18.9 (첫 실행 28.0) |
| 굵은 글자 안 입력 | — | — | — | 첫 실행 60.9 | 첫 실행 52.6 |
| 편집 행 진입 | — | 14.8 / 14.7 | 16.3 / 19.7 | 23.5 / 27.1 | 16.4 / 10.6 |
| 표 셀 글자 입력 | 16.9 / 5.3 | 15.7 / 2.2 | 1.3 / 1.7 | 2.3 / 2.8 | 1.3 / 1.9 |
| 표 행 추가(Enter) | 74.3 / 40.2 | 60.7 / 15.7 | 11.1 / 11.4 | 14.2 / 17.4 | 11.2 / 12.0 |
| 큰 폭 스크롤(7%) | 50.1 / 83.0 | 56.4 / 74.0 | 90.4 / 91.6 | 94.1 / 147.2 | **147.3 / 150.2 (첫 실행 153.4)** |
| 처음↔끝 | 24.8 / 46.2 | 36.3 / 59.0 | 53.2 / 38.3 | 33.5 / 58.1 | 57.4 / 59.1 |
| 찾기 막대 연 채 입력 | 16.1 / 5.3 | — | — | 2.8 / 26.1 | 2.5 / 17.1 |

- 글자 입력·표 셀 입력·표 행 추가는 부하가 덜한 실행에서 기준(20ms·70ms) 안이고, 이전 빌드도 같은 폭으로 흔들려 #76 탓으로 보이지 않는다. 다만 "굵은 글자 안 입력"(첫 실행 50~60ms)은 부하 영향인지 따로 조용한 환경에서 다시 볼 것.
- **큰 폭 스크롤은 표 셀마다 서식이 있는 문서에서 3번 모두 147~153ms로 기준 100ms를 넘는다**(구현 측정 91ms는 행마다 셀 하나만 서식인 문서). 이전 빌드 같은 문서 종류는 50~83ms. 화면에 새로 들어오는 표 셀마다 Inline을 만드는 비용으로, 아래 남은 일 3.
- 보기 전환 `ToBlock`(줄 번호 경로)은 이전 빌드 73.5ms ↔ 이번 69.7ms로 차이 없음(첫 실행 91ms는 부하).
- 사람이 직접 볼 것: 실제 한글 IME(서식 글자 안·뒤 입력, 조합 중 `Enter`·`Ctrl+B`, 조합 직후 `*`, 글자 겹침·끊김), springload(선택 없이 `Ctrl+B` 후 입력·변환 뒤 이어 쓰기 — 남은 일 1),
  도구막대·링크 팝업 위치·모양(밝은·어두운 테마), 실제 브라우저·Word·Google 문서·워드패드에서 복사한 붙여넣기와 이미지·파일 붙여넣기, 실제 마우스 끌어 선택의 표시 ↔ 입력란 글자 위치 어긋남,
  조용한 환경에서 성능 재측정(특히 굵은 글자 안 입력·큰 폭 스크롤).

## 남은 일

- **(병합 검증) springload 서식이 서식 입력란에서 먹지 않음** — 선택 없이 `Ctrl+B`(I/E/Shift+S도 같은 경로) 뒤 입력한 글자가 보통으로 들어가고, 입력 단축 변환 뒤 `ClearSpringFormat`도 먹지 않아
  이어 쓰는 글자가 굵게 이어진다(`시작 **굵뒤**`). 하네스에서 일반 RichTextBox는 입력으로 만든 글자 뒤에서 springload가 되지만, **코드로 넣은 글자(`Document` 다시 만들기·`TextRange.Text`) 뒤에서는
  일반 RichTextBox도 똑같이 무시**된다(화살표 이동·LogicalDirection 바꾸기·빈 Run에 커서 두기로도 안 됨). 서식 입력란은 늘 `SetMarkdown`/`SetRuns`로 내용을 넣으므로 실제 앱에서도 재현될 가능성이 높다.
  WPF 내부(TextSelection springload 적용 조건) 원인을 확정하지 못해 고치지 않았다. 후보: springload 대신 편집기가 "다음 입력 서식"(켜기·끄기 마스크와 위치)을 들고 있다가 TextChanged에서 들어간 글자 범위에
  적용(조합 중이면 확정 뒤로 미룸). IME와 얽히므로 설계 판단 필요. **실제 앱에서 사람 확인 필요**.
- (병합 검증) 표식 글자 검색: `**`를 찾으면 원문의 표식 4개가 결과로 잡히지만 서식 입력란에는 표식이 없어 빈 선택이 강조된다. 서식 블록에서 표식 검색을 결과에서 뺄지(보이는 글자 기준 검색) 판단 필요.
- 9번 밑줄·글자 색·형광펜: 위 설계(`marks`)로 구현 — 편집 경로별 범위 이동, `BlockSidecarTests` 왕복, 도구막대 `U`·색 고르기, `Ctrl+U`.
- 표 셀 편집 입력란의 WYSIWYG(지금은 셀 편집 중 표식 보임): 셀도 서식 입력란을 옮겨 쓰려면 셀 키 처리(`HandleTableKey`)·셀 붙여넣기를 같은 방식으로.
- 서식이 많은 큰 문서의 큰 폭 스크롤: 행마다 셀 하나가 서식이면 91ms, **표 셀 모두 서식이면 147~153ms로 기준(100ms) 초과**(병합 검증, 부하 환경 3회 일관). 표 셀 표시 Inline 재사용 또는 간단 서식(굵게만)의 빠른 길.

## 변경 이력

- 2026-11-01: 1~8 구현, 3번 측정으로 RichTextBox(하나) 결정, 9번 설계만 (`Ready for Verification`)

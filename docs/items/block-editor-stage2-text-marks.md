# 블록 편집기 2단계: 밑줄·글자 색·형광펜 (#76 9번, #78)

- 상태: Ready for Verification
- 상태 전환일: 2026-11-01
- 관련: #76(2단계 글자 서식 WYSIWYG) 9번 항목, #78(사이드카 형식 — "줄 안 범위 속성은 형식 확장 필요"). 1~8번은 [block-editor-stage2-inline-format.md](block-editor-stage2-inline-format.md).

## 요구사항

1. 범위 속성 저장: 설계안(블록 속성 `marks` 하나, `해시|u:0-4;h=yellow:6-11`, 본문 해시가 다르면 범위 폐기)을 기본으로, 범위 기준을 명확히 하고, 본문이 바깥에서 바뀌어 맞지 않으면
   엉뚱한 글자에 칠하지 않게 버린다(상태 줄 알림은 사이드카 규칙과 같게). 테스트로 보장.
2. 표시: 표시용 TextBlock과 서식 입력란(`RichBlockBox`)에서 밑줄·글자 색·형광펜, 굵게 등 기존 서식과 겹쳐도 정상, 테마 전환 시 색 갱신.
3. 편집 경로에서 범위 유지: 입력·삭제, 나누기(Enter)·합치기(Backspace/Delete)·붙여넣기·`/` 명령·찾아 바꾸기·복제·이동·실행 취소/다시 실행. 범위 경계 입력 규칙을 정하고 문서화.
4. UI: 서식 도구막대에 밑줄(U)과 색 버튼(글자 색 9 + 배경색 9 + 기본값), `Ctrl+U`. 블록 단위 색(블록 메뉴 > 색)과 구분.
5. 복사·붙여넣기: 편집기 안 복사 → 붙여넣기 시 범위 유지(내부 클립보드 형식), HTML `<u>`·색·`<mark>` → 범위, 바깥으로는 Markdown 글자만.
6. 표 셀·코드·원문·수식·front matter 블록은 대상 외.

원칙: 가상화, 편집하는 행에만 입력란, 변경 구간만 실행 취소, 1만 줄 성능(글자 입력 중앙 ≤ 20ms, 큰 폭 스크롤 ≤ 100ms). Markdown 파일에는 아무 표식도 추가하지 않는다.

## 설계

### 저장 형식(설계안 그대로 채택)

- 키: 블록 속성 `marks`(`EditorBlock.AttrMarks` = `TextMarks.Attribute`). 사이드카(#78 version 1) 형식은 바꾸지 않았다 — 값 하나가 늘었을 뿐이라 예전 Folderss는 모르는 속성으로 그대로 보관한다.
- 값: `해시|범위;범위…`
  - `u:시작-끝` 밑줄, `c=색:시작-끝` 글자 색, `h=색:시작-끝` 배경색(형광펜). 색 이름은 블록 색과 같은 9가지(`gray brown orange yellow green blue purple pink red`), 실제 색은 테마 밝기로.
  - 예: `9f2c41aa|u:0-4;c=red:5-7;h=yellow:6-11`. 쓸 때는 종류 순(밑줄 → 글자 색 → 배경색), 같은 종류는 위치 순으로 정리하고 겹침·이웃은 합친다(같은 종류가 겹치면 나중 것이 이김).
- **위치 기준: 편집기의 "보이는 글자"** — `InlineMarkdown.ToPlain(본문)`(서식 표식을 뺀 글자, 줄 안 수식은 편집기처럼 원문 `$…$`, 이미지·HTML·엔터티 같은 원문 조각은 원문 글자 그대로), UTF-16 단위, `[시작, 끝)`.
  - Markdown 원문 위치를 쓰지 않은 이유: 서식을 켜고 끄거나 다시 쓸 때마다 표식·이스케이프(`\*`)가 늘고 줄어 원문 위치가 흔들린다. 보이는 글자는 서식 편집에 흔들리지 않고, #76 편집기의
    모든 위치(커서·선택·`Slice`·`SplitAt`)가 이미 이 기준이라 그대로 맞물린다.
- **해시**: 그 보이는 글자의 SHA-256 앞 8자(소문자). 범위를 쓸 때(`TextMarks.Format`)마다 다시 계산한다.
- **폐기 규칙**(`TextMarks.TryParse`가 false → 범위를 쓰지 않음):
  - 해시가 지금 보이는 글자와 다름, 형식 오류(모르는 종류, 대문자 색 이름, `끝 ≤ 시작`), 범위가 글자 밖.
  - 사이드카를 다시 붙일 때(`BlockSidecar.Apply`): 블록 해시(종류+본문)가 맞아 속성을 붙였어도 `marks`만 맞지 않으면 그 속성만 지우고 **붙이지 못한 속성 수에 더한다** → 기존 상태 줄 알림
    (`UnmatchedAttributes` — "본문이 바뀐 블록의 속성 n개를 버림")에 그대로 나온다. 다른 속성(`color` 등)은 붙는다.
  - 다른 도구에서 블록 본문을 고친 경우는 블록 해시부터 달라 그 블록의 속성 전체가 기존 규칙대로 떨어진다(`marks`도 같이). 해시는 그 밖의 경로(사이드카 손 편집, 앞으로 파서 규칙이 바뀌어
    보이는 글자가 달라짐 등)에서 틀린 글자에 칠하지 않게 하는 두 번째 안전장치다.
  - 저장 때 다시 읽을 블록(`BlockSidecar.Serialize`의 anchor — 저장한 Markdown을 다시 읽은 블록) 글자가 편집 중 글자와 다르면(앞 공백 정리 등) 그 차이만큼 범위를 옮겨 저장한다(`AnchoredAttributes`).
  - 대상 종류가 아니면(표 행·코드·원문·수식·front matter·이미지·카드) 저장하지 않는다(`Kind`가 그런 종류로 바뀌면 속성을 지움).

### 편집 중 범위 유지 — 본문 변경에 따른 자동 이동 + 블록 사이 이동만 따로

설계 문서는 "본문을 바꾸는 모든 경로에서 범위를 같이 옮겨야 해서 크다"고 봤다. 경로마다 고치는 대신 **`EditorBlock.Text` 설정자 한 곳**에서 처리했다:

- 블록에 `marks`가 있으면 이전·새 보이는 글자를 비교해(`TextMarks.Adjust` — 앞뒤 같은 글자를 뺀 한 구간을 바뀐 곳으로 봄) 범위를 옮긴다. 속성이 없는 블록은 비용이 없다(사전 조회 하나).
  이것으로 찾아 바꾸기(`BlockFind.ReplaceOne`), `/` 명령 글자 지우기, 날짜 넣기(`InsertPlain`), 입력 단축 블록 전환(`# ` 지우기), 제목 전환(줄바꿈 → 공백), 외부에서 오는 그 밖의 `Text` 대입이
  따로 고치지 않고 맞는다. 앞으로 생길 경로도 같다.
- 한 번에 여러 곳을 바꾸는 `BlockFind.ReplaceAll`만, 범위가 있는 블록에서 결과를 뒤에서부터 하나씩 바꾼다(한 구간 비교가 결과마다 정확하게).
- 서식 입력란 입력은 RichTextBox가 이미 정확한 결과를 갖고 있으므로 자동 이동을 쓰지 않고 조각의 범위를 그대로 넣는다(`SetBlockFromRich` → `TextMarks.SetText`).
- 글자가 **블록 사이로** 옮겨 가는 곳만 편집기가 따로 정한다:
  - 나누기(Enter, `SplitBlock`): 앞 블록은 자동 이동으로 잘리고, 뒤 블록은 커서 뒤 범위(`TailMarks`).
  - 합치기(맨 앞 Backspace `BackspaceAtStart`, 끝 Delete `MergeNext`): `JoinBlock` — 자동 이동은 "끝에서 이어짐" 규칙으로 뒤 글자에 앞 블록 끝 범위를 칠하므로, 앞 범위 + 뒤 블록 제 범위로 덮어쓴다.
  - 여러 줄 붙여넣기(`PasteBlocks`): 붙인 블록마다 클립보드 범위, 커서 뒤 글자는 마지막 블록 끝에 `JoinBlock`.
- 블록 복제·이동·끌어 옮기기·실행 취소/다시 실행: 속성이 `Clone`·`Duplicate`·`SameContent`에 들어 있어 그대로 따라간다(범위만 바꿔도 실행 취소 기록에 남음).

**새 글자의 범위(경계 규칙)** — `TextMarks.Edit`:

| 경우 | 새 글자가 따르는 범위 | 예(밑줄 `abc`) |
|---|---|---|
| 범위 안에 입력 | 그 범위(늘어남) | `aXbc` 모두 밑줄 |
| 범위 **끝**에서 입력 | 바로 앞 글자 → **이어짐**(Notion과 같음) | `abcX` 모두 밑줄 |
| 범위 **시작**에서 입력(줄 처음 아님) | 바로 앞 글자(범위 밖) → 따라가지 않음 | `Xabc`의 X는 밑줄 아님 |
| 줄 처음에서 입력 | 바로 뒤 글자(첫 글자) | 줄 첫 글자가 밑줄이면 X도 밑줄 |
| 글자를 바꿈(선택 후 입력·찾아 바꾸기, 길이 다름) | 바꾼 첫 글자 | 칠한 낱말을 바꾸면 새 낱말도 칠함 |
| 같은 길이로 바꿈(줄바꿈 → 공백 등) | 글자마다 그대로 | |
| 지우기 | 범위가 줄고, 다 지우면 사라짐 | |
| 붙여넣기 | 붙인 글자 제 범위만(앞 글자 따르지 않음) | |
| **대기 서식이 있을 때**(선택 없이 `Ctrl+U`·색 뒤 입력) | 대기 서식에 정한 종류는 그 값이 **우선**, 정하지 않은 종류는 위 규칙 | 끝에서 `Ctrl+U`(끄기) 뒤 `X` → X 밑줄 아님 |

줄 처음 규칙은 서식 입력란(RichTextBox)이 줄 처음 입력을 첫 Run에 넣는 동작과 맞춘 것이다. 입력란 안 입력은 WPF가 커서가 붙은 Run에 글자를 넣고
그 Run의 원형(Tag)이 범위를 들고 있어 같은 규칙이 된다. 커서는 조각 경계에서 앞 조각 쪽에 붙인다(`RichBlockBox.PlaceCaret` — 병합 검증에서 `CaretPosition`을 설정하면
WPF가 뒤 조각 처음으로 옮겨 "시작에서 입력"이 뒤 범위를 따라가던 것을 고침). 선택한 글자를 덮어 입력하면 WPF는 지운 조각의 글자 장식·바탕을 이어 주지 않으므로
편집기가 바뀐 첫 글자 모양(서식·링크·범위)을 대기 서식으로 두었다가 들어온 글자에 칠한다(`SetPendingForReplace` → `PendingInlineFormat.Like`, 찾아 바꾸기와 같은 규칙).
대기 서식(선택 없이 서식 키·색)의 구현과 우선순위는 [block-editor-stage2-inline-format.md](block-editor-stage2-inline-format.md) "대기 서식" 절.

### 조각·표시·입력란

- `InlineRun`에 `Underline`·`TextColor`·`Highlight`(+`HasMarks`)를 더했다. `SameStyle`은 비교하므로 범위가 다른 조각은 합치지 않고, `Serialize`는 범위를 빼고 범위 경계에서 나뉜 조각(코드 조각 포함)을
  다시 이어서 쓴다(`JoinSplitPieces`) → **범위가 있어도 Markdown은 범위가 없을 때와 같다**(테스트). `Equivalent`(Canon)도 범위를 보지 않는다. 서식 적용(`ApplyFormat`)·`Slice`·`Replace`·입력 단축은 조각 사본을 다루므로 범위가 따라간다.
- `TextMarks.ApplyToRuns`(범위 → 조각, 경계에서 나눔. 표시용 수식 조각은 나누지 않고 첫 글자의 범위)·`FromRuns`(조각 → 범위).
- 표시(`BlockInlineRenderer.PopulateDisplay(…, marks)`): 범위가 있으면 서식 없는 글자도 빠른 길(Text 하나) 대신 조각으로 그린다. 같은 그림 판단 키(`DisplayKey`)에 `marks` 값을 넣었다.
  밑줄 + 취소선은 장식 둘을 함께(`UnderlineAndStrike`), 코드 바탕 위에 형광펜이 이긴다, 링크 글자에 글자 색을 칠하면 그 색(밑줄은 링크 밑줄).
- 입력란(`RichBlockBox.SetMarkdown(md, marks)`): 같은 그리기. 읽기(`ReadRun`)는 밑줄 = Run·상위 요소의 글자 장식(링크 Hyperlink의 밑줄 제외), 색 = Run.Tag 원형.
  입력으로 새로 생긴 Run(원형 없음 — 선택 서식 뒤 입력, 입력란 안 끌어 놓기)은 앞 조각에서 물려받은 지역 색 값을 팔레트(두 테마 모두)와 맞춰 읽는다(`BlockEditor.TextMarkNameOf`).
- **테마**: 색은 편집기 리소스 `BlockMarkText.<색>`·`BlockMarkBack.<색>`(`UpdateTextMarkBrushes` — 생성·Loaded·`RefreshTheme`에서 테마 밝기가 바뀌었을 때만 18개 붓을 바꿈)를 DynamicResource로 참조한다.
  테마를 바꿔도 행을 다시 그리지 않는다(기존 블록 색은 `SyncRealized`로 다시 칠함). 도구막대 견본은 편집기 리소스 밖(Popup)이라 열 때 직접 칠한다.
- **성능**: 범위가 없는 블록은 표시·입력·동기화 모두 기존 경로 그대로(속성 조회 하나, 조각 훑기 `HasMarks`). 범위가 있는 블록만 표시할 때 해시(SHA-256, 문단 글자) 한 번(같은 키면 다시 그리지 않음),
  입력마다 범위 읽기·해시 한 번. 측정은 아래 검증 11번(하네스).

### UI

- 서식 도구막대: `B I U S <> 🔗 A▾`. `U` = 밑줄(`Ctrl+U`, 켜진 상태 표시). `A▾`를 누르면 도구막대 아래에 색 고르기가 펼쳐진다:
  `글자 색` 줄(기본 + 9가지 색 글자 "A"), `배경색` 줄(기본 + 9가지 바탕), `글자 색·배경색 지우기`. 선택이 모두 그 색이면 견본 테두리가 강조색. 버튼은 포커스·선택을 가져가지 않고 한 번에 실행 취소 한 단계.
- 블록 단위 색과 구분: 도구막대 색은 **선택한 글자만**(`A▾` 도움말에 "블록 전체 색은 블록 메뉴 > 색"), 블록 메뉴 > `색`은 **블록 전체**(도움말에 반대 안내). 저장 키도 다름(`marks` vs `color`/`background`).
- `Ctrl+U`: 선택이 모두 밑줄이면 해제, 아니면 적용. 선택이 없으면 다음 입력 글자(편집기 대기 서식 — 취소선 등 다른 대기 서식과 함께). 표 셀·코드에서는 아무것도 하지 않음.
  글자 색은 도구막대가 선택이 있을 때만 뜨므로 UI로는 선택한 글자에만 칠한다(편집기 명령 `SetTextMarkColor`는 선택이 없으면 대기 서식으로 다음 입력 글자에 칠함).

### 복사·붙여넣기

- 편집기 형식 `Folderss.TextMarks`: 첫 줄 = 함께 담은 Markdown 글자의 해시(다른 프로그램이 글자만 바꿔 넣었으면 쓰지 않음), 다음 줄부터 Markdown 블록마다 `marks` 값(없으면 빈 줄).
  - 서식 입력란 복사·잘라내기(`OnRichCopying`): 선택 글자 하나 → 값 하나(붙일 때 다시 읽는 보이는 글자 기준). 블록 선택 복사(`CopyBlocks`): 블록마다.
  - 붙여넣기: 한 줄은 넣는 조각에 범위를 입혀 선택 자리에(`OnRichPasting`), 여러 줄은 블록마다(`ApplyPastedMarks` — 블록 수가 다르면 붙이지 않고, 블록마다 해시 검사로 맞지 않으면 버림).
    블록 선택 붙여넣기(`PasteAfterSelection`)·원문 입력란 여러 줄 붙여넣기(`OnEditorPasting`)도 같다.
  - 바깥 프로그램에는 Markdown 글자(`UnicodeText`)만 간다(밑줄·색은 사라짐 — Markdown 파일과 같은 원칙).
- HTML 붙여넣기(`ClipboardMarkdown.FromHtml(html, lineMarks)`): `u`·`ins`·`text-decoration: underline` → 밑줄, `color`·`<font color>` → 글자 색, `mark`(노랑)·`background-color`/`background` → 배경색.
  CSS 색(`#rgb`·`#rrggbb`·`rgb()`·`rgba()`·이름 일부)을 색상(hue)으로 가장 가까운 9가지에(채도 낮으면 회색, 어두운 주황·빨강은 갈색). 검정·흰색에 가까운 색(Google 문서가 모든 글자에 붙이는
  `color:#000000`)·투명·거의 투명은 버린다. Markdown 블록(빈 줄로 나뉜 줄)마다 값 하나, 코드 블록은 없음.
- RTF 붙여넣기는 밑줄·색을 옮기지 않는다(HTML이 함께 오는 Word·브라우저는 HTML이 먼저라 대부분 해당 없음 — 남은 일).

### 대상 외와 이유

- **표 셀**: 셀 편집 입력란은 원문 TextBox(#76에서도 셀은 표식이 보이는 원문 편집)라 범위를 보여 주고 고칠 수 없고, 범위가 셀마다 따로 있어야 해 형식(`셀번호:…`)과 셀 추가·삭제·이동·
  정렬(`BlockTable`) 규칙까지 늘어난다. 셀 WYSIWYG(#76 남은 일)와 함께 하는 것이 맞다. 표 셀 표시에는 `marks`를 넘기지 않는다.
- 코드·원문·수식·front matter·이미지 캡션·카드 제목: 원문 그대로 보이는 블록(`TextMarks.IsTarget` = `HasInlineFormatting && != TableRow`).

## 구현 내용

- `Services/TextMarks.cs`(신규, 순수): 값 형식·해시·검증(`Format`·`TryParse`·`Hash`), 글자별 모양으로 계산하는 편집 연산(`Adjust`·`Edit`·`Slice`·`Concat`·`Replace`·`Apply`·`Covers`·`Normalize`),
  블록(`Get`·`Set`·`SetText`·`DropIfInvalid`·`OnTextChanging`), 조각(`ApplyToRuns`·`FromRuns`·`Strip`).
- `Models/EditorBlock.cs`: `Text` 설정자(범위 자동 이동)·`Kind` 설정자(대상 아닌 종류면 `marks` 지움)·`SetTextOnly`·`AttrMarks`.
- `Services/InlineMarkdown.cs`: `InlineRun.Underline`·`TextColor`·`Highlight`·`HasMarks`, `SameStyle`, `Serialize`(범위 빼고 나뉜 조각 잇기), `Canon`.
- `Services/BlockSidecar.cs`: 다시 붙일 때 `marks` 검증·폐기 수 세기, 저장 때 다시 읽을 글자 기준으로 맞추기, 빈 속성 항목 쓰지 않기.
- `Services/BlockFind.cs`: `ReplaceAll` — 범위가 있는 블록은 뒤에서부터 하나씩.
- `Services/ClipboardMarkdown.cs`: HTML 밑줄·색·형광펜 → 줄마다 범위 값, `NearestColorName`.
- `Controls/BlockInlineRenderer.cs`: 표시·입력란 그리기(장식 합치기·색 리소스 참조), 표시 키에 `marks`, 읽기(밑줄·색).
- `Controls/BlockTextInput.cs`: `RichBlockBox.SetMarkdown(md, marks)`.
- `Controls/BlockEditor.Inline.cs`: `SetBlockFromRich`·`RichMarksDiffer`·`TailMarks`·`JoinBlock`·`ApplyPastedMarks`·클립보드 형식, `ToggleUnderline`·`SetTextMarkColor`·`ClearTextMarkColors`·`ApplySpringDecoration`, 도구막대 `U`·`A▾`·색 고르기, 붙여넣기·복사.
- `Controls/BlockEditor.cs`: `Ctrl+U`, 입력 동기화, 나누기·합치기·여러 줄 붙여넣기·블록 복사/붙여넣기의 범위, 색 리소스 갱신.
- `Controls/BlockEditor.Row.cs`: 입력란 붙이기·동기화(범위가 달라도 다시 채움)·표시에 `marks`.
- `Controls/BlockEditor.Color.cs`: 색 리소스(`UpdateTextMarkBrushes`), `TextMarkNameOf`, 블록 메뉴 "색" 도움말.

## 변경 파일

- `Folderss/Services/TextMarks.cs`(신규), `Folderss/Services/InlineMarkdown.cs`, `BlockSidecar.cs`, `BlockFind.cs`, `ClipboardMarkdown.cs`
- `Folderss/Models/EditorBlock.cs`
- `Folderss/Controls/BlockEditor.cs`, `BlockEditor.Inline.cs`, `BlockEditor.Row.cs`, `BlockEditor.Color.cs`, `BlockInlineRenderer.cs`, `BlockTextInput.cs`
- `Folderss/Viewers/BlockEditorViewer.cs`(단축키 안내 문구에 `Ctrl+U`·`A▾`)
- `tests/Folderss.SearchTests/TextMarksTests.cs`(신규), `BlockSidecarTests.cs`, `Folderss.SearchTests.csproj`(소스 링크 `TextMarks.cs`)
- `README.md`, `docs/architecture.md`, `docs/keyboard-shortcuts.md`, 이 문서, `docs/items/block-editor-stage2-inline-format.md`(9번 줄에 링크만)

## 검증

- 빌드: `dotnet build .\Folderss.sln -c Debug` 성공(바꾼 파일에 새 경고 없음), `dotnet build tests/Folderss.SearchTests` 성공.
- 단위 테스트(이번에 돌린 범위): `--filter TextMarksTests|BlockSidecarTests|InlineMarkdownTests|BlockFindTests|BlockMarkdownTests` 258개 통과. 전체 테스트·하네스는 병합 뒤 일괄.
  - `TextMarksTests`: 값 형식(종류 순·해시 8자)·왕복, 해시 불일치·형식 오류 8가지 거부, 범위 이동 12가지(안·끝 이어짐·시작·앞뒤·줄 처음·지우기·전체 지우기·다른 길이 바꾸기·같은 길이 바꾸기),
    나누기 → 잇기 같음, 합치기에서 뒤 글자가 앞 끝 범위를 따르지 않음, 붙여넣기 제 범위만, 적용·해제·덮어쓰기·확인, 범위가 든 조각의 Markdown이 범위 없을 때와 같음(굵게·코드 조각 나뉨·링크·수식·원문·HTML),
    굵게와 겹침·굵게 해제 뒤 범위 유지, 표시 수식 조각 통째로, 블록 본문 경로(입력 앞·끝 이어짐, `/` 명령 지우기, 찾아 모두 바꾸기 두 가지, 맞지 않는 값은 본문이 바뀔 때 지움),
    나누기(앞 자동 잘림·뒤 조각), 합치기, 복제·사본·`SameContent`, 제목 전환 유지·코드 전환 지움, `SetText`, HTML 밑줄·글자 색·`mark`·굵게+배경색, 여러 블록 HTML의 줄마다 값, 기본색 무시, 색 이름 대응 15가지.
  - `BlockSidecarTests`(추가 5): `marks` 왕복(같은 블록, Markdown은 속성 없을 때와 같음), 해시 불일치 → 버리고 1개로 셈(다른 속성 유지), 외부 편집 블록만 떨어짐, 저장 때 다시 읽는 글자 기준으로 맞춤, 대상 아닌 종류는 저장 안 함.
- **병합 뒤 하네스로 돌릴 검사 목록**(화면 밖 창 방식·입력 흉내는 `block-editor-stage1-selection.md` 검증 절·스크래치 `harness76/Sim.cs`):
  1. 표시: 밑줄·글자 색·형광펜이 표시 TextBlock과 서식 입력란에서 같은 자리에 보이는지, 굵게·기울임·취소선(밑줄+취소선 함께)·코드(형광펜이 코드 바탕을 덮음)·링크(글자 색 우선)와 겹침, 줄 안 수식 위 색.
     입력란 ↔ 표시 전환(클릭으로 진입·다른 행으로 이동) 때 모양이 바뀌지 않는지, 서식 없는 글자(빠른 길 대상)에 범위만 있는 문단.
  2. 테마 전환(밝은 ↔ 어두운): 화면의 표시 글자·붙어 있는 입력란·도구막대 견본 색이 다시 그리기 없이 바뀌는지, 블록 색(블록 메뉴)도 그대로 바뀌는지.
  3. 도구막대: 글자 선택 → `U` 켜짐 표시·적용·해제, `A▾` 펼침 → 글자 색·배경색 적용, 선택이 모두 그 색이면 견본 강조, `기본`(그 종류만 지움)·`글자 색·배경색 지우기`(밑줄은 남김), 버튼이 포커스·선택을 유지, 스크롤·Esc·선택 해제에 닫힘.
  4. `Ctrl+U`: 선택 적용·해제, 선택 없이 → 다음 입력 글자 밑줄(취소선 선택 서식과 함께), 표 셀·코드에서 아무 일 없음, RichTextBox 기본 밑줄 명령이 끼어들지 않음, 각 1단계 실행 취소.
  5. 입력 경계: 범위 안·끝(이어짐)·시작(안 따라감)·줄 처음 입력, 선택 후 입력(바꾼 첫 글자 범위), 지우기, 한글 IME 조합(범위 끝에서 조합 — 글자가 범위에 들어가는지, 조합 중 색이 깜박이지 않는지).
  6. 블록 사이: 범위 중간에서 Enter(앞·뒤 블록 범위), 맨 앞 Backspace·끝 Delete 합치기(뒤 글자가 앞 끝 범위에 물들지 않음), 여러 줄 붙여넣기의 커서 뒤 글자, `/` 명령 실행(글자 지우기 뒤 범위), 날짜 넣기,
     입력 단축 `# `(블록 전환 후 범위 위치), 제목·코드 전환(코드면 지워짐, 실행 취소로 돌아옴).
  7. 찾아 바꾸기: 하나 바꾸기·모두 바꾸기(한 블록 여러 결과)에서 다른 낱말 범위가 제자리, 칠한 낱말을 바꾸면 새 낱말에 칠해짐, 서식(굵게) 안 결과.
  8. 복제(`Ctrl+D`)·블록 이동(`Ctrl+Shift+↑/↓`·끌어 옮기기)·실행 취소/다시 실행(범위만 바꾼 단계 포함, 입력 묶음과 따로)·저장 표시(범위만 바꿔도 `*`).
  9. 복사·붙여넣기: 서식 입력란 선택 복사 → 다른 블록에 붙이기(범위 유지), 잘라내기, 블록 선택 `Ctrl+C` → `Ctrl+V`(블록마다 범위), 메모장에 붙이면 Markdown 글자만,
     브라우저·Word·Google 문서에서 밑줄·글자 색·형광펜 복사 → 가장 가까운 색, 검정 글자·흰 바탕이 범위로 생기지 않는지, 입력란 안 끌어 놓기(색이 지역 값으로 읽히는지).
  10. 저장 왕복: 범위 문서 저장 → Markdown에 아무 표식 없음(`git diff`로 확인), 사이드카 `marks` 값, 다시 열기 → 같은 범위. 다른 도구로 그 블록 글자 고침 → 범위 떨어지고 상태 줄 알림,
      사이드카 `marks` 해시만 손으로 바꿈 → 칠하지 않고 알림, 외부 변경 반영(다시 읽기) 뒤 입력란 범위.
  11. 성능(1만 줄, Debug): 범위 없는 문서에서 #76 수치가 그대로인지(글자 입력 중앙 ≤ 20ms, 큰 폭 스크롤 ≤ 100ms, 서식 많은 문서 91ms 대비), 모든 문단에 범위가 든 문서에서
      글자 입력 중앙·편집 행 진입·한 화면·큰 폭 점프(범위 렌더링 비용 — 해시와 조각 나누기), 테마 전환 시간.
  12. 기존 회귀: #76 smoke 18개(굵게 안 입력·나누기·합치기·Ctrl+B/I·입력 단축·Undo), #75 selection·table·find·external, #78 사이드카 17개.

### 병합 뒤 하네스 검증 결과 (2026-11-01)

스크래치 `C:\Users\KYJ\AppData\Local\Temp\claude\D--git-cshap-Folderss\0e9a6dec-1b59-4430-8014-acdde638b328\scratchpad\harness76m\`의 `marks9`(`Marks9.cs`)·`perfmarks`, 화면 밖 창,
실제 마우스·키보드 없음(입력 흉내·도구막대는 투명). 처음 돌린 결과 범위 시작 입력·선택 덮어 입력·입력란 진입만으로 수정됨 3건이 실패해 고쳤다(원인은
[block-editor-stage2-inline-format.md](block-editor-stage2-inline-format.md) 2차 재검증 "9번 하네스에서 찾아 고친 것"). 고친 뒤 73개 모두 통과(3번 반복).

| # | 검사 | 결과 |
|---|---|---|
| 1 | 표시: 밑줄, 굵게+밑줄, 취소선+밑줄(장식 둘), 코드 위 형광펜(코드 바탕을 덮음), 링크 글자 색 우선, 줄 안 수식 위 글자 색, 범위만 있는 보통 문단, 서식 입력란 같은 모양, 입력란에서 나와도 본문·범위 그대로·수정 아님 | 통과(수정 아님은 **수정 후**) |
| 2 | 어두운 → 밝은 테마: 표시 글자 색·배경색이 같은 Run에서 바뀜(다시 그리지 않음), 블록 색, 서식 입력란, 도구막대 견본 | 통과(`RefreshTheme`+그리기 8~13ms, 작은 문서) |
| 3 | 도구막대 `U` 적용·해제·켜짐 표시·포커스·선택 유지, `A▾` 펼침, 글자 색·배경색, 견본 강조, `기본`(그 종류만), `글자 색·배경색 지우기`(밑줄 남음), Esc·선택 해제에 닫힘 | 통과 |
| 4 | `Ctrl+U` 선택 적용·해제·각 Undo 한 단계, 선택 없이 `Ctrl+U`+`Ctrl+Shift+S` → 다음 입력 밑줄+취소선(대기 서식), RichTextBox 기본 밑줄 끼어듦 없음, 표 셀·코드에서 아무 일 없음(Markdown·버전) | 통과 |
| 5 | 범위 안·끝(이어짐)·시작(따라가지 않음)·줄 처음, 선택 후 입력(바꾼 첫 글자), 지우기, 대기 서식 우선(끝에서 `Ctrl+U` 끄기 → 안 이어짐, `Ctrl+B` → 굵게+밑줄 이어짐), 범위 끝 한글 조합(조합 중 다시 그리지 않음·확정 뒤 범위 안) | 통과(시작·선택 후 입력은 **수정 후**) |
| 6 | 범위 중간 Enter(앞·뒤), 맨 앞 Backspace·끝 Delete 합치기(뒤 글자 제 색), 여러 줄 붙여넣기 커서 뒤 글자, `/` 명령(할 일 — 글자 있는 블록이라 아래 새 블록, 지운 뒤 범위 제자리), 입력 단축 `# `, 코드 전환 → 지움, 실행 취소 → 돌아옴 | 통과 |
| 7 | 하나 바꾸기 → 다른 범위 제자리, 모두 바꾸기(한 블록 여러 결과·굵게 안) → 서식·범위 유지, 칠한 낱말은 새 낱말에 칠함 | 통과(보이는 글자 기준 찾기로) |
| 8 | `Ctrl+D` 복제, `Ctrl+Shift+↓` 이동, 범위만 바꿔도 수정 표시, 입력 Undo와 범위 단계 따로, Redo | 통과 |
| 9 | 서식 입력란 복사(바깥 형식은 Markdown 글자, 편집기 형식에 범위) → 다른 블록 붙이기(범위 유지), 글자만 붙이면 범위 없음, 블록 복사 자료 → 블록마다 범위, HTML `u`·색·`mark` → 범위, 검정 글자·흰 바탕 없음 | 통과(블록 선택 `Ctrl+C`/`Ctrl+V`·잘라내기·메모장은 실제 클립보드를 쓰지 않으려고 같은 자료 경로로만 확인) |
| 10 | 저장 → Markdown 원본과 같음(표식 없음), 사이드카 `marks` 값, 다시 열기 같은 범위, 사이드카 해시만 바꿈 → 칠하지 않음·"속성 1개를 붙이지 못함" 알림, 다른 도구로 블록 글자 고침 → 범위 떨어짐·알림, 외부 변경 반영 뒤 범위 | 통과 |
| 11 | 성능: 범위 없는 문서는 #76 2차 재검증 표(모든 셀 서식 큰 폭 스크롤 75~91ms 등). 범위 블록 500개 문서(`perfmarks`, 2회): 글자 입력 중앙 17.7·18.9ms, 편집 행 진입 20.5·19.2ms, 한 화면 50.7·51.4ms, 큰 폭 점프 55.6·55.0ms, 테마 전환(1만 줄) 99.5·115.6ms | 기준 안(3D 게임 부하) |
| 12 | 회귀: `smoke` 18, `selection` 53, `table` 76, `find` 34, `external` 27, `fm` 53, `fm76` 18, `switch` 53, `inline` 161, `BlockSidecarTests` 포함 단위 테스트 951 통과 | 통과 |

사람이 볼 것: 실제 한글 입력기로 범위 끝·선택 덮어 조합, 도구막대 색 고르기 모양(두 테마), 실제 브라우저·Word·Google 문서의 밑줄·색 붙여넣기, 블록 선택 `Ctrl+C`/`Ctrl+V`·메모장 붙여넣기(실제 클립보드), 입력란 안 끌어 놓기.

## 남은 일·한계

- 입력 단축의 `$수식$`·`[글](주소)` 변환은 바뀐 글자의 범위를 잃는다(새 조각으로 바꿈). `**글**` 등 강조 변환은 유지.
- 서식 입력란 밖 경로의 범위 이동은 앞뒤 같은 글자 비교라, 같은 글자가 이어진 곳(`aa`에 `a` 넣기)의 경계에서는 어느 쪽에 넣었는지 알 수 없어 한 글자 차이로 경계가 달라질 수 있다(입력란 입력은 해당 없음).
- RTF 붙여넣기의 밑줄·색, Markdown 뷰어(미리보기)에서 범위 표시, 표 셀 범위, 최근 쓴 색 다시 칠하기 단축키.
- Folderss 파일 작업에서 사이드카를 함께 옮기기(#78 남은 일 — `marks`도 같이 떨어짐).

## 변경 이력

- 2026-11-01: 병합 뒤 하네스 12항목 수행, 경계 시작 입력·선택 덮어 입력·입력란 진입만으로 수정됨 3건 수정.
- 2026-11-01: 구현(`Ready for Verification`). 이 worktree는 처음에 master(`489e234`)에서 시작해 `feature/block-editor-stage2`(`4463dbe`, #76 1~8 병합)로 맞춘 뒤 작업했다.

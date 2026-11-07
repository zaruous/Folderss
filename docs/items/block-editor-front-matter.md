# 블록 편집기: front matter 속성 표 (#81)

- 상태: Done — master 머지 확인 (이전: Ready for Verification)
- 상태 전환일: 2026-11-07

## 요구사항

이슈 #81(블록 편집기 ↔ Markdown 뷰어 전환과 기능 격차) 격차 표의 **"front matter 표시"** 항목. 지금은 문서 맨 앞 YAML front matter(`---` … `---`)가 원문 블록으로 보인다 →
Notion 페이지 속성처럼 **속성 표(키 | 값)** 로 보이게 한다. 같은 이슈의 "뷰어 전환"(ViewerHost 툴바·설정 창·세션)은 다른 브랜치에서 따로 구현한다(이 작업은 건드리지 않음).

- Markdown 왕복 불변: 편집하지 않았으면 열고 저장해도 front matter 원문이 바이트 단위로 같다(단위 테스트로 보장)
- 표시: 키·값 행. 단순 스칼라(`key: value`)·목록(`tags: [a, b]`, `- a`)은 보기 좋게, 해석할 수 없는 복잡한 YAML(중첩 맵, 여러 줄 블록 스칼라 등)은 원문 표시로 대체
- 편집(1차): 값 클릭 편집, 속성 추가·삭제. 편집한 키만 다시 쓰고 나머지 줄(주석·순서·따옴표)은 보존. 그 밖은 "원문 편집으로 전환"
- 원칙: 가상화(보이는 블록만 행), 편집하는 칸에만 TextBox(입력기 TSF 잠금 회피), 변경 구간만 실행 취소 기록, 테마 리소스는 DynamicResource, YAML 라이브러리 추가 없음

## 설계

- **블록 종류 `BlockKind.FrontMatter`(enum 끝에 추가)**: `Text`에 구분 줄까지 원문을 담는다. `BlockMarkdown.Parse`가 문서 첫 줄 `---`부터 닫는 `---`/`...`까지를 이 종류로 읽고
  (이전에는 `Raw`), `Serialize`는 `Raw`와 같은 경로로 원문을 그대로 쓴다 → 바이트 단위 왕복은 구조로 보장된다. 줄바꿈은 블록 안에서 `\n`이고 뷰어가 읽은 CRLF/LF로 되돌려 저장한다(기존 규칙).
  `Raw` 그대로 두고 "맨 앞 원문 블록이면 표로" 판정하는 방법도 봤지만, 행 모양·키 처리·높이 추정이 위치에 따라 바뀌어 더 복잡해서 별도 종류로 했다.
- **순수 로직 `Services/FrontMatter`(WPF 없음, 테스트 프로젝트에 소스 링크)**: 필요한 범위만 읽는 작은 파서.
  - 최상위 줄이 `키: 값`(따옴표 키 포함, 콜론 뒤 공백 또는 줄 끝)이어야 표로 본다. 최상위 목록·들여쓴 최상위·키 없는 줄이 있으면 `IsTable = false` → 전체 원문 표시.
  - 키 다음의 들여쓴 줄·0열 `- ` 줄(사이 빈 줄·주석 포함)이 그 속성의 줄. 값 종류:
    - `Scalar`: 한 줄 값(일반·`"…"`·`'…'`, 빈 값). 큰따옴표 이스케이프는 다시 쓸 때 같은 글자로 돌아오는 것(`\n \t \" \\ \/ 공백`)만 읽는다.
    - `List`: `[a, "b, c"]`(중첩 괄호 없음) 또는 같은 들여쓰기의 `- 스칼라` 줄들(항목 주석·사이 빈 줄 없음).
    - `Complex`: 그 밖 전부 — 중첩 맵, `|`·`>` 블록 스칼라, 앵커 `&`·별칭 `*`·태그 `!`, 흐름 맵 `{}`, 중첩 흐름 목록, 여러 줄 따옴표/일반 값, `- key: v` 맵 목록 등. 키 뒤 원문을 그대로 보여 주고 편집하지 않는다.
  - 편집은 **그 속성의 줄 범위만** 바꾼 본문을 돌려준다(`SetValue`·`SetItems`·`RenameKey`·`AddEntry`·`RemoveEntry`). 속성 순번과 키가 둘 다 맞을 때만 적용(본문이 바뀐 사이 다른 속성을 고치지 않게).
    - 값: 원래 따옴표 형식 유지(큰따옴표 → 이스케이프, 작은따옴표 → `''`), 일반 값은 YAML이 다르게 읽을 글자(`: `, ` #`, 앞뒤 공백, 지시 문자로 시작 등)면 큰따옴표. 키·콜론·콜론 뒤 공백·줄 끝 주석은 원문 그대로.
    - 목록: `[a, b]`는 한 줄로, `- a` 목록은 원래 들여쓰기로 다시 쓰고, 원래 있던 항목은 원문 토큰(따옴표 포함) 그대로. 블록 목록이 비면 `key: []`.
    - 이름 바꾸기: 키 부분만 바꾼다(빈 이름·다른 속성과 같은 이름은 거부). 추가: 닫는 줄 바로 앞에 `이름:`. 삭제: 그 속성 줄만(위 주석·빈 줄은 남김).
  - 타입 의미(`true`·`123`)는 해석하지 않는다 — 글자 그대로 보여 주고 고친 글자 그대로 쓴다(원래 일반 값이면 일반 값으로).
- **화면 `BlockEditor.FrontMatter.cs`(BlockRow를 partial로 나눔)**: Toc처럼 본문 입력란이 없는 블록. 머리글 `속성` + `+ 속성 추가`·`원문 편집`, 속성마다 [이름 150px | 값 | `×`(마우스를 올렸을 때만)] 행.
  목록 값은 칩, 빈 값은 `비어 있음`, 복잡한 값은 코드 글꼴 원문. 색은 모두 `SetResourceReference`(테마 키 `SurfaceBackground`·`BorderBrush`·`SecondaryText`·`PrimaryText`·`AccentBrush`·`ControlBackground`·`ControlHoverBrush`·`RowHoverBrush`·`DisabledTextBrush`).
  - 표는 그 블록의 행이 만들어질 때만(가상화) 그리고, 본문(`_fmRendered`)이 바뀔 때만 다시 만든다. 글자 입력 경로(다른 블록)에는 비용이 없다.
  - 칸을 누르면 그 칸에만 TextBox를 만든다(내용을 넣은 뒤 붙임, `Tag = null`이라 편집기의 본문 입력란 키 처리·끌어 선택·찾기 대상이 아님). `Enter` 적용·`Esc` 취소(둘 다 뒤에 블록 선택),
    `Tab`/`Shift+Tab` 적용 후 다음·이전 칸(복잡한 값 건너뜀), 포커스를 잃으면 적용, 한글 조합 중 `Enter`는 조합이 끝난 뒤 적용. 저장(`GetMarkdown`) 전에도 열린 칸을 적용한다.
  - 적용은 `ApplyFrontMatterEdit` → `RecordChange(row)`(그 블록 하나만 기록하는 입력 경로) — 앞뒤 입력과 한 단계로 묶이지 않게 `_typingRow`를 비운다. 편집 한 번 = 실행 취소 한 단계.
    실행 취소로 본문이 바뀌면 행이 다시 묶이며 표를 다시 그린다(열린 칸은 닫힘).
  - `원문 편집`(또는 복잡한 값 클릭): 블록 종류를 `Raw`로 바꾸고 원문 입력란에 커서. 입력란을 벗어나면(블록이 아직 맨 앞이고 front matter 모양이면, 창이 비활성이 된 경우는 제외) 다시 `FrontMatter`.
    보기 전환이라 저장할 변경이 아니므로 기록하지 않고 수정 표시도 생기지 않는다 — 기록 기준 사본(`_committed`)의 종류만 같이 바꿔 다음 기록이 이 블록을 바뀐 구간으로 잡지 않게 했다.
    원문에서 고친 글자는 일반 입력과 같이 기록된다.
  - 맨 앞 고정: 손잡이(`+`·`⋮⋮`)를 보이지 않고, 화살표 이동·포커스 대상에서 빼며(`IsEditable`), `Ctrl+Shift+↑↓`·끌어 놓기로 front matter를 옮기거나 그 위에 블록을 놓지 못하게 했다(`MovesFrontMatter`).
    다른 블록 뒤에 붙여넣은 `---` 블록은 front matter가 될 수 없으므로 `Raw`로 둔다. 행을 클릭하면 블록 선택(삭제는 `Delete`, `Ctrl+Z`로 복구).
  - 높이 추정: 줄 수 × 26 + 머리글(실제 높이는 화면에 들어올 때 측정).
- **사이드카 호환**: `BlockSidecar.Hash`가 종류 이름을 해시에 넣으므로, `FrontMatter`는 예전처럼 `Raw`로 해시한다(`HashKind`) — 이전에 저장한 사이드카의 문서 해시·앞 블록 해시가 그대로 맞는다.
- **찾기**: front matter는 찾기 대상에서 뺐다(`BlockFind.IsSearchable`). 결과를 입력란 선택으로 강조하는 방식이라 표 칸에 결과를 보여 줄 수 없기 때문 — 아래 "남은 것".

## 구현 내용

- `Folderss/Services/FrontMatter.cs` (새 파일): 위 파서·줄 단위 편집, 목록 입력 변환(`SplitListInput`·`FormatListForEdit`), `UniqueKey`.
- `Folderss/Controls/BlockEditor.FrontMatter.cs` (새 partial): 속성 표 그리기·칸 편집·추가/삭제·원문 편집 전환, `ApplyFrontMatterEdit`·`SetFrontMatterView`·`EditFrontMatterSource`·`ScheduleFrontMatterTable`·`PastedFrontMatterAsRaw`.
- `Folderss/Models/EditorBlock.cs`: `BlockKind.FrontMatter`.
- `Folderss/Services/BlockMarkdown.cs`: front matter를 `FrontMatter`로 읽고 `Raw`와 같이 쓴다.
- `Folderss/Services/BlockSidecar.cs`: `HashKind`(FrontMatter → Raw).
- `Folderss/Services/BlockFind.cs`: 찾기 대상에서 제외.
- `Folderss/Controls/BlockEditor.cs`: `IsEditable` 제외, `MovesFrontMatter`(블록 이동 막기), `GetMarkdown` 전 열린 칸 적용, 입력란 blur 때 속성 표 복귀 예약, 붙여넣기 두 곳에서 `Raw`로.
- `Folderss/Controls/BlockEditor.Row.cs`: `BlockRow`를 partial로, 손잡이 숨김·클릭 시 블록 선택·입력란 만들지 않음·표 패널 표시·Toc와 같은 상자 모양·원문 편집 중 안내 툴팁.
- `Folderss/Controls/BlockEditor.Panel.cs`: 높이 추정. `Folderss/Controls/BlockEditor.Selection.cs`: 끌어 놓기 막기.

## 변경 파일

- `Folderss/Services/FrontMatter.cs`, `Folderss/Controls/BlockEditor.FrontMatter.cs` (새 파일)
- `Folderss/Models/EditorBlock.cs`, `Folderss/Services/BlockMarkdown.cs`, `Folderss/Services/BlockSidecar.cs`, `Folderss/Services/BlockFind.cs`
- `Folderss/Controls/BlockEditor.cs`, `Folderss/Controls/BlockEditor.Row.cs`, `Folderss/Controls/BlockEditor.Panel.cs`, `Folderss/Controls/BlockEditor.Selection.cs`
- `tests/Folderss.SearchTests/FrontMatterTests.cs` (새 파일), `tests/Folderss.SearchTests/BlockMarkdownTests.cs`(정규형 문서 첫 블록 `Raw` → `FrontMatter`), `tests/Folderss.SearchTests/Folderss.SearchTests.csproj`(`FrontMatter.cs` 소스 링크)
- `README.md`, `docs/architecture.md`, 이 문서

## 검증

- [x] `dotnet build .\Folderss.sln -c Debug` 오류 0(Exit 0), `dotnet build tests/Folderss.SearchTests` 오류 0. 새 파일에서 나온 경고 없음.
- [x] 단위 테스트 `dotnet test tests/Folderss.SearchTests --filter "FrontMatterTests|BlockMarkdownTests|BlockSidecarTests"` — 병합 후 전체 735개 중 통과 731·건너뜀 4·실패 0
  - 해석: 스칼라(일반·큰따옴표+주석·작은따옴표 `''`·빈 값), 흐름 목록(따옴표 속 쉼표), 블록 목록(들여쓴·0열), 줄 번호·줄 수
  - 복잡 YAML 대체 12가지(중첩 맵, `|`·`>-`, 맵 목록, 앵커·별칭·태그, 흐름 맵, 중첩 흐름 목록, 여러 줄 따옴표, `\u` 이스케이프, 사이 빈 줄 목록) → `Complex`·원문 표시·편집 거부, 앞뒤 속성은 정상
  - 표가 아닌 front matter 4가지(최상위 목록·들여쓴 최상위·키 없는 줄·`key:value`) → 전체 원문, 추가 거부. `LooksLikeFrontMatter` 6가지
  - 왕복 불변 7가지(주석·빈 줄·줄 끝 공백·`...` 닫기·복잡 YAML·최상위 목록·탭) → 블록 본문 = 원문, `Serialize` = 원문 문서. CRLF 문서 왕복. 문서 중간 `---`는 front matter 아님
  - 편집 시 다른 줄 보존: 값(그 줄만·따옴표 형식·주석 유지), 일반 값 따옴표 필요 판단 9가지(다시 읽으면 같은 값), 빈 값↔값, 줄바꿈 → 공백, 키·순번 불일치 거부,
    목록(흐름: 원래 토큰 유지·새 항목 따옴표 / 블록: 들여쓰기·토큰 유지, 늘리기·비우기 `[]`), 이름 바꾸기(키만·따옴표 필요한 이름·빈/중복 거부), 추가(닫는 줄 앞·`...`·빈 front matter·중복 거부),
    삭제(그 줄만·위 주석 유지·복잡 값 여러 줄), 목록 입력 왕복, 문서 전체에서 편집한 줄만 바뀜
  - 사이드카 해시 호환(FrontMatter = Raw)
- [x] WPF 하네스(스크래치 하네스, 병합 후 일괄, 결과는 아래 "병합 후 검증 결과") — 돌릴 검사:
  1. **표시**: 테스트 문서(스칼라·따옴표·주석·빈 값·흐름/블록 목록·중첩 맵·`|` 블록 스칼라) 열기 → 첫 행이 `FrontMatter`, 속성 행 수·키·값 글자·칩 수, 복잡 값은 원문 글자, 본문 입력란(TextBox) 0개.
     최상위 목록 front matter → 안내 문구 + 원문. 밝은·어두운 테마 전환 뒤 색이 테마 리소스를 따름.
  2. **값 편집**: 값 칸 클릭 → 그 칸에만 TextBox(행 안 TextBox 1개), 글자 바꾸고 `Enter` → 블록 본문에서 그 줄만 바뀜, TextBox 사라지고 front matter 블록 선택, `IsModified` true.
     `Esc` → 본문 그대로. `Tab` → 다음 칸(복잡 값 건너뜀). 다른 블록 클릭(포커스 잃음) → 적용. 한글 조합 중 `Enter` → 조합 글자까지 적용.
  3. **목록 편집**: 칩 칸 클릭 → `a, "b, c", d` 입력란, 항목 바꾸고 `Enter` → 원래 형식(흐름/블록·들여쓰기) 유지.
  4. **이름 바꾸기·추가·삭제**: 이름 클릭 → 바꾸기(중복 이름이면 원래대로), `+ 속성 추가` → 끝에 `속성:`(있으면 `속성2`) + 이름 칸 편집 상태, `×` → 그 속성 줄만 삭제.
  5. **Undo/Redo**: 2~4 각각 `Ctrl+Z` 한 번에 이전 본문(표 다시 그림), `Ctrl+Y` 다시. 연속 편집 두 번이 한 단계로 묶이지 않음. 기록 항목의 범위가 블록 0 하나(`Start = 0`, `AfterCount = 1`).
     칸 편집 중 `Ctrl+Z` → 열린 칸 닫힘·앱 오류 없음.
  6. **원문 편집 전환**: `원문 편집` → 블록 종류 `Raw`·원문 입력란 포커스, 수정 표시 없음 → 원문에서 한 줄 고치고 다른 블록 클릭 → 다시 속성 표(고친 값 반영), `Ctrl+Z` 한 번에 원문 입력 되돌림.
     닫는 `---`를 지운 채 벗어나면 원문 블록으로 남음. 복잡 값 클릭 → 원문 편집.
  7. **저장 왕복**: 편집 없이 `Ctrl+S` → 디스크 바이트 = 원본(CRLF·LF·BOM 문서 각각). 값 하나 편집 후 저장 → 원본과 차이가 그 줄뿐(front matter 구간). 칸이 열린 채 `Ctrl+S` → 입력 중 값까지 저장.
     사이드카가 있는 front matter 문서(이전 버전에서 저장) → 열었을 때 속성이 그대로 붙음(`UnmatchedAttributes` 0).
  8. **맨 앞 고정**: front matter에 손잡이 안 보임, 첫 블록에서 `↑`·`Ctrl+Home` → front matter로 커서가 가지 않음(선택만), 첫 본문 블록 `Ctrl+Shift+↑` → 안 옮겨짐, 다른 블록을 끌어 맨 위에 놓기 → 안 됨,
     본문 중간에 `---\na: 1\n---` 붙여넣기 → 속성 표가 아니라 원문 블록.
  9. **성능**(1만 줄 문서 맨 앞에 속성 20개 front matter): 열기 시간 기존과 같은 수준, 문단 글자 입력 **중앙 ≤ 20ms**(front matter 행이 화면에 있을 때·없을 때), 값 편집 적용 1회 시간 기록,
     맨 위↔끝 큰 폭 스크롤 ≤ 100ms.

### 병합 후 검증 결과 (2026-11-01)

스크래치 WPF 하네스(화면 밖 창, InputManager로 마우스·키 주입 — 방식은 `block-editor-viewer-switch.md` 검증 절). 시험 문서 front matter: 일반·큰따옴표+주석·작은따옴표 `''`·빈 값·흐름 목록(따옴표 속 쉼표)·블록 목록·중첩 맵·`|` 블록 스칼라·`draft: true` 9개 속성.
결과 53개 **모두 통과**, 기능 수정 없음. 성능은 1만 줄 문서로 따로 측정(아래).

| # | 검사 | 결과 |
|---|---|---|
| 1 | 첫 블록 `FrontMatter`, 속성 9개·키 순서, 값 글자(`첫 문서`/`따옴표 값`/`it's`/`비어 있음`), 칩 수 3·2, 복잡 값 원문 글자, 본문 TextBox 0개 / Black → Light 사전 교체 → 키 글자 색이 새 `SecondaryText`(#9CA3AF → #626B78) / 최상위 목록 front matter → 안내 문구 + 원문 | 통과 |
| 2 | 값 칸 클릭 → 그 칸에만 TextBox 1개·포커스, `Enter` → 그 줄만 바뀜·TextBox 사라짐·블록 0 선택·수정됨 / `Esc` → 그대로 / `Tab`: 값 → 다음 이름, 이름 → (복잡 값 건너뜀) 다음 이름, `Shift+Tab` 거꾸로 / 다른 블록 클릭 → 적용(작은따옴표 형식 유지) / 한글 조합 중 `Enter`(ImeProcessed) → 조합 글자까지 적용 | 통과 |
| 3 | 흐름 목록 입력란 `a, "b, c", d` → 고친 뒤 `[a, "b, c", e]`(원래 토큰·형식) / 블록 목록 → `  - ` 들여쓰기 유지 | 통과 |
| 4 | 이름 바꾸기 → 키만, 중복 이름 → 원래대로 / `+ 속성 추가` → 닫는 줄 앞 `속성:`·이름 칸 편집, 다시 → `속성2` / 마우스를 올리면 `×` 보임, `×` → 그 줄만 삭제 | 통과 |
| 5 | 삭제 `Ctrl+Z` 한 번 → 이전 본문(표 다시 그림), `Ctrl+Y` 다시 / 연속 편집 두 번 → `Ctrl+Z`마다 한 단계 / 기록 범위 `Start 0, AfterCount 1` / 칸 편집 중 `Ctrl+Z` → 칸 닫힘·앱 오류 없음 | 통과 |
| 6 | `원문 편집` → `Raw`·원문 입력란 포커스·수정 아님 → 값 고치고 다른 블록 클릭 → 속성 표(고친 값), `Ctrl+Z` 한 번에 원문 입력 되돌림 / 닫는 `---` 지운 채 벗어남 → 원문 블록 / 복잡 값 클릭 → 원문 편집 | 통과 |
| 7 | 편집 없이 `Ctrl+S` → 디스크 바이트 = 원본(LF·CRLF·BOM 각각) / 값 하나 편집 후 저장 → 원본과 차이가 그 줄뿐(CRLF 유지) / 칸이 열린 채 `Ctrl+S` → 입력 중 값까지 저장 / 이전 버전(front matter = `Raw`) 사이드카 → `UnmatchedAttributes` 0·색 그대로 | 통과 |
| 8 | 손잡이 안 보임 / 첫 본문 블록 `↑` → front matter에 커서 안 감, `Ctrl+Home` → front matter 블록 선택만(입력란 없음) / `Ctrl+Shift+↑` → 안 옮겨짐 / 다른 블록을 맨 위(0)에 끌어 놓기(`MoveDraggedBlocks`) → 안 됨 / 본문 중간에 `---\na: 1\n---` 붙여넣기(`PasteBlocks`, 클립보드 안 씀) → 원문 블록 | 통과 |
| 9 | 성능(아래) | 통과(환경 영향 1건 — 아래) |

- 성능(Debug, 1만 줄 + 속성 20개 front matter: 11,455줄·블록 9,863개·표 행 8,301·이미지 60) — 조용한 환경 단독 실행: 열기 140ms, **문단 글자 입력 중앙 3.0ms**, 표 셀 입력 1.5ms, 표 행 추가(Enter) 12.7ms, 큰 표 끝 행 추가 14.6ms,
  큰 폭 스크롤(7%씩) 47.6ms·처음↔끝 25.0ms, 찾기 막대 연 채 입력 등 #75 항목 모두 기준 안.
- 같은 날 다른 프로그램(3D 게임) 부하가 생긴 뒤에는 측정값이 한 프레임(약 16ms) 단위로 올라갔다. 그래서 **front matter 없는 #75 문서와 번갈아 A/B로 2번씩** 쟀다(중앙값, 없음 → 있음):
  문단 글자 입력 16.0·16.5 → 15.8·16.4ms, 표 행 추가 61.0·63.4 → 63.5·60.9ms, 큰 표 끝 행 추가 75.9·75.9 → 77.1·77.3ms, 큰 폭 7% 49.1·49.9 → 46.6·49.0ms, 처음↔끝 22.9·28.6 → 25.8·27.3ms.
  **front matter로 인한 차이는 측정 오차 안**이다. 큰 표 끝 행 추가가 이 환경에서 70ms를 넘은 것은 front matter가 없는 기준 문서도 같아서 환경 영향으로 판단했다(조용한 환경 14.6ms).
- front matter 전용: front matter 행이 화면에 있을 때 문단 글자 입력 중앙 15.3ms(부하 환경 — 같은 환경 기준 측정과 같음), 값 편집 적용(Enter) 1회 중앙 32.8ms, front matter 문서 맨 위↔끝 25.9ms.
- 관찰(고치지 않음): 칸을 편집하는 중 `Ctrl+Z`는 칸에 친 글자를 버리고 칸을 닫은 뒤 **문서의 직전 편집 한 단계를 되돌린다**(입력란 자체의 실행 취소가 아님). 검사 기준(칸 닫힘·오류 없음)은 맞지만,
  칸 안 글자만 되돌리기를 기대하는 사용자도 있을 수 있어 설계 판단으로 남긴다.
- 하네스로 확인하지 못한 것(사람이 볼 것): 실제 한국어 입력기(IME)로 조합 중 `Enter`(하네스는 ImeProcessed 키와 조합 완료 글자를 순서대로 넣어 흉내), 속성 표 모양·칸 편집 상자 모양(밝은·어두운 테마), 실제 끌어 놓기 화면 표시(놓을 자리 표시선).

### #76 병합 후 회귀 (2026-11-01)

- 하네스 `harness76m`(`C:\Users\KYJ\AppData\Local\Temp\claude\D--git-cshap-Folderss\0e9a6dec-1b59-4430-8014-acdde638b328\scratchpad\harness76m\`) `fm` **53개 모두 통과**.
- #76 병합 조정(front matter를 `HasInlineFormatting`에서 제외) 확인 `fm76` **18개 모두 통과**: YAML 값 `a *b* c`·`snake_case_name`·`"**굵게 아님**"`이 서식으로 해석되지 않고 글자 그대로(굵게 Run 없음),
  front matter 행에 RichTextBox 없음, `원문 편집` 입력란은 `BlockTextBox`·YAML 원문 그대로, 원문 편집 중 `Ctrl+B`·`_` 입력이 서식이 되지 않음, 값 칸 편집기 TextBox,
  원문 편집에 들어갔다 나온 뒤 무편집 `Ctrl+S` → LF·CRLF·BOM 바이트 동일. `` ` ``·`[`로 시작하는 값은 YAML 예약 문자라 복잡 값(원문 표시) — 기존 설계대로.
- 성능: 1만 줄 + 속성 20개 front matter + 서식 + 이미지 문서(`perfm`) — `block-editor-stage2-inline-format.md` 성능 표.

## 남은 것 / 범위 밖

- 찾기·바꾸기에 front matter 값 포함(지금은 대상 아님 — 표 칸에 결과 강조가 필요).
- 값 타입 편집기(날짜 선택, 체크박스, 선택 목록 등 Notion 속성 종류)·속성 순서 끌어 바꾸기·복잡 값(중첩 맵) 표 편집 — 지금은 원문 편집으로.
- 블록 단위 복사(`Ctrl+C`)에 front matter가 들어가면 원문 그대로 복사된다(붙여넣으면 원문 블록).

## 변경 이력

- 2026-11-01: 구현, 앱·테스트 프로젝트 빌드 확인. 테스트·하네스 실행은 #81 뷰어 전환 작업과 병합한 뒤 일괄 진행.
- 2026-11-01: 병합 후 하네스 53개·단위 테스트 735개(실패 0)·1만 줄 성능(A/B) 확인, 기능 수정 없음 (Ready for Verification, 사용자 확인 대기)

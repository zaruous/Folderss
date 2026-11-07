# 블록 편집기 1단계: 선택·이동 (#75)

- 상태: Done — master 머지 확인 (이전: Ready for Verification)
- 상태 전환일: 2026-11-07

## 요구사항

이슈 #75(블록 편집기 1단계: 편집 조작) 중 "선택·이동" 항목. 같은 이슈의 "찾기·외부 변경"과 "표"는 다른 작업으로 따로 진행한다.

- 글자를 드래그하다 다른 블록으로 넘어가면 블록 단위 선택으로 전환(Notion 방식)
- 좌우·아래 빈 여백에서 드래그 → 사각 영역으로 여러 블록 선택
- `Shift+클릭`으로 블록 범위 선택
- 선택한 여러 블록을 손잡이로 함께 끌어 옮기기(지금은 블록 하나 + 자식만), 실행 취소 한 단계
- `Ctrl+Home`/`Ctrl+End` 문서 처음·끝, `PageUp`/`PageDown` 화면 단위 블록 이동

원칙 유지: 가상화(보이는 블록만 행), 편집하는 행에만 TextBox, 실행 취소는 변경 구간만. 1만 줄 문서 글자 입력 중앙 ≤ 20ms.

## 설계

- 블록 선택 모델은 그대로(연속 범위 하나 `_selAnchor`~`_selActive` + 자식). 영역·Shift+클릭·글자 끌기 모두 이 범위를 만든다 —
  삭제·복사·들여쓰기·전환·색 등 기존 선택 명령이 그대로 적용된다.
- 마우스로 고르는 동안 위치는 패널의 높이 누적(`_tops`)으로만 찾는다(이진 검색). 행을 새로 만들거나 TextBox를 붙이지 않고 화면에 있는 행의 선택 표시만 바꾼다.
- 사각 영역은 세로 구간만 본다(선택이 연속 범위이므로). 가로는 본문 폭(손잡이 포함)에 닿아야 한다. 열 묶음은 통째로 들어간다.
- 글자 끌기 전환: 마우스를 잡은 TextBox(`Mouse.Captured`)의 행 밖, 다른 블록 위로 가면 캡처를 패널로 옮긴다. 전환 뒤에는 처음 블록으로 돌아와도 블록 선택(한 블록)으로 둔다.
- 여러 블록 끌기: 선택 안의 블록 손잡이를 누르면 선택 전체를 끈다. `BlockSelection.MoveRangeTo`로 한 번에 옮기고 `RecordChange` 한 번(실행 취소 한 단계).
  들여쓰기는 범위의 가장 얕은 블록을 놓을 자리 수준에 맞추고 상대 깊이를 유지한다(블록 하나 + 자식일 때는 예전과 같은 결과).
- `PageUp`/`PageDown`: 지금 블록 위쪽 y ± 뷰포트 90%의 블록, 화면도 같은 거리만큼 넘긴다. 블록 하나가 화면보다 크면 한 블록 넘어간다.

## 구현 내용

- `Folderss/Services/BlockSelection.cs`(신규, WPF 없음): `IndexAt`, `SpanRange`, `PageTarget`, `MoveRangeTo`.
- `Folderss/Controls/BlockEditor.Selection.cs`(신규): 사각 영역 선택(`BeginMarquee`·`MarqueeAdorner`·자동 스크롤 타이머), 글자 끌기 → 블록 선택(`OnCrossBlockPreviewMouseMove`),
  `Shift+클릭`(`OnShiftClickPreview`, 조상 찾기는 `ElementTree.FindAncestor`), 여러 블록 끌기(`GetDragRange`·`MoveDraggedBlocks`), `HandleNavigationKey`.
- `Folderss/Controls/BlockEditor.Panel.Selection.cs`(신규): `BlockPanel`을 partial로 나눠 `IndexAtPoint`·`SpanRange`·`PageTargetFrom`·좌표 변환.
- 기존 파일 연결: `BlockEditor.cs`(생성자 `InitializeSelection()`, `OnPreviewKeyDown` 한 줄, `HandleMouseDown`·`UpdateDropIndicator`·`DropBlock`·`ShowBlockMenu`),
  `BlockEditor.Panel.cs`(`partial`, 빈 여백 누르기 → `BeginMarquee`, 예전 클릭 동작은 `ClickAt`으로 떼어 놓음 — 이제 마우스를 뗄 때 실행).
- 블록 손잡이 메뉴: 선택한 여러 블록 중 하나의 메뉴를 열면 선택을 유지해 선택 전체에 적용.

## 검증

1. 빌드: `dotnet build .\Folderss.sln -c Debug`, `dotnet build tests/Folderss.SearchTests` 오류 0.
2. 단위 테스트(`BlockSelectionTests`, 11개 — 병합 후 일괄 실행): 위치 찾기(높이 0 블록), 영역 범위(양방향·경계·빈 곳), 한 화면 목표(큰 블록·숨은 블록),
   여러 블록 옮기기(아래·위·끝, 들여쓰기 맞춤, 범위 안 놓기 거부, 열 속성).
3. WPF 하네스(병합 후 일괄 실행, 저장소에 하네스 코드 없음 — 스크래치 하네스에 추가할 검사):
   - 영역: 왼쪽 여백에서 블록 2~4 높이까지 끌기 → 2~4 선택, 아래 빈 곳에서 위로 끌기 → 마지막부터 선택, 끌지 않고 떼기 → 그 블록에 커서(예전 동작), 가장자리 자동 스크롤
   - 글자 끌기: 블록 1 글자에서 블록 3까지 끌기 → 1~3 블록 선택, TextBox 포커스 없음, `Delete`로 세 블록 삭제
   - `Shift+클릭`: 블록 2 편집 중 블록 5 Shift+클릭 → 2~5 선택, 같은 블록 안은 글자 선택
   - 여러 블록 끌기: 2~4 선택 후 3의 손잡이를 끝으로 → 순서 유지·선택 유지, `Ctrl+Z` 한 번에 원래대로
   - 키: `Ctrl+End` 마지막 블록 끝 커서, `Ctrl+Home` 첫 블록, `PageDown`/`PageUp` 화면 넘김, 블록 선택 중 `Shift+PageDown` 확장
   - 성능: 1만 줄 문서 글자 입력 중앙 ≤ 20ms(입력 경로는 바뀌지 않음), 영역 끌기 중 이동 처리 시간

### 병합 후 검증 결과 (2026-11-01)

- 단위 테스트: 전체 662개 중 통과 658·건너뜀 4·실패 0(`BlockSelectionTests` 포함). 솔루션 빌드 경고·오류 0.
- WPF 하네스(스크래치, 저장소 밖): 화면 밖(-20000) 창, 활성화하지 않음. 마우스는 WPF `InputManager`에 원시 마우스 보고를 넣어 캡처·MouseEnter·Preview/버블·ClickCount가
  실제와 같은 경로를 타게 하고, 커서 위치·키 상태(`GetCursorPos`/`GetKeyState`)는 하네스 프로세스 안에서만 흉내 냈다(실제 마우스·키보드는 건드리지 않음).
  선택·이동 **53개 모두 통과**(버그 1건 수정 후):

| 검사 | 결과 |
|---|---|
| 영역: 왼쪽 여백 → 블록 2~4(끄는 중·놓은 뒤, 앵커 2, 편집기 포커스, 캡처 해제), `Delete` 3블록·Undo | 통과 |
| 영역: 아래 빈 곳에서 위로 → 9~11, 앵커 11, `Shift+↑`로 8~11 | 통과 |
| 끌지 않고 떼기 → 그 블록 글자 끝 커서(블록 선택 없음), 문서 아래 클릭 → 끝 블록 | 통과 |
| 가장자리 자동 스크롤(600ms에 0→250px, 선택 26→35), 놓으면 멈춤 | 통과 |
| 글자 끌기 1→3 → 블록 1~3(패널 캡처, 처음 블록으로 돌아오면 1 하나), 놓은 뒤 캡처 해제·TextBox 포커스 없음, `Delete` 세 블록·Undo / 위로 4→2 | 통과(**수정 후**) |
| `Shift+클릭`: 블록 2 편집 중 5 → 2~5, 선택 중 7 → 2~7, 같은 블록은 글자 선택 | 통과 |
| 여러 블록 끌기: 2~4 선택 후 3의 손잡이를 끝으로 → 순서 유지·선택 9~11 유지, `Ctrl+Z` 한 번에 원래대로 / 선택 밖 손잡이는 그 블록만 | 통과 |
| 키: `Ctrl+End` 마지막 블록 글자 끝, `Ctrl+Home` 첫 블록 처음, `PageDown`(한 화면 703px 넘김·블록 화면 안)/`PageUp`, 블록 선택 중 `Shift+PageDown` 확장(0~25)·`PageDown` 이동, `Ctrl+Shift+End` | 통과 |

- 고친 버그: **글자 끌기 → 블록 선택이 두 블록에서 멈춤.** `LostMouseCapture`는 버블 이벤트라 전환 때 TextBox가 캡처를 놓으며 낸 이벤트가 패널의
  `LostMouseCapture` 처리기로 올라와 `EndMouseSelect(false)`가 막 시작한 선택을 끝냈다. 그 뒤 패널은 캡처를 쥔 채 모드가 `None`이어서 선택이 넓어지지 않았고
  마우스를 놓아도 캡처가 풀리지 않았다. 패널 자신의 캡처 잃음(`e.OriginalSource == _panel`)만 처리하도록 고침(`BlockEditor.Selection.cs`).
- `PageUp` 왕복은 블록 위쪽 기준으로 계산하므로 `PageDown` 두 번 → `PageUp`이 처음 `PageDown` 자리 ±1 블록에 온다(설계대로, 25 → 24).
- 성능(Debug, 11,424줄·블록 9,862개·표 행 8,301·이미지 60): 문단 글자 입력 **중앙 2.7ms**, 영역 끌기 마우스 이동 처리 중앙 0.4ms(최대 39ms),
  글자 끌기→블록 선택 이동 처리 중앙 0.2ms, `PageDown` 중앙 59ms(새 화면 행 생성 포함), `Ctrl+End` 중앙 0.4ms.
- 사람이 직접 볼 것: 실제 마우스로 영역 사각형 그리기 모양·색(장식 층), 자동 스크롤 속도 체감, 손잡이 끌기 중 놓기 선 위치.

### #76 병합 후 회귀 (2026-11-01)

- 하네스 `harness76m`(`C:\Users\KYJ\AppData\Local\Temp\claude\D--git-cshap-Folderss\0e9a6dec-1b59-4430-8014-acdde638b328\scratchpad\harness76m\`) `selection` **53개 모두 통과**. 문단은 이제 서식 입력란(`RichBlockBox`, `IBlockInput`)이라 하네스의 `TextBox` 캐스트를 `IBlockInput` 감싸개로 바꿨다.
  글자 끌기 → 블록 선택 전환은 RichTextBox 행에서도 그대로(캡처 `TextBoxBase`).
- `Shift+클릭`은 실제처럼 **블록 5의 표시 글자**를 누르도록 고침(입력란은 편집기에 하나라 하네스가 블록 5 입력란을 가져오면 입력란이 블록 5로 옮겨 가 시험이 틀어짐) — 2~5·2~7·같은 블록 글자 선택 통과.
- 같은 검증에서 고친 버그(9228041): ↑↓←→·표시 글자 클릭으로 서식 입력란을 옮겨 붙이면 포커스 행(`_focusedRow`)이 이전 행으로 남아, 멀리 스크롤하면 편집 행이 거둬져 포커스를 잃던 문제(`block-editor-stage2-inline-format.md` 검증 절).

## 변경 이력

- 2026-11-01: 착수·구현, 빌드 확인 (`Ready for Verification`). 단위 테스트·하네스는 병합 후 일괄 실행.
- 2026-11-01: 병합 후 하네스 53개·단위 테스트·1만 줄 성능 확인. 글자 끌기 → 블록 선택이 버블된 `LostMouseCapture`로 끊기던 문제 수정 (`Ready for Verification`, 사용자 확인 대기)

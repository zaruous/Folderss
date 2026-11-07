# 블록 편집 ↔ 미리보기(Markdown 뷰어) 전환 (#81 "1. 뷰어 전환")

- 상태: Done — master 머지 확인 (이전: Ready for Verification)
- 상태 전환일: 2026-11-07
- 이슈: #81 (묶음 #79)

## 요구사항

이슈 #81 "1. 뷰어 전환". 같은 이슈의 "front matter 표시"(블록 편집기 안 원문 블록 → 속성 표)는 다른 브랜치에서 따로 구현한다. "3. 기본 뷰어 전환 검토"는 구현하지 않고 아래 판단 자료만 남긴다.

- `.md`·`.markdown` 뷰어 탭 툴바(`ViewerHost`)에 `블록 편집` / `미리보기` 전환 버튼 — 확장자 매핑과 무관하게 지금 탭만 바꾼다(`builtin:blocks` ↔ `builtin:markdown`)
- 저장하지 않은 변경이 있으면 "저장 후 전환 / 취소"를 묻는다(편집 내용을 다른 뷰어로 넘기는 것은 하지 않음 — #81의 2차 검토 항목)
- 전환할 때 보던 위치 유지 — 블록 편집기 앵커 블록 ↔ 미리보기 제목·줄 번호(제목 1차, 줄 번호 가능한 범위)
- 파일별 마지막 보기 기억(Markdown 뷰어의 파일별 모드 기억과 같은 방식), 세션 복원 때 그 보기로 다시 열기
- 설정 > 뷰어에 `.md` 탭을 처음 열 때의 기본 보기 선택(기본값은 매핑을 따름 = 동작 변화 없음)
- 탭 닫기 잠금 유지, 이전 뷰어(WebView2 포함) 자원 해제

## 원인 분석 또는 설계

- **탭은 그대로, 뷰어만 교체**: `ViewerHost` 안에서 `DetachCurrentViewer`(이전 뷰어 `IDisposable.Dispose` — Markdown 뷰어는 `WebView2.Dispose`·감시자 정지, 블록 편집기는 감시자 정지) 후
  새 뷰어를 붙인다. `LayoutDocument`·ContentId(`viewer|경로`)를 바꾸지 않으므로 패널 잠금(`PanelLockService`는 ContentId 기준)·`CanClose`·탭 위치·세션 항목이 그대로이고,
  새 뷰어의 `TitleChanged`는 `CreateViewerHost`가 이미 구독한 `SetDocumentTitle` 경로로 들어가 잠금 표시(`🔒 `)가 유지된다. MainWindow는 고치지 않았다.
- **버튼을 보이는 조건**: 확장자가 `.md`·`.markdown`이고 지금 뷰어 키가 Markdown/블록 편집기(`IMarkdownViewSwitchable` 구현)일 때. `.md`를 Monaco 등으로 매핑했으면 버튼·기억을 쓰지 않는다
  (기억한 보기가 사용자가 명시한 다른 매핑을 덮지 않게).
- **저장 확인**: `ViewerHost.IsModified`면 "저장한 뒤 … 전환할까요?" 확인/취소(기본 취소, owner 지정). 확인 → `SaveForSwitchAsync`가 성공해야 전환. Markdown 뷰어는 저장 본문이 페이지에 있으므로
  `app.currentContent()`로 받아 기존 `Save`(이제 `bool` 반환)로 쓴다. 저장 직후 `ModifiedChanged(false)`를 직접 올려 탭 제목의 ` *`를 지운다(Markdown 뷰어의 `modified=false` 메시지는 이미 해제된 뒤라 오지 않음).
- **위치 대응**(순수 로직 `Services/MarkdownViewSwitch`, WPF 없음 — 테스트 프로젝트에 소스 링크):
  - 블록 → 미리보기: 화면 맨 위 블록(`BlockPanel.GetTopAnchor`) 위에서 가장 가까운 제목의 앵커 id(`HeadingIdAbove` — `BlockFind.Slugify`·중복 `-1` 규칙, `FindHeading`과 같은 순서)와
    그 블록의 줄 번호(`LineOfBlock` — 앞 블록까지 `Serialize`한 글과 이 블록까지 쓴 글이 갈라지는 곳). Markdown 뷰어는 `app.open` 직후 `app.setViewPosition(제목, 줄)`:
    미리보기는 제목으로(부드러운 스크롤 없이 즉시), 원문 편집은 줄로(`wrap=off`라 `줄 높이 × (줄-1)`), 분할은 둘 다.
  - 미리보기 → 블록: 페이지 `app.getViewPosition()` — 미리보기·분할은 화면 맨 위(그 위 포함) 제목 id, 원문 편집(대용량 파일 모드 포함)은 맨 위 줄 번호와 `mode=edit`(`PreferLine`).
    블록 편집기는 `ToBlock`: 제목 → (없거나 못 찾으면) 줄 번호(`BlockAtLine` 이분 탐색, `LineOfBlock`이 블록 순서대로 늘어남을 이용), 원문 편집에서 온 위치는 줄 번호 먼저.
    `BlockEditor.RestoreViewPosition`이 접힌 토글을 펴고(`RevealBlock`) `ScrollToAnchor`로 맨 위에 둔다(Dispatcher `Loaded` 시점 — 가상화 패널이 측정된 뒤).
  - 한계: 미리보기는 제목 단위라 제목 아래로 깊이 내려가 있던 위치는 그 제목으로 올라간다(제목이 없는 앞부분은 맨 위). 줄 번호는 블록 편집기가 정리한 형식(`Serialize`) 기준이라
    손으로 쓴 원문(빈 줄 여러 개, `*` 글머리 등)과는 몇 줄 어긋날 수 있다. 미리보기 이미지·Mermaid가 늦게 그려지면 제목 위치가 그만큼 밀릴 수 있다.
- **기억**(`MarkdownViewSettings`, `%LOCALAPPDATA%\Folderss\markdown-view.json`, `version` 1):
  - `defaultView`: `mapping`(기본) · `blocks` · `preview` — 설정 > 뷰어 ".md 탭을 처음 열 때".
  - `files`: `{path, view}` 배열, 오래된 것부터, 최근 200개(Markdown 뷰어 `localStorage` `modes`와 같은 방식 — 다시 쓰면 가장 최근으로 옮김). 경로는 `Path.GetFullPath`, 대소문자 무시.
    전환 버튼으로 바꿀 때만 기록한다(열기만 한 파일은 기록하지 않음 → 기본 보기 설정을 바꾸면 그대로 따라감).
  - `ViewerHost.OpenFile`이 `ChooseViewerKey(확장자, 매핑, 파일별 기억, 기본 보기)`로 뷰어를 고르므로 새 탭·링크로 연 탭·세션 복원(`ResolveDockContent` → `OpenFile`) 모두 같은 규칙.
  - 쓰기는 `SettingsFile.WriteAllText`(임시 파일 후 교체), 실패는 예외. 설정 창은 `TrySave(".md 기본 보기", "markdown-view.json", …)`, 전환 때 기억 저장 실패는 전환을 막지 않고 `Trace`에 남긴다(편의 기능).
  - 플러그인 계약(`GetAppSettings`)에는 노출하지 않았다 — 계약 변경 없음.
- Markdown 뷰어 자체의 편집/분할/미리보기 모드 기억(`localStorage`)은 그대로다. "미리보기"로 전환해도 그 파일에서 마지막으로 쓴 Markdown 뷰어 모드(예: 원문 편집)로 열린다.

## 구현 내용

- `Folderss/Services/MarkdownViewSwitch.cs` (새 파일): `MarkdownViewPosition`, `MarkdownViewSwitch`(뷰어 고르기·위치 대응), `MarkdownViewSettings`(기본 보기·파일별 기억 저장).
- `Folderss/Services/ViewerConfigService.cs`: `Resolve` 본문을 `public static CreateViewer(키)`로 분리(동작 같음).
- `Folderss/Viewers/IMarkdownViewSwitchable.cs` (새 파일): `SaveForSwitchAsync`·`CaptureViewPositionAsync`·`RestoreViewPositionWhenReady`.
- `Folderss/Viewers/BlockEditorViewer.ViewSwitch.cs` (새 partial): 위 인터페이스 구현.
- `Folderss/Viewers/MarkdownViewer.ViewSwitch.cs` (새 partial): 위 인터페이스 구현(`app.currentContent`·`getViewPosition`·`setViewPosition`).
- `Folderss/Viewers/MarkdownViewer.xaml.cs`: `Save`가 성공 여부(`bool`) 반환, `CallAppOpen` 끝에 `ApplyPendingViewPositionAsync` 한 줄.
- `Folderss/Viewers/Resources/markdown-app.js`: `getViewPosition`·`setViewPosition`·`currentContent` 추가(기존 동작 변경 없음).
- `Folderss/Controls/BlockEditor.ViewSwitch.cs` (새 partial): `CaptureViewPosition`·`RestoreViewPosition`.
- `Folderss/Controls/ViewerHost.xaml`: 툴바에 `ViewSwitchPanel`(`CompactToggleButtonStyle` 토글 2개).
- `Folderss/Controls/ViewerHost.xaml.cs`: `OpenFile`이 `ChooseViewerKey`로 뷰어 선택, 붙이기를 `AttachViewer`로 분리, 지금 뷰어 키 보관.
- `Folderss/Controls/ViewerHost.ViewSwitch.cs` (새 partial): `SwitchViewAsync`(재진입 막기, 저장 확인, 기다리는 사이 탭이 닫히면 중단), `CurrentView`·`CanSwitchView`(하네스용).
- `Folderss/SettingsWindow.xaml(.cs)`: 뷰어 패널 아래 ".md 탭을 처음 열 때" 콤보, `TrySave` 등록.

## 변경 파일

- 새 파일: `Folderss/Services/MarkdownViewSwitch.cs`, `Folderss/Viewers/IMarkdownViewSwitchable.cs`, `Folderss/Viewers/BlockEditorViewer.ViewSwitch.cs`, `Folderss/Viewers/MarkdownViewer.ViewSwitch.cs`,
  `Folderss/Controls/BlockEditor.ViewSwitch.cs`, `Folderss/Controls/ViewerHost.ViewSwitch.cs`, `tests/Folderss.SearchTests/MarkdownViewSwitchTests.cs`, 이 문서
- 수정: `Folderss/Services/ViewerConfigService.cs`, `Folderss/Viewers/MarkdownViewer.xaml.cs`, `Folderss/Viewers/Resources/markdown-app.js`, `Folderss/Controls/ViewerHost.xaml`,
  `Folderss/Controls/ViewerHost.xaml.cs`, `Folderss/SettingsWindow.xaml`, `Folderss/SettingsWindow.xaml.cs`, `tests/Folderss.SearchTests/Folderss.SearchTests.csproj`(소스 링크), `README.md`, `docs/architecture.md`

## 검증

- [x] `dotnet build .\Folderss.sln -c Debug` 오류 0, `dotnet build tests/Folderss.SearchTests` 오류 0
- [x] 단위 테스트 `dotnet test tests/Folderss.SearchTests --filter MarkdownViewSwitchTests` — 병합 후 전체 735개 중 통과 731·건너뜀 4·실패 0
  - 뷰어 고르기: 매핑을 따름(동작 변화 없음), 기억 > 기본 보기 > 매핑, 다른 매핑(Monaco)·다른 확장자는 그대로
  - 제목 id: 가장 가까운 위 제목·중복 `-1`·서식 제거, `FindHeading`과 왕복
  - 줄 번호: 정리된 문서에서 블록별 시작 줄, `BlockAtLine`(빈 줄·끝 넘음·빈 문서), 큰 문서(제목·여러 줄 문단·코드) 왕복, `ToBlock`/`FromBlock` 우선순위
  - 기억: 파일 없음 기본값, 저장·다시 읽기 왕복(대소문자 무시 덮어쓰기), 최근 200개(다시 쓰면 남음), 깨진 JSON·모르는 값 무시
- [x] WPF 하네스(병합 후 일괄, 결과는 아래 "병합 후 검증 결과") — 검사 목록:
  1. `.md`(매핑 Markdown) 탭: 툴바에 `블록 편집`·`미리보기` 표시, `미리보기`가 눌린 상태. `.json` 탭·`.md`를 Monaco로 매핑한 탭에는 버튼 없음
  2. `블록 편집` 클릭 → `CurrentView == "blocks"`, 콘텐츠가 `BlockEditorViewer`, 이전 `MarkdownViewer`가 `Dispose`됨(WebView2 `IsDisposed`/`CoreWebView2` 접근 시 예외, 브라우저 자식 프로세스 수가 늘지 않음 — 10번 왕복 후 비교)
  3. 같은 탭(`LayoutDocument` 참조 동일), ContentId 그대로, 잠근 탭이면 전환 후에도 `CanClose == false`·제목 `🔒 ` 접두사 유지, 탭 순서·`+ 새 패널` 끝 유지
  4. 미저장 상태(블록 편집기에서 글자 입력, ` *` 표시)에서 `미리보기` → 확인창: 취소 → 그대로 블록 편집기·` *` 유지·파일 안 바뀜 / 확인 → 파일 저장(내용 = `GetMarkdown`)·Markdown 뷰어로·` *` 사라짐
  5. Markdown 뷰어 원문 편집에서 수정(` *`) 후 `블록 편집` → 확인 → 디스크 내용 = 편집 내용, 블록 편집기가 그 내용으로 열림. 저장 실패(읽기 전용 파일) → 오류 창·전환 안 됨·` *` 유지
  6. 위치(블록 → 미리보기): 1만 줄 문서에서 블록 편집기를 중간 제목 아래 블록으로 스크롤 → `미리보기` → 미리보기 맨 위 제목 = 그 제목(`app.getViewPosition().heading`). 중복 제목(두 번째 `## 설치`) → `설치-1`
  7. 위치(미리보기 → 블록): 미리보기를 중간 제목으로 스크롤 → `블록 편집` → 화면 맨 위 블록 = 그 제목 블록. 접힌 토글 안 제목이면 토글이 펴짐(수정 아님)
  8. 위치(원문 편집 → 블록 → 원문 편집): Markdown 뷰어 원문 편집 모드에서 500번째 줄 근처 → `블록 편집` → 맨 위 블록 줄 번호(`LineOfBlock`)가 ±3줄 안 → 다시 `미리보기`(원문 편집 모드 기억) → 편집기 맨 위 줄 ±3줄
  9. 파일별 기억: A.md를 `블록 편집`으로 바꾸고 탭 닫기 → 다시 열면 블록 편집기. B.md는 매핑대로. `markdown-view.json`에 A만 기록
  10. 세션 복원: A.md(블록 편집)·B.md(미리보기) 탭을 연 채 종료 → 다시 시작 → A는 블록 편집기, B는 Markdown 뷰어로 복원, 잠금 상태 유지
  11. 설정 > 뷰어 `.md 탭을 처음 열 때` = `블록 편집` 저장 → 기억 없는 C.md가 블록 편집기로 열림, 기억 있는 A·B는 기억대로. `확장자 매핑을 따름`으로 되돌리면 C는 매핑대로. 설정 파일 쓰기 실패(읽기 전용 `markdown-view.json`) → "설정 저장 실패" 목록에 `.md 기본 보기`
  12. 연타·재진입: 전환 버튼을 빠르게 두 번 → 한 번만 전환(진행 중 버튼 비활성), 확인창이 떠 있는 동안 탭 닫기·다른 파일 링크 → 예외 없음
  13. 외부 변경·링크와 겹침: 전환 직후 외부에서 파일 수정 → 새 뷰어가 반영(이전 뷰어 감시자 이벤트 없음), 다른 문서의 `파일.md#제목` 링크로 연 탭은 그 제목으로(전환 위치보다 우선하지 않아도 됨 — 서로 다른 경로)
  14. 성능: 1만 줄 문서 블록 → 미리보기 전환 위치 계산(`FromBlock`) 시간, 미리보기 → 블록(`ToBlock` 줄 번호 경로, Serialize 이분 탐색) 시간 기록(목표: 각 100ms 이하). 블록 편집기 글자 입력 중앙 ≤ 20ms 유지(입력 경로는 바뀌지 않음)
- 사람이 직접 볼 것: 토글 버튼 모양(밝은·어두운 테마), 확인창 문구, 미리보기에서 이미지·Mermaid가 늦게 그려질 때 위치가 밀리는 정도.

### 병합 후 검증 결과 (2026-11-01)

스크래치 WPF 하네스(화면 밖 창 -20000, InputManager로 마우스·키 주입, 커서·키 상태는 하네스 프로세스 안에서만 흉내, 확인창은 하네스 UI 스레드의 메시지 상자만 찾아 답하고 아이콘(`STM_GETICON`)·기본 버튼(`DM_GETDEFID`)을 기록).
MainWindow 전체는 띄우지 않았다 — 실제 `%LOCALAPPDATA%\Folderss`의 세션·레이아웃·잠금 파일을 읽고 쓰기 때문. 대신 AvalonDock `DockingManager`에 `[A.md, B.md, + 새 패널]` 문서 탭을 만들어 `ViewerHost`를 넣고,
MainWindow의 탭 제목·잠금 규칙(`SetDocumentTitle`·`FormatDocumentTitle`·`GetPanelLockKey`·`IsPanelLocked`)은 리플렉션으로 같은 코드를 불렀다. 잠금은 `PanelLockService` 메모리 집합에만 넣었다(파일 저장 없음).
`markdown-view.json`·`viewer-config.json`은 하네스 폴더 경로로 바꿨고(`MarkdownViewSettings._shared`·`ViewerConfigService(path)`), WebView2 사용자 데이터 폴더도 환경 변수 `WEBVIEW2_USER_DATA_FOLDER`로 하네스 전용.
**WebView2(Markdown 뷰어)는 화면 밖 창에서도 초기화·문서 열기·스크립트 실행이 됨**(위치 검사는 `app.getViewPosition()`으로 직접 확인). 시험 문서: 정리된 형식 8,276줄·블록 6,282개(제목 330여 개, 중복 `## 설치`, 접힌 토글 안 `###` 제목).
결과 53개 **모두 통과**, 기능 수정 없음.

| # | 검사 | 결과 |
|---|---|---|
| 1 | `.md`(매핑 Markdown) 탭: 버튼 표시·`미리보기` 눌림·`CurrentView` preview / `.json` 탭·`.md`를 Monaco로 매핑한 탭은 버튼 없음 | 통과 |
| 2 | `블록 편집` 클릭 → blocks·`BlockEditorViewer`, 이전 `MarkdownViewer._disposed`, 해제된 WebView2의 `CoreWebView2` 접근 → `ObjectDisposedException` / 10번 왕복 모두 전환·해제, **WebView2 자식 프로세스 6개 → 10번 왕복 뒤 6개**(늘지 않음) | 통과 |
| 3 | 같은 `LayoutDocument`·같은 `ViewerHost`, ContentId 그대로, 잠근 탭 `CanClose=false`·제목 `🔒 A.md` 유지, 탭 순서 `A, B, + 새 패널` | 통과 |
| 4 | 블록 편집기 입력 → ` *` / `미리보기` → "보기 전환" 확인창(Question·기본 취소): 취소 → 블록 편집기·` *`·파일 그대로·버튼 상태 유지, 확인 → 디스크 = `GetMarkdown`·미리보기·` *` 사라짐(🔒 유지) | 통과 |
| 5 | Markdown 뷰어 원문 편집에서 수정(` *`) → `블록 편집` → 확인 → 디스크 = `app.currentContent()`·블록 편집기가 그 내용 / 읽기 전용 파일 → "저장 오류" 창·전환 안 됨·` *` 유지·버튼 다시 사용 가능. 블록 편집기 쪽 저장 실패도 같음 | 통과 |
| 6 | 블록 편집기 맨 위 = `## 섹션 200` 아래 3번째 블록 → 미리보기 `heading` = `섹션-200` / 두 번째 `## 설치` 아래 → `설치-1` | 통과 |
| 7 | 미리보기를 `섹션-300`으로 → 블록 편집기 맨 위 = 그 제목 블록 / 미리보기에서 펼친 토글 안 제목 → 블록 편집기 토글 펴짐·그 제목이 맨 위·수정 아님 / 닫힌 토글 안 제목이 맨 위 제목으로 잘못 잡히지 않음 | 통과 |
| 8 | 원문 편집 500번째 줄(`line` 499) → 블록 편집기 맨 위 블록 줄 499 → 다시 `미리보기`(원문 편집 모드 기억) → 맨 위 줄 498 (각 ±3 안) | 통과 |
| 9 | A를 블록 편집으로 바꾸고 탭 닫기(`ViewerHost.Dispose`) → 다시 열면 블록 편집기, B는 매핑대로, `markdown-view.json`에 A만 | 통과 |
| 10 | 재시작 흉내(설정 파일을 새로 읽고 새 탭에서 `OpenFile` — `ResolveDockContent`와 같은 경로): A 블록 편집기·B Markdown 뷰어, A 잠금(같은 잠금 키) 유지 | 통과 |
| 11 | 기본 보기 `블록 편집` → 기억 없는 C는 블록 편집기, A·B는 기억대로 / `매핑을 따름` → C는 미리보기 / 읽기 전용 `markdown-view.json` → `SettingsWindow.TrySave` 실패 목록에 `.md 기본 보기 (markdown-view.json)` / 설정 창 콤보 3개·선택값 = 저장값 | 통과 |
| 12 | 미리보기 → 블록 연타(입력 사이 처리 없이 두 번) → 진행 중 버튼 비활성, 새 뷰어 1번만 / 확인창이 떠 있는 동안 탭 닫기(`Dispose`)·같은 호스트가 다른 파일 열기 → 예외 없음·전환 안 함 | 통과 |
| 13 | 블록 편집으로 바꾼 직후 외부 수정 → 새 블록 편집기만 물음(1번)·반영, 미리보기로 바꾼 뒤 외부 수정 → Markdown 뷰어만 물음(1번) / `A.md#섹션-150` 대기 앵커로 열면 블록 편집기에서 그 제목이 맨 위, `B.md#b-제목`은 미리보기가 소비 | 통과 |
| 14 | 성능(아래) | 통과 |

- 성능(Debug, 6,282블록): `FromBlock` 중간 블록 중앙 2.7ms·끝 블록 5.7ms, `ToBlock` 제목 경로 0.1ms, **`ToBlock` 줄 번호 경로(끝 근처) 중앙 65~73ms(최대 106ms)** — 목표 100ms 안이지만 이분 탐색마다 앞부분을 두 번 `Serialize`하므로
  문서 크기에 비례해 늘어난다(더 큰 문서·느린 PC에서 100ms를 넘을 수 있음 — 한 번 `Serialize`로 블록별 시작 줄을 모두 구하는 방식이 후보). 뷰어 안 블록 편집기 문단 글자 입력 중앙 3.1ms(단독 실행. 같은 날 다른 프로그램 부하가 생긴 뒤 재측정에서는 이 경로와 무관한 기준 측정까지 모두 약 16ms로 올라감 — `block-editor-front-matter.md` 검증 9의 A/B 참고, 둘 다 20ms 안).
- 관찰(설계 판단, 고치지 않음): 확인창이 떠 있는 동안 탭이 닫혀도 `확인`을 누르면 저장은 된다(전환만 안 함). 사용자가 "저장 후 전환"을 고른 것이라 저장 자체는 의도에 맞다고 보고 그대로 둠.
- 하네스로 확인하지 못한 것(사람이 볼 것): 실제 MainWindow에서 패널 잠금 파일(`panel-locks.xml`)·`session.xml`을 거친 재시작 복원, 토글 버튼 모양(밝은·어두운 테마), 실제 마우스로 누를 때의 버튼 반응, 확인창이 앱 앞에 뜨는지,
  미리보기 이미지·Mermaid가 늦게 그려질 때 위치가 밀리는 정도.
### #76 병합 후 회귀 (2026-11-01)

- 하네스 `harness76m`(`C:\Users\KYJ\AppData\Local\Temp\claude\D--git-cshap-Folderss\0e9a6dec-1b59-4430-8014-acdde638b328\scratchpad\harness76m\`) `switch` **53개 모두 통과**(한 번은 직전에 강제 종료한 하네스가 WebView2 사용자 데이터 폴더를 잡고 있어 초기화 실패 → 다시 실행해 통과).
- `ToBlock` 줄 번호 경로: #76 이전 빌드 중앙 73.5ms ↔ 이번 69.7ms(같은 부하 환경 번갈아 측정) — 차이 없음. 뷰어 안 블록 편집기 글자 입력 중앙 3.8ms.

## 3. 기본 뷰어 전환 검토 — 판단 자료 (구현 안 함)

> **갱신(2026-11-01, #81 3단계)**: 아래 권장대로 진행 — 가장 큰 막힘이던 "저장하면 형식 정리"를 최소 변경 저장(바뀐 블록만 다시 씀)으로 없애고,
> `.md` 매핑은 그대로 둔 채 설정 ".md 탭을 처음 열 때"의 기본값(저장한 값이 없을 때)을 `블록 편집`으로 바꿨다. 32MB 넘는 파일은 미리보기로 연다.
> 설계·보존되지 않는 경우·남은 격차 재점검(Mermaid·HTML 블록·원격 이미지 등)·검증 목록은 `docs/items/block-editor-minimal-diff-save.md`. 아래 표는 그 전 시점 기록이다.

이번 작업으로 `.md` 기본 매핑을 바꾸지 않고도 사용자가 고를 수 있게 됐다: 설정 ".md 탭을 처음 열 때 = 블록 편집"으로 사실상 기본 뷰어를 블록 편집기로 쓸 수 있고, 탭마다 `미리보기`로 언제든 Markdown 뷰어를 쓴다.
따라서 남은 결정은 "아무것도 설정하지 않은 사용자의 기본값을 바꿀지"이다.

남은 격차(#81 표 기준, 2026-11-01):

| 항목 | 상태 | 기본값을 바꿀 때 영향 |
|---|---|---|
| `Ctrl+F`·외부 변경·링크 이동 | #75로 해결 | 없음 |
| 이미지 붙여넣기 | #77로 해결 | 없음 |
| 원격 이미지 | 블록 편집기는 블록마다 `이번만 불러오기`(수동), Markdown 뷰어는 https 자동 | 원격 이미지가 많은 README 등은 처음 열 때 비어 보임 |
| 줄 안 서식(굵게·링크 등) | #76 미구현 — 표식(`**`)이 그대로 보임 | 읽기용으로는 Markdown 뷰어보다 덜 읽힘. **가장 큰 격차** |
| 수식 | #77 C# 렌더러로 해결(KaTeX와 표현 범위 차이 가능) | 복잡한 수식은 미리보기와 모양이 다를 수 있음 |
| Mermaid | 블록 편집기 미지원(#77에서 제외) | 다이어그램 문서는 코드로만 보임 → 미리보기 전환 필요 |
| TOC 옆 패널 | 목차 블록(`[TOC]`)만, 옆 패널 없음 | 긴 문서 탐색이 불편 |
| front matter | 다른 브랜치에서 속성 표 보기 구현 중 | 병합 후 해결 예정 |
| 내보내기·인쇄·공유 | 이번 전환 버튼으로 해결(미리보기로 전환) | 한 번 클릭 더 |
| 분할 보기 | 블록 편집기에는 두지 않음(원문은 Markdown 뷰어 편집 모드) | 전환 버튼으로 대신 |

블록 편집기를 기본으로 할 때의 장점:
- 큰 문서(1만 줄)에서 열기 약 0.15초·입력 약 15ms로 WebView2 Markdown 뷰어보다 체감 성능이 크게 좋다(5MB 넘는 문서는 Markdown 뷰어가 미리보기를 끄는데 블록 편집기는 32MB까지 연다).
- 문서를 바로 고치는 흐름(블록·표·이미지·여러 열)이 기본이 된다. WebView2 프로세스를 띄우지 않아 탭이 많을 때 메모리가 적다.

단점·위험:
- **저장하면 Markdown 형식이 정리된다**(글머리 `-`, 번호 다시 매김, 블록 사이 빈 줄 하나, 표 구분 행). 남이 쓴 README를 한 글자만 고쳐도 diff가 커질 수 있다 — 기본값으로 두려면
  "바뀐 블록만 원문 그대로 쓰기"(원문 범위 보존) 같은 최소 변경 저장이 먼저 필요하다. 지금 가장 큰 차단 요인.
- #76 전까지 서식 표식이 보여 "읽기" 용도로는 Markdown 뷰어보다 못하다. Folderss에서 `.md`를 여는 대부분은 읽기일 가능성이 높다.
- 사이드카(`문서.md.meta.json`)가 Folderss 파일 작업(이름 바꾸기·이동·복사·삭제)에서 아직 따라가지 않는다(#78 남은 일). 기본값이 되면 속성을 쓴 문서가 늘어 문제가 커진다.
- Mermaid·원격 이미지 자동 표시가 없어 문서에 따라 정보가 빠져 보인다.

제안: 지금은 기본 매핑(`.md` → Markdown)과 설정 기본값(`매핑을 따름`)을 유지한다. #76(서식 WYSIWYG)과 최소 변경 저장(또는 저장 전 정리 범위 안내), #78 사이드카 파일 작업이 끝난 뒤
다시 검토하고, 그때도 첫 단계는 설정 기본값을 `블록 편집`으로 바꾸는 쪽(매핑은 그대로 → 되돌리기 쉬움)을 권한다.

## 변경 이력

- 2026-11-01: 구현, 앱·테스트 프로젝트 빌드 확인(오류 0). 단위 테스트·하네스 실행은 병합 후 일괄 진행.
- 2026-11-01: 병합 후 하네스 53개·단위 테스트 735개(실패 0) 통과, 기능 수정 없음 (Ready for Verification, 사용자 확인 대기)

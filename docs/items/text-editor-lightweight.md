# Text 뷰어를 가벼운 텍스트 편집기로 (`builtin:text`)

- 상태: Done — master 머지 확인 (이전: Verified (사용자 실제 앱 확인 완료))
- 상태 전환일: 2026-11-07
- 커밋: `0478a22`(편집기 교체·.txt/.log 기본 연결), `a7c8d4a`(툴바 인코딩 as → to)
- 착수일: 2026-11-07
- 이슈: 없음

## 요구사항

- 사용자 요청: "일반 텍스트 편집기를 좀 더 개선하고 싶어" → "가볍고 빠르지만 사용하기 편한 기능으로", "현재 기능과 추후 기능을 비교 검토하기 위해 현재 성능을 먼저 확인".
- 대상: 읽기 전용 Text 뷰어(`builtin:text`)를 편집기로 바꾼다(Monaco 뷰어는 그대로).
- 측정 후 사용자 결정: "제일 좋은 성능 편집기를 기본 text 편집기로 도입" → `.txt`·`.log` 기본 매핑을 이 편집기로(아래 "기본 매핑").
- 고른 기능: 찾기/바꾸기(Ctrl+F/H) · 인코딩 감지·선택 · 줄 바꿈·글꼴 크기·줄 이동 · 큰 파일 성능.

## 설계

### 엔진: WebView(highlight.js) → AvalonEdit(WPF)

| 안 | 장점 | 단점 |
|---|---|---|
| (가) 지금 WebView 페이지에 편집·가상 스크롤·찾기를 직접 구현 | 의존성 추가 없음 | 탭마다 WebView2 프로세스(수백 MB)·초기화 지연이 그대로, 가상 스크롤 편집기를 새로 만들어야 함 |
| (나) Monaco 재사용 | 기능 많음 | "가볍고 빠름"과 반대(이미 따로 있음) |
| (다) AvalonEdit 6.3 (NuGet, MIT) | WPF 안에서 그려 프로세스 없음, 줄 가상화·rope 문서·실행 취소·사각 선택·구문 강조 내장 | 의존성 1개 추가, 기본 구문 색이 밝은 배경용 |

**(다)를 택했다.** 측정에서 현재판의 비용 대부분이 WebView2(프로세스 7개, 1MB에 메모리 +370~700MB)였다.

### 구성

- `Viewers/TextViewer.xaml/.cs` — 본체. `IViewerToolbarProvider`(탭 툴바: 저장·줄 바꿈·공백 표시·단축키 ⓘ), `IViewerShortcutHandler`, `IViewerActivationAware`.
  - 읽기: `Task.Run`에서 바이트 읽기(공유 읽기 — 쓰는 중인 로그도 열림) → 인코딩 감지 → `TextDocument` 생성 → `SetOwnerThread(UI)`. `_loadVersion`으로 늦게 끝난 결과는 버림.
  - 저장: 읽은 인코딩(BOM 포함)으로 바이트를 만들어 `SettingsFile.Write`(임시 파일 후 교체). CP949처럼 글자가 사라지는 인코딩이면 저장 전에 `예: UTF-8로 / 아니요: 그대로('?') / 취소`.
  - 수정 여부 = `!UndoStack.IsOriginalFile || 저장 인코딩 변경`.
  - 상태 표시줄: 줄·열·선택 길이·전체 줄, 상태(수정됨·읽기 전용·외부 변경 반영 시각), 확대 비율(누르면 기본), 언어, `CRLF/LF ▾`, `인코딩 ▾`(다시 열기 / 저장 인코딩).
  - 한도: 8MB 넘으면 구문 강조 끔, 256MB 넘으면 열지 않음(안내).
- `Viewers/TextViewer.Find.cs` — 찾기/바꾸기·줄 이동. 입력하는 대로 찾기(입력이 멈추면 백그라운드에서 전체 검색, 찾기를 연 위치 다음 결과로 이동, 포커스는 입력란 유지). F3·바꾸기는 결과를 동기로 최신화. 모두 바꾸기는 `BeginUpdate/EndUpdate` 한 묶음(Ctrl+Z 한 번).
- `Viewers/TextViewer.External.cs` — 외부 변경. SHA-256 비교(원문 사본을 들지 않음), 블록 편집기와 같은 확인 규칙. "이 인코딩으로 다시 열기"로 지정한 인코딩은 반영 때도 유지.
- `Viewers/TextEditorRendering.cs` — `ThemedHighlightingColorizer`(어두운 테마에서 그릴 때만 구문 색 밝기 L≥0.66, 공유 정의는 고치지 않음), `SearchResultRenderer`(보이는 줄의 결과만 이진 탐색으로 그림).
- `Services/TextFileEncoding.cs` — 감지(BOM → 엄격 UTF-8 → CP949), 지정 인코딩으로 읽기, 저장 손실 글자 찾기, 줄 끝 감지·변환.
- `Services/TextSearch.cs` — 조건(대소문자·단어 단위·정규식)을 정규식 하나로. 결과 10만 개·정규식 2초 상한. 바꾸기는 전체 글에서 그 위치부터 맞춰 lookbehind·단어 경계 문맥을 지킴. 정규식 바꾸기는 `$1`·`\n`·`\t`.
- `Services/TextEditorSettings.cs` — `%LOCALAPPDATA%\Folderss\text-editor.json`(줄 바꿈·글꼴 크기·공백 표시). 편의 설정이라 읽기·저장 실패는 Trace만(Monaco 언어 기억과 같은 처리).

### 결정·주의

- 전역 단축키와 겹침: `ShowSearch`(Ctrl+F) → 문서 안 찾기. `SwitchPaneLeft/Right`(Ctrl+Shift+←/→) → 편집기·찾기 입력란에 포커스가 있으면 단어 단위 선택(`EditingCommands.SelectLeft/RightByWord`). 이전 Text 뷰어는 키 처리가 없었다.
- AvalonEdit 주소 Ctrl+클릭은 .NET에서 셸 실행 없이 시도해 실패하므로 `EnableHyperlinks=false`.
- `Properties/AssemblyInfo.cs`에 `[assembly: SupportedOSPlatform("windows7.0")]` 추가 — `GenerateAssemblyInfo=false`라 SDK 기본 표시가 빠져 AvalonEdit 호출마다 CA1416 경고가 났다.
- `TextFileEncoding`의 정적 초기화식이 정적 생성자(공급자 등록)보다 먼저 실행돼 CP949 생성이 실패하던 것을 스모크 실행에서 발견해 고쳤다. 테스트는 공급자를 따로 등록하지 않도록 바꿨다(같은 프로세스의 다른 테스트가 먼저 등록하면 여전히 가려질 수 있음).
- `Viewers/Resources/text-app.html` 삭제(이제 안 씀). `highlight.min.js`·`themes/hljs-*.css`는 Markdown 뷰어가 계속 쓴다.

### 기본 매핑: `.txt`·`.log` → Text 편집기

- 이전에는 `.txt`·`.log`가 기본 매핑이 아니었다(`LegacyDefaultMappings`는 옛 설정 파일 정리용 표일 뿐) — Windows 기본 프로그램(메모장 등)으로 열렸다.
  작업 중 README에 "기본 `.txt`·`.log`"라고 잘못 적었다가 바로잡았다.
- `ViewerConfigService.DefaultMappings`에 `.txt`·`.log` → `builtin:text` 추가.
- 함정: `IsLegacyFullDump`는 "현재 기본 매핑과 같은 항목이 있으면 옛 전체 덤프"로 판별했다. `.txt → Text`가 기본이 되면 사용자가 직접 고른 `.txt → Text`가 든
  버전 없는 재정의 파일을 덤프로 오판해 같은 파일의 `.sql → Monaco` 같은 선택을 버린다 → 판별 표지를 그 시절 기본 매핑(`LegacyDumpMarkers`: .md·.markdown·.json·.jsonl·.xml)으로 고정.
- 메모장으로 열고 싶으면 `.txt`를 `System Default`로 바꾸면 재정의로 저장된다.
- 테스트: `Load_MissingFile_UsesDefaultsOnly`(.txt·.log → text, .ini → 없음), `Load_UnversionedOverridesWithTxtText_IsNotMistakenForLegacyDump`, `SetMapping_SystemDefaultForTxt_IsKeptAsOverride`.
  "기본 매핑 없는 확장자" 예는 `.txt` 대신 `.ini`로 바꿈.

### 툴바 인코딩 변환 (as → to)

- 사용자 요청: "현재 열린 문서의 인코딩 정보를 표시하고, 저장 버튼 왼쪽에서 as 인코딩에서 to 인코딩으로 변환할 수 있게".
- 탭 툴바 저장 버튼 왼쪽에 `인코딩 [as ▾] → [to ▾]`.
  - as = 읽은 인코딩(자동 감지 또는 직접 지정, 툴팁에 구분 표시). 바꾸면 그 인코딩으로 다시 읽는다(저장 안 한 변경이 있으면 확인, 취소하면 원래 값으로 돌아감). 다시 읽으면 to도 같은 값이 된다.
  - to = 저장할 인코딩. as와 다르면 굵게 표시·툴팁 "저장하면 A → B로 변환", 수정됨 상태가 되고 저장할 때 변환한다. 읽기 전용 파일은 to를 끈다.
  - 예: BOM 없는 CP949 문서를 UTF-8로 바꾸기 = as가 자동으로 CP949 → to를 UTF-8 → 저장.
- 상태 표시줄 인코딩 메뉴(다시 열기 / 저장할 인코딩)와 같은 동작이며 `SyncEncodingUi`가 두 곳을 맞춘다.
- 확인: 측정기로 CP949(BOM 없음)·UTF-8 BOM·UTF-8 파일을 열어 툴바 값이 각각 `CP949 → CP949`, `UTF-8 BOM → UTF-8 BOM`, `UTF-8 → UTF-8`.

## 성능 비교

측정기: `Bench.exe`(scratchpad, 저장소 밖) — `ViewerConfigService.CreateViewer("builtin:text")`를 1200×800 창에 넣고 `Load`부터 화면에 그려질 때까지(WebView: 내용 후 두 프레임, WPF: 문서 채운 뒤 ApplicationIdle)를 잰다. 파일마다 새 프로세스(첫 탭), 메모리는 자기+자손 프로세스 Private Bytes 증가분. Release 빌드, 같은 PC.
파일: 로그(한글 섞인 110자 줄)와 저장소 `Services/*.cs`를 이어 붙인 코드.

| 파일 | 크기 | 현재판 열기 | 새 편집기 열기 | 현재판 메모리 | 새 편집기 메모리 | 새 편집기 처음↔끝 10회 |
|---|---:|---:|---:|---:|---:|---:|
| code-01mb.cs | 1.0 MB | 1,638 ms | 322 ms | +377 MB | +51 MB | 305 ms |
| log-01mb.log | 1.0 MB | 2,965 ms | 293 ms | +705 MB | +50 MB | 241 ms |
| code-05mb.cs | 5.0 MB | 3,853 ms | 359 ms | +1,037 MB | +106 MB | 302 ms |
| log-10mb.log | 10.3 MB | 300초 안에 안 열림(3회) | 404 ms | – | +122 MB | 265 ms |
| log-50mb.log | 51.4 MB | 미측정(중단) | 1,409 ms | – | +392 MB | 247 ms |
| longline-02mb.json (한 줄 220만 자) | 2.2 MB | 미측정 | 272 ms | – | +45 MB | 8 ms |
| lines20k-x113.txt (2만 자 줄 113개) | 2.2 MB | 미측정 | 869 ms | – | +169 MB | 4,939 ms |

모두 3회 중앙값. 현재판(WebView2)은 프로세스 7개, 새 편집기는 1개. 현재판 끝으로 스크롤 30 ms 안팎, 처음↔끝 10회 약 335 ms.
현재판 전체 측정은 시스템 메모리 부족으로 Claude Code가 중단했다 — 10MB 로그에서 WebView가 메모리를 크게 쓴 것으로 보인다(재측정하지 않음).

### 긴 줄(가로 가상화 없음) 대응

웹 조사(AvaloniaEdit PR #172, Stack Overflow)대로 AvalonEdit는 아주 긴 줄에 약하다. 처음 측정에서 한 줄 220만 자 JSON이 3회 모두 300초 안에 열리지 않았다(측정이 멈춘 것처럼 보임).

| 같은 2.2MB 내용 | 고치기 전 | 고친 뒤 |
|---|---:|---:|
| 한 줄, JSON 구문 강조 | 300초 넘음 | 열기 0.27 s, +45 MB |
| 한 줄, 강조 없음(.txt) | 열기 18.9 s, +637 MB | 열기 0.27 s, +56 MB |
| 20만 자 줄 11개 | 열기 2.0 s, 처음↔끝 10회 12.3 s | 0.50 s, 1.0 s |
| 2만 자 줄 113개 | 열기 1.2 s, 처음↔끝 10회 6.8 s | 0.87 s, 4.9 s |

- 원인 둘: 구문 강조가 줄 전체를 정규식에 넣음, WPF 글자 배치가 화면 밖까지 줄 전체를 함.
- 대응(VS Code `stopRenderingLineAfter`·긴 줄 토큰화 생략과 같은 방식): `LongLineElementGenerator`가 10,000자 뒤를 "… (N자 더)" 요소 하나로 대신 그림(문서는 그대로 — 저장·찾기·복사는 줄 전체). 읽을 때 `TextFileEncoding.MaxLineLength`로 재서 긴 줄이 있으면 구문 강조를 끄고 상태 표시줄에 표시.
- 남은 한계: 2만 자 줄이 화면을 가득 채운 문서에서 크게 이동하면 한 번에 약 0.5초. 한도를 5,000자로 낮춰도 같아(5.8 s) 그린 글자 수가 원인이 아니다 — 한도는 10,000자로 둠.
- 같은 조사에서 나온 "큰 선택 복사가 몇 분씩 멈춤"(붙여넣기 서식용 HTML을 만들며 다시 강조) 대응: `DataObject.AddSettingDataHandler`로 25만 자가 넘는 선택이나 강조가 없을 때는 HTML 형식을 빼고 일반 텍스트만 넣음. 수동 확인 필요.

## 검증

- [x] `dotnet build .\Folderss.sln -c Debug` — Exit 0, 오류 0 (경고 7은 기존 코드)
- [x] `dotnet test tests/Folderss.SearchTests --filter "TextEditorTests|ViewerConfig|ThemeFileTests"` — 63개 통과
- [ ] 전체 테스트(`dotnet test tests/Folderss.SearchTests`) — 15분 넘게 걸려 중단, 미확인
- [x] 측정기 스모크: 새 편집기로 1MB 로그 열기·끝으로 스크롤·처음↔끝 이동
- [x] 비교 측정: 새 편집기 7개 파일 × 3회(위 표)
- [x] 실제 앱에서 수동 확인(사용자, 2026-11-07 "확인 완료"): 편집·Ctrl+S(UTF-8/BOM/CP949 파일), 수정 표시·닫기 확인, 찾기/바꾸기·정규식·모두 바꾸기 후 Ctrl+Z, Ctrl+G, 인코딩 다시 열기·저장 인코딩, CRLF↔LF, 줄 바꿈·공백 표시·Ctrl+휠 기억, 외부 변경 확인, 읽기 전용 파일, 밝은/어두운 테마 구문 색, Ctrl+Shift+←/→ 선택

## 남은 제안(이번 범위 밖)

- 계속 늘어나는 로그는 외부 변경 확인이 반복된다(다른 뷰어와 같은 규칙). 필요하면 "끝 따라가기(tail)" 모드.
- 탭 툴바에 언어(구문 강조) 선택 — 지금은 확장자로만 정한다.

# 블록 편집기 3단계: 블록 종류·미디어 (이미지 붙여넣기·목차·수식 등) (#77)

- 상태: Done
- 사용자 확인: 확인 (2026-10-29)
- 상태 전환일: 2026-10-29

## 요구사항

이슈 #77(블록 편집기 3단계: 블록 종류·미디어) 중 사용자와 합의한 **Phase 1 범위**를 먼저 구현한다(다단 컬럼 레이아웃·Mermaid 미리보기·페이지 링크 블록(#74 연동)은 Phase 2로 분리).

### 1. 이미지 블록 보강 (5종)
- **클립보드 이미지 붙여넣기(`Ctrl+V`)**: 편집 중 클립보드에 이미지가 있으면 문서 옆 `images/<문서이름>-<타임스탬프>.png`로 저장하고 `BlockKind.Image` 블록을 즉시 삽입한다(`MarkdownViewer.SavePastedImage` 규칙 공유).
- **탐색기/폴더 패널 드래그 앤 드롭**:
  - 이미지 파일(`.png`, `.jpg`, `.jpeg`, `.gif`, `.bmp`, `.webp`, `.ico`, `.tiff`) 드롭 시: 문서 폴더 안이면 상대 경로로 삽입하고, 문서 폴더 밖이면 선택 대화상자(`GitDialogBase` 상속: `images/ 폴더로 복사 후 삽입 (권장)` / `상대 경로로 참조` / `절대 경로로 참조`)로 처리 방식을 고르게 한다.
  - 일반 파일 드롭 시: 파일 첨부 블록(`BlockKind.File`)으로 삽입한다.
- **크기 조절 핸들 + 정렬**:
  - 이미지 오른쪽 가장자리 드래그 핸들로 너비(px, 80~1200)를 조절하고 `Attributes["width"]`에 기록한다.
  - 정렬 메뉴/버튼(`좌측 정렬`, `가운데 정렬`, `우측 정렬`, `원래 크기`)으로 `Attributes["align"]`(`"left"` | `"center"` | `"right"`)을 설정한다.
  - Markdown 밖 속성이므로 #78 원칙에 따라 사이드카(`문서.md.meta.json`)에만 저장한다.
- **원격(`http://`, `https://`) 이미지 표시**:
  - `RemoteContentPolicy.RouteImage(uri, NetworkSettingsService.Current)`를 거쳐 기본 차단(`ShowRemoteImages == false`)을 유지한다.
  - 차단 상태에서는 안내 문구와 `"이번만 불러오기"` 버튼을 표시하고, 허용 상태(또는 이번만 불러오기 클릭 시)에서는 백그라운드 `HttpClient`로 내려받아 디코딩·캐시 후 표시한다.
- **클릭 동작**: 이미지 블록 도구 모음/우클릭 메뉴에서 `"원본 크기로 보기"`(모달 창) 및 `"탐색기에서 열기"`(`explorer.exe /select`)를 지원한다.

### 2. 추가 블록 종류 (Phase 1 — 6종)
- **토글 제목 1~3 (`/토글제목1`~`3`, `/th1`~`3`)**:
  - Markdown 호환성을 위해 `<details><summary>제목</summary>` 형식을 유지하고, 제목 단계는 `Attributes["heading"]` = `"1"` | `"2"` | `"3"`으로 사이드카에 저장한다.
  - 접기/펼치기 화살표와 H1/H2/H3 글꼴 크기(22/18/15pt, 굵게)를 함께 적용한다.
- **목차 블록 (`BlockKind.Toc`, `/목차`, `/toc`)**:
  - 문서 내 `Heading1`~`Heading3` 및 토글 제목(`Toggle` + `Attributes["heading"]`)을 자동 수집해 들여쓰기된 목차 목록으로 렌더링한다.
  - 항목 클릭 시 해당 제목 블록으로 스크롤 및 포커스 이동한다.
  - Markdown에는 `[TOC]`로 직렬화하고 `[TOC]` 또는 `<!-- toc -->`를 파싱한다.
- **수식 블록 (`BlockKind.Math`, `/수식`, `/math`, `$$`)**:
  - 블록 수식(`$$\n...\n$$` 또는 `$$ ... $$`)과 인라인 수식(`$...$`)을 지원한다.
  - 외부 NuGet 의존성 없이 순수 C# WPF 수식 렌더러(`BlockMathRenderer`)로 비편집 시 분수(`\frac{a}{b}`), 위/아래 첨자(`^{...}`, `_{...}`), 제곱근(`\sqrt{...}`), 그리스 문자(`\alpha`~`\omega`), 연산 기호(`\sum`, `\int`, `\pm`, `\times`, `\div`, `\neq`, `\leq`, `\geq`, `\rightarrow`, `\infty` 등)를 변환해 표시하고, 클릭/포커스 시 원본 LaTeX `TextBox`로 편집한다.
- **콜아웃 아이콘 선택기**:
  - 콜아웃 블록의 아이콘을 클릭하면 이모지 팔레트 팝업(자주 쓰는 이모지 24종 + 기본 유형 `NOTE`/`TIP`/`IMPORTANT`/`WARNING`/`CAUTION` + 직접 입력)을 열어 아이콘을 바꾼다.
  - 사용자 지정 이모지는 `Attributes["icon"]`으로 사이드카에 저장한다.
- **파일 첨부 블록 (`BlockKind.File`, `/파일`, `/file`)**:
  - 파일 아이콘, 파일 이름, 크기(사람이 읽기 쉬운 단위), `"열기"` 버튼(`FileOperationService.IsExecutable` 실행 파일 경고 포함), `"폴더 열기"` 버튼(`explorer.exe /select`)이 있는 카드로 렌더링한다.
  - Markdown에는 `[📎 이름](경로)` 형식으로 직렬화·파싱하여 사이드카 없이도 순수 Markdown 링크 호환성과 왕복(round-trip)을 보장한다.
- **북마크 블록 (`BlockKind.Bookmark`, `/북마크`, `/bookmark`)**:
  - URL 카드 형태로 제목(`Text` 또는 `Attributes["title"]`), 설명(`Attributes["description"]`), URL(`Source`), `"브라우저에서 열기"` 버튼, `"미리보기 불러오기"` 버튼을 표시한다.
  - 원격 접속 정책에 따라 자동으로 외부 요청을 보내지 않고, 사용자가 `"미리보기 불러오기"`를 눌렀을 때만 `NetworkSettingsService.Current` 정책 범위 안에서 `<title>` 및 `<meta name="description">`을 비동기로 가져와 `Attributes["title"]`, `Attributes["description"]`에 저장한다.
  - Markdown에는 `[🔖 제목](URL)` 형식으로 직렬화·파싱한다.

---

## 원인 분석 또는 설계

### 1. REA(`reverse-engineer-anything`) 기반 Notion Desktop 구현 분석
로컬에 설치된 Notion Desktop(`C:\Users\KYJ\AppData\Local\Programs\Notion\resources\app.asar` 및 `%APPDATA%\Notion\notion.db`)을 REA(`rea-agents@6.7.0`)로 정적·데이터베이스 역공학 분석하여 블록 데이터 모델과 미디어 처리 구조를 확인했다.

- **증거 번들 (Evidence IDs)**:
  - `ev_a1a75b3c0fb9bda1315d998238fc3f64d74e81ddd1d9091e3b0997890aca8575`: `Notion.exe` / `resources/app.asar` 아티팩트 인벤토리 확인
  - `ev_960eefd5ad31bd63e5806b64b5c52f4fdd1177046f2ebcd86362d11188fd4914`: `app.asar` 전체 정적 재구성 (JS 모듈 2,049개, AST 노드 921,001개, ContextBridge 노출 14개, IPC 채널 20개)
  - `ev_764293cd5caf9d774bf01314310f7aec2ab79b4edafda92b95c7bc6318866fab` / `ev_d3191e3eefddc2aef7c3144370950d6c6112aa7e51c839fc7f196b0b2544eba5`: `notion.db` SQLite 스키마 및 `block` 테이블 레코드 샘플링

- **Notion의 핵심 블록 저장 구조 (`block` 테이블, 47개 컬럼)**:
  - Notion은 블록의 **의미론적 본문(`type` + `properties` JSON)**과 **시각적·배치 속성(`format` JSON)**을 엄격히 분리한다.
  - 실제 `notion.db`에서 관찰된 레코드(`ev_d3191e3e...`):
    - `callout` 블록(`6c5e1d68-...`): `properties`에는 `{"title":[["Notion 팁: ",[["b"]]], ...]}` 텍스트만 두고, 아이콘과 배경색은 `format` JSON에 `{"page_icon":"💡","block_color":"gray_background"}`로 분리 저장한다.
    - 미디어/레이아웃 블록: `file_ids` 배열로 첨부 리소스를 추적하고, 폭·비율·정렬·아이콘(`block_width`, `block_alignment`, `page_icon`, `column_ratio`)은 모두 `format` JSON에 저장한다.
  - 렌더러 프로세스는 메인/유틸리티 프로세스(`SqliteServer.js`, `__mediaApi`)와 분리되어 UI 스레드 블로킹 없이 미디어를 비동기 로드한다.

### 2. Folderss 아키텍처 매핑 설계 (`BlockMarkdown` + `BlockSidecar`)
Notion의 `properties` vs `format` 분리 구조를 Folderss의 **순수 Markdown(`문서.md`) vs 사이드카(`문서.md.meta.json`, `EditorBlock.Attributes`)** 구조에 1:1 대응시킨다(#78 원칙 준수):

| 개념 | Notion (`notion.db` `block` 테이블) | Folderss (`BlockEditor`) |
|------|-----------------------------------|--------------------------|
| 블록 종류 | `block.type` (`callout`, `equation`, `table_of_contents`, `file`, `bookmark`, `image`, `toggle`) | `EditorBlock.Kind` (`Callout`, `Math`, `Toc`, `File`, `Bookmark`, `Image`, `Toggle`) |
| 본문/리소스 경로 | `block.properties` (`title`, `source`, `caption`) | `문서.md` (`EditorBlock.Text`, `EditorBlock.Source`) |
| 시각·배치 속성 | `block.format` (`block_width`, `block_alignment`, `page_icon`, `toggleable`, `block_color`) | `문서.md.meta.json` (`EditorBlock.Attributes`: `width`, `align`, `icon`, `heading`, `title`, `description`, `color`, `bg`) |
| 미디어/네트워크 I/O | `__mediaApi` / 비동기 프리페치 | `BlockImageLoader` (백그라운드 디코딩·원격 다운로드 캐시) + `RemoteContentPolicy` |

---

## 구현 내용

1. **블록 모델 확장 (`Models/EditorBlock.cs`)**:
   - `BlockKind`에 `Toc`, `Math`, `File`, `Bookmark` 4종을 추가하고 `EditorBlock.IsVerbatim`에 `BlockKind.Math`를 포함했다.
   - Notion의 `block.format`에 대응하는 사이드카 속성 상수(`AttrWidth = "width"`, `AttrAlign = "align"`, `AttrHeading = "heading"`, `AttrIcon = "icon"`, `AttrTitle = "title"`, `AttrDescription = "description"`, `AttrSize = "size"`)와 `GetToggleHeadingLevel()`, `GetHeadingLevel()` 헬퍼를 추가했다.
2. **Markdown 파서·직렬화 및 사이드카 해시 보강 (`Services/BlockMarkdown.cs`, `Services/BlockSidecar.cs`)**:
   - `[TOC]` / `<!-- toc -->` ↔ `BlockKind.Toc`
   - 한 줄 `$$ ... $$` 및 여러 줄 `$$\n...\n$$` ↔ `BlockKind.Math`
   - `[📎 이름](경로)` ↔ `BlockKind.File`, `[🔖 제목](URL)` ↔ `BlockKind.Bookmark`
   - 문단 앞 입력 단축(`$$`, `$$ `, `[TOC] `, `[toc] `)을 `TryMatchShortcut`에 추가했다.
   - `BlockSidecar.Hash`에서 `Image`뿐 아니라 `File`·`Bookmark` 블록도 `Source`를 해시에 포함하도록 확장했다.
3. **비동기 원격 이미지 로더 및 북마크 메타데이터 조회 (`Controls/BlockImageLoader.cs`)**:
   - `RemoteContentPolicy.ShouldAllowCertificateError`와 `NetworkSettingsService.Current`를 사용하는 `RemoteClient`(`HttpClient`, 15초 타임아웃, 20MB 제한)를 추가하고, `LoadRemote`로 원격 이미지를 백그라운드 디코딩·LRU 캐시하도록 구현했다.
   - 북마크 카드에서 수동 실행하는 `FetchPageMetaAsync(Uri)`로 웹페이지 `<title>` 및 `<meta name="description">`을 최대 128KB 범위 내에서 가져오도록 구현했다.
4. **미디어·파일 드롭·대화상자·수식 렌더러 (`Controls/BlockEditor.Media.cs`)**:
   - `TryPasteClipboardImage` / `SaveBitmapToImagesFolder`: `Ctrl+V` 시 클립보드 이미지를 `images/<문서이름>-<타임스탬프>.png`로 저장 후 이미지 블록으로 삽입.
   - `HandleDroppedFiles`: 탐색기·폴더 패널에서 드롭한 파일 중 이미지는 `BlockKind.Image`(문서 폴더 밖 이미지는 `BlockImageDropDialog`로 `images/` 복사·상대 경로·절대 경로 중 선택), 일반 파일은 파일 크기를 기록한 `BlockKind.File`로 삽입.
   - `BlockBookmarkDialog`, `BlockImagePreviewWindow`(원본 해상도·창 맞춤 토글·탐색기 열기 모달 창), `OpenLocalTarget`(`FileOperationService.IsExecutable` 경고 포함), `RevealInExplorer` 구현.
   - `BlockMathRenderer`: 외부 NuGet 없이 분수(`\frac{a}{b}`), 제곱근(`\sqrt[n]{x}`), 위·아래 첨자(`^{...}`, `_{...}`), 그리스 문자, 수학 연산 기호를 WPF `Inline` 트리로 렌더링하고 문단 내 인라인 수식(`$...$`)도 함께 렌더링하도록 구현.
5. **블록 행 UI·가상화 패널·뷰어 연동 (`Controls/BlockEditor.Row.cs`, `BlockEditor.Panel.cs`, `BlockEditor.cs`, `BlockEditor.Table.cs`, `Viewers/BlockEditorViewer.cs`)**:
   - 이미지 우측 끝 `Thumb` 리사이즈 핸들(`Attributes["width"]`), 정렬 툴바/메뉴(`Attributes["align"]`), 더블클릭/버튼 원본 크기 보기, 수동 원격 이미지 로드(`이번만 불러오기`) 구현.
   - 토글 제목 1~3(`/토글제목1~3`, `/th1~3`), 목차(`RebuildTocContent`), 수식(비편집 시 수식 렌더링, 클릭/포커스 시 LaTeX 편집), 콜아웃 아이콘 선택 팝업(`ShowCalloutIconPicker`), 파일·북마크 카드(`RefreshMediaCard`) 및 `/` 슬래시 명령·블록 컨텍스트 메뉴 연동.
6. **하네스 검증에서 찾아 고친 문제 (Claude)**:
   - **편집 중 `Ctrl+V` 이미지 붙여넣기가 동작하지 않음**: 클립보드에 글자 없이 이미지만 있으면 TextBox가 `ApplicationCommands.Paste`를 실행 불가로 막아 `DataObject.Pasting`이 오지 않았다(그래서 `OnEditorPasting`의 이미지 분기는 호출될 수 없었음).
     입력란에 `CommandManager.AddPreviewCanExecuteHandler`/`AddPreviewExecutedHandler`를 달아 이미지만 든 클립보드면 명령을 먼저 받아 `TryPasteClipboardImage`로 넣고(`BlockEditor.CanPasteMedia`, 코드·원문·수식 블록 제외), 죽은 분기는 지웠다.
   - **복사한 이미지 파일 붙여넣기(사용자 제보)**: 끌어 놓기는 되는데 탐색기·폴더 패널에서 복사한 이미지 파일을 붙여넣으면 들어가지 않았다. 클립보드에 그림이 아니라 파일 목록(FileDrop)이 들어가므로
     탐색기 복사(파일 목록만)는 붙여넣기 불가, 폴더 패널 복사(파일 목록 + 이름 글자)는 파일 이름 글자만 붙었다. 파일 목록에 있는 파일이 있으면 끌어 놓기와 같이 `HandleDroppedFiles`로 넣는다(`PasteMedia`, 블록 선택 상태 `PasteAfterSelection`도 같음). 폴더만 복사한 경우는 기존처럼 글자 붙여넣기.
   - **문단의 금액 표기가 수식으로 보임**: `$5 ~ $10`이 `5 ~ 10` 수식으로 렌더되던 것을 Pandoc 규칙(여는 `$` 뒤·닫는 `$` 앞 공백 금지, 닫는 `$` 뒤 숫자 금지, `\$` 제외)으로 바꿨다(`BlockMathRenderer.FindInlineMath`).
   - **목차가 화면에 있을 때 제목 입력이 느림**: 글자마다 목차 항목을 전부 다시 만들어 제목 301개 문서에서 입력 1회 약 110ms. 제목 목록(블록·단계)이 그대로면 바뀐 항목 글자만 고치도록 해 약 11ms로 줄였다(`RebuildTocContent`·`SameTocHeadings`).

## 변경 파일

- `Folderss/Models/EditorBlock.cs`
- `Folderss/Services/BlockMarkdown.cs`
- `Folderss/Services/BlockSidecar.cs`
- `Folderss/Controls/BlockImageLoader.cs`
- `Folderss/Controls/BlockEditor.Media.cs` (신규)
- `Folderss/Controls/BlockEditor.Row.cs`
- `Folderss/Controls/BlockEditor.Panel.cs`
- `Folderss/Controls/BlockEditor.Table.cs`
- `Folderss/Controls/BlockEditor.cs`
- `Folderss/Controls/ElementTree.cs` (신규, 조상 탐색 공용 헬퍼)
- `Folderss/Controls/ConsolePanel.xaml.cs`
- `Folderss/Controls/FavoritesPanel.xaml.cs`
- `Folderss/Controls/FolderBrowser.xaml.cs`
- `Folderss/Controls/SearchPanel.xaml.cs`
- `Folderss/GitWindow.xaml.cs`
- `Folderss/MainWindow.xaml.cs`
- `Folderss/Viewers/BlockEditorViewer.cs`
- `tests/Folderss.SearchTests/BlockMarkdownTests.cs`
- `tests/Folderss.SearchTests/BlockSidecarTests.cs`
- `docs/architecture.md`
- `README.md`
- `docs/items/block-editor-stage3-media.md`
- `docs/block-editor-test.md` (신규 테스트 문서)
- `docs/block-editor-test.md.meta.json` (신규 사이드카 메타데이터)

## 검증

1. **수식 클릭 시 비정상 종료 버그 분석 및 해결 검증**:
   - **원인 분석**: `MainWindow.xaml.cs`의 `DockManager_PreviewMouseLeftButtonDown` 터널링 마우스 이벤트에서 `FindVisualAncestor<LayoutDocumentTabItem>(e.OriginalSource as DependencyObject)`를 호출함. 수식(`BlockMathRenderer`) 렌더링 결과물인 `TextBlock` 내 `Run`/`Span`은 `Visual`/`Visual3D`가 아닌 `FrameworkContentElement`이므로 `VisualTreeHelper.GetParent(Run)` 호출 시 `InvalidOperationException: 'System.Windows.Documents.Run'이(가) Visual 또는 Visual3D가 아닙니다.` 예외가 발생하여 프로세스가 즉시 비정상 종료됨.
   - **조치 내용**:
     - `MainWindow.FindVisualAncestor`, `ConsolePanel.FindVisualAncestor`, `FolderBrowser.FindAncestor`, `SearchPanel.FindAncestor`, `FavoritesPanel.FindAncestor`, `GitWindow`의 시각적 트리 탐색부에 `element is Visual || element is Visual3D` 검사를 추가하고, `FrameworkContentElement`일 경우 `fce.Parent`로 상위 요소를 안전하게 탐색하도록 방어 로직 적용.
     - 후속 정리(Claude 검토): 위 6곳에 복사된 탐색 코드를 공용 헬퍼 `Controls/ElementTree`(`GetParent`·`FindAncestor<T>`)로 합치고, 타입 검사 뒤라 필요 없던 빈 `catch { return null; }`를 없앴다. Visual이 아닌 요소는 `LogicalTreeHelper.GetParent` → `ContentOperations.GetParent` 순으로 올라간다. 임시 WPF 콘솔로 `Border > TextBlock > Span > Run`, 분수(`InlineUIContainer > StackPanel > TextBlock > Run`)에서 조상 탐색이 예외 없이 동작함을 확인.
     - `BlockEditor.Row.cs`에서 수식 디스플레이 클릭 시 부드럽게 원본 편집기(`_mainEditor`)로 포커스 전환되도록 이벤트 라우팅 보강.
2. **솔루션 빌드**:
   - `dotnet build .\Folderss.sln -c Debug` → **성공 (오류 0개, Exit: 0)**
3. **단위 테스트 (`tests/Folderss.SearchTests`)**:
   - `BlockMarkdownTests.Toc_Math_File_Bookmark_RoundTripAndParseVariants` 및 `Shortcut_MatchesNotionMarkdownTriggers` (`$$`, `[TOC] ` 포함) 통과
   - `BlockSidecarTests.MediaAndBlockAttributes_RoundTripThroughSidecar` (`heading`, `icon`, `width`, `align`, `size`, `title`, `description` 사이드카 왕복 검증) 통과
   - 전체 테스트 스위트 실행: `실패: 0, 통과: 457, 건너뜀: 4, 전체: 461, 기간: 1분 20초`
4. **WPF 하네스(스크래치, 실제 창에서 합성 키·마우스·클립보드)**:
   - 기존 회귀: 기능 74, 사이드카 16, 뷰어 5, 대용량 4개 항목 모두 통과.
   - 3단계 기능 70개 통과(복사한 파일 붙여넣기: 폴더 패널·탐색기 복사·블록 선택 상태, 글자 붙여넣기 유지 포함): `/목차`·`/수식`·`/th2`와 Markdown, 입력 단축 `$$`·`[TOC] `, 목차 항목(제목 + 토글 제목)·입력 시 갱신·클릭 이동,
     수식 렌더(분수·제곱근·그리스 문자)·`Run` 클릭 시 창 전체 조상 탐색 예외 없음·LaTeX 편집 전환·포커스 이동 시 다시 렌더, 문단 인라인 수식과 금액 표기 구분,
     `Ctrl+V` 이미지(`images/문서-….png` 저장·빈 문단 대체·Undo), 파일 드롭(폴더 안 이미지 상대 경로, 일반 파일 → 파일 블록·크기, 공백 경로 Markdown 왕복, 폴더 밖 이미지 `images/` 복사·이름 겹침 `-2`),
     이미지 핸들 끌기 → `width`·가운데 정렬 → `align`(Markdown 불변, 사이드카에만)·원래 크기·Undo, 원격 이미지(표시 끔 → 요청 없이 안내 + `이번만 불러오기`, 이 PC 주소 서버에서 내려받아 표시),
     콜아웃 이모지 선택 → `icon`(Markdown 불변), 파일·북마크 카드(이름·크기, 제목·주소, 자동 요청 없음), 토글 제목 1 글자 크기.
   - 결정(2026-10-29, 사용자): 블록 편집기의 원격 이미지는 네트워크 설정 "원격 이미지 표시"와 관계없이 지금처럼 `이번만 불러오기`로만 불러온다(문서를 열 때 외부 요청 없음). Markdown 뷰어와 다른 동작은 그대로 둔다. GitHub #77 갱신은 하지 않음.
5. **대용량 성능(Debug, 11,424줄·블록 9,862개 — 표 300개 + 2,000행 표, 이미지 60개)**:
   - 기존 문서: 열기 195ms, 문단 입력 중앙 9.5ms, 표 셀 입력 4.5ms, 표 Enter 행 추가 33.6ms, 2,000행 표 끝 행 추가 31.4ms, 실행 취소 20ms, 120px 스크롤 8.8ms, 한 화면 이동 29ms, 저장 직렬화 9.6ms — 2단계 측정과 같은 수준.
   - 목차 + 제목 301개 + 섹션마다 인라인 수식: 열기 244ms, 목차가 화면에 있을 때 제목 입력 중앙 10.7ms(수정 전 109.8ms), 화면 밖 9.5ms, 수식 든 문단 입력 6.7ms, 문단 Enter 45ms, 실행 취소 25ms, 120px 스크롤 7.9ms, 한 화면 이동 41~65ms, 목차 마지막 항목 클릭 → 문서 끝 제목 이동.

## 변경 이력

- 2026-10-29: REA(`reverse-engineer-anything`)를 활용한 Notion Desktop(`app.asar`, `notion.db`) 아키텍처 분석 및 #77 Phase 1 설계 수립, 작업 착수 (`In Progress`)
- 2026-10-29: #77 Phase 1(이미지 블록 보강 5종 + 추가 블록 6종) 구현, 빌드 및 단위 테스트(457건 통과) 완료 (`Ready for Verification`)
- 2026-10-29: 수식 클릭 시 `System.Windows.Documents.Run`이 `VisualTreeHelper.GetParent`에 전달되어 비정상 종료되던 크래시 버그 원인 규명 및 WPF Visual/ContentElement 조상 탐색 방어 로직 전역 적용 완료 (`Ready for Verification`)
- 2026-10-29: 조상 탐색 중복 코드 6곳을 `Controls/ElementTree`로 통합, 빈 catch 제거. 빌드 오류 0, 전체 테스트 457건 통과 (`Ready for Verification`)
- 2026-10-29: 사용자가 실행 화면에서 수식 클릭 등 크래시 경로를 확인, 이상 없음. #77 완료 기준(하네스 기능 검사·1만 줄 성능)이 남아 `Ready for Verification` 유지
- 2026-10-29: WPF 하네스(3단계 61개 + 기존 회귀 99개)·1만 줄 성능 측정. 편집 중 `Ctrl+V` 이미지 붙여넣기 불가, 금액 표기 수식 오인식, 목차 화면 안 제목 입력 지연(110ms) 수정. Phase 2(여러 열·페이지 링크·Mermaid)는 `block-editor-stage3-phase2.md`로 분리 (`Ready for Verification`)
- 2026-10-29: 사용자 결정 — Phase 1은 현재 구현 범위로 마무리, 원격 이미지는 수동 불러오기 유지 (`Ready for Verification`)
- 2026-10-29: 사용자 제보 — 복사한 이미지 파일 붙여넣기가 안 됨. 클립보드 파일 목록(탐색기·폴더 패널 복사)을 끌어 놓기와 같이 처리하도록 수정, 하네스 70개 통과 (`Ready for Verification`)
- 2026-10-29: 사용자가 실행 화면에서 붙여넣기 등 수정 사항 확인 완료 (`Done`). 남은 #77 항목은 `block-editor-stage3-phase2.md`

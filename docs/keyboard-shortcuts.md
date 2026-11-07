# Folderss 키보드 단축키 매핑표

## 범례

| 구분 | 의미 |
|------|------|
| **커스텀** | 사용자가 설정 창에서 변경 가능 (`KeyBindingService` 등록) |
| **하드코딩** | 코드에 고정되어 변경 불가 |
| **OS/WPF** | WPF `ApplicationCommands` 또는 OS 기본 동작 |

---

## 1. 사용자 정의 가능 단축키 (KeyBindingService)

설정 경로: `%LOCALAPPDATA%\Folderss\keybindings.xml`

| CommandId | 표시 이름 | 기본 키 | 처리 위치 | 동작 |
|-----------|-----------|---------|-----------|------|
| `Rename` | 이름 변경 | `F2` | MainWindow | 선택된 항목 이름 변경 (즐겨찾기 패널 포커스 시 즐겨찾기 이름 변경) |
| `Refresh` | 새로 고침 | `F5` | MainWindow | 양쪽 패널 새로 고침 |
| `Move` | 이동 | `F6` | MainWindow | 선택 항목을 반대 패널로 이동 |
| `Delete` | 삭제 | `Delete` | MainWindow | 휴지통 이동 (Shift 누르면 영구 삭제) |
| `RefreshAlt` | 새로 고침 (대체) | `Ctrl+R` | MainWindow | 양쪽 패널 새로 고침 (대체키) |
| `NewFolder` | 새 폴더 | `Ctrl+Shift+N` | MainWindow | 활성 패널에 새 폴더 생성 |
| `NewFile` | 새 파일 | `Ctrl+N` | MainWindow | 활성 패널에 새 파일 생성 |
| `AddPanel` | 폴더 패널 추가 | `Ctrl+T` | MainWindow | 새 폴더 브라우저 탭 추가 |
| `CopyClipboard` | 복사 (클립보드) | `Ctrl+C` | MainWindow | 선택 항목 클립보드 복사; 즐겨찾기 패널에서는 경로 복사 |
| `CopyFolderPath` | 경로 복사 | `Ctrl+Shift+C` | MainWindow → `FolderBrowser.CopyPathForShortcut` | 경로를 텍스트로 복사 (FileDrop 없음). 트리뷰 포커스: 선택한 폴더, 파일 목록 포커스: 선택 항목(목록 순서, 여러 개면 줄마다 하나, 없으면 현재 폴더). 형식은 `설정 > 경로 복사`(기본 전체 경로). ID는 예전 이름(폴더 경로 복사) 그대로 |
| `CutClipboard` | 잘라내기 | `Ctrl+X` | MainWindow | 선택 항목 잘라내기 |
| `PasteClipboard` | 붙여넣기 | `Ctrl+V` | MainWindow | 클립보드 항목 붙여넣기 |
| `NavigateBack` | 뒤로 | `Alt+←` | MainWindow | 활성 패널 히스토리 뒤로 |
| `NavigateForward` | 앞으로 | `Alt+→` | MainWindow | 활성 패널 히스토리 앞으로 |
| `NavigateUp` | 상위 폴더 | `Alt+↑` | MainWindow | 활성 패널 상위 폴더 이동 |
| `SwitchPaneLeft` | 왼쪽 패널 전환 | `Ctrl+Shift+←` | MainWindow | 이전 폴더 패널로 포커스 전환 |
| `SwitchPaneRight` | 오른쪽 패널 전환 | `Ctrl+Shift+→` | MainWindow | 다음 폴더 패널로 포커스 전환 |
| `ShowSearch` | 파일 검색 | `Ctrl+F` | MainWindow | 파일 검색 창 열기 (파일명·파일 내용 대상 체크, 동시 검색 가능, 와일드카드 패턴) |
| `PanelMaximize` | 패널 최대화 토글 | `F11` | MainWindow | 활성 패널/문서 최대화 토글 |
| `NextPerspective` | 다음 퍼스펙티브 (파일 ↔ 플러그인) | `Ctrl+F8` | MainWindow (`MainWindow.Perspectives.cs`) | 메인 창에 띄운 플러그인 퍼스펙티브가 있을 때만: 파일 → 열린 플러그인들 → 파일 순으로 전환. 플러그인 퍼스펙티브가 보이는 동안 다른 본체 단축키는 처리하지 않고 플러그인에 넘김 |
| `UndoFileOperation` | 실행 취소 (파일 작업) | `Ctrl+Z` | MainWindow | 폴더 패널 포커스(텍스트 입력 중 아님)일 때 메인 패널의 마지막 파일 작업(복사·이동·휴지통 삭제·이름 변경·새 폴더·새 파일)을 확인 창을 거쳐 되돌림. ⋯ 메뉴 `실행 취소: …`와 같은 명령(`MainWindow.UndoLastFileOperation`). 경로·필터 입력란에서는 텍스트 되돌리기, 뷰어·캡쳐 편집 창은 각자 처리 |

---

## 2. 하드코딩 단축키

### FolderBrowser (파일 목록)

| 키 | 동작 | 비고 |
|----|------|------|
| `Enter` | 선택된 항목 열기 (더블클릭과 동일) | 하드코딩 |
| `Backspace` | 상위 폴더로 이동 | 하드코딩 |

### FavoritesPanel (즐겨찾기 패널)

MainWindow에서 `Rename`, `CopyClipboard` KeyBindingService 매핑으로 통합 처리.

### SearchPanel (검색 패널)

| 키 | 동작 | 비고 |
|----|------|------|
| `Escape` | 검색 패널 닫기 | 하드코딩 |
| `Enter` | 검색 실행 | 하드코딩 (검색 입력란에서) |

### ConsolePanel (콘솔)

| 키 | 동작 | 비고 |
|----|------|------|
| `Escape` | 명령 입력란으로 포커스 | 하드코딩 |
| `Tab` | 터미널에 Tab 키 전달 | 하드코딩 |
| `Home`/`End` | 터미널에 네비게이션 키 전달 | 하드코딩 |

### FolderBrowser SearchBox (파일 필터링)

| 키 | 동작 | 비고 |
|----|------|------|
| `↓` | 검색 결과 파일 목록으로 포커스 이동 | 하드코딩 |

---

## 3. OS/WPF 기본 단축키

FolderBrowser에서 `ApplicationCommands` CommandBinding으로 등록:

| 키 | 동작 | 비고 |
|----|------|------|
| `Ctrl+C` | 파일/폴더 클립보드 복사 | WPF CommandBinding |
| `Ctrl+X` | 파일/폴더 잘라내기 | WPF CommandBinding |
| `Ctrl+V` | 클립보드 붙여넣기 | WPF CommandBinding |

---

## 4. 뷰어 단축키

### MarkdownViewer

| 키 | 동작 | 비고 |
|----|------|------|
| `Ctrl+F` | 마크다운 내 텍스트 검색 (find) | KeyBindingService `ShowSearch` 매핑 사용 |
| `Ctrl+S` | 저장 (Edit·Split·Preview 모두) | `markdown-app.html` 하드코딩 |
| `Tab` / `Shift+Tab` | 편집기: 공백 4칸 넣기, 여러 줄 선택 시 들여쓰기 / 내어쓰기 | `markdown-app.html` 하드코딩 |
| `Enter` | 편집기: 목록 줄 끝에서 다음 항목 표시(`-`, `1.`, `- [ ]`) 이어 쓰기, 빈 항목이면 목록 끝내기 | IME 조합 중에는 동작 안 함 |
| `Ctrl+V` (이미지) | 편집기: 클립보드 이미지를 문서 옆 `images/`에 저장하고 링크 삽입 | |

### MonacoViewer

| 키 | 동작 | 비고 |
|----|------|------|
| `Ctrl+F` | Monaco 내장 파일 내 검색(`actions.find`) 열기 | KeyBindingService `ShowSearch` 매핑 사용 |

Monaco 내장 단축키(찾아 바꾸기 `Ctrl+H`, 커맨드 팔레트 `F1` 등)는 WebView2 내에서 자체 처리.
단, `Ctrl+F`는 전역 `ShowSearch`와 겹치므로 `HandleShortcut`에서 가로채 `app.openFind()`로 전달한다 (MarkdownViewer와 동일한 패턴).

### BlockEditorViewer (블록 편집기, `builtin:blocks`)

편집 단축키 전체는 README 블록 편집기 절과 뷰어 툴바 `단축키 ⓘ` 참고. 아래는 찾기·링크(#75).

| 키 | 동작 | 비고 |
|----|------|------|
| `Ctrl+F` | 찾기 막대 열기 (선택한 한 줄 글자를 검색어로) | KeyBindingService `ShowSearch` 매핑 사용(`BlockEditorViewer.HandleShortcut`). 매핑을 바꿔도 편집기 안 `Ctrl+F`는 `BlockEditor.HandleFindKey`가 연다 |
| `Ctrl+H` | 찾기 + 바꾸기 줄 열기 | `BlockEditor.Find.cs` 하드코딩 |
| `F3` / `Shift+F3` | 다음 / 이전 결과 (찾기 막대가 닫혀 있어도) | 〃 |
| `Enter` / `Shift+Enter` | 찾기 입력란: 다음 / 이전 | 〃 |
| `Enter` / `Ctrl+Alt+Enter` | 바꾸기 입력란: 지금 결과 바꾸기 / 모두 바꾸기(실행 취소 한 단계) | 〃 |
| `Esc` | 찾기 막대 닫기, 지금 결과 입력란으로 커서 | 찾기 막대에 포커스가 있을 때만(편집기 `Esc` 블록 선택과 겹치지 않음) |
| `Ctrl+클릭` | 본문 링크 열기(`#제목`·`파일.md#제목` 이동, 실행 파일은 확인) | `BlockEditor.OnLinkPreviewMouseDown` → `BlockEditorViewer.OpenLink` |

찾기 막대 입력란에서 누른 키는 `Ctrl+S`(저장)를 빼고 편집기 키 처리(`Ctrl+Z` 실행 취소 등)로 넘기지 않는다 — 입력란 자체의 글자 되돌리기가 동작한다.

### TextViewer (일반 텍스트 편집기, `builtin:text`)

편집 키(`Ctrl+Z/Y`, `Ctrl+A`, `Tab`/`Shift+Tab` 들여쓰기, `Alt+드래그` 사각형 선택, 선택 없이 `Ctrl+C/X` 한 줄)는 AvalonEdit 기본 동작. 아래는 `TextViewer.OnPreviewKeyDown`·`TextViewer.Find.cs` 하드코딩.

| 키 | 동작 | 비고 |
|----|------|------|
| `Ctrl+F` | 찾기 막대 열기 (선택한 한 줄 글자를 검색어로) | KeyBindingService `ShowSearch` 매핑 사용(`TextViewer.HandleShortcut`). 매핑을 바꿔도 편집기 안 `Ctrl+F`는 열림 |
| `Ctrl+H` | 찾기 + 바꾸기 줄 열기 (읽기 전용 파일은 찾기만) | |
| `F3` / `Shift+F3` | 다음 / 이전 결과 (찾기 막대가 닫혀 있어도, 검색어가 없으면 찾기 막대) | |
| `Enter` / `Shift+Enter` | 찾기 입력란: 다음 / 이전 | |
| `Enter` / `Ctrl+Alt+Enter` | 바꾸기 입력란: 지금 결과 바꾸기 / 모두 바꾸기(실행 취소 한 단계) | |
| `Alt+C` / `Alt+W` / `Alt+R` | 찾기 막대: 대/소문자 · 단어 단위 · 정규식 켜고 끄기 | |
| `Ctrl+G` | 줄 이동 막대(`줄` 또는 `줄:열`) | |
| `Esc` | 찾기·줄 이동 막대 닫고 편집기로 | 막대가 열려 있을 때만 |
| `Ctrl+S` | 저장 | |
| `Ctrl+휠`, `Ctrl+=` / `Ctrl+-`, `Ctrl+0` | 글꼴 확대 / 축소, 기본 크기 | 모든 Text 탭에 기억(`text-editor.json`) |
| `Ctrl+Shift+←/→` | 단어 단위 선택 | KeyBindingService `SwitchPaneLeft/Right`와 겹침 — 편집기·찾기 입력란에 포커스가 있으면 `HandleShortcut`이 선택으로 처리(패널 전환 안 함) |

### BlockEditor (블록 편집기)

블록 편집기 키는 `Controls/BlockEditor.cs` `OnPreviewKeyDown` 한 곳에서 하드코딩으로 처리한다(전체 목록은 README 블록 편집기 절). 아래는 #75 "선택·이동"에서 추가한 키.

| 키 | 동작 | 처리 위치 |
|----|------|------|
| `Ctrl+Home` / `Ctrl+End` | 문서 첫·마지막 블록으로(편집 중이면 커서, 블록 선택 중이면 선택 이동) | `BlockEditor.Selection.cs` `HandleNavigationKey` |
| `PageUp` / `PageDown` | 한 화면(뷰포트 90%) 떨어진 블록으로, 화면도 같이 넘김 | 같음 |
| `Shift+` 위 키 | 지금 블록(선택 시작)부터 블록 선택 확장 | 같음 |
| `Shift+클릭` | 지금 블록부터 누른 블록까지 블록 선택(같은 블록 안이면 글자 선택) | `OnShiftClickPreview` |

#76 "줄 안 서식 WYSIWYG"에서 바뀐 키. 서식 블록(텍스트·제목·목록·할 일·토글·인용·콜아웃)은 서식 입력란(`RichBlockBox`)에서 서식을 직접 켜고 끈다.
표 셀(원문 입력란)에서는 예전처럼 Markdown 표식으로 감싼다. RichTextBox 기본 서식 명령(`Ctrl+U` 밑줄, `Ctrl+E/L/R/J` 정렬, `Ctrl+[`/`]` 글자 크기 등)은 `RichBlockBox`에서 꺼 두었고,
`Ctrl+U`는 편집기의 밑줄(사이드카 범위 속성 `marks`, #76 9번)로 다시 잡았다.

| 키 | 동작 | 처리 위치 |
|----|------|------|
| `Ctrl+B` / `Ctrl+I` / `Ctrl+E` / `Ctrl+Shift+S` | 굵게 / 기울임 / 코드 / 취소선 적용·해제(선택이 모두 그 서식이면 해제). 선택이 없으면 다음 입력 글자 서식(대기 서식 — 여러 키를 겹쳐 누를 수 있고, 커서 이동·클릭·블록 전환하면 취소, 한글 조합은 확정 뒤 적용). 실행 취소 한 단계 | `HandleEditorKey` → `FormatKey` → `BlockEditor.Inline.cs` `ToggleInlineFormat` (표 셀은 `WrapSelection`) |
| `Ctrl+U` | 밑줄 적용·해제(선택이 모두 밑줄이면 해제, 선택이 없으면 다음 입력 글자). Markdown에는 쓰지 않고 사이드카 `marks`에 저장. 실행 취소 한 단계. 표 셀·코드 블록에서는 아무것도 하지 않음 | `HandleEditorKey` → `BlockEditor.Inline.cs` `ToggleUnderline` |
| `Ctrl+K` | 링크 주소 팝업(선택 글자를 링크로, 커서가 링크 안이면 그 링크 고치기·열기·제거, 선택이 없으면 주소를 링크 글자로 넣기). 팝업 안 `Enter` 적용·`Esc` 취소 | `ShowLinkEditor` (표 셀은 `InsertLink`) |
| `Ctrl+Z` (서식 단축 직후) | 입력 중 `**글**` 등으로 바뀐 서식을 입력한 표식 글자로 되돌림(변환 직후 이어 쓰는 글자는 서식 없음 — 대기 서식으로 끔) | 변환을 따로 한 단계로 기록(`TryInlineInputRule` → `ApplyRuns`) |
| `Ctrl+클릭` | 서식 입력란·표시 글자의 링크 열기(링크 조각 또는 맨 URL) | `OnLinkPreviewMouseDown` → `RichLinkAt` / `BlockInlineRenderer.DisplayLinkAt` |

글자 색·배경색(형광펜)은 단축키 없이 서식 도구막대 `A▾`에서 고른다(`SetTextMarkColor`·`ClearTextMarkColors`).

미구현 제안: 마지막에 쓴 글자 색·형광펜 다시 칠하기 단축키(Notion `Ctrl+Shift+H`).

---

## 5. 버그 및 불일치 사항 (리뷰 결과)

| # | 제목 | 심각도 | 상태 |
|---|------|--------|------|
| BUG-1 | `Ctrl+X`/`Ctrl+V` 커스텀 매핑 미적용 | 중 | **수정 완료** |
| BUG-2 | `Ctrl+N` (새 파일) KeyBindingService 미등록 | 낮 | **수정 완료** |
| BUG-3 | MarkdownViewer `Ctrl+F` 하드코딩 | 낮 | **수정 완료** |
| BUG-4 | FavoritesPanel `F2`/`Ctrl+C` 하드코딩 | 중 | **수정 완료** |
| BUG-5 | `docs/architecture.md` 문서 불일치 | - | **수정 완료** |
| BUG-6 | MonacoViewer `Ctrl+F` 전역 검색에 가로채임 | 낮 | **수정 완료** |

### BUG-1: `Ctrl+X` / `Ctrl+V` 커스텀 매핑 미적용 — 수정 완료

- **증상**: `KeyBindingService`에 `CutClipboard`, `PasteClipboard`가 등록되어 있으나 `MainWindow.Window_PreviewKeyDown`에서 미사용
- **수정**: `FolderBrowser`에 `CopySelectedToClipboard()`, `CutSelectedToClipboard()` 공개 메서드 추가. `MainWindow`에서 `kb.Matches(e, "CutClipboard")`, `kb.Matches(e, "PasteClipboard")` 핸들러 추가. `CopyClipboard`도 FavoritesPanel 외 일반 파일 복사까지 처리하도록 확장

### BUG-2: `Ctrl+N` (새 파일) KeyBindingService 미등록 — 수정 완료

- **증상**: `MainWindow.xaml.cs`에서 `e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control`로 하드코딩
- **수정**: `KeyBindingService.GetDefaults()`에 `NewFile` (`Ctrl+N`) 항목 추가. `MainWindow`에서 `kb.Matches(e, "NewFile")`로 교체

### BUG-3: MarkdownViewer `Ctrl+F` 하드코딩 — 수정 완료

- **증상**: `MarkdownViewer.HandleShortcut()`에서 `e.Key != Key.F` 직접 비교
- **수정**: `IViewerShortcutHandler.HandleShortcut` 시그니처에 `KeyBindingService` 파라미터 추가. `MarkdownViewer`에서 `kb.Matches(e, "ShowSearch")`로 교체. `ViewerHost` → `MainWindow` 호출 체인 업데이트

### BUG-4: FavoritesPanel `F2` / `Ctrl+C` 하드코딩 — 수정 완료

- **증상**: `FavoritesPanel.FavoritesTree_PreviewKeyDown()`에서 키 직접 비교
- **수정**: `FavoritesPanel`의 `PreviewKeyDown` 핸들러 제거. `MainWindow`의 `Rename` 핸들러가 `FavoritesPanel.IsKeyboardFocusWithin` 시 `FavoritesPanel.RenameSelected()` 호출하도록 변경. `CopyClipboard` 핸들러에서 즐겨찾기/파일 목록 모두 처리

### BUG-5: `docs/architecture.md` 문서 불일치 — 수정 완료

- **증상**: 클래스명 `KeybindingManager` (실제: `KeyBindingService`), 저장 형식 `JSON` (실제: `XML`)
- **수정**: 올바른 이름과 형식으로 문서 갱신

### BUG-6: MonacoViewer `Ctrl+F` 전역 검색에 가로채임 — 수정 완료

- **증상**: `MonacoViewer`가 `IViewerShortcutHandler`를 구현하지 않아 `TryHandleActiveViewerShortcut`가 항상 실패, 전역 `ShowSearch` 핸들러가 `Ctrl+F`를 처리해 Monaco의 파일 내 검색(`actions.find`)이 열리지 않음
- **수정**: `MonacoViewer`가 `IViewerShortcutHandler`를 구현하도록 추가. `HandleShortcut`에서 `ShowSearch`를 가로채 WebView에 포커스를 준 뒤 `app.openFind()`를 호출(Monaco `actions.find` 액션 실행). `MarkdownViewer`와 동일한 패턴 (`docs/items/monaco-viewer-ctrl-f-search-conflict.md` 참고)

---

## 6. 미구현 제안 단축키

파일 관리자에서 일반적으로 사용되는 단축키 중 현재 미구현 항목:

| 제안 키 | 동작 | 우선순위 | 비고 |
|---------|------|---------|------|
| `Ctrl+A` | 전체 선택 | 높음 | 대부분의 파일 관리자 기본 기능 |
| `Ctrl+W` | 현재 탭(패널) 닫기 | 높음 | `Ctrl+T`(추가)와 쌍을 이루는 기능 |
| `Ctrl+L` / `F4` | 경로 입력란 포커스 | 중간 | Windows 탐색기, 브라우저 공통 |
| `Ctrl+D` | 즐겨찾기에 현재 폴더 추가 | 중간 | 브라우저 북마크 패턴 |
| `Ctrl+H` | 숨김 파일 표시 토글 | 낮음 | 편의 기능 |
| `Space` | 빠른 미리보기 (Quick Look) | 낮음 | macOS Finder 스타일 |
| `Ctrl+Y` | 다시 실행 (실행 취소한 파일 작업) | 낮음 | `Ctrl+Z` 실행 취소는 구현됨(`UndoFileOperation`, `docs/items/file-operation-queue-and-undo.md`). 다시 실행은 아직 없음 |

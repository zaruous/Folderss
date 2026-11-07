# Monaco 뷰어: 번들 언어 자동 연결·탭 언어 선택·확장자별 언어 설정 (#82)

- 상태: Done — master 머지 확인 (이전: Ready for Verification)
- 상태 전환일: 2026-11-07
- 이슈: #82

## 요구사항

- 번들된 Monaco 언어(약 90개)를 확장자·파일 이름으로 자동 연결한다. 하드코딩 사전(`MonacoViewer.ExtToLanguage`, 23개)은 Monaco 기본과 다른 예외만 남긴다.
- Monaco 탭 툴바에서 현재 언어를 보고 바꾼다(검색 가능, 등록 언어 전체 + "자동"). 바꾸면 즉시 `setModelLanguage`, 파일별로 기억(최근 N개), 세션 복원·다시 열기 때 적용, "자동"은 기억 삭제.
- 설정 > 뷰어에 사용자 정의 연결(확장자·파일 이름 패턴 → 언어 id). 저장은 `TrySave` 목록.
- 우선순위: 파일별 선택 → 사용자 지정 → 예외 사전 → Monaco 등록 정보 → plaintext.
- 대용량 모드 동작(초대용량 plaintext 강제) 유지.

## 원인 분석 또는 설계

### 언어 결정 위치: JS 런타임(실제) + C# 정적 표(설정 창·테스트)

두 안을 비교했다.

| 안 | 장점 | 단점 |
|---|---|---|
| (가) 빌드 시 contribution.js에서 확장자 표를 추출해 C#이 최종 언어까지 결정 | C# 단위 테스트로 완결 | 빌드 단계·스크립트 추가, 런타임 등록(jsonl 등)과 따로 놀 수 있음 |
| (나) JS가 런타임 `getLanguages()`로 결정, C#은 강제 언어만 | Monaco가 실제로 등록한 정보 그대로(누락·순서 차이 없음) | C#만으로는 최종 결과를 알 수 없음 |

**(나)를 기본으로 하고, C#에 같은 규칙의 정적 표를 둔다.**

- 편집기: `monaco-app.html`의 `detectLanguage(fileName)` — `filenames` 일치 → `filenamePatterns`(glob) → `extensions`(이름 끝이 같은 것 중 가장 긴 것, 같은 길이면 먼저 등록된 언어) → plaintext. 대소문자 무시.
- C#: `Services/MonacoLanguages`
  - `ResolveForced(fileName, perFile, userMappings)` — 파일별 선택 → 사용자 지정 → 예외 사전. 없으면 null(편집기가 결정).
  - `Languages` — 번들 등록 정보를 옮긴 정적 표(92개: plaintext, json, basic-languages 89개, jsonl — 런타임 `getLanguages()` 순서).
  - `MatchRegistry` — JS `detectLanguage`와 같은 규칙. `Resolve` = `ResolveForced ?? MatchRegistry ?? plaintext`(테스트·표시용).
- **어긋남 방지**: 테스트 프로젝트가 `basic-languages/monaco.contribution.js`, `monaco.contribution-*.js`(json), `monaco-jsonl.js`를 출력 폴더로 복사하고,
  `MonacoLanguageTests.StaticTable_*`가 정규식으로 읽어 id·순서·확장자·파일 이름·별칭을 정적 표와 비교한다. Monaco를 올려 등록이 바뀌면 테스트가 실패하므로
  표를 다시 만든다(아래 "Monaco 업그레이드 시").
- 런타임 확인(브라우저에서 `monaco-app.html` 직접 로드, 2026-11-01): `getLanguages()` 92개, 순서 plaintext → json → abap … yaml → jsonl, 대표 25개 파일 이름의 `detectLanguage` 결과가 C# 기대값과 같음.

### 예외 사전

Monaco 등록 정보로 이미 맞게 열리는 것(`.xaml`/`.csproj`→xml, `.jsonl`→jsonl, `.txt`→plaintext, `.log`→plaintext(등록 없음))은 뺐고, 다른 것만 남겼다.

| 확장자 | 예외 | Monaco 기본 | 이유 |
|---|---|---|---|
| `.h` | cpp | c | 이전 동작 유지(Windows 코드베이스의 `.h`는 대부분 C++ 헤더) |

이전 사전 23개는 모두 같은 결과로 열린다(`Resolve_KeepsPreviousDictionaryResults`).

### 사용자 지정 패턴

- `.inc` — 점으로 시작하고 와일드카드 없음 → 이름 끝(확장자, 대소문자 무시)
- `Makefile` — 파일 이름 전체
- `Dockerfile.*`, `*.conf` — `*`·`?` glob(파일 이름 전체)
- 경로 구분자(`\`, `/`)·빈 값·`*`·`.` 은 거부. 같은 패턴은 먼저 온 것만 저장. 위에서부터 첫 일치.

### 탭 툴바

- `ViewerHost.xaml` 오른쪽 단추 줄 맨 앞에 `ViewerToolbarSlot`(ContentControl)을 두고, 뷰어가 `IViewerToolbarProvider`면 `ToolbarContent`를 넣는다(뗄 때 비움).
  `.md` 전환 단추(`ViewSwitchPanel`, #81)는 건드리지 않았다.
- `Viewers/MonacoLanguagePicker` — `CompactToggleButtonStyle` 단추(현재 언어 + ` ▾`, 파일별로 고른 언어면 ` ●`) + `Popup`(찾기 칸 + 목록). 찾기는 이름·id·확장자(`.go`, 정적 표 기준).
  ↑↓ Enter Esc, 클릭(원본 조상은 `ElementTree.FindAncestor`). 목록은 편집기가 보낸 런타임 등록 언어(`{type:'languages'}`), 받기 전에는 정적 표. 색은 모두 DynamicResource.
- 고르면: `MonacoLanguageSettings.RememberAndSave`(IO 실패는 Trace — `.md` 보기 기억과 같은 처리, 이번 탭에는 적용) → `app.setLanguage(id)` → `monaco.editor.setModelLanguage`.
  "자동"(null)은 기억을 지우고 사용자 지정·예외 사전부터 다시 따진 강제 언어(없으면 null → 자동 감지)를 보낸다.
- 편집기는 모델을 만들거나 언어를 바꿀 때 `{type:'language', id, huge}`를 보내고, 툴바는 이 값을 표시한다. ` ●`(파일별 선택)는 C#이 기억 여부로 정한다.
- 초대용량(50MB 초과): 편집기가 plaintext로 고정하고 단추는 사용 안 함(툴팁으로 이유 표시). 대용량(20MB 초과)은 이전처럼 언어를 유지하고 부가 기능만 끈다.
- 세션 복원·다시 읽기·외부 변경 다시 읽기·탭 다시 열기는 모두 `Load`/`PromptAndReloadAsync`가 `ResolveForced`를 다시 부르므로 별도 코드가 없다.

### 설정 파일 `monaco-language.json`

```json
{
  "version": 1,
  "mappings": [ { "pattern": ".inc", "language": "php" } ],
  "files": [ { "path": "C:\\work\\a.txt", "language": "markdown" } ]
}
```

- `files`는 오래된 것부터, 최근 200개(`MarkdownViewSettings`와 같은 방식, 경로는 `Path.GetFullPath`, 대소문자 무시).
- 쓰기 `SettingsFile.WriteAllText`(임시 파일 후 교체), 실패는 예외. 설정 창 `TrySave(failures, "Monaco 언어", "monaco-language.json", …)`.
- 읽기: 파일 없음·IO 실패·깨진 JSON·모르는 값은 기본값(빈 목록)으로. 언어 id는 모양(영숫자·`.`·`-`·`_`·`+`)만 검사하고 등록 여부는 편집기가 본다(없는 id면 자동으로 돌아감).
- 플러그인 노출 설정(`PluginAppSettings`)에는 넣지 않았다.

### TextViewer `ExtToLanguage`를 공유하지 않은 이유

`TextViewer`는 highlight.js(`text-app.html`)로 강조하며 언어 이름 체계가 다르다(`.sh → bash`, Monaco는 `shell`; highlight.js에는 `razor`·`st`·`hcl` 등이 없거나 이름이 다름).
같은 표를 쓰면 한쪽 이름이 다른 엔진에서 인식되지 않으므로 그대로 두었다. 공유하려면 Monaco id → highlight.js 이름 변환표가 따로 필요하다(후속 검토).

### 확장자 없는 파일

언어 결정은 파일 이름(`Dockerfile`, `Gemfile`, `config`, `.editorconfig`)도 보지만, 어떤 뷰어로 열지는 `ViewerConfigService`의 **확장자** 매핑이라
확장자가 없는 `Dockerfile`·`Makefile`은 지금도 Monaco 탭으로 열리지 않는다(`ViewerHost.CanOpen`). `.editorconfig`, `api.dockerfile`, `Dockerfile.dev`(확장자 `.dev`)처럼
확장자가 있으면 매핑 후 이름 규칙이 적용된다. 확장자 없는 파일을 뷰어에 매핑하는 기능은 이 항목 범위 밖이다.

### Monaco로 매핑하지 않았던 확장자 (요구 5)

이전에는 설정에서 `.go`를 Monaco로 매핑해도 사전에 없어 plaintext였다. 이제는 강제 언어가 없으면 편집기가 이름으로 고르므로 매핑만 하면 바로 강조된다(`.go`→go, `.rs`→rust …).
등록 정보에 없는 확장자(`.conf`, `.toml`)는 `Monaco 언어 연결`이나 탭 선택으로 정한다.

## 구현 내용

- `Services/MonacoLanguages.cs`(신규): `MonacoLanguageInfo`, `MonacoLanguages`(정적 표·예외 사전·`ResolveForced`·`Resolve`·`MatchRegistry`·`MatchesPattern`), `MonacoLanguageSettings`(`monaco-language.json`).
- `Viewers/Resources/monaco-app.html`: `detectLanguage`·`globToRegExp`·`isRegistered`·`postLanguages`·`postCurrentLanguage`, `app.open`에 `fileName` 인자 추가, `app.setLanguage` 추가.
- `Viewers/MonacoViewer.xaml.cs`: `ExtToLanguage` 삭제 → `MonacoLanguageSettings.Shared.ResolveForced`, 파일 이름 전달, `languages`/`language` 메시지 처리, `IViewerToolbarProvider`(언어 선택), 선택 시 기억·적용.
- `Viewers/MonacoLanguagePicker.xaml(.cs)`(신규), `Viewers/IViewerToolbarProvider.cs`(신규).
- `Controls/ViewerHost.xaml(.cs)`: `ViewerToolbarSlot` 추가, 붙일 때 채우고 뗄 때 비움.
- `SettingsWindow.xaml`: 뷰어 패널에 `Monaco 언어 연결` 영역(새 `RowDefinition`, `Grid.Row="5"`, 기존 행은 그대로).
  `SettingsWindow.MonacoLanguage.cs`(신규 partial): 목록·추가·삭제·저장. `SettingsWindow.xaml.cs`: 초기화 호출 1줄, `TrySave` 1줄.
- 테스트: `tests/Folderss.SearchTests/MonacoLanguageTests.cs`(신규), csproj에 소스 링크·Monaco 등록 파일 복사.

## 변경 파일

- `Folderss/Services/MonacoLanguages.cs` (신규)
- `Folderss/Viewers/MonacoLanguagePicker.xaml`, `.xaml.cs` (신규)
- `Folderss/Viewers/IViewerToolbarProvider.cs` (신규)
- `Folderss/SettingsWindow.MonacoLanguage.cs` (신규)
- `Folderss/Viewers/MonacoViewer.xaml.cs`
- `Folderss/Viewers/Resources/monaco-app.html`
- `Folderss/Controls/ViewerHost.xaml`, `.xaml.cs`
- `Folderss/SettingsWindow.xaml`, `.xaml.cs`
- `tests/Folderss.SearchTests/MonacoLanguageTests.cs` (신규), `Folderss.SearchTests.csproj`
- `README.md`, `docs/architecture.md`, `CLAUDE.md`, `docs/items/monaco-language.md`

## 검증

### 빌드·단위 테스트 (2026-11-01, worktree)

- `dotnet build .\Folderss.sln -c Debug` — 오류 0, Exit 0
- `dotnet build tests/Folderss.SearchTests` — 오류 0(경고 2개는 기존 `FileJobQueueTests`)
- `dotnet test tests/Folderss.SearchTests --filter FullyQualifiedName~MonacoLanguageTests` — 102개 통과
  - 정적 표 ↔ 번들 basic-languages(순서·확장자·파일 이름·별칭), json·jsonl 등록 일치, id 중복·모양
  - 대표 확장자 40여 개(go·rust·kotlin·ruby·php·scss·ini·lua·vb·razor·swift·hcl·graphql·proto·liquid …), 파일 이름 10개(Dockerfile·Gemfile·.editorconfig·`.git\config` …, 대소문자 무시)
  - 이전 사전 23개 결과 유지, 같은 확장자 두 언어(`.pp`) 먼저 등록된 언어
  - 우선순위(파일별 → 사용자 → 예외 → 등록 → plaintext), 잘못된 파일별 id 무시, 사용자 패턴(확장자·이름·glob, 첫 일치)
  - `monaco-language.json` 저장 왕복(중복·빈 패턴·잘못된 id 버림, 경로 대소문자), "자동" 삭제, 200개 제한·최근으로 옮김, 깨진 JSON 4종, 파일 없음
- 전체 테스트·하네스는 병합 후 일괄 실행(이 브랜치에서는 실행하지 않음).
- 브라우저 확인: 로컬 HTTP로 `monaco-app.html`을 열어 `getLanguages()` 92개·순서, `detectLanguage` 25개, `app.open(…,'main.go')`→go, `app.setLanguage('rust')`→rust, `app.setLanguage(null)`→go, 초대용량 `app.open(…,'rust',…,huge,'big.go')`→plaintext 확인.

### 하네스 검사 목록 (병합 후)

1. **대표 언어 20종 열기**: `.go .rs .kt .rb .php .scss .ini .lua .vb .cshtml .swift .tf .graphql .proto .ps1 .sh .sql .yaml .ts .cs`를 설정에서 Monaco로 매핑 → 각 파일 열기 → 모델 언어(`model.getLanguageId()`)와 툴바 표시(`Go`, `Rust` …)가 맞는지, 실제 토큰 색이 입혀지는지.
   이전 사전 확장자(`.xaml`→xml, `.jsonl`→jsonl, `.h`→cpp, `.txt`/`.log`→plaintext) 회귀, `api.dockerfile`→dockerfile, `.editorconfig`(Monaco 매핑 후)→ini.
2. **툴바 선택·기억·재열기**: `.txt` 파일에서 툴바 → `markdown` 선택 → 즉시 강조·` ●` 표시 → 탭 닫고 다시 열기 → markdown 유지 → 앱 재시작(세션 복원) → 유지 → `자동` 선택 → plaintext·` ●` 사라짐·`monaco-language.json` `files`에서 삭제.
   찾기 칸: `rust`·`.go`·`C#` 입력, ↑↓ Enter, Esc 닫기, 단추 다시 눌러 닫기(곧바로 다시 열리지 않음). 다시 읽기(↻)·외부 변경 다시 읽기 뒤에도 선택 유지.
3. **설정 지정·우선순위**: 설정 > 뷰어 `Monaco 언어 연결`에 `.inc → PHP`, `*.conf → Ini`, `.h → C` 추가 → 저장 → `.inc`·`.conf`(Monaco 매핑)·`.h` 열기 → 각각 php·ini·c. 파일별 선택이 연결보다 우선, `자동`이면 연결 언어로 돌아감.
   잘못된 입력(빈 값, `a\b`) 안내, 삭제, 저장 실패 시(파일 읽기 전용) `설정 저장 실패`에 "Monaco 언어 (monaco-language.json)" 표시.
4. **대용량 모드**: 20MB 초과 `.json` → json 유지·미니맵 등 꺼짐 / 50MB 초과 → plaintext·읽기 전용·언어 단추 사용 안 함(툴팁).
5. **테마 전환**: Black ↔ 밝은 테마에서 툴바 단추·팝업(배경·테두리·목록 선택·찾기 칸)이 테마 색을 따르는지, 설정 창 새 영역 색.
6. **WebView2 오프스크린**: 가려진 Monaco 탭(IdleSaver 일시 중지)에서 테마 전환 후 다시 보기, 백그라운드에서 세션 복원된 탭이 처음 보일 때 언어 표시가 채워지는지(`languages`/`language` 메시지가 늦게 와도 단추가 `…`로 남지 않는지).
7. **#81 공존**: `.md` 탭(블록 편집/미리보기 단추)에는 언어 단추가 없고, `.md`를 Monaco로 매핑하면 언어 단추만 보이는지.

### 병합 후 하네스 검증 (2026-11-01, `feature/monaco-language` 6c6de95 = master 0d17152 + #82)

화면 밖 WPF 하네스(창 -20000 위치·활성화 안 함, 실제 마우스 메시지 차단, 메시지 상자는 CBT 훅으로 화면 밖 이동·자동 응답, 언어 선택 `Popup`은 열지 않고 찾기 칸·목록만 다룸).
설정 파일(`monaco-language.json`·`markdown-view.json`·`viewer-config.json`)과 WebView2 사용자 데이터는 하네스 임시 폴더만 썼다. 모드마다 별도 프로세스·시간 제한(`HARNESS_MODE_TIMEOUT_SEC` 300~600초).
하네스는 저장소 밖(세션 scratchpad `harness82`)에 있다.

| 모드 | 검사 | 결과 |
|---|---|---|
| mon-langs (1) | 31개 파일: `.go .rs .kt .rb .php .swift .scss .ini .lua .vb .ps1 .sql .yaml .cs .py .java .cpp .h .jsonl .xaml .cshtml .tf .graphql .proto .sh .ts .txt .log .csproj`, `api.dockerfile`, `.editorconfig` — 모델 언어, 툴바 표시(`Go`·`Rust`·`C#`·`Terraform`… ● 없음), 실제 토큰 색 수(언어 있음 ≥2 / plaintext 1), C# `Resolve`와 런타임 일치 | 124 통과 |
| mon-pick (2) | `.txt` → 목록 93개(자동 + 92), 찾기 `rust`·`.go`·`C#`·`.md`·`Markdown`, ↓↑, Enter로 markdown → 즉시 강조·`Markdown ●`·`files` 기록, Esc, 닫힌 직후 단추 클릭 → 다시 안 열림, 목록 항목 클릭(MouseUp), ↻ 다시 읽기·외부 변경 다시 읽기(확인창 예)·탭 다시 열기·설정 다시 읽기(재시작) 뒤 유지, `자동` → plaintext·● 사라짐·`files`에서 삭제, `monaco-language.json` 읽기 전용이어도 이번 탭에 적용·예외 없음 | 23 통과 |
| mon-prio (3·요구 5) | 설정 창 `Monaco 언어 연결`: 언어 콤보 92개(기본 ini), `.inc→php`·`*.conf→ini`·`.h→c` 추가, `.INC` 중복 없음, 잘못된 입력(빈 값·`a\b`·`x/y`·`*`·`.`) 안내·추가 안 함, 줄 삭제. `.md 기본 보기`·`Monaco 언어` TrySave 둘 다 저장(실패 0)·Save_Click 목록에 둘 다 있음, 읽기 전용 → 실패 목록 `Monaco 언어 (monaco-language.json): Access to the path is denied.` 열기: 연결 전 `.conf`/`.toml` plaintext·`.h` cpp(예외), 연결 뒤 `.inc` php·`.conf` ini·`.h` c·`.go` go, 파일별 sql이 php보다 우선 → `자동`이면 php, 손으로 넣은 없는 id → 자동(go). `.go` 매핑 전 Monaco 아님 → 매핑 저장 뒤 go·토큰 색 | 26 통과 |
| mon-big (4) | 25MB `.json` → json·미니맵 끔·편집 가능·단추 사용(plaintext 선택·자동 복귀 적용), 51MB → plaintext·읽기 전용·미니맵 끔·단추 사용 안 함·툴팁 "초대용량…", 파일별 기억(json)이 있어도 plaintext | 6 통과 (열기 2.4초 / 3.3초) |
| mon-theme (5) | Black→Light→GitHub→Black: 편집기 `folderss-dark/light`, 팝업 배경=PanelBackground·테두리=BorderBrush·단추 글자=PrimaryText. 설정 창(화면 밖 표시) 연결 목록 배경이 테마 색 | 14 통과 |
| mon-off (6) | 가려진 채 연 탭(백그라운드 복원)이 처음 보일 때 rust·`Rust`(`…` 아님), 가려진 탭 30초 뒤 일시 중지 → 테마(Light) 전환·언어 선택 → 다시 보기에서 `folderss-light`·`Plain Text ●` | 4 통과 |
| mon-md (7·#81 공존) | `.md` 블록 편집/미리보기: 전환 단추만·언어 단추 없음, 같은 탭에서 `.cs` → 언어 단추·전환 단추 숨김, 다시 `.md` → 언어 단추 남지 않음, Dispose → 칸 비움. `.md`를 Monaco로 매핑(기본 보기 blocks/mapping/preview, 파일별 preview 기억 있어도) → Monaco·전환 단추 숨김·언어 단추·markdown | 10 통과 |
| 회귀 smoke·switch·dv81 | 블록 편집기 기본, `.md` 전환 14항목, 기본 보기 신규/기존 사용자 | 18 + 53 + 26 통과 |

합계 새 검사 207 + 회귀 97 통과, 실패 0. 실제 `%LOCALAPPDATA%\Folderss` 파일은 수정 시각이 바뀌지 않았다.

#### 남은 문제·사람이 볼 항목

- **찾기 칸 `md`(점 없음)로는 Markdown이 안 나온다**(`mdx`만). 확장자 찾기가 점을 포함한 완전 일치이고 이름 `Markdown`에 `md`가 없어서다. `.md`·`mark`로는 찾는다. 점 없는 확장자도 받을지는 판단 필요.
- **목록 표시 이름**: 별칭이 id와 대소문자만 다른 언어는 목록에 id(`go`, `rust`)로, 단추에는 별칭(`Go`, `Rust`)으로 보인다(`SetLanguages`의 Label 규칙). 의도인지 확인.
- 기억 저장 실패(`monaco-language.json` 읽기 전용) 때 이번 탭 단추는 `●`로 보이지만 파일에는 남지 않는다(설계대로 Trace만). 
- 하네스에서 하지 못한 것: 실제 팝업 열기·바깥 클릭으로 닫기·팝업 위치/그림(실제 화면에 그려지고 마우스를 잡으므로), 실제 `Save_Click`(다른 설정 파일까지 실제 경로에 씀 — 같은 `TrySave` 호출과 소스 목록으로 대신), MainWindow 세션 복원(설정 다시 읽기 + 새 탭 `Load` 경로로 대신). 실제 앱에서 눈으로 확인할 것: 언어 단추·팝업 모양(밝은/어두운 테마), 설정 창 `Monaco 언어 연결` 영역 배치.

### Monaco 업그레이드 시

1. `Viewers/Resources/monaco` 교체 후 `dotnet test tests/Folderss.SearchTests --filter MonacoLanguageTests`.
2. `StaticTable_*`가 실패하면 `MonacoLanguages.LanguageTable`을 다시 만든다 — `basic-languages/monaco.contribution.js`를 가짜 `define`/`monaco.languages.register`로 node에서 실행해
   등록 객체(id·aliases·extensions·filenames·filenamePatterns)를 뽑아 `L("id", "별칭|…", ".ext|…", "파일이름|…", "패턴|…")` 줄로 옮긴다(이번 작업에서 쓴 방법).
3. 런타임 순서(json·plaintext 위치)는 브라우저에서 `monaco.languages.getLanguages().map(l => l.id)`로 확인.

## 변경 이력

- 2026-11-01: 구현, Ready for Verification.
- 2026-11-01: master 병합 후 화면 밖 하네스 검증(새 검사 207 + 회귀 97 통과, 코드 수정 없음). 사람 확인 항목은 "검증" 절.

# Cursor 제안 '소' 규모 기능 5건 구현

- 상태: Ready for Verification

## 요구사항

2026-09-22 Cursor(composer-2.5) 에이전트가 저장소를 읽고 제안한 아이디어 중 '소(반나절)' 규모 5건을 구현한다.

1. 콘솔 폰트 크기 런타임 반영 (+ 프로필별 작업 폴더 기억)
2. `.gitignore` / `.folderssignore` 기반 목록 필터
3. 심볼릭 링크·junction 시각 표시와 링크 만들기
4. 검색 결과를 활성 패널의 임시 필터로 적용
5. 뷰어 미저장 변경 표시와 닫기 확인

## 설계와 구현 내용

### 1. 콘솔 폰트 크기 런타임 반영

- 원인: `ConsolePanel`이 `_settings`를 자체 캐시하고 새 탭을 열거나 셸을 다시 시작할 때만 다시 읽었다. 설정 창에서 폰트 크기를 저장해도 이미 열린 탭은 재시작 전까지 옛 크기로 남았다.
- 구현: `ConsolePanel.ApplySettings()` 추가 — 프로필을 다시 읽고 열린 모든 탭에 `ApplyTerminalAppearance`(FontSize + 내부 SetTheme 재적용). `MainWindow.Settings_Click`이 `ShowDialog() == true`일 때 호출한다.
- **프로필별 작업 폴더 기억은 넣지 않았다.** 콘솔이 항상 활성 폴더에서 시작하는 현재 설계와 충돌하고, 넣으려면 옵트인 설정 항목이 필요하다. `docs/아이디어.md` 후속 후보에 기록.

### 2. ignore 규칙 필터

- `Services/IgnoreRuleSet.cs` (신규): gitignore 문법 부분집합 매처. 주석·빈 줄, 부정(`!`), 디렉터리 전용(`/`), 앵커(`/`가 있는 패턴은 규칙 파일 위치 기준), `*`·`?`·`[..]`·`**`, 대소문자 무시(Windows git 기본). `LoadFor(directory)`는 가장 가까운 `.git`을 저장소 루트로 보고 루트→현재 폴더 순서로 `.gitignore`를 읽어 깊은 규칙이 우선하게 하며, 저장소 안에서는 `.git` 폴더도 숨긴다. `.folderssignore`는 저장소와 무관하게 드라이브 루트부터 읽는다.
- 판정은 **항목 자신만** 본다. 상위 폴더가 무시 대상이어도 그 안으로 들어가 보는 목록은 규칙에 직접 걸리는 항목만 숨긴다 — 일부러 들어간 폴더가 비어 보이는 것보다 예측 가능하다.
- `FolderBrowser`: 필터 바에 `ignore` 토글 추가(패널 단위, 재시작 시 꺼짐). `RefreshItems`가 토글이 켜져 있으면 규칙을 새로 읽어 걸리는 항목을 빼고, 상태바에 "ignore 규칙으로 N개 숨김", 토글 툴팁에 적용 중인 규칙 파일 목록을 보인다.
- 테스트: `tests/Folderss.SearchTests/IgnoreRuleSetTests.cs` 12건 (문법 9건, `LoadFor` 3건).

### 3. 링크 표시와 링크 만들기

- `FileSystemItem`에 `IsLink`/`LinkTarget`/`LinkToolTip` 추가. `FilePreviewService.GetLinkTarget(FileSystemInfo)`가 reparse point 중 `LinkTarget`을 읽을 수 있는 것(심볼릭 링크·junction)만 링크로 본다 — OneDrive 자리표시자 같은 다른 reparse point는 제외.
- 목록: 아이콘 🔗, 유형 "폴더 링크", 행 툴팁 "링크 대상: …"(`ItemContainerStyle`의 ToolTip Setter, null이면 툴팁 없음). 메타정보에 "링크 대상" 행(링크일 때만 표시).
- `FileOperationService.CreateLink(source, destinationDirectory)`: 심볼릭 링크를 먼저 시도하고, 권한 오류(`ERROR_PRIVILEGE_NOT_HELD`, `UnauthorizedAccessException`)면 폴더는 `mklink /J`로 junction을 만든다. 파일은 대안이 없어 관리자 권한/개발자 모드 안내와 함께 실패. 이름 충돌은 복사·이동과 같은 `(2)` 규칙.
- 메인 메뉴 `반대편 패널에 링크 만들기` (`MainWindow.CreateLink_Click`): `ExecuteTransfer`와 같은 대상 패널·핀 잠금 검사·확인창·오류 모음.

### 4. 검색 결과 → 패널 필터

- `SearchPanel` 상태바에 `패널에 필터 적용` 버튼(결과가 있을 때만). `ApplyFilterRequested(SearchFilterEventArgs{RootPath, FilePaths, Description})` 이벤트.
- `MainWindow.ApplySearchResultFilterToActivePane`: 활성 패널이 검색 대상 폴더와 다른 곳이면 먼저 이동한 뒤 `FolderBrowser.ApplySearchResultFilter` 호출.
- `FolderBrowser`: 결과 파일 집합과, 결과를 품은 폴더 집합(루트까지의 조상)을 만들어 `ApplyFilter`에 AND로 건다. 폴더는 결과가 있는 것만 보이므로 하위 폴더로 내려가며 볼 수 있다. 배너("검색 결과만 표시 — "검색어" (N개 파일)" + `해제`)가 필터 바 아래에 뜨고, 검색 루트 밖으로 이동하면 자동 해제된다. 이름 필터·ignore 필터와 함께 동작한다.

### 5. 뷰어 미저장 표시와 닫기 확인

- 뷰어(`MarkdownViewer`, `MonacoViewer`)는 이미 `ModifiedChanged`를 올리고 `ViewerHost`가 전달했지만 `MainWindow`가 쓰지 않았다.
- `ViewerHost.IsModified` 추가(이벤트를 따라감, 뷰어 교체 시 초기화). `MainWindow.SetDocumentTitle`이 호스트의 `IsModified`를 보고 제목 끝에 ` *`를 붙이거나 뗀다(잠금 접두사 🔒와 독립, `ModifiedChanged`마다 제목 재기록).
- `AttachViewerDocument`에서 `LayoutDocument.Closing`을 구독해 미저장이면 "닫을까요? 예/아니요"(기본 아니요)로 확인. X 버튼·탭 메뉴·코드의 `Close()`가 모두 `DockingManager`를 거쳐 `Closing`을 올리므로 한 곳으로 충분하다.
- 실제 종료(`Window_Closing`, `_reallyClose`)에서는 미저장 문서 개수를 세어 한 번 묻고, 거부하면 종료를 취소하고 `_reallyClose`를 되돌린다.
- 저장/저장 안 함/취소 3버튼은 넣지 않았다. 저장은 WebView2 편집기 내용을 비동기로 받아야 해서 동기 취소 흐름과 맞지 않는다. `docs/아이디어.md` 후속 후보에 기록.

## 변경 파일

- 신규: `Folderss/Services/IgnoreRuleSet.cs`, `tests/Folderss.SearchTests/IgnoreRuleSetTests.cs`
- `Folderss/Models/FileSystemItem.cs`
- `Folderss/Services/FilePreviewService.cs`, `Folderss/Services/FileOperationService.cs`
- `Folderss/Controls/FolderBrowser.xaml/.cs`, `Folderss/Controls/SearchPanel.xaml/.cs`, `Folderss/Controls/ViewerHost.xaml.cs`, `Folderss/Controls/ConsolePanel.xaml.cs`
- `Folderss/MainWindow.xaml/.cs`
- `tests/Folderss.SearchTests/Folderss.SearchTests.csproj`
- `README.md`, `docs/architecture.md`, `docs/아이디어.md`(신규)

## 검증

- [x] `dotnet build .\Folderss.sln -c Debug` — Exit 0
- [x] `dotnet test tests\Folderss.SearchTests` — 전체 통과 (IgnoreRuleSet 12건 포함)
- [ ] 설정 > 콘솔에서 폰트 크기를 바꿔 저장 → 열려 있는 콘솔 탭 글자 크기가 바로 바뀌는지
- [ ] git 저장소 폴더에서 `ignore` 토글 → `bin`·`obj`·`.git`이 사라지고 상태바에 숨긴 개수, 툴팁에 규칙 파일 목록이 보이는지. 저장소가 아닌 폴더에서는 `.folderssignore`만 적용되는지
- [ ] junction/심볼릭 링크 폴더가 🔗 아이콘·툴팁·메타정보 "링크 대상"으로 보이는지. OneDrive 파일에는 🔗가 붙지 않는지
- [ ] 폴더를 선택해 `반대편 패널에 링크 만들기` → 일반 권한에서 junction이 만들어지는지, 파일은 권한 안내가 나오는지
- [ ] 검색 후 `패널에 필터 적용` → 배너와 결과 파일만 보이는지, 결과가 있는 하위 폴더로 내려가도 유지되는지, 검색 루트 밖으로 나가면 풀리는지
- [ ] Markdown 편집 후 탭 제목에 ` *`, 닫기 시 확인창, Ctrl+S 후 표시가 사라지는지. 미저장 상태로 종료 시 확인창이 뜨는지

## 변경 이력

- 2026-09-22: 5건 구현(1번은 폰트 반영만), 항목 생성 (Ready for Verification)

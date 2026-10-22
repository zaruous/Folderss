# zip 파일을 폴더처럼 열기

- 상태: Ready for Verification

## 요구사항

사용자 요청: 파일 관리자에서 zip 파일을 폴더처럼 열어 안을 탐색하는 기능(Total Commander의 대표 기능 같은 것)을 넣고 싶다.

1. **들어가기·나오기**: 폴더 패널에서 `.zip`을 Enter/더블클릭하면 같은 패널에서 zip 안 목록을 보인다. 경로 표시는 `C:\a\b.zip\sub`. 하위 폴더 이동, Backspace·↑(상위)로 zip 밖으로 나오기, 뒤로/앞으로, 경로 입력란에 zip 경로 입력, 세션 복원(다시 시작하면 같은 zip 위치)이 맞물려야 한다.
2. **zip 안 파일 열기**: 임시 폴더에 풀어 기존 뷰어(Markdown·Monaco·Text) 또는 기본 프로그램으로 연다. 읽기 전용 사본이라는 것을 알린다. 아래 미리보기 영역도 zip 안 파일을 보인다.
3. **꺼내기**: zip 패널 → 반대편 패널 복사(끌어다 놓기·Ctrl+C/Ctrl+V·우클릭), 우클릭 "압축 풀기"(zip이 있는 폴더 / zip 이름 폴더 / 반대편 패널), 선택 항목 "zip으로 압축".
4. **zip 내부 수정은 1단계 범위 밖**: 삭제·이름 변경·추가·이동·편집 후 다시 넣기는 막고 이유를 안내한다.
5. **이름 인코딩·오류**: 한국어 Windows에서 만든 zip의 CP949 이름(UTF-8 플래그 없음)이 깨지지 않게. 암호 걸린 zip·손상된 zip은 친절한 메시지. 큰 zip은 목록을 한 번만 읽어 캐시하고 UI가 멈추지 않게.
6. 실제 디렉터리를 가정하는 기능(트리뷰·폴더 감시·ignore·검색 대상·콘솔 작업 폴더·Git·탐색기 열기·즐겨찾기 추가·링크 만들기·두 파일/폴더 비교·다음으로 열기)이 zip 안에서 깨지거나 엉뚱하게 동작하지 않게 막거나 안내한다.

## 원인 분석 또는 설계

### 참고 제품 조사

| 제품 | 들어가기 | 안 파일 열기·편집 | 꺼내기 | 압축 풀기 옵션 | 내부 수정 | 이름 인코딩 |
|---|---|---|---|---|---|---|
| Windows 탐색기 (압축(zip) 폴더) | 더블클릭하면 폴더처럼 열림 | 임시 위치에 풀어 연다(바뀐 내용은 zip에 안 들어감) | 안에서 끌어 놓기/복사 | `압축 풀기(Extract All)…` 마법사: 대상 폴더 지정 | 끌어 놓으면 추가됨. 암호 zip 작업은 지원 안 함 | 플래그 없는 이름은 시스템 코드 페이지 |
| Total Commander | Enter/더블클릭(설정 "아카이브를 폴더처럼", 기본 켬). 자동 압축 해제 EXE는 Ctrl+PgDn | F3/F4는 임시 폴더에 풀어 열고, 편집기를 닫으면 "다시 넣을까?" 물음 | 아카이브를 연 채 F5 → 반대편 패널에 풀림 | `파일 > 압축 풀기`(Alt+F9): 대상(반대편 패널), 파일 마스크, "저장된 경로대로 풀기", "아카이브마다 이름 폴더" | 내부 ZIP 패커로 추가·삭제 가능 | 설정으로 UTF-8/OEM |
| 7-Zip 파일 관리자 | Enter(=Open), Ctrl+PgDn `Open Inside`, Shift+Enter `Open Outside`(Windows 연결 프로그램) | 임시 폴더에 풀어 열고, 바뀌면 7-Zip으로 돌아올 때 "File '…' was modified. Do you want to update it in the archive?" | F5 `Copy To…` = 풀기 | Extract 대화상자: 대상, 경로 모드(전체/없음/절대), 덮어쓰기 모드(묻기/덮어쓰기/건너뛰기/자동 이름 바꾸기/기존 파일 이름 바꾸기), 비밀번호, "루트 폴더 중복 제거" | 지원(형식에 따라 불가) | 플래그 없으면 로컬 코드 페이지, `cu`/`cl`/`cp` 옵션 |
| Directory Opus | zip을 폴더처럼 열고 복사·보기 | (안내 페이지에 편집 반영 설명 없음) | 복사해서 꺼냄 | Archive Files 버튼의 압축·풀기 명령 | 가능. **"zip을 기본으로 읽기 전용으로 열기" 옵션** 있음, 추가 시 임시 파일로 원본 보호, "zip으로 취급할 확장자" 목록(.jar 등) 설정 | — |

핵심 정리:

- 네 제품 모두 **같은 패널에서 폴더처럼 들어가고**, **꺼내기 = 반대편 패널로 복사**다. 안 파일은 **임시 폴더에 풀어서** 연다.
- 편집 후 다시 넣기는 7-Zip·TC 모두 "바뀌었으면 다시 넣을지 묻기" 방식이고, 편집기 종류에 따라 감지가 실패하는 사례가 알려져 있다(포럼 버그 보고). 1단계에서는 하지 않고 읽기 전용임을 알린다 — Directory Opus도 "읽기 전용으로 열기"를 옵션으로 둔다.
- 압축 풀기는 "기본 동작으로 바로"(탐색기 `Extract All`, 7-Zip `Extract to "이름\"`, TC `F5`) + "옵션 대화상자"(대상·덮어쓰기 모드) 두 층이다. AGENTS.md의 "버튼은 기본 옵션으로 바로, ▾/…는 대화상자" 규칙과 맞다.
- 덮어쓰기 기본값은 제품마다 다르지만(7-Zip은 묻기), 앱의 기존 복사(`FileOperationService.Copy`)는 **같은 이름이 있으면 `이름 (2)`로 만들고 덮어쓰지 않는다**. 같은 규칙을 따른다.
- 이름 인코딩: zip 규격(APPNOTE)은 일반 목적 플래그 bit 11이 켜져 있으면 UTF-8, 꺼져 있으면 원래 IBM PC 문자 집합(CP437)이라고 정하지만, 실제로는 각 Windows가 **시스템 OEM 코드 페이지**(한국어 = CP949)로 쓴다. 플래그가 없다고 UTF-8이 아닌 것도 아니다(macOS·리눅스 zip). 한 zip 안에서 플래그가 항목마다 다를 수 있다.
- Windows 11 탐색기는 RAR·7z·TAR도 읽지만 암호 걸린 아카이브는 지원하지 않는다고 Microsoft가 밝힌다.

출처:
- Microsoft, [Zip and unzip files](https://support.microsoft.com/en-us/windows/zip-and-unzip-files-8d28fa72-f2f9-712f-67df-f80cf89fd4e5)
- Total Commander, [초보자 안내](https://www.ghisler.com/beginners.htm), 포럼 [아카이브를 폴더처럼](https://www.ghisler.ch/board/viewtopic.php?p=200400), [F4 편집 후 다시 넣기 문제](https://ghisler.ch/board/viewtopic.php?p=468359), ["Unpack path names if stored with files"](https://www.ghisler.ch/board/viewtopic.php?p=302994)
- 7-Zip 도움말 [File Manager 메뉴](https://7-zip.opensource.jp/chm/fm/menu.htm), [Extract 대화상자](https://7-zip.opensource.jp/chm/fm/plugins/7-zip/extract.htm), [-m 스위치(zip cu/cl/cp)](https://7-zip.opensource.jp/chm/cmdline/switches/method.htm), XYplorer 포럼 [7-Zip 편집 후 갱신 질문](https://www.xyplorer.com/xyfc/viewtopic.php?p=76600)
- Directory Opus 도움말 [Archives](https://www.gpsoft.com.au/help/opus10/Documents/Archives.htm), [Zip File Options](https://www.gpsoft.com.au/help/opus12/documents/Prefs/Zip_Files.htm)
- zip 이름 인코딩: [braindetox — 압축 파일 이름이 깨지는 이유(CP949·bit 11·0x7075)](https://braindetox.kr/posts/zip_filename_encoding_korean.html), [APPNOTE bit 11 설명(proxmox 패치 토론)](https://lists.proxmox.com/pipermail/pbs-devel/2021-September/003961.html)

### 설계

**가상 경로**: 패널의 `CurrentPath`는 `C:\a\b.zip\sub` 같은 가상 경로를 그대로 쓴다(제목·경로 입력란·뒤로/앞으로·세션에 그대로 저장). 경로를 받으면 끝에서부터 위로 올라가며 처음 만나는 "존재하는 파일"이 zip이면(확장자 `.zip` 또는 zip 시그니처) zip 경로 + zip 안 경로(`sub`)로 나눈다(`ZipArchivePath.TrySplit`). 실제 폴더가 필요한 기능을 위해 `FolderBrowser.FileSystemPath`(zip 안이면 zip이 있는 폴더)와 `IsInArchive`·`ArchivePath`를 둔다.

**목록 읽기·캐시**: `ZipListingCache`가 zip을 백그라운드(`Task.Run`)에서 한 번 읽어 폴더 트리(`ZipListing`)로 만들고, zip 파일 크기·수정 시각이 같으면 다시 읽지 않는다(최근 8개). 목록을 읽는 동안 상태 줄에 "읽는 중…". 디렉터리 항목이 없는 zip은 경로에서 폴더를 만든다. zip 파일 자체를 `FileSystemWatcher`로 지켜 바뀌면 다시 읽고, 지워지면 zip이 있던 폴더로 나간다.

**이름 인코딩**(`ZipEntryNameEncoding`): .NET `ZipArchive`는 bit 11이 켜진 항목은 UTF-8로, 꺼진 항목은 넘겨준 인코딩으로 읽는다. 꺼진 항목마다 다음 순서로 판정하는 인코딩을 넘긴다.
1. 모두 ASCII → 그대로.
2. 엄격한 UTF-8로 읽히고 C1 제어 문자(U+0080–U+009F)가 없으면 → UTF-8 (macOS·리눅스에서 만든 플래그 없는 zip).
3. 엄격한 CP949로 읽히면 → CP949 (한국어 Windows 탐색기·알집 등).
4. 그 밖 → CP437(규격의 원래 문자 집합, 모든 바이트가 글자로 바뀜).
읽은 이름은 NFC로 정규화한다(macOS zip의 한글 자모 분리 방지).

**zip Slip·위험한 이름**: `..` 조각이나 `:`(드라이브·대체 데이터 스트림)이 든 항목은 목록에서 숨기고 상태 줄에 개수를 보이며 절대 풀지 않는다. 앞의 `/`는 떼고 상대 경로로 본다(7-Zip·Info-ZIP과 같음). 풀 때 Windows에서 쓸 수 없는 글자(`<>:"|?*`, 제어 문자)는 `_`로, 끝의 점·공백은 `_`로, 장치 이름(CON·NUL·COM1 등)은 앞에 `_`를 붙인다. 최종 경로가 대상 폴더 밖이면 거부(이중 검사).

**zip 안 파일 열기**: `ZipTempStore`가 `%TEMP%\Folderss\zip\<pid>-<시각>\<zip 해시>\<zip 안 경로>`에 그 파일만 풀고 읽기 전용 특성을 붙인 뒤 기존 파일 열기 경로(`FileOpenRequested` → 뷰어 탭 또는 기본 프로그램)로 넘긴다. 같은 zip·같은 파일이면 다시 풀지 않는다. 실행 파일은 확인(기본 아니요, "같은 zip의 다른 파일이 필요할 수 있음" 안내 — 탐색기와 같은 경고). 처음 열 때 한 번 "읽기 전용 사본, 고친 내용은 zip에 반영되지 않음"을 알리고 이후엔 상태 줄로 알린다. 임시 폴더는 프로세스 종료 때 지우고, 하루 지난 다른(실행 중이 아닌) 세션 폴더는 다음 사용 때 지운다.

**꺼내기 규칙**:
- 반대편 패널로 복사(끌어 놓기·우클릭·Ctrl+C→Ctrl+V)는 기존 복사와 같다: 선택한 최상위 항목 이름이 대상에 있으면 `이름 (2)`로 새로 만든다 → 덮어쓰기 없음.
- 끌어다 놓기는 앱 안 전용 데이터 형식으로 넘기고 놓을 때 푼다(놓기 전에는 풀지 않음). 기존 복사 끌어 놓기처럼 "복사할까요?"를 묻는다.
- Ctrl+C는 선택 항목을 임시 폴더에 푼 뒤 그 경로를 클립보드(FileDrop)에 넣는다 → 앱의 붙여넣기·탐색기 붙여넣기 모두 그대로 동작(붙여넣기 코드 수정 없음). 512MB가 넘으면 먼저 묻는다.
- 오래 걸리면(0.4초 이상) 진행 창(취소 가능)을 띄운다. 파일은 `.folderss-part` 임시 이름으로 쓴 뒤 바꿔 취소·오류 때 반쪽 파일이 남지 않는다.

**압축 풀기(기본 동작 + 옵션)**:
- 우클릭 `압축 풀기 → '<이름>\'`(zip 여러 개면 각각): zip이 있는 폴더에 zip 이름의 **새 폴더**(있으면 `이름 (2)`)를 만들어 푼다. zip 맨 위가 zip과 같은 이름의 폴더 하나뿐이면 `이름\이름\`이 되지 않게 그 폴더를 바로 꺼낸다(7-Zip "루트 폴더 중복 제거"와 같은 조건).
- `압축 풀기…`: 대화상자에서 위치(zip이 있는 폴더 / 반대편 패널 / 직접 지정), "zip 이름으로 새 폴더 만들기", 같은 이름이 있을 때(이름 바꿔 둘 다 유지 — 기본 / 건너뛰기 / 덮어쓰기 — 경고와 확인 체크 필요)를 고른다.
- zip 안에서는 위쪽 띠(배너)의 `압축 풀기` 버튼이 기본 동작(위와 같음), 옆 `▾`가 같은 대화상자를 연다. 우클릭에는 `반대편 패널로 복사`, `복사(Ctrl+C)`, `압축 풀기…`(선택 항목), `전체 압축 풀기`, `zip 밖으로`, `탐색기에서 zip 파일 보기`.

**zip으로 압축**: 우클릭 `zip으로 압축 → '<이름>.zip'`은 현재 폴더에 바로 만든다(하나면 그 이름, 여럿이면 현재 폴더 이름, 있으면 `이름 (2).zip`). `zip으로 압축…`은 이름·위치(현재 폴더/반대편 패널)·압축 수준을 고른다. 같은 폴더의 임시 파일에 쓴 뒤 이름을 바꿔 반쪽 zip이 남지 않는다. 이름은 UTF-8(bit 11)로 저장, 빈 폴더 포함, 링크(reparse point) 폴더는 안으로 들어가지 않고 빈 폴더로 넣는다. 만드는 zip 자신은 넣지 않는다.

**zip 안에서 막는 것**(`zip 안에서는 …할 수 없습니다` 안내 + 꺼내는 방법): 삭제·이름 변경·새 폴더·새 파일·이동(F6)·잘라내기·붙여넣기·끌어 넣기·링크 만들기·두 파일/폴더 비교·즐겨찾기 추가·Git 창. 다음으로 열기 메뉴는 숨긴다. 탐색기로 열기는 zip 파일을 선택한 탐색기 창, 콘솔 작업 폴더·검색 대상은 zip이 있는 폴더(`FileSystemPath`). 트리뷰는 zip을 보이지 않고 zip이 있는 폴더에 머문다. 숨김·ignore 필터는 zip 안에 적용하지 않는다. 폴더 비교 창(안전 모드)과 플러그인 팝업 패널은 zip에 들어가지 않는다(기존 "폴더" 계약 유지).

**병합 충돌 최소화**: 다른 작업(파일 작업 대기열·실행 취소, 경로 복사 옵션)과 동시에 진행되므로 로직은 새 파일(`Services/ZipArchiveService.cs`·`ZipEntryNameEncoding.cs`·`ZipTempStore.cs`, `Controls/FolderBrowser.Archive.cs`, `MainWindow.Archive.cs`, `ArchiveDialogs.cs`)에 두고 기존 파일에는 한 줄짜리 연결(가드·훅)만 넣는다.

### 사용자 확인이 필요한 결정

| # | 고른 것 | 대안 |
|---|---|---|
| 1 | Enter/더블클릭으로 들어가는 것은 `.zip`만. `.jar`·`.nupkg`·`.vsix`·`.docx` 등 zip 기반 파일은 기존 동작(뷰어/기본 프로그램)을 유지하고 우클릭 `zip으로 열어 보기`로 들어간다 | Directory Opus처럼 "zip으로 취급할 확장자" 목록을 설정에 두기 |
| 2 | zip 내부 수정(삭제·이름 변경·추가·편집 후 다시 넣기)은 하지 않음(읽기 전용) | 후속: 7-Zip/TC식 "바뀌었으면 다시 넣을까?" + 임시 zip에 다시 써서 교체 |
| 3 | 플래그 없는 이름: UTF-8로 읽히면 UTF-8, 아니면 CP949, 그것도 아니면 CP437 (고정) | 시스템 OEM 코드 페이지 사용, 또는 패널에서 인코딩 고르기(일본어·중국어 zip 대응) |
| 4 | 새로 만드는 zip 이름은 UTF-8(bit 11) | 옛 도구 호환용 CP949 저장 옵션 |
| 5 | 꺼내기는 기존 복사 규칙(같은 이름이면 `이름 (2)`), 덮어쓰기는 `압축 풀기…` 대화상자에서만(경고+확인 체크) | 7-Zip처럼 파일마다 묻기 |
| 6 | 압축 풀기 기본 동작 = zip 이름 새 폴더, 맨 위가 같은 이름 폴더 하나면 중복 폴더 제거 | 탐색기처럼 항상 `이름\` 안에 그대로 |
| 7 | zip 안 파일은 읽기 전용 임시 사본으로 열고, 실행마다 처음 한 번만 안내 창 | 매번 안내 / 안내 없이 상태 줄만 |
| 8 | 임시 폴더: 종료 때 지우고 하루 지난 세션 폴더는 다음에 지움 → 다시 시작하면 zip 안 파일로 연 뷰어 탭은 복원되지 않음 | 임시 사본을 남겨 탭 복원 |
| 9 | 탐색기 등 다른 프로그램으로 끌어 놓기는 아직 안 됨(앱 안 패널끼리만). 다른 프로그램에는 Ctrl+C → 붙여넣기 | 끌기 시작할 때 임시 폴더에 풀어 FileDrop으로 넘기기 |
| 10 | zip 안의 zip은 기본 프로그램으로 엶(안으로 들어가지 않음) | 임시 사본 안으로 들어가기 |
| 11 | zip 안에서 검색·콘솔은 zip이 있는 폴더 기준, Git 창은 막음 | 검색을 zip 안 목록에서 하기(후속) |
| 12 | 단축키(Ctrl+PgDn `Open Inside`)는 추가하지 않음 — 문서 탭 전환(Ctrl+PgDn)과 겹침 | 다른 키로 추가 |

### 후속 과제

- zip 내부 수정(삭제·이름 변경·추가, 편집 후 다시 넣기). 7-Zip·TC처럼 임시 사본을 감시해 바뀌면 묻고, 새 zip을 임시 파일에 써서 원본과 교체(Directory Opus의 "임시 파일 사용"처럼 원본 보호).
- 탐색기로 끌어 놓기(가상 파일 `CFSTR_FILEDESCRIPTOR`/`FILECONTENTS`), zip 안의 zip 들어가기, 7z·RAR·TAR(Windows 11처럼 libarchive 등), 암호 zip(ZipCrypto·AES), Info-ZIP 유니코드 경로 추가 필드(0x7075).
- 파일 작업 대기열 기능이 병합되면 압축 풀기·압축도 대기열·진행률·실행 취소에 넣기.

## 구현 내용

- **순수 로직(테스트 프로젝트에서 단독 실행)**
  - `ZipArchivePath`: 가상 경로 분해(`TrySplit`)·결합, `.zip`만 Enter 대상(`IsBrowsableArchive`), zip 기반 확장자 + 시그니처면 `CanOpenAsZip`.
  - `ZipListing`/`ZipListingCache`: 항목 → 폴더 트리(암시적 폴더, 빈 폴더, 디렉터리 특성(DOS 0x10·Unix S_IFDIR), 같은 경로는 뒤의 것, 위험한 이름 숨김·개수·이름 10개), 크기·압축 크기·암호 항목 수, zip 크기·시각으로 최신 여부 판정. 캐시 최근 8개, 같은 zip 동시 요청 한 번만.
  - `ZipEntryNameEncoding`: .NET `ZipArchive`의 "플래그 없는 이름용 인코딩" 자리에 넣는 읽기 전용 `Encoding`(항목마다 ASCII → 엄격 UTF-8(C1 제어 문자 없을 때) → 엄격 CP949 → CP437).
  - `ZipArchiveService`: `PlanExtraction`(고른 항목을 대상 바로 아래, 복사 규칙이면 최상위 `이름 (2)`) → `Extract`(`.xxxx.folderss-part`에 쓰고 이름 바꿈, 충돌 모드 이름 바꾸기/건너뛰기/덮어쓰기(읽기 전용 특성 해제), 오류 모아 계속·100개면 멈춤, 취소는 남김, 풀기 직전 zip 변경 감지(크기·시각·항목 수·항목 이름)), `ReadEntryBytes`(미리보기), `CreateZip`, `SanitizeSegment`(`<>:"|?*`·제어 문자 → `_`, 끝 점·공백 → `_`, 장치 이름 앞 `_`), `EnsureInside`, `Describe`(사용자 문장).
  - `ZipTempStore`: `%TEMP%\Folderss\zip\<pid>-<시각>\<zip이름>-<해시>\<zip 안 경로>`에 열기용 사본(재사용, 읽기 전용 특성), `clip-xxx` 클립보드용 폴더, 프로세스 종료 때 정리, 하루 지난 다른 실행 폴더 수거(같은 pid의 Folderss가 실행 중이면 남김).
- **폴더 패널(`Controls/FolderBrowser.Archive.cs`, 기존 파일에는 훅만)**
  - `NavigateTo`가 실제 폴더가 아니면 zip으로 들어감(뒤로 기록, 검색 결과 필터 해제, 트리는 zip이 있는 폴더, zip 파일 감시). 실제 폴더로 가면 zip 상태 정리. `RefreshItems`·`NavigateUp`(↑·Backspace)·`UpdateStatusText`·경로 입력란·트리 루트(`FileSystemPath`)를 zip에 맞춤.
  - 목록은 캐시에 있으면 바로, 없으면 "읽는 중…" 후 표시. 없는 zip 안 폴더는 맨 위로(상태 줄 안내). 읽기 실패는 열기 전 폴더(또는 zip이 있는 폴더)로 돌아가 이유를 알림. 상위로 가면 나온 폴더/zip 파일을 선택.
  - zip 띠(`ArchiveBanner`): 읽기 전용 안내 + `압축 풀기`(zip 이름 새 폴더로 전체) + `▾`(옵션 대화상자) + `나가기`. 상태 줄에 항목 수·읽기 전용·위험한 경로 수(툴팁에 이름)·암호 항목 수.
  - 열기(더블클릭·Enter): zip 안 폴더면 이동, 파일이면 임시 사본 → 기존 `FileOpenRequested`(뷰어 탭/기본 프로그램). 실행 형식 확인, 처음 한 번 읽기 전용 안내, 상태 줄 안내. 암호 항목은 안내.
  - 미리보기: zip 안 텍스트(앞 10KB, 기존 `FilePreviewService.DecodeTextPreview` 규칙)·그림(32MB 이하)·메타정보(압축 전/후 크기, 읽기 전용, 암호). 폴더의 `.zip`을 고르면 요약(개수·크기·압축률·암호·위험한 이름·맨 위 100개)을 보이고 캐시를 채움.
  - 꺼내기: 끌어 놓기(앱 안 전용 데이터 `ArchiveDragPayload`, "복사할까요?" 확인), `Ctrl+C`(임시 폴더에 풀어 FileDrop, 512MB 넘으면 확인), zip 안 우클릭 메뉴(열기·반대편 패널로 복사·복사·압축 풀기…·전체 압축 풀기·전체 압축 풀기…·zip 밖으로·탐색기에서 zip 파일 보기). zip 안으로 끌어 넣기·잘라내기는 막음.
  - 폴더의 셸 우클릭 메뉴 위에 `압축 풀기 → '이름\'`(여러 zip이면 각각)·`압축 풀기…`·`zip으로 열어 보기`(zip 기반 확장자)·`zip으로 압축 → '이름.zip'`·`zip으로 압축…`.
  - 작업 결과 항목은 `SelectCreatedItem`으로 고르고, 곧이어 폴더 감시가 목록을 다시 만들어도 3초 안이면 다시 고른다(그 사이 사용자가 고른 것이 있으면 건드리지 않음).
  - 목록 행의 자동화 이름을 파일 이름으로(`AutomationProperties.Name`) — 화면 읽기 프로그램이 형식 이름 대신 파일 이름을 읽고, UIA 검증에도 씀.
- **메인 창(`MainWindow.Archive.cs`, 기존 파일에는 한 줄 가드)**: 삭제·이름 변경·새 폴더·새 파일·링크(반대편이 zip이어도)·두 파일/폴더 비교·즐겨찾기 추가·Git 창은 `BlockedInArchive`로 안내 후 중단. 이동(F6)은 zip에서 지워야 해서 막고, `ExecuteTransfer` 복사는 반대편으로 풂. 붙여넣기 대상이 zip이면 안내. 다음으로 열기 메뉴 숨김, 탐색기로 열기·탭 메뉴 탐색기는 zip 파일 선택, 콘솔·검색은 `FileSystemPath`. 세션·레이아웃 복원이 zip 가상 경로를 받음(`FolderOrArchiveExists`). ⋯ 메뉴에 `압축 풀기…`·`zip으로 압축…`.
- **대화상자(`ArchiveDialogs.cs`)**: `ArchiveExtractDialog`(고른 항목/전체, zip이 있는 폴더/반대편 패널/직접 지정(없으면 만듦), zip 이름 새 폴더, 같은 이름: 이름 바꾸기(기본)/건너뛰기/덮어쓰기(경고 + 확인 체크 전에는 버튼 꺼짐)), `ArchiveCompressDialog`(이름, 현재 폴더/반대편, 압축 수준 — 같은 이름이 있으면 거부), `ArchiveProgressWindow`(0.4초 넘을 때만, 취소·창 닫기 = 취소), `ArchiveOperations`(백그라운드 실행, 오류·취소·일부 실패 알림, zip 이름 폴더 풀기 + 루트 폴더 중복 제거).
- 플러그인 팝업 패널은 `ArchiveBrowsingEnabled = false`(계약상 `CurrentPath`가 실제 폴더), 폴더 비교 창(안전 모드)은 자동으로 꺼짐.

## 변경 파일

- 새 파일: `Folderss/Services/ZipArchiveService.cs`, `Folderss/Services/ZipEntryNameEncoding.cs`, `Folderss/Services/ZipTempStore.cs`, `Folderss/Controls/FolderBrowser.Archive.cs`, `Folderss/MainWindow.Archive.cs`, `Folderss/ArchiveDialogs.cs`, `tests/Folderss.SearchTests/ZipArchiveTests.cs`, `docs/items/zip-as-folder.md`
- 연결만: `Folderss/Controls/FolderBrowser.xaml`(zip 띠, 행 자동화 이름), `Folderss/Controls/FolderBrowser.xaml.cs`(훅 14곳), `Folderss/MainWindow.xaml`(⋯ 메뉴 2개), `Folderss/MainWindow.xaml.cs`(가드·경로 연결 18곳), `Folderss/Models/FileSystemItem.cs`(`ArchiveEntry`, 🔒), `Folderss/Services/FilePreviewService.cs`(`DecodeTextPreview` 분리), `Folderss/Services/PluginManager.cs`(팝업 패널 zip 끔), `tests/Folderss.SearchTests/Folderss.SearchTests.csproj`(소스 링크)
- 문서: `README.md`(기능·미리보기·아키텍처 요약), `docs/architecture.md`(파일 목록·zip 절), `AGENTS.md`·`CLAUDE.md`(주요 패턴 "폴더 패널 경로가 zip 안일 수 있음")

## 검증

- 빌드: `dotnet build .\Folderss.sln -c Debug` → 오류 0, Exit 0 (경고는 기존과 같은 CA1416 종류).
- 단위 테스트 `ZipArchiveTests` 27개 통과: 가상 경로 분해(폴더·zip 아닌 파일·jar 시그니처), 목록 트리(암시적·빈 폴더·크기·시각), CP949 이름(플래그 없음)·UTF-8(플래그 있음/없음)·NFD→NFC, 이름 판정 단위, 위험한 이름 숨김(`..`·`C:`·앞 `/`는 떼고 허용), 손상·잘린·없는 zip 문구, 암호 항목(목록 표시·풀기 실패 문구·미리보기 거부), 복사 규칙 `이름 (2)`·시각 보존·`.folderss-part` 없음, zip 안 하위 선택 풀기, 합치기 충돌 모드 3가지(읽기 전용 파일 덮어쓰기 포함), zip Slip 이중 검사, 이름 정리(`?`·장치 이름·끝 점), zip 변경 감지, 취소, 앞부분 읽기, zip 만들기(UTF-8 이름·빈 폴더·자기 자신 제외·이름 중복 시 `(2)`), 임시 사본(읽기 전용·재사용·정리)·오래된 폴더 수거.
- 전체 `dotnet test tests/Folderss.SearchTests`: 432개 중 428 통과, 4 건너뜀(기존 건너뜀), 실패 0 (`--blame-crash` 실행, 약 19분 — 실제 git 테스트가 오래 걸림).
  처음 한 번은 388개 진행 중 테스트 호스트가 멈춰 중단됐으나(실패 0), zip 테스트를 뺀 실행(405개)과 다시 한 전체 실행(432개) 모두 정상 종료해 재현되지 않았다.
- 실제 실행(격리 프로필 — `USERPROFILE`·`LOCALAPPDATA`·`TEMP`를 `%TEMP%\folderss-zip\` 아래로, UIA 패턴만, 키보드·마우스 주입·클립보드 사용 없음, 띄운 pid만 종료). CP949 이름 zip(`한글 폴더/보고서.txt`, `한글 폴더/하위/메모.md`, 빈 폴더, 그림, 암호 플래그 항목, 손상 zip)으로 44개 확인 모두 통과:
  - session.xml로 zip 안 하위 폴더(`…\시험.zip\한글 폴더`)에서 시작 → 한글 목록·zip 띠·상태 줄(`🔒 암호 항목 1개`)·zip 안 파일 미리보기(내용 일치)
  - ↑ → zip 맨 위(암시적·빈 폴더, 나온 폴더 선택), 암호 항목 메타정보 → ↑ → zip 밖(zip 파일 선택, 띠 숨김, zip 요약 미리보기 "파일 5개 · 폴더 4개"), 손상 zip 미리보기 안내
  - ← ← → (뒤로/앞으로로 zip 맨 위·하위 폴더 왕복 — 들어가기와 같은 `NavigateTo` 경로)
  - zip 띠 ▾ → 압축 풀기 대화상자(기본 "선택한 1개 항목") → 반대편 패널 → 파일이 디스크·반대편 목록에 생김(내용 일치)
  - zip 띠 `압축 풀기` → `자료\시험\` 아래 전체 풀림(하위·빈 폴더·그림), 암호 항목은 실패 안내 메시지 상자, `.folderss-part` 없음
  - ⋯ > 새 폴더 → "zip 읽기 전용" 안내, 디스크에 폴더 안 생김
  - 오른쪽 패널에서 ⋯ > `zip으로 압축…` → 기본 이름 `보고서.zip` → zip 생성(UTF-8 이름), 만든 zip 선택 유지
  - zip 맨 위에서 창 닫기(트레이로 숨김 = 세션 저장) → session.xml에 가상 경로 → 다시 시작하면 같은 zip 위치·목록 복원
  - 스크린샷: `C:\Users\USER\AppData\Local\Temp\claude\C--Users-USER-git-cshap-Folderss\b1e882b9-00a4-4b62-adf1-a51bf03c1731\scratchpad\zip-uia\shots\` (01~10)
- UIA로 확인하지 못한 것(입력 주입·클립보드 금지): 더블클릭/Enter로 들어가기·zip 안 파일 열기(뷰어 탭), 끌어 놓기, `Ctrl+C`, 우클릭 메뉴. 같은 함수(`NavigateTo`, `ZipTempStore.ExtractForOpen` + 기존 `FileOpenRequested`, `ArchiveOperations.ExtractAsync`)를 위 검증과 단위 테스트가 지난다. 사용자 확인 때 손으로 확인 필요.
- 이 환경의 Win32 메시지 상자는 UIA·Win32로 글자를 읽을 수 없어(자식 창 없음) 제목·클래스로 확인하고 내용은 스크린샷(07·08)으로 확인했다.

### 알려진 한계

- zip 안 수정 불가(읽기 전용), 암호 zip·7z·RAR 미지원, 탐색기로 끌어 놓기 불가, zip 안의 zip은 기본 프로그램으로 열림.
- Info-ZIP 유니코드 경로 추가 필드(0x7075)는 읽지 않는다(플래그 없는 이름은 위 판정만). 일본어·중국어 레거시 이름은 CP949로 읽히지 않으면 CP437로 보인다.
- zip 안 파일로 연 뷰어 탭은 임시 사본이 종료 때 지워져 다음 실행에 복원되지 않는다.
- 파일 작업 대기열(다른 작업)과 아직 합치지 않음 — 압축 풀기·압축은 자체 진행 창을 쓰고 실행 취소(Ctrl+Z) 기록에 남지 않는다.

## 변경 이력

- 2026-10-23: 항목 생성, 참고 제품 조사·설계 (In Progress)
- 2026-10-23: 1단계(읽기 전용 zip 보기·열기·꺼내기·압축 풀기·zip으로 압축) 구현, 단위 테스트 27개·전체 테스트·격리 프로필 UIA 실행 확인 (Ready for Verification — 사용자 확인 대기)

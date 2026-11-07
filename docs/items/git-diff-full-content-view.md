# Git diff 보기 모드: 변경 전/후 전체 내용만 보기

- 상태: Done — master 머지 확인 (이전: Ready for Verification — 테스트 통과, Windows 화면 확인 대기)
- 상태 전환일: 2026-11-07

## 요구사항

- 2026-11-02 요청: diff 창의 `전체 파일` 보기는 파일 전체를 보이지만 변경점(삭제 줄 섞임, +/− 색)을 함께 보인다.
  추가 옵션으로 **변경 전 전체 내용과 현재(변경 후) 전체 내용만** 보는 보기 모드를 둔다.

## 설계·결정

- 사용자가 지정하지 않아 다음으로 정함(바꿀 수 있음):
  - **양옆 두 칸**: 왼쪽 = 변경 전, 오른쪽 = 변경 후. 칸 위에 `변경 전`/`변경 후`(두 파일 비교 창은 파일 경로) 표시.
  - **색·줄 맞춤 없음**: 두 칸은 각자 원래 줄 번호로 그대로 보이고 **따로 스크롤**한다(줄을 맞추면 빈 줄이 끼어 "원래 내용"이 아니게 됨). 스크롤 동기화 없음.
  - 파일 머리줄(`diff --git …`, `index …`)은 양쪽에 남긴다(여러 파일 diff에서 파일 구분용). hunk 머리줄·`\ No newline at end of file`은 뺀다.
- 새 git 명령을 만들지 않고 `전체 파일`과 같은 `-U1000000` diff를 읽어 나눈다(`GitOutputParser.SplitSides`). 그래서 작업 트리·인덱스·커밋·stash·
  두 파일 비교·libgit2/CLI·UTF-16 재비교 경로가 모두 그대로 쓰인다. 표시 한도(2만 줄 diff)도 그대로 — 넘으면 양쪽 끝에 생략 안내.
- **공백 무시(`-w`)는 이 모드에서 적용하지 않는다.** `-w`면 공백만 바뀐 줄이 문맥 줄 하나(변경 후 모양)로 나와 변경 전 내용이 틀리게 보인다
  (`RealGit_FullContent_…` 테스트가 수정 전 코드에서 실패하는 것으로 확인). `GitDiffCommands.IgnoresWhitespace`를 CLI 인수(`WithViewMode`가 `--` 앞 `-w` 제거)·
  libgit2(`GitLibDiff`)·UTF-16 재비교(`GitEncodingDiff`)가 함께 쓴다.
- HTML 보고서 범위에는 넣지 않았다(요청 범위 밖). 이 모드에서 `HTML 보고서…`를 열면 범위가 `전체 파일`로 시작한다.
- 열거형 값은 끝에 추가(`FullContent` = 3) — 설정 저장은 이름이라 기존 설정과 호환, 콤보 순서 = 열거형 순서 유지.
- 충돌 파일(combined diff)은 앞 표시가 두 글자라 각 줄 앞에 한 글자가 남는다(이 모드에서 충돌 파일을 보는 경우는 고려하지 않음).

## 구현

- `Services/DiffSettingsService.cs` — `GitDiffViewMode.FullContent`.
- `Services/GitDiffCommands.cs` — `ContextOption`(FullContent도 `-U1000000`), `IgnoresWhitespace`, `WithViewMode`의 `-w` 제거.
- `Services/GitLibDiff.cs`, `Services/GitEncodingDiff.cs` — 같은 문맥·`-w` 규칙.
- `Services/GitOutputParser.cs` — `SplitSides`(삭제 → 변경 전, 추가 → 변경 후, 문맥 → 양쪽, 앞 표시 한 글자 제거, 줄 번호는 각 쪽 번호).
- `Controls/GitDiffView.xaml(.cs)` — 줄 스타일·템플릿을 리소스로 옮겨 두 목록이 공유, 오른쪽 칸(`NewPane`/`NewLineList`)·`GridSplitter`, 콤보 `변경 전/후 전체`.
  `Complete`가 모드가 FullContent면 나눠 보이고, `Clear`/`BeginLoad`/`ShowLines`/`Fail`은 한 칸으로 돌린다. `Ctrl+C`는 두 칸 모두.
- `SettingsWindow.xaml.cs` — 기본 보기 선택지 추가. `DiffReportDialog.cs` — 초기 범위.

## 검증

- `dotnet test tests/Folderss.SearchTests` (Linux, .NET 8 SDK): 1128 통과 / 5 건너뜀 / 0 실패.
  - `ZipArchiveTests` 2건이 Windows 구분자(`\`)를 고정해 Linux에서 실패하던 것을 `Path.DirectorySeparatorChar`로 바꿔 OS별로 맞춤(Windows에서는 기존과 같은 값).
  - 새 테스트: `GitTests.WithViewMode_FullContent_UsesWholeFileContext_AndDropsIgnoreWhitespace`, `WithViewMode_FullContent_KeepsPathNamedLikeOption`,
    `SplitSides_GivesOldAndNewContentWithoutMarks`, `RealGit_FullContent_ShowsExactOldAndNewFiles_EvenWithIgnoreWhitespace`;
    `GitLibDiffTests`에 FullContent + 공백 무시 요청이 CLI와 libgit2에서 같은지 2건.
- `Folderss.csproj` 빌드(Linux, `EnableWindowsTargeting`): `GitDiffView.xaml` BAML 생성 확인. 이 환경에서는 기존 `ConsolePanel`의 `Microsoft.Terminal` 참조 오류로 끝까지 빌드되지 않음 —
  **Windows에서 `dotnet build .\Folderss.sln -c Debug`와 화면 확인 필요**(양옆 칸 표시, 칸 나누기 끌기, 모드 전환 시 한 칸 복귀, 두 파일 비교 창 라벨).

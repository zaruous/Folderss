# Git 창 저장소별 설정(git config) 대화상자

- 상태: Ready for Verification

## 요구사항

- Git 창 왼쪽 저장소 목록에서 저장소를 선택하고 우클릭하면 `설정…` 컨텍스트 메뉴가 나온다.
- 고르면 그 저장소의 git 설정(인증 관련)을 보여 주고 저장할 수 있다.
- 글로벌 영역과 로컬 영역을 분리해서 보여 준다.

## 원인 분석 또는 설계

- 범위는 `--global`(~/.gitconfig)과 `--local`(저장소/.git/config) 둘만. `--system`은 관리자 권한이 필요해 제외.
- 편집 항목은 고정 목록(`GitConfigCommands.AuthFields`): `user.name`, `user.email`, `credential.helper`, `credential.username`,
  `credential.useHttpPath`, `core.sshCommand`. 비밀번호·토큰은 git config에 두는 것이 아니므로 다루지 않는다.
  그 밖의 키는 범위별 `전체 설정 보기`(읽기 전용)로만 보인다 — 임의 키 편집 UI는 요청 밖이라 넣지 않음.
- 읽기는 `git config --<scope> --list -z`(NUL 구분, 키\n값) 한 번씩. 글로벌 파일이 없으면 git이 128로 실패하므로 빈 설정으로 본다.
- 저장은 바뀐 키만: 값 있음 → `config --<scope> -- key value`, 비움 → `--unset`(원래 없던 키는 건너뜀 — unset은 없는 키에 실패).
  값이 여러 개인 키(`credential.helper` 다중 등록 등)는 git이 단일 set/unset을 거부(종료 코드 5)하므로 읽기 전용으로 보이고 저장에서 제외.
- 대화상자(`GitConfigDialog`)는 값만 모으고 실제 실행은 `GitWindow.RepoConfig_Click`이 `RunBusyAsync` 안에서 하나씩 실행해 출력 영역에 남긴다.
  한 항목이 실패하면 거기서 멈춘다(부분 적용 상태를 출력에서 확인 가능).

## 구현 내용

- `Services/GitConfigCommands.cs`(신규): `GitConfigScope`, `GitConfigEntry`, `GitConfigField`, `GitConfigChange`, `List/Set/Unset/Apply` 인수,
  `ParseList`, `ValuesOf`, `Diff`.
- `GitDialogs.cs`: `GitConfigDialog` — 글로벌/로컬 두 열, 항목별 TextBox(+ 힌트, 로컬이 비면 글로벌 값 안내), 전체 목록 Expander.
- `GitWindow.xaml`: `RepoList`에 ContextMenu(`설정…`), `PreviewMouseRightButtonDown`(우클릭한 항목 선택), `ContextMenuOpening`(선택 없음·작업 중이면 닫음).
- `GitWindow.xaml.cs`: `RepoList_PreviewMouseRightButtonDown`, `RepoList_ContextMenuOpening`, `RepoConfig_Click`, `ReadConfigAsync`.

## 변경 파일

- `Folderss/Services/GitConfigCommands.cs` (신규)
- `Folderss/GitDialogs.cs`, `Folderss/GitWindow.xaml`, `Folderss/GitWindow.xaml.cs`
- `tests/Folderss.SearchTests/Folderss.SearchTests.csproj`, `tests/Folderss.SearchTests/GitTests.cs`
- `README.md`, `docs/architecture.md`, `docs/items/git-repository-config-dialog.md`

## 검증

- `dotnet test tests/Folderss.SearchTests`: `ConfigParseList_*`, `ConfigDiff_*`(순수 로직), `RealGit_ConfigLocal_SetListUnset_RoundTrip_AndMultiValuedIsRejected`(실제 git) 통과.
- 리눅스 세션이라 WPF 실행 확인은 못 함. Windows에서 확인할 것: 우클릭 → `설정…` → 두 열 표시, 로컬 `user.email` 입력 후 저장 → `.git/config`에 반영,
  비우고 저장 → 제거, 글로벌 파일 없는 PC에서 열림.

## 변경 이력

- 2026-10-06: 최초 구현.

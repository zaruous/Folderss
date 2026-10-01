# GitHub 릴리스에서 플러그인 설치, 계약 버전 확인

- 상태: Ready for Verification

## 요구사항

1. zip 설치(`플러그인 찾기…`)는 유지한다.
2. GitHub 저장소(릴리스) 주소를 넣으면 최신 정식 릴리스를 찾아 그 zip을 직접 설치한다.
   - 릴리스에 zip이 여러 개면 목록에서 고른다.
   - 설치 출처를 기록하고, 같은 ID를 다른 출처에서 설치하면 강한 경고(확인 체크)를 거친다.
   - GitHub가 SHA-256(digest)을 주면 받은 파일과 비교한다.
3. 플러그인이 본체보다 높은 계약으로 빌드되었으면 설치 전에 막고 Folderss 업데이트를 안내한다.

## 원인 분석 또는 설계

- 계약 버전: 본체 계약 1.0 / 플러그인 계약 1.1 조합을 별도 실험(같은 구조의 AssemblyLoadContext)으로 확인한 결과, 플러그인이 새 멤버를
  쓰지 않아도 `FileNotFoundException: Contract, Version=1.1.0.0`으로 로드 자체가 실패했다. 반대(본체 2.0 / 플러그인 1.0·1.1)는 정상.
  → 기능 단위 `try/catch`로는 피할 수 없으므로 설치 시점에 진입점 DLL의 참조 버전을 메타데이터로 읽어(로드하지 않음) 비교한다.
- GitHub: `api.github.com/repos/<소유자>/<저장소>/releases/latest`의 `assets`를 쓴다. 자동 "Source code (zip)"은 assets에 없어 잘못 고를 일이 없다.
  `digest`(`sha256:…`)는 실제 응답에 있음을 확인(zaruous/Folderss v1.7.0). 비로그인 → 공개 저장소만, 호출 한도 IP당 시간당 60회.
- 출처 경고 이유: 플러그인 데이터는 id별 폴더(`plugin-data\<id>`)라, 같은 id를 쓰는 다른 플러그인이 기존 설정(DPAPI 비밀번호 포함 — 같은 Windows 사용자면 풀림)을 읽는다.
- 위험과 한계
  - 저장소 자체가 탈취되면 해시도 같은 곳에서 오므로 막지 못한다(전송 손상·변조만 막음). 출처가 같으면 경고도 없다.
  - 회사 프록시·SSL 검사로 실패할 수 있다. Windows 프록시와 현재 사용자 자격 증명을 쓰고, 오류는 삼키지 않고 문장으로 보여 준다.
  - 이 기능 이전에 설치한 플러그인은 출처 기록이 없어 일반 교체 확인만 한다.
  - "최신"은 GitHub Latest 표시 기준이다(버전 번호 아님). 버전 고정·업데이트 확인은 범위 밖.

## 구현 내용

- `Services/PluginGitHubSource.cs`: 주소 해석(https github.com만), 최신 릴리스 조회, 첨부 zip 받기(https github.com 주소만, 100MB 제한 — 선언 크기·Content-Length·실제 바이트), digest 비교,
  404·호출 한도·연결 실패·시간 초과를 사람이 읽을 문장으로.
- `Services/PluginSourceStore.cs`: `plugins\sources.json` 읽기·쓰기(`SettingsFile`, 실패는 예외, 손상 파일은 덮어쓰지 않음).
- `Services/PluginPackage.cs`: `ReadAssemblyReference`(System.Reflection.Metadata), `EnsureContractCompatible`.
- `PluginDialogs.cs`: `PluginGitHubInstallDialog`(확인 실패 시 창 유지), `PluginAssetChoiceDialog`, `PluginSourceChangeDialog`(경고 + 확인 체크, 기본 비활성).
- `SettingsWindow`: `GitHub에서 설치…` 버튼, zip·GitHub 공통 `InstallPluginPackage`(계약 확인 → 출처 비교 → 확인 → 등록 → 출처 기록), 제거 시 출처 기록 삭제. 설치 확인 문구에 출처 표시.

## 변경 파일

- `Folderss/Services/PluginGitHubSource.cs` (신규)
- `Folderss/Services/PluginSourceStore.cs` (신규)
- `Folderss/Services/PluginPackage.cs`
- `Folderss/PluginDialogs.cs` (신규)
- `Folderss/SettingsWindow.xaml`, `Folderss/SettingsWindow.xaml.cs`
- `tests/Folderss.SearchTests/PluginGitHubSourceTests.cs` (신규), `tests/Folderss.SearchTests/Folderss.SearchTests.csproj`
- `README.md`, `docs/architecture.md`, `docs/plugin-development.md`

## 검증

- `dotnet test tests/Folderss.SearchTests` (Linux): 전체 통과 — 새 테스트(주소 해석, 릴리스 응답·404·403/429 한도·연결 실패, 다운로드 해시 일치·불일치·없음, 비GitHub 주소·크기 초과 시 요청 없이 거부, 스트림 초과 중단, 출처 기록·손상 파일 보호, 계약 참조 읽기·비 .NET 파일, 계약 호환 판정) 포함.
- 실제 GitHub(Linux): `zaruous/Folderss` 최신 릴리스 조회 → 66MB 받기 → digest 일치 확인, 틀린 digest 거부.
- Folderss 빌드(Linux, `EnableWindowsTargeting`): 이번 변경과 무관한 Windows 전용 `Microsoft.Terminal` 참조 오류 1건 외 오류 없음. Windows 빌드는 PR CI로 확인.
- 확인하지 못한 것: Windows에서 대화상자 화면·테마, 실제 플러그인(DB Helper)의 GitHub 설치(아직 릴리스 없음), 회사 프록시 환경.

## 변경 이력

- 2026-10-01: 최초 구현.

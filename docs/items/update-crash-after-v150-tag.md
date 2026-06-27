# v1.5.0 업데이트 후 BadImageFormatException 크래시 수정

- 상태: Ready for Verification

## 내용

### 요구사항

- 태그 `v1.5.0` 이후 앱 내 업데이트를 수행한 뒤 발생한 시작 크래시를 수정한다.
- 업데이트 과정에서 실행 파일과 의존 DLL이 깨지지 않도록 한다.
- zip 배포본과 설치형 자산(`.exe`, `.msi`)을 구분해 안전하게 처리한다.

### 원인 분석 또는 설계

- 기존 업데이트 배치 파일은 `Assembly.GetExecutingAssembly().Location` 경로를 대상 파일로 사용했다.
- .NET 8 WPF 앱에서는 이 값이 `Folderss.exe`가 아니라 `Folderss.dll`이 될 수 있어, 새 `Folderss.exe`가 `Folderss.dll` 위에 복사되며 `BadImageFormatException`이 발생한다.
- zip 배포본도 단일 `exe`만 교체하고 있어 `deps.json`, `runtimeconfig.json`, DLL 버전이 함께 맞춰지지 않는 구조적 문제가 있다.

### 구현 내용

- 업데이트 대상 경로를 `Assembly.GetExecutingAssembly().Location` 대신 실제 프로세스 실행 파일 경로로 변경했다.
- zip 자산은 `Folderss.exe`만 꺼내 복사하지 않고, 필수 런타임 파일이 함께 있는 패키지 디렉터리 전체를 교체하도록 수정했다.
- 설치형 자산(`.exe`, `.msi`)은 현재 앱 파일을 덮어쓰지 않고 앱 종료 후 설치 프로그램 자체를 실행하도록 분리했다.
- GitHub Actions 릴리스 워크플로를 `dotnet publish` 기반으로 바꿔, 상위 `Release` 폴더 전체가 아니라 실제 배포 폴더만 zip으로 묶도록 수정했다.

### 변경 파일

- `Folderss/MainWindow.xaml.cs`
- `.github/workflows/release.yml`
- `README.md`
- `docs/architecture.md`
- `docs/items/update-crash-after-v150-tag.md`

### 검증

- `dotnet build .\Folderss.sln -c Release --no-restore -v:minimal`
- Release 산출물의 `Folderss.exe`와 `Folderss.dll`이 함께 다시 생성되는지 확인
- 업데이트 코드상 zip 배포본은 `exe + dll + deps.json + runtimeconfig.json`을 모두 갖춘 패키지만 허용하는지 확인
- 워크플로 정의가 `dotnet publish -> artifacts/Folderss-<tag> -> zip` 순서로 실제 배포 폴더만 압축하는지 확인

### 변경 이력

- 2026-06-27: 사용자 크래시 제보 접수, 원인 분석 착수.
- 2026-06-27: 업데이트 경로가 `Folderss.dll`을 잘못 덮어쓰는 문제와 zip 단일 파일 교체 문제 수정.
- 2026-06-27: Release 빌드 통과, 산출물 재생성 확인 후 사용자 검증 대기 상태로 전환.
- 2026-06-27: GitHub Actions 릴리스 자산 생성 경로도 배포 폴더 기준으로 정리.

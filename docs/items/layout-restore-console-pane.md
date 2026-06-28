# 종료 전 도킹 배치와 재시작 후 배치 불일치 수정

- 상태: Done

## 내용

### 요구사항

- 종료 직전의 즐겨찾기, 폴더 패널, 콘솔 패널 위치와 크기를 다음 실행에서 동일하게 복원한다.
- 왼쪽 폴더 아래에 분리된 콘솔이 왼쪽 폴더의 탭으로 합쳐지는 회귀를 방지한다.
- 가상 패널 기반 저장·복원 테스트로 결과를 검증한다.

### 원인 분석 또는 설계

- 창 닫기 버튼은 프로세스를 종료하지 않고 트레이로 숨기지만 기존 코드는 이 시점에 레이아웃을 저장하지 않았다.
- 실제 종료 경로는 콘솔 콘텐츠를 먼저 정리한 뒤 레이아웃을 저장해 종료 직전 UI 상태를 안전하게 보존하지 못했다.
- 저장 XML을 특정 배치로 강제 정규화하는 시도는 사용자가 만든 임의 배치를 훼손하므로 제거한다.

### 구현 내용

- 창 닫기 버튼으로 트레이에 숨기기 전에 현재 세션과 도킹 배치를 저장한다.
- 실제 종료 시 창이 보이는 경우에만 마지막 배치를 저장하고, 숨겨진 창에서는 직전에 저장한 정상 배치를 다시 덮어쓰지 않는다.
- 레이아웃을 임시 파일에 직렬화한 뒤 교체하고 형식 버전을 함께 기록한다.
- 버전 정보가 없는 기존 저장 파일에서 콘솔과 왼쪽 폴더가 같은 탭인 경우에만 콘솔을 왼쪽 하단 패널로 한 번 이관한다.
- 기본 배치와 닫힌 콘솔 재생성 위치를 왼쪽 폴더 아래로 통일한다.
- 저장 XML을 특정 고정 배치로 매번 재작성하던 정규화 시도를 제거해 이후 사용자 배치를 그대로 보존한다.
- 실제 WPF 창에 가상 패널을 연결한 저장·복원·재저장 회귀 검증 프로그램을 추가한다.

### 변경 파일

- `Folderss/MainWindow.xaml`
- `Folderss/MainWindow.xaml.cs`
- `Folderss/Services/DockLayoutService.cs`
- `Folderss.LayoutTests/Folderss.LayoutTests.csproj`
- `Folderss.LayoutTests/Program.cs`
- `Folderss.sln`
- `README.md`
- `docs/architecture.md`
- `AGENTS.md`

### 검증

- `dotnet run --project Folderss.LayoutTests\Folderss.LayoutTests.csproj -c Debug`: 성공. 별도 하단 콘솔 패널이 저장, 복원, 재저장 후에도 유지됨.
- `dotnet build .\Folderss.sln -c Debug --no-restore -v:minimal`: Exit 0, 오류 0.
- `dotnet build .\Folderss.sln -c Release --no-restore -v:minimal`: Exit 0, 오류 0. 평소 실행 경로에 새 바이너리 반영.
- Visual Studio MSBuild 직접 실행은 설치된 VS가 .NET SDK를 찾지 못해 실패했으며, 현재 SDK 형식 프로젝트에 맞춰 `AGENTS.md` 빌드 명령을 수정함.
- NuGet 취약성 데이터 조회 경고와 기존 ConPTY 런타임 식별자 경고가 있으나 빌드 결과에는 영향 없음.
- 현재 아이템 관리 기준에 따라 이 파일에서 상태와 상세 내용을 관리함.

### 변경 이력

- 2026-06-27: 문제 재현 자료와 현재 저장 XML 확인, 작업 착수.
- 2026-06-27: 종료 저장 순서 수정, 기존 레이아웃 1회 이관, 기본 배치 통일, 회귀 테스트 및 전체 빌드 완료. 사용자 확인 대기.
- 2026-06-27: 사용자가 결과 반영과 완료 처리를 요청해 항목 상태를 `Done`으로 변경.

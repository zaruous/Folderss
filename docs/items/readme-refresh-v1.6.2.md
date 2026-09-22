# README.md 최신화 (v1.6.2 기준)

- 상태: Ready for Verification

## 요구사항

README.md가 v1.4.5 기준으로 멈춰 있어, 현재 코드(v1.6.2 태그 + 트리뷰 선택 새로고침 커밋)와 맞게 전체를 최신화한다.

## 원인 분석 또는 설계

- README의 버전 표기, 아키텍처 요약, 기능 목록, 빌드 절차가 여러 릴리스 동안 부분 갱신만 되어 실제 코드와 어긋난 부분이 있었다.
- 실제 소스 트리(`Controls/`, `Viewers/`, `Models/`, `Services/`, `Converters/`, `tests/`), `MainWindow.xaml` 메뉴, `KeyBindingService` 기본 매핑, `ViewerConfigService` 기본 매핑, `Folderss.csproj` 패키지 목록, `.github/workflows/release.yml`, `docs/items/*.md`를 대조해 반영했다.

## 구현 내용

- 현재 버전을 `v1.6.2`로 갱신 (최신 git 태그 기준).
- 아키텍처 요약에 `Models/SearchTarget`, `Converters/FractionToStarConverter`, `Viewers/Resources/`, `tests/Folderss.SearchTests` 추가.
- 설계 특징에 뷰어 외부 변경 감지, 즐겨찾기·패널 잠금 설정 파일, 임시 파일 교체 저장 방식 추가.
- 기능 목록을 탐색·트리뷰·패널/뷰어/도구로 나누고 다음을 추가: 경로 입력란 `Ctrl+V` 경로 붙여넣기, 트리뷰 폴더 `Ctrl+C` 복사, 복사 시 이름 텍스트 포맷 동봉, 파일 즐겨찾기, 문서 탭 컨텍스트 메뉴(탐색기로 열기 포함), 뷰어 `다시 읽기` 버튼, 뷰어 내 `Ctrl+F`, 콘솔 `Ctrl+C` 선택 인식 복사, 파일 메타데이터 경로 표시.
- 내장 뷰어 표에 Markdown 문서 내 검색·상대 경로 이미지·로컬 링크 새 탭·우클릭 인쇄/저장/공유, Monaco 내장 검색·외부 변경 감지를 반영하고 HTML 내보내기의 상대 경로 이미지 제약을 명시.
- 메뉴 명칭을 실제 XAML과 일치시킴 (`보기 > 기본 도킹 배치로 초기화`, 메인 메뉴 `폴더 패널 추가...`).
- 단축키 절에 고정 키 표 추가, `docs/keyboard-shortcuts.md` 링크.
- 빌드 절: Visual Studio를 선택 사항으로 정리, `System.IO.FileSystem.AccessControl` 패키지 추가, `MSBuild(커맨드라인)` 제목을 `커맨드라인 빌드`로 변경, 테스트(`dotnet test tests\Folderss.SearchTests`)와 릴리스 워크플로 절 추가.
- 문서 절 추가 (`CLAUDE.md`, `docs/architecture.md`, `docs/PROJECT.md`, `docs/items/`, `docs/done/DONE.md`, `docs/keyboard-shortcuts.md`, `docs/설정/콘솔설정.md`).

## 변경 파일

- `README.md`
- `docs/items/readme-refresh-v1.6.2.md` (신규)

## 검증

- [x] `dotnet build .\Folderss.sln -c Debug` — Exit 0, 오류 0 (기존 CA1416 경고만 존재)
- [x] Debug 빌드 실행 확인
- [ ] README 내용이 실제 동작과 일치하는지 사용자 확인

## 참고

- `Properties/AssemblyInfo.cs`는 `1.6.1.0`으로 남아 있고 `v1.6.2` 태그도 이 값을 가리킨다. 릴리스 워크플로가 태그에서 버전을 덮어쓰므로 배포 바이너리는 1.6.2이지만, 로컬 빌드의 정보 창은 1.6.1로 표시된다. README 최신화 범위 밖이라 수정하지 않았다.

## 변경 이력

- 2026-09-22: README 전체 최신화, 항목 생성 (Ready for Verification)

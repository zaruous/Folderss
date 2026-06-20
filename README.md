# Folderss

Windows용 듀얼 패널 파일 관리자입니다. WPF와 .NET Framework 4.8로 구현했으며, AvalonDock을 활용한 자유로운 패널 도킹·탭·분리 창을 지원합니다.

![Folderss 스크린샷](<스크린샷 2026-06-20 122326.png>)

## 개요

경로 탐색, 검색, 파일 목록은 재사용 가능한 `Controls/FolderBrowser` 컴포넌트 하나로 구성되며, 이 컴포넌트를 AvalonDock 레이아웃 위에 여러 개 배치합니다. 왼쪽에는 즐겨찾기 패널, 아래에는 선택 파일 미리보기·메타정보 영역이 붙어 있습니다.

### 아키텍처 요약

```
Folderss/
├── Controls/
│   ├── FolderBrowser       — 핵심 파일 브라우저 컨트롤 (패널 재사용 단위)
│   └── FavoritesPanel      — 즐겨찾기 패널
├── Models/
│   ├── FileSystemItem      — 파일·폴더 뷰모델
│   └── FavoriteLocation    — 즐겨찾기 항목 모델
├── Services/
│   ├── FileOperationService    — 복사·이동·삭제·이름변경·새 폴더
│   ├── FilePreviewService      — 텍스트·이미지 미리보기 + 메타데이터
│   ├── DockLayoutService       — AvalonDock 레이아웃 저장·복원 (XML)
│   ├── SessionStateService     — 열린 폴더 경로 세션 저장·복원 (XML)
│   ├── FavoritesService        — 즐겨찾기 목록 저장·복원
│   ├── ShellContextMenuService — Windows 쉘 우클릭 컨텍스트 메뉴
│   └── ThemeManager            — 블랙·라이트 테마 전환 및 저장
├── Themes/
│   ├── Black.xaml / Light.xaml — 테마 색상 정의
│   └── Controls.xaml           — 공통 컨트롤 스타일
├── MainWindow               — 메인 창 및 AvalonDock 호스트
└── PromptWindow             — 이름 변경·새 폴더 입력 다이얼로그
```

### 설계 특징

- **`FolderBrowser` 재사용**: 좌·우 기본 패널과 추가 패널 모두 동일한 컨트롤 인스턴스를 사용합니다.
- **비동기 미리보기**: 파일 미리보기는 `Task.Run`으로 백그라운드에서 읽고, 요청 ID 비교로 레이스 컨디션을 방지합니다.
- **이동 이력**: 뒤로·앞으로 이동 이력을 패널별 스택으로 관리하며 최대 10개까지 유지합니다.
- **세션 복원**: 종료 시 열린 폴더 경로·활성 패널(`session.xml`)과 도킹 배치(`dock-layout.xml`)를 `%LOCALAPPDATA%\Folderss\`에 저장하고 다음 실행 시 복원합니다.
- **테마**: `ResourceDictionary` 교체 방식으로 런타임에 즉시 전환합니다.

## 현재 기능

- 좌우 듀얼 패널과 크기 조절
- 경로 직접 입력
- 뒤로, 앞으로, 상위 폴더 이동
- 현재 폴더 이름 검색
- 파일 실행
- 파일 및 폴더의 Windows 컨텍스트 메뉴
- 반대편 패널로 복사 및 이동
- 이름 변경, 새 폴더, 휴지통 삭제
- 다중 선택 파일 작업
- 블랙/라이트 테마 실시간 전환 및 사용자 설정 저장
- AvalonDock 기반 패널 도킹, 탭, 분리 창, 자동 숨김
- 도킹 배치 자동 저장 및 복원
- 왼쪽 즐겨찾기 패널과 사용자별 즐겨찾기 저장
- 폴더 목록 하단의 선택 파일 미리보기와 메타정보

## 파일 미리보기

- 이미지 파일은 미리보기 영역 크기에 맞춰 표시합니다.
- 일반 파일은 처음 10KB까지 텍스트로 표시하며 바이너리 파일은 안내 문구를 표시합니다.
- 이름, 형식, MB 단위 크기, 생성·수정일자, 현재 사용자 권한과 파일 특성을 표시합니다.
- 목록과 미리보기 사이, 내용과 메타정보 사이의 분할선을 드래그해 크기를 조절할 수 있습니다.

## 도킹과 즐겨찾기

- 폴더 패널 탭을 드래그해 좌우·상하로 도킹하거나 별도 창으로 분리할 수 있습니다.
- 즐겨찾기 패널은 도킹, 자동 숨김, 분리 및 닫기를 지원합니다.
- `보기 > 즐겨찾기 표시`로 닫힌 즐겨찾기를 다시 표시합니다.
- 즐겨찾기의 `+` 버튼은 현재 활성 폴더를 추가합니다.
- 툴바의 `+ 패널`, `파일 > 폴더 패널 추가` 또는 `Ctrl+T`로 폴더 패널을 추가할 수 있습니다.
- 추가한 폴더 패널도 이동·탭 병합·분리할 수 있으며 다음 실행 시 복원됩니다.
- 각 폴더 패널의 뒤로·앞으로 이동 내역은 최근 10개까지 유지됩니다.
- 앱 종료 시 열려 있던 폴더 위치와 활성 폴더를 저장하고 다음 실행 시 복원합니다.
- 도킹 배치는 앱 종료 시 저장되고 다음 실행 시 복원됩니다.

## 테마

`설정` 메뉴에서 블랙 테마와 라이트 테마를 전환할 수 있습니다. 마지막 선택은 사용자별로 저장됩니다.

테마 색상과 공통 컨트롤 스타일은 다음 파일로 분리되어 있습니다.

```text
Folderss\Themes\Black.xaml
Folderss\Themes\Light.xaml
Folderss\Themes\Controls.xaml
```

## 단축키

- `F2`: 이름 변경
- `F5`: 반대편 패널로 복사
- `F6`: 반대편 패널로 이동
- `Delete`: 휴지통으로 이동
- `Shift+Delete`: 영구 삭제
- `Ctrl+R`: 양쪽 패널 새로 고침
- `Ctrl+Shift+N`: 새 폴더

## 실행

Visual Studio에서 `Folderss.sln`을 열고 실행하거나 다음 파일을 실행합니다.

```text
Folderss\bin\Debug\Folderss.exe
```

Build Tools로 빌드하려면:

```powershell
& 'C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe' .\Folderss.sln /t:Rebuild /p:Configuration=Debug
```

> 기본 삭제는 Windows 휴지통으로 이동합니다. `Shift+Delete`는 휴지통을 거치지 않고 영구 삭제합니다.

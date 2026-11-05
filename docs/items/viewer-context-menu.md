# 파일 패널 우클릭 "뷰어 설정" 하위 메뉴

- 상태: Done — v1.9.1 (사용자 실제 창 확인 완료, 2026-11-05)

## 요구사항 (2026-11-05)

파일 패널 우클릭 메뉴에 `뷰어 설정`을 추가하고, 하위 메뉴에서 프로그램이 제공하는 내장 뷰어를 그 확장자에 쉽게 등록할 수 있게 한다.

## 구현

- `Controls/FolderBrowser.ViewerMenu.cs`(partial): 셸 우클릭 메뉴 맨 위 앱 항목에 `뷰어 설정 (.json) ▸` 추가(경로 복사 앞).
  - 하위 항목: `GetViewerKeys()` 순서의 뷰어(System Default·Markdown·Block Editor·Monaco·Text). 현재 연결된 뷰어에 체크,
    확장자 하나일 때 프로그램 기본 매핑에 `(기본값)` 표시.
  - 구분선 뒤 `기본값으로 되돌리기`(재정의된 확장자가 있을 때만), `설정 창에서 열기…`(→ `ViewerSettingsRequested` → `OpenSettings(this, "Viewers")`).
  - 폴더·확장자 없는 파일은 대상에서 뺀다. 대상 확장자가 없으면 메뉴를 넣지 않는다. 여러 확장자는 모두에 적용, 모두 같은 뷰어일 때만 체크.
  - 적용 후 상태 줄에 `.json → Monaco (새로 여는 파일부터 적용)`을 잠깐 표시.
- `ViewerConfigService`: `SetMapping`·`ResetMapping`(바로 저장, 실패는 예외)·`IsOverridden`·`GetDefaultMappingKey`·`GetDisplayName` 추가.
  표시 이름을 서비스로 옮겨 설정 창 `ToViewerDisplayName`이 이를 쓴다.
- `ShellContextMenuService.CustomMenuItem.Checked`(MF_CHECKED) 추가.
- `MainWindow.AttachFolderBrowser`: `browser.ViewerConfig = _viewerConfigService`(뷰어 탭과 같은 인스턴스) + 이벤트 연결.

## 결정

- 설정 창을 거치지 않고 즉시 저장한다(우클릭 한 번으로 등록이 목적). 저장 실패는 `설정 저장 실패` 경고로 알리고 이번 실행에는 적용된다.
- 기본 매핑이 없는 확장자에 System Default를 고르면 재정의를 지운다(매핑 없음 = 시스템 기본이라 같은 결과, 파일에 잔여물을 남기지 않음).
  `.md`처럼 기본 매핑이 있는 확장자의 System Default는 재정의로 남긴다.
- 이미 열린 뷰어 탭은 바꾸지 않는다(저장 안 된 편집이 있을 수 있음). 새로 여는 파일부터 적용.
- zip 안 보기·트리뷰 우클릭·안전 모드(폴더 비교 창)에는 넣지 않았다 — 각각 별도 메뉴 경로라 범위 밖.

## 검증

- `dotnet build .\Folderss.sln -c Debug` — 오류 0
- `dotnet test tests/Folderss.SearchTests --filter ViewerConfigServiceTests` — 18개 통과(새 7개: 즉시 저장·확장자 정규화, 기본값과 같으면 재정의 제거,
  System Default 처리 두 경우, 되돌리기, 기본 키 조회, 쓰기 실패 시 예외 + 메모리 유지)
- Release 빌드 후 사용자 실제 창 확인 완료(2026-11-05)

# 트리뷰 우클릭 Windows 컨텍스트 메뉴

- 상태: Done

## 요구사항

트리뷰에서 폴더를 우클릭하면 Windows 쉘 컨텍스트 메뉴가 표시되어야 한다.

## 원인 분석 또는 설계

파일 목록(`FileList_MouseRightButtonUp`)은 이미 `ShellContextMenuService.Show`로 쉘 메뉴를 띄우고, `OpenWithService`의 사용자 지정 "Open with" 항목을 함께 노출한다. 트리뷰에도 같은 패턴을 적용한다.

- 우클릭한 `TreeViewItem`의 `Tag` 경로를 대상으로 쉘 메뉴 표시. 빈 영역 우클릭 시 트리 루트 폴더를 대상으로 표시
- 우클릭으로 트리 선택(=탐색)을 바꾸지 않는다 — 선택 변경 없이 메뉴만 표시 (탐색기 트리와 동일한 감각)
- 쉘 메뉴로 이름변경·삭제·새 폴더 등이 수행될 수 있으므로 메뉴 종료 후 갱신 필요
  - 클릭한 항목의 부모 노드 자식만 다시 로드해 상위 트리의 펼침 상태를 보존 (`RefreshTreeAfterShellAction`)
  - 루트 항목이나 빈 영역이면 전체 트리 재구성
  - 파일 목록도 함께 갱신(`RefreshItems`)
- 기존 지연 로딩 코드(`EnsureTreeChildrenLoaded`)의 자식 로드 부분을 `ReloadTreeChildren`으로 분리해 재사용

## 구현 내용

- `FolderTree`에 `MouseRightButtonUp` 핸들러 추가
- 우클릭 대상 경로로 `ShellContextMenuService.Show` 호출, `OpenWithService` 매칭 항목을 커스텀 메뉴로 병합 (파일 목록과 동일 패턴)
- 메뉴 종료 후 부모 노드 자식 재로드 + 현재 경로 재선택, 루트 대상이면 전체 재구성
- `EnsureTreeChildrenLoaded`를 `ReloadTreeChildren` 재사용 구조로 리팩터링

참고: 쉘 컨텍스트 메뉴를 통한 작업은 폴더 고정 잠금의 차단 대상이 아니다 (기존 문서화된 한계와 동일).

## 변경 파일

- `Folderss/Controls/FolderBrowser.xaml` — `FolderTree`에 `MouseRightButtonUp` 연결
- `Folderss/Controls/FolderBrowser.xaml.cs` — 트리 우클릭 쉘 메뉴, 메뉴 후 트리 부분 갱신, 자식 로드 리팩터링
- `README.md` — 현재 기능 목록 반영
- `docs/items/tree-view-shell-context-menu.md`

## 검증

- [ ] `dotnet build .\Folderss.sln -c Debug` 성공 (`Exit: 0`) — 작업 환경(Linux, 네트워크 정책상 SDK 설치 불가)에서 빌드 불가, Windows 개발 환경에서 확인 필요
- [ ] 트리뷰 폴더 우클릭 시 Windows 컨텍스트 메뉴 표시, 선택(탐색)은 변경되지 않음
- [ ] 트리뷰 빈 영역 우클릭 시 트리 루트 폴더 기준 메뉴 표시
- [ ] 사용자 지정 "Open with" 항목이 메뉴 상단에 표시
- [ ] 쉘 메뉴로 이름변경·삭제·새 폴더 수행 후 트리와 파일 목록 갱신, 상위 펼침 상태 유지

## 변경 이력

- 2026-07-02: 작업 항목 생성, 트리뷰 우클릭 쉘 컨텍스트 메뉴 구현
- 2026-07-02: 사용자 확인 완료, 상태를 Done으로 변경

# 다른 마크다운 문서의 제목 링크(other.md#제목)로 이동

- 상태: Done

## 요구사항

`[설치](guide.md#설치-방법)`처럼 다른 문서의 제목을 가리키는 링크를 누르면 그 문서를 열고(이미 열려 있으면 그 탭으로 가서) 해당 제목으로 이동해야 한다.
같은 문서를 파일 이름으로 가리키는 링크(`this.md#제목`)는 탭을 다시 열지 않고 제목으로만 이동한다.

## 원인 분석 또는 설계

- 링크 클릭은 `FileOpenRequested(path)`로 파일 경로만 넘겨 `#제목`이 버려졌다.
- 이벤트 형식(`EventHandler<string>`)과 `MainWindow`의 탭 열기 흐름을 바꾸지 않도록, 링크를 누른 뷰어가 정적 표
  `PendingAnchors[대상 전체 경로] = 조각`에 넣고, 그 파일을 맡는 마크다운 뷰어가 꺼내 쓴다.
  - 새 탭: 페이지에 문서를 연 직후(`CallAppOpen` 끝).
  - 이미 열린 탭: `MainWindow.TryActivateExistingViewerDocument`가 부르는 `SetActive(true)` 때.
  - 페이지가 아직 준비되지 않았으면 꺼내지 않고 남겨 두었다가 준비 후 처리한다.

## 구현 내용

- `MarkdownViewer.xaml.cs`
  - `HandleLinkClick`: `.md` 링크의 `#조각`을 `ExtractFragment`로 떼어, 대상이 현재 문서면 `ScrollToAnchorAsync`, 아니면 `PendingAnchors`에 넣고 파일 열기 요청.
  - `ConsumePendingAnchorAsync`(`CallAppOpen` 끝, `SetActive(true)`에서 호출), `ScrollToAnchorAsync`(`app.scrollToAnchor` 호출).
- `markdown-app.js`: `scrollToAnchor(fragment)`(퍼센트 인코딩 해제 후 제목으로 스크롤·목차 활성) 추가, `app.scrollToAnchor`로 공개. 문서 안 `#` 링크 클릭도 같은 함수를 쓴다.

## 변경 파일

- `Folderss/Viewers/MarkdownViewer.xaml.cs`
- `Folderss/Viewers/Resources/markdown-app.js`
- `docs/items/markdown-viewer-cross-file-anchor.md`

## 검증

- `dotnet build .\Folderss.sln -c Debug` — 오류 0개.
- 헤드리스 Edge: `app.scrollToAnchor(encodeURIComponent('설치-방법'))` 후 목차 활성 항목이 "설치 방법".
- 앱에서 확인 필요: 확인용 폴더의 `변경-확인.md` → `anchor-target.md#두-번째-제목` 링크(새 탭으로 열리며 해당 제목으로 이동),
  같은 링크를 다시 눌렀을 때(이미 열린 탭으로 이동 후 제목 이동), 같은 문서 링크(`변경-확인.md#5-검색`).
- 알려진 제약: 대상 문서가 Edit 모드로 기억돼 있으면 미리보기가 숨겨져 있어 이동하지 않는다. 대상 파일이 Monaco 등 다른 뷰어로 매핑돼 있으면 파일만 열린다.

## 변경 이력

- 2026-10-15: 구현, 빌드·헤드리스 확인. 앱 수동 확인 대기.
- 2026-10-15: 사용자 앱 확인 결과 이슈 없음 → Done.

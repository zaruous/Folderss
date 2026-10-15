# 마크다운 편집기 이미지 붙여넣기

- 상태: Done

## 요구사항

화면 캡처 등 클립보드 이미지를 편집기에 붙여넣으면 문서 옆에 이미지 파일로 저장하고, 커서 위치에 상대 경로 이미지 링크를 넣는다.

## 원인 분석 또는 설계

- 페이지 스크립트는 파일을 쓸 수 없으므로, JS가 이미지 바이트(base64)를 `paste-image` 메시지로 보내고 C#이 저장한 뒤 `app.insertText`로 링크를 넣는다.
- 저장 위치는 문서 폴더의 `images/`, 이름은 `<문서이름>-<yyyyMMdd-HHmmss>.<확장자>`(겹치면 `-1`, `-2`). 기존 파일은 덮어쓰지 않는다.
- 클립보드에 텍스트도 있으면(예: 웹 페이지 복사) 기본 붙여넣기를 따른다. PNG·JPEG·GIF·WebP·BMP만 받는다.
- 링크의 파일 이름은 공백·괄호·`%`를 퍼센트 인코딩해 마크다운 링크가 끊기지 않게 한다(미리보기 쪽 `ResolveLocalLinkPath`가 다시 푼다).

## 구현 내용

- `markdown-app.html`
  - 편집기 `paste`: 텍스트 없이 이미지 파일만 있으면 기본 동작을 막고 `FileReader`로 base64를 만들어 `{ type: 'paste-image', mime, data }` 전송.
  - `app.insertText(text)`: 편집기에 포커스를 주고 `insertText`(실행 취소 가능). 그 사이 Preview로 바꿔 편집기가 숨겨졌으면 `setRangeText`로 넣고 `input` 이벤트를 발생시킨다.
- `MarkdownViewer.xaml.cs`
  - `OnWebMessageReceived`에 `paste-image` 추가 → `SavePastedImage(mime, data)`: 형식 확인, `images/` 생성, 저장, 링크 삽입. 실패 시 오류 창.

## 변경 파일

- `Folderss/Viewers/MarkdownViewer.xaml.cs`
- `Folderss/Viewers/Resources/markdown-app.html`
- `docs/keyboard-shortcuts.md`
- `README.md`
- `docs/items/markdown-editor-image-paste.md`

## 검증

- `dotnet build .\Folderss.sln -c Debug` — 오류 0개.
- 헤드리스 Edge: `app.insertText`가 Edit 모드와 편집기가 숨겨진 Preview 모드 모두에서 텍스트를 넣음.
- 앱에서 확인 필요: `Win+Shift+S` 캡처 후 편집기에서 Ctrl+V → `images/문서-날짜.png` 생성, `![...](images/...)` 삽입, 미리보기에 이미지 표시.
  공백이 있는 문서 이름, 같은 초에 두 번 붙여넣기(`-1`), 텍스트 붙여넣기는 기존과 같은지.

## 변경 이력

- 2026-10-15: 구현, 빌드·헤드리스 테스트 통과. 앱 수동 확인 대기.
- 2026-10-15: 사용자 앱 확인 결과 이슈 없음 → Done.

# Markdown 미리보기 이미지 확대와 이동

- 상태: Done

## 요구사항
- Markdown 미리보기와 분할 보기의 이미지를 클릭하면 확대 화면을 연다.
- 확대 화면에서 Ctrl+휠로 포인터 기준 확대·축소하고 왼쪽/가운데 버튼 드래그로 이동한다.
- 닫기와 Esc로 확대 화면을 닫는다.

## 원인 분석 또는 설계
- `markdown-app.js`는 미리보기 이미지를 렌더링하지만 이미지 클릭 처리는 없고, `markdown-app.html`의 이미지 스타일은 미리보기 너비에만 맞춘다.
- 미리보기 DOM의 이미지 클릭을 받아 뷰어 전용 오버레이에 동일한 이미지 주소를 표시한다. 배율과 이동량은 오버레이 안에서만 관리한다.
- 오버레이는 미리보기 DOM 밖에 두어 출력용 HTML/PDF 복제에 포함되지 않도록 한다.

## 구현 내용
- 미리보기·분할 보기 이미지 클릭으로 뷰어 전용 확대 화면을 연다. 이미지가 링크 안에 있어도 이미지 클릭은 확대 화면으로 처리한다.
- 이미지는 처음에 확대 화면에 맞춰 표시하고, Ctrl+휠은 포인터 위치를 기준으로 확대·축소한다(상한 800%, 하한 10% 또는 더 작은 초기 맞춤 배율).
- 왼쪽/가운데 버튼 드래그로 이미지를 이동하고, 닫기 버튼 또는 Esc로 확대 화면을 닫는다.
- 확대 화면은 미리보기 DOM 밖에 두고 인쇄 CSS에서도 숨겨 HTML/PDF 출력에 포함되지 않도록 했다.
- README와 아키텍처 문서에 조작법을 반영했다.

## 변경 파일
- `Folderss/Viewers/Resources/markdown-app.js`
- `Folderss/Viewers/Resources/markdown-app.html`
- `README.md`
- `docs/architecture.md`
- `docs/items/markdown-preview-image-zoom-pan.md`

## 검증
- `node --check Folderss\Viewers\Resources\markdown-app.js`: Exit 0.
- `dotnet build .\Folderss.sln -c Release`: Exit 0, 경고/오류 0개.
- 사용자 재요청에 따른 Release 재빌드: Exit 0, 경고/오류 0개.
- `git diff --check`: Exit 0.
- WebView2에서 이미지 클릭과 마우스 조작은 실제 앱 확인 필요.

## 변경 이력
- 2026-11-08: 요청 접수 및 구현 계획 작성. 개발 착수.
- 2026-11-08: 미리보기 이미지 확대 화면과 확대·이동 조작 구현, 문서 및 빌드 검증 후 Ready for Verification으로 변경.
- 2026-11-08: Release 구성을 다시 빌드해 성공 확인.
- 2026-11-08: 사용자가 결과를 승인하여 Done으로 변경.

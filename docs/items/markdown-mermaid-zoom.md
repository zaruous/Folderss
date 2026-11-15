# Markdown 미리보기 Mermaid 다이어그램 확대 보기

- 상태: Done

## 요구사항
- Mermaid 다이어그램이 미리보기 너비에 맞춰 작게 그려져 읽기 어렵다. 이미지처럼 클릭하면 최대 크기로 볼 수 있게 한다.

## 설계
- 기존 이미지 확대 화면(`#mdv-image-viewer`, `docs/items/markdown-preview-image-zoom-pan.md`)을 재사용한다.
- 다이어그램은 렌더링된 SVG를 복제해 `#mdv-diagram-full`에 넣고, 확대·이동 변환을 이미지 대신 이 요소에 적용한다(`zoomTarget`).
- 원래 크기는 SVG `viewBox`(없으면 화면 크기)로 정한다. 벡터라 처음 배율은 확대 화면에 맞춤(1 초과 허용). 이미지는 기존대로 최대 100%로 맞춘다.
- Ctrl+휠 상한은 800% 또는 처음 맞춤 배율 중 큰 값.
- 복제본은 미리보기 DOM 밖에 있고 닫을 때 비우므로 HTML/PDF 내보내기에 포함되지 않는다.

## 변경 파일
- `Folderss/Viewers/Resources/markdown-app.js` — `openDiagramViewer`, 미리보기 클릭 처리, `zoomTarget`
- `Folderss/Viewers/Resources/markdown-app.html` — `#mdv-diagram-full`, 제목 `#mdv-image-title`, 다이어그램 `cursor: zoom-in`
- `README.md`, `docs/architecture.md`

## 검증
- `node --check Folderss\Viewers\Resources\markdown-app.js`: Exit 0
- `dotnet build .\Folderss.sln -c Debug`: Exit 0, 오류 0개
- WebView2에서 다이어그램 클릭·확대·이동은 실제 앱 확인 필요

## 변경 이력
- 2026-11-15: 요청 접수, 구현, 빌드 확인 후 Ready for Verification.
- 2026-11-15: Release 빌드 후 사용자가 실제 앱에서 확인 완료, Done으로 변경.

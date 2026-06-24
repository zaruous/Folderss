# TODO

## Open With 컨텍스트 메뉴

- [x] 선택한 파일/폴더 확장자 기반 Open with 컨텍스트 메뉴 기능 구현

## 마크다운 뷰어

- [x] Phase 00 — 뷰어 프레임워크 스켈레톤 (IFileViewer, ViewerConfigService, ViewerHost, FolderBrowser 이벤트 연동)
- [x] Phase 01 — TextViewer (highlight.js 기반, WebView2 공통 인프라)
- [x] Phase 02 — MarkdownViewer (WebView2 + marked.js + mermaid.js + KaTeX + TOC)
- [x] Phase 02-E — Export (HTML + PDF via PrintToPdfAsync)
- [x] Phase 03 — 편집 모드 + 분할 뷰 (Edit / Split / Preview 모드 전환)
- [x] Phase 04 — 설정 창 뷰어 관리 탭

## 파일 컴포넌트

- [x] 파일 컴포넌트에 선택된 폴더의 변경 사항이 있으면 파일 리스너 기능으로 파일 내용 최신화 시킬 수 있는 기능 필요.
- [x] 파일 컴포넌트에서 빈 로우를 선택해도 윈도우 컨텍스트 메뉴가 나와야 함. (상위 폴더 기준)

## 파일 내용 검색

- [x] `Ctrl+F`로 열리는 파일 내용 검색 패널을 `Ctrl+F`로 토글해 닫거나 숨길 수 있어야 함. (`Esc`로도 패널 숨김 처리)

# 마크다운 본문의 HTML `<img>` 태그 상대 경로 이미지 표시

- 상태: Done

## 요구사항

마크다운 본문에 HTML 태그로 넣은 이미지(`<img src="images/a.png" width="200">`, `<p align="center"><img ...></p>` 등)도
마크다운 문법 이미지(`![](images/a.png)`)처럼 현재 문서 기준 상대 경로로 표시돼야 한다.

## 원인 분석 또는 설계

- 상대 경로 이미지를 `https://folderss-doc-asset/resolve?p=...`로 바꾸는 처리는 marked의 `image` 렌더러에만 있어,
  마크다운 문법 이미지만 바뀌었다. 본문 HTML의 `<img>`는 원래 경로 그대로 남아 페이지 출처(`Resources` 폴더) 기준으로 요청되어 깨졌다.
- 본문 HTML은 이미 DOMPurify로 정화하므로, DOMPurify `afterSanitizeAttributes` 훅에서 `<img src>`를 같은 `resolveImageSrc` 규칙으로 바꾼다.
  정화(위험 URI 제거)가 끝난 뒤에 바꾸므로 `javascript:` 등은 이미 지워진 상태다.

## 구현 내용

- `markdown-app.html`: `DOMPurify.addHook('afterSanitizeAttributes', ...)` 추가. `IMG`의 `src`를 `resolveImageSrc`로 변환.
  `https:`·`data:`·`file:` 등 스킴이 있는 주소와 이미 변환된 마크다운 이미지 주소는 그대로 둔다.
- HTML 내보내기의 이미지 data URI 변환(`InlineDocAssets`)은 같은 주소 형식을 찾으므로 HTML 태그 이미지에도 그대로 적용된다.

## 변경 파일

- `Folderss/Viewers/Resources/markdown-app.html`
- `docs/items/markdown-viewer-raw-html-img.md`

## 검증

- `dotnet build .\Folderss.sln -c Debug` — 오류 0개.
- 헤드리스 Edge에서 실제 `markdown-app.html`로 확인:
  - `<img src="images/raw.png" width="100">`, `<img src="../up/a b.png">`, `<img src="C:\pics\w.png">`, `<p align="center"><img src="logo.png"></p>`
    → `https://folderss-doc-asset/resolve?p=...`로 변환(다른 속성 유지).
  - `https://`, `data:` 이미지는 그대로. `javascript:` src는 제거, `onerror`는 제거되고 스크립트 실행 없음.
  - 마크다운 문법 이미지는 이전과 같음.
- 앱에서 확인 필요: 확인용 문서 `변경-확인.md`의 "HTML 태그로 넣은 이미지" 표시, HTML 내보내기 후 브라우저에서 표시.

## 변경 이력

- 2026-10-15: 수정 구현, 빌드·헤드리스 테스트 통과. 앱 수동 확인 대기.
- 2026-10-15: 사용자 앱 확인 결과 이슈 없음 → Done.

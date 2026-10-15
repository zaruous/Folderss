# 마크다운 HTML 내보내기의 로컬 이미지 깨짐 수정

- 상태: Done

## 요구사항

HTML로 내보낸 파일을 앱 밖(브라우저)에서 열어도 문서의 로컬 이미지가 보여야 한다.

## 원인 분석 또는 설계

- 미리보기는 로컬 이미지를 `https://folderss-doc-asset/resolve?p=...` 주소로 바꿔 `MarkdownViewer`가 응답하는데,
  내보낸 HTML에도 이 주소가 그대로 남아 앱 밖에서는 깨졌다(`markdown-viewer-relative-image-links.md`의 알려진 제약).
- 내보낼 때 C# 쪽에서 해당 주소를 찾아 이미지 파일을 data URI로 넣는다. JS에서 `fetch`로 읽는 방식은 CSP `connect-src`와 교차 출처 응답 헤더를 바꿔야 해서 택하지 않았다.

## 구현 내용

- `MarkdownViewer.ExportHtmlContent`가 저장 전에 `InlineDocAssets`를 거친다.
  `DocAssetUrlRegex`로 주소를 찾아 미리보기와 같은 `ResolveLocalLinkPath`로 경로를 풀고, `data:<형식>;base64,...`로 바꾼다.
  파일이 없거나 읽기 실패 시 원래 주소를 둔다.
- 오른쪽 메뉴 "다른 이름으로 저장"도 같은 경로(`exportHtml` → `export-html` 메시지)를 타므로 함께 적용된다.

## 변경 파일

- `Folderss/Viewers/MarkdownViewer.xaml.cs`
- `docs/architecture.md`
- `docs/items/markdown-export-html-inline-images.md`

## 검증

- `dotnet build .\Folderss.sln -c Debug` — 오류 0개.
- 앱에서 확인 필요: `![](img/a.png)`, `![](../assets/b.png)`가 있는 문서를 HTML로 내보낸 뒤 브라우저에서 열어 이미지 표시.
- 알려진 제약: 이미지가 크면 HTML 파일도 그만큼 커진다. KaTeX 글꼴은 `markdown-viewer-katex-fonts.md`로 미리보기·PDF에는 들어갔지만, 내보낸 HTML 파일에는 포함되지 않는다.

## 변경 이력

- 2026-10-15: 수정 구현, 빌드 확인. 앱 수동 확인 대기.
- 2026-10-15: 사용자 앱 확인 결과 이슈 없음 → Done.

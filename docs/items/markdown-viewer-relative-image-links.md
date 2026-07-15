# 마크다운 뷰어 상대 경로 이미지 표시 안 됨

- 상태: Ready for Verification (빌드는 사용자 환경에서 확인 필요)

## 요구사항

마크다운 패널에서 상대 경로(또는 로컬 절대 경로) 이미지 링크(`![](img/foo.png)`, `![](../assets/foo.png)` 등)를
사용하면 미리보기에 이미지가 표시되지 않는 문제를 수정한다.

## 원인 분석 또는 설계

`MarkdownViewer`의 미리보기 페이지는 `https://folderss-viewer/markdown-app.html`로 로드되며, 이 가상 호스트는
`SetVirtualHostNameToFolderMapping`으로 앱의 `Viewers/Resources` 폴더에 매핑되어 있다. 마크다운 본문에 상대
경로 이미지가 있으면 `marked.parse()`가 만든 `<img src="상대경로">`가 브라우저에 의해 페이지 오리진(즉
`Resources` 폴더) 기준으로 해석되어, 실제로 열려 있는 `.md` 파일의 폴더가 아니라 엉뚱한 위치를 요청하므로
항상 깨진다. 링크 클릭(`<a>`)은 이미 `HandleLinkClick`/`ResolveLocalLinkPath`로 현재 파일 기준 경로 해석을
하고 있었지만, `<img>` 렌더링에는 동일한 처리가 없었다.

`SetVirtualHostNameToFolderMapping`으로 파일이 열릴 때마다 그 폴더를 동적으로 매핑하는 방식도 검토했으나,
이 방식은 매핑된 폴더보다 상위 폴더로는 접근할 수 없어 `../assets/foo.png`처럼 상위 폴더를 참조하는 흔한
패턴이 깨진다. 대신 원본 경로 문자열을 쿼리 파라미터로 그대로 전달해 브라우저의 URL 정규화를 우회하고,
C# 쪽에서 기존 `ResolveLocalLinkPath` 로직으로 해석하는 방식을 선택했다.

## 구현 내용

- `markdown-app.html`에 `marked.use({ renderer: { image ... } })`를 추가해, 스킴이 없는 이미지 href를
  `https://folderss-doc-asset/resolve?p=<encodeURIComponent(href)>`로 치환한다.
  - `http(s):`, `data:`, `file:`, `ftp:`, `mailto:` 스킴이 이미 있는 링크는 그대로 둔다.
  - Windows 절대 경로(`C:\...`)는 콜론이 있어도 스킴으로 오인하지 않도록 알려진 스킴만 화이트리스트로 검사한다.
- CSP `img-src`에 `https://folderss-doc-asset`, `data:`, `http:`, `https:`를 추가해 로컬 리졸브 이미지·데이터
  URI·원격 이미지 링크를 모두 허용한다.
- `MarkdownViewer.OnWebResourceRequested`에서 `folderss-doc-asset` 호스트 요청을 가로채 `BuildDocAssetResponse`로
  응답한다: 쿼리의 원본 경로 문자열을 한 번 디코드해 `ResolveLocalLinkPath`(링크 클릭과 동일 로직)에 넘기고,
  해석된 절대 경로의 파일을 읽어 확장자 기반 `Content-Type`과 함께 반환한다. 실패 시 404를 반환한다.

## 변경 파일

- `Folderss/Viewers/Resources/markdown-app.html`
- `Folderss/Viewers/MarkdownViewer.xaml.cs`
- `docs/architecture.md`
- `docs/items/markdown-viewer-relative-image-links.md`

## 검증

- 이 세션은 Linux 원격 실행 환경이라 .NET SDK가 없어 `dotnet build`를 직접 실행하지 못했다.
- Windows 개발 환경에서 `dotnet build .\Folderss.sln -c Debug` 실행 및 아래 시나리오 수동 확인 필요.
  - 같은 폴더의 이미지: `![](image.png)`
  - 하위 폴더 이미지: `![](img/foo.png)`
  - 상위 폴더 이미지: `![](../assets/foo.png)`
  - 원격 이미지: `![](https://example.com/foo.png)`
  - 존재하지 않는 이미지 경로 → 깨진 이미지 아이콘만 표시되고 앱이 죽지 않는지
- 알려진 제약(별도 조치 없음): `HTML로 내보내기`로 저장한 독립 HTML 파일은 앱 밖에서 열리므로 `folderss-doc-asset`
  스킴이 동작하지 않아 상대 경로 이미지가 다시 깨진다. 미리보기와 인쇄/PDF 내보내기는 같은 WebView2 세션에서
  렌더링되므로 영향이 없다.

## 변경 이력

- 2026-07-15: 원인 분석 및 수정 구현. 빌드 검증은 Windows 환경에서 별도 확인 필요.

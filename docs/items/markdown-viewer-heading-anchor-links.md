# 마크다운 미리보기 문서 안 제목 링크(#앵커) 동작

- 상태: Done

## 요구사항

`[설치](#설치-방법)`처럼 GitHub 방식 제목 앵커로 쓴 문서 안 링크를 누르면 해당 제목으로 이동해야 한다.

## 원인 분석 또는 설계

- 목차를 만들며 제목 id를 `h-0`, `h-1`… 로 붙여 GitHub 규칙의 앵커와 맞지 않았다.
- marked가 한글 앵커를 퍼센트 인코딩(`#%EC%84%A4...`)하는데 클릭을 브라우저 기본 동작에 맡기고 있었다.
- GitHub 규칙: 소문자화, 글자·숫자·공백·`-`·`_` 외 문자 제거, 공백 하나당 `-`, 중복 제목은 `-1`, `-2`.

## 구현 내용

- `markdown-app.html`
  - `slugify`, `uniqueSlug` 추가. `buildToc`가 제목 id를 슬러그로 붙인다. 본문 HTML에 직접 준 id는 유지.
  - 앱 화면 요소 id(`toolbar`, `main` 등)와 겹치면 `-1`을 붙여 앱 스타일·동작과 충돌하지 않게 했다.
  - 미리보기의 `#` 링크 클릭은 퍼센트 인코딩을 풀어 해당 요소로 미리보기만 스크롤하고 목차 활성 항목을 맞춘다.
  - `scrollPreviewToHeading`은 `offsetTop` 대신 화면 좌표 차이로 위치를 계산해, 목록 안 `<a id>` 같은 중첩 요소에도 정확히 이동한다.
- 내보낸 HTML의 목차 링크도 같은 id를 쓰므로 그대로 동작한다.

## 변경 파일

- `Folderss/Viewers/Resources/markdown-app.html`
- `docs/items/markdown-viewer-heading-anchor-links.md`

## 검증

- 헤드리스 Edge: `# 설치 방법`→`설치-방법`, `## FAQ` 두 개→`faq`, `faq-1`, `## C++ & C# (2024)`→`c--c-2024`(GitHub와 같음),
  `## Toolbar`→`toolbar-1`(앱 요소와 충돌 회피, `#toolbar` 요소는 1개 유지). `[go](#설치-방법)` 클릭 시 외부 링크 메시지 없이 목차 활성 항목이 "설치 방법"으로 바뀜.
- 이후 해결: 앱 요소 id 충돌은 `markdown-viewer-app-id-prefix.md`(앱 id에 `mdv-` 접두사)로, 다른 문서 앵커는 `markdown-viewer-cross-file-anchor.md`로 해결.

## 변경 이력

- 2026-10-15: 수정 구현, 헤드리스 테스트 통과. 앱 수동 확인 대기.
- 2026-10-15: 알려진 제약 두 가지를 후속 항목으로 해결(위 검증 참고).
- 2026-10-15: 사용자 앱 확인 결과 이슈 없음 → Done.

# 마크다운 수식 KaTeX 글꼴 누락 수정

- 상태: Done

## 요구사항

수식이 KaTeX 전용 글꼴로 표시돼야 한다(분수·적분·그리스 문자 등의 모양과 간격이 맞아야 함).

## 원인 분석 또는 설계

- `katex.min.css`가 `fonts/KaTeX_*.woff2` 등을 참조하는데 `Viewers/Resources/fonts` 폴더가 없어, 수식이 시스템 글꼴로 대체 표시됐다.
- 번들된 KaTeX 버전(`0.16.9`)과 같은 버전의 글꼴을 넣는다. CSS가 `woff2`를 먼저 지정하고 WebView2(Chromium)는 `woff2`를 지원하므로 `woff2` 20개만 넣는다.

## 구현 내용

- `Viewers/Resources/fonts/`에 KaTeX 0.16.9 `woff2` 20개 추가(약 300KB). jsdelivr와 cdnjs 두 곳에서 받은 파일의 SHA-256이 모두 일치함을 확인.
- `Viewers\Resources\**\*.*` Content 와일드카드로 출력 폴더에 복사된다. CSP `font-src 'self'`로 허용되어 있다.

## 변경 파일

- `Folderss/Viewers/Resources/fonts/KaTeX_*.woff2` (신규 20개)
- `docs/items/markdown-viewer-katex-fonts.md`

## 검증

- CSP를 적용한 헤드리스 Edge(로컬 HTTP 서버)에서 수식 렌더링 후 `document.fonts`에 `KaTeX_Main`, `KaTeX_Math`가 `loaded` 상태. CSP 위반 없음.
- 빌드 출력 `Viewers/Resources/fonts`에 20개 복사 확인.
- 앱에서 확인 필요: `변경-확인.md`의 수식이 이전보다 교과서 같은 모양(세리프·이탤릭)으로 보이는지.
- 알려진 제약: HTML 내보내기 파일에는 글꼴이 포함되지 않아 앱 밖에서는 시스템 글꼴로 표시된다(미리보기·PDF는 정상).

## 변경 이력

- 2026-10-15: 구현, 헤드리스 확인. 앱 수동 확인 대기.
- 2026-10-15: 사용자 앱 확인 결과 이슈 없음 → Done.

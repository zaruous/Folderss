# 마크다운 패널 편집창이 좁게 고정되는 문제 수정

- 상태: Ready for Verification

## 요구사항

마크다운 패널에서 편집창(에디터 영역)이 화면 너비에 맞게 넓게 표시되지 않고
좁아진 상태로 유지되는 경우가 있다. 이를 수정한다.

## 원인 분석 또는 설계

`markdown-app.html`의 분할(Split) 모드 리사이즈 핸들(`split-handle`)은 드래그 시
`#editor-pane`에 `style.flex = 'none'`과 `style.width = '<고정 px>'`를 인라인으로 직접 설정한다.

이후 모드를 Edit/Preview로 전환하거나(`setMode`), 같은 뷰어 탭에서 다른 파일을 열 때(`app.open`)
이 인라인 `flex`/`width`가 초기화되지 않아 편집창이 분할 모드일 때 정했던 고정폭 그대로 남는다.
그 결과 Edit 단독 모드에서도 화면 전체 너비를 채우지 못하고 좁게 보인다.

## 구현 내용

- `resetEditorPaneSizeIfNotSplit(mode)` 헬퍼 추가 — split 모드가 아니면 `#editor-pane`의 인라인 `flex`/`width`를 제거해 CSS 기본값(`flex:1`)으로 복귀
- `setMode()`와 `app.open()` 양쪽에서 모드 결정 직후 이 헬퍼 호출

## 변경 파일

- `Folderss/Viewers/Resources/markdown-app.html`

## 검증

- [ ] Split 모드에서 편집창 폭을 드래그로 좁게 조정 → Edit 단독 모드로 전환 → 편집창이 화면 전체 너비로 표시됨
- [ ] Split 모드에서 폭 조정 후 같은 탭에서 다른 파일 열기(Markdown 링크 클릭 등) → 편집창이 정상 너비로 표시됨
- [ ] Split 모드 재진입 시 이전에 드래그한 폭이 아닌 기본 분할 비율로 표시됨(고정폭 초기화 확인)
- [ ] 빌드 성공 확인 (Windows/MSBuild 환경에서 확인 필요 — 이 세션은 Linux 환경이라 직접 빌드 불가)

## 변경 이력

- 2026-07-14: 초기 구현

# 블록 편집기 3단계 Phase 2: 여러 열 배치·페이지 링크 (#77)

- 상태: Done
- 사용자 확인: 확인 (2026-10-29)
- 상태 전환일: 2026-10-29

## 요구사항

이슈 #77(블록 편집기 3단계: 블록 종류·미디어) 중 Phase 1(`block-editor-stage3-media.md`)에서 뺀 나머지.

- 여러 열 배치(블록을 나란히) — Markdown으로 나타낼 수 없으므로 #78 원칙대로 사이드카(`EditorBlock.Attributes`)에 열 묶음·비율을 저장한다.
- 페이지 링크 블록 — 페이지 = `.md` 파일. `[📄 제목](경로.md)` 카드, 클릭하면 그 파일을 새 탭으로 연다. `/페이지`로 기존 문서를 고르거나 하위 페이지 파일을 새로 만든다.
  백링크·이동 경로는 #74(페이지·데이터베이스)로 남긴다.

#77 완료 기준: 블록마다 Markdown 왕복 단위 테스트와 하네스 기능 검사, 1만 줄 문서 스크롤 성능 유지.

## 원인 분석 또는 설계

결정(2026-10-29, 사용자):
- 여러 열: 사이드카 열 묶음. Markdown에는 블록을 1열 → 2열 순서대로 그대로 쓰고, 열 번호·너비 비율은 `문서.md.meta.json`에 둔다. 다른 편집기에서는 세로로 이어진 문서로 보인다.
- 페이지 링크: 페이지 = `.md` 파일.
- Mermaid 미리보기: 이번 범위에서 뺀다(#77의 선택 항목).

여러 열 설계:
- 속성 `column` = `묶음id/열번호`, 묶음 첫 블록의 `colratio`(`2:1` 등)가 너비 비율. Markdown은 블록 순서 그대로(1열 → 2열)라 다른 도구에서는 세로로 이어진다.
- 정리 규칙(`BlockOperations.ColumnLayout`, 구조 갱신 때마다): 같은 묶음 두 블록 사이에 낀 블록은 앞 블록의 열 / 떨어져 다시 나온 같은 묶음id는 뺌 /
  열 번호는 앞 블록보다 줄지 않게 하고 0부터 다시 매김(빈 열은 사라짐) / 한 열뿐이면 풂 / 비율은 묶음 첫 블록에만, 열 수와 맞을 때만.
- 가상화 패널: 묶음은 세로 흐름에서 한 덩어리. 묶음 안 블록의 `_tops`는 묶음 위쪽 y(이진 검색·스크롤 앵커용)로 두고, 실제 위치·폭은 열마다 쌓은 `_rowTop`·`_rowX`·`_rowW`.
  묶음 높이는 가장 긴 열. 기존 "블록 하나 = 세로 한 줄" 구조(행 재사용·높이 기억·앵커)를 그대로 쓴다.
- 새 블록은 만든 자리 블록의 열을 이어받는다(`InColumnOf`) — 묶음 마지막 블록 뒤에 만든 블록은 규칙만으로는 묶음 밖이 되므로 명시적으로 둔다. 문서 끝 클릭으로 만든 문단은 묶음 밖.
- 중첩 열(열 안 블록을 다시 열로)은 막는다(Notion도 같음).

## 구현 내용

1. **모델·Markdown (`EditorBlock`, `BlockMarkdown`, `BlockSidecar`)**: `BlockKind.Page`, `AttrColumn`·`AttrColumnRatio`, `IsLinkCard`(파일·북마크·페이지) — 흩어져 있던 `File || Bookmark` 검사 20곳을 이 헬퍼로 모았다.
   링크 카드 줄은 정규식 하나(`LinkCardLine`, `📎`·`🔖`·`📄`)로 읽고 쓴다. 사이드카 해시에 페이지 경로를 넣었다.
2. **열 연산 (`BlockOperations`)**: `ColumnLayout`(정리·위치별 묶음 정보), `MakeColumns`(최상위 블록을 열에 고르게, 모자라면 빈 문단), `MoveToColumn`(옆 열 끝으로, 오른쪽 끝이면 새 열, 최대 5열),
   `UngroupColumns`, `MoveRange`(`Ctrl+Shift+↑↓`)는 넘어간 블록의 열로.
3. **가상화 패널 (`BlockEditor.Panel.cs`)**: 위 설계대로 `ComputeTops`·`ColumnGeometry`(손잡이 자리를 뺀 본문 폭을 비율로), 높이 기억은 행 폭별, 화면 범위 거르기는 실제 위치로.
   끌어 놓을 자리 `DropTarget`(x로 열을 고르고 그 열 안 자리·표시선), 열 사이 빈 곳 클릭은 그 열 블록으로.
4. **편집기 (`BlockEditor.cs`, `.Table.cs`, `.Media.cs`)**: 블록 생성 19곳에 `InColumnOf`, 끌어 옮기기·파일 끌어 놓기는 놓은 열로(파일 끌어 놓기는 표시선 자리 = 앞 블록 뒤로 바로잡음),
   `/2열`·`/3열`·`/페이지`, 블록 메뉴 `열`(2열·3열로 나란히 / 왼쪽·오른쪽 열로 옮기기 / 열 너비 프리셋 / 열 풀기).
5. **페이지 링크 (`BlockEditor.Media.cs`, `.Row.cs`, `Viewers/BlockEditorViewer.cs`)**: 카드(📄 제목·경로·`파일 없음`, `열기`/`만들기`), `OpenPage`(없으면 만들지 묻기) → `PageOpenRequested` →
   뷰어 `IFileOpenRequester.FileOpenRequested`로 새 탭. `BlockPageDialog`(GitDialogBase): 새 하위 페이지(`문서이름/제목.md`, 못 쓰는 글자는 `_`, 같은 이름 있으면 막음) 또는 기존 문서 연결.

## 변경 파일

- `Folderss/Models/EditorBlock.cs`
- `Folderss/Services/BlockMarkdown.cs`, `BlockOperations.cs`, `BlockSidecar.cs`
- `Folderss/Controls/BlockEditor.cs`, `BlockEditor.Panel.cs`, `BlockEditor.Row.cs`, `BlockEditor.Table.cs`, `BlockEditor.Media.cs`
- `Folderss/Viewers/BlockEditorViewer.cs`
- `tests/Folderss.SearchTests/BlockColumnTests.cs` (신규)
- `README.md`, `docs/architecture.md`, `docs/items/block-editor-stage3-phase2.md`

## 검증

1. **빌드**: `dotnet build` 오류 0(기존 경고만).
2. **단위 테스트**: 전체 608 통과(건너뜀 4). `BlockColumnTests` 14개 — 열 정리 규칙(묶음·번호 다시 매김·사이에 낀 블록·떨어진 같은 id·한 열·줄어드는 열 번호·비율),
   `MakeColumns`(고르게 나눔·빈 열 채움·자식 같이), `MoveToColumn`(옆 열 끝·새 열·빈 열 사라짐), `MoveRange` 열 따라가기, 열 풀기, 사이드카 왕복(Markdown은 순서 그대로),
   페이지 링크 Markdown 왕복·사이드카 해시.
3. **WPF 하네스(실제 창)**: Phase 2 43개 통과 — `/2열` 나란히 배치(같은 y·같은 폭), 열 안 Enter·마지막 열 끝 Enter가 그 열에, Markdown 순서, 묶음 아래 블록은 긴 열 아래·전체 폭,
   2:1 비율 폭, 오른쪽 열로 옮기기, `Ctrl+Shift+↑`로 열 경계 넘기, Undo, 끌어 놓기로 다른 열 맨 위·묶음 밖, 열 풀기·Undo, 블록 4개 → 3열, 저장·다시 열기(비율 포함, 수정 아님),
   페이지 카드(파일 없음 → 만들기 → 열기), 제목 클릭 → 뷰어 `FileOpenRequested`, `/페이지` 대화상자 규칙(제목 필수·폴더/제목.md·못 쓰는 글자·같은 이름·기존 문서·없는 파일).
   기존 회귀: 3단계 70, 기능 74, 사이드카 16, 뷰어 5, 대용량 4, 목차 대용량 2 모두 통과.
4. **대용량 성능(Debug, 11,424줄·블록 9,862개 + 열 묶음 300개)**: 같은 시점에 열 없는 문서와 번갈아 측정 — 열기 292~344ms(열 없음 243~304ms), 열 안 글자 입력 중앙 10ms(같음),
   열 안 Enter 27~30ms(열 없는 문단 Enter 26~37ms), 120px 스크롤 20ms(17ms), 한 화면 이동 54~58ms(45~50ms). 열 정리 0.6ms, 위치 계산 1.5ms.
   (이 날은 PC 부하로 열 없는 문서도 2단계 측정보다 느리게 나왔다.)

## 변경 이력

- 2026-10-29: #77 Phase 1 검증 중 남은 항목을 별도 항목으로 분리 (`Todo`)
- 2026-10-29: 착수. 여러 열은 사이드카 열 묶음, 페이지 = .md 파일, Mermaid 제외로 결정 (`In Progress`)
- 2026-10-29: 여러 열·페이지 링크 구현, 단위 테스트 608·하네스 Phase 2 43개와 기존 회귀 통과, 열 묶음 300개 1만 줄 성능 측정 (`Ready for Verification`)
- 2026-10-29: 사용자 확인 완료 (`Done`)

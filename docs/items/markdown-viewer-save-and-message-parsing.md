# 마크다운 뷰어 저장 안전성 및 WebView 메시지 파싱 수정

- 상태: Done

## 요구사항

1. 저장 도중 실패해도 원본 파일이 사라지지 않아야 한다.
2. 저장되는 본문이 WebView 메시지 전달 과정에서 깨지지 않아야 한다.

## 원인 분석 또는 설계

- `MarkdownViewer.Save`가 임시 파일에 쓴 뒤 `File.Delete(원본)` → `File.Move(임시, 원본)` 순서로 교체했다.
  삭제와 이동 사이에 이동이 실패하면(백신·인덱서가 잠금, 권한 등) 원본이 없어지고 `.tmp`만 남는다.
- `OnWebMessageReceived`가 직접 만든 `ExtractJsonString`으로 JSON을 훑었는데 `\uXXXX`, `\b`, `\f`, `\/` 이스케이프를 처리하지 않아,
  본문에 제어문자 등이 있으면 `u0008` 같은 글자로 바뀐 채 저장될 수 있었다.
- 저장은 설정 파일이 이미 쓰는 `SettingsFile.WriteAllText`(임시 파일 → `File.Move(overwrite: true)`, 실패 시 임시 파일 삭제)를 재사용한다.
  `File.Replace`는 같은 헬퍼 주석에 적힌 이유(잠금·비NTFS에서 더 자주 실패)로 쓰지 않는다.
- 메시지는 `System.Text.Json.JsonDocument`로 파싱한다.

## 구현 내용

- `Save`: `SettingsFile.WriteAllText(_filePath, content, _encoding)`로 교체. 인코딩(BOM 포함 여부)은 기존과 같이 유지.
- `OnWebMessageReceived`: `JsonDocument.Parse(e.WebMessageAsJson)` 후 `GetJsonString` 헬퍼로 `type`, `content`, `uri`, `href`를 읽는다.
  `modified`의 `value`도 문자열 포함 검사 대신 `JsonValueKind.True`로 판정한다. `ExtractJsonString` 삭제.

## 변경 파일

- `Folderss/Viewers/MarkdownViewer.xaml.cs`
- `docs/items/markdown-viewer-save-and-message-parsing.md`

## 검증

- `dotnet build .\Folderss.sln -c Debug` — 오류 0개.
- 앱에서 확인 필요: 편집 후 Ctrl+S 저장 → 파일 내용·인코딩(BOM 있는 UTF-8, 없는 UTF-8, UTF-16) 유지, `.tmp` 파일이 남지 않는지.
- 참고: `MonacoViewer`의 같은 문제는 `monaco-viewer-save-message-dispose.md`에서 고쳤다.

## 변경 이력

- 2026-10-15: 수정 구현, 빌드 확인. 앱 수동 확인 대기.
- 2026-10-15: 사용자 앱 확인 결과 이슈 없음 → Done.

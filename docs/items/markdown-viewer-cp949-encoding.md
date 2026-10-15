# 마크다운 뷰어 BOM 없는 CP949 문서 인식

- 상태: Done

## 요구사항

BOM 없이 CP949(EUC-KR)로 저장된 오래된 한국어 `.md` 문서를 열어도 글자가 깨지지 않아야 하고, 저장해도 원래 인코딩이 유지돼야 한다.

## 원인 분석 또는 설계

- `MarkdownViewer.DetectEncoding`이 BOM만 보고, BOM이 없으면 무조건 UTF-8로 읽었다.
- BOM이 없으면 파일 전체를 엄격한 UTF-8(잘못된 바이트에서 예외)로 검사해, 통과하면 UTF-8, 실패하면 시스템 ANSI 코드 페이지(한국어 Windows는 CP949)로 읽는다.
  코드 페이지 공급자 등록과 "시스템이 이미 UTF-8이면 대체 없음" 처리는 Git diff가 쓰는 `GitTextDecoder.Resolve(GitFallbackEncoding.SystemAnsi)`를 재사용한다.
- 저장(`SettingsFile.WriteAllText(_filePath, content, _encoding)`)은 읽은 인코딩을 그대로 쓰므로 CP949 문서는 CP949로 저장된다.

## 구현 내용

- `DetectEncoding`: 파일을 공유 모드(`ReadWrite | Delete`)로 모두 읽어 BOM 확인 → 엄격 UTF-8 검사(`GetCharCount`) → 실패 시 시스템 ANSI.

## 변경 파일

- `Folderss/Viewers/MarkdownViewer.xaml.cs`
- `docs/items/markdown-viewer-cp949-encoding.md`

## 검증

- `dotnet build .\Folderss.sln -c Debug` — 오류 0개.
- 앱에서 확인 필요: 메모장에서 "ANSI"로 저장한 한글 `.md` 열기 → 한글 정상 표시, 편집 후 저장 → 다시 열어도 정상이고 다른 편집기에서 CP949로 열리는지.
  UTF-8(BOM 없음/있음) 문서는 이전과 같이 열리는지.
- 알려진 제약: CP949 문서에 CP949로 표현할 수 없는 문자(이모지 등)를 넣고 저장하면 `?`로 바뀐다.
  일부 바이트가 깨진 UTF-8 문서는 CP949로 읽힌다.

## 변경 이력

- 2026-10-15: 수정 구현, 빌드 확인. 앱 수동 확인 대기.
- 2026-10-15: 사용자 앱 확인 결과 이슈 없음 → Done.

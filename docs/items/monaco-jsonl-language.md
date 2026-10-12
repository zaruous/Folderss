# Monaco 뷰어: .jsonl 전용 언어와 줄 단위 포맷

- 상태: Ready for Verification

## 요구사항

설정에서 `.jsonl`을 Monaco 뷰어에 등록했는데 커맨드 팔레트에 `Format Document`가 나오지 않는다. JSONL(줄마다 JSON 하나)을 Monaco에서 강조하고 포맷할 수 있게 한다.

## 원인 분석 또는 설계

- 뷰어 매핑(`ViewerConfigService`)은 어느 뷰어로 열지만 정한다. Monaco 안의 언어는 `MonacoViewer.ExtToLanguage`가 따로 정하는데 `.jsonl`이 없어 `plaintext`로 열렸고, plaintext에는 포매터가 없어 명령이 나오지 않았다.
- `.jsonl`을 그냥 `json`으로 매핑하면 Monaco의 json 언어가 파일 전체를 JSON 하나로 검사해 둘째 줄부터 오류가 찍히고, 포매터가 레코드를 여러 줄로 펼쳐 저장하면 JSONL이 깨진다.
- 그래서 `jsonl` 언어를 따로 등록한다(`Viewers/Resources/monaco-jsonl.js`).
  - 구문 강조: 작은 Monarch 토크나이저(키·값 문자열은 `string.key.json`·`string.value.json`이라 기본 테마 색을 그대로 받음).
  - `Format Document`: 레코드마다 문자열 밖 공백만 지워 한 줄로(JSONL 본래 모양).
  - 커맨드 팔레트·오른쪽 메뉴 `JSONL: 레코드 펼쳐 보기`: 레코드마다 들여쓰기. 저장하면 JSONL이 아니므로 이름에 적었다. 펼쳐진 레코드는 줄을 모아 다시 파싱되는 지점까지 한 레코드로 보므로 `Format Document`로 되돌릴 수 있다. `JSONL: 레코드 한 줄로 압축`도 같은 일을 하는 명령으로 둬 팔레트에서 찾기 쉽게 했다.
  - 값은 `JSON.parse`로 유효한지만 보고 글자는 원문을 옮긴다. `JSON.parse`→`stringify`로 다시 쓰면 큰 정수 자릿수(예: 19자리 ID)와 정수꼴 키 순서가 바뀌므로 쓰지 않았다. 파싱이 안 되는 줄은 손대지 않는다.
- `.jsonl`을 기본 뷰어 매핑(`DefaultMappings`)에도 넣어 다른 사용자는 등록 없이 Monaco로 열리게 했다. 이미 다른 뷰어로 재정의한 사용자는 재정의가 유지된다(v2 형식은 기본값과 다른 것만 저장).

## 구현 내용

- `Folderss/Viewers/Resources/monaco-jsonl.js` (신규) — `splitRecords`·`compactJsonl`·`expandJsonl`(순수 함수, node에서도 읽힘), `registerJsonl(monaco)`, `addJsonlActions(editor)`.
- `Folderss/Viewers/Resources/monaco-app.html` — 스크립트 로드, 모델 생성 전 `registerJsonl`, 편집기 생성 후 `addJsonlActions`.
- `Folderss/Viewers/MonacoViewer.xaml.cs` — `ExtToLanguage`에 `.jsonl → jsonl`.
- `Folderss/Services/ViewerConfigService.cs` — `DefaultMappings`에 `.jsonl → builtin:monaco`.
- `README.md` — 내장 뷰어 표.

## 변경 파일

- `Folderss/Viewers/Resources/monaco-jsonl.js` (신규)
- `Folderss/Viewers/Resources/monaco-app.html`
- `Folderss/Viewers/MonacoViewer.xaml.cs`
- `Folderss/Services/ViewerConfigService.cs`
- `README.md`
- `docs/items/monaco-jsonl-language.md` (신규)

## 검증

- [x] node로 `monaco-jsonl.js`의 순수 함수 검사: 압축↔펼침 왕복, 큰 정수·키 순서 보존, 잘못된 줄·빈 줄·CRLF·문자열 안 괄호와 이스케이프 따옴표·빈 `{}`/`[]`.
- [x] `dotnet build Folderss.sln -c Debug -p:EnableWindowsTargeting=true`(리눅스) — 새 오류 없음(기존 리눅스 전용 `Microsoft.Terminal` 오류 1건만 동일).
- [x] `dotnet test tests/Folderss.SearchTests` 통과.
- [ ] Windows에서 `.jsonl` 파일을 열어 강조·`F1 > Format Document`·`JSONL: 레코드 펼쳐 보기` 동작 확인 (사용자)

## 변경 이력

- 2026-10-12 요청 접수, 원인 분석 후 전용 언어 방식(1번)으로 구현.

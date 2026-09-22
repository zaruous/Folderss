# 설정 저장 안정성 개선 (뷰어 매핑 손실 버그, 실패 보고, 단일 쓰기)

- 상태: Ready for Verification

## 요구사항

설정 창의 저장이 다른 PC에서 "잘 안 되는" 경우가 있다. 원인 조사 결과 아래 세 가지를 수정한다.

1. 뷰어 매핑이 저장은 되는데 다음 실행 때 사라지는 로직 버그
2. 저장 실패가 사용자에게 전혀 드러나지 않는 구조 (빈 `catch { }` 또는 처리되지 않은 예외)
3. 뷰어 매핑 저장 시 파일을 수십 번 덮어쓰는 구조

## 원인 분석 또는 설계

### 1. 뷰어 매핑 손실

- `ViewerConfigService.Load()`가 저장된 값이 `LegacyDefaultMappings`(`.txt → Text`, `.cs`/`.py`/`.ps1` 등 19개 → Monaco)와 같으면 건너뛰었다.
- 이 규칙은 5d94461(Monaco 도입)에서 저장 방식을 "전체 매핑 덤프"에서 "재정의만 저장"으로 바꿀 때, 과거 파일에 남은 기본 매핑 잔여물을 정리하려고 넣은 것이다. 그런데 과거 파일의 실제 내용(`.md/.markdown → markdown`, `.txt/.cs/.json/.xml/.log → text`)과 legacy 표(`.cs`/`.sql` 등 → monaco)는 대부분 일치하지 않아, 정리 효과는 `.txt/.log → text` 정도였고 대신 사용자의 명시적 선택을 버리는 부작용이 컸다.
- 조회 경로 `GetEffectiveMappingKey`는 `_overrides`와 `DefaultMappings`만 보므로, 사용자가 설정 창에서 `.txt → Text`나 `.sql → Monaco`를 직접 고르면 파일에는 기록되지만 로드 시 버려져 매핑이 사라진다. PC와 무관하게 재현되며, 새 PC에서 설정을 다시 잡을 때 특히 눈에 띈다. 실제로 이 PC의 `viewer-config.json`에도 `.sql`, `.java → monaco`가 들어 있는데 매번 버려지고 있었다.
- 설계: 파일에 `"version":2`를 기록하고, `IsLegacyFullDump`가 참일 때만 legacy 정리 규칙을 적용한다. 판별 기준은 "버전 표기가 없고 현재 기본 매핑과 같은 항목(예: `.md → markdown`)이 있다"이다. 재정의만 저장하는 코드는 기본값과 같은 항목을 절대 쓰지 않으므로 그런 항목이 있으면 Monaco 도입 전 전체 덤프 파일이다. 버전 표기만 없는 재정의 파일(v2 도입 전에 저장된 것)은 모든 항목을 사용자의 선택으로 유지한다.

### 2. 저장 실패 보고

- 뷰어 매핑·열기 프로그램·콘솔·테마 저장은 모두 빈 `catch { }`로 끝나 실패해도 창이 정상 종료됐다. 단축키 저장(`KeyBindingService.Save`, `File.Replace`)만 예외가 밖으로 나가는데 `App`에 전역 처리기가 없어 앱이 종료됐고, 그 뒤 항목은 저장되지 않았다.
- 설계: 각 서비스의 저장은 예외를 던지게 하고, `SettingsWindow.Save_Click`이 `TrySave`로 항목별로 독립 시도한 뒤 실패 목록(항목·파일명·예외 메시지·저장 폴더)을 메시지 하나로 보여준다. 서비스 인스턴스는 메인 창과 공유되어 이번 실행 중에는 변경이 적용되므로 창은 닫는다.
- 테마는 라디오 클릭 시 즉시 저장되는 구조라, 저장 버튼에서 `ThemeManager.SaveCurrentTheme()`로 한 번 더 써서 실패를 함께 보고한다. 메뉴 경로의 `SaveTheme`는 기존처럼 조용히 넘어간다.
- 설정 창 밖의 부수 저장인 `ConsolePanel`의 마지막 프로필 기억은 호출처에서 try/catch로 감싸 터미널 시작 흐름에 영향을 주지 않게 했다.

### 3. 단일 쓰기

- 기존 `Save_Click`은 `GetAllMappings()`로 얻은 모든 키에 `RemoveMapping`, 편집 행마다 `SetMapping`을 호출했고 각 호출이 `Save()`로 파일을 통째로 다시 썼다.
- 설계: `ViewerConfigService.ReplaceMappings(IEnumerable<KeyValuePair<string,string>>)`가 `_overrides`를 비우고 전체를 반영한 뒤 파일을 한 번만 쓴다. `SetMapping`/`RemoveMapping`/`GetAllMappings`는 다른 호출처가 없어 제거했다.

## 구현 내용

- `Services/ViewerConfigService.cs` — `ConfigVersion = 2` 상수, `ReplaceMappings`/`ApplyMapping` 추가, `SetMapping`/`RemoveMapping`/`GetAllMappings` 제거, `Load()`에 `IsLegacyFullDump` 기반 legacy 규칙 적용, `Save()`가 `version`을 기록하고 예외를 던지도록 변경, `SimpleJson.ReadVersion` 추가, 테스트용 경로 주입 생성자 추가.
- `Services/SettingsFile.cs` (신규) — 설정 파일 원자적 쓰기 헬퍼. 임시 파일에 쓴 뒤 `File.Move(temp, target, true)`로 교체, 실패 시 임시 파일 정리 후 예외 전파. 모든 설정 저장(`KeyBindingService`, `ViewerConfigService`, `OpenWithService`, `ConsoleSettingsService`, `ThemeManager`)이 이를 사용.
- `Services/KeyBindingService.cs` — 직접 구현하던 임시 파일 + `File.Replace`를 `SettingsFile.Write`로 교체.
- `SettingsWindow.xaml.cs` — `Save_Click`을 `TrySave` 기반으로 재구성, 실패 시 `설정 저장 실패` 메시지, `SettingsDirectory` 헬퍼, `using System`/`System.IO` 추가.
- `Services/OpenWithService.cs` — `Persist()`의 빈 catch 제거, `SettingsFile.Write` 사용.
- `Services/ConsoleSettingsService.cs` — `Save()`의 빈 catch 제거, `SettingsFile.Write` 사용.
- `Services/ThemeManager.cs` — `SaveCurrentTheme()`(예외 전파)과 `WriteTheme()`(`SettingsFile.WriteAllText`) 추가, 기존 `SaveTheme()`는 이를 감싸 삼킴.
- `Controls/ConsolePanel.xaml.cs` — 터미널 시작 후 프로필 기억 저장을 try/catch로 감쌈.
- `tests/Folderss.SearchTests` — `ViewerConfigServiceTests.cs`(11건), WPF 뷰어 타입 스텁 `ViewerStubs.cs`, csproj에 `ViewerConfigService.cs`·`SettingsFile.cs` 링크.
- `CLAUDE.md` — 설정 항목 추가 체크리스트에 저장 메서드 규칙(`SettingsFile`, 예외 전파) 추가.
- `docs/architecture.md` — `SettingsWindow 저장 흐름 / ViewerConfigService` 절과 `SettingsFile` 항목 추가.
- `README.md` — 설정 저장 안정성 항목, `viewer-config.json` 설명, 아키텍처 요약 갱신.

## 코드 리뷰 (Cursor composer-2.5, coworks `scenario-review/cursor-review.js`)

리뷰 전문은 `%TEMP%\claude\D--git-cshap-Folderss\efd4e547-eb83-4571-94dc-ec66c3e6974e\scratchpad\cursor-review-log.md`에 있다. 총평: 원인 분석·수정 방향이 코드·테스트·git 이력과 일치하고, 핵심 회귀는 `IsLegacyFullDump` + v2 기록 + 테스트로 막혀 있음. catch 제거의 호출처 전수 확인 결과 누락 없음.

| # | 지적 | 판단 | 반영 |
|---|---|---|---|
| 1 | Monaco 이전 전체 덤프인데 `.md` 행이 빠진 파일은 legacy 잔여물이 남을 수 있음 | 잠재 결함(앱 정상 경로에서는 발생하지 않음) | 알려진 제약으로 문서화, 테스트 `..._KnownLimitation`로 결정 기록 |
| 2 | `SimpleJson` 최소 파서(첫 `}`, 쉼표 split, 이스케이프 미지원) | 기존 코드, 앱이 쓰는 형식에서는 문제 없음 | `ReadVersion`이 `"version":"2"`(문자열)도 읽도록 보강, 테스트 추가. 파서 교체는 보류 |
| 3 | `File.WriteAllText`/`doc.Save` 직접 쓰기 — 비원자적 | 저장 안정성 목표와 상충 | `SettingsFile` 헬퍼 도입, 5개 저장 경로 통일 |
| 4 | `ReadVersion` 문자열 버전·키 충돌 | 제안 | 문자열 버전 처리 반영 |
| 5 | `TrySave`가 `ex.Message`만 표시 | 제안 | 보류 — 경로는 메시지에 별도 표기됨 |
| 6 | 메뉴 테마 전환 경로는 저장 실패를 여전히 무시 | 기존 동작, 설정 창 저장에서 보완됨 | 보류 |
| 검토 5 | `KeyBindingService`의 `File.Replace` → `File.Move(..., true)` 통일 | 권장 | `SettingsFile.Write`로 교체 |
| 검토 6 | 빠진 테스트: `system:default` 라운드트립, 문자열 버전, `.md` 없는 덤프 | 제안 | 3건 + 임시 파일 잔존 여부 1건 추가 |

## 변경 파일

- `Folderss/Services/ViewerConfigService.cs`
- `Folderss/Services/SettingsFile.cs` (신규)
- `Folderss/Services/KeyBindingService.cs`
- `Folderss/SettingsWindow.xaml.cs`
- `Folderss/Services/OpenWithService.cs`
- `Folderss/Services/ConsoleSettingsService.cs`
- `Folderss/Services/ThemeManager.cs`
- `Folderss/Controls/ConsolePanel.xaml.cs`
- `tests/Folderss.SearchTests/ViewerConfigServiceTests.cs`, `ViewerStubs.cs` (신규), `Folderss.SearchTests.csproj`
- `CLAUDE.md`, `docs/architecture.md`, `README.md`
- `docs/items/settings-save-reliability.md` (신규)

## 검증

- [x] `dotnet build .\Folderss.sln -c Debug` — Exit 0
- [x] `dotnet test tests\Folderss.SearchTests` — 전체 통과 (ViewerConfigService 11건 포함)
- [x] Cursor 코드 리뷰 1턴 — 결함 없음, 잠재 결함 3건 중 2건 반영·1건 문서화
- [ ] 설정 > 뷰어에서 `.txt → Text` 추가 후 저장, 앱 재시작 → 매핑이 남아 있고 `.txt` 더블클릭 시 Text 뷰어로 열리는지 확인
- [ ] `%LOCALAPPDATA%\Folderss\viewer-config.json`이 `{"version":2,"mappings":{...}}` 형식으로 기록되고 `.tmp`가 남지 않는지 확인
- [ ] 폴더를 읽기 전용으로 만들거나 파일을 다른 프로세스로 잠근 뒤 저장 → `설정 저장 실패` 메시지에 항목·파일명·예외 메시지가 나오고 앱이 죽지 않는지 확인
- [ ] 정상 환경에서 저장 → 메시지 없이 창이 닫히는지 확인

## 알려진 제약

- Monaco 도입 전 전체 덤프 파일에서 `.md`/`.markdown` 행이 빠져 있으면(당시 UI에서 지웠거나 손으로 고친 경우) 재정의 파일과 구분할 수 없어 `.txt/.log → Text` 잔여물이 남는다. 사용자 선택을 버리는 쪽보다 남기는 쪽을 택했고, 남은 항목은 설정 창에서 지울 수 있다.
- `SimpleJson`은 앱이 쓰는 한 줄 형식만 안정적으로 읽는다. 손으로 여러 줄로 고치거나 값에 `,`/`}`를 넣으면 일부 항목이 빠질 수 있다. `System.Text.Json`으로 교체하는 것은 별도 항목으로 남긴다.

## 변경 이력

- 2026-09-22: 세 가지 수정 및 문서 반영, 항목 생성 (Ready for Verification)
- 2026-09-22: Cursor 리뷰 반영 — `SettingsFile` 원자적 쓰기 헬퍼 도입·5개 저장 경로 통일, `ReadVersion` 문자열 버전 처리, 테스트 4건 추가, 알려진 제약 문서화

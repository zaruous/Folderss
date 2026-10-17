# 폴더 비교 — 수정 시각만 다른 같은 파일이 "다름"으로 표시됨

- 상태: Ready for Verification — 구현·단위 테스트 완료, Windows 화면 확인 대기

## 요구사항

- 폴더 비교의 기본 기준(크기·수정 시각)에서는 크기와 내용이 같은데 수정 시각만 다른 파일이 "다름"으로 표시된다.
  git checkout·압축 해제·시각을 보존하지 않는 복사 뒤에 흔히 생기는 상황이다. 이런 파일이 차이 목록을 채우면 실제 차이를 찾기 어렵다.
  사용자가 ▶◀로 같은 파일을 불필요하게 교체할 수도 있다. 교체할 때 옛 항목은 휴지통으로 가지만, 작업 이력과 되돌리기 대상만 늘어난다.
- 기본 기준의 빠르기는 유지하면서, 시각만 다른 같은 크기 파일은 내용으로 다시 확인해 판정할 수 있어야 한다.

## 원인 분석 또는 설계

### 원인

`DirectoryCompareService.Walker.CompareFiles`의 판정 순서가 아래와 같다.

1. 크기가 다르면 다름
2. `UseContentHash`가 켜져 있으면 SHA-256으로 판정한다(수정 시각은 보지 않음).
3. 꺼져 있으면 수정 시각 차이가 허용 오차(`TimestampTolerance`, 기본 2초)를 넘을 때 바로 **다름**이 된다. 내용은 확인하지 않는다.

3번 때문에 시각만 다른 같은 파일이 다름이 된다. 코드 오류라기보다 기준의 한계다. Beyond Compare의 기본 규칙(크기·시각)도 같은 결과를 낸다.
지금 이것을 피하는 방법은 "내용 해시" 기준밖에 없다. 그 기준은 크기가 같은 모든 파일을 끝까지 읽으므로 느리다.

### 설계

- 옵션 `VerifyContentWhenTimeDiffers`(bool)를 추가한다. 크기가 같고 시각 차이가 허용 오차를 넘을 때만 두 파일을 바이트 단위로 비교한다.
  - 같으면 **같음**으로 판정하고 사유를 "내용 같음(수정 시각만 다름)"으로 둔다. 툴팁에서 시각 차이가 있었음을 볼 수 있다.
  - 다르면 **다름**으로 판정하고 사유는 기존과 같은 "수정 시각 다름 (…가 최신)"으로 둔다.
  - 읽기에 실패하면 **비교 불가**로 판정하고 예외 메시지를 사유로 둔다(해시 기준과 같은 처리).
- 바이트 비교는 두 스트림을 블록(64KB)으로 읽어 첫 차이에서 멈춘다. 취소 토큰은 블록마다 검사한다.
- 시각이 허용 오차 안이면 지금처럼 읽지 않고 같음이다. 그래서 추가 비용은 "크기는 같고 시각만 다른 파일"에서만 생긴다.
- `UseContentHash`가 켜져 있으면 새 옵션은 의미가 없다(해시가 늘 내용을 본다). 기존 해시 경로는 바꾸지 않는다.
- 옵션 대화상자(`DirectoryCompareOptionsDialog`)의 판정 기준 라디오에 세 번째 항목을 추가한다.
  - 크기와 수정 시각 (권장)
  - **크기와 수정 시각, 시각만 다르면 내용 확인** (신규)
  - 내용 해시 (SHA-256)

  라디오 세 개는 두 bool로 대응시킨다. 저장은 `CompareSettingsService`(compare-settings.xml)의 속성 1개를 추가하고 `TrySave` 흐름을 그대로 따른다.
  판정 기준이 바뀌면 다시 비교하도록 `DirectoryCompareWindow`의 판정 옵션 비교식(`a.UseContentHash != b.UseContentHash || …`)에 새 옵션을 추가한다.
- 기본값: **끔**(가벼운 비교). 무거운 비교(시각만 다르면 내용 확인, 내용 해시)는 옵션으로 고른다.

### 가정·위험

- 시각만 다른 파일이 수만 개인 저장소(대형 checkout 직후)에서는 그 파일을 모두 끝까지 읽는다. 진행 표시는 지금 "읽은 폴더 수"뿐이라 파일 읽기 중에는 멈춘 것처럼 보일 수 있다. 중지 버튼은 블록 단위 취소로 동작한다.
- 방향 색(`DirectionColors`)은 상태가 다름일 때만 칠하므로, 같음으로 바뀐 파일에는 최신/오래됨 색이 붙지 않는다. 의도한 동작이다.
- 네트워크 드라이브에서는 바이트 비교가 양쪽 파일을 모두 전송받는다.

### 미결 사항

- 없음. 기본값은 사용자 결정으로 끔(가벼운 비교 기본, 무거운 비교는 옵션). 기존 설정 파일에는 속성이 없으므로 기존 사용자의 동작은 바뀌지 않는다.

## 구현 내용

- `DirectoryCompareOptions.VerifyContentWhenTimeDiffers`(기본 false) 추가.
- `DirectoryCompareService.Walker.CompareFiles`: 시각 차이가 허용 오차를 넘고 옵션이 켜져 있으면 `SameBytes`(64KB 블록, 첫 차이에서 멈춤, 블록마다 취소 확인)로 다시 판정한다. 같으면 같음 + 사유 "내용 같음(수정 시각만 다름)", 다르면 기존 다름 사유, 읽기 실패면 비교 불가 + 예외 메시지.
- `CompareSettingsService`: `verifyContentWhenTimeDiffers` 속성 읽기·쓰기.
- `DirectoryCompareOptionsDialog`: 판정 기준 라디오 3개(크기·시각 / 크기·시각 + 시각만 다르면 내용 확인 / 내용 해시). 가운데 항목에서도 허용 오차 입력이 켜진다. `CriteriaChanged`에 새 옵션을 넣어 바꾸면 다시 비교한다. 상태 줄 기준 문구에 ", 시각만 다르면 내용 확인"을 붙인다.
- `README.md`·`docs/architecture.md` 설명 갱신.

## 변경 파일

- `Folderss/Models/DirectoryCompareModels.cs` — 옵션 속성
- `Folderss/Services/DirectoryCompareService.cs` — `CompareFiles` 분기, 바이트 비교
- `Folderss/Services/CompareSettingsService.cs` — 저장·읽기
- `Folderss/DirectoryCompareWindow.cs` — 옵션 대화상자 라디오, 다시 비교 조건, 기준 표시 문구
- `tests/Folderss.SearchTests/DirectoryCompareServiceTests.cs` — 아래 테스트
- `README.md` 폴더 비교 옵션 설명

## 검증

- 재현 테스트를 먼저 추가하고, 수정 전 실패를 확인한다. 내용이 같고 시각이 5시간 다른 쌍을 옵션을 켜고 판정하면 같음이어야 한다.
- 함께 확인할 테스트:
  - 옵션을 끄면 같은 쌍이 다름이다(기존 동작 유지).
  - 크기는 같고 마지막 바이트만 다르며 시각도 다른 쌍은 옵션을 켜도 다름이다.
  - 시각이 허용 오차 안이면 파일을 읽지 않는다. 확인 방법은 한쪽 파일을 배타적으로 열어 둔 상태에서 판정해도 비교 불가가 되지 않는 것이다(파일 공유 잠금이 강제되지 않는 Linux에서는 건너뜀).
  - 읽기 실패(배타 잠금)이면서 시각이 다르면 비교 불가다.
  - 옵션 저장 왕복이 맞고, 기본값이 기존 설정 파일과 호환된다.
- 전체 테스트 회귀가 없어야 한다. Linux에서는 `DirectoryCompareServiceTests`의 기존 Linux 전용 실패 2건을 제외하고 확인한다.
- Windows에서 확인할 것: 옵션 대화상자 라디오 3개, 기준을 바꾸면 다시 비교, 툴팁 사유 표시.

### 결과 (Linux, .NET 8 SDK)

- 수정 전 재현: `TimeOnlyDifference_IsDifferentByDefault_AndSameWhenContentVerified`, `Settings_RoundTripAndDefaults` 실패 확인.
- 수정 후: 전체 335건 중 329 통과·3 건너뜀·3 실패. 실패 3건은 모두 master에서도 실패한다(이번 변경과 무관).
  - `DirectoryCompareServiceTests` 2건: 기존 Linux 전용 실패.
  - `GitLibReaderTests.UpstreamRanges_MatchCli`: 이 환경에서 master로 되돌려도 같은 실패.
- `VerifyContent_ReadsOnlyWhenTimeDiffers_AndUnreadableIsUnknown`은 Windows 전용이라 건너뜀(Windows CI에서 실행).
- 앱 본체를 `EnableWindowsTargeting`으로 컴파일: 기존 콘솔 패널 Windows 전용 패키지 오류 1종 외에 새 오류·경고 없음.

## 변경 이력

- 2026-10-15: 외부 분석 자료로 폴더 병합 계획을 검토했으나 얻을 것이 없어 폐기했다(PR #57 닫음). 검토 중 찾은 이 판정 한계만 수정 항목으로 남겼다(Todo).
- 2026-10-15: 사용자 결정 — 가벼운 비교를 기본으로, 무거운 비교는 옵션으로. 새 옵션 기본 끔으로 구현(Ready for Verification).

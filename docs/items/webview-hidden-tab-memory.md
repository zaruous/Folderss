# 가려진 WebView2 뷰어 탭의 메모리 줄이기

- 상태: Ready for Verification

## 요구사항

마크다운·Monaco·Text 뷰어 탭을 여러 개 열어 두었을 때, 보이지 않는 탭이 차지하는 메모리를 줄인다. 다시 그 탭으로 가면 그대로 동작해야 한다.

## 원인 분석 또는 설계

- 앱에서 측정(마크다운 탭 5개, Folderss 전용 WebView2 프로세스만 집계): 탭마다 렌더러 프로세스가 하나씩 떠 작업 집합 90~128MB,
  렌더러 합계 약 530~540MB, Folderss WebView2 전체 약 1,050MB. 공용 프로세스(브라우저·GPU·유틸리티)는 처음 한 번 약 530MB.
- WebView2가 제공하는 두 기능을 쓴다.
  - `CoreWebView2.MemoryUsageTargetLevel = Low`: 가려지는 즉시. 렌더러가 메모리를 줄인다.
  - `CoreWebView2.TrySuspendAsync()`: 30초 넘게 계속 가려져 있으면. 스크립트·타이머를 멈춰 CPU도 쓰지 않는다.
  - 다시 보이면 `Resume()`·`Normal`로 되돌린다(WebView2도 보일 때 자동 재개).
- 가려짐 판단은 탭 활성(`SetActive`)이 아니라 실제 화면 표시(`IsVisibleChanged`)로 한다. 활성은 "포커스" 의미라 보이는 탭도 비활성일 수 있다.
- 일시 중지된 WebView에 스크립트를 보내지 않도록, 가려진 탭에 오는 테마 변경은 미뤘다가 다시 보일 때 한 번만 적용한다.
  (테마를 바꿀 때 보이는 탭만 다시 그리게 되어 탭이 많을 때 테마 변경도 가벼워진다.) 다른 문서 제목 링크로 넘어온 이동도 보일 때 처리한다.
- `TextViewer`는 `IDisposable`이 아니어서 탭을 닫아도 WebView2가 해제되지 않았다. 마크다운·Monaco와 같은 방식으로 해제하도록 함께 고쳤다.

## 구현 내용

- `Viewers/WebViewIdleSaver.cs`(신규): 뷰어(UserControl)의 `IsVisibleChanged`를 받아 메모리 목표 조정, 30초 뒤 일시 중지, 다시 보일 때 재개 후 `onVisible` 콜백 호출.
  지원하지 않는 런타임 등에서 실패하면 메모리 절약만 포기한다.
- `MarkdownViewer`, `MonacoViewer`, `TextViewer`
  - WebView2 준비 직후 `WebViewIdleSaver` 생성, `Dispose`에서 해제.
  - `ApplyTheme`: 보이지 않으면 페이지에 보내지 않음. `OnBecameVisible`에서 페이지에 마지막으로 보낸 테마(`_pageTheme`)와 다르면 적용.
  - `MarkdownViewer.ConsumePendingAnchorAsync`: 보이지 않으면 남겨 두고 `OnBecameVisible`에서 처리.
- `TextViewer`: `IDisposable` 구현(이벤트 해제, `WebView.Dispose()`), 초기화 대기 중 Dispose 시 중단.

## 변경 파일

- `Folderss/Viewers/WebViewIdleSaver.cs` (신규)
- `Folderss/Viewers/MarkdownViewer.xaml.cs`, `MonacoViewer.xaml.cs`, `TextViewer.xaml.cs`
- `docs/architecture.md`
- `docs/items/webview-hidden-tab-memory.md`

## 검증

- `dotnet build .\Folderss.sln -c Debug` — 오류 0개.
- 앱에서 같은 조건으로 측정(마크다운 탭 5개를 차례로 연 뒤, 1개 보임·4개 가려짐):

| | 변경 전 | 변경 후 |
|---|---|---|
| 탭별 렌더러 작업 집합 | 90~128MB | 보이는 탭 약 88MB, 가려진 탭 1~37MB |
| 렌더러 합계 | 약 530~540MB | 약 140~150MB (탭 전환 뒤 약 60~70MB) |
| Folderss WebView2 전체 | 약 1,050MB | 약 600~670MB |
| 탭별 커밋(전용) 메모리 | 41~75MB | 39~73MB (거의 같음) |

  - 실제 RAM 사용(작업 집합, 작업 관리자 "메모리")은 크게 줄고, 커밋 메모리는 거의 같다. 가려진 탭의 메모리는 버려지는 것이 아니라 RAM 밖(페이징·압축)으로 밀려난다.
- 일시 중지 확인(DevTools 프로토콜로 페이지 타이머 실행 여부 확인): 30초 넘게 가려진 탭 4개 모두 타이머가 멈춤(일시 중지), 보이는 탭은 정상.
  가려진 탭으로 전환하면 즉시 `visible`·타이머 정상(재개), 떠난 탭은 30초 뒤 다시 일시 중지.
- 앱에서 확인 필요: 탭 여러 개 연 상태에서 테마 변경 후 각 탭으로 이동했을 때 새 테마로 보이는지, 오래 가려 둔 탭으로 돌아왔을 때 스크롤·편집 상태가 그대로인지,
  코드·텍스트 탭을 닫았다 열기 반복 후 오류가 없는지, 트레이로 숨겼다가 복원했을 때 정상인지.
- 알려진 제약: 오래 가려 둔 탭으로 처음 돌아갈 때 RAM 밖으로 밀려난 메모리를 다시 읽느라 아주 잠깐 늦을 수 있다.

## 변경 이력

- 2026-10-16: 구현, 빌드·앱 측정 완료. 앱 확인 대기.

# WebView2 뷰어 첫 표시 약 2초 지연 해결 (SmartScreen 평판 검사)

- 상태: Ready for Verification

## 요구사항

마크다운(그리고 Monaco·Text) 뷰어 탭을 열 때 첫 화면이 늦게 뜨는 문제를 줄인다.

## 원인 분석 또는 설계

앱 안에서 단계별 시간을 재 원인을 좁혔다(세션 복원으로 열리는 `README.md` 탭, 앱 재시작 3회 평균).

| 단계 | 시간 |
|---|---|
| 뷰어 생성 → WebView2 환경 준비 | 약 310ms (대부분 앱 시작 중 레이아웃) |
| WebView2 준비(`EnsureCoreWebView2Async`) | 약 620ms |
| **페이지 준비(`Navigate` → `NavigationCompleted`)** | **약 2,110ms** |
| 본문 전달·첫 렌더링 | 약 80ms |

- 페이지 쪽 기록(`performance` navigation 항목)으로는 HTML 응답이 3~8ms에 끝났는데, 첫 하위 리소스 요청이 2,012ms에 시작됐다.
- 렌더러가 이미 떠 있는 상태에서 다시 읽어도(Page.reload) 같은 2초가 걸려 프로세스 시작 비용이 아니었다.
- `WebResourceRequested` 필터를 좁혀도 그대로였다(C# 처리기는 페이지 자체 파일 요청에 호출되지도 않음).
- DevTools 프로토콜 이벤트에서 응답 수신(3ms) 뒤 탐색 확정(`lifecycleEvent init`)이 2,011ms에 일어나, 응답 후 확정 전 단계에서 기다리는 것이 확인됐다.
- `CoreWebView2Settings.IsReputationCheckingRequired = false`(SmartScreen 평판 검사 끔)로 바꾸자 첫 탐색 2,048ms → 308ms, 다시 읽기 2,015ms → 17ms.
  사내망 등에서 평판 검사 서버 응답을 기다리다 시간 초과(약 2초)로 넘어가는 것으로 판단한다.
- 이 뷰어들은 내부 가상 호스트(`folderss-viewer`) 페이지만 열고, 다른 주소로의 이동은 `NavigationStarting`에서 취소하므로 평판 검사가 보호할 대상이 없다.

## 구현 내용

- `MarkdownViewer`, `MonacoViewer`, `TextViewer`의 WebView2 초기화에서 가상 호스트 매핑 전에
  `WebView.CoreWebView2.Settings.IsReputationCheckingRequired = false` 설정(WebView2 SDK 1.0.2739.15 지원).
- 측정에 쓴 임시 계측 코드(시간 로그, 디버그 포트)는 측정 후 모두 제거했다.

## 변경 파일

- `Folderss/Viewers/MarkdownViewer.xaml.cs`
- `Folderss/Viewers/MonacoViewer.xaml.cs`
- `Folderss/Viewers/TextViewer.xaml.cs`
- `docs/architecture.md`
- `docs/items/webview-smartscreen-load-delay.md`

## 검증

- `dotnet build .\Folderss.sln -c Debug` — 오류 0개.
- 앱 재시작 3회 측정(`README.md` 탭, 뷰어 생성 → 본문 표시 완료):

| 구성 | 전체 | 페이지 준비 |
|---|---|---|
| 변경 전 | 약 3,140ms | 약 2,110ms |
| 평판 검사만 끔 | 약 1,415ms | 약 370ms |
| 평판 검사 끔 + 라이브러리 지연 로딩(`markdown-viewer-lazy-libs.md`) | 약 1,285ms | 약 320ms |

- 앱에서 확인 필요: 마크다운·코드·텍스트 탭을 열 때 체감 속도.
- 남은 비용: WebView2 준비(`EnsureCoreWebView2Async`) 약 620ms. 환경 공유·미리 준비로 줄일 수 있는지는 별도 검토.

## 변경 이력

- 2026-10-16: 원인 측정·수정, 빌드·앱 측정 완료. 앱 체감 확인 대기.

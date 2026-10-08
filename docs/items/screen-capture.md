# 화면 영역 캡쳐 (⋯ 메뉴 > 화면 캡쳐…)

- 상태: Ready for Verification

## 요구사항

- ⋯ 메뉴의 `테마` 아래에 캡쳐 도구를 둔다.
- 픽픽 등 캡쳐 도구처럼 반투명 바탕 위에서 마우스로 영역을 지정해 캡쳐하고, 결과를 팝업으로 보여 준다.
- (2단계, 이번 범위 아님) 결과 위에 도형·텍스트 넣기, 자르기, 리사이즈 후 저장.

## 원인 분석 또는 설계

사용자와 합의한 기본값(1단계):

- **정지 화면 방식**: 메뉴를 누르면 가상 화면 전체를 먼저 찍고, 그 그림을 모든 모니터를 덮는 오버레이 창에 깔아 그 위에서 고른다. 오버레이 자신이 찍히지 않고, 고르는 동안 화면이 바뀌어도 누른 순간의 모습이 남는다.
- **Folderss 창 숨김**: 찍기 전에 본체 창을 `Hide()`하고 300ms 기다린다(⋯ 메뉴 닫힘·DWM 숨김 페이드). 그래서 Folderss 자신은 찍을 수 없다.
- **저장**: `저장` 버튼은 활성 폴더 패널에 `캡쳐_yyyyMMdd_HHmmss.png`(같은 이름이 있으면 `_2`…)로 바로 저장하고, 옆 `▾`는 다른 이름으로 저장(PNG/JPEG/BMP, 확장자로 형식 결정, 그 밖의 확장자는 거부). 활성 폴더가 고정(📌)이면 새 파일과 같은 규칙으로 막는다.
- **클립보드**: 결과 창이 열리면 자동 복사. 실패는 `ClipboardService`가 알린다.
- **단축키 없음**: 메뉴만. 전역 핫키는 다른 프로그램과 충돌 문제가 있어 별도 작업.

DPI·멀티모니터:

- 앱은 시스템 DPI 모드(매니페스트 없음)다. `SystemInformation.VirtualScreen`(픽셀)과 `SystemParameters.VirtualScreen*`(DIP)은 같은 DPI 문맥의 값이라 서로 맞는다. 왼쪽·위쪽 모니터는 음수 좌표.
- 자르는 위치는 화면 좌표가 아니라 오버레이에 늘려 그린 그림 기준 비율(`ScreenCaptureService.ToPixelRect`)이라, 창이 실제 화면과 조금 어긋나도 사용자가 본 그림 그대로 잘린다.
- 알려진 제약: 배율이 다른 보조 모니터는 시스템 DPI 앱에 대해 Windows가 가상화한 해상도로 찍혀 흐릿할 수 있다. 앱 전체를 PerMonitorV2로 바꾸는 것은 AvalonDock·WebView2 전체에 영향이 커 캡쳐 때문에 하지 않았다.

## 구현 내용

- `Services/ScreenCaptureService.cs` (신규, 순수 로직) — `ToPixelRect`(드래그 방향 무관, 이미지 밖은 잘라냄, 반올림), `IsSelectable`(3픽셀 미만은 클릭으로 봄), `NextCapturePath`, `FormatFromPath`.
- `CaptureOverlayWindow.cs` (신규) — `CaptureVirtualScreen`(GDI `CopyFromScreen` → `BitmapSource`, DPI는 시스템 배율로 맞춰 결과 창에서 1:1 픽셀로 보이게 함). 테두리 없는 Topmost 창을 가상 화면 크기로 놓고, `CombinedGeometry(Exclude)`로 선택 영역만 뚫린 반투명 막, 선택 테두리, 픽셀 크기 표시, 안내문. 마우스를 놓으면 `CroppedBitmap`으로 잘라 닫음. Esc·오른쪽 클릭은 취소.
- `CaptureResultWindow.cs` (신규) — 비모달 팝업. 이미지는 1:1(넘치면 스크롤), `저장` / `▾` / `복사` / `닫기`, 상태 줄. 저장은 `SettingsFile.Write`(임시 파일 후 교체)로 실패 시 반쪽 파일·기존 파일 손상이 없게 하고, 실패는 메시지로 알림.
- `MainWindow.xaml` — ⋯ 메뉴 `테마` 바로 아래 `화면 캡쳐…`.
- `MainWindow.xaml.cs` — `ScreenCapture_Click`(숨김 → 찍기 → 오버레이 → 항상 다시 보이기 → 결과 창), `GetCaptureSaveFolder`(폴더 없음·고정 검사), `OnCaptureSaved`(활성 패널이 그 폴더면 새로 고치고 저장한 파일 선택). 잠긴 화면·UAC 보안 데스크톱처럼 찍을 수 없으면 알림.

## 변경 파일

- `Folderss/Services/ScreenCaptureService.cs`
- `Folderss/CaptureOverlayWindow.cs`
- `Folderss/CaptureResultWindow.cs`
- `Folderss/MainWindow.xaml`
- `Folderss/MainWindow.xaml.cs`
- `tests/Folderss.SearchTests/ScreenCaptureServiceTests.cs`, `Folderss.SearchTests.csproj`
- `README.md`, `docs/architecture.md`

## 검증

- `dotnet test tests/Folderss.SearchTests --filter ScreenCaptureServiceTests` — 18개 통과(배율 변환, 역방향 드래그, 창 밖 좌표 잘라냄, 부동소수 1픽셀 오차, 클릭 판정, 파일 이름·중복 접미사, 확장자 → 형식).
- 리눅스에서 `dotnet build Folderss/Folderss.csproj -p:EnableWindowsTargeting=true`로 컴파일 확인 — 새 코드 오류 없음. 남은 오류 1건(`ConsolePanel`의 `Microsoft.Terminal` 참조)은 기준 브랜치에서도 같은 리눅스 전용 문제.
- **Windows 실기 확인 필요**(이 환경에서 실행 불가): 단일·다중 모니터(왼쪽/위쪽 보조 모니터 포함), 125%/150% 배율, 배율이 서로 다른 모니터, 오버레이 Esc·오른쪽 클릭 취소, 저장·▾·복사, 고정 폴더 저장 거부, 최대화 상태에서 캡쳐 후 창 복원.

## 변경 이력

- 2026-10-08: 1단계(영역 선택 캡쳐 + 결과 팝업) 구현. 2단계(도형·텍스트·자르기·리사이즈)는 1단계 실기 확인 후 별도 작업.

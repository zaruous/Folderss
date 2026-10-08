# 화면 영역 캡쳐 (⋯ 메뉴 > 화면 캡쳐…)

- 상태: Ready for Verification

## 요구사항

- ⋯ 메뉴의 `테마` 아래에 캡쳐 도구를 둔다.
- 픽픽 등 캡쳐 도구처럼 반투명 바탕 위에서 마우스로 영역을 지정해 캡쳐하고, 결과를 팝업으로 보여 준다.
- 2단계: 결과 위에 도형·텍스트 넣기, 캡쳐된 영역 자르기, 이미지 리사이즈 후 저장.

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

2단계 편집(합의 없이 정한 기본값, 사용자 확인 대상):

- 도구는 사각형·타원·화살표·텍스트·자르기 + 크기 조절·되돌리기. 선택 후 이동, 다시 실행, 자유 곡선은 요청 밖이라 넣지 않음.
- 기본 도구는 없음(보기만) — 결과 창을 열자마자 클릭으로 도형이 그려지는 사고 방지. 같은 도구를 다시 누르면 꺼짐.
- 도형·텍스트는 저장 전까지 이미지 위의 WPF 요소(벡터)이고, 저장·복사·자르기·크기 조절 때 `RenderTargetBitmap`으로 합친다. 자르기·크기 조절은 합친 뒤 적용(그 뒤 개별 주석은 지울 수 없고 되돌리기로만 복원).
- 되돌리기 스냅숏은 (이미지 참조, 주석 배열)이라 도형 추가는 가볍지만, 자르기·크기 조절마다 이미지가 한 장씩 쌓인다. 횟수 제한은 두지 않았다.
- 좌표는 이미지 픽셀 단위(편집면 DIP = 픽셀), 화면에는 캡쳐 DPI만큼 줄여 1:1 픽셀로 보임. 글자 크기·굵기도 픽셀 단위.
- 자르기는 드래그를 놓으면 바로 적용(되돌리기 가능). 크기 조절은 대화상자(비율 유지 기본 켬, 한 변 최대 16384).
- 편집 후 저장·복사 없이 닫으면 확인한다. 복사도 "보관함"으로 본다.
- JPEG 저장은 합친 이미지(Pbgra32)를 24비트로 변환해 넘긴다(JPEG 인코더는 알파 형식을 받지 않음).

## 구현 내용

- `Services/ScreenCaptureService.cs` (신규, 순수 로직) — `ToPixelRect`(드래그 방향 무관, 이미지 밖은 잘라냄, 반올림), `IsSelectable`(3픽셀 미만은 클릭으로 봄), `NextCapturePath`, `FormatFromPath`.
- `CaptureOverlayWindow.cs` (신규) — `CaptureVirtualScreen`(GDI `CopyFromScreen` → `BitmapSource`, DPI는 시스템 배율로 맞춰 결과 창에서 1:1 픽셀로 보이게 함). 테두리 없는 Topmost 창을 가상 화면 크기로 놓고, `CombinedGeometry(Exclude)`로 선택 영역만 뚫린 반투명 막, 선택 테두리, 픽셀 크기 표시, 안내문. 마우스를 놓으면 `CroppedBitmap`으로 잘라 닫음. Esc·오른쪽 클릭은 취소.
- `CaptureResultWindow.cs` (신규) — 비모달 팝업. 이미지는 1:1(넘치면 스크롤), `저장` / `▾` / `복사` / `닫기`, 상태 줄. 저장은 `SettingsFile.Write`(임시 파일 후 교체)로 실패 시 반쪽 파일·기존 파일 손상이 없게 하고, 실패는 메시지로 알림.
- `MainWindow.xaml` — ⋯ 메뉴 `테마` 바로 아래 `화면 캡쳐…`.
- `MainWindow.xaml.cs` — `ScreenCapture_Click`(숨김 → 찍기 → 오버레이 → 항상 다시 보이기 → 결과 창), `GetCaptureSaveFolder`(폴더 없음·고정 검사), `OnCaptureSaved`(활성 패널이 그 폴더면 새로 고치고 저장한 파일 선택). 잠긴 화면·UAC 보안 데스크톱처럼 찍을 수 없으면 알림.

- 2단계 `CaptureEditor.cs` (신규) — 합쳐서 내보내는 층(이미지 + 주석)과 그리는 중 층(미리보기 도형, 자르기 틀, 텍스트 입력 상자)을 분리. 텍스트는 확정 시 `GetRectFromCharacterIndex(0)`로 입력 상자 안 첫 글자 위치를 재서 같은 자리에 `TextBlock`을 놓는다(테마 TextBox 여백과 무관). 크기 조절은 `BitmapScalingMode.HighQuality`로 다시 그림.
- 2단계 `CaptureResultWindow.cs` — 둘째 줄 도구(토글), 색 7가지, 굵기·글자 크기 선택, `크기 조절…`, `↶ 되돌리기`(Ctrl+Z, 텍스트 입력 중에는 입력 상자에 맡김), 미저장 닫기 확인. `CaptureResizeDialog`(GitDialogBase 상속).
- 2단계 `ScreenCaptureService` — `ArrowHead`, `ProportionalSide`, `IsValidSide`/`MaxImageSide`.

## 변경 파일

- `Folderss/Services/ScreenCaptureService.cs`
- `Folderss/CaptureOverlayWindow.cs`
- `Folderss/CaptureResultWindow.cs`
- `Folderss/CaptureEditor.cs` (2단계)
- `Folderss/MainWindow.xaml`
- `Folderss/MainWindow.xaml.cs`
- `tests/Folderss.SearchTests/ScreenCaptureServiceTests.cs`, `Folderss.SearchTests.csproj`
- `README.md`, `docs/architecture.md`

## 검증

- `dotnet test tests/Folderss.SearchTests --filter ScreenCaptureServiceTests` — 18개 통과(배율 변환, 역방향 드래그, 창 밖 좌표 잘라냄, 부동소수 1픽셀 오차, 클릭 판정, 파일 이름·중복 접미사, 확장자 → 형식).
- 리눅스에서 `dotnet build Folderss/Folderss.csproj -p:EnableWindowsTargeting=true`로 컴파일 확인 — 새 코드 오류 없음. 남은 오류 1건(`ConsolePanel`의 `Microsoft.Terminal` 참조)은 기준 브랜치에서도 같은 리눅스 전용 문제.
- **Windows 실기 확인 필요**(이 환경에서 실행 불가): 단일·다중 모니터(왼쪽/위쪽 보조 모니터 포함), 125%/150% 배율, 배율이 서로 다른 모니터, 오버레이 Esc·오른쪽 클릭 취소, 저장·▾·복사, 고정 폴더 저장 거부, 최대화 상태에서 캡쳐 후 창 복원.

- 2단계: `dotnet test ... --filter ScreenCaptureServiceTests` 30개 통과(화살표 머리 좌표·최소 머리·짧은 화살표·길이 0, 비율 유지 반올림, 한 변 범위 추가). 리눅스 컴파일에서 새 코드 오류 없음(남은 1건은 위와 같은 `ConsolePanel`).
- **2단계 Windows 실기 확인 필요**: 각 도구 그리기, 텍스트 확정 위치가 입력 때와 같은지, 125%/150% 배율에서 도형·글자 선명도, 자르기 후 되돌리기, 크기 조절(축소·확대·비율 해제), 편집 후 저장한 PNG/JPEG/BMP에 주석이 들어가는지, 미저장 닫기 확인.

## 변경 이력

- 2026-10-08: 1단계(영역 선택 캡쳐 + 결과 팝업) 구현. 2단계(도형·텍스트·자르기·리사이즈)는 1단계 실기 확인 후 별도 작업.
- 2026-10-08: 1단계 실기 확인 완료(사용자). 2단계 편집(도형·화살표·텍스트·자르기·크기 조절·되돌리기) 구현.

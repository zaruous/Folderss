# 디스크 사용량 보기 메뉴 추가

- 상태: Ready for Verification

## 요구사항

- 메인 메뉴 `보기`에 `디스크 사용량 보기` 항목 추가.
- 이 메뉴를 선택하면 즐겨찾기 패널 위쪽에 바로가기를 추가한다.
- 사용자 드라이브별 디스크 사용량을 가로바 형태와 GB 단위(총량/사용량/여유 공간)로 표시한다.

## 원인 분석 또는 설계

- 디스크 사용량은 실제 파일 경로가 아니므로 기존 `FavoriteLocation`(경로 기반) 즐겨찾기 구조에 그대로 끼워 넣을 수 없다.
  `FavoriteLocation`에 `IsSpecial`/`SpecialKind` 필드를 추가하고, `FavoritesConfiguration`에 그룹과 별개인
  `Pinned` 컬렉션을 두어 그룹 트리 위쪽에 항상 고정 표시되는 특수 바로가기를 표현한다.
- 디스크 사용량 탭은 콘솔 탭(`ContentId == "console"`)과 동일하게 단일 인스턴스 `LayoutDocument`
  (`ContentId == "disk-usage"`)로 열고, 파일 뷰어 탭(`OpenViewerTab`)과 같은 방식으로 `+ 새 패널` 탭 앞에 삽입한다.
- 가로바는 `Grid`의 두 `ColumnDefinition`(사용/여유)을 `GridLength(fraction, Star)`로 바인딩해 컨테이너 폭에 맞춰
  비율대로 늘어나도록 구현한다. `IValueConverter`(`FractionToStarConverter`)가 필요해 `Converters/` 폴더를 신설했다.
- 즐겨찾기 바로가기는 메뉴를 처음 클릭할 때 한 번만 추가하고(중복 방지), 이후에는 같은 바로가기를 눌러도
  동일한 디스크 사용량 탭을 열도록 한다.
- (추가 요청) 고정 바로가기는 즐겨찾기 그룹 트리 내부가 아니라 "즐겨찾기" 라벨을 포함한 `FavoritesPanel` 전체보다
  위쪽에, 완전히 별도인 레이어로 배치해야 한다. 이를 위해 `Controls/PinnedShortcutsPanel`을 새 `UserControl`로
  분리하고, `MainWindow.xaml`의 `FavoritesDock` 안에서 `FavoritesPanel` 위 Grid Row에 배치했다.
  데이터는 여전히 `FavoritesConfiguration.Pinned`(즐겨찾기와 같은 `favorites.xml`에 저장) 하나만 사용하며,
  `FavoritesPanel.PinnedItems`로 노출한 같은 `ObservableCollection` 인스턴스를
  `PinnedShortcutsPanel.SetItemsSource(...)`로 공유해 두 레이어가 항상 동기화되도록 했다.

## 구현 내용

- `Models/DriveUsageInfo.cs` 추가 — 드라이브 이름·총량/여유 바이트, GB 변환, 사용/여유 비율 프로퍼티.
- `Services/DiskUsageService.cs` 추가 — `DriveInfo.GetDrives()`에서 `IsReady`인 드라이브만 조회.
- `Converters/FractionToStarConverter.cs` 추가 — 0~1 비율을 `GridLength(fraction, Star)`로 변환.
- `Controls/DiskUsagePanel.xaml/.cs` 추가 — 드라이브별 카드 UI(이름, 가로바, 총량/사용량/여유 GB 텍스트), 새로고침 버튼.
- `Models/FavoriteLocation.cs` — `FavoriteLocation.IsSpecial`/`SpecialKind` 필드, `FavoritesConfiguration.Pinned` 컬렉션 추가.
- `Services/FavoritesService.cs` — `Normalize()`에서 `Pinned` null 가드 추가.
- `Controls/FavoritesPanel.xaml/.cs` — 고정 바로가기 UI는 제거하고 데이터만 소유: `PinnedItems` 프로퍼티로
  `_configuration.Pinned` 노출, `PinDiskUsageShortcut()`(중복 방지 후 맨 위 삽입 + 저장).
- `Controls/PinnedShortcutsPanel.xaml/.cs` 추가 — 즐겨찾기와 별도인 `UserControl`. 외부에서 주입한
  `ObservableCollection<FavoriteLocation>`을 표시하고(비어 있으면 숨김), 클릭 시 `NavigateRequested`에
  `SpecialKind` 전달.
- `MainWindow.xaml` — `보기` 메뉴에 `디스크 사용량 보기` 항목 추가. `FavoritesDock` 안에서
  `PinnedShortcutsPanel`을 `FavoritesPanel` 위 Grid Row 0에 별도 레이어로 배치.
- `MainWindow.xaml.cs` — 생성자에서 `PinnedShortcutsPanel.SetItemsSource(FavoritesPanel.PinnedItems)`로 데이터 공유,
  `ShowDiskUsage_Click`(바로가기 고정 + 탭 열기), `ShowDiskUsagePanel()`(단일 인스턴스 탭 관리),
  `FavoritesPanel_NavigateRequested`(두 컨트롤이 공유하는 핸들러)에서 `SpecialKind == "diskusage"` 분기 처리.
- `docs/architecture.md`, `README.md` 갱신.

## 변경 파일

- `Folderss/Models/DriveUsageInfo.cs` (신규)
- `Folderss/Services/DiskUsageService.cs` (신규)
- `Folderss/Converters/FractionToStarConverter.cs` (신규)
- `Folderss/Controls/DiskUsagePanel.xaml` (신규)
- `Folderss/Controls/DiskUsagePanel.xaml.cs` (신규)
- `Folderss/Controls/PinnedShortcutsPanel.xaml` (신규)
- `Folderss/Controls/PinnedShortcutsPanel.xaml.cs` (신규)
- `Folderss/Models/FavoriteLocation.cs`
- `Folderss/Services/FavoritesService.cs`
- `Folderss/Controls/FavoritesPanel.xaml`
- `Folderss/Controls/FavoritesPanel.xaml.cs`
- `Folderss/MainWindow.xaml`
- `Folderss/MainWindow.xaml.cs`
- `docs/architecture.md`
- `README.md`
- `docs/items/disk-usage-view.md`

## 검증

- [ ] `dotnet build .\Folderss.sln -c Debug` 성공 (`Exit: 0`) — 작업 환경(Linux, `dotnet` SDK 미설치)에서 빌드 불가, Windows 개발 환경에서 확인 필요
- [ ] `보기 > 디스크 사용량 보기` 클릭 시 즐겨찾기 패널("즐겨찾기" 헤더 포함) 위쪽 별도 레이어에 `디스크 사용량` 바로가기가 (최초 1회만) 추가됨
- [ ] 같은 메뉴를 다시 클릭하거나 즐겨찾기의 `디스크 사용량` 바로가기를 클릭하면 같은 탭이 재사용됨(중복 탭 생성 안 함)
- [ ] 디스크 사용량 탭에 준비된 모든 드라이브가 표시되고, 가로바 비율과 총량/사용량/여유 공간(GB)이 실제 값과 일치
- [ ] `새로 고침` 버튼 클릭 시 최신 사용량으로 갱신
- [ ] 앱 재시작 후에도 즐겨찾기 상단 고정 바로가기가 유지됨 (`favorites.xml`에 저장)
- [ ] 테마 전환 시 디스크 사용량 패널 색상이 함께 갱신됨

## 변경 이력

- 2026-07-17: 작업 항목 생성 및 구현
- 2026-07-18: 고정 바로가기 레이어(`PinnedShortcutsPanel`) 제거 — 미니 패널과 중복되어 즐겨찾기 도크에서 "디스크 사용량" 바로가기 레이어 삭제. 데이터 모델(`Pinned`)은 호환을 위해 유지.
- 2026-07-18: 즐겨찾기 위 컴팩트 미니 패널(`DiskUsageMiniPanel`, ContentId `disk-usage-mini`) 추가 — 즐겨찾기 열을 세로 분할해 상단에 독립 도킹 패널로 배치, 스플리터로 높이 조절 가능. 구버전 저장 레이아웃은 `EnsureDiskUsageMiniDock()`이 복원 후 자동 삽입.
- 2026-07-17: 시작 크래시 수정 — `FavoritesPanel`이 `PinnedShortcutsPanel`과 함께 XAML `Grid`(`FavoritesDockContent`)로 감싸진 뒤에도 `ResolveDockContent`/`BuildDefaultDockLayout`/`CreateFavoritesDock`가 `FavoritesPanel`을 도킹 콘텐츠로 직접 할당해 "지정한 요소가 이미 다른 요소의 논리 자식입니다" `InvalidOperationException`으로 크래시. 세 곳 모두 `FavoritesDockContent`(Grid)를 사용하도록 수정.

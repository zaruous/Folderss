# 즐겨찾기에 파일 추가 지원

- 상태: Ready for Verification

## 요구사항

즐겨찾기 패널에 폴더뿐만 아니라 파일도 추가할 수 있어야 한다.
파일 즐겨찾기를 더블클릭하면 뷰어 탭으로 열어야 한다.

## 설계

- `FavoriteLocation` 모델에 `IsFile` 속성 추가
- `AddFavorite()` 메서드에서 `File.Exists()` 검사 추가
- 더블클릭 시 `IsFile`이면 `FavoriteNavigateEventArgs.IsFile = true`로 전달
- MainWindow에서 `IsFile`이면 `OpenViewerTab()`으로 파일 열기
- "+" 버튼 동작: 파일이 하나 선택된 상태이면 파일 추가, 아니면 현재 폴더 추가
- 파일 즐겨찾기 아이콘은 📄, 폴더 즐겨찾기는 기존 ★ 유지
- 파일 즐겨찾기에서는 폴더 전용 컨텍스트 메뉴 항목(Explorer, 터미널, 새 폴더/파일) 숨김

## 구현 내용

1. `FavoriteLocation.cs` — `IsFile` 속성 추가 (XML 직렬화 포함)
2. `FavoritesPanel.xaml.cs`:
   - `AddFavorite()` — 파일 경로 허용, 자동 이름은 `Path.GetFileName()` 사용
   - `FavoritesTree_MouseDoubleClick` — 파일이면 `IsFile=true`로 이벤트 발생
   - 컨텍스트 메뉴 — 파일 즐겨찾기에 폴더 전용 항목 숨김
   - 중복 메시지 — "폴더" → "항목"으로 통일
3. `FavoritesPanel.xaml` — DataTrigger로 `IsFile=true`이면 📄 아이콘 표시
4. `FavoriteNavigateEventArgs` — `IsFile` 속성 추가
5. `MainWindow.xaml.cs`:
   - `AddCurrentRequested` 핸들러 — 파일 선택 시 파일 추가
   - `NavigateRequested` 핸들러 — `IsFile`이면 `OpenViewerTab()` 호출

## 변경 파일

- `Folderss/Models/FavoriteLocation.cs`
- `Folderss/Controls/FavoritesPanel.xaml`
- `Folderss/Controls/FavoritesPanel.xaml.cs`
- `Folderss/MainWindow.xaml.cs`

## 검증

- [ ] 파일 선택 후 즐겨찾기 "+" 버튼 → 파일이 즐겨찾기에 추가됨
- [ ] 파일 미선택 시 "+" 버튼 → 기존처럼 현재 폴더 추가
- [ ] 파일 즐겨찾기 더블클릭 → 뷰어 탭에서 파일 열림
- [ ] 폴더 즐겨찾기 더블클릭 → 기존처럼 폴더 이동
- [ ] 파일 즐겨찾기 아이콘이 📄로 표시
- [ ] 파일 즐겨찾기 우클릭 시 Explorer/터미널/새 폴더 메뉴 숨김
- [ ] 즐겨찾기 저장/복원 시 IsFile 값 유지

## 변경 이력

- 2026-06-30: 초기 구현

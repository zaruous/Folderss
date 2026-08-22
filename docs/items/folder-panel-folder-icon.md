# 폴더 패널 그리드의 폴더 아이콘 구분 개선

- 상태: Done

## 요구사항

폴더 패널의 파일 목록(그리드)에서 폴더 항목이 파일 항목과 잘 구분되지 않는다.
폴더 항목의 아이콘을 열린 폴더 아이콘(`📂`)으로 지정해 파일 아이콘과 시각적으로 구분되게 한다.

## 원인 분석

`Models/FileSystemItem.Icon`이 디렉터리에 대해 닫힌 폴더 이모지(`📁`)를 반환했다.
`📁`는 목록에서 사용하는 파일 아이콘(`📄`, `🖼`, `📦`)과 글리프 크기·실루엣이 비슷해
`Controls/FolderBrowser.xaml`의 아이콘 열(폭 38) 크기에서는 폴더와 파일이 한눈에 구분되지 않았다.

## 구현 내용

- `Models/FileSystemItem.Icon`의 디렉터리 분기 반환값을 `📁`에서 `📂`로 변경했다.
  이 프로퍼티는 `Controls/FolderBrowser.xaml`의 첫 번째 `GridViewColumn`(`DisplayMemberBinding="{Binding Icon}"`)이
  유일하게 바인딩하므로, 파일 목록 그리드 전체에 한 번의 변경으로 반영된다.
- 폴더 트리(`FolderBrowser.GetTreeItemHeader`)와 즐겨찾기 패널(`FavoritesPanel.xaml`)은
  항목이 모두 폴더라 파일과 혼동될 여지가 없어 기존 `📁`를 유지했다.

## 변경 파일

- `Folderss/Models/FileSystemItem.cs`

## 검증

- 코드 변경은 이모지 리터럴 한 곳 교체로 한정되며, 빌드는 리눅스 원격 환경(`dotnet` SDK 미설치, `net8.0-windows` 타깃)에서 실행하지 못했다.
  Windows에서 `dotnet build .\Folderss.sln -c Debug` 확인이 필요하다.
- [ ] 폴더 패널 파일 목록에서 폴더 행의 아이콘이 `📂`로 표시되는지 확인
- [ ] 같은 목록에서 파일 행 아이콘(`📄`, `🖼`, `📦`)과 시각적으로 구분되는지 확인
- [ ] 폴더 트리와 즐겨찾기 패널의 폴더 아이콘(`📁`)이 기존과 동일한지 확인

## 변경 이력

- 2026-08-22: 파일 목록 그리드의 폴더 아이콘을 `📂`로 변경했다.
- 2026-08-22: 사용자 확인 후 `v1.6.1` 릴리스에 포함했다.

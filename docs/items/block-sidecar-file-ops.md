# 파일 작업에서 블록 편집기 사이드카 함께 처리 (#78)

- 상태: Ready for Verification
- 상태 전환일: 2026-11-01

## 요구사항

이슈 #78(블록 편집기 사이드카 D안)의 남은 일. 블록 편집기는 Markdown으로 나타낼 수 없는 블록 속성(색·이미지 크기·열 배치·표 열 너비 등)을
문서 옆 `문서.md.meta.json`(`Services/BlockSidecar`)에 저장한다. 지금은 메인 패널에서 `.md`만 옮기거나 이름을 바꾸면 속성이 떨어져 나간다.

1. 메인 패널에서 `X.md`(·`.markdown`)를 이름 바꾸기·이동·복사·삭제(휴지통/영구)할 때 같은 폴더의 `X.md.meta.json`도 함께 처리한다. 이름 바꾸기는 사이드카도 `새이름.md.meta.json`으로.
2. 폴더째 작업은 안의 파일이 그대로 따라가므로 추가 처리 없음(확인만).
3. 사이드카만 고른 경우, 문서·사이드카를 둘 다 고른 경우(중복 처리 금지), 대상에 이미 사이드카가 있는 이름 충돌(기존 충돌 규칙과 일관),
   사이드카 작업만 실패한 경우(문서는 성공 처리 + 경고 — 데이터 손실 없는 쪽).
4. 실행 취소(`Ctrl+Z`)는 문서와 사이드카를 한 단계로 되돌린다.
5. 파일 목록에서 사이드카를 숨길지는 범위 밖(지금처럼 보임).
6. zip 안 경로·폴더 비교 창·끌어다 놓기·붙여넣기가 같은 대기열을 타는지 확인해 목록으로 남긴다.
7. 블록 편집기 페이지 링크(`/페이지`)·이미지 폴더 같은 다른 동반 파일은 범위 밖.

## 설계

### 동반 파일 찾기 (`Services/BlockSidecarFiles`, 순수 로직)

- 문서: 확장자 `.md`·`.markdown`(대소문자 무시)인 **파일**(같은 이름의 폴더는 아님).
- 동반 파일: `BlockSidecar.PathFor(문서)` = `문서 전체 이름 + ".meta.json"`이 실제로 있을 때만. 작업 스레드가 항목을 실행할 때 확인한다(대기열에 넣은 뒤 생긴·지워진 사이드카도 맞게).
- `Pair(sources)`: 작업 목록을 (항목, 동반 사이드카)로 묶는다. 문서와 그 사이드카를 둘 다 골랐으면 사이드카 항목은 목록에서 빼고 문서에 붙인다 → 한 번만 처리하고 진행률 항목 수도 1로 센다(`FileJob.SetTotalItems`).
  사이드카만 골랐으면 보통 파일로 그대로 처리(문서 없이 사이드카만 옮기거나 지울 수 있어야 함).
- `TargetFor(문서 대상)`: 문서가 실제로 간 경로(번호 붙은 새 이름 포함)의 사이드카 경로.

### 실행 (`FileJobQueue`)

- 동반 파일은 **문서 결과를 따른다**. 문서가 건너뜀·실패면 손대지 않고, 성공하면 문서 바로 뒤에(취소 확인 없이) 처리한다 — 취소가 문서와 사이드카 사이에 끼어 문서만 가는 지점을 만들지 않는다.
- 복사·이동(`TransferCompanion`): 문서가 간 자리의 `새이름.md.meta.json`으로. 작은 파일이라 `File.Copy`/`File.Move`로 하고 진행률 수치(파일 수·바이트)에는 넣지 않는다.
- 삭제(`DeleteCompanion`): 문서와 같은 방식(휴지통 작업이면 휴지통, 영구 삭제 작업이면 영구).
- 이름 변경(`Rename`): 문서 이름을 바꾼 뒤 사이드카를 `새이름.md.meta.json`으로. 대소문자만 바뀐 이름도 따라간다. 확장자를 `.txt` 등으로 바꿔도 사이드카는 이름을 따라간다(되돌려 다시 `.md`로 바꾸면 그대로 붙도록 — 규칙을 "항상 문서 이름을 따라감" 하나로 둠).

### 이름 충돌 규칙 (기존 충돌 창과 일관)

사이드카만 따로 묻지 않는다. 문서에 대한 선택이 그대로 적용된다.

| 문서 결과 | 사이드카 |
|---|---|
| 건너뛰기 | 건너뜀(원본·대상 사이드카 모두 그대로) |
| 바꾸기(옛 문서 → 휴지통) | 대상 자리의 옛 사이드카도 휴지통으로 보내고 바꿈 |
| 둘 다 유지(`a (2).md`) | `a (2).md.meta.json`으로 |
| 같은 폴더에 복사(자동 번호) | 번호 붙은 이름으로 |
| 충돌 없음, 그런데 대상 자리에 주인 없는 `a.md.meta.json`만 있음 | 그 파일을 휴지통으로 보내고 바꿈 — 남겨 두면 새로 온 문서에 엉뚱한 속성이 붙는다. 실행 취소하면 되살아난다 |
| 실패 | 그대로 |

휴지통을 쓸 수 없는 위치(네트워크·이동식 드라이브·긴 경로)라 대상 자리의 사이드카를 바꿀 수 없으면 사이드카만 실패로 본다(아래).

이름 변경은 원래도 같은 이름이 있으면 거부하는 즉시 작업이라 휴지통 바꾸기를 하지 않는다: 새 이름의 사이드카 자리가 다른 파일로 차 있으면 사이드카는 그대로 두고 경고한다.

### 사이드카만 실패

- 문서 작업은 성공으로 센다(완료 항목 +1, 실패 수 그대로). 작업의 오류 목록에 `a.md.meta.json: 블록 속성 파일을 문서와 함께 이동하지 못했습니다(문서는 이동됨, 속성 파일은 원래 자리에 남음) — 이유`를 남겨
  끝난 뒤 실패 요약에 함께 보인다. 사이드카는 원래 자리에 그대로 있으므로 잃는 데이터가 없다(사용자가 직접 옮기면 다시 붙음 — 블록 해시로 붙기 때문).
- 바꾸려고 휴지통에 보낸 대상 자리 사이드카는 되살리고, 다른 드라이브 이동이 복사만 하고 원본 지우기에서 실패하면 복사본을 지운다.
- 이름 변경은 기존 이력 경고 창(`파일 작업 이력`)에 같은 문구를 보인다.

### 실행 취소 — 같은 작업 기록의 항목으로 (새 `JournalOperationKind` 없음)

- 사이드카 처리 결과를 **문서와 같은 작업 기록(`JournalOperation`)의 별도 항목(`JournalItem`)** 으로 넣는다. 작업 하나 = 실행 취소 한 단계이므로 문서와 사이드카가 함께 되돌아간다.
  항목별 되돌리기(`Revert`: 이동 → 원래 자리로 + 바꾼 옛 항목 되살림, 복사 → 복사본을 휴지통으로 + 옛 항목 되살림, 휴지통 삭제 → 되살림, 이름 변경 → 옛 이름으로)와
  막힘 검사(`ItemProblems`: 그 사이 바뀜·원래 자리 점유 등)가 보통 파일과 똑같이 맞으므로 새 종류가 필요 없다.
  - 한 항목에 "동반 파일" 필드를 두는 안(문서 항목 하나에 사이드카 경로·휴지통 시각·바꾼 경로를 함께)도 봤지만, 사이드카 쪽 바꾸기·휴지통 시각·크기·수정 시각을 따로 다시 들고 `ItemProblems`·`Revert`를 두 갈래로 고쳐야 해 더 복잡하다.
  - 사이드카만 그 사이 바뀌었으면(편집기에서 다시 저장 등) 기존 규칙대로 그 항목만 막힘으로 보이고 확인 체크 후 문서만 되돌린다(바뀐 파일은 건드리지 않는 기존 원칙).
- `JournalItem.CompanionOf`(문서의 원래 경로, 기록 줄의 `"of"`)로 동반 항목임을 남긴다. 쓰는 곳: 작업 설명(`DescribeOperation`)의 항목 수에서 뺀다 — `이동 'a.md' → …`가 `이동 2개`로 보이지 않게.
  이름 변경 되돌리기 단계 설명에는 `(블록 속성 파일)`을 붙인다. 옛 기록에는 없는 필드이고 null이면 쓰지 않으므로 기존 이력·폴더 비교 창 이력과 호환된다.
- 이름 변경은 `RecordSingle`이 항목 여러 개를 한 작업으로 기록하도록 바꿨다.

### 사이드카 안의 `"file"` 필드

`BlockSidecar.Apply`는 `file` 필드를 **읽지도 검사하지도 않는다**(블록은 문서 해시·블록 해시로 붙는다). 그래서 파일 작업에서는 사이드카 내용을 고치지 않는다:
- 고치면 파일 작업이 "내용 쓰기"가 되어 실행 취소의 바뀜 검사(크기·수정 시각)와 복사본 되돌리기를 복잡하게 만들고, 쓰기 실패라는 새 실패 경로가 생긴다.
- `file`은 사람이 알아보기용 정보로 남는다. 블록 편집기에서 다음에 저장하면 새 이름으로 다시 쓰인다.
- 나중에 `file`을 검증하게 바꾸면(이름이 다르면 버림 등) 이 결정을 다시 봐야 한다 — `BlockSidecar.Serialize`·`Apply` 주석과 이 문서를 같이 고칠 것.

## 적용 범위 — 같은 대기열을 타는 경로

| 경로 | 대기열 | 사이드카 처리 |
|---|---|---|
| `F6`/⋯ `반대편 패널로 복사·이동`(`ExecuteTransfer` → `QueueFileTransfer`) | 탐 | 자동 적용 |
| 붙여넣기 `Ctrl+V`(`TryPasteFromClipboardInto` → `QueueFileTransfer`, 잘라내기 포함) | 탐 | 자동 적용 |
| 메인 창 패널로 끌어다 놓기(`FolderBrowser.FileList_Drop` → `QueueFileTransfer`) — 탐색기에서 끌어 온 것 포함 | 탐 | 자동 적용 |
| `Delete`/`Shift+Delete`(`DeleteSelected` → `QueueFileDelete`) | 탐 | 자동 적용. 휴지통 가능·불가 항목을 두 작업으로 나눠도 각 작업 안에서 짝지음 |
| 이름 변경(`Rename_Click` → `RenameRecorded` → `FileJobQueue.Rename`) | 즉시 작업 + 같은 이력 | 자동 적용 |
| 실행 취소(`Ctrl+Z`) | 탐 | 같은 작업 기록이라 한 단계 |
| 폴더째 복사·이동·삭제 | 탐 | 안의 사이드카가 그대로 따라감(추가 처리 없음, 테스트로 확인) |

적용되지 않는 경로(이번 범위 밖 — 사이드카가 따라가지 않음):

| 경로 | 이유 |
|---|---|
| 폴더 비교 창의 이동·복사·삭제(`FileOperationJournal.Move/Copy/Delete`, `compare-history`) | 비교 창은 자기 이력으로 직접 실행한다(대기열 아님). 두 이력을 섞지 않는 규칙 때문에 손대지 않음. 필요하면 같은 `BlockSidecarFiles.Pair`를 `FileOperationJournal.Run`에 붙이는 후속 작업 |
| zip 안 경로 | 메인 패널은 zip 안에서 삭제·이름 변경·붙여넣기·이동을 막고(`BlockedInArchive`), zip에서 꺼내기(`ExtractSelectionToFolder`)는 대기열이 아닌 압축 풀기다. 고른 `X.md`만 꺼내면 사이드카는 따라오지 않음 |
| `FolderBrowser.FileList_Drop`의 대체 경로(패널이 `MainWindow` 안에 있지 않을 때 `FileOperationService.Copy/Move` 직접 호출) | 지금 앱 구성에서는 쓰이지 않는 방어 경로 |
| 다른 프로그램(탐색기 등)으로 끌어 내보내기 | 셸이 처리 |
| Git 창의 변경 버리기(휴지통) | 파일 작업이 아니라 Git 작업 |
| 블록 편집기 자체의 다른 이름으로 저장 등 | 편집기 쪽 처리(이 작업은 블록 편집기 코드를 건드리지 않음) |

## 범위 밖 / 후속 검토

- **파일 목록에서 사이드카 숨기기**: 지금처럼 보인다. 숨기면 사이드카만 옮기기·지우기를 할 수 없고, 숨긴 파일이 있는 폴더를 "비었다"고 착각할 수 있어 설정으로 둘지 따로 검토.
- 폴더 비교 창에서 사이드카 짝짓기(위 표).
- 다른 동반 파일: 블록 편집기 페이지 링크(`/페이지`로 만든 하위 문서), 붙여 넣은 이미지 폴더, `.bak` 사이드카 보관본은 따라가지 않는다.
- 이름 충돌 창에 "블록 속성 파일도 함께 바뀜"을 보이는 안내 — 지금은 문서에 대한 선택이 그대로 적용된다는 것을 README에만 적었다.

## 구현 내용

- `Services/BlockSidecarFiles.cs`(새 파일, 순수 로직): `IsDocument`, `IsSidecar`, `DocumentOf`, `TargetFor`, `CompanionOf`, `Pair` + `SourceWithCompanion`.
- `Services/FileJobQueue.cs`: `Run`이 `Pair`로 항목을 묶어 문서 뒤 동반 파일 처리(`TransferCompanion`/`DeleteCompanion`, 같은 작업 기록에 `Record`), `Measure`는 묶은 목록 기준,
  `Rename`이 사이드카도 바꿔 한 작업으로 기록(`RenameCompanion`, `RecordSingle`이 항목 목록), `DescribeOperation`이 동반 항목을 세지 않음, 이름 변경 되돌리기 단계 설명 보강, 경고 문구 `CompanionWarning`.
- `Services/FileJobs.cs`: `FileJob.SetTotalItems`.
- `Services/FileOperationJournal.cs`: `JournalItem.CompanionOf` + 기록 줄 `"of"` 읽기·쓰기.
- 블록 편집기 코드(`Controls/BlockEditor*`, `BlockSidecar` 형식)는 바꾸지 않았다.

## 변경 파일

- `Folderss/Services/BlockSidecarFiles.cs` (새 파일)
- `Folderss/Services/FileJobQueue.cs`
- `Folderss/Services/FileJobs.cs`
- `Folderss/Services/FileOperationJournal.cs`
- `tests/Folderss.SearchTests/Folderss.SearchTests.csproj` — `BlockSidecarFiles.cs` 소스 링크
- `tests/Folderss.SearchTests/FileJobQueueTests.cs` — `partial`로 바꿈
- `tests/Folderss.SearchTests/FileJobQueueSidecarTests.cs` (새 파일)
- `docs/architecture.md`, `README.md`, `CLAUDE.md`, 이 문서

## 검증

- `dotnet build .\Folderss.sln -c Debug` — 오류 0, Exit 0.
- `dotnet build tests/Folderss.SearchTests` — 성공.
- 단위 테스트(`FileJobQueueSidecarTests.cs`, `FileJobQueueTests` partial — 가짜 휴지통과 임시 폴더의 실제 파일). 작업 중 `FileJobQueueTests|FileOperationJournalTests` 필터만 돌려 63개 통과, 전체 테스트는 병합 후 일괄:
  - `SidecarFiles_NamesAndCompanion` — 문서 확장자, `.markdown`, 사이드카 없는 문서, 다른 확장자, `X.md` 폴더, `DocumentOf`, `.md.meta.json` 같은 이름 없는 경우
  - `SidecarFiles_Pair_DropsSidecarSelectedWithItsDocument` — 둘 다 고르면 하나로, 사이드카만 고르면 보통 파일
  - `Move_DocumentTakesSidecar_UndoRestoresBothInOneStep` — 이동, 작업 설명이 `'a.md'`, 실행 취소 한 번에 둘 다
  - `Move_DocumentAndSidecarBothSelected_HandledOnce` — 항목 수 1, 오류 없음
  - `SidecarSelectedAlone_IsAnOrdinaryFile`
  - `CopyIntoSameFolder_SidecarGetsTheNumberedName` — `a (2).md.meta.json`, 되돌리기
  - `Conflict_SkipDocument_LeavesBothSidecarsAlone`
  - `Conflict_ReplaceDocument_ReplacesSidecar_UndoRestoresOldPair` — 휴지통 2개, 되돌리면 옛 문서·옛 사이드카
  - `Conflict_KeepBoth_SidecarFollowsNumberedName`
  - `OrphanSidecarAtDestination_IsSentToBin_UndoBringsItBack`
  - `SidecarFailsOnly_DocumentSucceeds_WithWarning` — 실패 수 0, 오류 1, 사이드카 원래 자리, 실행 취소는 문서만
  - `Delete_ToBin_TakesSidecar_UndoRestoresBoth_PermanentRemovesBoth`
  - `Rename_TakesSidecar_UndoRestoresBothInOneStep`
  - `Rename_SidecarNameTaken_DocumentRenamed_SidecarKept_Warns`
  - `CompanionMark_SurvivesHistoryReload` — `"of"` 저장·다시 읽기, 다시 연 뒤 실행 취소
  - `FolderMove_CarriesSidecarsInside_WithoutExtraItems`
- 병합 후 일괄: `dotnet test tests/Folderss.SearchTests`(전체 — 특히 `FileJobQueueTests`, `FileOperationJournalTests`, `BlockSidecarTests`).

### 수동 확인 목록 (실제 앱)

1. 블록 편집기에서 블록 색·이미지 크기를 넣고 저장한 `a.md`(옆에 `a.md.meta.json`)를 준비.
2. `F6` 이동 → 반대편에 `a.md`·`a.md.meta.json`이 함께 있고, 블록 편집기로 열면 색·크기가 그대로. 진행률 창 제목·항목 수는 1개.
3. `Ctrl+Z` → 확인 창 단계 2줄(문서·사이드카), 실행 후 둘 다 원래 폴더로.
4. 같은 폴더에 `Ctrl+C`/`Ctrl+V` → `a (2).md`·`a (2).md.meta.json`, 열면 속성 유지.
5. 반대편에 이미 `a.md`·`a.md.meta.json`이 있을 때 복사 → 충돌 창에서 `건너뛰기`(양쪽 그대로) / `바꾸기`(둘 다 바뀌고 옛 둘은 휴지통, `Ctrl+Z`로 옛 둘 복원) / `둘 다 유지`(`a (2).md.meta.json`).
6. 문서와 사이드카를 함께 골라(`Ctrl` 클릭) 이동·삭제 → 오류·중복 없이 한 항목으로.
7. 사이드카만 골라 삭제 → 사이드카만 휴지통으로(문서 그대로).
8. `Delete` → 둘 다 휴지통, `Ctrl+Z`로 둘 다 복원. `Shift+Delete` → 둘 다 영구 삭제.
9. 이름 변경으로 `a.md` → `b.md` → `b.md.meta.json`도 바뀜, `Ctrl+Z` 한 번에 둘 다 원래 이름. `b.md.meta.json`이 이미 있을 때는 경고 창, 문서만 바뀜.
10. 탐색기에서 `a.md` 하나만 메인 패널로 끌어 오기 → 탐색기 쪽 원본 폴더의 사이드카도 함께 복사·이동됨.
11. 사이드카 파일을 다른 프로그램으로 잠근 상태(예: 쓰기 모드로 열어 둠)에서 이동 → 문서만 이동, 완료 요약에 `블록 속성 파일을 문서와 함께 이동하지 못했습니다` 경고.
12. 폴더 비교 창·zip 안에서는 기존 동작 그대로(사이드카 따라가지 않음 — 위 "적용되지 않는 경로").

## 변경 이력

- 2026-11-01: 설계·구현·단위 테스트. 상태 Ready for Verification.

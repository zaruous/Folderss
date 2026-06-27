# Folderss 아이템 관리

현재 아이템과 상태는 [GitHub Project #1](https://github.com/users/zaruous/projects/1)에서 관리한다.

## 상태

| Status | 기준 |
|---|---|
| Todo | 요청 접수 후 아직 착수하지 않음 |
| In Progress | 현재 분석·설계·구현 중 |
| Ready for Verification | 구현과 자체 검증 완료, 사용자 최종 확인 대기 |
| Done | 사용자가 결과를 명시적으로 확인함 |

## Project item 본문

- 요구사항, 원인 분석 또는 설계, 구현 내용, 변경 파일, 검증, 변경 이력을 item 본문에 직접 기록한다.
- 로컬 상세 Markdown 링크만 기록하는 방식은 사용하지 않는다.
- 상태와 사용자 확인 여부는 본문에 중복 기록하지 않고 Project의 `Status`에서 관리한다.
- `docs/done/DONE.md`는 기존 릴리스 이력이며 현재 아이템 상태 인덱스로 사용하지 않는다.

## 동기화 원칙

- GitHub Project를 상태와 상세 내용의 단일 기준으로 사용하며 로컬 상태 인덱스나 항목별 상세 문서를 병행하지 않는다.
- 상태 변경 시 Project의 `Status`만 변경한다.
- Project 접근이 실패하면 동기화 실패를 보고하고 `docs/items/<항목>.md`에서 임시 관리한다.
- 임시 항목에는 제목, 내용, 상태를 기록하고 상태는 Project와 동일한 네 값을 사용한다.
- 연결이 복구되면 Project에 생성 또는 병합한 뒤 임시 파일을 제거한다.
- `docs/items/`는 연결 장애 시에만 사용하며 Project와 동시에 기준으로 사용하지 않는다.

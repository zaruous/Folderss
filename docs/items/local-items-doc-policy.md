# 로컬 개발 아이템 관리 문서화

- 상태: Ready for Verification

## 요구사항

`AGENTS.md`에서 기존 GitHub Project 연결 기준을 제거하고, `docs/items/` 폴더를 기준으로 로컬 환경에서 개발 아이템의 내용과 상태만 관리하도록 수정한다.
추가 요청에 따라 `docs/architecture.md`도 최신화한다.

## 원인 분석 또는 설계

기존 가이드는 GitHub Project를 단일 기준으로 지정하고 `docs/items/`를 연결 실패 시 임시 저장소로만 설명했다.
사용자 요청에 맞춰 `docs/items/<항목>.md`를 단일 기준으로 삼고, GitHub 연결·동기화·임시 저장소 개념을 제거하는 문서 구조로 정리했다.

## 구현 내용

- `AGENTS.md`의 작업 전 확인, 작업 후 수행, 상태 기록 원칙, 문서 구조, 개발 요청 처리 워크플로를 로컬 항목 파일 기준으로 변경했다.
- 누락되어 있던 `docs/PROJECT.md`를 생성해 로컬 아이템 상태값, 필수 형식, 운용 원칙을 정의했다.
- `docs/architecture.md`의 디렉터리 구조와 서비스 역할 상세에 개발 아이템 문서 관리 항목을 추가했다.
- 기존 완료 이력과 기존 항목 파일의 과거 GitHub 임시 기록 문구를 현재 로컬 기준에 맞게 정정했다.

## 변경 파일

- `AGENTS.md`
- `docs/PROJECT.md`
- `docs/architecture.md`
- `docs/done/DONE.md`
- `docs/items/layout-restore-console-pane.md`
- `docs/items/markdown-panel-ctrl-f-search.md`
- `docs/items/local-items-doc-policy.md`

## 검증

- `rg "GitHub Project|Project #1|동기화|임시|연결 장애|PROJECT.md" AGENTS.md docs\PROJECT.md docs\architecture.md docs\items docs\done\DONE.md`
- 남은 `GitHub Project` 문구는 정책상 금지 문구가 아니라 `GitHub Project 연결이나 동기화는 개발 아이템 관리에 사용하지 않는다`는 명시적 제외 설명이다.
- 코드 변경이 없는 문서 작업이므로 빌드는 수행하지 않았다.

## 변경 이력

- 2026-06-28: GitHub Project 중심 관리 문구를 로컬 `docs/items/` 중심 관리 문구로 변경하고 `docs/architecture.md`를 최신화했다.

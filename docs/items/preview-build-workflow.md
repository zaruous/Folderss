# GitHub Actions 프리뷰 빌드

- 상태: Ready for Verification

## 요구사항

- GitHub Actions로 프리뷰(미리 보기) 실행 파일을 만든다. (원문: "깃 액션으로 프리뷰 파일작업해줘")
- 해석(가정): PR·수동 실행 시 Windows에서 빌드한 실행 파일을 **Actions 아티팩트**로 올린다. GitHub Release·태그는 만들지 않는다
  (정식 배포는 기존 `release.yml`이 태그 푸시로 담당).

## 원인 분석 또는 설계

- 트리거: `pull_request`(대상 `master`, `**.md`·`docs/**`만 바뀌면 제외) + `workflow_dispatch`. 같은 PR의 이전 실행은 `concurrency`로 취소.
- 권한: `contents: read`만. PR 댓글을 달지 않는 이유 — 포크 PR에서는 토큰이 읽기 전용이라 실패하고, 쓰기 권한을 여는 것은 위험. 대신 Job Summary에 아티팩트 링크.
- 버전: `AssemblyInfo.cs`의 앞 세 자리 + `github.run_number` (예 `1.7.0.42`). `UpdateService.IsNewer`는 `v1.7.1` 등 다음 태그를 여전히 더 크게 보므로 업데이트 확인과 충돌하지 않는다.
  같은 세 자리의 정식 태그 `v1.7.0`(=1.7.0)은 프리뷰 1.7.0.42보다 작게 판정되어 업데이트로 뜨지 않는다 — 프리뷰는 해당 버전 이후 개발분이므로 의도대로다.
- 아티팩트: `Folderss-preview-<pr번호|브랜치>-<짧은 커밋>`, 14일 보관, 폴더 안 `PREVIEW.txt`(버전·커밋·출처·실행 링크·빌드 시각).
- 테스트: 별도 `test` job. 이 테스트들은 그동안 Linux에서만 돌려 봤으므로 Windows에서 실패하더라도 프리뷰는 받을 수 있게 분리. 실패 시 trx 결과 업로드.
- 스크립트 주입: 브랜치 이름·URL은 `${{ }}`로 스크립트에 직접 넣지 않고 `env:`로 전달.

## 구현 내용

- `.github/workflows/preview.yml` 추가 (`build`, `test` job).
- `README.md` 빌드 > 프리뷰 빌드 절 추가.

## 변경 파일

- 신규: `.github/workflows/preview.yml`, `docs/items/preview-build-workflow.md`
- 수정: `README.md`

## 검증

- 로컬: YAML 구문 확인(파이썬 yaml 파서).
- GitHub 첫 실행(PR #29, run 36652463791, 커밋 b07e5f5): `build` 성공(약 1분) — 아티팩트 `Folderss-preview-pr29-b07e5f5`
  (약 66MB, 14일 보관) 업로드 확인. `test` 성공 — Windows 러너(git 2.55.0.windows.5)에서 68개 중 통과 66·건너뜀 2
  (기존 권한 테스트, 셸 스크립트 도구를 쓰는 difftool 테스트는 Windows에서 의도적으로 건너뜀). 이 테스트들이 Windows에서 돈 첫 기록.
- 경고: `actions/checkout@v4`, `actions/setup-dotnet@v4`가 Node.js 20 대상이라 러너가 Node 24로 강제 실행한다는 경고(동작에는 영향 없음, release.yml도 동일).
- 미확인: 받은 zip을 Windows에서 풀어 실행해 보는 것(사용자 확인 필요).

## 변경 이력

- 2026-09-30: 요청 접수, preview.yml 추가.
- 2026-09-30: 첫 실행 성공(build·test), 결과 기록. 상태 Ready for Verification.

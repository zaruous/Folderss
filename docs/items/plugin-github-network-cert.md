# 플러그인 GitHub 설치에 네트워크 설정의 추가 신뢰 인증서 적용

- 상태: Ready for Verification

## 요구사항

설정 > 네트워크에 사내(SSL 검사 프록시) 인증서를 추가했는데 `GitHub에서 설치…`가 api.github.com:443 연결에 실패한다.

## 원인 분석

- `PluginGitHubSource.CreateClient`의 `HttpClientHandler`에 `ServerCertificateCustomValidationCallback`이 없었다.
  네트워크 설정(`NetworkSettingsService`)은 마크다운 뷰어·블록 편집기 원격 이미지에만 연결되어 있어, 추가한 인증서가 플러그인 받기에는 전혀 쓰이지 않았다.
  Windows 저장소에 그 루트가 없으면 TLS 검증 실패 → "GitHub에 연결하지 못했습니다" 오류.

## 결정

- 추가 신뢰 인증서(체인 + 호스트 이름 확인)만 적용한다. "인증서 오류가 있는 사이트 허용"은 플러그인 받기에 적용하지 않는다.
  실행할 코드(zip)와 그 SHA-256이 모두 같은 TLS 연결로 오므로, 검증을 끄면 중간자가 둘을 함께 바꿀 수 있어 해시 검사가 무의미해진다.
- 요청마다 `NetworkSettingsService.Current`를 읽으므로 설정 저장 후 재시작 없이 적용된다.

## 한계·주의

- 받기는 `github.com` → `release-assets.githubusercontent.com`(또는 `objects.githubusercontent.com`)로 리다이렉트된다. 추가한 인증서가 **루트(발급자) 인증서**여야
  모든 호스트에 통한다. 브라우저에서 api.github.com의 서버(말단) 인증서만 내보내 추가했다면 체인 검증이 안 되거나 다른 호스트에서 실패할 수 있다.
- 실패 원인이 인증서가 아니라 프록시 인증·방화벽 차단이면 이 수정으로 해결되지 않는다(오류 문구의 원래 메시지 확인).

## 변경 파일

- `Folderss/Services/PluginGitHubSource.cs` — 인증서 콜백, `ShouldAllowCertificateError`
- `Folderss/Services/NetworkSettingsService.cs` (주석), `Folderss/SettingsWindow.xaml` (안내 문구)
- `tests/Folderss.SearchTests/NetworkSettingsTests.cs` — 플러그인 판정 3건
- `README.md`, `docs/architecture.md`

## 검증

- `dotnet test tests/Folderss.SearchTests --filter "FullyQualifiedName~NetworkSettingsTests|FullyQualifiedName~PluginGitHubSourceTests"` (Linux): 89건 통과.
  새 테스트: 내부 루트로 발급된 api.github.com 인증서 허용, 신뢰 인증서 없음 거부, "오류 허용"만 켠 경우 거부.
- 확인하지 못한 것: 콜백이 핸들러에 연결되는 부분은 실제 TLS 서버 없이 단위 테스트하지 않았다. Windows 빌드·회사 프록시 환경 실측 필요.

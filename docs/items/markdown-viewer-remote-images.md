# 마크다운 뷰어 원격 이미지 표시와 네트워크 설정

- 상태: Ready for Verification

## 요구사항

마크다운 문서의 원격 이미지(`![](https://...)`, `<img src="http://...">`)를 볼 수 있어야 한다. 사용자 결정(2026-10-16):

1. 기본은 **https 원격 이미지 표시**(설정에서 끌 수 있음).
2. 설정에 **http 이미지 허용** 옵션(기본 꺼짐).
3. 설정에 **안전하지 않은 사이트(인증서 오류) 허용** 옵션(기본 꺼짐).
4. **localhost 같은 이 PC 안의 접속은 항상 허용**(http·자체 서명 인증서 포함).
5. 인증서는 **OS(Windows) 인증서 저장소**를 기본으로 쓰고, 앱의 네트워크 설정에서 **내부 인증서를 추가**해 신뢰할 수 있게 한다.

## 원인 분석 또는 설계

### 변경 전 동작 (2026-10-16 앱에서 확인)

- 페이지 CSP는 원격 이미지를 허용했지만, `MarkdownViewer.OnWebResourceRequested`가 모든 요청을 받아 내부 주소가 아니면 403으로 막았다.
  PC에서는 접속되는 https 이미지(curl 200/301)도 뷰어에서는 모두 깨졌다.

### 시나리오별 영향 (결정 근거)

| 시나리오 | 변경 전 | 변경 후(기본) |
|---|---|---|
| ① GitHub README 배지·스크린샷(https) | 깨짐 | 표시 |
| ② 로그인이 필요한 사내 위키 첨부 이미지 | 깨짐 | 대부분 깨짐(뷰어는 브라우저 로그인 정보를 쓰지 않음) |
| ③ `http://` 이미지 | 깨짐 | 깨짐(설정에서 허용 시 표시) |
| ④ 사외망 차단 환경 | 깨짐 | 깨짐 |
| ⑤ 받은 문서에 숨은 추적 이미지 | 요청 안 나감 | 요청이 나감(웹 브라우저·GitHub 미리보기와 같은 수준). 원격 이미지 표시를 끄면 막을 수 있음 |

### 설계

- 판정은 순수 로직(`RemoteContentPolicy`)으로 분리해 단위 테스트한다.
  - `RouteImage`: https → 표시 설정이 켜져 있거나 localhost면 `Direct`(WebView2가 직접 받음), http → localhost이거나 (표시 + http 허용)이면 `Proxy`, 그 밖은 `Block`.
  - `IsLoopbackHost`: `localhost`, `*.localhost`, `127.0.0.0/8`, `::1`.
  - `ShouldAllowCertificateError`: localhost이거나 "인증서 오류 허용"이면 허용, 아니면 추가한 내부 인증서를 신뢰 기준(`X509ChainTrustMode.CustomRootTrust`)으로 체인이 이어지고 호스트 이름이 맞을 때만 허용. 폐기 확인은 하지 않는다(사내망에서 대개 접근 불가).
- http 이미지: 뷰어 페이지가 https라 http 이미지는 브라우저가 혼합 콘텐츠로 막거나 https로 바꿔 시도한다.
  JS가 http 이미지 주소를 `https://folderss-doc-asset/remote?u=<원래 주소>`로 바꾸고, C#이 허용 여부를 판정한 뒤 `HttpClient`로 대신 받아 전달한다
  (이미지 `image/*`만, 20MB까지, 15초 제한, Windows 프록시 설정·현재 사용자 자격 증명 사용).
- 인증서: WebView2와 `HttpClient` 모두 Windows 인증서 저장소로 먼저 검증한다(회사가 배포한 루트 인증서 자동 신뢰).
  실패하면 WebView2의 `ServerCertificateErrorDetected`(하위 리소스에도 발생)와 `HttpClient`의 인증서 검증 콜백에서 `ShouldAllowCertificateError`로 다시 판정한다.
  추가한 인증서는 Windows에 설치하지 않고 Folderss 설정 파일(PEM)에만 둔다.
- WebView2는 허용(`AlwaysAllow`)한 인증서 결정을 실행 중 기억하므로, 네트워크 설정을 저장하면(`NetworkSettingsService.Changed`) `ClearServerCertificateErrorActionsAsync`로 지운다.
- 스크립트·스타일·연결 등 이미지가 아닌 외부 요청은 이전처럼 계속 막는다.

## 구현 내용

- `Services/NetworkSettingsService.cs`(신규): `NetworkSettings`(ShowRemoteImages=true, AllowHttpImages, AllowInvalidCertificates, TrustedCertificates),
  `network-settings.xml` 저장·읽기(`SettingsFile.Write`), `Current`·`Changed`, 인증서 파일 읽기(DER/PEM → PEM), 목록 요약·중복 확인.
- `Services/RemoteContentPolicy.cs`(신규): 위 판정 로직.
- `MarkdownViewer.xaml.cs`
  - `OnWebResourceRequested`: `folderss-doc-asset/remote` → `ProxyRemoteImage`(지연 응답 `GetDeferral`), 이미지 요청이 `Direct`면 통과, 나머지 외부 요청 403.
  - `ProxyRemoteImage`, `CopyWithLimitAsync`, 공유 `HttpClient`(인증서 검증 콜백).
  - `OnServerCertificateErrorDetected`, `OnNetworkSettingsChanged`(이벤트 구독·해제).
  - `InlineDocAssets`: HTML 내보내기에서 `folderss-doc-asset/remote` 주소를 원래 http 주소로 되돌린다.
- `markdown-app.js` `resolveImageSrc`: `http:` 이미지를 `folderss-doc-asset/remote?u=...`로 바꾼다(본문 HTML `<img>`도 같은 함수 사용).
- `SettingsWindow`: "네트워크" 탭 추가 — 원격 이미지 표시(https), http 이미지도 표시(원격 이미지가 켜져 있을 때만 선택 가능),
  인증서 오류 사이트 허용, localhost·Windows 인증서 저장소 안내, 추가 신뢰 인증서 목록(이름·만료일·지문)과 추가…/삭제. 저장은 `TrySave(... "network-settings.xml")`.
- 테스트: `tests/Folderss.SearchTests/NetworkSettingsTests.cs`(신규), 테스트 프로젝트에 두 서비스 소스 링크.

## 변경 파일

- `Folderss/Services/NetworkSettingsService.cs` (신규)
- `Folderss/Services/RemoteContentPolicy.cs` (신규)
- `Folderss/Viewers/MarkdownViewer.xaml.cs`
- `Folderss/Viewers/Resources/markdown-app.js`
- `Folderss/SettingsWindow.xaml`, `Folderss/SettingsWindow.xaml.cs`
- `tests/Folderss.SearchTests/NetworkSettingsTests.cs` (신규), `tests/Folderss.SearchTests/Folderss.SearchTests.csproj`
- `README.md`, `docs/architecture.md`
- `docs/items/markdown-viewer-remote-images.md`

## 검증

- `dotnet build .\Folderss.sln -c Debug` — 오류 0개.
- 단위 테스트 `dotnet test tests/Folderss.SearchTests --filter NetworkSettingsTests` — 30개 통과
  (경로 판정 기본값·http 허용·원격 끔, localhost 판정 11가지, 코드로 만든 테스트 루트 CA·서버 인증서로 체인 신뢰·호스트 불일치·무관한 루트·오류 허용·localhost,
  설정 왕복, DER/PEM 인증서 파일 읽기, 인증서가 아닌 파일 거부, 지문 중복 확인).
- 앱 종단 확인(로컬 http 서버 `127.0.0.1:8766`, 테스트 루트 CA가 발급한 `localtest.me` 인증서의 https 서버 `127.0.0.1:8767`.
  `localtest.me`는 `127.0.0.1`로 풀리는 공용 이름이라 "외부 주소"로 취급됨. 설정 파일을 바꿔 앱 재시작 후 확인용 문서의 이미지 표시 여부를 DevTools 프로토콜로 확인):

| 이미지 | 1. 기본 | 2. http 허용 + 내부 루트 추가 | 3. 인증서 오류 허용 | 4. 원격 이미지 끔 |
|---|---|---|---|---|
| A 외부 https(GitHub) | 보임 | 보임 | 보임 | 안 보임 |
| B http 127.0.0.1 | 보임 | 보임 | 보임 | 보임(localhost) |
| C http localtest.me | 안 보임 | 보임 | 안 보임 | 안 보임 |
| D https localtest.me(내부 CA) | 안 보임 | 보임 | 보임 | 안 보임 |
| E https localhost(이름 불일치·미신뢰) | 보임 | 보임 | 보임 | 보임(localhost) |
| F 본문 HTML `<img>` http localtest.me | 안 보임 | 보임 | 안 보임 | 안 보임 |

- 설정 창(UI 자동화로 조작): 네트워크 탭이 저장된 값을 불러옴, 원격 이미지를 켜면 http 체크가 활성화, 추가…로 `.cer` 선택 시 목록에
  `Folderss Test Internal Root | 2027-10-16 | F09406E61E6C31C0` 표시, 저장 시 `network-settings.xml`에 반영. 원격 이미지를 끈 상태로 시작한 앱에서 설정 저장 후
  다시 그리면 앱 재시작 없이 새 설정으로 이미지가 표시됨.
- 확인 후 정리: 테스트 서버 종료, 테스트용 `network-settings.xml` 삭제(기본값으로), 디버그 포트 없이 앱 재시작.
- 알려진 제약
  - 로그인이 필요한 이미지(사내 위키 첨부 등)는 표시되지 않는다.
  - 추가한 인증서는 루트(또는 체인 끝까지 이어지는 인증서)여야 한다. 중간 인증서만 추가하면 체인을 끝까지 만들 수 없어 신뢰되지 않는다.
  - 내보낸 HTML 파일의 원격 이미지는 원래 주소로 남아, 그 파일을 여는 브라우저가 직접 받는다.

## 변경 이력

- 2026-10-16: 현재 동작 확인, 시나리오·선택지 정리. 사용자 결정 대기.
- 2026-10-16: 사용자 결정(https 기본 표시 + http·인증서 오류 허용 옵션 + localhost 항상 허용 + OS 인증서 기본 + 내부 인증서 추가)에 따라 구현.
  단위 테스트 30개, 앱 종단 4개 시나리오, 설정 창 조작 확인. 앱 사용 확인 대기.

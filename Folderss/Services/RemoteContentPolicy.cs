using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace Folderss.Services
{
    /// <summary>원격 이미지를 어떻게 불러올지.</summary>
    public enum RemoteImageRoute
    {
        /// <summary>막는다.</summary>
        Block,
        /// <summary>WebView2가 직접 받는다(https). 인증서는 Windows 저장소로 검증하고, 오류는 <see cref="RemoteContentPolicy.ShouldAllowCertificateError"/>로 판정.</summary>
        Direct,
        /// <summary>Folderss(C#)가 대신 받아 전달한다(http). 뷰어 페이지가 https라 http 이미지는 브라우저가 혼합 콘텐츠로 막기 때문.</summary>
        Proxy
    }

    /// <summary>
    /// 뷰어의 외부 접속 판정. 순수 로직이라 단위 테스트한다.
    /// localhost·127.0.0.0/8·::1 같은 이 PC 안의 주소는 설정과 관계없이 http·자체 서명 인증서를 허용한다(개발 서버 등).
    /// </summary>
    public static class RemoteContentPolicy
    {
        public static bool IsLoopbackHost(string host)
        {
            if (string.IsNullOrEmpty(host))
                return false;

            host = host.Trim('[', ']');
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
                return true;

            IPAddress address;
            return IPAddress.TryParse(host, out address) && IPAddress.IsLoopback(address);
        }

        public static RemoteImageRoute RouteImage(Uri uri, NetworkSettings settings)
        {
            if (uri == null || !uri.IsAbsoluteUri || settings == null)
                return RemoteImageRoute.Block;

            var loopback = IsLoopbackHost(uri.Host);
            if (uri.Scheme == Uri.UriSchemeHttps)
                return loopback || settings.ShowRemoteImages ? RemoteImageRoute.Direct : RemoteImageRoute.Block;
            if (uri.Scheme == Uri.UriSchemeHttp)
                return loopback || (settings.ShowRemoteImages && settings.AllowHttpImages) ? RemoteImageRoute.Proxy : RemoteImageRoute.Block;
            return RemoteImageRoute.Block;
        }

        /// <summary>
        /// Windows 저장소로 검증에 실패한 서버 인증서를 그래도 허용할지.
        /// 이 PC 안의 주소이거나, "인증서 오류 허용"이 켜져 있거나, 추가한 내부 인증서로 체인이 이어지고 호스트 이름이 맞으면 허용한다.
        /// </summary>
        /// <param name="serverCertificate">서버가 보낸 인증서.</param>
        /// <param name="presentedChain">서버가 함께 보낸 중간 인증서들(없으면 빈 목록).</param>
        public static bool ShouldAllowCertificateError(Uri uri, X509Certificate2 serverCertificate,
            IEnumerable<X509Certificate2> presentedChain, NetworkSettings settings)
        {
            if (uri == null || settings == null)
                return false;
            if (IsLoopbackHost(uri.Host) || settings.AllowInvalidCertificates)
                return true;
            if (serverCertificate == null || settings.TrustedCertificates == null || settings.TrustedCertificates.Count == 0)
                return false;

            return ChainsToTrustedCertificate(serverCertificate, presentedChain, settings.TrustedCertificates) &&
                   serverCertificate.MatchesHostname(uri.Host);
        }

        /// <summary>추가한 내부 인증서(루트 또는 중간)를 신뢰 기준으로 삼아 체인을 만들 수 있는지. 폐기 확인은 하지 않는다(사내망에서 대개 접근 불가).</summary>
        public static bool ChainsToTrustedCertificate(X509Certificate2 serverCertificate,
            IEnumerable<X509Certificate2> presentedChain, IEnumerable<string> trustedPems)
        {
            var trusted = new List<X509Certificate2>();
            try
            {
                foreach (var pem in trustedPems)
                {
                    try { trusted.Add(X509Certificate2.CreateFromPem(pem)); }
                    catch (Exception) { /* 읽을 수 없는 항목은 건너뛴다 */ }
                }
                if (trusted.Count == 0)
                    return false;

                using (var chain = new X509Chain())
                {
                    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    foreach (var cert in trusted)
                    {
                        chain.ChainPolicy.CustomTrustStore.Add(cert);
                        // 중간 인증서만 추가한 경우에도 체인을 이을 수 있게 보조 저장소에도 넣는다.
                        chain.ChainPolicy.ExtraStore.Add(cert);
                    }
                    if (presentedChain != null)
                    {
                        foreach (var cert in presentedChain)
                            chain.ChainPolicy.ExtraStore.Add(cert);
                    }
                    return chain.Build(serverCertificate);
                }
            }
            finally
            {
                foreach (var cert in trusted)
                    cert.Dispose();
            }
        }
    }
}

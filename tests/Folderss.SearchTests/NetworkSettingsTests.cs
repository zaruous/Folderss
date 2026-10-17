using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Folderss.Services;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>네트워크 설정: 원격 이미지 경로 판정, localhost 예외, 내부 인증서로 인증서 오류 허용, 설정 저장·읽기 왕복.</summary>
    public sealed class NetworkSettingsTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "folderss-network-settings-" + Guid.NewGuid().ToString("N"));
        private readonly X509Certificate2 _root;
        private readonly X509Certificate2 _leaf;
        private readonly X509Certificate2 _otherRoot;

        public NetworkSettingsTests()
        {
            Directory.CreateDirectory(_dir);
            _root = CreateRoot("CN=Folderss Test Root");
            _otherRoot = CreateRoot("CN=Unrelated Root");
            _leaf = CreateLeaf(_root, "intranet.example");
        }

        public void Dispose()
        {
            _leaf.Dispose();
            _root.Dispose();
            _otherRoot.Dispose();
            Directory.Delete(_dir, true);
        }

        // ── 원격 이미지 경로 ──────────────────────────────────

        [Theory]
        [InlineData("https://img.shields.io/badge/a.png", RemoteImageRoute.Direct)]   // 기본: https 표시
        [InlineData("http://example.com/a.png", RemoteImageRoute.Block)]             // 기본: http 막음
        [InlineData("http://localhost:3000/a.png", RemoteImageRoute.Proxy)]          // 이 PC 안의 주소는 http도 허용
        [InlineData("http://127.0.0.1/a.png", RemoteImageRoute.Proxy)]
        [InlineData("https://localhost:5001/a.png", RemoteImageRoute.Direct)]
        [InlineData("ftp://example.com/a.png", RemoteImageRoute.Block)]
        public void RouteImage_Defaults(string url, RemoteImageRoute expected)
        {
            Assert.Equal(expected, RemoteContentPolicy.RouteImage(new Uri(url), new NetworkSettings()));
        }

        [Fact]
        public void RouteImage_HttpAllowed_GoesThroughProxy()
        {
            var settings = new NetworkSettings { AllowHttpImages = true };
            Assert.Equal(RemoteImageRoute.Proxy, RemoteContentPolicy.RouteImage(new Uri("http://example.com/a.png"), settings));
        }

        [Fact]
        public void RouteImage_RemoteImagesOff_BlocksExceptLoopback()
        {
            var settings = new NetworkSettings { ShowRemoteImages = false, AllowHttpImages = true };
            Assert.Equal(RemoteImageRoute.Block, RemoteContentPolicy.RouteImage(new Uri("https://example.com/a.png"), settings));
            Assert.Equal(RemoteImageRoute.Block, RemoteContentPolicy.RouteImage(new Uri("http://example.com/a.png"), settings));
            Assert.Equal(RemoteImageRoute.Direct, RemoteContentPolicy.RouteImage(new Uri("https://localhost/a.png"), settings));
            Assert.Equal(RemoteImageRoute.Proxy, RemoteContentPolicy.RouteImage(new Uri("http://localhost/a.png"), settings));
        }

        [Theory]
        [InlineData("localhost", true)]
        [InlineData("LOCALHOST", true)]
        [InlineData("app.localhost", true)]
        [InlineData("127.0.0.1", true)]
        [InlineData("127.5.5.5", true)]
        [InlineData("[::1]", true)]
        [InlineData("::1", true)]
        [InlineData("example.com", false)]
        [InlineData("10.0.0.1", false)]
        [InlineData("localhost.example.com", false)]
        [InlineData("", false)]
        public void IsLoopbackHost(string host, bool expected)
        {
            Assert.Equal(expected, RemoteContentPolicy.IsLoopbackHost(host));
        }

        // ── 인증서 오류 허용 ──────────────────────────────────

        [Fact]
        public void CertificateError_Default_IsNotAllowed()
        {
            Assert.False(RemoteContentPolicy.ShouldAllowCertificateError(
                new Uri("https://intranet.example/a.png"), _leaf, Array.Empty<X509Certificate2>(), new NetworkSettings()));
        }

        [Fact]
        public void CertificateError_TrustedInternalRoot_IsAllowed()
        {
            var settings = new NetworkSettings();
            settings.TrustedCertificates.Add(_root.ExportCertificatePem());
            Assert.True(RemoteContentPolicy.ShouldAllowCertificateError(
                new Uri("https://intranet.example/a.png"), _leaf, Array.Empty<X509Certificate2>(), settings));
        }

        [Fact]
        public void CertificateError_TrustedRootButWrongHost_IsNotAllowed()
        {
            var settings = new NetworkSettings();
            settings.TrustedCertificates.Add(_root.ExportCertificatePem());
            Assert.False(RemoteContentPolicy.ShouldAllowCertificateError(
                new Uri("https://other.example/a.png"), _leaf, Array.Empty<X509Certificate2>(), settings));
        }

        [Fact]
        public void CertificateError_UnrelatedRoot_IsNotAllowed()
        {
            var settings = new NetworkSettings();
            settings.TrustedCertificates.Add(_otherRoot.ExportCertificatePem());
            Assert.False(RemoteContentPolicy.ShouldAllowCertificateError(
                new Uri("https://intranet.example/a.png"), _leaf, Array.Empty<X509Certificate2>(), settings));
        }

        [Fact]
        public void CertificateError_AllowInvalidOption_IsAllowed()
        {
            Assert.True(RemoteContentPolicy.ShouldAllowCertificateError(
                new Uri("https://other.example/a.png"), _leaf, Array.Empty<X509Certificate2>(),
                new NetworkSettings { AllowInvalidCertificates = true }));
        }

        [Fact]
        public void CertificateError_Loopback_IsAlwaysAllowed()
        {
            Assert.True(RemoteContentPolicy.ShouldAllowCertificateError(
                new Uri("https://localhost:5001/a.png"), _leaf, Array.Empty<X509Certificate2>(), new NetworkSettings()));
        }

        // ── 설정 파일·인증서 파일 ──────────────────────────────

        [Fact]
        public void Load_MissingFile_ReturnsDefaults()
        {
            var settings = NetworkSettingsService.Load(Path.Combine(_dir, "none.xml"));
            Assert.True(settings.ShowRemoteImages);
            Assert.False(settings.AllowHttpImages);
            Assert.False(settings.AllowInvalidCertificates);
            Assert.Empty(settings.TrustedCertificates);
        }

        [Fact]
        public void SaveAndLoad_RoundTrip()
        {
            var path = Path.Combine(_dir, "network-settings.xml");
            var saved = new NetworkSettings { ShowRemoteImages = false, AllowHttpImages = true, AllowInvalidCertificates = true };
            saved.TrustedCertificates.Add(_root.ExportCertificatePem());
            NetworkSettingsService.Save(saved, path);

            var loaded = NetworkSettingsService.Load(path);
            Assert.False(loaded.ShowRemoteImages);
            Assert.True(loaded.AllowHttpImages);
            Assert.True(loaded.AllowInvalidCertificates);
            Assert.Single(loaded.TrustedCertificates);
            Assert.Equal(_root.Thumbprint, NetworkSettingsService.Describe(loaded.TrustedCertificates[0]).Thumbprint);
        }

        [Fact]
        public void ReadCertificateFile_DerAndPem()
        {
            var der = Path.Combine(_dir, "root.cer");
            var pem = Path.Combine(_dir, "root.pem");
            File.WriteAllBytes(der, _root.Export(X509ContentType.Cert));
            File.WriteAllText(pem, _root.ExportCertificatePem());

            foreach (var path in new[] { der, pem })
            {
                var read = NetworkSettingsService.ReadCertificateFileAsPem(path);
                Assert.Contains("-----BEGIN CERTIFICATE-----", read);
                Assert.Equal(_root.Thumbprint, NetworkSettingsService.Describe(read).Thumbprint);
            }
        }

        [Fact]
        public void ReadCertificateFile_NotACertificate_Throws()
        {
            var path = Path.Combine(_dir, "notes.txt");
            File.WriteAllText(path, "hello");
            Assert.Throws<InvalidOperationException>(() => NetworkSettingsService.ReadCertificateFileAsPem(path));
        }

        [Fact]
        public void ContainsCertificate_ComparesByThumbprint()
        {
            var list = new[] { _root.ExportCertificatePem() };
            Assert.True(NetworkSettingsService.ContainsCertificate(list, _root.ExportCertificatePem()));
            Assert.False(NetworkSettingsService.ContainsCertificate(list, _otherRoot.ExportCertificatePem()));
        }

        // ── 테스트용 인증서 ──────────────────────────────────

        private static X509Certificate2 CreateRoot(string subject)
        {
            using (var key = RSA.Create(2048))
            {
                var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
                return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
            }
        }

        private static X509Certificate2 CreateLeaf(X509Certificate2 issuer, string host)
        {
            using (var key = RSA.Create(2048))
            {
                var request = new CertificateRequest("CN=" + host, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
                var san = new SubjectAlternativeNameBuilder();
                san.AddDnsName(host);
                request.CertificateExtensions.Add(san.Build());
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                    new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
                var serial = new byte[8];
                RandomNumberGenerator.Fill(serial);
                return request.Create(issuer, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddMonths(6), serial);
            }
        }
    }
}

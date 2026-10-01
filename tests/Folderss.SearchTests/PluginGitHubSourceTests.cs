using Folderss.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>GitHub 릴리스에서 플러그인 설치: 주소 해석, 릴리스 응답, 다운로드 제한·해시 검증, 설치 출처 기록, 계약 버전 확인.</summary>
    public sealed class PluginGitHubSourceTests : IDisposable
    {
        private readonly string _root;

        public PluginGitHubSourceTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "folderss-plugin-gh-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
            public readonly List<Uri> Requests = new List<Uri>();

            public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) { _respond = respond; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(request.RequestUri);
                return Task.FromResult(_respond(request));
            }
        }

        private static HttpResponseMessage Json(string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }

        private static string Sha256(byte[] data)
        {
            using (var sha = SHA256.Create())
                return "sha256:" + Convert.ToHexString(sha.ComputeHash(data)).ToLowerInvariant();
        }

        private const string ReleaseJson = @"{
  ""tag_name"": ""v1.2.0"",
  ""html_url"": ""https://github.com/zaruous/Folderss-db-helper/releases/tag/v1.2.0"",
  ""assets"": [
    { ""name"": ""zaruous.folderss-db-helper-1.2.0.zip"", ""size"": 3, ""digest"": ""sha256:abc"",
      ""browser_download_url"": ""https://github.com/zaruous/Folderss-db-helper/releases/download/v1.2.0/a.zip"" },
    { ""name"": ""notes.txt"", ""size"": 1, ""browser_download_url"": ""https://github.com/x/y/releases/download/v1/notes.txt"" }
  ]
}";

        [Theory]
        [InlineData("https://github.com/zaruous/Folderss-db-helper")]
        [InlineData("https://github.com/zaruous/Folderss-db-helper/")]
        [InlineData("https://github.com/zaruous/Folderss-db-helper/releases")]
        [InlineData("https://github.com/zaruous/Folderss-db-helper/releases/latest")]
        [InlineData("https://github.com/zaruous/Folderss-db-helper.git")]
        [InlineData("  https://www.github.com/zaruous/Folderss-db-helper  ")]
        public void ParseRepository_AcceptsRepositoryAndReleaseUrls(string url)
        {
            var (owner, repo) = PluginGitHubSource.ParseRepository(url);
            Assert.Equal("zaruous", owner);
            Assert.Equal("Folderss-db-helper", repo);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("github.com/zaruous/Folderss-db-helper")]
        [InlineData("http://github.com/zaruous/Folderss-db-helper")]
        [InlineData("https://gitlab.com/zaruous/Folderss-db-helper")]
        [InlineData("https://github.com.evil.example/zaruous/Folderss-db-helper")]
        [InlineData("https://evil.example/github.com/zaruous/Folderss-db-helper")]
        [InlineData("https://github.com/zaruous")]
        [InlineData("https://github.com/zaruous/..")]
        public void ParseRepository_RejectsOtherUrls(string url)
        {
            Assert.Throws<ArgumentException>(() => PluginGitHubSource.ParseRepository(url));
        }

        [Fact]
        public void ParseRelease_ReadsTagAssetsAndDigest_ZipAssetsFiltersZip()
        {
            var release = PluginGitHubSource.ParseRelease(ReleaseJson);

            Assert.Equal("v1.2.0", release.TagName);
            Assert.Equal(2, release.Assets.Count);
            var zip = Assert.Single(release.ZipAssets);
            Assert.Equal("zaruous.folderss-db-helper-1.2.0.zip", zip.Name);
            Assert.Equal(3, zip.Size);
            Assert.Equal("sha256:abc", zip.Digest);
            Assert.Null(release.Assets[1].Digest);
        }

        [Fact]
        public async Task GetLatestRelease_RequestsLatestEndpoint()
        {
            var handler = new FakeHandler(r => Json(ReleaseJson));

            var release = await PluginGitHubSource.GetLatestReleaseAsync(new HttpClient(handler), "zaruous", "Folderss-db-helper", CancellationToken.None);

            Assert.Equal("v1.2.0", release.TagName);
            Assert.Equal("https://api.github.com/repos/zaruous/Folderss-db-helper/releases/latest", Assert.Single(handler.Requests).ToString());
        }

        [Fact]
        public async Task GetLatestRelease_NotFound_ExplainsPrivateOrNoRelease()
        {
            var client = new HttpClient(new FakeHandler(r => new HttpResponseMessage(HttpStatusCode.NotFound)));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => PluginGitHubSource.GetLatestReleaseAsync(client, "a", "b", CancellationToken.None));
            Assert.Contains("정식 릴리스", ex.Message);
            Assert.Contains("비공개", ex.Message);
        }

        [Theory]
        [InlineData(403)]
        [InlineData(429)]
        public async Task GetLatestRelease_RateLimited_SuggestsZipInstall(int status)
        {
            var client = new HttpClient(new FakeHandler(r =>
            {
                var response = new HttpResponseMessage((HttpStatusCode)status);
                response.Headers.Add("X-RateLimit-Remaining", "0");
                response.Headers.Add("X-RateLimit-Reset", "1900000000");
                return response;
            }));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => PluginGitHubSource.GetLatestReleaseAsync(client, "a", "b", CancellationToken.None));
            Assert.Contains("한도", ex.Message);
            Assert.Contains("플러그인 찾기", ex.Message);
        }

        [Fact]
        public async Task GetLatestRelease_NetworkFailure_MentionsProxy()
        {
            var client = new HttpClient(new FakeHandler(r => throw new HttpRequestException("proxy refused")));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => PluginGitHubSource.GetLatestReleaseAsync(client, "a", "b", CancellationToken.None));
            Assert.Contains("프록시", ex.Message);
            Assert.Contains("proxy refused", ex.Message);
        }

        [Fact]
        public async Task Download_WritesFile_WhenDigestMatches()
        {
            var data = new byte[] { 1, 2, 3 };
            var client = new HttpClient(new FakeHandler(r => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) }));
            var asset = new GitHubReleaseAsset { Name = "p.zip", Size = 3, Digest = Sha256(data), DownloadUrl = "https://github.com/a/b/releases/download/v1/p.zip" };
            var destination = Path.Combine(_root, "p.zip");

            await PluginGitHubSource.DownloadAsync(client, asset, destination, CancellationToken.None);

            Assert.Equal(data, File.ReadAllBytes(destination));
        }

        [Fact]
        public async Task Download_DigestMismatch_Throws()
        {
            var client = new HttpClient(new FakeHandler(r => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 9 }) }));
            var asset = new GitHubReleaseAsset { Name = "p.zip", Size = 1, Digest = Sha256(new byte[] { 1 }), DownloadUrl = "https://github.com/a/b/releases/download/v1/p.zip" };

            var ex = await Assert.ThrowsAsync<InvalidDataException>(() => PluginGitHubSource.DownloadAsync(client, asset, Path.Combine(_root, "p.zip"), CancellationToken.None));
            Assert.Contains("SHA-256", ex.Message);
        }

        [Fact]
        public async Task Download_NoDigest_SkipsVerification()
        {
            var client = new HttpClient(new FakeHandler(r => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 9 }) }));
            var asset = new GitHubReleaseAsset { Name = "p.zip", Size = 1, DownloadUrl = "https://github.com/a/b/releases/download/v1/p.zip" };

            await PluginGitHubSource.DownloadAsync(client, asset, Path.Combine(_root, "p.zip"), CancellationToken.None);
        }

        [Theory]
        [InlineData("http://github.com/a/b/releases/download/v1/p.zip")]
        [InlineData("https://evil.example/a/b/releases/download/v1/p.zip")]
        [InlineData(null)]
        public async Task Download_NonGitHubUrl_RejectedWithoutRequest(string url)
        {
            var handler = new FakeHandler(r => new HttpResponseMessage(HttpStatusCode.OK));
            var asset = new GitHubReleaseAsset { Name = "p.zip", Size = 1, DownloadUrl = url };

            await Assert.ThrowsAsync<InvalidOperationException>(() => PluginGitHubSource.DownloadAsync(new HttpClient(handler), asset, Path.Combine(_root, "p.zip"), CancellationToken.None));
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task Download_DeclaredTooLarge_RejectedWithoutRequest()
        {
            var handler = new FakeHandler(r => new HttpResponseMessage(HttpStatusCode.OK));
            var asset = new GitHubReleaseAsset { Name = "p.zip", Size = PluginGitHubSource.MaxDownloadBytes + 1, DownloadUrl = "https://github.com/a/b/releases/download/v1/p.zip" };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => PluginGitHubSource.DownloadAsync(new HttpClient(handler), asset, Path.Combine(_root, "p.zip"), CancellationToken.None));
            Assert.Contains("너무 큽니다", ex.Message);
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task Download_StreamLargerThanLimit_Aborts()
        {
            // 릴리스 정보의 size가 작아도 실제로 받은 양으로 다시 제한한다
            var client = new HttpClient(new FakeHandler(r => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new EndlessStream()) }));
            var asset = new GitHubReleaseAsset { Name = "p.zip", Size = 1, DownloadUrl = "https://github.com/a/b/releases/download/v1/p.zip" };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => PluginGitHubSource.DownloadAsync(client, asset, Path.Combine(_root, "p.zip"), CancellationToken.None));
            Assert.Contains("너무 큽니다", ex.Message);
        }

        private sealed class EndlessStream : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) { return count; }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        [Fact]
        public void SourceStore_SetGetRemove_AndMissingFileIsEmpty()
        {
            Assert.Null(PluginSourceStore.Get(_root, "a.b"));

            PluginSourceStore.Set(_root, "a.b", PluginGitHubSource.SourceKey("zaruous", "Folderss-db-helper"));
            PluginSourceStore.Set(_root, "c.d", PluginSourceStore.LocalFile);

            Assert.Equal("github.com/zaruous/Folderss-db-helper", PluginSourceStore.Get(_root, "A.B"));
            Assert.Equal(PluginSourceStore.LocalFile, PluginSourceStore.Get(_root, "c.d"));

            PluginSourceStore.Set(_root, "a.b", null);
            Assert.Null(PluginSourceStore.Get(_root, "a.b"));
            Assert.Equal(PluginSourceStore.LocalFile, PluginSourceStore.Get(_root, "c.d"));
        }

        [Fact]
        public void SourceStore_Corrupt_ThrowsInsteadOfOverwriting()
        {
            File.WriteAllText(PluginSourceStore.GetPath(_root), "{ broken");

            Assert.Throws<InvalidDataException>(() => PluginSourceStore.Set(_root, "a.b", PluginSourceStore.LocalFile));
            Assert.Equal("{ broken", File.ReadAllText(PluginSourceStore.GetPath(_root)));
        }

        [Fact]
        public void SourceStore_IsSameSource_IgnoresCase()
        {
            Assert.True(PluginSourceStore.IsSameSource("github.com/Zaruous/Repo", "github.com/zaruous/repo"));
            Assert.False(PluginSourceStore.IsSameSource("github.com/zaruous/repo", "github.com/other/repo"));
            Assert.False(PluginSourceStore.IsSameSource(PluginSourceStore.LocalFile, "github.com/zaruous/repo"));
        }

        private string ZipWithAssembly(string assemblyPath)
        {
            var zip = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
                archive.CreateEntryFromFile(assemblyPath, "Hello.dll");
            return zip;
        }

        [Fact]
        public void ReadAssemblyReference_ReadsReferencedVersionWithoutLoading()
        {
            // 이 테스트 어셈블리는 xunit.core를 참조하고 계약 DLL은 참조하지 않는다
            var zip = ZipWithAssembly(typeof(PluginGitHubSourceTests).Assembly.Location);
            var manifest = new PluginManifest { Assembly = "Hello.dll" };

            Assert.Equal(typeof(FactAttribute).Assembly.GetName().Version, PluginPackage.ReadAssemblyReference(zip, manifest, "xunit.core"));
            Assert.Null(PluginPackage.ReadAssemblyReference(zip, manifest, PluginPackage.ContractAssemblyName));
        }

        [Fact]
        public void ReadAssemblyReference_NotDotNet_Throws()
        {
            var file = Path.Combine(_root, "fake.dll");
            File.WriteAllText(file, "not a dll");
            var zip = ZipWithAssembly(file);

            Assert.Throws<InvalidDataException>(() => PluginPackage.ReadAssemblyReference(zip, new PluginManifest { Assembly = "Hello.dll" }, "x"));
        }

        [Theory]
        [InlineData("1.0.0.0", "1.0.0.0", true)]
        [InlineData("1.0.0.0", "2.0.0.0", true)]
        [InlineData(null, "1.0.0.0", true)]
        [InlineData("1.1.0.0", "1.0.0.0", false)]
        public void EnsureContractCompatible_RejectsNewerThanHost(string required, string host, bool ok)
        {
            Action check = () => PluginPackage.EnsureContractCompatible(required == null ? null : Version.Parse(required), Version.Parse(host));
            if (ok)
                check();
            else
                Assert.Contains("업데이트", Assert.Throws<InvalidDataException>(check).Message);
        }
    }
}

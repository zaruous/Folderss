using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Folderss.Services
{
    public sealed class GitHubReleaseAsset
    {
        public string Name { get; set; }
        public string DownloadUrl { get; set; }
        public long Size { get; set; }
        /// <summary>GitHub가 주는 해시(예: <c>sha256:…</c>). 오래된 릴리스는 없을 수 있다.</summary>
        public string Digest { get; set; }
    }

    public sealed class GitHubRelease
    {
        public string TagName { get; set; }
        public string HtmlUrl { get; set; }
        public List<GitHubReleaseAsset> Assets { get; set; } = new List<GitHubReleaseAsset>();

        public List<GitHubReleaseAsset> ZipAssets
        {
            get { return Assets.Where(a => a.Name != null && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList(); }
        }
    }

    /// <summary>
    /// 공개 GitHub 저장소의 최신 정식 릴리스(Pre-release·초안 제외)에서 플러그인 zip을 찾아 받는다.
    /// 로그인하지 않으므로 비공개 저장소는 안 되고, API 호출 한도는 IP당 시간당 60회다.
    /// 릴리스 페이지에 자동으로 붙는 "Source code (zip)"은 API의 첨부 파일 목록에 없어서 고를 일이 없다.
    /// 오류는 사람이 읽을 문장의 예외로 던진다(삼키지 않음 — 회사 프록시·방화벽 문제를 사용자가 알 수 있게).
    /// </summary>
    public static class PluginGitHubSource
    {
        public const long MaxDownloadBytes = 100L * 1024 * 1024;

        private static readonly Regex NamePattern = new Regex(@"^[A-Za-z0-9._-]{1,100}$");
        private static readonly Lazy<HttpClient> Shared = new Lazy<HttpClient>(CreateClient);

        public static HttpClient SharedClient { get { return Shared.Value; } }

        /// <summary>Windows 프록시 설정을 따르고, 인증 프록시에는 현재 사용자 자격 증명을 쓴다.</summary>
        public static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler { DefaultProxyCredentials = CredentialCache.DefaultCredentials };
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(2) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Folderss-PluginInstall");
            return client;
        }

        /// <summary>
        /// <c>https://github.com/&lt;소유자&gt;/&lt;저장소&gt;</c>(뒤에 <c>/releases</c> 등이 붙어도 됨)에서 저장소를 찾는다.
        /// 형식이 틀리면 <see cref="ArgumentException"/>.
        /// </summary>
        public static (string Owner, string Repo) ParseRepository(string url)
        {
            const string expected = "https://github.com/<소유자>/<저장소> 형식의 주소를 입력하세요.";
            Uri uri;
            if (!Uri.TryCreate((url ?? string.Empty).Trim(), UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new ArgumentException(expected);
            var host = uri.Host.ToLowerInvariant();
            if (host != "github.com" && host != "www.github.com")
                throw new ArgumentException("GitHub(github.com) 저장소만 설치할 수 있습니다. " + expected);

            var parts = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                throw new ArgumentException(expected);
            var owner = parts[0];
            var repo = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1].Substring(0, parts[1].Length - 4) : parts[1];
            if (!NamePattern.IsMatch(owner) || !NamePattern.IsMatch(repo) || repo.Trim('.').Length == 0)
                throw new ArgumentException(expected);
            return (owner, repo);
        }

        /// <summary>설치 출처 기록에 쓰는 값.</summary>
        public static string SourceKey(string owner, string repo)
        {
            return "github.com/" + owner + "/" + repo;
        }

        public static GitHubRelease ParseRelease(string json)
        {
            using (var document = JsonDocument.Parse(json))
            {
                var root = document.RootElement;
                var release = new GitHubRelease
                {
                    TagName = GetString(root, "tag_name"),
                    HtmlUrl = GetString(root, "html_url")
                };
                JsonElement assets;
                if (root.TryGetProperty("assets", out assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        JsonElement size;
                        release.Assets.Add(new GitHubReleaseAsset
                        {
                            Name = GetString(asset, "name"),
                            DownloadUrl = GetString(asset, "browser_download_url"),
                            Size = asset.TryGetProperty("size", out size) && size.ValueKind == JsonValueKind.Number ? size.GetInt64() : 0,
                            Digest = GetString(asset, "digest")
                        });
                    }
                }
                return release;
            }
        }

        public static async Task<GitHubRelease> GetLatestReleaseAsync(HttpClient client, string owner, string repo, CancellationToken cancellationToken)
        {
            var url = "https://api.github.com/repos/" + owner + "/" + repo + "/releases/latest";
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                using (var response = await SendAsync(client, request, cancellationToken))
                {
                    if (response.StatusCode == HttpStatusCode.NotFound)
                        throw new InvalidOperationException(owner + "/" + repo + "에서 정식 릴리스를 찾지 못했습니다. " +
                            "저장소 주소, 공개 저장소인지(비공개는 지원하지 않음), 정식 릴리스가 있는지(Pre-release·초안 제외) 확인하세요.");
                    EnsureSuccess(response, "GitHub 릴리스 정보");
                    return ParseRelease(await response.Content.ReadAsStringAsync(cancellationToken));
                }
            }
        }

        /// <summary>첨부 파일을 받아 <paramref name="destination"/>에 쓰고, GitHub가 해시를 주면 비교한다.</summary>
        public static async Task DownloadAsync(HttpClient client, GitHubReleaseAsset asset, string destination, CancellationToken cancellationToken)
        {
            Uri uri;
            if (asset == null || !Uri.TryCreate(asset.DownloadUrl, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps
                || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("GitHub 릴리스 첨부 파일 주소가 아닙니다: " + (asset == null ? null : asset.DownloadUrl));
            if (asset.Size > MaxDownloadBytes)
                throw new InvalidOperationException(TooLarge(asset.Name));

            using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
            using (var response = await SendAsync(client, request, cancellationToken, HttpCompletionOption.ResponseHeadersRead))
            {
                EnsureSuccess(response, "첨부 파일 " + asset.Name);
                if (response.Content.Headers.ContentLength > MaxDownloadBytes)
                    throw new InvalidOperationException(TooLarge(asset.Name));

                using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
                using (var output = File.Create(destination))
                {
                    var buffer = new byte[81920];
                    long total = 0;
                    int read;
                    while ((read = await ReadAsync(input, buffer, cancellationToken)) > 0)
                    {
                        total += read;
                        if (total > MaxDownloadBytes)
                            throw new InvalidOperationException(TooLarge(asset.Name));
                        await output.WriteAsync(buffer, 0, read, cancellationToken);
                    }
                }
            }
            VerifyDigest(destination, asset.Digest);
        }

        /// <summary>
        /// <c>sha256:&lt;hex&gt;</c>이면 파일 해시와 비교해 다르면 <see cref="InvalidDataException"/>. 값이 없거나 모르는 형식이면 검사하지 않는다.
        /// 전송 중 손상·변조는 막지만, 저장소 자체가 탈취된 경우는 막지 못한다(해시도 같은 곳에서 오므로).
        /// </summary>
        public static void VerifyDigest(string path, string digest)
        {
            const string prefix = "sha256:";
            if (string.IsNullOrWhiteSpace(digest) || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return;
            string actual;
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                actual = Convert.ToHexString(sha.ComputeHash(stream));
            if (!string.Equals(actual, digest.Substring(prefix.Length), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("받은 파일의 SHA-256이 GitHub가 알려 준 값과 다릅니다. 내려받는 중 손상되었거나 변조되었을 수 있어 설치하지 않습니다.");
        }

        private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken,
            HttpCompletionOption option = HttpCompletionOption.ResponseContentRead)
        {
            try
            {
                return await client.SendAsync(request, option, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException("GitHub에 연결하지 못했습니다. 인터넷 연결과 프록시·방화벽 설정을 확인하세요.\n" + ex.Message, ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException("GitHub 응답 시간이 초과되었습니다. 잠시 후 다시 시도하세요.", ex);
            }
        }

        private static async Task<int> ReadAsync(Stream input, byte[] buffer, CancellationToken cancellationToken)
        {
            try
            {
                return await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException("내려받는 중 연결이 끊겼습니다. 다시 시도하세요.\n" + ex.Message, ex);
            }
        }

        private static void EnsureSuccess(HttpResponseMessage response, string what)
        {
            if (response.IsSuccessStatusCode)
                return;
            if (IsRateLimited(response))
            {
                var reset = ResetTime(response);
                throw new InvalidOperationException("GitHub API 호출 한도(로그인하지 않으면 IP당 시간당 60회)를 넘었습니다. " +
                    (reset == null ? "잠시 후" : reset + " 이후") + " 다시 시도하거나, 릴리스 페이지에서 zip을 받아 '플러그인 찾기…'로 설치하세요.");
            }
            throw new InvalidOperationException(what + "을(를) 받지 못했습니다: " + (int)response.StatusCode + " " + response.ReasonPhrase);
        }

        private static bool IsRateLimited(HttpResponseMessage response)
        {
            if ((int)response.StatusCode == 429)
                return true;
            IEnumerable<string> values;
            return response.StatusCode == HttpStatusCode.Forbidden
                && response.Headers.TryGetValues("X-RateLimit-Remaining", out values) && values.FirstOrDefault() == "0";
        }

        private static string ResetTime(HttpResponseMessage response)
        {
            IEnumerable<string> values;
            long seconds;
            if (response.Headers.TryGetValues("X-RateLimit-Reset", out values) && long.TryParse(values.FirstOrDefault(), out seconds))
                return DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime().ToString("HH:mm");
            return null;
        }

        private static string TooLarge(string name)
        {
            return name + "이(가) 너무 큽니다(최대 " + MaxDownloadBytes / 1024 / 1024 + "MB). 플러그인 파일이 맞는지 확인하세요.";
        }

        private static string GetString(JsonElement element, string name)
        {
            JsonElement value;
            return element.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }
    }
}

using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Folderss.Services
{
    public static class UpdateService
    {
        private const string ApiUrl = "https://api.github.com/repos/zaruous/Folderss/releases/latest";

        public sealed class UpdateInfo
        {
            public string TagName { get; }
            public string HtmlUrl { get; }
            public string DownloadUrl { get; } // null이면 assets 없음 → 브라우저로 열기

            public UpdateInfo(string tagName, string htmlUrl, string downloadUrl)
            {
                TagName = tagName;
                HtmlUrl = htmlUrl;
                DownloadUrl = downloadUrl;
            }
        }

        public static async Task<UpdateInfo> CheckAsync()
        {
            try
            {
                string json;
                using (var client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "Folderss-UpdateCheck");
                    json = await client.DownloadStringTaskAsync(ApiUrl);
                }

                var tagMatch = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                var urlMatch = Regex.Match(json, "\"html_url\"\\s*:\\s*\"([^\"]+)\"");

                if (!tagMatch.Success || !urlMatch.Success)
                    return null;

                var tagName = tagMatch.Groups[1].Value;
                var htmlUrl = urlMatch.Groups[1].Value;

                if (!IsNewer(tagName))
                    return null;

                // assets 배열에서 .exe 또는 .msi 다운로드 URL 추출
                var downloadMatch = Regex.Match(
                    json,
                    "\"browser_download_url\"\\s*:\\s*\"([^\"]+\\.(?:exe|msi|zip))\"",
                    RegexOptions.IgnoreCase);
                var downloadUrl = downloadMatch.Success ? downloadMatch.Groups[1].Value : null;

                return new UpdateInfo(tagName, htmlUrl, downloadUrl);
            }
            catch
            {
                return null;
            }
        }

        public static Task DownloadAsync(string url, string destPath, IProgress<int> progress)
        {
            var tcs = new TaskCompletionSource<bool>();
            var client = new WebClient();
            client.Headers.Add("User-Agent", "Folderss-UpdateCheck");

            client.DownloadProgressChanged += (s, e) =>
                progress?.Report(e.ProgressPercentage);

            client.DownloadFileCompleted += (s, e) =>
            {
                client.Dispose();
                if (e.Error != null)
                    tcs.SetException(e.Error);
                else if (e.Cancelled)
                    tcs.SetCanceled();
                else
                    tcs.SetResult(true);
            };

            client.DownloadFileAsync(new Uri(url), destPath);
            return tcs.Task;
        }

        private static bool IsNewer(string tagName)
        {
            var normalized = tagName.TrimStart('v', 'V');
            Version remote;
            if (!Version.TryParse(normalized, out remote))
                return false;

            var current = Assembly.GetExecutingAssembly().GetName().Version;
            return remote > current;
        }
    }
}

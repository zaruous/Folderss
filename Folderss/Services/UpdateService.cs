using System;
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

            public UpdateInfo(string tagName, string htmlUrl)
            {
                TagName = tagName;
                HtmlUrl = htmlUrl;
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

                return IsNewer(tagName) ? new UpdateInfo(tagName, htmlUrl) : null;
            }
            catch
            {
                return null;
            }
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Folderss.Services;

namespace Folderss
{
    /// <summary>GitHub 저장소 주소를 받아 최신 정식 릴리스를 확인한다. 확인에 실패하면 창을 닫지 않고 이유를 보여 준다.</summary>
    public sealed class PluginGitHubInstallDialog : GitDialogBase
    {
        private readonly TextBox _url;

        public string RepoOwner { get; private set; }
        public string Repo { get; private set; }
        public GitHubRelease Release { get; private set; }

        public PluginGitHubInstallDialog() : base("GitHub에서 플러그인 설치", 520)
        {
            AddText("공개 GitHub 저장소의 최신 정식 릴리스에서 플러그인 zip을 받아 설치합니다.");
            AddLabel("저장소 주소");
            _url = new TextBox();
            Body.Children.Add(_url);
            AddText("예: https://github.com/zaruous/Folderss-db-helper", secondary: true, top: 4);
            AddText("플러그인은 Folderss와 같은 권한으로 실행됩니다. 신뢰할 수 있는 저장소만 입력하세요.", secondary: true, top: 8);
            AddButtons("릴리스 확인");
            Loaded += (s, e) => _url.Focus();
        }

        protected override async Task<string> ValidateAsync()
        {
            (string Owner, string Repo) repository;
            try
            {
                repository = PluginGitHubSource.ParseRepository(_url.Text);
            }
            catch (ArgumentException ex)
            {
                return ex.Message;
            }

            var release = await PluginGitHubSource.GetLatestReleaseAsync(PluginGitHubSource.SharedClient, repository.Owner, repository.Repo, CancellationToken.None);
            if (release.ZipAssets.Count == 0)
                return "최신 릴리스(" + release.TagName + ")에 zip 첨부 파일이 없습니다. 플러그인 zip이 릴리스에 첨부되어 있는지 확인하세요.";

            RepoOwner = repository.Owner;
            Repo = repository.Repo;
            Release = release;
            return null;
        }
    }

    /// <summary>릴리스에 zip이 여러 개일 때 설치할 파일을 고른다.</summary>
    public sealed class PluginAssetChoiceDialog : GitDialogBase
    {
        private readonly List<(RadioButton Radio, GitHubReleaseAsset Asset)> _choices = new List<(RadioButton, GitHubReleaseAsset)>();

        public GitHubReleaseAsset Selected
        {
            get { return _choices.Where(c => c.Radio.IsChecked == true).Select(c => c.Asset).FirstOrDefault(); }
        }

        public PluginAssetChoiceDialog(GitHubRelease release) : base("설치할 파일 선택", 480)
        {
            AddText(release.TagName + " 릴리스에 zip 파일이 여러 개 있습니다. 설치할 플러그인 파일을 고르세요.");
            var first = true;
            foreach (var asset in release.ZipAssets)
            {
                var size = asset.Size >= 1024 * 1024 ? (asset.Size / 1024.0 / 1024.0).ToString("0.0") + " MB" : (asset.Size / 1024.0).ToString("0") + " KB";
                _choices.Add((AddRadio("plugin-asset", asset.Name, size, first), asset));
                first = false;
            }
            AddButtons("설치");
        }
    }

    /// <summary>
    /// 같은 id의 플러그인을 다른 출처에서 설치할 때. 같은 id는 기존 플러그인의 설정·데이터를 그대로 읽으므로
    /// 경고와 확인 체크를 거쳐야 교체한다(기본은 설치하지 않음).
    /// </summary>
    public sealed class PluginSourceChangeDialog : GitDialogBase
    {
        public PluginSourceChangeDialog(PluginManifest manifest, string previousSource, string newSource) : base("플러그인 출처가 다릅니다", 520)
        {
            AddText(string.Format("{0} {1} ({2})은(는) 이미 다른 출처에서 설치되어 있습니다.", manifest.DisplayName, manifest.Version, manifest.Id));
            AddText("기존: " + PluginSourceStore.Describe(previousSource) + "\n새로 설치: " + PluginSourceStore.Describe(newSource), top: 8);
            AddText("⚠ 같은 ID의 플러그인은 기존 플러그인의 설정과 데이터(저장된 접속 정보·비밀번호 포함)를 그대로 읽을 수 있습니다. " +
                    "출처를 바꾼 것이 본인이 아니라면 설치하지 마세요.", warning: true, top: 8);
            var confirm = AddCheck("출처가 바뀐 것을 확인했고 이 플러그인을 신뢰합니다", false);
            var ok = AddButtons("교체 설치");
            ok.IsEnabled = false;
            confirm.Checked += (s, e) => ok.IsEnabled = true;
            confirm.Unchecked += (s, e) => ok.IsEnabled = false;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace Folderss.Services
{
    public enum GitPullMode
    {
        /// <summary>fast-forward만 (<c>--ff-only</c>). 갈라졌으면 실패하고 아무것도 바꾸지 않는다.</summary>
        FastForwardOnly,
        /// <summary>병합 커밋 허용 (<c>--no-rebase</c>).</summary>
        Merge,
        /// <summary>rebase (<c>--rebase</c>).</summary>
        Rebase,
        /// <summary>옵션 없이 <c>git pull</c> — 사용자 git 설정(<c>pull.rebase</c>, <c>pull.ff</c>)을 따른다.</summary>
        UseGitConfig
    }

    public enum GitBaseFolderMode
    {
        /// <summary>폴더 하나를 선택했으면 그 폴더, 아니면 패널의 현재 폴더.</summary>
        SelectedFolderFirst,
        /// <summary>선택과 관계없이 항상 패널의 현재 폴더.</summary>
        CurrentFolder
    }

    public sealed class GitSettings
    {
        /// <summary>git 실행 파일 경로. 비우면 PATH와 기본 설치 위치에서 찾는다.</summary>
        public string GitExecutablePath { get; set; } = string.Empty;
        public GitBaseFolderMode BaseFolderMode { get; set; } = GitBaseFolderMode.SelectedFolderFirst;
        public GitPullMode PullMode { get; set; } = GitPullMode.FastForwardOnly;
        public int ScanDepth { get; set; } = GitSettingsService.DefaultScanDepth;
        public List<string> ExcludedFolders { get; set; } = new List<string>(GitSettingsService.DefaultExcludedFolders);
        public int LogLimit { get; set; } = GitSettingsService.DefaultLogLimit;
        public bool LogAllBranches { get; set; } = true;

        public GitSettings Clone()
        {
            var clone = (GitSettings)MemberwiseClone();
            clone.ExcludedFolders = new List<string>(ExcludedFolders);
            return clone;
        }
    }

    /// <summary>
    /// Git 옵션을 <c>%LOCALAPPDATA%\Folderss\git-settings.xml</c>에 저장한다. 설정 창의 Git 탭이 편집한다.
    /// diff 옵션(공백 무시·인코딩·보기·외부 도구)은 <see cref="DiffSettingsService"/>로 옮겼다 — 옛 파일의 그 값은 거기서 한 번 이관한다.
    /// </summary>
    public static class GitSettingsService
    {
        public const int DefaultScanDepth = 6;
        public const int MinScanDepth = 0;
        public const int MaxScanDepth = 20;
        public const int DefaultLogLimit = 300;
        public const int MinLogLimit = 10;
        public const int MaxLogLimit = 5000;
        public static readonly string[] DefaultExcludedFolders = { "node_modules", "bin", "obj", ".vs", "packages" };

        public static readonly string DefaultConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Folderss", "git-settings.xml");

        public static GitSettings Load() => Load(DefaultConfigPath);

        public static GitSettings Load(string path)
        {
            var settings = new GitSettings();
            if (!File.Exists(path))
                return settings;

            try
            {
                var doc = new XmlDocument();
                doc.Load(path);
                var root = doc.DocumentElement;
                if (root == null)
                    return settings;

                settings.GitExecutablePath = root.GetAttribute("gitPath");
                if (Enum.TryParse(root.GetAttribute("baseFolder"), out GitBaseFolderMode baseFolderMode))
                    settings.BaseFolderMode = baseFolderMode;
                if (Enum.TryParse(root.GetAttribute("pullMode"), out GitPullMode pullMode))
                    settings.PullMode = pullMode;
                if (int.TryParse(root.GetAttribute("scanDepth"), out var depth))
                    settings.ScanDepth = Math.Clamp(depth, MinScanDepth, MaxScanDepth);
                if (int.TryParse(root.GetAttribute("logLimit"), out var logLimit))
                    settings.LogLimit = Math.Clamp(logLimit, MinLogLimit, MaxLogLimit);
                if (bool.TryParse(root.GetAttribute("logAll"), out var logAll))
                    settings.LogAllBranches = logAll;

                if (root.SelectSingleNode("excludes") != null)
                {
                    settings.ExcludedFolders = root.SelectNodes("excludes/exclude")
                        .Cast<XmlElement>()
                        .Select(e => e.InnerText.Trim())
                        .Where(name => name.Length > 0)
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                // 읽기 실패는 기본값으로 동작한다(창을 못 여는 것보다 낫다). 원인은 디버그 출력에 남긴다.
                System.Diagnostics.Debug.WriteLine("git-settings.xml 읽기 실패: " + ex);
            }
            return settings;
        }

        /// <summary>실패는 예외로 알린다 — 설정 창이 "설정 저장 실패"로 모아 보여 준다.</summary>
        public static void Save(GitSettings settings) => Save(settings, DefaultConfigPath);

        public static void Save(GitSettings settings, string path)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            var doc = new XmlDocument();
            var root = doc.CreateElement("git");
            root.SetAttribute("gitPath", settings.GitExecutablePath ?? string.Empty);
            root.SetAttribute("baseFolder", settings.BaseFolderMode.ToString());
            root.SetAttribute("pullMode", settings.PullMode.ToString());
            root.SetAttribute("scanDepth", settings.ScanDepth.ToString());
            root.SetAttribute("logLimit", settings.LogLimit.ToString());
            root.SetAttribute("logAll", settings.LogAllBranches.ToString());

            var excludes = doc.CreateElement("excludes");
            foreach (var name in settings.ExcludedFolders)
            {
                var element = doc.CreateElement("exclude");
                element.InnerText = name;
                excludes.AppendChild(element);
            }
            root.AppendChild(excludes);

            doc.AppendChild(root);
            SettingsFile.Write(path, doc.Save);
        }
    }
}

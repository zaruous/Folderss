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
        public GitBaseFolderMode BaseFolderMode { get; set; } = GitBaseFolderMode.SelectedFolderFirst;
        public GitPullMode PullMode { get; set; } = GitPullMode.FastForwardOnly;
        public int ScanDepth { get; set; } = GitSettingsService.DefaultScanDepth;
        public List<string> ExcludedFolders { get; set; } = new List<string>(GitSettingsService.DefaultExcludedFolders);
        public int LogLimit { get; set; } = GitSettingsService.DefaultLogLimit;
        public bool LogAllBranches { get; set; } = true;

        public GitSettings Clone()
        {
            return new GitSettings
            {
                BaseFolderMode = BaseFolderMode,
                PullMode = PullMode,
                ScanDepth = ScanDepth,
                ExcludedFolders = new List<string>(ExcludedFolders),
                LogLimit = LogLimit,
                LogAllBranches = LogAllBranches
            };
        }
    }

    /// <summary>Git 창 옵션을 <c>%LOCALAPPDATA%\Folderss\git-settings.xml</c>에 저장한다.</summary>
    public static class GitSettingsService
    {
        public const int DefaultScanDepth = 6;
        public const int MinScanDepth = 0;
        public const int MaxScanDepth = 20;
        public const int DefaultLogLimit = 300;
        public const int MinLogLimit = 10;
        public const int MaxLogLimit = 5000;
        public static readonly string[] DefaultExcludedFolders = { "node_modules", "bin", "obj", ".vs", "packages" };

        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Folderss", "git-settings.xml");

        public static GitSettings Load()
        {
            var settings = new GitSettings();
            if (!File.Exists(ConfigPath))
                return settings;

            try
            {
                var doc = new XmlDocument();
                doc.Load(ConfigPath);
                var root = doc.DocumentElement;
                if (root == null)
                    return settings;

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

        /// <summary>실패는 예외로 알린다 — 호출 측이 사용자에게 보여 준다.</summary>
        public static void Save(GitSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            var doc = new XmlDocument();
            var root = doc.CreateElement("git");
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

            SettingsFile.Write(ConfigPath, doc.Save);
        }
    }
}

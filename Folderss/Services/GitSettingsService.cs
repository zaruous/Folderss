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

    public enum GitDiffToolMode
    {
        /// <summary>외부 도구를 쓰지 않는다(내장 diff만).</summary>
        None,
        /// <summary>사용자 git 설정의 difftool(<c>diff.tool</c> / <c>merge.tool</c>)을 쓴다.</summary>
        GitConfig,
        /// <summary>설정 창에서 지정한 실행 파일과 인수를 쓴다.</summary>
        Custom
    }

    /// <summary>
    /// 파일 인코딩은 BOM이 있으면 그 인코딩, 없으면 UTF-8이다. 이 값은 BOM 없는 파일에서 UTF-8로 읽히지 않는 줄만
    /// 다시 해석할 대체 인코딩이다(기본: 사용 안 함).
    /// </summary>
    public enum GitFallbackEncoding
    {
        /// <summary>Windows 시스템 ANSI 코드 페이지(한국어 Windows는 CP949).</summary>
        SystemAnsi,
        Cp949,
        /// <summary>대체 해석 안 함(깨진 문자는 그대로).</summary>
        None
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

        /// <summary>내장 diff에서 공백만 바뀐 줄을 무시한다(<c>-w</c>).</summary>
        public bool IgnoreWhitespace { get; set; }
        public GitFallbackEncoding FallbackEncoding { get; set; } = GitFallbackEncoding.None;

        public GitDiffToolMode DiffToolMode { get; set; } = GitDiffToolMode.None;
        public string DiffToolPath { get; set; } = string.Empty;

        /// <summary>외부 도구 인수. <c>{left}</c>/<c>{right}</c>는 비교할 두 파일(또는 폴더)로 바뀐다.</summary>
        public string DiffToolArguments { get; set; } = GitSettingsService.DefaultDiffToolArguments;

        public GitSettings Clone()
        {
            var clone = (GitSettings)MemberwiseClone();
            clone.ExcludedFolders = new List<string>(ExcludedFolders);
            return clone;
        }
    }

    /// <summary>외부 비교 도구 프리셋. 설치 위치 후보 중 존재하는 첫 경로를 쓴다.</summary>
    public sealed class GitDiffToolPreset
    {
        public string Name { get; set; }
        public string[] Candidates { get; set; }
        public string Arguments { get; set; }
        public string Note { get; set; }

        public string ResolvePath()
        {
            var expanded = Candidates.Select(Environment.ExpandEnvironmentVariables).ToList();
            return expanded.FirstOrDefault(File.Exists) ?? expanded.FirstOrDefault() ?? string.Empty;
        }
    }

    /// <summary>Git 옵션을 <c>%LOCALAPPDATA%\Folderss\git-settings.xml</c>에 저장한다. 설정 창의 Git 탭이 편집한다.</summary>
    public static class GitSettingsService
    {
        public const int DefaultScanDepth = 6;
        public const int MinScanDepth = 0;
        public const int MaxScanDepth = 20;
        public const int DefaultLogLimit = 300;
        public const int MinLogLimit = 10;
        public const int MaxLogLimit = 5000;
        public const string DefaultDiffToolArguments = "\"{left}\" \"{right}\"";
        public static readonly string[] DefaultExcludedFolders = { "node_modules", "bin", "obj", ".vs", "packages" };

        public static readonly GitDiffToolPreset[] DiffToolPresets =
        {
            new GitDiffToolPreset
            {
                Name = "WinMerge",
                Candidates = new[] { @"%ProgramFiles%\WinMerge\WinMergeU.exe", @"%ProgramFiles(x86)%\WinMerge\WinMergeU.exe", @"%LOCALAPPDATA%\Programs\WinMerge\WinMergeU.exe" },
                Arguments = "-e -u -r \"{left}\" \"{right}\""
            },
            new GitDiffToolPreset
            {
                Name = "Beyond Compare",
                Candidates = new[] { @"%ProgramFiles%\Beyond Compare 5\BCompare.exe", @"%ProgramFiles%\Beyond Compare 4\BCompare.exe", @"%ProgramFiles(x86)%\Beyond Compare 4\BCompare.exe" },
                Arguments = "\"{left}\" \"{right}\""
            },
            new GitDiffToolPreset
            {
                Name = "Visual Studio Code",
                Candidates = new[] { @"%LOCALAPPDATA%\Programs\Microsoft VS Code\Code.exe", @"%ProgramFiles%\Microsoft VS Code\Code.exe" },
                Arguments = "--wait --new-window --diff \"{left}\" \"{right}\"",
                Note = "VS Code는 파일 비교만 됩니다(폴더 비교 불가)."
            },
            new GitDiffToolPreset
            {
                Name = "KDiff3",
                Candidates = new[] { @"%ProgramFiles%\KDiff3\kdiff3.exe", @"%ProgramFiles%\KDiff3\bin\kdiff3.exe" },
                Arguments = "\"{left}\" \"{right}\""
            },
            new GitDiffToolPreset
            {
                Name = "Meld",
                Candidates = new[] { @"%ProgramFiles%\Meld\Meld.exe", @"%LOCALAPPDATA%\Programs\Meld\Meld.exe" },
                Arguments = "\"{left}\" \"{right}\""
            }
        };

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
                if (bool.TryParse(root.GetAttribute("ignoreWhitespace"), out var ignoreWhitespace))
                    settings.IgnoreWhitespace = ignoreWhitespace;
                if (Enum.TryParse(root.GetAttribute("fallbackEncoding"), out GitFallbackEncoding fallback))
                    settings.FallbackEncoding = fallback;

                if (root.SelectSingleNode("excludes") != null)
                {
                    settings.ExcludedFolders = root.SelectNodes("excludes/exclude")
                        .Cast<XmlElement>()
                        .Select(e => e.InnerText.Trim())
                        .Where(name => name.Length > 0)
                        .ToList();
                }

                if (root.SelectSingleNode("diffTool") is XmlElement tool)
                {
                    if (Enum.TryParse(tool.GetAttribute("mode"), out GitDiffToolMode mode))
                        settings.DiffToolMode = mode;
                    settings.DiffToolPath = tool.GetAttribute("path");
                    if (tool.HasAttribute("arguments"))
                        settings.DiffToolArguments = tool.GetAttribute("arguments");
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
            root.SetAttribute("ignoreWhitespace", settings.IgnoreWhitespace.ToString());
            root.SetAttribute("fallbackEncoding", settings.FallbackEncoding.ToString());

            var excludes = doc.CreateElement("excludes");
            foreach (var name in settings.ExcludedFolders)
            {
                var element = doc.CreateElement("exclude");
                element.InnerText = name;
                excludes.AppendChild(element);
            }
            root.AppendChild(excludes);

            var tool = doc.CreateElement("diffTool");
            tool.SetAttribute("mode", settings.DiffToolMode.ToString());
            tool.SetAttribute("path", settings.DiffToolPath ?? string.Empty);
            tool.SetAttribute("arguments", settings.DiffToolArguments ?? string.Empty);
            root.AppendChild(tool);

            doc.AppendChild(root);
            SettingsFile.Write(path, doc.Save);
        }
    }
}

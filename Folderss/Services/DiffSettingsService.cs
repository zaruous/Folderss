using System;
using System.IO;
using System.Linq;
using System.Xml;

namespace Folderss.Services
{
    public enum GitDiffToolMode
    {
        /// <summary>외부 도구를 쓰지 않는다(내장 diff만).</summary>
        None,
        /// <summary>사용자 git 설정의 difftool(<c>diff.tool</c> / <c>merge.tool</c>)을 쓴다.</summary>
        GitConfig,
        /// <summary>설정 창에서 지정한 실행 파일과 인수를 쓴다.</summary>
        Custom
    }

    /// <summary>diff를 볼 때 변경점 주변을 얼마나 보일지.</summary>
    public enum GitDiffViewMode
    {
        /// <summary>변경점만(git 기본 문맥 3줄 또는 사용자 diff.context).</summary>
        ChangesOnly,
        /// <summary>변경점 + 앞뒤 10줄.</summary>
        Context10,
        /// <summary>파일 전체(변경점은 색으로 표시).</summary>
        FullFile
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

    /// <summary>
    /// 내장 diff와 외부 비교 도구 옵션. Git 창·두 파일 비교 창·HTML 보고서가 함께 쓴다(설정 창의 "비교" 탭).
    /// 이름의 Git 접두사(열거형)는 이전 버전과의 호환 때문에 그대로 둔다.
    /// </summary>
    public sealed class DiffSettings
    {
        /// <summary>내장 diff에서 공백만 바뀐 줄을 무시한다(<c>-w</c>).</summary>
        public bool IgnoreWhitespace { get; set; }
        public GitFallbackEncoding FallbackEncoding { get; set; } = GitFallbackEncoding.None;

        /// <summary>diff 창의 기본 보기. 각 diff 창에서 바로 바꿀 수 있고, 이 값은 창을 열 때의 초기값이다.</summary>
        public GitDiffViewMode DiffViewMode { get; set; } = GitDiffViewMode.ChangesOnly;

        public GitDiffToolMode DiffToolMode { get; set; } = GitDiffToolMode.None;
        public string DiffToolPath { get; set; } = string.Empty;

        /// <summary>외부 도구 인수. <c>{left}</c>/<c>{right}</c>는 비교할 두 파일(또는 폴더)로 바뀐다.</summary>
        public string DiffToolArguments { get; set; } = DiffSettingsService.DefaultDiffToolArguments;

        public DiffSettings Clone() => (DiffSettings)MemberwiseClone();
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

    /// <summary>
    /// diff 옵션을 <c>%LOCALAPPDATA%\Folderss\diff-settings.xml</c>에 저장한다. 이 파일이 없으면 이전 버전이 쓰던
    /// <c>git-settings.xml</c>의 같은 값(루트 속성 ignoreWhitespace·fallbackEncoding·diffView, diffTool 요소)을 읽어 이관한다.
    /// </summary>
    public static class DiffSettingsService
    {
        public const string DefaultDiffToolArguments = "\"{left}\" \"{right}\"";

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
                // BCompare.exe는 이미 떠 있는 창에 비교를 넘기고 바로 끝날 수 있다. BComp.exe는 비교를 닫을 때까지 기다린다(버전 관리 연동용).
                Name = "Beyond Compare",
                Candidates = new[] { @"%ProgramFiles%\Beyond Compare 5\BComp.exe", @"%ProgramFiles%\Beyond Compare 4\BComp.exe", @"%ProgramFiles(x86)%\Beyond Compare 4\BComp.exe" },
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
            "Folderss", "diff-settings.xml");

        /// <summary>
        /// 실제로 실행할 외부 도구 경로. Beyond Compare의 BCompare.exe는 같은 폴더의 BComp.exe로 바꾼다 —
        /// BCompare.exe는 이미 떠 있는 창에 비교를 넘기고 바로 끝날 수 있어, git이 Git 창 비교용 임시 파일을 곧바로 지워 빈 비교가 된다.
        /// 예전 프리셋으로 저장된 설정도 그대로 고쳐진다. BComp.exe가 없으면 원래 경로.
        /// </summary>
        public static string ResolveToolPath(string toolPath)
        {
            if (string.IsNullOrWhiteSpace(toolPath))
                return toolPath;
            var path = toolPath.Trim().Trim('"');
            if (!string.Equals(Path.GetFileName(path), "BCompare.exe", StringComparison.OrdinalIgnoreCase))
                return toolPath;
            var waiting = Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, "BComp.exe");
            return File.Exists(waiting) ? waiting : toolPath;
        }

        /// <summary>
        /// 저장된 실행 파일에 맞는 프리셋 위치(설정 창을 열 때 프리셋 목록에서 고른 상태로 보인다). 실행 파일 이름으로 맞추고
        /// (예전 프리셋의 BCompare.exe도 Beyond Compare), 맞는 것이 없으면 -1.
        /// </summary>
        public static int FindPresetIndex(string toolPath)
        {
            if (string.IsNullOrWhiteSpace(toolPath))
                return -1;
            var name = FileNameOf(toolPath.Trim().Trim('"'));
            if (string.Equals(name, "BCompare.exe", StringComparison.OrdinalIgnoreCase))
                name = "BComp.exe";
            return Array.FindIndex(DiffToolPresets,
                preset => preset.Candidates.Any(candidate => string.Equals(FileNameOf(candidate), name, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>Windows 경로의 마지막 이름. 프리셋 후보는 \ 구분이라 Path.GetFileName(리눅스에서는 /만 구분)을 쓰지 않는다.</summary>
        private static string FileNameOf(string path) => path.Substring(path.LastIndexOfAny(new[] { '\\', '/' }) + 1);

        public static DiffSettings Load() => Load(DefaultConfigPath, GitSettingsService.DefaultConfigPath);

        /// <param name="legacyGitSettingsPath">자체 파일이 없을 때 옛 값을 읽을 git-settings.xml.</param>
        public static DiffSettings Load(string path, string legacyGitSettingsPath)
        {
            var settings = new DiffSettings();
            var source = File.Exists(path) ? path : legacyGitSettingsPath;
            if (string.IsNullOrEmpty(source) || !File.Exists(source))
                return settings;

            try
            {
                var doc = new XmlDocument();
                doc.Load(source);
                var root = doc.DocumentElement;
                if (root == null)
                    return settings;

                if (bool.TryParse(root.GetAttribute("ignoreWhitespace"), out var ignoreWhitespace))
                    settings.IgnoreWhitespace = ignoreWhitespace;
                if (Enum.TryParse(root.GetAttribute("fallbackEncoding"), out GitFallbackEncoding fallback))
                    settings.FallbackEncoding = fallback;
                if (Enum.TryParse(root.GetAttribute("diffView"), out GitDiffViewMode diffView))
                    settings.DiffViewMode = diffView;

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
                System.Diagnostics.Debug.WriteLine(Path.GetFileName(source) + " 읽기 실패: " + ex);
            }
            return settings;
        }

        /// <summary>실패는 예외로 알린다 — 설정 창이 "설정 저장 실패"로 모아 보여 준다.</summary>
        public static void Save(DiffSettings settings) => Save(settings, DefaultConfigPath);

        public static void Save(DiffSettings settings, string path)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            var doc = new XmlDocument();
            var root = doc.CreateElement("diff");
            root.SetAttribute("ignoreWhitespace", settings.IgnoreWhitespace.ToString());
            root.SetAttribute("fallbackEncoding", settings.FallbackEncoding.ToString());
            root.SetAttribute("diffView", settings.DiffViewMode.ToString());

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

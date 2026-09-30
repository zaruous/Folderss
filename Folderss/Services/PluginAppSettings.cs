using System.Collections.Generic;

namespace Folderss.Services
{
    /// <summary>
    /// 플러그인에 읽기 전용으로 보여 주는 본체 설정. 키는 플러그인과의 약속이므로 C# 속성 이름에 묶지 않고 여기서 명시한다
    /// (리플렉션으로 자동 노출하면 속성 이름 변경이 플러그인을 조용히 깨뜨리고, 나중에 추가한 민감한 값도 그대로 새어 나간다).
    /// 다른 플러그인의 설정(plugin-data)은 넣지 않는다. 목록 값은 줄바꿈(\n)으로 잇는다.
    /// </summary>
    public static class PluginAppSettings
    {
        public static Dictionary<string, string> Build(string theme, GitSettings git, DiffSettings diff, ConsoleSettings console)
        {
            var values = new Dictionary<string, string>
            {
                ["theme"] = theme
            };
            if (git != null)
            {
                values["git.executablePath"] = git.GitExecutablePath;
                values["git.baseFolderMode"] = git.BaseFolderMode.ToString();
                values["git.pullMode"] = git.PullMode.ToString();
                values["git.scanDepth"] = git.ScanDepth.ToString();
                values["git.excludedFolders"] = string.Join("\n", git.ExcludedFolders ?? new List<string>());
                values["git.logLimit"] = git.LogLimit.ToString();
                values["git.logAllBranches"] = git.LogAllBranches ? "true" : "false";
            }
            if (diff != null)
            {
                values["diff.ignoreWhitespace"] = diff.IgnoreWhitespace ? "true" : "false";
                values["diff.fallbackEncoding"] = diff.FallbackEncoding.ToString();
                values["diff.viewMode"] = diff.DiffViewMode.ToString();
                values["diff.toolMode"] = diff.DiffToolMode.ToString();
                values["diff.toolPath"] = diff.DiffToolPath;
                values["diff.toolArguments"] = diff.DiffToolArguments;
            }
            if (console != null)
            {
                values["console.preferredProfileKey"] = console.PreferredProfileKey;
                values["console.fontSize"] = console.FontSize.ToString();
            }
            return values;
        }
    }
}

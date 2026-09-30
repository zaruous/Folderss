using System.Collections.Generic;
using System.Linq;

namespace Folderss.Services
{
    /// <summary>
    /// 내장 diff 하나와, 같은 비교를 외부 도구로 여는 <c>git difftool</c> 인수 한 쌍.
    /// <see cref="ExternalSelector"/>가 null이면 외부 도구로 열 수 없는 비교다(예: 추적 안 되는 파일, 최초 커밋).
    /// </summary>
    public sealed class GitDiffRequest
    {
        public string Title { get; set; }
        public List<string> Arguments { get; set; }
        public bool NoIndex { get; set; }
        public string EmptyMessage { get; set; } = "차이가 없습니다.";

        /// <summary><c>git difftool</c> 뒤에 붙일 비교 대상 인수.</summary>
        public List<string> ExternalSelector { get; set; }

        /// <summary>true면 폴더 비교(<c>-d</c>)로 한 번에 연다 — 여러 파일이 걸린 커밋·범위 비교.</summary>
        public bool ExternalDirDiff { get; set; }
    }

    /// <summary>
    /// Git 창이 쓰는 diff·비교 명령 인수. UI와 테스트가 같은 인수를 쓰도록 한 곳에 둔다.
    /// 공통: 색 끔, 사용자 외부 diff 드라이버(diff.external) 무시, 이름 변경 감지. 공백 무시(<c>-w</c>)는 선택.
    /// </summary>
    public static class GitDiffCommands
    {
        /// <summary>현재 브랜치의 upstream. 인수 목록으로 넘기므로 셸 해석 걱정이 없다.</summary>
        public const string Upstream = "@{u}";

        /// <summary><c>-c difftool.&lt;이름&gt;.cmd=…</c>로 등록하는 임시 도구 이름. 사용자 git 설정은 바꾸지 않는다.</summary>
        public const string CustomToolName = "folderss";

        private static List<string> Diff(bool ignoreWhitespace, IEnumerable<string> leading, IEnumerable<string> selector)
        {
            var args = new List<string> { "diff" };
            args.AddRange(leading);
            args.AddRange(new[] { "--no-color", "--no-ext-diff", "-M" });
            if (ignoreWhitespace)
                args.Add("-w");
            args.AddRange(selector);
            return args;
        }

        private static List<string> PathSelector(params string[] paths)
        {
            var selector = new List<string> { "--" };
            selector.AddRange(paths.Where(p => !string.IsNullOrEmpty(p)));
            return selector;
        }

        /// <summary>작업 트리 ↔ 인덱스 (스테이지 안 된 변경).</summary>
        public static GitDiffRequest WorkTree(string path, bool ignoreWhitespace = false)
        {
            var selector = PathSelector(path);
            return new GitDiffRequest
            {
                Arguments = Diff(ignoreWhitespace, new string[0], selector),
                ExternalSelector = selector
            };
        }

        /// <summary>인덱스 ↔ HEAD (스테이지된 변경). 이름 변경은 원래 경로도 넣어야 짝이 맞는다.</summary>
        public static GitDiffRequest Staged(string path, string originalPath, bool ignoreWhitespace = false)
        {
            var selector = PathSelector(originalPath, path);
            var external = new List<string> { "--cached" };
            external.AddRange(selector);
            return new GitDiffRequest
            {
                Arguments = Diff(ignoreWhitespace, new[] { "--cached" }, selector),
                ExternalSelector = external
            };
        }

        /// <summary>
        /// 추적 안 되는 파일 전체를 추가로 보인다. <c>/dev/null</c>은 git이 모든 플랫폼에서 빈 파일로 해석한다.
        /// <c>--no-index</c>는 차이가 있으면 종료 코드 1이므로 <see cref="IsSuccess"/>로 판정한다. 외부 도구는 지원하지 않는다.
        /// </summary>
        public static GitDiffRequest Untracked(string path, bool ignoreWhitespace = false)
        {
            return new GitDiffRequest
            {
                Arguments = Diff(ignoreWhitespace, new[] { "--no-index" }, PathSelector("/dev/null", path)),
                NoIndex = true
            };
        }

        /// <summary>두 리비전 비교. 예: <c>@{u}...HEAD</c>(push로 보낼 변경), <c>HEAD...@{u}</c>(pull로 받을 변경).</summary>
        public static GitDiffRequest Range(string range, bool ignoreWhitespace = false)
        {
            var selector = new List<string> { range, "--" };
            return new GitDiffRequest
            {
                Arguments = Diff(ignoreWhitespace, new string[0], selector),
                ExternalSelector = selector,
                ExternalDirDiff = true
            };
        }

        /// <summary>작업 트리(커밋 안 한 변경 포함, 추적 파일만) ↔ 리비전.</summary>
        public static GitDiffRequest WorkTreeAgainst(string revision, bool ignoreWhitespace = false)
        {
            var selector = new List<string> { revision, "--" };
            return new GitDiffRequest
            {
                Arguments = Diff(ignoreWhitespace, new string[0], selector),
                ExternalSelector = selector,
                ExternalDirDiff = true
            };
        }

        /// <summary>커밋 하나의 변경. 병합 커밋은 첫 부모 기준, 최초 커밋은 빈 트리 기준(외부 도구 미지원).</summary>
        public static GitDiffRequest Commit(string hash, string[] parents, bool ignoreWhitespace = false)
        {
            if (parents == null || parents.Length == 0)
            {
                var args = new List<string> { "show", "--format=", "--no-color", "--no-ext-diff", "-M" };
                if (ignoreWhitespace)
                    args.Add("-w");
                args.Add(hash);
                args.Add("--");
                return new GitDiffRequest { Arguments = args };
            }

            var selector = new List<string> { parents[0], hash, "--" };
            return new GitDiffRequest
            {
                Arguments = Diff(ignoreWhitespace, new string[0], selector),
                ExternalSelector = selector,
                ExternalDirDiff = true
            };
        }

        /// <summary><c>&lt;from&gt;..&lt;to&gt;</c> 범위 커밋 목록(<see cref="GitOutputParser.ParseLog"/>와 짝).</summary>
        public static List<string> LogRange(string from, string to, int limit)
        {
            return new List<string> { "log", "-z", "-n", limit.ToString(), GitOutputParser.LogFormat, from + ".." + to, "--" };
        }

        public static bool IsSuccess(GitResult result, bool noIndex)
        {
            return result != null && !result.TimedOut && (result.ExitCode == 0 || (noIndex && result.ExitCode == 1));
        }

        /// <summary>
        /// 외부 도구 실행 인수. <paramref name="mode"/>가 <see cref="GitDiffToolMode.Custom"/>이면 <c>-c difftool.folderss.cmd=…</c>로
        /// 이번 실행에만 도구를 등록한다(사용자 .gitconfig는 건드리지 않음). <see cref="GitDiffToolMode.GitConfig"/>면 사용자 설정 도구를 그대로 쓴다.
        /// </summary>
        public static List<string> ExternalTool(GitDiffRequest request, GitDiffToolMode mode, string toolPath, string toolArguments)
        {
            if (request?.ExternalSelector == null || mode == GitDiffToolMode.None)
                return null;

            var args = new List<string>();
            if (mode == GitDiffToolMode.Custom)
            {
                args.Add("-c");
                args.Add("difftool." + CustomToolName + ".cmd=" + BuildToolCommand(toolPath, toolArguments));
            }
            args.Add("difftool");
            if (mode == GitDiffToolMode.Custom)
                args.Add("--tool=" + CustomToolName);
            args.Add("--no-prompt");
            if (request.ExternalDirDiff)
                args.Add("--dir-diff");
            args.AddRange(request.ExternalSelector);
            return args;
        }

        /// <summary>
        /// difftool.cmd 값(git이 셸로 실행)을 만든다. 실행 파일 경로는 역슬래시를 슬래시로 바꾸고 작은따옴표로 감싼다.
        /// <c>{left}</c>/<c>{right}</c>는 항상 따옴표 친 <c>"$LOCAL"</c>/<c>"$REMOTE"</c>로 바꾼다 — 사용자가 따옴표를 빠뜨려도
        /// 임시 경로의 공백(예: 사용자 이름)에서 인수가 쪼개지지 않게. 둘 다 없으면 끝에 붙인다.
        /// </summary>
        public static string BuildToolCommand(string toolPath, string toolArguments)
        {
            // Git for Windows의 bash에서 안전한 형태(C:/Program Files/…)로 바꾼다. 역슬래시는 Windows 경로 구분자로만 쓰인다고 본다.
            var exe = "'" + (toolPath ?? string.Empty).Trim().Trim('"').Replace('\\', '/').Replace("'", "'\\''") + "'";
            var template = string.IsNullOrWhiteSpace(toolArguments) ? GitSettingsService.DefaultDiffToolArguments : toolArguments.Trim();
            template = template.Replace("\"{left}\"", "{left}").Replace("\"{right}\"", "{right}");
            if (!template.Contains("{left}") && !template.Contains("{right}"))
                template += " {left} {right}";
            template = template.Replace("{left}", "\"$LOCAL\"").Replace("{right}", "\"$REMOTE\"");
            return exe + " " + template;
        }
    }
}

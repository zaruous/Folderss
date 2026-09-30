using System.Collections.Generic;

namespace Folderss.Services
{
    /// <summary>
    /// Git 창이 쓰는 diff·비교 명령 인수. UI와 테스트가 같은 인수를 쓰도록 한 곳에 둔다.
    /// 공통: 색 끔, 사용자 외부 diff 도구(diff.external) 무시, 이름 변경 감지.
    /// </summary>
    public static class GitDiffCommands
    {
        /// <summary>현재 브랜치의 upstream. 인수 목록으로 넘기므로 셸 해석 걱정이 없다.</summary>
        public const string Upstream = "@{u}";

        private static readonly string[] Common = { "--no-color", "--no-ext-diff", "-M" };

        private static List<string> Diff(params string[] leading)
        {
            var args = new List<string> { "diff" };
            args.AddRange(leading);
            args.AddRange(Common);
            return args;
        }

        /// <summary>작업 트리 ↔ 인덱스 (스테이지 안 된 변경).</summary>
        public static List<string> WorkTree(string path)
        {
            var args = Diff();
            args.Add("--");
            args.Add(path);
            return args;
        }

        /// <summary>인덱스 ↔ HEAD (스테이지된 변경). 이름 변경은 원래 경로도 넣어야 짝이 맞는다.</summary>
        public static List<string> Staged(string path, string originalPath)
        {
            var args = Diff("--cached");
            args.Add("--");
            if (!string.IsNullOrEmpty(originalPath))
                args.Add(originalPath);
            args.Add(path);
            return args;
        }

        /// <summary>
        /// 추적 안 되는 파일 전체를 추가로 보인다. <c>/dev/null</c>은 git이 모든 플랫폼에서 빈 파일로 해석한다.
        /// <c>--no-index</c>는 차이가 있으면 종료 코드 1이므로 <see cref="IsSuccess"/>로 판정한다.
        /// </summary>
        public static List<string> Untracked(string path)
        {
            var args = Diff("--no-index");
            args.Add("--");
            args.Add("/dev/null");
            args.Add(path);
            return args;
        }

        /// <summary>두 리비전 비교. 예: <c>@{u}...HEAD</c>(push로 보낼 변경), <c>HEAD...@{u}</c>(pull로 받을 변경).</summary>
        public static List<string> Range(string range)
        {
            var args = Diff();
            args.Add(range);
            args.Add("--");
            return args;
        }

        /// <summary>작업 트리(커밋 안 한 변경 포함, 추적 파일만) ↔ 리비전.</summary>
        public static List<string> WorkTreeAgainst(string revision)
        {
            var args = Diff();
            args.Add(revision);
            args.Add("--");
            return args;
        }

        /// <summary>커밋 하나의 변경. 병합 커밋은 첫 부모 기준, 최초 커밋은 빈 트리 기준.</summary>
        public static List<string> Commit(string hash, string[] parents)
        {
            if (parents == null || parents.Length == 0)
            {
                var args = new List<string> { "show", "--format=" };
                args.AddRange(Common);
                args.Add(hash);
                args.Add("--");
                return args;
            }

            var diff = Diff();
            diff.Add(parents[0]);
            diff.Add(hash);
            diff.Add("--");
            return diff;
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
    }
}

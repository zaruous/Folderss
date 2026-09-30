using System.Collections.Generic;

namespace Folderss.Services
{
    public sealed class GitStashPushOptions
    {
        /// <summary>stash 메시지. 비우면 git 기본("WIP on &lt;브랜치&gt;: …").</summary>
        public string Message { get; set; }

        /// <summary>추적 안 되는 새 파일도 함께(<c>-u</c>). 기본은 추적 중인 파일의 변경만.</summary>
        public bool IncludeUntracked { get; set; }

        /// <summary>스테이지된 변경은 작업 트리에 그대로 둔다(<c>--keep-index</c>).</summary>
        public bool KeepIndex { get; set; }
    }

    public enum GitStashApplyMode
    {
        /// <summary>적용하고 stash는 남긴다(<c>apply</c>).</summary>
        Apply,
        /// <summary>적용하고 삭제(<c>pop</c>). 충돌하면 git이 stash를 남긴다.</summary>
        Pop,
        /// <summary>stash를 만든 커밋에서 새 브랜치를 만들어 꺼낸다(<c>stash branch</c>, 성공하면 stash 삭제).</summary>
        Branch
    }

    /// <summary>stash 명령 인수. 버튼 기본값과 ▾ 대화상자가 같은 함수를 쓴다.</summary>
    public static class GitStashCommands
    {
        /// <summary><see cref="GitOutputParser.ParseStashes"/>와 짝: 참조, 해시, 부모들, 시각, 제목.</summary>
        public static readonly string[] List = { "stash", "list", "-z", "--format=%gd%x1f%H%x1f%P%x1f%at%x1f%gs" };

        /// <summary>
        /// stash 저장. <paramref name="usePathspecStdin"/>이면 경로 목록을 표준 입력(NUL 구분)으로 받아 그 파일만 저장한다.
        /// </summary>
        public static List<string> Push(GitStashPushOptions options, bool usePathspecStdin = false)
        {
            var args = new List<string> { "stash", "push" };
            if (options.IncludeUntracked)
                args.Add("--include-untracked");
            if (options.KeepIndex)
                args.Add("--keep-index");
            if (!string.IsNullOrWhiteSpace(options.Message))
            {
                args.Add("-m");
                args.Add(options.Message.Trim());
            }
            if (usePathspecStdin)
            {
                args.Add("--pathspec-from-file=-");
                args.Add("--pathspec-file-nul");
            }
            return args;
        }

        /// <param name="restoreIndex">스테이지 상태까지 되살린다(<c>--index</c>). 충돌 가능성이 더 높다.</param>
        public static List<string> Apply(string stashRef, GitStashApplyMode mode, bool restoreIndex, string branchName = null)
        {
            if (mode == GitStashApplyMode.Branch)
                return new List<string> { "stash", "branch", branchName, stashRef };

            var args = new List<string> { "stash", mode == GitStashApplyMode.Pop ? "pop" : "apply" };
            if (restoreIndex)
                args.Add("--index");
            args.Add(stashRef);
            return args;
        }

        public static List<string> Drop(string stashRef) => new List<string> { "stash", "drop", stashRef };

        public static readonly string[] Clear = { "stash", "clear" };

        /// <summary>stash에 담긴 추적 안 되는 파일 목록(세 번째 부모 커밋의 트리).</summary>
        public static List<string> UntrackedFiles(string stashHash) =>
            new List<string> { "ls-tree", "-r", "--name-only", "-z", stashHash + "^3" };
    }
}

using System.Collections.Generic;

namespace Folderss.Services
{
    public enum GitResetMode
    {
        /// <summary>HEAD만 옮긴다. 변경은 스테이지된 채로 남는다.</summary>
        Soft,
        /// <summary>HEAD와 인덱스를 옮긴다. 변경은 작업 트리에 남는다(git 기본값).</summary>
        Mixed,
        /// <summary>HEAD·인덱스·작업 트리를 모두 맞춘다. 커밋 안 한 변경이 사라진다(되돌릴 수 없음).</summary>
        Hard
    }

    /// <summary>
    /// reset·브랜치·체크아웃·워킹트리 명령 인수. UI와 테스트가 같은 인수를 쓰도록 한 곳에 둔다.
    /// 사용자 입력(브랜치 이름·경로)은 항상 옵션이 아닌 인수 자리에 오도록 순서를 고정하고, 이름은 먼저 <see cref="CheckBranchName"/>로 검사한다.
    /// </summary>
    public static class GitRefCommands
    {
        public static List<string> Reset(GitResetMode mode, string target)
        {
            var flag = mode == GitResetMode.Soft ? "--soft" : mode == GitResetMode.Hard ? "--hard" : "--mixed";
            return new List<string> { "reset", flag, target, "--" };
        }

        /// <summary>git 자체 규칙으로 브랜치 이름을 검사한다(성공 = 쓸 수 있는 이름). '-'로 시작하는 이름도 여기서 걸러진다.</summary>
        public static List<string> CheckBranchName(string name)
        {
            return new List<string> { "check-ref-format", "--branch", name };
        }

        /// <summary>새 브랜치. 전환하면 <c>switch -c</c>, 아니면 <c>branch</c>. 시작점이 비면 현재 HEAD.</summary>
        public static List<string> CreateBranch(string name, string startPoint, bool switchAfter)
        {
            var args = switchAfter
                ? new List<string> { "switch", "-c", name }
                : new List<string> { "branch", "--", name };
            if (!string.IsNullOrEmpty(startPoint))
                args.Add(startPoint);
            return args;
        }

        /// <summary>브랜치 없이 커밋으로 이동(detached HEAD).</summary>
        public static List<string> CheckoutDetached(string commit)
        {
            return new List<string> { "switch", "--detach", commit };
        }

        public static readonly string[] WorktreeList = { "worktree", "list", "--porcelain" };

        /// <summary>
        /// 워킹트리 추가. <paramref name="newBranch"/>가 있으면 그 이름으로 새 브랜치를 <paramref name="commitish"/>(비면 HEAD)에서 만든다.
        /// 없으면 <paramref name="commitish"/>(기존 브랜치)를 체크아웃한다 — 다른 워킹트리에서 쓰는 브랜치면 git이 거부한다.
        /// </summary>
        public static List<string> WorktreeAdd(string path, string newBranch, string commitish)
        {
            var args = new List<string> { "worktree", "add" };
            if (!string.IsNullOrEmpty(newBranch))
            {
                args.Add("-b");
                args.Add(newBranch);
            }
            args.Add("--");
            args.Add(path);
            if (!string.IsNullOrEmpty(commitish))
                args.Add(commitish);
            return args;
        }

        /// <summary>
        /// 워킹트리 제거. 기본은 커밋 안 한 변경이 있으면 git이 거부한다.
        /// <paramref name="force"/>면 그 변경까지 지운다(<c>--force</c>, 되돌릴 수 없음 — ▾ 옵션에서 경고·확인 후에만).
        /// </summary>
        public static List<string> WorktreeRemove(string path, bool force = false)
        {
            var args = new List<string> { "worktree", "remove" };
            if (force)
                args.Add("--force");
            args.Add("--");
            args.Add(path);
            return args;
        }

        /// <summary>폴더가 지워진 워킹트리의 기록을 정리한다.</summary>
        public static readonly string[] WorktreePrune = { "worktree", "prune", "-v" };
    }
}

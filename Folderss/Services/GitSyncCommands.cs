using System.Collections.Generic;

namespace Folderss.Services
{
    /// <summary>pull 옵션. 기본값은 설정(<see cref="GitSettings.PullMode"/>)에서 온다.</summary>
    public sealed class GitPullOptions
    {
        public GitPullMode Mode { get; set; } = GitPullMode.FastForwardOnly;

        /// <summary>커밋 안 한 변경을 잠시 stash했다가 끝나면 되돌린다(<c>--autostash</c>, 병합·rebase일 때만 의미 있음).</summary>
        public bool AutoStash { get; set; }
    }

    public sealed class GitPushOptions
    {
        public string Remote { get; set; }
        public string Branch { get; set; }

        /// <summary>이 원격 브랜치를 upstream으로 설정(<c>-u</c>).</summary>
        public bool SetUpstream { get; set; }

        /// <summary>보내는 커밋에 달린 주석 태그도 함께(<c>--follow-tags</c>).</summary>
        public bool FollowTags { get; set; }
    }

    public sealed class GitFetchOptions
    {
        /// <summary>원격에서 지워진 브랜치의 추적 참조를 정리(<c>--prune</c>). 기본 켬(기존 동작).</summary>
        public bool Prune { get; set; } = true;

        /// <summary>모든 원격(<c>--all</c>). 끄면 기본 원격만.</summary>
        public bool AllRemotes { get; set; }

        /// <summary>모든 태그도 받기(<c>--tags</c>).</summary>
        public bool Tags { get; set; }
    }

    public sealed class GitCommitOptions
    {
        /// <summary>직전 커밋을 고친다(<c>--amend</c>). 메시지가 비면 직전 메시지를 그대로 쓴다(<c>--no-edit</c>).</summary>
        public bool Amend { get; set; }

        /// <summary>Signed-off-by 줄 추가(<c>-s</c>).</summary>
        public bool SignOff { get; set; }

        /// <summary>스테이지된 변경이 없어도 커밋(<c>--allow-empty</c>).</summary>
        public bool AllowEmpty { get; set; }
    }

    /// <summary>
    /// pull·push·fetch·커밋·브랜치 삭제 인수. 버튼은 기본 옵션으로 바로 실행하고, 옆의 ▾ 대화상자에서 고른 옵션도 같은 함수로 인수를 만든다.
    /// 강제 푸시는 만들지 않는다(원격의 다른 사람 커밋을 덮어쓸 수 있음).
    /// </summary>
    public static class GitSyncCommands
    {
        public static List<string> Pull(GitPullOptions options)
        {
            var args = new List<string> { "pull" };
            switch (options.Mode)
            {
                case GitPullMode.FastForwardOnly: args.Add("--ff-only"); break;
                case GitPullMode.Merge: args.Add("--no-rebase"); break;
                case GitPullMode.Rebase: args.Add("--rebase"); break;
            }
            if (options.AutoStash)
                args.Add("--autostash");
            return args;
        }

        /// <summary>원격·브랜치를 지정하지 않으면 현재 브랜치의 upstream으로 push.</summary>
        public static List<string> Push(GitPushOptions options)
        {
            var args = new List<string> { "push" };
            if (options.SetUpstream)
                args.Add("-u");
            if (options.FollowTags)
                args.Add("--follow-tags");
            if (!string.IsNullOrEmpty(options.Remote))
            {
                args.Add("--");
                args.Add(options.Remote);
                if (!string.IsNullOrEmpty(options.Branch))
                    args.Add(options.Branch);
            }
            return args;
        }

        public static List<string> Fetch(GitFetchOptions options)
        {
            var args = new List<string> { "fetch" };
            if (options.Prune)
                args.Add("--prune");
            if (options.AllRemotes)
                args.Add("--all");
            if (options.Tags)
                args.Add("--tags");
            return args;
        }

        /// <param name="messageFile">커밋 메시지 파일. null이면 amend에서 직전 메시지를 유지(<c>--no-edit</c>).</param>
        public static List<string> Commit(string messageFile, GitCommitOptions options)
        {
            var args = new List<string> { "commit" };
            if (options.Amend)
                args.Add("--amend");
            if (options.SignOff)
                args.Add("-s");
            if (options.AllowEmpty)
                args.Add("--allow-empty");
            if (messageFile != null)
            {
                args.Add("-F");
                args.Add(messageFile);
            }
            else if (options.Amend)
            {
                args.Add("--no-edit");
            }
            return args;
        }

        /// <summary>브랜치 삭제. <paramref name="force"/>면 병합 안 된 커밋이 있어도 지운다(<c>-D</c>, reflog로만 복구).</summary>
        public static List<string> DeleteBranch(string name, bool force)
        {
            return new List<string> { "branch", force ? "-D" : "-d", "--", name };
        }

        public static readonly string[] Remotes = { "remote" };
    }
}

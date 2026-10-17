using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Folderss.Models;
using LibGit2Sharp;

namespace Folderss.Services
{
    /// <summary>
    /// Git 창의 조회(브랜치·로그·upstream 비교·stash·워킹트리)를 git 프로세스 없이 LibGit2Sharp로 읽는다.
    /// 회사 PC처럼 프로세스 실행이 1초 넘게 걸리는 환경에서 저장소 선택이 수 초 걸리던 문제용(docs/items/git-performance.md).
    /// - 결과는 <see cref="GitOutputParser"/>가 CLI 출력에서 만드는 것과 같아야 한다(GitLibReaderTests가 비교).
    /// - 저장소는 호출마다 열고 닫는다. 열어 두면 팩 파일이 잠겨 콘솔의 git gc/repack과 부딪히고, Repository는 스레드 안전하지 않다.
    /// - 같게 만들 수 없는 경우(링크된 워킹트리, upstream 없음 등)는 <see cref="NotSupportedException"/>을 던진다. 호출자는 CLI로 다시 읽는다
    ///   (그 밖의 예외도 마찬가지로 CLI로 다시 읽되, 예상 밖이므로 출력창에 알린다).
    /// 쓰기·네트워크는 여기서 다루지 않는다(계속 git CLI). diff는 <see cref="GitLibDiff"/>가 같은 방식으로 만든다.
    /// </summary>
    public static class GitLibReader
    {
        /// <summary>
        /// <c>git status --porcelain=v2 --branch -z --untracked-files=all</c> + <see cref="GitOutputParser.ParseStatusV2"/>와 같은 스냅숏.
        /// git과 결과가 갈릴 수 있는 저장소(서브모듈, 인덱스 v3 이상 = skip-worktree·intent-to-add, sparse checkout,
        /// .gitattributes의 filter=(LFS 등 외부 필터를 libgit2는 실행하지 않음), 복사 감지)는 <see cref="NotSupportedException"/>.
        /// 조회만 하고 인덱스 갱신은 저장하지 않는다(CLI의 GIT_OPTIONAL_LOCKS=0과 같음).
        /// </summary>
        public static GitStatusSnapshot ReadStatus(string root)
        {
            using (var repo = new Repository(root))
            {
                EnsureStatusSupported(repo);

                var snapshot = new GitStatusSnapshot
                {
                    HeadOid = repo.Head.Tip?.Sha,
                    IsDetached = repo.Info.IsHeadDetached
                };
                if (!snapshot.IsDetached)
                {
                    snapshot.Branch = repo.Head.CanonicalName.StartsWith("refs/heads/", StringComparison.Ordinal)
                        ? repo.Head.CanonicalName.Substring("refs/heads/".Length)
                        : repo.Head.FriendlyName;
                    var upstream = repo.Head.TrackedBranch;
                    if (upstream != null)
                    {
                        // 원격 추적 브랜치가 사라졌어도(gone) git은 upstream 이름은 내고 ahead/behind는 내지 않는다.
                        snapshot.Upstream = upstream.FriendlyName;
                        if (upstream.Tip != null && repo.Head.Tip != null)
                        {
                            snapshot.Ahead = repo.Head.TrackingDetails.AheadBy ?? 0;
                            snapshot.Behind = repo.Head.TrackingDetails.BehindBy ?? 0;
                        }
                    }
                }

                var options = new StatusOptions
                {
                    Show = StatusShowOption.IndexAndWorkDir,
                    IncludeUntracked = true,
                    RecurseUntrackedDirs = true,
                    IncludeIgnored = false,
                    DetectRenamesInIndex = RenamesEnabled(repo),
                    DetectRenamesInWorkDir = false,
                    ExcludeSubmodules = true
                };
                var tracked = new List<GitStatusEntry>();
                var untracked = new List<GitStatusEntry>();
                foreach (var item in repo.RetrieveStatus(options))
                {
                    var state = item.State;
                    if (state.HasFlag(FileStatus.Conflicted))
                    {
                        var code = ConflictCode(repo, item.FilePath);
                        tracked.Add(new GitStatusEntry { Path = item.FilePath, IsConflicted = true, IndexState = code[0], WorkTreeState = code[1] });
                        continue;
                    }

                    var x = state.HasFlag(FileStatus.RenamedInIndex) ? 'R'
                        : state.HasFlag(FileStatus.NewInIndex) ? 'A'
                        : state.HasFlag(FileStatus.DeletedFromIndex) ? 'D'
                        : state.HasFlag(FileStatus.TypeChangeInIndex) ? 'T'
                        : state.HasFlag(FileStatus.ModifiedInIndex) ? 'M'
                        : '.';
                    var y = state.HasFlag(FileStatus.DeletedFromWorkdir) ? 'D'
                        : state.HasFlag(FileStatus.TypeChangeInWorkdir) ? 'T'
                        : state.HasFlag(FileStatus.ModifiedInWorkdir) ? 'M'
                        : '.';
                    if (x != '.' || y != '.')
                    {
                        var entry = new GitStatusEntry { Path = item.FilePath, IndexState = x, WorkTreeState = y };
                        if (x == 'R' && item.HeadToIndexRenameDetails != null)
                        {
                            entry.Path = item.HeadToIndexRenameDetails.NewFilePath;
                            entry.OriginalPath = item.HeadToIndexRenameDetails.OldFilePath;
                        }
                        tracked.Add(entry);
                    }
                    // 인덱스에서 지운 파일이 작업 트리에 남아 있으면(git rm --cached) git은 추적 항목과 "?" 항목을 따로 낸다.
                    if (state.HasFlag(FileStatus.NewInWorkdir))
                        untracked.Add(new GitStatusEntry { Path = item.FilePath, IsUntracked = true });
                }

                // git 순서: 추적 항목(1·2·u) 경로 순 → 추적 안 되는 항목 경로 순. 경로 비교는 UTF-8 바이트 순(strcmp).
                snapshot.Entries.AddRange(tracked.OrderBy(e => e.Path, Utf8Order));
                snapshot.Entries.AddRange(untracked.OrderBy(e => e.Path, Utf8Order));
                return snapshot;
            }
        }

        internal static void EnsureStatusSupported(Repository repo)
        {
            var gitDir = repo.Info.Path;
            var indexPath = Path.Combine(gitDir, "index");
            if (File.Exists(indexPath))
            {
                var header = new byte[8];
                using (var stream = new FileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (stream.Read(header, 0, 8) == 8 && header[7] != 2)
                        throw new NotSupportedException("인덱스 v" + header[7]);
                }
            }
            if (repo.Config.Get<bool>("core.sparseCheckout")?.Value == true)
                throw new NotSupportedException("sparse checkout");
            var workDir = repo.Info.WorkingDirectory ?? throw new NotSupportedException("bare 저장소");
            if (File.Exists(Path.Combine(workDir, ".gitmodules")) || repo.Index.Any(e => e.Mode == Mode.GitLink))
                throw new NotSupportedException("서브모듈");
            foreach (var attributes in new[] { Path.Combine(workDir, ".gitattributes"), Path.Combine(gitDir, "info", "attributes") })
            {
                if (File.Exists(attributes) && File.ReadAllText(attributes).Contains("filter="))
                    throw new NotSupportedException("필터 속성(filter=)");
            }
        }

        /// <summary>status.renames(없으면 diff.renames, 기본 true). copies면 git이 복사(C)도 찾으므로 지원하지 않는다.</summary>
        private static bool RenamesEnabled(Repository repo)
        {
            var value = (repo.Config.Get<string>("status.renames")?.Value ?? repo.Config.Get<string>("diff.renames")?.Value ?? "true").Trim().ToLowerInvariant();
            if (value == "copies" || value == "copy")
                throw new NotSupportedException("복사 감지(" + value + ")");
            return !(value == "false" || value == "no" || value == "off" || value == "0");
        }

        /// <summary>충돌 단계(1=공통 조상, 2=우리, 3=그들) 조합 → porcelain XY. git wt-status.c의 unmerged 표와 같다.</summary>
        private static string ConflictCode(Repository repo, string path)
        {
            var conflict = repo.Index.Conflicts[path];
            var mask = (conflict?.Ancestor != null ? 1 : 0) | (conflict?.Ours != null ? 2 : 0) | (conflict?.Theirs != null ? 4 : 0);
            switch (mask)
            {
                case 1: return "DD";
                case 2: return "AU";
                case 3: return "UD";
                case 4: return "UA";
                case 5: return "DU";
                case 6: return "AA";
                default: return "UU";
            }
        }

        private static readonly IComparer<string> Utf8Order = Comparer<string>.Create((a, b) =>
        {
            var x = System.Text.Encoding.UTF8.GetBytes(a);
            var y = System.Text.Encoding.UTF8.GetBytes(b);
            for (var i = 0; i < Math.Min(x.Length, y.Length); i++)
                if (x[i] != y[i])
                    return x[i].CompareTo(y[i]);
            return x.Length.CompareTo(y.Length);
        });

        /// <summary><c>git for-each-ref refs/heads refs/remotes</c> + <see cref="GitOutputParser.ParseBranches"/>와 같은 목록.</summary>
        public static List<GitBranchInfo> ReadBranches(string root)
        {
            using (var repo = new Repository(root))
            {
                var list = new List<GitBranchInfo>();
                foreach (var branch in repo.Branches.OrderBy(b => b.CanonicalName, StringComparer.Ordinal))
                {
                    var name = branch.CanonicalName;
                    GitBranchInfo info;
                    if (name.StartsWith("refs/heads/", StringComparison.Ordinal))
                    {
                        info = new GitBranchInfo { Name = name.Substring("refs/heads/".Length) };
                        info.Upstream = branch.TrackedBranch?.FriendlyName ?? string.Empty;
                    }
                    else if (name.StartsWith("refs/remotes/", StringComparison.Ordinal))
                    {
                        if (name.EndsWith("/HEAD", StringComparison.Ordinal))
                            continue;
                        info = new GitBranchInfo { Name = name.Substring("refs/remotes/".Length), IsRemote = true, Upstream = string.Empty };
                    }
                    else
                    {
                        continue;
                    }
                    info.IsCurrent = branch.IsCurrentRepositoryHead;
                    info.Subject = branch.Tip?.MessageShort ?? string.Empty;
                    list.Add(info);
                }
                return list;
            }
        }

        /// <summary><c>git log --topo-order -n limit [--all]</c> + <see cref="GitOutputParser.LogFormat"/>와 같은 목록.</summary>
        public static List<GitCommitInfo> ReadLog(string root, int limit, bool allRefs)
        {
            using (var repo = new Repository(root))
            {
                var starts = new List<Commit>();
                if (allRefs)
                {
                    // --all: refs/ 아래 모든 참조(이름 순) + HEAD. notes·사용자 정의 참조 포함, 커밋이 아닌 것(트리·blob 태그)은 건너뛴다.
                    foreach (var reference in repo.Refs.OrderBy(r => r.CanonicalName, StringComparer.Ordinal))
                    {
                        var commit = PeelToCommit(repo, reference);
                        if (commit != null)
                            starts.Add(commit);
                    }
                }
                if (repo.Head.Tip != null)
                    starts.Add(repo.Head.Tip);
                if (starts.Count == 0)
                    return new List<GitCommitInfo>();

                var reachable = repo.Commits.QueryBy(new CommitFilter { IncludeReachableFrom = starts, SortBy = CommitSortStrategies.None })
                    .ToDictionary(c => c.Sha, StringComparer.Ordinal);
                return ToInfos(repo, TopoOrder(starts, reachable).Take(limit));
            }
        }

        /// <summary>
        /// git <c>--topo-order</c>(sort_in_topological_order, REV_SORT_IN_GRAPH_ORDER)를 그대로 옮긴 것.
        /// libgit2의 위상 정렬도 올바르지만 merge 뒤 어느 부모 줄기를 먼저 내는지가 달라 그래프가 콘솔 git과 어긋난다.
        /// - 집합 안 자식이 없는 커밋(tip)을 커밋 날짜 내림차순(같으면 시작 순서)으로 늘어놓고, 스택(LIFO)으로 꺼낸다.
        /// - 커밋을 꺼내면 부모의 남은 자식 수를 줄이고, 0이 된 부모를 부모 순서대로 스택에 넣는다 → 마지막 부모 줄기가 먼저 나온다.
        /// </summary>
        private static IEnumerable<Commit> TopoOrder(List<Commit> starts, Dictionary<string, Commit> reachable)
        {
            var children = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var commit in reachable.Values)
                foreach (var parent in commit.Parents)
                    if (reachable.ContainsKey(parent.Sha))
                        children[parent.Sha] = children.TryGetValue(parent.Sha, out var n) ? n + 1 : 1;

            var tips = starts
                .Where(c => !children.ContainsKey(c.Sha))
                .GroupBy(c => c.Sha, StringComparer.Ordinal).Select(g => g.First())
                .Select((c, order) => (Commit: c, Order: order))
                .OrderByDescending(t => t.Commit.Committer.When.ToUnixTimeSeconds()).ThenBy(t => t.Order)
                .Select(t => t.Commit)
                .ToList();

            var stack = new Stack<Commit>();
            for (var i = tips.Count - 1; i >= 0; i--)
                stack.Push(tips[i]);
            while (stack.Count > 0)
            {
                var commit = stack.Pop();
                foreach (var parent in commit.Parents)
                {
                    if (!children.TryGetValue(parent.Sha, out var left))
                        continue;
                    if (left == 1)
                    {
                        children.Remove(parent.Sha);
                        stack.Push(reachable[parent.Sha]);
                    }
                    else
                    {
                        children[parent.Sha] = left - 1;
                    }
                }
                yield return commit;
            }
        }

        /// <summary>
        /// 현재 브랜치와 upstream의 차이: 보낼 커밋(<c>@{u}..HEAD</c>)과 받을 커밋(<c>HEAD..@{u}</c>), 각각 최대 <paramref name="limit"/>개.
        /// upstream이 없거나 원격 추적 브랜치가 없으면(gone) 예외 — CLI가 그 오류 문구를 그대로 보여 준다.
        /// </summary>
        public static (List<GitCommitInfo> Outgoing, List<GitCommitInfo> Incoming) ReadUpstreamRanges(string root, int limit)
        {
            using (var repo = new Repository(root))
            {
                var head = repo.Head.Tip ?? throw new NotSupportedException("HEAD 커밋 없음");
                var upstream = repo.Head.TrackedBranch?.Tip ?? throw new NotSupportedException("upstream 커밋 없음");
                return (Range(repo, upstream, head, limit), Range(repo, head, upstream, limit));
            }
        }

        private static List<GitCommitInfo> Range(Repository repo, Commit exclude, Commit include, int limit)
        {
            var inRange = repo.Commits.QueryBy(new CommitFilter
                {
                    IncludeReachableFrom = include,
                    ExcludeReachableFrom = exclude,
                    SortBy = CommitSortStrategies.None
                })
                .ToDictionary(c => c.Sha, StringComparer.Ordinal);
            return ToInfos(repo, DateOrder(include, inRange).Take(limit));
        }

        /// <summary>
        /// git <c>log a..b</c> 기본 순서(커밋 날짜 우선순위 큐, 같은 날짜는 먼저 넣은 것 먼저)를 그대로 옮긴 것.
        /// libgit2의 <see cref="CommitSortStrategies.Time"/>은 같은 초에 만든 커밋끼리 순서가 달라(부모가 자식보다 먼저 나오기도 함) 콘솔 git과 어긋난다.
        /// </summary>
        private static IEnumerable<Commit> DateOrder(Commit start, Dictionary<string, Commit> inRange)
        {
            if (!inRange.ContainsKey(start.Sha))
                yield break;
            var queued = new HashSet<string>(StringComparer.Ordinal) { start.Sha };
            var queue = new SortedSet<(long Time, long Order, Commit Commit)>(
                Comparer<(long Time, long Order, Commit Commit)>.Create((a, b) =>
                    a.Time != b.Time ? b.Time.CompareTo(a.Time) : a.Order.CompareTo(b.Order)));
            long order = 0;
            queue.Add((start.Committer.When.ToUnixTimeSeconds(), order++, start));
            while (queue.Count > 0)
            {
                var next = queue.Min;
                queue.Remove(next);
                foreach (var parent in next.Commit.Parents)
                    if (inRange.TryGetValue(parent.Sha, out var commit) && queued.Add(parent.Sha))
                        queue.Add((commit.Committer.When.ToUnixTimeSeconds(), order++, commit));
                yield return next.Commit;
            }
        }

        /// <summary><c>git stash list</c> + <see cref="GitStashCommands.List"/> 형식과 같은 목록(최신이 stash@{0}).</summary>
        public static List<GitStashInfo> ReadStashes(string root)
        {
            using (var repo = new Repository(root))
            {
                var list = new List<GitStashInfo>();
                if (repo.Refs["refs/stash"] == null)
                    return list;
                var index = 0;
                foreach (var entry in repo.Refs.Log("refs/stash"))
                {
                    var commit = repo.Lookup<Commit>(entry.To);
                    list.Add(new GitStashInfo
                    {
                        Ref = "stash@{" + index++ + "}",
                        Hash = entry.To.Sha,
                        Parents = commit?.Parents.Select(p => p.Sha).ToArray() ?? Array.Empty<string>(),
                        Time = commit == null ? default : AuthorTime(commit),
                        Subject = (entry.Message ?? string.Empty).TrimEnd('\n')
                    });
                }
                return list;
            }
        }

        /// <summary>
        /// <c>git worktree list --porcelain</c>과 같은 목록. 링크된 워킹트리가 없을 때(주 작업 트리 하나)만 다룬다 —
        /// 링크된 워킹트리의 잠김·prune 판정은 git 내부 규칙이라 흉내 내지 않고 예외를 던져 CLI로 읽게 한다.
        /// </summary>
        public static List<GitWorktreeInfo> ReadWorktrees(string root)
        {
            using (var repo = new Repository(root))
            {
                if (repo.Info.IsBare || repo.Info.WorkingDirectory == null)
                    throw new NotSupportedException("bare 저장소");
                var gitDir = repo.Info.Path.TrimEnd('\\', '/');
                // 링크된 워킹트리 자신이면 .git이 파일이고 gitdir이 <공통>/worktrees/<이름> 아래다.
                if (File.Exists(Path.Combine(repo.Info.WorkingDirectory, ".git")))
                    throw new NotSupportedException("링크된 워킹트리");
                var linked = Path.Combine(gitDir, "worktrees");
                if (Directory.Exists(linked) && Directory.EnumerateFileSystemEntries(linked).Any())
                    throw new NotSupportedException("링크된 워킹트리 있음");

                var info = new GitWorktreeInfo
                {
                    // git은 Windows에서도 슬래시로, 끝 구분자 없이 낸다.
                    Path = repo.Info.WorkingDirectory.TrimEnd('\\', '/').Replace('\\', '/'),
                    Head = repo.Head.Tip?.Sha ?? new string('0', 40),
                    IsDetached = repo.Info.IsHeadDetached,
                    IsMain = true
                };
                if (!info.IsDetached)
                    info.Branch = repo.Head.CanonicalName.StartsWith("refs/heads/", StringComparison.Ordinal)
                        ? repo.Head.CanonicalName.Substring("refs/heads/".Length)
                        : repo.Head.CanonicalName;
                return new List<GitWorktreeInfo> { info };
            }
        }

        private static List<GitCommitInfo> ToInfos(Repository repo, IEnumerable<Commit> commits)
        {
            var decorations = Decorations(repo);
            return commits.Select(c => new GitCommitInfo
            {
                Hash = c.Sha,
                Parents = c.Parents.Select(p => p.Sha).ToArray(),
                Author = c.Author.Name,
                Time = AuthorTime(c),
                Refs = decorations.TryGetValue(c.Sha, out var refs) ? refs : string.Empty,
                Subject = c.MessageShort
            }).ToList();
        }

        /// <summary>
        /// 커밋별 <c>%D</c> 문자열. git 2.53 기본 규칙: HEAD·refs/heads·refs/remotes·refs/tags·refs/stash만,
        /// 참조 이름 역순(바이트 순), HEAD가 가리키면 맨 앞(브랜치면 "HEAD -> 이름"으로 합침), 태그는 "tag: ".
        /// </summary>
        private static Dictionary<string, string> Decorations(Repository repo)
        {
            var byCommit = new Dictionary<string, List<(string RefName, string Label)>>(StringComparer.Ordinal);
            foreach (var reference in repo.Refs)
            {
                var name = reference.CanonicalName;
                string label;
                if (name.StartsWith("refs/heads/", StringComparison.Ordinal))
                    label = name.Substring("refs/heads/".Length);
                else if (name.StartsWith("refs/remotes/", StringComparison.Ordinal))
                    label = name.Substring("refs/remotes/".Length);
                else if (name.StartsWith("refs/tags/", StringComparison.Ordinal))
                    label = "tag: " + name.Substring("refs/tags/".Length);
                else if (name == "refs/stash")
                    label = name;
                else
                    continue;

                var commit = PeelToCommit(repo, reference);
                if (commit == null)
                    continue;
                if (!byCommit.TryGetValue(commit.Sha, out var list))
                    byCommit[commit.Sha] = list = new List<(string, string)>();
                list.Add((name, label));
            }

            var headSha = repo.Head.Tip?.Sha;
            var headBranch = repo.Info.IsHeadDetached ? null : repo.Head.CanonicalName;
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in byCommit)
            {
                var labels = pair.Value.OrderByDescending(r => r.RefName, StringComparer.Ordinal).ToList();
                var parts = new List<string>();
                if (pair.Key == headSha)
                {
                    var current = labels.FindIndex(r => r.RefName == headBranch);
                    if (current >= 0)
                    {
                        parts.Add("HEAD -> " + labels[current].Label);
                        labels.RemoveAt(current);
                    }
                    else
                    {
                        parts.Add("HEAD");
                    }
                }
                parts.AddRange(labels.Select(r => r.Label));
                result[pair.Key] = string.Join(", ", parts);
            }
            // 참조는 없고 HEAD만 가리키는 커밋(detached).
            if (headSha != null && !result.ContainsKey(headSha))
                result[headSha] = "HEAD";
            return result;
        }

        private static Commit PeelToCommit(Repository repo, Reference reference)
        {
            try
            {
                return reference.ResolveToDirectReference()?.Target?.Peel<Commit>();
            }
            catch (LibGit2SharpException)
            {
                return null;
            }
        }

        private static DateTimeOffset AuthorTime(Commit commit)
        {
            // %at는 초 단위 유닉스 시각이다. ParseLog도 FromUnixTimeSeconds로 만든다(오프셋 0).
            return DateTimeOffset.FromUnixTimeSeconds(commit.Author.When.ToUnixTimeSeconds());
        }
    }
}

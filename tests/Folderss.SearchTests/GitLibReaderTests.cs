using Folderss.Models;
using Folderss.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>
    /// GitLibReader(LibGit2Sharp)가 git CLI 출력 + GitOutputParser와 같은 결과를 내는지 실제 git으로 만든 저장소에서 비교한다.
    /// Git 창은 둘 중 하나로 읽으므로 결과가 다르면 화면이 콘솔 git과 어긋난다.
    /// </summary>
    public sealed class GitLibReaderTests : IDisposable
    {
        private readonly string _root;

        public GitLibReaderTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "folderss-lg2-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(_root, true);
            }
            catch { }
        }

        private static async Task<string> Git(string repo, params string[] args)
        {
            var result = await GitCommandRunner.RunAsync(repo, args, GitCommandRunner.QueryTimeout, CancellationToken.None);
            Assert.True(result.Success, string.Join(" ", args) + "\n" + result.StdErr);
            return result.StdOut;
        }

        private async Task<string> InitAsync(string name, bool bare = false)
        {
            var repo = Path.Combine(_root, name);
            Directory.CreateDirectory(repo);
            if (bare)
                await Git(repo, "init", "-q", "--bare", "-b", "main");
            else
                await Git(repo, "init", "-q", "-b", "main");
            await Configure(repo);
            return repo;
        }

        private static async Task Configure(string repo)
        {
            await Git(repo, "config", "user.name", "테스터");
            await Git(repo, "config", "user.email", "t@example.com");
            await Git(repo, "config", "commit.gpgsign", "false");
            await Git(repo, "config", "tag.gpgsign", "false");
            await Git(repo, "config", "core.autocrlf", "false");
        }

        private static async Task CommitAsync(string repo, string file, string message)
        {
            File.WriteAllText(Path.Combine(repo, file), message + Guid.NewGuid());
            await Git(repo, "add", "-A");
            await Git(repo, "commit", "-q", "-m", message);
        }

        // ── CLI 기준값(Git 창이 지금 쓰는 인수 그대로) ──────────────────────────

        private static async Task<List<GitBranchInfo>> CliBranches(string repo) =>
            GitOutputParser.ParseBranches(await Git(repo, "for-each-ref", GitOutputParser.BranchFormat, "refs/heads", "refs/remotes"));

        private static async Task<List<GitCommitInfo>> CliLog(string repo, int limit, bool all)
        {
            var args = new List<string> { "log", "--topo-order", "-z", "-n", limit.ToString(), GitOutputParser.LogFormat };
            if (all)
                args.Add("--all");
            return GitOutputParser.ParseLog(await Git(repo, args.ToArray()));
        }

        private static async Task<List<GitCommitInfo>> CliRange(string repo, string from, string to) =>
            GitOutputParser.ParseLog(await Git(repo, GitDiffCommands.LogRange(from, to, 500).ToArray()));

        private static async Task<List<GitStashInfo>> CliStashes(string repo) =>
            GitOutputParser.ParseStashes(await Git(repo, GitStashCommands.List));

        private static async Task<List<GitWorktreeInfo>> CliWorktrees(string repo) =>
            GitOutputParser.ParseWorktrees(await Git(repo, GitRefCommands.WorktreeList));

        // ── 비교용 한 줄 표현 ─────────────────────────────────────────────────

        private static List<string> Lines(IEnumerable<GitBranchInfo> list) =>
            list.OrderBy(b => b.IsRemote).ThenBy(b => b.Name, StringComparer.Ordinal)
                .Select(b => $"{b.Name}|{b.IsRemote}|{b.IsCurrent}|{b.Upstream}|{b.Subject}").ToList();

        private static List<string> Lines(IEnumerable<GitCommitInfo> list) =>
            list.Select(c => $"{c.Hash}|{string.Join(" ", c.Parents)}|{c.Author}|{c.Time.ToUnixTimeSeconds()}|{c.Refs}|{c.Subject}").ToList();

        private static List<string> Lines(IEnumerable<GitStashInfo> list) =>
            list.Select(s => $"{s.Ref}|{s.Hash}|{string.Join(" ", s.Parents)}|{s.Time.ToUnixTimeSeconds()}|{s.Subject}").ToList();

        private static List<string> Lines(IEnumerable<GitWorktreeInfo> list) =>
            list.Select(w => $"{w.Path}|{w.Head}|{w.Branch}|{w.IsDetached}|{w.IsBare}|{w.IsLocked}|{w.IsPrunable}|{w.IsMain}").ToList();

        /// <summary>
        /// 원격·클론, 양쪽 갈라진 커밋, merge, 가벼운·주석 태그, 원격 HEAD, 사라진 upstream, notes·사용자 참조,
        /// 여러 줄 메시지·한글, 일반·새 파일 포함 stash. 커밋은 빠르게 만들어 같은 초 시각이 많다(정렬 동점).
        /// </summary>
        private async Task<string> RichRepoAsync()
        {
            var origin = await InitAsync("origin", bare: true);
            var seed = await InitAsync("seed");
            await CommitAsync(seed, "a.txt", "첫 커밋");
            await Git(seed, "remote", "add", "origin", origin);
            await Git(seed, "push", "-q", "origin", "main");
            await Git(seed, "push", "-q", "origin", "main:gone");

            var work = Path.Combine(_root, "work");
            await Git(_root, "clone", "-q", origin, work);
            await Configure(work);

            // 원격 쪽에만 있는 커밋(받을 커밋)
            await CommitAsync(seed, "b.txt", "원격 커밋 1");
            await CommitAsync(seed, "b.txt", "원격 커밋 2");
            await Git(seed, "push", "-q", "origin", "main");
            await Git(work, "fetch", "-q", "origin");

            // 로컬 쪽: 갈라진 커밋, 기능 브랜치 merge, 여러 줄 메시지
            await CommitAsync(work, "c.txt", "로컬 1");
            await Git(work, "switch", "-q", "-c", "feature");
            await CommitAsync(work, "f.txt", "기능 1");
            await CommitAsync(work, "f.txt", "기능 2\n\n본문 줄");
            await Git(work, "switch", "-q", "main");
            await CommitAsync(work, "c.txt", "로컬 2\n이어지는 제목 줄");
            await Git(work, "merge", "-q", "--no-ff", "-m", "기능 merge", "feature");
            await Git(work, "switch", "-q", "-c", "side", "HEAD~2");
            await CommitAsync(work, "s.txt", "곁가지");
            await Git(work, "switch", "-q", "main");

            await Git(work, "tag", "v-light", "HEAD~1");
            await Git(work, "tag", "-a", "v-ann", "-m", "주석 태그", "HEAD");
            await Git(work, "update-ref", "refs/custom/x", "feature");
            await Git(work, "notes", "add", "-m", "노트", "HEAD");

            // upstream이 사라진 브랜치
            await Git(work, "branch", "-q", "--track", "orphan", "origin/gone");
            await Git(origin, "branch", "-D", "gone");
            await Git(work, "fetch", "-q", "--prune", "origin");

            // stash 둘: 일반, 새 파일 포함
            File.WriteAllText(Path.Combine(work, "a.txt"), "고침");
            await Git(work, "stash", "push", "-q", "-m", "일반 stash");
            File.WriteAllText(Path.Combine(work, "new.txt"), "새 파일");
            await Git(work, "stash", "push", "-q", "-u");
            return work;
        }

        [SkippableFact]
        public async Task Branches_MatchCli()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var repo = await RichRepoAsync();
            Assert.Equal(Lines(await CliBranches(repo)), Lines(GitLibReader.ReadBranches(repo)));
        }

        [SkippableFact]
        public async Task Log_MatchesCli_HeadAndAll()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var repo = await RichRepoAsync();
            Assert.Equal(Lines(await CliLog(repo, 300, false)), Lines(GitLibReader.ReadLog(repo, 300, false)));
            Assert.Equal(Lines(await CliLog(repo, 300, true)), Lines(GitLibReader.ReadLog(repo, 300, true)));
            // 한도에서 자르는 위치도 같아야 한다.
            Assert.Equal(Lines(await CliLog(repo, 3, true)), Lines(GitLibReader.ReadLog(repo, 3, true)));
        }

        [SkippableFact]
        public async Task Log_MatchesCli_Detached()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var repo = await RichRepoAsync();
            await Git(repo, "switch", "-q", "--detach", "HEAD~1");
            Assert.Equal(Lines(await CliLog(repo, 300, false)), Lines(GitLibReader.ReadLog(repo, 300, false)));
            Assert.Equal(Lines(await CliLog(repo, 300, true)), Lines(GitLibReader.ReadLog(repo, 300, true)));
            Assert.Equal(Lines(await CliBranches(repo)), Lines(GitLibReader.ReadBranches(repo)));
        }

        [SkippableFact]
        public async Task UpstreamRanges_MatchCli()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var repo = await RichRepoAsync();
            var (outgoing, incoming) = GitLibReader.ReadUpstreamRanges(repo, 500);
            Assert.NotEmpty(outgoing);
            Assert.NotEmpty(incoming);
            Assert.Equal(Lines(await CliRange(repo, GitDiffCommands.Upstream, "HEAD")), Lines(outgoing));
            Assert.Equal(Lines(await CliRange(repo, "HEAD", GitDiffCommands.Upstream)), Lines(incoming));
        }

        [SkippableFact]
        public async Task UpstreamRanges_Throw_WhenUpstreamGoneOrMissing()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var repo = await RichRepoAsync();
            await Git(repo, "switch", "-q", "orphan");      // upstream origin/gone이 사라짐 — CLI도 실패한다
            Assert.ThrowsAny<Exception>(() => GitLibReader.ReadUpstreamRanges(repo, 500));
            await Git(repo, "switch", "-q", "side");        // upstream 없음
            Assert.ThrowsAny<Exception>(() => GitLibReader.ReadUpstreamRanges(repo, 500));
        }

        [SkippableFact]
        public async Task Stashes_MatchCli()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var repo = await RichRepoAsync();
            var lib = GitLibReader.ReadStashes(repo);
            Assert.Equal(2, lib.Count);
            Assert.Equal(Lines(await CliStashes(repo)), Lines(lib));

            var empty = await InitAsync("nostash");
            await CommitAsync(empty, "a.txt", "하나");
            Assert.Empty(GitLibReader.ReadStashes(empty));
        }

        [SkippableFact]
        public async Task Worktrees_MatchCli_MainOnly_AndThrow_WhenLinked()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var repo = await RichRepoAsync();
            Assert.Equal(Lines(await CliWorktrees(repo)), Lines(GitLibReader.ReadWorktrees(repo)));

            await Git(repo, "switch", "-q", "--detach", "HEAD");
            Assert.Equal(Lines(await CliWorktrees(repo)), Lines(GitLibReader.ReadWorktrees(repo)));
            await Git(repo, "switch", "-q", "main");

            var linked = Path.Combine(_root, "linked-wt");
            await Git(repo, "worktree", "add", "-q", linked, "side");
            Assert.ThrowsAny<Exception>(() => GitLibReader.ReadWorktrees(repo));     // 주 저장소: 링크된 워킹트리 있음
            Assert.ThrowsAny<Exception>(() => GitLibReader.ReadWorktrees(linked));   // 링크된 워킹트리 자신
        }

        [SkippableFact]
        public async Task UnbornRepository_MatchesCli()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var repo = await InitAsync("unborn");
            Assert.Empty(GitLibReader.ReadBranches(repo));
            Assert.Empty(await CliBranches(repo));
            Assert.Empty(GitLibReader.ReadLog(repo, 300, true));      // --all인데 참조가 없음(CLI는 빈 출력)
            Assert.Equal(Lines(await CliWorktrees(repo)), Lines(GitLibReader.ReadWorktrees(repo)));
        }
    }
}

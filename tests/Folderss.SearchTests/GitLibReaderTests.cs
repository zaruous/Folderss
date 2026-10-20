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
    public sealed class GitLibReaderTests : IDisposable, IClassFixture<GitLibReaderTests.RichRepoFixture>
    {
        private readonly string _root;
        private readonly RichRepoFixture _rich;

        public GitLibReaderTests(RichRepoFixture rich)
        {
            _rich = rich;
            _root = Path.Combine(Path.GetTempPath(), "folderss-lg2-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose() => DeleteTree(_root);

        private static void DeleteTree(string root)
        {
            try
            {
                // git 객체 파일은 읽기 전용이라 먼저 속성을 푼다(Windows).
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(root, true);
            }
            catch { }
        }

        /// <summary>
        /// 여러 테스트가 쓰는 복잡한 저장소(<see cref="BuildRichRepoAsync"/>)를 클래스에서 한 번만 만든다.
        /// git 명령이 약 60번 드는데, 프로세스 실행이 1초 넘게 걸리는 PC에서는 테스트마다 만들면 이 클래스만 15분이 넘었다.
        /// 테스트는 <see cref="RichRepoAsync"/>로 파일 복사본을 받아 브랜치 전환·워킹트리 추가 등을 서로 영향 없이 한다.
        /// </summary>
        public sealed class RichRepoFixture : IAsyncLifetime
        {
            public string Root { get; } = Path.Combine(Path.GetTempPath(), "folderss-lg2-rich-" + Guid.NewGuid().ToString("N"));

            /// <summary>만든 작업 저장소. git이 없으면 null(테스트는 Skip).</summary>
            public string Work { get; private set; }

            public async Task InitializeAsync()
            {
                if (GitCommandRunner.FindGit() == null)
                    return;
                Directory.CreateDirectory(Root);
                Work = await BuildRichRepoAsync(Root);
            }

            public Task DisposeAsync()
            {
                DeleteTree(Root);
                return Task.CompletedTask;
            }
        }

        private static async Task<string> Git(string repo, params string[] args)
        {
            var result = await GitCommandRunner.RunAsync(repo, args, GitCommandRunner.QueryTimeout, CancellationToken.None);
            Assert.True(result.Success, string.Join(" ", args) + "\n" + result.StdErr);
            return result.StdOut;
        }

        private Task<string> InitAsync(string name, bool bare = false) => InitAsync(_root, name, bare);

        private static async Task<string> InitAsync(string root, string name, bool bare = false)
        {
            var repo = Path.Combine(root, name);
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
        /// <see cref="RichRepoFixture"/>가 만든 저장소를 이 테스트의 폴더로 복사해 돌려준다(git 프로세스 없이 파일 복사만).
        /// 원격(origin)은 복사하지 않고 픽스처 것을 그대로 가리킨다 — 테스트는 원격을 바꾸지 않는다.
        /// </summary>
        private Task<string> RichRepoAsync()
        {
            Skip.If(_rich.Work == null, "git이 설치되어 있지 않음");
            var work = Path.Combine(_root, "work");
            CopyTree(_rich.Work, work);
            return Task.FromResult(work);
        }

        private static void CopyTree(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
        }

        /// <summary>
        /// 원격·클론, 양쪽 갈라진 커밋, merge, 가벼운·주석 태그, 원격 HEAD, 사라진 upstream, notes·사용자 참조,
        /// 여러 줄 메시지·한글, 일반·새 파일 포함 stash. 커밋은 빠르게 만들어 같은 초 시각이 많다(정렬 동점).
        /// </summary>
        private static async Task<string> BuildRichRepoAsync(string root)
        {
            var origin = await InitAsync(root, "origin", bare: true);
            var seed = await InitAsync(root, "seed");
            await CommitAsync(seed, "a.txt", "첫 커밋");
            await Git(seed, "remote", "add", "origin", origin);
            await Git(seed, "push", "-q", "origin", "main");
            await Git(seed, "push", "-q", "origin", "main:gone");

            var work = Path.Combine(root, "work");
            await Git(root, "clone", "-q", origin, work);
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

        /// <summary>
        /// 로그 탭 파일 목록: <c>git diff-tree --name-status -M</c>(<see cref="GitDiffCommands.CommitFiles"/>)과 같은 목록·순서·상태.
        /// 정확한 이름 변경, 내용이 조금 바뀐 이름 변경(유사도 50% 이상), 기준 아래(삭제+추가), 실행 권한만 바뀜, 하위 폴더·한글 경로,
        /// 바이너리, 병합 커밋(첫 부모 기준), 최초 커밋.
        /// </summary>
        [SkippableFact]
        public async Task CommitFiles_MatchCli()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var repo = await InitAsync("commit-files");
            string Lines10(string tag) => string.Concat(Enumerable.Range(1, 10).Select(i => tag + " line " + i + "\n"));
            void Write(string path, string text)
            {
                var full = Path.Combine(repo, path);
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.WriteAllText(full, text);
            }
            Write("keep.txt", "keep\n");
            Write("exact.txt", Lines10("exact"));
            Write("similar.txt", Lines10("similar"));
            Write("below.txt", Lines10("below"));
            Write("gone.txt", "gone\n");
            Write("run.sh", "echo hi\n");
            File.WriteAllBytes(Path.Combine(repo, "bin.dat"), new byte[] { 0, 1, 2, 3, 0, 5 });
            await Git(repo, "add", "-A");
            await Git(repo, "commit", "-q", "-m", "root");

            Write("keep.txt", "keep 2\n");
            Directory.CreateDirectory(Path.Combine(repo, "moved"));
            File.Move(Path.Combine(repo, "exact.txt"), Path.Combine(repo, "moved", "exact.txt"));
            File.Delete(Path.Combine(repo, "similar.txt"));
            Write("renamed-similar.txt", Lines10("similar").Replace("similar line 3", "changed 3").Replace("similar line 7", "changed 7"));
            File.Delete(Path.Combine(repo, "below.txt"));
            Write("renamed-below.txt", Lines10("below").Replace("below line", "other") + "x\n");
            File.Delete(Path.Combine(repo, "gone.txt"));
            Write("새 폴더/한글.txt", "한글\n");
            File.WriteAllBytes(Path.Combine(repo, "bin.dat"), new byte[] { 0, 9, 9, 9, 0, 5 });
            await Git(repo, "add", "-A");
            await Git(repo, "update-index", "--chmod=+x", "run.sh");
            await Git(repo, "commit", "-q", "-m", "second");

            await Git(repo, "switch", "-q", "-c", "feature");
            Write("feature.txt", "feature\n");
            await Git(repo, "add", "-A");
            await Git(repo, "commit", "-q", "-m", "feature");
            await Git(repo, "switch", "-q", "main");
            Write("keep.txt", "keep 3\n");
            await Git(repo, "commit", "-q", "-am", "main");
            await Git(repo, "merge", "-q", "--no-ff", "-m", "merge", "feature");

            static string Line(GitStatusEntry e) => $"{e.IndexState}|{e.OriginalPath}|{e.Path}";
            var commits = await CliLog(repo, 100, false);
            Assert.Equal(5, commits.Count);
            foreach (var commit in commits)
            {
                var cli = GitOutputParser.ParseNameStatus(await Git(repo, GitDiffCommands.CommitFiles(commit.Hash, commit.Parents).ToArray()));
                var lib = GitLibReader.ReadCommitFiles(repo, commit.Hash, commit.Parents);
                Assert.Equal(cli.Select(Line).ToList(), lib.Select(Line).ToList());
            }

            // 위 비교가 의미 있도록 실제로 여러 상태가 나왔는지 확인한다.
            var second = commits.Single(c => c.Subject == "second");
            var kinds = GitLibReader.ReadCommitFiles(repo, second.Hash, second.Parents).Select(e => e.Kind).ToList();
            Assert.Contains(GitChangeKind.Renamed, kinds);
            Assert.Contains(GitChangeKind.Added, kinds);
            Assert.Contains(GitChangeKind.Deleted, kinds);
            Assert.Contains(GitChangeKind.Modified, kinds);
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
        public async Task UpstreamRanges_EmptyWhenUpToDate()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var repo = await RichRepoAsync();
            await Git(repo, "reset", "-q", "--hard", GitDiffCommands.Upstream);
            var (outgoing, incoming) = GitLibReader.ReadUpstreamRanges(repo, 500);
            Assert.Empty(outgoing);
            Assert.Empty(incoming);
            Assert.Empty(await CliRange(repo, GitDiffCommands.Upstream, "HEAD"));
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

        // ── status ─────────────────────────────────────────────────────────

        private static async Task<GitStatusSnapshot> CliStatus(string repo)
        {
            var result = await GitCommandRunner.RunAsync(repo, GitOutputParser.StatusArguments, GitCommandRunner.QueryTimeout, CancellationToken.None, readOnly: true);
            Assert.True(result.Success, result.StdErr);
            return GitOutputParser.ParseStatusV2(result.StdOut);
        }

        private static List<string> Lines(GitStatusSnapshot s)
        {
            var lines = new List<string> { $"#{s.Branch}|{s.Upstream}|+{s.Ahead}|-{s.Behind}|{s.IsDetached}|{s.HeadOid}" };
            lines.AddRange(s.Entries.Select(e => $"{e.IndexState}{e.WorkTreeState}|{e.Path}|{e.OriginalPath}|u={e.IsUntracked}|c={e.IsConflicted}"));
            return lines;
        }

        private static async Task AssertStatusMatches(string repo)
        {
            Assert.Equal(Lines(await CliStatus(repo)), Lines(GitLibReader.ReadStatus(repo)));
        }

        private static void Write(string repo, string path, string text)
        {
            var full = Path.Combine(repo, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, text);
        }

        private static readonly string Long = string.Join("\n", Enumerable.Range(1, 40).Select(i => "줄 " + i)) + "\n";

        [SkippableFact]
        public async Task Status_MatchesCli_WorkTreeIndexUntrackedRenames()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var repo = await InitAsync("status");
            foreach (var name in new[] { "a.txt", "b.txt", "c.txt", "d.txt", "gone.txt", "keep.txt", "한글 파일.txt", "sp ace.txt", "dir/x.txt" })
                Write(repo, name, name + "\n");
            Write(repo, "e.txt", Long);
            Write(repo, "f.txt", Long.Replace("줄", "행"));
            await Git(repo, "add", "-A");
            await Git(repo, "commit", "-q", "-m", "base");

            Write(repo, "a.txt", "작업 트리만\n");                       // .M
            Write(repo, "b.txt", "스테이지\n");                           // M.
            Write(repo, "c.txt", "스테이지\n");                           // MM
            await Git(repo, "add", "b.txt", "c.txt");
            Write(repo, "c.txt", "또 고침\n");
            await Git(repo, "rm", "-q", "d.txt");                         // D.
            Directory.CreateDirectory(Path.Combine(repo, "renamed"));    // git mv는 대상 폴더를 만들지 않는다
            await Git(repo, "mv", "e.txt", "renamed/e2.txt");             // R.
            await Git(repo, "mv", "f.txt", "f2.txt");                     // RM
            File.AppendAllText(Path.Combine(repo, "f2.txt"), "추가 줄\n");
            File.Delete(Path.Combine(repo, "gone.txt"));                  // .D
            await Git(repo, "rm", "-q", "--cached", "keep.txt");          // D. + ?
            Write(repo, "한글 파일.txt", "수정\n");
            Write(repo, "new staged.txt", "새 파일\n");                   // A.
            await Git(repo, "add", "new staged.txt");
            Write(repo, "u/v/w.txt", "추적 안 됨\n");                     // ? (하위 폴더 안까지)
            Write(repo, "새 폴더/파일.txt", "추적 안 됨\n");
            Write(repo, ".gitignore", "*.log\nbuild/\n");
            Write(repo, "x.log", "무시\n");
            Write(repo, "build/out.txt", "무시\n");

            await AssertStatusMatches(repo);
        }

        [SkippableFact]
        public async Task Status_MatchesCli_BranchHeader()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var repo = await RichRepoAsync();
            await AssertStatusMatches(repo);                              // ahead 3, behind 2
            await Git(repo, "switch", "-q", "orphan");                    // upstream 사라짐(gone)
            await AssertStatusMatches(repo);
            await Git(repo, "switch", "-q", "side");                      // upstream 없음
            await AssertStatusMatches(repo);
            await Git(repo, "switch", "-q", "--detach", "HEAD~1");        // detached
            await AssertStatusMatches(repo);

            var unborn = await InitAsync("status-unborn");                // 최초 커밋 전 + 스테이지된 파일
            Write(unborn, "first.txt", "처음\n");
            await Git(unborn, "add", "first.txt");
            Write(unborn, "later.txt", "추적 안 됨\n");
            await AssertStatusMatches(unborn);
        }

        [SkippableFact]
        public async Task Status_MatchesCli_MergeConflicts()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var repo = await InitAsync("conflict");
            Write(repo, "m.txt", "base\n");
            Write(repo, "du.txt", "base\n");
            Write(repo, "ud.txt", "base\n");
            await Git(repo, "add", "-A");
            await Git(repo, "commit", "-q", "-m", "base");

            await Git(repo, "switch", "-q", "-c", "other");
            Write(repo, "m.txt", "그들\n");
            Write(repo, "du.txt", "그들이 고침\n");
            File.Delete(Path.Combine(repo, "ud.txt"));
            Write(repo, "aa.txt", "그들이 추가\n");
            await Git(repo, "add", "-A");
            await Git(repo, "commit", "-q", "-m", "other");

            await Git(repo, "switch", "-q", "main");
            Write(repo, "m.txt", "우리\n");
            File.Delete(Path.Combine(repo, "du.txt"));
            Write(repo, "ud.txt", "우리가 고침\n");
            Write(repo, "aa.txt", "우리가 추가\n");
            await Git(repo, "add", "-A");
            await Git(repo, "commit", "-q", "-m", "ours");

            var merge = await GitCommandRunner.RunAsync(repo, new[] { "merge", "other" }, GitCommandRunner.QueryTimeout, CancellationToken.None);
            Assert.False(merge.Success);                                  // 충돌로 멈춤
            var lib = GitLibReader.ReadStatus(repo);
            Assert.Equal(new[] { "AA", "DU", "UD", "UU" }, lib.Entries.Where(e => e.IsConflicted).Select(e => $"{e.IndexState}{e.WorkTreeState}").OrderBy(x => x));
            await AssertStatusMatches(repo);
        }

        [SkippableFact]
        public async Task Status_MatchesCli_AutoCrlfAndNestedRepository()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var repo = await InitAsync("crlf");
            await Git(repo, "config", "core.autocrlf", "true");
            Write(repo, "lf.txt", "한 줄\n두 줄\n");
            await Git(repo, "add", "-A");
            await Git(repo, "commit", "-q", "-m", "base");
            // 내용은 같고 줄바꿈만 CRLF·수정 시각만 바뀜 → git은 변경 없음으로 본다.
            File.WriteAllText(Path.Combine(repo, "lf.txt"), "한 줄\r\n두 줄\r\n");
            File.SetLastWriteTimeUtc(Path.Combine(repo, "lf.txt"), DateTime.UtcNow.AddMinutes(5));

            var nested = Path.Combine(repo, "nested");                    // 저장소 안의 (서브모듈 아닌) 저장소
            Directory.CreateDirectory(nested);
            await Git(nested, "init", "-q");
            Write(nested, "inner.txt", "안\n");

            await AssertStatusMatches(repo);
        }

        [SkippableFact]
        public async Task Status_Throws_ForUnsupportedRepositories()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var repo = await InitAsync("unsupported");
            Write(repo, "a.txt", "a\n");
            await Git(repo, "add", "-A");
            await Git(repo, "commit", "-q", "-m", "base");
            GitLibReader.ReadStatus(repo);                                // 기본은 지원

            await Git(repo, "update-index", "--skip-worktree", "a.txt");  // 인덱스 v3
            Assert.Throws<NotSupportedException>(() => GitLibReader.ReadStatus(repo));
            await Git(repo, "update-index", "--no-skip-worktree", "a.txt");
            await Git(repo, "update-index", "--index-version", "2");
            GitLibReader.ReadStatus(repo);

            Write(repo, ".gitattributes", "*.bin filter=lfs diff=lfs merge=lfs -text\n");
            Assert.Throws<NotSupportedException>(() => GitLibReader.ReadStatus(repo));
            File.Delete(Path.Combine(repo, ".gitattributes"));

            Write(repo, ".gitmodules", "[submodule \"s\"]\n\tpath = s\n\turl = ../s\n");
            Assert.Throws<NotSupportedException>(() => GitLibReader.ReadStatus(repo));
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

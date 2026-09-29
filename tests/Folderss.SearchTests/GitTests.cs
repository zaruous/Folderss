using Folderss.Services;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>
    /// Git 연동의 WPF 비의존 부분: 저장소 탐색, 기계용 출력 파서, 실제 git으로 add→commit→status 왕복.
    /// 통합 테스트는 git이 PATH에 없으면 건너뛴다.
    /// </summary>
    public sealed class GitTests : IDisposable
    {
        private readonly string _root;

        public GitTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "folderss-git-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try
            {
                // git 객체 파일은 읽기 전용이라 먼저 속성을 푼다(Windows).
                foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(_root, true);
            }
            catch { }
        }

        private string P(params string[] parts) => Path.Combine(_root, Path.Combine(parts));

        private void MakeRepo(params string[] parts) => Directory.CreateDirectory(Path.Combine(P(parts), ".git"));

        // ── 저장소 탐색 ─────────────────────────────────────────────────────────

        [Fact]
        public void Scan_FindsNestedRepositories_GitFiles_AndSkipsExcludedFolders()
        {
            MakeRepo("a");
            MakeRepo("a", "sub");                        // 저장소 안의 중첩 저장소
            MakeRepo("b", "deep");
            Directory.CreateDirectory(P("c"));
            File.WriteAllText(P("c", ".git"), "gitdir: ../elsewhere"); // 워크트리·서브모듈
            MakeRepo("node_modules", "pkg");             // 제외 폴더
            MakeRepo("plain", "nope");

            var result = GitRepositoryScanner.Scan(_root, 6, new[] { "node_modules" });
            var found = result.Repositories.Select(r => r.DisplayPath.Replace('\\', '/')).ToList();

            Assert.Equal(new[] { "a", "a/sub", "b/deep", "c", "plain/nope" }, found);
            Assert.True(result.Repositories.Single(r => r.DisplayPath == "c").IsGitFile);
            Assert.DoesNotContain(result.Repositories, r => r.IsAncestor);
        }

        [Fact]
        public void Scan_RespectsDepthLimit()
        {
            MakeRepo("one");
            MakeRepo("one", "two", "three");

            var shallow = GitRepositoryScanner.Scan(_root, 1, null);
            Assert.Equal(new[] { "one" }, shallow.Repositories.Select(r => r.DisplayPath));

            var deep = GitRepositoryScanner.Scan(_root, 3, null);
            Assert.Equal(2, deep.Repositories.Count);
        }

        [Fact]
        public void Scan_InsideRepository_ListsAncestorFirst_AndBaseItselfAsDot()
        {
            MakeRepo("outer");
            MakeRepo("outer", "src", "inner");
            Directory.CreateDirectory(P("outer", "src"));

            var result = GitRepositoryScanner.Scan(P("outer", "src"), 6, null);

            Assert.True(result.Repositories[0].IsAncestor);
            Assert.Equal(P("outer"), result.Repositories[0].RootPath);
            Assert.Equal("inner", result.Repositories[1].DisplayPath);

            var self = GitRepositoryScanner.Scan(P("outer"), 0, null);
            Assert.Equal(".", self.Repositories.Single(r => !r.IsAncestor).DisplayPath);
        }

        [Fact]
        public void Scan_HonorsCancellation()
        {
            MakeRepo("a");
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                Assert.Throws<OperationCanceledException>(() => GitRepositoryScanner.Scan(_root, 6, null, null, cts.Token));
            }
        }

        // ── 출력 파서 ───────────────────────────────────────────────────────────

        [Fact]
        public void ParseStatusV2_ReadsBranchHeaders_AndAllRecordKinds()
        {
            var output = string.Join("\0",
                "# branch.oid 1234567890abcdef1234567890abcdef12345678",
                "# branch.head main",
                "# branch.upstream origin/main",
                "# branch.ab +2 -3",
                "1 M. N... 100644 100644 100644 aaaa bbbb 한글 폴더/파일 이름.txt",
                "1 .M N... 100644 100644 100644 aaaa bbbb README.md",
                "2 R. N... 100644 100644 100644 aaaa bbbb R100 new name.cs",
                "old name.cs",
                "u UU N... 100644 100644 100644 100644 aaaa bbbb cccc conflict.txt",
                "? untracked file.txt",
                "") ;

            var s = GitOutputParser.ParseStatusV2(output);

            Assert.Equal("main", s.Branch);
            Assert.Equal("origin/main", s.Upstream);
            Assert.Equal(2, s.Ahead);
            Assert.Equal(3, s.Behind);
            Assert.False(s.IsDetached);
            Assert.Equal(5, s.Entries.Count);
            Assert.Equal("한글 폴더/파일 이름.txt", s.Entries[0].Path);
            Assert.True(s.Entries[0].IsStaged);
            Assert.False(s.Entries[0].IsUnstaged);
            Assert.True(s.Entries[1].IsUnstaged);
            Assert.Equal("new name.cs", s.Entries[2].Path);
            Assert.Equal("old name.cs", s.Entries[2].OriginalPath);
            Assert.True(s.Entries[3].IsConflicted);
            Assert.False(s.Entries[3].IsStaged);
            Assert.True(s.Entries[4].IsUntracked);
            Assert.Equal((2, 1, 1, 1), (s.StagedCount, s.UnstagedCount, s.UntrackedCount, s.ConflictCount));
        }

        [Fact]
        public void ParseStatusV2_InitialAndDetached()
        {
            var initial = GitOutputParser.ParseStatusV2("# branch.oid (initial)\0# branch.head master\0");
            Assert.Null(initial.HeadOid);
            Assert.Equal("master", initial.BranchDisplay);

            var detached = GitOutputParser.ParseStatusV2("# branch.oid abcdef1234567\0# branch.head (detached)\0");
            Assert.True(detached.IsDetached);
            Assert.Equal("(detached @abcdef1)", detached.BranchDisplay);
        }

        [Fact]
        public void ParseLog_SplitsFields_AndParents()
        {
            const char f = GitOutputParser.FieldSeparator;
            var output = "h2" + f + "h1 hx" + f + "홍길동" + f + "1700000000" + f + "HEAD -> main, origin/main" + f + "병합 커밋\0"
                       + "\nh1" + f + "" + f + "kim" + f + "1600000000" + f + "" + f + "first\0";

            var commits = GitOutputParser.ParseLog(output);

            Assert.Equal(2, commits.Count);
            Assert.Equal(new[] { "h1", "hx" }, commits[0].Parents);
            Assert.Equal("홍길동", commits[0].Author);
            Assert.Equal("병합 커밋", commits[0].Subject);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), commits[0].Time);
            Assert.Empty(commits[1].Parents);
        }

        [Fact]
        public void ParseBranches_SeparatesLocalAndRemote_AndSkipsRemoteHead()
        {
            const char f = GitOutputParser.FieldSeparator;
            var output = string.Join("\n",
                "*" + f + "refs/heads/main" + f + "origin/main" + f + "msg",
                " " + f + "refs/heads/feature/x" + f + "" + f + "wip",
                " " + f + "refs/remotes/origin/HEAD" + f + "" + f + "",
                " " + f + "refs/remotes/origin/main" + f + "" + f + "msg",
                "");

            var branches = GitOutputParser.ParseBranches(output);

            Assert.Equal(new[] { "main", "feature/x", "origin/main" }, branches.Select(b => b.Name));
            Assert.True(branches[0].IsCurrent);
            Assert.Equal("origin/main", branches[0].Upstream);
            Assert.True(branches[2].IsRemote);
        }

        [Fact]
        public void ParseVersion_ReadsWindowsSuffixedVersion()
        {
            Assert.Equal(new Version(2, 43, 0), GitCommandRunner.ParseVersion("git version 2.43.0.windows.1"));
            Assert.Null(GitCommandRunner.ParseVersion("nope"));
        }

        // ── 실제 git 왕복 ───────────────────────────────────────────────────────

        private static Task<GitResult> Git(string repo, params string[] args) =>
            GitCommandRunner.RunAsync(repo, args, GitCommandRunner.QueryTimeout, CancellationToken.None);

        [SkippableFact]
        public async Task RealGit_AddCommitStatusBranchLog_RoundTrip()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");

            var repo = P("repo");
            Directory.CreateDirectory(repo);
            Assert.True((await Git(repo, "init", "-b", "main")).Success);
            await Git(repo, "config", "user.name", "테스트");
            await Git(repo, "config", "user.email", "t@example.com");
            await Git(repo, "config", "commit.gpgsign", "false");

            File.WriteAllText(Path.Combine(repo, "한글 파일.txt"), "x");
            var status = GitOutputParser.ParseStatusV2((await Git(repo, "status", "--porcelain=v2", "--branch", "-z")).StdOut);
            Assert.Null(status.HeadOid);
            Assert.Equal("한글 파일.txt", status.Entries.Single().Path);
            Assert.True(status.Entries.Single().IsUntracked);

            // UI와 같은 방식: 경로는 NUL 구분으로 표준 입력에 넘긴다.
            var add = await GitCommandRunner.RunAsync(repo, new[] { "add", "--pathspec-from-file=-", "--pathspec-file-nul" },
                GitCommandRunner.QueryTimeout, CancellationToken.None, standardInput: "한글 파일.txt\0");
            Assert.True(add.Success, add.StdErr);

            var messageFile = Path.Combine(_root, "msg.txt");
            File.WriteAllText(messageFile, "첫 커밋\n\n본문");
            var commit = await Git(repo, "commit", "-F", messageFile);
            Assert.True(commit.Success, commit.StdErr);

            status = GitOutputParser.ParseStatusV2((await Git(repo, "status", "--porcelain=v2", "--branch", "-z")).StdOut);
            Assert.NotNull(status.HeadOid);
            Assert.Empty(status.Entries);

            var branches = GitOutputParser.ParseBranches((await Git(repo, "for-each-ref", GitOutputParser.BranchFormat, "refs/heads", "refs/remotes")).StdOut);
            Assert.Equal("main", branches.Single(b => b.IsCurrent).Name);

            var log = GitOutputParser.ParseLog((await Git(repo, "log", "-z", "-n", "10", GitOutputParser.LogFormat)).StdOut);
            Assert.Equal("첫 커밋", log.Single().Subject);
            Assert.Equal("테스트", log.Single().Author);
        }

        [SkippableFact]
        public async Task RealGit_FailureReturnsStdErr_NotException()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");

            var result = await Git(_root, "status");   // 저장소 아님

            Assert.False(result.Success);
            Assert.Contains("not a git repository", result.StdErr, StringComparison.OrdinalIgnoreCase);
        }
    }
}

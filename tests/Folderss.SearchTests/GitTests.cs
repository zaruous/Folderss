using Folderss.Models;
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

        // ── diff 파서 ───────────────────────────────────────────────────────────

        [Fact]
        public void ParseDiff_ClassifiesLines_AndNumbersOldNew_TreatingDashesInsideHunkAsContent()
        {
            var diff = string.Join("\n",
                "diff --git a/a.txt b/a.txt",
                "index 111..222 100644",
                "--- a/a.txt",
                "+++ b/a.txt",
                "@@ -10,3 +10,3 @@ section",
                " keep",
                "--- 지운 줄이 --로 시작",
                "+++ 추가한 줄이 ++로 시작",
                " keep2",
                "\\ No newline at end of file",
                "diff --git a/bin.dat b/bin.dat",
                "Binary files a/bin.dat and b/bin.dat differ",
                "");

            var lines = GitOutputParser.ParseDiff(diff);

            Assert.Equal(new[]
            {
                GitDiffLineKind.Header, GitDiffLineKind.Header, GitDiffLineKind.Header, GitDiffLineKind.Header,
                GitDiffLineKind.Hunk, GitDiffLineKind.Context, GitDiffLineKind.Removed, GitDiffLineKind.Added,
                GitDiffLineKind.Context, GitDiffLineKind.Meta, GitDiffLineKind.Header, GitDiffLineKind.Header
            }, lines.Select(l => l.Kind));
            Assert.Equal((10, 10), (lines[5].OldLine.Value, lines[5].NewLine.Value));
            Assert.Equal(11, lines[6].OldLine);
            Assert.Null(lines[6].NewLine);
            Assert.Equal(11, lines[7].NewLine);
            Assert.Null(lines[7].OldLine);
            Assert.Equal((12, 12), (lines[8].OldLine.Value, lines[8].NewLine.Value));
        }

        [Fact]
        public void ParseDiff_CombinedConflictDiff_ClassifiesByTwoColumnMarks_WithoutNumbers()
        {
            var diff = "diff --cc c.txt\n@@@ -1,1 -1,1 +1,5 @@@\n  same\n++<<<<<<< HEAD\n +ours\n- theirs\n";

            var lines = GitOutputParser.ParseDiff(diff);

            Assert.Equal(new[] { GitDiffLineKind.Header, GitDiffLineKind.Hunk, GitDiffLineKind.Context,
                GitDiffLineKind.Added, GitDiffLineKind.Added, GitDiffLineKind.Removed }, lines.Select(l => l.Kind));
            Assert.All(lines, l => Assert.Null(l.NewLine));
        }

        [Fact]
        public void ParseDiff_TruncatesAtMaxLines_WithNotice()
        {
            var diff = string.Join("\n", Enumerable.Range(0, 10).Select(i => "line" + i));

            var lines = GitOutputParser.ParseDiff(diff, 4);

            Assert.Equal(5, lines.Count);
            Assert.Equal(GitDiffLineKind.Meta, lines[4].Kind);
            Assert.Contains("6줄", lines[4].Text);
            Assert.Empty(GitOutputParser.ParseDiff(""));
        }

        // ── 실제 git: diff와 upstream 비교 ───────────────────────────────────────

        private async Task<string> InitRepoAsync(string name)
        {
            var repo = P(name);
            Directory.CreateDirectory(repo);
            Assert.True((await Git(repo, "init", "-b", "main")).Success);
            await Git(repo, "config", "user.name", "t");
            await Git(repo, "config", "user.email", "t@example.com");
            await Git(repo, "config", "commit.gpgsign", "false");
            await Git(repo, "config", "core.autocrlf", "false");
            return repo;
        }

        private static async Task CommitAllAsync(string repo, string message)
        {
            Assert.True((await Git(repo, "add", "-A")).Success);
            var commit = await Git(repo, "commit", "-q", "-m", message);
            Assert.True(commit.Success, commit.StdErr);
        }

        private static Task<GitResult> Run(string repo, System.Collections.Generic.List<string> args) =>
            GitCommandRunner.RunAsync(repo, args, GitCommandRunner.QueryTimeout, CancellationToken.None, readOnly: true);

        private static Task<GitResult> Run(string repo, GitDiffRequest request) => Run(repo, request.Arguments);

        [SkippableFact]
        public async Task RealGit_WorkTreeStagedAndUntrackedDiffs()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var repo = await InitRepoAsync("diffrepo");
            File.WriteAllText(Path.Combine(repo, "a.txt"), "one\ntwo\n");
            await CommitAllAsync(repo, "init");

            File.WriteAllText(Path.Combine(repo, "a.txt"), "one\nTWO\n");
            var work = GitOutputParser.ParseDiff((await Run(repo, GitDiffCommands.WorkTree("a.txt"))).StdOut);
            Assert.Contains(work, l => l.Kind == GitDiffLineKind.Removed && l.Text == "-two" && l.OldLine == 2);
            Assert.Contains(work, l => l.Kind == GitDiffLineKind.Added && l.Text == "+TWO" && l.NewLine == 2);

            await Git(repo, "add", "a.txt");
            Assert.Empty((await Run(repo, GitDiffCommands.WorkTree("a.txt"))).StdOut);
            var staged = GitOutputParser.ParseDiff((await Run(repo, GitDiffCommands.Staged("a.txt", null))).StdOut);
            Assert.Contains(staged, l => l.Text == "+TWO");

            File.WriteAllText(Path.Combine(repo, "새 파일.txt"), "hello\n");
            var untrackedResult = await Run(repo, GitDiffCommands.Untracked("새 파일.txt"));
            Assert.Equal(1, untrackedResult.ExitCode);   // --no-index: 차이 있음 = 1
            Assert.True(GitDiffCommands.IsSuccess(untrackedResult, noIndex: true));
            Assert.False(GitDiffCommands.IsSuccess(untrackedResult, noIndex: false));
            Assert.Contains(GitOutputParser.ParseDiff(untrackedResult.StdOut), l => l.Text == "+hello" && l.NewLine == 1);
        }

        [SkippableFact]
        public async Task RealGit_CompareWithUpstream_OutgoingIncomingAndWorkTree()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");

            // 원격(bare) + 두 클론: other가 push한 커밋은 local이 받을 것, local 커밋은 보낼 것.
            var remote = P("remote.git");
            Assert.True((await Git(null, "init", "--bare", "-b", "main", remote)).Success);
            var seed = await InitRepoAsync("seed");
            File.WriteAllText(Path.Combine(seed, "f.txt"), "base\n");
            await CommitAllAsync(seed, "base");
            await Git(seed, "remote", "add", "origin", remote);
            Assert.True((await Git(seed, "push", "-u", "origin", "main")).Success);

            var local = P("local");
            Assert.True((await Git(null, "clone", "-q", remote, local)).Success);
            await Git(local, "config", "user.name", "t");
            await Git(local, "config", "user.email", "t@example.com");
            await Git(local, "config", "commit.gpgsign", "false");

            File.WriteAllText(Path.Combine(seed, "incoming.txt"), "from remote\n");
            await CommitAllAsync(seed, "원격 커밋");
            Assert.True((await Git(seed, "push")).Success);

            File.WriteAllText(Path.Combine(local, "outgoing.txt"), "from local\n");
            await CommitAllAsync(local, "로컬 커밋");
            Assert.True((await Git(local, "fetch")).Success);

            var outgoing = GitOutputParser.ParseLog((await Run(local, GitDiffCommands.LogRange(GitDiffCommands.Upstream, "HEAD", 100))).StdOut);
            var incoming = GitOutputParser.ParseLog((await Run(local, GitDiffCommands.LogRange("HEAD", GitDiffCommands.Upstream, 100))).StdOut);
            Assert.Equal("로컬 커밋", outgoing.Single().Subject);
            Assert.Equal("원격 커밋", incoming.Single().Subject);

            var pushDiff = (await Run(local, GitDiffCommands.Range(GitDiffCommands.Upstream + "...HEAD"))).StdOut;
            var pullDiff = (await Run(local, GitDiffCommands.Range("HEAD..." + GitDiffCommands.Upstream))).StdOut;
            Assert.Contains("outgoing.txt", pushDiff);
            Assert.DoesNotContain("incoming.txt", pushDiff);
            Assert.Contains("incoming.txt", pullDiff);
            Assert.DoesNotContain("outgoing.txt", pullDiff);

            // 작업 트리 ↔ upstream: 커밋 안 한 수정도 포함된다.
            File.WriteAllText(Path.Combine(local, "f.txt"), "edited\n");
            var workVsUpstream = (await Run(local, GitDiffCommands.WorkTreeAgainst(GitDiffCommands.Upstream))).StdOut;
            Assert.Contains("+edited", workVsUpstream);

            // 커밋 diff: 일반 커밋은 첫 부모 기준, 최초 커밋은 빈 트리 기준.
            var root = GitOutputParser.ParseLog((await Git(local, "log", "-z", "--reverse", GitOutputParser.LogFormat)).StdOut).First();
            var rootDiff = (await Run(local, GitDiffCommands.Commit(root.Hash, root.Parents))).StdOut;
            Assert.Contains("+base", rootDiff);
            var head = outgoing.Single();
            Assert.Contains("+from local", (await Run(local, GitDiffCommands.Commit(head.Hash, head.Parents))).StdOut);
        }

        // ── 인코딩 대체 해석 ────────────────────────────────────────────────────

        [Fact]
        public void Decode_MixedUtf8AndCp949Lines_DecodesEachLine()
        {
            var cp949 = GitTextDecoder.Resolve(GitFallbackEncoding.Cp949);
            var bytes = new System.Collections.Generic.List<byte>();
            bytes.AddRange(System.Text.Encoding.UTF8.GetBytes("+UTF-8 한글\n"));
            bytes.AddRange(cp949.GetBytes("-CP949 한글\n"));
            bytes.AddRange(System.Text.Encoding.UTF8.GetBytes(" 끝"));

            Assert.Equal("+UTF-8 한글\n-CP949 한글\n 끝", GitTextDecoder.Decode(bytes.ToArray(), cp949));

            // 대체 인코딩이 없으면 깨진 문자로라도 읽는다(예외 없음).
            Assert.StartsWith("+UTF-8 한글\n-CP949", GitTextDecoder.Decode(bytes.ToArray(), null));
            Assert.Equal(string.Empty, GitTextDecoder.Decode(new byte[0], cp949));
            Assert.Null(GitTextDecoder.Resolve(GitFallbackEncoding.None));
        }

        [SkippableFact]
        public async Task RealGit_DiffOfCp949File_ReadsWithFallback()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var cp949 = GitTextDecoder.Resolve(GitFallbackEncoding.Cp949);
            var repo = await InitRepoAsync("cp949repo");
            File.WriteAllBytes(Path.Combine(repo, "old.txt"), cp949.GetBytes("가나다\n"));
            await CommitAllAsync(repo, "init");
            File.WriteAllBytes(Path.Combine(repo, "old.txt"), cp949.GetBytes("라마바\n"));

            var result = await GitCommandRunner.RunAsync(repo, GitDiffCommands.WorkTree("old.txt").Arguments,
                GitCommandRunner.QueryTimeout, CancellationToken.None, readOnly: true, fallbackEncoding: cp949);

            var lines = GitOutputParser.ParseDiff(result.StdOut);
            Assert.Contains(lines, l => l.Text == "-가나다");
            Assert.Contains(lines, l => l.Text == "+라마바");
        }

        // ── 공백 무시 ───────────────────────────────────────────────────────────

        [SkippableFact]
        public async Task RealGit_IgnoreWhitespace_HidesWhitespaceOnlyChanges()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var repo = await InitRepoAsync("wsrepo");
            File.WriteAllText(Path.Combine(repo, "w.txt"), "a b\n");
            await CommitAllAsync(repo, "init");
            File.WriteAllText(Path.Combine(repo, "w.txt"), "a    b\n");

            Assert.NotEmpty((await Run(repo, GitDiffCommands.WorkTree("w.txt"))).StdOut);
            Assert.DoesNotContain("@@", (await Run(repo, GitDiffCommands.WorkTree("w.txt", ignoreWhitespace: true))).StdOut);
        }

        // ── 외부 비교 도구 ──────────────────────────────────────────────────────

        [Fact]
        public void BuildToolCommand_QuotesExe_AndAlwaysQuotesPlaceholders()
        {
            Assert.Equal(@"'C:/Program Files/WinMerge/WinMergeU.exe' -e -u ""$LOCAL"" ""$REMOTE""",
                GitDiffCommands.BuildToolCommand(@"""C:\Program Files\WinMerge\WinMergeU.exe""", @"-e -u ""{left}"" ""{right}"""));
            // 따옴표를 빠뜨려도 따옴표 친 형태가 된다.
            Assert.Equal(@"'x.exe' --diff ""$LOCAL"" ""$REMOTE""", GitDiffCommands.BuildToolCommand("x.exe", "--diff {left} {right}"));
            // 자리표시자가 없으면 끝에 붙인다. 빈 인수는 기본값.
            Assert.Equal(@"'x.exe' /s ""$LOCAL"" ""$REMOTE""", GitDiffCommands.BuildToolCommand("x.exe", "/s"));
            Assert.Equal(@"'x.exe' ""$LOCAL"" ""$REMOTE""", GitDiffCommands.BuildToolCommand("x.exe", "  "));
            // 경로의 작은따옴표는 셸 규칙대로 이스케이프.
            Assert.StartsWith(@"'C:/it'\''s/t.exe' ", GitDiffCommands.BuildToolCommand(@"C:\it's\t.exe", null));
        }

        [Fact]
        public void ExternalTool_BuildsDifftoolArgs_PerModeAndTarget()
        {
            var staged = GitDiffCommands.ExternalTool(GitDiffCommands.Staged("b.txt", "a.txt"), GitDiffToolMode.Custom, "t.exe", null);
            Assert.Equal(new[] { "-c", "difftool.folderss.cmd='t.exe' \"$LOCAL\" \"$REMOTE\"", "difftool", "--tool=folderss", "--no-prompt",
                "--cached", "--", "a.txt", "b.txt" }, staged);

            var range = GitDiffCommands.ExternalTool(GitDiffCommands.Range("@{u}...HEAD"), GitDiffToolMode.GitConfig, null, null);
            Assert.Equal(new[] { "difftool", "--no-prompt", "--dir-diff", "@{u}...HEAD", "--" }, range);

            Assert.Null(GitDiffCommands.ExternalTool(GitDiffCommands.Untracked("n.txt"), GitDiffToolMode.Custom, "t.exe", null));
            Assert.Null(GitDiffCommands.ExternalTool(GitDiffCommands.Commit("h", new string[0]), GitDiffToolMode.Custom, "t.exe", null));
            Assert.Null(GitDiffCommands.ExternalTool(GitDiffCommands.WorkTree("a.txt"), GitDiffToolMode.None, "t.exe", null));
        }

        [SkippableFact]
        public async Task RealGit_CustomDifftool_ReceivesBothSides_ForFileAndDirDiff()
        {
            Skip.If(GitCommandRunner.FindGit() == null || OperatingSystem.IsWindows(), "셸 스크립트 도구로 검증 — 리눅스·맥 전용");
            var repo = await InitRepoAsync("toolrepo");
            File.WriteAllText(Path.Combine(repo, "a.txt"), "old\n");
            await CommitAllAsync(repo, "init");
            File.WriteAllText(Path.Combine(repo, "a.txt"), "new\n");

            // 도구: 파일이면 두 내용을, 폴더면 두 목록을 기록한다. 경로에 공백을 넣어 따옴표 처리를 확인한다.
            var outDir = P("tool out");
            Directory.CreateDirectory(outDir);
            var script = Path.Combine(outDir, "fake tool.sh");
            File.WriteAllText(script,
                "#!/bin/sh\n" +
                "if [ -d \"$1\" ]; then ls -R \"$1\" > \"" + outDir + "/left.txt\"; ls -R \"$2\" > \"" + outDir + "/right.txt\";\n" +
                "else cat \"$1\" > \"" + outDir + "/left.txt\"; cat \"$2\" > \"" + outDir + "/right.txt\"; fi\n");
            System.Diagnostics.Process.Start("chmod", new[] { "+x", script }).WaitForExit();

            var fileArgs = GitDiffCommands.ExternalTool(GitDiffCommands.WorkTree("a.txt"), GitDiffToolMode.Custom, script, "{left} {right}");
            var fileRun = await GitCommandRunner.RunAsync(repo, fileArgs, GitCommandRunner.QueryTimeout, CancellationToken.None, throttle: false);
            Assert.True(fileRun.Success, fileRun.StdErr);
            Assert.Equal("old\n", File.ReadAllText(Path.Combine(outDir, "left.txt")));
            Assert.Equal("new\n", File.ReadAllText(Path.Combine(outDir, "right.txt")));

            await CommitAllAsync(repo, "second");
            var head = GitOutputParser.ParseLog((await Git(repo, "log", "-z", "-n", "1", GitOutputParser.LogFormat)).StdOut).Single();
            var dirArgs = GitDiffCommands.ExternalTool(GitDiffCommands.Commit(head.Hash, head.Parents), GitDiffToolMode.Custom, script, null);
            var dirRun = await GitCommandRunner.RunAsync(repo, dirArgs, GitCommandRunner.QueryTimeout, CancellationToken.None, throttle: false);
            Assert.True(dirRun.Success, dirRun.StdErr);
            Assert.Contains("a.txt", File.ReadAllText(Path.Combine(outDir, "right.txt")));
        }

        // ── 설정 저장 ───────────────────────────────────────────────────────────

        [Fact]
        public void Settings_RoundTrip_KeepsAllFields()
        {
            var path = P("git-settings.xml");
            var original = new GitSettings
            {
                GitExecutablePath = @"D:\PortableGit\cmd\git.exe",
                BaseFolderMode = GitBaseFolderMode.CurrentFolder,
                PullMode = GitPullMode.Rebase,
                ScanDepth = 3,
                ExcludedFolders = new System.Collections.Generic.List<string> { "dist", "out" },
                LogLimit = 50,
                LogAllBranches = false,
                IgnoreWhitespace = true,
                FallbackEncoding = GitFallbackEncoding.Cp949,
                DiffToolMode = GitDiffToolMode.Custom,
                DiffToolPath = @"C:\Program Files\WinMerge\WinMergeU.exe",
                DiffToolArguments = "-e -u \"{left}\" \"{right}\""
            };

            GitSettingsService.Save(original, path);
            var loaded = GitSettingsService.Load(path);

            Assert.Equal(original.GitExecutablePath, loaded.GitExecutablePath);
            Assert.Equal(original.BaseFolderMode, loaded.BaseFolderMode);
            Assert.Equal(original.PullMode, loaded.PullMode);
            Assert.Equal(3, loaded.ScanDepth);
            Assert.Equal(new[] { "dist", "out" }, loaded.ExcludedFolders);
            Assert.Equal(50, loaded.LogLimit);
            Assert.False(loaded.LogAllBranches);
            Assert.True(loaded.IgnoreWhitespace);
            Assert.Equal(GitFallbackEncoding.Cp949, loaded.FallbackEncoding);
            Assert.Equal(GitDiffToolMode.Custom, loaded.DiffToolMode);
            Assert.Equal(original.DiffToolPath, loaded.DiffToolPath);
            Assert.Equal(original.DiffToolArguments, loaded.DiffToolArguments);

            // 파일이 없으면 기본값.
            var defaults = GitSettingsService.Load(P("none.xml"));
            Assert.Equal(GitDiffToolMode.None, defaults.DiffToolMode);
            Assert.Equal(GitFallbackEncoding.SystemAnsi, defaults.FallbackEncoding);
        }

        [Fact]
        public void ConfiguredGitPath_Missing_ReturnsNull_InsteadOfFallingBack()
        {
            try
            {
                GitCommandRunner.ConfiguredGitPath = P("no-such-git.exe");
                Assert.Null(GitCommandRunner.FindGit());
            }
            finally
            {
                GitCommandRunner.ConfiguredGitPath = string.Empty;
            }
        }
    }
}

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
                DiffViewMode = GitDiffViewMode.FullFile,
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
            Assert.Equal(GitDiffViewMode.FullFile, loaded.DiffViewMode);
            Assert.Equal(GitDiffToolMode.Custom, loaded.DiffToolMode);
            Assert.Equal(original.DiffToolPath, loaded.DiffToolPath);
            Assert.Equal(original.DiffToolArguments, loaded.DiffToolArguments);

            // 파일이 없으면 기본값.
            var defaults = GitSettingsService.Load(P("none.xml"));
            Assert.Equal(GitDiffToolMode.None, defaults.DiffToolMode);
            Assert.Equal(GitFallbackEncoding.None, defaults.FallbackEncoding);   // BOM 없으면 UTF-8이 기본
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

        // ── 브랜치 그래프 ───────────────────────────────────────────────────────

        private static GitCommitInfo C(string hash, params string[] parents) => new GitCommitInfo { Hash = hash, Parents = parents };

        private static string Segs(GitGraphRow row) =>
            string.Join(" ", row.Segments.Select(s => s.ToString()).OrderBy(x => x, StringComparer.Ordinal));

        [Fact]
        public void Graph_Linear_StaysInLaneZero()
        {
            var rows = GitGraphLayout.Compute(new[] { C("c", "b"), C("b", "a"), C("a") });

            Assert.All(rows, r => Assert.Equal(0, r.NodeLane));
            Assert.Equal("0:1->0:2", Segs(rows[0]));            // 브랜치 끝: 위에서 오는 선 없음
            Assert.Equal("0:0->0:1 0:1->0:2", Segs(rows[1]));
            Assert.Equal("0:0->0:1", Segs(rows[2]));            // 최초 커밋: 아래로 가는 선 없음
            Assert.All(rows, r => Assert.Equal(1, r.LaneCount));
        }

        [Fact]
        public void Graph_BranchAndMerge_OpensSecondLane_AndJoinsBack()
        {
            //   m (병합: 첫 부모 x, 둘째 부모 f)
            //   f (feature, 부모 base)
            //   x (main, 부모 base)
            //   base
            var rows = GitGraphLayout.Compute(new[] { C("m", "x", "f"), C("f", "base"), C("x", "base"), C("base") });

            Assert.True(rows[0].IsMerge);
            Assert.Equal(0, rows[0].NodeLane);
            Assert.Equal("0:1->0:2 0:1->1:2", Segs(rows[0]));           // 두 부모로 갈라짐
            Assert.Equal(1, rows[1].NodeLane);                          // f는 둘째 레인
            Assert.Equal("0:0->0:2 1:0->1:1 1:1->1:2", Segs(rows[1]));  // main 레인은 지나감
            Assert.Equal(0, rows[2].NodeLane);
            Assert.Equal("0:0->0:1 0:1->0:2 1:0->1:2", Segs(rows[2]));
            Assert.Equal(0, rows[3].NodeLane);
            Assert.Equal("0:0->0:1 1:0->0:1", Segs(rows[3]));           // 두 레인이 base로 모임
            Assert.Equal(2, rows[1].LaneCount);
        }

        [Fact]
        public void Graph_TwoTips_AndOctopusMerge_AndTruncatedParent()
        {
            // 서로 다른 두 브랜치 끝(t1, t2), 세 부모 병합 o, 목록 밖 부모(missing)
            var rows = GitGraphLayout.Compute(new[] { C("t1", "o"), C("t2", "missing"), C("o", "p1", "p2", "p3"), C("p1"), C("p2"), C("p3") });

            Assert.Equal(0, rows[0].NodeLane);
            Assert.Equal(1, rows[1].NodeLane);                  // 둘째 끝은 새 레인
            Assert.Equal("0:0->0:2 1:1->1:2", Segs(rows[1]));
            Assert.Equal(0, rows[2].NodeLane);
            Assert.Equal(3, rows[2].Segments.Count(s => s.FromY == GitGraphLayout.Center));   // 부모 셋으로
            // 목록 밖 부모를 기다리는 레인은 끝까지 아래로 이어진다.
            Assert.All(rows.Skip(2), r => Assert.Contains(r.Segments, s => s.FromLane == 1 && s.ToLane == 1 && s.ToY == GitGraphLayout.Bottom));
        }

        [SkippableFact]
        public async Task Graph_RealGitLog_EveryParentLineReachesItsCommit()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var repo = await InitRepoAsync("graphrepo");
            File.WriteAllText(Path.Combine(repo, "f.txt"), "0\n");
            await CommitAllAsync(repo, "c0");
            await Git(repo, "switch", "-q", "-c", "feature");
            File.WriteAllText(Path.Combine(repo, "g.txt"), "1\n");
            await CommitAllAsync(repo, "f1");
            await Git(repo, "switch", "-q", "main");
            File.WriteAllText(Path.Combine(repo, "f.txt"), "2\n");
            await CommitAllAsync(repo, "m1");
            Assert.True((await Git(repo, "merge", "-q", "--no-edit", "feature")).Success);

            var commits = GitOutputParser.ParseLog((await Git(repo, "log", "--topo-order", "-z", "--all", GitOutputParser.LogFormat)).StdOut);
            var rows = GitGraphLayout.Compute(commits);

            Assert.Equal(4, rows.Count);
            Assert.True(rows[0].IsMerge);
            // 연결성: 행 i가 아래로 내린 선은 모두 다음 행 위에서 받는다(끊긴 선 없음).
            for (var i = 0; i < rows.Count - 1; i++)
            {
                var down = rows[i].Segments.Where(s => s.ToY == GitGraphLayout.Bottom).Select(s => s.ToLane).Distinct().OrderBy(x => x);
                var up = rows[i + 1].Segments.Where(s => s.FromY == GitGraphLayout.Top).Select(s => s.FromLane).Distinct().OrderBy(x => x);
                Assert.Equal(down, up);
            }
        }

        // ── BOM 판정·UTF-16 파일 diff ───────────────────────────────────────────

        [Fact]
        public void DetectBom_AllKinds_AndNoBomMeansUtf8()
        {
            Assert.Equal("UTF-8 (BOM)", GitTextDecoder.DescribeBom(new byte[] { 0xEF, 0xBB, 0xBF, 0x41 }));
            Assert.Equal("UTF-16 LE", GitTextDecoder.DescribeBom(new byte[] { 0xFF, 0xFE, 0x41, 0 }));
            Assert.Equal("UTF-16 BE", GitTextDecoder.DescribeBom(new byte[] { 0xFE, 0xFF, 0, 0x41 }));
            Assert.Equal("UTF-32 LE", GitTextDecoder.DescribeBom(new byte[] { 0xFF, 0xFE, 0, 0, 0x41, 0, 0, 0 }));
            Assert.Equal("UTF-32 BE", GitTextDecoder.DescribeBom(new byte[] { 0, 0, 0xFE, 0xFF, 0, 0, 0, 0x41 }));
            Assert.Equal("UTF-8", GitTextDecoder.DescribeBom(System.Text.Encoding.UTF8.GetBytes("한글")));

            Assert.True(GitTextDecoder.HasWideBom(new byte[] { 0xFF, 0xFE, 0x41, 0 }));
            Assert.False(GitTextDecoder.HasWideBom(new byte[] { 0xEF, 0xBB, 0xBF }));

            var utf16 = new System.Text.UnicodeEncoding(false, true);
            var bytes = utf16.GetPreamble().Concat(utf16.GetBytes("가나\n")).ToArray();
            Assert.Equal("가나\n", GitTextDecoder.DecodeFile(bytes, null));                       // BOM 제외하고 UTF-16으로
            Assert.Equal("한글", GitTextDecoder.DecodeFile(System.Text.Encoding.UTF8.GetBytes("한글"), null));   // BOM 없음 → UTF-8
            Assert.Equal("A", GitTextDecoder.DecodeFile(new byte[] { 0xEF, 0xBB, 0xBF, 0x41 }, null)); // UTF-8 BOM 제거
        }

        private static byte[] Utf16(string text)
        {
            var encoding = new System.Text.UnicodeEncoding(false, true);
            return encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();
        }

        [SkippableFact]
        public async Task RealGit_Utf16BomFile_IsReDiffedAsText_ButRealBinaryStaysBinary()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var repo = await InitRepoAsync("utf16repo");
            File.WriteAllBytes(Path.Combine(repo, "u16.txt"), Utf16("첫째\r\n둘째\r\n"));
            File.WriteAllBytes(Path.Combine(repo, "bin.dat"), new byte[] { 0, 1, 2, 3, 0 });
            await CommitAllAsync(repo, "init");
            File.WriteAllBytes(Path.Combine(repo, "u16.txt"), Utf16("첫째\r\n바뀜\r\n"));
            File.WriteAllBytes(Path.Combine(repo, "bin.dat"), new byte[] { 0, 9, 9, 9, 0 });

            // 작업 트리 diff (파일 하나)
            var request = GitDiffCommands.WorkTree("u16.txt");
            var raw = (await Run(repo, request)).StdOut;
            Assert.Contains("Binary files a/u16.txt and b/u16.txt differ", raw);   // git 자체는 바이너리로 봄
            var expanded = await GitEncodingDiff.ExpandAsync(repo, request, raw, false, null, CancellationToken.None);
            var lines = GitOutputParser.ParseDiff(expanded);
            Assert.Contains(lines, l => l.Kind == GitDiffLineKind.Removed && l.Text.TrimEnd('\r') == "-둘째" && l.OldLine == 2);
            Assert.Contains(lines, l => l.Kind == GitDiffLineKind.Added && l.Text.TrimEnd('\r') == "+바뀜" && l.NewLine == 2);
            Assert.Contains(lines, l => l.Text.Contains("UTF-16 LE"));

            // 진짜 바이너리는 그대로
            var binRequest = GitDiffCommands.WorkTree("bin.dat");
            var binRaw = (await Run(repo, binRequest)).StdOut;
            Assert.Equal(binRaw, await GitEncodingDiff.ExpandAsync(repo, binRequest, binRaw, false, null, CancellationToken.None));

            // 커밋 diff (여러 파일, 리비전 blob에서 읽기): 스테이지 → 커밋 뒤 커밋 diff에서도 텍스트로 보인다.
            await CommitAllAsync(repo, "change");
            var head = GitOutputParser.ParseLog((await Git(repo, "log", "-z", "-n", "1", GitOutputParser.LogFormat)).StdOut).Single();
            var commitRequest = GitDiffCommands.Commit(head.Hash, head.Parents);
            var commitRaw = (await Run(repo, commitRequest)).StdOut;
            var commitExpanded = await GitEncodingDiff.ExpandAsync(repo, commitRequest, commitRaw, false, null, CancellationToken.None);
            Assert.Contains(GitOutputParser.ParseDiff(commitExpanded), l => l.Text.TrimEnd('\r') == "+바뀜");
            Assert.Contains("Binary files a/bin.dat and b/bin.dat differ", commitExpanded);
        }

        [SkippableFact]
        public async Task RealGit_UserNoPrefixConfig_DoesNotBreakPaths()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var repo = await InitRepoAsync("noprefix");
            await Git(repo, "config", "diff.noprefix", "true");
            File.WriteAllText(Path.Combine(repo, "a.txt"), "1\n");
            await CommitAllAsync(repo, "init");
            File.WriteAllText(Path.Combine(repo, "a.txt"), "2\n");

            var output = (await Run(repo, GitDiffCommands.WorkTree("a.txt"))).StdOut;

            Assert.Contains("diff --git a/a.txt b/a.txt", output);
            Assert.Contains("--- a/a.txt", output);
        }

        // ── 변경 사항 트리 보기 ─────────────────────────────────────────────────

        private static GitStatusEntry E(string path, char x = 'M', char y = '.') => new GitStatusEntry { Path = path, IndexState = x, WorkTreeState = y };

        private static string Dump(System.Collections.Generic.IEnumerable<GitChangeNode> nodes, string indent = "") =>
            string.Concat(nodes.Select(n => indent + n.Name + (n.IsFolder ? "/" : "") + "\n" + Dump(n.Children, indent + "  ")));

        [Fact]
        public void ChangeTree_GroupsByFolder_FoldersFirst_AndCompactsSingleFolderChains()
        {
            var tree = GitChangeTree.Build(new[]
            {
                E("README.md"),
                E("src/app/core/A.cs"),
                E("src/app/core/B.cs"),
                E("src/app/ui/View.xaml"),
                E("docs/guide/한글.md"),
                E("Zeta.txt"),
                new GitStatusEntry { Path = "newdir/", IsUntracked = true }
            });

            Assert.Equal(
                "docs/guide/\n" +          // docs → guide 한 줄로 합침
                "  한글.md\n" +
                "src/app/\n" +             // src → app 은 합치고, app 아래에서 core/ui로 갈라짐
                "  core/\n" +
                "    A.cs\n" +
                "    B.cs\n" +
                "  ui/\n" +
                "    View.xaml\n" +
                "newdir/\n" +              // 추적 안 되는 폴더는 파일 항목(폴더 노드 아님)
                "README.md\n" +
                "Zeta.txt\n", Dump(tree));

            var src = tree.Single(n => n.Name == "src/app");
            Assert.Equal("src/app", src.Path);
            Assert.Equal(3, src.Entries.Count());                       // 폴더 스테이지 대상 = 아래 전체
            Assert.Contains("(3)", src.DisplayText);
            Assert.False(tree.Single(n => n.Name == "newdir/").IsFolder);
        }

        [Fact]
        public void ChangeTree_RenameShowsOriginal_AndEmptyInputGivesEmptyTree()
        {
            var tree = GitChangeTree.Build(new[] { new GitStatusEntry { Path = "b/new.cs", OriginalPath = "a/old.cs", IndexState = 'R' } });
            var leaf = tree.Single().Children.Single();
            Assert.Equal("new.cs", leaf.Name);
            Assert.Contains("← a/old.cs", leaf.DisplayText);
            Assert.StartsWith("R.", leaf.DisplayText);

            Assert.Empty(GitChangeTree.Build(new GitStatusEntry[0]));
        }

        // ── diff 보기 모드 (변경점만 / 문맥 10줄 / 전체 파일) ──────────────────

        [Fact]
        public void WithViewMode_InsertsContextRightAfterSubcommand()
        {
            var args = GitDiffCommands.WorkTree("a.txt").Arguments;

            Assert.Equal(args, GitDiffCommands.WithViewMode(args, GitDiffViewMode.ChangesOnly));
            var ten = GitDiffCommands.WithViewMode(args, GitDiffViewMode.Context10);
            Assert.Equal("diff", ten[0]);
            Assert.Equal("-U10", ten[1]);
            Assert.True(ten.IndexOf("-U10") < ten.IndexOf("--"));      // 경로 구분자보다 앞
            Assert.Equal("-U" + GitDiffCommands.FullFileContext, GitDiffCommands.WithViewMode(args, GitDiffViewMode.FullFile)[1]);
            Assert.Equal(args.Count, GitDiffCommands.WorkTree("a.txt").Arguments.Count);   // 원본은 바뀌지 않음
        }

        [SkippableFact]
        public async Task RealGit_ViewModes_ShowChangesOnly_TenLines_OrWholeFile()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var repo = await InitRepoAsync("viewmode");
            var lines = Enumerable.Range(1, 40).Select(i => "line" + i).ToList();
            File.WriteAllText(Path.Combine(repo, "f.txt"), string.Join("\n", lines) + "\n");
            await CommitAllAsync(repo, "init");
            lines[19] = "CHANGED";   // 20번째 줄
            File.WriteAllText(Path.Combine(repo, "f.txt"), string.Join("\n", lines) + "\n");
            // 사용자 diff.context 설정보다 보기 모드가 우선해야 한다.
            await Git(repo, "config", "diff.context", "1");

            async Task<int[]> ShownNewLines(GitDiffViewMode mode)
            {
                var result = await Run(repo, GitDiffCommands.WithViewMode(GitDiffCommands.WorkTree("f.txt").Arguments, mode));
                return GitOutputParser.ParseDiff(result.StdOut).Where(l => l.NewLine.HasValue).Select(l => l.NewLine.Value).ToArray();
            }

            Assert.Equal(new[] { 19, 20, 21 }, await ShownNewLines(GitDiffViewMode.ChangesOnly));   // 사용자 설정(1줄) 그대로
            Assert.Equal(Enumerable.Range(10, 21).ToArray(), await ShownNewLines(GitDiffViewMode.Context10));
            Assert.Equal(Enumerable.Range(1, 40).ToArray(), await ShownNewLines(GitDiffViewMode.FullFile));
        }

        // ── 커밋 안 된 새 폴더 안 파일 / 변경 없는 파일 ─────────────────────────

        [SkippableFact]
        public async Task RealGit_Status_ListsEveryFileInsideNewFolder()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var repo = await InitRepoAsync("untrackedall");
            File.WriteAllText(Path.Combine(repo, "keep.txt"), "k\n");
            await CommitAllAsync(repo, "init");
            Directory.CreateDirectory(Path.Combine(repo, "newdir", "sub"));
            File.WriteAllText(Path.Combine(repo, "newdir", "a.txt"), "a\n");
            File.WriteAllText(Path.Combine(repo, "newdir", "sub", "b.txt"), "b\n");
            File.WriteAllText(Path.Combine(repo, ".gitignore"), "*.log\n");
            File.WriteAllText(Path.Combine(repo, "newdir", "skip.log"), "x\n");

            var status = GitOutputParser.ParseStatusV2((await Git(repo, GitOutputParser.StatusArguments)).StdOut);
            var untracked = status.Entries.Where(e => e.IsUntracked).Select(e => e.Path).OrderBy(p => p, StringComparer.Ordinal).ToArray();

            // 폴더 한 줄("newdir/")이 아니라 파일 하나하나. .gitignore에 걸린 파일은 나오지 않는다.
            Assert.Equal(new[] { ".gitignore", "newdir/a.txt", "newdir/sub/b.txt" }, untracked);
        }

        [SkippableFact]
        public async Task RealGit_UnchangedEntries_AreTrackedFilesMinusChanges()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var repo = await InitRepoAsync("unchanged");
            foreach (var name in new[] { "same.txt", "edit.txt", "old.txt", "dir/deep.txt" })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(repo, name)));
                File.WriteAllText(Path.Combine(repo, name), name + "\n");
            }
            await CommitAllAsync(repo, "init");
            File.WriteAllText(Path.Combine(repo, "edit.txt"), "changed\n");
            await Git(repo, "mv", "old.txt", "renamed.txt");
            File.WriteAllText(Path.Combine(repo, "new.txt"), "n\n");

            var status = GitOutputParser.ParseStatusV2((await Git(repo, GitOutputParser.StatusArguments)).StdOut);
            var tracked = GitOutputParser.ParseNulList((await Git(repo, GitOutputParser.TrackedFilesArguments)).StdOut);
            var unchanged = GitOutputParser.UnchangedEntries(tracked, status);

            Assert.Equal(new[] { "dir/deep.txt", "same.txt" }, unchanged.Select(e => e.Path).OrderBy(p => p, StringComparer.Ordinal));
            Assert.All(unchanged, e => Assert.True(e.IsUnchanged && !e.IsStaged && !e.IsUnstaged));
        }

        [Fact]
        public void UnchangedEntries_DeduplicatesConflictStages_AndContentLinesNumbersEveryLine()
        {
            var snapshot = new GitStatusSnapshot();
            var unchanged = GitOutputParser.UnchangedEntries(new[] { "a.txt", "a.txt", "b.txt" }, snapshot);
            Assert.Equal(new[] { "a.txt", "b.txt" }, unchanged.Select(e => e.Path));
            Assert.Equal(new[] { "x", "y" }, GitOutputParser.ParseNulList("x\0y\0"));

            var lines = GitOutputParser.ContentLines("첫째\r\n둘째\n셋째\n", 2);
            Assert.Equal(3, lines.Count);
            Assert.Equal((1, "첫째"), (lines[0].NewLine.Value, lines[0].Text));
            Assert.Equal(GitDiffLineKind.Context, lines[1].Kind);
            Assert.Equal(GitDiffLineKind.Meta, lines[2].Kind);        // 한도 초과 안내
            Assert.Empty(GitOutputParser.ContentLines(""));
        }

        // ── 상태 배지 ───────────────────────────────────────────────────────────

        [Fact]
        public void StatusKind_DependsOnListSide_AndLabelsAreKorean()
        {
            var output = string.Join("\0",
                "# branch.oid abc",
                "# branch.head main",
                "1 AM N... 000000 100644 100644 0000 aaaa new-then-edited.cs",   // 스테이지: 추가 / 작업 트리: 수정
                "1 .M N... 100644 100644 100644 aaaa bbbb edited.cs",
                "1 .D N... 100644 100644 000000 aaaa bbbb removed.cs",
                "2 R. N... 100644 100644 100644 aaaa bbbb R100 b/new.cs",
                "a/old.cs",
                "u UU N... 100644 100644 100644 100644 aaaa bbbb cccc both.cs",
                "? brand-new.txt",
                "");
            var s = GitOutputParser.ParseStatusV2(output);
            GitStatusEntry Get(string path) => s.Entries.Single(e => e.Path == path);

            Assert.Equal(GitChangeKind.Modified, Get("new-then-edited.cs").ForSide(GitChangeSide.WorkTree).Kind);
            Assert.Equal(GitChangeKind.Added, Get("new-then-edited.cs").ForSide(GitChangeSide.Index).Kind);
            Assert.Equal("삭제", Get("removed.cs").ForSide(GitChangeSide.WorkTree).StatusLabel);
            Assert.Equal("이름 변경", Get("b/new.cs").ForSide(GitChangeSide.Index).StatusLabel);
            Assert.Equal("b/new.cs  ← a/old.cs", Get("b/new.cs").ForSide(GitChangeSide.Index).PathText);
            Assert.Equal("충돌", Get("both.cs").StatusLabel);
            Assert.Equal("새 파일", Get("brand-new.txt").StatusLabel);
            Assert.Equal("edited.cs", Get("edited.cs").PathText);
            Assert.Equal("변경 없음", new GitStatusEntry { Path = "x", IsUnchanged = true }.StatusLabel);

            // 사본은 원본을 바꾸지 않는다.
            var original = Get("new-then-edited.cs");
            original.ForSide(GitChangeSide.Index);
            Assert.Equal(GitChangeSide.WorkTree, original.Side);
        }

        [Fact]
        public void Summarize_CountsByKind_MostFirst()
        {
            var entries = new[]
            {
                new GitStatusEntry { Path = "a", WorkTreeState = 'M' },
                new GitStatusEntry { Path = "b", WorkTreeState = 'M' },
                new GitStatusEntry { Path = "c", IsUntracked = true },
                new GitStatusEntry { Path = "d", WorkTreeState = 'D' },
                new GitStatusEntry { Path = "e", IsUntracked = true },
                new GitStatusEntry { Path = "f", IsUntracked = true }
            };

            Assert.Equal("새 파일 3 · 수정 2 · 삭제 1", GitStatusEntry.Summarize(entries));
            Assert.Equal(string.Empty, GitStatusEntry.Summarize(new GitStatusEntry[0]));

            var node = GitChangeTree.Build(new[] { entries[2] }).Single();
            Assert.Equal("새 파일", node.StatusLabel);
            Assert.Equal("c", node.LabelText);
        }

        // ── reset · 브랜치 · 체크아웃 · 워킹트리 ───────────────────────────────

        private async Task<(string repo, string first, string second)> TwoCommitRepoAsync(string name)
        {
            var repo = await InitRepoAsync(name);
            File.WriteAllText(Path.Combine(repo, "f.txt"), "one\n");
            await CommitAllAsync(repo, "first");
            File.WriteAllText(Path.Combine(repo, "f.txt"), "two\n");
            await CommitAllAsync(repo, "second");
            var log = GitOutputParser.ParseLog((await Git(repo, "log", "-z", GitOutputParser.LogFormat)).StdOut);
            return (repo, log[1].Hash, log[0].Hash);
        }

        private static async Task<GitStatusSnapshot> StatusAsync(string repo) =>
            GitOutputParser.ParseStatusV2((await Git(repo, GitOutputParser.StatusArguments)).StdOut);

        private static Task<GitResult> RunArgs(string repo, System.Collections.Generic.IEnumerable<string> args) =>
            GitCommandRunner.RunAsync(repo, args, GitCommandRunner.QueryTimeout, CancellationToken.None);

        [SkippableFact]
        public async Task RealGit_ResetModes_MoveHead_AndKeepOrDropChanges()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");

            var (soft, softFirst, _) = await TwoCommitRepoAsync("reset-soft");
            Assert.True((await RunArgs(soft, GitRefCommands.Reset(GitResetMode.Soft, softFirst))).Success);
            var s1 = await StatusAsync(soft);
            Assert.Equal(softFirst, s1.HeadOid);
            Assert.True(s1.Entries.Single().IsStaged);                       // 변경은 스테이지에 남음

            var (mixed, mixedFirst, _) = await TwoCommitRepoAsync("reset-mixed");
            Assert.True((await RunArgs(mixed, GitRefCommands.Reset(GitResetMode.Mixed, mixedFirst))).Success);
            var s2 = await StatusAsync(mixed);
            Assert.False(s2.Entries.Single().IsStaged);                      // 스테이지는 풀리고
            Assert.Equal("two\n", File.ReadAllText(Path.Combine(mixed, "f.txt")));   // 작업 트리는 그대로

            var (hard, hardFirst, _) = await TwoCommitRepoAsync("reset-hard");
            File.WriteAllText(Path.Combine(hard, "f.txt"), "uncommitted\n");
            Assert.True((await RunArgs(hard, GitRefCommands.Reset(GitResetMode.Hard, hardFirst))).Success);
            Assert.Empty((await StatusAsync(hard)).Entries);                 // 커밋 안 한 변경까지 사라짐
            Assert.Equal("one\n", File.ReadAllText(Path.Combine(hard, "f.txt")));
        }

        private static Task<GitResult> RestoreAsync(string repo, GitRestoreMode mode, System.Collections.Generic.IEnumerable<GitStatusEntry> targets) =>
            GitCommandRunner.RunAsync(repo, GitRestoreCommands.Arguments(mode), GitCommandRunner.QueryTimeout, CancellationToken.None,
                standardInput: GitRestoreCommands.PathspecInput(targets));

        [Fact]
        public void WindowState_RoundTrips_AndBadValuesFallBackToDefaults()
        {
            var path = P("git-window.xml");
            Assert.False(GitWindowStateService.Load(path).ChangesTreeMode);   // 파일 없음 → 기본값

            GitWindowStateService.Save(new GitWindowState { ChangesTreeMode = true, ChangesListWidth = 412.5, UnstagedWeight = 0.75, StagedWeight = 1.25 }, path);
            var loaded = GitWindowStateService.Load(path);
            Assert.True(loaded.ChangesTreeMode);
            Assert.Equal(412.5, loaded.ChangesListWidth);
            Assert.Equal(0.75, loaded.UnstagedWeight);
            Assert.Equal(1.25, loaded.StagedWeight);

            // 음수·NaN·한쪽만 있는 비율은 버리고, 범위를 벗어난 너비는 잘라 낸다.
            File.WriteAllText(path, "<gitWindow changesTree=\"x\" listWidth=\"99999\" unstagedWeight=\"NaN\" stagedWeight=\"2\"/>");
            loaded = GitWindowStateService.Load(path);
            Assert.False(loaded.ChangesTreeMode);
            Assert.Equal(GitWindowState.MaxListWidth, loaded.ChangesListWidth);
            Assert.Equal(1, loaded.UnstagedWeight);
            Assert.Equal(1, loaded.StagedWeight);

            File.WriteAllText(path, "<broken");
            Assert.Equal(GitWindowState.DefaultListWidth, GitWindowStateService.Load(path).ChangesListWidth);
        }

        [Fact]
        public void Restore_ExcludesEntriesGitRejectsOrWouldDeleteOrEmpty()
        {
            GitStatusEntry E(char x, char y, bool untracked = false, bool conflict = false) =>
                new GitStatusEntry { Path = "p", IndexState = x, WorkTreeState = y, IsUntracked = untracked, IsConflicted = conflict };
            var wt = GitRestoreMode.WorkTree;
            var both = GitRestoreMode.WorkTreeAndIndex;

            Assert.Null(GitRestoreCommands.ExclusionReason(E('.', 'M'), wt, true));
            Assert.Null(GitRestoreCommands.ExclusionReason(E('A', 'M'), wt, true));        // 스테이지된 새 파일의 추가 수정 → 스테이지 내용으로
            Assert.NotNull(GitRestoreCommands.ExclusionReason(E('?', '?', untracked: true), wt, true));
            Assert.NotNull(GitRestoreCommands.ExclusionReason(E('.', 'A'), wt, true));     // add -N: 빈 파일이 됨
            Assert.NotNull(GitRestoreCommands.ExclusionReason(E('U', 'U', conflict: true), wt, true));

            Assert.Null(GitRestoreCommands.ExclusionReason(E('M', 'M'), both, true));
            Assert.Null(GitRestoreCommands.ExclusionReason(E('U', 'U', conflict: true), both, true));
            Assert.Null(GitRestoreCommands.ExclusionReason(E('A', 'U', conflict: true), both, true));   // 우리 쪽에 있음
            Assert.NotNull(GitRestoreCommands.ExclusionReason(E('U', 'A', conflict: true), both, true)); // 우리 쪽에 없음 → 삭제됨
            Assert.NotNull(GitRestoreCommands.ExclusionReason(E('A', 'M'), both, true));   // HEAD에 없음 → 삭제됨
            Assert.NotNull(GitRestoreCommands.ExclusionReason(E('R', 'M'), both, true));
            Assert.NotNull(GitRestoreCommands.ExclusionReason(E('M', 'M'), both, false));  // 첫 커밋 전
        }

        [SkippableFact]
        public async Task RealGit_Restore_WorkTreeKeepsIndex_HeadModeDropsBoth_ExcludedEntriesDoNotBreakCommand()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var repo = await InitRepoAsync("restore");
            File.WriteAllText(Path.Combine(repo, "a.txt"), "a\n");
            File.WriteAllText(Path.Combine(repo, "b.txt"), "b\n");
            await CommitAllAsync(repo, "init");

            // a: 스테이지(a2) + 추가 수정(a3), b: 작업 트리에서 삭제, 새 파일, add -N 파일, 스테이지된 새 파일 + 추가 수정
            File.WriteAllText(Path.Combine(repo, "a.txt"), "a2\n");
            await Git(repo, "add", "a.txt");
            File.WriteAllText(Path.Combine(repo, "a.txt"), "a3\n");
            File.Delete(Path.Combine(repo, "b.txt"));
            File.WriteAllText(Path.Combine(repo, "new.txt"), "keep\n");
            File.WriteAllText(Path.Combine(repo, "ita.txt"), "keep\n");
            await Git(repo, "add", "-N", "ita.txt");
            File.WriteAllText(Path.Combine(repo, "added.txt"), "s\n");
            await Git(repo, "add", "added.txt");
            File.WriteAllText(Path.Combine(repo, "added.txt"), "s2\n");

            var unstaged = (await StatusAsync(repo)).Entries.Where(entry => entry.IsUnstaged).ToList();
            Assert.Equal(5, unstaged.Count);

            // 작업 트리 모드: 새 파일·add -N은 빠지고, 나머지로 명령이 성공한다.
            var targets = GitRestoreCommands.Restorable(unstaged, GitRestoreMode.WorkTree, hasHead: true);
            Assert.Equal(new[] { "a.txt", "added.txt", "b.txt" }, targets.Select(t => t.Path).OrderBy(p => p, StringComparer.Ordinal));
            var result = await RestoreAsync(repo, GitRestoreMode.WorkTree, targets);
            Assert.True(result.Success, result.StdErr);
            Assert.Equal("a2\n", File.ReadAllText(Path.Combine(repo, "a.txt")));        // 스테이지 내용으로
            Assert.Equal("b\n", File.ReadAllText(Path.Combine(repo, "b.txt")));          // 삭제 복구
            Assert.Equal("s\n", File.ReadAllText(Path.Combine(repo, "added.txt")));
            Assert.Equal("keep\n", File.ReadAllText(Path.Combine(repo, "new.txt")));     // 건드리지 않음
            Assert.Equal("keep\n", File.ReadAllText(Path.Combine(repo, "ita.txt")));     // 비워지지 않음
            var status = await StatusAsync(repo);
            Assert.True(status.Entries.Single(entry => entry.Path == "a.txt").IsStaged);  // 스테이지는 남음

            // HEAD 모드: a는 HEAD로, 스테이지된 새 파일은 빠져서 디스크에 남는다.
            File.WriteAllText(Path.Combine(repo, "a.txt"), "a4\n");
            File.WriteAllText(Path.Combine(repo, "added.txt"), "s3\n");
            unstaged = (await StatusAsync(repo)).Entries.Where(entry => entry.IsUnstaged).ToList();
            targets = GitRestoreCommands.Restorable(unstaged, GitRestoreMode.WorkTreeAndIndex, hasHead: true);
            Assert.Equal(new[] { "a.txt" }, targets.Select(t => t.Path));
            result = await RestoreAsync(repo, GitRestoreMode.WorkTreeAndIndex, targets);
            Assert.True(result.Success, result.StdErr);
            Assert.Equal("a\n", File.ReadAllText(Path.Combine(repo, "a.txt")));
            Assert.DoesNotContain((await StatusAsync(repo)).Entries, entry => entry.Path == "a.txt");
            Assert.Equal("s3\n", File.ReadAllText(Path.Combine(repo, "added.txt")));
        }

        [SkippableFact]
        public async Task RealGit_Restore_HeadModeResolvesConflictToHead_WorkTreeModeExcludesIt()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var (repo, first, _) = await TwoCommitRepoAsync("restore-conflict");
            await Git(repo, "switch", "-c", "other", first);
            File.WriteAllText(Path.Combine(repo, "f.txt"), "other\n");
            await CommitAllAsync(repo, "other");
            await Git(repo, "switch", "main");
            Assert.False((await Git(repo, "merge", "other")).Success);

            var unstaged = (await StatusAsync(repo)).Entries.Where(entry => entry.IsUnstaged).ToList();
            Assert.True(unstaged.Single().IsConflicted);
            Assert.Empty(GitRestoreCommands.Restorable(unstaged, GitRestoreMode.WorkTree, hasHead: true));

            var targets = GitRestoreCommands.Restorable(unstaged, GitRestoreMode.WorkTreeAndIndex, hasHead: true);
            var result = await RestoreAsync(repo, GitRestoreMode.WorkTreeAndIndex, targets);
            Assert.True(result.Success, result.StdErr);
            Assert.Equal("two\n", File.ReadAllText(Path.Combine(repo, "f.txt")));
            Assert.Empty((await StatusAsync(repo)).Entries);
        }

        [SkippableFact]
        public async Task RealGit_CreateBranchFromCommit_WithOrWithoutSwitch_AndDetachedCheckout()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var (repo, first, second) = await TwoCommitRepoAsync("branches");

            Assert.True((await RunArgs(repo, GitRefCommands.CheckBranchName("feature/ok"))).Success);
            Assert.False((await RunArgs(repo, GitRefCommands.CheckBranchName("-bad"))).Success);
            Assert.False((await RunArgs(repo, GitRefCommands.CheckBranchName("bad name"))).Success);

            // 전환 없이: 현재 브랜치는 그대로, 새 브랜치는 첫 커밋을 가리킴
            Assert.True((await RunArgs(repo, GitRefCommands.CreateBranch("from-first", first, switchAfter: false))).Success);
            Assert.Equal("main", (await StatusAsync(repo)).Branch);
            Assert.Equal(first, (await Git(repo, "rev-parse", "from-first")).StdOut.Trim());

            // 전환하며: 새 브랜치로 옮겨가고 HEAD가 시작점
            Assert.True((await RunArgs(repo, GitRefCommands.CreateBranch("switched", first, switchAfter: true))).Success);
            var status = await StatusAsync(repo);
            Assert.Equal("switched", status.Branch);
            Assert.Equal(first, status.HeadOid);

            // 시작점 비움 = 현재 HEAD
            Assert.True((await RunArgs(repo, GitRefCommands.CreateBranch("here", null, switchAfter: false))).Success);
            Assert.Equal(first, (await Git(repo, "rev-parse", "here")).StdOut.Trim());

            // detached
            Assert.True((await RunArgs(repo, GitRefCommands.CheckoutDetached(second))).Success);
            status = await StatusAsync(repo);
            Assert.True(status.IsDetached);
            Assert.Equal(second, status.HeadOid);
        }

        [SkippableFact]
        public async Task RealGit_Worktree_AddListRemovePrune()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var (repo, first, _) = await TwoCommitRepoAsync("wt-main");
            await Git(repo, "branch", "existing");

            var newWt = P("wt new");        // 공백 있는 경로
            var existingWt = P("wt-existing");
            Assert.True((await RunArgs(repo, GitRefCommands.WorktreeAdd(newWt, "wt-branch", first))).Success);
            Assert.True((await RunArgs(repo, GitRefCommands.WorktreeAdd(existingWt, null, "existing"))).Success);
            // 같은 브랜치는 두 곳에서 체크아웃할 수 없다.
            Assert.False((await RunArgs(repo, GitRefCommands.WorktreeAdd(P("wt-dup"), null, "existing"))).Success);

            var list = GitOutputParser.ParseWorktrees((await RunArgs(repo, GitRefCommands.WorktreeList)).StdOut);
            Assert.Equal(3, list.Count);
            Assert.True(list[0].IsMain);
            Assert.Equal("main", list[0].Branch);
            var added = list.Single(w => w.Branch == "wt-branch");
            Assert.Equal(Path.GetFullPath(newWt), Path.GetFullPath(added.Path));
            Assert.Equal(first, added.Head);
            Assert.True(File.Exists(Path.Combine(newWt, ".git")));          // 워킹트리는 .git 파일 → 탐색기가 저장소로 인식

            // 변경이 있으면 제거 거부, 없으면 제거
            File.WriteAllText(Path.Combine(existingWt, "dirty.txt"), "x");
            Assert.False((await RunArgs(repo, GitRefCommands.WorktreeRemove(existingWt))).Success);
            Assert.True((await RunArgs(repo, GitRefCommands.WorktreeRemove(newWt))).Success);
            Assert.False(Directory.Exists(newWt));

            // 폴더를 직접 지우면 prunable → prune으로 정리
            foreach (var f in Directory.EnumerateFiles(existingWt, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(existingWt, true);
            list = GitOutputParser.ParseWorktrees((await RunArgs(repo, GitRefCommands.WorktreeList)).StdOut);
            Assert.True(list.Single(w => w.Branch == "existing").IsPrunable);
            Assert.True((await RunArgs(repo, GitRefCommands.WorktreePrune)).Success);
            Assert.Single(GitOutputParser.ParseWorktrees((await RunArgs(repo, GitRefCommands.WorktreeList)).StdOut));
        }

        [Fact]
        public void ParseWorktrees_ReadsAllFlags()
        {
            var output = "worktree C:/repo\nHEAD aaaa\nbranch refs/heads/main\n\n" +
                         "worktree C:/wt/det\nHEAD bbbbbbbbbb\ndetached\nlocked reason\n\n" +
                         "worktree C:/gone\nHEAD cccc\nbranch refs/heads/x\nprunable gitdir file points to non-existent location\n\n";

            var list = GitOutputParser.ParseWorktrees(output);

            Assert.Equal(3, list.Count);
            Assert.True(list[0].IsMain && !list[1].IsMain);
            Assert.Equal("main", list[0].Branch);
            Assert.True(list[1].IsDetached && list[1].IsLocked);
            Assert.Null(list[1].Branch);
            Assert.Contains("(detached @bbbbbbb)", list[1].DisplayText);
            Assert.True(list[2].IsPrunable);
            Assert.Contains("prune", list[2].DisplayText);
        }

        // ── ▾ 옵션: pull · push · fetch · 커밋 · 강제 삭제 ──────────────────────

        /// <summary>bare 원격 + 두 클론(a, b). 둘 다 main이 origin/main을 추적한다.</summary>
        private async Task<(string remote, string a, string b)> RemoteWithTwoClonesAsync(string name)
        {
            var remote = P(name + ".git");
            Assert.True((await Git(null, "init", "--bare", "-b", "main", remote)).Success);
            var a = P(name + "-a");
            var b = P(name + "-b");
            Assert.True((await Git(null, "clone", "-q", remote, a)).Success);
            foreach (var repo in new[] { a })
            {
                await Git(repo, "config", "user.name", "t");
                await Git(repo, "config", "user.email", "t@example.com");
                await Git(repo, "config", "commit.gpgsign", "false");
            }
            await Git(a, "switch", "-q", "-c", "main");
            File.WriteAllText(Path.Combine(a, "f.txt"), "base\n");
            await CommitAllAsync(a, "base");
            Assert.True((await Git(a, "push", "-q", "-u", "origin", "main")).Success);
            Assert.True((await Git(null, "clone", "-q", remote, b)).Success);
            await Git(b, "config", "user.name", "t");
            await Git(b, "config", "user.email", "t@example.com");
            await Git(b, "config", "commit.gpgsign", "false");
            return (remote, a, b);
        }

        /// <summary>a와 b가 서로 다른 커밋을 해서 갈라진 상태를 만든다(b가 먼저 push).</summary>
        private async Task DivergeAsync(string a, string b)
        {
            File.WriteAllText(Path.Combine(b, "b.txt"), "from b\n");
            await CommitAllAsync(b, "b commit");
            Assert.True((await Git(b, "push", "-q")).Success);
            File.WriteAllText(Path.Combine(a, "a.txt"), "from a\n");
            await CommitAllAsync(a, "a commit");
        }

        private static Task<GitResult> RunNet(string repo, System.Collections.Generic.IEnumerable<string> args) =>
            GitCommandRunner.RunAsync(repo, args, GitCommandRunner.NetworkTimeout, CancellationToken.None);

        [SkippableFact]
        public async Task RealGit_PullModes_OnDivergedBranches_AndAutoStash()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");

            var (_, a1, b1) = await RemoteWithTwoClonesAsync("pull-ff");
            await DivergeAsync(a1, b1);
            Assert.False((await RunNet(a1, GitSyncCommands.Pull(new GitPullOptions { Mode = GitPullMode.FastForwardOnly }))).Success);

            var (_, a2, b2) = await RemoteWithTwoClonesAsync("pull-merge");
            await DivergeAsync(a2, b2);
            Assert.True((await RunNet(a2, GitSyncCommands.Pull(new GitPullOptions { Mode = GitPullMode.Merge }))).Success);
            var head2 = GitOutputParser.ParseLog((await Git(a2, "log", "-z", "-n", "1", GitOutputParser.LogFormat)).StdOut).Single();
            Assert.Equal(2, head2.Parents.Length);                              // 병합 커밋

            var (_, a3, b3) = await RemoteWithTwoClonesAsync("pull-rebase");
            await DivergeAsync(a3, b3);
            File.WriteAllText(Path.Combine(a3, "f.txt"), "dirty\n");           // 커밋 안 한 변경
            Assert.False((await RunNet(a3, GitSyncCommands.Pull(new GitPullOptions { Mode = GitPullMode.Rebase }))).Success);
            Assert.True((await RunNet(a3, GitSyncCommands.Pull(new GitPullOptions { Mode = GitPullMode.Rebase, AutoStash = true }))).Success);
            var head3 = GitOutputParser.ParseLog((await Git(a3, "log", "-z", "-n", "1", GitOutputParser.LogFormat)).StdOut).Single();
            Assert.Single(head3.Parents);                                       // 일직선
            Assert.Equal("dirty\n", File.ReadAllText(Path.Combine(a3, "f.txt")));   // 변경 복원
        }

        [SkippableFact]
        public async Task RealGit_PushWithUpstreamAndTags_AndFetchPruneTags()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var (_, a, b) = await RemoteWithTwoClonesAsync("push");

            await Git(a, "switch", "-q", "-c", "feature");
            File.WriteAllText(Path.Combine(a, "n.txt"), "n\n");
            await CommitAllAsync(a, "feature commit");
            await Git(a, "tag", "-a", "v1", "-m", "release");
            Assert.Null((await StatusAsync(a)).Upstream);
            var push = await RunNet(a, GitSyncCommands.Push(new GitPushOptions { Remote = "origin", Branch = "feature", SetUpstream = true, FollowTags = true }));
            Assert.True(push.Success, push.StdErr);
            Assert.Equal("origin/feature", (await StatusAsync(a)).Upstream);

            // b: 태그와 feature 받기 → 원격에서 feature 삭제 → prune으로 추적 참조 정리
            Assert.True((await RunNet(b, GitSyncCommands.Fetch(new GitFetchOptions { Prune = true, Tags = true }))).Success);
            Assert.Contains("v1", (await Git(b, "tag")).StdOut);
            Assert.Contains("origin/feature", (await Git(b, "branch", "-r")).StdOut);
            Assert.True((await Git(a, "push", "-q", "origin", "--delete", "feature")).Success);
            Assert.True((await RunNet(b, GitSyncCommands.Fetch(new GitFetchOptions { Prune = false }))).Success);
            Assert.Contains("origin/feature", (await Git(b, "branch", "-r")).StdOut);   // prune 안 하면 남음
            Assert.True((await RunNet(b, GitSyncCommands.Fetch(new GitFetchOptions { Prune = true, AllRemotes = true }))).Success);
            Assert.DoesNotContain("origin/feature", (await Git(b, "branch", "-r")).StdOut);
        }

        [SkippableFact]
        public async Task RealGit_CommitOptions_AmendKeepsMessage_SignOff_AllowEmpty()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var (repo, _, second) = await TwoCommitRepoAsync("commitopts");

            File.WriteAllText(Path.Combine(repo, "g.txt"), "g\n");
            await Git(repo, "add", "g.txt");
            Assert.True((await RunArgs(repo, GitSyncCommands.Commit(null, new GitCommitOptions { Amend = true }))).Success);
            var log = GitOutputParser.ParseLog((await Git(repo, "log", "-z", GitOutputParser.LogFormat)).StdOut);
            Assert.Equal(2, log.Count);                                         // 커밋 수 그대로
            Assert.Equal("second", log[0].Subject);                             // 메시지 유지
            Assert.NotEqual(second, log[0].Hash);                               // 새 해시

            var messageFile = P("msg.txt");
            File.WriteAllText(messageFile, "empty one");
            Assert.False((await RunArgs(repo, GitSyncCommands.Commit(messageFile, new GitCommitOptions()))).Success);   // 스테이지 없음
            Assert.True((await RunArgs(repo, GitSyncCommands.Commit(messageFile, new GitCommitOptions { AllowEmpty = true, SignOff = true }))).Success);
            var body = (await Git(repo, "log", "-1", "--format=%B")).StdOut;
            Assert.Contains("empty one", body);
            Assert.Contains("Signed-off-by: t <t@example.com>", body);
        }

        [SkippableFact]
        public async Task RealGit_ForceDeleteBranch_AndForceRemoveDirtyWorktree()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var (repo, _, _) = await TwoCommitRepoAsync("force");
            await Git(repo, "switch", "-q", "-c", "unmerged");
            File.WriteAllText(Path.Combine(repo, "u.txt"), "u\n");
            await CommitAllAsync(repo, "unmerged work");
            await Git(repo, "switch", "-q", "main");

            Assert.False((await RunArgs(repo, GitSyncCommands.DeleteBranch("unmerged", force: false))).Success);
            Assert.True((await RunArgs(repo, GitSyncCommands.DeleteBranch("unmerged", force: true))).Success);
            Assert.DoesNotContain("unmerged", (await Git(repo, "branch")).StdOut);

            var wt = P("force-wt");
            Assert.True((await RunArgs(repo, GitRefCommands.WorktreeAdd(wt, "wtb", null))).Success);
            File.WriteAllText(Path.Combine(wt, "dirty.txt"), "x");
            Assert.False((await RunArgs(repo, GitRefCommands.WorktreeRemove(wt))).Success);
            Assert.True((await RunArgs(repo, GitRefCommands.WorktreeRemove(wt, force: true))).Success);
            Assert.False(Directory.Exists(wt));
        }

        // ── stash ──────────────────────────────────────────────────────────────

        private static async Task<System.Collections.Generic.List<GitStashInfo>> StashesAsync(string repo) =>
            GitOutputParser.ParseStashes((await RunArgs(repo, GitStashCommands.List)).StdOut);

        [SkippableFact]
        public async Task RealGit_StashPush_Options_AndList()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var (repo, _, _) = await TwoCommitRepoAsync("stash-push");

            // 기본: 추적 파일 변경만, 새 파일은 남는다
            File.WriteAllText(Path.Combine(repo, "f.txt"), "edited\n");
            File.WriteAllText(Path.Combine(repo, "new.txt"), "n\n");
            Assert.True((await RunArgs(repo, GitStashCommands.Push(new GitStashPushOptions()))).Success);
            Assert.Equal("two\n", File.ReadAllText(Path.Combine(repo, "f.txt")));
            Assert.True(File.Exists(Path.Combine(repo, "new.txt")));

            // -u + 메시지: 새 파일까지 치운다
            File.WriteAllText(Path.Combine(repo, "f.txt"), "edited2\n");
            Assert.True((await RunArgs(repo, GitStashCommands.Push(new GitStashPushOptions { Message = "한글 메시지", IncludeUntracked = true }))).Success);
            Assert.False(File.Exists(Path.Combine(repo, "new.txt")));

            var list = await StashesAsync(repo);
            Assert.Equal(2, list.Count);
            Assert.Equal("stash@{0}", list[0].Ref);
            Assert.Contains("한글 메시지", list[0].Subject);
            Assert.True(list[0].HasUntracked);
            Assert.False(list[1].HasUntracked);
            Assert.StartsWith("WIP on main", list[1].Subject);
            Assert.Equal(new[] { "new.txt" }, GitOutputParser.ParseNulList((await RunArgs(repo, GitStashCommands.UntrackedFiles(list[0].Hash))).StdOut));

            // --keep-index: 스테이지한 변경은 작업 트리에 남는다
            File.WriteAllText(Path.Combine(repo, "f.txt"), "staged\n");
            await Git(repo, "add", "f.txt");
            Assert.True((await RunArgs(repo, GitStashCommands.Push(new GitStashPushOptions { KeepIndex = true }))).Success);
            Assert.Equal("staged\n", File.ReadAllText(Path.Combine(repo, "f.txt")));

            // 선택한 파일만(pathspec을 표준 입력으로)
            await Git(repo, "reset", "-q", "--hard");
            File.WriteAllText(Path.Combine(repo, "f.txt"), "keep me\n");
            Directory.CreateDirectory(Path.Combine(repo, "d"));
            File.WriteAllText(Path.Combine(repo, "d", "g.txt"), "g\n");
            await Git(repo, "add", "d/g.txt");
            await Git(repo, "commit", "-q", "-m", "g");
            File.WriteAllText(Path.Combine(repo, "d", "g.txt"), "stash only this\n");
            var only = await GitCommandRunner.RunAsync(repo, GitStashCommands.Push(new GitStashPushOptions(), usePathspecStdin: true),
                GitCommandRunner.QueryTimeout, CancellationToken.None, standardInput: "d/g.txt\0");
            Assert.True(only.Success, only.StdErr);
            Assert.Equal("keep me\n", File.ReadAllText(Path.Combine(repo, "f.txt")));   // 고르지 않은 파일은 그대로
            Assert.Equal("g\n", File.ReadAllText(Path.Combine(repo, "d", "g.txt")));
        }

        [SkippableFact]
        public async Task RealGit_StashApplyPopBranchDropClear()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var (repo, _, _) = await TwoCommitRepoAsync("stash-apply");

            File.WriteAllText(Path.Combine(repo, "f.txt"), "staged\n");
            await Git(repo, "add", "f.txt");
            await RunArgs(repo, GitStashCommands.Push(new GitStashPushOptions { Message = "s1" }));

            // apply --index: 스테이지 상태까지 복원, stash는 남음
            Assert.True((await RunArgs(repo, GitStashCommands.Apply("stash@{0}", GitStashApplyMode.Apply, restoreIndex: true))).Success);
            Assert.True((await StatusAsync(repo)).Entries.Single().IsStaged);
            Assert.Single(await StashesAsync(repo));

            // pop(스테이지 복원 없이): 변경은 작업 트리로, stash는 삭제
            await Git(repo, "reset", "-q", "--hard");
            Assert.True((await RunArgs(repo, GitStashCommands.Apply("stash@{0}", GitStashApplyMode.Pop, restoreIndex: false))).Success);
            var status = await StatusAsync(repo);
            Assert.False(status.Entries.Single().IsStaged);
            Assert.Empty(await StashesAsync(repo));

            // branch: 새 브랜치로 꺼내고 stash 삭제
            await RunArgs(repo, GitStashCommands.Push(new GitStashPushOptions { Message = "s2" }));
            Assert.True((await RunArgs(repo, GitStashCommands.Apply("stash@{0}", GitStashApplyMode.Branch, false, "from-stash"))).Success);
            Assert.Equal("from-stash", (await StatusAsync(repo)).Branch);
            Assert.Equal("staged\n", File.ReadAllText(Path.Combine(repo, "f.txt")));
            Assert.Empty(await StashesAsync(repo));

            // drop / clear
            await Git(repo, "reset", "-q", "--hard");
            File.WriteAllText(Path.Combine(repo, "f.txt"), "a\n");
            await RunArgs(repo, GitStashCommands.Push(new GitStashPushOptions { Message = "a" }));
            File.WriteAllText(Path.Combine(repo, "f.txt"), "b\n");
            await RunArgs(repo, GitStashCommands.Push(new GitStashPushOptions { Message = "b" }));
            Assert.True((await RunArgs(repo, GitStashCommands.Drop("stash@{0}"))).Success);
            var remaining = await StashesAsync(repo);
            Assert.Contains("a", remaining.Single().Subject);                 // 뒤 번호가 당겨짐
            Assert.Equal("stash@{0}", remaining.Single().Ref);
            Assert.True((await RunArgs(repo, GitStashCommands.Clear)).Success);
            Assert.Empty(await StashesAsync(repo));
        }
    }
}

using Folderss.Models;
using Folderss.Services;
using LibGit2Sharp;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>
    /// GitLibDiff(libgit2 xdiff)가 같은 요청의 git CLI 출력과 글자 단위로 같은 diff 텍스트를 내는지 실제 git으로 만든 저장소에서 비교한다.
    /// Git 창·두 파일 비교는 둘 중 하나로 diff를 만들므로 다르면 화면이 콘솔 git과 어긋난다.
    /// </summary>
    public sealed class GitLibDiffTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "folderss-lgdiff-" + Guid.NewGuid().ToString("N"));

        public GitLibDiffTests() => Directory.CreateDirectory(_root);

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
            await Git(repo, bare ? new[] { "init", "-q", "--bare", "-b", "main" } : new[] { "init", "-q", "-b", "main" });
            await Configure(repo);
            return repo;
        }

        private static async Task Configure(string repo)
        {
            await Git(repo, "config", "user.name", "테스터");
            await Git(repo, "config", "user.email", "t@example.com");
            await Git(repo, "config", "commit.gpgsign", "false");
            await Git(repo, "config", "core.autocrlf", "false");
        }

        private static void Write(string repo, string path, string text) => WriteBytes(repo, path, new UTF8Encoding(false).GetBytes(text));

        private static void WriteBytes(string repo, string path, byte[] bytes)
        {
            var full = Path.Combine(repo, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, bytes);
        }

        private static async Task<string> Rev(string repo, string spec) => (await Git(repo, "rev-parse", spec)).Trim();

        private static async Task<string[]> Parents(string repo, string hash) =>
            (await Git(repo, "rev-list", "--parents", "-n", "1", hash)).Trim().Split(' ').Skip(1).ToArray();

        /// <summary>같은 요청을 git CLI(Git 창이 쓰는 인수 그대로)와 libgit2로 만들어 비교한다.</summary>
        private static async Task AssertSameAsCli(string repo, GitDiffRequest request,
            GitDiffViewMode mode = GitDiffViewMode.ChangesOnly, Encoding fallback = null)
        {
            var args = GitDiffCommands.WithViewMode(request.Arguments, mode);
            var cli = await GitCommandRunner.RunAsync(repo, args, GitCommandRunner.QueryTimeout, CancellationToken.None,
                readOnly: true, fallbackEncoding: fallback);
            Assert.True(GitDiffCommands.IsSuccess(cli, request.NoIndex), string.Join(" ", args) + "\n" + cli.StdErr);
            var lib = GitLibDiff.Read(repo, request, mode, fallback);
            if (WithoutSimilarity(lib) != WithoutSimilarity(cli.StdOut))
                Assert.Fail("git " + string.Join(" ", args) + "\n--- git ---\n" + cli.StdOut + "\n--- libgit2 ---\n" + lib);
        }

        /// <summary>이름 변경 유사도 숫자는 libgit2와 git의 계산식이 달라 1%쯤 다를 수 있다(본문은 같음). 그 숫자만 빼고 비교한다.</summary>
        private static string WithoutSimilarity(string diff) =>
            System.Text.RegularExpressions.Regex.Replace(diff, @"^(dis)?similarity index \d+%$", "$1similarity index N%",
                System.Text.RegularExpressions.RegexOptions.Multiline);

        private const string CodeBefore =
            "class Sample\n{\n    void One()\n    {\n        Foo();\n    }\n\n    void Three()\n    {\n        Baz();\n    }\n";

        // 새 메서드를 끼워 넣는다. indent heuristic이 없으면 hunk가 "    }\n\n    void Two()"처럼 어긋나게 잡힌다.
        private const string CodeAfter =
            "class Sample\n{\n    void One()\n    {\n        Foo(1);\n    }\n\n    void Two()\n    {\n        Bar();\n    }\n\n    void Three()\n    {\n        Baz();\n    }\n";

        private static string Filler(string prefix, int count) =>
            string.Concat(Enumerable.Range(1, count).Select(i => prefix + " " + i + "\n"));

        [SkippableFact]
        public async Task WorkTreeStagedAndUntracked_MatchCli()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var cp949 = GitTextDecoder.Resolve(GitFallbackEncoding.Cp949);
            var repo = await InitAsync("wt");
            Write(repo, "code.cs", Filler("// 머리", 20) + CodeBefore + Filler("// 꼬리", 20) + "}\n");
            Write(repo, "noeol.txt", "one\ntwo");
            Write(repo, "slider.txt", "1\n2\na\n\nb\n3\n4\n");
            Write(repo, "ws.txt", "a b\n  c\nd\n");
            Write(repo, "del.txt", "gone\n");
            WriteBytes(repo, "bin.dat", new byte[] { 0, 1, 2, 3, 0, 5 });
            WriteBytes(repo, "cp949.txt", cp949.GetBytes("가나다\n라마바\n"));
            Write(repo, "old.txt", Filler("line", 20));
            Write(repo, "한글 파일.txt", "가\n나\n다\n");
            await Git(repo, "add", "-A");
            await Git(repo, "commit", "-q", "-m", "init");

            Write(repo, "code.cs", Filler("// 머리", 20) + CodeAfter + Filler("// 꼬리", 20) + "}\n");
            Write(repo, "noeol.txt", "one\nTWO");
            // git t4061의 예: indent heuristic이 없으면 같은 삽입을 "2" 뒤로 잡는다(libgit2는 기본이 꺼짐, git은 켜짐).
            Write(repo, "slider.txt", "1\n2\na\n\nb\na\n\nb\n3\n4\n");
            Write(repo, "ws.txt", "a  b\n\tc\nd\n");
            File.Delete(Path.Combine(repo, "del.txt"));
            WriteBytes(repo, "bin.dat", new byte[] { 0, 1, 9, 3, 0, 5 });
            WriteBytes(repo, "cp949.txt", cp949.GetBytes("가나다\n라마사\n"));
            await Git(repo, "mv", "old.txt", "new.txt");
            Write(repo, "new.txt", Filler("line", 19) + "changed\n");
            Write(repo, "added.txt", "new\n");
            Write(repo, "한글 파일.txt", "가\n나나\n다\n");
            await Git(repo, "add", "new.txt", "added.txt", "한글 파일.txt");
            Write(repo, "untracked.txt", "u1\nu2\n");
            Write(repo, "sub/dir/깊은.txt", "deep\n");

            foreach (var mode in new[] { GitDiffViewMode.ChangesOnly, GitDiffViewMode.Context10, GitDiffViewMode.FullFile })
                await AssertSameAsCli(repo, GitDiffCommands.WorkTree("code.cs"), mode);
            await AssertSameAsCli(repo, GitDiffCommands.WorkTree("noeol.txt"));
            await AssertSameAsCli(repo, GitDiffCommands.WorkTree("slider.txt"));
            await AssertSameAsCli(repo, GitDiffCommands.WorkTree("ws.txt"));
            await AssertSameAsCli(repo, GitDiffCommands.WorkTree("ws.txt", ignoreWhitespace: true));
            await AssertSameAsCli(repo, GitDiffCommands.WorkTree("del.txt"));
            await AssertSameAsCli(repo, GitDiffCommands.WorkTree("bin.dat"));
            await AssertSameAsCli(repo, GitDiffCommands.WorkTree("cp949.txt"), fallback: cp949);
            await AssertSameAsCli(repo, GitDiffCommands.Staged("new.txt", "old.txt"));
            await AssertSameAsCli(repo, GitDiffCommands.Staged("added.txt", null));
            await AssertSameAsCli(repo, GitDiffCommands.Staged("한글 파일.txt", null));
            await AssertSameAsCli(repo, GitDiffCommands.Untracked("untracked.txt"));
            await AssertSameAsCli(repo, GitDiffCommands.Untracked("sub/dir/깊은.txt"));
        }

        [SkippableFact]
        public async Task StagedBeforeFirstCommit_MatchCli()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var repo = await InitAsync("unborn");
            Write(repo, "first.txt", "hello\n");
            await Git(repo, "add", "first.txt");
            await AssertSameAsCli(repo, GitDiffCommands.Staged("first.txt", null));
        }

        [SkippableFact]
        public async Task CommitsAndUpstreamRanges_MatchCli()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var remote = await InitAsync("remote.git", bare: true);
            var repo = await InitAsync("local");
            Write(repo, "a.txt", Filler("a", 30));
            Write(repo, "b.txt", "b\n");
            await Git(repo, "add", "-A");
            await Git(repo, "commit", "-q", "-m", "root");
            var root = await Rev(repo, "HEAD");
            Write(repo, "a.txt", Filler("a", 30).Replace("a 11\n", "A11\n"));
            await Git(repo, "commit", "-q", "-am", "second");
            var second = await Rev(repo, "HEAD");
            await Git(repo, "switch", "-q", "-c", "feature");
            Write(repo, "c.txt", "feature\n");
            await Git(repo, "add", "c.txt");
            await Git(repo, "commit", "-q", "-m", "feature");
            await Git(repo, "switch", "-q", "main");
            Write(repo, "b.txt", "b\nmain\n");
            await Git(repo, "commit", "-q", "-am", "main");
            await Git(repo, "merge", "-q", "--no-ff", "-m", "merge", "feature");
            var merge = await Rev(repo, "HEAD");
            await Git(repo, "remote", "add", "origin", remote);
            await Git(repo, "push", "-q", "-u", "origin", "main");

            // 보낼 커밋 하나, 받을 커밋 하나, 커밋 안 한 수정 하나.
            Write(repo, "b.txt", "b\nmain\nlocal\n");
            await Git(repo, "commit", "-q", "-am", "local");
            var other = Path.Combine(_root, "other");
            await Git(null, "clone", "-q", "-c", "core.autocrlf=false", remote, other);
            await Configure(other);
            Write(other, "d.txt", "remote\n");
            await Git(other, "add", "d.txt");
            await Git(other, "commit", "-q", "-m", "remote");
            await Git(other, "push", "-q");
            await Git(repo, "fetch", "-q");
            Write(repo, "a.txt", "worktree\n" + Filler("a", 30));

            await AssertSameAsCli(repo, GitDiffCommands.Commit(root, new string[0]));
            await AssertSameAsCli(repo, GitDiffCommands.Commit(second, await Parents(repo, second)));
            await AssertSameAsCli(repo, GitDiffCommands.Commit(second, await Parents(repo, second)), GitDiffViewMode.FullFile);
            await AssertSameAsCli(repo, GitDiffCommands.Commit(merge, await Parents(repo, merge)));
            await AssertSameAsCli(repo, GitDiffCommands.Range(GitDiffCommands.Upstream + "...HEAD"));
            await AssertSameAsCli(repo, GitDiffCommands.Range("HEAD..." + GitDiffCommands.Upstream));
            await AssertSameAsCli(repo, GitDiffCommands.WorkTreeAgainst(GitDiffCommands.Upstream));
            await AssertSameAsCli(repo, GitDiffCommands.WorkTreeAgainst(GitDiffCommands.Upstream, ignoreWhitespace: true));
        }

        [SkippableFact]
        public async Task TwoFiles_MatchCli()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var dir = Path.Combine(_root, "두 파일");
            Write(dir, "one.txt", "가\n나\n다\n" + Filler("x", 30));
            Write(dir, "two.txt", "가\n나나\n다\n" + Filler("x", 29) + "y\n");
            Write(dir, "same.txt", "가\n나\n다\n" + Filler("x", 30));
            Write(dir, "empty.txt", "");
            WriteBytes(dir, "a.bin", new byte[] { 0, 1, 2 });
            WriteBytes(dir, "b.bin", new byte[] { 0, 1, 3 });
            string F(string name) => Path.Combine(dir, name);

            foreach (var mode in new[] { GitDiffViewMode.ChangesOnly, GitDiffViewMode.Context10, GitDiffViewMode.FullFile })
                await AssertSameAsCli(null, GitDiffCommands.Files(F("one.txt"), F("two.txt")), mode);
            await AssertSameAsCli(null, GitDiffCommands.Files(F("one.txt"), F("two.txt"), ignoreWhitespace: true));
            await AssertSameAsCli(null, GitDiffCommands.Files(F("one.txt"), F("same.txt")));
            await AssertSameAsCli(null, GitDiffCommands.Files(F("empty.txt"), F("one.txt")));
            await AssertSameAsCli(null, GitDiffCommands.Files(F("a.bin"), F("b.bin")));
        }

        [Fact]
        public void TwoFiles_LineEndingsDiffer_UnlessIgnoringWhitespace()
        {
            // git --no-index는 core.autocrlf=true(Git for Windows 기본)에서 줄 끝만 다른 파일을 같다고 본다. 내장 비교는 그대로 보인다.
            Write(_root, "lf.txt", "a\nb\n");
            Write(_root, "crlf.txt", "a\r\nb\r\n");
            var request = GitDiffCommands.Files(Path.Combine(_root, "lf.txt"), Path.Combine(_root, "crlf.txt"));
            var lines = GitOutputParser.ParseDiff(GitLibDiff.Read(null, request, GitDiffViewMode.ChangesOnly, null));
            Assert.Equal(2, lines.Count(l => l.Kind == GitDiffLineKind.Removed));
            Assert.Equal(2, lines.Count(l => l.Kind == GitDiffLineKind.Added));

            request = GitDiffCommands.Files(request.Paths[0], request.Paths[1], ignoreWhitespace: true);
            lines = GitOutputParser.ParseDiff(GitLibDiff.Read(null, request, GitDiffViewMode.ChangesOnly, null));
            Assert.DoesNotContain(lines, l => l.Kind == GitDiffLineKind.Removed || l.Kind == GitDiffLineKind.Added);
        }

        [SkippableFact]
        public async Task Unsupported_FallsBackToCli()
        {
            Skip.If(GitCommandRunner.FindGit() == null, "git이 설치되어 있지 않음");
            var repo = await InitAsync("fallback");
            Write(repo, "f.txt", "base\n");
            await Git(repo, "add", "-A");
            await Git(repo, "commit", "-q", "-m", "base");
            await Git(repo, "switch", "-q", "-c", "other");
            Write(repo, "f.txt", "other\n");
            await Git(repo, "commit", "-q", "-am", "other");
            await Git(repo, "switch", "-q", "main");
            Write(repo, "f.txt", "main\n");
            await Git(repo, "commit", "-q", "-am", "main");
            var merge = await GitCommandRunner.RunAsync(repo, new[] { "merge", "-q", "other" }, GitCommandRunner.QueryTimeout, CancellationToken.None);
            Assert.False(merge.Success);

            // 충돌 파일: git은 combined diff(@@@)를 보인다.
            Assert.Throws<NotSupportedException>(() => GitLibDiff.Read(repo, GitDiffCommands.WorkTree("f.txt"), GitDiffViewMode.ChangesOnly, null));
            await Git(repo, "merge", "--abort");

            await Git(repo, "config", "diff.algorithm", "histogram");
            Write(repo, "f.txt", "changed\n");
            Assert.Throws<NotSupportedException>(() => GitLibDiff.Read(repo, GitDiffCommands.WorkTree("f.txt"), GitDiffViewMode.ChangesOnly, null));
            await Git(repo, "config", "--unset", "diff.algorithm");

            Write(repo, ".gitattributes", "*.psd filter=lfs diff=lfs merge=lfs -text\n");
            Assert.Throws<NotSupportedException>(() => GitLibDiff.Read(repo, GitDiffCommands.WorkTree("f.txt"), GitDiffViewMode.ChangesOnly, null));
        }

        [Fact]
        public void NativeLibraryName_MatchesLibGit2Sharp()
        {
            Assert.Equal("git2-" + GlobalSettings.Version.LibGit2CommitSha, GitLibDiff.NativeLibraryName);
        }
    }
}

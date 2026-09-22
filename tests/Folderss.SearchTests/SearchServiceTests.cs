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
    /// SearchService는 순수 파일시스템 로직이라 WPF 없이 검증할 수 있다.
    /// 접근 거부 디렉터리 재현은 POSIX 권한(chmod 000)에 의존하므로 root로 실행하면 의미가 없다
    /// (root는 권한 검사를 우회) — 그런 환경에서는 해당 테스트를 Skip한다.
    /// </summary>
    public sealed class SearchServiceTests : IDisposable
    {
        private readonly string _root;

        public SearchServiceTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "folderss-search-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        // ── 하위 폴더 포함 검색이 중간에 통째로 끊기던 버그 ────────────────────────────────

        [SkippableFact]
        public void Recursive_WithInaccessibleSubdirectory_StillReturnsAccessibleMatches()
        {
            WriteFile("target-a.txt");
            WriteFile("sub/target-b.txt");
            var denied = Path.Combine(_root, "denied");
            Directory.CreateDirectory(denied);
            File.WriteAllText(Path.Combine(denied, "target-c.txt"), string.Empty);

            try
            {
                Skip.IfNot(TryDenyDirectoryListing(denied),
                    "이 환경에서는 폴더 접근 거부를 재현할 수 없다(root는 POSIX 권한 검사를 우회한다).");

                var results = Run("target", recursive: true);

                Assert.Equal(
                    new[] { "target-a.txt", "target-b.txt" },
                    SortedNames(results));
            }
            finally
            {
                Chmod(denied, "755");
            }
        }

        // 정션/심볼릭 링크 순환. Windows의 %LOCALAPPDATA%\Application Data 같은 정션이 같은 형태다.
        [SkippableFact(Timeout = 30000)]
        public async Task Recursive_WithSymlinkLoop_TerminatesAndReportsEachFileOnce()
        {
            WriteFile("a/target-a.txt");
            Skip.IfNot(TryCreateDirectorySymlink(Path.Combine(_root, "a", "loop"), _root),
                "이 환경에서는 디렉터리 심볼릭 링크를 만들 수 없다.");

            var results = await RunAsync("target", recursive: true);

            Assert.Equal(new[] { "target-a.txt" }, SortedNames(results));
        }

        [Fact]
        public void Recursive_FindsMatchesInNestedDirectories()
        {
            WriteFile("top.txt");
            WriteFile("a/nested.txt");
            WriteFile("a/b/deep.txt");

            var results = Run(".txt", recursive: true);

            Assert.Equal(new[] { "deep.txt", "nested.txt", "top.txt" }, SortedNames(results));
        }

        [Fact]
        public void NonRecursive_IgnoresSubdirectories()
        {
            WriteFile("top.txt");
            WriteFile("a/nested.txt");

            var results = Run(".txt");

            Assert.Equal(new[] { "top.txt" }, SortedNames(results));
        }

        [Fact]
        public void Search_OnMissingRoot_Completes_WithoutResults()
        {
            Directory.Delete(_root, true);

            var results = Run("anything", recursive: true);

            Assert.Empty(results);
        }

        // ── 확장자 필드를 대체하는 와일드카드 패턴 검색 ─────────────────────────────────────

        [Fact]
        public void FileName_WildcardPattern_MatchesWholeNameOnly()
        {
            WriteFile("program.cs");
            WriteFile("readme.md");
            WriteFile("program.cs.bak");

            var results = Run("*.cs", recursive: true);

            Assert.Equal(new[] { "program.cs" }, SortedNames(results));
        }

        [Fact]
        public void FileName_QuestionMarkWildcard_MatchesSingleCharacter()
        {
            WriteFile("report1.txt");
            WriteFile("report12.txt");

            var results = Run("report?.txt", recursive: true);

            Assert.Equal(new[] { "report1.txt" }, SortedNames(results));
        }

        [Fact]
        public void FileName_WithoutWildcard_KeepsSubstringMatching()
        {
            WriteFile("monthly-report.txt");
            WriteFile("other.txt");

            var results = Run("report", recursive: true);

            Assert.Equal(new[] { "monthly-report.txt" }, SortedNames(results));
        }

        [Fact]
        public void FileName_WildcardPattern_IgnoresCaseByDefault()
        {
            WriteFile("Program.CS");

            var results = Run("*.cs", recursive: true);

            Assert.Equal(new[] { "Program.CS" }, SortedNames(results));
        }

        [Fact]
        public void FileName_WildcardPattern_HonoursCaseSensitiveOption()
        {
            WriteFile("Program.CS");

            var results = Run("*.cs", recursive: true, caseSensitive: true);

            Assert.Empty(results);
        }

        [Fact]
        public void FileName_RegexOption_TakesPrecedenceOverWildcard()
        {
            WriteFile("a1.log");
            WriteFile("ab.log");

            var results = Run(@"^a\d\.log$", recursive: true, useRegex: true);

            Assert.Equal(new[] { "a1.log" }, SortedNames(results));
        }

        // ── 내용 검색 ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void Content_MatchesLinesWithNumbers()
        {
            WriteFile("notes.txt", "first line\nneedle here\nlast line\n");

            var results = Run("needle", recursive: true, target: SearchTarget.Content);

            var result = Assert.Single(results);
            Assert.Equal(2, result.LineNumber);
            Assert.Equal("needle here", result.LineText);
        }

        // 내용 검색에서는 '*'를 와일드카드로 해석하지 않고 그대로 찾는다(기존 동작 유지).
        [Fact]
        public void Content_TreatsAsteriskAsLiteral()
        {
            WriteFile("notes.txt", "a * b\nplain\n");

            var results = Run("a * b", recursive: true, target: SearchTarget.Content);

            Assert.Single(results);
        }

        [Fact]
        public void Content_SkipsBinaryFiles()
        {
            File.WriteAllBytes(Path.Combine(_root, "blob.bin"), new byte[] { 0x6E, 0x00, 0x65, 0x65, 0x64 });
            WriteFile("notes.txt", "need\n");

            var results = Run("need", recursive: true, target: SearchTarget.Content);

            Assert.Equal(new[] { "notes.txt" }, SortedNames(results));
        }

        // ── 도우미 ────────────────────────────────────────────────────────────────────────

        private void WriteFile(string relativePath, string content = "")
        {
            var full = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, content);
        }

        private async Task<List<SearchResult>> RunAsync(
            string query,
            bool recursive = false,
            bool caseSensitive = false,
            bool useRegex = false,
            SearchTarget target = SearchTarget.FileName)
        {
            var collector = new ListProgress();
            await SearchService.SearchAsync(_root, query, recursive, caseSensitive, useRegex, target,
                collector, CancellationToken.None);
            return collector.Items;
        }

        private List<SearchResult> Run(
            string query,
            bool recursive = false,
            bool caseSensitive = false,
            bool useRegex = false,
            SearchTarget target = SearchTarget.FileName)
        {
            return RunAsync(query, recursive, caseSensitive, useRegex, target).GetAwaiter().GetResult();
        }

        private static string[] SortedNames(IEnumerable<SearchResult> results)
        {
            return results.Select(r => r.FileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        }

        /// <summary>
        /// 폴더를 실제로 열거 불가 상태로 만들 수 있으면 true. root처럼 권한 검사를 우회하는
        /// 계정이나 chmod가 없는 플랫폼에서는 false를 반환해 테스트를 Skip시킨다.
        /// </summary>
        private static bool TryDenyDirectoryListing(string path)
        {
            Chmod(path, "000");
            try
            {
                Directory.GetFiles(path);
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
            catch (IOException)
            {
                return true;
            }
        }

        private static bool TryCreateDirectorySymlink(string linkPath, string targetPath)
        {
            try
            {
                Directory.CreateSymbolicLink(linkPath, targetPath);
                return Directory.Exists(linkPath);
            }
            catch
            {
                return false;
            }
        }

        private static void Chmod(string path, string mode)
        {
            if (OperatingSystem.IsWindows()) return;
            using (var process = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("chmod", mode + " " + path) { UseShellExecute = false }))
            {
                process.WaitForExit();
            }
        }

        private sealed class ListProgress : IProgress<SearchResult>
        {
            public List<SearchResult> Items { get; } = new List<SearchResult>();
            public void Report(SearchResult value)
            {
                lock (Items) Items.Add(value);
            }
        }
    }
}

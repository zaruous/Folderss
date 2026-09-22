using Folderss.Services;
using System;
using System.IO;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>
    /// gitignore 부분집합 매처 검증. 규칙 텍스트에서 직접 만들 때는 실제 파일이 필요 없고, 경로만 절대 경로면 된다.
    /// 판정은 항목 자신만 보므로(상위 폴더 무시 여부는 보지 않음) 그 결정도 테스트로 고정한다.
    /// </summary>
    public sealed class IgnoreRuleSetTests : IDisposable
    {
        private readonly string _root;

        public IgnoreRuleSetTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "folderss-ignore-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private string P(params string[] parts)
        {
            return Path.Combine(_root, Path.Combine(parts));
        }

        // ── 패턴 문법 ─────────────────────────────────────────────────────────────

        [Fact]
        public void BasenamePattern_MatchesAtAnyDepth_AndDirectoryOnlySuffixSkipsFiles()
        {
            var rules = IgnoreRuleSet.FromText(_root, "node_modules/\n*.log\n");

            Assert.True(rules.IsIgnored(P("node_modules"), true));
            Assert.True(rules.IsIgnored(P("src", "pkg", "node_modules"), true));
            Assert.False(rules.IsIgnored(P("node_modules"), false));          // 파일이면 디렉터리 전용 규칙 미적용
            Assert.True(rules.IsIgnored(P("logs", "app.log"), false));
            Assert.False(rules.IsIgnored(P("logs", "app.log.txt"), false));
        }

        [Fact]
        public void AnchoredPattern_OnlyMatchesRelativeToRuleFile()
        {
            var rules = IgnoreRuleSet.FromText(_root, "/build\nsrc/generated\n");

            Assert.True(rules.IsIgnored(P("build"), true));
            Assert.False(rules.IsIgnored(P("src", "build"), true));           // 앵커 → 루트 바로 아래만
            Assert.True(rules.IsIgnored(P("src", "generated"), true));
            Assert.False(rules.IsIgnored(P("other", "src", "generated"), true)); // 중간 슬래시도 앵커
        }

        [Fact]
        public void Negation_LaterRuleWins()
        {
            var rules = IgnoreRuleSet.FromText(_root, "*.log\n!keep.log\n");

            Assert.True(rules.IsIgnored(P("a.log"), false));
            Assert.False(rules.IsIgnored(P("keep.log"), false));
            Assert.False(rules.IsIgnored(P("deep", "keep.log"), false));
        }

        [Fact]
        public void DoubleStar_CrossesDirectories()
        {
            var rules = IgnoreRuleSet.FromText(_root, "docs/**/*.md\n**/temp\nout/**\n");

            Assert.True(rules.IsIgnored(P("docs", "readme.md"), false));
            Assert.True(rules.IsIgnored(P("docs", "a", "b", "c.md"), false));
            Assert.False(rules.IsIgnored(P("src", "readme.md"), false));
            Assert.True(rules.IsIgnored(P("x", "y", "temp"), true));
            Assert.True(rules.IsIgnored(P("out", "bin", "app.exe"), false));
            Assert.False(rules.IsIgnored(P("out"), true));                     // "out/**"은 안의 것만
        }

        [Fact]
        public void Wildcards_DoNotCrossSlash_AndCharacterClassesWork()
        {
            var rules = IgnoreRuleSet.FromText(_root, "src/*.tmp\nfile?.txt\n[Bb]in/\n");

            Assert.True(rules.IsIgnored(P("src", "a.tmp"), false));
            Assert.False(rules.IsIgnored(P("src", "sub", "a.tmp"), false));   // *는 /를 넘지 않음
            Assert.True(rules.IsIgnored(P("file1.txt"), false));
            Assert.False(rules.IsIgnored(P("file12.txt"), false));
            Assert.True(rules.IsIgnored(P("Bin"), true));
        }

        [Fact]
        public void Matching_IsCaseInsensitive_LikeGitOnWindows()
        {
            var rules = IgnoreRuleSet.FromText(_root, "BIN/\n*.LOG\n");

            Assert.True(rules.IsIgnored(P("bin"), true));
            Assert.True(rules.IsIgnored(P("trace.log"), false));
        }

        [Fact]
        public void CommentsBlankLinesAndEscapes_AreHandled()
        {
            var rules = IgnoreRuleSet.FromText(_root, "# comment\n\n   \n\\#literal\n\\!bang\ntrailing   \n");

            Assert.Equal(3, rules.RuleCount);                                  // \#literal, \!bang, trailing
            Assert.True(rules.IsIgnored(P("#literal"), false));
            Assert.True(rules.IsIgnored(P("!bang"), false));
            Assert.True(rules.IsIgnored(P("trailing"), false));               // 끝 공백은 무시
            Assert.False(rules.IsIgnored(P("# comment"), false));
        }

        [Fact]
        public void PathsOutsideRuleBase_AreNeverIgnored()
        {
            var rules = IgnoreRuleSet.FromText(P("repo"), "*.log\n");

            Assert.True(rules.IsIgnored(P("repo", "a.log"), false));
            Assert.False(rules.IsIgnored(P("elsewhere", "a.log"), false));
        }

        [Fact]
        public void OnlyTheEntryItselfIsJudged_NotItsAncestors()
        {
            // 상위 폴더 bin이 무시 대상이어도 그 안을 보고 있는 목록의 항목은 규칙에 직접 걸리지 않으면 남긴다.
            var rules = IgnoreRuleSet.FromText(_root, "bin/\n");

            Assert.True(rules.IsIgnored(P("bin"), true));
            Assert.False(rules.IsIgnored(P("bin", "app.dll"), false));
        }

        // ── 파일에서 읽기 ─────────────────────────────────────────────────────────

        [Fact]
        public void LoadFor_ReadsGitignoreChainFromRepoRoot_DeeperFileOverrides()
        {
            var repo = P("repo");
            Directory.CreateDirectory(Path.Combine(repo, ".git"));
            Directory.CreateDirectory(Path.Combine(repo, "sub", "bin"));
            Directory.CreateDirectory(Path.Combine(repo, "bin"));
            File.WriteAllText(Path.Combine(repo, ".gitignore"), "bin/\n*.log\n");
            File.WriteAllText(Path.Combine(repo, "sub", ".gitignore"), "!bin/\n");

            var atRoot = IgnoreRuleSet.LoadFor(repo);
            Assert.True(atRoot.IsIgnored(Path.Combine(repo, "bin"), true));
            Assert.True(atRoot.IsIgnored(Path.Combine(repo, ".git"), true));  // 저장소 안에서는 .git도 숨김
            Assert.Single(atRoot.SourceFiles);

            var atSub = IgnoreRuleSet.LoadFor(Path.Combine(repo, "sub"));
            Assert.False(atSub.IsIgnored(Path.Combine(repo, "sub", "bin"), true)); // 하위 .gitignore의 부정이 우선
            Assert.True(atSub.IsIgnored(Path.Combine(repo, "sub", "x.log"), false));
            Assert.Equal(2, atSub.SourceFiles.Count);
        }

        [Fact]
        public void LoadFor_WithoutRepo_IgnoresGitignoreButHonorsFolderssignore()
        {
            var folder = P("plain");
            Directory.CreateDirectory(Path.Combine(folder, "child"));
            File.WriteAllText(Path.Combine(folder, ".gitignore"), "*.log\n");
            File.WriteAllText(Path.Combine(folder, ".folderssignore"), "*.bak\n");

            var rules = IgnoreRuleSet.LoadFor(Path.Combine(folder, "child"));

            Assert.False(rules.IsIgnored(Path.Combine(folder, "child", "a.log"), false)); // .git이 없으면 .gitignore는 무시
            Assert.True(rules.IsIgnored(Path.Combine(folder, "child", "a.bak"), false));  // .folderssignore는 상위 폴더 것도 적용
        }

        [Fact]
        public void LoadFor_MissingDirectory_ReturnsEmptySet()
        {
            var rules = IgnoreRuleSet.LoadFor(P("does-not-exist"));

            Assert.True(rules.IsEmpty);
            Assert.False(rules.IsIgnored(P("does-not-exist", "x"), false));
        }
    }
}

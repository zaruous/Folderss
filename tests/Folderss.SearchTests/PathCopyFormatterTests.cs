using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Folderss.Services;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>
    /// 경로 복사 형식: 전체·따옴표·이름·상위 폴더·저장소/반대편 기준 상대 경로·UNC, 여러 줄, 구분자, 대체 규칙,
    /// 루트·긴 경로·공백·한글. Windows 경로 규칙을 직접 처리하므로 OS와 관계없이 같은 결과여야 한다.
    /// </summary>
    public sealed class PathCopyFormatterTests
    {
        private const string Repo = @"C:\work\Folderss";

        private static PathCopyContext Context(PathCopySeparator separator = PathCopySeparator.Backslash,
            string opposite = null, Func<string, string> unc = null, string repo = Repo)
        {
            return new PathCopyContext
            {
                FindRepositoryRoot = folder => repo == null
                    ? null
                    : PathCopyFormatter.FindRepositoryRoot(folder, f => string.Equals(f, repo, StringComparison.OrdinalIgnoreCase)),
                OppositeFolder = opposite,
                ResolveUnc = unc,
                Separator = separator
            };
        }

        private static string One(string path, PathCopyFormat format, PathCopyContext context = null)
        {
            return PathCopyFormatter.Format(new[] { path }, format, context ?? Context()).Text;
        }

        // ── 기본 형식 ───────────────────────────────────────────────────

        [Theory]
        [InlineData(@"C:\work\my app\a.txt", @"C:\work\my app\a.txt")]
        [InlineData(@"C:\work\my app\", @"C:\work\my app")]       // 끝 구분자는 뗀다
        [InlineData(@"C:\", @"C:\")]                               // 드라이브 루트는 그대로
        [InlineData(@"D:\자료\보고서 최종.docx", @"D:\자료\보고서 최종.docx")]
        [InlineData(@"\\server\share\", @"\\server\share")]
        public void FullPath_KeepsPath_TrimsTrailingSeparator(string path, string expected)
        {
            Assert.Equal(expected, One(path, PathCopyFormat.FullPath));
        }

        [Theory]
        [InlineData(@"C:\work\my app\a.txt", "\"C:\\work\\my app\\a.txt\"")]
        [InlineData(@"C:\work\a.txt", "\"C:\\work\\a.txt\"")]      // 공백이 없어도 감싼다(탐색기 "경로로 복사"와 같음)
        [InlineData(@"C:\", "\"C:\\\"")]
        public void QuotedFullPath_AlwaysQuotes(string path, string expected)
        {
            Assert.Equal(expected, One(path, PathCopyFormat.QuotedFullPath));
        }

        [Theory]
        [InlineData(@"C:\work\my app\a.txt", "a.txt")]
        [InlineData(@"C:\work\my app", "my app")]
        [InlineData(@"C:\work\my app\", "my app")]
        [InlineData(@"D:\자료\보고서 최종.docx", "보고서 최종.docx")]
        [InlineData(@"C:\", @"C:\")]                               // 루트는 이름이 없어 경로 그대로
        [InlineData(@"\\server\share", @"\\server\share")]
        [InlineData(@"\\server\share\docs\a.txt", "a.txt")]
        public void Name_LastSegment_OrRootItself(string path, string expected)
        {
            var result = PathCopyFormatter.Format(new[] { path }, PathCopyFormat.Name, Context());
            Assert.Equal(expected, result.Text);
            Assert.Equal(0, result.FallbackCount);
        }

        [Theory]
        [InlineData(@"C:\work\my app\a.txt", @"C:\work\my app")]
        [InlineData(@"C:\work", @"C:\")]
        [InlineData(@"\\server\share\a.txt", @"\\server\share")]
        [InlineData(@"\\server\share\docs\a.txt", @"\\server\share\docs")]
        public void ParentFolder_DirectoryPart(string path, string expected)
        {
            Assert.Equal(expected, One(path, PathCopyFormat.ParentFolder));
        }

        [Fact]
        public void ParentFolder_OfRoot_FallsBackToPath()
        {
            var result = PathCopyFormatter.Format(new[] { @"C:\" }, PathCopyFormat.ParentFolder, Context());
            Assert.Equal(@"C:\", result.Text);
            Assert.Equal(1, result.FallbackCount);
        }

        // ── 여러 개 ─────────────────────────────────────────────────────

        [Fact]
        public void MultiplePaths_OnePerLine_CrLf_KeepsOrder_NoTrailingNewline()
        {
            var paths = new[] { @"C:\w\b.txt", @"C:\w\a.txt", @"C:\w\폴더" };
            var result = PathCopyFormatter.Format(paths, PathCopyFormat.FullPath, Context());
            Assert.Equal("C:\\w\\b.txt\r\nC:\\w\\a.txt\r\nC:\\w\\폴더", result.Text);
            Assert.Equal(3, result.Count);

            var quoted = PathCopyFormatter.Format(paths, PathCopyFormat.QuotedFullPath, Context());
            Assert.Equal("\"C:\\w\\b.txt\"\r\n\"C:\\w\\a.txt\"\r\n\"C:\\w\\폴더\"", quoted.Text);
        }

        [Fact]
        public void MultiplePaths_DuplicatesAndBlanks_AppearOnce()
        {
            var paths = new[] { @"C:\w\a.txt", "", @"c:\W\A.TXT", null, @"C:\w\a.txt\" };
            var result = PathCopyFormatter.Format(paths, PathCopyFormat.FullPath, Context());
            Assert.Equal(@"C:\w\a.txt", result.Text);
            Assert.Equal(1, result.Count);
        }

        [Fact]
        public void ParentFolder_SameFolderForAll_OneLine()
        {
            var result = PathCopyFormatter.Format(new[] { @"C:\w\a.txt", @"C:\w\b.txt" }, PathCopyFormat.ParentFolder, Context());
            Assert.Equal(@"C:\w", result.Text);
            Assert.Equal(1, result.Count);
        }

        [Fact]
        public void Empty_NoLines()
        {
            var result = PathCopyFormatter.Format(new string[0], PathCopyFormat.FullPath, Context());
            Assert.Equal(string.Empty, result.Text);
            Assert.Equal(0, result.Count);
            Assert.Equal("복사할 경로가 없습니다.", PathCopyFormatter.StatusMessage(result));
        }

        // ── 상대 경로: 저장소 기준 ──────────────────────────────────────

        [Theory]
        [InlineData(@"C:\work\Folderss\docs\items\a.md", @"docs\items\a.md")]
        [InlineData(@"C:\work\Folderss\README.md", "README.md")]
        [InlineData(@"C:\WORK\folderss\Docs", "Docs")]               // 대소문자 무시
        public void RelativeToRepository_FromRepositoryRoot(string path, string expected)
        {
            Assert.Equal(expected, One(path, PathCopyFormat.RelativeToRepository));
        }

        [Fact]
        public void RelativeToRepository_Slash_WhenSeparatorSlashForRelative()
        {
            Assert.Equal("docs/items/a.md",
                One(@"C:\work\Folderss\docs\items\a.md", PathCopyFormat.RelativeToRepository, Context(PathCopySeparator.SlashForRelative)));
            // 상대 경로만 / — 전체 경로는 \ 그대로
            Assert.Equal(@"C:\work\Folderss\a.md",
                One(@"C:\work\Folderss\a.md", PathCopyFormat.FullPath, Context(PathCopySeparator.SlashForRelative)));
        }

        [Fact]
        public void RelativeToRepository_OutsideRepository_FallsBackToFullPath()
        {
            var result = PathCopyFormatter.Format(new[] { @"D:\other\a.txt", @"C:\work\Folderss\b.txt" },
                PathCopyFormat.RelativeToRepository, Context());
            Assert.Equal("D:\\other\\a.txt\r\nb.txt", result.Text);
            Assert.Equal(1, result.FallbackCount);
            Assert.Equal("상대 경로 (저장소 기준) 2개 복사함 · 1개는 전체 경로로 대신", PathCopyFormatter.StatusMessage(result));
        }

        [Fact]
        public void RelativeToRepository_RepositoryRootItself_UsesContainingRepository()
        {
            // 저장소 루트 폴더 자신은 "그 저장소 안"이 아니다(담고 있는 폴더부터 찾는다) — 밖이면 전체 경로.
            var result = PathCopyFormatter.Format(new[] { Repo }, PathCopyFormat.RelativeToRepository, Context());
            Assert.Equal(Repo, result.Text);
            Assert.Equal(1, result.FallbackCount);
            Assert.False(PathCopyFormatter.IsAvailable(PathCopyFormat.RelativeToRepository, new[] { Repo }, Context()));
        }

        [Fact]
        public void FindRepositoryRoot_WalksUpIncludingSelf()
        {
            var markers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\work\outer", @"C:\work\outer\inner" };
            Assert.Equal(@"C:\work\outer\inner", PathCopyFormatter.FindRepositoryRoot(@"C:\work\outer\inner\src\x", markers.Contains));
            Assert.Equal(@"C:\work\outer\inner", PathCopyFormatter.FindRepositoryRoot(@"C:\work\outer\inner", markers.Contains));
            Assert.Equal(@"C:\work\outer", PathCopyFormatter.FindRepositoryRoot(@"C:\work\outer\docs", markers.Contains));
            Assert.Null(PathCopyFormatter.FindRepositoryRoot(@"C:\elsewhere", markers.Contains));
            Assert.Null(PathCopyFormatter.FindRepositoryRoot(null, markers.Contains));
        }

        [Fact]
        public void FindRepositoryRoot_RealGitFolderAndFile()
        {
            var root = Path.Combine(Path.GetTempPath(), "folderss-pathcopy-" + Guid.NewGuid().ToString("N"));
            try
            {
                var repo = Path.Combine(root, "repo");
                var worktree = Path.Combine(root, "worktree");
                Directory.CreateDirectory(Path.Combine(repo, ".git"));
                Directory.CreateDirectory(Path.Combine(repo, "src", "deep"));
                Directory.CreateDirectory(worktree);
                File.WriteAllText(Path.Combine(worktree, ".git"), "gitdir: ../repo/.git/worktrees/x");

                Func<string, bool> hasGit = folder => Directory.Exists(Path.Combine(folder, ".git")) || File.Exists(Path.Combine(folder, ".git"));
                Assert.Equal(repo, PathCopyFormatter.FindRepositoryRoot(Path.Combine(repo, "src", "deep"), hasGit), ignoreCase: true);
                Assert.Equal(worktree, PathCopyFormatter.FindRepositoryRoot(worktree, hasGit), ignoreCase: true);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        // ── 상대 경로: 반대편 패널 기준 ─────────────────────────────────

        [Theory]
        [InlineData(@"C:\work\Folderss\docs", @"C:\work\Folderss\src\a.cs", @"..\src\a.cs")]
        [InlineData(@"C:\work\Folderss", @"C:\work\Folderss\src\a.cs", @"src\a.cs")]
        [InlineData(@"C:\work\Folderss\docs\items", @"C:\work\a.txt", @"..\..\..\a.txt")]
        [InlineData(@"C:\work\Folderss\", @"C:\work\Folderss", ".")]
        [InlineData(@"C:\", @"C:\work\a.txt", @"work\a.txt")]
        [InlineData(@"\\srv\share\docs", @"\\SRV\Share\img\a.png", @"..\img\a.png")]
        public void RelativeToOppositePanel_UsesDotDot(string opposite, string path, string expected)
        {
            Assert.Equal(expected, One(path, PathCopyFormat.RelativeToOppositePanel, Context(opposite: opposite)));
        }

        [Fact]
        public void RelativeToOppositePanel_SlashForMarkdownLinks()
        {
            Assert.Equal("../images/그림 1.png",
                One(@"C:\notes\images\그림 1.png", PathCopyFormat.RelativeToOppositePanel,
                    Context(PathCopySeparator.SlashForRelative, opposite: @"C:\notes\docs")));
        }

        [Theory]
        [InlineData(@"D:\docs")]           // 다른 드라이브
        [InlineData(@"\\srv\other")]       // 다른 공유
        [InlineData(null)]                 // 반대편 패널 없음
        [InlineData("relative\\folder")]   // 상대 경로 기준은 쓰지 않음
        public void RelativeToOppositePanel_Unavailable_FallsBack(string opposite)
        {
            var context = Context(opposite: opposite);
            var result = PathCopyFormatter.Format(new[] { @"C:\work\a.txt" }, PathCopyFormat.RelativeToOppositePanel, context);
            Assert.Equal(@"C:\work\a.txt", result.Text);
            Assert.Equal(1, result.FallbackCount);
            Assert.False(PathCopyFormatter.IsAvailable(PathCopyFormat.RelativeToOppositePanel, new[] { @"C:\work\a.txt" }, context));
        }

        // ── UNC ─────────────────────────────────────────────────────────

        private static string FakeUnc(string path)
        {
            // Z: = \\fileserver\work (매핑된 네트워크 드라이브), 그 밖은 로컬이라 변환 실패
            return path.StartsWith(@"Z:", StringComparison.OrdinalIgnoreCase)
                ? PathCopyFormatter.CombineUnc(@"\\fileserver\work\", path)
                : null;
        }

        [Theory]
        [InlineData(@"Z:\팀 자료\a.txt", @"\\fileserver\work\팀 자료\a.txt")]
        [InlineData(@"Z:\", @"\\fileserver\work")]
        [InlineData(@"z:\docs\", @"\\fileserver\work\docs")]
        public void UncPath_MappedDrive_ToServerShare(string path, string expected)
        {
            var context = Context(unc: FakeUnc);
            Assert.Equal(expected, One(path, PathCopyFormat.UncPath, context));
            Assert.True(PathCopyFormatter.IsAvailable(PathCopyFormat.UncPath, new[] { path }, context));
        }

        [Fact]
        public void UncPath_ConversionFails_FallsBackToOriginalPath()
        {
            var context = Context(unc: FakeUnc);
            var result = PathCopyFormatter.Format(new[] { @"Z:\a.txt", @"C:\local\b.txt" }, PathCopyFormat.UncPath, context);
            Assert.Equal("\\\\fileserver\\work\\a.txt\r\nC:\\local\\b.txt", result.Text);
            Assert.Equal(1, result.FallbackCount);
            Assert.Equal("UNC 경로 2개 복사함 · 1개는 전체 경로로 대신", PathCopyFormatter.StatusMessage(result));

            // 로컬 드라이브만 고르면 메뉴에 UNC 항목이 보이지 않는다
            Assert.False(PathCopyFormatter.IsAvailable(PathCopyFormat.UncPath, new[] { @"C:\local\b.txt" }, context));
        }

        [Fact]
        public void UncPath_ResolverThrows_TreatedAsFailure()
        {
            var context = Context(unc: _ => throw new InvalidOperationException("network down"));
            var result = PathCopyFormatter.Format(new[] { @"Z:\a.txt" }, PathCopyFormat.UncPath, context);
            Assert.Equal(@"Z:\a.txt", result.Text);
            Assert.Equal(1, result.FallbackCount);
        }

        [Fact]
        public void UncPath_AlreadyUnc_IsItself_ButMenuHidden()
        {
            var context = Context(unc: FakeUnc);
            var result = PathCopyFormatter.Format(new[] { @"\\srv\share\a.txt" }, PathCopyFormat.UncPath, context);
            Assert.Equal(@"\\srv\share\a.txt", result.Text);
            Assert.Equal(0, result.FallbackCount);
            Assert.False(PathCopyFormatter.IsAvailable(PathCopyFormat.UncPath, new[] { @"\\srv\share\a.txt" }, context));
        }

        [Theory]
        [InlineData(@"\\server\share", @"Z:\a\b.txt", @"\\server\share\a\b.txt")]
        [InlineData(@"\\server\share\", @"Z:\a", @"\\server\share\a")]
        [InlineData(@"\\server\share", @"Z:\", @"\\server\share")]
        [InlineData(@"\\server\share", @"Z:", @"\\server\share")]
        [InlineData(@"\\server\share", @"\\other\x\a", null)]   // 드라이브 문자 경로가 아님
        [InlineData("", @"Z:\a", null)]
        public void CombineUnc_JoinsRemoteNameAndRest(string remote, string path, string expected)
        {
            Assert.Equal(expected, PathCopyFormatter.CombineUnc(remote, path));
        }

        // ── 구분자 ──────────────────────────────────────────────────────

        [Fact]
        public void SlashForAll_AppliesToPathFormats_NotName()
        {
            var context = Context(PathCopySeparator.SlashForAll, opposite: @"C:\work\Folderss\docs", unc: FakeUnc);
            Assert.Equal("C:/work/Folderss/src/a b.cs", One(@"C:\work\Folderss\src\a b.cs", PathCopyFormat.FullPath, context));
            Assert.Equal("\"C:/work/Folderss/src/a b.cs\"", One(@"C:\work\Folderss\src\a b.cs", PathCopyFormat.QuotedFullPath, context));
            Assert.Equal("C:/work/Folderss/src", One(@"C:\work\Folderss\src\a b.cs", PathCopyFormat.ParentFolder, context));
            Assert.Equal("src/a b.cs", One(@"C:\work\Folderss\src\a b.cs", PathCopyFormat.RelativeToRepository, context));
            Assert.Equal("../src/a b.cs", One(@"C:\work\Folderss\src\a b.cs", PathCopyFormat.RelativeToOppositePanel, context));
            Assert.Equal("//fileserver/work/a.txt", One(@"Z:\a.txt", PathCopyFormat.UncPath, context));
            Assert.Equal("a b.cs", One(@"C:\work\Folderss\src\a b.cs", PathCopyFormat.Name, context));
        }

        [Fact]
        public void SlashForRelative_FallbackFullPathKeepsBackslash()
        {
            // 저장소 밖이라 전체 경로로 대신하면 "상대 경로만 /" 설정이어도 \ 그대로
            var result = PathCopyFormatter.Format(new[] { @"D:\x\a.txt" }, PathCopyFormat.RelativeToRepository, Context(PathCopySeparator.SlashForRelative));
            Assert.Equal(@"D:\x\a.txt", result.Text);
        }

        // ── 경로 모양 ───────────────────────────────────────────────────

        [Fact]
        public void LongPath_KeptIntact()
        {
            var segments = Enumerable.Range(0, 30).Select(i => "폴더 이름 " + i.ToString("00"));
            var longPath = @"C:\" + string.Join(@"\", segments) + @"\끝 파일.txt";
            Assert.True(longPath.Length > 260);
            Assert.Equal(longPath, One(longPath, PathCopyFormat.FullPath));
            Assert.Equal("끝 파일.txt", One(longPath, PathCopyFormat.Name));
            Assert.Equal(longPath.Substring(0, longPath.LastIndexOf('\\')), One(longPath, PathCopyFormat.ParentFolder));
        }

        [Theory]
        [InlineData(@"\\?\C:\very\long\a.txt", "a.txt", @"\\?\C:\very\long")]
        [InlineData(@"\\?\UNC\srv\share\a.txt", "a.txt", @"\\?\UNC\srv\share")]
        public void ExtendedLengthPrefix_Understood(string path, string name, string parent)
        {
            Assert.Equal(name, PathCopyFormatter.GetName(path));
            Assert.Equal(parent, PathCopyFormatter.GetParent(path));
        }

        [Theory]
        [InlineData(@"\\srv\share\a", true)]
        [InlineData(@"\\?\UNC\srv\share\a", true)]
        [InlineData(@"\\?\C:\a", false)]   // 긴 경로 접두사의 로컬 경로
        [InlineData(@"C:\a", false)]
        [InlineData(@"\\", false)]
        public void IsUncPath_ExcludesExtendedLocalPaths(string path, bool expected)
        {
            Assert.Equal(expected, PathCopyFormatter.IsUncPath(path));
        }

        [Theory]
        [InlineData(@"C:\", 3)]
        [InlineData(@"C:", 2)]
        [InlineData(@"C:\a", 3)]
        [InlineData(@"\\srv\share", 11)]
        [InlineData(@"\\srv\share\a", 11)]
        [InlineData(@"\\?\C:\a", 7)]
        [InlineData(@"relative\a", 0)]
        public void GetRootLength_WindowsRules(string path, int expected)
        {
            Assert.Equal(expected, PathCopyFormatter.GetRootLength(path));
        }

        // ── 메뉴 가능 여부 ─────────────────────────────────────────────

        [Fact]
        public void IsAvailable_BasicFormatsAlways_ConditionalOnlyWhenTheyApply()
        {
            var inRepo = new[] { @"C:\work\Folderss\a.txt" };
            var context = Context(opposite: @"C:\work\Folderss\docs");
            foreach (var format in new[] { PathCopyFormat.FullPath, PathCopyFormat.QuotedFullPath, PathCopyFormat.Name, PathCopyFormat.ParentFolder })
                Assert.True(PathCopyFormatter.IsAvailable(format, inRepo, context));
            Assert.True(PathCopyFormatter.IsAvailable(PathCopyFormat.RelativeToRepository, inRepo, context));
            Assert.True(PathCopyFormatter.IsAvailable(PathCopyFormat.RelativeToOppositePanel, inRepo, context));
            Assert.False(PathCopyFormatter.IsAvailable(PathCopyFormat.UncPath, inRepo, context));   // 해석기 없음

            Assert.False(PathCopyFormatter.IsAvailable(PathCopyFormat.RelativeToRepository, new[] { @"D:\x.txt" }, context));
            Assert.False(PathCopyFormatter.IsAvailable(PathCopyFormat.FullPath, new string[0], context));
            Assert.Equal("Folderss", PathCopyFormatter.RepositoryName(inRepo, context));
            Assert.Null(PathCopyFormatter.RepositoryName(new[] { @"D:\x.txt" }, context));
        }

        [Fact]
        public void MenuOrder_HasEveryFormatOnce_WithDisplayNames()
        {
            var all = Enum.GetValues(typeof(PathCopyFormat)).Cast<PathCopyFormat>().ToList();
            Assert.Equal(all.OrderBy(f => f), PathCopyFormatter.MenuOrder.OrderBy(f => f));
            Assert.All(all, f => Assert.NotEqual(f.ToString(), PathCopyFormatter.DisplayName(f)));
        }

        [Fact]
        public void StatusMessage_CountsLines()
        {
            var result = PathCopyFormatter.Format(new[] { @"C:\a", @"C:\b", @"C:\c" }, PathCopyFormat.FullPath, Context());
            Assert.Equal("전체 경로 3개 복사함", PathCopyFormatter.StatusMessage(result));
        }
    }

    /// <summary>경로 복사 설정 저장·읽기(path-copy-settings.xml).</summary>
    public sealed class PathCopySettingsServiceTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "folderss-pathcopy-settings-" + Guid.NewGuid().ToString("N"));

        public PathCopySettingsServiceTests()
        {
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            Directory.Delete(_dir, true);
        }

        [Fact]
        public void Load_MissingFile_Defaults()
        {
            var settings = PathCopySettingsService.Load(Path.Combine(_dir, "none.xml"));
            Assert.Equal(PathCopyFormat.FullPath, settings.ShortcutFormat);
            Assert.Equal(PathCopySeparator.Backslash, settings.Separator);
        }

        [Fact]
        public void SaveThenLoad_RoundTrips()
        {
            var path = Path.Combine(_dir, "path-copy-settings.xml");
            PathCopySettingsService.Save(new PathCopySettings
            {
                ShortcutFormat = PathCopyFormat.QuotedFullPath,
                Separator = PathCopySeparator.SlashForRelative
            }, path);
            var loaded = PathCopySettingsService.Load(path);
            Assert.Equal(PathCopyFormat.QuotedFullPath, loaded.ShortcutFormat);
            Assert.Equal(PathCopySeparator.SlashForRelative, loaded.Separator);
            Assert.False(File.Exists(path + ".tmp"));
        }

        [Fact]
        public void Load_UnknownOrBrokenValues_FallBackPerItem()
        {
            var unknown = Path.Combine(_dir, "unknown.xml");
            File.WriteAllText(unknown, "<pathCopy shortcutFormat=\"Nope\" separator=\"SlashForAll\" />");
            var settings = PathCopySettingsService.Load(unknown);
            Assert.Equal(PathCopyFormat.FullPath, settings.ShortcutFormat);
            Assert.Equal(PathCopySeparator.SlashForAll, settings.Separator);

            var numeric = Path.Combine(_dir, "numeric.xml");
            File.WriteAllText(numeric, "<pathCopy shortcutFormat=\"99\" separator=\"7\" />");
            settings = PathCopySettingsService.Load(numeric);
            Assert.Equal(PathCopyFormat.FullPath, settings.ShortcutFormat);
            Assert.Equal(PathCopySeparator.Backslash, settings.Separator);

            var broken = Path.Combine(_dir, "broken.xml");
            File.WriteAllText(broken, "<pathCopy");
            Assert.Equal(PathCopyFormat.FullPath, PathCopySettingsService.Load(broken).ShortcutFormat);
        }

        [Fact]
        public void Save_Failure_Throws()
        {
            // 폴더 자리에 같은 이름 파일이 있어 쓸 수 없는 경로 — 빈 catch로 삼키지 않고 던져야 설정 창이 알린다.
            var blocker = Path.Combine(_dir, "blocker");
            File.WriteAllText(blocker, "x");
            Assert.ThrowsAny<Exception>(() => PathCopySettingsService.Save(new PathCopySettings(), Path.Combine(blocker, "path-copy-settings.xml")));
        }
    }
}

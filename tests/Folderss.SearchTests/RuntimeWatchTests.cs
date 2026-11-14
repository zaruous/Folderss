using System.Collections.Generic;
using Folderss.Services;
using Xunit;

namespace Folderss.SearchTests
{
    public class RuntimeWatchTests
    {
        private const string Core = @"C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.33\System.Private.CoreLib.dll";
        private const string Desktop = @"C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\8.0.33\PresentationFramework.dll";

        [Fact]
        public void WatchedFiles_FrameworkDependent_WatchesSharedRuntime()
        {
            var watched = RuntimeWatch.WatchedFiles(new[] { Core, Desktop, Core, null }, @"D:\app\bin");

            Assert.Equal(new[] { Core, Desktop }, watched);
        }

        [Fact]
        public void WatchedFiles_SelfContained_WatchesNothing()
        {
            var watched = RuntimeWatch.WatchedFiles(new[] { @"D:\app\bin\System.Private.CoreLib.dll", @"D:\app\bin\PresentationFramework.dll" }, @"D:\app\bin\");

            Assert.Empty(watched);
        }

        [Fact]
        public void WatchedFiles_SiblingFolderWithSamePrefix_IsNotAppFolder()
        {
            var watched = RuntimeWatch.WatchedFiles(new[] { @"D:\app\bin2\x.dll" }, @"D:\app\bin");

            Assert.Single(watched);
        }

        [Fact]
        public void MissingFiles_ReportsOnlyRemoved()
        {
            var existing = new HashSet<string> { Desktop };

            Assert.Equal(new[] { Core }, RuntimeWatch.MissingFiles(new[] { Core, Desktop }, existing.Contains));
            Assert.Empty(RuntimeWatch.MissingFiles(new[] { Desktop }, existing.Contains));
        }

        [Fact]
        public void VersionOf_ReadsRuntimeFolderName()
        {
            Assert.Equal("8.0.33", RuntimeWatch.VersionOf(Core));
            Assert.Null(RuntimeWatch.VersionOf(@"D:\app\bin\x.dll"));
            Assert.Null(RuntimeWatch.VersionOf(null));
        }
    }
}

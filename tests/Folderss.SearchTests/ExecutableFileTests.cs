using Folderss.Services;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>폴더 비교 창 더블클릭 전 확인 대상(더블클릭하면 Windows가 실행하는 형식) 판정.</summary>
    public sealed class ExecutableFileTests
    {
        [Theory]
        [InlineData(@"D:\deploy\setup.exe")]
        [InlineData(@"D:\deploy\run.BAT")]
        [InlineData(@"D:\deploy\run.cmd")]
        [InlineData(@"D:\deploy\install.msi")]
        [InlineData(@"D:\deploy\script.ps1")]
        [InlineData(@"D:\deploy\script.vbs")]
        [InlineData(@"D:\deploy\script.js")]
        [InlineData(@"D:\deploy\settings.reg")]
        [InlineData(@"D:\deploy\바로가기.lnk")]
        public void ExecutableTypes_NeedConfirmation(string path)
        {
            Assert.True(FileOperationService.IsExecutable(path));
        }

        [Theory]
        [InlineData(@"D:\proj\README.md")]
        [InlineData(@"D:\proj\App.xaml.cs")]
        [InlineData(@"D:\proj\data.json")]
        [InlineData(@"D:\proj\photo.png")]
        [InlineData(@"D:\proj\Makefile")]
        [InlineData(@"D:\proj\archive.exe.txt")]
        public void DocumentsAndSources_OpenWithoutConfirmation(string path)
        {
            Assert.False(FileOperationService.IsExecutable(path));
        }
    }
}

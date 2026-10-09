using System;
using System.IO;
using Folderss.Services;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>캡쳐 설정: 기본 저장 폴더는 사진 폴더(없으면 사용자 폴더), 저장·읽기 왕복, 설정 창 입력 검증.</summary>
    public sealed class CaptureSettingsServiceTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "folderss-capture-settings-" + Guid.NewGuid().ToString("N"));

        public CaptureSettingsServiceTests()
        {
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            Directory.Delete(_dir, true);
        }

        [Fact]
        public void Load_MissingFile_DefaultsToPicturesFolder()
        {
            var settings = CaptureSettingsService.Load(Path.Combine(_dir, "none.xml"));
            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            var expected = pictures.Length > 0 ? pictures : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Assert.Equal(expected, settings.SaveFolder);
            Assert.Equal(CaptureSettingsService.DefaultSaveFolder, settings.SaveFolder);
        }

        [Theory]
        [InlineData(@"C:\Users\kim\Pictures", @"C:\Users\kim", @"C:\Users\kim\Pictures")]  // 사진 폴더
        [InlineData(@"D:\OneDrive\사진", @"C:\Users\kim", @"D:\OneDrive\사진")]          // 옮긴 사진 폴더도 그대로
        [InlineData("", @"C:\Users\kim", @"C:\Users\kim")]                                // 사진 폴더를 못 얻으면 사용자 폴더
        public void ResolveDefault_PrefersPicturesThenUserProfile(string pictures, string profile, string expected)
        {
            Assert.Equal(expected, CaptureSettingsService.ResolveDefault(pictures, profile));
        }

        [Fact]
        public void SaveThenLoad_RoundTrips()
        {
            var path = Path.Combine(_dir, "capture-settings.xml");
            CaptureSettingsService.Save(new CaptureSettings { SaveFolder = @"D:\캡쳐 모음" }, path);
            Assert.Equal(@"D:\캡쳐 모음", CaptureSettingsService.Load(path).SaveFolder);
        }

        [Fact]
        public void Load_EmptyOrBrokenFile_FallsBackToDefault()
        {
            var empty = Path.Combine(_dir, "empty.xml");
            File.WriteAllText(empty, "<capture saveFolder=\"\" />");
            Assert.Equal(CaptureSettingsService.DefaultSaveFolder, CaptureSettingsService.Load(empty).SaveFolder);

            var broken = Path.Combine(_dir, "broken.xml");
            File.WriteAllText(broken, "<capture");
            Assert.Equal(CaptureSettingsService.DefaultSaveFolder, CaptureSettingsService.Load(broken).SaveFolder);
        }

        [Fact]
        public void Validate_EmptyMeansDefault()
        {
            Assert.Null(CaptureSettingsService.Validate("  ", _ => false, out var folder));
            Assert.Equal(CaptureSettingsService.DefaultSaveFolder, folder);
        }

        [Fact]
        public void Validate_TrimsQuotesAndAcceptsExistingAbsoluteFolder()
        {
            var existing = Path.GetFullPath(_dir);
            Assert.Null(CaptureSettingsService.Validate(" \"" + existing + "\" ", Directory.Exists, out var folder));
            Assert.Equal(existing, folder);
        }

        [Fact]
        public void Validate_RejectsRelativeOrMissingFolder()
        {
            Assert.NotNull(CaptureSettingsService.Validate("captures", _ => true, out _));
            Assert.NotNull(CaptureSettingsService.Validate(Path.Combine(_dir, "missing"), Directory.Exists, out _));
        }
    }
}

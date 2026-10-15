using Folderss.Models;
using Folderss.Services;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>
    /// 폴더 비교 판정·집계·복사 검증. 왼쪽/오른쪽 폴더를 임시 경로에 만들어 실제 파일 시스템으로 비교한다.
    /// </summary>
    public sealed class DirectoryCompareServiceTests : IDisposable
    {
        private static readonly DateTime BaseTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        private readonly string _root;
        private readonly string _left;
        private readonly string _right;

        public DirectoryCompareServiceTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "folderss-compare-" + Guid.NewGuid().ToString("N"));
            _left = Path.Combine(_root, "left");
            _right = Path.Combine(_root, "right");
            Directory.CreateDirectory(_left);
            Directory.CreateDirectory(_right);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private static void Write(string root, string relative, string text, DateTime? modifiedUtc = null)
        {
            var path = Path.Combine(root, N(relative));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
            File.SetLastWriteTimeUtc(path, modifiedUtc ?? BaseTime);
        }

        private CompareResult Run(DirectoryCompareOptions options = null)
        {
            return DirectoryCompareService.Compare(_left, _right, options ?? new DirectoryCompareOptions(), null, CancellationToken.None);
        }

        private static CompareEntry Find(CompareResult result, string relative)
        {
            return result.Entries.Single(e => string.Equals(e.RelativePath, N(relative), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>테스트는 '\'로 쓰고 OS 구분자로 바꾼다(리눅스 CI).</summary>
        private static string N(string relative)
        {
            return relative.Replace('\\', Path.DirectorySeparatorChar);
        }

        [Fact]
        public void Statuses_SameDifferentOneSidedAndTypeMismatch()
        {
            Write(_left, "same.txt", "abc");
            Write(_right, "same.txt", "abc");
            Write(_left, "size.txt", "abc");
            Write(_right, "size.txt", "abcd");
            Write(_left, "left-only.txt", "x");
            Write(_right, "right-only.txt", "x");
            Write(_left, "kind", "file");
            Directory.CreateDirectory(Path.Combine(_right, "kind"));

            var result = Run();

            Assert.True(result.IsComplete);
            Assert.Equal(CompareStatus.Same, Find(result, "same.txt").Status);
            Assert.Equal(CompareStatus.Different, Find(result, "size.txt").Status);
            Assert.Contains("크기", Find(result, "size.txt").Reason);
            Assert.Equal(CompareStatus.LeftOnly, Find(result, "left-only.txt").Status);
            Assert.Equal(CompareStatus.RightOnly, Find(result, "right-only.txt").Status);
            Assert.Equal(CompareStatus.TypeMismatch, Find(result, "kind").Status);
            Assert.Equal(1, result.SameCount);
            Assert.Equal(1, result.DifferentCount);
            Assert.Equal(1, result.LeftOnlyCount);
            Assert.Equal(1, result.RightOnlyCount);
            Assert.Equal(1, result.TypeMismatchCount);
        }

        [Theory]
        [InlineData(1.9, CompareStatus.Same)]
        [InlineData(2.1, CompareStatus.Different)]
        public void Timestamp_ToleranceBoundary(double seconds, CompareStatus expected)
        {
            Write(_left, "a.txt", "abc", BaseTime);
            Write(_right, "a.txt", "abc", BaseTime.AddSeconds(seconds));

            Assert.Equal(expected, Find(Run(), "a.txt").Status);
        }

        [Fact]
        public void ContentHash_IgnoresTimestampAndCatchesSameSizeEdits()
        {
            Write(_left, "same.txt", "abc", BaseTime);
            Write(_right, "same.txt", "abc", BaseTime.AddHours(5));
            Write(_left, "edited.txt", "abc", BaseTime);
            Write(_right, "edited.txt", "abd", BaseTime);

            var result = Run(new DirectoryCompareOptions { UseContentHash = true });

            Assert.Equal(CompareStatus.Same, Find(result, "same.txt").Status);
            Assert.Equal(CompareStatus.Different, Find(result, "edited.txt").Status);
        }

        [Fact]
        public void TimeOnlyDifference_IsDifferentByDefault_AndSameWhenContentVerified()
        {
            Write(_left, "same.txt", "abc", BaseTime);
            Write(_right, "same.txt", "abc", BaseTime.AddHours(5));

            Assert.Equal(CompareStatus.Different, Find(Run(), "same.txt").Status);

            var verified = Find(Run(new DirectoryCompareOptions { VerifyContentWhenTimeDiffers = true }), "same.txt");
            Assert.Equal(CompareStatus.Same, verified.Status);
            Assert.Contains("수정 시각만 다름", verified.Reason);
        }

        [Fact]
        public void VerifyContent_SameSizeDifferentLastByte_IsDifferent()
        {
            var text = new string('x', 200_000);
            Write(_left, "big.txt", text + "a", BaseTime);
            Write(_right, "big.txt", text + "b", BaseTime.AddHours(5));

            var entry = Find(Run(new DirectoryCompareOptions { VerifyContentWhenTimeDiffers = true }), "big.txt");

            Assert.Equal(CompareStatus.Different, entry.Status);
            Assert.Contains("수정 시각 다름", entry.Reason);
        }

        [SkippableFact]
        public void VerifyContent_ReadsOnlyWhenTimeDiffers_AndUnreadableIsUnknown()
        {
            Skip.IfNot(OperatingSystem.IsWindows(), "파일 공유 잠금은 Windows에서만 강제됨");
            Write(_left, "near.txt", "abc", BaseTime);
            Write(_right, "near.txt", "abc", BaseTime.AddSeconds(1));
            Write(_left, "far.txt", "abc", BaseTime);
            Write(_right, "far.txt", "abc", BaseTime.AddHours(5));

            using (new FileStream(Path.Combine(_right, "near.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
            using (new FileStream(Path.Combine(_right, "far.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var result = Run(new DirectoryCompareOptions { VerifyContentWhenTimeDiffers = true });
                Assert.Equal(CompareStatus.Same, Find(result, "near.txt").Status);      // 허용 오차 안 → 읽지 않음
                Assert.Equal(CompareStatus.Unknown, Find(result, "far.txt").Status);    // 읽어야 하는데 잠김
            }
        }

        [Fact]
        public void Folder_IsDifferentWhenAnyChildDiffers_AndNotCountedItself()
        {
            Write(_left, @"src\a.txt", "abc");
            Write(_right, @"src\a.txt", "abc");
            Write(_left, @"src\deep\b.txt", "abc");
            Write(_right, @"src\deep\b.txt", "xyz!");
            Write(_left, @"same\c.txt", "abc");
            Write(_right, @"same\c.txt", "abc");

            var result = Run();

            Assert.Equal(CompareStatus.Different, Find(result, "src").Status);
            Assert.Equal(CompareStatus.Different, Find(result, @"src\deep").Status);
            Assert.Equal(CompareStatus.Same, Find(result, "same").Status);
            Assert.Equal(1, result.DifferentCount);   // b.txt만. 폴더는 집계에서 뺀다.
            Assert.Equal(2, result.SameCount);
            // 화면 순서: 폴더가 자식보다 앞, 폴더 먼저·이름순
            var order = result.Entries.Select(e => e.RelativePath).ToList();
            string P(string r) => N(r);
            Assert.True(order.IndexOf("src") < order.IndexOf(@"src\a.txt"));
            Assert.True(order.IndexOf(@"src\deep") < order.IndexOf(@"src\a.txt"));
        }

        [Fact]
        public void NamesPairIgnoringCase()
        {
            Write(_left, "Readme.MD", "abc");
            Write(_right, "README.md", "abc");

            var result = Run();

            Assert.Single(result.Entries);
            Assert.Equal(CompareStatus.Same, result.Entries[0].Status);
        }

        [Fact]
        public void OneSidedFolder_IsNotEntered()
        {
            Write(_left, @"only\x.txt", "abc");

            var result = Run();

            Assert.Single(result.Entries);
            Assert.Equal(CompareStatus.LeftOnly, Find(result, "only").Status);
        }

        [Fact]
        public void IgnoreRules_SkipBothSidedMatches_AndNoteOneSidedRule()
        {
            Write(_left, ".folderssignore", "bin/\n*.log\n");
            Write(_right, ".folderssignore", "bin/\n");
            Write(_left, @"bin\a.dll", "1");
            Write(_right, @"bin\a.dll", "22");
            Write(_left, "app.log", "x");
            Write(_right, "app.log", "x");

            var result = Run();

            Assert.DoesNotContain(result.Entries, e => e.RelativePath == "bin");
            Assert.Contains("왼쪽에서만", Find(result, "app.log").Reason);
            Assert.Equal(CompareStatus.Different, Find(result, ".folderssignore").Status);

            var all = Run(new DirectoryCompareOptions { ApplyIgnoreRules = false });
            Assert.Equal(CompareStatus.Different, Find(all, "bin").Status);
        }

        [Fact]
        public void NotRecursive_LeavesBothSidedFoldersUnknown()
        {
            Write(_left, @"src\a.txt", "abc");
            Write(_right, @"src\a.txt", "xyz!");

            var result = Run(new DirectoryCompareOptions { Recursive = false });

            Assert.Single(result.Entries);
            Assert.Equal(CompareStatus.Unknown, Find(result, "src").Status);
            Assert.Equal(0, result.UnknownCount);
        }

        [Fact]
        public void Cancelled_ReturnsIncompleteWithoutThrowing()
        {
            Write(_left, "a.txt", "abc");
            Write(_right, "a.txt", "abc");
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                var result = DirectoryCompareService.Compare(_left, _right, new DirectoryCompareOptions(), null, cts.Token);
                Assert.False(result.IsComplete);
            }
        }

        [Fact]
        public void CopyEntry_OverwritesFileAndCreatesParents_FolderOnlyWhenOneSided()
        {
            Write(_left, @"src\a.txt", "new content");
            Write(_right, @"src\a.txt", "old");
            Write(_left, @"lib\deep\b.txt", "b");
            var result = Run();

            DirectoryCompareService.CopyEntry(result, Find(result, @"src\a.txt"), toRight: true);
            DirectoryCompareService.CopyEntry(result, Find(result, "lib"), toRight: true);

            Assert.Equal("new content", File.ReadAllText(Path.Combine(_right, "src", "a.txt")));
            Assert.Equal("b", File.ReadAllText(Path.Combine(_right, "lib", "deep", "b.txt")));
            Assert.False(DirectoryCompareService.CanCopy(Find(result, "src"), true, out _));
            Assert.False(DirectoryCompareService.CanCopy(Find(result, "lib"), false, out _));
            Assert.True(Run().Entries.Where(e => !e.IsAggregateFolder).All(e => e.Status == CompareStatus.Same));
        }

        [Fact]
        public void Settings_RoundTripAndDefaults()
        {
            var path = Path.Combine(_root, "compare-settings.xml");
            Assert.True(CompareSettingsService.Load(path).ApplyIgnoreRules);   // 파일 없음 → 기본값
            Assert.False(CompareSettingsService.Load(path).VerifyContentWhenTimeDiffers);   // 기본은 가벼운 비교

            CompareSettingsService.Save(new DirectoryCompareOptions
            {
                UseContentHash = true,
                VerifyContentWhenTimeDiffers = true,
                TimestampTolerance = TimeSpan.FromSeconds(5),
                ApplyIgnoreRules = false,
                Recursive = false,
                MaxDepth = 3,
                IncludeHidden = true
            }, path);
            var loaded = CompareSettingsService.Load(path);

            Assert.True(loaded.UseContentHash);
            Assert.True(loaded.VerifyContentWhenTimeDiffers);
            Assert.Equal(TimeSpan.FromSeconds(5), loaded.TimestampTolerance);
            Assert.False(loaded.ApplyIgnoreRules);
            Assert.False(loaded.Recursive);
            Assert.Equal(3, loaded.MaxDepth);
            Assert.True(loaded.IncludeHidden);
        }

        [Fact]
        public void Settings_DisplayAndMoveOptions_DefaultsAreSafest_AndRoundTrip()
        {
            var path = Path.Combine(_root, "compare-settings.xml");
            var defaults = CompareSettingsService.Load(path);
            Assert.False(defaults.DirectionColors);   // 수정 시각 방향은 오판 위험이 있어 기본 끔
            Assert.False(defaults.ShowFolderTree);
            Assert.False(defaults.ShowPreview);
            Assert.False(defaults.HideSame);
            Assert.Equal(100, defaults.MoveConfirmThreshold);

            CompareSettingsService.Save(new DirectoryCompareOptions
            {
                DirectionColors = true,
                ShowFolderTree = true,
                ShowPreview = true,
                HideSame = true,
                MoveConfirmThreshold = 0
            }, path);
            var loaded = CompareSettingsService.Load(path);

            Assert.True(loaded.DirectionColors);
            Assert.True(loaded.ShowFolderTree);
            Assert.True(loaded.ShowPreview);
            Assert.True(loaded.HideSame);
            Assert.Equal(0, loaded.MoveConfirmThreshold);
        }
    }
}

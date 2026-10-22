using Folderss.Services;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>휴지통 정보 파일($I) 해석과, Windows에서는 실제 휴지통으로 보냈다 되살리기까지 확인한다.</summary>
    public sealed class WindowsRecycleBinTests
    {
        private static readonly DateTime Deleted = new DateTime(2026, 10, 15, 1, 2, 3, DateTimeKind.Utc);

        private static byte[] Header(long version)
        {
            var bytes = new byte[24];
            BitConverter.GetBytes(version).CopyTo(bytes, 0);
            BitConverter.GetBytes(1234L).CopyTo(bytes, 8);
            BitConverter.GetBytes(Deleted.ToFileTimeUtc()).CopyTo(bytes, 16);
            return bytes;
        }

        [Fact]
        public void ParsesVersion2_LengthPrefixedPath()
        {
            const string path = @"D:\백업\proj\App.cs";
            var pathBytes = Encoding.Unicode.GetBytes(path + "\0");
            var data = new byte[28 + pathBytes.Length];
            Header(2).CopyTo(data, 0);
            BitConverter.GetBytes(path.Length + 1).CopyTo(data, 24);
            pathBytes.CopyTo(data, 28);

            var info = WindowsRecycleBin.ParseInfoFile(data);

            Assert.Equal(path, info.OriginalPath);
            Assert.Equal(Deleted, info.DeletedUtc);
        }

        [Fact]
        public void ParsesVersion1_FixedLengthPath()
        {
            const string path = @"C:\work\a.txt";
            var data = new byte[24 + 520];
            Header(1).CopyTo(data, 0);
            Encoding.Unicode.GetBytes(path).CopyTo(data, 24);

            var info = WindowsRecycleBin.ParseInfoFile(data);

            Assert.Equal(path, info.OriginalPath);
            Assert.Equal(Deleted, info.DeletedUtc);
        }

        [Fact]
        public void RejectsBrokenFiles()
        {
            Assert.Throws<FormatException>(() => WindowsRecycleBin.ParseInfoFile(new byte[10]));
            Assert.Throws<FormatException>(() => WindowsRecycleBin.ParseInfoFile(Header(3)));
            var badLength = new byte[28];
            Header(2).CopyTo(badLength, 0);
            BitConverter.GetBytes(1000).CopyTo(badLength, 24);
            Assert.Throws<FormatException>(() => WindowsRecycleBin.ParseInfoFile(badLength));
        }

        [Fact]
        public void RejectsNonInfoFile_WithOutOfRangeFileTime()
        {
            // 실제 휴지통에서 발견된 "$I70WDTZ.tmp"처럼 이름만 $I로 시작하고 형식이 다른 파일.
            // 버전보다 삭제 시각을 먼저 읽으면 DateTime이 ArgumentOutOfRangeException을 던져, 휴지통 검색 전체가 실패했다.
            var data = new byte[4148];
            BitConverter.GetBytes(4851293027410584380L).CopyTo(data, 0);
            BitConverter.GetBytes(5928239180699157836L).CopyTo(data, 16);
            Assert.Throws<FormatException>(() => WindowsRecycleBin.ParseInfoFile(data));

            // 버전은 맞는데 시각만 범위를 벗어난 경우도 건너뛸 수 있게 FormatException이어야 한다.
            var badTime = new byte[28];
            Header(2).CopyTo(badTime, 0);
            BitConverter.GetBytes(-1L).CopyTo(badTime, 16);
            Assert.Throws<FormatException>(() => WindowsRecycleBin.ParseInfoFile(badTime));
        }

        [SkippableFact]
        public void Windows_SendThenRestore_PutsFileBack()
        {
            Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows 휴지통 전용");
            var folder = Path.Combine(Path.GetTempPath(), "folderss-bin-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var file = Path.Combine(folder, "restore me.txt");
                File.WriteAllText(file, "A");
                var bin = new WindowsRecycleBin();

                var sent = bin.Send(file);

                Assert.False(File.Exists(file));
                Skip.IfNot(bin.CanRestore(file, sent), "이 환경의 휴지통에서 항목을 찾지 못함(휴지통 꺼짐 등)");
                bin.Restore(file, sent);
                Assert.Equal("A", File.ReadAllText(file));
                Assert.False(bin.CanRestore(file, sent));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [SkippableFact]
        public void Windows_IndexIsReread_WhenBinChanges()
        {
            // 되돌리기 확인은 항목마다 휴지통을 찾으므로 정보 파일 목록을 한 번 읽어 다시 쓴다.
            // 그 뒤에 보낸 항목도 찾아야 하고, 되살린 항목은 더 이상 찾지 않아야 한다.
            Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows 휴지통 전용");
            var folder = Path.Combine(Path.GetTempPath(), "folderss-bin-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var first = Path.Combine(folder, "first.txt");
                var second = Path.Combine(folder, "second.txt");
                File.WriteAllText(first, "1");
                File.WriteAllText(second, "2");
                var bin = new WindowsRecycleBin();

                var sentFirst = bin.Send(first);
                Skip.IfNot(bin.CanRestore(first, sentFirst), "이 환경의 휴지통에서 항목을 찾지 못함(휴지통 꺼짐 등)");
                var sentSecond = bin.Send(second);

                Assert.True(bin.CanRestore(second, sentSecond));
                bin.Restore(second, sentSecond);
                Assert.False(bin.CanRestore(second, sentSecond));
                bin.Restore(first, sentFirst);
                Assert.Equal("1", File.ReadAllText(first));
                Assert.Equal("2", File.ReadAllText(second));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [SkippableFact]
        public void IsRecyclable_FixedDriveShortPathOnly()
        {
            Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows 전용");
            var temp = Path.GetTempPath();
            Skip.IfNot(new DriveInfo(Path.GetPathRoot(temp)).DriveType == DriveType.Fixed, "임시 폴더가 고정 드라이브가 아님");

            Assert.True(WindowsRecycleBin.IsRecyclable(Path.Combine(temp, "a.txt")));
            Assert.False(WindowsRecycleBin.IsRecyclable(Path.Combine(temp, new string('x', 300))));
            Assert.False(WindowsRecycleBin.IsRecyclable(@"\\server\share\a.txt"));
        }
    }
}

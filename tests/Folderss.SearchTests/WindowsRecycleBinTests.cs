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
    }
}

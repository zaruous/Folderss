using Folderss.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>zip을 폴더처럼 열기: 가상 경로 분해, 목록 트리, 이름 인코딩(CP949·UTF-8), zip Slip 차단, 풀기·만들기, 임시 사본.</summary>
    public sealed class ZipArchiveTests : IDisposable
    {
        private readonly string _root;

        static ZipArchiveTests()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public ZipArchiveTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "folderss-zip-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            ZipTempStore.CleanupSession();
            ZipTempStore.RootOverride = null;
            try
            {
                foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(_root, true);
            }
            catch
            {
            }
        }

        /// <summary>항목 이름(끝이 '/'면 폴더)과 내용으로 zip을 만든다. <paramref name="nameEncoding"/>이 null이면 .NET 기본(비ASCII는 UTF-8 + 플래그).</summary>
        private string MakeZip(string name, Encoding nameEncoding, params (string Entry, string Content)[] entries)
        {
            var path = Path.Combine(_root, name);
            using (var stream = new FileStream(path, FileMode.Create))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, false, nameEncoding))
            {
                foreach (var (entry, content) in entries)
                {
                    var zipEntry = archive.CreateEntry(entry, CompressionLevel.NoCompression);
                    zipEntry.LastWriteTime = new DateTimeOffset(new DateTime(2024, 5, 6, 7, 8, 10));
                    if (entry.EndsWith("/"))
                        continue;
                    using (var writer = new StreamWriter(zipEntry.Open(), new UTF8Encoding(false)))
                        writer.Write(content);
                }
            }
            return path;
        }

        private string MakeZip(string name, params (string Entry, string Content)[] entries)
        {
            return MakeZip(name, null, entries);
        }

        private static IList<string> Names(IEnumerable<ZipEntryInfo> entries)
        {
            return entries.Select(entry => entry.Name).OrderBy(name => name, StringComparer.Ordinal).ToList();
        }

        // ── 가상 경로 ───────────────────────────────────────────────────────

        [Fact]
        public void TrySplit_PathInsideZip_ReturnsArchiveAndInnerPath()
        {
            var zip = MakeZip("a.zip", ("sub/f.txt", "x"));

            Assert.True(ZipArchivePath.TrySplit(Path.Combine(zip, "sub", "f.txt"), out var archive, out var inner));
            Assert.Equal(zip, archive);
            Assert.Equal("sub/f.txt", inner);

            Assert.True(ZipArchivePath.TrySplit(zip + "\\", out archive, out inner));
            Assert.Equal(zip, archive);
            Assert.Equal(string.Empty, inner);
        }

        [Fact]
        public void TrySplit_RealFolderOrNonZipFile_ReturnsFalse()
        {
            var text = Path.Combine(_root, "note.txt");
            File.WriteAllText(text, "hello");

            Assert.False(ZipArchivePath.TrySplit(_root, out _, out _));
            Assert.False(ZipArchivePath.TrySplit(Path.Combine(text, "inner"), out _, out _));
            Assert.False(ZipArchivePath.TrySplit(Path.Combine(_root, "missing", "x"), out _, out _));
        }

        [Fact]
        public void TrySplit_ZipBasedExtensionWithSignature_IsArchive()
        {
            var zip = MakeZip("lib.jar", ("META-INF/MANIFEST.MF", "Manifest-Version: 1.0"));

            Assert.True(ZipArchivePath.TrySplit(Path.Combine(zip, "META-INF"), out var archive, out var inner));
            Assert.Equal(zip, archive);
            Assert.Equal("META-INF", inner);
            Assert.True(ZipArchivePath.CanOpenAsZip(zip));
            Assert.False(ZipArchivePath.IsBrowsableArchive(zip));
        }

        [Fact]
        public void Combine_And_Parent_RoundTrip()
        {
            Assert.Equal(@"C:\a\b.zip", ZipArchivePath.Combine(@"C:\a\b.zip", ""));
            Assert.Equal(@"C:\a\b.zip\sub\f.txt", ZipArchivePath.Combine(@"C:\a\b.zip", "sub/f.txt"));
            Assert.Equal("sub", ZipArchivePath.GetParentInnerPath("sub/f.txt"));
            Assert.Equal(string.Empty, ZipArchivePath.GetParentInnerPath("f.txt"));
            Assert.Equal("a/b", ZipArchivePath.NormalizeInnerPath(@"\a\.\\b\"));
        }

        // ── 목록 ───────────────────────────────────────────────────────────

        [Fact]
        public void ReadListing_BuildsFolderTree_WithImplicitAndEmptyFolders()
        {
            var zip = MakeZip("tree.zip",
                ("a.txt", "A"),
                ("dir/b.txt", "BB"),
                ("dir/sub/", ""),
                ("dir2/deep/c.txt", "CCC"));

            var listing = ZipArchiveService.ReadListing(zip);

            Assert.Equal(new[] { "a.txt", "dir", "dir2" }, Names(listing.GetChildren("")));
            Assert.Equal(new[] { "b.txt", "sub" }, Names(listing.GetChildren("dir")));
            Assert.True(listing.FolderExists("dir/sub"));
            Assert.True(listing.FolderExists("DIR2/deep"));
            Assert.Empty(listing.GetChildren("dir/sub"));
            Assert.Equal(3, listing.FileCount);
            Assert.Equal(4, listing.FolderCount);
            Assert.Equal(6, listing.TotalSize);
            Assert.Equal(4, listing.EntryCount);
            var b = listing.Find("dir/b.txt");
            Assert.False(b.IsDirectory);
            Assert.Equal(2, b.Size);
            Assert.Equal(new DateTime(2024, 5, 6, 7, 8, 10), b.ModifiedAt);
            Assert.True(listing.IsCurrent());
        }

        [Fact]
        public void ReadListing_Cp949NamesWithoutUtf8Flag_DecodesKorean()
        {
            var zip = MakeZip("cp949.zip", Encoding.GetEncoding(949),
                ("한글 폴더/보고서.txt", "x"),
                ("가나다.md", "y"));

            var listing = ZipArchiveService.ReadListing(zip);

            Assert.Equal(new[] { "가나다.md", "한글 폴더" }, Names(listing.GetChildren("")));
            Assert.NotNull(listing.Find("한글 폴더/보고서.txt"));
        }

        [Fact]
        public void ReadListing_Utf8NamesWithFlag_DecodesKorean()
        {
            var zip = MakeZip("utf8.zip", ("자료/설명서.txt", "x"));

            var listing = ZipArchiveService.ReadListing(zip);

            Assert.Equal(new[] { "자료" }, Names(listing.GetChildren("")));
            Assert.Equal(new[] { "설명서.txt" }, Names(listing.GetChildren("자료")));
        }

        [Fact]
        public void ReadListing_Utf8NamesWithoutFlag_DecodesAsUtf8()
        {
            var zip = MakeZip("mac.zip", new Utf8WithoutFlagEncoding(), ("사진/여름.jpg", "x"));

            var listing = ZipArchiveService.ReadListing(zip);

            Assert.Equal(new[] { "사진" }, Names(listing.GetChildren("")));
            Assert.NotNull(listing.Find("사진/여름.jpg"));
        }

        [Fact]
        public void ReadListing_DecomposedHangul_IsNormalizedToNfc()
        {
            var nfd = "한글.txt".Normalize(NormalizationForm.FormD);
            var zip = MakeZip("nfd.zip", (nfd, "x"));

            var listing = ZipArchiveService.ReadListing(zip);

            Assert.Equal("한글.txt", listing.GetChildren("").Single().Name);
        }

        [Fact]
        public void NameEncoding_DecodesByValidity()
        {
            var cp949 = Encoding.GetEncoding(949).GetBytes("보고서");
            var utf8 = Encoding.UTF8.GetBytes("보고서");
            var latin = new byte[] { 0x63, 0x61, 0x66, 0x82 }; // CP437 "café"(é = 0x82), CP949·UTF-8로는 안 읽힘

            Assert.Equal("보고서", ZipEntryNameEncoding.Decode(cp949, 0, cp949.Length));
            Assert.Equal("보고서", ZipEntryNameEncoding.Decode(utf8, 0, utf8.Length));
            Assert.Equal("café", ZipEntryNameEncoding.Decode(latin, 0, latin.Length));
            Assert.Equal("plain.txt", ZipEntryNameEncoding.Decode(Encoding.ASCII.GetBytes("plain.txt"), 0, 9));
        }

        [Fact]
        public void ReadListing_UnsafeNames_AreHiddenAndCounted()
        {
            var zip = MakeZip("slip.zip",
                ("../evil.txt", "x"),
                ("a/../../evil2.txt", "x"),
                ("C:/abs.txt", "x"),
                ("ok/file.txt", "x"),
                ("/rooted.txt", "x"));

            var listing = ZipArchiveService.ReadListing(zip);

            Assert.Equal(3, listing.UnsafeEntryCount);
            Assert.Equal(new[] { "ok", "rooted.txt" }, Names(listing.GetChildren("")));
        }

        [Fact]
        public void ReadListing_CorruptOrNotZip_ThrowsFriendlyMessage()
        {
            var fake = Path.Combine(_root, "fake.zip");
            File.WriteAllText(fake, "this is not a zip file at all");
            var real = MakeZip("cut.zip", ("a.txt", "hello world"));
            var bytes = File.ReadAllBytes(real);
            var truncated = Path.Combine(_root, "truncated.zip");
            File.WriteAllBytes(truncated, bytes.Take(bytes.Length - 10).ToArray());

            var first = Assert.Throws<ZipArchiveException>(() => ZipArchiveService.ReadListing(fake));
            var second = Assert.Throws<ZipArchiveException>(() => ZipArchiveService.ReadListing(truncated));
            var missing = Assert.Throws<ZipArchiveException>(() => ZipArchiveService.ReadListing(Path.Combine(_root, "none.zip")));

            Assert.Contains("손상", first.Message);
            Assert.Contains("손상", second.Message);
            Assert.Contains("찾을 수 없습니다", missing.Message);
        }

        [Fact]
        public void EncryptedEntry_IsListedButCannotBeExtracted()
        {
            var zip = MakeZip("secret.zip", ("plain.txt", "open"), ("secret.txt", "hidden"));
            SetEncryptedFlag(zip, "secret.txt");

            var listing = ZipArchiveService.ReadListing(zip);
            var secret = listing.Find("secret.txt");

            Assert.True(secret.IsEncrypted);
            Assert.False(listing.Find("plain.txt").IsEncrypted);
            Assert.Equal(1, listing.EncryptedFileCount);

            var destination = Path.Combine(_root, "out-secret");
            var plan = ZipArchiveService.PlanExtraction(listing, listing.GetChildren(""), destination, new ZipExtractOptions());
            var result = ZipArchiveService.Extract(listing, plan, null, CancellationToken.None);

            Assert.Equal(1, result.FilesWritten);
            Assert.Single(result.Errors);
            Assert.Contains("암호", result.Errors[0]);
            Assert.True(File.Exists(Path.Combine(destination, "plain.txt")));
            Assert.False(File.Exists(Path.Combine(destination, "secret.txt")));
            Assert.Throws<ZipArchiveException>(() => ZipArchiveService.ReadEntryBytes(listing, secret, 100));
        }

        // ── 풀기 ───────────────────────────────────────────────────────────

        [Fact]
        public void Extract_CopySemantics_RenamesExistingTopLevel_AndKeepsContent()
        {
            var zip = MakeZip("copy.zip", ("dir/b.txt", "BB"), ("dir/sub/c.txt", "C"), ("top.txt", "T"));
            var destination = Path.Combine(_root, "dest");
            Directory.CreateDirectory(Path.Combine(destination, "dir"));
            File.WriteAllText(Path.Combine(destination, "top.txt"), "old");

            var listing = ZipArchiveService.ReadListing(zip);
            var plan = ZipArchiveService.PlanExtraction(listing, listing.GetChildren(""), destination, new ZipExtractOptions { UniqueTopLevel = true });
            var result = ZipArchiveService.Extract(listing, plan, null, CancellationToken.None);

            Assert.Empty(result.Errors);
            Assert.Equal(3, result.FilesWritten);
            Assert.Equal("old", File.ReadAllText(Path.Combine(destination, "top.txt")));
            Assert.Equal("T", File.ReadAllText(Path.Combine(destination, "top (2).txt")));
            Assert.Equal("BB", File.ReadAllText(Path.Combine(destination, "dir (2)", "b.txt")));
            Assert.Equal("C", File.ReadAllText(Path.Combine(destination, "dir (2)", "sub", "c.txt")));
            Assert.Equal(new DateTime(2024, 5, 6, 7, 8, 10), File.GetLastWriteTime(Path.Combine(destination, "top (2).txt")));
            Assert.Contains(Path.Combine(destination, "dir (2)"), result.TopLevelTargets);
            Assert.Empty(Directory.GetFiles(destination, "*.folderss-part", SearchOption.AllDirectories));
        }

        [Fact]
        public void Extract_SelectionInsideSubfolder_LandsDirectlyInDestination()
        {
            var zip = MakeZip("sel.zip", ("a/b/c.txt", "C"), ("a/d.txt", "D"));
            var listing = ZipArchiveService.ReadListing(zip);
            var destination = Path.Combine(_root, "sel-out");

            var plan = ZipArchiveService.PlanExtraction(listing, new[] { listing.Find("a/b"), listing.Find("a/d.txt") }, destination, new ZipExtractOptions());
            var result = ZipArchiveService.Extract(listing, plan, null, CancellationToken.None);

            Assert.Empty(result.Errors);
            Assert.Equal("C", File.ReadAllText(Path.Combine(destination, "b", "c.txt")));
            Assert.Equal("D", File.ReadAllText(Path.Combine(destination, "d.txt")));
        }

        [Theory]
        [InlineData(ZipConflictMode.Skip, "old", false)]
        [InlineData(ZipConflictMode.Rename, "old", true)]
        [InlineData(ZipConflictMode.Overwrite, "new", false)]
        public void Extract_MergeSemantics_FollowsConflictMode(ZipConflictMode mode, string expected, bool renamedExists)
        {
            var zip = MakeZip("merge.zip", ("dir/a.txt", "new"), ("dir/b.txt", "B"));
            var destination = Path.Combine(_root, "merge-" + mode);
            Directory.CreateDirectory(Path.Combine(destination, "dir"));
            File.WriteAllText(Path.Combine(destination, "dir", "a.txt"), "old");
            File.SetAttributes(Path.Combine(destination, "dir", "a.txt"), FileAttributes.ReadOnly);

            var listing = ZipArchiveService.ReadListing(zip);
            var plan = ZipArchiveService.PlanExtraction(listing, listing.GetChildren(""), destination,
                new ZipExtractOptions { UniqueTopLevel = false, Conflict = mode });
            var result = ZipArchiveService.Extract(listing, plan, null, CancellationToken.None);

            Assert.Empty(result.Errors);
            Assert.Equal(expected, File.ReadAllText(Path.Combine(destination, "dir", "a.txt")));
            Assert.Equal(renamedExists, File.Exists(Path.Combine(destination, "dir", "a (2).txt")));
            Assert.Equal("B", File.ReadAllText(Path.Combine(destination, "dir", "b.txt")));
            Assert.Equal(mode == ZipConflictMode.Skip ? 1 : 0, result.Skipped);
        }

        [Fact]
        public void Extract_UnsafeEntries_NeverWriteOutsideDestination()
        {
            var zip = MakeZip("slip2.zip", ("../escaped.txt", "x"), ("safe.txt", "ok"));
            var destination = Path.Combine(_root, "inner", "dest");

            var listing = ZipArchiveService.ReadListing(zip);
            var plan = ZipArchiveService.PlanExtraction(listing, new[] { listing.Root }, destination, new ZipExtractOptions());
            ZipArchiveService.Extract(listing, plan, null, CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(destination, "safe.txt")));
            Assert.False(File.Exists(Path.Combine(_root, "inner", "escaped.txt")));
            Assert.Throws<ZipArchiveException>(() => ZipArchiveService.EnsureInside(destination, Path.Combine(destination, "..", "x.txt")));
        }

        [Fact]
        public void Extract_InvalidWindowsNames_AreSanitized()
        {
            Assert.Equal("a_b_c", ZipArchiveService.SanitizeSegment("a?b*c"));
            Assert.Equal("_con.txt", ZipArchiveService.SanitizeSegment("con.txt"));
            Assert.Equal("_NUL", ZipArchiveService.SanitizeSegment("NUL"));
            Assert.Equal("name__", ZipArchiveService.SanitizeSegment("name. "));
            Assert.Equal("console.txt", ZipArchiveService.SanitizeSegment("console.txt"));

            var zip = MakeZip("names.zip", ("what?.txt", "q"), ("aux.log", "a"));
            var listing = ZipArchiveService.ReadListing(zip);
            var destination = Path.Combine(_root, "names-out");
            var plan = ZipArchiveService.PlanExtraction(listing, listing.GetChildren(""), destination, new ZipExtractOptions());
            var result = ZipArchiveService.Extract(listing, plan, null, CancellationToken.None);

            Assert.Empty(result.Errors);
            Assert.True(File.Exists(Path.Combine(destination, "what_.txt")));
            Assert.True(File.Exists(Path.Combine(destination, "_aux.log")));
        }

        [Fact]
        public void Extract_ZipChangedAfterListing_Throws()
        {
            var zip = MakeZip("changing.zip", ("a.txt", "A"));
            var listing = ZipArchiveService.ReadListing(zip);
            MakeZip("changing.zip", ("a.txt", "A"), ("b.txt", "B"));
            File.SetLastWriteTimeUtc(zip, listing.ArchiveWriteTimeUtc.AddMinutes(1));

            Assert.False(listing.IsCurrent());
            var plan = ZipArchiveService.PlanExtraction(listing, listing.GetChildren(""), Path.Combine(_root, "chg"), new ZipExtractOptions());
            var error = Assert.Throws<ZipArchiveException>(() => ZipArchiveService.Extract(listing, plan, null, CancellationToken.None));
            Assert.Equal(ZipArchiveException.ChangedMessage, error.Message);
        }

        [Fact]
        public void Extract_Cancelled_LeavesNoPartFiles()
        {
            var zip = MakeZip("cancel.zip", ("a.txt", "A"), ("b.txt", "B"));
            var listing = ZipArchiveService.ReadListing(zip);
            var destination = Path.Combine(_root, "cancel-out");
            var plan = ZipArchiveService.PlanExtraction(listing, listing.GetChildren(""), destination, new ZipExtractOptions());
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                var result = ZipArchiveService.Extract(listing, plan, null, cancellation.Token);
                Assert.True(result.Cancelled);
            }
            Assert.Empty(Directory.GetFiles(destination, "*", SearchOption.AllDirectories));
        }

        [Fact]
        public void ReadEntryBytes_ReadsPrefixOnly()
        {
            var zip = MakeZip("bytes.zip", ("long.txt", new string('x', 5000)));
            var listing = ZipArchiveService.ReadListing(zip);

            var bytes = ZipArchiveService.ReadEntryBytes(listing, listing.Find("long.txt"), 100);

            Assert.Equal(100, bytes.Length);
        }

        // ── 만들기 ─────────────────────────────────────────────────────────

        [Fact]
        public void CreateZip_FolderAndFiles_RoundTripWithUtf8Names()
        {
            var source = Path.Combine(_root, "src");
            Directory.CreateDirectory(Path.Combine(source, "프로젝트", "빈 폴더"));
            File.WriteAllText(Path.Combine(source, "프로젝트", "설명.txt"), "내용");
            File.WriteAllText(Path.Combine(source, "단독.txt"), "solo");
            var zipPath = Path.Combine(source, "묶음.zip");

            var result = ZipArchiveService.CreateZip(
                new[] { Path.Combine(source, "프로젝트"), Path.Combine(source, "단독.txt") }, zipPath, CompressionLevel.Optimal, null, CancellationToken.None);

            Assert.Empty(result.Errors);
            Assert.Equal(zipPath, result.ZipPath);
            Assert.Equal(2, result.FileCount);
            var listing = ZipArchiveService.ReadListing(zipPath);
            Assert.Equal(new[] { "단독.txt", "프로젝트" }, Names(listing.GetChildren("")));
            Assert.Equal(new[] { "빈 폴더", "설명.txt" }, Names(listing.GetChildren("프로젝트")));
            using (var archive = ZipFile.OpenRead(zipPath))
                Assert.Contains(archive.Entries, entry => entry.FullName == "프로젝트/설명.txt"); // UTF-8 플래그로 저장
            Assert.Empty(Directory.GetFiles(source, "*.folderss-part"));
        }

        [Fact]
        public void CreateZip_ExistingName_UsesUniqueName_AndSkipsItself()
        {
            var source = Path.Combine(_root, "self");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "a.txt"), "A");
            File.WriteAllText(Path.Combine(source, "self.zip"), "existing");

            var result = ZipArchiveService.CreateZip(new[] { source }, Path.Combine(source, "self.zip"), CompressionLevel.Fastest, null, CancellationToken.None);

            Assert.Equal(Path.Combine(source, "self (2).zip"), result.ZipPath);
            var listing = ZipArchiveService.ReadListing(result.ZipPath);
            var names = Names(listing.GetChildren("self"));
            Assert.Contains("a.txt", names);
            Assert.DoesNotContain("self (2).zip", names);
            Assert.Equal("existing", File.ReadAllText(Path.Combine(source, "self.zip")));
        }

        // ── 임시 사본 ──────────────────────────────────────────────────────

        [Fact]
        public void TempStore_ExtractForOpen_ReadOnlyCopy_ReusedAndCleanedUp()
        {
            ZipTempStore.CleanupSession();
            ZipTempStore.RootOverride = Path.Combine(_root, "temp");
            var zip = MakeZip("open.zip", ("docs/읽기.md", "# 제목"));
            var listing = ZipArchiveService.ReadListing(zip);
            var entry = listing.Find("docs/읽기.md");

            var path = ZipTempStore.ExtractForOpen(listing, entry, null, CancellationToken.None);

            Assert.StartsWith(ZipTempStore.SessionDirectory, path);
            Assert.EndsWith(Path.Combine("docs", "읽기.md"), path);
            Assert.Equal("# 제목", File.ReadAllText(path));
            Assert.True((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0);
            Assert.Equal(path, ZipTempStore.ExtractForOpen(listing, entry, null, CancellationToken.None));

            var session = ZipTempStore.SessionDirectory;
            ZipTempStore.CleanupSession();
            Assert.False(Directory.Exists(session));
        }

        [Fact]
        public void TempStore_SweepStale_DeletesOldFoldersOfFinishedProcesses()
        {
            ZipTempStore.RootOverride = Path.Combine(_root, "sweep");
            var old = Path.Combine(ZipTempStore.Root, "999999-20200101000000");
            var recent = Path.Combine(ZipTempStore.Root, "999998-20200101000000");
            Directory.CreateDirectory(old);
            Directory.CreateDirectory(recent);
            File.WriteAllText(Path.Combine(old, "x.txt"), "x");
            File.SetAttributes(Path.Combine(old, "x.txt"), FileAttributes.ReadOnly);
            Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-3));

            var deleted = ZipTempStore.SweepStale(TimeSpan.FromDays(1), null);

            Assert.Equal(1, deleted);
            Assert.False(Directory.Exists(old));
            Assert.True(Directory.Exists(recent));
        }

        // ── 도움 ───────────────────────────────────────────────────────────

        /// <summary>UTF-8 바이트로 쓰지만 코드 페이지가 65001이 아니라 .NET이 UTF-8 플래그를 켜지 않는다(macOS 압축 유틸리티 같은 zip).</summary>
        private sealed class Utf8WithoutFlagEncoding : Encoding
        {
            public override int GetByteCount(char[] chars, int index, int count) => UTF8.GetByteCount(chars, index, count);
            public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex) => UTF8.GetBytes(chars, charIndex, charCount, bytes, byteIndex);
            public override int GetCharCount(byte[] bytes, int index, int count) => UTF8.GetCharCount(bytes, index, count);
            public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex) => UTF8.GetChars(bytes, byteIndex, byteCount, chars, charIndex);
            public override int GetMaxByteCount(int charCount) => UTF8.GetMaxByteCount(charCount);
            public override int GetMaxCharCount(int byteCount) => UTF8.GetMaxCharCount(byteCount);
        }

        /// <summary>항목의 일반 목적 플래그 bit 0(암호화)을 켠다. 저장(무압축) 항목이라 내용에 시그니처가 섞이지 않는다.</summary>
        private static void SetEncryptedFlag(string zipPath, string entryName)
        {
            var bytes = File.ReadAllBytes(zipPath);
            var name = Encoding.ASCII.GetBytes(entryName);
            for (var i = 0; i + 4 <= bytes.Length; i++)
            {
                var local = bytes[i] == 0x50 && bytes[i + 1] == 0x4B && bytes[i + 2] == 0x03 && bytes[i + 3] == 0x04;
                var central = bytes[i] == 0x50 && bytes[i + 1] == 0x4B && bytes[i + 2] == 0x01 && bytes[i + 3] == 0x02;
                if (!local && !central)
                    continue;
                var nameOffset = i + (local ? 30 : 46);
                if (nameOffset + name.Length > bytes.Length || !bytes.Skip(nameOffset).Take(name.Length).SequenceEqual(name))
                    continue;
                bytes[i + (local ? 6 : 8)] |= 0x01;
            }
            File.WriteAllBytes(zipPath, bytes);
        }
    }
}

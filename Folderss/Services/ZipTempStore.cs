using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Folderss.Services
{
    /// <summary>
    /// zip 안 파일을 열거나 클립보드로 복사할 때 쓰는 임시 폴더.
    /// <c>%TEMP%\Folderss\zip\&lt;pid&gt;-&lt;시작 시각&gt;\</c>(이 실행 전용) 아래에 둔다.
    /// 프로세스가 끝날 때 이 실행의 폴더를 지우고(다른 프로그램이 잡고 있는 파일은 남음),
    /// 처음 쓸 때 하루 지난 다른 실행의 폴더(그 프로세스가 더는 없을 때만)를 지운다.
    /// </summary>
    public static class ZipTempStore
    {
        private static readonly object Gate = new object();
        private static string _sessionDirectory;
        private static int _clipboardCounter;
        private static bool _swept;

        /// <summary>테스트에서 임시 루트를 바꾼다. null이면 <c>%TEMP%\Folderss\zip</c>.</summary>
        internal static string RootOverride { get; set; }

        public static string Root
        {
            get { return RootOverride ?? Path.Combine(Path.GetTempPath(), "Folderss", "zip"); }
        }

        static ZipTempStore()
        {
            AppDomain.CurrentDomain.ProcessExit += (sender, args) => CleanupSession();
        }

        /// <summary>이 실행 전용 임시 폴더(처음 부를 때 만든다).</summary>
        public static string SessionDirectory
        {
            get
            {
                lock (Gate)
                {
                    if (_sessionDirectory == null || !Directory.Exists(_sessionDirectory))
                    {
                        var process = Process.GetCurrentProcess();
                        _sessionDirectory = Path.Combine(Root, string.Format("{0}-{1:yyyyMMddHHmmss}", process.Id, DateTime.Now));
                        Directory.CreateDirectory(_sessionDirectory);
                    }
                    if (!_swept)
                    {
                        _swept = true;
                        var current = _sessionDirectory;
                        ThreadPool.QueueUserWorkItem(_ => SweepStale(TimeSpan.FromDays(1), current));
                    }
                    return _sessionDirectory;
                }
            }
        }

        /// <summary>
        /// zip 안 파일 하나를 임시 폴더에 풀어 경로를 돌려준다. 같은 zip(크기·시각 같음)의 같은 파일이 이미 풀려 있으면 다시 풀지 않는다.
        /// 고친 내용이 zip에 들어가지 않는다는 것을 편집기가 알 수 있게 읽기 전용 특성을 붙인다.
        /// </summary>
        public static string ExtractForOpen(ZipListing listing, ZipEntryInfo entry, IProgress<ZipProgress> progress, CancellationToken cancellationToken)
        {
            if (entry == null || entry.IsDirectory)
                throw new ArgumentException("파일 항목이 아닙니다.", nameof(entry));
            if (entry.IsEncrypted)
                throw new ZipArchiveException(ZipArchiveException.EncryptedMessage);

            var directory = GetArchiveDirectory(listing);
            var target = Path.Combine(directory, ZipArchiveService.SanitizeRelative(entry.InnerPath));
            ZipArchiveService.EnsureInside(directory, target);

            if (File.Exists(target))
            {
                var existing = new FileInfo(target);
                if (existing.Length == entry.Size)
                    return target;
                ZipArchiveService.ClearReadOnly(target);
            }

            ZipArchiveService.EnsureCurrent(listing);
            using (var archive = ZipArchiveService.OpenArchive(listing.ArchivePath))
            {
                var zipEntry = ZipArchiveService.GetArchiveEntry(archive, listing, entry);
                var state = new ZipProgress { Total = 1, BytesTotal = entry.Size, CurrentName = entry.InnerPath };
                ZipArchiveService.ExtractEntryToFile(zipEntry, entry, target, true, state, progress, cancellationToken);
            }

            try
            {
                File.SetAttributes(target, File.GetAttributes(target) | FileAttributes.ReadOnly);
            }
            catch (Exception)
            {
                // 특성을 못 붙여도 열 수는 있다.
            }
            return target;
        }

        /// <summary>클립보드 복사용 새 폴더(호출마다 다른 폴더).</summary>
        public static string CreateClipboardDirectory()
        {
            var index = Interlocked.Increment(ref _clipboardCounter);
            var path = Path.Combine(SessionDirectory, "clip-" + index.ToString("D3") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>이 실행의 임시 폴더를 지운다. 잡혀 있는 파일은 남는다(다음 실행의 정리 대상).</summary>
        public static void CleanupSession()
        {
            string directory;
            lock (Gate)
            {
                directory = _sessionDirectory;
                _sessionDirectory = null;
            }
            if (directory != null)
                TryDeleteDirectory(directory);
        }

        /// <summary>
        /// <paramref name="age"/>보다 오래된 다른 실행의 폴더를 지운다. 폴더 이름의 pid로 같은 이름의 프로세스가 아직 실행 중이면 남긴다
        /// (트레이에서 며칠씩 실행되는 다른 Folderss 창의 열린 파일을 지우지 않기 위해). 지운 폴더 수.
        /// </summary>
        public static int SweepStale(TimeSpan age, string keep)
        {
            var deleted = 0;
            try
            {
                if (!Directory.Exists(Root))
                    return 0;
                var threshold = DateTime.UtcNow - age;
                var currentName = Process.GetCurrentProcess().ProcessName;
                foreach (var directory in Directory.EnumerateDirectories(Root))
                {
                    if (keep != null && string.Equals(directory.TrimEnd('\\'), keep.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (Directory.GetLastWriteTimeUtc(directory) > threshold || IsOwnerRunning(Path.GetFileName(directory), currentName))
                        continue;
                    if (TryDeleteDirectory(directory))
                        deleted++;
                }
            }
            catch (Exception)
            {
                // 정리는 최선 노력.
            }
            return deleted;
        }

        private static bool IsOwnerRunning(string folderName, string processName)
        {
            var dash = folderName.IndexOf('-');
            int pid;
            if (dash <= 0 || !int.TryParse(folderName.Substring(0, dash), out pid))
                return false;
            try
            {
                using (var process = Process.GetProcessById(pid))
                    return string.Equals(process.ProcessName, processName, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string GetArchiveDirectory(ZipListing listing)
        {
            var key = string.Format("{0}|{1}|{2}", listing.ArchivePath.ToUpperInvariant(), listing.ArchiveLength, listing.ArchiveWriteTimeUtc.Ticks);
            string hash;
            using (var sha = SHA256.Create())
                hash = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Substring(0, 10).ToLowerInvariant();
            // zip 이름을 붙여 두면 열린 탭·편집기 제목 표시줄에서 어느 zip의 사본인지 알 수 있다.
            var name = ZipArchiveService.SanitizeSegment(Path.GetFileNameWithoutExtension(listing.ArchivePath));
            if (name.Length > 40)
                name = name.Substring(0, 40);
            var directory = Path.Combine(SessionDirectory, name + "-" + hash);
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static bool TryDeleteDirectory(string directory)
        {
            try
            {
                if (!Directory.Exists(directory))
                    return true;
                foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                    ZipArchiveService.ClearReadOnly(file);
                Directory.Delete(directory, true);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}

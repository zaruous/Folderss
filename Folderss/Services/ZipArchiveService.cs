using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Folderss.Services
{
    /// <summary>
    /// zip 가상 경로: <c>C:\a\b.zip\sub\file.txt</c> = zip 파일 경로(<c>C:\a\b.zip</c>) + zip 안 경로(<c>sub/file.txt</c>, '/' 구분).
    /// 폴더 패널의 <c>CurrentPath</c>가 이 형태를 그대로 쓴다(경로 입력란·뒤로/앞으로·세션 저장).
    /// </summary>
    public static class ZipArchivePath
    {
        /// <summary>Enter·더블클릭으로 들어가는 확장자. 나머지 zip 기반 형식은 기존 동작(뷰어·기본 프로그램)을 유지한다.</summary>
        public const string BrowsableExtension = ".zip";

        /// <summary>zip 형식이지만 Enter는 기존 동작을 유지하고 우클릭 "zip으로 열어 보기"로만 들어가는 확장자.</summary>
        private static readonly HashSet<string> ZipBasedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jar", ".war", ".ear", ".aar", ".apk", ".nupkg", ".snupkg", ".vsix", ".whl", ".epub", ".xpi",
            ".docx", ".xlsx", ".pptx", ".odt", ".ods", ".odp", ".appx", ".msix", ".appxbundle", ".msixbundle", ".kmz", ".3mf"
        };

        public static bool IsBrowsableArchive(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                   string.Equals(Path.GetExtension(path), BrowsableExtension, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>우클릭 "zip으로 열어 보기"를 보일 파일인가: zip 기반 확장자이고 실제로 zip 시그니처가 있다.</summary>
        public static bool CanOpenAsZip(string path)
        {
            return !string.IsNullOrEmpty(path) && ZipBasedExtensions.Contains(Path.GetExtension(path)) && HasZipSignature(path);
        }

        /// <summary>파일 앞 4바이트가 zip 시그니처(PK 03 04, 빈 zip PK 05 06, 분할 표시 PK 07 08)인가.</summary>
        public static bool HasZipSignature(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var header = new byte[4];
                    if (stream.Read(header, 0, 4) != 4 || header[0] != (byte)'P' || header[1] != (byte)'K')
                        return false;
                    return (header[2] == 3 && header[3] == 4) || (header[2] == 5 && header[3] == 6) || (header[2] == 7 && header[3] == 8);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 경로를 zip 파일과 zip 안 경로로 나눈다. 끝에서부터 위로 올라가며 처음 만나는 "존재하는 파일"이 zip이면 성공.
        /// 확장자가 <c>.zip</c>이거나 zip 시그니처가 있는 파일만 zip으로 본다. 실제 폴더 경로·zip이 아닌 파일 아래 경로는 false.
        /// zip 안 경로가 실제로 있는지는 보지 않는다(목록을 읽은 뒤 확인).
        /// </summary>
        public static bool TrySplit(string path, out string archivePath, out string innerPath)
        {
            archivePath = null;
            innerPath = null;
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string candidate;
            try
            {
                candidate = TrimEnd(Path.GetFullPath(path));
            }
            catch (Exception)
            {
                return false;
            }

            if (Directory.Exists(candidate))
                return false;

            var segments = new List<string>();
            while (!string.IsNullOrEmpty(candidate))
            {
                if (File.Exists(candidate))
                {
                    if (!IsBrowsableArchive(candidate) && !HasZipSignature(candidate))
                        return false;
                    segments.Reverse();
                    archivePath = candidate;
                    innerPath = NormalizeInnerPath(string.Join("/", segments));
                    return true;
                }
                if (Directory.Exists(candidate))
                    return false;

                var parent = Path.GetDirectoryName(candidate);
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, candidate, StringComparison.OrdinalIgnoreCase))
                    return false;
                segments.Add(Path.GetFileName(candidate));
                candidate = TrimEnd(parent);
            }
            return false;
        }

        /// <summary>zip이 있는 가상 경로인가(zip 파일이 존재). zip 안 폴더가 실제로 있는지는 보지 않는다.</summary>
        public static bool Exists(string path)
        {
            return TrySplit(path, out _, out _);
        }

        public static string Combine(string archivePath, string innerPath)
        {
            var inner = NormalizeInnerPath(innerPath);
            return inner.Length == 0 ? archivePath : archivePath + Path.DirectorySeparatorChar + inner.Replace('/', Path.DirectorySeparatorChar);
        }

        /// <summary>'\'를 '/'로, 빈 조각·"."를 없앤다. 앞뒤 '/'는 남기지 않는다.</summary>
        public static string NormalizeInnerPath(string innerPath)
        {
            if (string.IsNullOrEmpty(innerPath))
                return string.Empty;
            var parts = innerPath.Replace('\\', '/').Split('/').Where(part => part.Length > 0 && part != ".");
            return string.Join("/", parts);
        }

        public static string GetParentInnerPath(string innerPath)
        {
            var inner = NormalizeInnerPath(innerPath);
            var index = inner.LastIndexOf('/');
            return index < 0 ? string.Empty : inner.Substring(0, index);
        }

        public static string GetInnerName(string innerPath)
        {
            var inner = NormalizeInnerPath(innerPath);
            var index = inner.LastIndexOf('/');
            return index < 0 ? inner : inner.Substring(index + 1);
        }

        private static string TrimEnd(string path)
        {
            var root = Path.GetPathRoot(path);
            return string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
                ? path
                : path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    /// <summary>zip 작업 실패를 사용자에게 보일 문장으로 담는다.</summary>
    public sealed class ZipArchiveException : IOException
    {
        public const string ChangedMessage = "zip 파일이 바뀌었습니다. 새로 고친(F5) 뒤 다시 시도하세요.";
        public const string EncryptedMessage = "암호로 보호된 항목이라 풀 수 없습니다(암호 zip은 아직 지원하지 않습니다). 7-Zip 같은 다른 프로그램을 쓰세요.";

        public ZipArchiveException(string message, Exception inner = null) : base(message, inner)
        {
        }
    }

    /// <summary>zip 안 항목(파일 또는 폴더) 하나.</summary>
    public sealed class ZipEntryInfo
    {
        /// <summary>표시 이름(NFC). 풀 때는 <see cref="ZipArchiveService.SanitizeSegment"/>로 Windows에서 쓸 수 있게 바꾼다.</summary>
        public string Name { get; internal set; }
        /// <summary>zip 안 경로('/' 구분, 표시 이름 기준). 맨 위 폴더는 빈 문자열.</summary>
        public string InnerPath { get; internal set; }
        public bool IsDirectory { get; internal set; }
        public long Size { get; internal set; }
        public long CompressedSize { get; internal set; }
        public DateTime ModifiedAt { get; internal set; }
        public bool IsEncrypted { get; internal set; }
        /// <summary><c>ZipArchive.Entries</c> 순서. 디렉터리 항목 없이 경로에서 만든 폴더는 -1.</summary>
        public int EntryIndex { get; internal set; } = -1;
        /// <summary>열 때 대조할 원래 항목 이름(같은 인코딩으로 읽은 값).</summary>
        public string EntryFullName { get; internal set; }
    }

    /// <summary>zip 한 개의 목록을 폴더 트리로 정리한 것. 만든 뒤에는 바뀌지 않는다(여러 스레드에서 읽어도 됨).</summary>
    public sealed class ZipListing
    {
        private sealed class Folder
        {
            public ZipEntryInfo Info;
            public readonly Dictionary<string, ZipEntryInfo> Children = new Dictionary<string, ZipEntryInfo>(StringComparer.OrdinalIgnoreCase);
        }

        private readonly Dictionary<string, Folder> _folders = new Dictionary<string, Folder>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _unsafeNames = new List<string>();

        public string ArchivePath { get; }
        public long ArchiveLength { get; }
        public DateTime ArchiveWriteTimeUtc { get; }
        /// <summary>zip의 실제 항목 수(<c>ZipArchive.Entries.Count</c>). 풀 때 zip이 바뀌지 않았는지 대조한다.</summary>
        public int EntryCount { get; private set; }
        public int FileCount { get; private set; }
        public int FolderCount { get; private set; }
        public long TotalSize { get; private set; }
        public long TotalCompressedSize { get; private set; }
        public int EncryptedFileCount { get; private set; }
        /// <summary>zip Slip 위험(<c>..</c>)·드라이브·대체 스트림(<c>:</c>)이 든 이름이라 목록에서 숨기고 풀지 않는 항목 수.</summary>
        public int UnsafeEntryCount { get; private set; }
        public IReadOnlyList<string> UnsafeEntryNames { get { return _unsafeNames; } }
        /// <summary>같은 경로가 파일과 폴더로 겹쳐 하나를 버린 수(같은 이름이 여러 번이면 뒤의 것을 쓴다).</summary>
        public int ConflictCount { get; private set; }
        public ZipEntryInfo Root { get { return _folders[string.Empty].Info; } }

        internal ZipListing(string archivePath, long length, DateTime writeTimeUtc, DateTime localWriteTime)
        {
            ArchivePath = archivePath;
            ArchiveLength = length;
            ArchiveWriteTimeUtc = writeTimeUtc;
            _folders[string.Empty] = new Folder
            {
                Info = new ZipEntryInfo
                {
                    Name = Path.GetFileName(archivePath),
                    InnerPath = string.Empty,
                    IsDirectory = true,
                    ModifiedAt = localWriteTime
                }
            };
        }

        /// <summary>zip 파일 크기·수정 시각이 목록을 읽을 때와 같은가.</summary>
        public bool IsCurrent()
        {
            try
            {
                var info = new FileInfo(ArchivePath);
                return info.Exists && info.Length == ArchiveLength && info.LastWriteTimeUtc == ArchiveWriteTimeUtc;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool FolderExists(string innerPath)
        {
            return _folders.ContainsKey(ZipArchivePath.NormalizeInnerPath(innerPath));
        }

        /// <summary>폴더 바로 아래 항목(폴더·파일). 없는 폴더면 빈 목록.</summary>
        public IReadOnlyList<ZipEntryInfo> GetChildren(string innerPath)
        {
            Folder folder;
            return _folders.TryGetValue(ZipArchivePath.NormalizeInnerPath(innerPath), out folder)
                ? folder.Children.Values.ToList()
                : new List<ZipEntryInfo>();
        }

        /// <summary>zip 안 경로의 파일 또는 폴더. 없으면 null. 빈 경로는 맨 위 폴더.</summary>
        public ZipEntryInfo Find(string innerPath)
        {
            var inner = ZipArchivePath.NormalizeInnerPath(innerPath);
            Folder folder;
            if (_folders.TryGetValue(inner, out folder))
                return folder.Info;
            if (!_folders.TryGetValue(ZipArchivePath.GetParentInnerPath(inner), out folder))
                return null;
            ZipEntryInfo entry;
            return folder.Children.TryGetValue(ZipArchivePath.GetInnerName(inner), out entry) ? entry : null;
        }

        /// <summary>폴더 아래 모든 항목(깊이 우선, 폴더가 그 내용보다 먼저). 파일이면 빈 목록.</summary>
        public IEnumerable<ZipEntryInfo> EnumerateDescendants(ZipEntryInfo folderInfo)
        {
            Folder folder;
            if (folderInfo == null || !folderInfo.IsDirectory || !_folders.TryGetValue(folderInfo.InnerPath, out folder))
                yield break;
            foreach (var child in folder.Children.Values)
            {
                yield return child;
                if (child.IsDirectory)
                {
                    foreach (var descendant in EnumerateDescendants(child))
                        yield return descendant;
                }
            }
        }

        /// <summary>항목들(폴더면 그 아래 전체)의 파일 수와 압축 전 크기.</summary>
        public void Measure(IEnumerable<ZipEntryInfo> items, out int fileCount, out long totalSize)
        {
            fileCount = 0;
            totalSize = 0;
            foreach (var item in items)
            {
                var all = item.IsDirectory ? EnumerateDescendants(item) : new[] { item };
                foreach (var entry in all.Where(entry => !entry.IsDirectory))
                {
                    fileCount++;
                    totalSize += entry.Size;
                }
            }
        }

        internal void Add(ZipArchiveEntry entry, int index)
        {
            var fullName = entry.FullName ?? string.Empty;
            var normalized = fullName.Replace('\\', '/');
            var segments = new List<string>();
            foreach (var part in normalized.Split('/'))
            {
                if (part.Length == 0 || part == ".")
                    continue;
                if (part == ".." || part.IndexOf(':') >= 0 || part.IndexOf('\0') >= 0)
                {
                    UnsafeEntryCount++;
                    if (_unsafeNames.Count < 10)
                        _unsafeNames.Add(fullName);
                    return;
                }
                segments.Add(part.Normalize(NormalizationForm.FormC));
            }
            if (segments.Count == 0)
                return;

            var innerPath = string.Join("/", segments);
            var isDirectory = normalized.EndsWith("/", StringComparison.Ordinal) ||
                              (entry.Length == 0 && IsDirectoryAttribute(entry.ExternalAttributes));
            var modified = SafeTime(entry);

            if (isDirectory)
            {
                var folder = EnsureFolder(innerPath);
                if (folder == null)
                    return;
                folder.Info.ModifiedAt = modified;
                folder.Info.EntryIndex = index;
                folder.Info.EntryFullName = fullName;
                return;
            }

            if (_folders.ContainsKey(innerPath))
            {
                ConflictCount++;
                return;
            }
            var parent = EnsureFolder(ZipArchivePath.GetParentInnerPath(innerPath));
            if (parent == null)
                return;

            var name = segments[segments.Count - 1];
            ZipEntryInfo previous;
            if (parent.Children.TryGetValue(name, out previous))
            {
                // 같은 경로가 여러 번이면 뒤의 것(순서대로 풀면 덮어쓰는 결과)을 쓴다.
                RemoveFileCounts(previous);
                ConflictCount++;
            }

            var info = new ZipEntryInfo
            {
                Name = name,
                InnerPath = innerPath,
                IsDirectory = false,
                Size = entry.Length,
                CompressedSize = entry.CompressedLength,
                ModifiedAt = modified,
                IsEncrypted = entry.IsEncrypted,
                EntryIndex = index,
                EntryFullName = fullName
            };
            parent.Children[name] = info;
            FileCount++;
            TotalSize += info.Size;
            TotalCompressedSize += info.CompressedSize;
            if (info.IsEncrypted)
                EncryptedFileCount++;
        }

        internal void Complete(int entryCount)
        {
            EntryCount = entryCount;
        }

        private Folder EnsureFolder(string innerPath)
        {
            Folder folder;
            if (_folders.TryGetValue(innerPath, out folder))
                return folder;

            var parent = EnsureFolder(ZipArchivePath.GetParentInnerPath(innerPath));
            var name = ZipArchivePath.GetInnerName(innerPath);
            ZipEntryInfo existing;
            if (parent.Children.TryGetValue(name, out existing) && !existing.IsDirectory)
            {
                // 같은 이름의 파일이 먼저 나왔다 — 그 아래에 항목이 있으므로 폴더로 본다.
                parent.Children.Remove(name);
                RemoveFileCounts(existing);
                ConflictCount++;
            }

            folder = new Folder
            {
                Info = new ZipEntryInfo
                {
                    Name = name,
                    InnerPath = innerPath,
                    IsDirectory = true,
                    ModifiedAt = Root.ModifiedAt
                }
            };
            _folders[innerPath] = folder;
            parent.Children[name] = folder.Info;
            FolderCount++;
            return folder;
        }

        private void RemoveFileCounts(ZipEntryInfo file)
        {
            FileCount--;
            TotalSize -= file.Size;
            TotalCompressedSize -= file.CompressedSize;
            if (file.IsEncrypted)
                EncryptedFileCount--;
        }

        private static bool IsDirectoryAttribute(int externalAttributes)
        {
            // 낮은 바이트 = MS-DOS 특성(0x10 디렉터리), 높은 16비트 = Unix 모드(S_IFDIR 0x4000).
            return (externalAttributes & 0x10) != 0 || ((externalAttributes >> 16) & 0xF000) == 0x4000;
        }

        private static DateTime SafeTime(ZipArchiveEntry entry)
        {
            try
            {
                return entry.LastWriteTime.DateTime;
            }
            catch (Exception)
            {
                return new DateTime(1980, 1, 1);
            }
        }
    }

    /// <summary>
    /// zip 목록 캐시. 같은 zip은 파일 크기·수정 시각이 같으면 다시 읽지 않는다(최근 <see cref="Capacity"/>개).
    /// 읽기는 백그라운드에서 하고, 같은 zip을 동시에 요청하면 한 번만 읽는다.
    /// </summary>
    public static class ZipListingCache
    {
        public const int Capacity = 8;
        private static readonly object Gate = new object();
        private static readonly LinkedList<ZipListing> Recent = new LinkedList<ZipListing>();
        private static readonly Dictionary<string, Task<ZipListing>> Pending = new Dictionary<string, Task<ZipListing>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>읽어 둔 목록이 있고 zip이 그대로면 그 목록, 아니면 null. 파일 상태만 확인하므로 UI 스레드에서 불러도 된다.</summary>
        public static ZipListing TryGetCurrent(string archivePath)
        {
            ZipListing listing;
            lock (Gate)
            {
                var node = Find(archivePath);
                if (node == null)
                    return null;
                listing = node.Value;
            }

            var current = listing.IsCurrent();
            lock (Gate)
            {
                var node = Find(archivePath);
                if (node != null && ReferenceEquals(node.Value, listing))
                {
                    Recent.Remove(node);
                    if (current)
                        Recent.AddFirst(node);
                }
            }
            return current ? listing : null;
        }

        public static Task<ZipListing> GetAsync(string archivePath)
        {
            var current = TryGetCurrent(archivePath);
            if (current != null)
                return Task.FromResult(current);

            lock (Gate)
            {
                Task<ZipListing> pending;
                if (Pending.TryGetValue(archivePath, out pending))
                    return pending;

                var task = Task.Run(() => ZipArchiveService.ReadListing(archivePath));
                Pending[archivePath] = task;
                task.ContinueWith(completed =>
                {
                    lock (Gate)
                    {
                        Pending.Remove(archivePath);
                        if (completed.Status != TaskStatus.RanToCompletion)
                            return;
                        var old = Find(archivePath);
                        if (old != null)
                            Recent.Remove(old);
                        Recent.AddFirst(completed.Result);
                        while (Recent.Count > Capacity)
                            Recent.RemoveLast();
                    }
                }, TaskScheduler.Default);
                return task;
            }
        }

        public static void Invalidate(string archivePath)
        {
            lock (Gate)
            {
                var node = Find(archivePath);
                if (node != null)
                    Recent.Remove(node);
            }
        }

        private static LinkedListNode<ZipListing> Find(string archivePath)
        {
            for (var node = Recent.First; node != null; node = node.Next)
            {
                if (string.Equals(node.Value.ArchivePath, archivePath, StringComparison.OrdinalIgnoreCase))
                    return node;
            }
            return null;
        }
    }

    public enum ZipConflictMode
    {
        /// <summary>같은 이름이 있으면 <c>이름 (2)</c>로 만들어 둘 다 유지 (기본).</summary>
        Rename,
        Skip,
        /// <summary>덮어쓰기 — 되돌릴 수 없어 대화상자에서 경고·확인 후에만.</summary>
        Overwrite
    }

    public sealed class ZipExtractOptions
    {
        /// <summary>
        /// true: 기존 복사와 같은 규칙 — 선택한 최상위 항목 이름이 대상에 있으면 <c>이름 (2)</c>로 새로 만든다(덮어쓰기 없음).
        /// false: 대상 폴더에 합친다. 같은 이름 파일은 <see cref="Conflict"/>대로.
        /// </summary>
        public bool UniqueTopLevel { get; set; } = true;
        public ZipConflictMode Conflict { get; set; } = ZipConflictMode.Rename;
    }

    public sealed class ZipPlannedItem
    {
        public ZipEntryInfo Entry { get; internal set; }
        public string TargetPath { get; internal set; }
        public bool IsTopLevel { get; internal set; }
    }

    public sealed class ZipExtractPlan
    {
        public string Destination { get; internal set; }
        public ZipExtractOptions Options { get; internal set; }
        public List<ZipPlannedItem> Items { get; } = new List<ZipPlannedItem>();
        public int FileCount { get; internal set; }
        public long TotalBytes { get; internal set; }
    }

    public sealed class ZipExtractResult
    {
        public int FilesWritten { get; internal set; }
        public int Skipped { get; internal set; }
        public bool Cancelled { get; internal set; }
        /// <summary>오류가 너무 많아 중간에 멈췄다.</summary>
        public bool Stopped { get; internal set; }
        public List<string> Errors { get; } = new List<string>();
        /// <summary>실제로 만든 최상위 항목 경로(선택해 보이기·클립보드용).</summary>
        public List<string> TopLevelTargets { get; } = new List<string>();
    }

    public sealed class ZipCreateResult
    {
        public string ZipPath { get; internal set; }
        public int FileCount { get; internal set; }
        public int FolderCount { get; internal set; }
        public bool Cancelled { get; internal set; }
        public List<string> Errors { get; } = new List<string>();
    }

    public sealed class ZipProgress
    {
        public int Done { get; set; }
        public int Total { get; set; }
        public long BytesDone { get; set; }
        public long BytesTotal { get; set; }
        public string CurrentName { get; set; }
    }

    /// <summary>
    /// zip 읽기·풀기·만들기 (순수 System.IO — 테스트 프로젝트에서 단독 실행). 읽기 전용: 기존 zip을 고치지 않는다.
    /// 항목 이름은 <see cref="ZipEntryNameEncoding"/>으로 읽는다(CP949·UTF-8 판정).
    /// </summary>
    public static class ZipArchiveService
    {
        public const int MaxErrors = 100;
        private const string PartSuffix = ".folderss-part";
        private const int BufferSize = 81920;

        private static readonly HashSet<string> ReservedDeviceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        /// <summary>zip 목록을 읽는다. 실패는 사용자에게 보일 문장의 <see cref="ZipArchiveException"/>.</summary>
        public static ZipListing ReadListing(string archivePath)
        {
            FileInfo info;
            try
            {
                info = new FileInfo(archivePath);
            }
            catch (Exception ex)
            {
                throw new ZipArchiveException("zip 경로가 잘못되었습니다: " + ex.Message, ex);
            }
            if (!info.Exists)
                throw new ZipArchiveException("zip 파일을 찾을 수 없습니다.\n" + archivePath);

            var listing = new ZipListing(info.FullName, info.Length, info.LastWriteTimeUtc, info.LastWriteTime);
            try
            {
                using (var archive = OpenArchive(info.FullName))
                {
                    var entries = archive.Entries;
                    for (var index = 0; index < entries.Count; index++)
                        listing.Add(entries[index], index);
                    listing.Complete(entries.Count);
                }
            }
            catch (InvalidDataException ex)
            {
                throw new ZipArchiveException("zip 파일이 손상되었거나 zip 형식이 아닙니다.\n(" + ex.Message + ")", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new ZipArchiveException("zip 파일을 읽을 권한이 없습니다.\n" + archivePath, ex);
            }
            catch (IOException ex) when (!(ex is ZipArchiveException))
            {
                throw new ZipArchiveException("zip 파일을 읽을 수 없습니다: " + ex.Message, ex);
            }
            return listing;
        }

        /// <summary>읽기 전용으로 연다. 다른 프로그램이 쓰거나 지우는 것을 막지 않는다.</summary>
        public static ZipArchive OpenArchive(string archivePath)
        {
            var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                BufferSize, FileOptions.RandomAccess);
            try
            {
                return new ZipArchive(stream, ZipArchiveMode.Read, false, ZipEntryNameEncoding.Instance);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        /// <summary>항목의 앞부분(최대 <paramref name="maxBytes"/>)을 읽는다. 미리보기용.</summary>
        public static byte[] ReadEntryBytes(ZipListing listing, ZipEntryInfo entry, int maxBytes)
        {
            EnsureCurrent(listing);
            using (var archive = OpenArchive(listing.ArchivePath))
            {
                var zipEntry = GetArchiveEntry(archive, listing, entry);
                if (zipEntry.IsEncrypted)
                    throw new ZipArchiveException(ZipArchiveException.EncryptedMessage);
                using (var stream = zipEntry.Open())
                using (var buffer = new MemoryStream())
                {
                    var chunk = new byte[Math.Min(BufferSize, Math.Max(1, maxBytes))];
                    while (buffer.Length < maxBytes)
                    {
                        var read = stream.Read(chunk, 0, (int)Math.Min(chunk.Length, maxBytes - buffer.Length));
                        if (read <= 0)
                            break;
                        buffer.Write(chunk, 0, read);
                    }
                    return buffer.ToArray();
                }
            }
        }

        /// <summary>
        /// 고른 항목(폴더면 그 아래 전체)을 <paramref name="destination"/>에 풀 계획. 고른 항목은 대상 폴더 바로 아래에 놓인다
        /// (zip 안 위치와 무관 — 탐색기·TC에서 zip 안 폴더를 복사할 때와 같다). 대상 폴더 밖을 가리키는 경로는 만들지 않는다.
        /// </summary>
        public static ZipExtractPlan PlanExtraction(ZipListing listing, IEnumerable<ZipEntryInfo> selection, string destination, ZipExtractOptions options)
        {
            options = options ?? new ZipExtractOptions();
            var root = TrimSeparators(Path.GetFullPath(destination));
            var plan = new ZipExtractPlan { Destination = root, Options = options };
            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var tops = (selection ?? Enumerable.Empty<ZipEntryInfo>())
                .Where(item => item != null)
                .GroupBy(item => item.InnerPath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            // 다른 고른 폴더 안에 있는 항목은 그 폴더와 함께 풀린다.
            tops = tops.Where(item => !tops.Any(other => other.IsDirectory && other.InnerPath.Length > 0 && item != other &&
                                                         item.InnerPath.StartsWith(other.InnerPath + "/", StringComparison.OrdinalIgnoreCase)))
                       .ToList();

            foreach (var top in tops)
            {
                if (top.InnerPath.Length == 0)
                {
                    // zip 맨 위를 고르면 그 내용 전체를 대상 폴더에 바로 푼다.
                    foreach (var child in listing.GetChildren(string.Empty))
                        AddTop(plan, listing, child, root, options, reserved);
                    continue;
                }
                AddTop(plan, listing, top, root, options, reserved);
            }
            return plan;
        }

        private static void AddTop(ZipExtractPlan plan, ZipListing listing, ZipEntryInfo top, string root, ZipExtractOptions options, HashSet<string> reserved)
        {
            var target = Path.Combine(root, SanitizeSegment(top.Name));
            if (options.UniqueTopLevel)
                target = GetUniquePath(target, top.IsDirectory, reserved);
            reserved.Add(target);
            EnsureInside(root, target);
            Add(plan, top, target, true);

            if (!top.IsDirectory)
                return;
            foreach (var descendant in listing.EnumerateDescendants(top))
            {
                var relative = descendant.InnerPath.Substring(top.InnerPath.Length + 1);
                var path = Path.Combine(target, SanitizeRelative(relative));
                EnsureInside(root, path);
                Add(plan, descendant, path, false);
            }
        }

        private static void Add(ZipExtractPlan plan, ZipEntryInfo entry, string target, bool isTop)
        {
            plan.Items.Add(new ZipPlannedItem { Entry = entry, TargetPath = target, IsTopLevel = isTop });
            if (!entry.IsDirectory)
            {
                plan.FileCount++;
                plan.TotalBytes += entry.Size;
            }
        }

        /// <summary>계획대로 푼다. 항목별 실패는 결과의 오류 목록에 모으고 계속한다. 취소하면 그때까지 푼 것은 남기고 <c>Cancelled</c>.</summary>
        public static ZipExtractResult Extract(ZipListing listing, ZipExtractPlan plan, IProgress<ZipProgress> progress, CancellationToken cancellationToken)
        {
            var result = new ZipExtractResult();
            EnsureCurrent(listing);
            Directory.CreateDirectory(plan.Destination);
            var state = new ZipProgress { Total = plan.FileCount, BytesTotal = plan.TotalBytes };

            try
            {
                using (var archive = OpenArchive(listing.ArchivePath))
                {
                    if (archive.Entries.Count != listing.EntryCount)
                        throw new ZipArchiveException(ZipArchiveException.ChangedMessage);

                    foreach (var item in plan.Items)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            ExtractItem(archive, listing, plan, item, result, state, progress, cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (ZipArchiveException ex) when (ex.Message == ZipArchiveException.ChangedMessage)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            result.Errors.Add(item.Entry.InnerPath + ": " + Describe(ex));
                            if (result.Errors.Count >= MaxErrors)
                            {
                                result.Stopped = true;
                                break;
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                result.Cancelled = true;
            }
            return result;
        }

        private static void ExtractItem(ZipArchive archive, ZipListing listing, ZipExtractPlan plan, ZipPlannedItem item, ZipExtractResult result,
            ZipProgress state, IProgress<ZipProgress> progress, CancellationToken cancellationToken)
        {
            EnsureInside(plan.Destination, item.TargetPath);
            if (item.Entry.IsDirectory)
            {
                if (File.Exists(item.TargetPath))
                    throw new IOException("같은 이름의 파일이 있어 폴더를 만들 수 없습니다.");
                Directory.CreateDirectory(item.TargetPath);
                if (item.IsTopLevel)
                    result.TopLevelTargets.Add(item.TargetPath);
                return;
            }

            var target = item.TargetPath;
            var overwrite = false;
            if (File.Exists(target) || Directory.Exists(target))
            {
                switch (plan.Options.Conflict)
                {
                    case ZipConflictMode.Skip:
                        result.Skipped++;
                        state.Done++;
                        state.BytesDone += item.Entry.Size;
                        progress?.Report(Snapshot(state));
                        return;
                    case ZipConflictMode.Overwrite:
                        if (Directory.Exists(target))
                            throw new IOException("같은 이름의 폴더가 있어 파일을 덮어쓸 수 없습니다.");
                        overwrite = true;
                        break;
                    default:
                        target = GetUniquePath(target, false, null);
                        break;
                }
            }

            state.CurrentName = item.Entry.InnerPath;
            progress?.Report(Snapshot(state));
            var zipEntry = GetArchiveEntry(archive, listing, item.Entry);
            ExtractEntryToFile(zipEntry, item.Entry, target, overwrite, state, progress, cancellationToken);
            state.Done++;
            result.FilesWritten++;
            if (item.IsTopLevel)
                result.TopLevelTargets.Add(target);
        }

        /// <summary>
        /// 항목 하나를 파일로 푼다. 같은 폴더의 임시 이름(<c>.xxxx.folderss-part</c>)에 쓴 뒤 바꿔 취소·오류 때 반쪽 파일이 남지 않는다.
        /// </summary>
        internal static void ExtractEntryToFile(ZipArchiveEntry zipEntry, ZipEntryInfo info, string target, bool overwrite,
            ZipProgress state, IProgress<ZipProgress> progress, CancellationToken cancellationToken)
        {
            if (zipEntry.IsEncrypted)
                throw new ZipArchiveException(ZipArchiveException.EncryptedMessage);

            var directory = Path.GetDirectoryName(target);
            Directory.CreateDirectory(directory);
            var part = Path.Combine(directory, "." + Guid.NewGuid().ToString("N").Substring(0, 8) + PartSuffix);
            try
            {
                using (var source = zipEntry.Open())
                using (var output = new FileStream(part, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize))
                    Copy(source, output, state, progress, cancellationToken);

                try
                {
                    File.SetLastWriteTime(part, info.ModifiedAt);
                }
                catch (Exception)
                {
                    // 시각을 못 맞춰도 내용은 맞다.
                }
                if (overwrite && File.Exists(target))
                    ClearReadOnly(target);
                File.Move(part, target, overwrite);
            }
            catch
            {
                TryDeleteFile(part);
                throw;
            }
        }

        /// <summary>
        /// 원본(파일·폴더, 같은 폴더에 있어야 함)을 새 zip으로 만든다. 같은 폴더의 임시 파일에 쓴 뒤 이름을 바꿔 반쪽 zip이 남지 않는다.
        /// <paramref name="zipPath"/>가 이미 있으면 <c>이름 (2).zip</c>. 이름은 UTF-8(플래그 bit 11)로 저장한다(.NET 기본).
        /// 빈 폴더도 넣고, 하위의 링크(reparse point) 폴더는 안으로 들어가지 않는다(순환 방지). 만드는 zip 자신은 넣지 않는다.
        /// </summary>
        public static ZipCreateResult CreateZip(IReadOnlyList<string> sources, string zipPath, CompressionLevel level,
            IProgress<ZipProgress> progress, CancellationToken cancellationToken)
        {
            var result = new ZipCreateResult();
            var finalPath = Path.GetFullPath(zipPath);
            var directory = Path.GetDirectoryName(finalPath);
            var part = Path.Combine(directory, "." + Guid.NewGuid().ToString("N").Substring(0, 8) + PartSuffix);
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { finalPath, part };

            var items = new List<CreateItem>();
            foreach (var source in sources ?? new string[0])
            {
                var full = TrimSeparators(Path.GetFullPath(source));
                var name = Path.GetFileName(full);
                if (File.Exists(full))
                    items.Add(new CreateItem(full, name, false, new FileInfo(full).Length));
                else if (Directory.Exists(full))
                    CollectDirectory(full, name, items, excluded, result.Errors, true);
                else
                    result.Errors.Add(name + ": 찾을 수 없습니다.");
            }

            var state = new ZipProgress
            {
                Total = items.Count(item => !item.IsDirectory),
                BytesTotal = items.Where(item => !item.IsDirectory).Sum(item => item.Size)
            };

            try
            {
                using (var stream = new FileStream(part, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, BufferSize))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, false))
                {
                    foreach (var item in items)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (item.IsDirectory)
                        {
                            var folderEntry = archive.CreateEntry(item.EntryName.TrimEnd('/') + "/");
                            folderEntry.LastWriteTime = ClampZipTime(SafeLastWrite(item.FullPath, true));
                            result.FolderCount++;
                            continue;
                        }

                        state.CurrentName = item.EntryName;
                        progress?.Report(Snapshot(state));
                        try
                        {
                            // 원본을 먼저 열어(잠긴 파일이면 여기서 실패) 반쪽 항목이 생기지 않게 한다.
                            using (var input = new FileStream(item.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, BufferSize))
                            {
                                var entry = archive.CreateEntry(item.EntryName, level);
                                entry.LastWriteTime = ClampZipTime(SafeLastWrite(item.FullPath, false));
                                using (var output = entry.Open())
                                    Copy(input, output, state, progress, cancellationToken);
                            }
                            result.FileCount++;
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            result.Errors.Add(item.EntryName + ": " + Describe(ex));
                        }
                        state.Done++;
                    }
                }

                if (File.Exists(finalPath) || Directory.Exists(finalPath))
                    finalPath = GetUniquePath(finalPath, false, null);
                File.Move(part, finalPath);
                result.ZipPath = finalPath;
            }
            catch (OperationCanceledException)
            {
                TryDeleteFile(part);
                result.Cancelled = true;
            }
            catch
            {
                TryDeleteFile(part);
                throw;
            }
            return result;
        }

        private sealed class CreateItem
        {
            public CreateItem(string fullPath, string entryName, bool isDirectory, long size)
            {
                FullPath = fullPath;
                EntryName = entryName;
                IsDirectory = isDirectory;
                Size = size;
            }

            public string FullPath { get; }
            public string EntryName { get; }
            public bool IsDirectory { get; }
            public long Size { get; }
        }

        private static void CollectDirectory(string directory, string entryName, List<CreateItem> items, HashSet<string> excluded, List<string> errors, bool isSelected)
        {
            items.Add(new CreateItem(directory, entryName, true, 0));
            // 고른 폴더 자신이 링크면 따라 들어가지만, 그 아래의 링크 폴더는 빈 폴더로만 넣는다(순환 방지).
            if (!isSelected && (new DirectoryInfo(directory).Attributes & FileAttributes.ReparsePoint) != 0)
                return;

            try
            {
                foreach (var file in Directory.EnumerateFiles(directory).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    if (excluded.Contains(file))
                        continue;
                    long size = 0;
                    try
                    {
                        size = new FileInfo(file).Length;
                    }
                    catch (Exception)
                    {
                    }
                    items.Add(new CreateItem(file, entryName + "/" + Path.GetFileName(file), false, size));
                }
                foreach (var child in Directory.EnumerateDirectories(directory).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                    CollectDirectory(child, entryName + "/" + Path.GetFileName(child), items, excluded, errors, false);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
                errors.Add(entryName + ": " + Describe(ex));
            }
        }

        /// <summary>zip 안 이름 한 조각을 Windows 파일 이름으로 바꾼다(쓸 수 없는 글자·끝의 점/공백·장치 이름).</summary>
        public static string SanitizeSegment(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "_";

            var chars = name.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                var ch = chars[i];
                if (ch < 32 || ch == '<' || ch == '>' || ch == ':' || ch == '"' || ch == '|' || ch == '?' || ch == '*' || ch == '\\' || ch == '/')
                    chars[i] = '_';
            }
            // Windows는 끝의 점·공백을 조용히 떼어 다른 이름이 되거나 겹친다.
            for (var i = chars.Length - 1; i >= 0 && (chars[i] == '.' || chars[i] == ' '); i--)
                chars[i] = '_';

            var result = new string(chars);
            var dot = result.IndexOf('.');
            var stem = dot < 0 ? result : result.Substring(0, dot);
            if (ReservedDeviceNames.Contains(stem.TrimEnd(' ')))
                result = "_" + result;
            return result;
        }

        /// <summary>'/' 구분 상대 경로의 조각마다 <see cref="SanitizeSegment"/>를 적용해 Windows 경로로.</summary>
        public static string SanitizeRelative(string relative)
        {
            var parts = ZipArchivePath.NormalizeInnerPath(relative).Split('/').Where(part => part.Length > 0).Select(SanitizeSegment);
            return string.Join(Path.DirectorySeparatorChar.ToString(), parts);
        }

        /// <summary><paramref name="path"/>가 <paramref name="root"/> 안이 아니면 거부한다(zip Slip 이중 검사).</summary>
        public static void EnsureInside(string root, string path)
        {
            var fullRoot = Path.GetFullPath(root);
            if (!fullRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                fullRoot += Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                throw new ZipArchiveException("zip 항목이 압축을 풀 폴더 밖을 가리켜 거부했습니다: " + path);
        }

        /// <summary>같은 이름이 있으면 <c>이름 (2)</c>, <c>이름 (3)</c>…(파일은 확장자 앞). 기존 복사(<c>FileOperationService</c>)와 같은 규칙.</summary>
        public static string GetUniquePath(string requestedPath, bool isDirectory, ISet<string> reserved)
        {
            if (!Taken(requestedPath, reserved))
                return requestedPath;

            var parent = Path.GetDirectoryName(requestedPath) ?? string.Empty;
            var extension = isDirectory ? string.Empty : Path.GetExtension(requestedPath);
            var baseName = isDirectory ? Path.GetFileName(requestedPath) : Path.GetFileNameWithoutExtension(requestedPath);
            for (var index = 2; ; index++)
            {
                var candidate = Path.Combine(parent, string.Format("{0} ({1}){2}", baseName, index, extension));
                if (!Taken(candidate, reserved))
                    return candidate;
            }
        }

        private static bool Taken(string path, ISet<string> reserved)
        {
            return File.Exists(path) || Directory.Exists(path) || (reserved != null && reserved.Contains(path));
        }

        /// <summary>예외를 사용자에게 보일 짧은 문장으로.</summary>
        public static string Describe(Exception ex)
        {
            if (ex is ZipArchiveException)
                return ex.Message;
            if (ex is InvalidDataException)
                return "손상되었거나 지원하지 않는 압축 방식입니다 (" + ex.Message + ")";
            if (ex is UnauthorizedAccessException)
                return "권한이 없습니다 (" + ex.Message + ")";
            if (ex is PathTooLongException)
                return "경로가 너무 깁니다";
            return ex.Message;
        }

        internal static void EnsureCurrent(ZipListing listing)
        {
            if (listing == null || !listing.IsCurrent())
                throw new ZipArchiveException(ZipArchiveException.ChangedMessage);
        }

        internal static ZipArchiveEntry GetArchiveEntry(ZipArchive archive, ZipListing listing, ZipEntryInfo info)
        {
            var entries = archive.Entries;
            if (info.EntryIndex < 0 || info.EntryIndex >= entries.Count || entries.Count != listing.EntryCount)
                throw new ZipArchiveException(ZipArchiveException.ChangedMessage);
            var entry = entries[info.EntryIndex];
            if (!string.Equals(entry.FullName, info.EntryFullName, StringComparison.Ordinal))
                throw new ZipArchiveException(ZipArchiveException.ChangedMessage);
            return entry;
        }

        private static void Copy(Stream source, Stream output, ZipProgress state, IProgress<ZipProgress> progress, CancellationToken cancellationToken)
        {
            var buffer = new byte[BufferSize];
            long sinceReport = 0;
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                output.Write(buffer, 0, read);
                if (state == null)
                    continue;
                state.BytesDone += read;
                sinceReport += read;
                if (sinceReport >= 1024 * 1024)
                {
                    sinceReport = 0;
                    progress?.Report(Snapshot(state));
                }
            }
        }

        private static ZipProgress Snapshot(ZipProgress state)
        {
            return new ZipProgress
            {
                Done = state.Done,
                Total = state.Total,
                BytesDone = state.BytesDone,
                BytesTotal = state.BytesTotal,
                CurrentName = state.CurrentName
            };
        }

        private static DateTime SafeLastWrite(string path, bool directory)
        {
            try
            {
                return directory ? Directory.GetLastWriteTime(path) : File.GetLastWriteTime(path);
            }
            catch (Exception)
            {
                return DateTime.Now;
            }
        }

        // zip(DOS) 시각은 1980–2107년만 담을 수 있다. 밖이면 .NET이 예외를 던진다.
        private static DateTimeOffset ClampZipTime(DateTime time)
        {
            var min = new DateTime(1980, 1, 1, 0, 0, 0);
            var max = new DateTime(2107, 12, 31, 23, 59, 58);
            if (time < min)
                time = min;
            if (time > max)
                time = max;
            return new DateTimeOffset(time);
        }

        internal static void ClearReadOnly(string path)
        {
            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }
            catch (Exception)
            {
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception)
            {
            }
        }

        // 드라이브 루트(C:\)는 그대로 둔다 — "C:"가 되면 Path.Combine이 드라이브 상대 경로를 만든다.
        private static string TrimSeparators(string path)
        {
            var root = Path.GetPathRoot(path);
            return string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
                ? path
                : path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}

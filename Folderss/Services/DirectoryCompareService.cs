using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Folderss.Models;

namespace Folderss.Services
{
    /// <summary>
    /// 두 폴더를 상대 경로(대소문자 무시)로 짝지어 비교한다. 순수 System.IO라 UI 없이 테스트한다.
    /// 폴더 쌍의 상태는 자식 판정의 집계다: 차이가 하나라도 있으면 다름, 비교 불가만 있으면 비교 불가.
    /// 링크(reparse point)는 대상이 아니라 링크 자체(LinkTarget)를 비교하고, 순환을 막기 위해 안으로 들어가지 않는다.
    /// 한쪽에만 있는 폴더도 안으로 들어가지 않는다 — 그 폴더 하나가 차이이고, 복사하면 통째로 간다.
    /// </summary>
    public static class DirectoryCompareService
    {
        private const FileAttributes HiddenOrSystem = FileAttributes.Hidden | FileAttributes.System;

        /// <param name="progress">지금까지 읽은 폴더 수. 백그라운드 스레드에서 보고된다.</param>
        public static Task<CompareResult> CompareAsync(string leftRoot, string rightRoot, DirectoryCompareOptions options,
            IProgress<int> progress, CancellationToken token)
        {
            return Task.Run(() => Compare(leftRoot, rightRoot, options, progress, token));
        }

        /// <summary>취소되면 예외 대신 <see cref="CompareResult.IsComplete"/>가 false인 부분 결과를 돌려준다.</summary>
        public static CompareResult Compare(string leftRoot, string rightRoot, DirectoryCompareOptions options,
            IProgress<int> progress, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(leftRoot) || !Directory.Exists(leftRoot))
                throw new DirectoryNotFoundException("왼쪽 폴더가 없습니다: " + leftRoot);
            if (string.IsNullOrWhiteSpace(rightRoot) || !Directory.Exists(rightRoot))
                throw new DirectoryNotFoundException("오른쪽 폴더가 없습니다: " + rightRoot);

            var walker = new Walker(options ?? new DirectoryCompareOptions(), progress, token);
            var result = new CompareResult
            {
                LeftRoot = Path.GetFullPath(leftRoot),
                RightRoot = Path.GetFullPath(rightRoot),
                Options = walker.Options.Clone()
            };
            try
            {
                walker.CompareFolder(result.LeftRoot, result.RightRoot, string.Empty, 0, result.Entries, out _);
                result.IsComplete = true;
            }
            catch (OperationCanceledException)
            {
                result.IsComplete = false;
            }
            Count(result);
            return result;
        }

        private static void Count(CompareResult result)
        {
            foreach (var entry in result.Entries.Where(e => !e.IsAggregateFolder))
            {
                switch (entry.Status)
                {
                    case CompareStatus.Same: result.SameCount++; break;
                    case CompareStatus.Different: result.DifferentCount++; break;
                    case CompareStatus.LeftOnly: result.LeftOnlyCount++; break;
                    case CompareStatus.RightOnly: result.RightOnlyCount++; break;
                    case CompareStatus.TypeMismatch: result.TypeMismatchCount++; break;
                    default: result.UnknownCount++; break;
                }
            }
        }

        /// <summary>
        /// 한쪽 항목을 반대쪽 같은 상대 경로로 복사한다(파일은 덮어쓰기). 폴더는 반대쪽에 아직 없을 때만 통째로 복사한다 —
        /// 양쪽에 다 있는 폴더를 합치는 것은 동기화라 범위 밖이다. 없는 상위 폴더는 만든다.
        /// </summary>
        public static void CopyEntry(CompareResult result, CompareEntry entry, bool toRight)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (!CanCopy(entry, toRight, out var reason))
                throw new InvalidOperationException(reason);

            var source = Path.Combine(toRight ? result.LeftRoot : result.RightRoot, entry.RelativePath);
            var destination = Path.Combine(toRight ? result.RightRoot : result.LeftRoot, entry.RelativePath);
            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);

            if (entry.IsDirectory)
            {
                if (Directory.Exists(destination) || File.Exists(destination))
                    throw new IOException("대상에 이미 같은 이름이 있습니다: " + destination);
                CopyDirectory(source, destination);
            }
            else
            {
                if (Directory.Exists(destination))
                    throw new IOException("대상에 같은 이름의 폴더가 있습니다: " + destination);
                File.Copy(source, destination, overwrite: true);
            }
        }

        /// <summary>복사할 수 있는 항목인가. 안 되면 <paramref name="reason"/>에 이유.</summary>
        public static bool CanCopy(CompareEntry entry, bool toRight, out string reason)
        {
            reason = null;
            var sourceExists = toRight ? entry.LeftExists : entry.RightExists;
            if (!sourceExists)
                reason = (toRight ? "왼쪽" : "오른쪽") + "에 없는 항목입니다: " + entry.RelativePath;
            else if (entry.Status == CompareStatus.TypeMismatch)
                reason = "양쪽 유형(폴더/파일)이 달라 복사할 수 없습니다: " + entry.RelativePath;
            else if (entry.IsDirectory && entry.LeftExists && entry.RightExists)
                reason = "양쪽에 다 있는 폴더는 통째로 복사하지 않습니다. 안의 항목을 골라 복사하세요: " + entry.RelativePath;
            return reason == null;
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.EnumerateFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            foreach (var directory in Directory.EnumerateDirectories(source))
                CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }

        private sealed class Walker
        {
            public readonly DirectoryCompareOptions Options;
            private readonly IProgress<int> _progress;
            private readonly CancellationToken _token;
            private int _folders;

            public Walker(DirectoryCompareOptions options, IProgress<int> progress, CancellationToken token)
            {
                Options = options;
                _progress = progress;
                _token = token;
            }

            /// <summary>두 폴더의 직속 항목을 비교해 <paramref name="entries"/>에 넣고 자식 집계 상태를 돌려준다.</summary>
            public CompareStatus CompareFolder(string leftDir, string rightDir, string relative, int depth,
                List<CompareEntry> entries, out string error)
            {
                _token.ThrowIfCancellationRequested();
                _progress?.Report(++_folders);

                error = null;
                var left = List(leftDir, ref error);
                var right = List(rightDir, ref error);
                if (left == null || right == null)
                    return CompareStatus.Unknown;

                var leftRules = Options.ApplyIgnoreRules ? IgnoreRuleSet.LoadFor(leftDir) : null;
                var rightRules = Options.ApplyIgnoreRules ? IgnoreRuleSet.LoadFor(rightDir) : null;

                var names = left.Keys.Union(right.Keys, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(name => IsDirectory(left, name) || IsDirectory(right, name))
                    .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var aggregate = CompareStatus.Same;
                foreach (var name in names)
                {
                    _token.ThrowIfCancellationRequested();
                    left.TryGetValue(name, out var l);
                    right.TryGetValue(name, out var r);

                    string ignoreNote = null;
                    if (leftRules != null)
                    {
                        var leftIgnored = l != null && leftRules.IsIgnored(l.FullName, IsDir(l));
                        var rightIgnored = r != null && rightRules.IsIgnored(r.FullName, IsDir(r));
                        if ((l == null || leftIgnored) && (r == null || rightIgnored))
                            continue;
                        // 한쪽만 ignore — 규칙 파일 차이 자체가 찾고 싶은 차이일 수 있어 비교에 남긴다.
                        if (leftIgnored != rightIgnored)
                            ignoreNote = (leftIgnored ? "왼쪽" : "오른쪽") + "에서만 ignore 규칙에 걸림";
                    }

                    var entry = new CompareEntry
                    {
                        Name = (l ?? r).Name,
                        RelativePath = relative.Length == 0 ? (l ?? r).Name : relative + Path.DirectorySeparatorChar + (l ?? r).Name,
                        Depth = depth,
                        LeftExists = l != null,
                        RightExists = r != null,
                        IsDirectory = IsDir(l ?? r)
                    };
                    Fill(entry, l, true);
                    Fill(entry, r, false);
                    entries.Add(entry);

                    Judge(entry, l, r, depth, entries);
                    if (ignoreNote != null)
                        entry.Reason = entry.Reason == null ? ignoreNote : entry.Reason + " · " + ignoreNote;
                    aggregate = Merge(aggregate, entry.Status);
                }
                return aggregate;
            }

            private void Judge(CompareEntry entry, FileSystemInfo l, FileSystemInfo r, int depth, List<CompareEntry> entries)
            {
                if (l == null || r == null)
                {
                    entry.Status = l != null ? CompareStatus.LeftOnly : CompareStatus.RightOnly;
                    return;
                }
                if (IsDir(l) != IsDir(r))
                {
                    entry.Status = CompareStatus.TypeMismatch;
                    entry.Reason = IsDir(l) ? "왼쪽은 폴더, 오른쪽은 파일" : "왼쪽은 파일, 오른쪽은 폴더";
                    return;
                }

                var leftLink = IsLink(l);
                var rightLink = IsLink(r);
                if (leftLink || rightLink)
                {
                    if (leftLink != rightLink)
                    {
                        entry.Status = CompareStatus.TypeMismatch;
                        entry.Reason = (leftLink ? "왼쪽" : "오른쪽") + "만 링크";
                    }
                    else if (string.Equals(l.LinkTarget, r.LinkTarget, StringComparison.OrdinalIgnoreCase))
                    {
                        entry.Status = CompareStatus.Same;
                        entry.Reason = "링크 대상 같음";
                    }
                    else
                    {
                        entry.Status = CompareStatus.Different;
                        entry.Reason = "링크 대상 다름: " + l.LinkTarget + " ≠ " + r.LinkTarget;
                    }
                    return;
                }

                if (IsDir(l))
                {
                    if (!Options.Recursive || (Options.MaxDepth > 0 && depth + 1 >= Options.MaxDepth))
                    {
                        // 들여다보지 않았으니 같다고 할 수 없다. 양쪽에 있는 폴더라 집계 개수에는 들어가지 않는다.
                        entry.Status = CompareStatus.Unknown;
                        entry.Reason = "하위 폴더는 비교하지 않음(옵션)";
                        return;
                    }
                    // 취소로 중간에 끝나면 이 값이 남는다.
                    entry.Status = CompareStatus.Unknown;
                    entry.Reason = "비교 중단됨";
                    entry.Status = CompareFolder(l.FullName, r.FullName, entry.RelativePath, depth + 1, entries, out var error);
                    entry.Reason = error ?? (entry.Status == CompareStatus.Different ? "하위에 차이 있음" : null);
                    return;
                }

                CompareFiles(entry, (FileInfo)l, (FileInfo)r);
            }

            private void CompareFiles(CompareEntry entry, FileInfo l, FileInfo r)
            {
                if (l.Length != r.Length)
                {
                    entry.Status = CompareStatus.Different;
                    entry.Reason = string.Format("크기 {0:N0} ≠ {1:N0}", l.Length, r.Length);
                    return;
                }
                if (Options.UseContentHash)
                {
                    try
                    {
                        var same = Hash(l.FullName).SequenceEqual(Hash(r.FullName));
                        entry.Status = same ? CompareStatus.Same : CompareStatus.Different;
                        entry.Reason = same ? null : "내용(해시) 다름";
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        entry.Status = CompareStatus.Unknown;
                        entry.Reason = ex.Message;
                    }
                    return;
                }
                var gap = (l.LastWriteTimeUtc - r.LastWriteTimeUtc).Duration();
                if (gap > Options.TimestampTolerance)
                {
                    entry.Status = CompareStatus.Different;
                    entry.Reason = "수정 시각 다름 (" + (l.LastWriteTimeUtc > r.LastWriteTimeUtc ? "왼쪽이 최신" : "오른쪽이 최신") + ")";
                    return;
                }
                entry.Status = CompareStatus.Same;
            }

            private byte[] Hash(string path)
            {
                _token.ThrowIfCancellationRequested();
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16))
                using (var sha = SHA256.Create())
                    return sha.ComputeHash(stream);
            }

            private Dictionary<string, FileSystemInfo> List(string directory, ref string error)
            {
                try
                {
                    var map = new Dictionary<string, FileSystemInfo>(StringComparer.OrdinalIgnoreCase);
                    foreach (var info in new DirectoryInfo(directory).EnumerateFileSystemInfos())
                    {
                        if (!Options.IncludeHidden && (info.Attributes & HiddenOrSystem) != 0)
                            continue;
                        map[info.Name] = info;
                    }
                    return map;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
                {
                    error = ex.Message;
                    return null;
                }
            }

            private static void Fill(CompareEntry entry, FileSystemInfo info, bool left)
            {
                if (info == null)
                    return;
                long? size = info is FileInfo file ? file.Length : (long?)null;
                if (left)
                {
                    entry.LeftSize = size;
                    entry.LeftModified = info.LastWriteTime;
                }
                else
                {
                    entry.RightSize = size;
                    entry.RightModified = info.LastWriteTime;
                }
            }

            private static CompareStatus Merge(CompareStatus aggregate, CompareStatus child)
            {
                if (aggregate == CompareStatus.Different || child == CompareStatus.Same)
                    return aggregate;
                return child == CompareStatus.Unknown ? CompareStatus.Unknown : CompareStatus.Different;
            }

            private static bool IsDirectory(Dictionary<string, FileSystemInfo> map, string name)
                => map.TryGetValue(name, out var info) && IsDir(info);

            private static bool IsDir(FileSystemInfo info) => info is DirectoryInfo;

            private static bool IsLink(FileSystemInfo info) => (info.Attributes & FileAttributes.ReparsePoint) != 0;
        }
    }
}

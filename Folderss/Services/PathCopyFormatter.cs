using System;
using System.Collections.Generic;
using System.Linq;

namespace Folderss.Services
{
    /// <summary>경로 복사 형식. 저장 파일(path-copy-settings.xml)에 이름으로 쓰므로 이름을 바꾸지 않는다.</summary>
    public enum PathCopyFormat
    {
        /// <summary><c>C:\work\a.txt</c></summary>
        FullPath,
        /// <summary><c>"C:\work\a.txt"</c> — 탐색기 "경로로 복사"와 같이 공백이 없어도 감싼다.</summary>
        QuotedFullPath,
        /// <summary><c>a.txt</c></summary>
        Name,
        /// <summary><c>C:\work</c></summary>
        ParentFolder,
        /// <summary>항목을 담은 Git 저장소 루트 기준 상대 경로.</summary>
        RelativeToRepository,
        /// <summary>반대편 폴더 패널의 현재 폴더 기준 상대 경로.</summary>
        RelativeToOppositePanel,
        /// <summary>매핑된 네트워크 드라이브 경로를 <c>\\server\share\…</c>로.</summary>
        UncPath
    }

    /// <summary>경로 구분자. VS Code의 <c>explorer.copyPathSeparator</c>·<c>copyRelativePathSeparator</c>를 한 값으로 합쳤다.</summary>
    public enum PathCopySeparator
    {
        /// <summary>Windows 기본 <c>\</c>.</summary>
        Backslash,
        /// <summary>상대 경로만 <c>/</c>(나머지는 <c>\</c>).</summary>
        SlashForRelative,
        /// <summary>모든 경로 형식에 <c>/</c>. 이름 형식은 영향 없음.</summary>
        SlashForAll
    }

    /// <summary>형식에 필요한 주변 정보. 파일 시스템·Win32 호출은 함수로 받아 테스트에서 바꿀 수 있게 한다.</summary>
    public sealed class PathCopyContext
    {
        /// <summary>폴더 → 그 폴더(자신 포함)부터 위로 찾은 Git 저장소 루트, 없으면 null.</summary>
        public Func<string, string> FindRepositoryRoot { get; set; }

        /// <summary>반대편 폴더 패널의 현재 폴더. 없으면 null.</summary>
        public string OppositeFolder { get; set; }

        /// <summary>전체 경로 → UNC 경로, 바꿀 수 없으면(로컬 드라이브·실패) null.</summary>
        public Func<string, string> ResolveUnc { get; set; }

        public PathCopySeparator Separator { get; set; }
    }

    public sealed class PathCopyResult
    {
        public PathCopyFormat Format { get; set; }

        /// <summary>클립보드에 넣을 글자(줄마다 하나, CRLF, 끝 줄바꿈 없음).</summary>
        public string Text { get; set; }

        /// <summary>복사한 줄 수(같은 줄은 한 번).</summary>
        public int Count { get; set; }

        /// <summary>요청한 형식을 쓸 수 없어 전체 경로로 대신한 항목 수.</summary>
        public int FallbackCount { get; set; }
    }

    /// <summary>
    /// 경로 복사 형식 만들기(순수 로직). Windows 경로 규칙(드라이브 루트 <c>C:\</c>, UNC 루트 <c>\\server\share</c>,
    /// 대소문자 무시)을 직접 처리해 System.IO.Path의 OS별 차이 없이 어디서나 같은 결과를 낸다.
    /// </summary>
    public static class PathCopyFormatter
    {
        public const string LineSeparator = "\r\n";

        /// <summary>메뉴·설정 창·상태 표시에 쓰는 형식 이름.</summary>
        public static string DisplayName(PathCopyFormat format)
        {
            switch (format)
            {
                case PathCopyFormat.FullPath: return "전체 경로";
                case PathCopyFormat.QuotedFullPath: return "전체 경로 (따옴표)";
                case PathCopyFormat.Name: return "이름";
                case PathCopyFormat.ParentFolder: return "상위 폴더 경로";
                case PathCopyFormat.RelativeToRepository: return "상대 경로 (저장소 기준)";
                case PathCopyFormat.RelativeToOppositePanel: return "상대 경로 (반대편 패널 기준)";
                case PathCopyFormat.UncPath: return "UNC 경로";
                default: return format.ToString();
            }
        }

        /// <summary>메뉴에 보이는 순서.</summary>
        public static IReadOnlyList<PathCopyFormat> MenuOrder { get; } = new[]
        {
            PathCopyFormat.FullPath,
            PathCopyFormat.QuotedFullPath,
            PathCopyFormat.Name,
            PathCopyFormat.ParentFolder,
            PathCopyFormat.RelativeToRepository,
            PathCopyFormat.RelativeToOppositePanel,
            PathCopyFormat.UncPath
        };

        /// <summary>
        /// 경로들을 한 형식으로 바꿔 줄마다 하나로 잇는다. 입력 순서를 지키고 같은 경로·같은 결과 줄은 한 번만 넣는다.
        /// 형식을 쓸 수 없는 항목은 전체 경로로 대신하고 <see cref="PathCopyResult.FallbackCount"/>에 센다.
        /// </summary>
        public static PathCopyResult Format(IEnumerable<string> paths, PathCopyFormat format, PathCopyContext context)
        {
            context = context ?? new PathCopyContext();
            var repositoryCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenLines = new HashSet<string>(StringComparer.Ordinal);
            var lines = new List<string>();
            var fallbacks = 0;

            foreach (var raw in paths ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                var path = TrimTrailingSeparators(raw);
                if (!seenPaths.Add(path))
                    continue;

                var line = FormatOne(path, format, context, repositoryCache, out var fallback);
                if (fallback)
                    fallbacks++;
                if (seenLines.Add(line))
                    lines.Add(line);
            }

            return new PathCopyResult
            {
                Format = format,
                Text = string.Join(LineSeparator, lines),
                Count = lines.Count,
                FallbackCount = fallbacks
            };
        }

        /// <summary>경로 하나를 바꾼다. 쓸 수 없으면 전체 경로(구분자 설정 적용)와 <paramref name="fallback"/> = true.</summary>
        public static string FormatOne(string path, PathCopyFormat format, PathCopyContext context, out bool fallback)
        {
            return FormatOne(TrimTrailingSeparators(path), format, context ?? new PathCopyContext(), null, out fallback);
        }

        private static string FormatOne(string path, PathCopyFormat format, PathCopyContext context,
            Dictionary<string, string> repositoryCache, out bool fallback)
        {
            fallback = false;
            var separator = context.Separator;
            switch (format)
            {
                case PathCopyFormat.QuotedFullPath:
                    return "\"" + ApplySeparator(path, separator == PathCopySeparator.SlashForAll) + "\"";

                case PathCopyFormat.Name:
                    var name = GetName(path);
                    // 드라이브 루트(C:\)·공유 루트(\\server\share)는 이름이 없다 — 대신이 아니라 그 자체가 답이다.
                    return name ?? ApplySeparator(path, separator == PathCopySeparator.SlashForAll);

                case PathCopyFormat.ParentFolder:
                    var parent = GetParent(path);
                    if (parent != null)
                        return ApplySeparator(parent, separator == PathCopySeparator.SlashForAll);
                    break;

                case PathCopyFormat.RelativeToRepository:
                    var root = FindRepositoryRootCached(GetParent(path), context, repositoryCache);
                    var inRepository = root == null ? null : GetRelativePath(root, path);
                    if (inRepository != null && inRepository != ".." && !inRepository.StartsWith(@"..\", StringComparison.Ordinal))
                        return ApplySeparator(inRepository, separator != PathCopySeparator.Backslash);
                    break;

                case PathCopyFormat.RelativeToOppositePanel:
                    var relative = string.IsNullOrWhiteSpace(context.OppositeFolder)
                        ? null
                        : GetRelativePath(TrimTrailingSeparators(context.OppositeFolder), path);
                    if (relative != null)
                        return ApplySeparator(relative, separator != PathCopySeparator.Backslash);
                    break;

                case PathCopyFormat.UncPath:
                    // 이미 네트워크 경로(\\server\share\…)로 연 폴더면 전체 경로가 곧 UNC 경로다.
                    if (IsUncPath(path))
                        return ApplySeparator(path, separator == PathCopySeparator.SlashForAll);
                    var unc = SafeResolve(context.ResolveUnc, path);
                    if (!string.IsNullOrEmpty(unc))
                        return ApplySeparator(TrimTrailingSeparators(unc), separator == PathCopySeparator.SlashForAll);
                    break;

                case PathCopyFormat.FullPath:
                    return ApplySeparator(path, separator == PathCopySeparator.SlashForAll);
            }

            fallback = true;
            return ApplySeparator(path, separator == PathCopySeparator.SlashForAll);
        }

        /// <summary>
        /// 메뉴에 이 형식을 보일지. 항상 쓸 수 있는 형식은 true, 조건부 형식은 경로 중 하나라도 그 형식으로 바뀔 때만 true
        /// (같은 패널의 선택은 모두 같은 폴더라 사실상 전부 되거나 전부 안 된다).
        /// </summary>
        public static bool IsAvailable(PathCopyFormat format, IReadOnlyList<string> paths, PathCopyContext context)
        {
            if (paths == null || paths.Count == 0)
                return false;
            switch (format)
            {
                case PathCopyFormat.RelativeToRepository:
                case PathCopyFormat.RelativeToOppositePanel:
                case PathCopyFormat.UncPath:
                    var cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    return paths.Where(p => !string.IsNullOrWhiteSpace(p)).Any(p =>
                    {
                        var path = TrimTrailingSeparators(p);
                        // UNC 항목은 바꿀 것이 있을 때(매핑된 드라이브)만 보인다 — 이미 UNC면 전체 경로와 같다.
                        if (format == PathCopyFormat.UncPath && IsUncPath(path))
                            return false;
                        FormatOne(path, format, context ?? new PathCopyContext(), cache, out var fallback);
                        return !fallback;
                    });
                default:
                    return true;
            }
        }

        /// <summary>저장소 기준 메뉴 이름에 붙일 저장소 이름(루트 폴더 이름). 저장소 밖이면 null.</summary>
        public static string RepositoryName(IReadOnlyList<string> paths, PathCopyContext context)
        {
            var first = paths?.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
            if (first == null || context == null)
                return null;
            var root = FindRepositoryRootCached(GetParent(first), context, null);
            return root == null ? null : (GetName(root) ?? root);
        }

        /// <summary>패널 상태 줄에 보일 문구. 예: "전체 경로 3개 복사함 · 1개는 전체 경로로 대신".</summary>
        public static string StatusMessage(PathCopyResult result)
        {
            if (result == null || result.Count == 0)
                return "복사할 경로가 없습니다.";
            var message = string.Format("{0} {1}개 복사함", DisplayName(result.Format), result.Count);
            if (result.FallbackCount > 0)
                message += string.Format(" · {0}개는 전체 경로로 대신", result.FallbackCount);
            return message;
        }

        /// <summary>
        /// 폴더(자신 포함)부터 루트까지 올라가며 <paramref name="hasGitMarker"/>(그 폴더에 <c>.git</c> 폴더·파일이 있나)가 참인 첫 폴더.
        /// </summary>
        public static string FindRepositoryRoot(string folder, Func<string, bool> hasGitMarker)
        {
            var current = string.IsNullOrWhiteSpace(folder) ? null : TrimTrailingSeparators(folder);
            while (current != null)
            {
                if (hasGitMarker(current))
                    return current;
                current = GetParent(current);
            }
            return null;
        }

        /// <summary>
        /// <c>WNetGetConnection</c>이 준 드라이브의 원격 이름(<c>\\server\share</c>)과 드라이브 문자 경로를 합친다.
        /// <c>Z:\docs\a.txt</c> + <c>\\server\share</c> → <c>\\server\share\docs\a.txt</c>. 드라이브 문자 경로가 아니면 null.
        /// </summary>
        public static string CombineUnc(string remoteName, string path)
        {
            if (string.IsNullOrWhiteSpace(remoteName) || !TryGetDriveName(path, out _))
                return null;
            var remote = remoteName.Trim().TrimEnd('\\', '/');
            var rest = path.Length > 2 ? path.Substring(2).TrimStart('\\', '/') : string.Empty;
            return rest.Length == 0 ? remote : remote + "\\" + rest;
        }

        /// <summary><c>C:\…</c>·<c>C:</c> 형태면 드라이브 이름(<c>C:</c>).</summary>
        public static bool TryGetDriveName(string path, out string drive)
        {
            drive = null;
            if (string.IsNullOrEmpty(path) || path.Length < 2 || path[1] != ':' || !IsAsciiLetter(path[0]))
                return false;
            if (path.Length > 2 && !IsSeparator(path[2]))
                return false;
            drive = char.ToUpperInvariant(path[0]) + ":";
            return true;
        }

        /// <summary><c>\\server\share\…</c>(또는 <c>\\?\UNC\…</c>). <c>\\?\C:\…</c> 같은 긴 경로 접두사의 로컬 경로는 아니다.</summary>
        public static bool IsUncPath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length <= 2 || !IsSeparator(path[0]) || !IsSeparator(path[1]))
                return false;
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) || path.StartsWith(@"\\.\UNC\", StringComparison.OrdinalIgnoreCase))
                return true;
            return !(path.Length > 3 && (path[2] == '?' || path[2] == '.') && IsSeparator(path[3]));
        }

        // ── Windows 경로 도우미 ─────────────────────────────────────────────

        /// <summary>
        /// 루트 길이. <c>C:\</c> = 3, <c>C:</c> = 2, <c>\\server\share</c> = 공유 이름 끝까지, <c>\\?\C:\</c> = 7,
        /// <c>\\?\UNC\server\share</c> = 공유 이름 끝까지, 상대 경로 = 0.
        /// </summary>
        public static int GetRootLength(string path)
        {
            if (string.IsNullOrEmpty(path))
                return 0;

            var offset = 0;
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) || path.StartsWith(@"\\.\UNC\", StringComparison.OrdinalIgnoreCase))
                return EndOfUncRoot(path, 8);
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
                offset = 4;
            else if (IsUncPath(path))
                return EndOfUncRoot(path, 2);

            if (path.Length >= offset + 2 && path[offset + 1] == ':' && IsAsciiLetter(path[offset]))
                return path.Length >= offset + 3 && IsSeparator(path[offset + 2]) ? offset + 3 : offset + 2;
            return offset;
        }

        /// <summary>끝의 <c>\</c>·<c>/</c>를 뗀다. 루트(<c>C:\</c>)는 그대로, <c>\\server\share\</c>는 <c>\\server\share</c>.</summary>
        public static string TrimTrailingSeparators(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;
            var rootLength = GetRootLength(path);
            var end = path.Length;
            while (end > rootLength && IsSeparator(path[end - 1]))
                end--;
            // UNC 루트 길이는 공유 이름 끝(구분자 제외)이라 위에서 이미 떼어진다.
            return end == path.Length ? path : path.Substring(0, end);
        }

        /// <summary>마지막 경로 조각. 루트(드라이브·공유)거나 비었으면 null.</summary>
        public static string GetName(string path)
        {
            path = TrimTrailingSeparators(path);
            if (string.IsNullOrEmpty(path))
                return null;
            var rootLength = GetRootLength(path);
            if (path.Length <= rootLength)
                return null;
            var index = LastSeparatorIndex(path);
            var name = index < rootLength ? path.Substring(rootLength) : path.Substring(index + 1);
            return name.Length == 0 ? null : name;
        }

        /// <summary>상위 폴더. <c>C:\a</c> → <c>C:\</c>, <c>\\s\sh\a</c> → <c>\\s\sh</c>, 루트·상대 이름 하나면 null.</summary>
        public static string GetParent(string path)
        {
            path = TrimTrailingSeparators(path);
            if (string.IsNullOrEmpty(path))
                return null;
            var rootLength = GetRootLength(path);
            if (path.Length <= rootLength)
                return null;
            var index = LastSeparatorIndex(path);
            if (index < rootLength)
                return rootLength == 0 ? null : TrimTrailingSeparators(path.Substring(0, rootLength));
            var parent = path.Substring(0, index);
            if (parent.Length == 0)
                return null;
            return parent.Length < rootLength ? path.Substring(0, rootLength) : TrimTrailingSeparators(parent);
        }

        /// <summary>
        /// <paramref name="basePath"/> 폴더에서 <paramref name="path"/>까지의 상대 경로(<c>\</c> 구분). 같으면 <c>.</c>.
        /// 루트(드라이브·공유)가 다르거나 둘 중 하나가 상대 경로면 null. 대소문자는 무시한다.
        /// </summary>
        public static string GetRelativePath(string basePath, string path)
        {
            if (string.IsNullOrWhiteSpace(basePath) || string.IsNullOrWhiteSpace(path))
                return null;
            basePath = TrimTrailingSeparators(basePath);
            path = TrimTrailingSeparators(path);
            var baseRootLength = GetRootLength(basePath);
            var rootLength = GetRootLength(path);
            if (baseRootLength == 0 || rootLength == 0)
                return null;
            var baseRoot = NormalizeRoot(basePath.Substring(0, baseRootLength));
            var root = NormalizeRoot(path.Substring(0, rootLength));
            if (!string.Equals(baseRoot, root, StringComparison.OrdinalIgnoreCase))
                return null;

            var baseParts = SplitParts(basePath.Substring(baseRootLength));
            var parts = SplitParts(path.Substring(rootLength));
            var common = 0;
            while (common < baseParts.Count && common < parts.Count &&
                   string.Equals(baseParts[common], parts[common], StringComparison.OrdinalIgnoreCase))
                common++;

            var result = new List<string>();
            for (var i = common; i < baseParts.Count; i++)
                result.Add("..");
            for (var i = common; i < parts.Count; i++)
                result.Add(parts[i]);
            return result.Count == 0 ? "." : string.Join("\\", result);
        }

        /// <summary><c>\</c>를 <c>/</c>로(켜졌을 때만).</summary>
        public static string ApplySeparator(string path, bool slash)
        {
            return slash && path != null ? path.Replace('\\', '/') : path;
        }

        private static string FindRepositoryRootCached(string folder, PathCopyContext context, Dictionary<string, string> cache)
        {
            if (folder == null || context.FindRepositoryRoot == null)
                return null;
            if (cache != null && cache.TryGetValue(folder, out var cached))
                return cached;
            string root;
            try
            {
                root = context.FindRepositoryRoot(folder);
            }
            catch (Exception)
            {
                // 권한 없는 상위 폴더 등 — 저장소를 못 찾은 것으로 본다(대체 규칙으로 전체 경로).
                root = null;
            }
            if (cache != null)
                cache[folder] = root;
            return root;
        }

        private static string SafeResolve(Func<string, string> resolve, string path)
        {
            if (resolve == null)
                return null;
            try
            {
                return resolve(path);
            }
            catch (Exception)
            {
                // 네트워크 API 실패는 변환 실패로 본다(대체 규칙으로 전체 경로).
                return null;
            }
        }

        private static string NormalizeRoot(string root)
        {
            return TrimTrailingSeparators(root).Replace('/', '\\');
        }

        private static List<string> SplitParts(string rest)
        {
            return rest.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        private static int EndOfUncRoot(string path, int start)
        {
            // \\server\share — 서버 이름 다음 구분자, 그 다음 공유 이름 끝까지.
            var serverEnd = IndexOfSeparator(path, start);
            if (serverEnd < 0)
                return path.Length;
            var shareEnd = IndexOfSeparator(path, serverEnd + 1);
            return shareEnd < 0 ? path.Length : shareEnd;
        }

        private static int IndexOfSeparator(string path, int start)
        {
            for (var i = start; i < path.Length; i++)
                if (IsSeparator(path[i]))
                    return i;
            return -1;
        }

        private static int LastSeparatorIndex(string path)
        {
            for (var i = path.Length - 1; i >= 0; i--)
                if (IsSeparator(path[i]))
                    return i;
            return -1;
        }

        private static bool IsSeparator(char c) => c == '\\' || c == '/';

        private static bool IsAsciiLetter(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
    }
}

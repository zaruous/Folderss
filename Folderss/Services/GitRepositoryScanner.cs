using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Folderss.Models;

namespace Folderss.Services
{
    /// <summary>
    /// 기준 폴더 아래(와 위)에서 Git 저장소를 찾는다. 순수 System.IO라 WPF 없이 테스트한다.
    /// - <c>.git</c> 폴더 또는 <c>.git</c> 파일(워크트리·서브모듈)이 있으면 저장소 루트로 본다 (<see cref="IgnoreRuleSet"/>과 같은 판정).
    /// - 저장소를 찾아도 안으로 계속 내려가 중첩 저장소도 찾는다. <c>.git</c> 폴더 자체와 제외 폴더에는 들어가지 않는다.
    /// - reparse point(심볼릭 링크·junction)는 따라가지 않는다(순환 방지). 접근 거부 폴더는 건너뛰고 개수만 센다.
    /// </summary>
    public static class GitRepositoryScanner
    {
        public sealed class ScanResult
        {
            public List<GitRepositoryInfo> Repositories { get; } = new List<GitRepositoryInfo>();
            public int InaccessibleCount { get; set; }
        }

        /// <summary>
        /// <paramref name="basePath"/>를 품은 상위 저장소(있으면 맨 앞)와 하위 저장소를 찾는다.
        /// <paramref name="maxDepth"/>는 기준 폴더를 0으로 센 깊이다. 찾는 즉시 <paramref name="found"/>를 부른다.
        /// </summary>
        public static ScanResult Scan(string basePath, int maxDepth, IEnumerable<string> excludedNames,
            Action<GitRepositoryInfo> found = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(basePath))
                throw new ArgumentException("A base path is required.", nameof(basePath));

            var root = Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (root.Length == 0 || root.EndsWith(":"))
                root += Path.DirectorySeparatorChar;

            var excluded = new HashSet<string>(excludedNames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase) { ".git" };
            var result = new ScanResult();

            void Add(GitRepositoryInfo info)
            {
                result.Repositories.Add(info);
                found?.Invoke(info);
            }

            var ancestor = FindAncestorRepository(root);
            if (ancestor != null)
            {
                Add(new GitRepositoryInfo
                {
                    RootPath = ancestor,
                    DisplayPath = ancestor,
                    IsAncestor = true,
                    IsGitFile = File.Exists(Path.Combine(ancestor, ".git"))
                });
            }

            var stack = new Stack<(string Path, int Depth)>();
            stack.Push((root, 0));
            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (directory, depth) = stack.Pop();

                var gitPath = Path.Combine(directory, ".git");
                var isGitDirectory = Directory.Exists(gitPath);
                var isGitFile = !isGitDirectory && File.Exists(gitPath);
                if (isGitDirectory || isGitFile)
                {
                    Add(new GitRepositoryInfo
                    {
                        RootPath = directory,
                        DisplayPath = GetRelativeDisplay(root, directory),
                        IsGitFile = isGitFile
                    });
                }

                if (depth >= maxDepth)
                    continue;

                string[] children;
                try
                {
                    children = Directory.GetDirectories(directory);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is System.Security.SecurityException)
                {
                    result.InaccessibleCount++;
                    continue;
                }

                // 스택이라 역순으로 넣어야 이름순으로 나온다.
                Array.Sort(children, StringComparer.OrdinalIgnoreCase);
                for (var i = children.Length - 1; i >= 0; i--)
                {
                    var child = children[i];
                    if (excluded.Contains(Path.GetFileName(child)) || IsReparsePoint(child))
                        continue;
                    stack.Push((child, depth + 1));
                }
            }

            return result;
        }

        /// <summary>기준 폴더 자신은 빼고, 부모부터 드라이브 루트까지 올라가며 첫 저장소 루트를 찾는다.</summary>
        public static string FindAncestorRepository(string basePath)
        {
            var current = Path.GetDirectoryName(Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            while (!string.IsNullOrEmpty(current))
            {
                var gitPath = Path.Combine(current, ".git");
                if (Directory.Exists(gitPath) || File.Exists(gitPath))
                    return current;
                current = Path.GetDirectoryName(current);
            }
            return null;
        }

        private static string GetRelativeDisplay(string root, string directory)
        {
            var relative = Path.GetRelativePath(root, directory);
            return string.IsNullOrEmpty(relative) ? "." : relative;
        }

        private static bool IsReparsePoint(string directory)
        {
            try
            {
                return (new DirectoryInfo(directory).Attributes & FileAttributes.ReparsePoint) != 0;
            }
            catch
            {
                // 속성을 못 읽으면 안전하게 따라가지 않는다.
                return true;
            }
        }
    }
}

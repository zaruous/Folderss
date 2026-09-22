using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Folderss.Services
{
    /// <summary>
    /// .gitignore / .folderssignore 규칙으로 폴더 목록에서 숨길 항목을 판정한다. gitignore 문법의 부분집합을 지원한다:
    /// 주석(#)·빈 줄, 부정(!), 디렉터리 전용(끝의 /), 앵커(/로 시작하거나 중간에 /가 있는 패턴은 규칙 파일 위치 기준),
    /// 와일드카드(*, ?, [..], **). Windows 기본값(core.ignorecase=true)에 맞춰 대소문자를 무시한다.
    /// 판정은 항목 자신만 본다 — 상위 폴더가 무시 대상이어도 그 안으로 들어가 보고 있는 목록은 숨기지 않는다.
    /// 사용자가 일부러 들어간 폴더가 텅 비어 보이는 것보다 규칙에 직접 걸리는 항목만 숨기는 쪽이 예측 가능하다.
    /// </summary>
    public sealed class IgnoreRuleSet
    {
        public const string GitIgnoreFileName = ".gitignore";
        public const string FolderssIgnoreFileName = ".folderssignore";

        private readonly List<IgnoreRule> _rules = new List<IgnoreRule>();
        private readonly List<string> _sourceFiles = new List<string>();

        /// <summary>규칙을 하나 이상 제공한 규칙 파일 경로. 루트에 가까운 것이 먼저다.</summary>
        public IReadOnlyList<string> SourceFiles { get { return _sourceFiles; } }
        public int RuleCount { get { return _rules.Count; } }
        public bool IsEmpty { get { return _rules.Count == 0; } }

        private IgnoreRuleSet() { }

        /// <summary>규칙 텍스트에서 직접 만든다 (테스트·프로그램 내장 규칙용). 패턴은 <paramref name="baseDirectory"/> 기준이다.</summary>
        public static IgnoreRuleSet FromText(string baseDirectory, string text)
        {
            var set = new IgnoreRuleSet();
            set.AddRules(baseDirectory, text, null);
            return set;
        }

        /// <summary>
        /// <paramref name="directory"/>의 목록에 적용할 규칙을 모은다.
        /// .gitignore는 가장 가까운 상위 .git(저장소 루트)이 있을 때만, 루트부터 현재 폴더까지 순서대로 읽는다 —
        /// git과 같이 깊은 폴더의 규칙이 나중에 와서 우선한다. 저장소 안에서는 .git 폴더 자체도 숨긴다.
        /// .folderssignore는 저장소와 무관하게 드라이브 루트부터 현재 폴더까지 읽는다.
        /// 규칙 파일을 읽을 수 없으면 그 파일만 건너뛴다.
        /// </summary>
        public static IgnoreRuleSet LoadFor(string directory)
        {
            var set = new IgnoreRuleSet();
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return set;

            var chain = new List<string>();
            var current = NormalizeDirectory(directory);
            while (!string.IsNullOrEmpty(current))
            {
                chain.Add(current);
                var parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                    break;
                current = NormalizeDirectory(parent);
            }
            chain.Reverse();

            var repoRootIndex = -1;
            for (var i = chain.Count - 1; i >= 0; i--)
            {
                var gitPath = Path.Combine(chain[i], ".git");
                if (Directory.Exists(gitPath) || File.Exists(gitPath))
                {
                    repoRootIndex = i;
                    break;
                }
            }

            if (repoRootIndex >= 0)
                set.AddRules(chain[repoRootIndex], ".git/", null);

            for (var i = 0; i < chain.Count; i++)
            {
                if (repoRootIndex >= 0 && i >= repoRootIndex)
                    set.TryAddRuleFile(chain[i], GitIgnoreFileName);
                set.TryAddRuleFile(chain[i], FolderssIgnoreFileName);
            }

            return set;
        }

        /// <summary><paramref name="fullPath"/>가 규칙에 걸려 숨겨야 하는 항목인지. 나중 규칙이 앞 규칙을 덮어쓴다(부정 포함).</summary>
        public bool IsIgnored(string fullPath, bool isDirectory)
        {
            if (_rules.Count == 0 || string.IsNullOrWhiteSpace(fullPath))
                return false;

            string normalized;
            try
            {
                normalized = Path.GetFullPath(fullPath).TrimEnd('\\', '/');
            }
            catch (Exception)
            {
                return false;
            }

            var ignored = false;
            foreach (var rule in _rules)
            {
                if (rule.Matches(normalized, isDirectory))
                    ignored = !rule.Negated;
            }
            return ignored;
        }

        private void TryAddRuleFile(string directory, string fileName)
        {
            var path = Path.Combine(directory, fileName);
            if (!File.Exists(path))
                return;

            try
            {
                AddRules(directory, File.ReadAllText(path), path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private void AddRules(string baseDirectory, string text, string sourcePath)
        {
            var basePrefix = ToBasePrefix(baseDirectory);
            var added = false;
            foreach (var rawLine in (text ?? string.Empty).Split('\n'))
            {
                var rule = IgnoreRule.TryParse(rawLine.TrimEnd('\r'), basePrefix);
                if (rule == null)
                    continue;
                _rules.Add(rule);
                added = true;
            }

            if (added && sourcePath != null)
                _sourceFiles.Add(sourcePath);
        }

        /// <summary>끝 구분자를 떼되, 드라이브 루트(<c>C:\</c>)는 <c>C:</c>가 되면 현재 폴더 기준 상대 경로로 바뀌므로 그대로 둔다.</summary>
        private static string NormalizeDirectory(string path)
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);
            if (!string.IsNullOrEmpty(root) && string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
                return full;
            return full.TrimEnd('\\', '/');
        }

        private static string ToBasePrefix(string baseDirectory)
        {
            var normalized = NormalizeDirectory(baseDirectory);
            return normalized.EndsWith("\\") || normalized.EndsWith("/")
                ? normalized
                : normalized + Path.DirectorySeparatorChar;
        }

        private sealed class IgnoreRule
        {
            private readonly string _basePrefix;
            private readonly Regex _regex;

            public bool Negated { get; private set; }
            public bool DirectoryOnly { get; private set; }

            private IgnoreRule(string basePrefix, Regex regex, bool negated, bool directoryOnly)
            {
                _basePrefix = basePrefix;
                _regex = regex;
                Negated = negated;
                DirectoryOnly = directoryOnly;
            }

            public static IgnoreRule TryParse(string line, string basePrefix)
            {
                if (line == null)
                    return null;

                // gitignore: 끝 공백은 \로 이스케이프하지 않는 한 무시한다.
                var pattern = line.TrimEnd(' ', '\t');
                if (pattern.Length == 0 || pattern[0] == '#')
                    return null;

                var negated = false;
                if (pattern[0] == '!')
                {
                    negated = true;
                    pattern = pattern.Substring(1);
                }
                else if (pattern.StartsWith("\\#", StringComparison.Ordinal) || pattern.StartsWith("\\!", StringComparison.Ordinal))
                {
                    pattern = pattern.Substring(1);
                }

                if (pattern.Length == 0)
                    return null;

                var directoryOnly = false;
                if (pattern.EndsWith("/", StringComparison.Ordinal))
                {
                    directoryOnly = true;
                    pattern = pattern.TrimEnd('/');
                    if (pattern.Length == 0)
                        return null;
                }

                // 슬래시가 있으면(앞이든 중간이든) 규칙 파일 위치 기준 경로 패턴, 없으면 어느 깊이에서든 이름만 비교한다.
                var anchored = pattern.IndexOf('/') >= 0;
                if (pattern.StartsWith("/", StringComparison.Ordinal))
                    pattern = pattern.Substring(1);
                if (pattern.Length == 0)
                    return null;

                var body = GlobToRegex(pattern);
                var expression = anchored ? "^" + body + "$" : "^(?:.*/)?" + body + "$";

                Regex regex;
                try
                {
                    regex = new Regex(expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }
                catch (ArgumentException)
                {
                    return null;
                }

                return new IgnoreRule(basePrefix, regex, negated, directoryOnly);
            }

            public bool Matches(string normalizedFullPath, bool isDirectory)
            {
                if (DirectoryOnly && !isDirectory)
                    return false;
                if (!normalizedFullPath.StartsWith(_basePrefix, StringComparison.OrdinalIgnoreCase))
                    return false;

                var relative = normalizedFullPath.Substring(_basePrefix.Length).Replace('\\', '/');
                return relative.Length > 0 && _regex.IsMatch(relative);
            }

            /// <summary>
            /// gitignore 글롭을 정규식 본문으로 바꾼다. <c>*</c>·<c>?</c>는 <c>/</c>를 넘지 않고,
            /// <c>**/</c>(앞)·<c>/**</c>(뒤)·<c>/**/</c>(중간)은 폴더 깊이를 넘는다.
            /// </summary>
            private static string GlobToRegex(string glob)
            {
                var sb = new StringBuilder();
                var i = 0;
                while (i < glob.Length)
                {
                    var c = glob[i];
                    if (c == '*')
                    {
                        if (i + 1 < glob.Length && glob[i + 1] == '*')
                        {
                            var followedBySlash = i + 2 < glob.Length && glob[i + 2] == '/';
                            var precededBySlash = i == 0 || glob[i - 1] == '/';
                            if (precededBySlash && followedBySlash)
                            {
                                sb.Append("(?:.*/)?");
                                i += 3;
                                continue;
                            }
                            sb.Append(".*");
                            i += 2;
                            continue;
                        }

                        sb.Append("[^/]*");
                        i++;
                        continue;
                    }

                    if (c == '?')
                    {
                        sb.Append("[^/]");
                        i++;
                        continue;
                    }

                    if (c == '[')
                    {
                        var close = glob.IndexOf(']', i + 1);
                        if (close > i + 1)
                        {
                            var set = glob.Substring(i + 1, close - i - 1);
                            if (set.StartsWith("!", StringComparison.Ordinal))
                                set = "^" + set.Substring(1);
                            sb.Append('[').Append(set.Replace("\\", "\\\\").Replace("[", "\\[")).Append(']');
                            i = close + 1;
                            continue;
                        }

                        sb.Append("\\[");
                        i++;
                        continue;
                    }

                    if (c == '\\' && i + 1 < glob.Length)
                    {
                        sb.Append(Regex.Escape(glob[i + 1].ToString()));
                        i += 2;
                        continue;
                    }

                    sb.Append(Regex.Escape(c.ToString()));
                    i++;
                }

                return sb.ToString();
            }
        }
    }
}

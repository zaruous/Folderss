using Folderss.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Folderss.Services
{
    public static class SearchService
    {
        public static Task SearchAsync(
            string rootPath,
            string query,
            bool recursive,
            bool caseSensitive,
            bool useRegex,
            SearchTarget target,
            IProgress<SearchResult> progress,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var comparisonType = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                var regex = BuildRegex(query, caseSensitive, useRegex, target);

                foreach (var filePath in EnumerateFiles(rootPath, recursive, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        if (target == SearchTarget.FileName)
                            ScanFileName(filePath, query, regex, comparisonType, progress);
                        else
                            ScanFile(filePath, query, regex, comparisonType, progress, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        // 접근 권한 없는 파일 등은 건너뜀
                    }
                }
            }, cancellationToken);
        }

        /// <summary>
        /// 검색어를 실제 매칭에 쓸 <see cref="Regex"/>로 변환한다. 단순 부분 일치로 처리해야 하면 null을 반환한다.
        /// 정규식 옵션이 꺼져 있어도 파일명 검색이면 <c>*.cs</c> 같은 와일드카드 패턴을 지원한다.
        /// 내용 검색은 줄 텍스트에 와일드카드를 적용하는 것이 부자연스러우므로 기존대로 부분 일치를 유지한다.
        /// </summary>
        private static Regex BuildRegex(string query, bool caseSensitive, bool useRegex, SearchTarget target)
        {
            var flags = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;

            if (useRegex)
                return new Regex(query, flags | RegexOptions.Compiled);

            if (target == SearchTarget.FileName && HasWildcard(query))
                return new Regex(BuildWildcardPattern(query), flags | RegexOptions.Compiled);

            return null;
        }

        private static bool HasWildcard(string query)
        {
            return !string.IsNullOrEmpty(query) && (query.IndexOf('*') >= 0 || query.IndexOf('?') >= 0);
        }

        /// <summary>
        /// <c>*.cs</c>, <c>report?.txt</c> 같은 와일드카드 패턴을 정규식으로 바꾼다.
        /// 확장자 필터를 대체하는 용도이므로 파일명 전체가 일치해야 한다(양끝 고정).
        /// </summary>
        private static string BuildWildcardPattern(string query)
        {
            var builder = new StringBuilder("^");
            foreach (var character in query)
            {
                if (character == '*')
                    builder.Append(".*");
                else if (character == '?')
                    builder.Append('.');
                else
                    builder.Append(Regex.Escape(character.ToString()));
            }
            return builder.Append('$').ToString();
        }

        /// <summary>
        /// 폴더 단위로 파일 목록을 확정하면서 순회한다.
        /// <see cref="Directory.EnumerateFiles(string, string, SearchOption)"/>는 하위 폴더 하나에서
        /// 접근 거부가 나면 열거 자체가 예외로 끊겨 검색 결과가 통째로 사라진다. 여기서는 폴더마다
        /// try/catch로 막아 나머지 폴더를 계속 훑고, 순환을 만드는 정션·심볼릭 링크는 들어가지 않는다.
        /// </summary>
        private static IEnumerable<string> EnumerateFiles(string rootPath, bool recursive, CancellationToken cancellationToken)
        {
            var pending = new Stack<string>();
            pending.Push(rootPath);

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = pending.Pop();

                string[] files;
                try
                {
                    files = Directory.GetFiles(directory);
                }
                catch
                {
                    continue;
                }

                foreach (var file in files)
                    yield return file;

                if (!recursive)
                    continue;

                string[] subdirectories;
                try
                {
                    subdirectories = Directory.GetDirectories(directory);
                }
                catch
                {
                    continue;
                }

                foreach (var subdirectory in subdirectories)
                {
                    if (IsReparsePoint(subdirectory))
                        continue;
                    pending.Push(subdirectory);
                }
            }
        }

        private static bool IsReparsePoint(string directory)
        {
            try
            {
                return (new DirectoryInfo(directory).Attributes & FileAttributes.ReparsePoint) != 0;
            }
            catch
            {
                // 속성을 못 읽는 폴더는 들어가지 않는다.
                return true;
            }
        }

        private static void ScanFileName(
            string filePath,
            string query,
            Regex regex,
            StringComparison comparisonType,
            IProgress<SearchResult> progress)
        {
            var fileName = Path.GetFileName(filePath);
            var matched = regex != null
                ? regex.IsMatch(fileName)
                : fileName.IndexOf(query, comparisonType) >= 0;

            if (!matched)
                return;

            progress.Report(new SearchResult
            {
                FilePath = filePath,
                FileName = fileName,
                FolderPath = Path.GetDirectoryName(filePath),
                LineNumber = 0,
                LineText = "(파일명 일치)"
            });
        }

        private static void ScanFile(
            string filePath,
            string query,
            Regex regex,
            StringComparison comparisonType,
            IProgress<SearchResult> progress,
            CancellationToken cancellationToken)
        {
            // 바이너리 판별을 위해 앞 4096바이트만 읽음
            var header = ReadHeader(filePath, 4096);
            if (header == null || FilePreviewService.LooksBinary(header))
                return;

            var lineNumber = 0;
            using (var reader = new StreamReader(filePath, detectEncodingFromByteOrderMarks: true))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    lineNumber++;

                    var matched = regex != null
                        ? regex.IsMatch(line)
                        : line.IndexOf(query, comparisonType) >= 0;

                    if (!matched)
                        continue;

                    progress.Report(new SearchResult
                    {
                        FilePath = filePath,
                        FileName = Path.GetFileName(filePath),
                        FolderPath = Path.GetDirectoryName(filePath),
                        LineNumber = lineNumber,
                        LineText = line.Trim()
                    });
                }
            }
        }

        private static byte[] ReadHeader(string filePath, int maxBytes)
        {
            try
            {
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var buffer = new byte[Math.Min(maxBytes, stream.Length)];
                    var offset = 0;
                    while (offset < buffer.Length)
                    {
                        var read = stream.Read(buffer, offset, buffer.Length - offset);
                        if (read == 0) break;
                        offset += read;
                    }
                    if (offset != buffer.Length)
                        Array.Resize(ref buffer, offset);
                    return buffer;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}

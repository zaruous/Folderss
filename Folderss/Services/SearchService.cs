using Folderss.Models;
using System;
using System.IO;
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
            IProgress<SearchResult> progress,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var comparisonType = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                Regex regex = null;

                if (useRegex)
                {
                    var flags = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                    regex = new Regex(query, flags | RegexOptions.Compiled);
                }

                foreach (var filePath in Directory.EnumerateFiles(rootPath, "*", option))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
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

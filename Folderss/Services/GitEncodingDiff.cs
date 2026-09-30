using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Folderss.Services
{
    /// <summary>
    /// UTF-16/UTF-32(BOM) 텍스트 파일은 NUL 바이트 때문에 git이 "Binary files … differ"로만 보인다.
    /// 그런 구간을 찾아 양쪽 내용을 BOM 규칙(<see cref="GitTextDecoder.DecodeFile"/>)으로 읽고, UTF-8 임시 파일 두 개를
    /// <c>git diff --no-index</c>로 다시 비교해 줄 diff로 바꿔 끼운다. BOM이 없는 진짜 바이너리는 그대로 둔다.
    /// </summary>
    public static class GitEncodingDiff
    {
        /// <summary>한쪽이라도 이보다 크면 다시 비교하지 않는다(메모리·시간).</summary>
        public const int MaxBytes = 10 * 1024 * 1024;

        private static readonly Regex BinaryLine = new Regex(
            @"^Binary files (?:a/(?<old>.+)|/dev/null) and (?:b/(?<new>.+)|/dev/null) differ$", RegexOptions.Compiled);

        public static async Task<string> ExpandAsync(string repositoryRoot, GitDiffRequest request, string diffText,
            bool ignoreWhitespace, Encoding fallbackEncoding, CancellationToken token)
        {
            if (string.IsNullOrEmpty(diffText) || request == null || diffText.IndexOf("Binary files ", StringComparison.Ordinal) < 0)
                return diffText;

            var oldSide = request.OldSide;
            if (request.OldSideMergeBaseOf != null)
            {
                var mergeBase = await GitCommandRunner.RunAsync(repositoryRoot,
                    new[] { "merge-base", request.OldSideMergeBaseOf[0], request.OldSideMergeBaseOf[1] },
                    GitCommandRunner.QueryTimeout, token, readOnly: true);
                if (!mergeBase.Success)
                    return diffText;
                oldSide = mergeBase.StdOut.Trim();
            }

            var lines = diffText.Split('\n');
            var output = new StringBuilder(diffText.Length);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var match = BinaryLine.Match(line.TrimEnd('\r'));
                string replacement = null;
                if (match.Success)
                {
                    var oldPath = match.Groups["old"].Success ? match.Groups["old"].Value : null;
                    var newPath = match.Groups["new"].Success ? match.Groups["new"].Value : null;
                    replacement = await TryTextDiffAsync(repositoryRoot, oldSide, oldPath, request.NewSide, newPath,
                        ignoreWhitespace, fallbackEncoding, token);
                }

                output.Append(replacement ?? line);
                if (i < lines.Length - 1)
                    output.Append('\n');
            }
            return output.ToString();
        }

        private static async Task<string> TryTextDiffAsync(string root, string oldSide, string oldPath, string newSide, string newPath,
            bool ignoreWhitespace, Encoding fallbackEncoding, CancellationToken token)
        {
            var oldBytes = oldPath == null ? new byte[0] : await ReadSideAsync(root, oldSide, oldPath, token);
            var newBytes = newPath == null ? new byte[0] : await ReadSideAsync(root, newSide, newPath, token);
            if (oldBytes == null || newBytes == null)
                return null;
            // 한쪽이라도 UTF-16/32 BOM이어야 "텍스트인데 바이너리로 보인" 경우다. 둘 다 아니면 진짜 바이너리.
            if (!GitTextDecoder.HasWideBom(oldBytes) && !GitTextDecoder.HasWideBom(newBytes))
                return null;

            var tempDir = Path.Combine(Path.GetTempPath(), "folderss-encdiff-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var oldFile = Path.Combine(tempDir, "old");
                var newFile = Path.Combine(tempDir, "new");
                var utf8 = new UTF8Encoding(false);
                File.WriteAllText(oldFile, GitTextDecoder.DecodeFile(oldBytes, fallbackEncoding), utf8);
                File.WriteAllText(newFile, GitTextDecoder.DecodeFile(newBytes, fallbackEncoding), utf8);

                var args = new List<string> { "diff", "--no-index", "--no-color", "--no-ext-diff" };
                if (ignoreWhitespace)
                    args.Add("-w");
                args.AddRange(new[] { "--", oldFile, newFile });
                var result = await GitCommandRunner.RunAsync(null, args, GitCommandRunner.QueryTimeout, token, readOnly: true);
                if (!GitDiffCommands.IsSuccess(result, noIndex: true))
                    return null;

                var header = string.Format("# 인코딩: {0} → {1} (BOM으로 판정, 텍스트로 다시 비교)",
                    oldPath == null ? "(없음)" : GitTextDecoder.DescribeBom(oldBytes),
                    newPath == null ? "(없음)" : GitTextDecoder.DescribeBom(newBytes));
                var hunkStart = result.StdOut.IndexOf("\n@@", StringComparison.Ordinal);
                if (hunkStart < 0)
                    return header + "\n# 내용은 같습니다 (인코딩·BOM·줄바꿈만 다름)";

                return header + "\n--- " + (oldPath == null ? "/dev/null" : "a/" + oldPath)
                     + "\n+++ " + (newPath == null ? "/dev/null" : "b/" + newPath)
                     + result.StdOut.Substring(hunkStart).TrimEnd('\n');
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        /// <summary>한쪽 내용 바이트. 없으면(추가·삭제·최초 커밋 전) 빈 배열, 너무 크거나 못 읽으면 null.</summary>
        private static async Task<byte[]> ReadSideAsync(string root, string side, string path, CancellationToken token)
        {
            if (side == null)
                return new byte[0];

            if (side == GitDiffCommands.WorkTreeSide)
            {
                var full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full))
                    return new byte[0];
                if (new FileInfo(full).Length > MaxBytes)
                    return null;
                try
                {
                    return File.ReadAllBytes(full);
                }
                catch (IOException)
                {
                    return null;
                }
            }

            var spec = side == GitDiffCommands.IndexSide ? ":" + path : side + ":" + path;
            var size = await GitCommandRunner.RunAsync(root, new[] { "cat-file", "-s", spec }, GitCommandRunner.QueryTimeout, token, readOnly: true);
            if (!size.Success)
                return new byte[0];   // 그 리비전에 파일이 없음(추가/삭제)
            if (!long.TryParse(size.StdOut.Trim(), out var length) || length > MaxBytes)
                return null;

            var blob = await GitCommandRunner.RunAsync(root, new[] { "cat-file", "blob", spec }, GitCommandRunner.QueryTimeout, token,
                readOnly: true, rawOutput: true);
            return blob.Success ? blob.StdOutBytes : null;
        }
    }
}

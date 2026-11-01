using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Folderss.Services
{
    /// <summary>
    /// 메인 패널 파일 작업(복사·이동·삭제·이름 변경)에서 블록 편집기 사이드카(<c>문서.md.meta.json</c>, <see cref="BlockSidecar"/>)를
    /// 문서와 함께 다루는 규칙(#78). 사이드카는 "동반 파일"로 문서의 결과를 따라간다 — 문서를 건너뛰거나 실패하면 그대로 두고,
    /// 문서가 간 자리(번호 붙은 새 이름 포함)의 <c>새이름.md.meta.json</c>으로 간다.
    /// 파일 시스템은 동반 파일이 있는지 볼 때만 읽는다. WPF를 쓰지 않아 테스트 프로젝트에서 소스 링크로 검증한다.
    /// </summary>
    public static class BlockSidecarFiles
    {
        private static readonly string[] DocumentExtensions = { ".md", ".markdown" };

        /// <summary>블록 편집기가 사이드카를 쓰는 문서 확장자(.md·.markdown)인가.</summary>
        public static bool IsDocument(string path)
        {
            var extension = Path.GetExtension(path ?? string.Empty);
            return DocumentExtensions.Any(e => string.Equals(e, extension, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary><c>X.md.meta.json</c>처럼 문서 이름 + <see cref="BlockSidecar.Extension"/>인가.</summary>
        public static bool IsSidecar(string path)
        {
            return DocumentOf(path) != null;
        }

        /// <summary>사이드카 경로에서 문서 경로. 사이드카 이름이 아니면 null.</summary>
        public static string DocumentOf(string sidecarPath)
        {
            if (string.IsNullOrEmpty(sidecarPath) || !sidecarPath.EndsWith(BlockSidecar.Extension, StringComparison.OrdinalIgnoreCase))
                return null;
            var document = sidecarPath.Substring(0, sidecarPath.Length - BlockSidecar.Extension.Length);
            return IsDocument(document) && Path.GetFileName(document).Length > Path.GetExtension(document).Length ? document : null;
        }

        /// <summary>문서가 <paramref name="documentTarget"/>으로 갈 때 사이드카가 갈 경로.</summary>
        public static string TargetFor(string documentTarget)
        {
            return BlockSidecar.PathFor(documentTarget);
        }

        /// <summary>문서 파일 옆에 실제로 있는 사이드카 경로. 문서가 아니거나(폴더 포함) 사이드카가 없으면 null.</summary>
        public static string CompanionOf(string documentPath)
        {
            if (string.IsNullOrEmpty(documentPath) || !IsDocument(documentPath) || Directory.Exists(documentPath))
                return null;
            var sidecar = BlockSidecar.PathFor(Path.TrimEndingDirectorySeparator(documentPath));
            return File.Exists(sidecar) ? sidecar : null;
        }

        /// <summary>
        /// 작업 목록을 (항목, 동반 사이드카)로 묶는다. 문서와 그 사이드카를 둘 다 골랐으면 사이드카는 따로 처리하지 않고 문서에 붙인다(중복 처리 방지).
        /// 사이드카만 골랐으면 보통 파일처럼 그대로 둔다. 순서는 원래 목록 순서.
        /// </summary>
        public static List<SourceWithCompanion> Pair(IEnumerable<string> sources)
        {
            var entries = (sources ?? Enumerable.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => new SourceWithCompanion(Path.TrimEndingDirectorySeparator(path), CompanionOf(path)))
                .ToList();
            var attached = new HashSet<string>(entries.Where(e => e.Companion != null).Select(e => Normalize(e.Companion)),
                StringComparer.OrdinalIgnoreCase);
            if (attached.Count == 0)
                return entries;
            return entries.Where(e => !attached.Contains(Normalize(e.Path))).ToList();
        }

        private static string Normalize(string path)
        {
            try
            {
                return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return path;
            }
        }
    }

    /// <summary>작업할 최상위 항목과 함께 따라갈 사이드카(없으면 null).</summary>
    public sealed class SourceWithCompanion
    {
        public SourceWithCompanion(string path, string companion)
        {
            Path = path;
            Companion = companion;
        }

        public string Path { get; }
        public string Companion { get; }
    }
}

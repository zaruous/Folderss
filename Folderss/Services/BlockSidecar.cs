using Folderss.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Folderss.Services
{
    /// <summary>
    /// 블록 편집기 사이드카(#78 D안): Markdown으로 나타낼 수 없는 블록 속성(<see cref="EditorBlock.Attributes"/>)을
    /// 문서 옆 <c>문서.md.meta.json</c>에 저장한다. Markdown 파일은 순수하게 두어 다른 도구·GitHub에서 그대로 보인다.
    /// <para>
    /// 속성은 "Markdown에 쓰인 블록 순번 + (종류·본문) 해시 + 바로 앞 블록 해시"로 블록에 붙인다. 문서 전체 해시가 같으면(그대로면) 순번으로 바로 붙고,
    /// 다른 도구에서 블록을 넣거나 지웠으면 같은 해시의 블록을 순서대로 찾되 내용이 같은 블록이 여럿이면 앞 블록까지 맞는 쪽을 고른다.
    /// 본문이 바뀐 블록의 속성은 붙지 않는다(개수를 알려 줌).
    /// </para>
    /// 형식(version 1, UTF-8, 사람이 읽을 수 있게 들여씀):
    /// <code>
    /// { "version": 1, "file": "문서.md", "document": "문서 전체 해시",
    ///   "blocks": [ { "index": 12, "kind": "Paragraph", "hash": "1a2b…(16자)", "before": "앞 블록 해시", "preview": "본문 앞 40자",
    ///                 "attrs": { "background": "yellow" } } ] }
    /// </code>
    /// WPF를 쓰지 않아 테스트 프로젝트에서 소스 링크로 검증한다.
    /// </summary>
    public static class BlockSidecar
    {
        public const int Version = 1;
        public const string Extension = ".meta.json";

        /// <summary>읽을 수 없는 사이드카(JSON 오류·모르는 버전)일 때 <see cref="Apply"/>가 돌려주는 값.</summary>
        public const int Unreadable = -1;

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            // 한글 미리보기를 \uXXXX로 바꾸지 않는다(사람이 열어 볼 수 있게).
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private sealed class SidecarDocument
        {
            public int Version { get; set; }
            public string File { get; set; }
            public string Document { get; set; }
            public List<SidecarEntry> Blocks { get; set; }
        }

        private sealed class SidecarEntry
        {
            public int Index { get; set; }
            public string Kind { get; set; }
            public string Hash { get; set; }
            public string Before { get; set; }
            public string Preview { get; set; }
            public SortedDictionary<string, string> Attrs { get; set; }
        }

        public static string PathFor(string markdownPath)
        {
            return markdownPath + Extension;
        }

        /// <summary>
        /// 속성이 있는 블록만 사이드카 JSON으로 쓴다. 속성이 하나도 없으면 null(사이드카 파일을 지우면 됨).
        /// <paramref name="savedMarkdown"/>은 함께 저장하는 Markdown — 다시 읽었을 때와 같은 블록으로 해시를 내려고 그것을 해석해 쓴다.
        /// </summary>
        public static string Serialize(IList<EditorBlock> blocks, string savedMarkdown, string fileName)
        {
            var written = blocks.Where(BlockMarkdown.IsWritten).ToList();
            if (!written.Any(b => b.Attributes != null && b.Attributes.Count > 0))
                return null;

            // 저장한 Markdown을 다시 읽은 블록과 개수가 같으면 그 블록으로 해시를 낸다(공백 정리 등으로 본문이 조금 달라져도 다시 읽을 때 맞도록).
            var reread = savedMarkdown != null ? BlockMarkdown.Parse(savedMarkdown).Where(BlockMarkdown.IsWritten).ToList() : null;
            var anchors = reread != null && reread.Count == written.Count ? reread : written;
            var hashes = anchors.Select(Hash).ToArray();

            var entries = new List<SidecarEntry>();
            for (var i = 0; i < written.Count; i++)
            {
                var block = written[i];
                if (block.Attributes == null || block.Attributes.Count == 0)
                    continue;
                entries.Add(new SidecarEntry
                {
                    Index = i,
                    Kind = anchors[i].Kind.ToString(),
                    Hash = hashes[i],
                    Before = i > 0 ? hashes[i - 1] : string.Empty,
                    Preview = Preview(anchors[i].Text),
                    Attrs = new SortedDictionary<string, string>(block.Attributes, StringComparer.Ordinal),
                });
            }
            return JsonSerializer.Serialize(new SidecarDocument
            {
                Version = Version,
                File = fileName,
                Document = DocumentHash(hashes),
                Blocks = entries,
            }, Options);
        }

        /// <summary>
        /// 사이드카 속성을 블록(방금 Markdown에서 읽은 목록)에 붙인다. 붙이지 못한 항목 수, 읽을 수 없으면 <see cref="Unreadable"/>.
        /// </summary>
        public static int Apply(IList<EditorBlock> blocks, string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return 0;
            SidecarDocument document;
            try
            {
                document = JsonSerializer.Deserialize<SidecarDocument>(json, Options);
            }
            catch (JsonException)
            {
                return Unreadable;
            }
            if (document == null || document.Version > Version)
                return Unreadable;
            if (document.Blocks == null)
                return 0;

            var written = blocks.Where(BlockMarkdown.IsWritten).ToList();
            var hashes = written.Select(Hash).ToArray();
            var unchanged = document.Document != null && document.Document == DocumentHash(hashes);
            var unmatched = 0;
            var from = 0;
            foreach (var entry in document.Blocks.Where(e => e != null).OrderBy(e => e.Index))
            {
                var target = -1;
                if (unchanged && entry.Index >= from && entry.Index < written.Count && hashes[entry.Index] == entry.Hash)
                {
                    target = entry.Index;
                }
                else
                {
                    // 같은 해시 후보 중 앞 블록까지 맞는 것, 없으면 첫 후보
                    for (var i = from; i < written.Count; i++)
                    {
                        if (hashes[i] != entry.Hash)
                            continue;
                        if (target < 0)
                            target = i;
                        if ((i > 0 ? hashes[i - 1] : string.Empty) == (entry.Before ?? string.Empty))
                        {
                            target = i;
                            break;
                        }
                    }
                }
                if (target < 0 || entry.Attrs == null)
                {
                    unmatched++;
                    continue;
                }
                foreach (var pair in entry.Attrs)
                    written[target].SetAttribute(pair.Key, pair.Value);
                from = target + 1;
            }
            return unmatched;
        }

        /// <summary>블록을 알아보는 해시: 종류 + 본문(줄 끝 공백·줄바꿈 형식 무시)의 SHA-256 앞 16자.</summary>
        public static string Hash(EditorBlock block)
        {
            var text = string.Join("\n", (block.Text ?? string.Empty).Replace("\r\n", "\n").Split('\n').Select(line => line.TrimEnd())).Trim();
            var hasSource = block.Kind == BlockKind.Image || EditorBlock.IsLinkCard(block.Kind);
            var input = block.Kind + "\n" + text + (hasSource ? "\n" + block.Source : string.Empty);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).Substring(0, 16).ToLowerInvariant();
        }

        /// <summary>문서 전체 해시(블록 해시를 이어 붙인 것의 해시). 문서가 그대로인지 판단한다.</summary>
        private static string DocumentHash(IEnumerable<string> blockHashes)
        {
            var joined = string.Join("|", blockHashes);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined))).Substring(0, 16).ToLowerInvariant();
        }

        private static string Preview(string text)
        {
            var oneLine = (text ?? string.Empty).Replace("\r", string.Empty).Replace('\n', ' ').Replace(EditorBlock.CellSeparator, '|').Trim();
            return oneLine.Length > 40 ? oneLine.Substring(0, 40) + "…" : oneLine;
        }
    }
}

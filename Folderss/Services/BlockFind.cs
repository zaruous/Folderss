using Folderss.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Folderss.Services
{
    /// <summary>블록 편집기 문서 내 검색 결과 하나: 블록 위치와 그 블록 <see cref="EditorBlock.Text"/> 안의 글자 범위.</summary>
    public struct BlockMatch
    {
        public int Block;
        public int Start;
        public int Length;

        public BlockMatch(int block, int start, int length)
        {
            Block = block;
            Start = start;
            Length = length;
        }

        public override string ToString()
        {
            return string.Format("{0}:{1}+{2}", Block, Start, Length);
        }
    }

    /// <summary>
    /// 블록 편집기(<c>Controls/BlockEditor</c>)의 찾기·바꾸기, 제목 앵커(<c>#제목</c>), 외부 변경 뒤 스크롤 앵커 복원, 링크 해석.
    /// 화면에 보이는 블록만 행을 만드는 가상화 구조라 화면이 아니라 모델(<see cref="EditorBlock"/> 목록)에서 찾는다.
    /// WPF를 쓰지 않아 테스트 프로젝트에서 소스 링크로 쓴다.
    /// </summary>
    public static class BlockFind
    {
        /// <summary>찾을 수 있는 블록인지. 구분선·목차는 본문이 없고, front matter는 속성 표라 결과를 입력란 선택으로 보여 줄 수 없다.</summary>
        public static bool IsSearchable(BlockKind kind)
        {
            return kind != BlockKind.Divider && kind != BlockKind.Toc && kind != BlockKind.FrontMatter;
        }

        /// <summary>
        /// 모든 블록에서 <paramref name="query"/>를 찾는다(겹치지 않게 앞에서부터). 표 행은 셀 경계(탭)를 넘는 일치를 뺀다.
        /// </summary>
        public static List<BlockMatch> FindAll(IReadOnlyList<EditorBlock> blocks, string query, bool matchCase)
        {
            var result = new List<BlockMatch>();
            if (blocks == null || string.IsNullOrEmpty(query))
                return result;
            var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            for (var i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                if (!IsSearchable(block.Kind))
                    continue;
                foreach (var start in FindInText(block.Text, query, comparison, block.Kind == BlockKind.TableRow))
                    result.Add(new BlockMatch(i, start, query.Length));
            }
            return result;
        }

        private static IEnumerable<int> FindInText(string text, string query, StringComparison comparison, bool tableRow)
        {
            if (string.IsNullOrEmpty(text) || text.Length < query.Length)
                yield break;
            var at = 0;
            while (at <= text.Length - query.Length)
            {
                var found = text.IndexOf(query, at, comparison);
                if (found < 0)
                    yield break;
                if (tableRow && text.IndexOf(EditorBlock.CellSeparator, found, query.Length) >= 0)
                {
                    at = found + 1;
                    continue;
                }
                yield return found;
                at = found + Math.Max(1, query.Length);
            }
        }

        /// <summary>
        /// 위치(블록·글자)에서 다음(<paramref name="forward"/>) 또는 이전 결과의 번호. 끝을 넘으면 처음으로 돈다. 결과가 없으면 -1.
        /// 다음 찾기는 그 위치에서 시작하는 결과도 고르고(<paramref name="inclusive"/>), 이전 찾기는 그 위치보다 앞선 결과만 고른다.
        /// </summary>
        public static int NextMatch(IReadOnlyList<BlockMatch> matches, int block, int offset, bool forward, bool inclusive = false)
        {
            if (matches == null || matches.Count == 0)
                return -1;
            if (forward)
            {
                for (var i = 0; i < matches.Count; i++)
                {
                    var m = matches[i];
                    if (m.Block > block || (m.Block == block && (inclusive ? m.Start >= offset : m.Start > offset)))
                        return i;
                }
                return 0;
            }
            for (var i = matches.Count - 1; i >= 0; i--)
            {
                var m = matches[i];
                if (m.Block < block || (m.Block == block && m.Start < offset))
                    return i;
            }
            return matches.Count - 1;
        }

        /// <summary>결과가 지금 블록 내용에서도 그 자리에 그대로 있는지(바꾸기 하나 전에 확인).</summary>
        public static bool IsMatchAt(IReadOnlyList<EditorBlock> blocks, BlockMatch match, string query, bool matchCase)
        {
            if (blocks == null || string.IsNullOrEmpty(query) || match.Block < 0 || match.Block >= blocks.Count || match.Length != query.Length)
                return false;
            var text = blocks[match.Block].Text ?? string.Empty;
            if (match.Start < 0 || match.Start + match.Length > text.Length)
                return false;
            return string.Compare(text, match.Start, query, 0, query.Length, matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) == 0;
        }

        /// <summary>바꿀 글자. 표 행이면 셀 구분자·줄바꿈을 공백으로(셀 구조가 바뀌지 않게), 그 밖은 그대로.</summary>
        public static string SanitizeReplacement(BlockKind kind, string replacement)
        {
            replacement = replacement ?? string.Empty;
            if (kind == BlockKind.TableRow)
                return replacement.Replace(EditorBlock.CellSeparator, ' ').Replace("\r", string.Empty).Replace('\n', ' ');
            return replacement;
        }

        /// <summary>결과 하나를 바꾼다. 바뀐 블록의 새 글자 끝 위치(다음 찾기 시작점)를 돌려준다. 결과가 그 자리에 없으면 -1.</summary>
        public static int ReplaceOne(IReadOnlyList<EditorBlock> blocks, BlockMatch match, string query, string replacement, bool matchCase)
        {
            if (!IsMatchAt(blocks, match, query, matchCase))
                return -1;
            var block = blocks[match.Block];
            var value = SanitizeReplacement(block.Kind, replacement);
            block.Text = block.Text.Remove(match.Start, match.Length).Insert(match.Start, value);
            return match.Start + value.Length;
        }

        /// <summary>
        /// 모든 결과를 바꾼다(블록 모델을 직접 고침). 바꾼 개수를 돌려주고, 바뀐 블록 범위를 <paramref name="first"/>·<paramref name="last"/>로 알린다(없으면 -1).
        /// 호출자는 이 변경을 실행 취소 한 단계로 기록한다.
        /// </summary>
        public static int ReplaceAll(IReadOnlyList<EditorBlock> blocks, string query, string replacement, bool matchCase, out int first, out int last)
        {
            first = last = -1;
            var matches = FindAll(blocks, query, matchCase);
            if (matches.Count == 0)
                return 0;
            var i = 0;
            while (i < matches.Count)
            {
                var blockIndex = matches[i].Block;
                var block = blocks[blockIndex];
                var value = SanitizeReplacement(block.Kind, replacement);
                if (block.GetAttribute(TextMarks.Attribute) != null)
                {
                    // 줄 안 범위(밑줄·색)가 있는 블록은 뒤에서부터 하나씩 바꿔 범위가 결과마다 정확히 옮겨지게 한다(EditorBlock.Text가 바뀐 구간 하나로 옮김).
                    var firstOfBlock = i;
                    while (i < matches.Count && matches[i].Block == blockIndex)
                        i++;
                    for (var k = i - 1; k >= firstOfBlock; k--)
                        block.Text = block.Text.Remove(matches[k].Start, matches[k].Length).Insert(matches[k].Start, value);
                    if (first < 0)
                        first = blockIndex;
                    last = blockIndex;
                    continue;
                }
                var text = block.Text;
                var builder = new StringBuilder(text.Length);
                var copied = 0;
                for (; i < matches.Count && matches[i].Block == blockIndex; i++)
                {
                    builder.Append(text, copied, matches[i].Start - copied);
                    builder.Append(value);
                    copied = matches[i].Start + matches[i].Length;
                }
                builder.Append(text, copied, text.Length - copied);
                block.Text = builder.ToString();
                if (first < 0)
                    first = blockIndex;
                last = blockIndex;
            }
            return matches.Count;
        }

        /// <summary>표 행 본문(탭 구분) 안 위치를 셀 번호와 그 셀 안 위치로 바꾼다.</summary>
        public static void CellOffset(string text, int offset, out int cell, out int cellOffset)
        {
            cell = 0;
            var cellStart = 0;
            text = text ?? string.Empty;
            offset = Math.Max(0, Math.Min(offset, text.Length));
            for (var i = 0; i < offset; i++)
            {
                if (text[i] == EditorBlock.CellSeparator)
                {
                    cell++;
                    cellStart = i + 1;
                }
            }
            cellOffset = offset - cellStart;
        }

        /// <summary>셀 번호와 셀 안 위치를 표 행 본문 안 위치로 바꾼다(<see cref="CellOffset"/>의 반대).</summary>
        public static int TextOffset(string text, int cell, int cellOffset)
        {
            text = text ?? string.Empty;
            var start = 0;
            for (var c = 0; c < cell; c++)
            {
                var tab = text.IndexOf(EditorBlock.CellSeparator, start);
                if (tab < 0)
                    return text.Length;
                start = tab + 1;
            }
            return Math.Min(text.Length, start + Math.Max(0, cellOffset));
        }

        // ── 제목 앵커(#제목) ───────────────────────────────────────────────

        private static readonly Regex InlineLink = new Regex(@"!?\[([^\]]*)\]\([^)]*\)", RegexOptions.Compiled);
        private static readonly Regex SlugDrop = new Regex(@"[^\p{L}\p{M}\p{N}\s_-]", RegexOptions.Compiled);

        /// <summary>
        /// Markdown 뷰어(<c>markdown-app.js</c> <c>slugify</c>)와 같은 GitHub 제목 앵커 규칙: 소문자로, 글자·숫자·공백·<c>-</c>·<c>_</c> 외 문자는 지우고 공백은 하나씩 <c>-</c>로.
        /// 블록 본문은 줄 안 서식 원문이므로 링크는 글자만 남기고, 굵게·기울임·취소선 표식(<c>**</c>·<c>__</c>·<c>~~</c>)은 먼저 지운다.
        /// </summary>
        public static string Slugify(string headingText)
        {
            var text = InlineLink.Replace(headingText ?? string.Empty, "$1").Replace("**", string.Empty).Replace("__", string.Empty).Replace("~~", string.Empty);
            text = SlugDrop.Replace(text.Trim().ToLowerInvariant(), string.Empty);
            var builder = new StringBuilder(text.Length);
            foreach (var c in text)
                builder.Append(char.IsWhiteSpace(c) ? '-' : c);
            return builder.ToString();
        }

        /// <summary>
        /// <c>#조각</c>(퍼센트 인코딩일 수 있음)이 가리키는 제목 블록 위치. 같은 제목이 여럿이면 GitHub처럼 <c>-1</c>, <c>-2</c>가 붙는다. 없으면 -1.
        /// </summary>
        public static int FindHeading(IReadOnlyList<EditorBlock> blocks, string fragment)
        {
            if (blocks == null || string.IsNullOrEmpty(fragment))
                return -1;
            var target = fragment.StartsWith("#", StringComparison.Ordinal) ? fragment.Substring(1) : fragment;
            try
            {
                target = Uri.UnescapeDataString(target);
            }
            catch (UriFormatException)
            {
            }
            if (target.Length == 0)
                return -1;

            var used = new HashSet<string>(StringComparer.Ordinal);
            var loose = -1;
            var looseTarget = Slugify(target);
            for (var i = 0; i < blocks.Count; i++)
            {
                if (blocks[i].GetHeadingLevel() == 0)
                    continue;
                var slug = Slugify(blocks[i].Text);
                var id = slug.Length > 0 ? slug : "section";
                for (var n = 1; used.Contains(id); n++)
                    id = (slug.Length > 0 ? slug : "section") + "-" + n;
                used.Add(id);
                if (string.Equals(id, target, StringComparison.Ordinal))
                    return i;
                // 손으로 쓴 앵커(대문자·공백 등)는 같은 규칙으로 바꿔 한 번 더 맞춰 본다.
                if (loose < 0 && string.Equals(id, looseTarget, StringComparison.Ordinal))
                    loose = i;
            }
            return loose;
        }

        // ── 외부 변경 뒤 스크롤 앵커 ───────────────────────────────────────

        /// <summary>
        /// 다시 읽은 문서에서 보던 블록(<paramref name="anchor"/>, 예전 위치 <paramref name="oldIndex"/>)의 새 위치.
        /// 종류·본문이 같은 블록을 예전 위치에서 가까운 순서로 찾고, 없으면 예전 위치(문서 길이 안으로 줄임)를 쓴다.
        /// </summary>
        public static int MapAnchor(EditorBlock anchor, int oldIndex, IReadOnlyList<EditorBlock> newBlocks)
        {
            if (newBlocks == null || newBlocks.Count == 0)
                return -1;
            var start = Math.Max(0, Math.Min(oldIndex, newBlocks.Count - 1));
            if (anchor == null)
                return start;
            for (var d = 0; d < newBlocks.Count; d++)
            {
                var below = start + d;
                var above = start - d;
                if (below >= newBlocks.Count && above < 0)
                    break;
                if (below < newBlocks.Count && SameAnchor(anchor, newBlocks[below]))
                    return below;
                if (d > 0 && above >= 0 && SameAnchor(anchor, newBlocks[above]))
                    return above;
            }
            return start;
        }

        private static bool SameAnchor(EditorBlock a, EditorBlock b)
        {
            return a.Kind == b.Kind && string.Equals(a.Text, b.Text, StringComparison.Ordinal) && string.Equals(a.Source, b.Source, StringComparison.Ordinal);
        }

        // ── 링크 ───────────────────────────────────────────────────────────

        private static readonly Regex MarkdownLink = new Regex(@"!?\[(?:[^\]\\]|\\.)*\]\(\s*(?:<([^>]*)>|([^)\s]+))(?:\s+(?:""[^""]*""|'[^']*'))?\s*\)", RegexOptions.Compiled);
        private static readonly Regex AngleLink = new Regex(@"<((?:https?|file|mailto):[^>\s]+)>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex BareUrl = new Regex(@"(?:https?://|www\.)[^\s<>()\[\]""']+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// 본문 <paramref name="offset"/> 위치에 걸친 링크 주소: <c>[글자](주소)</c>·<c>![대체](주소)</c>·<c>&lt;https://…&gt;</c>·맨 URL(<c>https://</c>·<c>www.</c>). 없으면 null.
        /// </summary>
        public static string LinkAt(string text, int offset)
        {
            if (string.IsNullOrEmpty(text) || offset < 0 || offset > text.Length)
                return null;
            foreach (Match m in MarkdownLink.Matches(text))
            {
                if (offset >= m.Index && offset <= m.Index + m.Length - 1)
                    return m.Groups[1].Success ? m.Groups[1].Value.Trim() : m.Groups[2].Value;
            }
            foreach (Match m in AngleLink.Matches(text))
            {
                if (offset >= m.Index && offset <= m.Index + m.Length - 1)
                    return m.Groups[1].Value;
            }
            foreach (Match m in BareUrl.Matches(text))
            {
                if (offset >= m.Index && offset <= m.Index + m.Length)
                {
                    var url = m.Value.TrimEnd('.', ',', ';', ':', '!', '?');
                    return url.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + url : url;
                }
            }
            return null;
        }

        /// <summary>링크의 <c>#조각</c>(없으면 null).</summary>
        public static string ExtractFragment(string link)
        {
            if (link == null)
                return null;
            var hash = link.IndexOf('#');
            return hash >= 0 && hash < link.Length - 1 ? link.Substring(hash + 1) : null;
        }

        /// <summary>
        /// 링크를 로컬 경로로 푼다(Markdown 뷰어 <c>ResolveLocalLinkPath</c>와 같은 규칙): 절대 경로·<c>file:</c> URI·문서 폴더 기준 상대 경로.
        /// 웹 주소 등 로컬이 아니면 null. <c>#조각</c>·<c>?질의</c>는 뺀다.
        /// </summary>
        public static string ResolveLocalLink(string baseDirectory, string link)
        {
            if (string.IsNullOrWhiteSpace(link))
                return null;
            var end = link.Length;
            var hash = link.IndexOf('#');
            if (hash >= 0)
                end = hash;
            var query = link.IndexOf('?');
            if (query >= 0 && query < end)
                end = query;
            var pathPart = link.Substring(0, end);
            if (string.IsNullOrWhiteSpace(pathPart))
                return null;

            try
            {
                var unescaped = Uri.UnescapeDataString(pathPart);
                if (Path.IsPathRooted(unescaped))
                    return Path.GetFullPath(unescaped);

                Uri absolute;
                if (Uri.TryCreate(pathPart, UriKind.Absolute, out absolute))
                    return absolute.IsFile ? absolute.LocalPath : null;

                if (string.IsNullOrWhiteSpace(baseDirectory))
                    return null;
                return Path.GetFullPath(Path.Combine(baseDirectory, unescaped.Replace('/', Path.DirectorySeparatorChar)));
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (UriFormatException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
            catch (PathTooLongException)
            {
                return null;
            }
        }
    }
}

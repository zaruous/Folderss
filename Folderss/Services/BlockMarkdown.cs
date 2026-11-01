using Folderss.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Folderss.Services
{
    /// <summary>
    /// 블록 편집기 문서(<see cref="EditorBlock"/> 목록) ↔ Markdown 변환. WPF를 쓰지 않아 테스트 프로젝트에서 소스 링크로 검증한다.
    /// <list type="bullet">
    /// <item>목록 중첩은 CommonMark 규칙(자식은 부모 항목의 내용 시작 열만큼 들여씀)으로 읽고 쓴다.</item>
    /// <item>토글은 GitHub이 그리는 <c>&lt;details&gt;&lt;summary&gt;</c>, 콜아웃은 GitHub 알림 <c>&gt; [!NOTE]</c>로 저장한다.</item>
    /// <item>문서 맨 앞 front matter는 <see cref="BlockKind.FrontMatter"/>(원문 그대로 보관·저장, 화면은 속성 표), 구분 행 없는 표·HTML 블록처럼
    /// 편집기가 해석하지 않는 부분은 <see cref="BlockKind.Raw"/>로 원문을 보존한다.</item>
    /// <item><see cref="Serialize"/>는 정규형으로 쓴다(글머리 기호 <c>-</c>, 번호 다시 매김, 블록 사이 빈 줄 하나, 빈 문단은 쓰지 않음).
    /// 파일 저장은 <see cref="MarkdownSource"/>(BlockMarkdown.Source.cs)가 바뀐 블록만 정규형으로 다시 쓰고 나머지는 원문 줄을 그대로 둔다(#81).</item>
    /// </list>
    /// </summary>
    public static partial class BlockMarkdown
    {
        private static readonly Regex AtxHeading = new Regex(@"^(#{1,6})(?:[ \t]+(.*?))?(?:[ \t]+#+)?[ \t]*$");
        private static readonly Regex ThematicBreak = new Regex(@"^(?:(?:\*[ \t]*){3,}|(?:-[ \t]*){3,}|(?:_[ \t]*){3,})$");
        private static readonly Regex SetextH1 = new Regex(@"^=+[ \t]*$");
        private static readonly Regex SetextH2 = new Regex(@"^-+[ \t]*$");
        private static readonly Regex BulletItem = new Regex(@"^([-*+])(?:([ \t]+)(.*)|$)");
        private static readonly Regex OrderedItem = new Regex(@"^(\d{1,9})([.)])(?:([ \t]+)(.*)|$)");
        private static readonly Regex TaskPrefix = new Regex(@"^\[([ xX])\](?:[ \t]+(.*)|$)");
        private static readonly Regex CalloutMarker = new Regex(@"^\[!([A-Za-z]+)\][ \t]*$");
        private static readonly Regex NumberedShortcut = new Regex(@"^\d{1,9}[.)] $");
        private static readonly Regex TableDelimiter = new Regex(@"^\|?[ \t]*:?-+:?[ \t]*(\|[ \t]*:?-+:?[ \t]*)*\|?[ \t]*$");
        private static readonly Regex ImageLine = new Regex(@"^!\[([^\]]*)\]\([ \t]*(?:<([^>]*)>|([^)\s]+))(?:[ \t]+""[^""]*"")?[ \t]*\)$");
        /// <summary>링크 카드 줄 <c>[아이콘 제목](경로)</c>: 1 = 아이콘, 2·3 = 제목, 4·5 = 경로(꺾쇠 안 / 밖).</summary>
        private static readonly Regex LinkCardLine = new Regex(@"^\[(📎|🔖|📄)(?:[ \t]+([^\]]*)|([^\]]*))\]\([ \t]*(?:<([^>]*)>|([^)\s]+))(?:[ \t]+""[^""]*"")?[ \t]*\)$");

        public static readonly string[] CalloutTypes = { "NOTE", "TIP", "IMPORTANT", "WARNING", "CAUTION" };

        // ── 읽기 ───────────────────────────────────────────────────────────

        public static List<EditorBlock> Parse(string markdown)
        {
            return ParseCore(markdown, null);
        }

        /// <summary>
        /// <paramref name="record"/>가 있으면 블록마다 원문 줄 범위·쓰기 형식(목록 표식 등)을 기록한다(<see cref="MarkdownSource"/>).
        /// <paramref name="allowFrontMatter"/>가 false면 첫 줄 <c>---</c>를 front matter로 보지 않는다(문서 중간 구간만 다시 읽어 검증할 때).
        /// </summary>
        internal static List<EditorBlock> ParseCore(string markdown, SourceRecord record, bool allowFrontMatter = true)
        {
            var text = (markdown ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            if (text.Length > 0 && text[0] == '﻿')
                text = text.Substring(1);
            var lines = text.Split('\n');
            var blocks = new List<EditorBlock>();
            new Parser(lines, record, null).Parse(blocks, 0, allowFrontMatter);
            BlockOperations.NormalizeIndents(blocks);
            return blocks;
        }

        /// <summary><see cref="ParseCore"/>가 원문을 읽으며 남기는 기록(<see cref="MarkdownSource"/>).</summary>
        internal sealed class SourceRecord
        {
            /// <summary>블록별 원문 자리·형식.</summary>
            public readonly Dictionary<EditorBlock, BlockSourceInfo> Infos = new Dictionary<EditorBlock, BlockSourceInfo>();

            /// <summary>맨 바깥에서 닫히지 않은 코드 펜스·수식·토글이 문서 끝까지 이어졌는지(그 뒤에 글을 붙이면 그 블록에 빨려 들어감).</summary>
            public bool OpenAtEnd;
        }

        private sealed class Parser
        {
            private readonly string[] _lines;
            // 원문 범위 기록(없으면 기록하지 않음)과 이 파서의 줄 → 문서 줄 번호(null이면 같음, -1은 원문 줄이 아님 — 토글 닫는 줄 앞 글자)
            private readonly SourceRecord _record;
            private readonly Dictionary<EditorBlock, BlockSourceInfo> _sources;
            private readonly int[] _lineMap;

            // 열려 있는 목록 항목: 표식 열, 내용 시작 열, 블록 들여쓰기 수준
            private readonly List<int[]> _listStack = new List<int[]>();
            private EditorBlock _open;   // 다음 줄이 이어 붙을 수 있는 문단·목록 항목
            private bool _lastWasBlank = true;
            private int _baseLevel;
            // 맨 바깥 파서에서 빈 줄(또는 문서 처음) 바로 다음, 들여쓰지 않은 줄 — 여기서 시작한 블록은 앞 내용과 상관없이 같은 결과로 읽힌다(SafeStart).
            private int _safeLine = -1;

            public Parser(string[] lines, SourceRecord record, int[] lineMap)
            {
                _lines = lines;
                _record = record;
                _sources = record?.Infos;
                _lineMap = lineMap;
            }

            /// <summary>맨 바깥 파서에서 블록이 문서 끝까지 닫히지 않았다.</summary>
            private void MarkOpenAtEnd()
            {
                if (_record != null && _lineMap == null)
                    _record.OpenAtEnd = true;
            }

            private int DocLine(int i)
            {
                if (_lineMap == null)
                    return i;
                return i >= 0 && i < _lineMap.Length ? _lineMap[i] : -1;
            }

            /// <summary>블록이 이 파서의 줄 [start, end)에서 왔음을 기록한다. 문서 줄로 이어지지 않으면 기록하지 않는다(그 블록은 늘 정규형으로 쓴다).</summary>
            private BlockSourceInfo Track(EditorBlock block, int start, int end, int column)
            {
                if (_sources == null || end <= start)
                    return null;
                var s = DocLine(start);
                var e = DocLine(end - 1);
                if (s < 0 || e < 0 || e - s != end - 1 - start)
                    return null;
                var info = new BlockSourceInfo { Start = s, End = e + 1, Column = column, SafeStart = _lineMap == null && start == _safeLine };
                _sources[block] = info;
                return info;
            }

            /// <summary>문단·목록 항목이 다음 줄(<paramref name="line"/>)까지 이어졌다.</summary>
            private void Extend(EditorBlock block, int line)
            {
                BlockSourceInfo info;
                if (_sources == null || block == null || !_sources.TryGetValue(block, out info))
                    return;
                if (DocLine(line) == info.End)
                    info.End++;
                else
                    _sources.Remove(block);
            }

            public void Parse(List<EditorBlock> output, int baseLevel, bool allowFrontMatter)
            {
                _baseLevel = baseLevel;
                for (var i = 0; i < _lines.Length; i++)
                {
                    var line = _lines[i];
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        _open = null;
                        _lastWasBlank = true;
                        continue;
                    }

                    var indent = CountIndent(line);
                    var content = line.TrimStart(' ', '\t');
                    if (_sources != null)
                        _safeLine = _lineMap == null && _lastWasBlank && _open == null && indent == 0 ? i : -1;

                    if (allowFrontMatter && i == 0 && content.TrimEnd() == "---")
                    {
                        var close = FindFrontMatterEnd(1);
                        if (close > 0)
                        {
                            var frontMatter = new EditorBlock(BlockKind.FrontMatter, string.Join("\n", _lines, 0, close + 1));
                            output.Add(frontMatter);
                            Track(frontMatter, 0, close + 1, 0);
                            i = close;
                            Close();
                            continue;
                        }
                    }

                    // 문단 바로 아래의 === / --- 는 Setext 제목이다(--- 를 구분선으로 읽기 전에 본다).
                    if (_open != null && !_lastWasBlank && _open.Kind == BlockKind.Paragraph && indent < 4)
                    {
                        if (SetextH1.IsMatch(content) || SetextH2.IsMatch(content))
                        {
                            _open.Kind = SetextH1.IsMatch(content) ? BlockKind.Heading1 : BlockKind.Heading2;
                            _open.Text = _open.Text.Replace('\n', ' ');
                            Extend(_open, i);
                            BlockSourceInfo setext;
                            if (_sources != null && _sources.TryGetValue(_open, out setext))
                                setext.Setext = content.TrimEnd();
                            Close();
                            continue;
                        }
                    }

                    var fence = FenceOf(content);
                    if (fence != null)
                    {
                        var level = LevelForBlock(indent);
                        var end = FindFenceEnd(i + 1, fence);
                        if (end >= _lines.Length)
                            MarkOpenAtEnd();
                        var body = new List<string>();
                        for (var k = i + 1; k < end && k < _lines.Length; k++)
                            body.Add(StripIndent(_lines[k], indent));
                        var code = new EditorBlock(BlockKind.Code, string.Join("\n", body), level)
                        {
                            Language = content.Substring(fence.Length).Trim(),
                        };
                        output.Add(code);
                        Track(code, i, Math.Min(end, _lines.Length - 1) + 1, indent);
                        i = Math.Min(end, _lines.Length - 1);
                        Close();
                        continue;
                    }

                    var trimmedContent = content.TrimEnd();
                    if (trimmedContent.StartsWith("$$", StringComparison.Ordinal))
                    {
                        var level = LevelForBlock(indent);
                        if (trimmedContent.Length >= 4 && trimmedContent.EndsWith("$$", StringComparison.Ordinal))
                        {
                            var expr = trimmedContent.Substring(2, trimmedContent.Length - 4).Trim();
                            var inlineMath = new EditorBlock(BlockKind.Math, expr, level);
                            output.Add(inlineMath);
                            Track(inlineMath, i, i + 1, indent);
                            Close();
                            continue;
                        }
                        if (trimmedContent == "$$")
                        {
                            var end = FindMathEnd(i + 1);
                            if (end >= _lines.Length)
                                MarkOpenAtEnd();
                            var body = new List<string>();
                            for (var k = i + 1; k < end && k < _lines.Length; k++)
                                body.Add(StripIndent(_lines[k], indent).TrimEnd());
                            var math = new EditorBlock(BlockKind.Math, string.Join("\n", body), level);
                            output.Add(math);
                            Track(math, i, Math.Min(end, _lines.Length - 1) + 1, indent);
                            i = Math.Min(end, _lines.Length - 1);
                            Close();
                            continue;
                        }
                    }

                    if (indent < 4 && (string.Equals(trimmedContent, "[TOC]", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(trimmedContent, "<!-- toc -->", StringComparison.OrdinalIgnoreCase)))
                    {
                        var toc = new EditorBlock(BlockKind.Toc, string.Empty, LevelForBlock(indent));
                        output.Add(toc);
                        Track(toc, i, i + 1, indent);
                        Close();
                        continue;
                    }

                    if (content.StartsWith("<details", StringComparison.OrdinalIgnoreCase))
                    {
                        i = ParseDetails(output, i, indent);
                        Close();
                        continue;
                    }

                    if (indent < 4 && ThematicBreak.IsMatch(content.TrimEnd()))
                    {
                        var divider = new EditorBlock(BlockKind.Divider, string.Empty, LevelForBlock(indent));
                        output.Add(divider);
                        Track(divider, i, i + 1, indent);
                        Close();
                        continue;
                    }

                    var heading = indent < 4 ? AtxHeading.Match(content.TrimEnd()) : Match.Empty;
                    if (heading.Success)
                    {
                        var level = LevelForBlock(indent);
                        var hashes = heading.Groups[1].Value.Length;
                        var headingBlock = hashes <= 3
                            ? new EditorBlock(hashes == 1 ? BlockKind.Heading1 : hashes == 2 ? BlockKind.Heading2 : BlockKind.Heading3,
                                heading.Groups[2].Value, level)
                            : new EditorBlock(BlockKind.Paragraph, content.TrimEnd(), level);
                        output.Add(headingBlock);
                        Track(headingBlock, i, i + 1, indent);
                        Close();
                        continue;
                    }

                    if (TryParseListItem(output, i, indent, content))
                        continue;

                    if (content.StartsWith(">"))
                    {
                        i = ParseQuote(output, i, indent);
                        Close();
                        continue;
                    }

                    // GFM 표: 머리글 줄 다음에 구분 행(|---|:-:|)이 오면 표. 행마다 블록이 된다.
                    if (indent < 4 && content.IndexOf('|') >= 0 && i + 1 < _lines.Length && IsTableStart(content, _lines[i + 1]))
                    {
                        i = ParseTable(output, i, indent);
                        Close();
                        continue;
                    }

                    // 이전 줄에 이어지는 문단·목록 항목 본문(빈 줄 없이 온 일반 텍스트)
                    if (_open != null && !_lastWasBlank)
                    {
                        _open.Text += "\n" + content.TrimEnd();
                        Extend(_open, i);
                        continue;
                    }

                    if (content.StartsWith("|") || (content.StartsWith("<") && !content.StartsWith("</details", StringComparison.OrdinalIgnoreCase))
                        || (indent >= 4 && _listStack.Count == 0))
                    {
                        i = ParseRaw(output, i, indent);
                        Close();
                        continue;
                    }

                    var image = ImageLine.Match(content.TrimEnd());
                    if (image.Success)
                    {
                        var imageBlock = new EditorBlock(BlockKind.Image, image.Groups[1].Value, LevelForBlock(indent))
                        {
                            Source = image.Groups[2].Success ? image.Groups[2].Value : image.Groups[3].Value,
                        };
                        output.Add(imageBlock);
                        Track(imageBlock, i, i + 1, indent);
                        Close();
                        continue;
                    }

                    var card = LinkCardLine.Match(content.TrimEnd());
                    if (card.Success)
                    {
                        var label = card.Groups[2].Success ? card.Groups[2].Value : card.Groups[3].Value;
                        var cardBlock = new EditorBlock(LinkCardKind(card.Groups[1].Value), label, LevelForBlock(indent))
                        {
                            Source = card.Groups[4].Success ? card.Groups[4].Value : card.Groups[5].Value,
                        };
                        output.Add(cardBlock);
                        Track(cardBlock, i, i + 1, indent);
                        Close();
                        continue;
                    }

                    var paragraph = new EditorBlock(BlockKind.Paragraph, content.TrimEnd(), LevelForBlock(indent));
                    output.Add(paragraph);
                    Track(paragraph, i, i + 1, indent);
                    _open = paragraph;
                    _lastWasBlank = false;
                }
            }

            private void Close()
            {
                _open = null;
                _lastWasBlank = false;
            }

            /// <summary>목록 항목이 아닌 블록의 들여쓰기 수준. 들여쓰기가 부모 항목 내용 열보다 작으면 그 항목은 닫힌다.</summary>
            private int LevelForBlock(int indent)
            {
                while (_listStack.Count > 0 && indent < _listStack[_listStack.Count - 1][1])
                    _listStack.RemoveAt(_listStack.Count - 1);
                return _listStack.Count > 0 ? _listStack[_listStack.Count - 1][2] + 1 : _baseLevel;
            }

            private bool TryParseListItem(List<EditorBlock> output, int lineIndex, int indent, string content)
            {
                if (indent >= 4 && _listStack.Count == 0)
                    return false;

                BlockKind kind;
                string marker;
                string spacing;
                string rest;
                var bullet = BulletItem.Match(content);
                if (bullet.Success)
                {
                    kind = BlockKind.Bulleted;
                    marker = bullet.Groups[1].Value;
                    spacing = bullet.Groups[2].Value;
                    rest = bullet.Groups[3].Value;
                }
                else
                {
                    var ordered = OrderedItem.Match(content);
                    if (!ordered.Success)
                        return false;
                    kind = BlockKind.Numbered;
                    marker = ordered.Groups[1].Value + ordered.Groups[2].Value;
                    spacing = ordered.Groups[3].Value;
                    rest = ordered.Groups[4].Value;
                }

                var level = LevelForBlock(indent);
                var contentColumn = indent + marker.Length + (spacing.Length == 0 || spacing.Length > 4 ? 1 : spacing.Length);
                _listStack.Add(new[] { indent, contentColumn, level });

                var block = new EditorBlock(kind, rest.TrimEnd(), level);
                if (kind == BlockKind.Bulleted)
                {
                    var task = TaskPrefix.Match(block.Text);
                    if (task.Success)
                    {
                        block.Kind = BlockKind.Todo;
                        block.Checked = task.Groups[1].Value != " ";
                        block.Text = task.Groups[2].Value;
                    }
                }

                if (_sources != null)
                {
                    // 같은 목록의 첫 번호 항목인지(앞쪽에서 같은 수준의 블록이 번호 항목이 아니면 첫 항목) — 시작 번호를 지키려고 기록한다.
                    var first = true;
                    for (var k = output.Count - 1; k >= 0; k--)
                    {
                        if (output[k].Indent > level)
                            continue;
                        first = !(output[k].Indent == level && output[k].Kind == BlockKind.Numbered);
                        break;
                    }
                    var info = Track(block, lineIndex, lineIndex + 1, indent);
                    if (info != null)
                    {
                        info.ContentColumn = contentColumn;
                        info.Spacing = (byte)(spacing.Length == 0 || spacing.Length > 4 ? 1 : spacing.Length);
                        if (kind == BlockKind.Bulleted)
                        {
                            info.Bullet = marker[0];
                        }
                        else
                        {
                            info.Delim = marker[marker.Length - 1];
                            int number;
                            if (first && int.TryParse(marker.Substring(0, marker.Length - 1), out number))
                                info.StartNumber = number;
                        }
                    }
                }

                output.Add(block);
                _open = block;
                _lastWasBlank = false;
                return true;
            }

            private int ParseQuote(List<EditorBlock> output, int start, int indent)
            {
                var level = LevelForBlock(indent);
                var body = new List<string>();
                var i = start;
                for (; i < _lines.Length; i++)
                {
                    var trimmed = _lines[i].TrimStart(' ', '\t');
                    if (!trimmed.StartsWith(">"))
                        break;
                    var stripped = trimmed.Substring(1);
                    if (stripped.StartsWith(" "))
                        stripped = stripped.Substring(1);
                    body.Add(stripped.TrimEnd());
                }

                var callout = body.Count > 0 ? CalloutMarker.Match(body[0]) : Match.Empty;
                EditorBlock block;
                if (callout.Success)
                {
                    block = new EditorBlock(BlockKind.Callout, string.Join("\n", body.Skip(1)), level)
                    {
                        CalloutType = callout.Groups[1].Value.ToUpperInvariant(),
                    };
                }
                else
                {
                    block = new EditorBlock(BlockKind.Quote, string.Join("\n", body), level);
                }
                output.Add(block);
                Track(block, start, i, indent);
                return i - 1;
            }

            private static bool IsTableStart(string header, string next)
            {
                var delimiter = next.Trim();
                // 구분 행에 | 가 없으면(`---`) 구분선·Setext 제목과 헷갈리므로 표로 보지 않는다.
                if (delimiter.IndexOf('|') < 0 || delimiter.IndexOf('-') < 0 || !TableDelimiter.IsMatch(delimiter))
                    return false;
                return SplitTableRow(header).Count == SplitTableRow(delimiter).Count;
            }

            /// <summary>머리글·구분 행과 이어지는 행(빈 줄이나 <c>|</c> 없는 줄 전까지)을 표 행 블록으로 읽는다. 반환값은 마지막으로 읽은 줄.</summary>
            private int ParseTable(List<EditorBlock> output, int start, int indent)
            {
                var level = LevelForBlock(indent);
                var align = SplitTableRow(_lines[start + 1].Trim()).Select(cell =>
                {
                    var left = cell.StartsWith(":");
                    var right = cell.EndsWith(":");
                    return left && right ? "c" : right ? "r" : left ? "l" : string.Empty;
                });
                var header = new EditorBlock(BlockKind.TableRow, JoinCells(SplitTableRow(_lines[start])), level)
                {
                    TableAlign = string.Join(",", align),
                };
                output.Add(header);
                Track(header, start, start + 2, indent);   // 머리글 행 + 구분 행

                var i = start + 2;
                for (; i < _lines.Length; i++)
                {
                    var line = _lines[i];
                    if (string.IsNullOrWhiteSpace(line) || line.IndexOf('|') < 0)
                        break;
                    var row = new EditorBlock(BlockKind.TableRow, JoinCells(SplitTableRow(line)), level);
                    output.Add(row);
                    Track(row, i, i + 1, CountIndent(line));
                }
                return i - 1;
            }

            private int ParseRaw(List<EditorBlock> output, int start, int indent)
            {
                var level = indent >= 4 ? _baseLevel : LevelForBlock(indent);
                var body = new List<string>();
                var i = start;
                for (; i < _lines.Length && !string.IsNullOrWhiteSpace(_lines[i]); i++)
                    body.Add(indent >= 4 ? _lines[i].TrimEnd() : StripIndent(_lines[i], indent).TrimEnd());
                var raw = new EditorBlock(BlockKind.Raw, string.Join("\n", body), level);
                output.Add(raw);
                // 4칸 이상 들여쓴 원문은 들여쓰기를 본문에 담으므로 열 위치는 기록하지 않는다.
                Track(raw, start, i, indent >= 4 ? -1 : indent);
                return i - 1;
            }

            /// <summary><c>&lt;details&gt;</c> … <c>&lt;/details&gt;</c>를 토글 블록과 그 자식 블록으로 읽는다. 반환값은 마지막으로 읽은 줄.</summary>
            private int ParseDetails(List<EditorBlock> output, int start, int indent)
            {
                var level = LevelForBlock(indent);
                var depth = 0;
                var close = -1;
                for (var k = start; k < _lines.Length; k++)
                {
                    var t = _lines[k].Trim();
                    depth += CountOccurrences(t, "<details");
                    depth -= CountOccurrences(t, "</details>");
                    if (depth <= 0)
                    {
                        close = k;
                        break;
                    }
                }
                if (close < 0)
                {
                    close = _lines.Length;
                    MarkOpenAtEnd();
                }

                // 제목: <summary>…</summary>. 같은 줄 또는 바로 아래 줄들에 있다.
                var title = string.Empty;
                var bodyStart = start + 1;
                var openTag = _lines[start];
                for (var k = start; k <= close && k < _lines.Length && k <= start + 3; k++)
                {
                    var s = _lines[k].IndexOf("<summary>", StringComparison.OrdinalIgnoreCase);
                    if (s < 0)
                        continue;
                    var e = _lines[k].IndexOf("</summary>", s, StringComparison.OrdinalIgnoreCase);
                    title = e > s ? _lines[k].Substring(s + 9, e - s - 9) : _lines[k].Substring(s + 9);
                    bodyStart = k + 1;
                    break;
                }

                var toggle = new EditorBlock(BlockKind.Toggle, title.Trim(), level)
                {
                    Collapsed = openTag.IndexOf(" open", StringComparison.OrdinalIgnoreCase) < 0,
                };
                output.Add(toggle);

                var inner = new List<string>();
                var innerMap = _sources != null ? new List<int>() : null;
                for (var k = bodyStart; k < close && k < _lines.Length; k++)
                {
                    inner.Add(StripIndent(_lines[k], indent));
                    innerMap?.Add(DocLine(k));
                }
                // 닫는 태그 앞에 내용이 붙어 있으면(`text</details>`) 그 부분도 자식으로 읽는다.
                var closingHasContent = false;
                if (close < _lines.Length && close >= bodyStart)
                {
                    var closingLine = StripIndent(_lines[close], indent);
                    var tag = closingLine.LastIndexOf("</details>", StringComparison.OrdinalIgnoreCase);
                    if (tag > 0 && closingLine.Substring(0, tag).Trim().Length > 0)
                    {
                        inner.Add(closingLine.Substring(0, tag));
                        innerMap?.Add(-1);   // 원문 줄 하나를 자식과 닫는 태그가 나눠 쓰므로 그 자식은 원문으로 쓰지 않는다
                        closingHasContent = true;
                    }
                }
                // 여는 줄(+ summary 줄)과 닫는 줄을 따로 기록한다. 한 줄 토글·닫히지 않은 토글 등 줄을 나눌 수 없으면 기록하지 않는다(늘 정규형).
                if (_sources != null && close < _lines.Length && close >= bodyStart && !closingHasContent)
                {
                    var head = Track(toggle, start, bodyStart, indent);
                    var tail = DocLine(close);
                    if (head != null && tail >= 0)
                    {
                        head.TailStart = tail;
                        head.TailEnd = tail + 1;
                    }
                    else if (head != null)
                    {
                        _sources.Remove(toggle);
                    }
                }
                new Parser(inner.ToArray(), _record, innerMap?.ToArray()).Parse(output, level + 1, false);
                return Math.Min(close, _lines.Length - 1);
            }

            private int FindFrontMatterEnd(int from)
            {
                for (var k = from; k < _lines.Length; k++)
                {
                    var t = _lines[k].TrimEnd();
                    if (t == "---" || t == "...")
                        return k;
                }
                return -1;
            }

            private int FindFenceEnd(int from, string fence)
            {
                for (var k = from; k < _lines.Length; k++)
                {
                    var t = _lines[k].Trim();
                    if (t.Length >= fence.Length && t.All(c => c == fence[0]))
                        return k;
                }
                return _lines.Length;
            }

            private int FindMathEnd(int from)
            {
                for (var k = from; k < _lines.Length; k++)
                {
                    if (_lines[k].Trim() == "$$")
                        return k;
                }
                return _lines.Length;
            }
        }

        /// <summary>표 한 줄을 셀로 나눈다. 앞뒤 <c>|</c>는 떼고 <c>\|</c>는 셀 안의 글자로 본다.</summary>
        public static List<string> SplitTableRow(string line)
        {
            var text = (line ?? string.Empty).Trim();
            if (text.StartsWith("|"))
                text = text.Substring(1);
            if (text.EndsWith("|") && !text.EndsWith("\\|"))
                text = text.Substring(0, text.Length - 1);

            var cells = new List<string>();
            var cell = new StringBuilder();
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] == '|')
                {
                    cell.Append('|');
                    i++;
                }
                else if (text[i] == '|')
                {
                    cells.Add(cell.ToString().Trim());
                    cell.Clear();
                }
                else
                {
                    cell.Append(text[i] == EditorBlock.CellSeparator ? ' ' : text[i]);
                }
            }
            cells.Add(cell.ToString().Trim());
            return cells;
        }

        /// <summary>셀 목록을 표 행 블록의 본문(탭 구분)으로 합친다. 셀 안의 탭·줄바꿈은 공백으로 바꾼다.</summary>
        public static string JoinCells(IEnumerable<string> cells)
        {
            return string.Join(EditorBlock.CellSeparator.ToString(),
                cells.Select(c => (c ?? string.Empty).Replace(EditorBlock.CellSeparator, ' ').Replace("\r", string.Empty).Replace('\n', ' ')));
        }

        public static string[] SplitCells(string text)
        {
            return (text ?? string.Empty).Split(EditorBlock.CellSeparator);
        }

        private static string FenceOf(string content)
        {
            if (content.Length < 3 || (content[0] != '`' && content[0] != '~'))
                return null;
            var n = 0;
            while (n < content.Length && content[n] == content[0])
                n++;
            if (n < 3)
                return null;
            // 백틱 펜스의 정보 문자열에는 백틱이 올 수 없다(줄 안 코드 `` `a` `` 와 구분).
            if (content[0] == '`' && content.IndexOf('`', n) >= 0)
                return null;
            return content.Substring(0, n);
        }

        private static int CountIndent(string line)
        {
            var n = 0;
            foreach (var c in line)
            {
                if (c == ' ') n++;
                else if (c == '\t') n += 4 - (n % 4);
                else break;
            }
            return n;
        }

        /// <summary>앞 공백을 최대 <paramref name="count"/>칸까지 지운다(탭은 4칸).</summary>
        private static string StripIndent(string line, int count)
        {
            var removed = 0;
            var i = 0;
            while (i < line.Length && removed < count)
            {
                if (line[i] == ' ') removed++;
                else if (line[i] == '\t') removed += 4 - (removed % 4);
                else break;
                i++;
            }
            return line.Substring(i);
        }

        private static int CountOccurrences(string text, string token)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(token, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                count++;
                index += token.Length;
            }
            return count;
        }

        // ── 쓰기 ───────────────────────────────────────────────────────────

        /// <summary>블록 목록을 Markdown으로 쓴다. 가장 얕은 블록을 맨 바깥 수준으로 본다(일부 블록 복사에도 쓴다).</summary>
        public static string Serialize(IList<EditorBlock> blocks)
        {
            if (blocks == null || blocks.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            Write(sb, blocks, 0, blocks.Count, blocks.Min(b => b.Indent), string.Empty, null);
            var text = sb.ToString().TrimEnd('\n');
            return text.Length == 0 ? string.Empty : text + "\n";
        }

        /// <summary>
        /// <see cref="Serialize"/>와 같은 순서로 쓰되 블록 조각(원문 구간 단위)마다 <paramref name="writer"/>에 알린다(<see cref="MarkdownSource"/>).
        /// 원래 있던 블록은 원문의 목록 표식·시작 번호·들여쓰기·Setext 제목 형식을 따른다.
        /// </summary>
        internal static void WritePieces(StringBuilder sb, IList<EditorBlock> blocks, PieceWriter writer)
        {
            if (blocks.Count > 0)
                Write(sb, blocks, 0, blocks.Count, blocks.Min(b => b.Indent), string.Empty, writer);
        }

        /// <summary>목록 항목을 쓴 형식(같은 수준의 다음 새 항목이 이어받는다).</summary>
        private sealed class ListStyle
        {
            public int Column;
            public char Bullet;
            public char Delim;
            public int Spacing;
        }

        /// <summary>블록을 쓰고 나서 쓴 블록이 하나라도 있으면 true(토글 자식 뒤 빈 줄 판단).</summary>
        private static bool Write(StringBuilder sb, IList<EditorBlock> blocks, int start, int end, int baseIndent, string prefix, PieceWriter writer)
        {
            var contentColumns = new Dictionary<int, int>();   // 수준별 마지막 목록 항목의 내용 시작 열
            var numbers = new Dictionary<int, int>();          // 수준별 번호 목록 카운터
            var styles = writer != null ? new Dictionary<int, ListStyle>() : null;   // 수준별 바로 앞 목록 항목 형식
            BlockKind? previous = null;
            var previousLevel = -1;
            var tableColumns = 0;                              // 지금 쓰는 표의 열 수
            var wroteAny = false;

            for (var i = start; i < end; i++)
            {
                var block = blocks[i];
                var level = Math.Max(0, block.Indent - baseIndent);

                // 빈 문단은 Markdown으로 나타낼 수 없어 쓰지 않는다.
                if (!IsWritten(block))
                    continue;
                wroteAny = true;

                // 같은 수준 앞 목록 항목의 내용 열: 이 블록이 그 열 이상으로 들여쓰면 그 항목 안으로 들어가 버린다.
                int escape;
                if (!contentColumns.TryGetValue(level, out escape))
                    escape = int.MaxValue;

                RemoveLevels(numbers, level + 1);
                RemoveLevels(contentColumns, level);
                if (styles != null && styles.Count > 0)
                {
                    RemoveLevels(styles, level + 1);
                    if (!EditorBlock.IsListItem(block.Kind))
                        styles.Remove(level);
                }

                // 목록 항목 아래 자식은 부모 항목의 내용 시작 열만큼 들여쓴다. 목록이 아닌 부모 아래면 들여쓰지 않는다
                // (Markdown에서 4칸 들여쓴 문단은 코드 블록이 되므로).
                var column = 0;
                for (var parent = level - 1; parent >= 0; parent--)
                {
                    int col;
                    if (contentColumns.TryGetValue(parent, out col))
                    {
                        column = col;
                        break;
                    }
                }

                // 원문 형식: 원래 들여쓰기(부모 안·코드 블록이 되지 않는 범위), 없으면 같은 목록 앞 항목의 들여쓰기
                var hint = writer?.Hint(i, block);
                ListStyle sibling = null;
                if (writer != null)
                {
                    if (EditorBlock.IsListItem(block.Kind))
                        styles.TryGetValue(level, out sibling);
                    var max = Math.Min(column + 3, escape - 1);
                    if (hint != null && hint.Column >= column && hint.Column <= max)
                        column = hint.Column;
                    else if (sibling != null && sibling.Column >= column && sibling.Column <= max)
                        column = sibling.Column;
                }
                string indent = null;   // prefix + 열만큼 공백 — 실제로 쓸 때만 만든다
                var context = new PieceContext { Indent = prefix.Length + column, Number = -1 };

                var continuesTable = block.Kind == BlockKind.TableRow && previous == BlockKind.TableRow && previousLevel == level;
                if (previous != null && !continuesTable && !(EditorBlock.IsListItem(previous.Value) && EditorBlock.IsListItem(block.Kind)))
                    sb.Append('\n');

                // 문맥(번호·목록 표식·표 열 수): 조각 글은 블록 내용과 이 문맥만으로 정해진다.
                ListStyle written = null;
                string marker = null;
                var numberLength = 0;   // 번호 목록 두 번째 항목부터: 번호 글자 수(조각 비교에서 번호는 빼고 본다)
                if (EditorBlock.IsListItem(block.Kind))
                {
                    var spacing = 1;
                    if (writer != null)
                    {
                        written = new ListStyle
                        {
                            Column = column,
                            Bullet = hint != null && hint.Bullet != '\0' ? hint.Bullet : sibling != null && sibling.Bullet != '\0' ? sibling.Bullet : '-',
                            Delim = hint != null && hint.Delim != '\0' ? hint.Delim : sibling != null && sibling.Delim != '\0' ? sibling.Delim : '.',
                            Spacing = hint != null && hint.Spacing > 0 ? hint.Spacing : sibling != null && sibling.Spacing > 0 ? sibling.Spacing : 1,
                        };
                        spacing = written.Spacing;
                    }
                    if (block.Kind == BlockKind.Numbered)
                    {
                        int n;
                        if (numbers.TryGetValue(level, out n))
                        {
                            n++;
                            numberLength = n.ToString().Length;
                        }
                        else
                        {
                            n = hint != null && hint.StartNumber >= 0 ? hint.StartNumber : 1;
                        }
                        numbers[level] = n;
                        context.Marker = written != null ? written.Delim : '.';
                        context.Number = numberLength > 0 ? -1 : n;
                        marker = n + context.Marker.ToString();
                    }
                    else
                    {
                        context.Marker = written != null ? written.Bullet : '-';
                        marker = context.Marker.ToString();
                    }
                    context.Spacing = (byte)spacing;
                    context.Digits = (byte)numberLength;
                    marker += new string(' ', spacing);
                }
                else if (block.Kind == BlockKind.TableRow)
                {
                    if (!continuesTable)
                    {
                        // 표의 첫 행이 머리글. 열 수는 표 전체에서 가장 많은 셀 수.
                        tableColumns = 0;
                        for (var k = i; k < end && blocks[k].Kind == BlockKind.TableRow && blocks[k].Indent == block.Indent; k++)
                            tableColumns = Math.Max(tableColumns, CountCells(blocks[k].Text));
                    }
                    context.TableColumns = tableColumns;
                    context.ContinuesTable = continuesTable;
                }

                var pieceStart = sb.Length;
                if (block.Kind == BlockKind.Toggle)
                {
                    indent = prefix + new string(' ', column);
                    if ((writer?.Begin(i, block, 0, sb, context) ?? PieceWriter.Write) == PieceWriter.Write)
                    {
                        RenderToggleHead(sb, indent, block.Collapsed, block.Text);
                        writer?.Piece(i, block, 0, sb, pieceStart, 0, 0);
                    }
                    sb.Append('\n');
                    var childEnd = Math.Min(end, BlockOperations.SubtreeEnd(blocks, i));
                    if (childEnd > i + 1 && Write(sb, blocks, i + 1, childEnd, block.Indent + 1, indent, writer))
                        sb.Append('\n');
                    var tailStart = sb.Length;
                    if ((writer?.Begin(i, block, 1, sb, context) ?? PieceWriter.Write) == PieceWriter.Write)
                    {
                        sb.Append(indent).Append("</details>\n");
                        writer?.Piece(i, block, 1, sb, tailStart, 0, 0);
                    }
                    i = childEnd - 1;
                }
                else
                {
                    var action = writer?.Begin(i, block, 0, sb, context) ?? PieceWriter.Write;
                    bool preserved;
                    if (action == PieceWriter.Write)
                    {
                        indent = prefix + new string(' ', column);
                        RenderBody(sb, indent, block.Kind, block.Text, block.Checked, ExtraOf(block), hint?.Setext, marker, tableColumns, continuesTable);
                        preserved = writer != null && writer.Piece(i, block, 0, sb, pieceStart, indent.Length, numberLength);
                    }
                    else
                    {
                        preserved = action == PieceWriter.SkipPreserved;
                    }

                    if (marker != null)
                    {
                        contentColumns[level] = column + marker.Length;
                        if (writer != null)
                        {
                            // 원문 그대로 쓰는 항목은 원문의 내용 열을 자식이 따른다(번호 자릿수 등이 정규형과 달라도).
                            if (preserved && hint != null && hint.ContentColumn >= 0)
                                contentColumns[level] = hint.ContentColumn;
                            styles[level] = written;
                        }
                    }
                }

                // 같은 수준에 번호 목록이 아닌 블록이 끼면 번호는 1부터 다시 시작한다(BlockOperations.ListLabels와 같은 규칙).
                if (block.Kind != BlockKind.Numbered)
                    numbers.Remove(level);
                previous = block.Kind;
                previousLevel = level;
            }
            return wroteAny;
        }

        /// <summary>수준 <paramref name="from"/> 이상의 항목을 지운다.</summary>
        private static void RemoveLevels<T>(Dictionary<int, T> map, int from)
        {
            if (map.Count == 0)
                return;
            List<int> remove = null;
            foreach (var key in map.Keys)
            {
                if (key >= from)
                    (remove ??= new List<int>()).Add(key);
            }
            if (remove != null)
            {
                foreach (var key in remove)
                    map.Remove(key);
            }
        }

        /// <summary>쓰기에 쓰이는 종류별 속성(코드 언어·콜아웃 종류·이미지/카드 경로·표 머리글 정렬). 그 밖의 종류는 null.</summary>
        internal static string ExtraOf(EditorBlock block)
        {
            switch (block.Kind)
            {
                case BlockKind.Code: return block.Language;
                case BlockKind.Callout: return block.CalloutType;
                case BlockKind.Image:
                case BlockKind.File:
                case BlockKind.Bookmark:
                case BlockKind.Page: return block.Source;
                case BlockKind.TableRow: return block.TableAlign;
                default: return null;
            }
        }

        internal static void RenderToggleHead(StringBuilder sb, string indent, bool collapsed, string text)
        {
            sb.Append(indent).Append(collapsed ? "<details>" : "<details open>").Append('\n');
            sb.Append(indent).Append("<summary>").Append(OneLine(text)).Append("</summary>\n");
        }

        /// <summary>
        /// 토글이 아닌 블록 하나의 조각 글. <paramref name="extra"/>는 <see cref="ExtraOf"/>, <paramref name="marker"/>는 목록 표식(번호·기호 + 뒤 공백),
        /// 이어지는 줄은 표식 뒤 열에 맞춘다. 내용과 문맥(<see cref="PieceContext"/>)만으로 정해진다(<see cref="MarkdownSource"/>가 읽을 때 글을 다시 만들 때도 씀).
        /// </summary>
        internal static void RenderBody(StringBuilder sb, string indent, BlockKind kind, string text, bool isChecked, string extra, string setext,
            string marker, int tableColumns, bool continuesTable)
        {
            text = text ?? string.Empty;
            switch (kind)
            {
                case BlockKind.Heading1:
                case BlockKind.Heading2:
                case BlockKind.Heading3:
                    if (setext != null && kind != BlockKind.Heading3 && setext[0] == (kind == BlockKind.Heading1 ? '=' : '-')
                        && OneLine(text).Trim().Length > 0)
                    {
                        sb.Append(indent).Append(OneLine(text)).Append('\n').Append(indent).Append(setext).Append('\n');
                        break;
                    }
                    var hashes = kind == BlockKind.Heading1 ? "# " : kind == BlockKind.Heading2 ? "## " : "### ";
                    sb.Append(indent).Append(hashes).Append(OneLine(text)).Append('\n');
                    break;

                case BlockKind.Bulleted:
                case BlockKind.Numbered:
                case BlockKind.Todo:
                    var first = kind == BlockKind.Todo ? (isChecked ? "[x] " : "[ ] ") : string.Empty;
                    AppendLines(sb, indent + marker + first, new string(' ', indent.Length + marker.Length), text);
                    break;

                case BlockKind.Quote:
                    foreach (var line in SplitLines(text))
                        sb.Append(indent).Append(line.Length == 0 ? ">" : "> " + line).Append('\n');
                    break;

                case BlockKind.Callout:
                    sb.Append(indent).Append("> [!").Append(NormalizeCalloutType(extra)).Append("]\n");
                    if (text.Length > 0)
                        foreach (var line in SplitLines(text))
                            sb.Append(indent).Append(line.Length == 0 ? ">" : "> " + line).Append('\n');
                    break;

                case BlockKind.Code:
                    var fence = new string('`', Math.Max(3, LongestBacktickRun(text) + 1));
                    sb.Append(indent).Append(fence).Append(OneLine(extra).Trim()).Append('\n');
                    if (text.Length > 0)
                        foreach (var line in SplitLines(text))
                            sb.Append(line.Length == 0 ? string.Empty : indent + line).Append('\n');
                    sb.Append(indent).Append(fence).Append('\n');
                    break;

                case BlockKind.Divider:
                    sb.Append(indent).Append("---\n");
                    break;

                case BlockKind.Raw:
                case BlockKind.FrontMatter:
                    foreach (var line in SplitLines(text))
                        sb.Append(line.Length == 0 ? string.Empty : indent + line).Append('\n');
                    break;

                case BlockKind.TableRow:
                    AppendTableRow(sb, indent, SplitCells(text), tableColumns);
                    if (!continuesTable)
                    {
                        var aligns = (extra ?? string.Empty).Split(',');
                        sb.Append(indent).Append('|');
                        for (var c = 0; c < tableColumns; c++)
                        {
                            var a = c < aligns.Length ? aligns[c] : string.Empty;
                            sb.Append(a == "c" ? " :---: |" : a == "r" ? " ---: |" : a == "l" ? " :--- |" : " --- |");
                        }
                        sb.Append('\n');
                    }
                    break;

                case BlockKind.Image:
                    var source = extra ?? string.Empty;
                    if (source.IndexOfAny(new[] { ' ', '(', ')' }) >= 0)
                        source = "<" + source + ">";
                    sb.Append(indent).Append("![").Append(OneLine(text).Replace("]", "\\]")).Append("](").Append(source).Append(")\n");
                    break;

                case BlockKind.Toc:
                    sb.Append(indent).Append("[TOC]\n");
                    break;

                case BlockKind.Math:
                    sb.Append(indent).Append("$$\n");
                    if (text.Length > 0)
                        foreach (var line in SplitLines(text))
                            sb.Append(line.Length == 0 ? string.Empty : indent + line).Append('\n');
                    sb.Append(indent).Append("$$\n");
                    break;

                case BlockKind.File:
                case BlockKind.Bookmark:
                case BlockKind.Page:
                    var cardSource = extra ?? string.Empty;
                    if (cardSource.IndexOfAny(new[] { ' ', '(', ')' }) >= 0)
                        cardSource = "<" + cardSource + ">";
                    var cardLabel = OneLine(text).Replace("]", "\\]");
                    var cardIcon = LinkCardIcon(kind);
                    sb.Append(indent).Append(cardLabel.Length > 0 ? "[" + cardIcon + " " + cardLabel + "](" : "[" + cardIcon + "](").Append(cardSource).Append(")\n");
                    break;

                default:
                    AppendLines(sb, indent, indent, text);
                    break;
            }
        }

        /// <summary>Markdown에 쓰이는 블록인지(빈 문단은 쓰지 않는다). 사이드카가 블록 위치를 셀 때도 같은 규칙을 쓴다.</summary>
        public static bool IsWritten(EditorBlock block)
        {
            return !(block.Kind == BlockKind.Paragraph && block.Text.Trim().Length == 0);
        }

        private static void AppendTableRow(StringBuilder sb, string indent, string[] cells, int columns)
        {
            sb.Append(indent).Append('|');
            for (var c = 0; c < columns; c++)
            {
                var cell = c < cells.Length ? cells[c].Replace("|", "\\|") : string.Empty;
                sb.Append(' ').Append(cell).Append(cell.Length > 0 ? " |" : "|");
            }
            sb.Append('\n');
        }

        /// <summary>표 행 블록의 셀 수(탭 구분자 수 + 1).</summary>
        public static int CountCells(string text)
        {
            var count = 1;
            foreach (var c in text ?? string.Empty)
                if (c == EditorBlock.CellSeparator)
                    count++;
            return count;
        }

        private static void AppendLines(StringBuilder sb, string firstPrefix, string nextPrefix, string text)
        {
            var lines = SplitLines(text);
            for (var i = 0; i < lines.Length; i++)
                sb.Append(i == 0 ? firstPrefix : nextPrefix).Append(lines[i]).Append('\n');
        }

        private static string[] SplitLines(string text)
        {
            return (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        }

        private static string OneLine(string text)
        {
            return (text ?? string.Empty).Replace("\r\n", " ").Replace('\n', ' ');
        }

        /// <summary>링크 카드 줄의 아이콘(<c>📎</c> 파일·<c>🔖</c> 북마크·<c>📄</c> 페이지).</summary>
        private static string LinkCardIcon(BlockKind kind)
        {
            return kind == BlockKind.Bookmark ? "🔖" : kind == BlockKind.Page ? "📄" : "📎";
        }

        private static BlockKind LinkCardKind(string icon)
        {
            return icon == "🔖" ? BlockKind.Bookmark : icon == "📄" ? BlockKind.Page : BlockKind.File;
        }

        private static int LongestBacktickRun(string text)
        {
            var longest = 0;
            foreach (var line in SplitLines(text))
            {
                var t = line.TrimStart();
                var n = 0;
                while (n < t.Length && t[n] == '`')
                    n++;
                longest = Math.Max(longest, n);
            }
            return longest;
        }

        public static string NormalizeCalloutType(string type)
        {
            var upper = (type ?? string.Empty).Trim().ToUpperInvariant();
            return upper.Length == 0 ? "NOTE" : upper;
        }

        // ── 입력 단축 ──────────────────────────────────────────────────────

        /// <summary>
        /// 문단 맨 앞에 입력한 Markdown 표식(커서 앞 글자 전부)을 블록 종류로 바꾼다. Notion과 같은 자동 변환:
        /// <c># </c> 제목 1, <c>## </c>, <c>### </c>, <c>- </c>/<c>* </c>/<c>+ </c> 글머리 기호, <c>1. </c> 번호, <c>[] </c>/<c>[x] </c> 할 일,
        /// <c>&gt; </c>·<c>" </c> 인용, <c>``` </c>(백틱 셋) 코드, <c>---</c> 구분선.
        /// </summary>
        public static bool TryMatchShortcut(string prefix, out BlockKind kind, out bool isChecked)
        {
            isChecked = false;
            kind = BlockKind.Paragraph;
            switch (prefix)
            {
                case "# ": kind = BlockKind.Heading1; return true;
                case "## ": kind = BlockKind.Heading2; return true;
                case "### ": kind = BlockKind.Heading3; return true;
                case "- ":
                case "* ":
                case "+ ": kind = BlockKind.Bulleted; return true;
                case "[] ":
                case "[ ] ": kind = BlockKind.Todo; return true;
                case "[x] ":
                case "[X] ": kind = BlockKind.Todo; isChecked = true; return true;
                case "> ":
                case "\" ": kind = BlockKind.Quote; return true;
                case "```": kind = BlockKind.Code; return true;
                case "$$":
                case "$$ ": kind = BlockKind.Math; return true;
                case "[TOC] ":
                case "[toc] ": kind = BlockKind.Toc; return true;
                case "---": kind = BlockKind.Divider; return true;
            }
            if (prefix != null && NumberedShortcut.IsMatch(prefix))
            {
                kind = BlockKind.Numbered;
                return true;
            }
            return false;
        }
    }
}

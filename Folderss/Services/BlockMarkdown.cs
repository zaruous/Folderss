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
    /// <item>front matter·표·HTML 블록처럼 편집기가 해석하지 않는 부분은 <see cref="BlockKind.Raw"/>로 원문을 보존한다.</item>
    /// <item>저장하면 형식이 정규화된다(글머리 기호 <c>-</c>, 번호 다시 매김, 블록 사이 빈 줄 하나, 빈 문단은 쓰지 않음).</item>
    /// </list>
    /// </summary>
    public static class BlockMarkdown
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

        public static readonly string[] CalloutTypes = { "NOTE", "TIP", "IMPORTANT", "WARNING", "CAUTION" };

        // ── 읽기 ───────────────────────────────────────────────────────────

        public static List<EditorBlock> Parse(string markdown)
        {
            var text = (markdown ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            if (text.Length > 0 && text[0] == '﻿')
                text = text.Substring(1);
            var lines = text.Split('\n');
            var blocks = new List<EditorBlock>();
            new Parser(lines).Parse(blocks, 0, true);
            BlockOperations.NormalizeIndents(blocks);
            return blocks;
        }

        private sealed class Parser
        {
            private readonly string[] _lines;

            // 열려 있는 목록 항목: 표식 열, 내용 시작 열, 블록 들여쓰기 수준
            private readonly List<int[]> _listStack = new List<int[]>();
            private EditorBlock _open;   // 다음 줄이 이어 붙을 수 있는 문단·목록 항목
            private bool _lastWasBlank = true;
            private int _baseLevel;

            public Parser(string[] lines)
            {
                _lines = lines;
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

                    if (allowFrontMatter && i == 0 && content.TrimEnd() == "---")
                    {
                        var close = FindFrontMatterEnd(1);
                        if (close > 0)
                        {
                            output.Add(new EditorBlock(BlockKind.Raw, string.Join("\n", _lines, 0, close + 1)));
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
                            Close();
                            continue;
                        }
                    }

                    var fence = FenceOf(content);
                    if (fence != null)
                    {
                        var level = LevelForBlock(indent);
                        var end = FindFenceEnd(i + 1, fence);
                        var body = new List<string>();
                        for (var k = i + 1; k < end && k < _lines.Length; k++)
                            body.Add(StripIndent(_lines[k], indent));
                        output.Add(new EditorBlock(BlockKind.Code, string.Join("\n", body), level)
                        {
                            Language = content.Substring(fence.Length).Trim(),
                        });
                        i = Math.Min(end, _lines.Length - 1);
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
                        output.Add(new EditorBlock(BlockKind.Divider, string.Empty, LevelForBlock(indent)));
                        Close();
                        continue;
                    }

                    var heading = indent < 4 ? AtxHeading.Match(content.TrimEnd()) : Match.Empty;
                    if (heading.Success)
                    {
                        var level = LevelForBlock(indent);
                        var hashes = heading.Groups[1].Value.Length;
                        output.Add(hashes <= 3
                            ? new EditorBlock(hashes == 1 ? BlockKind.Heading1 : hashes == 2 ? BlockKind.Heading2 : BlockKind.Heading3,
                                heading.Groups[2].Value, level)
                            : new EditorBlock(BlockKind.Paragraph, content.TrimEnd(), level));
                        Close();
                        continue;
                    }

                    if (TryParseListItem(output, line, indent, content))
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
                        output.Add(new EditorBlock(BlockKind.Image, image.Groups[1].Value, LevelForBlock(indent))
                        {
                            Source = image.Groups[2].Success ? image.Groups[2].Value : image.Groups[3].Value,
                        });
                        Close();
                        continue;
                    }

                    var paragraph = new EditorBlock(BlockKind.Paragraph, content.TrimEnd(), LevelForBlock(indent));
                    output.Add(paragraph);
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

            private bool TryParseListItem(List<EditorBlock> output, string line, int indent, string content)
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
                if (callout.Success)
                {
                    output.Add(new EditorBlock(BlockKind.Callout, string.Join("\n", body.Skip(1)), level)
                    {
                        CalloutType = callout.Groups[1].Value.ToUpperInvariant(),
                    });
                }
                else
                {
                    output.Add(new EditorBlock(BlockKind.Quote, string.Join("\n", body), level));
                }
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
                output.Add(new EditorBlock(BlockKind.TableRow, JoinCells(SplitTableRow(_lines[start])), level)
                {
                    TableAlign = string.Join(",", align),
                });

                var i = start + 2;
                for (; i < _lines.Length; i++)
                {
                    var line = _lines[i];
                    if (string.IsNullOrWhiteSpace(line) || line.IndexOf('|') < 0)
                        break;
                    output.Add(new EditorBlock(BlockKind.TableRow, JoinCells(SplitTableRow(line)), level));
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
                output.Add(new EditorBlock(BlockKind.Raw, string.Join("\n", body), level));
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
                    close = _lines.Length;

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
                for (var k = bodyStart; k < close && k < _lines.Length; k++)
                    inner.Add(StripIndent(_lines[k], indent));
                // 닫는 태그 앞에 내용이 붙어 있으면(`text</details>`) 그 부분도 자식으로 읽는다.
                if (close < _lines.Length && close >= bodyStart)
                {
                    var closingLine = StripIndent(_lines[close], indent);
                    var tag = closingLine.LastIndexOf("</details>", StringComparison.OrdinalIgnoreCase);
                    if (tag > 0 && closingLine.Substring(0, tag).Trim().Length > 0)
                        inner.Add(closingLine.Substring(0, tag));
                }
                new Parser(inner.ToArray()).Parse(output, level + 1, false);
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
            Write(sb, blocks, 0, blocks.Count, blocks.Min(b => b.Indent), string.Empty);
            var text = sb.ToString().TrimEnd('\n');
            return text.Length == 0 ? string.Empty : text + "\n";
        }

        private static void Write(StringBuilder sb, IList<EditorBlock> blocks, int start, int end, int baseIndent, string prefix)
        {
            var contentColumns = new Dictionary<int, int>();   // 수준별 마지막 목록 항목의 내용 시작 열
            var numbers = new Dictionary<int, int>();          // 수준별 번호 목록 카운터
            BlockKind? previous = null;
            var previousLevel = -1;
            var tableColumns = 0;                              // 지금 쓰는 표의 열 수

            for (var i = start; i < end; i++)
            {
                var block = blocks[i];
                var level = Math.Max(0, block.Indent - baseIndent);

                // 빈 문단은 Markdown으로 나타낼 수 없어 쓰지 않는다.
                if (!IsWritten(block))
                    continue;

                foreach (var key in numbers.Keys.Where(k => k > level).ToList())
                    numbers.Remove(key);
                foreach (var key in contentColumns.Keys.Where(k => k >= level).ToList())
                    contentColumns.Remove(key);

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
                var indent = prefix + new string(' ', column);

                var continuesTable = block.Kind == BlockKind.TableRow && previous == BlockKind.TableRow && previousLevel == level;
                if (previous != null && !continuesTable && !(EditorBlock.IsListItem(previous.Value) && EditorBlock.IsListItem(block.Kind)))
                    sb.Append('\n');

                switch (block.Kind)
                {
                    case BlockKind.Heading1:
                    case BlockKind.Heading2:
                    case BlockKind.Heading3:
                        var hashes = block.Kind == BlockKind.Heading1 ? "# " : block.Kind == BlockKind.Heading2 ? "## " : "### ";
                        sb.Append(indent).Append(hashes).Append(OneLine(block.Text)).Append('\n');
                        break;

                    case BlockKind.Bulleted:
                    case BlockKind.Numbered:
                    case BlockKind.Todo:
                        string marker;
                        if (block.Kind == BlockKind.Numbered)
                        {
                            int n;
                            numbers.TryGetValue(level, out n);
                            numbers[level] = ++n;
                            marker = n + ". ";
                        }
                        else
                        {
                            marker = "- ";
                        }
                        var itemColumn = column + marker.Length;
                        var first = block.Kind == BlockKind.Todo ? (block.Checked ? "[x] " : "[ ] ") : string.Empty;
                        AppendLines(sb, indent + marker + first, prefix + new string(' ', itemColumn), block.Text);
                        contentColumns[level] = itemColumn;
                        break;

                    case BlockKind.Quote:
                        foreach (var line in SplitLines(block.Text))
                            sb.Append(indent).Append(line.Length == 0 ? ">" : "> " + line).Append('\n');
                        break;

                    case BlockKind.Callout:
                        sb.Append(indent).Append("> [!").Append(NormalizeCalloutType(block.CalloutType)).Append("]\n");
                        if (block.Text.Length > 0)
                            foreach (var line in SplitLines(block.Text))
                                sb.Append(indent).Append(line.Length == 0 ? ">" : "> " + line).Append('\n');
                        break;

                    case BlockKind.Code:
                        var fence = new string('`', Math.Max(3, LongestBacktickRun(block.Text) + 1));
                        sb.Append(indent).Append(fence).Append(OneLine(block.Language).Trim()).Append('\n');
                        if (block.Text.Length > 0)
                            foreach (var line in SplitLines(block.Text))
                                sb.Append(line.Length == 0 ? string.Empty : indent + line).Append('\n');
                        sb.Append(indent).Append(fence).Append('\n');
                        break;

                    case BlockKind.Divider:
                        sb.Append(indent).Append("---\n");
                        break;

                    case BlockKind.Raw:
                        foreach (var line in SplitLines(block.Text))
                            sb.Append(line.Length == 0 ? string.Empty : indent + line).Append('\n');
                        break;

                    case BlockKind.TableRow:
                        if (!continuesTable)
                        {
                            // 표의 첫 행이 머리글. 열 수는 표 전체에서 가장 많은 셀 수.
                            tableColumns = 0;
                            for (var k = i; k < end && blocks[k].Kind == BlockKind.TableRow && blocks[k].Indent == block.Indent; k++)
                                tableColumns = Math.Max(tableColumns, CountCells(blocks[k].Text));
                            AppendTableRow(sb, indent, SplitCells(block.Text), tableColumns);
                            var aligns = (block.TableAlign ?? string.Empty).Split(',');
                            sb.Append(indent).Append('|');
                            for (var c = 0; c < tableColumns; c++)
                            {
                                var a = c < aligns.Length ? aligns[c] : string.Empty;
                                sb.Append(a == "c" ? " :---: |" : a == "r" ? " ---: |" : a == "l" ? " :--- |" : " --- |");
                            }
                            sb.Append('\n');
                        }
                        else
                        {
                            AppendTableRow(sb, indent, SplitCells(block.Text), tableColumns);
                        }
                        break;

                    case BlockKind.Image:
                        var source = block.Source ?? string.Empty;
                        if (source.IndexOfAny(new[] { ' ', '(', ')' }) >= 0)
                            source = "<" + source + ">";
                        sb.Append(indent).Append("![").Append(OneLine(block.Text).Replace("]", "\\]")).Append("](").Append(source).Append(")\n");
                        break;

                    case BlockKind.Toggle:
                        sb.Append(indent).Append(block.Collapsed ? "<details>" : "<details open>").Append('\n');
                        sb.Append(indent).Append("<summary>").Append(OneLine(block.Text)).Append("</summary>\n\n");
                        var childEnd = Math.Min(end, BlockOperations.SubtreeEnd(blocks, i));
                        if (childEnd > i + 1)
                        {
                            var before = sb.Length;
                            Write(sb, blocks, i + 1, childEnd, block.Indent + 1, indent);
                            if (sb.Length > before)
                                sb.Append('\n');
                        }
                        sb.Append(indent).Append("</details>\n");
                        i = childEnd - 1;
                        break;

                    default:
                        AppendLines(sb, indent, indent, block.Text);
                        break;
                }

                // 같은 수준에 번호 목록이 아닌 블록이 끼면 번호는 1부터 다시 시작한다(BlockOperations.ListLabels와 같은 규칙).
                if (block.Kind != BlockKind.Numbered)
                    numbers.Remove(level);
                previous = block.Kind;
                previousLevel = level;
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

using Folderss.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Folderss.Services
{
    /// <summary>
    /// 블록 하나가 원문에서 온 자리와 쓰던 형식(<see cref="BlockMarkdown.ParseCore"/>가 기록). 줄 번호는 문서 줄(0부터), 끝은 포함하지 않는다.
    /// 열 위치는 그 블록을 읽은 파서 기준(토글 자식은 토글 들여쓰기를 뺀 열)이라 <see cref="BlockMarkdown"/> 쓰기의 열과 같은 기준이다.
    /// </summary>
    internal sealed class BlockSourceInfo
    {
        public int Start = -1;
        public int End = -1;
        /// <summary>토글의 닫는 줄(<c>&lt;/details&gt;</c>). 없으면 -1.</summary>
        public int TailStart = -1;
        public int TailEnd = -1;
        /// <summary>첫 줄 들여쓰기 열. 모르면 -1.</summary>
        public int Column = -1;
        /// <summary>목록 항목의 내용 시작 열(자식 들여쓰기 기준). 목록이 아니면 -1.</summary>
        public int ContentColumn = -1;
        /// <summary>글머리 기호(<c>-</c>·<c>*</c>·<c>+</c>), 번호 목록 구분 기호(<c>.</c>·<c>)</c>), 표식 뒤 공백 수.</summary>
        public char Bullet;
        public char Delim;
        public int Spacing;
        /// <summary>목록 첫 번호 항목의 번호(시작 번호). 첫 항목이 아니면 -1.</summary>
        public int StartNumber = -1;
        /// <summary>Setext 제목의 밑줄(<c>===</c>·<c>---</c>). ATX 제목이면 null.</summary>
        public string Setext;

        // 읽을 때 정규형으로 쓴 조각의 해시와 원문 순서(<see cref="MarkdownSource"/>). 순서가 -1이면 원문으로 쓸 수 없는 조각.
        public ulong HeadKey;
        public ulong TailKey;
        public int HeadOrder = -1;
        public int TailOrder = -1;

        public int PieceStart(int part) => part == 0 ? Start : TailStart;
        public int PieceEnd(int part) => part == 0 ? End : TailEnd;
        public int PieceOrder(int part) => part == 0 ? HeadOrder : TailOrder;
        public ulong PieceKey(int part) => part == 0 ? HeadKey : TailKey;
    }

    /// <summary><see cref="BlockMarkdown.WritePieces"/>가 블록 조각을 하나 쓸 때마다 부르는 쪽.</summary>
    internal abstract class PieceWriter
    {
        /// <summary>원래 형식(목록 표식·들여쓰기 등). 새 블록이면 null.</summary>
        public abstract BlockSourceInfo Hint(int index, EditorBlock block);

        /// <summary>
        /// <paramref name="sb"/>의 <paramref name="start"/>부터 끝까지가 블록 <paramref name="index"/>의 조각 <paramref name="part"/>(0 = 본문·토글 여는 줄, 1 = 토글 닫는 줄)이다.
        /// <paramref name="maskStart"/>부터 <paramref name="maskLength"/>글자(번호 목록의 번호)는 바뀌어도 같은 조각으로 본다. 원문 그대로 쓸 조각이면 true.
        /// </summary>
        public abstract bool Piece(int index, EditorBlock block, int part, StringBuilder sb, int start, int maskStart, int maskLength);
    }

    /// <summary>저장할 글과 블록별 시작 줄.</summary>
    public sealed class MarkdownSaveResult
    {
        /// <summary>파일에 쓸 글(원문의 줄 끝 형식·끝 줄바꿈 유무를 따름).</summary>
        public string Text { get; set; }

        /// <summary>블록 순번별 시작 줄(1부터). 쓰지 않는 블록(빈 문단)은 다음 블록의 줄. 정규형으로 되돌아간 경우 null.</summary>
        public int[] BlockLines { get; set; }

        /// <summary>1 = 바뀐 조각만 다시 씀, 2 = 바뀐 최상위 묶음(목록·표 등) 전체를 다시 씀, 3 = 문서 전체 정규형, 0 = 바뀐 것 없음(원문 그대로).</summary>
        public int Mode { get; set; }

        /// <summary>정규형으로 다시 쓴 조각 수.</summary>
        public int RewrittenPieces { get; set; }
    }

    /// <summary>
    /// 최소 변경 저장(#81 3단계): 읽은 Markdown 원문을 기억해 두었다가 저장할 때 바뀌지 않은 블록은 원문 줄을 바이트 그대로,
    /// 바뀐 블록만 정규형(<see cref="BlockMarkdown"/>)으로 다시 쓴다.
    /// <list type="bullet">
    /// <item>조각: 블록 하나(토글은 여는 줄·닫는 줄 두 조각, 표 머리글은 구분 행 포함)가 차지한 원문 줄. 조각 사이 빈 줄 등은 구분자.</item>
    /// <item>바뀜 판정: 그 블록을 문맥(번호·들여쓰기·표 열 수) 속에서 정규형으로 쓴 글이 읽을 때와 같은지(번호 목록 두 번째 항목부터는 번호 숫자 제외).
    /// 같으면 원문 조각, 다르면 정규형. 사이드카 속성(색 등)은 Markdown에 없으므로 판정에 들지 않는다.</item>
    /// <item>구분자: 앞뒤 조각이 원문에서도 바로 이웃이었으면 원문 구분자, 아니면(블록을 넣거나 지우거나 옮김) 정규형 구분자.</item>
    /// <item>다시 쓴 블록은 원래 목록 표식(<c>*</c>·<c>+</c>·<c>1)</c>)·시작 번호·들여쓰기·Setext 제목 형식을 따르고, 새 목록 항목은 같은 목록 앞 항목을 따른다.</item>
    /// <item>줄 끝(CRLF/LF)·끝 줄바꿈 유무는 파일 단위로 지킨다(다시 쓴 조각도 파일의 줄 끝). BOM은 인코딩이 지킨다.</item>
    /// <item>안전장치: 결과를 다시 읽어 블록 구조가 저장하려는 블록(또는 정규형을 다시 읽은 것)과 다르면 바뀐 최상위 묶음 전체를 다시 쓰고, 그래도 다르면 문서 전체 정규형.</item>
    /// </list>
    /// 원문 글자열은 뷰어가 이미 들고 있는 것과 같은 객체를 참조하고, 그 밖에는 줄 시작 위치·조각 해시만 둔다. WPF를 쓰지 않아 테스트 프로젝트에서 소스 링크로 검증한다.
    /// </summary>
    public sealed class MarkdownSource
    {
        private readonly string _text;
        private readonly int[] _lineStarts;   // 줄 수 + 1개(마지막은 글 길이)
        private readonly string _newline;
        private readonly bool _endsWithNewline;
        // 블록 id → 원문 자리·형식·조각 해시(블록마다 객체 하나)
        private readonly Dictionary<long, BlockSourceInfo> _entries = new Dictionary<long, BlockSourceInfo>();
        private int _orderedCount;
        // 읽을 때 쓴 조각 순서(블록 id·조각·해시)의 개수와 누적 해시 — 저장할 때 같으면 원문을 그대로 돌려준다.
        private const ulong SequenceSeed = 14695981039346656037UL;
        private int _sequenceCount;
        private ulong _sequenceHash = SequenceSeed;

        private MarkdownSource(string text)
        {
            _text = text ?? string.Empty;
            _newline = _text.Contains("\r\n") ? "\r\n" : "\n";
            _endsWithNewline = _text.Length == 0 || _text[_text.Length - 1] == '\n' || _text[_text.Length - 1] == '\r';
            _lineStarts = LineStarts(_text);
        }

        /// <summary>원문(파일에서 읽은 글)의 줄 끝: <c>\r\n</c>이 하나라도 있으면 CRLF.</summary>
        public string Newline => _newline;

        /// <summary>원문 글.</summary>
        public string Text => _text;

        /// <summary>Markdown을 블록으로 읽고 원문 조각을 기억한다.</summary>
        public static List<EditorBlock> Parse(string markdown, out MarkdownSource source)
        {
            var infos = new Dictionary<EditorBlock, BlockSourceInfo>();
            var blocks = BlockMarkdown.ParseCore(markdown, infos);
            source = new MarkdownSource(markdown);
            source.Capture(blocks, infos);
            return blocks;
        }

        /// <summary>빈 문서(새 파일).</summary>
        public static MarkdownSource Empty()
        {
            return new MarkdownSource(string.Empty);
        }

        private static int[] LineStarts(string text)
        {
            // BlockMarkdown.Parse와 같이 \r\n·\n·\r 하나를 줄 끝으로 본다.
            var starts = new List<int> { 0 };
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                        i++;
                    starts.Add(i + 1);
                }
                else if (c == '\n')
                {
                    starts.Add(i + 1);
                }
            }
            starts.Add(text.Length);
            return starts.ToArray();
        }

        private int LineCount => _lineStarts.Length - 1;

        private int Offset(int line)
        {
            return line >= LineCount ? _text.Length : _lineStarts[line];
        }

        private string Slice(int fromLine, int toLine)
        {
            var a = Offset(fromLine);
            var b = Offset(toLine);
            return b > a ? _text.Substring(a, b - a) : string.Empty;
        }

        // ── 읽을 때 ────────────────────────────────────────────────────────

        private void Capture(List<EditorBlock> blocks, Dictionary<EditorBlock, BlockSourceInfo> infos)
        {
            foreach (var pair in infos)
                _entries[pair.Key.Id] = pair.Value;

            var writer = new CaptureWriter(this);
            BlockMarkdown.WritePieces(new StringBuilder(), blocks, writer);

            // 원문 순서로 번호를 매긴다(겹치는 조각은 버림 — 그 블록은 정규형으로 쓴다).
            var pieces = new List<KeyValuePair<BlockSourceInfo, int>>();
            foreach (var info in _entries.Values)
            {
                if (info.HeadOrder == 0) pieces.Add(new KeyValuePair<BlockSourceInfo, int>(info, 0));
                if (info.TailOrder == 0) pieces.Add(new KeyValuePair<BlockSourceInfo, int>(info, 1));
                info.HeadOrder = info.TailOrder = -1;
            }
            pieces.Sort((a, b) => a.Key.PieceStart(a.Value).CompareTo(b.Key.PieceStart(b.Value)));
            var lastEnd = 0;
            foreach (var piece in pieces)
            {
                var start = piece.Key.PieceStart(piece.Value);
                var end = piece.Key.PieceEnd(piece.Value);
                if (start < lastEnd || end <= start)
                    continue;
                if (piece.Value == 0) piece.Key.HeadOrder = _orderedCount; else piece.Key.TailOrder = _orderedCount;
                _orderedCount++;
                lastEnd = end;
            }
        }

        private sealed class CaptureWriter : PieceWriter
        {
            private readonly MarkdownSource _owner;

            public CaptureWriter(MarkdownSource owner)
            {
                _owner = owner;
            }

            public override BlockSourceInfo Hint(int index, EditorBlock block)
            {
                BlockSourceInfo info;
                return _owner._entries.TryGetValue(block.Id, out info) ? info : null;
            }

            public override bool Piece(int index, EditorBlock block, int part, StringBuilder sb, int start, int maskStart, int maskLength)
            {
                var key = KeyOf(sb.ToString(start, sb.Length - start), maskStart, maskLength);
                _owner._sequenceHash = Step(_owner._sequenceHash, block.Id * 2 + part, key);
                _owner._sequenceCount++;
                BlockSourceInfo info;
                if (!_owner._entries.TryGetValue(block.Id, out info))
                    return false;
                var s = info.PieceStart(part);
                var e = info.PieceEnd(part);
                if (s < 0 || e <= s)
                    return false;
                // 순서는 Capture 끝에서 매긴다(여기서 0은 "원문 조각 있음" 표시).
                if (part == 0) { info.HeadKey = key; info.HeadOrder = 0; }
                else { info.TailKey = key; info.TailOrder = 0; }
                return true;
            }
        }

        private static ulong Step(ulong hash, long id, ulong key)
        {
            hash = (hash ^ (ulong)id) * 1099511628211UL;
            hash = (hash ^ key) * 1099511628211UL;
            return hash;
        }

        /// <summary>조각 비교용 해시(FNV-1a 64비트). 번호 목록 번호 자리는 빼고 센다.</summary>
        private static ulong KeyOf(string text, int maskStart, int maskLength)
        {
            var hash = 14695981039346656037UL;
            for (var i = 0; i < text.Length; i++)
            {
                if (maskLength > 0 && i >= maskStart && i < maskStart + maskLength)
                    continue;
                hash ^= text[i];
                hash *= 1099511628211UL;
            }
            hash ^= (ulong)(text.Length - maskLength);
            hash *= 1099511628211UL;
            return hash;
        }

        // ── 저장할 때 ──────────────────────────────────────────────────────

        private sealed class Emitted
        {
            public int Index;
            public long Id;
            public int Part;
            public string Text;        // 정규형 조각(\n 줄 끝)
            public string Separator;   // 이 조각 앞 정규형 구분자
            public BlockSourceInfo Ref;   // 원문 조각이 있으면 그 블록 정보(Part 조각), 없으면 null
            public ulong Key;
            public int Order => Ref.PieceOrder(Part);
            public int Start => Ref.PieceStart(Part);
            public int End => Ref.PieceEnd(Part);
            public bool Preserved;
            public bool Forced;
        }

        private sealed class SaveWriter : PieceWriter
        {
            private readonly MarkdownSource _owner;
            private readonly int[] _groups;
            private readonly HashSet<int> _forced;
            private int _lastEnd;
            public readonly List<Emitted> Pieces = new List<Emitted>();

            public SaveWriter(MarkdownSource owner, int[] groups, HashSet<int> forced)
            {
                _owner = owner;
                _groups = groups;
                _forced = forced;
            }

            private bool IsForced(int index)
            {
                return _forced != null && _forced.Contains(_groups[index]);
            }

            public override BlockSourceInfo Hint(int index, EditorBlock block)
            {
                BlockSourceInfo info;
                if (!_owner._entries.TryGetValue(block.Id, out info))
                    return null;
                if (!IsForced(index))
                    return info;
                // 통째로 다시 쓰는 묶음: 목록 표식·시작 번호만 원문을 따르고 들여쓰기·제목 형식은 정규형.
                return new BlockSourceInfo { Bullet = info.Bullet, Delim = info.Delim, StartNumber = info.StartNumber };
            }

            public override bool Piece(int index, EditorBlock block, int part, StringBuilder sb, int start, int maskStart, int maskLength)
            {
                var text = sb.ToString(start, sb.Length - start);
                var separator = start > _lastEnd ? sb.ToString(_lastEnd, start - _lastEnd) : string.Empty;
                _lastEnd = sb.Length;
                var key = KeyOf(text, maskStart, maskLength);
                BlockSourceInfo info;
                var r = _owner._entries.TryGetValue(block.Id, out info) && info.PieceOrder(part) >= 0 ? info : null;
                var forced = IsForced(index);
                var preserved = r != null && r.PieceKey(part) == key && !forced;
                Pieces.Add(new Emitted
                {
                    Index = index, Id = block.Id * 2 + part, Part = part, Text = text, Separator = separator,
                    Ref = r, Key = key, Preserved = preserved, Forced = forced,
                });
                return preserved;
            }
        }

        /// <summary>
        /// 저장할 글. 바뀐 블록이 없으면 원문 그대로(바이트 동일), 있으면 바뀐 조각만 정규형.
        /// </summary>
        public MarkdownSaveResult Save(IList<EditorBlock> blocks)
        {
            if (blocks == null)
                blocks = new List<EditorBlock>();
            var groups = Groups(blocks);

            var first = Assemble(blocks, groups, null);
            if (IsUnchanged(first.Pieces))
                return new MarkdownSaveResult { Text = _text, BlockLines = first.Result.BlockLines, Mode = 0 };

            List<EditorBlock> canonicalParsed = null;
            if (Verify(first.Result.Text, blocks, ref canonicalParsed))
                return first.Result;

            // 바뀐 조각이 든 최상위 묶음(목록 전체·표 전체·토글 전체 등)을 통째로 다시 쓴다.
            var forced = new HashSet<int>(first.Pieces.Where(p => !p.Preserved).Select(p => groups[p.Index]));
            var second = Assemble(blocks, groups, forced);
            second.Result.Mode = 2;
            if (Verify(second.Result.Text, blocks, ref canonicalParsed))
                return second.Result;

            var canonical = BlockMarkdown.Serialize(blocks);
            return new MarkdownSaveResult
            {
                Text = _newline == "\n" ? canonical : canonical.Replace("\n", _newline),
                BlockLines = null,
                Mode = 3,
                RewrittenPieces = first.Pieces.Count,
            };
        }

        private bool IsUnchanged(List<Emitted> pieces)
        {
            if (pieces.Count != _sequenceCount)
                return false;
            var hash = SequenceSeed;
            foreach (var piece in pieces)
                hash = Step(hash, piece.Id, piece.Key);
            return hash == _sequenceHash;
        }

        private sealed class Assembly
        {
            public List<Emitted> Pieces;
            public MarkdownSaveResult Result;
        }

        private Assembly Assemble(IList<EditorBlock> blocks, int[] groups, HashSet<int> forced)
        {
            var writer = new SaveWriter(this, groups, forced);
            BlockMarkdown.WritePieces(new StringBuilder(), blocks, writer);
            var pieces = writer.Pieces;

            var output = new StringBuilder(_text.Length + 256);
            var lines = new int[blocks.Count];
            for (var i = 0; i < lines.Length; i++)
                lines[i] = -1;
            var line = 1;
            var rewritten = 0;
            Emitted previous = null;
            foreach (var piece in pieces)
            {
                string separator;
                if (previous == null)
                {
                    separator = piece.Ref != null && piece.Order == 0 ? Slice(0, piece.Start) : Convert(piece.Separator);
                }
                else
                {
                    var adjacent = piece.Ref != null && previous.Ref != null && piece.Order == previous.Order + 1
                                   && !piece.Forced && !previous.Forced;
                    separator = adjacent ? Slice(previous.End, piece.Start) : Convert(piece.Separator);
                    if (output.Length > 0 && output[output.Length - 1] != '\n' && output[output.Length - 1] != '\r')
                        line += Append(output, _newline);   // 끝 줄바꿈 없던 원문 마지막 줄 뒤에 이어 쓴다
                }
                line += Append(output, separator);

                if (piece.Part == 0 && lines[piece.Index] < 0)
                    lines[piece.Index] = line;
                if (piece.Preserved)
                {
                    line += Append(output, Slice(piece.Start, piece.End));
                }
                else
                {
                    rewritten++;
                    line += Append(output, Convert(piece.Text));
                }
                previous = piece;
            }

            if (previous != null && previous.Ref != null && _orderedCount > 0 && previous.Order == _orderedCount - 1)
            {
                Append(output, Slice(previous.End, LineCount));   // 원문 마지막 조각 뒤(끝 빈 줄 등)
            }
            else if (pieces.Count == 0 && _sequenceCount == 0)
            {
                output.Append(_text);   // 원래도 쓸 블록이 없던 문서(빈 줄만 등)
            }
            // 끝 줄바꿈 유무는 원문을 따른다.
            if (!_endsWithNewline && output.Length > 0 && output[output.Length - 1] == '\n')
            {
                output.Length--;
                if (output.Length > 0 && output[output.Length - 1] == '\r')
                    output.Length--;
            }

            // 쓰지 않은 블록(빈 문단)은 다음 블록의 줄, 끝이면 마지막 줄 다음.
            var next = line;
            for (var i = lines.Length - 1; i >= 0; i--)
            {
                if (lines[i] < 0)
                    lines[i] = next;
                else
                    next = lines[i];
            }

            return new Assembly
            {
                Pieces = pieces,
                Result = new MarkdownSaveResult { Text = output.ToString(), BlockLines = lines, Mode = 1, RewrittenPieces = rewritten },
            };
        }

        private static int Append(StringBuilder sb, string text)
        {
            sb.Append(text);
            var count = 0;
            foreach (var c in text)
            {
                if (c == '\n')
                    count++;
            }
            return count;
        }

        private string Convert(string canonical)
        {
            return _newline == "\n" || canonical.IndexOf('\n') < 0 ? canonical : canonical.Replace("\n", _newline);
        }

        /// <summary>
        /// 최상위 묶음 번호: 최상위 블록마다 하나, 이어진 최상위 목록 항목·표 행은 한 묶음, 자식(들여쓴 블록·토글 안)은 부모 묶음.
        /// </summary>
        private static int[] Groups(IList<EditorBlock> blocks)
        {
            var groups = new int[blocks.Count];
            if (blocks.Count == 0)
                return groups;
            var min = blocks.Min(b => b.Indent);
            var group = 0;
            BlockKind? previousTop = null;
            for (var i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                if (b.Indent <= min)
                {
                    var merge = previousTop != null &&
                                ((EditorBlock.IsListItem(previousTop.Value) && EditorBlock.IsListItem(b.Kind)) ||
                                 (previousTop == BlockKind.TableRow && b.Kind == BlockKind.TableRow));
                    if (previousTop != null && !merge)
                        group++;
                    if (BlockMarkdown.IsWritten(b))
                        previousTop = b.Kind;
                }
                groups[i] = group;
            }
            return groups;
        }

        // ── 검증 ───────────────────────────────────────────────────────────

        /// <summary>저장할 글을 다시 읽은 블록 구조가 저장하려는 블록과 같은지(아니면 정규형을 다시 읽은 것과 같은지).</summary>
        private static bool Verify(string text, IList<EditorBlock> blocks, ref List<EditorBlock> canonicalParsed)
        {
            var parsed = BlockMarkdown.Parse(text);
            if (SameStructure(parsed, blocks))
                return true;
            if (canonicalParsed == null)
                canonicalParsed = BlockMarkdown.Parse(BlockMarkdown.Serialize(blocks));
            return SameStructure(parsed, canonicalParsed);
        }

        /// <summary>Markdown에 나타나는 내용(종류·상대 들여쓰기·본문·체크·접힘·언어·콜아웃 종류·경로·표 머리글 정렬)이 같은지. 빈 문단은 보지 않는다.</summary>
        public static bool SameStructure(IList<EditorBlock> a, IList<EditorBlock> b)
        {
            var x = a.Where(BlockMarkdown.IsWritten).ToList();
            var y = b.Where(BlockMarkdown.IsWritten).ToList();
            if (x.Count != y.Count)
                return false;
            if (x.Count == 0)
                return true;
            var minX = x.Min(k => k.Indent);
            var minY = y.Min(k => k.Indent);
            for (var i = 0; i < x.Count; i++)
            {
                var p = x[i];
                var q = y[i];
                if (p.Kind != q.Kind || p.Indent - minX != q.Indent - minY || !string.Equals(p.Text, q.Text, StringComparison.Ordinal))
                    return false;
                switch (p.Kind)
                {
                    case BlockKind.Todo:
                        if (p.Checked != q.Checked) return false;
                        break;
                    case BlockKind.Toggle:
                        if (p.Collapsed != q.Collapsed) return false;
                        break;
                    case BlockKind.Code:
                        if (!string.Equals((p.Language ?? string.Empty).Trim(), (q.Language ?? string.Empty).Trim(), StringComparison.Ordinal)) return false;
                        break;
                    case BlockKind.Callout:
                        if (BlockMarkdown.NormalizeCalloutType(p.CalloutType) != BlockMarkdown.NormalizeCalloutType(q.CalloutType)) return false;
                        break;
                    case BlockKind.Image:
                    case BlockKind.File:
                    case BlockKind.Bookmark:
                    case BlockKind.Page:
                        if (!string.Equals(p.Source, q.Source, StringComparison.Ordinal)) return false;
                        break;
                    case BlockKind.TableRow:
                        var firstRow = i == 0 || x[i - 1].Kind != BlockKind.TableRow || x[i - 1].Indent != p.Indent;
                        if (firstRow && !string.Equals(p.TableAlign ?? string.Empty, q.TableAlign ?? string.Empty, StringComparison.Ordinal)) return false;
                        break;
                }
            }
            return true;
        }
    }
}

using Folderss.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Folderss.Services
{
    /// <summary>
    /// 블록 하나가 원문에서 온 자리와 쓰던 형식(<see cref="BlockMarkdown.ParseCore"/>가 기록), 읽을 때의 내용·문맥(<see cref="MarkdownSource"/>가 기록).
    /// 줄 번호는 문서 줄(0부터), 끝은 포함하지 않는다. 열 위치는 그 블록을 읽은 파서 기준(토글 자식은 토글 들여쓰기를 뺀 열)이라 <see cref="BlockMarkdown"/> 쓰기의 열과 같은 기준이다.
    /// 원문 자리를 기록하지 못한 블록(줄을 나눌 수 없는 토글 등)도 내용·문맥을 담으려고 <see cref="Start"/> = -1로 하나씩 둔다.
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
        public byte Spacing;
        /// <summary>목록 첫 번호 항목의 번호(시작 번호). 첫 항목이 아니면 -1.</summary>
        public int StartNumber = -1;
        /// <summary>Setext 제목의 밑줄(<c>===</c>·<c>---</c>). ATX 제목이면 null.</summary>
        public string Setext;
        /// <summary>맨 바깥에서 빈 줄(또는 문서 처음) 바로 다음 들여쓰지 않은 줄에서 시작했다 — 앞 내용과 상관없이 같은 블록으로 읽힌다.</summary>
        public bool SafeStart;

        // 원문 순서(<see cref="MarkdownSource"/>). -1이면 원문으로 쓸 수 없는 조각.
        public int HeadOrder = -1;
        public int TailOrder = -1;

        // 읽을 때 블록 내용(Markdown에 나타나는 것만 — 글자열은 같은 객체를 참조)과 그 블록을 쓸 때의 문맥. 블록 수만큼 있으므로 작게 담는다.
        private byte _kind;
        public BlockKind Kind { get => (BlockKind)_kind; set => _kind = (byte)value; }
        public string Text;
        public string Extra;
        public int Indent;
        public bool Checked;
        public bool Collapsed;
        public PieceContext Context;

        public bool Tracked => Start >= 0;
        public int PieceStart(int part) => part == 0 ? Start : TailStart;
        public int PieceEnd(int part) => part == 0 ? End : TailEnd;
        public int PieceOrder(int part) => part == 0 ? HeadOrder : TailOrder;
    }

    /// <summary>
    /// 블록 조각 하나를 쓸 때의 문맥: 들여쓰기 열, 목록 표식(기호·구분 기호, 뒤 공백, 첫 항목 번호 또는 둘째 항목부터의 번호 자릿수), 표 열 수·머리글 여부.
    /// 조각 글(정규형)은 블록 내용과 이 문맥만으로 정해진다(<see cref="BlockMarkdown.RenderBody"/>).
    /// </summary>
    internal struct PieceContext : IEquatable<PieceContext>
    {
        public int Indent;
        /// <summary>번호 목록 첫 항목의 번호. 아니면 -1(둘째 항목부터는 <see cref="Digits"/>만 본다).</summary>
        public int Number;
        public int TableColumns;
        public char Marker;
        public byte Spacing;
        public byte Digits;
        public bool ContinuesTable;

        public bool Equals(PieceContext other)
        {
            return Indent == other.Indent && Marker == other.Marker && Spacing == other.Spacing && Number == other.Number && Digits == other.Digits
                   && TableColumns == other.TableColumns && ContinuesTable == other.ContinuesTable;
        }

        public override bool Equals(object obj) => obj is PieceContext other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Indent, Marker, Spacing, Number, Digits, TableColumns, ContinuesTable);
    }

    /// <summary><see cref="BlockMarkdown.WritePieces"/>가 블록 조각을 하나 쓸 때마다 부르는 쪽.</summary>
    internal abstract class PieceWriter
    {
        /// <summary><see cref="Begin"/> 결과: 조각 글을 써서 <see cref="Piece"/>로 알린다.</summary>
        public const int Write = 0;
        /// <summary><see cref="Begin"/> 결과: 쓰지 않는다 — 원문 그대로 쓸 조각.</summary>
        public const int SkipPreserved = 1;
        /// <summary><see cref="Begin"/> 결과: 쓰지 않는다 — 원문으로 쓰지 않는 조각(글이 필요 없을 때).</summary>
        public const int SkipRewritten = 2;

        /// <summary>원래 형식(목록 표식·들여쓰기 등). 새 블록이면 null.</summary>
        public abstract BlockSourceInfo Hint(int index, EditorBlock block);

        /// <summary>
        /// 블록 <paramref name="index"/>의 조각 <paramref name="part"/>(0 = 본문·토글 여는 줄, 1 = 토글 닫는 줄)를 <paramref name="sb"/> 끝에 쓰기 직전. <paramref name="context"/>는 그 조각의 문맥.
        /// <see cref="Write"/>면 조각 글을 쓴 뒤 <see cref="Piece"/>를 부르고, 아니면 글을 쓰지 않는다.
        /// </summary>
        public abstract int Begin(int index, EditorBlock block, int part, StringBuilder sb, in PieceContext context);

        /// <summary>
        /// <paramref name="sb"/>의 <paramref name="start"/>부터 끝까지가 블록 <paramref name="index"/>의 조각 <paramref name="part"/>다.
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
    /// 같으면 원문 조각, 다르면 정규형. 사이드카 속성(색 등)은 Markdown에 없으므로 판정에 들지 않는다.
    /// 블록 내용·문맥이 읽을 때와 같으면 정규형 글도 같으므로 쓰지 않고 바로 원문 조각을 쓰고, 다를 때만 지금 글과 읽을 때 글(읽을 때 내용·문맥으로 다시 만듦)을 비교한다.</item>
    /// <item>구분자: 앞뒤 조각이 원문에서도 바로 이웃이었으면 원문 구분자, 아니면(블록을 넣거나 지우거나 옮김) 정규형 구분자.</item>
    /// <item>다시 쓴 블록은 원래 목록 표식(<c>*</c>·<c>+</c>·<c>1)</c>)·시작 번호·들여쓰기·Setext 제목 형식을 따르고, 새 목록 항목은 같은 목록 앞 항목을 따른다.</item>
    /// <item>줄 끝(CRLF/LF)·끝 줄바꿈 유무는 파일 단위로 지킨다(다시 쓴 조각도 파일의 줄 끝). BOM은 인코딩이 지킨다.</item>
    /// <item>안전장치: 결과를 다시 읽은 블록 구조가 저장하려는 블록(또는 정규형을 다시 읽은 것)과 다르면 바뀐 최상위 묶음 전체를 다시 쓰고, 그래도 다르면 문서 전체 정규형.
    /// 다시 읽기는 바뀐 조각이 든 구간만 한다(<see cref="VerifyChanged"/> — 원문 그대로인 구간은 읽을 때 블록과 같음이 보장됨). 구간 검증이 실패하면 문서 전체를 다시 읽어 판단한다.</item>
    /// </list>
    /// 원문 글자열은 뷰어가 이미 들고 있는 것과 같은 객체를 참조하고, 그 밖에는 블록당 정보 하나와 (처음 저장할 때) 줄 시작 위치만 둔다. WPF를 쓰지 않아 테스트 프로젝트에서 소스 링크로 검증한다.
    /// </summary>
    public sealed class MarkdownSource
    {
        private readonly string _text;
        private readonly string _newline;
        private readonly bool _endsWithNewline;
        // 블록 id → 원문 자리·형식·읽을 때 내용과 문맥(블록마다 객체 하나)
        private readonly Dictionary<long, BlockSourceInfo> _entries = new Dictionary<long, BlockSourceInfo>();
        // 원문 순서 → 조각(정보·조각 번호)
        private BlockSourceInfo[] _orderInfo = Array.Empty<BlockSourceInfo>();
        private int _orderedCount;
        private bool _openAtEnd;
        // 읽을 때 쓴 조각 순서(블록 id·조각)의 개수와 누적 해시 — 저장할 때 같고 모든 조각 글이 같으면 원문을 그대로 돌려준다.
        private const ulong SequenceSeed = 14695981039346656037UL;
        private int _sequenceCount;
        private ulong _sequenceHash = SequenceSeed;

        // 처음 저장할 때 만든다: 줄 시작 위치(줄 수 + 1개, 마지막은 글 길이), 그 위치 앞의 \n 개수, 조각 뒤 구분자가 빈 줄뿐인지(0 모름, 1 예, 2 아니오)
        private int[] _lineStarts;
        private int[] _newlinesBefore;
        private byte[] _gapBlank;

        private MarkdownSource(string text)
        {
            _text = text ?? string.Empty;
            _newline = _text.Contains("\r\n") ? "\r\n" : "\n";
            _endsWithNewline = _text.Length == 0 || _text[_text.Length - 1] == '\n' || _text[_text.Length - 1] == '\r';
        }

        /// <summary>원문(파일에서 읽은 글)의 줄 끝: <c>\r\n</c>이 하나라도 있으면 CRLF.</summary>
        public string Newline => _newline;

        /// <summary>원문 글.</summary>
        public string Text => _text;

        /// <summary>Markdown을 블록으로 읽고 원문 조각을 기억한다.</summary>
        public static List<EditorBlock> Parse(string markdown, out MarkdownSource source)
        {
            var record = new BlockMarkdown.SourceRecord();
            var blocks = BlockMarkdown.ParseCore(markdown, record);
            source = new MarkdownSource(markdown);
            source.Capture(blocks, record);
            return blocks;
        }

        /// <summary>빈 문서(새 파일).</summary>
        public static MarkdownSource Empty()
        {
            return new MarkdownSource(string.Empty);
        }

        private void EnsureLines()
        {
            if (_lineStarts != null)
                return;
            // BlockMarkdown.Parse와 같이 \r\n·\n·\r 하나를 줄 끝으로 본다.
            var starts = new List<int>(_text.Length / 32 + 2) { 0 };
            var newlines = new List<int>(_text.Length / 32 + 2) { 0 };
            var count = 0;
            for (var i = 0; i < _text.Length; i++)
            {
                var c = _text[i];
                if (c == '\r')
                {
                    if (i + 1 < _text.Length && _text[i + 1] == '\n')
                    {
                        i++;
                        count++;
                    }
                    starts.Add(i + 1);
                    newlines.Add(count);
                }
                else if (c == '\n')
                {
                    count++;
                    starts.Add(i + 1);
                    newlines.Add(count);
                }
            }
            starts.Add(_text.Length);
            newlines.Add(count);
            _newlinesBefore = newlines.ToArray();
            _lineStarts = starts.ToArray();
        }

        private int LineCount => _lineStarts.Length - 1;

        private int Offset(int line)
        {
            return line >= LineCount ? _text.Length : _lineStarts[line];
        }

        /// <summary>원문 줄 [fromLine, toLine)을 붙이고 그 안의 <c>\n</c> 개수를 돌려준다.</summary>
        private int AppendLines(StringBuilder sb, int fromLine, int toLine)
        {
            var from = Math.Min(fromLine, LineCount);
            var to = Math.Min(toLine, LineCount);
            if (to <= from)
                return 0;
            var a = _lineStarts[from];
            var b = _lineStarts[to];
            sb.Append(_text, a, b - a);
            return _newlinesBefore[to] - _newlinesBefore[from];
        }

        // ── 읽을 때 ────────────────────────────────────────────────────────

        private void Capture(List<EditorBlock> blocks, BlockMarkdown.SourceRecord record)
        {
            _openAtEnd = record.OpenAtEnd;
            foreach (var pair in record.Infos)
                _entries[pair.Key.Id] = pair.Value;
            foreach (var block in blocks)
            {
                BlockSourceInfo info;
                if (!_entries.TryGetValue(block.Id, out info))
                {
                    info = new BlockSourceInfo();   // 원문 자리를 기록하지 못한 블록: 내용·문맥만
                    _entries[block.Id] = info;
                }
                info.Kind = block.Kind;
                info.Text = block.Text;
                info.Extra = BlockMarkdown.ExtraOf(block);
                info.Indent = block.Indent;
                info.Checked = block.Checked;
                info.Collapsed = block.Collapsed;
            }

            // 글은 쓰지 않고 문맥만 기록한다(조각 글은 필요할 때 내용·문맥으로 다시 만든다).
            BlockMarkdown.WritePieces(new StringBuilder(), blocks, new CaptureWriter(this));

            // 원문 순서로 번호를 매긴다(겹치는 조각은 버림 — 그 블록은 정규형으로 쓴다).
            var pieces = new List<KeyValuePair<BlockSourceInfo, int>>();
            var sorted = true;
            var previousStart = -1;
            foreach (var info in _entries.Values)
            {
                if (info.HeadOrder == 0) Add(pieces, info, 0, ref previousStart, ref sorted);
                if (info.TailOrder == 0) Add(pieces, info, 1, ref previousStart, ref sorted);
                info.HeadOrder = info.TailOrder = -1;
            }
            if (!sorted)
                pieces.Sort((a, b) => a.Key.PieceStart(a.Value).CompareTo(b.Key.PieceStart(b.Value)));
            var orderInfo = new List<BlockSourceInfo>(pieces.Count);
            var lastEnd = 0;
            foreach (var piece in pieces)
            {
                var start = piece.Key.PieceStart(piece.Value);
                var end = piece.Key.PieceEnd(piece.Value);
                if (start < lastEnd || end <= start)
                    continue;
                if (piece.Value == 0) piece.Key.HeadOrder = _orderedCount; else piece.Key.TailOrder = _orderedCount;
                orderInfo.Add(piece.Key);
                _orderedCount++;
                lastEnd = end;
            }
            _orderInfo = orderInfo.ToArray();
        }

        private static void Add(List<KeyValuePair<BlockSourceInfo, int>> pieces, BlockSourceInfo info, int part, ref int previousStart, ref bool sorted)
        {
            // 이미 시작 줄 순서(같은 줄 없음)면 정렬하지 않는다 — 정렬 결과가 같으므로.
            var start = info.PieceStart(part);
            if (start <= previousStart)
                sorted = false;
            previousStart = start;
            pieces.Add(new KeyValuePair<BlockSourceInfo, int>(info, part));
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
                return _owner._entries.TryGetValue(block.Id, out info) && info.Tracked ? info : null;
            }

            public override int Begin(int index, EditorBlock block, int part, StringBuilder sb, in PieceContext context)
            {
                _owner._sequenceHash = Step(_owner._sequenceHash, block.Id * 2 + part);
                _owner._sequenceCount++;
                var info = _owner._entries[block.Id];
                if (part == 0)
                    info.Context = context;
                var s = info.PieceStart(part);
                var e = info.PieceEnd(part);
                if (s < 0 || e <= s)
                    return SkipRewritten;
                // 순서는 Capture 끝에서 매긴다(여기서 0은 "원문 조각 있음" 표시).
                if (part == 0) info.HeadOrder = 0; else info.TailOrder = 0;
                return SkipPreserved;
            }

            public override bool Piece(int index, EditorBlock block, int part, StringBuilder sb, int start, int maskStart, int maskLength)
            {
                return false;   // Begin이 늘 건너뛰므로 불리지 않는다
            }
        }

        private static ulong Step(ulong hash, long id)
        {
            return (hash ^ (ulong)id) * 1099511628211UL;
        }

        /// <summary>조각 비교용 해시(FNV-1a 64비트). 번호 목록 번호 자리는 빼고 센다.</summary>
        private static ulong KeyOf(StringBuilder sb, int start, int maskStart, int maskLength)
        {
            var hash = 14695981039346656037UL;
            var length = sb.Length - start;
            var i = 0;
            foreach (var chunk in sb.GetChunks())
            {
                var span = chunk.Span;
                for (var k = 0; k < span.Length; k++, i++)
                {
                    var at = i - start;
                    if (at < 0 || (maskLength > 0 && at >= maskStart && at < maskStart + maskLength))
                        continue;
                    hash ^= span[k];
                    hash *= 1099511628211UL;
                }
            }
            hash ^= (ulong)(length - maskLength);
            hash *= 1099511628211UL;
            return hash;
        }

        /// <summary>읽을 때 그 조각을 정규형으로 쓴 글의 해시 — 읽을 때 내용·문맥으로 다시 만든다(블록 내용이나 문맥이 바뀐 조각에만 필요).</summary>
        private static ulong OriginalKey(BlockSourceInfo info, int part)
        {
            var context = info.Context;
            var indent = new string(' ', context.Indent);
            var sb = new StringBuilder();
            if (part == 1)
            {
                sb.Append(indent).Append("</details>\n");
                return KeyOf(sb, 0, 0, 0);
            }
            if (info.Kind == BlockKind.Toggle)
            {
                BlockMarkdown.RenderToggleHead(sb, indent, info.Collapsed, info.Text);
                return KeyOf(sb, 0, 0, 0);
            }
            string marker = null;
            if (EditorBlock.IsListItem(info.Kind))
            {
                // 둘째 항목부터의 번호는 비교에서 빼므로 자릿수만 맞춘다.
                marker = info.Kind == BlockKind.Numbered
                    ? (context.Number >= 0 ? context.Number.ToString() : new string('0', context.Digits)) + context.Marker
                    : context.Marker.ToString();
                marker += new string(' ', context.Spacing);
            }
            BlockMarkdown.RenderBody(sb, indent, info.Kind, info.Text, info.Checked, info.Extra, info.Tracked ? info.Setext : null,
                marker, context.TableColumns, context.ContinuesTable);
            return KeyOf(sb, 0, context.Indent, context.Digits);
        }

        /// <summary>블록의 Markdown에 나타나는 내용(들여쓰기 제외)이 읽을 때와 같은지.</summary>
        private static bool SameContent(EditorBlock block, BlockSourceInfo info)
        {
            return block.Kind == info.Kind && block.Checked == info.Checked && block.Collapsed == info.Collapsed
                   && string.Equals(block.Text, info.Text, StringComparison.Ordinal)
                   && string.Equals(BlockMarkdown.ExtraOf(block), info.Extra, StringComparison.Ordinal);
        }

        // ── 저장할 때 ──────────────────────────────────────────────────────

        private sealed class Emitted
        {
            public int Index;
            public long Id;
            public int Part;
            public string Text;        // 정규형 조각(\n 줄 끝). 쓰지 않고 건너뛴 조각은 null
            public string Separator;   // 이 조각 앞 정규형 구분자
            public BlockSourceInfo Info;  // 이 블록의 읽을 때 정보(새 블록이면 null)
            public BlockSourceInfo Ref;   // 원문 조각이 있으면 그 블록 정보(Part 조각), 없으면 null
            public ulong Key;          // 쓴 조각만
            public int Order => Ref.PieceOrder(Part);
            public int Start => Ref.PieceStart(Part);
            public int End => Ref.PieceEnd(Part);
            public bool Preserved;
            public bool Forced;
            public bool Skipped;       // 내용·문맥이 읽을 때와 같아 쓰지 않음(= 정규형 글이 읽을 때와 같음)
            public bool Adjacent;      // 앞 조각과 원문 구분자로 이었음
            public int OutStart;       // 결과 글에서 조각이 시작하는 위치
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
                if (!_owner._entries.TryGetValue(block.Id, out info) || !info.Tracked)
                    return null;
                if (!IsForced(index))
                    return info;
                // 통째로 다시 쓰는 묶음: 목록 표식·시작 번호만 원문을 따르고 들여쓰기·제목 형식은 정규형.
                return new BlockSourceInfo { Bullet = info.Bullet, Delim = info.Delim, StartNumber = info.StartNumber };
            }

            private string TakeSeparator(StringBuilder sb, int start)
            {
                var separator = start > _lastEnd ? sb.ToString(_lastEnd, start - _lastEnd) : string.Empty;
                _lastEnd = sb.Length;
                return separator;
            }

            public override int Begin(int index, EditorBlock block, int part, StringBuilder sb, in PieceContext context)
            {
                BlockSourceInfo info;
                if (!_owner._entries.TryGetValue(block.Id, out info) || info.PieceOrder(part) < 0 || IsForced(index))
                    return Write;
                // 내용과 문맥이 읽을 때와 같으면 정규형 글도 같다 → 쓰지 않고 원문 조각(닫는 줄은 들여쓰기만 본다).
                var same = part == 1 ? context.Indent == info.Context.Indent : context.Equals(info.Context) && SameContent(block, info);
                if (!same)
                    return Write;
                Pieces.Add(new Emitted
                {
                    Index = index, Id = block.Id * 2 + part, Part = part, Separator = TakeSeparator(sb, sb.Length),
                    Info = info, Ref = info, Preserved = true, Skipped = true,
                });
                return SkipPreserved;
            }

            public override bool Piece(int index, EditorBlock block, int part, StringBuilder sb, int start, int maskStart, int maskLength)
            {
                var separator = start > _lastEnd ? sb.ToString(_lastEnd, start - _lastEnd) : string.Empty;
                var text = sb.ToString(start, sb.Length - start);
                _lastEnd = sb.Length;
                var key = KeyOf(sb, start, maskStart, maskLength);
                BlockSourceInfo info;
                _owner._entries.TryGetValue(block.Id, out info);
                var r = info != null && info.PieceOrder(part) >= 0 ? info : null;
                var forced = IsForced(index);
                var preserved = r != null && !forced && OriginalKey(r, part) == key;
                Pieces.Add(new Emitted
                {
                    Index = index, Id = block.Id * 2 + part, Part = part, Text = text, Separator = separator,
                    Info = info, Ref = r, Key = key, Preserved = preserved, Forced = forced,
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
            EnsureLines();
            var groups = Groups(blocks);

            var first = Assemble(blocks, groups, null);
            if (IsUnchanged(first.Pieces))
                return new MarkdownSaveResult { Text = _text, BlockLines = first.Result.BlockLines, Mode = 0 };

            List<EditorBlock> canonicalParsed = null;
            if (VerifyChanged(first, blocks) || Verify(first.Result.Text, blocks, ref canonicalParsed))
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

        /// <summary>조각 순서(블록 id·조각)가 읽을 때와 같고 모든 조각의 정규형 글이 읽을 때와 같은지.</summary>
        private bool IsUnchanged(List<Emitted> pieces)
        {
            if (pieces.Count != _sequenceCount)
                return false;
            var hash = SequenceSeed;
            foreach (var piece in pieces)
                hash = Step(hash, piece.Id);
            if (hash != _sequenceHash)
                return false;
            foreach (var piece in pieces)
            {
                if (!piece.Skipped && (piece.Info == null || piece.Key != OriginalKey(piece.Info, piece.Part)))
                    return false;
            }
            return true;
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
                if (previous == null)
                {
                    if (piece.Ref != null && piece.Order == 0)
                        line += AppendLines(output, 0, piece.Start);
                    else
                        line += Append(output, Convert(piece.Separator));
                }
                else
                {
                    var adjacent = piece.Ref != null && previous.Ref != null && piece.Order == previous.Order + 1
                                   && !piece.Forced && !previous.Forced;
                    piece.Adjacent = adjacent;
                    if (output.Length > 0 && output[output.Length - 1] != '\n' && output[output.Length - 1] != '\r')
                        line += Append(output, _newline);   // 끝 줄바꿈 없던 원문 마지막 줄 뒤에 이어 쓴다
                    if (adjacent)
                        line += AppendLines(output, previous.End, piece.Start);
                    else
                        line += Append(output, Convert(piece.Separator));
                }

                if (piece.Part == 0 && lines[piece.Index] < 0)
                    lines[piece.Index] = line;
                piece.OutStart = output.Length;
                if (piece.Preserved)
                {
                    line += AppendLines(output, piece.Start, piece.End);
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
                AppendLines(output, previous.End, LineCount);   // 원문 마지막 조각 뒤(끝 빈 줄 등)
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
            var min = MinIndent(blocks, false);
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

        private static int MinIndent(IList<EditorBlock> blocks, bool writtenOnly)
        {
            var min = int.MaxValue;
            foreach (var b in blocks)
            {
                if (b.Indent < min && (!writtenOnly || BlockMarkdown.IsWritten(b)))
                    min = b.Indent;
            }
            return min == int.MaxValue ? 0 : min;
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

        // 구간 끝 확인용 문단: 구간 뒤에 붙여 읽었을 때 이 문단이 맨 바깥 문단으로 나오면 구간 안 블록이 모두 구간 안에서 끝났다.
        private const string Sentinel = "\u0001\u0002";

        /// <summary>
        /// 바뀐 조각이 든 구간만 다시 읽어 <c>SameStructure(Parse(text), blocks)</c>가 참임을 확인한다. true면 문서 전체를 다시 읽어도 같다(거짓이면 전체 검증으로 판단).
        /// <list type="bullet">
        /// <item>자르는 자리: 맨 바깥 블록의 첫 조각이면서 결과 글에서 바로 앞 줄이 빈 줄이고 그 줄이 들여쓰지 않은 자리. 파서는 이런 줄에서 앞 내용과 상관없이 새로 시작한 것과 같다.</item>
        /// <item>원문 그대로인 구간(모든 조각이 원문 그대로·원문 순서로 이웃·원문 구분자가 빈 줄뿐·블록 내용이 읽을 때와 같음, 원문에서도 시작과 다음 조각이 SafeStart)은
        /// 원문을 읽을 때와 같은 블록으로 읽히므로 다시 읽지 않는다.</item>
        /// <item>나머지 구간은 그 글만 읽는다. 구간 뒤에 <see cref="Sentinel"/> 문단을 붙여 그것이 맨 바깥 문단으로 나오는지로 닫히지 않은 펜스·토글 등이 다음 구간으로 넘어가지 않음을 확인한다.</item>
        /// </list>
        /// </summary>
        private bool VerifyChanged(Assembly assembly, IList<EditorBlock> blocks)
        {
            var pieces = assembly.Pieces;
            var text = assembly.Result.Text;
            if (pieces.Count == 0 || FrontMatterOpener(text, blocks))
                return false;
            var minY = MinIndent(blocks, true);

            var cuts = new List<int> { 0 };
            for (var k = 1; k < pieces.Count; k++)
            {
                var p = pieces[k];
                if (p.Part == 0 && blocks[p.Index].Indent == minY && IsCut(text, p.OutStart))
                    cuts.Add(k);
            }
            for (var c = 0; c < cuts.Count; c++)
            {
                var a = cuts[c];
                var b = c + 1 < cuts.Count ? cuts[c + 1] : pieces.Count;
                if (Unchanged(pieces, a, b, blocks, minY, text))
                    continue;
                var from = a == 0 ? 0 : pieces[a].OutStart;
                var to = b < pieces.Count ? pieces[b].OutStart : text.Length;
                if (!VerifyRange(text, from, to, pieces, a, b, blocks, minY))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 첫 줄이 <c>---</c>인데 첫 블록이 front matter가 아닌지. front matter는 닫는 줄을 문서 끝까지 찾으므로(뒤쪽 구분선·Setext 밑줄이 닫는 줄이 될 수 있음)
        /// 구간만 읽어서는 판단할 수 없다 — 문서 전체 검증으로 넘긴다.
        /// </summary>
        private static bool FrontMatterOpener(string text, IList<EditorBlock> blocks)
        {
            var start = text.Length > 0 && text[0] == '﻿' ? 1 : 0;
            var end = text.IndexOfAny(new[] { '\n', '\r' }, start);
            var first = text.Substring(start, (end < 0 ? text.Length : end) - start).TrimStart(' ', '\t').TrimEnd();
            if (first != "---")
                return false;
            foreach (var block in blocks)
            {
                if (BlockMarkdown.IsWritten(block))
                    return block.Kind != BlockKind.FrontMatter;
            }
            return true;
        }

        /// <summary>결과 글 <paramref name="position"/>이 들여쓰지 않은 줄의 처음이고 바로 앞 줄이 빈 줄(공백·탭만)인지.</summary>
        private static bool IsCut(string text, int position)
        {
            if (position <= 0 || position >= text.Length || char.IsWhiteSpace(text[position]))
                return false;
            var j = position - 1;
            if (text[j] == '\n')
            {
                j--;
                if (j >= 0 && text[j] == '\r')
                    j--;
            }
            else if (text[j] == '\r')
            {
                j--;
            }
            else
            {
                return false;   // 줄 처음이 아님
            }
            for (; j >= 0 && text[j] != '\n' && text[j] != '\r'; j--)
            {
                if (text[j] != ' ' && text[j] != '\t')
                    return false;
            }
            return true;
        }

        /// <summary>조각 [a, b)가 원문 그대로라 원문을 읽을 때와 같은 블록으로 읽히는지.</summary>
        private bool Unchanged(List<Emitted> pieces, int a, int b, IList<EditorBlock> blocks, int minY, string text)
        {
            for (var k = a; k < b; k++)
            {
                var p = pieces[k];
                if (!p.Preserved || p.Ref == null || p.Forced)
                    return false;
                if (k > a && (!p.Adjacent || !GapBlank(pieces[k - 1].Order)))
                    return false;
                if (p.Part == 0)
                {
                    var block = blocks[p.Index];
                    if (!SameContent(block, p.Ref) || block.Indent - minY != p.Ref.Indent)
                        return false;
                }
            }
            var first = pieces[a];
            if (first.Part != 0 || !first.Ref.SafeStart)
                return false;
            // 첫 줄(front matter 자리)이 원문과 결과에서 같은지.
            var firstLine = text.IndexOfAny(new[] { '\n', '\r' }, 0, first.OutStart) < 0;
            if (firstLine != (first.Start == 0))
                return false;
            if (a == 0 && first.Order == 0 && !GapBlank(-1))
                return false;
            var last = pieces[b - 1];
            if (!GapBlank(last.Order))
                return false;
            var nextOrder = last.Order + 1;
            if (nextOrder < _orderedCount)
                return _orderInfo[nextOrder].HeadOrder == nextOrder && _orderInfo[nextOrder].SafeStart;
            return !_openAtEnd;
        }

        private int PartOf(int order) => _orderInfo[order].HeadOrder == order ? 0 : 1;

        /// <summary>원문에서 순서 <paramref name="order"/> 조각 뒤(다음 조각 앞 또는 문서 끝까지, -1이면 첫 조각 앞)가 빈 줄(공백·탭, 첫 줄 BOM)뿐인지.</summary>
        private bool GapBlank(int order)
        {
            if (_gapBlank == null)
                _gapBlank = new byte[_orderedCount + 1];
            ref var known = ref _gapBlank[order + 1];
            if (known == 0)
            {
                var from = order < 0 ? 0 : Offset(_orderInfo[order].PieceEnd(PartOf(order)));
                var to = order + 1 < _orderedCount ? Offset(_orderInfo[order + 1].PieceStart(PartOf(order + 1))) : _text.Length;
                var blank = true;
                for (var i = from; i < to && blank; i++)
                {
                    var c = _text[i];
                    blank = c == ' ' || c == '\t' || c == '\n' || c == '\r' || (i == 0 && c == '﻿');
                }
                known = (byte)(blank ? 1 : 2);
            }
            return known == 1;
        }

        /// <summary>결과 글 [from, to)를 따로 읽어 조각 [a, b)의 블록과 같은 구조인지(끝이 문서 끝이 아니면 Sentinel 문단으로 구간 안에서 닫혔는지도).</summary>
        private static bool VerifyRange(string text, int from, int to, List<Emitted> pieces, int a, int b, IList<EditorBlock> blocks, int minY)
        {
            var atEnd = to >= text.Length;
            // 읽기는 글 맨 앞 BOM만 떼므로, 문서 중간에서 BOM으로 시작하는 구간은 따로 읽으면 결과가 달라진다(원문 첫 조각을 뒤로 옮긴 경우).
            if (from > 0 && text[from] == '﻿')
                return false;
            var range = text.Substring(from, to - from);
            if (!atEnd)
                range += Sentinel;
            var parsed = BlockMarkdown.ParseCore(range, null, from == 0);
            if (!atEnd)
            {
                var last = parsed.Count > 0 ? parsed[parsed.Count - 1] : null;
                if (last == null || last.Kind != BlockKind.Paragraph || last.Indent != 0 || last.Text != Sentinel)
                    return false;
                parsed.RemoveAt(parsed.Count - 1);
            }
            var x = parsed.Where(BlockMarkdown.IsWritten).ToList();
            var count = 0;
            for (var k = a; k < b; k++)
            {
                if (pieces[k].Part != 0)
                    continue;
                if (count >= x.Count)
                    return false;
                var firstRow = count == 0 || x[count - 1].Kind != BlockKind.TableRow || x[count - 1].Indent != x[count].Indent;
                if (!SameBlock(x[count], blocks[pieces[k].Index], 0, minY, firstRow))
                    return false;
                count++;
            }
            return count == x.Count;
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
                var firstRow = i == 0 || x[i - 1].Kind != BlockKind.TableRow || x[i - 1].Indent != p.Indent;
                if (!SameBlock(p, y[i], minX, minY, firstRow))
                    return false;
            }
            return true;
        }

        private static bool SameBlock(EditorBlock p, EditorBlock q, int minX, int minY, bool firstRow)
        {
            if (p.Kind != q.Kind || p.Indent - minX != q.Indent - minY || !string.Equals(p.Text, q.Text, StringComparison.Ordinal))
                return false;
            switch (p.Kind)
            {
                case BlockKind.Todo:
                    return p.Checked == q.Checked;
                case BlockKind.Toggle:
                    return p.Collapsed == q.Collapsed;
                case BlockKind.Code:
                    return string.Equals((p.Language ?? string.Empty).Trim(), (q.Language ?? string.Empty).Trim(), StringComparison.Ordinal);
                case BlockKind.Callout:
                    return BlockMarkdown.NormalizeCalloutType(p.CalloutType) == BlockMarkdown.NormalizeCalloutType(q.CalloutType);
                case BlockKind.Image:
                case BlockKind.File:
                case BlockKind.Bookmark:
                case BlockKind.Page:
                    return string.Equals(p.Source, q.Source, StringComparison.Ordinal);
                case BlockKind.TableRow:
                    return !firstRow || string.Equals(p.TableAlign ?? string.Empty, q.TableAlign ?? string.Empty, StringComparison.Ordinal);
            }
            return true;
        }
    }
}

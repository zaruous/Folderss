using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Folderss.Services
{
    /// <summary>
    /// 작업 종류. 뒤의 셋(이름 변경·새 폴더·새 파일)은 메인 패널 작업 이력만 쓴다(폴더 비교 창은 만들지 않음).
    /// 저장은 이름으로 하므로 값을 끝에만 더한다.
    /// </summary>
    public enum JournalOperationKind { Move, Copy, Delete, DeletePermanently, Rename, CreateFolder, CreateFile }

    public enum JournalOperationState
    {
        Completed,
        /// <summary>"끝" 기록이 없다 — 도중에 앱이 끝났거나 기록 쓰기가 실패했다. 기록된 항목만 되돌린다.</summary>
        Interrupted,
        Undone,
        /// <summary>되돌리지 않고 넘김. 그 앞 작업을 되돌릴 수 있게 된다.</summary>
        Skipped
    }

    /// <summary>
    /// 휴지통. Windows 구현은 셸 휴지통을 쓰고, 테스트는 임시 폴더로 흉내 낸다.
    /// 되살릴 항목은 원래 경로와 <see cref="Send"/>가 돌려준 시각으로 찾는다.
    /// </summary>
    public interface IRecycleBin
    {
        /// <summary>휴지통으로 보내고 보낸 시각(UTC)을 돌려준다. 휴지통이 없는 위치면 예외.</summary>
        DateTime Send(string path);
        bool CanRestore(string originalPath, DateTime sentUtc);
        void Restore(string originalPath, DateTime sentUtc);

        /// <summary>이 위치를 휴지통으로 보낼 수 있는가(보내기 전에 미리 판단·경고하는 용도). 기본은 가능.</summary>
        bool CanSend(string path) => true;
    }

    public sealed class JournalItem
    {
        /// <summary>이동·복사의 원본, 삭제한 항목의 경로.</summary>
        public string Source { get; set; }
        /// <summary>이동·복사로 생긴 경로. 삭제는 null.</summary>
        public string Destination { get; set; }
        public bool IsDirectory { get; set; }
        /// <summary>이동하면서 교체해 휴지통으로 보낸 대상의 옛 항목 경로(= Destination). 교체가 없으면 null.</summary>
        public string ReplacedPath { get; set; }
        /// <summary>교체·삭제로 휴지통에 보낸 시각. 되살릴 때 찾는 키.</summary>
        public DateTime? RecycledUtc { get; set; }
        /// <summary>작업 직후 결과 파일의 크기·수정 시각. 되돌리기 전에 그 사이 바뀌었는지 본다(폴더는 있는지만 본다).</summary>
        public long? Size { get; set; }
        public DateTime? ModifiedUtc { get; set; }
        public bool Succeeded { get; set; }
        public string Error { get; set; }
        public bool Reverted { get; set; }
    }

    public sealed class JournalOperation
    {
        public string Id { get; set; }
        public DateTime StartedUtc { get; set; }
        public JournalOperationKind Kind { get; set; }
        public JournalOperationState State { get; set; }
        public List<JournalItem> Items { get; } = new List<JournalItem>();
        /// <summary>마지막 되돌리기 시도의 오류. 성공하면 null.</summary>
        public string UndoError { get; set; }

        /// <summary>되돌릴 거리가 남아 있는가(영구 삭제·이미 되돌림·넘김·성공 항목 없음은 아님).</summary>
        public bool CanUndo =>
            Kind != JournalOperationKind.DeletePermanently &&
            (State == JournalOperationState.Completed || State == JournalOperationState.Interrupted) &&
            Items.Any(item => item.Succeeded && !item.Reverted);
    }

    /// <summary>
    /// 폴더 비교 창에서 한 파일 작업(이동·복사·삭제)을 실행하면서 기록하고, 최근 작업부터(LIFO) 되돌린다.
    /// 기록은 <c>yyyy-MM-dd.jsonl</c>(UTC 날짜)에 한 줄씩 덧붙이고 7일 지나면 지운다.
    /// 작업마다 "시작"을 먼저 쓰고 실행한다 — 시작을 못 쓰면 실행하지 않아 이력에 안 남는 작업이 생기지 않는다.
    /// 한 번에 한 작업만 부른다(비교 창이 순서를 보장).
    /// 메인 패널 파일 작업 대기열(<see cref="FileJobQueue"/>)은 별도 폴더의 인스턴스를 쓰고, 실행은 직접 하면서
    /// <see cref="Begin"/>/<see cref="AddItem"/>/<see cref="End"/>로 같은 형식의 기록만 남긴다(대기열이 잠금으로 순서를 보장).
    /// </summary>
    public sealed class FileOperationJournal
    {
        public static readonly TimeSpan Retention = TimeSpan.FromDays(7);
        private const string FileDateFormat = "yyyy-MM-dd";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly string _directory;
        private readonly IRecycleBin _recycleBin;
        private readonly Func<DateTime> _utcNow;
        private readonly List<JournalOperation> _operations = new List<JournalOperation>();

        public FileOperationJournal(string directory, IRecycleBin recycleBin, Func<DateTime> utcNow = null)
        {
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _recycleBin = recycleBin ?? throw new ArgumentNullException(nameof(recycleBin));
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <summary>오래된 것부터.</summary>
        public IReadOnlyList<JournalOperation> Operations => _operations;

        /// <summary>불러오다 건너뛴 줄·지우지 못한 오래된 기록 파일. 화면에 알리는 용도.</summary>
        public List<string> LoadWarnings { get; } = new List<string>();

        /// <summary>지금 되돌릴 수 있는 유일한 작업(되돌릴 거리가 남은 가장 최근 작업). 없으면 null.</summary>
        public JournalOperation UndoTarget => _operations.LastOrDefault(op => op.CanUndo);

        /// <summary><paramref name="destinationDirectory"/>에 이미 같은 이름이 있는 경로들 — 이동하면 교체된다(확인 창용).</summary>
        public static List<string> FindConflicts(IEnumerable<string> sources, string destinationDirectory)
        {
            return sources
                .Select(source => Path.Combine(destinationDirectory, Path.GetFileName(TrimEnd(source))))
                .Where(Exists)
                .ToList();
        }

        /// <summary>
        /// 폴더들 아래 파일 수. <paramref name="stopAt"/>에 닿으면 더 세지 않고 그 값을 돌려준다(큰 폴더 이동 확인용 — 기준만 넘으면 된다).
        /// 읽지 못하는 하위 폴더는 건너뛴다.
        /// </summary>
        public static int CountFiles(IEnumerable<string> folders, int stopAt)
        {
            var count = 0;
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
            foreach (var folder in folders.Where(Directory.Exists))
            {
                foreach (var _ in Directory.EnumerateFiles(folder, "*", options))
                {
                    if (++count >= stopAt)
                        return count;
                }
            }
            return count;
        }

        /// <summary>7일 지난 기록 파일을 지우고 남은 기록을 읽는다.</summary>
        public void Load()
        {
            _operations.Clear();
            LoadWarnings.Clear();
            if (!Directory.Exists(_directory))
                return;

            var oldest = _utcNow().Date - Retention;
            var files = new List<string>();
            foreach (var file in Directory.GetFiles(_directory, "*.jsonl"))
            {
                if (DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), FileDateFormat, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var date) && date < oldest)
                {
                    try { File.Delete(file); }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        LoadWarnings.Add("오래된 작업 이력을 지우지 못함: " + file + " — " + ex.Message);
                    }
                    continue;
                }
                files.Add(file);
            }

            var byId = new Dictionary<string, JournalOperation>();
            var ended = new HashSet<string>();
            foreach (var file in files.OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal))
            {
                var number = 0;
                foreach (var text in File.ReadLines(file, Encoding.UTF8))
                {
                    number++;
                    if (string.IsNullOrWhiteSpace(text))
                        continue;
                    Line line;
                    try { line = JsonSerializer.Deserialize<Line>(text, JsonOptions); }
                    catch (JsonException)
                    {
                        LoadWarnings.Add(string.Format("작업 이력 줄을 읽지 못해 건너뜀: {0} {1}번째 줄", Path.GetFileName(file), number));
                        continue;
                    }
                    Apply(line, byId, ended);
                }
            }

            foreach (var op in _operations)
            {
                if (op.State == JournalOperationState.Completed && !ended.Contains(op.Id))
                    op.State = JournalOperationState.Interrupted;
            }
        }

        /// <summary>
        /// 고른 항목을 <paramref name="destinationDirectory"/>로 옮긴다. 같은 이름이 있으면 그 옛 항목을 휴지통으로 보내고 교체한다
        /// (확인은 부르는 쪽이 <see cref="FindConflicts"/>로 먼저 받는다). 항목 하나가 실패해도 나머지는 계속한다.
        /// </summary>
        public JournalOperation Move(IEnumerable<string> sources, string destinationDirectory)
        {
            return Run(JournalOperationKind.Move, sources, source =>
            {
                FileOperationService.ValidateTransfer(source, destinationDirectory, true);
                var item = NewItem(source);
                var destination = Path.Combine(destinationDirectory, Path.GetFileName(TrimEnd(source)));
                if (Exists(destination))
                {
                    // 비교 기준이 겹치면(D:\a\b ↔ D:\a) 교체 대상이 원본을 품은 상위 폴더일 수 있다 — 휴지통에 보내면 원본도 같이 간다.
                    if (Path.GetFullPath(source).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination)) + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase))
                        throw new IOException("교체할 대상 폴더가 원본을 품고 있어 이동할 수 없습니다: " + destination);
                    item.ReplacedPath = destination;
                    item.RecycledUtc = _recycleBin.Send(destination);
                }
                try
                {
                    MoveEntry(source, destination, item.IsDirectory);
                }
                catch (Exception ex) when (item.ReplacedPath != null)
                {
                    // 교체하려고 휴지통에 보낸 옛 항목을 되살린다. 그것도 안 되면 오류에 남긴다.
                    try { _recycleBin.Restore(item.ReplacedPath, item.RecycledUtc.Value); }
                    catch (Exception restoreError)
                    {
                        throw new IOException(ex.Message + " (교체하려고 휴지통에 보낸 " + item.ReplacedPath + "도 되살리지 못함: " + restoreError.Message + ")", ex);
                    }
                    throw;
                }
                item.Destination = destination;
                CaptureFacts(item, destination);
                return item;
            });
        }

        /// <summary>붙여넣기와 같은 규칙으로 복사한다 — 같은 이름이 있으면 "이름 (2)"처럼 새 이름을 붙인다.</summary>
        public JournalOperation Copy(IEnumerable<string> sources, string destinationDirectory)
        {
            return Run(JournalOperationKind.Copy, sources, source =>
            {
                FileOperationService.ValidateTransfer(source, destinationDirectory, false);
                var item = NewItem(source);
                var destination = FileOperationService.GetUniquePath(
                    Path.Combine(destinationDirectory, Path.GetFileName(TrimEnd(source))), item.IsDirectory);
                if (item.IsDirectory)
                    FileOperationService.CopyDirectory(source, destination);
                else
                    File.Copy(source, destination);
                item.Destination = destination;
                CaptureFacts(item, destination);
                return item;
            });
        }

        /// <summary>휴지통으로 보내거나(되돌리기 가능) 영구 삭제한다(되돌리기 불가).</summary>
        public JournalOperation Delete(IEnumerable<string> paths, bool permanently)
        {
            var kind = permanently ? JournalOperationKind.DeletePermanently : JournalOperationKind.Delete;
            return Run(kind, paths, path =>
            {
                var item = NewItem(path);
                if (permanently)
                {
                    if (item.IsDirectory) Directory.Delete(path, true);
                    else File.Delete(path);
                }
                else
                {
                    item.RecycledUtc = _recycleBin.Send(path);
                }
                return item;
            });
        }

        /// <summary>되돌리기를 막는 이유들. 비어 있으면 되돌릴 수 있다. 아무것도 바꾸지 않는다.</summary>
        public List<string> CheckUndo(JournalOperation op)
        {
            var problems = new List<string>();
            if (op == null || !op.CanUndo)
            {
                problems.Add("되돌릴 수 없는 작업입니다.");
                return problems;
            }

            foreach (var item in Pending(op))
                problems.AddRange(ItemProblems(op.Kind, item));
            return problems;
        }

        /// <summary>항목 하나를 되돌리지 못하게 막는 이유들(<see cref="CheckUndo"/>와 같은 판정). 비어 있으면 그 항목은 되돌릴 수 있다.</summary>
        public List<string> ItemProblems(JournalOperationKind kind, JournalItem item)
        {
            var problems = new List<string>();
            switch (kind)
            {
                case JournalOperationKind.Move:
                    if (!Unchanged(item, item.Destination))
                        problems.Add("옮긴 뒤 바뀌었거나 없음: " + item.Destination);
                    if (Exists(item.Source))
                        problems.Add("원래 자리에 같은 이름이 새로 있음: " + item.Source);
                    if (item.ReplacedPath != null && !_recycleBin.CanRestore(item.ReplacedPath, item.RecycledUtc.Value))
                        problems.Add("교체한 옛 항목이 휴지통에 없음: " + item.ReplacedPath);
                    break;
                case JournalOperationKind.Copy:
                    if (!Unchanged(item, item.Destination))
                        problems.Add("복사한 뒤 바뀌었거나 없음: " + item.Destination);
                    else if (!_recycleBin.CanSend(item.Destination))
                        problems.Add("휴지통을 쓸 수 없는 위치라 복사본을 지우지 않음(직접 지우세요): " + item.Destination);
                    if (item.ReplacedPath != null && !_recycleBin.CanRestore(item.ReplacedPath, item.RecycledUtc.Value))
                        problems.Add("교체한 옛 항목이 휴지통에 없음: " + item.ReplacedPath);
                    break;
                case JournalOperationKind.Delete:
                    if (Exists(item.Source))
                        problems.Add("원래 자리에 같은 이름이 새로 있음: " + item.Source);
                    if (!_recycleBin.CanRestore(item.Source, item.RecycledUtc.Value))
                        problems.Add("휴지통에 없음: " + item.Source);
                    break;
                case JournalOperationKind.Rename:
                    if (!Unchanged(item, item.Destination))
                        problems.Add("이름을 바꾼 뒤 바뀌었거나 없음: " + item.Destination);
                    // 대소문자만 바꾼 이름이면 원래 이름이 곧 지금 항목이다.
                    if (!string.Equals(item.Source, item.Destination, StringComparison.OrdinalIgnoreCase) && Exists(item.Source))
                        problems.Add("원래 이름으로 된 항목이 새로 있음: " + item.Source);
                    break;
                case JournalOperationKind.CreateFolder:
                    if (!Directory.Exists(item.Destination))
                        problems.Add("만든 폴더가 없음: " + item.Destination);
                    else if (Directory.EnumerateFileSystemEntries(item.Destination).Any())
                        problems.Add("만든 폴더에 항목이 생겨 지우지 않음: " + item.Destination);
                    break;
                case JournalOperationKind.CreateFile:
                    if (!Unchanged(item, item.Destination))
                        problems.Add("만든 파일이 바뀌었거나 없어 지우지 않음: " + item.Destination);
                    break;
            }
            return problems;
        }

        /// <summary>
        /// 작업을 되돌린다. 최근 작업(<see cref="UndoTarget"/>)만 되고, <see cref="CheckUndo"/>에 걸리면 아무것도 하지 않는다.
        /// 도중에 실패하면 거기서 멈추고(이미 되돌린 항목은 기록됨) 예외를 던진다 — 다시 부르면 남은 항목만 한다.
        /// </summary>
        public void Undo(JournalOperation op)
        {
            if (op == null || !ReferenceEquals(op, UndoTarget))
                throw new InvalidOperationException("최근 작업부터 차례로 되돌릴 수 있습니다.");
            var problems = CheckUndo(op);
            if (problems.Count > 0)
                throw new InvalidOperationException("되돌릴 수 없습니다.\n" + string.Join("\n", problems));

            foreach (var item in Pending(op).Reverse().ToList())
            {
                var index = op.Items.IndexOf(item);
                try
                {
                    Revert(op.Kind, item);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    op.UndoError = ex.Message;
                    Append(new Line { T = "revert", Id = op.Id, I = index, Ok = false, Err = ex.Message, At = _utcNow() });
                    throw new IOException("되돌리기 중 실패: " + ex.Message, ex);
                }
                item.Reverted = true;
                Append(new Line { T = "revert", Id = op.Id, I = index, Ok = true, At = _utcNow() });
            }
            op.UndoError = null;
            op.State = JournalOperationState.Undone;
        }

        /// <summary>되돌리지 않고 넘긴다(최근 작업만). 그 앞 작업이 되돌리기 대상이 된다.</summary>
        public void Skip(JournalOperation op)
        {
            if (op == null || !ReferenceEquals(op, UndoTarget))
                throw new InvalidOperationException("최근 작업부터 차례로 넘길 수 있습니다.");
            Append(new Line { T = "skip", Id = op.Id, At = _utcNow() });
            op.State = JournalOperationState.Skipped;
        }

        /// <summary>
        /// 되돌릴 수 있는 항목만 되돌린다(최근 작업만). 그 사이 바뀌었거나 원래 자리가 차 있는 등 막힌 항목은 건드리지 않고 이유를 돌려준다.
        /// 막힌 항목이 남으면 작업을 "넘김"으로 기록해 그 앞 작업이 다음 되돌리기 대상이 된다.
        /// <paramref name="token"/>으로 멈추거나 도중에 실패하면 거기서 멈춘다(되돌린 항목은 기록됨) — 다시 부르면 남은 항목만 한다.
        /// </summary>
        public List<string> UndoAvailable(JournalOperation op, System.Threading.CancellationToken token = default)
        {
            if (op == null || !ReferenceEquals(op, UndoTarget))
                throw new InvalidOperationException("최근 작업부터 차례로 되돌릴 수 있습니다.");

            var blocked = new List<string>();
            var ready = new List<JournalItem>();
            foreach (var item in Pending(op))
            {
                var problems = ItemProblems(op.Kind, item);
                if (problems.Count > 0)
                    blocked.AddRange(problems);
                else
                    ready.Add(item);
            }

            ready.Reverse();
            foreach (var item in ready)
            {
                token.ThrowIfCancellationRequested();
                var index = op.Items.IndexOf(item);
                try
                {
                    Revert(op.Kind, item);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    op.UndoError = ex.Message;
                    Append(new Line { T = "revert", Id = op.Id, I = index, Ok = false, Err = ex.Message, At = _utcNow() });
                    throw new IOException("되돌리기 중 실패: " + ex.Message, ex);
                }
                item.Reverted = true;
                Append(new Line { T = "revert", Id = op.Id, I = index, Ok = true, At = _utcNow() });
            }

            op.UndoError = null;
            if (blocked.Count > 0)
            {
                Append(new Line { T = "skip", Id = op.Id, At = _utcNow() });
                op.State = JournalOperationState.Skipped;
            }
            else
            {
                op.State = JournalOperationState.Undone;
            }
            return blocked;
        }

        // ── 외부 실행기용 기록(메인 패널 파일 작업 대기열) ─────────────────────
        // 실행은 부르는 쪽이 하고(진행률·일시정지·충돌 선택), 이력은 같은 형식으로 남긴다.
        // 순서: Begin(시작 기록 — 실패하면 예외, 아무것도 남지 않음) → 항목마다 AddItem → End.

        /// <summary>작업 시작을 기록하고 이력에 넣는다. 기록하지 못하면 예외.</summary>
        public JournalOperation Begin(JournalOperationKind kind, IEnumerable<string> plan)
        {
            var sources = (plan ?? Enumerable.Empty<string>()).Select(TrimEnd).ToList();
            var op = new JournalOperation { Id = Guid.NewGuid().ToString("N"), StartedUtc = _utcNow(), Kind = kind };
            Append(new Line { T = "begin", Id = op.Id, At = op.StartedUtc, Kind = kind.ToString(), Plan = sources });
            _operations.Add(op);
            return op;
        }

        /// <summary>항목 결과를 더한다. 파일에 쓰지 못하면 작업을 중단됨으로 표시하고 예외(메모리에는 남아 이번 실행 중에는 되돌릴 수 있음).</summary>
        public void AddItem(JournalOperation op, JournalItem item)
        {
            op.Items.Add(item);
            try
            {
                Append(ItemLine(op.Id, op.Items.Count - 1, item));
            }
            catch
            {
                op.State = JournalOperationState.Interrupted;
                throw;
            }
        }

        /// <summary>작업 끝을 기록한다. 쓰지 못하면 중단됨으로 표시하고 예외.</summary>
        public void End(JournalOperation op)
        {
            try
            {
                Append(new Line { T = "end", Id = op.Id, At = _utcNow() });
            }
            catch
            {
                op.State = JournalOperationState.Interrupted;
                throw;
            }
        }

        /// <summary>도중에 기록을 못 이어 쓴 작업을 중단됨으로 표시한다(메모리만).</summary>
        public static void MarkInterrupted(JournalOperation op)
        {
            if (op != null && op.State == JournalOperationState.Completed)
                op.State = JournalOperationState.Interrupted;
        }

        private JournalOperation Run(JournalOperationKind kind, IEnumerable<string> paths, Func<string, JournalItem> execute)
        {
            var sources = paths.Select(TrimEnd).ToList();
            var op = new JournalOperation { Id = Guid.NewGuid().ToString("N"), StartedUtc = _utcNow(), Kind = kind };

            // 시작을 못 쓰면 예외가 그대로 나가 아무것도 실행하지 않는다.
            Append(new Line { T = "begin", Id = op.Id, At = op.StartedUtc, Kind = kind.ToString(), Plan = sources });
            _operations.Add(op);

            try
            {
                foreach (var source in sources)
                {
                    JournalItem item;
                    try
                    {
                        item = execute(source);
                        item.Succeeded = true;
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException ||
                                               ex is NotSupportedException || ex is InvalidOperationException)
                    {
                        item = new JournalItem { Source = source, Error = ex.Message };
                    }
                    op.Items.Add(item);
                    Append(ItemLine(op.Id, op.Items.Count - 1, item));
                }
                Append(new Line { T = "end", Id = op.Id, At = _utcNow() });
            }
            catch
            {
                op.State = JournalOperationState.Interrupted;
                throw;
            }
            return op;
        }

        private void Revert(JournalOperationKind kind, JournalItem item)
        {
            switch (kind)
            {
                case JournalOperationKind.Move:
                    var parent = Path.GetDirectoryName(item.Source);
                    if (!string.IsNullOrEmpty(parent))
                        Directory.CreateDirectory(parent);
                    MoveEntry(item.Destination, item.Source, item.IsDirectory);
                    if (item.ReplacedPath != null)
                        _recycleBin.Restore(item.ReplacedPath, item.RecycledUtc.Value);
                    break;
                case JournalOperationKind.Copy:
                    _recycleBin.Send(item.Destination);
                    if (item.ReplacedPath != null)
                        _recycleBin.Restore(item.ReplacedPath, item.RecycledUtc.Value);
                    break;
                case JournalOperationKind.Delete:
                    _recycleBin.Restore(item.Source, item.RecycledUtc.Value);
                    break;
                case JournalOperationKind.Rename:
                    MoveEntry(item.Destination, item.Source, item.IsDirectory);
                    break;
                case JournalOperationKind.CreateFolder:
                    Directory.Delete(item.Destination, false);   // 비어 있을 때만 지워진다
                    break;
                case JournalOperationKind.CreateFile:
                    File.Delete(item.Destination);
                    break;
            }
        }

        /// <summary>파일은 드라이브가 달라도 File.Move가 처리한다. 폴더는 드라이브가 다르면 Directory.Move가 안 되므로 복사 후 원본 삭제.</summary>
        private static void MoveEntry(string source, string destination, bool isDirectory)
        {
            if (!isDirectory)
            {
                File.Move(source, destination);
                return;
            }
            if (string.Equals(Path.GetPathRoot(Path.GetFullPath(source)), Path.GetPathRoot(Path.GetFullPath(destination)),
                    StringComparison.OrdinalIgnoreCase))
            {
                Directory.Move(source, destination);
                return;
            }

            try
            {
                FileOperationService.CopyDirectory(source, destination);
            }
            catch
            {
                // 복사하다 만 대상은 지운다(원본은 그대로).
                if (Directory.Exists(destination))
                    Directory.Delete(destination, true);
                throw;
            }
            try
            {
                Directory.Delete(source, true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new IOException("다른 드라이브로 복사는 됐지만 원본 일부를 지우지 못했습니다(양쪽에 남아 있음): " + ex.Message, ex);
            }
        }

        private void Apply(Line line, Dictionary<string, JournalOperation> byId, HashSet<string> ended)
        {
            if (line?.T == null || line.Id == null)
                return;

            if (line.T == "begin")
            {
                if (byId.ContainsKey(line.Id) || !Enum.TryParse(line.Kind, out JournalOperationKind kind))
                    return;
                var op = new JournalOperation { Id = line.Id, StartedUtc = line.At ?? DateTime.MinValue, Kind = kind };
                byId[line.Id] = op;
                _operations.Add(op);
                return;
            }

            // 시작 줄이 (7일 삭제 등으로) 없는 작업의 나머지 줄은 무시한다.
            if (!byId.TryGetValue(line.Id, out var target))
                return;

            switch (line.T)
            {
                case "item":
                    target.Items.Add(new JournalItem
                    {
                        Source = line.Src,
                        Destination = line.Dst,
                        IsDirectory = line.Dir ?? false,
                        ReplacedPath = line.Replaced,
                        RecycledUtc = line.BinAt,
                        Size = line.Size,
                        ModifiedUtc = line.Mtime,
                        Succeeded = line.Ok ?? false,
                        Error = line.Err
                    });
                    break;
                case "end":
                    ended.Add(line.Id);
                    break;
                case "revert":
                    if (line.I is int index && index >= 0 && index < target.Items.Count)
                    {
                        if (line.Ok == true)
                            target.Items[index].Reverted = true;
                        else
                            target.UndoError = line.Err;
                    }
                    if (target.Items.Where(item => item.Succeeded).All(item => item.Reverted))
                        target.State = JournalOperationState.Undone;
                    break;
                case "skip":
                    target.State = JournalOperationState.Skipped;
                    break;
            }
        }

        private void Append(Line line)
        {
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, _utcNow().ToString(FileDateFormat, CultureInfo.InvariantCulture) + ".jsonl");
            File.AppendAllText(path, JsonSerializer.Serialize(line, JsonOptions) + "\n", new UTF8Encoding(false));
        }

        private static Line ItemLine(string id, int index, JournalItem item) => new Line
        {
            T = "item",
            Id = id,
            I = index,
            Src = item.Source,
            Dst = item.Destination,
            Dir = item.IsDirectory ? true : (bool?)null,
            Replaced = item.ReplacedPath,
            BinAt = item.RecycledUtc,
            Size = item.Size,
            Mtime = item.ModifiedUtc,
            Ok = item.Succeeded,
            Err = item.Error
        };

        private static IEnumerable<JournalItem> Pending(JournalOperation op) => op.Items.Where(item => item.Succeeded && !item.Reverted);

        private static JournalItem NewItem(string source)
        {
            if (Directory.Exists(source))
                return new JournalItem { Source = source, IsDirectory = true };
            if (File.Exists(source))
                return new JournalItem { Source = source };
            throw new FileNotFoundException("항목이 없습니다: " + source, source);
        }

        /// <summary>파일이면 결과의 크기·수정 시각을 적어 둔다(되돌리기 전에 그 사이 바뀌었는지 본다).</summary>
        internal static void CaptureFacts(JournalItem item, string path)
        {
            if (item.IsDirectory)
                return;
            var info = new FileInfo(path);
            item.Size = info.Length;
            item.ModifiedUtc = info.LastWriteTimeUtc;
        }

        private static bool Unchanged(JournalItem item, string path)
        {
            if (item.IsDirectory)
                return Directory.Exists(path);
            var info = new FileInfo(path);
            return info.Exists && info.Length == item.Size && info.LastWriteTimeUtc == item.ModifiedUtc;
        }

        private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

        private static string TrimEnd(string path) => Path.TrimEndingDirectorySeparator(path ?? string.Empty);

        /// <summary>jsonl 한 줄. 종류(t)에 따라 쓰는 칸이 다르고 안 쓰는 칸은 저장하지 않는다.</summary>
        private sealed class Line
        {
            [JsonPropertyName("t")] public string T { get; set; }
            [JsonPropertyName("id")] public string Id { get; set; }
            [JsonPropertyName("at")] public DateTime? At { get; set; }
            [JsonPropertyName("kind")] public string Kind { get; set; }
            [JsonPropertyName("plan")] public List<string> Plan { get; set; }
            [JsonPropertyName("i")] public int? I { get; set; }
            [JsonPropertyName("src")] public string Src { get; set; }
            [JsonPropertyName("dst")] public string Dst { get; set; }
            [JsonPropertyName("dir")] public bool? Dir { get; set; }
            [JsonPropertyName("replaced")] public string Replaced { get; set; }
            [JsonPropertyName("binAt")] public DateTime? BinAt { get; set; }
            [JsonPropertyName("size")] public long? Size { get; set; }
            [JsonPropertyName("mtime")] public DateTime? Mtime { get; set; }
            [JsonPropertyName("ok")] public bool? Ok { get; set; }
            [JsonPropertyName("err")] public string Err { get; set; }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Folderss.Services
{
    /// <summary>
    /// 메인 패널 파일 작업 대기열. 복사·이동·삭제·실행 취소를 작업 스레드 하나에서 넣은 순서대로 실행하고,
    /// 결과를 메인 패널 작업 이력(<see cref="FileOperationJournal"/>, 폴더 비교 창과 다른 폴더)에 남긴다.
    /// 이름 변경·새 폴더·새 파일은 바로 끝나는 작업이라 부르는 스레드에서 실행하고 기록만 같이 한다.
    /// 이력 객체는 스레드 안전하지 않으므로 모든 접근을 <see cref="_journalLock"/>으로 감싼다.
    /// UI 없이 동작한다 — 충돌 질문(<see cref="AskConflict"/>)과 끝 알림(<see cref="JobFinished"/>)은 작업 스레드에서 불리므로 UI가 넘겨받는다.
    /// </summary>
    public sealed class FileJobQueue
    {
        /// <summary>끝난 작업은 이만큼만 목록에 남긴다.</summary>
        private const int KeepFinished = 30;
        /// <summary>폴더 링크가 자기 상위를 가리키는 순환 등에서 끝없이 내려가지 않게.</summary>
        private const int MaxDepth = 256;
        private const int ErrorNotSameDevice = 17;

        private readonly FileOperationJournal _journal;
        private readonly IRecycleBin _recycleBin;
        private readonly object _journalLock = new object();
        private readonly object _queueLock = new object();
        private readonly Queue<FileJob> _pending = new Queue<FileJob>();
        private readonly List<FileJob> _jobs = new List<FileJob>();
        private readonly List<TaskCompletionSource<bool>> _idleWaiters = new List<TaskCompletionSource<bool>>();
        private Thread _worker;
        private FileJob _current;

        public FileJobQueue(FileOperationJournal journal, IRecycleBin recycleBin)
        {
            _journal = journal ?? throw new ArgumentNullException(nameof(journal));
            _recycleBin = recycleBin ?? throw new ArgumentNullException(nameof(recycleBin));
        }

        /// <summary>이름 충돌 질문. 작업 스레드에서 불린다. 없으면 건너뛰기.</summary>
        public Func<FileJob, ConflictInfo, ConflictAnswer> AskConflict { get; set; }

        /// <summary>작업이 끝났다(작업 스레드에서).</summary>
        public event Action<FileJob> JobFinished;

        /// <summary>대기열 목록이 바뀌었다(넣기·시작·끝·지우기, 아무 스레드에서나).</summary>
        public event Action JobsChanged;

        /// <summary>대기 중이거나 실행 중인 작업이 있다.</summary>
        public bool IsBusy { get { lock (_queueLock) return _current != null || _pending.Count > 0; } }

        /// <summary>실행 취소 작업이 진행 중이다(그동안 이름 변경 등 기록이 필요한 즉시 작업을 막는다).</summary>
        public bool IsUndoRunning { get { lock (_queueLock) return _current?.Kind == FileJobKind.Undo; } }

        public IReadOnlyList<FileJob> Jobs { get { lock (_queueLock) return _jobs.ToList(); } }

        /// <summary>작업을 대기열 끝에 넣는다.</summary>
        public FileJob Enqueue(FileJob job)
        {
            lock (_queueLock)
            {
                _jobs.Add(job);
                _pending.Enqueue(job);
                if (_worker == null)
                {
                    // 셸 휴지통 보내기(SHFileOperation)를 부르므로 STA 스레드로 둔다.
                    _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "Folderss file jobs" };
                    if (OperatingSystem.IsWindows())
                        _worker.SetApartmentState(ApartmentState.STA);
                    _worker.Start();
                }
                Monitor.PulseAll(_queueLock);
            }
            JobsChanged?.Invoke();
            return job;
        }

        /// <summary>끝난 작업을 목록에서 지운다.</summary>
        public void ClearFinished()
        {
            lock (_queueLock)
                _jobs.RemoveAll(job => job.IsFinished);
            JobsChanged?.Invoke();
        }

        /// <summary>대기 중인 작업과 진행 중인 작업을 모두 취소한다.</summary>
        public void CancelAll()
        {
            foreach (var job in Jobs.Where(job => !job.IsFinished))
                job.Cancel();
        }

        /// <summary>대기열이 빌 때 끝나는 작업.</summary>
        public Task WhenIdleAsync()
        {
            lock (_queueLock)
            {
                if (_current == null && _pending.Count == 0)
                    return Task.CompletedTask;
                var waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _idleWaiters.Add(waiter);
                return waiter.Task;
            }
        }

        private void WorkerLoop()
        {
            while (true)
            {
                FileJob job;
                lock (_queueLock)
                {
                    while (_pending.Count == 0)
                        Monitor.Wait(_queueLock);
                    job = _pending.Dequeue();
                    _current = job;
                }
                JobsChanged?.Invoke();

                try
                {
                    Run(job);
                }
                catch (Exception ex)
                {
                    // 예상 못 한 예외로 작업 스레드가 죽으면 대기열 전체가 멈춘다. 그 작업만 실패로 남긴다.
                    job.AddError("예상하지 못한 오류: " + ex.Message);
                    job.Finish(FileJobState.Failed);
                }

                List<TaskCompletionSource<bool>> waiters = null;
                lock (_queueLock)
                {
                    _current = null;
                    var finished = _jobs.Where(item => item.IsFinished).ToList();
                    if (finished.Count > KeepFinished)
                        foreach (var old in finished.Take(finished.Count - KeepFinished))
                            _jobs.Remove(old);
                    if (_pending.Count == 0)
                    {
                        waiters = _idleWaiters.ToList();
                        _idleWaiters.Clear();
                    }
                }
                JobsChanged?.Invoke();
                try
                {
                    JobFinished?.Invoke(job);
                }
                finally
                {
                    waiters?.ForEach(waiter => waiter.TrySetResult(true));
                }
            }
        }

        // ── 작업 실행 ───────────────────────────────────────────────

        private void Run(FileJob job)
        {
            if (job.IsCancellationRequested)
            {
                job.Finish(FileJobState.Cancelled);
                return;
            }
            if (job.Kind == FileJobKind.Undo)
            {
                RunUndo(job);
                return;
            }
            if ((job.Kind == FileJobKind.Copy || job.Kind == FileJobKind.Move) &&
                (string.IsNullOrWhiteSpace(job.Destination) || !Directory.Exists(job.Destination)))
            {
                job.AddError("대상 폴더가 없습니다: " + job.Destination);
                job.Finish(FileJobState.Failed);
                return;
            }

            JournalOperation op = null;
            var state = FileJobState.Completed;
            try
            {
                job.SetState(FileJobState.Preparing);
                if (job.Kind == FileJobKind.Copy || job.Kind == FileJobKind.Move)
                    Measure(job);

                op = BeginHistory(job);
                job.SetState(FileJobState.Running);
                var policy = new ConflictPolicy();
                for (var index = 0; index < job.Sources.Count; index++)
                {
                    job.Checkpoint();
                    var source = Path.TrimEndingDirectorySeparator(job.Sources[index]);
                    JournalItem item;
                    if (job.Kind == FileJobKind.Delete || job.Kind == FileJobKind.DeletePermanently)
                        item = DeleteItem(job, source);
                    else
                        item = TransferItem(job, source, policy, job.Sources.Count - index - 1);
                    if (item != null)
                        op = Record(job, op, item);
                }
            }
            catch (OperationCanceledException)
            {
                state = FileJobState.Cancelled;
                var done = job.Snapshot().DoneItems;
                job.AddNote(done == 0
                    ? "취소됨 — 진행 중이던 항목은 원래대로 돌렸습니다(끝난 항목 없음)."
                    : string.Format("취소됨 — 진행 중이던 항목은 원래대로 돌렸고, 끝난 항목 {0}개는 남아 있습니다(실행 취소로 되돌릴 수 있음).", done));
            }
            finally
            {
                EndHistory(job, op);
                job.Finish(state);
            }
        }

        /// <summary>복사·이동할 파일 수와 크기. 같은 드라이브 안의 이동은 이름만 바뀌므로 세지 않는다.</summary>
        private void Measure(FileJob job)
        {
            var files = 0;
            long bytes = 0;
            foreach (var source in job.Sources)
            {
                job.Checkpoint();
                if (job.Kind == FileJobKind.Move && SameVolume(source, job.Destination))
                    continue;
                if (File.Exists(source))
                {
                    files++;
                    bytes += SafeLength(source);
                }
                else if (Directory.Exists(source))
                {
                    job.SetCurrent("파일 세는 중: " + source);
                    CountTree(job, source, ref files, ref bytes, 0);
                }
            }
            job.SetTotals(files, bytes);
        }

        private static void CountTree(FileJob job, string folder, ref int files, ref long bytes, int depth)
        {
            if (depth > MaxDepth)
                return;
            job.Checkpoint();
            try
            {
                foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", Enumerate))
                {
                    files++;
                    bytes += file.Length;
                }
                foreach (var directory in Directory.EnumerateDirectories(folder, "*", Enumerate))
                    CountTree(job, directory, ref files, ref bytes, depth + 1);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // 세기는 진행률용이다. 읽지 못하는 폴더는 실제 복사에서 오류로 알린다.
            }
        }

        private static readonly EnumerationOptions Enumerate = new EnumerationOptions
        {
            AttributesToSkip = 0,
            IgnoreInaccessible = false,
            RecurseSubdirectories = false
        };

        /// <summary>
        /// 최상위 항목 하나를 복사·이동한다. 건너뛰면 null, 실패하면 실패 항목. 취소되면 이 항목을 원래대로 돌리고 예외를 다시 던진다.
        /// </summary>
        private JournalItem TransferItem(FileJob job, string source, ConflictPolicy policy, int remaining)
        {
            var move = job.Kind == FileJobKind.Move;
            var destinationDirectory = job.Destination;
            job.SetCurrent(source);
            var isDirectory = Directory.Exists(source);
            var item = new JournalItem { Source = source, IsDirectory = isDirectory };
            try
            {
                if (!isDirectory && !File.Exists(source))
                    throw new FileNotFoundException("항목이 없습니다: " + source, source);
                FileOperationService.ValidateTransfer(source, destinationDirectory, move);

                var target = Path.Combine(destinationDirectory, Path.GetFileName(source));
                var replace = false;
                if (Exists(target))
                {
                    var choice = SamePath(source, target)
                        ? ConflictChoice.KeepBoth   // 같은 폴더에 복사: 자기 자신과의 충돌이라 번호를 붙인다(기존 붙여넣기 동작)
                        : ResolveConflict(job, policy, source, target, isDirectory, move, remaining);
                    if (choice == ConflictChoice.Skip)
                    {
                        job.ItemSkipped();
                        return null;
                    }
                    if (choice == ConflictChoice.KeepBoth)
                    {
                        target = FileOperationService.GetUniquePath(target, isDirectory);
                    }
                    else
                    {
                        var blocked = ReplaceBlockedReason(source, target);
                        if (blocked != null)
                            throw new IOException(blocked);
                        replace = true;
                    }
                }

                if (move && SameVolume(source, destinationDirectory) && TryMoveInPlace(item, source, target, isDirectory, replace))
                {
                    // 같은 드라이브: 이름만 바뀐다.
                }
                else
                {
                    CopyThenSwap(job, item, source, target, isDirectory, replace, move);
                }

                item.Destination = target;
                item.Succeeded = true;
                FileOperationJournal.CaptureFacts(item, target);
                job.ItemDone();
                return item;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (IsItemError(ex))
            {
                item.Succeeded = false;
                item.Error = ex.Message;
                job.ItemFailed(Path.GetFileName(source) + ": " + ex.Message);
                return item;
            }
        }

        private ConflictChoice ResolveConflict(FileJob job, ConflictPolicy policy, string source, string target, bool isDirectory, bool move, int remaining)
        {
            var targetIsDirectory = Directory.Exists(target);
            var info = new ConflictInfo
            {
                SourcePath = source,
                DestinationPath = target,
                IsMove = move,
                SourceIsDirectory = isDirectory,
                DestinationIsDirectory = targetIsDirectory,
                SourceSize = isDirectory ? (long?)null : SafeLength(source),
                DestinationSize = targetIsDirectory ? (long?)null : SafeLength(target),
                SourceModifiedUtc = SafeModified(source),
                DestinationModifiedUtc = SafeModified(target),
                RemainingItems = remaining
            };
            info.ReplaceBlockedReason = ReplaceBlockedReason(source, target);
            info.CanReplace = info.ReplaceBlockedReason == null;

            var ask = AskConflict;
            job.SetState(FileJobState.WaitingForAnswer);
            ConflictChoice? choice;
            try
            {
                choice = policy.Resolve(info, ask == null ? (Func<ConflictInfo, ConflictAnswer>)null : conflict => ask(job, conflict));
            }
            finally
            {
                job.SetState(FileJobState.Running);
            }
            if (choice == null)
            {
                job.Cancel();
                throw new OperationCanceledException();
            }
            return choice.Value;
        }

        /// <summary>바꾸기를 할 수 없는 이유. 바꿀 수 있으면 null.</summary>
        private string ReplaceBlockedReason(string source, string target)
        {
            // 원본을 품은 상위 폴더를 휴지통에 보내면 원본도 같이 간다.
            if (Path.GetFullPath(source).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(target)) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                return "바꿀 대상 폴더가 원본을 품고 있어 바꿀 수 없습니다: " + target;
            if (!_recycleBin.CanSend(target))
                return "휴지통을 쓸 수 없는 위치(네트워크·이동식 드라이브 또는 긴 경로)라 기존 항목을 바꾸지 않았습니다: " + target;
            return null;
        }

        /// <summary>같은 드라이브 안 이동. 드라이브가 같은데 장치가 달라(마운트 폴더 등) 옮기지 못하면 false — 복사 후 삭제로 한다.</summary>
        private bool TryMoveInPlace(JournalItem item, string source, string target, bool isDirectory, bool replace)
        {
            if (replace)
            {
                item.ReplacedPath = target;
                item.RecycledUtc = _recycleBin.Send(target);
            }
            try
            {
                if (isDirectory)
                    Directory.Move(source, target);
                else
                    File.Move(source, target);
                return true;
            }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) == ErrorNotSameDevice && !Exists(target))
            {
                RestoreReplaced(item, ex);
                return false;
            }
            catch (Exception ex)
            {
                RestoreReplaced(item, ex);
                throw;
            }
        }

        /// <summary>
        /// 복사(다른 드라이브 이동이면 복사 뒤 원본 삭제). 바꾸기면 먼저 임시 이름으로 다 복사한 다음 옛 항목을 휴지통으로 보내고 이름을 바꾼다 —
        /// 복사가 실패·취소되면 옛 항목은 그대로다. 실패·취소 시 만들던 복사본은 지운다.
        /// </summary>
        private void CopyThenSwap(FileJob job, JournalItem item, string source, string target, bool isDirectory, bool replace, bool move)
        {
            var work = replace ? TemporaryPath(target) : target;
            var fileErrors = new List<string>();
            try
            {
                if (isDirectory)
                    CopyTree(job, source, work, fileErrors, stopOnError: move, depth: 0);
                else
                    CopyOneFile(job, source, work);
            }
            catch
            {
                Cleanup(work);
                throw;
            }

            if (replace)
            {
                try
                {
                    item.RecycledUtc = _recycleBin.Send(target);
                    item.ReplacedPath = target;
                }
                catch
                {
                    Cleanup(work);
                    throw;
                }
                try
                {
                    if (isDirectory)
                        Directory.Move(work, target);
                    else
                        File.Move(work, target);
                }
                catch (Exception ex)
                {
                    RestoreReplaced(item, ex);
                    Cleanup(work);
                    throw;
                }
            }

            if (fileErrors.Count > 0)
            {
                item.Error = string.Format("파일 {0}개를 복사하지 못했습니다", fileErrors.Count);
                foreach (var error in fileErrors)
                    job.AddError(Path.GetFileName(source) + "\\" + error);
            }

            if (!move)
                return;

            try
            {
                FileCopier.DeletePath(source);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                if (!isDirectory)
                {
                    // 파일은 원본이 그대로 남아 있으니 복사본을 지워 이동 전으로 되돌린다.
                    Cleanup(target);
                    if (item.ReplacedPath != null)
                        RestoreReplaced(item, ex);
                    throw new IOException("원본을 지울 수 없어(다른 프로그램이 사용 중일 수 있음) 이동하지 않았습니다: " + ex.Message, ex);
                }
                // 폴더는 일부가 이미 지워졌을 수 있어 복사본을 지우면 안 된다. 성공으로 기록하고 남은 원본을 알린다.
                item.Error = "다른 드라이브로 복사는 됐지만 원본 일부를 지우지 못했습니다(양쪽에 남음): " + ex.Message;
                job.AddError(Path.GetFileName(source) + ": " + item.Error);
            }
        }

        private static void CopyTree(FileJob job, string source, string destination, List<string> fileErrors, bool stopOnError, int depth)
        {
            if (depth > MaxDepth)
                throw new IOException("폴더가 너무 깊거나 자기 자신을 가리키는 링크 폴더가 있습니다: " + source);
            job.Checkpoint();
            Directory.CreateDirectory(destination);

            IEnumerable<FileInfo> files;
            IEnumerable<string> directories;
            try
            {
                files = new DirectoryInfo(source).EnumerateFiles("*", Enumerate).ToList();
                directories = Directory.EnumerateDirectories(source, "*", Enumerate).ToList();
            }
            catch (Exception ex) when (!stopOnError && (ex is IOException || ex is UnauthorizedAccessException))
            {
                fileErrors.Add(Relative(source) + ": " + ex.Message);
                return;
            }

            foreach (var file in files)
            {
                try
                {
                    CopyOneFile(job, file.FullName, Path.Combine(destination, file.Name));
                }
                catch (Exception ex) when (!stopOnError && (ex is IOException || ex is UnauthorizedAccessException))
                {
                    fileErrors.Add(file.Name + ": " + ex.Message);
                }
            }
            foreach (var directory in directories)
                CopyTree(job, directory, Path.Combine(destination, Path.GetFileName(directory)), fileErrors, stopOnError, depth + 1);

            // 숨김·시스템 폴더 속성은 살린다(읽기 전용은 지우기·되돌리기를 막을 수 있어 옮기지 않음).
            var attributes = File.GetAttributes(source) & (FileAttributes.Hidden | FileAttributes.System);
            if (attributes != 0)
                File.SetAttributes(destination, File.GetAttributes(destination) | attributes);
        }

        private static void CopyOneFile(FileJob job, string source, string destination)
        {
            var size = SafeLength(source);
            job.BeginFile(source);
            var ok = false;
            try
            {
                FileCopier.CopyFile(source, destination, job.ReportFileBytes, job.Checkpoint);
                ok = true;
            }
            finally
            {
                job.EndFile(size, ok);
            }
        }

        private JournalItem DeleteItem(FileJob job, string path)
        {
            job.SetCurrent(path);
            var isDirectory = Directory.Exists(path);
            var item = new JournalItem { Source = path, IsDirectory = isDirectory };
            try
            {
                if (!isDirectory && !File.Exists(path))
                    throw new FileNotFoundException("항목이 없습니다: " + path, path);
                if (job.Kind == FileJobKind.DeletePermanently)
                {
                    FileCopier.DeletePath(path);
                }
                else
                {
                    if (!_recycleBin.CanSend(path))
                        throw new IOException("휴지통을 쓸 수 없는 위치(네트워크·이동식 드라이브 또는 긴 경로)라 지우지 않았습니다.");
                    item.RecycledUtc = _recycleBin.Send(path);
                }
                item.Succeeded = true;
                job.ItemDone();
                return item;
            }
            catch (Exception ex) when (IsItemError(ex))
            {
                item.Error = ex.Message;
                job.ItemFailed(Path.GetFileName(path) + ": " + ex.Message);
                return item;
            }
        }

        private void RunUndo(FileJob job)
        {
            job.SetState(FileJobState.Running);
            job.SetCurrent("되돌리는 중…");
            var state = FileJobState.Completed;
            try
            {
                List<string> blocked;
                lock (_journalLock)
                    blocked = _journal.UndoAvailable(job.UndoOperation, job.Token);
                foreach (var problem in blocked)
                    job.AddNote(problem);
            }
            catch (OperationCanceledException)
            {
                state = FileJobState.Cancelled;
                job.AddNote("되돌리기를 멈췄습니다. 다시 실행 취소하면 남은 항목을 이어서 되돌립니다.");
            }
            catch (Exception ex) when (IsItemError(ex))
            {
                job.ItemFailed(ex.Message);
            }
            finally
            {
                job.Finish(state);
            }
        }

        // ── 이력 ───────────────────────────────────────────────────

        private static JournalOperationKind ToJournalKind(FileJobKind kind)
        {
            switch (kind)
            {
                case FileJobKind.Copy: return JournalOperationKind.Copy;
                case FileJobKind.Move: return JournalOperationKind.Move;
                case FileJobKind.Delete: return JournalOperationKind.Delete;
                default: return JournalOperationKind.DeletePermanently;
            }
        }

        /// <summary>
        /// "시작" 기록. 쓰지 못하면 작업은 그대로 하되(오늘까지의 동작과 같음) 실행 취소할 수 없다고 알린다.
        /// 폴더 비교 창은 기록을 못 쓰면 실행하지 않지만, 메인 패널의 일반 복사·삭제를 이력 때문에 막지는 않는다.
        /// </summary>
        private JournalOperation BeginHistory(FileJob job)
        {
            try
            {
                lock (_journalLock)
                    return _journal.Begin(ToJournalKind(job.Kind), job.Sources);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                job.AddNote("작업 이력을 쓰지 못해 이 작업은 실행 취소할 수 없습니다: " + ex.Message);
                return null;
            }
        }

        private JournalOperation Record(FileJob job, JournalOperation op, JournalItem item)
        {
            if (op == null)
                return null;
            try
            {
                lock (_journalLock)
                    _journal.AddItem(op, item);
                return op;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                job.AddNote("작업 이력을 이어 쓰지 못했습니다(이번 실행 중에는 기록된 항목까지 실행 취소 가능): " + ex.Message);
                return null;
            }
        }

        private void EndHistory(FileJob job, JournalOperation op)
        {
            if (op == null)
                return;
            try
            {
                lock (_journalLock)
                    _journal.End(op);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                job.AddNote("작업 이력의 끝을 쓰지 못했습니다: " + ex.Message);
            }
        }

        // ── 바로 끝나는 작업(부르는 스레드에서 실행 + 기록) ─────────────────

        /// <summary>이름을 바꾸고 기록한다. 바꾸기 실패는 예외, 기록 실패는 경고 문구를 돌려준다(이름은 바뀜).</summary>
        public string Rename(string path, string newName, out string historyWarning)
        {
            var isDirectory = Directory.Exists(path);
            var destination = FileOperationService.Rename(path, newName);
            var item = new JournalItem { Source = Path.TrimEndingDirectorySeparator(path), Destination = destination, IsDirectory = isDirectory, Succeeded = true };
            FileOperationJournal.CaptureFacts(item, destination);
            historyWarning = RecordSingle(JournalOperationKind.Rename, item);
            return destination;
        }

        public string CreateFolder(string parent, string name, out string historyWarning)
        {
            var path = FileOperationService.CreateDirectory(parent, name);
            historyWarning = RecordSingle(JournalOperationKind.CreateFolder,
                new JournalItem { Source = path, Destination = path, IsDirectory = true, Succeeded = true });
            return path;
        }

        public string CreateFile(string parent, string name, out string historyWarning)
        {
            var path = FileOperationService.CreateFile(parent, name);
            var item = new JournalItem { Source = path, Destination = path, Succeeded = true };
            FileOperationJournal.CaptureFacts(item, path);
            historyWarning = RecordSingle(JournalOperationKind.CreateFile, item);
            return path;
        }

        private string RecordSingle(JournalOperationKind kind, JournalItem item)
        {
            try
            {
                lock (_journalLock)
                {
                    var op = _journal.Begin(kind, new[] { item.Source });
                    _journal.AddItem(op, item);
                    _journal.End(op);
                }
                return null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return "작업 이력을 쓰지 못해 실행 취소할 수 없습니다: " + ex.Message;
            }
        }

        // ── 실행 취소 ───────────────────────────────────────────────

        /// <summary>지금 실행 취소할 작업의 한 줄 설명과 시각(파일을 확인하지 않아 빠름 — 메뉴 표시용). 없으면 null.</summary>
        public Tuple<string, DateTime> PeekUndo()
        {
            lock (_journalLock)
            {
                var target = _journal.UndoTarget;
                return target == null ? null : Tuple.Create(DescribeOperation(target), target.StartedUtc);
            }
        }

        /// <summary>
        /// 지금 실행 취소할 작업과 항목별로 되돌릴 수 있는지(파일·휴지통 확인). 없으면 null.
        /// 항목이 많으면 시간이 걸릴 수 있어 UI는 백그라운드에서 부른다.
        /// </summary>
        public UndoPlan PlanUndo()
        {
            lock (_journalLock)
            {
                var target = _journal.UndoTarget;
                if (target == null)
                    return null;
                var plan = new UndoPlan
                {
                    Operation = target,
                    Kind = target.Kind,
                    StartedUtc = target.StartedUtc,
                    Interrupted = target.State == JournalOperationState.Interrupted,
                    Summary = DescribeOperation(target)
                };
                foreach (var item in target.Items.Where(item => item.Succeeded && !item.Reverted))
                {
                    var problems = _journal.ItemProblems(target.Kind, item);
                    if (problems.Count > 0)
                        plan.Blocked.AddRange(problems);
                    else
                        plan.Steps.Add(DescribeUndoStep(target.Kind, item));
                }
                var index = _journal.Operations.ToList().IndexOf(target);
                plan.NewerPermanentDeletes = _journal.Operations.Skip(index + 1)
                    .Count(op => op.Kind == JournalOperationKind.DeletePermanently && op.Items.Any(item => item.Succeeded));
                return plan;
            }
        }

        /// <summary>계획한 작업을 되돌리는 작업을 대기열에 넣는다. 그 사이 대상이 바뀌었으면 null.</summary>
        public FileJob EnqueueUndo(UndoPlan plan)
        {
            lock (_journalLock)
            {
                if (plan == null || !ReferenceEquals(plan.Operation, _journal.UndoTarget))
                    return null;
            }
            var job = new FileJob(FileJobKind.Undo, Array.Empty<string>(), null, "실행 취소: " + plan.Summary) { UndoOperation = plan.Operation };
            return Enqueue(job);
        }

        /// <summary>되돌릴 수 없는 최근 작업을 넘겨 그 앞 작업을 대상으로 만든다.</summary>
        public void SkipUndo(UndoPlan plan)
        {
            lock (_journalLock)
            {
                if (plan != null && ReferenceEquals(plan.Operation, _journal.UndoTarget))
                    _journal.Skip(plan.Operation);
            }
        }

        public static string DescribeOperation(JournalOperation op)
        {
            var ok = op.Items.Where(item => item.Succeeded).ToList();
            var first = ok.FirstOrDefault();
            var what = ok.Count == 1 && first != null
                ? "'" + Path.GetFileName(first.Source) + "'"
                : ok.Count + "개";
            switch (op.Kind)
            {
                case JournalOperationKind.Copy: return string.Format("복사 {0} → {1}", what, Path.GetDirectoryName(first?.Destination ?? string.Empty));
                case JournalOperationKind.Move: return string.Format("이동 {0} → {1}", what, Path.GetDirectoryName(first?.Destination ?? string.Empty));
                case JournalOperationKind.Delete: return string.Format("휴지통으로 이동 {0}", what);
                case JournalOperationKind.DeletePermanently: return string.Format("영구 삭제 {0}", what);
                case JournalOperationKind.Rename: return string.Format("이름 변경 '{0}' → '{1}'", Path.GetFileName(first?.Source), Path.GetFileName(first?.Destination));
                case JournalOperationKind.CreateFolder: return string.Format("새 폴더 '{0}'", Path.GetFileName(first?.Destination));
                default: return string.Format("새 파일 '{0}'", Path.GetFileName(first?.Destination));
            }
        }

        private static string DescribeUndoStep(JournalOperationKind kind, JournalItem item)
        {
            switch (kind)
            {
                case JournalOperationKind.Move:
                    return item.Destination + " → " + item.Source + (item.ReplacedPath != null ? " (바꾼 옛 항목은 휴지통에서 되살림)" : string.Empty);
                case JournalOperationKind.Copy:
                    return "복사본을 휴지통으로: " + item.Destination + (item.ReplacedPath != null ? " (바꾼 옛 항목은 휴지통에서 되살림)" : string.Empty);
                case JournalOperationKind.Delete:
                    return "휴지통에서 되살림: " + item.Source;
                case JournalOperationKind.Rename:
                    return "이름 되돌림: " + Path.GetFileName(item.Destination) + " → " + Path.GetFileName(item.Source);
                case JournalOperationKind.CreateFolder:
                    return "빈 폴더 지움: " + item.Destination;
                default:
                    return "빈 파일 지움: " + item.Destination;
            }
        }

        // ── 도우미 ─────────────────────────────────────────────────

        private void RestoreReplaced(JournalItem item, Exception cause)
        {
            if (item.ReplacedPath == null)
                return;
            try
            {
                _recycleBin.Restore(item.ReplacedPath, item.RecycledUtc.Value);
                item.ReplacedPath = null;
                item.RecycledUtc = null;
            }
            catch (Exception restoreError) when (restoreError is IOException || restoreError is UnauthorizedAccessException)
            {
                throw new IOException(cause.Message + " (바꾸려고 휴지통에 보낸 " + item.ReplacedPath + "도 되살리지 못함: " + restoreError.Message + ")", cause);
            }
        }

        private static void Cleanup(string path)
        {
            FileCopier.TryDeletePath(path, out _);
        }

        private static string TemporaryPath(string target)
        {
            var parent = Path.GetDirectoryName(target) ?? string.Empty;
            return Path.Combine(parent, "~folderss-" + Guid.NewGuid().ToString("N").Substring(0, 12) + ".tmp");
        }

        internal static bool SameVolume(string a, string b)
        {
            return string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
        }

        private static bool SamePath(string a, string b)
        {
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsItemError(Exception ex) =>
            ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException ||
            ex is InvalidOperationException || ex is System.Security.SecurityException;

        private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

        private static long SafeLength(string path)
        {
            try { return new FileInfo(path).Length; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return 0; }
        }

        private static DateTime SafeModified(string path)
        {
            try { return Directory.Exists(path) ? Directory.GetLastWriteTimeUtc(path) : File.GetLastWriteTimeUtc(path); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return DateTime.MinValue; }
        }

        private static string Relative(string path) => Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
    }

    /// <summary>실행 취소 확인 창에 보일 계획. 실제 되돌리기는 실행 시점에 다시 확인한다.</summary>
    public sealed class UndoPlan
    {
        public JournalOperation Operation { get; set; }
        public JournalOperationKind Kind { get; set; }
        public DateTime StartedUtc { get; set; }
        public bool Interrupted { get; set; }
        public string Summary { get; set; }
        /// <summary>되돌릴 항목 설명.</summary>
        public List<string> Steps { get; } = new List<string>();
        /// <summary>그 사이 바뀌어 그대로 둘 항목과 이유.</summary>
        public List<string> Blocked { get; } = new List<string>();
        /// <summary>이 작업 뒤에 한 영구 삭제 수(되돌릴 수 없어 건너뜀).</summary>
        public int NewerPermanentDeletes { get; set; }
    }
}

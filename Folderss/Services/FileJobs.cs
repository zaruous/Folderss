using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace Folderss.Services
{
    public enum FileJobKind { Copy, Move, Delete, DeletePermanently, Undo }

    public enum FileJobState
    {
        Queued,
        /// <summary>복사할 파일 수·크기를 세는 중.</summary>
        Preparing,
        Running,
        Paused,
        /// <summary>이름 충돌 대화상자의 답을 기다리는 중.</summary>
        WaitingForAnswer,
        Completed,
        Cancelled,
        /// <summary>작업 전체가 시작하지 못함(대상 폴더 없음 등). 항목별 실패는 Completed + 실패 수.</summary>
        Failed
    }

    /// <summary>이름 충돌 처리. 기본(가장 안전)은 대상을 건드리지 않는 건너뛰기.</summary>
    public enum ConflictChoice { Skip, KeepBoth, Replace }

    /// <summary>복사·이동하려는 자리에 같은 이름이 있을 때 묻는 내용.</summary>
    public sealed class ConflictInfo
    {
        public string SourcePath { get; set; }
        public string DestinationPath { get; set; }
        public bool IsMove { get; set; }
        public bool SourceIsDirectory { get; set; }
        public bool DestinationIsDirectory { get; set; }
        public long? SourceSize { get; set; }
        public long? DestinationSize { get; set; }
        public DateTime SourceModifiedUtc { get; set; }
        public DateTime DestinationModifiedUtc { get; set; }
        /// <summary>대상 자리를 휴지통으로 보낼 수 있는가. 아니면 바꾸기를 하지 않는다.</summary>
        public bool CanReplace { get; set; }
        /// <summary>바꾸기를 고르면 그 항목이 실패로 남는 이유(휴지통 없음 등). 바꿀 수 있으면 null.</summary>
        public string ReplaceBlockedReason { get; set; }
        /// <summary>이 작업에서 아직 처리하지 않은 최상위 항목 수(이 항목 제외). "나머지에도 적용" 안내용.</summary>
        public int RemainingItems { get; set; }

        /// <summary>대상 파일이 더 최신인가. FAT·네트워크 드라이브의 2초 시각 단위와 같은 순간에 쓴 파일을 잘못 알리지 않게 2초 넘게 늦을 때만.</summary>
        public bool DestinationIsNewer => !SourceIsDirectory && !DestinationIsDirectory &&
                                          DestinationModifiedUtc - SourceModifiedUtc > TimeSpan.FromSeconds(2);
    }

    public sealed class ConflictAnswer
    {
        public ConflictChoice Choice { get; set; }
        /// <summary>이 작업의 나머지 충돌에도 같은 선택을 쓴다.</summary>
        public bool ApplyToAll { get; set; }
        /// <summary>작업을 멈춘다(남은 항목은 처리하지 않음).</summary>
        public bool CancelJob { get; set; }

        public static ConflictAnswer Cancel => new ConflictAnswer { CancelJob = true };
    }

    /// <summary>
    /// 작업 하나의 충돌 선택. "나머지에도 적용"을 고르면 그 뒤로는 묻지 않는다.
    /// 묻는 함수가 없으면(헤드리스) 건너뛰기.
    /// </summary>
    public sealed class ConflictPolicy
    {
        private ConflictChoice? _remembered;

        public ConflictChoice? Remembered => _remembered;

        /// <summary>선택을 돌려준다. null이면 작업 취소.</summary>
        public ConflictChoice? Resolve(ConflictInfo info, Func<ConflictInfo, ConflictAnswer> ask)
        {
            if (_remembered.HasValue)
                return _remembered.Value;
            if (ask == null)
                return ConflictChoice.Skip;
            var answer = ask(info);
            if (answer == null || answer.CancelJob)
                return null;
            if (answer.ApplyToAll)
                _remembered = answer.Choice;
            return answer.Choice;
        }
    }

    /// <summary>
    /// 전송 속도(최근 몇 초의 이동 평균)와 남은 시간 추정. 시간은 일시정지·질문 대기를 뺀 실행 시간으로 넣는다.
    /// 처음 1초는 표본이 적어 남은 시간을 내지 않는다(탐색기처럼 흔들리는 추정을 피함).
    /// </summary>
    public sealed class TransferRate
    {
        private readonly TimeSpan _window;
        private readonly Queue<(TimeSpan Time, long Bytes)> _samples = new Queue<(TimeSpan, long)>();

        public TransferRate(TimeSpan? window = null)
        {
            _window = window ?? TimeSpan.FromSeconds(5);
        }

        public void Add(TimeSpan elapsed, long bytesDone)
        {
            _samples.Enqueue((elapsed, bytesDone));
            while (_samples.Count > 2 && elapsed - _samples.Peek().Time > _window)
                _samples.Dequeue();
        }

        /// <summary>초당 바이트. 표본이 모자라면 0.</summary>
        public double BytesPerSecond
        {
            get
            {
                if (_samples.Count < 2)
                    return 0;
                var first = _samples.Peek();
                var last = _samples.Last();
                var seconds = (last.Time - first.Time).TotalSeconds;
                return seconds <= 0.2 ? 0 : Math.Max(0, last.Bytes - first.Bytes) / seconds;
            }
        }

        /// <summary>남은 바이트를 지금 속도로 보낼 때 걸리는 시간. 추정할 수 없으면 null.</summary>
        public TimeSpan? Remaining(long remainingBytes)
        {
            if (_samples.Count < 2 || _samples.Last().Time < TimeSpan.FromSeconds(1))
                return null;
            var speed = BytesPerSecond;
            if (speed <= 0)
                return null;
            return TimeSpan.FromSeconds(Math.Max(0, remainingBytes) / speed);
        }
    }

    /// <summary>진행률 창에 보일 한 순간의 상태(변하지 않는 값).</summary>
    public sealed class FileJobSnapshot
    {
        public int Id { get; set; }
        public FileJobKind Kind { get; set; }
        public FileJobState State { get; set; }
        public string Title { get; set; }
        public string CurrentItem { get; set; }
        public int TotalItems { get; set; }
        public int DoneItems { get; set; }
        public int SkippedItems { get; set; }
        public int FailedItems { get; set; }
        public int TotalFiles { get; set; }
        public int DoneFiles { get; set; }
        public long TotalBytes { get; set; }
        public long ProcessedBytes { get; set; }
        public double BytesPerSecond { get; set; }
        public TimeSpan? Remaining { get; set; }
        public IReadOnlyList<string> Errors { get; set; }
        public IReadOnlyList<string> Notes { get; set; }
        public bool IsFinished { get; set; }
        public bool CanPause { get; set; }

        /// <summary>0~1. 바이트를 세는 작업은 바이트, 아니면 항목 수 기준. 셀 수 없으면 null(막대가 움직이기만 함).</summary>
        public double? Fraction
        {
            get
            {
                // 끝까지 간 작업만 가득 채운다(취소·실패는 멈춘 자리를 보인다).
                if (State == FileJobState.Completed)
                    return 1;
                if (TotalBytes > 0)
                    return Math.Min(1, (double)ProcessedBytes / TotalBytes);
                if (TotalItems > 0 && Kind != FileJobKind.Undo)
                    return Math.Min(1, (double)(DoneItems + SkippedItems + FailedItems) / TotalItems);
                return null;
            }
        }
    }

    /// <summary>
    /// 대기열의 파일 작업 하나. 진행 수치는 작업 스레드가 쓰고 화면은 <see cref="Snapshot"/>으로 읽는다.
    /// 일시정지는 다음 덩어리(파일 복사 중이면 1MB 안팎) 경계에서, 취소는 진행 중이던 최상위 항목을 원래대로 돌린 뒤 멈춘다.
    /// </summary>
    public sealed class FileJob
    {
        private static int _nextId;

        private readonly object _sync = new object();
        private readonly ManualResetEventSlim _running = new ManualResetEventSlim(true);
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        private readonly Stopwatch _activeTime = new Stopwatch();
        private readonly TransferRate _rate = new TransferRate();
        private readonly List<string> _errors = new List<string>();
        private readonly List<string> _notes = new List<string>();
        private FileJobState _state = FileJobState.Queued;
        private string _currentItem;
        private int _totalItems, _doneItems, _skippedItems, _failedItems, _totalFiles, _doneFiles;
        private long _totalBytes, _processedBytes, _fileStartBytes;
        private TimeSpan? _lastSample;

        public FileJob(FileJobKind kind, IEnumerable<string> sources, string destination, string title = null)
        {
            Id = Interlocked.Increment(ref _nextId);
            Kind = kind;
            Sources = (sources ?? Enumerable.Empty<string>()).Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
            Destination = destination;
            Title = title ?? FileJobFormat.Title(kind, Sources, destination);
            _totalItems = Sources.Count;
        }

        public int Id { get; }
        public FileJobKind Kind { get; }
        public IReadOnlyList<string> Sources { get; }
        /// <summary>복사·이동의 대상 폴더. 삭제·되돌리기는 null.</summary>
        public string Destination { get; }
        public string Title { get; }

        /// <summary>되돌리기 작업의 대상 이력.</summary>
        public JournalOperation UndoOperation { get; set; }

        /// <summary>끝난 뒤 남은 항목(되돌리기에서 막혀 그대로 둔 항목 등) 설명. 끝나기 전에는 비어 있다.</summary>
        public IReadOnlyList<string> Notes { get { lock (_sync) return _notes.ToList(); } }
        public IReadOnlyList<string> Errors { get { lock (_sync) return _errors.ToList(); } }

        public FileJobState State { get { lock (_sync) return _state; } }
        public bool IsFinished { get { var state = State; return state == FileJobState.Completed || state == FileJobState.Cancelled || state == FileJobState.Failed; } }
        public bool IsCancellationRequested => _cancel.IsCancellationRequested;
        public bool IsPauseRequested => !_running.IsSet;
        public CancellationToken Token => _cancel.Token;

        /// <summary>일시정지(진행 중일 때 다음 경계에서 멈춤). 되돌리기 작업은 일시정지하지 않는다.</summary>
        public void Pause()
        {
            if (Kind != FileJobKind.Undo && !IsFinished)
                _running.Reset();
        }

        public void Resume() => _running.Set();

        /// <summary>취소. 대기 중이면 시작하지 않고, 진행 중이면 지금 항목을 되돌린 뒤 멈춘다.</summary>
        public void Cancel()
        {
            _cancel.Cancel();
            _running.Set();
        }

        public FileJobSnapshot Snapshot()
        {
            lock (_sync)
            {
                var state = _state;
                if (state == FileJobState.Running && !_running.IsSet)
                    state = FileJobState.Paused;
                var finished = state == FileJobState.Completed || state == FileJobState.Cancelled || state == FileJobState.Failed;
                return new FileJobSnapshot
                {
                    Id = Id,
                    Kind = Kind,
                    State = state,
                    Title = Title,
                    CurrentItem = finished ? null : _currentItem,
                    TotalItems = _totalItems,
                    DoneItems = _doneItems,
                    SkippedItems = _skippedItems,
                    FailedItems = _failedItems,
                    TotalFiles = _totalFiles,
                    DoneFiles = _doneFiles,
                    TotalBytes = _totalBytes,
                    ProcessedBytes = _processedBytes,
                    BytesPerSecond = state == FileJobState.Running ? _rate.BytesPerSecond : 0,
                    Remaining = finished || state != FileJobState.Running ? null : _rate.Remaining(_totalBytes - _processedBytes),
                    Errors = _errors.ToList(),
                    Notes = _notes.ToList(),
                    IsFinished = finished,
                    CanPause = Kind != FileJobKind.Undo && (state == FileJobState.Running || state == FileJobState.Paused || state == FileJobState.Preparing)
                };
            }
        }

        // ── 작업 스레드 쪽 ─────────────────────────────────────────

        internal void SetState(FileJobState state)
        {
            lock (_sync)
            {
                _state = state;
                if (state == FileJobState.Running)
                    _activeTime.Start();
                else
                    _activeTime.Stop();
            }
        }

        internal void SetCurrent(string text)
        {
            lock (_sync) _currentItem = text;
        }

        internal void SetTotals(int files, long bytes)
        {
            lock (_sync)
            {
                _totalFiles = files;
                _totalBytes = bytes;
            }
        }

        /// <summary>일시정지 중이면 풀릴 때까지 기다리고, 취소됐으면 <see cref="OperationCanceledException"/>.</summary>
        internal void Checkpoint()
        {
            _cancel.Token.ThrowIfCancellationRequested();
            if (_running.IsSet)
                return;
            FileJobState previous;
            lock (_sync)
            {
                previous = _state;
                _state = FileJobState.Paused;
                _activeTime.Stop();
            }
            WaitHandle.WaitAny(new[] { _running.WaitHandle, _cancel.Token.WaitHandle });
            SetState(previous == FileJobState.Paused ? FileJobState.Running : previous);
            _cancel.Token.ThrowIfCancellationRequested();
        }

        internal void BeginFile(string path)
        {
            lock (_sync)
            {
                _currentItem = path;
                _fileStartBytes = _processedBytes;
            }
        }

        /// <summary>지금 파일에서 복사한 바이트.</summary>
        internal void ReportFileBytes(long bytesInFile)
        {
            lock (_sync)
            {
                _processedBytes = _fileStartBytes + bytesInFile;
                var now = _activeTime.Elapsed;
                if (_lastSample == null || now - _lastSample.Value >= TimeSpan.FromMilliseconds(100))
                {
                    _rate.Add(now, _processedBytes);
                    _lastSample = now;
                }
            }
            ProgressReported?.Invoke(bytesInFile);
        }

        /// <summary>파일 복사 진행을 알릴 때마다(작업 스레드). 시험에서 "복사 도중"을 정확히 잡는 데 쓴다.</summary>
        internal Action<long> ProgressReported { get; set; }

        /// <summary>파일 하나 끝(성공이든 실패든). 실패한 파일도 처리한 양으로 쳐서 막대가 끝까지 간다.</summary>
        internal void EndFile(long fileSize, bool succeeded)
        {
            lock (_sync)
            {
                _processedBytes = _fileStartBytes + fileSize;
                if (succeeded)
                    _doneFiles++;
            }
        }

        internal void ItemDone() { lock (_sync) _doneItems++; }
        internal void ItemSkipped() { lock (_sync) _skippedItems++; }

        internal void ItemFailed(string error)
        {
            lock (_sync)
            {
                _failedItems++;
                _errors.Add(error);
            }
        }

        /// <summary>항목은 끝났지만 일부 문제(폴더 안 파일 일부 실패 등)를 오류 목록에 남긴다.</summary>
        internal void AddError(string error) { lock (_sync) _errors.Add(error); }
        internal void AddNote(string note) { lock (_sync) _notes.Add(note); }

        internal void Finish(FileJobState state)
        {
            lock (_sync)
            {
                _state = state;
                _currentItem = null;
                _activeTime.Stop();
            }
            _running.Set();
        }

        internal TimeSpan ActiveTime { get { lock (_sync) return _activeTime.Elapsed; } }
    }

    /// <summary>진행률 창·요약 문구.</summary>
    public static class FileJobFormat
    {
        public static string Title(FileJobKind kind, IReadOnlyList<string> sources, string destination)
        {
            var count = sources?.Count ?? 0;
            var what = count == 1 ? "'" + Path.GetFileName(Path.TrimEndingDirectorySeparator(sources[0])) + "'" : count + "개";
            switch (kind)
            {
                case FileJobKind.Copy: return string.Format("복사 {0} → {1}", what, destination);
                case FileJobKind.Move: return string.Format("이동 {0} → {1}", what, destination);
                case FileJobKind.Delete: return string.Format("휴지통으로 {0}", what);
                case FileJobKind.DeletePermanently: return string.Format("영구 삭제 {0}", what);
                default: return "실행 취소";
            }
        }

        public static string Verb(FileJobKind kind)
        {
            switch (kind)
            {
                case FileJobKind.Copy: return "복사";
                case FileJobKind.Move: return "이동";
                case FileJobKind.Delete: return "휴지통으로 이동";
                case FileJobKind.DeletePermanently: return "영구 삭제";
                default: return "실행 취소";
            }
        }

        public static string Bytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            var unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            if (unit == 0)
                return bytes.ToString(CultureInfo.InvariantCulture) + " B";
            return value.ToString(value >= 100 ? "0" : value >= 10 ? "0.0" : "0.00", CultureInfo.InvariantCulture) + " " + units[unit];
        }

        public static string Speed(double bytesPerSecond) => bytesPerSecond <= 0 ? null : Bytes((long)bytesPerSecond) + "/s";

        /// <summary>"약 35초", "약 4분 10초", "약 1시간 5분". 추정 없음은 null.</summary>
        public static string Remaining(TimeSpan? remaining)
        {
            if (remaining == null)
                return null;
            var value = remaining.Value;
            if (value.TotalSeconds < 1)
                return "곧 끝남";
            if (value.TotalMinutes < 1)
                return string.Format("약 {0}초", (int)Math.Ceiling(value.TotalSeconds));
            if (value.TotalHours < 1)
                return string.Format("약 {0}분 {1}초", (int)value.TotalMinutes, value.Seconds);
            return string.Format("약 {0}시간 {1}분", (int)value.TotalHours, value.Minutes);
        }

        public static string State(FileJobSnapshot snapshot)
        {
            switch (snapshot.State)
            {
                case FileJobState.Queued: return "대기 중";
                case FileJobState.Preparing: return "준비 중(파일 세는 중)";
                case FileJobState.Running: return "진행 중";
                case FileJobState.Paused: return "일시정지";
                case FileJobState.WaitingForAnswer: return "이름 충돌 — 선택을 기다림";
                case FileJobState.Cancelled: return "취소됨";
                case FileJobState.Failed: return "실패";
                default:
                    return snapshot.FailedItems > 0 || snapshot.Errors.Count > 0 ? "완료(오류 있음)" : "완료";
            }
        }

        /// <summary>한 줄 진행 설명: "항목 2/5 · 파일 120/340 · 1.20 GB / 4.00 GB · 85.2 MB/s · 약 34초 남음".</summary>
        public static string Detail(FileJobSnapshot s)
        {
            var parts = new List<string>();
            if (s.TotalItems > 1 || s.Kind == FileJobKind.Delete || s.Kind == FileJobKind.DeletePermanently)
                parts.Add(string.Format("항목 {0}/{1}", s.DoneItems + s.SkippedItems + s.FailedItems, s.TotalItems));
            if (s.TotalFiles > 0)
                parts.Add(string.Format("파일 {0}/{1}", s.DoneFiles, s.TotalFiles));
            if (s.TotalBytes > 0)
                parts.Add(Bytes(s.ProcessedBytes) + " / " + Bytes(s.TotalBytes));
            if (!s.IsFinished)
            {
                var speed = Speed(s.BytesPerSecond);
                if (speed != null)
                    parts.Add(speed);
                var remaining = Remaining(s.Remaining);
                if (remaining != null)
                    parts.Add(remaining + " 남음");
            }
            if (s.SkippedItems > 0)
                parts.Add("건너뜀 " + s.SkippedItems);
            if (s.FailedItems > 0)
                parts.Add("실패 " + s.FailedItems);
            return string.Join(" · ", parts);
        }
    }
}

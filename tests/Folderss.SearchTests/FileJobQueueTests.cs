using Folderss.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>
    /// 메인 패널 파일 작업 대기열: 복사·이동·삭제 실행(진행 수치·충돌 선택·일시정지·취소), 이력 기록과 실행 취소,
    /// 이름 변경·새 폴더·새 파일 기록. 임시 폴더의 실제 파일과 가짜 휴지통으로 확인한다.
    /// </summary>
    public sealed class FileJobQueueTests : IDisposable
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        private readonly string _root;
        private readonly string _left;
        private readonly string _right;
        private readonly string _history;
        private readonly FakeRecycleBin _bin;

        public FileJobQueueTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "folderss-jobs-" + Guid.NewGuid().ToString("N"));
            _left = Path.Combine(_root, "left");
            _right = Path.Combine(_root, "right");
            _history = Path.Combine(_root, "history");
            Directory.CreateDirectory(_left);
            Directory.CreateDirectory(_right);
            _bin = new FakeRecycleBin(Path.Combine(_root, "bin"));
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        private FileJobQueue NewQueue(out FileOperationJournal journal)
        {
            journal = new FileOperationJournal(_history, _bin);
            journal.Load();
            return new FileJobQueue(journal, _bin);
        }

        private FileJobQueue NewQueue() => NewQueue(out _);

        private static string Write(string folder, string relative, string text)
        {
            var path = Path.Combine(folder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
            return path;
        }

        private static FileJob RunToEnd(FileJobQueue queue, FileJob job)
        {
            queue.Enqueue(job);
            Assert.True(queue.WhenIdleAsync().Wait(Timeout), "작업이 제시간에 끝나지 않음");
            return job;
        }

        private static void Undo(FileJobQueue queue)
        {
            var plan = queue.PlanUndo();
            Assert.NotNull(plan);
            var job = queue.EnqueueUndo(plan);
            Assert.NotNull(job);
            Assert.True(queue.WhenIdleAsync().Wait(Timeout));
            Assert.Equal(FileJobState.Completed, job.State);
        }

        [Fact]
        public void Copy_FilesAndFolder_CountsProgress_AndUndoSendsCopiesToBin()
        {
            var a = Write(_left, "a.txt", "AAAA");
            Write(_left, Path.Combine("src", "deep", "x.txt"), "XX");
            Write(_left, Path.Combine("src", "y.txt"), "Y");
            var queue = NewQueue();

            var job = RunToEnd(queue, new FileJob(FileJobKind.Copy, new[] { a, Path.Combine(_left, "src") }, _right));

            var s = job.Snapshot();
            Assert.Equal(FileJobState.Completed, s.State);
            Assert.Equal(3, s.TotalFiles);
            Assert.Equal(3, s.DoneFiles);
            Assert.Equal(7, s.TotalBytes);
            Assert.Equal(7, s.ProcessedBytes);
            Assert.Equal(2, s.DoneItems);
            Assert.Equal(1.0, s.Fraction);
            Assert.Equal("XX", File.ReadAllText(Path.Combine(_right, "src", "deep", "x.txt")));
            Assert.True(File.Exists(a));

            Undo(queue);

            Assert.False(File.Exists(Path.Combine(_right, "a.txt")));
            Assert.False(Directory.Exists(Path.Combine(_right, "src")));
            Assert.Equal(2, _bin.Items.Count);
            Assert.True(File.Exists(a));
        }

        [Fact]
        public void Move_SameVolume_ThenUndo_PutsBack()
        {
            var a = Write(_left, "a.txt", "A");
            Write(_left, Path.Combine("dir", "b.txt"), "B");
            var queue = NewQueue();

            var job = RunToEnd(queue, new FileJob(FileJobKind.Move, new[] { a, Path.Combine(_left, "dir") }, _right));

            Assert.Equal(FileJobState.Completed, job.State);
            Assert.False(File.Exists(a));
            Assert.Equal("B", File.ReadAllText(Path.Combine(_right, "dir", "b.txt")));

            Undo(queue);

            Assert.Equal("A", File.ReadAllText(a));
            Assert.Equal("B", File.ReadAllText(Path.Combine(_left, "dir", "b.txt")));
            Assert.Empty(Directory.GetFileSystemEntries(_right));
        }

        [Fact]
        public void Move_IntoSameFolder_FailsThatItemOnly()
        {
            var a = Write(_left, "a.txt", "A");
            var b = Write(_root, "b.txt", "B");
            var queue = NewQueue();

            var job = RunToEnd(queue, new FileJob(FileJobKind.Move, new[] { a, b }, _left));

            var s = job.Snapshot();
            Assert.Equal(1, s.FailedItems);
            Assert.Equal(1, s.DoneItems);
            Assert.True(File.Exists(a));
            Assert.True(File.Exists(Path.Combine(_left, "b.txt")));
        }

        [Fact]
        public void Conflict_DefaultWithoutAsker_IsSkip()
        {
            var source = Write(_left, "a.txt", "new");
            var target = Write(_right, "a.txt", "old");
            var queue = NewQueue();

            var job = RunToEnd(queue, new FileJob(FileJobKind.Copy, new[] { source }, _right));

            Assert.Equal(1, job.Snapshot().SkippedItems);
            Assert.Equal("old", File.ReadAllText(target));
            Assert.Single(Directory.GetFiles(_right));
        }

        [Fact]
        public void Conflict_ApplyToAll_AsksOnce()
        {
            var sources = Enumerable.Range(0, 3).Select(i => Write(_left, "f" + i + ".txt", "new" + i)).ToList();
            foreach (var source in sources)
                Write(_right, Path.GetFileName(source), "old");
            var asked = new List<ConflictInfo>();
            var queue = NewQueue();
            queue.AskConflict = (job, info) =>
            {
                asked.Add(info);
                return new ConflictAnswer { Choice = ConflictChoice.KeepBoth, ApplyToAll = true };
            };

            var result = RunToEnd(queue, new FileJob(FileJobKind.Copy, sources, _right));

            Assert.Single(asked);
            Assert.Equal(2, asked[0].RemainingItems);
            Assert.Equal(3, result.Snapshot().DoneItems);
            Assert.Equal("new1", File.ReadAllText(Path.Combine(_right, "f1 (2).txt")));
            Assert.Equal("old", File.ReadAllText(Path.Combine(_right, "f1.txt")));
        }

        [Fact]
        public void Conflict_Replace_SendsOldToBin_AndUndoRestoresOld()
        {
            var source = Write(_left, "App.cs", "new");
            var target = Write(_right, "App.cs", "old");
            var queue = NewQueue();
            queue.AskConflict = (job, info) =>
            {
                Assert.True(info.CanReplace);
                return new ConflictAnswer { Choice = ConflictChoice.Replace };
            };

            RunToEnd(queue, new FileJob(FileJobKind.Copy, new[] { source }, _right));

            Assert.Equal("new", File.ReadAllText(target));
            Assert.Single(_bin.Items);
            Assert.Empty(Directory.GetFiles(_right, "~folderss-*"));

            Undo(queue);

            Assert.Equal("old", File.ReadAllText(target));
            Assert.Equal("new", File.ReadAllText(source));
        }

        [Fact]
        public void Conflict_ReplaceWhereBinUnavailable_FailsItemAndKeepsTarget()
        {
            var source = Write(_left, "a.txt", "new");
            var target = Write(_right, "a.txt", "old");
            _bin.Unavailable = true;
            var queue = NewQueue();
            ConflictInfo seen = null;
            queue.AskConflict = (job, info) =>
            {
                seen = info;
                return new ConflictAnswer { Choice = ConflictChoice.Replace };
            };

            var job = RunToEnd(queue, new FileJob(FileJobKind.Copy, new[] { source }, _right));

            Assert.False(seen.CanReplace);
            Assert.NotNull(seen.ReplaceBlockedReason);
            Assert.Equal(1, job.Snapshot().FailedItems);
            Assert.Equal("old", File.ReadAllText(target));
        }

        [Fact]
        public void CopyIntoSameFolder_KeepsBothWithoutAsking()
        {
            var a = Write(_left, "a.txt", "A");
            var queue = NewQueue();
            queue.AskConflict = (job, info) => throw new InvalidOperationException("묻지 않아야 함");

            RunToEnd(queue, new FileJob(FileJobKind.Copy, new[] { a }, _left));

            Assert.Equal("A", File.ReadAllText(Path.Combine(_left, "a (2).txt")));
        }

        [Fact]
        public void CancelFromConflictDialog_StopsJob_UndoRevertsOnlyFinishedItems()
        {
            var first = Write(_left, "1.txt", "one");
            var second = Write(_left, "2.txt", "two");
            var third = Write(_left, "3.txt", "three");
            Write(_right, "2.txt", "old");
            var queue = NewQueue();
            queue.AskConflict = (job, info) => ConflictAnswer.Cancel;

            var job = RunToEnd(queue, new FileJob(FileJobKind.Move, new[] { first, second, third }, _right));

            Assert.Equal(FileJobState.Cancelled, job.State);
            Assert.False(File.Exists(first));                 // 끝난 항목은 남는다
            Assert.True(File.Exists(second));
            Assert.True(File.Exists(third));                  // 남은 항목은 처리하지 않음
            Assert.Equal("old", File.ReadAllText(Path.Combine(_right, "2.txt")));

            var plan = queue.PlanUndo();
            Assert.Single(plan.Steps);
            Undo(queue);
            Assert.Equal("one", File.ReadAllText(first));
        }

        [Fact]
        public void Pause_HoldsTheQueue_ResumeFinishes()
        {
            var a = Write(_left, "a.txt", "A");
            var b = Write(_left, "b.txt", "B");
            var queue = NewQueue();
            var firstJob = new FileJob(FileJobKind.Copy, new[] { a }, _right);
            var secondJob = new FileJob(FileJobKind.Copy, new[] { b }, _right);
            firstJob.Pause();

            queue.Enqueue(firstJob);
            queue.Enqueue(secondJob);
            Assert.True(SpinWait.SpinUntil(() => firstJob.Snapshot().State == FileJobState.Paused, Timeout));

            Thread.Sleep(200);
            Assert.False(File.Exists(Path.Combine(_right, "a.txt")));
            Assert.Equal(FileJobState.Queued, secondJob.State);   // 한 번에 하나, 넣은 순서대로
            Assert.True(queue.IsBusy);

            firstJob.Resume();
            Assert.True(queue.WhenIdleAsync().Wait(Timeout));
            Assert.True(File.Exists(Path.Combine(_right, "a.txt")));
            Assert.True(File.Exists(Path.Combine(_right, "b.txt")));
        }

        [Fact]
        public void CancelQueuedJob_NeverRuns()
        {
            var a = Write(_left, "a.txt", "A");
            var b = Write(_left, "b.txt", "B");
            var queue = NewQueue();
            var first = new FileJob(FileJobKind.Copy, new[] { a }, _right);
            var second = new FileJob(FileJobKind.Copy, new[] { b }, _right);
            first.Pause();
            queue.Enqueue(first);
            queue.Enqueue(second);

            second.Cancel();
            first.Resume();
            Assert.True(queue.WhenIdleAsync().Wait(Timeout));

            Assert.Equal(FileJobState.Cancelled, second.State);
            Assert.False(File.Exists(Path.Combine(_right, "b.txt")));
        }

        [Fact]
        public void CancelDuringLargeFileCopy_RemovesPartialFile_AndRecordsNothing()
        {
            var big = Path.Combine(_left, "big.bin");
            using (var stream = File.Create(big))
                stream.SetLength(64L * 1024 * 1024);
            var small = Write(_left, "small.txt", "S");
            var queue = NewQueue();
            var job = new FileJob(FileJobKind.Copy, new[] { small, big }, _right);
            long cancelledAt = 0;
            // big.bin을 복사하는 도중(처음 덩어리 뒤)에 취소한다.
            job.ProgressReported = bytes =>
            {
                if (bytes > 0 && bytes < new FileInfo(big).Length && cancelledAt == 0 && job.Snapshot().DoneFiles == 1)
                {
                    cancelledAt = bytes;
                    job.Cancel();
                }
            };

            RunToEnd(queue, job);

            Assert.True(cancelledAt > 0, "복사 도중에 진행 알림이 와야 함");
            Assert.Equal(FileJobState.Cancelled, job.State);
            Assert.True(File.Exists(Path.Combine(_right, "small.txt")));
            Assert.False(File.Exists(Path.Combine(_right, "big.bin")));
            var plan = queue.PlanUndo();
            Assert.Single(plan.Steps);   // 끝난 small.txt만
        }

        [Fact]
        public void LockedFile_FailsThatItem_OthersContinue()
        {
            if (!OperatingSystem.IsWindows())
                return;   // 다른 OS는 공유 잠금이 강제되지 않는다
            var locked = Write(_left, "locked.txt", "L");
            var free = Write(_left, "free.txt", "F");
            var queue = NewQueue();
            FileJob job;
            using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                job = RunToEnd(queue, new FileJob(FileJobKind.Copy, new[] { locked, free }, _right));

            var s = job.Snapshot();
            Assert.Equal(1, s.FailedItems);
            Assert.Equal(1, s.DoneItems);
            Assert.Contains("locked.txt", s.Errors.Single());
            Assert.False(File.Exists(Path.Combine(_right, "locked.txt")));
            Assert.True(File.Exists(Path.Combine(_right, "free.txt")));
        }

        [Fact]
        public void LockedFileInsideFolderCopy_SkipsThatFile_KeepsRest()
        {
            if (!OperatingSystem.IsWindows())
                return;
            var folder = Path.Combine(_left, "pkg");
            var locked = Write(folder, "locked.txt", "L");
            Write(folder, "ok.txt", "O");
            var queue = NewQueue();
            FileJob job;
            using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                job = RunToEnd(queue, new FileJob(FileJobKind.Copy, new[] { folder }, _right));

            var s = job.Snapshot();
            Assert.Equal(1, s.DoneItems);
            Assert.Single(s.Errors);
            Assert.True(File.Exists(Path.Combine(_right, "pkg", "ok.txt")));
            Assert.False(File.Exists(Path.Combine(_right, "pkg", "locked.txt")));
        }

        [Fact]
        public void LongPath_IsCopied()
        {
            var deep = _left;
            while (deep.Length < 300)
                deep = Path.Combine(deep, new string('d', 40));
            var file = Write(deep, "long.txt", "LONG");
            Assert.True(file.Length > 260);
            var queue = NewQueue();

            var job = RunToEnd(queue, new FileJob(FileJobKind.Copy, new[] { Path.Combine(_left, new string('d', 40)) }, _right));

            Assert.Equal(1, job.Snapshot().DoneItems);
            var copied = Directory.GetFiles(_right, "long.txt", SearchOption.AllDirectories).Single();
            Assert.Equal("LONG", File.ReadAllText(copied));
        }

        [Fact]
        public void Delete_ToBin_UndoRestores_PermanentIsNotUndoable()
        {
            var a = Write(_left, "a.txt", "A");
            var b = Write(_left, "b.txt", "B");
            var queue = NewQueue();

            RunToEnd(queue, new FileJob(FileJobKind.Delete, new[] { a }, null));
            RunToEnd(queue, new FileJob(FileJobKind.DeletePermanently, new[] { b }, null));

            Assert.False(File.Exists(a));
            Assert.False(File.Exists(b));
            var plan = queue.PlanUndo();
            Assert.Equal(JournalOperationKind.Delete, plan.Kind);   // 영구 삭제는 건너뜀
            Assert.Equal(1, plan.NewerPermanentDeletes);

            Undo(queue);

            Assert.Equal("A", File.ReadAllText(a));
            Assert.Null(queue.PlanUndo());
        }

        [Fact]
        public void Delete_WhereBinUnavailable_FailsInsteadOfDeletingPermanently()
        {
            var a = Write(_left, "a.txt", "A");
            _bin.Unavailable = true;
            var queue = NewQueue();

            var job = RunToEnd(queue, new FileJob(FileJobKind.Delete, new[] { a }, null));

            Assert.Equal(1, job.Snapshot().FailedItems);
            Assert.True(File.Exists(a));
        }

        [Fact]
        public void Undo_ChangedItemIsLeftAlone_RestRevertedAfterConfirm()
        {
            var a = Write(_left, "a.txt", "A");
            var b = Write(_left, "b.txt", "B");
            var queue = NewQueue(out var journal);
            RunToEnd(queue, new FileJob(FileJobKind.Copy, new[] { a, b }, _right));
            var older = Write(_left, "c.txt", "C");
            File.WriteAllText(Path.Combine(_right, "a.txt"), "edited after copy");

            var plan = queue.PlanUndo();

            Assert.Single(plan.Steps);
            Assert.Single(plan.Blocked);
            Undo(queue);
            Assert.Equal("edited after copy", File.ReadAllText(Path.Combine(_right, "a.txt")));   // 바뀐 것은 그대로
            Assert.False(File.Exists(Path.Combine(_right, "b.txt")));
            Assert.Equal(JournalOperationState.Skipped, journal.Operations.Last().State);
            Assert.Null(queue.PlanUndo());
            Assert.True(File.Exists(older));
        }

        [Fact]
        public void Undo_AllBlocked_CanBeSkippedToReachOlderOperation()
        {
            var a = Write(_left, "a.txt", "A");
            var queue = NewQueue();
            RunToEnd(queue, new FileJob(FileJobKind.Move, new[] { a }, _right));
            var dir = queue.CreateFolder(_left, "new", out var warning);
            Assert.Null(warning);
            Write(dir, "inside.txt", "x");

            var plan = queue.PlanUndo();
            Assert.Equal(JournalOperationKind.CreateFolder, plan.Kind);
            Assert.Empty(plan.Steps);
            queue.SkipUndo(plan);

            Assert.Equal(JournalOperationKind.Move, queue.PlanUndo().Kind);
            Undo(queue);
            Assert.True(File.Exists(a));
            Assert.True(File.Exists(Path.Combine(dir, "inside.txt")));
        }

        [Fact]
        public void Rename_CreateFolder_CreateFile_AreRecorded_AndUndoneInReverse()
        {
            var a = Write(_left, "a.txt", "A");
            var queue = NewQueue();

            var renamed = queue.Rename(a, "b.txt", out _);
            var folder = queue.CreateFolder(_left, "새 폴더", out _);
            var file = queue.CreateFile(_left, "새 파일.txt", out _);

            Assert.Equal(JournalOperationKind.CreateFile, queue.PlanUndo().Kind);
            Undo(queue);
            Assert.False(File.Exists(file));
            Undo(queue);
            Assert.False(Directory.Exists(folder));
            Undo(queue);
            Assert.True(File.Exists(a));
            Assert.False(File.Exists(renamed));
        }

        [Fact]
        public void CreatedFile_WithContentLater_IsNotDeletedByUndo()
        {
            var queue = NewQueue();
            var file = queue.CreateFile(_left, "note.txt", out _);
            File.WriteAllText(file, "typed something");

            var plan = queue.PlanUndo();

            Assert.Empty(plan.Steps);
            Assert.Single(plan.Blocked);
        }

        [Fact]
        public void History_SurvivesReload_ForUndo()
        {
            var a = Write(_left, "a.txt", "A");
            RunToEnd(NewQueue(), new FileJob(FileJobKind.Move, new[] { a }, _right));

            var reloaded = NewQueue();
            var plan = reloaded.PlanUndo();

            Assert.Equal(JournalOperationKind.Move, plan.Kind);
            Assert.False(plan.Interrupted);
            Undo(reloaded);
            Assert.True(File.Exists(a));
        }

        [Fact]
        public void OperationWithoutEnd_ReloadsAsInterrupted_AndUndoesRecordedItems()
        {
            // 앱이 작업 도중 끝난 경우: 시작·항목 기록만 있고 끝 기록이 없다.
            var a = Write(_left, "a.txt", "A");
            var journal = new FileOperationJournal(_history, _bin);
            journal.Load();
            var op = journal.Begin(JournalOperationKind.Move, new[] { a, Path.Combine(_left, "b.txt") });
            var moved = Path.Combine(_right, "a.txt");
            File.Move(a, moved);
            var item = new JournalItem { Source = a, Destination = moved, Succeeded = true };
            FileOperationJournal.CaptureFacts(item, moved);
            journal.AddItem(op, item);

            var queue = NewQueue();
            var plan = queue.PlanUndo();

            Assert.True(plan.Interrupted);
            Assert.Single(plan.Steps);
            Undo(queue);
            Assert.Equal("A", File.ReadAllText(a));
        }

        [Fact]
        public void HistoryNotWritable_JobStillRuns_ButNothingToUndo()
        {
            var a = Write(_left, "a.txt", "A");
            File.WriteAllText(Path.Combine(_root, "blocked"), "a file where the history folder should be");
            var journal = new FileOperationJournal(Path.Combine(_root, "blocked"), _bin);
            var queue = new FileJobQueue(journal, _bin);

            var job = RunToEnd(queue, new FileJob(FileJobKind.Copy, new[] { a }, _right));

            Assert.Equal(FileJobState.Completed, job.State);
            Assert.True(File.Exists(Path.Combine(_right, "a.txt")));
            Assert.Single(job.Snapshot().Notes);
            Assert.Null(queue.PlanUndo());
        }

        [Fact]
        public void MissingDestination_FailsWholeJob()
        {
            var a = Write(_left, "a.txt", "A");
            var queue = NewQueue();

            var job = RunToEnd(queue, new FileJob(FileJobKind.Copy, new[] { a }, Path.Combine(_root, "nowhere")));

            Assert.Equal(FileJobState.Failed, job.State);
            Assert.Single(job.Snapshot().Errors);
        }

        [Fact]
        public void ConflictPolicy_RemembersOnlyWhenApplyToAll()
        {
            var policy = new ConflictPolicy();
            var calls = 0;
            ConflictAnswer Ask(ConflictInfo info) { calls++; return new ConflictAnswer { Choice = ConflictChoice.Replace }; }

            Assert.Equal(ConflictChoice.Replace, policy.Resolve(new ConflictInfo(), Ask));
            Assert.Equal(ConflictChoice.Replace, policy.Resolve(new ConflictInfo(), Ask));
            Assert.Equal(2, calls);
            Assert.Null(policy.Resolve(new ConflictInfo(), _ => ConflictAnswer.Cancel));
            Assert.Equal(ConflictChoice.Skip, new ConflictPolicy().Resolve(new ConflictInfo(), null));

            var all = new ConflictPolicy();
            all.Resolve(new ConflictInfo(), _ => new ConflictAnswer { Choice = ConflictChoice.KeepBoth, ApplyToAll = true });
            Assert.Equal(ConflictChoice.KeepBoth, all.Resolve(new ConflictInfo(), _ => throw new InvalidOperationException()));
        }

        [Fact]
        public void TransferRate_SpeedAndRemaining()
        {
            var rate = new TransferRate(TimeSpan.FromSeconds(5));
            Assert.Null(rate.Remaining(100));
            rate.Add(TimeSpan.FromSeconds(0), 0);
            rate.Add(TimeSpan.FromSeconds(0.5), 50);
            Assert.Null(rate.Remaining(100));   // 처음 1초는 추정하지 않음
            rate.Add(TimeSpan.FromSeconds(2), 200);

            Assert.Equal(100, rate.BytesPerSecond, 3);
            Assert.Equal(TimeSpan.FromSeconds(3), rate.Remaining(300));

            // 창(5초) 밖 표본은 버려 최근 속도를 따른다.
            rate.Add(TimeSpan.FromSeconds(8), 200 + 6 * 1000);
            rate.Add(TimeSpan.FromSeconds(9), 200 + 7 * 1000);
            Assert.True(rate.BytesPerSecond > 900);
        }

        [Fact]
        public void Format_BytesSpeedRemaining()
        {
            Assert.Equal("512 B", FileJobFormat.Bytes(512));
            Assert.Equal("1.50 KB", FileJobFormat.Bytes(1536));
            Assert.Equal("12.3 MB", FileJobFormat.Bytes((long)(12.3 * 1024 * 1024)));
            Assert.Equal("85.0 MB/s", FileJobFormat.Speed(85 * 1024 * 1024));
            Assert.Null(FileJobFormat.Speed(0));
            Assert.Equal("약 35초", FileJobFormat.Remaining(TimeSpan.FromSeconds(34.2)));
            Assert.Equal("약 4분 10초", FileJobFormat.Remaining(TimeSpan.FromSeconds(250)));
            Assert.Equal("약 1시간 5분", FileJobFormat.Remaining(TimeSpan.FromMinutes(65)));
            Assert.Null(FileJobFormat.Remaining(null));
        }

        [Fact]
        public void ExtendedPath_ForDriveAndUnc()
        {
            if (!OperatingSystem.IsWindows())
                return;
            Assert.Equal(@"\\?\C:\a\b.txt", FileCopier.ToExtendedPath(@"C:\a\.\b.txt"));
            Assert.Equal(@"\\?\UNC\server\share\x", FileCopier.ToExtendedPath(@"\\server\share\x"));
            Assert.Equal(@"\\?\C:\x", FileCopier.ToExtendedPath(@"\\?\C:\x"));
        }

        /// <summary>원래 경로·보낸 시각으로 찾아 되살리는 휴지통 흉내. <see cref="Unavailable"/>이면 휴지통이 없는 위치처럼 군다.</summary>
        private sealed class FakeRecycleBin : IRecycleBin
        {
            private readonly string _folder;
            private readonly object _sync = new object();
            private long _ticks = DateTime.UtcNow.Ticks;
            public readonly List<(string Original, DateTime SentUtc, string Stored)> Items = new List<(string, DateTime, string)>();
            public volatile bool Unavailable;

            public FakeRecycleBin(string folder)
            {
                _folder = folder;
                Directory.CreateDirectory(folder);
            }

            public bool CanSend(string path) => !Unavailable;

            public DateTime Send(string path)
            {
                if (Unavailable)
                    throw new IOException("휴지통 없음");
                lock (_sync)
                {
                    var stored = Path.Combine(_folder, Guid.NewGuid().ToString("N"));
                    if (Directory.Exists(path))
                        Directory.Move(path, stored);
                    else
                        File.Move(path, stored);
                    var sent = new DateTime(Interlocked.Increment(ref _ticks), DateTimeKind.Utc);
                    Items.Add((path, sent, stored));
                    return sent;
                }
            }

            public bool CanRestore(string originalPath, DateTime sentUtc)
            {
                lock (_sync)
                    return Find(originalPath, sentUtc) >= 0;
            }

            public void Restore(string originalPath, DateTime sentUtc)
            {
                lock (_sync)
                {
                    var index = Find(originalPath, sentUtc);
                    if (index < 0)
                        throw new IOException("휴지통에 없음: " + originalPath);
                    var item = Items[index];
                    if (Directory.Exists(item.Stored))
                        Directory.Move(item.Stored, originalPath);
                    else
                        File.Move(item.Stored, originalPath);
                    Items.RemoveAt(index);
                }
            }

            private int Find(string originalPath, DateTime sentUtc)
                => Items.FindIndex(i => string.Equals(i.Original, originalPath, StringComparison.OrdinalIgnoreCase) && i.SentUtc == sentUtc);
        }
    }
}

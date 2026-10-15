using Folderss.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>
    /// 폴더 비교 창의 작업 이력(이동·복사·삭제 기록과 되돌리기) 검증. 임시 폴더의 실제 파일로 확인하고,
    /// Windows 휴지통 대신 임시 폴더로 옮겼다 되살리는 가짜 휴지통을 쓴다.
    /// </summary>
    public sealed class FileOperationJournalTests : IDisposable
    {
        private readonly string _root;
        private readonly string _left;
        private readonly string _right;
        private readonly string _history;
        private readonly FakeRecycleBin _bin;
        private DateTime _now = new DateTime(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);

        public FileOperationJournalTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "folderss-journal-" + Guid.NewGuid().ToString("N"));
            _left = Path.Combine(_root, "left");
            _right = Path.Combine(_root, "right");
            _history = Path.Combine(_root, "history");
            Directory.CreateDirectory(_left);
            Directory.CreateDirectory(_right);
            _bin = new FakeRecycleBin(Path.Combine(_root, "bin"));
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch (IOException) { }
        }

        private FileOperationJournal NewJournal()
        {
            var journal = new FileOperationJournal(_history, _bin, () => _now);
            journal.Load();
            return journal;
        }

        private static string Write(string folder, string relative, string text)
        {
            var path = Path.Combine(folder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
            return path;
        }

        private void Tick() => _now = _now.AddSeconds(1);

        [Fact]
        public void Move_Files_ThenUndo_PutsThemBack()
        {
            var a = Write(_left, "a.txt", "A");
            var b = Write(_left, "b.txt", "B");
            var journal = NewJournal();

            var op = journal.Move(new[] { a, b }, _right);

            Assert.Equal(JournalOperationKind.Move, op.Kind);
            Assert.All(op.Items, item => Assert.True(item.Succeeded));
            Assert.False(File.Exists(a));
            Assert.Equal("A", File.ReadAllText(Path.Combine(_right, "a.txt")));

            journal.Undo(op);

            Assert.Equal(JournalOperationState.Undone, op.State);
            Assert.Equal("A", File.ReadAllText(a));
            Assert.Equal("B", File.ReadAllText(b));
            Assert.False(File.Exists(Path.Combine(_right, "a.txt")));
        }

        [Fact]
        public void Move_SameName_ReplacesAndSendsOldToBin_UndoRestoresBoth()
        {
            var source = Write(_left, "App.cs", "new");
            var target = Write(_right, "App.cs", "old");
            var journal = NewJournal();

            Assert.Equal(new[] { target }, FileOperationJournal.FindConflicts(new[] { source }, _right));

            var op = journal.Move(new[] { source }, _right);

            Assert.Equal("new", File.ReadAllText(target));
            Assert.Equal(target, op.Items[0].ReplacedPath);
            Assert.Single(_bin.Items);

            journal.Undo(op);

            Assert.Equal("new", File.ReadAllText(source));
            Assert.Equal("old", File.ReadAllText(target));
            Assert.Empty(_bin.Items);
        }

        [Fact]
        public void Move_Directory_ThenUndo()
        {
            Write(_left, Path.Combine("src", "deep", "x.txt"), "X");
            var source = Path.Combine(_left, "src");
            var journal = NewJournal();

            var op = journal.Move(new[] { source }, _right);

            Assert.True(op.Items[0].IsDirectory);
            Assert.False(Directory.Exists(source));
            Assert.Equal("X", File.ReadAllText(Path.Combine(_right, "src", "deep", "x.txt")));

            journal.Undo(op);

            Assert.Equal("X", File.ReadAllText(Path.Combine(source, "deep", "x.txt")));
            Assert.False(Directory.Exists(Path.Combine(_right, "src")));
        }

        [Fact]
        public void Move_IntoItsOwnSubfolder_FailsThatItemOnly()
        {
            var folder = Path.Combine(_left, "src");
            Write(folder, "a.txt", "A");
            var other = Write(_left, "b.txt", "B");
            var inner = Path.Combine(folder, "inner");
            Directory.CreateDirectory(inner);
            var journal = NewJournal();

            var op = journal.Move(new[] { folder, other }, inner);

            Assert.False(op.Items[0].Succeeded);
            Assert.NotNull(op.Items[0].Error);
            Assert.True(Directory.Exists(folder));
            Assert.True(op.Items[1].Succeeded);
        }

        [Fact]
        public void Move_RefusesToReplaceAFolderThatContainsTheSource()
        {
            // 왼쪽 기준 D:\a\b, 오른쪽 기준 D:\a 처럼 겹칠 때: b\b를 a로 옮기면 교체 대상 a\b가 원본을 품고 있다.
            var outer = Path.Combine(_left, "b");
            var source = Path.Combine(outer, "b");
            Write(source, "x.txt", "X");
            var journal = NewJournal();

            var op = journal.Move(new[] { source }, _left);

            Assert.False(op.Items[0].Succeeded);
            Assert.Equal(0, _bin.SendCount);   // 원본을 품은 폴더를 휴지통에 보내 보지도 않는다
            Assert.Equal("X", File.ReadAllText(Path.Combine(source, "x.txt")));
        }

        [Fact]
        public void Move_PartialFailure_UndoRevertsOnlySucceededItems()
        {
            var a = Write(_left, "a.txt", "A");
            var missing = Path.Combine(_left, "missing.txt");
            var journal = NewJournal();

            var op = journal.Move(new[] { missing, a }, _right);

            Assert.False(op.Items[0].Succeeded);
            Assert.True(op.Items[1].Succeeded);
            Assert.Empty(journal.CheckUndo(op));

            journal.Undo(op);

            Assert.Equal("A", File.ReadAllText(a));
        }

        [Fact]
        public void Copy_UsesNumberedNameLikePaste_UndoSendsCopyToBin()
        {
            var source = Write(_left, "a.txt", "A");
            Write(_right, "a.txt", "other");
            var journal = NewJournal();

            var op = journal.Copy(new[] { source }, _right);

            var created = op.Items[0].Destination;
            Assert.Equal(Path.Combine(_right, "a (2).txt"), created);
            Assert.Equal("A", File.ReadAllText(created));
            Assert.True(File.Exists(source));

            journal.Undo(op);

            Assert.False(File.Exists(created));
            Assert.Equal("other", File.ReadAllText(Path.Combine(_right, "a.txt")));
            Assert.Single(_bin.Items);
        }

        [Fact]
        public void Delete_ToBin_UndoRestores_PermanentDeleteIsNotUndoable()
        {
            var a = Write(_left, "a.txt", "A");
            var b = Write(_left, "b.txt", "B");
            var journal = NewJournal();

            var recycled = journal.Delete(new[] { a }, permanently: false);
            Tick();
            var permanent = journal.Delete(new[] { b }, permanently: true);

            Assert.False(File.Exists(a));
            Assert.False(File.Exists(b));
            Assert.False(permanent.CanUndo);
            // 영구 삭제는 되돌릴 수 없어 건너뛰고, 그 앞 작업이 되돌리기 대상이 된다.
            Assert.Same(recycled, journal.UndoTarget);

            journal.Undo(recycled);

            Assert.Equal("A", File.ReadAllText(a));
            Assert.Null(journal.UndoTarget);
        }

        [Fact]
        public void Undo_IsLastInFirstOut()
        {
            var a = Write(_left, "a.txt", "A");
            var journal = NewJournal();

            var move = journal.Move(new[] { a }, _right);
            Tick();
            var delete = journal.Delete(new[] { Path.Combine(_right, "a.txt") }, permanently: false);

            Assert.Same(delete, journal.UndoTarget);
            Assert.Throws<InvalidOperationException>(() => journal.Undo(move));

            journal.Undo(delete);
            Assert.Same(move, journal.UndoTarget);
            journal.Undo(move);

            Assert.Equal("A", File.ReadAllText(a));
        }

        [Fact]
        public void Undo_RefusedWhenResultChangedOrOriginalPlaceTaken()
        {
            var a = Write(_left, "a.txt", "A");
            var b = Write(_left, "b.txt", "B");
            var journal = NewJournal();
            var op = journal.Move(new[] { a, b }, _right);

            File.WriteAllText(Path.Combine(_right, "a.txt"), "changed later");
            File.WriteAllText(b, "new file at original place");

            var problems = journal.CheckUndo(op);

            Assert.Equal(2, problems.Count);
            Assert.Throws<InvalidOperationException>(() => journal.Undo(op));
            // 아무것도 되돌리지 않았다.
            Assert.Equal("changed later", File.ReadAllText(Path.Combine(_right, "a.txt")));
            Assert.Equal(JournalOperationState.Completed, op.State);
        }

        [Fact]
        public void Undo_RefusedWhenReplacedItemIsGoneFromBin()
        {
            var source = Write(_left, "App.cs", "new");
            Write(_right, "App.cs", "old");
            var journal = NewJournal();
            var op = journal.Move(new[] { source }, _right);

            _bin.Empty();

            Assert.Single(journal.CheckUndo(op));
        }

        [Fact]
        public void Skip_LetsOlderOperationBeUndone()
        {
            var a = Write(_left, "a.txt", "A");
            var b = Write(_left, "b.txt", "B");
            var journal = NewJournal();
            var first = journal.Move(new[] { a }, _right);
            Tick();
            var second = journal.Move(new[] { b }, _right);
            File.WriteAllText(Path.Combine(_right, "b.txt"), "changed");

            Assert.NotEmpty(journal.CheckUndo(second));
            journal.Skip(second);

            Assert.Equal(JournalOperationState.Skipped, second.State);
            Assert.Same(first, journal.UndoTarget);
        }

        [Fact]
        public void History_SurvivesReload_AndCanStillUndo()
        {
            var a = Write(_left, "a.txt", "A");
            var journal = NewJournal();
            journal.Move(new[] { a }, _right);
            Tick();
            var b = Write(_left, "b.txt", "B");
            var second = journal.Move(new[] { b }, _right);
            journal.Undo(second);

            var reloaded = NewJournal();

            Assert.Equal(2, reloaded.Operations.Count);
            Assert.Equal(JournalOperationState.Completed, reloaded.Operations[0].State);
            Assert.Equal(JournalOperationState.Undone, reloaded.Operations[1].State);
            Assert.Same(reloaded.Operations[0], reloaded.UndoTarget);

            reloaded.Undo(reloaded.UndoTarget);

            Assert.Equal("A", File.ReadAllText(a));
            Assert.Empty(reloaded.LoadWarnings);
        }

        [Fact]
        public void OperationWithoutEnd_IsInterrupted_AndUndoesRecordedItems()
        {
            var a = Write(_left, "a.txt", "A");
            var journal = NewJournal();
            journal.Move(new[] { a }, _right);

            var file = Directory.GetFiles(_history).Single();
            var lines = File.ReadAllLines(file).Where(line => !line.Contains("\"t\":\"end\"")).ToArray();
            File.WriteAllLines(file, lines);

            var reloaded = NewJournal();
            var op = reloaded.Operations.Single();

            Assert.Equal(JournalOperationState.Interrupted, op.State);
            Assert.Same(op, reloaded.UndoTarget);
            reloaded.Undo(op);
            Assert.Equal("A", File.ReadAllText(a));
        }

        [Fact]
        public void Load_DeletesHistoryOlderThanSevenDays()
        {
            Directory.CreateDirectory(_history);
            var old = Path.Combine(_history, _now.AddDays(-8).ToString("yyyy-MM-dd") + ".jsonl");
            var recent = Path.Combine(_history, _now.AddDays(-6).ToString("yyyy-MM-dd") + ".jsonl");
            File.WriteAllText(old, string.Empty);
            File.WriteAllText(recent, string.Empty);

            NewJournal();

            Assert.False(File.Exists(old));
            Assert.True(File.Exists(recent));
        }

        [Fact]
        public void WhenHistoryCannotBeWritten_NothingIsExecuted()
        {
            var a = Write(_left, "a.txt", "A");
            File.WriteAllText(Path.Combine(_root, "blocked"), "a file where the history folder should be");
            var journal = new FileOperationJournal(Path.Combine(_root, "blocked"), _bin, () => _now);

            Assert.ThrowsAny<IOException>(() => journal.Move(new[] { a }, _right));

            Assert.Equal("A", File.ReadAllText(a));
            Assert.False(File.Exists(Path.Combine(_right, "a.txt")));
        }

        [Fact]
        public void MalformedLine_IsReportedNotFatal()
        {
            var a = Write(_left, "a.txt", "A");
            var journal = NewJournal();
            journal.Move(new[] { a }, _right);
            File.AppendAllText(Directory.GetFiles(_history).Single(), "{not json\n");

            var reloaded = NewJournal();

            Assert.Single(reloaded.Operations);
            Assert.Single(reloaded.LoadWarnings);
        }

        [Fact]
        public void CountFiles_CountsRecursively_AndStopsAtLimit()
        {
            for (var i = 0; i < 5; i++)
                Write(_left, Path.Combine("big", "sub" + i, "f.txt"), "x");
            var big = Path.Combine(_left, "big");

            Assert.Equal(5, FileOperationJournal.CountFiles(new[] { big }, 100));
            Assert.Equal(3, FileOperationJournal.CountFiles(new[] { big }, 3));
            Assert.Equal(0, FileOperationJournal.CountFiles(new[] { Path.Combine(_left, "missing") }, 100));
        }

        /// <summary>원래 경로·보낸 시각으로 찾아 되살리는 휴지통 흉내.</summary>
        private sealed class FakeRecycleBin : IRecycleBin
        {
            private readonly string _folder;
            public readonly List<(string Original, DateTime SentUtc, string Stored)> Items = new List<(string, DateTime, string)>();

            public FakeRecycleBin(string folder)
            {
                _folder = folder;
                Directory.CreateDirectory(folder);
            }

            public int SendCount;

            public DateTime Send(string path)
            {
                SendCount++;
                var stored = Path.Combine(_folder, Guid.NewGuid().ToString("N"));
                if (Directory.Exists(path))
                    Directory.Move(path, stored);
                else
                    File.Move(path, stored);
                var sent = DateTime.UtcNow;
                Items.Add((path, sent, stored));
                return sent;
            }

            public bool CanRestore(string originalPath, DateTime sentUtc) => Find(originalPath, sentUtc) >= 0;

            public void Restore(string originalPath, DateTime sentUtc)
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

            public void Empty() => Items.Clear();

            private int Find(string originalPath, DateTime sentUtc)
                => Items.FindIndex(i => string.Equals(i.Original, originalPath, StringComparison.OrdinalIgnoreCase) && i.SentUtc == sentUtc);
        }
    }
}

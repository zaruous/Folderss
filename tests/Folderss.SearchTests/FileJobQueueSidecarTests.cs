using Folderss.Services;
using System.IO;
using System.Linq;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>
    /// 블록 편집기 사이드카(문서.md.meta.json)를 문서와 함께 처리하는 메인 패널 파일 작업(#78):
    /// 동반 파일 찾기·중복 제거, 이동·복사·삭제·이름 변경에서 따라가기, 충돌 규칙, 사이드카만 실패, 한 단계 실행 취소.
    /// </summary>
    public sealed partial class FileJobQueueTests
    {
        private string WriteDocument(string folder, string name, string text = "# 문서", string sidecar = "{\"version\":1}")
        {
            var document = Write(folder, name, text);
            File.WriteAllText(BlockSidecar.PathFor(document), sidecar);
            return document;
        }

        private static string Sidecar(string document) => BlockSidecar.PathFor(document);

        [Fact]
        public void SidecarFiles_NamesAndCompanion()
        {
            var doc = WriteDocument(_left, "a.md");
            var plain = Write(_left, "b.md", "B");
            var other = Write(_left, "c.txt", "C");
            File.WriteAllText(other + BlockSidecar.Extension, "{}");
            var markdown = WriteDocument(_left, "d.markdown");
            Directory.CreateDirectory(Path.Combine(_left, "folder.md"));
            File.WriteAllText(Path.Combine(_left, "folder.md") + BlockSidecar.Extension, "{}");

            Assert.Equal(Sidecar(doc), BlockSidecarFiles.CompanionOf(doc));
            Assert.Equal(Sidecar(markdown), BlockSidecarFiles.CompanionOf(markdown));
            Assert.Null(BlockSidecarFiles.CompanionOf(plain));                       // 사이드카 없음
            Assert.Null(BlockSidecarFiles.CompanionOf(other));                       // 문서 확장자가 아님
            Assert.Null(BlockSidecarFiles.CompanionOf(Path.Combine(_left, "folder.md")));   // 폴더
            Assert.True(BlockSidecarFiles.IsSidecar(Sidecar(doc)));
            Assert.Equal(doc, BlockSidecarFiles.DocumentOf(Sidecar(doc)));
            Assert.False(BlockSidecarFiles.IsSidecar(other + BlockSidecar.Extension));
            Assert.False(BlockSidecarFiles.IsSidecar(Path.Combine(_left, ".md.meta.json")));
            Assert.Equal(Path.Combine(_right, "x (2).md.meta.json"), BlockSidecarFiles.TargetFor(Path.Combine(_right, "x (2).md")));
        }

        [Fact]
        public void SidecarFiles_Pair_DropsSidecarSelectedWithItsDocument()
        {
            var doc = WriteDocument(_left, "a.md");
            var lone = WriteDocument(_left, "b.md");

            // 문서와 사이드카를 둘 다 골랐으면 사이드카는 문서에 붙는다. 사이드카만 고른 것은 보통 파일로 남는다.
            var pairs = BlockSidecarFiles.Pair(new[] { Sidecar(doc), doc, Sidecar(lone) });

            Assert.Equal(2, pairs.Count);
            Assert.Equal(doc, pairs[0].Path);
            Assert.Equal(Sidecar(doc), pairs[0].Companion);
            Assert.Equal(Sidecar(lone), pairs[1].Path);
            Assert.Null(pairs[1].Companion);
        }

        [Fact]
        public void Move_DocumentTakesSidecar_UndoRestoresBothInOneStep()
        {
            var doc = WriteDocument(_left, "a.md", sidecar: "{\"attrs\":1}");
            var queue = NewQueue();

            var job = RunToEnd(queue, new FileJob(FileJobKind.Move, new[] { doc }, _right));

            Assert.Equal(1, job.Snapshot().DoneItems);
            Assert.Empty(job.Errors);
            Assert.False(File.Exists(doc));
            Assert.False(File.Exists(Sidecar(doc)));
            Assert.Equal("{\"attrs\":1}", File.ReadAllText(Sidecar(Path.Combine(_right, "a.md"))));
            Assert.Equal("이동 'a.md' → " + _right, queue.PeekUndo().Item1);   // 사이드카는 항목 수에 넣지 않음

            var plan = queue.PlanUndo();
            Assert.Equal(2, plan.Steps.Count);
            Undo(queue);

            Assert.True(File.Exists(doc));
            Assert.Equal("{\"attrs\":1}", File.ReadAllText(Sidecar(doc)));
            Assert.Empty(Directory.GetFileSystemEntries(_right));
            Assert.Null(queue.PlanUndo());
        }

        [Fact]
        public void Move_DocumentAndSidecarBothSelected_HandledOnce()
        {
            var doc = WriteDocument(_left, "a.md");
            var queue = NewQueue();

            var job = RunToEnd(queue, new FileJob(FileJobKind.Move, new[] { doc, Sidecar(doc) }, _right));

            var s = job.Snapshot();
            Assert.Equal(1, s.TotalItems);
            Assert.Equal(1, s.DoneItems);
            Assert.Equal(0, s.FailedItems);
            Assert.Empty(job.Errors);
            Assert.True(File.Exists(Path.Combine(_right, "a.md")));
            Assert.True(File.Exists(Sidecar(Path.Combine(_right, "a.md"))));
        }

        [Fact]
        public void SidecarSelectedAlone_IsAnOrdinaryFile()
        {
            var doc = WriteDocument(_left, "a.md");
            var queue = NewQueue();

            RunToEnd(queue, new FileJob(FileJobKind.Copy, new[] { Sidecar(doc) }, _right));

            Assert.True(File.Exists(Sidecar(Path.Combine(_right, "a.md"))));
            Assert.False(File.Exists(Path.Combine(_right, "a.md")));
            Assert.Equal("복사 'a.md.meta.json' → " + _right, queue.PeekUndo().Item1);
        }

        [Fact]
        public void CopyIntoSameFolder_SidecarGetsTheNumberedName()
        {
            var doc = WriteDocument(_left, "a.md");
            var queue = NewQueue();

            RunToEnd(queue, new FileJob(FileJobKind.Copy, new[] { doc }, _left));

            Assert.True(File.Exists(Path.Combine(_left, "a (2).md")));
            Assert.True(File.Exists(Path.Combine(_left, "a (2).md.meta.json")));
            Assert.True(File.Exists(Sidecar(doc)));

            Undo(queue);
            Assert.False(File.Exists(Path.Combine(_left, "a (2).md.meta.json")));
            Assert.True(File.Exists(Sidecar(doc)));
        }

        [Fact]
        public void Conflict_SkipDocument_LeavesBothSidecarsAlone()
        {
            var doc = WriteDocument(_left, "a.md", sidecar: "new");
            var target = WriteDocument(_right, "a.md", sidecar: "old");
            var queue = NewQueue();   // 묻는 함수 없음 → 건너뛰기

            var job = RunToEnd(queue, new FileJob(FileJobKind.Move, new[] { doc }, _right));

            Assert.Equal(1, job.Snapshot().SkippedItems);
            Assert.Equal("new", File.ReadAllText(Sidecar(doc)));
            Assert.Equal("old", File.ReadAllText(Sidecar(target)));
        }

        [Fact]
        public void Conflict_ReplaceDocument_ReplacesSidecar_UndoRestoresOldPair()
        {
            var doc = WriteDocument(_left, "a.md", "new doc", "new");
            var target = WriteDocument(_right, "a.md", "old doc", "old");
            var queue = NewQueue();
            queue.AskConflict = (job, info) => new ConflictAnswer { Choice = ConflictChoice.Replace };

            var result = RunToEnd(queue, new FileJob(FileJobKind.Copy, new[] { doc }, _right));

            Assert.Empty(result.Errors);
            Assert.Equal("new doc", File.ReadAllText(target));
            Assert.Equal("new", File.ReadAllText(Sidecar(target)));
            Assert.Equal(2, _bin.Items.Count);   // 옛 문서와 옛 사이드카

            Undo(queue);

            Assert.Equal("old doc", File.ReadAllText(target));
            Assert.Equal("old", File.ReadAllText(Sidecar(target)));
            Assert.Equal("new", File.ReadAllText(Sidecar(doc)));
        }

        [Fact]
        public void Conflict_KeepBoth_SidecarFollowsNumberedName()
        {
            var doc = WriteDocument(_left, "a.md", sidecar: "new");
            var target = WriteDocument(_right, "a.md", sidecar: "old");
            var queue = NewQueue();
            queue.AskConflict = (job, info) => new ConflictAnswer { Choice = ConflictChoice.KeepBoth };

            RunToEnd(queue, new FileJob(FileJobKind.Move, new[] { doc }, _right));

            Assert.Equal("new", File.ReadAllText(Path.Combine(_right, "a (2).md.meta.json")));
            Assert.Equal("old", File.ReadAllText(Sidecar(target)));
            Assert.False(File.Exists(Sidecar(doc)));
        }

        [Fact]
        public void OrphanSidecarAtDestination_IsSentToBin_UndoBringsItBack()
        {
            var doc = WriteDocument(_left, "a.md", sidecar: "mine");
            var orphan = Write(_right, "a.md.meta.json", "orphan");
            var queue = NewQueue();

            var job = RunToEnd(queue, new FileJob(FileJobKind.Move, new[] { doc }, _right));

            Assert.Empty(job.Errors);
            Assert.Equal("mine", File.ReadAllText(orphan));
            Assert.Single(_bin.Items);

            Undo(queue);
            Assert.Equal("orphan", File.ReadAllText(orphan));
            Assert.Equal("mine", File.ReadAllText(Sidecar(doc)));
        }

        [Fact]
        public void SidecarFailsOnly_DocumentSucceeds_WithWarning()
        {
            var doc = WriteDocument(_left, "a.md", sidecar: "mine");
            var orphan = Write(_right, "a.md.meta.json", "orphan");
            _bin.Unavailable = true;   // 대상 자리의 사이드카를 휴지통으로 보낼 수 없음 → 사이드카만 실패
            var queue = NewQueue();

            var job = RunToEnd(queue, new FileJob(FileJobKind.Move, new[] { doc }, _right));

            var s = job.Snapshot();
            Assert.Equal(1, s.DoneItems);
            Assert.Equal(0, s.FailedItems);
            Assert.Single(job.Errors);
            Assert.Contains("블록 속성 파일", job.Errors[0]);
            Assert.True(File.Exists(Path.Combine(_right, "a.md")));
            Assert.Equal("mine", File.ReadAllText(Sidecar(doc)));   // 원래 자리에 남음
            Assert.Equal("orphan", File.ReadAllText(orphan));

            _bin.Unavailable = false;
            Assert.Single(queue.PlanUndo().Steps);   // 문서만 기록됨
        }

        [Fact]
        public void Delete_ToBin_TakesSidecar_UndoRestoresBoth_PermanentRemovesBoth()
        {
            var doc = WriteDocument(_left, "a.md");
            var other = WriteDocument(_left, "b.md");
            var queue = NewQueue();

            RunToEnd(queue, new FileJob(FileJobKind.Delete, new[] { doc, Sidecar(doc) }, null));

            Assert.False(File.Exists(doc));
            Assert.False(File.Exists(Sidecar(doc)));
            Assert.Equal(2, _bin.Items.Count);
            Assert.Equal("휴지통으로 이동 'a.md'", queue.PeekUndo().Item1);
            Undo(queue);
            Assert.True(File.Exists(doc));
            Assert.True(File.Exists(Sidecar(doc)));

            RunToEnd(queue, new FileJob(FileJobKind.DeletePermanently, new[] { other }, null));
            Assert.False(File.Exists(other));
            Assert.False(File.Exists(Sidecar(other)));
        }

        [Fact]
        public void Rename_TakesSidecar_UndoRestoresBothInOneStep()
        {
            var doc = WriteDocument(_left, "a.md", sidecar: "attrs");
            var queue = NewQueue();

            var renamed = queue.Rename(doc, "b.md", out var warning);

            Assert.Null(warning);
            Assert.Equal("attrs", File.ReadAllText(Sidecar(renamed)));
            Assert.False(File.Exists(Sidecar(doc)));
            Assert.Equal("이름 변경 'a.md' → 'b.md'", queue.PeekUndo().Item1);

            Undo(queue);
            Assert.True(File.Exists(doc));
            Assert.Equal("attrs", File.ReadAllText(Sidecar(doc)));
            Assert.False(File.Exists(renamed));
            Assert.False(File.Exists(Sidecar(renamed)));
            Assert.Null(queue.PlanUndo());
        }

        [Fact]
        public void Rename_SidecarNameTaken_DocumentRenamed_SidecarKept_Warns()
        {
            var doc = WriteDocument(_left, "a.md", sidecar: "mine");
            var taken = Write(_left, "b.md.meta.json", "someone else");
            var queue = NewQueue();

            var renamed = queue.Rename(doc, "b.md", out var warning);

            Assert.NotNull(warning);
            Assert.Contains("블록 속성 파일", warning);
            Assert.True(File.Exists(renamed));
            Assert.Equal("mine", File.ReadAllText(Sidecar(doc)));
            Assert.Equal("someone else", File.ReadAllText(taken));

            Undo(queue);
            Assert.True(File.Exists(doc));
        }

        [Fact]
        public void CompanionMark_SurvivesHistoryReload()
        {
            var doc = WriteDocument(_left, "a.md");
            RunToEnd(NewQueue(), new FileJob(FileJobKind.Copy, new[] { doc }, _right));

            var reloaded = NewQueue(out var journal);

            var items = journal.Operations.Last().Items;
            Assert.Null(items[0].CompanionOf);
            Assert.Equal(doc, items[1].CompanionOf);
            Assert.Equal("복사 'a.md' → " + _right, reloaded.PeekUndo().Item1);
            Undo(reloaded);
            Assert.Empty(Directory.GetFileSystemEntries(_right));
        }

        [Fact]
        public void FolderMove_CarriesSidecarsInside_WithoutExtraItems()
        {
            var folder = Path.Combine(_left, "notes");
            WriteDocument(folder, "a.md");
            var queue = NewQueue();

            var job = RunToEnd(queue, new FileJob(FileJobKind.Move, new[] { folder }, _right));

            Assert.Equal(1, job.Snapshot().DoneItems);
            Assert.True(File.Exists(Path.Combine(_right, "notes", "a.md.meta.json")));
            Assert.Single(queue.PlanUndo().Steps);
        }
    }
}

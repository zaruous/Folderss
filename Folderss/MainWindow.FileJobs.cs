using Folderss.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Folderss
{
    /// <summary>
    /// 메인 창의 파일 작업 대기열·진행률 창·실행 취소(Ctrl+Z) 연결.
    /// 복사·이동·삭제는 <see cref="FileJobQueue"/>에 넣어 백그라운드에서 하나씩 실행하고, 끝나면 패널을 새로 고치고 실패를 모아 알린다.
    /// 메인 패널 작업 이력은 폴더 비교 창과 다른 폴더(<see cref="FileHistoryDirectory"/>)에 둔다.
    /// </summary>
    public partial class MainWindow
    {
        public static readonly string FileHistoryDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Folderss", "file-history");

        /// <summary>이만큼 넘게 걸리는 작업이면 진행률 창을 띄운다(짧은 작업은 창이 깜박이지 않게).</summary>
        private static readonly TimeSpan FileJobsWindowDelay = TimeSpan.FromMilliseconds(800);

        private FileJobQueue _fileJobs;
        private FileJobsWindow _fileJobsWindow;
        private List<string> _fileHistoryWarnings = new List<string>();

        private FileJobQueue FileJobs
        {
            get
            {
                if (_fileJobs != null)
                    return _fileJobs;
                var journal = new FileOperationJournal(FileHistoryDirectory, new WindowsRecycleBin());
                try
                {
                    journal.Load();
                    _fileHistoryWarnings = journal.LoadWarnings.ToList();
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    _fileHistoryWarnings.Add("파일 작업 이력을 읽지 못했습니다: " + ex.Message);
                }
                _fileJobs = new FileJobQueue(journal, new WindowsRecycleBin()) { AskConflict = AskFileConflict };
                _fileJobs.JobFinished += job => Dispatcher.BeginInvoke(new Action(() => OnFileJobFinished(job)));
                return _fileJobs;
            }
        }

        // ── 작업 넣기 ───────────────────────────────────────────────

        /// <summary>복사·이동을 대기열에 넣는다(확인·고정 잠금 검사는 부르는 쪽이 끝낸 뒤). 끌어다 놓기도 쓴다.</summary>
        internal FileJob QueueFileTransfer(IEnumerable<string> sources, string destination, bool move)
        {
            var job = FileJobs.Enqueue(new FileJob(move ? FileJobKind.Move : FileJobKind.Copy, sources, destination));
            ScheduleFileJobsWindow(job);
            return job;
        }

        /// <summary>
        /// 삭제를 확인받고 대기열에 넣는다. 휴지통으로 보낼 수 없는 항목(네트워크·이동식 드라이브, 260자 이상 경로)이 섞여 있으면
        /// 그 항목은 영구 삭제된다는 경고와 확인 체크를 받는다(예전에는 확인 없이 영구 삭제됐다).
        /// </summary>
        private void QueueFileDelete(IList<string> paths, bool permanently)
        {
            var names = paths.Select(path => Path.GetFileName(Path.TrimEndingDirectorySeparator(path))).ToList();
            if (permanently)
            {
                var confirm = new CompareConfirmDialog("영구 삭제",
                    string.Format("{0}개 항목을 휴지통을 거치지 않고 영구 삭제합니다.", paths.Count), names,
                    "되돌릴 수 없습니다. 실행 취소(Ctrl+Z)로도 되살릴 수 없습니다.", "되돌릴 수 없다는 것을 이해했습니다", required: true, okText: "영구 삭제")
                { Owner = this };
                if (confirm.ShowDialog() != true)
                    return;
                ScheduleFileJobsWindow(FileJobs.Enqueue(new FileJob(FileJobKind.DeletePermanently, paths, null)));
                return;
            }

            var recyclable = paths.Where(WindowsRecycleBin.IsRecyclable).ToList();
            var permanent = paths.Where(path => !WindowsRecycleBin.IsRecyclable(path)).ToList();
            if (permanent.Count == 0)
            {
                var answer = MessageBox.Show(this,
                    string.Format("{0}개 항목을 휴지통으로 보내시겠습니까?\n실행 취소(Ctrl+Z)로 되돌릴 수 있습니다.", paths.Count),
                    "휴지통으로 이동", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes)
                    return;
            }
            else
            {
                var dialog = new CompareConfirmDialog("삭제 확인",
                    string.Format("{0}개 항목을 삭제합니다. 그중 {1}개는 휴지통이 없는 위치(네트워크·이동식 드라이브) 또는 260자 이상 긴 경로라 휴지통에 보낼 수 없어 영구 삭제됩니다.",
                        paths.Count, permanent.Count),
                    permanent.Select(path => Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) + "  — " + path).ToList(),
                    "영구 삭제되는 항목은 되돌릴 수 없습니다(실행 취소 불가). 나머지 " + recyclable.Count + "개는 휴지통으로 갑니다.",
                    "되돌릴 수 없다는 것을 이해했습니다", required: true, okText: "삭제")
                { Owner = this };
                if (dialog.ShowDialog() != true)
                    return;
            }

            if (recyclable.Count > 0)
                ScheduleFileJobsWindow(FileJobs.Enqueue(new FileJob(FileJobKind.Delete, recyclable, null)));
            if (permanent.Count > 0)
                ScheduleFileJobsWindow(FileJobs.Enqueue(new FileJob(FileJobKind.DeletePermanently, permanent, null)));
        }

        // ── 이름 변경·새 폴더·새 파일 (바로 실행 + 기록) ────────────────────

        /// <summary>실행 취소가 진행 중이면 이력 기록을 기다리느라 화면이 멈추지 않게 막는다.</summary>
        private bool EnsureNoUndoRunning(string title)
        {
            if (_fileJobs == null || !_fileJobs.IsUndoRunning)
                return true;
            MessageBox.Show(this, "실행 취소가 진행 중입니다. 끝난 뒤 다시 시도하세요.", title, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        private void RenameRecorded(string path, string newName)
        {
            FileJobs.Rename(path, newName, out var warning);
            ShowHistoryWarning(warning);
        }

        private void CreateFolderRecorded(string parent, string name)
        {
            FileJobs.CreateFolder(parent, name, out var warning);
            ShowHistoryWarning(warning);
        }

        private void CreateFileRecorded(string parent, string name)
        {
            FileJobs.CreateFile(parent, name, out var warning);
            ShowHistoryWarning(warning);
        }

        private void ShowHistoryWarning(string warning)
        {
            if (warning != null)
                MessageBox.Show(this, warning, "파일 작업 이력", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // ── 진행률 창 ───────────────────────────────────────────────

        private void ShowFileJobs_Click(object sender, RoutedEventArgs e) => ShowFileJobsWindow(auto: false);

        private void ShowFileJobsWindow(bool auto)
        {
            if (_fileJobsWindow == null)
            {
                _fileJobsWindow = new FileJobsWindow(FileJobs) { AutoShown = auto };
                if (IsVisible)
                    _fileJobsWindow.Owner = this;
                _fileJobsWindow.Closed += (s, e) => _fileJobsWindow = null;
                // 자동으로 뜰 때는 포커스를 뺏지 않는다 — 사용자는 하던 일을 계속한다.
                _fileJobsWindow.ShowActivated = !auto;
                _fileJobsWindow.Show();
                return;
            }
            if (!auto)
            {
                _fileJobsWindow.AutoShown = false;
                _fileJobsWindow.Activate();
            }
        }

        private void ScheduleFileJobsWindow(FileJob job)
        {
            var timer = new DispatcherTimer { Interval = FileJobsWindowDelay };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                if (!job.IsFinished)
                    ShowFileJobsWindow(auto: true);
            };
            timer.Start();
        }

        private ConflictAnswer AskFileConflict(FileJob job, ConflictInfo info)
        {
            try
            {
                return Dispatcher.Invoke(() =>
                {
                    if (job.IsCancellationRequested)
                        return ConflictAnswer.Cancel;
                    var dialog = new FileConflictDialog(info, job.Title);
                    Window owner = _fileJobsWindow != null && _fileJobsWindow.IsVisible ? _fileJobsWindow : this;
                    if (owner.IsVisible)
                        dialog.Owner = owner;
                    dialog.ShowDialog();
                    return dialog.Answer;
                });
            }
            catch (Exception ex) when (ex is TaskCanceledException || ex is OperationCanceledException || ex is InvalidOperationException)
            {
                // 앱 종료 중 등 화면에 물을 수 없으면 작업을 멈춘다(대상은 건드리지 않음).
                return ConflictAnswer.Cancel;
            }
        }

        /// <summary>작업이 끝나면 패널을 새로 고치고, 실패·남긴 항목을 모아 한 번 알린다(취소한 작업은 알리지 않음).</summary>
        private void OnFileJobFinished(FileJob job)
        {
            RefreshBothPanes();
            var snapshot = job.Snapshot();
            if (snapshot.State == FileJobState.Cancelled && snapshot.Errors.Count == 0)
                return;

            var lines = new List<string>();
            if (snapshot.Errors.Count > 0)
            {
                lines.AddRange(snapshot.Errors.Take(8));
                if (snapshot.Errors.Count > 8)
                    lines.Add(string.Format("… 외 {0}개", snapshot.Errors.Count - 8));
            }
            var hasNotes = snapshot.Notes.Count > 0;
            if (hasNotes)
            {
                if (lines.Count > 0)
                    lines.Add(string.Empty);
                lines.Add(job.Kind == FileJobKind.Undo ? "그 사이 바뀌어 그대로 둔 항목:" : "참고:");
                lines.AddRange(snapshot.Notes.Take(8));
                if (snapshot.Notes.Count > 8)
                    lines.Add(string.Format("… 외 {0}개", snapshot.Notes.Count - 8));
            }
            if (lines.Count == 0)
                return;

            var counts = job.Kind == FileJobKind.Undo
                ? string.Empty
                : string.Format("완료 {0} · 건너뜀 {1} · 실패 {2}\n\n", snapshot.DoneItems, snapshot.SkippedItems, snapshot.FailedItems);
            var title = snapshot.Errors.Count > 0
                ? FileJobFormat.Verb(job.Kind) + " 중 일부 항목 실패"
                : FileJobFormat.Verb(job.Kind);
            MessageBox.Show(this, counts + string.Join(Environment.NewLine, lines), title, MessageBoxButton.OK,
                snapshot.Errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }

        // ── 실행 취소 ───────────────────────────────────────────────

        private void UndoFileOperation_Click(object sender, RoutedEventArgs e) => UndoLastFileOperation();

        /// <summary>메인 메뉴를 열 때 실행 취소 항목에 대상 작업을 보인다.</summary>
        private void UpdateUndoMenuItem()
        {
            if (UndoFileOperationMenuItem == null)
                return;
            UndoFileOperationMenuItem.ToolTip = null;
            if (_fileJobs != null && _fileJobs.IsBusy)
            {
                UndoFileOperationMenuItem.Header = "실행 취소 (파일 작업 진행 중)";
                return;
            }
            var target = FileJobs.PeekUndo();
            UndoFileOperationMenuItem.Header = target == null ? "실행 취소 (없음)" : "실행 취소: " + target.Item1;
            UndoFileOperationMenuItem.ToolTip = target == null ? null : "작업 시각 " + target.Item2.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        }

        private bool _planningUndo;

        /// <summary>
        /// 메인 패널에서 한 마지막 파일 작업을 되돌린다. 항상 확인 창을 거친다(이력은 재시작 뒤에도 7일 남으므로 무엇을 되돌리는지 보여 줌).
        /// 그 사이 바뀐 항목이 있으면 목록과 "바뀐 항목은 그대로 두고 나머지만" 확인 체크를 받는다. 진행 중인 작업이 있으면 막는다.
        /// </summary>
        private async void UndoLastFileOperation()
        {
            var queue = FileJobs;
            if (_planningUndo)
                return;
            if (queue.IsBusy)
            {
                MessageBox.Show(this, "진행 중이거나 대기 중인 파일 작업이 끝난 뒤 실행 취소할 수 있습니다.", "실행 취소",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                ShowFileJobsWindow(auto: false);
                return;
            }

            // 항목이 많으면 파일·휴지통 확인에 시간이 걸려 화면이 멈추지 않게 백그라운드에서 계획한다.
            UndoPlan plan;
            _planningUndo = true;
            var cursor = Mouse.OverrideCursor;
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                plan = await Task.Run(() => queue.PlanUndo());
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                MessageBox.Show(this, "실행 취소할 작업을 확인하지 못했습니다.\n\n" + ex.Message, "실행 취소", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            finally
            {
                Mouse.OverrideCursor = cursor;
                _planningUndo = false;
            }
            if (plan == null)
            {
                var message = "실행 취소할 파일 작업이 없습니다.\n(폴더 비교 창에서 한 작업은 그 창의 작업 이력에서 되돌립니다. 영구 삭제는 되돌릴 수 없습니다.)";
                if (_fileHistoryWarnings.Count > 0)
                    message += "\n\n" + string.Join("\n", _fileHistoryWarnings.Take(5));
                MessageBox.Show(this, message, "실행 취소", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (IsUndoBlockedByPin(plan))
            {
                ShowPinLockedMessage();
                return;
            }

            var when = plan.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            if (plan.Steps.Count == 0)
            {
                var skip = MessageBox.Show(this,
                    "다음 작업은 그 사이 바뀌어 되돌릴 항목이 없습니다(아무것도 바꾸지 않았습니다).\n\n" + plan.Summary + "  (" + when + ")\n\n"
                    + string.Join("\n", plan.Blocked.Take(10))
                    + "\n\n이 작업을 넘기고 그 앞 작업을 실행 취소 대상으로 할까요? (파일은 그대로)",
                    "실행 취소", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (skip == MessageBoxResult.Yes)
                {
                    try
                    {
                        queue.SkipUndo(plan);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
                    {
                        MessageBox.Show(this, "작업 이력에 기록하지 못했습니다.\n\n" + ex.Message, "실행 취소", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                return;
            }

            var intro = string.Format("다음 작업을 되돌립니다.\n{0}\n작업 시각: {1}", plan.Summary, when);
            if (plan.Interrupted)
                intro += "\n⚠ 도중에 끝난(중단된) 작업입니다. 기록된 항목만 되돌립니다.";
            if (plan.NewerPermanentDeletes > 0)
                intro += string.Format("\n그 뒤에 한 영구 삭제 {0}건은 되돌릴 수 없어 건너뜁니다.", plan.NewerPermanentDeletes);

            string warning = null;
            string check = null;
            if (plan.Blocked.Count > 0)
            {
                warning = string.Format("다음 {0}개는 작업 뒤 바뀌었거나 자리가 차 있어 되돌리지 않고 그대로 둡니다:\n", plan.Blocked.Count)
                          + string.Join("\n", plan.Blocked.Take(10))
                          + (plan.Blocked.Count > 10 ? string.Format("\n… 외 {0}개", plan.Blocked.Count - 10) : string.Empty);
                check = "바뀐 항목은 그대로 두고 나머지만 되돌립니다";
            }
            var dialog = new CompareConfirmDialog("실행 취소", intro, plan.Steps, warning, check, required: check != null, okText: "되돌리기") { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            var job = queue.EnqueueUndo(plan);
            if (job == null)
            {
                MessageBox.Show(this, "그 사이 작업 이력이 바뀌었습니다. 다시 실행 취소하세요.", "실행 취소", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            ScheduleFileJobsWindow(job);
        }

        /// <summary>되돌리면 고정(📌)된 폴더의 구조가 바뀌는가. 파일 이름 되돌리기는 구조를 바꾸지 않아 허용(이름 변경과 같은 규칙).</summary>
        private bool IsUndoBlockedByPin(UndoPlan plan)
        {
            var items = plan.Operation.Items.Where(item => item.Succeeded && !item.Reverted);
            if (plan.Kind == JournalOperationKind.Rename)
                items = items.Where(item => item.IsDirectory);
            foreach (var item in items)
            {
                foreach (var path in new[] { item.Source, item.Destination, item.ReplacedPath })
                {
                    var parent = string.IsNullOrEmpty(path) ? null : Path.GetDirectoryName(path);
                    if (parent != null && IsDestinationPinLocked(parent))
                        return true;
                }
            }
            return false;
        }

        // ── 종료 ───────────────────────────────────────────────────

        /// <summary>
        /// 실제 종료 전: 진행 중·대기 중인 파일 작업이 있으면 묻고, 종료하면 모두 취소한 뒤 진행 중 항목을 원래대로 돌릴 때까지 잠시(최대 5초) 기다린다.
        /// 기다리는 동안 메시지를 돌려 작업 스레드의 화면 호출이 막히지 않게 한다. false면 종료하지 않는다.
        /// </summary>
        private bool ConfirmExitWithFileJobs()
        {
            if (_fileJobs == null || !_fileJobs.IsBusy)
                return true;
            var answer = MessageBox.Show(this,
                "진행 중이거나 대기 중인 파일 작업이 있습니다.\n종료하면 모두 취소합니다(진행 중이던 항목은 원래대로 돌리고, 끝난 항목은 남습니다). 그래도 종료할까요?",
                "파일 작업 진행 중", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
                return false;

            _fileJobs.CancelAll();
            var frame = new DispatcherFrame();
            var idle = _fileJobs.WhenIdleAsync();
            Task.WhenAny(idle, Task.Delay(TimeSpan.FromSeconds(5))).ContinueWith(
                _ => Dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
            return true;
        }
    }
}

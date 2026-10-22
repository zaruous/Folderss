using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Folderss.Services;

namespace Folderss
{
    /// <summary>
    /// 파일 작업 진행률 창(비모달, 메인 창 소유). 대기열의 모든 작업을 한 창에 보이고 작업마다 일시정지·계속·취소(대기 중이면 빼기).
    /// 작업 스레드의 진행 수치는 0.25초마다 <see cref="FileJob.Snapshot"/>으로 읽어 그린다. 코드로 구성.
    /// </summary>
    public sealed class FileJobsWindow : Window
    {
        private readonly FileJobQueue _queue;
        private readonly StackPanel _rows = new StackPanel();
        private readonly TextBlock _summary = new TextBlock { Margin = new Thickness(2, 0, 2, 8) };
        private readonly TextBlock _empty = new TextBlock { Text = "진행 중인 파일 작업이 없습니다.", Margin = new Thickness(4, 12, 4, 12) };
        private readonly Dictionary<int, JobRow> _rowsById = new Dictionary<int, JobRow>();
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        private DateTime? _idleSince;

        /// <summary>작업이 오래 걸려 자동으로 뜬 창인가. 이런 창은 대기열이 비면 스스로 닫힌다(사용자가 창을 누른 적이 없을 때).</summary>
        public bool AutoShown { get; set; }
        private bool _touched;

        public FileJobsWindow(FileJobQueue queue)
        {
            _queue = queue;
            Title = "파일 작업";
            Width = 600;
            Height = 380;
            MinWidth = 420;
            MinHeight = 200;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SetResourceReference(BackgroundProperty, "WindowBackground");
            SetResourceReference(ForegroundProperty, "PrimaryText");
            SetResourceReference(FontFamilyProperty, "AppFontFamily");
            FontSize = 13;

            var root = new DockPanel { Margin = new Thickness(10) };
            DockPanel.SetDock(_summary, Dock.Top);
            _summary.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            root.Children.Add(_summary);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var clear = new Button { Content = "끝난 작업 지우기", Margin = new Thickness(0, 0, 6, 0) };
            clear.Click += (s, e) => _queue.ClearFinished();
            var close = new Button { Content = "닫기", MinWidth = 75, IsCancel = true };
            close.Click += (s, e) => Close();
            buttons.Children.Add(clear);
            buttons.Children.Add(close);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            _empty.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            var list = new StackPanel();
            list.Children.Add(_empty);
            list.Children.Add(_rows);
            root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            Content = root;

            _queue.JobsChanged += OnJobsChanged;
            _timer.Tick += (s, e) => UpdateRows();
            // 사용자가 창을 직접 만졌을 때만 자동 닫기를 멈춘다(대화상자가 닫히며 Windows가 이 창을 활성화하는 것은 치지 않음).
            PreviewMouseDown += (s, e) => _touched = true;
            PreviewKeyDown += (s, e) => _touched = true;
            Loaded += (s, e) =>
            {
                Rebuild();
                _timer.Start();
            };
            Closed += (s, e) =>
            {
                _timer.Stop();
                _queue.JobsChanged -= OnJobsChanged;
            };
        }

        private void OnJobsChanged()
        {
            if (Dispatcher.CheckAccess())
                Rebuild();
            else
                Dispatcher.BeginInvoke(new Action(Rebuild));
        }

        private void Rebuild()
        {
            var jobs = _queue.Jobs;
            var ids = new HashSet<int>(jobs.Select(job => job.Id));
            foreach (var id in _rowsById.Keys.Where(id => !ids.Contains(id)).ToList())
            {
                _rows.Children.Remove(_rowsById[id].Root);
                _rowsById.Remove(id);
            }
            foreach (var job in jobs)
            {
                if (_rowsById.ContainsKey(job.Id))
                    continue;
                var row = new JobRow(job);
                _rowsById[job.Id] = row;
                _rows.Children.Add(row.Root);
            }
            UpdateRows();
        }

        private void UpdateRows()
        {
            var snapshots = _rowsById.Values.Select(row => row.Update()).ToList();
            _empty.Visibility = snapshots.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var running = snapshots.Count(s => !s.IsFinished && s.State != FileJobState.Queued);
            var queued = snapshots.Count(s => s.State == FileJobState.Queued);
            var finished = snapshots.Count(s => s.IsFinished);
            _summary.Text = string.Format("진행 {0} · 대기 {1} · 끝남 {2}   — 한 번에 하나씩 넣은 순서대로 실행합니다.", running, queued, finished);

            // 자동으로 뜬 창은 대기열이 비고 1.5초 지나면 닫는다(사용자가 창을 눌렀으면 그대로 둔다).
            if (running + queued > 0)
            {
                _idleSince = null;
            }
            else if (AutoShown && !_touched)
            {
                _idleSince = _idleSince ?? DateTime.UtcNow;
                if (DateTime.UtcNow - _idleSince.Value > TimeSpan.FromSeconds(1.5))
                    Close();
            }
        }

        /// <summary>작업 한 줄: 제목·상태 / 막대 / 수치 / 현재 항목 / 오류 + 일시정지·취소 버튼.</summary>
        private sealed class JobRow
        {
            private readonly FileJob _job;
            private readonly TextBlock _title = new TextBlock { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            private readonly TextBlock _state = new TextBlock { Margin = new Thickness(8, 0, 0, 0) };
            private readonly ProgressBar _bar = new ProgressBar { Height = 6, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 6, 0, 4) };
            private readonly TextBlock _detail = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
            private readonly TextBlock _current = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
            private readonly TextBlock _errors = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x75)) };
            private readonly TextBlock _notes = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
            private readonly Button _pause = new Button { MinWidth = 86, Margin = new Thickness(0, 0, 0, 4) };
            private readonly Button _cancel = new Button { MinWidth = 86 };

            public Border Root { get; }

            public JobRow(FileJob job)
            {
                _job = job;
                _title.Text = job.Title;
                _title.ToolTip = job.Title;
                _detail.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
                _current.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
                _notes.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
                _state.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");

                var header = new DockPanel();
                DockPanel.SetDock(_state, Dock.Right);
                header.Children.Add(_state);
                header.Children.Add(_title);

                var text = new StackPanel();
                text.Children.Add(header);
                text.Children.Add(_bar);
                text.Children.Add(_detail);
                text.Children.Add(_current);
                text.Children.Add(_errors);
                text.Children.Add(_notes);

                _pause.Click += (s, e) =>
                {
                    if (_job.IsPauseRequested)
                        _job.Resume();
                    else
                        _job.Pause();
                    Update();
                };
                _cancel.Click += (s, e) =>
                {
                    _job.Cancel();
                    Update();
                };
                var buttons = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
                buttons.Children.Add(_pause);
                buttons.Children.Add(_cancel);

                var grid = new DockPanel();
                DockPanel.SetDock(buttons, Dock.Right);
                grid.Children.Add(buttons);
                grid.Children.Add(text);

                Root = new Border { Child = grid, Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 6), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) };
                Root.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                Root.SetResourceReference(Border.BackgroundProperty, "PanelBackground");
            }

            public FileJobSnapshot Update()
            {
                var s = _job.Snapshot();
                _state.Text = FileJobFormat.State(s);
                var fraction = s.Fraction;
                _bar.IsIndeterminate = fraction == null && !s.IsFinished;
                _bar.Value = fraction ?? (s.IsFinished ? 1 : 0);
                _detail.Text = FileJobFormat.Detail(s);
                _detail.Visibility = string.IsNullOrEmpty(_detail.Text) ? Visibility.Collapsed : Visibility.Visible;
                _current.Text = s.CurrentItem ?? string.Empty;
                _current.ToolTip = s.CurrentItem;
                _current.Visibility = string.IsNullOrEmpty(s.CurrentItem) ? Visibility.Collapsed : Visibility.Visible;
                _errors.Text = s.Errors.Count == 0 ? string.Empty
                    : string.Join("\n", s.Errors.Take(3)) + (s.Errors.Count > 3 ? string.Format("\n… 외 {0}개", s.Errors.Count - 3) : string.Empty);
                _errors.Visibility = s.Errors.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
                _notes.Text = string.Join("\n", s.Notes.Take(3)) + (s.Notes.Count > 3 ? string.Format("\n… 외 {0}개", s.Notes.Count - 3) : string.Empty);
                _notes.Visibility = s.Notes.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

                _pause.Content = s.State == FileJobState.Paused || _job.IsPauseRequested ? "▶ 계속" : "⏸ 일시정지";
                _pause.Visibility = s.CanPause ? Visibility.Visible : Visibility.Collapsed;
                _cancel.Content = s.State == FileJobState.Queued ? "대기열에서 빼기" : "✕ 취소";
                _cancel.Visibility = s.IsFinished || _job.IsCancellationRequested ? Visibility.Collapsed : Visibility.Visible;
                return s;
            }
        }
    }

    /// <summary>
    /// 이름 충돌 대화상자. 건너뛰기(기본) / 둘 다 유지 / 바꾸기 + "나머지 충돌에도 적용". 취소는 작업을 멈춘다.
    /// 양쪽 종류·크기·수정 시각을 보이고 대상이 더 최신이면 표시한다. 휴지통이 없는 위치면 바꾸기를 끈다.
    /// </summary>
    public sealed class FileConflictDialog : GitDialogBase
    {
        private readonly RadioButton _skip;
        private readonly RadioButton _keepBoth;
        private readonly RadioButton _replace;
        private readonly CheckBox _applyToAll;

        public FileConflictDialog(ConflictInfo info, string jobTitle) : base("이름 충돌", 580)
        {
            var name = Path.GetFileName(info.DestinationPath);
            var verb = info.IsMove ? "이동" : "복사";
            AddText(string.Format("{0}에 같은 이름 '{1}'이(가) 이미 있습니다.", Path.GetDirectoryName(info.DestinationPath), name));
            AddText(jobTitle, secondary: true, top: 2);

            AddLabel(verb + "할 항목");
            AddText(Describe(info.SourcePath, info.SourceIsDirectory, info.SourceSize, info.SourceModifiedUtc), secondary: true);
            AddLabel("대상에 있는 항목");
            AddText(Describe(info.DestinationPath, info.DestinationIsDirectory, info.DestinationSize, info.DestinationModifiedUtc), secondary: true);
            if (info.DestinationIsNewer)
                AddText("⚠ 대상 파일이 더 최신입니다(수정 시각 기준).", warning: true, top: 4);
            if (info.SourceIsDirectory != info.DestinationIsDirectory)
                AddText("⚠ 한쪽은 폴더, 한쪽은 파일입니다.", warning: true, top: 4);

            AddLabel("처리");
            _skip = AddRadio("conflict", "건너뛰기 (기본)", string.Format("대상은 그대로 두고 이 항목은 {0}하지 않습니다.", verb), true);
            _keepBoth = AddRadio("conflict", "둘 다 유지",
                string.Format("{0}하는 항목 이름에 번호를 붙입니다: {1}", verb, Path.GetFileName(FileOperationService.GetUniquePath(info.DestinationPath, info.SourceIsDirectory))), false);
            _replace = AddRadio("conflict", "바꾸기",
                info.DestinationIsDirectory
                    ? "대상 폴더 전체(그 안에만 있는 항목 포함)를 휴지통으로 보내고 바꿉니다. 합치지 않습니다. 실행 취소(Ctrl+Z)로 되돌릴 수 있습니다."
                    : "대상의 기존 항목을 휴지통으로 보내고 바꿉니다. 실행 취소(Ctrl+Z)로 되돌릴 수 있습니다.", false);
            if (!info.CanReplace)
            {
                _replace.IsEnabled = false;
                AddText("바꿀 수 없음: " + info.ReplaceBlockedReason, warning: true, top: 4);
            }
            if (info.RemainingItems > 0)
                _applyToAll = AddCheck(string.Format(CultureInfo.InvariantCulture, "이 작업의 나머지 충돌에도 같은 선택 적용 (남은 항목 {0}개)", info.RemainingItems), false);

            AddText("취소를 누르면 남은 항목은 처리하지 않고 작업을 멈춥니다(이미 끝난 항목은 그대로 남고 실행 취소할 수 있습니다).", secondary: true, top: 12);
            AddButtons("확인");
        }

        public ConflictAnswer Answer => DialogResult == true
            ? new ConflictAnswer
            {
                Choice = _replace.IsChecked == true ? ConflictChoice.Replace : _keepBoth.IsChecked == true ? ConflictChoice.KeepBoth : ConflictChoice.Skip,
                ApplyToAll = _applyToAll?.IsChecked == true
            }
            : ConflictAnswer.Cancel;

        private static string Describe(string path, bool isDirectory, long? size, DateTime modifiedUtc)
        {
            var parts = new List<string> { isDirectory ? "폴더" : "파일" };
            if (size.HasValue)
                parts.Add(FileJobFormat.Bytes(size.Value));
            if (modifiedUtc > DateTime.MinValue)
                parts.Add("수정 " + modifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            return string.Join(" · ", parts) + "\n" + path;
        }
    }
}

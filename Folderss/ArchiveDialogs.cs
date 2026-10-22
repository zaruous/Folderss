using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Folderss.Services;

namespace Folderss
{
    /// <summary>
    /// 압축 풀기 옵션(우클릭 `압축 풀기…`, zip 띠의 ▾, ⋯ 메뉴). 기본 동작(`압축 풀기 → '이름\'`)은 이 대화상자 없이 바로 실행한다.
    /// 덮어쓰기는 되돌릴 수 없어 경고와 확인 체크를 거쳐야 한다(AGENTS.md "선택지가 여러 개인 기능은 대화상자로").
    /// </summary>
    public sealed class ArchiveExtractDialog : GitDialogBase
    {
        private readonly RadioButton _selectionRadio;
        private readonly RadioButton _zipFolder;
        private readonly RadioButton _opposite;
        private readonly RadioButton _custom;
        private readonly TextBox _customPath;
        private readonly CheckBox _namedFolder;
        private readonly RadioButton _rename;
        private readonly RadioButton _skip;
        private readonly RadioButton _overwrite;
        private readonly CheckBox _overwriteConfirm;
        private readonly string _zipFolderPath;
        private readonly string _oppositePath;
        private bool _namedFolderTouched;

        /// <param name="subject">무엇을 푸는지(예: "'자료.zip'").</param>
        /// <param name="selectionCount">zip 안에서 고른 항목 수. 0이면 "선택 항목만" 선택지가 없다.</param>
        /// <param name="namedFolder">"zip 이름으로 새 폴더" 표시용 이름. null이면 "zip마다 이름 폴더".</param>
        public ArchiveExtractDialog(string subject, int selectionCount, string zipFolder, string oppositeFolder, string namedFolder)
            : base("압축 풀기", 540)
        {
            _zipFolderPath = zipFolder;
            _oppositePath = oppositeFolder;
            AddText(subject + "의 압축을 풉니다. zip 파일은 바꾸지 않습니다.");

            if (selectionCount > 0)
            {
                AddLabel("풀 항목");
                _selectionRadio = AddRadio("items", string.Format("선택한 {0:N0}개 항목", selectionCount), null, true);
                var whole = AddRadio("items", "zip 전체", null, false);
                whole.Checked += (s, e) => { if (!_namedFolderTouched) _namedFolder.IsChecked = true; };
                _selectionRadio.Checked += (s, e) => { if (!_namedFolderTouched) _namedFolder.IsChecked = false; };
            }

            AddLabel("풀 위치");
            _zipFolder = AddRadio("dest", "zip이 있는 폴더", zipFolder, true);
            _opposite = AddRadio("dest", "반대편 패널", oppositeFolder ?? "반대편 폴더 패널이 없거나 zip 안을 보고 있습니다.", false);
            _opposite.IsEnabled = oppositeFolder != null;
            _custom = AddRadio("dest", "직접 지정 (없으면 만듭니다)", null, false);
            var row = new DockPanel { Margin = new Thickness(20, 4, 0, 0) };
            var browse = new Button { Content = "찾아보기…", Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(browse, Dock.Right);
            _customPath = new TextBox { Text = zipFolder, IsEnabled = false };
            AutomationProperties.SetName(_customPath, "풀 위치 직접 지정");
            row.Children.Add(browse);
            row.Children.Add(_customPath);
            Body.Children.Add(row);
            _custom.Checked += (s, e) => _customPath.IsEnabled = true;
            _custom.Unchecked += (s, e) => _customPath.IsEnabled = false;
            browse.Click += (s, e) => Browse();

            _namedFolder = AddCheck(namedFolder == null
                ? "zip마다 zip 이름으로 새 폴더를 만들어 그 안에 풀기"
                : string.Format("zip 이름으로 새 폴더('{0}')를 만들어 그 안에 풀기", namedFolder), selectionCount == 0);
            _namedFolder.Click += (s, e) => _namedFolderTouched = true;
            AddText("같은 이름의 폴더가 있으면 '이름 (2)'로 만듭니다. zip 맨 위가 zip과 같은 이름의 폴더 하나뿐이면 폴더가 겹치지 않게 그 폴더를 바로 꺼냅니다.", secondary: true, top: 2).Margin = new Thickness(20, 2, 0, 0);

            AddLabel("같은 이름의 파일이 이미 있을 때");
            _rename = AddRadio("conflict", "이름을 바꿔 둘 다 유지 (예: 보고서 (2).txt)", null, true);
            _skip = AddRadio("conflict", "건너뛰기 (있는 파일을 그대로 둠)", null, false);
            _overwrite = AddRadio("conflict", "덮어쓰기", null, false);
            var warning = AddText("⚠ 있던 파일이 zip의 내용으로 바뀌며 되돌릴 수 없습니다(휴지통에 남지 않음).", warning: true, top: 4);
            warning.Margin = new Thickness(20, 4, 0, 0);
            _overwriteConfirm = AddCheck("되돌릴 수 없다는 것을 이해했습니다", false);
            _overwriteConfirm.Margin = new Thickness(20, 6, 0, 0);

            var ok = AddButtons("압축 풀기");
            void Update()
            {
                var overwrite = _overwrite.IsChecked == true;
                warning.Visibility = _overwriteConfirm.Visibility = overwrite ? Visibility.Visible : Visibility.Collapsed;
                ok.IsEnabled = !overwrite || _overwriteConfirm.IsChecked == true;
            }
            _overwrite.Checked += (s, e) => Update();
            _overwrite.Unchecked += (s, e) => Update();
            _overwriteConfirm.Checked += (s, e) => Update();
            _overwriteConfirm.Unchecked += (s, e) => Update();
            Update();
        }

        /// <summary>zip 안에서 고른 항목만 푸는가(false면 zip 전체).</summary>
        public bool ExtractSelection => _selectionRadio != null && _selectionRadio.IsChecked == true;

        public string Destination
        {
            get
            {
                if (_opposite.IsChecked == true)
                    return _oppositePath;
                if (_custom.IsChecked == true)
                    return Path.GetFullPath(Environment.ExpandEnvironmentVariables(_customPath.Text.Trim().Trim('"')));
                return _zipFolderPath;
            }
        }

        public bool CreateNamedFolder => _namedFolder.IsChecked == true;

        public ZipConflictMode Conflict => _overwrite.IsChecked == true
            ? ZipConflictMode.Overwrite
            : _skip.IsChecked == true ? ZipConflictMode.Skip : ZipConflictMode.Rename;

        private void Browse()
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "압축을 풀 폴더", ShowNewFolderButton = true })
            {
                if (Directory.Exists(_customPath.Text.Trim()))
                    dialog.SelectedPath = _customPath.Text.Trim();
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    _customPath.Text = dialog.SelectedPath;
                    _custom.IsChecked = true;
                }
            }
        }

        protected override Task<string> ValidateAsync()
        {
            string destination;
            try
            {
                destination = Destination;
            }
            catch (Exception ex)
            {
                return Task.FromResult("풀 위치가 잘못되었습니다: " + ex.Message);
            }
            if (string.IsNullOrWhiteSpace(destination) || !Path.IsPathFullyQualified(destination))
                return Task.FromResult("풀 위치를 전체 경로로 입력하세요.");
            if (File.Exists(destination) || ZipArchivePath.Exists(destination))
                return Task.FromResult("파일이나 zip 안에는 풀 수 없습니다. 폴더를 고르세요.");
            var root = Path.GetPathRoot(destination);
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return Task.FromResult("드라이브를 찾을 수 없습니다: " + root);
            return Task.FromResult<string>(null);
        }
    }

    /// <summary>zip으로 압축 옵션(우클릭 `zip으로 압축…`, ⋯ 메뉴). 기본 동작(`zip으로 압축 → '이름.zip'`)은 이 대화상자 없이 바로 실행한다.</summary>
    public sealed class ArchiveCompressDialog : GitDialogBase
    {
        private readonly TextBox _name;
        private readonly RadioButton _opposite;
        private readonly ComboBox _level;
        private readonly string _currentFolder;
        private readonly string _oppositeFolder;

        private sealed class LevelChoice
        {
            public string Text { get; set; }
            public CompressionLevel Level { get; set; }
            public override string ToString() => Text;
        }

        public ArchiveCompressDialog(string subject, string defaultName, string currentFolder, string oppositeFolder) : base("zip으로 압축", 500)
        {
            _currentFolder = currentFolder;
            _oppositeFolder = oppositeFolder;
            AddText(subject + "을(를) 새 zip 파일로 묶습니다. 원본은 그대로 둡니다.");

            AddLabel("zip 파일 이름");
            _name = new TextBox { Text = defaultName };
            AutomationProperties.SetName(_name, "zip 파일 이름");
            Body.Children.Add(_name);

            AddLabel("만들 위치");
            AddRadio("dest", "현재 폴더", currentFolder, true);
            _opposite = AddRadio("dest", "반대편 패널", oppositeFolder ?? "반대편 폴더 패널이 없거나 zip 안을 보고 있습니다.", false);
            _opposite.IsEnabled = oppositeFolder != null;

            AddLabel("압축 수준");
            var levels = new List<LevelChoice>
            {
                new LevelChoice { Text = "보통 (권장)", Level = CompressionLevel.Optimal },
                new LevelChoice { Text = "빠르게", Level = CompressionLevel.Fastest },
                new LevelChoice { Text = "최대 (느림)", Level = CompressionLevel.SmallestSize },
                new LevelChoice { Text = "압축 안 함 (묶기만)", Level = CompressionLevel.NoCompression }
            };
            _level = new ComboBox { ItemsSource = levels, SelectedIndex = 0 };
            AutomationProperties.SetName(_level, "압축 수준");
            Body.Children.Add(_level);
            AddText("파일 이름은 UTF-8로 저장합니다(Windows 10 이상 탐색기·7-Zip·반디집에서 한글이 깨지지 않음). 빈 폴더도 넣습니다.", secondary: true, top: 8);
            AddButtons("압축");
            Loaded += (s, e) =>
            {
                _name.Focus();
                var stem = Path.GetFileNameWithoutExtension(_name.Text);
                _name.Select(0, stem.Length);
            };
        }

        public string ZipName
        {
            get
            {
                var name = _name.Text.Trim();
                return name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? name : name + ".zip";
            }
        }

        public string Destination => _opposite.IsChecked == true ? _oppositeFolder : _currentFolder;

        public CompressionLevel Level => (_level.SelectedItem as LevelChoice)?.Level ?? CompressionLevel.Optimal;

        protected override Task<string> ValidateAsync()
        {
            var name = _name.Text.Trim();
            if (name.Length == 0 || name == ".zip")
                return Task.FromResult("zip 파일 이름을 입력하세요.");
            if (ZipName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return Task.FromResult("파일 이름에 쓸 수 없는 글자가 있습니다: " + name);
            if (string.IsNullOrWhiteSpace(Destination) || !Directory.Exists(Destination))
                return Task.FromResult("만들 위치 폴더를 찾을 수 없습니다.");
            var target = Path.Combine(Destination, ZipName);
            if (File.Exists(target) || Directory.Exists(target))
                return Task.FromResult("같은 이름이 이미 있습니다. 다른 이름을 입력하세요.\n" + target);
            return Task.FromResult<string>(null);
        }
    }

    /// <summary>오래 걸리는 zip 작업의 진행 창(비모달, 취소 가능). 창을 닫아도 취소된다.</summary>
    internal sealed class ArchiveProgressWindow : Window
    {
        private readonly TextBlock _message = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _detail = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 6, 0, 0), FontSize = 12 };
        private readonly ProgressBar _bar = new ProgressBar { Height = 14, Minimum = 0, Maximum = 1000, Margin = new Thickness(0, 10, 0, 0) };
        private readonly CancellationTokenSource _cancellation;
        private bool _closingByCode;

        public ArchiveProgressWindow(string title, CancellationTokenSource cancellation)
        {
            _cancellation = cancellation;
            Title = title;
            Width = 460;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SetResourceReference(BackgroundProperty, "WindowBackground");
            SetResourceReference(ForegroundProperty, "PrimaryText");
            SetResourceReference(FontFamilyProperty, "AppFontFamily");
            FontSize = 13;
            _detail.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            _message.Text = title + "…";

            var cancel = new Button { Content = "취소", MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0), IsCancel = true };
            cancel.Click += (s, e) => RequestCancel();
            var body = new StackPanel { Margin = new Thickness(16) };
            body.Children.Add(_message);
            body.Children.Add(_bar);
            body.Children.Add(_detail);
            body.Children.Add(cancel);
            Content = body;
        }

        public void Report(ZipProgress progress)
        {
            if (progress == null)
                return;
            if (progress.BytesTotal > 0)
                _bar.Value = Math.Min(1000, progress.BytesDone * 1000.0 / progress.BytesTotal);
            else if (progress.Total > 0)
                _bar.Value = Math.Min(1000, progress.Done * 1000.0 / progress.Total);
            _message.Text = string.Format("{0} — {1:N0} / {2:N0}개 파일 ({3} / {4})", Title, progress.Done, progress.Total,
                ArchiveOperations.FormatSize(progress.BytesDone), ArchiveOperations.FormatSize(progress.BytesTotal));
            _detail.Text = progress.CurrentName ?? string.Empty;
        }

        public void CloseQuietly()
        {
            _closingByCode = true;
            Close();
        }

        private void RequestCancel()
        {
            _cancellation.Cancel();
            _message.Text = "취소하는 중…";
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_closingByCode)
            {
                // 작업이 끝나면 코드가 닫는다. 사용자가 닫으면 취소만 요청한다.
                e.Cancel = true;
                RequestCancel();
            }
            base.OnClosing(e);
        }
    }

    /// <summary>zip 작업을 백그라운드에서 돌리고(0.4초 넘으면 진행 창), 결과·오류를 알리는 공용 흐름.</summary>
    internal static class ArchiveOperations
    {
        private const string ExtractTitle = "압축 풀기";

        public static async Task<T> RunAsync<T>(Window owner, string title, Func<IProgress<ZipProgress>, CancellationToken, T> work)
        {
            using (var cancellation = new CancellationTokenSource())
            {
                ArchiveProgressWindow window = null;
                ZipProgress last = null;
                var progress = new Progress<ZipProgress>(value =>
                {
                    last = value;
                    window?.Report(value);
                });
                var task = Task.Run(() => work(progress, cancellation.Token));
                if (await Task.WhenAny(task, Task.Delay(400)) != task)
                {
                    window = new ArchiveProgressWindow(title, cancellation);
                    if (owner != null && owner.IsVisible)
                        window.Owner = owner;
                    window.Report(last);
                    window.Show();
                }
                try
                {
                    return await task;
                }
                finally
                {
                    window?.CloseQuietly();
                }
            }
        }

        /// <summary>계획 → 풀기 → 문제 알림. 실패하면 알리고 null.</summary>
        public static async Task<ZipExtractResult> ExtractAsync(Window owner, ZipListing listing, IEnumerable<ZipEntryInfo> items, string destination,
            ZipExtractOptions options, string title = null)
        {
            ZipExtractResult result;
            try
            {
                var plan = ZipArchiveService.PlanExtraction(listing, items, destination, options);
                result = await RunAsync(owner, title ?? "압축 푸는 중", (progress, token) => ZipArchiveService.Extract(listing, plan, progress, token));
            }
            catch (Exception ex)
            {
                ShowError(owner, ExtractTitle, ex);
                return null;
            }
            ReportProblems(owner, result);
            return result;
        }

        /// <summary>
        /// zip 하나를 <paramref name="parentDirectory"/>의 zip 이름 새 폴더(있으면 <c>이름 (2)</c>)에 푼다. 만든 폴더 경로, 실패하면 null.
        /// zip 맨 위가 zip과 같은 이름의 폴더 하나뿐이면 <c>이름\이름\</c>이 되지 않게 그 폴더를 바로 꺼낸다(7-Zip "루트 폴더 중복 제거"와 같은 조건).
        /// </summary>
        public static async Task<string> ExtractToNamedFolderAsync(Window owner, string archivePath, string parentDirectory, ZipConflictMode conflict)
        {
            var listing = await GetListingAsync(owner, archivePath);
            if (listing == null)
                return null;

            var baseName = Path.GetFileNameWithoutExtension(archivePath);
            var roots = listing.GetChildren(string.Empty);
            if (roots.Count == 1 && roots[0].IsDirectory && string.Equals(roots[0].Name, baseName, StringComparison.OrdinalIgnoreCase))
            {
                var single = await ExtractAsync(owner, listing, roots, parentDirectory, new ZipExtractOptions { UniqueTopLevel = true, Conflict = conflict });
                return single?.TopLevelTargets.FirstOrDefault();
            }

            string folder;
            try
            {
                folder = ZipArchiveService.GetUniquePath(Path.Combine(parentDirectory, ZipArchiveService.SanitizeSegment(baseName)), true, null);
                Directory.CreateDirectory(folder);
            }
            catch (Exception ex)
            {
                ShowError(owner, ExtractTitle, ex);
                return null;
            }

            var result = await ExtractAsync(owner, listing, new[] { listing.Root }, folder, new ZipExtractOptions { UniqueTopLevel = false, Conflict = conflict });
            if (result == null || (result.FilesWritten == 0 && result.TopLevelTargets.Count == 0))
                TryDeleteEmptyDirectory(folder);
            return Directory.Exists(folder) ? folder : null;
        }

        public static async Task<ZipListing> GetListingAsync(Window owner, string archivePath)
        {
            try
            {
                return await ZipListingCache.GetAsync(archivePath);
            }
            catch (Exception ex)
            {
                ShowError(owner, "zip을 열 수 없습니다", ex);
                return null;
            }
        }

        public static async Task<ZipCreateResult> CreateZipAsync(Window owner, IReadOnlyList<string> sources, string zipPath, CompressionLevel level)
        {
            ZipCreateResult result;
            try
            {
                result = await RunAsync(owner, "zip으로 압축하는 중", (progress, token) => ZipArchiveService.CreateZip(sources, zipPath, level, progress, token));
            }
            catch (Exception ex)
            {
                ShowError(owner, "zip으로 압축", ex);
                return null;
            }

            if (result.Cancelled)
                MessageBox.Show(owner, "압축을 취소했습니다. 만들던 zip은 지웠습니다.", "zip으로 압축", MessageBoxButton.OK, MessageBoxImage.Information);
            else if (result.Errors.Count > 0)
                MessageBox.Show(owner, string.Format("zip을 만들었지만 {0}개 항목을 넣지 못했습니다.\n{1}\n\n{2}", result.Errors.Count, result.ZipPath, Summarize(result.Errors)),
                    "zip으로 압축", MessageBoxButton.OK, MessageBoxImage.Warning);
            return result;
        }

        public static void ShowError(Window owner, string title, Exception ex)
        {
            var inner = ex is AggregateException aggregate && aggregate.InnerException != null ? aggregate.InnerException : ex;
            MessageBox.Show(owner, ZipArchiveService.Describe(inner), title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        public static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            var unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return unit == 0 ? value.ToString("0") + " " + units[unit] : value.ToString("0.##") + " " + units[unit];
        }

        private static void ReportProblems(Window owner, ZipExtractResult result)
        {
            if (result.Cancelled)
            {
                MessageBox.Show(owner, string.Format("압축 풀기를 취소했습니다. 그때까지 푼 파일 {0:N0}개는 남아 있습니다.", result.FilesWritten),
                    ExtractTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (result.Errors.Count == 0)
                return;
            var header = result.Stopped
                ? string.Format("오류가 많아({0}개 이상) 중간에 멈췄습니다.", ZipArchiveService.MaxErrors)
                : string.Format("{0:N0}개 항목을 풀지 못했습니다(나머지 {1:N0}개는 풀었습니다).", result.Errors.Count, result.FilesWritten);
            MessageBox.Show(owner, header + "\n\n" + Summarize(result.Errors), ExtractTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private static string Summarize(IList<string> errors)
        {
            var text = string.Join(Environment.NewLine, errors.Take(8));
            if (errors.Count > 8)
                text += Environment.NewLine + string.Format("외 {0}개", errors.Count - 8);
            return text;
        }

        private static void TryDeleteEmptyDirectory(string directory)
        {
            try
            {
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
            }
            catch (Exception)
            {
            }
        }
    }
}

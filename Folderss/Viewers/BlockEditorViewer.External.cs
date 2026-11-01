using Folderss.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace Folderss.Viewers
{
    /// <summary>
    /// 블록 편집기 뷰어의 외부 변경 감지·반영과 링크 열기(#75).
    /// <list type="bullet">
    /// <item>문서와 사이드카(<c>문서.md.meta.json</c>)를 <see cref="FileSystemWatcher"/>로 감시한다. 디스크 내용이 마지막으로 읽거나 저장한 내용과 같으면(자기 저장 포함) 무시한다.</item>
    /// <item>저장하지 않은 변경이 없으면 바로 다시 읽고, 있으면 묻는다(거부한 같은 내용은 다시 묻지 않음 — Markdown 뷰어와 같은 규칙). 보던 블록(앵커)은 유지한다.</item>
    /// <item>보이지 않는 탭이면 활성화될 때 처리한다.</item>
    /// </list>
    /// </summary>
    public sealed partial class BlockEditorViewer
    {
        private FileSystemWatcher _fileWatcher;
        private DispatcherTimer _fileReloadTimer;
        private bool _isActive = true;
        private bool _pendingExternalReload;
        private bool _reloadPromptOpen;
        private bool _disposed;
        private string _diskText;          // 마지막으로 읽거나 저장한 Markdown(디스크 글자 그대로, 줄바꿈 포함)
        private string _diskSidecar;       // 마지막으로 읽거나 저장한 사이드카(없으면 null)
        private string _promptedText;      // 다시 읽기를 거부한 외부 내용 — 파일이 또 바뀌기 전까지 다시 묻지 않는다
        private string _promptedSidecar;
        private string _reloadNote;

        // ── 활성화·정리 ────────────────────────────────────────────────────

        public void SetActive(bool isActive)
        {
            _isActive = isActive;
            if (!isActive)
                return;
            if (_pendingExternalReload)
                ScheduleExternalReload();
            // 이미 열린 탭으로 다른 문서의 "파일.md#제목" 링크가 들어오면 활성화될 때 이동한다.
            ConsumePendingAnchorLater();
        }

        public void Dispose()
        {
            _disposed = true;
            StopFileWatcher();
        }

        // ── 파일 감시 ──────────────────────────────────────────────────────

        private void StartFileWatcher(string filePath)
        {
            StopFileWatcher();
            var dir = Path.GetDirectoryName(filePath);
            var fileName = Path.GetFileName(filePath);
            if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(fileName) || !Directory.Exists(dir))
                return;
            try
            {
                var watcher = new FileSystemWatcher(dir)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                };
                watcher.Filters.Add(fileName);
                watcher.Filters.Add(Path.GetFileName(BlockSidecar.PathFor(filePath)));
                watcher.Changed += OnWatchedFileChanged;
                watcher.Created += OnWatchedFileChanged;
                watcher.Deleted += OnWatchedFileChanged;
                watcher.Renamed += OnWatchedFileChanged;
                watcher.EnableRaisingEvents = true;
                _fileWatcher = watcher;
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is UnauthorizedAccessException || ex is PlatformNotSupportedException)
            {
                // 감시할 수 없는 위치(일부 네트워크 드라이브 등)면 ↻ 다시 읽기로만 반영한다.
                _fileWatcher = null;
            }
        }

        private void StopFileWatcher()
        {
            _fileReloadTimer?.Stop();
            _pendingExternalReload = false;
            if (_fileWatcher == null)
                return;
            _fileWatcher.EnableRaisingEvents = false;
            _fileWatcher.Changed -= OnWatchedFileChanged;
            _fileWatcher.Created -= OnWatchedFileChanged;
            _fileWatcher.Deleted -= OnWatchedFileChanged;
            _fileWatcher.Renamed -= OnWatchedFileChanged;
            _fileWatcher.Dispose();
            _fileWatcher = null;
        }

        private void OnWatchedFileChanged(object sender, FileSystemEventArgs e)
        {
            if (_disposed || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_disposed)
                    return;
                _pendingExternalReload = true;
                if (_isActive)
                    ScheduleExternalReload();
            }));
        }

        private void ScheduleExternalReload()
        {
            // 저장 프로그램은 여러 번 나눠 쓰므로 잠깐 기다렸다가 한 번만 읽는다.
            _fileReloadTimer.Stop();
            _fileReloadTimer.Start();
        }

        private void OnFileReloadTimerTick(object sender, EventArgs e)
        {
            _fileReloadTimer.Stop();
            ReloadExternalChange();
        }

        private bool ReadDisk(out string text, out string sidecar)
        {
            text = sidecar = null;
            if (string.IsNullOrWhiteSpace(_filePath) || !File.Exists(_filePath))
                return false;
            var encoding = MarkdownViewer.DetectEncoding(_filePath);
            text = MarkdownViewer.ReadAllTextAllowingWriters(_filePath, encoding);
            var sidecarPath = BlockSidecar.PathFor(_filePath);
            sidecar = File.Exists(sidecarPath) ? MarkdownViewer.ReadAllTextAllowingWriters(sidecarPath, Encoding.UTF8) : null;
            return true;
        }

        private void ReloadExternalChange()
        {
            // 확인창(모달)이 떠 있는 동안 감시 이벤트로 다시 들어와 확인창이 겹치지 않게 한다.
            if (_reloadPromptOpen || _disposed || _tooLarge || _filePath == null)
                return;

            string text, sidecar;
            try
            {
                // 문서가 지워졌거나 이름이 바뀌었으면(저장 프로그램의 임시 파일 교체 중일 수도 있음) 그대로 둔다.
                if (!ReadDisk(out text, out sidecar))
                {
                    _pendingExternalReload = false;
                    return;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // 다른 프로그램이 아직 쓰는 중이면 조금 뒤에 다시 읽는다.
                if (_isActive)
                    ScheduleExternalReload();
                return;
            }

            _pendingExternalReload = false;
            if (text == _diskText && sidecar == _diskSidecar)
            {
                _promptedText = _promptedSidecar = null;
                return;
            }
            if (text == _promptedText && sidecar == _promptedSidecar)
                return;

            if (_editor.IsModified)
            {
                _reloadPromptOpen = true;
                try
                {
                    const string message = "파일이 외부에서 변경되었습니다. 다시 읽으면 저장하지 않은 변경 내용이 사라집니다.\n\n다시 읽을까요?";
                    // owner 없이 띄우면 앱이 백그라운드일 때 확인창이 다른 창 뒤에 숨는다(Markdown 뷰어와 같은 처리).
                    var owner = Window.GetWindow(this);
                    var answer = owner != null
                        ? MessageBox.Show(owner, message, Path.GetFileName(_filePath), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
                        : MessageBox.Show(message, Path.GetFileName(_filePath), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                    if (answer != MessageBoxResult.Yes)
                    {
                        _promptedText = text;
                        _promptedSidecar = sidecar;
                        return;
                    }
                    // 확인창이 떠 있는 동안 또 바뀌었을 수 있으므로 지금 내용을 다시 읽는다.
                    try
                    {
                        string latest, latestSidecar;
                        if (ReadDisk(out latest, out latestSidecar))
                        {
                            text = latest;
                            sidecar = latestSidecar;
                        }
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                    }
                }
                finally
                {
                    _reloadPromptOpen = false;
                }
            }

            _encoding = MarkdownViewer.DetectEncoding(_filePath);
            _reloadNote = "외부 변경 반영 " + DateTime.Now.ToString("HH:mm:ss");
            ApplyContent(text, sidecar, true);
        }

        // ── 링크 ───────────────────────────────────────────────────────────

        /// <summary>
        /// 본문 링크 Ctrl+클릭: <c>#제목</c>은 이 문서 안에서 이동, 로컬 <c>.md</c>는 새 뷰어 탭(<c>파일.md#제목</c>이면 그 제목으로),
        /// 그 밖의 로컬 파일은 뷰어 탭(실행 파일은 확인 뒤 실행), 폴더는 탐색기, 웹 주소는 브라우저. Markdown 뷰어의 링크 처리와 같은 규칙.
        /// </summary>
        private void OpenLink(string link)
        {
            try
            {
                if (link.StartsWith("#", StringComparison.Ordinal))
                {
                    if (!_editor.ScrollToHeading(link))
                        ShowLinkMessage("이 문서에서 제목을 찾지 못했습니다: " + link);
                    return;
                }

                var path = _filePath != null ? BlockFind.ResolveLocalLink(Path.GetDirectoryName(_filePath), link) : null;
                if (path != null)
                {
                    var fragment = BlockFind.ExtractFragment(link);
                    if (_filePath != null && string.Equals(path, Path.GetFullPath(_filePath), StringComparison.OrdinalIgnoreCase))
                    {
                        if (fragment != null && !_editor.ScrollToHeading(fragment))
                            ShowLinkMessage("이 문서에서 제목을 찾지 못했습니다: #" + fragment);
                        return;
                    }
                    if (Directory.Exists(path))
                    {
                        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                        return;
                    }
                    if (!File.Exists(path))
                    {
                        ShowLinkMessage("파일이 없습니다:\n" + path);
                        return;
                    }
                    // 받은 문서의 링크 한 번으로 프로그램·스크립트가 실행되지 않도록 확인을 받는다.
                    if (FileOperationService.IsExecutable(path))
                    {
                        if (ConfirmRunExecutable(path))
                            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                        return;
                    }
                    if (fragment != null)
                        MarkdownViewer.PendingAnchors[path] = fragment;
                    FileOpenRequested?.Invoke(this, path);
                    return;
                }

                Uri uri;
                if (Uri.TryCreate(link, UriKind.Absolute, out uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeMailto))
                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ShowLinkMessage(ex.Message);
            }
        }

        private bool ConfirmRunExecutable(string path)
        {
            var message = "링크가 실행 파일을 가리킵니다. 열면 프로그램이나 스크립트가 실행됩니다.\n\n" + path + "\n\n실행할까요?";
            var owner = Window.GetWindow(this);
            var answer = owner != null
                ? MessageBox.Show(owner, message, "실행 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
                : MessageBox.Show(message, "실행 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            return answer == MessageBoxResult.Yes;
        }

        private void ShowLinkMessage(string message)
        {
            var owner = Window.GetWindow(this);
            if (owner != null)
                MessageBox.Show(owner, message, "링크", MessageBoxButton.OK, MessageBoxImage.Information);
            else
                MessageBox.Show(message, "링크", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// 다른 문서의 <c>파일.md#제목</c> 링크로 이 문서가 열리거나 활성화됐으면 그 제목으로 옮긴다. 화면에 붙은 뒤(레이아웃 후) 처리한다.
        /// 링크를 누른 쪽이 Markdown 뷰어여도 같은 표(<see cref="MarkdownViewer.PendingAnchors"/>)를 쓴다.
        /// </summary>
        private void ConsumePendingAnchorLater()
        {
            if (_filePath == null || _tooLarge)
                return;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                string fragment;
                if (_disposed || _filePath == null || !IsVisible)
                    return;
                var key = Path.GetFullPath(_filePath);
                if (!MarkdownViewer.PendingAnchors.TryGetValue(key, out fragment))
                    return;
                MarkdownViewer.PendingAnchors.Remove(key);
                _editor.ScrollToHeading(fragment);
            }));
        }
    }
}

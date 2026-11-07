using Folderss.Services;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Folderss.Viewers
{
    /// <summary>
    /// 일반 텍스트 편집기의 외부 변경 감시(블록 편집기·Markdown 뷰어와 같은 규칙).
    /// <list type="bullet">
    /// <item>디스크 내용의 SHA-256이 마지막으로 읽거나 저장한 내용과 같으면(자기 저장 포함) 무시한다. 원문 사본을 따로 들고 있지 않아 큰 파일도 가볍다.</item>
    /// <item>바뀌었으면 다시 읽을지 묻는다 — 저장하지 않은 변경이 없으면 기본 예, 있으면 경고·기본 아니요. 거부한 같은 내용은 다시 묻지 않는다.</item>
    /// <item>다시 읽어도 캐럿·스크롤 위치는 유지한다. 보이지 않는 탭이면 활성화될 때 처리한다.</item>
    /// </list>
    /// </summary>
    public partial class TextViewer
    {
        private FileSystemWatcher _fileWatcher;
        private DispatcherTimer _fileReloadTimer;
        private bool _disposed;
        private bool _isActive = true;
        private bool _pendingExternalReload;
        private bool _reloadPromptOpen;
        private byte[] _promptedHash;        // 다시 읽기를 거부한 외부 내용 — 파일이 또 바뀌기 전까지 다시 묻지 않는다
        private string _externalNote;

        private void InitializeExternalWatch()
        {
            _fileReloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _fileReloadTimer.Tick += async (s, e) =>
            {
                _fileReloadTimer.Stop();
                await ReloadExternalChangeAsync();
            };
        }

        public void SetActive(bool isActive)
        {
            _isActive = isActive;
            if (isActive && _pendingExternalReload)
                ScheduleExternalReload();
        }

        private void StartFileWatcher(string filePath)
        {
            StopFileWatcher();
            var dir = Path.GetDirectoryName(filePath);
            var fileName = Path.GetFileName(filePath);
            if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(fileName) || !Directory.Exists(dir))
                return;
            try
            {
                var watcher = new FileSystemWatcher(dir, fileName)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                };
                watcher.Changed += OnWatchedFileChanged;
                watcher.Created += OnWatchedFileChanged;
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

        private async Task ReloadExternalChangeAsync()
        {
            // 확인창(모달)이 떠 있는 동안 감시 이벤트로 다시 들어와 확인창이 겹치지 않게 한다.
            if (_reloadPromptOpen || _disposed || _tooLarge || _loading || _filePath == null)
                return;

            var path = _filePath;
            byte[] hash;
            try
            {
                hash = await Task.Run(() => File.Exists(path) ? SHA256.HashData(TextFileEncoding.ReadAllBytesShared(path)) : null);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // 다른 프로그램이 아직 쓰는 중이면 조금 뒤에 다시 읽는다.
                if (_isActive && !_disposed)
                    ScheduleExternalReload();
                return;
            }
            if (_disposed || path != _filePath || _loading)
                return;

            _pendingExternalReload = false;
            // 지워졌거나 이름이 바뀌었으면(저장 프로그램의 임시 파일 교체 중일 수도 있음) 그대로 둔다.
            if (hash == null)
                return;
            if (_diskHash != null && hash.SequenceEqual(_diskHash))
            {
                _promptedHash = null;
                return;
            }
            if (_promptedHash != null && hash.SequenceEqual(_promptedHash))
                return;

            MessageBoxResult answer;
            _reloadPromptOpen = true;
            try
            {
                var modified = IsModified;
                answer = AskMessage(modified
                        ? "파일이 외부에서 변경되었습니다. 다시 읽으면 저장하지 않은 변경 내용이 사라집니다.\n\n다시 읽을까요?"
                        : "파일이 외부에서 변경되었습니다. 다시 읽을까요?",
                    MessageBoxButton.YesNo,
                    modified ? MessageBoxImage.Warning : MessageBoxImage.Question,
                    modified ? MessageBoxResult.No : MessageBoxResult.Yes);
            }
            finally
            {
                _reloadPromptOpen = false;
            }
            if (_disposed)
                return;
            if (answer != MessageBoxResult.Yes)
            {
                _promptedHash = hash;
                return;
            }

            // 확인창이 떠 있는 동안 또 바뀌었을 수 있으므로 지금 내용을 다시 읽는다. 지정해 연 인코딩은 유지한다.
            _externalNote = "외부 변경 반영 " + DateTime.Now.ToString("HH:mm:ss");
            await LoadAsync(_forcedEncoding, true);
        }
    }
}

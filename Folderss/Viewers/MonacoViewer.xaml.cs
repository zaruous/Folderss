using Folderss.Services;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Folderss.Viewers
{
    public partial class MonacoViewer : UserControl, IFileViewer, IViewerShortcutHandler, IViewerToolbarProvider, IDisposable
    {
        private const long LargeFileBytes = 20L * 1024 * 1024;
        private const long HugeFileBytes = 50L * 1024 * 1024;

        private static readonly string ResourcesPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Viewers", "Resources");

        // 언어 결정은 편집기 안(monaco-app.html detectLanguage)이 Monaco 등록 정보로 하고, C#은 강제 언어만 넘긴다(#82).
        // 강제 언어 = 파일별 선택 → 설정의 사용자 지정 → 예외 사전(MonacoLanguages.ResolveForced). 초대용량은 plaintext.

        private ThemeInfo _currentTheme = ThemeManager.CurrentTheme;
        private bool _webViewReady;
        private string _filePath;
        private Encoding _encoding;
        private bool _modified;
        private bool _largeFileMode;
        private bool _hugeFileMode;
        private string _pendingContent;
        private string _pendingLanguage;
        private string _lastLoadedContent;
        private FileSystemWatcher _fileWatcher;
        private readonly DispatcherTimer _fileReloadTimer;
        private bool _disposed;
        private bool _reloadPromptOpen;
        private WebViewIdleSaver _idleSaver;
        private string _pageTheme;
        private readonly MonacoLanguagePicker _languagePicker;

        public UIElement View => this;
        public ViewerCapabilities Capabilities => ViewerCapabilities.Edit;

        public event EventHandler<string> TitleChanged;
        public event EventHandler<bool> ModifiedChanged;

        /// <summary>탭 툴바의 언어 선택(#82).</summary>
        public UIElement ToolbarContent => _languagePicker;

        public MonacoViewer()
        {
            InitializeComponent();
            _languagePicker = new MonacoLanguagePicker();
            _languagePicker.LanguagePicked += OnLanguagePicked;
            _fileReloadTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _fileReloadTimer.Tick += OnFileReloadTimerTick;
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            await InitializeWebViewAsync();
        }

        private async System.Threading.Tasks.Task InitializeWebViewAsync()
        {
            // 절전/장시간 유휴 후 WebView2 브라우저 프로세스가 끊긴 상태에서 초기화하면
            // RPC_E_DISCONNECTED(COMException)가 난다. async void OnLoaded에서 올라가면 앱 전체가
            // 종료되므로 여기서 잡아 이 뷰어에만 오류를 표시한다.
            try
            {
                CoreWebView2Environment.GetAvailableBrowserVersionString();

                var env = await CoreWebView2Environment.CreateAsync(null,
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Folderss", "WebView2Cache"));

                await WebView.EnsureCoreWebView2Async(env);
            }
            catch (Exception ex)
            {
                if (_disposed)
                    return;
                WebView.Visibility = Visibility.Collapsed;
                ErrorText.Text = "WebView2 초기화 오류:\n" + ex.GetType().Name + "\n" + ex.Message;
                ErrorText.Visibility = Visibility.Visible;
                return;
            }

            // 초기화를 기다리는 사이 탭이 닫혀 Dispose됐으면 해제된 컨트롤을 건드리지 않는다.
            if (_disposed)
                return;

            // 이 뷰어는 내부 가상 호스트(folderss-viewer) 페이지만 열고 다른 주소 이동은 NavigationStarting에서 막으므로
            // SmartScreen 평판 검사가 지킬 것이 없다. 사내망 등에서 검사 서버 응답을 기다리느라 페이지 표시가 매번 약 2초 늦어져 끈다.
            WebView.CoreWebView2.Settings.IsReputationCheckingRequired = false;
            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "folderss-viewer", ResourcesPath,
                CoreWebView2HostResourceAccessKind.Allow);

            WebView.CoreWebView2.AddWebResourceRequestedFilter(
                "*", CoreWebView2WebResourceContext.All);
            WebView.CoreWebView2.WebResourceRequested += OnWebResourceRequested;
            WebView.CoreWebView2.NavigationStarting += OnNavigationStarting;
            WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            _webViewReady = true;
            _idleSaver = new WebViewIdleSaver(this, WebView, OnBecameVisible);
            WebView.CoreWebView2.Navigate("https://folderss-viewer/monaco-app.html");
            WebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
        }

        private void OnWebResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            var uri = new Uri(e.Request.Uri);
            if (!string.Equals(uri.Host, "folderss-viewer", StringComparison.OrdinalIgnoreCase))
                e.Response = WebView.CoreWebView2.Environment.CreateWebResourceResponse(null, 403, "Blocked", "");
        }

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            var uri = new Uri(e.Uri);
            if (!string.Equals(uri.Host, "folderss-viewer", StringComparison.OrdinalIgnoreCase))
                e.Cancel = true;
        }

        private async void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            WebView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
            if (_pendingContent != null)
                await CallAppOpen(_pendingContent, _pendingLanguage);
        }

        private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            // 직접 문자열을 훑으면 이스케이프 처리를 놓쳐 저장 본문이 깨질 수 있으므로 JSON 파서로 읽는다.
            using (var document = JsonDocument.Parse(e.WebMessageAsJson))
            {
                var message = document.RootElement;
                if (message.ValueKind != JsonValueKind.Object)
                    return;

                switch (GetJsonString(message, "type"))
                {
                    case "modified":
                        JsonElement value;
                        var val = message.TryGetProperty("value", out value) && value.ValueKind == JsonValueKind.True;
                        _modified = val;
                        ModifiedChanged?.Invoke(this, val);
                        break;
                    case "save-request":
                        var content = GetJsonString(message, "content");
                        if (content != null)
                            Save(content);
                        break;
                    case "languages":
                        OnLanguagesReceived(message);
                        break;
                    case "language":
                        JsonElement flag;
                        // "자동" 표시는 이 파일에 고른 언어(파일별 기억)가 없을 때. 사용자 지정·예외 사전으로 정해진 언어도 자동으로 본다.
                        _languagePicker.SetCurrent(GetJsonString(message, "id"),
                            MonacoLanguageSettings.Shared.GetRemembered(_filePath) == null,
                            message.TryGetProperty("huge", out flag) && flag.ValueKind == JsonValueKind.True);
                        break;
                }
            }
        }

        private static string GetJsonString(JsonElement obj, string name)
        {
            JsonElement property;
            return obj.TryGetProperty(name, out property) && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
        }

        public void Load(string filePath)
        {
            StopFileWatcher();

            _filePath = filePath;
            _encoding = DetectEncoding(filePath);
            _modified = false;
            var fileSize = new FileInfo(filePath).Length;
            _largeFileMode = fileSize > LargeFileBytes;
            _hugeFileMode = fileSize > HugeFileBytes;
            ModifiedChanged?.Invoke(this, false);

            var content = File.ReadAllText(filePath, _encoding);
            _lastLoadedContent = content;
            var language = _hugeFileMode ? MonacoLanguages.PlainText : GetForcedLanguage(filePath);

            TitleChanged?.Invoke(this, Path.GetFileName(filePath));
            StartFileWatcher(filePath);

            if (!_webViewReady)
            {
                _pendingContent = content;
                _pendingLanguage = language;
                return;
            }

            var _ = CallAppOpen(content, language);
        }

        private async System.Threading.Tasks.Task CallAppOpen(string content, string language)
        {
            // 확인창이 떠 있는 동안 탭이 닫히거나 WebView2가 사라졌을 수 있다(이슈 #30) — 준비되면 다시 연다.
            if (_disposed || WebView.CoreWebView2 == null)
            {
                _pendingContent = content;
                _pendingLanguage = language;
                return;
            }

            _pendingContent = null;
            _pendingLanguage = null;
            _pageTheme = ThemeName(_currentTheme);

            var script = string.Format(
                "app.open({0},{1},{2},{3},{4},{5},{6})",
                JsonString(content),
                JsonString(language),
                JsonString(ThemeName(_currentTheme)),
                _hugeFileMode ? "true" : "false",
                _largeFileMode ? "true" : "false",
                _hugeFileMode ? "true" : "false",
                JsonString(_filePath == null ? null : Path.GetFileName(_filePath)));
            await WebView.CoreWebView2.ExecuteScriptAsync(script);
        }

        private void Save(string content)
        {
            if (_filePath == null) return;

            try
            {
                // 원본을 먼저 지우고 옮기면 옮기기가 실패할 때 원본이 사라진다. 임시 파일에 쓴 뒤 덮어쓰기 이동으로 교체한다.
                SettingsFile.WriteAllText(_filePath, content, _encoding);
                _lastLoadedContent = content;
                var _ = WebView.CoreWebView2.ExecuteScriptAsync("app.markSaved()");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "저장 오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void ApplyTheme(ThemeInfo theme)
        {
            _currentTheme = theme;
            if (_disposed) return;
            // 편집기가 그려지기 전에 보이는 바탕을 테마 색으로 맞춰 깜빡임을 없앤다.
            WebView.DefaultBackgroundColor = PageBackground(theme);
            // 가려진 탭은 다시 보일 때(OnBecameVisible) 적용한다. 일시 중지된 WebView에 스크립트를 보내지 않는다.
            if (!_webViewReady || !IsVisible) return;
            SendTheme();
        }

        private void SendTheme()
        {
            _pageTheme = ThemeName(_currentTheme);
            var _ = WebView.CoreWebView2.ExecuteScriptAsync(
                string.Format("app.setTheme({0})", JsonString(_pageTheme)));
        }

        // 탭이 다시 보일 때 가려진 동안 바뀐 테마를 적용한다.
        private void OnBecameVisible()
        {
            if (_webViewReady && _pageTheme != null && _pageTheme != ThemeName(_currentTheme))
                SendTheme();
        }

        public void Export(ExportFormat format, string destPath)
        {
        }

        public bool IsModified => _modified;

        public bool HandleShortcut(KeyEventArgs e, KeyBindingService kb)
        {
            if (!kb.Matches(e, "ShowSearch"))
                return false;

            if (!_webViewReady || WebView.CoreWebView2 == null)
                return false;

            WebView.Focus();
            var _ = WebView.CoreWebView2.ExecuteScriptAsync("app.openFind()");
            return true;
        }

        private void StartFileWatcher(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            var fileName = Path.GetFileName(filePath);
            if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(fileName) || !Directory.Exists(dir))
                return;

            _fileWatcher = new FileSystemWatcher(dir, fileName)
            {
                NotifyFilter = NotifyFilters.FileName |
                               NotifyFilters.LastWrite |
                               NotifyFilters.Size |
                               NotifyFilters.CreationTime
            };
            _fileWatcher.Changed += OnWatchedFileChanged;
            _fileWatcher.Created += OnWatchedFileChanged;
            _fileWatcher.Renamed += OnWatchedFileChanged;
            _fileWatcher.EnableRaisingEvents = true;
        }

        private void StopFileWatcher()
        {
            _fileReloadTimer.Stop();
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

            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (_disposed)
                    return;
                _fileReloadTimer.Stop();
                _fileReloadTimer.Start();
            }));
        }

        private void OnFileReloadTimerTick(object sender, EventArgs e)
        {
            _fileReloadTimer.Stop();
            PromptAndReloadAsync();
        }

        private async void PromptAndReloadAsync()
        {
            // 모달 확인창의 메시지 펌프 안에서 타이머가 다시 돌아 확인창이 겹쳐 뜨지 않게 한다.
            if (_reloadPromptOpen || _disposed)
                return;
            if (string.IsNullOrWhiteSpace(_filePath) || !File.Exists(_filePath))
                return;

            try
            {
                var content = ReadAllTextAllowingWriters(_filePath, _encoding);
                if (string.Equals(content, _lastLoadedContent, StringComparison.Ordinal))
                    return;

                var message = _modified
                    ? "파일이 외부에서 변경되었습니다. 다시 읽으면 편집 중인 내용이 사라집니다.\n\n다시 읽으시겠습니까?"
                    : "파일이 외부에서 변경되었습니다. 다시 읽으시겠습니까?";
                MessageBoxResult answer;
                _reloadPromptOpen = true;
                try
                {
                    answer = MessageBox.Show(
                        message,
                        Path.GetFileName(_filePath),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question,
                        MessageBoxResult.Yes);
                }
                finally
                {
                    _reloadPromptOpen = false;
                }

                if (answer != MessageBoxResult.Yes || _disposed)
                    return;

                // 확인창이 떠 있는 동안 파일이 또 바뀌었을 수 있어 최신 내용을 다시 읽는다.
                content = ReadAllTextAllowingWriters(_filePath, _encoding);

                _lastLoadedContent = content;
                _modified = false;
                ModifiedChanged?.Invoke(this, false);

                var language = _hugeFileMode ? MonacoLanguages.PlainText : GetForcedLanguage(_filePath);
                await CallAppOpen(content, language);
            }
            catch (IOException)
            {
                _fileReloadTimer.Stop();
                _fileReloadTimer.Start();
            }
            catch (UnauthorizedAccessException)
            {
                _fileReloadTimer.Stop();
                _fileReloadTimer.Start();
            }
        }

        private static string ReadAllTextAllowingWriters(string filePath, Encoding encoding)
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(fs, encoding, true))
                return reader.ReadToEnd();
        }

        public void Dispose()
        {
            _disposed = true;
            StopFileWatcher();
            if (WebView.CoreWebView2 != null)
            {
                WebView.CoreWebView2.WebResourceRequested -= OnWebResourceRequested;
                WebView.CoreWebView2.NavigationStarting -= OnNavigationStarting;
                WebView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
            }

            // WPF WebView2는 화면에서 빠져도 스스로 정리되지 않아, 탭을 닫을 때마다 브라우저 프로세스와 메모리가 남는다.
            // 준비 플래그를 내려 이후 들어오는 ExecuteScriptAsync 호출(테마 변경 등)이 해제된 컨트롤을 건드리지 않게 한다.
            _webViewReady = false;
            _idleSaver?.Dispose();
            WebView.Dispose();
        }

        private static Encoding DetectEncoding(string filePath)
        {
            var bom = new byte[4];
            using (var fs = File.OpenRead(filePath))
                fs.Read(bom, 0, 4);

            if (bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF) return Encoding.UTF8;
            if (bom[0] == 0xFF && bom[1] == 0xFE) return Encoding.Unicode;
            if (bom[0] == 0xFE && bom[1] == 0xFF) return Encoding.BigEndianUnicode;
            return new UTF8Encoding(false);
        }

        /// <summary>편집기에 넘길 강제 언어(파일별 선택 → 사용자 지정 → 예외 사전). null이면 편집기가 파일 이름으로 고른다.</summary>
        private static string GetForcedLanguage(string filePath)
        {
            return MonacoLanguageSettings.Shared.ResolveForced(filePath);
        }

        private void OnLanguagesReceived(JsonElement message)
        {
            JsonElement items;
            if (!message.TryGetProperty("items", out items) || items.ValueKind != JsonValueKind.Array)
                return;
            var languages = new List<KeyValuePair<string, string>>();
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;
                var id = GetJsonString(item, "id");
                if (!string.IsNullOrWhiteSpace(id))
                    languages.Add(new KeyValuePair<string, string>(id, GetJsonString(item, "name")));
            }
            if (languages.Count > 0)
                _languagePicker.SetLanguages(languages);
        }

        // 탭 툴바에서 언어를 골랐다(null = 자동). 편집기에 바로 적용하고 파일별로 기억한다(자동이면 기억 삭제).
        private void OnLanguagePicked(object sender, string languageId)
        {
            if (_disposed || _filePath == null)
                return;
            try
            {
                MonacoLanguageSettings.Shared.RememberAndSave(_filePath, languageId);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // 파일별 언어 기억은 편의 기능이라 저장하지 못해도 이번 탭에는 적용한다(다음에 열 때 기억만 빠짐). 원인은 디버그 출력에 남긴다.
                System.Diagnostics.Trace.WriteLine("monaco-language.json 저장 실패: " + ex);
            }

            if (!_webViewReady || WebView.CoreWebView2 == null || _hugeFileMode)
                return;
            // 자동이면 사용자 지정·예외 사전까지 다시 따진다(파일별 선택만 지운 상태).
            var language = languageId ?? GetForcedLanguage(_filePath);
            var _ = WebView.CoreWebView2.ExecuteScriptAsync(
                string.Format("app.setLanguage({0})", JsonString(language)));
        }

        // monaco-app.html의 편집기 테마 배경(folderss-dark #0e1013, folderss-light #ffffff)과 같은 색
        private static System.Drawing.Color PageBackground(ThemeInfo theme)
        {
            return theme.IsLight
                ? System.Drawing.ColorTranslator.FromHtml("#ffffff")
                : System.Drawing.ColorTranslator.FromHtml("#0e1013");
        }

        // monaco-app.html은 밝은/어두운 편집기 테마 두 가지만 있다.
        private static string ThemeName(ThemeInfo theme)
        {
            return theme.IsLight ? "light" : "dark";
        }

        private static string JsonString(string value)
        {
            if (value == null) return "null";
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.AppendFormat("\\u{0:x4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}

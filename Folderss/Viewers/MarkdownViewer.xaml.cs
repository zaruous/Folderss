using Folderss.Services;
using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace Folderss.Viewers
{
    public partial class MarkdownViewer : UserControl, IFileViewer, IFileOpenRequester, IViewerActivationAware, IViewerShortcutHandler, IDisposable
    {
        private const long LargeMarkdownBytes = 5L * 1024 * 1024;

        private static readonly string ResourcesPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Viewers", "Resources");

        private string _filePath;
        private Encoding _encoding;
        private AppTheme _currentTheme = AppTheme.Black;
        private bool _webViewReady;
        private bool _pageReady;
        private bool _modified;
        private bool _largeFileMode;
        private FileSystemWatcher _fileWatcher;
        private readonly DispatcherTimer _fileReloadTimer;
        private string _lastLoadedContent;
        private bool _disposed;
        private bool _isActive = true;
        private bool _pendingExternalReload;
        private CoreWebView2ContextMenuItem _printMenuItem;
        private CoreWebView2ContextMenuItem _saveAsMenuItem;
        private CoreWebView2ContextMenuItem _shareMenuItem;

        private string _pendingContent;

        public UIElement View => this;
        public ViewerCapabilities Capabilities =>
            ViewerCapabilities.Edit | ViewerCapabilities.Export;

        public event EventHandler<string> TitleChanged;
        public event EventHandler<bool> ModifiedChanged;
        public event EventHandler<string> FileOpenRequested;

        public MarkdownViewer()
        {
            InitializeComponent();
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
            try
            {
                CoreWebView2Environment.GetAvailableBrowserVersionString();
            }
            catch (Exception ex)
            {
                WebView.Visibility = Visibility.Collapsed;
                ErrorText.Text = "WebView2 초기화 오류:\n" + ex.GetType().Name + "\n" + ex.Message;
                ErrorText.Visibility = Visibility.Visible;
                return;
            }

            var env = await CoreWebView2Environment.CreateAsync(null,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Folderss", "WebView2Cache"));

            await WebView.EnsureCoreWebView2Async(env);

            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "folderss-viewer", ResourcesPath,
                CoreWebView2HostResourceAccessKind.Allow);

            WebView.CoreWebView2.AddWebResourceRequestedFilter(
                "*", CoreWebView2WebResourceContext.All);
            WebView.CoreWebView2.WebResourceRequested += OnWebResourceRequested;
            WebView.CoreWebView2.NavigationStarting   += OnNavigationStarting;
            WebView.CoreWebView2.NavigationCompleted  += OnNavigationCompleted;
            WebView.CoreWebView2.WebMessageReceived   += OnWebMessageReceived;
            WebView.CoreWebView2.ContextMenuRequested += OnContextMenuRequested;
            InitializeContentContextMenuItems();

            _webViewReady = true;
            WebView.CoreWebView2.Navigate("https://folderss-viewer/markdown-app.html");
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
            {
                e.Cancel = true;
                return;
            }

            _pageReady = false;
        }

        private async void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess || _disposed)
                return;

            _pageReady = true;
            var content = _pendingContent ?? _lastLoadedContent;
            if (content != null)
                await CallAppOpen(content);
        }

        private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            var json = e.WebMessageAsJson;
            var type = ExtractJsonString(json, "type");

            switch (type)
            {
                case "modified":
                    var val = json.Contains("\"value\":true");
                    _modified = val;
                    ModifiedChanged?.Invoke(this, val);
                    break;

                case "save-request":
                    var content = ExtractJsonString(json, "content");
                    if (content != null)
                        Save(content);
                    break;

                case "export-html":
                    var html = ExtractJsonString(json, "content");
                    if (html != null)
                        ExportHtmlContent(html);
                    break;

                case "export-pdf":
                    ExportPdfAsync();
                    break;

                case "open-link":
                    var uri = ExtractJsonString(json, "uri");
                    var href = ExtractJsonString(json, "href");
                    if (uri != null || href != null)
                        HandleLinkClick(uri, href);
                    break;

            }
        }

        private void OnContextMenuRequested(object sender, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            ReplaceContextMenuItem(e, "print", _printMenuItem);
            ReplaceContextMenuItem(e, "saveAs", _saveAsMenuItem);
            ReplaceContextMenuItem(e, "share", _shareMenuItem);
        }

        private void InitializeContentContextMenuItems()
        {
            _printMenuItem = CreateContentContextMenuItem("인쇄", "app.printContent()");
            _saveAsMenuItem = CreateContentContextMenuItem("다른 이름으로 저장", "app.saveContentAs()");
            _shareMenuItem = CreateContentContextMenuItem("공유", "app.shareContent()");
        }

        private CoreWebView2ContextMenuItem CreateContentContextMenuItem(string label, string script)
        {
            var item = WebView.CoreWebView2.Environment.CreateContextMenuItem(
                label, null, CoreWebView2ContextMenuItemKind.Command);
            item.CustomItemSelected += async (s, e) =>
            {
                if (!_disposed && _pageReady)
                    await WebView.CoreWebView2.ExecuteScriptAsync(script);
            };
            return item;
        }

        private void ReplaceContextMenuItem(
            CoreWebView2ContextMenuRequestedEventArgs args,
            string defaultName,
            CoreWebView2ContextMenuItem replacement)
        {
            for (var i = 0; i < args.MenuItems.Count; i++)
            {
                if (!string.Equals(args.MenuItems[i].Name, defaultName, StringComparison.OrdinalIgnoreCase))
                    continue;

                args.MenuItems.RemoveAt(i);
                args.MenuItems.Insert(i, replacement);
                return;
            }
        }

        public void Load(string filePath)
        {
            StopFileWatcher();

            _filePath = filePath;
            _encoding = DetectEncoding(filePath);
            _largeFileMode = new FileInfo(filePath).Length > LargeMarkdownBytes;
            var content = File.ReadAllText(filePath, _encoding);
            _lastLoadedContent = content;
            _pendingExternalReload = false;

            TitleChanged?.Invoke(this, Path.GetFileName(filePath));
            StartFileWatcher(filePath);

            if (!_webViewReady || !_pageReady)
            {
                _pendingContent = content;
                return;
            }

            var _ = CallAppOpen(content);
        }

        private async System.Threading.Tasks.Task CallAppOpen(string content)
        {
            if (!_webViewReady || !_pageReady)
            {
                _pendingContent = content;
                return;
            }

            _pendingContent = null;
            var script = string.Format(
                "app.open({0},{1},{2},{3})",
                JsonString(content),
                JsonString(ThemeName(_currentTheme)),
                JsonString(_largeFileMode ? "edit" : "preview"),
                _largeFileMode ? "true" : "false");
            await WebView.CoreWebView2.ExecuteScriptAsync(script);
        }

        private async System.Threading.Tasks.Task CallAppReloadContent(string content)
        {
            _pendingContent = null;
            if (!_webViewReady || !_pageReady)
            {
                _pendingContent = content;
                return;
            }

            var script = string.Format(
                "app.reloadContent({0},{1})",
                JsonString(content),
                _largeFileMode ? "true" : "false");
            await WebView.CoreWebView2.ExecuteScriptAsync(script);
        }

        private void Save(string content)
        {
            if (_filePath == null) return;
            try
            {
                var tmp = _filePath + ".tmp";
                File.WriteAllText(tmp, content, _encoding);
                File.Delete(_filePath);
                File.Move(tmp, _filePath);
                _lastLoadedContent = content;
                _pendingExternalReload = false;
                var _ = WebView.CoreWebView2.ExecuteScriptAsync("app.markSaved()");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "저장 오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void ApplyTheme(AppTheme theme)
        {
            _currentTheme = theme;
            if (!_webViewReady) return;
            var _ = WebView.CoreWebView2.ExecuteScriptAsync(
                string.Format("app.setTheme({0})", JsonString(ThemeName(theme))));
        }

        public bool HandleShortcut(KeyEventArgs e, KeyBindingService kb)
        {
            if (!kb.Matches(e, "ShowSearch"))
                return false;

            if (!_webViewReady || !_pageReady || WebView.CoreWebView2 == null)
                return false;

            WebView.Focus();
            WebView.CoreWebView2.ExecuteScriptAsync("app.openFind()");
            return true;
        }

        public void Export(ExportFormat format, string destPath)
        {
            switch (format)
            {
                case ExportFormat.Html:
                    var _ = WebView.CoreWebView2.ExecuteScriptAsync("exportHtml()");
                    break;
                case ExportFormat.Pdf:
                    ExportPdfAsync();
                    break;
            }
        }

        private void ExportHtmlContent(string html)
        {
            using (var dlg = new WinForms.SaveFileDialog())
            {
                dlg.Filter = "HTML 파일|*.html|모든 파일|*.*";
                dlg.DefaultExt = "html";
                dlg.FileName = Path.GetFileNameWithoutExtension(_filePath ?? "export");
                if (dlg.ShowDialog() != WinForms.DialogResult.OK) return;
                File.WriteAllText(dlg.FileName, html, new UTF8Encoding(true));
            }
        }

        private async void ExportPdfAsync()
        {
            using (var dlg = new WinForms.SaveFileDialog())
            {
                dlg.Filter = "PDF 파일|*.pdf|모든 파일|*.*";
                dlg.DefaultExt = "pdf";
                dlg.FileName = Path.GetFileNameWithoutExtension(_filePath ?? "export");
                if (dlg.ShowDialog() != WinForms.DialogResult.OK) return;
                try
                {
                    await WebView.CoreWebView2.PrintToPdfAsync(dlg.FileName);
                }
                finally
                {
                    await WebView.CoreWebView2.ExecuteScriptAsync(
                        "document.getElementById('operation-surface').innerHTML = ''");
                }
            }
        }

        private void HandleLinkClick(string uri, string href)
        {
            try
            {
                var link = string.IsNullOrWhiteSpace(href) ? uri : href;
                if (string.IsNullOrWhiteSpace(link) || link.StartsWith("#", StringComparison.Ordinal))
                    return;

                var path = ResolveLocalLinkPath(link);
                if (path != null)
                {
                    var ext = Path.GetExtension(path);
                    if ((ext.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
                         ext.Equals(".markdown", StringComparison.OrdinalIgnoreCase)) &&
                        File.Exists(path))
                    {
                        var handler = FileOpenRequested;
                        if (handler != null)
                            handler(this, path);
                        return;
                    }

                    if (File.Exists(path) || Directory.Exists(path))
                    {
                        System.Diagnostics.Process.Start(
                            new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                        return;
                    }
                }

                var u = new Uri(uri ?? link);
                if (u.Scheme == "http" || u.Scheme == "https")
                {
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
                }
            }
            catch { }
        }

        public bool IsModified => _modified;

        public void SetActive(bool isActive)
        {
            _isActive = isActive;
            if (_isActive && _pendingExternalReload)
                ScheduleExternalReload();
        }

        // ── Helpers ──────────────────────────────────────────────────────

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

                _pendingExternalReload = true;
                if (_isActive)
                    ScheduleExternalReload();
            }));
        }

        private async void OnFileReloadTimerTick(object sender, EventArgs e)
        {
            _fileReloadTimer.Stop();
            await ReloadExternalFileAsync();
        }

        private void ScheduleExternalReload()
        {
            _fileReloadTimer.Stop();
            _fileReloadTimer.Start();
        }

        private async System.Threading.Tasks.Task ReloadExternalFileAsync()
        {
            if (string.IsNullOrWhiteSpace(_filePath) || !File.Exists(_filePath))
                return;

            if (_modified)
                return;

            try
            {
                _encoding = DetectEncoding(_filePath);
                _largeFileMode = new FileInfo(_filePath).Length > LargeMarkdownBytes;
                var content = ReadAllTextAllowingWriters(_filePath, _encoding);
                if (string.Equals(content, _lastLoadedContent, StringComparison.Ordinal))
                {
                    _pendingExternalReload = false;
                    return;
                }

                _lastLoadedContent = content;
                _pendingExternalReload = false;
                _modified = false;
                ModifiedChanged?.Invoke(this, false);
                await CallAppReloadContent(content);
            }
            catch (IOException)
            {
                if (_isActive)
                    ScheduleExternalReload();
            }
            catch (UnauthorizedAccessException)
            {
                if (_isActive)
                    ScheduleExternalReload();
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
                WebView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                WebView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                WebView.CoreWebView2.ContextMenuRequested -= OnContextMenuRequested;
            }
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

        private static string ThemeName(AppTheme theme)
        {
            switch (theme)
            {
                case AppTheme.Light:      return "light";
                case AppTheme.Nord:       return "nord";
                case AppTheme.Catppuccin: return "catppuccin";
                case AppTheme.Solarized:  return "solarized";
                case AppTheme.Dracula:    return "dracula";
                case AppTheme.GitHub:     return "github";
                default:                  return "dark";
            }
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
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b");  break;
                    case '\f': sb.Append("\\f");  break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private string ResolveLocalLinkPath(string link)
        {
            var pathPart = StripFragmentAndQuery(link);
            if (string.IsNullOrWhiteSpace(pathPart))
                return null;

            var unescapedPath = Uri.UnescapeDataString(pathPart);
            if (Path.IsPathRooted(unescapedPath))
                return Path.GetFullPath(unescapedPath);

            Uri absoluteUri;
            if (Uri.TryCreate(pathPart, UriKind.Absolute, out absoluteUri))
            {
                if (absoluteUri.Scheme == "file")
                    return absoluteUri.LocalPath;
                return null;
            }

            pathPart = unescapedPath.Replace('/', Path.DirectorySeparatorChar);

            if (string.IsNullOrWhiteSpace(_filePath))
                return null;

            var baseDir = Path.GetDirectoryName(_filePath);
            if (string.IsNullOrWhiteSpace(baseDir))
                return null;

            return Path.GetFullPath(Path.Combine(baseDir, pathPart));
        }

        private static string StripFragmentAndQuery(string value)
        {
            var end = value.Length;
            var hash = value.IndexOf('#');
            if (hash >= 0 && hash < end)
                end = hash;
            var query = value.IndexOf('?');
            if (query >= 0 && query < end)
                end = query;
            return value.Substring(0, end);
        }

        // Minimal JSON string extractor — no external deps
        private static string ExtractJsonString(string json, string key)
        {
            var search = "\"" + key + "\":\"";
            var idx = json.IndexOf(search, StringComparison.Ordinal);
            if (idx < 0) return null;
            idx += search.Length;
            var sb = new StringBuilder();
            for (var i = idx; i < json.Length; i++)
            {
                var c = json[i];
                if (c == '"') break;
                if (c == '\\' && i + 1 < json.Length)
                {
                    i++;
                    switch (json[i])
                    {
                        case '"':  sb.Append('"');  break;
                        case '\\': sb.Append('\\'); break;
                        case 'n':  sb.Append('\n'); break;
                        case 'r':  sb.Append('\r'); break;
                        case 't':  sb.Append('\t'); break;
                        default:   sb.Append(json[i]); break;
                    }
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

    }
}

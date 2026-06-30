using Folderss.Services;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Folderss.Viewers
{
    public partial class MonacoViewer : UserControl, IFileViewer, IDisposable
    {
        private const long LargeFileBytes = 20L * 1024 * 1024;
        private const long HugeFileBytes = 50L * 1024 * 1024;

        private static readonly string ResourcesPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Viewers", "Resources");

        private static readonly Dictionary<string, string> ExtToLanguage =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { ".cs",    "csharp"     },
                { ".js",    "javascript" },
                { ".ts",    "typescript" },
                { ".json",  "json"       },
                { ".xml",   "xml"        },
                { ".xaml",  "xml"        },
                { ".html",  "html"       },
                { ".css",   "css"        },
                { ".py",    "python"     },
                { ".java",  "java"       },
                { ".cpp",   "cpp"        },
                { ".c",     "c"          },
                { ".h",     "cpp"        },
                { ".sh",    "shell"      },
                { ".bat",   "bat"        },
                { ".ps1",   "powershell" },
                { ".yaml",  "yaml"       },
                { ".yml",   "yaml"       },
                { ".sql",   "sql"        },
                { ".md",    "markdown"   },
                { ".log",   "plaintext"  },
                { ".txt",   "plaintext"  },
            };

        private AppTheme _currentTheme = AppTheme.Black;
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

        public UIElement View => this;
        public ViewerCapabilities Capabilities => ViewerCapabilities.Edit;

        public event EventHandler<string> TitleChanged;
        public event EventHandler<bool> ModifiedChanged;

        public MonacoViewer()
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
            WebView.CoreWebView2.NavigationStarting += OnNavigationStarting;
            WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            _webViewReady = true;
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
            }
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
            var language = _hugeFileMode ? "plaintext" : GetLanguage(filePath);

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
            _pendingContent = null;
            _pendingLanguage = null;

            var script = string.Format(
                "app.open({0},{1},{2},{3},{4},{5})",
                JsonString(content),
                JsonString(language),
                JsonString(ThemeName(_currentTheme)),
                _hugeFileMode ? "true" : "false",
                _largeFileMode ? "true" : "false",
                _hugeFileMode ? "true" : "false");
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

        public void Export(ExportFormat format, string destPath)
        {
        }

        public bool IsModified => _modified;

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
                var answer = MessageBox.Show(
                    message,
                    Path.GetFileName(_filePath),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.Yes);

                if (answer != MessageBoxResult.Yes)
                    return;

                _lastLoadedContent = content;
                _modified = false;
                ModifiedChanged?.Invoke(this, false);

                var language = _hugeFileMode ? "plaintext" : GetLanguage(_filePath);
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

        private static string GetLanguage(string filePath)
        {
            var ext = Path.GetExtension(filePath);
            string lang;
            return ExtToLanguage.TryGetValue(ext, out lang) ? lang : "plaintext";
        }

        private static string ThemeName(AppTheme theme)
        {
            switch (theme)
            {
                case AppTheme.Light: return "light";
                case AppTheme.GitHub: return "github";
                default: return "dark";
            }
        }

        private static string ExtractJsonString(string json, string key)
        {
            var pattern = "\"" + key + "\":";
            var i = json.IndexOf(pattern, StringComparison.Ordinal);
            if (i < 0) return null;
            i += pattern.Length;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i >= json.Length || json[i] != '"') return null;
            i++;
            var sb = new StringBuilder();
            while (i < json.Length)
            {
                var c = json[i++];
                if (c == '"') break;
                if (c == '\\' && i < json.Length)
                {
                    var e = json[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 <= json.Length)
                            {
                                var hex = json.Substring(i, 4);
                                sb.Append((char)Convert.ToInt32(hex, 16));
                                i += 4;
                            }
                            break;
                        default:
                            sb.Append(e);
                            break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
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

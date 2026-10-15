using Folderss.Services;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace Folderss.Viewers
{
    public partial class TextViewer : UserControl, IFileViewer, IDisposable
    {
        private static readonly string ResourcesPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Viewers", "Resources");

        private string _filePath;
        private Encoding _encoding;
        private AppTheme _currentTheme = AppTheme.Black;
        private bool _webViewReady;
        private string _pendingContent;
        private string _pendingLanguage;
        private bool _disposed;
        private WebViewIdleSaver _idleSaver;
        private string _pageTheme;

        public UIElement View => this;

        public ViewerCapabilities Capabilities => ViewerCapabilities.ReadOnly;

        public event EventHandler<string> TitleChanged;
        public event EventHandler<bool> ModifiedChanged;

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
                { ".sh",    "bash"       },
                { ".bat",   "bat"        },
                { ".ps1",   "powershell" },
                { ".yaml",  "yaml"       },
                { ".yml",   "yaml"       },
                { ".sql",   "sql"        },
                { ".md",    "markdown"   },
                { ".log",   "plaintext"  },
                { ".txt",   "plaintext"  },
            };

        public TextViewer()
        {
            InitializeComponent();
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

            _webViewReady = true;
            _idleSaver = new WebViewIdleSaver(this, WebView, OnBecameVisible);
            WebView.CoreWebView2.Navigate("https://folderss-viewer/text-app.html");
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

        public void Load(string filePath)
        {
            _filePath = filePath;
            _encoding = DetectEncoding(filePath);
            var content = File.ReadAllText(filePath, _encoding);
            var language = GetLanguage(filePath);

            var title = Path.GetFileName(filePath);
            TitleChanged?.Invoke(this, title);
            ModifiedChanged?.Invoke(this, false);

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
            _pageTheme = ThemeName(_currentTheme);

            var script = string.Format(
                "app.open({0},{1},{2},true)",
                JsonString(content), JsonString(language), JsonString(ThemeName(_currentTheme)));
            await WebView.CoreWebView2.ExecuteScriptAsync(script);
        }

        public void ApplyTheme(AppTheme theme)
        {
            _currentTheme = theme;
            if (_disposed) return;
            // 페이지가 그려지기 전(테마를 받기 전)에 보이는 바탕을 테마 색으로 맞춰 깜빡임을 없앤다.
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

        public void Dispose()
        {
            _disposed = true;
            _idleSaver?.Dispose();
            if (WebView.CoreWebView2 != null)
            {
                WebView.CoreWebView2.WebResourceRequested -= OnWebResourceRequested;
                WebView.CoreWebView2.NavigationStarting -= OnNavigationStarting;
                WebView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
            }

            // WPF WebView2는 화면에서 빠져도 스스로 정리되지 않아, 탭을 닫을 때마다 브라우저 프로세스와 메모리가 남는다.
            _webViewReady = false;
            WebView.Dispose();
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

        public void Export(ExportFormat format, string destPath)
        {
            // TextViewer is read-only; export not supported
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

        // text-app.html 테마 토큰 --bg와 같은 색
        private static System.Drawing.Color PageBackground(AppTheme theme)
        {
            switch (theme)
            {
                case AppTheme.Light:      return System.Drawing.ColorTranslator.FromHtml("#ffffff");
                case AppTheme.Nord:       return System.Drawing.ColorTranslator.FromHtml("#2e3440");
                case AppTheme.Catppuccin: return System.Drawing.ColorTranslator.FromHtml("#1e1e2e");
                case AppTheme.Solarized:  return System.Drawing.ColorTranslator.FromHtml("#002b36");
                case AppTheme.Dracula:    return System.Drawing.ColorTranslator.FromHtml("#282a36");
                case AppTheme.GitHub:     return System.Drawing.ColorTranslator.FromHtml("#ffffff");
                default:                  return System.Drawing.ColorTranslator.FromHtml("#0d1117");
            }
        }

        private static string ThemeName(AppTheme theme)
        {
            switch (theme)
            {
                case AppTheme.Light:     return "light";
                case AppTheme.Nord:      return "nord";
                case AppTheme.Catppuccin: return "catppuccin";
                case AppTheme.Solarized: return "solarized";
                case AppTheme.Dracula:   return "dracula";
                case AppTheme.GitHub:    return "github";
                default:                 return "dark";
            }
        }
    }
}

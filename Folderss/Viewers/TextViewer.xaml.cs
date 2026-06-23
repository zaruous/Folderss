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
    public partial class TextViewer : UserControl, IFileViewer
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

            _webViewReady = true;
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
                "app.open({0},{1},{2},true)",
                JsonString(content), JsonString(language), JsonString(ThemeName(_currentTheme)));
            await WebView.CoreWebView2.ExecuteScriptAsync(script);
        }

        public void ApplyTheme(AppTheme theme)
        {
            _currentTheme = theme;
            if (!_webViewReady) return;

            var _ = WebView.CoreWebView2.ExecuteScriptAsync(
                string.Format("app.setTheme({0})", JsonString(ThemeName(theme))));
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

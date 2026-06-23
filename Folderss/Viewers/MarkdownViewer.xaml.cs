using Folderss.Services;
using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using WinForms = System.Windows.Forms;

namespace Folderss.Viewers
{
    public partial class MarkdownViewer : UserControl, IFileViewer
    {
        private const long LargeMarkdownBytes = 5L * 1024 * 1024;

        private static readonly string ResourcesPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Viewers", "Resources");

        private string _filePath;
        private Encoding _encoding;
        private AppTheme _currentTheme = AppTheme.Black;
        private bool _webViewReady;
        private bool _modified;
        private bool _largeFileMode;

        private string _pendingContent;

        public UIElement View => this;
        public ViewerCapabilities Capabilities =>
            ViewerCapabilities.Edit | ViewerCapabilities.Export;

        public event EventHandler<string> TitleChanged;
        public event EventHandler<bool> ModifiedChanged;

        public MarkdownViewer()
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
            WebView.CoreWebView2.NavigationStarting   += OnNavigationStarting;
            WebView.CoreWebView2.WebMessageReceived   += OnWebMessageReceived;

            _webViewReady = true;
            WebView.CoreWebView2.Navigate("https://folderss-viewer/markdown-app.html");
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
                await CallAppOpen(_pendingContent);
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
                    if (uri != null)
                        HandleLinkClick(uri);
                    break;
            }
        }

        public void Load(string filePath)
        {
            _filePath = filePath;
            _encoding = DetectEncoding(filePath);
            _largeFileMode = new FileInfo(filePath).Length > LargeMarkdownBytes;
            var content = File.ReadAllText(filePath, _encoding);

            TitleChanged?.Invoke(this, Path.GetFileName(filePath));

            if (!_webViewReady)
            {
                _pendingContent = content;
                return;
            }

            var _ = CallAppOpen(content);
        }

        private async System.Threading.Tasks.Task CallAppOpen(string content)
        {
            _pendingContent = null;
            var script = string.Format(
                "app.open({0},{1},{2},{3})",
                JsonString(content),
                JsonString(ThemeName(_currentTheme)),
                JsonString(_largeFileMode ? "edit" : "preview"),
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
                await WebView.CoreWebView2.PrintToPdfAsync(dlg.FileName);
            }
        }

        private void HandleLinkClick(string uri)
        {
            try
            {
                var u = new Uri(uri);
                if (u.Scheme == "http" || u.Scheme == "https")
                {
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
                }
                else if (u.Scheme == "file")
                {
                    var path = u.LocalPath;
                    if (Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase) ||
                        Path.GetExtension(path).Equals(".markdown", StringComparison.OrdinalIgnoreCase))
                    {
                        // Raise to parent via TitleChanged trick is not clean; use postMessage roundtrip
                        // For now open in OS handler
                        System.Diagnostics.Process.Start(
                            new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                    }
                }
            }
            catch { }
        }

        public bool IsModified => _modified;

        // ── Helpers ──────────────────────────────────────────────────────

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

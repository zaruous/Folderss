using Folderss.Services;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        private const string DocAssetHost = "folderss-doc-asset";

        private static readonly string ResourcesPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Viewers", "Resources");

        private string _filePath;
        private Encoding _encoding;
        private ThemeInfo _currentTheme = ThemeManager.CurrentTheme;
        private bool _webViewReady;
        private bool _pageReady;
        private bool _modified;
        private bool _largeFileMode;
        private FileSystemWatcher _fileWatcher;
        private readonly DispatcherTimer _fileReloadTimer;
        private string _lastLoadedContent;
        private string _lastPromptedContent;
        private bool _reloadPromptOpen;
        private bool _disposed;
        private bool _isActive = true;
        private bool _pendingExternalReload;
        private CoreWebView2ContextMenuItem _printMenuItem;
        private CoreWebView2ContextMenuItem _saveAsMenuItem;
        private CoreWebView2ContextMenuItem _shareMenuItem;
        private WebViewIdleSaver _idleSaver;
        private string _pageTheme;

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
            WebView.CoreWebView2.NavigationStarting   += OnNavigationStarting;
            WebView.CoreWebView2.NavigationCompleted  += OnNavigationCompleted;
            WebView.CoreWebView2.WebMessageReceived   += OnWebMessageReceived;
            WebView.CoreWebView2.ContextMenuRequested += OnContextMenuRequested;
            WebView.CoreWebView2.ServerCertificateErrorDetected += OnServerCertificateErrorDetected;
            NetworkSettingsService.Changed += OnNetworkSettingsChanged;
            InitializeContentContextMenuItems();

            _webViewReady = true;
            _idleSaver = new WebViewIdleSaver(this, WebView, OnBecameVisible);
            WebView.CoreWebView2.Navigate("https://folderss-viewer/markdown-app.html");
        }

        private void OnWebResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            var uri = new Uri(e.Request.Uri);

            if (string.Equals(uri.Host, DocAssetHost, StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(uri.AbsolutePath, "/remote", StringComparison.OrdinalIgnoreCase))
                    ProxyRemoteImage(e, uri);
                else
                    e.Response = BuildDocAssetResponse(uri);
                return;
            }

            if (string.Equals(uri.Host, "folderss-viewer", StringComparison.OrdinalIgnoreCase))
                return;

            // 원격 https 이미지는 네트워크 설정이 허용하면 WebView2가 직접 받는다(인증서는 Windows 저장소로 검증).
            // 그 밖의 외부 요청(스크립트·스타일·연결 등)은 계속 막는다.
            if (e.ResourceContext == CoreWebView2WebResourceContext.Image &&
                RemoteContentPolicy.RouteImage(uri, NetworkSettingsService.Current) == RemoteImageRoute.Direct)
                return;

            e.Response = WebView.CoreWebView2.Environment.CreateWebResourceResponse(null, 403, "Blocked", "");
        }

        private const long MaxRemoteImageBytes = 20L * 1024 * 1024;

        private static readonly Lazy<HttpClient> RemoteImageClient = new Lazy<HttpClient>(() =>
        {
            var handler = new HttpClientHandler
            {
                // Windows 프록시 설정을 따르고, 인증 프록시에는 현재 사용자 자격 증명을 쓴다.
                DefaultProxyCredentials = CredentialCache.DefaultCredentials,
                // http에서 https로 넘어가는 경우: Windows 저장소 검증에 실패하면 네트워크 설정(localhost·오류 허용·내부 인증서)으로 다시 판정한다.
                ServerCertificateCustomValidationCallback = (request, certificate, chain, errors) =>
                    errors == SslPolicyErrors.None ||
                    RemoteContentPolicy.ShouldAllowCertificateError(request.RequestUri, certificate,
                        chain == null ? Enumerable.Empty<X509Certificate2>() : chain.ChainElements.Cast<X509ChainElement>().Select(el => el.Certificate),
                        NetworkSettingsService.Current)
            };
            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        });

        /// <summary>
        /// http 이미지는 뷰어 페이지(https)에서 혼합 콘텐츠로 막히므로, 네트워크 설정이 허용하면 Folderss가 대신 받아
        /// 같은 출처 주소(<c>folderss-doc-asset/remote</c>)로 전달한다. 이미지(image/*)만, 20MB까지.
        /// </summary>
        private async void ProxyRemoteImage(CoreWebView2WebResourceRequestedEventArgs e, Uri requestUri)
        {
            var env = WebView.CoreWebView2.Environment;
            var target = ExtractQueryValue(requestUri.Query, "u");
            Uri remote;
            if (target == null ||
                !Uri.TryCreate(Uri.UnescapeDataString(target), UriKind.Absolute, out remote) ||
                RemoteContentPolicy.RouteImage(remote, NetworkSettingsService.Current) != RemoteImageRoute.Proxy)
            {
                e.Response = env.CreateWebResourceResponse(null, 403, "Blocked", "");
                return;
            }

            var deferral = e.GetDeferral();
            CoreWebView2WebResourceResponse result;
            try
            {
                using (var response = await RemoteImageClient.Value.GetAsync(remote, HttpCompletionOption.ResponseHeadersRead))
                {
                    var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                    if (!response.IsSuccessStatusCode ||
                        !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
                        response.Content.Headers.ContentLength > MaxRemoteImageBytes)
                    {
                        result = env.CreateWebResourceResponse(null, 404, "Not Found", "");
                    }
                    else
                    {
                        var buffer = new MemoryStream();
                        using (var body = await response.Content.ReadAsStreamAsync())
                            await CopyWithLimitAsync(body, buffer, MaxRemoteImageBytes);
                        buffer.Position = 0;
                        result = env.CreateWebResourceResponse(buffer, 200, "OK",
                            "Content-Type: " + mediaType + "\r\nCache-Control: no-cache");
                    }
                }
            }
            catch (Exception)
            {
                result = _disposed ? null : env.CreateWebResourceResponse(null, 404, "Not Found", "");
            }

            try
            {
                if (!_disposed)
                    e.Response = result;
                deferral.Complete();
            }
            catch (Exception)
            {
                // 받는 사이 탭이 닫혀 WebView2가 해제됐으면 응답할 곳이 없다.
            }
        }

        private static async System.Threading.Tasks.Task CopyWithLimitAsync(Stream source, Stream destination, long limit)
        {
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total > limit)
                    throw new InvalidDataException("image too large");
                await destination.WriteAsync(buffer, 0, read);
            }
        }

        /// <summary>
        /// Windows 인증서 저장소로 검증에 실패한 원격 이미지 서버를 네트워크 설정(localhost·인증서 오류 허용·내부 인증서)으로 다시 판정한다.
        /// </summary>
        private void OnServerCertificateErrorDetected(object sender, CoreWebView2ServerCertificateErrorDetectedEventArgs e)
        {
            var allow = false;
            try
            {
                var uri = new Uri(e.RequestUri);
                var chain = e.ServerCertificate.PemEncodedIssuerCertificateChain
                    .Select(pem => X509Certificate2.CreateFromPem(pem)).ToList();
                try
                {
                    using (var server = X509Certificate2.CreateFromPem(e.ServerCertificate.ToPemEncoding()))
                        allow = RemoteContentPolicy.ShouldAllowCertificateError(uri, server, chain, NetworkSettingsService.Current);
                }
                finally
                {
                    foreach (var cert in chain)
                        cert.Dispose();
                }
            }
            catch (Exception)
            {
                allow = false;
            }
            e.Action = allow ? CoreWebView2ServerCertificateErrorAction.AlwaysAllow : CoreWebView2ServerCertificateErrorAction.Cancel;
        }

        // 인증서 허용은 WebView2가 실행 중 기억하므로(AlwaysAllow), 네트워크 설정이 바뀌면 지운다.
        private void OnNetworkSettingsChanged(object sender, EventArgs e)
        {
            if (_disposed || !_webViewReady)
                return;
            _ = WebView.CoreWebView2.ClearServerCertificateErrorActionsAsync();
        }

        /// <summary>
        /// 마크다운 본문의 상대(또는 로컬 절대) 이미지 경로 요청을 처리한다.
        /// 요청 쿼리의 원본 경로 문자열(p)을 그대로 <see cref="ResolveLocalLinkPath"/>에 넘겨
        /// 현재 열려 있는 파일 기준으로 해석하므로, 브라우저의 URL 정규화로 "../" 상위 이동이
        /// 뭉개지는 것을 피할 수 있다 (가상 호스트 폴더 매핑은 상위 폴더로 못 올라감).
        /// </summary>
        private CoreWebView2WebResourceResponse BuildDocAssetResponse(Uri requestUri)
        {
            var env = WebView.CoreWebView2.Environment;
            var encodedPath = ExtractQueryValue(requestUri.Query, "p");
            if (encodedPath == null)
                return env.CreateWebResourceResponse(null, 400, "Bad Request", "");

            try
            {
                var link = Uri.UnescapeDataString(encodedPath);
                var path = ResolveLocalLinkPath(link);
                if (path == null || !File.Exists(path))
                    return env.CreateWebResourceResponse(null, 404, "Not Found", "");

                var bytes = File.ReadAllBytes(path);
                var stream = new MemoryStream(bytes);
                return env.CreateWebResourceResponse(stream, 200, "OK",
                    "Content-Type: " + GetImageContentType(path) + "\r\nCache-Control: no-cache");
            }
            catch
            {
                return env.CreateWebResourceResponse(null, 404, "Not Found", "");
            }
        }

        private static string ExtractQueryValue(string query, string key)
        {
            if (string.IsNullOrEmpty(query))
                return null;

            foreach (var pair in query.TrimStart('?').Split('&'))
            {
                var idx = pair.IndexOf('=');
                if (idx < 0) continue;
                if (string.Equals(pair.Substring(0, idx), key, StringComparison.Ordinal))
                    return pair.Substring(idx + 1);
            }
            return null;
        }

        private static string GetImageContentType(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".png":  return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".gif":  return "image/gif";
                case ".bmp":  return "image/bmp";
                case ".webp": return "image/webp";
                case ".svg":  return "image/svg+xml";
                case ".ico":  return "image/x-icon";
                default:      return "application/octet-stream";
            }
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
            // 직접 문자열을 훑으면 \uXXXX 이스케이프 등을 놓쳐 저장 본문이 깨질 수 있으므로 JSON 파서로 읽는다.
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

                    case "export-html":
                        var html = GetJsonString(message, "content");
                        if (html != null)
                            ExportHtmlContent(html);
                        break;

                    case "export-pdf":
                        ExportPdfAsync();
                        break;

                    case "paste-image":
                        SavePastedImage(GetJsonString(message, "mime"), GetJsonString(message, "data"));
                        break;

                    case "open-link":
                        var uri = GetJsonString(message, "uri");
                        var href = GetJsonString(message, "href");
                        if (uri != null || href != null)
                            HandleLinkClick(uri, href);
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
            _lastPromptedContent = null;
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
            _pageTheme = ViewerTheme.ToJson(_currentTheme);
            var script = string.Format(
                "app.open({0},{1},{2},{3},{4})",
                JsonString(content),
                ViewerTheme.ToJson(_currentTheme),
                JsonString(_largeFileMode ? "edit" : "preview"),
                _largeFileMode ? "true" : "false",
                JsonString(_filePath));
            await WebView.CoreWebView2.ExecuteScriptAsync(script);
            await ConsumePendingAnchorAsync();
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
                // 원본을 먼저 지우고 옮기면 옮기기가 실패할 때 원본이 사라진다. 임시 파일에 쓴 뒤 덮어쓰기 이동으로 교체한다.
                SettingsFile.WriteAllText(_filePath, content, _encoding);
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

        public void ApplyTheme(ThemeInfo theme)
        {
            _currentTheme = theme;
            if (_disposed) return;
            // 페이지가 그려지기 전(테마를 받기 전)에 보이는 바탕을 테마 색으로 맞춰 흰색·검은색 깜빡임을 없앤다.
            WebView.DefaultBackgroundColor = ViewerTheme.Background(theme);
            // 가려진 탭은 다시 보일 때(OnBecameVisible) 적용한다. 탭마다 미리보기를 다시 그리지 않고, 일시 중지된 WebView에 스크립트를 보내지 않는다.
            if (!_webViewReady || !_pageReady || !IsVisible) return;
            SendTheme();
        }

        private void SendTheme()
        {
            _pageTheme = ViewerTheme.ToJson(_currentTheme);
            var _ = WebView.CoreWebView2.ExecuteScriptAsync(
                string.Format("app.setTheme({0})", _pageTheme));
        }

        // 탭이 다시 보일 때: 가려진 동안 바뀐 테마와, 다른 문서 링크로 넘어온 제목 이동을 적용한다.
        private void OnBecameVisible()
        {
            if (!_webViewReady || !_pageReady)
                return;
            if (_pageTheme != null && _pageTheme != ViewerTheme.ToJson(_currentTheme))
                SendTheme();
            _ = ConsumePendingAnchorAsync();
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
                File.WriteAllText(dlg.FileName, InlineDocAssets(html), new UTF8Encoding(true));
            }
        }

        private static readonly Regex DocAssetUrlRegex = new Regex(
            @"https://" + DocAssetHost + @"/resolve\?p=([^""'\s)&<>]*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// 미리보기의 로컬 이미지는 이 뷰어만 응답하는 <c>folderss-doc-asset</c> 주소를 쓰므로, 내보낸 HTML을 앱 밖에서 열면 깨진다.
        /// 내보낼 때 해당 이미지를 data URI로 넣어 파일 하나로 열리게 한다. 찾을 수 없는 이미지는 주소를 그대로 둔다.
        /// </summary>
        private static readonly Regex RemoteProxyUrlRegex = new Regex(
            @"https://" + DocAssetHost + @"/remote\?u=([^""'\s)&<>]*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private string InlineDocAssets(string html)
        {
            // 대신 받아 전달하던 http 이미지(folderss-doc-asset/remote)는 앱 밖에서 동작하지 않으므로 원래 주소로 되돌린다.
            html = RemoteProxyUrlRegex.Replace(html, match =>
                WebUtility.HtmlEncode(Uri.UnescapeDataString(match.Groups[1].Value)));
            return DocAssetUrlRegex.Replace(html, match =>
            {
                try
                {
                    var path = ResolveLocalLinkPath(Uri.UnescapeDataString(match.Groups[1].Value));
                    if (path == null || !File.Exists(path))
                        return match.Value;
                    return "data:" + GetImageContentType(path) + ";base64," + Convert.ToBase64String(File.ReadAllBytes(path));
                }
                catch (Exception)
                {
                    return match.Value;
                }
            });
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
                    // 기본 설정은 배경을 인쇄하지 않아 표 헤더/코드 블록 배경이 PDF에서 빠진다.
                    var printSettings = WebView.CoreWebView2.Environment.CreatePrintSettings();
                    printSettings.ShouldPrintBackgrounds = true;
                    await WebView.CoreWebView2.PrintToPdfAsync(dlg.FileName, printSettings);
                }
                finally
                {
                    // innerHTML 직접 비우기 대신 앱 API 사용 — 인쇄 보호 플래그(_printSurfaceReady)도 함께 초기화된다.
                    await WebView.CoreWebView2.ExecuteScriptAsync("app.clearOperationSurface()");
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
                        var fragment = ExtractFragment(link);
                        if (_filePath != null && string.Equals(path, Path.GetFullPath(_filePath), StringComparison.OrdinalIgnoreCase))
                        {
                            // 같은 문서를 가리키는 링크(this.md#제목)는 탭을 다시 열지 않고 제목으로만 이동한다.
                            if (fragment != null)
                                _ = ScrollToAnchorAsync(fragment);
                            return;
                        }

                        // 다른 문서의 제목(other.md#제목)은 그 문서를 연 뷰어가 열기·활성화 직후 꺼내 이동한다.
                        if (fragment != null)
                            PendingAnchors[path] = fragment;
                        var handler = FileOpenRequested;
                        if (handler != null)
                            handler(this, path);
                        return;
                    }

                    if (File.Exists(path) || Directory.Exists(path))
                    {
                        // 받은 문서의 링크 한 번 클릭으로 프로그램·스크립트가 실행되지 않도록 확인을 받는다.
                        if (File.Exists(path) && FileOperationService.IsExecutable(path) && !ConfirmRunExecutable(path))
                            return;

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

        /// <summary>
        /// 편집기에 붙여넣은 클립보드 이미지를 문서 옆 <c>images</c> 폴더에 "문서이름-날짜시각" 이름으로 저장하고
        /// 커서 위치에 상대 경로 이미지 링크를 넣는다.
        /// </summary>
        private async void SavePastedImage(string mime, string base64)
        {
            if (_filePath == null || string.IsNullOrEmpty(base64))
                return;

            string extension;
            switch (mime)
            {
                case "image/png":  extension = ".png"; break;
                case "image/jpeg": extension = ".jpg"; break;
                case "image/gif":  extension = ".gif"; break;
                case "image/webp": extension = ".webp"; break;
                case "image/bmp":  extension = ".bmp"; break;
                default: return;
            }

            string link;
            try
            {
                var bytes = Convert.FromBase64String(base64);
                var directory = Path.Combine(Path.GetDirectoryName(_filePath), "images");
                Directory.CreateDirectory(directory);

                var baseName = Path.GetFileNameWithoutExtension(_filePath) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var path = Path.Combine(directory, baseName + extension);
                for (var n = 1; File.Exists(path); n++)
                    path = Path.Combine(directory, baseName + "-" + n + extension);
                File.WriteAllBytes(path, bytes);

                // 공백·괄호가 있으면 마크다운 링크가 끊기므로 인코딩한다 (ResolveLocalLinkPath가 다시 푼다).
                var fileName = Path.GetFileName(path);
                var encoded = fileName.Replace("%", "%25").Replace(" ", "%20").Replace("(", "%28").Replace(")", "%29");
                link = "![" + Path.GetFileNameWithoutExtension(fileName) + "](images/" + encoded + ")";
            }
            catch (Exception ex)
            {
                var owner = Window.GetWindow(this);
                if (owner != null)
                    MessageBox.Show(owner, ex.Message, "이미지 붙여넣기 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                else
                    MessageBox.Show(ex.Message, "이미지 붙여넣기 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!_disposed && _pageReady)
                await WebView.CoreWebView2.ExecuteScriptAsync("app.insertText(" + JsonString(link) + ")");
        }

        private bool ConfirmRunExecutable(string path)
        {
            const string message = "링크가 실행 파일을 가리킵니다. 열면 프로그램이나 스크립트가 실행됩니다.\n\n{0}\n\n실행할까요?";
            var owner = Window.GetWindow(this);
            var answer = owner != null
                ? MessageBox.Show(owner, string.Format(message, path), "실행 확인",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
                : MessageBox.Show(string.Format(message, path), "실행 확인",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            return answer == MessageBoxResult.Yes;
        }

        public bool IsModified => _modified;

        public void SetActive(bool isActive)
        {
            _isActive = isActive;
            if (_isActive && _pendingExternalReload)
                ScheduleExternalReload();
            // 이미 열린 탭으로 다른 문서의 제목 링크가 들어오면 탭 활성화 때 이동한다.
            if (_isActive)
                _ = ConsumePendingAnchorAsync();
        }

        /// <summary>
        /// 다른 문서의 "파일#제목" 링크로 열 때 이동할 제목. 키는 대상 파일 전체 경로.
        /// 링크를 누른 뷰어가 넣고, 그 파일을 새로 열거나 이미 열린 탭을 활성화한 뷰어가 꺼낸다.
        /// </summary>
        private static readonly Dictionary<string, string> PendingAnchors =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private async System.Threading.Tasks.Task ConsumePendingAnchorAsync()
        {
            string fragment;
            // 아직 가려져 있으면(일시 중지됐을 수 있음) 꺼내지 않고 두었다가 보일 때(OnBecameVisible) 처리한다.
            if (_filePath == null || !_webViewReady || !_pageReady || _pendingContent != null || !IsVisible ||
                !PendingAnchors.TryGetValue(Path.GetFullPath(_filePath), out fragment))
                return;

            PendingAnchors.Remove(Path.GetFullPath(_filePath));
            await ScrollToAnchorAsync(fragment);
        }

        private async System.Threading.Tasks.Task ScrollToAnchorAsync(string fragment)
        {
            if (_disposed || !_webViewReady || !_pageReady)
                return;
            await WebView.CoreWebView2.ExecuteScriptAsync("app.scrollToAnchor(" + JsonString(fragment) + ")");
        }

        private static string ExtractFragment(string link)
        {
            var hash = link.IndexOf('#');
            return hash >= 0 && hash < link.Length - 1 ? link.Substring(hash + 1) : null;
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
            // 모달 확인창이 떠 있는 동안 워처가 다시 트리거되어도(중첩 메시지 펌프) 재진입해
            // 확인창이 중복으로 뜨지 않도록 가드.
            if (_reloadPromptOpen)
                return;

            if (string.IsNullOrWhiteSpace(_filePath) || !File.Exists(_filePath))
                return;

            try
            {
                _encoding = DetectEncoding(_filePath);
                _largeFileMode = new FileInfo(_filePath).Length > LargeMarkdownBytes;
                var content = ReadAllTextAllowingWriters(_filePath, _encoding);
                if (string.Equals(content, _lastLoadedContent, StringComparison.Ordinal))
                {
                    _pendingExternalReload = false;
                    _lastPromptedContent = null;
                    return;
                }

                // 이전에 같은 외부 변경 내용을 이미 물어봤고 사용자가 거부한 경우,
                // 파일이 다시 바뀌기 전까지는 동일한 확인창을 반복해서 띄우지 않는다.
                if (string.Equals(content, _lastPromptedContent, StringComparison.Ordinal))
                {
                    _pendingExternalReload = false;
                    return;
                }

                _reloadPromptOpen = true;
                try
                {
                    var message = _modified
                        ? "파일이 외부에서 변경되었습니다. 다시 읽으면 편집 중인 내용이 사라집니다.\n\n다시 읽으시겠습니까?"
                        : "파일이 외부에서 변경되었습니다. 다시 읽으시겠습니까?";
                    // owner 없이 띄우면 앱이 백그라운드일 때 확인창이 다른 창 뒤에 숨은 채
                    // _reloadPromptOpen 가드만 걸려서 이후 확인창이 전혀 안 뜨는 것처럼 보인다.
                    var owner = Window.GetWindow(this);
                    var answer = owner != null
                        ? MessageBox.Show(
                            owner,
                            message,
                            Path.GetFileName(_filePath),
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question,
                            MessageBoxResult.Yes)
                        : MessageBox.Show(
                            message,
                            Path.GetFileName(_filePath),
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question,
                            MessageBoxResult.Yes);

                    _pendingExternalReload = false;
                    _lastPromptedContent = content;
                    if (answer != MessageBoxResult.Yes)
                        return;

                    // 확인창이 떠 있는 동안(모달) 파일이 또 변경됐을 수 있으므로,
                    // 확인창을 띄우기 전에 읽었던 스냅샷 대신 지금 시점의 최신 내용을 다시 읽는다.
                    var latestContent = content;
                    try
                    {
                        _encoding = DetectEncoding(_filePath);
                        latestContent = ReadAllTextAllowingWriters(_filePath, _encoding);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }

                    _lastLoadedContent = latestContent;
                    _lastPromptedContent = null;
                    _modified = false;
                    ModifiedChanged?.Invoke(this, false);
                    await CallAppReloadContent(latestContent);
                }
                finally
                {
                    _reloadPromptOpen = false;
                }
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
                WebView.CoreWebView2.ServerCertificateErrorDetected -= OnServerCertificateErrorDetected;
            }

            // WPF WebView2는 화면에서 빠져도 스스로 정리되지 않아, 탭을 닫을 때마다 브라우저 프로세스와 메모리가 남는다.
            // 준비 플래그를 내려 이후 들어오는 ExecuteScriptAsync 호출(테마 변경 등)이 해제된 컨트롤을 건드리지 않게 한다.
            _webViewReady = false;
            _pageReady = false;
            _idleSaver?.Dispose();
            NetworkSettingsService.Changed -= OnNetworkSettingsChanged;
            WebView.Dispose();
        }

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>
        /// BOM이 있으면 그 인코딩, 없으면 UTF-8. 단 BOM 없는 파일이 UTF-8로 읽히지 않으면(한국어 Windows의 오래된 CP949 문서 등)
        /// 시스템 ANSI 코드 페이지로 읽는다. 저장도 읽은 인코딩 그대로 하므로 원래 인코딩이 유지된다.
        /// </summary>
        private static Encoding DetectEncoding(string filePath)
        {
            byte[] bytes;
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var ms = new MemoryStream())
            {
                fs.CopyTo(ms);
                bytes = ms.ToArray();
            }

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8;
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode;
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode;

            try
            {
                StrictUtf8.GetCharCount(bytes);
                return new UTF8Encoding(false);
            }
            catch (DecoderFallbackException)
            {
                return GitTextDecoder.Resolve(GitFallbackEncoding.SystemAnsi) ?? new UTF8Encoding(false);
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

    }
}

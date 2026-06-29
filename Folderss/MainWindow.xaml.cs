using Folderss.Controls;
using Folderss.Services;
using AvalonDock.Layout;
using AvalonDock.Layout.Serialization;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Xml;
using Forms = System.Windows.Forms;

namespace Folderss
{
    public partial class MainWindow : Window
    {
        private FolderBrowser _activePane;
        private SessionState _sessionState;
        private bool _openingPanelFromAddTab;
        private Forms.NotifyIcon _trayIcon;
        private bool _reallyClose;
        private bool _fixingAddPanelLayout;
        private bool _shownTrayBalloon;
        private bool _isCut;
        private HashSet<string> _cutPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Window _searchWindow;
        private Controls.SearchPanel _searchPanel;
        private Controls.ConsolePanel _consolePanel;
        private readonly KeyBindingService _keyBindingService = new KeyBindingService();
        private readonly ViewerConfigService _viewerConfigService = new ViewerConfigService();
        private bool _isPanelMaximized = false;
        private string _savedLayoutXml = null;
        private string _maximizedContentId = null;
        private object _maximizedContent = null;
        private bool _restoringPanelLayout;

        private FolderBrowser ActivePane
        {
            get { return _activePane ?? LeftPane; }
        }

        private FolderBrowser TargetPane
        {
            get
            {
                return GetFolderBrowsers().FirstOrDefault(pane => pane != ActivePane) ?? ActivePane;
            }
        }

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 구버전 콘솔 레이아웃(LayoutAnchorable 방식)만 정리한다.
            var layoutPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Folderss",
                "dock-layout.xml");
            if (File.Exists(layoutPath))
            {
                try
                {
                    var xml = File.ReadAllText(layoutPath);
                    if (Regex.IsMatch(xml, "<LayoutAnchorable\\b[^>]*\\bContentId=\"console\"[^>]*>"))
                    {
                        File.Delete(layoutPath);
                    }
                }
                catch { }
            }

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _sessionState = SessionStateService.Load();
            _keyBindingService.Load();

            AttachFolderBrowser(LeftPane);
            AttachFolderBrowser(RightPane);
            LeftPane.Initialize(GetExistingPath(_sessionState.LeftFolderPath, home));
            RightPane.Initialize(GetExistingPath(
                _sessionState.RightFolderPath,
                Directory.Exists(documents) ? documents : home));
            ActivatePane(LeftPane);
            UpdateThemeMenuChecks();

            var restored = DockLayoutService.Restore(DockManager, ResolveDockContent);
            if (!restored)
            {
                BuildDefaultDockLayout();
                RestoreAdditionalPanels(_sessionState.OpenFolderPaths);
                if (File.Exists(DockLayoutService.RestoreErrorPath))
                {
                    try
                    {
                        var restoreError = File.ReadAllText(DockLayoutService.RestoreErrorPath);
                        MessageBox.Show(
                            "저장된 레이아웃을 복원하지 못해 기본 레이아웃으로 시작했습니다.\n\n" + restoreError,
                            "레이아웃 복원 실패",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                    catch
                    {
                    }
                }
            }
            else
                AttachRestoredViewerDocuments();

            // 저장된 레이아웃에 console 탭이 포함된 경우에만 콘솔 패널 인스턴스를 주입한다.
            var consoleDoc = DockManager.Layout.Descendents()
                .OfType<LayoutDocument>()
                .FirstOrDefault(d => d.ContentId == "console");
            if (consoleDoc != null)
            {
                consoleDoc.Content = GetConsolePanel();
            }

            if (restored && DockLayoutService.RequiresLegacyConsoleMigration)
                MigrateLegacyConsoleTabLayout();

            EnsureAddPanelTab();
            DockManager.LayoutChanged += DockManager_LayoutChanged;

            var previousActive = GetFolderBrowsers().FirstOrDefault(
                pane => string.Equals(pane.CurrentPath, _sessionState.ActiveFolderPath, StringComparison.OrdinalIgnoreCase));
            if (previousActive != null)
                ActivatePane(previousActive);

            UpdateMaximizeButton();
            InitTrayIcon();
        }

        private void InitTrayIcon()
        {
            _trayIcon = new Forms.NotifyIcon { Text = "Folderss" };

            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"));
            if (resource != null)
                _trayIcon.Icon = new System.Drawing.Icon(resource.Stream);

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("열기", null, (s, e) => Dispatcher.Invoke(ShowFromTray));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("종료", null, (s, e) => Dispatcher.Invoke(ExitApp));
            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (s, e) => Dispatcher.Invoke(ShowFromTray);
            _trayIcon.Visible = true;
        }

        private void ShowFromTray()
        {
            Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
        }

        private void ExitApp()
        {
            _reallyClose = true;
            Close();
        }

        private static string GetExistingPath(string savedPath, string fallbackPath)
        {
            return !string.IsNullOrWhiteSpace(savedPath) && Directory.Exists(savedPath)
                ? savedPath
                : fallbackPath;
        }

        private void RestoreAdditionalPanels(IEnumerable<string> paths)
        {
            foreach (var path in paths ?? Enumerable.Empty<string>())
            {
                if (!Directory.Exists(path) ||
                    string.Equals(path, LeftPane.CurrentPath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(path, RightPane.CurrentPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AddFolderPanel(path);
            }
        }

        private void MainMenuButton_Click(object sender, RoutedEventArgs e)
        {
            PopulateOpenWithMenu();
            MainContextMenu.PlacementTarget = MainMenuButton;
            MainContextMenu.Placement = PlacementMode.Bottom;
            MainContextMenu.IsOpen = true;
        }

        private void PopulateOpenWithMenu()
        {
            var selected = ActivePane?.SelectedItems?.ToList();
            List<string> paths;
            if (selected != null && selected.Count > 0)
                paths = selected.Select(item => item.FullPath).ToList();
            else if (!string.IsNullOrEmpty(ActivePane?.CurrentPath))
                paths = new List<string> { ActivePane.CurrentPath };
            else
                paths = new List<string>();

            OpenWithMenuItem.Items.Clear();

            if (paths.Count == 0)
            {
                OpenWithSeparator.Visibility = System.Windows.Visibility.Collapsed;
                OpenWithMenuItem.Visibility = System.Windows.Visibility.Collapsed;
                return;
            }

            var entries = OpenWithService.GetMatchingEntries(paths);

            if (entries.Count == 0)
            {
                OpenWithSeparator.Visibility = System.Windows.Visibility.Collapsed;
                OpenWithMenuItem.Visibility = System.Windows.Visibility.Collapsed;
                return;
            }

            OpenWithSeparator.Visibility = System.Windows.Visibility.Visible;
            OpenWithMenuItem.Visibility = System.Windows.Visibility.Visible;

            var capturedPaths = paths.ToList();
            foreach (var entry in entries)
            {
                var captured = entry;
                var item = new System.Windows.Controls.MenuItem { Header = entry.Name };
                if (!string.IsNullOrWhiteSpace(entry.Description))
                    item.ToolTip = entry.Description;
                item.Click += (s, args) => OpenWithService.Launch(captured, capturedPaths);
                OpenWithMenuItem.Items.Add(item);
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximizeRestore();
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            if (WindowState == WindowState.Maximized)
            {
                var mousePosition = e.GetPosition(this);
                var horizontalRatio = mousePosition.X / ActualWidth;
                WindowState = WindowState.Normal;
                Left = mousePosition.X - (RestoreBounds.Width * horizontalRatio);
                Top = 0;
            }

            DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximizeRestore();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            UpdateMaximizeButton();
        }

        private void ToggleMaximizeRestore()
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void TogglePanelMaximize()
        {
            if (!_isPanelMaximized)
            {
                var activeDoc = DockManager.Layout.Descendents()
                    .OfType<LayoutDocument>()
                    .FirstOrDefault(d => d.IsActive);
                if (activeDoc == null) return;

                var sb = new StringBuilder();
                using (var xmlWriter = XmlWriter.Create(sb, new XmlWriterSettings { Indent = false }))
                {
                    new XmlLayoutSerializer(DockManager).Serialize(xmlWriter);
                }
                _savedLayoutXml = sb.ToString();
                _maximizedContentId = activeDoc.ContentId;
                _maximizedContent = activeDoc.Content;

                var content = activeDoc.Content;
                var title = activeDoc.Title;
                var contentId = activeDoc.ContentId;

                var newRoot = new LayoutRoot();
                var panel = new LayoutPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                var docPane = new LayoutDocumentPane();
                var doc = new LayoutDocument
                {
                    Title = title,
                    ContentId = contentId,
                    Content = content,
                    CanClose = false
                };
                docPane.Children.Add(doc);
                panel.Children.Add(docPane);
                newRoot.RootPanel = panel;

                _restoringPanelLayout = true;
                try
                {
                    DockManager.Layout = newRoot;
                }
                finally
                {
                    _restoringPanelLayout = false;
                }
                doc.IsActive = true;

                _isPanelMaximized = true;
            }
            else
            {
                var activeBrowser = _activePane;
                var maxContentId = _maximizedContentId;
                var maxContent = _maximizedContent;

                _restoringPanelLayout = true;
                try
                {
                    var serializer = new XmlLayoutSerializer(DockManager);
                    serializer.LayoutSerializationCallback += (sender, args) =>
                    {
                        if (maxContentId != null && args.Model.ContentId == maxContentId && maxContent != null)
                        {
                            args.Content = maxContent;
                            return;
                        }
                        var content = ResolveDockContent(args.Model.ContentId);
                        if (content != null)
                            args.Content = content;
                        else
                            args.Cancel = true;
                    };
                    using (var reader = XmlReader.Create(new StringReader(_savedLayoutXml)))
                    {
                        serializer.Deserialize(reader);
                    }

                    _savedLayoutXml = null;
                    _maximizedContentId = null;
                    _maximizedContent = null;
                    _isPanelMaximized = false;

                    EnsureAddPanelTab();

                    if (activeBrowser != null)
                        ActivatePane(activeBrowser);
                }
                finally
                {
                    _restoringPanelLayout = false;
                }

                var capturedContentId = maxContentId;
                var capturedBrowser = activeBrowser;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _restoringPanelLayout = true;
                    try
                    {
                        if (capturedContentId != null)
                        {
                            var docToActivate = DockManager.Layout.Descendents()
                                .OfType<LayoutDocument>()
                                .FirstOrDefault(d => d.ContentId == capturedContentId);
                            if (docToActivate != null)
                                docToActivate.IsActive = true;
                        }
                        if (capturedBrowser != null)
                            ActivatePane(capturedBrowser);
                    }
                    finally
                    {
                        _restoringPanelLayout = false;
                    }
                }), System.Windows.Threading.DispatcherPriority.ContextIdle);
            }
        }

        private void UpdateMaximizeButton()
        {
            if (MaximizeButton == null)
                return;

            MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
            MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "이전 크기로 복원" : "최대화";
        }

        private object ResolveDockContent(string contentId)
        {
            if (contentId == "favorites")
                return FavoritesPanel;
            if (contentId == "left-folder")
                return LeftPane;
            if (contentId == "right-folder")
                return RightPane;
            if (contentId == "console")
                return GetConsolePanel();
            if (contentId == "add-folder-panel")
                return new System.Windows.Controls.Grid();
            if (string.IsNullOrWhiteSpace(contentId))
                return null;

            const string viewerPrefix = "viewer|";
            if (contentId.StartsWith(viewerPrefix, StringComparison.Ordinal))
            {
                var filePath = Uri.UnescapeDataString(contentId.Substring(viewerPrefix.Length));
                if (!File.Exists(filePath))
                    return null;

                var viewerHost = CreateViewerHost(filePath, false);
                if (viewerHost == null)
                    return null;

                viewerHost.OpenFile(filePath);
                return viewerHost;
            }

            const string prefix = "folder-panel|";
            if (!contentId.StartsWith(prefix, StringComparison.Ordinal))
                return null;

            var separatorIndex = contentId.IndexOf('|', prefix.Length);
            if (separatorIndex < 0 || separatorIndex == contentId.Length - 1)
                return null;

            var path = Uri.UnescapeDataString(contentId.Substring(separatorIndex + 1));
            if (!Directory.Exists(path))
                return null;

            return CreateFolderBrowser(path);
        }

        private void AddFolderPanel_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new Forms.FolderBrowserDialog())
            {
                dialog.Description = "새 패널에서 열 폴더를 선택하세요.";
                dialog.ShowNewFolderButton = true;
                dialog.SelectedPath = Directory.Exists(ActivePane.CurrentPath)
                    ? ActivePane.CurrentPath
                    : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                if (dialog.ShowDialog() != Forms.DialogResult.OK)
                    return;

                AddFolderPanel(dialog.SelectedPath);
            }
        }

        private void AddFolderPanel(string path)
        {
            if (TryActivateExistingFolderDocument(path))
                return;

            var addDocument = GetAddPanelDocument();
            var pane = addDocument == null
                ? DockManager.Layout.Descendents().OfType<LayoutDocumentPane>().FirstOrDefault()
                : addDocument.Parent as LayoutDocumentPane;
            if (pane == null)
                return;

            var browser = CreateFolderBrowser(path);
            var title = new DirectoryInfo(path).Name;
            if (string.IsNullOrWhiteSpace(title))
                title = path;

            var document = new LayoutDocument
            {
                Title = title,
                ContentId = string.Format(
                    "folder-panel|{0}|{1}",
                    Guid.NewGuid().ToString("N"),
                    Uri.EscapeDataString(path)),
                Content = browser,
                CanClose = true
            };
            var insertIndex = addDocument == null ? pane.ChildrenCount : pane.IndexOfChild(addDocument);
            pane.InsertChildAt(insertIndex, document);
            document.IsActive = true;
            ActivatePane(browser);
        }

        private void EnsureAddPanelTab()
        {
            var addDocument = GetAddPanelDocument();
            if (addDocument == null)
            {
                var pane = DockManager.Layout.Descendents()
                    .OfType<LayoutDocumentPane>()
                    .FirstOrDefault();
                if (pane == null)
                    return;

                addDocument = new LayoutDocument
                {
                    Title = "＋",
                    ContentId = "add-folder-panel",
                    CanClose = false,
                    Content = new System.Windows.Controls.Grid()
                };
                pane.Children.Add(addDocument);
            }

            addDocument.IsActiveChanged -= AddPanelDocument_IsActiveChanged;
            addDocument.IsActiveChanged += AddPanelDocument_IsActiveChanged;
            addDocument.Title = "＋ 새 패널";
            MoveAddPanelTabToEnd();

            // If the add tab is already active after layout restore, IsActiveChanged
            // will not fire on the next click. Move focus back to a folder tab first.
            if (addDocument.IsActive && !_openingPanelFromAddTab && !_restoringPanelLayout)
            {
                var folderDoc = DockManager.Layout.Descendents()
                    .OfType<LayoutDocument>()
                    .FirstOrDefault(d => d.ContentId != "add-folder-panel" && d.Content is FolderBrowser);
                if (folderDoc != null)
                    Dispatcher.BeginInvoke(
                        new Action(() => folderDoc.IsActive = true),
                        DispatcherPriority.Background);
            }
        }

        private void DockManager_LayoutChanged(object sender, EventArgs e)
        {
            if (_fixingAddPanelLayout)
                return;

            var addDocFloating = DockManager.Layout.FloatingWindows
                .SelectMany(fw => fw.Descendents().OfType<LayoutDocument>())
                .FirstOrDefault(d => d.ContentId == "add-folder-panel");

            if (addDocFloating == null)
                return;

            _fixingAddPanelLayout = true;
            try
            {
                addDocFloating.Parent?.RemoveChild(addDocFloating);
                var targetPane = DockManager.Layout.Descendents()
                    .OfType<LayoutDocumentPane>()
                    .FirstOrDefault();
                targetPane?.Children.Add(addDocFloating);
                EnsureAddPanelTab();
            }
            finally
            {
                _fixingAddPanelLayout = false;
            }
        }

        private LayoutDocument GetAddPanelDocument()
        {
            return DockManager.Layout.Descendents()
                .OfType<LayoutDocument>()
                .FirstOrDefault(document => document.ContentId == "add-folder-panel");
        }

        private void MoveAddPanelTabToEnd()
        {
            var addDocument = GetAddPanelDocument();
            var pane = addDocument == null ? null : addDocument.Parent as LayoutDocumentPane;
            if (pane == null)
                return;

            var index = pane.IndexOfChild(addDocument);
            if (index < 0 || index == pane.ChildrenCount - 1)
                return;

            pane.RemoveChild(addDocument);
            pane.Children.Add(addDocument);
        }

        private void AddPanelDocument_IsActiveChanged(object sender, EventArgs e)
        {
            var addDocument = sender as LayoutDocument;
            if (addDocument == null || !addDocument.IsActive || _openingPanelFromAddTab || _restoringPanelLayout)
                return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                var fallbackDocument = DockManager.Layout.Descendents()
                    .OfType<LayoutDocument>()
                    .FirstOrDefault(d => d.ContentId != "add-folder-panel" && d.Content is FolderBrowser);
                if (fallbackDocument != null)
                    fallbackDocument.IsActive = true;
            }), DispatcherPriority.Background);
        }

        private FolderBrowser CreateFolderBrowser(string path)
        {
            var browser = new FolderBrowser();
            AttachFolderBrowser(browser);
            browser.Initialize(path);
            return browser;
        }

        private void AttachFolderBrowser(FolderBrowser browser)
        {
            browser.Activated -= Pane_Activated;
            browser.PathChanged -= FolderBrowser_PathChanged;
            browser.FileOpenRequested -= Browser_FileOpenRequested;
            browser.Activated += Pane_Activated;
            browser.PathChanged += FolderBrowser_PathChanged;
            browser.FileOpenRequested += Browser_FileOpenRequested;
        }

        private void Browser_FileOpenRequested(object sender, string filePath)
        {
            OpenViewerTab(filePath);
        }

        private void OpenViewerTab(string filePath)
        {
            if (TryActivateExistingViewerDocument(filePath))
                return;

            var viewerHost = CreateViewerHost(filePath, true);
            if (viewerHost == null)
            {
                try
                {
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "파일을 열 수 없습니다", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return;
            }

            var pane = DockManager.Layout.Descendents()
                .OfType<LayoutDocumentPane>().FirstOrDefault();
            if (pane == null)
            {
                viewerHost.Dispose();
                return;
            }

            var fileName = Path.GetFileName(filePath);
            var document = new LayoutDocument
            {
                Title = fileName,
                ContentId = string.Format("viewer|{0}", Uri.EscapeDataString(filePath)),
                Content = viewerHost,
                CanClose = true
            };
            AttachViewerDocument(document, viewerHost);
            var addDocument = GetAddPanelDocument();
            var insertIndex = addDocument != null && ReferenceEquals(addDocument.Parent, pane)
                ? pane.IndexOfChild(addDocument)
                : pane.ChildrenCount;
            if (insertIndex < 0)
                insertIndex = pane.ChildrenCount;

            pane.InsertChildAt(insertIndex, document);
            MoveAddPanelTabToEnd();
            document.IsActive = true;
            viewerHost.SetActive(document.IsActive);

            viewerHost.OpenFile(filePath);
        }

        private ViewerHost CreateViewerHost(string filePath, bool isActive)
        {
            var viewerHost = new ViewerHost(_viewerConfigService);
            if (!viewerHost.CanOpen(filePath))
                return null;

            viewerHost.SetActive(isActive);
            viewerHost.TitleChanged += (s, title) =>
            {
                var doc = DockManager.Layout.Descendents()
                    .OfType<LayoutDocument>()
                    .FirstOrDefault(d => ReferenceEquals(d.Content, s));
                if (doc != null)
                    doc.Title = title;
            };
            viewerHost.FileOpenRequested += (s, requestedPath) => OpenViewerTab(requestedPath);
            return viewerHost;
        }

        private void AttachViewerDocument(LayoutDocument document, ViewerHost viewerHost)
        {
            document.IsActiveChanged += (s, e) => viewerHost.SetActive(document.IsActive);
            document.Closed += (s, e) => DisposeDocumentContent(document);
        }

        private void AttachRestoredViewerDocuments()
        {
            foreach (var document in DockManager.Layout.Descendents().OfType<LayoutDocument>())
            {
                var viewerHost = document.Content as ViewerHost;
                if (viewerHost == null)
                    continue;

                AttachViewerDocument(document, viewerHost);
                viewerHost.SetActive(document.IsActive);
            }
        }

        private bool TryActivateExistingFolderDocument(string path)
        {
            var normalizedPath = NormalizeDirectoryPath(path);
            if (normalizedPath == null)
                return false;

            foreach (var document in GetAllLayoutDocuments())
            {
                var browser = document.Content as FolderBrowser;
                if (browser == null)
                    continue;

                var currentPath = NormalizeDirectoryPath(browser.CurrentPath);
                if (!string.Equals(currentPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                document.IsActive = true;
                ActivatePane(browser);
                return true;
            }

            return false;
        }

        private bool TryActivateExistingViewerDocument(string filePath)
        {
            var normalizedPath = NormalizeFilePath(filePath);
            if (normalizedPath == null)
                return false;

            foreach (var document in GetAllLayoutDocuments())
            {
                var existingPath = GetViewerPathFromContentId(document.ContentId);
                if (existingPath == null)
                    continue;

                if (!string.Equals(NormalizeFilePath(existingPath), normalizedPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                document.IsActive = true;
                var viewerHost = document.Content as ViewerHost;
                if (viewerHost != null)
                    viewerHost.SetActive(true);
                return true;
            }

            return false;
        }

        private IEnumerable<LayoutDocument> GetAllLayoutDocuments()
        {
            var docked = DockManager.Layout.Descendents().OfType<LayoutDocument>();
            var floating = DockManager.Layout.FloatingWindows
                .SelectMany(window => window.Descendents().OfType<LayoutDocument>());
            return docked.Concat(floating).Distinct();
        }

        private static string GetViewerPathFromContentId(string contentId)
        {
            const string prefix = "viewer|";
            if (string.IsNullOrWhiteSpace(contentId) || !contentId.StartsWith(prefix, StringComparison.Ordinal))
                return null;

            return Uri.UnescapeDataString(contentId.Substring(prefix.Length));
        }

        private static string NormalizeFilePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeDirectoryPath(string path)
        {
            var normalizedPath = NormalizeFilePath(path);
            if (normalizedPath == null)
                return null;

            return normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private void FolderBrowser_PathChanged(object sender, EventArgs e)
        {
            var browser = sender as FolderBrowser;
            if (browser == null)
                return;

            var layoutContent = DockManager.Layout.Descendents()
                .OfType<LayoutContent>()
                .FirstOrDefault(content => ReferenceEquals(content.Content, browser));
            if (layoutContent == null)
                return;

            var title = new DirectoryInfo(browser.CurrentPath).Name;
            layoutContent.Title = string.IsNullOrWhiteSpace(title) ? browser.CurrentPath : title;

            if (layoutContent.ContentId != "left-folder" && layoutContent.ContentId != "right-folder")
            {
                var id = ExtractPanelId(layoutContent.ContentId);
                layoutContent.ContentId = string.Format(
                    "folder-panel|{0}|{1}",
                    id,
                    Uri.EscapeDataString(browser.CurrentPath));
            }

            if (ReferenceEquals(browser, ActivePane))
                UpdateActivePaneText();
        }

        private static string ExtractPanelId(string contentId)
        {
            const string prefix = "folder-panel|";
            if (!string.IsNullOrWhiteSpace(contentId) && contentId.StartsWith(prefix, StringComparison.Ordinal))
            {
                var separator = contentId.IndexOf('|', prefix.Length);
                if (separator > prefix.Length)
                    return contentId.Substring(prefix.Length, separator - prefix.Length);
            }

            return Guid.NewGuid().ToString("N");
        }

        private IList<FolderBrowser> GetFolderBrowsers()
        {
            return DockManager.Layout.Descendents()
                .OfType<LayoutContent>()
                .Select(content => content.Content as FolderBrowser)
                .Where(browser => browser != null)
                .Distinct()
                .ToList();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (!_reallyClose)
            {
                SaveCurrentLayoutState();
                e.Cancel = true;
                Hide();
                if (!_shownTrayBalloon)
                {
                    _shownTrayBalloon = true;
                    _trayIcon?.ShowBalloonTip(
                        3000,
                        "Folderss",
                        "트레이에서 계속 실행 중입니다. 아이콘을 더블클릭하면 창이 열립니다.",
                        Forms.ToolTipIcon.Info);
                }
                return;
            }

            // If the window is hidden, the visible layout was already saved before Hide().
            // Serializing the unloaded visual state again can overwrite that last good layout.
            if (IsVisible)
                SaveCurrentLayoutState();
            DisposeAllDisposableDocumentContent();

            _trayIcon?.Dispose();
            _trayIcon = null;
        }

        private void SaveSessionState()
        {
            try
            {
                var browsers = GetFolderBrowsers();
                SessionStateService.Save(new SessionState
                {
                    LeftFolderPath = LeftPane.CurrentPath,
                    RightFolderPath = RightPane.CurrentPath,
                    ActiveFolderPath = ActivePane.CurrentPath,
                    OpenFolderPaths = browsers
                        .Select(browser => browser.CurrentPath)
                        .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                });
            }
            catch
            {
                // 세션 저장 실패는 앱 종료를 막지 않는다.
            }
        }

        private void SaveCurrentLayoutState()
        {
            SaveSessionState();

            try
            {
                DockLayoutService.Save(DockManager);
            }
            catch
            {
                // 배치 저장 실패는 UI 흐름을 막지 않는다.
            }
        }

        private void FavoritesPanel_AddCurrentRequested(object sender, EventArgs e)
        {
            if (FavoritesPanel.AddFavorite(ActivePane.CurrentPath))
            {
                MessageBox.Show(
                    "현재 폴더를 즐겨찾기에 추가했습니다.",
                    "Folderss",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void FavoritesPanel_NavigateRequested(object sender, FavoriteNavigateEventArgs e)
        {
            ActivePane.NavigateTo(e.Path);
        }

        private void ShowFavorites_Click(object sender, RoutedEventArgs e)
        {
            var dock = FindDock("favorites") ?? CreateFavoritesDock();
            dock?.Show();
        }

        private void ResetDockLayout_Click(object sender, RoutedEventArgs e)
        {
            DockLayoutService.Reset();
            BuildDefaultDockLayout();
            EnsureAddPanelTab();
            ActivatePane(LeftPane);
            MessageBox.Show(
                "도킹 배치가 기본 구성으로 초기화되었습니다.",
                "Folderss",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void BlackTheme_Click(object sender, RoutedEventArgs e)
        {
            ApplyTheme(AppTheme.Black);
        }

        private void LightTheme_Click(object sender, RoutedEventArgs e)
        {
            ApplyTheme(AppTheme.Light);
        }

        private void NordTheme_Click(object sender, RoutedEventArgs e)
        {
            ApplyTheme(AppTheme.Nord);
        }

        private void CatppuccinTheme_Click(object sender, RoutedEventArgs e)
        {
            ApplyTheme(AppTheme.Catppuccin);
        }

        private void SolarizedTheme_Click(object sender, RoutedEventArgs e)
        {
            ApplyTheme(AppTheme.Solarized);
        }

        private void DraculaTheme_Click(object sender, RoutedEventArgs e)
        {
            ApplyTheme(AppTheme.Dracula);
        }

        private void GitHubTheme_Click(object sender, RoutedEventArgs e)
        {
            ApplyTheme(AppTheme.GitHub);
        }

        private void ApplyTheme(AppTheme theme)
        {
            ThemeManager.ApplyTheme(theme);
            foreach (var pane in GetFolderBrowsers())
                pane.SetActive(ActivePane == pane);
            UpdateThemeMenuChecks();
        }

        private void UpdateThemeMenuChecks()
        {
            BlackThemeMenuItem.IsChecked       = ThemeManager.CurrentTheme == AppTheme.Black;
            LightThemeMenuItem.IsChecked       = ThemeManager.CurrentTheme == AppTheme.Light;
            NordThemeMenuItem.IsChecked        = ThemeManager.CurrentTheme == AppTheme.Nord;
            CatppuccinThemeMenuItem.IsChecked  = ThemeManager.CurrentTheme == AppTheme.Catppuccin;
            SolarizedThemeMenuItem.IsChecked   = ThemeManager.CurrentTheme == AppTheme.Solarized;
            DraculaThemeMenuItem.IsChecked     = ThemeManager.CurrentTheme == AppTheme.Dracula;
            GitHubThemeMenuItem.IsChecked      = ThemeManager.CurrentTheme == AppTheme.GitHub;
        }

        private void ActivatePane(FolderBrowser pane)
        {
            _activePane = pane;
            foreach (var folderPane in GetFolderBrowsers())
                folderPane.SetActive(pane == folderPane);
            UpdateActivePaneText();
        }

        private void UpdateActivePaneText()
        {
            ActivePaneText.Text = string.Format("활성 패널: {0}", ActivePane.CurrentPath ?? string.Empty);
        }

        private void Pane_Activated(object sender, EventArgs e)
        {
            if (_restoringPanelLayout)
                return;
            var pane = sender as FolderBrowser;
            if (pane != null)
                ActivatePane(pane);
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            ExecuteTransfer(false);
        }

        private void Move_Click(object sender, RoutedEventArgs e)
        {
            ExecuteTransfer(true);
        }

        private void ExecuteTransfer(bool move)
        {
            var selected = ActivePane.SelectedItems.ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("복사하거나 이동할 항목을 선택하세요.", "Folderss", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(TargetPane.CurrentPath) || !Directory.Exists(TargetPane.CurrentPath))
                return;

            var verb = move ? "이동" : "복사";
            var answer = MessageBox.Show(
                string.Format("{0}개 항목을 다음 폴더로 {1}하시겠습니까?\n\n{2}", selected.Count, verb, TargetPane.CurrentPath),
                verb,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
                return;

            var errors = new List<string>();
            foreach (var item in selected)
            {
                try
                {
                    var sourceParent = Path.GetDirectoryName(item.FullPath);
                    if (move && string.Equals(sourceParent, TargetPane.CurrentPath, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("원본과 대상 폴더가 같습니다.");

                    if (move)
                        FileOperationService.Move(item.FullPath, TargetPane.CurrentPath);
                    else
                        FileOperationService.Copy(item.FullPath, TargetPane.CurrentPath);
                }
                catch (Exception exception)
                {
                    errors.Add(item.Name + ": " + exception.Message);
                }
            }

            RefreshBothPanes();
            ShowErrorsIfAny(verb, errors);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelected(false);
        }

        private void DeleteSelected(bool permanently)
        {
            var selected = ActivePane.SelectedItems.ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("삭제할 항목을 선택하세요.", "Folderss", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var message = permanently
                ? string.Format("{0}개 항목을 영구 삭제하시겠습니까?\n이 작업은 되돌릴 수 없습니다.", selected.Count)
                : string.Format("{0}개 항목을 휴지통으로 보내시겠습니까?", selected.Count);
            var answer = MessageBox.Show(
                message,
                permanently ? "영구 삭제 확인" : "휴지통으로 이동",
                MessageBoxButton.YesNo,
                permanently ? MessageBoxImage.Warning : MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
                return;

            var errors = new List<string>();
            foreach (var item in selected)
            {
                try
                {
                    if (permanently)
                        FileOperationService.DeletePermanently(item.FullPath);
                    else
                        FileOperationService.MoveToRecycleBin(item.FullPath);
                }
                catch (Exception exception)
                {
                    errors.Add(item.Name + ": " + exception.Message);
                }
            }

            RefreshBothPanes();
            ShowErrorsIfAny(permanently ? "영구 삭제" : "휴지통으로 이동", errors);
        }

        private void Rename_Click(object sender, RoutedEventArgs e)
        {
            var item = ActivePane.SelectedItem;
            if (item == null)
            {
                MessageBox.Show("이름을 바꿀 항목을 하나 선택하세요.", "Folderss", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var prompt = new PromptWindow("이름 변경", "새 이름을 입력하세요.", item.Name) { Owner = this };
            if (prompt.ShowDialog() != true || string.Equals(prompt.Value.Trim(), item.Name, StringComparison.Ordinal))
                return;

            try
            {
                FileOperationService.Rename(item.FullPath, prompt.Value);
                RefreshBothPanes();
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "이름을 변경할 수 없습니다", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void NewFolder_Click(object sender, RoutedEventArgs e)
        {
            var prompt = new PromptWindow("새 폴더", "폴더 이름을 입력하세요.", "새 폴더") { Owner = this };
            if (prompt.ShowDialog() != true)
                return;

            try
            {
                FileOperationService.CreateDirectory(ActivePane.CurrentPath, prompt.Value);
                ActivePane.RefreshItems();
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "폴더를 만들 수 없습니다", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void NewFile_Click(object sender, RoutedEventArgs e)
        {
            var prompt = new PromptWindow("새 파일", "파일 이름을 입력하세요.", "새 파일.txt") { Owner = this };
            if (prompt.ShowDialog() != true)
                return;

            try
            {
                FileOperationService.CreateFile(ActivePane.CurrentPath, prompt.Value);
                ActivePane.RefreshItems();
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "파일을 만들 수 없습니다", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshBothPanes();
        }

        private void RefreshBothPanes()
        {
            foreach (var pane in GetFolderBrowsers())
                pane.RefreshItems();
            UpdateActivePaneText();
        }

        private static void ShowErrorsIfAny(string operation, IList<string> errors)
        {
            if (errors.Count == 0)
                return;

            var details = string.Join(Environment.NewLine, errors.Take(8));
            if (errors.Count > 8)
                details += Environment.NewLine + string.Format("외 {0}개", errors.Count - 8);

            MessageBox.Show(details, operation + " 중 일부 항목 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var kb = _keyBindingService;

            if (TryHandleActiveViewerShortcut(e))
            {
                e.Handled = true;
                return;
            }

            if (kb.Matches(e, "Rename"))
            {
                if (FavoritesPanel.IsKeyboardFocusWithin)
                    FavoritesPanel.RenameSelected();
                else
                    Rename_Click(sender, e);
                e.Handled = true;
            }
            else if (kb.Matches(e, "Refresh"))
            {
                RefreshBothPanes();
                e.Handled = true;
            }
            else if (kb.Matches(e, "Move"))
            {
                Move_Click(sender, e);
                e.Handled = true;
            }
            else if (kb.Matches(e, "Delete", ModifierKeys.Shift))
            {
                if (Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;
                DeleteSelected((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift);
                e.Handled = true;
            }
            else if (kb.Matches(e, "RefreshAlt"))
            {
                RefreshBothPanes();
                e.Handled = true;
            }
            else if (kb.Matches(e, "NewFolder"))
            {
                NewFolder_Click(sender, e);
                e.Handled = true;
            }
            else if (kb.Matches(e, "NewFile"))
            {
                NewFile_Click(sender, e);
                e.Handled = true;
            }
            else if (kb.Matches(e, "AddPanel"))
            {
                AddFolderPanel_Click(sender, e);
                e.Handled = true;
            }
            else if (kb.Matches(e, "CopyClipboard"))
            {
                if (FavoritesPanel.IsKeyboardFocusWithin)
                {
                    FavoritesPanel.CopySelectedFavoritePath();
                }
                else if (ActivePane.SelectedItems.Count > 0)
                {
                    ActivePane.CopySelectedToClipboard();
                }
                e.Handled = true;
            }
            else if (kb.Matches(e, "CutClipboard"))
            {
                if (ActivePane.SelectedItems.Count > 0)
                    ActivePane.CutSelectedToClipboard();
                e.Handled = true;
            }
            else if (kb.Matches(e, "PasteClipboard"))
            {
                PasteFromClipboard();
                e.Handled = true;
            }
            else if (kb.Matches(e, "NavigateBack"))
            {
                ActivePane.NavigateBack();
                e.Handled = true;
            }
            else if (kb.Matches(e, "NavigateForward"))
            {
                ActivePane.NavigateForward();
                e.Handled = true;
            }
            else if (kb.Matches(e, "NavigateUp"))
            {
                ActivePane.NavigateUp();
                e.Handled = true;
            }
            else if (kb.Matches(e, "SwitchPaneLeft"))
            {
                SwitchToAdjacentPane(-1);
                e.Handled = true;
            }
            else if (kb.Matches(e, "SwitchPaneRight"))
            {
                SwitchToAdjacentPane(1);
                e.Handled = true;
            }
            else if (kb.Matches(e, "ShowSearch"))
            {
                ShowSearchPanel();
                e.Handled = true;
            }
            else if (kb.Matches(e, "PanelMaximize"))
            {
                TogglePanelMaximize();
                e.Handled = true;
            }
        }

        private bool TryHandleActiveViewerShortcut(KeyEventArgs e)
        {
            var activeDocument = DockManager.Layout.Descendents()
                .OfType<LayoutDocument>()
                .FirstOrDefault(document => document.IsActive);
            var viewerHost = activeDocument?.Content as ViewerHost;
            return viewerHost != null && viewerHost.HandleShortcut(e, _keyBindingService);
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var win = new SettingsWindow(_keyBindingService, _viewerConfigService) { Owner = this };
            win.ShowDialog();
        }

        private LayoutAnchorable FindDock(string contentId)
        {
            // 도킹된 상태
            var docked = DockManager.Layout.Descendents()
                .OfType<LayoutAnchorable>()
                .FirstOrDefault(a => a.ContentId == contentId);
            if (docked != null)
                return docked;

            // Hide()로 숨겨진 상태
            var hidden = DockManager.Layout.Hidden
                .OfType<LayoutAnchorable>()
                .FirstOrDefault(a => a.ContentId == contentId);
            if (hidden != null)
                return hidden;

            // 플로팅 윈도우 상태
            return DockManager.Layout.FloatingWindows
                .SelectMany(fw => fw.Descendents().OfType<LayoutAnchorable>())
                .FirstOrDefault(a => a.ContentId == contentId);
        }

        private void ShowSearch_Click(object sender, RoutedEventArgs e)
        {
            ShowSearchPanel();
        }

        private void ShowSearchPanel()
        {
            if (_searchPanel == null)
            {
                _searchPanel = new Controls.SearchPanel();
                _searchPanel.NavigateRequested += (s, e) => ActivePane.SelectAndScrollTo(e.Path);
                _searchPanel.HideRequested += (s, e) => _searchWindow?.Hide();
            }

            if (_searchWindow == null || !_searchWindow.IsLoaded)
            {
                _searchWindow = new Window
                {
                    Title = "파일 내용 검색",
                    Width = 900,
                    Height = 420,
                    MinWidth = 500,
                    MinHeight = 250,
                    Content = _searchPanel,
                    Owner = this,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow
                };
                _searchWindow.Closing += (s, e) => { e.Cancel = true; _searchWindow.Hide(); };
            }

            if (_searchWindow.IsVisible)
            {
                _searchWindow.Hide();
                return;
            }

            _searchPanel.SetRootPath(ActivePane.CurrentPath);
            _searchWindow.Show();
            Dispatcher.BeginInvoke(new Action(() => _searchPanel.FocusSearchBox()), DispatcherPriority.Input);
        }

        private void SwitchToAdjacentPane(int direction)
        {
            var browsers = GetFolderBrowsers();
            if (browsers.Count <= 1)
                return;

            var currentIndex = browsers.IndexOf(ActivePane);
            if (currentIndex < 0)
                return;

            var nextIndex = (currentIndex + direction + browsers.Count) % browsers.Count;
            var nextPane = browsers[nextIndex];

            var layoutContent = DockManager.Layout.Descendents()
                .OfType<LayoutContent>()
                .FirstOrDefault(c => ReferenceEquals(c.Content, nextPane));
            if (layoutContent != null)
                layoutContent.IsActive = true;

            // AvalonDock 레이아웃 업데이트 후 포커스를 이동해야 덮어쓰지 않는다.
            Dispatcher.BeginInvoke(new Action(() => nextPane.FocusFileList()), DispatcherPriority.Input);
        }

        public void ClearCutStateFromClipboard()
        {
            ClearCutState();
        }

        public void SetCutStateFromClipboard(IEnumerable<string> paths)
        {
            _isCut = true;
            _cutPaths = new HashSet<string>(paths ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            foreach (var pane in GetFolderBrowsers())
            {
                pane.CutPaths = _cutPaths;
                pane.RefreshItems();
            }
        }

        public bool TryPasteFromClipboardInto(FolderBrowser targetPane)
        {
            if (!Clipboard.ContainsFileDropList())
                return false;

            var files = Clipboard.GetFileDropList();
            if (files.Count == 0)
                return false;

            var targetPath = targetPane?.CurrentPath;
            if (string.IsNullOrWhiteSpace(targetPath) || !Directory.Exists(targetPath))
                return false;

            bool isCut = _isCut;
            var errors = new List<string>();
            foreach (string source in files)
            {
                try
                {
                    if (isCut)
                        FileOperationService.Move(source, targetPath);
                    else
                        FileOperationService.Copy(source, targetPath);
                }
                catch (Exception exception)
                {
                    errors.Add(Path.GetFileName(source) + ": " + exception.Message);
                }
            }

            if (isCut)
            {
                _isCut = false;
                _cutPaths.Clear();
                foreach (var pane in GetFolderBrowsers())
                {
                    pane.CutPaths = null;
                    pane.RefreshItems();
                }
            }
            else
            {
                targetPane.RefreshItems();
            }

            ShowErrorsIfAny("붙여넣기", errors);
            return true;
        }

        private void CopyToClipboard()
        {
            var selected = ActivePane.SelectedItems.ToList();
            if (selected.Count == 0)
                return;

            var paths = new System.Collections.Specialized.StringCollection();
            foreach (var item in selected)
                paths.Add(item.FullPath);

            Clipboard.SetFileDropList(paths);
            ClearCutState();
        }

        private void CutToClipboard()
        {
            var selected = ActivePane.SelectedItems.ToList();
            if (selected.Count == 0)
                return;

            var paths = new System.Collections.Specialized.StringCollection();
            foreach (var item in selected)
                paths.Add(item.FullPath);

            Clipboard.SetFileDropList(paths);

            _isCut = true;
            _cutPaths = new HashSet<string>(selected.Select(i => i.FullPath), StringComparer.OrdinalIgnoreCase);

            foreach (var pane in GetFolderBrowsers())
            {
                pane.CutPaths = _cutPaths;
                pane.RefreshItems();
            }
        }

        private void ClearCutState()
        {
            if (!_isCut)
                return;

            _isCut = false;
            _cutPaths.Clear();

            foreach (var pane in GetFolderBrowsers())
            {
                pane.CutPaths = null;
                pane.RefreshItems();
            }
        }

        private void PasteFromClipboard()
        {
            TryPasteFromClipboardInto(ActivePane);
        }

        private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            var info = await Services.UpdateService.CheckAsync();
            if (info == null)
            {
                MessageBox.Show(
                    "현재 최신 버전입니다.",
                    "업데이트 확인",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (info.DownloadUrl == null)
            {
                // 인스톨러 asset 없음 → 브라우저로 릴리즈 페이지 열기
                var result = MessageBox.Show(
                    string.Format("새 버전 {0}이(가) 있습니다.\n다운로드 페이지를 여시겠습니까?", info.TagName),
                    "업데이트 확인",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);
                if (result == MessageBoxResult.Yes)
                    OpenUrl(info.HtmlUrl);
                return;
            }

            // 인스톨러 asset 있음 → 자동 설치 제안
            var install = MessageBox.Show(
                string.Format("새 버전 {0}이(가) 있습니다.\n지금 설치하시겠습니까?", info.TagName),
                "업데이트 확인",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (install != MessageBoxResult.Yes)
                return;

            await DownloadAndInstallAsync(info);
        }

        private async Task DownloadAndInstallAsync(Services.UpdateService.UpdateInfo info)
        {
            var ext = System.IO.Path.GetExtension(info.DownloadUrl);
            var tempPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "Folderss-update",
                string.Format("Folderss-{0}{1}", info.TagName, ext));

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(tempPath));

            // 다운로드 진행률 창 구성
            var progressBar = new System.Windows.Controls.ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Height = 20
            };
            var statusLabel = new System.Windows.Controls.TextBlock
            {
                Text = string.Format("Folderss {0} 다운로드 중...", info.TagName),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(statusLabel);
            panel.Children.Add(progressBar);

            var progressWindow = new Window
            {
                Title = "업데이트 다운로드",
                Width = 420,
                Height = 110,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
                Content = panel
            };
            progressWindow.Show();

            try
            {
                var progress = new Progress<int>(p => progressBar.Value = p);
                await Services.UpdateService.DownloadAsync(info.DownloadUrl, tempPath, progress);

                progressWindow.Close();

                if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    var packageDir = ExtractUpdatePackageDirectory(tempPath);
                    if (packageDir == null)
                    {
                        MessageBox.Show(
                            "zip 파일에서 Folderss 전체 배포본을 찾을 수 없습니다.",
                            "업데이트 오류",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        return;
                    }

                    LaunchZipUpdaterBatch(packageDir);
                    ExitApp();
                    return;
                }

                LaunchInstallerBatch(tempPath);
                ExitApp();
            }
            catch (Exception ex)
            {
                progressWindow.Close();
                MessageBox.Show(
                    "다운로드 중 오류가 발생했습니다.\n" + ex.Message,
                    "업데이트 오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static string ExtractUpdatePackageDirectory(string zipPath)
        {
            var extractDir = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(zipPath), "extracted");

            if (System.IO.Directory.Exists(extractDir))
                System.IO.Directory.Delete(extractDir, true);

            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractDir);

            var exePath = System.IO.Directory.GetFiles(
                extractDir,
                "Folderss.exe",
                System.IO.SearchOption.AllDirectories).FirstOrDefault();
            if (exePath == null)
                return null;

            var packageDir = System.IO.Path.GetDirectoryName(exePath);
            var dllPath = System.IO.Path.Combine(packageDir, "Folderss.dll");
            var depsPath = System.IO.Path.Combine(packageDir, "Folderss.deps.json");
            var runtimeConfigPath = System.IO.Path.Combine(packageDir, "Folderss.runtimeconfig.json");

            if (!File.Exists(dllPath) || !File.Exists(depsPath) || !File.Exists(runtimeConfigPath))
                return null;

            return packageDir;
        }

        private static void LaunchZipUpdaterBatch(string packageDir)
        {
            var currentExePath = GetCurrentProcessExecutablePath();
            var installDir = System.IO.Path.GetDirectoryName(currentExePath);
            var batchPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "folderss_update.bat");

            // 현재 프로세스가 종료될 때까지 전체 배포본 복사를 재시도한 뒤 새 앱을 실행한다.
            var batch =
                "@echo off\r\n" +
                ":retry\r\n" +
                "xcopy /e /i /y \"" + packageDir + "\\*\" \"" + installDir + "\\\" >nul 2>&1\r\n" +
                "if errorlevel 1 (\r\n" +
                "    ping -n 2 127.0.0.1 >nul\r\n" +
                "    goto retry\r\n" +
                ")\r\n" +
                "start \"\" \"" + currentExePath + "\"\r\n" +
                "del \"%~f0\"\r\n";

            System.IO.File.WriteAllText(batchPath, batch, System.Text.Encoding.Default);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c \"" + batchPath + "\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }

        private static void LaunchInstallerBatch(string installerPath)
        {
            var batchPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "folderss_update.bat");

            // 설치형 자산은 현재 앱을 덮어쓰지 않고 종료 후 그대로 실행한다.
            var batch =
                "@echo off\r\n" +
                ":retry\r\n" +
                "start \"\" \"" + installerPath + "\"\r\n" +
                "if errorlevel 1 (\r\n" +
                "    ping -n 2 127.0.0.1 >nul\r\n" +
                "    goto retry\r\n" +
                ")\r\n" +
                "del \"%~f0\"\r\n";

            System.IO.File.WriteAllText(batchPath, batch, System.Text.Encoding.Default);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c \"" + batchPath + "\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }

        private static string GetCurrentProcessExecutablePath()
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
                return processPath;

            using (var process = Process.GetCurrentProcess())
            {
                return process.MainModule.FileName;
            }
        }

        private static void OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            new AboutWindow { Owner = this }.ShowDialog();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            ExitApp();
        }

        // ── Tab context menu ──────────────────────────────────────────────

        private void DockManager_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var tab = FindVisualAncestor<AvalonDock.Controls.LayoutDocumentTabItem>(
                e.OriginalSource as DependencyObject);
            if (tab == null) return;

            var document = tab.Model as LayoutDocument;
            if (document == null) return;

            e.Handled = true;

            var menu = new System.Windows.Controls.ContextMenu();

            var closeItem = new System.Windows.Controls.MenuItem { Header = "닫기" };
            closeItem.IsEnabled = document.CanClose;
            closeItem.Click += (s, _) => document.Close();
            menu.Items.Add(closeItem);

            menu.Items.Add(new System.Windows.Controls.Separator());

            var closeOthers = new System.Windows.Controls.MenuItem { Header = "다른 탭 닫기" };
            closeOthers.Click += (s, _) => CloseTabsExcept(document);
            menu.Items.Add(closeOthers);

            var closeLeft = new System.Windows.Controls.MenuItem { Header = "왼쪽 탭 닫기" };
            closeLeft.Click += (s, _) => CloseTabsToLeft(document);
            menu.Items.Add(closeLeft);

            var closeRight = new System.Windows.Controls.MenuItem { Header = "오른쪽 탭 닫기" };
            closeRight.Click += (s, _) => CloseTabsToRight(document);
            menu.Items.Add(closeRight);

            menu.PlacementTarget = tab;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.IsOpen = true;
        }

        private List<LayoutDocument> GetSiblingDocuments(LayoutDocument target)
        {
            var pane = target.Parent as LayoutDocumentPane;
            if (pane == null) return new List<LayoutDocument>();
            return pane.Children.OfType<LayoutDocument>().ToList();
        }

        private void CloseTabsExcept(LayoutDocument target)
        {
            foreach (var doc in GetSiblingDocuments(target)
                .Where(d => !ReferenceEquals(d, target) && d.CanClose).ToList())
                doc.Close();
        }

        private void CloseTabsToLeft(LayoutDocument target)
        {
            var siblings = GetSiblingDocuments(target);
            var idx = siblings.IndexOf(target);
            foreach (var doc in siblings.Take(idx).Where(d => d.CanClose).ToList())
                doc.Close();
        }

        private void CloseTabsToRight(LayoutDocument target)
        {
            var siblings = GetSiblingDocuments(target);
            var idx = siblings.IndexOf(target);
            foreach (var doc in siblings.Skip(idx + 1).Where(d => d.CanClose).ToList())
                doc.Close();
        }

        private void DisposeAllDisposableDocumentContent()
        {
            foreach (var doc in DockManager.Layout.Descendents().OfType<LayoutDocument>().ToList())
                DisposeDocumentContent(doc);
        }

        private void DisposeDocumentContent(LayoutDocument document)
        {
            var disposable = document == null ? null : document.Content as IDisposable;
            if (disposable != null)
                disposable.Dispose();
            if (document != null && document.ContentId == "console")
                _consolePanel = null;
        }

        private static T FindVisualAncestor<T>(DependencyObject element) where T : DependencyObject
        {
            while (element != null)
            {
                if (element is T found) return found;
                element = System.Windows.Media.VisualTreeHelper.GetParent(element);
            }
            return null;
        }

        private Controls.ConsolePanel GetConsolePanel()
        {
            if (_consolePanel != null)
                return _consolePanel;

            _consolePanel = new Controls.ConsolePanel();
            _consolePanel.ActiveDirectoryProvider = () => ActivePane.CurrentPath;
            return _consolePanel;
        }

        private void BuildDefaultDockLayout()
        {
            var root = new LayoutRoot();
            var rootPanel = new LayoutPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal
            };
            var favoritesPane = new LayoutAnchorablePane
            {
                DockWidth = new GridLength(230)
            };
            favoritesPane.Children.Add(new LayoutAnchorable
            {
                Title = "즐겨찾기",
                ContentId = "favorites",
                Content = FavoritesPanel,
                CanClose = true,
                CanHide = true,
                CanAutoHide = true
            });
            var leftPane = new LayoutDocumentPane();
            leftPane.Children.Add(new LayoutDocument
            {
                Title = "왼쪽 폴더",
                ContentId = "left-folder",
                Content = LeftPane,
                CanClose = false
            });
            leftPane.Children.Add(new LayoutDocument
            {
                Title = "＋ 새 패널",
                ContentId = "add-folder-panel",
                Content = new System.Windows.Controls.Grid(),
                CanClose = false
            });

            var rightPane = new LayoutDocumentPane();
            rightPane.Children.Add(new LayoutDocument
            {
                Title = "오른쪽 폴더",
                ContentId = "right-folder",
                Content = RightPane,
                CanClose = false
            });

            var consolePane = new LayoutDocumentPane
            {
                DockHeight = new GridLength(220)
            };
            consolePane.Children.Add(new LayoutDocument
            {
                Title = "콘솔",
                ContentId = "console",
                Content = GetConsolePanel(),
                CanClose = true
            });

            var leftColumn = new LayoutDocumentPaneGroup
            {
                Orientation = System.Windows.Controls.Orientation.Vertical
            };
            leftColumn.Children.Add(leftPane);
            leftColumn.Children.Add(consolePane);

            var documents = new LayoutDocumentPaneGroup
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal
            };
            documents.Children.Add(leftColumn);
            documents.Children.Add(rightPane);

            rootPanel.Children.Add(favoritesPane);
            rootPanel.Children.Add(documents);
            root.RootPanel = rootPanel;
            DockManager.Layout = root;
        }

        private LayoutAnchorable CreateFavoritesDock()
        {
            var rootPanel = DockManager.Layout == null ? null : DockManager.Layout.RootPanel;
            if (rootPanel == null)
                return null;
            var hostPanel = rootPanel.Children
                .OfType<LayoutPanel>()
                .FirstOrDefault(panel => panel.Orientation == System.Windows.Controls.Orientation.Horizontal)
                ?? rootPanel;

            var pane = new LayoutAnchorablePane
            {
                DockWidth = new GridLength(230)
            };
            var dock = new LayoutAnchorable
            {
                Title = "즐겨찾기",
                ContentId = "favorites",
                Content = FavoritesPanel,
                CanClose = true,
                CanHide = true,
                CanAutoHide = true
            };

            pane.Children.Add(dock);
            hostPanel.InsertChildAt(0, pane);
            return dock;
        }

        private LayoutDocumentPane EnsureConsolePane()
        {
            var existingConsolePane = DockManager.Layout.Descendents()
                .OfType<LayoutDocumentPane>()
                .FirstOrDefault(pane => pane.Children.OfType<LayoutDocument>()
                    .Any(child => child.ContentId == "console"));
            if (existingConsolePane != null)
                return existingConsolePane;

            var leftPane = DockManager.Layout.Descendents()
                .OfType<LayoutDocumentPane>()
                .FirstOrDefault(pane => pane.Children.OfType<LayoutDocument>()
                    .Any(child => child.ContentId == "left-folder"));
            if (leftPane == null)
                return null;

            var consolePane = new LayoutDocumentPane
            {
                DockHeight = new GridLength(220)
            };

            var parentGroup = leftPane.Parent as LayoutDocumentPaneGroup;
            if (parentGroup != null &&
                parentGroup.Orientation == System.Windows.Controls.Orientation.Vertical)
            {
                parentGroup.Children.Add(consolePane);
                return consolePane;
            }

            if (parentGroup == null)
                return null;

            var leftPaneIndex = parentGroup.IndexOfChild(leftPane);
            if (leftPaneIndex < 0)
                return null;

            var leftColumn = new LayoutDocumentPaneGroup
            {
                Orientation = System.Windows.Controls.Orientation.Vertical,
                DockWidth = leftPane.DockWidth
            };
            parentGroup.RemoveChild(leftPane);
            parentGroup.InsertChildAt(leftPaneIndex, leftColumn);
            leftColumn.Children.Add(leftPane);
            leftColumn.Children.Add(consolePane);
            return consolePane;
        }

        private void MigrateLegacyConsoleTabLayout()
        {
            var leftDocument = DockManager.Layout.Descendents()
                .OfType<LayoutDocument>()
                .FirstOrDefault(document => document.ContentId == "left-folder");
            var consoleDocument = DockManager.Layout.Descendents()
                .OfType<LayoutDocument>()
                .FirstOrDefault(document => document.ContentId == "console");
            var sourcePane = leftDocument == null ? null : leftDocument.Parent as LayoutDocumentPane;

            if (sourcePane == null || consoleDocument == null ||
                !ReferenceEquals(sourcePane, consoleDocument.Parent))
            {
                return;
            }

            sourcePane.RemoveChild(consoleDocument);
            var targetPane = EnsureConsolePane();
            if (targetPane == null)
            {
                sourcePane.Children.Add(consoleDocument);
                return;
            }

            targetPane.Children.Add(consoleDocument);
        }

        private void ShowConsolePanel()
        {
            var doc = DockManager.Layout.Descendents()
                .OfType<LayoutDocument>()
                .FirstOrDefault(d => d.ContentId == "console");

            if (doc == null)
            {
                var targetPane = EnsureConsolePane();
                if (targetPane == null)
                    return;

                doc = new LayoutDocument
                {
                    Title = "콘솔",
                    ContentId = "console",
                    Content = GetConsolePanel(),
                    CanClose = true
                };
                doc.Closed += (s, e) => DisposeDocumentContent(doc);
                targetPane.Children.Add(doc);
            }

            doc.IsActive = true;

            var panel = GetConsolePanel();
            if (panel != null)
            {
                panel.EnsureStarted();
                Dispatcher.BeginInvoke(new Action(panel.FocusCommandBox), DispatcherPriority.Input);
            }
        }

        private void ShowConsole_Click(object sender, RoutedEventArgs e)
        {
            ShowConsolePanel();
        }

        private void DockManager_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var tab = FindVisualAncestor<AvalonDock.Controls.LayoutDocumentTabItem>(
                e.OriginalSource as DependencyObject);
            var document = tab == null ? null : tab.Model as LayoutDocument;
            if (document == null || document.ContentId != "add-folder-panel" || _openingPanelFromAddTab)
                return;

            e.Handled = true;
            _openingPanelFromAddTab = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    AddFolderPanel(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                }
                finally
                {
                    _openingPanelFromAddTab = false;
                }
            }), DispatcherPriority.Background);
        }
    }
}


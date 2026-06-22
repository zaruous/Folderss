using Folderss.Controls;
using Folderss.Services;
using AvalonDock.Layout;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Folderss
{
    public partial class MainWindow : Window
    {
        private FolderBrowser _activePane;
        private bool _resetDockLayoutRequested;
        private SessionState _sessionState;
        private bool _openingPanelFromAddTab;
        private Forms.NotifyIcon _trayIcon;
        private bool _reallyClose;
        private bool _fixingAddPanelLayout;
        private bool _shownTrayBalloon;
        private bool _isCut;
        private HashSet<string> _cutPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _sessionState = SessionStateService.Load();

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
                RestoreAdditionalPanels(_sessionState.OpenFolderPaths);

            // 검색 패널은 Ctrl+F로 열기 전까지 숨김
            FindDock("search-panel")?.Hide();

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
            MainContextMenu.PlacementTarget = MainMenuButton;
            MainContextMenu.Placement = PlacementMode.Bottom;
            MainContextMenu.IsOpen = true;
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
            if (contentId == "add-folder-panel")
                return new System.Windows.Controls.Grid();
            if (contentId == "search-panel")
                return SearchPanel;

            const string prefix = "folder-panel|";
            if (string.IsNullOrWhiteSpace(contentId) || !contentId.StartsWith(prefix, StringComparison.Ordinal))
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

        private void AddPanelDocument_IsActiveChanged(object sender, EventArgs e)
        {
            var addDocument = sender as LayoutDocument;
            if (addDocument == null || !addDocument.IsActive || _openingPanelFromAddTab)
                return;

            _openingPanelFromAddTab = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var defaultPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    AddFolderPanel(defaultPath);
                }
                finally
                {
                    _openingPanelFromAddTab = false;
                }
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
            browser.Activated += Pane_Activated;
            browser.PathChanged += FolderBrowser_PathChanged;
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

            _trayIcon?.Dispose();
            _trayIcon = null;

            SaveSessionState();

            if (!_resetDockLayoutRequested)
            {
                try
                {
                    DockLayoutService.Save(DockManager);
                }
                catch
                {
                    // 종료 시 배치 저장 실패는 앱 종료를 막지 않는다.
                }
            }
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
            FindDock("favorites")?.Show();
        }

        private void ResetDockLayout_Click(object sender, RoutedEventArgs e)
        {
            DockLayoutService.Reset();
            _resetDockLayoutRequested = true;
            MessageBox.Show(
                "도킹 배치가 초기화되었습니다. 다음 실행부터 기본 배치가 적용됩니다.",
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

        private void ApplyTheme(AppTheme theme)
        {
            ThemeManager.ApplyTheme(theme);
            foreach (var pane in GetFolderBrowsers())
                pane.SetActive(ActivePane == pane);
            UpdateThemeMenuChecks();
        }

        private void UpdateThemeMenuChecks()
        {
            BlackThemeMenuItem.IsChecked = ThemeManager.CurrentTheme == AppTheme.Black;
            LightThemeMenuItem.IsChecked = ThemeManager.CurrentTheme == AppTheme.Light;
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
            if (e.Key == Key.F2)
            {
                if (FavoritesPanel.IsKeyboardFocusWithin)
                    return;
                Rename_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.F5)
            {
                Copy_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.F6)
            {
                Move_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                DeleteSelected((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift);
                e.Handled = true;
            }
            else if (e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Control)
            {
                RefreshBothPanes();
                e.Handled = true;
            }
            else if (e.Key == Key.N && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                NewFolder_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.T && Keyboard.Modifiers == ModifierKeys.Control)
            {
                AddFolderPanel_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (Keyboard.FocusedElement is System.Windows.Controls.TextBox)
                    return;
                CopyToClipboard();
                e.Handled = true;
            }
            else if (e.Key == Key.X && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (Keyboard.FocusedElement is System.Windows.Controls.TextBox)
                    return;
                CutToClipboard();
                e.Handled = true;
            }
            else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (Keyboard.FocusedElement is System.Windows.Controls.TextBox)
                    return;
                PasteFromClipboard();
                e.Handled = true;
            }
            else if (e.SystemKey == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt)
            {
                ActivePane.NavigateBack();
                e.Handled = true;
            }
            else if (e.SystemKey == Key.Right && Keyboard.Modifiers == ModifierKeys.Alt)
            {
                ActivePane.NavigateForward();
                e.Handled = true;
            }
            else if (e.SystemKey == Key.Up && Keyboard.Modifiers == ModifierKeys.Alt)
            {
                ActivePane.NavigateUp();
                e.Handled = true;
            }
            else if (e.Key == Key.Left && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                SwitchToAdjacentPane(-1);
                e.Handled = true;
            }
            else if (e.Key == Key.Right && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                SwitchToAdjacentPane(1);
                e.Handled = true;
            }
            else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ShowSearchPanel();
                e.Handled = true;
            }
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
            SearchPanel.SetRootPath(ActivePane.CurrentPath);
            var dock = FindDock("search-panel");
            if (dock == null) return;

            if (dock.IsVisible)
            {
                dock.Hide();
                return;
            }
            dock.Show();
            Dispatcher.BeginInvoke(new Action(() => SearchPanel.FocusSearchBox()), DispatcherPriority.Input);
        }

        private void SearchPanel_NavigateRequested(object sender, Controls.SearchNavigateEventArgs e)
        {
            ActivePane.NavigateTo(e.Path);
        }

        private void SearchPanel_HideRequested(object sender, EventArgs e)
        {
            FindDock("search-panel")?.Hide();
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
            if (!Clipboard.ContainsFileDropList())
                return;

            var files = Clipboard.GetFileDropList();
            if (files.Count == 0)
                return;

            var targetPath = ActivePane.CurrentPath;
            if (string.IsNullOrWhiteSpace(targetPath) || !Directory.Exists(targetPath))
                return;

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
                ActivePane.RefreshItems();
            }

            ShowErrorsIfAny("붙여넣기", errors);
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            new AboutWindow { Owner = this }.ShowDialog();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            ExitApp();
        }
    }
}

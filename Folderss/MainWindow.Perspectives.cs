using Folderss.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace Folderss
{
    /// <summary>
    /// 퍼스펙티브: 메인 창 전체를 파일 화면(DockManager)과 메인 창에 띄운 플러그인 화면 사이에서 바꾼다
    /// (이클립스 Java SE / Java EE 퍼스펙티브 전환처럼). 표시 방식이 <see cref="PluginDisplayStore.Main"/>인 플러그인과
    /// 기본 기능 Git(<see cref="PluginDisplayStore.BuiltinGitId"/>)만 여기로 온다.
    /// DockManager는 다른 부모로 옮기지 않고 <see cref="UIElement.Visibility"/>만 바꾼다 — 옮기면 도킹 콘텐츠의 논리 부모가 꼬인다.
    /// 플러그인 뷰는 처음 열 때 한 번 만들고 닫을 때까지 유지한다(전환해도 상태가 남게).
    /// </summary>
    public partial class MainWindow
    {
        private sealed class PluginPerspective
        {
            public string Id;
            public PluginManifest Manifest;
            public FrameworkElement View;
            public ToggleButton Button;
            public string Icon = "🔌";
            /// <summary>퍼스펙티브를 닫을 때(팝업으로 옮길 때는 아님). 플러그인은 null — Dispose를 부르지 않는다.</summary>
            public Action Closed;
            /// <summary>같은 뷰를 팝업 창에 넣어 연다.</summary>
            public Action MoveToPopup;
        }

        private readonly List<PluginPerspective> _perspectives = new List<PluginPerspective>();
        private ToggleButton _filePerspectiveButton;

        /// <summary>지금 보이는 플러그인 퍼스펙티브. 파일 퍼스펙티브면 null.</summary>
        private PluginPerspective _activePerspective;

        private bool IsPluginPerspectiveActive
        {
            get { return _activePerspective != null; }
        }

        private void InitializePerspectives()
        {
            PluginManager.PluginFaulted += OnPluginFaulted;
        }

        /// <summary>⋯ 메뉴·트레이에서 플러그인을 실행할 때. 설정 > 플러그인의 표시 방식에 따라 팝업 또는 메인 창에 띄운다.</summary>
        private void RunPlugin(PluginManifest manifest)
        {
            var existing = FindPerspective(manifest.Id);
            if (existing != null)
            {
                ShowPerspective(existing);
                return;
            }

            // plugin.json popupOnly: 창을 직접 다루는 플러그인은 설정과 관계없이 팝업으로만 연다.
            if (manifest.PopupOnly || PluginManager.GetDisplayMode(manifest.Id) != PluginDisplayStore.Main)
            {
                PluginManager.ShowPluginWindow(manifest, IsVisible ? this : null);
                return;
            }

            var view = PluginManager.CreatePluginView(manifest);
            if (view == null)
                return;

            OpenPerspective(new PluginPerspective
            {
                Id = manifest.Id,
                Manifest = manifest,
                View = view,
                MoveToPopup = () => PluginManager.ShowViewWindow(manifest, view, this)
            });
        }

        /// <summary>Git을 메인 창 퍼스펙티브로 연다(설정 > 플러그인의 "Git (기본 기능)" 표시 방식이 메인 창일 때).</summary>
        private void OpenGitPerspective(GitView view)
        {
            var perspective = new PluginPerspective
            {
                Id = PluginDisplayStore.BuiltinGitId,
                Manifest = new PluginManifest { Id = PluginDisplayStore.BuiltinGitId, Name = "Git", Description = "Git 저장소 (기본 기능)" },
                View = view,
                Icon = "⎇",
                Closed = view.Shutdown,
                MoveToPopup = () => new GitWindow(view) { Owner = this }.Show()
            };
            // git이 없거나 버전이 맞지 않으면 뷰가 Loaded 중에 닫기를 요청한다 — 레이아웃 처리 중에 떼지 않게 다음 순서에서.
            view.CloseRequested += () => Dispatcher.BeginInvoke(new Action(() => ClosePerspective(perspective)), DispatcherPriority.Background);
            OpenPerspective(perspective);
        }

        private void OpenPerspective(PluginPerspective perspective)
        {
            var manifest = perspective.Manifest;
            var view = perspective.View;
            try
            {
                view.Visibility = Visibility.Collapsed;
                PerspectiveHost.Children.Add(view);
            }
            catch (Exception ex)
            {
                // 이미 다른 부모가 있는 요소를 돌려준 경우 등
                PerspectiveHost.Children.Remove(view);
                MessageBox.Show("플러그인 화면을 메인 창에 넣지 못했습니다.\n\n" + ex.GetType().Name + ": " + ex.Message,
                    "플러그인 - " + manifest.DisplayName, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            perspective.Button = CreatePerspectiveButton(perspective);
            _perspectives.Add(perspective);
            ShowPerspective(perspective);
        }

        private PluginPerspective FindPerspective(string id)
        {
            if (id == null)
                return null;
            return _perspectives.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary><paramref name="perspective"/>가 null이면 파일 퍼스펙티브.</summary>
        private void ShowPerspective(PluginPerspective perspective)
        {
            if (perspective != null && !_perspectives.Contains(perspective))
                perspective = null;
            if (!IsVisible && perspective != null)
                ShowFromTray();

            _activePerspective = perspective;
            DockManager.Visibility = perspective == null ? Visibility.Visible : Visibility.Collapsed;
            StatusBarBorder.Visibility = perspective == null ? Visibility.Visible : Visibility.Collapsed;
            PerspectiveHost.Visibility = perspective == null ? Visibility.Collapsed : Visibility.Visible;
            foreach (var item in _perspectives)
                item.View.Visibility = item == perspective ? Visibility.Visible : Visibility.Collapsed;

            UpdatePerspectiveBar();

            // 숨겨진 쪽에 키보드 포커스가 남으면 단축키·입력이 보이지 않는 화면으로 간다.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_activePerspective != perspective)
                    return;
                // 다른 창(퍼스펙티브에서 옮긴 팝업 등)이 활성이면 포커스를 가져오지 않는다 — Focus()가 메인 창을 활성화한다.
                if (!IsActive)
                    return;
                if (perspective != null)
                {
                    if (!perspective.View.IsKeyboardFocusWithin)
                        perspective.View.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                }
                else if (!DockManager.IsKeyboardFocusWithin)
                {
                    ActivePane.FocusFileList();
                }
            }), DispatcherPriority.Input);
        }

        private void ShowFilePerspective()
        {
            if (IsPluginPerspectiveActive)
                ShowPerspective(null);
        }

        /// <summary>파일 → 열린 플러그인들 → 다시 파일 순으로 전환한다 (단축키 NextPerspective).</summary>
        private void ShowNextPerspective()
        {
            if (_perspectives.Count == 0)
                return;
            var index = _activePerspective == null ? -1 : _perspectives.IndexOf(_activePerspective);
            ShowPerspective(index + 1 < _perspectives.Count ? _perspectives[index + 1] : null);
        }

        private void ClosePerspective(PluginPerspective perspective)
        {
            if (DetachPerspective(perspective))
                perspective.Closed?.Invoke();
        }

        /// <summary>앱 종료 때 열린 퍼스펙티브의 정리 작업(Git: 진행 중인 git 프로세스 종료)을 부른다.</summary>
        private void ShutdownPerspectives()
        {
            foreach (var perspective in _perspectives.ToList())
                perspective.Closed?.Invoke();
        }

        /// <summary>퍼스펙티브를 목록·화면에서 떼어 낸다. 뷰 자체는 건드리지 않는다(팝업으로 옮길 수 있게).</summary>
        private bool DetachPerspective(PluginPerspective perspective)
        {
            if (!_perspectives.Remove(perspective))
                return false;
            // 팝업 창을 닫을 때와 같이 트리에서 떼어 내기만 한다(Unloaded가 온다). Dispose는 부르지 않는다 —
            // 팝업도 부르지 않고, 플러그인이 같은 뷰를 다시 돌려줄 수도 있다.
            PerspectiveHost.Children.Remove(perspective.View);
            ShowPerspective(_activePerspective == perspective ? null : _activePerspective);
            return true;
        }

        /// <summary>
        /// 같은 뷰를 팝업 창으로 옮긴다. <c>CreateView()</c>를 다시 부르지 않으므로 화면 상태가 남고,
        /// "창은 하나만" 규칙을 가진 플러그인이 새 창을 바로 닫는 일도 없다.
        /// 메뉴 클릭 처리 중에 버튼(메뉴의 주인)을 지우지 않도록 다음 순서에서 옮긴다.
        /// </summary>
        private void MovePerspectiveToPopup(PluginPerspective perspective)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_perspectives.Contains(perspective))
                    return;
                DetachPerspective(perspective);
                // 다른 퍼스펙티브가 보이는 중에 옮기면 이 뷰는 접혀(Collapsed) 있다.
                perspective.View.Visibility = Visibility.Visible;
                perspective.MoveToPopup();
            }), DispatcherPriority.Background);
        }

        /// <summary>
        /// 메인 창 Close()가 플러그인 코드에서 왔고 그 플러그인이 퍼스펙티브로 떠 있으면, 트레이로 숨기지 않고 그 퍼스펙티브만 닫는다.
        /// 팝업용으로 만든 플러그인은 <c>Window.GetWindow(view).Close()</c>로 자기 창을 닫는데, 퍼스펙티브에서는 그 창이 메인 창이기 때문.
        /// </summary>
        private bool TryClosePerspectiveForPluginClose()
        {
            if (_perspectives.Count == 0)
                return false;
            var perspective = FindPerspective(PluginManager.FindCallingPluginId());
            if (perspective == null)
                return false;
            Dispatcher.BeginInvoke(new Action(() => ClosePerspective(perspective)), DispatcherPriority.Background);
            return true;
        }

        private void OnPluginFaulted(string pluginId)
        {
            var perspective = FindPerspective(pluginId);
            if (perspective == null)
                return;
            // 예외 처리 중 레이아웃을 바로 바꾸지 않고 다음 순서에서 떼어 낸다.
            Dispatcher.BeginInvoke(new Action(() => ClosePerspective(perspective)), DispatcherPriority.Send);
        }

        // ── 전환 버튼 ─────────────────────────────────────────────────────

        private void UpdatePerspectiveBar()
        {
            if (_perspectives.Count == 0)
            {
                PerspectiveBar.Children.Clear();
                PerspectiveBar.Visibility = Visibility.Collapsed;
                return;
            }

            if (_filePerspectiveButton == null)
            {
                _filePerspectiveButton = CreateBarButton("📁", "파일", null);
                _filePerspectiveButton.ToolTip = "파일 관리 화면 (Ctrl+F8: 다음 퍼스펙티브)";
                _filePerspectiveButton.Click += (s, e) => ShowPerspective(null);
            }

            PerspectiveBar.Children.Clear();
            PerspectiveBar.Children.Add(_filePerspectiveButton);
            foreach (var item in _perspectives)
                PerspectiveBar.Children.Add(item.Button);

            _filePerspectiveButton.IsChecked = _activePerspective == null;
            foreach (var item in _perspectives)
                item.Button.IsChecked = item == _activePerspective;
            PerspectiveBar.Visibility = Visibility.Visible;
        }

        private ToggleButton CreatePerspectiveButton(PluginPerspective perspective)
        {
            var close = new Button
            {
                Content = "×",
                Style = (Style)FindResource("CompactToolButtonStyle"),
                Width = 16,
                Height = 16,
                FontSize = 11,
                Margin = new Thickness(4, 0, 0, 0),
                ToolTip = "닫기"
            };
            close.Click += (s, e) =>
            {
                e.Handled = true;
                ClosePerspective(perspective);
            };

            var button = CreateBarButton(perspective.Icon, perspective.Manifest.DisplayName, close);
            button.ToolTip = string.IsNullOrWhiteSpace(perspective.Manifest.Description)
                ? perspective.Manifest.DisplayName
                : perspective.Manifest.DisplayName + "\n" + perspective.Manifest.Description;
            button.Click += (s, e) => ShowPerspective(perspective);

            var closeItem = new MenuItem { Header = "닫기" };
            closeItem.Click += (s, e) => ClosePerspective(perspective);
            var popupItem = new MenuItem { Header = "팝업 창으로 옮기기", ToolTip = "지금 화면(입력·상태 그대로)을 팝업 창으로 옮깁니다. 표시 방식 설정은 바꾸지 않습니다." };
            popupItem.Click += (s, e) => MovePerspectiveToPopup(perspective);
            button.ContextMenu = new ContextMenu();
            button.ContextMenu.Items.Add(popupItem);
            button.ContextMenu.Items.Add(new Separator());
            button.ContextMenu.Items.Add(closeItem);
            return button;
        }

        private ToggleButton CreateBarButton(string icon, string text, UIElement trailing)
        {
            // CompactToggleButtonStyle 템플릿은 Padding을 쓰지 않아 내용 쪽에 여백을 둔다.
            var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) };
            content.Children.Add(new TextBlock { Text = icon, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(new TextBlock
            {
                Text = text,
                MaxWidth = 160,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            });
            if (trailing != null)
                content.Children.Add(trailing);

            return new ToggleButton
            {
                Style = (Style)FindResource("CompactToggleButtonStyle"),
                Content = content,
                Height = 26,
                Margin = new Thickness(2, 0, 0, 0),
                Focusable = false
            };
        }

        // ── 다른 기능과의 연결 ──────────────────────────────────────────

        /// <summary>
        /// 플러그인 퍼스펙티브가 보이는 동안 ⋯ 메뉴에서 파일·패널 대상 항목을 끈다(숨겨진 패널의 선택에 작업이 가지 않게).
        /// 어디서나 쓸 수 있는 항목은 XAML에서 <c>Tag="global"</c>.
        /// </summary>
        private void UpdateMainMenuForPerspective()
        {
            // 원래 꺼져 있던 항목을 켜지 않도록 여기서 끈 항목만 되돌린다.
            foreach (var item in _menuItemsDisabledByPerspective)
                item.IsEnabled = true;
            _menuItemsDisabledByPerspective.Clear();
            if (!IsPluginPerspectiveActive)
                return;
            foreach (var item in MainContextMenu.Items.OfType<MenuItem>())
            {
                if (item.IsEnabled && !string.Equals(item.Tag as string, "global", StringComparison.Ordinal))
                {
                    item.IsEnabled = false;
                    _menuItemsDisabledByPerspective.Add(item);
                }
            }
        }

        private readonly List<MenuItem> _menuItemsDisabledByPerspective = new List<MenuItem>();
    }
}

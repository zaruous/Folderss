using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Folderss.Services;

namespace Folderss
{
    /// <summary>
    /// 잠긴 파일의 사용 중인 프로세스 조회 및 종료/삭제 재시도 대화상자.
    /// </summary>
    public sealed class FileLockInspectionWindow : Window
    {
        private readonly string _targetPath;
        private readonly Action<string> _retryDeleteAction;

        private readonly TextBlock _pathText = new TextBlock { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock _statusText = new TextBlock { Margin = new Thickness(0, 4, 0, 8), TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _snapshotTimeText = new TextBlock { FontSize = 11, Margin = new Thickness(0, 0, 0, 8) };
        private readonly ListView _processListView = new ListView();
        private readonly Button _refreshButton = new Button { Content = "새로고침 (F5)", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        private readonly Button _requestCloseButton = new Button { Content = "정상 종료 요청", MinWidth = 100, Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
        private readonly Button _forceKillButton = new Button { Content = "강제 종료…", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
        private readonly Button _retryDeleteButton = new Button { Content = "삭제 재시도", MinWidth = 95, Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
        private readonly Button _closeButton = new Button { Content = "닫기", MinWidth = 75, IsCancel = true };
        private readonly TextBlock _operationMessage = new TextBlock { FontSize = 12, Margin = new Thickness(2, 6, 2, 0), TextWrapping = TextWrapping.Wrap };

        private CancellationTokenSource _inspectCts;
        private FileLockInspectionResult _lastResult;

        public FileLockInspectionWindow(string targetPath, Action<string> retryDeleteAction = null)
        {
            _targetPath = targetPath ?? string.Empty;
            _retryDeleteAction = retryDeleteAction;

            Title = "사용 중인 프로세스 확인";
            Width = 680;
            Height = 460;
            MinWidth = 520;
            MinHeight = 320;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            SetResourceReference(BackgroundProperty, "WindowBackground");
            SetResourceReference(ForegroundProperty, "PrimaryText");
            SetResourceReference(FontFamilyProperty, "AppFontFamily");
            FontSize = 13;

            BuildUi();

            Loaded += (s, e) => RefreshInspection();
            KeyDown += OnWindowKeyDown;
        }

        private void BuildUi()
        {
            var root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header (Path, Copy)
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Status & Snapshot
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Process List
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Operation message
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

            // 1. Header (Path & Copy button)
            var headerDock = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var copyPathBtn = new Button { Content = "경로 복사", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(8, 0, 0, 0) };
            DockPanel.SetDock(copyPathBtn, Dock.Right);
            copyPathBtn.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(_targetPath);
                    SetOperationMessage("경로를 클립보드에 복사했습니다.", isWarning: false);
                }
                catch (Exception ex)
                {
                    SetOperationMessage("경로 복사 실패: " + ex.Message, isWarning: true);
                }
            };
            headerDock.Children.Add(copyPathBtn);

            _pathText.Text = _targetPath;
            _pathText.ToolTip = _targetPath;
            headerDock.Children.Add(_pathText);
            Grid.SetRow(headerDock, 0);
            root.Children.Add(headerDock);

            // 2. Status & Snapshot Info
            var statusPanel = new StackPanel();
            _statusText.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            _statusText.Text = "프로세스 점유 상태를 조회하고 있습니다...";
            _snapshotTimeText.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");

            statusPanel.Children.Add(_statusText);
            statusPanel.Children.Add(_snapshotTimeText);
            Grid.SetRow(statusPanel, 1);
            root.Children.Add(statusPanel);

            // 3. Process ListView
            var gridView = new GridView();
            gridView.Columns.Add(new GridViewColumn
            {
                Header = "프로세스명 / 창 제목",
                Width = 190,
                DisplayMemberBinding = new System.Windows.Data.Binding("DisplayName")
            });
            gridView.Columns.Add(new GridViewColumn
            {
                Header = "PID",
                Width = 65,
                DisplayMemberBinding = new System.Windows.Data.Binding("ProcessId")
            });
            gridView.Columns.Add(new GridViewColumn
            {
                Header = "종류",
                Width = 130,
                DisplayMemberBinding = new System.Windows.Data.Binding("ProcessTypeDescription")
            });
            gridView.Columns.Add(new GridViewColumn
            {
                Header = "실행 파일 경로 / 상태",
                Width = 240,
                DisplayMemberBinding = new System.Windows.Data.Binding("ExecutablePath")
            });

            _processListView.View = gridView;
            _processListView.SelectionMode = SelectionMode.Single;
            _processListView.SelectionChanged += OnProcessSelectionChanged;
            _processListView.SetResourceReference(BackgroundProperty, "PanelBackground");
            _processListView.SetResourceReference(ForegroundProperty, "PrimaryText");
            _processListView.SetResourceReference(BorderBrushProperty, "BorderBrush");

            Grid.SetRow(_processListView, 2);
            root.Children.Add(_processListView);

            // 4. Operation message
            Grid.SetRow(_operationMessage, 3);
            root.Children.Add(_operationMessage);

            // 5. Bottom Buttons
            var bottomDock = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };

            var rightButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            rightButtons.Children.Add(_refreshButton);
            rightButtons.Children.Add(_requestCloseButton);
            rightButtons.Children.Add(_forceKillButton);
            if (_retryDeleteAction != null)
            {
                rightButtons.Children.Add(_retryDeleteButton);
            }
            rightButtons.Children.Add(_closeButton);

            bottomDock.Children.Add(rightButtons);
            Grid.SetRow(bottomDock, 4);
            root.Children.Add(bottomDock);

            Content = root;

            // Events
            _refreshButton.Click += (s, e) => RefreshInspection();
            _requestCloseButton.Click += OnRequestCloseClick;
            _forceKillButton.Click += OnForceKillClick;
            _retryDeleteButton.Click += OnRetryDeleteClick;
            _closeButton.Click += (s, e) => Close();
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F5)
            {
                RefreshInspection();
                e.Handled = true;
            }
        }

        private async void RefreshInspection()
        {
            _inspectCts?.Cancel();
            _inspectCts = new CancellationTokenSource();
            var token = _inspectCts.Token;

            _refreshButton.IsEnabled = false;
            _requestCloseButton.IsEnabled = false;
            _forceKillButton.IsEnabled = false;
            _retryDeleteButton.IsEnabled = false;
            _statusText.Text = "점유 프로세스를 조회하는 중입니다...";
            _operationMessage.Text = string.Empty;

            try
            {
                var result = await FileLockService.InspectAsync(_targetPath, token);
                if (token.IsCancellationRequested)
                    return;

                _lastResult = result;
                _processListView.ItemsSource = result.Processes;

                _snapshotTimeText.Text = $"조회 시각: {result.InspectedAtUtc.ToLocalTime():HH:mm:ss} (현재 시점의 스냅숏이며 변경될 수 있습니다)";

                if (!result.Success)
                {
                    _statusText.Text = result.Message;
                    _statusText.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x75));
                }
                else
                {
                    _statusText.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
                    _statusText.Text = result.Message;
                }

                // 파일 존재 여부에 따라 삭제 재시도 버튼 활성화
                if (_retryDeleteAction != null)
                {
                    bool exists = File.Exists(_targetPath) || Directory.Exists(_targetPath);
                    _retryDeleteButton.IsEnabled = exists;
                }
            }
            catch (OperationCanceledException)
            {
                // 취소됨
            }
            catch (Exception ex)
            {
                _statusText.Text = "조회 오류: " + ex.Message;
                _statusText.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x75));
            }
            finally
            {
                _refreshButton.IsEnabled = true;
                UpdateActionButtons();
            }
        }

        private void OnProcessSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateActionButtons();
        }

        private void UpdateActionButtons()
        {
            var selected = _processListView.SelectedItem as ProcessLockInfo;
            if (selected == null)
            {
                _requestCloseButton.IsEnabled = false;
                _forceKillButton.IsEnabled = false;
                return;
            }

            _requestCloseButton.IsEnabled = selected.CanRequestClose;
            _forceKillButton.IsEnabled = selected.CanForceKill;

            if (!string.IsNullOrEmpty(selected.UnclosableReason))
            {
                SetOperationMessage($"선택한 프로세스({selected.DisplayName}): {selected.UnclosableReason}", isWarning: false);
            }
            else
            {
                _operationMessage.Text = string.Empty;
            }
        }

        private async void OnRequestCloseClick(object sender, RoutedEventArgs e)
        {
            var selected = _processListView.SelectedItem as ProcessLockInfo;
            if (selected == null || !selected.CanRequestClose)
                return;

            _requestCloseButton.IsEnabled = false;
            _forceKillButton.IsEnabled = false;
            _refreshButton.IsEnabled = false;
            SetOperationMessage($"'{selected.DisplayName}' (PID: {selected.ProcessId}) 프로세스에 정상 종료(창 닫기)를 요청했습니다. 저장 확인 대화상자 처리 대기 중...", isWarning: false);

            try
            {
                var (exited, message) = await FileLockService.RequestCloseProcessAsync(selected, waitTimeoutMs: 4000);
                SetOperationMessage(message, isWarning: !exited);
            }
            catch (Exception ex)
            {
                SetOperationMessage("정상 종료 요청 중 오류: " + ex.Message, isWarning: true);
            }
            finally
            {
                _refreshButton.IsEnabled = true;
                RefreshInspection();
            }
        }

        private void OnForceKillClick(object sender, RoutedEventArgs e)
        {
            var selected = _processListView.SelectedItem as ProcessLockInfo;
            if (selected == null || !selected.CanForceKill)
                return;

            var confirm = new GitForceConfirmDialog(
                "프로세스 강제 종료",
                $"'{selected.DisplayName}' (PID: {selected.ProcessId})을(를) 종료합니다.",
                "강제 종료 사용",
                "저장되지 않은 작업이나 데이터가 손실될 수 있으며 되돌릴 수 없습니다.",
                "확인") { Owner = this };
            if (confirm.ShowDialog() != true || !confirm.Force)
                return;

            var (exited, message) = FileLockService.ForceKillProcess(selected);
            SetOperationMessage(message, isWarning: !exited);
            RefreshInspection();
        }

        private void OnRetryDeleteClick(object sender, RoutedEventArgs e)
        {
            if (_retryDeleteAction == null)
                return;

            var confirm = MessageBox.Show(this,
                $"'{Path.GetFileName(_targetPath)}' 삭제를 다시 시도하시겠습니까?\n\n이 작업은 파일 작업 대기열에 추가되어 실행됩니다.",
                "삭제 재시도",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            Close();
            _retryDeleteAction(_targetPath);
        }

        private void SetOperationMessage(string message, bool isWarning)
        {
            _operationMessage.Text = message;
            if (isWarning)
            {
                _operationMessage.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x75));
            }
            else
            {
                _operationMessage.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _inspectCts?.Cancel();
            base.OnClosed(e);
        }
    }
}

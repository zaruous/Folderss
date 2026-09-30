using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Folderss.Controls;
using Folderss.Services;

namespace Folderss
{
    /// <summary>
    /// 폴더 패널에서 고른 두 파일을 Git 창과 같은 diff 뷰(<see cref="GitDiffView"/>)로 비교하는 비모달 창.
    /// <c>git diff --no-index</c>를 쓰므로 저장소(형상 관리) 밖 파일도 비교된다(git 실행 파일은 필요).
    /// 공백 무시·대체 인코딩·보기 모드·외부 도구는 설정 창의 비교 탭(<see cref="DiffSettings"/>)을 따른다.
    /// </summary>
    public sealed class FileCompareWindow : Window
    {
        private string _oldFile;
        private string _newFile;
        private DiffSettings _settings;
        private readonly Action _openDiffSettings;
        private readonly GitDiffView _view = new GitDiffView();
        private readonly TextBlock _leftName = new TextBlock();
        private readonly TextBlock _leftFolder = new TextBlock();
        private readonly TextBlock _rightName = new TextBlock();
        private readonly TextBlock _rightFolder = new TextBlock();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

        /// <param name="openDiffSettings">외부 도구가 지정되지 않았을 때 설정 창의 비교 탭을 연다.</param>
        public FileCompareWindow(string oldFile, string newFile, DiffSettings settings, string gitExecutablePath, Action openDiffSettings)
        {
            _oldFile = oldFile;
            _newFile = newFile;
            _settings = (settings ?? new DiffSettings()).Clone();
            _openDiffSettings = openDiffSettings;
            GitCommandRunner.ConfiguredGitPath = gitExecutablePath;

            Width = 1000;
            Height = 700;
            MinWidth = 520;
            MinHeight = 320;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            SetResourceReference(BackgroundProperty, "WindowBackground");
            SetResourceReference(ForegroundProperty, "PrimaryText");
            SetResourceReference(FontFamilyProperty, "AppFontFamily");
            FontSize = 13;

            var root = new DockPanel { Margin = new Thickness(8) };
            var header = BuildHeader();
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);
            root.Children.Add(_view);
            Content = root;

            _view.ViewMode = _settings.DiffViewMode;
            _view.TextLoader = LoadTextAsync;
            _view.ViewModeChanged += async (sender, args) => await LoadAsync();
            _view.ExternalToolRequested += ExternalTool_Requested;

            UpdateHeader();
            Loaded += async (sender, args) => await LoadAsync();
            Closed += (sender, args) => _lifetime.Cancel();
        }

        /// <summary>설정 창에서 비교 설정을 저장했다. 보기 모드 기본값·공백·인코딩을 반영해 다시 읽는다.</summary>
        public async void ApplySettings(DiffSettings settings)
        {
            if (settings == null || _lifetime.IsCancellationRequested)
                return;
            var previous = _settings;
            _settings = settings.Clone();
            if (previous.DiffViewMode != _settings.DiffViewMode)
                _view.ViewMode = _settings.DiffViewMode;
            if (previous.DiffViewMode != _settings.DiffViewMode || previous.IgnoreWhitespace != _settings.IgnoreWhitespace
                || previous.FallbackEncoding != _settings.FallbackEncoding)
                await LoadAsync();
        }

        private Grid BuildHeader()
        {
            // 왼쪽 | 좌우 바꾸기 | 오른쪽 — 파일 이름(굵게)과 위치(폴더 경로, 흐리게)를 보인다.
            var grid = new Grid { Margin = new Thickness(2, 0, 2, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = BuildSide("왼쪽 (변경 전)", _leftName, _leftFolder);
            var right = BuildSide("오른쪽 (변경 후)", _rightName, _rightFolder);
            var swap = new Button
            {
                Content = "⇄ 좌우 바꾸기",
                Margin = new Thickness(10, 0, 10, 0),
                Padding = new Thickness(10, 4, 10, 4),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "왼쪽(변경 전)과 오른쪽(변경 후)을 바꿔 다시 비교합니다"
            };
            swap.Click += async (sender, args) =>
            {
                (_oldFile, _newFile) = (_newFile, _oldFile);
                UpdateHeader();
                await LoadAsync();
            };

            Grid.SetColumn(left, 0);
            Grid.SetColumn(swap, 1);
            Grid.SetColumn(right, 2);
            grid.Children.Add(left);
            grid.Children.Add(swap);
            grid.Children.Add(right);
            return grid;
        }

        private static StackPanel BuildSide(string caption, TextBlock name, TextBlock folder)
        {
            var panel = new StackPanel();
            var label = new TextBlock { Text = caption, FontSize = 11 };
            label.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            name.FontWeight = FontWeights.SemiBold;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            folder.FontSize = 12;
            folder.TextTrimming = TextTrimming.CharacterEllipsis;
            folder.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            panel.Children.Add(label);
            panel.Children.Add(name);
            panel.Children.Add(folder);
            return panel;
        }

        private void UpdateHeader()
        {
            Title = "파일 비교 - " + Path.GetFileName(_oldFile) + " ↔ " + Path.GetFileName(_newFile);
            SetSide(_leftName, _leftFolder, _oldFile);
            SetSide(_rightName, _rightFolder, _newFile);
        }

        private static void SetSide(TextBlock name, TextBlock folder, string path)
        {
            name.Text = Path.GetFileName(path);
            folder.Text = Path.GetDirectoryName(path) ?? string.Empty;
            name.ToolTip = folder.ToolTip = path;
        }

        private GitDiffRequest CreateRequest()
        {
            var diff = GitDiffCommands.Files(_oldFile, _newFile, _settings.IgnoreWhitespace);
            diff.Title = Path.GetFileName(_oldFile) + "  ↔  " + Path.GetFileName(_newFile);
            diff.OldLabel = _oldFile;
            diff.NewLabel = _newFile;
            return diff;
        }

        private async Task LoadAsync()
        {
            var diff = CreateRequest();
            var request = _view.BeginLoad(diff);
            try
            {
                _view.Complete(request, await LoadTextAsync(diff, _view.ViewMode, _lifetime.Token), diff.EmptyMessage);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                // git 없음(FileNotFoundException), 파일 접근 실패 등. async void 이벤트에서 부르므로 여기서 막는다.
                _view.Fail(request, ex.Message);
            }
        }

        /// <summary>화면과 HTML 보고서가 같이 쓰는 로더. 실패는 예외(첫 줄 오류 문구).</summary>
        private async Task<string> LoadTextAsync(GitDiffRequest diff, GitDiffViewMode mode, CancellationToken token)
        {
            var fallback = ResolveFallbackEncoding();
            var result = await GitCommandRunner.RunAsync(null, GitDiffCommands.WithViewMode(diff.Arguments, mode),
                GitCommandRunner.QueryTimeout, token, readOnly: true, fallbackEncoding: fallback);
            if (!GitDiffCommands.IsSuccess(result, diff.NoIndex))
            {
                var message = result.StdErr?.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0);
                throw new InvalidOperationException(message ?? "차이를 읽지 못했습니다.");
            }
            // UTF-16/32(BOM) 파일은 git이 바이너리로 보이므로 BOM으로 읽어 텍스트로 다시 비교해 끼운다.
            return await GitEncodingDiff.ExpandFilesAsync(_oldFile, _newFile, result.StdOut, _settings.IgnoreWhitespace, fallback, token, mode);
        }

        private async void ExternalTool_Requested(object sender, EventArgs e)
        {
            try
            {
                var run = await DiffToolLauncher.RunAsync(this, _view.CurrentRequest, _settings, null, _openDiffSettings, null, _lifetime.Token);
                if (run == null)
                    return;
                if (!run.Succeeded)
                {
                    var detail = string.IsNullOrWhiteSpace(run.Result.StdErr) ? "종료 코드 " + run.Result.ExitCode : run.Result.StdErr.Trim();
                    MessageBox.Show(this, "외부 비교 도구가 실패했습니다.\n$ " + run.Result.CommandLine + "\n\n" + detail, "외부 비교 도구",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "외부 비교 도구를 실행하지 못했습니다: " + ex.Message, "외부 비교 도구", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private System.Text.Encoding ResolveFallbackEncoding()
        {
            try
            {
                return GitTextDecoder.Resolve(_settings.FallbackEncoding);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException)
            {
                return null;   // 코드 페이지를 못 쓰는 환경이면 대체 해석 없이 UTF-8로만 읽는다.
            }
        }
    }
}

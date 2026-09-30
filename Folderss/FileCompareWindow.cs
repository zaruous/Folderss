using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Folderss.Controls;
using Folderss.Services;

namespace Folderss
{
    /// <summary>
    /// 폴더 패널에서 고른 두 파일을 Git 창과 같은 diff 뷰(<see cref="GitDiffView"/>)로 비교하는 비모달 창.
    /// <c>git diff --no-index</c>를 쓰므로 저장소 밖 파일도 비교된다(git 실행 파일은 필요). 공백 무시·대체 인코딩·보기 모드는 Git 설정을 따른다.
    /// </summary>
    public sealed class FileCompareWindow : Window
    {
        private readonly string _oldFile;
        private readonly string _newFile;
        private readonly GitSettings _settings;
        private readonly GitDiffView _view = new GitDiffView { Margin = new Thickness(8) };
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

        public FileCompareWindow(string oldFile, string newFile, GitSettings settings)
        {
            _oldFile = oldFile;
            _newFile = newFile;
            _settings = (settings ?? new GitSettings()).Clone();
            GitCommandRunner.ConfiguredGitPath = _settings.GitExecutablePath;

            Title = "파일 비교 - " + Path.GetFileName(oldFile) + " ↔ " + Path.GetFileName(newFile);
            Width = 1000;
            Height = 700;
            MinWidth = 480;
            MinHeight = 320;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            SetResourceReference(BackgroundProperty, "WindowBackground");
            SetResourceReference(ForegroundProperty, "PrimaryText");
            SetResourceReference(FontFamilyProperty, "AppFontFamily");
            FontSize = 13;

            _view.ViewMode = _settings.DiffViewMode;
            _view.ViewModeChanged += async (sender, args) => await LoadAsync();
            Content = _view;

            Loaded += async (sender, args) => await LoadAsync();
            Closed += (sender, args) => _lifetime.Cancel();
        }

        private async Task LoadAsync()
        {
            var diff = GitDiffCommands.Files(_oldFile, _newFile, _settings.IgnoreWhitespace);
            diff.Title = _oldFile + "  ↔  " + _newFile;
            var request = _view.BeginLoad(diff);
            try
            {
                var fallback = ResolveFallbackEncoding();
                var mode = _view.ViewMode;
                var result = await GitCommandRunner.RunAsync(null, GitDiffCommands.WithViewMode(diff.Arguments, mode),
                    GitCommandRunner.QueryTimeout, _lifetime.Token, readOnly: true, fallbackEncoding: fallback);
                if (GitDiffCommands.IsSuccess(result, diff.NoIndex))
                {
                    // UTF-16/32(BOM) 파일은 git이 바이너리로 보이므로 BOM으로 읽어 텍스트로 다시 비교해 끼운다.
                    var text = await GitEncodingDiff.ExpandFilesAsync(_oldFile, _newFile, result.StdOut, _settings.IgnoreWhitespace, fallback,
                        _lifetime.Token, mode);
                    _view.Complete(request, text, diff.EmptyMessage);
                }
                else
                {
                    var message = result.StdErr?.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0);
                    _view.Fail(request, message ?? "차이를 읽지 못했습니다.");
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                // git 없음(FileNotFoundException) 등. async void 이벤트에서 부르므로 여기서 막는다.
                _view.Fail(request, ex.Message);
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

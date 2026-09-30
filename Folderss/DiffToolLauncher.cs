using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Folderss.Services;

namespace Folderss
{
    /// <summary>
    /// 외부 비교 도구 실행(공용). Git 창과 두 파일 비교 창이 같은 사전 검사·안내 문구·실행 방식을 쓴다.
    /// 실행은 <c>git difftool</c>이며 도구 창을 닫을 때까지 끝나지 않으므로 시간 제한·동시 실행 제한·창 수명 토큰을 쓰지 않는다
    /// (비교 창을 닫아도 도구는 유지).
    /// </summary>
    public static class DiffToolLauncher
    {
        public const string QuickExitHint =
            "비교 도구가 바로 종료됐습니다. 도구 창이 비어 있거나 파일을 못 찾으면, 창을 닫을 때까지 기다리도록 인수를 설정하세요 " +
            "(git은 도구가 끝나면 임시 파일을 지웁니다. VS Code: --wait, WinMerge: 단일 인스턴스 옵션 끄기).";

        public sealed class Run
        {
            public GitResult Result { get; set; }
            public bool Succeeded { get; set; }
            public bool ExitedQuickly { get; set; }
        }

        /// <summary>
        /// 검사 후 실행한다. 도구가 없거나 설정이 잘못됐으면 안내만 하고 null.
        /// </summary>
        /// <param name="workingDirectory">저장소 폴더(<c>git -C</c>). 두 파일 비교는 null.</param>
        /// <param name="openDiffSettings">"도구를 지정할까요?"에 예를 누르면 설정 창의 비교 탭을 연다.</param>
        /// <param name="log">실행할 명령 줄을 알린다(Git 창의 출력 창). null이면 알리지 않는다.</param>
        public static async Task<Run> RunAsync(Window owner, GitDiffRequest request, DiffSettings settings, string workingDirectory,
            Action openDiffSettings, Action<string> log, CancellationToken queryToken)
        {
            if (request?.ExternalSelector == null || settings == null)
                return null;

            var mode = settings.DiffToolMode;
            if (mode == GitDiffToolMode.None)
            {
                if (MessageBox.Show(owner, "외부 비교 도구가 지정되어 있지 않습니다.\n설정 > 비교에서 도구를 지정할까요?", "외부 비교 도구",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    openDiffSettings?.Invoke();
                return null;
            }
            if (mode == GitDiffToolMode.Custom && !File.Exists(settings.DiffToolPath))
            {
                MessageBox.Show(owner, "지정한 비교 도구 실행 파일이 없습니다:\n" + settings.DiffToolPath + "\n\n설정 > 비교에서 경로를 확인하세요.",
                    "외부 비교 도구", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
            if (mode == GitDiffToolMode.GitConfig && !await HasConfiguredDiffToolAsync(workingDirectory, queryToken))
            {
                MessageBox.Show(owner, "git 설정에 difftool이 없습니다 (diff.tool / merge.tool).\n" +
                    "git config --global diff.tool <도구>로 설정하거나, 설정 > 비교에서 '직접 지정'을 고르세요.",
                    "외부 비교 도구", MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }

            var args = GitDiffCommands.ExternalTool(request, mode, settings.DiffToolPath, settings.DiffToolArguments);
            log?.Invoke("외부 비교 도구 실행: " + GitCommandRunner.FormatCommandLine(args));

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = await GitCommandRunner.RunAsync(workingDirectory, args, Timeout.InfiniteTimeSpan, CancellationToken.None, throttle: false);
            watch.Stop();

            // --no-index(두 파일 비교)는 차이가 있으면 difftool도 종료 코드 1이다.
            var succeeded = GitDiffCommands.IsSuccess(result, request.NoIndex) && string.IsNullOrWhiteSpace(result.StdErr);
            return new Run
            {
                Result = result,
                Succeeded = succeeded,
                ExitedQuickly = succeeded && watch.Elapsed < TimeSpan.FromSeconds(2)
            };
        }

        private static async Task<bool> HasConfiguredDiffToolAsync(string workingDirectory, CancellationToken token)
        {
            foreach (var key in new[] { "diff.tool", "merge.tool" })
            {
                var result = await GitCommandRunner.RunAsync(workingDirectory, new List<string> { "config", "--get", key },
                    GitCommandRunner.QueryTimeout, token, readOnly: true);
                if (result.Success && !string.IsNullOrWhiteSpace(result.StdOut))
                    return true;
            }
            return false;
        }
    }
}

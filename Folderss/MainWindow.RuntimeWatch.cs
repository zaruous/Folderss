using Folderss.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Folderss
{
    /// <summary>
    /// 실행 중 .NET 런타임 교체 감지 → 재시작 안내(docs/items/crash-log.md).
    /// 감지·안내 경로는 이미 로드된 것(CoreLib File.Exists, WindowsBase DispatcherTimer, PresentationFramework MessageBox)만 쓴다 —
    /// 런타임 폴더가 지워진 뒤라 새 어셈블리를 불러오면 그 자리에서 실패한다.
    /// </summary>
    public partial class MainWindow
    {
        private static readonly TimeSpan RuntimeCheckInterval = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan RuntimeRemindInterval = TimeSpan.FromMinutes(30);

        private DispatcherTimer _runtimeWatchTimer;
        private System.Collections.Generic.List<string> _runtimeWatchedFiles;
        private bool _runtimePrompting;

        /// <summary>App.OnExit가 보고 새 프로세스를 띄운다(종료 절차를 모두 거친 뒤).</summary>
        public static bool RestartRequested { get; private set; }

        private void StartRuntimeWatch()
        {
            _runtimeWatchedFiles = RuntimeWatch.WatchedFiles(new[]
            {
                typeof(object).Assembly.Location,                 // Microsoft.NETCore.App
                typeof(Window).Assembly.Location,                 // Microsoft.WindowsDesktop.App
            }, AppContext.BaseDirectory);
            if (_runtimeWatchedFiles.Count == 0)
                return; // self-contained 배포 — 시스템 업데이트 영향 없음
            // 재시작에 쓸 Process 어셈블리를 지금 불러 둔다(교체 뒤에는 새로 못 불러온다).
            using (Process.GetCurrentProcess()) { }

            _runtimeWatchTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = RuntimeCheckInterval };
            _runtimeWatchTimer.Tick += (s, e) => CheckRuntime();
            _runtimeWatchTimer.Start();
        }

        private void CheckRuntime()
        {
            if (_runtimePrompting)
                return;
            var missing = RuntimeWatch.MissingFiles(_runtimeWatchedFiles, File.Exists);
            if (missing.Count == 0)
                return;

            _runtimePrompting = true;
            _runtimeWatchTimer.Stop();
            try
            {
                var version = RuntimeWatch.VersionOf(missing[0]) ?? "알 수 없음";
                CrashLog.Write("실행 중 .NET 런타임 교체 감지(재시작 안내)", null, "사라진 런타임 파일: " + string.Join(", ", missing));
                var answer = MessageBox.Show(
                    "Folderss가 사용하던 .NET 런타임(" + version + ")이 업데이트로 교체되었습니다.\n\n" +
                    "이대로 쓰면 아직 사용하지 않은 기능을 처음 실행할 때 앱이 갑자기 종료될 수 있습니다.\n" +
                    "지금 다시 시작할까요? (저장하지 않은 문서·진행 중인 파일 작업은 종료 전에 확인합니다)\n\n" +
                    "[아니요]를 누르면 30분 뒤에 다시 알립니다.",
                    "Folderss 다시 시작 필요", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.Yes);
                if (answer == MessageBoxResult.Yes)
                {
                    RestartRequested = true;
                    ExitApp();
                    if (!_reallyClose)
                        RestartRequested = false; // 저장 확인 등에서 종료를 취소함
                    else
                        return;
                }
                _runtimeWatchTimer.Interval = RuntimeRemindInterval;
                _runtimeWatchTimer.Start();
            }
            finally
            {
                _runtimePrompting = false;
            }
        }

        /// <summary>App.OnExit에서 부른다: 같은 실행 파일을 다시 띄운다(apphost가 지금 설치된 런타임을 고른다).</summary>
        public static void LaunchRestartedInstance()
        {
            try
            {
                var exe = GetCurrentProcessExecutablePath();
                var info = new ProcessStartInfo { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory };
                if (string.Equals(Path.GetFileNameWithoutExtension(exe), "dotnet", StringComparison.OrdinalIgnoreCase))
                {
                    // dotnet Folderss.dll 로 실행한 경우
                    info.FileName = exe;
                    info.Arguments = "\"" + typeof(MainWindow).Assembly.Location + "\"";
                }
                else
                {
                    info.FileName = exe;
                }
                Process.Start(info);
            }
            catch (Exception ex)
            {
                CrashLog.Write("재시작 실패", ex, null);
                MessageBox.Show("Folderss를 자동으로 다시 시작하지 못했습니다. 직접 다시 실행하세요.\n\n" + ex.Message,
                    "Folderss", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}

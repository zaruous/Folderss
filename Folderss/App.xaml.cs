using Folderss.Services;
using System;
using System.Windows;
using System.Windows.Threading;

namespace Folderss
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // 본체 크래시 스택 기록(crash-log.txt). 테마 적용 같은 시작 단계 예외도 남도록 가장 먼저 건다.
            CrashLog.Install();
            ThemeManager.ApplySavedTheme();
            // 플러그인 코드에서 난 UI 스레드 예외만 처리해 본체가 죽지 않게 한다. 본체 예외는 기록한 뒤 기존대로 둔다(종료).
            DispatcherUnhandledException += (sender, args) =>
            {
                if (PluginManager.TryHandleUnhandled(args.Exception))
                    args.Handled = true;
                else
                    CrashLog.WriteDispatcherException(args);
            };
            // 플러그인이 본체를 끝내는 것은 막을 수 없어 원인만 기록한다. 이전 실행의 비정상 종료는 창이 뜬 뒤 알린다.
            PluginManager.InstallExitHooks(this);
            Dispatcher.BeginInvoke(new Action(PluginManager.ReportPreviousAbnormalExits), DispatcherPriority.ApplicationIdle);
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            PluginManager.OnApplicationExit();
            base.OnExit(e);
        }
    }
}

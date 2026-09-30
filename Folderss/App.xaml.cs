using Folderss.Services;
using System.Windows;

namespace Folderss
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            ThemeManager.ApplySavedTheme();
            // 플러그인 코드에서 난 UI 스레드 예외만 처리해 본체가 죽지 않게 한다. 본체 예외는 기존대로 둔다.
            DispatcherUnhandledException += (sender, args) =>
            {
                if (PluginManager.TryHandleUnhandled(args.Exception))
                    args.Handled = true;
            };
            base.OnStartup(e);
        }
    }
}

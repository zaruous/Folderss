using Folderss.Services;
using System.Windows;

namespace Folderss
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            ThemeManager.ApplySavedTheme();
            base.OnStartup(e);
        }
    }
}

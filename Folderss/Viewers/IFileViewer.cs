using Folderss.Services;
using System;
using System.Windows;

namespace Folderss.Viewers
{
    public interface IFileViewer
    {
        UIElement          View         { get; }
        ViewerCapabilities Capabilities { get; }

        void Load(string filePath);
        void ApplyTheme(AppTheme theme);
        void Export(ExportFormat format, string destPath);

        event EventHandler<string> TitleChanged;
        event EventHandler<bool>   ModifiedChanged;
    }

    [Flags]
    public enum ViewerCapabilities
    {
        ReadOnly = 0,
        Edit     = 1 << 0,
        Export   = 1 << 1,
    }

    public enum ExportFormat
    {
        Html,
        Pdf,
    }
}

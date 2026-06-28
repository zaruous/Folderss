using Folderss.Services;
using System;
using System.Windows;
using System.Windows.Input;

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

    public interface IFileOpenRequester
    {
        event EventHandler<string> FileOpenRequested;
    }

    public interface IViewerActivationAware
    {
        void SetActive(bool isActive);
    }

    public interface IViewerShortcutHandler
    {
        bool HandleShortcut(KeyEventArgs e);
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

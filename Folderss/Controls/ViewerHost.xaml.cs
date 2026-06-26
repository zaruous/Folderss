using Folderss.Services;
using Folderss.Viewers;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Folderss.Controls
{
    public partial class ViewerHost : UserControl
    {
        private readonly ViewerConfigService _viewerConfig;
        private IFileViewer _currentViewer;

        public event EventHandler<string> TitleChanged;
        public event EventHandler<bool> ModifiedChanged;
        public event EventHandler<string> FileOpenRequested;

        public ViewerHost(ViewerConfigService viewerConfig)
        {
            InitializeComponent();
            _viewerConfig = viewerConfig;
        }

        public bool CanOpen(string filePath)
        {
            var ext = Path.GetExtension(filePath);
            return _viewerConfig.Resolve(ext) != null;
        }

        public void OpenFile(string filePath)
        {
            DetachCurrentViewer();

            var ext = Path.GetExtension(filePath);
            var viewer = _viewerConfig.Resolve(ext);
            if (viewer == null)
                return;

            _currentViewer = viewer;
            viewer.TitleChanged += Viewer_TitleChanged;
            viewer.ModifiedChanged += Viewer_ModifiedChanged;
            var fileOpenRequester = viewer as IFileOpenRequester;
            if (fileOpenRequester != null)
                fileOpenRequester.FileOpenRequested += Viewer_FileOpenRequested;
            viewer.Load(filePath);
            viewer.ApplyTheme(ThemeManager.CurrentTheme);
            HostContent.Content = viewer.View;
        }

        public void ApplyTheme(AppTheme theme)
        {
            _currentViewer?.ApplyTheme(theme);
        }

        private void DetachCurrentViewer()
        {
            if (_currentViewer == null)
                return;

            _currentViewer.TitleChanged -= Viewer_TitleChanged;
            _currentViewer.ModifiedChanged -= Viewer_ModifiedChanged;
            var fileOpenRequester = _currentViewer as IFileOpenRequester;
            if (fileOpenRequester != null)
                fileOpenRequester.FileOpenRequested -= Viewer_FileOpenRequested;
            HostContent.Content = null;
            _currentViewer = null;
        }

        private void Viewer_TitleChanged(object sender, string title)
        {
            var handler = TitleChanged;
            if (handler != null)
                handler(this, title);
        }

        private void Viewer_ModifiedChanged(object sender, bool modified)
        {
            var handler = ModifiedChanged;
            if (handler != null)
                handler(this, modified);
        }

        private void Viewer_FileOpenRequested(object sender, string filePath)
        {
            var handler = FileOpenRequested;
            if (handler != null)
                handler(this, filePath);
        }
    }
}

using Folderss.Services;
using Folderss.Viewers;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Folderss.Controls
{
    public partial class ViewerHost : UserControl, IDisposable
    {
        private readonly ViewerConfigService _viewerConfig;
        private IFileViewer _currentViewer;
        private string _currentFilePath;
        private bool _isActive = true;

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

            _currentFilePath = filePath;
            _currentViewer = viewer;
            viewer.TitleChanged += Viewer_TitleChanged;
            viewer.ModifiedChanged += Viewer_ModifiedChanged;
            var fileOpenRequester = viewer as IFileOpenRequester;
            if (fileOpenRequester != null)
                fileOpenRequester.FileOpenRequested += Viewer_FileOpenRequested;
            viewer.Load(filePath);
            viewer.ApplyTheme(ThemeManager.CurrentTheme);
            var activationAware = viewer as IViewerActivationAware;
            if (activationAware != null)
                activationAware.SetActive(_isActive);
            HostContent.Content = viewer.View;
            FilePathText.Text = filePath;
        }

        public void SetActive(bool isActive)
        {
            _isActive = isActive;
            var activationAware = _currentViewer as IViewerActivationAware;
            if (activationAware != null)
                activationAware.SetActive(isActive);
        }

        public void ApplyTheme(AppTheme theme)
        {
            _currentViewer?.ApplyTheme(theme);
        }

        public bool HandleShortcut(KeyEventArgs e, KeyBindingService kb)
        {
            var shortcutHandler = _currentViewer as IViewerShortcutHandler;
            return shortcutHandler != null && shortcutHandler.HandleShortcut(e, kb);
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            if (_currentViewer == null || string.IsNullOrWhiteSpace(_currentFilePath))
                return;

            if (!File.Exists(_currentFilePath))
            {
                MessageBox.Show("파일이 존재하지 않습니다.", "Folderss", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _currentViewer.Load(_currentFilePath);
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
            var disposable = _currentViewer as IDisposable;
            if (disposable != null)
                disposable.Dispose();
            HostContent.Content = null;
            _currentViewer = null;
        }

        public void Dispose()
        {
            DetachCurrentViewer();
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

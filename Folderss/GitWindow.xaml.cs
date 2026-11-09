using System;
using System.Windows;

namespace Folderss
{
    /// <summary>
    /// Git 팝업 창. 내용은 <see cref="GitView"/>이고, 창을 닫으면 뷰의 진행 중인 작업을 끝낸다(<see cref="GitView.Shutdown"/>).
    /// 퍼스펙티브에서 "팝업 창으로 옮기기"를 하면 이미 만든 뷰를 그대로 받는다.
    /// </summary>
    public partial class GitWindow : Window
    {
        public GitView View { get; }

        public GitWindow(GitView view)
        {
            InitializeComponent();
            View = view;
            Content = view;
            Title = view.Title;
            view.TitleChanged += View_TitleChanged;
            view.CloseRequested += Close;
        }

        private void View_TitleChanged()
        {
            Title = View.Title;
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            View.TitleChanged -= View_TitleChanged;
            View.CloseRequested -= Close;
            View.Shutdown();
        }
    }
}

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;

namespace Folderss
{
    public sealed class AboutWindow : Window
    {
        public AboutWindow()
        {
            Title = "Folderss 정보";
            Width = 380;
            Height = 220;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var root = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };

            root.Children.Add(new TextBlock
            {
                Text = "Folderss 1.0",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            root.Children.Add(new TextBlock
            {
                Text = "가볍게 시작한 듀얼 패널 Windows 파일 관리자입니다.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16)
            });

            var linkBlock = new TextBlock { Margin = new Thickness(0, 0, 0, 20) };
            linkBlock.Inlines.Add(new Run("GitHub: "));
            var link = new Hyperlink(new Run("https://github.com/zaruous/Folderss"))
            {
                NavigateUri = new System.Uri("https://github.com/zaruous/Folderss")
            };
            link.RequestNavigate += (s, e) =>
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            };
            linkBlock.Inlines.Add(link);
            root.Children.Add(linkBlock);

            var ok = new Button
            {
                Content = "확인",
                IsDefault = true,
                IsCancel = true,
                MinWidth = 75,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            ok.Click += (s, e) => Close();
            root.Children.Add(ok);

            Content = root;
        }
    }
}

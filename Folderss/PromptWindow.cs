using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Folderss
{
    public sealed class PromptWindow : Window
    {
        private readonly TextBox _input;

        public string Value
        {
            get { return _input.Text; }
        }

        public PromptWindow(string title, string message, string initialValue = "")
        {
            Title = title;
            Width = 430;
            Height = 185;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock { Text = message, Margin = new Thickness(0, 0, 0, 10) };
            _input = new TextBox { Text = initialValue, MinWidth = 380 };
            _input.KeyDown += Input_KeyDown;

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            var ok = new Button { Content = "확인", IsDefault = true, MinWidth = 75 };
            ok.Click += (sender, args) => { DialogResult = true; };
            var cancel = new Button { Content = "취소", IsCancel = true, MinWidth = 75 };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            Grid.SetRow(label, 0);
            Grid.SetRow(_input, 1);
            Grid.SetRow(buttons, 2);
            root.Children.Add(label);
            root.Children.Add(_input);
            root.Children.Add(buttons);
            Content = root;

            Loaded += (sender, args) =>
            {
                _input.Focus();
                _input.SelectAll();
            };
        }

        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                DialogResult = true;
        }
    }
}

using Folderss.Models;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Folderss
{
    public sealed class KeyCaptureWindow : Window
    {
        public Key CapturedKey { get; private set; } = Key.None;
        public ModifierKeys CapturedModifiers { get; private set; } = ModifierKeys.None;

        private TextBlock _keyDisplay;
        private Button _okButton;

        public KeyCaptureWindow()
        {
            Title = "단축키 변경";
            Width = 320;
            Height = 200;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var instr = new TextBlock
            {
                Text = "새 단축키 조합을 입력하세요:",
                Margin = new Thickness(20, 18, 20, 8),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(instr, 0);
            root.Children.Add(instr);

            var keyBorder = new Border
            {
                Margin = new Thickness(20, 4, 20, 4),
                Height = 44,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4)
            };
            keyBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            keyBorder.SetResourceReference(Border.BackgroundProperty, "ControlBackground");

            _keyDisplay = new TextBlock
            {
                Text = "키 입력 대기 중...",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 14
            };
            _keyDisplay.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            keyBorder.Child = _keyDisplay;
            Grid.SetRow(keyBorder, 1);
            root.Children.Add(keyBorder);

            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(20, 12, 20, 16)
            };

            _okButton = new Button
            {
                Content = "확인",
                MinWidth = 75,
                IsDefault = true,
                IsEnabled = false,
                Margin = new Thickness(0, 0, 8, 0)
            };
            _okButton.Click += (s, e) => { DialogResult = true; Close(); };

            var cancelBtn = new Button { Content = "취소", MinWidth = 75, IsCancel = true };
            cancelBtn.Click += (s, e) => Close();

            btnRow.Children.Add(_okButton);
            btnRow.Children.Add(cancelBtn);
            Grid.SetRow(btnRow, 2);
            root.Children.Add(btnRow);

            Content = root;
            PreviewKeyDown += Capture_PreviewKeyDown;
            Loaded += (s, e) => Focus();
        }

        private void Capture_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Resolve the actual key (Alt combos come in as Key.System)
            var key = (e.Key == Key.System) ? e.SystemKey : e.Key;

            // Ignore standalone modifier keys
            if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LWin || key == Key.RWin ||
                key == Key.System || key == Key.None)
                return;

            var mods = Keyboard.Modifiers;
            CapturedKey = key;
            CapturedModifiers = mods;

            var parts = new List<string>();
            if ((mods & ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((mods & ModifierKeys.Alt) != 0) parts.Add("Alt");
            if ((mods & ModifierKeys.Shift) != 0) parts.Add("Shift");
            parts.Add(KeyBindingEntry.KeyToString(key));

            _keyDisplay.Text = string.Join("+", parts);
            _keyDisplay.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
            _okButton.IsEnabled = true;
            e.Handled = true;
        }
    }
}

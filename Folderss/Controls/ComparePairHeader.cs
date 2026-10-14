using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Folderss.Controls
{
    /// <summary>
    /// 비교 창 위쪽의 "왼쪽 | ⇄ 좌우 바꾸기 | 오른쪽" 머리글. 각 쪽은 캡션, 이름(굵게), 위치(상위 폴더, 흐리게)를 보인다.
    /// 두 파일 비교 창과 폴더 비교 창이 같이 쓴다. 좌우를 실제로 바꾸는 것은 <see cref="SwapRequested"/>를 받는 쪽이다.
    /// <see cref="CanBrowse"/>를 켜면 각 쪽에 <c>…</c> 버튼이 생기고 <see cref="BrowseRequested"/>(true = 왼쪽)를 올린다.
    /// </summary>
    public sealed class ComparePairHeader : Grid
    {
        private readonly Side _left;
        private readonly Side _right;

        public event EventHandler SwapRequested;
        public event EventHandler<bool> BrowseRequested;

        public ComparePairHeader(string leftCaption, string rightCaption, string swapToolTip)
        {
            Margin = new Thickness(2, 0, 2, 8);
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _left = new Side(leftCaption, () => BrowseRequested?.Invoke(this, true));
            _right = new Side(rightCaption, () => BrowseRequested?.Invoke(this, false));
            var swap = new Button
            {
                Content = "⇄ 좌우 바꾸기",
                Margin = new Thickness(10, 0, 10, 0),
                Padding = new Thickness(10, 4, 10, 4),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = swapToolTip
            };
            swap.Click += (sender, args) => SwapRequested?.Invoke(this, EventArgs.Empty);

            SetColumn(_left.Root, 0);
            SetColumn(swap, 1);
            SetColumn(_right.Root, 2);
            Children.Add(_left.Root);
            Children.Add(swap);
            Children.Add(_right.Root);
        }

        /// <summary>각 쪽 이름 옆 <c>…</c>(경로 고르기) 버튼을 보인다.</summary>
        public bool CanBrowse
        {
            get { return _left.Browse.Visibility == Visibility.Visible; }
            set { _left.Browse.Visibility = _right.Browse.Visibility = value ? Visibility.Visible : Visibility.Collapsed; }
        }

        /// <summary>경로가 비어 있으면 그 쪽에 <paramref name="emptyText"/>를 보인다.</summary>
        public void SetPaths(string left, string right, string emptyText = "(선택 안 함)")
        {
            _left.Set(left, emptyText);
            _right.Set(right, emptyText);
        }

        private sealed class Side
        {
            public readonly DockPanel Root = new DockPanel();
            public readonly Button Browse;
            private readonly TextBlock _name = new TextBlock();
            private readonly TextBlock _folder = new TextBlock();

            public Side(string caption, Action browse)
            {
                Browse = new Button
                {
                    Content = "…",
                    MinWidth = 0,
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "다른 폴더 고르기",
                    Visibility = Visibility.Collapsed
                };
                Browse.Click += (sender, args) => browse();
                DockPanel.SetDock(Browse, Dock.Right);
                Root.Children.Add(Browse);

                var text = new StackPanel();
                var label = new TextBlock { Text = caption, FontSize = 11 };
                label.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
                _name.FontWeight = FontWeights.SemiBold;
                _name.TextTrimming = TextTrimming.CharacterEllipsis;
                _folder.FontSize = 12;
                _folder.TextTrimming = TextTrimming.CharacterEllipsis;
                _folder.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
                text.Children.Add(label);
                text.Children.Add(_name);
                text.Children.Add(_folder);
                Root.Children.Add(text);
            }

            public void Set(string path, string emptyText)
            {
                if (string.IsNullOrEmpty(path))
                {
                    _name.Text = emptyText;
                    _folder.Text = string.Empty;
                    _name.ToolTip = _folder.ToolTip = null;
                    return;
                }
                var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var name = Path.GetFileName(trimmed);
                // 드라이브 루트(C:\)는 이름이 없으니 경로 자체를 이름으로 보인다.
                _name.Text = string.IsNullOrEmpty(name) ? path : name;
                _folder.Text = string.IsNullOrEmpty(name) ? string.Empty : Path.GetDirectoryName(trimmed) ?? string.Empty;
                _name.ToolTip = _folder.ToolTip = path;
            }
        }
    }
}

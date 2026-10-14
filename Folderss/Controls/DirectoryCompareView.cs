using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Folderss.Models;
using Folderss.Services;

namespace Folderss.Controls
{
    /// <summary>
    /// 폴더 비교 결과(<see cref="CompareResult"/>) 목록. 들여쓴 상대 경로, 비교 상태(색 배지), 양쪽 크기·수정 시각, 사유를 보인다.
    /// 상태 색은 행 배경이 아니라 비교 칸 배지에만 칠한다 — 행 배경을 바꾸면 선택·hover 표시를 덮는다.
    /// 동작(파일 비교 열기, 복사, 탐색기)은 이벤트로만 올리고 처리는 담는 쪽(창)이 한다.
    /// </summary>
    public sealed class DirectoryCompareView : ListView
    {
        private static readonly Brush DifferentBrush = Frozen(0x55, 0xE5, 0xC0, 0x7B);
        private static readonly Brush LeftOnlyBrush = Frozen(0x55, 0x98, 0xC3, 0x79);
        private static readonly Brush RightOnlyBrush = Frozen(0x55, 0x61, 0xAF, 0xEF);
        private static readonly Brush MismatchBrush = Frozen(0x66, 0xE0, 0x6C, 0x75);
        private static readonly Brush UnknownBrush = Frozen(0x44, 0x88, 0x88, 0x88);

        private readonly List<Row> _rows = new List<Row>();
        private readonly MenuItem _compareItem;
        private readonly MenuItem _copyRightItem;
        private readonly MenuItem _copyLeftItem;
        private readonly MenuItem _revealLeftItem;
        private readonly MenuItem _revealRightItem;
        private CompareResult _result;
        private bool _showDifferencesOnly;

        /// <summary>양쪽에 다 있는 파일을 두 파일 비교로 열고 싶다(더블클릭·Enter·메뉴).</summary>
        public event EventHandler<CompareEntry> CompareRequested;
        /// <summary>선택 항목을 복사하고 싶다(방향은 <see cref="CopyRequestEventArgs.ToRight"/>).</summary>
        public event EventHandler<CopyRequestEventArgs> CopyRequested;
        /// <summary>항목 위치를 탐색기로 열고 싶다.</summary>
        public event EventHandler<RevealRequestEventArgs> RevealRequested;

        public DirectoryCompareView()
        {
            SelectionMode = SelectionMode.Extended;
            VirtualizingPanel.SetIsVirtualizing(this, true);
            VirtualizingPanel.SetVirtualizationMode(this, VirtualizationMode.Recycling);

            var grid = new GridView();
            grid.Columns.Add(new GridViewColumn { Header = "이름", Width = 320, CellTemplate = NameTemplate() });
            grid.Columns.Add(new GridViewColumn { Header = "비교", Width = 100, CellTemplate = StatusTemplate() });
            grid.Columns.Add(TextColumn("왼쪽 크기", nameof(Row.LeftSize), 80, TextAlignment.Right));
            grid.Columns.Add(TextColumn("왼쪽 수정한 날짜", nameof(Row.LeftModified), 130, TextAlignment.Left));
            grid.Columns.Add(TextColumn("오른쪽 크기", nameof(Row.RightSize), 80, TextAlignment.Right));
            grid.Columns.Add(TextColumn("오른쪽 수정한 날짜", nameof(Row.RightModified), 130, TextAlignment.Left));
            grid.Columns.Add(TextColumn("사유", nameof(Row.Reason), 260, TextAlignment.Left));
            View = grid;

            ItemsSource = _rows;
            CollectionViewSource.GetDefaultView(_rows).Filter = item => !_showDifferencesOnly || ((Row)item).Entry.Status != CompareStatus.Same;

            var menu = new ContextMenu();
            _compareItem = AddMenu(menu, "두 파일 비교", (s, e) => RaiseCompare(SelectedEntry));
            menu.Items.Add(new Separator());
            _copyRightItem = AddMenu(menu, "왼쪽 → 오른쪽 복사", (s, e) => RaiseCopy(true));
            _copyLeftItem = AddMenu(menu, "오른쪽 → 왼쪽 복사", (s, e) => RaiseCopy(false));
            menu.Items.Add(new Separator());
            _revealLeftItem = AddMenu(menu, "왼쪽 위치 탐색기로 열기", (s, e) => RevealRequested?.Invoke(this, new RevealRequestEventArgs(SelectedEntry, true)));
            _revealRightItem = AddMenu(menu, "오른쪽 위치 탐색기로 열기", (s, e) => RevealRequested?.Invoke(this, new RevealRequestEventArgs(SelectedEntry, false)));
            menu.Opened += (s, e) => UpdateMenu();
            ContextMenu = menu;

            MouseDoubleClick += (s, e) =>
            {
                if (ItemsControl.ContainerFromElement(this, e.OriginalSource as DependencyObject) is ListViewItem)
                    RaiseCompare(SelectedEntry);
            };
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    RaiseCompare(SelectedEntry);
                    e.Handled = true;
                }
            };
        }

        public CompareResult Result
        {
            get { return _result; }
            set
            {
                _result = value;
                _rows.Clear();
                if (value != null)
                    _rows.AddRange(value.Entries.Select(entry => new Row(entry)));
                CollectionViewSource.GetDefaultView(_rows).Refresh();
            }
        }

        /// <summary>같음 항목을 숨긴다. 같은 폴더(자식이 모두 같음)도 함께 숨는다.</summary>
        public bool ShowDifferencesOnly
        {
            get { return _showDifferencesOnly; }
            set
            {
                _showDifferencesOnly = value;
                CollectionViewSource.GetDefaultView(_rows).Refresh();
            }
        }

        public IReadOnlyList<CompareEntry> SelectedEntries => SelectedItems.Cast<Row>().Select(row => row.Entry).ToList();

        private CompareEntry SelectedEntry => SelectedItems.Count == 1 ? ((Row)SelectedItem).Entry : null;

        public static bool CanCompareFiles(CompareEntry entry)
            => entry != null && !entry.IsDirectory && entry.LeftExists && entry.RightExists && entry.Status != CompareStatus.TypeMismatch;

        private void RaiseCompare(CompareEntry entry)
        {
            if (CanCompareFiles(entry))
                CompareRequested?.Invoke(this, entry);
        }

        private void RaiseCopy(bool toRight)
        {
            var entries = SelectedEntries;
            if (entries.Count > 0)
                CopyRequested?.Invoke(this, new CopyRequestEventArgs(entries, toRight));
        }

        private void UpdateMenu()
        {
            var entries = SelectedEntries;
            var single = SelectedEntry;
            _compareItem.IsEnabled = CanCompareFiles(single);
            _copyRightItem.IsEnabled = entries.Any(entry => DirectoryCompareService.CanCopy(entry, true, out _));
            _copyLeftItem.IsEnabled = entries.Any(entry => DirectoryCompareService.CanCopy(entry, false, out _));
            _revealLeftItem.IsEnabled = single != null && single.LeftExists;
            _revealRightItem.IsEnabled = single != null && single.RightExists;
        }

        private static MenuItem AddMenu(ContextMenu menu, string header, RoutedEventHandler click)
        {
            var item = new MenuItem { Header = header };
            item.Click += click;
            menu.Items.Add(item);
            return item;
        }

        private static DataTemplate NameTemplate()
        {
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding(nameof(Row.DisplayName)));
            text.SetBinding(FrameworkElement.MarginProperty, new Binding(nameof(Row.Indent)));
            text.SetBinding(FrameworkElement.ToolTipProperty, new Binding("Entry." + nameof(CompareEntry.RelativePath)));
            text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            return new DataTemplate { VisualTree = text };
        }

        private static DataTemplate StatusTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetBinding(Border.BackgroundProperty, new Binding(nameof(Row.StatusBrush)));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            border.SetValue(Border.PaddingProperty, new Thickness(6, 1, 6, 1));
            border.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding(nameof(Row.StatusText)));
            border.AppendChild(text);
            return new DataTemplate { VisualTree = border };
        }

        private static GridViewColumn TextColumn(string header, string path, double width, TextAlignment alignment)
        {
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding(path));
            text.SetValue(TextBlock.TextAlignmentProperty, alignment);
            text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            if (path == nameof(Row.Reason))
                text.SetBinding(FrameworkElement.ToolTipProperty, new Binding(path));
            return new GridViewColumn { Header = header, Width = width, CellTemplate = new DataTemplate { VisualTree = text } };
        }

        private static Brush Frozen(byte a, byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }

        /// <summary>표시용 행. 결과가 바뀌면 통째로 다시 만든다(항목 상태는 행 수명 동안 바뀌지 않는다).</summary>
        public sealed class Row
        {
            public Row(CompareEntry entry)
            {
                Entry = entry;
            }

            public CompareEntry Entry { get; }
            public string DisplayName => (Entry.IsDirectory ? "📁 " : "📄 ") + Entry.Name;
            public Thickness Indent => new Thickness(Entry.Depth * 16, 0, 0, 0);
            public string LeftSize => FormatSize(Entry.LeftSize);
            public string RightSize => FormatSize(Entry.RightSize);
            public string LeftModified => FormatTime(Entry.LeftModified);
            public string RightModified => FormatTime(Entry.RightModified);
            public string Reason => Entry.Reason;

            public string StatusText
            {
                get
                {
                    switch (Entry.Status)
                    {
                        case CompareStatus.Same: return "= 같음";
                        case CompareStatus.Different: return "≠ 다름";
                        case CompareStatus.LeftOnly: return "◀ 왼쪽만";
                        case CompareStatus.RightOnly: return "▶ 오른쪽만";
                        case CompareStatus.TypeMismatch: return "⚠ 유형 다름";
                        default: return "? 비교 불가";
                    }
                }
            }

            public Brush StatusBrush
            {
                get
                {
                    switch (Entry.Status)
                    {
                        case CompareStatus.Different: return DifferentBrush;
                        case CompareStatus.LeftOnly: return LeftOnlyBrush;
                        case CompareStatus.RightOnly: return RightOnlyBrush;
                        case CompareStatus.TypeMismatch: return MismatchBrush;
                        case CompareStatus.Unknown: return UnknownBrush;
                        default: return Brushes.Transparent;
                    }
                }
            }

            private static string FormatTime(DateTime? time) => time?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;

            private static string FormatSize(long? size)
            {
                if (size == null)
                    return string.Empty;
                string[] units = { "B", "KB", "MB", "GB", "TB" };
                double value = size.Value;
                var unit = 0;
                while (value >= 1024 && unit < units.Length - 1)
                {
                    value /= 1024;
                    unit++;
                }
                return unit == 0 ? value.ToString("0") + " " + units[unit] : value.ToString("0.##") + " " + units[unit];
            }
        }
    }

    public sealed class CopyRequestEventArgs : EventArgs
    {
        public CopyRequestEventArgs(IReadOnlyList<CompareEntry> entries, bool toRight)
        {
            Entries = entries;
            ToRight = toRight;
        }

        public IReadOnlyList<CompareEntry> Entries { get; }
        public bool ToRight { get; }
    }

    public sealed class RevealRequestEventArgs : EventArgs
    {
        public RevealRequestEventArgs(CompareEntry entry, bool left)
        {
            Entry = entry;
            Left = left;
        }

        public CompareEntry Entry { get; }
        public bool Left { get; }
    }
}

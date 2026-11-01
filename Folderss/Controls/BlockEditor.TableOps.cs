using Folderss.Models;
using Folderss.Services;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Folderss.Controls
{
    public sealed partial class BlockEditor
    {
        // ── 표 조작(#75): 셀 메뉴·열 너비·행열 끌어 옮기기·머리글 표시 ─────────────
        //
        // 표 전체 속성은 표의 첫 행 블록에 둔다(BlockTable): 열 정렬 TableAlign(Markdown 구분 행), 열 너비 colwidths·머리글 끄기 noheader(사이드카).
        // 연산은 모두 블록 목록을 바꾼 뒤 SyncRows → RecordChange 한 번 — 실행 취소 한 단계이고 기록은 바뀐 구간(그 표)만 남는다.

        internal enum TableDragKind
        {
            Resize,
            Column,
            Row,
        }

        private sealed class TableDrag
        {
            public TableDragKind Kind;
            public BlockRow Row;
            public UIElement Grip;
            public int Column;
            public EditorBlock Header;
            public int Start;
            public int End;
            public double StartX;
            public double StartWidth;
            public int Target = -1;
            public BlockRow HintRow;
        }

        private TableDrag _tableDrag;

        private bool IsTableDragging => _tableDrag != null;

        /// <summary>표 행에 표 정보(열 수·첫 행·머리글 표시·정렬·마지막 행·열 너비)를 준다. <see cref="ApplyRowContext"/>에서 부른다.</summary>
        private void ApplyTableContext(BlockRow row, int index)
        {
            var header = _blocks[_tableStart[index]];
            var first = _tableHeader[index];
            row.SetTable(_tableColumns[index], first, first && !BlockTable.IsHeaderHidden(header), header.TableAlign, _tableLast[index],
                header.GetAttribute(EditorBlock.AttrColWidths));
        }

        /// <summary>표 [시작, 끝)과 첫 행(표 전체 속성이 있는 블록). 표 행이 아니면 false.</summary>
        private bool TryGetTable(int index, out int start, out int end, out EditorBlock header)
        {
            start = end = index;
            header = null;
            if (index < 0 || index >= _blocks.Count || _blocks[index].Kind != BlockKind.TableRow)
                return false;
            GetTableRange(index, out start, out end);
            header = _blocks[start];
            return true;
        }

        // ── 셀 메뉴 ───────────────────────────────────────────────────────

        /// <summary>셀 오른쪽 클릭 메뉴.</summary>
        private void ShowTableCellMenu(BlockRow row, int column, UIElement target)
        {
            int start, end;
            EditorBlock header;
            if (!TryGetTable(IndexOf(row), out start, out end, out header))
                return;
            var columns = BlockTable.ColumnCount(_blocks, start, end);
            // 메뉴 항목은 누를 때 위치를 다시 구한다(메뉴가 떠 있는 동안 문서가 바뀔 수 있음).
            Func<int> at = () => IndexOf(row);

            var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.MousePoint };
            menu.Items.Add(CreateMenuItem("위에 행 삽입", string.Empty, () => InsertTableRowAt(at(), true, column)));
            menu.Items.Add(CreateMenuItem("아래에 행 삽입", "Enter", () => InsertTableRowAt(at(), false, column)));
            menu.Items.Add(CreateMenuItem("왼쪽에 열 삽입", string.Empty, () => InsertTableColumnAt(at(), column)));
            menu.Items.Add(CreateMenuItem("오른쪽에 열 삽입", string.Empty, () => InsertTableColumnAt(at(), column + 1)));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("행 삭제", string.Empty, () => DeleteTableRowAt(at(), column)));
            var deleteColumn = CreateMenuItem("열 삭제", string.Empty, () => DeleteTableColumnAt(at(), column));
            deleteColumn.IsEnabled = columns > 1;
            menu.Items.Add(deleteColumn);
            menu.Items.Add(new Separator());

            var current = BlockTable.GetColumnAlign(header, column);
            var alignMenu = new MenuItem { Header = "열 정렬" };
            alignMenu.Items.Add(CreateAlignItem("왼쪽", BlockTable.AlignLeft, current, at, column));
            alignMenu.Items.Add(CreateAlignItem("가운데", BlockTable.AlignCenter, current, at, column));
            alignMenu.Items.Add(CreateAlignItem("오른쪽", BlockTable.AlignRight, current, at, column));
            menu.Items.Add(alignMenu);
            if (BlockTable.ParseWidths(header.GetAttribute(EditorBlock.AttrColWidths), columns).Any(w => w > 0))
                menu.Items.Add(CreateMenuItem("열 너비 모두 자동", string.Empty, () => ResetTableColumnWidths(at())));
            var headerItem = CreateMenuItem("머리글 행", string.Empty, () => ToggleTableHeader(at()));
            headerItem.IsCheckable = false;
            headerItem.IsChecked = !BlockTable.IsHeaderHidden(header);
            menu.Items.Add(headerItem);
            menu.IsOpen = true;
        }

        private MenuItem CreateAlignItem(string title, string align, string current, Func<int> at, int column)
        {
            var item = CreateMenuItem(title, string.Empty, () => SetTableColumnAlign(at(), column, align));
            // 정렬을 정하지 않은 열(기본)은 왼쪽으로 표시된다
            item.IsChecked = current == align || (align == BlockTable.AlignLeft && current.Length == 0);
            return item;
        }

        /// <summary>행 위·아래에 빈 행을 넣는다. 첫 행 위에 넣으면 새 행이 머리글이 된다(표 전체 속성이 따라감).</summary>
        private void InsertTableRowAt(int index, bool above, int column)
        {
            int start, end;
            EditorBlock header;
            if (!TryGetTable(index, out start, out end, out header))
                return;
            var row = _blocks[index];
            var columns = BlockTable.ColumnCount(_blocks, start, end);
            var insertAt = above ? index : index + 1;
            var created = InColumnOf(new EditorBlock(BlockKind.TableRow, BlockMarkdown.JoinCells(Enumerable.Repeat(string.Empty, columns)), row.Indent), row);
            BlockTable.InsertRow(_blocks, start, insertAt, created);
            SyncRows();
            FocusRow(insertAt, 0, column);
            RecordChange();
        }

        private void DeleteTableRowAt(int index, int column)
        {
            if (index >= 0 && index < _blocks.Count && _blocks[index].Kind == BlockKind.TableRow)
                DeleteTableRow(index, column);
        }

        /// <summary><paramref name="at"/>번 자리에 빈 열을 넣고 그 셀로 커서를 옮긴다.</summary>
        private void InsertTableColumnAt(int index, int at)
        {
            int start, end;
            EditorBlock header;
            if (!TryGetTable(index, out start, out end, out header))
                return;
            BlockTable.InsertColumn(_blocks, start, end, at);
            SyncRows();
            FocusRow(index, 0, at);
            RecordChange();
        }

        private void DeleteTableColumnAt(int index, int column)
        {
            int start, end;
            EditorBlock header;
            if (!TryGetTable(index, out start, out end, out header))
                return;
            if (!BlockTable.DeleteColumn(_blocks, start, end, column))
                return;
            SyncRows();
            FocusRow(index, int.MaxValue, Math.Max(0, Math.Min(column, BlockTable.ColumnCount(_blocks, start, end) - 1)));
            RecordChange();
        }

        private void SetTableColumnAlign(int index, int column, string align)
        {
            int start, end;
            EditorBlock header;
            if (!TryGetTable(index, out start, out end, out header))
                return;
            BlockTable.SetColumnAlign(header, BlockTable.ColumnCount(_blocks, start, end), column, align);
            SyncRows();
            RecordChange();
        }

        private void ToggleTableHeader(int index)
        {
            int start, end;
            EditorBlock header;
            if (!TryGetTable(index, out start, out end, out header))
                return;
            BlockTable.SetHeaderHidden(header, !BlockTable.IsHeaderHidden(header));
            SyncRows();
            RecordChange();
        }

        private void ResetTableColumnWidths(int index)
        {
            int start, end;
            EditorBlock header;
            if (!TryGetTable(index, out start, out end, out header))
                return;
            header.SetAttribute(EditorBlock.AttrColWidths, null);
            SyncRows();
            RecordChange();
        }

        /// <summary>열 경계 두 번 클릭: 그 열 너비를 자동으로.</summary>
        private void ResetTableColumnWidth(BlockRow row, int column)
        {
            int start, end;
            EditorBlock header;
            if (!TryGetTable(IndexOf(row), out start, out end, out header))
                return;
            BlockTable.SetColumnWidth(header, BlockTable.ColumnCount(_blocks, start, end), column, 0);
            SyncRows();
            RecordChange();
        }

        /// <summary>표 행을 표 안 다른 자리로 옮긴다(첫 행이 바뀌면 표 전체 속성은 새 첫 행으로).</summary>
        private void MoveTableRow(int from, int to)
        {
            int start, end;
            EditorBlock header;
            if (!TryGetTable(from, out start, out end, out header) || !BlockTable.MoveRow(_blocks, start, end, from, to))
                return;
            SyncRows();
            FocusRow(to, int.MaxValue, 0);
            RecordChange();
        }

        private void MoveTableColumn(int index, int from, int to)
        {
            int start, end;
            EditorBlock header;
            if (!TryGetTable(index, out start, out end, out header) || !BlockTable.MoveColumn(_blocks, start, end, from, to))
                return;
            SyncRows();
            FocusRow(index, int.MaxValue, to);
            RecordChange();
        }

        // ── 끌기: 열 너비·열 옮기기·행 옮기기 ──────────────────────────────────

        private void BeginTableDrag(BlockRow row, int column, UIElement grip, TableDragKind kind, MouseButtonEventArgs e)
        {
            int start, end;
            EditorBlock header;
            if (_tableDrag != null || column < 0 || !TryGetTable(IndexOf(row), out start, out end, out header))
                return;
            _tableDrag = new TableDrag
            {
                Kind = kind,
                Row = row,
                Grip = grip,
                Column = column,
                Header = header,
                Start = start,
                End = end,
                StartX = e.GetPosition(this).X,
                StartWidth = row.CellWidth(column),
            };
            grip.CaptureMouse();
        }

        private void ContinueTableDrag(UIElement grip, MouseEventArgs e)
        {
            var drag = _tableDrag;
            if (drag == null || !ReferenceEquals(drag.Grip, grip) || e.LeftButton != MouseButtonState.Pressed)
                return;
            switch (drag.Kind)
            {
                case TableDragKind.Resize:
                    // 끄는 동안은 첫 행 속성만 바꾸고 화면에 있는 그 표의 행만 다시 그린다(기록은 놓을 때 한 번)
                    var width = Math.Max(BlockTable.MinColumnWidth, drag.StartWidth + e.GetPosition(this).X - drag.StartX);
                    BlockTable.SetColumnWidth(drag.Header, BlockTable.ColumnCount(_blocks, drag.Start, drag.End), drag.Column, width);
                    foreach (var realized in _panel.RealizedRows.ToList())
                    {
                        var index = IndexOf(realized);
                        if (index >= drag.Start && index < drag.End && realized.Block.Kind == BlockKind.TableRow)
                            ApplyRowContext(realized, index);
                    }
                    break;

                case TableDragKind.Column:
                    var column = drag.Row.ColumnAtX(e.GetPosition(drag.Row.Root).X);
                    SetTableDropHint(drag, drag.Row, column);
                    break;

                case TableDragKind.Row:
                    BlockRow over = null;
                    var target = -1;
                    foreach (var realized in _panel.RealizedRows)
                    {
                        var index = IndexOf(realized);
                        if (index < drag.Start || index >= drag.End)
                            continue;
                        var y = e.GetPosition(realized.Root).Y;
                        if (y >= 0 && y < realized.Root.ActualHeight)
                        {
                            over = realized;
                            target = index;
                            break;
                        }
                    }
                    if (over != null)
                        SetTableDropHint(drag, over, -1, target);
                    break;
            }
        }

        private void SetTableDropHint(TableDrag drag, BlockRow row, int column, int target = -1)
        {
            if (drag.HintRow != null && !ReferenceEquals(drag.HintRow, row))
                drag.HintRow.SetCellDropHint(null);
            drag.HintRow = row;
            drag.Target = drag.Kind == TableDragKind.Column ? column : target;
            row.SetCellDropHint(column);
        }

        /// <summary>끌기를 끝낸다. <paramref name="apply"/>가 false(캡처를 잃음)면 열·행 옮기기는 취소하고 너비는 지금 값으로 남긴다.</summary>
        private void EndTableDrag(UIElement grip, bool apply)
        {
            var drag = _tableDrag;
            if (drag == null || !ReferenceEquals(drag.Grip, grip))
                return;
            _tableDrag = null;
            if (grip.IsMouseCaptured)
                grip.ReleaseMouseCapture();
            if (drag.HintRow != null)
                drag.HintRow.SetCellDropHint(null);

            switch (drag.Kind)
            {
                case TableDragKind.Resize:
                    SyncRows();
                    RecordChange();
                    break;
                case TableDragKind.Column:
                    if (apply && drag.Target >= 0)
                        MoveTableColumn(IndexOf(drag.Row), drag.Column, drag.Target);
                    break;
                case TableDragKind.Row:
                    if (apply && drag.Target >= 0)
                        MoveTableRow(IndexOf(drag.Row), drag.Target);
                    break;
            }
        }
    }
}

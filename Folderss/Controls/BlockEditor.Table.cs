using Folderss.Models;
using Folderss.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace Folderss.Controls
{
    public sealed partial class BlockEditor
    {
        // ── 표 ─────────────────────────────────────────────────────────────
        //
        // 표는 행마다 블록(BlockKind.TableRow, 셀은 탭 구분)이다. 행 추가·삭제·이동은 블록 연산과 같고,
        // 가상화 패널이 화면에 보이는 행만 만들므로 행이 수천 개인 표도 행 추가·입력 비용이 문서 크기와 무관하다.

        /// <summary>
        /// 표 셀 키: Tab/Shift+Tab 다음·이전 셀(마지막 셀에서 Tab은 행 추가), Enter 아래에 행 추가, ↑↓ 같은 열의 위·아래 행,
        /// ←→ 셀 경계에서 이웃 셀, 빈 행 첫 셀 맨 앞 Backspace는 행 삭제. 처리하지 않은 키는 일반 블록 키로 넘긴다.
        /// </summary>
        private bool HandleTableKey(BlockRow row, Key key, ModifierKeys mods)
        {
            var editor = row.Editor;
            var column = row.ActiveColumn;
            var index = IndexOf(row);
            switch (key)
            {
                case Key.Tab:
                    if (mods == ModifierKeys.None)
                    {
                        if (column < row.CellCount - 1)
                            FocusEditor(row.EditorForColumn(column + 1), int.MaxValue);
                        else if (IsSameTable(index, index + 1))
                            FocusRow(index + 1, int.MaxValue, 0);
                        else
                            InsertTableRow(index + 1, 0);
                        return true;
                    }
                    if (mods == ModifierKeys.Shift)
                    {
                        if (column > 0)
                            FocusEditor(row.EditorForColumn(column - 1), int.MaxValue);
                        else if (IsSameTable(index, index - 1))
                            FocusRow(index - 1, int.MaxValue, int.MaxValue);
                        return true;
                    }
                    return false;

                case Key.Enter:
                    if (mods == ModifierKeys.None)
                    {
                        InsertTableRow(index + 1, column);
                        return true;
                    }
                    // 셀은 한 줄. Shift+Enter 줄바꿈은 막는다.
                    return mods == ModifierKeys.Shift;

                case Key.Up:
                case Key.Down:
                    var direction = key == Key.Up ? -1 : 1;
                    if (mods == ModifierKeys.None && (direction < 0 ? IsOnFirstLine(editor) : IsOnLastLine(editor)))
                    {
                        FocusVertical(row, direction);
                        return true;
                    }
                    return false;

                case Key.Left:
                    if (mods == ModifierKeys.None && editor.SelectionLength == 0 && editor.CaretIndex == 0 && column > 0)
                    {
                        FocusEditor(row.EditorForColumn(column - 1), int.MaxValue);
                        return true;
                    }
                    return false;

                case Key.Right:
                    if (mods == ModifierKeys.None && editor.SelectionLength == 0 && editor.CaretIndex == editor.Text.Length && column < row.CellCount - 1)
                    {
                        FocusEditor(row.EditorForColumn(column + 1), 0);
                        return true;
                    }
                    return false;

                case Key.Back:
                    if (mods != ModifierKeys.None || editor.SelectionLength != 0 || editor.CaretIndex != 0)
                        return false;
                    if (row.JoinedCells().Replace(EditorBlock.CellSeparator.ToString(), string.Empty).Trim().Length == 0)
                        DeleteTableRow(index, column);
                    else if (column > 0)
                        FocusEditor(row.EditorForColumn(column - 1), int.MaxValue);
                    // 첫 셀 맨 앞에서는 앞 블록과 합치지 않는다.
                    return true;

                case Key.Delete:
                    // 셀 끝에서 다음 블록을 합치지 않는다.
                    return mods == ModifierKeys.None && editor.SelectionLength == 0 && editor.CaretIndex == editor.Text.Length;
            }
            return false;
        }

        private bool IsSameTable(int a, int b)
        {
            return a >= 0 && b >= 0 && a < _blocks.Count && b < _blocks.Count &&
                   _blocks[a].Kind == BlockKind.TableRow && _blocks[b].Kind == BlockKind.TableRow &&
                   _blocks[a].Indent == _blocks[b].Indent && IsVisibleIndex(b);
        }

        /// <summary><paramref name="at"/> 위치(바로 위가 표 행)에 빈 행을 넣고 <paramref name="column"/> 셀에 커서를 둔다.</summary>
        private void InsertTableRow(int at, int column)
        {
            if (at <= 0 || at > _blocks.Count || _blocks[at - 1].Kind != BlockKind.TableRow)
                return;
            var above = _blocks[at - 1];
            var columns = Math.Max(1, at - 1 < _tableColumns.Length ? _tableColumns[at - 1] : BlockMarkdown.CountCells(above.Text));
            _blocks.Insert(at, new EditorBlock(BlockKind.TableRow, BlockMarkdown.JoinCells(Enumerable.Repeat(string.Empty, columns)), above.Indent));
            SyncRows();
            FocusRow(at, 0, column);
            RecordChange();
        }

        private void DeleteTableRow(int index, int column)
        {
            if (index < 0)
                return;
            var wasOnlyRow = !IsSameTable(index, index - 1) && !IsSameTable(index, index + 1);
            _blocks.RemoveAt(index);
            if (_blocks.Count == 0)
                _blocks.Add(new EditorBlock(BlockKind.Paragraph));
            SyncRows();
            if (!wasOnlyRow && IsSameTable(index - 1, index - 1))
                FocusRow(index - 1, int.MaxValue, column);
            else
                FocusRow(Math.Min(index, _blocks.Count - 1), 0, column);
            RecordChange();
        }

        /// <summary>표의 [시작, 끝) 범위.</summary>
        private void GetTableRange(int index, out int start, out int end)
        {
            start = index;
            while (IsSameTable(index, start - 1))
                start--;
            end = index + 1;
            while (IsSameTable(index, end))
                end++;
        }

        /// <summary>표 오른쪽 끝에 열을 하나 더한다(모든 행에 빈 셀).</summary>
        private void AddTableColumn(int index)
        {
            int start, end;
            GetTableRange(index, out start, out end);
            var columns = index < _tableColumns.Length ? _tableColumns[index] : 1;
            for (var i = start; i < end; i++)
            {
                var cells = BlockMarkdown.SplitCells(_blocks[i].Text).ToList();
                while (cells.Count < columns + 1)
                    cells.Add(string.Empty);
                _blocks[i].Text = BlockMarkdown.JoinCells(cells);
            }
            var aligns = (_blocks[start].TableAlign ?? string.Empty).Split(',').ToList();
            while (aligns.Count < columns + 1)
                aligns.Add(string.Empty);
            _blocks[start].TableAlign = string.Join(",", aligns);
            SyncRows();
            FocusRow(index, 0, columns);
            RecordChange();
        }

        /// <summary>표의 마지막 열을 지운다(열이 하나면 그대로).</summary>
        private void RemoveLastTableColumn(int index)
        {
            int start, end;
            GetTableRange(index, out start, out end);
            var columns = index < _tableColumns.Length ? _tableColumns[index] : 1;
            if (columns <= 1)
                return;
            for (var i = start; i < end; i++)
            {
                var cells = BlockMarkdown.SplitCells(_blocks[i].Text).Take(columns - 1);
                _blocks[i].Text = BlockMarkdown.JoinCells(cells);
            }
            var aligns = (_blocks[start].TableAlign ?? string.Empty).Split(',').Take(columns - 1);
            _blocks[start].TableAlign = string.Join(",", aligns);
            SyncRows();
            FocusRow(index, int.MaxValue, columns - 2);
            RecordChange();
        }

        /// <summary><c>/표</c>: 3열 표(머리글 + 2행). 빈 문단이면 그 자리에, 아니면 아래에 넣는다.</summary>
        private void InsertTable(BlockRow row)
        {
            var index = IndexOf(row);
            var indent = row.Block.Indent;
            var empty = BlockMarkdown.JoinCells(new[] { string.Empty, string.Empty, string.Empty });
            var rows = new List<EditorBlock>
            {
                new EditorBlock(BlockKind.TableRow, empty, indent) { TableAlign = ",," },
                new EditorBlock(BlockKind.TableRow, empty, indent),
                new EditorBlock(BlockKind.TableRow, empty, indent),
            };
            int at;
            if (row.Block.Kind == BlockKind.Paragraph && row.Block.Text.Trim().Length == 0)
            {
                _blocks.RemoveAt(index);
                at = index;
            }
            else
            {
                at = BlockOperations.SubtreeEnd(_blocks, index);
            }
            _blocks.InsertRange(at, rows);
            BlockOperations.NormalizeIndents(_blocks);
            SyncRows();
            FocusRow(at, 0, 0);
            RecordChange();
        }

        /// <summary><c>/이미지</c>: 빈 이미지 블록을 넣고 경로 입력란에 커서를 둔다.</summary>
        private void InsertImage(BlockRow row)
        {
            var index = IndexOf(row);
            var image = new EditorBlock(BlockKind.Image, string.Empty, row.Block.Indent);
            int at;
            if (row.Block.Kind == BlockKind.Paragraph && row.Block.Text.Trim().Length == 0)
            {
                _blocks.RemoveAt(index);
                at = index;
            }
            else
            {
                at = BlockOperations.SubtreeEnd(_blocks, index);
            }
            _blocks.Insert(at, image);
            BlockOperations.NormalizeIndents(_blocks);
            SyncRows();
            var imageRow = _panel.EnsureRealized(at);
            if (imageRow != null)
                imageRow.FocusMeta();
            RecordChange();
        }

        private void OnCellTextChanged(BlockRow row)
        {
            if (_suppressTextChanged)
                return;
            row.Block.Text = row.JoinedCells();
            RecordChange(row);
            if (row.Editor.IsKeyboardFocused)
                ScheduleCaretIntoView(row.Editor);
        }

        /// <summary>
        /// 셀에 여러 줄·탭 텍스트(엑셀·스프레드시트 복사, Markdown 표)를 붙여넣으면 지금 셀부터 칸·행을 채운다.
        /// 남는 줄은 아래에 새 행으로 넣고, 열이 모자라면 표가 넓어진다.
        /// </summary>
        private void OnCellPasting(BlockRow row, int column, DataObjectPastingEventArgs e)
        {
            if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText))
                return;
            var text = e.DataObject.GetData(DataFormats.UnicodeText) as string;
            if (string.IsNullOrEmpty(text) || (text.IndexOf('\n') < 0 && text.IndexOf('\t') < 0))
                return;
            e.CancelCommand();
            PasteIntoCells(row, column, text);
        }

        private void PasteIntoCells(BlockRow row, int column, string text)
        {
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n').Split('\n')
                .Where(l => !BlockMarkdownIsDelimiter(l))
                .Select(l => l.IndexOf('\t') >= 0 ? l.Split('\t').ToList() : BlockMarkdown.SplitTableRow(l))
                .ToList();
            if (lines.Count == 0)
                return;

            var index = IndexOf(row);
            var indent = row.Block.Indent;
            var last = index;
            for (var i = 0; i < lines.Count; i++)
            {
                List<string> cells;
                if (i == 0)
                {
                    cells = BlockMarkdown.SplitCells(row.JoinedCells()).ToList();
                }
                else
                {
                    var newRow = new EditorBlock(BlockKind.TableRow, string.Empty, indent);
                    _blocks.Insert(index + i, newRow);
                    cells = new List<string>();
                    last = index + i;
                }
                while (cells.Count < column + lines[i].Count)
                    cells.Add(string.Empty);
                for (var c = 0; c < lines[i].Count; c++)
                    cells[column + c] = lines[i][c].Trim();
                _blocks[index + i].Text = BlockMarkdown.JoinCells(cells);
            }
            SyncRows();
            FocusRow(last, int.MaxValue, column + lines[lines.Count - 1].Count - 1);
            RecordChange();
        }

        private static bool BlockMarkdownIsDelimiter(string line)
        {
            var trimmed = line.Trim();
            return trimmed.Length > 0 && trimmed.IndexOf('-') >= 0 && trimmed.Trim('|', '-', ':', ' ', '\t').Length == 0;
        }

        // ── 이미지·보조 입력란 ────────────────────────────────────────────

        /// <summary>코드 언어·이미지 경로 입력란이 바뀌었다.</summary>
        private void OnMetaChanged(BlockRow row, string value)
        {
            if (_suppressTextChanged)
                return;
            if (row.Block.Kind == BlockKind.Image)
            {
                row.Block.Source = value;
                row.RefreshImage();
            }
            else
            {
                row.Block.Language = value;
            }
            RecordChange(row);
        }

        /// <summary>
        /// 이미지 경로를 로컬 파일 경로로. 상대 경로는 <see cref="BaseDirectory"/> 기준. http(s) 등 원격 주소는 null(표시하지 않음 —
        /// 문서를 여는 것만으로 외부에 요청이 나가지 않게).
        /// </summary>
        private string ResolveImagePath(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return null;
            source = source.Trim();
            Uri uri;
            if (Uri.TryCreate(source, UriKind.Absolute, out uri) && !(uri.IsFile || uri.Scheme.Length == 1))
                return uri.IsFile ? uri.LocalPath : null;
            try
            {
                if (uri != null && uri.IsFile)
                    return uri.LocalPath;
                var relative = Uri.UnescapeDataString(source.Split('?', '#')[0]).Replace('/', Path.DirectorySeparatorChar);
                return Path.GetFullPath(Path.IsPathRooted(relative) ? relative : Path.Combine(BaseDirectory ?? Environment.CurrentDirectory, relative));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return null;
            }
        }
    }
}

using Folderss.Models;
using Folderss.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Folderss.Controls
{
    public sealed partial class BlockEditor
    {
        /// <summary>
        /// 블록 하나의 화면. 구조: Root[손잡이 | 본문 테두리[표식 | (코드 언어·이미지 경로) / 본문·표·이미지 / (이미지 캡션)]] + 선택 표시.
        /// <para>
        /// 편집하지 않는 행은 <see cref="TextBlock"/>으로만 그린다. IME가 설치된 Windows에서 WPF는 화면에 붙은 TextBox를 모두 입력기(TSF)에
        /// 등록하고, 등록된 TextBox의 내용을 바꾸면 입력기와 잠금을 주고받아(프로세스 간 호출) 수백 ms~수 초 멈출 수 있다 — 가상화로 행을
        /// 다시 묶을 때마다 그랬다. 그래서 TextBox는 마우스를 올리거나 커서가 들어올 때 그 행에만 만들고(<see cref="EnsureEditing"/>),
        /// 그런 행은 재사용하지 않는다(<see cref="HasEditors"/>).
        /// </para>
        /// 종류가 바뀌어도 TextBox는 그대로 두고 모양만 바꾼다 — 입력 중 포커스·IME 조합이 끊기지 않게.
        /// </summary>
        private sealed class BlockRow
        {
            private readonly BlockEditor _owner;
            private readonly Border _body;
            private readonly Grid _bodyGrid;
            private readonly TextBlock _display;
            private TextBox _mainEditor;
            private bool _editing;
            private StackPanel _gutter;
            private double _gutterTop = 2;
            private Border _highlight;
            private TextBlock _metaDisplay;
            private TextBox _metaBox;
            private TextBlock _placeholder;
            private Border _divider;
            private UIElement _marker;
            private TextBlock _markerText;
            private CheckBox _checkBox;
            private BlockKind? _appliedKind;
            private string _appliedTextColor;   // 마지막으로 칠한 글자색(흐림·색 이름·테마 밝기) — 바뀔 때만 다시 칠한다
            private string _appliedBackground;  // 마지막으로 칠한 배경색
            private string _appliedCallout;
            private string _label;

            // 표: 셀마다 [표시용 TextBlock, 그 셀을 편집할 때만 TextBox(없으면 null)].
            // 표 영역은 [셀 | 오른쪽 끝 열 추가 +] / [마지막 행에만 아래 행 추가 +]. 버튼 자리는 늘 잡아 두고 마우스를 올렸을 때만 보인다(흔들림 없게).
            private Grid _tableArea;
            private Grid _tableGrid;
            private Border _addColumnButton;
            private Border _addRowButton;
            private bool _tableLast;
            private readonly List<Grid> _cellHosts = new List<Grid>();
            private readonly List<TextBlock> _cellDisplays = new List<TextBlock>();
            private readonly List<TextBox> _cells = new List<TextBox>();
            private int _tableColumns;
            private bool _tableHeader;
            private string _tableAlign = string.Empty;

            // 이미지
            private Grid _imageHost;
            private Image _image;
            private TextBlock _imageStatus;
            private string _imageKey;

            public EditorBlock Block { get; private set; }
            public Grid Root { get; }

            /// <summary>입력란. 없으면 만든다. 표 행은 마지막으로 포커스를 받은 셀, 이미지는 캡션.</summary>
            public TextBox Editor
            {
                get
                {
                    EnsureEditing();
                    return Block.Kind == BlockKind.TableRow && _cellHosts.Count > 0 ? EnsureCellEditor(Math.Min(ActiveColumn, _cellHosts.Count - 1)) : _mainEditor;
                }
            }

            /// <summary>표 행에서 마지막으로 포커스를 받은 셀 번호.</summary>
            public int ActiveColumn { get; private set; }

            public int CellCount => _cellHosts.Count;

            /// <summary>입력란(TextBox)을 만든 행. 입력기에 등록돼 있어 다른 블록에 다시 묶어 쓰지 않는다.</summary>
            public bool HasEditors => _editing;

            /// <summary>접근성 트리에 내보낼 요소. 입력란을 새로 만들지 않는다.</summary>
            public UIElement AutomationTarget => _editing && _mainEditor != null && _mainEditor.Visibility == Visibility.Visible ? _mainEditor : (UIElement)Root;

            public BlockRow(BlockEditor owner, EditorBlock block)
            {
                _owner = owner;
                Block = block;

                Root = new Grid { Background = Brushes.Transparent, Tag = this };
                Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(GutterWidth) });
                Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                _bodyGrid = new Grid();
                _bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                _bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                _bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                _bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                _bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                _body = new Border { Child = _bodyGrid };
                Grid.SetColumn(_body, 1);
                Root.Children.Add(_body);

                // TextBox 글자 위치(왼쪽 2px 안쪽 여백 + 위아래 Padding 3)에 맞춘다.
                _display = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 3, 2, 3) };
                Grid.SetColumn(_display, 1);
                Grid.SetRow(_display, 1);
                _bodyGrid.Children.Add(_display);

                Root.MouseEnter += (s, e) =>
                {
                    EnsureGutter().Opacity = 1;
                    SetTableButtonsShown(true);
                    // 누르기 전에 입력란을 만들어 두어 첫 클릭·끌어 선택이 TextBox에 그대로 간다.
                    if (Block.Kind != BlockKind.Divider && _owner._dragRow == null)
                        EnsureEditing();
                };
                Root.MouseLeave += (s, e) =>
                {
                    if (_gutter != null && _owner._dragRow != this)
                        _gutter.Opacity = 0;
                    SetTableButtonsShown(false);
                };
                Root.MouseLeftButtonDown += Root_MouseLeftButtonDown;

                SyncFromBlock();
            }

            private TextBox CreateTextBox(string text)
            {
                var box = new TextBox
                {
                    TextWrapping = TextWrapping.Wrap,
                    AcceptsReturn = true,
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    Padding = new Thickness(0, 3, 0, 3),
                    IsUndoEnabled = false,
                    VerticalContentAlignment = VerticalAlignment.Top,
                    Tag = this,
                    // 화면에 붙이기 전에 내용을 넣는다(붙은 뒤에 넣으면 입력기와 잠금을 주고받음).
                    Text = text ?? string.Empty,
                };
                box.SetResourceReference(TextBoxBase.CaretBrushProperty, "PrimaryText");
                box.SetResourceReference(TextBoxBase.SelectionBrushProperty, "SelectionBrush");
                return box;
            }

            private StackPanel EnsureGutter()
            {
                if (_gutter != null)
                    return _gutter;

                var add = CreateGutterButton("+", "클릭: 아래에 블록 추가 (표에서는 행 추가)");
                var handle = CreateGutterButton("⋮⋮", "끌기: 옮기기\n클릭: 블록 메뉴");
                handle.Cursor = Cursors.SizeAll;
                _gutter = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, _gutterTop, 4, 0),
                    Opacity = 0,
                };
                _gutter.Children.Add(add);
                _gutter.Children.Add(handle);
                Root.Children.Add(_gutter);

                add.MouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    _owner.AddBlockBelow(this);
                };
                handle.MouseLeftButtonDown += (s, e) => _owner.HandleMouseDown(this, handle, e);
                handle.MouseMove += (s, e) => _owner.HandleMouseMove(handle, e);
                handle.MouseLeftButtonUp += (s, e) => _owner.HandleMouseUp(this, handle, e);
                // 끄는 중 창 전환 등으로 마우스 캡처를 잃으면 놓기 표시를 지운다.
                handle.LostMouseCapture += (s, e) =>
                {
                    if (_owner._dragRow == this)
                        _owner.EndDrag(handle);
                };
                return _gutter;
            }

            private static Border CreateGutterButton(string text, string tooltip)
            {
                var label = new TextBlock
                {
                    Text = text,
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                label.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
                var button = new Border
                {
                    Width = 22,
                    Height = 24,
                    CornerRadius = new CornerRadius(3),
                    Background = Brushes.Transparent,
                    Cursor = Cursors.Hand,
                    ToolTip = tooltip,
                    Child = label,
                };
                button.MouseEnter += (s, e) => button.SetResourceReference(Border.BackgroundProperty, "ControlHoverBrush");
                button.MouseLeave += (s, e) => button.Background = Brushes.Transparent;
                return button;
            }

            private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            {
                // 입력란 밖(들여쓰기·여백·이미지)을 누르면 그 블록 끝으로, 입력란이 없는 구분선은 블록 선택.
                if (e.Handled)
                    return;
                e.Handled = true;
                var index = _owner.IndexOf(this);
                if (Block.Kind != BlockKind.Divider)
                    _owner.FocusRow(index, int.MaxValue, ActiveColumn);
                else
                    _owner.SelectBlocks(index, index);
            }

            /// <summary>화면 밖으로 나가 쉬는 행의 일시 상태를 지운다(다른 블록에 다시 묶일 때 남지 않게).</summary>
            public void ResetTransient()
            {
                if (_gutter != null)
                    _gutter.Opacity = 0;
                if (_highlight != null)
                    _highlight.Visibility = Visibility.Collapsed;
                SetTableButtonsShown(false);
            }

            /// <summary>같은 행에 다른 블록(되돌린 사본이나 가상화로 다시 쓰는 행)을 묶고 화면을 맞춘다.</summary>
            public void Rebind(EditorBlock block)
            {
                if (block.Id != Block.Id)
                {
                    ActiveColumn = 0;
                    _imageKey = null;
                }
                Block = block;
                SyncFromBlock();
            }

            /// <summary>블록 내용을 화면에 반영한다. 종류가 바뀌었을 때만 모양 전체를 다시 만든다.</summary>
            public void SyncFromBlock()
            {
                if (_appliedKind != Block.Kind)
                    ApplyKind();

                _owner._suppressTextChanged = true;
                try
                {
                    if (Block.Kind == BlockKind.TableRow)
                    {
                        SyncCells();
                    }
                    else
                    {
                        if (_display.Text != Block.Text)
                            _display.Text = Block.Text;
                        if (_mainEditor != null && _mainEditor.Text != Block.Text)
                            _mainEditor.Text = Block.Text;
                    }
                    var meta = MetaValue();
                    if (_metaDisplay != null && _metaDisplay.Text != meta)
                        _metaDisplay.Text = meta;
                    if (_metaBox != null && _metaBox.Text != meta)
                        _metaBox.Text = meta;
                }
                finally
                {
                    _owner._suppressTextChanged = false;
                }

                SyncEditors();
                RefreshState();
                if (Block.Kind == BlockKind.Image)
                    RefreshImage();
            }

            private string MetaValue()
            {
                return (Block.Kind == BlockKind.Image ? Block.Source : Block.Language) ?? string.Empty;
            }

            // ── 편집 모드(TextBox) ─────────────────────────────────────────

            /// <summary>이 행을 편집할 수 있게 입력란(TextBox)을 만든다. 한 번 만들면 행이 거둬질 때까지 둔다.</summary>
            public void EnsureEditing()
            {
                if (_editing)
                    return;
                _editing = true;
                SyncEditors();
                _appliedTextColor = null;
                RefreshState();
            }

            /// <summary>편집 모드면 지금 종류에 필요한 입력란을 만들고 표시용 TextBlock을 숨긴다.</summary>
            private void SyncEditors()
            {
                var kind = Block.Kind;
                var showMeta = kind == BlockKind.Code || kind == BlockKind.Image;
                if (!_editing)
                {
                    _display.Visibility = kind == BlockKind.Divider || kind == BlockKind.TableRow ? Visibility.Collapsed : Visibility.Visible;
                    if (_metaDisplay != null)
                        _metaDisplay.Visibility = showMeta ? Visibility.Visible : Visibility.Collapsed;
                    return;
                }

                _display.Visibility = Visibility.Collapsed;
                if (_metaDisplay != null)
                    _metaDisplay.Visibility = Visibility.Collapsed;
                if (showMeta)
                    EnsureMetaBox().Visibility = Visibility.Visible;
                else if (_metaBox != null)
                    _metaBox.Visibility = Visibility.Collapsed;

                if (kind == BlockKind.TableRow)
                {
                    // 셀 입력란은 커서가 들어가거나 마우스를 올린 셀에만 만든다(EnsureCellEditor) — 행 추가가 셀 수만큼 느려지지 않게.
                    if (_mainEditor != null)
                        _mainEditor.Visibility = Visibility.Collapsed;
                    for (var i = 0; i < _cells.Count; i++)
                    {
                        if (_cells[i] == null)
                            continue;
                        _cellDisplays[i].Visibility = Visibility.Collapsed;
                        StyleCell(_cells[i], i);
                    }
                    return;
                }

                if (_mainEditor == null)
                    CreateMainEditor();
                _mainEditor.Visibility = kind == BlockKind.Divider ? Visibility.Collapsed : Visibility.Visible;
                Grid.SetRow(_mainEditor, kind == BlockKind.Image ? 2 : 1);
                ApplyTextStyle(_mainEditor);
                _mainEditor.AcceptsTab = EditorBlock.IsVerbatim(kind);
                _mainEditor.ToolTip = kind == BlockKind.Raw ? "블록 편집기가 해석하지 않는 Markdown 원문(front matter·HTML 등). 그대로 저장됩니다." : null;
                _appliedTextColor = null;
            }

            private void CreateMainEditor()
            {
                _mainEditor = CreateTextBox(Block.Text);
                Grid.SetColumn(_mainEditor, 1);
                Grid.SetRow(_mainEditor, Block.Kind == BlockKind.Image ? 2 : 1);
                _bodyGrid.Children.Add(_mainEditor);

                _mainEditor.TextChanged += (s, e) => _owner.OnEditorTextChanged(this, e);
                _mainEditor.GotKeyboardFocus += (s, e) => _owner.OnEditorFocused(this);
                _mainEditor.LostKeyboardFocus += (s, e) => _owner.OnEditorBlurred(this);
                _mainEditor.SelectionChanged += (s, e) => _owner.OnEditorSelectionChanged(this);
                DataObject.AddPastingHandler(_mainEditor, (s, e) => _owner.OnEditorPasting(this, e));
            }

            /// <summary>글꼴·크기·굵기를 종류에 맞춘다(표시용 TextBlock과 TextBox 공통).</summary>
            private void ApplyTextStyle(FrameworkElement target)
            {
                var kind = Block.Kind;
                if (EditorBlock.IsVerbatim(kind))
                    target.SetValue(TextElement.FontFamilyProperty, CodeFont);
                else
                    target.SetResourceReference(TextElement.FontFamilyProperty, "AppFontFamily");
                target.SetValue(TextElement.FontSizeProperty, FontSizeOf(kind));
                target.SetValue(TextElement.FontWeightProperty, kind == BlockKind.Heading1 ? FontWeights.Bold
                    : kind == BlockKind.Heading2 || kind == BlockKind.Heading3 ? FontWeights.SemiBold
                    : FontWeights.Normal);
            }

            private void ApplyKind()
            {
                var kind = Block.Kind;
                var verbatim = EditorBlock.IsVerbatim(kind);
                _appliedKind = kind;
                _appliedCallout = null;
                _appliedTextColor = null;

                ApplyTextStyle(_display);
                Grid.SetRow(_display, kind == BlockKind.Image ? 2 : 1);

                if (kind == BlockKind.Divider)
                    EnsureDivider().Visibility = Visibility.Visible;
                else if (_divider != null)
                    _divider.Visibility = Visibility.Collapsed;
                if (kind == BlockKind.Code || kind == BlockKind.Image)
                {
                    var meta = EnsureMetaDisplay();
                    meta.Text = MetaValue();
                    var tip = kind == BlockKind.Image ? "이미지 경로 (문서 폴더 기준 상대 경로 또는 절대 경로)" : "코드 언어 (예: csharp, python, json)";
                    meta.ToolTip = tip;
                    if (_metaBox != null)
                        _metaBox.ToolTip = tip;
                }
                if (kind == BlockKind.TableRow)
                {
                    EnsureTableGrid();
                    _tableArea.Visibility = Visibility.Visible;
                }
                else if (_tableArea != null)
                {
                    _tableArea.Visibility = Visibility.Collapsed;
                }
                if (kind == BlockKind.Image)
                {
                    EnsureImageHost().Visibility = Visibility.Visible;
                }
                else if (_imageHost != null)
                {
                    _imageHost.Visibility = Visibility.Collapsed;
                    _image.Source = null;
                    _imageKey = null;
                }
                if (_placeholder != null)
                    StylePlaceholder();

                Root.Margin = kind == BlockKind.Heading1 ? new Thickness(0, 22, 0, 2)
                    : kind == BlockKind.Heading2 ? new Thickness(0, 16, 0, 2)
                    : kind == BlockKind.Heading3 ? new Thickness(0, 10, 0, 1)
                    : kind == BlockKind.TableRow ? new Thickness(0)
                    : verbatim || kind == BlockKind.Callout || kind == BlockKind.Image ? new Thickness(0, 4, 0, 4)
                    : new Thickness(0, 1, 0, 1);
                _gutterTop = kind == BlockKind.Heading1 ? 9 : kind == BlockKind.Heading2 ? 5 : verbatim || kind == BlockKind.Callout ? 8 : 2;
                if (_gutter != null)
                    _gutter.Margin = new Thickness(0, _gutterTop, 4, 0);

                ApplyBodyStyle();
                SetMarker(CreateMarker(kind));
            }

            /// <summary>본문 테두리(인용 막대·콜아웃·코드 상자)와 블록 배경색(사이드카 속성)을 칠한다.</summary>
            private void ApplyBodyStyle()
            {
                var kind = Block.Kind;
                _body.ClearValue(Border.BackgroundProperty);
                _body.ClearValue(Border.BorderBrushProperty);
                _body.BorderThickness = new Thickness(0);
                _body.Padding = new Thickness(0);
                _body.CornerRadius = new CornerRadius(0);
                switch (kind)
                {
                    case BlockKind.Quote:
                        _body.BorderThickness = new Thickness(3, 0, 0, 0);
                        _body.SetResourceReference(Border.BorderBrushProperty, "PrimaryText");
                        _body.Padding = new Thickness(14, 0, 0, 0);
                        break;
                    case BlockKind.Callout:
                        _body.SetResourceReference(Border.BackgroundProperty, "ControlBackground");
                        _body.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                        _body.BorderThickness = new Thickness(1);
                        _body.CornerRadius = new CornerRadius(5);
                        _body.Padding = new Thickness(12, 10, 14, 10);
                        break;
                    case BlockKind.Code:
                    case BlockKind.Raw:
                        _body.SetResourceReference(Border.BackgroundProperty, "SurfaceBackground");
                        _body.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                        _body.BorderThickness = new Thickness(1);
                        _body.CornerRadius = new CornerRadius(5);
                        _body.Padding = new Thickness(14, 8, 14, 10);
                        break;
                }

                var background = Block.GetAttribute(BackgroundAttribute);
                var brush = background == null ? null : _owner.BlockColorBrush(background, true);
                if (_tableGrid != null)
                    _tableGrid.Background = kind == BlockKind.TableRow ? brush : null;
                if (kind == BlockKind.TableRow)
                {
                    // 표 행은 셀 영역만 칠한다(가장자리 + 버튼 자리는 그대로).
                }
                else if (brush != null)
                {
                    _body.Background = brush;
                    if (_body.Padding == new Thickness(0))
                    {
                        _body.Padding = new Thickness(6, 0, 6, 0);
                        _body.CornerRadius = new CornerRadius(3);
                    }
                }
                _appliedBackground = BackgroundKey();
            }

            private string BackgroundKey()
            {
                var name = Block.GetAttribute(BackgroundAttribute);
                return name == null ? string.Empty : name + (_owner.IsDarkTheme() ? "/dark" : "/light");
            }

            /// <summary>글자색: 체크한 할 일은 흐리게, 그 밖은 블록 글자색(사이드카 속성) 또는 기본 글자색. 표시용·입력란·표 셀 모두.</summary>
            private void ApplyForeground()
            {
                var dimmed = Block.Kind == BlockKind.Todo && Block.Checked;
                var name = dimmed ? null : Block.GetAttribute(ColorAttribute);
                var brush = name == null ? null : _owner.BlockColorBrush(name, false);
                var resource = dimmed ? "DisabledTextBrush" : "PrimaryText";
                var targets = new List<FrameworkElement> { _display };
                if (_mainEditor != null)
                    targets.Add(_mainEditor);
                targets.AddRange(_cellDisplays);
                targets.AddRange(_cells.Where(c => c != null));
                foreach (var target in targets)
                {
                    if (brush != null)
                        target.SetValue(TextElement.ForegroundProperty, brush);
                    else
                        target.SetResourceReference(TextElement.ForegroundProperty, resource);
                }
            }

            private string ForegroundKey()
            {
                if (Block.Kind == BlockKind.Todo && Block.Checked)
                    return "dimmed";
                var name = Block.GetAttribute(ColorAttribute);
                return name == null ? string.Empty : name + (_owner.IsDarkTheme() ? "/dark" : "/light");
            }

            private UIElement CreateMarker(BlockKind kind)
            {
                _markerText = null;
                _checkBox = null;
                switch (kind)
                {
                    case BlockKind.Bulleted:
                        _markerText = CreateMarkerText(_label ?? "•", 24, TextAlignment.Center);
                        _markerText.FontSize = FontSizeOf(kind) + 2;
                        _markerText.Margin = new Thickness(0, 1, 2, 0);
                        return _markerText;
                    case BlockKind.Numbered:
                        _markerText = CreateMarkerText(_label ?? "1.", 24, TextAlignment.Right);
                        _markerText.Margin = new Thickness(0, 3, 6, 0);
                        return _markerText;
                    case BlockKind.Todo:
                        _checkBox = new CheckBox
                        {
                            Focusable = false,
                            Margin = new Thickness(4, 6, 8, 0),
                            VerticalAlignment = VerticalAlignment.Top,
                            Cursor = Cursors.Hand,
                            IsChecked = Block.Checked,
                        };
                        _checkBox.Click += (s, e) => _owner.ToggleChecked(this);
                        return _checkBox;
                    case BlockKind.Toggle:
                        _markerText = CreateMarkerText(string.Empty, 24, TextAlignment.Center);
                        _markerText.Margin = new Thickness(0, 3, 2, 0);
                        _markerText.Cursor = Cursors.Hand;
                        _markerText.Background = Brushes.Transparent;
                        _markerText.ToolTip = "접기·펴기 (Ctrl+Enter)";
                        _markerText.MouseLeftButtonDown += (s, e) =>
                        {
                            e.Handled = true;
                            _owner.ToggleCollapse(this);
                        };
                        return _markerText;
                    case BlockKind.Callout:
                        _markerText = CreateMarkerText(string.Empty, 0, TextAlignment.Left);
                        _markerText.FontSize = 16;
                        _markerText.Margin = new Thickness(0, 1, 10, 0);
                        _markerText.Cursor = Cursors.Hand;
                        _markerText.Background = Brushes.Transparent;
                        _markerText.MouseLeftButtonDown += (s, e) =>
                        {
                            e.Handled = true;
                            _owner.CycleCallout(this);
                        };
                        return _markerText;
                    default:
                        return null;
                }
            }

            private void SetMarker(UIElement marker)
            {
                if (_marker != null)
                    _bodyGrid.Children.Remove(_marker);
                _marker = marker;
                if (marker == null)
                    return;
                Grid.SetRow(marker, 1);
                Grid.SetColumn(marker, 0);
                _bodyGrid.Children.Add(marker);
            }

            private TextBlock CreateMarkerText(string text, double width, TextAlignment alignment)
            {
                var marker = new TextBlock { Text = text, TextAlignment = alignment, FontSize = FontSizeOf(Block.Kind), VerticalAlignment = VerticalAlignment.Top };
                if (width > 0)
                    marker.MinWidth = width;
                marker.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
                return marker;
            }

            private Border EnsureDivider()
            {
                if (_divider != null)
                    return _divider;
                _divider = new Border { Height = 1, Margin = new Thickness(0, 13, 0, 13) };
                _divider.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
                Grid.SetColumn(_divider, 1);
                Grid.SetRow(_divider, 1);
                _bodyGrid.Children.Add(_divider);
                return _divider;
            }

            /// <summary>코드 언어·이미지 경로 표시(본문 위 작은 글자). 편집할 때는 같은 자리의 TextBox로 바뀐다.</summary>
            private TextBlock EnsureMetaDisplay()
            {
                if (_metaDisplay != null)
                    return _metaDisplay;
                _metaDisplay = new TextBlock { FontSize = 11, Margin = new Thickness(2, 0, 2, 4), MinWidth = 90 };
                _metaDisplay.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
                Grid.SetColumn(_metaDisplay, 1);
                Grid.SetRow(_metaDisplay, 0);
                _bodyGrid.Children.Add(_metaDisplay);
                return _metaDisplay;
            }

            private TextBox EnsureMetaBox()
            {
                if (_metaBox != null)
                    return _metaBox;
                _metaBox = new TextBox
                {
                    FontSize = 11,
                    MinWidth = 90,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    Padding = new Thickness(0, 0, 0, 4),
                    Text = MetaValue(),
                    ToolTip = _metaDisplay?.ToolTip,
                    Tag = this,
                };
                _metaBox.SetResourceReference(Control.ForegroundProperty, "SecondaryText");
                Grid.SetColumn(_metaBox, 1);
                Grid.SetRow(_metaBox, 0);
                _bodyGrid.Children.Add(_metaBox);
                _metaBox.TextChanged += (s, e) => _owner.OnMetaChanged(this, _metaBox.Text);
                return _metaBox;
            }

            /// <summary>이미지 경로 입력란에 커서를 둔다(새 이미지 블록).</summary>
            public void FocusMeta()
            {
                EnsureEditing();
                var box = EnsureMetaBox();
                _owner.UpdateLayout();
                box.Focus();
                box.SelectAll();
            }

            private void StylePlaceholder()
            {
                ApplyTextStyle(_placeholder);
                _placeholder.Text = PlaceholderOf(Block.Kind);
                Grid.SetRow(_placeholder, Block.Kind == BlockKind.Image ? 2 : 1);
            }

            /// <summary>체크·접힘·콜아웃 종류처럼 종류는 그대로인 상태 변화를 반영한다. 바뀐 것만 건드린다.</summary>
            public void RefreshState()
            {
                switch (Block.Kind)
                {
                    case BlockKind.Todo:
                        if (_checkBox != null && _checkBox.IsChecked != Block.Checked)
                            _checkBox.IsChecked = Block.Checked;
                        break;
                    case BlockKind.Toggle:
                        var arrow = Block.Collapsed ? "▸" : "▾";
                        if (_markerText != null && _markerText.Text != arrow)
                            _markerText.Text = arrow;
                        break;
                    case BlockKind.Callout:
                        var type = BlockMarkdown.NormalizeCalloutType(Block.CalloutType);
                        if (_markerText != null && _appliedCallout != type)
                        {
                            _markerText.Text = CalloutIcon(type);
                            _markerText.ToolTip = type + " — 클릭해 종류 바꾸기";
                            _appliedCallout = type;
                        }
                        break;
                }

                var foreground = ForegroundKey();
                if (_appliedTextColor != foreground)
                {
                    ApplyForeground();
                    _appliedTextColor = foreground;
                }
                if (_appliedBackground != BackgroundKey())
                    ApplyBodyStyle();
                UpdatePlaceholder();
            }

            public void SetLabel(string label)
            {
                if (label == _label)
                    return;
                _label = label;
                if (_markerText != null && (Block.Kind == BlockKind.Bulleted || Block.Kind == BlockKind.Numbered))
                    _markerText.Text = label ?? string.Empty;
            }

            public void ApplyIndent()
            {
                var margin = new Thickness(Block.Indent * IndentWidth, 0, 0, 0);
                if (_body.Margin == margin)
                    return;
                _body.Margin = margin;
                if (_highlight != null)
                    _highlight.Margin = margin;
            }

            public void SetSelected(bool selected)
            {
                if (_highlight == null)
                {
                    if (!selected)
                        return;
                    _highlight = new Border { IsHitTestVisible = false, CornerRadius = new CornerRadius(3), Opacity = 0.45, Margin = _body.Margin };
                    _highlight.SetResourceReference(Border.BackgroundProperty, "SelectionBrush");
                    Grid.SetColumn(_highlight, 1);
                    Root.Children.Add(_highlight);
                }
                _highlight.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            }

            public void UpdatePlaceholder()
            {
                var kind = Block.Kind;
                var show = kind != BlockKind.Divider && kind != BlockKind.TableRow && Block.Text.Length == 0 &&
                           (IsHeading(kind) || (_mainEditor != null && _mainEditor.IsKeyboardFocused));
                if (!show)
                {
                    if (_placeholder != null)
                        _placeholder.Visibility = Visibility.Collapsed;
                    return;
                }
                if (_placeholder == null)
                {
                    _placeholder = new TextBlock
                    {
                        IsHitTestVisible = false,
                        Margin = new Thickness(2, 3, 0, 0),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    };
                    _placeholder.SetResourceReference(TextBlock.ForegroundProperty, "DisabledTextBrush");
                    Grid.SetColumn(_placeholder, 1);
                    _bodyGrid.Children.Add(_placeholder);
                    StylePlaceholder();
                }
                _placeholder.Visibility = Visibility.Visible;
            }

            // ── 표 ─────────────────────────────────────────────────────────

            /// <summary>표 정보(열 수·머리글 여부·열 정렬·표의 마지막 행인지)를 받는다.</summary>
            public void SetTable(int columns, bool header, string align, bool last)
            {
                if (Block.Kind != BlockKind.TableRow)
                    return;
                align = align ?? string.Empty;
                if (last != _tableLast)
                {
                    _tableLast = last;
                    EnsureTableGrid();
                    _addRowButton.Visibility = last ? Visibility.Visible : Visibility.Collapsed;
                }
                if (columns == _tableColumns && header == _tableHeader && align == _tableAlign && _cellHosts.Count == Math.Max(1, Math.Max(columns, BlockMarkdown.CountCells(Block.Text))))
                    return;
                _tableColumns = columns;
                _tableHeader = header;
                _tableAlign = align;
                _owner._suppressTextChanged = true;
                try
                {
                    SyncCells();
                }
                finally
                {
                    _owner._suppressTextChanged = false;
                }
                SyncEditors();
                // 셀 칸이 늘었으면 새 칸도 블록 글자색을 따르게
                _appliedTextColor = null;
                RefreshState();
            }

            private Grid EnsureTableGrid()
            {
                if (_tableGrid != null)
                    return _tableGrid;
                _tableGrid = new Grid();

                _addColumnButton = CreateTableAddButton("오른쪽 끝에 열 추가");
                _addColumnButton.Width = TableAddColumnWidth - 4;
                _addColumnButton.Margin = new Thickness(4, 0, 0, 0);
                _addColumnButton.MouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    _owner.AddTableColumn(_owner.IndexOf(this));
                };
                _addRowButton = CreateTableAddButton("아래에 행 추가");
                _addRowButton.Height = TableAddRowHeight - 4;
                _addRowButton.Margin = new Thickness(0, 4, 0, 0);
                _addRowButton.Visibility = _tableLast ? Visibility.Visible : Visibility.Collapsed;
                _addRowButton.MouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    _owner.InsertTableRow(_owner.IndexOf(this) + 1, 0);
                };

                _tableArea = new Grid();
                _tableArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                _tableArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TableAddColumnWidth) });
                _tableArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                _tableArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                _tableArea.Children.Add(_tableGrid);
                Grid.SetColumn(_addColumnButton, 1);
                _tableArea.Children.Add(_addColumnButton);
                Grid.SetRow(_addRowButton, 1);
                _tableArea.Children.Add(_addRowButton);
                Grid.SetColumn(_tableArea, 1);
                Grid.SetRow(_tableArea, 1);
                _bodyGrid.Children.Add(_tableArea);
                return _tableGrid;
            }

            /// <summary>표 가장자리의 + 버튼(Notion의 열·행 추가). 평소에는 투명하고 행에 마우스를 올리면 보인다.</summary>
            private static Border CreateTableAddButton(string tooltip)
            {
                var label = new TextBlock
                {
                    Text = "+",
                    FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                label.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
                var button = new Border
                {
                    CornerRadius = new CornerRadius(3),
                    Cursor = Cursors.Hand,
                    ToolTip = tooltip,
                    Child = label,
                    Opacity = 0,
                };
                button.SetResourceReference(Border.BackgroundProperty, "ControlBackground");
                button.MouseEnter += (s, e) => button.SetResourceReference(Border.BackgroundProperty, "ControlHoverBrush");
                button.MouseLeave += (s, e) => button.SetResourceReference(Border.BackgroundProperty, "ControlBackground");
                return button;
            }

            private void SetTableButtonsShown(bool shown)
            {
                if (_addColumnButton == null)
                    return;
                var opacity = shown && Block.Kind == BlockKind.TableRow && _owner._dragRow == null ? 1 : 0;
                _addColumnButton.Opacity = opacity;
                _addRowButton.Opacity = opacity;
            }

            /// <summary>셀 칸 수를 표의 열 수에 맞추고 블록 본문(탭 구분)을 셀에 나눠 넣는다(편집 중이면 입력란에도).</summary>
            private void SyncCells()
            {
                var grid = EnsureTableGrid();
                var values = BlockMarkdown.SplitCells(Block.Text);
                var count = Math.Max(1, Math.Max(_tableColumns, values.Length));
                while (_cellHosts.Count < count)
                    AddCellHost(grid);
                while (_cellHosts.Count > count)
                {
                    var last = _cellHosts.Count - 1;
                    grid.Children.Remove((UIElement)_cellHosts[last].Parent);
                    grid.ColumnDefinitions.RemoveAt(grid.ColumnDefinitions.Count - 1);
                    _cellHosts.RemoveAt(last);
                    _cellDisplays.RemoveAt(last);
                    _cells.RemoveAt(last);
                }

                var aligns = _tableAlign.Split(',');
                for (var i = 0; i < _cellHosts.Count; i++)
                {
                    var value = i < values.Length ? values[i] : string.Empty;
                    var display = _cellDisplays[i];
                    if (display.Text != value)
                        display.Text = value;
                    var editor = _cells[i];
                    if (editor != null && editor.Text != value)
                        editor.Text = value;
                    var a = i < aligns.Length ? aligns[i] : string.Empty;
                    display.TextAlignment = a == "c" ? TextAlignment.Center : a == "r" ? TextAlignment.Right : TextAlignment.Left;
                    display.FontWeight = _tableHeader ? FontWeights.SemiBold : FontWeights.Normal;
                    display.Visibility = editor != null ? Visibility.Collapsed : Visibility.Visible;
                    if (editor != null)
                        StyleCell(editor, i);
                    var border = (Border)_cellHosts[i].Parent;
                    border.BorderThickness = new Thickness(i == 0 ? 1 : 0, _tableHeader ? 1 : 0, 1, 1);
                    if (_tableHeader)
                        border.SetResourceReference(Border.BackgroundProperty, "SurfaceBackground");
                    else
                        border.Background = Brushes.Transparent;
                }
            }

            private void AddCellHost(Grid grid)
            {
                var display = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 7, 8, 7), FontSize = 14 };
                display.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
                display.SetResourceReference(TextBlock.FontFamilyProperty, "AppFontFamily");
                var host = new Grid();
                host.Children.Add(display);
                var border = new Border { Child = host };
                border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetColumn(border, _cellHosts.Count);
                grid.Children.Add(border);
                _cellHosts.Add(host);
                _cellDisplays.Add(display);
                _cells.Add(null);
                host.Background = Brushes.Transparent;
                host.MouseEnter += (s, e) =>
                {
                    if (_owner._dragRow == null)
                        EnsureCellEditor(_cellHosts.IndexOf(host));
                };
            }

            /// <summary><paramref name="index"/>번 셀의 입력란. 없으면 지금 셀 값으로 만든다(화면에 붙이기 전에 내용을 넣음).</summary>
            private TextBox EnsureCellEditor(int index)
            {
                if (index < 0 || index >= _cellHosts.Count)
                    return null;
                if (_cells[index] != null)
                    return _cells[index];
                _editing = true;
                var values = BlockMarkdown.SplitCells(Block.Text);
                AddCellEditor(index, index < values.Length ? values[index] : string.Empty);
                _cellDisplays[index].Visibility = Visibility.Collapsed;
                _appliedTextColor = null;
                RefreshState();
                return _cells[index];
            }

            private void AddCellEditor(int index, string value)
            {
                var cell = CreateTextBox(value);
                cell.AcceptsReturn = false;
                cell.Padding = new Thickness(6, 4, 6, 4);
                cell.FontSize = 14;
                cell.SetResourceReference(Control.ForegroundProperty, "PrimaryText");
                cell.SetResourceReference(Control.FontFamilyProperty, "AppFontFamily");
                StyleCell(cell, index);
                _cellHosts[index].Children.Add(cell);
                _cells[index] = cell;

                cell.TextChanged += (s, e) => _owner.OnCellTextChanged(this);
                cell.GotKeyboardFocus += (s, e) =>
                {
                    ActiveColumn = Math.Max(0, _cells.IndexOf(cell));
                    _owner.OnEditorFocused(this);
                };
                cell.LostKeyboardFocus += (s, e) => _owner.OnEditorBlurred(this);
                cell.SelectionChanged += (s, e) => _owner.OnEditorSelectionChanged(this);
                DataObject.AddPastingHandler(cell, (s, e) => _owner.OnCellPasting(this, Math.Max(0, _cells.IndexOf(cell)), e));
            }

            private void StyleCell(TextBox cell, int index)
            {
                var aligns = _tableAlign.Split(',');
                var a = index < aligns.Length ? aligns[index] : string.Empty;
                cell.TextAlignment = a == "c" ? TextAlignment.Center : a == "r" ? TextAlignment.Right : TextAlignment.Left;
                cell.FontWeight = _tableHeader ? FontWeights.SemiBold : FontWeights.Normal;
                cell.Visibility = Visibility.Visible;
            }

            /// <summary>표 셀 본문들을 블록 본문(탭 구분)으로 합친다.</summary>
            public string JoinedCells()
            {
                var values = BlockMarkdown.SplitCells(Block.Text);
                var count = Math.Max(values.Length, _cellHosts.Count);
                var result = new string[count];
                for (var i = 0; i < count; i++)
                    result[i] = i < _cells.Count && _cells[i] != null ? _cells[i].Text : i < values.Length ? values[i] : string.Empty;
                return BlockMarkdown.JoinCells(result);
            }

            /// <summary>표 행은 <paramref name="column"/>번 셀(범위를 넘으면 마지막 셀), 그 밖은 입력란. 없으면 만든다.</summary>
            public TextBox EditorForColumn(int column)
            {
                EnsureEditing();
                if (Block.Kind != BlockKind.TableRow || _cellHosts.Count == 0)
                    return _mainEditor;
                return EnsureCellEditor(Math.Max(0, Math.Min(column, _cellHosts.Count - 1)));
            }

            /// <summary>위·아래 이동 때 가로 위치 <paramref name="x"/>(패널 좌표)에 있는 입력란. 표 행이면 그 위치의 셀.</summary>
            public TextBox EditorAtPanelX(double x, Visual panel)
            {
                EnsureEditing();
                if (Block.Kind != BlockKind.TableRow || _cellHosts.Count == 0)
                    return _mainEditor;
                for (var i = 0; i < _cellHosts.Count; i++)
                {
                    var border = (FrameworkElement)_cellHosts[i].Parent;
                    var right = border.TranslatePoint(new Point(border.ActualWidth, 0), (UIElement)panel).X;
                    if (x < right)
                        return EnsureCellEditor(i);
                }
                return EnsureCellEditor(_cellHosts.Count - 1);
            }

            // ── 이미지 ─────────────────────────────────────────────────────

            private Grid EnsureImageHost()
            {
                if (_imageHost != null)
                    return _imageHost;
                _image = new Image
                {
                    Stretch = Stretch.Uniform,
                    StretchDirection = StretchDirection.DownOnly,
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
                _imageStatus = new TextBlock
                {
                    Margin = new Thickness(12),
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                _imageStatus.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
                var statusBox = new Border { MinHeight = 120, CornerRadius = new CornerRadius(4), Child = _imageStatus };
                statusBox.SetResourceReference(Border.BackgroundProperty, "ControlBackground");
                _imageHost = new Grid();
                _imageHost.Children.Add(statusBox);
                _imageHost.Children.Add(_image);
                Grid.SetColumn(_imageHost, 1);
                Grid.SetRow(_imageHost, 1);
                _bodyGrid.Children.Add(_imageHost);
                return _imageHost;
            }

            private void ShowImageStatus(string text)
            {
                var box = (Border)_imageStatus.Parent;
                box.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
                _imageStatus.Text = text ?? string.Empty;
            }

            /// <summary>이미지 경로가 바뀌었거나 아직 안 불러왔으면 비동기로 불러온다(디코딩은 백그라운드, 화면 폭에 맞춰 줄임).</summary>
            public void RefreshImage()
            {
                EnsureImageHost();
                var source = Block.Source ?? string.Empty;
                var path = _owner.ResolveImagePath(source);
                if (path == null)
                {
                    _image.Source = null;
                    _imageKey = null;
                    ShowImageStatus(source.Length == 0 ? "이미지 경로를 위에 입력하세요" : "원격 이미지는 표시하지 않습니다: " + source);
                    return;
                }

                var dpi = 1.0;
                var presentation = PresentationSource.FromVisual(Root);
                if (presentation != null && presentation.CompositionTarget != null)
                    dpi = presentation.CompositionTarget.TransformToDevice.M11;
                var width = (_owner._panel.ActualWidth > 0 ? Math.Min(_owner._panel.ActualWidth, ReadableWidth + GutterWidth) : ReadableWidth) * dpi;
                var decodeWidth = (int)Math.Min(2400, Math.Ceiling(width / 400) * 400);
                var key = path + "|" + decodeWidth;
                if (key == _imageKey)
                    return;
                _imageKey = key;
                if (_image.Source == null)
                    ShowImageStatus("이미지 불러오는 중… " + source);

                var id = Block.Id;
                BlockImageLoader.Load(path, decodeWidth, (image, error) =>
                {
                    if (Block.Id != id || _imageKey != key)
                        return;
                    _image.Source = image;
                    ShowImageStatus(image == null ? "이미지를 열 수 없습니다: " + source + (error != null ? " (" + error + ")" : string.Empty) : null);
                });
            }

            private static double FontSizeOf(BlockKind kind)
            {
                switch (kind)
                {
                    case BlockKind.Heading1: return 28;
                    case BlockKind.Heading2: return 22;
                    case BlockKind.Heading3: return 18;
                    case BlockKind.Code: return 13.5;
                    case BlockKind.Raw: return 13;
                    case BlockKind.Image: return 13;
                    default: return 15;
                }
            }

            private static string PlaceholderOf(BlockKind kind)
            {
                switch (kind)
                {
                    case BlockKind.Heading1: return "제목 1";
                    case BlockKind.Heading2: return "제목 2";
                    case BlockKind.Heading3: return "제목 3";
                    case BlockKind.Bulleted:
                    case BlockKind.Numbered: return "목록";
                    case BlockKind.Todo: return "할 일";
                    case BlockKind.Toggle: return "토글";
                    case BlockKind.Quote: return "인용";
                    case BlockKind.Callout: return "콜아웃";
                    case BlockKind.Code: return "코드";
                    case BlockKind.Raw: return "Markdown 원문";
                    case BlockKind.Image: return "캡션(대체 텍스트)";
                    default: return "글을 쓰거나 '/'를 눌러 명령 사용";
                }
            }

            private static string CalloutIcon(string type)
            {
                switch (type)
                {
                    case "TIP": return "💡";
                    case "IMPORTANT": return "❗";
                    case "WARNING": return "⚠";
                    case "CAUTION": return "⛔";
                    default: return "ℹ";
                }
            }
        }
    }
}

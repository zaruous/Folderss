using Folderss.Models;
using Folderss.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
            private StackPanel _tocPanel;
            private readonly List<EditorBlock> _tocBlocks = new List<EditorBlock>();   // 지금 목차에 그린 제목 블록·단계·항목 글자
            private readonly List<int> _tocLevels = new List<int>();
            private readonly List<TextBlock> _tocTexts = new List<TextBlock>();
            private Border _mediaCard;
            private Popup _calloutPopup;
            private UIElement _marker;
            private TextBlock _markerText;
            private CheckBox _checkBox;
            private BlockKind? _appliedKind;
            private int _appliedToggleHeading;
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
            private Grid _imageContainer;
            private Image _image;
            private Thumb _imageResizeThumb;
            private Border _imageToolbar;
            private TextBlock _imageStatus;
            private Button _loadRemoteOnceBtn;
            private bool _allowRemoteOnce;
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
                _display = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 3, 2, 3), Background = Brushes.Transparent };
                _display.MouseLeftButtonDown += (s, e) => Root_MouseLeftButtonDown(s, e);
                Grid.SetColumn(_display, 1);
                Grid.SetRow(_display, 1);
                _bodyGrid.Children.Add(_display);

                Root.MouseEnter += (s, e) =>
                {
                    EnsureGutter().Opacity = 1;
                    SetTableButtonsShown(true);
                    SetImageControlsShown(true);
                    // 누르기 전에 입력란을 만들어 두어 첫 클릭·끌어 선택이 TextBox에 그대로 간다.
                    // 단, 수식(Math)은 마우스를 올렸을 때 렌더링된 수식이 유지되도록 클릭할 때만 편집 모드로 들어간다.
                    if (Block.Kind != BlockKind.Divider && Block.Kind != BlockKind.Toc && Block.Kind != BlockKind.Math && _owner._dragRow == null)
                        EnsureEditing();
                };
                Root.MouseLeave += (s, e) =>
                {
                    if (_gutter != null && _owner._dragRow != this)
                        _gutter.Opacity = 0;
                    SetTableButtonsShown(false);
                    SetImageControlsShown(false);
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
                // 입력란 밖(들여쓰기·여백·이미지)을 누르면 그 블록 끝으로, 입력란이 없는 구분선·목차는 블록 선택.
                if (e.Handled)
                    return;
                e.Handled = true;
                var index = _owner.IndexOf(this);
                if (Block.Kind == BlockKind.Divider || Block.Kind == BlockKind.Toc)
                {
                    _owner.SelectBlocks(index, index);
                }
                else
                {
                    EnsureEditing();
                    if (Block.Kind == BlockKind.Math && _mainEditor != null)
                    {
                        _display.Visibility = Visibility.Collapsed;
                        _mainEditor.Visibility = Visibility.Visible;
                    }
                    _owner.FocusRow(index, int.MaxValue, ActiveColumn);
                }
            }

            /// <summary>화면 밖으로 나가 쉬는 행의 일시 상태를 지운다(다른 블록에 다시 묶일 때 남지 않게).</summary>
            public void ResetTransient()
            {
                if (_gutter != null)
                    _gutter.Opacity = 0;
                if (_highlight != null)
                    _highlight.Visibility = Visibility.Collapsed;
                if (_calloutPopup != null)
                    _calloutPopup.IsOpen = false;
                SetTableButtonsShown(false);
                SetImageControlsShown(false);
            }

            /// <summary>같은 행에 다른 블록(되돌린 사본이나 가상화로 다시 쓰는 행)을 묶고 화면을 맞춘다.</summary>
            public void Rebind(EditorBlock block)
            {
                if (block.Id != Block.Id)
                {
                    ActiveColumn = 0;
                    _imageKey = null;
                    _allowRemoteOnce = false;
                }
                Block = block;
                SyncFromBlock();
            }

            /// <summary>블록 내용을 화면에 반영한다. 종류가 바뀌었을 때만 모양 전체를 다시 만든다.</summary>
            public void SyncFromBlock()
            {
                var toggleHeading = Block.GetToggleHeadingLevel();
                if (_appliedKind != Block.Kind || _appliedToggleHeading != toggleHeading)
                    ApplyKind();

                _owner._suppressTextChanged = true;
                try
                {
                    if (Block.Kind == BlockKind.TableRow)
                    {
                        SyncCells();
                    }
                    else if (Block.Kind == BlockKind.Toc)
                    {
                        RebuildTocContent();
                    }
                    else
                    {
                        UpdateDisplayContent();
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
                else if (EditorBlock.IsLinkCard(Block.Kind))
                    RefreshMediaCard();
            }

            /// <summary>표시용 <see cref="_display"/> 내용을 갱신한다(수식 블록 또는 인라인 <c>$...$</c> 수식 포함).</summary>
            private void UpdateDisplayContent()
            {
                var text = Block.Text ?? string.Empty;
                if (Block.Kind == BlockKind.Math)
                {
                    _display.TextAlignment = TextAlignment.Center;
                    BlockMathRenderer.PopulateInlines(_display.Inlines, string.IsNullOrWhiteSpace(text) ? "\\text{(클릭하여 수식 입력)}" : text, 16);
                    return;
                }

                _display.TextAlignment = TextAlignment.Left;
                if (!EditorBlock.IsVerbatim(Block.Kind) && BlockMathRenderer.ContainsInlineMath(text))
                {
                    BlockMathRenderer.PopulateMixedInlines(_display.Inlines, text, FontSizeOf(Block));
                    return;
                }

                if (_display.Inlines.Count > 1 || _display.Text != text)
                {
                    _display.Inlines.Clear();
                    _display.Text = text;
                }
            }

            private string MetaValue()
            {
                return (Block.Kind == BlockKind.Image || EditorBlock.IsLinkCard(Block.Kind)
                    ? Block.Source
                    : Block.Language) ?? string.Empty;
            }

            // ── 편집 모드(TextBox) ─────────────────────────────────────────

            /// <summary>이 행을 편집할 수 있게 입력란(TextBox)을 만든다. 한 번 만들면 행이 거둬질 때까지 둔다.</summary>
            public void EnsureEditing()
            {
                if (Block.Kind == BlockKind.Divider || Block.Kind == BlockKind.Toc)
                    return;
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
                var showMeta = kind == BlockKind.Code || kind == BlockKind.Image || EditorBlock.IsLinkCard(kind);
                if (!_editing)
                {
                    _display.Visibility = kind == BlockKind.Divider || kind == BlockKind.Toc || kind == BlockKind.TableRow ? Visibility.Collapsed : Visibility.Visible;
                    if (_metaDisplay != null)
                        _metaDisplay.Visibility = showMeta ? Visibility.Visible : Visibility.Collapsed;
                    return;
                }

                if (_metaDisplay != null)
                    _metaDisplay.Visibility = Visibility.Collapsed;
                if (showMeta)
                    EnsureMetaBox().Visibility = Visibility.Visible;
                else if (_metaBox != null)
                    _metaBox.Visibility = Visibility.Collapsed;

                if (kind == BlockKind.TableRow)
                {
                    _display.Visibility = Visibility.Collapsed;
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

                if (kind == BlockKind.Divider || kind == BlockKind.Toc)
                {
                    _display.Visibility = Visibility.Collapsed;
                    if (_mainEditor != null)
                        _mainEditor.Visibility = Visibility.Collapsed;
                    return;
                }

                if (_mainEditor == null)
                    CreateMainEditor();

                // 수식 블록은 키보드 포커스가 있을 때만 원문 TextBox를 보여주고, 평소에는 렌더링된 수식(_display)을 보여준다.
                if (kind == BlockKind.Math && !_mainEditor.IsKeyboardFocused && !string.IsNullOrWhiteSpace(Block.Text))
                {
                    _mainEditor.Visibility = Visibility.Collapsed;
                    _display.Visibility = Visibility.Visible;
                }
                else
                {
                    _display.Visibility = Visibility.Collapsed;
                    _mainEditor.Visibility = Visibility.Visible;
                }

                var editorRow = kind == BlockKind.Image || EditorBlock.IsLinkCard(kind) ? 2 : 1;
                Grid.SetRow(_mainEditor, editorRow);
                ApplyTextStyle(_mainEditor);
                _mainEditor.AcceptsTab = EditorBlock.IsVerbatim(kind);
                _mainEditor.ToolTip = kind == BlockKind.Raw
                    ? "블록 편집기가 해석하지 않는 Markdown 원문(front matter·HTML 등). 그대로 저장됩니다."
                    : kind == BlockKind.Math
                        ? "LaTeX 수식 입력 (예: \\frac{a}{b}, \\sqrt{x^2 + y^2}, \\sum_{i=1}^{n} x_i)"
                        : null;
                _appliedTextColor = null;
            }

            private void CreateMainEditor()
            {
                _mainEditor = CreateTextBox(Block.Text);
                Grid.SetColumn(_mainEditor, 1);
                Grid.SetRow(_mainEditor, Block.Kind == BlockKind.Image || EditorBlock.IsLinkCard(Block.Kind) ? 2 : 1);
                _bodyGrid.Children.Add(_mainEditor);

                _mainEditor.TextChanged += (s, e) =>
                {
                    _owner.OnEditorTextChanged(this, e);
                    if (EditorBlock.IsLinkCard(Block.Kind))
                        RefreshMediaCard();
                };
                _mainEditor.GotKeyboardFocus += (s, e) =>
                {
                    if (Block.Kind == BlockKind.Math)
                    {
                        _display.Visibility = Visibility.Collapsed;
                        _mainEditor.Visibility = Visibility.Visible;
                    }
                    _owner.OnEditorFocused(this);
                };
                _mainEditor.LostKeyboardFocus += (s, e) =>
                {
                    if (Block.Kind == BlockKind.Math && !string.IsNullOrWhiteSpace(Block.Text))
                    {
                        UpdateDisplayContent();
                        _mainEditor.Visibility = Visibility.Collapsed;
                        _display.Visibility = Visibility.Visible;
                    }
                    _owner.OnEditorBlurred(this);
                };
                _mainEditor.SelectionChanged += (s, e) => _owner.OnEditorSelectionChanged(this);
                DataObject.AddPastingHandler(_mainEditor, (s, e) => _owner.OnEditorPasting(this, e));
                // 복사한 파일·이미지: TextBox는 글자가 없으면 붙여넣기를 막고(Pasting이 오지 않음) 폴더 패널 복사는 파일 이름만 붙이므로,
                // 명령을 먼저 받아 이미지·파일 블록으로 넣는다.
                CommandManager.AddPreviewCanExecuteHandler(_mainEditor, (s, e) =>
                {
                    if (e.Command == ApplicationCommands.Paste && _owner.CanPasteMedia(this))
                    {
                        e.CanExecute = true;
                        e.Handled = true;
                    }
                });
                CommandManager.AddPreviewExecutedHandler(_mainEditor, (s, e) =>
                {
                    if (e.Command == ApplicationCommands.Paste && _owner.CanPasteMedia(this))
                    {
                        e.Handled = true;
                        _owner.PasteMedia(_owner.IndexOf(this));
                    }
                });
            }

            /// <summary>글꼴·크기·굵기를 종류에 맞춘다(표시용 TextBlock과 TextBox 공통).</summary>
            private void ApplyTextStyle(FrameworkElement target)
            {
                var kind = Block.Kind;
                var headingLevel = Block.GetHeadingLevel();
                if (kind == BlockKind.Code || kind == BlockKind.Raw || (kind == BlockKind.Math && ReferenceEquals(target, _mainEditor)))
                    target.SetValue(TextElement.FontFamilyProperty, CodeFont);
                else
                    target.SetResourceReference(TextElement.FontFamilyProperty, "AppFontFamily");
                target.SetValue(TextElement.FontSizeProperty, FontSizeOf(Block));
                target.SetValue(TextElement.FontWeightProperty, headingLevel == 1 ? FontWeights.Bold
                    : headingLevel == 2 || headingLevel == 3 ? FontWeights.SemiBold
                    : FontWeights.Normal);
            }

            private void ApplyKind()
            {
                var kind = Block.Kind;
                var toggleHeading = Block.GetToggleHeadingLevel();
                var headingLevel = Block.GetHeadingLevel();
                var verbatim = EditorBlock.IsVerbatim(kind);
                _appliedKind = kind;
                _appliedToggleHeading = toggleHeading;
                _appliedCallout = null;
                _appliedTextColor = null;

                ApplyTextStyle(_display);
                Grid.SetRow(_display, kind == BlockKind.Image || EditorBlock.IsLinkCard(kind) ? 2 : 1);

                if (kind == BlockKind.Divider)
                    EnsureDivider().Visibility = Visibility.Visible;
                else if (_divider != null)
                    _divider.Visibility = Visibility.Collapsed;

                if (kind == BlockKind.Toc)
                {
                    EnsureTocPanel().Visibility = Visibility.Visible;
                    RebuildTocContent();
                }
                else if (_tocPanel != null)
                {
                    _tocPanel.Visibility = Visibility.Collapsed;
                }

                if (EditorBlock.IsLinkCard(kind))
                {
                    EnsureMediaCard().Visibility = Visibility.Visible;
                }
                else if (_mediaCard != null)
                {
                    _mediaCard.Visibility = Visibility.Collapsed;
                }

                if (kind == BlockKind.Code || kind == BlockKind.Image || EditorBlock.IsLinkCard(kind))
                {
                    var meta = EnsureMetaDisplay();
                    meta.Text = MetaValue();
                    var tip = kind == BlockKind.Image ? "이미지 경로 (문서 폴더 기준 상대 경로, 절대 경로 또는 http/https 주소)"
                        : kind == BlockKind.File ? "첨부 파일 경로 (문서 폴더 기준 상대 경로 또는 절대 경로)"
                        : kind == BlockKind.Bookmark ? "북마크 웹 주소 (https://...)"
                        : kind == BlockKind.Page ? "페이지(.md 문서) 경로 (문서 폴더 기준 상대 경로 또는 절대 경로)"
                        : "코드 언어 (예: csharp, python, json)";
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

                Root.Margin = headingLevel == 1 ? new Thickness(0, 22, 0, 2)
                    : headingLevel == 2 ? new Thickness(0, 16, 0, 2)
                    : headingLevel == 3 ? new Thickness(0, 10, 0, 1)
                    : kind == BlockKind.TableRow ? new Thickness(0)
                    : verbatim || kind == BlockKind.Callout || kind == BlockKind.Image || kind == BlockKind.Toc || EditorBlock.IsLinkCard(kind) ? new Thickness(0, 4, 0, 4)
                    : new Thickness(0, 1, 0, 1);
                _gutterTop = headingLevel == 1 ? 9 : headingLevel == 2 ? 5 : verbatim || kind == BlockKind.Callout || kind == BlockKind.Toc || EditorBlock.IsLinkCard(kind) ? 8 : 2;
                if (_gutter != null)
                    _gutter.Margin = new Thickness(0, _gutterTop, 4, 0);

                ApplyBodyStyle();
                SetMarker(CreateMarker(kind));
            }

            /// <summary>본문 테두리(인용 막대·콜아웃·코드·수식·목차 상자)와 블록 배경색(사이드카 속성)을 칠한다.</summary>
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
                    case BlockKind.Math:
                        _body.SetResourceReference(Border.BackgroundProperty, "SurfaceBackground");
                        _body.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                        _body.BorderThickness = new Thickness(1);
                        _body.CornerRadius = new CornerRadius(5);
                        _body.Padding = new Thickness(14, 8, 14, 10);
                        break;
                    case BlockKind.Toc:
                        _body.SetResourceReference(Border.BackgroundProperty, "SurfaceBackground");
                        _body.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                        _body.BorderThickness = new Thickness(1);
                        _body.CornerRadius = new CornerRadius(5);
                        _body.Padding = new Thickness(12, 8, 12, 8);
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
                        _markerText.FontSize = FontSizeOf(Block) + 2;
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
                        var toggleH = Block.GetToggleHeadingLevel();
                        _markerText = CreateMarkerText(string.Empty, 24, TextAlignment.Center);
                        _markerText.FontSize = toggleH == 1 ? 18 : toggleH == 2 ? 16 : 15;
                        _markerText.Margin = new Thickness(0, toggleH == 1 ? 6 : toggleH == 2 ? 4 : 3, 2, 0);
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
                            ShowCalloutIconPicker();
                        };
                        return _markerText;
                    default:
                        return null;
                }
            }

            private void ShowCalloutIconPicker()
            {
                if (_markerText == null)
                    return;

                var stack = new StackPanel { Margin = new Thickness(4) };
                var typeRow = new WrapPanel { Margin = new Thickness(2, 2, 2, 6) };
                foreach (var calloutType in BlockMarkdown.CalloutTypes)
                {
                    var t = calloutType;
                    var btn = new Button
                    {
                        Content = CalloutIcon(t) + " " + t,
                        Margin = new Thickness(2),
                        Padding = new Thickness(6, 2, 6, 2),
                        FontSize = 11,
                        Cursor = Cursors.Hand,
                    };
                    btn.Click += (_, __) =>
                    {
                        Block.CalloutType = t;
                        if (_calloutPopup != null)
                            _calloutPopup.IsOpen = false;
                        _appliedCallout = null;
                        RefreshState();
                        _owner.RecordChange();
                    };
                    typeRow.Children.Add(btn);
                }
                stack.Children.Add(typeRow);

                var emojiLabel = new TextBlock { Text = "아이콘 선택 (사이드카 저장)", FontSize = 11, Margin = new Thickness(4, 0, 4, 4) };
                emojiLabel.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
                stack.Children.Add(emojiLabel);

                var emojis = new[]
                {
                    "💡", "ℹ", "❗", "⚠", "⛔", "✅", "📌", "📝", "🔥", "🚀", "🎯", "💬", "🔍", "⭐", "📎", "🔒", "⏱", "🎉"
                };
                var emojiWrap = new WrapPanel { MaxWidth = 230, Margin = new Thickness(2) };
                foreach (var emoji in emojis)
                {
                    var icon = emoji;
                    var btn = new Button
                    {
                        Content = icon,
                        Width = 32,
                        Height = 28,
                        Margin = new Thickness(2),
                        FontSize = 14,
                        Cursor = Cursors.Hand,
                    };
                    btn.Click += (_, __) =>
                    {
                        Block.SetAttribute(EditorBlock.AttrIcon, icon);
                        if (_calloutPopup != null)
                            _calloutPopup.IsOpen = false;
                        _appliedCallout = null;
                        RefreshState();
                        _owner.RecordChange();
                    };
                    emojiWrap.Children.Add(btn);
                }
                stack.Children.Add(emojiWrap);

                if (Block.GetAttribute(EditorBlock.AttrIcon) != null)
                {
                    var resetBtn = new Button
                    {
                        Content = "기본 아이콘으로 되돌리기",
                        Margin = new Thickness(2, 6, 2, 2),
                        Padding = new Thickness(6, 2, 6, 2),
                        FontSize = 11,
                    };
                    resetBtn.Click += (_, __) =>
                    {
                        Block.SetAttribute(EditorBlock.AttrIcon, null);
                        if (_calloutPopup != null)
                            _calloutPopup.IsOpen = false;
                        _appliedCallout = null;
                        RefreshState();
                        _owner.RecordChange();
                    };
                    stack.Children.Add(resetBtn);
                }

                var border = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(6),
                    Child = stack,
                };
                border.SetResourceReference(Border.BackgroundProperty, "SurfaceBackground");
                border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

                _calloutPopup = new Popup
                {
                    Child = border,
                    PlacementTarget = _markerText,
                    Placement = PlacementMode.Bottom,
                    StaysOpen = false,
                    AllowsTransparency = true,
                };
                _calloutPopup.IsOpen = true;
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
                var marker = new TextBlock { Text = text, TextAlignment = alignment, FontSize = FontSizeOf(Block), VerticalAlignment = VerticalAlignment.Top };
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

            private StackPanel EnsureTocPanel()
            {
                if (_tocPanel != null)
                    return _tocPanel;
                _tocPanel = new StackPanel();
                Grid.SetColumn(_tocPanel, 1);
                Grid.SetRow(_tocPanel, 1);
                _bodyGrid.Children.Add(_tocPanel);
                return _tocPanel;
            }

            /// <summary>문서 내 제목 블록들을 스캔해 클릭하면 이동하는 목차 항목을 다시 만든다.</summary>
            public void RebuildTocContent()
            {
                var headings = new List<EditorBlock>();
                foreach (var b in _owner._blocks)
                {
                    if (b.GetHeadingLevel() > 0)
                        headings.Add(b);
                }
                // 제목 목록(블록·단계)이 그대로면 바뀐 글자만 고친다 — 제목에 입력할 때마다 항목 수백 개를 다시 만들지 않게.
                if (_tocPanel != null && SameTocHeadings(headings))
                {
                    for (var i = 0; i < headings.Count; i++)
                    {
                        var text = TocItemText(headings[i]);
                        if (_tocTexts[i].Text != text)
                            _tocTexts[i].Text = text;
                    }
                    return;
                }
                _tocBlocks.Clear();
                _tocLevels.Clear();
                _tocTexts.Clear();

                var panel = EnsureTocPanel();
                panel.Children.Clear();

                var header = new TextBlock
                {
                    Text = "목차",
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(2, 0, 2, 4),
                };
                header.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
                panel.Children.Add(header);

                foreach (var b in headings)
                {
                    var level = b.GetHeadingLevel();
                    var targetBlock = b;
                    var rowText = new TextBlock
                    {
                        Text = TocItemText(b),
                        FontSize = level == 1 ? 13.5 : level == 2 ? 13 : 12.5,
                        FontWeight = level == 1 ? FontWeights.SemiBold : FontWeights.Normal,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    };
                    rowText.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");

                    var itemBorder = new Border
                    {
                        Padding = new Thickness(4 + (level - 1) * 16, 3, 6, 3),
                        CornerRadius = new CornerRadius(3),
                        Background = Brushes.Transparent,
                        Cursor = Cursors.Hand,
                        Child = rowText,
                    };
                    itemBorder.MouseEnter += (_, __) => itemBorder.SetResourceReference(Border.BackgroundProperty, "ControlHoverBrush");
                    itemBorder.MouseLeave += (_, __) => itemBorder.Background = Brushes.Transparent;
                    itemBorder.MouseLeftButtonDown += (_, ev) =>
                    {
                        ev.Handled = true;
                        var idx = _owner._blocks.IndexOf(targetBlock);
                        if (idx >= 0)
                        {
                            _owner.BringRowIntoView(idx);
                            _owner.FocusRow(idx, 0);
                        }
                    };
                    panel.Children.Add(itemBorder);
                    _tocBlocks.Add(b);
                    _tocLevels.Add(level);
                    _tocTexts.Add(rowText);
                }

                if (headings.Count == 0)
                {
                    var empty = new TextBlock
                    {
                        Text = "문서에 제목 블록(#, ##, ###)을 추가하면 목차가 자동으로 표시됩니다.",
                        FontSize = 12,
                        Margin = new Thickness(4, 2, 4, 2),
                    };
                    empty.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
                    panel.Children.Add(empty);
                }
            }

            private bool SameTocHeadings(List<EditorBlock> headings)
            {
                if (headings.Count != _tocBlocks.Count)
                    return false;
                for (var i = 0; i < headings.Count; i++)
                {
                    if (!ReferenceEquals(headings[i], _tocBlocks[i]) || headings[i].GetHeadingLevel() != _tocLevels[i])
                        return false;
                }
                return true;
            }

            private static string TocItemText(EditorBlock block)
            {
                return string.IsNullOrWhiteSpace(block.Text) ? "(빈 제목)" : block.Text.Trim();
            }

            private Border EnsureMediaCard()
            {
                if (_mediaCard != null)
                    return _mediaCard;
                _mediaCard = new Border
                {
                    CornerRadius = new CornerRadius(5),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 2, 0, 2),
                };
                _mediaCard.SetResourceReference(Border.BackgroundProperty, "SurfaceBackground");
                _mediaCard.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                Grid.SetColumn(_mediaCard, 1);
                Grid.SetRow(_mediaCard, 1);
                _bodyGrid.Children.Add(_mediaCard);
                return _mediaCard;
            }

            /// <summary>파일 첨부(<see cref="BlockKind.File"/>) 또는 북마크(<see cref="BlockKind.Bookmark"/>) 카드를 다시 그린다.</summary>
            public void RefreshMediaCard()
            {
                var card = EnsureMediaCard();
                if (Block.Kind == BlockKind.File)
                {
                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var icon = new TextBlock
                    {
                        Text = "📎",
                        FontSize = 18,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 10, 0),
                    };
                    grid.Children.Add(icon);

                    var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    var displayName = !string.IsNullOrWhiteSpace(Block.Text)
                        ? Block.Text
                        : !string.IsNullOrWhiteSpace(Block.Source)
                            ? Path.GetFileName(Block.Source)
                            : "(파일 경로를 입력하세요)";
                    var titleBlock = new TextBlock
                    {
                        Text = displayName,
                        FontSize = 13.5,
                        FontWeight = FontWeights.SemiBold,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    };
                    titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
                    infoStack.Children.Add(titleBlock);

                    var sizeAttr = Block.GetAttribute(EditorBlock.AttrSize);
                    var resolved = _owner.ResolveImagePath(Block.Source);
                    if (string.IsNullOrEmpty(sizeAttr) && !string.IsNullOrEmpty(resolved) && File.Exists(resolved))
                    {
                        try
                        {
                            sizeAttr = FormatFileSize(new FileInfo(resolved).Length);
                            Block.SetAttribute(EditorBlock.AttrSize, sizeAttr);
                        }
                        catch
                        {
                        }
                    }

                    var subText = (Block.Source ?? string.Empty) + (!string.IsNullOrEmpty(sizeAttr) ? " · " + sizeAttr : string.Empty);
                    var subBlock = new TextBlock
                    {
                        Text = subText,
                        FontSize = 11.5,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Margin = new Thickness(0, 2, 0, 0),
                    };
                    subBlock.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
                    infoStack.Children.Add(subBlock);
                    Grid.SetColumn(infoStack, 1);
                    grid.Children.Add(infoStack);

                    var btnStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    var openBtn = new Button
                    {
                        Content = "열기",
                        Padding = new Thickness(8, 3, 8, 3),
                        Margin = new Thickness(6, 0, 0, 0),
                        FontSize = 11.5,
                    };
                    openBtn.Click += (_, ev) =>
                    {
                        ev.Handled = true;
                        _owner.OpenLocalTarget(_owner.ResolveImagePath(Block.Source));
                    };
                    var folderBtn = new Button
                    {
                        Content = "탐색기",
                        Padding = new Thickness(8, 3, 8, 3),
                        Margin = new Thickness(4, 0, 0, 0),
                        FontSize = 11.5,
                    };
                    folderBtn.Click += (_, ev) =>
                    {
                        ev.Handled = true;
                        _owner.RevealInExplorer(_owner.ResolveImagePath(Block.Source));
                    };
                    btnStack.Children.Add(openBtn);
                    btnStack.Children.Add(folderBtn);
                    Grid.SetColumn(btnStack, 2);
                    grid.Children.Add(btnStack);

                    card.Child = grid;
                }
                else if (Block.Kind == BlockKind.Bookmark)
                {
                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var icon = new TextBlock
                    {
                        Text = "🔖",
                        FontSize = 18,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, 2, 10, 0),
                    };
                    grid.Children.Add(icon);

                    var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    var titleText = !string.IsNullOrWhiteSpace(Block.Text)
                        ? Block.Text
                        : Block.GetAttribute(EditorBlock.AttrTitle) ?? Block.Source ?? "(웹 주소를 입력하세요)";
                    var titleBlock = new TextBlock
                    {
                        Text = titleText,
                        FontSize = 13.5,
                        FontWeight = FontWeights.SemiBold,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    };
                    titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
                    infoStack.Children.Add(titleBlock);

                    var desc = Block.GetAttribute(EditorBlock.AttrDescription);
                    if (!string.IsNullOrWhiteSpace(desc))
                    {
                        var descBlock = new TextBlock
                        {
                            Text = desc,
                            FontSize = 12,
                            TextWrapping = TextWrapping.Wrap,
                            MaxHeight = 36,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            Margin = new Thickness(0, 2, 0, 0),
                        };
                        descBlock.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
                        infoStack.Children.Add(descBlock);
                    }

                    var urlBlock = new TextBlock
                    {
                        Text = Block.Source ?? string.Empty,
                        FontSize = 11,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Margin = new Thickness(0, 3, 0, 0),
                    };
                    urlBlock.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                    infoStack.Children.Add(urlBlock);

                    Grid.SetColumn(infoStack, 1);
                    grid.Children.Add(infoStack);

                    var btnStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    var fetchBtn = new Button
                    {
                        Content = "미리보기 불러오기",
                        Padding = new Thickness(8, 3, 8, 3),
                        Margin = new Thickness(6, 0, 0, 0),
                        FontSize = 11.5,
                        ToolTip = "웹페이지에 접속해 제목과 설명을 가져옵니다",
                    };
                    fetchBtn.Click += (_, ev) =>
                    {
                        ev.Handled = true;
                        FetchBookmarkPreview();
                    };
                    var openBtn = new Button
                    {
                        Content = "열기 ↗",
                        Padding = new Thickness(8, 3, 8, 3),
                        Margin = new Thickness(4, 0, 0, 0),
                        FontSize = 11.5,
                    };
                    openBtn.Click += (_, ev) =>
                    {
                        ev.Handled = true;
                        OpenBookmarkInBrowser();
                    };
                    btnStack.Children.Add(fetchBtn);
                    btnStack.Children.Add(openBtn);
                    Grid.SetColumn(btnStack, 2);
                    grid.Children.Add(btnStack);

                    card.Child = grid;
                }
                else if (Block.Kind == BlockKind.Page)
                {
                    // 페이지 링크: 제목을 누르면 그 문서를 새 탭으로 연다
                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    grid.Children.Add(new TextBlock { Text = "📄", FontSize = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });

                    var path = _owner.ResolveImagePath(Block.Source);
                    var exists = path != null && File.Exists(path);
                    var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    var titleBlock = new TextBlock
                    {
                        Text = PageTitle(Block),
                        FontSize = 13.5,
                        FontWeight = FontWeights.SemiBold,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        TextDecorations = TextDecorations.Underline,
                        Cursor = Cursors.Hand,
                        ToolTip = "페이지 열기",
                    };
                    titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
                    titleBlock.MouseLeftButtonDown += (_, ev) =>
                    {
                        ev.Handled = true;
                        _owner.OpenPage(Block);
                    };
                    infoStack.Children.Add(titleBlock);
                    var subBlock = new TextBlock
                    {
                        Text = (Block.Source ?? string.Empty) + (exists || string.IsNullOrWhiteSpace(Block.Source) ? string.Empty : " · 파일 없음"),
                        FontSize = 11.5,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Margin = new Thickness(0, 2, 0, 0),
                    };
                    subBlock.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
                    infoStack.Children.Add(subBlock);
                    Grid.SetColumn(infoStack, 1);
                    grid.Children.Add(infoStack);

                    var openBtn = new Button
                    {
                        Content = exists ? "열기" : "만들기",
                        Padding = new Thickness(8, 3, 8, 3),
                        Margin = new Thickness(6, 0, 0, 0),
                        FontSize = 11.5,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    openBtn.Click += (_, ev) =>
                    {
                        ev.Handled = true;
                        _owner.OpenPage(Block);
                    };
                    Grid.SetColumn(openBtn, 2);
                    grid.Children.Add(openBtn);

                    card.Child = grid;
                }
            }

            /// <summary>페이지 링크 제목: 적은 제목, 없으면 파일 이름(확장자 제외).</summary>
            private static string PageTitle(EditorBlock block)
            {
                if (!string.IsNullOrWhiteSpace(block.Text))
                    return block.Text;
                var name = Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(block.Source ?? string.Empty));
                return string.IsNullOrWhiteSpace(name) ? "(페이지 경로를 입력하세요)" : name;
            }

            public void OpenBookmarkInBrowser()
            {
                var url = (Block.Source ?? string.Empty).Trim();
                Uri uri;
                if (!Uri.TryCreate(url, UriKind.Absolute, out uri) ||
                    (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show(Window.GetWindow(_owner), "유효한 http:// 또는 https:// 웹 주소가 아닙니다.", "북마크 열기", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                try
                {
                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show(Window.GetWindow(_owner), "브라우저를 열 수 없습니다.\n" + ex.Message, "북마크 열기", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            public async void FetchBookmarkPreview()
            {
                var url = (Block.Source ?? string.Empty).Trim();
                Uri uri;
                if (!Uri.TryCreate(url, UriKind.Absolute, out uri) ||
                    (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show(Window.GetWindow(_owner), "유효한 http:// 또는 https:// 웹 주소를 먼저 입력하세요.", "북마크 미리보기", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var id = Block.Id;
                Tuple<string, string> meta;
                try
                {
                    meta = await BlockImageLoader.FetchPageMetaAsync(uri);
                }
                catch (Exception ex)
                {
                    if (Block.Id == id)
                        MessageBox.Show(Window.GetWindow(_owner), "페이지 정보를 가져오지 못했습니다: " + ex.Message, "북마크 미리보기", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (Block.Id != id || meta == null)
                    return;

                var title = meta.Item1;
                var desc = meta.Item2;
                if (!string.IsNullOrWhiteSpace(title))
                {
                    Block.SetAttribute(EditorBlock.AttrTitle, title);
                    if (string.IsNullOrWhiteSpace(Block.Text) || string.Equals(Block.Text, Block.Source, StringComparison.OrdinalIgnoreCase))
                    {
                        Block.Text = title;
                        if (_mainEditor != null)
                        {
                            _owner._suppressTextChanged = true;
                            try { _mainEditor.Text = title; }
                            finally { _owner._suppressTextChanged = false; }
                        }
                    }
                }
                if (!string.IsNullOrWhiteSpace(desc))
                    Block.SetAttribute(EditorBlock.AttrDescription, desc);

                RefreshMediaCard();
                _owner._panel.InvalidateMeasure();
                _owner.RecordChange();
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
                _placeholder.Text = PlaceholderOf(Block);
                Grid.SetRow(_placeholder, Block.Kind == BlockKind.Image || EditorBlock.IsLinkCard(Block.Kind) ? 2 : 1);
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
                        var customIcon = Block.GetAttribute(EditorBlock.AttrIcon);
                        var calloutKey = type + "|" + (customIcon ?? string.Empty);
                        if (_markerText != null && _appliedCallout != calloutKey)
                        {
                            _markerText.Text = !string.IsNullOrEmpty(customIcon) ? customIcon : CalloutIcon(type);
                            _markerText.ToolTip = type + " — 클릭해 아이콘·종류 바꾸기";
                            _appliedCallout = calloutKey;
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
                var show = kind != BlockKind.Divider && kind != BlockKind.Toc && kind != BlockKind.TableRow && Block.Text.Length == 0 &&
                           (Block.GetHeadingLevel() > 0 || (_mainEditor != null && _mainEditor.IsKeyboardFocused));
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
                    Cursor = Cursors.Hand,
                };
                RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
                _image.MouseLeftButtonDown += (s, e) =>
                {
                    if (e.ClickCount == 2)
                    {
                        e.Handled = true;
                        OpenImagePreview();
                    }
                };

                _imageResizeThumb = new Thumb
                {
                    Width = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Cursor = Cursors.SizeWE,
                    Opacity = 0,
                    ToolTip = "끌기: 이미지 폭 조절 · 더블클릭: 원래 폭으로 초기화",
                };
                _imageResizeThumb.DragDelta += (s, e) =>
                {
                    if (_image.Source == null)
                        return;
                    var currentWidth = _image.ActualWidth > 0 ? _image.ActualWidth : 360;
                    var maxAvailable = Math.Max(160, _bodyGrid.ActualWidth > 20 ? _bodyGrid.ActualWidth - 16 : ReadableWidth);
                    var nextWidth = (int)Math.Max(80, Math.Min(maxAvailable, Math.Round(currentWidth + e.HorizontalChange)));
                    _image.MaxWidth = nextWidth;
                    Block.SetAttribute(EditorBlock.AttrWidth, nextWidth.ToString(CultureInfo.InvariantCulture));
                    _owner._panel.InvalidateMeasure();
                };
                _imageResizeThumb.DragCompleted += (s, e) => _owner.RecordChange();
                _imageResizeThumb.MouseDoubleClick += (s, e) =>
                {
                    e.Handled = true;
                    ResetImageWidth();
                };

                _imageToolbar = CreateImageToolbar();
                _imageContainer = new Grid
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                _imageContainer.Children.Add(_image);
                _imageContainer.Children.Add(_imageResizeThumb);
                _imageContainer.Children.Add(_imageToolbar);

                _imageStatus = new TextBlock
                {
                    Margin = new Thickness(12, 12, 12, 6),
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                _imageStatus.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");

                _loadRemoteOnceBtn = new Button
                {
                    Content = "이번만 불러오기",
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(12, 0, 12, 12),
                    Padding = new Thickness(10, 4, 10, 4),
                    Visibility = Visibility.Collapsed,
                };
                _loadRemoteOnceBtn.Click += (s, e) =>
                {
                    e.Handled = true;
                    _allowRemoteOnce = true;
                    _imageKey = null;
                    RefreshImage();
                };

                var statusStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                statusStack.Children.Add(_imageStatus);
                statusStack.Children.Add(_loadRemoteOnceBtn);

                var statusBox = new Border { MinHeight = 100, CornerRadius = new CornerRadius(4), Child = statusStack };
                statusBox.SetResourceReference(Border.BackgroundProperty, "ControlBackground");

                _imageHost = new Grid();
                _imageHost.Children.Add(statusBox);
                _imageHost.Children.Add(_imageContainer);
                Grid.SetColumn(_imageHost, 1);
                Grid.SetRow(_imageHost, 1);
                _bodyGrid.Children.Add(_imageHost);
                return _imageHost;
            }

            private Border CreateImageToolbar()
            {
                var stack = new StackPanel { Orientation = Orientation.Horizontal };
                stack.Children.Add(CreateImageToolButton("좌", "좌측 정렬", () => SetImageAlign("left")));
                stack.Children.Add(CreateImageToolButton("중", "가운데 정렬", () => SetImageAlign("center")));
                stack.Children.Add(CreateImageToolButton("우", "우측 정렬", () => SetImageAlign("right")));
                stack.Children.Add(CreateImageToolButton("1:1", "폭 초기화", ResetImageWidth));
                stack.Children.Add(CreateImageToolButton("🔍", "원본 크기로 보기", OpenImagePreview));
                stack.Children.Add(CreateImageToolButton("📁", "탐색기에서 열기", () =>
                {
                    var local = _owner.ResolveImagePath(Block.Source);
                    if (local != null)
                        _owner.RevealInExplorer(local);
                }));

                var bar = new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(6),
                    Padding = new Thickness(3, 2, 3, 2),
                    CornerRadius = new CornerRadius(4),
                    BorderThickness = new Thickness(1),
                    Opacity = 0,
                    Child = stack,
                };
                bar.SetResourceReference(Border.BackgroundProperty, "SurfaceBackground");
                bar.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                return bar;
            }

            private static Button CreateImageToolButton(string text, string tooltip, Action onClick)
            {
                var btn = new Button
                {
                    Content = text,
                    ToolTip = tooltip,
                    FontSize = 11,
                    Padding = new Thickness(5, 1, 5, 1),
                    Margin = new Thickness(1, 0, 1, 0),
                    Cursor = Cursors.Hand,
                };
                btn.Click += (s, e) =>
                {
                    e.Handled = true;
                    onClick();
                };
                return btn;
            }

            private void SetImageControlsShown(bool shown)
            {
                if (_imageToolbar == null)
                    return;
                var visible = shown && Block.Kind == BlockKind.Image && _image.Source != null && _owner._dragRow == null;
                _imageToolbar.Opacity = visible ? 0.94 : 0;
                _imageResizeThumb.Opacity = visible ? 0.85 : 0;
            }

            public void SetImageAlign(string align)
            {
                if (Block.Kind != BlockKind.Image)
                    return;
                Block.SetAttribute(EditorBlock.AttrAlign, string.Equals(align, "left", StringComparison.OrdinalIgnoreCase) ? null : align);
                ApplyImageLayoutAttributes();
                _owner.RecordChange();
            }

            public void ResetImageWidth()
            {
                if (Block.Kind != BlockKind.Image)
                    return;
                Block.SetAttribute(EditorBlock.AttrWidth, null);
                ApplyImageLayoutAttributes();
                _owner._panel.InvalidateMeasure();
                _owner.RecordChange();
            }

            public void OpenImagePreview()
            {
                if (Block.Kind != BlockKind.Image || _image?.Source == null)
                    return;
                var localPath = _owner.ResolveImagePath(Block.Source);
                var win = new BlockImagePreviewWindow(
                    _image.Source,
                    !string.IsNullOrWhiteSpace(Block.Text) ? Block.Text : Block.Source,
                    localPath,
                    localPath != null ? new Action(() => _owner.RevealInExplorer(localPath)) : null)
                {
                    Owner = Window.GetWindow(_owner),
                };
                win.ShowDialog();
            }

            private void ApplyImageLayoutAttributes()
            {
                if (_imageContainer == null || _image == null)
                    return;

                var align = Block.GetAttribute(EditorBlock.AttrAlign);
                _imageContainer.HorizontalAlignment = string.Equals(align, "center", StringComparison.OrdinalIgnoreCase)
                    ? HorizontalAlignment.Center
                    : string.Equals(align, "right", StringComparison.OrdinalIgnoreCase)
                        ? HorizontalAlignment.Right
                        : HorizontalAlignment.Left;

                int customWidth;
                if (int.TryParse(Block.GetAttribute(EditorBlock.AttrWidth), NumberStyles.Integer, CultureInfo.InvariantCulture, out customWidth) &&
                    customWidth >= 60 && customWidth <= 4000)
                {
                    _image.MaxWidth = customWidth;
                }
                else
                {
                    _image.MaxWidth = double.PositiveInfinity;
                }
            }

            private void ShowImageStatus(string text, bool showRemoteOnceButton = false)
            {
                var stack = (StackPanel)_imageStatus.Parent;
                var box = (Border)stack.Parent;
                box.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
                _imageStatus.Text = text ?? string.Empty;
                if (_loadRemoteOnceBtn != null)
                    _loadRemoteOnceBtn.Visibility = showRemoteOnceButton ? Visibility.Visible : Visibility.Collapsed;
            }

            /// <summary>이미지 경로가 바뀌었거나 아직 안 불러왔으면 비동기로 불러온다(디코딩은 백그라운드, 화면 폭에 맞춰 줄임).</summary>
            public void RefreshImage()
            {
                EnsureImageHost();
                ApplyImageLayoutAttributes();

                var source = (Block.Source ?? string.Empty).Trim();
                if (source.Length == 0)
                {
                    _image.Source = null;
                    _imageKey = null;
                    ShowImageStatus("이미지 경로를 위에 입력하거나 클립보드 이미지를 붙여넣으세요(Ctrl+V)");
                    return;
                }

                var dpi = 1.0;
                var presentation = PresentationSource.FromVisual(Root);
                if (presentation != null && presentation.CompositionTarget != null)
                    dpi = presentation.CompositionTarget.TransformToDevice.M11;
                var width = (_owner._panel.ActualWidth > 0 ? Math.Min(_owner._panel.ActualWidth, ReadableWidth + GutterWidth) : ReadableWidth) * dpi;
                var decodeWidth = (int)Math.Min(2400, Math.Ceiling(width / 400) * 400);

                var path = _owner.ResolveImagePath(source);
                if (path == null)
                {
                    Uri remoteUri;
                    if (Uri.TryCreate(source, UriKind.Absolute, out remoteUri) &&
                        (string.Equals(remoteUri.Scheme, "http", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(remoteUri.Scheme, "https", StringComparison.OrdinalIgnoreCase)))
                    {
                        var route = RemoteContentPolicy.RouteImage(remoteUri, NetworkSettingsService.Current);
                        var isLoopback = RemoteContentPolicy.IsLoopbackHost(remoteUri.Host);
                        var allowed = isLoopback || _allowRemoteOnce;
                        if (!allowed)
                        {
                            _image.Source = null;
                            _imageKey = null;
                            var policyNote = route == RemoteImageRoute.Block
                                ? "원격 이미지가 차단되어 있습니다: " + source
                                : "문서를 열 때 외부 요청이 나가지 않도록 원격 이미지는 수동으로 불러옵니다: " + source;
                            ShowImageStatus(policyNote, showRemoteOnceButton: true);
                            return;
                        }

                        var remoteKey = "remote|" + remoteUri.AbsoluteUri + "|" + decodeWidth;
                        if (remoteKey == _imageKey)
                            return;
                        _imageKey = remoteKey;
                        if (_image.Source == null)
                            ShowImageStatus("원격 이미지 불러오는 중… " + source);

                        var remoteId = Block.Id;
                        BlockImageLoader.LoadRemote(remoteUri, decodeWidth, (image, error) =>
                        {
                            if (Block.Id != remoteId || _imageKey != remoteKey)
                                return;
                            _image.Source = image;
                            ShowImageStatus(image == null ? "원격 이미지를 불러올 수 없습니다: " + source + (error != null ? " (" + error + ")" : string.Empty) : null);
                            _owner._panel.InvalidateMeasure();
                        });
                        return;
                    }

                    _image.Source = null;
                    _imageKey = null;
                    ShowImageStatus("이미지를 열 수 없습니다: " + source);
                    return;
                }

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
                    _owner._panel.InvalidateMeasure();
                });
            }

            private static double FontSizeOf(EditorBlock block)
            {
                if (block == null)
                    return 15;
                var toggleH = block.GetToggleHeadingLevel();
                if (toggleH == 1) return 24;
                if (toggleH == 2) return 20;
                if (toggleH == 3) return 17;

                switch (block.Kind)
                {
                    case BlockKind.Heading1: return 28;
                    case BlockKind.Heading2: return 22;
                    case BlockKind.Heading3: return 18;
                    case BlockKind.Code:
                    case BlockKind.Math: return 13.5;
                    case BlockKind.Raw: return 13;
                    case BlockKind.Image:
                    case BlockKind.File:
                    case BlockKind.Bookmark:
                    case BlockKind.Page: return 12.5;
                    default: return 15;
                }
            }

            private static string PlaceholderOf(EditorBlock block)
            {
                if (block == null)
                    return string.Empty;
                var toggleH = block.GetToggleHeadingLevel();
                if (toggleH == 1) return "토글 제목 1";
                if (toggleH == 2) return "토글 제목 2";
                if (toggleH == 3) return "토글 제목 3";

                switch (block.Kind)
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
                    case BlockKind.Math: return "LaTeX 수식 (예: E = mc^2, \\frac{a}{b})";
                    case BlockKind.Raw: return "Markdown 원문";
                    case BlockKind.Image: return "캡션(대체 텍스트)";
                    case BlockKind.File: return "표시할 파일 이름";
                    case BlockKind.Bookmark: return "표시할 북마크 제목";
                    case BlockKind.Page: return "페이지 제목";
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

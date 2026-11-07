using Folderss.Services;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;
using System;
using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Folderss.Viewers
{
    /// <summary>
    /// 일반 텍스트 편집기(<c>builtin:text</c>). AvalonEdit(WPF)로 그려 WebView 없이 빨리 열리고, 화면에 보이는 줄만 그려 큰 파일도 가볍다.
    /// <list type="bullet">
    /// <item>읽기·인코딩 감지·문서 만들기는 백그라운드에서(큰 파일에서도 화면이 멈추지 않음). 감지: BOM → UTF-8 → CP949(<see cref="TextFileEncoding"/>).</item>
    /// <item>저장은 읽은 인코딩(BOM 포함)·줄 끝을 유지한다. 상태 표시줄에서 다른 인코딩으로 다시 열기/저장 인코딩 바꾸기, CRLF↔LF 바꾸기.</item>
    /// <item>찾기/바꾸기(Ctrl+F/H, F3), 줄 이동(Ctrl+G) — <c>TextViewer.Find.cs</c>. 외부 변경 감시 — <c>TextViewer.External.cs</c>.</item>
    /// <item>줄 바꿈·글꼴 크기·공백 표시는 <see cref="TextEditorSettings"/>에 기억한다.</item>
    /// </list>
    /// </summary>
    public partial class TextViewer : UserControl, IFileViewer, IViewerShortcutHandler, IViewerToolbarProvider, IViewerActivationAware, IDisposable
    {
        /// <summary>여는 최대 크기. 넘으면 안내만 보인다(글 전체를 메모리에 올리므로).</summary>
        public const long MaxFileBytes = 256L * 1024 * 1024;

        /// <summary>이보다 큰 파일은 구문 강조를 끈다(끝으로 이동할 때 앞부분 강조 계산을 피함).</summary>
        public const long HighlightLimitBytes = 8L * 1024 * 1024;

        /// <summary>이보다 큰 파일은 읽는 동안 "읽는 중" 안내를 보인다.</summary>
        private const long LoadingNoticeBytes = 4L * 1024 * 1024;

        private const string ShortcutHelp =
            "Ctrl+S 저장 · Ctrl+Z / Ctrl+Y 실행 취소·다시 실행\n" +
            "Ctrl+F 찾기 · Ctrl+H 바꾸기 · F3 / Shift+F3 다음·이전 · Esc 닫기\n" +
            "  찾기 막대: Alt+C 대/소문자 · Alt+W 단어 단위 · Alt+R 정규식($1, \\n 바꾸기)\n" +
            "Ctrl+G 줄 이동(줄 또는 줄:열)\n" +
            "Ctrl+휠 · Ctrl+= / Ctrl+- 글꼴 크기 · Ctrl+0 기본 크기\n" +
            "Alt+드래그 사각형 선택 · 선택 없이 Ctrl+C / Ctrl+X 한 줄 복사·잘라내기\n" +
            "Tab / Shift+Tab 선택 줄 들여쓰기·내어쓰기\n" +
            "상태 표시줄: 인코딩·줄 끝(CRLF/LF)을 눌러 바꾸기";

        private readonly StackPanel _toolbar;
        private readonly ToggleButton _wrapToggle;
        private readonly ToggleButton _whitespaceToggle;
        private readonly TextEditorSettings _settings = TextEditorSettings.Shared;

        private string _filePath;
        private Encoding _encoding = new UTF8Encoding(false);   // 저장할 인코딩
        private Encoding _loadedEncoding;                       // 읽을 때 감지(또는 지정)한 인코딩
        private Encoding _forcedEncoding;                       // "이 인코딩으로 다시 열기"로 지정한 인코딩(외부 변경 반영 때도 유지). 자동 감지면 null
        private string _newLine = "\r\n";
        private bool _tooLarge;
        private bool _loading;
        private bool _readOnlyFile;
        private bool _lastModified;
        private int _loadVersion;
        private long _fileBytes;
        private byte[] _diskHash;                               // 마지막으로 읽거나 저장한 디스크 내용의 SHA-256
        private ThemedHighlightingColorizer _colorizer;
        private string _languageName;                           // 구문 강조 이름(없으면 일반 텍스트)
        private bool _hasLongLines;                             // LongLineElementGenerator.Limit보다 긴 줄이 있음(읽을 때 잼)
        private ThemeInfo _theme = ThemeManager.CurrentTheme;

        public UIElement View => this;

        public ViewerCapabilities Capabilities => _tooLarge || _readOnlyFile ? ViewerCapabilities.ReadOnly : ViewerCapabilities.Edit;

        public event EventHandler<string> TitleChanged;
        public event EventHandler<bool> ModifiedChanged;

        /// <summary>탭 툴바: 저장 · 줄 바꿈 · 공백 표시 · 단축키 도움말.</summary>
        public UIElement ToolbarContent => _toolbar;

        /// <summary>저장하지 않은 변경(글 변경 또는 저장 인코딩 변경)이 있는지.</summary>
        public bool IsModified => Editor.Document != null && (!Editor.Document.UndoStack.IsOriginalFile || _encodingChanged);

        private bool _encodingChanged;

        public TextViewer()
        {
            InitializeComponent();

            var save = new Button { Content = "저장", Padding = new Thickness(10, 1, 10, 1), Height = 24, ToolTip = "저장 (Ctrl+S)" };
            save.Click += (s, e) => Save();
            _wrapToggle = MakeToggle("줄 바꿈", "긴 줄을 창 너비에서 접어 보기 (모든 텍스트 탭에 기억)");
            _wrapToggle.Click += (s, e) => SetWordWrap(_wrapToggle.IsChecked == true, true);
            _whitespaceToggle = MakeToggle("공백 표시", "공백(·)·탭(→) 표시 (모든 텍스트 탭에 기억)");
            _whitespaceToggle.Click += (s, e) => SetShowWhitespace(_whitespaceToggle.IsChecked == true, true);
            var help = new TextBlock
            {
                Text = "단축키 ⓘ",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 4, 0),
                FontSize = 11,
                Cursor = Cursors.Help,
                ToolTip = ShortcutHelp,
            };
            help.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            ToolTipService.SetShowDuration(help, 60000);

            _toolbar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _toolbar.Children.Add(save);
            _toolbar.Children.Add(_wrapToggle);
            _toolbar.Children.Add(_whitespaceToggle);
            _toolbar.Children.Add(help);

            ConfigureEditor();
            InitializeFind();
            InitializeExternalWatch();

            PreviewKeyDown += OnPreviewKeyDown;
            Editor.PreviewMouseWheel += OnEditorPreviewMouseWheel;
            Editor.TextArea.Caret.PositionChanged += (s, e) => UpdateCaretText();
            Editor.TextArea.SelectionChanged += (s, e) => UpdateCaretText();
            Editor.DocumentChanged += (s, e) => UpdateCaretText();

            SetWordWrap(_settings.WordWrap, false);
            SetShowWhitespace(_settings.ShowWhitespace, false);
            SetFontSize(_settings.FontSize, false);
            UpdateStatus();
        }

        private static ToggleButton MakeToggle(string text, string tooltip)
        {
            var toggle = new ToggleButton
            {
                Content = text,
                Height = 24,
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(6, 0, 0, 0),
                ToolTip = tooltip,
            };
            toggle.SetResourceReference(StyleProperty, "CompactToggleButtonStyle");
            return toggle;
        }

        private void ConfigureEditor()
        {
            var options = Editor.Options;
            options.HighlightCurrentLine = true;
            options.ConvertTabsToSpaces = false;          // 파일의 들여쓰기 방식을 바꾸지 않는다
            options.IndentationSize = 4;
            options.AllowScrollBelowDocument = false;
            options.EnableRectangularSelection = true;    // Alt+드래그
            options.CutCopyWholeLine = true;              // 선택 없이 Ctrl+C/X → 한 줄
            // 주소 Ctrl+클릭 열기는 .NET에서 셸 실행 없이 시도해 실패하므로 끈다(글로만 보인다).
            options.EnableHyperlinks = false;
            options.EnableEmailHyperlinks = false;
            options.EnableTextDragDrop = true;

            Editor.ContextMenu = BuildContextMenu();
            Editor.TextArea.TextView.BackgroundRenderers.Add(_searchRenderer);
            Editor.TextArea.TextView.ElementGenerators.Add(new LongLineElementGenerator());

            // 복사할 때 AvalonEdit는 붙여넣기 서식용 HTML을 만들며 선택 전체를 다시 구문 강조한다 — 큰 선택이면 몇 분씩 멈춘다.
            // 큰 선택(또는 강조가 없을 때)은 HTML을 빼고 일반 텍스트만 넣는다.
            DataObject.AddSettingDataHandler(Editor.TextArea, (s, e) =>
            {
                if (e.Format == DataFormats.Html && (_colorizer == null || Editor.TextArea.Selection.Length > HtmlCopyLimitChars))
                    e.CancelCommand();
            });
        }

        /// <summary>복사에 HTML 서식을 넣는 최대 선택 길이(글자).</summary>
        private const int HtmlCopyLimitChars = 256 * 1024;

        private ContextMenu BuildContextMenu()
        {
            var menu = new ContextMenu();
            menu.Items.Add(new MenuItem { Header = "잘라내기", Command = ApplicationCommands.Cut, CommandTarget = Editor.TextArea, InputGestureText = "Ctrl+X" });
            menu.Items.Add(new MenuItem { Header = "복사", Command = ApplicationCommands.Copy, CommandTarget = Editor.TextArea, InputGestureText = "Ctrl+C" });
            menu.Items.Add(new MenuItem { Header = "붙여넣기", Command = ApplicationCommands.Paste, CommandTarget = Editor.TextArea, InputGestureText = "Ctrl+V" });
            menu.Items.Add(new MenuItem { Header = "모두 선택", Command = ApplicationCommands.SelectAll, CommandTarget = Editor.TextArea, InputGestureText = "Ctrl+A" });
            menu.Items.Add(new Separator());
            var find = new MenuItem { Header = "찾기", InputGestureText = "Ctrl+F" };
            find.Click += (s, e) => OpenFind(false);
            var replace = new MenuItem { Header = "바꾸기", InputGestureText = "Ctrl+H" };
            replace.Click += (s, e) => OpenFind(true);
            var gotoLine = new MenuItem { Header = "줄 이동", InputGestureText = "Ctrl+G" };
            gotoLine.Click += (s, e) => OpenGoto();
            menu.Items.Add(find);
            menu.Items.Add(replace);
            menu.Items.Add(gotoLine);
            menu.Opened += (s, e) => replace.IsEnabled = !Editor.IsReadOnly;
            return menu;
        }

        // ── 열기 ───────────────────────────────────────────────────────────

        public void Load(string filePath)
        {
            // 다시 읽기(↻)로 같은 파일을 읽을 때 편집 내용을 말없이 버리지 않는다.
            if (IsModified && string.Equals(filePath, _filePath, StringComparison.OrdinalIgnoreCase) &&
                !ConfirmDiscard("저장하지 않은 변경이 있습니다. 파일을 다시 읽으면 변경 내용이 사라집니다. 다시 읽을까요?"))
                return;

            StopFileWatcher();
            var samePath = string.Equals(filePath, _filePath, StringComparison.OrdinalIgnoreCase);
            _filePath = filePath;
            TitleChanged?.Invoke(this, Path.GetFileName(filePath));
            var _ = LoadAsync(null, samePath);
        }

        /// <summary>
        /// 파일을 백그라운드에서 읽어 편집기에 넣는다. <paramref name="forcedEncoding"/>이 있으면 그 인코딩으로 읽는다(다시 열기).
        /// <paramref name="keepPosition"/>이면 캐럿·스크롤 위치를 유지한다(다시 읽기·외부 변경 반영).
        /// </summary>
        private async Task LoadAsync(Encoding forcedEncoding, bool keepPosition)
        {
            var version = ++_loadVersion;
            var path = _filePath;
            long length;
            try
            {
                var info = new FileInfo(path);
                length = info.Length;
                _readOnlyFile = info.IsReadOnly;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                ShowNotice("파일 정보를 읽지 못했습니다.\n" + ex.Message);
                return;
            }

            _fileBytes = length;
            if (length > MaxFileBytes)
            {
                _tooLarge = true;
                Editor.Document = new TextDocument();
                Editor.IsReadOnly = true;
                ShowNotice(string.Format("파일이 너무 커서({0:N0} MB) 열지 않았습니다 (최대 {1:N0} MB).\n파일 패널 우클릭 > 뷰어 설정에서 다른 뷰어를 고르거나 외부 프로그램으로 열어 주세요.",
                    length / (1024 * 1024), MaxFileBytes / (1024 * 1024)));
                RaiseModifiedChanged();
                UpdateStatus();
                return;
            }

            _tooLarge = false;
            _loading = true;
            if (length > LoadingNoticeBytes)
                ShowNotice(string.Format("읽는 중… ({0:N1} MB)", length / (1024.0 * 1024)));
            UpdateStatus();

            var caret = keepPosition ? Editor.CaretOffset : 0;
            var vertical = keepPosition ? Editor.VerticalOffset : 0;
            var horizontal = keepPosition ? Editor.HorizontalOffset : 0;
            var uiThread = Dispatcher.Thread;

            LoadResult result;
            try
            {
                result = await Task.Run(() =>
                {
                    var bytes = TextFileEncoding.ReadAllBytesShared(path);
                    Encoding encoding;
                    string text;
                    if (forcedEncoding != null)
                    {
                        encoding = forcedEncoding;
                        text = TextFileEncoding.DecodeAs(bytes, forcedEncoding);
                    }
                    else
                        text = TextFileEncoding.Decode(bytes, out encoding);
                    var document = new TextDocument(text) { FileName = path };
                    document.SetOwnerThread(uiThread);   // 다 만든 문서를 UI 스레드에 넘긴다
                    return new LoadResult
                    {
                        Document = document,
                        Encoding = encoding,
                        NewLine = TextFileEncoding.DetectNewLine(text),
                        Hash = SHA256.HashData(bytes),
                        Forced = forcedEncoding,
                        MaxLineLength = TextFileEncoding.MaxLineLength(text),
                    };
                });
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is OutOfMemoryException)
            {
                if (version != _loadVersion || _disposed)
                    return;
                _loading = false;
                ShowNotice("파일을 읽지 못했습니다.\n" + ex.Message);
                UpdateStatus();
                return;
            }

            // 읽는 사이 다른 파일을 열었거나 탭이 닫혔으면 버린다.
            if (version != _loadVersion || _disposed)
                return;

            ApplyDocument(result, caret, vertical, horizontal);
        }

        private sealed class LoadResult
        {
            public TextDocument Document;
            public Encoding Encoding;
            public string NewLine;
            public byte[] Hash;
            public Encoding Forced;
            public int MaxLineLength;
        }

        private void ApplyDocument(LoadResult result, int caret, double vertical, double horizontal)
        {
            var old = Editor.Document;
            if (old != null)
                old.UndoStack.PropertyChanged -= OnUndoStackPropertyChanged;

            _loading = false;
            _forcedEncoding = result.Forced;
            _encoding = result.Encoding;
            _loadedEncoding = result.Encoding;
            _encodingChanged = false;
            _newLine = result.NewLine;
            _diskHash = result.Hash;
            _promptedHash = null;
            _hasLongLines = result.MaxLineLength > LongLineElementGenerator.Limit;

            Editor.Document = result.Document;
            result.Document.UndoStack.PropertyChanged += OnUndoStackPropertyChanged;
            Editor.IsReadOnly = _readOnlyFile;
            ApplyHighlighting();
            HideNotice();

            if (caret > 0 || vertical > 0)
            {
                Editor.CaretOffset = Math.Min(caret, result.Document.TextLength);
                Editor.ScrollToVerticalOffset(vertical);
                Editor.ScrollToHorizontalOffset(horizontal);
            }

            OnDocumentReplacedForFind();
            StartFileWatcher(_filePath);
            RaiseModifiedChanged();
            UpdateStatus();
            UpdateCaretText();
        }

        private void ApplyHighlighting()
        {
            if (_colorizer != null)
            {
                Editor.TextArea.TextView.LineTransformers.Remove(_colorizer);
                _colorizer = null;
            }
            _languageName = null;
            // 구문 강조는 줄 하나를 통째로 정규식에 넣는다 — 긴 줄(압축 JSON 등)이 있으면 몇 분씩 멈추므로 끈다.
            if (_filePath == null || _fileBytes > HighlightLimitBytes || _hasLongLines)
                return;
            var definition = HighlightingManager.Instance.GetDefinitionByExtension(Path.GetExtension(_filePath));
            if (definition == null)
                return;
            _languageName = definition.Name;
            // 정의는 앱 전체가 함께 쓰므로 고치지 않고, 어두운 테마에서는 그릴 때만 밝게 바꾼다.
            _colorizer = new ThemedHighlightingColorizer(definition) { Dark = !_theme.IsLight };
            Editor.TextArea.TextView.LineTransformers.Insert(0, _colorizer);
        }

        private void OnUndoStackPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(UndoStack.IsOriginalFile))
            {
                RaiseModifiedChanged();
                UpdateStatus();
            }
        }

        private void RaiseModifiedChanged()
        {
            var modified = IsModified;
            if (modified == _lastModified)
                return;
            _lastModified = modified;
            ModifiedChanged?.Invoke(this, modified);
        }

        // ── 저장 ───────────────────────────────────────────────────────────

        private void Save()
        {
            if (_filePath == null || _tooLarge || _loading || Editor.Document == null)
                return;
            if (_readOnlyFile)
            {
                ShowMessage("읽기 전용 파일이라 저장할 수 없습니다.\n파일 속성에서 읽기 전용을 해제한 뒤 다시 읽어 주세요.", MessageBoxImage.Information);
                return;
            }

            var text = Editor.Document.Text;
            var encoding = _encoding;

            // CP949 등은 없는 글자를 말없이 '?'로 바꾼다. 저장 전에 알리고 UTF-8로 바꿀 수 있게 한다.
            var lost = TextFileEncoding.FindUnencodable(text, encoding);
            if (lost != null)
            {
                var answer = AskMessage(string.Format(
                    "{0}에 없는 글자가 있습니다: {1}\n\n" +
                    "예: UTF-8로 바꿔 저장(글자 유지)\n아니요: {0}로 저장(이 글자들은 '?'로 바뀜)\n취소: 저장하지 않음",
                    TextFileEncoding.DisplayName(encoding), lost),
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.Yes);
                if (answer == MessageBoxResult.Cancel)
                    return;
                if (answer == MessageBoxResult.Yes)
                    encoding = new UTF8Encoding(false);
            }

            var preamble = encoding.GetPreamble();
            var body = encoding.GetBytes(text);
            var bytes = new byte[preamble.Length + body.Length];
            Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
            Buffer.BlockCopy(body, 0, bytes, preamble.Length, body.Length);

            try
            {
                // 임시 파일에 쓴 뒤 교체한다(쓰기 도중 실패해도 원본이 남음).
                SettingsFile.Write(_filePath, temporaryPath => File.WriteAllBytes(temporaryPath, bytes));
            }
            catch (Exception ex)
            {
                ShowMessage(ex.Message, MessageBoxImage.Error, "저장 오류");
                return;
            }

            _diskHash = SHA256.HashData(bytes);   // 이 저장으로 생긴 파일 감시 이벤트는 무시한다
            _promptedHash = null;
            _fileBytes = bytes.Length;
            _encoding = encoding;
            _loadedEncoding = encoding;
            _encodingChanged = false;
            _externalNote = null;
            Editor.Document.UndoStack.MarkAsOriginalFile();
            RaiseModifiedChanged();
            UpdateStatus();
        }

        // ── 보기 설정 ─────────────────────────────────────────────────────

        private void SetWordWrap(bool wrap, bool remember)
        {
            Editor.WordWrap = wrap;
            _wrapToggle.IsChecked = wrap;
            if (remember && _settings.WordWrap != wrap)
            {
                _settings.WordWrap = wrap;
                _settings.TrySave();
            }
        }

        private void SetShowWhitespace(bool show, bool remember)
        {
            Editor.Options.ShowSpaces = show;
            Editor.Options.ShowTabs = show;
            _whitespaceToggle.IsChecked = show;
            if (remember && _settings.ShowWhitespace != show)
            {
                _settings.ShowWhitespace = show;
                _settings.TrySave();
            }
        }

        private void SetFontSize(double size, bool remember)
        {
            size = TextEditorSettings.ClampFontSize(size);
            Editor.FontSize = size;
            ZoomButton.Content = string.Format("{0:0}%", size / TextEditorSettings.DefaultFontSize * 100);
            if (remember && Math.Abs(_settings.FontSize - size) > 0.01)
            {
                _settings.FontSize = size;
                _settings.TrySave();
            }
        }

        private void Zoom(int direction)
        {
            var size = Editor.FontSize;
            var step = size < 12 ? 1 : size < 24 ? 2 : 4;
            SetFontSize(direction == 0 ? TextEditorSettings.DefaultFontSize : size + direction * step, true);
        }

        private void ZoomButton_Click(object sender, RoutedEventArgs e)
        {
            Zoom(0);
        }

        private void OnEditorPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
                return;
            Zoom(e.Delta > 0 ? 1 : -1);
            e.Handled = true;
        }

        // ── 인코딩·줄 끝 ──────────────────────────────────────────────────

        private void EncodingButton_Click(object sender, RoutedEventArgs e)
        {
            if (_filePath == null || _tooLarge || _loading)
                return;
            var menu = new ContextMenu { PlacementTarget = EncodingButton, Placement = PlacementMode.Top };

            menu.Items.Add(new MenuItem { Header = "이 인코딩으로 다시 열기", IsEnabled = false });
            foreach (var choice in TextFileEncoding.Choices)
            {
                var encoding = choice.Value;
                var item = new MenuItem { Header = "    " + choice.Key, IsChecked = TextFileEncoding.SameEncoding(encoding, _loadedEncoding) };
                item.Click += (s, args) => ReopenWithEncoding(encoding);
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = "저장할 인코딩", IsEnabled = false });
            foreach (var choice in TextFileEncoding.Choices)
            {
                var encoding = choice.Value;
                var item = new MenuItem { Header = "    " + choice.Key, IsChecked = TextFileEncoding.SameEncoding(encoding, _encoding), IsEnabled = !Editor.IsReadOnly };
                item.Click += (s, args) => SetSaveEncoding(encoding);
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }

        private void ReopenWithEncoding(Encoding encoding)
        {
            if (IsModified && !ConfirmDiscard("저장하지 않은 변경이 있습니다. 다른 인코딩으로 다시 열면 변경 내용이 사라집니다. 다시 열까요?"))
                return;
            StopFileWatcher();
            var _ = LoadAsync(encoding, true);
        }

        private void SetSaveEncoding(Encoding encoding)
        {
            _encoding = encoding;
            _encodingChanged = !TextFileEncoding.SameEncoding(encoding, _loadedEncoding);
            RaiseModifiedChanged();
            UpdateStatus();
        }

        private void NewLineButton_Click(object sender, RoutedEventArgs e)
        {
            if (_filePath == null || _tooLarge || _loading)
                return;
            var menu = new ContextMenu { PlacementTarget = NewLineButton, Placement = PlacementMode.Top };
            AddNewLineItem(menu, "CRLF (Windows)", "\r\n");
            AddNewLineItem(menu, "LF (Unix·macOS)", "\n");
            menu.IsOpen = true;
        }

        private void AddNewLineItem(ContextMenu menu, string header, string newLine)
        {
            var item = new MenuItem { Header = header, IsChecked = _newLine == newLine, IsEnabled = !Editor.IsReadOnly };
            item.Click += (s, e) => ConvertNewLines(newLine);
            menu.Items.Add(item);
        }

        /// <summary>모든 줄 끝을 바꾼다. 한 번의 편집이라 Ctrl+Z 한 번으로 되돌린다. 새로 치는 줄도 문서의 줄 끝을 따른다.</summary>
        private void ConvertNewLines(string newLine)
        {
            var document = Editor.Document;
            if (document == null)
                return;
            var text = document.Text;
            var converted = TextFileEncoding.NormalizeNewLines(text, newLine);
            _newLine = newLine;
            if (!string.Equals(text, converted, StringComparison.Ordinal))
            {
                var caretLine = Editor.TextArea.Caret.Line;
                var caretColumn = Editor.TextArea.Caret.Column;
                document.Replace(0, document.TextLength, converted);
                Editor.TextArea.Caret.Position = new ICSharpCode.AvalonEdit.TextViewPosition(caretLine, caretColumn);
            }
            UpdateStatus();
        }

        // ── 테마 ───────────────────────────────────────────────────────────

        public void ApplyTheme(ThemeInfo theme)
        {
            _theme = theme;
            var area = Editor.TextArea;
            area.SelectionBrush = new SolidColorBrush(WithAlpha(theme.GetColor("SelectionColor"), 0xB0));
            area.SelectionForeground = null;   // 선택해도 구문 색 유지
            area.SelectionBorder = null;
            area.SelectionCornerRadius = 1;
            area.Caret.CaretBrush = new SolidColorBrush(theme.GetColor("PrimaryTextColor"));
            area.TextView.CurrentLineBackground = new SolidColorBrush(WithAlpha(theme.GetColor("ControlHoverColor"), 0x60));
            area.TextView.CurrentLineBorder = null;
            area.TextView.NonPrintableCharacterBrush = new SolidColorBrush(WithAlpha(theme.GetColor("DisabledTextColor"), 0xC0));
            var accent = theme.GetColor("AccentColor");
            _searchRenderer.MatchBrush = new SolidColorBrush(WithAlpha(accent, 0x45));
            _searchRenderer.CurrentBrush = new SolidColorBrush(WithAlpha(accent, 0x99));
            if (_colorizer != null)
                _colorizer.Dark = !theme.IsLight;
            area.TextView.Redraw();
        }

        private static Color WithAlpha(Color c, byte alpha)
        {
            return Color.FromArgb(alpha, c.R, c.G, c.B);
        }

        // ── 상태 표시 ─────────────────────────────────────────────────────

        private void UpdateStatus()
        {
            EncodingButton.Content = _tooLarge ? string.Empty : TextFileEncoding.DisplayName(_encoding)
                + (_encodingChanged ? " (저장 시 변경)" : string.Empty);
            NewLineButton.Content = _tooLarge ? string.Empty : _newLine == "\n" ? "LF" : "CRLF";
            EncodingButton.Visibility = NewLineButton.Visibility = _tooLarge ? Visibility.Collapsed : Visibility.Visible;

            LanguageText.Text = _tooLarge ? string.Empty
                : _languageName ?? (_fileBytes > HighlightLimitBytes ? "일반 텍스트 (큰 파일: 구문 강조 끔)"
                    : _hasLongLines ? "일반 텍스트 (긴 줄: 구문 강조 끔)" : "일반 텍스트");
            LanguageText.ToolTip = _hasLongLines
                ? string.Format("{0:N0}자가 넘는 줄은 앞 {0:N0}자만 화면에 그립니다(저장·찾기·복사는 줄 전체).", LongLineElementGenerator.Limit)
                : null;

            string state;
            if (_tooLarge)
                state = "열지 않음";
            else if (_loading)
                state = "읽는 중…";
            else if (_readOnlyFile)
                state = "읽기 전용";
            else
                state = IsModified ? "● 수정됨" : "저장됨";
            if (_externalNote != null)
                state += " · " + _externalNote;
            StateText.Text = state;
        }

        private void UpdateCaretText()
        {
            var document = Editor.Document;
            if (document == null || _tooLarge)
            {
                CaretText.Text = string.Empty;
                return;
            }
            var caret = Editor.TextArea.Caret;
            var text = string.Format("줄 {0:N0}, 열 {1:N0}", caret.Line, caret.Column);
            var selection = Editor.TextArea.Selection;
            if (!selection.IsEmpty)
            {
                var segment = selection.SurroundingSegment;
                var lines = document.GetLineByOffset(segment.EndOffset).LineNumber - document.GetLineByOffset(segment.Offset).LineNumber + 1;
                text += lines > 1
                    ? string.Format(" (선택 {0:N0}자, {1:N0}줄)", selection.Length, lines)
                    : string.Format(" (선택 {0:N0}자)", selection.Length);
            }
            CaretText.Text = text + string.Format("  ·  {0:N0}줄", document.LineCount);
        }

        private void ShowNotice(string text)
        {
            NoticeText.Text = text;
            NoticeText.Visibility = Visibility.Visible;
            Editor.Visibility = Visibility.Hidden;
        }

        private void HideNotice()
        {
            NoticeText.Visibility = Visibility.Collapsed;
            Editor.Visibility = Visibility.Visible;
        }

        // ── 키 ─────────────────────────────────────────────────────────────

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var mods = Keyboard.Modifiers;

            if (mods == ModifierKeys.Control)
            {
                switch (key)
                {
                    case Key.S: Save(); e.Handled = true; return;
                    case Key.F: OpenFind(false); e.Handled = true; return;
                    case Key.H: OpenFind(true); e.Handled = true; return;
                    case Key.G: OpenGoto(); e.Handled = true; return;
                    case Key.OemPlus:
                    case Key.Add: Zoom(1); e.Handled = true; return;
                    case Key.OemMinus:
                    case Key.Subtract: Zoom(-1); e.Handled = true; return;
                    case Key.D0:
                    case Key.NumPad0: Zoom(0); e.Handled = true; return;
                }
            }
            if (key == Key.F3 && (mods == ModifierKeys.None || mods == ModifierKeys.Shift))
            {
                if (string.IsNullOrEmpty(FindBox.Text))
                    OpenFind(false);
                else
                    FindStep(mods == ModifierKeys.Shift ? -1 : 1);
                e.Handled = true;
                return;
            }
            if (key == Key.Escape && mods == ModifierKeys.None && (FindBar.Visibility == Visibility.Visible || GotoBar.Visibility == Visibility.Visible))
            {
                CloseBars();
                e.Handled = true;
            }
        }

        public bool HandleShortcut(KeyEventArgs e, KeyBindingService kb)
        {
            if (_filePath == null || _tooLarge)
                return false;
            // 전역 "내용 검색"(기본 Ctrl+F)은 메인 창이 먼저 받으므로 여기서 가로채 문서 안 찾기를 연다(다른 뷰어와 같은 방식).
            if (kb.Matches(e, "ShowSearch"))
            {
                OpenFind(false);
                return true;
            }
            // 패널 전환(기본 Ctrl+Shift+←/→)은 편집 중에는 단어 단위 선택으로 쓴다(편집기·찾기 입력란 모두).
            var left = kb.Matches(e, "SwitchPaneLeft");
            if ((left || kb.Matches(e, "SwitchPaneRight")) && IsKeyboardFocusWithin)
            {
                var target = Keyboard.FocusedElement as IInputElement;
                var command = left ? EditingCommands.SelectLeftByWord : EditingCommands.SelectRightByWord;
                if (target != null && command.CanExecute(null, target))
                {
                    command.Execute(null, target);
                    return true;
                }
            }
            return false;
        }

        // ── 확인창 ─────────────────────────────────────────────────────────

        private bool ConfirmDiscard(string message)
        {
            return AskMessage(message, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
        }

        private MessageBoxResult AskMessage(string message, MessageBoxButton buttons, MessageBoxImage icon, MessageBoxResult defaultResult)
        {
            var title = _filePath != null ? Path.GetFileName(_filePath) : "텍스트 편집기";
            // owner 없이 띄우면 앱이 백그라운드일 때 확인창이 다른 창 뒤에 숨는다.
            var owner = Window.GetWindow(this);
            return owner != null
                ? MessageBox.Show(owner, message, title, buttons, icon, defaultResult)
                : MessageBox.Show(message, title, buttons, icon, defaultResult);
        }

        private void ShowMessage(string message, MessageBoxImage icon, string title = null)
        {
            title = title ?? (_filePath != null ? Path.GetFileName(_filePath) : "텍스트 편집기");
            var owner = Window.GetWindow(this);
            if (owner != null)
                MessageBox.Show(owner, message, title, MessageBoxButton.OK, icon);
            else
                MessageBox.Show(message, title, MessageBoxButton.OK, icon);
        }

        public void Export(ExportFormat format, string destPath)
        {
            // 일반 텍스트는 내보내기가 없다.
        }

        public void Dispose()
        {
            _disposed = true;
            _loadVersion++;   // 읽는 중인 결과를 버린다
            StopFileWatcher();
            _searchTimer.Stop();
            if (Editor.Document != null)
                Editor.Document.UndoStack.PropertyChanged -= OnUndoStackPropertyChanged;
        }
    }
}

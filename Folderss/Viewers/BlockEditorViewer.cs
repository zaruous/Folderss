using Folderss.Controls;
using Folderss.Services;
using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Folderss.Viewers
{
    /// <summary>
    /// Markdown 파일을 Notion식 블록 편집기(<see cref="BlockEditor"/>)로 편집하는 뷰어(<c>builtin:blocks</c>).
    /// 읽은 인코딩·줄바꿈(CRLF/LF)으로 저장하고, 저장할 때 Markdown 형식은 <see cref="BlockMarkdown"/> 규칙으로 정리된다.
    /// </summary>
    public sealed partial class BlockEditorViewer : UserControl, IFileViewer, IViewerShortcutHandler, IFileOpenRequester, IViewerActivationAware, IDisposable
    {
        /// <summary>
        /// 편집기는 화면에 보이는 블록만 입력란을 만들므로(가상화) 블록 수 제한은 없다. 이 크기는 해석·메모리 상한일 뿐이다
        /// (블록 모델·실행 취소 기록이 파일 크기에 비례).
        /// </summary>
        /// <summary>블록 편집기로 여는 최대 파일 크기. 넘으면 .md 탭은 미리보기(Markdown 뷰어)로 연다(ViewerHost.OpenFile).</summary>
        public const long MaxFileBytes = 32 * 1024 * 1024;

        private const string ShortcutHelp =
            "Enter 블록 나누기 · Shift+Enter 줄바꿈 · Tab/Shift+Tab 들여쓰기\n" +
            "/ 블록 메뉴(/표, /이미지, /수식, /목차, /파일, /북마크, /토글제목1~3 포함) · # ## ### - 1. [] > ``` $$ [TOC] --- 입력 시 자동 변환\n" +
            "이미지: Ctrl+V 클립보드 이미지 붙여넣기(images/ 자동 저장) · 탐색기/폴더 패널에서 파일 드래그 앤 드롭 · 우측 끝 핸들 끌기로 폭 조절\n" +
            "표: Tab 다음 셀(끝에서 행 추가) · Enter 아래에 행 추가 · 빈 행 Backspace 행 삭제 · 엑셀 붙여넣기\n" +
            "Esc 블록 선택(Shift+↑↓ 확장, Del 삭제, Ctrl+C Markdown 복사)\n" +
            "Ctrl+Shift+↑↓ 블록 이동 · Ctrl+D 복제 · Ctrl+Enter 체크·접기\n" +
            "Ctrl+Alt+0~8 전환(텍스트·제목1~3·할 일·글머리·번호·토글·코드)\n" +
            "Ctrl+B 굵게 · Ctrl+I 기울임 · Ctrl+U 밑줄 · Ctrl+E 코드 · Ctrl+Shift+S 취소선 · Ctrl+K 링크 주소 · 글자 선택 시 서식 도구막대(글자 색·형광펜 A▾)\n" +
            "**글** *글* ~~글~~ `코드` $수식$ [글](주소) 입력 시 서식으로 변환(Ctrl+Z로 표식 글자로)\n" +
            "Ctrl+F 찾기 · Ctrl+H 바꾸기 · F3/Shift+F3 다음·이전 · Ctrl+클릭 링크 열기(#제목·파일.md#제목 이동)\n" +
            "Ctrl+Z/Ctrl+Y 실행 취소·다시 실행 · Ctrl+S 저장";

        private readonly BlockEditor _editor = new BlockEditor();
        private readonly TextBlock _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0), FontSize = 11 };
        private readonly TextBlock _notice = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(40), FontSize = 13, Visibility = Visibility.Collapsed };
        private string _filePath;
        private Encoding _encoding = new UTF8Encoding(false);
        private bool _crlf;
        private bool _tooLarge;
        private string _sidecarNote;
        private bool _sidecarUnreadable;

        public UIElement View => this;

        public ViewerCapabilities Capabilities => _tooLarge ? ViewerCapabilities.ReadOnly : ViewerCapabilities.Edit;

        public event EventHandler<string> TitleChanged;
        public event EventHandler<bool> ModifiedChanged;

        /// <summary>페이지 링크 블록을 눌렀다(열 문서 경로). 본체가 새 탭으로 연다.</summary>
        public event EventHandler<string> FileOpenRequested;

        public BlockEditorViewer()
        {
            var save = new Button { Content = "저장", Padding = new Thickness(12, 3, 12, 3), ToolTip = "Ctrl+S" };
            save.Click += (s, e) => Save();

            var fullWidth = new CheckBox { Content = "전체 너비", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            fullWidth.SetResourceReference(ForegroundProperty, "PrimaryText");
            fullWidth.Click += (s, e) => _editor.FullWidth = fullWidth.IsChecked == true;

            var help = new TextBlock
            {
                Text = "단축키 ⓘ",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
                FontSize = 11,
                Cursor = Cursors.Help,
                ToolTip = ShortcutHelp,
            };
            help.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            ToolTipService.SetShowDuration(help, 60000);

            _status.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            _notice.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");

            var toolbar = new DockPanel { LastChildFill = false, Margin = new Thickness(6, 4, 6, 4) };
            toolbar.Children.Add(save);
            toolbar.Children.Add(fullWidth);
            toolbar.Children.Add(help);
            DockPanel.SetDock(_status, Dock.Right);
            toolbar.Children.Add(_status);

            var toolbarBorder = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = toolbar };
            toolbarBorder.SetResourceReference(Border.BackgroundProperty, "SurfaceBackground");
            toolbarBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

            var body = new Grid();
            body.Children.Add(_editor);
            body.Children.Add(_notice);

            var root = new DockPanel();
            DockPanel.SetDock(toolbarBorder, Dock.Top);
            root.Children.Add(toolbarBorder);
            root.Children.Add(body);
            Content = root;

            _editor.ModifiedChanged += (s, e) =>
            {
                UpdateStatus();
                ModifiedChanged?.Invoke(this, _editor.IsModified);
            };
            _editor.SaveRequested += (s, e) => Save();
            _editor.PageOpenRequested += (s, path) => FileOpenRequested?.Invoke(this, path);
            _editor.LinkOpenRequested += (s, link) => OpenLink(link);
            _fileReloadTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _fileReloadTimer.Tick += OnFileReloadTimerTick;

            Loaded += (s, e) => NetworkSettingsService.Changed += OnNetworkSettingsChanged;
            Unloaded += (s, e) => NetworkSettingsService.Changed -= OnNetworkSettingsChanged;
        }

        private void OnNetworkSettingsChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() => _editor.RefreshRemoteImages()));
        }

        public void Load(string filePath)
        {
            // 다시 읽기(↻)로 같은 파일을 읽을 때 편집 내용을 말없이 버리지 않는다.
            if (_editor.IsModified && string.Equals(filePath, _filePath, StringComparison.OrdinalIgnoreCase) &&
                MessageBox.Show("저장하지 않은 변경이 있습니다. 파일을 다시 읽으면 변경 내용이 사라집니다. 다시 읽을까요?",
                    "블록 편집기", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;

            StopFileWatcher();
            _reloadNote = null;
            _filePath = filePath;
            TitleChanged?.Invoke(this, Path.GetFileName(filePath));

            var length = new FileInfo(filePath).Length;
            if (length > MaxFileBytes)
            {
                ShowTooLarge(string.Format("파일이 커서({0:N0} KB) 블록 편집기로 열지 않았습니다 (최대 {1:N0} KB).", length / 1024, MaxFileBytes / 1024));
                return;
            }

            _encoding = MarkdownViewer.DetectEncoding(filePath);
            var content = File.ReadAllText(filePath, _encoding);

            // Markdown 밖 속성(블록 색 등)은 문서 옆 사이드카(문서.md.meta.json)에 있다(#78).
            var sidecarPath = BlockSidecar.PathFor(filePath);
            var sidecar = File.Exists(sidecarPath) ? File.ReadAllText(sidecarPath, Encoding.UTF8) : null;
            ApplyContent(content, sidecar, false);
            StartFileWatcher(filePath);
            ConsumePendingAnchorLater();
        }

        /// <summary>읽은 Markdown·사이드카를 편집기에 넣는다. <paramref name="keepScroll"/>면 보던 블록(앵커)으로 돌아간다(외부 변경 반영).</summary>
        private void ApplyContent(string content, string sidecar, bool keepScroll)
        {
            _tooLarge = false;
            _crlf = content.Contains("\r\n");
            _notice.Visibility = Visibility.Collapsed;
            _editor.Visibility = Visibility.Visible;
            _editor.DocumentPath = _filePath;
            _editor.BaseDirectory = Path.GetDirectoryName(_filePath);

            Models.EditorBlock anchor = null;
            double delta = 0;
            var anchorIndex = keepScroll ? _editor.CaptureScrollAnchor(out anchor, out delta) : -1;
            _editor.SetMarkdown(content, sidecar);
            if (keepScroll)
                _editor.RestoreScrollAnchor(anchor, anchorIndex, delta);
            // 자기 저장·이미 반영한 내용은 외부 변경으로 보지 않는다(BlockEditorViewer.External.cs).
            _diskText = content;
            _diskSidecar = sidecar;
            _promptedText = _promptedSidecar = null;

            _sidecarUnreadable = _editor.UnmatchedAttributes == BlockSidecar.Unreadable;
            _sidecarNote = _sidecarUnreadable ? "속성 파일을 읽지 못함(저장하면 .bak로 보관)"
                : _editor.UnmatchedAttributes > 0 ? string.Format("속성 {0}개를 붙이지 못함(다른 곳에서 본문이 바뀜)", _editor.UnmatchedAttributes)
                : null;
            ModifiedChanged?.Invoke(this, false);
            UpdateStatus();
        }

        private void ShowTooLarge(string reason)
        {
            _tooLarge = true;
            _editor.SetMarkdown(string.Empty);
            _editor.Visibility = Visibility.Collapsed;
            _notice.Text = reason + "\n설정 > 뷰어에서 이 확장자를 Markdown 또는 Monaco 뷰어로 바꿔 열어 주세요.";
            _notice.Visibility = Visibility.Visible;
            ModifiedChanged?.Invoke(this, false);
            UpdateStatus();
        }

        private void Save()
        {
            if (_filePath == null || _tooLarge)
                return;
            // 바뀐 블록만 다시 쓰고 나머지는 원문 줄 그대로(줄 끝 CRLF/LF·끝 줄바꿈 유무도 원문을 따름 — 최소 변경 저장, #81).
            var text = _editor.GetMarkdown();
            var sidecar = _editor.GetSidecar(text, Path.GetFileName(_filePath));
            try
            {
                // 임시 파일에 쓴 뒤 교체한다(쓰기 도중 실패해도 원본이 남음).
                SettingsFile.WriteAllText(_filePath, text, _encoding);
                _diskText = text;   // 이 저장으로 생긴 파일 감시 이벤트는 무시한다
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "저장 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var sidecarPath = BlockSidecar.PathFor(_filePath);
            try
            {
                // 읽지 못한 속성 파일은 덮어쓰기 전에 한 번 보관한다(손으로 고치다 깨진 경우 등).
                if (_sidecarUnreadable && File.Exists(sidecarPath))
                    File.Copy(sidecarPath, sidecarPath + ".bak", true);
                _sidecarUnreadable = false;
                if (sidecar != null)
                    SettingsFile.WriteAllText(sidecarPath, sidecar, new UTF8Encoding(false));
                else if (File.Exists(sidecarPath))
                    File.Delete(sidecarPath);   // 속성이 모두 지워졌으면 속성 파일도 남기지 않는다
                _diskSidecar = sidecar;
            }
            catch (Exception ex)
            {
                // Markdown은 저장됐다. 속성은 다음 저장 때 다시 쓰도록 미저장 상태로 둔다.
                MessageBox.Show("Markdown은 저장했지만 속성 파일(" + Path.GetFileName(sidecarPath) + ")을 저장하지 못했습니다.\n" + ex.Message,
                    "저장 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            _sidecarNote = null;
            _reloadNote = null;
            _editor.MarkSaved();
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            if (_tooLarge)
            {
                _status.Text = "읽지 않음";
                return;
            }
            _status.Text = string.Format("{0}{1} · {2} · {3}{4}{5}",
                _encoding.WebName.ToUpperInvariant(), _encoding.GetPreamble().Length > 0 ? " BOM" : string.Empty,
                _crlf ? "CRLF" : "LF", _editor.IsModified ? "수정됨" : "저장됨",
                _sidecarNote != null ? " · " + _sidecarNote : string.Empty,
                _reloadNote != null ? " · " + _reloadNote : string.Empty);
        }

        public void ApplyTheme(ThemeInfo theme)
        {
            // 대부분의 색은 테마 리소스(DynamicResource)를 따른다. 블록 색은 테마 밝기에 맞춰 고르므로 다시 칠한다.
            _editor.RefreshTheme();
        }

        public bool HandleShortcut(KeyEventArgs e, KeyBindingService kb)
        {
            // 편집기 단축키는 BlockEditor가 직접 처리한다. 단 전역 "내용 검색"(기본 Ctrl+F)은 메인 창이 먼저 받으므로
            // 여기서 가로채 문서 내 찾기를 연다(Markdown·Monaco 뷰어와 같은 방식). 나머지 메인 창 단축키는 그대로 둔다.
            if (_tooLarge || _filePath == null || !kb.Matches(e, "ShowSearch"))
                return false;
            _editor.OpenFind(false);
            return true;
        }

        public void Export(ExportFormat format, string destPath)
        {
            // 내보내기는 Markdown 뷰어가 맡는다.
        }
    }
}

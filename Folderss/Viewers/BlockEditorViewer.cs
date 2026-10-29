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
    public sealed class BlockEditorViewer : UserControl, IFileViewer, IViewerShortcutHandler
    {
        /// <summary>
        /// 편집기는 화면에 보이는 블록만 입력란을 만들므로(가상화) 블록 수 제한은 없다. 이 크기는 해석·메모리 상한일 뿐이다
        /// (블록 모델·실행 취소 기록이 파일 크기에 비례).
        /// </summary>
        private const long MaxFileBytes = 32 * 1024 * 1024;

        private const string ShortcutHelp =
            "Enter 블록 나누기 · Shift+Enter 줄바꿈 · Tab/Shift+Tab 들여쓰기\n" +
            "/ 블록 메뉴(/표, /이미지, /수식, /목차, /파일, /북마크, /토글제목1~3 포함) · # ## ### - 1. [] > ``` $$ [TOC] --- 입력 시 자동 변환\n" +
            "이미지: Ctrl+V 클립보드 이미지 붙여넣기(images/ 자동 저장) · 탐색기/폴더 패널에서 파일 드래그 앤 드롭 · 우측 끝 핸들 끌기로 폭 조절\n" +
            "표: Tab 다음 셀(끝에서 행 추가) · Enter 아래에 행 추가 · 빈 행 Backspace 행 삭제 · 엑셀 붙여넣기\n" +
            "Esc 블록 선택(Shift+↑↓ 확장, Del 삭제, Ctrl+C Markdown 복사)\n" +
            "Ctrl+Shift+↑↓ 블록 이동 · Ctrl+D 복제 · Ctrl+Enter 체크·접기\n" +
            "Ctrl+Alt+0~8 전환(텍스트·제목1~3·할 일·글머리·번호·토글·코드)\n" +
            "Ctrl+B 굵게 · Ctrl+I 기울임 · Ctrl+E 코드 · Ctrl+Shift+S 취소선 · Ctrl+K 링크\n" +
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

            _tooLarge = false;
            _crlf = content.Contains("\r\n");
            _notice.Visibility = Visibility.Collapsed;
            _editor.Visibility = Visibility.Visible;
            _editor.DocumentPath = filePath;
            _editor.BaseDirectory = Path.GetDirectoryName(filePath);

            // Markdown 밖 속성(블록 색 등)은 문서 옆 사이드카(문서.md.meta.json)에 있다(#78).
            var sidecarPath = BlockSidecar.PathFor(filePath);
            var sidecar = File.Exists(sidecarPath) ? File.ReadAllText(sidecarPath, Encoding.UTF8) : null;
            _editor.SetMarkdown(content, sidecar);
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
            var markdown = _editor.GetMarkdown();
            var sidecar = _editor.GetSidecar(markdown, Path.GetFileName(_filePath));
            var text = _crlf ? markdown.Replace("\n", "\r\n") : markdown;
            try
            {
                // 임시 파일에 쓴 뒤 교체한다(쓰기 도중 실패해도 원본이 남음).
                SettingsFile.WriteAllText(_filePath, text, _encoding);
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
            }
            catch (Exception ex)
            {
                // Markdown은 저장됐다. 속성은 다음 저장 때 다시 쓰도록 미저장 상태로 둔다.
                MessageBox.Show("Markdown은 저장했지만 속성 파일(" + Path.GetFileName(sidecarPath) + ")을 저장하지 못했습니다.\n" + ex.Message,
                    "저장 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            _sidecarNote = null;
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
            _status.Text = string.Format("{0}{1} · {2} · {3}{4}",
                _encoding.WebName.ToUpperInvariant(), _encoding.GetPreamble().Length > 0 ? " BOM" : string.Empty,
                _crlf ? "CRLF" : "LF", _editor.IsModified ? "수정됨" : "저장됨",
                _sidecarNote != null ? " · " + _sidecarNote : string.Empty);
        }

        public void ApplyTheme(ThemeInfo theme)
        {
            // 대부분의 색은 테마 리소스(DynamicResource)를 따른다. 블록 색은 테마 밝기에 맞춰 고르므로 다시 칠한다.
            _editor.RefreshTheme();
        }

        public bool HandleShortcut(KeyEventArgs e, KeyBindingService kb)
        {
            // 편집기 단축키는 BlockEditor가 직접 처리한다. 메인 창 단축키(내용 검색 등)는 그대로 둔다.
            return false;
        }

        public void Export(ExportFormat format, string destPath)
        {
            // 내보내기는 Markdown 뷰어가 맡는다.
        }
    }
}

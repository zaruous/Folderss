using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Folderss.Models;
using Folderss.Services;
using Microsoft.Win32;

namespace Folderss.Controls
{
    public sealed partial class BlockEditor
    {
        private static readonly HashSet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico", ".tiff", ".tif"
        };

        /// <summary>현재 열려 있는 문서 파일의 전체 경로(붙여넣은 이미지 파일 이름 생성에 쓴다).</summary>
        public string DocumentPath { get; set; }

        internal static bool IsImageFile(string path)
        {
            return !string.IsNullOrEmpty(path) && ImageExtensions.Contains(Path.GetExtension(path));
        }

        internal static string EncodeMarkdownPath(string relativePath)
        {
            var normalized = (relativePath ?? string.Empty).Replace('\\', '/');
            var parts = normalized.Split('/');
            for (var i = 0; i < parts.Length; i++)
                parts[i] = Uri.EscapeDataString(parts[i]);
            return string.Join("/", parts);
        }

        /// <summary>클립보드에 이미지가 있으면 문서 옆 <c>images/</c> 폴더에 저장하고 이미지 블록으로 넣는다.</summary>
        internal bool TryPasteClipboardImage(int blockIndex)
        {
            if (!Clipboard.ContainsImage())
                return false;

            if (string.IsNullOrEmpty(BaseDirectory) || !Directory.Exists(BaseDirectory))
            {
                MessageBox.Show(
                    Window.GetWindow(this),
                    "먼저 문서를 파일로 저장한 뒤에 이미지를 붙여넣을 수 있습니다.",
                    "이미지 붙여넣기",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return true;
            }

            try
            {
                var bitmap = Clipboard.GetImage();
                if (bitmap == null)
                    return false;

                var relativePath = SaveBitmapToImagesFolder(bitmap, DocumentPath, BaseDirectory);
                InsertOrReplaceMediaBlock(blockIndex, new EditorBlock(BlockKind.Image, string.Empty)
                {
                    Source = relativePath,
                });
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Window.GetWindow(this),
                    "이미지를 저장할 수 없습니다.\n" + ex.Message,
                    "이미지 붙여넣기",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return true;
            }
        }

        internal static string SaveBitmapToImagesFolder(BitmapSource bitmap, string documentPath, string baseDirectory)
        {
            var imagesDir = Path.Combine(baseDirectory, "images");
            Directory.CreateDirectory(imagesDir);

            var docName = string.IsNullOrEmpty(documentPath)
                ? "image"
                : Path.GetFileNameWithoutExtension(documentPath);
            foreach (var invalid in Path.GetInvalidFileNameChars())
                docName = docName.Replace(invalid, '_');
            if (string.IsNullOrWhiteSpace(docName))
                docName = "image";

            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var fileName = docName + "-" + timestamp + ".png";
            var fullPath = Path.Combine(imagesDir, fileName);
            var suffix = 2;
            while (File.Exists(fullPath))
            {
                fileName = docName + "-" + timestamp + "-" + suffix + ".png";
                fullPath = Path.Combine(imagesDir, fileName);
                suffix++;
            }

            using (var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(stream);
            }

            return EncodeMarkdownPath("images/" + fileName);
        }

        private void InsertOrReplaceMediaBlock(int index, EditorBlock mediaBlock)
        {
            if (index < 0 || index >= _blocks.Count)
                index = _blocks.Count - 1;

            ClearSelection();

            var current = _blocks[index];
            mediaBlock.Indent = current.Indent;

            if (current.Kind == BlockKind.Paragraph && string.IsNullOrEmpty(current.Text) && string.IsNullOrEmpty(current.Source))
            {
                CopyContent(mediaBlock, current);
                EnsureTrailingParagraph(index);
                SyncRows();
                FocusRow(index, 0);
                RecordChange();
            }
            else
            {
                var insertAt = index + 1;
                _blocks.Insert(insertAt, mediaBlock);
                EnsureTrailingParagraph(insertAt);
                SyncRows();
                FocusRow(insertAt, 0);
                RecordChange();
            }
        }

        private void EnsureTrailingParagraph(int index)
        {
            if (index == _blocks.Count - 1)
                _blocks.Add(new EditorBlock(BlockKind.Paragraph, string.Empty, _blocks[index].Indent));
        }

        /// <summary>탐색기·폴더 패널에서 드롭한 파일들을 이미지 블록 또는 파일 첨부 블록으로 삽입한다.</summary>
        internal void HandleDroppedFiles(string[] files, int targetIndex)
        {
            if (files == null || files.Length == 0)
                return;

            var validFiles = files.Where(f => !string.IsNullOrWhiteSpace(f) && File.Exists(f)).ToList();
            if (validFiles.Count == 0)
                return;

            var baseDir = BaseDirectory;
            var hasOutsideImage = false;
            if (!string.IsNullOrEmpty(baseDir) && Directory.Exists(baseDir))
            {
                foreach (var file in validFiles)
                {
                    if (IsImageFile(file) && !IsInsideDirectory(file, baseDir))
                    {
                        hasOutsideImage = true;
                        break;
                    }
                }
            }

            var outsideMode = BlockImageDropMode.CopyToImages;
            if (hasOutsideImage)
            {
                var dlg = new BlockImageDropDialog(validFiles.Count(IsImageFile))
                {
                    Owner = Window.GetWindow(this),
                };
                if (dlg.ShowDialog() != true)
                    return;
                outsideMode = dlg.SelectedMode;
            }

            ClearSelection();

            var insertAt = Math.Max(0, Math.Min(targetIndex, _blocks.Count - 1));
            var indent = _blocks[insertAt].Indent;
            var replaceFirst = _blocks[insertAt].Kind == BlockKind.Paragraph &&
                               string.IsNullOrEmpty(_blocks[insertAt].Text) &&
                               string.IsNullOrEmpty(_blocks[insertAt].Source);

            foreach (var file in validFiles)
            {
                EditorBlock created;
                if (IsImageFile(file))
                {
                    var src = ResolveDroppedImageSource(file, baseDir, outsideMode);
                    var alt = Path.GetFileNameWithoutExtension(file);
                    created = new EditorBlock(BlockKind.Image, alt, indent) { Source = src };
                }
                else
                {
                    var src = ResolveDroppedFileSource(file, baseDir);
                    var name = Path.GetFileName(file);
                    created = new EditorBlock(BlockKind.File, name, indent) { Source = src };
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.Exists)
                            created.SetAttribute(EditorBlock.AttrSize, FormatFileSize(info.Length));
                    }
                    catch
                    {
                    }
                }

                if (replaceFirst)
                {
                    CopyContent(created, _blocks[insertAt]);
                    replaceFirst = false;
                }
                else
                {
                    insertAt++;
                    _blocks.Insert(insertAt, created);
                }
            }

            EnsureTrailingParagraph(insertAt);
            SyncRows();
            FocusRow(insertAt, 0);
            RecordChange();
        }

        private static bool IsInsideDirectory(string filePath, string directoryPath)
        {
            try
            {
                var fullFile = Path.GetFullPath(filePath);
                var fullDir = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                              + Path.DirectorySeparatorChar;
                return fullFile.StartsWith(fullDir, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string ResolveDroppedImageSource(string filePath, string baseDir, BlockImageDropMode mode)
        {
            if (string.IsNullOrEmpty(baseDir) || !Directory.Exists(baseDir))
                return filePath;

            if (IsInsideDirectory(filePath, baseDir))
                return Path.GetRelativePath(baseDir, filePath).Replace('\\', '/');

            switch (mode)
            {
                case BlockImageDropMode.CopyToImages:
                    var imagesDir = Path.Combine(baseDir, "images");
                    Directory.CreateDirectory(imagesDir);
                    var baseName = Path.GetFileNameWithoutExtension(filePath);
                    var ext = Path.GetExtension(filePath);
                    var candidate = baseName + ext;
                    var dest = Path.Combine(imagesDir, candidate);
                    var suffix = 2;
                    while (File.Exists(dest))
                    {
                        candidate = baseName + "-" + suffix + ext;
                        dest = Path.Combine(imagesDir, candidate);
                        suffix++;
                    }
                    File.Copy(filePath, dest, false);
                    return "images/" + candidate;

                case BlockImageDropMode.RelativePath:
                    try
                    {
                        return Path.GetRelativePath(baseDir, filePath).Replace('\\', '/');
                    }
                    catch
                    {
                        return filePath;
                    }

                default:
                    return filePath;
            }
        }

        private static string ResolveDroppedFileSource(string filePath, string baseDir)
        {
            if (!string.IsNullOrEmpty(baseDir) && Directory.Exists(baseDir) && IsInsideDirectory(filePath, baseDir))
                return Path.GetRelativePath(baseDir, filePath).Replace('\\', '/');
            return filePath;
        }

        internal void PromptAndInsertFileBlock(int index)
        {
            var dlg = new OpenFileDialog
            {
                Title = "첨부할 파일 선택",
                InitialDirectory = !string.IsNullOrEmpty(BaseDirectory) && Directory.Exists(BaseDirectory) ? BaseDirectory : null,
            };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true)
                return;

            var src = ResolveDroppedFileSource(dlg.FileName, BaseDirectory);
            var name = Path.GetFileName(dlg.FileName);
            var block = new EditorBlock(BlockKind.File, name) { Source = src };
            try
            {
                var info = new FileInfo(dlg.FileName);
                if (info.Exists)
                    block.SetAttribute(EditorBlock.AttrSize, FormatFileSize(info.Length));
            }
            catch
            {
            }
            InsertOrReplaceMediaBlock(index, block);
        }

        internal void PromptAndInsertBookmarkBlock(int index)
        {
            var dlg = new BlockBookmarkDialog
            {
                Owner = Window.GetWindow(this),
            };
            if (dlg.ShowDialog() != true)
                return;

            var block = new EditorBlock(BlockKind.Bookmark, dlg.BookmarkTitle)
            {
                Source = dlg.BookmarkUrl,
            };
            if (!string.IsNullOrWhiteSpace(dlg.BookmarkTitle))
                block.SetAttribute(EditorBlock.AttrTitle, dlg.BookmarkTitle);
            InsertOrReplaceMediaBlock(index, block);
        }

        internal static string FormatFileSize(long bytes)
        {
            if (bytes < 1024)
                return bytes.ToString(CultureInfo.InvariantCulture) + " B";
            if (bytes < 1024 * 1024)
                return (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
            if (bytes < 1024L * 1024 * 1024)
                return (bytes / (1024.0 * 1024.0)).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
            return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("0.##", CultureInfo.InvariantCulture) + " GB";
        }

        internal void OpenLocalTarget(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
            {
                MessageBox.Show(Window.GetWindow(this), "파일을 찾을 수 없습니다:\n" + fullPath, "파일 열기", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (FileOperationService.IsExecutable(fullPath))
            {
                var confirm = MessageBox.Show(
                    Window.GetWindow(this),
                    "실행 파일입니다. 실행하시겠습니까?\n\n" + fullPath,
                    "실행 확인",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
                if (confirm != MessageBoxResult.Yes)
                    return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this), "파일을 열 수 없습니다.\n" + ex.Message, "파일 열기", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        internal void RevealInExplorer(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                return;
            try
            {
                if (File.Exists(fullPath))
                    Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + fullPath + "\"") { UseShellExecute = true });
                else if (Directory.Exists(fullPath))
                    Process.Start(new ProcessStartInfo("explorer.exe", "\"" + fullPath + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this), "탐색기를 열 수 없습니다.\n" + ex.Message, "탐색기에서 열기", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>문서 내 제목 블록이 바뀌었을 때 화면에 떠 있는 목차(<see cref="BlockKind.Toc"/>) 행을 다시 그린다.</summary>
        internal void RefreshTocRows()
        {
            var hasToc = false;
            for (var i = 0; i < _blocks.Count; i++)
            {
                if (_blocks[i].Kind == BlockKind.Toc)
                {
                    hasToc = true;
                    break;
                }
            }
            if (!hasToc)
                return;

            foreach (var row in _panel.RealizedRows)
            {
                if (row.Block != null && row.Block.Kind == BlockKind.Toc)
                    row.RebuildTocContent();
            }
            _panel.InvalidateMeasure();
        }

        /// <summary>네트워크 설정이 바뀌었을 때 화면에 떠 있는 이미지 행을 다시 확인한다.</summary>
        public void RefreshRemoteImages()
        {
            foreach (var row in _panel.RealizedRows)
            {
                if (row.Block != null && row.Block.Kind == BlockKind.Image)
                    row.RefreshImage();
            }
        }
    }

    internal enum BlockImageDropMode
    {
        CopyToImages,
        RelativePath,
        AbsolutePath,
    }

    /// <summary>문서 폴더 밖의 이미지를 드롭했을 때 처리 방식을 고르는 대화상자.</summary>
    internal sealed class BlockImageDropDialog : GitDialogBase
    {
        private readonly RadioButton _copyRadio;
        private readonly RadioButton _relativeRadio;
        private readonly RadioButton _absoluteRadio;

        public BlockImageDropMode SelectedMode
        {
            get
            {
                if (_relativeRadio.IsChecked == true) return BlockImageDropMode.RelativePath;
                if (_absoluteRadio.IsChecked == true) return BlockImageDropMode.AbsolutePath;
                return BlockImageDropMode.CopyToImages;
            }
        }

        public BlockImageDropDialog(int imageCount)
            : base("외부 이미지 삽입 방식", 440)
        {
            AddText(imageCount > 1
                ? string.Format("문서 폴더 밖에 있는 이미지 {0}개를 어떻게 넣을까요?", imageCount)
                : "문서 폴더 밖에 있는 이미지를 어떻게 넣을까요?");
            _copyRadio = AddRadio("imgmode", "문서 옆 images/ 폴더로 복사 후 상대 경로로 삽입 (권장)", "문서를 다른 곳으로 옮겨도 이미지가 함께 유지됩니다.", true);
            _relativeRadio = AddRadio("imgmode", "원래 위치의 상대 경로로 참조", "파일을 복사하지 않고 현재 문서 기준 상대 경로로 연결합니다.", false);
            _absoluteRadio = AddRadio("imgmode", "절대 경로로 참조", "이 PC의 전체 경로를 그대로 기록합니다.", false);
            AddButtons("삽입");
        }
    }

    /// <summary>북마크 블록 추가 시 URL과 표시 제목을 입력받는 대화상자.</summary>
    internal sealed class BlockBookmarkDialog : GitDialogBase
    {
        private readonly TextBox _urlBox;
        private readonly TextBox _titleBox;

        public string BookmarkUrl => (_urlBox.Text ?? string.Empty).Trim();
        public string BookmarkTitle => (_titleBox.Text ?? string.Empty).Trim();

        public BlockBookmarkDialog()
            : base("북마크 블록 추가", 440)
        {
            AddLabel("웹 주소 (URL)");
            _urlBox = new TextBox { Text = "https://", Padding = new Thickness(6, 4, 6, 4) };
            Body.Children.Add(_urlBox);

            AddLabel("표시 제목 (생략 가능)");
            _titleBox = new TextBox { Padding = new Thickness(6, 4, 6, 4) };
            Body.Children.Add(_titleBox);

            AddText("추가한 뒤 카드에서 '미리보기 불러오기'를 누르면 웹페이지의 제목과 설명을 가져옵니다.", secondary: true, top: 8);
            AddButtons("추가");
            Loaded += (_, __) =>
            {
                _urlBox.Focus();
                _urlBox.SelectAll();
            };
        }

        protected override Task<string> ValidateAsync()
        {
            var url = BookmarkUrl;
            Uri parsed;
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                return Task.FromResult("올바른 http:// 또는 https:// 주소를 입력하세요.");
            }
            return Task.FromResult<string>(null);
        }
    }

    /// <summary>이미지 블록의 '원본 크기로 보기' 모달 창.</summary>
    internal sealed class BlockImagePreviewWindow : Window
    {
        public BlockImagePreviewWindow(ImageSource imageSource, string title, string localPath = null, Action onOpenInExplorer = null)
        {
            var bitmap = imageSource as BitmapSource;
            var pixelW = bitmap != null ? bitmap.PixelWidth : 800;
            var pixelH = bitmap != null ? bitmap.PixelHeight : 600;
            Title = string.IsNullOrWhiteSpace(title) ? "이미지 원본 보기" : title;
            Width = Math.Min(1200, Math.Max(480, pixelW + 60));
            Height = Math.Min(860, Math.Max(360, pixelH + 90));
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SetResourceReference(BackgroundProperty, "WindowBackground");
            SetResourceReference(ForegroundProperty, "PrimaryText");

            var root = new DockPanel();
            var bar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(12, 8, 12, 8),
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            DockPanel.SetDock(bar, Dock.Top);

            var info = new TextBlock
            {
                Text = string.Format(CultureInfo.InvariantCulture, "{0} × {1} px", pixelW, pixelH),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
            };
            info.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");

            var img = new Image
            {
                Source = imageSource,
                Stretch = Stretch.None,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

            var fitToggle = new CheckBox
            {
                Content = "창 크기에 맞춤",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
            };
            fitToggle.SetResourceReference(ForegroundProperty, "PrimaryText");
            fitToggle.Checked += (_, __) => img.Stretch = Stretch.Uniform;
            fitToggle.Unchecked += (_, __) => img.Stretch = Stretch.None;

            bar.Children.Add(info);
            bar.Children.Add(fitToggle);
            if (!string.IsNullOrEmpty(localPath) && onOpenInExplorer != null)
            {
                var openExplorerBtn = new Button
                {
                    Content = "탐색기에서 열기",
                    Padding = new Thickness(10, 3, 10, 3),
                    Margin = new Thickness(0, 0, 8, 0),
                };
                openExplorerBtn.Click += (_, __) => onOpenInExplorer();
                bar.Children.Add(openExplorerBtn);
            }
            var closeBtn = new Button { Content = "닫기 (Esc)", Padding = new Thickness(10, 3, 10, 3), IsCancel = true };
            closeBtn.Click += (_, __) => Close();
            bar.Children.Add(closeBtn);
            root.Children.Add(bar);

            var scroller = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = img,
                Padding = new Thickness(12),
            };
            root.Children.Add(scroller);
            Content = root;

            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Close();
                    e.Handled = true;
                }
            };
        }
    }

    /// <summary>
    /// 외부 NuGet 의존성 없는 경량 순수 C# WPF 수식 렌더러.
    /// 분수(<c>\frac{a}{b}</c>), 위·아래 첨자(<c>^{...}</c>, <c>_{...}</c>), 제곱근(<c>\sqrt{...}</c>),
    /// 그리스 문자·수학 기호를 WPF <see cref="Inline"/> 및 <see cref="UIElement"/>로 변환한다.
    /// </summary>
    internal static class BlockMathRenderer
    {
        private static readonly FontFamily MathFont = new FontFamily("Cambria Math, Segoe UI Symbol, Segoe UI");

        private static readonly Dictionary<string, string> Symbols = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // 소문자 그리스 문자
            { "alpha", "α" }, { "beta", "β" }, { "gamma", "γ" }, { "delta", "δ" },
            { "epsilon", "ϵ" }, { "varepsilon", "ε" }, { "zeta", "ζ" }, { "eta", "η" },
            { "theta", "θ" }, { "vartheta", "ϑ" }, { "iota", "ι" }, { "kappa", "κ" },
            { "lambda", "λ" }, { "mu", "μ" }, { "nu", "ν" }, { "xi", "ξ" },
            { "pi", "π" }, { "rho", "ρ" }, { "sigma", "σ" }, { "tau", "τ" },
            { "upsilon", "υ" }, { "phi", "ϕ" }, { "varphi", "φ" }, { "chi", "χ" },
            { "psi", "ψ" }, { "omega", "ω" },
            // 대문자 그리스 문자
            { "Gamma", "Γ" }, { "Delta", "Δ" }, { "Theta", "Θ" }, { "Lambda", "Λ" },
            { "Xi", "Ξ" }, { "Pi", "Π" }, { "Sigma", "Σ" }, { "Upsilon", "Υ" },
            { "Phi", "Φ" }, { "Psi", "Ψ" }, { "Omega", "Ω" },
            // 연산자 및 관계 기호
            { "sum", "∑" }, { "prod", "∏" }, { "coprod", "∐" },
            { "int", "∫" }, { "iint", "∬" }, { "iiint", "∭" }, { "oint", "∮" },
            { "pm", "±" }, { "mp", "∓" }, { "times", "×" }, { "div", "÷" },
            { "cdot", "·" }, { "ast", "∗" }, { "star", "⋆" }, { "circ", "∘" }, { "bullet", "•" },
            { "neq", "≠" }, { "ne", "≠" }, { "leq", "≤" }, { "le", "≤" },
            { "geq", "≥" }, { "ge", "≥" }, { "ll", "≪" }, { "gg", "≫" },
            { "approx", "≈" }, { "equiv", "≡" }, { "sim", "∼" }, { "simeq", "≃" },
            { "cong", "≅" }, { "propto", "∝" }, { "infty", "∞" },
            { "partial", "∂" }, { "nabla", "∇" }, { "forall", "∀" }, { "exists", "∃" }, { "nexists", "∄" },
            { "in", "∈" }, { "notin", "∉" }, { "ni", "∋" },
            { "subset", "⊂" }, { "supset", "⊃" }, { "subseteq", "⊆" }, { "supseteq", "⊇" },
            { "cup", "∪" }, { "cap", "∩" }, { "emptyset", "∅" }, { "varnothing", "∅" },
            { "land", "∧" }, { "lor", "∨" }, { "lnot", "¬" }, { "neg", "¬" },
            { "oplus", "⊕" }, { "otimes", "⊗" }, { "perp", "⊥" }, { "parallel", "∥" },
            { "angle", "∠" }, { "triangle", "△" }, { "therefore", "∴" }, { "because", "∵" },
            { "to", "→" }, { "rightarrow", "→" }, { "leftarrow", "←" }, { "leftrightarrow", "↔" },
            { "Rightarrow", "⇒" }, { "Leftarrow", "⇐" }, { "Leftrightarrow", "⇔" }, { "iff", "⇔" },
            { "mapsto", "↦" }, { "uparrow", "↑" }, { "downarrow", "↓" },
            { "dots", "…" }, { "ldots", "…" }, { "cdots", "⋯" }, { "vdots", "⋮" }, { "ddots", "⋱" },
            { "quad", "  " }, { "qquad", "    " }, { ",", " " }, { ";", " " }, { ":", " " },
            { "left", "" }, { "right", "" },
            { "sin", "sin" }, { "cos", "cos" }, { "tan", "tan" }, { "log", "log" }, { "ln", "ln" },
            { "lim", "lim" }, { "max", "max" }, { "min", "min" }, { "exp", "exp" }, { "det", "det" },
        };

        /// <summary>수식 블록(<see cref="BlockKind.Math"/>)의 비편집 표시용 인라인들을 채운다.</summary>
        public static void PopulateInlines(InlineCollection target, string latex, double baseFontSize)
        {
            target.Clear();
            if (string.IsNullOrWhiteSpace(latex))
            {
                var placeholder = new Run("수식을 입력하려면 클릭하세요 (예: \\frac{-b \\pm \\sqrt{b^2 - 4ac}}{2a})")
                {
                    FontStyle = FontStyles.Italic,
                };
                placeholder.SetResourceReference(TextElement.ForegroundProperty, "SecondaryText");
                target.Add(placeholder);
                return;
            }

            var lines = latex.Replace("\r\n", "\n").Split('\n');
            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                if (lineIndex > 0)
                    target.Add(new LineBreak());
                AppendMathNodes(target, lines[lineIndex], baseFontSize);
            }
        }

        /// <summary>문단 안의 인라인 수식(<c>$...$</c>)을 변환한 <see cref="Span"/>을 만든다.</summary>
        public static Span CreateInlineMathSpan(string latex, double baseFontSize)
        {
            var span = new Span
            {
                FontFamily = MathFont,
                FontStyle = FontStyles.Italic,
            };
            span.SetResourceReference(TextElement.BackgroundProperty, "SurfaceBackground");
            AppendMathNodes(span.Inlines, latex, baseFontSize);
            return span;
        }

        /// <summary>문단 텍스트 안에 인라인 수식(<c>$...$</c>)이 들어 있는지 확인한다.</summary>
        public static bool ContainsInlineMath(string text)
        {
            int start, end;
            return FindInlineMath(text, 0, out start, out end);
        }

        /// <summary>일반 텍스트와 인라인 수식(<c>$...$</c>)이 섞인 문단을 <see cref="InlineCollection"/>으로 채운다.</summary>
        public static void PopulateMixedInlines(InlineCollection target, string text, double baseFontSize)
        {
            target.Clear();
            if (string.IsNullOrEmpty(text))
                return;

            var i = 0;
            int start, end;
            while (FindInlineMath(text, i, out start, out end))
            {
                if (start > i)
                    target.Add(new Run(text.Substring(i, start - i)));
                target.Add(CreateInlineMathSpan(text.Substring(start + 1, end - start - 1), baseFontSize));
                i = end + 1;
            }
            if (i < text.Length)
                target.Add(new Run(text.Substring(i)));
        }

        /// <summary>
        /// <paramref name="from"/>부터 인라인 수식 <c>$...$</c>의 여는·닫는 <c>$</c> 위치를 찾는다(Pandoc 규칙).
        /// 여는 <c>$</c> 바로 뒤와 닫는 <c>$</c> 바로 앞은 공백이 아니고, 닫는 <c>$</c> 바로 뒤는 숫자가 아니어야 한다 —
        /// "$5 ~ $10" 같은 금액은 수식이 아니다. <c>\$</c>는 글자이고, 줄바꿈을 넘지 않는다.
        /// </summary>
        private static bool FindInlineMath(string text, int from, out int start, out int end)
        {
            start = end = -1;
            if (string.IsNullOrEmpty(text))
                return false;
            for (var open = text.IndexOf('$', from); open >= 0 && open + 2 < text.Length; open = text.IndexOf('$', open + 1))
            {
                if (IsEscaped(text, open) || char.IsWhiteSpace(text[open + 1]) || text[open + 1] == '$')
                    continue;
                for (var close = text.IndexOf('$', open + 2); close >= 0; close = text.IndexOf('$', close + 1))
                {
                    if (text.IndexOf('\n', open + 1, close - open - 1) >= 0)
                        break;
                    if (IsEscaped(text, close) || char.IsWhiteSpace(text[close - 1]) ||
                        (close + 1 < text.Length && char.IsDigit(text[close + 1])))
                        continue;
                    start = open;
                    end = close;
                    return true;
                }
            }
            return false;
        }

        private static bool IsEscaped(string text, int index)
        {
            return index > 0 && text[index - 1] == '\\';
        }

        private static void AppendMathNodes(InlineCollection inlines, string expr, double fontSize)
        {
            if (string.IsNullOrEmpty(expr))
                return;

            var buf = new StringBuilder();
            var i = 0;
            while (i < expr.Length)
            {
                var c = expr[i];
                if (c == '\\')
                {
                    i++;
                    if (i >= expr.Length)
                        break;

                    if (!char.IsLetter(expr[i]))
                    {
                        var singleCmd = expr[i].ToString();
                        string mappedSingle;
                        if (Symbols.TryGetValue(singleCmd, out mappedSingle))
                            buf.Append(mappedSingle);
                        else
                            buf.Append(expr[i]);
                        i++;
                        continue;
                    }

                    var start = i;
                    while (i < expr.Length && char.IsLetter(expr[i]))
                        i++;
                    var cmd = expr.Substring(start, i - start);
                    while (i < expr.Length && expr[i] == ' ')
                        i++;

                    if (cmd == "frac")
                    {
                        FlushRun(inlines, buf, fontSize);
                        var num = ReadGroup(expr, ref i);
                        var den = ReadGroup(expr, ref i);
                        inlines.Add(BuildFractionInline(num, den, fontSize));
                        continue;
                    }

                    if (cmd == "sqrt")
                    {
                        FlushRun(inlines, buf, fontSize);
                        if (i < expr.Length && expr[i] == '[')
                        {
                            var closeBracket = expr.IndexOf(']', i + 1);
                            if (closeBracket > i)
                            {
                                var deg = expr.Substring(i + 1, closeBracket - i - 1);
                                i = closeBracket + 1;
                                var degRun = new Run(deg)
                                {
                                    FontFamily = MathFont,
                                    FontSize = Math.Max(9, fontSize * 0.65),
                                    BaselineAlignment = BaselineAlignment.Superscript,
                                };
                                inlines.Add(degRun);
                            }
                        }
                        var rad = ReadGroup(expr, ref i);
                        inlines.Add(BuildSqrtInline(rad, fontSize));
                        continue;
                    }

                    if (cmd == "text" || cmd == "mathrm" || cmd == "mathbf")
                    {
                        FlushRun(inlines, buf, fontSize);
                        var content = ReadGroup(expr, ref i);
                        var run = new Run(content)
                        {
                            FontStyle = FontStyles.Normal,
                            FontWeight = cmd == "mathbf" ? FontWeights.Bold : FontWeights.Normal,
                            FontSize = fontSize,
                        };
                        inlines.Add(run);
                        continue;
                    }

                    string sym;
                    if (Symbols.TryGetValue(cmd, out sym))
                        buf.Append(sym);
                    else
                        buf.Append(cmd);
                    continue;
                }

                if (c == '^' || c == '_')
                {
                    FlushRun(inlines, buf, fontSize);
                    var isSuper = c == '^';
                    i++;
                    var subExpr = ReadGroup(expr, ref i);
                    var subSpan = new Span
                    {
                        FontFamily = MathFont,
                        FontSize = Math.Max(9.5, fontSize * 0.75),
                        BaselineAlignment = isSuper ? BaselineAlignment.Superscript : BaselineAlignment.Subscript,
                    };
                    AppendMathNodes(subSpan.Inlines, subExpr, Math.Max(9.5, fontSize * 0.75));
                    inlines.Add(subSpan);
                    continue;
                }

                if (c == '{' || c == '}')
                {
                    i++;
                    continue;
                }

                if (c == '-')
                    buf.Append('−');
                else
                    buf.Append(c);
                i++;
            }

            FlushRun(inlines, buf, fontSize);
        }

        private static void FlushRun(InlineCollection inlines, StringBuilder buf, double fontSize)
        {
            if (buf.Length == 0)
                return;
            inlines.Add(new Run(buf.ToString())
            {
                FontFamily = MathFont,
                FontSize = fontSize,
            });
            buf.Clear();
        }

        private static string ReadGroup(string expr, ref int index)
        {
            while (index < expr.Length && expr[index] == ' ')
                index++;
            if (index >= expr.Length)
                return string.Empty;

            if (expr[index] == '{')
            {
                index++;
                var start = index;
                var depth = 1;
                while (index < expr.Length && depth > 0)
                {
                    if (expr[index] == '{') depth++;
                    else if (expr[index] == '}') depth--;
                    index++;
                }
                return expr.Substring(start, Math.Max(0, index - start - 1));
            }

            if (expr[index] == '\\')
            {
                var start = index;
                index++;
                while (index < expr.Length && char.IsLetter(expr[index]))
                    index++;
                return expr.Substring(start, index - start);
            }

            return expr[index++].ToString();
        }

        private static Inline BuildFractionInline(string num, string den, double fontSize)
        {
            var childSize = Math.Max(10.5, fontSize * 0.85);
            var numBlock = new TextBlock
            {
                FontFamily = MathFont,
                FontSize = childSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(3, 0, 3, 1),
            };
            numBlock.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
            AppendMathNodes(numBlock.Inlines, num, childSize);

            var line = new Border
            {
                Height = 1,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            line.SetResourceReference(Border.BackgroundProperty, "PrimaryText");

            var denBlock = new TextBlock
            {
                FontFamily = MathFont,
                FontSize = childSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(3, 1, 3, 0),
            };
            denBlock.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
            AppendMathNodes(denBlock.Inlines, den, childSize);

            var stack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 2, 0),
            };
            stack.Children.Add(numBlock);
            stack.Children.Add(line);
            stack.Children.Add(denBlock);

            return new InlineUIContainer(stack)
            {
                BaselineAlignment = BaselineAlignment.Center,
            };
        }

        private static Inline BuildSqrtInline(string radicand, double fontSize)
        {
            var rootSign = new TextBlock
            {
                Text = "√",
                FontFamily = MathFont,
                FontSize = fontSize * 1.1,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(1, 0, 0, 0),
            };
            rootSign.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");

            var innerText = new TextBlock
            {
                FontFamily = MathFont,
                FontSize = fontSize,
                Padding = new Thickness(2, 1, 3, 0),
            };
            innerText.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
            AppendMathNodes(innerText.Inlines, radicand, fontSize);

            var overline = new Border
            {
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = innerText,
                VerticalAlignment = VerticalAlignment.Center,
            };
            overline.SetResourceReference(Border.BorderBrushProperty, "PrimaryText");

            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };
            panel.Children.Add(rootSign);
            panel.Children.Add(overline);

            return new InlineUIContainer(panel)
            {
                BaselineAlignment = BaselineAlignment.Baseline,
            };
        }
    }
}

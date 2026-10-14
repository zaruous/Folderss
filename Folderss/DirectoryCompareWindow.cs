using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Folderss.Controls;
using Folderss.Models;
using Folderss.Services;
using Forms = System.Windows.Forms;

namespace Folderss
{
    /// <summary>
    /// 두 폴더를 비교하는 비모달 창. 머리글(<see cref="ComparePairHeader"/>)과 결과 목록(<see cref="DirectoryCompareView"/>)을 담고
    /// 비교 실행·옵션·복사·탐색기 열기를 처리한다. 판정은 <see cref="DirectoryCompareService"/>.
    /// 비교 버튼은 저장된 옵션(기본 = 권장값)으로 바로 실행하고, 옵션은 옆 ▾에서만 바꾼다.
    /// </summary>
    public sealed class DirectoryCompareWindow : Window
    {
        private string _left;
        private string _right;
        private DirectoryCompareOptions _options;
        private readonly Action<string, string> _openFileCompare;
        private readonly ComparePairHeader _header = new ComparePairHeader(
            "왼쪽", "오른쪽", "왼쪽과 오른쪽 폴더를 바꿔 다시 비교합니다");
        private readonly DirectoryCompareView _view = new DirectoryCompareView();
        private readonly Button _compareButton = new Button { Content = "비교", MinWidth = 70 };
        private readonly Button _cancelButton = new Button { Content = "중지", MinWidth = 60, IsEnabled = false };
        private readonly CheckBox _differencesOnly = new CheckBox
        {
            Content = "차이만 보기",
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            ToolTip = "같은 항목(자식이 모두 같은 폴더 포함)을 숨깁니다"
        };
        private readonly TextBlock _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock _optionSummary = new TextBlock { FontSize = 12, Margin = new Thickness(2, 6, 2, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        private CancellationTokenSource _run;
        private bool _closed;

        /// <param name="openFileCompare">양쪽에 있는 파일 한 쌍을 두 파일 비교 창으로 연다(왼쪽, 오른쪽 전체 경로).</param>
        public DirectoryCompareWindow(string left, string right, Action<string, string> openFileCompare)
        {
            _left = left;
            _right = right;
            _openFileCompare = openFileCompare;
            _options = CompareSettingsService.Load();

            Width = 1150;
            Height = 720;
            MinWidth = 640;
            MinHeight = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            SetResourceReference(BackgroundProperty, "WindowBackground");
            SetResourceReference(ForegroundProperty, "PrimaryText");
            SetResourceReference(FontFamilyProperty, "AppFontFamily");
            FontSize = 13;

            _header.CanBrowse = true;
            _header.SwapRequested += (s, e) =>
            {
                (_left, _right) = (_right, _left);
                UpdateHeader();
                StartCompare();
            };
            _header.BrowseRequested += (s, isLeft) => Browse(isLeft);

            _view.ShowDifferencesOnly = true;
            _view.CompareRequested += (s, entry) => _openFileCompare?.Invoke(FullPath(entry, true), FullPath(entry, false));
            _view.CopyRequested += View_CopyRequested;
            _view.RevealRequested += (s, e) => Reveal(FullPath(e.Entry, e.Left));

            var root = new DockPanel { Margin = new Thickness(8) };
            DockPanel.SetDock(_header, Dock.Top);
            root.Children.Add(_header);
            var toolbar = BuildToolbar();
            DockPanel.SetDock(toolbar, Dock.Top);
            root.Children.Add(toolbar);
            _view.Margin = new Thickness(0, 8, 0, 0);
            root.Children.Add(_view);
            Content = root;

            UpdateHeader();
            Loaded += (s, e) => StartCompare();
            Closed += (s, e) =>
            {
                _closed = true;
                _run?.Cancel();
            };
        }

        private StackPanel BuildToolbar()
        {
            var panel = new StackPanel();
            var row = new DockPanel();

            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            _compareButton.ToolTip = "저장된 옵션으로 다시 비교합니다 (옵션은 ▾)";
            _compareButton.Click += (s, e) => StartCompare();
            var options = new Button { ToolTip = "비교 옵션 (판정 기준·시각 허용 오차·ignore 규칙·하위 폴더·숨김 파일)" };
            options.SetResourceReference(StyleProperty, "OptionArrowButton");
            options.Click += (s, e) => EditOptions();
            _cancelButton.Margin = new Thickness(6, 0, 0, 0);
            _cancelButton.Click += (s, e) => _run?.Cancel();
            _differencesOnly.SetResourceReference(ForegroundProperty, "PrimaryText");
            _differencesOnly.Click += (s, e) => _view.ShowDifferencesOnly = _differencesOnly.IsChecked == true;
            buttons.Children.Add(_compareButton);
            buttons.Children.Add(options);
            buttons.Children.Add(_cancelButton);
            buttons.Children.Add(_differencesOnly);
            DockPanel.SetDock(buttons, Dock.Left);
            row.Children.Add(buttons);
            row.Children.Add(_status);

            _optionSummary.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            panel.Children.Add(row);
            panel.Children.Add(_optionSummary);
            return panel;
        }

        private void UpdateHeader()
        {
            Title = "폴더 비교 - " + DisplayName(_left) + " ↔ " + DisplayName(_right);
            _header.SetPaths(_left, _right);
            _optionSummary.Text = DescribeOptions(_options);
        }

        private static string DisplayName(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "(선택 안 함)";
            var name = Path.GetFileName(path.TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(name) ? path : name;
        }

        public static string DescribeOptions(DirectoryCompareOptions options)
        {
            var parts = new List<string>
            {
                options.UseContentHash
                    ? "기준: 내용 해시"
                    : "기준: 크기·수정 시각(±" + options.TimestampTolerance.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture) + "초)",
                options.ApplyIgnoreRules ? "ignore 규칙 적용" : "ignore 규칙 무시",
                !options.Recursive ? "하위 폴더 안 봄" : options.MaxDepth > 0 ? "하위 폴더 " + options.MaxDepth + "단계까지" : "하위 폴더 전체",
                options.IncludeHidden ? "숨김 파일 포함" : "숨김 파일 제외"
            };
            return string.Join(" · ", parts);
        }

        private async void StartCompare()
        {
            if (string.IsNullOrEmpty(_left) || string.IsNullOrEmpty(_right))
            {
                _status.Text = "비교할 폴더를 양쪽 모두 고르세요 (…)";
                _view.Result = null;
                return;
            }

            _run?.Cancel();
            var run = new CancellationTokenSource();
            _run = run;
            _compareButton.IsEnabled = false;
            _cancelButton.IsEnabled = true;
            _status.Text = "비교 중…";
            var progress = new Progress<int>(folders =>
            {
                if (ReferenceEquals(_run, run))
                    _status.Text = "비교 중… 폴더 " + folders.ToString("N0") + "개";
            });

            try
            {
                var result = await DirectoryCompareService.CompareAsync(_left, _right, _options.Clone(), progress, run.Token);
                if (!ReferenceEquals(_run, run) || _closed)
                    return;
                _view.Result = result;
                _status.Text = Summarize(result);
            }
            catch (Exception ex)
            {
                if (ReferenceEquals(_run, run) && !_closed)
                {
                    _view.Result = null;
                    _status.Text = "비교하지 못했습니다: " + ex.Message;
                }
            }
            finally
            {
                if (ReferenceEquals(_run, run))
                {
                    _compareButton.IsEnabled = true;
                    _cancelButton.IsEnabled = false;
                    _run = null;
                }
                run.Dispose();
            }
        }

        public static string Summarize(CompareResult result)
        {
            var text = string.Format("같음 {0:N0} · 다름 {1:N0} · 왼쪽만 {2:N0} · 오른쪽만 {3:N0}",
                result.SameCount, result.DifferentCount, result.LeftOnlyCount, result.RightOnlyCount);
            if (result.TypeMismatchCount > 0)
                text += string.Format(" · 유형 다름 {0:N0}", result.TypeMismatchCount);
            if (result.UnknownCount > 0)
                text += string.Format(" · 비교 불가 {0:N0}", result.UnknownCount);
            if (!result.IsComplete)
                text += "  (중지됨 — 일부 결과)";
            else if (!result.HasDifferences && result.UnknownCount == 0)
                text += "  — 차이 없음";
            return text;
        }

        private void EditOptions()
        {
            var dialog = new DirectoryCompareOptionsDialog(_options) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;
            _options = dialog.Result;
            try
            {
                CompareSettingsService.Save(_options);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "비교 옵션을 저장하지 못했습니다. 이번 비교에만 적용합니다.\n\n" + ex.Message, "폴더 비교",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            UpdateHeader();
            StartCompare();
        }

        private void Browse(bool isLeft)
        {
            using (var dialog = new Forms.FolderBrowserDialog())
            {
                var current = isLeft ? _left : _right;
                dialog.Description = (isLeft ? "왼쪽" : "오른쪽") + "에서 비교할 폴더를 선택하세요.";
                dialog.UseDescriptionForTitle = true;
                if (!string.IsNullOrEmpty(current) && Directory.Exists(current))
                    dialog.SelectedPath = current;
                if (dialog.ShowDialog() != Forms.DialogResult.OK)
                    return;
                if (isLeft)
                    _left = dialog.SelectedPath;
                else
                    _right = dialog.SelectedPath;
            }
            UpdateHeader();
            StartCompare();
        }

        private string FullPath(CompareEntry entry, bool left)
        {
            var result = _view.Result;
            return Path.Combine(left ? result.LeftRoot : result.RightRoot, entry.RelativePath);
        }

        private void View_CopyRequested(object sender, CopyRequestEventArgs e)
        {
            var result = _view.Result;
            if (result == null || _run != null)
                return;
            var entries = e.Entries.Where(entry => DirectoryCompareService.CanCopy(entry, e.ToRight, out _)).ToList();
            if (entries.Count == 0)
                return;

            var direction = e.ToRight ? "왼쪽 → 오른쪽" : "오른쪽 → 왼쪽";
            var overwrites = entries.Count(entry => e.ToRight ? entry.RightExists : entry.LeftExists);
            var preview = string.Join("\n", entries.Take(10).Select(entry => "  " + entry.RelativePath))
                + (entries.Count > 10 ? "\n  … 외 " + (entries.Count - 10) + "개" : string.Empty);
            var message = string.Format("{0}개 항목을 {1}로 복사합니다.\n\n{2}", entries.Count, direction, preview);
            if (overwrites > 0)
                message += string.Format("\n\n⚠ 이 중 {0}개는 대상 파일을 덮어씁니다. 덮어쓴 파일은 되돌릴 수 없습니다.", overwrites);
            if (MessageBox.Show(this, message, "폴더 비교 - 복사", MessageBoxButton.OKCancel,
                    overwrites > 0 ? MessageBoxImage.Warning : MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK)
                return;

            var errors = new List<string>();
            foreach (var entry in entries)
            {
                try
                {
                    DirectoryCompareService.CopyEntry(result, entry, e.ToRight);
                }
                catch (Exception ex)
                {
                    errors.Add(entry.RelativePath + ": " + ex.Message);
                }
            }
            if (errors.Count > 0)
                MessageBox.Show(this, string.Format("{0}개 항목을 복사하지 못했습니다.\n\n{1}", errors.Count, string.Join("\n", errors.Take(10))),
                    "폴더 비교 - 복사", MessageBoxButton.OK, MessageBoxImage.Warning);
            StartCompare();
        }

        private void Reveal(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "탐색기로 열기", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    /// <summary>폴더 비교 옵션. 처음 값은 권장값(크기·수정 시각 ±2초, ignore 적용, 하위 전체, 숨김 제외)이다.</summary>
    public sealed class DirectoryCompareOptionsDialog : GitDialogBase
    {
        private readonly RadioButton _byTime;
        private readonly RadioButton _byHash;
        private readonly TextBox _tolerance;
        private readonly CheckBox _ignore;
        private readonly CheckBox _recursive;
        private readonly TextBox _maxDepth;
        private readonly CheckBox _hidden;

        public DirectoryCompareOptions Result { get; private set; }

        public DirectoryCompareOptionsDialog(DirectoryCompareOptions current) : base("폴더 비교 옵션", 440)
        {
            current = current ?? new DirectoryCompareOptions();

            AddLabel("판정 기준");
            _byTime = AddRadio("criteria", "크기와 수정 시각 (권장)", "빠릅니다. 크기가 같고 수정 시각 차이가 허용 오차 안이면 같음.", !current.UseContentHash);
            _byHash = AddRadio("criteria", "내용 해시 (SHA-256)", "크기가 같은 파일의 내용을 모두 읽어 비교합니다. 정확하지만 느립니다. 수정 시각은 보지 않습니다.", current.UseContentHash);
            _tolerance = AddNumber("수정 시각 허용 오차(초)", current.TimestampTolerance.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture),
                "FAT·네트워크 드라이브는 시각을 2초 단위로 저장해 기본 2초입니다.");

            AddLabel("범위");
            _ignore = AddCheck(".gitignore / .folderssignore 규칙에 걸리는 항목 제외 (권장)", current.ApplyIgnoreRules);
            _recursive = AddCheck("하위 폴더까지 비교 (권장)", current.Recursive);
            _maxDepth = AddNumber("최대 깊이 (0 = 무제한)", current.MaxDepth.ToString(CultureInfo.InvariantCulture), null);
            _hidden = AddCheck("숨김·시스템 파일 포함", current.IncludeHidden);

            _byHash.Checked += (s, e) => _tolerance.IsEnabled = false;
            _byTime.Checked += (s, e) => _tolerance.IsEnabled = true;
            _tolerance.IsEnabled = !current.UseContentHash;
            _recursive.Click += (s, e) => _maxDepth.IsEnabled = _recursive.IsChecked == true;
            _maxDepth.IsEnabled = current.Recursive;

            var reset = new Button { Content = "권장값으로", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
            reset.Click += (s, e) => Apply(new DirectoryCompareOptions());
            Body.Children.Add(reset);
            AddButtons("확인 후 비교");
        }

        private void Apply(DirectoryCompareOptions options)
        {
            _byTime.IsChecked = !options.UseContentHash;
            _byHash.IsChecked = options.UseContentHash;
            _tolerance.Text = options.TimestampTolerance.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture);
            _ignore.IsChecked = options.ApplyIgnoreRules;
            _recursive.IsChecked = options.Recursive;
            _maxDepth.Text = options.MaxDepth.ToString(CultureInfo.InvariantCulture);
            _maxDepth.IsEnabled = options.Recursive;
            _hidden.IsChecked = options.IncludeHidden;
        }

        private TextBox AddNumber(string label, string value, string hint)
        {
            var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var box = new TextBox { Text = value, Width = 70, HorizontalAlignment = HorizontalAlignment.Left };
            row.Children.Add(text);
            row.Children.Add(box);
            Body.Children.Add(row);
            if (hint != null)
                AddText(hint, secondary: true, top: 2);
            return box;
        }

        protected override Task<string> ValidateAsync()
        {
            if (!double.TryParse(_tolerance.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || seconds < 0 || seconds > 86400)
                return Task.FromResult("수정 시각 허용 오차는 0 ~ 86400 사이의 초로 입력하세요.");
            if (!int.TryParse(_maxDepth.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var depth) || depth < 0)
                return Task.FromResult("최대 깊이는 0 이상의 정수로 입력하세요 (0 = 무제한).");
            Result = new DirectoryCompareOptions
            {
                UseContentHash = _byHash.IsChecked == true,
                TimestampTolerance = TimeSpan.FromSeconds(seconds),
                ApplyIgnoreRules = _ignore.IsChecked == true,
                Recursive = _recursive.IsChecked == true,
                MaxDepth = depth,
                IncludeHidden = _hidden.IsChecked == true
            };
            return Task.FromResult<string>(null);
        }
    }
}

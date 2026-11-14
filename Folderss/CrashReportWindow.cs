using Folderss.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Folderss
{
    /// <summary>
    /// ⋯ 메뉴 > 보기 > 크래시 정보: crash-log.txt·plugin-log.txt·이벤트 뷰어(Application Error / WER / .NET Runtime)·보존된 덤프를
    /// 한 목록으로 보여 주고, 선택 항목이나 전체를 붙여넣기 좋은 글자로 복사한다.
    /// </summary>
    public sealed class CrashReportWindow : Window
    {
        private const string LocalDumpsKey = @"SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\Folderss.exe";
        private const int EventDays = 30;
        private const int MaxEventsScanned = 3000;

        private readonly ListBox _list;
        private readonly TextBox _detail;
        private readonly TextBlock _status;
        private readonly TextBlock _copyStatus;
        private readonly Button _dumpSetupButton;
        private List<CrashReportEntry> _entries = new List<CrashReportEntry>();

        public CrashReportWindow()
        {
            Title = "크래시 정보";
            Width = 1100;
            Height = 680;
            MinWidth = 640;
            MinHeight = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SetResourceReference(BackgroundProperty, "WindowBackground");
            SetResourceReference(ForegroundProperty, "PrimaryText");

            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            toolbar.Children.Add(MakeButton("새로 고침", async (s, e) => await ReloadAsync()));
            toolbar.Children.Add(MakeButton("선택 항목 복사", (s, e) => CopySelected()));
            toolbar.Children.Add(MakeButton("전체 복사", (s, e) => CopyAll()));
            toolbar.Children.Add(MakeButton("로그 폴더 열기", (s, e) => OpenFolder(Path.GetDirectoryName(CrashLog.LogPath))));
            _dumpSetupButton = MakeButton("덤프 보존 설정 명령 복사", (s, e) => CopyDumpSetupCommand());
            _dumpSetupButton.ToolTip = "훅이 돌지 못하는 크래시(네이티브 경계 등)도 스택을 볼 수 있게, Windows가 크래시 덤프를 지우지 않고 남기도록 하는 관리자 PowerShell 명령을 복사합니다.";
            toolbar.Children.Add(_dumpSetupButton);
            _copyStatus = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 4) };
            _copyStatus.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            toolbar.Children.Add(_copyStatus);
            Grid.SetRow(toolbar, 0);
            root.Children.Add(toolbar);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MinWidth = 200 });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star), MinWidth = 200 });

            _list = new ListBox { DisplayMemberPath = "Display", SelectionMode = SelectionMode.Extended };
            ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
            _list.SelectionChanged += (s, e) => ShowSelected();
            Grid.SetColumn(_list, 0);
            body.Children.Add(_list);

            var splitter = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent };
            Grid.SetColumn(splitter, 1);
            body.Children.Add(splitter);

            _detail = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new FontFamily("Consolas, D2Coding, Malgun Gothic"),
                FontSize = 12,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            Grid.SetColumn(_detail, 2);
            body.Children.Add(_detail);
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            _status = new TextBlock { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
            _status.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            Grid.SetRow(_status, 2);
            root.Children.Add(_status);

            Content = root;
            Loaded += async (s, e) => await ReloadAsync();
        }

        private static Button MakeButton(string text, RoutedEventHandler click)
        {
            var button = new Button { Content = text, MinWidth = 80, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 4) };
            button.Click += click;
            return button;
        }

        private async Task ReloadAsync()
        {
            _status.Text = "읽는 중…";
            _list.ItemsSource = null;
            _detail.Text = string.Empty;
            var notes = new List<string>();
            var entries = await Task.Run(() => Collect(notes));
            _entries = entries;
            _list.ItemsSource = _entries;
            if (_entries.Count > 0)
                _list.SelectedIndex = 0;

            var dumpFolder = ConfiguredDumpFolder();
            _dumpSetupButton.Visibility = dumpFolder == null ? Visibility.Visible : Visibility.Collapsed;
            var status = new StringBuilder();
            status.AppendFormat("항목 {0}개 (이벤트 뷰어는 최근 {1}일).  크래시 로그: {2}", _entries.Count, EventDays, CrashLog.LogPath);
            status.Append(dumpFolder == null
                ? "\n덤프 보존 설정 없음 — 훅이 기록하지 못한 크래시는 스택을 볼 수 없습니다. '덤프 보존 설정 명령 복사'로 설정하세요."
                : "\n덤프 보존 폴더: " + dumpFolder);
            foreach (var note in notes)
                status.Append('\n').Append(note);
            _status.Text = status.ToString();
        }

        private static List<CrashReportEntry> Collect(List<string> notes)
        {
            var all = new List<CrashReportEntry>();
            all.AddRange(ReadFileEntries(CrashLog.LogPath, CrashReportParser.ParseCrashLog, notes));
            all.AddRange(ReadFileEntries(PluginManager.LogPath, CrashReportParser.ParsePluginLog, notes));
            all.AddRange(ReadEventEntries(notes));
            all.AddRange(ReadDumpEntries(notes));
            return CrashReportParser.SortNewestFirst(all);
        }

        private static IEnumerable<CrashReportEntry> ReadFileEntries(string path, Func<string, List<CrashReportEntry>> parse, List<string> notes)
        {
            try
            {
                var result = new List<CrashReportEntry>();
                if (File.Exists(path))
                    result.AddRange(parse(ReadShared(path)));
                var old = path + ".old";
                if (File.Exists(old))
                    result.AddRange(parse(ReadShared(old)));
                return result;
            }
            catch (Exception ex)
            {
                notes.Add(Path.GetFileName(path) + " 읽기 실패: " + ex.Message);
                return Enumerable.Empty<CrashReportEntry>();
            }
        }

        private static string ReadShared(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                return reader.ReadToEnd();
        }

        /// <summary>Application 로그의 Folderss.exe 관련 Application Error(1000)·WER(1001)·.NET Runtime(1026 등).</summary>
        private static IEnumerable<CrashReportEntry> ReadEventEntries(List<string> notes)
        {
            var result = new List<CrashReportEntry>();
            var ms = (long)TimeSpan.FromDays(EventDays).TotalMilliseconds;
            var xpath = "*[System[Provider[@Name='Application Error' or @Name='Windows Error Reporting' or @Name='.NET Runtime'] and TimeCreated[timediff(@SystemTime) <= " + ms + "]]]";
            try
            {
                var query = new EventLogQuery("Application", PathType.LogName, xpath) { ReverseDirection = true };
                using (var reader = new EventLogReader(query))
                {
                    var scanned = 0;
                    for (var record = reader.ReadEvent(); record != null && scanned < MaxEventsScanned; record = reader.ReadEvent(), scanned++)
                    {
                        using (record)
                        {
                            string message;
                            try { message = record.FormatDescription(); }
                            catch (EventLogException) { message = null; }
                            if (string.IsNullOrEmpty(message) || message.IndexOf("Folderss", StringComparison.OrdinalIgnoreCase) < 0)
                                continue;
                            result.Add(new CrashReportEntry
                            {
                                Time = record.TimeCreated ?? DateTime.MinValue,
                                Source = "이벤트 " + record.ProviderName + " " + record.Id,
                                Summary = EventSummary(record.ProviderName, message),
                                Detail = message,
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                notes.Add("이벤트 뷰어 읽기 실패: " + ex.Message);
            }
            return result;
        }

        private static string EventSummary(string provider, string message)
        {
            var lines = message.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (provider == ".NET Runtime")
            {
                // 1026: "Exception Info: System.X: 메시지" 줄이 원인이다.
                var info = lines.FirstOrDefault(l => l.StartsWith("Exception Info:", StringComparison.OrdinalIgnoreCase));
                return CrashReportParser.Shorten(info ?? lines.FirstOrDefault());
            }
            // Application Error·WER: 예외 코드와 오류 모듈.
            var code = lines.FirstOrDefault(l => l.Contains("0xe0") || l.StartsWith("예외 코드") || l.StartsWith("Exception code") || l.StartsWith("P7:"));
            var module = lines.FirstOrDefault(l => l.StartsWith("오류 있는 모듈 이름") || l.StartsWith("Faulting module name") || l.StartsWith("P4:"));
            var head = provider == "Windows Error Reporting" ? "WER 보고(" + (lines.FirstOrDefault(l => l.Contains("APPCRASH")) ?? "") + ")" : "응용 프로그램 오류";
            return CrashReportParser.Shorten(string.Join(" · ", new[] { head, module, code }.Where(s => !string.IsNullOrEmpty(s))));
        }

        /// <summary>보존된 크래시 덤프(.dmp) 파일 — 설정한 폴더와 Windows 기본 폴더(%LOCALAPPDATA%\CrashDumps).</summary>
        private static IEnumerable<CrashReportEntry> ReadDumpEntries(List<string> notes)
        {
            var result = new List<CrashReportEntry>();
            var folders = new[] { ConfiguredDumpFolder(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps") }
                .Where(f => !string.IsNullOrEmpty(f)).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in folders)
            {
                try
                {
                    if (!Directory.Exists(folder))
                        continue;
                    foreach (var file in new DirectoryInfo(folder).GetFiles("Folderss.exe*.dmp"))
                    {
                        result.Add(new CrashReportEntry
                        {
                            Time = file.LastWriteTime,
                            Source = "덤프 파일",
                            Summary = string.Format("{0} ({1:N0} KB)", file.Name, file.Length / 1024),
                            Detail = file.FullName + Environment.NewLine + Environment.NewLine +
                                     "분석: dotnet tool install -g dotnet-dump 후" + Environment.NewLine +
                                     "  dotnet-dump analyze \"" + file.FullName + "\"" + Environment.NewLine +
                                     "  > pe -nested   (예외와 스택)" + Environment.NewLine +
                                     "  > clrstack -all",
                        });
                    }
                }
                catch (Exception ex)
                {
                    notes.Add("덤프 폴더 읽기 실패(" + folder + "): " + ex.Message);
                }
            }
            return result;
        }

        /// <summary>WER LocalDumps 설정 폴더(없으면 null). 키만 있고 폴더 값이 없으면 Windows 기본 폴더.</summary>
        private static string ConfiguredDumpFolder()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(LocalDumpsKey))
                {
                    if (key == null)
                        return null;
                    var folder = key.GetValue("DumpFolder") as string;
                    return string.IsNullOrWhiteSpace(folder)
                        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps")
                        : Environment.ExpandEnvironmentVariables(folder);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void ShowSelected()
        {
            var selected = SelectedEntries();
            _detail.Text = selected.Count == 1
                ? selected[0].ToClipboardText()
                : CrashReportParser.JoinForClipboard(selected);
            _detail.ScrollToHome();
        }

        private List<CrashReportEntry> SelectedEntries()
        {
            // 목록 순서(최신 위) 그대로.
            var set = new HashSet<CrashReportEntry>(_list.SelectedItems.Cast<CrashReportEntry>());
            return _entries.Where(set.Contains).ToList();
        }

        private void CopySelected()
        {
            var selected = SelectedEntries();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "복사할 항목을 선택하세요.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            // 상세 창에서 일부를 드래그로 골랐으면 그 부분만.
            var text = _detail.IsKeyboardFocusWithin && _detail.SelectionLength > 0
                ? _detail.SelectedText
                : CrashReportParser.JoinForClipboard(selected);
            Copy(text);
        }

        private void CopyAll()
        {
            if (_entries.Count == 0)
                return;
            Copy(CrashReportParser.JoinForClipboard(_entries));
        }

        private void Copy(string text)
        {
            if (ClipboardService.TrySetDataObject(text))
                _copyStatus.Text = string.Format("복사했습니다 ({0:N0}자, {1:HH:mm:ss})", text.Length, DateTime.Now);
        }

        private void CopyDumpSetupCommand()
        {
            var command = string.Join(Environment.NewLine, new[]
            {
                "# 관리자 PowerShell에서 실행 — Folderss.exe 크래시 때 덤프를 지우지 않고 남깁니다(최근 5개).",
                "$k='HKLM:\\" + LocalDumpsKey + "'",
                "New-Item $k -Force | Out-Null",
                "New-ItemProperty $k -Name DumpFolder -Value '" + Path.Combine(Path.GetDirectoryName(CrashLog.LogPath), "dumps") + "' -PropertyType ExpandString -Force",
                "New-ItemProperty $k -Name DumpType -Value 2 -PropertyType DWord -Force",
                "New-ItemProperty $k -Name DumpCount -Value 5 -PropertyType DWord -Force",
            });
            Copy(command);
        }

        private void OpenFolder(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "폴더를 열지 못했습니다: " + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EasyWindowsTerminalControl;

namespace Folderss.Diagnose
{
    public partial class MainWindow : Window
    {
        // conpty.dll 로드 검사용 P/Invoke 정의
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibraryW(string lpszLib);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hLibModule);

        public MainWindow()
        {
            InitializeComponent();

            // EasyWindowsTerminalControl 및 Microsoft.Terminal 내부에서 터지는 숨은 예외들을 100% 포착
            AppDomain.CurrentDomain.FirstChanceException += (s, ev) =>
            {
                var src = ev.Exception.Source ?? "";
                if (src.Contains("Terminal") || src.Contains("ConPTY") || src.Contains("Easy"))
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        Log($"[FirstChanceException] Source: {src}, Message: {ev.Exception.Message}");
                        if (ev.Exception.InnerException != null)
                        {
                            Log($"  -> Inner Exception: {ev.Exception.InnerException.Message}");
                        }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            };

            Log("==== Folderss ConPTY 터미널 진단 시작 ====");
            Log($"OS Version: {Environment.OSVersion}");
            Log($".NET Version: {Environment.Version}");
            Log($"Process Architecture: {(Environment.Is64BitProcess ? "x64" : "x86")}");
            Log($"Current Directory: {Environment.CurrentDirectory}");

            // 1. conpty.dll 물리적 로드 검사
            CheckConPtyDll();

            // 2. DependencyPropertyDescriptor를 통해 ConPTYTerm 프로퍼티 할당 감시
            try
            {
                var dpd = DependencyPropertyDescriptor.FromProperty(
                    EasyTerminalControl.ConPTYTermProperty,
                    typeof(EasyTerminalControl));
                if (dpd != null)
                {
                    dpd.AddValueChanged(TerminalControl, (s, e) =>
                    {
                        var pty = TerminalControl.ConPTYTerm;
                        Log($"[DP Change] ConPTYTerm 인스턴스가 갱신됨: {(pty != null ? "NOT NULL" : "NULL")}");
                        if (pty != null)
                        {
                            SubscribePtyEvents(pty);
                        }
                    });
                    Log("[Success] DependencyPropertyDescriptor 감시 등록 완료.");
                }
                else
                {
                    Log("[Fail] ConPTYTermProperty Descriptor를 찾지 못했습니다.");
                }
            }
            catch (Exception ex)
            {
                Log($"[Exception] Descriptor 등록 오류: {ex.Message}");
            }

            // 3. EasyTerminalControl 기본 속성 검사
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Log("MainWindow Loaded 완료.");
            
            // 초기 콤보박스 선택 셸로 설정 로드
            try
            {
                var item = ShellSelect.SelectedItem as ComboBoxItem;
                if (item != null)
                {
                    var cmd = item.Tag?.ToString();
                    Log($"[Init] StartupCommandLine 초기 설정 시도: {cmd}");
                    TerminalControl.StartupCommandLine = cmd;
                }
            }
            catch (Exception ex)
            {
                Log($"[Exception] 초기 로딩 실패: {ex.Message}");
            }
        }

        private void CheckConPtyDll()
        {
            // conpty.dll은 일반적으로 exe 옆 runtimes\win10-x64\native\conpty.dll 에 위치합니다.
            string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtimes", "win10-x64", "native", "conpty.dll");
            Log($"검사 대상 conpty.dll 로컬 경로: {localPath}");
            Log($"경로 파일 존재 여부: {File.Exists(localPath)}");

            IntPtr hLib = LoadLibraryW(localPath);
            if (hLib != IntPtr.Zero)
            {
                Log($"[Success] conpty.dll 로드 성공! 핸들 주소: {hLib}");
                FreeLibrary(hLib);
            }
            else
            {
                int err = Marshal.GetLastWin32Error();
                Log($"[Fail] conpty.dll 로드 실패! Win32 Error Code: {err} (0x{err:X})");
                Log("도움말: 에러 126은 모듈을 찾을 수 없거나 종속 DLL(C++ Runtime 등)이 누락되었음을 의미합니다.");
            }
        }

        private void SubscribePtyEvents(TermPTY pty)
        {
            Log("새로운 TermPTY 이벤트 구독 시작...");
            
            pty.TermReady += (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    Log("== [Event] TermReady: 셸 프로세스 가상 콘솔 준비 완료! ==");
                    StatusDot.Fill = Brushes.LimeGreen;
                    StatusLabel.Text = "실행 중 (준비됨)";

                    // 프로세스 상태 검사
                    try
                    {
                        var proc = pty.Process;
                        if (proc != null)
                        {
                            var pidProp = proc.GetType().GetProperty("Pid");
                            var pidVal = pidProp != null ? pidProp.GetValue(proc) : "N/A";
                            Log($"[ProcInfo] Type: {proc.GetType().Name}, Pid: {pidVal}, HasExited: {proc.HasExited}");
                        }
                        else
                        {
                            Log("[ProcInfo] 프로세스 인스턴스가 NULL입니다.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[ProcInfo Error] 조회 오류: {ex.Message}");
                    }
                });
            };

            pty.TerminalOutput += (s, e) =>
            {
                // 터미널에서 문자열 출력이 들어오는 경우
                // 너무 많을 수 있으므로 이벤트 도달 사실 및 글자 수만 간략 로깅
                Dispatcher.Invoke(() =>
                {
                    // Log($"[Output Stream] 수신 문자 수: {e.Text?.Length}");
                });
            };

            Log("TermPTY 이벤트 구독이 완료되었습니다.");
        }

        private async void Restart_Click(object sender, RoutedEventArgs e)
        {
            var item = ShellSelect.SelectedItem as ComboBoxItem;
            if (item == null) return;

            string baseCmd = item.Tag?.ToString() ?? "";
            Log($"--- [Restart Click] 대상 CommandLine: {baseCmd} ---");

            StatusDot.Fill = Brushes.Orange;
            StatusLabel.Text = "재시작 중...";

            try
            {
                TerminalControl.StartupCommandLine = baseCmd;
                Log($"StartupCommandLine 속성 변경 완료. RestartTerm(null, true) 실행...");
                
                await TerminalControl.RestartTerm(null, true);
                
                Log("[Success] RestartTerm 태스크가 완료되었습니다.");
            }
            catch (Exception ex)
            {
                Log($"[Exception] 터미널 재시작 실패: {ex.Message}");
                Log($"Stack Trace:\n{ex.StackTrace}");
                StatusDot.Fill = Brushes.Red;
                StatusLabel.Text = "에러 발생";
            }
        }

        private void SendCd_Click(object sender, RoutedEventArgs e)
        {
            var pty = TerminalControl.ConPTYTerm;
            if (pty == null)
            {
                Log("[Warn] 현재 활성화된 TermPTY 인스턴스가 존재하지 않습니다.");
                return;
            }

            var dir = DirInput.Text;
            var item = ShellSelect.SelectedItem as ComboBoxItem;
            bool isCmd = item != null && item.Content?.ToString()?.Contains("cmd.exe") == true;

            string cmd;
            if (isCmd)
            {
                cmd = $"cd /d \"{dir}\"\r\n";
            }
            else
            {
                cmd = $"Set-Location -LiteralPath '{dir.Replace("'", "''")}'\r\n";
            }

            Log($"[Command Send] 인풋 텍스트 전송 시도: {cmd.TrimEnd()}");
            try
            {
                pty.WriteToTerm(cmd);
                Log("[Success] 텍스트가 정상 전송되었습니다.");
            }
            catch (Exception ex)
            {
                Log($"[Exception] 텍스트 전송 실패: {ex.Message}");
            }
        }

        private void Log(string message)
        {
            var time = DateTime.Now.ToString("HH:mm:ss.fff");
            LogBox.AppendText($"[{time}] {message}\n");
            LogBox.ScrollToEnd();
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            LogBox.Clear();
        }

        private void SaveLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"diagnose_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                File.WriteAllText(logPath, LogBox.Text, Encoding.UTF8);
                MessageBox.Show($"로그가 정상적으로 저장되었습니다:\n{logPath}", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"로그 파일 저장 실패: {ex.Message}", "에러", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
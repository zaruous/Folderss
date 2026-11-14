using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Folderss.Services
{
    /// <summary>
    /// 본체 크래시 로그(%LOCALAPPDATA%\Folderss\crash-log.txt). 플러그인 여부와 관계없이 처리되지 않은 예외의 전체 스택을 남긴다.
    /// 이벤트 뷰어(.NET Runtime 1026)·WER 덤프는 남지 않거나 곧 지워질 수 있어, 앱이 종료되기 직전에 직접 기록한다.
    /// 기록 실패는 원래 예외를 가리지 않도록 삼킨다(종료 경로라 알릴 방법도 없다).
    /// </summary>
    public static class CrashLog
    {
        private const long MaxBytes = 5 * 1024 * 1024;
        private static readonly object Gate = new object();
        private static Exception _lastLogged;

        public static string LogPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Folderss", "crash-log.txt");
            }
        }

        /// <summary>앱 시작 맨 처음에 부른다. AppDomain 종료 훅·관찰되지 않은 Task 예외를 건다.</summary>
        public static void Install()
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
                Write(args.IsTerminating ? "처리되지 않은 예외로 종료" : "처리되지 않은 예외",
                    args.ExceptionObject as Exception, args.ExceptionObject);
            TaskScheduler.UnobservedTaskException += (sender, args) =>
                Write("관찰되지 않은 Task 예외(앱은 계속 실행)", args.Exception, null);

            // 기록에 쓰는 어셈블리(스택 줄 번호용 PDB 읽기 등)를 한가할 때 미리 불러 둔다.
            // 실행 중 .NET 업데이트로 런타임 폴더가 지워진 뒤에는 새로 불러오지 못해 기록 자체가 실패한다(07:57 크래시).
            Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                try
                {
                    Build("preload", new InvalidOperationException(), null);
                    using (var stream = new FileStream(Path.GetTempFileName(), FileMode.Open, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose | FileOptions.WriteThrough))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                        writer.Write(string.Empty);
                }
                catch (Exception)
                {
                }
            }));
        }

        /// <summary>UI 스레드 예외를 아무도 처리하지 않을 때(곧 종료) App.DispatcherUnhandledException에서 부른다.</summary>
        public static void WriteDispatcherException(DispatcherUnhandledExceptionEventArgs args)
        {
            Write("UI 스레드 처리되지 않은 예외(종료됨)", args.Exception, null);
        }

        public static void Write(string title, Exception exception, object rawObject)
        {
            try
            {
                lock (Gate)
                {
                    // UI 스레드 예외는 Dispatcher 처리기와 AppDomain 종료 훅에 두 번 온다 — 같은 예외는 한 번만.
                    if (exception != null && ReferenceEquals(exception, _lastLogged))
                        return;
                    _lastLogged = exception;

                    var text = Build(title, exception, rawObject);
                    var path = LogPath;
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    RotateIfLarge(path);
                    using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.WriteThrough))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                        writer.Write(text);
                }
            }
            catch (Exception)
            {
                // 종료 경로에서 기록 실패가 원래 예외를 덮으면 안 된다.
            }
        }

        internal static string Build(string title, Exception exception, object rawObject)
        {
            var sb = new StringBuilder();
            sb.AppendLine("==================================================================");
            sb.AppendFormat("[{0:yyyy-MM-dd HH:mm:ss.fff}] {1}", DateTime.Now, title).AppendLine();
            sb.AppendFormat("버전: {0}  pid: {1}  CLR: {2}  OS: {3}  64bit: {4}",
                typeof(CrashLog).Assembly.GetName().Version, Environment.ProcessId, Environment.Version,
                Environment.OSVersion.VersionString, Environment.Is64BitProcess).AppendLine();
            var thread = Thread.CurrentThread;
            sb.AppendFormat("스레드: {0} (id {1}, UI {2})", thread.Name ?? "(이름 없음)", thread.ManagedThreadId,
                Application.Current != null && Application.Current.Dispatcher.Thread == thread).AppendLine();
            sb.AppendFormat("포커스: {0}", DescribeFocus()).AppendLine();
            sb.AppendLine();

            if (exception == null)
            {
                sb.AppendLine("예외 객체: " + Convert.ToString(rawObject));
            }
            else
            {
                // ToString은 내부 예외까지 포함하지만, 종류·메시지·Data를 단계별로 한 번 더 적어 읽기 쉽게 한다.
                var depth = 0;
                for (var e = exception; e != null; e = e.InnerException, depth++)
                {
                    sb.AppendFormat("[{0}] {1}: {2}", depth, e.GetType().FullName, e.Message).AppendLine();
                    if (e.HResult != 0)
                        sb.AppendFormat("    HResult: 0x{0:X8}", e.HResult).AppendLine();
                    foreach (DictionaryEntry entry in e.Data)
                        sb.AppendFormat("    Data[{0}] = {1}", entry.Key, entry.Value).AppendLine();
                }
                sb.AppendLine();
                sb.AppendLine(exception.ToString());
            }

            sb.AppendLine();
            sb.AppendLine("기록 시점 호출 스택:");
            sb.AppendLine(new StackTrace(1, true).ToString());
            sb.AppendLine();
            return sb.ToString();
        }

        private static string DescribeFocus()
        {
            try
            {
                var app = Application.Current;
                if (app == null || !app.Dispatcher.CheckAccess())
                    return "(UI 스레드 아님)";
                var focused = System.Windows.Input.Keyboard.FocusedElement;
                return focused == null ? "(없음)" : focused.GetType().FullName;
            }
            catch (Exception)
            {
                return "(알 수 없음)";
            }
        }

        private static void RotateIfLarge(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < MaxBytes)
                return;
            var old = path + ".old";
            File.Delete(old);
            File.Move(path, old);
        }
    }
}

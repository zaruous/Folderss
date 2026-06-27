using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Folderss.Services
{
    public enum ConsoleShellKind
    {
        WindowsPowerShell,
        PowerShell7,
        CommandPrompt
    }

    public sealed class ConsoleShellInfo
    {
        public ConsoleShellKind Kind { get; set; }
        public string DisplayName { get; set; }
        public string FileName { get; set; }
        public string Arguments { get; set; }
    }

    public sealed class ConsoleOutputEventArgs : EventArgs
    {
        public ConsoleOutputEventArgs(string text, bool isError)
        {
            Text = text;
            IsError = isError;
        }

        public string Text { get; private set; }
        public bool IsError { get; private set; }
    }

    public sealed class ConsoleSessionService : IDisposable
    {
        private Process _process;
        private bool _disposed;

        public event EventHandler<ConsoleOutputEventArgs> OutputReceived;
        public event EventHandler Exited;
        public event EventHandler StateChanged;

        public bool IsRunning
        {
            get { return _process != null && !_process.HasExited; }
        }

        public ConsoleShellKind CurrentShellKind { get; private set; }
        public string WorkingDirectory { get; private set; }

        public static IReadOnlyList<ConsoleShellInfo> GetAvailableShells()
        {
            var shells = new List<ConsoleShellInfo>
            {
                new ConsoleShellInfo
                {
                    Kind = ConsoleShellKind.WindowsPowerShell,
                    DisplayName = "Windows PowerShell",
                    FileName = "powershell.exe",
                    Arguments = "-NoLogo -NoProfile -NoExit"
                }
            };

            var pwsh = FindOnPath("pwsh.exe");
            if (!string.IsNullOrEmpty(pwsh))
            {
                shells.Add(new ConsoleShellInfo
                {
                    Kind = ConsoleShellKind.PowerShell7,
                    DisplayName = "PowerShell 7",
                    FileName = pwsh,
                    Arguments = "-NoLogo -NoProfile -NoExit"
                });
            }

            shells.Add(new ConsoleShellInfo
            {
                Kind = ConsoleShellKind.CommandPrompt,
                DisplayName = "명령 프롬프트",
                FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
                Arguments = "/Q /K"
            });

            return shells;
        }

        public static void LaunchExternalTerminal(ConsoleShellKind shellKind, string workingDirectory, string command = null)
        {
            var directory = ResolveWorkingDirectory(workingDirectory);
            var shell = GetAvailableShells().FirstOrDefault(s => s.Kind == shellKind)
                ?? GetAvailableShells().First();

            var startInfo = new ProcessStartInfo
            {
                FileName = shell.FileName,
                WorkingDirectory = directory,
                UseShellExecute = true
            };

            if (shell.Kind == ConsoleShellKind.CommandPrompt)
            {
                startInfo.Arguments = string.IsNullOrWhiteSpace(command)
                    ? "/K"
                    : "/K " + QuoteCmdCommand("cd /d " + QuoteCmdPath(directory) + " && " + command);
            }
            else
            {
                startInfo.Arguments = string.IsNullOrWhiteSpace(command)
                    ? "-NoExit -NoProfile"
                    : "-NoExit -NoProfile -Command " + QuoteProcessArgument(
                        "Set-Location -LiteralPath '" + directory.Replace("'", "''") + "'; " + command);
            }

            Process.Start(startInfo);
        }

        public void Start(ConsoleShellKind shellKind, string workingDirectory)
        {
            ThrowIfDisposed();
            Stop();

            var shell = GetAvailableShells().FirstOrDefault(s => s.Kind == shellKind)
                ?? GetAvailableShells().First();
            var directory = ResolveWorkingDirectory(workingDirectory);

            var startInfo = new ProcessStartInfo
            {
                FileName = shell.FileName,
                Arguments = shell.Arguments,
                WorkingDirectory = directory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };
            process.OutputDataReceived += Process_OutputDataReceived;
            process.ErrorDataReceived += Process_ErrorDataReceived;
            process.Exited += Process_Exited;

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            _process = process;
            CurrentShellKind = shell.Kind;
            WorkingDirectory = directory;
            OnStateChanged();
            OnOutputReceived(shell.DisplayName + " started: " + directory, false);
        }

        public void Restart(ConsoleShellKind shellKind, string workingDirectory)
        {
            Start(shellKind, workingDirectory);
        }

        public void SendCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command) || !IsRunning)
                return;

            try
            {
                _process.StandardInput.WriteLine(command);
                _process.StandardInput.Flush();
            }
            catch (Exception ex)
            {
                OnOutputReceived(ex.Message, true);
            }
        }

        public void ChangeDirectory(string path)
        {
            var directory = ResolveWorkingDirectory(path);
            if (CurrentShellKind == ConsoleShellKind.CommandPrompt)
                SendCommand("cd /d \"" + directory.Replace("\"", "\\\"") + "\"");
            else
                SendCommand("Set-Location -LiteralPath '" + directory.Replace("'", "''") + "'");

            WorkingDirectory = directory;
            OnStateChanged();
        }

        public void Stop()
        {
            var process = _process;
            if (process == null)
                return;

            _process = null;

            try
            {
                process.OutputDataReceived -= Process_OutputDataReceived;
                process.ErrorDataReceived -= Process_ErrorDataReceived;
                process.Exited -= Process_Exited;

                if (!process.HasExited)
                {
                    try { process.StandardInput.WriteLine("exit"); } catch { }
                    if (!process.WaitForExit(1200))
                        process.Kill();
                }
            }
            catch { }
            finally
            {
                process.Dispose();
                OnStateChanged();
            }
        }

        public static ConsoleShellKind ParseShellKind(string value)
        {
            ConsoleShellKind kind;
            return Enum.TryParse(value, true, out kind)
                ? kind
                : ConsoleShellKind.WindowsPowerShell;
        }

        private static string ResolveWorkingDirectory(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                return path;
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        private static string FindOnPath(string fileName)
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in path.Split(Path.PathSeparator))
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(dir))
                        continue;
                    var candidate = Path.Combine(dir.Trim(), fileName);
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch { }
            }
            return null;
        }

        private static string QuoteProcessArgument(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }

        private static string QuoteCmdCommand(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }

        private static string QuoteCmdPath(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
        }

        private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
                OnOutputReceived(e.Data, false);
        }

        private void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
                OnOutputReceived(e.Data, true);
        }

        private void Process_Exited(object sender, EventArgs e)
        {
            OnOutputReceived("Console process exited.", false);
            OnStateChanged();
            var handler = Exited;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        private void OnOutputReceived(string text, bool isError)
        {
            var handler = OutputReceived;
            if (handler != null)
                handler(this, new ConsoleOutputEventArgs(text, isError));
        }

        private void OnStateChanged()
        {
            var handler = StateChanged;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Stop();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Folderss.Services
{
    public sealed class GitResult
    {
        public int ExitCode { get; set; }
        public string StdOut { get; set; } = string.Empty;
        public string StdErr { get; set; } = string.Empty;
        public string CommandLine { get; set; }
        public bool TimedOut { get; set; }
        public bool Success => !TimedOut && ExitCode == 0;
    }

    /// <summary>
    /// 설치된 git 실행 파일을 직접 부른다. 사용자 설정·인증(Git Credential Manager, ssh-agent)·hook이 콘솔과 똑같이 적용된다.
    /// - 인수는 <see cref="ProcessStartInfo.ArgumentList"/>로만 넘긴다(문자열 조합 금지 — 경로·브랜치명 따옴표/인젝션 문제 차단).
    /// - <c>GIT_TERMINAL_PROMPT=0</c>으로 터미널 입력 대기를 막고, 타임아웃·취소 시 프로세스 트리를 끝낸다.
    /// - 출력은 UTF-8로 읽고 <c>core.quotepath=false</c>로 한글 경로를 그대로 받는다.
    /// </summary>
    public static class GitCommandRunner
    {
        public static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan NetworkTimeout = TimeSpan.FromMinutes(5);
        // switch/restore는 2.23, add/restore의 --pathspec-from-file은 2.26부터다.
        public static readonly Version MinimumVersion = new Version(2, 26);

        // 여러 저장소 상태를 한꺼번에 읽을 때 프로세스를 너무 많이 띄우지 않는다.
        private static readonly SemaphoreSlim Concurrency = new SemaphoreSlim(4);
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private static string _gitPath;

        /// <summary>git 실행 파일 경로. 못 찾으면 null. PATH → Git for Windows 기본 설치 위치 순.</summary>
        public static string FindGit()
        {
            if (_gitPath != null && File.Exists(_gitPath))
                return _gitPath;

            var names = OperatingSystem.IsWindows() ? new[] { "git.exe" } : new[] { "git" };
            var candidates = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .SelectMany(dir => names.Select(name => SafeCombine(dir.Trim().Trim('"'), name)))
                .Concat(new[]
                {
                    SafeCombine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Git\cmd\git.exe"),
                    SafeCombine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Git\cmd\git.exe")
                });

            _gitPath = candidates.FirstOrDefault(path => path != null && File.Exists(path));
            return _gitPath;
        }

        private static string SafeCombine(string directory, string name)
        {
            try
            {
                return string.IsNullOrEmpty(directory) ? null : Path.Combine(directory, name);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        /// <summary>"git version 2.43.0.windows.1" → 2.43.0. 해석 못 하면 null.</summary>
        public static Version ParseVersion(string versionOutput)
        {
            var match = Regex.Match(versionOutput ?? string.Empty, @"(\d+)\.(\d+)(?:\.(\d+))?");
            if (!match.Success)
                return null;
            return new Version(
                int.Parse(match.Groups[1].Value),
                int.Parse(match.Groups[2].Value),
                match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0);
        }

        /// <param name="repository">작업 폴더(<c>git -C</c>). null이면 지정하지 않는다.</param>
        /// <param name="readOnly">조회 명령이면 true — <c>GIT_OPTIONAL_LOCKS=0</c>으로 IDE 등과 index.lock 경합을 피한다.</param>
        /// <param name="standardInput">표준 입력으로 보낼 내용(예: <c>--pathspec-from-file=-</c>).</param>
        public static async Task<GitResult> RunAsync(string repository, IEnumerable<string> arguments, TimeSpan timeout,
            CancellationToken cancellationToken, bool readOnly = false, string standardInput = null)
        {
            var git = FindGit();
            if (git == null)
                throw new FileNotFoundException("git 실행 파일을 찾을 수 없습니다. Git for Windows를 설치하고 PATH에 등록하세요.");

            var userArgs = arguments.ToList();
            var args = new List<string>();
            if (!string.IsNullOrEmpty(repository))
            {
                args.Add("-C");
                args.Add(repository);
            }
            args.AddRange(new[] { "-c", "core.quotepath=false", "-c", "color.ui=false" });
            args.AddRange(userArgs);

            var startInfo = new ProcessStartInfo(git)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = Utf8NoBom,
                StandardErrorEncoding = Utf8NoBom
            };
            foreach (var arg in args)
                startInfo.ArgumentList.Add(arg);
            startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
            // pull(병합)이 편집기를 띄워 멈추지 않게 한다.
            startInfo.Environment["GIT_MERGE_AUTOEDIT"] = "no";
            if (readOnly)
                startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";

            var result = new GitResult { CommandLine = FormatCommandLine(userArgs) };

            await Concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using (var process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    var stdoutTask = process.StandardOutput.ReadToEndAsync();
                    var stderrTask = process.StandardError.ReadToEndAsync();

                    try
                    {
                        if (standardInput != null)
                        {
                            var bytes = Utf8NoBom.GetBytes(standardInput);
                            await process.StandardInput.BaseStream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
                        }
                        process.StandardInput.Close();
                    }
                    catch (IOException)
                    {
                        // git이 입력을 다 읽기 전에 끝난 경우 — 종료 코드와 stderr로 판단한다.
                    }

                    using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        timeoutSource.CancelAfter(timeout);
                        try
                        {
                            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                            if (cancellationToken.IsCancellationRequested)
                                throw;
                            result.TimedOut = true;
                        }
                    }

                    result.StdOut = await stdoutTask.ConfigureAwait(false);
                    result.StdErr = await stderrTask.ConfigureAwait(false);
                    if (result.TimedOut)
                    {
                        result.ExitCode = -1;
                        result.StdErr += string.Format("\n시간 초과({0}초)로 중단했습니다. 인증 창이 다른 창 뒤에 떠 있거나 SSH 키 암호 입력을 기다리는 중일 수 있습니다.", (int)timeout.TotalSeconds);
                    }
                    else
                    {
                        result.ExitCode = process.ExitCode;
                    }
                }
            }
            finally
            {
                Concurrency.Release();
            }
            return result;
        }

        public static string FormatCommandLine(IEnumerable<string> args)
        {
            return "git " + string.Join(" ", args.Select(a => a.Length == 0 || a.Any(char.IsWhiteSpace) || a.Contains('"')
                ? "\"" + a.Replace("\"", "\\\"") + "\""
                : a));
        }
    }
}

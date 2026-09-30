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

        /// <summary><c>rawOutput</c>로 실행했을 때의 표준 출력 바이트(파일 내용 그대로). 그 외에는 null.</summary>
        public byte[] StdOutBytes { get; set; }
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
        private static string _configuredGitPath = string.Empty;

        /// <summary>
        /// 설정에서 지정한 git 경로. 비우면 자동 탐색. 지정했는데 파일이 없으면 <see cref="FindGit"/>가 null을 돌려준다 —
        /// 조용히 다른 git으로 대신 실행하면 사용자가 의도한 버전·설정과 다른 동작을 원인 모르게 겪게 된다.
        /// </summary>
        public static string ConfiguredGitPath
        {
            get => _configuredGitPath;
            set
            {
                var normalized = (value ?? string.Empty).Trim().Trim('"');
                if (normalized == _configuredGitPath)
                    return;
                _configuredGitPath = normalized;
                _gitPath = null;
            }
        }

        /// <summary>git 실행 파일 경로. 못 찾으면 null. 설정 경로 → PATH → Git for Windows 기본 설치 위치 순.</summary>
        public static string FindGit()
        {
            if (_configuredGitPath.Length > 0)
                return File.Exists(_configuredGitPath) ? _configuredGitPath : null;

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
        /// <param name="fallbackEncoding">지정하면 표준 출력을 바이트로 받아 UTF-8로 읽히지 않는 줄만 이 인코딩으로 해석한다(diff용).</param>
        /// <param name="throttle">false면 동시 실행 제한을 받지 않는다 — 사용자가 닫을 때까지 떠 있는 외부 비교 도구용.</param>
        /// <param name="rawOutput">true면 표준 출력을 해석하지 않고 <see cref="GitResult.StdOutBytes"/>에 담는다(<c>cat-file blob</c>용).</param>
        public static async Task<GitResult> RunAsync(string repository, IEnumerable<string> arguments, TimeSpan timeout,
            CancellationToken cancellationToken, bool readOnly = false, string standardInput = null,
            Encoding fallbackEncoding = null, bool throttle = true, bool rawOutput = false)
        {
            var git = FindGit();
            if (git == null)
            {
                throw new FileNotFoundException(_configuredGitPath.Length > 0
                    ? "설정한 git 실행 파일이 없습니다: " + _configuredGitPath + " (설정 > Git에서 경로를 확인하세요)"
                    : "git 실행 파일을 찾을 수 없습니다. Git for Windows를 설치하고 PATH에 등록하세요.");
            }

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

            if (throttle)
                await Concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using (var process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    var rawTask = rawOutput ? ReadBytesAsync(process.StandardOutput.BaseStream) : null;
                    var stdoutTask = rawOutput
                        ? Task.FromResult(string.Empty)
                        : fallbackEncoding == null
                            ? process.StandardOutput.ReadToEndAsync()
                            : ReadDecodedAsync(process.StandardOutput.BaseStream, fallbackEncoding);
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
                    if (rawTask != null)
                        result.StdOutBytes = await rawTask.ConfigureAwait(false);
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
                if (throttle)
                    Concurrency.Release();
            }
            return result;
        }

        private static async Task<string> ReadDecodedAsync(Stream stream, Encoding fallbackEncoding)
        {
            return GitTextDecoder.Decode(await ReadBytesAsync(stream).ConfigureAwait(false), fallbackEncoding);
        }

        private static async Task<byte[]> ReadBytesAsync(Stream stream)
        {
            using (var buffer = new MemoryStream())
            {
                await stream.CopyToAsync(buffer).ConfigureAwait(false);
                return buffer.ToArray();
            }
        }

        public static string FormatCommandLine(IEnumerable<string> args)
        {
            return "git " + string.Join(" ", args.Select(a => a.Length == 0 || a.Any(char.IsWhiteSpace) || a.Contains('"')
                ? "\"" + a.Replace("\"", "\\\"") + "\""
                : a));
        }
    }

    /// <summary>
    /// 파일 인코딩 판정 규칙: BOM(매직넘버)이 있으면 그 인코딩, 없으면 UTF-8.
    /// UTF-8로 읽히지 않는 줄만 선택적으로 대체 인코딩(CP949 등)으로 다시 읽는다(기본은 대체 없음).
    /// git이 내보낸 바이트(파일 내용 그대로)를 줄 단위로 해석한다. UTF-8로 올바른 줄은 UTF-8로, 아닌 줄만 대체 인코딩으로 읽는다.
    /// 한국어 Windows의 오래된 소스(CP949)가 diff에서 깨지는 문제용. 줄 구분 0x0A는 CP949 두 번째 바이트(0x41 이상)와 겹치지 않는다.
    /// </summary>
    public static class GitTextDecoder
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        static GitTextDecoder()
        {
            // .NET(Core)은 기본으로 UTF-8·ASCII 등만 제공한다. CP949 같은 코드 페이지는 공급자를 등록해야 쓸 수 있다.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        /// <summary>설정값을 실제 인코딩으로. 대체 해석이 필요 없으면 null.</summary>
        public static Encoding Resolve(GitFallbackEncoding mode)
        {
            switch (mode)
            {
                case GitFallbackEncoding.Cp949:
                    return Encoding.GetEncoding(949);
                case GitFallbackEncoding.SystemAnsi:
                    var codePage = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
                    // 시스템이 이미 UTF-8(65001)이면 대체할 것이 없다.
                    return codePage == 65001 ? null : Encoding.GetEncoding(codePage);
                default:
                    return null;
            }
        }

        /// <summary>
        /// BOM으로 인코딩을 판정한다. 없으면 null. UTF-32를 UTF-16보다 먼저 본다(FF FE 00 00).
        /// 돌려주는 인코딩은 잘못된 바이트에서 예외 대신 대체 문자를 쓴다.
        /// </summary>
        public static Encoding DetectBom(byte[] bytes, out int bomLength)
        {
            bomLength = 0;
            if (bytes == null)
                return null;
            if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0)
            {
                bomLength = 4;
                return new UTF32Encoding(false, true);
            }
            if (bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xFE && bytes[3] == 0xFF)
            {
                bomLength = 4;
                return new UTF32Encoding(true, true);
            }
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                bomLength = 3;
                return new UTF8Encoding(true);
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                bomLength = 2;
                return new UnicodeEncoding(false, true);
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                bomLength = 2;
                return new UnicodeEncoding(true, true);
            }
            return null;
        }

        /// <summary>
        /// UTF-16/UTF-32 BOM인지. 이런 파일은 NUL 바이트 때문에 git이 바이너리로 보고 줄 diff를 만들지 않는다.
        /// </summary>
        public static bool HasWideBom(byte[] bytes)
        {
            var encoding = DetectBom(bytes, out _);
            return encoding is UnicodeEncoding || encoding is UTF32Encoding;
        }

        /// <summary>파일 내용 전체를 규칙대로 읽는다: BOM이 있으면 그 인코딩(BOM 제외), 없으면 UTF-8(+선택적 대체).</summary>
        public static string DecodeFile(byte[] bytes, Encoding fallbackEncoding)
        {
            var encoding = DetectBom(bytes, out var bomLength);
            if (encoding == null)
                return Decode(bytes, fallbackEncoding);
            // BOM 인코딩은 예외 없이 잘못된 바이트를 대체 문자로 바꾸는 인스턴스다(DetectBom 참고).
            return encoding.GetString(bytes, bomLength, bytes.Length - bomLength);
        }

        public static string DescribeBom(byte[] bytes)
        {
            var encoding = DetectBom(bytes, out _);
            switch (encoding)
            {
                case UTF32Encoding _: return bytes[0] == 0xFF ? "UTF-32 LE" : "UTF-32 BE";
                case UnicodeEncoding _: return bytes[0] == 0xFF ? "UTF-16 LE" : "UTF-16 BE";
                case UTF8Encoding _: return "UTF-8 (BOM)";
                default: return "UTF-8";
            }
        }

        public static string Decode(byte[] bytes, Encoding fallbackEncoding)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;

            try
            {
                return StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                if (fallbackEncoding == null)
                    return Encoding.UTF8.GetString(bytes);
            }

            var builder = new StringBuilder(bytes.Length);
            var start = 0;
            while (start < bytes.Length)
            {
                var end = Array.IndexOf(bytes, (byte)'\n', start);
                var length = (end < 0 ? bytes.Length : end + 1) - start;
                try
                {
                    builder.Append(StrictUtf8.GetString(bytes, start, length));
                }
                catch (DecoderFallbackException)
                {
                    builder.Append(fallbackEncoding.GetString(bytes, start, length));
                }
                start += length;
            }
            return builder.ToString();
        }
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Folderss.Services
{
    public enum FileErrorClassification
    {
        Unknown,
        SharingOrLockViolation,
        AccessDenied,
        NotFound,
        PathTooLong,
        DirectoryNotEmpty
    }

    /// <summary>
    /// 잠금 프로세스 정보 모델.
    /// </summary>
    public sealed class ProcessLockInfo
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; }
        public string ExecutablePath { get; set; }
        public string AppTitle { get; set; }
        public DateTime? StartTimeUtc { get; set; }
        public bool IsService { get; set; }
        public string ServiceName { get; set; }
        public bool IsCritical { get; set; }
        public bool IsCurrentProcess { get; set; }
        public bool CanRequestClose { get; set; }
        public bool CanForceKill { get; set; }
        public string UnclosableReason { get; set; }

        public string DisplayName => !string.IsNullOrWhiteSpace(AppTitle) ? AppTitle : ProcessName;

        public string ProcessTypeDescription
        {
            get
            {
                if (IsCurrentProcess)
                    return "Folderss (현재 앱)";
                if (IsCritical)
                    return "시스템 핵심 프로세스";
                if (IsService)
                    return "Windows 서비스" + (!string.IsNullOrWhiteSpace(ServiceName) ? $" ({ServiceName})" : string.Empty);
                return "일반 애플리케이션";
            }
        }
    }

    /// <summary>
    /// 파일/디렉터리 잠금 검사 결과.
    /// </summary>
    public sealed class FileLockInspectionResult
    {
        public string TargetPath { get; set; }
        public bool IsDirectory { get; set; }
        public bool IsArchiveVirtualPath { get; set; }
        public DateTime InspectedAtUtc { get; set; } = DateTime.UtcNow;
        public IReadOnlyList<ProcessLockInfo> Processes { get; set; } = Array.Empty<ProcessLockInfo>();
        public IReadOnlyList<string> CandidateLockedFiles { get; set; } = Array.Empty<string>();
        public string Message { get; set; }
        public bool Success { get; set; }
        public int? ErrorCode { get; set; }
    }

    /// <summary>
    /// Windows Restart Manager를 이용한 잠긴 파일 프로세스 진단 및 안전한 프로세스 종료 서비스.
    /// </summary>
    public static class FileLockService
    {
        public const int ErrorAccessDenied = 5;
        public const int ErrorSharingViolation = 32;
        public const int ErrorLockViolation = 33;
        public const int ErrorFileNotFound = 2;
        public const int ErrorPathNotFound = 3;
        public const int ErrorDirectoryNotEmpty = 145;
        public const int ErrorFilenameExcedRange = 206; // Path too long

        private const int ErrorSuccess = 0;
        private const int ErrorMoreData = 234;
        private const int CchRmSessionKey = 32;
        private const int CchRmMaxAppName = 255;
        private const int CchRmMaxSvcName = 63;

        private static readonly HashSet<string> KnownCriticalProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "system", "system idle process", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost", "dwm"
        };

        #region P/Invoke Definitions

        [StructLayout(LayoutKind.Sequential)]
        public struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        public enum RM_APP_TYPE
        {
            RmUnknownApp = 0,
            RmMainWindow = 1,
            RmOtherWindow = 2,
            RmService = 3,
            RmExplorer = 4,
            RmConsole = 5,
            RmCritical = 1000
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxAppName + 1)]
            public string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxSvcName + 1)]
            public string strServiceShortName;
            public RM_APP_TYPE ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bRestartable;
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmStartSession(out uint pSessionHandle, uint dwSessionFlags, StringBuilder strSessionKey);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmRegisterResources(
            uint dwSessionHandle,
            uint nFiles,
            string[] rgsFilenames,
            uint nApplications,
            [In] RM_UNIQUE_PROCESS[] rgApplications,
            uint nServices,
            string[] rgsServiceNames);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmGetList(
            uint dwSessionHandle,
            out uint pnProcInfoNeeded,
            ref uint pnProcInfo,
            [In, Out] RM_PROCESS_INFO[] rgAffectedApps,
            out uint lpdwRebootReasons);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmEndSession(uint dwSessionHandle);

        #endregion

        #region Error Classification

        /// <summary>
        /// 예외에서 Win32 오류 코드를 추출한다.
        /// </summary>
        public static int? GetWin32ErrorCode(Exception ex)
        {
            if (ex == null)
                return null;

            if (ex is Win32Exception win32)
                return win32.NativeErrorCode;

            if (ex is UnauthorizedAccessException)
                return ErrorAccessDenied;

            if (ex is FileNotFoundException)
                return ErrorFileNotFound;

            if (ex is DirectoryNotFoundException)
                return ErrorPathNotFound;

            if (ex is PathTooLongException)
                return ErrorFilenameExcedRange;

            // HResult 하위 16비트가 Win32 오류 코드 (FACILITY_WIN32 = 7)
            uint hr = (uint)ex.HResult;
            if ((hr & 0xFFFF0000) == 0x80070000)
                return (int)(hr & 0xFFFF);

            if (hr > 0 && hr <= 0xFFFF)
                return (int)hr;

            return null;
        }

        /// <summary>
        /// 공유 위반(ERROR_SHARING_VIOLATION 32) 또는 잠금 위반(ERROR_LOCK_VIOLATION 33)인지 판정한다.
        /// </summary>
        public static bool IsSharingOrLockViolation(Exception ex)
        {
            var code = GetWin32ErrorCode(ex);
            return code == ErrorSharingViolation || code == ErrorLockViolation;
        }

        /// <summary>
        /// 접근 거부(ERROR_ACCESS_DENIED 5 또는 UnauthorizedAccessException)인지 판정한다.
        /// </summary>
        public static bool IsAccessDenied(Exception ex)
        {
            var code = GetWin32ErrorCode(ex);
            return code == ErrorAccessDenied;
        }

        /// <summary>
        /// 파일 예외의 원인을 분류한다.
        /// </summary>
        public static FileErrorClassification ClassifyError(Exception ex)
        {
            if (ex == null)
                return FileErrorClassification.Unknown;

            var code = GetWin32ErrorCode(ex);
            switch (code)
            {
                case ErrorSharingViolation:
                case ErrorLockViolation:
                    return FileErrorClassification.SharingOrLockViolation;
                case ErrorAccessDenied:
                    return FileErrorClassification.AccessDenied;
                case ErrorFileNotFound:
                case ErrorPathNotFound:
                    return FileErrorClassification.NotFound;
                case ErrorFilenameExcedRange:
                    return FileErrorClassification.PathTooLong;
                case ErrorDirectoryNotEmpty:
                    return FileErrorClassification.DirectoryNotEmpty;
                default:
                    return FileErrorClassification.Unknown;
            }
        }

        /// <summary>
        /// 분류에 따른 사용자 안내 문구를 반환한다.
        /// </summary>
        public static string GetClassificationDescription(FileErrorClassification classification)
        {
            switch (classification)
            {
                case FileErrorClassification.SharingOrLockViolation:
                    return "다른 프로세스가 파일을 열고 있어 작업할 수 없습니다 (공유/잠금 위반).";
                case FileErrorClassification.AccessDenied:
                    return "파일이나 폴더에 접근할 권한이 없습니다 (접근 거부). 잠긴 파일이 아닐 수 있습니다.";
                case FileErrorClassification.NotFound:
                    return "지정한 파일 또는 경로를 찾을 수 없습니다.";
                case FileErrorClassification.PathTooLong:
                    return "경로 길이가 시스템 허용 한도를 초과했습니다.";
                case FileErrorClassification.DirectoryNotEmpty:
                    return "디렉터리가 비어 있지 않습니다.";
                default:
                    return "작업 중 오류가 발생했습니다.";
            }
        }

        #endregion

        #region Process Inspection

        /// <summary>
        /// 지정한 파일 또는 폴더를 점유 중인 프로세스 목록을 비동기로 조회한다.
        /// </summary>
        public static Task<FileLockInspectionResult> InspectAsync(string path, CancellationToken token = default)
        {
            return Task.Run(() => Inspect(path, token), token);
        }

        /// <summary>
        /// 지정한 파일 또는 폴더를 점유 중인 프로세스 목록을 조회한다.
        /// </summary>
        public static FileLockInspectionResult Inspect(string path, CancellationToken token = default)
        {
            var result = new FileLockInspectionResult
            {
                TargetPath = path,
                InspectedAtUtc = DateTime.UtcNow
            };

            if (string.IsNullOrWhiteSpace(path))
            {
                result.Success = false;
                result.Message = "경로가 지정되지 않았습니다.";
                return result;
            }

            // zip 가상 경로 판별 (.zip\ 뒤의 경로이거나 zip 아카이브 표시)
            if (IsArchiveVirtualPath(path))
            {
                result.IsArchiveVirtualPath = true;
                result.Success = false;
                result.Message = "압축 파일(.zip) 내부의 가상 경로는 Windows Restart Manager 프로세스 조회를 지원하지 않습니다.";
                return result;
            }

            if (!OperatingSystem.IsWindows())
            {
                result.Success = false;
                result.Message = "Windows 이외의 환경에서는 Restart Manager를 지원하지 않습니다.";
                return result;
            }

            try
            {
                if (Directory.Exists(path))
                {
                    result.IsDirectory = true;
                    // 폴더는 Restart Manager에 직접 등록할 수 없다.
                    // 대신 하위 잠긴 파일을 탐색한다.
                    var candidates = FindLockedFilesInDirectory(path, 10);
                    result.CandidateLockedFiles = candidates;

                    if (candidates.Count == 0)
                    {
                        result.Success = true;
                        result.Message = "폴더 자체는 Restart Manager에 등록할 수 없습니다. 하위 파일 중 직접 잠긴 파일이 감지되지 않았습니다. (하위 파일의 권한 부족이거나 하위 폴더 잠금일 수 있습니다)";
                        return result;
                    }

                    // 탐색 범위에서 찾은 모든 후보를 한 세션에 등록한다.
                    var (procs, err) = QueryRestartManager(candidates.ToArray());
                    result.Processes = procs;
                    result.ErrorCode = err;
                    result.Success = err == ErrorSuccess;
                    result.Message = procs.Count > 0
                        ? $"폴더 하위 후보 파일 {candidates.Count}개를 사용하는 프로세스 {procs.Count}개가 감지되었습니다. (최대 10개 파일, 깊이 8까지 조회)"
                        : $"폴더 하위 후보 파일 {candidates.Count}개의 점유 프로세스가 감지되지 않았습니다 (오류 코드: {err}, 최대 10개 파일, 깊이 8까지 조회).";
                    return result;
                }

                if (!File.Exists(path))
                {
                    result.Success = false;
                    result.Message = "파일이 존재하지 않습니다: " + path;
                    return result;
                }

                var (fileProcs, fileErr) = QueryRestartManager(new[] { path });
                result.Processes = fileProcs;
                result.ErrorCode = fileErr;
                result.Success = fileErr == ErrorSuccess;

                if (fileErr == ErrorSuccess)
                {
                    result.Message = fileProcs.Count > 0
                        ? $"이 파일을 사용 중인 프로세스 {fileProcs.Count}개가 감지되었습니다."
                        : "현재 이 파일을 잠그고 있는 활성 프로세스가 감지되지 않았습니다. (다른 원인이거나 핸들이 이미 닫혔을 수 있습니다)";
                }
                else
                {
                    result.Message = $"Restart Manager 조회 실패 (Win32 오류 코드: {fileErr})";
                }

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = "프로세스 조회 중 예외 발생: " + ex.Message;
                return result;
            }
        }

        private static bool IsArchiveVirtualPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            // .zip\ 또는 .zip/ 문자열이 경로 중간에 있으면 가상 경로
            int idx = path.IndexOf(".zip", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && idx + 4 < path.Length)
            {
                char next = path[idx + 4];
                if (next == '\\' || next == '/')
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 폴더 내에서 열기 시도(FileShare.None)가 실패하는 파일들을 찾아낸다.
        /// </summary>
        public static List<string> FindLockedFilesInDirectory(string dirPath, int maxCount)
        {
            var locked = new List<string>();
            try
            {
                var options = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = true,
                    MaxRecursionDepth = 8
                };

                foreach (var file in Directory.EnumerateFiles(dirPath, "*", options))
                {
                    if (IsFileLocked(file))
                    {
                        locked.Add(file);
                        if (locked.Count >= maxCount)
                            break;
                    }
                }
            }
            catch
            {
                // 디렉터리 접근 오류 무시
            }
            return locked;
        }

        /// <summary>
        /// 파일이 다른 프로세스에 의해 배타적으로 열려 있는지 테스트한다.
        /// </summary>
        public static bool IsFileLocked(string filePath)
        {
            FileStream stream = null;
            try
            {
                // 공유 없이 쓰기/삭제 모드로 열기 시도
                stream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return false;
            }
            catch (IOException ex) when (IsSharingOrLockViolation(ex))
            {
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                stream?.Dispose();
            }
        }

        private static (List<ProcessLockInfo> Processes, int ErrorCode) QueryRestartManager(string[] files)
        {
            var list = new List<ProcessLockInfo>();
            if (!OperatingSystem.IsWindows() || files == null || files.Length == 0)
                return (list, ErrorSuccess);

            uint sessionHandle = 0;
            var sessionKey = new StringBuilder(CchRmSessionKey + 1);

            int startRes = RmStartSession(out sessionHandle, 0, sessionKey);
            if (startRes != ErrorSuccess)
                return (list, startRes);

            try
            {
                // 전체 파일 경로를 정규화하여 전달
                var fullPaths = files.Select(f => Path.GetFullPath(f)).ToArray();
                int regRes = RmRegisterResources(sessionHandle, (uint)fullPaths.Length, fullPaths, 0, null, 0, null);
                if (regRes != ErrorSuccess)
                    return (list, regRes);

                uint pnProcInfoNeeded = 0;
                uint pnProcInfo = 0;
                var affected = new RM_PROCESS_INFO[0];
                uint lpdwRebootReasons;

                // 버퍼 크기 조회를 위한 1차 호출
                int getListRes = RmGetList(sessionHandle, out pnProcInfoNeeded, ref pnProcInfo, affected, out lpdwRebootReasons);

                int retry = 0;
                while (getListRes == ErrorMoreData && retry++ < 3)
                {
                    affected = new RM_PROCESS_INFO[pnProcInfoNeeded];
                    pnProcInfo = pnProcInfoNeeded;
                    getListRes = RmGetList(sessionHandle, out pnProcInfoNeeded, ref pnProcInfo, affected, out lpdwRebootReasons);
                }

                if (getListRes != ErrorSuccess)
                    return (list, getListRes);

                for (int i = 0; i < pnProcInfo; i++)
                {
                    var evaluated = EvaluateProcess(affected[i]);
                    list.Add(evaluated);
                }

                return (list, ErrorSuccess);
            }
            finally
            {
                if (sessionHandle != 0)
                {
                    RmEndSession(sessionHandle);
                }
            }
        }

        /// <summary>
        /// RM_PROCESS_INFO 구조체로부터 안전성 및 종료 가능 여부를 평가한다.
        /// </summary>
        public static ProcessLockInfo EvaluateProcess(RM_PROCESS_INFO info)
        {
            int pid = info.Process.dwProcessId;
            var startTimeUtc = ConvertFileTimeToDateTime(info.Process.ProcessStartTime);

            var result = new ProcessLockInfo
            {
                ProcessId = pid,
                ProcessName = info.strAppName,
                AppTitle = info.strAppName,
                ServiceName = info.strServiceShortName,
                StartTimeUtc = startTimeUtc,
                IsService = info.ApplicationType == RM_APP_TYPE.RmService || !string.IsNullOrWhiteSpace(info.strServiceShortName),
                IsCritical = info.ApplicationType == RM_APP_TYPE.RmCritical || pid <= 4,
                IsCurrentProcess = pid == Environment.ProcessId
            };

            // 시스템 핵심 프로세스명 체크
            if (!string.IsNullOrWhiteSpace(result.ProcessName) && KnownCriticalProcessNames.Contains(result.ProcessName))
            {
                result.IsCritical = true;
            }

            // 추가 프로세스 정보 조회 시도
            try
            {
                using var proc = Process.GetProcessById(pid);
                if (string.IsNullOrWhiteSpace(result.ProcessName))
                    result.ProcessName = proc.ProcessName;

                if (string.IsNullOrWhiteSpace(result.AppTitle) && !string.IsNullOrWhiteSpace(proc.MainWindowTitle))
                    result.AppTitle = proc.MainWindowTitle;

                if (startTimeUtc == null)
                {
                    try { result.StartTimeUtc = proc.StartTime.ToUniversalTime(); } catch { }
                }

                try
                {
                    result.ExecutablePath = proc.MainModule?.FileName;
                }
                catch
                {
                    // 권한 부족 또는 64/32비트 프로세스 접근 불가
                }
            }
            catch (Exception ex)
            {
                // 프로세스가 이미 종료되었거나 접근 권한이 없음
                if (string.IsNullOrWhiteSpace(result.ProcessName))
                    result.ProcessName = $"PID {pid}";
                result.UnclosableReason = "프로세스 정보를 조회할 권한이 없거나 이미 종료되었습니다: " + ex.Message;
            }

            // 종료 가능 여부 및 사유 결정
            if (result.IsCurrentProcess)
            {
                result.CanRequestClose = false;
                result.CanForceKill = false;
                result.UnclosableReason = "현재 실행 중인 Folderss 파일 관리자입니다.";
            }
            else if (result.IsCritical)
            {
                result.CanRequestClose = false;
                result.CanForceKill = false;
                result.UnclosableReason = "Windows 시스템 핵심 프로세스입니다 (종료할 수 없습니다).";
            }
            else if (result.IsService)
            {
                result.CanRequestClose = false;
                result.CanForceKill = false;
                result.UnclosableReason = "Windows 서비스 프로세스입니다 (서비스 관리자에서 관리해야 합니다).";
            }
            else if (info.TSSessionId != CurrentSessionId)
            {
                result.CanRequestClose = false;
                result.CanForceKill = false;
                result.UnclosableReason = "다른 사용자 세션이거나 세션을 확인할 수 없는 프로세스입니다.";
            }
            else
            {
                // 일반 프로세스
                result.CanRequestClose = true;
                result.CanForceKill = true;
            }

            return result;
        }

        private static uint CurrentSessionId
        {
            get { using var current = Process.GetCurrentProcess(); return (uint)current.SessionId; }
        }

        public static DateTime? ConvertFileTimeToDateTime(System.Runtime.InteropServices.ComTypes.FILETIME fileTime)
        {
            long high = (long)fileTime.dwHighDateTime << 32;
            long low = (long)(uint)fileTime.dwLowDateTime;
            long combined = high | low;
            if (combined <= 0)
                return null;
            try
            {
                return DateTime.FromFileTimeUtc(combined);
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Process Termination

        /// <summary>
        /// 일반 프로세스에 정상 종료(CloseMainWindow)를 요청하고 비동기로 대기한다.
        /// 저장 확인 대화상자 처리 시간을 위해 일정 시간 기다린다.
        /// </summary>
        public static async Task<(bool Exited, string Message)> RequestCloseProcessAsync(
            ProcessLockInfo info,
            int waitTimeoutMs = 4000,
            CancellationToken cancellationToken = default)
        {
            if (info == null)
                return (false, "프로세스 정보가 없습니다.");

            if (!info.CanRequestClose)
                return (false, info.UnclosableReason ?? "종료할 수 없는 프로세스입니다.");

            Process proc = null;
            try
            {
                proc = Process.GetProcessById(info.ProcessId);
            }
            catch (ArgumentException)
            {
                return (true, "프로세스가 이미 종료되었습니다.");
            }
            catch (Exception ex)
            {
                return (false, "프로세스를 찾을 수 없습니다: " + ex.Message);
            }

            using (proc)
            {
                try
                {
                    if ((uint)proc.SessionId != CurrentSessionId)
                        return (false, "다른 사용자 세션의 프로세스는 종료할 수 없습니다.");
                }
                catch (Exception ex) { return (false, "프로세스 세션 확인 실패: " + ex.Message); }

                // PID 재사용 검증 (시작 시각 확인)
                if (info.StartTimeUtc.HasValue)
                {
                    try
                    {
                        var currentStartTimeUtc = proc.StartTime.ToUniversalTime();
                        if (Math.Abs((currentStartTimeUtc - info.StartTimeUtc.Value).TotalSeconds) > 2.0)
                        {
                            return (false, "PID가 다른 프로세스에 의해 재사용되었습니다. 안전을 위해 종료를 중단합니다.");
                        }
                    }
                    catch (Exception ex)
                    {
                        return (false, "프로세스 시작 시각을 검증할 수 없습니다: " + ex.Message);
                    }
                }

                // 정상 종료 요청
                bool closeSent = false;
                try
                {
                    closeSent = proc.CloseMainWindow();
                }
                catch (Exception ex)
                {
                    return (false, "정상 종료 요청(CloseMainWindow) 실패: " + ex.Message);
                }

                if (!closeSent)
                {
                    return (false, "메인 창 핸들이 없어 정상 종료 요청을 전달할 수 없습니다. 콘솔 또는 백그라운드 프로세스일 수 있습니다.");
                }

                // 대기 루프
                var stopwatch = Stopwatch.StartNew();
                while (stopwatch.ElapsedMilliseconds < waitTimeoutMs)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        proc.Refresh();
                        if (proc.HasExited)
                            return (true, "프로세스가 정상적으로 종료되었습니다.");
                    }
                    catch
                    {
                        return (true, "프로세스가 종료되었습니다.");
                    }

                    await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                }

                return (false, "종료 요청을 전송했으나 제한 시간 내에 종료되지 않았습니다 (저장 확인 대화상자가 열려 있을 수 있습니다).");
            }
        }

        /// <summary>
        /// 일반 프로세스를 강제 종료(Kill)한다.
        /// 시스템 프로세스, 서비스, Folderss 자신은 절대 종료하지 않는다.
        /// </summary>
        public static (bool Exited, string Message) ForceKillProcess(ProcessLockInfo info)
        {
            if (info == null)
                return (false, "프로세스 정보가 없습니다.");

            if (!info.CanForceKill)
                return (false, info.UnclosableReason ?? "강제 종료할 수 없는 프로세스입니다.");

            Process proc = null;
            try
            {
                proc = Process.GetProcessById(info.ProcessId);
            }
            catch (ArgumentException)
            {
                return (true, "프로세스가 이미 종료되었습니다.");
            }
            catch (Exception ex)
            {
                return (false, "프로세스를 찾을 수 없습니다: " + ex.Message);
            }

            using (proc)
            {
                try
                {
                    if ((uint)proc.SessionId != CurrentSessionId)
                        return (false, "다른 사용자 세션의 프로세스는 종료할 수 없습니다.");
                }
                catch (Exception ex) { return (false, "프로세스 세션 확인 실패: " + ex.Message); }

                // 재검증
                if (proc.Id == Environment.ProcessId)
                    return (false, "현재 애플리케이션은 강제 종료할 수 없습니다.");

                if (proc.Id <= 4 || KnownCriticalProcessNames.Contains(proc.ProcessName))
                    return (false, "시스템 핵심 프로세스는 강제 종료할 수 없습니다.");

                // PID 재사용 검증
                if (info.StartTimeUtc.HasValue)
                {
                    try
                    {
                        var currentStartTimeUtc = proc.StartTime.ToUniversalTime();
                        if (Math.Abs((currentStartTimeUtc - info.StartTimeUtc.Value).TotalSeconds) > 2.0)
                        {
                            return (false, "PID가 다른 프로세스에 의해 재사용되었습니다. 안전을 위해 강제 종료를 중단합니다.");
                        }
                    }
                    catch (Exception ex)
                    {
                        return (false, "프로세스 시작 시각을 검증할 수 없습니다: " + ex.Message);
                    }
                }

                try
                {
                    proc.Kill();
                    return (true, "프로세스가 강제 종료되었습니다.");
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorAccessDenied)
                {
                    return (false, "프로세스 강제 종료 권한이 부족합니다 (관리자 권한 필요).");
                }
                catch (Exception ex)
                {
                    return (false, "강제 종료 실패: " + ex.Message);
                }
            }
        }

        #endregion
    }
}

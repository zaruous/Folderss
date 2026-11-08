using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Folderss.Services;
using Xunit;

namespace Folderss.SearchTests
{
    public class FileLockServiceTests
    {
        [Fact]
        public void ErrorClassification_DistinguishesSharingViolation_AndAccessDenied()
        {
            // Win32 32 (0x20): ERROR_SHARING_VIOLATION
            var sharingEx = new IOException("Sharing violation", unchecked((int)0x80070020));
            Assert.True(FileLockService.IsSharingOrLockViolation(sharingEx));
            Assert.False(FileLockService.IsAccessDenied(sharingEx));
            Assert.Equal(FileErrorClassification.SharingOrLockViolation, FileLockService.ClassifyError(sharingEx));

            // Win32 33 (0x21): ERROR_LOCK_VIOLATION
            var lockEx = new IOException("Lock violation", unchecked((int)0x80070021));
            Assert.True(FileLockService.IsSharingOrLockViolation(lockEx));
            Assert.False(FileLockService.IsAccessDenied(lockEx));
            Assert.Equal(FileErrorClassification.SharingOrLockViolation, FileLockService.ClassifyError(lockEx));

            // Win32 5: ERROR_ACCESS_DENIED
            var accessDeniedWin32 = new Win32Exception(5);
            Assert.False(FileLockService.IsSharingOrLockViolation(accessDeniedWin32));
            Assert.True(FileLockService.IsAccessDenied(accessDeniedWin32));
            Assert.Equal(FileErrorClassification.AccessDenied, FileLockService.ClassifyError(accessDeniedWin32));

            // UnauthorizedAccessException
            var unauthEx = new UnauthorizedAccessException("Access denied");
            Assert.False(FileLockService.IsSharingOrLockViolation(unauthEx));
            Assert.True(FileLockService.IsAccessDenied(unauthEx));
            Assert.Equal(FileErrorClassification.AccessDenied, FileLockService.ClassifyError(unauthEx));
        }

        [Fact]
        public void ProcessSafetyEvaluation_BlocksCurrentProcessAndSystemProcesses()
        {
            // 1. Folderss 앱 자신 (Current Process)
            var currentProcInfo = new FileLockService.RM_PROCESS_INFO
            {
                Process = new FileLockService.RM_UNIQUE_PROCESS { dwProcessId = Environment.ProcessId },
                strAppName = "Folderss.exe",
                ApplicationType = FileLockService.RM_APP_TYPE.RmMainWindow
            };
            var evalCurrent = FileLockService.EvaluateProcess(currentProcInfo);
            Assert.True(evalCurrent.IsCurrentProcess);
            Assert.False(evalCurrent.CanRequestClose);
            Assert.False(evalCurrent.CanForceKill);
            Assert.Contains("Folderss", evalCurrent.UnclosableReason);

            // 2. 시스템 핵심 프로세스 (PID <= 4)
            var systemProcInfo = new FileLockService.RM_PROCESS_INFO
            {
                Process = new FileLockService.RM_UNIQUE_PROCESS { dwProcessId = 4 },
                strAppName = "System",
                ApplicationType = FileLockService.RM_APP_TYPE.RmCritical
            };
            var evalSystem = FileLockService.EvaluateProcess(systemProcInfo);
            Assert.True(evalSystem.IsCritical);
            Assert.False(evalSystem.CanRequestClose);
            Assert.False(evalSystem.CanForceKill);

            // 3. 시스템 핵심 프로세스 이름 (csrss)
            var csrssProcInfo = new FileLockService.RM_PROCESS_INFO
            {
                Process = new FileLockService.RM_UNIQUE_PROCESS { dwProcessId = 99999 },
                strAppName = "csrss.exe",
                ApplicationType = FileLockService.RM_APP_TYPE.RmCritical
            };
            var evalCsrss = FileLockService.EvaluateProcess(csrssProcInfo);
            Assert.True(evalCsrss.IsCritical);
            Assert.False(evalCsrss.CanRequestClose);
            Assert.False(evalCsrss.CanForceKill);

            // 4. Windows 서비스
            var serviceProcInfo = new FileLockService.RM_PROCESS_INFO
            {
                Process = new FileLockService.RM_UNIQUE_PROCESS { dwProcessId = 88888 },
                strAppName = "svchost.exe",
                strServiceShortName = "wuauserv",
                ApplicationType = FileLockService.RM_APP_TYPE.RmService
            };
            var evalService = FileLockService.EvaluateProcess(serviceProcInfo);
            Assert.True(evalService.IsService);
            Assert.False(evalService.CanRequestClose);
            Assert.False(evalService.CanForceKill);

            // 5. 일반 UI 프로세스
            var userAppInfo = new FileLockService.RM_PROCESS_INFO
            {
                Process = new FileLockService.RM_UNIQUE_PROCESS { dwProcessId = 77777 },
                strAppName = "notepad.exe",
                ApplicationType = FileLockService.RM_APP_TYPE.RmMainWindow
            };
            var evalUserApp = FileLockService.EvaluateProcess(userAppInfo);
            Assert.False(evalUserApp.IsCritical);
            Assert.False(evalUserApp.IsService);
            Assert.False(evalUserApp.IsCurrentProcess);
            Assert.True(evalUserApp.CanRequestClose);
            Assert.True(evalUserApp.CanForceKill);
        }

        [Fact]
        public void VirtualArchivePaths_DetectedAndBlocked()
        {
            string zipPath = @"C:\Users\test\archive.zip\subfolder\test.txt";
            var result = FileLockService.Inspect(zipPath);
            Assert.False(result.Success);
            Assert.True(result.IsArchiveVirtualPath);
            Assert.Contains("가상 경로", result.Message);
        }

        [Fact]
        public void RealWindowsFileLock_DetectsLockingProcessViaRestartManager()
        {
            if (!OperatingSystem.IsWindows())
                return;

            string tempFile = Path.Combine(Path.GetTempPath(), "Folderss_LockTest_" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(tempFile, "lock test content");

            try
            {
                // 파일을 배타적 모드로 연다 (공유 없음)
                using (var fs = new FileStream(tempFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    Assert.True(FileLockService.IsFileLocked(tempFile));

                    var result = FileLockService.Inspect(tempFile);
                    Assert.True(result.Success);

                    // 점유 프로세스 중에 현재 프로세스(테스트 러너)가 포함되어 있어야 함
                    var currentPid = Environment.ProcessId;
                    var lockProc = result.Processes.FirstOrDefault(p => p.ProcessId == currentPid);
                    Assert.NotNull(lockProc);
                    Assert.True(lockProc.IsCurrentProcess);
                }

                // 스트림 닫은 후에는 잠금이 풀려 있어야 함
                Assert.False(FileLockService.IsFileLocked(tempFile));
                var afterResult = FileLockService.Inspect(tempFile);
                Assert.True(afterResult.Success);
                Assert.Empty(afterResult.Processes);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }
        }
    }
}

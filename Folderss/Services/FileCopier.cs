using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace Folderss.Services
{
    /// <summary>
    /// 파일 하나 복사(진행 알림·일시정지·취소)와 읽기 전용까지 지우는 삭제. 파일 작업 대기열이 쓴다.
    /// Windows는 <c>CopyFileEx</c>(진행 콜백, 속성·수정 시각·대체 데이터 스트림 보존, 취소 시 만들던 파일 삭제)를,
    /// 다른 OS(테스트)는 스트림 복사를 쓴다. 대상이 이미 있으면 덮어쓰지 않고 실패한다 — 충돌은 부르는 쪽이 먼저 정한다.
    /// </summary>
    public static class FileCopier
    {
        private const uint ProgressContinue = 0;
        private const uint ProgressCancel = 1;
        private const uint CopyFileFailIfExists = 0x1;
        private const int ErrorFileExists = 80;
        private const int ErrorAlreadyExists = 183;
        private const int ErrorRequestAborted = 1235;
        private const int BufferSize = 1 << 20;

        /// <summary>
        /// <paramref name="source"/>를 <paramref name="destination"/>(없어야 함)으로 복사한다.
        /// <paramref name="progress"/>는 이 파일에서 지금까지 복사한 바이트, <paramref name="checkpoint"/>는 덩어리마다 불려
        /// 일시정지 중이면 기다리고 취소됐으면 <see cref="OperationCanceledException"/>을 던진다.
        /// 취소·실패하면 만들던 대상 파일을 지운다(이미 있던 대상은 건드리지 않음).
        /// </summary>
        public static void CopyFile(string source, string destination, Action<long> progress, Action checkpoint)
        {
            if (OperatingSystem.IsWindows())
                CopyWithWin32(source, destination, progress, checkpoint);
            else
                CopyWithStreams(source, destination, progress, checkpoint);
        }

        /// <summary>폴더면 안의 읽기 전용 속성을 풀고 통째로, 파일이면 하나 지운다. 없으면 아무것도 안 한다.</summary>
        public static void DeletePath(string path)
        {
            if (Directory.Exists(path))
            {
                ClearReadOnly(path);
                Directory.Delete(path, true);
            }
            else if (File.Exists(path))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
                File.Delete(path);
            }
        }

        /// <summary>정리용 삭제: 실패해도 원래 오류를 가리지 않도록 결과만 돌려준다.</summary>
        public static bool TryDeletePath(string path, out string error)
        {
            error = null;
            try
            {
                DeletePath(path);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Win32 파일 API에 넘길 확장 경로(<c>\\?\C:\…</c>, <c>\\?\UNC\서버\공유\…</c>). 260자 이상 긴 경로도 열린다.
        /// 확장 경로는 정규화를 하지 않으므로 먼저 전체 경로로 바꾼다.
        /// </summary>
        public static string ToExtendedPath(string path)
        {
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
                return path;
            var full = Path.GetFullPath(path);
            if (full.StartsWith(@"\\", StringComparison.Ordinal))
                return @"\\?\UNC\" + full.Substring(2);
            return @"\\?\" + full;
        }

        private static void ClearReadOnly(string directory)
        {
            // 링크 폴더(reparse point) 안으로는 들어가지 않는다 — Directory.Delete도 링크 자체만 지운다.
            foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true }))
            {
                if ((entry.Attributes & FileAttributes.ReadOnly) != 0)
                    entry.Attributes &= ~FileAttributes.ReadOnly;
                if (entry is DirectoryInfo && (entry.Attributes & FileAttributes.ReparsePoint) == 0)
                    ClearReadOnly(entry.FullName);
            }
        }

        // ── Windows: CopyFileEx ───────────────────────────────────────

        private delegate uint CopyProgressRoutine(long totalFileSize, long totalBytesTransferred, long streamSize, long streamBytesTransferred,
            uint streamNumber, uint callbackReason, IntPtr sourceFile, IntPtr destinationFile, IntPtr data);

        [DllImport("kernel32.dll", EntryPoint = "CopyFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CopyFileEx(string existingFileName, string newFileName, CopyProgressRoutine progressRoutine,
            IntPtr data, IntPtr cancel, uint copyFlags);

        private static void CopyWithWin32(string source, string destination, Action<long> progress, Action checkpoint)
        {
            Exception callbackError = null;
            CopyProgressRoutine routine = (total, transferred, streamSize, streamTransferred, stream, reason, src, dst, data) =>
            {
                // 네이티브 프레임을 넘어 예외를 던지면 안 된다. 담아 두고 취소를 돌려준 뒤 밖에서 다시 던진다.
                try
                {
                    progress?.Invoke(transferred);
                    checkpoint?.Invoke();
                    return ProgressContinue;
                }
                catch (Exception ex)
                {
                    callbackError = ex;
                    return ProgressCancel;
                }
            };

            var ok = CopyFileEx(ToExtendedPath(source), ToExtendedPath(destination), routine, IntPtr.Zero, IntPtr.Zero, CopyFileFailIfExists);
            var error = Marshal.GetLastWin32Error();
            GC.KeepAlive(routine);
            if (ok)
                return;

            // PROGRESS_CANCEL이면 CopyFileEx가 만들던 대상 파일을 지운다. 그 밖의 실패는 남을 수 있어 직접 지운다(이미 있던 대상이면 건드리지 않음).
            if (callbackError == null && error != ErrorRequestAborted && error != ErrorFileExists && error != ErrorAlreadyExists)
                TryDeletePath(destination, out _);
            if (callbackError != null)
            {
                TryDeletePath(destination, out _);
                if (callbackError is OperationCanceledException)
                    throw callbackError;
                throw new IOException(callbackError.Message, callbackError);
            }
            if (error == ErrorRequestAborted)
                throw new OperationCanceledException();
            if (error == ErrorFileExists || error == ErrorAlreadyExists)
                throw new IOException("대상에 같은 이름이 이미 있습니다: " + destination);
            throw new IOException(new Win32Exception(error).Message + " (" + source + ")", unchecked((int)0x80070000) | error);
        }

        // ── 그 밖의 OS(테스트): 스트림 복사 ──────────────────────────────

        private static void CopyWithStreams(string source, string destination, Action<long> progress, Action checkpoint)
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan))
            {
                var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16);
                try
                {
                    var buffer = new byte[BufferSize];
                    long total = 0;
                    progress?.Invoke(0);
                    checkpoint?.Invoke();
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        output.Write(buffer, 0, read);
                        total += read;
                        progress?.Invoke(total);
                        checkpoint?.Invoke();
                    }
                    output.Dispose();
                }
                catch
                {
                    output.Dispose();
                    TryDeletePath(destination, out _);
                    throw;
                }
            }
            File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
            File.SetAttributes(destination, File.GetAttributes(source));
        }
    }
}

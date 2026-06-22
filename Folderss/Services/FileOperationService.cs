using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.VisualBasic.FileIO;

namespace Folderss.Services
{
    public static class FileOperationService
    {
        public static void Copy(string source, string destinationDirectory)
        {
            ValidateTransfer(source, destinationDirectory, false);
            if (Directory.Exists(source))
            {
                var destination = GetUniquePath(Path.Combine(destinationDirectory, Path.GetFileName(source)), true);
                CopyDirectory(source, destination);
                return;
            }

            var fileDestination = GetUniquePath(Path.Combine(destinationDirectory, Path.GetFileName(source)), false);
            File.Copy(source, fileDestination);
        }

        public static void Move(string source, string destinationDirectory)
        {
            ValidateTransfer(source, destinationDirectory, true);
            var isDirectory = Directory.Exists(source);
            var destination = GetUniquePath(Path.Combine(destinationDirectory, Path.GetFileName(source)), isDirectory);

            if (isDirectory)
                Directory.Move(source, destination);
            else
                File.Move(source, destination);
        }

        public static void CreateShortcut(string source, string destinationDirectory)
        {
            if (!File.Exists(source) && !Directory.Exists(source))
                throw new FileNotFoundException("바로가기를 만들 원본이 존재하지 않습니다.", source);

            var shortcutPath = GetUniquePath(
                Path.Combine(destinationDirectory, Path.GetFileName(source) + ".lnk"),
                false);
            var shellLink = (IShellLinkW)new ShellLink();
            try
            {
                shellLink.SetPath(source);
                shellLink.SetDescription(Path.GetFileName(source) + " 바로가기");

                var workingDirectory = Directory.Exists(source)
                    ? source
                    : Path.GetDirectoryName(source);
                if (!string.IsNullOrWhiteSpace(workingDirectory))
                    shellLink.SetWorkingDirectory(workingDirectory);

                ((IPersistFile)shellLink).Save(shortcutPath, false);
            }
            finally
            {
                Marshal.FinalReleaseComObject(shellLink);
            }
        }

        public static void MoveToRecycleBin(string path)
        {
            if (Directory.Exists(path))
            {
                FileSystem.DeleteDirectory(
                    path,
                    UIOption.OnlyErrorDialogs,
                    RecycleOption.SendToRecycleBin,
                    UICancelOption.ThrowException);
            }
            else if (File.Exists(path))
            {
                FileSystem.DeleteFile(
                    path,
                    UIOption.OnlyErrorDialogs,
                    RecycleOption.SendToRecycleBin,
                    UICancelOption.ThrowException);
            }
        }

        public static void DeletePermanently(string path)
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
            else if (File.Exists(path))
                File.Delete(path);
        }

        public static string Rename(string path, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName) || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("사용할 수 없는 이름입니다.");

            var parent = Path.GetDirectoryName(path);
            var destination = Path.Combine(parent ?? string.Empty, newName.Trim());
            if (File.Exists(destination) || Directory.Exists(destination))
                throw new IOException("같은 이름의 항목이 이미 있습니다.");

            if (Directory.Exists(path))
                Directory.Move(path, destination);
            else
                File.Move(path, destination);

            return destination;
        }

        public static string CreateDirectory(string parent, string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("사용할 수 없는 폴더 이름입니다.");

            var path = Path.Combine(parent, name.Trim());
            if (Directory.Exists(path) || File.Exists(path))
                throw new IOException("같은 이름의 항목이 이미 있습니다.");

            return Directory.CreateDirectory(path).FullName;
        }

        public static string CreateFile(string parent, string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("사용할 수 없는 파일 이름입니다.");

            var path = Path.Combine(parent, name.Trim());
            if (File.Exists(path) || Directory.Exists(path))
                throw new IOException("같은 이름의 항목이 이미 있습니다.");

            File.Create(path).Dispose();
            return path;
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);

            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));

            foreach (var directory in Directory.GetDirectories(source))
                CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }

        private static void ValidateTransfer(string source, string destinationDirectory, bool move)
        {
            var sourceFullPath = NormalizePath(source);
            var destinationFullPath = NormalizePath(destinationDirectory);
            var sourceParent = Path.GetDirectoryName(sourceFullPath);

            if (move && string.Equals(NormalizePath(sourceParent), destinationFullPath, StringComparison.OrdinalIgnoreCase))
                throw new IOException("원본과 대상 폴더가 같습니다.");

            if (!Directory.Exists(source))
                return;

            if (string.Equals(sourceFullPath, destinationFullPath, StringComparison.OrdinalIgnoreCase) ||
                destinationFullPath.StartsWith(
                    sourceFullPath + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                throw new IOException("폴더를 자기 자신이나 하위 폴더로 복사 또는 이동할 수 없습니다.");
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
                ? root
                : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static string GetUniquePath(string requestedPath, bool isDirectory)
        {
            if (!File.Exists(requestedPath) && !Directory.Exists(requestedPath))
                return requestedPath;

            var parent = Path.GetDirectoryName(requestedPath) ?? string.Empty;
            var extension = isDirectory ? string.Empty : Path.GetExtension(requestedPath);
            var baseName = isDirectory ? Path.GetFileName(requestedPath) : Path.GetFileNameWithoutExtension(requestedPath);

            var index = 2;
            string candidate;
            do
            {
                candidate = Path.Combine(parent, string.Format("{0} ({1}){2}", baseName, index, extension));
                index++;
            }
            while (File.Exists(candidate) || Directory.Exists(candidate));

            return candidate;
        }

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink
        {
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder filePath, int maxPath,
                IntPtr findData, uint flags);
            void GetIDList(out IntPtr itemIdList);
            void SetIDList(IntPtr itemIdList);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder name, int maxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder directory,
                int maxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder arguments,
                int maxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
            void GetHotkey(out short hotkey);
            void SetHotkey(short hotkey);
            void GetShowCmd(out int showCommand);
            void SetShowCmd(int showCommand);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder iconPath,
                int iconPathLength, out int iconIndex);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
            void Resolve(IntPtr windowHandle, uint flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string filePath);
        }
    }
}

using System;
using System.IO;
using Microsoft.VisualBasic.FileIO;

namespace Folderss.Services
{
    public static class FileOperationService
    {
        public static void Copy(string source, string destinationDirectory)
        {
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
            var isDirectory = Directory.Exists(source);
            var destination = GetUniquePath(Path.Combine(destinationDirectory, Path.GetFileName(source)), isDirectory);

            if (isDirectory)
                Directory.Move(source, destination);
            else
                File.Move(source, destination);
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

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);

            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));

            foreach (var directory in Directory.GetDirectories(source))
                CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
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
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Folderss.Services
{
    public static class FilePreviewService
    {
        public const int TextPreviewByteLimit = 10 * 1024;

        private static readonly HashSet<string> ImageExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".bmp", ".gif", ".ico", ".jpeg", ".jpg", ".png", ".tif", ".tiff"
            };

        public static bool IsImage(string path)
        {
            return ImageExtensions.Contains(Path.GetExtension(path));
        }

        public static string ReadTextPreview(string path, out bool truncated)
        {
            var fileLength = new FileInfo(path).Length;
            var bytesToRead = (int)Math.Min(fileLength, TextPreviewByteLimit);
            var buffer = new byte[bytesToRead];

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var offset = 0;
                while (offset < buffer.Length)
                {
                    var read = stream.Read(buffer, offset, buffer.Length - offset);
                    if (read == 0)
                        break;
                    offset += read;
                }

                if (offset != buffer.Length)
                    Array.Resize(ref buffer, offset);
            }

            truncated = fileLength > buffer.Length;
            if (LooksBinary(buffer))
                return "바이너리 파일은 텍스트 내용 미리보기를 지원하지 않습니다.";

            var encoding = DetectEncoding(buffer);
            var preambleLength = GetPreambleLength(buffer, encoding);
            var text = encoding.GetString(buffer, preambleLength, buffer.Length - preambleLength);
            return truncated ? text + Environment.NewLine + Environment.NewLine + "… 처음 10KB까지만 표시합니다." : text;
        }

        public static FileMetadata ReadMetadata(string path)
        {
            var isDirectory = Directory.Exists(path);
            var info = isDirectory ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);
            var size = isDirectory ? 0L : ((FileInfo)info).Length;

            return new FileMetadata
            {
                Name = info.Name,
                Type = isDirectory ? "폴더" : GetFileType(info.Name),
                SizeText = isDirectory ? "-" : string.Format("{0:0.00} MB", size / 1024d / 1024d),
                CreatedAt = info.CreationTime,
                ModifiedAt = info.LastWriteTime,
                Permissions = GetPermissions(path, isDirectory),
                Attributes = info.Attributes.ToString()
            };
        }

        private static string GetFileType(string name)
        {
            var extension = Path.GetExtension(name);
            return string.IsNullOrWhiteSpace(extension)
                ? "파일"
                : extension.TrimStart('.').ToUpperInvariant() + " 파일";
        }

        private static string GetPermissions(string path, bool isDirectory)
        {
            try
            {
                FileSystemSecurity security = isDirectory
                    ? (FileSystemSecurity)Directory.GetAccessControl(path)
                    : File.GetAccessControl(path);
                var identity = WindowsIdentity.GetCurrent();
                var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (identity != null)
                {
                    identities.Add(identity.User == null ? string.Empty : identity.User.Value);
                    foreach (var group in identity.Groups ?? new IdentityReferenceCollection())
                        identities.Add(group.Value);
                }

                var allowed = FileSystemRights.ReadPermissions;
                var denied = (FileSystemRights)0;
                var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));
                foreach (FileSystemAccessRule rule in rules)
                {
                    var sid = rule.IdentityReference.Value;
                    if (!identities.Contains(sid))
                        continue;

                    if (rule.AccessControlType == AccessControlType.Deny)
                        denied |= rule.FileSystemRights;
                    else
                        allowed |= rule.FileSystemRights;
                }

                var permissions = new List<string>();
                AddPermission(permissions, "읽기", allowed, denied, FileSystemRights.ReadData | FileSystemRights.ListDirectory);
                AddPermission(permissions, "쓰기", allowed, denied, FileSystemRights.WriteData | FileSystemRights.CreateFiles);
                AddPermission(permissions, "수정", allowed, denied, FileSystemRights.Modify);
                AddPermission(permissions, "전체 제어", allowed, denied, FileSystemRights.FullControl);
                return permissions.Count == 0 ? "권한 정보 없음" : string.Join(", ", permissions);
            }
            catch
            {
                return "확인할 수 없음";
            }
        }

        private static void AddPermission(
            ICollection<string> result,
            string name,
            FileSystemRights allowed,
            FileSystemRights denied,
            FileSystemRights required)
        {
            if ((denied & required) == 0 && (allowed & required) != 0)
                result.Add(name);
        }

        internal static bool LooksBinary(byte[] bytes)
        {
            if (bytes.Length == 0)
                return false;

            var controlCharacters = 0;
            foreach (var value in bytes.Take(4096))
            {
                if (value == 0)
                    return true;
                if (value < 8 || (value > 13 && value < 32))
                    controlCharacters++;
            }

            return controlCharacters > Math.Max(4, Math.Min(bytes.Length, 4096) / 20);
        }

        private static Encoding DetectEncoding(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return new UTF8Encoding(true);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode;
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode;
            return new UTF8Encoding(false, false);
        }

        private static int GetPreambleLength(byte[] bytes, Encoding encoding)
        {
            var preamble = encoding.GetPreamble();
            if (preamble.Length == 0 || bytes.Length < preamble.Length)
                return 0;

            for (var index = 0; index < preamble.Length; index++)
            {
                if (bytes[index] != preamble[index])
                    return 0;
            }

            return preamble.Length;
        }
    }

    public sealed class FileMetadata
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string SizeText { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ModifiedAt { get; set; }
        public string Permissions { get; set; }
        public string Attributes { get; set; }
    }
}

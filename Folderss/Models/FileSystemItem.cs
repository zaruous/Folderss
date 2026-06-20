using System;
using System.IO;

namespace Folderss.Models
{
    public sealed class FileSystemItem
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public DateTime ModifiedAt { get; set; }

        public string Kind
        {
            get
            {
                if (IsDirectory)
                    return "폴더";

                var extension = Path.GetExtension(Name);
                return string.IsNullOrWhiteSpace(extension)
                    ? "파일"
                    : extension.TrimStart('.').ToUpperInvariant() + " 파일";
            }
        }

        public string DisplaySize
        {
            get
            {
                if (IsDirectory)
                    return string.Empty;

                string[] units = { "B", "KB", "MB", "GB", "TB" };
                double value = Size;
                var unit = 0;
                while (value >= 1024 && unit < units.Length - 1)
                {
                    value /= 1024;
                    unit++;
                }

                return unit == 0 ? value.ToString("0") + " " + units[unit] : value.ToString("0.##") + " " + units[unit];
            }
        }

        public string Icon
        {
            get
            {
                if (IsDirectory)
                    return "📁";

                var extension = Path.GetExtension(Name).ToLowerInvariant();
                switch (extension)
                {
                    case ".png":
                    case ".jpg":
                    case ".jpeg":
                    case ".gif":
                    case ".bmp":
                        return "🖼";
                    case ".zip":
                    case ".7z":
                    case ".rar":
                        return "📦";
                    case ".txt":
                    case ".md":
                    case ".log":
                        return "📄";
                    default:
                        return "◻";
                }
            }
        }
    }
}

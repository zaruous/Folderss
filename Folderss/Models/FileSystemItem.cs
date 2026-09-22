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
        public bool IsCut { get; set; }

        /// <summary>심볼릭 링크·junction처럼 대상 경로를 알 수 있는 reparse point. OneDrive 자리표시자 같은 다른 reparse point는 링크로 보지 않는다.</summary>
        public bool IsLink { get; set; }
        public string LinkTarget { get; set; }

        /// <summary>링크 항목에만 붙는 행 툴팁. null이면 WPF가 툴팁을 만들지 않는다.</summary>
        public string LinkToolTip
        {
            get { return IsLink ? "링크 대상: " + (LinkTarget ?? "(알 수 없음)") : null; }
        }

        public string Kind
        {
            get
            {
                if (IsDirectory)
                    return IsLink ? "폴더 링크" : "폴더";

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
                if (IsLink)
                    return "🔗";
                if (IsDirectory)
                    return "📂";

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
                        return "📄";
                }
            }
        }
    }
}

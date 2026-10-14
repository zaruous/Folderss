using System;
using System.Globalization;
using System.IO;
using System.Xml;
using Folderss.Models;

namespace Folderss.Services
{
    /// <summary>폴더 비교 옵션(<see cref="DirectoryCompareOptions"/>)을 <c>%LOCALAPPDATA%\Folderss\compare-settings.xml</c>에 저장한다.</summary>
    public static class CompareSettingsService
    {
        public static readonly string DefaultConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Folderss", "compare-settings.xml");

        public static DirectoryCompareOptions Load() => Load(DefaultConfigPath);

        /// <summary>파일이 없거나 읽지 못하면 기본값(권장값). 값 하나가 깨졌으면 그 값만 기본값.</summary>
        public static DirectoryCompareOptions Load(string path)
        {
            var options = new DirectoryCompareOptions();
            if (!File.Exists(path))
                return options;
            try
            {
                var doc = new XmlDocument();
                doc.Load(path);
                var root = doc.DocumentElement;
                if (root == null)
                    return options;
                options.UseContentHash = ReadBool(root, "contentHash", options.UseContentHash);
                options.ApplyIgnoreRules = ReadBool(root, "applyIgnoreRules", options.ApplyIgnoreRules);
                options.Recursive = ReadBool(root, "recursive", options.Recursive);
                options.IncludeHidden = ReadBool(root, "includeHidden", options.IncludeHidden);
                options.DirectionColors = ReadBool(root, "directionColors", options.DirectionColors);
                options.ShowFolderTree = ReadBool(root, "showFolderTree", options.ShowFolderTree);
                options.ShowPreview = ReadBool(root, "showPreview", options.ShowPreview);
                options.HideSame = ReadBool(root, "hideSame", options.HideSame);
                if (int.TryParse(root.GetAttribute("moveConfirmThreshold"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var threshold) && threshold >= 0)
                    options.MoveConfirmThreshold = threshold;
                if (int.TryParse(root.GetAttribute("maxDepth"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var depth) && depth >= 0)
                    options.MaxDepth = depth;
                if (double.TryParse(root.GetAttribute("timestampToleranceSeconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                    && seconds >= 0 && seconds <= 86400)
                    options.TimestampTolerance = TimeSpan.FromSeconds(seconds);
            }
            catch (Exception ex)
            {
                // 읽기 실패는 기본값으로 동작한다(비교를 못 하는 것보다 낫다). 원인은 디버그 출력에 남긴다.
                System.Diagnostics.Debug.WriteLine(Path.GetFileName(path) + " 읽기 실패: " + ex);
            }
            return options;
        }

        /// <summary>실패는 예외로 알린다.</summary>
        public static void Save(DirectoryCompareOptions options) => Save(options, DefaultConfigPath);

        public static void Save(DirectoryCompareOptions options, string path)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            var doc = new XmlDocument();
            var root = doc.CreateElement("compare");
            root.SetAttribute("contentHash", options.UseContentHash ? "true" : "false");
            root.SetAttribute("timestampToleranceSeconds", options.TimestampTolerance.TotalSeconds.ToString(CultureInfo.InvariantCulture));
            root.SetAttribute("applyIgnoreRules", options.ApplyIgnoreRules ? "true" : "false");
            root.SetAttribute("recursive", options.Recursive ? "true" : "false");
            root.SetAttribute("maxDepth", options.MaxDepth.ToString(CultureInfo.InvariantCulture));
            root.SetAttribute("includeHidden", options.IncludeHidden ? "true" : "false");
            root.SetAttribute("directionColors", options.DirectionColors ? "true" : "false");
            root.SetAttribute("showFolderTree", options.ShowFolderTree ? "true" : "false");
            root.SetAttribute("showPreview", options.ShowPreview ? "true" : "false");
            root.SetAttribute("hideSame", options.HideSame ? "true" : "false");
            root.SetAttribute("moveConfirmThreshold", options.MoveConfirmThreshold.ToString(CultureInfo.InvariantCulture));
            doc.AppendChild(root);
            SettingsFile.Write(path, doc.Save);
        }

        private static bool ReadBool(XmlElement root, string name, bool fallback)
        {
            return bool.TryParse(root.GetAttribute(name), out var value) ? value : fallback;
        }
    }
}

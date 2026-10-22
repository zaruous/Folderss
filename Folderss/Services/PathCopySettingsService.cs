using System;
using System.IO;
using System.Xml;

namespace Folderss.Services
{
    /// <summary>경로 복사 설정(설정 창의 "경로 복사" 탭).</summary>
    public sealed class PathCopySettings
    {
        /// <summary>경로 복사 단축키(기본 Ctrl+Shift+C)가 쓰는 형식. 기본은 따옴표 없는 전체 경로(기존 트리뷰 동작과 같음).</summary>
        public PathCopyFormat ShortcutFormat { get; set; } = PathCopyFormat.FullPath;

        /// <summary>경로 구분자. 기본은 Windows <c>\</c>.</summary>
        public PathCopySeparator Separator { get; set; } = PathCopySeparator.Backslash;

        public PathCopySettings Clone() => (PathCopySettings)MemberwiseClone();
    }

    /// <summary>
    /// 경로 복사 설정을 <c>%LOCALAPPDATA%\Folderss\path-copy-settings.xml</c>에 저장한다.
    /// 메뉴·단축키는 복사할 때마다 <see cref="Current"/>를 읽으므로 저장하면 바로 적용된다.
    /// </summary>
    public static class PathCopySettingsService
    {
        public static readonly string DefaultConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Folderss", "path-copy-settings.xml");

        private static PathCopySettings _current;

        /// <summary>현재 적용 중인 설정(처음 쓸 때 파일에서 읽는다).</summary>
        public static PathCopySettings Current
        {
            get { return _current ?? (_current = Load()); }
        }

        public static PathCopySettings Load() => Load(DefaultConfigPath);

        /// <summary>파일이 없거나 읽지 못하거나 모르는 값이면 그 항목은 기본값.</summary>
        public static PathCopySettings Load(string path)
        {
            var settings = new PathCopySettings();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return settings;
            try
            {
                var doc = new XmlDocument();
                doc.Load(path);
                var root = doc.DocumentElement;
                if (root == null)
                    return settings;
                if (Enum.TryParse(root.GetAttribute("shortcutFormat"), false, out PathCopyFormat format) &&
                    Enum.IsDefined(typeof(PathCopyFormat), format))
                    settings.ShortcutFormat = format;
                if (Enum.TryParse(root.GetAttribute("separator"), false, out PathCopySeparator separator) &&
                    Enum.IsDefined(typeof(PathCopySeparator), separator))
                    settings.Separator = separator;
            }
            catch (Exception ex)
            {
                // 읽기 실패는 기본값으로 동작한다(복사를 못 하는 것보다 낫다). 원인은 디버그 출력에 남긴다.
                System.Diagnostics.Debug.WriteLine(Path.GetFileName(path) + " 읽기 실패: " + ex);
            }
            return settings;
        }

        /// <summary>파일에 저장하고 <see cref="Current"/>를 바꾼다. 실패는 예외로 알린다(설정 창이 "설정 저장 실패"로 모아 보여 준다).</summary>
        public static void Save(PathCopySettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            // 파일 저장이 실패해도 이번 실행 중에는 바뀐 설정을 쓴다(다른 설정과 같은 방식).
            _current = settings.Clone();
            Save(settings, DefaultConfigPath);
        }

        public static void Save(PathCopySettings settings, string path)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            var doc = new XmlDocument();
            var root = doc.CreateElement("pathCopy");
            root.SetAttribute("shortcutFormat", settings.ShortcutFormat.ToString());
            root.SetAttribute("separator", settings.Separator.ToString());
            doc.AppendChild(root);
            SettingsFile.Write(path, doc.Save);
        }
    }
}

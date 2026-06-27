using System;
using System.IO;
using System.Xml;

namespace Folderss.Services
{
    public sealed class ConsoleSettings
    {
        public int MaxOutputLineCount { get; set; } = ConsoleSettingsService.DefaultMaxOutputLineCount;
        public string PreferredShellKind { get; set; } = ConsoleSettingsService.DefaultShellKind;

        public ConsoleSettings Clone()
        {
            return new ConsoleSettings
            {
                MaxOutputLineCount = MaxOutputLineCount,
                PreferredShellKind = PreferredShellKind
            };
        }
    }

    public static class ConsoleSettingsService
    {
        public const int DefaultMaxOutputLineCount = 5000;
        public const int MinOutputLineCount = 100;
        public const int MaxOutputLineCount = 100000;
        public const string DefaultShellKind = "WindowsPowerShell";

        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Folderss", "console-settings.xml");

        public static ConsoleSettings Load()
        {
            var settings = new ConsoleSettings();

            try
            {
                if (!File.Exists(ConfigPath))
                    return settings;

                var doc = new XmlDocument();
                doc.Load(ConfigPath);

                int maxLines;
                var value = doc.SelectSingleNode("/ConsoleSettings/MaxOutputLineCount")?.InnerText;
                if (int.TryParse(value, out maxLines))
                    settings.MaxOutputLineCount = ClampMaxOutputLineCount(maxLines);

                var shell = doc.SelectSingleNode("/ConsoleSettings/PreferredShellKind")?.InnerText;
                if (!string.IsNullOrWhiteSpace(shell))
                    settings.PreferredShellKind = shell.Trim();
            }
            catch
            {
                return new ConsoleSettings();
            }

            return settings;
        }

        public static void Save(ConsoleSettings settings)
        {
            if (settings == null)
                settings = new ConsoleSettings();

            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var doc = new XmlDocument();
                var declaration = doc.CreateXmlDeclaration("1.0", "utf-8", null);
                doc.AppendChild(declaration);

                var root = doc.CreateElement("ConsoleSettings");
                doc.AppendChild(root);

                AppendChild(doc, root, "MaxOutputLineCount",
                    ClampMaxOutputLineCount(settings.MaxOutputLineCount).ToString());
                AppendChild(doc, root, "PreferredShellKind",
                    string.IsNullOrWhiteSpace(settings.PreferredShellKind)
                        ? DefaultShellKind
                        : settings.PreferredShellKind.Trim());

                doc.Save(ConfigPath);
            }
            catch
            {
                // 설정 저장 실패가 설정 창 전체 저장 흐름을 중단하지 않도록 한다.
            }
        }

        public static int ClampMaxOutputLineCount(int value)
        {
            if (value < MinOutputLineCount)
                return MinOutputLineCount;
            if (value > MaxOutputLineCount)
                return MaxOutputLineCount;
            return value;
        }

        private static void AppendChild(XmlDocument doc, XmlNode parent, string name, string value)
        {
            var el = doc.CreateElement(name);
            el.InnerText = value ?? "";
            parent.AppendChild(el);
        }
    }
}

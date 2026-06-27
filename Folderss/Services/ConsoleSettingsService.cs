using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace Folderss.Services
{
    public sealed class ConsoleCommandProfile
    {
        public string Key { get; set; }
        public string DisplayName { get; set; }
        public string FileName { get; set; }
        public string Arguments { get; set; }
        public string ShellKind { get; set; }
        public bool IsBuiltIn { get; set; }

        public ConsoleCommandProfile Clone()
        {
            return new ConsoleCommandProfile
            {
                Key = Key,
                DisplayName = DisplayName,
                FileName = FileName,
                Arguments = Arguments,
                ShellKind = ShellKind,
                IsBuiltIn = IsBuiltIn
            };
        }
    }

    public sealed class ConsoleSettings
    {
        public string PreferredProfileKey { get; set; } = ConsoleSettingsService.DefaultProfileKey;
        public int FontSize { get; set; } = ConsoleSettingsService.DefaultFontSize;
        public List<ConsoleCommandProfile> CustomProfiles { get; set; } = new List<ConsoleCommandProfile>();

        public ConsoleSettings Clone()
        {
            return new ConsoleSettings
            {
                PreferredProfileKey = PreferredProfileKey,
                FontSize = FontSize,
                CustomProfiles = CustomProfiles.Select(profile => profile.Clone()).ToList()
            };
        }
    }

    public static class ConsoleSettingsService
    {
        public const string DefaultProfileKey = "builtin:powershell";
        public const int DefaultFontSize = 13;
        public const int MinFontSize = 8;
        public const int MaxFontSize = 32;

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

                var preferredProfileKey = doc.SelectSingleNode("/ConsoleSettings/PreferredProfileKey")?.InnerText;
                if (!string.IsNullOrWhiteSpace(preferredProfileKey))
                {
                    settings.PreferredProfileKey = preferredProfileKey.Trim();
                }
                else
                {
                    var legacyShellKind = doc.SelectSingleNode("/ConsoleSettings/PreferredShellKind")?.InnerText;
                    settings.PreferredProfileKey = LegacyShellKindToProfileKey(legacyShellKind);
                }

                int fontSize;
                var fontSizeValue = doc.SelectSingleNode("/ConsoleSettings/FontSize")?.InnerText;
                if (int.TryParse(fontSizeValue, out fontSize))
                    settings.FontSize = ClampFontSize(fontSize);

                var profileNodes = doc.SelectNodes("/ConsoleSettings/CustomProfiles/Profile");
                if (profileNodes != null)
                {
                    foreach (XmlNode node in profileNodes)
                    {
                        var profile = new ConsoleCommandProfile
                        {
                            Key = node.Attributes?["Key"]?.Value,
                            DisplayName = node["DisplayName"]?.InnerText?.Trim(),
                            FileName = node["FileName"]?.InnerText?.Trim(),
                            Arguments = node["Arguments"]?.InnerText ?? "",
                            ShellKind = node["ShellKind"]?.InnerText?.Trim(),
                            IsBuiltIn = false
                        };

                        if (string.IsNullOrWhiteSpace(profile.Key))
                            profile.Key = "custom:" + Guid.NewGuid().ToString("N");

                        if (string.IsNullOrWhiteSpace(profile.DisplayName) || string.IsNullOrWhiteSpace(profile.FileName))
                            continue;

                        settings.CustomProfiles.Add(profile);
                    }
                }
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

                AppendChild(doc, root, "PreferredProfileKey",
                    string.IsNullOrWhiteSpace(settings.PreferredProfileKey)
                        ? DefaultProfileKey
                        : settings.PreferredProfileKey.Trim());
                AppendChild(doc, root, "FontSize", ClampFontSize(settings.FontSize).ToString());

                var customProfiles = doc.CreateElement("CustomProfiles");
                root.AppendChild(customProfiles);

                foreach (var profile in settings.CustomProfiles.Where(profile => profile != null))
                {
                    if (string.IsNullOrWhiteSpace(profile.DisplayName) || string.IsNullOrWhiteSpace(profile.FileName))
                        continue;

                    var profileElement = doc.CreateElement("Profile");
                    var key = string.IsNullOrWhiteSpace(profile.Key)
                        ? "custom:" + Guid.NewGuid().ToString("N")
                        : profile.Key.Trim();
                    var keyAttribute = doc.CreateAttribute("Key");
                    keyAttribute.Value = key;
                    profileElement.Attributes.Append(keyAttribute);

                    AppendChild(doc, profileElement, "DisplayName", profile.DisplayName.Trim());
                    AppendChild(doc, profileElement, "FileName", profile.FileName.Trim());
                    AppendChild(doc, profileElement, "Arguments", profile.Arguments ?? "");
                    AppendChild(doc, profileElement, "ShellKind", profile.ShellKind ?? "");
                    customProfiles.AppendChild(profileElement);
                }

                doc.Save(ConfigPath);
            }
            catch
            {
                // 설정 저장 실패가 설정 창 전체 저장 흐름을 중단하지 않도록 한다.
            }
        }

        public static int ClampFontSize(int value)
        {
            if (value < MinFontSize)
                return MinFontSize;
            if (value > MaxFontSize)
                return MaxFontSize;
            return value;
        }

        private static string LegacyShellKindToProfileKey(string value)
        {
            switch ((value ?? "").Trim())
            {
                case "PowerShell7":
                    return "builtin:pwsh";
                case "CommandPrompt":
                    return "builtin:cmd";
                default:
                    return DefaultProfileKey;
            }
        }

        private static void AppendChild(XmlDocument doc, XmlNode parent, string name, string value)
        {
            var el = doc.CreateElement(name);
            el.InnerText = value ?? "";
            parent.AppendChild(el);
        }
    }
}

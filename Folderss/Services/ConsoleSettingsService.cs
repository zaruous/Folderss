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

        /// <summary>
        /// 콘솔 설정을 파일에 쓴다. 임시 파일에 쓴 뒤 교체하고(<see cref="SettingsFile"/>), 실패는 삼키지 않고
        /// 예외로 알린다 — 설정 창은 각 저장을 독립적으로 시도해 실패를 모아 보여주므로 여기서 삼킬 필요가 없고,
        /// 삼키면 다른 PC에서 저장이 안 될 때 원인을 알 수 없다.
        /// 설정 창 밖의 부수 저장(콘솔 탭 시작 시 마지막 프로필 기억)은 호출처에서 처리한다.
        /// </summary>
        public static void Save(ConsoleSettings settings)
        {
            if (settings == null)
                settings = new ConsoleSettings();

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

            SettingsFile.Write(ConfigPath, doc.Save);
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

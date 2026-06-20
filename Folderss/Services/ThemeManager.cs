using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace Folderss.Services
{
    public enum AppTheme
    {
        Black,
        Light
    }

    public static class ThemeManager
    {
        private const string ThemeDictionaryPrefix = "Themes/";

        public static AppTheme CurrentTheme { get; private set; } = AppTheme.Black;

        private static string SettingsPath
        {
            get
            {
                var directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Folderss");
                return Path.Combine(directory, "theme.txt");
            }
        }

        public static void ApplySavedTheme()
        {
            var theme = AppTheme.Black;
            try
            {
                if (File.Exists(SettingsPath))
                    Enum.TryParse(File.ReadAllText(SettingsPath).Trim(), true, out theme);
            }
            catch
            {
                theme = AppTheme.Black;
            }

            ApplyTheme(theme, false);
        }

        public static void ApplyTheme(AppTheme theme, bool save = true)
        {
            var dictionaries = Application.Current.Resources.MergedDictionaries;
            var existing = dictionaries.FirstOrDefault(IsThemeDictionary);
            var replacement = new ResourceDictionary
            {
                Source = new Uri(string.Format("Themes/{0}.xaml", theme), UriKind.Relative)
            };

            if (existing == null)
                dictionaries.Insert(0, replacement);
            else
                dictionaries[dictionaries.IndexOf(existing)] = replacement;

            CurrentTheme = theme;

            if (save)
                SaveTheme(theme);
        }

        private static bool IsThemeDictionary(ResourceDictionary dictionary)
        {
            if (dictionary.Source == null)
                return false;

            var source = dictionary.Source.OriginalString.Replace('\\', '/');
            return source.StartsWith(ThemeDictionaryPrefix, StringComparison.OrdinalIgnoreCase) &&
                   (source.EndsWith("Black.xaml", StringComparison.OrdinalIgnoreCase) ||
                    source.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase));
        }

        private static void SaveTheme(AppTheme theme)
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(SettingsPath, theme.ToString());
            }
            catch
            {
                // 테마 전환은 유지하고 설정 저장 실패만 무시한다.
            }
        }
    }
}

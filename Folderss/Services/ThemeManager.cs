using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace Folderss.Services
{
    public enum AppTheme
    {
        Black,
        Light,
        Nord,
        Catppuccin,
        Solarized,
        Dracula,
        GitHub
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
            if (!source.StartsWith(ThemeDictionaryPrefix, StringComparison.OrdinalIgnoreCase))
                return false;

            return Enum.GetNames(typeof(AppTheme))
                       .Any(name => source.EndsWith(name + ".xaml", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 현재 테마를 파일에 기록한다. 실패는 예외로 알린다 — 설정 창의 저장에서 다른 설정과 함께 실패를 보고하는 용도.
        /// </summary>
        public static void SaveCurrentTheme()
        {
            WriteTheme(CurrentTheme);
        }

        private static void WriteTheme(AppTheme theme)
        {
            SettingsFile.WriteAllText(SettingsPath, theme.ToString());
        }

        private static void SaveTheme(AppTheme theme)
        {
            try
            {
                WriteTheme(theme);
            }
            catch
            {
                // 메뉴·라디오 버튼으로 바꾼 테마 전환은 유지하고 저장 실패만 무시한다.
                // 설정 창의 저장에서는 SaveCurrentTheme로 다시 써서 실패를 사용자에게 알린다.
            }
        }
    }
}

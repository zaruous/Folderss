using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Resources;
using System.Windows;
using System.Windows.Media;

namespace Folderss.Services
{
    /// <summary>
    /// <c>Themes/</c> 폴더의 테마 XAML 하나. 빌드에 포함된 파일을 실행 시 찾아 만든다(<see cref="ThemeManager.Themes"/>).
    /// </summary>
    public sealed class ThemeInfo
    {
        /// <summary>theme.txt·플러그인 설정에 쓰는 이름. 테마 XAML의 <c>ThemeName</c>, 없으면 파일 이름(소문자).</summary>
        public string Name { get; }
        public string DisplayName { get; }
        public int Order { get; }
        public ResourceDictionary Resources { get; }

        /// <summary>패널 바탕이 밝은 테마. 뷰어의 코드 하이라이트·Mermaid·Monaco 색을 고를 때 쓴다.</summary>
        public bool IsLight
        {
            get
            {
                var c = GetColor("PanelBackgroundColor");
                return 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B > 128;
            }
        }

        internal ThemeInfo(string name, string displayName, int order, ResourceDictionary resources)
        {
            Name = name;
            DisplayName = displayName;
            Order = order;
            Resources = resources;
        }

        public Color GetColor(string key)
        {
            return (Color)Resources[key];
        }
    }

    public static class ThemeManager
    {
        private const string DefaultThemeName = "Black";
        private const string ThemeResourcePrefix = "themes/";
        private const string ThemeResourceSuffix = ".baml";

        // 테마가 정의해야 하는 색. 없으면 화면·뷰어 색을 만들 수 없어 목록에서 뺀다 (전체 키는 Black.xaml 기준, 테스트로 확인).
        private static readonly string[] RequiredColorKeys =
        {
            "WindowBackgroundColor", "PanelBackgroundColor", "SurfaceBackgroundColor", "ControlBackgroundColor",
            "ControlHoverColor", "ControlPressedColor", "BorderColor", "PrimaryTextColor", "SecondaryTextColor",
            "DisabledTextColor", "AccentColor", "AccentHoverColor", "SelectionColor", "RowHoverColor",
        };

        private static List<ThemeInfo> _themes;

        public static ThemeInfo CurrentTheme { get; private set; }

        /// <summary>빌드에 포함된 테마 목록(ThemeOrder, 표시 이름 순). 처음 읽을 때 한 번 찾는다.</summary>
        public static IReadOnlyList<ThemeInfo> Themes
        {
            get
            {
                if (_themes == null)
                    _themes = DiscoverThemes();
                return _themes;
            }
        }

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

        public static ThemeInfo Find(string name)
        {
            return Themes.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public static void ApplySavedTheme()
        {
            ThemeInfo theme = null;
            try
            {
                if (File.Exists(SettingsPath))
                    theme = Find(File.ReadAllText(SettingsPath).Trim());
            }
            catch
            {
                theme = null;
            }

            ApplyTheme(theme ?? Find(DefaultThemeName) ?? Themes.First(), false);
        }

        public static void ApplyTheme(ThemeInfo theme, bool save = true)
        {
            var dictionaries = Application.Current.Resources.MergedDictionaries;
            var existing = dictionaries.FirstOrDefault(IsThemeDictionary);

            if (existing == null)
                dictionaries.Insert(0, theme.Resources);
            else if (!ReferenceEquals(existing, theme.Resources))
                dictionaries[dictionaries.IndexOf(existing)] = theme.Resources;

            CurrentTheme = theme;

            if (save)
                SaveTheme(theme);
        }

        // App.xaml이 처음 넣는 Black.xaml, 이후 넣은 테마 모두 "Themes/"에서 왔고 Controls.xaml이 아니다.
        private static bool IsThemeDictionary(ResourceDictionary dictionary)
        {
            if (Themes.Any(t => ReferenceEquals(t.Resources, dictionary)))
                return true;
            if (dictionary.Source == null)
                return false;

            var source = dictionary.Source.OriginalString.Replace('\\', '/');
            return source.StartsWith("Themes/", StringComparison.OrdinalIgnoreCase)
                && !source.EndsWith("/Controls.xaml", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 어셈블리에 컴파일된 <c>themes/*.baml</c>(Controls 제외)을 읽어 테마 목록을 만든다.
        /// 읽지 못하거나 필수 색이 빠진 파일은 건너뛴다 — 테마 하나 때문에 앱이 시작되지 않는 일을 막는다.
        /// </summary>
        private static List<ThemeInfo> DiscoverThemes()
        {
            var themes = new List<ThemeInfo>();
            var assembly = typeof(ThemeManager).Assembly;
            using (var stream = assembly.GetManifestResourceStream(assembly.GetName().Name + ".g.resources"))
            using (var reader = new ResourceReader(stream))
            {
                foreach (DictionaryEntry entry in reader)
                {
                    var fileName = ThemeFileName((string)entry.Key);
                    if (fileName == null)
                        continue;

                    var theme = TryLoadTheme(fileName);
                    if (theme != null && themes.All(t => !string.Equals(t.Name, theme.Name, StringComparison.OrdinalIgnoreCase)))
                        themes.Add(theme);
                }
            }

            return themes.OrderBy(t => t.Order).ThenBy(t => t.DisplayName, StringComparer.CurrentCulture).ToList();
        }

        /// <summary>리소스 키(예: <c>themes/tokyonight.baml</c>)가 테마면 파일 이름(<c>tokyonight</c>), 아니면 null.</summary>
        internal static string ThemeFileName(string resourceKey)
        {
            if (!resourceKey.StartsWith(ThemeResourcePrefix, StringComparison.OrdinalIgnoreCase)
                || !resourceKey.EndsWith(ThemeResourceSuffix, StringComparison.OrdinalIgnoreCase))
                return null;

            var name = resourceKey.Substring(ThemeResourcePrefix.Length,
                resourceKey.Length - ThemeResourcePrefix.Length - ThemeResourceSuffix.Length);
            if (name.Length == 0 || name.Contains('/') || string.Equals(name, "controls", StringComparison.OrdinalIgnoreCase))
                return null;
            return name;
        }

        private static ThemeInfo TryLoadTheme(string fileName)
        {
            ResourceDictionary resources;
            try
            {
                resources = new ResourceDictionary
                {
                    Source = new Uri("Themes/" + fileName + ".xaml", UriKind.Relative)
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine("테마를 읽지 못해 건너뜀: " + fileName + " — " + ex.Message);
                return null;
            }

            var missing = RequiredColorKeys.Where(key => !(resources[key] is Color)).ToList();
            if (missing.Count > 0)
            {
                Debug.WriteLine("필수 색이 없어 테마를 건너뜀: " + fileName + " — " + string.Join(", ", missing));
                return null;
            }

            var name = resources["ThemeName"] as string;
            if (string.IsNullOrWhiteSpace(name))
                name = fileName;
            var displayName = resources["ThemeDisplayName"] as string;
            if (string.IsNullOrWhiteSpace(displayName))
                displayName = name;
            var order = resources["ThemeOrder"] is int value ? value : int.MaxValue;

            return new ThemeInfo(name, displayName, order, resources);
        }

        /// <summary>
        /// 현재 테마를 파일에 기록한다. 실패는 예외로 알린다 — 설정 창의 저장에서 다른 설정과 함께 실패를 보고하는 용도.
        /// </summary>
        public static void SaveCurrentTheme()
        {
            WriteTheme(CurrentTheme);
        }

        private static void WriteTheme(ThemeInfo theme)
        {
            SettingsFile.WriteAllText(SettingsPath, theme.Name);
        }

        private static void SaveTheme(ThemeInfo theme)
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

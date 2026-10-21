using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>
    /// Themes/ 폴더의 테마는 실행 시 자동으로 목록에 올라오므로(ThemeManager), 파일만 추가했을 때 키가 빠져
    /// 일부 화면 색이 비는 일을 여기서 막는다. 기준은 Black.xaml. 테마 정보 키(ThemeName 등)는 생략할 수 있다.
    /// </summary>
    public sealed class ThemeFileTests
    {
        private static readonly XNamespace XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";
        private static readonly string[] OptionalKeys = { "ThemeName", "ThemeDisplayName", "ThemeOrder" };

        private static string ThemesDirectory
        {
            get { return Path.Combine(AppContext.BaseDirectory, "Themes"); }
        }

        private static IEnumerable<string> ThemeFiles()
        {
            return Directory.GetFiles(ThemesDirectory, "*.xaml")
                .Where(path => !string.Equals(Path.GetFileName(path), "Controls.xaml", StringComparison.OrdinalIgnoreCase));
        }

        private static XElement[] Entries(string path)
        {
            return XDocument.Load(path).Root.Elements().Where(e => e.Attribute(XamlNs + "Key") != null).ToArray();
        }

        private static HashSet<string> RequiredKeys(string path)
        {
            return new HashSet<string>(Entries(path)
                .Select(e => e.Attribute(XamlNs + "Key").Value)
                .Where(key => !OptionalKeys.Contains(key)));
        }

        [Fact]
        public void EveryTheme_DefinesSameKeysAsBlack()
        {
            var expected = RequiredKeys(Path.Combine(ThemesDirectory, "Black.xaml"));
            Assert.True(expected.Count > 0);

            foreach (var path in ThemeFiles())
            {
                var actual = RequiredKeys(path);
                var missing = expected.Except(actual).OrderBy(k => k).ToList();
                var extra = actual.Except(expected).OrderBy(k => k).ToList();
                Assert.True(missing.Count == 0 && extra.Count == 0,
                    Path.GetFileName(path) + " — 빠진 키: " + string.Join(", ", missing) + " / 추가 키: " + string.Join(", ", extra));
            }
        }

        [Fact]
        public void ThemeNames_AreUnique()
        {
            var names = ThemeFiles().Select(path =>
            {
                var nameEntry = Entries(path).FirstOrDefault(e => e.Attribute(XamlNs + "Key").Value == "ThemeName");
                return nameEntry != null ? nameEntry.Value.Trim() : Path.GetFileNameWithoutExtension(path);
            }).ToList();

            var duplicates = names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.True(duplicates.Count == 0, "같은 테마 이름: " + string.Join(", ", duplicates));
            Assert.Contains("Black", names);
        }
    }
}

using Folderss.Services;
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
    /// 실행 폴더 Themes\에서 읽는 파일 검사(ThemeFileValidator)도 함께 확인한다.
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
    
        // ── 실행 폴더 Themes\ 검사 ──────────────────────────────────────────────

        private const string Header = "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" "
            + "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:sys=\"clr-namespace:System;assembly=mscorlib\">";

        [Fact]
        public void Validator_AcceptsEveryBuiltInTheme()
        {
            // 기본 테마를 실행 폴더 Themes\에 복사해 고쳐 쓸 수 있어야 한다.
            foreach (var path in ThemeFiles())
                Assert.True(ThemeFileValidator.Validate(File.ReadAllText(path)) == null,
                    Path.GetFileName(path) + ": " + ThemeFileValidator.Validate(File.ReadAllText(path)));
        }

        [Theory]
        [InlineData("<Color x:Key=\"A\">#112233</Color>")]
        [InlineData("<sys:String x:Key=\"ThemeName\">Mine</sys:String><sys:Int32 x:Key=\"ThemeOrder\">5</sys:Int32>")]
        [InlineData("<Color x:Key=\"A\">#112233</Color><SolidColorBrush x:Key=\"B\" Color=\"{StaticResource A}\"/>")]
        [InlineData("<SolidColorBrush x:Key=\"{x:Static SystemColors.WindowBrushKey}\" Color=\"#000000\"/>")]
        public void Validator_AcceptsThemeContent(string body)
        {
            Assert.Null(ThemeFileValidator.Validate(Header + body + "</ResourceDictionary>"));
        }

        [Theory]
        // 프로그램 실행이 가능한 요소
        [InlineData("<ObjectDataProvider x:Key=\"P\" ObjectType=\"{x:Type sys:Diagnostics.Process}\" MethodName=\"Start\"/>")]
        // 다른 파일을 끌어오는 병합
        [InlineData("<ResourceDictionary.MergedDictionaries><ResourceDictionary Source=\"x.xaml\"/></ResourceDictionary.MergedDictionaries>")]
        // SystemColors 외 정적 멤버
        [InlineData("<SolidColorBrush x:Key=\"B\" Color=\"{x:Static sys:Environment.NewLine}\"/>")]
        [InlineData("<SolidColorBrush x:Key=\"B\" Color=\"{Binding X}\"/>")]
        [InlineData("<Color x:Key=\"A\" x:Class=\"Evil\">#112233</Color>")]
        public void Validator_RejectsUnsafeContent(string body)
        {
            Assert.NotNull(ThemeFileValidator.Validate(Header + body + "</ResourceDictionary>"));
        }

        [Fact]
        public void Validator_RejectsDtdAndOtherRoot()
        {
            Assert.NotNull(ThemeFileValidator.Validate("<!DOCTYPE x [<!ENTITY e \"a\">]>" + Header + "</ResourceDictionary>"));
            Assert.NotNull(ThemeFileValidator.Validate("<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"/>"));
            Assert.NotNull(ThemeFileValidator.Validate("not xml"));
        }
    }
}

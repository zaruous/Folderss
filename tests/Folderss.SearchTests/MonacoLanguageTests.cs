using Folderss.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>
    /// Monaco 뷰어 언어 결정(#82): 정적 표 ↔ 번들 등록 파일 일치, 대표 확장자·파일 이름 매핑, 우선순위
    /// (파일별 선택 → 사용자 지정 → 예외 사전 → 등록 정보 → plaintext), monaco-language.json 저장 왕복·개수 제한·깨진 JSON.
    /// </summary>
    public sealed class MonacoLanguageTests
    {
        private static readonly KeyValuePair<string, string>[] NoMappings = new KeyValuePair<string, string>[0];

        private static string Resolve(string fileName)
        {
            return MonacoLanguages.Resolve(fileName, null, NoMappings);
        }

        // ── 정적 표 ↔ 번들 Monaco ───────────────────────────────────────

        private static string MonacoDirectory => Path.Combine(AppContext.BaseDirectory, "Monaco");

        private static string[] ReadArray(string segment, string name)
        {
            var match = Regex.Match(segment, "\\b" + name + ":\\[([^\\]]*)\\]");
            if (!match.Success)
                return new string[0];
            return Regex.Matches(match.Groups[1].Value, "\"([^\"]*)\"").Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
        }

        /// <summary>basic-languages/monaco.contribution.js의 <c>i({id:"…",…})</c> 등록을 순서대로 읽는다.</summary>
        private static List<MonacoLanguageInfo> ReadBasicLanguages()
        {
            var source = File.ReadAllText(Path.Combine(MonacoDirectory, "basic-languages", "monaco.contribution.js"));
            var result = new List<MonacoLanguageInfo>();
            foreach (Match match in Regex.Matches(source, "\\bi\\(\\{id:\"([^\"]+)\"(.*?)loader:"))
            {
                var segment = match.Groups[2].Value;
                result.Add(new MonacoLanguageInfo(match.Groups[1].Value,
                    ReadArray(segment, "aliases"), ReadArray(segment, "extensions"),
                    ReadArray(segment, "filenames"), ReadArray(segment, "filenamePatterns")));
            }
            return result;
        }

        [Fact]
        public void StaticTable_MatchesBundledBasicLanguages()
        {
            var bundled = ReadBasicLanguages();
            Assert.True(bundled.Count > 70, "basic-languages 등록을 읽지 못함: " + bundled.Count);

            // 정적 표에서 basic-languages 부분만(앞의 plaintext, 뒤의 json·jsonl 제외) — 순서까지 같아야 한다(같은 확장자는 먼저 등록된 언어가 이김).
            var table = MonacoLanguages.Languages.Where(l => l.Id != "plaintext" && l.Id != "json" && l.Id != "jsonl").ToList();
            Assert.Equal(bundled.Select(l => l.Id), table.Select(l => l.Id));
            for (var i = 0; i < bundled.Count; i++)
            {
                Assert.Equal(bundled[i].Extensions, table[i].Extensions);
                Assert.Equal(bundled[i].Filenames, table[i].Filenames);
                Assert.Equal(bundled[i].FilenamePatterns, table[i].FilenamePatterns);
                Assert.Equal(bundled[i].Aliases, table[i].Aliases);
            }
        }

        [Fact]
        public void StaticTable_JsonAndJsonlMatchBundledRegistrations()
        {
            var jsonSource = Directory.GetFiles(MonacoDirectory, "monaco.contribution-*.js")
                .Select(File.ReadAllText)
                .FirstOrDefault(s => s.Contains("id:\"json\","));
            Assert.NotNull(jsonSource);
            var jsonSegment = Regex.Match(jsonSource, "id:\"json\",(.*?)\\}").Groups[1].Value;
            Assert.Equal(ReadArray(jsonSegment, "extensions"), MonacoLanguages.Find("json").Extensions);

            var jsonl = File.ReadAllText(Path.Combine(MonacoDirectory, "monaco-jsonl.js"));
            var jsonlExtensions = Regex.Matches(
                    Regex.Match(jsonl, "id: 'jsonl', extensions: \\[([^\\]]*)\\]").Groups[1].Value, "'([^']*)'")
                .Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
            Assert.NotEmpty(jsonlExtensions);
            Assert.Equal(jsonlExtensions, MonacoLanguages.Find("jsonl").Extensions);
        }

        [Fact]
        public void StaticTable_IdsAreUniqueAndValid()
        {
            var ids = MonacoLanguages.Languages.Select(l => l.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(ids, id => Assert.True(MonacoLanguages.IsValidLanguageId(id), id));
            Assert.All(MonacoLanguages.Exceptions.Values, id => Assert.NotNull(MonacoLanguages.Find(id)));
        }

        // ── 대표 확장자·파일 이름 ─────────────────────────────────────

        [Theory]
        [InlineData("main.go", "go")]
        [InlineData("lib.rs", "rust")]
        [InlineData("App.kt", "kotlin")]
        [InlineData("build.gradle.kts", "kotlin")]
        [InlineData("app.rb", "ruby")]
        [InlineData("index.php", "php")]
        [InlineData("site.scss", "scss")]
        [InlineData("setup.ini", "ini")]
        [InlineData("init.lua", "lua")]
        [InlineData("Module1.vb", "vb")]
        [InlineData("Page.cshtml", "razor")]
        [InlineData("main.swift", "swift")]
        [InlineData("Program.cs", "csharp")]
        [InlineData("MainWindow.xaml", "xml")]
        [InlineData("Folderss.csproj", "xml")]
        [InlineData("app.config", "xml")]
        [InlineData("data.json", "json")]
        [InlineData("events.jsonl", "jsonl")]
        [InlineData("events.ndjson", "jsonl")]
        [InlineData("index.html", "html")]
        [InlineData("style.css", "css")]
        [InlineData("app.ts", "typescript")]
        [InlineData("App.tsx", "typescript")]
        [InlineData("app.mjs", "javascript")]
        [InlineData("script.py", "python")]
        [InlineData("run.sh", "shell")]
        [InlineData("build.bat", "bat")]
        [InlineData("build.cmd", "bat")]
        [InlineData("profile.ps1", "powershell")]
        [InlineData("docker-compose.yml", "yaml")]
        [InlineData("query.sql", "sql")]
        [InlineData("README.md", "markdown")]
        [InlineData("main.tf", "hcl")]
        [InlineData("schema.graphql", "graphql")]
        [InlineData("api.proto", "proto")]
        [InlineData("theme.liquid", "liquid")]
        [InlineData("page.html.liquid", "liquid")]
        [InlineData("main.c", "c")]
        [InlineData("main.cpp", "cpp")]
        [InlineData("notes.txt", "plaintext")]
        [InlineData("app.log", "plaintext")]
        [InlineData("data.unknownext", "plaintext")]
        [InlineData("NOEXTENSION", "plaintext")]
        public void Resolve_MapsBundledExtensions(string fileName, string expected)
        {
            Assert.Equal(expected, Resolve(fileName));
        }

        [Theory]
        [InlineData("Dockerfile", "dockerfile")]
        [InlineData("dockerfile", "dockerfile")]
        [InlineData("api.dockerfile", "dockerfile")]
        [InlineData("Gemfile", "ruby")]
        [InlineData("Rakefile", "ruby")]
        [InlineData(".editorconfig", "ini")]
        [InlineData(".gitattributes", "ini")]
        [InlineData("config", "ini")]
        [InlineData(@"C:\repo\.git\config", "ini")]
        [InlineData("Jakefile", "javascript")]
        public void Resolve_MapsBundledFileNames_CaseInsensitive(string fileName, string expected)
        {
            Assert.Equal(expected, Resolve(fileName));
        }

        [Fact]
        public void Resolve_UpperCaseExtension()
        {
            Assert.Equal("go", Resolve(@"D:\src\MAIN.GO"));
            Assert.Equal("st", Resolve("Motion.tcpou"));   // 등록은 .TcPOU
        }

        [Fact]
        public void Resolve_SameExtensionInTwoLanguages_FirstRegisteredWins()
        {
            // .pp는 pascal과 ruby(Puppet) 둘 다 등록 — getLanguages() 순서상 pascal이 먼저.
            Assert.Equal("pascal", Resolve("unit.pp"));
        }

        /// <summary>#82 이전 MonacoViewer.ExtToLanguage(23개)가 고르던 언어가 그대로 유지된다(회귀 방지).</summary>
        [Theory]
        [InlineData(".cs", "csharp")]
        [InlineData(".js", "javascript")]
        [InlineData(".ts", "typescript")]
        [InlineData(".json", "json")]
        [InlineData(".jsonl", "jsonl")]
        [InlineData(".xml", "xml")]
        [InlineData(".xaml", "xml")]
        [InlineData(".html", "html")]
        [InlineData(".css", "css")]
        [InlineData(".py", "python")]
        [InlineData(".java", "java")]
        [InlineData(".cpp", "cpp")]
        [InlineData(".c", "c")]
        [InlineData(".h", "cpp")]
        [InlineData(".sh", "shell")]
        [InlineData(".bat", "bat")]
        [InlineData(".ps1", "powershell")]
        [InlineData(".yaml", "yaml")]
        [InlineData(".yml", "yaml")]
        [InlineData(".sql", "sql")]
        [InlineData(".md", "markdown")]
        [InlineData(".log", "plaintext")]
        [InlineData(".txt", "plaintext")]
        public void Resolve_KeepsPreviousDictionaryResults(string extension, string expected)
        {
            Assert.Equal(expected, Resolve("file" + extension));
        }

        // ── 우선순위 ───────────────────────────────────────────────

        [Fact]
        public void Priority_PerFile_Then_User_Then_Exception_Then_Registry_Then_PlainText()
        {
            var user = new[] { new KeyValuePair<string, string>(".h", "c") };

            Assert.Equal("rust", MonacoLanguages.Resolve("a.h", "rust", user));        // 파일별 선택
            Assert.Equal("c", MonacoLanguages.Resolve("a.h", null, user));             // 사용자 지정
            Assert.Equal("cpp", MonacoLanguages.Resolve("a.h", null, NoMappings));      // 예외 사전(.h → cpp)
            Assert.Equal("go", MonacoLanguages.Resolve("a.go", null, NoMappings));      // 등록 정보
            Assert.Equal("plaintext", MonacoLanguages.Resolve("a.zzz", null, NoMappings));
        }

        [Fact]
        public void ResolveForced_NullWhenEditorShouldDecide()
        {
            Assert.Null(MonacoLanguages.ResolveForced("main.go", null, NoMappings));
            Assert.Equal("cpp", MonacoLanguages.ResolveForced("a.h", null, NoMappings));
            Assert.Equal("php", MonacoLanguages.ResolveForced("a.go", "php", NoMappings));
        }

        [Fact]
        public void ResolveForced_InvalidPerFileIdIsIgnored()
        {
            Assert.Null(MonacoLanguages.ResolveForced("main.go", "bad id!", NoMappings));
            Assert.Null(MonacoLanguages.ResolveForced("main.go", "", NoMappings));
        }

        [Fact]
        public void UserMappings_FirstMatchWins_AndSupportsNameAndGlob()
        {
            var user = new[]
            {
                new KeyValuePair<string, string>("Dockerfile.*", "dockerfile"),
                new KeyValuePair<string, string>(".inc", "php"),
                new KeyValuePair<string, string>("*.conf", "ini"),
                new KeyValuePair<string, string>("Makefile", "shell"),
                new KeyValuePair<string, string>(".conf", "yaml"),   // 위 *.conf가 먼저
            };

            Assert.Equal("dockerfile", MonacoLanguages.Resolve("Dockerfile.dev", null, user));
            Assert.Equal("php", MonacoLanguages.Resolve(@"C:\site\header.INC", null, user));
            Assert.Equal("ini", MonacoLanguages.Resolve("nginx.conf", null, user));
            Assert.Equal("shell", MonacoLanguages.Resolve("makefile", null, user));
            Assert.Equal("plaintext", MonacoLanguages.Resolve("Makefile.am", null, user));
        }

        [Theory]
        [InlineData("a.inc", ".inc", true)]
        [InlineData("a.INC", ".inc", true)]
        [InlineData("a.inc.bak", ".inc", false)]
        [InlineData("Makefile", "Makefile", true)]
        [InlineData("GNUmakefile", "Makefile", false)]
        [InlineData("Dockerfile.prod", "Dockerfile*", true)]
        [InlineData("x.conf", "*.con?", true)]
        [InlineData("x.conf", "", false)]
        [InlineData("x.conf", "dir\\x.conf", false)]
        public void MatchesPattern(string fileName, string pattern, bool expected)
        {
            Assert.Equal(expected, MonacoLanguages.MatchesPattern(fileName, pattern));
        }

        // ── monaco-language.json ──────────────────────────────────

        private static string TempPath()
        {
            return Path.Combine(Path.GetTempPath(), "folderss-monaco-language-" + Guid.NewGuid().ToString("N") + ".json");
        }

        [Fact]
        public void Settings_MappingsAndFiles_RoundTrip()
        {
            var path = TempPath();
            try
            {
                var settings = new MonacoLanguageSettings(path);
                settings.SaveMappings(new[]
                {
                    new KeyValuePair<string, string>(".inc", "php"),
                    new KeyValuePair<string, string>("Makefile", "shell"),
                    new KeyValuePair<string, string>(".INC", "ini"),           // 같은 패턴은 먼저 온 것만
                    new KeyValuePair<string, string>("", "ini"),               // 빈 패턴 버림
                    new KeyValuePair<string, string>(".bad", "not valid id"),  // 잘못된 id 버림
                });
                settings.RememberAndSave(@"C:\work\a.txt", "markdown");

                var loaded = new MonacoLanguageSettings(path);
                Assert.Equal(new[] { ".inc", "Makefile" }, loaded.Mappings.Select(m => m.Key));
                Assert.Equal(new[] { "php", "shell" }, loaded.Mappings.Select(m => m.Value));
                Assert.Equal("markdown", loaded.GetRemembered(@"C:\work\a.txt"));
                Assert.Equal("markdown", loaded.GetRemembered(@"c:\WORK\A.TXT"));
                Assert.Equal("markdown", loaded.ResolveForced(@"C:\work\a.txt"));
                Assert.Equal("php", loaded.ResolveForced(@"C:\work\b.inc"));
                Assert.Null(loaded.ResolveForced(@"C:\work\b.go"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Settings_AutoClearsRemembered()
        {
            var path = TempPath();
            try
            {
                var settings = new MonacoLanguageSettings(path);
                settings.RememberAndSave(@"C:\work\a.h", "c");
                Assert.Equal("c", new MonacoLanguageSettings(path).GetRemembered(@"C:\work\a.h"));

                settings.RememberAndSave(@"C:\work\a.h", null);   // "자동"
                var loaded = new MonacoLanguageSettings(path);
                Assert.Null(loaded.GetRemembered(@"C:\work\a.h"));
                Assert.Equal("cpp", loaded.ResolveForced(@"C:\work\a.h"));   // 예외 사전으로 돌아감
                Assert.False(settings.Remember(@"C:\work\a.h", null));      // 이미 없음 → 바뀐 것 없음
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Settings_KeepsMostRecentFilesUpToLimit()
        {
            var settings = new MonacoLanguageSettings(TempPath());
            for (var i = 0; i < MonacoLanguageSettings.MaxRememberedFiles + 25; i++)
                settings.Remember(@"C:\work\f" + i + ".txt", "ini");

            Assert.Equal(MonacoLanguageSettings.MaxRememberedFiles, settings.RememberedCount);
            Assert.Null(settings.GetRemembered(@"C:\work\f0.txt"));
            Assert.Null(settings.GetRemembered(@"C:\work\f24.txt"));
            Assert.Equal("ini", settings.GetRemembered(@"C:\work\f25.txt"));

            // 다시 고른 파일은 가장 최근으로 옮겨져 다음에 밀려나지 않는다.
            settings.Remember(@"C:\work\f25.txt", "go");
            settings.Remember(@"C:\work\new.txt", "go");
            Assert.Equal("go", settings.GetRemembered(@"C:\work\f25.txt"));
            Assert.Null(settings.GetRemembered(@"C:\work\f26.txt"));
        }

        [Theory]
        [InlineData("{ not json")]
        [InlineData("[]")]
        [InlineData("{\"mappings\": 3, \"files\": \"x\"}")]
        [InlineData("{\"mappings\": [1, {\"pattern\": 2}], \"files\": [{\"path\": \"C:\\\\a.txt\"}]}")]
        public void Settings_BrokenJson_FallsBackToDefaults(string json)
        {
            var path = TempPath();
            try
            {
                File.WriteAllText(path, json);
                var settings = new MonacoLanguageSettings(path);
                Assert.Empty(settings.Mappings);
                Assert.Equal(0, settings.RememberedCount);

                // 다음 저장은 새로 쓴다.
                settings.SaveMappings(new[] { new KeyValuePair<string, string>(".inc", "php") });
                Assert.Single(new MonacoLanguageSettings(path).Mappings);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Settings_MissingFile_IsEmpty()
        {
            var settings = new MonacoLanguageSettings(TempPath());
            Assert.Empty(settings.Mappings);
            Assert.Null(settings.ResolveForced(@"C:\a\main.go"));
            Assert.Equal("cpp", settings.ResolveForced(@"C:\a\x.h"));
        }
    }
}

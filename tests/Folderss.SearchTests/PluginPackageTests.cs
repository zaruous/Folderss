using Folderss.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>플러그인 zip 등록·검증·압축 해제(Zip Slip 차단)와 플러그인 설정 저장.</summary>
    public sealed class PluginPackageTests : IDisposable
    {
        private const string ValidManifest =
            "{ \"id\": \"sample.hello\", \"name\": \"Hello\", \"version\": \"1.0.0\", \"assembly\": \"Hello.dll\", \"type\": \"Hello.Plugin\", \"hasSettings\": true }";

        private readonly string _root;

        public PluginPackageTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "folderss-plugin-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private string MakeZip(string name, params (string Entry, string Content)[] entries)
        {
            var path = Path.Combine(_root, name);
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                foreach (var (entry, content) in entries)
                {
                    using (var writer = new StreamWriter(archive.CreateEntry(entry).Open(), new UTF8Encoding(false)))
                        writer.Write(content);
                }
            }
            return path;
        }

        [Fact]
        public void ReadManifest_Valid_ReadsFieldsCaseInsensitive()
        {
            var zip = MakeZip("a.zip", ("plugin.json", ValidManifest), ("Hello.dll", "x"));

            var manifest = PluginPackage.ReadManifest(zip);

            Assert.Equal("sample.hello", manifest.Id);
            Assert.Equal("Hello", manifest.DisplayName);
            Assert.Equal("Hello.Plugin", manifest.Type);
            Assert.True(manifest.HasSettings);
            Assert.Equal(zip, manifest.PackagePath);
        }

        [Fact]
        public void ReadManifest_NoManifest_Throws()
        {
            var zip = MakeZip("a.zip", ("Hello.dll", "x"));
            Assert.Throws<InvalidDataException>(() => PluginPackage.ReadManifest(zip));
        }

        [Fact]
        public void ReadManifest_BrokenJson_Throws()
        {
            var zip = MakeZip("a.zip", ("plugin.json", "{ id: "), ("Hello.dll", "x"));
            Assert.Throws<InvalidDataException>(() => PluginPackage.ReadManifest(zip));
        }

        [Theory]
        [InlineData("../evil")]
        [InlineData("a b")]
        [InlineData("")]
        public void ReadManifest_InvalidId_Throws(string id)
        {
            var zip = MakeZip("a.zip", ("plugin.json", ValidManifest.Replace("sample.hello", id)), ("Hello.dll", "x"));
            Assert.Throws<InvalidDataException>(() => PluginPackage.ReadManifest(zip));
        }

        [Theory]
        [InlineData("../Hello.dll")]
        [InlineData("Hello.exe")]
        public void ReadManifest_InvalidAssemblyPath_Throws(string assembly)
        {
            var zip = MakeZip("a.zip", ("plugin.json", ValidManifest.Replace("Hello.dll", assembly)), ("Hello.dll", "x"));
            Assert.Throws<InvalidDataException>(() => PluginPackage.ReadManifest(zip));
        }

        [Fact]
        public void ReadManifest_AssemblyMissingFromZip_Throws()
        {
            var zip = MakeZip("a.zip", ("plugin.json", ValidManifest));
            Assert.Throws<InvalidDataException>(() => PluginPackage.ReadManifest(zip));
        }

        [Fact]
        public void ReadManifest_AssemblyInSubfolder_Accepted()
        {
            var zip = MakeZip("a.zip", ("plugin.json", ValidManifest.Replace("Hello.dll", "bin/Hello.dll")), ("bin/Hello.dll", "x"));
            Assert.Equal("bin/Hello.dll", PluginPackage.ReadManifest(zip).Assembly);
        }

        [Fact]
        public void ExtractSafely_EntryEscapingDestination_ThrowsAndWritesNothingOutside()
        {
            var zip = MakeZip("a.zip", ("../escaped.txt", "x"));
            var destination = Path.Combine(_root, "out");
            Directory.CreateDirectory(destination);

            Assert.Throws<InvalidDataException>(() => PluginPackage.ExtractSafely(zip, destination));
            Assert.False(File.Exists(Path.Combine(_root, "escaped.txt")));
        }

        [Fact]
        public void Extract_SameContent_ReusesFolder_DifferentContent_NewFolder()
        {
            var zip = MakeZip("a.zip", ("plugin.json", ValidManifest), ("Hello.dll", "v1"), ("res/a.txt", "r"));
            var extractRoot = Path.Combine(_root, "extracted");
            var manifest = PluginPackage.ReadManifest(zip);

            var first = PluginPackage.Extract(manifest, extractRoot);
            File.WriteAllText(Path.Combine(first, "Hello.dll"), "touched");
            var second = PluginPackage.Extract(manifest, extractRoot);

            Assert.Equal(first, second);
            Assert.Equal("touched", File.ReadAllText(Path.Combine(second, "Hello.dll")));
            Assert.Equal("r", File.ReadAllText(Path.Combine(first, "res", "a.txt")));

            var zip2 = MakeZip("b.zip", ("plugin.json", ValidManifest), ("Hello.dll", "v2"));
            var third = PluginPackage.Extract(PluginPackage.ReadManifest(zip2), extractRoot);
            Assert.NotEqual(first, third);
            Assert.Equal("v2", File.ReadAllText(Path.Combine(third, "Hello.dll")));
        }

        [Fact]
        public void Install_CopiesAsIdZip_AndListReturnsIt_SkippingBadZips()
        {
            var source = MakeZip("download.zip", ("plugin.json", ValidManifest), ("Hello.dll", "x"));
            var plugins = Path.Combine(_root, "plugins");

            var installed = PluginPackage.Install(source, plugins);
            File.WriteAllText(Path.Combine(plugins, "broken.zip"), "not a zip");
            File.Copy(installed.PackagePath, Path.Combine(plugins, "renamed.zip"));

            var errors = new List<string>();
            var list = PluginPackage.List(plugins, errors);

            Assert.Equal(Path.Combine(plugins, "sample.hello.zip"), installed.PackagePath);
            Assert.True(File.Exists(source));
            var only = Assert.Single(list);
            Assert.Equal("sample.hello", only.Id);
            Assert.Equal(2, errors.Count);
        }

        [Fact]
        public void Install_SameId_Replaces()
        {
            var plugins = Path.Combine(_root, "plugins");
            PluginPackage.Install(MakeZip("v1.zip", ("plugin.json", ValidManifest), ("Hello.dll", "x")), plugins);
            PluginPackage.Install(MakeZip("v2.zip", ("plugin.json", ValidManifest.Replace("1.0.0", "2.0.0")), ("Hello.dll", "x")), plugins);

            Assert.Equal("2.0.0", Assert.Single(PluginPackage.List(plugins)).Version);
        }

        [Fact]
        public void List_MissingDirectory_Empty()
        {
            Assert.Empty(PluginPackage.List(Path.Combine(_root, "none")));
        }

        [Fact]
        public void SettingsStore_RoundTripsAcrossInstances_NullRemoves()
        {
            var path = Path.Combine(_root, "data", "settings.json");
            var store = new PluginSettingsStore(path);
            Assert.Null(store.Get("startPath"));

            store.Set("startPath", @"C:\work");
            store.Set("other", "1");
            store.Set("other", null);

            var reloaded = new PluginSettingsStore(path);
            Assert.Equal(@"C:\work", reloaded.Get("startPath"));
            Assert.Null(reloaded.Get("other"));
            Assert.Single(reloaded.GetAll());
        }

        [Fact]
        public void SettingsStore_CorruptFile_StartsEmptyWithLoadError()
        {
            var path = Path.Combine(_root, "settings.json");
            File.WriteAllText(path, "{ broken");

            var store = new PluginSettingsStore(path);

            Assert.Empty(store.GetAll());
            Assert.NotNull(store.LoadError);
        }
    }
}

using Folderss.Services;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>플러그인 표시 방식(plugins\display.json): 기본 팝업, 저장·읽기, 깨진 파일, 알 수 없는 값.</summary>
    public sealed class PluginDisplayStoreTests : IDisposable
    {
        private readonly string _root;

        public PluginDisplayStoreTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "folderss-plugin-display-" + Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        [Fact]
        public void MissingFile_DefaultsToPopup_AndSavingOnlyDefaultsWritesNothing()
        {
            Assert.Equal(PluginDisplayStore.Popup, PluginDisplayStore.Get(_root, "a.b"));

            PluginDisplayStore.Save(_root, new Dictionary<string, string> { { "a.b", PluginDisplayStore.Popup } });
            Assert.False(File.Exists(PluginDisplayStore.GetPath(_root)));
        }

        [Fact]
        public void Save_RoundTrips_CaseInsensitiveIds_AndDropsPopupEntries()
        {
            PluginDisplayStore.Save(_root, new Dictionary<string, string>
            {
                { "a.b", PluginDisplayStore.Main },
                { "c.d", PluginDisplayStore.Popup }
            });

            Assert.Equal(PluginDisplayStore.Main, PluginDisplayStore.Get(_root, "A.B"));
            Assert.Equal(PluginDisplayStore.Popup, PluginDisplayStore.Get(_root, "c.d"));
            Assert.DoesNotContain("c.d", File.ReadAllText(PluginDisplayStore.GetPath(_root)));

            // 메인 → 팝업으로 되돌리면 기록이 남아 있던 파일도 비운다.
            PluginDisplayStore.Save(_root, new Dictionary<string, string> { { "a.b", PluginDisplayStore.Popup } });
            Assert.Equal(PluginDisplayStore.Popup, PluginDisplayStore.Get(_root, "a.b"));
        }

        [Fact]
        public void CorruptFile_LoadThrows_GetFallsBackToPopup()
        {
            Directory.CreateDirectory(_root);
            File.WriteAllText(PluginDisplayStore.GetPath(_root), "{ not json");

            Assert.Throws<InvalidDataException>(() => PluginDisplayStore.Load(_root));
            Assert.Equal(PluginDisplayStore.Popup, PluginDisplayStore.Get(_root, "a.b"));
        }

        [Fact]
        public void UnknownValues_AreIgnored()
        {
            Directory.CreateDirectory(_root);
            File.WriteAllText(PluginDisplayStore.GetPath(_root), "{ \"a.b\": \"floating\", \"c.d\": \"MAIN\" }");

            var modes = PluginDisplayStore.Load(_root);
            Assert.False(modes.ContainsKey("a.b"));
            Assert.Equal(PluginDisplayStore.Main, modes["c.d"]);
        }
    }
}

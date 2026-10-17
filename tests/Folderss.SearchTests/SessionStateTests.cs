using Folderss.Services;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Xunit;

namespace Folderss.SearchTests
{
    public class SessionStateTests
    {
        private static SessionState RoundTrip(SessionState state)
        {
            var serializer = new XmlSerializer(typeof(SessionState));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, state);
                using (var reader = new StringReader(writer.ToString()))
                    return (SessionState)serializer.Deserialize(reader);
            }
        }

        [Fact]
        public void TreeViews_RoundTrip()
        {
            var state = new SessionState { LeftFolderPath = @"C:\a" };
            state.TreeViews.Add(new FolderTreeViewState { PanelKey = "left-folder", RootPath = @"C:\a", Pinned = true });
            state.TreeViews.Add(new FolderTreeViewState { PanelKey = "folder-panel|abc", RootPath = @"D:\b", Pinned = false });

            var loaded = RoundTrip(state);

            Assert.Equal(new[] { "left-folder", "folder-panel|abc" }, loaded.TreeViews.Select(t => t.PanelKey));
            Assert.Equal(new[] { @"C:\a", @"D:\b" }, loaded.TreeViews.Select(t => t.RootPath));
            Assert.Equal(new[] { true, false }, loaded.TreeViews.Select(t => t.Pinned));
        }

        [Fact]
        public void OldSessionFile_WithoutTreeViews_LoadsWithEmptyList()
        {
            const string oldXml = "<?xml version=\"1.0\"?><SessionState><LeftFolderPath>C:\\a</LeftFolderPath>" +
                                  "<OpenFolderPaths><string>C:\\b</string></OpenFolderPaths></SessionState>";
            using (var reader = new StringReader(oldXml))
            {
                var loaded = (SessionState)new XmlSerializer(typeof(SessionState)).Deserialize(reader);
                Assert.Equal(@"C:\a", loaded.LeftFolderPath);
                Assert.NotNull(loaded.TreeViews);
                Assert.Empty(loaded.TreeViews);
            }
        }
    }
}

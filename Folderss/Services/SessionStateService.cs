using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace Folderss.Services
{
    public static class SessionStateService
    {
        private static string SettingsPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Folderss",
                    "session.xml");
            }
        }

        public static SessionState Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return new SessionState();

                var serializer = new XmlSerializer(typeof(SessionState));
                using (var stream = File.OpenRead(SettingsPath))
                    return (SessionState)serializer.Deserialize(stream);
            }
            catch
            {
                return new SessionState();
            }
        }

        public static void Save(SessionState state)
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var serializer = new XmlSerializer(typeof(SessionState));
            using (var stream = File.Create(SettingsPath))
                serializer.Serialize(stream, state);
        }
    }

    [Serializable]
    public sealed class SessionState
    {
        public string LeftFolderPath { get; set; }
        public string RightFolderPath { get; set; }
        public string ActiveFolderPath { get; set; }
        public List<string> OpenFolderPaths { get; set; } = new List<string>();

        /// <summary>트리뷰를 연 채 종료한 폴더 패널들. 다음 시작 때 같은 패널(PanelKey)에 다시 연다.</summary>
        public List<FolderTreeViewState> TreeViews { get; set; } = new List<FolderTreeViewState>();
    }

    [Serializable]
    public sealed class FolderTreeViewState
    {
        /// <summary>도킹 패널 키(left-folder, right-folder, folder-panel|&lt;ID&gt;). 패널 잠금 키와 같다.</summary>
        public string PanelKey { get; set; }

        /// <summary>트리 루트 폴더(현재 폴더와 다를 수 있다).</summary>
        public string RootPath { get; set; }

        /// <summary>트리 루트 고정(📌) 여부.</summary>
        public bool Pinned { get; set; }
    }
}

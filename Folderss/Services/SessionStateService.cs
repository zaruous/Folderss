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
    }
}

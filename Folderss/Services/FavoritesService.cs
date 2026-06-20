using Folderss.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace Folderss.Services
{
    public static class FavoritesService
    {
        private static string SettingsPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Folderss",
                    "favorites.xml");
            }
        }

        public static IList<FavoriteLocation> Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var serializer = new XmlSerializer(typeof(List<FavoriteLocation>));
                    using (var stream = File.OpenRead(SettingsPath))
                        return (List<FavoriteLocation>)serializer.Deserialize(stream);
                }
            }
            catch
            {
                // 손상된 설정은 기본 즐겨찾기로 복구한다.
            }

            return CreateDefaults();
        }

        public static void Save(IEnumerable<FavoriteLocation> favorites)
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var serializer = new XmlSerializer(typeof(List<FavoriteLocation>));
            using (var stream = File.Create(SettingsPath))
                serializer.Serialize(stream, new List<FavoriteLocation>(favorites));
        }

        private static IList<FavoriteLocation> CreateDefaults()
        {
            var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var result = new List<FavoriteLocation>();
            AddIfExists(result, "바탕 화면", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            AddIfExists(result, "문서", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            AddIfExists(result, "다운로드", Path.Combine(user, "Downloads"));
            AddIfExists(result, "사용자 폴더", user);
            return result;
        }

        private static void AddIfExists(ICollection<FavoriteLocation> items, string name, string path)
        {
            if (Directory.Exists(path))
                items.Add(new FavoriteLocation { Name = name, Path = path });
        }
    }
}

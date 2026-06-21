using Folderss.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

        public static FavoritesConfiguration Load()
        {
            if (File.Exists(SettingsPath))
            {
                try
                {
                    var serializer = new XmlSerializer(typeof(FavoritesConfiguration));
                    using (var stream = File.OpenRead(SettingsPath))
                        return Normalize((FavoritesConfiguration)serializer.Deserialize(stream));
                }
                catch
                {
                    var migrated = TryLoadLegacy();
                    if (migrated != null)
                    {
                        try
                        {
                            Save(migrated);
                        }
                        catch
                        {
                            // 읽기는 성공했으므로 현재 세션에서는 마이그레이션된 데이터를 사용한다.
                        }
                        return migrated;
                    }
                }
            }

            return CreateDefaults();
        }

        public static void Save(FavoritesConfiguration configuration)
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var serializer = new XmlSerializer(typeof(FavoritesConfiguration));
            using (var stream = File.Create(SettingsPath))
                serializer.Serialize(stream, configuration);
        }

        private static FavoritesConfiguration TryLoadLegacy()
        {
            try
            {
                var serializer = new XmlSerializer(typeof(List<FavoriteLocation>));
                using (var stream = File.OpenRead(SettingsPath))
                {
                    var favorites = (List<FavoriteLocation>)serializer.Deserialize(stream);
                    var configuration = CreateEmpty();
                    foreach (var favorite in favorites)
                        configuration.Groups[0].Favorites.Add(favorite);
                    return configuration;
                }
            }
            catch
            {
                // 손상되었거나 알 수 없는 형식이면 기본 즐겨찾기로 복구한다.
                return null;
            }
        }

        private static FavoritesConfiguration CreateDefaults()
        {
            var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var configuration = CreateEmpty();
            var favorites = configuration.Groups[0].Favorites;
            AddIfExists(favorites, "바탕 화면", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            AddIfExists(favorites, "문서", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            AddIfExists(favorites, "다운로드", Path.Combine(user, "Downloads"));
            AddIfExists(favorites, "사용자 폴더", user);
            return configuration;
        }

        private static void AddIfExists(ICollection<FavoriteLocation> items, string name, string path)
        {
            if (Directory.Exists(path))
                items.Add(new FavoriteLocation { Name = name, Path = path });
        }

        private static FavoritesConfiguration CreateEmpty()
        {
            var configuration = new FavoritesConfiguration();
            configuration.Groups.Add(new FavoriteGroup { Name = "기본" });
            return configuration;
        }

        private static FavoritesConfiguration Normalize(FavoritesConfiguration configuration)
        {
            if (configuration == null)
                return CreateEmpty();

            if (configuration.Groups == null)
                configuration.Groups = new ObservableCollection<FavoriteGroup>();

            foreach (var group in configuration.Groups)
            {
                if (group.Favorites == null)
                    group.Favorites = new ObservableCollection<FavoriteLocation>();
                if (string.IsNullOrWhiteSpace(group.Name))
                    group.Name = "이름 없는 그룹";
            }

            if (configuration.Groups.Count == 0)
                configuration.Groups.Add(new FavoriteGroup { Name = "기본" });

            return configuration;
        }
    }
}

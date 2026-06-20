using AvalonDock;
using AvalonDock.Layout.Serialization;
using System;
using System.IO;
using System.Windows;

namespace Folderss.Services
{
    public static class DockLayoutService
    {
        private static string LayoutPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Folderss",
                    "dock-layout.xml");
            }
        }

        public static void Save(DockingManager manager)
        {
            var directory = Path.GetDirectoryName(LayoutPath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            new XmlLayoutSerializer(manager).Serialize(LayoutPath);
        }

        public static bool Restore(DockingManager manager, Func<string, object> contentResolver)
        {
            if (!File.Exists(LayoutPath))
                return false;

            try
            {
                var serializer = new XmlLayoutSerializer(manager);
                serializer.LayoutSerializationCallback += (sender, args) =>
                {
                    var content = contentResolver(args.Model.ContentId);
                    if (content != null)
                        args.Content = content;
                    else
                        args.Cancel = true;
                };
                serializer.Deserialize(LayoutPath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void Reset()
        {
            if (File.Exists(LayoutPath))
                File.Delete(LayoutPath);
        }
    }
}

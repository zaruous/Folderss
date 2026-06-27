using AvalonDock;
using AvalonDock.Layout.Serialization;
using System;
using System.IO;
using System.Windows;

namespace Folderss.Services
{
    public static class DockLayoutService
    {
        private const string CurrentLayoutVersion = "2";

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

        public static string RestoreErrorPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Folderss",
                    "dock-layout.restore-error.txt");
            }
        }

        public static bool RequiresLegacyConsoleMigration
        {
            get
            {
                try
                {
                    var versionPath = GetVersionPath(LayoutPath);
                    return !File.Exists(versionPath) ||
                        !string.Equals(File.ReadAllText(versionPath).Trim(), CurrentLayoutVersion, StringComparison.Ordinal);
                }
                catch
                {
                    return true;
                }
            }
        }

        public static void Save(DockingManager manager)
        {
            Save(manager, LayoutPath);
        }

        public static void Save(DockingManager manager, string layoutPath)
        {
            if (manager == null)
                throw new ArgumentNullException(nameof(manager));
            if (string.IsNullOrWhiteSpace(layoutPath))
                throw new ArgumentException("A layout path is required.", nameof(layoutPath));

            var directory = Path.GetDirectoryName(layoutPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var temporaryPath = layoutPath + ".tmp";
            try
            {
                new XmlLayoutSerializer(manager).Serialize(temporaryPath);
                File.Move(temporaryPath, layoutPath, true);
                File.WriteAllText(GetVersionPath(layoutPath), CurrentLayoutVersion);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        public static bool Restore(DockingManager manager, Func<string, object> contentResolver)
        {
            return Restore(manager, contentResolver, LayoutPath, RestoreErrorPath);
        }

        public static bool Restore(
            DockingManager manager,
            Func<string, object> contentResolver,
            string layoutPath)
        {
            return Restore(manager, contentResolver, layoutPath, layoutPath + ".restore-error.txt");
        }

        private static bool Restore(
            DockingManager manager,
            Func<string, object> contentResolver,
            string layoutPath,
            string restoreErrorPath)
        {
            if (manager == null)
                throw new ArgumentNullException(nameof(manager));
            if (contentResolver == null)
                throw new ArgumentNullException(nameof(contentResolver));
            if (string.IsNullOrWhiteSpace(layoutPath))
                throw new ArgumentException("A layout path is required.", nameof(layoutPath));
            if (!File.Exists(layoutPath))
                return false;

            try
            {
                if (File.Exists(restoreErrorPath))
                    File.Delete(restoreErrorPath);

                var serializer = new XmlLayoutSerializer(manager);
                serializer.LayoutSerializationCallback += (sender, args) =>
                {
                    var content = contentResolver(args.Model.ContentId);
                    if (content != null)
                        args.Content = content;
                    else
                        args.Cancel = true;
                };
                serializer.Deserialize(layoutPath);
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    var directory = Path.GetDirectoryName(restoreErrorPath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        Directory.CreateDirectory(directory);
                    File.WriteAllText(
                        restoreErrorPath,
                        "Layout restore failed." + Environment.NewLine +
                        ex.ToString());
                }
                catch
                {
                }
                return false;
            }
        }

        public static void Reset()
        {
            if (File.Exists(LayoutPath))
                File.Delete(LayoutPath);
            var versionPath = GetVersionPath(LayoutPath);
            if (File.Exists(versionPath))
                File.Delete(versionPath);
        }

        private static string GetVersionPath(string layoutPath)
        {
            return layoutPath + ".version";
        }
    }
}

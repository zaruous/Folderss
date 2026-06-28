using Folderss.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;

namespace Folderss.Services
{
    public static class OpenWithService
    {
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Folderss", "open-with.xml");

        private static List<OpenWithEntry> _entries = new List<OpenWithEntry>();

        static OpenWithService()
        {
            Load();
        }

        public static IReadOnlyList<OpenWithEntry> GetAll()
        {
            return _entries.AsReadOnly();
        }

        // Returns entries whose mask matches any of the given paths.
        public static IReadOnlyList<OpenWithEntry> GetMatchingEntries(IEnumerable<string> paths)
        {
            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool hasFolder = false;

            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                if (Directory.Exists(path))
                {
                    hasFolder = true;
                    extensions.Add("folder");
                }
                else
                {
                    var ext = Path.GetExtension(path);
                    if (!string.IsNullOrEmpty(ext))
                        extensions.Add(ext.ToLowerInvariant());
                    else
                        extensions.Add("");
                }
            }

            return _entries
                .Where(e => MaskMatches(e.ExtensionMask, extensions, hasFolder))
                .ToList();
        }

        public static OpenWithEntry GetDefaultEntryForPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            var token = Directory.Exists(path)
                ? "folder"
                : Path.GetExtension(path);

            if (string.IsNullOrWhiteSpace(token))
                return null;

            var matches = _entries
                .Where(entry => MaskHasSpecificToken(entry.ExtensionMask, token))
                .ToList();

            return matches.Count == 1 ? matches[0] : null;
        }

        public static void Launch(OpenWithEntry entry, IEnumerable<string> paths)
        {
            var pathList = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            if (pathList.Count == 0) return;

            var quotedPaths = string.Join(" ", pathList.Select(p => "\"" + p + "\""));
            var args = string.IsNullOrEmpty(entry.Arguments)
                ? quotedPaths
                : entry.Arguments.Replace("{0}", quotedPaths);

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = entry.ExecutablePath,
                    Arguments = args,
                    UseShellExecute = false
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    ex.Message,
                    "실행 오류",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        public static void Save(IEnumerable<OpenWithEntry> entries)
        {
            _entries = entries.ToList();
            Persist();
        }

        private static bool MaskMatches(string mask, HashSet<string> extensions, bool hasFolder)
        {
            if (string.IsNullOrWhiteSpace(mask) || mask == "*")
                return true;

            var parts = mask.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var token = part.Trim().ToLowerInvariant();
                if (token == "folder" && hasFolder)
                    return true;
                if (token == "*")
                    return true;
                if (extensions.Contains(token))
                    return true;
                // Support mask without leading dot
                if (!token.StartsWith(".") && extensions.Contains("." + token))
                    return true;
            }
            return false;
        }

        private static bool MaskHasSpecificToken(string mask, string token)
        {
            if (string.IsNullOrWhiteSpace(mask) || string.IsNullOrWhiteSpace(token))
                return false;

            var normalizedToken = token.Trim().ToLowerInvariant();
            var parts = mask.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var normalizedMask = part.Trim().ToLowerInvariant();
                if (normalizedMask == "*" || string.IsNullOrWhiteSpace(normalizedMask))
                    continue;

                if (normalizedMask == normalizedToken)
                    return true;

                if (normalizedToken != "folder" && !normalizedMask.StartsWith(".") && "." + normalizedMask == normalizedToken)
                    return true;
            }

            return false;
        }

        private static void Load()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                    return;

                var doc = new XmlDocument();
                doc.Load(ConfigPath);

                var nodes = doc.SelectNodes("/OpenWithEntries/Entry");
                if (nodes == null) return;

                foreach (XmlNode node in nodes)
                {
                    var entry = new OpenWithEntry
                    {
                        Id = ReadNode(node, "Id") ?? Guid.NewGuid().ToString("N"),
                        Name = ReadNode(node, "Name") ?? "",
                        Description = ReadNode(node, "Description") ?? "",
                        ExecutablePath = ReadNode(node, "ExecutablePath") ?? "",
                        Arguments = ReadNode(node, "Arguments") ?? "\"{0}\"",
                        ExtensionMask = ReadNode(node, "ExtensionMask") ?? "*"
                    };
                    if (!string.IsNullOrWhiteSpace(entry.Name))
                        _entries.Add(entry);
                }
            }
            catch { }
        }

        private static void Persist()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var doc = new XmlDocument();
                var declaration = doc.CreateXmlDeclaration("1.0", "utf-8", null);
                doc.AppendChild(declaration);

                var root = doc.CreateElement("OpenWithEntries");
                doc.AppendChild(root);

                foreach (var e in _entries)
                {
                    var node = doc.CreateElement("Entry");
                    AppendChild(doc, node, "Id", e.Id);
                    AppendChild(doc, node, "Name", e.Name);
                    AppendChild(doc, node, "Description", e.Description);
                    AppendChild(doc, node, "ExecutablePath", e.ExecutablePath);
                    AppendChild(doc, node, "Arguments", e.Arguments);
                    AppendChild(doc, node, "ExtensionMask", e.ExtensionMask);
                    root.AppendChild(node);
                }

                doc.Save(ConfigPath);
            }
            catch { }
        }

        private static string ReadNode(XmlNode parent, string name)
        {
            return parent.SelectSingleNode(name)?.InnerText;
        }

        private static void AppendChild(XmlDocument doc, XmlNode parent, string name, string value)
        {
            var el = doc.CreateElement(name);
            el.InnerText = value ?? "";
            parent.AppendChild(el);
        }
    }
}



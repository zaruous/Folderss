using Folderss.Viewers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Folderss.Services
{
    public class ViewerConfigService
    {
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Folderss", "viewer-config.json");

        private Dictionary<string, string> _mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".md",       "builtin:markdown" },
            { ".markdown", "builtin:markdown" },
            { ".txt",      "builtin:text"     },
            { ".cs",       "builtin:text"     },
            { ".json",     "builtin:text"     },
            { ".xml",      "builtin:text"     },
            { ".log",      "builtin:text"     },
        };

        public ViewerConfigService()
        {
            Load();
        }

        public IFileViewer Resolve(string extension)
        {
            string key;
            if (!_mappings.TryGetValue(extension, out key))
                return null;

            switch (key)
            {
                case "builtin:text":
                    return new Folderss.Viewers.TextViewer();
                case "builtin:markdown":
                    return new Folderss.Viewers.MarkdownViewer();
                default:
                    return null;
            }
        }

        public bool HasMapping(string extension)
        {
            return _mappings.ContainsKey(extension);
        }

        public string GetMappingKey(string extension)
        {
            string key;
            return _mappings.TryGetValue(extension, out key) ? key : null;
        }

        public void SetMapping(string extension, string viewerKey)
        {
            _mappings[extension] = viewerKey;
            Save();
        }

        public void RemoveMapping(string extension)
        {
            _mappings.Remove(extension);
            Save();
        }

        public IReadOnlyDictionary<string, string> GetAllMappings()
        {
            return _mappings;
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                    return;

                var json = File.ReadAllText(ConfigPath, Encoding.UTF8);
                var loaded = SimpleJson.Deserialize(json);
                if (loaded != null)
                    foreach (var kv in loaded)
                        _mappings[kv.Key] = kv.Value;
            }
            catch { }
        }

        private void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var sb = new StringBuilder();
                sb.Append("{\"mappings\":{");
                var first = true;
                foreach (var kv in _mappings)
                {
                    if (!first) sb.Append(",");
                    sb.AppendFormat("\"{0}\":\"{1}\"",
                        kv.Key.Replace("\"", "\\\""),
                        kv.Value.Replace("\\", "\\\\").Replace("\"", "\\\""));
                    first = false;
                }
                sb.Append("}}");
                File.WriteAllText(ConfigPath, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        // Minimal JSON deserializer for the mappings object.
        private static class SimpleJson
        {
            public static Dictionary<string, string> Deserialize(string json)
            {
                try
                {
                    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    // Find "mappings" object
                    var mappingsStart = json.IndexOf("\"mappings\"", StringComparison.Ordinal);
                    if (mappingsStart < 0) return null;

                    var braceOpen = json.IndexOf('{', mappingsStart + 10);
                    if (braceOpen < 0) return null;

                    var braceClose = json.IndexOf('}', braceOpen + 1);
                    if (braceClose < 0) return null;

                    var inner = json.Substring(braceOpen + 1, braceClose - braceOpen - 1);
                    foreach (var pair in inner.Split(','))
                    {
                        var colon = pair.IndexOf(':');
                        if (colon < 0) continue;
                        var key = pair.Substring(0, colon).Trim().Trim('"');
                        var val = pair.Substring(colon + 1).Trim().Trim('"');
                        if (!string.IsNullOrEmpty(key))
                            result[key] = val;
                    }
                    return result;
                }
                catch { return null; }
            }
        }
    }
}

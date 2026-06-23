using Folderss.Viewers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Folderss.Services
{
    public class ViewerConfigService
    {
        public const string SystemDefaultKey = "system:default";
        public const string BuiltInTextKey = "builtin:text";
        public const string BuiltInMarkdownKey = "builtin:markdown";
        public const string BuiltInMonacoKey = "builtin:monaco";

        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Folderss", "viewer-config.json");

        private static readonly Dictionary<string, string> DefaultMappings =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".md",       BuiltInMarkdownKey },
            { ".markdown", BuiltInMarkdownKey },
            { ".json",     BuiltInMonacoKey   },
            { ".xml",      BuiltInMonacoKey   },
        };

        private static readonly Dictionary<string, string> LegacyDefaultMappings =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".txt",      BuiltInTextKey     },
            { ".log",      BuiltInTextKey     },
            { ".cs",       BuiltInMonacoKey   },
            { ".xaml",     BuiltInMonacoKey   },
            { ".js",       BuiltInMonacoKey   },
            { ".ts",       BuiltInMonacoKey   },
            { ".html",     BuiltInMonacoKey   },
            { ".css",      BuiltInMonacoKey   },
            { ".py",       BuiltInMonacoKey   },
            { ".java",     BuiltInMonacoKey   },
            { ".cpp",      BuiltInMonacoKey   },
            { ".c",        BuiltInMonacoKey   },
            { ".h",        BuiltInMonacoKey   },
            { ".sh",       BuiltInMonacoKey   },
            { ".bat",      BuiltInMonacoKey   },
            { ".ps1",      BuiltInMonacoKey   },
            { ".yaml",     BuiltInMonacoKey   },
            { ".yml",      BuiltInMonacoKey   },
            { ".sql",      BuiltInMonacoKey   },
        };

        private readonly Dictionary<string, string> _overrides =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public sealed class ViewerMapping
        {
            public string Extension { get; set; }
            public string ViewerKey { get; set; }
            public string DefaultViewerKey { get; set; }
            public bool IsBuiltInDefault { get; set; }
        }

        public ViewerConfigService()
        {
            Load();
        }

        public IFileViewer Resolve(string extension)
        {
            var key = GetEffectiveMappingKey(extension);
            if (string.IsNullOrEmpty(key) || string.Equals(key, SystemDefaultKey, StringComparison.OrdinalIgnoreCase))
                return null;

            switch (key)
            {
                case BuiltInTextKey:
                    return new Folderss.Viewers.TextViewer();
                case BuiltInMarkdownKey:
                    return new Folderss.Viewers.MarkdownViewer();
                case BuiltInMonacoKey:
                    return new Folderss.Viewers.MonacoViewer();
                default:
                    return null;
            }
        }

        public bool HasMapping(string extension)
        {
            return GetEffectiveMappingKey(extension) != null;
        }

        public string GetMappingKey(string extension)
        {
            return GetEffectiveMappingKey(extension);
        }

        public void SetMapping(string extension, string viewerKey)
        {
            if (!extension.StartsWith("."))
                extension = "." + extension;

            string defaultKey;
            if (DefaultMappings.TryGetValue(extension, out defaultKey) &&
                string.Equals(defaultKey, viewerKey, StringComparison.OrdinalIgnoreCase))
                _overrides.Remove(extension);
            else
                _overrides[extension] = viewerKey;

            Save();
        }

        public void RemoveMapping(string extension)
        {
            _overrides.Remove(extension);
            Save();
        }

        public IReadOnlyDictionary<string, string> GetAllMappings()
        {
            var result = new Dictionary<string, string>(DefaultMappings, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _overrides)
                result[kv.Key] = kv.Value;
            return result;
        }

        public IReadOnlyList<ViewerMapping> GetMappingRows()
        {
            var keys = new SortedSet<string>(DefaultMappings.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (var key in _overrides.Keys)
                keys.Add(key);

            var rows = new List<ViewerMapping>();
            foreach (var extension in keys)
            {
                string defaultKey;
                string overrideKey;
                var hasDefault = DefaultMappings.TryGetValue(extension, out defaultKey);
                rows.Add(new ViewerMapping
                {
                    Extension = extension,
                    ViewerKey = _overrides.TryGetValue(extension, out overrideKey) ? overrideKey : defaultKey,
                    DefaultViewerKey = hasDefault ? defaultKey : null,
                    IsBuiltInDefault = hasDefault
                });
            }
            return rows;
        }

        public static IReadOnlyList<string> GetViewerKeys()
        {
            return new[] { SystemDefaultKey, BuiltInMarkdownKey, BuiltInMonacoKey, BuiltInTextKey };
        }

        private string GetEffectiveMappingKey(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
                return null;
            if (!extension.StartsWith("."))
                extension = "." + extension;

            string key;
            if (_overrides.TryGetValue(extension, out key))
                return key;
            return DefaultMappings.TryGetValue(extension, out key) ? key : null;
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
                    {
                        string defaultKey;
                        if (DefaultMappings.TryGetValue(kv.Key, out defaultKey) &&
                            string.Equals(defaultKey, kv.Value, StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (LegacyDefaultMappings.TryGetValue(kv.Key, out defaultKey) &&
                            string.Equals(defaultKey, kv.Value, StringComparison.OrdinalIgnoreCase))
                            continue;
                        _overrides[kv.Key] = kv.Value;
                    }
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
                foreach (var kv in _overrides)
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

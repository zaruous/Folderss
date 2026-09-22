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

        private static readonly string DefaultConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Folderss", "viewer-config.json");

        private readonly string _configPath;

        /// <summary>
        /// viewer-config.json 형식 버전. v2부터 최상위에 "version"을 기록하며 기본값과 다른 재정의만 담는다.
        /// 버전 표기가 없는 파일은 Monaco 도입 전의 기본 매핑 전체 덤프이거나 v2 도입 전에 저장된 재정의 목록인데,
        /// <see cref="IsLegacyFullDump"/>가 둘을 구분한다.
        /// </summary>
        private const int ConfigVersion = 2;

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

        public ViewerConfigService() : this(null) { }

        /// <param name="configPath">설정 파일 경로. null이면 `%LOCALAPPDATA%\Folderss\viewer-config.json`. 테스트에서 임시 경로를 준다.</param>
        public ViewerConfigService(string configPath)
        {
            _configPath = string.IsNullOrWhiteSpace(configPath) ? DefaultConfigPath : configPath;
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

        /// <summary>
        /// 설정 창에서 편집한 매핑 전체를 한 번에 반영하고 파일을 한 번만 쓴다.
        /// 메모리 상태는 파일 쓰기 전에 갱신되므로, 쓰기가 실패해도 이번 실행 중에는 새 매핑이 적용된다.
        /// 저장 실패는 예외로 호출자에게 알린다.
        /// </summary>
        public void ReplaceMappings(IEnumerable<KeyValuePair<string, string>> mappings)
        {
            _overrides.Clear();
            foreach (var kv in mappings)
                ApplyMapping(kv.Key, kv.Value);
            Save();
        }

        private void ApplyMapping(string extension, string viewerKey)
        {
            if (string.IsNullOrWhiteSpace(extension) || string.IsNullOrWhiteSpace(viewerKey))
                return;
            if (!extension.StartsWith("."))
                extension = "." + extension;

            // 기본 매핑과 같은 값은 재정의가 아니므로 저장하지 않는다.
            string defaultKey;
            if (DefaultMappings.TryGetValue(extension, out defaultKey) &&
                string.Equals(defaultKey, viewerKey, StringComparison.OrdinalIgnoreCase))
                _overrides.Remove(extension);
            else
                _overrides[extension] = viewerKey;
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
                if (!File.Exists(_configPath))
                    return;

                var json = File.ReadAllText(_configPath, Encoding.UTF8);
                var loaded = SimpleJson.Deserialize(json);
                if (loaded == null)
                    return;

                var dropLegacyDefaults = IsLegacyFullDump(json, loaded);
                foreach (var kv in loaded)
                {
                    string defaultKey;
                    if (DefaultMappings.TryGetValue(kv.Key, out defaultKey) &&
                        string.Equals(defaultKey, kv.Value, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (dropLegacyDefaults &&
                        LegacyDefaultMappings.TryGetValue(kv.Key, out defaultKey) &&
                        string.Equals(defaultKey, kv.Value, StringComparison.OrdinalIgnoreCase))
                        continue;
                    _overrides[kv.Key] = kv.Value;
                }
            }
            catch { }
        }

        /// <summary>
        /// Monaco 도입 전(5d94461 이전) 파일인지 판별한다. 그 시절 파일은 기본 매핑 전체를 그대로 덤프했으므로
        /// `.txt → Text`처럼 <see cref="LegacyDefaultMappings"/>와 같은 항목은 사용자의 선택이 아닌 잔여물이라 버려야 한다.
        /// 판별 기준: 버전 표기가 없고, 현재 기본 매핑과 같은 항목(예: `.md → markdown`)이 들어 있다.
        /// 재정의만 저장하는 코드는 기본 매핑과 같은 값을 절대 쓰지 않으므로, 그런 항목이 있으면 전체 덤프 파일이다.
        /// 버전 표기만 없는 재정의 파일(v2 도입 전에 저장된 것)은 모든 항목이 사용자의 선택이라 그대로 유지해야 한다 —
        /// 과거에는 이를 구분하지 않고 legacy 표와 같은 값을 모두 버려서 `.sql → Monaco`, `.txt → Text` 같은 매핑이
        /// 저장은 되지만 재시작 후 사라졌다.
        /// </summary>
        private static bool IsLegacyFullDump(string json, Dictionary<string, string> loaded)
        {
            if (SimpleJson.ReadVersion(json) >= ConfigVersion)
                return false;

            foreach (var kv in loaded)
            {
                string defaultKey;
                if (DefaultMappings.TryGetValue(kv.Key, out defaultKey) &&
                    string.Equals(defaultKey, kv.Value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 재정의 매핑을 v2 형식으로 파일에 쓴다. 임시 파일에 쓴 뒤 교체하고(<see cref="SettingsFile"/>),
        /// 실패는 삼키지 않고 예외로 알린다 — 설정 창이 다른 설정의 실패와 함께 모아 사용자에게 보여준다.
        /// </summary>
        private void Save()
        {
            var sb = new StringBuilder();
            sb.Append("{\"version\":").Append(ConfigVersion).Append(",\"mappings\":{");
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
            SettingsFile.WriteAllText(_configPath, sb.ToString(), Encoding.UTF8);
        }

        // Minimal JSON deserializer for the mappings object.
        private static class SimpleJson
        {
            /// <summary>
            /// 최상위 "version" 정수를 읽는다. 표기가 없으면 v1 파일로 본다.
            /// 앱은 항상 <c>{"version":2,...}</c>로 쓰지만, 손으로 고친 <c>"version":"2"</c>(문자열)도 같은 값으로 읽는다 —
            /// 그렇지 않으면 v1로 오판해 legacy 정리 규칙이 사용자 매핑을 버릴 수 있다.
            /// </summary>
            public static int ReadVersion(string json)
            {
                var index = json.IndexOf("\"version\"", StringComparison.Ordinal);
                if (index < 0) return 1;

                var colon = json.IndexOf(':', index);
                if (colon < 0) return 1;

                var position = colon + 1;
                while (position < json.Length && char.IsWhiteSpace(json[position])) position++;
                if (position < json.Length && json[position] == '"') position++;
                var start = position;
                while (position < json.Length && char.IsDigit(json[position])) position++;

                int version;
                return int.TryParse(json.Substring(start, position - start), out version) ? version : 1;
            }

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

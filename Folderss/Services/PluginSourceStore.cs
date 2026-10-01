using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Folderss.Services
{
    /// <summary>
    /// 플러그인을 어디서 설치했는지(<c>plugins\sources.json</c>, id → 출처). 같은 id를 다른 출처에서 설치하면 경고하기 위함이다 —
    /// 같은 id의 플러그인은 기존 플러그인의 설정·데이터(plugin-data)를 그대로 읽을 수 있다.
    /// 출처 값: <see cref="LocalFile"/> 또는 <c>github.com/&lt;소유자&gt;/&lt;저장소&gt;</c>. 기록이 없으면(이 기능 이전 설치) null.
    /// </summary>
    public static class PluginSourceStore
    {
        public const string LocalFile = "local";
        public const string FileName = "sources.json";

        public static string GetPath(string pluginsDirectory)
        {
            return Path.Combine(pluginsDirectory, FileName);
        }

        /// <summary>파일이 없으면 빈 사전. 읽을 수 없으면 <see cref="InvalidDataException"/> (덮어써서 기록을 잃지 않게).</summary>
        public static Dictionary<string, string> Load(string pluginsDirectory)
        {
            var path = GetPath(pluginsDirectory);
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path))
                return result;
            try
            {
                var values = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
                if (values != null)
                    foreach (var pair in values)
                        result[pair.Key] = pair.Value;
                return result;
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("플러그인 설치 출처 기록(" + path + ")을 읽지 못했습니다: " + ex.Message, ex);
            }
        }

        public static string Get(string pluginsDirectory, string id)
        {
            string source;
            return Load(pluginsDirectory).TryGetValue(id, out source) ? source : null;
        }

        /// <summary><paramref name="source"/>가 null이면 기록을 지운다. 쓰기 실패는 예외.</summary>
        public static void Set(string pluginsDirectory, string id, string source)
        {
            var values = Load(pluginsDirectory);
            if (source == null)
            {
                if (!values.Remove(id))
                    return;
            }
            else
            {
                values[id] = source;
            }
            SettingsFile.WriteAllText(GetPath(pluginsDirectory), JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }));
        }

        public static bool IsSameSource(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        public static string Describe(string source)
        {
            if (source == null)
                return "기록 없음";
            return IsSameSource(source, LocalFile) ? "로컬 zip 파일" : "GitHub " + source;
        }
    }
}

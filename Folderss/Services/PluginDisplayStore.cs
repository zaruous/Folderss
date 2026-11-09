using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Folderss.Services
{
    /// <summary>
    /// 플러그인별 표시 방식(<c>plugins\display.json</c>, id → <see cref="Popup"/>/<see cref="Main"/>).
    /// 본체가 관리하는 값이라 플러그인 자기 settings.json에 두지 않는다. 기록이 없으면 <see cref="Popup"/>.
    /// </summary>
    public static class PluginDisplayStore
    {
        public const string Popup = "popup";
        public const string Main = "main";
        public const string FileName = "display.json";

        /// <summary>
        /// 플러그인이 아닌 기본 기능 Git의 표시 방식 키. 설정 > 플러그인 목록에 항상 보이고 같은 파일에 저장한다.
        /// 플러그인 ID 규칙(<c>PluginPackage.IdPattern</c>)에 ':'가 없어 플러그인 ID와 겹치지 않는다.
        /// </summary>
        public const string BuiltinGitId = "builtin:git";

        public static string GetPath(string pluginsDirectory)
        {
            return Path.Combine(pluginsDirectory, FileName);
        }

        /// <summary>파일이 없으면 빈 사전. 읽을 수 없으면 <see cref="InvalidDataException"/>. 알 수 없는 값은 버린다.</summary>
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
                        if (IsKnown(pair.Value))
                            result[pair.Key] = Normalize(pair.Value);
                return result;
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("플러그인 표시 방식 기록(" + path + ")을 읽지 못했습니다: " + ex.Message, ex);
            }
        }

        /// <summary>표시 방식. 기록이 없거나 파일을 읽을 수 없으면 <see cref="Popup"/>.</summary>
        public static string Get(string pluginsDirectory, string id)
        {
            try
            {
                string mode;
                return Load(pluginsDirectory).TryGetValue(id, out mode) ? mode : Popup;
            }
            catch (InvalidDataException)
            {
                return Popup;
            }
        }

        /// <summary>전체를 바꿔 쓴다. 기본값(<see cref="Popup"/>)은 기록하지 않는다. 쓰기 실패는 예외.</summary>
        public static void Save(string pluginsDirectory, IDictionary<string, string> modes)
        {
            var values = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in modes)
                if (string.Equals(Normalize(pair.Value), Main, StringComparison.Ordinal))
                    values[pair.Key] = Main;
            var path = GetPath(pluginsDirectory);
            if (values.Count == 0 && !File.Exists(path))
                return;
            SettingsFile.WriteAllText(path, JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }));
        }

        public static bool IsKnown(string mode)
        {
            return string.Equals(mode, Popup, StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, Main, StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string mode)
        {
            return string.Equals(mode, Main, StringComparison.OrdinalIgnoreCase) ? Main : Popup;
        }
    }
}

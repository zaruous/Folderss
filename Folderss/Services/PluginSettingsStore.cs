using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Folderss.Services
{
    /// <summary>
    /// 플러그인 하나의 키/값 설정(<c>settings.json</c>). 쓰기는 <see cref="SettingsFile"/>로 하고 실패는 예외로 던진다.
    /// 읽기 실패(깨진 파일)는 빈 설정으로 시작하되 원인을 <see cref="LoadError"/>에 남긴다 — 다음 저장이 깨진 파일을 덮어쓴다.
    /// </summary>
    public sealed class PluginSettingsStore
    {
        private readonly string _path;
        private readonly object _gate = new object();
        private Dictionary<string, string> _values;

        public PluginSettingsStore(string path)
        {
            _path = path;
        }

        public string LoadError { get; private set; }

        public string Get(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            lock (_gate)
            {
                string value;
                return EnsureLoaded().TryGetValue(key, out value) ? value : null;
            }
        }

        public void Set(string key, string value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            lock (_gate)
            {
                var next = new Dictionary<string, string>(EnsureLoaded(), StringComparer.Ordinal);
                if (value == null)
                    next.Remove(key);
                else
                    next[key] = value;

                SettingsFile.WriteAllText(_path, JsonSerializer.Serialize(next, new JsonSerializerOptions { WriteIndented = true }));
                _values = next;
            }
        }

        public IReadOnlyDictionary<string, string> GetAll()
        {
            lock (_gate)
                return new Dictionary<string, string>(EnsureLoaded(), StringComparer.Ordinal);
        }

        private Dictionary<string, string> EnsureLoaded()
        {
            if (_values != null)
                return _values;

            _values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!File.Exists(_path))
                return _values;
            try
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path));
                if (loaded != null)
                    _values = new Dictionary<string, string>(loaded, StringComparer.Ordinal);
            }
            catch (Exception ex)
            {
                LoadError = ex.Message;
            }
            return _values;
        }
    }
}

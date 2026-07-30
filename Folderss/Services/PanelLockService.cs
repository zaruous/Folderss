using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace Folderss.Services
{
    /// <summary>
    /// 문서 탭(패널)의 잠금 상태를 저장·복원한다.
    /// 잠긴 패널은 닫기가 비활성화되며, 상태는 %LOCALAPPDATA%\Folderss\panel-locks.xml에 남아
    /// 프로그램을 다시 실행해도 유지된다.
    /// </summary>
    public static class PanelLockService
    {
        private static HashSet<string> _lockedKeys;

        private static string SettingsPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Folderss",
                    "panel-locks.xml");
            }
        }

        private static HashSet<string> LockedKeys
        {
            get
            {
                if (_lockedKeys == null)
                    Load();
                return _lockedKeys;
            }
        }

        public static bool IsLocked(string lockKey)
        {
            return !string.IsNullOrWhiteSpace(lockKey) && LockedKeys.Contains(lockKey);
        }

        public static void SetLocked(string lockKey, bool locked)
        {
            if (string.IsNullOrWhiteSpace(lockKey))
                return;

            var changed = locked
                ? LockedKeys.Add(lockKey)
                : LockedKeys.Remove(lockKey);

            if (changed)
                Save();
        }

        /// <summary>현재 존재하는 패널 키만 남기고 나머지 잠금 항목을 제거한다.</summary>
        public static void Prune(IEnumerable<string> aliveKeys)
        {
            var alive = new HashSet<string>(
                (aliveKeys ?? Enumerable.Empty<string>()).Where(key => !string.IsNullOrWhiteSpace(key)),
                StringComparer.OrdinalIgnoreCase);

            var removed = LockedKeys.RemoveWhere(key => !alive.Contains(key));
            if (removed > 0)
                Save();
        }

        private static void Load()
        {
            _lockedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (!File.Exists(SettingsPath))
                    return;

                var serializer = new XmlSerializer(typeof(PanelLockState));
                PanelLockState state;
                using (var stream = File.OpenRead(SettingsPath))
                    state = (PanelLockState)serializer.Deserialize(stream);

                foreach (var key in state?.LockedPanels ?? new List<string>())
                {
                    if (!string.IsNullOrWhiteSpace(key))
                        _lockedKeys.Add(key);
                }
            }
            catch
            {
                // 잠금 정보 손상은 앱 시작을 막지 않는다. 잠금 없는 상태로 시작한다.
                _lockedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var state = new PanelLockState
                {
                    LockedPanels = LockedKeys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToList()
                };

                var serializer = new XmlSerializer(typeof(PanelLockState));
                var temporaryPath = SettingsPath + ".tmp";
                using (var stream = File.Create(temporaryPath))
                    serializer.Serialize(stream, state);
                File.Move(temporaryPath, SettingsPath, true);
            }
            catch
            {
                // 저장 실패는 UI 흐름을 막지 않는다. 현재 세션의 잠금 상태는 메모리에 유지된다.
            }
        }
    }

    [Serializable]
    public sealed class PanelLockState
    {
        public List<string> LockedPanels { get; set; } = new List<string>();
    }
}

using Folderss.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Xml.Serialization;

namespace Folderss.Services
{
    public class KeyBindingService
    {
        private static string SettingsPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Folderss", "keybindings.xml");
            }
        }

        private List<KeyBindingEntry> _bindings = GetDefaults();

        public IReadOnlyList<KeyBindingEntry> Bindings { get { return _bindings; } }

        public static List<KeyBindingEntry> GetDefaults()
        {
            return new List<KeyBindingEntry>
            {
                new KeyBindingEntry { CommandId = "Rename",          DisplayName = "이름 변경",        Key = Key.F2 },
                new KeyBindingEntry { CommandId = "Refresh",         DisplayName = "새로 고침",        Key = Key.F5 },
                new KeyBindingEntry { CommandId = "Move",            DisplayName = "이동",             Key = Key.F6 },
                new KeyBindingEntry { CommandId = "Delete",          DisplayName = "삭제",             Key = Key.Delete },
                new KeyBindingEntry { CommandId = "RefreshAlt",      DisplayName = "새로 고침 (대체)", Key = Key.R,     Modifiers = ModifierKeys.Control },
                new KeyBindingEntry { CommandId = "NewFolder",       DisplayName = "새 폴더",          Key = Key.N,     Modifiers = ModifierKeys.Control | ModifierKeys.Shift },
                new KeyBindingEntry { CommandId = "AddPanel",        DisplayName = "폴더 패널 추가",   Key = Key.T,     Modifiers = ModifierKeys.Control },
                new KeyBindingEntry { CommandId = "CopyClipboard",   DisplayName = "복사 (클립보드)",  Key = Key.C,     Modifiers = ModifierKeys.Control },
                new KeyBindingEntry { CommandId = "CutClipboard",    DisplayName = "잘라내기",         Key = Key.X,     Modifiers = ModifierKeys.Control },
                new KeyBindingEntry { CommandId = "PasteClipboard",  DisplayName = "붙여넣기",         Key = Key.V,     Modifiers = ModifierKeys.Control },
                new KeyBindingEntry { CommandId = "NavigateBack",    DisplayName = "뒤로",             Key = Key.Left,  Modifiers = ModifierKeys.Alt },
                new KeyBindingEntry { CommandId = "NavigateForward", DisplayName = "앞으로",           Key = Key.Right, Modifiers = ModifierKeys.Alt },
                new KeyBindingEntry { CommandId = "NavigateUp",      DisplayName = "상위 폴더",        Key = Key.Up,    Modifiers = ModifierKeys.Alt },
                new KeyBindingEntry { CommandId = "SwitchPaneLeft",  DisplayName = "왼쪽 패널 전환",   Key = Key.Left,  Modifiers = ModifierKeys.Control | ModifierKeys.Shift },
                new KeyBindingEntry { CommandId = "SwitchPaneRight", DisplayName = "오른쪽 패널 전환", Key = Key.Right, Modifiers = ModifierKeys.Control | ModifierKeys.Shift },
                new KeyBindingEntry { CommandId = "ShowSearch",      DisplayName = "내용 검색",        Key = Key.F,     Modifiers = ModifierKeys.Control },
                new KeyBindingEntry { CommandId = "PanelMaximize",  DisplayName = "패널 최대화 토글", Key = Key.F11 },
            };
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;

                var ser = new XmlSerializer(typeof(List<KeyBindingEntry>));
                List<KeyBindingEntry> saved;
                using (var stream = File.OpenRead(SettingsPath))
                {
                    saved = (List<KeyBindingEntry>)ser.Deserialize(stream);
                }

                // Merge: start from defaults so new commands always appear
                var merged = GetDefaults();
                foreach (var def in merged)
                {
                    var s = saved.FirstOrDefault(b => b.CommandId == def.CommandId);
                    if (s != null)
                    {
                        def.Key = s.Key;
                        def.Modifiers = s.Modifiers;
                    }
                }
                _bindings = merged;
            }
            catch
            {
                _bindings = GetDefaults();
            }
        }

        public void Save(IEnumerable<KeyBindingEntry> bindings)
        {
            _bindings = bindings.ToList();
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            // Write to a temp file first, then atomically replace — prevents corrupt
            // keybindings.xml if the process is killed mid-write.
            var tempPath = SettingsPath + ".tmp";
            var ser = new XmlSerializer(typeof(List<KeyBindingEntry>));
            using (var stream = File.Create(tempPath))
            {
                ser.Serialize(stream, _bindings);
            }

            if (File.Exists(SettingsPath))
                File.Replace(tempPath, SettingsPath, null);
            else
                File.Move(tempPath, SettingsPath);
        }

        public KeyBindingEntry GetBinding(string commandId)
        {
            return _bindings.FirstOrDefault(b => b.CommandId == commandId);
        }

        /// <summary>
        /// Returns true if the key event matches the binding for commandId.
        /// ignoreModifiers: modifier bits to exclude from the modifier check (e.g. Shift for Delete).
        /// Alt combinations arrive as e.SystemKey in WPF, handled automatically.
        /// </summary>
        public bool Matches(KeyEventArgs e, string commandId, ModifierKeys ignoreModifiers = ModifierKeys.None)
        {
            var b = GetBinding(commandId);
            if (b == null || b.Key == Key.None) return false;

            var actualMods = Keyboard.Modifiers & ~ignoreModifiers;
            if (actualMods != b.Modifiers) return false;

            // Alt combinations set e.Key = Key.System; the real key is in e.SystemKey
            if ((b.Modifiers & ModifierKeys.Alt) != 0)
                return e.SystemKey == b.Key;

            return e.Key == b.Key;
        }
    }
}

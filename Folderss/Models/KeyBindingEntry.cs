using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Input;
using System.Xml.Serialization;

namespace Folderss.Models
{
    [Serializable]
    public class KeyBindingEntry : INotifyPropertyChanged
    {
        private Key _key;
        private ModifierKeys _modifiers;

        public string CommandId { get; set; }
        public string DisplayName { get; set; }

        public Key Key
        {
            get { return _key; }
            set { _key = value; Notify("Key"); Notify("KeyDisplayText"); }
        }

        public ModifierKeys Modifiers
        {
            get { return _modifiers; }
            set { _modifiers = value; Notify("Modifiers"); Notify("KeyDisplayText"); }
        }

        [XmlIgnore]
        public string KeyDisplayText
        {
            get
            {
                if (_key == Key.None) return "(없음)";
                var parts = new List<string>();
                if ((_modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
                if ((_modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
                if ((_modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
                parts.Add(KeyToString(_key));
                return string.Join("+", parts);
            }
        }

        public static string KeyToString(Key key)
        {
            switch (key)
            {
                case Key.F1: return "F1";
                case Key.F2: return "F2";
                case Key.F3: return "F3";
                case Key.F4: return "F4";
                case Key.F5: return "F5";
                case Key.F6: return "F6";
                case Key.F7: return "F7";
                case Key.F8: return "F8";
                case Key.F9: return "F9";
                case Key.F10: return "F10";
                case Key.F11: return "F11";
                case Key.F12: return "F12";
                case Key.Left: return "←";
                case Key.Right: return "→";
                case Key.Up: return "↑";
                case Key.Down: return "↓";
                case Key.Delete: return "Delete";
                case Key.Enter: return "Enter";
                case Key.Space: return "Space";
                case Key.Tab: return "Tab";
                case Key.Back: return "Backspace";
                case Key.Home: return "Home";
                case Key.End: return "End";
                case Key.PageUp: return "PageUp";
                case Key.PageDown: return "PageDown";
                case Key.Insert: return "Insert";
                case Key.Escape: return "Esc";
                default: return key.ToString();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Notify(string p)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
        }

        public KeyBindingEntry Clone()
        {
            return new KeyBindingEntry
            {
                CommandId = CommandId,
                DisplayName = DisplayName,
                Key = Key,
                Modifiers = Modifiers
            };
        }
    }
}

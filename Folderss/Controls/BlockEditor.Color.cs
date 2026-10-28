using Folderss.Models;
using System;
using System.Windows.Controls;
using System.Windows.Media;

namespace Folderss.Controls
{
    public sealed partial class BlockEditor
    {
        // ── 블록 색(사이드카 속성) ─────────────────────────────────────────
        //
        // 블록 글자색(color)·배경색(background)은 Markdown으로 나타낼 수 없어 사이드카(문서.md.meta.json)에 저장한다(#78 D안).
        // 색 이름만 저장하고 실제 색은 테마 밝기에 맞춰 고른다(Notion 팔레트).

        private const string ColorAttribute = "color";
        private const string BackgroundAttribute = "background";

        private static readonly string[] BlockColorNames = { "gray", "brown", "orange", "yellow", "green", "blue", "purple", "pink", "red" };
        private static readonly string[] BlockColorLabels = { "회색", "갈색", "주황", "노랑", "초록", "파랑", "보라", "분홍", "빨강" };

        private static readonly string[] TextOnLight = { "#787774", "#9F6B53", "#D9730D", "#CB912F", "#448361", "#337EA9", "#9065B0", "#C14C8A", "#D44C47" };
        private static readonly string[] TextOnDark = { "#9B9B9B", "#BA856F", "#C77D48", "#CA9849", "#529E72", "#379AD3", "#9D68D3", "#D15796", "#DF5452" };
        private static readonly string[] BackgroundOnLight = { "#F1F1EF", "#F4EEEE", "#FBECDD", "#FBF3DB", "#EDF3EC", "#E7F3F8", "#F6F3F9", "#FAF1F5", "#FDEBEC" };
        private static readonly string[] BackgroundOnDark = { "#2F2F2F", "#4A3228", "#5C3B23", "#564328", "#243D30", "#143A4E", "#3C2D49", "#4E2C3C", "#522E2A" };

        /// <summary>지금 테마가 어두운지(패널 바탕 밝기). ThemeInfo.IsLight와 같은 계산.</summary>
        private bool IsDarkTheme()
        {
            var resource = TryFindResource("PanelBackgroundColor");
            if (!(resource is Color))
                return true;
            var c = (Color)resource;
            return 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B <= 128;
        }

        /// <summary>블록 색 이름의 실제 색. 모르는 이름이면 null.</summary>
        private SolidColorBrush BlockColorBrush(string name, bool background)
        {
            var index = Array.IndexOf(BlockColorNames, name);
            if (index < 0)
                return null;
            var dark = IsDarkTheme();
            var hex = background ? (dark ? BackgroundOnDark : BackgroundOnLight)[index] : (dark ? TextOnDark : TextOnLight)[index];
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        /// <summary>[first, last] 블록의 속성 하나를 바꾼다(값이 비면 지움). 실행 취소 한 단계.</summary>
        private void SetBlockAttribute(int first, int last, string key, string value)
        {
            if (first < 0)
                return;
            for (var i = first; i <= last && i < _blocks.Count; i++)
                _blocks[i].SetAttribute(key, value);
            SyncRows();
            RecordChange();
        }

        /// <summary>블록 메뉴의 "색": 기본(색 없음), 글자색 9가지, 배경색 9가지.</summary>
        private MenuItem CreateColorMenu(Func<int> first, Func<int> last, Func<bool> valid)
        {
            var menu = new MenuItem { Header = "색" };
            menu.Items.Add(CreateMenuItem("기본 (색 없음)", string.Empty, () =>
            {
                if (!valid())
                    return;
                for (var i = first(); i <= last() && i < _blocks.Count; i++)
                {
                    _blocks[i].SetAttribute(ColorAttribute, null);
                    _blocks[i].SetAttribute(BackgroundAttribute, null);
                }
                SyncRows();
                RecordChange();
            }));
            menu.Items.Add(new Separator());
            var text = new MenuItem { Header = "글자색" };
            var background = new MenuItem { Header = "배경색" };
            for (var i = 0; i < BlockColorNames.Length; i++)
            {
                var name = BlockColorNames[i];
                text.Items.Add(CreateColorItem(BlockColorLabels[i], name, false, () => { if (valid()) SetBlockAttribute(first(), last(), ColorAttribute, name); }));
                background.Items.Add(CreateColorItem(BlockColorLabels[i] + " 배경", name, true, () => { if (valid()) SetBlockAttribute(first(), last(), BackgroundAttribute, name); }));
            }
            menu.Items.Add(text);
            menu.Items.Add(background);
            return menu;
        }

        private MenuItem CreateColorItem(string label, string name, bool background, Action action)
        {
            var item = CreateMenuItem(label, string.Empty, action);
            item.Icon = new System.Windows.Controls.Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new System.Windows.CornerRadius(3),
                Background = BlockColorBrush(name, background),
            };
            return item;
        }
    }
}

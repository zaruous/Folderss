using Folderss.Services;
using System.Windows.Media;

namespace Folderss.Viewers
{
    /// <summary>
    /// 웹 뷰어(markdown-app.html, text-app.html)에 넘기는 테마 색. 테마 XAML의 색에서 만들어, 테마를 추가해도 페이지를 고칠 필요가 없다.
    /// 페이지는 <c>colors</c>를 CSS 변수(<c>--bg</c> 등)로, <c>light</c>로 코드 하이라이트·Mermaid 밝기를 고른다.
    /// </summary>
    internal static class ViewerTheme
    {
        public static string ToJson(ThemeInfo theme)
        {
            return "{\"light\":" + (theme.IsLight ? "true" : "false") + ",\"colors\":{"
                + Entry("bg", theme, "PanelBackgroundColor") + ","
                + Entry("panel", theme, "SurfaceBackgroundColor") + ","
                + Entry("border", theme, "BorderColor") + ","
                + Entry("fg", theme, "PrimaryTextColor") + ","
                + Entry("fg2", theme, "SecondaryTextColor") + ","
                + Entry("accent", theme, "AccentColor") + ","
                + Entry("hover", theme, "ControlHoverColor") + ","
                + Entry("sel", theme, "SelectionColor") + ","
                + Entry("line-num", theme, "DisabledTextColor") + ","
                + Entry("scroll", theme, "BorderColor") + "}}";
        }

        /// <summary>페이지 <c>--bg</c>와 같은 색. 페이지가 그려지기 전 WebView 바탕으로 쓴다.</summary>
        public static System.Drawing.Color Background(ThemeInfo theme)
        {
            var c = theme.GetColor("PanelBackgroundColor");
            return System.Drawing.Color.FromArgb(c.R, c.G, c.B);
        }

        private static string Entry(string name, ThemeInfo theme, string key)
        {
            return "\"" + name + "\":\"" + Hex(theme.GetColor(key)) + "\"";
        }

        private static string Hex(Color c)
        {
            return string.Format("#{0:x2}{1:x2}{2:x2}", c.R, c.G, c.B);
        }
    }
}

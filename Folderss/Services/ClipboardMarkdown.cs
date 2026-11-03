using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Folderss.Services
{
    /// <summary>
    /// 블록 편집기 붙여넣기(#76): 클립보드 HTML(브라우저·Word·Google 문서 등)을 Markdown으로. 줄 안 서식은 굵게·기울임·취소선·코드·링크만,
    /// 문단·제목(h1~h3)·목록·인용·줄바꿈은 블록 Markdown 줄로 바꾼다. 나머지 태그(표·그림·글꼴)는 글자만 남긴다. WPF 없음(단위 테스트).
    /// 밑줄(<c>u</c>·<c>ins</c>·<c>text-decoration: underline</c>)·글자 색(<c>color</c>·<c>&lt;font color&gt;</c>)·배경색(<c>mark</c>·<c>background-color</c>)은
    /// Markdown에 넣지 않고 줄마다 범위 속성 값(<see cref="TextMarks"/>)으로 따로 돌려준다(#76 9번). 색은 가장 가까운 9가지 이름으로, 검정·흰색·투명은 버린다.
    /// </summary>
    public static class ClipboardMarkdown
    {
        private static readonly Regex Tag = new Regex(@"<!--[\s\S]*?-->|<(/?)([A-Za-z][A-Za-z0-9]*)((?:[^>""']|""[^""]*""|'[^']*')*)>", RegexOptions.Compiled);
        private static readonly Regex Href = new Regex(@"\bhref\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex Style = new Regex(@"\bstyle\s*=\s*(?:""([^""]*)""|'([^']*)')", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex FontColor = new Regex(@"\bcolor\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RgbFunction = new Regex(@"^rgba?\(\s*(\d+)\s*[, ]\s*(\d+)\s*[, ]\s*(\d+)\s*(?:[,/]\s*([\d.]+%?)\s*)?\)$", RegexOptions.Compiled);

        /// <summary>CF_HTML(클립보드 "HTML Format")에서 조각(StartFragment~EndFragment)만 꺼낸다. 머리말이 없으면 그대로.</summary>
        public static string ExtractFragment(string cfHtml)
        {
            if (string.IsNullOrEmpty(cfHtml))
                return string.Empty;
            var start = cfHtml.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase);
            var end = cfHtml.IndexOf("<!--EndFragment-->", StringComparison.OrdinalIgnoreCase);
            if (start >= 0 && end > start)
                return cfHtml.Substring(start + "<!--StartFragment-->".Length, end - start - "<!--StartFragment-->".Length);
            var body = cfHtml.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
            if (body >= 0)
            {
                var close = cfHtml.IndexOf('>', body);
                var bodyEnd = cfHtml.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
                if (close > 0)
                    return bodyEnd > close ? cfHtml.Substring(close + 1, bodyEnd - close - 1) : cfHtml.Substring(close + 1);
            }
            var html = cfHtml.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
            return html >= 0 ? cfHtml.Substring(html) : cfHtml;
        }

        private sealed class Element
        {
            public string Name;
            public InlineFormat Format;
            public InlineFormat Clear;   // style로 끈 서식(예: Google 문서가 전체를 감싸는 <b style="font-weight:normal">)
            public string Link;
            public bool Ignore;
            public bool Underline;
            public string Color;
            public string Highlight;
        }

        /// <summary>HTML 조각을 Markdown으로. 블록이 하나뿐이면 줄 안 Markdown 한 줄.</summary>
        public static string FromHtml(string html)
        {
            return FromHtml(html, null);
        }

        /// <summary>
        /// HTML 조각을 Markdown으로. <paramref name="lineMarks"/>가 있으면 Markdown 블록(빈 줄로 나뉜 줄)마다 범위 속성 값(<see cref="TextMarks.Format"/>, 없으면 null)을 같은 순서로 채운다.
        /// </summary>
        public static string FromHtml(string html, List<string> lineMarks)
        {
            html = ExtractFragment(html);
            var lines = new List<string>();
            var runs = new List<InlineRun>();
            var prefix = string.Empty;
            var stack = new List<Element>();
            var listStack = new List<string>();   // "ul" / "ol"
            var orderedCount = new List<int>();
            var inPre = false;
            var pre = new StringBuilder();

            Action flush = () =>
            {
                var md = InlineMarkdown.Serialize(runs).Trim();
                if (md.Length > 0)
                {
                    lines.Add(prefix + md);
                    if (lineMarks != null)
                    {
                        var marks = TextMarks.FromRuns(runs);
                        var plain = InlineMarkdown.ToPlain(md);
                        lineMarks.Add(marks.Count == 0 ? null : TextMarks.Format(TextMarks.Adjust(marks, string.Concat(runs.Select(r => r.Text)), plain), plain));
                    }
                }
                runs.Clear();
                prefix = string.Empty;
            };

            var pos = 0;
            foreach (Match m in Tag.Matches(html))
            {
                if (m.Index > pos)
                    AddText(html.Substring(pos, m.Index - pos), stack, runs, inPre, pre);
                pos = m.Index + m.Length;
                if (m.Value.StartsWith("<!--", StringComparison.Ordinal))
                    continue;
                var closing = m.Groups[1].Value == "/";
                var name = m.Groups[2].Value.ToLowerInvariant();
                var attrs = m.Groups[3].Value;
                var selfClosing = attrs.TrimEnd().EndsWith("/", StringComparison.Ordinal);

                switch (name)
                {
                    case "br":
                        if (inPre) pre.Append('\n');
                        else runs.Add(Styled(new InlineRun { Text = "\n", Format = Current(stack), Link = CurrentLink(stack) }, stack));
                        continue;
                    case "p": case "div": case "h1": case "h2": case "h3": case "h4": case "h5": case "h6": case "li": case "blockquote": case "tr": case "dt": case "dd":
                        flush();
                        if (!closing)
                        {
                            if (name == "h1") prefix = "# ";
                            else if (name == "h2") prefix = "## ";
                            else if (name == "h3") prefix = "### ";
                            else if (name == "h4") prefix = "#### ";
                            else if (name == "h5") prefix = "##### ";
                            else if (name == "h6") prefix = "###### ";
                            else if (name == "blockquote") prefix = "> ";
                            else if (name == "li")
                            {
                                var depth = Math.Max(0, listStack.Count - 1);
                                var ordered = listStack.Count > 0 && listStack[listStack.Count - 1] == "ol";
                                if (ordered)
                                    orderedCount[orderedCount.Count - 1]++;
                                prefix = new string(' ', depth * 2) + (ordered ? orderedCount[orderedCount.Count - 1] + ". " : "- ");
                            }
                        }
                        continue;
                    case "ul": case "ol":
                        flush();
                        if (closing)
                        {
                            if (listStack.Count > 0) listStack.RemoveAt(listStack.Count - 1);
                            if (orderedCount.Count > 0) orderedCount.RemoveAt(orderedCount.Count - 1);
                        }
                        else
                        {
                            listStack.Add(name);
                            orderedCount.Add(0);
                        }
                        continue;
                    case "pre":
                        flush();
                        if (closing)
                        {
                            inPre = false;
                            var code = WebUtility.HtmlDecode(pre.ToString()).TrimEnd('\n', '\r');
                            if (code.Length > 0)
                            {
                                lines.Add("```\n" + code + "\n```");
                                lineMarks?.Add(null);
                            }
                            pre.Clear();
                        }
                        else
                        {
                            inPre = true;
                        }
                        continue;
                    case "td": case "th":
                        if (closing && runs.Count > 0)
                            runs.Add(new InlineRun { Text = " " });
                        continue;
                }

                if (selfClosing)
                    continue;
                if (closing)
                {
                    var at = stack.FindLastIndex(e => e.Name == name);
                    if (at >= 0)
                        stack.RemoveRange(at, stack.Count - at);
                    continue;
                }
                var element = new Element { Name = name };
                switch (name)
                {
                    case "b": case "strong": element.Format = InlineFormat.Bold; break;
                    case "i": case "em": case "cite": element.Format = InlineFormat.Italic; break;
                    case "s": case "strike": case "del": element.Format = InlineFormat.Strike; break;
                    case "code": case "tt": case "kbd": case "samp": element.Format = InlineFormat.Code; break;
                    case "a":
                        var href = Href.Match(attrs);
                        if (href.Success)
                        {
                            var url = WebUtility.HtmlDecode(href.Groups[1].Success ? href.Groups[1].Value : href.Groups[2].Success ? href.Groups[2].Value : href.Groups[3].Value);
                            if (!string.IsNullOrWhiteSpace(url) && !url.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
                                element.Link = url.Trim();
                        }
                        break;
                    case "style": case "script": case "head": case "title":
                        element.Ignore = true;
                        break;
                    case "u": case "ins":
                        element.Underline = true;
                        break;
                    case "mark":
                        element.Highlight = "yellow";
                        break;
                    case "font":
                        var fontColor = FontColor.Match(attrs);
                        if (fontColor.Success)
                            element.Color = NearestColorName(fontColor.Groups[1].Success ? fontColor.Groups[1].Value : fontColor.Groups[2].Success ? fontColor.Groups[2].Value : fontColor.Groups[3].Value, false);
                        break;
                }
                var style = Style.Match(attrs);
                if (style.Success)
                {
                    var css = style.Groups[1].Success ? style.Groups[1].Value : style.Groups[2].Value;
                    InlineFormat off;
                    element.Format |= FromStyle(css, out off);
                    element.Format &= ~off;
                    element.Clear = off;
                    ApplyStyleMarks(css, element);
                }
                stack.Add(element);
            }
            if (pos < html.Length)
                AddText(html.Substring(pos), stack, runs, inPre, pre);
            flush();
            return string.Join("\n\n", lines);
        }

        private static InlineFormat FromStyle(string css, out InlineFormat off)
        {
            var format = InlineFormat.None;
            off = InlineFormat.None;
            foreach (var part in css.Split(';'))
            {
                var kv = part.Split(new[] { ':' }, 2);
                if (kv.Length != 2)
                    continue;
                var key = kv[0].Trim().ToLowerInvariant();
                var value = kv[1].Trim().ToLowerInvariant();
                int weight;
                if (key == "font-weight" && (value == "bold" || value == "bolder" || int.TryParse(value, out weight) && weight >= 600))
                    format |= InlineFormat.Bold;
                else if (key == "font-weight" && (value == "normal" || value == "lighter" || int.TryParse(value, out weight) && weight < 600))
                    off |= InlineFormat.Bold;
                else if (key == "font-style" && (value == "italic" || value == "oblique"))
                    format |= InlineFormat.Italic;
                else if (key == "font-style" && value == "normal")
                    off |= InlineFormat.Italic;
                else if ((key == "text-decoration" || key == "text-decoration-line") && value.Contains("line-through"))
                    format |= InlineFormat.Strike;
                else if (key == "font-family" && (value.Contains("monospace") || value.Contains("consolas") || value.Contains("courier") || value.Contains("mono")))
                    format |= InlineFormat.Code;
            }
            return format;
        }

        /// <summary>CSS의 밑줄·글자 색·배경색을 요소에 둔다.</summary>
        private static void ApplyStyleMarks(string css, Element element)
        {
            foreach (var part in css.Split(';'))
            {
                var kv = part.Split(new[] { ':' }, 2);
                if (kv.Length != 2)
                    continue;
                var key = kv[0].Trim().ToLowerInvariant();
                var value = kv[1].Trim().ToLowerInvariant();
                if ((key == "text-decoration" || key == "text-decoration-line") && value.Contains("underline"))
                    element.Underline = true;
                else if (key == "color")
                    element.Color = NearestColorName(value, false);
                else if (key == "background-color" || key == "background")
                    element.Highlight = NearestColorName(value, true);
            }
        }

        private static readonly Dictionary<string, string> NamedColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "red", "#ff0000" }, { "darkred", "#8b0000" }, { "crimson", "#dc143c" }, { "orange", "#ffa500" }, { "darkorange", "#ff8c00" },
            { "yellow", "#ffff00" }, { "gold", "#ffd700" }, { "green", "#008000" }, { "lime", "#00ff00" }, { "darkgreen", "#006400" },
            { "blue", "#0000ff" }, { "navy", "#000080" }, { "darkblue", "#00008b" }, { "teal", "#008080" }, { "aqua", "#00ffff" }, { "cyan", "#00ffff" },
            { "purple", "#800080" }, { "violet", "#ee82ee" }, { "magenta", "#ff00ff" }, { "fuchsia", "#ff00ff" }, { "pink", "#ffc0cb" }, { "hotpink", "#ff69b4" },
            { "brown", "#a52a2a" }, { "maroon", "#800000" }, { "gray", "#808080" }, { "grey", "#808080" }, { "silver", "#c0c0c0" },
            { "black", "#000000" }, { "white", "#ffffff" },
        };

        /// <summary>
        /// CSS 색 → 가장 가까운 색 이름(<see cref="TextMarks.ColorNames"/>). 검정·흰색에 가까운 색(기본 글자색·바탕), 거의 투명한 색, 모르는 값은 null.
        /// 색상(hue)으로 고르고, 채도가 낮으면 회색, 어두운 주황 계열은 갈색.
        /// </summary>
        public static string NearestColorName(string css, bool background)
        {
            if (string.IsNullOrWhiteSpace(css))
                return null;
            css = css.Trim().ToLowerInvariant();
            var important = css.IndexOf("!important", StringComparison.Ordinal);
            if (important >= 0)
                css = css.Substring(0, important).Trim();
            // background 단축 속성은 색만 본다(첫 낱말)
            if (background && css.IndexOf(' ') > 0 && !css.StartsWith("rgb", StringComparison.Ordinal))
                css = css.Substring(0, css.IndexOf(' '));
            string named;
            if (NamedColors.TryGetValue(css, out named))
                css = named;
            int r, g, b;
            if (css.StartsWith("#", StringComparison.Ordinal))
            {
                var h = css.Substring(1);
                if (h.Length == 3 || h.Length == 4)
                    h = new string(new[] { h[0], h[0], h[1], h[1], h[2], h[2] });
                else if (h.Length == 8)
                    h = h.Substring(0, 6);
                int value;
                if (h.Length != 6 || !int.TryParse(h, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out value))
                    return null;
                r = (value >> 16) & 0xff;
                g = (value >> 8) & 0xff;
                b = value & 0xff;
            }
            else
            {
                var m = RgbFunction.Match(css);
                if (!m.Success)
                    return null;
                if (m.Groups[4].Success)
                {
                    var alpha = m.Groups[4].Value;
                    var percent = alpha.EndsWith("%", StringComparison.Ordinal);
                    double a;
                    if (double.TryParse(alpha.TrimEnd('%'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out a) && (percent ? a < 20 : a < 0.2))
                        return null;   // 거의 투명
                }
                r = Math.Min(255, int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
                g = Math.Min(255, int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
                b = Math.Min(255, int.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture));
            }
            var rf = r / 255.0;
            var gf = g / 255.0;
            var bf = b / 255.0;
            var max = Math.Max(rf, Math.Max(gf, bf));
            var min = Math.Min(rf, Math.Min(gf, bf));
            var light = (max + min) / 2;
            var delta = max - min;
            var saturation = delta == 0 ? 0 : delta / (1 - Math.Abs(2 * light - 1));
            if (light < 0.12 || light > 0.94)
                return null;   // 검정·흰색(기본 글자색·바탕)
            if (saturation < 0.15 || delta < 0.08)
                return light < 0.85 ? "gray" : null;
            double hue;
            if (max == rf) hue = 60 * (((gf - bf) / delta) % 6);
            else if (max == gf) hue = 60 * ((bf - rf) / delta + 2);
            else hue = 60 * ((rf - gf) / delta + 4);
            if (hue < 0)
                hue += 360;
            if (hue < 12 || hue >= 345) return light < 0.3 ? "brown" : "red";
            if (hue < 40) return light < 0.4 ? "brown" : "orange";
            if (hue < 70) return "yellow";
            if (hue < 165) return "green";
            if (hue < 255) return "blue";
            if (hue < 315) return "purple";
            return "pink";
        }

        private static InlineRun Styled(InlineRun run, List<Element> stack)
        {
            foreach (var e in stack)
            {
                if (e.Underline) run.Underline = true;
                if (e.Color != null) run.TextColor = e.Color;
                if (e.Highlight != null) run.Highlight = e.Highlight;
            }
            return run;
        }

        private static InlineFormat Current(List<Element> stack)
        {
            var f = InlineFormat.None;
            foreach (var e in stack)
                f = (f | e.Format) & ~e.Clear;
            return f;
        }

        private static string CurrentLink(List<Element> stack)
        {
            for (var i = stack.Count - 1; i >= 0; i--)
                if (stack[i].Link != null)
                    return stack[i].Link;
            return null;
        }

        private static void AddText(string raw, List<Element> stack, List<InlineRun> runs, bool inPre, StringBuilder pre)
        {
            if (stack.Any(e => e.Ignore))
                return;
            if (inPre)
            {
                pre.Append(raw);
                return;
            }
            // HTML 공백 규칙: 줄바꿈·탭·연속 공백은 공백 하나
            var text = Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(raw, @"[\r\n\t ]+", " ")), " ", " ");
            if (text.Length == 0)
                return;
            if (runs.Count == 0 || runs[runs.Count - 1].Text.EndsWith("\n", StringComparison.Ordinal))
                text = text.TrimStart(' ');
            if (text.Length == 0)
                return;
            runs.Add(Styled(new InlineRun { Text = text, Format = Current(stack), Link = CurrentLink(stack) }, stack));
        }
    }
}

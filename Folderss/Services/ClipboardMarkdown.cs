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
    /// 문단·제목(h1~h3)·목록·인용·줄바꿈은 블록 Markdown 줄로 바꾼다. 나머지 태그(표·그림·글꼴·색)는 글자만 남긴다. WPF 없음(단위 테스트).
    /// </summary>
    public static class ClipboardMarkdown
    {
        private static readonly Regex Tag = new Regex(@"<!--[\s\S]*?-->|<(/?)([A-Za-z][A-Za-z0-9]*)((?:[^>""']|""[^""]*""|'[^']*')*)>", RegexOptions.Compiled);
        private static readonly Regex Href = new Regex(@"\bhref\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex Style = new Regex(@"\bstyle\s*=\s*(?:""([^""]*)""|'([^']*)')", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
            public string Link;
            public bool Ignore;
        }

        /// <summary>HTML 조각을 Markdown으로. 블록이 하나뿐이면 줄 안 Markdown 한 줄.</summary>
        public static string FromHtml(string html)
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
                runs.Clear();
                if (md.Length > 0)
                    lines.Add(prefix + md);
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
                        else runs.Add(new InlineRun { Text = "\n", Format = Current(stack), Link = CurrentLink(stack) });
                        continue;
                    case "p": case "div": case "h1": case "h2": case "h3": case "h4": case "h5": case "h6": case "li": case "blockquote": case "tr": case "dt": case "dd":
                        flush();
                        if (!closing)
                        {
                            if (name == "h1") prefix = "# ";
                            else if (name == "h2") prefix = "## ";
                            else if (name == "h3") prefix = "### ";
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
                                lines.Add("```\n" + code + "\n```");
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
                }
                var style = Style.Match(attrs);
                if (style.Success)
                    element.Format |= FromStyle(style.Groups[1].Success ? style.Groups[1].Value : style.Groups[2].Value);
                stack.Add(element);
            }
            if (pos < html.Length)
                AddText(html.Substring(pos), stack, runs, inPre, pre);
            flush();
            return string.Join("\n\n", lines);
        }

        private static InlineFormat FromStyle(string css)
        {
            var format = InlineFormat.None;
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
                else if (key == "font-style" && (value == "italic" || value == "oblique"))
                    format |= InlineFormat.Italic;
                else if ((key == "text-decoration" || key == "text-decoration-line") && value.Contains("line-through"))
                    format |= InlineFormat.Strike;
                else if (key == "font-family" && (value.Contains("monospace") || value.Contains("consolas") || value.Contains("courier") || value.Contains("mono")))
                    format |= InlineFormat.Code;
            }
            return format;
        }

        private static InlineFormat Current(List<Element> stack)
        {
            var f = InlineFormat.None;
            foreach (var e in stack)
                f |= e.Format;
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
            runs.Add(new InlineRun { Text = text, Format = Current(stack), Link = CurrentLink(stack) });
        }
    }
}

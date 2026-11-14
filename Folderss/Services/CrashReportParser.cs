using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Folderss.Services
{
    /// <summary>크래시 정보 창의 항목 하나(시각·출처·한 줄 요약·복사할 전체 내용).</summary>
    public sealed class CrashReportEntry
    {
        public DateTime Time { get; set; }
        public string Source { get; set; }
        public string Summary { get; set; }
        public string Detail { get; set; }

        /// <summary>목록 표시 글자.</summary>
        public string Display
        {
            get { return string.Format("{0:yyyy-MM-dd HH:mm:ss}  [{1}]  {2}", Time, Source, Summary); }
        }

        /// <summary>복사·붙여넣기용 글자(머리 줄 + 전체 내용).</summary>
        public string ToClipboardText()
        {
            return string.Format("### {0:yyyy-MM-dd HH:mm:ss} [{1}] {2}{3}{4}", Time, Source, Summary, Environment.NewLine, (Detail ?? string.Empty).TrimEnd());
        }
    }

    /// <summary>crash-log.txt·plugin-log.txt를 항목으로 나누고 요약을 만든다 (순수 로직).</summary>
    public static class CrashReportParser
    {
        public const string CrashLogSource = "크래시 로그";
        public const string PluginLogSource = "플러그인 로그";

        private static readonly Regex StampLine = new Regex(@"^\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})(\.\d+)?\]\s*(.*)$", RegexOptions.Compiled);
        private static readonly Regex ExceptionLine = new Regex(@"^\[0\]\s+(.+)$", RegexOptions.Compiled);

        /// <summary><see cref="CrashLog"/>가 쓴 파일: "=====" 줄로 나뉜 항목들.</summary>
        public static List<CrashReportEntry> ParseCrashLog(string text)
        {
            var result = new List<CrashReportEntry>();
            if (string.IsNullOrWhiteSpace(text))
                return result;
            var blocks = Regex.Split(Normalize(text), @"^={10,}\s*$", RegexOptions.Multiline);
            foreach (var raw in blocks)
            {
                var block = raw.Trim('\n');
                if (block.Trim().Length == 0)
                    continue;
                var lines = block.Split('\n');
                var head = lines.Select(l => StampLine.Match(l)).FirstOrDefault(m => m.Success);
                var exception = lines.Select(l => ExceptionLine.Match(l)).FirstOrDefault(m => m.Success);
                var title = head != null ? head.Groups[3].Value : "(제목 없음)";
                result.Add(new CrashReportEntry
                {
                    Time = head != null ? ParseStamp(head.Groups[1].Value) : DateTime.MinValue,
                    Source = CrashLogSource,
                    Summary = exception != null ? title + " — " + Shorten(exception.Groups[1].Value) : title,
                    Detail = block,
                });
            }
            return result;
        }

        /// <summary>plugin-log.txt: "[yyyy-MM-dd HH:mm:ss] 메시지"로 시작하는 항목들(여러 줄).</summary>
        public static List<CrashReportEntry> ParsePluginLog(string text)
        {
            var result = new List<CrashReportEntry>();
            if (string.IsNullOrWhiteSpace(text))
                return result;
            CrashReportEntry current = null;
            var body = new StringBuilder();
            foreach (var line in Normalize(text).Split('\n'))
            {
                var match = StampLine.Match(line);
                if (match.Success)
                {
                    Flush(result, current, body);
                    current = new CrashReportEntry
                    {
                        Time = ParseStamp(match.Groups[1].Value),
                        Source = PluginLogSource,
                        Summary = match.Groups[3].Value,
                    };
                    body.Clear();
                    body.Append(line).Append('\n');
                }
                else if (current != null)
                {
                    body.Append(line).Append('\n');
                }
            }
            Flush(result, current, body);
            return result;
        }

        private static void Flush(List<CrashReportEntry> result, CrashReportEntry entry, StringBuilder body)
        {
            if (entry == null)
                return;
            entry.Detail = body.ToString().TrimEnd('\n');
            // 비정상 종료 항목은 원인 줄, 예외 항목은 첫 줄 그대로가 요약으로 낫다.
            var cause = entry.Detail.Split('\n').FirstOrDefault(l => l.StartsWith("원인:", StringComparison.Ordinal));
            if (cause != null)
                entry.Summary = entry.Summary + " — " + Shorten(cause);
            else
                entry.Summary = Shorten(entry.Summary);
            result.Add(entry);
        }

        /// <summary>최신 항목이 위로.</summary>
        public static List<CrashReportEntry> SortNewestFirst(IEnumerable<CrashReportEntry> entries)
        {
            return entries.OrderByDescending(e => e.Time).ToList();
        }

        /// <summary>여러 항목을 붙여넣기 좋은 글자 하나로.</summary>
        public static string JoinForClipboard(IEnumerable<CrashReportEntry> entries)
        {
            var nl = Environment.NewLine;
            return string.Join(nl + nl, entries.Select(e => e.ToClipboardText()));
        }

        public static string Shorten(string text, int max = 160)
        {
            text = (text ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ').Trim();
            return text.Length <= max ? text : text.Substring(0, max) + "…";
        }

        private static string Normalize(string text)
        {
            return text.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private static DateTime ParseStamp(string value)
        {
            DateTime time;
            return DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out time)
                ? time
                : DateTime.MinValue;
        }
    }
}

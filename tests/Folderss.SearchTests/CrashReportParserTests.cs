using System;
using System.Linq;
using Folderss.Services;
using Xunit;

namespace Folderss.SearchTests
{
    public class CrashReportParserTests
    {
        private const string CrashLogText =
            "==================================================================\r\n" +
            "[2026-11-14 08:08:37.959] UI 스레드 처리되지 않은 예외(종료됨)\r\n" +
            "버전: 1.9.2.0  pid: 1  CLR: 8.0  OS: x  64bit: True\r\n" +
            "\r\n" +
            "[0] System.InvalidOperationException: 바깥\r\n" +
            "[1] System.FormatException: 안쪽\r\n" +
            "\r\n" +
            "System.InvalidOperationException: 바깥\r\n" +
            "   at A.B()\r\n" +
            "\r\n" +
            "==================================================================\r\n" +
            "[2026-11-15 09:00:00.001] 관찰되지 않은 Task 예외(앱은 계속 실행)\r\n" +
            "\r\n" +
            "[0] System.TimeoutException: 시간 초과\r\n";

        [Fact]
        public void CrashLog_SplitsEntries_WithTimeTitleAndFirstException()
        {
            var entries = CrashReportParser.ParseCrashLog(CrashLogText);

            Assert.Equal(2, entries.Count);
            Assert.Equal(new DateTime(2026, 11, 14, 8, 8, 37), entries[0].Time);
            Assert.Equal("UI 스레드 처리되지 않은 예외(종료됨) — System.InvalidOperationException: 바깥", entries[0].Summary);
            Assert.Contains("   at A.B()", entries[0].Detail);
            Assert.DoesNotContain("시간 초과", entries[0].Detail);
            Assert.Equal(CrashReportParser.CrashLogSource, entries[1].Source);
        }

        [Fact]
        public void PluginLog_MultiLineEntries_UseCauseLineInSummary()
        {
            var text =
                "[2026-10-05 10:28:26] 플러그인 x의 처리되지 않은 UI 예외(본체는 계속 실행): System.InvalidOperationException: 오류\n" +
                "   at X.Y()\n" +
                "\n" +
                "[2026-11-14 08:17:23] 이전 실행이 정상 종료되지 않음\n" +
                "2026-11-13 06:40:08 시작한 실행(pid 42372), 로드된 플러그인: a\n" +
                "원인: 기록 없음 — 강제 종료로 추정\n";

            var entries = CrashReportParser.ParsePluginLog(text);

            Assert.Equal(2, entries.Count);
            Assert.Contains("   at X.Y()", entries[0].Detail);
            Assert.Equal("이전 실행이 정상 종료되지 않음 — 원인: 기록 없음 — 강제 종료로 추정", entries[1].Summary);
            Assert.Equal(new DateTime(2026, 11, 14, 8, 17, 23), entries[1].Time);
        }

        [Fact]
        public void Sort_And_Join_NewestFirstWithHeaders()
        {
            var entries = CrashReportParser.SortNewestFirst(CrashReportParser.ParseCrashLog(CrashLogText));
            var text = CrashReportParser.JoinForClipboard(entries);

            Assert.Equal(new DateTime(2026, 11, 15, 9, 0, 0), entries[0].Time);
            Assert.StartsWith("### 2026-11-15 09:00:00 [크래시 로그]", text);
            Assert.Contains("### 2026-11-14 08:08:37 [크래시 로그]", text);
        }

        [Fact]
        public void EmptyOrGarbage_GivesNoEntries()
        {
            Assert.Empty(CrashReportParser.ParseCrashLog(""));
            Assert.Empty(CrashReportParser.ParsePluginLog("머리 없는 줄\n또 한 줄"));
        }
    }
}

using Folderss.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>플러그인 종료 감지 기록(plugin-sessions)과 플러그인에 보여 주는 본체 설정 키.</summary>
    public sealed class PluginSessionTests : IDisposable
    {
        private readonly string _root;

        public PluginSessionTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "folderss-plugin-session-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private static PluginSessionRecord Record(int pid, string reason = null)
        {
            return new PluginSessionRecord
            {
                ProcessId = pid,
                StartedAtUtc = new DateTime(2026, 9, 30, 1, 0, pid % 60, DateTimeKind.Utc),
                Plugins = new List<string> { "sample.hello" },
                ExitReason = reason
            };
        }

        [Fact]
        public void TakeStale_ReturnsAndDeletesDeadRecords_KeepsRunningOnes()
        {
            PluginSessionLog.Write(_root, Record(100, "Environment.Exit"));
            PluginSessionLog.Write(_root, Record(200));
            File.WriteAllText(Path.Combine(_root, "300.json"), "{ broken");

            var stale = PluginSessionLog.TakeStale(_root, r => r.ProcessId == 200);

            var only = Assert.Single(stale);
            Assert.Equal(100, only.ProcessId);
            Assert.Equal("Environment.Exit", only.ExitReason);
            Assert.Equal(new[] { "sample.hello" }, only.Plugins);
            Assert.Equal(new[] { "200.json" }, Directory.GetFiles(_root).Select(Path.GetFileName));
            Assert.Empty(PluginSessionLog.TakeStale(_root, r => r.ProcessId == 200));
        }

        [Fact]
        public void Delete_RemovesOwnRecordOnly()
        {
            PluginSessionLog.Write(_root, Record(1));
            PluginSessionLog.Write(_root, Record(2));

            PluginSessionLog.Delete(_root, 1);
            PluginSessionLog.Delete(_root, 99);

            Assert.Equal(new[] { "2.json" }, Directory.GetFiles(_root).Select(Path.GetFileName));
        }

        [Fact]
        public void TakeStale_MissingDirectory_Empty()
        {
            Assert.Empty(PluginSessionLog.TakeStale(Path.Combine(_root, "none"), r => false));
        }

        [Fact]
        public void Describe_NoReason_SaysForcedTermination()
        {
            var text = PluginSessionLog.Describe(Record(5));
            Assert.Contains("sample.hello", text);
            Assert.Contains("강제 종료", text);
            Assert.Contains("Environment.Exit", PluginSessionLog.Describe(Record(5, "Environment.Exit 호출")));
        }

        [Fact]
        public void AppendLog_Appends()
        {
            var log = Path.Combine(_root, "logs", "plugin-log.txt");
            PluginSessionLog.AppendLog(log, "첫째");
            PluginSessionLog.AppendLog(log, "둘째");

            var content = File.ReadAllText(log);
            Assert.True(content.IndexOf("첫째", StringComparison.Ordinal) < content.IndexOf("둘째", StringComparison.Ordinal));
        }

        [Fact]
        public void AppSettings_ExposesDocumentedKeysOnly()
        {
            var git = new GitSettings { GitExecutablePath = @"C:\git\git.exe", ExcludedFolders = new List<string> { "node_modules", "bin" } };
            var values = PluginAppSettings.Build("Nord", git, new DiffSettings { IgnoreWhitespace = true }, new ConsoleSettings { FontSize = 15 });

            Assert.Equal("Nord", values["theme"]);
            Assert.Equal(@"C:\git\git.exe", values["git.executablePath"]);
            Assert.Equal("node_modules\nbin", values["git.excludedFolders"]);
            Assert.Equal("true", values["diff.ignoreWhitespace"]);
            Assert.Equal("15", values["console.fontSize"]);
            Assert.Equal(new[]
            {
                "console.fontSize", "console.preferredProfileKey",
                "diff.fallbackEncoding", "diff.ignoreWhitespace", "diff.toolArguments", "diff.toolMode", "diff.toolPath", "diff.viewMode",
                "git.baseFolderMode", "git.excludedFolders", "git.executablePath", "git.logAllBranches", "git.logLimit", "git.pullMode", "git.scanDepth",
                "theme"
            }, values.Keys.OrderBy(k => k, StringComparer.Ordinal));
            Assert.DoesNotContain(values.Keys, k => k.StartsWith("plugin", StringComparison.OrdinalIgnoreCase));
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Folderss.Services
{
    /// <summary>플러그인을 로드한 실행 한 번의 기록 (<c>plugin-sessions\&lt;pid&gt;.json</c>).</summary>
    public sealed class PluginSessionRecord
    {
        public int ProcessId { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public List<string> Plugins { get; set; } = new List<string>();
        /// <summary>종료 직전에 알아낸 원인. 강제 종료(Kill·FailFast·스택 오버플로·네이티브 크래시)면 기록할 틈이 없어 null로 남는다.</summary>
        public string ExitReason { get; set; }
    }

    /// <summary>
    /// 플러그인 때문에 본체가 종료됐는지 다음 실행에서라도 알 수 있게 하는 기록과 로그. 순수 System.IO 로직(테스트 대상).
    /// 플러그인을 처음 로드할 때 기록을 쓰고, 사용자가 정상 종료하면 지운다. 다음 시작 때 주인(실행 중인 같은 프로세스)이 없는
    /// 기록이 남아 있으면 그 실행은 정상 종료되지 않은 것이다. Folderss는 여러 개 동시에 실행될 수 있어 프로세스별 파일을 쓴다.
    /// </summary>
    public static class PluginSessionLog
    {
        private static readonly object LogGate = new object();

        public static string GetRecordPath(string directory, int processId)
        {
            return Path.Combine(directory, processId + ".json");
        }

        public static void Write(string directory, PluginSessionRecord record)
        {
            SettingsFile.WriteAllText(GetRecordPath(directory, record.ProcessId),
                JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
        }

        public static void Delete(string directory, int processId)
        {
            var path = GetRecordPath(directory, processId);
            if (File.Exists(path))
                File.Delete(path);
        }

        /// <summary>
        /// 주인 없는 기록을 읽어서 지우고 돌려준다. <paramref name="isRunning"/>이 true인 기록(지금 실행 중인 다른 창)은 건드리지 않는다.
        /// 읽지 못하는 파일도 지운다 — 남겨 두면 매번 시작할 때마다 같은 실패를 반복한다.
        /// </summary>
        public static List<PluginSessionRecord> TakeStale(string directory, Func<PluginSessionRecord, bool> isRunning)
        {
            var result = new List<PluginSessionRecord>();
            if (!Directory.Exists(directory))
                return result;

            foreach (var path in Directory.GetFiles(directory, "*.json"))
            {
                PluginSessionRecord record = null;
                try { record = JsonSerializer.Deserialize<PluginSessionRecord>(File.ReadAllText(path)); }
                catch (Exception) { }

                if (record != null && isRunning(record))
                    continue;
                if (record != null)
                    result.Add(record);
                try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            return result.OrderBy(r => r.StartedAtUtc).ToList();
        }

        public static string Describe(PluginSessionRecord record)
        {
            return string.Format("{0:yyyy-MM-dd HH:mm:ss} 시작한 실행(pid {1}), 로드된 플러그인: {2}\n원인: {3}",
                record.StartedAtUtc.ToLocalTime(), record.ProcessId,
                record.Plugins.Count == 0 ? "(없음)" : string.Join(", ", record.Plugins),
                string.IsNullOrWhiteSpace(record.ExitReason)
                    ? "기록 없음 — 강제 종료(프로세스 종료, FailFast, 스택 오버플로, 네이티브 크래시, 전원·OS 강제 종료 등)로 추정"
                    : record.ExitReason);
        }

        /// <summary>로그 파일에 한 항목을 덧붙인다. 여러 스레드(종료 처리·예외 처리)에서 불릴 수 있다.</summary>
        public static void AppendLog(string logPath, string message)
        {
            lock (LogGate)
            {
                var directory = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.AppendAllText(logPath,
                    string.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1}{2}{2}", DateTime.Now, message, Environment.NewLine),
                    new UTF8Encoding(false));
            }
        }
    }
}

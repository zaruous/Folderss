using System;
using System.IO;
using System.Security.Principal;
using System.Text;

namespace Folderss.Services
{
    /// <summary>
    /// Windows 휴지통. 보내기는 셸(<see cref="FileOperationService.MoveToRecycleBin"/>), 되살리기는 휴지통 폴더의
    /// 정보 파일(<c>$I…</c>: 원래 경로·삭제 시각)을 읽어 짝인 <c>$R…</c> 항목을 원래 자리로 옮긴다.
    /// 셸 COM으로 찾으면 표시 이름에서 확장자가 숨겨질 수 있어 원래 경로를 정확히 맞추기 어렵다.
    /// 고정 드라이브만 받는다 — 네트워크·이동식 드라이브는 휴지통이 없어 보내기가 곧 영구 삭제가 된다.
    /// </summary>
    public sealed class WindowsRecycleBin : IRecycleBin
    {
        /// <summary>셸이 기록한 삭제 시각과 <see cref="Send"/>가 돌려준 시각의 허용 차이.</summary>
        private static readonly TimeSpan MatchWindow = TimeSpan.FromMinutes(2);

        public DateTime Send(string path)
        {
            EnsureRecyclable(path);
            FileOperationService.MoveToRecycleBin(path);
            if (File.Exists(path) || Directory.Exists(path))
                throw new IOException("휴지통으로 보내지 못했습니다: " + path);
            return DateTime.UtcNow;
        }

        public bool CanRestore(string originalPath, DateTime sentUtc) => Find(originalPath, sentUtc) != null;

        public void Restore(string originalPath, DateTime sentUtc)
        {
            var info = Find(originalPath, sentUtc)
                       ?? throw new IOException("휴지통에서 찾지 못했습니다(비웠거나 크기 제한으로 바로 지워졌을 수 있음): " + originalPath);
            var stored = Path.Combine(Path.GetDirectoryName(info), "$R" + Path.GetFileName(info).Substring(2));
            if (File.Exists(originalPath) || Directory.Exists(originalPath))
                throw new IOException("원래 자리에 같은 이름이 있습니다: " + originalPath);

            var parent = Path.GetDirectoryName(originalPath);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);
            if (Directory.Exists(stored))
                Directory.Move(stored, originalPath);
            else
                File.Move(stored, originalPath);
            File.Delete(info);
        }

        /// <summary>원래 경로가 같고 삭제 시각이 가장 가까운 정보 파일(<c>$I…</c>) 경로. 없으면 null.</summary>
        private static string Find(string originalPath, DateTime sentUtc)
        {
            var folder = GetUserBinFolder(originalPath);
            if (folder == null || !Directory.Exists(folder))
                return null;

            var target = Path.GetFullPath(originalPath);
            string best = null;
            var bestGap = TimeSpan.MaxValue;
            foreach (var info in Directory.EnumerateFiles(folder, "$I*"))
            {
                RecycleInfo parsed;
                try { parsed = ParseInfoFile(File.ReadAllBytes(info)); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is FormatException) { continue; }

                if (!string.Equals(parsed.OriginalPath, target, StringComparison.OrdinalIgnoreCase))
                    continue;
                var stored = Path.Combine(folder, "$R" + Path.GetFileName(info).Substring(2));
                if (!File.Exists(stored) && !Directory.Exists(stored))
                    continue;
                var gap = (parsed.DeletedUtc - sentUtc).Duration();
                if (gap <= MatchWindow && gap < bestGap)
                {
                    best = info;
                    bestGap = gap;
                }
            }
            return best;
        }

        private static void EnsureRecyclable(string path)
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal) ||
                new DriveInfo(root).DriveType != DriveType.Fixed)
                throw new IOException("휴지통을 쓸 수 없는 위치입니다(네트워크·이동식 드라이브). 되돌릴 수 없어 작업하지 않습니다: " + path);
        }

        private static string GetUserBinFolder(string path)
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            var sid = WindowsIdentity.GetCurrent().User?.Value;
            return string.IsNullOrEmpty(root) || sid == null ? null : Path.Combine(root, "$Recycle.Bin", sid);
        }

        internal sealed class RecycleInfo
        {
            public string OriginalPath { get; set; }
            public DateTime DeletedUtc { get; set; }
        }

        /// <summary>
        /// <c>$I</c> 파일: 버전(8) · 원래 크기(8) · 삭제 시각 FILETIME(8) · 경로.
        /// 버전 1(Vista~8)은 경로가 260자 고정, 버전 2(Windows 10~)는 글자 수(4, 끝의 null 포함) 뒤에 경로.
        /// </summary>
        internal static RecycleInfo ParseInfoFile(byte[] data)
        {
            if (data == null || data.Length < 24)
                throw new FormatException("휴지통 정보 파일이 너무 짧습니다.");

            var version = BitConverter.ToInt64(data, 0);
            var deleted = DateTime.FromFileTimeUtc(BitConverter.ToInt64(data, 16));
            string path;
            if (version == 1)
            {
                path = ReadUtf16(data, 24, Math.Min(520, data.Length - 24));
            }
            else if (version == 2)
            {
                if (data.Length < 28)
                    throw new FormatException("휴지통 정보 파일이 너무 짧습니다.");
                var chars = BitConverter.ToInt32(data, 24);
                if (chars < 0 || 28 + chars * 2L > data.Length)
                    throw new FormatException("휴지통 정보 파일의 경로 길이가 잘못되었습니다.");
                path = ReadUtf16(data, 28, chars * 2);
            }
            else
            {
                throw new FormatException("알 수 없는 휴지통 정보 파일 버전: " + version);
            }
            return new RecycleInfo { OriginalPath = path, DeletedUtc = deleted };
        }

        private static string ReadUtf16(byte[] data, int offset, int length)
        {
            var text = Encoding.Unicode.GetString(data, offset, length);
            var end = text.IndexOf('\0');
            return end >= 0 ? text.Substring(0, end) : text;
        }
    }
}

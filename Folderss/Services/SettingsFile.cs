using System;
using System.IO;
using System.Text;

namespace Folderss.Services
{
    /// <summary>
    /// 설정 파일 쓰기 공통 헬퍼. 임시 파일(<c>&lt;경로&gt;.tmp</c>)에 먼저 쓴 뒤 교체하므로
    /// 쓰기 도중 프로세스가 종료돼도 기존 파일이 반쪽으로 남지 않는다.
    /// 실패는 삼키지 않고 예외로 알린다 — 설정 창이 항목별로 모아서 사용자에게 보여준다.
    /// 교체는 <see cref="File.Move(string, string, bool)"/>를 쓴다. <see cref="File.Replace"/>는 대상 파일을
    /// 다른 프로세스(백신·인덱서)가 잡고 있거나 비NTFS·네트워크 프로필에서 더 자주 실패한다.
    /// </summary>
    public static class SettingsFile
    {
        /// <summary>
        /// <paramref name="writeToPath"/>가 임시 경로에 내용을 다 쓰면 <paramref name="path"/>로 교체한다.
        /// XmlDocument.Save, XmlSerializer 등 스트림 기반 쓰기에 사용한다.
        /// </summary>
        public static void Write(string path, Action<string> writeToPath)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A settings path is required.", nameof(path));
            if (writeToPath == null)
                throw new ArgumentNullException(nameof(writeToPath));

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var temporaryPath = path + ".tmp";
            try
            {
                writeToPath(temporaryPath);
                File.Move(temporaryPath, path, true);
            }
            catch
            {
                // 실패한 임시 파일은 지워 두고 예외는 그대로 올린다. 삭제 실패는 원래 예외를 가리지 않도록 무시한다.
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
                throw;
            }
        }

        /// <summary>텍스트를 지정 인코딩으로 쓴다.</summary>
        public static void WriteAllText(string path, string content, Encoding encoding)
        {
            Write(path, temporaryPath => File.WriteAllText(temporaryPath, content ?? string.Empty, encoding));
        }

        /// <summary>텍스트를 BOM 없는 UTF-8로 쓴다 (<see cref="File.WriteAllText(string, string)"/>와 같은 바이트).</summary>
        public static void WriteAllText(string path, string content)
        {
            WriteAllText(path, content, new UTF8Encoding(false));
        }
    }
}

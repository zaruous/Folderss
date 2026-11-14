using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Folderss.Services
{
    /// <summary>
    /// 실행 중에 공용 .NET 런타임이 업데이트로 지워졌는지 판단한다 (순수 로직).
    /// 프레임워크 종속 배포는 C:\Program Files\dotnet\shared\…\8.0.x\의 DLL을 필요할 때 처음 불러온다.
    /// Windows Update가 새 패치를 깔며 옛 버전 폴더를 지우면, 그 뒤 처음 쓰는 기능(예: RichTextBox 잘라내기의
    /// System.Text.Encoding.CodePages)에서 파일을 못 찾아 앱이 종료된다(docs/items/crash-log.md).
    /// </summary>
    public static class RuntimeWatch
    {
        /// <summary>
        /// 감시할 런타임 파일(이미 로드된 프레임워크 어셈블리 경로). 앱 폴더 안의 것(self-contained 배포)은 업데이트로 지워지지 않으므로 뺀다.
        /// </summary>
        public static List<string> WatchedFiles(IEnumerable<string> loadedFrameworkAssemblies, string appBaseDirectory)
        {
            var baseDir = NormalizeDir(appBaseDirectory);
            return loadedFrameworkAssemblies
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Where(p => baseDir == null || !p.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>감시 파일 중 사라진 것(없으면 빈 목록).</summary>
        public static List<string> MissingFiles(IEnumerable<string> watchedFiles, Func<string, bool> fileExists)
        {
            return watchedFiles.Where(p => !fileExists(p)).ToList();
        }

        /// <summary>런타임 폴더 이름에서 버전(…\Microsoft.NETCore.App\8.0.33\x.dll → 8.0.33). 모르면 null.</summary>
        public static string VersionOf(string assemblyPath)
        {
            if (string.IsNullOrEmpty(assemblyPath))
                return null;
            var dir = Path.GetFileName(Path.GetDirectoryName(assemblyPath));
            return !string.IsNullOrEmpty(dir) && char.IsDigit(dir[0]) ? dir : null;
        }

        private static string NormalizeDir(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir))
                return null;
            return dir.EndsWith("\\", StringComparison.Ordinal) || dir.EndsWith("/", StringComparison.Ordinal) ? dir : dir + Path.DirectorySeparatorChar;
        }
    }
}

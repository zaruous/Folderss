using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Folderss.Services
{
    /// <summary>zip 안의 <c>plugin.json</c>.</summary>
    public sealed class PluginManifest
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string Description { get; set; }
        /// <summary>진입점 DLL 파일 이름 (zip 루트 기준 상대 경로).</summary>
        public string Assembly { get; set; }
        /// <summary><c>IFolderssPlugin</c>을 구현한 형식의 전체 이름.</summary>
        public string Type { get; set; }
        /// <summary>true면 설정 창을 열 때 플러그인을 로드해 설정 탭을 받는다.</summary>
        public bool HasSettings { get; set; }

        /// <summary>등록된 zip 경로 (파일에서 읽지 않음).</summary>
        public string PackagePath { get; set; }

        public string DisplayName { get { return string.IsNullOrWhiteSpace(Name) ? Id : Name; } }
    }

    /// <summary>
    /// 플러그인 zip 등록·목록·압축 해제. 순수 System.IO 로직이라 테스트 프로젝트에서 단독 실행된다.
    /// 등록 목록은 별도 파일 없이 <c>plugins</c> 폴더의 <c>&lt;id&gt;.zip</c> 파일 자체다 — 목록 파일과 zip이 어긋날 일이 없다.
    /// </summary>
    public static class PluginPackage
    {
        public const string ManifestFileName = "plugin.json";
        private const string CompleteMarker = ".complete";

        private static readonly Regex IdPattern = new Regex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$");

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        /// <summary>zip의 plugin.json을 읽어 검증한다. 형식이 틀리면 <see cref="InvalidDataException"/>.</summary>
        public static PluginManifest ReadManifest(string zipPath)
        {
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var entry = archive.Entries.FirstOrDefault(e =>
                    string.Equals(e.FullName, ManifestFileName, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                    throw new InvalidDataException("zip 루트에 " + ManifestFileName + "이 없습니다.");

                PluginManifest manifest;
                using (var stream = entry.Open())
                {
                    try
                    {
                        manifest = JsonSerializer.Deserialize<PluginManifest>(stream, JsonOptions);
                    }
                    catch (JsonException ex)
                    {
                        throw new InvalidDataException(ManifestFileName + " 형식이 잘못되었습니다: " + ex.Message, ex);
                    }
                }

                Validate(manifest);
                if (!archive.Entries.Any(e => string.Equals(NormalizeEntryName(e.FullName), NormalizeEntryName(manifest.Assembly),
                        StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("zip에 assembly로 지정한 파일(" + manifest.Assembly + ")이 없습니다.");

                manifest.PackagePath = zipPath;
                return manifest;
            }
        }

        public static void Validate(PluginManifest manifest)
        {
            if (manifest == null)
                throw new InvalidDataException(ManifestFileName + "이 비어 있습니다.");
            if (string.IsNullOrWhiteSpace(manifest.Id) || !IdPattern.IsMatch(manifest.Id))
                throw new InvalidDataException("id는 영문·숫자·. _ -만 쓸 수 있습니다(최대 64자): " + manifest.Id);
            if (string.IsNullOrWhiteSpace(manifest.Assembly) || !manifest.Assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                || Path.IsPathRooted(manifest.Assembly) || manifest.Assembly.Split('/', '\\').Contains(".."))
                throw new InvalidDataException("assembly는 zip 안의 .dll 상대 경로여야 합니다: " + manifest.Assembly);
            if (string.IsNullOrWhiteSpace(manifest.Type))
                throw new InvalidDataException("type(IFolderssPlugin 구현 형식 이름)이 없습니다.");
        }

        /// <summary><paramref name="pluginsDirectory"/>의 zip을 모두 읽는다. 읽지 못한 zip은 <paramref name="errors"/>에 담고 건너뛴다.</summary>
        public static List<PluginManifest> List(string pluginsDirectory, List<string> errors = null)
        {
            var result = new List<PluginManifest>();
            if (!Directory.Exists(pluginsDirectory))
                return result;

            foreach (var zip in Directory.GetFiles(pluginsDirectory, "*.zip").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var manifest = ReadManifest(zip);
                    // 파일 이름과 id가 다르면(수동으로 넣은 zip 등) 같은 id가 두 번 보일 수 있어 파일 이름을 기준으로 한다.
                    if (!string.Equals(Path.GetFileNameWithoutExtension(zip), manifest.Id, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("파일 이름이 id(" + manifest.Id + ")와 다릅니다.");
                    result.Add(manifest);
                }
                catch (Exception ex)
                {
                    errors?.Add(Path.GetFileName(zip) + ": " + ex.Message);
                }
            }
            return result;
        }

        public static string GetPackagePath(string pluginsDirectory, string id)
        {
            return Path.Combine(pluginsDirectory, id + ".zip");
        }

        /// <summary>zip을 plugins 폴더에 <c>&lt;id&gt;.zip</c>으로 복사한다. 같은 id가 있으면 덮어쓴다.</summary>
        public static PluginManifest Install(string sourceZip, string pluginsDirectory)
        {
            var manifest = ReadManifest(sourceZip);
            Directory.CreateDirectory(pluginsDirectory);
            var target = GetPackagePath(pluginsDirectory, manifest.Id);
            if (!string.Equals(Path.GetFullPath(sourceZip), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            {
                var temporary = target + ".tmp";
                File.Copy(sourceZip, temporary, true);
                File.Move(temporary, target, true);
            }
            manifest.PackagePath = target;
            return manifest;
        }

        /// <summary>
        /// zip을 <paramref name="extractRoot"/>\&lt;id&gt;-&lt;zip 해시&gt;에 푼다. 같은 내용이 이미 풀려 있으면 그대로 쓴다.
        /// 해시별 폴더를 쓰는 이유: 이전 실행이나 다른 Folderss 창이 로드한 DLL은 잠겨 있어 같은 폴더에 덮어쓸 수 없다.
        /// </summary>
        public static string Extract(PluginManifest manifest, string extractRoot)
        {
            var target = Path.Combine(extractRoot, manifest.Id + "-" + ComputeShortHash(manifest.PackagePath));
            if (File.Exists(Path.Combine(target, CompleteMarker)))
                return target;

            if (Directory.Exists(target))
                Directory.Delete(target, true);
            Directory.CreateDirectory(target);
            ExtractSafely(manifest.PackagePath, target);
            File.WriteAllText(Path.Combine(target, CompleteMarker), string.Empty);
            return target;
        }

        /// <summary>zip 항목 경로가 대상 폴더 밖(<c>..\</c>, 절대 경로)을 가리키면 거부한다 (Zip Slip).</summary>
        public static void ExtractSafely(string zipPath, string destination)
        {
            var root = Path.GetFullPath(destination);
            if (!root.EndsWith(Path.DirectorySeparatorChar.ToString()))
                root += Path.DirectorySeparatorChar;

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    var path = Path.GetFullPath(Path.Combine(root, NormalizeEntryName(entry.FullName)));
                    if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("zip 항목이 압축 해제 폴더 밖을 가리킵니다: " + entry.FullName);

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(path);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    entry.ExtractToFile(path, true);
                }
            }
        }

        private static string NormalizeEntryName(string name)
        {
            return (name ?? string.Empty).Replace('\\', '/').TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        }

        private static string ComputeShortHash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return Convert.ToHexString(sha.ComputeHash(stream)).Substring(0, 12).ToLowerInvariant();
        }
    }
}

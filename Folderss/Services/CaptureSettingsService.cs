using System;
using System.IO;
using System.Xml;

namespace Folderss.Services
{
    /// <summary>화면 캡쳐 설정(설정 창의 "캡쳐" 탭).</summary>
    public sealed class CaptureSettings
    {
        /// <summary>결과 창 <c>저장</c>이 바로 저장하는 폴더. 기본은 사진 폴더(<c>C:\Users\이름\Pictures</c>, 옮겼으면 옮긴 곳).</summary>
        public string SaveFolder { get; set; } = CaptureSettingsService.DefaultSaveFolder;

        public CaptureSettings Clone() => (CaptureSettings)MemberwiseClone();
    }

    /// <summary>캡쳐 설정을 <c>%LOCALAPPDATA%\Folderss\capture-settings.xml</c>에 저장한다.</summary>
    public static class CaptureSettingsService
    {
        /// <summary>기본 저장 폴더: 사진 폴더(Windows 라이브러리 위치, OneDrive 등으로 옮겼으면 그곳). 얻지 못하면 사용자 폴더.</summary>
        public static string DefaultSaveFolder => ResolveDefault(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        /// <summary>사진 폴더 경로가 비어 있으면(폴더가 없거나 정책으로 막힘) 사용자 폴더.</summary>
        public static string ResolveDefault(string picturesFolder, string userProfileFolder)
        {
            return string.IsNullOrEmpty(picturesFolder) ? userProfileFolder : picturesFolder;
        }

        public static readonly string DefaultConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Folderss", "capture-settings.xml");

        public static CaptureSettings Load() => Load(DefaultConfigPath);

        /// <summary>파일이 없거나 읽지 못하거나 폴더 값이 비어 있으면 기본값.</summary>
        public static CaptureSettings Load(string path)
        {
            var settings = new CaptureSettings();
            if (!File.Exists(path))
                return settings;
            try
            {
                var doc = new XmlDocument();
                doc.Load(path);
                var folder = doc.DocumentElement?.GetAttribute("saveFolder");
                if (!string.IsNullOrWhiteSpace(folder))
                    settings.SaveFolder = folder;
            }
            catch (Exception ex)
            {
                // 읽기 실패는 기본값으로 동작한다(캡쳐를 못 하는 것보다 낫다). 원인은 디버그 출력에 남긴다.
                System.Diagnostics.Debug.WriteLine(Path.GetFileName(path) + " 읽기 실패: " + ex);
            }
            return settings;
        }

        /// <summary>실패는 예외로 알린다 — 설정 창이 "설정 저장 실패"로 모아 보여 준다.</summary>
        public static void Save(CaptureSettings settings) => Save(settings, DefaultConfigPath);

        public static void Save(CaptureSettings settings, string path)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            var doc = new XmlDocument();
            var root = doc.CreateElement("capture");
            root.SetAttribute("saveFolder", settings.SaveFolder ?? string.Empty);
            doc.AppendChild(root);
            SettingsFile.Write(path, doc.Save);
        }

        /// <summary>
        /// 설정 창의 저장 폴더 입력 검증. 앞뒤 공백·큰따옴표는 떼고, 비어 있으면 기본(사진 폴더)으로 본다.
        /// 절대 경로이면서 있는 폴더만 받는다(저장할 때 가서야 실패하지 않게). 오류 문구 또는 null.
        /// </summary>
        public static string Validate(string input, Func<string, bool> directoryExists, out string folder)
        {
            folder = (input ?? string.Empty).Trim().Trim('"').Trim();
            if (folder.Length == 0)
            {
                folder = DefaultSaveFolder;
                return null;
            }
            if (!Path.IsPathFullyQualified(folder))
                return "캡쳐 저장 폴더는 드라이브부터 쓴 전체 경로여야 합니다.";
            if (!directoryExists(folder))
                return "캡쳐 저장 폴더가 없습니다: " + folder;
            return null;
        }
    }
}

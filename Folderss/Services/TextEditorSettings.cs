using System;
using System.IO;
using System.Text.Json;

namespace Folderss.Services
{
    /// <summary>
    /// 일반 텍스트 편집기(<c>builtin:text</c>)의 보기 설정 — 줄 바꿈·글꼴 크기·공백 표시. 탭마다 바꾸면 다음에 여는 탭에도 이어진다.
    /// <c>%LOCALAPPDATA%\Folderss\text-editor.json</c>에 저장한다.
    /// </summary>
    public sealed class TextEditorSettings
    {
        public const double DefaultFontSize = 13;
        public const double MinFontSize = 6;
        public const double MaxFontSize = 72;

        private static TextEditorSettings _shared;

        public bool WordWrap { get; set; }
        public double FontSize { get; set; } = DefaultFontSize;
        public bool ShowWhitespace { get; set; }

        public static string DefaultPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Folderss", "text-editor.json");

        /// <summary>앱 전체가 함께 쓰는 설정. 처음 쓸 때 파일에서 읽는다(읽지 못하면 기본값).</summary>
        public static TextEditorSettings Shared => _shared ?? (_shared = Load(DefaultPath));

        public static TextEditorSettings Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var loaded = JsonSerializer.Deserialize<TextEditorSettings>(File.ReadAllText(path));
                    if (loaded != null)
                    {
                        loaded.FontSize = ClampFontSize(loaded.FontSize);
                        return loaded;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                // 보기 설정은 편의 기능이라 읽지 못하면 기본값으로 연다. 원인은 디버그 출력에 남긴다.
                System.Diagnostics.Trace.WriteLine("text-editor.json 읽기 실패: " + ex);
            }
            return new TextEditorSettings();
        }

        /// <summary>파일에 쓴다(임시 파일 후 교체). 실패하면 예외를 던진다.</summary>
        public void Save(string path)
        {
            SettingsFile.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }

        /// <summary>바뀐 보기 설정을 저장한다. 실패해도 지금 탭에는 적용된 채로 두고 원인만 디버그 출력에 남긴다(다음 실행에 기억만 빠짐).</summary>
        public void TrySave()
        {
            try
            {
                Save(DefaultPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                System.Diagnostics.Trace.WriteLine("text-editor.json 저장 실패: " + ex);
            }
        }

        public static double ClampFontSize(double size)
        {
            if (double.IsNaN(size) || size <= 0)
                return DefaultFontSize;
            return Math.Max(MinFontSize, Math.Min(MaxFontSize, size));
        }
    }
}

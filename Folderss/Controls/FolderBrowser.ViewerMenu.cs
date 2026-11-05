using Folderss.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace Folderss.Controls
{
    /// <summary>
    /// 파일 목록 우클릭 "뷰어 설정" 하위 메뉴. 선택한 파일의 확장자에 프로그램 내장 뷰어를 바로 연결한다
    /// (설정 창 &gt; 뷰어의 확장자 매핑과 같은 저장소, <see cref="ViewerConfigService.SetMapping"/>).
    /// </summary>
    public partial class FolderBrowser
    {
        /// <summary>뷰어 매핑 저장소. 호스트(MainWindow)가 넣어 주며, 없으면 "뷰어 설정" 메뉴를 띄우지 않는다.</summary>
        public ViewerConfigService ViewerConfig { get; set; }

        /// <summary>"뷰어 설정" 하위 메뉴의 "설정 창에서 열기…"를 눌렀다. 호스트가 설정 창 뷰어 탭을 연다.</summary>
        public event EventHandler ViewerSettingsRequested;

        /// <summary>
        /// 선택한 파일들(폴더 제외)의 확장자에 대한 "뷰어 설정" 하위 메뉴. 확장자가 없는 파일만 있으면 null.
        /// 확장자가 여러 개면 고른 뷰어를 모두에 적용하고, 모두 같은 뷰어일 때만 체크 표시한다.
        /// </summary>
        private ShellContextMenuService.CustomMenuItem? BuildViewerSettingsShellMenu(IEnumerable<string> paths)
        {
            var config = ViewerConfig;
            if (config == null)
                return null;

            var extensions = paths
                .Where(path => !Directory.Exists(path))
                .Select(Path.GetExtension)
                .Where(ext => !string.IsNullOrWhiteSpace(ext) && ext != ".")
                .Select(ext => ext.ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(ext => ext, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (extensions.Count == 0)
                return null;

            var current = extensions
                .Select(ext => config.GetMappingKey(ext) ?? ViewerConfigService.SystemDefaultKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var currentKey = current.Count == 1 ? current[0] : null;
            var singleDefault = extensions.Count == 1 ? ViewerConfigService.GetDefaultMappingKey(extensions[0]) : null;

            var children = new List<ShellContextMenuService.CustomMenuItem>();
            foreach (var key in ViewerConfigService.GetViewerKeys())
            {
                var capturedKey = key;
                var label = ViewerConfigService.GetDisplayName(key);
                if (string.Equals(key, singleDefault, StringComparison.OrdinalIgnoreCase))
                    label += " (기본값)";
                children.Add(new ShellContextMenuService.CustomMenuItem
                {
                    Label = label,
                    Checked = string.Equals(key, currentKey, StringComparison.OrdinalIgnoreCase),
                    Invoke = () => ApplyViewerMapping(extensions, ext => config.SetMapping(ext, capturedKey),
                        ViewerConfigService.GetDisplayName(capturedKey))
                });
            }

            var overridden = extensions.Where(config.IsOverridden).ToList();
            children.Add(new ShellContextMenuService.CustomMenuItem()); // 구분선
            if (overridden.Count > 0)
            {
                children.Add(new ShellContextMenuService.CustomMenuItem
                {
                    Label = "기본값으로 되돌리기",
                    Invoke = () => ApplyViewerMapping(overridden, config.ResetMapping, "기본값")
                });
            }
            children.Add(new ShellContextMenuService.CustomMenuItem
            {
                Label = "설정 창에서 열기…",
                Invoke = () => ViewerSettingsRequested?.Invoke(this, EventArgs.Empty)
            });

            return new ShellContextMenuService.CustomMenuItem
            {
                Label = "뷰어 설정 (" + FormatExtensions(extensions) + ")",
                Children = children
            };
        }

        private void ApplyViewerMapping(IReadOnlyList<string> extensions, Action<string> apply, string viewerName)
        {
            try
            {
                foreach (var ext in extensions)
                    apply(ext);
                ShowTransientStatus(FormatExtensions(extensions) + " → " + viewerName + " (새로 여는 파일부터 적용)");
            }
            catch (Exception exception)
            {
                // 메모리 매핑은 이미 바뀌어 이번 실행에는 적용된다. 저장 실패만 알린다.
                MessageBox.Show(Window.GetWindow(this),
                    "viewer-config.json: " + exception.Message + "\n\n이번 실행에는 적용되지만 다시 시작하면 사라집니다.",
                    "설정 저장 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static string FormatExtensions(IReadOnlyList<string> extensions)
        {
            const int shown = 3;
            var text = string.Join(", ", extensions.Take(shown));
            return extensions.Count > shown ? text + " 외 " + (extensions.Count - shown) + "개" : text;
        }
    }
}

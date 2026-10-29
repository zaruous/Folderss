using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Controls;

namespace Folderss.Services
{
    /// <summary>
    /// 경로 복사의 Windows 쪽: 저장소 찾기·UNC 변환(WNet API)·클립보드·메뉴 만들기.
    /// 형식 규칙은 <see cref="PathCopyFormatter"/>(순수 로직, 단위 테스트)에 있다.
    /// </summary>
    public static class PathCopyService
    {
        public const string MenuTitle = "경로 복사";

        /// <summary>
        /// 경로 복사 단축키의 표시 문구(예: "Ctrl+Shift+C"). 메인 창이 키 설정에서 읽는 함수를 넣는다 — 설정 창에서 키를 바꿔도 바로 맞는다.
        /// null이거나 빈 문자열이면 메뉴에 단축키를 표시하지 않는다.
        /// </summary>
        public static Func<string> ShortcutTextProvider { get; set; }

        public sealed class MenuEntry
        {
            public PathCopyFormat Format { get; set; }
            public string Label { get; set; }
            /// <summary>이 항목이 단축키의 형식이면 단축키 문구, 아니면 null.</summary>
            public string Shortcut { get; set; }
            /// <summary>조건부 형식(저장소·반대편·UNC) 묶음의 첫 항목이면 위에 구분선.</summary>
            public bool SeparatorBefore { get; set; }
        }

        public static PathCopyContext CreateContext(string oppositeFolder)
        {
            return new PathCopyContext
            {
                FindRepositoryRoot = folder => PathCopyFormatter.FindRepositoryRoot(folder, HasGitMarker),
                OppositeFolder = oppositeFolder,
                ResolveUnc = ToUniversalPath,
                Separator = PathCopySettingsService.Current.Separator
            };
        }

        /// <summary>
        /// 형식대로 만들어 텍스트만 클립보드에 넣는다(FileDrop 없음 — 탐색기에 붙여넣어도 파일이 복사되지 않는다).
        /// 복사할 것이 없으면 Count 0인 결과, 클립보드를 다른 프로그램이 잡고 있으면(<see cref="ClipboardService"/>가 알림) null.
        /// </summary>
        public static PathCopyResult Copy(IReadOnlyList<string> paths, PathCopyFormat format, PathCopyContext context)
        {
            var result = PathCopyFormatter.Format(paths, format, context);
            if (result.Count == 0)
                return result;
            return ClipboardService.TrySetDataObject(result.Text) ? result : null;
        }

        /// <summary>메뉴에 보일 항목. 조건부 형식은 쓸 수 있을 때만, 이름에 기준 폴더 이름을 넣는다.</summary>
        public static List<MenuEntry> GetMenuEntries(IReadOnlyList<string> paths, PathCopyContext context)
        {
            var entries = new List<MenuEntry>();
            if (paths == null || paths.Count == 0)
                return entries;

            var shortcut = SafeShortcutText();
            var shortcutFormat = PathCopySettingsService.Current.ShortcutFormat;
            var conditionalStarted = false;
            foreach (var format in PathCopyFormatter.MenuOrder)
            {
                if (!PathCopyFormatter.IsAvailable(format, paths, context))
                    continue;

                var label = PathCopyFormatter.DisplayName(format);
                var conditional = false;
                switch (format)
                {
                    case PathCopyFormat.RelativeToRepository:
                        label = string.Format("상대 경로 (저장소 '{0}' 기준)", PathCopyFormatter.RepositoryName(paths, context));
                        conditional = true;
                        break;
                    case PathCopyFormat.RelativeToOppositePanel:
                        var folder = PathCopyFormatter.TrimTrailingSeparators(context.OppositeFolder);
                        label = string.Format("상대 경로 (반대편 패널 '{0}' 기준)", PathCopyFormatter.GetName(folder) ?? folder);
                        conditional = true;
                        break;
                    case PathCopyFormat.UncPath:
                        conditional = true;
                        break;
                }

                entries.Add(new MenuEntry
                {
                    Format = format,
                    Label = label,
                    Shortcut = format == shortcutFormat && !string.IsNullOrEmpty(shortcut) ? shortcut : null,
                    SeparatorBefore = conditional && !conditionalStarted
                });
                if (conditional)
                    conditionalStarted = true;
            }

            // 단축키 형식이 지금 쓸 수 없는 조건부 형식이면 단축키는 전체 경로로 대신하므로 그 항목에 표시한다.
            if (!string.IsNullOrEmpty(shortcut) && entries.All(e => e.Shortcut == null))
            {
                var full = entries.FirstOrDefault(e => e.Format == PathCopyFormat.FullPath);
                if (full != null)
                    full.Shortcut = shortcut;
            }
            return entries;
        }

        /// <summary>Windows 셸 우클릭 메뉴 맨 위에 넣을 "경로 복사 ▸" 하위 메뉴. 항목이 없으면 null.</summary>
        public static ShellContextMenuService.CustomMenuItem? BuildShellMenu(IReadOnlyList<string> paths, PathCopyContext context,
            Action<PathCopyFormat> copy)
        {
            var entries = GetMenuEntries(paths, context);
            if (entries.Count == 0)
                return null;

            var children = new List<ShellContextMenuService.CustomMenuItem>();
            foreach (var entry in entries)
            {
                if (entry.SeparatorBefore)
                    children.Add(new ShellContextMenuService.CustomMenuItem());
                var format = entry.Format;
                // Win32 메뉴는 &를 바로 가기 문자로 읽으므로 폴더 이름의 &를 &&로, 단축키 문구는 탭 뒤에 둔다(오른쪽 정렬).
                var label = entry.Label.Replace("&", "&&") + (entry.Shortcut == null ? string.Empty : "\t" + entry.Shortcut);
                children.Add(new ShellContextMenuService.CustomMenuItem { Label = label, Invoke = () => copy(format) });
            }
            return new ShellContextMenuService.CustomMenuItem { Label = MenuTitle, Children = children };
        }

        /// <summary>WPF 메뉴(⋯ 메인 메뉴)의 "경로 복사" 항목 아래를 다시 채운다. 항목이 없으면 비활성.</summary>
        public static void FillMenu(MenuItem parent, IReadOnlyList<string> paths, PathCopyContext context, Action<PathCopyFormat> copy)
        {
            parent.Items.Clear();
            var entries = GetMenuEntries(paths, context);
            foreach (var entry in entries)
            {
                if (entry.SeparatorBefore)
                    parent.Items.Add(new Separator());
                var format = entry.Format;
                // WPF 머리글의 _는 바로 가기 표시라 폴더 이름의 _를 __로 바꾼다.
                var item = new MenuItem { Header = entry.Label.Replace("_", "__"), InputGestureText = entry.Shortcut ?? string.Empty };
                item.Click += (s, e) => copy(format);
                parent.Items.Add(item);
            }
            if (entries.Count == 0)
                parent.Items.Add(new MenuItem { Header = "(복사할 항목 없음)", IsEnabled = false });
        }

        private static string SafeShortcutText()
        {
            try
            {
                return ShortcutTextProvider?.Invoke();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool HasGitMarker(string folder)
        {
            var git = Path.Combine(folder, ".git");
            return Directory.Exists(git) || File.Exists(git);
        }

        // ── UNC 변환 (매핑된 네트워크 드라이브) ─────────────────────────────

        private const int UniversalNameInfoLevel = 1;
        private const int NoError = 0;
        private const int ErrorMoreData = 234;

        /// <summary>
        /// 매핑된 네트워크 드라이브의 경로를 UNC로 바꾼다(<c>Z:\a.txt</c> → <c>\\server\share\a.txt</c>). 네트워크 드라이브가 아니거나 실패하면 null.
        /// <c>WNetGetUniversalName</c>(DFS 등도 처리) → 안 되면 <c>WNetGetConnection</c>으로 드라이브의 원격 이름 + 나머지 경로.
        /// </summary>
        public static string ToUniversalPath(string path)
        {
            if (!PathCopyFormatter.TryGetDriveName(path, out var drive))
                return null;
            try
            {
                if (new DriveInfo(drive).DriveType != DriveType.Network)
                    return null;
            }
            catch (ArgumentException)
            {
                return null;
            }

            var universal = GetUniversalName(path);
            if (!string.IsNullOrEmpty(universal))
                return universal;

            var remote = new StringBuilder(1024);
            var length = remote.Capacity;
            return WNetGetConnection(drive, remote, ref length) == NoError
                ? PathCopyFormatter.CombineUnc(remote.ToString(), path)
                : null;
        }

        private static string GetUniversalName(string path)
        {
            var size = 1024;
            var buffer = Marshal.AllocCoTaskMem(size);
            try
            {
                var result = WNetGetUniversalName(path, UniversalNameInfoLevel, buffer, ref size);
                if (result == ErrorMoreData)
                {
                    Marshal.FreeCoTaskMem(buffer);
                    buffer = IntPtr.Zero;
                    buffer = Marshal.AllocCoTaskMem(size);
                    result = WNetGetUniversalName(path, UniversalNameInfoLevel, buffer, ref size);
                }
                if (result != NoError)
                    return null;
                // UNIVERSAL_NAME_INFO { LPWSTR lpUniversalName } — 문자열은 같은 버퍼 안에 있다.
                var namePointer = Marshal.ReadIntPtr(buffer);
                return namePointer == IntPtr.Zero ? null : Marshal.PtrToStringUni(namePointer);
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(buffer);
            }
        }

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetGetUniversalName(string localPath, int infoLevel, IntPtr buffer, ref int bufferSize);

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetGetConnection(string localName, StringBuilder remoteName, ref int length);
    }
}

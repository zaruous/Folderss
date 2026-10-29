using Folderss.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Folderss.Controls
{
    /// <summary>
    /// 경로 복사(우클릭·⋯ 메뉴의 "경로 복사 ▸", 경로 복사 단축키). 형식은 <see cref="PathCopyFormatter"/>,
    /// 클립보드·메뉴·UNC 변환은 <see cref="PathCopyService"/>가 맡고 여기서는 대상 고르기와 상태 표시만 한다.
    /// </summary>
    public partial class FolderBrowser
    {
        private static readonly TimeSpan PathCopyStatusDuration = TimeSpan.FromSeconds(4);
        private DispatcherTimer _pathCopyStatusTimer;
        private string _pathCopyStatus;

        /// <summary>
        /// 반대편 폴더 패널의 현재 폴더("상대 경로 (반대편 패널 기준)"의 기준). 메인 창이 넣는다.
        /// null이거나 null을 돌려주면 그 형식을 메뉴에 보이지 않는다(폴더 비교 창·플러그인 팝업 패널).
        /// </summary>
        public Func<string> OppositeFolderProvider { get; set; }

        /// <summary>파일 목록 기준 경로 복사 대상: 선택 항목(목록에 보이는 순서), 고른 것이 없으면 현재 폴더.</summary>
        public IReadOnlyList<string> GetPathCopyTargets()
        {
            var selected = FileList.SelectedItems.OfType<Models.FileSystemItem>().ToList();
            if (selected.Count == 0)
                return string.IsNullOrWhiteSpace(CurrentPath) ? new List<string>() : new List<string> { CurrentPath };

            // 목록을 한 번만 훑어 보이는 순서로 정렬한다(우클릭마다 불리므로 선택이 많아도 IndexOf 반복을 피한다).
            var remaining = new HashSet<Models.FileSystemItem>(selected);
            var ordered = new List<string>(selected.Count);
            foreach (var item in FileList.Items.OfType<Models.FileSystemItem>())
            {
                if (remaining.Remove(item))
                    ordered.Add(item.FullPath);
            }
            // 목록에 안 보이는 선택(필터로 가려짐 등)은 선택 순서대로 뒤에 붙인다.
            ordered.AddRange(selected.Where(remaining.Contains).Select(item => item.FullPath));
            return ordered;
        }

        /// <summary>
        /// 경로 복사 단축키(기본 Ctrl+Shift+C). 이 패널에 키보드 포커스가 없으면 false(다른 패널이 처리).
        /// 트리에 포커스가 있으면 트리에서 고른 폴더, 아니면 파일 목록의 선택(없으면 현재 폴더)을 설정의 단축키 형식으로 복사한다.
        /// </summary>
        public bool CopyPathForShortcut()
        {
            if (!IsKeyboardFocusWithin)
                return false;

            IReadOnlyList<string> targets;
            if (FolderTree.IsKeyboardFocusWithin)
            {
                var item = FolderTree.SelectedItem as TreeViewItem;
                var path = item == null ? null : item.Tag as string;
                if (string.IsNullOrWhiteSpace(path))
                    return false;
                targets = new[] { path };
            }
            else
            {
                targets = GetPathCopyTargets();
                if (targets.Count == 0)
                    return false;
            }

            CopyPaths(targets, PathCopySettingsService.Current.ShortcutFormat, CreatePathCopyContext());
            return true;
        }

        /// <summary>⋯ 메인 메뉴의 "경로 복사" 아래를 이 패널의 대상으로 다시 채운다.</summary>
        public void FillPathCopyMenu(MenuItem parent)
        {
            var targets = GetPathCopyTargets();
            var context = CreatePathCopyContext();
            PathCopyService.FillMenu(parent, targets, context, format => CopyPaths(targets, format, context));
        }

        /// <summary>셸 우클릭 메뉴 맨 위에 넣을 "경로 복사 ▸". 대상이 없으면 null.</summary>
        private ShellContextMenuService.CustomMenuItem? BuildPathCopyShellMenu(IReadOnlyList<string> paths)
        {
            var context = CreatePathCopyContext();
            return PathCopyService.BuildShellMenu(paths, context, format => CopyPaths(paths, format, context));
        }

        private PathCopyContext CreatePathCopyContext()
        {
            return PathCopyService.CreateContext(OppositeFolderProvider?.Invoke());
        }

        private void CopyPaths(IReadOnlyList<string> paths, PathCopyFormat format, PathCopyContext context)
        {
            var result = PathCopyService.Copy(paths, format, context);
            if (result == null)
                return; // 클립보드를 다른 프로그램이 잡고 있음 — ClipboardService가 이미 알렸다.

            if (result.Count > 0)
            {
                // 다른 복사와 같이 잘라내기 표시를 지운다(클립보드가 더는 잘라낸 파일 목록이 아님).
                var window = Window.GetWindow(this) as Folderss.MainWindow;
                if (window != null)
                    window.ClearCutStateFromClipboard();
            }
            ShowTransientStatus(PathCopyFormatter.StatusMessage(result));
        }

        /// <summary>
        /// 상태 줄에 잠깐(4초) 문구를 보인 뒤 원래 항목 수 표시로 돌린다.
        /// 셸 우클릭 메뉴는 닫힌 뒤 목록을 새로 고치며 상태 줄을 다시 쓰므로, 그 뒤에 보이도록 디스패처 뒤로 미룬다.
        /// </summary>
        public void ShowTransientStatus(string message)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _pathCopyStatus = message;
                StatusText.Text = message;
                if (_pathCopyStatusTimer == null)
                {
                    _pathCopyStatusTimer = new DispatcherTimer { Interval = PathCopyStatusDuration };
                    _pathCopyStatusTimer.Tick += (s, e) =>
                    {
                        _pathCopyStatusTimer.Stop();
                        // 그 사이 다른 표시(끌어 놓기 안내, 새로 고침)로 바뀌었으면 건드리지 않는다.
                        if (StatusText.Text == _pathCopyStatus)
                            UpdateStatusText();
                        _pathCopyStatus = null;
                    };
                }
                _pathCopyStatusTimer.Stop();
                _pathCopyStatusTimer.Start();
            }), DispatcherPriority.Background);
        }
    }
}

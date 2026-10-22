using Folderss.Controls;
using Folderss.Services;
using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace Folderss
{
    /// <summary>
    /// zip을 폴더처럼 열기(읽기 전용)의 메인 창 연결. 파일 작업 핸들러에는 이 파일의 가드를 한 줄씩만 부른다.
    /// docs/items/zip-as-folder.md 참고.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>"zip 안에서는 …" 안내. <paramref name="cannot"/>은 "삭제할 수 없습니다"처럼 서술어까지.</summary>
        public static void ShowArchiveReadOnlyMessage(string cannot)
        {
            MessageBox.Show(
                string.Format("zip 안에서는 {0}.\n\nzip 파일은 읽기 전용으로 엽니다(zip 안 수정은 아직 지원하지 않습니다).\n" +
                              "꺼내려면 끌어 놓기 · Ctrl+C → Ctrl+V · 우클릭 > 반대편 패널로 복사 · 압축 풀기를 쓰세요.", cannot),
                "zip 읽기 전용",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        /// <summary>활성 패널이 zip 안이면 알리고 true. <paramref name="checkTarget"/>이면 반대편(대상) 패널이 zip 안일 때도 막는다.</summary>
        private bool BlockedInArchive(string cannot, bool checkTarget = false)
        {
            if (ActivePane != null && ActivePane.IsInArchive)
            {
                ShowArchiveReadOnlyMessage(cannot);
                return true;
            }
            if (checkTarget && TargetPane != null && !ReferenceEquals(TargetPane, ActivePane) && TargetPane.IsInArchive)
            {
                ShowTargetArchiveMessage();
                return true;
            }
            return false;
        }

        private static void ShowTargetArchiveMessage()
        {
            MessageBox.Show(
                "반대편 패널이 zip 안을 보고 있습니다. zip 안으로 넣는 기능은 아직 없습니다.\n반대편 패널을 실제 폴더로 옮긴 뒤 다시 하세요.",
                "zip 읽기 전용", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// 이동·복사(<c>ExecuteTransfer</c>)에서 zip이 끼면 처리하고 true. zip에서 옮기기는 zip을 고쳐야 해서 막고,
        /// 복사는 반대편 패널로 압축을 푼다(기존 복사 규칙 — 같은 이름이면 '이름 (2)').
        /// </summary>
        private bool TryHandleArchiveTransfer(bool move)
        {
            var target = TargetPane;
            var hasTarget = target != null && !ReferenceEquals(target, ActivePane);
            var activeInZip = ActivePane.IsInArchive;
            var targetInZip = hasTarget && target.IsInArchive;
            if (!activeInZip && !targetInZip)
                return false;

            if (move && activeInZip)
                ShowArchiveReadOnlyMessage("항목을 옮길 수 없습니다(옮기면 zip에서 지워야 합니다)");
            else if (targetInZip)
                ShowTargetArchiveMessage();
            else if (!hasTarget)
                MessageBox.Show("zip에서 꺼낼 반대편 폴더 패널이 없습니다.", "zip에서 복사", MessageBoxButton.OK, MessageBoxImage.Information);
            else
                ActivePane.ExtractSelectionToFolder(target.CurrentPath);
            return true;
        }

        /// <summary>붙여넣기 대상 패널이 zip 안이면 알리고 true.</summary>
        private static bool BlockedPasteIntoArchive(FolderBrowser targetPane)
        {
            if (targetPane == null || !targetPane.IsInArchive)
                return false;
            ShowArchiveReadOnlyMessage("붙여넣을 수 없습니다");
            return true;
        }

        /// <summary><paramref name="pane"/>이 아닌 첫 폴더 패널(⋯ 메뉴의 대상 패널과 같은 규칙). 패널이 하나면 null.</summary>
        public FolderBrowser GetOppositePane(FolderBrowser pane)
        {
            return GetFolderBrowsers().FirstOrDefault(browser => !ReferenceEquals(browser, pane));
        }

        /// <summary>zip 작업(압축 풀기·압축)이 끝나면 모든 패널을 새로 고치고, 결과 폴더를 보는 패널에서 만든 항목을 고른다.</summary>
        public void ShowArchiveOperationResult(string directory, string createdPath)
        {
            RefreshBothPanes();
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(createdPath))
                return;
            var normalized = NormalizeDirectoryPath(directory);
            foreach (var pane in GetFolderBrowsers().Where(pane => !pane.IsInArchive))
            {
                if (string.Equals(NormalizeDirectoryPath(pane.CurrentPath), normalized, StringComparison.OrdinalIgnoreCase))
                    pane.SelectCreatedItem(Path.GetFileName(createdPath));
            }
        }

        /// <summary>세션·레이아웃 복원: 실제 폴더이거나 zip 안 경로(zip 파일이 있음)면 true.</summary>
        private static bool FolderOrArchiveExists(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && (Directory.Exists(path) || ZipArchivePath.Exists(path));
        }

        /// <summary>⋯ 메뉴 `압축 풀기…`.</summary>
        private void ExtractArchive_Click(object sender, RoutedEventArgs e)
        {
            ActivePane?.ShowExtractDialog();
        }

        /// <summary>⋯ 메뉴 `zip으로 압축…`.</summary>
        private void CompressToZip_Click(object sender, RoutedEventArgs e)
        {
            if (ActivePane != null && !ActivePane.IsInArchive && IsDestinationPinLocked(ActivePane.CurrentPath))
            {
                ShowPinLockedMessage();
                return;
            }
            ActivePane?.ShowCompressDialog();
        }
    }
}

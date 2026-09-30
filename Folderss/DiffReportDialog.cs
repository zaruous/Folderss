using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Folderss.Services;
using Microsoft.Win32;

namespace Folderss
{
    /// <summary>HTML 보고서 형식 선택: 배치(양옆 / 한 줄) × 범위(변경점만 / 앞뒤 10줄 / 전체 파일).</summary>
    public sealed class DiffReportDialog : GitDialogBase
    {
        private readonly RadioButton _sideBySide;
        private readonly RadioButton _changesOnly;
        private readonly RadioButton _context10;
        private readonly RadioButton _fullFile;
        private readonly CheckBox _openAfterSave;

        public DiffReportLayout Layout => _sideBySide.IsChecked == true ? DiffReportLayout.SideBySide : DiffReportLayout.Inline;

        public GitDiffViewMode Scope => _fullFile.IsChecked == true ? GitDiffViewMode.FullFile
            : _context10.IsChecked == true ? GitDiffViewMode.Context10 : GitDiffViewMode.ChangesOnly;

        public bool OpenAfterSave => _openAfterSave.IsChecked == true;

        /// <param name="initialScope">처음 고른 범위 — 지금 보고 있는 diff 창의 보기 모드.</param>
        public DiffReportDialog(GitDiffViewMode initialScope) : base("HTML 보고서", 460)
        {
            AddText("보고 있는 비교를 HTML 파일 한 장으로 저장합니다(스타일 포함, 브라우저에서 인쇄·PDF 저장 가능).", secondary: true);

            AddLabel("배치");
            _sideBySide = AddRadio("layout", "양옆 비교", "왼쪽 = 변경 전, 오른쪽 = 변경 후. 바뀐 줄을 같은 행에 나란히 놓습니다.", true);
            AddRadio("layout", "한 줄 보기", "변경 전/후 줄을 한 열에 −/+로 차례로 보입니다(unified).", false);

            AddLabel("범위");
            _changesOnly = AddRadio("scope", "변경점만", "바뀐 곳과 앞뒤 몇 줄(git 기본 문맥)만.", initialScope == GitDiffViewMode.ChangesOnly);
            _context10 = AddRadio("scope", "변경점 + 앞뒤 10줄", null, initialScope == GitDiffViewMode.Context10);
            _fullFile = AddRadio("scope", "전체 파일", "파일 전체를 넣고 바뀐 줄만 색으로 표시합니다. 큰 파일은 보고서도 커집니다.",
                initialScope == GitDiffViewMode.FullFile);

            _openAfterSave = AddCheck("저장한 뒤 기본 브라우저로 열기", true);
            AddButtons("저장…");
        }
    }

    /// <summary>
    /// diff 창(<see cref="Controls.GitDiffView"/>)의 "HTML 보고서…" 흐름. 형식 선택 → 그 범위로 diff를 다시 읽기(호출 측 로더)
    /// → 저장 위치 선택 → UTF-8 HTML 저장 → (선택) 브라우저로 열기. Git 창·두 파일 비교 창이 같은 흐름을 쓴다.
    /// </summary>
    public static class DiffReportExporter
    {
        public static async Task ExportAsync(Window owner, GitDiffRequest request, GitDiffViewMode currentMode,
            Func<GitDiffRequest, GitDiffViewMode, CancellationToken, Task<string>> loadText)
        {
            if (request == null || loadText == null)
                return;

            var dialog = new DiffReportDialog(currentMode) { Owner = owner };
            if (dialog.ShowDialog() != true)
                return;

            string diffText;
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                diffText = await loadText(request, dialog.Scope, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                MessageBox.Show(owner, "비교 내용을 읽지 못했습니다.\n" + ex.Message, "HTML 보고서", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }

            var generatedAt = DateTime.Now;
            var save = new SaveFileDialog
            {
                Title = "HTML 보고서 저장",
                Filter = "HTML 파일 (*.html)|*.html",
                DefaultExt = ".html",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = "diff-report-" + generatedAt.ToString("yyyyMMdd-HHmmss") + ".html"
            };
            if (save.ShowDialog(owner) != true)
                return;

            var html = DiffHtmlReport.Build(diffText, new DiffReportInfo
            {
                Title = request.Title,
                LeftLabel = request.OldLabel,
                RightLabel = request.NewLabel,
                Layout = dialog.Layout,
                Scope = dialog.Scope,
                GeneratedAt = generatedAt
            });

            try
            {
                File.WriteAllText(save.FileName, html, new UTF8Encoding(false));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                MessageBox.Show(owner, "보고서를 저장하지 못했습니다.\n" + save.FileName + "\n\n" + ex.Message, "HTML 보고서",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!dialog.OpenAfterSave)
                return;
            try
            {
                Process.Start(new ProcessStartInfo(save.FileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "저장은 됐지만 브라우저로 열지 못했습니다.\n" + save.FileName + "\n\n" + ex.Message, "HTML 보고서",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}

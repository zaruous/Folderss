using Folderss.Models;
using Folderss.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Folderss.Controls
{
    /// <summary>
    /// zip 파일을 폴더처럼 열기(읽기 전용). 패널의 <see cref="CurrentPath"/>는 <c>C:\a\b.zip\sub</c> 같은 가상 경로가 되고,
    /// 목록은 <see cref="ZipListingCache"/>에서 백그라운드로 읽는다. 기존 파일에는 한 줄짜리 연결만 두고 동작은 여기에 모은다.
    /// docs/items/zip-as-folder.md 참고.
    /// </summary>
    public partial class FolderBrowser
    {
        private const long ClipboardConfirmBytes = 512L * 1024 * 1024;
        private const long ImagePreviewLimit = 32L * 1024 * 1024;

        private string _archivePath;
        private string _archiveInnerPath = string.Empty;
        private ZipListing _archiveListing;
        private int _archiveLoadId;
        private bool _archiveBrowsingEnabled = true;
        private string _selectAfterArchiveLoad;
        private string _archiveNotice;
        private static bool _readOnlyNoticeShown;
        private string _pendingSelectName;
        private DateTime _pendingSelectUntil;

        /// <summary>zip에 들어갈 수 있는가. 폴더 비교 창(안전 모드)과 플러그인 팝업 패널은 끈다(기존 "폴더" 계약 유지).</summary>
        public bool ArchiveBrowsingEnabled
        {
            get { return _archiveBrowsingEnabled && !_safeMode; }
            set { _archiveBrowsingEnabled = value; }
        }

        /// <summary>지금 zip 안을 보고 있는가(읽기 전용).</summary>
        public bool IsInArchive
        {
            get { return _archivePath != null; }
        }

        /// <summary>보고 있는 zip 파일 경로. zip 밖이면 null.</summary>
        public string ArchivePath
        {
            get { return _archivePath; }
        }

        /// <summary>실제 폴더가 필요한 기능(콘솔·검색·트리)이 쓸 경로: zip 안이면 zip이 있는 폴더, 아니면 <see cref="CurrentPath"/>.</summary>
        public string FileSystemPath
        {
            get { return IsInArchive ? Path.GetDirectoryName(_archivePath) : CurrentPath; }
        }

        private MainWindow HostWindow
        {
            get { return Window.GetWindow(this) as MainWindow; }
        }

        private Window OwnerWindow
        {
            get { return Window.GetWindow(this); }
        }

        // ── 들어가기·나오기 ─────────────────────────────────────────────────

        /// <summary><see cref="NavigateTo"/>에서 실제 폴더가 아닐 때: zip 가상 경로면 들어가고 true.</summary>
        private bool TryNavigateIntoArchive(string fullPath, bool addHistory)
        {
            string archive;
            string inner;
            if (!ArchiveBrowsingEnabled || !ZipArchivePath.TrySplit(fullPath, out archive, out inner))
                return false;

            var target = ZipArchivePath.Combine(archive, inner);
            var previousPath = CurrentPath;
            var pushedHistory = false;
            if (addHistory && !string.IsNullOrWhiteSpace(previousPath) &&
                !string.Equals(previousPath, target, StringComparison.OrdinalIgnoreCase))
            {
                PushHistory(_backHistory, previousPath);
                _forwardHistory.Clear();
                pushedHistory = true;
            }

            // 검색 결과 필터는 실제 폴더에만 의미가 있다.
            if (_resultFilterFiles != null)
                ClearSearchResultFilter();

            var sameArchive = string.Equals(_archivePath, archive, StringComparison.OrdinalIgnoreCase);
            _archivePath = archive;
            _archiveInnerPath = inner;
            CurrentPath = target;
            SearchBox.Text = string.Empty;
            SyncTreeForArchive();
            UpdatePathBoxText();
            SyncDriveComboBox();
            if (!sameArchive || _watcher == null)
                StartArchiveWatcher(archive);
            UpdateArchiveBanner();
            LoadArchiveListing(previousPath, pushedHistory, true);

            var handler = PathChanged;
            if (handler != null)
                handler(this, EventArgs.Empty);
            return true;
        }

        /// <summary>실제 폴더로 이동할 때 zip 상태를 정리한다(<see cref="NavigateTo"/>에서 호출).</summary>
        private void ExitArchiveState()
        {
            if (_archivePath == null)
                return;
            _archivePath = null;
            _archiveInnerPath = string.Empty;
            _archiveListing = null;
            _selectAfterArchiveLoad = null;
            _archiveLoadId++;
            StatusText.ToolTip = null;
            UpdateArchiveBanner();
        }

        /// <summary>zip 안의 상위로: zip 맨 위면 zip이 있는 폴더로 나가 zip 파일을 고른다.</summary>
        private void NavigateUpFromArchive()
        {
            var archive = _archivePath;
            if (_archiveInnerPath.Length == 0)
            {
                ExitArchiveToFolder();
                return;
            }
            _selectAfterArchiveLoad = ZipArchivePath.GetInnerName(_archiveInnerPath);
            NavigateTo(ZipArchivePath.Combine(archive, ZipArchivePath.GetParentInnerPath(_archiveInnerPath)));
        }

        private void ExitArchiveToFolder()
        {
            var archive = _archivePath;
            if (archive == null)
                return;
            var folder = Path.GetDirectoryName(archive);
            if (string.IsNullOrEmpty(folder))
                return;
            NavigateTo(folder);
            if (!IsInArchive)
                SelectAndFocusByName(Path.GetFileName(archive));
        }

        private void SyncTreeForArchive()
        {
            if (!_treeViewVisible)
                return;
            // 트리에는 zip을 보이지 않는다. 트리는 zip이 있는 폴더에 머문다.
            var folder = Path.GetDirectoryName(_archivePath);
            var sameRoot = !string.IsNullOrWhiteSpace(_treeRootPath) &&
                           string.Equals(_treeRootPath.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            if (!IsFolderPinned && !_treeNavigationInProgress && !sameRoot)
            {
                _treeRootPath = folder;
                RebuildFolderTree();
            }
            SelectTreePath(folder);
        }

        private void StartArchiveWatcher(string archive)
        {
            StopWatcher();
            try
            {
                // zip 파일 자체가 바뀌거나 지워지면 다시 읽는다(크기·시각이 같으면 캐시를 그대로 쓴다).
                _watcher = new FileSystemWatcher(Path.GetDirectoryName(archive), Path.GetFileName(archive))
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };
                _watcher.Changed += OnFileSystemChanged;
                _watcher.Created += OnFileSystemChanged;
                _watcher.Deleted += OnFileSystemChanged;
                _watcher.Renamed += OnFileSystemChanged;
                _watcher.Error += OnWatcherError;
            }
            catch
            {
                _watcher = null;
            }
        }

        // ── 목록 ─────────────────────────────────────────────────────────────

        /// <summary>zip 목록을 보인다. 캐시에 있으면 바로, 없으면 백그라운드에서 읽는다(<see cref="RefreshItems"/>도 이 경로).</summary>
        private async void LoadArchiveListing(string fallbackPath, bool pushedHistory, bool userInitiated)
        {
            var id = ++_archiveLoadId;
            var archive = _archivePath;
            var cached = ZipListingCache.TryGetCurrent(archive);
            if (cached != null)
            {
                ShowArchiveListing(cached);
                return;
            }

            if (_archiveListing == null || !string.Equals(_archiveListing.ArchivePath, archive, StringComparison.OrdinalIgnoreCase))
            {
                _items.Clear();
                ApplyFilter();
            }
            StatusText.Text = "zip 목록을 읽는 중…";

            ZipListing listing;
            try
            {
                listing = await ZipListingCache.GetAsync(archive);
            }
            catch (Exception ex)
            {
                if (id == _archiveLoadId)
                    HandleArchiveLoadFailure(archive, ex, fallbackPath, pushedHistory, userInitiated);
                return;
            }

            if (id != _archiveLoadId || !string.Equals(_archivePath, archive, StringComparison.OrdinalIgnoreCase))
                return;
            ShowArchiveListing(listing);
        }

        private void HandleArchiveLoadFailure(string archive, Exception ex, string fallbackPath, bool pushedHistory, bool userInitiated)
        {
            var message = ZipArchiveService.Describe(ex is AggregateException && ex.InnerException != null ? ex.InnerException : ex);
            if (pushedHistory && _backHistory.Count > 0)
                _backHistory.Pop();

            // 사용자가 연 경우엔 열기 전 폴더로, 새로 고침·감시로 실패하면(zip이 지워지거나 망가짐) zip이 있던 폴더로 돌아간다.
            var folder = Path.GetDirectoryName(archive);
            var back = userInitiated && !string.IsNullOrWhiteSpace(fallbackPath) && Directory.Exists(fallbackPath) ? fallbackPath : folder;
            if (!string.IsNullOrEmpty(back) && Directory.Exists(back))
            {
                NavigateTo(back, false);
                if (string.Equals(back.TrimEnd('\\'), (folder ?? string.Empty).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    SelectAndFocusByName(Path.GetFileName(archive));
            }
            else
            {
                ExitArchiveState();
                _items.Clear();
                ApplyFilter();
            }

            StatusText.Text = "zip을 열 수 없습니다: " + message.Split('\n')[0];
            if (userInitiated)
                MessageBox.Show(OwnerWindow, message, "zip을 열 수 없습니다", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void ShowArchiveListing(ZipListing listing)
        {
            _archiveListing = listing;
            if (!listing.FolderExists(_archiveInnerPath))
            {
                _archiveNotice = string.Format("zip 안에 '{0}' 폴더가 없어 맨 위를 엽니다", _archiveInnerPath);
                _archiveInnerPath = string.Empty;
                CurrentPath = ZipArchivePath.Combine(_archivePath, string.Empty);
                UpdatePathBoxText();
                var handler = PathChanged;
                if (handler != null)
                    handler(this, EventArgs.Empty);
            }

            _items.Clear();
            foreach (var entry in listing.GetChildren(_archiveInnerPath))
            {
                _items.Add(new FileSystemItem
                {
                    Name = entry.Name,
                    FullPath = ZipArchivePath.Combine(_archivePath, entry.InnerPath),
                    IsDirectory = entry.IsDirectory,
                    Size = entry.IsDirectory ? 0 : entry.Size,
                    ModifiedAt = entry.ModifiedAt,
                    ArchiveEntry = entry
                });
            }

            _ignoredCount = 0;
            UpdateIgnoreRulesToolTip(null);
            ApplyCompareOverlay();
            ApplyFilter();
            UpdateArchiveStatusText();
            UpdateArchiveBanner();

            if (_selectAfterArchiveLoad != null)
            {
                var name = _selectAfterArchiveLoad;
                _selectAfterArchiveLoad = null;
                SelectAndFocusByName(name);
            }
        }

        private void UpdateArchiveStatusText()
        {
            var listing = _archiveListing;
            var parts = new List<string> { string.Format("{0:N0}개 항목", _items.Count), "zip 안 · 읽기 전용" };
            if (listing != null && listing.UnsafeEntryCount > 0)
                parts.Add(string.Format("⚠ 위험한 경로 {0:N0}개 숨김", listing.UnsafeEntryCount));
            if (listing != null && listing.EncryptedFileCount > 0)
                parts.Add(string.Format("🔒 암호 항목 {0:N0}개(열 수 없음)", listing.EncryptedFileCount));
            var text = string.Join(" · ", parts);
            if (_archiveNotice != null)
            {
                text = _archiveNotice + " — " + text;
                _archiveNotice = null;
            }
            StatusText.Text = text;
            StatusText.ToolTip = listing != null && listing.UnsafeEntryCount > 0
                ? "zip 밖을 가리키거나(..) 드라이브·대체 스트림(:)이 든 이름이라 숨기고 풀지 않는 항목:\n" + string.Join("\n", listing.UnsafeEntryNames)
                : null;
        }

        private void UpdateArchiveBanner()
        {
            if (ArchiveBanner == null)
                return;
            ArchiveBanner.Visibility = IsInArchive ? Visibility.Visible : Visibility.Collapsed;
            if (!IsInArchive)
                return;
            ArchiveBannerText.Text = "📦 zip 안 · 읽기 전용 — 꺼내기: 끌어 놓기 · Ctrl+C · 우클릭";
            ArchiveExtractButton.ToolTip = string.Format("zip 전체를 zip이 있는 폴더의 새 폴더 '{0}'에 풉니다",
                Path.GetFileNameWithoutExtension(_archivePath));
        }

        /// <summary>
        /// 작업 결과로 만든 항목을 고른다. 곧이어 폴더 감시가 목록을 다시 만들어도(0.4초 뒤) 선택이 풀리지 않게
        /// 잠깐(3초) 기억했다가 <see cref="RefreshItems"/> 뒤에 다시 고른다. 그 사이 사용자가 다른 것을 고르면 건드리지 않는다.
        /// </summary>
        public void SelectCreatedItem(string name)
        {
            _pendingSelectName = name;
            _pendingSelectUntil = DateTime.UtcNow.AddSeconds(3);
            SelectByName(name);
        }

        private void ApplyPendingSelection()
        {
            if (_pendingSelectName == null)
                return;
            if (DateTime.UtcNow > _pendingSelectUntil)
            {
                _pendingSelectName = null;
                return;
            }
            if (FileList.SelectedItems.Count == 0)
                SelectByName(_pendingSelectName);
        }

        private void SelectAndFocusByName(string name)
        {
            SelectByName(name);
            var match = FileList.SelectedItem;
            if (match == null || !FileList.IsKeyboardFocusWithin)
                return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var container = FileList.ItemContainerGenerator.ContainerFromItem(match) as ListViewItem;
                if (container != null)
                    container.Focus();
            }), DispatcherPriority.Input);
        }

        // ── 열기·미리보기 ────────────────────────────────────────────────────

        /// <summary>더블클릭·Enter: zip 안 항목이면 들어가거나 임시 사본으로 열고, zip 파일이면 들어간다. 처리했으면 true.</summary>
        private bool TryOpenArchiveItem(FileSystemItem item)
        {
            if (item.IsArchiveEntry)
            {
                if (item.IsDirectory)
                    NavigateTo(item.FullPath);
                else
                    OpenArchiveEntry(item);
                return true;
            }
            if (!item.IsDirectory && ArchiveBrowsingEnabled && ZipArchivePath.IsBrowsableArchive(item.FullPath))
            {
                NavigateTo(item.FullPath);
                return true;
            }
            return false;
        }

        private async void OpenArchiveEntry(FileSystemItem item)
        {
            var listing = _archiveListing;
            var entry = item.ArchiveEntry;
            if (listing == null || entry == null)
                return;
            var owner = OwnerWindow;
            if (entry.IsEncrypted)
            {
                MessageBox.Show(owner, ZipArchiveException.EncryptedMessage, "파일을 열 수 없습니다", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (FileOperationService.IsExecutable(entry.Name) &&
                MessageBox.Show(owner,
                    string.Format("'{0}'은(는) 열면 실행되는 형식입니다.\n\n이 파일 하나만 임시 폴더에 풀어 실행합니다. 같은 zip의 다른 파일이 필요한 프로그램은 제대로 동작하지 않을 수 있으니 먼저 압축을 푸는 것이 좋습니다.\n\n그래도 실행할까요?", entry.Name),
                    "zip 안 실행 파일", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;

            string path;
            try
            {
                path = await ArchiveOperations.RunAsync(owner, "zip에서 파일을 꺼내는 중",
                    (progress, token) => ZipTempStore.ExtractForOpen(listing, entry, progress, token));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                ArchiveOperations.ShowError(owner, "파일을 열 수 없습니다", ex);
                return;
            }

            if (!_readOnlyNoticeShown)
            {
                _readOnlyNoticeShown = true;
                MessageBox.Show(owner,
                    "zip 안 파일은 임시 폴더에 풀어 읽기 전용 사본으로 엽니다.\n고친 내용은 zip에 저장되지 않습니다. 고치려면 먼저 압축을 풀어 주세요.\n\n" +
                    "사본 위치: " + ZipTempStore.SessionDirectory + "\n(프로그램을 끝내면 지웁니다)\n\n이 안내는 이번 실행에서 한 번만 보입니다.",
                    "zip 안 파일 열기", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            StatusText.Text = string.Format("'{0}'을(를) 읽기 전용 사본으로 열었습니다 — 고친 내용은 zip에 반영되지 않습니다", entry.Name);

            var handler = FileOpenRequested;
            if (handler != null)
            {
                handler(this, path);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception exception)
            {
                MessageBox.Show(owner, exception.Message, "파일을 열 수 없습니다", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>zip 안 항목의 메타정보·내용 미리보기(파일 앞부분만 읽음).</summary>
        private async Task ShowArchiveEntryPreviewAsync(FileSystemItem item, int requestId)
        {
            var entry = item.ArchiveEntry;
            var listing = _archiveListing;
            MetadataPath.Text = item.FullPath;
            MetadataName.Text = item.Name;
            MetadataType.Text = item.Kind + " (zip 안)";
            MetadataCreated.Text = "-";
            MetadataModified.Text = entry.ModifiedAt.ToString("yyyy-MM-dd HH:mm:ss");
            MetadataPermissions.Text = "읽기 전용 (zip 안)";
            MetadataAttributes.Text = entry.IsEncrypted ? "암호로 보호됨" : "압축됨";
            MetadataLinkTargetLabel.Visibility = Visibility.Collapsed;
            MetadataLinkTarget.Visibility = Visibility.Collapsed;

            if (item.IsDirectory)
            {
                if (listing != null)
                {
                    int files;
                    long size;
                    listing.Measure(new[] { entry }, out files, out size);
                    MetadataSize.Text = string.Format("{0:0.00} MB (파일 {1:N0}개)", size / 1024d / 1024d, files);
                }
                PreviewEmptyText.Text = "폴더는 내용 미리보기를 지원하지 않습니다.";
                return;
            }

            MetadataSize.Text = string.Format("{0:0.00} MB (압축 {1:0.00} MB)", entry.Size / 1024d / 1024d, entry.CompressedSize / 1024d / 1024d);
            if (entry.IsEncrypted)
            {
                ShowPreviewMessage("암호로 보호된 항목이라 미리 볼 수 없습니다.");
                return;
            }
            if (listing == null)
                return;

            try
            {
                if (FilePreviewService.IsImage(entry.Name))
                {
                    if (entry.Size > ImagePreviewLimit)
                    {
                        ShowPreviewMessage("이미지가 커서(32MB 초과) 미리 보지 않습니다.");
                        return;
                    }
                    var image = await Task.Run(() => ZipArchiveService.ReadEntryBytes(listing, entry, (int)entry.Size));
                    if (requestId != _previewRequestId)
                        return;
                    ShowArchiveImagePreview(image);
                    return;
                }

                var prefix = await Task.Run(() => ZipArchiveService.ReadEntryBytes(listing, entry, FilePreviewService.TextPreviewByteLimit));
                if (requestId != _previewRequestId)
                    return;
                PreviewEmptyText.Visibility = Visibility.Collapsed;
                TextPreview.Text = FilePreviewService.DecodeTextPreview(prefix, entry.Size > prefix.Length);
                TextPreviewScroll.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                if (requestId == _previewRequestId)
                    ShowPreviewMessage("미리보기를 표시할 수 없습니다.\n" + ZipArchiveService.Describe(ex));
            }
        }

        private void ShowArchiveImagePreview(byte[] bytes)
        {
            try
            {
                using (var stream = new MemoryStream(bytes))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 1600;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    ImagePreview.Source = bitmap;
                }
                PreviewEmptyText.Visibility = Visibility.Collapsed;
                ImagePreviewContainer.Visibility = Visibility.Visible;
            }
            catch (Exception)
            {
                ShowPreviewMessage("이미지를 읽을 수 없습니다.");
            }
        }

        /// <summary>폴더의 zip 파일을 고르면 내용 요약(개수·크기·맨 위 항목)을 보인다. 목록 캐시도 미리 채워 Enter가 바로 열린다.</summary>
        private async Task ShowZipSummaryPreviewAsync(string zipPath, int requestId)
        {
            ZipListing listing;
            try
            {
                listing = await ZipListingCache.GetAsync(zipPath);
            }
            catch (Exception ex)
            {
                if (requestId == _previewRequestId)
                    ShowPreviewMessage("zip 목록을 읽을 수 없습니다.\n" + ZipArchiveService.Describe(ex is AggregateException && ex.InnerException != null ? ex.InnerException : ex));
                return;
            }
            if (requestId != _previewRequestId)
                return;

            var text = new StringBuilder();
            text.AppendLine(ArchiveBrowsingEnabled ? "zip 파일 — Enter(더블클릭)로 안을 볼 수 있습니다." : "zip 파일");
            text.AppendLine();
            text.AppendFormat("파일 {0:N0}개 · 폴더 {1:N0}개", listing.FileCount, listing.FolderCount).AppendLine();
            var ratio = listing.TotalSize > 0 ? 100.0 * listing.TotalCompressedSize / listing.TotalSize : 100.0;
            text.AppendFormat("압축 전 {0} · 압축 후 {1} ({2:0}%)", ArchiveOperations.FormatSize(listing.TotalSize),
                ArchiveOperations.FormatSize(listing.TotalCompressedSize), ratio).AppendLine();
            if (listing.EncryptedFileCount > 0)
                text.AppendFormat("🔒 암호로 보호된 항목 {0:N0}개 — 열거나 풀 수 없습니다", listing.EncryptedFileCount).AppendLine();
            if (listing.UnsafeEntryCount > 0)
                text.AppendFormat("⚠ zip 밖을 가리키는 등 위험한 이름 {0:N0}개 — 숨기고 풀지 않습니다", listing.UnsafeEntryCount).AppendLine();
            text.AppendLine();
            text.AppendLine("맨 위 항목:");
            var children = listing.GetChildren(string.Empty)
                .OrderByDescending(child => child.IsDirectory)
                .ThenBy(child => child.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            foreach (var child in children.Take(100))
            {
                text.AppendLine(child.IsDirectory
                    ? "  📂 " + child.Name + "\\"
                    : string.Format("  📄 {0}  ({1})", child.Name, ArchiveOperations.FormatSize(child.Size)));
            }
            if (children.Count > 100)
                text.AppendFormat("  … 외 {0:N0}개", children.Count - 100).AppendLine();

            PreviewEmptyText.Visibility = Visibility.Collapsed;
            TextPreview.Text = text.ToString();
            TextPreviewScroll.Visibility = Visibility.Visible;
        }

        // ── 꺼내기: 끌어 놓기 ────────────────────────────────────────────────

        /// <summary>앱 안 끌어 놓기용 데이터. 놓을 때까지 풀지 않는다(탐색기 등 다른 프로그램은 이 형식을 받지 않음).</summary>
        internal sealed class ArchiveDragPayload
        {
            public const string Format = "Folderss.ArchiveEntries";

            public ArchiveDragPayload(FolderBrowser source, ZipListing listing, IList<ZipEntryInfo> entries)
            {
                Source = source;
                Listing = listing;
                Entries = entries;
            }

            public FolderBrowser Source { get; }
            public ZipListing Listing { get; }
            public IList<ZipEntryInfo> Entries { get; }

            public static ArchiveDragPayload From(IDataObject data)
            {
                try
                {
                    return data != null && data.GetDataPresent(Format) ? data.GetData(Format) as ArchiveDragPayload : null;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        private void BeginArchiveDrag()
        {
            var entries = SelectedItems.Where(item => item.IsArchiveEntry).Select(item => item.ArchiveEntry).ToList();
            if (entries.Count == 0 || _archiveListing == null)
                return;
            var data = new DataObject(ArchiveDragPayload.Format, new ArchiveDragPayload(this, _archiveListing, entries));
            DragDrop.DoDragDrop(FileList, data, DragDropEffects.Copy);
        }

        /// <summary>끌어 놓기 위로 지날 때: zip 항목이면 복사(압축 풀기)로, zip 안으로 넣으려 하면 막는다. 처리했으면 true.</summary>
        private bool TryHandleArchiveDragOver(DragEventArgs e)
        {
            var payload = ArchiveDragPayload.From(e.Data);
            if (payload == null && !IsInArchive)
                return false;

            string reason = null;
            var destination = payload == null ? null : GetArchiveDropDestination(e, payload, out reason);
            e.Effects = destination == null ? DragDropEffects.None : DragDropEffects.Copy;
            StatusText.Text = destination == null
                ? (payload == null ? "zip 안에는 넣을 수 없습니다 (읽기 전용)" : reason)
                : "압축 풀기(복사): " + destination;
            e.Handled = true;
            return true;
        }

        private bool TryHandleArchiveDrop(DragEventArgs e)
        {
            var payload = ArchiveDragPayload.From(e.Data);
            if (payload == null && !IsInArchive)
                return false;

            e.Handled = true;
            string reason = null;
            var destination = payload == null ? null : GetArchiveDropDestination(e, payload, out reason);
            if (destination == null)
            {
                e.Effects = DragDropEffects.None;
                UpdateStatusText();
                if (payload == null)
                    Folderss.MainWindow.ShowArchiveReadOnlyMessage("끌어 넣을 수 없습니다");
                else if (reason == PinLockedReason)
                    Folderss.MainWindow.ShowPinLockedMessage();
                return true;
            }

            e.Effects = DragDropEffects.Copy;
            var answer = MessageBox.Show(OwnerWindow,
                string.Format("zip에서 {0}개 항목을 다음 폴더로 꺼낼까요(복사)?\n\n{1}\n\n같은 이름이 있으면 '이름 (2)'로 만듭니다.", payload.Entries.Count, destination),
                "zip에서 복사", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
            if (answer != MessageBoxResult.Yes)
            {
                UpdateStatusText();
                return true;
            }
            ExtractEntriesTo(payload.Listing, payload.Entries, destination);
            return true;
        }

        private const string PinLockedReason = "고정된 폴더: 구조를 변경할 수 없습니다.";

        private string GetArchiveDropDestination(DragEventArgs e, ArchiveDragPayload payload, out string reason)
        {
            reason = null;
            if (_safeMode || IsInArchive)
            {
                reason = IsInArchive ? "zip 안에는 넣을 수 없습니다 (읽기 전용)" : "이 창에서는 zip에서 끌어 놓을 수 없습니다";
                return null;
            }
            var destination = GetDropDestination(e.OriginalSource as DependencyObject);
            if (string.IsNullOrWhiteSpace(destination) || !Directory.Exists(destination))
            {
                reason = "놓을 수 없는 위치입니다";
                return null;
            }
            var host = HostWindow;
            if (host != null && host.IsDestinationPinLocked(destination))
            {
                reason = PinLockedReason;
                return null;
            }
            return destination;
        }

        // ── 꺼내기: 복사·압축 풀기 ───────────────────────────────────────────

        /// <summary>zip 안에서 고른 항목을 <paramref name="destination"/>에 기존 복사 규칙(같은 이름이면 '이름 (2)')으로 꺼낸다.</summary>
        public void ExtractSelectionToFolder(string destination)
        {
            var entries = SelectedItems.Where(item => item.IsArchiveEntry).Select(item => item.ArchiveEntry).ToList();
            if (entries.Count == 0 || _archiveListing == null)
            {
                MessageBox.Show(OwnerWindow, "zip에서 꺼낼 항목을 선택하세요.", "zip에서 복사", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (string.IsNullOrWhiteSpace(destination) || !Directory.Exists(destination))
                return;
            var host = HostWindow;
            if (host != null && host.IsDestinationPinLocked(destination))
            {
                Folderss.MainWindow.ShowPinLockedMessage();
                return;
            }
            ExtractEntriesTo(_archiveListing, entries, destination);
        }

        private async void ExtractEntriesTo(ZipListing listing, IList<ZipEntryInfo> entries, string destination)
        {
            var result = await ArchiveOperations.ExtractAsync(OwnerWindow, listing, entries, destination, new ZipExtractOptions { UniqueTopLevel = true },
                "zip에서 복사하는 중");
            if (result != null)
                AfterArchiveOperation(destination, result.TopLevelTargets.FirstOrDefault());
        }

        /// <summary>Ctrl+C: 고른 항목을 임시 폴더에 풀고 그 경로를 클립보드(파일 목록)에 넣는다 — 앱·탐색기 붙여넣기가 그대로 동작한다.</summary>
        public async void CopyArchiveSelectionToClipboard()
        {
            var listing = _archiveListing;
            var entries = SelectedItems.Where(item => item.IsArchiveEntry).Select(item => item.ArchiveEntry).ToList();
            if (listing == null || entries.Count == 0)
                return;

            var owner = OwnerWindow;
            int files;
            long size;
            listing.Measure(entries, out files, out size);
            if (size > ClipboardConfirmBytes &&
                MessageBox.Show(owner,
                    string.Format("클립보드에 넣으려면 선택한 항목({0})을 임시 폴더에 먼저 풀어야 합니다. 계속할까요?\n\n반대편 패널로 바로 꺼내려면 끌어 놓기나 우클릭 > 반대편 패널로 복사가 빠릅니다.", ArchiveOperations.FormatSize(size)),
                    "zip에서 복사", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;

            string directory;
            try
            {
                directory = ZipTempStore.CreateClipboardDirectory();
            }
            catch (Exception ex)
            {
                ArchiveOperations.ShowError(owner, "zip에서 복사", ex);
                return;
            }

            var result = await ArchiveOperations.ExtractAsync(owner, listing, entries, directory, new ZipExtractOptions { UniqueTopLevel = true }, "클립보드에 복사하는 중");
            if (result == null || result.Cancelled || result.TopLevelTargets.Count == 0)
                return;

            var paths = new System.Collections.Specialized.StringCollection();
            foreach (var path in result.TopLevelTargets)
                paths.Add(path);
            var data = new DataObject();
            data.SetFileDropList(paths);
            data.SetText(BuildNameListText(result.TopLevelTargets), TextDataFormat.UnicodeText);
            if (!ClipboardService.TrySetDataObject(data))
                return;
            HostWindow?.ClearCutStateFromClipboard();
            StatusText.Text = string.Format("{0:N0}개 항목을 클립보드에 복사했습니다 (임시 사본 — 붙여넣으면 복사됩니다)", result.TopLevelTargets.Count);
        }

        /// <summary>zip 전체를 zip이 있는 폴더의 zip 이름 새 폴더에 푼다(zip 띠의 `압축 풀기`, 우클릭 `전체 압축 풀기`).</summary>
        private async void ExtractWholeArchiveToNamedFolder()
        {
            var archive = _archivePath;
            if (archive == null)
                return;
            var folder = Path.GetDirectoryName(archive);
            if (IsPinLockedDestinationForArchive(folder))
                return;
            var created = await ArchiveOperations.ExtractToNamedFolderAsync(OwnerWindow, archive, folder, ZipConflictMode.Rename);
            if (created != null)
            {
                AfterArchiveOperation(folder, created);
                if (IsInArchive)
                    StatusText.Text = "압축을 풀었습니다: " + created;
            }
        }

        /// <summary>폴더의 zip 파일들을 각각 zip 이름 새 폴더에 푼다(우클릭 `압축 풀기 → '이름\'`).</summary>
        private async void ExtractArchivesToNamedFolders(IList<string> zipPaths)
        {
            var folder = Path.GetDirectoryName(zipPaths[0]);
            if (IsPinLockedDestinationForArchive(folder))
                return;
            string last = null;
            foreach (var zip in zipPaths)
            {
                var created = await ArchiveOperations.ExtractToNamedFolderAsync(OwnerWindow, zip, Path.GetDirectoryName(zip), ZipConflictMode.Rename);
                if (created != null)
                    last = created;
            }
            if (last != null)
                AfterArchiveOperation(folder, last);
        }

        /// <summary>
        /// 압축 풀기 옵션 대화상자. zip 안이면 고른 항목(없으면 zip 전체), 폴더면 고른 zip 파일들. ⋯ 메뉴 `압축 풀기…`도 이것을 부른다.
        /// </summary>
        public void ShowExtractDialog()
        {
            if (IsInArchive)
            {
                ShowArchiveExtractDialog(SelectedItems.Where(item => item.IsArchiveEntry).ToList());
                return;
            }
            var zips = SelectedItems
                .Where(item => !item.IsDirectory && (ZipArchivePath.IsBrowsableArchive(item.FullPath) || ZipArchivePath.CanOpenAsZip(item.FullPath)))
                .Select(item => item.FullPath)
                .ToList();
            if (zips.Count == 0)
            {
                MessageBox.Show(OwnerWindow, "압축을 풀 zip 파일을 선택하세요.", "압축 풀기", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            ShowExtractDialogForArchives(zips);
        }

        private void ShowArchiveExtractDialog(IList<FileSystemItem> selected)
        {
            var archive = _archivePath;
            var listing = _archiveListing;
            if (archive == null || listing == null)
                return;
            var zipName = Path.GetFileNameWithoutExtension(archive);
            var dialog = new ArchiveExtractDialog("'" + Path.GetFileName(archive) + "'", selected.Count, Path.GetDirectoryName(archive), OppositeFolderPath(), zipName)
            {
                Owner = OwnerWindow
            };
            if (dialog.ShowDialog() != true)
                return;

            if (!dialog.ExtractSelection)
            {
                RunExtractDialogForArchives(new[] { archive }, dialog);
                return;
            }
            RunExtractDialogForSelection(listing, selected.Select(item => item.ArchiveEntry).ToList(), zipName, dialog);
        }

        private async void RunExtractDialogForSelection(ZipListing listing, IList<ZipEntryInfo> entries, string zipName, ArchiveExtractDialog dialog)
        {
            var destination = PrepareDestination(dialog.Destination);
            if (destination == null)
                return;
            if (dialog.CreateNamedFolder)
            {
                destination = ZipArchiveService.GetUniquePath(Path.Combine(destination, ZipArchiveService.SanitizeSegment(zipName)), true, null);
                if (PrepareDestination(destination) == null)
                    return;
            }
            var result = await ArchiveOperations.ExtractAsync(OwnerWindow, listing, entries, destination,
                new ZipExtractOptions { UniqueTopLevel = false, Conflict = dialog.Conflict });
            if (result != null)
                AfterArchiveOperation(dialog.Destination, dialog.CreateNamedFolder ? destination : result.TopLevelTargets.FirstOrDefault());
        }

        private void ShowExtractDialogForArchives(IList<string> zipPaths)
        {
            var subject = zipPaths.Count == 1 ? "'" + Path.GetFileName(zipPaths[0]) + "'" : string.Format("zip 파일 {0}개", zipPaths.Count);
            var dialog = new ArchiveExtractDialog(subject, 0, Path.GetDirectoryName(zipPaths[0]), OppositeFolderPath(),
                zipPaths.Count == 1 ? Path.GetFileNameWithoutExtension(zipPaths[0]) : null)
            {
                Owner = OwnerWindow
            };
            if (dialog.ShowDialog() == true)
                RunExtractDialogForArchives(zipPaths, dialog);
        }

        private async void RunExtractDialogForArchives(IList<string> zipPaths, ArchiveExtractDialog dialog)
        {
            var destination = PrepareDestination(dialog.Destination);
            if (destination == null)
                return;
            var owner = OwnerWindow;
            string last = null;
            foreach (var zip in zipPaths)
            {
                if (dialog.CreateNamedFolder)
                {
                    last = await ArchiveOperations.ExtractToNamedFolderAsync(owner, zip, destination, dialog.Conflict) ?? last;
                    continue;
                }
                var listing = await ArchiveOperations.GetListingAsync(owner, zip);
                if (listing == null)
                    continue;
                var result = await ArchiveOperations.ExtractAsync(owner, listing, new[] { listing.Root }, destination,
                    new ZipExtractOptions { UniqueTopLevel = false, Conflict = dialog.Conflict });
                if (result != null)
                    last = result.TopLevelTargets.FirstOrDefault() ?? last;
            }
            AfterArchiveOperation(destination, last);
        }

        /// <summary>풀 위치를 확인하고(고정 폴더 아님) 없으면 만든다. 쓸 수 없으면 알리고 null.</summary>
        private string PrepareDestination(string destination)
        {
            if (string.IsNullOrWhiteSpace(destination))
                return null;
            if (IsPinLockedDestinationForArchive(destination))
                return null;
            try
            {
                Directory.CreateDirectory(destination);
                return destination;
            }
            catch (Exception ex)
            {
                ArchiveOperations.ShowError(OwnerWindow, "압축 풀기", ex);
                return null;
            }
        }

        private bool IsPinLockedDestinationForArchive(string destination)
        {
            var host = HostWindow;
            if (host == null || !host.IsDestinationPinLocked(destination))
                return false;
            Folderss.MainWindow.ShowPinLockedMessage();
            return true;
        }

        /// <summary>작업이 끝나면 패널을 새로 고치고, 결과 폴더를 보고 있는 패널에서 만든 항목을 고른다.</summary>
        private void AfterArchiveOperation(string directory, string createdPath)
        {
            var host = HostWindow;
            if (host != null)
            {
                host.ShowArchiveOperationResult(directory, createdPath);
                return;
            }
            RefreshItems();
            if (createdPath != null && !IsInArchive && IsSameDirectory(CurrentPath, directory))
                SelectCreatedItem(Path.GetFileName(createdPath));
        }

        /// <summary>반대편(다른) 폴더 패널의 실제 폴더. 없거나 그 패널도 zip 안이면 null.</summary>
        private string OppositeFolderPath()
        {
            var opposite = HostWindow?.GetOppositePane(this);
            return opposite != null && !opposite.IsInArchive && !string.IsNullOrWhiteSpace(opposite.CurrentPath) && Directory.Exists(opposite.CurrentPath)
                ? opposite.CurrentPath
                : null;
        }

        // ── zip으로 압축 ─────────────────────────────────────────────────────

        /// <summary>⋯ 메뉴 `zip으로 압축…`: 고른 항목을 옵션 대화상자로 압축한다.</summary>
        public void ShowCompressDialog()
        {
            if (IsInArchive)
            {
                MessageBox.Show(OwnerWindow, "zip 안 항목은 바로 압축할 수 없습니다. 먼저 꺼낸 뒤 압축하세요.", "zip으로 압축", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var selected = SelectedItems.ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(OwnerWindow, "압축할 파일이나 폴더를 선택하세요.", "zip으로 압축", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            ShowCompressDialog(selected.Select(item => item.FullPath).ToList(), DefaultZipName(selected));
        }

        private void ShowCompressDialog(IList<string> sources, string defaultName)
        {
            var subject = sources.Count == 1 ? "'" + Path.GetFileName(sources[0]) + "'" : string.Format("{0:N0}개 항목", sources.Count);
            var dialog = new ArchiveCompressDialog(subject, defaultName, CurrentPath, OppositeFolderPath()) { Owner = OwnerWindow };
            if (dialog.ShowDialog() == true)
                Compress(sources, dialog.Destination, dialog.ZipName, dialog.Level);
        }

        private async void Compress(IList<string> sources, string destination, string zipName, CompressionLevel level)
        {
            if (IsPinLockedDestinationForArchive(destination))
                return;
            var result = await ArchiveOperations.CreateZipAsync(OwnerWindow, sources.ToList(), Path.Combine(destination, zipName), level);
            if (result != null && result.ZipPath != null)
                AfterArchiveOperation(destination, result.ZipPath);
        }

        /// <summary>하나면 그 이름(파일은 확장자 대신 .zip), 여럿이면 현재 폴더 이름.</summary>
        private string DefaultZipName(IList<FileSystemItem> selected)
        {
            string stem;
            if (selected.Count == 1)
                stem = selected[0].IsDirectory ? selected[0].Name : Path.GetFileNameWithoutExtension(selected[0].Name);
            else
                stem = Path.GetFileName((CurrentPath ?? string.Empty).TrimEnd('\\'));
            if (string.IsNullOrWhiteSpace(stem))
                stem = "압축";
            return stem + ".zip";
        }

        // ── 우클릭 메뉴 ──────────────────────────────────────────────────────

        /// <summary>폴더의 Windows 셸 메뉴 맨 위에 붙일 zip 항목(압축 풀기·zip으로 열어 보기·zip으로 압축).</summary>
        private void AddArchiveShellMenuItems(List<ShellContextMenuService.CustomMenuItem> customItems)
        {
            var selected = SelectedItems.ToList();
            if (selected.Count == 0 || selected.Any(item => item.IsArchiveEntry))
                return;

            var zips = selected.Where(item => !item.IsDirectory && ZipArchivePath.IsBrowsableArchive(item.FullPath)).ToList();
            if (zips.Count == selected.Count)
            {
                var paths = zips.Select(item => item.FullPath).ToList();
                var label = zips.Count == 1
                    ? string.Format("압축 풀기 → '{0}\\'", Path.GetFileNameWithoutExtension(zips[0].Name))
                    : string.Format("압축 풀기 → 각 zip 이름 폴더 ({0}개)", zips.Count);
                AddShellItem(customItems, label, () => ExtractArchivesToNamedFolders(paths));
                AddShellItem(customItems, "압축 풀기…", () => ShowExtractDialogForArchives(paths));
            }
            else if (selected.Count == 1 && !selected[0].IsDirectory && ArchiveBrowsingEnabled && ZipArchivePath.CanOpenAsZip(selected[0].FullPath))
            {
                var path = selected[0].FullPath;
                AddShellItem(customItems, "zip으로 열어 보기", () => NavigateTo(path));
            }

            var sources = selected.Select(item => item.FullPath).ToList();
            var zipName = DefaultZipName(selected);
            var folder = CurrentPath;
            AddShellItem(customItems, string.Format("zip으로 압축 → '{0}'", zipName), () => Compress(sources, folder, zipName, CompressionLevel.Optimal));
            AddShellItem(customItems, "zip으로 압축…", () => ShowCompressDialog(sources, zipName));
        }

        private static void AddShellItem(List<ShellContextMenuService.CustomMenuItem> items, string label, Action invoke)
        {
            // Win32 메뉴 문자열의 &는 단축 글자 표시라 그대로 보이게 &&로 바꾼다.
            items.Add(new ShellContextMenuService.CustomMenuItem { Label = label.Replace("&", "&&"), Invoke = invoke });
        }

        /// <summary>zip 안 우클릭 메뉴(Windows 셸 메뉴는 실제 파일에만 쓸 수 있다).</summary>
        private void ShowArchiveContextMenu()
        {
            var archive = _archivePath;
            var selected = SelectedItems.Where(item => item.IsArchiveEntry).ToList();
            var opposite = OppositeFolderPath();
            var menu = new ContextMenu();

            if (selected.Count > 0)
            {
                if (selected.Count == 1)
                {
                    var single = selected[0];
                    AddMenuItem(menu, single.IsDirectory ? "열기" : "열기 (읽기 전용 사본)", "Enter", () => TryOpenArchiveItem(single));
                }
                var toOpposite = AddMenuItem(menu, "반대편 패널로 복사 (압축 풀기)", null, () => ExtractSelectionToFolder(opposite));
                toOpposite.IsEnabled = opposite != null;
                toOpposite.ToolTip = opposite ?? "반대편 폴더 패널이 없거나 zip 안을 보고 있습니다.";
                ToolTipService.SetShowOnDisabled(toOpposite, true);
                AddMenuItem(menu, "복사", "Ctrl+C", CopyArchiveSelectionToClipboard);
                AddMenuItem(menu, "압축 풀기…", null, () => ShowArchiveExtractDialog(selected));
                menu.Items.Add(new Separator());
            }

            AddMenuItem(menu, string.Format("전체 압축 풀기 → '{0}\\'", Path.GetFileNameWithoutExtension(archive)), null, ExtractWholeArchiveToNamedFolder);
            AddMenuItem(menu, "전체 압축 풀기…", null, () => ShowExtractDialogForArchives(new[] { archive }));
            menu.Items.Add(new Separator());
            AddMenuItem(menu, "zip 밖으로 나가기", "Backspace", ExitArchiveToFolder);
            AddMenuItem(menu, "탐색기에서 zip 파일 보기", null, () => RevealInExplorer(archive));

            menu.PlacementTarget = FileList;
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
        }

        private static MenuItem AddMenuItem(ContextMenu menu, string header, string gesture, Action action)
        {
            var item = new MenuItem { Header = header, InputGestureText = gesture ?? string.Empty };
            item.Click += (s, e) => action();
            menu.Items.Add(item);
            return item;
        }

        private void RevealInExplorer(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
            }
            catch (Exception exception)
            {
                MessageBox.Show(OwnerWindow, exception.Message, "탐색기를 열 수 없습니다", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── zip 띠(배너) 버튼 ────────────────────────────────────────────────

        private void ArchiveExtract_Click(object sender, RoutedEventArgs e)
        {
            ExtractWholeArchiveToNamedFolder();
        }

        private void ArchiveExtractOptions_Click(object sender, RoutedEventArgs e)
        {
            ShowArchiveExtractDialog(SelectedItems.Where(item => item.IsArchiveEntry).ToList());
        }

        private void ArchiveExit_Click(object sender, RoutedEventArgs e)
        {
            ExitArchiveToFolder();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private bool? _lastLatestChapterSortDescending;
        private string _lastNhentaiResolvedHtml;
        private CancellationTokenSource _autoRetryCts;
        private Task _autoRetryLoopTask;
        private const string DefaultListFileName = "gallery-list-default.md";
        private const string DefaultListLegacyFileName = "gallery-list-default.txt";
        private const string AutosaveListFileName = "gallery-list-autosave.md";
        private const string AutosaveListLegacyFileName = "gallery-list-autosave.txt";
        private const string AutosaveListMirrorFileName = "gallery-list-autosave.mirror.md";
        private const int AutosaveDebounceMs = 150;
        private readonly object _galleryAutosaveLock = new object();
        private Timer _galleryAutosaveTimer;
        private bool _isGalleryAutosaveInitialized;
        private bool _isGalleryAutosaveSuspended;
        private bool _isRestoringGalleryListState;
        private string _currentGalleryListPath;

        private sealed class GallerySnapshot
        {
            public bool IsChecked { get; set; }
            public string Link { get; set; }
            public string Name { get; set; }
            public int OriginalIndex { get; set; }
            public string LinkCount { get; set; }
            public string SourceDomain { get; set; }
            public bool IsDuplicate { get; set; }
            public bool HasNoChapters { get; set; }
            public int NhentaiTotalPagesHint { get; set; }
        }

        private string DefaultListPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DefaultListFileName);
        private string DefaultListLegacyPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DefaultListLegacyFileName);
        private string AutosaveListPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "autosave", AutosaveListFileName);
        private string AutosaveListLegacyPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "autosave", AutosaveListLegacyFileName);
        private string AutosaveListMirrorPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "autosave", AutosaveListMirrorFileName);
        internal string CurrentGalleryListPath => string.IsNullOrWhiteSpace(_currentGalleryListPath) ? AutosaveListPath : _currentGalleryListPath;

        private void InitializeGalleryListAutosave()
        {
            if (_isGalleryAutosaveInitialized)
            {
                return;
            }

            _isGalleryAutosaveInitialized = true;
            _galleryAutosaveTimer = new Timer(_ => SaveActiveGalleryListSnapshot(), null, Timeout.Infinite, Timeout.Infinite);
            _scrapedItems.CollectionChanged += ScrapedItems_CollectionChanged;
            _lightNovelItems.CollectionChanged += LightNovelItems_CollectionChanged;

            foreach (var item in _scrapedItems)
            {
                SubscribeGalleryItemAutosave(item);
            }

            foreach (var item in _lightNovelItems)
            {
                SubscribeLightNovelItemAutosave(item);
            }

            foreach (var chapters in _lightNovelChapterMap.Values)
            {
                TrackLightNovelChapterAutosave(chapters);
            }

            RunWithGalleryAutosaveSuspended(() =>
            {
                _currentGalleryListPath = AutosaveListPath;
                TryLoadGalleryListFile(AutosaveListPath, showMessage: false);
            });

            RequestGalleryListAutosave(0);
        }

        private void SaveActiveGalleryListSnapshot()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(SaveActiveGalleryListSnapshot);
                return;
            }

            if (_isGalleryAutosaveSuspended)
            {
                return;
            }

            try
            {
                string dir = Path.GetDirectoryName(AutosaveListPath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                SaveGalleryListToFile(AutosaveListPath);
                SaveGalleryItemsMarkdownFile(AutosaveListMirrorPath, GetFlattenedScrapedItems(), "Gallery Autosave Mirror Snapshot");
            }
            catch (Exception ex)
            {
                Log($"Autosave failed: {ex.Message}");
            }
        }

        private void RequestGalleryListAutosave(int delayMs = AutosaveDebounceMs)
        {
            if (_isGalleryAutosaveSuspended || _galleryAutosaveTimer == null)
            {
                return;
            }

            lock (_galleryAutosaveLock)
            {
                _galleryAutosaveTimer.Change(Math.Max(0, delayMs), Timeout.Infinite);
            }
        }

        private void ScrapedItems_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e?.OldItems != null)
            {
                foreach (var item in e.OldItems.OfType<GalleryItem>())
                {
                    UnsubscribeGalleryItemAutosave(item);
                }
            }

            if (e?.NewItems != null)
            {
                foreach (var item in e.NewItems.OfType<GalleryItem>())
                {
                    SubscribeGalleryItemAutosave(item);
                }
            }

            if (!_isRestoringGalleryListState)
            {
                bool hasListChange = e == null ||
                                     e.Action == NotifyCollectionChangedAction.Reset ||
                                     e.Action == NotifyCollectionChangedAction.Remove ||
                                     e.Action == NotifyCollectionChangedAction.Replace ||
                                     e.Action == NotifyCollectionChangedAction.Move ||
                                     e.OldItems != null ||
                                     e.NewItems != null;

                if (hasListChange)
                {
                    RequestDownloadMissingChapterRowSync(forceSyncOrder: true);
                    RequestAutoScanDownloadMissingChapters(immediate: true);
                }
            }

            RequestGalleryListAutosave();
            UpdateStats();
        }

        private void LightNovelItems_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e?.OldItems != null)
            {
                foreach (var item in e.OldItems.OfType<GalleryItem>())
                {
                    UnsubscribeLightNovelItemAutosave(item);
                }
            }

            if (e?.NewItems != null)
            {
                foreach (var item in e.NewItems.OfType<GalleryItem>())
                {
                    SubscribeLightNovelItemAutosave(item);
                }
            }

            RequestGalleryListAutosave();
        }

        private void SubscribeGalleryItemAutosave(GalleryItem item)
        {
            if (item == null)
            {
                return;
            }

            item.PropertyChanged -= GalleryItem_AutosavePropertyChanged;
            item.PropertyChanged += GalleryItem_AutosavePropertyChanged;
        }

        private void UnsubscribeGalleryItemAutosave(GalleryItem item)
        {
            if (item == null)
            {
                return;
            }

            item.PropertyChanged -= GalleryItem_AutosavePropertyChanged;
        }

        private void SubscribeLightNovelItemAutosave(GalleryItem item)
        {
            if (item == null)
            {
                return;
            }

            item.PropertyChanged -= LightNovelItem_AutosavePropertyChanged;
            item.PropertyChanged += LightNovelItem_AutosavePropertyChanged;
        }

        private void UnsubscribeLightNovelItemAutosave(GalleryItem item)
        {
            if (item == null)
            {
                return;
            }

            item.PropertyChanged -= LightNovelItem_AutosavePropertyChanged;
        }

        private void LightNovelItem_AutosavePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            RequestGalleryListAutosave();
        }

        private void TrackLightNovelChapterAutosave(System.Collections.ObjectModel.ObservableCollection<LightNovelChapterRecord> chapters)
        {
            if (chapters == null)
            {
                return;
            }

            chapters.CollectionChanged -= LightNovelChapters_CollectionChanged;
            chapters.CollectionChanged += LightNovelChapters_CollectionChanged;

            foreach (var chapter in chapters)
            {
                SubscribeLightNovelChapterAutosave(chapter);
            }
        }

        private void LightNovelChapters_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e?.OldItems != null)
            {
                foreach (var chapter in e.OldItems.OfType<LightNovelChapterRecord>())
                {
                    UnsubscribeLightNovelChapterAutosave(chapter);
                }
            }

            if (e?.NewItems != null)
            {
                foreach (var chapter in e.NewItems.OfType<LightNovelChapterRecord>())
                {
                    SubscribeLightNovelChapterAutosave(chapter);
                }
            }

            RequestGalleryListAutosave();
        }

        private void SubscribeLightNovelChapterAutosave(LightNovelChapterRecord chapter)
        {
            if (chapter == null)
            {
                return;
            }

            chapter.PropertyChanged -= LightNovelChapter_AutosavePropertyChanged;
            chapter.PropertyChanged += LightNovelChapter_AutosavePropertyChanged;
        }

        private void UnsubscribeLightNovelChapterAutosave(LightNovelChapterRecord chapter)
        {
            if (chapter == null)
            {
                return;
            }

            chapter.PropertyChanged -= LightNovelChapter_AutosavePropertyChanged;
        }

        private void LightNovelChapter_AutosavePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            RequestGalleryListAutosave();
        }

        private void GalleryItem_AutosavePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (sender is GalleryItem item &&
                !string.IsNullOrWhiteSpace(e?.PropertyName))
            {
                bool isHighFrequencyProgressProperty =
                    string.Equals(e.PropertyName, nameof(GalleryItem.DownloadProgressPercent), StringComparison.Ordinal) ||
                    string.Equals(e.PropertyName, nameof(GalleryItem.DownloadSpeedBytesPerSecond), StringComparison.Ordinal) ||
                    string.Equals(e.PropertyName, nameof(GalleryItem.CurrentProcess), StringComparison.Ordinal) ||
                    string.Equals(e.PropertyName, nameof(GalleryItem.CompletedChapters), StringComparison.Ordinal) ||
                    string.Equals(e.PropertyName, nameof(GalleryItem.DownloadingChapter), StringComparison.Ordinal) ||
                    string.Equals(e.PropertyName, nameof(GalleryItem.DownloadingPageProgress), StringComparison.Ordinal) ||
                    string.Equals(e.PropertyName, nameof(GalleryItem.DownloadingPageLink), StringComparison.Ordinal);

                if (isHighFrequencyProgressProperty)
                {
                    return;
                }
                bool isMissingChapterUiProperty =
                    string.Equals(e.PropertyName, nameof(GalleryItem.MissingChapterStatusText), StringComparison.Ordinal) ||
                    string.Equals(e.PropertyName, nameof(GalleryItem.MissingChapterSortText), StringComparison.Ordinal) ||
                    string.Equals(e.PropertyName, nameof(GalleryItem.HasMissingChapterIssue), StringComparison.Ordinal);
                if (isMissingChapterUiProperty && (_downloadMissingChapterScanInProgress || _downloadMissingChapterBulkRefreshing))
                {
                    return;
                }

                if (string.Equals(e.PropertyName, nameof(GalleryItem.Status), StringComparison.Ordinal))
                {
                    UpdateStats();
                }

                if (string.Equals(e.PropertyName, nameof(GalleryItem.Status), StringComparison.Ordinal) ||
                    string.Equals(e.PropertyName, nameof(GalleryItem.CurrentProcess), StringComparison.Ordinal) ||
                    string.Equals(e.PropertyName, nameof(GalleryItem.CompletedChapters), StringComparison.Ordinal) ||
                    string.Equals(e.PropertyName, nameof(GalleryItem.DownloadingChapter), StringComparison.Ordinal) ||
                    string.Equals(e.PropertyName, nameof(GalleryItem.DownloadingPageProgress), StringComparison.Ordinal))
                {
                }

                if (string.Equals(e.PropertyName, nameof(GalleryItem.IsChecked), StringComparison.Ordinal))
                {
                    var row = FindDownloadMissingChapterRow(item);
                    if (row != null && row.IsChecked != item.IsChecked)
                    {
                        row.IsChecked = item.IsChecked;
                    }
                    if (item.IsChecked)
                    {
                        QueueDownloadMissingChapterRescan(item);
                    }
                    HandleGalleryItemCheckedStateChanged(item);
                }
            }

            RequestGalleryListAutosave();
        }

        private void RunWithGalleryAutosaveSuspended(Action action)
        {
            _isGalleryAutosaveSuspended = true;
            try
            {
                action?.Invoke();
            }
            finally
            {
                _isGalleryAutosaveSuspended = false;
            }
        }

        private void BtnNewList_Click(object sender, RoutedEventArgs e)
        {
            _scrapedItems.Clear();
            if (dgResults != null)
            {
                SafeRefreshResultsView();
            }

            if (lblLinkCount != null)
            {
                lblLinkCount.Text = "0";
            }

            if (chkSelectAll != null)
            {
                chkSelectAll.IsChecked = false;
            }

            lblStatus.Text = _isVietnameseUi ? "Đã tạo danh sách mới." : "Started a new list.";
            RecalculateDuplicates();
        }

        private void BtnSaveCustom_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Markdown list (*.md)|*.md|List files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = "gallery-list.md"
            };

            if (dialog.ShowDialog(this) == true)
            {
                SaveGalleryListToFile(dialog.FileName);
                lblStatus.Text = _isVietnameseUi ? "Đã lưu danh sách." : "List saved.";
            }
        }

        private void BtnLoadCustom_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Markdown list (*.md)|*.md|List files (*.txt)|*.txt|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog(this) == true)
            {
                TryLoadGalleryListFile(dialog.FileName, showMessage: true);
            }
        }

        private void BtnOpenHomepage_Click(object sender, RoutedEventArgs e)
        {
            string tag = (sender as FrameworkElement)?.Tag?.ToString()?.Trim().ToLowerInvariant();
            string url = GetSourceHomepageUrl(tag);

            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    _isVietnameseUi ? $"Không thể mở trang chủ: {ex.Message}" : $"Cannot open homepage: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static string GetSourceHomepageUrl(string tag)
        {
            switch ((tag ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "truyenqq":
                    return "https://truyenqqko.com/";
                case "nettruyen":
                case "nettruyenviet10.com":
                    return "https://nettruyenviet10.com/";
                case "nettruyen.tech":
                    return "https://nettruyen.tech/";
                case "daomeoden":
                    return "https://daomeoden.net/";
                case "dilib.vn":
                case "thuviensach.vn":
                    return "https://thuviensach.vn/";
                case "doctruyen.us":
                    return "https://doctruyen.us/";
                case "loppytoonn.com":
                    return "https://loppytoonn.com/";
                case "haibabamanga.somee.com":
                    return "http://haibabamanga.somee.com/";
                case "hako":
                    return "https://ln.hako.vn/";
                case "vihentai":
                    return "https://vi-hentai.pro/";
                case "damconuong":
                    return "https://damconuong.shop/";
                case "sayhentai":
                    return "https://sayhentai.cx/";
                case "hentaiforce":
                    return "https://hentaiforce.net/";
                case "nhentai.net":
                    return "https://nhentai.net/";
                case "nhentai":
                    return "https://nhentai.xxx/";
                case "hentaiera":
                    return "https://hentaiera.com/";
                case "hentai2read":
                    return "https://hentai2read.com/";
                default:
                    return null;
            }
        }

        private string GetDefaultSourceTagUrl(string tag)
        {
            switch ((tag ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "truyenqq":
                    return "https://truyenqqko.com/the-loai/action-26";
                case "nettruyen":
                case "nettruyenviet10.com":
                    return "https://nettruyenviet10.com/tim-truyen/adventure";
                case "nettruyen.tech":
                    return "https://nettruyen.tech/the-loai/truyen-scan";
                case "dilib.vn":
                case "thuviensach.vn":
                    return "https://thuviensach.vn/truyen-tranh/shounen/";
                case "doctruyen.us":
                    return "https://doctruyen.us/the-loai/action";
                case "loppytoonn.com":
                    return "https://loppytoonn.com/the-loai/lang-man";
                case "haibabamanga.somee.com":
                    return "http://haibabamanga.somee.com/Home/Category?slug=action";
                case "daomeoden":
                    return "https://daomeoden.net/the-loai/romance.html";
                case "vihentai":
                    return "https://vi-hentai.pro/the-loai/khong-che";
                case "damconuong":
                    return "https://damconuong.shop/the-loai/elf";
                case "sayhentai":
                    return "https://sayhentai.cx/genre/romance";
                case "hentaiforce":
                    return "https://hentaiforce.net/search?q=%22uncensored%22+%22full+color";
                case "nhentai.net":
                    return "https://nhentai.net/tag/raccoon-girl/";
                case "nhentai":
                    return "https://nhentai.xxx/tag/very-long-hair/";
                case "hentai2read":
                    return "https://hentai2read.com/";
                case "hentaiera":
                    return "https://hentaiera.com/artist/yamamoto/popular/";
                case "hako":
                    return "https://ln.hako.vn/the-loai/action";
                default:
                    return null;
            }
        }

        private TextBox GetSourceTagTextBox(string tag)
        {
            switch ((tag ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "truyenqq":
                    return txtTruyenqqTagUrl;
                case "nettruyen":
                case "nettruyenviet10.com":
                    return txtNettruyenTagUrl;
                case "nettruyen.tech":
                    return txtNettruyenTechTagUrl;
                case "dilib.vn":
                case "thuviensach.vn":
                    return txtDilibTagUrl;
                case "doctruyen.us":
                    return txtDoctruyenTagUrl;
                case "loppytoonn.com":
                    return txtLoppyTagUrl;
                case "haibabamanga.somee.com":
                    return txtHaibabaTagUrl;
                case "daomeoden":
                    return txtDaomeodenTagUrl;
                case "vihentai":
                    return txtViHentaiTagUrl;
                case "damconuong":
                    return txtDamconuongTagUrl;
                case "sayhentai":
                    return txtTruyenggvnTagUrl;
                case "hentaiforce":
                    return txtTagUrl;
                case "nhentai.net":
                    return txtNhentaiNetTagUrl;
                case "nhentai":
                    return txtNhentaiTagUrl;
                case "hentai2read":
                    return txtHentai2readTagUrl;
                case "hentaiera":
                    return txtHentaieraTagUrl;
                case "hako":
                    return txtHakoTagUrl;
                default:
                    return null;
            }
        }

        private void BtnRestoreDefaultTag_Click(object sender, RoutedEventArgs e)
        {
            string tag = (sender as FrameworkElement)?.Tag?.ToString()?.Trim().ToLowerInvariant();
            string defaultUrl = GetDefaultSourceTagUrl(tag);
            TextBox targetBox = GetSourceTagTextBox(tag);
            if (string.IsNullOrWhiteSpace(defaultUrl) || targetBox == null)
            {
                return;
            }

            targetBox.Text = defaultUrl;
            targetBox.CaretIndex = targetBox.Text.Length;
            targetBox.Focus();
            lblStatus.Text = _isVietnameseUi ? "Đã khôi phục URL mặc định." : "Default URL restored.";
        }

        private async System.Threading.Tasks.Task PasteDirectLinksFromClipboardAsync(
            Func<List<string>, System.Threading.Tasks.Task> importFunc,
            string emptyMessage)
        {
            try
            {
                string text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
                var links = text
                    .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim())
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (links.Count == 0)
                {
                    MessageBox.Show(emptyMessage, "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                await importFunc(links);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    _isVietnameseUi ? $"Không thể dán link trực tiếp: {ex.Message}" : $"Cannot paste direct links: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void MainWindow_PreviewDragOver(object sender, DragEventArgs e)
        {
            e.Effects = TryGetDroppedText(e.Data, out _) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private async void MainWindow_Drop(object sender, DragEventArgs e)
        {
            if (!TryGetDroppedText(e.Data, out string text) || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            await AppendDroppedDirectLinksAsync(text);
        }

        private static bool TryGetDroppedText(IDataObject data, out string text)
        {
            text = string.Empty;
            if (data == null)
            {
                return false;
            }

            if (data.GetDataPresent(DataFormats.UnicodeText))
            {
                text = data.GetData(DataFormats.UnicodeText) as string;
            }
            else if (data.GetDataPresent(DataFormats.Text))
            {
                text = data.GetData(DataFormats.Text) as string;
            }
            else if (data.GetDataPresent("UniformResourceLocatorW"))
            {
                text = data.GetData("UniformResourceLocatorW")?.ToString();
            }
            else if (data.GetDataPresent("UniformResourceLocator"))
            {
                text = data.GetData("UniformResourceLocator")?.ToString();
            }

            return !string.IsNullOrWhiteSpace(text);
        }

        private async Task AppendDroppedDirectLinksAsync(string text)
        {
            var links = (text ?? string.Empty)
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => NormalizeBookUrl(line.Trim()))
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (links.Count == 0)
            {
                return;
            }

            if (links.Any(IsMangadexUrl))
            {
                bool proceed = await PromptMangadexLanguageSelectionAsync();
                if (!proceed)
                {
                    return;
                }
            }

            ShowResultsImportingIndicator();
            try
            {
                SelectDownloadMangaTab();
                var existingLinks = new HashSet<string>(
                    _scrapedItems
                        .Select(item => item?.Link)
                        .Where(link => !string.IsNullOrWhiteSpace(link)),
                    StringComparer.OrdinalIgnoreCase);

                foreach (string link in links)
                {
                    bool allowUiJump = link.IndexOf("loppytoonn.com", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool handled = await TryAppendSupportedDirectLinkAsync(link, showMessageBox: false, allowUiJump: allowUiJump);
                    if (handled)
                    {
                        MarkNewlyImportedItemsChecked(existingLinks);
                        ClearAppendCompletedStatus();
                        continue;
                    }

                    RouteAndProcessInputLink(link, allowUiJump: false);
                }
            }
            finally
            {
                HideResultsImportingIndicator();
            }
        }

        private List<GalleryItem> OrderItemsByDisplayOrder(IEnumerable<GalleryItem> items)
        {
            var itemSet = new HashSet<GalleryItem>(items);
            var viewItems = ResultsView?.Cast<object>()
                .OfType<GalleryItem>()
                .Where(itemSet.Contains)
                .ToList();

            if (viewItems != null && viewItems.Count == itemSet.Count)
            {
                return viewItems;
            }

            return items.OrderBy(item => item.OriginalIndex).ToList();
        }

        private void BtnSortByLatestChapter_Click(object sender, RoutedEventArgs e)
        {
            var view = ResultsView;
            if (view == null)
            {
                return;
            }

            bool descending = _lastLatestChapterSortDescending != true;
            _lastLatestChapterSortDescending = descending;

            var sorted = _scrapedItems
                .OrderByDescending(item => descending ? ExtractNumber(item.LinkCount) : -ExtractNumber(item.LinkCount))
                .ThenBy(item => item.OriginalIndex)
                .ToList();

            _scrapedItems.Clear();
            foreach (var item in sorted)
            {
                _scrapedItems.Add(item);
            }

            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription("OriginalIndex", ListSortDirection.Ascending));
            UpdateLatestChapterButtonLabel();
            Log(descending ? "Sorted by latest chapter descending." : "Sorted by latest chapter ascending.");
        }

        private void MenuInvertSelectedRows_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in dgResults.SelectedItems.Cast<GalleryItem>())
            {
                item.IsChecked = !item.IsChecked;
            }

            Log("Inverted checked state for selected rows.");
        }

        private void BtnSortByStatus_Click(object sender, RoutedEventArgs e)
        {
            ApplyResultsSort(colStatus, "StatusSortOrder", ref _isStatusSortAscending, "status");
        }

        private void BtnSortByProcess_Click(object sender, RoutedEventArgs e)
        {
            ApplyResultsSort(colProcess, "ProcessSortText", ref _isProcessSortAscending, "process");
        }

        private void BtnArchivePause_Click(object sender, RoutedEventArgs e)
        {
            _isDownloadPaused = !_isDownloadPaused;
            if (btnArchivePause != null)
            {
                btnArchivePause.Content = _isDownloadPaused ? "RESUME" : "PAUSE";
            }

            lblStatus.Text = _isDownloadPaused
                ? (_isVietnameseUi ? "Đã tạm dừng thao tác lưu trữ." : "Archive task paused.")
                : (_isVietnameseUi ? "Đã tiếp tục thao tác lưu trữ." : "Archive task resumed.");
        }

        private void BtnArchiveStop_Click(object sender, RoutedEventArgs e)
        {
            _archiveCts?.Cancel();
            _isDownloadPaused = false;
            if (archiveProgressPanel != null)
            {
                archiveProgressPanel.Visibility = Visibility.Collapsed;
            }

            if (btnArchivePause != null)
            {
                btnArchivePause.Content = "PAUSE";
            }

            lblStatus.Text = _isVietnameseUi ? "Đã dừng thao tác lưu trữ." : "Archive task stopped.";
        }

        private async void BtnRetryErrors_Click(object sender, RoutedEventArgs e)
        {
            var erroredItems = _scrapedItems
                .Where(item => item.GetUniqueErrorCount() > 0)
                .ToList();

            if (erroredItems.Count == 0)
            {
                MessageBox.Show(
                    _isVietnameseUi ? "Không có lỗi để thử lại." : "No errors to retry.",
                    "Information",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            foreach (var item in erroredItems)
            {
                await RetryDownloadQueueItemErrorsAsync(item, showMessageBox: false);
            }

            MessageBox.Show(
                _isVietnameseUi ? "Đã thử tải lại toàn bộ mục bị lỗi." : "Retried all errored items.",
                "Retry",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void BtnAutoRetryErrors_Checked(object sender, RoutedEventArgs e)
        {
            StartAutoRetryLoopAsync();
            UpdateCompactDownloadToolbarState();
            UpdateLightNovelFloatingControlState();
        }

        private void BtnAutoRetryErrors_Unchecked(object sender, RoutedEventArgs e)
        {
            StopAutoRetryLoop();
            UpdateCompactDownloadToolbarState();
            UpdateLightNovelFloatingControlState();
        }

        private void StartAutoRetryLoopAsync()
        {
            if (_autoRetryLoopTask != null && !_autoRetryLoopTask.IsCompleted)
            {
                return;
            }

            _autoRetryCts?.Dispose();
            _autoRetryCts = new CancellationTokenSource();
            CancellationToken token = _autoRetryCts.Token;

            _autoRetryLoopTask = Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                             var erroredItems = _scrapedItems
                                 .Where(item => item.GetUniqueErrors().Any(e => IsRetryableDownloadError(item, e)))
                                 .ToList();

                            foreach (var item in erroredItems)
                            {
                                token.ThrowIfCancellationRequested();
                                await RetryDownloadQueueItemErrorsAsync(item, showMessageBox: false);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            Log($"[Auto Retry] Lỗi khi retry tự động: {ex.Message}");
                        }

                        await Task.Delay(1500, token);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (btnAutoRetryErrors != null && btnAutoRetryErrors.IsChecked == true)
                        {
                            Log("[Auto Retry] Loop dừng nhưng toggle vẫn bật; sẽ tiếp tục theo dõi lỗi mới.");
                        }
                    });
                }
            }, token);
        }

        private void StopAutoRetryLoop()
        {
            _autoRetryCts?.Cancel();
        }

        private async void BtnAutoRetryErrors_Click(object sender, RoutedEventArgs e)
        {
            await Task.CompletedTask;
        }

        private void BtnRetryErrorLog_Click(object sender, RoutedEventArgs e)
        {
            var erroredItems = _scrapedItems
                .Where(item => item.GetUniqueErrorCount() > 0)
                .ToList();

            if (erroredItems.Count == 0)
            {
                MessageBox.Show(
                    _isVietnameseUi ? "Không có lỗi để hiển thị." : "No error logs available.",
                    "Information",
                    MessageBoxButton.OK,
                MessageBoxImage.Information);
                return;
            }

            var logWindow = new ErrorLogWindow(erroredItems, this)
            {
                Owner = this
            };
            logWindow.Show();
        }

        private void BtnOpenFolderInRow_Click_LegacyDoNotUse(object sender, RoutedEventArgs e)
        {
            // Legacy placeholder only. Explorer flow moved to MainWindow.SystemExplorer.cs.
            return;
        }

        private void SaveGalleryListToFile(string path)
        {
            string extension = Path.GetExtension(path ?? string.Empty);
            var flattened = GetFlattenedScrapedItems();
            if (string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase))
            {
                var lines = flattened.Select(item => string.Join("\t", new[]
                {
                    item.IsChecked ? "1" : "0",
                    EncodeCell(item.Name),
                    EncodeCell(item.Link),
                    item.OriginalIndex.ToString(),
                    EncodeCell(item.LinkCount),
                    EncodeCell(item.SourceDomain),
                    item.HasNoChapters ? "1" : "0",
                    item.NhentaiTotalPagesHint.ToString()
                }));

                WriteAllLinesAtomically(path, lines, Encoding.UTF8);
                SaveDownloadMissingChapterCacheToFile(GetDownloadMissingChapterCachePath(path));
                _currentGalleryListPath = path;
                return;
            }

            SaveGalleryItemsMarkdownFile(path, _scrapedItems.ToList(), "Gallery And Light Novel Snapshot");
            SaveDownloadMissingChapterCacheToFile(GetDownloadMissingChapterCachePath(path));
            _currentGalleryListPath = path;
        }

        private void TryLoadGalleryListFile(string path, bool showMessage)
        {
            try
            {
                string[] candidates = ResolveGalleryListLoadCandidates(path);
                List<GalleryItem> loaded = null;
                List<LightNovelBookState> loadedLightNovels = null;
                List<GalleryItemState> loadedMangaStates = null;
                string loadedPath = null;

                foreach (string candidate in candidates)
                {
                    if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate))
                    {
                        continue;
                    }

                        try
                        {
                            List<GalleryItem> currentLoaded;
                            List<LightNovelBookState> currentLoadedLightNovels = null;
                            List<GalleryItemState> currentLoadedMangaStates = null;
                            if (string.Equals(Path.GetExtension(candidate), ".md", StringComparison.OrdinalIgnoreCase))
                            {
                                string content = File.ReadAllText(candidate, Encoding.UTF8);
                                ApplyGalleryMarkdownSettings(content);
                                GalleryMarkdownPayload payload = TryLoadGalleryMarkdownPayload(content);
                                currentLoaded = LoadGalleryItemsFromMarkdown(content);
                                currentLoadedLightNovels = LoadLightNovelBooksFromMarkdown(content);
                                currentLoadedMangaStates = payload?.MangaItems?.OrderBy(item => item.OriginalIndex).ToList();
                            }
                            else
                            {
                                currentLoaded = File.ReadAllLines(candidate, Encoding.UTF8)
                                .Select(ParseGallerySnapshot)
                                .Where(item => item != null)
                                .ToList();
                            }

                            if ((currentLoaded != null && currentLoaded.Count > 0) ||
                                (currentLoadedLightNovels != null && currentLoadedLightNovels.Count > 0))
                            {
                                loaded = currentLoaded;
                                loadedLightNovels = currentLoadedLightNovels;
                                loadedMangaStates = currentLoadedMangaStates;
                                loadedPath = candidate;
                                break;
                            }
                    }
                    catch
                    {
                    }
                }

                if ((loaded == null || loaded.Count == 0) &&
                    (loadedLightNovels == null || loadedLightNovels.Count == 0))
                {
                    return;
                }

                _isRestoringGalleryListState = true;
                try
                {
                    RunWithGalleryAutosaveSuspended(() =>
                    {
                        _scrapedItems.Clear();
                        foreach (var item in loaded ?? Enumerable.Empty<GalleryItem>())
                        {
                            _scrapedItems.Add(item);
                        }

                        if (loadedMangaStates == null || !loadedMangaStates.Any(item => item.ParallelSplitChildren != null && item.ParallelSplitChildren.Count > 0))
                        {
                            AutoCollapseAllSplitTasks();
                        }
                        ApplyLoadedLightNovelBooks(loadedLightNovels ?? new List<LightNovelBookState>());
                    });

                    TryLoadDownloadMissingChapterCacheFromFile(GetDownloadMissingChapterCachePath(loadedPath ?? path));
                    MergeDownloadMissingChapterRowsFromGalleryStates(loadedMangaStates);
                    _currentGalleryListPath = loadedPath ?? path;
                }
                finally
                {
                    _isRestoringGalleryListState = false;
                }

                lblLinkCount.Text = _scrapedItems.Count.ToString();
                RecalculateDuplicates();
                RefreshLightNovelSummary();
                RequestGalleryListAutosave(0);

                if (showMessage)
                {
                    MessageBox.Show(
                        _isVietnameseUi ? "Đã tải danh sách thành công." : "List loaded successfully.",
                        "Load",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    _isVietnameseUi ? $"Không thể tải danh sách: {ex.Message}" : $"Cannot load list: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private string[] ResolveGalleryListLoadCandidates(string requestedPath)
        {
            if (string.Equals(requestedPath, AutosaveListPath, StringComparison.OrdinalIgnoreCase))
            {
                return new[]
                {
                    AutosaveListPath,
                    AutosaveListPath + ".bak",
                    AutosaveListMirrorPath,
                    AutosaveListMirrorPath + ".bak",
                    AutosaveListLegacyPath
                }
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(path => File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue)
                .ToArray();
            }

            string resolved = ResolveGalleryListLoadPath(requestedPath);
            return new[] { resolved };
        }

        private string ResolveGalleryListLoadPath(string requestedPath)
        {
            if (string.Equals(requestedPath, AutosaveListPath, StringComparison.OrdinalIgnoreCase))
            {
                string[] autosaveCandidates =
                {
                    AutosaveListPath,
                    AutosaveListPath + ".bak",
                    AutosaveListMirrorPath,
                    AutosaveListMirrorPath + ".bak",
                    AutosaveListLegacyPath
                };

                string latestAutosave = autosaveCandidates
                    .Where(File.Exists)
                    .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
                    .FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(latestAutosave))
                {
                    return latestAutosave;
                }
            }

            if (!string.IsNullOrWhiteSpace(requestedPath) && File.Exists(requestedPath))
            {
                return requestedPath;
            }

            if (string.Equals(requestedPath, AutosaveListPath, StringComparison.OrdinalIgnoreCase) && File.Exists(AutosaveListLegacyPath))
            {
                return AutosaveListLegacyPath;
            }

            if (string.Equals(requestedPath, DefaultListPath, StringComparison.OrdinalIgnoreCase) && File.Exists(DefaultListLegacyPath))
            {
                return DefaultListLegacyPath;
            }

            return requestedPath;
        }

        private GalleryItem ParseGallerySnapshot(string line)
        {
            var parts = line.Split('\t');
            if (parts.Length < 8)
            {
                return null;
            }

            int.TryParse(parts[3], out int originalIndex);
            int.TryParse(parts[7], out int nhentaiPagesHint);

            return new GalleryItem
            {
                IsChecked = parts[0] == "1",
                Name = DecodeCell(parts[1]),
                Link = DecodeCell(parts[2]),
                OriginalIndex = originalIndex,
                LinkCount = DecodeCell(parts[4]),
                SourceDomain = DecodeCell(parts[5]),
                HasNoChapters = parts[6] == "1",
                NhentaiTotalPagesHint = nhentaiPagesHint
            };
        }

        private static string EncodeCell(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        private static string DecodeCell(string value)
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(value ?? string.Empty));
            }
            catch
            {
                return value ?? string.Empty;
            }
        }
    }
}

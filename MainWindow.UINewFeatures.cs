using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

#pragma warning disable 4014
namespace get_link_manga
{
    /// <summary>
    /// Partial class for new UI features: Bookmarks, History, Download Queue,
    /// Open Link/Download buttons, and Chapter Selection.
    /// </summary>
    public partial class MainWindow : Window
    {
        // ===== BOOKMARK & HISTORY =====

        private void BtnOpenBookmarks_Click(object sender, RoutedEventArgs e)
        {
            OpenBookmarkHistoryWindow(1); // Bookmarks is index 1
        }

        private void BtnOpenHistory_Click(object sender, RoutedEventArgs e)
        {
            OpenBookmarkHistoryWindow(0); // History is index 0
        }

        private void OpenBookmarkHistoryWindow(int selectedTab = 0)
        {
            if (_bookmarkHistoryWindowInstance != null)
            {
                if (_bookmarkHistoryWindowInstance.WindowState == WindowState.Minimized)
                    _bookmarkHistoryWindowInstance.WindowState = WindowState.Normal;
                _bookmarkHistoryWindowInstance.SelectTab(selectedTab);
                _bookmarkHistoryWindowInstance.Activate();
            }
            else
            {
                _bookmarkHistoryWindowInstance = new BookmarkHistoryWindow
                {
                    Owner = this
                };
                _bookmarkHistoryWindowInstance.Closed += (s, args) => { _bookmarkHistoryWindowInstance = null; };
                _bookmarkHistoryWindowInstance.SelectTab(selectedTab);
                _bookmarkHistoryWindowInstance.Show();
            }
        }

        private void MenuBookmarkSelected_Click(object sender, RoutedEventArgs e)
        {
            if (dgResults.SelectedItems.Count == 0) return;

            int count = 0;
            foreach (var item in dgResults.SelectedItems.Cast<GalleryItem>())
            {
                string domain = "";
                try { domain = new Uri(item.Link).Host; } catch { }

                _bookmarkManager.AddBookmark(new BookmarkEntry
                {
                    Name = item.Name,
                    Url = item.Link,
                    SourceDomain = domain,
                    BookmarkedAt = DateTime.Now
                });
                count++;
            }

            Log($"Đã bookmark {count} truyện.");

            // Refresh bookmark window if open
            if (_bookmarkHistoryWindowInstance != null)
            {
                _bookmarkHistoryWindowInstance.RefreshBookmarks();
            }
        }

        private void MenuOpenInBrowser_Click(object sender, RoutedEventArgs e)
        {
            if (dgResults.SelectedItem is GalleryItem item && !string.IsNullOrEmpty(item.Link))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = item.Link,
                        UseShellExecute = true
                    });
                    Log($"🌐 Opened: {item.Link}");
                }
                catch (Exception ex)
                {
                    Log($"Failed to open link: {ex.Message}");
                }
            }
        }

        // ===== ROW ACTION BUTTONS (🌐 Open Link, ⬇️ Download Single) =====

        private void BtnOpenLinkInRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string url && !string.IsNullOrEmpty(url))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                    Log($"🌐 Opened: {url}");
                }
                catch (Exception ex)
                {
                    Log($"Failed to open link: {ex.Message}");
                }
            }
        }

        private async void BtnDownloadSingleInRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                // Get the DataContext (GalleryItem) from the button
                var item = btn.DataContext as GalleryItem;
                if (item == null) return;

                string downloadRoot = txtDownloadPath.Text.Trim();
                if (string.IsNullOrEmpty(downloadRoot))
                {
                    ShowLocalizedMessageBox(
                        "Please select a download folder first.",
                        "Vui lòng chọn thư mục lưu trước.",
                        "Error",
                        "Lỗi",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                await StartDownloadProcessAsync(new List<GalleryItem> { item }, preserveExistingState: true);
            }
        }

        // ===== DOWNLOAD QUEUE =====

        private void BtnShowErrors_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is GalleryItem item)
            {
                if (item.ErrorCount > 0)
                {
                    var errorWindow = new ErrorReportWindow(item, this)
                    {
                        Owner = this
                    };
                    errorWindow.Show();
                }
            }
        }

        private async void BtnRowResume_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is GalleryItem item)
            {
                if (_downloadCts == null && (item.IsPaused || string.Equals(item.Status, "Paused", StringComparison.OrdinalIgnoreCase) || string.Equals(item.Status, "Downloading", StringComparison.OrdinalIgnoreCase)))
                {
                    Log($"[Local Actions] Restarting saved download for '{item.Name}'");
                    await StartDownloadProcessAsync(new System.Collections.Generic.List<GalleryItem> { item }, preserveExistingState: true);
                    return;
                }

                item.IsPaused = false;
                item.Status = "Downloading";
                Log($"[Local Actions] Resumed download for '{item.Name}'");
            }
        }

        private void BtnRowPause_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is GalleryItem item)
            {
                item.IsPaused = true;
                item.Status = "Paused";
                Log($"[Local Actions] Paused download for '{item.Name}'");
            }
        }

        private void BtnRowStop_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is GalleryItem item)
            {
                item.IsStopped = true;
                item.Status = "Cancelled";
                Log($"[Local Actions] Stopped download for '{item.Name}'");
            }
        }

        internal void UpdateQueueErrorLabel()
        {
            // No-op since label was removed from UI
        }

        /// <summary>
        /// Add a gallery to the download history after successful download.
        /// </summary>
        internal void AddToHistory(GalleryItem item, int chaptersDownloaded, string downloadPath)
        {
            string domain = "";
            try { domain = new Uri(item.Link).Host; } catch { }

            _bookmarkManager.AddHistory(new HistoryEntry
            {
                Name = item.Name,
                Url = item.Link,
                SourceDomain = domain,
                DownloadedAt = DateTime.Now,
                ChaptersDownloaded = chaptersDownloaded,
                DownloadPath = downloadPath
            });

            // Refresh history window if open
            if (_bookmarkHistoryWindowInstance != null)
            {
                Dispatcher.Invoke(() => _bookmarkHistoryWindowInstance.RefreshHistory());
            }
        }

        /// <summary>
        /// Parse the chapter selection textbox and return the filter HashSet.
        /// Returns null if empty (download all). Shows error message if invalid.
        /// </summary>
        internal ChapterFilter GetChapterSelectionFilterForItem(GalleryItem item)
        {
            string itemInput = item?.ChapterSelectionText?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(itemInput))
            {
                if (ChapterRangeParser.TryParse(itemInput, out var itemFilter, out string itemError))
                {
                    string display = ChapterRangeParser.ToDisplayString(itemFilter);
                    Log($"Chapter filter applied for '{item?.Name}': {display}");
                    return itemFilter;
                }

                Dispatcher.Invoke(() =>
                {
                    ShowLocalizedMessageBox(
                        $"Chapter selection for '{item?.Name}' is invalid:\n{itemError}\n\nClear row filter or fix syntax.",
                        $"Chapter selection cho '{item?.Name}' không hợp lệ:\n{itemError}\n\nHãy xóa hoặc sửa cú pháp ở ô chapter của dòng này.",
                        "Chapter Selection Error",
                        "Lỗi chọn chapter",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                });
            }

            return null;
        }

        /// <summary>
        /// Thử tải lại các trang bị lỗi của một GalleryItem.
        /// </summary>
        public async Task RetryDownloadQueueItemErrorsAsync(GalleryItem queueItem, bool showMessageBox = true)
        {
            if (queueItem == null)
                return;

            var errorsToRetry = queueItem.GetUniqueErrors()
                .Where(e => e.PageNumber > 0 && !string.IsNullOrEmpty(e.ImageUrl) && e.AttemptCount < 3)
                .ToList();
            if (errorsToRetry.Count == 0)
                return;

            string originalStatus = queueItem.Status;
            // Remove only the errors we are retrying so we can track them
            Dispatcher.Invoke(() => {
                queueItem.Name = FormatGalleryTitle(queueItem.Name);
                foreach (var err in errorsToRetry)
                {
                    queueItem.Errors.Remove(err);
                }
                queueItem.ErrorCount = queueItem.GetUniqueErrorCount();
                queueItem.Status = "Downloading";
                queueItem.CurrentProcess = BuildRetryProcessText(0, errorsToRetry.Count, 0, 0, false);
            });

                    Log($"[Retry] Đang thử tải lại {errorsToRetry.Count} trang bị lỗi của '{queueItem.Name}'...");

            int successfulRetries = 0;
            int failedRetries = 0;
            bool wasDownloading = string.Equals(originalStatus, "Downloading", StringComparison.OrdinalIgnoreCase);
            var chapterFoldersToFinalize = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var err in errorsToRetry)
            {
                err.AttemptCount++;
                try
                {
                    string targetFolder;
                    string retryUnmergedFolder = null;
                    string retryMergedFolder = null;
                    bool isViHentai = queueItem.SourceDomain != null && queueItem.SourceDomain.Contains("vi-hentai.pro");
                    bool isTruyenqq = queueItem.SourceDomain != null && queueItem.SourceDomain.Contains("truyenqq");
                    bool isNettruyen = queueItem.SourceDomain != null && queueItem.SourceDomain.IndexOf("nettruyen", StringComparison.OrdinalIgnoreCase) >= 0;
                    string imageUrl = err.ImageUrl;

                    if (string.IsNullOrEmpty(imageUrl) && isTruyenqq)
                    {
                        imageUrl = await ResolveTruyenqqRetryImageUrlAsync(queueItem, err);
                    }

                    if (string.IsNullOrEmpty(imageUrl))
                    {
                        throw new Exception("No image URL available");
                    }

                    queueItem.Name = FormatGalleryTitle(queueItem.Name);
                    string safeManga = GetSafePathName(queueItem.Name);
                    
                    if (isViHentai)
                    {
                        string safeChapter = GetSafeChapterPathName(err.ChapterName);
                        string siteRoot = GetSiteDownloadRoot(queueItem.DownloadPath, "vi-hentai.pro");
                        retryUnmergedFolder = Path.Combine(siteRoot, $"{safeManga}-{safeChapter}");
                        retryMergedFolder = Path.Combine(siteRoot, safeManga, safeChapter);
                        targetFolder = Directory.Exists(retryMergedFolder) ? retryMergedFolder : retryUnmergedFolder;
                    }
                    else if (isTruyenqq)
                    {
                        string safeChapter = GetSafeChapterPathName(err.ChapterName);
                        string siteRoot = GetSiteDownloadRoot(queueItem.DownloadPath, "truyenqq");
                        retryUnmergedFolder = Path.Combine(siteRoot, $"{safeManga}-{safeChapter}");
                        retryMergedFolder = Path.Combine(siteRoot, safeManga, safeChapter);
                        targetFolder = Directory.Exists(retryMergedFolder) ? retryMergedFolder : retryUnmergedFolder;
                    }
                    else if (isNettruyen)
                    {
                        string safeChapter = GetSafeChapterPathName(err.ChapterName);
                        string siteRoot = GetSiteDownloadRoot(queueItem.DownloadPath, "nettruyen");
                        retryUnmergedFolder = Path.Combine(siteRoot, $"{safeManga}-{safeChapter}");
                        retryMergedFolder = Path.Combine(siteRoot, safeManga, safeChapter);
                        targetFolder = Directory.Exists(retryMergedFolder) ? retryMergedFolder : retryUnmergedFolder;
                    }
                    else
                    {
                        targetFolder = Path.Combine(GetConfiguredDownloadRoot(queueItem.DownloadPath, queueItem.SourceDomain ?? string.Empty), safeManga);
                    }

                    if (!string.IsNullOrWhiteSpace(retryUnmergedFolder) &&
                        !string.IsNullOrWhiteSpace(retryMergedFolder))
                    {
                        chapterFoldersToFinalize[retryUnmergedFolder] = retryMergedFolder;
                    }

                    Directory.CreateDirectory(targetFolder);

                    string ext = Path.GetExtension(imageUrl.Split('?')[0]);
                    if (string.IsNullOrEmpty(ext)) ext = ".jpg";
                    string fileName = $"{err.PageNumber:D3}{ext}";
                    string finalFilePath = Path.Combine(targetFolder, fileName);

                    var token = _downloadCts?.Token ?? System.Threading.CancellationToken.None;

                    await DownloadUrlToFileWithRefererAsync(
                        imageUrl,
                        queueItem.Link, 
                        finalFilePath, 
                        token, 
                        isViHentai: isViHentai, 
                        isTruyenqq: isTruyenqq
                    );

                    successfulRetries++;
                    Log($"[Retry] Đã tải thành công: {err.ChapterName}, Trang {err.PageNumber}");
                    Dispatcher.Invoke(() =>
                    {
                        var keyToRemove = _checkErrorIndex.Keys.FirstOrDefault(k => {
                            if (_checkErrorIndex.TryGetValue(k, out var val))
                            {
                                return string.Equals(val.BookName, queueItem.Name, StringComparison.OrdinalIgnoreCase) &&
                                       string.Equals(val.ChapterName, err.ChapterName, StringComparison.OrdinalIgnoreCase) &&
                                       val.PageNumber == err.PageNumber;
                            }
                            return false;
                        });

                        if (keyToRemove != null)
                        {
                            if (_checkErrorIndex.TryGetValue(keyToRemove, out var itemToRemove))
                            {
                                _checkErrors.Remove(itemToRemove);
                            }
                            _checkErrorIndex.Remove(keyToRemove);
                        }
                    });
                }
                catch (Exception ex)
                {
                    failedRetries++;
                    Log($"[Retry] Thất bại khi tải {err.ChapterName}, Trang {err.PageNumber}: {ex.Message}");
                    Dispatcher.Invoke(() =>
                    {
                        queueItem.AddError(err.ChapterName, err.PageNumber, ex.Message, err.ImageUrl, chapterUrl: err.ChapterUrl, pageName: err.PageName, attemptCount: err.AttemptCount);
                    });
                    RecordCheckError(queueItem.SourceDomain ?? "retry", queueItem.Name, err.ChapterName, err.PageNumber, ex.Message, err.ImageUrl, pageName: err.PageName);
                }

                int retryIndex = successfulRetries + failedRetries;
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    queueItem.CurrentProcess = BuildRetryProcessText(retryIndex, errorsToRetry.Count, successfulRetries, failedRetries, false);
                }));
            }

            foreach (var pair in chapterFoldersToFinalize)
            {
                try
                {
                    await FinalizeRetryChapterFolderAsync(pair.Key, pair.Value, _downloadCts?.Token ?? CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Log($"[Retry] Không thể dọn folder chapter '{pair.Key}': {ex.Message}");
                }
            }

            Dispatcher.Invoke(() => {
                if (wasDownloading)
                {
                    queueItem.Status = "Downloading";
                    queueItem.CurrentProcess = BuildRetryProcessText(errorsToRetry.Count, errorsToRetry.Count, successfulRetries, failedRetries, true);
                }
                else
                {
                    queueItem.Status = queueItem.HasAnyErrors() ? "Error" : "Completed";
                    queueItem.CurrentProcess = queueItem.HasAnyErrors() ? "Done with errors" : "Done";
                }
            });

            UpdateQueueErrorLabel();

            Log($"[Retry] Hoàn tất thử tải lại cho '{queueItem.Name}'. Thành công: {successfulRetries}/{errorsToRetry.Count}.");
            if (showMessageBox)
            {
                ShowLocalizedMessageBox(
                    $"Retry complete!\nSuccess: {successfulRetries}\nFailed: {failedRetries}",
                    $"Thử tải lại hoàn tất!\nThành công: {successfulRetries}\nThất bại: {failedRetries}",
                    "Retry Completed",
                    "Kết quả thử tải lại",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private static string BuildRetryProcessText(int current, int total, int ok, int fail, bool finished)
        {
            int safeCurrent = Math.Max(0, Math.Min(current, total));
            int safeTotal = Math.Max(0, total);
            string prefix = finished ? "Retrying errors" : "Retrying";
            return $"{prefix} {safeCurrent}/{safeTotal} errors ({ok} ok, {fail} fail)";
        }

        private async Task FinalizeRetryChapterFolderAsync(string unmergedPath, string mergedPath, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(unmergedPath) || string.IsNullOrWhiteSpace(mergedPath))
            {
                return;
            }

            if (!Directory.Exists(unmergedPath))
            {
                return;
            }

            await AutoMergeChapterFolderAsync(unmergedPath, mergedPath, token);
        }

        private async Task<string> ResolveTruyenqqRetryImageUrlAsync(GalleryItem queueItem, ErrorDetail err)
        {
            string chapterLink = FindTruyenqqChapterLinkForRetry(queueItem, err.ChapterName);
            if (string.IsNullOrWhiteSpace(chapterLink) || err.PageNumber <= 0)
            {
                return null;
            }

            string html = await FetchStringAsync(chapterLink, _downloadCts?.Token ?? CancellationToken.None);
            string safeHtml = GetSafeChapterHtml(html);
            int startIndex = -1;
            foreach (string marker in new[]
            {
                "class=\"story-see-content\"",
                "class=\"chapter_content\"",
                "class=\"chapter-content\"",
                "class=\"reading-detail\"",
                "id=\"chapter_content\""
            })
            {
                int markerIndex = safeHtml.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (markerIndex != -1)
                {
                    startIndex = markerIndex;
                    break;
                }
            }

            string contentArea = startIndex >= 0 ? safeHtml.Substring(startIndex) : safeHtml;
            var imageUrls = ExtractTruyenqqImageUrls(contentArea, chapterLink);
            int index = err.PageNumber - 1;
            if (index < 0 || index >= imageUrls.Count)
            {
                return null;
            }

            return imageUrls[index];
        }

        private string FindTruyenqqChapterLinkForRetry(GalleryItem queueItem, string chapterName)
        {
            if (queueItem == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(queueItem.Link) && Regex.IsMatch(queueItem.Link, @"(?:chap|chapter|chuong)", RegexOptions.IgnoreCase))
            {
                return queueItem.Link;
            }

            string processPath = GetExistingDownloadProcessFilePath(queueItem.DownloadPath, "truyenqq", queueItem);
            if (!File.Exists(processPath))
            {
                return null;
            }

            string normalizedChapter = NormalizeRetryChapterKey(chapterName);
            foreach (string line in File.ReadAllLines(processPath, System.Text.Encoding.UTF8))
            {
                string[] cells = line.Split('|');
                if (cells.Length < 5)
                {
                    continue;
                }

                string chapterCell = NormalizeRetryChapterKey(cells[3]);
                string link = cells[4].Trim();
                string linkCell = NormalizeRetryChapterKey(link);
                if (link.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                    (chapterCell.Contains(normalizedChapter) || linkCell.Contains(normalizedChapter)))
                {
                    return link;
                }
            }

            return null;
        }

        private string NormalizeRetryChapterKey(string value)
        {
            string normalized = (value ?? string.Empty).ToLowerInvariant();
            normalized = Regex.Replace(normalized, @"[^a-z0-9]+", " ").Trim();
            return normalized;
        }

        internal void RemoveErrorFromGlobalAndQueue(string bookName, string chapterName, string pageNumberStr, string errorMessage)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => RemoveErrorFromGlobalAndQueue(bookName, chapterName, pageNumberStr, errorMessage));
                return;
            }

            // 1. Xóa trong queueItem
            var queueItem = _scrapedItems.FirstOrDefault(x => string.Equals(x.Name, bookName, StringComparison.OrdinalIgnoreCase));
            if (queueItem != null)
            {
                int pNum = 0;
                int.TryParse(pageNumberStr, out pNum);
                var err = queueItem.Errors.FirstOrDefault(e => 
                    string.Equals(e.ChapterName, chapterName, StringComparison.OrdinalIgnoreCase) && 
                    e.PageNumber == pNum);
                if (err != null)
                {
                    queueItem.Errors.Remove(err);
                    queueItem.ErrorCount = queueItem.Errors.Count;
                }
                if (queueItem.Errors.Count == 0)
                {
                    queueItem.Status = "Completed";
                    queueItem.CurrentProcess = "Done";
                }

                _scrapedItems.Remove(queueItem);
            }

            // 2. Xóa trong _checkErrors chính
            int pageNum = 0;
            int.TryParse(pageNumberStr, out pageNum);
            var keyToRemove = _checkErrorIndex.Keys.FirstOrDefault(k => {
                if (_checkErrorIndex.TryGetValue(k, out var val))
                {
                    return string.Equals(val.BookName, bookName, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(val.ChapterName, chapterName, StringComparison.OrdinalIgnoreCase) &&
                           val.PageNumber == pageNum;
                }
                return false;
            });

            if (keyToRemove != null)
            {
                if (_checkErrorIndex.TryGetValue(keyToRemove, out var itemToRemove))
                {
                    _checkErrors.Remove(itemToRemove);
                }
                _checkErrorIndex.Remove(keyToRemove);
            }

            UpdateStats();
        }
    }
}
#pragma warning restore 4014

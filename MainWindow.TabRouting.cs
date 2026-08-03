using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private int _resultsImportingIndicatorDepth;
        private int _resultsMissingChapterScanningIndicatorDepth;

        private void ShowResultsImportingIndicator()
        {
            _resultsImportingIndicatorDepth++;
            if (txtResultsImportingStatus != null)
            {
                txtResultsImportingStatus.Visibility = Visibility.Visible;
            }
        }

        private void HideResultsImportingIndicator()
        {
            _resultsImportingIndicatorDepth = Math.Max(0, _resultsImportingIndicatorDepth - 1);
            if (_resultsImportingIndicatorDepth == 0 && txtResultsImportingStatus != null)
            {
                txtResultsImportingStatus.Visibility = Visibility.Collapsed;
            }
        }

        private bool IsResultsImportingActive()
        {
            return _resultsImportingIndicatorDepth > 0;
        }

        private void SetResultsImportingStatus(string message)
        {
            if (txtResultsImportingStatus == null)
            {
                return;
            }

            txtResultsImportingStatus.Text = message ?? string.Empty;
        }

        private void ShowTransientResultsImportingStatus(string message = null)
        {
            if (txtResultsImportingStatus == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(message))
            {
                txtResultsImportingStatus.Text = message;
            }

            txtResultsImportingStatus.Visibility = Visibility.Visible;
        }

        private void HideTransientResultsImportingStatus()
        {
            if (_resultsImportingIndicatorDepth > 0 || txtResultsImportingStatus == null)
            {
                return;
            }

            txtResultsImportingStatus.Visibility = Visibility.Collapsed;
        }

        private void UpdateResultsImportingProgress(int processed, int total, string currentTitle = null)
        {
            int safeProcessed = Math.Max(0, processed);
            int safeTotal = Math.Max(1, total);
            double pct = (double)safeProcessed / safeTotal * 100d;
            string suffix = string.IsNullOrWhiteSpace(currentTitle) ? string.Empty : " | " + currentTitle;
            SetResultsImportingStatus($"importing book {safeProcessed}/{total} ({pct:0}%)" + suffix);
        }

        private void UpdateResultsCrawlProgress(int processed, int total, string currentTitle = null)
        {
            int safeProcessed = Math.Max(0, processed);
            int safeTotal = Math.Max(1, total);
            double pct = (double)safeProcessed / safeTotal * 100d;
            string suffix = string.IsNullOrWhiteSpace(currentTitle) ? string.Empty : " | " + currentTitle;
            ShowTransientResultsImportingStatus($"getting link {safeProcessed}/{total} ({pct:0}%)" + suffix);
        }

        private void FlushScrapedResultsUi()
        {
            _scrapedItems?.FlushPendingNotifications();
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private async Task<string> GetImportDisplayNameAsync(int previousCount)
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                GalleryItem importedItem = _scrapedItems
                    .Skip(Math.Max(0, previousCount))
                    .LastOrDefault(item => item != null && !string.IsNullOrWhiteSpace(item.Name));
                if (importedItem != null)
                {
                    return importedItem.Name;
                }

                await Task.Delay(100);
                FlushScrapedResultsUi();
            }

            return string.Empty;
        }

        private string GuessImportDisplayName(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            try
            {
                var uri = new Uri(url.Trim(), UriKind.Absolute);
                string slug = GetImportQueryParam(uri, "slug");
                if (!string.IsNullOrWhiteSpace(slug))
                {
                    return HumanizeImportSlug(CleanImportSlugForDomain(uri, slug));
                }

                string[] segments = uri.AbsolutePath
                    .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(segment => !string.IsNullOrWhiteSpace(segment))
                    .ToArray();
                for (int i = segments.Length - 1; i >= 0; i--)
                {
                    string segment = Path.GetFileNameWithoutExtension(segments[i]).Trim();
                    if (string.IsNullOrWhiteSpace(segment) ||
                        segment.Equals("home", StringComparison.OrdinalIgnoreCase) ||
                        segment.Equals("detail", StringComparison.OrdinalIgnoreCase) ||
                        segment.Equals("category", StringComparison.OrdinalIgnoreCase) ||
                        segment.Equals("readchapter", StringComparison.OrdinalIgnoreCase) ||
                        segment.Equals("truyen-tranh", StringComparison.OrdinalIgnoreCase) ||
                        Regex.IsMatch(segment, @"^\d+$"))
                    {
                        continue;
                    }

                    return HumanizeImportSlug(CleanImportSlugForDomain(uri, segment));
                }
            }
            catch
            {
            }

            return url.Trim();
        }

        private string HumanizeImportSlug(string value)
        {
            string clean = WebUtility.HtmlDecode(value ?? string.Empty).Trim();
            clean = Regex.Replace(clean, @"[-_]+", " ").Trim();
            if (string.IsNullOrWhiteSpace(clean))
            {
                return string.Empty;
            }

            return FormatGalleryTitle(clean);
        }

        private string CleanImportSlugForDomain(Uri uri, string value)
        {
            string clean = (value ?? string.Empty).Trim();
            if (uri == null || string.IsNullOrWhiteSpace(clean))
            {
                return clean;
            }

            string host = uri.Host ?? string.Empty;
            if (host.IndexOf("truyenqq", StringComparison.OrdinalIgnoreCase) >= 0 &&
                uri.AbsolutePath.IndexOf("/truyen-tranh/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                clean = Regex.Replace(clean, @"-\d+$", string.Empty);
            }

            return clean;
        }

        private static string GetImportQueryParam(Uri uri, string key)
        {
            if (uri == null || string.IsNullOrWhiteSpace(uri.Query) || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            string prefix = key + "=";
            foreach (string pair in uri.Query.TrimStart('?').Split('&'))
            {
                if (pair.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(pair.Substring(prefix.Length));
                }
            }

            return string.Empty;
        }

        private bool ShouldShowImportSummary(bool showMessageBox, int total, int imported, int failed)
        {
            return false;
        }

        private void ShowImportSummaryIfNeeded(bool showMessageBox, int total, int imported, int failed, string label = "item")
        {
            if (!ShouldShowImportSummary(showMessageBox, total, imported, failed))
            {
                return;
            }

            ShowInfo($"Đã thêm {imported} {label}. Thất bại: {failed}", "Kết quả");
        }

        private void ShowResultsMissingChapterScanningIndicator()
        {
            _resultsMissingChapterScanningIndicatorDepth++;
            if (txtResultsMissingChapterScanningStatus != null)
            {
                txtResultsMissingChapterScanningStatus.Visibility = Visibility.Visible;
            }
        }

        private void HideResultsMissingChapterScanningIndicator()
        {
            _resultsMissingChapterScanningIndicatorDepth = Math.Max(0, _resultsMissingChapterScanningIndicatorDepth - 1);
            if (_resultsMissingChapterScanningIndicatorDepth == 0 && txtResultsMissingChapterScanningStatus != null)
            {
                txtResultsMissingChapterScanningStatus.Visibility = Visibility.Collapsed;
            }
        }

        private void SelectMangaSourceRoot()
        {
            if (tabLeftPanel != null && tabMangaSourceRootItem != null)
            {
                tabLeftPanel.SelectedItem = tabMangaSourceRootItem;
            }
        }

        private void SelectHentaiSourceRoot()
        {
            if (tabLeftPanel != null && tabHentaiSourceRootItem != null)
            {
                tabLeftPanel.SelectedItem = tabHentaiSourceRootItem;
            }
        }

        private void SelectNovelSourceRoot()
        {
            if (tabLeftPanel != null && tabLightNovelRootItem != null)
            {
                tabLeftPanel.SelectedItem = tabLightNovelRootItem;
            }
        }

        private void SelectDownloadMangaTab()
        {
            SelectAppSection(AppSection.Download);
            if (tabDownloadRoot != null && tabDownloadRoot.Items.Count > GetDownloadMangaTabIndex())
            {
                tabDownloadRoot.SelectedIndex = GetDownloadMangaTabIndex();
            }
        }

        private void SelectDownloadNovelTab()
        {
            SelectDownloadMangaTab();
        }

        public bool IsSupportedDomain(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            string lower = url.ToLowerInvariant();
            return lower.Contains("truyenqq") || lower.Contains("nettruyen") ||
                   lower.Contains("daomeoden") || lower.Contains("dilib.vn") || lower.Contains("thuviensach.vn") || lower.Contains("doctruyen.us") || lower.Contains("loppytoonn.com") || lower.Contains("haibabamanga") || lower.Contains("vi-hentai") || lower.Contains("vihentai") ||
                   lower.Contains("sayhentai") || lower.Contains("truyengg") || lower.Contains("hentaiforce") ||
                   lower.Contains("damconuong") ||
                   lower.Contains("mangadex.org") || lower.Contains("www.mangadex.org") ||
                   lower.Contains("nhentai") || lower.Contains("hentai2read") || lower.Contains("hentaiera") ||
                   lower.Contains("hako") || lower.Contains("docln.net") || lower.Contains("docln.sbs");
        }

        private async Task WaitAndScrapeAsync(Button fetchButton, RoutedEventHandler scrapeHandler)
        {
            var oldCursor = Cursor;
            try
            {
                Cursor = Cursors.Wait;
                if (lblStatus != null)
                {
                    lblStatus.Text = "⏳ Đang xử lý dữ liệu link... (Processing link...)";
                }

                await Task.Delay(150);
                int timeoutCount = 0;
                while (fetchButton != null && !fetchButton.IsEnabled && timeoutCount < 120)
                {
                    await Task.Delay(500);
                    timeoutCount++;
                }

                scrapeHandler?.Invoke(this, new RoutedEventArgs());
            }
            catch
            {
            }
            finally
            {
                Cursor = oldCursor;
            }
        }

        private string NormalizeBookUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return url;
            }

            url = url.Trim();
            string lower = url.ToLowerInvariant();
            if (lower.Contains("nhentai.net/g/") || lower.Contains("nhentai.xxx/g/"))
            {
                // Regex tìm kiếm pattern /g/{galleryId}/{pageNum}/ hoặc /g/{galleryId}/{pageNum}
                // ví dụ: https://nhentai.net/g/159844/1/
                var match = Regex.Match(url, @"^(https?://nhentai\.(?:net|xxx)/g/\d+)/(\d+)/?$", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return match.Groups[1].Value + "/";
                }
            }
            return url;
        }

        public async void RouteAndProcessInputLink(string url, bool allowUiJump = true)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            url = NormalizeBookUrl(url);
            string lowerUrl = url.ToLowerInvariant();
            if (allowUiJump)
            {
                SelectAppSection(AppSection.Download);
            }

            if (lowerUrl.Contains("hako.vn") || lowerUrl.Contains("hako.re") || lowerUrl.Contains("hako") || lowerUrl.Contains("docln.net") || lowerUrl.Contains("docln.sbs"))
            {
                if (allowUiJump && tabDownloadRoot != null && tabDownloadRoot.Items.Count > GetDownloadMangaTabIndex())
                {
                    tabDownloadRoot.SelectedIndex = GetDownloadMangaTabIndex();
                }

                if (allowUiJump)
                {
                    SelectNovelSourceRoot();
                }

                if (TryParseHakoBookUrl(url, out _, out _, out _) || TryParseHakoChapterUrl(url, out _, out _, out _, out _, out _))
                {
                    await ImportHakoDirectLinksAsync(new List<string> { url });
                    return;
                }

                if (txtHakoTagUrl != null)
                {
                    txtHakoTagUrl.Text = url;
                }

                BtnHakoFetchInfo_Click(this, new RoutedEventArgs());
                await WaitAndScrapeAsync(btnHakoFetchInfo, BtnHakoScrape_Click);
                return;
            }

            int mangaIndex = GetDownloadMangaTabIndex();
            if (allowUiJump && tabDownloadRoot != null && tabDownloadRoot.Items.Count > mangaIndex)
            {
                tabDownloadRoot.SelectedIndex = mangaIndex;
            }

            if (allowUiJump)
            {
                SelectMangaSourceRoot();
            }

            if (lowerUrl.Contains("mangadex.org") || lowerUrl.Contains("www.mangadex.org"))
            {
                if (allowUiJump)
                {
                    SelectMangaTabByHeader("mangadex.org");
                }
                if (txtMangadexTagUrl != null) txtMangadexTagUrl.Text = url;
                BtnMangadexAnalyze_Click(this, new RoutedEventArgs());
            }
            else if (lowerUrl.Contains("truyenqq"))
            {
                if (allowUiJump && tabManga != null) tabManga.SelectedIndex = 0;
                if (txtTruyenqqTagUrl != null) txtTruyenqqTagUrl.Text = url;
                BtnTruyenqqFetchInfo_Click(this, new RoutedEventArgs());
                await WaitAndScrapeAsync(btnTruyenqqFetchInfo, BtnTruyenqqScrape_Click);
            }
            else if (lowerUrl.Contains("nettruyen.tech"))
            {
                if (allowUiJump && tabManga != null) tabManga.SelectedIndex = 2;
                if (txtNettruyenTechTagUrl != null) txtNettruyenTechTagUrl.Text = url;
                BtnNettruyenTechFetchInfo_Click(this, new RoutedEventArgs());
                await WaitAndScrapeAsync(btnNettruyenTechFetchInfo, BtnNettruyenTechScrape_Click);
            }
            else if (lowerUrl.Contains("nettruyen"))
            {
                if (allowUiJump && tabManga != null) tabManga.SelectedIndex = 1;
                if (txtNettruyenTagUrl != null) txtNettruyenTagUrl.Text = url;
                BtnNettruyenFetchInfo_Click(this, new RoutedEventArgs());
                await WaitAndScrapeAsync(btnNettruyenFetchInfo, BtnNettruyenScrape_Click);
            }
            else if (lowerUrl.Contains("daomeoden"))
            {
                if (allowUiJump)
                {
                    SelectHentaiSourceRoot();
                    SelectHentaiTabByHeader("daomeoden");
                }
                if (txtDaomeodenTagUrl != null) txtDaomeodenTagUrl.Text = url;
                BtnDaomeodenFetchInfo_Click(this, new RoutedEventArgs());
                await WaitAndScrapeAsync(btnDaomeodenFetchInfo, BtnDaomeodenScrape_Click);
            }
            else if (lowerUrl.Contains("damconuong"))
            {
                if (allowUiJump)
                {
                    SelectHentaiSourceRoot();
                    SelectHentaiTabByHeader("damconuong");
                }
                if (txtDamconuongTagUrl != null) txtDamconuongTagUrl.Text = url;
                if (IsDamconuongCategoryUrl(url))
                {
                    BtnDamconuongFetchInfo_Click(this, new RoutedEventArgs());
                    await WaitAndScrapeAsync(btnDamconuongFetchInfo, BtnDamconuongScrape_Click);
                }
                else
                {
                    await ImportDamconuongDirectLinksAsync(new List<string> { url });
                }
            }
            else if (lowerUrl.Contains("dilib.vn") || lowerUrl.Contains("thuviensach.vn"))
            {
                if (allowUiJump && tabManga != null) tabManga.SelectedIndex = 3;
                if (txtDilibTagUrl != null) txtDilibTagUrl.Text = url;
                BtnDilibFetchInfo_Click(this, new RoutedEventArgs());
                await WaitAndScrapeAsync(btnDilibFetchInfo, BtnDilibScrape_Click);
            }
            else if (lowerUrl.Contains("doctruyen.us"))
            {
                if (allowUiJump && tabManga != null) tabManga.SelectedIndex = 4;
                if (txtDoctruyenTagUrl != null) txtDoctruyenTagUrl.Text = url;
                BtnDoctruyenAnalyze_Click(this, new RoutedEventArgs());
            }
            else if (lowerUrl.Contains("loppytoonn.com"))
            {
                if (allowUiJump)
                {
                    SelectMangaTabByHeader("loppytoonn.com");
                }
                if (txtLoppyTagUrl != null) txtLoppyTagUrl.Text = url;
                BtnLoppyAnalyze_Click(this, new RoutedEventArgs());
            }
            else if (lowerUrl.Contains("haibabamanga"))
            {
                if (allowUiJump)
                {
                    SelectMangaTabByHeader("haibabamanga.somee.com");
                }
                if (txtHaibabaTagUrl != null) txtHaibabaTagUrl.Text = url;
                BtnHaibabaAnalyze_Click(this, new RoutedEventArgs());
            }
            else if (lowerUrl.Contains("vi-hentai") || lowerUrl.Contains("vihentai"))
            {
                if (allowUiJump)
                {
                    SelectHentaiSourceRoot();
                    if (tabHentai != null) tabHentai.SelectedIndex = 0;
                }
                if (txtViHentaiTagUrl != null) txtViHentaiTagUrl.Text = url;
                BtnViHentaiFetchInfo_Click(this, new RoutedEventArgs());
                await WaitAndScrapeAsync(btnViHentaiFetchInfo, BtnViHentaiScrape_Click);
            }
            else if (lowerUrl.Contains("sayhentai") || lowerUrl.Contains("truyengg"))
            {
                if (allowUiJump)
                {
                    SelectHentaiSourceRoot();
                    SelectHentaiTabByHeader("sayhentai");
                }
                if (txtTruyenggvnTagUrl != null) txtTruyenggvnTagUrl.Text = url;
                BtnTruyenggvnFetchInfo_Click(this, new RoutedEventArgs());
                await WaitAndScrapeAsync(btnTruyenggvnFetchInfo, BtnTruyenggvnScrape_Click);
            }
            else if (lowerUrl.Contains("hentaiforce"))
            {
                if (allowUiJump)
                {
                    SelectHentaiSourceRoot();
                    SelectHentaiTabByHeader("hentaiforce");
                }
                if (txtTagUrl != null) txtTagUrl.Text = url;
                BtnFetchInfo_Click(this, new RoutedEventArgs());
                await WaitAndScrapeAsync(btnFetchInfo, BtnScrape_Click);
            }
            else if (lowerUrl.Contains("nhentai.net"))
            {
                SelectHentaiSourceRoot();
                SelectHentaiTabByHeader("nhentai.net");
                if (txtNhentaiNetTagUrl != null) txtNhentaiNetTagUrl.Text = url;
                BtnNhentaiNetFetchInfo_Click(this, new RoutedEventArgs());
                await WaitAndScrapeAsync(btnNhentaiNetFetchInfo, BtnNhentaiNetScrape_Click);
            }
            else if (lowerUrl.Contains("nhentai"))
            {
                SelectHentaiSourceRoot();
                SelectHentaiTabByHeader("nhentai");
                if (txtNhentaiTagUrl != null) txtNhentaiTagUrl.Text = url;
                BtnNhentaiFetchInfo_Click(this, new RoutedEventArgs());
                await WaitAndScrapeAsync(btnNhentaiFetchInfo, BtnNhentaiScrape_Click);
            }
            else if (lowerUrl.Contains("hentai2read"))
            {
                SelectHentaiSourceRoot();
                SelectHentaiTabByHeader("hentai2read");
                if (txtHentai2readTagUrl != null) txtHentai2readTagUrl.Text = url;
                BtnHentai2readFetchInfo_Click(this, new RoutedEventArgs());
                await WaitAndScrapeAsync(btnHentai2readFetchInfo, BtnHentai2readScrape_Click);
            }
            else if (lowerUrl.Contains("hentaiera"))
            {
                SelectHentaiSourceRoot();
                SelectHentaiTabByHeader("hentaiera");
                if (txtHentaieraTagUrl != null) txtHentaieraTagUrl.Text = url;
                BtnHentaieraFetchInfo_Click(this, new RoutedEventArgs());
                await WaitAndScrapeAsync(btnHentaieraFetchInfo, BtnHentaieraScrape_Click);
            }
        }

        public async Task AppendSupportedInputLinks(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var links = text
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => NormalizeBookUrl(line.Trim()))
                .Where(line => !string.IsNullOrWhiteSpace(line))
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
                progressBar.Value = 0;
                progressBar.IsIndeterminate = false;
                var existingLinks = new HashSet<string>(
                    _scrapedItems
                        .Select(item => item?.Link)
                        .Where(link => !string.IsNullOrWhiteSpace(link)),
                    StringComparer.OrdinalIgnoreCase);
                int imported = 0;
                int failed = 0;
                string currentDisplayTitle = string.Empty;

                for (int i = 0; i < links.Count; i++)
                {
                    string link = links[i];
                    UpdateResultsImportingProgress(i, links.Count, currentDisplayTitle);
                    int beforeCount = _scrapedItems.Count;

                    if (TryAppendExistingSupportedLinkDuplicate(link))
                    {
                        imported += Math.Max(0, _scrapedItems.Count - beforeCount);
                        ClearAppendCompletedStatus();
                        FlushScrapedResultsUi();
                        currentDisplayTitle = await GetImportDisplayNameAsync(beforeCount);
                        UpdateResultsImportingProgress(i + 1, links.Count, currentDisplayTitle);
                        progressBar.Value = (double)(i + 1) / Math.Max(1, links.Count) * 100d;
                        continue;
                    }

                    bool allowUiJump = link.IndexOf("loppytoonn.com", StringComparison.OrdinalIgnoreCase) >= 0 || link.IndexOf("haibabamanga", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool handled = await TryAppendSupportedDirectLinkAsync(link, showMessageBox: false, allowUiJump: allowUiJump);
                    if (handled)
                    {
                        imported += Math.Max(0, _scrapedItems.Count - beforeCount);
                        MarkNewlyImportedItemsChecked(existingLinks);
                        ClearAppendCompletedStatus();
                        FlushScrapedResultsUi();
                        currentDisplayTitle = await GetImportDisplayNameAsync(beforeCount);
                        UpdateResultsImportingProgress(i + 1, links.Count, currentDisplayTitle);
                    }
                    else
                    {
                        failed++;
                        UpdateResultsImportingProgress(i + 1, links.Count, currentDisplayTitle);
                    }

                    progressBar.Value = (double)(i + 1) / Math.Max(1, links.Count) * 100d;
                }

                RecalculateDuplicates();
                if (lblLinkCount != null)
                {
                    lblLinkCount.Text = _scrapedItems.Count.ToString();
                }

                SetResultsImportingStatus($"import completed. added {imported} item. failed: {failed}");
                ShowImportSummaryIfNeeded(true, links.Count, imported, failed);
            }
            finally
            {
                progressBar.Value = 100;
                HideResultsImportingIndicator();
            }
        }

        private bool TryAppendExistingSupportedLinkDuplicate(string link)
        {
            if (string.IsNullOrWhiteSpace(link))
            {
                return false;
            }

            GalleryItem existing = _scrapedItems.LastOrDefault(item =>
                item != null &&
                !string.IsNullOrWhiteSpace(item.Link) &&
                item.Link.Equals(link, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                return false;
            }

            _scrapedItems.Add(CloneGalleryItemForDuplicatePaste(existing));
            return true;
        }

        private GalleryItem CloneGalleryItemForDuplicatePaste(GalleryItem source)
        {
            if (source == null)
            {
                return null;
            }

            var clone = new GalleryItem
            {
                Link = source.Link,
                Name = source.Name,
                LinkCount = source.LinkCount,
                SourceDomain = source.SourceDomain,
                HasNoChapters = source.HasNoChapters,
                IsParallelSplitTask = source.IsParallelSplitTask,
                NhentaiTotalPagesHint = source.NhentaiTotalPagesHint,
                ChapterSelectionText = source.ChapterSelectionText,
                MissingChapterLatestChapterText = source.MissingChapterLatestChapterText,
                ConnectionCount = source.ConnectionCount,
                MultiDownloadCount = source.MultiDownloadCount,
                IsChecked = true,
                OriginalIndex = _scrapedItems.Count
            };

            System.Diagnostics.Debug.Assert(
                string.Equals(clone.Link, source.Link, StringComparison.OrdinalIgnoreCase),
                "Duplicate paste clone must preserve source link.");

            return clone;
        }

        private void MarkNewlyImportedItemsChecked(ISet<string> existingLinks)
        {
            if (existingLinks == null)
            {
                return;
            }

            foreach (GalleryItem item in _scrapedItems)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Link) || existingLinks.Contains(item.Link))
                {
                    continue;
                }

                item.IsChecked = true;
                existingLinks.Add(item.Link);
            }
        }

        private void ClearAppendCompletedStatus()
        {
            if (lblStatus == null)
            {
                return;
            }

            string status = lblStatus.Text ?? string.Empty;
            if (status.StartsWith("Import completed", StringComparison.OrdinalIgnoreCase) ||
                status.IndexOf("Imported ", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                lblStatus.Text = "Ready.";
            }
        }

        private async Task<bool> TryAppendSupportedDirectLinkAsync(string url, bool showMessageBox = true, bool allowUiJump = true)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            string lowerUrl = url.Trim().ToLowerInvariant();
            if (allowUiJump)
            {
                SelectAppSection(AppSection.Download);
            }

            if (lowerUrl.Contains("hako.vn") || lowerUrl.Contains("hako.re") || lowerUrl.Contains("hako") || lowerUrl.Contains("docln.net") || lowerUrl.Contains("docln.sbs"))
            {
                if (allowUiJump && tabDownloadRoot != null && tabDownloadRoot.Items.Count > GetDownloadMangaTabIndex()) tabDownloadRoot.SelectedIndex = GetDownloadMangaTabIndex();
                if (allowUiJump) SelectNovelSourceRoot();

                if (TryParseHakoBookUrl(url, out _, out _, out _) || TryParseHakoChapterUrl(url, out _, out _, out _, out _, out _))
                {
                    await ImportHakoDirectLinksAsync(new List<string> { url });
                    return true;
                }

                if (txtHakoTagUrl != null)
                {
                    txtHakoTagUrl.Text = url;
                }

                BtnHakoFetchInfo_Click(this, new RoutedEventArgs());
                await WaitAndScrapeAsync(btnHakoFetchInfo, BtnHakoScrape_Click);
                return true;
            }

            int mangaIndex = GetDownloadMangaTabIndex();
            if (allowUiJump && tabDownloadRoot != null && tabDownloadRoot.Items.Count > mangaIndex) tabDownloadRoot.SelectedIndex = mangaIndex;
            if (allowUiJump) SelectMangaSourceRoot();

            if (lowerUrl.Contains("mangadex.org") || lowerUrl.Contains("www.mangadex.org"))
            {
                if (allowUiJump)
                {
                    SelectMangaTabByHeader("mangadex.org");
                }
                await ImportMangadexDirectLinksAsync(new List<string> { url }, showMessageBox: showMessageBox);
                return true;
            }

            if (lowerUrl.Contains("truyenqq"))
            {
                if (allowUiJump && tabManga != null) tabManga.SelectedIndex = 0;
                await ImportTruyenqqDirectLinksAsync(new List<string> { url }, showMessageBox);
                return true;
            }

            if (lowerUrl.Contains("nettruyen.tech"))
            {
                if (allowUiJump && tabManga != null) tabManga.SelectedIndex = 2;
                await ImportNettruyenTechDirectLinksAsync(new List<string> { url }, showMessageBox);
                return true;
            }

            if (lowerUrl.Contains("nettruyen"))
            {
                if (allowUiJump && tabManga != null) tabManga.SelectedIndex = 1;
                await ImportNettruyenDirectLinksAsync(new List<string> { url }, showMessageBox);
                return true;
            }

            if (lowerUrl.Contains("daomeoden"))
            {
                if (allowUiJump)
                {
                    SelectHentaiSourceRoot();
                    SelectHentaiTabByHeader("daomeoden");
                }
                await ImportDaomeodenDirectLinksAsync(new List<string> { url });
                return true;
            }

            if (lowerUrl.Contains("damconuong"))
            {
                if (allowUiJump)
                {
                    SelectHentaiSourceRoot();
                    SelectHentaiTabByHeader("damconuong");
                }
                await ImportDamconuongDirectLinksAsync(new List<string> { url }, showMessageBox);
                return true;
            }

            if (lowerUrl.Contains("dilib.vn") || lowerUrl.Contains("thuviensach.vn"))
            {
                if (allowUiJump && tabManga != null) tabManga.SelectedIndex = 3;
                await ImportDilibDirectLinksAsync(new List<string> { url }, clearExisting: false, showMessageBox: showMessageBox);
                return true;
            }

            if (lowerUrl.Contains("doctruyen.us"))
            {
                if (allowUiJump && tabManga != null) tabManga.SelectedIndex = 4;
                await ImportDoctruyenDirectLinksAsync(new List<string> { url }, showMessageBox);
                return true;
            }

            if (lowerUrl.Contains("loppytoonn.com"))
            {
                if (allowUiJump)
                {
                    SelectMangaTabByHeader("loppytoonn.com");
                }
                await ImportLoppyDirectLinksAsync(new List<string> { url }, showMessageBox);
                return true;
            }

            if (lowerUrl.Contains("haibabamanga"))
            {
                if (allowUiJump)
                {
                    SelectMangaTabByHeader("haibabamanga.somee.com");
                }
                await ImportHaibabaDirectLinksAsync(new List<string> { url }, showMessageBox);
                return true;
            }

            if (lowerUrl.Contains("vi-hentai") || lowerUrl.Contains("vihentai"))
            {
                if (allowUiJump)
                {
                    SelectHentaiSourceRoot();
                    if (tabHentai != null) tabHentai.SelectedIndex = 0;
                }
                await ImportViHentaiDirectLinksAsync(new List<string> { url }, showMessageBox);
                return true;
            }

            if (lowerUrl.Contains("sayhentai") || lowerUrl.Contains("truyengg"))
            {
                if (allowUiJump)
                {
                    SelectHentaiSourceRoot();
                    SelectHentaiTabByHeader("sayhentai");
                }
                await ImportTruyenggvnDirectLinksAsync(new List<string> { url });
                return true;
            }

            if (lowerUrl.Contains("hentaiforce"))
            {
                if (allowUiJump)
                {
                    SelectHentaiSourceRoot();
                    SelectHentaiTabByHeader("hentaiforce");
                }
                await ImportDirectLinksAsync(new List<string> { url }, showMessageBox);
                return true;
            }

            if (lowerUrl.Contains("nhentai.net"))
            {
                if (IsNhentaiNetTagOrListUrl(url))
                {
                    if (allowUiJump)
                    {
                        SelectHentaiSourceRoot();
                        SelectHentaiTabByHeader("nhentai.net");
                    }
                    if (txtNhentaiNetTagUrl != null) txtNhentaiNetTagUrl.Text = url;
                    BtnNhentaiNetFetchInfo_Click(this, new RoutedEventArgs());
                    await WaitAndScrapeAsync(btnNhentaiNetFetchInfo, BtnNhentaiNetScrape_Click);
                    return true;
                }

                if (allowUiJump)
                {
                    SelectHentaiSourceRoot();
                    SelectHentaiTabByHeader("nhentai.net");
                }
                await ImportNhentaiNetDirectLinksAsync(new List<string> { url }, showMessageBox);
                return true;
            }

            if (lowerUrl.Contains("nhentai"))
            {
                if (allowUiJump)
                {
                    SelectHentaiSourceRoot();
                    SelectHentaiTabByHeader("nhentai");
                }
                await ImportNhentaiDirectLinksAsync(new List<string> { url }, showMessageBox);
                return true;
            }

            if (lowerUrl.Contains("hentai2read"))
            {
                if (allowUiJump)
                {
                    SelectHentaiSourceRoot();
                    SelectHentaiTabByHeader("hentai2read");
                }
                await ImportHentai2readDirectLinksAsync(new List<string> { url });
                return true;
            }

            if (lowerUrl.Contains("hentaiera"))
            {
                if (allowUiJump)
                {
                    SelectHentaiSourceRoot();
                    SelectHentaiTabByHeader("hentaiera");
                }
                await ImportHentaieraDirectLinksAsync(new List<string> { url }, showMessageBox);
                return true;
            }

            return false;
        }

        private void SelectMangaTabByHeader(string headerKeyword)
        {
            if (tabManga == null)
            {
                return;
            }

            for (int i = 0; i < tabManga.Items.Count; i++)
            {
                if (tabManga.Items[i] is TabItem tabItem &&
                    tabItem.Header?.ToString()?.ToLowerInvariant().Contains(headerKeyword) == true)
                {
                    tabManga.SelectedIndex = i;
                    return;
                }
            }
        }

        private void SelectHentaiTabByHeader(string headerKeyword)
        {
            if (tabHentai == null)
            {
                return;
            }

            for (int i = 0; i < tabHentai.Items.Count; i++)
            {
                if (tabHentai.Items[i] is TabItem tabItem &&
                    tabItem.Header?.ToString()?.ToLowerInvariant().Contains(headerKeyword) == true)
                {
                    tabHentai.SelectedIndex = i;
                    return;
                }
            }
        }

        private bool IsNhentaiNetTagOrListUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            string lower = url.Trim().ToLowerInvariant();
            
            // Nếu có /g/ và theo sau là ID số cụ thể thì đó là Link Book chứ không phải Link List
            if (Regex.IsMatch(lower, @"nhentai\.net/g/\d+"))
            {
                return false;
            }

            return lower.Contains("/tag/") ||
                   lower.Contains("/artist/") ||
                   lower.Contains("/parody/") ||
                   lower.Contains("/group/") ||
                   lower.Contains("/character/") ||
                   lower.Contains("/search/") ||
                   lower.Contains("?q=");
        }
    }
}

#pragma warning disable 4014
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private const string HaibabaBaseUrl = "http://haibabamanga.somee.com";
        private const string HaibabaSiteFolder = "haibabamanga.somee.com";
        private static readonly Regex HaibabaChapterAnchorRegex = new Regex(
            @"<a\b(?=[^>]*\bclass=[""'][^""']*\bchapter-item\b[^""']*[""'])(?=[^>]*\bhref=[""'](?<href>[^""']*?/Home/ReadChapter\?[^""']+)[""'])[^>]*>(?<inner>[\s\S]*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private void HaibabaLog(string message)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Log("[haibabamanga.somee.com] " + message);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private bool IsHaibabaUrl(string url)
        {
            return TryParseHaibabaUri(url, out _);
        }

        private bool IsHaibabaCategoryUrl(string url)
        {
            return TryParseHaibabaUri(url, out Uri uri) &&
                   IsHaibabaListingPath(uri.AbsolutePath);
        }

        private bool IsHaibabaBookUrl(string url)
        {
            return TryParseHaibabaUri(url, out Uri uri) &&
                   uri.AbsolutePath.Equals("/Home/Detail", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsHaibabaChapterUrl(string url)
        {
            return TryParseHaibabaUri(url, out Uri uri) &&
                   uri.AbsolutePath.Equals("/Home/ReadChapter", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHaibabaListingPath(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return false;
            }

            switch (absolutePath.Trim())
            {
                case "/Home/Category":
                case "/Home/HotManga":
                case "/Home/DoneManga":
                case "/Home/ReleasingManga":
                case "/Home/CommingManga":
                    return true;
                default:
                    return false;
            }
        }

        private bool TryParseHaibabaUri(string url, out Uri uri)
        {
            uri = null;
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            string decoded = WebUtility.HtmlDecode(url).Trim();
            string normalized = decoded;
            if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "http://haibabamanga.somee.com/" + normalized.TrimStart('/');
            }

            if (!Uri.TryCreate(normalized, UriKind.Absolute, out uri))
            {
                return false;
            }

            return uri.Host.Equals("haibabamanga.somee.com", StringComparison.OrdinalIgnoreCase) ||
                   uri.Host.EndsWith(".haibabamanga.somee.com", StringComparison.OrdinalIgnoreCase);
        }

        private static void RunHaibabaChapterExtractionSelfCheck()
        {
            const string sampleHtml = @"<a href=""/Home/ReadChapter?slug=test&amp;chapterId=1"" class=""btn btn-primary btn-read-action"">Đọc ngay</a>
<ul class=""chapter-list"">
  <li><a href=""/Home/ReadChapter?slug=test&amp;chapterId=1"" class=""chapter-item""><span>Chương 22</span></a></li>
</ul>";
            const string samplePreviewHtml = @"<div class=""manga-cover-container mb-3""><img src=""https://example.com/uploads/comics/test-thumb.png"" class=""manga-cover"" alt=""Test""></div>";

            MatchCollection matches = HaibabaChapterAnchorRegex.Matches(sampleHtml);
            System.Diagnostics.Debug.Assert(matches.Count == 1, "Haibaba chapter regex should ignore non-chapter-item links.");
            System.Diagnostics.Debug.Assert(matches[0].Groups["inner"].Value.IndexOf("22", StringComparison.OrdinalIgnoreCase) >= 0, "Haibaba chapter regex should keep real chapter label.");
            System.Diagnostics.Debug.Assert(
                string.Equals(
                    ExtractHaibabaPreviewUrlFromHtml(samplePreviewHtml, HaibabaBaseUrl + "/Home/Detail?slug=test"),
                    "https://example.com/uploads/comics/test-thumb.png",
                    StringComparison.OrdinalIgnoreCase),
                "Haibaba preview extraction should support manga-cover-container images.");
        }

        private string NormalizeHaibabaUrl(string url)
        {
            url = WebUtility.HtmlDecode(url);
            if (!TryParseHaibabaUri(url, out Uri uri))
            {
                throw new ArgumentException("URL haibabamanga.somee.com không hợp lệ.");
            }

            string slug = GetQueryParam(url, "slug");
            string page = GetQueryParam(url, "page");
            string chapterId = GetQueryParam(url, "chapterId");

            var queryParts = new List<string>();
            if (!string.IsNullOrEmpty(slug)) queryParts.Add("slug=" + slug);
            if (!string.IsNullOrEmpty(chapterId)) queryParts.Add("chapterId=" + chapterId);
            if (!string.IsNullOrEmpty(page)) queryParts.Add("page=" + page);

            var builder = new UriBuilder(uri)
            {
                Fragment = string.Empty,
                Query = queryParts.Count > 0 ? string.Join("&", queryParts) : string.Empty
            };

            string path = builder.Path.TrimEnd('/');
            builder.Path = string.IsNullOrWhiteSpace(path) ? "/" : path;
            return builder.Uri.AbsoluteUri.TrimEnd('/');
        }

        private string CleanHaibabaTitle(string value)
        {
            string clean = WebUtility.HtmlDecode(Regex.Replace(value ?? string.Empty, @"<[^>]+>", " ")).Trim();
            clean = Regex.Replace(clean, @"\s+", " ").Trim();
            clean = Regex.Replace(clean, @"\s*-\s*Hải Bá Bá\s*-\s*MangakaApp\s*$", string.Empty, RegexOptions.IgnoreCase).Trim();
            return FormatGalleryTitle(clean);
        }

        private string HumanizeHaibabaSlug(string slug)
        {
            string clean = Regex.Replace((slug ?? string.Empty).Trim('/'), @"[-_]+", " ").Trim();
            return string.IsNullOrWhiteSpace(clean) ? "Haibaba" : FormatGalleryTitle(clean);
        }

        private void TxtHaibabaTagUrl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText()?.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        txtHaibabaTagUrl.Text = text;
                        txtHaibabaTagUrl.CaretIndex = txtHaibabaTagUrl.Text.Length;
                        e.Handled = true;
                    }
                }
                return;
            }

            if (e.Key == Key.Enter)
            {
                BtnHaibabaAnalyze_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void TxtHaibabaTotalPages_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (txtHaibabaPageTo != null && txtHaibabaTotalPages != null)
            {
                txtHaibabaPageTo.Text = txtHaibabaTotalPages.Text;
            }
        }

        private async void BtnHaibabaAnalyze_Click(object sender, RoutedEventArgs e)
        {
            await AnalyzeHaibabaUrlAsync(txtHaibabaTagUrl?.Text);
        }

        private async void BtnHaibabaScrape_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                btnHaibabaScrape.Content = "CANCELLING...";
                btnHaibabaScrape.IsEnabled = false;
                btnHaibabaCrawlMore.IsEnabled = false;
                return;
            }
            if (!ConfirmScrapeDuringDownloadIfNeeded(true)) return;
            SelectDownloadMangaTab();
            await ScrapeHaibabaAsync(clearExisting: true);
        }

        private async void BtnHaibabaCrawlMore_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                btnHaibabaCrawlMore.Content = "CANCELLING...";
                btnHaibabaCrawlMore.IsEnabled = false;
                btnHaibabaScrape.IsEnabled = false;
                return;
            }

            SelectDownloadMangaTab();
            await ScrapeHaibabaAsync(clearExisting: false);
        }

        private async Task AnalyzeHaibabaUrlAsync(string rawUrl)
        {
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                ShowWarning("Vui lòng nhập URL haibabamanga.somee.com hợp lệ.", "Thông báo");
                return;
            }

            btnHaibabaAnalyze.IsEnabled = false;
            progressBar.IsIndeterminate = true;

            try
            {
                string normalized = NormalizeHaibabaUrl(rawUrl);
                txtHaibabaTagUrl.Text = normalized;
                CancellationToken token = _downloadCts?.Token ?? CancellationToken.None;
                if (IsHaibabaCategoryUrl(normalized))
                {
                    string html = await FetchStringAsync(normalized, token);
                    int totalPages = ExtractHaibabaCategoryPageCount(html);
                    txtHaibabaTotalPages.Text = Math.Max(1, totalPages).ToString(CultureInfo.InvariantCulture);
                    txtHaibabaPageFrom.Text = "1";
                    txtHaibabaPageTo.Text = Math.Max(1, totalPages).ToString(CultureInfo.InvariantCulture);
                    lblStatus.Text = $"Haibaba listing: {totalPages} pages.";
                }
                else
                {
                    txtHaibabaTotalPages.Text = "1";
                    txtHaibabaPageFrom.Text = "1";
                    txtHaibabaPageTo.Text = "1";
                    lblStatus.Text = IsHaibabaBookUrl(normalized)
                        ? "Haibaba book ready."
                        : IsHaibabaChapterUrl(normalized)
                            ? "Haibaba chapter ready."
                            : "Đang phân tích haibabamanga.somee.com...";
                }
            }
            catch (Exception ex)
            {
                HaibabaLog("Lỗi phân tích: " + ex.Message);
                ShowWarning(ex.Message, "Thông báo");
                lblStatus.Text = "Analysis failed.";
                txtHaibabaTotalPages.Text = "1";
                txtHaibabaPageFrom.Text = "1";
                txtHaibabaPageTo.Text = "1";
            }
            finally
            {
                progressBar.IsIndeterminate = false;
                btnHaibabaAnalyze.IsEnabled = true;
            }
        }

        private void BtnHaibabaPasteDirect_Click(object sender, RoutedEventArgs e)
        {
            var window = new DirectDownloadWindow(
                customTitle: "PASTE HAIBABAMANGA LINKS",
                customDescription: "Paste haibabamanga.somee.com category, book, or chapter links below. App sẽ tự nhận diện đúng kiểu URL.",
                customExample:
                    "Example:\nhttp://haibabamanga.somee.com/Home/Category?slug=action\nhttp://haibabamanga.somee.com/Home/Detail?slug=dieu-thu-cuong-y\nhttp://haibabamanga.somee.com/Home/ReadChapter?slug=dieu-thu-cuong-y&chapterId=6586d47fe120ddf2198e9433")
            {
                Owner = this
            };

            window.OnImport = async links => await ImportHaibabaDirectLinksAsync(links);
            window.ShowDialog();
        }

        private async Task ScrapeHaibabaAsync(bool clearExisting)
        {
            string rawUrl = txtHaibabaTagUrl?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                ShowWarning("Vui lòng nhập URL haibabamanga.somee.com hợp lệ.", "Thông báo");
                return;
            }

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            btnHaibabaScrape.Content = "STOP CRAWLER";
            btnHaibabaCrawlMore.Content = "STOP CRAWLER";
            bool keepControlsEnabled = IsResultsImportingActive();
            if (!keepControlsEnabled)
            {
                btnHaibabaAnalyze.IsEnabled = false;
            }
            progressBar.Value = 0;
            progressBar.IsIndeterminate = false;

            if (clearExisting)
            {
                _scrapedItems.Clear();
                lblLinkCount.Text = "0";
            }

            try
            {
                ShowTransientResultsImportingStatus("getting link...");
                await ImportHaibabaDirectLinksAsync(new List<string> { rawUrl }, clearExisting: false, showMessageBox: true, token: token);
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = "Crawling cancelled.";
            }
            catch (Exception ex)
            {
                HaibabaLog("Lỗi khi crawl: " + ex.Message);
                lblStatus.Text = "Crawling failed.";
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                btnHaibabaScrape.Content = "GET LINK";
                btnHaibabaCrawlMore.Content = "GET MORE";
                btnHaibabaScrape.IsEnabled = true;
                btnHaibabaCrawlMore.IsEnabled = true;
                btnHaibabaAnalyze.IsEnabled = true;
                HideTransientResultsImportingStatus();
            }
        }

        private async Task<int> AddHaibabaImportedItemsAsync(IEnumerable<GalleryItem> items, HashSet<string> existingLinks, string statusText = null)
        {
            const int batchSize = 40;
            List<GalleryItem> pendingItems = (items ?? Enumerable.Empty<GalleryItem>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Link))
                .Where(item => existingLinks == null || existingLinks.Add(item.Link))
                .ToList();

            int imported = 0;
            for (int i = 0; i < pendingItems.Count; i += batchSize)
            {
                List<GalleryItem> batch = pendingItems.Skip(i).Take(batchSize).ToList();
                await Dispatcher.InvokeAsync(() =>
                {
                    foreach (GalleryItem item in batch)
                    {
                        item.OriginalIndex = _scrapedItems.Count;
                        _scrapedItems.Add(item);
                        imported++;
                    }

                    if (lblLinkCount != null)
                    {
                        lblLinkCount.Text = _scrapedItems.Count.ToString();
                    }

                    if (!string.IsNullOrWhiteSpace(statusText))
                    {
                        lblStatus.Text = statusText + $" | total: {_scrapedItems.Count}";
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);

                if (i + batchSize < pendingItems.Count)
                {
                    await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                }
            }

            return imported;
        }

        private async Task ImportHaibabaDirectLinksAsync(IReadOnlyList<string> links, bool clearExisting = false, bool showMessageBox = true, CancellationToken? token = null)
        {
            if (links == null || links.Count == 0)
            {
                return;
            }

            CancellationToken effectiveToken = token ?? _downloadCts?.Token ?? CancellationToken.None;

            if (clearExisting)
            {
                _scrapedItems.Clear();
                lblLinkCount.Text = "0";
            }

            bool keepControlsEnabled = IsResultsImportingActive();
            if (!keepControlsEnabled)
            {
                btnHaibabaAnalyze.IsEnabled = false;
            }
            progressBar.Value = 0;
            progressBar.IsIndeterminate = false;

            int imported = 0;
            int failed = 0;
            int total = links.Count;
            int processed = 0;
            var existingLinks = new HashSet<string>(_scrapedItems
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Link))
                .Select(item => item.Link), StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (string rawLink in links)
                {
                    effectiveToken.ThrowIfCancellationRequested();
                    string normalized;
                    try
                    {
                        normalized = NormalizeHaibabaUrl(rawLink);
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        processed++;
                        if (!keepControlsEnabled)
                        {
                            progressBar.Value = total == 0 ? 0 : (double)processed / total * 100;
                        }
                        HaibabaLog("Bỏ qua link lỗi: " + ex.Message);
                        continue;
                    }

                    txtHaibabaTagUrl.Text = normalized;
                    lblStatus.Text = "Đang xử lý " + normalized;

                    try
                    {
                        bool isCategoryUrl = IsHaibabaCategoryUrl(normalized);
                        int pageFrom = isCategoryUrl ? ParseHaibabaPageBox(txtHaibabaPageFrom, 1) : 1;
                        int pageTo = isCategoryUrl ? ParseHaibabaPageBox(txtHaibabaPageTo, ParseHaibabaPageBox(txtHaibabaTotalPages, 1)) : 1;
                        List<GalleryItem> items = await CreateHaibabaItemsFromUrlAsync(
                            normalized,
                            effectiveToken,
                            pageFrom,
                            pageTo,
                            async (pageItems, page, endPage) =>
                            {
                                if (!keepControlsEnabled)
                                {
                                    double pageProgress = endPage <= 0 ? 0 : (double)page / endPage * 100d;
                                    progressBar.Value = pageProgress;
                                    lblStatus.Text = $"Đang lấy link trang {page}/{endPage} ({pageProgress:0}%)";
                                }

                                UpdateResultsCrawlProgress(page, endPage, GuessImportDisplayName(normalized));

                                if (pageItems == null || pageItems.Count == 0)
                                {
                                    return;
                                }

                                imported += await AddHaibabaImportedItemsAsync(
                                    pageItems,
                                    existingLinks,
                                    $"Haibaba page {page}/{endPage}: +{pageItems.Count} item");
                            });
                        if (!isCategoryUrl)
                        {
                            imported += await AddHaibabaImportedItemsAsync(items, existingLinks);
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        HaibabaLog("Import lỗi với '" + normalized + "': " + ex.Message);
                    }

                    processed++;
                    if (!keepControlsEnabled)
                    {
                        progressBar.Value = total == 0 ? 0 : (double)processed / total * 100;
                    }
                }

                RecalculateDuplicates();
                if (lblLinkCount != null)
                {
                    lblLinkCount.Text = _scrapedItems.Count.ToString();
                }

                ShowImportSummaryIfNeeded(showMessageBox, total, imported, failed);
            }
            finally
            {
                if (!keepControlsEnabled)
                {
                    btnHaibabaAnalyze.IsEnabled = true;
                }
                if (!keepControlsEnabled)
                {
                    progressBar.Value = 100;
                }
            }
        }

        private int ParseHaibabaPageBox(TextBox box, int fallback)
        {
            if (box == null) return fallback;
            string txt = box.Text?.Trim();
            if (int.TryParse(txt, out int val))
            {
                return Math.Max(1, val);
            }
            return Math.Max(1, fallback);
        }

        private async Task<List<GalleryItem>> CreateHaibabaItemsFromUrlAsync(string url, CancellationToken token, int? pageFrom = null, int? pageTo = null, Func<List<GalleryItem>, int, int, Task> onCategoryPageReady = null)
        {
            string normalized = NormalizeHaibabaUrl(url);
            string html = await FetchStringAsync(normalized, token);

            if (IsHaibabaCategoryUrl(normalized))
            {
                return await ExtractHaibabaCategoryItemsAsync(html, normalized, token, pageFrom ?? 1, pageTo ?? int.MaxValue, onCategoryPageReady);
            }

            if (IsHaibabaBookUrl(normalized))
            {
                string title = ExtractHaibabaBookTitle(html, normalized);
                List<string> chapterLinks = ExtractHaibabaChapterLinks(html, normalized);
                string previewUrl = ExtractHaibabaPreviewUrlFromHtml(html, normalized);
                return new List<GalleryItem>
                {
                    new GalleryItem
                    {
                        Link = normalized,
                        Name = title,
                        LinkCount = chapterLinks.Count > 0 ? chapterLinks.Count + " chapters" : string.Empty,
                        HoverPreviewThumbnailUrl = previewUrl,
                        SourceDomain = HaibabaSiteFolder,
                        IsChecked = true
                    }
                };
            }

            if (IsHaibabaChapterUrl(normalized))
            {
                string bookTitle = ExtractHaibabaBookTitleFromChapterHtml(html, normalized);
                string chapterTitle = ExtractHaibabaChapterTitle(html, normalized);
                string previewUrl = ExtractHaibabaPreviewUrlFromHtml(html, normalized);
                return new List<GalleryItem>
                {
                    new GalleryItem
                    {
                        Link = normalized,
                        Name = string.IsNullOrWhiteSpace(chapterTitle) ? bookTitle : $"{bookTitle} - {chapterTitle}",
                        HoverPreviewThumbnailUrl = previewUrl,
                        SourceDomain = HaibabaSiteFolder,
                        IsChecked = true
                    }
                };
            }

            throw new Exception("URL haibabamanga.somee.com không hỗ trợ.");
        }

        private int ExtractHaibabaCategoryPageCount(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return 1;
            // E.g. Trang <span class="fw-bold text-white">1</span>/480
            Match match = Regex.Match(html, @"Trang(?:\s*<[^>]+>\s*\d+\s*</[^>]+>|\s+\d+)\s*/\s*(?<total>\d+)", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups["total"].Value, out int total))
            {
                return total;
            }

            int maxPage = 1;
            var matches = Regex.Matches(html, @"page=(?<num>\d+)", RegexOptions.IgnoreCase);
            foreach (Match m in matches)
            {
                if (int.TryParse(m.Groups["num"].Value, out int pageNum))
                {
                    if (pageNum > maxPage) maxPage = pageNum;
                }
            }
            return maxPage;
        }

        private void AppendHaibabaCategoryItems(List<GalleryItem> results, HashSet<string> seen, string html, string categoryUrl)
        {
            if (string.IsNullOrWhiteSpace(html)) return;
            // Match any href attribute pointing to /Home/Detail
            var matches = Regex.Matches(html, @"href=[""'](?<href>[^""']*?/Home/Detail\?slug=[^""'#\s>]+)[""']", RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                string rawHref = match.Groups["href"].Value;
                string cleanHref;
                try
                {
                    cleanHref = NormalizeHaibabaUrl(rawHref);
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(cleanHref) || !seen.Add(cleanHref)) continue;

                string slug = GetQueryParam(cleanHref, "slug");
                string title = HumanizeHaibabaSlug(slug);

                results.Add(new GalleryItem
                {
                    Link = cleanHref,
                    Name = title,
                    SourceDomain = HaibabaSiteFolder,
                    IsChecked = true
                });
            }
        }

        private async Task<List<GalleryItem>> ExtractHaibabaCategoryItemsAsync(string firstPageHtml, string categoryUrl, CancellationToken token, int pageFrom, int pageTo, Func<List<GalleryItem>, int, int, Task> onPageReady = null)
        {
            string baseCategoryUrl = NormalizeHaibabaUrl(categoryUrl);
            int totalPages = ExtractHaibabaCategoryPageCount(firstPageHtml);
            int startPage = Math.Max(1, pageFrom);
            int endPage = Math.Min(Math.Max(startPage, pageTo), Math.Max(1, totalPages));
            var results = new List<GalleryItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (startPage == 1)
            {
                int beforeCount = results.Count;
                AppendHaibabaCategoryItems(results, seen, firstPageHtml, baseCategoryUrl);
                if (onPageReady != null && results.Count > beforeCount)
                {
                    await onPageReady(results.Skip(beforeCount).ToList(), 1, endPage);
                }
            }

            for (int page = Math.Max(2, startPage); page <= endPage; page++)
            {
                token.ThrowIfCancellationRequested();
                string pageUrl = BuildHaibabaListingPageUrl(baseCategoryUrl, page);
                string html = await FetchStringAsync(pageUrl, token);
                int beforeCount = results.Count;
                AppendHaibabaCategoryItems(results, seen, html, baseCategoryUrl);
                if (onPageReady != null && results.Count > beforeCount)
                {
                    await onPageReady(results.Skip(beforeCount).ToList(), page, endPage);
                }
            }

            return results;
        }

        private string BuildHaibabaListingPageUrl(string listingUrl, int page)
        {
            if (!TryParseHaibabaUri(listingUrl, out Uri uri))
            {
                return listingUrl;
            }

            string slug = GetQueryParam(listingUrl, "slug");
            var queryParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(slug))
            {
                queryParts.Add("slug=" + slug);
            }

            if (page > 1)
            {
                queryParts.Add("page=" + page.ToString(CultureInfo.InvariantCulture));
            }

            var builder = new UriBuilder(uri)
            {
                Fragment = string.Empty,
                Query = queryParts.Count > 0 ? string.Join("&", queryParts) : string.Empty
            };

            return builder.Uri.AbsoluteUri.TrimEnd('/');
        }

        private string ExtractHaibabaBookTitle(string html, string url)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;

            var titleDivMatch = Regex.Match(html, @"<div[^>]*class=[""'][^""']*\binfo-title\b[^""']*[""'][^>]*>(?<title>[\s\S]*?)</div>", RegexOptions.IgnoreCase);
            if (titleDivMatch.Success)
            {
                string title = CleanHtml(titleDivMatch.Groups["title"].Value);
                if (!string.IsNullOrWhiteSpace(title)) return title;
            }

            var titleTagMatch = Regex.Match(html, @"<title>\s*(?<title>.*?)\s*</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (titleTagMatch.Success)
            {
                string title = CleanHtml(titleTagMatch.Groups["title"].Value);
                title = Regex.Replace(title, @"\s*-\s*Hải Bá Bá\s*-\s*MangakaApp\s*$", string.Empty, RegexOptions.IgnoreCase).Trim();
                return title;
            }

            string slug = GetQueryParam(url, "slug");
            return HumanizeHaibabaSlug(slug);
        }

        private string ExtractHaibabaBookTitleFromChapterHtml(string html, string url)
        {
            return ExtractHaibabaBookTitle(html, url);
        }

        private static string ExtractHaibabaPreviewUrlFromHtml(string html, string pageUrl)
        {
            return GetHaibabaPreviewUrlCandidatesFromHtml(html, pageUrl).FirstOrDefault() ?? string.Empty;
        }

        private static List<string> GetHaibabaPreviewUrlCandidatesFromHtml(string html, string pageUrl)
        {
            var urls = new List<string>();
            if (string.IsNullOrWhiteSpace(html))
            {
                return urls;
            }

            string coverHtml = string.Empty;
            Match coverBlockMatch = Regex.Match(
                html,
                @"<div[^>]*class=[""'][^""']*\bmanga-cover-container\b[^""']*[""'][^>]*>(?<content>[\s\S]*?)</div>",
                RegexOptions.IgnoreCase);
            if (coverBlockMatch.Success)
            {
                coverHtml = coverBlockMatch.Groups["content"].Value;
            }

            CollectHaibabaPreviewUrls(coverHtml, pageUrl, urls);
            if (urls.Count == 0)
            {
                CollectHaibabaPreviewUrls(html, pageUrl, urls);
            }

            return urls;
        }

        private static void CollectHaibabaPreviewUrls(string htmlFragment, string pageUrl, List<string> urls)
        {
            if (string.IsNullOrWhiteSpace(htmlFragment) || urls == null)
            {
                return;
            }

            foreach (Match match in Regex.Matches(
                htmlFragment,
                @"(?:data-src|data-original|src)=[""'](?<url>[^""']+?\.(?:jpe?g|png|gif|webp|bmp)(?:\?[^""']*)?)[""']",
                RegexOptions.IgnoreCase))
            {
                string normalizedUrl = NormalizeHaibabaPreviewUrl(match.Groups["url"].Value, pageUrl);
                if (!string.IsNullOrWhiteSpace(normalizedUrl) &&
                    !urls.Contains(normalizedUrl, StringComparer.OrdinalIgnoreCase))
                {
                    urls.Add(normalizedUrl);
                }
            }
        }

        private static string NormalizeHaibabaPreviewUrl(string imageUrl, string pageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                return string.Empty;
            }

            string cleanUrl = WebUtility.HtmlDecode(imageUrl).Replace("\\/", "/").Trim();
            if (string.IsNullOrWhiteSpace(cleanUrl))
            {
                return string.Empty;
            }

            if (Uri.TryCreate(cleanUrl, UriKind.Absolute, out Uri absoluteUri))
            {
                return absoluteUri.AbsoluteUri;
            }

            if (Uri.TryCreate(pageUrl, UriKind.Absolute, out Uri baseUri) &&
                Uri.TryCreate(baseUri, cleanUrl, out Uri resolvedUri))
            {
                return resolvedUri.AbsoluteUri;
            }

            return cleanUrl;
        }

        private string ExtractHaibabaChapterTitle(string html, string url)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;

            var h3Match = Regex.Match(html, @"<h3[^>]*class=[""'][^""']*\btext-primary\b[^""']*[""'][^>]*>\s*Chương:\s*(?<title>.*?)\s*</h3>", RegexOptions.IgnoreCase);
            if (h3Match.Success)
            {
                return "Chương " + CleanHtml(h3Match.Groups["title"].Value).Trim();
            }

            var chapterTitleMatch = Regex.Match(html, @"<[^>]*class=[""'][^""']*\bchapter-title\b[^""']*[""'][^>]*>(?<title>[\s\S]*?)</[^>]+>", RegexOptions.IgnoreCase);
            if (chapterTitleMatch.Success)
            {
                string title = CleanHtml(chapterTitleMatch.Groups["title"].Value);
                if (!string.IsNullOrWhiteSpace(title)) return title;
            }

            var titleTagMatch = Regex.Match(html, @"<title>\s*(?<title>.*?)\s*</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (titleTagMatch.Success)
            {
                string title = CleanHtml(titleTagMatch.Groups["title"].Value);
                title = Regex.Replace(title, @"\s*-\s*Hải Bá Bá\s*-\s*MangakaApp\s*$", string.Empty, RegexOptions.IgnoreCase).Trim();
                return title;
            }

            string chapterId = GetQueryParam(url, "chapterId");
            return chapterId;
        }

        private List<GalleryItem> ExtractHaibabaChapters(string html, string bookUrl)
        {
            var list = new List<(GalleryItem item, double num)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(html)) return new List<GalleryItem>();

            var matches = HaibabaChapterAnchorRegex.Matches(html);
            foreach (Match m in matches)
            {
                string rawHref = m.Groups["href"].Value;
                string cleanHref;
                try
                {
                    cleanHref = NormalizeHaibabaUrl(rawHref);
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(cleanHref) || !seen.Add(cleanHref)) continue;

                string innerText = CleanHtml(m.Groups["inner"].Value);
                double num = ParseChapterNumberFromText(innerText);
                string chapterName = NormalizeChapterLabel(innerText);
                if (string.IsNullOrWhiteSpace(chapterName))
                {
                    chapterName = innerText;
                }

                var chapItem = new GalleryItem
                {
                    Link = cleanHref,
                    Name = chapterName,
                    SourceDomain = HaibabaSiteFolder
                };

                list.Add((chapItem, num));
            }

            if (list.Count > 0)
            {
                list.Reverse();
                var sorted = list.OrderBy(x => x.num).Select(x => x.item).ToList();
                return sorted;
            }

            return new List<GalleryItem>();
        }

        private List<string> ExtractHaibabaChapterLinks(string html, string bookUrl)
        {
            var list = new List<(string url, double num)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(html)) return new List<string>();

            var matches = HaibabaChapterAnchorRegex.Matches(html);
            foreach (Match m in matches)
            {
                string rawHref = m.Groups["href"].Value;
                string cleanHref;
                try
                {
                    cleanHref = NormalizeHaibabaUrl(rawHref);
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(cleanHref) || !seen.Add(cleanHref)) continue;

                string innerText = CleanHtml(m.Groups["inner"].Value);
                double num = ParseChapterNumberFromText(innerText);
                list.Add((cleanHref, num));
            }

            if (list.Count > 0)
            {
                list.Reverse();
                var sorted = list.OrderBy(x => x.num).Select(x => x.url).ToList();
                return sorted;
            }

            return new List<string>();
        }

        private async Task DownloadHaibabaGalleryAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem = null, ChapterFilter chapterFilter = null)
        {
            item.Link = NormalizeHaibabaUrl(item.Link);

            if (IsHaibabaChapterUrl(item.Link))
            {
                await DownloadHaibabaChapterAsync(item, rootFolder, token, queueItem);
                return;
            }

            if (!IsHaibabaBookUrl(item.Link))
            {
                throw new Exception("Link haibabamanga.somee.com không hợp lệ. Cần link book hoặc chapter.");
            }

            await DownloadHaibabaBookAsync(item, rootFolder, token, queueItem, chapterFilter);
        }

        private async Task DownloadHaibabaBookAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem, ChapterFilter chapterFilter)
        {
            string bookUrl = NormalizeHaibabaUrl(item.Link);
            if (TryGetCachedDownloadChapterLinks(item, out List<string> cachedChapterLinks) && cachedChapterLinks != null)
            {
                cachedChapterLinks = cachedChapterLinks.Where(IsHaibabaChapterUrl).ToList();
            }

            if (cachedChapterLinks != null && cachedChapterLinks.Count > 0)
            {
                TryGetCachedDownloadChapterItems(item, out List<ReaderChapterItem> cachedItems);
                double GetHaibabaChapterNumber(string url)
                {
                    var found = cachedItems?.FirstOrDefault(x => string.Equals(x.FolderPath, url, StringComparison.OrdinalIgnoreCase));
                    if (found != null && TryParseReaderChapterNumber(found.Name, out double num, out _))
                    {
                        return num;
                    }
                    return ParseHaibabaChapterNumber(url);
                }

                var cachedLabelsByLink = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (cachedItems != null)
                {
                    foreach (var cached in cachedItems)
                    {
                        if (!string.IsNullOrWhiteSpace(cached.FolderPath) && !string.IsNullOrWhiteSpace(cached.Name))
                        {
                            cachedLabelsByLink[cached.FolderPath.Trim()] = cached.Name.Trim();
                        }
                    }
                }

                cachedChapterLinks = cachedChapterLinks.OrderBy(GetHaibabaChapterNumber).ToList();
                List<string> effectiveChapterLinks = chapterFilter != null
                    ? FilterPendingChapterLinksFromProcess(rootFolder, HaibabaSiteFolder, item, cachedChapterLinks.Where(link => chapterFilter.IsMatch(GetHaibabaChapterNumber(link))).ToList(), cachedLabelsByLink)
                    : FilterPendingChapterLinksFromProcess(rootFolder, HaibabaSiteFolder, item, cachedChapterLinks, cachedLabelsByLink);
                if (effectiveChapterLinks.Count == 0)
                {
                    if (queueItem != null)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            queueItem.Status = "Completed";
                            queueItem.CurrentProcess = "Đã hoàn tất theo process";
                        });
                    }
                    return;
                }

                string cachedBookTitle = string.IsNullOrWhiteSpace(item.Name) ? GetSafePathName(bookUrl) : item.Name;
                if (queueItem != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        queueItem.TotalChapters = effectiveChapterLinks.Count;
                        queueItem.CompletedChapters = 0;
                    });
                }

                int cachedCompletedCount = 0;
                foreach (string chapterLink in effectiveChapterLinks)
                {
                    token.ThrowIfCancellationRequested();
                    var chapterItem = new GalleryItem
                    {
                        Link = chapterLink,
                        Name = cachedBookTitle,
                        SourceDomain = HaibabaSiteFolder
                    };

                    bool completed = await DownloadHaibabaChapterAsync(chapterItem, rootFolder, token, queueItem, isParentQueue: true, bookTitleOverride: cachedBookTitle);
                    if (completed)
                    {
                        MarkChapterProcessDone(rootFolder, HaibabaSiteFolder, item, chapterLink);
                        cachedCompletedCount++;
                    }

                    if (queueItem != null && completed)
                    {
                        Dispatcher.Invoke(() => queueItem.CompletedChapters = cachedCompletedCount);
                    }
                }
                return;
            }

            string html = await FetchStringAsync(bookUrl, token);
            string bookTitle = ExtractHaibabaBookTitle(html, bookUrl);
            item.Name = bookTitle;

            List<GalleryItem> chapters = ExtractHaibabaChapters(html, bookUrl);
            CacheDownloadMissingChapterItems(item, chapters);
            List<string> chapterLinks = chapters.Select(c => c.Link).ToList();

            var freshLabelsByLink = chapters.ToDictionary(c => c.Link, c => c.Name, StringComparer.OrdinalIgnoreCase);
            double GetHaibabaChapterNumberFromItem(string url)
            {
                var found = chapters.FirstOrDefault(x => string.Equals(x.Link, url, StringComparison.OrdinalIgnoreCase));
                if (found != null && TryParseReaderChapterNumber(found.Name, out double num, out _))
                {
                    return num;
                }
                return ParseHaibabaChapterNumber(url);
            }

            if (chapterFilter != null)
            {
                var filtered = chapterLinks.Where(link => chapterFilter.IsMatch(GetHaibabaChapterNumberFromItem(link))).ToList();
                chapterLinks = FilterPendingChapterLinksFromProcess(rootFolder, HaibabaSiteFolder, item, filtered, freshLabelsByLink);
            }
            else
            {
                chapterLinks = FilterPendingChapterLinksFromProcess(rootFolder, HaibabaSiteFolder, item, chapterLinks, freshLabelsByLink);
            }

            if (chapterLinks.Count == 0)
            {
                if (queueItem != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        queueItem.Status = "Completed";
                        queueItem.CurrentProcess = "Đã hoàn tất theo process";
                    });
                }
                return;
            }

            if (queueItem != null)
            {
                Dispatcher.Invoke(() =>
                {
                    queueItem.TotalChapters = chapterLinks.Count;
                    queueItem.CompletedChapters = 0;
                });
            }

            int completedCount = 0;
            foreach (string chapterLink in chapterLinks)
            {
                token.ThrowIfCancellationRequested();
                var chapterItem = new GalleryItem
                {
                    Link = chapterLink,
                    Name = bookTitle,
                    SourceDomain = HaibabaSiteFolder
                };

                bool completed = await DownloadHaibabaChapterAsync(chapterItem, rootFolder, token, queueItem, isParentQueue: true, bookTitleOverride: bookTitle);
                if (completed)
                {
                    MarkChapterProcessDone(rootFolder, HaibabaSiteFolder, item, chapterLink);
                    completedCount++;
                }

                if (queueItem != null && completed)
                {
                    Dispatcher.Invoke(() => queueItem.CompletedChapters = completedCount);
                }
            }
        }

        private async Task<bool> DownloadHaibabaChapterAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem = null, bool isParentQueue = false, string bookTitleOverride = null)
        {
            string chapterUrl = NormalizeHaibabaUrl(item.Link);
            HaibabaLog($"Đang fetch chapter URL: {chapterUrl}");
            string html = await FetchStringAsync(chapterUrl, token);
            HaibabaLog($"Fetched chapter html size: {html?.Length ?? 0}");
            string bookTitle = string.IsNullOrWhiteSpace(bookTitleOverride)
                ? ExtractHaibabaBookTitleFromChapterHtml(html, chapterUrl)
                : CleanHaibabaTitle(bookTitleOverride);
            string chapterTitle = NormalizeChapterLabel(ExtractHaibabaChapterTitle(html, chapterUrl));

            if (string.IsNullOrWhiteSpace(bookTitle))
            {
                bookTitle = HumanizeHaibabaSlug(GetQueryParam(chapterUrl, "slug"));
            }

            if (string.IsNullOrWhiteSpace(chapterTitle))
            {
                string chapterId = GetQueryParam(chapterUrl, "chapterId");
                chapterTitle = NormalizeChapterLabel(chapterId.Replace("-", " "));
            }

            item.Name = bookTitle;
            string processChapterLabel = CompactSingleLine(chapterTitle);
            string safeBook = GetCanonicalBookFolderName(item, bookTitle, "Unknown Book");
            string aliasSafeBook = GetSafePathName(bookTitle);
            string safeChapter = GetDownloadChapterFolderName(bookTitle, chapterTitle);
            string siteRootFolder = GetSiteDownloadRoot(rootFolder, HaibabaSiteFolder);
            await NormalizeChapterFolderAliasAsync(siteRootFolder, safeBook, aliasSafeBook, safeChapter, token);

            string unmergedPath = Path.Combine(siteRootFolder, $"{safeBook}-{safeChapter}");
            string mergedPath = Path.Combine(siteRootFolder, safeBook, safeChapter);
            string finalTargetFolder = _isSingleComicFolderType ? mergedPath : unmergedPath;
            string tempFolder = BuildStableTempFolderPath(siteRootFolder, HaibabaSiteFolder, safeBook, safeChapter, chapterUrl);
            Directory.CreateDirectory(tempFolder);
            RegisterTempFolder(tempFolder);

            try
            {
                List<string> imageUrls = ExtractHaibabaImageUrls(html);
                if (imageUrls.Count == 0)
                {
                    string snippet = string.IsNullOrWhiteSpace(html) ? "null/empty" : (html.Length > 200 ? html.Substring(0, 200) : html);
                    HaibabaLog($"Lỗi: Không tìm thấy ảnh. HTML snippet: {snippet}");
                    throw new Exception("Không tìm thấy ảnh chapter haibabamanga.somee.com.");
                }

                if (queueItem != null && !isParentQueue)
                {
                    Dispatcher.Invoke(() =>
                    {
                        queueItem.TotalChapters = imageUrls.Count;
                        queueItem.CompletedChapters = 0;
                    });
                }

                if (queueItem != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        queueItem.DownloadingChapter = processChapterLabel;
                        queueItem.DownloadingPageProgress = $"1/{imageUrls.Count}";
                        queueItem.CurrentProcess = isParentQueue
                            ? $"{processChapterLabel} (trang 1/{imageUrls.Count})"
                            : $"1/{imageUrls.Count} pages";
                    });
                }

                WriteTempProgressLog(tempFolder, item, "Downloading", 0, imageUrls.Count, "0/0 pages", $"Bắt đầu tải {chapterTitle}");

                int maxThreads = GetCurrentConnectionLimit();
                List<string> pageFilenames = DetermineImageFilenames(imageUrls);
                using (var semaphore = new DynamicSemaphore(maxThreads, GetCurrentConnectionLimit))
                {
                    var tasks = new List<Task>();
                    int completedPages = 0;
                    object lockObj = new object();

                    for (int p = 0; p < imageUrls.Count; p++)
                    {
                        int index = p;
                        string imgUrl = imageUrls[index];

                        tasks.Add(Task.Run(async () =>
                        {
                            var pageWatch = System.Diagnostics.Stopwatch.StartNew();
                            while (_isDownloadPaused || (queueItem != null && queueItem.IsPaused))
                            {
                                token.ThrowIfCancellationRequested();
                                if (queueItem != null && queueItem.IsStopped)
                                {
                                    throw new OperationCanceledException();
                                }
                                await Task.Delay(200, token);
                            }

                            token.ThrowIfCancellationRequested();
                            await semaphore.WaitAsync(token);
                            try
                            {
                                string fileName = pageFilenames[index];
                                string localFilePath = Path.Combine(tempFolder, fileName);
                                if (File.Exists(localFilePath) && new FileInfo(localFilePath).Length > 1024)
                                {
                                    lock (lockObj)
                                    {
                                        completedPages++;
                                        string processText = isParentQueue
                                            ? $"{processChapterLabel} (trang {completedPages}/{imageUrls.Count})"
                                            : $"{completedPages}/{imageUrls.Count} pages";
                                        UpdateDownloadRowMetrics(queueItem, completedPages, imageUrls.Count, processText, 0, 0, isParentQueue);
                                    }
                                    return;
                                }

                                if (queueItem != null && queueItem.IsStopped)
                                {
                                    throw new OperationCanceledException();
                                }
                                token.ThrowIfCancellationRequested();
                                await DownloadUrlToFileWithRefererAsync(imgUrl, null, localFilePath, token);
                                pageWatch.Stop();
                                lock (lockObj)
                                {
                                    completedPages++;
                                    long downloadedBytes = File.Exists(localFilePath) ? new FileInfo(localFilePath).Length : 0;
                                    string processText = isParentQueue
                                        ? $"{processChapterLabel} (trang {completedPages}/{imageUrls.Count})"
                                        : $"{completedPages}/{imageUrls.Count} pages";
                                    UpdateDownloadRowMetrics(queueItem, completedPages, imageUrls.Count, processText, downloadedBytes, pageWatch.ElapsedMilliseconds, isParentQueue);
                                    if (queueItem != null)
                                    {
                                        int pageNumber = completedPages;
                                        Dispatcher.BeginInvoke((Action)(() => queueItem.DownloadingPageProgress = $"{pageNumber}/{imageUrls.Count}"));
                                    }
                                }
                            }
                            finally
                            {
                                semaphore.Release();
                            }
                        }, token));
                    }

                    await Task.WhenAll(tasks);
                }

                WriteTempProgressLog(tempFolder, item, "Done", imageUrls.Count, imageUrls.Count, $"{imageUrls.Count}/{imageUrls.Count} pages", "Download completed");
                MoveTempFolderToTarget(tempFolder, finalTargetFolder, "haibaba");
                return ValidateDownloadedFiles(finalTargetFolder, imageUrls.Count, queueItem ?? item, chapterTitle, chapterUrl: chapterUrl);
            }
            finally
            {
                UnregisterTempFolder(tempFolder);
            }
        }

        private List<string> ExtractHaibabaImageUrls(string html)
        {
            var results = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(html)) return results;

            foreach (Match match in Regex.Matches(html, @"<img[^>]+>", RegexOptions.IgnoreCase))
            {
                string tag = match.Value;
                string imageUrl = string.Empty;
                var dataSrcMatch = Regex.Match(tag, @"\bdata-src\s*=\s*[""'](?<src>[^""']+)[""']", RegexOptions.IgnoreCase);
                if (dataSrcMatch.Success)
                {
                    imageUrl = dataSrcMatch.Groups["src"].Value.Trim();
                }
                else
                {
                    var srcMatch = Regex.Match(tag, @"\bsrc\s*=\s*[""'](?<src>[^""']+)[""']", RegexOptions.IgnoreCase);
                    if (srcMatch.Success)
                    {
                        imageUrl = srcMatch.Groups["src"].Value.Trim();
                    }
                }

                if (string.IsNullOrWhiteSpace(imageUrl))
                {
                    continue;
                }

                imageUrl = WebUtility.HtmlDecode(imageUrl).Replace("\\/", "/").Trim();

                if (imageUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
                if (imageUrl.IndexOf("credit", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (imageUrl.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (imageUrl.IndexOf("thumb", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                // Chỉ nhận link ảnh truyện (chứa uploads/ hoặc otruyencdn)
                if (imageUrl.IndexOf("uploads/", StringComparison.OrdinalIgnoreCase) < 0 &&
                    imageUrl.IndexOf("otruyencdn", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (imageUrl.StartsWith("//", StringComparison.Ordinal))
                {
                    imageUrl = "https:" + imageUrl;
                }
                else if (imageUrl.StartsWith("/", StringComparison.Ordinal))
                {
                    imageUrl = "http://haibabamanga.somee.com" + imageUrl;
                }

                if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out Uri imageUri))
                {
                    continue;
                }

                switch (Path.GetExtension(imageUri.AbsolutePath).ToLowerInvariant())
                {
                    case ".webp":
                    case ".gif":
                    case ".jpg":
                    case ".jpeg":
                    case ".png":
                    case ".bmp":
                        break;
                    default:
                        continue;
                }

                if (seen.Add(imageUrl))
                {
                    results.Add(imageUrl);
                }
            }

            return results;
        }

        private double ParseHaibabaChapterNumber(string url)
        {
            string chapterId = GetQueryParam(url, "chapterId");
            if (TryParseChapterNumberFromChapterToken(chapterId, out double strictValue))
            {
                return strictValue;
            }

            Match match = Regex.Match(chapterId, @"(?<num>\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (match.Success &&
                double.TryParse(match.Groups["num"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
            {
                return value;
            }

            return 0d;
        }

        private static string GetQueryParam(string url, string paramName)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;
            try
            {
                int qIndex = url.IndexOf('?');
                if (qIndex < 0) return string.Empty;
                string query = url.Substring(qIndex + 1);
                string[] pairs = query.Split('&');
                foreach (var pair in pairs)
                {
                    string[] parts = pair.Split('=');
                    if (parts.Length == 2 && string.Equals(parts[0], paramName, StringComparison.OrdinalIgnoreCase))
                    {
                        return Uri.UnescapeDataString(parts[1]);
                    }
                }
            }
            catch {}
            return string.Empty;
        }

        private string CleanHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;
            string clean = WebUtility.HtmlDecode(Regex.Replace(html, @"<[^>]+>", " ")).Trim();
            return Regex.Replace(clean, @"\s+", " ").Trim();
        }
    }
}

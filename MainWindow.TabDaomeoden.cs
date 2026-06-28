using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private const string DaomeodenSiteFolder = "daomeoden.net";
        private const string DaomeodenBaseUrl = "https://daomeoden.net";
        private const string DaomeodenImageBaseUrl = "https://dmd-image-content-sng-1.imggo.net";

        private sealed class DaomeodenChapterInfo
        {
            public string BookTitle { get; set; }
            public string ChapterTitle { get; set; }
            public string BookSlug { get; set; }
            public List<string> FolderCandidates { get; set; } = new List<string>();
            public string RefererUrl { get; set; }
            public string ChapterId { get; set; }
            public string AjaxToken { get; set; }
            public string ImageBaseUrl { get; set; }
            public string ImagePathPrefix { get; set; }
            public string PageFilePrefix { get; set; }
            public string PageFileExtension { get; set; }
            public string PageFileSeparator { get; set; }
            public int PageNumberPadding { get; set; } = 3;
            public string SingleImageUrl { get; set; }
        }

        private sealed class DaomeodenDirectImageInfo
        {
            public string ImageBaseUrl { get; set; }
            public string ImagePathPrefix { get; set; }
            public string BookSlug { get; set; }
            public string ChapterFolder { get; set; }
            public string FileExtension { get; set; }
            public string PageFilePrefix { get; set; }
            public string PageFileSeparator { get; set; }
            public string PageToken { get; set; }
            public string FullImageUrl { get; set; }
        }

        private void DaomeodenLog(string message)
        {
            Log("[daomeoden] " + message);
        }

        private bool IsDaomeodenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            try
            {
                var uri = new Uri(url);
                return uri.Host.IndexOf("daomeoden.net", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return url.IndexOf("daomeoden.net", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        private bool IsDaomeodenImageRedirectUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            try
            {
                var uri = new Uri(url);
                return uri.Host.IndexOf("imggo.net", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return Regex.IsMatch(url, @"imggo\.net/", RegexOptions.IgnoreCase);
            }
        }

        private string NormalizeDaomeodenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            string normalized = url.Trim();
            if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.StartsWith("dmd-image-content-", StringComparison.OrdinalIgnoreCase)
                    ? "https://" + normalized
                    : DaomeodenBaseUrl + (normalized.StartsWith("/") ? string.Empty : "/") + normalized;
            }

            if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri uri))
            {
                throw new ArgumentException("URL daomeoden không hợp lệ.");
            }

            bool validHost =
                uri.Host.IndexOf("daomeoden.net", StringComparison.OrdinalIgnoreCase) >= 0 ||
                uri.Host.IndexOf("imggo.net", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!validHost)
            {
                throw new ArgumentException("URL phải thuộc domain daomeoden.net hoặc imggo.net.");
            }

            return uri.AbsoluteUri.TrimEnd('/');
        }

        private string GetDaomeodenPageUrl(string baseUrl, int page)
        {
            baseUrl = NormalizeDaomeodenUrl(baseUrl);
            if (page <= 1)
            {
                return baseUrl;
            }

            var builder = new UriBuilder(baseUrl);
            string query = builder.Query ?? string.Empty;
            query = query.TrimStart('?');
            query = Regex.Replace(query, @"(^|&)page=\d+(&|$)", "$1", RegexOptions.IgnoreCase).Trim('&');
            builder.Query = string.IsNullOrWhiteSpace(query) ? $"page={page}" : $"{query}&page={page}";
            return builder.Uri.AbsoluteUri.TrimEnd('/');
        }

        private async void BtnDaomeodenFetchInfo_Click(object sender, RoutedEventArgs e)
        {
            string rawUrl = txtDaomeodenTagUrl.Text.Trim();
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                MessageBox.Show("Vui lòng nhập URL hợp lệ.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            btnDaomeodenFetchInfo.IsEnabled = false;
            progressBar.IsIndeterminate = true;
            lblStatus.Text = "Đang phân tích trang daomeoden...";

            try
            {
                string normalizedUrl = NormalizeDaomeodenUrl(rawUrl);
                txtDaomeodenTagUrl.Text = normalizedUrl;

                string html = await FetchStringAsync(normalizedUrl, _downloadCts?.Token ?? CancellationToken.None);
                int estimatedPages = 1;
                if (Regex.IsMatch(html, @"bookGenreLoad\s*\(", RegexOptions.IgnoreCase))
                {
                    estimatedPages = 1;
                    DaomeodenLog("Trang thể loại dùng AJAX động. Mặc định crawl theo URL hiện tại; nếu cần trang khác, hãy nhập URL page cụ thể.");
                }

                txtDaomeodenTotalPages.Text = estimatedPages.ToString();
                txtDaomeodenPageTo.Text = estimatedPages.ToString();
                lblStatus.Text = $"Analysis complete. Found {estimatedPages} page.";
            }
            catch (Exception ex)
            {
                DaomeodenLog("Lỗi khi phân tích: " + ex.Message);
                txtDaomeodenTotalPages.Text = "1";
                txtDaomeodenPageTo.Text = "1";
                lblStatus.Text = "Analysis failed.";
            }
            finally
            {
                btnDaomeodenFetchInfo.IsEnabled = true;
                progressBar.IsIndeterminate = false;
            }
        }

        private void TxtDaomeodenTotalPages_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (txtDaomeodenPageTo != null && txtDaomeodenTotalPages != null)
            {
                txtDaomeodenPageTo.Text = txtDaomeodenTotalPages.Text;
            }
        }

        private async void BtnDaomeodenScrape_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                btnDaomeodenScrape.Content = "CANCELLING...";
                btnDaomeodenScrape.IsEnabled = false;
                if (btnDaomeodenCrawlMore != null)
                {
                    btnDaomeodenCrawlMore.IsEnabled = false;
                }
                return;
            }

            await ScrapeDaomeodenAsync(clearExisting: true);
        }

        private async void BtnDaomeodenCrawlMore_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                btnDaomeodenCrawlMore.Content = "CANCELLING...";
                btnDaomeodenCrawlMore.IsEnabled = false;
                btnDaomeodenScrape.IsEnabled = false;
                return;
            }

            await ScrapeDaomeodenAsync(clearExisting: false);
        }

        private async Task ScrapeDaomeodenAsync(bool clearExisting)
        {
            string rawUrl = txtDaomeodenTagUrl.Text.Trim();
            if (!int.TryParse(txtDaomeodenPageFrom.Text, out int pageFrom) || pageFrom < 1)
            {
                MessageBox.Show("Trang bắt đầu không hợp lệ.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!int.TryParse(txtDaomeodenPageTo.Text, out int pageTo) || pageTo < pageFrom)
            {
                MessageBox.Show("Trang kết thúc không hợp lệ.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            btnDaomeodenScrape.Content = "STOP CRAWLER";
            btnDaomeodenCrawlMore.Content = "STOP CRAWLER";
            btnDaomeodenFetchInfo.IsEnabled = false;
            lblStatus.Text = "Đang cào daomeoden...";
            progressBar.Value = 0;

            if (clearExisting)
            {
                _scrapedItems.Clear();
                if (chkSelectAll != null)
                {
                    chkSelectAll.IsChecked = false;
                }
                lblLinkCount.Text = "0";
            }

            try
            {
                string baseUrl = NormalizeDaomeodenUrl(rawUrl);
                txtDaomeodenTagUrl.Text = baseUrl;
                int totalPages = pageTo - pageFrom + 1;

                for (int page = pageFrom; page <= pageTo; page++)
                {
                    token.ThrowIfCancellationRequested();
                    string pageUrl = GetDaomeodenPageUrl(baseUrl, page);
                    string html = await FetchStringAsync(pageUrl, _downloadCts?.Token ?? CancellationToken.None);

                    foreach (var item in ParseDaomeodenGalleryItemsFromHtml(html, pageUrl))
                    {
                        if (_scrapedItems.Any(existing => existing.Link.Equals(item.Link, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        _scrapedItems.Add(item);
                    }

                    double progress = ((double)(page - pageFrom + 1) / totalPages) * 100;
                    progressBar.Value = progress;
                    lblStatus.Text = $"Scanning page {page}/{pageTo} ({progress:0}%)";
                    lblLinkCount.Text = _scrapedItems.Count.ToString();
                }

                var sortedItems = _scrapedItems
                    .OrderBy(item => item.Name)
                    .ThenBy(item => item.OriginalIndex)
                    .ToList();
                _scrapedItems.Clear();
                foreach (var item in sortedItems)
                {
                    _scrapedItems.Add(item);
                }

                RecalculateDuplicates();
                lblLinkCount.Text = _scrapedItems.Count.ToString();
                lblStatus.Text = "Crawling completed successfully.";
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = "Crawling cancelled.";
            }
            catch (Exception ex)
            {
                DaomeodenLog("Lỗi khi cào: " + ex.Message);
                lblStatus.Text = "Crawling failed.";
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                btnDaomeodenScrape.Content = "START CRAWLING";
                btnDaomeodenCrawlMore.Content = "CRAWL MORE";
                btnDaomeodenScrape.IsEnabled = true;
                btnDaomeodenCrawlMore.IsEnabled = true;
                btnDaomeodenFetchInfo.IsEnabled = true;
            }
        }

        private List<GalleryItem> ParseDaomeodenGalleryItemsFromHtml(string html, string pageUrl)
        {
            var results = new List<GalleryItem>();
            var matches = Regex.Matches(
                html ?? string.Empty,
                @"<div class=""item-cover"">\s*<a href=""(?<link>/truyen-tranh/[^""]+)"".*?</a>\s*</div>\s*<div class=""mt-1 item-title\s*"">\s*<a href=""[^""]+"">(?<title>.*?)</a></div>.*?part-2"">\s*<span[^>]*>(?<latest>.*?)</span>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match match in matches)
            {
                string relativeLink = match.Groups["link"].Value.Trim();
                string title = WebUtility.HtmlDecode(Regex.Replace(match.Groups["title"].Value, @"<[^>]+>", string.Empty)).Trim();
                string latest = WebUtility.HtmlDecode(Regex.Replace(match.Groups["latest"].Value, @"<[^>]+>", string.Empty)).Trim();
                string fullLink = DaomeodenBaseUrl + relativeLink;
                if (string.IsNullOrWhiteSpace(title))
                {
                    title = HumanizeDaomeodenSlug(Path.GetFileNameWithoutExtension(relativeLink).Trim('/'));
                }

                results.Add(new GalleryItem
                {
                    Link = fullLink.TrimEnd('/'),
                    Name = FormatGalleryTitle(title),
                    LinkCount = latest,
                    SourceDomain = DaomeodenSiteFolder,
                    OriginalIndex = _scrapedItems.Count + results.Count,
                    IsChecked = false
                });
            }

            return results;
        }

        private void BtnDaomeodenPasteDirect_Click(object sender, RoutedEventArgs e)
        {
            var window = new DirectDownloadWindow(
                customTitle: "PASTE DAOMEODEN LINKS",
                customDescription: "Paste daomeoden tag/book/chapter links or imggo direct image links below. The system will normalize and import them automatically.",
                customExample: "Example:\nhttps://daomeoden.net/the-loai/romance.html\nhttps://daomeoden.net/truyen-tranh/con-gai-cua-boss-47593-0.html\nhttps://daomeoden.net/doc-truyen-tranh/con-gai-cua-boss-47593/chuong-1-1457500-0.html\nhttps://dmd-image-content-sng-1.imggo.net/books/con-gai-cua-boss/1/001.jpg")
            {
                Owner = this
            };

            window.OnImport = async links => await ImportDaomeodenDirectLinksAsync(links);
            window.ShowDialog();
        }

        private async Task ImportDaomeodenDirectLinksAsync(List<string> links)
        {
            btnDaomeodenScrape.IsEnabled = false;
            btnDaomeodenFetchInfo.IsEnabled = false;
            progressBar.Value = 0;
            progressBar.IsIndeterminate = false;

            int success = 0;
            int failed = 0;
            int total = links.Count;

            try
            {
                for (int i = 0; i < links.Count; i++)
                {
                    string rawLink = links[i].Trim();
                    lblStatus.Text = $"[{i + 1}/{total}] Importing {rawLink}";

                    try
                    {
                        if (IsDaomeodenImageRedirectUrl(rawLink))
                        {
                            AddDaomeodenDirectImageItem(rawLink);
                            success++;
                        }
                        else
                        {
                            string link = NormalizeDaomeodenUrl(rawLink);
                            if (Regex.IsMatch(link, @"/the-loai/", RegexOptions.IgnoreCase))
                            {
                                string html = await FetchStringAsync(link, _downloadCts?.Token ?? CancellationToken.None);
                                foreach (var item in ParseDaomeodenGalleryItemsFromHtml(html, link))
                                {
                                    if (_scrapedItems.Any(existing => existing.Link.Equals(item.Link, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        continue;
                                    }

                                    _scrapedItems.Add(item);
                                }
                                success++;
                            }
                            else
                            {
                                AddDaomeodenItem(await BuildDaomeodenGalleryItemAsync(link));
                                success++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        DaomeodenLog($"Import lỗi với '{rawLink}': {ex.Message}");
                    }

                    progressBar.Value = ((double)(i + 1) / total) * 100;
                    lblLinkCount.Text = _scrapedItems.Count.ToString();
                }

                RecalculateDuplicates();
                lblLinkCount.Text = _scrapedItems.Count.ToString();
                lblStatus.Text = $"Import completed. Success: {success}, Failed: {failed}.";
            }
            finally
            {
                btnDaomeodenScrape.IsEnabled = true;
                btnDaomeodenFetchInfo.IsEnabled = true;
            }
        }

        private void AddDaomeodenItem(GalleryItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Link))
            {
                return;
            }

            if (_scrapedItems.Any(existing => existing.Link.Equals(item.Link, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            item.OriginalIndex = _scrapedItems.Count;
            _scrapedItems.Add(item);
        }

        private void AddDaomeodenDirectImageItem(string rawLink)
        {
            string link = NormalizeDaomeodenUrl(rawLink);
            var directInfo = ParseDaomeodenDirectImageUrl(link);
            string bookTitle = "Daomeoden";
            string chapterTitle = "Chapter";
            if (directInfo != null)
            {
                bookTitle = HumanizeDaomeodenSlug(directInfo.BookSlug);
                chapterTitle = "Chapter " + directInfo.ChapterFolder.Replace("-", ".");
            }

            AddDaomeodenItem(new GalleryItem
            {
                Link = link,
                Name = FormatGalleryTitle($"{bookTitle} - {chapterTitle}"),
                SourceDomain = DaomeodenSiteFolder,
                IsChecked = true
            });
        }

        private async Task<GalleryItem> BuildDaomeodenGalleryItemAsync(string link)
        {
            string html = await FetchStringAsync(link, _downloadCts?.Token ?? CancellationToken.None);
            string title = Path.GetFileNameWithoutExtension(link);
            string latest = string.Empty;

            if (Regex.IsMatch(link, @"/truyen-tranh/", RegexOptions.IgnoreCase))
            {
                title = ExtractDaomeodenBookTitleFromHtml(html, link);
                var chapterCountMatch = Regex.Match(html, @"chapter-count[^>]*>\s*<span>(?<count>[^<]+)</span>", RegexOptions.IgnoreCase);
                if (chapterCountMatch.Success)
                {
                    latest = chapterCountMatch.Groups["count"].Value.Trim();
                }
            }
            else if (Regex.IsMatch(link, @"/doc-truyen-tranh/", RegexOptions.IgnoreCase))
            {
                var chapterInfo = ParseDaomeodenChapterInfoFromChapterUrl(link);
                title = $"{chapterInfo.BookTitle} - {chapterInfo.ChapterTitle}";
                latest = chapterInfo.ChapterTitle;
            }

            return new GalleryItem
            {
                Link = link,
                Name = FormatGalleryTitle(title),
                LinkCount = latest,
                SourceDomain = DaomeodenSiteFolder,
                IsChecked = true
            };
        }

        private string ExtractDaomeodenBookTitleFromHtml(string html, string link)
        {
            var titleMatch = Regex.Match(html ?? string.Empty, @"<title>\s*(.*?)\s*</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (titleMatch.Success)
            {
                string rawTitle = WebUtility.HtmlDecode(titleMatch.Groups[1].Value).Trim();
                rawTitle = Regex.Replace(rawTitle, @"^\s*(?:Đọc truyện tranh|Doc truyen tranh|Truyện tranh|Truyen tranh)\s+", string.Empty, RegexOptions.IgnoreCase);
                rawTitle = Regex.Replace(rawTitle, @"\s*-\s*(?:Chuong|Chương|Chapter|Chap).*$", string.Empty, RegexOptions.IgnoreCase);
                rawTitle = rawTitle.Trim().Trim('!', '-', '|', ' ');
                if (!string.IsNullOrWhiteSpace(rawTitle))
                {
                    return rawTitle;
                }
            }

            try
            {
                var uri = new Uri(link);
                string slug = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
                slug = Regex.Replace(slug, @"-\d+-0$", string.Empty);
                return HumanizeDaomeodenSlug(slug);
            }
            catch
            {
                return HumanizeDaomeodenSlug(link);
            }
        }

        private string HumanizeDaomeodenSlug(string value)
        {
            string cleaned = (value ?? string.Empty).Trim().Trim('/').Trim();
            cleaned = Regex.Replace(cleaned, @"-\d+-0$", string.Empty);
            cleaned = Regex.Replace(cleaned, @"-\d+$", string.Empty);
            cleaned = cleaned.Replace('-', ' ');
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return "Daomeoden";
            }

            return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(cleaned.ToLowerInvariant());
        }

        private async Task DownloadDaomeodenGalleryAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem = null, ChapterFilter chapterFilter = null)
        {
            string normalizedLink = NormalizeDaomeodenUrl(item.Link);

            if (IsDaomeodenImageRedirectUrl(normalizedLink))
            {
                await DownloadDaomeodenDirectImageSetAsync(item, rootFolder, token, queueItem);
                return;
            }

            var uri = new Uri(normalizedLink);
            string[] segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                throw new Exception("URL daomeoden không hợp lệ.");
            }

            if (segments[0].Equals("truyen-tranh", StringComparison.OrdinalIgnoreCase))
            {
                await DownloadDaomeodenBookAsync(item, rootFolder, token, queueItem, chapterFilter);
                return;
            }

            if (segments[0].Equals("doc-truyen-tranh", StringComparison.OrdinalIgnoreCase))
            {
                await DownloadDaomeodenChapterAsync(item, rootFolder, token, queueItem, false);
                return;
            }

            throw new Exception("Daomeoden chỉ hỗ trợ link truyện, link chapter hoặc imggo direct.");
        }

        private async Task DownloadDaomeodenBookAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem, ChapterFilter chapterFilter)
        {
            string html = await FetchStringAsync(item.Link, _downloadCts?.Token ?? CancellationToken.None);
            item.Name = FormatGalleryTitle(ExtractDaomeodenBookTitleFromHtml(html, item.Link));

            var chapterMatches = Regex.Matches(
                html,
                @"(?:href|openUrl\()\s*(?:=\s*|['""])(?<link>/doc-truyen-tranh/[^'"")\s>]+)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            var chapterLinks = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in chapterMatches)
            {
                string link = NormalizeDaomeodenUrl(match.Groups["link"].Value);
                if (seen.Add(link))
                {
                    chapterLinks.Add(link);
                }
            }

            if (chapterFilter != null)
            {
                chapterLinks = chapterLinks
                    .Where(link => chapterFilter.IsMatch(ParseChapterNumber(link)))
                    .ToList();
            }
            else
            {
                var pendingFromProcess = LoadPendingChapterLinksFromProcess(rootFolder, DaomeodenSiteFolder, item);
                if (pendingFromProcess != null)
                {
                    pendingFromProcess = pendingFromProcess
                        .Where(IsDaomeodenChapterLink)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (pendingFromProcess.Count == 0)
                    {
                        InitializeChapterProcess(rootFolder, DaomeodenSiteFolder, item, chapterLinks, preserveExistingDone: false);
                    }
                    else
                    {
                        chapterLinks = pendingFromProcess;
                    }
                }
                else
                {
                    InitializeChapterProcess(rootFolder, DaomeodenSiteFolder, item, chapterLinks);
                }
            }

            if (queueItem != null)
            {
                Dispatcher.Invoke(() =>
                {
                    queueItem.TotalChapters = chapterLinks.Count;
                    queueItem.CompletedChapters = 0;
                });
            }

            for (int i = 0; i < chapterLinks.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var chapterItem = new GalleryItem
                {
                    Link = chapterLinks[i],
                    Name = item.Name,
                    SourceDomain = DaomeodenSiteFolder
                };

                bool chapterCompleted = await DownloadDaomeodenChapterAsync(chapterItem, rootFolder, token, queueItem, true);
                if (chapterCompleted)
                {
                    MarkChapterProcessDone(rootFolder, DaomeodenSiteFolder, item, chapterLinks[i]);
                }

                if (queueItem != null)
                {
                    int completed = i + 1;
                    Dispatcher.Invoke(() => queueItem.CompletedChapters = completed);
                }
            }
        }

        private bool IsDaomeodenChapterLink(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            try
            {
                string normalized = NormalizeDaomeodenUrl(url);
                var uri = new Uri(normalized);
                string[] segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                return segments.Length >= 3 &&
                       segments[0].Equals("doc-truyen-tranh", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private async Task DownloadDaomeodenDirectImageSetAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem)
        {
            string link = NormalizeDaomeodenUrl(item.Link);
            var directInfo = ParseDaomeodenDirectImageUrl(link);
            if (directInfo == null)
            {
                throw new Exception("Không thể phân tích link imggo của daomeoden.");
            }

            var info = new DaomeodenChapterInfo
            {
                BookTitle = HumanizeDaomeodenSlug(directInfo.BookSlug),
                ChapterTitle = "Chapter " + directInfo.ChapterFolder.Replace("-", "."),
                BookSlug = directInfo.BookSlug,
                RefererUrl = DaomeodenBaseUrl + "/",
                ImageBaseUrl = directInfo.ImageBaseUrl,
                ImagePathPrefix = directInfo.ImagePathPrefix,
                PageFilePrefix = directInfo.PageFilePrefix,
                PageFileSeparator = directInfo.PageFileSeparator,
                PageFileExtension = directInfo.FileExtension,
                PageNumberPadding = string.IsNullOrWhiteSpace(directInfo.PageToken) ? 3 : directInfo.PageToken.Length,
                SingleImageUrl = string.IsNullOrWhiteSpace(directInfo.PageToken) ? directInfo.FullImageUrl : null
            };
            info.FolderCandidates.Add(directInfo.ChapterFolder);

            await DownloadDaomeodenChapterImagesAsync(item, rootFolder, token, queueItem, info);
        }

        private async Task<bool> DownloadDaomeodenChapterAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem, bool isParentQueue)
        {
            var info = ParseDaomeodenChapterInfoFromChapterUrl(item.Link);
            await EnrichDaomeodenChapterInfoFromPageAsync(info, token);
            return await DownloadDaomeodenChapterImagesAsync(item, rootFolder, token, queueItem, info, isParentQueue);
        }

        private async Task<bool> DownloadDaomeodenChapterImagesAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem, DaomeodenChapterInfo info, bool isParentQueue = false)
        {
            info.ChapterTitle = NormalizeChapterLabel(info.ChapterTitle);
            string safeManga = GetSafePathName(info.BookTitle);
            string safeChapter = GetSafeChapterPathName(info.BookTitle, info.ChapterTitle);
            string siteRootFolder = GetSiteDownloadRoot(rootFolder, DaomeodenSiteFolder);
            string targetFolder = Path.Combine(siteRootFolder, safeManga, safeChapter);
            string tempFolder = BuildStableTempFolderPath(siteRootFolder, DaomeodenSiteFolder, safeManga, safeChapter, item.Link);

            Directory.CreateDirectory(tempFolder);
            RegisterTempFolder(tempFolder);

            try
            {
                var imageUrls = await BuildDaomeodenImageUrlsAsync(info, token);
                if (imageUrls.Count == 0)
                {
                    throw new Exception("Không tìm thấy ảnh nào cho chapter này.");
                }

                if (queueItem != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        queueItem.DownloadingChapter = info.ChapterTitle;
                        queueItem.CurrentProcess = $"Downloading {info.ChapterTitle}";
                    });
                }

                int maxThreads = GetCurrentConnectionLimit();
            var pageFilenames = DetermineImageFilenames(imageUrls);

            using (var semaphore = new DynamicSemaphore(maxThreads, GetCurrentConnectionLimit))
            {
                var tasks = new System.Collections.Generic.List<Task>();
                int completedPages = 0;
                object lockObj = new object();

                for (int i = 0; i < imageUrls.Count; i++)
                {
                    int index = i;
                    string pageUrl = imageUrls[index];
                    string filePath = Path.Combine(tempFolder, pageFilenames[index]);

                        tasks.Add(Task.Run(async () =>
                        {
                            var pageWatch = System.Diagnostics.Stopwatch.StartNew();
                            while (_isDownloadPaused || (queueItem != null && queueItem.IsPaused))
                            {
                                token.ThrowIfCancellationRequested();
                                if (queueItem != null && queueItem.IsStopped) throw new OperationCanceledException();
                                await Task.Delay(200, token);
                            }
                            token.ThrowIfCancellationRequested();

                            await semaphore.WaitAsync(token);
                            try
                            {
                                while (_isDownloadPaused || (queueItem != null && queueItem.IsPaused))
                                {
                                    token.ThrowIfCancellationRequested();
                                    if (queueItem != null && queueItem.IsStopped) throw new OperationCanceledException();
                                    await Task.Delay(200, token);
                                }
                                token.ThrowIfCancellationRequested();

                                if (File.Exists(filePath) && new FileInfo(filePath).Length > 1024)
                                {
                                    pageWatch.Stop();
                                    lock (lockObj)
                                    {
                                        completedPages++;
                                        string processText = isParentQueue ? $"{info.ChapterTitle} (trang {completedPages}/{imageUrls.Count})" : $"Trang {completedPages}/{imageUrls.Count}";
                                        UpdateDownloadRowMetrics(queueItem, completedPages, imageUrls.Count, processText, 0, 0, isParentQueue);
                                        if (queueItem != null)
                                        {
                                            int pageNumber = completedPages;
                                            Dispatcher.BeginInvoke((Action)(() =>
                                            {
                                                queueItem.DownloadingPageProgress = $"{pageNumber}/{imageUrls.Count}";
                                            }));
                                        }
                                    }
                                    return;
                                }

                                string downloadedPath = null;
                                try
                                {
                                    await DownloadUrlToFileWithRefererAsync(pageUrl, info.RefererUrl, filePath, token);
                                    downloadedPath = filePath;
                                }
                                catch (Exception ex)
                                {
                                    Log($"[daomeoden] Lỗi tải trang {index + 1} của chapter '{info.ChapterTitle}': {ex.Message}");
                                }

                                pageWatch.Stop();
                                lock (lockObj)
                                {
                                    completedPages++;
                                    long downloadedBytes = !string.IsNullOrWhiteSpace(downloadedPath) && File.Exists(downloadedPath) ? new FileInfo(downloadedPath).Length : 0;
                                    string processText = isParentQueue ? $"{info.ChapterTitle} (trang {completedPages}/{imageUrls.Count})" : $"Trang {completedPages}/{imageUrls.Count}";
                                    UpdateDownloadRowMetrics(queueItem, completedPages, imageUrls.Count, processText, downloadedBytes, pageWatch.ElapsedMilliseconds, isParentQueue);
                                    if (queueItem != null)
                                    {
                                        int pageNumber = completedPages;
                                        Dispatcher.BeginInvoke((Action)(() =>
                                        {
                                            queueItem.DownloadingPageProgress = $"{pageNumber}/{imageUrls.Count}";
                                        }));
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

                var pageMap = imageUrls
                    .Select((url, index) => new { url, index })
                    .ToDictionary(x => x.index + 1, x => x.url);
                MoveTempFolderToTarget(tempFolder, targetFolder, "daomeoden");
                return ValidateDownloadedFiles(targetFolder, imageUrls.Count, queueItem ?? item, info.ChapterTitle, pageMap, chapterUrl: item.Link);
            }
            finally
            {
            }
        }

        private DaomeodenChapterInfo ParseDaomeodenChapterInfoFromChapterUrl(string chapterUrl)
        {
            string normalized = NormalizeDaomeodenUrl(chapterUrl);
            var uri = new Uri(normalized);
            string[] segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 3 || !segments[0].Equals("doc-truyen-tranh", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Link chapter daomeoden không hợp lệ.");
            }

            string rawBookSlug = segments[1];
            string bookSlug = Regex.Replace(rawBookSlug, @"-\d+$", string.Empty);
            string chapterFile = Path.GetFileNameWithoutExtension(segments[2]);
            var chapterMatch = Regex.Match(chapterFile, @"^(?:chuong|chapter|chap)-(?<token>.+)-\d+-0$", RegexOptions.IgnoreCase);
            string token = chapterMatch.Success ? chapterMatch.Groups["token"].Value : chapterFile;

            string numericToken = token;
            if (token.IndexOf("-") >= 0)
            {
                numericToken = token.Replace("-", ".");
            }

            var info = new DaomeodenChapterInfo
            {
                BookTitle = HumanizeDaomeodenSlug(bookSlug),
                ChapterTitle = "Chapter " + numericToken,
                BookSlug = bookSlug,
                RefererUrl = normalized
            };

            foreach (string candidate in new[]
            {
                token,
                token.Replace(".", "-"),
                token.Replace("-", "."),
                Regex.Match(token, @"\d+(?:-\d+)?").Value
            })
            {
                if (!string.IsNullOrWhiteSpace(candidate) &&
                    !info.FolderCandidates.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                {
                    info.FolderCandidates.Add(candidate);
                }
            }

            return info;
        }

        private async Task<List<string>> BuildDaomeodenImageUrlsAsync(DaomeodenChapterInfo info, CancellationToken token)
        {
            if (!string.IsNullOrWhiteSpace(info.SingleImageUrl))
            {
                return new List<string> { info.SingleImageUrl };
            }

            var ajaxImageUrls = await TryLoadDaomeodenChapterImagesFromAjaxAsync(info, token);
            if (ajaxImageUrls.Count > 0)
            {
                return ajaxImageUrls;
            }

            string imageBaseUrl = string.IsNullOrWhiteSpace(info.ImageBaseUrl) ? DaomeodenImageBaseUrl : info.ImageBaseUrl.TrimEnd('/');
            string imagePathPrefix = string.IsNullOrWhiteSpace(info.ImagePathPrefix) ? "books" : info.ImagePathPrefix;

            foreach (string chapterFolder in info.FolderCandidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate)))
            {
                var imageUrls = new List<string>();
                int missCount = 0;

                for (int page = 1; page <= 500; page++)
                {
                    token.ThrowIfCancellationRequested();
                    string imageUrl = BuildDaomeodenFallbackImageUrl(imageBaseUrl, imagePathPrefix, info, chapterFolder, page);
                    bool exists = await DoesDaomeodenImageExistAsync(imageUrl, info.RefererUrl, token);
                    if (exists)
                    {
                        missCount = 0;
                        imageUrls.Add(imageUrl);
                        continue;
                    }

                    if (imageUrls.Count == 0)
                    {
                        break;
                    }

                    missCount++;
                    if (missCount >= 3)
                    {
                        return imageUrls;
                    }
                }

                if (imageUrls.Count > 0)
                {
                    return imageUrls;
                }
            }

            return new List<string>();
        }

        private async Task EnrichDaomeodenChapterInfoFromPageAsync(DaomeodenChapterInfo info, CancellationToken token)
        {
            if (info == null || string.IsNullOrWhiteSpace(info.RefererUrl))
            {
                return;
            }

            string html;
            using (var request = new HttpRequestMessage(HttpMethod.Get, info.RefererUrl))
            using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, token))
            {
                response.EnsureSuccessStatusCode();
                html = await response.Content.ReadAsStringAsync();
            }

            var breadcrumbMatch = Regex.Match(
                html,
                @"<span class=""me-2 breadcrumb"">(?<book>.*?)</span>.*?<span class=""me-2 breadcrumb active "">(?<chapter>.*?)</span>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (breadcrumbMatch.Success)
            {
                string bookTitle = WebUtility.HtmlDecode(Regex.Replace(breadcrumbMatch.Groups["book"].Value, @"<[^>]+>", string.Empty)).Trim();
                string chapterTitle = WebUtility.HtmlDecode(Regex.Replace(breadcrumbMatch.Groups["chapter"].Value, @"<[^>]+>", string.Empty)).Trim();

                if (!string.IsNullOrWhiteSpace(bookTitle))
                {
                    info.BookTitle = bookTitle;
                }

                if (!string.IsNullOrWhiteSpace(chapterTitle))
                {
                    chapterTitle = Regex.Replace(chapterTitle, @"\s+", " ").Trim();
                    info.ChapterTitle = chapterTitle.StartsWith("Chuong", StringComparison.OrdinalIgnoreCase) ||
                                        chapterTitle.StartsWith("Chapter", StringComparison.OrdinalIgnoreCase) ||
                                        chapterTitle.StartsWith("Chap", StringComparison.OrdinalIgnoreCase)
                        ? chapterTitle
                        : "Chapter " + chapterTitle;
                }
            }

            if (string.IsNullOrWhiteSpace(info.ChapterId))
            {
                var chapterIdMatch = Regex.Match(html, @"\bchapterId\s*=\s*'(?<id>\d+)'", RegexOptions.IgnoreCase);
                if (chapterIdMatch.Success)
                {
                    info.ChapterId = chapterIdMatch.Groups["id"].Value;
                }
            }

            if (string.IsNullOrWhiteSpace(info.AjaxToken))
            {
                var tokenMatch = Regex.Match(html, @"\b_token\s*=\s*'(?<token>[^']+)'", RegexOptions.IgnoreCase);
                if (tokenMatch.Success)
                {
                    info.AjaxToken = tokenMatch.Groups["token"].Value;
                }
            }
        }

        private async Task<List<string>> TryLoadDaomeodenChapterImagesFromAjaxAsync(DaomeodenChapterInfo info, CancellationToken token)
        {
            if (info == null)
            {
                return new List<string>();
            }

            if (string.IsNullOrWhiteSpace(info.AjaxToken) || string.IsNullOrWhiteSpace(info.ChapterId))
            {
                try
                {
                    await EnrichDaomeodenChapterInfoFromPageAsync(info, token);
                }
                catch (Exception ex)
                {
                    DaomeodenLog("Không thể đọc metadata chapter từ trang gốc: " + ex.Message);
                }
            }

            if (string.IsNullOrWhiteSpace(info.AjaxToken) || string.IsNullOrWhiteSpace(info.ChapterId))
            {
                return new List<string>();
            }

            using (var request = new HttpRequestMessage(HttpMethod.Post, DaomeodenBaseUrl + "/apps/controllers/book/bookChapterContent.php"))
            {
                request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "token", info.AjaxToken },
                    { "chapterId", info.ChapterId },
                    { "cookies", "W10=" }
                });

                if (!string.IsNullOrWhiteSpace(info.RefererUrl))
                {
                    request.Headers.Referrer = new Uri(info.RefererUrl);
                }

                using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, token))
                {
                    response.EnsureSuccessStatusCode();
                    string json = await response.Content.ReadAsStringAsync();
                    if (!json.Contains(@"""status"":200"))
                    {
                        return new List<string>();
                    }

                    var dataMatch = Regex.Match(json, @"""data"":""(?<html>(?:\\.|[^""])*)""", RegexOptions.Singleline);
                    if (!dataMatch.Success)
                    {
                        return new List<string>();
                    }

                    string chapterHtml = Regex.Unescape(dataMatch.Groups["html"].Value).Replace("\\/", "/");
                    MatchCollection matches = Regex.Matches(chapterHtml, @"<(?:img|source)[^>]+data-src=""(?<url>[^""]+)""", RegexOptions.IgnoreCase);
                    if (matches.Count == 0)
                    {
                        matches = Regex.Matches(chapterHtml, @"<(?:img|source)[^>]+src=""(?<url>[^""]+)""", RegexOptions.IgnoreCase);
                    }

                    var imageUrls = new List<string>();
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (Match match in matches)
                    {
                        string imageUrl = match.Groups["url"].Value.Trim();
                        if (string.IsNullOrWhiteSpace(imageUrl) ||
                            imageUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                            imageUrl.IndexOf("imggo.net", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }

                        if (imageUrl.StartsWith("//"))
                        {
                            imageUrl = "https:" + imageUrl;
                        }

                        if (seen.Add(imageUrl))
                        {
                            imageUrls.Add(imageUrl);
                        }
                    }

                    if (imageUrls.Count > 0)
                    {
                        DaomeodenLog($"Đã lấy {imageUrls.Count} ảnh từ AJAX chapter {info.ChapterId}.");
                    }

                    return imageUrls;
                }
            }
        }

        private async Task<bool> DoesDaomeodenImageExistAsync(string imageUrl, string refererUrl, CancellationToken token)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Head, imageUrl))
                {
                    if (!string.IsNullOrWhiteSpace(refererUrl))
                    {
                        request.Headers.Referrer = new Uri(refererUrl);
                    }

                    using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token))
                    {
                        return response.IsSuccessStatusCode;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private string BuildDaomeodenFallbackImageUrl(string imageBaseUrl, string imagePathPrefix, DaomeodenChapterInfo info, string chapterFolder, int page)
        {
            string extension = string.IsNullOrWhiteSpace(info.PageFileExtension) ? ".jpg" : info.PageFileExtension;
            int padding = info.PageNumberPadding > 0 ? info.PageNumberPadding : 3;
            string pageToken = page.ToString().PadLeft(padding, '0');
            string separator = string.IsNullOrWhiteSpace(info.PageFileSeparator) ? "_" : info.PageFileSeparator;

            if (!string.IsNullOrWhiteSpace(info.PageFilePrefix))
            {
                return $"{imageBaseUrl}/{imagePathPrefix}/{info.BookSlug}/{chapterFolder}/{info.PageFilePrefix}{separator}{pageToken}{extension}";
            }

            return $"{imageBaseUrl}/{imagePathPrefix}/{info.BookSlug}/{chapterFolder}/{pageToken}{extension}";
        }

        private string GetDaomeodenImageExtension(string imageUrl)
        {
            try
            {
                string extension = Path.GetExtension(new Uri(imageUrl).AbsolutePath);
                if (!string.IsNullOrWhiteSpace(extension))
                {
                    return extension.ToLowerInvariant();
                }
            }
            catch
            {
            }

            return ".jpg";
        }

        private DaomeodenDirectImageInfo ParseDaomeodenDirectImageUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                if (uri.Host.IndexOf("imggo.net", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return null;
                }

                string[] segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length < 4)
                {
                    return null;
                }

                string fileName = segments[segments.Length - 1];
                string chapterFolder = segments[segments.Length - 2];
                string bookSlug = segments[segments.Length - 3];
                string[] prefixSegments = segments.Take(segments.Length - 3).ToArray();
                string extension = Path.GetExtension(fileName);
                string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
                if (string.IsNullOrWhiteSpace(extension) || string.IsNullOrWhiteSpace(fileNameWithoutExtension))
                {
                    return null;
                }

                var info = new DaomeodenDirectImageInfo
                {
                    ImageBaseUrl = $"{uri.Scheme}://{uri.Host}",
                    ImagePathPrefix = string.Join("/", prefixSegments),
                    BookSlug = bookSlug,
                    ChapterFolder = chapterFolder,
                    FileExtension = extension.ToLowerInvariant(),
                    FullImageUrl = uri.AbsoluteUri
                };

                var numberedMatch = Regex.Match(fileNameWithoutExtension, @"^(?<prefix>.+?)(?<sep>[_-])(?<page>\d+)$", RegexOptions.IgnoreCase);
                if (numberedMatch.Success)
                {
                    info.PageFilePrefix = numberedMatch.Groups["prefix"].Value;
                    info.PageFileSeparator = numberedMatch.Groups["sep"].Value;
                    info.PageToken = numberedMatch.Groups["page"].Value;
                }

                return info;
            }
            catch
            {
                return null;
            }
        }
    }
}

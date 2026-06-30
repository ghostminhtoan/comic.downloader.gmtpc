using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private const string Hentai2readBaseUrl = "https://hentai2read.com";
        private const string Hentai2readSiteFolder = "hentai2read.com";

        private static readonly HashSet<string> Hentai2readSortTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "name-az",
            "name-za",
            "last-updated",
            "oldest-updated",
            "most-popular",
            "most-popular-daily",
            "most-popular-weekly",
            "most-popular-monthly",
            "user-recommendation",
            "trending",
            "staff-pick",
            "least-popular",
            "last-added",
            "early-added",
            "top-rating",
            "lowest-rating"
        };

        private void Hentai2readLog(string message)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                string logLine = $"[{DateTime.Now:HH:mm:ss}] {message}\r\n";
                bool isError = IsErrorMessage(message);
                AppendLogLine(txtHentai2readLog, logLine, isError);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private sealed class Hentai2readListRoute
        {
            public string NormalizedListingUrl { get; set; }
            public string PageBaseUrl { get; set; }
            public string SortToken { get; set; }
            public bool IsListingRoute { get; set; }
        }

        private string NormalizeHentai2readUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return Hentai2readBaseUrl + "/";
            }

            string normalized = url.Trim();
            if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (normalized.StartsWith("/", StringComparison.Ordinal))
                {
                    normalized = Hentai2readBaseUrl + normalized;
                }
                else if (normalized.Contains("."))
                {
                    normalized = "https://" + normalized.TrimStart('/');
                }
                else
                {
                    normalized = Hentai2readBaseUrl + "/" + normalized.TrimStart('/');
                }
            }

            if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri uri))
            {
                return normalized;
            }

            string absolute = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
            string query = uri.Query ?? string.Empty;
            return absolute + query;
        }

        private Hentai2readListRoute AnalyzeHentai2readRoute(string url)
        {
            string normalized = NormalizeHentai2readUrl(url);
            if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri uri))
            {
                return new Hentai2readListRoute
                {
                    NormalizedListingUrl = normalized,
                    PageBaseUrl = normalized.TrimEnd('/'),
                    SortToken = "last-updated",
                    IsListingRoute = false
                };
            }

            string[] parts = uri.AbsolutePath
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                .ToArray();

            bool isListingRoute =
                parts.Length >= 2 &&
                (
                    (parts[0].Equals("hentai-list", StringComparison.OrdinalIgnoreCase) &&
                     (parts[1].Equals("category", StringComparison.OrdinalIgnoreCase) ||
                      parts[1].Equals("author", StringComparison.OrdinalIgnoreCase) ||
                      parts[1].Equals("artist", StringComparison.OrdinalIgnoreCase) ||
                      parts[1].Equals("character", StringComparison.OrdinalIgnoreCase) ||
                      parts[1].Equals("parody", StringComparison.OrdinalIgnoreCase))) ||
                    parts[0].Equals("group", StringComparison.OrdinalIgnoreCase)
                );

            if (!isListingRoute)
            {
                return new Hentai2readListRoute
                {
                    NormalizedListingUrl = normalized,
                    PageBaseUrl = normalized.TrimEnd('/'),
                    SortToken = "last-updated",
                    IsListingRoute = false
                };
            }

            var cleanParts = new List<string>(parts);
            if (cleanParts.Count > 0 && Regex.IsMatch(cleanParts[cleanParts.Count - 1], @"^\d+$"))
            {
                cleanParts.RemoveAt(cleanParts.Count - 1);
            }

            string sortToken = cleanParts.LastOrDefault(part => Hentai2readSortTokens.Contains(part));
            if (sortToken == null)
            {
                if (cleanParts[0].Equals("hentai-list", StringComparison.OrdinalIgnoreCase))
                {
                    if (!cleanParts.Any(part => part.Equals("all", StringComparison.OrdinalIgnoreCase)))
                    {
                        cleanParts.Add("all");
                    }
                }
                cleanParts.Add("last-updated");
                sortToken = "last-updated";
            }

            string pageBaseUrl = $"{uri.Scheme}://{uri.Host}/" + string.Join("/", cleanParts);
            return new Hentai2readListRoute
            {
                NormalizedListingUrl = pageBaseUrl + "/",
                PageBaseUrl = pageBaseUrl,
                SortToken = sortToken,
                IsListingRoute = true
            };
        }

        private string GetHentai2readPageUrl(string baseUrl, int page)
        {
            var route = AnalyzeHentai2readRoute(baseUrl);
            if (!route.IsListingRoute)
            {
                return route.NormalizedListingUrl;
            }

            return $"{route.PageBaseUrl}/{Math.Max(1, page)}/";
        }

        private bool IsHentai2readChapterToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            string normalized = WebUtility.UrlDecode(token).Trim().Trim('/');
            if (normalized.Equals("login", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("register", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("thumbnails", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("hentai-list", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("group", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("author", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("artist", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("character", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("parody", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("search", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private double ParseHentai2readChapterNumber(string url)
        {
            if (string.IsNullOrEmpty(url)) return 0.0;
            try
            {
                var uri = new Uri(url);
                string[] segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length >= 2)
                {
                    string token = segments[1];
                    if (double.TryParse(token, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double num))
                    {
                        return num;
                    }
                    var match = Regex.Match(token, @"(?<num>\d+(?:\.\d+)?)");
                    if (match.Success && double.TryParse(match.Groups["num"].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double num2))
                    {
                        return num2;
                    }
                }
            }
            catch {}
            return 0.0;
        }

        private bool IsHentai2readStaticImageUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            return Regex.IsMatch(
                url,
                @"^https?://static\.(?:hentaicdn\.com|hentai\.direct)/hentai/\d+/[^/]+/[^/?#]+\.(?:jpg|jpeg|png|webp|bmp|gif)(?:\?.*)?$",
                RegexOptions.IgnoreCase);
        }

        private bool IsHentai2readBookUrl(string url)
        {
            if (!Uri.TryCreate(NormalizeHentai2readUrl(url), UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            string[] segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            return segments.Length == 1 && !segments[0].Equals("group", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsHentai2readChapterUrl(string url)
        {
            if (!Uri.TryCreate(NormalizeHentai2readUrl(url), UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            string[] segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            return segments.Length == 2 && IsHentai2readChapterToken(segments[1]);
        }

        private bool IsHentai2readReaderPageUrl(string url)
        {
            if (!Uri.TryCreate(NormalizeHentai2readUrl(url), UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            string[] segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            return segments.Length >= 3 &&
                   IsHentai2readChapterToken(segments[1]) &&
                   int.TryParse(segments[2], out _);
        }

        private int GetHentai2readMaxPageFromHtml(string html, Hentai2readListRoute route)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return 1;
            }

            if (!route.IsListingRoute)
            {
                return 1;
            }

            int maxPage = 1;
            string pageBase = route.PageBaseUrl.TrimEnd('/') + "/";
            string cleanPageBase = WebUtility.UrlDecode(pageBase.Replace("+", " ")).TrimEnd('/') + "/";
            var hrefMatches = Regex.Matches(html, @"href\s*=\s*[""'](?<href>[^""']+)[""']", RegexOptions.IgnoreCase);
            foreach (Match match in hrefMatches)
            {
                string href = WebUtility.HtmlDecode(match.Groups["href"].Value.Trim());
                if (string.IsNullOrWhiteSpace(href))
                {
                    continue;
                }

                string absolute;
                if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    absolute = href;
                }
                else if (href.StartsWith("/", StringComparison.Ordinal))
                {
                    absolute = Hentai2readBaseUrl + href;
                }
                else
                {
                    continue;
                }

                absolute = NormalizeHentai2readUrl(absolute).TrimEnd('/') + "/";
                string cleanAbsolute = WebUtility.UrlDecode(absolute.Replace("+", " ")).TrimEnd('/') + "/";
                if (!cleanAbsolute.StartsWith(cleanPageBase, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string tail = cleanAbsolute.Substring(cleanPageBase.Length).Trim('/');
                if (int.TryParse(tail, out int pageNum) && pageNum > maxPage)
                {
                    maxPage = pageNum;
                }
            }

            return maxPage;
        }

        private IEnumerable<GalleryItem> ParseHentai2readGalleryItems(string html)
        {
            var items = new List<GalleryItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var matches = Regex.Matches(
                html ?? string.Empty,
                @"<a[^>]+href=""(?<href>https?://(?:www\.)?hentai2read\.com/(?<slug>[^""/?#]+)/?|/(?<slug2>[^""/?#]+)/?)""[^>]*>(?<inner>.*?)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match match in matches)
            {
                string slug = match.Groups["slug"].Success ? match.Groups["slug"].Value : match.Groups["slug2"].Value;
                if (string.IsNullOrWhiteSpace(slug))
                {
                    continue;
                }

                string href = match.Groups["href"].Value.Trim();
                string link = href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? href
                    : Hentai2readBaseUrl + "/" + slug + "/";
                link = NormalizeHentai2readUrl(link).TrimEnd('/') + "/";

                if (slug.Equals("hentai-list", StringComparison.OrdinalIgnoreCase) ||
                    slug.Equals("group", StringComparison.OrdinalIgnoreCase) ||
                    slug.Equals("author", StringComparison.OrdinalIgnoreCase) ||
                    slug.Equals("artist", StringComparison.OrdinalIgnoreCase) ||
                    slug.Equals("character", StringComparison.OrdinalIgnoreCase) ||
                    slug.Equals("parody", StringComparison.OrdinalIgnoreCase) ||
                    slug.Equals("search", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string title = WebUtility.HtmlDecode(Regex.Replace(match.Groups["inner"].Value, @"<[^>]+>", " ")).Trim();
                title = Regex.Replace(title, @"\s+", " ").Trim();
                if (string.IsNullOrWhiteSpace(title) || title.Length < 2)
                {
                    title = slug.Replace("_", " ").Replace("-", " ");
                }

                if (!seen.Add(link))
                {
                    continue;
                }

                items.Add(new GalleryItem
                {
                    Link = link,
                    Name = FormatGalleryTitle(title),
                    OriginalIndex = _scrapedItems.Count + items.Count,
                    IsChecked = false,
                    SourceDomain = "hentai2read.com"
                });
            }

            return items;
        }

        private List<string> ExtractHentai2readChapterLinks(string html, string bookUrl)
        {
            var links = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!Uri.TryCreate(NormalizeHentai2readUrl(bookUrl), UriKind.Absolute, out Uri bookUri))
            {
                return links;
            }

            string[] segments = bookUri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                return links;
            }

            string slug = segments[0];
            string pattern = @"href\s*=\s*[""'](?<href>(?:https?://(?:www\.)?hentai2read\.com)?/" + Regex.Escape(slug) + @"/(?<chapter>[^""'/?#]+)/?)[""']";
            foreach (Match match in Regex.Matches(html ?? string.Empty, pattern, RegexOptions.IgnoreCase))
            {
                string chapterToken = WebUtility.HtmlDecode(match.Groups["chapter"].Value.Trim());
                if (!IsHentai2readChapterToken(chapterToken))
                {
                    continue;
                }

                string href = WebUtility.HtmlDecode(match.Groups["href"].Value.Trim());
                if (!href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    href = Hentai2readBaseUrl + (href.StartsWith("/") ? string.Empty : "/") + href;
                }

                string normalized = NormalizeHentai2readUrl(href).TrimEnd('/') + "/";
                if (seen.Add(normalized))
                {
                    links.Add(normalized);
                }
            }

            return links
                .OrderBy(ParseHentai2readChapterNumber)
                .ThenBy(link => link, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<string> ExtractHentai2readReaderPageLinks(string html, string chapterUrl)
        {
            var links = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string chapterBase = NormalizeHentai2readUrl(chapterUrl).TrimEnd('/');
            string escapedBase = Regex.Escape(chapterBase);

            string[] patterns =
            {
                @"href\s*=\s*[""'](?<href>" + escapedBase + @"/(?<page>\d+)/?)[""']",
                @"href\s*=\s*[""'](?<href>(?:https?://(?:www\.)?hentai2read\.com)?(?<path>/[^""']+/" + Regex.Escape(chapterBase.Split('/').Last()) + @"/(?<page>\d+)/?))[""']"
            };

            foreach (string pattern in patterns)
            {
                foreach (Match match in Regex.Matches(html ?? string.Empty, pattern, RegexOptions.IgnoreCase))
                {
                    string href = WebUtility.HtmlDecode(match.Groups["href"].Value.Trim());
                    if (string.IsNullOrWhiteSpace(href))
                    {
                        continue;
                    }

                    if (!href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                        !href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        href = Hentai2readBaseUrl + (href.StartsWith("/") ? string.Empty : "/") + href;
                    }

                    string normalized = NormalizeHentai2readUrl(href).TrimEnd('/') + "/";
                    if (seen.Add(normalized))
                    {
                        links.Add(normalized);
                    }
                }
            }

            return links
                .OrderBy(link =>
                {
                    string[] pathSegments = new Uri(link).AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    return pathSegments.Length >= 3 && int.TryParse(pathSegments[2], out int pageNum) ? pageNum : int.MaxValue;
                })
                .ToList();
        }

        private List<string> ExtractHentai2readDirectImageUrls(string html)
        {
            var imageUrls = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string content = (html ?? string.Empty).Replace("\\/", "/");
            foreach (Match match in Regex.Matches(
                content,
                @"(?<url>https?://static\.(?:hentaicdn\.com|hentai\.direct)/hentai/\d+/[^""'\s<>]+?\.(?:jpg|jpeg|png|webp|bmp|gif)(?:\?[^""'\s<>]*)?)",
                RegexOptions.IgnoreCase))
            {
                string url = WebUtility.HtmlDecode(match.Groups["url"].Value.Trim());
                if (seen.Add(url))
                {
                    imageUrls.Add(url);
                }
            }

            foreach (Match match in Regex.Matches(
                content,
                @"['""]images['""]\s*:\s*\[(?<images>.*?)\]",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                string imagesBlock = match.Groups["images"].Value;
                foreach (Match imageMatch in Regex.Matches(
                    imagesBlock,
                    @"[""'](?<path>/\d+/[^""']+?\.(?:jpg|jpeg|png|webp|bmp|gif))[""']",
                    RegexOptions.IgnoreCase))
                {
                    string relativePath = WebUtility.HtmlDecode(imageMatch.Groups["path"].Value.Trim()).Replace("\\/", "/");
                    string url = "https://static.hentaicdn.com/hentai" + relativePath;
                    if (seen.Add(url))
                    {
                        imageUrls.Add(url);
                    }
                }
            }

            return imageUrls;
        }

        private async Task<string> ResolveHentai2readReaderImageUrlAsync(string readerPageUrl, CancellationToken token)
        {
            string html = await FetchStringAsync(readerPageUrl, token);
            string imageUrl = ExtractHentai2readDirectImageUrls(html).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                throw new Exception($"Không lấy được ảnh từ reader page: {readerPageUrl}");
            }

            return imageUrl;
        }

        private async void BtnHentai2readFetchInfo_Click(object sender, RoutedEventArgs e)
        {
            var route = AnalyzeHentai2readRoute(txtHentai2readTagUrl.Text);
            txtHentai2readTagUrl.Text = route.NormalizedListingUrl;

            btnHentai2readFetchInfo.IsEnabled = false;
            progressBar.IsIndeterminate = true;
            lblStatus.Text = "Đang phân tích trang hentai2read...";

            try
            {
                string html = await FetchStringAsync(route.NormalizedListingUrl, _downloadCts?.Token ?? CancellationToken.None);
                int maxPage = GetHentai2readMaxPageFromHtml(html, route);
                txtHentai2readTotalPages.Text = maxPage.ToString();
                txtHentai2readPageTo.Text = maxPage.ToString();
                Hentai2readLog($"Phân tích xong. Sort={route.SortToken}. Max page={maxPage}.");
                lblStatus.Text = $"Analysis complete. Found {maxPage} pages.";
            }
            catch (Exception ex)
            {
                txtHentai2readTotalPages.Text = "1";
                txtHentai2readPageTo.Text = "1";
                Hentai2readLog($"Lỗi phân tích: {ex.Message}");
                lblStatus.Text = "Analysis failed.";
            }
            finally
            {
                btnHentai2readFetchInfo.IsEnabled = true;
                progressBar.IsIndeterminate = false;
            }
        }

        private void TxtHentai2readTotalPages_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (txtHentai2readPageTo != null && txtHentai2readTotalPages != null)
            {
                txtHentai2readPageTo.Text = txtHentai2readTotalPages.Text;
            }
        }

        private async void BtnHentai2readScrape_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                btnHentai2readScrape.Content = "CANCELLING...";
                btnHentai2readScrape.IsEnabled = false;
                btnHentai2readCrawlMore.IsEnabled = false;
                return;
            }

            await ScrapeHentai2readAsync(clearExisting: true);
        }

        private async void BtnHentai2readCrawlMore_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                btnHentai2readCrawlMore.Content = "CANCELLING...";
                btnHentai2readCrawlMore.IsEnabled = false;
                btnHentai2readScrape.IsEnabled = false;
                return;
            }

            await ScrapeHentai2readAsync(clearExisting: false);
        }

        private async Task ScrapeHentai2readAsync(bool clearExisting)
        {
            var route = AnalyzeHentai2readRoute(txtHentai2readTagUrl.Text);
            txtHentai2readTagUrl.Text = route.NormalizedListingUrl;

            if (!route.IsListingRoute)
            {
                await ImportHentai2readDirectLinksAsync(new List<string> { route.NormalizedListingUrl });
                return;
            }

            if (!int.TryParse(txtHentai2readPageFrom.Text, out int pageFrom) || pageFrom < 1)
            {
                MessageBox.Show("Trang bắt đầu không hợp lệ.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!int.TryParse(txtHentai2readPageTo.Text, out int pageTo) || pageTo < pageFrom)
            {
                MessageBox.Show("Trang kết thúc không hợp lệ.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            btnHentai2readScrape.Content = "STOP CRAWLER";
            btnHentai2readCrawlMore.Content = "STOP CRAWLER";
            btnHentai2readFetchInfo.IsEnabled = false;
            progressBar.Value = 0;
            lblStatus.Text = "Đang cào hentai2read...";

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
                int totalPages = pageTo - pageFrom + 1;
                for (int page = pageFrom; page <= pageTo; page++)
                {
                    token.ThrowIfCancellationRequested();
                    string pageUrl = GetHentai2readPageUrl(route.NormalizedListingUrl, page);
                    Hentai2readLog($"Đang tải trang {page}: {pageUrl}");
                    string html = await FetchStringAsync(pageUrl, _downloadCts?.Token ?? CancellationToken.None);

                    int before = _scrapedItems.Count;
                    foreach (var item in ParseHentai2readGalleryItems(html))
                    {
                        if (_scrapedItems.Any(existing => existing.Link.Equals(item.Link, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        _scrapedItems.Add(item);
                    }

                    int added = _scrapedItems.Count - before;
                    double progress = ((double)(page - pageFrom + 1) / totalPages) * 100d;
                    progressBar.Value = progress;
                    lblLinkCount.Text = _scrapedItems.Count.ToString();
                    lblStatus.Text = $"Scanning page {page}/{pageTo} ({progress:0}%)";
                    Hentai2readLog($"Trang {page} xong. Thêm {added} link.");
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
                Hentai2readLog($"Lỗi cào: {ex.Message}");
                lblStatus.Text = "Crawling failed.";
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                btnHentai2readScrape.Content = "GET LINK";
                btnHentai2readScrape.IsEnabled = true;
                btnHentai2readCrawlMore.Content = "GET MORE";
                btnHentai2readCrawlMore.IsEnabled = true;
                btnHentai2readFetchInfo.IsEnabled = true;
            }
        }

        private void BtnHentai2readPasteDirect_Click(object sender, RoutedEventArgs e)
        {
            var win = new DirectDownloadWindow(
                isNhentai: false,
                customTitle: "PASTE HENTAI2READ LINKS",
                customDescription: "Paste book/chapter/image-page links của hentai2read.com, mỗi dòng 1 link.",
                customExample: "Example:\nhttps://hentai2read.com/an_ancient_tradition_young_wife_is_harassed/\nhttps://hentai2read.com/an_ancient_tradition_young_wife_is_harassed/1.5/\nhttps://hentai2read.com/an_ancient_tradition_young_wife_is_harassed/1.5/1/")
            {
                Owner = this
            };
            win.OnImport = async links => await ImportHentai2readDirectLinksAsync(links);
            win.Show();
        }

        private async Task ImportHentai2readDirectLinksAsync(List<string> links)
        {
            btnHentai2readScrape.IsEnabled = false;
            btnHentai2readFetchInfo.IsEnabled = false;
            progressBar.Value = 0;
            progressBar.IsIndeterminate = false;

            try
            {
                for (int i = 0; i < links.Count; i++)
                {
                    string link = NormalizeHentai2readUrl(links[i]).TrimEnd('/') + "/";
                    string title = "Hentai2Read";

                    try
                    {
                        string html = await FetchStringAsync(link, _downloadCts?.Token ?? CancellationToken.None);
                        var titleMatch = Regex.Match(html, @"<title[^>]*>\s*(?<title>.*?)\s*</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                        if (titleMatch.Success)
                        {
                            title = WebUtility.HtmlDecode(Regex.Replace(titleMatch.Groups["title"].Value, @"<[^>]+>", string.Empty)).Trim();
                            title = Regex.Replace(title, @"\s*[-|]\s*Hentai2Read\s*$", string.Empty, RegexOptions.IgnoreCase).Trim();
                            int readIdx = title.IndexOf(" - Read ", StringComparison.OrdinalIgnoreCase);
                            if (readIdx >= 0)
                            {
                                title = title.Substring(0, readIdx).Trim();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Hentai2readLog($"Import fallback {link}: {ex.Message}");
                    }

                    if (!_scrapedItems.Any(item => item.Link.Equals(link, StringComparison.OrdinalIgnoreCase)))
                    {
                        _scrapedItems.Add(new GalleryItem
                        {
                            Link = link,
                            Name = FormatGalleryTitle(title),
                            OriginalIndex = _scrapedItems.Count,
                            IsChecked = true,
                            SourceDomain = "hentai2read.com"
                        });
                    }

                    progressBar.Value = ((double)(i + 1) / Math.Max(1, links.Count)) * 100d;
                    lblLinkCount.Text = _scrapedItems.Count.ToString();
                }

                RecalculateDuplicates();
            }
            finally
            {
                btnHentai2readScrape.IsEnabled = true;
                btnHentai2readFetchInfo.IsEnabled = true;
            }
        }

        internal Task<bool> SolveHentai2readCaptchaIfNeededAsync(string testUrl)
        {
            return Task.FromResult(true);
        }

        private async Task DownloadHentai2readGalleryAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem = null, ChapterFilter chapterFilter = null)
        {
            item.Link = NormalizeHentai2readUrl(item.Link).TrimEnd('/') + "/";

            if (IsHentai2readStaticImageUrl(item.Link))
            {
                await DownloadHentai2readDirectImageAsync(item, rootFolder, token, queueItem);
                return;
            }

            if (IsHentai2readReaderPageUrl(item.Link))
            {
                await DownloadHentai2readReaderPageAsync(item, rootFolder, token, queueItem);
                return;
            }

            if (IsHentai2readChapterUrl(item.Link))
            {
                await DownloadHentai2readChapterAsync(item, rootFolder, token, queueItem, isParentQueue: false);
                return;
            }

            if (IsHentai2readBookUrl(item.Link))
            {
                await DownloadHentai2readBookAsync(item, rootFolder, token, queueItem, chapterFilter);
                return;
            }

            throw new Exception("Link hentai2read không hợp lệ. Cần link book, chapter, image-page, hoặc static image.");
        }

        private async Task DownloadHentai2readBookAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem, ChapterFilter chapterFilter)
        {
            string html = await FetchStringAsync(item.Link, token);

            var titleMatch = Regex.Match(html, @"<title[^>]*>\s*(?<title>.*?)\s*</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (titleMatch.Success)
            {
                string rawTitle = WebUtility.HtmlDecode(Regex.Replace(titleMatch.Groups["title"].Value, @"<[^>]+>", string.Empty)).Trim();
                rawTitle = Regex.Replace(rawTitle, @"\s*[-|]\s*Hentai2Read\s*$", string.Empty, RegexOptions.IgnoreCase).Trim();
                if (!string.IsNullOrWhiteSpace(rawTitle))
                {
                    int readIdx = rawTitle.IndexOf(" - Read ", StringComparison.OrdinalIgnoreCase);
                    if (readIdx >= 0)
                    {
                        rawTitle = rawTitle.Substring(0, readIdx).Trim();
                    }
                    item.Name = FormatGalleryTitle(rawTitle);
                }
            }

            var chapterLinks = ExtractHentai2readChapterLinks(html, item.Link);
            if (chapterLinks.Count == 0)
            {
                await DownloadHentai2readChapterAsync(item, rootFolder, token, queueItem, isParentQueue: false);
                return;
            }

            if (chapterFilter != null)
            {
                chapterLinks = chapterLinks
                    .Where(link => chapterFilter.IsMatch(ParseHentai2readChapterNumber(link)))
                    .ToList();
            }
            else
            {
                chapterLinks = FilterPendingChapterLinksFromProcess(rootFolder, Hentai2readSiteFolder, item, chapterLinks);
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
            for (int i = 0; i < chapterLinks.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var chapterItem = new GalleryItem
                {
                    Link = chapterLinks[i],
                    Name = item.Name,
                    SourceDomain = Hentai2readSiteFolder
                };

                bool chapterCompleted = await DownloadHentai2readChapterAsync(chapterItem, rootFolder, token, queueItem, isParentQueue: true);
                if (chapterCompleted)
                {
                    MarkChapterProcessDone(rootFolder, Hentai2readSiteFolder, item, chapterLinks[i]);
                    completedCount++;
                }

                if (queueItem != null && chapterCompleted)
                {
                    Dispatcher.Invoke(() => queueItem.CompletedChapters = completedCount);
                }
            }
        }

        private async Task<bool> DownloadHentai2readChapterAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem, bool isParentQueue)
        {
            string chapterUrl = NormalizeHentai2readUrl(item.Link).TrimEnd('/') + "/";
            string html = await FetchStringAsync(chapterUrl, token);

            string[] pathSegments = new Uri(chapterUrl).AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            string bookSlug = pathSegments.Length >= 1 ? pathSegments[0] : "book";
            string chapterToken = pathSegments.Length >= 2 ? pathSegments[1] : "1";

            string bookTitle = !string.IsNullOrWhiteSpace(item.Name)
                ? item.Name
                : FormatGalleryTitle(bookSlug.Replace("_", " ").Replace("-", " "));
            string chapterTitle = NormalizeChapterLabel("Chapter " + chapterToken.Replace("-", "."));

            var breadcrumbMatch = Regex.Match(html, @"itemprop=""name"">\s*(Chapter\s+[^<]+)\s*</span>", RegexOptions.IgnoreCase);
            if (breadcrumbMatch.Success)
            {
                chapterTitle = WebUtility.HtmlDecode(breadcrumbMatch.Groups[1].Value).Trim();
            }
            else
            {
                var titleMatch = Regex.Match(html, @"<title[^>]*>\s*(?<title>.*?)\s*</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (titleMatch.Success)
                {
                    string rawTitle = WebUtility.HtmlDecode(Regex.Replace(titleMatch.Groups["title"].Value, @"<[^>]+>", string.Empty)).Trim();
                    rawTitle = Regex.Replace(rawTitle, @"\s*[-|]\s*Hentai2Read\s*$", string.Empty, RegexOptions.IgnoreCase).Trim();
                    if (!string.IsNullOrWhiteSpace(rawTitle))
                    {
                        int readIdx = rawTitle.IndexOf(" - Read ", StringComparison.OrdinalIgnoreCase);
                        if (readIdx >= 0)
                        {
                            rawTitle = rawTitle.Substring(0, readIdx).Trim();
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(rawTitle))
                    {
                        string[] parts = rawTitle.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToArray();
                        parts = parts.Where(p => !Regex.IsMatch(p, @"\bPage\s+\d+", RegexOptions.IgnoreCase)).ToArray();
                        if (parts.Length >= 2)
                        {
                            bookTitle = FormatGalleryTitle(parts[0]);
                            chapterTitle = NormalizeChapterLabel(parts[parts.Length - 1]);
                        }
                        else if (parts.Length == 1)
                        {
                            bookTitle = FormatGalleryTitle(parts[0]);
                        }
                    }
                }
            }

            var imageUrls = ExtractHentai2readDirectImageUrls(html);
            if (imageUrls.Count == 0)
            {
                var readerPageLinks = ExtractHentai2readReaderPageLinks(html, chapterUrl);
                foreach (string readerPageUrl in readerPageLinks)
                {
                    token.ThrowIfCancellationRequested();
                    string imageUrl = await ResolveHentai2readReaderImageUrlAsync(readerPageUrl, token);
                    imageUrls.Add(imageUrl);
                }
            }

            if (imageUrls.Count == 0)
            {
                throw new Exception("Không tìm thấy ảnh chapter hentai2read.");
            }

            string safeBook = GetCanonicalBookFolderName(item, bookTitle, "Unknown Book");
            string aliasSafeBook = GetSafePathName(bookTitle);
            string safeChapter = GetDownloadChapterFolderName(bookTitle, chapterTitle);
            string siteRootFolder = GetSiteDownloadRoot(rootFolder, Hentai2readSiteFolder);
            await NormalizeChapterFolderAliasAsync(siteRootFolder, safeBook, aliasSafeBook, safeChapter, token);

            string unmergedPath = Path.Combine(siteRootFolder, $"{safeBook}-{safeChapter}");
            string mergedPath = Path.Combine(siteRootFolder, safeBook, safeChapter);
            string finalTargetFolder = _isSingleComicFolderType ? mergedPath : unmergedPath;
            string tempFolder = BuildStableTempFolderPath(siteRootFolder, Hentai2readSiteFolder, safeBook, safeChapter, chapterUrl);
            Directory.CreateDirectory(tempFolder);
            RegisterTempFolder(tempFolder);

            string progressKey = $"hentai2read.com|{safeBook}";
            int totalChaptersForLog = queueItem != null ? Math.Max(1, queueItem.TotalChapters) : 1;
            int currentChapterForLog = queueItem != null ? Math.Max(1, Math.Min(queueItem.CompletedChapters + 1, totalChaptersForLog)) : 1;
            UpsertMainLogLine(progressKey, $"[hentai2read.com] Đang tải {bookTitle} - {chapterTitle} ({currentChapterForLog}/{totalChaptersForLog})");
            WriteTempProgressLog(tempFolder, item, "Downloading", 0, imageUrls.Count, "0/0 pages", $"Bắt đầu tải {chapterTitle}");

            int maxThreads = GetCurrentConnectionLimit();
            if (queueItem != null && !isParentQueue)
            {
                Dispatcher.Invoke(() =>
                {
                    queueItem.TotalChapters = imageUrls.Count;
                    queueItem.CompletedChapters = 0;
                });
            }

            var pageFilenames = DetermineImageFilenames(imageUrls);

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
                            if (queueItem != null && queueItem.IsStopped) throw new OperationCanceledException();
                            await Task.Delay(200, token);
                        }
                        token.ThrowIfCancellationRequested();

                        await semaphore.WaitAsync(token);
                        try
                        {
                            string fileName = pageFilenames[index];
                            string localFilePath = Path.Combine(tempFolder, fileName);
                            string unmergedFilePath = Path.Combine(unmergedPath, fileName);
                            string mergedFilePath = Path.Combine(mergedPath, fileName);

                            if ((File.Exists(localFilePath) && new FileInfo(localFilePath).Length > 1024) ||
                                (File.Exists(unmergedFilePath) && new FileInfo(unmergedFilePath).Length > 1024) ||
                                (File.Exists(mergedFilePath) && new FileInfo(mergedFilePath).Length > 1024))
                            {
                                pageWatch.Stop();
                                lock (lockObj)
                                {
                                    completedPages++;
                                    string processText = isParentQueue ? $"{chapterTitle} (trang {completedPages}/{imageUrls.Count})" : $"Trang {completedPages}/{imageUrls.Count}";
                                    UpdateDownloadRowMetrics(queueItem, completedPages, imageUrls.Count, processText, 0, 0, isParentQueue);
                                    WriteTempProgressLog(tempFolder, item, "Downloading", completedPages, imageUrls.Count, processText, $"Trang {index + 1} đã có sẵn", imgUrl);
                                }
                                return;
                            }

                            string downloadedPath = null;
                            try
                            {
                                await DownloadUrlToFileWithRefererAsync(imgUrl, chapterUrl, localFilePath, token);
                                downloadedPath = localFilePath;
                            }
                            catch (Exception ex)
                            {
                                lock (lockObj)
                                {
                                    if (queueItem != null)
                                    {
                                        string pageName = Path.GetFileNameWithoutExtension(pageFilenames[index]);
                                        queueItem.AddError(chapterTitle, index + 1, ex.Message, imgUrl, chapterUrl, pageName);
                                        RecordCheckError(Hentai2readSiteFolder, queueItem.Name ?? bookTitle, chapterTitle, index + 1, ex.Message, imgUrl, pageName);
                                    }
                                }
                            }

                            pageWatch.Stop();
                            lock (lockObj)
                            {
                                completedPages++;
                                long downloadedBytes = !string.IsNullOrWhiteSpace(downloadedPath) && File.Exists(downloadedPath) ? new FileInfo(downloadedPath).Length : 0;
                                string processText = isParentQueue ? $"{chapterTitle} (trang {completedPages}/{imageUrls.Count})" : $"Trang {completedPages}/{imageUrls.Count}";
                                UpdateDownloadRowMetrics(queueItem, completedPages, imageUrls.Count, processText, downloadedBytes, pageWatch.ElapsedMilliseconds, isParentQueue);
                                WriteTempProgressLog(tempFolder, item, "Downloading", completedPages, imageUrls.Count, processText, $"Trang {index + 1} hoàn tất", imgUrl);
                            }
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, token));
                }

                await Task.WhenAll(tasks);

                if (Directory.Exists(tempFolder))
                {
                    WriteTempProgressLog(tempFolder, item, "Done", imageUrls.Count, imageUrls.Count, isParentQueue ? $"{chapterTitle} (trang {imageUrls.Count}/{imageUrls.Count})" : $"Trang {imageUrls.Count}/{imageUrls.Count}", "Download completed");
                }

                MoveTempFolderToTarget(tempFolder, finalTargetFolder, "hentai2read");
                if (_isSingleComicFolderType)
                {
                    await NormalizeChapterFolderAliasAsync(siteRootFolder, safeBook, aliasSafeBook, safeChapter, token);
                }
                UpsertMainLogLine(progressKey, $"[hentai2read.com] Đã tải xong {bookTitle} - {chapterTitle} ({currentChapterForLog}/{totalChaptersForLog})");

                return ValidateDownloadedFiles(finalTargetFolder, imageUrls.Count, queueItem ?? item, chapterTitle, chapterUrl: item.Link);
            }
        }

        private async Task DownloadHentai2readReaderPageAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem)
        {
            string readerPageUrl = NormalizeHentai2readUrl(item.Link).TrimEnd('/') + "/";
            string imageUrl = await ResolveHentai2readReaderImageUrlAsync(readerPageUrl, token);

            var directItem = new GalleryItem
            {
                Link = imageUrl,
                Name = item.Name,
                SourceDomain = Hentai2readSiteFolder
            };

            await DownloadHentai2readDirectImageAsync(directItem, rootFolder, token, queueItem, readerPageUrl);
        }

        private async Task DownloadHentai2readDirectImageAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem, string refererUrl = null)
        {
            string imageUrl = item.Link;
            string fileName = BuildOrderedImageFilename(1, imageUrl);
            string title = !string.IsNullOrWhiteSpace(item.Name) ? item.Name : Path.GetFileNameWithoutExtension(fileName);
            string safeTitle = GetSafePathName(title);
            string resolvedRoot = GetConfiguredDownloadRoot(rootFolder, item);
            string targetFolder = Path.Combine(resolvedRoot, safeTitle);
            Directory.CreateDirectory(targetFolder);

            string finalPath = Path.Combine(targetFolder, fileName);
            await DownloadUrlToFileWithRefererAsync(imageUrl, refererUrl ?? Hentai2readBaseUrl + "/", finalPath, token);

            if (queueItem != null)
            {
                Dispatcher.Invoke(() =>
                {
                    queueItem.TotalChapters = 1;
                    queueItem.CompletedChapters = 1;
                    queueItem.CurrentProcess = "1/1 pages";
                });
            }
        }
    }
}

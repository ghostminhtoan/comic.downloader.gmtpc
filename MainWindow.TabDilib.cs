using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
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
        private const string DilibSiteFolder = "dilib.vn";
        private const string DilibBaseUrl = "https://dilib.vn";
        private const string DilibDefaultCategoryUrl = "https://dilib.vn/truyen-tranh/shounen/";

        private void DilibLog(string message)
        {
            Log("[dilib.vn] " + message);
        }

        private bool IsDilibUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            try
            {
                var uri = new Uri(url);
                return uri.Host.IndexOf("dilib.vn", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return url.IndexOf("dilib.vn", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        private bool IsDilibCategoryUrl(string url)
        {
            if (!IsDilibUrl(url))
            {
                return false;
            }

            try
            {
                var uri = new Uri(NormalizeDilibUrl(url));
                string path = uri.AbsolutePath.ToLowerInvariant();
                return path.Contains("/truyen-tranh/");
            }
            catch
            {
                return false;
            }
        }

        private bool IsDilibBookUrl(string url)
        {
            if (!IsDilibUrl(url))
            {
                return false;
            }

            try
            {
                var uri = new Uri(NormalizeDilibUrl(url));
                string path = uri.AbsolutePath.TrimEnd('/').ToLowerInvariant();
                return path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) &&
                       !path.Contains("/truyen-tranh/");
            }
            catch
            {
                return false;
            }
        }

        private bool IsDilibChapterUrl(string url)
        {
            if (!IsDilibUrl(url))
            {
                return false;
            }

            try
            {
                var uri = new Uri(NormalizeDilibUrl(url));
                string path = uri.AbsolutePath.ToLowerInvariant();
                return path.Contains("/truyen-tranh/") && path.Contains("-chap-") && path.EndsWith(".html", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private string NormalizeDilibUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            string normalized = url.Trim();
            if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                normalized = DilibBaseUrl + (normalized.StartsWith("/") ? string.Empty : "/") + normalized;
            }

            if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri uri))
            {
                throw new ArgumentException("URL dilib.vn không hợp lệ.");
            }

            if (uri.Host.IndexOf("dilib.vn", StringComparison.OrdinalIgnoreCase) < 0)
            {
                throw new ArgumentException("URL phải thuộc domain dilib.vn.");
            }

            return uri.AbsoluteUri.TrimEnd('/');
        }

        private string GetDilibCategoryPageUrl(string baseUrl, int page)
        {
            string normalized = NormalizeDilibUrl(baseUrl);
            var uri = new Uri(normalized);
            string path = Regex.Replace(uri.AbsolutePath.TrimEnd('/'), @"/page/\d+$", string.Empty, RegexOptions.IgnoreCase).TrimEnd('/');
            if (page > 1)
            {
                path = path + "/page/" + page;
            }
            else
            {
                path = path + "/";
            }

            return new UriBuilder(uri)
            {
                Path = path
            }.Uri.AbsoluteUri;
        }

        private string HumanizeDilibSlug(string value)
        {
            string cleaned = (value ?? string.Empty).Trim().Trim('/');
            cleaned = Regex.Replace(cleaned, @"-chap-\d+$", string.Empty, RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"-\d+$", string.Empty);
            cleaned = cleaned.Replace('-', ' ');
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return "Dilib";
            }

            return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(cleaned.ToLowerInvariant());
        }

        private string CleanDilibDisplayTitle(string title)
        {
            string cleaned = WebUtility.HtmlDecode(title ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return string.Empty;
            }

            cleaned = Regex.Replace(cleaned, @"^\s*truyện\s+tranh\s+", string.Empty, RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\s*[,|-]\s*thư\s+viện\s+số\s*$", string.Empty, RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\s*-\s*truyện\s+tranh\s*$", string.Empty, RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
            return cleaned;
        }

        internal void InitializeDilibDefaults()
        {
            if (txtDilibTagUrl != null && string.IsNullOrWhiteSpace(txtDilibTagUrl.Text))
            {
                txtDilibTagUrl.Text = DilibDefaultCategoryUrl;
            }
        }

        private void TxtDilibTagUrl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText()?.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        txtDilibTagUrl.Text = text;
                        txtDilibTagUrl.CaretIndex = txtDilibTagUrl.Text.Length;
                        e.Handled = true;
                    }
                }
                return;
            }

            if (e.Key == Key.Enter)
            {
                BtnDilibFetchInfo_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private async void BtnDilibFetchInfo_Click(object sender, RoutedEventArgs e)
        {
            await AnalyzeDilibUrlAsync(txtDilibTagUrl?.Text);
        }

        private void TxtDilibTotalPages_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (txtDilibPageTo != null && txtDilibTotalPages != null)
            {
                txtDilibPageTo.Text = txtDilibTotalPages.Text;
            }
        }

        private async void BtnDilibScrape_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                btnDilibScrape.Content = "CANCELLING...";
                btnDilibScrape.IsEnabled = false;
                btnDilibCrawlMore.IsEnabled = false;
                return;
            }

            await ScrapeDilibAsync(clearExisting: true);
        }

        private async void BtnDilibCrawlMore_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                btnDilibCrawlMore.Content = "CANCELLING...";
                btnDilibCrawlMore.IsEnabled = false;
                btnDilibScrape.IsEnabled = false;
                return;
            }

            await ScrapeDilibAsync(clearExisting: false);
        }

        private void BtnDilibPasteDirect_Click(object sender, RoutedEventArgs e)
        {
            var window = new DirectDownloadWindow(
                customTitle: "PASTE DILIB LINKS",
                customDescription: "Paste dilib.vn category, book, or chapter links below. The system will crawl the right level automatically.",
                customExample:
                    "Example:\nhttps://dilib.vn/truyen-tranh/shounen/\nhttps://dilib.vn/hoang-tu-tennis-prince-of-tennis-15443.html\nhttps://dilib.vn/truyen-tranh/hoang-tu-tennis-prince-of-tennis-15443-chap-1.html")
            {
                Owner = this
            };

            window.OnImport = async links => await ImportDilibDirectLinksAsync(links, clearExisting: false);
            window.ShowDialog();
        }

        private async Task AnalyzeDilibUrlAsync(string rawUrl)
        {
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                MessageBox.Show("Vui lòng nhập URL hợp lệ.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                string normalized = NormalizeDilibUrl(rawUrl);
                txtDilibTagUrl.Text = normalized;

                if (IsDilibCategoryUrl(normalized))
                {
                    string html = await FetchStringAsync(GetDilibCategoryPageUrl(normalized, 1), _downloadCts?.Token ?? CancellationToken.None);
                    int totalPages = GetDilibCategoryMaxPage(html);
                    txtDilibTotalPages.Text = Math.Max(1, totalPages).ToString();
                    txtDilibPageFrom.Text = "1";
                    txtDilibPageTo.Text = Math.Max(1, totalPages).ToString();
                    lblStatus.Text = $"Dilib category: {totalPages} pages.";
                }
                else if (IsDilibBookUrl(normalized))
                {
                    string html = await FetchStringAsync(normalized, _downloadCts?.Token ?? CancellationToken.None);
                    int totalChapters = ExtractDilibChapterLinksFromBookHtml(html, normalized).Count;
                    txtDilibTotalPages.Text = Math.Max(1, totalChapters).ToString();
                    txtDilibPageFrom.Text = "1";
                    txtDilibPageTo.Text = Math.Max(1, totalChapters).ToString();
                    lblStatus.Text = $"Dilib book: {totalChapters} chapters.";
                }
                else if (IsDilibChapterUrl(normalized))
                {
                    txtDilibTotalPages.Text = "1";
                    txtDilibPageFrom.Text = "1";
                    txtDilibPageTo.Text = "1";
                    lblStatus.Text = "Dilib chapter ready.";
                }
                else
                {
                    MessageBox.Show("URL dilib không hợp lệ.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                DilibLog("Lỗi phân tích: " + ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                txtDilibTotalPages.Text = "1";
                txtDilibPageFrom.Text = "1";
                txtDilibPageTo.Text = "1";
            }
        }

        private async Task ScrapeDilibAsync(bool clearExisting)
        {
            string rawUrl = txtDilibTagUrl?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                MessageBox.Show("Vui lòng nhập URL hợp lệ.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            btnDilibScrape.Content = "STOP CRAWLER";
            btnDilibCrawlMore.Content = "STOP CRAWLER";
            btnDilibFetchInfo.IsEnabled = false;
            progressBar.Value = 0;
            progressBar.IsIndeterminate = false;

            if (clearExisting)
            {
                _scrapedItems.Clear();
                lblLinkCount.Text = "0";
            }

            try
            {
                await ImportDilibDirectLinksAsync(new List<string> { rawUrl }, clearExisting: false, showMessageBox: true, token: token);
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = "Crawling cancelled.";
            }
            catch (Exception ex)
            {
                DilibLog("Lỗi khi crawl: " + ex.Message);
                lblStatus.Text = "Crawling failed.";
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                btnDilibScrape.Content = "GET LINK";
                btnDilibCrawlMore.Content = "GET MORE";
                btnDilibScrape.IsEnabled = true;
                btnDilibCrawlMore.IsEnabled = true;
                btnDilibFetchInfo.IsEnabled = true;
            }
        }

        private async Task ImportDilibDirectLinksAsync(IReadOnlyList<string> links, bool clearExisting, bool showMessageBox = true, CancellationToken? token = null)
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

            btnDilibFetchInfo.IsEnabled = false;
            progressBar.Value = 0;
            progressBar.IsIndeterminate = false;

            int added = 0;
            int total = links.Count;

            try
            {
                foreach (string rawLink in links)
                {
                    effectiveToken.ThrowIfCancellationRequested();
                    string link = NormalizeDilibUrl(rawLink);
                    txtDilibTagUrl.Text = link;

                    var items = await CreateDilibItemsFromUrlAsync(link, effectiveToken);
                    foreach (var item in items)
                    {
                        if (_scrapedItems.Any(existing => existing.Link.Equals(item.Link, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        item.OriginalIndex = _scrapedItems.Count;
                        _scrapedItems.Add(item);
                        added++;
                    }

                    progressBar.Value = (double)added / Math.Max(1, total) * 100d;
                }

                RecalculateDuplicates();
                lblLinkCount.Text = _scrapedItems.Count.ToString();
                lblStatus.Text = $"Imported {_scrapedItems.Count} items.";

                if (showMessageBox)
                {
                    MessageBox.Show($"Đã nhập {_scrapedItems.Count} mục dilib.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                DilibLog("Lỗi nhập link: " + ex.Message);
                if (showMessageBox)
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                btnDilibFetchInfo.IsEnabled = true;
            }
        }

        private async Task<List<GalleryItem>> CreateDilibItemsFromUrlAsync(string url, CancellationToken token)
        {
            var results = new List<GalleryItem>();
            string normalized = NormalizeDilibUrl(url);

            if (IsDilibCategoryUrl(normalized))
            {
                string baseUrl = GetDilibCategoryPageUrl(normalized, 1);
                string firstPageHtml = await FetchStringAsync(baseUrl, token);
                int totalPages = Math.Max(1, GetDilibCategoryMaxPage(firstPageHtml));

                for (int page = 1; page <= totalPages; page++)
                {
                    token.ThrowIfCancellationRequested();
                    string pageUrl = GetDilibCategoryPageUrl(baseUrl, page);
                    string html = page == 1 ? firstPageHtml : await FetchStringAsync(pageUrl, token);
                    var items = ExtractDilibCategoryItemsFromHtml(html, pageUrl);
                    foreach (var item in items)
                    {
                        if (!results.Any(existing => existing.Link.Equals(item.Link, StringComparison.OrdinalIgnoreCase)))
                        {
                            results.Add(item);
                        }
                    }
                }

                return results;
            }

            if (IsDilibBookUrl(normalized))
            {
                string html = await FetchStringAsync(normalized, token);
                string bookTitle = GetDilibBookTitleFromHtml(html, normalized);
                var chapters = ExtractDilibChapterLinksFromBookHtml(html, normalized);

                results.Add(new GalleryItem
                {
                    Link = normalized,
                    Name = FormatGalleryTitle(bookTitle),
                    LinkCount = chapters.Count > 0 ? chapters.Count + " chapters" : string.Empty,
                    SourceDomain = DilibSiteFolder,
                    OriginalIndex = 0,
                    IsChecked = true
                });

                return results;
            }

            if (IsDilibChapterUrl(normalized))
            {
                string html = await FetchStringAsync(normalized, token);
                string chapterTitle = GetDilibChapterTitleFromHtml(html, normalized);
                string bookTitle = GetDilibBookTitleFromHtml(html, normalized);
                results.Add(new GalleryItem
                {
                    Link = normalized,
                    Name = FormatGalleryTitle($"{bookTitle} - {chapterTitle}"),
                    SourceDomain = DilibSiteFolder,
                    OriginalIndex = 0,
                    IsChecked = true
                });
                return results;
            }

            throw new Exception("URL dilib không hỗ trợ.");
        }

        private int GetDilibCategoryMaxPage(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return 1;
            }

            string scope = ExtractDilibProductsSection(html);
            int maxPage = 1;
            var matches = Regex.Matches(html, @"/page/(?<page>\d+)", RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                if (int.TryParse(match.Groups["page"].Value, out int page) && page > maxPage)
                {
                    maxPage = page;
                }
            }

            var textMatch = Regex.Match(scope, @"Trang\s+\d+\s*/\s*(?<page>\d+)", RegexOptions.IgnoreCase);
            if (textMatch.Success && int.TryParse(textMatch.Groups["page"].Value, out int textPage) && textPage > maxPage)
            {
                maxPage = textPage;
            }

            return maxPage;
        }

        private List<GalleryItem> ExtractDilibCategoryItemsFromHtml(string html, string pageUrl)
        {
            var results = new List<GalleryItem>();
            if (string.IsNullOrWhiteSpace(html))
            {
                return results;
            }

            string scope = ExtractDilibProductsSection(html);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var patterns = new[]
            {
                @"(?is)(?<count>\d+)\s*chap.*?<a[^>]+href=""(?<link>/[^""?#]+?-\d+\.html)""[^>]*>(?<title>.*?)</a>",
                @"(?is)<a[^>]+href=""(?<link>/[^""?#]+?-\d+\.html)""[^>]*>(?<title>.*?)</a>.*?(?<count>\d+)\s*chap"
            };

            foreach (string pattern in patterns)
            {
                foreach (Match match in Regex.Matches(scope, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline))
                {
                    string link = match.Groups["link"].Value.Trim();
                    if (string.IsNullOrWhiteSpace(link) || link.IndexOf("/truyen-tranh/", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        continue;
                    }

                    string normalizedLink = NormalizeDilibUrl(link);
                    if (!seen.Add(normalizedLink))
                    {
                        continue;
                    }

                    string title = WebUtility.HtmlDecode(Regex.Replace(match.Groups["title"].Value, @"<[^>]+>", string.Empty)).Trim();
                    if (string.IsNullOrWhiteSpace(title))
                    {
                        title = HumanizeDilibSlug(Path.GetFileNameWithoutExtension(new Uri(normalizedLink).AbsolutePath));
                    }
                    title = CleanDilibDisplayTitle(title);

                    string count = match.Groups["count"].Success ? match.Groups["count"].Value.Trim() + " chapters" : string.Empty;
                    results.Add(new GalleryItem
                    {
                        Link = normalizedLink,
                        Name = FormatGalleryTitle(title),
                        LinkCount = count,
                        SourceDomain = DilibSiteFolder,
                        OriginalIndex = results.Count,
                        IsChecked = true
                    });
                }

                if (results.Count > 0)
                {
                    return results;
                }
            }

            return results;
        }

        private string ExtractDilibProductsSection(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            Match startMatch = Regex.Match(
                html,
                @"<div[^>]*class=""[^""]*\bproducts\b[^""]*\brow\b[^""]*""[^>]*>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (!startMatch.Success)
            {
                return html;
            }

            int startIndex = startMatch.Index + startMatch.Length;
            int endIndex = html.Length;
            string[] endMarkers = new[]
            {
                @"<nav",
                @"class=""pagination""",
                @"id=""pagination""",
                @"<section",
                @"</main>",
                @"</div>\s*</div>\s*</div>"
            };

            foreach (string marker in endMarkers)
            {
                int markerIndex = html.IndexOf(marker, startIndex, StringComparison.OrdinalIgnoreCase);
                if (markerIndex >= 0 && markerIndex < endIndex)
                {
                    endIndex = markerIndex;
                }
            }

            if (endIndex <= startIndex)
            {
                return html.Substring(startIndex);
            }

            return html.Substring(startIndex, endIndex - startIndex);
        }

        private List<GalleryItem> ExtractDilibChapterLinksFromBookHtml(string html, string bookUrl)
        {
            var results = new List<GalleryItem>();
            if (string.IsNullOrWhiteSpace(html))
            {
                return results;
            }

            string bookTitle = GetDilibBookTitleFromHtml(html, bookUrl);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var matches = Regex.Matches(
                html,
                @"<a[^>]+href=""(?<link>/truyen-tranh/[^""?#]+?-chap-(?<chapter>\d+)\.html)""[^>]*>(?<title>.*?)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match match in matches)
            {
                string link = NormalizeDilibUrl(match.Groups["link"].Value);
                if (!seen.Add(link))
                {
                    continue;
                }

                string chapterTitle = WebUtility.HtmlDecode(Regex.Replace(match.Groups["title"].Value, @"<[^>]+>", string.Empty)).Trim();
                if (string.IsNullOrWhiteSpace(chapterTitle))
                {
                    chapterTitle = "Chapter " + match.Groups["chapter"].Value;
                }
                chapterTitle = CleanDilibDisplayTitle(chapterTitle);

                results.Add(new GalleryItem
                {
                    Link = link,
                    Name = FormatGalleryTitle($"{bookTitle} - {chapterTitle}"),
                    LinkCount = "chapter " + match.Groups["chapter"].Value,
                    SourceDomain = DilibSiteFolder,
                    OriginalIndex = results.Count,
                    IsChecked = true
                });
            }

            if (results.Count > 1)
            {
                results = results
                    .OrderBy(item => ParseChapterNumber(item.Link))
                    .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (results.Count == 0)
            {
                results.Add(new GalleryItem
                {
                    Link = NormalizeDilibUrl(bookUrl),
                    Name = FormatGalleryTitle(bookTitle),
                    SourceDomain = DilibSiteFolder,
                    OriginalIndex = 0,
                    IsChecked = true
                });
            }

            return results;
        }

        private string GetDilibBookTitleFromHtml(string html, string link)
        {
            string title = ExtractDilibTitleFromHtml(html);
            if (!string.IsNullOrWhiteSpace(title))
            {
                return CleanDilibDisplayTitle(title);
            }

            try
            {
                var uri = new Uri(NormalizeDilibUrl(link));
                string slug = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
                slug = Regex.Replace(slug, @"-\d+$", string.Empty);
                return CleanDilibDisplayTitle(HumanizeDilibSlug(slug));
            }
            catch
            {
                return CleanDilibDisplayTitle(HumanizeDilibSlug(link));
            }
        }

        private string GetDilibChapterTitleFromHtml(string html, string link)
        {
            string title = ExtractDilibTitleFromHtml(html);
            if (!string.IsNullOrWhiteSpace(title))
            {
                return CleanDilibDisplayTitle(title);
            }

            try
            {
                var uri = new Uri(NormalizeDilibUrl(link));
                string path = uri.AbsolutePath;
                var match = Regex.Match(path, @"-chap-(?<chapter>\d+)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return CleanDilibDisplayTitle("Chapter " + match.Groups["chapter"].Value);
                }
            }
            catch
            {
            }

            return "Chapter";
        }

        private string ExtractDilibTitleFromHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            var patterns = new[]
            {
                @"<meta[^>]+property=""og:title""[^>]+content=""(?<title>[^""]+)""",
                @"<meta[^>]+name=""title""[^>]+content=""(?<title>[^""]+)""",
                @"<title>(?<title>.*?)</title>",
                @"<h1[^>]*>(?<title>.*?)</h1>"
            };

            foreach (string pattern in patterns)
            {
                var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (!match.Success)
                {
                    continue;
                }

                string title = WebUtility.HtmlDecode(Regex.Replace(match.Groups["title"].Value, @"<[^>]+>", string.Empty)).Trim();
                title = Regex.Replace(title, @"\s*[-|]\s*dilib\.vn.*$", string.Empty, RegexOptions.IgnoreCase).Trim();
                if (!string.IsNullOrWhiteSpace(title))
                {
                    return title;
                }
            }

            return string.Empty;
        }

        private List<string> ExtractDilibImageUrlsFromHtml(string html, string pageUrl = null)
        {
            var results = new List<string>();
            if (string.IsNullOrWhiteSpace(html))
            {
                return results;
            }

            Uri baseUri = null;
            if (!string.IsNullOrWhiteSpace(pageUrl))
            {
                Uri.TryCreate(NormalizeDilibUrl(pageUrl), UriKind.Absolute, out baseUri);
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var matches = Regex.Matches(
                html,
                @"(?:src|data-src|data-original|data-lazy-src|data-url)\s*=\s*[""'](?<url>(?:https?://(?:www\.)?dilib\.vn)?/[^""'?#>]+/img[^""'?#>]+\.(?:webp|gif|jpg|jpeg|png|bmp)(?:\?[^""'<>]*)?)[""']",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match match in matches)
            {
                string imageUrl = ResolveDilibUrl(baseUri, match.Groups["url"].Value.Trim());
                if (string.IsNullOrWhiteSpace(imageUrl))
                {
                    continue;
                }

                string fileName = Path.GetFileName(new Uri(imageUrl).AbsolutePath);
                if (!fileName.StartsWith("img", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (seen.Add(imageUrl))
                {
                    results.Add(imageUrl);
                }
            }

            if (results.Count == 0)
            {
                var fallbackMatches = Regex.Matches(
                    html,
                    @"https?://(?:www\.)?dilib\.vn/[^""'?#>]+/img[^""'?#>]+\.(?:webp|gif|jpg|jpeg|png|bmp)(?:\?[^""'<>]*)?",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);

                foreach (Match match in fallbackMatches)
                {
                    string imageUrl = ResolveDilibUrl(baseUri, match.Value.Trim());
                    string fileName = Path.GetFileName(new Uri(imageUrl).AbsolutePath);
                    if (!fileName.StartsWith("img", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (seen.Add(imageUrl))
                    {
                        results.Add(imageUrl);
                    }
                }
            }

            return results;
        }

        private string ResolveDilibUrl(Uri baseUri, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string normalized = value.Trim().Trim('"', '\'');
            if (normalized.StartsWith("//", StringComparison.Ordinal))
            {
                normalized = "https:" + normalized;
            }

            if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            if (baseUri != null && Uri.TryCreate(baseUri, normalized, out Uri resolved))
            {
                return resolved.AbsoluteUri;
            }

            if (normalized.StartsWith("/"))
            {
                return DilibBaseUrl + normalized;
            }

            return DilibBaseUrl + "/" + normalized;
        }

        private async Task DownloadDilibGalleryAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem = null, ChapterFilter chapterFilter = null)
        {
            string normalized = NormalizeDilibUrl(item.Link);
            if (IsDilibChapterUrl(normalized))
            {
                await DownloadDilibChapterAsync(item, rootFolder, token, queueItem);
                return;
            }

            if (IsDilibBookUrl(normalized))
            {
                if (chapterFilter == null)
                {
                    var pendingFromProcess = LoadPendingChapterLinksFromProcess(rootFolder, DilibSiteFolder, item);
                    if (pendingFromProcess != null)
                    {
                        if (pendingFromProcess.Count == 0)
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

                        await DownloadDilibPendingChaptersAsync(item, rootFolder, token, queueItem, pendingFromProcess);
                        return;
                    }
                }

                await DownloadDilibBookAsync(item, rootFolder, token, queueItem, chapterFilter);
                return;
            }

            throw new Exception("Dilib chỉ hỗ trợ book hoặc chapter link.");
        }

        private async Task DownloadDilibBookAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem, ChapterFilter chapterFilter)
        {
            string normalized = NormalizeDilibUrl(item.Link);
            string html = await FetchStringAsync(normalized, token);
            string bookTitle = GetDilibBookTitleFromHtml(html, normalized);
            item.Name = FormatGalleryTitle(bookTitle);

            var chapters = ExtractDilibChapterLinksFromBookHtml(html, normalized);
            if (chapterFilter != null)
            {
                chapters = chapters.Where(chapter => chapterFilter.IsMatch(ParseChapterNumber(chapter.Link))).ToList();
            }
            else
            {
                chapters = FilterPendingChapterLinksFromProcess(rootFolder, DilibSiteFolder, item, chapters.Select(chapter => chapter.Link).ToList())
                    .Select(link => chapters.First(chapter => string.Equals(chapter.Link, link, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }
            // ponytail: sort theo chapter number sau filter để tránh DOM order lộn xộn.
            chapters = chapters
                .OrderBy(chapter => GetDilibChapterSortNumber(chapter))
                .ThenBy(chapter => chapter.Link, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (queueItem != null)
            {
                Dispatcher.Invoke(() =>
                {
                    queueItem.TotalChapters = Math.Max(1, chapters.Count);
                    queueItem.CompletedChapters = 0;
                });
            }

            if (chapters.Count == 0)
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

            for (int i = 0; i < chapters.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var chapterItem = new GalleryItem
                {
                    Link = chapters[i].Link,
                    Name = chapters[i].Name,
                    SourceDomain = DilibSiteFolder
                };

                bool completed = await DownloadDilibChapterAsync(chapterItem, rootFolder, token, queueItem, isParentQueue: true, bookTitleOverride: bookTitle);
                if (completed)
                {
                    MarkChapterProcessDone(rootFolder, DilibSiteFolder, item, chapters[i].Link);
                }
                if (queueItem != null)
                {
                    int completedCount = i + 1;
                    Dispatcher.Invoke(() => queueItem.CompletedChapters = completedCount);
                }

                if (!completed)
                {
                    break;
                }
            }
        }

        private async Task DownloadDilibPendingChaptersAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem, IList<string> chapterLinks)
        {
            string html = await FetchStringAsync(NormalizeDilibUrl(item.Link), token);
            string bookTitle = GetDilibBookTitleFromHtml(html, item.Link);
            chapterLinks = (chapterLinks ?? Array.Empty<string>())
                .OrderBy(GetDilibChapterSortNumber)
                .ThenBy(link => link, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (queueItem != null)
            {
                Dispatcher.Invoke(() =>
                {
                    queueItem.TotalChapters = Math.Max(1, chapterLinks.Count);
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
                    Name = item.Name,
                    SourceDomain = DilibSiteFolder
                };

                bool completed = await DownloadDilibChapterAsync(chapterItem, rootFolder, token, queueItem, isParentQueue: true, bookTitleOverride: bookTitle);
                if (!completed)
                {
                    break;
                }

                MarkChapterProcessDone(rootFolder, DilibSiteFolder, item, chapterLink);
                completedCount++;
                if (queueItem != null)
                {
                    Dispatcher.Invoke(() => queueItem.CompletedChapters = completedCount);
                }
            }
        }

        private async Task<bool> DownloadDilibChapterAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem = null, bool isParentQueue = false, string bookTitleOverride = null)
        {
            string normalized = NormalizeDilibUrl(item.Link);
                string html = await FetchStringAsync(normalized, token);
            string bookTitle = string.IsNullOrWhiteSpace(bookTitleOverride)
                    ? GetDilibBookTitleFromHtml(html, normalized)
                    : CleanDilibDisplayTitle(bookTitleOverride);
                string chapterTitle = GetDilibChapterTitleFromHtml(html, normalized);
                item.Name = FormatGalleryTitle($"{bookTitle} - {chapterTitle}");

            var imageUrls = ExtractDilibImageUrlsFromHtml(html, normalized);
            if (imageUrls.Count == 0)
            {
                throw new Exception("Không tìm thấy ảnh chapter hợp lệ.");
            }

            string safeBook = GetSafePathName(bookTitle);
            // ponytail: Dilib title on chapter page can be generic; folder key must come from link.
            string safeChapter = GetSafeChapterPathName(GetChapterProcessLabel(normalized));
            string siteRoot = GetSiteDownloadRoot(rootFolder, DilibSiteFolder);
            string targetFolder = _isSingleComicFolderType
                ? Path.Combine(siteRoot, safeBook, safeChapter)
                : Path.Combine(siteRoot, $"{safeBook}-{safeChapter}");
            string tempFolder = BuildStableTempFolderPath(siteRoot, DilibSiteFolder, safeBook, safeChapter, normalized);
            Directory.CreateDirectory(tempFolder);
            RegisterTempFolder(tempFolder);

            try
            {
                if (queueItem != null)
                {
                    string processChapterLabel = GetChapterProcessLabel(normalized);
                    Dispatcher.Invoke(() =>
                    {
                        queueItem.DownloadingChapter = processChapterLabel;
                        queueItem.CurrentProcess = $"Downloading {processChapterLabel}";
                    });
                }

                int maxThreads = GetCurrentConnectionLimit();
                using (var semaphore = new DynamicSemaphore(maxThreads, GetCurrentConnectionLimit))
                {
                    var tasks = new List<Task>();
                    int completedPages = 0;
                    object lockObj = new object();
                    DateTime lastUiUpdateUtc = DateTime.MinValue;

                    for (int i = 0; i < imageUrls.Count; i++)
                    {
                        int pageIndex = i + 1;
                        string imageUrl = imageUrls[i];
                        tasks.Add(Task.Run(async () =>
                        {
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
                                while (_isDownloadPaused || (queueItem != null && queueItem.IsPaused))
                                {
                                    token.ThrowIfCancellationRequested();
                                    if (queueItem != null && queueItem.IsStopped)
                                    {
                                        throw new OperationCanceledException();
                                    }

                                    await Task.Delay(200, token);
                                }

                                string originalName = Path.GetFileName(new Uri(imageUrl).AbsolutePath);
                                if (string.IsNullOrWhiteSpace(originalName))
                                {
                                    originalName = "img.jpg";
                                }
                                string fileName = $"{pageIndex:D4}-{originalName}";

                                string localPath = Path.Combine(tempFolder, fileName);
                                string targetPath = Path.Combine(targetFolder, fileName);
                                if ((File.Exists(localPath) && new FileInfo(localPath).Length > 1024) ||
                                    (File.Exists(targetPath) && new FileInfo(targetPath).Length > 1024))
                                {
                                    lock (lockObj)
                                    {
                                        completedPages++;
                                        bool shouldFlushUi = completedPages == imageUrls.Count ||
                                                             completedPages == 1 ||
                                                             (DateTime.UtcNow - lastUiUpdateUtc).TotalMilliseconds >= 500 ||
                                                             completedPages % 5 == 0;
                                        if (shouldFlushUi)
                                        {
                                            lastUiUpdateUtc = DateTime.UtcNow;
                                            UpdateDownloadRowMetrics(queueItem, completedPages, imageUrls.Count, $"{completedPages}/{imageUrls.Count} pages", 0, 0, isParentQueue);
                                        }
                                    }
                                    return;
                                }

                                var watch = System.Diagnostics.Stopwatch.StartNew();
                                string downloadedPath = null;
                                try
                                {
                                    await DownloadUrlToFileWithRefererAsync(imageUrl, normalized, localPath, token);
                                    downloadedPath = localPath;
                                }
                                catch (Exception ex)
                                {
                                    lock (lockObj)
                                    {
                                        if (queueItem != null)
                                        {
                                            string pageName = Path.GetFileNameWithoutExtension(fileName);
                                            queueItem.AddError(chapterTitle, pageIndex, ex.Message, imageUrl, normalized, pageName);
                                            RecordCheckError("dilib", queueItem.Name ?? bookTitle, chapterTitle, pageIndex, ex.Message, imageUrl, pageName);
                                        }
                                        Log($"[dilib] Lỗi tải trang {pageIndex} của chapter '{chapterTitle}': {ex.Message}");
                                    }
                                }

                                lock (lockObj)
                                {
                                    completedPages++;
                                    watch.Stop();
                                    long bytes = !string.IsNullOrWhiteSpace(downloadedPath) && File.Exists(downloadedPath) ? new FileInfo(downloadedPath).Length : 0;
                                    WriteTempProgressLog(tempFolder, item, "Downloading", completedPages, imageUrls.Count, $"{completedPages}/{imageUrls.Count} pages", $"Page {pageIndex} completed");
                                    bool shouldFlushUi = completedPages == imageUrls.Count ||
                                                        completedPages == 1 ||
                                                        (DateTime.UtcNow - lastUiUpdateUtc).TotalMilliseconds >= 500 ||
                                                        completedPages % 5 == 0;
                                    if (shouldFlushUi)
                                    {
                                        lastUiUpdateUtc = DateTime.UtcNow;
                                        UpdateDownloadRowMetrics(queueItem, completedPages, imageUrls.Count, $"{completedPages}/{imageUrls.Count} pages", bytes, watch.ElapsedMilliseconds, isParentQueue);
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

                MoveTempFolderToTarget(tempFolder, targetFolder, "dilib");
                ValidateDownloadedFiles(targetFolder, imageUrls.Count, queueItem ?? item, chapterTitle, null, chapterUrl: normalized);
                return true;
            }
            finally
            {
                UnregisterTempFolder(tempFolder);
            }
        }

        private double GetDilibChapterSortNumber(GalleryItem chapter)
        {
            if (chapter == null)
            {
                return 0d;
            }

            double number = ParseChapterNumber(chapter.Link);
            if (number > 0d)
            {
                return number;
            }

            var match = Regex.Match(chapter.LinkCount ?? string.Empty, @"(?<num>\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (match.Success && double.TryParse(match.Groups["num"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out number))
            {
                return number;
            }

            match = Regex.Match(chapter.Link ?? string.Empty, @"(?<num>\d+(?:\.\d+)?)(?:\.html)?\s*$", RegexOptions.IgnoreCase);
            if (match.Success && double.TryParse(match.Groups["num"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out number))
            {
                return number;
            }

            return double.MaxValue;
        }

        private double GetDilibChapterSortNumber(string link)
        {
            if (string.IsNullOrWhiteSpace(link))
            {
                return double.MaxValue;
            }

            double number = ParseChapterNumber(link);
            if (number > 0d)
            {
                return number;
            }

            Match match = Regex.Match(link, @"(?<num>\d+(?:\.\d+)?)(?:\.html)?\s*$", RegexOptions.IgnoreCase);
            if (match.Success && double.TryParse(match.Groups["num"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out number))
            {
                return number;
            }

            return double.MaxValue;
        }
    }
}

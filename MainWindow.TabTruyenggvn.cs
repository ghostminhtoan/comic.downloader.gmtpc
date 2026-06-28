#pragma warning disable 4014
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
        private const string TruyenggvnBaseUrl = "https://sayhentai.cx";
        private const string TruyenggvnSiteFolder = "sayhentai";
        private const string TruyenggvnImageHost = "cdn.pubtranxzyzz.store";

        private sealed class TruyenggvnImageInfo
        {
            public string BookId { get; set; }
            public string ChapterToken { get; set; }
            public string BookTitle { get; set; }
            public string ChapterTitle { get; set; }
        }

        private sealed class TruyenggvnChapterRouteInfo
        {
            public string BookId { get; set; }
            public string ChapterToken { get; set; }
        }

        private void TruyenggvnLog(string message)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                string logLine = $"[{DateTime.Now:HH:mm:ss}] {message}\r\n";
                bool isError = IsErrorMessage(message);
                AppendLogLine(txtTruyenggvnLog, logLine, isError);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private bool IsTruyenggvnUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            try
            {
                var uri = new Uri(url);
                return uri.Host.Equals("sayhentai.cx", StringComparison.OrdinalIgnoreCase) ||
                       uri.Host.EndsWith(".sayhentai.cx", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return url.IndexOf("sayhentai.cx", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        private bool IsTruyenggvnImageUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            try
            {
                var uri = new Uri(url);
                return uri.Host.Equals(TruyenggvnImageHost, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return url.IndexOf(TruyenggvnImageHost, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        private string NormalizeTruyenggvnUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            string normalized = url.Trim();
            if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (normalized.StartsWith("sayhentai.cx/", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Equals("sayhentai.cx", StringComparison.OrdinalIgnoreCase) ||
                    normalized.StartsWith(TruyenggvnImageHost + "/", StringComparison.OrdinalIgnoreCase))
                {
                    normalized = "https://" + normalized.TrimStart('/');
                }
                else
                {
                    normalized = TruyenggvnBaseUrl + (normalized.StartsWith("/") ? string.Empty : "/") + normalized;
                }
            }

            if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri uri))
            {
                throw new ArgumentException("URL sayhentai không hợp lệ.");
            }

            bool validHost =
                uri.Host.Equals("sayhentai.cx", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith(".sayhentai.cx", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.Equals(TruyenggvnImageHost, StringComparison.OrdinalIgnoreCase);

            if (!validHost)
            {
                throw new ArgumentException("URL phải thuộc domain sayhentai.cx hoặc cdn.pubtranxzyzz.store.");
            }

            return uri.AbsoluteUri.TrimEnd('/');
        }

        internal async Task<bool> CheckIfTruyenggvnBlockedAsync(string testUrl)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, testUrl))
                using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                {
                    if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.ServiceUnavailable)
                    {
                        return true;
                    }

                    string html = await response.Content.ReadAsStringAsync();
                    return html.IndexOf("cf-challenge", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           html.IndexOf("cf-turnstile", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           html.IndexOf("Just a moment", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           html.IndexOf("Enable JavaScript and cookies to continue", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           html.IndexOf("xác minh bạn không phải là bot", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch (Exception ex)
            {
                return ex.Message.IndexOf("403", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       ex.Message.IndexOf("503", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        internal async Task<bool> SolveTruyenggvnCaptchaIfNeededAsync(string testUrl)
        {
            bool isBlocked = await CheckIfTruyenggvnBlockedAsync(testUrl);
            if (!isBlocked)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(testUrl))
            {
                TruyenggvnLog("Trang đang yêu cầu captcha/Cloudflare. Tab sayhentai.cx không dùng bypass captcha.");
                return false;
            }

            if (_isCaptchaWindowActive)
            {
                while (_isCaptchaWindowActive)
                {
                    await Task.Delay(500);
                }

                return !await CheckIfTruyenggvnBlockedAsync(testUrl);
            }

            await _captchaSemaphore.WaitAsync();
            try
            {
                isBlocked = await CheckIfTruyenggvnBlockedAsync(testUrl);
                if (!isBlocked)
                {
                    return true;
                }

                _isCaptchaWindowActive = true;
                _isDownloadPaused = true;
                TruyenggvnLog("Phát hiện thử thách Cloudflare. Đang mở cửa sổ bypass captcha...");

                bool solved = false;
                try
                {
                    await await Dispatcher.InvokeAsync(async () =>
                    {
                        var captchaWin = CreateCaptchaWindow(testUrl, autoDeleteCookiesOnLoad: true, headlessAutomation: _lightNovelAutoFocusEnabled);
                        captchaWin.Owner = this;

                        if (await captchaWin.ShowNonBlockingAsync())
                        {
                            var originalUri = new Uri(testUrl);
                            var resolvedUri = captchaWin.ResolvedUri ?? originalUri;

                            foreach (Cookie cookie in captchaWin.ResolvedCookies.GetCookies(resolvedUri))
                            {
                                _cookieContainer.Add(resolvedUri, cookie);
                            }

                            if (originalUri.Host != resolvedUri.Host)
                            {
                                foreach (Cookie cookie in captchaWin.ResolvedCookies.GetCookies(originalUri))
                                {
                                    _cookieContainer.Add(originalUri, cookie);
                                }
                            }

                            if (!string.IsNullOrWhiteSpace(captchaWin.UserAgent))
                            {
                                _httpClient.DefaultRequestHeaders.UserAgent.Clear();
                                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(captchaWin.UserAgent);
                            }

                            solved = true;
                        }
                    });
                }
                finally
                {
                    _isCaptchaWindowActive = false;
                }

                _isDownloadPaused = false;
                return solved;
            }
            finally
            {
                _captchaSemaphore.Release();
            }
        }

        private string GetTruyenggvnPageUrl(string baseUrl, int page)
        {
            baseUrl = NormalizeTruyenggvnUrl(baseUrl);
            if (IsTruyenggvnBookPageUrl(baseUrl) || IsTruyenggvnChapterPageUrl(baseUrl) || IsTruyenggvnImageUrl(baseUrl))
            {
                return baseUrl;
            }

            if (IsTruyenggvnListingUrl(baseUrl))
            {
                string cleanUrl = NormalizeTruyenggvnListingRoot(baseUrl);
                if (Regex.IsMatch(cleanUrl, @"[?&](?:page|paged)=", RegexOptions.IgnoreCase))
                {
                    return Regex.Replace(cleanUrl, @"([?&](?:page|paged)=)\d+", "$1" + Math.Max(1, page), RegexOptions.IgnoreCase);
                }

                string separator = cleanUrl.IndexOf("?", StringComparison.Ordinal) >= 0 ? "&" : "?";
                return cleanUrl + separator + "page=" + Math.Max(1, page);
            }

            if (page <= 1)
            {
                return baseUrl;
            }

            string legacyCleanUrl = Regex.Replace(baseUrl, @"/trang-\d+\.html$", ".html", RegexOptions.IgnoreCase);
            legacyCleanUrl = Regex.Replace(legacyCleanUrl, @"\.html$", string.Empty, RegexOptions.IgnoreCase);
            return legacyCleanUrl + "/trang-" + page + ".html";
        }

        private async void BtnTruyenggvnFetchInfo_Click(object sender, RoutedEventArgs e)
        {
            string rawUrl = txtTruyenggvnTagUrl.Text.Trim();
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                MessageBox.Show("Vui lòng nhập URL hợp lệ.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            btnTruyenggvnFetchInfo.IsEnabled = false;
            lblStatus.Text = "Đang phân tích trang sayhentai...";
            progressBar.IsIndeterminate = true;

            try
            {
                string normalizedUrl = NormalizeTruyenggvnUrl(rawUrl);
                txtTruyenggvnTagUrl.Text = normalizedUrl;

                if (IsTruyenggvnImageUrl(normalizedUrl))
                {
                    txtTruyenggvnTotalPages.Text = "1";
                    txtTruyenggvnPageTo.Text = "1";
                    lblStatus.Text = "Ảnh trực tiếp không có phân trang.";
                    return;
                }

                if (IsTruyenggvnBookPageUrl(normalizedUrl) || IsTruyenggvnChapterPageUrl(normalizedUrl))
                {
                    txtTruyenggvnTotalPages.Text = "1";
                    txtTruyenggvnPageTo.Text = "1";
                    lblStatus.Text = "Book/chapter không có phân trang danh sách.";
                    return;
                }

                bool captchaOk = await SolveTruyenggvnCaptchaIfNeededAsync(normalizedUrl);
                if (!captchaOk)
                {
                    TruyenggvnLog("Không thể bypass Cloudflare.");
                    lblStatus.Text = "Analysis failed (Cloudflare).";
                    return;
                }

                string html = await FetchStringAsync(normalizedUrl, _cts?.Token ?? CancellationToken.None);
                int maxPage = ExtractMaxTruyenggvnPage(html, normalizedUrl);

                txtTruyenggvnTotalPages.Text = maxPage.ToString();
                txtTruyenggvnPageTo.Text = maxPage.ToString();
                TruyenggvnLog($"Phân tích xong. Tìm thấy {maxPage} trang.");
                lblStatus.Text = $"Analysis complete. Found {maxPage} pages.";
            }
            catch (Exception ex)
            {
                TruyenggvnLog("Lỗi phân tích: " + ex.Message);
                txtTruyenggvnTotalPages.Text = "1";
                txtTruyenggvnPageTo.Text = "1";
                lblStatus.Text = "Analysis failed.";
            }
            finally
            {
                btnTruyenggvnFetchInfo.IsEnabled = true;
                progressBar.IsIndeterminate = false;
            }
        }

        private int ExtractMaxTruyenggvnPage(string html, string currentUrl)
        {
            if (IsTruyenggvnBookPageUrl(currentUrl) || IsTruyenggvnChapterPageUrl(currentUrl) || IsTruyenggvnImageUrl(currentUrl))
            {
                return 1;
            }

            int maxPage = 1;
            foreach (Match match in Regex.Matches(html ?? string.Empty, @"/page/(\d+)", RegexOptions.IgnoreCase))
            {
                if (int.TryParse(match.Groups[1].Value, out int pageNum) && pageNum > maxPage)
                {
                    maxPage = pageNum;
                }
            }

            foreach (Match match in Regex.Matches(html ?? string.Empty, @"[?&](?:page|paged)=(\d+)", RegexOptions.IgnoreCase))
            {
                if (int.TryParse(match.Groups[1].Value, out int pageNum) && pageNum > maxPage)
                {
                    maxPage = pageNum;
                }
            }

            foreach (Match match in Regex.Matches(html ?? string.Empty, @"trang-(\d+)\.html", RegexOptions.IgnoreCase))
            {
                if (int.TryParse(match.Groups[1].Value, out int pageNum) && pageNum > maxPage)
                {
                    maxPage = pageNum;
                }
            }

            if (maxPage == 1 && currentUrl.IndexOf("/the-loai/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                maxPage = 1;
            }

            return maxPage;
        }

        private void TxtTruyenggvnTotalPages_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (txtTruyenggvnPageTo != null && txtTruyenggvnTotalPages != null)
            {
                txtTruyenggvnPageTo.Text = txtTruyenggvnTotalPages.Text;
            }
        }

        private async void BtnTruyenggvnScrape_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                btnTruyenggvnScrape.Content = "CANCELLING...";
                btnTruyenggvnScrape.IsEnabled = false;
                if (btnTruyenggvnCrawlMore != null)
                {
                    btnTruyenggvnCrawlMore.IsEnabled = false;
                }
                return;
            }

            await ScrapeTruyenggvnAsync(true);
        }

        private async void BtnTruyenggvnCrawlMore_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                btnTruyenggvnCrawlMore.Content = "CANCELLING...";
                btnTruyenggvnCrawlMore.IsEnabled = false;
                btnTruyenggvnScrape.IsEnabled = false;
                return;
            }

            await ScrapeTruyenggvnAsync(false);
        }

        private async Task ScrapeTruyenggvnAsync(bool clearExisting)
        {
            string rawUrl = txtTruyenggvnTagUrl.Text.Trim();
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                MessageBox.Show("Vui lòng nhập URL hợp lệ.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!int.TryParse(txtTruyenggvnPageFrom.Text, out int pageFrom) || pageFrom < 1)
            {
                MessageBox.Show("Trang bắt đầu không hợp lệ.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!int.TryParse(txtTruyenggvnPageTo.Text, out int pageTo) || pageTo < pageFrom)
            {
                MessageBox.Show("Trang kết thúc không hợp lệ.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            btnTruyenggvnScrape.Content = "STOP CRAWLER";
            btnTruyenggvnCrawlMore.Content = "STOP CRAWLER";
            btnTruyenggvnFetchInfo.IsEnabled = false;
            lblStatus.Text = "Đang cào sayhentai...";
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
                string normalizedUrl = NormalizeTruyenggvnUrl(rawUrl);
                txtTruyenggvnTagUrl.Text = normalizedUrl;

                if (IsTruyenggvnImageUrl(normalizedUrl) ||
                    IsTruyenggvnChapterPageUrl(normalizedUrl) ||
                    IsTruyenggvnBookPageUrl(normalizedUrl))
                {
                    await ImportTruyenggvnDirectLinksAsync(new List<string> { normalizedUrl });
                    lblStatus.Text = "Import completed.";
                    return;
                }

                int totalPages = pageTo - pageFrom + 1;
                int processed = 0;

                for (int page = pageFrom; page <= pageTo; page++)
                {
                    token.ThrowIfCancellationRequested();

                    string pageUrl = GetTruyenggvnPageUrl(normalizedUrl, page);
                    bool captchaOk = await SolveTruyenggvnCaptchaIfNeededAsync(pageUrl);
                    if (!captchaOk)
                    {
                        TruyenggvnLog($"Không thể bypass Cloudflare cho trang {page}.");
                        continue;
                    }

                    string html = await FetchStringAsync(pageUrl, token);
                    var items = ParseTruyenggvnGalleryItemsFromHtml(html, pageUrl);

                    foreach (var item in items)
                    {
                        if (_scrapedItems.Any(existing => existing.Link.Equals(item.Link, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        _scrapedItems.Add(item);
                    }

                    processed++;
                    double progress = ((double)processed / totalPages) * 100;
                    progressBar.Value = progress;
                    lblStatus.Text = $"Searching page {page}/{pageTo} ({progress:0}%)";
                    lblLinkCount.Text = _scrapedItems.Count.ToString();
                }

                var sorted = _scrapedItems.OrderBy(item => item.Name).ThenBy(item => item.OriginalIndex).ToList();
                _scrapedItems.Clear();
                foreach (var item in sorted)
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
                TruyenggvnLog("Lỗi khi cào: " + ex.Message);
                lblStatus.Text = "Crawling failed.";
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                btnTruyenggvnScrape.Content = "GET LINK";
                btnTruyenggvnCrawlMore.Content = "GET MORE";
                btnTruyenggvnScrape.IsEnabled = true;
                btnTruyenggvnCrawlMore.IsEnabled = true;
                btnTruyenggvnFetchInfo.IsEnabled = true;
            }
        }

        private List<GalleryItem> ParseTruyenggvnGalleryItemsFromHtml(string html, string pageUrl)
        {
            var results = new List<GalleryItem>();
            var indexByLink = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string baseUrl = $"{new Uri(pageUrl).Scheme}://{new Uri(pageUrl).Host}";
            string listingHtml = ExtractTruyenggvnListingHtml(html);

            foreach (Match match in Regex.Matches(
                listingHtml,
                @"<h[1-6][^>]*>\s*<a\s+[^>]*href=[""'](?<link>(?:https?://(?:www\.)?sayhentai\.cx)?/[^""'#?]+?\.html)[""'][^>]*>(?<text>.*?)</a>\s*</h[1-6]>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                TryAddTruyenggvnGalleryItem(results, indexByLink, baseUrl, match.Groups["link"].Value, match.Groups["text"].Value);
            }

            foreach (Match match in Regex.Matches(
                listingHtml,
                @"<a\s+[^>]*href=[""'](?<link>(?:https?://(?:www\.)?sayhentai\.cx)?/(?!(?:genre|nhom-dich|tac-gia|page)(?:/|$)|completed(?:\?page=\d+)?/?$)[^""'#?]+?\.html)[""'][^>]*?(?:title=[""'](?<titleAttr>[^""']+)[""'])?[^>]*>(?<text>.*?)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                string preferredText = match.Groups["titleAttr"].Value;
                if (string.IsNullOrWhiteSpace(preferredText))
                {
                    preferredText = match.Groups["text"].Value;
                }

                TryAddTruyenggvnGalleryItem(results, indexByLink, baseUrl, match.Groups["link"].Value, preferredText);
            }

            return results;
        }

        private string ExtractTruyenggvnListingHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            int startIndex = -1;
            string[] startMarkers = new[]
            {
                "class=\"listing row",
                "class=\"tab-content-wrap\""
            };

            foreach (string marker in startMarkers)
            {
                int markerIndex = html.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (markerIndex >= 0)
                {
                    startIndex = markerIndex;
                    break;
                }
            }

            string section = startIndex >= 0 ? html.Substring(startIndex) : html;

            int endIndex = section.Length;
            string[] endMarkers = new[]
            {
                "class=\"sidebar-col",
                "<h5 class=\"heading\">Truyện Mới",
                ">Truyện Mới<"
            };

            foreach (string marker in endMarkers)
            {
                int markerIndex = section.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (markerIndex >= 0 && markerIndex < endIndex)
                {
                    endIndex = markerIndex;
                }
            }

            return endIndex < section.Length ? section.Substring(0, endIndex) : section;
        }

        private void TryAddTruyenggvnGalleryItem(List<GalleryItem> results, Dictionary<string, int> indexByLink, string baseUrl, string rawLink, string rawTitle)
        {
            string fullLink = (rawLink ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(fullLink))
            {
                return;
            }

            if (!fullLink.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !fullLink.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                fullLink = baseUrl + fullLink;
            }

            fullLink = fullLink.TrimEnd('/');
            if (!Uri.TryCreate(fullLink, UriKind.Absolute, out Uri fullUri))
            {
                return;
            }

            string title = WebUtility.HtmlDecode(Regex.Replace(rawTitle ?? string.Empty, @"<[^>]+>", string.Empty)).Trim();
            title = Regex.Replace(title, @"\s+", " ").Trim();
            if (string.IsNullOrWhiteSpace(title) ||
                title.Equals("Mới", StringComparison.OrdinalIgnoreCase) ||
                title.Equals("Hot", StringComparison.OrdinalIgnoreCase))
            {
                title = HumanizeTruyenggvnSlug(Path.GetFileName(fullUri.AbsolutePath));
            }

            if (indexByLink.TryGetValue(fullLink, out int existingIndex))
            {
                var existing = results[existingIndex];
                bool existingLooksBadge =
                    existing != null &&
                    (existing.Name.Equals("Mới", StringComparison.OrdinalIgnoreCase) ||
                     existing.Name.Equals("Hot", StringComparison.OrdinalIgnoreCase));

                if (existingLooksBadge && !string.IsNullOrWhiteSpace(title))
                {
                    existing.Name = FormatGalleryTitle(title);
                }

                return;
            }

            indexByLink[fullLink] = results.Count;
            results.Add(new GalleryItem
            {
                Link = fullLink,
                Name = FormatGalleryTitle(title),
                SourceDomain = TruyenggvnSiteFolder,
                OriginalIndex = _scrapedItems.Count + results.Count,
                IsChecked = false
            });
        }

        private string HumanizeTruyenggvnSlug(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug))
            {
                return "Truyenggvn";
            }

            string clean = Path.GetFileNameWithoutExtension(slug);
            clean = Regex.Replace(clean, @"-chap-.+$", string.Empty, RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"-chuong-.+$", string.Empty, RegexOptions.IgnoreCase);
            clean = clean.Replace("-", " ").Trim();
            return string.IsNullOrWhiteSpace(clean) ? "Truyenggvn" : WebUtility.HtmlDecode(clean);
        }

        private bool IsTruyenggvnBookPageUrl(string url)
        {
            if (!IsTruyenggvnUrl(url))
            {
                return false;
            }

            try
            {
                string path = new Uri(url).AbsolutePath;
                return Regex.IsMatch(path, @"^/[^/]+\.html$", RegexOptions.IgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private bool IsTruyenggvnChapterPageUrl(string url)
        {
            if (!IsTruyenggvnUrl(url))
            {
                return false;
            }

            try
            {
                string path = new Uri(url).AbsolutePath;
                return Regex.IsMatch(path, @"^/[^/]+/chuong-[^/?#]+/?$", RegexOptions.IgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private bool IsTruyenggvnListingUrl(string url)
        {
            if (!IsTruyenggvnUrl(url) || IsTruyenggvnImageUrl(url))
            {
                return false;
            }

            try
            {
                string path = new Uri(url).AbsolutePath.TrimEnd('/');
                return path.Equals("/completed", StringComparison.OrdinalIgnoreCase) ||
                       path.Equals("/genre", StringComparison.OrdinalIgnoreCase) ||
                       path.StartsWith("/genre/", StringComparison.OrdinalIgnoreCase) ||
                       path.StartsWith("/nhom-dich/", StringComparison.OrdinalIgnoreCase) ||
                       path.StartsWith("/tac-gia/", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private string NormalizeTruyenggvnListingRoot(string url)
        {
            string normalized = NormalizeTruyenggvnUrl(url);
            normalized = Regex.Replace(normalized, @"/page/\d+/?$", string.Empty, RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"([?&](?:page|paged)=)\d+", string.Empty, RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"[?&]$", string.Empty, RegexOptions.IgnoreCase);
            normalized = normalized.TrimEnd('/');
            return normalized;
        }

        private void BtnTruyenggvnPasteDirect_Click(object sender, RoutedEventArgs e)
        {
            var window = new DirectDownloadWindow(
                customTitle: "PASTE TRUYENGGVN LINKS",
                customDescription: "Paste sayhentai tag/book/chapter links or direct image links below. You can freely edit the list before importing.",
                customExample: "Example:\nhttps://sayhentai.cx/genre/romance\nhttps://sayhentai.cx/nhom-dich/26\nhttps://sayhentai.cx/truyen-toi-da-duoc-hoan-doi-than-xac.html\nhttps://sayhentai.cx/truyen-toi-da-duoc-hoan-doi-than-xac/chuong-1\nhttps://cdn.pubtranxzyzz.store/hen/9648/1/6864ff7bb77aa.jpg?token=...&expires=...")
            {
                Owner = this
            };

            window.OnImport = async links => await ImportTruyenggvnDirectLinksAsync(links);
            window.ShowDialog();
        }

        private void BtnTruyenggvnResetCookie_Click(object sender, RoutedEventArgs e)
        {
            string url = txtTruyenggvnTagUrl?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                url = TruyenggvnBaseUrl;
            }

            ResetCookiesForCaptcha(url);
            TruyenggvnLog("Đã reset cookie cho tab sayhentai. Tab này không dùng bypass captcha.");
        }

        private async Task ImportTruyenggvnDirectLinksAsync(List<string> links)
        {
            btnTruyenggvnScrape.IsEnabled = false;
            btnTruyenggvnFetchInfo.IsEnabled = false;
            progressBar.Value = 0;
            progressBar.IsIndeterminate = false;

            int imported = 0;
            int failed = 0;

            try
            {
                for (int i = 0; i < links.Count; i++)
                {
                    string link = NormalizeTruyenggvnUrl(links[i]);
                    lblStatus.Text = $"[{i + 1}/{links.Count}] Đang phân tích: {link}";

                    try
                    {
                        if (_scrapedItems.Any(item => item.Link.Equals(link, StringComparison.OrdinalIgnoreCase)))
                        {
                            imported++;
                            continue;
                        }

                        string title = await BuildTruyenggvnItemTitleAsync(link);
                        _scrapedItems.Add(new GalleryItem
                        {
                            Link = link,
                            Name = FormatGalleryTitle(title),
                            OriginalIndex = _scrapedItems.Count,
                            IsChecked = true,
                            SourceDomain = TruyenggvnSiteFolder
                        });
                        imported++;
                    }
                    catch (Exception ex)
                    {
                        TruyenggvnLog($"Import lỗi với '{link}': {ex.Message}");
                        failed++;
                    }

                    progressBar.Value = ((double)(i + 1) / links.Count) * 100;
                    lblLinkCount.Text = _scrapedItems.Count.ToString();
                }

                RecalculateDuplicates();
                lblLinkCount.Text = _scrapedItems.Count.ToString();
                lblStatus.Text = $"Import completed. Success: {imported}, Failed: {failed}.";
            }
            finally
            {
                btnTruyenggvnScrape.IsEnabled = true;
                btnTruyenggvnFetchInfo.IsEnabled = true;
            }
        }

        private async Task<string> BuildTruyenggvnItemTitleAsync(string link)
        {
            if (IsTruyenggvnImageUrl(link))
            {
                var info = ParseTruyenggvnImageUrl(link);
                if (info != null)
                {
                    return $"{info.BookTitle} - {info.ChapterTitle}";
                }

                return "Truyenggvn Image";
            }

            bool captchaOk = await SolveTruyenggvnCaptchaIfNeededAsync(link);
            if (!captchaOk)
            {
                throw new Exception("Không thể bypass Cloudflare.");
            }

            string html = await FetchStringAsync(link, _cts?.Token ?? CancellationToken.None);
            string title = ExtractTruyenggvnTitleFromHtml(html);
            if (!string.IsNullOrWhiteSpace(title))
            {
                return title;
            }

            return HumanizeTruyenggvnSlug(new Uri(link).AbsolutePath);
        }

        private string ExtractTruyenggvnTitleFromHtml(string html)
        {
            foreach (string pattern in new[]
            {
                @"<meta\s+property=[""']og:title[""']\s+content=[""'](?<title>[^""']+)[""']",
                @"<title>\s*(?<title>.*?)\s*</title>",
                @"<h1[^>]*>(?<title>.*?)</h1>"
            })
            {
                Match match = Regex.Match(html ?? string.Empty, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (match.Success)
                {
                    string title = WebUtility.HtmlDecode(Regex.Replace(match.Groups["title"].Value, @"<[^>]+>", string.Empty)).Trim();
                    title = Regex.Replace(title, @"\s*[\-\|]\s*Truyen.*$", string.Empty, RegexOptions.IgnoreCase).Trim();
                    title = Regex.Replace(title, @"\s*[\-\|]\s*(?:Việt Hentai|Viet Hentai|Hentai Vietsub HD|Kuro Neko|Mèo đen|sayhentai\.cx|SayHentai).*$", string.Empty, RegexOptions.IgnoreCase).Trim();
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        return title;
                    }
                }
            }

            return string.Empty;
        }

        private async Task DownloadTruyenggvnGalleryAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem = null, ChapterFilter chapterFilter = null)
        {
            string cleanLink = NormalizeTruyenggvnUrl(item.Link);
            if (IsTruyenggvnImageUrl(cleanLink))
            {
                await DownloadTruyenggvnChapterAsync(item, rootFolder, token, queueItem, false);
                return;
            }

            var uri = new Uri(cleanLink);
            string[] segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            string bookSlug = segments.Length > 0
                ? Path.GetFileNameWithoutExtension(segments[0])
                : string.Empty;
            bool isChapter = IsTruyenggvnChapterPageUrl(cleanLink);
            bool isBook = IsTruyenggvnBookPageUrl(cleanLink);

            if (!isBook)
            {
                await DownloadTruyenggvnChapterAsync(item, rootFolder, token, queueItem, false);
                return;
            }

            if (chapterFilter == null)
            {
                var pending = LoadPendingChapterLinksFromProcess(rootFolder, TruyenggvnSiteFolder, item);
                if (pending != null)
                {
                    if (pending.Count == 0)
                    {
                        if (queueItem != null)
                        {
                            Dispatcher.BeginInvoke((Action)(() =>
                            {
                                queueItem.Status = "Completed";
                                queueItem.CurrentProcess = "Đã hoàn tất theo process";
                            }));
                        }
                        return;
                    }

                    await DownloadTruyenggvnPendingChaptersAsync(item, rootFolder, token, queueItem, pending);
                    return;
                }
            }

            bool captchaOk = await SolveTruyenggvnCaptchaIfNeededAsync(cleanLink);
            if (!captchaOk)
            {
                throw new Exception("Không thể vượt qua Cloudflare captcha.");
            }

            string html = await FetchStringAsync(cleanLink, token);
            string title = ExtractTruyenggvnTitleFromHtml(html);
            if (!string.IsNullOrWhiteSpace(title))
            {
                item.Name = FormatGalleryTitle(title);
            }

            var chapterLinks = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match match in Regex.Matches(html, @"href=[""'](?<link>(?:https?://(?:www\.)?sayhentai\.cx)?/[^/""'#?]+/chuong-[^""'#?]+)[""']", RegexOptions.IgnoreCase))
            {
                string link = match.Groups["link"].Value.Trim();
                string chapterPath = link;
                if (Uri.TryCreate(link, UriKind.Absolute, out Uri absoluteChapterUri))
                {
                    chapterPath = absoluteChapterUri.AbsolutePath;
                }

                string[] linkSegments = chapterPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (string.IsNullOrWhiteSpace(bookSlug) ||
                    linkSegments.Length < 2 ||
                    !linkSegments[0].Equals(bookSlug, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!link.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !link.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    link = TruyenggvnBaseUrl + (link.StartsWith("/") ? string.Empty : "/") + link;
                }

                link = link.TrimEnd('/');
                if (seen.Add(link))
                {
                    chapterLinks.Add(link);
                }
            }

            if (chapterLinks.Count == 0)
            {
                if (IsTruyenggvnUpcomingOnlyBook(html))
                {
                    string message = $"'{item.Name}' hiện chỉ có trạng thái chap sắp ra, chưa có chapter nào để tải.";
                    TruyenggvnLog(message);
                    if (queueItem != null)
                    {
                        Dispatcher.BeginInvoke((Action)(() =>
                        {
                            queueItem.Status = "Completed";
                            queueItem.CurrentProcess = "Chưa có chapter để tải";
                        }));
                    }
                    return;
                }

                TruyenggvnLog($"Không tìm thấy chapter nào trong '{item.Name}'. Thử tải trực tiếp như chapter.");
                await DownloadTruyenggvnChapterAsync(item, rootFolder, token, queueItem, false);
                return;
            }

            chapterLinks = chapterLinks.OrderBy(ParseTruyenggvnChapterNumber).ToList();
            int totalFound = chapterLinks.Count;

            if (chapterFilter != null)
            {
                chapterLinks = chapterLinks
                    .Where(link => chapterFilter.IsMatch(ParseTruyenggvnChapterNumber(link)))
                    .ToList();

                if (chapterLinks.Count == 0)
                {
                    TruyenggvnLog($"Không có chapter nào khớp bộ lọc trong tổng {totalFound} chapter của '{item.Name}'.");
                    if (queueItem != null)
                    {
                        Dispatcher.BeginInvoke((Action)(() =>
                        {
                            queueItem.Status = "Completed";
                            queueItem.CurrentProcess = "Không có chapter trùng khớp bộ lọc";
                        }));
                    }
                    return;
                }
            }
            else
            {
                chapterLinks = FilterPendingChapterLinksFromProcess(rootFolder, TruyenggvnSiteFolder, item, chapterLinks);
                if (chapterLinks.Count == 0)
                {
                    if (queueItem != null)
                    {
                        Dispatcher.BeginInvoke((Action)(() =>
                        {
                            queueItem.Status = "Completed";
                            queueItem.CurrentProcess = "Đã hoàn tất theo process";
                        }));
                    }
                    return;
                }
            }

            Dispatcher.BeginInvoke((Action)(() =>
            {
                item.LinkCount = chapterLinks.Count.ToString();
            }));

            await DownloadTruyenggvnPendingChaptersAsync(item, rootFolder, token, queueItem, chapterLinks);
        }

        private async Task DownloadTruyenggvnPendingChaptersAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem, IList<string> chapterLinks)
        {
            if (queueItem != null)
            {
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    queueItem.TotalChapters = chapterLinks.Count;
                    queueItem.CompletedChapters = 0;
                }));
            }

            int completedCount = 0;
            for (int i = 0; i < chapterLinks.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                string chapLink = chapterLinks[i];
                var chapItem = new GalleryItem
                {
                    Link = chapLink,
                    Name = item.Name,
                    SourceDomain = TruyenggvnSiteFolder
                };

                bool chapterCompleted = await DownloadTruyenggvnChapterAsync(chapItem, rootFolder, token, queueItem, true);
                if (chapterCompleted)
                {
                    MarkChapterProcessDone(rootFolder, TruyenggvnSiteFolder, item, chapLink);
                    completedCount++;
                }

                if (queueItem != null && chapterCompleted)
                {
                    Dispatcher.BeginInvoke((Action)(() =>
                    {
                        queueItem.CompletedChapters = completedCount;
                    }));
                }
            }
        }

        private async Task<bool> DownloadTruyenggvnChapterAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem, bool isParentQueue)
        {
            string normalizedLink = NormalizeTruyenggvnUrl(item.Link);
            string html = string.Empty;
            string cleanManga = item.Name;
            string cleanChapter = "chap 1";
            var imageUrls = new List<string>();

            if (IsTruyenggvnImageUrl(normalizedLink))
            {
                imageUrls.Add(normalizedLink);
                var imageInfo = ParseTruyenggvnImageUrl(normalizedLink);
                cleanManga = imageInfo?.BookTitle ?? "SayHentai";
                cleanChapter = imageInfo?.ChapterTitle ?? "chap 1";
            }
            else
            {
                bool captchaOk = await SolveTruyenggvnCaptchaIfNeededAsync(normalizedLink);
                if (!captchaOk)
                {
                    throw new Exception("Không thể vượt qua Cloudflare captcha.");
                }

                html = await FetchStringAsync(normalizedLink, token);
                string pageTitle = ExtractTruyenggvnTitleFromHtml(html);
                ParseTruyenggvnPageTitle(pageTitle, item.Name, normalizedLink, out cleanManga, out cleanChapter);
                if (!string.IsNullOrWhiteSpace(item?.Name))
                {
                    cleanManga = item.Name;
                }
                imageUrls = ExtractTruyenggvnImageUrls(html, normalizedLink);
            }

            if (imageUrls.Count == 0)
            {
                throw new Exception("Không tìm thấy ảnh nào để tải.");
            }

            cleanChapter = NormalizeChapterLabel(cleanChapter);
            string safeManga = GetCanonicalBookFolderName(item, cleanManga, "SayHentai");
            string aliasSafeManga = GetSafePathName(cleanManga);
            string safeChapter = GetDownloadChapterFolderName(cleanManga, cleanChapter);
            string progressKey = $"sayhentai|{safeManga}";
            int totalChaptersForLog = queueItem != null ? Math.Max(1, queueItem.TotalChapters) : 1;
            int currentChapterForLog = queueItem != null ? Math.Max(1, Math.Min(queueItem.CompletedChapters + 1, totalChaptersForLog)) : 1;

            UpsertMainLogLine(progressKey, $"[sayhentai] Đang tải {cleanManga} - {cleanChapter} ({currentChapterForLog}/{totalChaptersForLog})");

            string siteRootFolder = GetSiteDownloadRoot(rootFolder, TruyenggvnSiteFolder);
            await NormalizeChapterFolderAliasAsync(siteRootFolder, safeManga, aliasSafeManga, safeChapter, token);

            string unmergedPath = Path.Combine(siteRootFolder, $"{safeManga}-{safeChapter}");
            string mergedPath = Path.Combine(siteRootFolder, safeManga, safeChapter);
            string finalTargetFolder = _isSingleComicFolderType ? mergedPath : unmergedPath;
            string tempFolder = BuildStableTempFolderPath(siteRootFolder, TruyenggvnSiteFolder, safeManga, safeChapter, normalizedLink);
            Directory.CreateDirectory(tempFolder);
            RegisterTempFolder(tempFolder);
            WriteTempProgressLog(tempFolder, item, "Downloading", 0, imageUrls.Count, "0/0 pages", $"Bắt đầu tải {cleanChapter}");

            int maxThreads = GetBookConnectionLimit(queueItem ?? item);

            if (queueItem != null && !isParentQueue)
            {
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    queueItem.TotalChapters = imageUrls.Count;
                    queueItem.CompletedChapters = 0;
                }));
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
                            if (queueItem != null && queueItem.IsStopped)
                            {
                                throw new OperationCanceledException();
                            }
                            await Task.Delay(200, token);
                        }

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
                                    string processText = isParentQueue ? $"{cleanChapter} (trang {completedPages}/{imageUrls.Count})" : $"Trang {completedPages}/{imageUrls.Count}";
                                    UpdateDownloadRowMetrics(queueItem, completedPages, imageUrls.Count, processText, 0, 0, isParentQueue);
                                    WriteTempProgressLog(tempFolder, item, "Downloading", completedPages, imageUrls.Count, processText, $"Trang {index + 1} đã có sẵn", imgUrl);
                                }
                                return;
                            }

                            string downloadedPath = null;
                            try
                            {
                                await DownloadUrlToFileWithRefererAsync(imgUrl, normalizedLink, localFilePath, token, isTruyenqq: true);
                                downloadedPath = localFilePath;
                            }
                            catch (Exception ex)
                            {
                                lock (lockObj)
                                {
                                    if (queueItem != null)
                                    {
                                        string pageName = Path.GetFileNameWithoutExtension(pageFilenames[index]);
                                        queueItem.AddError(cleanChapter, index + 1, ex.Message, imgUrl, normalizedLink, pageName);
                                        RecordCheckError("sayhentai", queueItem.Name ?? cleanManga, cleanChapter, index + 1, ex.Message, imgUrl, pageName);
                                    }
                                }
                            }

                            pageWatch.Stop();
                            lock (lockObj)
                            {
                                completedPages++;
                                long downloadedBytes = !string.IsNullOrWhiteSpace(downloadedPath) && File.Exists(downloadedPath) ? new FileInfo(downloadedPath).Length : 0;
                                string processText = isParentQueue ? $"{cleanChapter} (trang {completedPages}/{imageUrls.Count})" : $"Trang {completedPages}/{imageUrls.Count}";
                                UpdateDownloadRowMetrics(queueItem, completedPages, imageUrls.Count, processText, downloadedBytes, pageWatch.ElapsedMilliseconds, isParentQueue);
                                WriteTempProgressLog(
                                    tempFolder,
                                    item,
                                    "Downloading",
                                    completedPages,
                                    imageUrls.Count,
                                    processText,
                                    $"Trang {index + 1} hoàn tất",
                                    imgUrl);
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

            if (Directory.Exists(tempFolder))
            {
                WriteTempProgressLog(tempFolder, item, "Done", imageUrls.Count, imageUrls.Count, isParentQueue ? $"{cleanChapter} (trang {imageUrls.Count}/{imageUrls.Count})" : $"Trang {imageUrls.Count}/{imageUrls.Count}", "Download completed");
                MoveTempFolderToTarget(tempFolder, finalTargetFolder, "sayhentai");
                if (_isSingleComicFolderType)
                {
                    await NormalizeChapterFolderAliasAsync(siteRootFolder, safeManga, aliasSafeManga, safeChapter, token);
                }
                UpsertMainLogLine(progressKey, $"[sayhentai] Đã tải xong {cleanManga} - {cleanChapter} ({currentChapterForLog}/{totalChaptersForLog})");
            }

            return ValidateDownloadedFiles(finalTargetFolder, imageUrls.Count, queueItem, cleanChapter, chapterUrl: normalizedLink);
        }

        private void ParseTruyenggvnPageTitle(string pageTitle, string fallbackName, string link, out string mangaTitle, out string chapterTitle)
        {
            string preferredFallback = string.IsNullOrWhiteSpace(fallbackName) ? string.Empty : fallbackName.Trim();

            if (!string.IsNullOrWhiteSpace(link))
            {
                try
                {
                    var uri = new Uri(link);
                    string[] segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    if (segments.Length >= 2 && segments[1].StartsWith("chuong-", StringComparison.OrdinalIgnoreCase))
                    {
                        mangaTitle = string.IsNullOrWhiteSpace(preferredFallback) ? HumanizeTruyenggvnSlug(segments[0]) : preferredFallback;
                        chapterTitle = WebUtility.HtmlDecode(segments[1].Replace("-", " ").Trim());
                        return;
                    }

                    if (segments.Length >= 1 && segments[0].EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                    {
                        mangaTitle = string.IsNullOrWhiteSpace(preferredFallback) ? HumanizeTruyenggvnSlug(segments[0]) : preferredFallback;
                        chapterTitle = "chap 1";
                        return;
                    }
                }
                catch
                {
                }
            }

            if (!string.IsNullOrWhiteSpace(link))
            {
                try
                {
                    string slug = Path.GetFileNameWithoutExtension(new Uri(link).AbsolutePath);
                    Match chapterUrlMatch = Regex.Match(slug, @"^(?<manga>.+)-(?<bookId>\d+)-chap-(?<chap>\d+(?:-\d+)*)$", RegexOptions.IgnoreCase);
                    if (chapterUrlMatch.Success)
                    {
                        mangaTitle = HumanizeTruyenggvnSlug(chapterUrlMatch.Groups["manga"].Value);
                        chapterTitle = "chap " + chapterUrlMatch.Groups["chap"].Value.Replace("-", ".");
                        return;
                    }

                    Match bookUrlMatch = Regex.Match(slug, @"^(?<manga>.+)-(?<bookId>\d+)$", RegexOptions.IgnoreCase);
                    if (bookUrlMatch.Success)
                    {
                        mangaTitle = HumanizeTruyenggvnSlug(bookUrlMatch.Groups["manga"].Value);
                        chapterTitle = "chap 1";
                        return;
                    }
                }
                catch
                {
                }
            }

            mangaTitle = string.IsNullOrWhiteSpace(fallbackName) ? HumanizeTruyenggvnSlug(new Uri(link).AbsolutePath) : fallbackName;
            chapterTitle = "chap 1";

            if (string.IsNullOrWhiteSpace(pageTitle))
            {
                string slug = Path.GetFileNameWithoutExtension(new Uri(link).AbsolutePath);
                Match slugMatch = Regex.Match(slug, @"^(?<manga>.+)-chap-(?<chap>[^-]+(?:-\d+)*)$", RegexOptions.IgnoreCase);
                if (slugMatch.Success)
                {
                    mangaTitle = HumanizeTruyenggvnSlug(slugMatch.Groups["manga"].Value);
                    chapterTitle = "chap " + slugMatch.Groups["chap"].Value.Replace("-", ".");
                }
                return;
            }

            string cleaned = Regex.Replace(pageTitle, @"\s*[\-\|]\s*Truyen.*$", string.Empty, RegexOptions.IgnoreCase).Trim();
            Match match = Regex.Match(cleaned, @"^(?<manga>.*?)\s*[-|]\s*(?<chap>(?:chap|chapter|chương)\s*[^-|]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                mangaTitle = match.Groups["manga"].Value.Trim();
                chapterTitle = Regex.Replace(match.Groups["chap"].Value.Trim(), @"\bchapter\b|\bchương\b", "chap", RegexOptions.IgnoreCase);
                return;
            }

            Match chapOnly = Regex.Match(cleaned, @"(?<chap>(?:chap|chapter|chương)\s*[\d\.]+)", RegexOptions.IgnoreCase);
            if (chapOnly.Success)
            {
                chapterTitle = Regex.Replace(chapOnly.Groups["chap"].Value.Trim(), @"\bchapter\b|\bchương\b", "chap", RegexOptions.IgnoreCase);
                mangaTitle = cleaned.Replace(chapOnly.Groups["chap"].Value, string.Empty).Trim(' ', '-', '|');
            }
            else
            {
                mangaTitle = cleaned;
            }
        }

        private List<string> ExtractTruyenggvnImageUrls(string html, string pageUrl)
        {
            string safeHtml = GetSafeTruyenggvnChapterHtml(html);
            string contentArea = ExtractTruyenggvnReadingContent(safeHtml);
            var routeInfo = ParseTruyenggvnChapterRouteInfo(pageUrl);
            var urls = new List<string>();
            var bestByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var orderedKeys = new List<string>();

            void AddImageCandidate(string candidateUrl)
            {
                if (string.IsNullOrWhiteSpace(candidateUrl))
                {
                    return;
                }

                string normalizedCandidate = WebUtility.HtmlDecode(candidateUrl).Trim();
                if (!IsLikelyTruyenggvnChapterImageUrl(normalizedCandidate, routeInfo))
                {
                    return;
                }

                string key = normalizedCandidate.Split('?')[0];
                if (!bestByPath.TryGetValue(key, out string currentBest))
                {
                    bestByPath[key] = normalizedCandidate;
                    orderedKeys.Add(key);
                    return;
                }

                bool currentHasToken = currentBest.IndexOf("token=", StringComparison.OrdinalIgnoreCase) >= 0;
                bool candidateHasToken = normalizedCandidate.IndexOf("token=", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!currentHasToken && candidateHasToken)
                {
                    bestByPath[key] = normalizedCandidate;
                }
            }

            foreach (Match match in Regex.Matches(contentArea ?? string.Empty, @"https?://cdn\.pubtranxzyzz\.store/hen/\d+/[^/""'\s>]+/[^""'\s>]+\.(?:jpg|jpeg|png|webp|gif)(?:\?[^""'\s>]*)?", RegexOptions.IgnoreCase))
            {
                AddImageCandidate(match.Value);
            }

            foreach (Match match in Regex.Matches(contentArea ?? string.Empty, @"https?://[^""'\s>]+?\.truyenvua\.com/[^""'\s>]+", RegexOptions.IgnoreCase))
            {
                string url = WebUtility.HtmlDecode(match.Value).Trim();
                if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AddImageCandidate(url);
            }

            if (bestByPath.Count == 0)
            {
                foreach (Match match in Regex.Matches(contentArea ?? string.Empty, @"<(?:img|source)[^>]+(?:data-src|data-original|src)=[""'](?<url>[^""']+)[""']", RegexOptions.IgnoreCase))
                {
                    string url = WebUtility.HtmlDecode(match.Groups["url"].Value).Trim();
                    if (string.IsNullOrWhiteSpace(url) ||
                        url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                        (url.IndexOf("truyenvua.com", StringComparison.OrdinalIgnoreCase) < 0 &&
                         url.IndexOf(TruyenggvnImageHost, StringComparison.OrdinalIgnoreCase) < 0))
                    {
                        continue;
                    }

                    if (url.StartsWith("//"))
                    {
                        url = "https:" + url;
                    }
                    else if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                             !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        url = TruyenggvnBaseUrl + (url.StartsWith("/") ? string.Empty : "/") + url;
                    }

                    AddImageCandidate(url);
                }
            }

            foreach (string key in orderedKeys)
            {
                if (bestByPath.TryGetValue(key, out string bestUrl))
                {
                    urls.Add(bestUrl);
                }
            }

            return urls;
        }

        private string GetSafeTruyenggvnChapterHtml(string html)
        {
            if (string.IsNullOrEmpty(html))
            {
                return html;
            }

            string[] stopIndicators = new[]
            {
                "class=\"comment-box\"",
                "class=\"comment-wrapper\"",
                "class=\"comment-holder\"",
                "class=\"list-comment\"",
                "class=\"list_comment\"",
                "class=\"comments\"",
                "id=\"comment\"",
                "id=\"comments\"",
                "id=\"disqus_thread\"",
                "Bình Luận(",
                "Bình Luận (",
                ">Bình Luận<",
                ">Bình luận<",
                "Like và theo dõi để ủng hộ TruyenGGVN nhé",
                "Mời bạn thảo luận",
                "truyenqqno.com",
                "truyenggno.com"
            };

            int minIndex = html.Length;
            foreach (string indicator in stopIndicators)
            {
                int index = html.IndexOf(indicator, StringComparison.OrdinalIgnoreCase);
                if (index >= 0 && index < minIndex)
                {
                    minIndex = index;
                }
            }

            return minIndex < html.Length ? html.Substring(0, minIndex) : html;
        }

        private string ExtractTruyenggvnReadingContent(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return html;
            }

            int startIndex = -1;
            string[] containerMarkers = new[]
            {
                "class=\"page-chapter\"",
                "class=\"reading-detail\"",
                "class=\"reading-content\"",
                "class=\"chapter-content\"",
                "class=\"chapter_content\"",
                "class=\"box-chap\"",
                "class=\"container-chapter-reader\"",
                "id=\"chapter_content\""
            };

            foreach (string marker in containerMarkers)
            {
                int index = html.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    startIndex = index;
                    break;
                }
            }

            string contentArea = startIndex >= 0 ? html.Substring(startIndex) : html;

            string[] secondaryStops = new[]
            {
                "Bình Luận(",
                "Bình Luận (",
                ">Bình Luận<",
                ">Bình luận<",
                "class=\"comment-box\"",
                "class=\"comment-wrapper\"",
                "class=\"list-comment\"",
                "id=\"comment\"",
                "id=\"comments\""
            };

            int minStopIndex = contentArea.Length;
            foreach (string stop in secondaryStops)
            {
                int index = contentArea.IndexOf(stop, StringComparison.OrdinalIgnoreCase);
                if (index >= 0 && index < minStopIndex)
                {
                    minStopIndex = index;
                }
            }

            return minStopIndex < contentArea.Length ? contentArea.Substring(0, minStopIndex) : contentArea;
        }

        private TruyenggvnChapterRouteInfo ParseTruyenggvnChapterRouteInfo(string pageUrl)
        {
            if (string.IsNullOrWhiteSpace(pageUrl))
            {
                return null;
            }

            try
            {
                string slug = Path.GetFileNameWithoutExtension(new Uri(pageUrl).AbsolutePath);
                Match match = Regex.Match(
                    slug,
                    @"-(?<bookId>\d+)-chap-(?<chapter>\d+(?:-\d+)*)$",
                    RegexOptions.IgnoreCase);

                if (!match.Success)
                {
                    return null;
                }

                return new TruyenggvnChapterRouteInfo
                {
                    BookId = match.Groups["bookId"].Value,
                    ChapterToken = match.Groups["chapter"].Value
                };
            }
            catch
            {
                return null;
            }
        }

        private bool IsTruyenggvnUpcomingOnlyBook(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return false;
            }

            string plainText = WebUtility.HtmlDecode(Regex.Replace(html, @"<[^>]+>", " "));
            plainText = Regex.Replace(plainText, @"\s+", " ").Trim();

            bool hasUpcomingMarker =
                plainText.IndexOf("CHAP SẮP RA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                plainText.IndexOf("CHAP SAP RA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                plainText.IndexOf("sắp ra", StringComparison.OrdinalIgnoreCase) >= 0 ||
                plainText.IndexOf("sap ra", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!hasUpcomingMarker)
            {
                return false;
            }

            bool hasRealChapterLink = Regex.IsMatch(
                html,
                @"href=[""'](?:https?://(?:www\.)?sayhentai\.cx)?/[^/""'#?]+/chuong-[^""'#?]+[""']",
                RegexOptions.IgnoreCase);

            return !hasRealChapterLink;
        }

        private bool IsLikelyTruyenggvnChapterImageUrl(string url, TruyenggvnChapterRouteInfo routeInfo)
        {
            if (string.IsNullOrWhiteSpace(url) ||
                (url.IndexOf("truyenvua.com", StringComparison.OrdinalIgnoreCase) < 0 &&
                 url.IndexOf(TruyenggvnImageHost, StringComparison.OrdinalIgnoreCase) < 0))
            {
                return false;
            }

            if (url.IndexOf("avatar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                url.IndexOf("comment", StringComparison.OrdinalIgnoreCase) >= 0 ||
                url.IndexOf("banner", StringComparison.OrdinalIgnoreCase) >= 0 ||
                url.IndexOf("facebook", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            if (routeInfo == null || string.IsNullOrWhiteSpace(routeInfo.BookId))
            {
                return Regex.IsMatch(url, @"https?://cdn\.pubtranxzyzz\.store/hen/\d+/[^/]+/", RegexOptions.IgnoreCase) ||
                       url.IndexOf("truyenvua.com", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            string chapterToken = routeInfo.ChapterToken ?? string.Empty;
            bool matchesFolderPattern = Regex.IsMatch(
                url,
                @"/" + Regex.Escape(routeInfo.BookId) + @"/" + Regex.Escape(chapterToken) + @"/",
                RegexOptions.IgnoreCase);

            bool matchesFilePattern = Regex.IsMatch(
                url,
                @"chap_" + Regex.Escape(routeInfo.BookId) + @"-time_",
                RegexOptions.IgnoreCase);

            return matchesFolderPattern || matchesFilePattern;
        }

        private double ParseTruyenggvnChapterNumber(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return 0;
            }

            Match match = Regex.Match(url, @"-chap-(?<num>\d+(?:-\d+)?(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string token = match.Groups["num"].Value.Replace("-", ".");
                if (double.TryParse(token, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double value))
                {
                    return value;
                }
            }

            match = Regex.Match(url, @"/chuong-(?<num>\d+(?:-\d+)?(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string token = match.Groups["num"].Value.Replace("-", ".");
                if (double.TryParse(token, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double value))
                {
                    return value;
                }
            }

            return 0;
        }

        private TruyenggvnImageInfo ParseTruyenggvnImageUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                if (!uri.Host.Equals(TruyenggvnImageHost, StringComparison.OrdinalIgnoreCase) &&
                    uri.Host.IndexOf("truyenvua.com", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return null;
                }

                string[] segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (uri.Host.Equals(TruyenggvnImageHost, StringComparison.OrdinalIgnoreCase))
                {
                    if (segments.Length < 4 || !segments[0].Equals("hen", StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }

                    string imageBookId = segments[1];
                    string imageChapterToken = segments[2];
                    return new TruyenggvnImageInfo
                    {
                        BookId = imageBookId,
                        ChapterToken = imageChapterToken,
                        BookTitle = "Book " + imageBookId,
                        ChapterTitle = "chap " + imageChapterToken.Replace("-", ".")
                    };
                }

                if (segments.Length < 3)
                {
                    return null;
                }

                string bookId = segments[0];
                string chapterToken = segments[1];
                return new TruyenggvnImageInfo
                {
                    BookId = bookId,
                    ChapterToken = chapterToken,
                    BookTitle = "Book " + bookId,
                    ChapterTitle = "chap " + chapterToken.Replace("-", ".")
                };
            }
            catch
            {
                return null;
            }
        }
    }
}
#pragma warning restore 4014

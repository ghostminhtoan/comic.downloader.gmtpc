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

#pragma warning disable 4014
namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private string _lastCaptchaResolvedHtml = null;

        private void NettruyenLog(string message)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                string logLine = $"[{DateTime.Now:HH:mm:ss}] {message}\r\n";
                bool isError = IsErrorMessage(message);
                AppendLogLine(txtNettruyenLog, logLine, isError);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        internal async Task<bool> CheckIfNettruyenBlockedAsync(string testUrl)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, testUrl))
                {
                    using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                    {
                        if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.ServiceUnavailable)
                        {
                            return true; // Cloudflare blocked (403/503)
                        }

                        using (var content = response.Content)
                        {
                            string html = await content.ReadAsStringAsync();
                            if (html.Contains("cf-challenge") || 
                                html.Contains("cf-turnstile") || 
                                html.Contains("Turnstile") || 
                                html.Contains("Just a moment...") ||
                                html.Contains("thực hiện xác minh bảo mật") ||
                                html.Contains("xác minh bạn không phải là bot"))
                            {
                                return true;
                            }
                        }
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("403") || ex.Message.Contains("503"))
                {
                    return true;
                }
                return false;
            }
        }

        internal async Task<bool> SolveNettruyenCaptchaIfNeededAsync(string testUrl)
        {
            bool isBlocked = await CheckIfNettruyenBlockedAsync(testUrl);
            if (!isBlocked)
            {
                return true; // Not blocked
            }

            if (_isCaptchaWindowActive)
            {
                while (_isCaptchaWindowActive)
                {
                    await Task.Delay(500);
                }
                isBlocked = await CheckIfNettruyenBlockedAsync(testUrl);
                if (!isBlocked)
                {
                    return true;
                }
            }

            await _captchaSemaphore.WaitAsync();
            try
            {
                // Re-check after acquiring lock
                isBlocked = await CheckIfNettruyenBlockedAsync(testUrl);
                if (!isBlocked)
                {
                    return true;
                }

                _isCaptchaWindowActive = true;
                _isDownloadPaused = true;
                NettruyenLog("Phát hiện thử thách Cloudflare / Captcha. Tạm dừng tải và đang mở trình duyệt giải tự động...");

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

                            // Add cookies for resolvedUri
                            var resolvedCookies = captchaWin.ResolvedCookies.GetCookies(resolvedUri);
                            foreach (Cookie cookie in resolvedCookies)
                            {
                                _cookieContainer.Add(resolvedUri, cookie);
                            }

                            // Add cookies for originalUri if different
                            if (originalUri.Host != resolvedUri.Host)
                            {
                                var originalCookies = captchaWin.ResolvedCookies.GetCookies(originalUri);
                                foreach (Cookie cookie in originalCookies)
                                {
                                    _cookieContainer.Add(originalUri, cookie);
                                }
                            }

                            if (!string.IsNullOrEmpty(captchaWin.UserAgent))
                            {
                                _httpClient.DefaultRequestHeaders.UserAgent.Clear();
                                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(captchaWin.UserAgent);
                            }
                            _lastCaptchaResolvedHtml = captchaWin.ResolvedHtml;
                            solved = true;
                        }
                    });
                }
                finally
                {
                    _isCaptchaWindowActive = false;
                }

                if (solved)
                {
                    NettruyenLog("Giải captcha thành công. Tiếp tục tải...");
                    _isDownloadPaused = false;
                    return true;
                }
                return false;
            }
            finally
            {
                _captchaSemaphore.Release();
            }
        }

        private bool IsNettruyenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            try
            {
                var uri = new Uri(url);
                return uri.Host.IndexOf("nettruyen", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return url.IndexOf("nettruyen", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        private string ExtractNettruyenBaseUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                return $"{uri.Scheme}://{uri.Host}";
            }
            catch
            {
                var match = Regex.Match(url, @"^(https?://[^/]+)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
            return "https://nettruyen.gg"; // Default fallback
        }

        private string GetNettruyenPageUrl(string baseUrl, int page)
        {
            baseUrl = baseUrl.Trim();
            if (page == 1) return baseUrl;

            // If there's already a query string, append/replace page parameter
            if (baseUrl.Contains("?"))
            {
                try
                {
                    var uri = new Uri(baseUrl);
                    string query = uri.Query;
                    if (Regex.IsMatch(query, @"[?&]page=\d+", RegexOptions.IgnoreCase))
                    {
                        query = Regex.Replace(query, @"([?&]page=)\d+", $"$1{page}", RegexOptions.IgnoreCase);
                    }
                    else
                    {
                        query += $"&page={page}";
                    }
                    var builder = new UriBuilder(uri) { Query = query.TrimStart('?') };
                    return builder.Uri.ToString();
                }
                catch
                {
                    string cleanUrl = Regex.Replace(baseUrl, @"[?&]page=\d+", "", RegexOptions.IgnoreCase);
                    char separator = cleanUrl.Contains("?") ? '&' : '?';
                    return $"{cleanUrl}{separator}page={page}";
                }
            }

            // Otherwise, append page query param
            return $"{baseUrl}?page={page}";
        }

        private static string NormalizeNettruyenHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return html ?? string.Empty;
            }

            return html.Replace("\\/", "/");
        }

        private static List<string> ExtractNettruyenChapterLinks(string chapterListHtml, string activeDomain, string parentPath)
        {
            var chapterLinks = new List<string>();
            var seenChapters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            chapterListHtml = NormalizeNettruyenHtml(chapterListHtml);
            string escapedPath = Regex.Escape(parentPath.TrimEnd('/'));
            string pattern = @"href=[""'](?<link>[^""']*?" + escapedPath + @"/(?:chuong|chap|chapter|c|chuong-tranh|chuong-doc)-\d+[^""'\s?#]*)[""']";
            var matches = Regex.Matches(chapterListHtml, pattern, RegexOptions.IgnoreCase);

            foreach (Match m in matches)
            {
                string link = m.Groups["link"].Value.Trim();
                if (!link.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !link.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    link = activeDomain + (link.StartsWith("/") ? string.Empty : "/") + link;
                }

                link = link.TrimEnd('/');

                if (seenChapters.Add(link))
                {
                    chapterLinks.Add(link);
                }
            }

            return chapterLinks;
        }

        private static bool HasNettruyenViewMoreButton(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return false;
            }

            // ponytail: tiny heuristic. Upgrade path: DOM parse if site changes markup again.
            return Regex.IsMatch(html, @"<a\b[^>]*class=[""'][^""']*\bview-more\b[^""']*[""'][^>]*>", RegexOptions.IgnoreCase) ||
                   Regex.IsMatch(html, @"\bxem\s*th[eê]m\b", RegexOptions.IgnoreCase) ||
                   Regex.IsMatch(html, @"\bview-more\b", RegexOptions.IgnoreCase);
        }

        private async Task<string> LoadExpandedNettruyenChapterHtmlAsync(string cleanLink)
        {
            string webViewHtml = null;
            _isDownloadPaused = true;
            try
            {
                await await Dispatcher.InvokeAsync(async () =>
                {
                    var captchaWin = CreateWatchMoreCaptcha(cleanLink, autoDeleteCookiesOnLoad: false, headlessAutomation: _lightNovelAutoFocusEnabled);
                    captchaWin.Owner = this;
                    captchaWin.Title = "ĐANG TẢI DANH SÁCH CHƯƠNG - VUI LÒNG CHỜ...";

                    if (await captchaWin.ShowNonBlockingAsync() && !string.IsNullOrEmpty(captchaWin.ResolvedHtml))
                    {
                        webViewHtml = NormalizeNettruyenHtml(captchaWin.ResolvedHtml);

                        var resolvedUri = captchaWin.ResolvedUri ?? new Uri(cleanLink);
                        var resolvedCookies = captchaWin.ResolvedCookies.GetCookies(resolvedUri);
                        foreach (Cookie cookie in resolvedCookies)
                        {
                            _cookieContainer.Add(resolvedUri, cookie);
                        }
                    }
                });
            }
            finally
            {
                _isDownloadPaused = false;
            }

            return webViewHtml;
        }

        private async void BtnNettruyenFetchInfo_Click(object sender, RoutedEventArgs e)
        {
            string url = txtNettruyenTagUrl.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("Vui lòng nhập URL hợp lệ.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            btnNettruyenFetchInfo.IsEnabled = false;
            lblStatus.Text = "Đang phân tích trang Nettruyen...";
            progressBar.IsIndeterminate = true;
            NettruyenLog($"Đang phân tích URL: {url}");

            try
            {
                bool captchaOk = await SolveNettruyenCaptchaIfNeededAsync(url);
                if (!captchaOk)
                {
                    NettruyenLog("Không thể bypass Cloudflare. Hủy phân tích.");
                    lblStatus.Text = "Analysis failed (Cloudflare).";
                    return;
                }

                // Fetch targeted page HTML
                string html = await FetchStringAsync(url, _downloadCts?.Token ?? CancellationToken.None);
                
                int maxPage = 1;
                var pageMatches = Regex.Matches(html, @"[?&]page=(\d+)", RegexOptions.IgnoreCase);
                foreach (Match m in pageMatches)
                {
                    if (int.TryParse(m.Groups[1].Value, out int pageNum))
                    {
                        if (pageNum > maxPage) maxPage = pageNum;
                    }
                }
                var trangMatches = Regex.Matches(html, @"trang-(\d+)", RegexOptions.IgnoreCase);
                foreach (Match m in trangMatches)
                {
                    if (int.TryParse(m.Groups[1].Value, out int pageNum))
                    {
                        if (pageNum > maxPage) maxPage = pageNum;
                    }
                }

                txtNettruyenTotalPages.Text = maxPage.ToString();
                txtNettruyenPageTo.Text = maxPage.ToString();
                
                NettruyenLog($"Phân tích hoàn tất. Phát hiện tối đa {maxPage} trang.");
                lblStatus.Text = $"Analysis complete. Found {maxPage} pages.";
            }
            catch (Exception ex)
            {
                NettruyenLog($"Lỗi khi phân tích: {ex.Message}");
                txtNettruyenTotalPages.Text = "1";
                lblStatus.Text = "Analysis failed.";
            }
            finally
            {
                btnNettruyenFetchInfo.IsEnabled = true;
                progressBar.IsIndeterminate = false;
            }
        }

        private void TxtNettruyenTotalPages_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (txtNettruyenPageTo != null && txtNettruyenTotalPages != null)
            {
                txtNettruyenPageTo.Text = txtNettruyenTotalPages.Text;
            }
        }

        private async void BtnNettruyenScrape_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                btnNettruyenScrape.Content = "CANCELLING...";
                btnNettruyenScrape.IsEnabled = false;
                if (btnNettruyenCrawlMore != null) btnNettruyenCrawlMore.IsEnabled = false;
                return;
            }
            await ScrapeNettruyenAsync(clearExisting: true);
        }

        private async void BtnNettruyenCrawlMore_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                if (btnNettruyenCrawlMore != null)
                {
                    btnNettruyenCrawlMore.Content = "CANCELLING...";
                    btnNettruyenCrawlMore.IsEnabled = false;
                }
                btnNettruyenScrape.IsEnabled = false;
                return;
            }
            await ScrapeNettruyenAsync(clearExisting: false);
        }

        private async Task ScrapeNettruyenAsync(bool clearExisting)
        {
            string baseUrl = txtNettruyenTagUrl.Text.Trim();
            if (string.IsNullOrEmpty(baseUrl))
            {
                MessageBox.Show("Vui lòng nhập URL hợp lệ.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                baseUrl = "https://" + baseUrl;
            }

            if (!int.TryParse(txtNettruyenPageFrom.Text, out int pageFrom) || pageFrom < 1)
            {
                MessageBox.Show("Trang bắt đầu không hợp lệ.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!int.TryParse(txtNettruyenPageTo.Text, out int pageTo) || pageTo < pageFrom)
            {
                MessageBox.Show("Trang kết thúc không hợp lệ.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            btnNettruyenScrape.Content = "STOP CRAWLER";
            if (btnNettruyenCrawlMore != null)
            {
                btnNettruyenCrawlMore.Content = "STOP CRAWLER";
            }
            btnNettruyenFetchInfo.IsEnabled = false;
            lblStatus.Text = "Đang cào Nettruyen...";
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

            NettruyenLog($"Bắt đầu cào từ trang {pageFrom} đến {pageTo}...");

            try
            {
                int totalPages = pageTo - pageFrom + 1;
                int pagesProcessed = 0;

                for (int page = pageFrom; page <= pageTo; page++)
                {
                    token.ThrowIfCancellationRequested();

                    string pageUrl = GetNettruyenPageUrl(baseUrl, page);
                    NettruyenLog($"Đang tải trang {page}: {pageUrl}");

                    bool captchaOk = await SolveNettruyenCaptchaIfNeededAsync(pageUrl);
                    if (!captchaOk)
                    {
                        NettruyenLog($"Không thể bypass Cloudflare cho trang {page}. Bỏ qua trang này.");
                        continue;
                    }

                    string html = await FetchStringAsync(pageUrl, _downloadCts?.Token ?? CancellationToken.None);
                    
                    // Match <a> tags containing /truyen-tranh/ links
                    var viewMatches = Regex.Matches(html, @"<a\s+[^>]*?href=[""'](?<link>[^""']*?/truyen-tranh/[^""']+)[""'][^>]*>(?<content>[\s\S]*?)<\/a>", RegexOptions.IgnoreCase);
                    
                    int pageCount = 0;
                    var pageParents = new List<GalleryItem>();
                    var parentLatestChaps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    string lastParentUrl = null;
                    foreach (Match match in viewMatches)
                    {
                        string relativeLink = match.Groups["link"].Value.Trim();
                        string fullLink = relativeLink;
                        if (!fullLink.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                            !fullLink.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            string activeDomain = ExtractNettruyenBaseUrl(pageUrl);
                            fullLink = activeDomain + (fullLink.StartsWith("/") ? "" : "/") + fullLink;
                        }
                        fullLink = fullLink.TrimEnd('/');

                        // Detect if chapter link
                        bool isChap = Regex.IsMatch(relativeLink, @"/(?:chuong|chap|chapter|c)-\d+", RegexOptions.IgnoreCase);
                        if (isChap)
                        {
                            if (lastParentUrl != null)
                            {
                                string textVal = Regex.Replace(match.Groups["content"].Value, @"<[^>]+>", "").Trim();
                                textVal = WebUtility.HtmlDecode(textVal);
                                if (!string.IsNullOrEmpty(textVal))
                                {
                                    if (!parentLatestChaps.ContainsKey(lastParentUrl))
                                    {
                                        parentLatestChaps[lastParentUrl] = textVal;
                                    }
                                    else
                                    {
                                        double existingNum = ParseChapterNumberFromText(parentLatestChaps[lastParentUrl]);
                                        double currentNum = ParseChapterNumberFromText(textVal);
                                        if (currentNum > existingNum)
                                        {
                                            parentLatestChaps[lastParentUrl] = textVal;
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            // Parent Detail Page Link (Verify segment structure to make sure it's indeed the details page)
                            // Segment 1 is /truyen-tranh/ and segment 2 is slug.
                            try
                            {
                                var tempUri = new Uri(fullLink);
                                var pathSegments = tempUri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                                if (pathSegments.Length != 2 || !pathSegments[0].Equals("truyen-tranh", StringComparison.OrdinalIgnoreCase))
                                {
                                    continue; // Skip non-manga detail link structures
                                }
                            }
                            catch { }

                            lastParentUrl = fullLink;
                            
                            string rawContent = match.Groups["content"].Value;
                            string title = Regex.Replace(rawContent, @"<[^>]+>", "").Trim();
                            title = WebUtility.HtmlDecode(title);

                            var titleAttrMatch = Regex.Match(match.Value, @"title=[""'](?<titleAttr>[^""']+)[""']", RegexOptions.IgnoreCase);
                            if (titleAttrMatch.Success)
                            {
                                string t = WebUtility.HtmlDecode(titleAttrMatch.Groups["titleAttr"].Value.Trim());
                                if (!string.IsNullOrEmpty(t) && t.Length > title.Length)
                                {
                                    title = t;
                                }
                            }

                            if (string.IsNullOrWhiteSpace(title) || title.Length < 2) continue;

                            var existingItem = _scrapedItems.FirstOrDefault(item => item.Link.Equals(fullLink, StringComparison.OrdinalIgnoreCase));
                            if (existingItem == null && !pageParents.Any(p => p.Link.Equals(fullLink, StringComparison.OrdinalIgnoreCase)))
                            {
                                pageParents.Add(new GalleryItem
                                {
                                    Link = fullLink,
                                    Name = FormatGalleryTitle(title),
                                    OriginalIndex = _scrapedItems.Count + pageParents.Count,
                                    IsChecked = false
                                });
                            }
                        }
                    }

                    // Apply latest chap numbers and add to list
                    foreach (var item in pageParents)
                    {
                        if (parentLatestChaps.TryGetValue(item.Link, out string latestChap))
                        {
                            item.LinkCount = latestChap;
                        }
                        _scrapedItems.Add(item);
                        pageCount++;
                    }

                    pagesProcessed++;
                    double progressPct = ((double)pagesProcessed / totalPages) * 100;
                    progressBar.Value = progressPct;
                    lblStatus.Text = $"Searching page {page}/{pageTo} ({progressPct:0}%)";
                    lblLinkCount.Text = _scrapedItems.Count.ToString();
                    NettruyenLog($"Trang {page} hoàn tất. Tìm thấy {pageCount} liên kết mới.");
                }

                // Sort items deterministically
                var sortedItems = _scrapedItems
                    .OrderBy(item => item.Name)
                    .ThenBy(item => item.OriginalIndex)
                    .ToList();
                _scrapedItems.Clear();
                foreach (var item in sortedItems) _scrapedItems.Add(item);

                RecalculateDuplicates();
                NettruyenLog($"Cào dữ liệu hoàn tất! Tổng cộng thu thập được {_scrapedItems.Count} liên kết độc nhất.");
                lblStatus.Text = "Crawling completed successfully.";
                lblLinkCount.Text = _scrapedItems.Count.ToString();
            }
            catch (OperationCanceledException)
            {
                NettruyenLog("Đã hủy cào theo yêu cầu người dùng.");
                lblStatus.Text = "Crawling cancelled.";
            }
            catch (Exception ex)
            {
                NettruyenLog($"Lỗi nghiêm trọng khi cào: {ex.Message}");
                lblStatus.Text = "Crawling failed.";
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                btnNettruyenScrape.Content = "GET LINK";
                btnNettruyenScrape.IsEnabled = true;
                if (btnNettruyenCrawlMore != null)
                {
                    btnNettruyenCrawlMore.Content = "GET MORE";
                    btnNettruyenCrawlMore.IsEnabled = true;
                }
                btnNettruyenFetchInfo.IsEnabled = true;
            }
        }

        private void BtnNettruyenPasteDirect_Click(object sender, RoutedEventArgs e)
        {
            var win = new DirectDownloadWindow(isNhentai: false);
            win.Owner = this;
            win.OnImport = async (links) =>
            {
                if (links != null && links.Any())
                {
                    await ImportNettruyenDirectLinksAsync(links);
                }
            };
            win.Show();
        }

        private async Task ImportNettruyenDirectLinksAsync(List<string> links, bool showMessageBox = true)
        {
            btnNettruyenScrape.IsEnabled = false;
            btnNettruyenFetchInfo.IsEnabled = false;
            
            progressBar.Value = 0;
            progressBar.IsIndeterminate = false;

            int total = links.Count;
            int imported = 0;
            int failed = 0;

            NettruyenLog($"[Import] Bắt đầu phân tích và nhập {total} liên kết trực tiếp...");
            lblStatus.Text = $"Importing 0/{total} links...";

            try
            {
                for (int i = 0; i < total; i++)
                {
                    string link = links[i].Trim().TrimEnd('/');
                    
                    if (!link.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                        !link.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        link = "https://" + link;
                    }

                    lblStatus.Text = $"[{i + 1}/{total}] Đang phân tích: {link}";

                    try
                    {
                        if (_scrapedItems.Any(item => item.Link.Equals(link, StringComparison.OrdinalIgnoreCase)))
                        {
                            NettruyenLog($"[Import] Bỏ qua liên kết đã tồn tại: {link}");
                            imported++;
                            continue;
                        }

                        bool captchaOk = await SolveNettruyenCaptchaIfNeededAsync(link);
                        if (!captchaOk)
                        {
                            NettruyenLog($"[Import] Không thể bypass Cloudflare cho: {link}");
                            failed++;
                            continue;
                        }
                        string html = await FetchStringAsync(link, _downloadCts?.Token ?? CancellationToken.None);
                        var titleMatch = Regex.Match(html, @"<title>\s*(.*?)\s*</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                        string title = "Manga - " + link.Split('/').Last();
                        string latestChapText = "";
                        if (titleMatch.Success)
                        {
                            string rawTitle = WebUtility.HtmlDecode(titleMatch.Groups[1].Value).Trim();
                            ParseMangaNameAndLatestChap(rawTitle, out string mangaName, out latestChapText);
                            title = mangaName;
                        }

                        Dispatcher.BeginInvoke((Action)(() =>
                        {
                            _scrapedItems.Add(new GalleryItem
                            {
                                Link = link,
                                Name = FormatGalleryTitle(title),
                                OriginalIndex = _scrapedItems.Count,
                                IsChecked = true,
                                HasNoChapters = false,
                                LinkCount = latestChapText
                            });
                        }));

                        NettruyenLog($"[Import {i + 1}/{total}] Thành công: {title}");
                        imported++;
                    }
                    catch (Exception ex)
                    {
                        NettruyenLog($"[Import] Lỗi xử lý link '{link}': {ex.Message}");
                        failed++;

                        string fallbackTitle = "Fallback - Nettruyen - " + link.Split('/').Last();
                        Dispatcher.BeginInvoke((Action)(() =>
                        {
                            _scrapedItems.Add(new GalleryItem
                            {
                                Link = link,
                                Name = fallbackTitle,
                                OriginalIndex = _scrapedItems.Count,
                                IsChecked = true,
                                HasNoChapters = false
                            });
                        }));
                    }

                    double pct = ((double)(i + 1) / total) * 100;
                    progressBar.Value = pct;
                    lblLinkCount.Text = _scrapedItems.Count.ToString();
                }

                RecalculateDuplicates();
                lblLinkCount.Text = _scrapedItems.Count.ToString();
                NettruyenLog($"[Import] Nhập hoàn tất! Thành công: {imported}, Lỗi/Fallback: {failed}.");
                lblStatus.Text = $"Import completed. Success: {imported}, Failed: {failed}.";
                
                if (showMessageBox)
                {
                    MessageBox.Show($"Đã nhập thành công {total} đường dẫn!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            finally
            {
                btnNettruyenScrape.IsEnabled = true;
                btnNettruyenFetchInfo.IsEnabled = true;
                if (btnStartDownload != null) btnStartDownload.IsEnabled = true;
                progressBar.Value = 100;
            }
        }

        private async Task DownloadNettruyenGalleryAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem = null, ChapterFilter chapterFilter = null)
        {
            string cleanLink = item.Link.TrimEnd('/');
            string activeDomain = ExtractNettruyenBaseUrl(cleanLink);

            var uri = new Uri(cleanLink);
            string parentPath = uri.AbsolutePath.TrimEnd('/');
            var segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            // Detail Page segment pattern: /truyen-tranh/{slug}
            // Chapter Page segment pattern: /truyen-tranh/{slug}/chuong-{num} or similar
            bool isDetailPage = segments.Length == 2 && segments[0].Equals("truyen-tranh", StringComparison.OrdinalIgnoreCase);

            if (isDetailPage)
            {
                if (chapterFilter == null)
                {
                    var pendingFromProcess = LoadPendingChapterLinksFromProcess(rootFolder, "nettruyen", item);
                    if (pendingFromProcess != null)
                    {
                        if (pendingFromProcess.Count == 0)
                        {
                            Log($"[nettruyen] Process cho '{item.Name}' đã hoàn tất, bỏ qua Download All.");
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

                        Log($"[nettruyen] Resume từ process: còn {pendingFromProcess.Count} chapter cần tải cho '{item.Name}'.");
                        await DownloadNettruyenPendingChaptersAsync(item, rootFolder, token, queueItem, pendingFromProcess);
                        return;
                    }
                }

                bool captchaOk = await SolveNettruyenCaptchaIfNeededAsync(cleanLink);
                if (!captchaOk)
                {
                    throw new Exception("Không thể vượt qua Cloudflare captcha.");
                }
                
                string html = "";
                if (!string.IsNullOrEmpty(_lastCaptchaResolvedHtml))
                {
                    html = NormalizeNettruyenHtml(_lastCaptchaResolvedHtml);
                    _lastCaptchaResolvedHtml = null; // Clear it
                    Log("[nettruyen] Sử dụng HTML đã nạp đầy đủ từ trình duyệt giải captcha.");
                }
                else
                {
                    html = NormalizeNettruyenHtml(await FetchStringAsync(cleanLink, _downloadCts?.Token ?? CancellationToken.None));
                }

                // Update manga title from <title> tag if available
                var titleMatch = Regex.Match(html, @"<title>\s*(.*?)\s*</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (titleMatch.Success)
                {
                    string rawTitle = WebUtility.HtmlDecode(titleMatch.Groups[1].Value).Trim();
                    string[] commonSuffixes = { " - NetTruyen", " - Nettruyen", " | NetTruyen", " | Nettruyen" };
                    foreach (var suffix in commonSuffixes)
                    {
                        if (rawTitle.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        {
                            rawTitle = rawTitle.Substring(0, rawTitle.Length - suffix.Length).Trim();
                        }
                    }
                    item.Name = FormatGalleryTitle(rawTitle);
                }

                // Try to find story/comic ID to fetch full chapter list from AJAX service
                string storyId = null;
                var idMatch = Regex.Match(html, @"gOpts\.comicId\s*=\s*['""]?(?<id>\d+)['""]?", RegexOptions.IgnoreCase);
                if (!idMatch.Success)
                {
                    idMatch = Regex.Match(html, @"id=[""'](?:story_id|storyId|comicId)[""'][^>]*value=[""'\s]?(?<id>\d+)[""'\s]?", RegexOptions.IgnoreCase);
                }
                if (!idMatch.Success)
                {
                    idMatch = Regex.Match(html, @"value=[""'\s]?(?<id>\d+)[""'\s]?[^>]*id=[""'](?:story_id|storyId|comicId)[""']", RegexOptions.IgnoreCase);
                }
                if (!idMatch.Success)
                {
                    idMatch = Regex.Match(html, @"(?:story_id|storyId|comicId)\s*=\s*(?:[""']?(?<id>\d+)[""']?|\d+)", RegexOptions.IgnoreCase);
                }
                if (!idMatch.Success)
                {
                    idMatch = Regex.Match(html, @"data-id=[""'](?<id>\d+)[""']", RegexOptions.IgnoreCase);
                }

                if (idMatch.Success)
                {
                    storyId = idMatch.Groups["id"].Value;
                }

                string chapterListHtml = html;
                string expandedChapterListHtml = null;
                bool expandedViaWebView = false;

                async Task<string> GetExpandedChapterListHtmlAsync()
                {
                    if (expandedChapterListHtml == null)
                    {
                        expandedChapterListHtml = await LoadExpandedNettruyenChapterHtmlAsync(cleanLink);
                    }

                    return expandedChapterListHtml;
                }

                bool pageHasViewMore = HasNettruyenViewMoreButton(html);
                if (pageHasViewMore)
                {
                    Log("[nettruyen] Thấy nút 'Xem thêm'. Đang click để bung danh sách chapter...");
                    string webViewHtml = await GetExpandedChapterListHtmlAsync();
                    if (!string.IsNullOrEmpty(webViewHtml))
                    {
                        chapterListHtml = webViewHtml;
                        expandedViaWebView = true;
                        Log("[nettruyen] Đã lấy HTML đầy đủ sau khi click 'Xem thêm'.");
                    }
                }

                if (!string.IsNullOrEmpty(storyId))
                {
                    bool loadedChapters = false;
                    
                    // Try ProcessChapterList first
                    try
                    {
                        string ajaxUrl = $"{activeDomain}/Comic/Services/ComicService.asmx/ProcessChapterList";
                        using (var request = new HttpRequestMessage(HttpMethod.Post, ajaxUrl))
                        {
                            request.Headers.Referrer = new Uri(cleanLink);
                            request.Content = new StringContent($"{{\"comicId\":{storyId}}}", System.Text.Encoding.UTF8, "application/json");
                            
                            using (var response = await _httpClient.SendAsync(request))
                            {
                                if (response.IsSuccessStatusCode)
                                {
                                    string jsonResponse = await response.Content.ReadAsStringAsync();
                                    var dMatch = Regex.Match(jsonResponse, @"""d""\s*:\s*""(?<htmlContent>.*?)""\s*}", RegexOptions.Singleline);
                                    if (dMatch.Success)
                                    {
                                        string unescapedHtml = NormalizeNettruyenHtml(Regex.Unescape(dMatch.Groups["htmlContent"].Value));
                                        if (!string.IsNullOrWhiteSpace(unescapedHtml) && unescapedHtml.Length > 100)
                                        {
                                            var tempLinks = ExtractNettruyenChapterLinks(unescapedHtml, activeDomain, parentPath);
                                            if (tempLinks.Count > 0)
                                            {
                                                chapterListHtml = unescapedHtml;
                                                Log($"[nettruyen] Tải thành công danh sách toàn bộ chương qua ProcessChapterList (ID: {storyId}).");
                                                loadedChapters = true;
                                            }
                                            else
                                            {
                                                Log($"[nettruyen] ProcessChapterList trả về HTML không chứa chương nào. Bỏ qua.");
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[nettruyen] Lỗi khi lấy chương qua ProcessChapterList: {ex.Message}. Thử GetListChapter...");
                    }

                    // Fallback to GetListChapter
                    if (!loadedChapters)
                    {
                        try
                        {
                            string ajaxUrl = $"{activeDomain}/Comic/Services/ComicService.asmx/GetListChapter";
                            using (var request = new HttpRequestMessage(HttpMethod.Post, ajaxUrl))
                            {
                                request.Headers.Referrer = new Uri(cleanLink);
                                request.Content = new StringContent($"{{\"id\":{storyId}}}", System.Text.Encoding.UTF8, "application/json");
                                
                                using (var response = await _httpClient.SendAsync(request))
                                {
                                    if (response.IsSuccessStatusCode)
                                    {
                                        string jsonResponse = await response.Content.ReadAsStringAsync();
                                        var dMatch = Regex.Match(jsonResponse, @"""d""\s*:\s*""(?<htmlContent>.*?)""\s*}", RegexOptions.Singleline);
                                        if (dMatch.Success)
                                        {
                                            string unescapedHtml = NormalizeNettruyenHtml(Regex.Unescape(dMatch.Groups["htmlContent"].Value));
                                            if (!string.IsNullOrWhiteSpace(unescapedHtml))
                                            {
                                                var tempLinks = ExtractNettruyenChapterLinks(unescapedHtml, activeDomain, parentPath);
                                                if (tempLinks.Count > 0)
                                                {
                                                    chapterListHtml = unescapedHtml;
                                                    Log($"[nettruyen] Tải thành công danh sách toàn bộ chương qua GetListChapter (ID: {storyId}).");
                                                    loadedChapters = true;
                                                }
                                                else
                                                {
                                                    Log($"[nettruyen] GetListChapter trả về HTML không chứa chương nào. Bỏ qua.");
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"[nettruyen] Lỗi khi lấy toàn bộ chương qua GetListChapter: {ex.Message}. Sẽ dùng danh sách chương mặc định.");
                        }
                    }
                }

                var chapterLinks = ExtractNettruyenChapterLinks(chapterListHtml, activeDomain, parentPath);

                // If AJAX succeeded but page originally had "Xem thêm", verify AJAX returned more chapters than visible HTML
                if (!expandedViaWebView && chapterListHtml != html && pageHasViewMore)
                {
                    var htmlVisibleLinks = ExtractNettruyenChapterLinks(html, activeDomain, parentPath);
                    if (chapterLinks.Count <= htmlVisibleLinks.Count)
                    {
                        Log($"[nettruyen] AJAX trả về {chapterLinks.Count} chương, HTML visible có {htmlVisibleLinks.Count}. AJAX không bung thêm. Mở trình duyệt click 'Xem thêm'...");
                        string webViewHtml = await GetExpandedChapterListHtmlAsync();
                        if (!string.IsNullOrEmpty(webViewHtml))
                        {
                            var webViewLinks = ExtractNettruyenChapterLinks(webViewHtml, activeDomain, parentPath);
                            if (webViewLinks.Count > chapterLinks.Count)
                            {
                                chapterListHtml = webViewHtml;
                                chapterLinks = webViewLinks;
                                Log($"[nettruyen] WebView bung được {webViewLinks.Count} chương (trước đó AJAX: {htmlVisibleLinks.Count}).");
                            }
                        }
                    }
                }

                double expectedLatestChapter = ParseChapterNumberFromText(item.LinkCount);
                bool looksIncomplete = chapterLinks.Count > 0 &&
                    ((expectedLatestChapter >= 20 && chapterLinks.Count + 5 < expectedLatestChapter) ||
                     (pageHasViewMore && expectedLatestChapter == 0));

                if (looksIncomplete)
                {
                    int originalChapterCount = chapterLinks.Count;
                    string reason = expectedLatestChapter > 0
                        ? $"{originalChapterCount} link, chap mới nhất ~ {expectedLatestChapter:0.##}"
                        : $"{originalChapterCount} link, trang có nút 'Xem thêm'";
                    Log($"[nettruyen] Danh sách chương có vẻ thiếu ({reason}). Thử mở trình duyệt để bung đủ danh sách...");
                    string webViewHtml = await GetExpandedChapterListHtmlAsync();
                    if (!string.IsNullOrEmpty(webViewHtml))
                    {
                        var retriedLinks = ExtractNettruyenChapterLinks(webViewHtml, activeDomain, parentPath);
                        if (retriedLinks.Count > originalChapterCount)
                        {
                            chapterListHtml = webViewHtml;
                            chapterLinks = retriedLinks;
                            Log($"[nettruyen] Đã mở rộng danh sách chương từ {originalChapterCount} lên {retriedLinks.Count} link.");
                        }
                    }
                }

                if (chapterLinks.Count == 0)
                {
                    Log($"[nettruyen] Không tìm thấy chương nào trong '{item.Name}'. Thử tải trực tiếp trang này như một chapter...");
                    await DownloadNettruyenChapterAsync(item, rootFolder, token, queueItem, isParentQueue: false);
                    return;
                }

                // Sort chapters in ascending order
                chapterLinks = chapterLinks.OrderBy(ParseChapterNumber).ToList();

                var totalFoundChapters = chapterLinks.Count;
                if (chapterFilter != null)
                {
                    var filtered = new List<string>();
                    foreach (var link in chapterLinks)
                    {
                        double chapNum = ParseChapterNumber(link);
                        if (chapterFilter.IsMatch(chapNum))
                        {
                            filtered.Add(link);
                        }
                    }
                    chapterLinks = filtered;
                    if (chapterLinks.Count == 0)
                    {
                        Log($"[nettruyen] Không có chương nào trùng khớp với bộ lọc đã chọn trong tổng số {totalFoundChapters} chương của '{item.Name}'.");
                        if (queueItem != null)
                        {
                            Dispatcher.BeginInvoke((Action)(() => {
                                queueItem.Status = "Completed";
                                queueItem.CurrentProcess = "Không có chương trùng khớp bộ lọc";
                            }));
                        }
                        return;
                    }
                }
                else
                {
                    chapterLinks = FilterPendingChapterLinksFromProcess(rootFolder, "nettruyen", item, chapterLinks);
                    if (chapterLinks.Count == 0)
                    {
                        NettruyenLog($"Tất cả chapter của '{item.Name}' đã Done theo process.");
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

                NettruyenLog($"Phát hiện {chapterLinks.Count} chương cho truyện '{item.Name}'. Bắt đầu tải lần lượt...");

                await DownloadNettruyenPendingChaptersAsync(item, rootFolder, token, queueItem, chapterLinks);

                Dispatcher.BeginInvoke((Action)(() =>
                {
                    item.LinkCount = chapterLinks.Count.ToString();
                }));
            }
            else
            {
                // Direct Chapter page
                await DownloadNettruyenChapterAsync(item, rootFolder, token, queueItem, isParentQueue: false);
            }
        }

        private async Task DownloadNettruyenPendingChaptersAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem, IList<string> chapterLinks)
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
            for (int idx = 0; idx < chapterLinks.Count; idx++)
            {
                token.ThrowIfCancellationRequested();
                string chapLink = chapterLinks[idx];

                var chapItem = new GalleryItem { Link = chapLink, Name = item.Name };
                bool chapterCompleted = await DownloadNettruyenChapterAsync(chapItem, rootFolder, token, queueItem, isParentQueue: true);
                if (chapterCompleted)
                {
                    MarkChapterProcessDone(rootFolder, "nettruyen", item, chapLink);
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

        private async Task<bool> DownloadNettruyenChapterAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem = null, bool isParentQueue = false)
        {
            bool captchaOk = await SolveNettruyenCaptchaIfNeededAsync(item.Link);
            if (!captchaOk)
            {
                throw new Exception("Không thể vượt qua Cloudflare captcha.");
            }
            string html = await FetchStringAsync(item.Link, _downloadCts?.Token ?? CancellationToken.None);

            string mangaTitle = item.Name;
            string chapterTitle = "Chương 1";

            // Try to extract clean titles from page
            var titleMatch = Regex.Match(html, @"<title>\s*(.*?)\s*</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (titleMatch.Success)
            {
                string rawTitle = WebUtility.HtmlDecode(titleMatch.Groups[1].Value).Trim();
                string[] commonSuffixes = { " - NetTruyen", " - Nettruyen", " | NetTruyen", " | Nettruyen" };
                foreach (var suffix in commonSuffixes)
                {
                    if (rawTitle.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        rawTitle = rawTitle.Substring(0, rawTitle.Length - suffix.Length).Trim();
                    }
                }

                string[] parts = rawTitle.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    int chapPartIdx = -1;
                    for (int i = parts.Length - 1; i >= 1; i--)
                    {
                        if (Regex.IsMatch(parts[i], @"\b(chap|chương|chapter|chuong)\b", RegexOptions.IgnoreCase))
                        {
                            chapPartIdx = i;
                            break;
                        }
                    }
                    if (chapPartIdx > 0)
                    {
                        mangaTitle = string.Join(" - ", parts, 0, chapPartIdx).Trim();
                        chapterTitle = string.Join(" - ", parts, chapPartIdx, parts.Length - chapPartIdx).Trim();
                    }
                    else
                    {
                        mangaTitle = string.Join(" - ", parts, 0, parts.Length - 1).Trim();
                        chapterTitle = parts[parts.Length - 1].Trim();
                    }
                }
                else if (parts.Length == 1)
                {
                    chapterTitle = parts[0].Trim();
                }
            }

            // Clean Manga Title
            string cleanManga = mangaTitle;
            cleanManga = Regex.Replace(cleanManga, @"\s+(?:chương|Chương|chap|Chap)\s+mới\s+nhất\s+\d+.*", "", RegexOptions.IgnoreCase);
            cleanManga = cleanManga.Trim();
            if (string.IsNullOrEmpty(cleanManga))
            {
                cleanManga = "Unknown Nettruyen Manga";
            }

            // Clean Chapter Title
            string cleanChapter = chapterTitle;
            var chapMatch = Regex.Match(chapterTitle, @"(chap|chương|chapter|chuong)\s*(?<num>\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (chapMatch.Success)
            {
                string type = chapMatch.Groups[1].Value.ToLower();
                if (type == "chapter" || type == "chuong") type = "chap";
                string num = chapMatch.Groups["num"].Value;
                cleanChapter = $"{type} {num}";
            }
            else
            {
                cleanChapter = Regex.Replace(cleanChapter, @"\s+Tiếng\s+Việt\s+NetTruyen.*", "", RegexOptions.IgnoreCase);
                cleanChapter = cleanChapter.Trim();
            }

            cleanChapter = NormalizeChapterLabel(cleanChapter);
            string safeManga = GetSafePathName(cleanManga);
            string safeChapter = GetDownloadChapterFolderName(cleanManga, cleanChapter);
            string progressKey = $"nettruyen|{GetSafePathName(cleanManga)}";
            int totalChaptersForLog = queueItem != null ? Math.Max(1, queueItem.TotalChapters) : 1;
            int currentChapterForLog = queueItem != null ? Math.Max(1, Math.Min(queueItem.CompletedChapters + 1, totalChaptersForLog)) : 1;
            UpsertMainLogLine(progressKey, $"[nettruyen] Đang tải {cleanManga} - {cleanChapter} ({currentChapterForLog}/{totalChaptersForLog})");
            
            string siteRootFolder = GetSiteDownloadRoot(rootFolder, "nettruyen");
            string unmergedPath = Path.Combine(siteRootFolder, $"{safeManga}-{safeChapter}");
            string mergedPath = Path.Combine(siteRootFolder, safeManga, safeChapter);
            string finalTargetFolder = _isSingleComicFolderType ? mergedPath : unmergedPath;
            string tempFolder = BuildStableTempFolderPath(siteRootFolder, "nettruyen", safeManga, safeChapter, item.Link);
            Directory.CreateDirectory(tempFolder);
            RegisterTempFolder(tempFolder);
            // Isolate images inside reading area
            string safeHtml = GetSafeChapterHtml(html);
            int startIndex = -1;
            string[] containerMarkers = new[]
            {
                "class=\"page-chapter\"",
                "class=\"reading-detail\"",
                "class=\"chapter-content\"",
                "class=\"box-chap\"",
                "id=\"chapter_content\""
            };

            foreach (var marker in containerMarkers)
            {
                int index = safeHtml.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index != -1)
                {
                    startIndex = index;
                    break;
                }
            }

            string contentArea = safeHtml;
            if (startIndex != -1)
            {
                contentArea = safeHtml.Substring(startIndex);
            }

            // Extract all image URLs from isolated reading area
            var pageCandidateUrls = new List<List<string>>();
            var imgTags = Regex.Matches(contentArea, @"<img\s+[^>]*>", RegexOptions.IgnoreCase);
            
            foreach (Match imgTag in imgTags)
            {
                string tag = imgTag.Value;
                var candidates = new List<string>();

                Action<string> addAttr = (attrName) =>
                {
                    var match = Regex.Match(tag, attrName + @"=[""'](?<url>[^""']+)[""']", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        string val = match.Groups["url"].Value.Trim();
                        if (!string.IsNullOrEmpty(val))
                        {
                            if (val.StartsWith("//"))
                            {
                                val = "https:" + val;
                            }
                            else if (!val.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                                     !val.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                            {
                                string activeDomain = ExtractNettruyenBaseUrl(item.Link);
                                val = activeDomain + (val.StartsWith("/") ? "" : "/") + val;
                            }

                            if (val.Contains("/assets/") ||
                                val.EndsWith("logo.png", StringComparison.OrdinalIgnoreCase) ||
                                val.EndsWith("avatar.jpg", StringComparison.OrdinalIgnoreCase) ||
                                val.Contains("avatar") ||
                                val.Contains("loading") ||
                                val.Contains("spacer.gif") ||
                                val.Contains("transparent.gif") ||
                                val.Contains("/images/logo") ||
                                val.Contains("/images/favicon") ||
                                val.Contains("facebook.com") ||
                                val.Contains("banner") ||
                                val.Contains("advertisement") ||
                                val.Contains("nettruyenviet.webp") ||
                                Regex.IsMatch(val, @"/0{1,3}\.(jpg|jpeg|png|webp|gif|bmp)$", RegexOptions.IgnoreCase))
                            {
                                return;
                            }

                            if (!candidates.Contains(val))
                            {
                                candidates.Add(val);
                            }
                        }
                    }
                };

                addAttr("data-original");
                addAttr("data-src");
                addAttr("data-sv1");
                addAttr("data-sv2");
                addAttr("src");

                if (candidates.Count > 0)
                {
                    pageCandidateUrls.Add(candidates);
                }
            }

            if (pageCandidateUrls.Count == 0)
            {
                throw new Exception($"Không thể tìm thấy hình ảnh nào của chương truyện '{chapterTitle}' để tải xuống.");
            }

            var imageUrls = pageCandidateUrls.Select(list => list[0]).ToList();

            WriteTempProgressLog(tempFolder, item, "Downloading", 0, imageUrls.Count, "0/0 pages", $"Bắt đầu tải {cleanChapter}");

            // Connection settings
            int maxThreads = GetBookConnectionLimit(queueItem ?? item);

            NettruyenLog($"Bắt đầu tải {imageUrls.Count} trang của chapter '{chapterTitle}' với {maxThreads} kết nối song song...");

            if (queueItem != null && !isParentQueue)
            {
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    queueItem.TotalChapters = imageUrls.Count;
                    queueItem.CompletedChapters = 0;
                    queueItem.DownloadingChapter = cleanChapter;
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
                            Exception lastEx = null;
                            foreach (var candidateUrl in pageCandidateUrls[index])
                            {
                                try
                                {
                                    // Pass item.Link (which is the chapter page URL) as the Referer to bypass hotlinking protection
                                    await DownloadUrlToFileWithRefererAsync(candidateUrl, item.Link, localFilePath, token, isTruyenqq: true);
                                    downloadedPath = localFilePath;
                                    lastEx = null;
                                    break; // Success!
                                }
                                catch (Exception ex)
                                {
                                    lastEx = ex;
                                    Log($"[nettruyen] Lỗi tải trang {index + 1} từ server '{candidateUrl}': {ex.Message}. Thử server khác...");
                                }
                            }

                            if (downloadedPath == null && lastEx != null)
                            {
                                lock (lockObj)
                                {
                                    if (queueItem != null)
                                    {
                                        string pageName = Path.GetFileNameWithoutExtension(pageFilenames[index]);
                                        queueItem.AddError(cleanChapter, index + 1, lastEx.Message, imgUrl, item.Link, pageName);
                                        RecordCheckError("nettruyen", queueItem.Name ?? cleanManga, cleanChapter, index + 1, lastEx.Message, imgUrl, pageName);
                                    }
                                    Log($"[nettruyen] Lỗi tải trang {index + 1} của chapter '{cleanChapter}' ở tất cả server: {lastEx.Message}");
                                }
                            }

                            pageWatch.Stop();
                            lock (lockObj)
                            {
                                completedPages++;
                                long downloadedBytes = !string.IsNullOrWhiteSpace(downloadedPath) && File.Exists(downloadedPath) ? new FileInfo(downloadedPath).Length : 0;
                                string processText = isParentQueue ? $"{cleanChapter} (trang {completedPages}/{imageUrls.Count})" : $"Trang {completedPages}/{imageUrls.Count}";
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
            }

            if (Directory.Exists(tempFolder))
            {
                WriteTempProgressLog(tempFolder, item, "Done", imageUrls.Count, imageUrls.Count, isParentQueue ? $"{cleanChapter} (trang {imageUrls.Count}/{imageUrls.Count})" : $"Trang {imageUrls.Count}/{imageUrls.Count}", "Download completed");
                MoveTempFolderToTarget(tempFolder, finalTargetFolder, "nettruyen");
                UpsertMainLogLine(progressKey, $"[nettruyen] Đã tải xong {cleanManga} - {cleanChapter} ({currentChapterForLog}/{totalChaptersForLog})");
            }

            return ValidateDownloadedFiles(finalTargetFolder, imageUrls.Count, queueItem, cleanChapter, pageImageUrls: null, chapterUrl: item.Link);
        }
    }
}
#pragma warning restore 4014

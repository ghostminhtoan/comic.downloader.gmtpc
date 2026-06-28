using System;
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
        private void HentaieraLog(string message)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                string logLine = $"[{DateTime.Now:HH:mm:ss}] {message}\r\n";
                bool isError = IsErrorMessage(message);
                AppendLogLine(txtHentaieraLog, logLine, isError);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private string GetHentaieraPageUrl(string baseUrl, int page)
        {
            baseUrl = baseUrl.Trim();
            // Remove page parameter (support optional amp;)
            string cleanUrl = Regex.Replace(baseUrl, @"([?&])(?:amp;)?page=\d+(&|$)", "$1", RegexOptions.IgnoreCase);
            cleanUrl = cleanUrl.TrimEnd('&', '?');

            string separator = cleanUrl.Contains("?") ? "&" : "?";
            return $"{cleanUrl}{separator}page={page}";
        }

        private async void BtnHentaieraFetchInfo_Click(object sender, RoutedEventArgs e)
        {
            string url = txtHentaieraTagUrl.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("Vui lòng nhập URL hợp lệ (Please enter a valid URL).", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            url = NormalizeHentaieraUrl(url);
            txtHentaieraTagUrl.Text = url;

            btnHentaieraFetchInfo.IsEnabled = false;
            lblStatus.Text = "Đang phân tích trang hentaiera.com...";
            progressBar.IsIndeterminate = true;
            HentaieraLog($"Đang phân tích URL: {url}");

            try
            {
                bool ok = await SolveHentaieraCaptchaIfNeededAsync(url);
                if (!ok)
                {
                    lblStatus.Text = "Blocked by Cloudflare.";
                    btnHentaieraFetchInfo.IsEnabled = true;
                    progressBar.IsIndeterminate = false;
                    return;
                }

                string html = null;
                try
                {
                    html = await FetchStringAsync(url, _downloadCts?.Token ?? CancellationToken.None);
                    if (html.Contains("Just a moment...") || html.Contains("cloudflare-challenge") || html.Contains("cf-challenge"))
                    {
                        throw new HttpRequestException("Cloudflare challenge detected");
                    }
                }
                catch (HttpRequestException)
                {
                    bool ok2 = await SolveHentaieraCaptchaIfNeededAsync(url);
                    if (!ok2)
                    {
                        lblStatus.Text = "Blocked by Cloudflare.";
                        btnHentaieraFetchInfo.IsEnabled = true;
                        progressBar.IsIndeterminate = false;
                        return;
                    }
                    html = await FetchStringAsync(url, _downloadCts?.Token ?? CancellationToken.None);
                }
                
                int maxPage = 1;
                // Check all page= href links
                var pageMatches = Regex.Matches(html, @"[?&]page=(\d+)", RegexOptions.IgnoreCase);
                foreach (Match m in pageMatches)
                {
                    if (int.TryParse(m.Groups[1].Value, out int pageNum))
                    {
                        if (pageNum > maxPage) maxPage = pageNum;
                    }
                }

                txtHentaieraTotalPages.Text = maxPage.ToString();
                txtHentaieraPageTo.Text = maxPage.ToString();
                
                HentaieraLog($"Phân tích hoàn tất. Phát hiện tối đa {maxPage} trang.");
                lblStatus.Text = $"Analysis complete. Found {maxPage} pages.";
            }
            catch (Exception ex)
            {
                HentaieraLog($"Lỗi khi phân tích: {ex.Message}");
                txtHentaieraTotalPages.Text = "1";
                lblStatus.Text = "Analysis failed.";
            }
            finally
            {
                btnHentaieraFetchInfo.IsEnabled = true;
                progressBar.IsIndeterminate = false;
            }
        }

        private void TxtHentaieraTotalPages_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (txtHentaieraPageTo != null && txtHentaieraTotalPages != null)
            {
                txtHentaieraPageTo.Text = txtHentaieraTotalPages.Text;
            }
        }

        private async void BtnHentaieraScrape_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                btnHentaieraScrape.Content = "CANCELLING...";
                btnHentaieraScrape.IsEnabled = false;
                if (btnHentaieraCrawlMore != null) btnHentaieraCrawlMore.IsEnabled = false;
                return;
            }
            await ScrapeHentaieraAsync(clearExisting: true);
        }

        private async void BtnHentaieraCrawlMore_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                if (btnHentaieraCrawlMore != null)
                {
                    btnHentaieraCrawlMore.Content = "CANCELLING...";
                    btnHentaieraCrawlMore.IsEnabled = false;
                }
                btnHentaieraScrape.IsEnabled = false;
                return;
            }
            await ScrapeHentaieraAsync(clearExisting: false);
        }

        private async Task ScrapeHentaieraAsync(bool clearExisting)
        {
            string baseUrl = txtHentaieraTagUrl.Text.Trim();
            if (string.IsNullOrEmpty(baseUrl))
            {
                MessageBox.Show("Vui lòng nhập URL hợp lệ (Please enter a valid URL).", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                baseUrl = "https://" + baseUrl;
            }

            baseUrl = NormalizeHentaieraUrl(baseUrl);
            txtHentaieraTagUrl.Text = baseUrl;

            if (!int.TryParse(txtHentaieraPageFrom.Text, out int pageFrom) || pageFrom < 1)
            {
                MessageBox.Show("Trang bắt đầu không hợp lệ (Invalid 'From Page').", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!int.TryParse(txtHentaieraPageTo.Text, out int pageTo) || pageTo < pageFrom)
            {
                MessageBox.Show("Trang kết thúc không hợp lệ (Invalid 'To Page').", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            btnHentaieraScrape.Content = "STOP CRAWLER";
            if (btnHentaieraCrawlMore != null)
            {
                btnHentaieraCrawlMore.Content = "STOP CRAWLER";
            }
            btnHentaieraFetchInfo.IsEnabled = false;
            lblStatus.Text = "Đang cào hentaiera.com...";
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

            HentaieraLog($"Bắt đầu cào từ trang {pageFrom} đến {pageTo}...");

            try
            {
                int totalPages = pageTo - pageFrom + 1;
                int pagesProcessed = 0;

                for (int page = pageFrom; page <= pageTo; page++)
                {
                    token.ThrowIfCancellationRequested();

                    string pageUrl = GetHentaieraPageUrl(baseUrl, page);
                    HentaieraLog($"Đang tải trang {page}: {pageUrl}");

                    bool ok = await SolveHentaieraCaptchaIfNeededAsync(pageUrl);
                    if (!ok)
                    {
                        throw new Exception("Bị chặn bởi Cloudflare Captcha trên hentaiera.com.");
                    }

                    string html = null;
                    try
                    {
                        html = await FetchStringAsync(pageUrl, _downloadCts?.Token ?? CancellationToken.None);
                        if (html.Contains("Just a moment...") || html.Contains("cloudflare-challenge") || html.Contains("cf-challenge"))
                        {
                            throw new HttpRequestException("Cloudflare challenge detected");
                        }
                    }
                    catch (HttpRequestException)
                    {
                        bool ok2 = await SolveHentaieraCaptchaIfNeededAsync(pageUrl);
                        if (!ok2)
                        {
                            throw new Exception("Bị chặn bởi Cloudflare Captcha trên hentaiera.com.");
                        }
                        html = await FetchStringAsync(pageUrl, _downloadCts?.Token ?? CancellationToken.None);
                    }
                    
                    // Match items with class gallery_title or caption containing the gallery link and title
                    var viewMatches = Regex.Matches(html, @"(?:<h2\s+class=""gallery_title""|<div\s+class=""(?:caption|title)"")[^>]*>\s*<a\s+[^>]*href=""[^""']*?/gallery/(?<id>\d+)/?""[^>]*>(?<title>[\s\S]*?)</a>", RegexOptions.IgnoreCase);
                    
                    int pageCount = 0;
                    foreach (Match match in viewMatches)
                    {
                        string id = match.Groups["id"].Value;
                        string titleRaw = match.Groups["title"].Value;
                        string fullLink = $"https://hentaiera.com/gallery/{id}/";

                        string title = Regex.Replace(titleRaw, @"<[^>]+>", "").Trim();

                        if (string.IsNullOrEmpty(title))
                        {
                            title = $"Gallery {id}";
                        }
                        else
                        {
                            title = WebUtility.HtmlDecode(title).Trim();
                        }
                        title = FormatGalleryTitle(title);

                        var existingItem = _scrapedItems.FirstOrDefault(item => item.Link.Equals(fullLink, StringComparison.OrdinalIgnoreCase));
                        if (existingItem == null)
                        {
                            _scrapedItems.Add(new GalleryItem
                            {
                                Link = fullLink,
                                Name = title,
                                OriginalIndex = _scrapedItems.Count,
                                IsChecked = false
                            });
                            pageCount++;
                        }
                    }

                    pagesProcessed++;
                    double progressPct = ((double)pagesProcessed / totalPages) * 100;
                    progressBar.Value = progressPct;
                    lblStatus.Text = $"Searching page {page}/{pageTo} ({progressPct:0}%)";
                    lblLinkCount.Text = _scrapedItems.Count.ToString(); // real-time update
                    HentaieraLog($"Trang {page} hoàn tất. Tìm thấy {pageCount} liên kết mới.");
                }

                // Sort items deterministically (by Name then OriginalIndex)
                var sortedItems = _scrapedItems
                    .OrderBy(item => item.Name)
                    .ThenBy(item => item.OriginalIndex)
                    .ToList();
                _scrapedItems.Clear();
                foreach (var item in sortedItems) _scrapedItems.Add(item);

                RecalculateDuplicates();
                HentaieraLog($"Cào dữ liệu hoàn tất! Tổng cộng thu thập được {_scrapedItems.Count} liên kết độc nhất.");
                lblStatus.Text = "Crawling completed successfully.";
                lblLinkCount.Text = _scrapedItems.Count.ToString();
            }
            catch (OperationCanceledException)
            {
                HentaieraLog("Đã hủy cào theo yêu cầu người dùng.");
                lblStatus.Text = "Crawling cancelled.";
            }
            catch (Exception ex)
            {
                HentaieraLog($"Lỗi nghiêm trọng khi cào: {ex.Message}");
                lblStatus.Text = "Crawling failed.";
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                btnHentaieraScrape.Content = "GET LINK";
                btnHentaieraScrape.IsEnabled = true;
                if (btnHentaieraCrawlMore != null)
                {
                    btnHentaieraCrawlMore.Content = "GET MORE";
                    btnHentaieraCrawlMore.IsEnabled = true;
                }
                btnHentaieraFetchInfo.IsEnabled = true;
            }
        }

        private void BtnHentaieraPasteDirect_Click(object sender, RoutedEventArgs e)
        {
            var win = new DirectDownloadWindow(isNhentai: false);
            win.Owner = this;
            win.OnImport = async (links) =>
            {
                if (links != null && links.Any())
                {
                    await ImportHentaieraDirectLinksAsync(links);
                }
            };
            win.Show();
        }

        private async Task ImportHentaieraDirectLinksAsync(System.Collections.Generic.List<string> links, bool showMessageBox = true)
        {
            btnHentaieraScrape.IsEnabled = false;
            btnHentaieraFetchInfo.IsEnabled = false;
            progressBar.Value = 0;
            progressBar.IsIndeterminate = false;

            int total = links.Count;
            int imported = 0;
            int failed = 0;

            HentaieraLog($"[Import] Bắt đầu phân tích và nhập {total} liên kết trực tiếp...");
            lblStatus.Text = $"Importing 0/{total} links...";

            try
            {
                for (int i = 0; i < total; i++)
                {
                    string link = links[i];
                    if (!string.IsNullOrEmpty(link))
                    {
                        link = link.Trim();
                        if (!link.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                            !link.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            link = "https://" + link;
                        }
                        link = NormalizeHentaieraUrl(link);
                    }
                    lblStatus.Text = $"[{i + 1}/{total}] Đang phân tích: {link}";

                    try
                    {
                        if (_scrapedItems.Any(item => item.Link.Equals(link, StringComparison.OrdinalIgnoreCase)))
                        {
                            HentaieraLog($"[Import] Bỏ qua liên kết đã tồn tại: {link}");
                            imported++;
                            continue;
                        }

                        // Try to scrape title from page
                        bool ok = await SolveHentaieraCaptchaIfNeededAsync(link);
                        if (!ok)
                        {
                            throw new Exception("Bị chặn bởi Cloudflare Captcha.");
                        }

                        string html = null;
                        try
                        {
                            html = await FetchStringAsync(link, _downloadCts?.Token ?? CancellationToken.None);
                            if (html.Contains("Just a moment...") || html.Contains("cloudflare-challenge") || html.Contains("cf-challenge"))
                            {
                                throw new HttpRequestException("Cloudflare challenge detected");
                            }
                        }
                        catch (HttpRequestException)
                        {
                            bool ok2 = await SolveHentaieraCaptchaIfNeededAsync(link);
                            if (!ok2)
                            {
                                throw new Exception("Bị chặn bởi Cloudflare Captcha.");
                            }
                            html = await FetchStringAsync(link, _downloadCts?.Token ?? CancellationToken.None);
                        }
                        var titleMatch = Regex.Match(html, @"<title[^>]*>\s*(.*?)\s*</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                        string title = "Gallery ID " + GetHentaieraGalleryIdFromLink(link);
                        
                        if (titleMatch.Success)
                        {
                            string titleRaw = titleMatch.Groups[1].Value;
                            title = Regex.Replace(titleRaw, @"<[^>]+>", ""); // Strip HTML tags
                            title = WebUtility.HtmlDecode(title).Trim();
                            
                            // Strip suffix like " - HentaiEra" or " | HentaiEra"
                            title = Regex.Replace(title, @"\s*[-|]\s*HentaiEra\s*$", "", RegexOptions.IgnoreCase);
                        }
                        title = FormatGalleryTitle(title);

                        Dispatcher.Invoke(() =>
                        {
                            _scrapedItems.Add(new GalleryItem
                            {
                                Link = link,
                                Name = title,
                                OriginalIndex = _scrapedItems.Count,
                                IsChecked = true,
                                HasNoChapters = false
                            });
                        });

                        HentaieraLog($"[Import {i + 1}/{total}] Thành công: {title}");
                        imported++;
                    }
                    catch (Exception ex)
                    {
                        HentaieraLog($"[Import] Lỗi xử lý link '{link}': {ex.Message}");
                        failed++;

                        string fallbackTitle = "Fallback - Hentaiera - " + GetHentaieraGalleryIdFromLink(link);
                        Dispatcher.Invoke(() =>
                        {
                            _scrapedItems.Add(new GalleryItem
                            {
                                Link = link,
                                Name = fallbackTitle,
                                OriginalIndex = _scrapedItems.Count,
                                IsChecked = true,
                                HasNoChapters = false
                            });
                        });
                    }

                    double pct = ((double)(i + 1) / total) * 100;
                    progressBar.Value = pct;
                    lblLinkCount.Text = _scrapedItems.Count.ToString(); // real-time update
                }

                RecalculateDuplicates();
                lblLinkCount.Text = _scrapedItems.Count.ToString();
                HentaieraLog($"[Import] Nhập hoàn tất! Thành công: {imported}, Lỗi/Fallback: {failed}.");
                lblStatus.Text = $"Import completed. Success: {imported}, Failed: {failed}.";
                
                if (showMessageBox)
                {
                    MessageBox.Show($"Đã nhập thành công {total} đường dẫn!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            finally
            {
                btnHentaieraScrape.IsEnabled = true;
                btnHentaieraFetchInfo.IsEnabled = true;
                if (btnStartDownload != null) btnStartDownload.IsEnabled = true;
                progressBar.Value = 100;
            }
        }

        private string GetHentaieraGalleryIdFromLink(string link)
        {
            var match = Regex.Match(link, @"/(?:gallery|view)/(\d+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            return "Unknown";
        }

        private string NormalizeHentaieraUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;

            // Match view link structure e.g. https://hentaiera.com/view/1431024/1/
            var match = Regex.Match(url, @"^(https?://[^/]+)?/view/(\d+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string domain = match.Groups[1].Value;
                if (string.IsNullOrEmpty(domain))
                {
                    domain = "https://hentaiera.com";
                }
                string id = match.Groups[2].Value;
                return $"{domain}/gallery/{id}/";
            }

            return url;
        }

        internal async Task<bool> SolveHentaieraCaptchaIfNeededAsync(string testUrl)
        {
            // Simple check if it returns Cloudflare title or fails with 403
            bool isBlocked = false;
            try
            {
                using (var response = await _httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, testUrl), HttpCompletionOption.ResponseHeadersRead))
                {
                    if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.ServiceUnavailable)
                    {
                        isBlocked = true;
                    }
                }
            }
            catch
            {
                isBlocked = true;
            }

            if (!isBlocked)
            {
                return true;
            }

            if (_isCaptchaWindowActive)
            {
                while (_isCaptchaWindowActive)
                {
                    await Task.Delay(500);
                }
                return true;
            }

            await _captchaSemaphore.WaitAsync();
            try
            {
                _isCaptchaWindowActive = true;
                _isDownloadPaused = true;
                HentaieraLog("[hentaiera.com] Phát hiện thử thách Cloudflare / Captcha. Mở trình duyệt giải tự động...");

                bool solved = false;
                try
                {
                    await await Dispatcher.InvokeAsync(async () =>
                    {
                        var captchaWin = CreateCaptchaWindow(testUrl, autoDeleteCookiesOnLoad: true, headlessAutomation: _lightNovelAutoFocusEnabled);
                        captchaWin.Owner = this;

                        if (await captchaWin.ShowNonBlockingAsync())
                        {
                            var resolvedUri = captchaWin.ResolvedUri ?? new Uri(testUrl);
                            var resolvedCookies = captchaWin.ResolvedCookies.GetCookies(resolvedUri);
                            foreach (Cookie cookie in resolvedCookies)
                            {
                                _cookieContainer.Add(resolvedUri, cookie);
                            }

                            if (!string.IsNullOrEmpty(captchaWin.UserAgent))
                            {
                                _httpClient.DefaultRequestHeaders.UserAgent.Clear();
                                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(captchaWin.UserAgent);
                            }
                            solved = true;
                        }
                    });
                }
                catch (Exception ex)
                {
                    HentaieraLog($"[Captcha Error] Lỗi giải captcha: {ex.Message}");
                }

                _isCaptchaWindowActive = false;
                _isDownloadPaused = false;
                return solved;
            }
            finally
            {
                _captchaSemaphore.Release();
            }
        }
    }
}

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

#pragma warning disable 4014
namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private void TruyenqqLog(string message)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                string logLine = $"[{DateTime.Now:HH:mm:ss}] {message}\r\n";
                bool isError = IsErrorMessage(message);
                AppendLogLine(txtTruyenqqLog, logLine, isError);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        internal async Task<bool> CheckIfTruyenqqBlockedAsync(string testUrl)
        {
            testUrl = ResolveTruyenqqRequestUrl(testUrl);
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

        private string ResolveTruyenqqRequestUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return url;
            }

            string normalized = NormalizeTruyenqqUrl(url);
            if (string.IsNullOrWhiteSpace(_truyenqqPreferredBaseUrl))
            {
                return normalized;
            }

            if (!Uri.TryCreate(_truyenqqPreferredBaseUrl, UriKind.Absolute, out Uri preferredBase))
            {
                return normalized;
            }

            var uri = new Uri(normalized);
            var builder = new UriBuilder(uri)
            {
                Scheme = preferredBase.Scheme,
                Host = preferredBase.Host,
                Port = preferredBase.Port
            };

            return builder.Uri.AbsoluteUri.TrimEnd('/');
        }
        internal async Task<bool> SolveTruyenqqCaptchaIfNeededAsync(string testUrl)
        {
            testUrl = ResolveTruyenqqRequestUrl(testUrl);
            bool isBlocked = await CheckIfTruyenqqBlockedAsync(testUrl);
            if (!isBlocked && !string.IsNullOrWhiteSpace(_truyenqqPreferredBaseUrl))
            {
                return true; // Not blocked
            }

            if (!isBlocked && string.IsNullOrWhiteSpace(_truyenqqPreferredBaseUrl))
            {
                // First-time setup: force one captcha pass so we can learn the active domain.
                isBlocked = true;
            }

            if (_isCaptchaWindowActive)
            {
                while (_isCaptchaWindowActive)
                {
                    await Task.Delay(500);
                }
                isBlocked = await CheckIfTruyenqqBlockedAsync(testUrl);
                if (!isBlocked)
                {
                    return true;
                }
            }

            await _captchaSemaphore.WaitAsync();
            try
            {
                // Re-check after acquiring lock
                isBlocked = await CheckIfTruyenqqBlockedAsync(testUrl);
                if (!isBlocked)
                {
                    return true;
                }

                _isCaptchaWindowActive = true;
                _isDownloadPaused = true;
                TruyenqqLog("Phát hiện thử thách Cloudflare / Captcha. Tạm dừng tải và đang mở trình duyệt giải tự động...");

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
                            _truyenqqPreferredBaseUrl = $"{resolvedUri.Scheme}://{resolvedUri.Host}";

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
                    TruyenqqLog("Giải captcha thành công. Tiếp tục tải...");
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

        private bool IsTruyenqqUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            try
            {
                var uri = new Uri(url);
                return IsTruyenqqHost(uri.Host);
            }
            catch
            {
                return Regex.IsMatch(url, @"(?:https?://)?(?:www\.)?truyenqq[a-zA-Z0-9-]*\.com", RegexOptions.IgnoreCase);
            }
        }

        private bool IsTruyenqqHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return false;
            return Regex.IsMatch(host, @"^(?:www\.)?truyenqq[a-zA-Z0-9-]*\.com$", RegexOptions.IgnoreCase);
        }

        private string NormalizeTruyenqqUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return "";
            }

            string normalized = url.Trim();
            if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "https://" + normalized;
            }

            if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri uri) || !IsTruyenqqHost(uri.Host))
            {
                throw new ArgumentException("URL phải thuộc domain truyenqq*.com.");
            }

            var builder = new UriBuilder(uri)
            {
                Fragment = ""
            };

            if (!string.IsNullOrWhiteSpace(_truyenqqPreferredBaseUrl) &&
                Uri.TryCreate(_truyenqqPreferredBaseUrl, UriKind.Absolute, out Uri preferredBase))
            {
                builder.Scheme = preferredBase.Scheme;
                builder.Host = preferredBase.Host;
                builder.Port = preferredBase.Port;
            }

            string path = builder.Path ?? "/";
            path = Regex.Replace(path, @"/{2,}", "/");
            if (path.Length > 1)
            {
                path = path.TrimEnd('/');
            }

            builder.Path = path;
            return builder.Uri.AbsoluteUri.TrimEnd('/');
        }

        private string StripTruyenqqBrandSuffix(string rawTitle)
        {
            if (string.IsNullOrWhiteSpace(rawTitle))
            {
                return rawTitle;
            }

            return Regex.Replace(
                rawTitle.Trim(),
                @"\s*[\-\|]\s*truy[eệ]nqq[a-zA-Z0-9-]*.*$",
                "",
                RegexOptions.IgnoreCase).Trim();
        }

        private string ExtractTruyenqqBaseUrl(string url)
        {
            if (!string.IsNullOrWhiteSpace(_truyenqqPreferredBaseUrl))
            {
                return _truyenqqPreferredBaseUrl.TrimEnd('/');
            }

            try
            {
                var uri = new Uri(NormalizeTruyenqqUrl(url));
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
            return "https://truyenqqko.com"; // Default fallback
        }

        private string GetTruyenqqPageUrl(string baseUrl, int page)
        {
            baseUrl = baseUrl.Trim();
            if (page == 1) return baseUrl;

            try
            {
                var uri = new Uri(baseUrl);
                string path = uri.AbsolutePath;
                path = Regex.Replace(path, @"/trang-\d+/?", "", RegexOptions.IgnoreCase);
                path = path.TrimEnd('/');
                
                string newPath = $"{path}/trang-{page}";
                var builder = new UriBuilder(uri)
                {
                    Path = newPath
                };
                return builder.Uri.ToString();
            }
            catch
            {
                string cleanUrl = baseUrl;
                cleanUrl = Regex.Replace(cleanUrl, @"/trang-\d+/?", "", RegexOptions.IgnoreCase);
                cleanUrl = cleanUrl.TrimEnd('/');
                return $"{cleanUrl}/trang-{page}";
            }
        }

        private async void BtnTruyenqqFetchInfo_Click(object sender, RoutedEventArgs e)
        {
            string rawUrl = txtTruyenqqTagUrl.Text.Trim();
            if (string.IsNullOrEmpty(rawUrl))
            {
                MessageBox.Show("Vui lòng nhập URL hợp lệ.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            btnTruyenqqFetchInfo.IsEnabled = false;
            lblStatus.Text = "Đang phân tích trang truyenqq...";
            progressBar.IsIndeterminate = true;


            try
            {
                string normalizedUrl = ResolveTruyenqqRequestUrl(rawUrl);
                txtTruyenqqTagUrl.Text = normalizedUrl;
                TruyenqqLog($"Đang phân tích URL: {normalizedUrl}");
                bool captchaOk = await SolveTruyenqqCaptchaIfNeededAsync(normalizedUrl);
                if (!captchaOk)
                {
                    TruyenqqLog("Không thể bypass Cloudflare. Hủy phân tích.");
                    lblStatus.Text = "Analysis failed (Cloudflare).";
                    return;
                }

                normalizedUrl = ResolveTruyenqqRequestUrl(normalizedUrl);
                txtTruyenqqTagUrl.Text = normalizedUrl;

                // Fetch targeted page HTML
                string html = await FetchStringAsync(normalizedUrl, _downloadCts?.Token ?? CancellationToken.None);
                
                int maxPage = 1;
                // Parse page patterns: /trang-420 or trang-420
                var pageMatches = Regex.Matches(html, @"trang-(\d+)", RegexOptions.IgnoreCase);
                foreach (Match m in pageMatches)
                {
                    if (int.TryParse(m.Groups[1].Value, out int pageNum))
                    {
                        if (pageNum > maxPage) maxPage = pageNum;
                    }
                }

                txtTruyenqqTotalPages.Text = maxPage.ToString();
                txtTruyenqqPageTo.Text = maxPage.ToString();
                
                TruyenqqLog($"Phân tích hoàn tất. Phát hiện tối đa {maxPage} trang.");
                lblStatus.Text = $"Analysis complete. Found {maxPage} pages.";
            }
            catch (Exception ex)
            {
                TruyenqqLog($"Lỗi khi phân tích: {ex.Message}");
                txtTruyenqqTotalPages.Text = "1";
                lblStatus.Text = "Analysis failed.";
            }
            finally
            {
                btnTruyenqqFetchInfo.IsEnabled = true;
                progressBar.IsIndeterminate = false;
            }
        }

        private void TxtTruyenqqTotalPages_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (txtTruyenqqPageTo != null && txtTruyenqqTotalPages != null)
            {
                txtTruyenqqPageTo.Text = txtTruyenqqTotalPages.Text;
            }
        }

        private async void BtnTruyenqqScrape_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                btnTruyenqqScrape.Content = "CANCELLING...";
                btnTruyenqqScrape.IsEnabled = false;
                if (btnTruyenqqCrawlMore != null) btnTruyenqqCrawlMore.IsEnabled = false;
                return;
            }
            await ScrapeTruyenqqAsync(clearExisting: true);
        }

        private async void BtnTruyenqqCrawlMore_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                if (btnTruyenqqCrawlMore != null)
                {
                    btnTruyenqqCrawlMore.Content = "CANCELLING...";
                    btnTruyenqqCrawlMore.IsEnabled = false;
                }
                btnTruyenqqScrape.IsEnabled = false;
                return;
            }
            await ScrapeTruyenqqAsync(clearExisting: false);
        }

        private async Task ScrapeTruyenqqAsync(bool clearExisting)
        {
            string rawBaseUrl = txtTruyenqqTagUrl.Text.Trim();
            if (string.IsNullOrEmpty(rawBaseUrl))
            {
                MessageBox.Show("Vui lòng nhập URL hợp lệ.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!int.TryParse(txtTruyenqqPageFrom.Text, out int pageFrom) || pageFrom < 1)
            {
                MessageBox.Show("Trang bắt đầu không hợp lệ.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!int.TryParse(txtTruyenqqPageTo.Text, out int pageTo) || pageTo < pageFrom)
            {
                MessageBox.Show("Trang kết thúc không hợp lệ.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            btnTruyenqqScrape.Content = "STOP CRAWLER";
            if (btnTruyenqqCrawlMore != null)
            {
                btnTruyenqqCrawlMore.Content = "STOP CRAWLER";
            }
            btnTruyenqqFetchInfo.IsEnabled = false;
            lblStatus.Text = "Đang cào truyenqq...";
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

            TruyenqqLog($"Bắt đầu cào từ trang {pageFrom} đến {pageTo}...");

            try
            {
                string baseUrl = NormalizeTruyenqqUrl(rawBaseUrl);
                txtTruyenqqTagUrl.Text = baseUrl;
                int totalPages = pageTo - pageFrom + 1;
                int pagesProcessed = 0;

                for (int page = pageFrom; page <= pageTo; page++)
                {
                    token.ThrowIfCancellationRequested();

                    string pageUrl = GetTruyenqqPageUrl(baseUrl, page);
                    TruyenqqLog($"Đang tải trang {page}: {pageUrl}");

                    bool captchaOk = await SolveTruyenqqCaptchaIfNeededAsync(pageUrl);
                    if (!captchaOk)
                    {
                        TruyenqqLog($"Không thể bypass Cloudflare cho trang {page}. Bỏ qua trang này.");
                        continue;
                    }

                    pageUrl = ResolveTruyenqqRequestUrl(pageUrl);
                    string html = await FetchStringAsync(pageUrl, _downloadCts?.Token ?? CancellationToken.None);
                    
                    // Match all <a> tags containing /truyen-tranh/ links, parent or child (chapter) links
                    var viewMatches = Regex.Matches(html, @"<a\s+[^>]*?href=[""'](?<link>[^""']*?/truyen-tranh/[^""']+)[""'][^>]*>(?<content>[\s\S]*?)<\/a>", RegexOptions.IgnoreCase);
                    
                    int pageCount = 0;
                    
                    // Parse parent links first and find their latest chap from adjacent child links
                    var pageParents = new System.Collections.Generic.List<GalleryItem>();
                    var parentLatestChaps = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    // First pass: extract all parents and trace adjacent chapter mappings
                    string lastParentUrl = null;
                    foreach (Match match in viewMatches)
                    {
                        string relativeLink = match.Groups["link"].Value.Trim();
                        string fullLink = relativeLink;
                        if (!fullLink.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                            !fullLink.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            string activeDomain = ExtractTruyenqqBaseUrl(pageUrl);
                            fullLink = activeDomain + (fullLink.StartsWith("/") ? "" : "/") + fullLink;
                        }
                        fullLink = fullLink.TrimEnd('/');

                        if (Regex.IsMatch(relativeLink, @"-chap(?:-|\b)", RegexOptions.IgnoreCase))
                        {
                            // It's a chapter link. If we have a preceding parent, associate the chapter text.
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
                            // It's a parent link
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

                    // Apply the latest chap numbers and add to list
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
                    TruyenqqLog($"Trang {page} hoàn tất. Tìm thấy {pageCount} liên kết mới.");
                }

                // Sort items deterministically
                var sortedItems = _scrapedItems
                    .OrderBy(item => item.Name)
                    .ThenBy(item => item.OriginalIndex)
                    .ToList();
                _scrapedItems.Clear();
                foreach (var item in sortedItems) _scrapedItems.Add(item);

                RecalculateDuplicates();
                TruyenqqLog($"Cào dữ liệu hoàn tất! Tổng cộng thu thập được {_scrapedItems.Count} liên kết độc nhất.");
                lblStatus.Text = "Crawling completed successfully.";
                lblLinkCount.Text = _scrapedItems.Count.ToString();
            }
            catch (OperationCanceledException)
            {
                TruyenqqLog("Đã hủy cào theo yêu cầu người dùng.");
                lblStatus.Text = "Crawling cancelled.";
            }
            catch (Exception ex)
            {
                TruyenqqLog($"Lỗi nghiêm trọng khi cào: {ex.Message}");
                lblStatus.Text = "Crawling failed.";
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                btnTruyenqqScrape.Content = "GET LINK";
                btnTruyenqqScrape.IsEnabled = true;
                if (btnTruyenqqCrawlMore != null)
                {
                    btnTruyenqqCrawlMore.Content = "GET MORE";
                    btnTruyenqqCrawlMore.IsEnabled = true;
                }
                btnTruyenqqFetchInfo.IsEnabled = true;
            }
        }

        private void BtnTruyenqqPasteDirect_Click(object sender, RoutedEventArgs e)
        {
            var win = new DirectDownloadWindow(isNhentai: false);
            win.Owner = this;
            win.OnImport = async (links) =>
            {
                if (links != null && links.Any())
                {
                    await ImportTruyenqqDirectLinksAsync(links);
                }
            };
            win.Show();
        }

        private async Task ImportTruyenqqDirectLinksAsync(System.Collections.Generic.List<string> links, bool showMessageBox = true)
        {
            btnTruyenqqScrape.IsEnabled = false;
            btnTruyenqqFetchInfo.IsEnabled = false;
            
            progressBar.Value = 0;
            progressBar.IsIndeterminate = false;

            int total = links.Count;
            int imported = 0;
            int failed = 0;

            TruyenqqLog($"[Import] Bắt đầu phân tích và nhập {total} liên kết trực tiếp...");
            lblStatus.Text = $"Importing 0/{total} links...";

            try
            {
                for (int i = 0; i < total; i++)
                {
                    string link = links[i].Trim();
                    
                    if (!link.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                        !link.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        link = NormalizeTruyenqqUrl(link);
                    }

                    lblStatus.Text = $"[{i + 1}/{total}] Đang phân tích: {link}";

                    try
                    {
                        link = ResolveTruyenqqRequestUrl(link);
                        lblStatus.Text = $"[{i + 1}/{total}] Đang phân tích: {link}";
                        if (_scrapedItems.Any(item => item.Link.Equals(link, StringComparison.OrdinalIgnoreCase)))
                        {
                            TruyenqqLog($"[Import] Bỏ qua liên kết đã tồn tại: {link}");
                            imported++;
                            continue;
                        }

                        // Try to scrape title from target page
                        bool captchaOk = await SolveTruyenqqCaptchaIfNeededAsync(link);
                        if (!captchaOk)
                        {
                            TruyenqqLog($"[Import] Không thể bypass Cloudflare cho: {link}");
                            failed++;
                            continue;
                        }
                        link = ResolveTruyenqqRequestUrl(link);
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

                        Dispatcher.Invoke(() =>
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
                        });

                        TruyenqqLog($"[Import {i + 1}/{total}] Thành công: {title}");
                        imported++;
                    }
                    catch (Exception ex)
                    {
                        TruyenqqLog($"[Import] Lỗi xử lý link '{link}': {ex.Message}");
                        failed++;

                        string fallbackTitle = "Fallback - Truyenqq - " + link.Split('/').Last();
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
                TruyenqqLog($"[Import] Nhập hoàn tất! Thành công: {imported}, Lỗi/Fallback: {failed}.");
                lblStatus.Text = $"Import completed. Success: {imported}, Failed: {failed}.";
                
                if (showMessageBox)
                {
                    MessageBox.Show($"Đã nhập thành công {total} đường dẫn!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            finally
            {
                btnTruyenqqScrape.IsEnabled = true;
                btnTruyenqqFetchInfo.IsEnabled = true;
                if (btnStartDownload != null) btnStartDownload.IsEnabled = true;
                progressBar.Value = 100;
            }
        }

        private async Task<byte[]> GetTruyenqqByteArrayWithRefererAsync(string url, string referer, CancellationToken token)
        {
            int delayMs = 600;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                    {
                        request.Headers.Referrer = new Uri(referer);
                        using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token))
                        {
                            response.EnsureSuccessStatusCode();
                            return await response.Content.ReadAsByteArrayAsync();
                        }
                    }
                }
                catch (HttpRequestException ex) when (attempt < 3)
                {
                    Log($"[truyenqq] Thử tải lại ảnh do lỗi mạng: {ex.Message}. Chờ {delayMs}ms.");
                    await Task.Delay(delayMs, token);
                    delayMs = Math.Min(delayMs * 2, 6000);
                }
                catch (TaskCanceledException) when (!token.IsCancellationRequested && attempt < 3)
                {
                    Log($"[truyenqq] Thử tải lại ảnh do timeout. Chờ {delayMs}ms.");
                    await Task.Delay(delayMs, token);
                    delayMs = Math.Min(delayMs * 2, 6000);
                }
            }

            throw new Exception($"Không thể tải ảnh truyenqq: {url}");
        }

        private string GetSafeChapterHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return html;

            string[] commentIndicators = new[]
            {
                "class=\"comment-box\"",
                "class=\"comment-wrapper\"",
                "class=\"comment-holder\"",
                "id=\"comment\"",
                "id=\"truyenqq-comment\"",
                "class=\"fb-comments\"",
                "class=\"list-comment\"",
                "class=\"list_comment\"",
                "id=\"disqus_thread\"",
                "class=\"comments\"",
                "class=\"comment\"",
                "id=\"comments\"",
                "<!-- Bình luận -->",
                "<!-- Bình luận"
            };

            int minIndex = html.Length;
            foreach (var indicator in commentIndicators)
            {
                int index = html.IndexOf(indicator, StringComparison.OrdinalIgnoreCase);
                if (index != -1 && index < minIndex)
                {
                    minIndex = index;
                }
            }

            if (minIndex < html.Length)
            {
                return html.Substring(0, minIndex);
            }
            return html;
        }

        private async Task DownloadTruyenqqGalleryAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem = null, ChapterFilter chapterFilter = null)
        {
            string cleanLink = ResolveTruyenqqRequestUrl(item.Link);
            string activeDomain = ExtractTruyenqqBaseUrl(cleanLink);

            // Determine if parent link or child link
            // Parent links contain /truyen-tranh/{slug}
            // Child link contains /truyen-tranh/{slug}-{chapterSuffix}
            var uri = new Uri(cleanLink);
            var segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length >= 2 && segments[0].Equals("truyen-tranh", StringComparison.OrdinalIgnoreCase))
            {
                if (chapterFilter == null)
                {
                    var pendingFromProcess = LoadPendingChapterLinksFromProcess(rootFolder, "truyenqq", item);
                    if (pendingFromProcess != null)
                    {
                        if (pendingFromProcess.Count == 0)
                        {
                            Log($"[truyenqq] Process cho '{item.Name}' đã hoàn tất, bỏ qua Download All.");
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

                        Log($"[truyenqq] Resume từ process: còn {pendingFromProcess.Count} chapter cần tải cho '{item.Name}'.");
                        await DownloadTruyenqqPendingChaptersAsync(item, rootFolder, token, queueItem, pendingFromProcess);
                        return;
                    }
                }

                string slug = segments[1];
                bool captchaOk = await SolveTruyenqqCaptchaIfNeededAsync(cleanLink);
                if (!captchaOk)
                {
                    throw new Exception("Không thể vượt qua Cloudflare captcha.");
                }
                cleanLink = ResolveTruyenqqRequestUrl(cleanLink);
                string html = await FetchStringAsync(cleanLink, _downloadCts?.Token ?? CancellationToken.None);

                // Update manga title from <title> tag if available
                var titleMatch = Regex.Match(html, @"<title>\s*(.*?)\s*</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (titleMatch.Success)
                {
                    string rawTitle = WebUtility.HtmlDecode(titleMatch.Groups[1].Value).Trim();
                    string[] commonSuffixes = { " - TruyenQQ", " - TruyenQQ", " | TruyenQQ", " | TruyenQQ" };
                    foreach (var suffix in commonSuffixes)
                    {
                        if (rawTitle.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        {
                            rawTitle = rawTitle.Substring(0, rawTitle.Length - suffix.Length).Trim();
                        }
                    }
                    rawTitle = StripTruyenqqBrandSuffix(rawTitle);
                    item.Name = FormatGalleryTitle(rawTitle);
                }

                // Parent path (absolute path of the manga info page)
                string parentPath = uri.AbsolutePath.TrimEnd('/');
                string escapedPath = Regex.Escape(parentPath);

                // Scrape child links: href=".../truyen-tranh/slug-{suffix}"
                // IMPORTANT: prefer strict chap suffix, then widen only if site shifts markup.
                string pattern = @"href=[""'](?<link>[^""']*?" + escapedPath + @"-chap(?:[^""'\s?#]*)?)[""']";
                var matches = Regex.Matches(html, pattern, RegexOptions.IgnoreCase);
                if (matches.Count == 0)
                {
                    string fallbackPattern = @"href=[""'](?<link>[^""']*?" + escapedPath + @"-(?:chap|chapter|chuong)(?:[^""'\s?#]*)?)[""']";
                    matches = Regex.Matches(html, fallbackPattern, RegexOptions.IgnoreCase);
                }

                var chapterLinks = new System.Collections.Generic.List<string>();
                var seenChapters = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (Match m in matches)
                {
                    string link = m.Groups["link"].Value.Trim();
                    if (!link.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                        !link.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        link = activeDomain + (link.StartsWith("/") ? "" : "/") + link;
                    }
                    
                    link = link.TrimEnd('/');

                    if (seenChapters.Add(link))
                    {
                        chapterLinks.Add(link);
                    }
                }

                if (chapterLinks.Count == 0)
                {
                    // Check if it's actually a direct chapter page (was matched as details page but has no chapters inside)
                    // Some chapters might have similar pattern, but if no sub-chapters are found, let's treat it as a direct chapter page download
                    Log($"[truyenqq] Không tìm thấy chương nào trong '{item.Name}'. Thử tải trực tiếp trang này như một chapter...");
                    await DownloadTruyenqqChapterAsync(item, rootFolder, token, queueItem, isParentQueue: false);
                    return;
                }

                // Sort chapters in ascending order so that oldest chapters (like Chap 1) are downloaded first.
                chapterLinks = chapterLinks.OrderBy(ParseChapterNumber).ToList();

                var totalFoundChapters = chapterLinks.Count;
                if (chapterFilter != null)
                {
                    var filtered = new System.Collections.Generic.List<string>();
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
                        Log($"[truyenqq] Không có chương nào trùng khớp với bộ lọc đã chọn trong tổng số {totalFoundChapters} chương của '{item.Name}'.");
                        if (queueItem != null)
                        {
                            Dispatcher.Invoke(() => {
                                queueItem.Status = "Completed";
                                queueItem.CurrentProcess = "Không có chương trùng khớp bộ lọc";
                            });
                        }
                        return;
                    }
                }
                else
                {
                    chapterLinks = FilterPendingChapterLinksFromProcess(rootFolder, "truyenqq", item, chapterLinks);
                    if (chapterLinks.Count == 0)
                    {
                        TruyenqqLog($"Tất cả chapter của '{item.Name}' đã Done theo process.");
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
                }

                TruyenqqLog($"Phát hiện {chapterLinks.Count} chương cho truyện '{item.Name}'. Bắt đầu tải lần lượt...");

                await DownloadTruyenqqPendingChaptersAsync(item, rootFolder, token, queueItem, chapterLinks);

                Dispatcher.Invoke(() =>
                {
                    item.LinkCount = chapterLinks.Count.ToString();
                });
            }
            else
            {
                // Direct Chapter page
                bool chapterCompleted = await DownloadTruyenqqChapterAsync(item, rootFolder, token, queueItem, isParentQueue: false);
                if (!chapterCompleted)
                {
                    throw new Exception($"Truyenqq chapter '{item.Name}' tải không hoàn tất.");
                }
            }
        }

        private async Task DownloadTruyenqqPendingChaptersAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem, IList<string> chapterLinks)
        {
            if (queueItem != null)
            {
                Dispatcher.Invoke(() =>
                {
                    queueItem.TotalChapters = chapterLinks.Count;
                    queueItem.CompletedChapters = 0;
                });
            }

            int completedCount = 0;
            for (int idx = 0; idx < chapterLinks.Count; idx++)
            {
                token.ThrowIfCancellationRequested();
                string chapLink = chapterLinks[idx];

                var chapItem = new GalleryItem { Link = chapLink, Name = item.Name };
                bool chapterCompleted = false;
                try
                {
                    chapterCompleted = await DownloadTruyenqqChapterAsync(chapItem, rootFolder, token, queueItem, isParentQueue: true);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    string chapterLabel = NormalizeChapterLabel("chap " + Math.Max(1d, ParseChapterNumber(chapLink)).ToString("0.##", CultureInfo.InvariantCulture));
                    TruyenqqLog($"[truyenqq] Bỏ qua {chapterLabel} của '{item.Name}': {ex.Message}");
                    if (queueItem != null)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            queueItem.AddError(chapterLabel, 0, ex.Message, chapLink, item.Link);
                        });
                        RecordCheckError("truyenqq", queueItem.Name ?? item.Name, chapterLabel, 0, ex.Message, chapLink);
                    }
                    continue;
                }

                if (chapterCompleted)
                {
                    MarkChapterProcessDone(rootFolder, "truyenqq", item, chapLink);
                    completedCount++;
                }

                if (queueItem != null && chapterCompleted)
                {
                    Dispatcher.Invoke(() =>
                    {
                        queueItem.CompletedChapters = completedCount;
                    });
                }
            }
        }

        private async Task<bool> DownloadTruyenqqChapterAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem = null, bool isParentQueue = false)
        {
            item.Link = ResolveTruyenqqRequestUrl(item.Link);

            bool captchaOk = await SolveTruyenqqCaptchaIfNeededAsync(item.Link);
            if (!captchaOk)
            {
                throw new Exception("Không thể vượt qua Cloudflare captcha.");
            }
            item.Link = ResolveTruyenqqRequestUrl(item.Link);
            string html = await FetchStringAsync(item.Link, _downloadCts?.Token ?? CancellationToken.None);

            string mangaTitle = item.Name;
            string chapterTitle = "Chương 1";

            // Try to extract clean titles from page
            var titleMatch = Regex.Match(html, @"<title>\s*(.*?)\s*</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (titleMatch.Success)
            {
                string rawTitle = WebUtility.HtmlDecode(titleMatch.Groups[1].Value).Trim();
                string[] commonSuffixes = { " - TruyenQQ", " - TruyenQQ", " | TruyenQQ", " | TruyenQQ" };
                foreach (var suffix in commonSuffixes)
                {
                    if (rawTitle.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        rawTitle = rawTitle.Substring(0, rawTitle.Length - suffix.Length).Trim();
                    }
                }

                // TruyenQQ formats title: "Tên Truyện - Tên Chương"
                // Some manga names have " - " in them so we must find the RIGHTMOST
                // chapter-like part (containing chap/chương/chapter keyword) as the separator.
                string[] parts = rawTitle.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    // Find the rightmost part that looks like a chapter label
                    int chapPartIdx = -1;
                    for (int i = parts.Length - 1; i >= 1; i--)
                    {
                        if (Regex.IsMatch(parts[i], @"\b(chap|chương|chapter)\b", RegexOptions.IgnoreCase))
                        {
                            chapPartIdx = i;
                            break;
                        }
                    }
                    if (chapPartIdx > 0)
                    {
                        // Join everything before the chapter part as manga title
                        mangaTitle = string.Join(" - ", parts, 0, chapPartIdx).Trim();
                        chapterTitle = string.Join(" - ", parts, chapPartIdx, parts.Length - chapPartIdx).Trim();
                    }
                    else
                    {
                        // Fallback: last part is chapter
                        mangaTitle = string.Join(" - ", parts, 0, parts.Length - 1).Trim();
                        chapterTitle = parts[parts.Length - 1].Trim();
                    }
                }
                else if (parts.Length == 1)
                {
                    chapterTitle = parts[0].Trim();
                }
            }

            // Clean Manga Title: remove "chương mới nhất \d+" suffix
            string cleanManga = mangaTitle;
            cleanManga = Regex.Replace(cleanManga, @"\s+(?:chương|Chương)\s+mới\s+nhất\s+\d+.*", "", RegexOptions.IgnoreCase);
            cleanManga = cleanManga.Trim();
            if (string.IsNullOrEmpty(cleanManga))
            {
                cleanManga = "Unknown Manga";
            }

            // Clean Chapter Title: extract the exact chapter string (e.g. chap 2)
            string cleanChapter = chapterTitle;
            var chapMatch = Regex.Match(chapterTitle, @"(chap|chương|chapter)\s*(?<num>\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (chapMatch.Success)
            {
                string type = chapMatch.Groups[1].Value.ToLower();
                if (type == "chapter") type = "chap";
                string num = chapMatch.Groups["num"].Value;
                cleanChapter = $"{type} {num}";
            }
            else
            {
                cleanChapter = Regex.Replace(cleanChapter, @"\s+Tiếng\s+Việt\s+TruyenQQ.*", "", RegexOptions.IgnoreCase);
                cleanChapter = cleanChapter.Trim();
            }

            cleanChapter = NormalizeChapterLabel(cleanChapter);
            string safeManga = GetSafePathName(cleanManga);
            string safeChapter = GetDownloadChapterFolderName(cleanManga, cleanChapter);
            string progressKey = $"truyenqq|{GetSafePathName(cleanManga)}";
            int totalChaptersForLog = queueItem != null ? Math.Max(1, queueItem.TotalChapters) : 1;
            int currentChapterForLog = queueItem != null ? Math.Max(1, Math.Min(queueItem.CompletedChapters + 1, totalChaptersForLog)) : 1;
            UpsertMainLogLine(progressKey, $"[truyenqq] Đang tải {cleanManga} - {cleanChapter} ({currentChapterForLog}/{totalChaptersForLog})");
            
            string siteRootFolder = GetSiteDownloadRoot(rootFolder, "truyenqq");
            string unmergedPath = Path.Combine(siteRootFolder, $"{safeManga}-{safeChapter}");
            string mergedPath = Path.Combine(siteRootFolder, safeManga, safeChapter);
            string finalTargetFolder = _isSingleComicFolderType ? mergedPath : unmergedPath;
            string tempFolder = BuildStableTempFolderPath(siteRootFolder, "truyenqq", safeManga, safeChapter, item.Link);
            Directory.CreateDirectory(tempFolder);
            RegisterTempFolder(tempFolder);
            // Isolate images using Safe Chapter HTML (no comments section)
            string safeHtml = GetSafeChapterHtml(html);

            // Isolate reading container contents to avoid ads/logo/header images
            int startIndex = -1;
            string[] containerMarkers = new[]
            {
                "class=\"story-see-content\"",
                "class=\"chapter_content\"",
                "class=\"chapter-content\"",
                "class=\"reading-detail\"",
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

            var imageUrls = ExtractTruyenqqImageUrls(contentArea, item.Link);
            if (imageUrls.Count == 0 && !string.Equals(contentArea, safeHtml, StringComparison.Ordinal))
            {
                // ponytail: fallback full safe HTML when scoped scan misses images; upgrade to DOM parse if markup churn keeps happening.
                imageUrls = ExtractTruyenqqImageUrls(safeHtml, item.Link);
            }
            if (imageUrls.Count == 0)
            {
                imageUrls = ExtractTruyenqqImageUrls(html, item.Link);
            }

            if (imageUrls.Count == 0)
            {
                throw new Exception($"Không thể tìm thấy hình ảnh nào của chương truyện '{chapterTitle}' để tải xuống.");
            }

            WriteTempProgressLog(tempFolder, item, "Downloading", 0, imageUrls.Count, "0/0 pages", $"Bắt đầu tải {cleanChapter}");

            // Connection count settings
            int maxThreads = GetCurrentConnectionLimit();

            TruyenqqLog($"Bắt đầu tải {imageUrls.Count} trang của chapter '{chapterTitle}' với {maxThreads} kết nối song song...");

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
                var tasks = new System.Collections.Generic.List<Task>();
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

                            if ((File.Exists(localFilePath) && new FileInfo(localFilePath).Length > 0) ||
                                (File.Exists(unmergedFilePath) && new FileInfo(unmergedFilePath).Length > 0) ||
                                (File.Exists(mergedFilePath) && new FileInfo(mergedFilePath).Length > 0))
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
                                await DownloadUrlToFileWithRefererAsync(imgUrl, item.Link, localFilePath, token, isTruyenqq: true);
                                downloadedPath = localFilePath;
                            }
                            catch (Exception ex)
                            {
                                lock (lockObj)
                                {
                                    if (queueItem != null)
                                    {
                                        int pageNumber = index + 1;
                                        string pageName = pageNumber.ToString(CultureInfo.InvariantCulture);
                                        if (TryGetTruyenqqImagePageNumber(imgUrl, out int actualPageNumber))
                                        {
                                            pageNumber = actualPageNumber;
                                            pageName = actualPageNumber.ToString(CultureInfo.InvariantCulture);
                                        }

                                        queueItem.AddError(cleanChapter, pageNumber, ex.Message, imgUrl, item.Link, pageName);
                                        RecordCheckError("truyenqq", queueItem.Name ?? cleanManga, cleanChapter, pageNumber, ex.Message, imgUrl, pageName);
                                    }
                                    Log($"[truyenqq] Lỗi tải trang {index + 1} của chapter '{cleanChapter}': {ex.Message}");
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

            try
            {
                if (Directory.Exists(tempFolder))
                {
                    WriteTempProgressLog(tempFolder, item, "Done", imageUrls.Count, imageUrls.Count, isParentQueue ? $"{cleanChapter} (trang {imageUrls.Count}/{imageUrls.Count})" : $"Trang {imageUrls.Count}/{imageUrls.Count}", "Download completed");
                }
                MoveTempFolderToTarget(tempFolder, finalTargetFolder, "truyenqq");
                UpsertMainLogLine(progressKey, $"[truyenqq] Đã tải xong {cleanManga} - {cleanChapter} ({currentChapterForLog}/{totalChaptersForLog})");
            }
            catch (Exception ex)
            {
                Log($"[truyenqq] [Lỗi] Không thể di chuyển thư mục tạm: {ex.Message}");
            }
            finally
            {
            }

            // Check for missing files
            return ValidateDownloadedFiles(finalTargetFolder, imageUrls.Count, queueItem, cleanChapter, chapterUrl: item.Link);
        }

        private List<string> ExtractTruyenqqImageUrls(string contentArea, string pageUrl)
        {
            var imageUrls = new List<string>();
            string html = contentArea ?? string.Empty;
            string extractionScope = html;

            var pageBlocks = Regex.Matches(
                html,
                @"<div[^>]+id=[""']page_\d+[""'][^>]*class=[""'][^""']*page-chapter[^""']*[""'][^>]*>.*?(?=<div[^>]+id=[""']page_\d+[""']|$)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (pageBlocks.Count > 0)
            {
                extractionScope = string.Join(Environment.NewLine, pageBlocks.Cast<Match>().Select(match => match.Value));
            }

            var imgTags = Regex.Matches(extractionScope, @"<(?:img|source)\s+[^>]*>", RegexOptions.IgnoreCase);

            foreach (Match imgTag in imgTags)
            {
                string tag = imgTag.Value;
                string imgUrl = null;

                var dataOriginalMatch = Regex.Match(tag, @"data-original=[""'](?<url>[^""']+)[""']", RegexOptions.IgnoreCase);
                if (dataOriginalMatch.Success)
                {
                    imgUrl = dataOriginalMatch.Groups["url"].Value;
                }
                else
                {
                    var dataCdnMatch = Regex.Match(tag, @"data-cdn=[""'](?<url>[^""']+)[""']", RegexOptions.IgnoreCase);
                    if (dataCdnMatch.Success)
                    {
                        imgUrl = dataCdnMatch.Groups["url"].Value;
                    }
                    else
                    {
                        var dataLazySrcMatch = Regex.Match(tag, @"data-lazy-src=[""'](?<url>[^""']+)[""']", RegexOptions.IgnoreCase);
                        if (dataLazySrcMatch.Success)
                        {
                            imgUrl = dataLazySrcMatch.Groups["url"].Value;
                        }
                        else
                        {
                            var dataImageMatch = Regex.Match(tag, @"data-image=[""'](?<url>[^""']+)[""']", RegexOptions.IgnoreCase);
                            if (dataImageMatch.Success)
                            {
                                imgUrl = dataImageMatch.Groups["url"].Value;
                            }
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(imgUrl))
                {
                    var dataSrcMatch = Regex.Match(tag, @"data-src=[""'](?<url>[^""']+)[""']", RegexOptions.IgnoreCase);
                    if (dataSrcMatch.Success)
                    {
                        imgUrl = dataSrcMatch.Groups["url"].Value;
                    }
                    else
                    {
                        var srcMatch = Regex.Match(tag, @"src=[""'](?<url>[^""']+)[""']", RegexOptions.IgnoreCase);
                        if (srcMatch.Success)
                        {
                            imgUrl = srcMatch.Groups["url"].Value;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(imgUrl))
                {
                    var srcSetMatch = Regex.Match(tag, @"srcset=[""'](?<url>[^,""']+)", RegexOptions.IgnoreCase);
                    if (srcSetMatch.Success)
                    {
                        imgUrl = srcSetMatch.Groups["url"].Value;
                    }
                }

                if (string.IsNullOrWhiteSpace(imgUrl))
                {
                    continue;
                }

                imgUrl = imgUrl.Trim();
                if (imgUrl.EndsWith("logo.png", StringComparison.OrdinalIgnoreCase) ||
                    imgUrl.EndsWith("avatar.jpg", StringComparison.OrdinalIgnoreCase) ||
                    imgUrl.Contains("avatar") ||
                    imgUrl.Contains("loading") ||
                    imgUrl.Contains("spacer.gif") ||
                    imgUrl.Contains("transparent.gif") ||
                    imgUrl.Contains("/images/logo") ||
                    imgUrl.Contains("/images/favicon") ||
                    imgUrl.Contains("facebook.com") ||
                    imgUrl.Contains("banner") ||
                    imgUrl.Contains("advertisement") ||
                    imgUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!imgUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !imgUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    string activeDomain = ExtractTruyenqqBaseUrl(pageUrl);
                    imgUrl = activeDomain + (imgUrl.StartsWith("/") ? string.Empty : "/") + imgUrl;
                }

                if (!imageUrls.Contains(imgUrl))
                {
                    imageUrls.Add(imgUrl);
                }
            }

            if (imageUrls.Count == 0)
            {
                foreach (Match match in Regex.Matches(
                    extractionScope,
                    @"https?://[^""'\s>]+?\.(?:jpg|jpeg|png|gif|webp|bmp)(?:\?[^""'\s>]*)?",
                    RegexOptions.IgnoreCase))
                {
                    string imgUrl = match.Value.Trim();
                    if (string.IsNullOrWhiteSpace(imgUrl) ||
                        imgUrl.Contains("avatar") ||
                        imgUrl.Contains("logo") ||
                        imgUrl.Contains("banner") ||
                        imgUrl.Contains("no_image"))
                    {
                        continue;
                    }

                    if (!imageUrls.Contains(imgUrl))
                    {
                        imageUrls.Add(imgUrl);
                    }
                }
            }

            return PreferSingleTruyenqqImageServer(imageUrls);
        }

        private List<string> PreferSingleTruyenqqImageServer(IList<string> imageUrls)
        {
            if (imageUrls == null || imageUrls.Count == 0)
            {
                return new List<string>();
            }

            string[] preferredHosts = new[]
            {
                "hinhhinh.com",
                "truyenvua.com"
            };

            foreach (string preferredHost in preferredHosts)
            {
                var selected = imageUrls
                    .Where(url => IsTruyenqqImageHost(url, preferredHost))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (selected.Count > 0)
                {
                    // ponytail: pick 1 image host only; if site adds more mirrors later, extend preferredHosts.
                    return selected;
                }
            }

            return imageUrls
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private bool IsTruyenqqImageHost(string url, string preferredHost)
        {
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(preferredHost))
            {
                return false;
            }

            try
            {
                return Uri.TryCreate(url, UriKind.Absolute, out Uri uri) &&
                    uri.Host.IndexOf(preferredHost, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private double ParseChapterNumber(string url)
        {
            if (string.IsNullOrEmpty(url)) return 0.0;
            var match = Regex.Match(url, @"(?:chap|chapter|chuong|trang)[^\d]*(?<num>\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (match.Success && double.TryParse(match.Groups["num"].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double num))
            {
                return num;
            }
            return 0.0;
        }

        private static bool TryGetTruyenqqImagePageNumber(string imageUrl, out int pageNumber)
        {
            pageNumber = 0;
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                return false;
            }

            string fileName = imageUrl;
            try
            {
                if (Uri.TryCreate(imageUrl, UriKind.Absolute, out Uri uri))
                {
                    fileName = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
                }
                else
                {
                    fileName = Path.GetFileNameWithoutExtension(imageUrl.Split('?')[0]);
                }
            }
            catch
            {
                fileName = Path.GetFileNameWithoutExtension(imageUrl.Split('?')[0]);
            }

            var match = Regex.Match(fileName ?? string.Empty, @"(?<num>\d+)(?!.*\d)");
            return match.Success && int.TryParse(match.Groups["num"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out pageNumber);
        }

        private static void ParseMangaNameAndLatestChap(string rawTitle, out string mangaName, out string latestChap)
        {
            mangaName = rawTitle;
            latestChap = "";
            mangaName = Regex.Replace(
                mangaName,
                @"\s*[\-\|]\s*truy[eệ]nqq[a-zA-Z0-9-]*.*$",
                "",
                RegexOptions.IgnoreCase).Trim();
            string[] commonSuffixes = { " - TruyenQQ", " - TruyenQQ", " | TruyenQQ", " | TruyenQQ" };
            foreach (var suffix in commonSuffixes)
            {
                if (mangaName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    mangaName = mangaName.Substring(0, mangaName.Length - suffix.Length).Trim();
            }
            var match = Regex.Match(mangaName, @"^(?<manga>.*?)\s+(?<indicator>chương mới nhất|chap mới nhất|chương|chap|chapter)\s+(?<val>[\d\w\.-]+(?:[\s\S]*?))$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                mangaName = match.Groups["manga"].Value.Trim();
                mangaName = Regex.Replace(mangaName, @"\s*(?:\||-)\s*tới\s*$", "", RegexOptions.IgnoreCase).Trim();
                mangaName = Regex.Replace(mangaName, @"\s+tới\s*$", "", RegexOptions.IgnoreCase).Trim();
                latestChap = match.Groups["indicator"].Value.Trim() + " " + match.Groups["val"].Value.Trim();
            }
        }

        private double ParseChapterNumberFromText(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0.0;
            var match = Regex.Match(text, @"(?<num>\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (match.Success && double.TryParse(match.Groups["num"].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double num))
            {
                return num;
            }
            return 0.0;
        }
    }
}
#pragma warning restore 4014


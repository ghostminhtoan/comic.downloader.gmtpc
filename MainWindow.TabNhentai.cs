using System;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private bool _isUpdatingNhentaiUrl = false;

        private string GetNhentaiPageUrl(string baseUrl, int page)
        {
            baseUrl = baseUrl.Trim();
            // Strip page parameter (support optional amp;)
            string cleanUrl = Regex.Replace(baseUrl, @"([?&])(?:amp;)?page=\d+(&|$)", "$1", RegexOptions.IgnoreCase);
            cleanUrl = cleanUrl.TrimEnd('&', '?');

            string separator = cleanUrl.Contains("?") ? "&" : "?";
            return $"{cleanUrl}{separator}page={page}";
        }

        private string UpdateNhentaiUrlSort(string url, string sortValue)
        {
            url = url.Trim();
            if (string.IsNullOrEmpty(url)) return url;

            // Strip page parameter to keep URL clean when switching sort options (support optional amp;)
            url = Regex.Replace(url, @"([?&])(?:amp;)?page=\d+(&|$)", "$1", RegexOptions.IgnoreCase);
            url = url.TrimEnd('&', '?');

            if (url.Contains("?"))
            {
                if (Regex.IsMatch(url, @"([?&])(?:amp;)?sort=[^&]*", RegexOptions.IgnoreCase))
                {
                    url = Regex.Replace(url, @"([?&])(?:amp;)?sort=[^&]*", $"$1sort={sortValue}", RegexOptions.IgnoreCase);
                }
                else
                {
                    url = $"{url}&sort={sortValue}";
                }
            }
            else
            {
                url = $"{url}?sort={sortValue}";
            }
            return url;
        }

        private void SelectNhentaiSortComboBoxByValue(string sortVal)
        {
            if (cmbNhentaiSort == null) return;
            for (int i = 0; i < cmbNhentaiSort.Items.Count; i++)
            {
                if (cmbNhentaiSort.Items[i] is ComboBoxItem item && string.Equals(item.Tag?.ToString(), sortVal, StringComparison.OrdinalIgnoreCase))
                {
                    cmbNhentaiSort.SelectedIndex = i;
                    break;
                }
            }
        }

        private void TxtNhentaiTagUrl_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingNhentaiUrl) return;

            string url = txtNhentaiTagUrl.Text.Trim();
            if (string.IsNullOrEmpty(url)) return;

            if (url.IndexOf("nhentai.net", StringComparison.OrdinalIgnoreCase) >= 0 ||
                url.IndexOf("nhentai.xxx", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (url.IndexOf("nhentai.net", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _isUpdatingNhentaiUrl = true;
                    try
                    {
                        txtNhentaiTagUrl.Text = url.Replace("nhentai.net", "nhentai.xxx");
                    }
                    finally
                    {
                        _isUpdatingNhentaiUrl = false;
                    }

                    url = txtNhentaiTagUrl.Text.Trim();
                }

                var match = Regex.Match(url, @"[?&](?:amp;)?sort=([^&]+)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string sortVal = match.Groups[1].Value.ToLower();
                    _isUpdatingNhentaiUrl = true;
                    try
                    {
                        SelectNhentaiSortComboBoxByValue(sortVal);
                    }
                    finally
                    {
                        _isUpdatingNhentaiUrl = false;
                    }
                }
                else
                {
                    // By default, select "Recent" (date) and append to URL
                    _isUpdatingNhentaiUrl = true;
                    try
                    {
                        SelectNhentaiSortComboBoxByValue("date");
                        string updatedUrl = UpdateNhentaiUrlSort(url, "date");
                        txtNhentaiTagUrl.Text = updatedUrl;
                    }
                    finally
                    {
                        _isUpdatingNhentaiUrl = false;
                    }
                }
            }
        }



        private async void BtnNhentaiFetchInfo_Click(object sender, RoutedEventArgs e)
        {
            string url = txtNhentaiTagUrl.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("Please enter a valid URL.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Ensure URL has a scheme
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            btnNhentaiFetchInfo.IsEnabled = false;
            lblStatus.Text = "Analyzing target page...";
            progressBar.IsIndeterminate = true;
            Log($"Analyzing URL: {url}");

            try
            {
                bool ok = await SolveNhentaiCaptchaIfNeededAsync(url);
                if (!ok)
                {
                    lblStatus.Text = "Blocked by Cloudflare.";
                    btnNhentaiFetchInfo.IsEnabled = true;
                    progressBar.IsIndeterminate = false;
                    return;
                }

                string html = await FetchStringAsync(url, _downloadCts?.Token ?? CancellationToken.None);
                
                // Parse all hrefs from the HTML
                var hrefMatches = Regex.Matches(html, @"href\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                
                int maxPage = 1;
                foreach (Match hrefMatch in hrefMatches)
                {
                    string link = hrefMatch.Groups[1].Value.Trim();
                    // Match query parameter page=xxx (support optional amp;)
                    var pageMatch = Regex.Match(link, @"[?&](?:amp;)?page=(\d+)", RegexOptions.IgnoreCase);
                    if (pageMatch.Success)
                    {
                        if (int.TryParse(pageMatch.Groups[1].Value, out int pageNum))
                        {
                            if (pageNum > maxPage) maxPage = pageNum;
                        }
                    }
                }

                _detectedMaxPage = maxPage;
                txtNhentaiTotalPages.Text = _detectedMaxPage.ToString();
                txtNhentaiPageTo.Text = _detectedMaxPage.ToString();
                Log($"Analysis completed. Detected maximum pages: {_detectedMaxPage}");
                lblStatus.Text = $"Analysis complete. Found {_detectedMaxPage} pages.";
            }
            catch (Exception ex)
            {
                Log($"Error during analysis: {ex.Message}");
                txtNhentaiTotalPages.Text = "1";
                lblStatus.Text = "Analysis failed.";
            }
            finally
            {
                btnNhentaiFetchInfo.IsEnabled = true;
                progressBar.IsIndeterminate = false;
            }
        }

        private void TxtNhentaiTotalPages_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (txtNhentaiPageTo != null && txtNhentaiTotalPages != null)
            {
                txtNhentaiPageTo.Text = txtNhentaiTotalPages.Text;
            }
        }

        private async void BtnNhentaiScrape_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                btnNhentaiScrape.Content = "CANCELLING...";
                btnNhentaiScrape.IsEnabled = false;
                if (btnNhentaiCrawlMore != null) btnNhentaiCrawlMore.IsEnabled = false;
                return;
            }
            await ScrapeNhentaiAsync(clearExisting: true);
        }

        private async void BtnNhentaiCrawlMore_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                if (btnNhentaiCrawlMore != null)
                {
                    btnNhentaiCrawlMore.Content = "CANCELLING...";
                    btnNhentaiCrawlMore.IsEnabled = false;
                }
                btnNhentaiScrape.IsEnabled = false;
                return;
            }
            await ScrapeNhentaiAsync(clearExisting: false);
        }

        private async Task ScrapeNhentaiAsync(bool clearExisting)
        {
            string baseUrl = txtNhentaiTagUrl.Text.Trim();
            if (string.IsNullOrEmpty(baseUrl))
            {
                MessageBox.Show("Please enter a valid URL.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Ensure URL has a scheme
            if (!baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                baseUrl = "https://" + baseUrl;
            }

            if (!int.TryParse(txtNhentaiPageFrom.Text, out int pageFrom) || pageFrom < 1)
            {
                MessageBox.Show("Invalid 'From Page' value. Must be greater than or equal to 1.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!int.TryParse(txtNhentaiPageTo.Text, out int pageTo) || pageTo < pageFrom)
            {
                MessageBox.Show("Invalid 'To Page' value. Must be greater than or equal to 'From Page'.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            btnNhentaiScrape.Content = "STOP CRAWLER";
            if (btnNhentaiCrawlMore != null)
            {
                btnNhentaiCrawlMore.Content = "STOP CRAWLER";
            }
            btnNhentaiFetchInfo.IsEnabled = false;
            lblStatus.Text = "Crawling nhentai.xxx in progress...";
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

            Log($"Starting nhentai crawler from page {pageFrom} to {pageTo}...");

            try
            {
                int totalPages = pageTo - pageFrom + 1;
                int pagesProcessed = 0;

                for (int page = pageFrom; page <= pageTo; page++)
                {
                    if (token.IsCancellationRequested)
                    {
                        token.ThrowIfCancellationRequested();
                    }

                    string pageUrl = GetNhentaiPageUrl(baseUrl, page);
                    Log($"Requesting page {page}: {pageUrl}");
                    
                    string html = null;
                    bool pageLoaded = false;
                    try
                    {
                        bool ok = await SolveNhentaiCaptchaIfNeededAsync(pageUrl);
                        if (!ok)
                        {
                            throw new Exception("Bị chặn bởi Cloudflare Captcha.");
                        }
                        html = await FetchStringAsync(pageUrl, _downloadCts?.Token ?? CancellationToken.None);
                        pageLoaded = true;
                    }
                    catch (Exception ex)
                    {
                        Log($"Warning: Page {page} could not be loaded ({ex.Message}). Trying fallback as single gallery ID...");
                    }

                    int pageCount = 0;

                    if (pageLoaded && html != null)
                    {
                        // Extract view links along with their titles
                        // E.g. <a href="/g/412345/" class="cover">...<div class="caption">Artist - Title</div></a>
                        var viewMatches = Regex.Matches(html, @"<a\s+href=""[^""]*?/g/(\d+)/?""[^>]*>.*?<div\s+class=""caption"">([^<]+)</div>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                        
                        foreach (Match match in viewMatches)
                        {
                            string viewId = match.Groups[1].Value;
                            string title = WebUtility.HtmlDecode(match.Groups[2].Value.Trim());
                            title = FormatGalleryTitle(title);
                            string fullLink = $"https://nhentai.xxx/g/{viewId}/";

                            if (!_scrapedItems.Any(item => item.Link == fullLink || item.Name.Equals(title, StringComparison.OrdinalIgnoreCase)))
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
                        Log($"Page {page} processed. Found {pageCount} unique view links on this page.");
                    }
                    else
                    {
                        // Fallback: Try to request the page number directly as a single gallery ID
                        string galleryUrl = $"https://nhentai.xxx/g/{page}/";
                        try
                        {
                            bool ok = await SolveNhentaiCaptchaIfNeededAsync(galleryUrl);
                            if (!ok)
                            {
                                throw new Exception("Bị chặn bởi Cloudflare Captcha.");
                            }
                            string galleryHtml = await FetchStringAsync(galleryUrl, _downloadCts?.Token ?? CancellationToken.None);
                            
                            // Extract title of the gallery: <h1 class="title">...</h1>
                            var titleMatch = Regex.Match(galleryHtml, @"<h1\s+class=""title"">\s*(.*?)\s*</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                            string title = $"Gallery {page}";
                            if (titleMatch.Success)
                            {
                                string titleRaw = titleMatch.Groups[1].Value;
                                title = Regex.Replace(titleRaw, @"<[^>]+>", ""); // Strip HTML tags like <span>
                                title = WebUtility.HtmlDecode(title).Trim();
                            }
                            else
                            {
                                // Try title tag fallback
                                var fallbackMatch = Regex.Match(galleryHtml, @"<title>\s*(.*?)\s*</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                                if (fallbackMatch.Success)
                                {
                                    string temp = WebUtility.HtmlDecode(fallbackMatch.Groups[1].Value).Trim();
                                    string suffix = " - nhentai";
                                    if (temp.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                                    {
                                        temp = temp.Substring(0, temp.Length - suffix.Length).Trim();
                                    }
                                    title = temp;
                                }
                            }
                            title = FormatGalleryTitle(title);

                            string fullLink = galleryUrl;
                            if (!_scrapedItems.Any(item => item.Link == fullLink || item.Name.Equals(title, StringComparison.OrdinalIgnoreCase)))
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
                            Log($"Gallery ID {page} processed as single item. Found gallery: {title}");
                        }
                        catch (Exception ex2)
                        {
                            Log($"Warning: Gallery ID {page} fallback failed ({ex2.Message}). Skipping.");
                        }
                    }

                    pagesProcessed++;
                    double progressPct = ((double)pagesProcessed / totalPages) * 100;
                    progressBar.Value = progressPct;
                    lblStatus.Text = $"Searching page {page}/{pageTo} ({progressPct:0}%)";
                    lblLinkCount.Text = _scrapedItems.Count.ToString(); // real-time update
                }

                RecalculateDuplicates();
                Log($"Crawling finished! Total unique links gathered: {_scrapedItems.Count}");
                lblStatus.Text = "Crawling completed successfully.";
                
                lblLinkCount.Text = _scrapedItems.Count.ToString();
            }
            catch (OperationCanceledException)
            {
                Log("Crawling process cancelled by user.");
                lblStatus.Text = "Crawling cancelled.";
            }
            catch (Exception ex)
            {
                Log($"Critical crawler error: {ex.Message}");
                lblStatus.Text = "Crawling failed due to error.";
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                btnNhentaiScrape.Content = "GET LINK";
                btnNhentaiScrape.IsEnabled = true;
                if (btnNhentaiCrawlMore != null)
                {
                    btnNhentaiCrawlMore.Content = "GET MORE";
                    btnNhentaiCrawlMore.IsEnabled = true;
                }
                btnNhentaiFetchInfo.IsEnabled = true;
            }
        }

        private void BtnNhentaiPasteDirect_Click(object sender, RoutedEventArgs e)
        {
            var win = new DirectDownloadWindow(isNhentai: true);
            win.Owner = this;
            win.OnImport = async (links) =>
            {
                if (links != null && links.Any())
                {
                    await ImportNhentaiDirectLinksAsync(links);
                }
            };
            win.Show();
        }

        private async Task ImportNhentaiDirectLinksAsync(System.Collections.Generic.List<string> links, bool showMessageBox = true)
        {
            btnNhentaiScrape.IsEnabled = false;
            btnNhentaiFetchInfo.IsEnabled = false;
            progressBar.Value = 0;
            progressBar.IsIndeterminate = false;

            int total = links.Count;
            int imported = 0;
            int failed = 0;

            Log($"[Import nhentai] Bắt đầu phân tích và nhập {total} liên kết trực tiếp...");
            lblStatus.Text = $"Importing 0/{total} links...";

            try
            {
                for (int i = 0; i < total; i++)
                {
                    string link = links[i];
                    lblStatus.Text = $"[{i + 1}/{total}] Đang tải tiêu đề: {link}";

                    try
                    {
                        // Check if already exists in results grid
                        if (_scrapedItems.Any(item => item.Link.Equals(link, StringComparison.OrdinalIgnoreCase)))
                        {
                            Log($"[Import nhentai] Bỏ qua liên kết đã tồn tại: {link}");
                            imported++;
                            continue;
                        }

                        // Check if it is a direct CDN link
                        var cdnMatch = Regex.Match(link, @"(?:https?:)?//(?<subdomain>[it]\d*)\.nhentai\.net/galleries/(?<mediaId>\d+)/(?<pageNum>\d+)(?<isThumb>t)?\.(?<ext>jpg|png|gif|webp|jpeg)", RegexOptions.IgnoreCase);
                        if (cdnMatch.Success)
                        {
                            string mediaId = cdnMatch.Groups["mediaId"].Value;
                            string cdnTitle = $"Direct CDN Gallery - {mediaId}";
                            Dispatcher.Invoke(() =>
                            {
                                _scrapedItems.Add(new GalleryItem
                                {
                                    Link = link,
                                    Name = cdnTitle,
                                    OriginalIndex = _scrapedItems.Count,
                                    IsChecked = true
                                });
                            });
                            Log($"[Import nhentai {i + 1}/{total}] Nhập trực tiếp link CDN: {cdnTitle} ({link})");
                            imported++;
                            continue;
                        }

                        // Try to scrape title from page
                        bool ok = await SolveNhentaiCaptchaIfNeededAsync(link);
                        if (!ok)
                        {
                            throw new Exception("Bị chặn bởi Cloudflare Captcha.");
                        }
                        string html = await FetchStringAsync(link, _downloadCts?.Token ?? CancellationToken.None);
                        var titleMatch = Regex.Match(html, @"<h1\s+class=""title"">\s*(.*?)\s*</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                        string title = "Gallery ID " + GetNhentaiGalleryIdFromLink(link);
                        
                        if (titleMatch.Success)
                        {
                            string titleRaw = titleMatch.Groups[1].Value;
                            title = Regex.Replace(titleRaw, @"<[^>]+>", ""); // Strip HTML tags
                            title = WebUtility.HtmlDecode(title).Trim();
                        }
                        else
                        {
                            // Try fallback using title tag
                            var fallbackMatch = Regex.Match(html, @"<title>\s*(.*?)\s*</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                            if (fallbackMatch.Success)
                            {
                                string temp = WebUtility.HtmlDecode(fallbackMatch.Groups[1].Value).Trim();
                                string suffix = " - nhentai";
                                if (temp.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                                {
                                    temp = temp.Substring(0, temp.Length - suffix.Length).Trim();
                                }
                                title = temp;
                            }
                        }

                        title = FormatGalleryTitle(title);

                        Dispatcher.Invoke(() =>
                        {
                            _scrapedItems.Add(new GalleryItem
                            {
                                Link = link,
                                Name = title,
                                OriginalIndex = _scrapedItems.Count,
                                IsChecked = true // Tick checkmark by default for direct links
                            });
                        });

                        Log($"[Import nhentai {i + 1}/{total}] Thành công: {title} ({link})");
                        imported++;
                    }
                    catch (Exception ex)
                    {
                        Log($"[Import nhentai] Lỗi khi xử lý link '{link}': {ex.Message}");
                        failed++;
                        
                        // Add fallback anyway so user doesn't lose the link
                        string fallbackTitle = "Fallback - Gallery ID " + GetNhentaiGalleryIdFromLink(link);
                        Dispatcher.Invoke(() =>
                        {
                            _scrapedItems.Add(new GalleryItem
                            {
                                Link = link,
                                Name = fallbackTitle,
                                OriginalIndex = _scrapedItems.Count,
                                IsChecked = true
                            });
                        });
                    }

                    double pct = ((double)(i + 1) / total) * 100;
                    progressBar.Value = pct;
                    lblLinkCount.Text = _scrapedItems.Count.ToString(); // real-time update
                }

                RecalculateDuplicates();
                lblLinkCount.Text = _scrapedItems.Count.ToString();

                Log($"[Import nhentai] Nhập hoàn tất! Thành công: {imported}, Lỗi/Fallback: {failed}. Tổng số liên kết hiện tại: {_scrapedItems.Count}");
                lblStatus.Text = $"Import completed. Success: {imported}, Failed: {failed}.";
                
                if (showMessageBox)
                {
                    MessageBox.Show($"Đã nhập thành công {total} đường dẫn vào bảng Extracted Gallery Links!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            finally
            {
                btnNhentaiScrape.IsEnabled = true;
                btnNhentaiFetchInfo.IsEnabled = true;
                if (btnStartDownload != null) btnStartDownload.IsEnabled = true;
                progressBar.Value = 100;
            }
        }

        private string GetNhentaiGalleryIdFromLink(string link)
        {
            var match = Regex.Match(link, @"/g/(\d+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            return "Unknown";
        }
    }
}

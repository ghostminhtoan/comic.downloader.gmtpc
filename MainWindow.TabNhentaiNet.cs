using System;
using System.Collections.Generic;
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
        private bool _isUpdatingNhentaiNetUrl = false;

        private void NhentaiNetLog(string message)
        {
            string logLine = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Log(logLine);
        }

        private string GetNhentaiNetPageUrl(string baseUrl, int page)
        {
            baseUrl = baseUrl.Trim();
            string cleanUrl = Regex.Replace(baseUrl, @"([?&])(?:amp;)?page=\d+(&|$)", "$1", RegexOptions.IgnoreCase);
            cleanUrl = cleanUrl.TrimEnd('&', '?');

            string separator = cleanUrl.Contains("?") ? "&" : "?";
            return $"{cleanUrl}{separator}page={page}";
        }

        private string UpdateNhentaiNetUrlSort(string url, string sortValue)
        {
            url = url.Trim();
            if (string.IsNullOrEmpty(url)) return url;

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

        private void SelectNhentaiNetSortComboBoxByValue(string sortVal)
        {
            if (cmbNhentaiNetSort == null) return;
            for (int i = 0; i < cmbNhentaiNetSort.Items.Count; i++)
            {
                if (cmbNhentaiNetSort.Items[i] is ComboBoxItem item && string.Equals(item.Tag?.ToString(), sortVal, StringComparison.OrdinalIgnoreCase))
                {
                    cmbNhentaiNetSort.SelectedIndex = i;
                    break;
                }
            }
        }

        private void TxtNhentaiNetTagUrl_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingNhentaiNetUrl) return;
            if (txtNhentaiNetTagUrl == null) return;

            string url = txtNhentaiNetTagUrl.Text.Trim();
            if (string.IsNullOrEmpty(url)) return;

            if (url.IndexOf("nhentai.net", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var match = Regex.Match(url, @"[?&](?:amp;)?sort=([^&]+)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string sortVal = match.Groups[1].Value.ToLower();
                    _isUpdatingNhentaiNetUrl = true;
                    try
                    {
                        SelectNhentaiNetSortComboBoxByValue(sortVal);
                    }
                    finally
                    {
                        _isUpdatingNhentaiNetUrl = false;
                    }
                }
                else
                {
                    _isUpdatingNhentaiNetUrl = true;
                    try
                    {
                        SelectNhentaiNetSortComboBoxByValue("date");
                        string updatedUrl = UpdateNhentaiNetUrlSort(url, "date");
                        txtNhentaiNetTagUrl.Text = updatedUrl;
                    }
                    finally
                    {
                        _isUpdatingNhentaiNetUrl = false;
                    }
                }
            }
        }

        private void CmbNhentaiNetSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingNhentaiNetUrl) return;
            if (txtNhentaiNetTagUrl == null) return;

            if (cmbNhentaiNetSort.SelectedItem is ComboBoxItem selectedItem)
            {
                string sortVal = selectedItem.Tag?.ToString();
                if (!string.IsNullOrEmpty(sortVal))
                {
                    _isUpdatingNhentaiNetUrl = true;
                    try
                    {
                        txtNhentaiNetTagUrl.Text = UpdateNhentaiNetUrlSort(txtNhentaiNetTagUrl.Text, sortVal);
                    }
                    finally
                    {
                        _isUpdatingNhentaiNetUrl = false;
                    }
                }
            }
        }

        private async void BtnNhentaiNetFetchInfo_Click(object sender, RoutedEventArgs e)
        {
            string url = txtNhentaiNetTagUrl.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("Please enter a valid URL.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            btnNhentaiNetFetchInfo.IsEnabled = false;
            lblStatus.Text = "Analyzing nhentai.net target page...";
            progressBar.IsIndeterminate = true;
            Log($"[nhentai.net] Analyzing URL: {url}");

            try
            {
                string html = null;
                try
                {
                    html = await FetchStringAsync(url, _downloadCts?.Token ?? CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Log($"[nhentai.net] HttpClient fetch failed ({ex.Message}). Trying to resolve captcha...");
                    bool ok = await SolveNhentaiCaptchaIfNeededAsync(url);
                    if (ok)
                    {
                        try
                        {
                            html = await FetchStringAsync(url, _downloadCts?.Token ?? CancellationToken.None);
                        }
                        catch (Exception ex2)
                        {
                            Log($"[nhentai.net] HttpClient retry fetch failed: {ex2.Message}. Fallback to WebView2 HTML.");
                            html = _lastNhentaiResolvedHtml;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(html) && !string.IsNullOrWhiteSpace(_lastNhentaiResolvedHtml))
                {
                    html = _lastNhentaiResolvedHtml;
                }

                if (string.IsNullOrWhiteSpace(html))
                {
                    throw new Exception("Không lấy được dữ liệu trang HTML (cả HttpClient và Captcha window đều trống).");
                }

                int maxPage = 1;

                // Try class="last" pagination link first (nhentai.net SvelteKit)
                var lastPageMatch = Regex.Match(html, @"class=""last[^""]*""[^>]*href=""[^""]*(?:page|page%3D|page=)(\d+)""", RegexOptions.IgnoreCase);
                if (!lastPageMatch.Success)
                {
                    lastPageMatch = Regex.Match(html, @"href=""[^""]*(?:page|page%3D|page=)(\d+)""[^>]*class=""[^""]*last", RegexOptions.IgnoreCase);
                }
                if (lastPageMatch.Success && int.TryParse(lastPageMatch.Groups[1].Value, out int lastPageNum))
                {
                    maxPage = lastPageNum;
                }
                else
                {
                    // Fallback: scan all page= links
                    var hrefMatches = Regex.Matches(html, @"href\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                    foreach (Match hrefMatch in hrefMatches)
                    {
                        string link = hrefMatch.Groups[1].Value.Trim();
                        var pageMatch = Regex.Match(link, @"[?&](?:amp;)?page=(\d+)", RegexOptions.IgnoreCase);
                        if (pageMatch.Success && int.TryParse(pageMatch.Groups[1].Value, out int pageNum))
                        {
                            if (pageNum > maxPage) maxPage = pageNum;
                        }
                    }
                }

                txtNhentaiNetTotalPages.Text = maxPage.ToString();
                txtNhentaiNetPageTo.Text = maxPage.ToString();
                Log($"[nhentai.net] Analysis completed. Detected maximum pages: {maxPage}");
                lblStatus.Text = $"Analysis complete. Found {maxPage} pages.";
            }
            catch (Exception ex)
            {
                Log($"[nhentai.net] Error during analysis: {ex.Message}");
                txtNhentaiNetTotalPages.Text = "1";
                lblStatus.Text = "Analysis failed.";
            }
            finally
            {
                btnNhentaiNetFetchInfo.IsEnabled = true;
                progressBar.IsIndeterminate = false;
            }
        }

        private void TxtNhentaiNetTotalPages_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (txtNhentaiNetPageTo != null && txtNhentaiNetTotalPages != null)
            {
                txtNhentaiNetPageTo.Text = txtNhentaiNetTotalPages.Text;
            }
        }

        private async void BtnNhentaiNetScrape_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                btnNhentaiNetScrape.Content = "CANCELLING...";
                btnNhentaiNetScrape.IsEnabled = false;
                if (btnNhentaiNetCrawlMore != null) btnNhentaiNetCrawlMore.IsEnabled = false;
                return;
            }
            SelectDownloadMangaTab();
            await ScrapeNhentaiNetAsync(clearExisting: true);
        }

        private async void BtnNhentaiNetCrawlMore_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                if (btnNhentaiNetCrawlMore != null)
                {
                    btnNhentaiNetCrawlMore.Content = "CANCELLING...";
                    btnNhentaiNetCrawlMore.IsEnabled = false;
                }
                btnNhentaiNetScrape.IsEnabled = false;
                return;
            }
            SelectDownloadMangaTab();
            await ScrapeNhentaiNetAsync(clearExisting: false);
        }

        private async Task ScrapeNhentaiNetAsync(bool clearExisting)
        {
            string baseUrl = txtNhentaiNetTagUrl.Text.Trim();
            if (string.IsNullOrEmpty(baseUrl))
            {
                MessageBox.Show("Please enter a valid URL.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                baseUrl = "https://" + baseUrl;
            }

            if (!int.TryParse(txtNhentaiNetPageFrom.Text, out int pageFrom) || pageFrom < 1)
            {
                MessageBox.Show("Invalid 'From Page' value. Must be greater than or equal to 1.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!int.TryParse(txtNhentaiNetPageTo.Text, out int pageTo) || pageTo < pageFrom)
            {
                MessageBox.Show("Invalid 'To Page' value. Must be greater than or equal to 'From Page'.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            btnNhentaiNetScrape.Content = "STOP CRAWLER";
            if (btnNhentaiNetCrawlMore != null)
            {
                btnNhentaiNetCrawlMore.Content = "STOP CRAWLER";
            }
            btnNhentaiNetFetchInfo.IsEnabled = false;
            lblStatus.Text = "Crawling nhentai.net in progress...";
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

            Log($"[nhentai.net] Starting crawler from page {pageFrom} to {pageTo}...");

            try
            {
                ShowTransientResultsImportingStatus("getting link...");
                int totalPages = pageTo - pageFrom + 1;
                int pagesProcessed = 0;

                for (int page = pageFrom; page <= pageTo; page++)
                {
                    if (token.IsCancellationRequested)
                    {
                        token.ThrowIfCancellationRequested();
                    }

                    string pageUrl = GetNhentaiNetPageUrl(baseUrl, page);
                    Log($"[nhentai.net] Requesting page {page}: {pageUrl}");

                    string html = null;
                    bool pageLoaded = false;
                    try
                    {
                        try
                        {
                            html = await FetchStringAsync(pageUrl, _downloadCts?.Token ?? CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            Log($"[nhentai.net] HttpClient fetch page {page} failed ({ex.Message}). Trying to resolve captcha...");
                            bool ok = await SolveNhentaiCaptchaIfNeededAsync(pageUrl);
                            if (ok)
                            {
                                try
                                {
                                    html = await FetchStringAsync(pageUrl, _downloadCts?.Token ?? CancellationToken.None);
                                }
                                catch (Exception ex2)
                                {
                                    Log($"[nhentai.net] HttpClient retry fetch page {page} failed: {ex2.Message}. Fallback to WebView2 HTML.");
                                    html = _lastNhentaiResolvedHtml;
                                }
                            }
                        }

                        if (string.IsNullOrWhiteSpace(html) && !string.IsNullOrWhiteSpace(_lastNhentaiResolvedHtml))
                        {
                            html = _lastNhentaiResolvedHtml;
                        }

                        if (!string.IsNullOrWhiteSpace(html))
                        {
                            pageLoaded = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[nhentai.net] Warning: Page {page} could not be loaded ({ex.Message}). Trying fallback as single gallery ID...");
                    }

                    int pageCount = 0;

                    if (pageLoaded && html != null)
                    {
                        // Extract gallery cards: <a href="/g/{id}/" class="cover"...><img src="{thumbUrl}"...><div class="caption">{title}</div></a>
                        var viewMatches = Regex.Matches(html,
                            @"<a\s+href=""[^""]*?/g/(\d+)/?""[^>]*class=""cover""[^>]*>.*?<img[^>]+(?:src|data-src)=""([^""]+)""[^>]*/?>.*?<div\s+class=""caption"">([^<]+)</div>",
                            RegexOptions.IgnoreCase | RegexOptions.Singleline);

                        if (viewMatches.Count == 0)
                        {
                            // Fallback: try nhentai.xxx-style pattern (href before class)
                            viewMatches = Regex.Matches(html,
                                @"<a\s+href=""[^""]*?/g/(\d+)/?""[^>]*>.*?<img[^>]+(?:src|data-src)=""([^""]+)""[^>]*/?>.*?<div\s+class=""caption"">([^<]+)</div>",
                                RegexOptions.IgnoreCase | RegexOptions.Singleline);
                        }

                        if (viewMatches.Count == 0)
                        {
                            Log($"[nhentai.net] HttpClient found 0 items on page {page}. Forcing WebView2 render...");
                            bool ok = await SolveNhentaiCaptchaIfNeededAsync(pageUrl, force: true);
                            if (ok && !string.IsNullOrWhiteSpace(_lastNhentaiResolvedHtml))
                            {
                                html = _lastNhentaiResolvedHtml;
                                viewMatches = Regex.Matches(html,
                                    @"<a\s+href=""[^""]*?/g/(\d+)/?""[^>]*class=""cover""[^>]*>.*?<img[^>]+(?:src|data-src)=""([^""]+)""[^>]*/?>.*?<div\s+class=""caption"">([^<]+)</div>",
                                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                                if (viewMatches.Count == 0)
                                {
                                    viewMatches = Regex.Matches(html,
                                        @"<a\s+href=""[^""]*?/g/(\d+)/?""[^>]*>.*?<img[^>]+(?:src|data-src)=""([^""]+)""[^>]*/?>.*?<div\s+class=""caption"">([^<]+)</div>",
                                        RegexOptions.IgnoreCase | RegexOptions.Singleline);
                                }
                            }
                        }

                        foreach (Match match in viewMatches)
                        {
                            string viewId = match.Groups[1].Value;
                            string thumbUrl = WebUtility.HtmlDecode(match.Groups[2].Value.Trim());
                            string title = WebUtility.HtmlDecode(match.Groups[3].Value.Trim());
                            title = FormatGalleryTitle(title);
                            string fullLink = $"https://nhentai.net/g/{viewId}/";

                            // Ensure thumb URL is absolute
                            if (thumbUrl.StartsWith("//"))
                            {
                                thumbUrl = "https:" + thumbUrl;
                            }

                            if (!_scrapedItems.Any(item => item.Link == fullLink || item.Name.Equals(title, StringComparison.OrdinalIgnoreCase)))
                            {
                                _scrapedItems.Add(new GalleryItem
                                {
                                    Link = fullLink,
                                    Name = title,
                                    OriginalIndex = _scrapedItems.Count,
                                    IsChecked = false,
                                    HoverPreviewThumbnailUrl = thumbUrl,
                                    SourceDomain = "nhentai.net"
                                });
                                pageCount++;
                            }
                        }
                        Log($"[nhentai.net] Page {page} processed. Found {pageCount} unique gallery links on this page.");
                    }
                    else
                    {
                        // Fallback: try as single gallery ID
                        string galleryUrl = $"https://nhentai.net/g/{page}/";
                        try
                        {
                            bool ok = await SolveNhentaiCaptchaIfNeededAsync(galleryUrl);
                            if (!ok)
                            {
                                throw new Exception("Bị chặn bởi Cloudflare Captcha.");
                            }
                            string galleryHtml = await FetchStringAsync(galleryUrl, _downloadCts?.Token ?? CancellationToken.None);

                            string title = ExtractNhentaiNetGalleryTitle(galleryHtml, page.ToString());
                            string thumbUrl = ExtractNhentaiNetGalleryCover(galleryHtml);

                            string fullLink = galleryUrl;
                            if (!_scrapedItems.Any(item => item.Link == fullLink || item.Name.Equals(title, StringComparison.OrdinalIgnoreCase)))
                            {
                                _scrapedItems.Add(new GalleryItem
                                {
                                    Link = fullLink,
                                    Name = title,
                                    OriginalIndex = _scrapedItems.Count,
                                    IsChecked = false,
                                    HoverPreviewThumbnailUrl = thumbUrl,
                                    SourceDomain = "nhentai.net"
                                });
                                pageCount++;
                            }
                            Log($"[nhentai.net] Gallery ID {page} processed as single item: {title}");
                        }
                        catch (Exception ex2)
                        {
                            Log($"[nhentai.net] Warning: Gallery ID {page} fallback failed ({ex2.Message}). Skipping.");
                        }
                    }

                    pagesProcessed++;
                    double progressPct = ((double)pagesProcessed / totalPages) * 100;
                    progressBar.Value = progressPct;
                    lblStatus.Text = $"Searching page {page}/{pageTo} ({progressPct:0}%)";
                    UpdateResultsCrawlProgress(pagesProcessed, totalPages, GuessImportDisplayName(baseUrl));
                    lblLinkCount.Text = _scrapedItems.Count.ToString();
                }

                RecalculateDuplicates();
                Log($"[nhentai.net] Crawling finished! Total unique links gathered: {_scrapedItems.Count}");
                lblStatus.Text = "Crawling completed successfully.";

                lblLinkCount.Text = _scrapedItems.Count.ToString();
            }
            catch (OperationCanceledException)
            {
                Log("[nhentai.net] Crawling process cancelled by user.");
                lblStatus.Text = "Crawling cancelled.";
            }
            catch (Exception ex)
            {
                Log($"[nhentai.net] Critical crawler error: {ex.Message}");
                lblStatus.Text = "Crawling failed due to error.";
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                btnNhentaiNetScrape.Content = "GET LINK";
                btnNhentaiNetScrape.IsEnabled = true;
                if (btnNhentaiNetCrawlMore != null)
                {
                    btnNhentaiNetCrawlMore.Content = "GET MORE";
                    btnNhentaiNetCrawlMore.IsEnabled = true;
                }
                btnNhentaiNetFetchInfo.IsEnabled = true;
                HideTransientResultsImportingStatus();
            }
        }

        private void BtnNhentaiNetPasteDirect_Click(object sender, RoutedEventArgs e)
        {
            var win = new DirectDownloadWindow(isNhentai: true);
            win.Owner = this;
            win.OnImport = async (links) =>
            {
                if (links != null && links.Any())
                {
                    await ImportNhentaiNetDirectLinksAsync(links);
                }
            };
            win.Show();
        }

        private async Task ImportNhentaiNetDirectLinksAsync(List<string> links, bool showMessageBox = true)
        {
            bool keepControlsEnabled = IsResultsImportingActive();
            if (!keepControlsEnabled)
            {
                btnNhentaiNetScrape.IsEnabled = false;
                btnNhentaiNetFetchInfo.IsEnabled = false;
            }
            progressBar.Value = 0;
            progressBar.IsIndeterminate = false;

            int total = links.Count;
            int imported = 0;
            int failed = 0;

            Log($"[Import nhentai.net] Bắt đầu phân tích và nhập {total} liên kết trực tiếp...");
            lblStatus.Text = $"Importing 0/{total} links...";

            try
            {
                for (int i = 0; i < total; i++)
                {
                    string link = links[i];
                    lblStatus.Text = $"[{i + 1}/{total}] Đang tải tiêu đề: {link}";

                    try
                    {
                        if (_scrapedItems.Any(item => item.Link.Equals(link, StringComparison.OrdinalIgnoreCase)))
                        {
                            Log($"[Import nhentai.net] Bỏ qua liên kết đã tồn tại: {link}");
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
                                    IsChecked = true,
                                    SourceDomain = "nhentai.net"
                                });
                            });
                            Log($"[Import nhentai.net {i + 1}/{total}] Nhập trực tiếp link CDN: {cdnTitle} ({link})");
                            imported++;
                            continue;
                        }

                        string html = null;
                        try
                        {
                            html = await FetchStringAsync(link, _downloadCts?.Token ?? CancellationToken.None);
                        }
                        catch (Exception)
                        {
                            // Chỉ giải captcha bằng WebView2 nếu HttpClient thực sự bị lỗi (chặn bởi Cloudflare)
                            bool ok = await SolveNhentaiCaptchaIfNeededAsync(link);
                            if (!ok)
                            {
                                throw new Exception("Bị chặn bởi Cloudflare Captcha.");
                            }
                            html = await FetchStringAsync(link, _downloadCts?.Token ?? CancellationToken.None);
                        }

                        if (string.IsNullOrWhiteSpace(html) && !string.IsNullOrWhiteSpace(_lastNhentaiResolvedHtml))
                        {
                            html = _lastNhentaiResolvedHtml;
                        }

                        if (string.IsNullOrWhiteSpace(html))
                        {
                            throw new Exception("Nội dung HTML tải về bị trống.");
                        }

                        string galleryId = GetNhentaiGalleryIdFromLink(link);
                        string title = ExtractNhentaiNetGalleryTitle(html, galleryId);
                        string thumbUrl = ExtractNhentaiNetGalleryCover(html);

                        Dispatcher.Invoke(() =>
                        {
                            _scrapedItems.Add(new GalleryItem
                            {
                                Link = link,
                                Name = title,
                                OriginalIndex = _scrapedItems.Count,
                                IsChecked = true,
                                HoverPreviewThumbnailUrl = thumbUrl,
                                SourceDomain = "nhentai.net"
                            });
                        });

                        Log($"[Import nhentai.net {i + 1}/{total}] Thành công: {title} ({link})");
                        imported++;
                    }
                    catch (Exception ex)
                    {
                        Log($"[Import nhentai.net] Lỗi khi xử lý link '{link}': {ex.Message}");
                        failed++;

                        string fallbackTitle = "Fallback - Gallery ID " + GetNhentaiGalleryIdFromLink(link);
                        Dispatcher.Invoke(() =>
                        {
                            _scrapedItems.Add(new GalleryItem
                            {
                                Link = link,
                                Name = fallbackTitle,
                                OriginalIndex = _scrapedItems.Count,
                                IsChecked = true,
                                SourceDomain = "nhentai.net"
                            });
                        });
                    }

                    double pct = ((double)(i + 1) / total) * 100;
                    if (!keepControlsEnabled)
                    {
                        progressBar.Value = pct;
                    }
                    lblLinkCount.Text = _scrapedItems.Count.ToString();
                }

                RecalculateDuplicates();
                lblLinkCount.Text = _scrapedItems.Count.ToString();

                Log($"[Import nhentai.net] Nhập hoàn tất! Thành công: {imported}, Lỗi/Fallback: {failed}. Tổng số liên kết hiện tại: {_scrapedItems.Count}");
                lblStatus.Text = $"Import completed. Success: {imported}, Failed: {failed}.";

                ShowImportSummaryIfNeeded(showMessageBox, total, imported, failed);
            }
            finally
            {
                if (!keepControlsEnabled)
                {
                    btnNhentaiNetScrape.IsEnabled = true;
                    btnNhentaiNetFetchInfo.IsEnabled = true;
                }
                if (btnStartDownload != null) btnStartDownload.IsEnabled = true;
                if (!keepControlsEnabled)
                {
                    progressBar.Value = 100;
                }
            }
        }

        private string ExtractNhentaiNetGalleryTitle(string html, string fallbackId)
        {
            // Try <h1 class="title">...</h1>
            var titleMatch = Regex.Match(html, @"<h1\s+class=""title"">\s*(.*?)\s*</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (titleMatch.Success)
            {
                string titleRaw = titleMatch.Groups[1].Value;
                string title = Regex.Replace(titleRaw, @"<[^>]+>", "");
                title = WebUtility.HtmlDecode(title).Trim();
                if (!string.IsNullOrWhiteSpace(title))
                {
                    return FormatGalleryTitle(title);
                }
            }

            // Try <title>...</title>
            var fallbackMatch = Regex.Match(html, @"<title>\s*(.*?)\s*</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (fallbackMatch.Success)
            {
                string temp = WebUtility.HtmlDecode(fallbackMatch.Groups[1].Value).Trim();
                string suffix = " nhentai";
                int idx = temp.LastIndexOf(suffix, StringComparison.OrdinalIgnoreCase);
                if (idx > 0)
                {
                    temp = temp.Substring(0, idx).TrimEnd(' ', '|', '-', '–', '—').Trim();
                }
                if (!string.IsNullOrWhiteSpace(temp))
                {
                    return FormatGalleryTitle(temp);
                }
            }

            return FormatGalleryTitle($"Gallery {fallbackId}");
        }

        private string ExtractNhentaiNetGalleryCover(string html)
        {
            // Try cover image: <img ... src="https://t{N}.nhentai.net/galleries/{mediaId}/cover.webp"
            var coverMatch = Regex.Match(html, @"(?:src|data-src)=""(https?://t\d*\.nhentai\.net/galleries/\d+/(?:cover|thumb)\.\w+)""", RegexOptions.IgnoreCase);
            if (coverMatch.Success)
            {
                return WebUtility.HtmlDecode(coverMatch.Groups[1].Value);
            }

            // Fallback: first gallery thumbnail
            var thumbMatch = Regex.Match(html, @"(?:src|data-src)=""(https?://t\d*\.nhentai\.net/galleries/\d+/\d+t?\.\w+)""", RegexOptions.IgnoreCase);
            if (thumbMatch.Success)
            {
                return WebUtility.HtmlDecode(thumbMatch.Groups[1].Value);
            }

            return string.Empty;
        }
    }
}

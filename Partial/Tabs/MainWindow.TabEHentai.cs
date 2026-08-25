using System;
using System.Collections.Generic;
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
        private const string EHentaiSiteFolder = "e-hentai.org";

        private void EHentaiLog(string message)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                string logLine = $"[{DateTime.Now:HH:mm:ss}] {message}\r\n";
                bool isError = IsErrorMessage(message);
                AppendLogLine(txtEHentaiLog, logLine, isError);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private bool IsEHentaiUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            string lower = url.ToLowerInvariant();
            return lower.Contains("e-hentai.org") || lower.Contains("exhentai.org");
        }

        private bool IsEHentaiGalleryUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            return Regex.IsMatch(url, @"https?://(?:e-hentai|exhentai)\.org/g/\d+/[a-zA-Z0-9]+", RegexOptions.IgnoreCase);
        }

        private string NormalizeEHentaiUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;
            url = url.Trim();
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }
            return url;
        }

        private string GetEHentaiPageUrl(string baseUrl, int page)
        {
            baseUrl = NormalizeEHentaiUrl(baseUrl);

            if (IsEHentaiGalleryUrl(baseUrl))
            {
                // Gallery pagination uses ?p=0, ?p=1, ...
                int pIndex = Math.Max(0, page - 1);
                string cleanUrl = Regex.Replace(baseUrl, @"([?&])p=\d+(&|$)", "$1", RegexOptions.IgnoreCase).TrimEnd('&', '?');
                string sep = cleanUrl.Contains("?") ? "&" : "?";
                return $"{cleanUrl}{sep}p={pIndex}";
            }
            else
            {
                // List / Tag / Search pagination uses ?page=0, ?page=1, ...
                int pageIndex = Math.Max(0, page - 1);
                string cleanUrl = Regex.Replace(baseUrl, @"([?&])(?:page|p)=\d+(&|$)", "$1", RegexOptions.IgnoreCase).TrimEnd('&', '?');
                if (pageIndex == 0)
                {
                    return cleanUrl;
                }
                string sep = cleanUrl.Contains("?") ? "&" : "?";
                return $"{cleanUrl}{sep}page={pageIndex}";
            }
        }

        private async void BtnEHentaiFetchInfo_Click(object sender, RoutedEventArgs e)
        {
            string url = txtEHentaiTagUrl.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("Vui lòng nhập URL hợp lệ (Please enter a valid URL).", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            url = NormalizeEHentaiUrl(url);
            txtEHentaiTagUrl.Text = url;

            btnEHentaiFetchInfo.IsEnabled = false;
            lblStatus.Text = "Đang phân tích trang e-hentai.org...";
            progressBar.IsIndeterminate = true;
            EHentaiLog($"Đang phân tích URL: {url}");

            try
            {
                string html = await FetchStringAsync(url, _downloadCts?.Token ?? CancellationToken.None);
                int maxPage = 1;

                if (IsEHentaiGalleryUrl(url))
                {
                    // For single gallery: count gallery index pages or read total image pages
                    // Total pages in gallery footer table.ptt td or table.ptb td
                    var pttMatches = Regex.Matches(html, @"[?&]p=(\d+)", RegexOptions.IgnoreCase);
                    foreach (Match m in pttMatches)
                    {
                        if (int.TryParse(m.Groups[1].Value, out int pVal))
                        {
                            if (pVal + 1 > maxPage) maxPage = pVal + 1;
                        }
                    }

                    var countMatch = Regex.Match(html, @"Showing\s+[\d,]+\s*-\s*[\d,]+\s+of\s+([\d,]+)\s+images", RegexOptions.IgnoreCase);
                    if (!countMatch.Success)
                    {
                        countMatch = Regex.Match(html, @"<td[^>]*class=""gdt2""[^>]*>(\d+)\s+pages</td>", RegexOptions.IgnoreCase);
                    }
                    if (countMatch.Success && int.TryParse(countMatch.Groups[1].Value.Replace(",", ""), out int totalImages))
                    {
                        EHentaiLog($"Gallery có tổng cộng {totalImages} ảnh.");
                    }
                }
                else
                {
                    // 1. Check "Found about X,XXX results" / "Showing X - Y of Z results"
                    var foundResultsMatch = Regex.Match(html, @"Found\s+(?:about\s+)?([\d,]+)\s+results", RegexOptions.IgnoreCase);
                    if (!foundResultsMatch.Success)
                    {
                        foundResultsMatch = Regex.Match(html, @"Showing\s+[\d,]+\s*-\s*[\d,]+\s+of\s+([\d,]+)\s+results", RegexOptions.IgnoreCase);
                    }

                    if (foundResultsMatch.Success && int.TryParse(foundResultsMatch.Groups[1].Value.Replace(",", ""), out int totalResults))
                    {
                        // E-Hentai displays 25 galleries per page in list/compact mode
                        int calculatedPages = (int)Math.Ceiling(totalResults / 25.0);
                        if (calculatedPages > maxPage)
                        {
                            maxPage = calculatedPages;
                            EHentaiLog($"Tìm thấy khoảng {totalResults:N0} truyện (~{maxPage:N0} trang).");
                        }
                    }

                    // 2. Check legacy table.ptt if present
                    var pageMatches = Regex.Matches(html, @"[?&]page=(\d+)", RegexOptions.IgnoreCase);
                    foreach (Match m in pageMatches)
                    {
                        if (int.TryParse(m.Groups[1].Value, out int pageNum))
                        {
                            if (pageNum + 1 > maxPage) maxPage = pageNum + 1;
                        }
                    }

                    var pttMatches = Regex.Matches(html, @"<table[^>]*class=""ptt""[^>]*>.*?</table>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    if (pttMatches.Count > 0)
                    {
                        var innerMatches = Regex.Matches(pttMatches[0].Value, @">(\d+)</a>", RegexOptions.IgnoreCase);
                        foreach (Match m in innerMatches)
                        {
                            if (int.TryParse(m.Groups[1].Value, out int pVal))
                            {
                                if (pVal > maxPage) maxPage = pVal;
                            }
                        }
                    }
                }

                txtEHentaiTotalPages.Text = maxPage.ToString();
                txtEHentaiPageTo.Text = maxPage.ToString();

                EHentaiLog($"Phân tích hoàn tất. Phát hiện tối đa {maxPage} trang.");
                lblStatus.Text = $"Analysis complete. Found {maxPage} pages.";
            }
            catch (Exception ex)
            {
                EHentaiLog($"Lỗi khi phân tích: {ex.Message}");
                txtEHentaiTotalPages.Text = "1";
                lblStatus.Text = "Analysis failed.";
            }
            finally
            {
                btnEHentaiFetchInfo.IsEnabled = true;
                progressBar.IsIndeterminate = false;
            }
        }

        private void TxtEHentaiTotalPages_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (txtEHentaiPageTo != null && txtEHentaiTotalPages != null)
            {
                txtEHentaiPageTo.Text = txtEHentaiTotalPages.Text;
            }
        }

        private async void BtnEHentaiScrape_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                btnEHentaiScrape.Content = "CANCELLING...";
                btnEHentaiScrape.IsEnabled = false;
                if (btnEHentaiCrawlMore != null) btnEHentaiCrawlMore.IsEnabled = false;
                return;
            }
            if (!ConfirmScrapeDuringDownloadIfNeeded(true)) return;
            SelectDownloadMangaTab();
            await ScrapeEHentaiAsync(clearExisting: true);
        }

        private async void BtnEHentaiCrawlMore_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                if (btnEHentaiCrawlMore != null)
                {
                    btnEHentaiCrawlMore.Content = "CANCELLING...";
                    btnEHentaiCrawlMore.IsEnabled = false;
                }
                btnEHentaiScrape.IsEnabled = false;
                return;
            }
            SelectDownloadMangaTab();
            await ScrapeEHentaiAsync(clearExisting: false);
        }

        private string ExtractEHentaiNextUrl(string html, string currentUrl)
        {
            if (string.IsNullOrWhiteSpace(html)) return null;

            // 1. Variable in javascript: var nexturl="https://e-hentai.org/...";
            var varMatch = Regex.Match(html, @"var\s+nexturl\s*=\s*['""](?<url>https?://[^'""]+?)['""]", RegexOptions.IgnoreCase);
            if (varMatch.Success && !string.IsNullOrWhiteSpace(varMatch.Groups["url"].Value))
            {
                return WebUtility.HtmlDecode(varMatch.Groups["url"].Value);
            }

            // 2. Element in HTML: <a id="dnext" href="..."> or <a id="unext" href="...">
            var aMatch = Regex.Match(html, @"<a[^>]+id=['""][ud]next['""][^>]+href=['""](?<url>[^'""]+?)['""]", RegexOptions.IgnoreCase);
            if (aMatch.Success)
            {
                string rawUrl = WebUtility.HtmlDecode(aMatch.Groups["url"].Value);
                if (!rawUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !rawUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    var baseUri = new Uri(currentUrl);
                    return new Uri(baseUri, rawUrl).AbsoluteUri;
                }
                return rawUrl;
            }

            return null;
        }

        private async Task ScrapeEHentaiAsync(bool clearExisting)
        {
            string baseUrl = txtEHentaiTagUrl.Text.Trim();
            if (string.IsNullOrEmpty(baseUrl))
            {
                MessageBox.Show("Vui lòng nhập URL hợp lệ (Please enter a valid URL).", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            baseUrl = NormalizeEHentaiUrl(baseUrl);
            txtEHentaiTagUrl.Text = baseUrl;

            if (!int.TryParse(txtEHentaiPageFrom.Text, out int pageFrom) || pageFrom < 1)
            {
                MessageBox.Show("Trang bắt đầu không hợp lệ (Invalid 'From Page').", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!int.TryParse(txtEHentaiPageTo.Text, out int pageTo) || pageTo < pageFrom)
            {
                MessageBox.Show("Trang kết thúc không hợp lệ (Invalid 'To Page').", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            btnEHentaiScrape.Content = "STOP CRAWLER";
            if (btnEHentaiCrawlMore != null)
            {
                btnEHentaiCrawlMore.Content = "STOP CRAWLER";
            }
            btnEHentaiFetchInfo.IsEnabled = false;
            lblStatus.Text = "Đang cào e-hentai.org...";
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

            EHentaiLog($"Bắt đầu cào từ trang {pageFrom} đến {pageTo}...");

            try
            {
                ShowTransientResultsImportingStatus("getting link...");

                // If user entered a single gallery URL directly
                if (IsEHentaiGalleryUrl(baseUrl))
                {
                    string html = await FetchStringAsync(baseUrl, token);
                    string title = ExtractEHentaiTitle(html, baseUrl);
                    string coverUrl = ExtractEHentaiCoverUrl(html, baseUrl);

                    Dispatcher.Invoke(() =>
                    {
                        var item = new GalleryItem
                        {
                            Link = baseUrl,
                            Name = title,
                            OriginalIndex = _scrapedItems.Count,
                            IsChecked = true,
                            HasNoChapters = false,
                            SourceDomain = EHentaiSiteFolder
                        };
                        if (!string.IsNullOrWhiteSpace(coverUrl))
                        {
                            item.HoverPreviewThumbnailUrl = coverUrl;
                        }
                        ExtractAndApplyEHentaiPreviewTags(item, html);
                        _scrapedItems.Add(item);
                    });

                    EHentaiLog($"Đã thêm 1 gallery: {title}");
                    lblStatus.Text = "Crawling completed successfully.";
                    lblLinkCount.Text = _scrapedItems.Count.ToString();
                    return;
                }

                int totalPages = pageTo - pageFrom + 1;
                int pagesProcessed = 0;

                // Keyset sequential pagination for e-hentai tag/search listings
                string currentUrl = baseUrl;

                // If starting from a page > 1, seek to that page first
                for (int p = 1; p < pageFrom; p++)
                {
                    token.ThrowIfCancellationRequested();
                    EHentaiLog($"Đang tua đến trang {pageFrom} (bước qua trang {p})...");
                    string seekHtml = await FetchStringAsync(currentUrl, token);
                    string nextUrl = ExtractEHentaiNextUrl(seekHtml, currentUrl);
                    if (string.IsNullOrWhiteSpace(nextUrl))
                    {
                        EHentaiLog($"Không tìm thấy trang tiếp theo sau trang {p}.");
                        break;
                    }
                    currentUrl = nextUrl;
                }

                for (int page = pageFrom; page <= pageTo; page++)
                {
                    token.ThrowIfCancellationRequested();

                    EHentaiLog($"Đang tải trang {page}: {currentUrl}");

                    string html = await FetchStringAsync(currentUrl, token);
                    if (string.IsNullOrWhiteSpace(html))
                    {
                        break;
                    }

                    // Extract gallery cards from listing: <a href="https://e-hentai.org/g/XXXX/YYYY/">
                    var matches = Regex.Matches(html, @"<a[^>]+href=['""](?<link>https?://(?:e-hentai|exhentai)\.org/g/\d+/[a-zA-Z0-9]+/?)(?:[^'""]*)['""][^>]*>(?<content>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    var seenInPage = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (Match m in matches)
                    {
                        string galleryLink = m.Groups["link"].Value.Trim();
                        if (!galleryLink.EndsWith("/")) galleryLink += "/";

                        if (seenInPage.Contains(galleryLink)) continue;
                        seenInPage.Add(galleryLink);

                        if (_scrapedItems.Any(x => x.Link.Equals(galleryLink, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        // Try to find title in snippet or nearby
                        string snippet = m.Groups["content"].Value;
                        string title = string.Empty;

                        var gndMatch = Regex.Match(snippet, @"<div[^>]*class=""glink""[^>]*>(.*?)</div>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                        if (gndMatch.Success)
                        {
                            title = WebUtility.HtmlDecode(Regex.Replace(gndMatch.Groups[1].Value, @"<[^>]+>", "")).Trim();
                        }
                        else
                        {
                            var imgAltMatch = Regex.Match(snippet, @"alt=['""]([^'""]+)['""]", RegexOptions.IgnoreCase);
                            if (imgAltMatch.Success)
                            {
                                title = WebUtility.HtmlDecode(imgAltMatch.Groups[1].Value).Trim();
                            }
                        }

                        if (string.IsNullOrWhiteSpace(title))
                        {
                            title = "E-Hentai Gallery " + GetEHentaiGalleryIdFromLink(galleryLink);
                        }

                        title = FormatGalleryTitle(title);

                        // Try to extract thumb url from snippet
                        string coverUrl = string.Empty;
                        var imgMatch = Regex.Match(snippet, @"<img[^>]+(?:src|data-src)=['""](?<url>[^'""]+?)['""]", RegexOptions.IgnoreCase);
                        if (imgMatch.Success)
                        {
                            coverUrl = imgMatch.Groups["url"].Value;
                        }

                        Dispatcher.Invoke(() =>
                        {
                            var galleryItem = new GalleryItem
                            {
                                Link = galleryLink,
                                Name = title,
                                OriginalIndex = _scrapedItems.Count,
                                IsChecked = true,
                                HasNoChapters = false,
                                SourceDomain = EHentaiSiteFolder
                            };
                            if (!string.IsNullOrWhiteSpace(coverUrl))
                            {
                                galleryItem.HoverPreviewThumbnailUrl = coverUrl;
                            }
                            _scrapedItems.Add(galleryItem);
                        });
                    }

                    pagesProcessed++;
                    double progressPct = (double)pagesProcessed / totalPages * 100;
                    progressBar.Value = progressPct;
                    lblStatus.Text = $"Searching page {page}/{pageTo} ({progressPct:0}%)";
                    UpdateResultsCrawlProgress(pagesProcessed, totalPages, GuessImportDisplayName(baseUrl));
                    lblLinkCount.Text = _scrapedItems.Count.ToString();

                    if (page < pageTo)
                    {
                        string nextUrl = ExtractEHentaiNextUrl(html, currentUrl);
                        if (string.IsNullOrWhiteSpace(nextUrl))
                        {
                            EHentaiLog($"Đã đến trang cuối cùng của danh mục tại trang {page}.");
                            break;
                        }
                        currentUrl = nextUrl;
                    }
                }

                RecalculateDuplicates();
                EHentaiLog($"Cào hoàn tất! Thu thập được {_scrapedItems.Count} truyện.");
                lblStatus.Text = "Crawling completed successfully.";
                lblLinkCount.Text = _scrapedItems.Count.ToString();
            }
            catch (OperationCanceledException)
            {
                EHentaiLog("Tiến trình cào bị hủy bởi người dùng.");
                lblStatus.Text = "Crawling cancelled.";
            }
            catch (Exception ex)
            {
                EHentaiLog($"Lỗi cào dữ liệu: {ex.Message}");
                lblStatus.Text = "Crawling failed due to error.";
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                btnEHentaiScrape.Content = "GET LINK";
                btnEHentaiScrape.IsEnabled = true;
                if (btnEHentaiCrawlMore != null)
                {
                    btnEHentaiCrawlMore.Content = "GET MORE";
                    btnEHentaiCrawlMore.IsEnabled = true;
                }
                btnEHentaiFetchInfo.IsEnabled = true;
                HideTransientResultsImportingStatus();
            }
        }

        private void BtnEHentaiPasteDirect_Click(object sender, RoutedEventArgs e)
        {
            var win = new DirectDownloadWindow();
            win.Owner = this;
            win.OnImport = async (links) =>
            {
                if (links != null && links.Any())
                {
                    await ImportEHentaiDirectLinksAsync(links);
                }
            };
            win.Show();
        }

        private async Task ImportEHentaiDirectLinksAsync(List<string> links, bool showMessageBox = true)
        {
            bool keepControlsEnabled = IsResultsImportingActive();
            if (!keepControlsEnabled)
            {
                btnEHentaiScrape.IsEnabled = false;
                btnEHentaiFetchInfo.IsEnabled = false;
                progressBar.Value = 0;
                progressBar.IsIndeterminate = false;
            }

            int total = links.Count;
            int imported = 0;
            int failed = 0;

            EHentaiLog($"[Import] Bắt đầu phân tích và nhập {total} liên kết e-hentai trực tiếp...");
            if (!keepControlsEnabled)
            {
                lblStatus.Text = $"Importing 0/{total} links...";
            }

            try
            {
                for (int i = 0; i < total; i++)
                {
                    string link = links[i];
                    if (!string.IsNullOrEmpty(link))
                    {
                        link = NormalizeEHentaiUrl(link);
                    }
                    if (!keepControlsEnabled)
                    {
                        lblStatus.Text = $"[{i + 1}/{total}] Đang phân tích: {link}";
                    }

                    try
                    {
                        if (_scrapedItems.Any(item => item.Link.Equals(link, StringComparison.OrdinalIgnoreCase)))
                        {
                            EHentaiLog($"[Import] Bỏ qua liên kết đã tồn tại: {link}");
                            imported++;
                            continue;
                        }

                        string html = await FetchStringAsync(link, _downloadCts?.Token ?? CancellationToken.None);
                        string title = ExtractEHentaiTitle(html, link);
                        string coverUrl = ExtractEHentaiCoverUrl(html, link);

                        Dispatcher.Invoke(() =>
                        {
                            var item = new GalleryItem
                            {
                                Link = link,
                                Name = title,
                                OriginalIndex = _scrapedItems.Count,
                                IsChecked = true,
                                HasNoChapters = false,
                                SourceDomain = EHentaiSiteFolder
                            };
                            if (!string.IsNullOrWhiteSpace(coverUrl))
                            {
                                item.HoverPreviewThumbnailUrl = coverUrl;
                            }
                            ExtractAndApplyEHentaiPreviewTags(item, html);
                            _scrapedItems.Add(item);
                        });

                        EHentaiLog($"[Import {i + 1}/{total}] Thành công: {title}");
                        imported++;
                    }
                    catch (Exception ex)
                    {
                        EHentaiLog($"[Import] Lỗi xử lý link '{link}': {ex.Message}");
                        failed++;

                        string fallbackTitle = "Fallback - E-Hentai - " + GetEHentaiGalleryIdFromLink(link);
                        Dispatcher.Invoke(() =>
                        {
                            _scrapedItems.Add(new GalleryItem
                            {
                                Link = link,
                                Name = fallbackTitle,
                                OriginalIndex = _scrapedItems.Count,
                                IsChecked = true,
                                HasNoChapters = false,
                                SourceDomain = EHentaiSiteFolder
                            });
                        });
                    }

                    if (!keepControlsEnabled)
                    {
                        double pct = ((double)(i + 1) / total) * 100;
                        progressBar.Value = pct;
                    }
                    lblLinkCount.Text = _scrapedItems.Count.ToString();
                }

                RecalculateDuplicates();
                lblLinkCount.Text = _scrapedItems.Count.ToString();
                EHentaiLog($"[Import] Nhập hoàn tất! Thành công: {imported}, Lỗi/Fallback: {failed}.");
                if (!keepControlsEnabled)
                {
                    lblStatus.Text = $"Import completed. Success: {imported}, Failed: {failed}.";
                }

                ShowImportSummaryIfNeeded(showMessageBox, total, imported, failed);
            }
            finally
            {
                if (!keepControlsEnabled)
                {
                    btnEHentaiScrape.IsEnabled = true;
                    btnEHentaiFetchInfo.IsEnabled = true;
                    if (btnStartDownload != null) btnStartDownload.IsEnabled = true;
                    progressBar.Value = 100;
                }
            }
        }

        private string GetEHentaiGalleryIdFromLink(string link)
        {
            var match = Regex.Match(link, @"/g/(\d+)/([a-zA-Z0-9]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            return "Unknown";
        }

        private string ExtractEHentaiTitle(string html, string galleryUrl)
        {
            if (!string.IsNullOrWhiteSpace(html))
            {
                // #gn (English/Romanized title) or #gj (Japanese title)
                var gnMatch = Regex.Match(html, @"<h1[^>]*id=""gn""[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (gnMatch.Success && !string.IsNullOrWhiteSpace(gnMatch.Groups[1].Value))
                {
                    return WebUtility.HtmlDecode(Regex.Replace(gnMatch.Groups[1].Value, @"<[^>]+>", "")).Trim();
                }

                var gjMatch = Regex.Match(html, @"<h1[^>]*id=""gj""[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (gjMatch.Success && !string.IsNullOrWhiteSpace(gjMatch.Groups[1].Value))
                {
                    return WebUtility.HtmlDecode(Regex.Replace(gjMatch.Groups[1].Value, @"<[^>]+>", "")).Trim();
                }

                var titleMatch = Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (titleMatch.Success)
                {
                    string t = WebUtility.HtmlDecode(Regex.Replace(titleMatch.Groups[1].Value, @"\s*-\s*E-Hentai Galleries\s*$", "", RegexOptions.IgnoreCase)).Trim();
                    if (!string.IsNullOrWhiteSpace(t)) return t;
                }
            }

            return "E-Hentai Gallery " + GetEHentaiGalleryIdFromLink(galleryUrl);
        }

        internal string ExtractEHentaiCoverUrl(string html, string galleryUrl)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;

            // #gd1 > div style="background:transparent url(...) ..."
            var bgMatch = Regex.Match(html, @"id=""gd1""[^>]*>.*?url\((['""]?)(?<url>https?://[^'"")]+)\1\)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (bgMatch.Success)
            {
                return bgMatch.Groups["url"].Value;
            }

            // Normal img in #gd1
            var imgMatch = Regex.Match(html, @"id=""gd1""[^>]*>.*?<img[^>]+src=['""](?<url>https?://[^'""]+)['""]", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (imgMatch.Success)
            {
                return imgMatch.Groups["url"].Value;
            }

            // Fallback first gdt thumbnail
            var gdtMatch = Regex.Match(html, @"id=""gdt""[^>]*>.*?<img[^>]+src=['""](?<url>https?://[^'""]+)['""]", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (gdtMatch.Success)
            {
                return gdtMatch.Groups["url"].Value;
            }

            return string.Empty;
        }

        internal List<string> ExtractEHentaiPreviewThumbnails(string html, string galleryUrl)
        {
            var results = new List<string>();
            if (string.IsNullOrWhiteSpace(html)) return results;

            // Extract all thumbnail images in #gdt
            var matches = Regex.Matches(html, @"<div[^>]*class=""gdt[ml]""[^>]*>.*?<img[^>]+src=['""](?<url>https?://[^'""]+)['""]", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match m in matches)
            {
                string url = m.Groups["url"].Value;
                if (!string.IsNullOrWhiteSpace(url) && !results.Contains(url))
                {
                    results.Add(url);
                }
            }

            return results;
        }

        private void BtnClearEHentaiLog_Click(object sender, RoutedEventArgs e)
        {
            if (txtEHentaiLog != null)
            {
                txtEHentaiLog.Document.Blocks.Clear();
            }
        }
    }
}

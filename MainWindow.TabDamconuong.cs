using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
        private const string DamconuongBaseUrl = "https://damconuong.shop";
        private const string DamconuongSiteFolder = "damconuong.shop";
        private const string DamconuongBrandSuffixPattern = @"\s*[-|]\s*(?:HentaiVN\s*-\s*)?Dâm Cô Nương\s*$";
        private const string DamconuongChapterSuffixPattern = @"\s+(?:chương|chap|chapter)\s+\d+(?:\.\d+)?\s*$";

        private void DamconuongLog(string message)
        {
            Log("[damconuong.shop] " + message);
        }

        private bool IsDamconuongUrl(string url)
        {
            return TryParseDamconuongUri(url, out _);
        }

        private string NormalizeDamconuongRedirectInput()
        {
            string redirectValue = string.Empty;
            if (txtDamconuongRedirectDomain != null)
            {
                if (Dispatcher.CheckAccess())
                {
                    redirectValue = txtDamconuongRedirectDomain.Text;
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        redirectValue = txtDamconuongRedirectDomain.Text;
                    });
                }
            }

            redirectValue = (redirectValue ?? string.Empty).Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(redirectValue))
            {
                return string.Empty;
            }

            if (!redirectValue.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !redirectValue.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                redirectValue = "https://" + redirectValue;
            }

            try
            {
                var redirectUri = new Uri(redirectValue);
                return $"{redirectUri.Scheme}://{redirectUri.Host}";
            }
            catch
            {
                return string.Empty;
            }
        }

        private bool IsDamconuongKnownHost(string host)
        {
            string normalizedHost = (host ?? string.Empty).Trim();
            if (normalizedHost.IndexOf("damconuong.", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            string redirectBaseUrl = NormalizeDamconuongRedirectInput();
            if (Uri.TryCreate(redirectBaseUrl, UriKind.Absolute, out Uri redirectUri) &&
                string.Equals(normalizedHost, redirectUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private string ApplyDamconuongRedirectDomain(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return url;
            }

            string candidate = url.Trim();
            bool isDamconuong = candidate.IndexOf("damconuong", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                candidate.IndexOf("mbpro.vip", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isDamconuong)
            {
                return url;
            }

            string redirectBaseUrl = NormalizeDamconuongRedirectInput();
            if (string.IsNullOrWhiteSpace(redirectBaseUrl))
            {
                return url;
            }

            if (!candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                candidate = "https://" + candidate;
            }

            try
            {
                var sourceUri = new Uri(candidate);
                var redirectUri = new Uri(redirectBaseUrl);
                var builder = new UriBuilder(sourceUri)
                {
                    Scheme = redirectUri.Scheme,
                    Host = redirectUri.Host,
                    Port = redirectUri.IsDefaultPort ? -1 : redirectUri.Port
                };
                return builder.Uri.ToString().TrimEnd('/');
            }
            catch
            {
                return url;
            }
        }

        private string GetDamconuongResolvedBaseUrl()
        {
            string redirectBaseUrl = NormalizeDamconuongRedirectInput();
            return string.IsNullOrWhiteSpace(redirectBaseUrl) ? DamconuongBaseUrl : redirectBaseUrl;
        }

        private string[] GetDamconuongAllowedHosts()
        {
            var hosts = new List<string> { "damconuong.shop", "damconuong.store", "mbpro.vip" };
            if (Uri.TryCreate(GetDamconuongResolvedBaseUrl(), UriKind.Absolute, out Uri redirectUri) &&
                !hosts.Contains(redirectUri.Host, StringComparer.OrdinalIgnoreCase))
            {
                hosts.Add(redirectUri.Host);
            }

            return hosts.ToArray();
        }

        private async Task RefreshDamconuongRedirectDomainAsync()
        {
            const string probeUrl = "https://damconuong.shop/the-loai/elf";

            try
            {
                Uri finalUri = await ProbeNettruyenTechRedirectUriAsync(probeUrl);
                if (finalUri == null)
                {
                    return;
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    if (txtDamconuongRedirectDomain == null)
                    {
                        return;
                    }

                    string resolvedBaseUrl = string.Equals(finalUri.Host, "damconuong.shop", StringComparison.OrdinalIgnoreCase)
                        ? string.Empty
                        : $"{finalUri.Scheme}://{finalUri.Host}";

                    string currentValue = NormalizeDamconuongRedirectInput();
                    if (string.Equals(currentValue, resolvedBaseUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    txtDamconuongRedirectDomain.Text = resolvedBaseUrl;
                    if (txtDamconuongTagUrl != null)
                    {
                        txtDamconuongTagUrl.Text = ApplyDamconuongRedirectDomain(txtDamconuongTagUrl.Text);
                    }
                    RequestGalleryListAutosave(0);
                });
            }
            catch (Exception ex)
            {
                DamconuongLog($"[redirect] Không kiểm tra được domain: {ex.Message}");
            }
        }

        private async Task EnsureDamconuongRedirectDomainAsync()
        {
            if (!string.IsNullOrWhiteSpace(NormalizeDamconuongRedirectInput()))
            {
                return;
            }

            if (Interlocked.Exchange(ref _damconuongRedirectProbeStarted, 1) != 0)
            {
                return;
            }

            try
            {
                await RefreshDamconuongRedirectDomainAsync();
            }
            finally
            {
                Interlocked.Exchange(ref _damconuongRedirectProbeStarted, 0);
            }
        }

        private bool IsDamconuongCategoryUrl(string url)
        {
            return TryParseDamconuongUri(url, out Uri uri) &&
                   uri.AbsolutePath.StartsWith("/the-loai/", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsDamconuongBookUrl(string url)
        {
            return TryParseDamconuongUri(url, out Uri uri) &&
                   uri.AbsolutePath.StartsWith("/truyen/", StringComparison.OrdinalIgnoreCase) &&
                   uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Length == 2;
        }

        private bool IsDamconuongChapterUrl(string url)
        {
            return TryParseDamconuongUri(url, out Uri uri) &&
                   uri.AbsolutePath.StartsWith("/truyen/", StringComparison.OrdinalIgnoreCase) &&
                   uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Length >= 3;
        }

        private bool TryParseDamconuongUri(string url, out Uri uri)
        {
            uri = null;
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            string normalized = NormalizeDamconuongUrl(url);
            return Uri.TryCreate(normalized, UriKind.Absolute, out uri) &&
                   IsDamconuongKnownHost(uri.Host);
        }

        private string NormalizeDamconuongUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            string normalized = url.Trim();
            if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                normalized = DamconuongBaseUrl + (normalized.StartsWith("/", StringComparison.Ordinal) ? string.Empty : "/") + normalized;
            }

            normalized = ApplyDamconuongRedirectDomain(normalized);

            if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri uri))
            {
                return normalized;
            }

            var builder = new UriBuilder(uri)
            {
                Fragment = string.Empty
            };

            string path = builder.Path.TrimEnd('/');
            if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(0, path.Length - 5);
            }

            builder.Path = string.IsNullOrWhiteSpace(path) ? "/" : path;
            return builder.Uri.AbsoluteUri.TrimEnd('/');
        }

        private string GetDamconuongPageUrl(string baseUrl, int page)
        {
            string normalized = NormalizeDamconuongUrl(baseUrl);
            if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri uri))
            {
                return normalized;
            }

            if (page <= 1)
            {
                return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
            }

            var builder = new UriBuilder(uri);
            string query = (builder.Query ?? string.Empty).TrimStart('?');
            query = Regex.Replace(query, @"(^|&)(?:amp;)?page=\d+(&|$)", "$1", RegexOptions.IgnoreCase).Trim('&');
            builder.Query = string.IsNullOrWhiteSpace(query) ? $"page={page}" : $"{query}&page={page}";
            return builder.Uri.AbsoluteUri.TrimEnd('/');
        }

        private string CleanDamconuongTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return string.Empty;
            }

            string clean = WebUtility.HtmlDecode(Regex.Replace(title, @"<[^>]+>", string.Empty)).Trim();
            clean = Regex.Replace(clean, DamconuongBrandSuffixPattern, string.Empty, RegexOptions.IgnoreCase).Trim();
            clean = Regex.Replace(clean, @"\s+", " ").Trim();
            return FormatGalleryTitle(clean);
        }

        private bool IsDamconuongLoginRequiredHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return false;
            }

            if (html.IndexOf("Chưa có chương nào", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            return html.IndexOf("Yêu cầu đăng nhập", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   html.IndexOf("Nội dung này dành cho người dùng đã xác thực", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   html.IndexOf("Tạo tài khoản mới", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string GetDamconuongLoginRequiredMessage()
        {
            return _isVietnameseUi
                ? "Qua tab mật khẩu, đăng nhập để download."
                : "Go to the password tab, sign in, then download.";
        }

        private string GetDamconuongNoChaptersMessage()
        {
            return _isVietnameseUi ? "Chưa có chương nào." : "No chapters yet.";
        }

        private string GetDamconuongLoginRequiredDisplayName()
        {
            return _isVietnameseUi
                ? "Qua tab mật khẩu, đăng nhập để download"
                : "Go to password tab, sign in to download";
        }

        private bool IsDamconuongLoginWarningTitle(string title)
        {
            string normalized = (title ?? string.Empty).Trim();
            return normalized.IndexOf("18+", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalized.IndexOf("cảnh báo nội dung", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalized.IndexOf("canh bao noi dung", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   string.Equals(normalized, GetDamconuongLoginRequiredDisplayName(), StringComparison.OrdinalIgnoreCase);
        }

        private string ExtractDamconuongBookTitleFromPageHtml(string html, string pageUrl, string fallbackName)
        {
            string title = ExtractDamconuongTitleFromHtml(html);
            if (string.IsNullOrWhiteSpace(title))
            {
                title = CleanDamconuongTitle(fallbackName);
            }

            if (!string.IsNullOrWhiteSpace(title) && !IsDamconuongChapterUrl(pageUrl))
            {
                return title;
            }

            string stripped = Regex.Replace(title ?? string.Empty, DamconuongBrandSuffixPattern, string.Empty, RegexOptions.IgnoreCase).Trim();
            if (!string.IsNullOrWhiteSpace(stripped))
            {
                string withoutChapter = Regex.Replace(stripped, DamconuongChapterSuffixPattern, string.Empty, RegexOptions.IgnoreCase).Trim();
                if (!string.IsNullOrWhiteSpace(withoutChapter))
                {
                    return withoutChapter;
                }
            }

            string bookUrl = GetDamconuongBookUrl(pageUrl);
            return CleanDamconuongTitle(GetDamconuongSlugFromLink(bookUrl).Replace('-', ' '));
        }

        private IEnumerable<GalleryItem> GetDamconuongUiItemsByLink(string link)
        {
            string normalizedLink = NormalizeDamconuongUrl(link);
            var items = new List<GalleryItem>();

            if (dgResults != null && Dispatcher.CheckAccess())
            {
                items.AddRange(dgResults.Items
                    .OfType<GalleryItem>()
                    .Where(item => item != null &&
                                   string.Equals(NormalizeDamconuongUrl(item.Link), normalizedLink, StringComparison.OrdinalIgnoreCase)));
            }

            items.AddRange(_scrapedItems
                .Where(item => item != null &&
                               string.Equals(NormalizeDamconuongUrl(item.Link), normalizedLink, StringComparison.OrdinalIgnoreCase)));

            return items.Distinct().ToList();
        }

        private async Task<string> ResolveDamconuongBookTitleAfterLoginAsync(GalleryItem item, CancellationToken token)
        {
            var candidateUrls = new List<string>();
            string normalizedLink = NormalizeDamconuongUrl(item?.Link);
            string bookUrl = GetDamconuongBookUrl(normalizedLink);

            if (!string.IsNullOrWhiteSpace(bookUrl))
            {
                candidateUrls.Add(bookUrl);
            }

            if (!string.IsNullOrWhiteSpace(normalizedLink) && !candidateUrls.Contains(normalizedLink, StringComparer.OrdinalIgnoreCase))
            {
                candidateUrls.Add(normalizedLink);
            }

            if (TryGetCachedDownloadChapterLinks(item, out List<string> cachedChapterLinks))
            {
                candidateUrls.AddRange(cachedChapterLinks.Take(2));
            }

            for (int attempt = 0; attempt < 3; attempt++)
            {
                foreach (string candidateUrl in candidateUrls
                    .Where(url => !string.IsNullOrWhiteSpace(url))
                    .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    token.ThrowIfCancellationRequested();
                    string html = await FetchStringAsync(candidateUrl, token);
                    if (IsDamconuongLoginRequiredHtml(html))
                    {
                        continue;
                    }

                    string title = ExtractDamconuongBookTitleFromPageHtml(html, candidateUrl, item?.Name);
                    if (!string.IsNullOrWhiteSpace(title) && !IsDamconuongLoginWarningTitle(title))
                    {
                        return title;
                    }
                }

                if (attempt < 2)
                {
                    await Task.Delay(350, token);
                }
            }

            return string.Empty;
        }

        internal async Task<int> RefreshDamconuongBlockedBookNamesAfterLoginAsync(CancellationToken token)
        {
            List<GalleryItem> uiItems = await Dispatcher.InvokeAsync(() =>
                (dgResults?.Items.OfType<GalleryItem>() ?? Enumerable.Empty<GalleryItem>()).ToList());

            var targets = uiItems
                .Concat(_scrapedItems)
                .Where(item => item != null &&
                               IsDamconuongUrl(item.Link) &&
                               (IsDamconuongLoginWarningTitle(item.Name) ||
                                string.Equals(item.Status, "Error", StringComparison.OrdinalIgnoreCase) ||
                                ((item.CurrentProcess ?? string.Empty).IndexOf("đăng nhập", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                ((item.CurrentProcess ?? string.Empty).IndexOf("sign in", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                item.Errors.Any(err => !string.IsNullOrWhiteSpace(err?.ErrorMessage) &&
                                                       (err.ErrorMessage.IndexOf("đăng nhập", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                        err.ErrorMessage.IndexOf("sign in", StringComparison.OrdinalIgnoreCase) >= 0))))
                .GroupBy(item => NormalizeDamconuongUrl(item.Link))
                .Select(group => group.First())
                .ToList();

            int refreshed = 0;
            foreach (GalleryItem item in targets)
            {
                token.ThrowIfCancellationRequested();
                string title = await ResolveDamconuongBookTitleAfterLoginAsync(item, token);
                if (string.IsNullOrWhiteSpace(title) || IsDamconuongLoginWarningTitle(title))
                {
                    continue;
                }

                bool changedAny = false;
                await Dispatcher.InvokeAsync(() =>
                {
                    foreach (GalleryItem matchedItem in GetDamconuongUiItemsByLink(item.Link))
                    {
                        if (!string.Equals(matchedItem.Name, title, StringComparison.Ordinal))
                        {
                            matchedItem.Name = title;
                            refreshed++;
                            changedAny = true;
                        }

                        QueueDownloadMissingChapterRescan(matchedItem);
                    }

                    if (changedAny)
                    {
                        SafeRefreshResultsView();
                    }
                });
                if (changedAny)
                {
                    await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                }
            }

            return refreshed;
        }

        private async void BtnDamconuongFetchInfo_Click(object sender, RoutedEventArgs e)
        {
            string url = txtDamconuongTagUrl.Text.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show("Vui lòng nhập URL hợp lệ.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            url = NormalizeDamconuongUrl(url);
            txtDamconuongTagUrl.Text = url;

            btnDamconuongFetchInfo.IsEnabled = false;
            lblStatus.Text = "Đang phân tích trang damconuong.shop...";
            progressBar.IsIndeterminate = true;

            try
            {
                if (!IsDamconuongCategoryUrl(url))
                {
                    txtDamconuongTotalPages.Text = "1";
                    txtDamconuongPageTo.Text = "1";
                    lblStatus.Text = "Analysis complete. Found 1 page.";
                    return;
                }

                string html = await FetchStringAsync(url, _downloadCts?.Token ?? CancellationToken.None);
                int maxPage = 1;
                foreach (Match match in Regex.Matches(html ?? string.Empty, @"[?&](?:amp;)?page=(\d+)", RegexOptions.IgnoreCase))
                {
                    if (int.TryParse(match.Groups[1].Value, out int pageNum) && pageNum > maxPage)
                    {
                        maxPage = pageNum;
                    }
                }

                txtDamconuongTotalPages.Text = maxPage.ToString(CultureInfo.InvariantCulture);
                txtDamconuongPageTo.Text = maxPage.ToString(CultureInfo.InvariantCulture);
                lblStatus.Text = $"Analysis complete. Found {maxPage} pages.";
            }
            catch (Exception ex)
            {
                DamconuongLog("Lỗi khi phân tích: " + ex.Message);
                txtDamconuongTotalPages.Text = "1";
                txtDamconuongPageTo.Text = "1";
                lblStatus.Text = "Analysis failed.";
            }
            finally
            {
                btnDamconuongFetchInfo.IsEnabled = true;
                progressBar.IsIndeterminate = false;
            }
        }

        private void TxtDamconuongTotalPages_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (txtDamconuongPageTo != null && txtDamconuongTotalPages != null)
            {
                txtDamconuongPageTo.Text = txtDamconuongTotalPages.Text;
            }
        }

        private async void BtnDamconuongScrape_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                btnDamconuongScrape.Content = "CANCELLING...";
                btnDamconuongScrape.IsEnabled = false;
                if (btnDamconuongCrawlMore != null)
                {
                    btnDamconuongCrawlMore.IsEnabled = false;
                }
                return;
            }

            if (!ConfirmScrapeDuringDownloadIfNeeded(true)) return;
            SelectDownloadMangaTab();
            await ScrapeDamconuongAsync(clearExisting: true);
        }

        private async void BtnDamconuongCrawlMore_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                if (btnDamconuongCrawlMore != null)
                {
                    btnDamconuongCrawlMore.Content = "CANCELLING...";
                    btnDamconuongCrawlMore.IsEnabled = false;
                }
                btnDamconuongScrape.IsEnabled = false;
                return;
            }

            SelectDownloadMangaTab();
            await ScrapeDamconuongAsync(clearExisting: false);
        }

        private async Task ScrapeDamconuongAsync(bool clearExisting)
        {
            string baseUrl = NormalizeDamconuongUrl(txtDamconuongTagUrl.Text.Trim());
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                MessageBox.Show("Vui lòng nhập URL hợp lệ.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            txtDamconuongTagUrl.Text = baseUrl;

            if (clearExisting)
            {
                _scrapedItems.Clear();
                if (chkSelectAll != null)
                {
                    chkSelectAll.IsChecked = false;
                }
                lblLinkCount.Text = "0";
            }

            if (!IsDamconuongCategoryUrl(baseUrl))
            {
                await ImportDamconuongDirectLinksAsync(new List<string> { baseUrl });
                return;
            }

            if (!int.TryParse(txtDamconuongPageFrom.Text, out int pageFrom) || pageFrom < 1)
            {
                MessageBox.Show("Trang bắt đầu không hợp lệ.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!int.TryParse(txtDamconuongPageTo.Text, out int pageTo) || pageTo < pageFrom)
            {
                MessageBox.Show("Trang kết thúc không hợp lệ.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            btnDamconuongScrape.Content = "STOP CRAWLER";
            if (btnDamconuongCrawlMore != null)
            {
                btnDamconuongCrawlMore.Content = "STOP CRAWLER";
            }
            btnDamconuongFetchInfo.IsEnabled = false;
            lblStatus.Text = "Đang cào damconuong.shop...";
            progressBar.Value = 0;

            try
            {
                ShowTransientResultsImportingStatus("getting link...");
                await CrawlDamconuongCategoryAsync(baseUrl, pageFrom, pageTo, isChecked: false, token: token);
                RecalculateDuplicates();
                lblStatus.Text = "Crawling completed successfully.";
                lblLinkCount.Text = _scrapedItems.Count.ToString();
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = "Crawling cancelled.";
            }
            catch (Exception ex)
            {
                DamconuongLog("Lỗi khi cào: " + ex.Message);
                lblStatus.Text = "Crawling failed.";
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                btnDamconuongScrape.Content = "GET LINK";
                btnDamconuongScrape.IsEnabled = true;
                if (btnDamconuongCrawlMore != null)
                {
                    btnDamconuongCrawlMore.Content = "GET MORE";
                    btnDamconuongCrawlMore.IsEnabled = true;
                }
                btnDamconuongFetchInfo.IsEnabled = true;
                HideTransientResultsImportingStatus();
            }
        }

        private void BtnDamconuongPasteDirect_Click(object sender, RoutedEventArgs e)
        {
            var win = new DirectDownloadWindow(
                customTitle: "PASTE DAMCONUONG LINKS",
                customDescription: "Paste damconuong.shop category/book/chapter links below. Direct chapter links are imported as single items.",
                customExample: "Example:\nhttps://damconuong.shop/the-loai/elf\nhttps://damconuong.shop/truyen/truyen-san-vo-nguoi-o-the-gioi-khac\nhttps://damconuong.shop/truyen/truyen-san-vo-nguoi-o-the-gioi-khac/chapter-97.html")
            {
                Owner = this
            };

            win.OnImport = async links =>
            {
                if (links != null && links.Any())
                {
                    await ImportDamconuongDirectLinksAsync(links);
                }
            };

            win.ShowDialog();
        }

        private async Task ImportDamconuongDirectLinksAsync(List<string> links, bool showMessageBox = true)
        {
            bool keepControlsEnabled = IsResultsImportingActive();
            if (!keepControlsEnabled)
            {
                btnDamconuongScrape.IsEnabled = false;
                btnDamconuongFetchInfo.IsEnabled = false;
            }
            progressBar.Value = 0;
            progressBar.IsIndeterminate = false;

            int total = links?.Count ?? 0;
            int imported = 0;
            int failed = 0;

            try
            {
                for (int i = 0; i < total; i++)
                {
                    string rawLink = links[i];
                    string link = NormalizeDamconuongUrl(rawLink);
                    if (string.IsNullOrWhiteSpace(link))
                    {
                        failed++;
                        continue;
                    }

                    lblStatus.Text = $"[{i + 1}/{total}] Đang phân tích: {link}";

                    try
                    {
                        if (IsDamconuongCategoryUrl(link))
                        {
                            await CrawlDamconuongCategoryAsync(link, 1, int.MaxValue, isChecked: true, token: _downloadCts?.Token ?? CancellationToken.None);
                            imported++;
                            continue;
                        }

                        GalleryItem item = await BuildDamconuongDirectItemAsync(link);
                        AddDamconuongImportedItem(item);
                        imported++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        DamconuongLog($"Import lỗi với '{link}': {ex.Message}");
                        AddDamconuongImportedItem(new GalleryItem
                        {
                            Link = link,
                            Name = CleanDamconuongTitle(GetDamconuongSlugFromLink(link).Replace('-', ' ')),
                            OriginalIndex = _scrapedItems.Count,
                            IsChecked = true,
                            SourceDomain = DamconuongSiteFolder
                        });
                    }

                    if (!keepControlsEnabled)
                    {
                        progressBar.Value = ((double)(i + 1) / Math.Max(1, total)) * 100d;
                    }
                    lblLinkCount.Text = _scrapedItems.Count.ToString();
                }

                RecalculateDuplicates();
                lblLinkCount.Text = _scrapedItems.Count.ToString();
                lblStatus.Text = $"Import completed. Success: {imported}, Failed: {failed}.";

                ShowImportSummaryIfNeeded(showMessageBox, total, imported, failed);
            }
            finally
            {
                if (!keepControlsEnabled)
                {
                    btnDamconuongScrape.IsEnabled = true;
                    btnDamconuongFetchInfo.IsEnabled = true;
                }
            }
        }

        private void AddDamconuongImportedItem(GalleryItem item)
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

        private async Task CrawlDamconuongCategoryAsync(string categoryUrl, int pageFrom, int pageTo, bool isChecked, CancellationToken token)
        {
            string url = NormalizeDamconuongUrl(categoryUrl);
            string html = await FetchStringAsync(url, token);
            int maxPage = 1;
            foreach (Match match in Regex.Matches(html ?? string.Empty, @"[?&](?:amp;)?page=(\d+)", RegexOptions.IgnoreCase))
            {
                if (int.TryParse(match.Groups[1].Value, out int pageNum) && pageNum > maxPage)
                {
                    maxPage = pageNum;
                }
            }

            int startPage = Math.Max(1, pageFrom);
            int endPage = pageTo == int.MaxValue ? maxPage : Math.Min(maxPage, Math.Max(pageFrom, pageTo));
            int totalPages = Math.Max(1, endPage - startPage + 1);
            int processed = 0;

            for (int page = startPage; page <= endPage; page++)
            {
                token.ThrowIfCancellationRequested();

                string pageUrl = GetDamconuongPageUrl(url, page);
                string pageHtml = page == 1 ? html : await FetchStringAsync(pageUrl, token);
                foreach (GalleryItem item in ExtractDamconuongCategoryItems(pageHtml, pageUrl, isChecked))
                {
                    AddDamconuongImportedItem(item);
                }

                processed++;
                double progressPct = ((double)processed / totalPages) * 100d;
                progressBar.Value = progressPct;
                lblStatus.Text = $"Searching page {page}/{endPage} ({progressPct:0}%)";
                UpdateResultsCrawlProgress(processed, totalPages, GuessImportDisplayName(categoryUrl));
                lblLinkCount.Text = _scrapedItems.Count.ToString();
            }
        }

        private IEnumerable<GalleryItem> ExtractDamconuongCategoryItems(string html, string pageUrl, bool isChecked)
        {
            var items = new List<GalleryItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match match in Regex.Matches(html ?? string.Empty, @"<a[^>]+href=[""'](?<href>[^""']*?/truyen/(?<slug>[^""'?#/]+)(?:/[^""'?#]+)?)[""'][^>]*>(?<inner>[\s\S]*?)</a>", RegexOptions.IgnoreCase))
            {
                string href = WebUtility.HtmlDecode(match.Groups["href"].Value.Trim());
                if (string.IsNullOrWhiteSpace(href))
                {
                    continue;
                }

                string absolute = href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? href
                    : DamconuongBaseUrl + (href.StartsWith("/", StringComparison.Ordinal) ? string.Empty : "/") + href;
                absolute = NormalizeDamconuongUrl(absolute);

                if (!TryParseDamconuongUri(absolute, out Uri uri))
                {
                    continue;
                }

                string[] segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length != 2)
                {
                    continue;
                }

                if (!seen.Add(absolute))
                {
                    continue;
                }

                string slug = segments[1];
                string title = CleanDamconuongTitle(Regex.Replace(match.Groups["inner"].Value, @"<[^>]+>", " "));
                if (string.IsNullOrWhiteSpace(title))
                {
                    title = CleanDamconuongTitle(slug.Replace('-', ' '));
                }

                items.Add(new GalleryItem
                {
                    Link = absolute,
                    Name = title,
                    OriginalIndex = _scrapedItems.Count + items.Count,
                    IsChecked = isChecked,
                    SourceDomain = DamconuongSiteFolder
                });
            }

            return items;
        }

        private async Task<GalleryItem> BuildDamconuongDirectItemAsync(string link)
        {
            string html = await FetchStringAsync(link, _downloadCts?.Token ?? CancellationToken.None);
            string title = IsDamconuongLoginRequiredHtml(html)
                ? GetDamconuongLoginRequiredDisplayName()
                : ExtractDamconuongTitleFromHtml(html);
            if (string.IsNullOrWhiteSpace(title) || IsDamconuongLoginWarningTitle(title))
            {
                title = IsDamconuongLoginRequiredHtml(html)
                    ? GetDamconuongLoginRequiredDisplayName()
                    : GetDamconuongSlugFromLink(link).Replace('-', ' ');
            }

            return new GalleryItem
            {
                Link = link,
                Name = title,
                OriginalIndex = _scrapedItems.Count,
                IsChecked = true,
                SourceDomain = DamconuongSiteFolder
            };
        }

        private string ExtractDamconuongTitleFromHtml(string html)
        {
            string title = string.Empty;

            Match match = Regex.Match(html ?? string.Empty, @"<title[^>]*>\s*(?<title>[\s\S]*?)\s*</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (match.Success)
            {
                title = CleanDamconuongTitle(match.Groups["title"].Value);
            }

            if (!string.IsNullOrWhiteSpace(title))
            {
                return title;
            }

            match = Regex.Match(html ?? string.Empty, @"<meta[^>]+property=[""']og:title[""'][^>]+content=[""'](?<title>[^""']+)[""']", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                title = CleanDamconuongTitle(match.Groups["title"].Value);
            }

            return title;
        }

        private string GetDamconuongSlugFromLink(string link)
        {
            if (!TryParseDamconuongUri(link, out Uri uri))
            {
                return "damconuong";
            }

            string[] segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2)
            {
                return segments[1];
            }

            return "damconuong";
        }

        private string GetDamconuongBookUrl(string link)
        {
            if (!TryParseDamconuongUri(link, out Uri uri))
            {
                return NormalizeDamconuongUrl(link);
            }

            string[] segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2 &&
                string.Equals(segments[0], "truyen", StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeDamconuongUrl($"{DamconuongBaseUrl}/truyen/{segments[1]}");
            }

            return NormalizeDamconuongUrl(link);
        }

        private async Task DownloadDamconuongGalleryAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem = null, ChapterFilter chapterFilter = null)
        {
            item.Link = NormalizeDamconuongUrl(item.Link);

            if (IsDamconuongChapterUrl(item.Link))
            {
                await DownloadDamconuongChapterAsync(item, rootFolder, token, queueItem);
                return;
            }

            if (!IsDamconuongBookUrl(item.Link))
            {
                throw new Exception("Link damconuong không hợp lệ. Cần link book hoặc chapter.");
            }

            await DownloadDamconuongBookAsync(item, rootFolder, token, queueItem, chapterFilter);
        }

        private async Task DownloadDamconuongBookAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem, ChapterFilter chapterFilter)
        {
            string bookUrl = NormalizeDamconuongUrl(item.Link);
            string html = await FetchStringAsync(bookUrl, token);
            if (IsDamconuongLoginRequiredHtml(html))
            {
                throw new Exception(GetDamconuongLoginRequiredMessage());
            }

            string bookTitle = CleanDamconuongTitle(item.Name);
            if (string.IsNullOrWhiteSpace(bookTitle))
            {
                bookTitle = ExtractDamconuongTitleFromHtml(html);
            }

            string[] segments = new Uri(bookUrl).AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            string bookSlug = segments.Length >= 2 ? segments[1] : GetDamconuongSlugFromLink(bookUrl);
            bookTitle = string.IsNullOrWhiteSpace(bookTitle) ? CleanDamconuongTitle(bookSlug.Replace('-', ' ')) : bookTitle;
            item.Name = bookTitle;
            if (queueItem != null)
            {
                Dispatcher.Invoke(() => queueItem.Name = bookTitle);
            }

            var chapterLinks = ExtractDamconuongChapterLinks(html, bookUrl);
            CacheDownloadMissingChapterLinks(item, chapterLinks);
            if (chapterFilter != null)
            {
                var filtered = chapterLinks.Where(link => chapterFilter.IsMatch(ParseDamconuongChapterNumber(link))).ToList();
                chapterLinks = FilterPendingChapterLinksFromProcess(rootFolder, DamconuongSiteFolder, item, filtered);
            }
            else
            {
                chapterLinks = FilterPendingChapterLinksFromProcess(rootFolder, DamconuongSiteFolder, item, chapterLinks);
            }

            if (chapterLinks.Count == 0)
            {
                throw new Exception(GetDamconuongNoChaptersMessage());
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
                string chapterLink = chapterLinks[i];
                var chapterItem = new GalleryItem
                {
                    Link = chapterLink,
                    Name = bookTitle,
                    SourceDomain = DamconuongSiteFolder
                };

                bool chapterCompleted = false;
                try
                {
                    chapterCompleted = await DownloadDamconuongChapterAsync(chapterItem, rootFolder, token, queueItem, isParentQueue: true);
                }
                catch (Exception ex)
                {
                    DamconuongLog($"Lỗi chapter '{chapterLink}': {ex.Message}");
                    if (queueItem != null)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            queueItem.AddError(GetDamconuongChapterSlugFromLink(chapterLink), 0, ex.Message, chapterLink, chapterLink);
                        });
                    }
                }

                if (chapterCompleted)
                {
                    MarkChapterProcessDone(rootFolder, DamconuongSiteFolder, item, chapterLink);
                    completedCount++;
                }

                if (queueItem != null && chapterCompleted)
                {
                    Dispatcher.Invoke(() => queueItem.CompletedChapters = completedCount);
                }
            }
        }

        private async Task<bool> DownloadDamconuongChapterAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem = null, bool isParentQueue = false)
        {
            string chapterUrl = NormalizeDamconuongUrl(item.Link);
            string chapterSlug = GetDamconuongChapterSlugFromLink(chapterUrl);
            string html = await FetchStringAsync(chapterUrl, token);
            if (IsDamconuongLoginRequiredHtml(html))
            {
                throw new Exception(GetDamconuongLoginRequiredMessage());
            }

            string title = ExtractDamconuongTitleFromHtml(html);
            string bookTitle = title;
            string chapterTitle = string.Empty;
            if (!string.IsNullOrWhiteSpace(title))
            {
                string stripped = Regex.Replace(title, DamconuongBrandSuffixPattern, string.Empty, RegexOptions.IgnoreCase).Trim();
                string withoutChapter = Regex.Replace(stripped, DamconuongChapterSuffixPattern, string.Empty, RegexOptions.IgnoreCase).Trim();
                chapterTitle = Regex.Replace(stripped, @"^" + Regex.Escape(withoutChapter) + @"\s*", string.Empty, RegexOptions.IgnoreCase).Trim();
                if (string.IsNullOrWhiteSpace(chapterTitle))
                {
                    chapterTitle = Regex.Match(stripped, @"(?:Chương|Chapter|Chap)\s+\d+(?:\.\d+)?", RegexOptions.IgnoreCase).Value;
                }
                bookTitle = withoutChapter;
            }

            if (string.IsNullOrWhiteSpace(bookTitle))
            {
                bookTitle = CleanDamconuongTitle(item.Name);
            }
            if (string.IsNullOrWhiteSpace(bookTitle))
            {
                bookTitle = CleanDamconuongTitle(GetDamconuongSlugFromLink(chapterUrl).Replace('-', ' '));
            }

            if (string.IsNullOrWhiteSpace(chapterTitle))
            {
                chapterTitle = NormalizeChapterLabel("Chapter " + chapterSlug.Replace("-", "."));
            }
            else
            {
                chapterTitle = NormalizeChapterLabel(chapterTitle);
            }

            item.Name = bookTitle;
            string processChapterLabel = CompactSingleLine(chapterTitle);

            string safeBook = GetCanonicalBookFolderName(item, bookTitle, "Unknown Book");
            string aliasSafeBook = GetSafePathName(bookTitle);
            string safeChapter = GetDownloadChapterFolderName(bookTitle, chapterTitle);
            string siteRootFolder = GetSiteDownloadRoot(rootFolder, DamconuongSiteFolder);
            await NormalizeChapterFolderAliasAsync(siteRootFolder, safeBook, aliasSafeBook, safeChapter, token);

            string unmergedPath = Path.Combine(siteRootFolder, $"{safeBook}-{safeChapter}");
            string mergedPath = Path.Combine(siteRootFolder, safeBook, safeChapter);
            string finalTargetFolder = _isSingleComicFolderType ? mergedPath : unmergedPath;
            string tempFolder = BuildStableTempFolderPath(siteRootFolder, DamconuongSiteFolder, safeBook, safeChapter, chapterUrl);
            Directory.CreateDirectory(tempFolder);
            RegisterTempFolder(tempFolder);

            try
            {
                var imageUrls = ExtractDamconuongImageUrls(html);
                if (imageUrls.Count == 0)
                {
                    throw new Exception("Không tìm thấy ảnh chapter damconuong.");
                }

                if (queueItem != null && !isParentQueue)
                {
                    Dispatcher.Invoke(() =>
                    {
                        queueItem.TotalChapters = imageUrls.Count;
                        queueItem.CompletedChapters = 0;
                    });
                }

                if (queueItem != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        queueItem.DownloadingChapter = processChapterLabel;
                        queueItem.DownloadingPageProgress = $"1/{imageUrls.Count}";
                        queueItem.CurrentProcess = isParentQueue
                            ? $"{processChapterLabel} (trang 1/{imageUrls.Count})"
                            : $"1/{imageUrls.Count} pages";
                    });
                }

                WriteTempProgressLog(tempFolder, item, "Downloading", 0, imageUrls.Count, "0/0 pages", $"Bắt đầu tải {chapterTitle}");

                int maxThreads = GetCurrentConnectionLimit();
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
                            var pageWatch = Stopwatch.StartNew();
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
                                string fileName = pageFilenames[index];
                                string localFilePath = Path.Combine(tempFolder, fileName);
                                if (File.Exists(localFilePath) && new FileInfo(localFilePath).Length > 1024)
                                {
                                    lock (lockObj)
                                    {
                                        completedPages++;
                                        string processText = isParentQueue
                                            ? $"{processChapterLabel} (trang {completedPages}/{imageUrls.Count})"
                                            : $"{completedPages}/{imageUrls.Count} pages";
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

                                await DownloadUrlToFileWithRefererAsync(imgUrl, chapterUrl, localFilePath, token);
                                pageWatch.Stop();
                                lock (lockObj)
                                {
                                    completedPages++;
                                    long downloadedBytes = File.Exists(localFilePath) ? new FileInfo(localFilePath).Length : 0;
                                    string processText = isParentQueue
                                        ? $"{processChapterLabel} (trang {completedPages}/{imageUrls.Count})"
                                        : $"{completedPages}/{imageUrls.Count} pages";
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

                WriteTempProgressLog(tempFolder, item, "Done", imageUrls.Count, imageUrls.Count, $"{imageUrls.Count}/{imageUrls.Count} pages", "Download completed");
                MoveTempFolderToTarget(tempFolder, finalTargetFolder, "damconuong");
                bool chapterValid = ValidateDownloadedFiles(finalTargetFolder, imageUrls.Count, queueItem ?? item, chapterTitle, chapterUrl: chapterUrl);
                if (chapterValid && queueItem != null && !string.IsNullOrWhiteSpace(chapterSlug))
                {
                    ClearResolvedErrors(queueItem, chapterSlug);
                }

                return chapterValid;
            }
            finally
            {
                UnregisterTempFolder(tempFolder);
            }
        }

        private List<string> ExtractDamconuongChapterLinks(string html, string bookUrl)
        {
            var links = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!TryParseDamconuongUri(bookUrl, out Uri bookUri))
            {
                return links;
            }

            string[] segments = bookUri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2)
            {
                return links;
            }

            string bookSlug = segments[1];
            string sameBookChapterPattern = @"href\s*=\s*[""'](?<href>(?:(?:https?:)?\/\/(?:www\.)?damconuong\.[^\/""']+)?\/truyen\/" + Regex.Escape(bookSlug) + @"/(?<chapter>[^""'?#>]+)(?:\.html)?)[""']";
            foreach (Match match in Regex.Matches(html ?? string.Empty, sameBookChapterPattern, RegexOptions.IgnoreCase))
            {
                string href = WebUtility.HtmlDecode(match.Groups["href"].Value.Trim());
                if (string.IsNullOrWhiteSpace(href))
                {
                    continue;
                }

                string absolute = href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? href
                    : $"{bookUri.Scheme}://{bookUri.Host}" + (href.StartsWith("/", StringComparison.Ordinal) ? string.Empty : "/") + href;
                absolute = NormalizeDamconuongUrl(absolute);

                if (!seen.Add(absolute))
                {
                    continue;
                }

                links.Add(absolute);
            }

            // ponytail: HTML hiện tại trả chap 1 riêng trước, rồi phần còn lại giảm dần; Reverse() làm chap 1 tụt xuống cuối.
            return links
                .OrderBy(ParseDamconuongChapterNumber)
                .ThenBy(link => link, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private double ParseDamconuongChapterNumber(string url)
        {
            if (!TryParseDamconuongUri(url, out Uri uri))
            {
                return 0d;
            }

            string[] segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 3)
            {
                return 0d;
            }

            string token = Path.GetFileNameWithoutExtension(segments[2]);
            if (TryParseChapterNumberFromChapterToken(token, out double strictValue))
            {
                return strictValue;
            }

            Match match = Regex.Match(token, @"(?<num>\d+(?:\.\d+)?)");
            if (match.Success && double.TryParse(match.Groups["num"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
            {
                return value;
            }

            return 0d;
        }

        private string GetDamconuongChapterSlugFromLink(string chapterUrl)
        {
            if (!TryParseDamconuongUri(chapterUrl, out Uri uri))
            {
                return string.Empty;
            }

            string[] segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 3)
            {
                return string.Empty;
            }

            return Path.GetFileNameWithoutExtension(segments[2]);
        }

        private List<string> ExtractDamconuongImageUrls(string html)
        {
            string contentHtml = ExtractHtmlElementById(html, "chapter-content");
            if (string.IsNullOrWhiteSpace(contentHtml))
            {
                contentHtml = ExtractHtmlElementByClass(html, "reading-detail box_doc");
            }

            if (string.IsNullOrWhiteSpace(contentHtml))
            {
                return new List<string>();
            }

            var imageUrls = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in Regex.Matches(contentHtml, @"<img[^>]+(?:data-src|src)\s*=\s*[""'](?<url>[^""']+)[""']", RegexOptions.IgnoreCase))
            {
                string imageUrl = WebUtility.HtmlDecode(match.Groups["url"].Value.Trim()).Replace("\\/", "/");
                if (string.IsNullOrWhiteSpace(imageUrl) ||
                    imageUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (imageUrl.StartsWith("//", StringComparison.Ordinal))
                {
                    imageUrl = "https:" + imageUrl;
                }

                if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out Uri uri))
                {
                    continue;
                }

                string ext = Path.GetExtension(uri.AbsolutePath);
                if (string.IsNullOrWhiteSpace(ext))
                {
                    continue;
                }

                switch (ext.ToLowerInvariant())
                {
                    case ".webp":
                    case ".gif":
                    case ".jpg":
                    case ".jpeg":
                    case ".png":
                    case ".bmp":
                        break;
                    default:
                        continue;
                }

                if (seen.Add(imageUrl))
                {
                    imageUrls.Add(imageUrl);
                }
            }

            return imageUrls;
        }
    }
}

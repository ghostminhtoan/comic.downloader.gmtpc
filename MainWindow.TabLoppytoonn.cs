using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
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
        private const string LoppyBaseUrl = "https://loppytoonn.com";
        private const string LoppySiteFolder = "loppytoonn.com";

        private void LoppyLog(string message)
        {
            Log("[loppytoonn.com] " + message);
        }

        private bool IsLoppyUrl(string url)
        {
            return TryParseLoppyUri(url, out _);
        }

        private bool IsLoppyCategoryUrl(string url)
        {
            return TryParseLoppyUri(url, out Uri uri) &&
                   GetLoppySegments(uri).Length == 2 &&
                   GetLoppySegments(uri)[0].Equals("the-loai", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsLoppyBookUrl(string url)
        {
            if (!TryParseLoppyUri(url, out Uri uri))
            {
                return false;
            }

            string[] segments = GetLoppySegments(uri);
            return segments.Length == 2 &&
                   segments[0].Equals("truyen", StringComparison.OrdinalIgnoreCase) &&
                   !string.IsNullOrWhiteSpace(segments[1]);
        }

        private bool IsLoppyChapterUrl(string url)
        {
            if (!TryParseLoppyUri(url, out Uri uri))
            {
                return false;
            }

            string[] segments = GetLoppySegments(uri);
            return segments.Length == 3 &&
                   segments[0].Equals("truyen", StringComparison.OrdinalIgnoreCase) &&
                   !string.IsNullOrWhiteSpace(segments[1]) &&
                   !string.IsNullOrWhiteSpace(segments[2]);
        }

        private bool TryParseLoppyUri(string url, out Uri uri)
        {
            uri = null;
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            string normalized = url.Trim();
            if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                normalized = LoppyBaseUrl + (normalized.StartsWith("/", StringComparison.Ordinal) ? string.Empty : "/") + normalized;
            }

            if (!Uri.TryCreate(normalized, UriKind.Absolute, out uri))
            {
                return false;
            }

            return uri.Host.IndexOf("loppytoonn.com", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string NormalizeLoppyUrl(string url)
        {
            if (!TryParseLoppyUri(url, out Uri uri))
            {
                throw new ArgumentException("URL loppytoonn.com không hợp lệ.");
            }

            var builder = new UriBuilder(uri)
            {
                Query = string.Empty,
                Fragment = string.Empty
            };

            string path = builder.Path.TrimEnd('/');
            builder.Path = string.IsNullOrWhiteSpace(path) ? "/" : path;
            return builder.Uri.AbsoluteUri.TrimEnd('/');
        }

        private static string[] GetLoppySegments(Uri uri)
        {
            return uri?.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
        }

        private string CleanLoppyTitle(string value)
        {
            string clean = WebUtility.HtmlDecode(Regex.Replace(value ?? string.Empty, @"<[^>]+>", " ")).Trim();
            clean = Regex.Replace(clean, @"\s+", " ").Trim();
            clean = Regex.Replace(clean, @"\s*-\s*LoppyToon\s*$", string.Empty, RegexOptions.IgnoreCase).Trim();
            clean = Regex.Replace(clean, @"^\s*Truyện\s+", string.Empty, RegexOptions.IgnoreCase).Trim();
            return FormatGalleryTitle(clean);
        }

        private string HumanizeLoppySlug(string slug)
        {
            string clean = Regex.Replace((slug ?? string.Empty).Trim('/'), @"[-_]+", " ").Trim();
            return string.IsNullOrWhiteSpace(clean) ? "LoppyToon" : FormatGalleryTitle(clean);
        }

        private static bool IsLoppyNovelTitle(string title)
        {
            return !string.IsNullOrWhiteSpace(title) &&
                   title.IndexOf("novel", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void TxtLoppyTagUrl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText()?.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        txtLoppyTagUrl.Text = text;
                        txtLoppyTagUrl.CaretIndex = txtLoppyTagUrl.Text.Length;
                        e.Handled = true;
                    }
                }

                return;
            }

            if (e.Key == Key.Enter)
            {
                BtnLoppyAnalyze_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void TxtLoppyTotalPages_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (txtLoppyPageTo != null && txtLoppyTotalPages != null)
            {
                txtLoppyPageTo.Text = txtLoppyTotalPages.Text;
            }
        }

        private async void BtnLoppyAnalyze_Click(object sender, RoutedEventArgs e)
        {
            await AnalyzeLoppyUrlAsync(txtLoppyTagUrl?.Text);
        }

        private async void BtnLoppyScrape_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                btnLoppyScrape.Content = "CANCELLING...";
                btnLoppyScrape.IsEnabled = false;
                btnLoppyCrawlMore.IsEnabled = false;
                return;
            }
            if (!ConfirmScrapeDuringDownloadIfNeeded(true)) return;
            SelectDownloadMangaTab();
            await ScrapeLoppyAsync(clearExisting: true);
        }

        private async void BtnLoppyCrawlMore_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                btnLoppyCrawlMore.Content = "CANCELLING...";
                btnLoppyCrawlMore.IsEnabled = false;
                btnLoppyScrape.IsEnabled = false;
                return;
            }

            SelectDownloadMangaTab();
            await ScrapeLoppyAsync(clearExisting: false);
        }

        private async Task AnalyzeLoppyUrlAsync(string rawUrl)
        {
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                ShowWarning("Vui lòng nhập URL loppytoonn.com hợp lệ.", "Thông báo");
                return;
            }

            btnLoppyAnalyze.IsEnabled = false;
            progressBar.IsIndeterminate = true;

            try
            {
                string normalized = NormalizeLoppyUrl(rawUrl);
                txtLoppyTagUrl.Text = normalized;
                CancellationToken token = _downloadCts?.Token ?? CancellationToken.None;
                if (IsLoppyCategoryUrl(normalized))
                {
                    string html = await FetchStringAsync(normalized, token);
                    int totalPages = ExtractLoppyCategoryPageCount(html);
                    txtLoppyTotalPages.Text = Math.Max(1, totalPages).ToString(CultureInfo.InvariantCulture);
                    txtLoppyPageFrom.Text = "1";
                    txtLoppyPageTo.Text = Math.Max(1, totalPages).ToString(CultureInfo.InvariantCulture);
                    lblStatus.Text = $"Loppy category: {totalPages} pages.";
                }
                else
                {
                    txtLoppyTotalPages.Text = "1";
                    txtLoppyPageFrom.Text = "1";
                    txtLoppyPageTo.Text = "1";
                    lblStatus.Text = IsLoppyBookUrl(normalized)
                        ? "Loppy book ready."
                        : IsLoppyChapterUrl(normalized)
                            ? "Loppy chapter ready."
                            : "Đang phân tích loppytoonn.com...";
                }
            }
            catch (Exception ex)
            {
                LoppyLog("Lỗi phân tích: " + ex.Message);
                ShowWarning(ex.Message, "Thông báo");
                lblStatus.Text = "Analysis failed.";
                txtLoppyTotalPages.Text = "1";
                txtLoppyPageFrom.Text = "1";
                txtLoppyPageTo.Text = "1";
            }
            finally
            {
                progressBar.IsIndeterminate = false;
                btnLoppyAnalyze.IsEnabled = true;
            }
        }

        private void BtnLoppyPasteDirect_Click(object sender, RoutedEventArgs e)
        {
            var window = new DirectDownloadWindow(
                customTitle: "PASTE LOPPYTOONN LINKS",
                customDescription: "Paste loppytoonn.com category, book, or chapter links below. App sẽ tự nhận diện đúng kiểu URL.",
                customExample:
                    "Example:\nhttps://loppytoonn.com/the-loai/lang-man\nhttps://loppytoonn.com/truyen/cuoc-hon-nhan-lua-dao\nhttps://loppytoonn.com/truyen/cuoc-hon-nhan-lua-dao/chap-1")
            {
                Owner = this
            };

            window.OnImport = async links => await ImportLoppyDirectLinksAsync(links);
            window.ShowDialog();
        }

        private async Task ScrapeLoppyAsync(bool clearExisting)
        {
            string rawUrl = txtLoppyTagUrl?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                ShowWarning("Vui lòng nhập URL loppytoonn.com hợp lệ.", "Thông báo");
                return;
            }

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            btnLoppyScrape.Content = "STOP CRAWLER";
            btnLoppyCrawlMore.Content = "STOP CRAWLER";
            bool keepControlsEnabled = IsResultsImportingActive();
            if (!keepControlsEnabled)
            {
                btnLoppyAnalyze.IsEnabled = false;
            }
            progressBar.Value = 0;
            progressBar.IsIndeterminate = false;

            if (clearExisting)
            {
                _scrapedItems.Clear();
                lblLinkCount.Text = "0";
            }

            try
            {
                ShowTransientResultsImportingStatus("getting link...");
                await ImportLoppyDirectLinksAsync(new List<string> { rawUrl }, clearExisting: false, showMessageBox: true, token: token);
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = "Crawling cancelled.";
            }
            catch (Exception ex)
            {
                LoppyLog("Lỗi khi crawl: " + ex.Message);
                lblStatus.Text = "Crawling failed.";
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                btnLoppyScrape.Content = "GET LINK";
                btnLoppyCrawlMore.Content = "GET MORE";
                btnLoppyScrape.IsEnabled = true;
                btnLoppyCrawlMore.IsEnabled = true;
                btnLoppyAnalyze.IsEnabled = true;
                HideTransientResultsImportingStatus();
            }
        }

        private async Task ImportLoppyDirectLinksAsync(IReadOnlyList<string> links, bool clearExisting = false, bool showMessageBox = true, CancellationToken? token = null)
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

            bool keepControlsEnabled = IsResultsImportingActive();
            if (!keepControlsEnabled)
            {
                btnLoppyAnalyze.IsEnabled = false;
            }
            progressBar.Value = 0;
            progressBar.IsIndeterminate = false;

            int imported = 0;
            int failed = 0;
            int total = links.Count;
            int processed = 0;

            try
            {
                foreach (string rawLink in links)
                {
                    effectiveToken.ThrowIfCancellationRequested();
                    string normalized;
                    try
                    {
                        normalized = NormalizeLoppyUrl(rawLink);
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        processed++;
                        if (!keepControlsEnabled)
                        {
                            progressBar.Value = total == 0 ? 0 : (double)processed / total * 100;
                        }
                        LoppyLog("Bỏ qua link lỗi: " + ex.Message);
                        continue;
                    }

                    txtLoppyTagUrl.Text = normalized;
                    lblStatus.Text = "Đang xử lý " + normalized;

                    try
                    {
                        List<GalleryItem> items = await CreateLoppyItemsFromUrlAsync(
                            normalized,
                            effectiveToken,
                            IsLoppyCategoryUrl(normalized) ? ParseLoppyPageBox(txtLoppyPageFrom, 1) : 1,
                            IsLoppyCategoryUrl(normalized) ? ParseLoppyPageBox(txtLoppyPageTo, ParseLoppyPageBox(txtLoppyTotalPages, 1)) : 1,
                            (page, endPage) =>
                            {
                                if (!keepControlsEnabled)
                                {
                                    double pageProgress = endPage <= 0 ? 0 : (double)page / endPage * 100d;
                                    progressBar.Value = pageProgress;
                                    lblStatus.Text = $"Đang lấy link trang {page}/{endPage} ({pageProgress:0}%)";
                                }

                                UpdateResultsCrawlProgress(page, endPage, GuessImportDisplayName(normalized));
                            });
                        foreach (GalleryItem item in items)
                        {
                            if (_scrapedItems.Any(existing => string.Equals(existing.Link, item.Link, StringComparison.OrdinalIgnoreCase)))
                            {
                                continue;
                            }

                            item.OriginalIndex = _scrapedItems.Count;
                            _scrapedItems.Add(item);
                            imported++;
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        LoppyLog("Import lỗi với '" + normalized + "': " + ex.Message);
                    }

                    processed++;
                    if (!keepControlsEnabled)
                    {
                        progressBar.Value = total == 0 ? 0 : (double)processed / total * 100;
                    }
                }

                RecalculateDuplicates();
                lblLinkCount.Text = _scrapedItems.Count.ToString(CultureInfo.InvariantCulture);
                lblStatus.Text = $"Imported {imported} loppy item(s).";

                ShowImportSummaryIfNeeded(showMessageBox, total, imported, failed);
            }
            finally
            {
                btnLoppyAnalyze.IsEnabled = true;
            }
        }

        private int ParseLoppyPageBox(TextBox textBox, int fallback)
        {
            if (textBox == null)
            {
                return Math.Max(1, fallback);
            }

            if (int.TryParse(textBox.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0)
            {
                return value;
            }

            return Math.Max(1, fallback);
        }

        private async Task<List<GalleryItem>> CreateLoppyItemsFromUrlAsync(string url, CancellationToken token, int? pageFrom = null, int? pageTo = null, Action<int, int> onCategoryPageChanged = null)
        {
            string normalized = NormalizeLoppyUrl(url);
            string html = await FetchStringAsync(normalized, token);

            if (IsLoppyCategoryUrl(normalized))
            {
                return await ExtractLoppyCategoryItemsAsync(html, normalized, token, pageFrom ?? 1, pageTo ?? int.MaxValue, onCategoryPageChanged);
            }

            if (IsLoppyBookUrl(normalized))
            {
                string title = ExtractLoppyBookTitle(html, normalized);
                List<string> chapterLinks = ExtractLoppyChapterLinks(html, normalized);
                return new List<GalleryItem>
                {
                    new GalleryItem
                    {
                        Link = normalized,
                        Name = title,
                        LinkCount = chapterLinks.Count > 0 ? chapterLinks.Count + " chapters" : string.Empty,
                        SourceDomain = LoppySiteFolder,
                        IsChecked = true
                    }
                };
            }

            if (IsLoppyChapterUrl(normalized))
            {
                string bookTitle = ExtractLoppyBookTitle(html, normalized);
                string chapterTitle = ExtractLoppyChapterTitle(html, normalized);
                return new List<GalleryItem>
                {
                    new GalleryItem
                    {
                        Link = normalized,
                        Name = string.IsNullOrWhiteSpace(chapterTitle) ? bookTitle : $"{bookTitle} - {chapterTitle}",
                        SourceDomain = LoppySiteFolder,
                        IsChecked = true
                    }
                };
            }

            throw new Exception("URL loppytoonn.com không hỗ trợ.");
        }

        private async Task<List<GalleryItem>> ExtractLoppyCategoryItemsAsync(string firstPageHtml, string categoryUrl, CancellationToken token, int pageFrom, int pageTo, Action<int, int> onPageChanged = null)
        {
            string baseCategoryUrl = NormalizeLoppyUrl(categoryUrl);
            int totalPages = ExtractLoppyCategoryPageCount(firstPageHtml);
            int startPage = Math.Max(1, pageFrom);
            int endPage = Math.Min(Math.Max(startPage, pageTo), Math.Max(1, totalPages));
            var results = new List<GalleryItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (startPage == 1)
            {
                AppendLoppyCategoryItems(results, seen, firstPageHtml, baseCategoryUrl);
                onPageChanged?.Invoke(1, endPage);
            }

            for (int page = Math.Max(2, startPage); page <= endPage; page++)
            {
                token.ThrowIfCancellationRequested();
                string pageUrl = baseCategoryUrl + "?page=" + page.ToString(CultureInfo.InvariantCulture);
                string html = await FetchStringAsync(pageUrl, token);
                AppendLoppyCategoryItems(results, seen, html, baseCategoryUrl);
                onPageChanged?.Invoke(page, endPage);
            }

            return results;
        }

        private void AppendLoppyCategoryItems(List<GalleryItem> results, HashSet<string> seen, string html, string categoryUrl)
        {
            Uri baseUri = new Uri(categoryUrl);
            string source = html ?? string.Empty;
            foreach (Match match in Regex.Matches(source, @"<a[^>]+href\s*=\s*[""'](?<href>(?:https?:\/\/(?:www\.)?loppytoonn\.com)?\/truyen\/[^""'#?\s>]+\/?)[""'][^>]*>(?<inner>[\s\S]*?)<\/a>", RegexOptions.IgnoreCase))
            {
                string href = ResolveLoppyUrl(baseUri, match.Groups["href"].Value.Trim());
                if (href.IndexOf("${", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    href.IndexOf("{", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                if (!IsLoppyBookUrl(href))
                {
                    continue;
                }

                string normalized = NormalizeLoppyUrl(href);
                if (!seen.Add(normalized))
                {
                    continue;
                }

                string inner = match.Groups["inner"].Value;
                if (inner.IndexOf("<img", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                string title = string.Empty;
                Match altMatch = Regex.Match(inner, @"\balt\s*=\s*[""'](?<title>[^""']+)[""']", RegexOptions.IgnoreCase);
                if (altMatch.Success)
                {
                    title = CleanLoppyTitle(altMatch.Groups["title"].Value);
                }

                if (string.IsNullOrWhiteSpace(title))
                {
                    Match nameMatch = Regex.Match(inner, @"<p[^>]*class\s*=\s*[""'][^""']*\bcomic-name\b[^""']*[""'][^>]*>(?<title>[\s\S]*?)</p>", RegexOptions.IgnoreCase);
                    if (nameMatch.Success)
                    {
                        title = CleanLoppyTitle(nameMatch.Groups["title"].Value);
                    }
                }

                if (string.IsNullOrWhiteSpace(title))
                {
                    title = HumanizeLoppySlug(GetLoppyBookSlug(normalized));
                }

                results.Add(new GalleryItem
                {
                    Link = normalized,
                    Name = title,
                    SourceDomain = LoppySiteFolder,
                    IsChecked = true
                });
            }
        }

        private int ExtractLoppyCategoryPageCount(string html)
        {
            int maxPage = 1;
            foreach (Match match in Regex.Matches(html ?? string.Empty, @"[?&]page=(?<page>\d+)", RegexOptions.IgnoreCase))
            {
                if (int.TryParse(match.Groups["page"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int page) && page > maxPage)
                {
                    maxPage = page;
                }
            }

            return maxPage;
        }

        private string ExtractLoppyBookTitle(string html, string fallbackUrl)
        {
            Match infoMatch = Regex.Match(html ?? string.Empty, @"<div[^>]*class\s*=\s*[""'][^""']*\binfo-title\b[^""']*[""'][^>]*>(?<title>[\s\S]*?)</div>", RegexOptions.IgnoreCase);
            if (infoMatch.Success)
            {
                string title = CleanLoppyTitle(infoMatch.Groups["title"].Value);
                if (!string.IsNullOrWhiteSpace(title))
                {
                    return title;
                }
            }

            string titleTag = ExtractLoppyBookTitleFromTitleTag(html);
            if (!string.IsNullOrWhiteSpace(titleTag))
            {
                return titleTag;
            }

            return HumanizeLoppySlug(GetLoppyBookSlug(fallbackUrl));
        }

        private string ExtractLoppyChapterTitle(string html, string fallbackUrl)
        {
            Match chapterInfoMatch = Regex.Match(
                html ?? string.Empty,
                @"<span[^>]*class\s*=\s*[""'][^""']*\bchapter-title\b[^""']*[""'][^>]*>(?<title>[\s\S]*?)</span>",
                RegexOptions.IgnoreCase);

            if (chapterInfoMatch.Success)
            {
                string title = CleanLoppyTitle(chapterInfoMatch.Groups["title"].Value);
                if (title.IndexOf(" - ", StringComparison.Ordinal) >= 0)
                {
                    string[] parts = title.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
                    string last = parts[parts.Length - 1].Trim();
                    if (LooksLikeLoppyChapterTitle(last))
                    {
                        return NormalizeChapterLabel(last);
                    }
                }

                if (LooksLikeLoppyChapterTitle(title))
                {
                    return NormalizeChapterLabel(title);
                }
            }

            string titleTag = ExtractLoppyPageTitleTag(html);
            if (!string.IsNullOrWhiteSpace(titleTag))
            {
                Match titleMatch = Regex.Match(titleTag, @"(?<chapter>(?:chap(?:ter)?|chương|chuong)\s*\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
                if (titleMatch.Success)
                {
                    return NormalizeChapterLabel(titleMatch.Groups["chapter"].Value);
                }
            }

            return NormalizeChapterLabel(GetLoppyChapterSlugFromUrl(fallbackUrl).Replace("-", " "));
        }

        private string ExtractLoppyPageTitleTag(string html)
        {
            Match titleMatch = Regex.Match(html ?? string.Empty, @"<title>\s*(?<title>.*?)\s*</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!titleMatch.Success)
            {
                return string.Empty;
            }

            return CleanLoppyTitle(titleMatch.Groups["title"].Value);
        }

        private string ExtractLoppyBookTitleFromTitleTag(string html)
        {
            string title = ExtractLoppyPageTitleTag(html);
            if (title.IndexOf(" - ", StringComparison.Ordinal) > 0)
            {
                string[] parts = title.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    return CleanLoppyTitle(parts[0].Trim());
                }
            }

            return title;
        }

        private static bool LooksLikeLoppyChapterTitle(string title)
        {
            return !string.IsNullOrWhiteSpace(title) &&
                   Regex.IsMatch(title.Trim(), @"^(?i)(?:chap(?:ter)?|chương|chuong)\s*\d+(?:\.\d+)?(?:\b|$)");
        }

        private List<string> ExtractLoppyChapterLinks(string html, string bookUrl)
        {
            var links = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!TryParseLoppyUri(bookUrl, out Uri uri))
            {
                return links;
            }

            string bookSlug = Regex.Escape(GetLoppyBookSlug(bookUrl));
            string source = ExtractHtmlElementByClass(html, "episode-list");
            if (string.IsNullOrWhiteSpace(source))
            {
                source = html ?? string.Empty;
            }

            string pattern = @"href\s*=\s*[""'](?<href>(?:https?:\/\/(?:www\.)?loppytoonn\.com)?\/truyen\/" + bookSlug + @"\/[^""'#?\/]+\/?)[""']";
            foreach (Match match in Regex.Matches(source, pattern, RegexOptions.IgnoreCase))
            {
                string absolute = ResolveLoppyUrl(uri, match.Groups["href"].Value.Trim());
                if (!IsLoppyChapterUrl(absolute))
                {
                    continue;
                }

                string normalized = NormalizeLoppyUrl(absolute);
                if (!seen.Add(normalized))
                {
                    continue;
                }

                links.Add(normalized);
            }

            links.Reverse();
            return links;
        }

        private double ParseLoppyChapterNumber(string url)
        {
            string chapterSlug = GetLoppyChapterSlugFromUrl(url);
            if (TryParseChapterNumberFromChapterToken(chapterSlug, out double strictValue))
            {
                return strictValue;
            }

            Match match = Regex.Match(chapterSlug, @"(?<num>\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (match.Success &&
                double.TryParse(match.Groups["num"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
            {
                return value;
            }

            return 0d;
        }

        private static string ResolveLoppyUrl(Uri baseUri, string href)
        {
            string value = WebUtility.HtmlDecode(href ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            if (value.StartsWith("//", StringComparison.Ordinal))
            {
                value = "https:" + value;
            }

            if (Uri.TryCreate(value, UriKind.Absolute, out Uri absolute))
            {
                return absolute.AbsoluteUri;
            }

            if (Uri.TryCreate(baseUri, value, out Uri relative))
            {
                return relative.AbsoluteUri;
            }

            return value;
        }

        private string GetLoppyBookSlug(string url)
        {
            if (!TryParseLoppyUri(url, out Uri uri))
            {
                return "loppytoon";
            }

            string[] segments = GetLoppySegments(uri);
            return segments.Length >= 2 ? segments[1] : "loppytoon";
        }

        private string GetLoppyChapterSlugFromUrl(string url)
        {
            if (!TryParseLoppyUri(url, out Uri uri))
            {
                return string.Empty;
            }

            string[] segments = GetLoppySegments(uri);
            return segments.Length >= 3 ? segments[2] : string.Empty;
        }

        private async Task DownloadLoppyGalleryAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem = null, ChapterFilter chapterFilter = null)
        {
            item.Link = NormalizeLoppyUrl(item.Link);

            if (IsLoppyChapterUrl(item.Link))
            {
                string chapterHtml = await FetchStringAsync(item.Link, token);
                if (IsLoppyNovelTitle(ExtractLoppyBookTitle(chapterHtml, item.Link)))
                {
                    await DownloadLoppyNovelChapterAsync(item, rootFolder, token, queueItem, isParentQueue: false, bookTitleOverride: null, prefetchedHtml: chapterHtml, chapterSequenceIndex: 1);
                    return;
                }

                await DownloadLoppyChapterAsync(item, rootFolder, token, queueItem, prefetchedHtml: chapterHtml);
                return;
            }

            if (!IsLoppyBookUrl(item.Link))
            {
                throw new Exception("Link loppytoonn.com không hợp lệ. Cần link book hoặc chapter.");
            }

            string bookHtml = await FetchStringAsync(item.Link, token);
            string bookTitle = ExtractLoppyBookTitle(bookHtml, item.Link);
            item.Name = bookTitle;
            if (IsLoppyNovelTitle(bookTitle))
            {
                await DownloadLoppyNovelBookAsync(item, rootFolder, token, queueItem, chapterFilter, bookHtml);
                return;
            }

            await DownloadLoppyBookAsync(item, rootFolder, token, queueItem, chapterFilter, bookHtml);
        }

        private async Task DownloadLoppyBookAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem, ChapterFilter chapterFilter, string prefetchedHtml = null)
        {
            string bookUrl = NormalizeLoppyUrl(item.Link);
            if (TryGetCachedDownloadChapterLinks(item, out List<string> cachedChapterLinks))
            {
                cachedChapterLinks = cachedChapterLinks.OrderBy(ParseLoppyChapterNumber).ToList();
                List<string> effectiveChapterLinks = chapterFilter != null
                    ? FilterPendingChapterLinksFromProcess(rootFolder, LoppySiteFolder, item, cachedChapterLinks.Where(link => chapterFilter.IsMatch(ParseLoppyChapterNumber(link))).ToList())
                    : FilterPendingChapterLinksFromProcess(rootFolder, LoppySiteFolder, item, cachedChapterLinks);
                if (effectiveChapterLinks.Count == 0)
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

                string cachedBookTitle = string.IsNullOrWhiteSpace(item.Name) ? GetSafePathName(bookUrl) : item.Name;
                if (queueItem != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        queueItem.TotalChapters = effectiveChapterLinks.Count;
                        queueItem.CompletedChapters = 0;
                    });
                }

                int cachedCompletedCount = 0;
                foreach (string chapterLink in effectiveChapterLinks)
                {
                    token.ThrowIfCancellationRequested();

                    var chapterItem = new GalleryItem
                    {
                        Link = chapterLink,
                        Name = cachedBookTitle,
                        SourceDomain = LoppySiteFolder
                    };

                    bool completed = await DownloadLoppyChapterAsync(chapterItem, rootFolder, token, queueItem, isParentQueue: true, bookTitleOverride: cachedBookTitle);
                    if (completed)
                    {
                        MarkChapterProcessDone(rootFolder, LoppySiteFolder, item, chapterLink);
                        cachedCompletedCount++;
                    }

                    if (queueItem != null && completed)
                    {
                        Dispatcher.Invoke(() => queueItem.CompletedChapters = cachedCompletedCount);
                    }
                }

                return;
            }

            string html = string.IsNullOrWhiteSpace(prefetchedHtml)
                ? await FetchStringAsync(bookUrl, token)
                : prefetchedHtml;
            string bookTitle = ExtractLoppyBookTitle(html, bookUrl);
            item.Name = bookTitle;

            List<string> chapterLinks = ExtractLoppyChapterLinks(html, bookUrl);
            CacheDownloadMissingChapterLinks(item, chapterLinks);
            if (chapterFilter != null)
            {
                var filtered = chapterLinks.Where(link => chapterFilter.IsMatch(ParseLoppyChapterNumber(link))).ToList();
                chapterLinks = FilterPendingChapterLinksFromProcess(rootFolder, LoppySiteFolder, item, filtered);
            }
            else
            {
                chapterLinks = FilterPendingChapterLinksFromProcess(rootFolder, LoppySiteFolder, item, chapterLinks);
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
            foreach (string chapterLink in chapterLinks)
            {
                token.ThrowIfCancellationRequested();

                var chapterItem = new GalleryItem
                {
                    Link = chapterLink,
                    Name = bookTitle,
                    SourceDomain = LoppySiteFolder
                };

                bool completed = await DownloadLoppyChapterAsync(chapterItem, rootFolder, token, queueItem, isParentQueue: true, bookTitleOverride: bookTitle);
                if (completed)
                {
                    MarkChapterProcessDone(rootFolder, LoppySiteFolder, item, chapterLink);
                    completedCount++;
                }

                if (queueItem != null && completed)
                {
                    Dispatcher.Invoke(() => queueItem.CompletedChapters = completedCount);
                }
            }
        }

        private async Task<bool> DownloadLoppyChapterAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem = null, bool isParentQueue = false, string bookTitleOverride = null, string prefetchedHtml = null)
        {
            string chapterUrl = NormalizeLoppyUrl(item.Link);
            string html = string.IsNullOrWhiteSpace(prefetchedHtml)
                ? await FetchStringAsync(chapterUrl, token)
                : prefetchedHtml;
            string bookTitle = string.IsNullOrWhiteSpace(bookTitleOverride)
                ? ExtractLoppyBookTitle(html, chapterUrl)
                : CleanLoppyTitle(bookTitleOverride);
            string chapterTitle = NormalizeChapterLabel(ExtractLoppyChapterTitle(html, chapterUrl));
            string chapterSlug = GetLoppyChapterSlugFromUrl(chapterUrl);

            if (string.IsNullOrWhiteSpace(bookTitle))
            {
                bookTitle = HumanizeLoppySlug(GetLoppyBookSlug(chapterUrl));
            }

            if (string.IsNullOrWhiteSpace(chapterTitle))
            {
                chapterTitle = NormalizeChapterLabel(chapterSlug.Replace("-", " "));
            }

            item.Name = bookTitle;
            string processChapterLabel = CompactSingleLine(chapterTitle);
            string safeBook = GetCanonicalBookFolderName(item, bookTitle, "Unknown Book");
            string aliasSafeBook = GetSafePathName(bookTitle);
            string safeChapter = GetDownloadChapterFolderName(bookTitle, chapterTitle);
            string siteRootFolder = GetSiteDownloadRoot(rootFolder, LoppySiteFolder);
            await NormalizeChapterFolderAliasAsync(siteRootFolder, safeBook, aliasSafeBook, safeChapter, token);

            string unmergedPath = Path.Combine(siteRootFolder, $"{safeBook}-{safeChapter}");
            string mergedPath = Path.Combine(siteRootFolder, safeBook, safeChapter);
            string finalTargetFolder = _isSingleComicFolderType ? mergedPath : unmergedPath;
            string tempFolder = BuildStableTempFolderPath(siteRootFolder, LoppySiteFolder, safeBook, safeChapter, chapterUrl);
            Directory.CreateDirectory(tempFolder);
            RegisterTempFolder(tempFolder);

            try
            {
                List<string> imageUrls = ExtractLoppyImageUrls(html);
                if (imageUrls.Count == 0)
                {
                    throw new Exception("Không tìm thấy ảnh chapter loppytoonn.com.");
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
                List<string> pageFilenames = DetermineImageFilenames(imageUrls);
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
                                        Dispatcher.BeginInvoke((Action)(() => queueItem.DownloadingPageProgress = $"{pageNumber}/{imageUrls.Count}"));
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
                MoveTempFolderToTarget(tempFolder, finalTargetFolder, "loppytoonn");
                return ValidateDownloadedFiles(finalTargetFolder, imageUrls.Count, queueItem ?? item, chapterTitle, chapterUrl: chapterUrl);
            }
            finally
            {
                UnregisterTempFolder(tempFolder);
            }
        }

        private List<string> ExtractLoppyImageUrls(string html)
        {
            string scope = ExtractHtmlElementByClass(html, "reader-container");
            if (string.IsNullOrWhiteSpace(scope))
            {
                scope = html ?? string.Empty;
            }

            var results = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in Regex.Matches(scope, @"<img[^>]+(?:data-src|src)\s*=\s*[""'](?<url>[^""']+)[""'][^>]*>", RegexOptions.IgnoreCase))
            {
                string imageUrl = WebUtility.HtmlDecode(match.Groups["url"].Value.Trim()).Replace("\\/", "/");
                if (string.IsNullOrWhiteSpace(imageUrl) || imageUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (imageUrl.StartsWith("//", StringComparison.Ordinal))
                {
                    imageUrl = "https:" + imageUrl;
                }

                if (imageUrl.IndexOf("credit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    imageUrl.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out Uri imageUri))
                {
                    continue;
                }

                switch (Path.GetExtension(imageUri.AbsolutePath).ToLowerInvariant())
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
                    results.Add(imageUrl);
                }
            }

            return results;
        }

        private async Task DownloadLoppyNovelBookAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem, ChapterFilter chapterFilter, string prefetchedHtml = null)
        {
            string bookUrl = NormalizeLoppyUrl(item.Link);
            string html = string.IsNullOrWhiteSpace(prefetchedHtml)
                ? await FetchStringAsync(bookUrl, token)
                : prefetchedHtml;
            string bookTitle = ExtractLoppyBookTitle(html, bookUrl);
            item.Name = bookTitle;

            List<string> chapterLinks = ExtractLoppyChapterLinks(html, bookUrl);
            CacheDownloadMissingChapterLinks(item, chapterLinks);
            if (chapterFilter != null)
            {
                var filtered = chapterLinks.Where(link => chapterFilter.IsMatch(ParseLoppyChapterNumber(link))).ToList();
                chapterLinks = FilterPendingChapterLinksFromProcess(rootFolder, LoppySiteFolder, item, filtered);
            }
            else
            {
                chapterLinks = FilterPendingChapterLinksFromProcess(rootFolder, LoppySiteFolder, item, chapterLinks);
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
            for (int index = 0; index < chapterLinks.Count; index++)
            {
                string chapterLink = chapterLinks[index];
                token.ThrowIfCancellationRequested();

                var chapterItem = new GalleryItem
                {
                    Link = chapterLink,
                    Name = bookTitle,
                    SourceDomain = LoppySiteFolder
                };

                bool completed = await DownloadLoppyNovelChapterAsync(
                    chapterItem,
                    rootFolder,
                    token,
                    queueItem,
                    isParentQueue: true,
                    bookTitleOverride: bookTitle,
                    prefetchedHtml: null,
                    chapterSequenceIndex: index + 1);
                if (completed)
                {
                    MarkChapterProcessDone(rootFolder, LoppySiteFolder, item, chapterLink);
                    completedCount++;
                }

                if (queueItem != null && completed)
                {
                    Dispatcher.Invoke(() => queueItem.CompletedChapters = completedCount);
                }
            }
        }

        private async Task<bool> DownloadLoppyNovelChapterAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem, bool isParentQueue, string bookTitleOverride, string prefetchedHtml, int chapterSequenceIndex)
        {
            string chapterUrl = NormalizeLoppyUrl(item.Link);
            string html = string.IsNullOrWhiteSpace(prefetchedHtml)
                ? await FetchStringAsync(chapterUrl, token)
                : prefetchedHtml;
            string bookTitle = string.IsNullOrWhiteSpace(bookTitleOverride)
                ? ExtractLoppyBookTitle(html, chapterUrl)
                : CleanLoppyTitle(bookTitleOverride);
            string chapterTitle = NormalizeChapterLabel(ExtractLoppyChapterTitle(html, chapterUrl));

            if (string.IsNullOrWhiteSpace(bookTitle))
            {
                bookTitle = HumanizeLoppySlug(GetLoppyBookSlug(chapterUrl));
            }

            if (string.IsNullOrWhiteSpace(chapterTitle))
            {
                chapterTitle = NormalizeChapterLabel(GetLoppyChapterSlugFromUrl(chapterUrl).Replace("-", " "));
            }

            string markdown = BuildLoppyNovelChapterMarkdown(bookTitle, chapterTitle, chapterUrl, html);
            item.Name = bookTitle;

            string siteRootFolder = GetSiteDownloadRoot(rootFolder, LoppySiteFolder);
            string safeBook = GetCanonicalBookFolderName(item, bookTitle, "loppytoon-novel", 72);
            string aliasSafeBook = GetSafePathName(bookTitle, 72);
            await NormalizeBookFolderAliasAsync(siteRootFolder, safeBook, aliasSafeBook, token);

            string finalTargetFolder = Path.Combine(siteRootFolder, safeBook);
            Directory.CreateDirectory(finalTargetFolder);

            string chapterFilePath = Path.Combine(finalTargetFolder, BuildHakoChapterFileName(chapterTitle, Math.Max(1, chapterSequenceIndex)));
            File.WriteAllText(chapterFilePath, markdown, new System.Text.UTF8Encoding(true));

            if (queueItem != null)
            {
                Dispatcher.Invoke(() =>
                {
                    queueItem.DownloadingChapter = CompactSingleLine(chapterTitle);
                    queueItem.DownloadingPageProgress = "1/1";
                    queueItem.CurrentProcess = isParentQueue
                        ? $"{CompactSingleLine(chapterTitle)} (.md)"
                        : "1/1 file";
                });
            }

            return File.Exists(chapterFilePath) && new FileInfo(chapterFilePath).Length > 16;
        }

        private string BuildLoppyNovelChapterMarkdown(string bookTitle, string chapterTitle, string chapterUrl, string html)
        {
            string contentHtml = ExtractHtmlElementByClass(html, "chapter-content");
            if (string.IsNullOrWhiteSpace(contentHtml))
            {
                throw new Exception("Không tìm thấy .chapter-content cho novel loppytoonn.com.");
            }

            string contentMarkdown = ConvertLoppyNovelContentHtmlToMarkdown(contentHtml, chapterUrl);
            EnsureHakoChapterHasText(Regex.Replace(contentMarkdown ?? string.Empty, @"!\[[^\]]*\]\([^)]+\)", string.Empty).Trim());

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<style>");
            sb.AppendLine("body { max-width: 880px; margin: 0 auto; padding: 24px; line-height: 1.85; font-family: Georgia, \"Times New Roman\", serif !important; color: #1f2937; }");
            sb.AppendLine("p, li { line-height: 1.85; }");
            sb.AppendLine("h1, h2 { line-height: 1.35; color: #111827; }");
            sb.AppendLine("img { display: block; max-width: 100%; height: auto; margin: 20px auto; border-radius: 8px; }");
            sb.AppendLine("hr { border: 0; border-top: 1px solid #e5e7eb; margin: 24px 0; }");
            sb.AppendLine(".ln-meta { color: #6b7280; font-size: 0.95em; margin-top: -4px; }");
            sb.AppendLine(".ln-meta a { color: #2563eb; text-decoration: none; }");
            sb.AppendLine(".ln-meta a:hover { text-decoration: underline; }");
            sb.AppendLine("</style>");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(bookTitle))
            {
                sb.AppendLine("# " + bookTitle.Trim());
                sb.AppendLine();
            }

            sb.AppendLine("## " + chapterTitle.Trim());
            sb.AppendLine();
            sb.AppendLine("<div class=\"ln-meta\">Nguồn: <a href=\"" + chapterUrl.Trim() + "\">" + chapterUrl.Trim() + "</a></div>");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine(contentMarkdown.Trim());
            sb.AppendLine();
            return sb.ToString();
        }

        private string ConvertLoppyNovelContentHtmlToMarkdown(string contentHtml, string chapterUrl)
        {
            if (string.IsNullOrWhiteSpace(contentHtml))
            {
                return string.Empty;
            }

            Uri baseUri = new Uri(NormalizeLoppyUrl(chapterUrl));
            string text = Regex.Replace(contentHtml, @"<(script|style|iframe|svg)[^>]*>.*?</\1>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(
                text,
                @"<img[^>]+(?:data-src|src)\s*=\s*[""'](?<url>[^""']+)[""'][^>]*?(?:alt\s*=\s*[""'](?<alt>[^""']*)[""'])?[^>]*>",
                match =>
                {
                    string rawUrl = WebUtility.HtmlDecode(match.Groups["url"].Value.Trim()).Replace("\\/", "/");
                    if (string.IsNullOrWhiteSpace(rawUrl) ||
                        rawUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                        rawUrl.IndexOf("credit", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return Environment.NewLine;
                    }

                    string imageUrl = ResolveLoppyUrl(baseUri, rawUrl);
                    if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out Uri imageUri))
                    {
                        return Environment.NewLine;
                    }

                    string extension = Path.GetExtension(imageUri.AbsolutePath).ToLowerInvariant();
                    if (extension != ".webp" &&
                        extension != ".gif" &&
                        extension != ".jpg" &&
                        extension != ".jpeg" &&
                        extension != ".png" &&
                        extension != ".bmp")
                    {
                        return Environment.NewLine;
                    }

                    string alt = WebUtility.HtmlDecode(match.Groups["alt"].Value.Trim());
                    if (string.IsNullOrWhiteSpace(alt) || alt.IndexOf("credit", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        alt = "image";
                    }

                    return "\n\n![" + alt + "](" + imageUrl + ")\n\n";
                },
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, @"<a[^>]*>(?:\s|&nbsp;)*</a>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, @"<a[^>]*href\s*=\s*[""'][^""']+[""'][^>]*>(?<inner>.*?)</a>", "${inner}", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"</p\s*>", "\n\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<p[^>]*>", string.Empty, RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"</div\s*>", "\n\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<div[^>]*>", string.Empty, RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"</h[1-6]\s*>", "\n\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<h[1-6][^>]*>", string.Empty, RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<[^>]+>", string.Empty, RegexOptions.Singleline);
            text = WebUtility.HtmlDecode(text);
            text = text.Replace('\u00a0', ' ');
            text = Regex.Replace(text, @"[ \t]+\n", "\n");
            text = Regex.Replace(text, @"\n[ \t]+", "\n");
            text = Regex.Replace(text, @"\n{3,}", "\n\n");

            var lines = text
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
                .Select(line => line.Trim())
                .Where(line =>
                    !string.IsNullOrWhiteSpace(line) &&
                    !Regex.IsMatch(line, @"^Ảnh tạm thời bị tắt\.?$", RegexOptions.IgnoreCase))
                .ToList();

            var dedupedLines = new List<string>();
            foreach (string line in lines)
            {
                if (dedupedLines.Count > 0 && string.Equals(dedupedLines[dedupedLines.Count - 1], line, StringComparison.Ordinal))
                {
                    continue;
                }

                dedupedLines.Add(line);
            }

            return string.Join("\n\n", dedupedLines);
        }
    }
}

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
        private const string DoctruyenBaseUrl = "https://doctruyen.us";
        private const string DoctruyenSiteFolder = "doctruyen.us";

        private void DoctruyenLog(string message)
        {
            Log("[doctruyen.us] " + message);
        }

        private bool IsDoctruyenUrl(string url)
        {
            return TryParseDoctruyenUri(url, out _);
        }

        private bool IsDoctruyenCategoryUrl(string url)
        {
            return TryParseDoctruyenUri(url, out Uri uri) &&
                   GetDoctruyenSegments(uri).Length == 2 &&
                   GetDoctruyenSegments(uri)[0].Equals("the-loai", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsDoctruyenBookUrl(string url)
        {
            if (!TryParseDoctruyenUri(url, out Uri uri))
            {
                return false;
            }

            string[] segments = GetDoctruyenSegments(uri);
            return segments.Length == 1 && !IsDoctruyenReservedRootSlug(segments[0]);
        }

        private bool IsDoctruyenChapterUrl(string url)
        {
            if (!TryParseDoctruyenUri(url, out Uri uri))
            {
                return false;
            }

            string[] segments = GetDoctruyenSegments(uri);
            return segments.Length == 2 && !segments[0].Equals("the-loai", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryParseDoctruyenUri(string url, out Uri uri)
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
                normalized = DoctruyenBaseUrl + (normalized.StartsWith("/", StringComparison.Ordinal) ? string.Empty : "/") + normalized;
            }

            if (!Uri.TryCreate(normalized, UriKind.Absolute, out uri))
            {
                return false;
            }

            return uri.Host.IndexOf("doctruyen.us", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string NormalizeDoctruyenUrl(string url)
        {
            if (!TryParseDoctruyenUri(url, out Uri uri))
            {
                throw new ArgumentException("URL doctruyen.us không hợp lệ.");
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

        private static string[] GetDoctruyenSegments(Uri uri)
        {
            return uri?.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
        }

        private static bool IsDoctruyenReservedRootSlug(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug))
            {
                return true;
            }

            switch (slug.Trim().ToLowerInvariant())
            {
                case "the-loai":
                case "truyen-moi":
                case "cap-nhat":
                case "da-hoan-thanh":
                case "theo-doi.php":
                case "tim-kiem":
                case "sitemap.xml":
                case "robots.txt":
                case "favicon.ico":
                    return true;
                default:
                    return false;
            }
        }

        private string CleanDoctruyenTitle(string value)
        {
            string clean = WebUtility.HtmlDecode(Regex.Replace(value ?? string.Empty, @"<[^>]+>", " ")).Trim();
            clean = Regex.Replace(clean, @"\s+", " ").Trim();
            clean = Regex.Replace(clean, @"\s*-\s*Đọc truyện online\s*$", string.Empty, RegexOptions.IgnoreCase).Trim();
            return FormatGalleryTitle(clean);
        }

        private string HumanizeDoctruyenSlug(string slug)
        {
            string clean = Regex.Replace((slug ?? string.Empty).Trim('/'), @"[-_]+", " ").Trim();
            return string.IsNullOrWhiteSpace(clean) ? "Doctruyen" : FormatGalleryTitle(clean);
        }

        private void TxtDoctruyenTagUrl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText()?.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        txtDoctruyenTagUrl.Text = text;
                        txtDoctruyenTagUrl.CaretIndex = txtDoctruyenTagUrl.Text.Length;
                        e.Handled = true;
                    }
                }

                return;
            }

            if (e.Key == Key.Enter)
            {
                BtnDoctruyenAnalyze_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private async void BtnDoctruyenAnalyze_Click(object sender, RoutedEventArgs e)
        {
            string rawUrl = txtDoctruyenTagUrl?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                ShowWarning("Vui lòng nhập URL doctruyen.us hợp lệ.", "Thông báo");
                return;
            }

            btnDoctruyenAnalyze.IsEnabled = false;
            btnDoctruyenPasteDirect.IsEnabled = false;
            progressBar.IsIndeterminate = true;

            try
            {
                string normalized = NormalizeDoctruyenUrl(rawUrl);
                txtDoctruyenTagUrl.Text = normalized;
                txtDoctruyenPageHintText.Text = "Web này không hỗ trợ page number.";
                lblStatus.Text = IsDoctruyenCategoryUrl(normalized)
                    ? "Doctruyen category detected. Web này không hỗ trợ page number."
                    : "Đang phân tích doctruyen.us...";

                await ImportDoctruyenDirectLinksAsync(new List<string> { normalized }, showMessageBox: false);
            }
            catch (Exception ex)
            {
                DoctruyenLog("Lỗi phân tích: " + ex.Message);
                ShowWarning(ex.Message, "Thông báo");
                lblStatus.Text = "Analysis failed.";
            }
            finally
            {
                progressBar.IsIndeterminate = false;
                btnDoctruyenAnalyze.IsEnabled = true;
                btnDoctruyenPasteDirect.IsEnabled = true;
            }
        }

        private void BtnDoctruyenPasteDirect_Click(object sender, RoutedEventArgs e)
        {
            var window = new DirectDownloadWindow(
                customTitle: "PASTE DOCTRUYEN LINKS",
                customDescription: "Paste doctruyen.us category, book, or chapter links below. App sẽ tự nhận diện đúng kiểu URL.",
                customExample:
                    "Example:\nhttps://doctruyen.us/the-loai/action\nhttps://doctruyen.us/phi-vu-den-toi\nhttps://doctruyen.us/phi-vu-den-toi/chapter-25")
            {
                Owner = this
            };

            window.OnImport = async links => await ImportDoctruyenDirectLinksAsync(links);
            window.ShowDialog();
        }

        private async Task ImportDoctruyenDirectLinksAsync(IReadOnlyList<string> links, bool showMessageBox = true)
        {
            if (links == null || links.Count == 0)
            {
                return;
            }

            int imported = 0;
            int failed = 0;

            foreach (string rawLink in links)
            {
                string normalized;
                try
                {
                    normalized = NormalizeDoctruyenUrl(rawLink);
                }
                catch (Exception ex)
                {
                    failed++;
                    DoctruyenLog("Bỏ qua link lỗi: " + ex.Message);
                    continue;
                }

                txtDoctruyenTagUrl.Text = normalized;
                lblStatus.Text = "Đang xử lý " + normalized;

                try
                {
                    List<GalleryItem> items = await CreateDoctruyenItemsFromUrlAsync(normalized, _downloadCts?.Token ?? CancellationToken.None);
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
                    DoctruyenLog("Import lỗi với '" + normalized + "': " + ex.Message);
                }
            }

            RecalculateDuplicates();
            lblLinkCount.Text = _scrapedItems.Count.ToString(CultureInfo.InvariantCulture);
            lblStatus.Text = $"Imported {imported} doctruyen item(s).";

            ShowImportSummaryIfNeeded(showMessageBox, links.Count, imported, failed);
        }

        private async Task<List<GalleryItem>> CreateDoctruyenItemsFromUrlAsync(string url, CancellationToken token)
        {
            string normalized = NormalizeDoctruyenUrl(url);
            string html = await FetchStringAsync(normalized, token);

            if (IsDoctruyenCategoryUrl(normalized))
            {
                txtDoctruyenPageHintText.Text = "Web này không hỗ trợ page number.";
                // ponytail: category parse dùng regex + reserved slug filter. Nếu site đổi card markup, nâng cấp sang HTML parser.
                return ExtractDoctruyenCategoryItems(html, normalized);
            }

            if (IsDoctruyenBookUrl(normalized))
            {
                string title = ExtractDoctruyenBookTitle(html, normalized);
                List<string> chapterLinks = ExtractDoctruyenChapterLinks(html, normalized);
                return new List<GalleryItem>
                {
                    new GalleryItem
                    {
                        Link = normalized,
                        Name = title,
                        LinkCount = chapterLinks.Count > 0 ? chapterLinks.Count + " chapters" : string.Empty,
                        SourceDomain = DoctruyenSiteFolder,
                        IsChecked = true
                    }
                };
            }

            if (IsDoctruyenChapterUrl(normalized))
            {
                string bookTitle = ExtractDoctruyenBookTitle(html, normalized);
                string chapterTitle = ExtractDoctruyenChapterTitle(html, normalized);
                return new List<GalleryItem>
                {
                    new GalleryItem
                    {
                        Link = normalized,
                        Name = string.IsNullOrWhiteSpace(chapterTitle) ? bookTitle : $"{bookTitle} - {chapterTitle}",
                        SourceDomain = DoctruyenSiteFolder,
                        IsChecked = true
                    }
                };
            }

            throw new Exception("URL doctruyen.us không hỗ trợ.");
        }

        private List<GalleryItem> ExtractDoctruyenCategoryItems(string html, string categoryUrl)
        {
            var results = new List<GalleryItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Uri baseUri = new Uri(categoryUrl);

            foreach (Match match in Regex.Matches(html ?? string.Empty, @"<a[^>]+href=[""'](?<href>/[^""'#?]+/?)[""'][^>]*>(?<inner>[\s\S]*?)</a>", RegexOptions.IgnoreCase))
            {
                string href = ResolveDoctruyenUrl(baseUri, match.Groups["href"].Value.Trim());
                if (!IsDoctruyenBookUrl(href) || !seen.Add(href))
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
                    title = CleanDoctruyenTitle(altMatch.Groups["title"].Value);
                }

                if (string.IsNullOrWhiteSpace(title))
                {
                    title = HumanizeDoctruyenSlug(GetDoctruyenBookSlug(href));
                }

                results.Add(new GalleryItem
                {
                    Link = NormalizeDoctruyenUrl(href),
                    Name = title,
                    SourceDomain = DoctruyenSiteFolder,
                    IsChecked = true
                });
            }

            return results;
        }

        private string ExtractDoctruyenBookTitle(string html, string fallbackUrl)
        {
            Match h1Match = Regex.Match(html ?? string.Empty, @"<h1[^>]*class=[""'][^""']*\bmanga-title\b[^""']*[""'][^>]*>(?<title>[\s\S]*?)</h1>", RegexOptions.IgnoreCase);
            if (h1Match.Success)
            {
                return CleanDoctruyenTitle(h1Match.Groups["title"].Value);
            }

            string titleTag = ExtractDoctruyenTitleTag(html);
            if (!string.IsNullOrWhiteSpace(titleTag))
            {
                if (titleTag.IndexOf(" - ", StringComparison.Ordinal) > 0)
                {
                    string[] parts = titleTag.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
                    return parts[parts.Length - 1].Trim();
                }

                if (!string.IsNullOrWhiteSpace(titleTag))
                {
                    return titleTag;
                }
            }

            return HumanizeDoctruyenSlug(GetDoctruyenBookSlug(fallbackUrl));
        }

        private string ExtractDoctruyenChapterTitle(string html, string fallbackUrl)
        {
            foreach (Match headerMatch in Regex.Matches(html ?? string.Empty, @"<h2[^>]*>(?<title>[\s\S]*?)</h2>", RegexOptions.IgnoreCase))
            {
                string title = CleanDoctruyenTitle(headerMatch.Groups["title"].Value);
                if (LooksLikeDoctruyenChapterTitle(title))
                {
                    return title;
                }
            }

            string titleTag = ExtractDoctruyenTitleTag(html);
            if (!string.IsNullOrWhiteSpace(titleTag) && titleTag.IndexOf(" - ", StringComparison.Ordinal) > 0)
            {
                string chapterTitle = titleTag.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                if (LooksLikeDoctruyenChapterTitle(chapterTitle))
                {
                    return chapterTitle;
                }
            }

            return NormalizeChapterLabel(GetDoctruyenChapterSlugFromUrl(fallbackUrl).Replace("-", " "));
        }

        private string ExtractDoctruyenTitleTag(string html)
        {
            Match titleMatch = Regex.Match(html ?? string.Empty, @"<title>\s*(?<title>.*?)\s*</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return titleMatch.Success ? CleanDoctruyenTitle(titleMatch.Groups["title"].Value) : string.Empty;
        }

        private static bool LooksLikeDoctruyenChapterTitle(string title)
        {
            return !string.IsNullOrWhiteSpace(title) &&
                   Regex.IsMatch(title.Trim(), @"^(?i)(?:chap(?:ter)?|chương|chuong)\s*\d+(?:\.\d+)?(?:\b|$)");
        }

        private List<string> ExtractDoctruyenChapterLinks(string html, string bookUrl)
        {
            var links = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!TryParseDoctruyenUri(bookUrl, out Uri uri))
            {
                return links;
            }

            string[] segments = GetDoctruyenSegments(uri);
            if (segments.Length != 1)
            {
                return links;
            }

            string bookSlug = Regex.Escape(segments[0]);
            string source = html ?? string.Empty;
            string chapterItemPattern =
                @"<a(?=[^>]*\bclass\s*=\s*[""'][^""']*\bchapter-item\b[^""']*[""'])(?=[^>]*\bhref\s*=\s*[""'](?<href>(?:https?:\/\/(?:www\.)?doctruyen\.us)?\/" +
                bookSlug +
                @"\/[^""'#?\/]+\/?)[""'])[^>]*>";
            var matches = Regex.Matches(source, chapterItemPattern, RegexOptions.IgnoreCase);
            if (matches.Count == 0)
            {
                matches = Regex.Matches(source, @"href\s*=\s*[""'](?<href>(?:https?:\/\/(?:www\.)?doctruyen\.us)?\/" + bookSlug + @"\/[^""'#?\/]+\/?)[""']", RegexOptions.IgnoreCase);
            }

            foreach (Match match in matches)
            {
                string href = match.Groups["href"].Value.Trim();
                if (string.IsNullOrWhiteSpace(href))
                {
                    continue;
                }

                string absolute = ResolveDoctruyenUrl(uri, href);
                if (!IsDoctruyenChapterUrl(absolute) || !seen.Add(absolute))
                {
                    continue;
                }

                links.Add(NormalizeDoctruyenUrl(absolute));
            }

            links.Reverse();
            return links;
        }

        private double ParseDoctruyenChapterNumber(string url)
        {
            string chapterSlug = GetDoctruyenChapterSlugFromUrl(url);
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

        private static string ResolveDoctruyenUrl(Uri baseUri, string href)
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

        private string GetDoctruyenBookSlug(string url)
        {
            if (!TryParseDoctruyenUri(url, out Uri uri))
            {
                return "doctruyen";
            }

            string[] segments = GetDoctruyenSegments(uri);
            return segments.Length > 0 ? segments[0] : "doctruyen";
        }

        private string GetDoctruyenChapterSlugFromUrl(string url)
        {
            if (!TryParseDoctruyenUri(url, out Uri uri))
            {
                return string.Empty;
            }

            string[] segments = GetDoctruyenSegments(uri);
            return segments.Length >= 2 ? segments[1] : string.Empty;
        }

        private async Task DownloadDoctruyenGalleryAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem = null, ChapterFilter chapterFilter = null)
        {
            item.Link = NormalizeDoctruyenUrl(item.Link);

            if (IsDoctruyenChapterUrl(item.Link))
            {
                await DownloadDoctruyenChapterAsync(item, rootFolder, token, queueItem);
                return;
            }

            if (!IsDoctruyenBookUrl(item.Link))
            {
                throw new Exception("Link doctruyen không hợp lệ. Cần link book hoặc chapter.");
            }

            await DownloadDoctruyenBookAsync(item, rootFolder, token, queueItem, chapterFilter);
        }

        private async Task DownloadDoctruyenBookAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem, ChapterFilter chapterFilter)
        {
            string bookUrl = NormalizeDoctruyenUrl(item.Link);
            if (TryGetCachedDownloadChapterLinks(item, out List<string> cachedChapterLinks))
            {
                cachedChapterLinks = cachedChapterLinks.OrderBy(ParseDoctruyenChapterNumber).ToList();
                List<string> effectiveChapterLinks = chapterFilter != null
                    ? FilterPendingChapterLinksFromProcess(rootFolder, DoctruyenSiteFolder, item, cachedChapterLinks.Where(link => chapterFilter.IsMatch(ParseDoctruyenChapterNumber(link))).ToList())
                    : FilterPendingChapterLinksFromProcess(rootFolder, DoctruyenSiteFolder, item, cachedChapterLinks);
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
                        SourceDomain = DoctruyenSiteFolder
                    };

                    bool completed = await DownloadDoctruyenChapterAsync(chapterItem, rootFolder, token, queueItem, isParentQueue: true, bookTitleOverride: cachedBookTitle);
                    if (completed)
                    {
                        MarkChapterProcessDone(rootFolder, DoctruyenSiteFolder, item, chapterLink);
                        cachedCompletedCount++;
                    }

                    if (queueItem != null && completed)
                    {
                        Dispatcher.Invoke(() => queueItem.CompletedChapters = cachedCompletedCount);
                    }
                }

                return;
            }

            string html = await FetchStringAsync(bookUrl, token);
            string bookTitle = ExtractDoctruyenBookTitle(html, bookUrl);
            item.Name = bookTitle;

            List<string> chapterLinks = ExtractDoctruyenChapterLinks(html, bookUrl);
            CacheDownloadMissingChapterLinks(item, chapterLinks);
            if (chapterFilter != null)
            {
                var filtered = chapterLinks.Where(link => chapterFilter.IsMatch(ParseDoctruyenChapterNumber(link))).ToList();
                chapterLinks = FilterPendingChapterLinksFromProcess(rootFolder, DoctruyenSiteFolder, item, filtered);
            }
            else
            {
                chapterLinks = FilterPendingChapterLinksFromProcess(rootFolder, DoctruyenSiteFolder, item, chapterLinks);
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
                    SourceDomain = DoctruyenSiteFolder
                };

                bool completed = await DownloadDoctruyenChapterAsync(chapterItem, rootFolder, token, queueItem, isParentQueue: true, bookTitleOverride: bookTitle);
                if (completed)
                {
                    MarkChapterProcessDone(rootFolder, DoctruyenSiteFolder, item, chapterLink);
                    completedCount++;
                }

                if (queueItem != null && completed)
                {
                    Dispatcher.Invoke(() => queueItem.CompletedChapters = completedCount);
                }
            }
        }

        private async Task<bool> DownloadDoctruyenChapterAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem = null, bool isParentQueue = false, string bookTitleOverride = null)
        {
            string chapterUrl = NormalizeDoctruyenUrl(item.Link);
            string html = await FetchStringAsync(chapterUrl, token);
            string bookTitle = string.IsNullOrWhiteSpace(bookTitleOverride)
                ? ExtractDoctruyenBookTitle(html, chapterUrl)
                : CleanDoctruyenTitle(bookTitleOverride);
            string chapterTitle = NormalizeChapterLabel(ExtractDoctruyenChapterTitle(html, chapterUrl));
            string chapterSlug = GetDoctruyenChapterSlugFromUrl(chapterUrl);

            if (string.IsNullOrWhiteSpace(bookTitle))
            {
                bookTitle = HumanizeDoctruyenSlug(GetDoctruyenBookSlug(chapterUrl));
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
            string siteRootFolder = GetSiteDownloadRoot(rootFolder, DoctruyenSiteFolder);
            await NormalizeChapterFolderAliasAsync(siteRootFolder, safeBook, aliasSafeBook, safeChapter, token);

            string unmergedPath = Path.Combine(siteRootFolder, $"{safeBook}-{safeChapter}");
            string mergedPath = Path.Combine(siteRootFolder, safeBook, safeChapter);
            string finalTargetFolder = _isSingleComicFolderType ? mergedPath : unmergedPath;
            string tempFolder = BuildStableTempFolderPath(siteRootFolder, DoctruyenSiteFolder, safeBook, safeChapter, chapterUrl);
            Directory.CreateDirectory(tempFolder);
            RegisterTempFolder(tempFolder);

            try
            {
                List<string> imageUrls = ExtractDoctruyenImageUrls(html);
                if (imageUrls.Count == 0)
                {
                    throw new Exception("Không tìm thấy ảnh chapter doctruyen.");
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
                MoveTempFolderToTarget(tempFolder, finalTargetFolder, "doctruyen");
                return ValidateDownloadedFiles(finalTargetFolder, imageUrls.Count, queueItem ?? item, chapterTitle, chapterUrl: chapterUrl);
            }
            finally
            {
                UnregisterTempFolder(tempFolder);
            }
        }

        private List<string> ExtractDoctruyenImageUrls(string html)
        {
            string scope = ExtractHtmlElementByClass(html, "reader-images-container");
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
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private const string HakoSiteFolder = "ln.hako.vn";
        private const string HakoBaseUrl = "https://docln.net";

        private sealed class HakoChapterInfo
        {
            public string BookTitle { get; set; }
            public string Title { get; set; }
            public string Link { get; set; }
            public double? ChapterNumber { get; set; }
            public string VolumeTitle { get; set; }
            public int VolumeOrder { get; set; }
            public int SequenceIndex { get; set; }
        }

        private void HakoLog(string message)
        {
            Log("[hako] " + message);
        }

        private bool IsHakoUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            try
            {
                var uri = new Uri(url);
                return uri.Host.IndexOf("ln.hako.vn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       uri.Host.IndexOf("docln.net", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       uri.Host.IndexOf("ln.hako.re", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return url.IndexOf("ln.hako.vn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       url.IndexOf("docln.net", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       url.IndexOf("ln.hako.re", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        private string NormalizeHakoUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            string normalized = WebUtility.HtmlDecode(url).Trim();
            if (normalized.StartsWith("//", StringComparison.Ordinal))
            {
                normalized = "https:" + normalized;
            }
            normalized = normalized.Replace("ln.hako.vn", "docln.net").Replace("ln.hako.re", "docln.net");

            if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                normalized = HakoBaseUrl + (normalized.StartsWith("/") ? string.Empty : "/") + normalized;
            }

            if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri uri) ||
                uri.Host.IndexOf("docln.net", StringComparison.OrdinalIgnoreCase) < 0)
            {
                throw new ArgumentException("URL phải thuộc domain docln.net.");
            }

            return uri.AbsoluteUri;
        }

        private bool TryParseHakoBookUrl(string url, out string bookId, out string slug, out string canonicalUrl)
        {
            bookId = null;
            slug = null;
            canonicalUrl = null;

            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            string normalizedUrl = NormalizeHakoUrl(url);
            if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            string[] segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length != 2 || !IsHakoBookSection(segments[0]))
            {
                return false;
            }

            Match match = Regex.Match(segments[1], @"^(?<id>\d+)-(?<slug>.+)$", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return false;
            }

            bookId = match.Groups["id"].Value;
            slug = match.Groups["slug"].Value.Trim().Trim('-');
            if (string.IsNullOrWhiteSpace(slug))
            {
                return false;
            }

            canonicalUrl = $"{uri.Scheme}://{uri.Host}/{segments[0]}/{bookId}-{slug}/";
            return true;
        }

        private bool TryParseHakoChapterUrl(string url, out string bookId, out string bookSlug, out string chapterId, out string chapterSlug, out string canonicalUrl)
        {
            bookId = null;
            bookSlug = null;
            chapterId = null;
            chapterSlug = null;
            canonicalUrl = null;

            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            string normalizedUrl = NormalizeHakoUrl(url);
            if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            string[] segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length != 3 || !IsHakoBookSection(segments[0]))
            {
                return false;
            }

            Match bookMatch = Regex.Match(segments[1], @"^(?<id>\d+)-(?<slug>.+)$", RegexOptions.IgnoreCase);
            Match chapterMatch = Regex.Match(segments[2], @"^(?<chapterId>c\d+)-(?<chapterSlug>.+)$", RegexOptions.IgnoreCase);
            if (!bookMatch.Success || !chapterMatch.Success)
            {
                return false;
            }

            bookId = bookMatch.Groups["id"].Value;
            bookSlug = bookMatch.Groups["slug"].Value.Trim().Trim('-');
            chapterId = chapterMatch.Groups["chapterId"].Value;
            chapterSlug = chapterMatch.Groups["chapterSlug"].Value.Trim().Trim('-');
            if (string.IsNullOrWhiteSpace(bookSlug) || string.IsNullOrWhiteSpace(chapterSlug))
            {
                return false;
            }

            canonicalUrl = $"{uri.Scheme}://{uri.Host}/{segments[0]}/{bookId}-{bookSlug}/{chapterId}-{chapterSlug}";
            return true;
        }

        private static bool IsHakoBookSection(string segment)
        {
            return segment.Equals("truyen", StringComparison.OrdinalIgnoreCase) ||
                   segment.Equals("ai-dich", StringComparison.OrdinalIgnoreCase) ||
                   segment.Equals("sang-tac", StringComparison.OrdinalIgnoreCase);
        }

        private string GetHakoTagPageUrl(string baseUrl, int page)
        {
            baseUrl = NormalizeHakoUrl(baseUrl);
            var builder = new UriBuilder(baseUrl);
            string query = (builder.Query ?? string.Empty).TrimStart('?');
            query = Regex.Replace(query, @"(^|&)page=\d+(&|$)", "$1", RegexOptions.IgnoreCase).Trim('&');

            if (page <= 1)
            {
                builder.Query = query;
                return builder.Uri.AbsoluteUri.TrimEnd('?');
            }

            builder.Query = string.IsNullOrWhiteSpace(query) ? $"page={page}" : $"{query}&page={page}";
            return builder.Uri.AbsoluteUri;
        }

        private static string NormalizeHakoPathForCompare(Uri uri)
        {
            string path = uri?.AbsolutePath ?? string.Empty;
            path = WebUtility.UrlDecode(path).Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                return "/";
            }

            path = Regex.Replace(path, @"/{2,}", "/");
            if (path.Length > 1)
            {
                path = path.TrimEnd('/');
            }

            return path.ToLowerInvariant();
        }

        internal async Task<bool> CheckIfHakoBlockedAsync(string testUrl)
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
                    return IsHakoChallengeHtml(html);
                }
            }
            catch
            {
                return true;
            }
        }

        private void ApplyHakoBrowserSession(CaptchaWindow captchaWin, string requestUrl)
        {
            var originalUri = new Uri(requestUrl);
            var resolvedUri = captchaWin.ResolvedUri ?? originalUri;

            foreach (Cookie cookie in captchaWin.ResolvedCookies.GetCookies(resolvedUri))
            {
                _cookieContainer.Add(resolvedUri, cookie);
            }

            if (!string.Equals(originalUri.Host, resolvedUri.Host, StringComparison.OrdinalIgnoreCase))
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
        }

        private async Task<string> FetchHakoHtmlViaBrowserAsync(string requestUrl, bool headlessAutomation)
        {
            while (_isCaptchaWindowActive)
            {
                await Task.Delay(250);
            }

            await _captchaSemaphore.WaitAsync();
            try
            {
                string resolvedHtml = null;
                bool solved = false;
                bool previousPaused = _isDownloadPaused;

                _isCaptchaWindowActive = true;
                if (!headlessAutomation)
                {
                    _isDownloadPaused = true;
                    HakoLog("Hako chặn request thường. Mở CaptchaWindow để lấy HTML thật.");
                }

                try
                {
                    await await Dispatcher.InvokeAsync(async () =>
                    {
                        var captchaWin = CreateCaptchaWindow(requestUrl, autoDeleteCookiesOnLoad: true, headlessAutomation: headlessAutomation);
                        captchaWin.Owner = this;

                        if (await captchaWin.ShowNonBlockingAsync())
                        {
                            ApplyHakoBrowserSession(captchaWin, requestUrl);
                            resolvedHtml = captchaWin.ResolvedHtml;
                            solved = true;
                        }
                    });
                }
                finally
                {
                    _isCaptchaWindowActive = false;
                    _isDownloadPaused = previousPaused;
                }

                if (!solved || string.IsNullOrWhiteSpace(resolvedHtml) || IsHakoChallengeHtml(resolvedHtml))
                {
                    return null;
                }

                _hakoCaptchaSessionReady = true;
                return resolvedHtml;
            }
            finally
            {
                _captchaSemaphore.Release();
            }
        }

        internal async Task<bool> SolveHakoCaptchaIfNeededAsync(string testUrl, bool forceChallengeCheck = false)
        {
            if (_hakoCaptchaSessionReady && !forceChallengeCheck)
            {
                return true;
            }

            if (forceChallengeCheck && !await CheckIfHakoBlockedAsync(testUrl))
            {
                _hakoCaptchaSessionReady = true;
                return true;
            }

            if (_isCaptchaWindowActive)
            {
                while (_isCaptchaWindowActive)
                {
                    await Task.Delay(500);
                }

                if (!await CheckIfHakoBlockedAsync(testUrl))
                {
                    _hakoCaptchaSessionReady = true;
                    return true;
                }
            }

            await _captchaSemaphore.WaitAsync();
            try
            {
                if (_hakoCaptchaSessionReady && !forceChallengeCheck)
                {
                    return true;
                }

                if (forceChallengeCheck && !await CheckIfHakoBlockedAsync(testUrl))
                {
                    _hakoCaptchaSessionReady = true;
                    return true;
                }

                _isCaptchaWindowActive = true;
                _isDownloadPaused = true;
                HakoLog("Phát hiện Cloudflare/Captcha. Mở CaptchaWindow để đồng bộ cookie phiên.");

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

                            if (!string.Equals(originalUri.Host, resolvedUri.Host, StringComparison.OrdinalIgnoreCase))
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

                if (solved)
                {
                    _isDownloadPaused = false;
                    _hakoCaptchaSessionReady = true;
                    HakoLog("Captcha/cookie đồng bộ xong. Tiếp tục.");
                    return true;
                }

                HakoLog("HttpClient không lấy được HTML Hako. Chuyển sang browser session.");
                return false;
            }
            finally
            {
                _captchaSemaphore.Release();
            }
        }

        private static bool IsHakoChallengeHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return false;
            }

            string plainText = WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ", RegexOptions.Singleline));
            plainText = Regex.Replace(plainText, @"\s+", " ").Trim();

            return html.IndexOf("cf-turnstile", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   html.IndexOf("cf-challenge", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   html.IndexOf("challenge-platform", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   plainText.IndexOf("Just a moment", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   plainText.IndexOf("Checking your browser", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   plainText.IndexOf("xác minh bạn là con người", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   plainText.IndexOf("verify you are human", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private async Task<string> TryFetchHakoHtmlByHttpClientAsync(string normalizedUrl, CancellationToken token)
        {
            string html = await FetchStringAsync(normalizedUrl, token);
            if (IsHakoChallengeHtml(html))
            {
                throw new HttpRequestException("Cloudflare challenge detected.");
            }

            return html;
        }

        private async Task<string> FetchHakoHtmlAsync(string url, CancellationToken token)
        {
            string normalizedUrl = NormalizeHakoUrl(url);
            Exception lastError = null;

            try
            {
                string httpClientHtml = await TryFetchHakoHtmlByHttpClientAsync(normalizedUrl, token);
                if (!string.IsNullOrWhiteSpace(httpClientHtml))
                {
                    return httpClientHtml;
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            try
            {
                string firecrawlHtml = await TryFetchHakoHtmlByFirecrawlAsync(normalizedUrl, token);
                if (!string.IsNullOrWhiteSpace(firecrawlHtml) && !IsHakoChallengeHtml(firecrawlHtml))
                {
                    return firecrawlHtml;
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            try
            {
                string browserHtml = await FetchHakoHtmlViaBrowserAsync(normalizedUrl, headlessAutomation: true);
                if (!string.IsNullOrWhiteSpace(browserHtml) && !IsHakoChallengeHtml(browserHtml))
                {
                    _hakoCaptchaSessionReady = true;
                    return browserHtml;
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            string visibleHtml = await FetchHakoHtmlViaBrowserAsync(normalizedUrl, headlessAutomation: false);
            if (string.IsNullOrWhiteSpace(visibleHtml) || IsHakoChallengeHtml(visibleHtml))
            {
                _hakoCaptchaSessionReady = false;
                if (lastError != null)
                {
                    throw new Exception("Không thể lấy HTML thật từ Hako sau khi thử lại bằng browser session. " + lastError.Message);
                }

                throw new Exception("Không thể lấy HTML thật từ Hako sau khi vượt captcha.");
            }

            _hakoCaptchaSessionReady = true;
            return visibleHtml;
        }

        private static bool IsHakoForbiddenChapterHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return false;
            }

            string text = WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ", RegexOptions.Singleline));
            text = Regex.Replace(text, @"\s+", " ").Trim();

            return text.IndexOf("403", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   (text.IndexOf("không phù hợp", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf("khong phu hop", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf("hãy chờ đợi người làm sửa lại", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf("hay cho doi nguoi lam sua lai", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf("forbidden", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsHakoTooManyRequestsHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return false;
            }

            string text = WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ", RegexOptions.Singleline));
            text = Regex.Replace(text, @"\s+", " ").Trim();

            return text.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   (text.IndexOf("too many requests", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf("quá nhiều yêu cầu", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf("qua nhieu yeu cau", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsSkippableHakoChapterError(Exception ex)
        {
            if (ex == null)
            {
                return false;
            }

            string message = ex.Message ?? string.Empty;
            return message.IndexOf("403", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("forbidden", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("không phù hợp", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("khong phu hop", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("khong trich xuat duoc noi dung text", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("non-text chapter", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsHakoRateLimitError(Exception ex)
        {
            if (ex == null)
            {
                return false;
            }

            string message = ex.Message ?? string.Empty;
            return message.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("too many requests", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("quá nhiều yêu cầu", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("qua nhieu yeu cau", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void EnsureHakoChapterHasText(string plainText)
        {
            if (!string.IsNullOrWhiteSpace(plainText))
            {
                return;
            }

            throw new InvalidOperationException("non-text chapter");
        }

        private async Task<string> TryFetchHakoChapterHtmlViaWebViewAsync(string chapterUrl, CancellationToken token, bool autoFocus)
        {
            token.ThrowIfCancellationRequested();
            HakoChapterCaptureResult capture = await HakoChapterCaptureWindow.CaptureAsync(
                this,
                chapterUrl,
                _isVietnameseUi,
                autoFocus,
                token,
                () => _lightNovelCopyCts?.Cancel());
            token.ThrowIfCancellationRequested();
            if (capture == null || string.IsNullOrWhiteSpace(capture.ContentHtml))
            {
                if (capture != null && capture.IsRateLimited)
                {
                    throw new InvalidOperationException("429 too many requests");
                }

                return null;
            }

            return BuildHakoChapterHtmlFromCapture(chapterUrl, capture);
        }

        private string BuildHakoChapterHtmlFromCapture(string chapterUrl, HakoChapterCaptureResult capture)
        {
            string bookLink = "#";
            string fallbackBookTitle = string.Empty;
            if (TryParseHakoChapterUrl(chapterUrl, out string bookId, out string bookSlug, out _, out _, out string canonicalChapterUrl) &&
                Uri.TryCreate(canonicalChapterUrl, UriKind.Absolute, out Uri chapterUri))
            {
                string section = chapterUri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "truyen";
                bookLink = $"{chapterUri.Scheme}://{chapterUri.Host}/{section}/{bookId}-{bookSlug}/";
                fallbackBookTitle = HumanizeHakoSlug(bookSlug);
            }

            string title = string.IsNullOrWhiteSpace(capture.ChapterTitle) ? string.Empty : WebUtility.HtmlEncode(capture.ChapterTitle.Trim());
            string bookTitle = string.IsNullOrWhiteSpace(capture.BookTitle)
                ? WebUtility.HtmlEncode(fallbackBookTitle)
                : WebUtility.HtmlEncode(capture.BookTitle.Trim());

            var sb = new StringBuilder();
            sb.Append("<html><body>");
            sb.Append("<div class=\"title-top\"><h4>");
            sb.Append(title);
            sb.Append("</h4></div>");
            sb.Append("<a href=\"");
            sb.Append(WebUtility.HtmlEncode(bookLink));
            sb.Append("\">");
            sb.Append(bookTitle);
            sb.Append("</a>");
            sb.Append("<div id=\"chapter-content\" class=\"long-text no-select text-justify\">");
            sb.Append(capture.ContentHtml ?? string.Empty);
            sb.Append("</div>");
            sb.Append("</body></html>");
            return sb.ToString();
        }

        private async void BtnHakoFetchInfo_Click(object sender, RoutedEventArgs e)
        {
            string rawUrl = txtHakoTagUrl.Text.Trim();
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                ShowLocalizedMessageBox(
                    "Please enter a Hako tag URL.",
                    "Vui lòng nhập URL tag của Hako.",
                    "Information",
                    "Thông báo",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            btnHakoFetchInfo.IsEnabled = false;
            progressBar.IsIndeterminate = true;
            lblStatus.Text = _isVietnameseUi ? "Đang phân tích trang Hako..." : "Analyzing Hako page...";

            try
            {
                string normalizedUrl = NormalizeHakoUrl(rawUrl);
                txtHakoTagUrl.Text = normalizedUrl;

                string html = await FetchHakoHtmlAsync(normalizedUrl, _downloadCts?.Token ?? CancellationToken.None);
                int totalPages = ExtractHakoMaxPage(html, normalizedUrl);
                txtHakoTotalPages.Text = totalPages.ToString(CultureInfo.InvariantCulture);
                txtHakoPageTo.Text = totalPages.ToString(CultureInfo.InvariantCulture);
                lblStatus.Text = _isVietnameseUi
                    ? $"Phân tích xong. Phát hiện {totalPages} trang. Bấm GET LINK để nạp truyện vào danh sách."
                    : $"Analysis done. Found {totalPages} pages. Click GET LINK to load books into the list.";
            }
            catch (Exception ex)
            {
                HakoLog("Lỗi khi phân tích: " + ex.Message);
                txtHakoTotalPages.Text = "1";
                txtHakoPageTo.Text = "1";
                lblStatus.Text = _isVietnameseUi ? "Phân tích thất bại." : "Analysis failed.";
            }
            finally
            {
                btnHakoFetchInfo.IsEnabled = true;
                progressBar.IsIndeterminate = false;
            }
        }

        private void TxtHakoTotalPages_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (txtHakoPageTo != null && txtHakoTotalPages != null)
            {
                txtHakoPageTo.Text = txtHakoTotalPages.Text;
            }
        }

        private async void BtnHakoScrape_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                btnHakoScrape.Content = _isVietnameseUi ? "ĐANG HỦY..." : "CANCELLING...";
                btnHakoScrape.IsEnabled = false;
                if (btnHakoCrawlMore != null)
                {
                    btnHakoCrawlMore.IsEnabled = false;
                }
                return;
            }

            await ScrapeHakoAsync(true);
        }

        private async void BtnHakoCrawlMore_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                btnHakoCrawlMore.Content = _isVietnameseUi ? "ĐANG HỦY..." : "CANCELLING...";
                btnHakoCrawlMore.IsEnabled = false;
                btnHakoScrape.IsEnabled = false;
                return;
            }

            await ScrapeHakoAsync(false);
        }

        private async Task ScrapeHakoAsync(bool clearExisting)
        {
            string rawUrl = txtHakoTagUrl.Text.Trim();
            if (!int.TryParse(txtHakoPageFrom.Text, out int pageFrom) || pageFrom < 1)
            {
                ShowLocalizedMessageBox(
                    "Start page is invalid.",
                    "Trang bắt đầu không hợp lệ.",
                    "Information",
                    "Thông báo",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(txtHakoPageTo.Text, out int pageTo) || pageTo < pageFrom)
            {
                ShowLocalizedMessageBox(
                    "End page is invalid.",
                    "Trang kết thúc không hợp lệ.",
                    "Information",
                    "Thông báo",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            btnHakoScrape.Content = _isVietnameseUi ? "DỪNG CÀO" : "STOP CRAWLER";
            btnHakoCrawlMore.Content = _isVietnameseUi ? "DỪNG CÀO" : "STOP CRAWLER";
            btnHakoFetchInfo.IsEnabled = false;
            progressBar.Value = 0;
            lblStatus.Text = _isVietnameseUi ? "Đang cào Hako..." : "Crawling Hako...";

            if (clearExisting)
            {
                ClearLightNovelQueue();
            }

            try
            {
                string baseUrl = NormalizeHakoUrl(rawUrl);
                txtHakoTagUrl.Text = baseUrl;
                int totalPages = pageTo - pageFrom + 1;

                for (int page = pageFrom; page <= pageTo; page++)
                {
                    token.ThrowIfCancellationRequested();

                    string pageUrl = GetHakoTagPageUrl(baseUrl, page);
                    string html = await FetchHakoHtmlAsync(pageUrl, token);
                    foreach (GalleryItem item in ParseHakoGalleryItemsFromHtml(html))
                    {
                        if (_lightNovelItems.Any(existing => string.Equals(existing.Link, item.Link, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        AddLightNovelQueueItem(item);
                    }

                    double progress = ((double)(page - pageFrom + 1) / totalPages) * 100;
                    progressBar.Value = progress;
                    lblStatus.Text = _isVietnameseUi
                        ? $"Đang quét trang {page}/{pageTo} ({progress:0}%)"
                        : $"Scanning page {page}/{pageTo} ({progress:0}%)";
                    RefreshLightNovelSummary();
                }

                RefreshLightNovelSummary();
                lblStatus.Text = _isVietnameseUi ? "Cào Hako hoàn tất." : "Hako crawl completed.";
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = _isVietnameseUi ? "Đã hủy cào Hako." : "Hako crawl cancelled.";
            }
            catch (Exception ex)
            {
                HakoLog("Lỗi khi cào: " + ex.Message);
                lblStatus.Text = _isVietnameseUi ? "Cào Hako thất bại." : "Hako crawl failed.";
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                btnHakoScrape.Content = _isVietnameseUi ? "LẤY LINK" : "GET LINK";
                btnHakoCrawlMore.Content = _isVietnameseUi ? "LẤY THÊM" : "GET MORE";
                btnHakoScrape.IsEnabled = true;
                btnHakoCrawlMore.IsEnabled = true;
                btnHakoFetchInfo.IsEnabled = true;
            }
        }
        private int ExtractHakoMaxPage(string html, string pageUrl)
        {
            int maxPage = 1;
            string absoluteBase = NormalizeHakoUrl(pageUrl);
            Uri baseUri = new Uri(absoluteBase);
            string targetPath = NormalizeHakoPathForCompare(baseUri);
            bool foundPageLink = false;

            foreach (Match match in Regex.Matches(html ?? string.Empty, @"href\s*=\s*[""'](?<href>[^""'#>]+)[""']", RegexOptions.IgnoreCase))
            {
                string href = WebUtility.HtmlDecode(match.Groups["href"].Value.Trim());
                if (string.IsNullOrWhiteSpace(href))
                {
                    continue;
                }

                if (href.StartsWith("//", StringComparison.Ordinal))
                {
                    href = baseUri.Scheme + ":" + href;
                }

                Uri candidateUri;
                if (!Uri.TryCreate(href, UriKind.Absolute, out candidateUri))
                {
                    candidateUri = new Uri(baseUri, href);
                }

                if (candidateUri.Host.IndexOf("docln.net", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                string candidatePath = NormalizeHakoPathForCompare(candidateUri);
                if (!string.Equals(candidatePath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Match queryMatch = Regex.Match(candidateUri.Query ?? string.Empty, @"(?:^|[?&])page=(?<page>\d+)", RegexOptions.IgnoreCase);
                if (!queryMatch.Success)
                {
                    continue;
                }

                foundPageLink = true;
                if (int.TryParse(queryMatch.Groups["page"].Value, out int page) && page > maxPage)
                {
                    maxPage = page;
                }
            }

            if (!foundPageLink)
            {
                foreach (Match match in Regex.Matches(html ?? string.Empty, @"(?:^|[?&])page=(?<page>\d+)", RegexOptions.IgnoreCase))
                {
                    if (int.TryParse(match.Groups["page"].Value, out int page) && page > maxPage)
                    {
                        maxPage = page;
                    }
                }
            }

            return maxPage;
        }

        private List<GalleryItem> ParseHakoGalleryItemsFromHtml(string html)
        {
            var results = new List<GalleryItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match match in Regex.Matches(
                html ?? string.Empty,
                @"<a[^>]+href\s*=\s*[""'](?<link>(?:https?:\/\/(?:ln\.hako\.vn|docln\.net|ln\.hako\.re))?\/(?:truyen|ai-dich|sang-tac)\/[^""'#?]+\/?)(?:[""'])[^>]*?(?:title\s*=\s*[""'](?<title>[^""']+)[""'])?[^>]*>(?<text>.*?)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                string link = NormalizeHakoUrl(match.Groups["link"].Value);
                if (!TryParseHakoBookUrl(link, out _, out _, out string canonicalBookUrl))
                {
                    continue;
                }

                link = canonicalBookUrl;
                if (!seen.Add(link))
                {
                    continue;
                }

                string title = WebUtility.HtmlDecode(match.Groups["title"].Value).Trim();
                if (string.IsNullOrWhiteSpace(title))
                {
                    title = StripHtmlToPlainText(match.Groups["text"].Value);
                }

                if (string.IsNullOrWhiteSpace(title))
                {
                    title = HumanizeHakoSlug(new Uri(link).Segments.LastOrDefault());
                }

                results.Add(new GalleryItem
                {
                    Link = link,
                    Name = FormatGalleryTitle(title),
                    LinkCount = string.Empty,
                    SourceDomain = HakoSiteFolder,
                    OriginalIndex = _lightNovelItems.Count + results.Count,
                    IsChecked = false
                });
            }

            return results;
        }

        private void BtnHakoPasteDirect_Click(object sender, RoutedEventArgs e)
        {
            var window = new DirectDownloadWindow(
                customTitle: "PASTE HAKO LINKS",
                customDescription: "Paste Hako book links or chapter links below. The system will normalize and import them automatically.",
                customExample: "Example:\nhttps://ln.hako.vn/truyen/23391-bi-kip-sinh-ton-tai-hoc-vien/\nhttps://ln.hako.vn/truyen/23391-bi-kip-sinh-ton-tai-hoc-vien/c227326-chuong-01")
            {
                Owner = this
            };

            window.OnImport = async links => await ImportHakoDirectLinksAsync(links);
            window.ShowDialog();
        }

        private async Task ImportHakoDirectLinksAsync(List<string> links)
        {
            btnHakoScrape.IsEnabled = false;
            btnHakoFetchInfo.IsEnabled = false;
            progressBar.Value = 0;
            progressBar.IsIndeterminate = false;

            int success = 0;
            int failed = 0;
            int total = links.Count;

            try
            {
                for (int i = 0; i < total; i++)
                {
                    string rawLink = links[i].Trim();
                    lblStatus.Text = $"[{i + 1}/{total}] Importing {rawLink}";

                    try
                    {
                        GalleryItem item = await BuildHakoDirectGalleryItemAsync(rawLink);
                        if (_lightNovelItems.Any(existing => string.Equals(existing.Link, item.Link, StringComparison.OrdinalIgnoreCase)))
                        {
                            HakoLog($"[Import] Bỏ qua link trùng: {item.Link}");
                            success++;
                            continue;
                        }

                        item.OriginalIndex = _lightNovelItems.Count;
                        AddLightNovelQueueItem(item);
                        success++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        HakoLog($"[Import] Lỗi với '{rawLink}': {ex.Message}");
                    }

                    progressBar.Value = ((double)(i + 1) / Math.Max(1, total)) * 100;
                    RefreshLightNovelSummary();
                }

                RefreshLightNovelSummary();
                lblStatus.Text = $"Import completed. Success: {success}, Failed: {failed}.";
            }
            finally
            {
                btnHakoScrape.IsEnabled = true;
                btnHakoFetchInfo.IsEnabled = true;
            }
        }

        private Task<GalleryItem> BuildHakoDirectGalleryItemAsync(string rawLink)
        {
            string normalizedLink = NormalizeHakoUrl(rawLink);

            if (TryParseHakoBookUrl(normalizedLink, out _, out string bookSlug, out string canonicalBookUrl))
            {
                string bookTitle = HumanizeHakoSlug(bookSlug);
                return Task.FromResult(new GalleryItem
                {
                    Link = canonicalBookUrl,
                    Name = FormatGalleryTitle(bookTitle),
                    SourceDomain = HakoSiteFolder,
                    IsChecked = true
                });
            }

            if (TryParseHakoChapterUrl(normalizedLink, out _, out string parsedBookSlug, out _, out string chapterSlug, out string canonicalChapterUrl))
            {
                string bookTitle = HumanizeHakoSlug(parsedBookSlug);
                string chapterTitle = NormalizeChapterLabel(HumanizeHakoSlug(chapterSlug));
                return Task.FromResult(new GalleryItem
                {
                    Link = canonicalChapterUrl,
                    Name = FormatGalleryTitle($"{bookTitle} - {chapterTitle}"),
                    LinkCount = chapterTitle,
                    SourceDomain = HakoSiteFolder,
                    IsChecked = true
                });
            }

            throw new Exception("Link Hako phải là link book hoặc link chapter hợp lệ.");
        }

        private async Task DownloadHakoNovelAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem = null, ChapterFilter chapterFilter = null)
        {
            if (TryParseHakoChapterUrl(item.Link, out _, out _, out _, out _, out _))
            {
                await DownloadSingleHakoChapterAsync(item, rootFolder, token, queueItem);
                return;
            }

            string resolvedRoot = GetConfiguredDownloadRoot(rootFolder, item);
            string safeTitle = GetCanonicalBookFolderName(item, item?.Name, "hako-book", 72);
            string targetFolder = Path.Combine(resolvedRoot, safeTitle);
            string tempFolder = BuildStableTempFolderPath(resolvedRoot, HakoSiteFolder, safeTitle, item.Link, item.Name);

            Directory.CreateDirectory(tempFolder);
            RegisterTempFolder(tempFolder);

            try
            {
                string html = await FetchHakoHtmlAsync(item.Link, token);
                string detectedTitle = ExtractHakoBookTitle(html);
                if (!string.IsNullOrWhiteSpace(detectedTitle))
                {
                    Dispatcher.Invoke(() =>
                    {
                        item.Name = FormatGalleryTitle(detectedTitle);
                    });
                }

                List<HakoChapterInfo> chapters = ExtractHakoChapterLinks(html, item.Link);
                if (chapters.Count == 0)
                {
                    item.HasNoChapters = true;
                    if (queueItem != null)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            queueItem.TotalChapters = 0;
                            queueItem.CompletedChapters = 0;
                            queueItem.CurrentProcess = "0/0 chapters";
                        });
                    }

                    WriteTempProgressLog(tempFolder, item, "Done", 0, 0, "0/0 chapters", "Không tìm thấy chapter nào.");
                    MoveTempFolderToTarget(tempFolder, targetFolder, "Hako");
                    return;
                }

                List<HakoChapterInfo> filteredChapters = ApplyHakoChapterFilter(chapters, chapterFilter);
                string processSiteFolder = GetProcessSiteFolder(item);
                List<string> pendingLinks = FilterPendingChapterLinksFromProcess(rootFolder, processSiteFolder, item, filteredChapters.Select(ch => ch.Link).ToList());
                if (pendingLinks != null && pendingLinks.Count > 0)
                {
                    filteredChapters = filteredChapters
                        .Where(ch => pendingLinks.Contains(ch.Link, StringComparer.OrdinalIgnoreCase))
                        .ToList();
                }

                int totalChapters = filteredChapters.Count;
                if (queueItem != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        queueItem.TotalChapters = totalChapters;
                        queueItem.CompletedChapters = 0;
                        queueItem.CurrentProcess = $"0/{totalChapters} chapters";
                    });
                }

                WriteTempProgressLog(tempFolder, item, "Downloading", 0, totalChapters, $"0/{totalChapters} chapters", "Bắt đầu copy text Hako");

                int completed = 0;
                foreach (HakoChapterInfo chapter in filteredChapters)
                {
                    while (_isDownloadPaused || item.IsPaused)
                    {
                        token.ThrowIfCancellationRequested();
                        if (item.IsStopped)
                        {
                            throw new OperationCanceledException();
                        }
                        await Task.Delay(200, token);
                    }

                    token.ThrowIfCancellationRequested();

                    string chapterLabel = NormalizeChapterLabel(chapter.Title);
                    string chapterFolder = GetHakoVolumeFolderPath(tempFolder, chapter.VolumeTitle, chapter.VolumeOrder);
                    string chapterFilePath = Path.Combine(chapterFolder, BuildHakoChapterFileName(chapterLabel, chapter.SequenceIndex));

                    Dispatcher.Invoke(() =>
                    {
                        item.DownloadingChapter = chapterLabel;
                        item.DownloadingPageProgress = $"{completed + 1}/{totalChapters}";
                        if (queueItem != null)
                        {
                            queueItem.DownloadingChapter = chapterLabel;
                            queueItem.DownloadingPageProgress = $"{completed + 1}/{totalChapters}";
                        }
                    });

                    try
                    {
                        string chapterHtml = await FetchHakoHtmlAsync(chapter.Link, token);
                        string plainText = BuildHakoChapterPlainText(chapterHtml);
                        EnsureHakoChapterHasText(plainText);
                        string markdown = BuildHakoChapterMarkdown(item, chapter, chapterHtml);
                        File.WriteAllText(chapterFilePath, markdown, new UTF8Encoding(true));
                        RecordLightNovelChapterSnapshot(item, chapterLabel, plainText, markdown, chapterFilePath);
                    }
                    catch (Exception ex) when (IsSkippableHakoChapterError(ex))
                    {
                        HakoLog($"Skip Hako chapter: {chapterLabel} - {chapter.Link} - {ex.Message}");
                        completed++;
                        MarkChapterProcessDone(rootFolder, processSiteFolder, item, chapter.Link);

                        if (queueItem != null)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                queueItem.CompletedChapters = completed;
                                queueItem.CurrentProcess = $"Skip {completed}/{totalChapters}";
                            });
                        }

                        WriteTempProgressLog(tempFolder, item, "Downloading", completed, totalChapters, $"{completed}/{totalChapters} chapters", $"Skip {chapterLabel}");
                        continue;
                    }

                    completed++;
                    MarkChapterProcessDone(rootFolder, processSiteFolder, item, chapter.Link);

                    if (queueItem != null)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            queueItem.CompletedChapters = completed;
                            queueItem.CurrentProcess = $"{completed}/{totalChapters} chapters";
                        });
                    }

                    WriteTempProgressLog(tempFolder, item, "Downloading", completed, totalChapters, $"{completed}/{totalChapters} chapters", $"Saved {chapterLabel}");
                }

                WriteTempProgressLog(tempFolder, item, "Done", totalChapters, totalChapters, $"{totalChapters}/{totalChapters} chapters", "Download completed");
                MoveTempFolderToTarget(tempFolder, targetFolder, "Hako");
            }
            finally
            {
                if (token.IsCancellationRequested && Directory.Exists(tempFolder))
                {
                    try
                    {
                        Directory.Delete(tempFolder, true);
                    }
                    catch (Exception ex)
                    {
                        HakoLog($"Không thể xóa temp Hako '{tempFolder}': {ex.Message}");
                    }
                }

                UnregisterTempFolder(tempFolder);
            }
        }

        private async Task DownloadSingleHakoChapterAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem)
        {
            string chapterUrl = NormalizeHakoUrl(item.Link);
            string chapterHtml = await FetchHakoHtmlAsync(chapterUrl, token);
            string bookTitle = ExtractHakoBookTitleFromChapterHtml(chapterHtml, chapterUrl);
            string chapterTitle = ExtractHakoTitleTopText(chapterHtml);

            if (string.IsNullOrWhiteSpace(bookTitle) && TryParseHakoChapterUrl(chapterUrl, out _, out string bookSlug, out _, out _, out _))
            {
                bookTitle = HumanizeHakoSlug(bookSlug);
            }

            if (string.IsNullOrWhiteSpace(bookTitle))
            {
                bookTitle = "Hako";
            }

            if (string.IsNullOrWhiteSpace(chapterTitle))
            {
                chapterTitle = string.IsNullOrWhiteSpace(item.LinkCount)
                    ? HumanizeHakoSlug(new Uri(chapterUrl).Segments.LastOrDefault())
                    : item.LinkCount;
            }

            string resolvedRoot = GetConfiguredDownloadRoot(rootFolder, item);
            string safeBookTitle = GetCanonicalBookFolderName(item, bookTitle, "hako-book", 72);
            string targetFolder = Path.Combine(resolvedRoot, safeBookTitle);
            string tempFolder = BuildStableTempFolderPath(resolvedRoot, HakoSiteFolder, safeBookTitle, chapterUrl, chapterTitle);

            Directory.CreateDirectory(tempFolder);
            RegisterTempFolder(tempFolder);

            try
            {
                string normalizedChapterTitle = NormalizeChapterLabel(chapterTitle);
                string chapterFilePath = Path.Combine(tempFolder, BuildHakoChapterFileName(normalizedChapterTitle, 1));
                var chapterInfo = new HakoChapterInfo
                {
                    BookTitle = bookTitle,
                    Title = chapterTitle,
                    Link = chapterUrl,
                    ChapterNumber = TryExtractHakoChapterNumber(chapterTitle, chapterUrl),
                    SequenceIndex = 1
                };

                if (queueItem != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        queueItem.TotalChapters = 1;
                        queueItem.CompletedChapters = 0;
                        queueItem.DownloadingChapter = normalizedChapterTitle;
                        queueItem.DownloadingPageProgress = "1/1";
                        queueItem.CurrentProcess = "0/1 chapters";
                    });
                }

                item.Name = FormatGalleryTitle(bookTitle);
                string plainText = BuildHakoChapterPlainText(chapterHtml);
                EnsureHakoChapterHasText(plainText);
                string markdown = BuildHakoChapterMarkdown(item, chapterInfo, chapterHtml);
                File.WriteAllText(chapterFilePath, markdown, new UTF8Encoding(true));
                RecordLightNovelChapterSnapshot(item, normalizedChapterTitle, plainText, markdown, chapterFilePath);

                string processSiteFolder = GetProcessSiteFolder(item);
                InitializeChapterProcess(rootFolder, processSiteFolder, item, new List<string> { chapterUrl });
                MarkChapterProcessDone(rootFolder, processSiteFolder, item, chapterUrl);

                if (queueItem != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        queueItem.CompletedChapters = 1;
                        queueItem.CurrentProcess = "1/1 chapters";
                    });
                }

                WriteTempProgressLog(tempFolder, item, "Done", 1, 1, "1/1 chapters", $"Saved {normalizedChapterTitle}");
                MoveTempFolderToTarget(tempFolder, targetFolder, "Hako");
            }
            finally
            {
                if (token.IsCancellationRequested && Directory.Exists(tempFolder))
                {
                    try
                    {
                        Directory.Delete(tempFolder, true);
                    }
                    catch (Exception ex)
                    {
                        HakoLog($"Không thể xóa temp Hako '{tempFolder}': {ex.Message}");
                    }
                }

                UnregisterTempFolder(tempFolder);
            }
        }

        private List<HakoChapterInfo> ApplyHakoChapterFilter(List<HakoChapterInfo> chapters, ChapterFilter chapterFilter)
        {
            if (chapterFilter == null)
            {
                return chapters;
            }

            var filtered = new List<HakoChapterInfo>();
            foreach (HakoChapterInfo chapter in chapters)
            {
                if (!chapter.ChapterNumber.HasValue || chapterFilter.IsMatch(chapter.ChapterNumber.Value))
                {
                    filtered.Add(chapter);
                }
            }

            return filtered;
        }

        private string ExtractHakoBookTitle(string html)
        {
            string raw = ExtractFirstGroup(html, @"<h1[^>]*>(?<text>.*?)</h1>", "text");
            if (string.IsNullOrWhiteSpace(raw))
            {
                raw = ExtractFirstGroup(html, @"<title[^>]*>(?<text>.*?)</title>", "text");
            }

            raw = StripHtmlToPlainText(raw);
            raw = Regex.Replace(raw, @"\s*-\s*(Cổng Light Novel|Đọc Light Novel).*$", string.Empty, RegexOptions.IgnoreCase).Trim();
            return raw;
        }

        private string ExtractHakoBookTitleFromChapterHtml(string html, string chapterUrl)
        {
            string[] patterns =
            {
                @"<div[^>]*class\s*=\s*[""'][^""']*rd_sidebar-name[^""']*[""'][^>]*>.*?<h5[^>]*>\s*<a[^>]*>(?<text>.*?)</a>",
                @"<span[^>]*class\s*=\s*[""'][^""']*series-name[^""']*[""'][^>]*>(?<text>.*?)</span>",
                @"<a[^>]+href\s*=\s*[""'](?<link>/(?:truyen|ai-dich|sang-tac)/[^""'#?]+/?)[^""']*[""'][^>]*>(?<text>.*?)</a>"
            };

            foreach (string pattern in patterns)
            {
                Match match = Regex.Match(
                    html ?? string.Empty,
                    pattern,
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (!match.Success)
                {
                    continue;
                }

                string text = StripHtmlToPlainText(match.Groups["text"].Value);
                if (IsInvalidHakoBookTitle(text))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.Trim();
                }
            }

            if (TryParseHakoChapterUrl(chapterUrl, out _, out string bookSlug, out _, out _, out _))
            {
                return HumanizeHakoSlug(bookSlug);
            }

            return string.Empty;
        }

        private static bool IsInvalidHakoBookTitle(string text)
        {
            string normalized = (text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return true;
            }

            return Regex.IsMatch(
                normalized,
                @"^Ảnh tạm thời bị tắt\.?$|^Anh tam thoi bi tat\.?$|^Temporary image disabled\.?$",
                RegexOptions.IgnoreCase);
        }

        private List<HakoChapterInfo> ExtractHakoChapterLinks(string html, string bookUrl)
        {
            var chapters = new List<HakoChapterInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Uri baseUri = new Uri(NormalizeHakoUrl(bookUrl));
            string basePath = baseUri.AbsolutePath.TrimEnd('/');
            string bookTitle = ExtractHakoBookTitle(html);
            int sequenceIndex = 0;

            MatchCollection sectionMatches = Regex.Matches(
                html ?? string.Empty,
                @"<section[^>]*class\s*=\s*[""'][^""']*\bvolume-list\b[^""']*[""'][^>]*>(?<body>.*?)</section>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (sectionMatches.Count > 0)
            {
                int volumeOrder = 0;
                foreach (Match sectionMatch in sectionMatches)
                {
                    volumeOrder++;
                    string sectionHtml = sectionMatch.Groups["body"].Value;
                    string volumeTitle = StripHtmlToPlainText(ExtractFirstGroup(
                        sectionHtml,
                        @"<span[^>]*class\s*=\s*[""'][^""']*\bsect-title\b[^""']*[""'][^>]*>(?<title>.*?)</span>",
                        "title"));
                    volumeTitle = NormalizeHakoVolumeTitle(volumeTitle, volumeOrder);

                    foreach (HakoChapterInfo chapter in ExtractHakoChapterLinksFromBlock(
                        sectionHtml,
                        bookTitle,
                        basePath,
                        seen,
                        volumeTitle,
                        volumeOrder,
                        ref sequenceIndex))
                    {
                        chapters.Add(chapter);
                    }
                }

                if (chapters.Count > 0)
                {
                    return chapters;
                }
            }

            foreach (HakoChapterInfo chapter in ExtractHakoChapterLinksFromBlock(
                html ?? string.Empty,
                bookTitle,
                basePath,
                seen,
                null,
                0,
                ref sequenceIndex))
            {
                chapters.Add(chapter);
            }

            return chapters;
        }

        private List<HakoChapterInfo> ExtractHakoChapterLinksFromBlock(
            string htmlBlock,
            string bookTitle,
            string basePath,
            HashSet<string> seen,
            string volumeTitle,
            int volumeOrder,
            ref int sequenceIndex)
        {
            var chapters = new List<HakoChapterInfo>();
            if (string.IsNullOrWhiteSpace(htmlBlock))
            {
                return chapters;
            }

            foreach (Match match in Regex.Matches(
                htmlBlock,
                @"<a[^>]+href\s*=\s*[""'](?<link>(?:https?:\/\/(?:ln\.hako\.vn|docln\.net|ln\.hako\.re))?\/(?:truyen|ai-dich|sang-tac)\/[^""'#?]+\/c\d+[^""'#?]*)[""'][^>]*(?:title\s*=\s*[""'](?<titleAttr>[^""']*)[""'])?[^>]*>(?<text>.*?)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                string link = NormalizeHakoUrl(match.Groups["link"].Value);
                if (!TryParseHakoChapterUrl(link, out _, out _, out _, out _, out string canonicalChapterUrl))
                {
                    continue;
                }

                link = canonicalChapterUrl;
                Uri chapterUri = new Uri(link);
                if (!chapterUri.AbsolutePath.StartsWith(basePath + "/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (seen != null && !seen.Add(link))
                {
                    continue;
                }

                string title = StripHtmlToPlainText(match.Groups["titleAttr"].Value);
                if (string.IsNullOrWhiteSpace(title))
                {
                    title = StripHtmlToPlainText(match.Groups["text"].Value);
                }

                if (string.IsNullOrWhiteSpace(title))
                {
                    title = HumanizeHakoSlug(chapterUri.Segments.LastOrDefault());
                }

                chapters.Add(new HakoChapterInfo
                {
                    BookTitle = bookTitle,
                    Link = link,
                    Title = title,
                    ChapterNumber = TryExtractHakoChapterNumber(title, link),
                    VolumeTitle = volumeTitle,
                    VolumeOrder = volumeOrder,
                    SequenceIndex = ++sequenceIndex
                });
            }

            return chapters;
        }

        private string BuildHakoChapterMarkdown(GalleryItem item, HakoChapterInfo chapter, string html)
        {
            string titleTopText = ExtractHakoTitleTopText(html);
            string chapterTitle = string.IsNullOrWhiteSpace(titleTopText)
                ? NormalizeChapterLabel(chapter.Title)
                : NormalizeChapterLabel(titleTopText);
            string contentHtml = ExtractHakoChapterContentHtml(html);
            string contentMarkdown = ConvertHakoContentHtmlToMarkdown(contentHtml);
            if (string.IsNullOrWhiteSpace(contentMarkdown))
            {
                throw new Exception("Không trích xuất được nội dung text trong chapter-content.");
            }

            string bookTitle = item?.Name;
            if (string.IsNullOrWhiteSpace(bookTitle))
            {
                bookTitle = chapter?.BookTitle;
            }

            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(bookTitle))
            {
                sb.AppendLine("# " + bookTitle.Trim());
                sb.AppendLine();
            }

            sb.AppendLine("## " + chapterTitle);
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(chapter?.VolumeTitle))
            {
                sb.AppendLine("> Volume: " + chapter.VolumeTitle.Trim());
                sb.AppendLine();
            }
            if (!string.IsNullOrWhiteSpace(chapter?.Link))
            {
                sb.AppendLine("> Source: " + chapter.Link.Trim());
                sb.AppendLine();
            }
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine(contentMarkdown.Trim());
            sb.AppendLine();
            return sb.ToString();
        }

        private string BuildHakoChapterPlainText(string html)
        {
            string contentHtml = ExtractHakoChapterContentHtml(html);
            string contentMarkdown = ConvertHakoContentHtmlToMarkdown(contentHtml);
            return contentMarkdown?.Trim() ?? string.Empty;
        }

        private string ExtractHakoChapterContentHtml(string html)
        {
            string contentHtml = ExtractHtmlElementById(html, "chapter-content");
            if (!string.IsNullOrWhiteSpace(contentHtml))
            {
                string protectedContentHtml = TryDecodeProtectedHakoChapterContent(contentHtml);
                if (!string.IsNullOrWhiteSpace(protectedContentHtml))
                {
                    return protectedContentHtml;
                }

                return contentHtml;
            }

            contentHtml = ExtractHtmlElementByClass(html, "chapter-content");
            if (!string.IsNullOrWhiteSpace(contentHtml))
            {
                string protectedContentHtml = TryDecodeProtectedHakoChapterContent(contentHtml);
                if (!string.IsNullOrWhiteSpace(protectedContentHtml))
                {
                    return protectedContentHtml;
                }

                return contentHtml;
            }

            contentHtml = ExtractHtmlElementByClass(html, "long-text");
            if (!string.IsNullOrWhiteSpace(contentHtml))
            {
                string protectedContentHtml = TryDecodeProtectedHakoChapterContent(contentHtml);
                if (!string.IsNullOrWhiteSpace(protectedContentHtml))
                {
                    return protectedContentHtml;
                }
            }

            return contentHtml;
        }

        private string ExtractHakoTitleTopText(string html)
        {
            string titleTopHtml = ExtractFirstGroup(html, @"<div[^>]*class\s*=\s*[""'][^""']*title-top[^""']*[""'][^>]*>(?<text>.*?)</div>", "text");
            if (string.IsNullOrWhiteSpace(titleTopHtml))
            {
                return string.Empty;
            }

            string h4Text = StripHtmlToPlainText(ExtractFirstGroup(titleTopHtml, @"<h4[^>]*>(?<text>.*?)</h4>", "text"));
            if (!string.IsNullOrWhiteSpace(h4Text))
            {
                return h4Text.Trim();
            }

            string h2Text = StripHtmlToPlainText(ExtractFirstGroup(titleTopHtml, @"<h2[^>]*>(?<text>.*?)</h2>", "text"));
            if (!string.IsNullOrWhiteSpace(h2Text))
            {
                return h2Text.Trim();
            }

            string fullText = StripHtmlToPlainText(titleTopHtml);
            return string.IsNullOrWhiteSpace(fullText) ? string.Empty : fullText.Trim();
        }

        private string ConvertHakoContentHtmlToMarkdown(string contentHtml)
        {
            if (string.IsNullOrWhiteSpace(contentHtml))
            {
                return string.Empty;
            }

            string text = Regex.Replace(contentHtml, @"<(script|style|iframe|svg)[^>]*>.*?</\1>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, @"<img[^>]*>", string.Empty, RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<a[^>]*>(?:\s|&nbsp;|<img[^>]*>)*</a>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
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
                    !Regex.IsMatch(line, @"^(https?://|/(?:truyen|ai-dich|sang-tac)/\d+)", RegexOptions.IgnoreCase) &&
                    !Regex.IsMatch(line, @"^Ảnh tạm thời bị tắt\.?$", RegexOptions.IgnoreCase))
                .ToList();

            var dedupedLines = new List<string>();
            foreach (string line in lines)
            {
                if (dedupedLines.Count > 0 && string.Equals(dedupedLines[dedupedLines.Count - 1], line, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                dedupedLines.Add(line);
            }

            return string.Join("\n\n", dedupedLines);
        }

        private string TryDecodeProtectedHakoChapterContent(string contentHtml)
        {
            if (string.IsNullOrWhiteSpace(contentHtml) ||
                contentHtml.IndexOf("chapter-c-protected", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return string.Empty;
            }

            string protectedTag = ExtractFirstGroup(
                contentHtml,
                @"(?<tag><div[^>]*id\s*=\s*[""']chapter-c-protected[""'][^>]*>)",
                "tag");
            if (string.IsNullOrWhiteSpace(protectedTag))
            {
                return string.Empty;
            }

            string scheme = ExtractHtmlAttributeValue(protectedTag, "data-s");
            string key = ExtractHtmlAttributeValue(protectedTag, "data-k");
            string chunksJson = ExtractHtmlAttributeValue(protectedTag, "data-c");
            if (string.IsNullOrWhiteSpace(chunksJson))
            {
                return string.Empty;
            }

            List<string> chunks = ParseProtectedHakoChunkList(chunksJson);
            if (chunks.Count == 0)
            {
                return string.Empty;
            }

            chunks.Sort((left, right) => ExtractProtectedHakoChunkOrder(left).CompareTo(ExtractProtectedHakoChunkOrder(right)));

            var sb = new StringBuilder();
            foreach (string chunk in chunks)
            {
                if (string.IsNullOrWhiteSpace(chunk) || chunk.Length <= 4)
                {
                    continue;
                }

                string payload = chunk.Substring(4);
                string decodedChunk = DecodeProtectedHakoChunk(payload, scheme, key);
                if (!string.IsNullOrWhiteSpace(decodedChunk))
                {
                    sb.Append(decodedChunk);
                }
            }

            return sb.ToString().Trim();
        }

        private static string ExtractHtmlAttributeValue(string tagHtml, string attributeName)
        {
            if (string.IsNullOrWhiteSpace(tagHtml) || string.IsNullOrWhiteSpace(attributeName))
            {
                return string.Empty;
            }

            string value = ExtractFirstGroup(
                tagHtml,
                $@"\b{Regex.Escape(attributeName)}\s*=\s*[""'](?<value>[^""']*)[""']",
                "value");
            return string.IsNullOrWhiteSpace(value) ? string.Empty : WebUtility.HtmlDecode(value);
        }

        private static List<string> ParseProtectedHakoChunkList(string chunksJson)
        {
            var chunks = new List<string>();
            if (string.IsNullOrWhiteSpace(chunksJson))
            {
                return chunks;
            }

            string normalized = WebUtility.HtmlDecode(chunksJson).Replace(@"\/", "/");
            MatchCollection matches = Regex.Matches(normalized, @"""(?<value>(?:\\.|[^""\\])*)""");
            foreach (Match match in matches)
            {
                string value = match.Groups["value"].Value;
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                chunks.Add(Regex.Unescape(value));
            }

            return chunks;
        }

        private static int ExtractProtectedHakoChunkOrder(string chunk)
        {
            if (string.IsNullOrWhiteSpace(chunk) || chunk.Length < 4)
            {
                return int.MaxValue;
            }

            return int.TryParse(chunk.Substring(0, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out int order)
                ? order
                : int.MaxValue;
        }

        private static string DecodeProtectedHakoChunk(string payload, string scheme, string key)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return string.Empty;
            }

            try
            {
                string normalizedPayload = string.Equals(scheme, "base64_reverse", StringComparison.OrdinalIgnoreCase)
                    ? new string(payload.Reverse().ToArray())
                    : payload;
                byte[] bytes = Convert.FromBase64String(normalizedPayload);

                if (string.Equals(scheme, "xor_shuffle", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(key))
                {
                    byte[] decodedBytes = new byte[bytes.Length];
                    for (int i = 0; i < bytes.Length; i++)
                    {
                        decodedBytes[i] = (byte)(bytes[i] ^ (byte)key[i % key.Length]);
                    }

                    bytes = decodedBytes;
                }

                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ExtractHtmlElementById(string html, string id)
        {
            if (string.IsNullOrWhiteSpace(html) || string.IsNullOrWhiteSpace(id))
            {
                return string.Empty;
            }

            Match startMatch = Regex.Match(
                html,
                $@"<div[^>]*id\s*=\s*[""']{Regex.Escape(id)}[""'][^>]*>",
                RegexOptions.IgnoreCase);

            if (!startMatch.Success)
            {
                return string.Empty;
            }

            return ExtractDivInnerHtmlFromMatch(html, startMatch);
        }

        private static string ExtractHtmlElementByClass(string html, string className)
        {
            if (string.IsNullOrWhiteSpace(html) || string.IsNullOrWhiteSpace(className))
            {
                return string.Empty;
            }

            Match startMatch = Regex.Match(
                html,
                $@"<div[^>]*class\s*=\s*[""'][^""']*{Regex.Escape(className)}[^""']*[""'][^>]*>",
                RegexOptions.IgnoreCase);

            if (!startMatch.Success)
            {
                return string.Empty;
            }

            return ExtractDivInnerHtmlFromMatch(html, startMatch);
        }

        private static string ExtractDivInnerHtmlFromMatch(string html, Match startMatch)
        {
            int startIndex = startMatch.Index + startMatch.Length;
            int depth = 1;
            int scanIndex = startIndex;
            var tokenRegex = new Regex(@"<div\b|</div>", RegexOptions.IgnoreCase);

            while (depth > 0)
            {
                Match tokenMatch = tokenRegex.Match(html, scanIndex);
                if (!tokenMatch.Success)
                {
                    return html.Substring(startIndex);
                }

                if (tokenMatch.Value.StartsWith("</div", StringComparison.OrdinalIgnoreCase))
                {
                    depth--;
                }
                else
                {
                    depth++;
                }

                scanIndex = tokenMatch.Index + tokenMatch.Length;
                if (depth == 0)
                {
                    return html.Substring(startIndex, tokenMatch.Index - startIndex);
                }
            }

            return string.Empty;
        }

        private static string ExtractFirstGroup(string html, string pattern, string groupName)
        {
            Match match = Regex.Match(html ?? string.Empty, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return match.Success ? match.Groups[groupName].Value : string.Empty;
        }

        private static string StripHtmlToPlainText(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            string text = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<[^>]+>", string.Empty, RegexOptions.Singleline);
            text = WebUtility.HtmlDecode(text);
            text = text.Replace('\u00a0', ' ');
            text = Regex.Replace(text, @"\s+", " ").Trim();
            return text;
        }

        private static string HumanizeHakoSlug(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug))
            {
                return "Unknown Hako Item";
            }

            slug = slug.Trim('/').Trim();
            slug = Regex.Replace(slug, @"^[a-z]\d+-", string.Empty, RegexOptions.IgnoreCase);
            slug = Regex.Replace(slug, @"^\d+-", string.Empty, RegexOptions.IgnoreCase);
            slug = WebUtility.UrlDecode(slug).Replace("-", " ");
            return Regex.Replace(slug, @"\s+", " ").Trim();
        }

        private static string NormalizeHakoVolumeTitle(string volumeTitle, int volumeOrder)
        {
            string cleaned = (volumeTitle ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                return cleaned;
            }

            return volumeOrder > 0
                ? $"volume {volumeOrder:00}"
                : string.Empty;
        }

        private string GetHakoVolumeFolderPath(string rootFolder, string volumeTitle, int volumeOrder)
        {
            string normalizedVolume = NormalizeHakoVolumeTitle(volumeTitle, volumeOrder);
            if (string.IsNullOrWhiteSpace(normalizedVolume))
            {
                Directory.CreateDirectory(rootFolder);
                return rootFolder;
            }

            string safeVolumeName = GetSafeVolumePathName(normalizedVolume, volumeOrder, 40);
            if (string.IsNullOrWhiteSpace(safeVolumeName))
            {
                Directory.CreateDirectory(rootFolder);
                return rootFolder;
            }

            string prefix = volumeOrder > 0 ? volumeOrder.ToString("00", CultureInfo.InvariantCulture) + " - " : string.Empty;
            string volumeFolder = Path.Combine(rootFolder, prefix + safeVolumeName);
            Directory.CreateDirectory(volumeFolder);
            return volumeFolder;
        }

        private string BuildHakoChapterFileName(string chapterTitle, int sequenceIndex)
        {
            string safeChapterName = GetSafeChapterPathName(chapterTitle, 92);
            string prefix = sequenceIndex > 0
                ? sequenceIndex.ToString("0000", CultureInfo.InvariantCulture) + " - "
                : string.Empty;
            return prefix + safeChapterName + ".md";
        }

        private static double? TryExtractHakoChapterNumber(string title, string link)
        {
            string raw = title ?? string.Empty;
            Match titleMatch = Regex.Match(raw, @"(?:chap(?:ter)?|chương|chuong)\s*(?<num>\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (titleMatch.Success &&
                double.TryParse(titleMatch.Groups["num"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double titleValue))
            {
                return titleValue;
            }

            Match linkMatch = Regex.Match(link ?? string.Empty, @"/c\d+-(?:[^/?#]*?)(?<num>\d+(?:\.\d+)?)(?:[^/?#]*)?$", RegexOptions.IgnoreCase);
            if (linkMatch.Success &&
                double.TryParse(linkMatch.Groups["num"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double linkValue))
            {
                return linkValue;
            }

            return null;
        }
    }
}

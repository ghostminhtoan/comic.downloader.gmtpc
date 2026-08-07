using System;
using System.Collections.Concurrent;
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
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using WinForms = System.Windows.Forms;

#pragma warning disable 4014
namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private static readonly ConcurrentDictionary<string, (DateTime Timestamp, List<ReaderChapterItem> Chapters)> _nettruyenScanCache 
            = new ConcurrentDictionary<string, (DateTime Timestamp, List<ReaderChapterItem> Chapters)>(StringComparer.OrdinalIgnoreCase);

        private static readonly Regex _nettruyenJsonObjectRegex = new Regex(@"\{(?<obj>[^{}]*""chapter_num""[^{}]*)\}", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex _nettruyenChapterNumRegex = new Regex(@"""chapter_num""\s*:\s*""?(?<num>\d+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _nettruyenChapterNameRegex = new Regex(@"""chapter_name""\s*:\s*""(?<name>(?:\\.|[^""\\])*)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private string _lastCaptchaResolvedHtml = null;
        private RichTextBox _nettruyenLogOverride = null;
        private int _nettruyenWatchMoreWebViewActiveCount;

        private RichTextBox GetNettruyenLogTarget()
        {
            return _nettruyenLogOverride ?? txtNettruyenLog;
        }

        private void NettruyenLog(string message)
        {
            var logTarget = GetNettruyenLogTarget();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                string logLine = $"[{DateTime.Now:HH:mm:ss}] {message}\r\n";
                bool isError = IsErrorMessage(message);
                AppendLogLine(logTarget, logLine, isError);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private bool IsNettruyenTechUrl(string url)
        {
            return !string.IsNullOrWhiteSpace(url) &&
                   url.IndexOf("nettruyen.tech", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string GetNettruyenSiteFolder(GalleryItem item)
        {
            string siteFolder = GetDownloadSiteKey(item);
            if (string.Equals(siteFolder, "nettruyen.tech", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(siteFolder, "nettruyenviet10.com", StringComparison.OrdinalIgnoreCase))
            {
                return siteFolder;
            }

            return "nettruyen.tech";
        }

        private string NormalizeNettruyenTechRedirectInput()
        {
            string redirectValue = txtNettruyenTechRedirectDomain != null
                ? txtNettruyenTechRedirectDomain.Text
                : string.Empty;

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

        private string ApplyNettruyenTechRedirectDomain(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return url;
            }

            string redirectBaseUrl = NormalizeNettruyenTechRedirectInput();
            if (string.IsNullOrWhiteSpace(redirectBaseUrl))
            {
                return url;
            }

            string candidate = url.Trim();
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

        private async Task EnsureNettruyenTechRedirectDomainAsync()
        {
            if (!string.IsNullOrWhiteSpace(NormalizeNettruyenTechRedirectInput()))
            {
                return;
            }

            if (Interlocked.Exchange(ref _nettruyenTechRedirectProbeStarted, 1) != 0)
            {
                return;
            }

            try
            {
                await RefreshNettruyenTechRedirectDomainAsync();
            }
            finally
            {
                Interlocked.Exchange(ref _nettruyenTechRedirectProbeStarted, 0);
            }
        }

        private async Task RefreshNettruyenTechRedirectDomainAsync()
        {
            const string probeUrl = "https://nettruyen.tech/the-loai/truyen-scan";

            try
            {
                Uri finalUri = await ProbeNettruyenTechRedirectUriAsync(probeUrl);
                if (finalUri == null)
                {
                    return;
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    if (txtNettruyenTechRedirectDomain == null)
                    {
                        return;
                    }

                    string resolvedBaseUrl = string.Equals(finalUri.Host, "nettruyen.tech", StringComparison.OrdinalIgnoreCase)
                        ? string.Empty
                        : $"{finalUri.Scheme}://{finalUri.Host}";

                    string currentValue = NormalizeNettruyenTechRedirectInput();
                    if (string.Equals(currentValue, resolvedBaseUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    txtNettruyenTechRedirectDomain.Text = resolvedBaseUrl;
                    RequestGalleryListAutosave(0);
                });
            }
            catch (Exception ex)
            {
                NettruyenLog($"[nettruyen.tech] Không kiểm tra được redirect domain: {ex.Message}");
            }
        }

        private async Task<Uri> ProbeNettruyenTechRedirectUriAsync(string url)
        {
            var probeTask = new TaskCompletionSource<Uri>();
            var probeThread = new Thread(() =>
            {
                WinForms.Form probeForm = null;
                WebView2 probeWebView = null;
                WinForms.Timer timeoutTimer = null;
                Uri latestUri = null;

                void UpdateLatestUri(string candidate)
                {
                    if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri parsedUri))
                    {
                        latestUri = parsedUri;
                    }
                }

                void CompleteProbe(Uri result = null)
                {
                    if (!probeTask.TrySetResult(result ?? latestUri ?? new Uri(url)))
                    {
                        return;
                    }

                    try
                    {
                        timeoutTimer?.Stop();
                        timeoutTimer?.Dispose();
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (probeForm != null && !probeForm.IsDisposed)
                        {
                            probeForm.BeginInvoke(new Action(() => probeForm.Close()));
                        }
                    }
                    catch
                    {
                    }
                }

                try
                {
                    probeForm = new WinForms.Form
                    {
                        ShowInTaskbar = false,
                        FormBorderStyle = WinForms.FormBorderStyle.None,
                        StartPosition = WinForms.FormStartPosition.Manual,
                        Location = new System.Drawing.Point(-32000, -32000),
                        Size = new System.Drawing.Size(1, 1),
                        Opacity = 0,
                        WindowState = WinForms.FormWindowState.Minimized
                    };

                    probeWebView = new WebView2
                    {
                        Dock = WinForms.DockStyle.Fill
                    };
                    probeForm.Controls.Add(probeWebView);

                    probeWebView.NavigationStarting += (sender, args) => UpdateLatestUri(args.Uri);
                    probeWebView.NavigationCompleted += (sender, args) =>
                    {
                        UpdateLatestUri(probeWebView.Source?.ToString() ?? probeWebView.CoreWebView2?.Source);
                        if (latestUri != null &&
                            !string.Equals(latestUri.Host, "nettruyen.tech", StringComparison.OrdinalIgnoreCase))
                        {
                            CompleteProbe(latestUri);
                        }
                    };

                    probeForm.Load += async (sender, args) =>
                    {
                        try
                        {
                            string browserArgs = "--disable-extensions --disable-popup-blocking --disable-background-networking --disable-sync --no-first-run --disable-features=msSmartScreenProtection,RendererCodeIntegrity --blink-settings=imagesEnabled=false";
                            string userDataFolder = Path.Combine(PortablePaths.WebView2RuntimeRoot, "nettruyen-startup-probe");
                            Directory.CreateDirectory(userDataFolder);

                            var env = await CoreWebView2Environment.CreateAsync(
                                null,
                                userDataFolder,
                                new CoreWebView2EnvironmentOptions(browserArgs));
                            await probeWebView.EnsureCoreWebView2Async(env);

                            probeWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                            probeWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                            probeWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                            probeWebView.CoreWebView2.NewWindowRequested += (popupSender, popupArgs) => { popupArgs.Handled = true; };
                            await probeWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
const textOnlyStyle = document.createElement('style');
textOnlyStyle.textContent = 'img, picture, video, audio, canvas, [style*=""background-image""] { display: none !important; visibility: hidden !important; }';
(document.head || document.documentElement).appendChild(textOnlyStyle);
window.open = () => null;
document.addEventListener('click', function (event) {
  const anchor = event.target && event.target.closest ? event.target.closest('a[target=""_blank""]') : null;
  if (anchor) {
    anchor.removeAttribute('target');
  }
}, true);");

                            UpdateLatestUri(url);
                            timeoutTimer = new WinForms.Timer { Interval = 15000 };
                            timeoutTimer.Tick += (timeoutSender, timeoutArgs) => CompleteProbe();
                            timeoutTimer.Start();
                            probeWebView.Source = new Uri(url);
                        }
                        catch (Exception ex)
                        {
                            probeTask.TrySetException(ex);
                            probeForm.Close();
                        }
                    };

                    probeForm.FormClosed += (sender, args) =>
                    {
                        try
                        {
                            timeoutTimer?.Stop();
                            timeoutTimer?.Dispose();
                        }
                        catch
                        {
                        }

                        try
                        {
                            probeWebView?.Dispose();
                        }
                        catch
                        {
                        }

                        if (!probeTask.Task.IsCompleted)
                        {
                            probeTask.TrySetResult(latestUri ?? new Uri(url));
                        }

                        WinForms.Application.ExitThread();
                    };

                    WinForms.Application.Run(probeForm);
                }
                catch (Exception ex)
                {
                    probeTask.TrySetException(ex);
                }
            });
            probeThread.SetApartmentState(ApartmentState.STA);
            probeThread.IsBackground = true;
            probeThread.Start();
            return await probeTask.Task;
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
            if (IsCaptchaCooldownActive(testUrl)) return true;

            BrowserSessionSnapshot cachedSession = GetCachedBrowserSession(testUrl);
            if (cachedSession != null)
            {
                ApplyBrowserSessionSnapshot(cachedSession);
            }

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
                        bool useHeadlessAutomation = false;
                        var captchaWin = CreateCaptchaWindow(testUrl, autoDeleteCookiesOnLoad: true, headlessAutomation: useHeadlessAutomation);
                        if (!_downloadMissingChapterScanInProgress)
                        {
                            captchaWin.Owner = this;
                        }

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
                                RememberScopedUserAgent(testUrl, captchaWin.UserAgent);
                                RememberScopedUserAgent(resolvedUri.AbsoluteUri, captchaWin.UserAgent);
                            }
                            RememberBrowserSession(testUrl, resolvedUri, captchaWin.UserAgent, captchaWin.ResolvedCookies, BrowserSessionEngine.WebView2);
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
                    MarkCaptchaSolved(testUrl);
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
            return "https://nettruyenviet10.com"; // ponytail: fallback only when URL parse fails. Upgrade path: infer from active tab if needed.
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

        private static string ExtractNettruyenCenterHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return html ?? string.Empty;
            }

            string normalized = NormalizeNettruyenHtml(html);
            int centerIndex = normalized.IndexOf("id=\"ctl00_divCenter\"", StringComparison.OrdinalIgnoreCase);
            if (centerIndex < 0)
            {
                return normalized;
            }

            int rightIndex = normalized.IndexOf("id=\"ctl00_divRight\"", centerIndex, StringComparison.OrdinalIgnoreCase);
            if (rightIndex > centerIndex)
            {
                return normalized.Substring(centerIndex, rightIndex - centerIndex);
            }

            return normalized.Substring(centerIndex);
        }

        private static string ExtractNettruyenListChapterHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return html ?? string.Empty;
            }

            string normalized = NormalizeNettruyenHtml(html);
            int listIndex = normalized.IndexOf("class=\"list-chapter\"", StringComparison.OrdinalIgnoreCase);
            if (listIndex < 0)
            {
                listIndex = normalized.IndexOf("id=\"nt_listchapter\"", StringComparison.OrdinalIgnoreCase);
            }

            if (listIndex < 0)
            {
                return normalized;
            }

            int rightIndex = normalized.IndexOf("id=\"ctl00_divRight\"", listIndex, StringComparison.OrdinalIgnoreCase);
            if (rightIndex < 0)
            {
                rightIndex = normalized.IndexOf("class=\"right-side\"", listIndex, StringComparison.OrdinalIgnoreCase);
            }
            if (rightIndex < 0)
            {
                rightIndex = normalized.IndexOf("class=\"visited-comics\"", listIndex, StringComparison.OrdinalIgnoreCase);
            }
            if (rightIndex > listIndex)
            {
                string sliced = normalized.Substring(listIndex, rightIndex - listIndex);
                if (sliced.Length > 200 && sliced.IndexOf("<a", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return sliced;
                }
            }

            return normalized.Substring(listIndex);
        }

        private static string StripNettruyenBookPrefix(string title)
        {
            return string.IsNullOrWhiteSpace(title)
                ? title
                : Regex.Replace(title, @"^\s*(?:đọc\s+)?(?:truyện|truyen)(?:\s+tranh)?\s+", string.Empty, RegexOptions.IgnoreCase).Trim();
        }

        private string ExtractNettruyenBookTitle(string html, string fallbackTitle = null)
        {
            string title = null;

            if (!string.IsNullOrWhiteSpace(html))
            {
                // 1. Try title-detail h1 (for detail page)
                var match = Regex.Match(
                    html,
                    @"<h1\b[^>]*\btitle-detail\b[^>]*>(?<title>[\s\S]*?)<\/h1>",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);

                if (match.Success)
                {
                    title = WebUtility.HtmlDecode(Regex.Replace(match.Groups["title"].Value, @"<[^>]+>", "").Trim());
                }

                // 2. Try breadcrumb schema (commonly found on chapter pages)
                if (string.IsNullOrWhiteSpace(title))
                {
                    var breadcrumbMatches = Regex.Matches(html, @"<li[^>]*itemprop=""itemListElement""[^>]*>[\s\S]*?<span[^>]*itemprop=""name""[^>]*>(?<name>[^<]+)</span>", RegexOptions.IgnoreCase);
                    if (breadcrumbMatches.Count >= 3)
                    {
                        string candidate = WebUtility.HtmlDecode(breadcrumbMatches[2].Groups["name"].Value.Trim());
                        if (!string.IsNullOrEmpty(candidate) && !candidate.Equals("Truyện tranh", StringComparison.OrdinalIgnoreCase))
                        {
                            title = candidate;
                        }
                    }
                }

                // 3. Try standard breadcrumb links: e.g. <a href=".../truyen-tranh/..." ...>Book Title</a>
                if (string.IsNullOrWhiteSpace(title))
                {
                    var linkMatches = Regex.Matches(html, @"<a\s+[^>]*href=[""'](?:https?://[^/]+)?/truyen-tranh/[^""'/]+[""'][^>]*>(?<name>[\s\S]*?)<\/a>", RegexOptions.IgnoreCase);
                    if (linkMatches.Count >= 2)
                    {
                        string candidate = Regex.Replace(linkMatches[1].Groups["name"].Value, @"<[^>]+>", "").Trim();
                        candidate = WebUtility.HtmlDecode(candidate);
                        if (!string.IsNullOrEmpty(candidate) && !candidate.Equals("Truyện tranh", StringComparison.OrdinalIgnoreCase))
                        {
                            title = candidate;
                        }
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                title = fallbackTitle ?? string.Empty;
            }

            // Cleanup common suffixes
            string[] commonSuffixes = { " - NetTruyen", " - Nettruyen", " | NetTruyen", " | Nettruyen" };
            foreach (var suffix in commonSuffixes)
            {
                if (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    title = title.Substring(0, title.Length - suffix.Length).Trim();
                }
            }

            // Strip chapter numbers if somehow they are still in the title
            title = Regex.Replace(title, @"\s+(?:chap(?:ter)?|chương|chuong)\s*\d+(?:\.\d+)?(?:\s+next)?.*$", "", RegexOptions.IgnoreCase);

            return FormatGalleryTitle(StripNettruyenBookPrefix(title));
        }

        private static List<ReaderChapterItem> ExtractNettruyenChapterItems(string chapterListHtml, string activeDomain, string parentPath)
        {
            var chapterItems = new List<ReaderChapterItem>();
            var seenChapters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string targetHtml = ExtractNettruyenListChapterHtml(chapterListHtml);
            string pattern = @"<a\b[^>]*href=[""'](?<link>[^""']*(?:chuong|chap|chapter|c|chuong-tranh|chuong-doc)[-_]?\d+(?:\.\d+)?[^""'\s?#]*)[""'][^>]*>(?<name>[\s\S]*?)<\/a>";
            var matches = Regex.Matches(targetHtml, pattern, RegexOptions.IgnoreCase);
            if (matches.Count == 0 && !ReferenceEquals(targetHtml, chapterListHtml))
            {
                targetHtml = chapterListHtml;
                matches = Regex.Matches(targetHtml, pattern, RegexOptions.IgnoreCase);
            }

            foreach (Match m in matches)
            {
                string rawLink = m.Groups["link"].Value.Trim();
                string link = WebUtility.UrlDecode(rawLink);
                if (!link.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !link.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    link = activeDomain + (link.StartsWith("/") ? string.Empty : "/") + link;
                }
                else
                {
                    var activeUri = new Uri(activeDomain);
                    if (Uri.TryCreate(link, UriKind.Absolute, out Uri tempLinkUri) &&
                        tempLinkUri.Host.IndexOf("nettruyen", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        !string.Equals(tempLinkUri.Host, activeUri.Host, StringComparison.OrdinalIgnoreCase))
                    {
                        var builder = new UriBuilder(tempLinkUri) { Host = activeUri.Host };
                        link = builder.Uri.ToString();
                    }
                }

                link = link.TrimEnd('/');
                if (!string.IsNullOrWhiteSpace(parentPath) &&
                    Uri.TryCreate(link, UriKind.Absolute, out Uri linkUri) &&
                    !linkUri.AbsolutePath.StartsWith(parentPath.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase) &&
                    !linkUri.AbsolutePath.StartsWith(parentPath.TrimEnd('/') + "-", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (seenChapters.Add(link))
                {
                    string name = WebUtility.HtmlDecode(Regex.Replace(m.Groups["name"].Value, @"<[^>]+>", string.Empty)).Trim();
                    chapterItems.Add(new ReaderChapterItem
                    {
                        Name = string.IsNullOrWhiteSpace(name) ? null : name,
                        FolderPath = link,
                        Pages = new List<ReaderPageItem>()
                    });
                }
            }

            // Fallback 1: If parentPath filtering eliminated all items, retry without parentPath check for matches containing parentPath slug
            if (chapterItems.Count == 0 && matches.Count > 0)
            {
                string slug = !string.IsNullOrWhiteSpace(parentPath) ? Path.GetFileName(parentPath.TrimEnd('/')) : null;
                foreach (Match m in matches)
                {
                    string rawLink = m.Groups["link"].Value.Trim();
                    string link = WebUtility.UrlDecode(rawLink);
                    if (!link.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                        !link.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        link = activeDomain + (link.StartsWith("/") ? string.Empty : "/") + link;
                    }

                    link = link.TrimEnd('/');
                    if (slug == null || link.IndexOf(slug, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (seenChapters.Add(link))
                        {
                            string name = WebUtility.HtmlDecode(Regex.Replace(m.Groups["name"].Value, @"<[^>]+>", string.Empty)).Trim();
                            chapterItems.Add(new ReaderChapterItem
                            {
                                Name = string.IsNullOrWhiteSpace(name) ? null : name,
                                FolderPath = link,
                                Pages = new List<ReaderPageItem>()
                            });
                        }
                    }
                }
            }

            // Fallback 2: Extract numeric chapter numbers from embedded script JSON or text
            if (chapterItems.Count == 0 && !string.IsNullOrWhiteSpace(chapterListHtml))
            {
                string slug = !string.IsNullOrWhiteSpace(parentPath) ? parentPath.TrimEnd('/') : string.Empty;
                foreach (Match sm in Regex.Matches(chapterListHtml, @"""chapter_num""\s*:\s*""?(?<num>\d+(?:\.\d+)?)", RegexOptions.IgnoreCase))
                {
                    string num = sm.Groups["num"].Value;
                    if (!string.IsNullOrWhiteSpace(slug) && !string.IsNullOrWhiteSpace(num))
                    {
                        string link = $"{activeDomain}{slug}/chuong-{num}".TrimEnd('/');
                        if (seenChapters.Add(link))
                        {
                            chapterItems.Add(new ReaderChapterItem
                            {
                                Name = $"Chapter {num}",
                                FolderPath = link,
                                Pages = new List<ReaderPageItem>()
                            });
                        }
                    }
                }
            }

            // Debug logging if 0 chapters detected
            if (chapterItems.Count == 0 && !string.IsNullOrWhiteSpace(chapterListHtml) && chapterListHtml.Length > 100)
            {
                try
                {
                    string debugDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".tmp");
                    Directory.CreateDirectory(debugDir);
                    string slug = !string.IsNullOrWhiteSpace(parentPath) ? Path.GetFileName(parentPath.TrimEnd('/')) : "unknown";
                    File.WriteAllText(Path.Combine(debugDir, $"debug_0_chap_{slug}.html"), chapterListHtml, System.Text.Encoding.UTF8);
                }
                catch { }
            }

            return chapterItems;
        }

        private static List<string> ExtractNettruyenChapterLinks(string chapterListHtml, string activeDomain, string parentPath)
        {
            return ExtractNettruyenChapterItems(chapterListHtml, activeDomain, parentPath)
                .Select(chapter => chapter.FolderPath)
                .ToList();
        }

        private async Task<List<ReaderChapterItem>> LoadNettruyenChapterListApiAsync(string cleanLink, string activeDomain, CancellationToken token)
        {
            if (_nettruyenScanCache.TryGetValue(cleanLink, out var cached) && (DateTime.Now - cached.Timestamp).TotalMinutes < 15)
            {
                return cached.Chapters;
            }

            try
            {
                var uri = new Uri(cleanLink);
                string[] segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length < 2 || !segments[0].Equals("truyen-tranh", StringComparison.OrdinalIgnoreCase))
                {
                    return new List<ReaderChapterItem>();
                }

                string slug = segments[1];
                string apiUrl = $"{activeDomain}/Comic/Services/ComicService.asmx/ChapterList?slug={Uri.EscapeDataString(slug)}";
                using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(4));
                    using (var request = new HttpRequestMessage(HttpMethod.Get, apiUrl))
                    {
                        request.Headers.Referrer = uri;
                        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
                        using (var response = await _httpClient.SendAsync(request, timeoutCts.Token))
                        {
                            if (!response.IsSuccessStatusCode)
                            {
                                return new List<ReaderChapterItem>();
                            }

                            string json = await response.Content.ReadAsStringAsync();
                            var links = new List<ReaderChapterItem>();
                            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (Match objectMatch in _nettruyenJsonObjectRegex.Matches(json))
                            {
                                string obj = objectMatch.Groups["obj"].Value;
                                Match numberMatch = _nettruyenChapterNumRegex.Match(obj);
                                if (!numberMatch.Success)
                                {
                                    continue;
                                }

                                string number = numberMatch.Groups["num"].Value;
                                string link = $"{activeDomain}/truyen-tranh/{slug}/chuong-{number}".TrimEnd('/');
                                if (seen.Add(link))
                                {
                                    Match nameMatch = _nettruyenChapterNameRegex.Match(obj);
                                    string name = nameMatch.Success
                                        ? WebUtility.HtmlDecode(Regex.Unescape(nameMatch.Groups["name"].Value)).Trim()
                                        : null;
                                    links.Add(new ReaderChapterItem
                                    {
                                        Name = string.IsNullOrWhiteSpace(name) ? null : name,
                                        FolderPath = link,
                                        Pages = new List<ReaderPageItem>()
                                    });
                                }
                            }

                            if (links.Count > 0)
                            {
                                _nettruyenScanCache[cleanLink] = (DateTime.Now, links);
                            }

                            return links;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[nettruyen] Lỗi ChapterList API: {ex.Message}");
                return new List<ReaderChapterItem>();
            }
        }

        private static bool HasNettruyenViewMoreButton(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return false;
            }

            // ponytail: tiny heuristic. Upgrade path: DOM parse if site changes markup again.
            return Regex.IsMatch(html, @"<a\b[^>]*class=[""'][^""']*\bview-more\b[^""']*[""'][^>]*>", RegexOptions.IgnoreCase) ||
                   Regex.IsMatch(html, @"\bxem[\s-]*th[eê]m\b", RegexOptions.IgnoreCase) ||
                   Regex.IsMatch(html, @"\bview-more\b", RegexOptions.IgnoreCase);
        }

        private async Task<Tuple<string, string>> LoadExpandedNettruyenChapterHtmlAsync(string cleanLink)
        {
            string webViewHtml = null;
            string resolvedUrl = null;
            await WaitForNettruyenWatchMoreSlotAsync();
            _isDownloadPaused = true;
            try
            {
                await await Dispatcher.InvokeAsync(async () =>
                {
                    bool useHeadlessAutomation = true;
                    var captchaWin = CreateWatchMoreCaptcha(cleanLink, autoDeleteCookiesOnLoad: false, headlessAutomation: useHeadlessAutomation);
                    if (!_downloadMissingChapterScanInProgress)
                    {
                        captchaWin.Owner = this;
                    }
                    captchaWin.Title = "ĐANG TẢI DANH SÁCH CHƯƠNG - VUI LÒNG CHỜ...";

                    if (await captchaWin.ShowNonBlockingAsync() && !string.IsNullOrEmpty(captchaWin.ResolvedHtml))
                    {
                        webViewHtml = NormalizeNettruyenHtml(captchaWin.ResolvedHtml);
                        resolvedUrl = captchaWin.ResolvedUri?.ToString();

                        var resolvedUri = captchaWin.ResolvedUri ?? new Uri(cleanLink);
                        var resolvedCookies = captchaWin.ResolvedCookies.GetCookies(resolvedUri);
                        foreach (Cookie cookie in resolvedCookies)
                        {
                            _cookieContainer.Add(resolvedUri, cookie);
                        }
                        RememberBrowserSession(cleanLink, resolvedUri, captchaWin.UserAgent, captchaWin.ResolvedCookies, BrowserSessionEngine.WebView2);
                    }
                });
            }
            finally
            {
                _isDownloadPaused = false;
                Interlocked.Decrement(ref _nettruyenWatchMoreWebViewActiveCount);
            }

            return Tuple.Create(webViewHtml, resolvedUrl);
        }

        private async Task WaitForNettruyenWatchMoreSlotAsync()
        {
            while (true)
            {
                int active = Volatile.Read(ref _nettruyenWatchMoreWebViewActiveCount);
                int limit = GetDownloadMissingChapterParallelLimit();
                if (active < limit &&
                    Interlocked.CompareExchange(ref _nettruyenWatchMoreWebViewActiveCount, active + 1, active) == active)
                {
                    return;
                }

                await Task.Delay(150);
            }
        }

        private async Task NettruyenFetchInfoAsync(bool isTech = false)
        {
            TextBox tagUrlBox = isTech ? txtNettruyenTechTagUrl : txtNettruyenTagUrl;
            TextBox totalPagesBox = isTech ? txtNettruyenTechTotalPages : txtNettruyenTotalPages;
            TextBox pageToBox = isTech ? txtNettruyenTechPageTo : txtNettruyenPageTo;
            Button fetchButton = isTech ? btnNettruyenTechFetchInfo : btnNettruyenFetchInfo;

            string url = tagUrlBox.Text.Trim();
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

            if (isTech)
            {
                await EnsureNettruyenTechRedirectDomainAsync();
                url = ApplyNettruyenTechRedirectDomain(url);
            }

            if (fetchButton != null) fetchButton.IsEnabled = false;
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

                if (totalPagesBox != null) totalPagesBox.Text = maxPage.ToString();
                if (pageToBox != null) pageToBox.Text = maxPage.ToString();
                
                NettruyenLog($"Phân tích hoàn tất. Phát hiện tối đa {maxPage} trang.");
                lblStatus.Text = $"Analysis complete. Found {maxPage} pages.";
            }
            catch (Exception ex)
            {
                NettruyenLog($"Lỗi khi phân tích: {ex.Message}");
                if (totalPagesBox != null) totalPagesBox.Text = "1";
                lblStatus.Text = "Analysis failed.";
            }
            finally
            {
                if (fetchButton != null) fetchButton.IsEnabled = true;
                progressBar.IsIndeterminate = false;
            }
        }

        private async void BtnNettruyenFetchInfo_Click(object sender, RoutedEventArgs e)
        {
            await NettruyenFetchInfoAsync();
        }

        private void SyncNettruyenTotalPages(TextBox totalPagesBox, TextBox pageToBox)
        {
            if (totalPagesBox != null && pageToBox != null)
            {
                pageToBox.Text = totalPagesBox.Text;
            }
        }

        private void TxtNettruyenTotalPages_TextChanged(object sender, TextChangedEventArgs e)
        {
            SyncNettruyenTotalPages(txtNettruyenTotalPages, txtNettruyenPageTo);
        }

        private void TxtNettruyenTechTotalPages_TextChanged(object sender, TextChangedEventArgs e)
        {
            SyncNettruyenTotalPages(txtNettruyenTechTotalPages, txtNettruyenTechPageTo);
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
            SelectDownloadMangaTab();
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
            SelectDownloadMangaTab();
            await ScrapeNettruyenAsync(clearExisting: false);
        }

        private void CopyNettruyenTechInputsToPrimary()
        {
            if (txtNettruyenTechTagUrl != null && txtNettruyenTagUrl != null) txtNettruyenTagUrl.Text = ApplyNettruyenTechRedirectDomain(txtNettruyenTechTagUrl.Text);
            if (txtNettruyenTechPageFrom != null && txtNettruyenPageFrom != null) txtNettruyenPageFrom.Text = txtNettruyenTechPageFrom.Text;
            if (txtNettruyenTechPageTo != null && txtNettruyenPageTo != null) txtNettruyenPageTo.Text = txtNettruyenTechPageTo.Text;
            if (txtNettruyenTechTotalPages != null && txtNettruyenTotalPages != null) txtNettruyenTotalPages.Text = txtNettruyenTechTotalPages.Text;
        }

        private void CopyNettruyenPrimaryOutputsToTech()
        {
            if (txtNettruyenTechTagUrl != null && txtNettruyenTagUrl != null) txtNettruyenTechTagUrl.Text = txtNettruyenTagUrl.Text;
            if (txtNettruyenTechPageFrom != null && txtNettruyenPageFrom != null) txtNettruyenTechPageFrom.Text = txtNettruyenPageFrom.Text;
            if (txtNettruyenTechPageTo != null && txtNettruyenPageTo != null) txtNettruyenTechPageTo.Text = txtNettruyenPageTo.Text;
            if (txtNettruyenTechTotalPages != null && txtNettruyenTotalPages != null) txtNettruyenTechTotalPages.Text = txtNettruyenTotalPages.Text;
        }

        private void SetNettruyenTechButtonsEnabled(bool isEnabled)
        {
            if (btnNettruyenTechFetchInfo != null) btnNettruyenTechFetchInfo.IsEnabled = isEnabled;
            if (btnNettruyenTechScrape != null) btnNettruyenTechScrape.IsEnabled = isEnabled;
            if (btnNettruyenTechCrawlMore != null) btnNettruyenTechCrawlMore.IsEnabled = isEnabled;
            if (btnNettruyenTechPasteDirect != null) btnNettruyenTechPasteDirect.IsEnabled = isEnabled;
        }

        private async void BtnNettruyenTechFetchInfo_Click(object sender, RoutedEventArgs e)
        {
            SetNettruyenTechButtonsEnabled(false);
            var oldLogTarget = _nettruyenLogOverride;
            _nettruyenLogOverride = txtNettruyenTechLog;
            try
            {
                await NettruyenFetchInfoAsync(isTech: true);
            }
            finally
            {
                _nettruyenLogOverride = oldLogTarget;
                SetNettruyenTechButtonsEnabled(true);
            }
        }

        private async void BtnNettruyenTechScrape_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                if (btnNettruyenTechScrape != null)
                {
                    btnNettruyenTechScrape.Content = "CANCELLING...";
                    btnNettruyenTechScrape.IsEnabled = false;
                }
                if (btnNettruyenTechCrawlMore != null) btnNettruyenTechCrawlMore.IsEnabled = false;
                return;
            }

            SetNettruyenTechButtonsEnabled(false);
            var oldLogTarget = _nettruyenLogOverride;
            _nettruyenLogOverride = txtNettruyenTechLog;
            try
            {
                SelectDownloadMangaTab();
                await ScrapeNettruyenAsync(clearExisting: true, isTech: true);
            }
            finally
            {
                _nettruyenLogOverride = oldLogTarget;
                SetNettruyenTechButtonsEnabled(true);
            }
        }

        private async void BtnNettruyenTechCrawlMore_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                if (btnNettruyenTechCrawlMore != null)
                {
                    btnNettruyenTechCrawlMore.Content = "CANCELLING...";
                    btnNettruyenTechCrawlMore.IsEnabled = false;
                }
                if (btnNettruyenTechScrape != null) btnNettruyenTechScrape.IsEnabled = false;
                return;
            }

            SetNettruyenTechButtonsEnabled(false);
            var oldLogTarget = _nettruyenLogOverride;
            _nettruyenLogOverride = txtNettruyenTechLog;
            try
            {
                SelectDownloadMangaTab();
                await ScrapeNettruyenAsync(clearExisting: false, isTech: true);
            }
            finally
            {
                _nettruyenLogOverride = oldLogTarget;
                SetNettruyenTechButtonsEnabled(true);
            }
        }

        private async Task ScrapeNettruyenAsync(bool clearExisting, bool isTech = false)
        {
            TextBox tagUrlBox = isTech ? txtNettruyenTechTagUrl : txtNettruyenTagUrl;
            TextBox pageFromBox = isTech ? txtNettruyenTechPageFrom : txtNettruyenPageFrom;
            TextBox pageToBox = isTech ? txtNettruyenTechPageTo : txtNettruyenPageTo;
            Button scrapeButton = isTech ? btnNettruyenTechScrape : btnNettruyenScrape;
            Button crawlMoreButton = isTech ? btnNettruyenTechCrawlMore : btnNettruyenCrawlMore;
            Button fetchButton = isTech ? btnNettruyenTechFetchInfo : btnNettruyenFetchInfo;

            string baseUrl = tagUrlBox.Text.Trim();
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

            if (isTech)
            {
                await EnsureNettruyenTechRedirectDomainAsync();
                baseUrl = ApplyNettruyenTechRedirectDomain(baseUrl);
            }

            if (!int.TryParse(pageFromBox.Text, out int pageFrom) || pageFrom < 1)
            {
                MessageBox.Show("Trang bắt đầu không hợp lệ.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!int.TryParse(pageToBox.Text, out int pageTo) || pageTo < pageFrom)
            {
                MessageBox.Show("Trang kết thúc không hợp lệ.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            if (scrapeButton != null) scrapeButton.Content = "STOP CRAWLER";
            if (crawlMoreButton != null) crawlMoreButton.Content = "STOP CRAWLER";
            if (fetchButton != null) fetchButton.IsEnabled = false;
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
                ShowTransientResultsImportingStatus("getting link...");
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

                    string html = ExtractNettruyenCenterHtml(await FetchStringAsync(pageUrl, _downloadCts?.Token ?? CancellationToken.None));
                    
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
                        else
                        {
                            string activeDomain = ExtractNettruyenBaseUrl(pageUrl);
                            var activeUri = new Uri(activeDomain);
                            if (Uri.TryCreate(fullLink, UriKind.Absolute, out Uri linkUri) &&
                                linkUri.Host.IndexOf("nettruyen", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                !string.Equals(linkUri.Host, activeUri.Host, StringComparison.OrdinalIgnoreCase))
                            {
                                var builder = new UriBuilder(linkUri) { Host = activeUri.Host };
                                fullLink = builder.Uri.ToString();
                            }
                        }
                        fullLink = fullLink.TrimEnd('/');

                        // Detect if chapter link
                        bool isChap = Regex.IsMatch(relativeLink, @"(?:/|-)(?:chuong|chap|chapter|c|chuong-tranh|chuong-doc)[-_]?\d+(?:\.\d+)?", RegexOptions.IgnoreCase);
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
                                    Name = ExtractNettruyenBookTitle(null, title),
                                    SourceDomain = GetDownloadSiteKey(new GalleryItem { Link = fullLink }),
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
                    UpdateResultsCrawlProgress(pagesProcessed, totalPages, GuessImportDisplayName(baseUrl));
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
                if (scrapeButton != null)
                {
                    scrapeButton.Content = "GET LINK";
                    scrapeButton.IsEnabled = true;
                }
                if (crawlMoreButton != null)
                {
                    crawlMoreButton.Content = "GET MORE";
                    crawlMoreButton.IsEnabled = true;
                }
                if (fetchButton != null) fetchButton.IsEnabled = true;
                HideTransientResultsImportingStatus();
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

        private void BtnNettruyenTechPasteDirect_Click(object sender, RoutedEventArgs e)
        {
            var win = new DirectDownloadWindow(isNhentai: false);
            win.Owner = this;
            win.OnImport = async (links) =>
            {
                if (links != null && links.Any())
                {
                    await ImportNettruyenTechDirectLinksAsync(links);
                }
            };
            win.Show();
        }

        private async Task ImportNettruyenTechDirectLinksAsync(List<string> links, bool showMessageBox = true)
        {
            bool keepControlsEnabled = IsResultsImportingActive();
            if (!keepControlsEnabled)
            {
                SetNettruyenTechButtonsEnabled(false);
            }
            var oldLogTarget = _nettruyenLogOverride;
            _nettruyenLogOverride = txtNettruyenTechLog;
            try
            {
                await ImportNettruyenDirectLinksAsync(
                    links ?? new List<string>(),
                    showMessageBox);
            }
            finally
            {
                _nettruyenLogOverride = oldLogTarget;
                if (!keepControlsEnabled)
                {
                    SetNettruyenTechButtonsEnabled(true);
                }
            }
        }

        private async Task ImportNettruyenDirectLinksAsync(List<string> links, bool showMessageBox = true)
        {
            bool keepControlsEnabled = IsResultsImportingActive();
            if (!keepControlsEnabled)
            {
                btnNettruyenScrape.IsEnabled = false;
                btnNettruyenFetchInfo.IsEnabled = false;
            }
            
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
                            title = ExtractNettruyenBookTitle(html, mangaName);
                        }

                        Dispatcher.BeginInvoke((Action)(() =>
                        {
                            _scrapedItems.Add(new GalleryItem
                            {
                                Link = link,
                                Name = FormatGalleryTitle(title),
                                SourceDomain = GetDownloadSiteKey(new GalleryItem { Link = link }),
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
                                SourceDomain = GetDownloadSiteKey(new GalleryItem { Link = link }),
                                OriginalIndex = _scrapedItems.Count,
                                IsChecked = true,
                                HasNoChapters = false
                            });
                        }));
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
                NettruyenLog($"[Import] Nhập hoàn tất! Thành công: {imported}, Lỗi/Fallback: {failed}.");
                lblStatus.Text = $"Import completed. Success: {imported}, Failed: {failed}.";
                
                ShowImportSummaryIfNeeded(showMessageBox, total, imported, failed);
            }
            finally
            {
                if (!keepControlsEnabled)
                {
                    btnNettruyenScrape.IsEnabled = true;
                    btnNettruyenFetchInfo.IsEnabled = true;
                }
                if (btnStartDownload != null) btnStartDownload.IsEnabled = true;
                if (!keepControlsEnabled)
                {
                    progressBar.Value = 100;
                }
            }
        }

        private async Task DownloadNettruyenGalleryAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem = null, ChapterFilter chapterFilter = null)
        {
            string cleanLink = item.Link.TrimEnd('/');
            string activeDomain = ExtractNettruyenBaseUrl(cleanLink);
            string siteFolder = GetNettruyenSiteFolder(item);

            var uri = new Uri(cleanLink);
            string parentPath = Regex.Replace(uri.AbsolutePath.TrimEnd('/'), @"\.html$", "", RegexOptions.IgnoreCase);
            var segments = parentPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            bool looksLikeChapter = Regex.IsMatch(uri.AbsolutePath, @"(?:/|-)(?:chuong|chap|chapter|c|chuong-tranh|chuong-doc)[-_]?\d+(?:\.\d+)?", RegexOptions.IgnoreCase);

            // Detail Page: /truyen-tranh/{slug} or /truyen-tranh/{slug}.html
            // Chapter Page: /truyen-tranh/{slug}/chuong-1 or /truyen-tranh/{slug}-chuong-1.html
            bool isDetailPage = segments.Length == 2 &&
                segments[0].Equals("truyen-tranh", StringComparison.OrdinalIgnoreCase) &&
                !looksLikeChapter;
            if (!isDetailPage && uri.AbsolutePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase) && !looksLikeChapter)
            {
                isDetailPage = true;
            }

            if (isDetailPage)
            {
                if (chapterFilter == null)
                {
                    var pendingFromProcess = LoadPendingChapterLinksFromProcess(rootFolder, siteFolder, item);
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

                if (TryGetCachedDownloadChapterLinks(item, out List<string> cachedChapterLinks) && cachedChapterLinks != null && cachedChapterLinks.Count > 0)
                {
                    cachedChapterLinks = cachedChapterLinks.OrderBy(ParseChapterNumber).ToList();
                    NettruyenLog($"Dùng {cachedChapterLinks.Count} chapter từ cache check thiếu chap cho '{item.Name}'.");
                    List<string> effectiveChapterLinks = chapterFilter != null
                        ? FilterPendingChapterLinksFromProcess(rootFolder, siteFolder, item, cachedChapterLinks.Where(link => chapterFilter.IsMatch(ParseChapterNumber(link))).ToList())
                        : FilterPendingChapterLinksFromProcess(rootFolder, siteFolder, item, cachedChapterLinks);
                    if (effectiveChapterLinks.Count == 0)
                    {
                        if (queueItem != null)
                        {
                            Dispatcher.BeginInvoke((Action)(() =>
                            {
                                queueItem.Status = "Completed";
                                queueItem.CurrentProcess = chapterFilter != null
                                    ? "Không có chương trùng khớp bộ lọc"
                                    : "Đã hoàn tất theo process";
                            }));
                        }
                        return;
                    }

                    await DownloadNettruyenPendingChaptersAsync(item, rootFolder, token, queueItem, effectiveChapterLinks);
                    Dispatcher.BeginInvoke((Action)(() => item.LinkCount = cachedChapterLinks.Count.ToString()));
                    return;
                }

                var chapterLinks = await GetNettruyenChapterLinksInternalAsync(item, cleanLink, token, forceRefresh: false);

                if (chapterLinks == null || chapterLinks.Count == 0)
                {
                    Log($"[nettruyen] Lỗi: Không tìm thấy chương nào trong '{item.Name}'. Hủy tải xuống.");
                    if (queueItem != null)
                    {
                        Dispatcher.BeginInvoke((Action)(() =>
                        {
                            queueItem.Status = "Error";
                            queueItem.CurrentProcess = "Lỗi: Không tìm thấy danh sách chapter";
                        }));
                    }
                    throw new Exception($"Không tìm thấy danh sách chapter cho truyện '{item.Name}'.");
                }

                // Sort chapters in ascending order
                chapterLinks = chapterLinks.OrderBy(ParseChapterNumber).ToList();
                CacheDownloadMissingChapterLinks(item, chapterLinks);

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
                    chapterLinks = FilterPendingChapterLinksFromProcess(rootFolder, siteFolder, item, filtered);
                    if (chapterLinks.Count == 0)
                    {
                        Log($"[nettruyen] Không có chương nào cần tải (hoặc đã hoàn tất theo process) trong tổng số {totalFoundChapters} chương của '{item.Name}'.");
                        if (queueItem != null)
                        {
                            Dispatcher.BeginInvoke((Action)(() => {
                                queueItem.Status = "Completed";
                                queueItem.CurrentProcess = "Đã hoàn tất theo process";
                            }));
                        }
                        return;
                    }
                }
                else
                {
                    chapterLinks = FilterPendingChapterLinksFromProcess(rootFolder, siteFolder, item, chapterLinks);
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
            string siteFolder = GetNettruyenSiteFolder(item);
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

                var chapItem = new GalleryItem { Link = chapLink, Name = item.Name, SourceDomain = siteFolder };
                bool chapterCompleted = await DownloadNettruyenChapterAsync(chapItem, rootFolder, token, queueItem, isParentQueue: true);
                if (chapterCompleted)
                {
                    MarkChapterProcessDone(rootFolder, siteFolder, item, chapLink);
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
            string siteFolder = GetNettruyenSiteFolder(item);
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

                mangaTitle = ExtractNettruyenBookTitle(html, rawTitle);
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
            string progressKey = $"{siteFolder}|{GetSafePathName(cleanManga)}";
            int totalChaptersForLog = queueItem != null ? Math.Max(1, queueItem.TotalChapters) : 1;
            int currentChapterForLog = queueItem != null ? Math.Max(1, Math.Min(queueItem.CompletedChapters + 1, totalChaptersForLog)) : 1;
            UpsertMainLogLine(progressKey, $"[{siteFolder}] Đang tải {cleanManga} - {cleanChapter} ({currentChapterForLog}/{totalChaptersForLog})");
            
            string siteRootFolder = GetSiteDownloadRoot(rootFolder, siteFolder);
            string unmergedPath = Path.Combine(siteRootFolder, $"{safeManga}-{safeChapter}");
            string mergedPath = Path.Combine(siteRootFolder, safeManga, safeChapter);
            string finalTargetFolder = _isSingleComicFolderType ? mergedPath : unmergedPath;
            string tempFolder = BuildStableTempFolderPath(siteRootFolder, siteFolder, safeManga, safeChapter, item.Link);
            Directory.CreateDirectory(tempFolder);
            RegisterTempFolder(tempFolder);
            // Isolate images inside reading area
            string safeHtml = GetSafeChapterHtml(html);
            int startIndex = -1;
            string[] containerMarkers = new[]
            {
                "class=\"reading-detail box_doc\"",
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
                                        RecordCheckError(siteFolder, queueItem.Name ?? cleanManga, cleanChapter, index + 1, lastEx.Message, imgUrl, pageName);
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
                MoveTempFolderToTarget(tempFolder, finalTargetFolder, siteFolder);
                UpsertMainLogLine(progressKey, $"[{siteFolder}] Đã tải xong {cleanManga} - {cleanChapter} ({currentChapterForLog}/{totalChaptersForLog})");
            }

            return ValidateDownloadedFiles(finalTargetFolder, imageUrls.Count, queueItem, cleanChapter, pageImageUrls: null, chapterUrl: item.Link);
        }

        private async Task<List<string>> GetNettruyenChapterLinksInternalAsync(GalleryItem item, string cleanLink, CancellationToken token, bool forceRefresh)
        {
            string url = cleanLink;
            if (!forceRefresh && _downloadChapterItemCache.TryGetValue(url, out List<ReaderChapterItem> cachedItems))
            {
                return cachedItems.Select(ch => ch.FolderPath).ToList();
            }

                string activeDomain = ExtractNettruyenBaseUrl(cleanLink);
                var uri = new Uri(cleanLink);
                bool canUseWebView2 = cleanLink.IndexOf("nettruyenviet10.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      cleanLink.IndexOf("nettruyen.tech", StringComparison.OrdinalIgnoreCase) >= 0;

                var chapterLabelsByLink = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                Action<IEnumerable<ReaderChapterItem>> rememberChapterLabels = chapters =>
                {
                    foreach (ReaderChapterItem chapter in chapters ?? Enumerable.Empty<ReaderChapterItem>())
                    {
                        if (!string.IsNullOrWhiteSpace(chapter?.FolderPath) && !string.IsNullOrWhiteSpace(chapter.Name))
                        {
                            chapterLabelsByLink[chapter.FolderPath] = chapter.Name;
                        }
                    }
                };

                // Fast Path 1: Check ChapterList API / memory cache first (instant ~100ms response)
                var chapterListApiItems = await LoadNettruyenChapterListApiAsync(cleanLink, activeDomain, token);
                rememberChapterLabels(chapterListApiItems);
                if (chapterListApiItems != null && chapterListApiItems.Count > 0)
                {
                    Log($"[nettruyen] Tải nhanh thành công {chapterListApiItems.Count} chương qua ChapterList API.");
                    var apiResults = chapterListApiItems.Select(ch => ch.FolderPath).ToList();
                    _downloadChapterItemCache[url] = CloneReaderChapterItems(chapterListApiItems);
                    return apiResults;
                }

                // Fast Path 2: Direct HTTP Fetch (without opening WebView2 window)
                string html = "";
                try
                {
                    html = ExtractNettruyenCenterHtml(await FetchStringAsync(cleanLink, token));
                }
                catch { }

                // Fallback to Captcha Solver ONLY if HTTP fetch failed or was blocked
                if (string.IsNullOrWhiteSpace(html) || html.Contains("cf-challenge") || html.Contains("Just a moment..."))
                {
                    bool captchaOk = await SolveNettruyenCaptchaIfNeededAsync(cleanLink);
                    if (!captchaOk)
                    {
                        throw new Exception("Không thể vượt qua Cloudflare captcha.");
                    }

                    if (!string.IsNullOrEmpty(_lastCaptchaResolvedHtml))
                    {
                        html = ExtractNettruyenCenterHtml(_lastCaptchaResolvedHtml);
                        _lastCaptchaResolvedHtml = null;
                        Log("[nettruyen] Sử dụng HTML đã nạp đầy đủ từ trình duyệt giải captcha.");
                    }
                    else
                    {
                        html = ExtractNettruyenCenterHtml(await FetchStringAsync(cleanLink, token));
                    }
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
                    string cleanTitle = ExtractNettruyenBookTitle(html, rawTitle);
                    if (!string.IsNullOrEmpty(cleanTitle))
                    {
                        if (Dispatcher.CheckAccess())
                        {
                            item.Name = cleanTitle;
                        }
                        else
                        {
                            Dispatcher.Invoke(() => item.Name = cleanTitle);
                        }
                    }
                }

                string storyId = null;
                var idMatch = Regex.Match(html, @"gOpts\.comicId\s*=\s*['""]?(?<id>\d+)['""]?", RegexOptions.IgnoreCase);
                if (!idMatch.Success) idMatch = Regex.Match(html, @"id=[""'](?:story_id|storyId|comicId)[""'][^>]*value=[""'\s]?(?<id>\d+)[""'\s]?", RegexOptions.IgnoreCase);
                if (!idMatch.Success) idMatch = Regex.Match(html, @"value=[""'\s]?(?<id>\d+)[""'\s]?[^>]*id=[""'](?:story_id|storyId|comicId)[""']", RegexOptions.IgnoreCase);
                if (!idMatch.Success) idMatch = Regex.Match(html, @"(?:story_id|storyId|comicId)\s*=\s*(?:[""']?(?<id>\d+)[""']?|\d+)", RegexOptions.IgnoreCase);
                if (!idMatch.Success) idMatch = Regex.Match(html, @"data-id=[""'](?<id>\d+)[""']", RegexOptions.IgnoreCase);

                if (idMatch.Success)
                {
                    storyId = idMatch.Groups["id"].Value;
                }

                string parentPath = Regex.Replace(uri.AbsolutePath.TrimEnd('/'), @"\.html$", "", RegexOptions.IgnoreCase);
                string chapterListHtml = ExtractNettruyenListChapterHtml(html);
                var chapterItems = ExtractNettruyenChapterItems(chapterListHtml, activeDomain, parentPath);
                if ((chapterItems == null || chapterItems.Count == 0) && !string.IsNullOrWhiteSpace(html))
                {
                    chapterItems = ExtractNettruyenChapterItems(html, activeDomain, parentPath);
                }
                rememberChapterLabels(chapterItems);
                var chapterLinks = chapterItems.Select(chapter => chapter.FolderPath).ToList();
                bool loadedChapters = false;
                chapterListApiItems = await LoadNettruyenChapterListApiAsync(cleanLink, activeDomain, token);
                rememberChapterLabels(chapterListApiItems);
                if (chapterListApiItems.Count > 0 && (chapterListApiItems.Count >= chapterLinks.Count || chapterLinks.Count == 0))
                {
                    chapterLinks = chapterListApiItems.Select(chapter => chapter.FolderPath).ToList();
                    Log($"[nettruyen] Tải thành công danh sách toàn bộ chương qua ChapterList API ({chapterLinks.Count} chương).");
                    loadedChapters = true;
                }
                string expandedChapterListHtml = null;
                string expandedResolvedUrl = null;
                bool expandedViaWebView = false;

                async Task<string> GetExpandedChapterListHtmlAsync()
                {
                    if (!canUseWebView2)
                    {
                        return null;
                    }
                    if (expandedChapterListHtml == null)
                    {
                        Tuple<string, string> expandedResult = await LoadExpandedNettruyenChapterHtmlAsync(cleanLink);
                        expandedChapterListHtml = expandedResult.Item1;
                        expandedResolvedUrl = expandedResult.Item2;
                    }
                    return expandedChapterListHtml;
                }

                bool pageHasViewMore = HasNettruyenViewMoreButton(html);
                if (!loadedChapters && !string.IsNullOrEmpty(storyId))
                {
                    loadedChapters = false;
                    try
                    {
                        string ajaxUrl = $"{activeDomain}/Comic/Services/ComicService.asmx/ProcessChapterList";
                        using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                        {
                            timeoutCts.CancelAfter(TimeSpan.FromSeconds(4));
                            using (var request = new HttpRequestMessage(HttpMethod.Post, ajaxUrl))
                            {
                                request.Headers.Referrer = new Uri(cleanLink);
                                request.Content = new StringContent($"{{\"comicId\":{storyId}}}", System.Text.Encoding.UTF8, "application/json");
                                using (var response = await _httpClient.SendAsync(request, timeoutCts.Token))
                                {
                                    if (response.IsSuccessStatusCode)
                                    {
                                        string jsonResponse = await response.Content.ReadAsStringAsync();
                                        var dMatch = Regex.Match(jsonResponse, @"""d""\s*:\s*""(?<htmlContent>.*?)""\s*}", RegexOptions.Singleline);
                                        if (dMatch.Success)
                                        {
                                            string unescapedHtml = ExtractNettruyenListChapterHtml(Regex.Unescape(dMatch.Groups["htmlContent"].Value));
                                            if (!string.IsNullOrWhiteSpace(unescapedHtml) && unescapedHtml.Length > 100)
                                            {
                                                var tempItems = ExtractNettruyenChapterItems(unescapedHtml, activeDomain, parentPath);
                                                rememberChapterLabels(tempItems);
                                                var tempLinks = tempItems.Select(chapter => chapter.FolderPath).ToList();
                                                if (tempLinks.Count > 0)
                                                {
                                                    chapterListHtml = unescapedHtml;
                                                    chapterLinks = tempLinks;
                                                    Log($"[nettruyen] Tải thành công danh sách toàn bộ chương qua ProcessChapterList (ID: {storyId}).");
                                                    loadedChapters = true;
                                                }
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

                    if (!loadedChapters)
                    {
                        try
                        {
                            string ajaxUrl = $"{activeDomain}/Comic/Services/ComicService.asmx/GetListChapter";
                            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                            {
                                timeoutCts.CancelAfter(TimeSpan.FromSeconds(4));
                                using (var request = new HttpRequestMessage(HttpMethod.Post, ajaxUrl))
                                {
                                    request.Headers.Referrer = new Uri(cleanLink);
                                    request.Content = new StringContent($"{{\"id\":{storyId}}}", System.Text.Encoding.UTF8, "application/json");
                                    using (var response = await _httpClient.SendAsync(request, timeoutCts.Token))
                                    {
                                        if (response.IsSuccessStatusCode)
                                        {
                                            string jsonResponse = await response.Content.ReadAsStringAsync();
                                            var dMatch = Regex.Match(jsonResponse, @"""d""\s*:\s*""(?<htmlContent>.*?)""\s*}", RegexOptions.Singleline);
                                            if (dMatch.Success)
                                            {
                                                string unescapedHtml = ExtractNettruyenListChapterHtml(Regex.Unescape(dMatch.Groups["htmlContent"].Value));
                                                if (!string.IsNullOrWhiteSpace(unescapedHtml))
                                                {
                                                    var tempItems = ExtractNettruyenChapterItems(unescapedHtml, activeDomain, parentPath);
                                                    rememberChapterLabels(tempItems);
                                                    var tempLinks = tempItems.Select(chapter => chapter.FolderPath).ToList();
                                                    if (tempLinks.Count > 0)
                                                    {
                                                        chapterListHtml = unescapedHtml;
                                                        chapterLinks = tempLinks;
                                                        Log($"[nettruyen] Tải thành công danh sách toàn bộ chương qua GetListChapter (ID: {storyId}).");
                                                        loadedChapters = true;
                                                    }
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

                if (!expandedViaWebView && pageHasViewMore)
                {
                    bool firstPassIncomplete = !loadedChapters;
                    if (!firstPassIncomplete)
                    {
                        double maxChap = 0;
                        foreach (var ch in chapterLinks)
                        {
                            double num = ParseChapterNumberFromText(ch);
                            if (num > maxChap) maxChap = num;
                        }
                        if (maxChap > chapterLinks.Count + 3)
                        {
                            firstPassIncomplete = true;
                        }
                    }

                    if (firstPassIncomplete && canUseWebView2)
                    {
                        Log($"[nettruyen] Danh sách chương chưa đầy đủ hoặc AJAX thất bại (có {chapterLinks.Count} chương). Đang dùng WebView2 để bung full...");
                        string webViewHtml = await GetExpandedChapterListHtmlAsync();
                        if (!string.IsNullOrEmpty(webViewHtml))
                        {
                            string expandedSourceLink = string.IsNullOrWhiteSpace(expandedResolvedUrl) ? cleanLink : expandedResolvedUrl;
                            activeDomain = ExtractNettruyenBaseUrl(expandedSourceLink);
                            parentPath = Regex.Replace(new Uri(expandedSourceLink).AbsolutePath.TrimEnd('/'), @"\.html$", "", RegexOptions.IgnoreCase);
                            var webViewItems = ExtractNettruyenChapterItems(ExtractNettruyenListChapterHtml(webViewHtml), activeDomain, parentPath);
                            rememberChapterLabels(webViewItems);
                            var webViewLinks = webViewItems.Select(chapter => chapter.FolderPath).ToList();
                            if (webViewLinks.Count > chapterLinks.Count)
                            {
                                chapterListHtml = ExtractNettruyenListChapterHtml(webViewHtml);
                                chapterLinks = webViewLinks;
                                expandedViaWebView = true;
                                Log($"[nettruyen] WebView bung được {webViewLinks.Count} chương (trước đó có {chapterLinks.Count}).");
                            }
                        }
                    }
                }

                double expectedLatestChapter = ParseChapterNumberFromText(item.LinkCount);
                double maxChapterFromLinks = 0;
                if (chapterLinks != null && chapterLinks.Count > 0)
                {
                    foreach (var cl in chapterLinks)
                    {
                        double num = ParseChapterNumber(cl);
                        if (num > maxChapterFromLinks) maxChapterFromLinks = num;
                    }
                }
                if (expectedLatestChapter <= 0 || maxChapterFromLinks > expectedLatestChapter)
                {
                    expectedLatestChapter = maxChapterFromLinks;
                }

                bool looksIncomplete = chapterLinks.Count > 0 &&
                    ((expectedLatestChapter >= 20 && (chapterLinks.Count + 5 < expectedLatestChapter || (pageHasViewMore && chapterLinks.Count < expectedLatestChapter))) ||
                     (pageHasViewMore && expectedLatestChapter == 0));

                int expandAttempts = 0;
                if (looksIncomplete)
                {
                    int originalChapterCount = chapterLinks.Count;
                    string reason = expectedLatestChapter > 0
                        ? $"{originalChapterCount} link, chap mới nhất ~ {expectedLatestChapter:0.##}"
                        : $"{originalChapterCount} link, trang có nút 'Xem thêm'";
                    Log($"[nettruyen] Danh sách chương có vẻ thiếu ({reason}). Thử mở trình duyệt để bung đủ danh sách...");
                    while (expandAttempts < 3)
                    {
                        expandAttempts++;
                        if (expandAttempts > 1)
                        {
                            expandedChapterListHtml = null;
                            expandedResolvedUrl = null;
                            await Task.Delay(500 * expandAttempts, token);
                        }
                        string webViewHtml = await GetExpandedChapterListHtmlAsync();
                        if (string.IsNullOrEmpty(webViewHtml))
                        {
                            break;
                        }

                        string expandedSourceLink = string.IsNullOrWhiteSpace(expandedResolvedUrl) ? cleanLink : expandedResolvedUrl;
                        activeDomain = ExtractNettruyenBaseUrl(expandedSourceLink);
                        parentPath = Regex.Replace(new Uri(expandedSourceLink).AbsolutePath.TrimEnd('/'), @"\.html$", "", RegexOptions.IgnoreCase);
                        var retriedItems = ExtractNettruyenChapterItems(ExtractNettruyenListChapterHtml(webViewHtml), activeDomain, parentPath);
                        rememberChapterLabels(retriedItems);
                        var retriedLinks = retriedItems.Select(chapter => chapter.FolderPath).ToList();
                        if (retriedLinks.Count > originalChapterCount)
                        {
                            chapterListHtml = ExtractNettruyenListChapterHtml(webViewHtml);
                            chapterLinks = retriedLinks;
                            Log($"[nettruyen] Đã mở rộng danh sách chương từ {originalChapterCount} lên {retriedLinks.Count} link.");
                            originalChapterCount = retriedLinks.Count;
                        }

                        if (expectedLatestChapter <= 0 || chapterLinks.Count + 3 >= expectedLatestChapter || !pageHasViewMore)
                        {
                            break;
                        }
                    }
                }

                var finalResults = chapterLinks.OrderBy(ParseChapterNumber).Select(link => new ReaderChapterItem
                {
                    Name = chapterLabelsByLink.TryGetValue(link, out string chapterName) && !string.IsNullOrWhiteSpace(chapterName)
                        ? chapterName
                        : BuildDownloadChapterLabel(link),
                    FolderPath = link,
                    Pages = new List<ReaderPageItem>()
                }).ToList();
                if (finalResults.Count > 0)
                {
                    _downloadChapterItemCache[url] = CloneReaderChapterItems(finalResults);
                }

                return chapterLinks;
        }
    }
}
#pragma warning restore 4014

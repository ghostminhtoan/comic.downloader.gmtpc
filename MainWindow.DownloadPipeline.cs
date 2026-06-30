using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private static readonly TimeSpan BrowserSessionTtl = TimeSpan.FromMinutes(20);
        private readonly ConcurrentDictionary<string, BrowserSessionSnapshot> _browserSessionSnapshots = new ConcurrentDictionary<string, BrowserSessionSnapshot>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, object> _manifestLocks = new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        private static readonly SiteDownloadProfile[] _downloadProfiles = new[]
        {
            new SiteDownloadProfile
            {
                Id = "hentaiforce",
                HostAliases = new[] { "hentaiforce.net", "m1.hentaiforce.net" },
                BrowserSessionPreferred = false,
                ChromeFallbackPreferred = false,
                DefaultConcurrencyCap = 6,
                InterRequestDelayMs = 120,
                AllowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" },
                ChallengeMarkers = new[] { "Just a moment...", "cloudflare", "cf-challenge", "captcha" },
                RetryPolicy = new RetryPolicyProfile { MaxAttempts = 4, BaseDelayMs = 400, MaxDelayMs = 8000, BrowserChallengeNeedsSessionRefresh = false }
            },
            new SiteDownloadProfile
            {
                Id = "nhentai",
                HostAliases = new[] { "nhentai.xxx", "nhentai.net", "i.nhentai.net", "i1.nhentai.net", "i2.nhentai.net", "i3.nhentai.net", "i4.nhentai.net", "t.nhentai.net" },
                BrowserSessionPreferred = true,
                ChromeFallbackPreferred = true,
                DefaultConcurrencyCap = 4,
                InterRequestDelayMs = 180,
                AllowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp" },
                ChallengeMarkers = new[] { "Just a moment...", "cloudflare", "cf-challenge", "captcha", "Access denied" },
                RetryPolicy = new RetryPolicyProfile { MaxAttempts = 4, BaseDelayMs = 700, MaxDelayMs = 12000, BrowserChallengeNeedsSessionRefresh = true }
            },
            new SiteDownloadProfile
            {
                Id = "vi-hentai",
                HostAliases = new[] { "vi-hentai.pro" },
                BrowserSessionPreferred = true,
                ChromeFallbackPreferred = true,
                DefaultConcurrencyCap = 3,
                InterRequestDelayMs = 260,
                AllowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp" },
                ChallengeMarkers = new[] { "Just a moment...", "cloudflare", "cf-challenge", "cf-turnstile", "captcha", "xác minh" },
                RetryPolicy = new RetryPolicyProfile { MaxAttempts = 4, BaseDelayMs = 800, MaxDelayMs = 15000, BrowserChallengeNeedsSessionRefresh = true }
            },
            new SiteDownloadProfile
            {
                Id = "damconuong",
                HostAliases = new[] { "damconuong.shop" },
                BrowserSessionPreferred = false,
                ChromeFallbackPreferred = false,
                DefaultConcurrencyCap = 4,
                InterRequestDelayMs = 120,
                AllowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp" },
                ChallengeMarkers = new[] { "Just a moment...", "cloudflare", "cf-challenge", "captcha" },
                RetryPolicy = new RetryPolicyProfile { MaxAttempts = 3, BaseDelayMs = 500, MaxDelayMs = 8000, BrowserChallengeNeedsSessionRefresh = false }
            },
            new SiteDownloadProfile
            {
                Id = "hentaiera",
                HostAliases = new[] { "hentaiera.com" },
                BrowserSessionPreferred = true,
                ChromeFallbackPreferred = true,
                DefaultConcurrencyCap = 3,
                InterRequestDelayMs = 240,
                AllowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp" },
                ChallengeMarkers = new[] { "Just a moment...", "cloudflare", "cf-challenge", "captcha" },
                RetryPolicy = new RetryPolicyProfile { MaxAttempts = 4, BaseDelayMs = 750, MaxDelayMs = 12000, BrowserChallengeNeedsSessionRefresh = true }
            },
            new SiteDownloadProfile
            {
                Id = "truyenqq",
                HostAliases = new[] { "truyenqq", "truyenqqto.com", "truyenqqpro.com" },
                BrowserSessionPreferred = false,
                ChromeFallbackPreferred = false,
                DefaultConcurrencyCap = 6,
                InterRequestDelayMs = 100,
                AllowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" },
                ChallengeMarkers = new[] { "Just a moment...", "cloudflare", "cf-challenge", "captcha" },
                RetryPolicy = new RetryPolicyProfile { MaxAttempts = 4, BaseDelayMs = 600, MaxDelayMs = 10000, BrowserChallengeNeedsSessionRefresh = true }
            },
            new SiteDownloadProfile
            {
                Id = "default",
                HostAliases = new string[0],
                BrowserSessionPreferred = false,
                ChromeFallbackPreferred = false,
                DefaultConcurrencyCap = 5,
                InterRequestDelayMs = 150,
                AllowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp" },
                ChallengeMarkers = new[] { "Just a moment...", "cloudflare", "cf-challenge", "captcha" },
                RetryPolicy = new RetryPolicyProfile { MaxAttempts = 3, BaseDelayMs = 500, MaxDelayMs = 8000, BrowserChallengeNeedsSessionRefresh = false }
            }
        };

        private SiteDownloadProfile GetSiteDownloadProfile(string urlOrHost)
        {
            string host = ExtractHostOrRaw(urlOrHost);
            foreach (var profile in _downloadProfiles)
            {
                if (profile.HostAliases == null)
                {
                    continue;
                }

                foreach (var alias in profile.HostAliases)
                {
                    if (!string.IsNullOrWhiteSpace(alias) &&
                        host.IndexOf(alias, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return profile;
                    }
                }
            }

            return _downloadProfiles.Last();
        }

        private string ExtractHostOrRaw(string urlOrHost)
        {
            if (string.IsNullOrWhiteSpace(urlOrHost))
            {
                return string.Empty;
            }

            try
            {
                return new Uri(urlOrHost).Host ?? string.Empty;
            }
            catch
            {
                return urlOrHost.Trim();
            }
        }

        private int GetEffectiveConnectionLimit(SiteDownloadProfile profile, int requestedLimit)
        {
            int effective = Math.Max(1, Math.Min(requestedLimit, profile != null ? profile.DefaultConcurrencyCap : requestedLimit));
            if (profile != null && effective < requestedLimit)
            {
                Log($"[Throttle] Site '{profile.Id}' tự hạ concurrency từ {requestedLimit} xuống {effective} để tránh rate-limit.");
            }

            return effective;
        }

        private string GetDownloadManifestFilePath(string rootFolder, string siteFolder, GalleryItem item)
        {
            string safeSite = GetSafePathName(siteFolder ?? "site");
            string bookKey = GetBookIdentifier(item != null ? item.Link : null) ?? (item != null ? item.Name : null) ?? "item";
            string safeBookKey = GetSafePathName(bookKey.Replace("|", "-"));
            if (safeBookKey.Length > 120)
            {
                safeBookKey = safeBookKey.Substring(0, 120).Trim();
            }

            return Path.Combine(PortablePaths.PortableTempRoot, ".manifest", safeSite, safeBookKey + ".json");
        }

        private DownloadManifest LoadOrCreateManifest(string rootFolder, string siteFolder, GalleryItem item, int expectedPageCount, SiteDownloadProfile profile)
        {
            string manifestPath = GetDownloadManifestFilePath(rootFolder, siteFolder, item);
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath));
            object manifestLock = _manifestLocks.GetOrAdd(manifestPath, _ => new object());

            lock (manifestLock)
            {
                DownloadManifest manifest = LoadManifestFromFile(manifestPath);
                if (manifest == null)
                {
                    manifest = new DownloadManifest
                    {
                        GalleryName = item != null ? item.Name : string.Empty,
                        GalleryUrl = item != null ? item.Link : string.Empty,
                        SiteProfileId = profile != null ? profile.Id : "default",
                        ExpectedPageCount = expectedPageCount,
                        UpdatedUtc = DateTime.UtcNow
                    };
                }
                else
                {
                    manifest.GalleryName = item != null ? item.Name : manifest.GalleryName;
                    manifest.GalleryUrl = item != null ? item.Link : manifest.GalleryUrl;
                    manifest.SiteProfileId = profile != null ? profile.Id : manifest.SiteProfileId;
                    manifest.ExpectedPageCount = Math.Max(expectedPageCount, manifest.ExpectedPageCount);
                    manifest.UpdatedUtc = DateTime.UtcNow;
                }

                SaveManifestToFile(manifestPath, manifest);
                return manifest;
            }
        }

        private DownloadManifest LoadManifestFromFile(string manifestPath)
        {
            if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            {
                return null;
            }

            try
            {
                using (var stream = File.OpenRead(manifestPath))
                {
                    var serializer = new DataContractJsonSerializer(typeof(DownloadManifest));
                    return serializer.ReadObject(stream) as DownloadManifest;
                }
            }
            catch (Exception ex)
            {
                Log($"[Manifest] Không thể đọc manifest '{manifestPath}': {ex.Message}");
                return null;
            }
        }

        private void SaveManifestToFile(string manifestPath, DownloadManifest manifest)
        {
            if (string.IsNullOrWhiteSpace(manifestPath) || manifest == null)
            {
                return;
            }

            try
            {
                DateTime nowUtc = DateTime.UtcNow;
                if (_processWriteTimes.TryGetValue(manifestPath, out DateTime lastWriteUtc) &&
                    (nowUtc - lastWriteUtc).TotalMilliseconds < 1000)
                {
                    return;
                }

                _processWriteTimes[manifestPath] = nowUtc;
                manifest.UpdatedUtc = DateTime.UtcNow;
                Directory.CreateDirectory(Path.GetDirectoryName(manifestPath));
                using (var stream = File.Create(manifestPath))
                {
                    var serializer = new DataContractJsonSerializer(typeof(DownloadManifest));
                    serializer.WriteObject(stream, manifest);
                }
            }
            catch (Exception ex)
            {
                Log($"[Manifest] Không thể ghi manifest '{manifestPath}': {ex.Message}");
            }
        }

        private PageDownloadRecord GetOrCreatePageRecord(DownloadManifest manifest, int pageNumber)
        {
            if (manifest == null)
            {
                return null;
            }

            PageDownloadRecord record = manifest.Pages.FirstOrDefault(page => page.PageNumber == pageNumber);
            if (record == null)
            {
                record = new PageDownloadRecord
                {
                    PageNumber = pageNumber,
                    State = DownloadPageState.Pending,
                    UpdatedUtc = DateTime.UtcNow
                };
                manifest.Pages.Add(record);
            }

            return record;
        }

        private void UpdateManifestPageRecord(string manifestPath, DownloadManifest manifest, int pageNumber, Action<PageDownloadRecord> updateAction)
        {
            if (manifest == null || string.IsNullOrWhiteSpace(manifestPath))
            {
                return;
            }

            object manifestLock = _manifestLocks.GetOrAdd(manifestPath, _ => new object());
            lock (manifestLock)
            {
                var record = GetOrCreatePageRecord(manifest, pageNumber);
                updateAction(record);
                record.UpdatedUtc = DateTime.UtcNow;
                manifest.UpdatedUtc = DateTime.UtcNow;
                SaveManifestToFile(manifestPath, manifest);
            }
        }

        private void ReconcileManifestForFolder(string folderPath, DownloadManifest manifest)
        {
            if (manifest == null || string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                return;
            }

            foreach (string file in Directory.GetFiles(folderPath))
            {
                int pageNumber = ExtractPageNumberFromFilename(Path.GetFileNameWithoutExtension(file));
                if (pageNumber < 0)
                {
                    continue;
                }

                var record = GetOrCreatePageRecord(manifest, pageNumber);
                record.SavedRelativePath = Path.GetFileName(file);
                record.FileSize = new FileInfo(file).Length;
                record.ActualExtension = Path.GetExtension(file);
                record.Verified = record.FileSize > 1024;
                record.State = record.Verified ? DownloadPageState.Verified : DownloadPageState.Failed;
                record.UpdatedUtc = DateTime.UtcNow;
            }
        }

        private bool ContainsChallengeMarker(string text, SiteDownloadProfile profile)
        {
            if (string.IsNullOrWhiteSpace(text) || profile == null || profile.ChallengeMarkers == null)
            {
                return false;
            }

            foreach (string marker in profile.ChallengeMarkers)
            {
                if (!string.IsNullOrWhiteSpace(marker) &&
                    text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsHttpStatusChallenge(HttpStatusCode statusCode)
        {
            return statusCode == HttpStatusCode.Forbidden ||
                   statusCode == HttpStatusCode.ServiceUnavailable ||
                   statusCode == (HttpStatusCode)429;
        }

        private BrowserSessionSnapshot GetCachedBrowserSession(string url)
        {
            string host = ExtractHostOrRaw(url);
            BrowserSessionSnapshot snapshot;
            if (_browserSessionSnapshots.TryGetValue(host, out snapshot) && snapshot != null && !snapshot.IsExpired(BrowserSessionTtl))
            {
                return snapshot;
            }

            return null;
        }

        private void CacheBrowserSession(BrowserSessionSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.SourceHost))
            {
                return;
            }

            _browserSessionSnapshots[snapshot.SourceHost] = snapshot;
        }

        private void ApplyBrowserSessionSnapshot(BrowserSessionSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.UserAgent))
            {
                _httpClient.DefaultRequestHeaders.UserAgent.Clear();
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(snapshot.UserAgent);
            }

            foreach (var cookie in snapshot.Cookies ?? new List<BrowserSessionCookie>())
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(cookie.Domain) || string.IsNullOrWhiteSpace(cookie.Name))
                    {
                        continue;
                    }

                    string domain = cookie.Domain.StartsWith(".") ? cookie.Domain : "." + cookie.Domain;
                    var netCookie = new System.Net.Cookie(cookie.Name, cookie.Value ?? string.Empty, string.IsNullOrWhiteSpace(cookie.Path) ? "/" : cookie.Path, domain)
                    {
                        Secure = cookie.Secure,
                        HttpOnly = cookie.HttpOnly
                    };

                    if (cookie.ExpiresUtc.HasValue)
                    {
                        netCookie.Expires = cookie.ExpiresUtc.Value.ToLocalTime();
                    }

                    Uri resolvedUri = null;
                    if (!string.IsNullOrWhiteSpace(snapshot.ResolvedUrl))
                    {
                        Uri.TryCreate(snapshot.ResolvedUrl, UriKind.Absolute, out resolvedUri);
                    }
                    if (resolvedUri == null)
                    {
                        Uri.TryCreate("https://" + cookie.Domain.TrimStart('.'), UriKind.Absolute, out resolvedUri);
                    }

                    if (resolvedUri != null)
                    {
                        _cookieContainer.Add(resolvedUri, netCookie);
                    }
                }
                catch
                {
                }
            }
        }

        private async Task<BrowserSessionSnapshot> AcquireBrowserSessionAsync(string url, SiteDownloadProfile profile, CancellationToken token)
        {
            BrowserSessionSnapshot cached = GetCachedBrowserSession(url);
            if (cached != null)
            {
                ApplyBrowserSessionSnapshot(cached);
                return cached;
            }

            BrowserSessionSnapshot snapshot = await AcquireWebView2SessionAsync(url);
            if (snapshot != null)
            {
                CacheBrowserSession(snapshot);
                ApplyBrowserSessionSnapshot(snapshot);
                return snapshot;
            }

            if (profile != null && profile.ChromeFallbackPreferred)
            {
                snapshot = await AcquireChromeFallbackSessionAsync(url, token);
                if (snapshot != null)
                {
                    CacheBrowserSession(snapshot);
                    ApplyBrowserSessionSnapshot(snapshot);
                }
            }

            return snapshot;
        }

        private async Task<BrowserSessionSnapshot> AcquireWebView2SessionAsync(string url)
        {
            BrowserSessionSnapshot snapshot = null;
            await await Dispatcher.InvokeAsync(async () =>
            {
                var captchaWin = CreateCaptchaWindow(url, autoDeleteCookiesOnLoad: true, headlessAutomation: _lightNovelAutoFocusEnabled);
                captchaWin.Owner = this;

                if (await captchaWin.ShowNonBlockingAsync())
                {
                    snapshot = BuildBrowserSessionSnapshot(
                        captchaWin.ResolvedUri ?? new Uri(url),
                        captchaWin.UserAgent,
                        captchaWin.ResolvedCookies,
                        BrowserSessionEngine.WebView2);
                }
            });

            return snapshot;
        }

        private BrowserSessionSnapshot BuildBrowserSessionSnapshot(Uri resolvedUri, string userAgent, CookieContainer cookies, BrowserSessionEngine engine)
        {
            var snapshot = new BrowserSessionSnapshot
            {
                SourceHost = resolvedUri != null ? resolvedUri.Host : string.Empty,
                ResolvedUrl = resolvedUri != null ? resolvedUri.AbsoluteUri : string.Empty,
                UserAgent = string.IsNullOrWhiteSpace(userAgent) ? _defaultUserAgent : userAgent,
                Engine = engine,
                AcquiredUtc = DateTime.UtcNow
            };

            if (resolvedUri != null && cookies != null)
            {
                foreach (System.Net.Cookie cookie in cookies.GetCookies(resolvedUri))
                {
                    snapshot.Cookies.Add(new BrowserSessionCookie
                    {
                        Name = cookie.Name,
                        Value = cookie.Value,
                        Domain = cookie.Domain,
                        Path = cookie.Path,
                        ExpiresUtc = cookie.Expires == DateTime.MinValue ? (DateTime?)null : cookie.Expires.ToUniversalTime(),
                        Secure = cookie.Secure,
                        HttpOnly = cookie.HttpOnly
                    });
                }
            }

            return snapshot;
        }

        private async Task<BrowserSessionSnapshot> AcquireChromeFallbackSessionAsync(string url, CancellationToken token)
        {
            return await Task.Run(() =>
            {
                try
                {
                    string chromeProfileRoot = Path.Combine(PortablePaths.PortableDataRoot, "chrome-fallback");
                    Directory.CreateDirectory(chromeProfileRoot);

                    var options = new ChromeOptions();
                    string chromeBinary = TryFindChromeExecutable();
                    if (!string.IsNullOrWhiteSpace(chromeBinary))
                    {
                        options.BinaryLocation = chromeBinary;
                    }

                    options.AddArgument("--disable-blink-features=AutomationControlled");
                    options.AddArgument("--disable-popup-blocking");
                    options.AddArgument("--no-first-run");
                    options.AddArgument("--no-default-browser-check");
                    options.AddArgument("--user-data-dir=" + chromeProfileRoot);

                    ChromeDriverService service = ChromeDriverService.CreateDefaultService();
                    service.HideCommandPromptWindow = true;
                    service.SuppressInitialDiagnosticInformation = true;

                    using (var driver = new ChromeDriver(service, options, TimeSpan.FromMinutes(2)))
                    {
                        driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(60);
                        driver.Navigate().GoToUrl(url);

                        DateTime deadline = DateTime.UtcNow.AddMinutes(2);
                        SiteDownloadProfile profile = GetSiteDownloadProfile(url);
                        while (DateTime.UtcNow < deadline)
                        {
                            token.ThrowIfCancellationRequested();
                            Thread.Sleep(1200);

                            string title = string.Empty;
                            string source = string.Empty;
                            try
                            {
                                title = driver.Title ?? string.Empty;
                            }
                            catch
                            {
                            }

                            try
                            {
                                source = driver.PageSource ?? string.Empty;
                            }
                            catch
                            {
                            }

                            if (!ContainsChallengeMarker(title + Environment.NewLine + source, profile))
                            {
                                break;
                            }
                        }

                        Uri resolvedUri = null;
                        try
                        {
                            resolvedUri = new Uri(driver.Url);
                        }
                        catch
                        {
                            resolvedUri = new Uri(url);
                        }

                        string userAgent = _defaultUserAgent;
                        try
                        {
                            userAgent = Convert.ToString(((IJavaScriptExecutor)driver).ExecuteScript("return navigator.userAgent;")) ?? _defaultUserAgent;
                        }
                        catch
                        {
                        }

                        var snapshot = new BrowserSessionSnapshot
                        {
                            SourceHost = resolvedUri.Host,
                            ResolvedUrl = resolvedUri.AbsoluteUri,
                            UserAgent = userAgent,
                            Engine = BrowserSessionEngine.ChromeFallback,
                            AcquiredUtc = DateTime.UtcNow
                        };

                        foreach (var cookie in driver.Manage().Cookies.AllCookies)
                        {
                            snapshot.Cookies.Add(new BrowserSessionCookie
                            {
                                Name = cookie.Name,
                                Value = cookie.Value,
                                Domain = cookie.Domain,
                                Path = cookie.Path,
                                ExpiresUtc = cookie.Expiry.HasValue ? cookie.Expiry.Value.ToUniversalTime() : (DateTime?)null,
                                Secure = cookie.Secure,
                                HttpOnly = cookie.IsHttpOnly
                            });
                        }

                        Log($"[Browser Session] Chrome fallback lấy session thành công cho {resolvedUri.Host}.");
                        return snapshot;
                    }
                }
                catch (Exception ex)
                {
                    Log($"[Browser Session] Chrome fallback thất bại: {ex.Message}");
                    return null;
                }
            }, token);
        }

        private string TryFindChromeExecutable()
        {
            string[] candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")
            };

            foreach (string candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private async Task<string> GetHtmlWithSiteSessionAsync(string url, SiteDownloadProfile profile, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (profile != null && profile.BrowserSessionPreferred)
            {
                BrowserSessionSnapshot cached = GetCachedBrowserSession(url);
                if (cached != null)
                {
                    ApplyBrowserSessionSnapshot(cached);
                }
            }

            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token))
                {
                    string html = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode && !ContainsChallengeMarker(html, profile))
                    {
                        return html;
                    }
                }
            }

            BrowserSessionSnapshot snapshot = await AcquireBrowserSessionAsync(url, profile, token);
            if (snapshot == null)
            {
                throw new HttpRequestException("Không lấy được browser session hợp lệ.");
            }

            using (var retryRequest = new HttpRequestMessage(HttpMethod.Get, url))
            using (var retryResponse = await _httpClient.SendAsync(retryRequest, HttpCompletionOption.ResponseHeadersRead, token))
            {
                string html = await retryResponse.Content.ReadAsStringAsync();
                if (!retryResponse.IsSuccessStatusCode)
                {
                    retryResponse.EnsureSuccessStatusCode();
                }

                if (ContainsChallengeMarker(html, profile))
                {
                    throw new HttpRequestException("Trang vẫn còn challenge sau khi refresh browser session.");
                }

                return html;
            }
        }

        private async Task<DownloadFileResult> DownloadUrlToFileWithMetadataAsync(string url, string referer, string filePath, CancellationToken token, SiteDownloadProfile profile)
        {
            if (File.Exists(filePath) && new FileInfo(filePath).Length > 1024)
            {
                string existingExt = DetectImageExtensionFromFile(filePath);
                return new DownloadFileResult
                {
                    FinalPath = filePath,
                    ActualExtension = existingExt,
                    FileSize = new FileInfo(filePath).Length
                };
            }

            string parentFolder = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(parentFolder))
            {
                Directory.CreateDirectory(parentFolder);
            }

            RetryPolicyProfile retryPolicy = profile != null ? profile.RetryPolicy : null;
            int maxAttempts = retryPolicy != null ? Math.Max(1, retryPolicy.MaxAttempts) : 3;
            int delayMs = retryPolicy != null ? Math.Max(100, retryPolicy.BaseDelayMs) : 500;
            int maxDelayMs = retryPolicy != null ? Math.Max(delayMs, retryPolicy.MaxDelayMs) : 8000;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (profile != null && profile.InterRequestDelayMs > 0)
                    {
                        await Task.Delay(profile.InterRequestDelayMs, token);
                    }

                    using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                    {
                        if (!string.IsNullOrEmpty(referer))
                        {
                            request.Headers.Referrer = new Uri(referer);
                        }

                        using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token))
                        {
                            if (IsHttpStatusChallenge(response.StatusCode) &&
                                profile != null &&
                                profile.RetryPolicy != null &&
                                profile.RetryPolicy.BrowserChallengeNeedsSessionRefresh)
                            {
                                await AcquireBrowserSessionAsync(!string.IsNullOrWhiteSpace(referer) ? referer : url, profile, token);
                                if (attempt < maxAttempts)
                                {
                                    await Task.Delay(delayMs, token);
                                    delayMs = Math.Min(delayMs * 2, maxDelayMs);
                                    continue;
                                }
                            }

                            if ((int)response.StatusCode == 429 && attempt < maxAttempts)
                            {
                                await Task.Delay(delayMs, token);
                                delayMs = Math.Min(delayMs * 2, maxDelayMs);
                                continue;
                            }

                            response.EnsureSuccessStatusCode();

                            using (var contentStream = await response.Content.ReadAsStreamAsync())
                            using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                            {
                                await contentStream.CopyToAsync(fileStream, 81920, token);
                            }

                            var fileInfo = new FileInfo(filePath);
                            if (fileInfo.Length <= 1024)
                            {
                                throw new IOException("Downloaded file too small.");
                            }

                            string mediaType = response.Content.Headers.ContentType != null
                                ? response.Content.Headers.ContentType.MediaType
                                : null;
                            string detectedExt = NormalizeExtensionFromContentType(mediaType);
                            if (string.IsNullOrWhiteSpace(detectedExt))
                            {
                                detectedExt = DetectImageExtensionFromFile(filePath);
                            }

                            string finalPath = filePath;
                            if (!string.IsNullOrWhiteSpace(detectedExt) &&
                                !string.Equals(Path.GetExtension(filePath), detectedExt, StringComparison.OrdinalIgnoreCase))
                            {
                                finalPath = Path.ChangeExtension(filePath, detectedExt);
                                if (!string.Equals(finalPath, filePath, StringComparison.OrdinalIgnoreCase))
                                {
                                    if (File.Exists(finalPath))
                                    {
                                        File.Delete(finalPath);
                                    }

                                    File.Move(filePath, finalPath);
                                }
                            }

                            return new DownloadFileResult
                            {
                                FinalPath = finalPath,
                                ActualExtension = detectedExt,
                                FileSize = new FileInfo(finalPath).Length,
                                MediaType = mediaType
                            };
                        }
                    }
                }
                catch (HttpRequestException ex) when (attempt < maxAttempts)
                {
                    Log($"[Download Retry] Lỗi mạng, retry {attempt}/{maxAttempts} sau {delayMs}ms: {ex.Message}");
                    await Task.Delay(delayMs, token);
                    delayMs = Math.Min(delayMs * 2, maxDelayMs);
                }
                catch (TaskCanceledException) when (!token.IsCancellationRequested && attempt < maxAttempts)
                {
                    Log($"[Download Retry] Timeout, retry {attempt}/{maxAttempts} sau {delayMs}ms.");
                    await Task.Delay(delayMs, token);
                    delayMs = Math.Min(delayMs * 2, maxDelayMs);
                }
                catch (IOException ex) when (attempt < maxAttempts)
                {
                    Log($"[Download Retry] File hỏng hoặc quá nhỏ, retry {attempt}/{maxAttempts}: {ex.Message}");
                    TryDeleteFile(filePath);
                    await Task.Delay(delayMs, token);
                    delayMs = Math.Min(delayMs * 2, maxDelayMs);
                }
            }

            throw new Exception("Không thể tải file sau khi đã retry hết mức: " + url);
        }

        private void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private string NormalizeExtensionFromContentType(string mediaType)
        {
            switch ((mediaType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "image/jpeg":
                case "image/jpg":
                    return ".jpg";
                case "image/png":
                    return ".png";
                case "image/webp":
                    return ".webp";
                case "image/gif":
                    return ".gif";
                case "image/bmp":
                    return ".bmp";
                default:
                    return null;
            }
        }

        private string DetectImageExtensionFromFile(string filePath)
        {
            try
            {
                byte[] header = new byte[16];
                using (var stream = File.OpenRead(filePath))
                {
                    int read = stream.Read(header, 0, header.Length);
                    if (read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
                    {
                        return ".jpg";
                    }

                    if (read >= 8 &&
                        header[0] == 0x89 &&
                        header[1] == 0x50 &&
                        header[2] == 0x4E &&
                        header[3] == 0x47)
                    {
                        return ".png";
                    }

                    if (read >= 6 &&
                        header[0] == 0x47 &&
                        header[1] == 0x49 &&
                        header[2] == 0x46)
                    {
                        return ".gif";
                    }

                    if (read >= 2 &&
                        header[0] == 0x42 &&
                        header[1] == 0x4D)
                    {
                        return ".bmp";
                    }

                    if (read >= 12 &&
                        header[0] == 0x52 &&
                        header[1] == 0x49 &&
                        header[2] == 0x46 &&
                        header[3] == 0x46 &&
                        header[8] == 0x57 &&
                        header[9] == 0x45 &&
                        header[10] == 0x42 &&
                        header[11] == 0x50)
                    {
                        return ".webp";
                    }
                }
            }
            catch
            {
            }

            return Path.GetExtension(filePath);
        }
    }
}

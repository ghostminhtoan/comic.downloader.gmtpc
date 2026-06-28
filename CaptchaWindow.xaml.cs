using System;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace get_link_manga
{
    public enum CaptchaType
    {
        General,
        Special,
        WatchMore
    }

    public partial class CaptchaWindow : Window
    {
        private readonly WebView2 webView = new WebView2();
        public CookieContainer ResolvedCookies { get; private set; } = new CookieContainer();
        public Uri ResolvedUri { get; private set; }
        public string UserAgent { get; private set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
        public string ResolvedHtml { get; private set; }
        public bool WasCompleted { get; private set; }
        private readonly string _targetUrl;
        private readonly CaptchaType _captchaType;
        private readonly bool _autoDeleteCookiesOnLoad;
        private readonly bool _headlessAutomation;
        private readonly DateTime _windowOpenedAt = DateTime.Now;
        private DateTime _captchaBypassStartTime = DateTime.MinValue;
        private DateTime _lastCaptchaKeyboardAttempt = DateTime.MinValue;
        private DateTime _challengeDetectedAt = DateTime.MinValue;
        public bool BypassWasNeeded { get; private set; }
        public double WindowElapsedSeconds => (DateTime.Now - _windowOpenedAt).TotalSeconds;
        private bool _userInteracted = false;
        private bool _isSendingBypassKeys = false;

        public CaptchaWindow(string targetUrl, CaptchaType captchaType, bool autoDeleteCookiesOnLoad = false, bool headlessAutomation = false)
        {
            InitializeComponent();
            if (webViewHost != null)
            {
                webViewHost.Children.Add(webView);
            }
            _targetUrl = targetUrl;
            _captchaType = captchaType;
            _autoDeleteCookiesOnLoad = autoDeleteCookiesOnLoad;
            _headlessAutomation = headlessAutomation;
            ApplyLanguage(GetIsVietnameseUiEnabled());

            if (_headlessAutomation)
            {
                ConfigureHeadlessWindow();
            }

            try
            {
                var uri = new Uri(targetUrl);
                this.Title = $"{GetCaptchaWindowTitlePrefix()} - {uri.Host.ToUpper()}";
            }
            catch
            {
                this.Title = GetCaptchaWindowTitlePrefix();
            }
            Loaded += CaptchaWindow_Loaded;
            PreviewMouseDown += Window_PreviewMouseDown;
            PreviewKeyDown += Window_PreviewKeyDown;
        }

        private void Window_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_isSendingBypassKeys)
            {
                _userInteracted = true;
            }
        }

        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!_isSendingBypassKeys)
            {
                _userInteracted = true;
            }
        }

        public Task<bool> ShowNonBlockingAsync()
        {
            var tcs = new TaskCompletionSource<bool>();
            Closed += OnClosed;
            Show();
            return tcs.Task;

            void OnClosed(object sender, EventArgs e)
            {
                Closed -= OnClosed;
                tcs.TrySetResult(WasCompleted);
            }
        }

        private void CloseWithResult(bool completed)
        {
            WasCompleted = completed;
            Close();
        }

        private void ConfigureHeadlessWindow()
        {
            ShowInTaskbar = false;
            ShowActivated = false;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Width = 1;
            Height = 1;
            Left = -10000;
            Top = -10000;
            Opacity = 0;

            if (txtCaptchaHeader != null)
            {
                txtCaptchaHeader.Visibility = Visibility.Collapsed;
            }
            if (txtCaptchaDescription != null)
            {
                txtCaptchaDescription.Visibility = Visibility.Collapsed;
            }
            if (btnDeleteCookies != null)
            {
                btnDeleteCookies.Visibility = Visibility.Collapsed;
            }
            if (btnDone != null)
            {
                btnDone.Visibility = Visibility.Collapsed;
            }
            if (btnCancel != null)
            {
                btnCancel.Visibility = Visibility.Collapsed;
            }
        }

        private bool GetIsVietnameseUiEnabled()
        {
            try
            {
                if (Application.Current?.Properties["IsVietnameseUi"] is bool isVietnamese)
                {
                    return isVietnamese;
                }
            }
            catch
            {
            }

            return false;
        }

        private string GetCaptchaWindowTitlePrefix()
        {
            return GetIsVietnameseUiEnabled() ? "Vượt Cloudflare Captcha" : "Cloudflare Captcha";
        }

        private void ApplyLanguage(bool isVietnamese)
        {
            if (isVietnamese)
            {
                if (txtCaptchaHeader != null) txtCaptchaHeader.Text = "VƯỢT CLOUDFLARE CAPTCHA";
                if (txtCaptchaDescription != null) txtCaptchaDescription.Text = "VUI LÒNG HOÀN THÀNH THỬ THÁCH TRONG TRÌNH DUYỆT BÊN DƯỚI. KHI TRANG TẢI XONG, NHẤN 'ĐÃ XONG'";
                if (btnDeleteCookies != null) btnDeleteCookies.Content = "XÓA COOKIE";
                if (btnDone != null) btnDone.Content = "ĐÃ XONG CAPTCHA";
                if (btnCancel != null) btnCancel.Content = "HỦY";
            }
            else
            {
                if (txtCaptchaHeader != null) txtCaptchaHeader.Text = "CLOUDFLARE CAPTCHA BYPASS";
                if (txtCaptchaDescription != null) txtCaptchaDescription.Text = "PLEASE COMPLETE THE CHALLENGE IN THE BROWSER BELOW. WHEN THE PAGE FINISHES LOADING, CLICK 'DONE'";
                if (btnDeleteCookies != null) btnDeleteCookies.Content = "DELETE COOKIES";
                if (btnDone != null) btnDone.Content = "CAPTCHA DONE";
                if (btnCancel != null) btnCancel.Content = "CANCEL";
            }
        }

        private static bool UrlContainsHost(string url, params string[] patterns)
        {
            if (string.IsNullOrWhiteSpace(url) || patterns == null)
            {
                return false;
            }

            for (int i = 0; i < patterns.Length; i++)
            {
                string pattern = patterns[i];
                if (!string.IsNullOrWhiteSpace(pattern) &&
                    url.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsNettruyenUrl(string url)
        {
            return UrlContainsHost(url, "nettruyen");
        }

        private static bool IsTruyenqqChallengeUrl(string url)
        {
            return UrlContainsHost(url, "truyenqq", "truyenqqko");
        }

        private static bool ShouldUseVerifyFindSequence(string url)
        {
            return IsTruyenqqChallengeUrl(url);
        }

        private double GetInitialCaptchaAttemptDelaySeconds(string url)
        {
            return 5.0; // Giảm xuống 5 giây để bắt đầu bypass nhanh hơn
        }

        private double GetRepeatCaptchaAttemptDelaySeconds(string url)
        {
            return 4.0; // Lặp lại mỗi 4 giây
        }

        private string GetCaptchaFindKeyword(string url)
        {
            return ShouldUseVerifyFindSequence(url) ? "verify" : "human";
        }

        private async Task<bool> TryAutoSolveTruyenqqChallengeAsync(string url)
        {
            if (!IsTruyenqqChallengeUrl(url))
            {
                return false;
            }

            try
            {
                return await await Dispatcher.InvokeAsync(async () =>
                {
                    if (webView.CoreWebView2 == null)
                    {
                        return false;
                    }

                    string script = @"
                        (function() {
                            function isVisible(el) {
                                if (!el) return false;
                                var style = window.getComputedStyle(el);
                                if (!style || style.display === 'none' || style.visibility === 'hidden' || style.pointerEvents === 'none') {
                                    return false;
                                }
                                var rect = el.getBoundingClientRect();
                                return rect.width > 0 && rect.height > 0;
                            }

                            function clickElement(el) {
                                if (!el || !isVisible(el)) return false;
                                try { el.click(); return true; } catch (e) {}
                                try { el.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, view: window })); return true; } catch (e2) {}
                                return false;
                            }

                            var needles = ['verify', 'human', 'captcha', 'continue', 'next', 'xac minh', 'xác minh', 'robot', 'submit'];
                            var elements = document.querySelectorAll('button, a, input, label, div, span');
                            for (var i = 0; i < elements.length; i++) {
                                var el = elements[i];
                                var text = ((el.innerText || el.textContent || el.value || '') + '').trim().toLowerCase();
                                if (!text || !isVisible(el)) continue;
                                for (var n = 0; n < needles.length; n++) {
                                    if (text.indexOf(needles[n]) !== -1 && clickElement(el)) {
                                        return 'clicked';
                                    }
                                }
                            }

                            var checkboxes = document.querySelectorAll('input[type=""checkbox""], input[type=""radio""]');
                            for (var j = 0; j < checkboxes.length; j++) {
                                if (clickElement(checkboxes[j])) {
                                    return 'clicked';
                                }
                            }

                            return 'idle';
                        })();";

                    string result = await webView.CoreWebView2.ExecuteScriptAsync(script);
                    return string.Equals((result ?? string.Empty).Trim('"'), "clicked", StringComparison.OrdinalIgnoreCase);
                });
            }
            catch
            {
                return false;
            }
        }

        private async Task SendCaptchaKeyboardBypassAsync(string url)
        {
            _isSendingBypassKeys = true;
            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        this.Activate();
                        webView.Focus();
                        System.Windows.Input.Keyboard.Focus(webView);
                    }
                    catch
                    {
                    }
                });
                await Task.Delay(300);

                string findKeyword = GetCaptchaFindKeyword(url);

                System.Windows.Forms.SendKeys.SendWait("^f");
                await Task.Delay(500);

                System.Windows.Forms.SendKeys.SendWait(findKeyword);
                await Task.Delay(500);

                System.Windows.Forms.SendKeys.SendWait("{ESCAPE}");
                await Task.Delay(300);

                System.Windows.Forms.SendKeys.SendWait("+{TAB}");
                await Task.Delay(300);

                System.Windows.Forms.SendKeys.SendWait(" ");
            }
            finally
            {
                _isSendingBypassKeys = false;
            }
        }

        private async Task<bool> PageContainsKeywordAsync(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return false;
            }

            try
            {
                return await await Dispatcher.InvokeAsync(async () =>
                {
                    if (webView.CoreWebView2 == null)
                    {
                        return false;
                    }

                    string escapedKeyword = keyword.Replace("\\", "\\\\").Replace("'", "\\'");
                    string script = @"
                        (function() {
                            var keyword = '" + escapedKeyword + @"'.toLowerCase();
                            var bodyText = (document.body && document.body.innerText || '').toLowerCase();
                            var html = (document.documentElement && document.documentElement.outerHTML || '').toLowerCase();
                            return bodyText.indexOf(keyword) !== -1 || html.indexOf(keyword) !== -1;
                        })();";

                    string result = await webView.CoreWebView2.ExecuteScriptAsync(script);
                    return string.Equals(result?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
                });
            }
            catch
            {
                return false;
            }
        }

        private string GetWebView2UserDataFolder()
        {
            string domain = "general";
            try
            {
                if (!string.IsNullOrEmpty(_targetUrl))
                {
                    var uri = new Uri(_targetUrl);
                    string host = uri.Host.ToLower();
                    if (host.Contains("truyenqq")) domain = "truyenqq";
                    else if (host.Contains("nettruyen")) domain = "nettruyen";
                    else if (host.Contains("vi-hentai") || host.Contains("hentaivn")) domain = "hentaivn";
                    else if (host.Contains("hentai2read")) domain = "hentai2read";
                    else if (host.Contains("daomeoden")) domain = "daomeoden";
                    else
                    {
                        var parts = host.Split('.');
                        if (parts.Length >= 2)
                        {
                            domain = parts[parts.Length - 2];
                        }
                        else
                        {
                            domain = host;
                        }
                    }
                }
            }
            catch {}

            return System.IO.Path.Combine(PortablePaths.WebView2RuntimeRoot, domain);
        }

        private async void CaptchaWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                string browserArgs = "--disable-extensions --disable-component-extensions-with-background-pages --disable-background-networking --disable-sync --disable-default-apps --no-first-run --disable-features=msSmartScreenProtection,RendererCodeIntegrity";
                var env = await CoreWebView2Environment.CreateAsync(
                    null,
                    GetWebView2UserDataFolder(),
                    new CoreWebView2EnvironmentOptions(browserArgs));
                await webView.EnsureCoreWebView2Async(env);

                if (webView.CoreWebView2 != null)
                {
                    webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                    webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                    webView.CoreWebView2.Settings.IsZoomControlEnabled = true;
                    webView.CoreWebView2.Settings.UserAgent = UserAgent;
                }

                webView.Source = new Uri(_targetUrl);

                if (_autoDeleteCookiesOnLoad)
                {
                    await DeleteCookiesAndReloadAsync(showMessage: false);
                }

                // Start auto-bypass detection loop
                _ = AutoDetectBypassAsync();
            }
            catch (Exception ex)
            {
                if (!_headlessAutomation)
                {
                    MessageBox.Show($"Lỗi khởi tạo trình duyệt WebView2: {ex.Message}\n\nHãy đảm bảo bạn đã cài đặt WebView2 Runtime trên hệ thống.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                CloseWithResult(false);
            }
        }

        private async Task AutoDetectBypassAsync()
        {
            DateTime nettruyenChaptersWaitStartTime = DateTime.MinValue;
            while (true)
            {
                await Task.Delay(1000);
                
                bool isReady = false;
                Dispatcher.Invoke(() => isReady = webView.CoreWebView2 != null);
                if (!isReady) continue;

                try
                {
                    string url = "";
                    string title = "";
                    Dispatcher.Invoke(() =>
                    {
                        url = webView.Source?.ToString() ?? "";
                        title = webView.CoreWebView2.DocumentTitle ?? "";
                    });

                    // Execute JS check to see if we've successfully loaded the page content without cloudflare block
                    string jsCheck = @"
                        (function() {
                            var html = document.documentElement.outerHTML || '';
                            if (html.indexOf('cf-challenge') !== -1 || 
                                html.indexOf('cf-turnstile') !== -1 || 
                                html.indexOf('Turnstile') !== -1 || 
                                html.indexOf('Just a moment...') !== -1 ||
                                html.indexOf('Performing security verification') !== -1 ||
                                html.indexOf('thực hiện xác minh bảo mật') !== -1 ||
                                html.indexOf('xác minh bạn không phải là bot') !== -1) {
                                return 'challenge';
                            }
                            if (document.getElementById('cf-challenge-running') || 
                                document.getElementById('challenge-form') || 
                                html.indexOf('challenge-platform') !== -1) {
                                return 'challenge';
                            }
                            return 'ok';
                        })()";

                    string result = await await Dispatcher.InvokeAsync(async () =>
                    {
                        try
                        {
                            return await webView.CoreWebView2.ExecuteScriptAsync(jsCheck);
                        }
                        catch
                        {
                            return "challenge";
                        }
                    });

                    if (result != null && result.Trim('"') == "ok")
                    {
                        bool challengeWasReal = BypassWasNeeded ||
                            (_challengeDetectedAt != DateTime.MinValue && (DateTime.Now - _challengeDetectedAt).TotalSeconds >= 2.0);
                        BypassWasNeeded = challengeWasReal;

                        if (!title.Contains("Just a moment") && !title.Contains("Cloudflare"))
                        {
                            bool shouldDelay = false;
                            if (_captchaType == CaptchaType.WatchMore)
                            {
                                // Find and click "Xem thêm" by text content (CSS selectors are unreliable across nettruyen domains)
                                string processChaptersJs = @"
                                     (function() {
                                         function getChapterLinks() {
                                             var html = document.documentElement.outerHTML || '';
                                             var matches = html.match(/\/(?:chuong|chap|chapter|c|chuong-tranh|chuong-doc)-\d+(?:\.\d+)?(?:\/|\s|""|'|\?|$)/gi);
                                             return matches ? matches.length : 0;
                                         }
                                         
                                         if (window.viewMoreClicked) {
                                             var elapsed = Date.now() - window.viewMoreClickedTime;
                                             var chapterCount = getChapterLinks();
                                             var baseline = window.viewMoreChapterBaseline || 0;
                                             if (chapterCount > baseline) {
                                                 return 'ready';
                                             }
                                             return elapsed < 12000 ? 'waiting' : (chapterCount > 0 ? 'ready' : 'waiting');
                                         }
                                         
                                         // Prioritize the correct chapter-list view-more button
                                         var xemThem = document.querySelector('.list-chapter .view-more') ||
                                                       document.querySelector('#nt_listchapter .view-more') ||
                                                       document.querySelector('.view-more:not(.morelink)');
                                         
                                         if (!xemThem) {
                                             var allEls = document.querySelectorAll('a, button, span, div');
                                             for (var i = 0; i < allEls.length; i++) {
                                                 var el = allEls[i];
                                                 // Exclude description expand links
                                                 if (el.classList.contains('morelink') || el.closest('.shortened') || el.closest('.detail-content')) {
                                                     continue;
                                                 }
                                                var txt = (el.textContent || '').trim();
                                                 if (/^\+?\s*xem\s*th.*m$/i.test(txt)) {
                                                     xemThem = el;
                                                     break;
                                                 }
                                             }
                                         }

                                         if (xemThem) {
                                              window.viewMoreChapterBaseline = getChapterLinks();
                                              xemThem.classList.remove('hidden');
                                              xemThem.style.display = '';
                                              xemThem.style.visibility = 'visible';
                                              xemThem.style.border = '5px solid red';
                                              xemThem.style.backgroundColor = 'yellow';
                                              xemThem.scrollIntoView({behavior:'instant',block:'center'});

                                              window.viewMoreClicked = true;
                                              window.viewMoreClickedTime = Date.now();

                                              if (xemThem.offsetWidth > 0 || xemThem.offsetHeight > 0 || xemThem.getClientRects().length > 0) {
                                                  xemThem.click();
                                                  return 'waiting';
                                              } else {
                                                  xemThem.click();
                                                  xemThem.dispatchEvent(new MouseEvent('click', {bubbles:true,cancelable:true}));
                                                  return 'waiting';
                                              }
                                          }
                                         
                                         // If no view-more button exists and we have chapters, it's ready
                                         var chapterCount = getChapterLinks();
                                         if (chapterCount > 0) {
                                             return 'ready';
                                         }
                                         return 'waiting';
                                      })()";

                                string statusStr = await await Dispatcher.InvokeAsync(async () =>
                                {
                                    try { return await webView.CoreWebView2.ExecuteScriptAsync(processChaptersJs); } catch { return "ready"; }
                                });

                                string statusVal = statusStr?.Trim('"') ?? "ready";
                                if (statusVal == "clicked" || statusVal == "waiting")
                                {
                                    if (nettruyenChaptersWaitStartTime == DateTime.MinValue)
                                    {
                                        nettruyenChaptersWaitStartTime = DateTime.Now;
                                    }
                                    
                                    double elapsed = (DateTime.Now - nettruyenChaptersWaitStartTime).TotalSeconds;
                                    if (elapsed < 20.0) // Timeout after 20 seconds of waiting for chapters to load
                                    {
                                        shouldDelay = true;
                                    }
                                }
                            }
                            if (!shouldDelay)
                            {
                                // Get final HTML
                                string finalHtml = await await Dispatcher.InvokeAsync(async () =>
                                {
                                    try { return await webView.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML"); } catch { return null; }
                                });
                                if (!string.IsNullOrEmpty(finalHtml))
                                {
                                     if (finalHtml.StartsWith("\"") && finalHtml.EndsWith("\""))
                                     {
                                         finalHtml = UnescapeJsonString(finalHtml);
                                     }
                                    ResolvedHtml = finalHtml;
                                }

                                // False positive if window clears almost instantly without a real challenge.
                                Dispatcher.Invoke(() =>
                                {
                                    BtnDone_Click(this, null);
                                });
                                break;
                            }
                        }
                    }
                    else
                    {
                        if (_challengeDetectedAt == DateTime.MinValue)
                        {
                            _challengeDetectedAt = DateTime.Now;
                        }

                        if (_headlessAutomation && _challengeDetectedAt != DateTime.MinValue &&
                            (DateTime.Now - _challengeDetectedAt).TotalSeconds >= 6.0)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                CloseWithResult(false);
                            });
                            break;
                        }
                    }
                    
                    // Turnstile captcha bypass via keyboard sequence.
                    if (!_headlessAutomation && (IsNettruyenUrl(url) || IsTruyenqqChallengeUrl(url)))
                    {
                        if (_captchaBypassStartTime == DateTime.MinValue)
                        {
                            _captchaBypassStartTime = DateTime.Now;
                        }

                        double secsSinceStart = (DateTime.Now - _captchaBypassStartTime).TotalSeconds;
                        double secsSinceLastAttempt = (DateTime.Now - _lastCaptchaKeyboardAttempt).TotalSeconds;
                        double initialDelay = GetInitialCaptchaAttemptDelaySeconds(url);
                        double repeatDelay = GetRepeatCaptchaAttemptDelaySeconds(url);

                        bool shouldAttempt = false;
                        if (_lastCaptchaKeyboardAttempt == DateTime.MinValue && secsSinceStart >= initialDelay)
                        {
                            shouldAttempt = true;
                        }
                        else if (_lastCaptchaKeyboardAttempt != DateTime.MinValue && secsSinceLastAttempt >= repeatDelay)
                        {
                            shouldAttempt = true;
                        }

                        if (shouldAttempt && !_userInteracted)
                        {
                            // Tìm từ khóa trong DOM bằng JS sạch thay vì Ctrl+F của trình duyệt
                            bool containsChallengeText = await PageContainsKeywordAsync("human") || 
                                                         await PageContainsKeywordAsync("con người") ||
                                                         await PageContainsKeywordAsync("con nguoi") ||
                                                         await PageContainsKeywordAsync("verify") ||
                                                         await PageContainsKeywordAsync("xác minh") ||
                                                         await PageContainsKeywordAsync("robot");
                            if (containsChallengeText && !_userInteracted)
                            {
                                BypassWasNeeded = true;
                                _lastCaptchaKeyboardAttempt = DateTime.Now;
                                if (IsTruyenqqChallengeUrl(url) && await TryAutoSolveTruyenqqChallengeAsync(url))
                                {
                                    continue;
                                }
                                await SendCaptchaKeyboardBypassAsync(url);
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore errors during page loading
                }
            }
        }

        private async void BtnDone_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (webView.CoreWebView2 == null)
                {
                    if (!_headlessAutomation)
                    {
                        MessageBox.Show("Trình duyệt chưa sẵn sàng.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    return;
                }

                // Expose current redirected URI from the final WebView navigation if possible
                if (webView.Source != null)
                {
                    ResolvedUri = webView.Source;
                }
                else if (webView.CoreWebView2 != null &&
                         Uri.TryCreate(webView.CoreWebView2.Source, UriKind.Absolute, out Uri finalUri))
                {
                    ResolvedUri = finalUri;
                }

                try
                {
                    string finalHtml = await webView.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML");
                    if (!string.IsNullOrWhiteSpace(finalHtml))
                    {
                        if (finalHtml.StartsWith("\"") && finalHtml.EndsWith("\""))
                        {
                            finalHtml = UnescapeJsonString(finalHtml);
                        }

                        ResolvedHtml = finalHtml;
                    }
                }
                catch
                {
                }

                // Get cookies from WebView2 CookieManager for the final redirected URL
                string fetchUrl = ResolvedUri?.ToString() ?? _targetUrl;
                var list = await webView.CoreWebView2.CookieManager.GetCookiesAsync(fetchUrl);
                ResolvedCookies = new CookieContainer();
                var uri = new Uri(fetchUrl);
                
                foreach (var w2Cookie in list)
                {
                    var cookie = new Cookie(w2Cookie.Name, w2Cookie.Value, w2Cookie.Path, w2Cookie.Domain);
                    ResolvedCookies.Add(uri, cookie);
                }

                // Also get cookies for original URL just in case
                if (fetchUrl != _targetUrl)
                {
                    try
                    {
                        var originalUri = new Uri(_targetUrl);
                        var originalList = await webView.CoreWebView2.CookieManager.GetCookiesAsync(_targetUrl);
                        foreach (var w2Cookie in originalList)
                        {
                            var cookie = new Cookie(w2Cookie.Name, w2Cookie.Value, w2Cookie.Path, w2Cookie.Domain);
                            ResolvedCookies.Add(originalUri, cookie);
                        }
                    }
                    catch {}
                }

                // Get User-Agent dynamically
                string ua = await webView.CoreWebView2.ExecuteScriptAsync("navigator.userAgent");
                if (!string.IsNullOrEmpty(ua))
                {
                    if (ua.StartsWith("\"") && ua.EndsWith("\"") && ua.Length > 2)
                    {
                        ua = ua.Substring(1, ua.Length - 2);
                    }
                    UserAgent = ua;
                }

                CloseWithResult(true);
            }
            catch (Exception ex)
            {
                if (!_headlessAutomation)
                {
                    await RecoverFromCookieErrorAsync(ex);
                    return;
                }

                if (!_headlessAutomation)
                {
                    MessageBox.Show($"Lỗi thu thập cookies: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                CloseWithResult(false);
            }
        }

        private async Task DeleteCookiesAndReloadAsync(bool showMessage)
        {
            if (webView.CoreWebView2 == null)
            {
                throw new InvalidOperationException("Trình duyệt chưa sẵn sàng.");
            }

            webView.CoreWebView2.CookieManager.DeleteAllCookies();
            PortableRuntimeBootstrap.ResetPortableRuntimeStorage();
            PortableRuntimeBootstrap.EnsurePortableRuntime();

            ResolvedCookies = new CookieContainer();
            ResolvedUri = null;
            ResolvedHtml = null;
            BypassWasNeeded = false;
            _captchaBypassStartTime = DateTime.MinValue;
            _lastCaptchaKeyboardAttempt = DateTime.MinValue;
            _challengeDetectedAt = DateTime.MinValue;

            await Task.Delay(250);

            Dispatcher.Invoke(() =>
            {
                try
                {
                    webView.CoreWebView2.Navigate(_targetUrl);
                }
                catch
                {
                }
            });

            if (showMessage)
            {
                MessageBox.Show("Đã xóa cookie, refresh trang, tiếp tục chờ captcha/bypass.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async Task RecoverFromCookieErrorAsync(Exception ex)
        {
            try
            {
                await DeleteCookiesAndReloadAsync(showMessage: false);
                MessageBox.Show("Lỗi cookie. Đã tự reload captcha để thử lại.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch
            {
                MessageBox.Show($"Lỗi thu thập cookies: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                CloseWithResult(false);
            }
        }

        private async void BtnDeleteCookies_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await DeleteCookiesAndReloadAsync(showMessage: true);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi xóa cookie: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            CloseWithResult(false);
        }

        private string UnescapeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            value = value.Trim();
            if (value.StartsWith("\"") && value.EndsWith("\"") && value.Length >= 2)
            {
                value = value.Substring(1, value.Length - 2);
            }
            try
            {
                value = System.Text.RegularExpressions.Regex.Unescape(value);
            }
            catch {}
            return value;
        }
    }
}

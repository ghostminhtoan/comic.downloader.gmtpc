using System;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
        private readonly WebView2 _automationWebView = new WebView2();
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
        private bool _automationWebViewReady;
        private bool _autoDoneInProgress;
        private bool ExactVerifyDetected;
        private bool VerifyBypassAttempted;
        private bool VerifyBypassSucceeded;
        private bool MayAutoCloseAfterBypass;
        private DateTime _okStateDetectedAt = DateTime.MinValue;
        private DateTime _lastIncompatibleRefreshAt = DateTime.MinValue;
        private DateTime _verifySolveCooldownUntil = DateTime.MinValue;

        public CaptchaWindow(string targetUrl, CaptchaType captchaType, bool autoDeleteCookiesOnLoad = false, bool headlessAutomation = false)
        {
            InitializeComponent();
            if (webViewHost != null)
            {
                webViewHost.Children.Add(webView);
                ConfigureAutomationWebView();
                webViewHost.Children.Add(_automationWebView);
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
            Closed += CaptchaWindow_Closed;
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

        private void CaptchaWindow_Closed(object sender, EventArgs e)
        {
            CleanupWebViews();
        }

        private void CleanupWebViews()
        {
            try { webView.CoreWebView2?.Stop(); } catch { }
            try { _automationWebView.CoreWebView2?.Stop(); } catch { }
            try { webView.CoreWebView2.NewWindowRequested -= CoreWebView2_NewWindowRequested; } catch { }
            try { _automationWebView.CoreWebView2.NewWindowRequested -= CoreWebView2_NewWindowRequested; } catch { }
            try { webView.Source = null; } catch { }
            try { _automationWebView.Source = null; } catch { }
            try { webView.Dispose(); } catch { }
            try { _automationWebView.Dispose(); } catch { }
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

        private async Task TriggerCaptchaDoneAfterDelayAsync()
        {
            if (_autoDoneInProgress || WasCompleted)
            {
                return;
            }

            _autoDoneInProgress = true;
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1.5));

                if (WasCompleted || !IsLoaded)
                {
                    return;
                }

                await Dispatcher.InvokeAsync(() => BtnDone_Click(this, null));
            }
            finally
            {
                _autoDoneInProgress = false;
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
            return IsTruyenqqChallengeUrl(url) || IsNettruyenUrl(url);
        }

        private double GetInitialCaptchaAttemptDelaySeconds(string url)
        {
            return 0.25;
        }

        private double GetRepeatCaptchaAttemptDelaySeconds(string url)
        {
            return 0.35;
        }

        private void ConfigureAutomationWebView()
        {
            _automationWebView.Width = 1;
            _automationWebView.Height = 1;
            _automationWebView.IsHitTestVisible = false;
            _automationWebView.Focusable = false;
            _automationWebView.Visibility = Visibility.Hidden;
        }

        private async Task<string> TryAutoSolveChallengeInWebViewAsync(WebView2 targetWebView)
        {
            if (targetWebView?.CoreWebView2 == null)
            {
                return "none";
            }

            try
            {
                return await await Dispatcher.InvokeAsync(async () =>
                {
                    if (targetWebView.CoreWebView2 == null)
                    {
                        return "none";
                    }

                    string script = @"
                        (function() {
                            function normalize(text) {
                                return ((text || '') + '').toLowerCase().replace(/\s+/g, ' ').trim();
                            }

                            function isVisible(el) {
                                if (!el) return false;
                                var style = window.getComputedStyle(el);
                                if (!style || style.display === 'none' || style.visibility === 'hidden' || style.pointerEvents === 'none') {
                                    return false;
                                }
                                var rect = el.getBoundingClientRect();
                                return rect.width > 0 && rect.height > 0;
                            }

                            function getElementText(el) {
                                if (!el) return '';
                                var parts = [];
                                parts.push(el.innerText || '');
                                parts.push(el.textContent || '');
                                parts.push(el.getAttribute ? (el.getAttribute('aria-label') || '') : '');
                                parts.push(el.getAttribute ? (el.getAttribute('value') || '') : '');
                                parts.push(el.getAttribute ? (el.getAttribute('title') || '') : '');
                                return normalize(parts.join(' '));
                            }

                            function isVerifyPhrase(text) {
                                text = normalize(text);
                                if (!text || text.indexOf('verifying') !== -1) return false;
                                return text === 'verify' || text.indexOf('verify ') === 0;
                            }

                            function fireKeyboardAndClick(el) {
                                if (!el || !isVisible(el)) return false;
                                var tag = (el.tagName || '').toLowerCase();
                                var type = normalize(el.type || '');
                                if (tag === 'label') {
                                    var labelledBox = firstVisible([
                                        el.querySelector && el.querySelector('input[type=""checkbox""]'),
                                        el.querySelector && el.querySelector('[role=""checkbox""]'),
                                        el.querySelector && el.querySelector('iframe')
                                    ]);
                                    if (labelledBox && labelledBox !== el && fireKeyboardAndClick(labelledBox)) return true;
                                }

                                try { if (el.focus) el.focus({ preventScroll: true }); } catch (e) {}
                                if (tag === 'input' && (type === 'checkbox' || type === 'radio')) {
                                    try { el.checked = true; } catch (e) {}
                                    try { el.dispatchEvent(new Event('input', { bubbles: true })); } catch (e) {}
                                    try { el.dispatchEvent(new Event('change', { bubbles: true })); } catch (e) {}
                                }
                                try { el.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', code: 'Space', keyCode: 32, which: 32, bubbles: true })); } catch (e) {}
                                try { el.dispatchEvent(new KeyboardEvent('keyup', { key: ' ', code: 'Space', keyCode: 32, which: 32, bubbles: true })); } catch (e) {}
                                try { el.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true, view: window })); } catch (e) {}
                                try { el.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, cancelable: true, view: window })); } catch (e) {}
                                try { el.click(); return true; } catch (e) {}
                                try { el.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, view: window })); return true; } catch (e) {}
                                return false;
                            }

                            function firstVisible(list) {
                                for (var i = 0; i < list.length; i++) {
                                    if (list[i] && isVisible(list[i])) return list[i];
                                }
                                return null;
                            }

                            function isFocusable(el) {
                                if (!el || !isVisible(el)) return false;
                                if (el.tabIndex >= 0) return true;
                                var tag = (el.tagName || '').toLowerCase();
                                return tag === 'input' || tag === 'button' || tag === 'a' || tag === 'iframe' || tag === 'label';
                            }

                            function getPreviousVerifyTarget(candidate) {
                                if (!candidate) return null;
                                var current = candidate.previousElementSibling;
                                while (current) {
                                    if (isFocusable(current)) return current;
                                    var nested = current.querySelector ? firstVisible([
                                        current.querySelector('input[type=""checkbox""]'),
                                        current.querySelector('[role=""checkbox""]'),
                                        current.querySelector('label'),
                                        current.querySelector('button'),
                                        current.querySelector('iframe'),
                                        current.querySelector('a')
                                    ]) : null;
                                    if (nested) return nested;
                                    current = current.previousElementSibling;
                                }
                                return null;
                            }

                            function resolveVerifyTarget(candidate) {
                                if (!candidate) return null;
                                var previousFocusable = getPreviousVerifyTarget(candidate);
                                if (previousFocusable) return previousFocusable;

                                if (candidate.parentElement) {
                                    var parentPrevious = getPreviousVerifyTarget(candidate.parentElement);
                                    if (parentPrevious) return parentPrevious;
                                }

                                var forId = candidate.getAttribute ? candidate.getAttribute('for') : null;
                                if (forId) {
                                    var labelledTarget = document.getElementById(forId);
                                    if (labelledTarget && isVisible(labelledTarget)) return labelledTarget;
                                }

                                var container = candidate.closest ? (
                                    candidate.closest('[class*=""turnstile""]') ||
                                    candidate.closest('[class*=""challenge""]') ||
                                    candidate.closest('label') ||
                                    candidate.closest('[role=""checkbox""]') ||
                                    candidate.closest('form') ||
                                    candidate.parentElement
                                ) : candidate.parentElement;
                                var scope = container || candidate;
                                return firstVisible([
                                    scope.querySelector && scope.querySelector('input[type=""checkbox""]'),
                                    scope.querySelector && scope.querySelector('[role=""checkbox""]'),
                                    scope.querySelector && scope.querySelector('label'),
                                    scope.querySelector && scope.querySelector('iframe'),
                                    scope.querySelector && scope.querySelector('button'),
                                    scope.querySelector && scope.querySelector('a'),
                                    candidate
                                ]);
                            }

                            var elements = document.querySelectorAll('button, a, input, label, div, span, p');
                            var foundCandidate = null;
                            for (var i = 0; i < elements.length; i++) {
                                var el = elements[i];
                                if (!isVisible(el)) continue;
                                if (isVerifyPhrase(getElementText(el))) {
                                    foundCandidate = el;
                                    break;
                                }
                            }

                            if (!foundCandidate) {
                                return 'none';
                            }

                            var target = resolveVerifyTarget(foundCandidate);
                            if (target && fireKeyboardAndClick(target)) {
                                return 'clicked';
                            }

                            if (target && target !== foundCandidate && target.focus) {
                                try { target.focus({ preventScroll: true }); } catch (e) {}
                                var active = document.activeElement;
                                if (active && active !== target && fireKeyboardAndClick(active)) {
                                    return 'clicked';
                                }
                            }

                            if (fireKeyboardAndClick(foundCandidate)) {
                                return 'clicked';
                            }

                            if (target && target !== foundCandidate && target.querySelectorAll) {
                                var nearby = target.querySelectorAll('label, input[type=""checkbox""], [role=""checkbox""], iframe, button, a, div, span');
                                for (var j = 0; j < nearby.length; j++) {
                                    if (fireKeyboardAndClick(nearby[j])) {
                                        return 'clicked';
                                    }
                                }
                            }

                            if (foundCandidate.parentElement) {
                                var parentNearby = foundCandidate.parentElement.querySelectorAll('label, input[type=""checkbox""], [role=""checkbox""], iframe, button, a');
                                for (var k = 0; k < parentNearby.length; k++) {
                                    if (fireKeyboardAndClick(parentNearby[k])) {
                                        return 'clicked';
                                    }
                                }
                            }

                            return 'found';
                        })();";

                    string result = await targetWebView.CoreWebView2.ExecuteScriptAsync(script);
                    string normalizedResult = (result ?? string.Empty).Trim('"').Trim();
                    if (string.Equals(normalizedResult, "clicked", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(normalizedResult, "found", StringComparison.OrdinalIgnoreCase))
                    {
                        return normalizedResult.ToLowerInvariant();
                    }

                    return "none";
                });
            }
            catch
            {
                return "none";
            }
        }

        private async Task<string> TryAutoSolveChallengeWithAutomationWebViewAsync()
        {
            string visibleResult = await TryAutoSolveChallengeInWebViewAsync(webView);
            if (!string.Equals(visibleResult, "none", StringComparison.OrdinalIgnoreCase))
            {
                return visibleResult;
            }

            return await TryAutoSolveChallengeInWebViewAsync(_automationWebView);
        }

        private bool ShouldUseVisibleVerifyOnlyFlow(string url)
        {
            return !_headlessAutomation &&
                   _captchaType != CaptchaType.WatchMore &&
                   ShouldUseVerifyFindSequence(url);
        }

        private bool CanAutoCloseVisibleVerifyFlow()
        {
            if (_okStateDetectedAt == DateTime.MinValue)
            {
                return false;
            }

            bool hasRealSolveSignal = BypassWasNeeded ||
                                      ExactVerifyDetected ||
                                      VerifyBypassAttempted ||
                                      VerifyBypassSucceeded ||
                                      MayAutoCloseAfterBypass;
            if (!hasRealSolveSignal)
            {
                return false;
            }

            return (DateTime.Now - _okStateDetectedAt).TotalSeconds >= 3.0;
        }

        private bool IsVerifySolveCooldownActive()
        {
            return _verifySolveCooldownUntil != DateTime.MinValue &&
                   DateTime.Now < _verifySolveCooldownUntil;
        }

        private async Task<bool> TryRecoverIncompatibleChallengeAsync()
        {
            if ((DateTime.Now - _lastIncompatibleRefreshAt).TotalSeconds < 1.5)
            {
                return false;
            }

            WebView2 targetWebView = webView?.CoreWebView2 != null ? webView : _automationWebView;
            if (targetWebView?.CoreWebView2 == null)
            {
                return false;
            }

            try
            {
                string script = @"
                    (function() {
                        function normalize(text) {
                            return ((text || '') + '').toLowerCase().replace(/\s+/g, ' ').trim();
                        }

                        var bodyText = normalize(document.body && document.body.innerText || '');
                        var html = normalize(document.documentElement && document.documentElement.outerHTML || '');
                        var incompatible = bodyText.indexOf('incompatible browser extension or network configuration') !== -1 ||
                                           html.indexOf('incompatible browser extension or network configuration') !== -1;
                        if (!incompatible) {
                            return 'none';
                        }

                        var links = document.querySelectorAll('a, button');
                        for (var i = 0; i < links.length; i++) {
                            var text = normalize(links[i].innerText || links[i].textContent || links[i].value || '');
                            if (text.indexOf('refresh this page') !== -1 || text === 'refresh') {
                                try { links[i].click(); return 'clicked'; } catch (e) {}
                                try { links[i].dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, view: window })); return 'clicked'; } catch (e) {}
                            }
                        }

                        try { location.reload(); return 'reloaded'; } catch (e) {}
                        return 'detected';
                    })();";

                string result = await targetWebView.CoreWebView2.ExecuteScriptAsync(script);
                string normalizedResult = (result ?? string.Empty).Trim('"').Trim().ToLowerInvariant();
                if (normalizedResult == "clicked" || normalizedResult == "reloaded" || normalizedResult == "detected")
                {
                    _lastIncompatibleRefreshAt = DateTime.Now;
                    _okStateDetectedAt = DateTime.MinValue;
                    try
                    {
                        webView.CoreWebView2?.Reload();
                    }
                    catch
                    {
                    }
                    try
                    {
                        _automationWebView.CoreWebView2?.Reload();
                    }
                    catch
                    {
                    }
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private async Task<bool> PageContainsKeywordAsync(string keyword, bool preferAutomationWebView = false)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return false;
            }

            WebView2 targetWebView = preferAutomationWebView && _automationWebViewReady ? _automationWebView : webView;
            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (targetWebView?.CoreWebView2 == null)
                    {
                        targetWebView = webView;
                    }
                });

                return await await Dispatcher.InvokeAsync(async () =>
                {
                    if (targetWebView?.CoreWebView2 == null)
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

                    string result = await targetWebView.CoreWebView2.ExecuteScriptAsync(script);
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
                    if (_targetUrl.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase))
                    {
                        domain = "linkgrabber";
                    }
                    else
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
            }
            catch {}

            return System.IO.Path.Combine(PortablePaths.WebView2CaptchaUserDataFolder, domain);
        }

        private async void CaptchaWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                string browserArgs = "--disable-background-networking --disable-sync --disable-default-apps --no-first-run --disable-features=msSmartScreenProtection,RendererCodeIntegrity";
                string extensionPath = System.IO.Path.Combine(PortablePaths.AppRoot, "extensions", "Link Grabber 0.6.1_0");
                if (System.IO.Directory.Exists(extensionPath))
                {
                    browserArgs += $" --load-extension=\"{extensionPath}\"";
                }
                else
                {
                    browserArgs += " --disable-extensions --disable-component-extensions-with-background-pages";
                }

                bool isExtensionPage = !string.IsNullOrEmpty(_targetUrl) && _targetUrl.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase);
                if (!isExtensionPage && (string.IsNullOrEmpty(_targetUrl) || (!_targetUrl.Contains("truyenqq") && !_targetUrl.Contains("nettruyen"))))
                {
                    browserArgs += " --blink-settings=imagesEnabled=false";
                }
                var env = await CoreWebView2Environment.CreateAsync(
                    null,
                    GetWebView2UserDataFolder(),
                    new CoreWebView2EnvironmentOptions(browserArgs));
                await webView.EnsureCoreWebView2Async(env);
                await _automationWebView.EnsureCoreWebView2Async(env);

                if (webView.CoreWebView2 != null)
                {
                    webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                    webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                    webView.CoreWebView2.Settings.IsZoomControlEnabled = true;
                    webView.CoreWebView2.Settings.UserAgent = UserAgent;
                    webView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
                    string initScript = @"
window.open = () => null;
document.addEventListener('click', function (event) {
  const anchor = event.target && event.target.closest ? event.target.closest('a[target=""_blank""]') : null;
  if (anchor) {
    anchor.removeAttribute('target');
  }
}, true);";
                    if (!isExtensionPage && (string.IsNullOrEmpty(_targetUrl) || (!_targetUrl.Contains("truyenqq") && !_targetUrl.Contains("nettruyen"))))
                    {
                        initScript = @"
const textOnlyStyle = document.createElement('style');
textOnlyStyle.textContent = 'img, picture, video, audio, canvas, [style*=""background-image""] { display: none !important; visibility: hidden !important; }';
(document.head || document.documentElement).appendChild(textOnlyStyle);
" + initScript;
                    }
                    await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(initScript);
                }

                if (_automationWebView.CoreWebView2 != null)
                {
                    _automationWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    _automationWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                    _automationWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                    _automationWebView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                    _automationWebView.CoreWebView2.Settings.UserAgent = UserAgent;
                    _automationWebView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
                    await _automationWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
window.open = () => null;
document.addEventListener('click', function (event) {
  const anchor = event.target && event.target.closest ? event.target.closest('a[target=""_blank""]') : null;
  if (anchor) {
    anchor.removeAttribute('target');
  }
}, true);");
                    _automationWebViewReady = true;
                }

                webView.Source = new Uri(_targetUrl);
                _automationWebView.Source = new Uri(_targetUrl);

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

        private void CoreWebView2_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            string targetUrl = e.Uri;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var captchaWin = new CaptchaWindow(targetUrl, _captchaType, autoDeleteCookiesOnLoad: false, headlessAutomation: false);
                    captchaWin.Owner = this;
                    captchaWin.Show();
                }
                catch {}
            }));
        }

        private async Task AutoDetectBypassAsync()
        {
            DateTime nettruyenChaptersWaitStartTime = DateTime.MinValue;
            while (true)
            {
                await Task.Delay(250);

                if (WasCompleted || !IsLoaded)
                {
                    break;
                }
                
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
                    bool useVisibleVerifyOnlyFlow = ShouldUseVisibleVerifyOnlyFlow(url);

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
                        _verifySolveCooldownUntil = DateTime.MinValue;

                        if (_okStateDetectedAt == DateTime.MinValue)
                        {
                            _okStateDetectedAt = DateTime.Now;
                        }

                        bool challengeWasReal = BypassWasNeeded ||
                            ExactVerifyDetected ||
                            VerifyBypassAttempted ||
                            (_challengeDetectedAt != DateTime.MinValue && (DateTime.Now - _challengeDetectedAt).TotalSeconds >= 2.0);
                        BypassWasNeeded = challengeWasReal;

                        if (!title.Contains("Just a moment") && !title.Contains("Cloudflare"))
                        {
                            bool shouldDelay = false;
                            if (_headlessAutomation &&
                                UrlContainsHost(_targetUrl, "nettruyen.tech") &&
                                Uri.TryCreate(url, UriKind.Absolute, out Uri currentUri) &&
                                string.Equals(currentUri.Host, "nettruyen.tech", StringComparison.OrdinalIgnoreCase) &&
                                WindowElapsedSeconds < 5.0)
                            {
                                // ponytail: wait a bit for nettruyen.tech browser-side redirect before auto-closing headless probe.
                                shouldDelay = true;
                            }
                            if (_captchaType == CaptchaType.WatchMore)
                            {
                                         // Find and click "Xem thêm" by text content (CSS selectors are unreliable across nettruyen domains
                                         string processChaptersJs = @"
                                          (function() {
                                          try {
                                              var lists = document.querySelectorAll('#chapter_list, .list-chapter ul, ul.chapter-list, [id*=""chapter_list""]');
                                              for (var l = 0; l < lists.length; l++) {
                                                  lists[l].classList.add('active');
                                                  lists[l].style.display = 'flex';
                                                  lists[l].style.maxHeight = 'none';
                                              }
                                          } catch(e) {}

                                          function getChapterRoot() {
                                              return document.querySelector('.list-chapter') ||
                                                     document.querySelector('#nt_listchapter') ||
                                                     document.querySelector('.chapter-list') ||
                                                     document.querySelector('[id*=""listchapter""]') ||
                                                     document.querySelector('[class*=""list-chapter""]');
                                          }

                                          function getChapterLinks() {
                                              var root = getChapterRoot() || document;
                                              return root.querySelectorAll('a[href*=""chuong""], a[href*=""chap""], a[href*=""chapter""], a[href*=""c-""], a[href*=""/c/""], a[href*=""chuong-tranh""]').length;
                                          }

                                          function findViewMoreButton(root) {
                                              // 1. High priority: Direct chapter list view-more button
                                              var chapterBtn = document.querySelector('.list-chapter a.view-more, #nt_listchapter a.view-more, nav a.view-more, .list-chapter a[class*=""view-more""], #nt_listchapter a[class*=""view-more""], a.view-more:not(.hidden)');
                                              if (chapterBtn) return chapterBtn;

                                              if (root) {
                                                  var directMatch = root.querySelector('a.view-more, .view-more, [class*=""view-more""]');
                                                  if (directMatch && (!directMatch.className || directMatch.className.indexOf('morelink') < 0)) return directMatch;
                                              }

                                              // 2. Search candidates from BOTTOM to TOP to avoid description ""morelink"" at the top
                                              var candidates = Array.from(document.querySelectorAll('a, button')).reverse();
                                              for (var i = 0; i < candidates.length; i++) {
                                                  var node = candidates[i];
                                                  var cls = (node.className || '').toString().toLowerCase();
                                                  if (cls.indexOf('morelink') >= 0) continue; // Skip description expander button
                                                  var text = (node.innerText || node.textContent || '').replace(/\s+/g, ' ').trim().toLowerCase();
                                                  if (text === 'xem thêm' || text === '+ xem thêm' || text.indexOf('xem thêm') >= 0 || text.indexOf('xem them') >= 0 || cls.indexOf('view-more') >= 0) {
                                                      return node;
                                                  }
                                              }
                                              return null;
                                          }

                                          function fireClick(node) {
                                              if (!node) return false;
                                              try { node.click(); } catch (e) {}
                                              try { node.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true, view: window })); } catch (e) {}
                                              try { node.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, cancelable: true, view: window })); } catch (e) {}
                                              try { node.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, view: window })); } catch (e) {}
                                              try {
                                                  if (window.jQuery) {
                                                      window.jQuery(node).click();
                                                      window.jQuery(node).trigger('click');
                                                  }
                                              } catch (e) {}

                                              // Click all chapter view-more buttons on page
                                              try {
                                                  var allChapterBtns = document.querySelectorAll('.list-chapter a.view-more, #nt_listchapter a.view-more, a.view-more');
                                                  for (var b = 0; b < allChapterBtns.length; b++) {
                                                      allChapterBtns[b].click();
                                                  }
                                              } catch (e) {}

                                              return true;
                                          }
                                          
                                          if (window.viewMoreClicked) {
                                              var elapsed = Date.now() - window.viewMoreClickedTime;
                                              var chapterCount = getChapterLinks();
                                              var baseline = window.viewMoreChapterBaseline || 0;
                                              if (chapterCount > baseline) {
                                                  if (window.viewMoreLastCount !== chapterCount) {
                                                      window.viewMoreLastCount = chapterCount;
                                                      window.viewMoreStableSince = Date.now();
                                                      return 'waiting';
                                                  }
                                                  if (!window.viewMoreStableSince) window.viewMoreStableSince = Date.now();
                                                  if (elapsed < 3000 || Date.now() - window.viewMoreStableSince < 1200) {
                                                      return 'waiting';
                                                  }
                                                  return 'ready';
                                              }
                                              if (elapsed >= 1500 && elapsed < 12000) {
                                                  var retryRoot = getChapterRoot();
                                                  var retryButton = findViewMoreButton(retryRoot);
                                                  if (retryButton) {
                                                      if (retryButton.classList) {
                                                          retryButton.classList.remove('hidden');
                                                          retryButton.classList.remove('hide');
                                                      }
                                                      retryButton.style.display = '';
                                                      retryButton.style.visibility = 'visible';
                                                      retryButton.scrollIntoView({behavior:'instant',block:'center'});
                                                      fireClick(retryButton);
                                                      window.viewMoreClickedTime = Date.now();
                                                      return 'waiting';
                                                  }
                                              }
                                              return elapsed < 12000 ? 'waiting' : (chapterCount > 0 ? 'ready' : 'waiting');
                                          }
                                          
                                          var root = getChapterRoot() || document;
                                          var xemThem = findViewMoreButton(root);

                                          if (xemThem) {
                                              window.viewMoreChapterBaseline = getChapterLinks();
                                              if (xemThem.classList) {
                                                  xemThem.classList.remove('hidden');
                                                  xemThem.classList.remove('hide');
                                              }
                                              xemThem.style.display = '';
                                              xemThem.style.visibility = 'visible';
                                              xemThem.style.border = '5px solid red';
                                              xemThem.style.backgroundColor = 'yellow';
                                              xemThem.scrollIntoView({behavior:'instant',block:'center'});

                                              window.viewMoreClicked = true;
                                              window.viewMoreClickedTime = Date.now();
                                              window.viewMoreLastCount = 0;
                                              window.viewMoreStableSince = 0;

                                              fireClick(xemThem);
                                              return 'waiting';
                                          }

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
                                    if (elapsed < 25.0) // Timeout after 25 seconds of waiting for chapters to load
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

                                if (!useVisibleVerifyOnlyFlow || CanAutoCloseVisibleVerifyFlow())
                                {
                                    // False positive if window clears almost instantly without a real challenge.
                                    await TriggerCaptchaDoneAfterDelayAsync();
                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        _okStateDetectedAt = DateTime.MinValue;

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
                    
                    // Run DOM-only helper script in a separate hidden WebView2. Never steal focus from user apps.
                    if (useVisibleVerifyOnlyFlow)
                    {
                        if (await TryRecoverIncompatibleChallengeAsync())
                        {
                            continue;
                        }

                        if (IsVerifySolveCooldownActive())
                        {
                            continue;
                        }

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
                            string verifyState = await TryAutoSolveChallengeWithAutomationWebViewAsync();
                            if (!string.Equals(verifyState, "none", StringComparison.OrdinalIgnoreCase) && !_userInteracted)
                            {
                                BypassWasNeeded = true;
                                ExactVerifyDetected = true;
                                _lastCaptchaKeyboardAttempt = DateTime.Now;
                                if (string.Equals(verifyState, "clicked", StringComparison.OrdinalIgnoreCase))
                                {
                                    VerifyBypassAttempted = true;
                                    VerifyBypassSucceeded = true;
                                    MayAutoCloseAfterBypass = ExactVerifyDetected && VerifyBypassSucceeded;
                                    _verifySolveCooldownUntil = DateTime.Now.AddSeconds(8);
                                }

                                continue;
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
            if (_automationWebView.CoreWebView2 != null)
            {
                _automationWebView.CoreWebView2.CookieManager.DeleteAllCookies();
            }
            PortableRuntimeBootstrap.ResetPortableRuntimeStorage();
            PortableRuntimeBootstrap.EnsurePortableRuntime();

            ResolvedCookies = new CookieContainer();
            ResolvedUri = null;
            ResolvedHtml = null;
            BypassWasNeeded = false;
            _captchaBypassStartTime = DateTime.MinValue;
            _lastCaptchaKeyboardAttempt = DateTime.MinValue;
            _challengeDetectedAt = DateTime.MinValue;
            ExactVerifyDetected = false;
            VerifyBypassAttempted = false;
            VerifyBypassSucceeded = false;
            MayAutoCloseAfterBypass = false;
            _okStateDetectedAt = DateTime.MinValue;
            _lastIncompatibleRefreshAt = DateTime.MinValue;
            _verifySolveCooldownUntil = DateTime.MinValue;

            await Task.Delay(250);

            Dispatcher.Invoke(() =>
            {
                try
                {
                    webView.CoreWebView2.Navigate(_targetUrl);
                    if (_automationWebView.CoreWebView2 != null)
                    {
                        _automationWebView.CoreWebView2.Navigate(_targetUrl);
                    }
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

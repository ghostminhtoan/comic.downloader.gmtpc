using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private static volatile bool _isDamconuongLoginWindowActive;
        private DamconuongLoginWindow _damconuongLoginWindow;

        private async void BtnDamconuongLogin_Click(object sender, RoutedEventArgs e)
        {
            string preferredUrl = txtDamconuongTagUrl?.Text?.Trim();
            if (!IsDamconuongUrl(preferredUrl))
            {
                preferredUrl = DamconuongBaseUrl;
            }

            string email = txtDamconuongLoginEmail?.Text?.Trim() ?? string.Empty;
            string password = txtDamconuongLoginPassword?.Password ?? string.Empty;
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                await OpenDamconuongLoginAsync(preferredUrl);
                return;
            }

            try
            {
                lblStatus.Text = _isVietnameseUi ? "Đang mở login damconuong.shop và điền sẵn tài khoản..." : "Opening damconuong.shop login and prefilling credentials...";
                DamconuongLoginWindow loginWindow = await EnsureDamconuongLoginWindowAsync(preferredUrl);
                bool applied = await loginWindow.ApplyCredentialsAsync(email, password);
                if (!applied)
                {
                    lblStatus.Text = _isVietnameseUi
                        ? "Không tìm thấy form login damconuong. Cửa sổ login đã mở để bạn tự xử lý."
                        : "Damconuong login form not found. Login window left open for manual handling.";
                    return;
                }

                lblStatus.Text = _isVietnameseUi
                    ? "Đã điền email/password + remember me. Hãy bấm nút ĐĂNG NHẬP của website, rồi bấm HOÀN TẤT."
                    : "Prefilled email/password + remember me. Click the website LOGIN button, then click DONE.";
                DamconuongLog("Đã prefill tài khoản damconuong, chờ người dùng bấm nút đăng nhập của website.");
            }
            catch (Exception ex)
            {
                lblStatus.Text = (_isVietnameseUi ? "Auto login damconuong lỗi: " : "Damconuong auto login failed: ") + ex.Message;
                DamconuongLog("Lỗi auto login: " + ex.Message);
            }
        }

        private async Task OpenDamconuongLoginAsync(string targetUrl)
        {
            if (_isDamconuongLoginWindowActive)
            {
                _damconuongLoginWindow?.Activate();
                ShowInfo("Cửa sổ login damconuong đang mở.", "Thông báo");
                return;
            }

            try
            {
                DamconuongLoginWindow loginWindow = await EnsureDamconuongLoginWindowAsync(targetUrl);
                _isDamconuongLoginWindowActive = true;
                lblStatus.Text = _isVietnameseUi ? "Đang mở login damconuong.shop..." : "Opening damconuong.shop login...";

                if (await loginWindow.ShowNonBlockingAsync())
                {
                    SyncDamconuongLoginState(loginWindow);
                    lblStatus.Text = _isVietnameseUi ? "Đã đồng bộ phiên login damconuong.shop." : "damconuong.shop login session synced.";
                    DamconuongLog("Đồng bộ cookie và user-agent từ cửa sổ login thành công.");
                }
                else
                {
                    lblStatus.Text = _isVietnameseUi ? "Đã hủy login damconuong.shop." : "damconuong.shop login cancelled.";
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = (_isVietnameseUi ? "Login damconuong lỗi: " : "damconuong login failed: ") + ex.Message;
                DamconuongLog("Lỗi login: " + ex.Message);
            }
            finally
            {
                _isDamconuongLoginWindowActive = false;
                if (_damconuongLoginWindow != null && !_damconuongLoginWindow.IsVisible)
                {
                    _damconuongLoginWindow = null;
                }
            }
        }

        private async Task<DamconuongLoginWindow> EnsureDamconuongLoginWindowAsync(string targetUrl)
        {
            string loginUrl = IsDamconuongUrl(targetUrl) ? NormalizeDamconuongUrl(targetUrl) : DamconuongBaseUrl;
            if (_damconuongLoginWindow == null || !_damconuongLoginWindow.IsLoaded || !_damconuongLoginWindow.IsVisible)
            {
                _damconuongLoginWindow = new DamconuongLoginWindow(loginUrl, _isVietnameseUi)
                {
                    Owner = this
                };
                _damconuongLoginWindow.Closed += (_, __) => _damconuongLoginWindow = null;
                _damconuongLoginWindow.Show();
            }
            else
            {
                _damconuongLoginWindow.Activate();
                await _damconuongLoginWindow.NavigateIfNeededAsync(loginUrl);
            }

            await _damconuongLoginWindow.WaitUntilReadyAsync();
            return _damconuongLoginWindow;
        }

        private void SyncDamconuongLoginState(DamconuongLoginWindow loginWindow)
        {
            if (loginWindow == null)
            {
                return;
            }

            Uri baseUri = new Uri(DamconuongBaseUrl);
            Uri resolvedUri = loginWindow.ResolvedUri ?? baseUri;

            foreach (Cookie cookie in loginWindow.ResolvedCookies.GetCookies(resolvedUri).Cast<Cookie>())
            {
                _cookieContainer.Add(resolvedUri, cookie);
                _cookieContainer.Add(baseUri, new Cookie(cookie.Name, cookie.Value, string.IsNullOrWhiteSpace(cookie.Path) ? "/" : cookie.Path, baseUri.Host)
                {
                    Expires = cookie.Expires,
                    Secure = cookie.Secure,
                    HttpOnly = cookie.HttpOnly
                });
            }

            foreach (Cookie cookie in loginWindow.ResolvedCookies.GetCookies(baseUri).Cast<Cookie>())
            {
                _cookieContainer.Add(baseUri, cookie);
            }

            if (!string.IsNullOrWhiteSpace(loginWindow.UserAgent))
            {
                _httpClient.DefaultRequestHeaders.UserAgent.Clear();
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(loginWindow.UserAgent);
            }
        }
    }

    internal sealed class DamconuongLoginWindow : Window
    {
        private readonly WebView2 _webView;
        private readonly TextBlock _statusText;
        private string _targetUrl;
        private readonly bool _isVietnamese;
        private readonly TaskCompletionSource<bool> _readyTcs = new TaskCompletionSource<bool>();
        private bool _wasCompleted;

        internal CookieContainer ResolvedCookies { get; private set; } = new CookieContainer();
        internal Uri ResolvedUri { get; private set; }
        internal string UserAgent { get; private set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        internal DamconuongLoginWindow(string targetUrl, bool isVietnamese)
        {
            _targetUrl = string.IsNullOrWhiteSpace(targetUrl) ? "https://damconuong.shop" : targetUrl;
            _isVietnamese = isVietnamese;

            Title = isVietnamese ? "LOGIN DAMCONUONG.SHOP" : "DAMCONUONG.SHOP LOGIN";
            Width = 1260;
            Height = 860;
            MinWidth = 980;
            MinHeight = 720;
            Background = new SolidColorBrush(Color.FromRgb(0x09, 0x0D, 0x14));
            Foreground = Brushes.White;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new StackPanel
            {
                Margin = new Thickness(16, 14, 16, 10)
            };
            header.Children.Add(new TextBlock
            {
                Text = isVietnamese ? "ĐĂNG NHẬP DAMCONUONG.SHOP" : "DAMCONUONG.SHOP LOGIN",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0xE7, 0xFF))
            });
            header.Children.Add(new TextBlock
            {
                Text = isVietnamese
                    ? "Đăng nhập damconuong trong WebView này. Xong thì bấm HOÀN TẤT để đồng bộ cookie cho downloader."
                    : "Sign in to damconuong in this WebView. Click DONE to sync cookies back to the downloader.",
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0xB3, 0xC7))
            });
            root.Children.Add(header);

            _webView = new WebView2
            {
                Margin = new Thickness(16, 0, 16, 12)
            };
            Grid.SetRow(_webView, 1);
            root.Children.Add(_webView);

            var footer = new Grid
            {
                Margin = new Thickness(16, 0, 16, 16)
            };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusText = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x6A))
            };
            footer.Children.Add(_statusText);

            var doneButton = new Button
            {
                Content = isVietnamese ? "HOÀN TẤT" : "DONE",
                MinWidth = 120,
                Margin = new Thickness(12, 0, 0, 0),
                Padding = new Thickness(14, 8, 14, 8)
            };
            doneButton.Click += async (sender, args) => await CompleteAsync();
            Grid.SetColumn(doneButton, 1);
            footer.Children.Add(doneButton);

            var cancelButton = new Button
            {
                Content = isVietnamese ? "ĐÓNG" : "CLOSE",
                MinWidth = 96,
                Margin = new Thickness(10, 0, 0, 0),
                Padding = new Thickness(14, 8, 14, 8)
            };
            cancelButton.Click += (sender, args) => Close();
            Grid.SetColumn(cancelButton, 2);
            footer.Children.Add(cancelButton);

            Grid.SetRow(footer, 2);
            root.Children.Add(footer);
            Content = root;

            Loaded += DamconuongLoginWindow_Loaded;
        }

        internal Task<bool> ShowNonBlockingAsync()
        {
            var tcs = new TaskCompletionSource<bool>();
            Closed += OnClosed;
            return tcs.Task;

            void OnClosed(object sender, EventArgs e)
            {
                Closed -= OnClosed;
                tcs.TrySetResult(_wasCompleted);
            }
        }

        private async void DamconuongLoginWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                string userDataFolder = System.IO.Path.Combine(PortablePaths.WebView2UserDataFolder, "damconuong-login");
                System.IO.Directory.CreateDirectory(userDataFolder);
                var env = await CoreWebView2Environment.CreateAsync(
                    null,
                    userDataFolder,
                    new CoreWebView2EnvironmentOptions());

                await _webView.EnsureCoreWebView2Async(env);
                if (_webView.CoreWebView2 != null)
                {
                    _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                    _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                    _webView.CoreWebView2.Settings.IsZoomControlEnabled = true;
                    _webView.CoreWebView2.Settings.UserAgent = UserAgent;
                    _webView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
                    _webView.CoreWebView2.NavigationCompleted += WebView_NavigationCompleted;
                    await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
window.open = () => null;
document.addEventListener('click', function (event) {
  const anchor = event.target && event.target.closest ? event.target.closest('a[target=""_blank""]') : null;
  if (anchor) {
    anchor.removeAttribute('target');
  }
}, true);");
                }

                _readyTcs.TrySetResult(true);
                _statusText.Text = _isVietnamese
                    ? "Đang mở trang damconuong.shop..."
                    : "Opening damconuong.shop...";
                _webView.Source = new Uri(_targetUrl);
            }
            catch (Exception ex)
            {
                _readyTcs.TrySetException(ex);
                MessageBox.Show(
                    (_isVietnamese ? "Không thể khởi tạo WebView2: " : "Failed to initialize WebView2: ") + ex.Message,
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Close();
            }
        }

        private void WebView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (_webView?.CoreWebView2 == null)
            {
                return;
            }

            _statusText.Text = _isVietnamese
                ? "Đăng nhập xong thì bấm HOÀN TẤT để lưu cookie."
                : "Click DONE after login to save cookies.";
        }

        private void CoreWebView2_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
        }

        internal Task WaitUntilReadyAsync()
        {
            return _readyTcs.Task;
        }

        internal async Task NavigateIfNeededAsync(string targetUrl)
        {
            await WaitUntilReadyAsync();
            string normalized = string.IsNullOrWhiteSpace(targetUrl) ? "https://damconuong.shop" : targetUrl;
            _targetUrl = normalized;
            string current = _webView.Source?.ToString() ?? string.Empty;
            if (!string.Equals(current, normalized, StringComparison.OrdinalIgnoreCase))
            {
                _webView.CoreWebView2?.Navigate(normalized);
                await Task.Delay(1200);
            }
        }

        internal async Task<bool> ApplyCredentialsAsync(string email, string password)
        {
            await WaitUntilReadyAsync();
            await NavigateToLoginFormAsync();

            string script =
@"
(async () => {
  const selectorsEmail = [
    'input[type=""email""]',
    'input[name*=""email"" i]',
    'input[id*=""email"" i]',
    'input[autocomplete=""username""]',
    'input[autocomplete=""email""]'
  ];
  const selectorsPassword = [
    'input[type=""password""]',
    'input[name*=""pass"" i]',
    'input[id*=""pass"" i]',
    'input[autocomplete=""current-password""]'
  ];
  const submitSelectors = [
    'button[type=""submit""]',
    'input[type=""submit""]',
    'button[name*=""login"" i]',
    'button[id*=""login"" i]',
    '.btn-login',
    '.login-button'
  ];
  const rememberSelectors = [
    'input[type=""checkbox""][name*=""remember"" i]',
    'input[type=""checkbox""][id*=""remember"" i]',
    'input[type=""checkbox""][autocomplete=""remember""]'
  ];
  const find = list => {
    for (const selector of list) {
      const matches = Array.from(document.querySelectorAll(selector));
      const el = matches.find(node => {
        if (!node) return false;
        const style = window.getComputedStyle(node);
        return !node.disabled && style.display !== 'none' && style.visibility !== 'hidden';
      });
      if (el) return el;
    }
    return null;
  };
  const applyValue = (el, value) => {
    el.focus();
    const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value')?.set
      || Object.getOwnPropertyDescriptor(Object.getPrototypeOf(el), 'value')?.set;
    if (setter) setter.call(el, value);
    else el.value = value;
    el.dispatchEvent(new Event('input', { bubbles: true }));
    el.dispatchEvent(new Event('change', { bubbles: true }));
  };
  const typeSequentially = async (el, value) => {
    applyValue(el, '');
    for (const ch of value) {
      el.dispatchEvent(new KeyboardEvent('keydown', { key: ch, bubbles: true }));
      el.dispatchEvent(new KeyboardEvent('keypress', { key: ch, bubbles: true }));
      applyValue(el, (el.value || '') + ch);
      el.dispatchEvent(new KeyboardEvent('keyup', { key: ch, bubbles: true }));
      await new Promise(resolve => setTimeout(resolve, 35));
    }
    el.dispatchEvent(new Event('blur', { bubbles: true }));
  };
  const setChecked = (el, checked) => {
    el.focus();
    const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'checked')?.set
      || Object.getOwnPropertyDescriptor(Object.getPrototypeOf(el), 'checked')?.set;
    if (setter) setter.call(el, checked);
    else el.checked = checked;
    el.dispatchEvent(new Event('input', { bubbles: true }));
    el.dispatchEvent(new Event('change', { bubbles: true }));
    if (!!el.checked !== checked && typeof el.click === 'function') {
      el.click();
    }
    el.dispatchEvent(new Event('blur', { bubbles: true }));
  };
  const emailInput = find(selectorsEmail);
  const passwordInput = find(selectorsPassword);
  if (!emailInput || !passwordInput) return 'missing';
  await typeSequentially(emailInput, __EMAIL__);
  await new Promise(resolve => setTimeout(resolve, 120));
  await typeSequentially(passwordInput, __PASSWORD__);
  const rememberInput = find(rememberSelectors);
  if (rememberInput && !rememberInput.checked) {
    setChecked(rememberInput, true);
  } else if (!rememberInput) {
    const rememberLabel = Array.from(document.querySelectorAll('label,span,div')).find(node => {
      const text = (node.textContent || '').trim().toLowerCase();
      return text.includes('remember me');
    });
    if (rememberLabel) {
      rememberLabel.click();
    }
  }
  await new Promise(resolve => setTimeout(resolve, 180));
  if ((passwordInput.value || '') !== __PASSWORD__) {
    await typeSequentially(passwordInput, __PASSWORD__);
    await new Promise(resolve => setTimeout(resolve, 180));
  }
  return 'prefilled';
})()";

            script = script
                .Replace("__EMAIL__", ToJavaScriptStringLiteral(email))
                .Replace("__PASSWORD__", ToJavaScriptStringLiteral(password));
            string result = await ExecuteStringScriptAsync(script);

            _statusText.Text = _isVietnamese
                ? "Đã điền sẵn tài khoản. Hãy bấm nút ĐĂNG NHẬP của website rồi bấm HOÀN TẤT."
                : "Credentials prefilled. Click the website LOGIN button, then click DONE.";
            return string.Equals(result, "prefilled", StringComparison.OrdinalIgnoreCase);
        }

        internal async Task<bool> WaitForAuthenticatedSessionAsync()
        {
            await WaitUntilReadyAsync();

            for (int attempt = 0; attempt < 20; attempt++)
            {
                bool hasAuthCookies = await HasAuthenticationCookiesAsync();
                string state = await ExecuteStringScriptAsync(@"
(() => {
  const text = (document.body?.innerText || '').toLowerCase();
  const hasPassword = !!document.querySelector('input[type=""password""]');
  const loginRequired =
    text.includes('yêu cầu đăng nhập') ||
    text.includes('nội dung này dành cho người dùng đã xác thực') ||
    text.includes('noi dung nay danh cho nguoi dung da xac thuc') ||
    text.includes('tạo tài khoản mới') ||
    text.includes('tao tai khoan moi');

  if (hasPassword) return 'password';
  if (loginRequired) return 'blocked';
  return 'ready';
})()");

                if (hasAuthCookies && !string.Equals(state, "blocked", StringComparison.OrdinalIgnoreCase))
                {
                    await NavigateIfNeededAsync(_targetUrl);
                    await Task.Delay(1000);
                    string targetState = await ExecuteStringScriptAsync(@"
(() => {
  const text = (document.body?.innerText || '').toLowerCase();
  const loginRequired =
    text.includes('yêu cầu đăng nhập') ||
    text.includes('nội dung này dành cho người dùng đã xác thực') ||
    text.includes('noi dung nay danh cho nguoi dung da xac thuc') ||
    text.includes('tạo tài khoản mới') ||
    text.includes('tao tai khoan moi');
  return loginRequired ? 'blocked' : 'ready';
})()");
                    if (string.Equals(targetState, "ready", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                if (attempt == 3 || attempt == 7 || attempt == 11 || attempt == 15)
                {
                    _webView.CoreWebView2?.Navigate(_targetUrl);
                }

                await Task.Delay(1000);
            }

            return false;
        }

        private async Task<bool> HasAuthenticationCookiesAsync()
        {
            if (_webView?.CoreWebView2 == null)
            {
                return false;
            }

            foreach (string url in new[] { "https://damconuong.shop", _targetUrl }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var cookies = await _webView.CoreWebView2.CookieManager.GetCookiesAsync(url);
                if (cookies.Any(cookie =>
                    cookie != null &&
                    !string.IsNullOrWhiteSpace(cookie.Value) &&
                    (cookie.Name.IndexOf("session", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     cookie.Name.IndexOf("remember", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     cookie.Name.IndexOf("auth", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     cookie.Name.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0)))
                {
                    return true;
                }
            }

            return false;
        }

        internal async Task CaptureSessionAsync()
        {
            await WaitUntilReadyAsync();
            await RefreshResolvedSessionAsync();
        }

        private async Task NavigateToLoginFormAsync()
        {
            await WaitUntilReadyAsync();
            for (int i = 0; i < 3; i++)
            {
                string state = await ExecuteStringScriptAsync(@"
(() => {
  if (document.querySelector('input[type=""password""]')) return 'ready';
  const candidates = Array.from(document.querySelectorAll('a,button'));
  const loginNode = candidates.find(node => {
    const text = (node.textContent || '').trim().toLowerCase();
    const href = (node.getAttribute('href') || '').toLowerCase();
    return text.includes('đăng nhập') || text.includes('dang nhap') || text.includes('login') || href.includes('dang-nhap') || href.includes('/login');
  });
  if (!loginNode) return 'missing';
  loginNode.click();
  return 'clicked';
})()");

                if (string.Equals(state, "ready", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (string.Equals(state, "clicked", StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Delay(1500);
                    continue;
                }

                _webView.CoreWebView2?.Navigate("https://damconuong.shop");
                await Task.Delay(1500);
            }
        }

        private async Task RefreshResolvedSessionAsync()
        {
            if (_webView?.CoreWebView2 == null)
            {
                return;
            }

            string currentUrl = _webView.Source?.ToString() ?? _targetUrl;
            ResolvedUri = Uri.TryCreate(currentUrl, UriKind.Absolute, out Uri resolvedUri) ? resolvedUri : new Uri(_targetUrl);

            ResolvedCookies = new CookieContainer();
            foreach (string url in new[] { currentUrl, "https://damconuong.shop", _targetUrl }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (CoreWebView2Cookie webCookie in await _webView.CoreWebView2.CookieManager.GetCookiesAsync(url))
                {
                    Uri cookieUri = Uri.TryCreate("https://" + webCookie.Domain.TrimStart('.'), UriKind.Absolute, out Uri parsedCookieUri)
                        ? parsedCookieUri
                        : ResolvedUri;

                    var cookie = new Cookie(webCookie.Name, webCookie.Value, string.IsNullOrWhiteSpace(webCookie.Path) ? "/" : webCookie.Path, webCookie.Domain)
                    {
                        Secure = webCookie.IsSecure,
                        HttpOnly = webCookie.IsHttpOnly
                    };

                    if (webCookie.Expires != DateTime.MinValue)
                    {
                        cookie.Expires = webCookie.Expires;
                    }

                    ResolvedCookies.Add(cookieUri, cookie);
                }
            }

            try
            {
                string userAgent = await ExecuteStringScriptAsync("navigator.userAgent");
                if (!string.IsNullOrWhiteSpace(userAgent))
                {
                    UserAgent = userAgent;
                }
            }
            catch
            {
            }
        }

        private async Task<string> ExecuteStringScriptAsync(string script)
        {
            string raw = await _webView.CoreWebView2.ExecuteScriptAsync(script);
            return DecodeWebViewString(raw);
        }

        private static string DecodeWebViewString(string value)
        {
            string text = (value ?? string.Empty).Trim();
            if (text.StartsWith("\"", StringComparison.Ordinal) && text.EndsWith("\"", StringComparison.Ordinal) && text.Length >= 2)
            {
                text = text.Substring(1, text.Length - 2)
                    .Replace("\\\\", "\\")
                    .Replace("\\\"", "\"")
                    .Replace("\\n", "\n")
                    .Replace("\\r", "\r")
                    .Replace("\\t", "\t")
                    .Replace("\\u003C", "<")
                    .Replace("\\u003E", ">")
                    .Replace("\\u0026", "&");
            }

            return string.Equals(text, "null", StringComparison.OrdinalIgnoreCase) ? string.Empty : text;
        }

        private static string ToJavaScriptStringLiteral(string value)
        {
            string text = value ?? string.Empty;
            return "\"" + text
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("</", "<\\/") + "\"";
        }

        private async Task CompleteAsync()
        {
            if (_webView?.CoreWebView2 == null)
            {
                return;
            }

            try
            {
                await RefreshResolvedSessionAsync();
                _wasCompleted = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    (_isVietnamese ? "Không thể lưu phiên login: " : "Failed to save login session: ") + ex.Message,
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}

using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private async void BtnCaptcha_Click(object sender, RoutedEventArgs e)
        {
            string url = string.Empty;
            string defaultFallbackDomain = string.Empty;
            var button = sender as Button;
            
            if (button == btnFetchCaptcha) { url = txtTagUrl.Text; defaultFallbackDomain = "hentaiforce.net"; }
            else if (button == btnNhentaiFetchCaptcha) { url = txtNhentaiTagUrl.Text; defaultFallbackDomain = "nhentai.xxx"; }
            else if (button == btnViHentaiFetchCaptcha) { url = txtViHentaiTagUrl.Text; defaultFallbackDomain = "vi-hentai.com"; }
            else if (button == btnTruyenqqFetchCaptcha) { url = txtTruyenqqTagUrl.Text; defaultFallbackDomain = "truyenqq.com.vn"; }
            else if (button == btnNettruyenFetchCaptcha) { url = txtNettruyenTagUrl.Text; defaultFallbackDomain = "nettruyen.com"; }
            else if (button == btnHakoFetchCaptcha) { url = txtHakoTagUrl.Text; defaultFallbackDomain = "ln.hako.vn"; }
            else if (button == btnDamconuongFetchCaptcha) { url = txtDamconuongTagUrl.Text; defaultFallbackDomain = "damconuong.shop"; }
            else if (button == btnTruyenggvnFetchCaptcha) { url = txtTruyenggvnTagUrl.Text; defaultFallbackDomain = "truyengg.com"; }
            else if (button == btnHentai2readFetchCaptcha) { url = txtHentai2readTagUrl.Text; defaultFallbackDomain = "hentai2read.com"; }
            else if (button == btnHentaieraFetchCaptcha) { url = txtHentaieraTagUrl.Text; defaultFallbackDomain = "hentaiera.com"; }
            else if (button == btnNhentaiNetFetchCaptcha) { url = txtNhentaiNetTagUrl?.Text; defaultFallbackDomain = "nhentai.net"; }

            // Lấy domain cần xóa cookie
            string targetDomain = defaultFallbackDomain;
            if (!string.IsNullOrWhiteSpace(url))
            {
                targetDomain = NormalizeCookieHostKey(url);
            }

            if (!string.IsNullOrWhiteSpace(targetDomain))
            {
                // Thực hiện xóa cookie cho domain này
                _cookieContainersByHost.TryRemove(targetDomain, out _);
                string wildCardKey = "." + targetDomain;
                var subKeys = _cookieContainersByHost.Keys.Where(k => k.EndsWith(wildCardKey, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var subKey in subKeys)
                {
                    _cookieContainersByHost.TryRemove(subKey, out _);
                }
                
                // Đồng thời reset cấu trúc SQLite nếu domain là truyenqq
                if (IsTruyenqqUrl(targetDomain))
                {
                    _truyenqqPreferredBaseUrl = null;
                }

                // Xóa và tạo lại folder captcha WebView2
                string captchaFolderName = GetCaptchaFolderNameFromDomain(targetDomain);
                string captchaPath = System.IO.Path.Combine(PortablePaths.WebView2CaptchaUserDataFolder, captchaFolderName);
                try
                {
                    if (System.IO.Directory.Exists(captchaPath))
                    {
                        System.IO.Directory.Delete(captchaPath, true);
                        Log($"[Captcha] Đã xóa folder captcha: {captchaFolderName}");
                    }
                    System.IO.Directory.CreateDirectory(captchaPath);
                }
                catch (Exception ex)
                {
                    Log($"[Captcha] Không thể reset folder captcha {captchaFolderName}: {ex.Message}");
                }
                
                Log($"[Captcha] Đã xóa cookie cache của tên miền: {targetDomain}");
            }

            if (string.IsNullOrWhiteSpace(url))
            {
                // Nếu không nhập URL, chỉ cần xóa cookie trong cache là đủ
                ShowInfo($"Đã xóa sạch cookie của tên miền {targetDomain}.", "Thông báo");
                return;
            }

            if (button == btnNhentaiFetchCaptcha)
            {
                ResetCookiesForCaptcha(url);
                ShowInfo("Đã xóa cookie cho nhentai.xxx. Site này không cần captcha nữa.", "Thông báo");
                return;
            }

            ResetCookiesForCaptcha(url);

            var captchaWin = CreateCaptchaWindow(url, autoDeleteCookiesOnLoad: true);
            captchaWin.Owner = this;

            if (await ShowCaptchaWindowWithFocusHandlingAsync(captchaWin, useNovelFocusStealth: _lightNovelAutoFocusEnabled))
            {
                SyncCaptchaWindowState(url, captchaWin);
            }
        }

        private void ResetCookiesForCaptcha(string url)
        {
            try
            {
                InitializeHttpClientState();
                PortableRuntimeBootstrap.ResetPortableRuntimeStorage();
                PortableRuntimeBootstrap.EnsurePortableRuntime();
                _hakoCaptchaSessionReady = false;
                if (IsTruyenqqUrl(url))
                {
                    _truyenqqPreferredBaseUrl = null;
                }

                Log("Đã xóa cookie và khởi tạo lại phiên captcha.");
            }
            catch (Exception ex)
            {
                Log($"[Captcha] Không thể reset cookie: {ex.Message}");
            }
        }

        private void SetShutdownAfterCompleteFromFloating(bool enabled)
        {
            _shutdownAfterCompleted = enabled;
            if (tglShutdownAfterDownload != null)
            {
                tglShutdownAfterDownload.IsChecked = enabled;
            }

            if (chkShutdownAfterCompleted != null)
            {
                chkShutdownAfterCompleted.IsChecked = enabled;
            }
        }

        private async Task ResetActiveCaptchaFromFloatingAsync()
        {
            string url = GetActiveCaptchaTargetUrl();
            if (string.IsNullOrWhiteSpace(url))
            {
                ShowWarning("Không tìm thấy URL captcha ở tab hiện tại.", "Thông báo");
                return;
            }

            ResetCookiesForCaptcha(url);
            if (IsNhentaiCaptchaUrl(url))
            {
                ShowInfo("Đã làm mới cookie cho nhentai.xxx.", "Thông báo");
                return;
            }

            await await Dispatcher.InvokeAsync(async () =>
            {
                var captchaWin = CreateCaptchaWindow(url, autoDeleteCookiesOnLoad: true);
                captchaWin.Owner = this;

                if (await ShowCaptchaWindowWithFocusHandlingAsync(captchaWin, useNovelFocusStealth: _lightNovelAutoFocusEnabled))
                {
                    SyncCaptchaWindowState(url, captchaWin);
                }
            });
        }

        private string GetActiveCaptchaTargetUrl()
        {
            if (tabLeftPanel?.SelectedIndex == 1)
            {
                string selectedHentai = (tabHentai?.SelectedItem as TabItem)?.Header?.ToString()?.ToLowerInvariant() ?? string.Empty;
                if (selectedHentai.Contains("nhentai")) return txtNhentaiTagUrl?.Text?.Trim() ?? string.Empty;
                if (selectedHentai.Contains("hentai2read")) return txtHentai2readTagUrl?.Text?.Trim() ?? string.Empty;
                if (selectedHentai.Contains("hentaiera")) return txtHentaieraTagUrl?.Text?.Trim() ?? string.Empty;
                if (selectedHentai.Contains("hentaiforce")) return txtTagUrl?.Text?.Trim() ?? string.Empty;
                if (selectedHentai.Contains("daomeoden")) return txtDaomeodenTagUrl?.Text?.Trim() ?? string.Empty;
                if (selectedHentai.Contains("damconuong")) return txtDamconuongTagUrl?.Text?.Trim() ?? string.Empty;
                return txtViHentaiTagUrl?.Text?.Trim() ?? string.Empty;
            }

            if (tabLeftPanel?.SelectedIndex == 2)
            {
                return txtHakoTagUrl?.Text?.Trim() ?? string.Empty;
            }

            string selectedManga = (tabManga?.SelectedItem as TabItem)?.Header?.ToString()?.ToLowerInvariant() ?? string.Empty;
            if (selectedManga.Contains("nettruyen")) return txtNettruyenTagUrl?.Text?.Trim() ?? string.Empty;
            if (selectedManga.Contains("daomeoden")) return txtDaomeodenTagUrl?.Text?.Trim() ?? string.Empty;
            if (selectedManga.Contains("truyengg")) return txtTruyenggvnTagUrl?.Text?.Trim() ?? string.Empty;
            return txtTruyenqqTagUrl?.Text?.Trim() ?? string.Empty;
        }

        private static bool IsNhentaiCaptchaUrl(string url)
        {
            return !string.IsNullOrWhiteSpace(url) &&
                   url.IndexOf("nhentai", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SyncCaptchaWindowState(string url, CaptchaWindow captchaWin)
        {
            try
            {
                var originalUri = new Uri(url);
                var resolvedUri = captchaWin.ResolvedUri ?? originalUri;

                foreach (Cookie cookie in captchaWin.ResolvedCookies.GetCookies(resolvedUri).Cast<Cookie>())
                {
                    _cookieContainer.Add(resolvedUri, cookie);
                }

                if (originalUri.Host != resolvedUri.Host)
                {
                    foreach (Cookie cookie in captchaWin.ResolvedCookies.GetCookies(originalUri).Cast<Cookie>())
                    {
                        _cookieContainer.Add(originalUri, cookie);
                    }
                }

                if (!string.IsNullOrEmpty(captchaWin.UserAgent))
                {
                    RememberScopedUserAgent(originalUri.AbsoluteUri, captchaWin.UserAgent);
                    RememberScopedUserAgent(resolvedUri.AbsoluteUri, captchaWin.UserAgent);
                }

                if (captchaWin.BypassWasNeeded)
                {
                    Log("Đồng bộ cookie và user-agent từ CaptchaWindow thành công sau khi bypass captcha.");
                }
                else
                {
                    Log("Đồng bộ cookie và user-agent từ CaptchaWindow thành công. Không phát hiện captcha thật.");
                }
            }
            catch (Exception ex)
            {
                ShowError($"Lỗi lưu cookie: {ex.Message}", "Lỗi");
            }
        }

        public CaptchaWindow CreateCaptchaWindow(string url, bool autoDeleteCookiesOnLoad = true, bool headlessAutomation = false)
        {
            if (IsWatchMoreDomain(url))
            {
                return CreateWatchMoreCaptcha(url, autoDeleteCookiesOnLoad, headlessAutomation);
            }
            if (IsSpecialDomain(url))
            {
                return CreateSpecialCaptcha(url, autoDeleteCookiesOnLoad, headlessAutomation);
            }
            return CreateGeneralCaptcha(url, autoDeleteCookiesOnLoad, headlessAutomation);
        }

        private async Task<bool> ShowCaptchaWindowWithFocusHandlingAsync(CaptchaWindow captchaWin, bool useNovelFocusStealth)
        {
            if (captchaWin == null)
            {
                return false;
            }

            bool shouldHideToTray = useNovelFocusStealth && !_lightNovelFocusTrayHidden;
            if (shouldHideToTray)
            {
                HideMainWindowToFocusTray();
            }

            try
            {
                return await captchaWin.ShowNonBlockingAsync();
            }
            finally
            {
                if (shouldHideToTray)
                {
                    RestoreMainWindowFromFocusTray(activateWindow: false);
                }
            }
        }
    }
}

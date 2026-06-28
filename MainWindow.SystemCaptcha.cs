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
            var button = sender as Button;
            if (button == btnFetchCaptcha) url = txtTagUrl.Text;
            else if (button == btnNhentaiFetchCaptcha) url = txtNhentaiTagUrl.Text;
            else if (button == btnViHentaiFetchCaptcha) url = txtViHentaiTagUrl.Text;
            else if (button == btnTruyenqqFetchCaptcha) url = txtTruyenqqTagUrl.Text;
            else if (button == btnNettruyenFetchCaptcha) url = txtNettruyenTagUrl.Text;
            else if (button == btnHakoFetchCaptcha) url = txtHakoTagUrl.Text;
            else if (button == btnTruyenggvnFetchCaptcha) url = txtTruyenggvnTagUrl.Text;
            else if (button == btnHentai2readFetchCaptcha) url = txtHentai2readTagUrl.Text;
            else if (button == btnHentaieraFetchCaptcha) url = txtHentaieraTagUrl.Text;

            if (string.IsNullOrWhiteSpace(url))
            {
                ShowWarning("Vui lòng nhập TARGET TAG URL trước.", "Thông báo");
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

            if (await captchaWin.ShowNonBlockingAsync())
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

                if (await captchaWin.ShowNonBlockingAsync())
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
                    _httpClient.DefaultRequestHeaders.UserAgent.Clear();
                    _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(captchaWin.UserAgent);
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
    }
}

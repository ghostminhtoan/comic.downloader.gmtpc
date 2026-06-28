using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private const string LatestUpdateUrl = "https://github.com/ghostminhtoan/getlink210-GMTPC/releases/download/latest/Comic-GMTPC.latest.exe";
        private bool _isUpdateCheckInProgress;
        private bool _isUpdateInstallInProgress;

        private sealed class RemoteUpdateInfo
        {
            public long? ContentLength { get; set; }
            public DateTimeOffset? LastModified { get; set; }
            public string EntityTag { get; set; }
            public string FinalUrl { get; set; }
        }

        private string GetUpdateDownloadRoot()
        {
            return Path.Combine(PortablePaths.PortableDataRoot, "updates");
        }

        private string GetDownloadedLatestExePath()
        {
            return Path.Combine(GetUpdateDownloadRoot(), "Comic-GMTPC.latest.exe");
        }

        private string GetUpdateLauncherScriptPath()
        {
            return Path.Combine(GetUpdateDownloadRoot(), "apply_update.cmd");
        }

        private void RefreshUpdateSectionContent()
        {
            if (_updateContentText != null)
            {
                _updateContentText.Text = (_isVietnameseUi ? "Build hiện tại" : "Current build") + $": {BuildInfo.DisplayText}\n\n" +
                                          (_isVietnameseUi ? "App root" : "App root") + $": {PortablePaths.AppRoot}\n" +
                                          (_isVietnameseUi ? "Download root mặc định" : "Default download root") + $": {PortablePaths.DefaultDownloadRoot}\n" +
                                          (_isVietnameseUi ? "WebView2 portable data" : "Portable WebView2 data") + $": {PortablePaths.WebView2UserDataFolder}\n" +
                                          (_isVietnameseUi ? "Nguồn auto update" : "Auto update source") + $": {LatestUpdateUrl}\n\n" +
                                          (_isVietnameseUi
                                              ? "Checklist nhanh: build xong, quét Watch, mở thử vài chapter, rồi mới đóng gói."
                                              : "Quick checklist: build clean, refresh Watch, test a few chapter transitions, then package.");
            }

            if (_updateStatusText != null && string.IsNullOrWhiteSpace(_updateStatusText.Text))
            {
                _updateStatusText.Text = _isVietnameseUi
                    ? "Sẵn sàng kiểm tra bản mới."
                    : "Ready to check latest build.";
            }

            UpdateUpdateButtonState();
        }

        private void UpdateUpdateButtonState()
        {
            if (_btnCheckUpdates != null)
            {
                _btnCheckUpdates.IsEnabled = !_isUpdateCheckInProgress && !_isUpdateInstallInProgress;
                _btnCheckUpdates.Content = _isUpdateCheckInProgress
                    ? (_isVietnameseUi ? "ĐANG KIỂM TRA..." : "CHECKING...")
                    : (_isVietnameseUi ? "KIỂM TRA CẬP NHẬT" : "CHECK UPDATES");
            }

            if (_btnInstallLatest != null)
            {
                _btnInstallLatest.IsEnabled = !_isUpdateCheckInProgress && !_isUpdateInstallInProgress;
                _btnInstallLatest.Content = _isUpdateInstallInProgress
                    ? (_isVietnameseUi ? "ĐANG TẢI & CÀI..." : "DOWNLOADING & INSTALLING...")
                    : (_isVietnameseUi ? "TẢI & CÀI BẢN MỚI" : "DOWNLOAD & INSTALL");
            }
        }

        private void SetUpdateStatus(string message)
        {
            Dispatcher.Invoke(() =>
            {
                if (_updateStatusText != null)
                {
                    _updateStatusText.Text = message ?? string.Empty;
                }
            });
        }

        private async Task<RemoteUpdateInfo> RequestRemoteUpdateInfoAsync()
        {
            using (var request = new HttpRequestMessage(HttpMethod.Head, LatestUpdateUrl))
            using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                return new RemoteUpdateInfo
                {
                    ContentLength = response.Content.Headers.ContentLength,
                    LastModified = response.Content.Headers.LastModified,
                    EntityTag = response.Headers.ETag != null ? response.Headers.ETag.Tag : null,
                    FinalUrl = response.RequestMessage != null && response.RequestMessage.RequestUri != null
                        ? response.RequestMessage.RequestUri.ToString()
                        : LatestUpdateUrl
                };
            }
        }

        private static string FormatByteSize(long? bytes)
        {
            if (!bytes.HasValue || bytes.Value < 0)
            {
                return "unknown";
            }

            double size = bytes.Value;
            string[] units = { "B", "KB", "MB", "GB" };
            int unitIndex = 0;
            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1}", size, units[unitIndex]);
        }

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            if (_isUpdateCheckInProgress || _isUpdateInstallInProgress)
            {
                return;
            }

            _isUpdateCheckInProgress = true;
            UpdateUpdateButtonState();
            SetUpdateStatus(_isVietnameseUi ? "Đang kiểm tra file update..." : "Checking update package...");

            try
            {
                RemoteUpdateInfo info = await RequestRemoteUpdateInfoAsync();
                string timeText = info.LastModified.HasValue
                    ? info.LastModified.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                    : (_isVietnameseUi ? "không rõ" : "unknown");

                SetUpdateStatus((_isVietnameseUi ? "Đã kết nối file update." : "Update package reachable.") +
                    Environment.NewLine +
                    (_isVietnameseUi ? "Dung lượng" : "Size") + $": {FormatByteSize(info.ContentLength)}" + Environment.NewLine +
                    (_isVietnameseUi ? "Cập nhật lúc" : "Last modified") + $": {timeText}");
            }
            catch (Exception ex)
            {
                SetUpdateStatus((_isVietnameseUi ? "Kiểm tra update thất bại" : "Update check failed") + $": {ex.Message}");
            }
            finally
            {
                _isUpdateCheckInProgress = false;
                UpdateUpdateButtonState();
            }
        }

        private async void BtnInstallLatest_Click(object sender, RoutedEventArgs e)
        {
            if (_isUpdateCheckInProgress || _isUpdateInstallInProgress)
            {
                return;
            }

            _isUpdateInstallInProgress = true;
            UpdateUpdateButtonState();

            try
            {
                Directory.CreateDirectory(GetUpdateDownloadRoot());

                string downloadedExePath = GetDownloadedLatestExePath();
                SetUpdateStatus(_isVietnameseUi ? "Đang tải bản mới nhất..." : "Downloading latest build...");

                using (var response = await _httpClient.GetAsync(LatestUpdateUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    using (var responseStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(downloadedExePath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await responseStream.CopyToAsync(fileStream);
                    }
                }

                string scriptPath = GetUpdateLauncherScriptPath();
                string currentExePath = Process.GetCurrentProcess().MainModule.FileName;
                string backupExePath = currentExePath + ".bak";
                string scriptBody =
                    "@echo off" + Environment.NewLine +
                    "setlocal" + Environment.NewLine +
                    "set \"TARGET=" + currentExePath + "\"" + Environment.NewLine +
                    "set \"SOURCE=" + downloadedExePath + "\"" + Environment.NewLine +
                    "set \"BACKUP=" + backupExePath + "\"" + Environment.NewLine +
                    "for /l %%i in (1,1,90) do (" + Environment.NewLine +
                    "  move /y \"%TARGET%\" \"%BACKUP%\" >nul 2>nul" + Environment.NewLine +
                    "  if not errorlevel 1 goto replace" + Environment.NewLine +
                    "  timeout /t 1 /nobreak >nul" + Environment.NewLine +
                    ")" + Environment.NewLine +
                    "exit /b 1" + Environment.NewLine +
                    ":replace" + Environment.NewLine +
                    "move /y \"%SOURCE%\" \"%TARGET%\" >nul 2>nul" + Environment.NewLine +
                    "if errorlevel 1 exit /b 1" + Environment.NewLine +
                    "start \"\" \"%TARGET%\"" + Environment.NewLine +
                    "timeout /t 2 /nobreak >nul" + Environment.NewLine +
                    "del /f /q \"%BACKUP%\" >nul 2>nul" + Environment.NewLine +
                    "del /f /q \"%~f0\" >nul 2>nul" + Environment.NewLine;
                File.WriteAllText(scriptPath, scriptBody, Encoding.ASCII);

                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c \"" + scriptPath + "\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = PortablePaths.AppRoot
                };
                Process.Start(startInfo);

                SetUpdateStatus(_isVietnameseUi
                    ? "Đã tải xong. App sẽ đóng để thay file và mở lại bản mới."
                    : "Download complete. App will close, replace the EXE, and relaunch.");

                await Task.Delay(500);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                SetUpdateStatus((_isVietnameseUi ? "Cài update thất bại" : "Update install failed") + $": {ex.Message}");
                _isUpdateInstallInProgress = false;
                UpdateUpdateButtonState();
                return;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private void BtnSourceSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchSourceBooksWithGoogle();
        }

        private void TxtSourceSearchBook_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                SearchSourceBooksWithGoogle();
            }
        }

        private async void SearchSourceBooksWithGoogle()
        {
            string rawInput = txtSourceSearchBook?.Text;
            if (string.IsNullOrWhiteSpace(rawInput))
            {
                SetSourceSearchStatus(
                    "Please enter book name(s).",
                    "Vui lòng nhập tên truyện.");
                return;
            }

            var bookNames = rawInput.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(name => name.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();

            if (bookNames.Count == 0)
            {
                SetSourceSearchStatus(
                    "Please enter book name(s).",
                    "Vui lòng nhập tên truyện.");
                return;
            }

            List<string> domains = GetSelectedSourceSearchDomains();
            if (domains.Count == 0)
            {
                SetSourceSearchStatus(
                    "Please check at least one domain.",
                    "Vui lòng tick ít nhất một domain.");
                return;
            }

            bool openNewWindow = chkSourceSearchNewWindow?.IsChecked == true;
            int openedCount = 0;
            bool isFirst = true;

            string browserPath = null;
            string newWindowArg = "--new-window";

            if (openNewWindow)
            {
                browserPath = GetDefaultBrowserPath(out newWindowArg);

                if (string.IsNullOrEmpty(browserPath))
                {
                    string chromePath = GetAppPathFromRegistry("chrome.exe");
                    string edgePath = GetAppPathFromRegistry("msedge.exe");
                    string firefoxPath = GetAppPathFromRegistry("firefox.exe");

                    if (!string.IsNullOrEmpty(chromePath) && System.IO.File.Exists(chromePath))
                    {
                        browserPath = chromePath;
                        newWindowArg = "--new-window";
                    }
                    else if (!string.IsNullOrEmpty(edgePath) && System.IO.File.Exists(edgePath))
                    {
                        browserPath = edgePath;
                        newWindowArg = "--new-window";
                    }
                    else if (!string.IsNullOrEmpty(firefoxPath) && System.IO.File.Exists(firefoxPath))
                    {
                        browserPath = firefoxPath;
                        newWindowArg = "-new-window";
                    }
                }
            }

            bool isBing = true;
            string engineName = "Bing";
            if (cmbSearchEngine != null)
            {
                var selectedItem = cmbSearchEngine.SelectedItem as ComboBoxItem;
                if (selectedItem != null)
                {
                    engineName = selectedItem.Content.ToString();
                    isBing = engineName.Equals("Bing", StringComparison.OrdinalIgnoreCase);
                }
            }

            try
            {
                foreach (string book in bookNames)
                {
                    foreach (string domain in domains)
                    {
                        string query = WebUtility.UrlEncode("site:" + ResolveSourceSearchDomain(domain) + " " + book);
                        string url = isBing ? "https://www.bing.com/search?q=" + query : "https://www.google.com/search?q=" + query;

                        if (openNewWindow && !string.IsNullOrEmpty(browserPath))
                        {
                            if (isFirst)
                            {
                                Process.Start(new ProcessStartInfo
                                {
                                    FileName = browserPath,
                                    Arguments = $"{newWindowArg} \"{url}\"",
                                    UseShellExecute = false
                                });
                                isFirst = false;
                            }
                            else
                            {
                                Process.Start(new ProcessStartInfo
                                {
                                    FileName = browserPath,
                                    Arguments = $"\"{url}\"",
                                    UseShellExecute = false
                                });
                            }
                        }
                        else
                        {
                            OpenSourceSearchUrl(url);
                        }

                        openedCount++;
                        await System.Threading.Tasks.Task.Delay(300);
                    }
                }

                SetSourceSearchStatus(
                    $"Opened {engineName} search for {openedCount} query/queries.",
                    $"Đã mở {engineName} search cho {openedCount} lượt tìm kiếm.");
            }
            catch (Exception ex)
            {
                SetSourceSearchStatus(
                    $"Open {engineName} search failed: " + ex.Message,
                    $"Mở {engineName} search lỗi: " + ex.Message);
            }
        }

        private string GetDefaultBrowserPath(out string newWindowArg)
        {
            newWindowArg = "--new-window";
            try
            {
                string progId = null;
                using (var userChoiceKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice"))
                {
                    if (userChoiceKey != null)
                    {
                        progId = userChoiceKey.GetValue("ProgId")?.ToString();
                    }
                }

                if (string.IsNullOrEmpty(progId))
                {
                    using (var userChoiceKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice"))
                    {
                        if (userChoiceKey != null)
                        {
                            progId = userChoiceKey.GetValue("ProgId")?.ToString();
                        }
                    }
                }

                if (!string.IsNullOrEmpty(progId))
                {
                    string command = null;
                    using (var commandKey = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command"))
                    {
                        if (commandKey != null)
                        {
                            command = commandKey.GetValue("")?.ToString();
                        }
                    }

                    if (!string.IsNullOrEmpty(command))
                    {
                        string exePath = null;
                        command = command.Trim();
                        if (command.StartsWith("\""))
                        {
                            int nextQuote = command.IndexOf("\"", 1);
                            if (nextQuote > 1)
                            {
                                exePath = command.Substring(1, nextQuote - 1);
                            }
                        }
                        else
                        {
                            int firstSpace = command.IndexOf(" ");
                            exePath = firstSpace > 0 ? command.Substring(0, firstSpace) : command;
                        }

                        if (!string.IsNullOrEmpty(exePath) && System.IO.File.Exists(exePath))
                        {
                            string exeLower = exePath.ToLowerInvariant();
                            if (exeLower.Contains("firefox"))
                            {
                                newWindowArg = "-new-window";
                            }
                            else
                            {
                                newWindowArg = "--new-window";
                            }
                            return exePath;
                        }
                    }
                }
            }
            catch {}
            return null;
        }

        private string GetAppPathFromRegistry(string exeName)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exeName}"))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("");
                        if (val != null)
                        {
                            return val.ToString();
                        }
                    }
                }

                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exeName}"))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("");
                        if (val != null)
                        {
                            return val.ToString();
                        }
                    }
                }
            }
            catch {}
            return null;
        }

        private List<string> GetSelectedSourceSearchDomains()
        {
            if (cmbSourceSearchDomains == null)
            {
                return new List<string>();
            }

            return cmbSourceSearchDomains.Items
                .OfType<ComboBoxItem>()
                .Select(item => item.Content as CheckBox)
                .Where(checkBox => checkBox?.IsChecked == true)
                .Select(checkBox => (checkBox.Tag as string ?? checkBox.Content as string ?? string.Empty).Trim())
                .Where(domain => !string.IsNullOrWhiteSpace(domain))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private string ResolveSourceSearchDomain(string domain)
        {
            string key = NormalizeSourceSearchDomain(domain).ToLowerInvariant();
            if (key == "nettruyen.tech")
            {
                string redirectDomain = NormalizeSourceSearchDomain(txtNettruyenTechRedirectDomain?.Text);
                return string.IsNullOrWhiteSpace(redirectDomain) ? "nettruyen.tech" : redirectDomain;
            }

            if (key == "damconuong.shop" || key == "damconuong")
            {
                string redirectDomain = NormalizeSourceSearchDomain(txtDamconuongRedirectDomain?.Text);
                return string.IsNullOrWhiteSpace(redirectDomain) ? "damconuong.shop" : redirectDomain;
            }

            switch (key)
            {
                case "truyenqq":
                    return "truyenqqko.com";
                case "nettruyen":
                    return "nettruyenviet10.com";
                case "mangadex":
                    return "mangadex.org";
                case "daomeoden":
                    return "daomeoden.net";
                case "hentaivn":
                    return "vi-hentai.pro";
                case "sayhentai":
                    return "sayhentai.cx";
                case "hentaiforce":
                    return "hentaiforce.net";
                case "nhentai.net":
                    return "nhentai.net";
                case "nhentai":
                    return "nhentai.xxx";
                case "hentai2read":
                    return "hentai2read.com";
                case "hentaiera":
                    return "hentaiera.com";
                case "hako":
                    return "ln.hako.vn";
                default:
                    return key;
            }
        }

        private static string NormalizeSourceSearchDomain(string domain)
        {
            string cleanDomain = (domain ?? string.Empty).Trim();
            if (cleanDomain.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                cleanDomain.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (Uri.TryCreate(cleanDomain, UriKind.Absolute, out Uri uri))
                {
                    cleanDomain = uri.Host;
                }
            }

            int slashIndex = cleanDomain.IndexOf('/');
            return (slashIndex >= 0 ? cleanDomain.Substring(0, slashIndex) : cleanDomain).Trim();
        }

        private void OpenSourceSearchUrl(string url)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }

        private void SetSourceSearchStatus(string englishText, string vietnameseText)
        {
            if (txtSourceSearchStatus != null)
            {
                txtSourceSearchStatus.Text = _isVietnameseUi ? vietnameseText : englishText;
            }
        }

        private void UpdateSourceSearchLanguage()
        {
            if (tabSourceSearchRootItem != null) tabSourceSearchRootItem.Header = _isVietnameseUi ? "Tìm kiếm" : "Search";
            if (txtSourceSearchTitle != null) txtSourceSearchTitle.Text = _isVietnameseUi ? "TÌM TRUYỆN HÀNG LOẠT" : "BATCH SEARCH BOOKS";
            if (txtSourceSearchBookLabel != null) txtSourceSearchBookLabel.Text = _isVietnameseUi ? "DANH SÁCH TÊN TRUYỆN (MỖI TRUYỆN 1 DÒNG)" : "BOOK LIST (ONE PER LINE)";
            if (txtSourceSearchDomainLabel != null) txtSourceSearchDomainLabel.Text = _isVietnameseUi ? "CHỌN NGUỒN" : "SELECT DOMAIN";
            if (txtSourceSearchEngineLabel != null) txtSourceSearchEngineLabel.Text = _isVietnameseUi ? "ENGINE TÌM KIẾM" : "SEARCH ENGINE";
            if (chkSourceSearchNewWindow != null) chkSourceSearchNewWindow.Content = _isVietnameseUi ? "Mở trong cửa sổ mới" : "Open in new window";
            if (btnSourceSearch != null) btnSourceSearch.Content = _isVietnameseUi ? "TÌM" : "SEARCH";
            UpdateSourceSearchDomainsText();
        }

        private void CmbSourceSearchDomains_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateSourceSearchDomainsText();
        }

        private void CheckBox_SourceSearchDomainChanged(object sender, RoutedEventArgs e)
        {
            UpdateSourceSearchDomainsText();
        }

        private void UpdateSourceSearchDomainsText()
        {
            if (cmbSourceSearchDomains == null) return;
            var selected = GetSelectedSourceSearchDomains();
            if (selected.Count == 0)
            {
                cmbSourceSearchDomains.Text = _isVietnameseUi ? "Chọn nguồn..." : "Select domains...";
            }
            else
            {
                cmbSourceSearchDomains.Text = string.Join(", ", selected);
            }
        }
    }
}

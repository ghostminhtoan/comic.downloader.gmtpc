using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private void SelectMangaSourceRoot()
        {
            if (tabLeftPanel != null && tabMangaSourceRootItem != null)
            {
                tabLeftPanel.SelectedItem = tabMangaSourceRootItem;
            }
        }

        private void SelectHentaiSourceRoot()
        {
            if (tabLeftPanel != null && tabHentaiSourceRootItem != null)
            {
                tabLeftPanel.SelectedItem = tabHentaiSourceRootItem;
            }
        }

        private void SelectNovelSourceRoot()
        {
            if (tabLeftPanel != null && tabLightNovelRootItem != null)
            {
                tabLeftPanel.SelectedItem = tabLightNovelRootItem;
            }
        }

        public bool IsSupportedDomain(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            string lower = url.ToLowerInvariant();
            return lower.Contains("truyenqq") || lower.Contains("qquyen") || lower.Contains("nettruyen") ||
                   lower.Contains("daomeoden") || lower.Contains("dilib.vn") || lower.Contains("vi-hentai") || lower.Contains("vihentai") ||
                   lower.Contains("sayhentai") || lower.Contains("truyengg") || lower.Contains("hentaiforce") ||
                   lower.Contains("damconuong") ||
                   lower.Contains("nhentai") || lower.Contains("hentai2read") || lower.Contains("hentaiera") ||
                   lower.Contains("hako");
        }

        private async Task WaitAndScrapeAsync(Button fetchButton, RoutedEventHandler scrapeHandler)
        {
            var oldCursor = Cursor;
            try
            {
                Cursor = Cursors.Wait;
                if (lblStatus != null)
                {
                    lblStatus.Text = "⏳ Đang xử lý dữ liệu link... (Processing link...)";
                }

                await Task.Delay(150);
                int timeoutCount = 0;
                while (fetchButton != null && !fetchButton.IsEnabled && timeoutCount < 120)
                {
                    await Task.Delay(500);
                    timeoutCount++;
                }

                scrapeHandler?.Invoke(this, new RoutedEventArgs());
            }
            catch
            {
            }
            finally
            {
                Cursor = oldCursor;
            }
        }

        public async void RouteAndProcessInputLink(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            url = url.Trim();
            string lowerUrl = url.ToLowerInvariant();
            SelectAppSection(AppSection.Download);

            if (lowerUrl.Contains("hako.vn") || lowerUrl.Contains("hako.re") || lowerUrl.Contains("hako"))
            {
                if (tabDownloadRoot != null && tabDownloadRoot.Items.Count >= 2)
                {
                    tabDownloadRoot.SelectedIndex = 1;
                }

                SelectNovelSourceRoot();

                if (txtHakoTagUrl != null)
                {
                    txtHakoTagUrl.Text = url;
                }

                BtnHakoFetchInfo_Click(this, new RoutedEventArgs());
                await WaitAndScrapeAsync(btnHakoFetchInfo, BtnHakoScrape_Click);
                return;
            }

            if (tabDownloadRoot != null && tabDownloadRoot.Items.Count >= 2)
            {
                tabDownloadRoot.SelectedIndex = 0;
            }

            SelectMangaSourceRoot();

            if (lowerUrl.Contains("truyenqq") || lowerUrl.Contains("qquyen"))
            {
                if (tabManga != null) tabManga.SelectedIndex = 0;
                if (txtTruyenqqTagUrl != null) txtTruyenqqTagUrl.Text = url;
                BtnTruyenqqFetchInfo_Click(this, new RoutedEventArgs());
                await WaitAndScrapeAsync(btnTruyenqqFetchInfo, BtnTruyenqqScrape_Click);
            }
            else if (lowerUrl.Contains("nettruyen.tech"))
            {
                if (tabManga != null) tabManga.SelectedIndex = 2;
                if (txtNettruyenTechTagUrl != null) txtNettruyenTechTagUrl.Text = url;
                BtnNettruyenTechFetchInfo_Click(this, new RoutedEventArgs());
                await WaitAndScrapeAsync(btnNettruyenTechFetchInfo, BtnNettruyenTechScrape_Click);
            }
            else if (lowerUrl.Contains("nettruyen") || (!lowerUrl.Contains("nhentai.net") && (lowerUrl.Contains("truyenrr") || lowerUrl.Contains("truyenco") || lowerUrl.Contains("truyenplus"))))
            {
                if (tabManga != null) tabManga.SelectedIndex = 1;
                if (txtNettruyenTagUrl != null) txtNettruyenTagUrl.Text = url;
                BtnNettruyenFetchInfo_Click(this, new RoutedEventArgs());
                await WaitAndScrapeAsync(btnNettruyenFetchInfo, BtnNettruyenScrape_Click);
            }
            else if (lowerUrl.Contains("daomeoden"))
            {
                SelectHentaiSourceRoot();
                SelectHentaiTabByHeader("daomeoden");
                if (txtDaomeodenTagUrl != null) txtDaomeodenTagUrl.Text = url;
                BtnDaomeodenFetchInfo_Click(this, new RoutedEventArgs());
                await WaitAndScrapeAsync(btnDaomeodenFetchInfo, BtnDaomeodenScrape_Click);
            }
            else if (lowerUrl.Contains("damconuong"))
            {
                SelectHentaiSourceRoot();
                SelectHentaiTabByHeader("damconuong");
                if (txtDamconuongTagUrl != null) txtDamconuongTagUrl.Text = url;
                if (IsDamconuongCategoryUrl(url))
                {
                    BtnDamconuongFetchInfo_Click(this, new RoutedEventArgs());
                    await WaitAndScrapeAsync(btnDamconuongFetchInfo, BtnDamconuongScrape_Click);
                }
                else
                {
                    await ImportDamconuongDirectLinksAsync(new List<string> { url });
                }
            }
            else if (lowerUrl.Contains("dilib.vn"))
            {
                if (tabManga != null) tabManga.SelectedIndex = 3;
                if (txtDilibTagUrl != null) txtDilibTagUrl.Text = url;
                BtnDilibFetchInfo_Click(this, new RoutedEventArgs());
                await WaitAndScrapeAsync(btnDilibFetchInfo, BtnDilibScrape_Click);
            }
            else if (lowerUrl.Contains("vi-hentai") || lowerUrl.Contains("vihentai"))
            {
                SelectHentaiSourceRoot();
                if (tabHentai != null) tabHentai.SelectedIndex = 0;
                if (txtViHentaiTagUrl != null) txtViHentaiTagUrl.Text = url;
                BtnViHentaiFetchInfo_Click(this, new RoutedEventArgs());
                await WaitAndScrapeAsync(btnViHentaiFetchInfo, BtnViHentaiScrape_Click);
            }
            else if (lowerUrl.Contains("sayhentai") || lowerUrl.Contains("truyengg"))
            {
                SelectHentaiSourceRoot();
                SelectHentaiTabByHeader("sayhentai");
                if (txtTruyenggvnTagUrl != null) txtTruyenggvnTagUrl.Text = url;
                BtnTruyenggvnFetchInfo_Click(this, new RoutedEventArgs());
                await WaitAndScrapeAsync(btnTruyenggvnFetchInfo, BtnTruyenggvnScrape_Click);
            }
            else if (lowerUrl.Contains("hentaiforce"))
            {
                SelectHentaiSourceRoot();
                SelectHentaiTabByHeader("hentaiforce");
                if (txtTagUrl != null) txtTagUrl.Text = url;
                BtnFetchInfo_Click(this, new RoutedEventArgs());
                await WaitAndScrapeAsync(btnFetchInfo, BtnScrape_Click);
            }
            else if (lowerUrl.Contains("nhentai"))
            {
                SelectHentaiSourceRoot();
                SelectHentaiTabByHeader("nhentai");
                if (txtNhentaiTagUrl != null) txtNhentaiTagUrl.Text = url;
                BtnNhentaiFetchInfo_Click(this, new RoutedEventArgs());
                await WaitAndScrapeAsync(btnNhentaiFetchInfo, BtnNhentaiScrape_Click);
            }
            else if (lowerUrl.Contains("hentai2read"))
            {
                SelectHentaiSourceRoot();
                SelectHentaiTabByHeader("hentai2read");
                if (txtHentai2readTagUrl != null) txtHentai2readTagUrl.Text = url;
                BtnHentai2readFetchInfo_Click(this, new RoutedEventArgs());
                await WaitAndScrapeAsync(btnHentai2readFetchInfo, BtnHentai2readScrape_Click);
            }
            else if (lowerUrl.Contains("hentaiera"))
            {
                SelectHentaiSourceRoot();
                SelectHentaiTabByHeader("hentaiera");
                if (txtHentaieraTagUrl != null) txtHentaieraTagUrl.Text = url;
                BtnHentaieraFetchInfo_Click(this, new RoutedEventArgs());
                await WaitAndScrapeAsync(btnHentaieraFetchInfo, BtnHentaieraScrape_Click);
            }
        }

        public async Task AppendSupportedInputLinks(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var links = text
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (links.Count == 0)
            {
                return;
            }

            var existingLinks = new HashSet<string>(
                _scrapedItems
                    .Select(item => item?.Link)
                    .Where(link => !string.IsNullOrWhiteSpace(link)),
                StringComparer.OrdinalIgnoreCase);

            foreach (string link in links)
            {
                bool handled = await TryAppendSupportedDirectLinkAsync(link, showMessageBox: false);
                if (handled)
                {
                    MarkNewlyImportedItemsChecked(existingLinks);
                    ClearAppendCompletedStatus();
                }
            }
        }

        private void MarkNewlyImportedItemsChecked(ISet<string> existingLinks)
        {
            if (existingLinks == null)
            {
                return;
            }

            foreach (GalleryItem item in _scrapedItems)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Link) || existingLinks.Contains(item.Link))
                {
                    continue;
                }

                item.IsChecked = true;
                existingLinks.Add(item.Link);
            }
        }

        private void ClearAppendCompletedStatus()
        {
            if (lblStatus == null)
            {
                return;
            }

            string status = lblStatus.Text ?? string.Empty;
            if (status.StartsWith("Import completed", StringComparison.OrdinalIgnoreCase) ||
                status.IndexOf("Imported ", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                lblStatus.Text = "Ready.";
            }
        }

        private async Task<bool> TryAppendSupportedDirectLinkAsync(string url, bool showMessageBox = true)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            string lowerUrl = url.Trim().ToLowerInvariant();
            SelectAppSection(AppSection.Download);

            if (lowerUrl.Contains("hako.vn") || lowerUrl.Contains("hako.re") || lowerUrl.Contains("hako"))
            {
                if (tabDownloadRoot != null && tabDownloadRoot.Items.Count >= 2) tabDownloadRoot.SelectedIndex = 1;
                SelectNovelSourceRoot();
                await ImportLightNovelDirectLinksAsync(new List<string> { url });
                return true;
            }

            if (tabDownloadRoot != null && tabDownloadRoot.Items.Count >= 2) tabDownloadRoot.SelectedIndex = 0;
            SelectMangaSourceRoot();

            if (lowerUrl.Contains("truyenqq") || lowerUrl.Contains("qquyen"))
            {
                if (tabManga != null) tabManga.SelectedIndex = 0;
                await ImportTruyenqqDirectLinksAsync(new List<string> { url }, showMessageBox);
                return true;
            }

            if (lowerUrl.Contains("nettruyen.tech"))
            {
                if (tabManga != null) tabManga.SelectedIndex = 2;
                await ImportNettruyenTechDirectLinksAsync(new List<string> { url }, showMessageBox);
                return true;
            }

            if (lowerUrl.Contains("nettruyen") || (!lowerUrl.Contains("nhentai.net") && (lowerUrl.Contains("truyenrr") || lowerUrl.Contains("truyenco") || lowerUrl.Contains("truyenplus"))))
            {
                if (tabManga != null) tabManga.SelectedIndex = 1;
                await ImportNettruyenDirectLinksAsync(new List<string> { url }, showMessageBox);
                return true;
            }

            if (lowerUrl.Contains("daomeoden"))
            {
                SelectHentaiSourceRoot();
                SelectHentaiTabByHeader("daomeoden");
                await ImportDaomeodenDirectLinksAsync(new List<string> { url });
                return true;
            }

            if (lowerUrl.Contains("damconuong"))
            {
                SelectHentaiSourceRoot();
                SelectHentaiTabByHeader("damconuong");
                await ImportDamconuongDirectLinksAsync(new List<string> { url }, showMessageBox);
                return true;
            }

            if (lowerUrl.Contains("dilib.vn"))
            {
                if (tabManga != null) tabManga.SelectedIndex = 3;
                await ImportDilibDirectLinksAsync(new List<string> { url }, clearExisting: false, showMessageBox: showMessageBox);
                return true;
            }

            if (lowerUrl.Contains("vi-hentai") || lowerUrl.Contains("vihentai"))
            {
                SelectHentaiSourceRoot();
                if (tabHentai != null) tabHentai.SelectedIndex = 0;
                await ImportViHentaiDirectLinksAsync(new List<string> { url }, showMessageBox);
                return true;
            }

            if (lowerUrl.Contains("sayhentai") || lowerUrl.Contains("truyengg"))
            {
                SelectHentaiSourceRoot();
                SelectHentaiTabByHeader("sayhentai");
                await ImportTruyenggvnDirectLinksAsync(new List<string> { url });
                return true;
            }

            if (lowerUrl.Contains("hentaiforce"))
            {
                SelectHentaiSourceRoot();
                SelectHentaiTabByHeader("hentaiforce");
                await ImportDirectLinksAsync(new List<string> { url }, showMessageBox);
                return true;
            }

            if (lowerUrl.Contains("nhentai"))
            {
                SelectHentaiSourceRoot();
                SelectHentaiTabByHeader("nhentai");
                await ImportNhentaiDirectLinksAsync(new List<string> { url }, showMessageBox);
                return true;
            }

            if (lowerUrl.Contains("hentai2read"))
            {
                SelectHentaiSourceRoot();
                SelectHentaiTabByHeader("hentai2read");
                await ImportHentai2readDirectLinksAsync(new List<string> { url });
                return true;
            }

            if (lowerUrl.Contains("hentaiera"))
            {
                SelectHentaiSourceRoot();
                SelectHentaiTabByHeader("hentaiera");
                await ImportHentaieraDirectLinksAsync(new List<string> { url }, showMessageBox);
                return true;
            }

            return false;
        }

        private void SelectHentaiTabByHeader(string headerKeyword)
        {
            if (tabHentai == null)
            {
                return;
            }

            for (int i = 0; i < tabHentai.Items.Count; i++)
            {
                if (tabHentai.Items[i] is TabItem tabItem &&
                    tabItem.Header?.ToString()?.ToLowerInvariant().Contains(headerKeyword) == true)
                {
                    tabHentai.SelectedIndex = i;
                    return;
                }
            }
        }
    }
}

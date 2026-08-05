using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private bool _isUpdatingHitomiLaUrl = false;

        // Class đại diện cấu trúc gg.js của hitomi
        public class HitomiGG
        {
            public int MDefault { get; set; } = 0;
            public Dictionary<int, int> MMap { get; set; } = new Dictionary<int, int>();
            public string B { get; set; } = string.Empty;
            public long LastRetrieval { get; set; } = 0;

            public async Task RefreshAsync(MainWindow window)
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (LastRetrieval > 0 && LastRetrieval + 60000 >= now)
                {
                    return;
                }

                try
                {
                    string body = await window.FetchStringAsync("https://ltn.gold-usergeneratedcontent.net/gg.js", CancellationToken.None);
                    if (string.IsNullOrEmpty(body)) return;

                    var reDefault = new Regex(@"var o = (\d)");
                    var reO = new Regex(@"o = (\d); break;");
                    var reCase = new Regex(@"case (\d+):");
                    var reB = new Regex(@"b: '(.+)'");

                    var capDefault = reDefault.Match(body);
                    if (capDefault.Success)
                    {
                        MDefault = int.Parse(capDefault.Groups[1].Value);
                    }

                    var capO = reO.Match(body);
                    if (capO.Success)
                    {
                        int o = int.Parse(capO.Groups[1].Value);
                        MMap.Clear();
                        foreach (Match cap in reCase.Matches(body))
                        {
                            if (int.TryParse(cap.Groups[1].Value, out int caseVal))
                            {
                                MMap[caseVal] = o;
                            }
                        }
                    }

                    var capB = reB.Match(body);
                    if (capB.Success)
                    {
                        B = capB.Groups[1].Value;
                    }

                    LastRetrieval = now;
                }
                catch (Exception ex)
                {
                    window.Log($"[Hitomi GG] Lỗi tải gg.js: {ex.Message}");
                }
            }

            public int GetM(int g)
            {
                if (MMap.TryGetValue(g, out int val)) return val;
                return MDefault;
            }

            public string GetS(string hash)
            {
                var re = new Regex(@"(..)(.)$");
                var match = re.Match(hash);
                if (match.Success)
                {
                    string combined = match.Groups[2].Value + match.Groups[1].Value;
                    int num = Convert.ToInt32(combined, 16);
                    return num.ToString();
                }
                return string.Empty;
            }
        }

        private static readonly HitomiGG _hitomiGG = new HitomiGG();

        // Decode và phân giải link ảnh theo chuẩn hitomi-downloader (Rust/JS version)
        public static async Task<string> ResolveHitomiImageUrlAsync(MainWindow window, string hash, string name, bool isThumbnail = false)
        {
            await _hitomiGG.RefreshAsync(window);

            string ext = name.Split('.').LastOrDefault() ?? "jpg";
            if (isThumbnail)
            {
                // Thumbnail path
                string realPath = Regex.Replace(hash, @"^.*(..)(.)$", "$2/$1/" + hash);
                string url = $"https://a.gold-usergeneratedcontent.net/webpbigtn/{realPath}.webp";
                
                // subdomain_from_url
                string sub = await GetHitomiSubdomainAsync(url, "tn", null);
                return url.Replace("//a.gold-usergeneratedcontent.net/", $"//{sub}.gold-usergeneratedcontent.net/");
            }
            else
            {
                // Full image path
                string b = _hitomiGG.B;
                string s = _hitomiGG.GetS(hash);
                string url = $"https://a.gold-usergeneratedcontent.net/webp/{b}{s}/{hash}.webp"; // Ưu tiên Webp chất lượng cao

                string sub = await GetHitomiSubdomainAsync(url, null, "webp");
                return url.Replace("//a.gold-usergeneratedcontent.net/", $"//{sub}.gold-usergeneratedcontent.net/");
            }
        }

        private static async Task<string> GetHitomiSubdomainAsync(string url, string baseDomain, string dir)
        {
            string retval = "";
            if (string.IsNullOrEmpty(baseDomain))
            {
                if (dir == "webp") retval = "w";
                else if (dir == "avif") retval = "a";
            }

            var match = Regex.Match(url, @"/[0-9a-f]{61}([0-9a-f]{2})([0-9a-f])");
            if (match.Success)
            {
                string combined = match.Groups[2].Value + match.Groups[1].Value;
                int g = Convert.ToInt32(combined, 16);
                int m = _hitomiGG.GetM(g);

                if (string.IsNullOrEmpty(baseDomain))
                {
                    retval = retval + (1 + m);
                }
                else
                {
                    char c = (char)(97 + m);
                    retval = c + baseDomain;
                }
            }
            return retval;
        }

        private void HitomiLaLog(string message)
        {
            Log($"[{DateTime.Now:HH:mm:ss}] [hitomi.la] {message}");
        }

        private void TxtHitomiLaTotalPages_TextChanged(object sender, TextChangedEventArgs e)
        {
        }

        private async void BtnHitomiLaFetchInfo_Click(object sender, RoutedEventArgs e)
        {
            string url = txtHitomiLaTagUrl.Text.Trim();
            if (string.IsNullOrEmpty(url)) return;

            btnHitomiLaFetchInfo.IsEnabled = false;
            lblStatus.Text = "Analyzing hitomi.la page...";
            progressBar.IsIndeterminate = true;

            try
            {
                // Hitomi.la sử dụng AJAX nozomi để get ID. Ta chỉ cần hiển thị thông báo sẵn sàng.
                HitomiLaLog($"Analyzing target URL: {url}");
                lblStatus.Text = "Ready to get links.";
                txtHitomiLaTotalPages.Text = "1";
                txtHitomiLaPageFrom.Text = "1";
                txtHitomiLaPageTo.Text = "1";
            }
            catch (Exception ex)
            {
                HitomiLaLog($"Lỗi: {ex.Message}");
            }
            finally
            {
                btnHitomiLaFetchInfo.IsEnabled = true;
                progressBar.IsIndeterminate = false;
            }
        }

        private async void BtnHitomiLaScrape_Click(object sender, RoutedEventArgs e)
        {
            await ScrapeHitomiLaAsync(false);
        }

        private async void BtnHitomiLaCrawlMore_Click(object sender, RoutedEventArgs e)
        {
            await ScrapeHitomiLaAsync(true);
        }

        private async Task ScrapeHitomiLaAsync(bool append)
        {
            string url = txtHitomiLaTagUrl.Text.Trim();
            if (string.IsNullOrEmpty(url)) return;

            btnHitomiLaScrape.IsEnabled = false;
            btnHitomiLaCrawlMore.IsEnabled = false;
            progressBar.IsIndeterminate = true;

            try
            {
                if (!append)
                {
                    _scrapedItems.Clear();
                }

                // Nếu là URL truyện đơn lẻ: ví dụ https://hitomi.la/doujinshi/tên-truyện-số_id.html hoặc chỉ chứa ID số
                var singleMatch = Regex.Match(url, @"(?:-|/)(\d+)\.html");
                if (singleMatch.Success)
                {
                    string id = singleMatch.Groups[1].Value;
                    await ImportHitomiLaDirectLinksAsync(new List<string> { $"https://hitomi.la/gallery/{id}.html" });
                    return;
                }

                // Nếu không là page list, dùng default fetch homepage
                HitomiLaLog("Fetching index list từ hitomi...");
                // Đọc nozomi từ ltn index
                byte[] data = null;
                using (var httpClient = CreateScopedHttpClient("https://ltn.gold-usergeneratedcontent.net/index-all.nozomi"))
                {
                    data = await httpClient.GetByteArrayAsync("https://ltn.gold-usergeneratedcontent.net/index-all.nozomi");
                }
                if (data != null && data.Length > 0)
                {
                    int count = data.Length / 4;
                    var ids = new List<string>();
                    for (int i = 0; i < Math.Min(count, 40); i++)
                    {
                        int id = BigEndianToInt32(data, i * 4);
                        ids.Add($"https://hitomi.la/gallery/{id}.html");
                    }
                    await ImportHitomiLaDirectLinksAsync(ids, showMessageBox: false);
                }
            }
            catch (Exception ex)
            {
                HitomiLaLog($"Scrape error: {ex.Message}");
            }
            finally
            {
                btnHitomiLaScrape.IsEnabled = true;
                btnHitomiLaCrawlMore.IsEnabled = true;
                progressBar.IsIndeterminate = false;
            }
        }

        private int BigEndianToInt32(byte[] data, int index)
        {
            return (data[index] << 24) | (data[index + 1] << 16) | (data[index + 2] << 8) | data[index + 3];
        }

        private async void BtnHitomiLaPasteDirect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string text = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(text)) return;
                var links = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(l => l.Trim())
                                .Where(l => !string.IsNullOrEmpty(l))
                                .ToList();
                if (links.Count > 0)
                {
                    await ImportHitomiLaDirectLinksAsync(links);
                }
            }
            catch (Exception ex)
            {
                HitomiLaLog($"Paste error: {ex.Message}");
            }
        }

        public async Task ImportHitomiLaDirectLinksAsync(List<string> links, bool showMessageBox = true, bool keepControlsEnabled = false)
        {
            if (links == null || links.Count == 0) return;
            int total = links.Count;
            int imported = 0;
            int failed = 0;

            if (!keepControlsEnabled)
            {
                btnHitomiLaScrape.IsEnabled = false;
                btnHitomiLaFetchInfo.IsEnabled = false;
                if (btnStartDownload != null) btnStartDownload.IsEnabled = false;
            }

            try
            {
                for (int i = 0; i < total; i++)
                {
                    string link = links[i];
                    var idMatch = Regex.Match(link, @"(\d+)(?:\.html)?$");
                    if (!idMatch.Success)
                    {
                        failed++;
                        continue;
                    }

                    string id = idMatch.Groups[1].Value;
                    string apiJsonUrl = $"https://ltn.gold-usergeneratedcontent.net/galleries/{id}.js";

                    try
                    {
                        string jsContent = await FetchStringAsync(apiJsonUrl, CancellationToken.None);
                        if (string.IsNullOrEmpty(jsContent))
                        {
                            failed++;
                            continue;
                        }

                        // Remove "var galleryinfo = " prefix
                        string json = jsContent.Replace("var galleryinfo = ", "").Trim();
                        dynamic galleryInfo = JsonConvert.DeserializeObject(json);

                        string title = galleryInfo.title;
                        string artist = "";
                        if (galleryInfo.artists != null && galleryInfo.artists.Count > 0)
                        {
                            artist = galleryInfo.artists[0].artist;
                        }

                        string displayName = string.IsNullOrEmpty(artist) ? title : $"[{artist}] {title}";
                        displayName = FormatGalleryTitle(displayName);

                        // Lấy hash của trang đầu làm preview thumbnail
                        string thumbUrl = "";
                        if (galleryInfo.files != null && galleryInfo.files.Count > 0)
                        {
                            string firstHash = galleryInfo.files[0].hash;
                            string firstName = galleryInfo.files[0].name;
                            thumbUrl = await ResolveHitomiImageUrlAsync(this, firstHash, firstName, isThumbnail: true);
                        }

                        // Lưu json metadata vào tag của GalleryItem để lúc tải ảnh lôi ra hash
                        string serializedInfo = JsonConvert.SerializeObject(galleryInfo);

                        Dispatcher.Invoke(() =>
                        {
                            _scrapedItems.Add(new GalleryItem
                            {
                                Link = $"https://hitomi.la/gallery/{id}.html",
                                Name = displayName,
                                OriginalIndex = _scrapedItems.Count,
                                IsChecked = true,
                                HoverPreviewThumbnailUrl = thumbUrl,
                                SourceDomain = "hitomi.la",
                                Tag = serializedInfo // Đút dữ liệu đầy đủ của book vào đây
                            });
                        });

                        imported++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        HitomiLaLog($"Lỗi import ID {id}: {ex.Message}");
                    }
                }

                RecalculateDuplicates();
                lblLinkCount.Text = _scrapedItems.Count.ToString();
                lblStatus.Text = $"Import completed. Success: {imported}, Failed: {failed}.";
            }
            finally
            {
                if (!keepControlsEnabled)
                {
                    btnHitomiLaScrape.IsEnabled = true;
                    btnHitomiLaFetchInfo.IsEnabled = true;
                }
                if (btnStartDownload != null) btnStartDownload.IsEnabled = true;
            }
        }
    }
}

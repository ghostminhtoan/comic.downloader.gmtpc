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
                string sub = GetHitomiSubdomainAsync(url, "tn", null);
                return url.Replace("//a.gold-usergeneratedcontent.net/", $"//{sub}.gold-usergeneratedcontent.net/");
            }
                // Full image path
                string b = _hitomiGG.B;
                string s = _hitomiGG.GetS(hash);
                string fullUrl = $"https://a.gold-usergeneratedcontent.net/{b}{s}/{hash}.webp"; // Ưu tiên Webp chất lượng cao

                string fullSub = GetHitomiSubdomainAsync(fullUrl, null, "webp");
                return fullUrl.Replace("//a.gold-usergeneratedcontent.net/", $"//{fullSub}.gold-usergeneratedcontent.net/");
        }

        private static string GetHitomiSubdomainAsync(string url, string baseDomain, string dir)
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

        private bool IsHitomiLaTagOrListUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            string lower = url.Trim().ToLowerInvariant();
            if (Regex.IsMatch(lower, @"hitomi\.la/(?:reader|doujinshi|manga|gamecg|cg|gallery)/.*?(\d+)\.html"))
            {
                return false;
            }
            if (Regex.IsMatch(lower, @"-\d+\.html"))
            {
                return false;
            }
            return lower.Contains("/tag/") ||
                   lower.Contains("/artist/") ||
                   lower.Contains("/character/") ||
                   lower.Contains("/series/") ||
                   lower.Contains("/group/") ||
                   lower.Contains("search") ||
                   lower.Contains("index-");
        }

        private string GetHitomiNozomiUrl(string url)
        {
            var match = Regex.Match(url, @"/(tag|artist|character|series|group)/([^/#?]+)\.html");
            if (match.Success)
            {
                string category = match.Groups[1].Value;
                string name = match.Groups[2].Value;
                return $"https://ltn.gold-usergeneratedcontent.net/n/{category}/{name}.nozomi";
            }
            return "https://ltn.gold-usergeneratedcontent.net/index-all.nozomi";
        }

        private async Task<byte[]> GetHitomiSearchDataAsync(string url)
        {
            int qIdx = url.IndexOf('?');
            if (qIdx < 0)
            {
                string fallbackUrl = "https://ltn.gold-usergeneratedcontent.net/index-all.nozomi";
                using (var httpClient = CreateScopedHttpClient(fallbackUrl))
                {
                    return await httpClient.GetByteArrayAsync(fallbackUrl);
                }
            }

            string queryString = url.Substring(qIdx + 1);
            int hashIdx = queryString.IndexOf('#');
            if (hashIdx >= 0)
            {
                queryString = queryString.Substring(0, hashIdx);
            }
            queryString = Uri.UnescapeDataString(queryString).Trim();
            if (string.IsNullOrEmpty(queryString))
            {
                string fallbackUrl = "https://ltn.gold-usergeneratedcontent.net/index-all.nozomi";
                using (var httpClient = CreateScopedHttpClient(fallbackUrl))
                {
                    return await httpClient.GetByteArrayAsync(fallbackUrl);
                }
            }

            // Lay version cua galleriesindex dong
            string galleriesIndexVer = "1786272821";
            try
            {
                using (var httpClient = CreateScopedHttpClient("https://ltn.gold-usergeneratedcontent.net/galleriesindex/version"))
                {
                    string verStr = await httpClient.GetStringAsync("https://ltn.gold-usergeneratedcontent.net/galleriesindex/version");
                    if (!string.IsNullOrWhiteSpace(verStr))
                    {
                        galleriesIndexVer = verStr.Trim();
                    }
                }
            }
            catch { }

            var rawTokens = queryString.Split(new[] { ' ', '+' }, StringSplitOptions.RemoveEmptyEntries);
            var tokenSets = new List<HashSet<int>>();

            foreach (var t in rawTokens)
            {
                string cleanToken = t.Trim();
                if (string.IsNullOrEmpty(cleanToken)) continue;

                int colonIdx = cleanToken.IndexOf(':');
                if (colonIdx > 0 && System.Text.RegularExpressions.Regex.IsMatch(cleanToken.Substring(0, colonIdx), @"^[a-zA-Z0-9_-]+$"))
                {
                    // Token co prefix: language, artist, character, series, group, type, female, male...
                    string prefix = cleanToken.Substring(0, colonIdx).ToLowerInvariant();
                    string val = cleanToken.Substring(colonIdx + 1).ToLowerInvariant().Replace('_', ' ');

                    string nozomiUrl;
                    if (prefix == "language")
                    {
                        nozomiUrl = $"https://ltn.gold-usergeneratedcontent.net/index-{val}.nozomi";
                    }
                    else if (prefix == "artist" || prefix == "character" || prefix == "series" || prefix == "group" || prefix == "type")
                    {
                        nozomiUrl = $"https://ltn.gold-usergeneratedcontent.net/n/{prefix}/{val}-all.nozomi";
                    }
                    else
                    {
                        string tagValue = (prefix + ":" + val);
                        nozomiUrl = $"https://ltn.gold-usergeneratedcontent.net/n/tag/{tagValue}-all.nozomi";
                    }

                    var ids = await FetchNozomiIdsAsync(nozomiUrl);
                    if (ids != null && ids.Count > 0)
                    {
                        tokenSets.Add(ids);
                        HitomiLaLog($"[Search] Token '{cleanToken}' via Nozomi => {ids.Count} IDs");
                    }
                }
                else
                {
                    // Token free-text (VD: "dragon", "ball", "super"):
                    // 1) Thu qua Hitomi B-Tree galleries.index
                    string val = cleanToken.ToLowerInvariant().Replace('_', ' ');
                    var ids = await FetchHitomiBTreeIdsAsync(val, galleriesIndexVer);

                    // 2) Hop nhat voi cac nozomi candidate neu co (series, tag, character...)
                    var candidateUrls = new[] {
                        $"https://ltn.gold-usergeneratedcontent.net/n/series/{val}-all.nozomi",
                        $"https://ltn.gold-usergeneratedcontent.net/n/tag/{val}-all.nozomi",
                        $"https://ltn.gold-usergeneratedcontent.net/n/character/{val}-all.nozomi",
                        $"https://ltn.gold-usergeneratedcontent.net/n/artist/{val}-all.nozomi",
                        $"https://ltn.gold-usergeneratedcontent.net/n/group/{val}-all.nozomi"
                    };

                    foreach (var cUrl in candidateUrls)
                    {
                        var cIds = await FetchNozomiIdsAsync(cUrl);
                        if (cIds != null && cIds.Count > 0)
                        {
                            if (ids == null) ids = new HashSet<int>();
                            ids.UnionWith(cIds);
                        }
                    }

                    if (ids != null && ids.Count > 0)
                    {
                        tokenSets.Add(ids);
                        HitomiLaLog($"[Search] Free-text token '{cleanToken}' => {ids.Count} IDs");
                    }
                }
            }

            if (tokenSets.Count == 0)
            {
                return new byte[0];
            }

            // Giao (Intersection) tat ca cac token
            HashSet<int> resultIds = new HashSet<int>(tokenSets[0]);
            for (int i = 1; i < tokenSets.Count; i++)
            {
                resultIds.IntersectWith(tokenSets[i]);
            }

            if (resultIds.Count == 0)
            {
                return new byte[0];
            }

            byte[] resultBytes = new byte[resultIds.Count * 4];
            int idx = 0;
            var sortedIds = resultIds.OrderByDescending(id => id);
            foreach (int id in sortedIds)
            {
                resultBytes[idx] = (byte)((id >> 24) & 0xFF);
                resultBytes[idx + 1] = (byte)((id >> 16) & 0xFF);
                resultBytes[idx + 2] = (byte)((id >> 8) & 0xFF);
                resultBytes[idx + 3] = (byte)(id & 0xFF);
                idx += 4;
            }

            return resultBytes;
        }

        private async Task<HashSet<int>> FetchNozomiIdsAsync(string url)
        {
            try
            {
                using (var httpClient = CreateScopedHttpClient(url))
                {
                    byte[] bytes = await httpClient.GetByteArrayAsync(url);
                    if (bytes != null && bytes.Length > 0)
                    {
                        var ids = new HashSet<int>();
                        int count = bytes.Length / 4;
                        for (int i = 0; i < count; i++)
                        {
                            ids.Add(BigEndianToInt32(bytes, i * 4));
                        }
                        return ids;
                    }
                }
            }
            catch { }
            return null;
        }

        private async Task<HashSet<int>> FetchHitomiBTreeIdsAsync(string term, string ver)
        {
            try
            {
                byte[] key;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    key = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(term)).Take(4).ToArray();
                }

                long[] matchData = await BSearchHitomiNodeAsync(key, 0, ver);
                if (matchData == null) return null;

                long off = matchData[0];
                long len = matchData[1];

                string dataUrl = $"https://ltn.gold-usergeneratedcontent.net/galleriesindex/galleries.{ver}.data";
                byte[] inbuf = await FetchUrlAtRangeAsync(dataUrl, off, off + len - 1);
                if (inbuf == null || inbuf.Length < 4) return null;

                int numIds = BigEndianToInt32(inbuf, 0);
                var set = new HashSet<int>();
                for (int i = 0; i < numIds; i++)
                {
                    int id = BigEndianToInt32(inbuf, 4 + i * 4);
                    set.Add(id);
                }
                return set;
            }
            catch { }
            return null;
        }

        private async Task<long[]> BSearchHitomiNodeAsync(byte[] key, long nodeAddress, string ver)
        {
            string indexUrl = $"https://ltn.gold-usergeneratedcontent.net/galleriesindex/galleries.{ver}.index";
            byte[] data = await FetchUrlAtRangeAsync(indexUrl, nodeAddress, nodeAddress + 463);
            if (data == null || data.Length < 4) return null;

            int pos = 0;
            int numberOfKeys = BigEndianToInt32(data, pos);
            pos += 4;

            var keys = new List<byte[]>();
            for (int i = 0; i < numberOfKeys; i++)
            {
                int keySize = BigEndianToInt32(data, pos);
                pos += 4;
                if (keySize <= 0 || keySize > 32) return null;
                byte[] k = new byte[keySize];
                Array.Copy(data, pos, k, 0, keySize);
                pos += keySize;
                keys.Add(k);
            }

            int numberOfDatas = BigEndianToInt32(data, pos);
            pos += 4;
            var datas = new List<long[]>();
            for (int i = 0; i < numberOfDatas; i++)
            {
                long hi = (uint)BigEndianToInt32(data, pos);
                long lo = (uint)BigEndianToInt32(data, pos + 4);
                long offset = (hi << 32) | lo;
                pos += 8;

                int length = BigEndianToInt32(data, pos);
                pos += 4;
                datas.Add(new long[] { offset, length });
            }

            var subnodes = new List<long>();
            for (int i = 0; i < 17; i++)
            {
                if (pos + 8 > data.Length) break;
                long hi = (uint)BigEndianToInt32(data, pos);
                long lo = (uint)BigEndianToInt32(data, pos + 4);
                long subAddr = (hi << 32) | lo;
                pos += 8;
                subnodes.Add(subAddr);
            }

            bool there = false;
            int where = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                int cmp = CompareByteArrays(key, keys[i]);
                if (cmp <= 0)
                {
                    there = (cmp == 0);
                    where = i;
                    break;
                }
                where = i + 1;
            }

            if (there) return datas[where];

            bool isLeaf = subnodes.All(addr => addr == 0);
            if (isLeaf) return null;

            if (where < subnodes.Count && subnodes[where] != 0)
            {
                return await BSearchHitomiNodeAsync(key, subnodes[where], ver);
            }
            return null;
        }

        private static int CompareByteArrays(byte[] dv1, byte[] dv2)
        {
            int top = Math.Min(dv1.Length, dv2.Length);
            for (int i = 0; i < top; i++)
            {
                if (dv1[i] < dv2[i]) return -1;
                if (dv1[i] > dv2[i]) return 1;
            }
            return 0;
        }

        private async Task<byte[]> FetchUrlAtRangeAsync(string url, long start, long end)
        {
            try
            {
                var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                req.AddRange((int)start, (int)end);
                using (var resp = await req.GetResponseAsync())
                using (var stream = resp.GetResponseStream())
                using (var ms = new System.IO.MemoryStream())
                {
                    await stream.CopyToAsync(ms);
                    return ms.ToArray();
                }
            }
            catch
            {
                return null;
            }
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
                HitomiLaLog($"Analyzing target URL: {url}");
                if (!IsHitomiLaTagOrListUrl(url))
                {
                    lblStatus.Text = "Ready to get link.";
                    txtHitomiLaTotalPages.Text = "1";
                    txtHitomiLaPageFrom.Text = "1";
                    txtHitomiLaPageTo.Text = "1";
                    return;
                }

                byte[] data = null;
                if (url.Contains("search"))
                {
                    data = await GetHitomiSearchDataAsync(url);
                }
                else
                {
                    string nozomiUrl = GetHitomiNozomiUrl(url);
                    using (var httpClient = CreateScopedHttpClient(nozomiUrl))
                    {
                        data = await httpClient.GetByteArrayAsync(nozomiUrl);
                    }
                }

                if (data != null && data.Length > 0)
                {
                    int totalIDs = data.Length / 4;
                    int totalPages = (int)Math.Ceiling(totalIDs / 25.0);
                    txtHitomiLaTotalPages.Text = totalPages.ToString();
                    txtHitomiLaPageFrom.Text = "1";
                    txtHitomiLaPageTo.Text = "1";
                    lblStatus.Text = $"Ready to get links. Found {totalIDs} books ({totalPages} pages).";
                }
                else
                {
                    lblStatus.Text = "No books found on this page.";
                }
            }
            catch (Exception ex)
            {
                HitomiLaLog($"Lỗi: {ex.Message}");
                lblStatus.Text = $"Analyze error: {ex.Message}";
            }
            finally
            {
                btnHitomiLaFetchInfo.IsEnabled = true;
                progressBar.IsIndeterminate = false;
            }
        }

        private async void BtnHitomiLaScrape_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmScrapeDuringDownloadIfNeeded(true)) return;
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

                if (!IsHitomiLaTagOrListUrl(url))
                {
                    var singleMatch = Regex.Match(url, @"(?:-|/)(\d+)\.html");
                    if (singleMatch.Success)
                    {
                        string id = singleMatch.Groups[1].Value;
                        await ImportHitomiLaDirectLinksAsync(new List<string> { $"https://hitomi.la/reader/{id}.html" });
                    }
                    return;
                }

                byte[] data = null;
                if (url.Contains("search"))
                {
                    HitomiLaLog("Đang xử lý tìm kiếm kết hợp trên Hitomi.la...");
                    data = await GetHitomiSearchDataAsync(url);
                }
                else
                {
                    string nozomiUrl = GetHitomiNozomiUrl(url);
                    HitomiLaLog($"Fetching nozomi list từ {nozomiUrl}...");
                    using (var httpClient = CreateScopedHttpClient(nozomiUrl))
                    {
                        data = await httpClient.GetByteArrayAsync(nozomiUrl);
                    }
                }

                if (data == null || data.Length == 0)
                {
                    HitomiLaLog("Không lấy được dữ liệu từ nozomi index.");
                    return;
                }

                int totalIDs = data.Length / 4;
                int totalPages = (int)Math.Ceiling(totalIDs / 25.0);

                if (!int.TryParse(txtHitomiLaPageFrom.Text, out int pageFrom)) pageFrom = 1;
                if (!int.TryParse(txtHitomiLaPageTo.Text, out int pageTo)) pageTo = 1;

                if (pageFrom < 1) pageFrom = 1;
                if (pageTo < pageFrom) pageTo = pageFrom;
                if (pageTo > totalPages) pageTo = totalPages;

                if (append)
                {
                    pageFrom = pageTo + 1;
                    pageTo = pageFrom;
                    if (pageFrom > totalPages)
                    {
                        HitomiLaLog("Đã đạt trang cuối cùng.");
                        return;
                    }
                    txtHitomiLaPageFrom.Text = pageFrom.ToString();
                    txtHitomiLaPageTo.Text = pageTo.ToString();
                }

                HitomiLaLog($"Đang xử lý nozomi từ trang {pageFrom} đến {pageTo}...");

                var idsToFetch = new List<string>();
                for (int page = pageFrom; page <= pageTo; page++)
                {
                    int startIndex = (page - 1) * 25;
                    for (int i = 0; i < 25; i++)
                    {
                        int targetIndex = startIndex + i;
                        if (targetIndex >= totalIDs) break;

                        int id = BigEndianToInt32(data, targetIndex * 4);
                        idsToFetch.Add($"https://hitomi.la/reader/{id}.html");
                    }
                }

                if (idsToFetch.Count > 0)
                {
                    await ImportHitomiLaDirectLinksAsync(idsToFetch, showMessageBox: false, keepControlsEnabled: true);
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
                // btnStartDownload luôn bật để cho phép bấm download
            }

            try
            {
                var cleanIds = new List<string>();
                foreach (var link in links)
                {
                    if (string.IsNullOrEmpty(link)) continue;
                    string targetLink = link;
                    int hashIdx = targetLink.IndexOf('#');
                    if (hashIdx >= 0) targetLink = targetLink.Substring(0, hashIdx);
                    int qIdx = targetLink.IndexOf('?');
                    if (qIdx >= 0) targetLink = targetLink.Substring(0, qIdx);
                    targetLink = targetLink.Trim();

                    var idMatch = Regex.Match(targetLink, @"(\d+)(?:\.html)?$");
                    if (idMatch.Success)
                    {
                        cleanIds.Add(idMatch.Groups[1].Value);
                    }
                    else
                    {
                        failed++;
                    }
                }

                ShowResultsImportingIndicator();
                UpdateResultsImportingProgress(imported, cleanIds.Count);

                // Tải song song thông tin các gallery trong trang với batch size 25
                int batchSize = 25;
                for (int b = 0; b < cleanIds.Count; b += batchSize)
                {
                    var batch = cleanIds.Skip(b).Take(batchSize).ToList();
                    var tasks = batch.Select(async id =>
                    {
                        string apiJsonUrl = $"https://ltn.gold-usergeneratedcontent.net/galleries/{id}.js";
                        try
                        {
                            string jsContent = await FetchStringAsync(apiJsonUrl, CancellationToken.None);
                            if (string.IsNullOrEmpty(jsContent)) return null;

                            string json = jsContent.Replace("var galleryinfo = ", "").Trim();
                            if (json.EndsWith(";"))
                            {
                                json = json.Substring(0, json.Length - 1).Trim();
                            }
                            dynamic galleryInfo = JsonConvert.DeserializeObject(json);

                            string title = galleryInfo.title;
                            string artist = "";
                            if (galleryInfo.artists != null && galleryInfo.artists.Count > 0)
                            {
                                artist = galleryInfo.artists[0].artist;
                            }

                            string displayName = string.IsNullOrEmpty(artist) ? title : $"[{artist}] {title}";
                            string language = "";
                            if (galleryInfo.language_localname != null)
                            {
                                language = (string)galleryInfo.language_localname;
                                if (language == "中文") language = "Chinese";
                                else if (language == "日本語") language = "Japanese";
                                else if (language == "한국어") language = "Korean";
                            }
                            if (!string.IsNullOrEmpty(language))
                            {
                                string langSuffix = $"[{language}]";
                                if (!displayName.EndsWith(langSuffix, StringComparison.OrdinalIgnoreCase))
                                {
                                    displayName = $"{displayName} {langSuffix}";
                                }
                            }
                            displayName = FormatGalleryTitle(displayName);

                            string thumbUrl = "";
                            if (galleryInfo.files != null && galleryInfo.files.Count > 0)
                            {
                                string firstHash = galleryInfo.files[0].hash;
                                string firstName = galleryInfo.files[0].name;
                                thumbUrl = await ResolveHitomiImageUrlAsync(this, firstHash, firstName, isThumbnail: true);
                            }

                            string galleryUrl = $"https://hitomi.la/reader/{id}.html";
                            if (galleryInfo.galleryurl != null)
                            {
                                string relUrl = ((string)galleryInfo.galleryurl).TrimStart('/');
                                galleryUrl = "https://hitomi.la/" + relUrl;
                            }

                            string serializedInfo = JsonConvert.SerializeObject(galleryInfo);

                            return new GalleryItem
                            {
                                Link = galleryUrl,
                                Name = displayName,
                                IsChecked = true,
                                HoverPreviewThumbnailUrl = thumbUrl,
                                SourceDomain = "hitomi.la",
                                Tag = serializedInfo
                            };
                        }
                        catch (Exception ex)
                        {
                            HitomiLaLog($"Lỗi import ID {id}: {ex.Message}");
                            return null;
                        }
                    }).ToList();

                    var results = await Task.WhenAll(tasks);
                    var newItems = results.Where(item => item != null).ToList();

                    imported += newItems.Count;
                    failed += batch.Count - newItems.Count;

                    if (newItems.Count > 0)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            foreach (var item in newItems)
                            {
                                item.OriginalIndex = _scrapedItems.Count;
                                _scrapedItems.Add(item);
                            }
                            UpdateResultsImportingProgress(imported, cleanIds.Count, newItems.LastOrDefault()?.Name);
                        });
                    }
                }

                RecalculateDuplicates();
                lblLinkCount.Text = _scrapedItems.Count.ToString();
                lblStatus.Text = $"Import completed. Success: {imported}, Failed: {failed}.";
            }
            finally
            {
                HideResultsImportingIndicator();
                if (!keepControlsEnabled)
                {
                    btnHitomiLaScrape.IsEnabled = true;
                    btnHitomiLaFetchInfo.IsEnabled = true;
                }
                if (btnStartDownload != null) btnStartDownload.IsEnabled = true;
            }
        }

        private string ExtractHitomiLaGalleryCover(string html, string pageUrl)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;
            try
            {
                // Tìm <div class="cover"> ... <img src="..."
                var match = Regex.Match(html, @"class=""cover""[^>]*>.*?<img[^>]+src=""([^""]+)""", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string src = WebUtility.HtmlDecode(match.Groups[1].Value);
                    if (src.StartsWith("//"))
                    {
                        src = "https:" + src;
                    }
                    else if (!src.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        src = new Uri(new Uri(pageUrl), src).AbsoluteUri;
                    }
                    return src;
                }
            }
            catch (Exception ex)
            {
                Log($"[Hitomi Preview] Lỗi trích xuất cover: {ex.Message}");
            }
            return string.Empty;
        }
    }
}

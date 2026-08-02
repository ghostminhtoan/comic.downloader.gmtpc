using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private const string MangadexBaseUrl = "https://mangadex.org";
        private const string MangadexApiBaseUrl = "https://api.mangadex.org";
        private const string MangadexSiteFolder = "mangadex.org";
        private const int MangadexCategoryPageSize = 100;
        private const int MangadexFeedPageSize = 500;
        // ponytail: spawn ChromeDriver theo request cho MangaDex; cham hon pool nhung tranh profile lock va crash DevToolsActivePort. Nang cap sau: shared WebView2 fetcher.
        private readonly SemaphoreSlim _mangadexBrowserFetchSemaphore = new SemaphoreSlim(2, 2);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, MangadexMangaData> _mangadexMangaCache = new System.Collections.Concurrent.ConcurrentDictionary<string, MangadexMangaData>(StringComparer.OrdinalIgnoreCase);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, MangadexChapterData> _mangadexChapterCache = new System.Collections.Concurrent.ConcurrentDictionary<string, MangadexChapterData>(StringComparer.OrdinalIgnoreCase);
        private static readonly Regex MangadexUuidRegex = new Regex(
            @"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private void MangadexLog(string message)
        {
            Log("[mangadex.org] " + message);
        }

        private bool IsMangadexUrl(string url)
        {
            return TryParseMangadexUri(url, out _);
        }

        private bool IsMangadexHomeUrl(string url)
        {
            if (!TryParseMangadexUri(url, out Uri uri))
            {
                return false;
            }

            return GetMangadexSegments(uri).Length == 0;
        }

        private bool IsMangadexCategoryUrl(string url)
        {
            return TryParseMangadexTag(url, out _, out _);
        }

        private bool IsMangadexBookUrl(string url)
        {
            return TryParseMangadexMangaId(url, out _, out _);
        }

        private bool IsMangadexChapterUrl(string url)
        {
            return TryParseMangadexChapterId(url, out _, out _);
        }

        private bool TryParseMangadexUri(string url, out Uri uri)
        {
            uri = null;
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            string normalized = WebUtility.HtmlDecode(url).Trim();
            if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                normalized = MangadexBaseUrl + (normalized.StartsWith("/", StringComparison.Ordinal) ? string.Empty : "/") + normalized;
            }

            if (!Uri.TryCreate(normalized, UriKind.Absolute, out uri))
            {
                return false;
            }

            return uri.Host.Equals("mangadex.org", StringComparison.OrdinalIgnoreCase) ||
                   uri.Host.Equals("www.mangadex.org", StringComparison.OrdinalIgnoreCase);
        }

        private string NormalizeMangadexUrl(string url)
        {
            if (!TryParseMangadexUri(url, out Uri uri))
            {
                throw new ArgumentException("URL mangadex.org không hợp lệ.");
            }

            string[] segments = GetMangadexSegments(uri);
            var builder = new UriBuilder(MangadexBaseUrl)
            {
                Fragment = string.Empty,
                Query = string.Empty
            };

            if (segments.Length == 0)
            {
                builder.Path = "/";
                return builder.Uri.AbsoluteUri.TrimEnd('/');
            }

            builder.Path = "/" + string.Join("/", segments.Select(Uri.EscapeDataString));
            return builder.Uri.AbsoluteUri.TrimEnd('/');
        }

        private static string[] GetMangadexSegments(Uri uri)
        {
            return uri?.AbsolutePath
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(segment => !string.IsNullOrWhiteSpace(segment))
                .ToArray() ?? Array.Empty<string>();
        }

        private bool TryParseMangadexTag(string url, out string tagId, out string tagSlug)
        {
            tagId = string.Empty;
            tagSlug = string.Empty;
            if (!TryParseMangadexUri(url, out Uri uri))
            {
                return false;
            }

            string[] segments = GetMangadexSegments(uri);
            if (segments.Length < 3 || !segments[0].Equals("tag", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!MangadexUuidRegex.IsMatch(segments[1]))
            {
                return false;
            }

            tagId = segments[1];
            tagSlug = segments[2];
            return true;
        }

        private bool TryParseMangadexMangaId(string url, out string mangaId, out string mangaSlug)
        {
            mangaId = string.Empty;
            mangaSlug = string.Empty;
            if (!TryParseMangadexUri(url, out Uri uri))
            {
                return false;
            }

            string[] segments = GetMangadexSegments(uri);
            if (segments.Length < 2 || !segments[0].Equals("title", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!MangadexUuidRegex.IsMatch(segments[1]))
            {
                return false;
            }

            mangaId = segments[1];
            mangaSlug = segments.Length >= 3 ? segments[2] : string.Empty;
            return true;
        }

        private bool TryParseMangadexChapterId(string url, out string chapterId, out string chapterSlug)
        {
            chapterId = string.Empty;
            chapterSlug = string.Empty;
            if (!TryParseMangadexUri(url, out Uri uri))
            {
                return false;
            }

            string[] segments = GetMangadexSegments(uri);
            if (segments.Length < 2 || !segments[0].Equals("chapter", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!MangadexUuidRegex.IsMatch(segments[1]))
            {
                return false;
            }

            chapterId = segments[1];
            chapterSlug = segments.Length >= 3 ? segments[2] : string.Empty;
            return true;
        }

        private static string BuildMangadexMangaUrl(string mangaId, string slug)
        {
            string safeSlug = string.IsNullOrWhiteSpace(slug) ? "title" : slug.Trim('/');
            return $"{MangadexBaseUrl}/title/{mangaId}/{safeSlug}";
        }

        private static string BuildMangadexChapterUrl(string chapterId)
        {
            return $"{MangadexBaseUrl}/chapter/{chapterId}";
        }

        private static string BuildMangadexCoverUrl(string mangaId, string fileName)
        {
            if (string.IsNullOrWhiteSpace(mangaId) || string.IsNullOrWhiteSpace(fileName))
            {
                return string.Empty;
            }

            return $"https://uploads.mangadex.org/covers/{mangaId}/{fileName}";
        }

        private static string BuildMangadexApiUrl(string path, IDictionary<string, string> query = null)
        {
            var builder = new StringBuilder(MangadexApiBaseUrl);
            if (!path.StartsWith("/", StringComparison.Ordinal))
            {
                builder.Append('/');
            }

            builder.Append(path);
            if (query == null || query.Count == 0)
            {
                return builder.ToString();
            }

            builder.Append('?');
            bool first = true;
            foreach (KeyValuePair<string, string> pair in query)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null)
                {
                    continue;
                }

                if (!first)
                {
                    builder.Append('&');
                }

                first = false;
                builder.Append(Uri.EscapeDataString(pair.Key));
                builder.Append('=');
                builder.Append(Uri.EscapeDataString(pair.Value));
            }

            return builder.ToString();
        }

        private static T DeserializeMangadexJson<T>(string json)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json ?? string.Empty)))
            {
                var serializer = new DataContractJsonSerializer(typeof(T));
                return (T)serializer.ReadObject(stream);
            }
        }

        private async Task<T> GetMangadexJsonAsync<T>(string url, CancellationToken token)
        {
            Exception lastError = null;
            for (int attempt = 1; attempt <= 4; attempt++)
            {
                token.ThrowIfCancellationRequested();

                // First try direct HttpClient fetch
                try
                {
                    string json = await FetchStringAsync(url, token);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        return DeserializeMangadexJson<T>(json);
                    }

                    throw new HttpRequestException("MangaDex trả JSON rỗng.");
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    MangadexLog($"HttpClient lỗi với MangaDex. Thử các phương thức fallback ({attempt}/4): {ex.Message}");
                }

                // If HttpClient fails, try browser fallback if it's a mangadex host
                if (IsMangadexBrowserFetchUrl(url))
                {
                    try
                    {
                        string json = await FetchMangadexTextViaBrowserAsync(url, token);
                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            return DeserializeMangadexJson<T>(json);
                        }

                        throw new HttpRequestException("Browser MangaDex trả JSON rỗng.");
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        MangadexLog($"Browser lỗi với MangaDex ({attempt}/4): {ex.Message}");
                    }
                }

                // If both fail, try curl fallback
                try
                {
                    string json = await FetchMangadexTextWithCurlAsync(url, token);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        return DeserializeMangadexJson<T>(json);
                    }

                    throw new HttpRequestException("curl MangaDex trả JSON rỗng.");
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    MangadexLog($"curl lỗi với MangaDex ({attempt}/4): {ex.Message}");
                }

                if (attempt < 4)
                {
                    await Task.Delay(400 * attempt, token);
                }
            }

            throw new HttpRequestException("Không lấy được JSON MangaDex sau nhiều lần thử.", lastError);
        }

        private bool IsMangadexBrowserFetchUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            return url.IndexOf("mangadex.org", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   url.IndexOf("mangadex.network", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   url.IndexOf("uploads.mangadex.org", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private ChromeDriver CreateMangadexChromeDriver(int poolIndex)
        {
            var options = new ChromeOptions();
            string chromeBinary = TryFindChromeExecutable();
            if (!string.IsNullOrWhiteSpace(chromeBinary))
            {
                options.BinaryLocation = chromeBinary;
            }

            options.AddArgument("--headless=new");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--disable-software-rasterizer");
            options.AddArgument("--disable-dev-shm-usage");
            options.AddArgument("--disable-blink-features=AutomationControlled");
            options.AddArgument("--disable-popup-blocking");
            options.AddArgument("--disable-extensions");
            options.AddArgument("--no-first-run");
            options.AddArgument("--no-default-browser-check");
            options.AddArgument("--no-sandbox");
            options.AddArgument("--remote-debugging-port=0");
            options.AddArgument("--window-size=1280,900");

            ChromeDriverService service = ChromeDriverService.CreateDefaultService();
            service.HideCommandPromptWindow = true;
            service.SuppressInitialDiagnosticInformation = true;

            var driver = new ChromeDriver(service, options, TimeSpan.FromMinutes(2));
            OptimizeSystemPriorityForBackgroundTasks();
            driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(60);
            driver.Manage().Timeouts().AsynchronousJavaScript = TimeSpan.FromSeconds(90);
            return driver;
        }

        private void EnsureMangadexBrowserDriverPool()
        {
        }

        private ChromeDriver RentMangadexBrowserDriver()
        {
            return CreateMangadexChromeDriver(0);
        }

        private void ReturnMangadexBrowserDriver(ChromeDriver driver)
        {
            driver?.Dispose();
        }

        public void DisposeMangadexBrowserDrivers()
        {
        }

        private async Task<string> FetchMangadexTextViaBrowserAsync(string url, CancellationToken token)
        {
            return await FetchMangadexBrowserPayloadAsync(url, fetchBinary: false, token);
        }

        private async Task<string> FetchMangadexTextWithCurlAsync(string url, CancellationToken token)
        {
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();

                string arguments = "--fail --location --silent --show-error --compressed " +
                                   "--user-agent " + QuoteWindowsArgument("Mozilla/5.0") + " " +
                                   "--header " + QuoteWindowsArgument("Accept: application/json") + " " +
                                   "--output - ";
                arguments += QuoteWindowsArgument(url);

                var startInfo = new ProcessStartInfo
                {
                    FileName = GetCurlPath(),
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System)
                };

                using (var process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    string json = process.StandardOutput.ReadToEnd();
                    string stdErr = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        throw new HttpRequestException(string.IsNullOrWhiteSpace(stdErr) ? "curl MangaDex lỗi." : stdErr.Trim());
                    }

                    return json;
                }
            }, token);
        }

        private async Task<byte[]> FetchMangadexBytesViaBrowserAsync(string url, string referer, CancellationToken token)
        {
            string payload = await FetchMangadexBrowserPayloadAsync(url, fetchBinary: true, token);
            return Convert.FromBase64String(payload);
        }

        private async Task<string> FetchMangadexBrowserPayloadAsync(string url, bool fetchBinary, CancellationToken token)
        {
            await _mangadexBrowserFetchSemaphore.WaitAsync(token);
            try
            {
                EnsureMangadexBrowserDriverPool();
                return await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    ChromeDriver driver = RentMangadexBrowserDriver();
                    try
                    {
                        driver.Navigate().GoToUrl(url);
                        Thread.Sleep(800);

                        if (!fetchBinary)
                        {
                            string textPayload = Convert.ToString(((IJavaScriptExecutor)driver).ExecuteScript(@"
var body = document.body;
if (!body) {
  return document.documentElement ? document.documentElement.outerHTML : '';
}
return body.innerText || body.textContent || body.innerHTML || '';
")) ?? string.Empty;
                            textPayload = textPayload.Trim();
                            if (!string.IsNullOrWhiteSpace(textPayload))
                            {
                                return textPayload;
                            }
                        }

                        string script = fetchBinary
                            ? @"
var done = arguments[arguments.length - 1];
fetch(window.location.href, { method: 'GET', credentials: 'omit', cache: 'no-store' })
  .then(function (response) {
    if (!response.ok) {
      throw new Error('HTTP ' + response.status);
    }
    return response.blob();
  })
  .then(function (blob) {
    var reader = new FileReader();
    reader.onloadend = function () {
      var data = String(reader.result || '');
      var commaIndex = data.indexOf(',');
      done(commaIndex >= 0 ? 'OK:' + data.substring(commaIndex + 1) : 'ERR:Không đọc được base64');
    };
    reader.readAsDataURL(blob);
  })
  .catch(function (error) {
    done('ERR:' + (error && error.message ? error.message : String(error)));
  });"
                            : @"
var done = arguments[arguments.length - 1];
fetch(window.location.href, { method: 'GET', credentials: 'omit', cache: 'no-store' })
  .then(function (response) {
    return response.text().then(function (text) {
      done(response.ok ? 'OK:' + text : 'ERR:HTTP ' + response.status + ' ' + text);
    });
  })
  .catch(function (error) {
    done('ERR:' + (error && error.message ? error.message : String(error)));
  });";

                        string result = Convert.ToString(((IJavaScriptExecutor)driver).ExecuteAsyncScript(script)) ?? string.Empty;
                        if (result.StartsWith("OK:", StringComparison.Ordinal))
                        {
                            return result.Substring(3);
                        }

                        throw new HttpRequestException(string.IsNullOrWhiteSpace(result)
                            ? "Chrome fallback MangaDex không trả dữ liệu."
                            : result);
                    }
                    finally
                    {
                        ReturnMangadexBrowserDriver(driver);
                    }
                }, token);
            }
            finally
            {
                _mangadexBrowserFetchSemaphore.Release();
            }
        }

        private string CleanMangadexText(string value)
        {
            string clean = WebUtility.HtmlDecode(Regex.Replace(value ?? string.Empty, @"<[^>]+>", " ")).Trim();
            clean = Regex.Replace(clean, @"\s+", " ").Trim();
            clean = clean.Replace(" - MangaDex", string.Empty).Trim();
            return FormatGalleryTitle(clean);
        }

        private string HumanizeMangadexSlug(string slug)
        {
            string clean = Regex.Replace((slug ?? string.Empty).Trim('/'), @"[-_]+", " ").Trim();
            return string.IsNullOrWhiteSpace(clean) ? "MangaDex" : FormatGalleryTitle(clean);
        }

        private string GetMangadexPreferredTitle(MangadexMangaAttributes attributes, string fallbackSlug = null)
        {
            if (attributes != null)
            {
                if (attributes.Title != null &&
                    attributes.Title.TryGetValue("en", out string englishTitle) &&
                    !string.IsNullOrWhiteSpace(englishTitle))
                {
                    return CleanMangadexText(englishTitle);
                }

                if (attributes.Title != null && attributes.Title.Count > 0)
                {
                    return CleanMangadexText(attributes.Title.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)));
                }
            }

            return HumanizeMangadexSlug(fallbackSlug);
        }

        private string GetMangadexChapterDisplayTitle(MangadexChapterAttributes attributes, string fallbackSlug = null)
        {
            string chapterNumber = attributes?.Chapter;
            string title = CleanMangadexText(attributes?.Title);
            if (!string.IsNullOrWhiteSpace(chapterNumber))
            {
                string normalized = NormalizeChapterLabel("Chương " + chapterNumber);
                return string.IsNullOrWhiteSpace(title)
                    ? normalized
                    : normalized + " - " + title;
            }

            if (!string.IsNullOrWhiteSpace(title))
            {
                return CompactSingleLine(title);
            }

            return NormalizeChapterLabel((fallbackSlug ?? string.Empty).Replace("-", " "));
        }

        private double ParseMangadexChapterNumber(string value)
        {
            if (TryParseChapterNumberFromChapterToken(value, out double strictValue))
            {
                return strictValue;
            }

            Match match = Regex.Match(value ?? string.Empty, @"(?<num>\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (match.Success &&
                double.TryParse(match.Groups["num"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed))
            {
                return parsed;
            }

            return 0d;
        }

        private void TxtMangadexTagUrl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText()?.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        txtMangadexTagUrl.Text = text;
                        txtMangadexTagUrl.CaretIndex = txtMangadexTagUrl.Text.Length;
                        _ = Dispatcher.BeginInvoke(new System.Action(async () => await AnalyzeMangadexUrlAsync(text)));
                        e.Handled = true;
                    }
                }

                return;
            }

            if (e.Key == Key.Enter)
            {
                BtnMangadexAnalyze_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void TxtMangadexTotalPages_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (txtMangadexPageTo != null && txtMangadexTotalPages != null)
            {
                txtMangadexPageTo.Text = txtMangadexTotalPages.Text;
            }
        }

        private async void BtnMangadexAnalyze_Click(object sender, RoutedEventArgs e)
        {
            await AnalyzeMangadexUrlAsync(txtMangadexTagUrl?.Text);
        }

        private async void BtnMangadexScrape_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                btnMangadexScrape.Content = "CANCELLING...";
                btnMangadexScrape.IsEnabled = false;
                btnMangadexCrawlMore.IsEnabled = false;
                return;
            }

            SelectDownloadMangaTab();
            await ScrapeMangadexAsync(clearExisting: true);
        }

        private async void BtnMangadexCrawlMore_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                btnMangadexCrawlMore.Content = "CANCELLING...";
                btnMangadexCrawlMore.IsEnabled = false;
                btnMangadexScrape.IsEnabled = false;
                return;
            }

            SelectDownloadMangaTab();
            await ScrapeMangadexAsync(clearExisting: false);
        }

        private async Task AnalyzeMangadexUrlAsync(string rawUrl)
        {
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                ShowWarning("Vui lòng nhập URL mangadex.org hợp lệ.", "Thông báo");
                return;
            }

            btnMangadexAnalyze.IsEnabled = false;
            btnMangadexPasteDirect.IsEnabled = false;
            progressBar.IsIndeterminate = true;

            try
            {
                string normalized = NormalizeMangadexUrl(rawUrl);
                txtMangadexTagUrl.Text = normalized;

                if (IsMangadexHomeUrl(normalized))
                {
                    txtMangadexPageHintText.Text = "Dán link tag/title/chapter để bắt đầu.";
                    txtMangadexTotalPages.Text = "1";
                    txtMangadexPageFrom.Text = "1";
                    txtMangadexPageTo.Text = "1";
                    lblStatus.Text = "MangaDex homepage ready.";
                    return;
                }

                if (IsMangadexCategoryUrl(normalized))
                {
                    int totalPages = await GetMangadexCategoryTotalPagesAsync(normalized, _downloadCts?.Token ?? CancellationToken.None);
                    txtMangadexPageHintText.Text = $"Category detected. Tổng page API: {Math.Max(1, totalPages)}";
                    txtMangadexTotalPages.Text = Math.Max(1, totalPages).ToString(CultureInfo.InvariantCulture);
                    txtMangadexPageFrom.Text = "1";
                    txtMangadexPageTo.Text = Math.Max(1, totalPages).ToString(CultureInfo.InvariantCulture);
                    lblStatus.Text = $"MangaDex category: {Math.Max(1, totalPages)} pages.";
                    return;
                }

                if (IsMangadexBookUrl(normalized))
                {
                    if (!TryParseMangadexMangaId(normalized, out string mangaId, out string mangaSlug))
                    {
                        throw new Exception("Không tách được manga id từ link MangaDex.");
                    }

                    MangadexMangaData manga = await GetMangadexMangaAsync(mangaId, _downloadCts?.Token ?? CancellationToken.None);
                    List<MangadexChapterDescriptor> chapters = await GetMangadexBookChaptersAsync(mangaId, _downloadCts?.Token ?? CancellationToken.None);
                    int totalPages = Math.Max(1, chapters.Count);
                    txtMangadexTotalPages.Text = totalPages.ToString(CultureInfo.InvariantCulture);
                    txtMangadexPageFrom.Text = "1";
                    txtMangadexPageTo.Text = totalPages.ToString(CultureInfo.InvariantCulture);
                    txtMangadexPageHintText.Text = $"Book detected. Tổng chapter/page: {totalPages}";
                    lblStatus.Text = $"MangaDex book ready: {GetMangadexPreferredTitle(manga.Attributes, mangaSlug)}";
                }
                else if (IsMangadexChapterUrl(normalized))
                {
                    if (!TryParseMangadexChapterId(normalized, out string chapterId, out _))
                    {
                        throw new Exception("Không tách được chapter id từ link MangaDex.");
                    }

                    MangadexAtHomeResponse atHome = await GetMangadexAtHomeAsync(chapterId, _downloadCts?.Token ?? CancellationToken.None);
                    int totalPages = Math.Max(
                        atHome?.Chapter?.Data?.Count ?? 0,
                        atHome?.Chapter?.DataSaver?.Count ?? 0);
                    totalPages = Math.Max(1, totalPages);
                    txtMangadexTotalPages.Text = totalPages.ToString(CultureInfo.InvariantCulture);
                    txtMangadexPageFrom.Text = "1";
                    txtMangadexPageTo.Text = totalPages.ToString(CultureInfo.InvariantCulture);
                    txtMangadexPageHintText.Text = $"Chapter detected. Tổng ảnh/page: {totalPages}";
                    lblStatus.Text = $"MangaDex chapter ready: {totalPages} page(s).";
                }
                else
                {
                    txtMangadexTotalPages.Text = "1";
                    txtMangadexPageFrom.Text = "1";
                    txtMangadexPageTo.Text = "1";
                    txtMangadexPageHintText.Text = "MangaDex URL ready.";
                    lblStatus.Text = "MangaDex URL ready.";
                }

                await ImportMangadexDirectLinksAsync(new List<string> { normalized }, clearExisting: false, showMessageBox: false);
            }
            catch (Exception ex)
            {
                MangadexLog("Lỗi phân tích: " + ex.Message);
                ShowWarning(ex.Message, "Thông báo");
                lblStatus.Text = "Analysis failed.";
                txtMangadexTotalPages.Text = "1";
                txtMangadexPageFrom.Text = "1";
                txtMangadexPageTo.Text = "1";
            }
            finally
            {
                progressBar.IsIndeterminate = false;
                btnMangadexAnalyze.IsEnabled = true;
                btnMangadexPasteDirect.IsEnabled = true;
            }
        }

        private void BtnMangadexPasteDirect_Click(object sender, RoutedEventArgs e)
        {
            var window = new DirectDownloadWindow(
                customTitle: "PASTE MANGADEX LINKS",
                customDescription: "Paste mangadex.org category, book, or chapter links below. App sẽ tự nhận diện đúng kiểu URL.",
                customExample:
                    "Example:\nhttps://mangadex.org/tag/423e2eae-a7a2-4a8b-ac03-a8351462d71d/romance\nhttps://mangadex.org/title/de9e3b62-eac5-4c0a-917d-ffccad694381/real-mo-tama-ni-wa-uso-o-tsuku\nhttps://mangadex.org/chapter/3746d002-3c0b-46f8-bf86-405bf45bf3e8")
            {
                Owner = this
            };

            window.OnImport = async links => await ImportMangadexDirectLinksAsync(links);
            window.ShowDialog();
        }

        private async Task ScrapeMangadexAsync(bool clearExisting)
        {
            string rawUrl = txtMangadexTagUrl?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                ShowWarning("Vui lòng nhập URL mangadex.org hợp lệ.", "Thông báo");
                return;
            }

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            btnMangadexScrape.Content = "STOP CRAWLER";
            btnMangadexCrawlMore.Content = "STOP CRAWLER";
            bool keepControlsEnabled = IsResultsImportingActive();
            if (!keepControlsEnabled)
            {
                btnMangadexAnalyze.IsEnabled = false;
            }

            progressBar.Value = 0;
            progressBar.IsIndeterminate = false;

            if (clearExisting)
            {
                _scrapedItems.Clear();
                lblLinkCount.Text = "0";
            }

            try
            {
                ShowTransientResultsImportingStatus("getting link...");
                await ImportMangadexDirectLinksAsync(new List<string> { rawUrl }, clearExisting: false, showMessageBox: true, token: token);
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = "Crawling cancelled.";
            }
            catch (Exception ex)
            {
                MangadexLog("Lỗi khi crawl: " + ex.Message);
                lblStatus.Text = "Crawling failed.";
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                btnMangadexScrape.Content = "GET LINK";
                btnMangadexCrawlMore.Content = "GET MORE";
                btnMangadexScrape.IsEnabled = true;
                btnMangadexCrawlMore.IsEnabled = true;
                btnMangadexAnalyze.IsEnabled = true;
                HideTransientResultsImportingStatus();
            }
        }

        private async Task<int> AddMangadexImportedItemsAsync(IEnumerable<GalleryItem> items, HashSet<string> existingLinks, string statusText = null)
        {
            const int batchSize = 40;
            List<GalleryItem> pendingItems = (items ?? Enumerable.Empty<GalleryItem>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Link))
                .Where(item => existingLinks == null || existingLinks.Add(item.Link))
                .ToList();

            int imported = 0;
            for (int i = 0; i < pendingItems.Count; i += batchSize)
            {
                List<GalleryItem> batch = pendingItems.Skip(i).Take(batchSize).ToList();
                await Dispatcher.InvokeAsync(() =>
                {
                    foreach (GalleryItem item in batch)
                    {
                        item.OriginalIndex = _scrapedItems.Count;
                        item.IsChecked = true;
                        _scrapedItems.Add(item);
                        imported++;
                    }

                    if (lblLinkCount != null)
                    {
                        lblLinkCount.Text = _scrapedItems.Count.ToString();
                    }

                    if (!string.IsNullOrWhiteSpace(statusText))
                    {
                        lblStatus.Text = statusText + $" | total: {_scrapedItems.Count}";
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);

                if (i + batchSize < pendingItems.Count)
                {
                    await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                }
            }

            return imported;
        }

        private async Task ImportMangadexDirectLinksAsync(IReadOnlyList<string> links, bool clearExisting = false, bool showMessageBox = true, CancellationToken? token = null)
        {
            if (links == null || links.Count == 0)
            {
                return;
            }

            SyncMangadexSessionLanguagesFromUi();

            CancellationToken effectiveToken = token ?? _downloadCts?.Token ?? CancellationToken.None;

            if (clearExisting)
            {
                _scrapedItems.Clear();
                lblLinkCount.Text = "0";
            }

            bool keepControlsEnabled = IsResultsImportingActive();
            if (!keepControlsEnabled)
            {
                btnMangadexAnalyze.IsEnabled = false;
            }

            progressBar.Value = 0;
            progressBar.IsIndeterminate = false;

            int imported = 0;
            int failed = 0;
            int total = links.Count;
            int processed = 0;
            var existingLinks = new HashSet<string>(
                _scrapedItems
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Link))
                    .Select(item => item.Link),
                StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (string rawLink in links)
                {
                    effectiveToken.ThrowIfCancellationRequested();

                    string normalized;
                    try
                    {
                        normalized = NormalizeMangadexUrl(rawLink);
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        processed++;
                        if (!keepControlsEnabled)
                        {
                            progressBar.Value = total == 0 ? 0 : (double)processed / total * 100d;
                        }

                        MangadexLog("Bỏ qua link lỗi: " + ex.Message);
                        continue;
                    }

                    txtMangadexTagUrl.Text = normalized;
                    lblStatus.Text = "Đang xử lý " + normalized;

                    try
                    {
                        bool isCategoryUrl = IsMangadexCategoryUrl(normalized);
                        int pageFrom = isCategoryUrl ? ParseMangadexPageBox(txtMangadexPageFrom, 1) : 1;
                        int pageTo = isCategoryUrl ? ParseMangadexPageBox(txtMangadexPageTo, ParseMangadexPageBox(txtMangadexTotalPages, 1)) : 1;
                        List<GalleryItem> items = await CreateMangadexItemsFromUrlAsync(
                            normalized,
                            effectiveToken,
                            pageFrom,
                            pageTo,
                            async (pageItems, page, endPage) =>
                            {
                                if (!keepControlsEnabled)
                                {
                                    double pageProgress = endPage <= 0 ? 0 : (double)page / endPage * 100d;
                                    progressBar.Value = pageProgress;
                                    lblStatus.Text = $"Đang lấy link trang {page}/{endPage} ({pageProgress:0}%)";
                                }

                                UpdateResultsCrawlProgress(page, endPage, GuessImportDisplayName(normalized));

                                if (pageItems == null || pageItems.Count == 0)
                                {
                                    return;
                                }

                                imported += await AddMangadexImportedItemsAsync(
                                    pageItems,
                                    existingLinks,
                                    $"MangaDex page {page}/{endPage}: +{pageItems.Count} item");
                            });

                        if (!isCategoryUrl)
                        {
                            imported += await AddMangadexImportedItemsAsync(items, existingLinks);
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        MangadexLog("Import lỗi với '" + normalized + "': " + ex.Message);
                    }

                    processed++;
                    if (!keepControlsEnabled)
                    {
                        progressBar.Value = total == 0 ? 0 : (double)processed / total * 100d;
                    }
                }

                RecalculateDuplicates();
                lblLinkCount.Text = _scrapedItems.Count.ToString(CultureInfo.InvariantCulture);
                lblStatus.Text = $"Imported {imported} mangadex item(s).";
                ShowImportSummaryIfNeeded(showMessageBox, links.Count, imported, failed);
            }
            finally
            {
                if (!keepControlsEnabled)
                {
                    btnMangadexAnalyze.IsEnabled = true;
                }
            }
        }

        private int ParseMangadexPageBox(TextBox box, int fallback)
        {
            if (box == null)
            {
                return Math.Max(1, fallback);
            }

            string text = box.Text?.Trim();
            if (int.TryParse(text, out int page))
            {
                return Math.Max(1, page);
            }

            return Math.Max(1, fallback);
        }

        private async Task<List<GalleryItem>> CreateMangadexItemsFromUrlAsync(
            string url,
            CancellationToken token,
            int? pageFrom = null,
            int? pageTo = null,
            Func<List<GalleryItem>, int, int, Task> onCategoryPageReady = null)
        {
            string normalized = NormalizeMangadexUrl(url);

            if (IsMangadexHomeUrl(normalized))
            {
                return new List<GalleryItem>();
            }

            if (TryParseMangadexTag(normalized, out string tagId, out string tagSlug))
            {
                return await ExtractMangadexCategoryItemsAsync(tagId, tagSlug, token, pageFrom ?? 1, pageTo ?? int.MaxValue, onCategoryPageReady);
            }

            if (TryParseMangadexMangaId(normalized, out string mangaId, out string mangaSlug))
            {
                MangadexMangaData manga = null;
                try
                {
                    manga = await GetMangadexMangaAsync(mangaId, token);
                }
                catch (Exception ex)
                {
                    MangadexLog("Import book fallback metadata: " + ex.Message);
                }

                string bookTitle = manga != null
                    ? GetMangadexPreferredTitle(manga.Attributes, mangaSlug)
                    : HumanizeMangadexSlug(mangaSlug);
                return new List<GalleryItem>
                {
                    new GalleryItem
                    {
                        Link = BuildMangadexMangaUrl(mangaId, string.IsNullOrWhiteSpace(mangaSlug) ? SlugifyTitle(bookTitle) : mangaSlug),
                        Name = AppendMangadexLanguageSuffix(bookTitle, _lastSelectedMangadexLangPrimary, _lastSelectedMangadexLangFallback),
                        LinkCount = string.Empty,
                        HoverPreviewThumbnailUrl = manga == null ? string.Empty : BuildMangadexCoverUrl(manga.Id, manga.CoverFileName),
                        SourceDomain = MangadexSiteFolder,
                        IsChecked = true,
                        MangadexLangPrimary = _lastSelectedMangadexLangPrimary,
                        MangadexLangFallback = _lastSelectedMangadexLangFallback
                    }
                };
            }

            if (TryParseMangadexChapterId(normalized, out string chapterId, out string chapterSlug))
            {
                MangadexChapterData chapter = await GetMangadexChapterAsync(chapterId, token);
                MangadexMangaData manga = await ResolveMangadexMangaForChapterAsync(chapter, token);
                string bookTitle = manga != null
                    ? GetMangadexPreferredTitle(manga.Attributes, string.Empty)
                    : "MangaDex";
                string chapterTitle = GetMangadexChapterDisplayTitle(chapter.Attributes, chapterSlug);
                return new List<GalleryItem>
                {
                    new GalleryItem
                    {
                        Link = BuildMangadexChapterUrl(chapterId),
                        Name = AppendMangadexLanguageSuffix(string.IsNullOrWhiteSpace(chapterTitle) ? bookTitle : $"{bookTitle} - {chapterTitle}", _lastSelectedMangadexLangPrimary, _lastSelectedMangadexLangFallback),
                        HoverPreviewThumbnailUrl = manga == null ? string.Empty : BuildMangadexCoverUrl(manga.Id, manga.CoverFileName),
                        SourceDomain = MangadexSiteFolder,
                        IsChecked = true,
                        MangadexLangPrimary = _lastSelectedMangadexLangPrimary,
                        MangadexLangFallback = _lastSelectedMangadexLangFallback
                    }
                };
            }

            throw new Exception("URL mangadex.org không hỗ trợ.");
        }

        private async Task<int> GetMangadexCategoryTotalPagesAsync(string url, CancellationToken token)
        {
            if (!TryParseMangadexTag(url, out string tagId, out _))
            {
                return 1;
            }

            MangadexListResponse<MangadexMangaData> response = await GetMangadexJsonAsync<MangadexListResponse<MangadexMangaData>>(
                BuildMangadexSearchUrl(tagId, 0, MangadexCategoryPageSize),
                token);
            int total = response?.Total ?? 0;
            return Math.Max(1, (int)Math.Ceiling(total / (double)MangadexCategoryPageSize));
        }

        private async Task<List<GalleryItem>> ExtractMangadexCategoryItemsAsync(
            string tagId,
            string tagSlug,
            CancellationToken token,
            int pageFrom,
            int pageTo,
            Func<List<GalleryItem>, int, int, Task> onPageReady = null)
        {
            MangadexListResponse<MangadexMangaData> firstPage = await GetMangadexJsonAsync<MangadexListResponse<MangadexMangaData>>(
                BuildMangadexSearchUrl(tagId, 0, MangadexCategoryPageSize),
                token);

            int totalItems = firstPage?.Total ?? 0;
            int totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)MangadexCategoryPageSize));
            int startPage = Math.Max(1, pageFrom);
            int endPage = Math.Min(Math.Max(startPage, pageTo), totalPages);
            var results = new List<GalleryItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int page = startPage; page <= endPage; page++)
            {
                token.ThrowIfCancellationRequested();
                MangadexListResponse<MangadexMangaData> response = page == 1 && firstPage != null
                    ? firstPage
                    : await GetMangadexJsonAsync<MangadexListResponse<MangadexMangaData>>(
                        BuildMangadexSearchUrl(tagId, (page - 1) * MangadexCategoryPageSize, MangadexCategoryPageSize),
                        token);

                List<GalleryItem> pageItems = ExtractMangadexCategoryItems(response?.Data, seen, tagSlug);
                results.AddRange(pageItems);

                if (onPageReady != null && pageItems.Count > 0)
                {
                    await onPageReady(pageItems, page, endPage);
                }
            }

            return results;
        }

        private List<GalleryItem> ExtractMangadexCategoryItems(IEnumerable<MangadexMangaData> mangas, HashSet<string> seen, string fallbackSlug)
        {
            var results = new List<GalleryItem>();
            foreach (MangadexMangaData manga in mangas ?? Enumerable.Empty<MangadexMangaData>())
            {
                if (manga == null || string.IsNullOrWhiteSpace(manga.Id))
                {
                    continue;
                }

                string title = GetMangadexPreferredTitle(manga.Attributes, fallbackSlug);
                string url = BuildMangadexMangaUrl(manga.Id, SlugifyTitle(title));
                if (!seen.Add(url))
                {
                    continue;
                }

                results.Add(new GalleryItem
                {
                    Link = url,
                    Name = AppendMangadexLanguageSuffix(title, _lastSelectedMangadexLangPrimary, _lastSelectedMangadexLangFallback),
                    HoverPreviewThumbnailUrl = BuildMangadexCoverUrl(manga.Id, manga.CoverFileName),
                    SourceDomain = MangadexSiteFolder,
                    IsChecked = true,
                    MangadexLangPrimary = _lastSelectedMangadexLangPrimary,
                    MangadexLangFallback = _lastSelectedMangadexLangFallback
                });
            }

            return results;
        }

        private async Task<MangadexMangaData> GetMangadexMangaAsync(string mangaId, CancellationToken token)
        {
            if (_mangadexMangaCache.TryGetValue(mangaId, out MangadexMangaData cached))
            {
                return cached;
            }

            var response = await GetMangadexJsonAsync<MangadexSingleResponse<MangadexMangaData>>(
                BuildMangadexApiUrl($"/manga/{mangaId}", new Dictionary<string, string>
                {
                    { "includes[]", "cover_art" }
                }),
                token);
            MangadexMangaData manga = response?.Data;
            if (manga == null)
            {
                throw new Exception("Không lấy được thông tin truyện từ MangaDex.");
            }

            manga.CoverFileName = GetMangadexCoverFileName(manga);
            _mangadexMangaCache[mangaId] = manga;
            return manga;
        }

        private async Task<MangadexChapterData> GetMangadexChapterAsync(string chapterId, CancellationToken token)
        {
            if (_mangadexChapterCache.TryGetValue(chapterId, out MangadexChapterData cached))
            {
                return cached;
            }

            var response = await GetMangadexJsonAsync<MangadexSingleResponse<MangadexChapterData>>(
                BuildMangadexApiUrl($"/chapter/{chapterId}", new Dictionary<string, string>
                {
                    { "includes[]", "manga" },
                    { "includes[]", "scanlation_group" }
                }),
                token);
            MangadexChapterData chapter = response?.Data;
            if (chapter == null)
            {
                throw new Exception("Không lấy được thông tin chapter từ MangaDex.");
            }

            _mangadexChapterCache[chapterId] = chapter;
            return chapter;
        }

        private async Task<MangadexMangaData> ResolveMangadexMangaForChapterAsync(MangadexChapterData chapter, CancellationToken token)
        {
            MangadexRelationship mangaRelation = chapter?.Relationships?
                .FirstOrDefault(relationship => relationship != null && relationship.Type == "manga");
            if (mangaRelation == null || string.IsNullOrWhiteSpace(mangaRelation.Id))
            {
                return null;
            }

            if (_mangadexMangaCache.TryGetValue(mangaRelation.Id, out MangadexMangaData cachedManga))
            {
                return cachedManga;
            }

            if (mangaRelation.Attributes != null || (mangaRelation.Relationships != null && mangaRelation.Relationships.Count > 0))
            {
                var manga = new MangadexMangaData
                {
                    Id = mangaRelation.Id,
                    Attributes = new MangadexMangaAttributes
                    {
                        Title = new Dictionary<string, string>()
                    },
                    Relationships = mangaRelation.Relationships,
                    CoverFileName = GetMangadexCoverFileName(mangaRelation)
                };
                _mangadexMangaCache[manga.Id] = manga;
                return manga;
            }

            var resolvedManga = await GetMangadexMangaAsync(mangaRelation.Id, token);
            if (resolvedManga != null)
            {
                _mangadexMangaCache[resolvedManga.Id] = resolvedManga;
            }
            return resolvedManga;
        }

        private string GetMangadexChapterGroupName(MangadexChapterData chapter)
        {
            var groupRelation = chapter?.Relationships?
                .FirstOrDefault(r => r != null && r.Type == "scanlation_group");
            return groupRelation?.Attributes?.Name ?? string.Empty;
        }

        private static string BuildMangadexBookUrl(string mangaId, int page = 1)
        {
            string safeId = (mangaId ?? string.Empty).Trim();
            string url = $"{MangadexBaseUrl}/title/{Uri.EscapeDataString(safeId)}";
            return page > 1
                ? url + "?page=" + page.ToString(CultureInfo.InvariantCulture)
                : url;
        }

        private async Task<string> GetMangadexRenderedHtmlAsync(string url, CancellationToken token)
        {
            await _mangadexBrowserFetchSemaphore.WaitAsync(token);
            try
            {
                EnsureMangadexBrowserDriverPool();
                return await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    ChromeDriver driver = RentMangadexBrowserDriver();
                    try
                    {
                        driver.Navigate().GoToUrl(url);
                        string html = string.Empty;
                        for (int attempt = 0; attempt < 60; attempt++)
                        {
                            token.ThrowIfCancellationRequested();
                            html = driver.PageSource ?? string.Empty;
                            if (html.IndexOf("/chapter/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                html.IndexOf("No chapters", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                html.IndexOf("chapter-grid", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                break;
                            }

                            Thread.Sleep(250);
                        }

                        return html;
                    }
                    finally
                    {
                        ReturnMangadexBrowserDriver(driver);
                    }
                }, token);
            }
            finally
            {
                _mangadexBrowserFetchSemaphore.Release();
            }
        }

        private static int ParseMangadexBookTotalPages(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return 1;
            }

            const string pagerClass = "flex justify-center flex-wrap gap-2 mt-6";
            int markerIndex = html.IndexOf(pagerClass, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                return 1;
            }

            int snippetStart = Math.Max(0, markerIndex - 128);
            int snippetLength = Math.Min(4096, html.Length - snippetStart);
            string snippet = html.Substring(snippetStart, snippetLength);
            int totalPages = 1;

            foreach (Match match in Regex.Matches(snippet, @">(?<page>\d{1,6})</span>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                if (int.TryParse(match.Groups["page"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int page))
                {
                    totalPages = Math.Max(totalPages, page);
                }
            }

            return Math.Max(1, totalPages);
        }

        private List<MangadexChapterDescriptor> ParseMangadexBookChaptersFromHtml(string html, HashSet<string> seen, ref int sequenceIndex)
        {
            var chapters = new List<MangadexChapterDescriptor>();
            if (string.IsNullOrWhiteSpace(html))
            {
                return chapters;
            }

            foreach (Match match in Regex.Matches(
                html,
                "<a[^>]+href=\"/chapter/(?<id>[0-9a-f-]{36})\"[^>]*class=\"[^\"]*flex\\s+flex-grow\\s+items-center[^\"]*\"[^>]*title=\"(?<title>[^\"]*)\"",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline))
            {
                string chapterId = (match.Groups["id"].Value ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(chapterId) || !seen.Add(chapterId))
                {
                    continue;
                }

                string rawTitle = WebUtility.HtmlDecode(match.Groups["title"].Value ?? string.Empty).Trim();
                string displayTitle = string.IsNullOrWhiteSpace(rawTitle)
                    ? NormalizeChapterLabel(chapterId)
                    : CompactSingleLine(rawTitle);

                chapters.Add(new MangadexChapterDescriptor
                {
                    Id = chapterId,
                    Url = BuildMangadexChapterUrl(chapterId),
                    DisplayTitle = displayTitle,
                    ChapterNumber = ParseMangadexChapterNumber(displayTitle),
                    SequenceIndex = sequenceIndex++
                });
            }

            return chapters;
        }

        private async Task<List<MangadexChapterDescriptor>> GetMangadexBookChaptersAsync(string mangaId, CancellationToken token)
        {
            return await GetMangadexBookChaptersAsync(mangaId, (GalleryItem)null, token);
        }

        private async Task<List<MangadexChapterDescriptor>> GetMangadexBookChaptersAsync(string mangaId, GalleryItem item, CancellationToken token)
        {
            List<string> languages = new List<string>();
            string primaryLang = "vi";
            bool useFallback = false;

            if (item != null)
            {
                primaryLang = item.MangadexLangPrimary ?? "vi";
                useFallback = item.MangadexLangFallback;
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    if (chkMangadexLangVi != null && chkMangadexLangVi.IsChecked == true) primaryLang = "vi";
                    else if (chkMangadexLangEn != null && chkMangadexLangEn.IsChecked == true) primaryLang = "en";
                    useFallback = chkMangadexLangFallback != null && chkMangadexLangFallback.IsChecked == true;
                });
            }

            languages.Add(primaryLang);
            if (useFallback)
            {
                string fallbackLang = primaryLang == "vi" ? "en" : "vi";
                languages.Add(fallbackLang);
            }

            List<MangadexChapterDescriptor> allChapters = await GetMangadexBookChaptersAsync(mangaId, languages, token);

            // Group and filter based on primary/fallback priority
            var filteredChapters = new List<MangadexChapterDescriptor>();
            var chaptersByNumber = allChapters
                .GroupBy(c => c.ChapterNumber == 0 ? (c.DisplayTitle ?? string.Empty).Trim().ToLowerInvariant() : c.ChapterNumber.ToString(CultureInfo.InvariantCulture))
                .ToList();

            foreach (var group in chaptersByNumber)
            {
                var primaryChapter = group.FirstOrDefault(c => string.Equals(c.TranslatedLanguage, primaryLang, StringComparison.OrdinalIgnoreCase));
                if (primaryChapter != null)
                {
                    filteredChapters.Add(primaryChapter);
                }
                else if (useFallback)
                {
                    string fallbackLang = primaryLang == "vi" ? "en" : "vi";
                    var fallbackChapter = group.FirstOrDefault(c => string.Equals(c.TranslatedLanguage, fallbackLang, StringComparison.OrdinalIgnoreCase));
                    if (fallbackChapter != null)
                    {
                        filteredChapters.Add(fallbackChapter);
                    }
                    else
                    {
                        var firstAvailable = group.FirstOrDefault();
                        if (firstAvailable != null)
                        {
                            filteredChapters.Add(firstAvailable);
                        }
                    }
                }
            }

            return filteredChapters.OrderBy(c => c.ChapterNumber).ThenBy(c => c.SequenceIndex).ToList();
        }

        private async Task<List<MangadexChapterDescriptor>> GetMangadexBookChaptersAsync(string mangaId, IEnumerable<string> translatedLanguages, CancellationToken token)
        {
            var allChapters = new List<MangadexChapterData>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int offset = 0;

            while (true)
            {
                token.ThrowIfCancellationRequested();
                MangadexListResponse<MangadexChapterData> response = await GetMangadexJsonAsync<MangadexListResponse<MangadexChapterData>>(
                    BuildMangadexFeedUrl(mangaId, offset, MangadexFeedPageSize, translatedLanguages),
                    token);
                List<MangadexChapterData> pageItems = response?.Data ?? new List<MangadexChapterData>();
                if (pageItems.Count == 0)
                {
                    break;
                }

                foreach (MangadexChapterData chapter in pageItems)
                {
                    string chapterId = (chapter?.Id ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(chapterId) || !seen.Add(chapterId))
                    {
                        continue;
                    }

                    _mangadexChapterCache[chapterId] = chapter;
                    allChapters.Add(chapter);
                }

                offset += pageItems.Count;
                if (pageItems.Count < MangadexFeedPageSize)
                {
                    break;
                }
            }

            string preferredGroup = string.Empty;
            Dispatcher.Invoke(() => preferredGroup = txtMangadexGroupFilter.Text.Trim());

            var filteredChapters = new List<MangadexChapterData>();
            var grouped = allChapters.GroupBy(c => new {
                Lang = (c.Attributes?.TranslatedLanguage ?? string.Empty).Trim().ToLower(),
                ChapNum = (c.Attributes?.Chapter ?? string.Empty).Trim()
            });

            foreach (var g in grouped)
            {
                if (string.IsNullOrEmpty(g.Key.ChapNum))
                {
                    filteredChapters.AddRange(g);
                }
                else
                {
                    MangadexChapterData selected = null;
                    if (!string.IsNullOrWhiteSpace(preferredGroup))
                    {
                        selected = g.FirstOrDefault(c =>
                        {
                            string gName = GetMangadexChapterGroupName(c);
                            return gName.IndexOf(preferredGroup, StringComparison.OrdinalIgnoreCase) >= 0;
                        });
                    }

                    if (selected == null)
                    {
                        selected = g.First();
                    }

                    filteredChapters.Add(selected);
                }
            }

            var chapters = new List<MangadexChapterDescriptor>();
            int sequenceIndex = 0;
            foreach (MangadexChapterData chapter in filteredChapters)
            {
                string displayTitle = GetMangadexChapterDisplayTitle(chapter.Attributes);
                if (!string.IsNullOrWhiteSpace(preferredGroup))
                {
                    string gName = GetMangadexChapterGroupName(chapter);
                    if (string.IsNullOrWhiteSpace(gName))
                    {
                        gName = "No Group";
                    }
                    displayTitle = $"{displayTitle}-group {gName}";
                }

                chapters.Add(new MangadexChapterDescriptor
                {
                    Id = chapter.Id,
                    Url = BuildMangadexChapterUrl(chapter.Id),
                    DisplayTitle = displayTitle,
                    ChapterNumber = ParseMangadexChapterNumber(displayTitle),
                    SequenceIndex = sequenceIndex++,
                    TranslatedLanguage = chapter.Attributes?.TranslatedLanguage
                });
            }

            return chapters;
        }

        private async Task<MangadexAtHomeResponse> GetMangadexAtHomeAsync(string chapterId, CancellationToken token)
        {
            MangadexAtHomeResponse response = await GetMangadexJsonAsync<MangadexAtHomeResponse>(
                BuildMangadexApiUrl($"/at-home/server/{chapterId}"),
                token);
            if (response == null || string.IsNullOrWhiteSpace(response.BaseUrl) || response.Chapter == null)
            {
                throw new Exception("Không lấy được danh sách ảnh chapter từ MangaDex.");
            }

            return response;
        }

        private static string GetMangadexCoverFileName(MangadexMangaData manga)
        {
            return GetMangadexCoverFileName(manga?.Relationships);
        }

        private static string GetMangadexCoverFileName(MangadexRelationship manga)
        {
            return GetMangadexCoverFileName(manga?.Relationships);
        }

        private static string GetMangadexCoverFileName(IEnumerable<MangadexRelationship> relationships)
        {
            MangadexRelationship cover = relationships?
                .FirstOrDefault(relationship => relationship != null && relationship.Type == "cover_art");
            if (cover == null)
            {
                return string.Empty;
            }

            if (cover.Attributes != null && !string.IsNullOrWhiteSpace(cover.Attributes.FileName))
            {
                return cover.Attributes.FileName.Trim();
            }

            return string.Empty;
        }

        private static string BuildMangadexSearchUrl(string tagId, int offset, int limit)
        {
            return $"{MangadexApiBaseUrl}/manga" +
                   $"?includedTags%5B%5D={Uri.EscapeDataString(tagId)}" +
                   "&contentRating%5B%5D=safe" +
                   "&contentRating%5B%5D=suggestive" +
                   "&contentRating%5B%5D=erotica" +
                   "&contentRating%5B%5D=pornographic" +
                   "&includes%5B%5D=cover_art" +
                   "&order%5BlatestUploadedChapter%5D=desc" +
                   $"&offset={offset.ToString(CultureInfo.InvariantCulture)}" +
                   $"&limit={limit.ToString(CultureInfo.InvariantCulture)}";
        }

        private static string BuildMangadexFeedUrl(string mangaId, int offset, int limit, string translatedLanguage = null)
        {
            return BuildMangadexFeedUrl(mangaId, offset, limit, string.IsNullOrWhiteSpace(translatedLanguage) ? null : new[] { translatedLanguage });
        }

        private static string BuildMangadexFeedUrl(string mangaId, int offset, int limit, IEnumerable<string> translatedLanguages)
        {
            string languageQuery = string.Empty;
            foreach (string language in (translatedLanguages ?? Enumerable.Empty<string>())
                .Select(value => (value ?? string.Empty).Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                languageQuery += $"&translatedLanguage%5B%5D={Uri.EscapeDataString(language)}";
            }

            return $"{MangadexApiBaseUrl}/manga/{Uri.EscapeDataString(mangaId ?? string.Empty)}/feed" +
                   $"?offset={offset.ToString(CultureInfo.InvariantCulture)}" +
                   $"&limit={limit.ToString(CultureInfo.InvariantCulture)}" +
                   languageQuery +
                   "&includes%5B%5D=scanlation_group" +
                   "&includeFutureUpdates=0" +
                   "&includeEmptyPages=0" +
                   "&includeExternalUrl=0" +
                   "&order%5Bvolume%5D=asc" +
                   "&order%5Bchapter%5D=asc" +
                   "&order%5BreadableAt%5D=asc";
        }

        private static string SlugifyTitle(string title)
        {
            string clean = WebUtility.HtmlDecode(title ?? string.Empty).Trim().ToLowerInvariant();
            clean = Regex.Replace(clean, @"[^a-z0-9]+", "-").Trim('-');
            return string.IsNullOrWhiteSpace(clean) ? "title" : clean;
        }

        private async Task<List<ReaderChapterItem>> GetMangadexReaderChapterItemsAsync(GalleryItem item, string url, CancellationToken token)
        {
            if (!TryParseMangadexMangaId(url, out string mangaId, out _))
            {
                return new List<ReaderChapterItem>();
            }

            // Với riêng mangadex, bất kể người dùng chọn tiếng Anh hay tiếng Việt đều phải scan chapter độc lập cho cả 2 ngôn ngữ của cùng một book.
            // Nên ta lấy tất cả các chapters của cả "vi" và "en" cùng một lúc để xử lý độc lập.
            List<MangadexChapterDescriptor> allChapters = await GetMangadexBookChaptersAsync(mangaId, new[] { "vi", "en" }, token);

            // Group theo TranslatedLanguage để đảm bảo độc lập, sau đó trong mỗi group ta group theo ChapterNumber/Title giống như cũ để tránh trùng lặp nhóm (group filter).
            var result = new List<ReaderChapterItem>();
            string preferredGroup = string.Empty;
            Dispatcher.Invoke(() => preferredGroup = txtMangadexGroupFilter?.Text?.Trim() ?? string.Empty);

            var chaptersByLang = allChapters.GroupBy(c => (c.TranslatedLanguage ?? string.Empty).Trim().ToLowerInvariant());
            foreach (var langGroup in chaptersByLang)
            {
                string lang = langGroup.Key; // "vi" hoặc "en"
                string suffix = lang == "vi" ? " [VI]" : " [EN]";

                var groupedByChap = langGroup.GroupBy(c => c.ChapterNumber == 0 ? (c.DisplayTitle ?? string.Empty).Trim().ToLowerInvariant() : c.ChapterNumber.ToString(CultureInfo.InvariantCulture));
                foreach (var g in groupedByChap)
                {
                    MangadexChapterDescriptor selected = null;
                    if (!string.IsNullOrWhiteSpace(preferredGroup))
                    {
                        selected = g.FirstOrDefault(c =>
                        {
                            // Tìm trong cache hoặc tự parse group name từ DisplayTitle
                            string displayName = c.DisplayTitle ?? string.Empty;
                            return displayName.IndexOf(preferredGroup, StringComparison.OrdinalIgnoreCase) >= 0;
                        });
                    }

                    if (selected == null)
                    {
                        selected = g.First();
                    }

                    // Thêm hậu tố ngôn ngữ để phân biệt rõ ràng từng book, giúp thống kê chuẩn xác thiếu chap nào của ngôn ngữ nào.
                    string nameWithLang = selected.DisplayTitle;
                    if (!nameWithLang.EndsWith(" [VI]", StringComparison.OrdinalIgnoreCase) && !nameWithLang.EndsWith(" [EN]", StringComparison.OrdinalIgnoreCase))
                    {
                        nameWithLang = nameWithLang + suffix;
                    }

                    result.Add(new ReaderChapterItem
                    {
                        Name = BuildDownloadChapterItemName(selected.Url, nameWithLang),
                        FolderPath = selected.Url,
                        Pages = new List<ReaderPageItem>(),
                        ParsedChapterNumber = selected.ChapterNumber
                    });
                }
            }

            return result;
        }

        private async Task DownloadMangadexGalleryAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem = null, ChapterFilter chapterFilter = null)
        {
            item.Link = NormalizeMangadexUrl(item.Link);

            if (IsMangadexChapterUrl(item.Link))
            {
                await DownloadMangadexChapterAsync(item, rootFolder, token, queueItem);
                return;
            }

            if (!IsMangadexBookUrl(item.Link))
            {
                throw new Exception("Link MangaDex không hợp lệ. Cần link book hoặc chapter.");
            }

            await DownloadMangadexBookAsync(item, rootFolder, token, queueItem, chapterFilter);
        }

        private async Task DownloadMangadexBookAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem, ChapterFilter chapterFilter)
        {
            string bookUrl = NormalizeMangadexUrl(item.Link);
            if (false && TryGetCachedDownloadChapterItems(item, out List<ReaderChapterItem> cachedItems) && cachedItems != null && cachedItems.Count > 0)
            {
                cachedItems = cachedItems
                    .Where(chapter => chapter != null &&
                                      !string.IsNullOrWhiteSpace(chapter.FolderPath) &&
                                      IsMangadexChapterUrl(chapter.FolderPath))
                    .ToList();

                if (cachedItems.Count > 0)
                {
                    double GetChapterNumber(ReaderChapterItem chapterItem)
                    {
                        if (chapterItem?.ParsedChapterNumber.HasValue == true)
                        {
                            return chapterItem.ParsedChapterNumber.Value;
                        }

                        return ParseMangadexChapterNumber(chapterItem?.Name);
                    }

                    var cachedChapterEntries = cachedItems
                        .Select(chapter => new
                        {
                            Link = chapter.FolderPath.Trim(),
                            Label = chapter.Name,
                            Number = GetChapterNumber(chapter)
                        })
                        .Where(entry => !string.IsNullOrWhiteSpace(entry.Link))
                        .GroupBy(entry => entry.Link, StringComparer.OrdinalIgnoreCase)
                        .Select(group => group
                            .OrderByDescending(entry => entry.Number)
                            .ThenBy(entry => entry.Label, StringComparer.OrdinalIgnoreCase)
                            .First())
                        .OrderBy(entry => entry.Number)
                        .ThenBy(entry => entry.Label, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var cachedLabelsByLink = cachedChapterEntries
                        .ToDictionary(entry => entry.Link, entry => entry.Label, StringComparer.OrdinalIgnoreCase);

                    List<string> cachedChapterLinks = (chapterFilter != null
                            ? cachedChapterEntries.Where(entry => chapterFilter.IsMatch(entry.Number))
                            : cachedChapterEntries)
                        .Select(entry => entry.Link)
                        .ToList();

                    List<string> effectiveChapterLinks = FilterPendingChapterLinksFromProcess(
                        rootFolder,
                        MangadexSiteFolder,
                        item,
                        cachedChapterLinks,
                        cachedLabelsByLink);

                    if (effectiveChapterLinks.Count == 0)
                    {
                        if (queueItem != null)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                queueItem.Status = "Completed";
                                queueItem.CurrentProcess = "Đã hoàn tất theo process";
                            });
                        }

                        return;
                    }

                    string cachedBookTitle = string.IsNullOrWhiteSpace(item.Name) ? GetSafePathName(bookUrl) : item.Name;
                    if (queueItem != null)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            queueItem.TotalChapters = effectiveChapterLinks.Count;
                            queueItem.CompletedChapters = 0;
                        });
                    }

                    int completedCount = 0;
                    foreach (string chapterLink in effectiveChapterLinks)
                    {
                        token.ThrowIfCancellationRequested();
                        var chapterItem = new GalleryItem
                        {
                            Link = chapterLink,
                            Name = cachedBookTitle,
                            SourceDomain = MangadexSiteFolder
                        };

                        bool completed = await DownloadMangadexChapterAsync(chapterItem, rootFolder, token, queueItem, isParentQueue: true, bookTitleOverride: cachedBookTitle);
                        if (completed)
                        {
                            MarkChapterProcessDone(rootFolder, MangadexSiteFolder, item, chapterLink);
                            completedCount++;
                        }

                        if (queueItem != null && completed)
                        {
                            Dispatcher.Invoke(() => queueItem.CompletedChapters = completedCount);
                        }
                    }

                    return;
                }
            }

            if (!TryParseMangadexMangaId(bookUrl, out string mangaId, out string mangaSlug))
            {
                throw new Exception("Không tách được manga id từ link MangaDex.");
            }

            MangadexMangaData manga = await GetMangadexMangaAsync(mangaId, token);
            List<MangadexChapterDescriptor> chapters = await GetMangadexBookChaptersAsync(mangaId, item, token);
            item.Name = AppendMangadexLanguageSuffix(GetMangadexPreferredTitle(manga.Attributes, mangaSlug), item.MangadexLangPrimary, item.MangadexLangFallback);

            List<ReaderChapterItem> chapterItems = chapters
                .Select(chapter => new ReaderChapterItem
                {
                    Name = BuildDownloadChapterItemName(chapter.Url, chapter.DisplayTitle),
                    FolderPath = chapter.Url,
                    Pages = new List<ReaderPageItem>(),
                    ParsedChapterNumber = chapter.ChapterNumber
                })
                .ToList();
            CacheDownloadMissingChapterItems(item, chapterItems);

            var chapterEntries = chapters
                .Where(chapter => !string.IsNullOrWhiteSpace(chapter?.Url))
                .Select(chapter => new
                {
                    Link = chapter.Url.Trim(),
                    Label = BuildDownloadChapterItemName(chapter.Url, chapter.DisplayTitle),
                    Number = chapter.ChapterNumber,
                    LanguageFolder = GetMangadexLanguageFolderName(chapter.TranslatedLanguage)
                })
                .GroupBy(entry => entry.Link, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(entry => entry.Number)
                    .ThenBy(entry => entry.Label, StringComparer.OrdinalIgnoreCase)
                    .First())
                .OrderBy(entry => entry.Number)
                .ThenBy(entry => entry.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var groups = chapterEntries
                .GroupBy(entry => entry.LanguageFolder)
                .ToList();

            int totalPendingChapters = 0;
            var pendingChaptersByGroup = new Dictionary<string, List<string>>();

            foreach (var group in groups)
            {
                string langFolder = group.Key;
                string siteFolderWithLang = Path.Combine(MangadexSiteFolder, langFolder);

                var groupEntries = group.ToList();
                var labelsByLink = groupEntries
                    .ToDictionary(entry => entry.Link, entry => entry.Label, StringComparer.OrdinalIgnoreCase);

                List<string> groupChapterLinks = (chapterFilter != null
                        ? groupEntries.Where(entry => chapterFilter.IsMatch(entry.Number))
                        : groupEntries)
                    .Select(entry => entry.Link)
                    .ToList();

                List<string> pendingGroupLinks = FilterPendingChapterLinksFromProcess(
                    rootFolder,
                    siteFolderWithLang,
                    item,
                    groupChapterLinks,
                    labelsByLink);

                if (pendingGroupLinks.Count > 0)
                {
                    pendingChaptersByGroup[langFolder] = pendingGroupLinks;
                    totalPendingChapters += pendingGroupLinks.Count;
                }
            }

            if (totalPendingChapters == 0)
            {
                if (queueItem != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        queueItem.Status = "Completed";
                        queueItem.CurrentProcess = "Đã hoàn tất theo process";
                    });
                }

                return;
            }

            if (queueItem != null)
            {
                Dispatcher.Invoke(() =>
                {
                    queueItem.TotalChapters = totalPendingChapters;
                    queueItem.CompletedChapters = 0;
                });
            }

            int completedCountApi = 0;
            foreach (var kvp in pendingChaptersByGroup)
            {
                string langFolder = kvp.Key;
                string siteFolderWithLang = Path.Combine(MangadexSiteFolder, langFolder);
                List<string> pendingLinks = kvp.Value;

                foreach (string chapterLink in pendingLinks)
                {
                    token.ThrowIfCancellationRequested();
                    var chapterItem = new GalleryItem
                    {
                        Link = chapterLink,
                        Name = item.Name,
                        SourceDomain = MangadexSiteFolder
                    };

                    bool completed = await DownloadMangadexChapterAsync(chapterItem, rootFolder, token, queueItem, isParentQueue: true, bookTitleOverride: item.Name);
                    if (completed)
                    {
                        MarkChapterProcessDone(rootFolder, siteFolderWithLang, item, chapterLink);
                        completedCountApi++;
                    }

                    if (queueItem != null && completed)
                    {
                        Dispatcher.Invoke(() => queueItem.CompletedChapters = completedCountApi);
                    }
                }
            }
        }

        private async Task<bool> DownloadMangadexChapterAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem = null, bool isParentQueue = false, string bookTitleOverride = null)
        {
            string chapterUrl = NormalizeMangadexUrl(item.Link);
            if (!TryParseMangadexChapterId(chapterUrl, out string chapterId, out string chapterSlug))
            {
                throw new Exception("Không tách được chapter id từ link MangaDex.");
            }

            MangadexChapterData chapter = await GetMangadexChapterAsync(chapterId, token);
            MangadexMangaData manga = await ResolveMangadexMangaForChapterAsync(chapter, token);
            MangadexAtHomeResponse atHome = await GetMangadexAtHomeAsync(chapterId, token);

            string bookTitle = string.IsNullOrWhiteSpace(bookTitleOverride)
                ? GetMangadexPreferredTitle(manga?.Attributes, string.Empty)
                : CleanMangadexText(bookTitleOverride);
            string chapterTitle = GetMangadexChapterDisplayTitle(chapter.Attributes, chapterSlug);
            if (string.IsNullOrWhiteSpace(bookTitle))
            {
                bookTitle = "MangaDex";
            }

            if (string.IsNullOrWhiteSpace(chapterTitle))
            {
                chapterTitle = NormalizeChapterLabel(chapterSlug.Replace("-", " "));
            }

            string preferredGroup = string.Empty;
            Dispatcher.Invoke(() => preferredGroup = txtMangadexGroupFilter.Text.Trim());
            if (!string.IsNullOrWhiteSpace(preferredGroup))
            {
                string gName = GetMangadexChapterGroupName(chapter);
                if (string.IsNullOrWhiteSpace(gName))
                {
                    gName = "No Group";
                }
                chapterTitle = $"{chapterTitle}-group {gName}";
            }

            item.Name = bookTitle;
            string processChapterLabel = CompactSingleLine(chapterTitle);
            string safeBook = GetCanonicalBookFolderName(item, bookTitle, "Unknown Book");
            string aliasSafeBook = GetSafePathName(bookTitle);
            string safeChapter = GetDownloadChapterFolderName(bookTitle, chapterTitle);
            if (!string.IsNullOrWhiteSpace(preferredGroup))
            {
                string gName = GetMangadexChapterGroupName(chapter);
                if (string.IsNullOrWhiteSpace(gName))
                {
                    gName = "No Group";
                }
                safeChapter = $"{safeChapter}-group {GetSafePathName(gName)}";
            }
            string langFolder = GetMangadexLanguageFolderName(chapter.Attributes?.TranslatedLanguage);
            string siteRootFolder = Path.Combine(GetSiteDownloadRoot(rootFolder, MangadexSiteFolder), langFolder);
            await NormalizeChapterFolderAliasAsync(siteRootFolder, safeBook, aliasSafeBook, safeChapter, token);

            string unmergedPath = Path.Combine(siteRootFolder, $"{safeBook}-{safeChapter}");
            string mergedPath = Path.Combine(siteRootFolder, safeBook, safeChapter);
            string finalTargetFolder = _isSingleComicFolderType ? mergedPath : unmergedPath;
            string tempFolder = BuildStableTempFolderPath(siteRootFolder, MangadexSiteFolder, safeBook, safeChapter, chapterUrl);
            Directory.CreateDirectory(tempFolder);
            RegisterTempFolder(tempFolder);

            try
            {
                List<string> imageUrls = BuildMangadexImageUrls(atHome, preferDataSaver: false);
                if (imageUrls.Count == 0)
                {
                    imageUrls = BuildMangadexImageUrls(atHome, preferDataSaver: true);
                }

                if (imageUrls.Count > 0)
                {
                    MangadexLog($"Ảnh chapter sample: {imageUrls[0]}");
                }

                if (imageUrls.Count == 0)
                {
                    throw new Exception("Không tìm thấy ảnh chapter MangaDex.");
                }

                if (queueItem != null && !isParentQueue)
                {
                    Dispatcher.Invoke(() =>
                    {
                        queueItem.TotalChapters = imageUrls.Count;
                        queueItem.CompletedChapters = 0;
                    });
                }

                if (queueItem != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        queueItem.DownloadingChapter = processChapterLabel;
                        queueItem.DownloadingPageProgress = $"1/{imageUrls.Count}";
                        queueItem.CurrentProcess = isParentQueue
                            ? $"{processChapterLabel} (trang 1/{imageUrls.Count})"
                            : $"1/{imageUrls.Count} pages";
                    });
                }

                WriteTempProgressLog(tempFolder, item, "Downloading", 0, imageUrls.Count, "0/0 pages", $"Bắt đầu tải {chapterTitle}");

                int maxThreads = GetCurrentConnectionLimit();
                List<string> pageFilenames = DetermineImageFilenames(imageUrls);
                using (var semaphore = new DynamicSemaphore(maxThreads, GetCurrentConnectionLimit))
                {
                    var tasks = new List<Task>();
                    int completedPages = 0;
                    object lockObj = new object();

                    for (int p = 0; p < imageUrls.Count; p++)
                    {
                        int index = p;
                        string imgUrl = imageUrls[index];

                        tasks.Add(Task.Run(async () =>
                        {
                            var pageWatch = System.Diagnostics.Stopwatch.StartNew();
                            while (_isDownloadPaused || (queueItem != null && queueItem.IsPaused))
                            {
                                token.ThrowIfCancellationRequested();
                                if (queueItem != null && queueItem.IsStopped)
                                {
                                    throw new OperationCanceledException();
                                }

                                await Task.Delay(200, token);
                            }

                            token.ThrowIfCancellationRequested();
                            await semaphore.WaitAsync(token);
                            try
                            {
                                string fileName = pageFilenames[index];
                                string localFilePath = Path.Combine(tempFolder, fileName);
                                if (File.Exists(localFilePath) && new FileInfo(localFilePath).Length > 1024)
                                {
                                    lock (lockObj)
                                    {
                                        completedPages++;
                                        string processText = isParentQueue
                                            ? $"{processChapterLabel} (trang {completedPages}/{imageUrls.Count})"
                                            : $"{completedPages}/{imageUrls.Count} pages";
                                        UpdateDownloadRowMetrics(queueItem, completedPages, imageUrls.Count, processText, 0, 0, isParentQueue);
                                    }

                                    return;
                                }

                                if (queueItem != null && queueItem.IsStopped)
                                {
                                    throw new OperationCanceledException();
                                }

                                try
                                {
                                    await DownloadUrlToFileWithRefererAsync(imgUrl, chapterUrl, localFilePath, token);
                                }
                                catch
                                {
                                    string fallbackImgUrl = GetMangadexFallbackImageUrl(imgUrl);
                                    if (string.IsNullOrWhiteSpace(fallbackImgUrl) ||
                                        string.Equals(fallbackImgUrl, imgUrl, StringComparison.OrdinalIgnoreCase))
                                    {
                                        throw;
                                    }

                                    await DownloadUrlToFileWithRefererAsync(fallbackImgUrl, chapterUrl, localFilePath, token);
                                }
                                pageWatch.Stop();
                                lock (lockObj)
                                {
                                    completedPages++;
                                    long downloadedBytes = File.Exists(localFilePath) ? new FileInfo(localFilePath).Length : 0;
                                    string processText = isParentQueue
                                        ? $"{processChapterLabel} (trang {completedPages}/{imageUrls.Count})"
                                        : $"{completedPages}/{imageUrls.Count} pages";
                                    UpdateDownloadRowMetrics(queueItem, completedPages, imageUrls.Count, processText, downloadedBytes, pageWatch.ElapsedMilliseconds, isParentQueue);
                                    if (queueItem != null)
                                    {
                                        int pageNumber = completedPages;
                                        Dispatcher.BeginInvoke((Action)(() => queueItem.DownloadingPageProgress = $"{pageNumber}/{imageUrls.Count}"));
                                    }
                                }
                            }
                            finally
                            {
                                semaphore.Release();
                            }
                        }, token));
                    }

                    await Task.WhenAll(tasks);
                }

                WriteTempProgressLog(tempFolder, item, "Done", imageUrls.Count, imageUrls.Count, $"{imageUrls.Count}/{imageUrls.Count} pages", "Download completed");
                MoveTempFolderToTarget(tempFolder, finalTargetFolder, "mangadex");
                return ValidateDownloadedFiles(finalTargetFolder, imageUrls.Count, queueItem ?? item, chapterTitle, chapterUrl: chapterUrl);
            }
            finally
            {
                UnregisterTempFolder(tempFolder);
            }
        }

        private List<string> BuildMangadexImageUrls(MangadexAtHomeResponse atHome, bool preferDataSaver)
        {
            var urls = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (atHome?.Chapter == null || string.IsNullOrWhiteSpace(atHome.BaseUrl) || string.IsNullOrWhiteSpace(atHome.Chapter.Hash))
            {
                return urls;
            }

            string quality = preferDataSaver ? "data-saver" : "data";
            IEnumerable<string> files = preferDataSaver ? atHome.Chapter.DataSaver : atHome.Chapter.Data;
            foreach (string fileName in files ?? Enumerable.Empty<string>())
            {
                string cleanFileName = WebUtility.HtmlDecode(fileName ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(cleanFileName))
                {
                    continue;
                }

                string lowerFileName = cleanFileName.ToLowerInvariant();
                if (lowerFileName.Contains("credit") || lowerFileName.Contains("icon"))
                {
                    continue;
                }

                switch (Path.GetExtension(cleanFileName).ToLowerInvariant())
                {
                    case ".webp":
                    case ".gif":
                    case ".jpg":
                    case ".jpeg":
                    case ".png":
                    case ".bmp":
                        break;
                    default:
                        continue;
                }

                string imageUrl = $"{atHome.BaseUrl.TrimEnd('/')}/{quality}/{atHome.Chapter.Hash}/{cleanFileName}";
                if (seen.Add(imageUrl))
                {
                    urls.Add(imageUrl);
                }
            }

            return urls;
        }

        private static string GetMangadexFallbackImageUrl(string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                return string.Empty;
            }

            try
            {
                var uri = new Uri(imageUrl);
                if (!uri.Host.Equals("uploads.mangadex.org", StringComparison.OrdinalIgnoreCase))
                {
                    var builder = new UriBuilder(uri)
                    {
                        Host = "uploads.mangadex.org",
                        Port = -1
                    };
                    return builder.Uri.AbsoluteUri;
                }
            }
            catch {}

            if (imageUrl.IndexOf("/data-saver/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return imageUrl.Replace("/data-saver/", "/data/");
            }

            if (imageUrl.IndexOf("/data/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return imageUrl.Replace("/data/", "/data-saver/");
            }

            return string.Empty;
        }

        [DataContract]
        private sealed class MangadexSingleResponse<T>
        {
            [DataMember(Name = "data")]
            public T Data { get; set; }
        }

        [DataContract]
        private sealed class MangadexListResponse<T>
        {
            [DataMember(Name = "data")]
            public List<T> Data { get; set; }

            [DataMember(Name = "total")]
            public int Total { get; set; }
        }

        [DataContract]
        private sealed class MangadexAtHomeResponse
        {
            [DataMember(Name = "baseUrl")]
            public string BaseUrl { get; set; }

            [DataMember(Name = "chapter")]
            public MangadexAtHomeChapter Chapter { get; set; }
        }

        [DataContract]
        private sealed class MangadexAtHomeChapter
        {
            [DataMember(Name = "hash")]
            public string Hash { get; set; }

            [DataMember(Name = "data")]
            public List<string> Data { get; set; }

            [DataMember(Name = "dataSaver")]
            public List<string> DataSaver { get; set; }
        }

        [DataContract]
        private sealed class MangadexMangaData
        {
            [DataMember(Name = "id")]
            public string Id { get; set; }

            [DataMember(Name = "attributes")]
            public MangadexMangaAttributes Attributes { get; set; }

            [DataMember(Name = "relationships")]
            public List<MangadexRelationship> Relationships { get; set; }

            public string CoverFileName { get; set; }
        }

        [DataContract]
        private sealed class MangadexChapterData
        {
            [DataMember(Name = "id")]
            public string Id { get; set; }

            [DataMember(Name = "attributes")]
            public MangadexChapterAttributes Attributes { get; set; }

            [DataMember(Name = "relationships")]
            public List<MangadexRelationship> Relationships { get; set; }
        }

        [DataContract]
        private sealed class MangadexRelationship
        {
            [DataMember(Name = "id")]
            public string Id { get; set; }

            [DataMember(Name = "type")]
            public string Type { get; set; }

            [DataMember(Name = "attributes")]
            public MangadexRelationshipAttributes Attributes { get; set; }

            [DataMember(Name = "relationships")]
            public List<MangadexRelationship> Relationships { get; set; }

            public string CoverFileName { get; set; }
        }

        [DataContract]
        private sealed class MangadexRelationshipAttributes
        {
            [DataMember(Name = "fileName")]
            public string FileName { get; set; }

            [DataMember(Name = "name")]
            public string Name { get; set; }
        }

        [DataContract]
        private sealed class MangadexMangaAttributes
        {
            [DataMember(Name = "title")]
            public Dictionary<string, string> Title { get; set; }
        }

        [DataContract]
        private sealed class MangadexChapterAttributes
        {
            [DataMember(Name = "chapter")]
            public string Chapter { get; set; }

            [DataMember(Name = "title")]
            public string Title { get; set; }

            [DataMember(Name = "translatedLanguage")]
            public string TranslatedLanguage { get; set; }
        }

        private sealed class MangadexChapterDescriptor
        {
            public string Id { get; set; }

            public string Url { get; set; }

            public string DisplayTitle { get; set; }

            public double ChapterNumber { get; set; }

            public int SequenceIndex { get; set; }

            public string TranslatedLanguage { get; set; }
        }

        private static string GetMangadexLanguageFolderName(string langCode)
        {
            if (string.IsNullOrWhiteSpace(langCode))
            {
                return "Unknown";
            }
            string clean = langCode.Trim().ToLowerInvariant();
            switch (clean)
            {
                case "vi":
                    return "Vietnamese";
                case "en":
                    return "English";
                case "ja":
                    return "Japanese";
                default:
                    if (clean.Length > 0)
                    {
                        return char.ToUpper(clean[0]) + clean.Substring(1);
                    }
                    return "Unknown";
            }
        }

        private string _lastSelectedMangadexLangPrimary = "vi";
        private bool _lastSelectedMangadexLangFallback = false;

        private void InitializeMangadexControls()
        {
            if (chkMangadexLangVi != null && chkMangadexLangEn != null)
            {
                chkMangadexLangVi.Checked += (s, e) =>
                {
                    if (chkMangadexLangEn.IsChecked == true) chkMangadexLangEn.IsChecked = false;
                };
                chkMangadexLangEn.Checked += (s, e) =>
                {
                    if (chkMangadexLangVi.IsChecked == true) chkMangadexLangVi.IsChecked = false;
                };
                chkMangadexLangVi.Unchecked += (s, e) =>
                {
                    if (chkMangadexLangEn.IsChecked != true) chkMangadexLangVi.IsChecked = true;
                };
                chkMangadexLangEn.Unchecked += (s, e) =>
                {
                    if (chkMangadexLangVi.IsChecked != true) chkMangadexLangEn.IsChecked = true;
                };
            }
        }

        private void SyncMangadexSessionLanguagesFromUi()
        {
            if (chkMangadexLangVi != null && chkMangadexLangVi.IsChecked == true)
            {
                _lastSelectedMangadexLangPrimary = "vi";
            }
            else if (chkMangadexLangEn != null && chkMangadexLangEn.IsChecked == true)
            {
                _lastSelectedMangadexLangPrimary = "en";
            }
            _lastSelectedMangadexLangFallback = chkMangadexLangFallback != null && chkMangadexLangFallback.IsChecked == true;
        }

        private string AppendMangadexLanguageSuffix(string title, string primary, bool fallback)
        {
            if (string.IsNullOrWhiteSpace(title)) return title;
            title = Regex.Replace(title, @"\s*\[MD-[a-z]+(\+[a-z]+)?\]", "");
            string suffix = fallback ? $"[MD-{primary}+{(primary == "vi" ? "en" : "vi")}]" : $"[MD-{primary}]";
            return $"{title} {suffix}";
        }

        private async Task<bool> PromptMangadexLanguageSelectionAsync()
        {
            return await Dispatcher.InvokeAsync(() =>
            {
                var dialog = new MangadexLanguageDialog(
                    _isVietnameseUi,
                    chkMangadexLangVi != null && chkMangadexLangVi.IsChecked == true,
                    chkMangadexLangEn != null && chkMangadexLangEn.IsChecked == true,
                    chkMangadexLangFallback != null && chkMangadexLangFallback.IsChecked == true)
                {
                    Owner = this
                };
                if (txtMangadexGroupFilter != null)
                {
                    dialog.SelectedGroup = txtMangadexGroupFilter.Text;
                }

                if (dialog.ShowDialog() == true)
                {
                    if (chkMangadexLangVi != null) chkMangadexLangVi.IsChecked = dialog.SelectedVi;
                    if (chkMangadexLangEn != null) chkMangadexLangEn.IsChecked = dialog.SelectedEn;
                    if (chkMangadexLangFallback != null) chkMangadexLangFallback.IsChecked = dialog.SelectedFallback;
                    if (txtMangadexGroupFilter != null) txtMangadexGroupFilter.Text = dialog.SelectedGroup ?? string.Empty;

                    _lastSelectedMangadexLangPrimary = dialog.SelectedVi ? "vi" : "en";
                    _lastSelectedMangadexLangFallback = dialog.SelectedFallback;
                    return true;
                }
                return false;
            });
        }

        private sealed class MangadexLanguageDialog : Window
        {
            public bool SelectedVi { get; set; }
            public bool SelectedEn { get; set; }
            public bool SelectedFallback { get; set; }
            public string SelectedGroup { get; set; }

            public MangadexLanguageDialog(bool isVietnameseUi, bool selectedVi, bool selectedEn, bool selectedFallback)
            {
                this.Title = isVietnameseUi ? "Ngôn ngữ MangaDex" : "MangaDex Language";
                this.Width = 340;
                this.Height = 335;
                this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                this.ResizeMode = ResizeMode.NoResize;
                this.Background = Application.Current.TryFindResource("CyberpunkWindowBackgroundBrush") as System.Windows.Media.Brush 
                    ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(26, 26, 26));
                this.ShowInTaskbar = false;

                var mainStack = new StackPanel { Margin = new Thickness(20) };

                var label = new TextBlock
                {
                    Text = isVietnameseUi 
                        ? "Phát hiện link MangaDex. Chọn ngôn ngữ tải:" 
                        : "MangaDex link detected. Select download language:",
                    Foreground = Application.Current.TryFindResource("CyberpunkTextBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.White,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 15),
                    TextWrapping = TextWrapping.Wrap
                };
                mainStack.Children.Add(label);

                var chkVi = new CheckBox
                {
                    Content = isVietnameseUi ? "Tiếng Việt" : "Vietnamese",
                    IsChecked = selectedVi,
                    Foreground = Application.Current.TryFindResource("CyberpunkTextBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.White,
                    Margin = new Thickness(0, 0, 0, 10),
                    FontWeight = FontWeights.Bold
                };
                mainStack.Children.Add(chkVi);

                var chkEn = new CheckBox
                {
                    Content = isVietnameseUi ? "Tiếng Anh" : "English",
                    IsChecked = selectedEn,
                    Foreground = Application.Current.TryFindResource("CyberpunkTextBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.White,
                    Margin = new Thickness(0, 0, 0, 10),
                    FontWeight = FontWeights.Bold
                };
                mainStack.Children.Add(chkEn);

                chkVi.Checked += (s, e) => { if (chkEn.IsChecked == true) chkEn.IsChecked = false; };
                chkEn.Checked += (s, e) => { if (chkVi.IsChecked == true) chkVi.IsChecked = false; };
                chkVi.Unchecked += (s, e) => { if (chkEn.IsChecked != true) chkVi.IsChecked = true; };
                chkEn.Unchecked += (s, e) => { if (chkVi.IsChecked != true) chkEn.IsChecked = true; };

                var chkFallback = new CheckBox
                {
                    IsChecked = selectedFallback,
                    Foreground = Application.Current.TryFindResource("CyberpunkTextBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.White,
                    Margin = new Thickness(0, 0, 0, 15),
                    FontWeight = FontWeights.Bold
                };
                var tbFallback = new TextBlock
                {
                    Text = isVietnameseUi 
                        ? "Tải ngôn ngữ phụ nếu ngôn ngữ chính không có chap" 
                        : "Download fallback language if primary has no chapter",
                    TextWrapping = TextWrapping.Wrap,
                    Width = 260
                };
                chkFallback.Content = tbFallback;
                mainStack.Children.Add(chkFallback);

                var groupLabel = new TextBlock
                {
                    Text = isVietnameseUi 
                        ? "Ưu tiên nhóm dịch (Không bắt buộc):" 
                        : "Preferred scanlator group (Optional):",
                    Foreground = Application.Current.TryFindResource("CyberpunkTextBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.White,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 5)
                };
                mainStack.Children.Add(groupLabel);

                var txtGroup = new TextBox
                {
                    Height = 26,
                    Margin = new Thickness(0, 0, 0, 20),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(14, 18, 26)),
                    BorderBrush = Application.Current.TryFindResource("CyberpunkBorderBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray,
                    Foreground = Application.Current.TryFindResource("CyberpunkYellowBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Yellow
                };
                this.Loaded += (s, e) => { txtGroup.Text = SelectedGroup ?? string.Empty; };
                mainStack.Children.Add(txtGroup);

                var btnStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

                var btnOk = new Button
                {
                    Content = "OK",
                    Width = 75,
                    Height = 28,
                    IsDefault = true,
                    Margin = new Thickness(0, 0, 10, 0),
                    Style = Application.Current.TryFindResource("CompactCyanButton") as Style
                };
                btnOk.Click += (s, e) =>
                {
                    if (chkVi.IsChecked != true && chkEn.IsChecked != true)
                    {
                        MessageBox.Show(
                            isVietnameseUi ? "Vui lòng chọn ít nhất một ngôn ngữ." : "Please select at least one language.",
                            "Warning",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }
                    SelectedVi = chkVi.IsChecked == true;
                    SelectedEn = chkEn.IsChecked == true;
                    SelectedFallback = chkFallback.IsChecked == true;
                    SelectedGroup = txtGroup.Text.Trim();
                    this.DialogResult = true;
                    this.Close();
                };

                var btnCancel = new Button
                {
                    Content = isVietnameseUi ? "Hủy" : "Cancel",
                    Width = 75,
                    Height = 28,
                    IsCancel = true,
                    Style = Application.Current.TryFindResource("CompactPinkButton") as Style
                };
                btnCancel.Click += (s, e) =>
                {
                    this.DialogResult = false;
                    this.Close();
                };

                btnStack.Children.Add(btnOk);
                btnStack.Children.Add(btnCancel);
                mainStack.Children.Add(btnStack);

                this.Content = mainStack;
            }
        }
    }
}

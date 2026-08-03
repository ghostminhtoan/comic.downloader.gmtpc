using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace get_link_manga
{
    public partial class MainWindow
    {
        private const string FirecrawlDefaultApiUrl = "https://api.firecrawl.dev";
        private static int _firecrawlCliAvailable = -1;

        [DataContract]
        private sealed class FirecrawlScrapeRequest
        {
            [DataMember(Name = "url")]
            public string Url { get; set; }

            [DataMember(Name = "formats")]
            public List<string> Formats { get; set; }

            [DataMember(Name = "onlyMainContent")]
            public bool OnlyMainContent { get; set; }

            [DataMember(Name = "onlyCleanContent")]
            public bool OnlyCleanContent { get; set; }

            [DataMember(Name = "waitFor")]
            public int WaitFor { get; set; }

            [DataMember(Name = "timeout")]
            public int Timeout { get; set; }

            [DataMember(Name = "blockAds")]
            public bool BlockAds { get; set; }

            [DataMember(Name = "proxy")]
            public string Proxy { get; set; }
        }

        [DataContract]
        private sealed class FirecrawlScrapeResponse
        {
            [DataMember(Name = "success")]
            public bool Success { get; set; }

            [DataMember(Name = "data")]
            public FirecrawlScrapeData Data { get; set; }
        }

        [DataContract]
        private sealed class FirecrawlScrapeData
        {
            [DataMember(Name = "html")]
            public string Html { get; set; }

            [DataMember(Name = "rawHtml")]
            public string RawHtml { get; set; }

            [DataMember(Name = "links")]
            public List<string> Links { get; set; }
        }

        [DataContract]
        private sealed class FirecrawlCliScrapeResponse
        {
            [DataMember(Name = "html")]
            public string Html { get; set; }

            [DataMember(Name = "rawHtml")]
            public string RawHtml { get; set; }

            [DataMember(Name = "links")]
            public List<string> Links { get; set; }
        }

        private sealed class FirecrawlPageSnapshot
        {
            public string Html { get; set; }
            public List<string> Links { get; set; } = new List<string>();
        }

        private static string GetFirecrawlApiKey()
        {
            return (Environment.GetEnvironmentVariable("FIRECRAWL_API_KEY") ?? string.Empty).Trim();
        }

        private static string GetFirecrawlApiBaseUrl()
        {
            string configured = (Environment.GetEnvironmentVariable("FIRECRAWL_API_URL") ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(configured)
                ? FirecrawlDefaultApiUrl
                : configured.TrimEnd('/');
        }

        private static bool CanUseFirecrawl()
        {
            return !string.IsNullOrWhiteSpace(GetFirecrawlApiKey()) || IsFirecrawlCliAvailable();
        }

        private static bool IsFirecrawlCliAvailable()
        {
            if (_firecrawlCliAvailable >= 0)
            {
                return _firecrawlCliAvailable == 1;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c where firecrawl",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    process.WaitForExit(5000);
                    _firecrawlCliAvailable = process.ExitCode == 0 ? 1 : 0;
                }
            }
            catch
            {
                _firecrawlCliAvailable = 0;
            }

            return _firecrawlCliAvailable == 1;
        }

        private async Task<string> TryFetchHtmlByFirecrawlAsync(string normalizedUrl, CancellationToken token)
        {
            FirecrawlPageSnapshot snapshot = await TryFetchPageByFirecrawlAsync(normalizedUrl, token);
            return snapshot?.Html;
        }

        private async Task<FirecrawlPageSnapshot> TryFetchPageByFirecrawlAsync(string normalizedUrl, CancellationToken token, bool preferFastChapterList = false)
        {
            string apiKey = GetFirecrawlApiKey();
            IEnumerable<string> urlsToScrape;
            if (IsHakoUrl(normalizedUrl))
            {
                urlsToScrape = BuildPreferredHakoFirecrawlUrls(normalizedUrl, preferFastChapterList);
            }
            else
            {
                urlsToScrape = new[] { normalizedUrl };
            }

            foreach (string candidateUrl in urlsToScrape)
            {
                FirecrawlPageSnapshot snapshot = null;
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    snapshot = await TryScrapePageByFirecrawlApiAsync(candidateUrl, apiKey, token, preferFastChapterList);
                }

                if (!HasFirecrawlContent(snapshot) && IsFirecrawlCliAvailable())
                {
                    snapshot = await TryScrapePageByFirecrawlCliAsync(candidateUrl, token);
                }

                if (HasFirecrawlContent(snapshot))
                {
                    if (IsHakoUrl(normalizedUrl))
                    {
                        HakoLog($"Firecrawl da tra ve du lieu cho Hako tu {candidateUrl}.");
                    }
                    else
                    {
                        Log($"[firecrawl] Firecrawl da tra ve du lieu tu {candidateUrl}.");
                    }
                    return snapshot;
                }
            }

            if (IsHakoUrl(normalizedUrl))
            {
                HakoLog($"Firecrawl khong kha dung. Chuyen sang WebView2 Fetcher cho {normalizedUrl}...");
            }
            else
            {
                Log($"[firecrawl-fallback] Firecrawl khong kha dung. Chuyen sang WebView2 Fetcher cho {normalizedUrl}...");
            }

            return await TryScrapePageByWebView2Async(normalizedUrl, token);
        }

        private async Task<string> TryFetchHakoHtmlByFirecrawlAsync(string normalizedUrl, CancellationToken token)
        {
            // ponytail: Hako Firecrawl chỉ dùng API/CLI thuần, không fallback WebView2.
            // FetchHakoHtmlAsync sẽ tự check blocked và mở WebView2 khi thật sự cần.
            string apiKey = GetFirecrawlApiKey();
            IEnumerable<string> urlsToScrape = IsHakoUrl(normalizedUrl)
                ? BuildPreferredHakoFirecrawlUrls(normalizedUrl, false)
                : new[] { normalizedUrl };

            foreach (string candidateUrl in urlsToScrape)
            {
                FirecrawlPageSnapshot snapshot = null;
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    snapshot = await TryScrapePageByFirecrawlApiAsync(candidateUrl, apiKey, token, false);
                }

                if (!HasFirecrawlContent(snapshot) && IsFirecrawlCliAvailable())
                {
                    snapshot = await TryScrapePageByFirecrawlCliAsync(candidateUrl, token);
                }

                if (HasFirecrawlContent(snapshot))
                {
                    HakoLog($"Firecrawl da tra ve du lieu cho Hako tu {candidateUrl}.");
                    return snapshot.Html;
                }
            }

            return null;
        }

        private async Task<FirecrawlPageSnapshot> TryFetchHakoPageByFirecrawlAsync(string normalizedUrl, CancellationToken token, bool preferFastChapterList = false)
        {
            // ponytail: Hako Firecrawl page fetch chỉ dùng API/CLI thuần, không fallback WebView2.
            string apiKey = GetFirecrawlApiKey();
            IEnumerable<string> urlsToScrape = IsHakoUrl(normalizedUrl)
                ? BuildPreferredHakoFirecrawlUrls(normalizedUrl, preferFastChapterList)
                : new[] { normalizedUrl };

            foreach (string candidateUrl in urlsToScrape)
            {
                FirecrawlPageSnapshot snapshot = null;
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    snapshot = await TryScrapePageByFirecrawlApiAsync(candidateUrl, apiKey, token, preferFastChapterList);
                }

                if (!HasFirecrawlContent(snapshot) && IsFirecrawlCliAvailable())
                {
                    snapshot = await TryScrapePageByFirecrawlCliAsync(candidateUrl, token);
                }

                if (HasFirecrawlContent(snapshot))
                {
                    HakoLog($"Firecrawl da tra ve du lieu cho Hako tu {candidateUrl}.");
                    return snapshot;
                }
            }

            return null;
        }

        private async Task<FirecrawlPageSnapshot> TryScrapePageByFirecrawlApiAsync(string url, string apiKey, CancellationToken token, bool preferFastChapterList)
        {
            string endpoint = GetFirecrawlApiBaseUrl() + "/v2/scrape";
            var payload = new FirecrawlScrapeRequest
            {
                Url = url,
                Formats = new List<string> { "html", "links" },
                OnlyMainContent = false,
                OnlyCleanContent = false,
                WaitFor = preferFastChapterList ? 0 : 1200,
                Timeout = preferFastChapterList ? 15000 : 60000,
                BlockAds = true,
                Proxy = "auto"
            };

            using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                request.Content = new StringContent(SerializeJson(payload), Encoding.UTF8, "application/json");

                using (HttpResponseMessage response = await _httpClient.SendAsync(request, token))
                {
                    string responseJson = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        HakoLog($"Firecrawl scrape loi {(int)response.StatusCode} voi {url}.");
                        return null;
                    }

                    FirecrawlScrapeResponse parsed = DeserializeJson<FirecrawlScrapeResponse>(responseJson);
                    return new FirecrawlPageSnapshot
                    {
                        Html = !string.IsNullOrWhiteSpace(parsed?.Data?.Html)
                            ? parsed.Data.Html
                            : parsed?.Data?.RawHtml,
                        Links = parsed?.Data?.Links ?? new List<string>()
                    };
                }
            }
        }

        private async Task<FirecrawlPageSnapshot> TryScrapePageByFirecrawlCliAsync(string url, CancellationToken token)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), "Comic-GMTPC", "firecrawl", Guid.NewGuid().ToString("N") + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(tempFile));

            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c firecrawl scrape \"{url}\" --format \"html,links\" --json -o \"{tempFile}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            try
            {
                using (var process = new Process { StartInfo = startInfo })
                {
                    process.Start();

                    Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                    Task<string> stderrTask = process.StandardError.ReadToEndAsync();
                    using (token.Register(() => TryKillProcess(process)))
                    {
                        await Task.Run(() => process.WaitForExit(), token);
                    }

                    string stdout = await stdoutTask;
                    string stderr = await stderrTask;

                    if (process.ExitCode != 0)
                    {
                        HakoLog($"Firecrawl CLI loi {process.ExitCode} voi {url}. {FirstNonEmptyLine(stderr, stdout)}");
                        return null;
                    }
                }

                if (!File.Exists(tempFile))
                {
                    HakoLog($"Firecrawl CLI khong tao file ket qua cho {url}.");
                    return null;
                }

                FirecrawlCliScrapeResponse parsed = DeserializeJson<FirecrawlCliScrapeResponse>(File.ReadAllText(tempFile, Encoding.UTF8));
                return new FirecrawlPageSnapshot
                {
                    Html = !string.IsNullOrWhiteSpace(parsed?.Html)
                        ? parsed.Html
                        : parsed?.RawHtml,
                    Links = parsed?.Links ?? new List<string>()
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                HakoLog($"Firecrawl CLI fallback loi voi {url}: {ex.Message}");
                return null;
            }
            finally
            {
                try
                {
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
                catch
                {
                }
            }
        }

        private static bool HasFirecrawlContent(FirecrawlPageSnapshot snapshot)
        {
            return snapshot != null &&
                   (!string.IsNullOrWhiteSpace(snapshot.Html) ||
                    (snapshot.Links != null && snapshot.Links.Count > 0));
        }

        private static void TryKillProcess(Process process)
        {
            try
            {
                if (process != null && !process.HasExited)
                {
                    process.Kill();
                }
            }
            catch
            {
            }
        }

        private static string FirstNonEmptyLine(params string[] values)
        {
            foreach (string value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                using (var reader = new StringReader(value))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            return line;
                        }
                    }
                }
            }

            return string.Empty;
        }

        private static IEnumerable<string> BuildPreferredHakoFirecrawlUrls(string normalizedUrl, bool preferFastChapterList)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> candidates = new List<string>();

            if (Uri.TryCreate(normalizedUrl, UriKind.Absolute, out Uri uri))
            {
                candidates.Add(uri.AbsoluteUri);

                if (!string.Equals(uri.Host, "docln.net", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(ReplaceHost(normalizedUrl, "docln.net"));
                }

                if (!string.Equals(uri.Host, "ln.hako.vn", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(ReplaceHost(normalizedUrl, "ln.hako.vn"));
                }

                if (!string.Equals(uri.Host, "docln.sbs", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(ReplaceHost(normalizedUrl, "docln.sbs"));
                }

                if (!string.Equals(uri.Host, "docln.co", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(ReplaceHost(normalizedUrl, "docln.co"));
                }

                if (!preferFastChapterList && !string.Equals(uri.Host, "ln.hako.re", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(ReplaceHost(normalizedUrl, "ln.hako.re"));
                }
            }
            else
            {
                candidates.Add(normalizedUrl);
                candidates.Add(ReplaceHost(normalizedUrl, "docln.net"));
                candidates.Add(ReplaceHost(normalizedUrl, "ln.hako.vn"));
                candidates.Add(ReplaceHost(normalizedUrl, "docln.sbs"));
                candidates.Add(ReplaceHost(normalizedUrl, "docln.co"));
                if (!preferFastChapterList)
                {
                    candidates.Add(ReplaceHost(normalizedUrl, "ln.hako.re"));
                }
            }

            foreach (string candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate) && seen.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }

        private static string ReplaceHost(string absoluteUrl, string host)
        {
            if (string.IsNullOrWhiteSpace(absoluteUrl) || string.IsNullOrWhiteSpace(host))
            {
                return absoluteUrl;
            }

            if (!Uri.TryCreate(absoluteUrl, UriKind.Absolute, out Uri uri))
            {
                return absoluteUrl;
            }

            var builder = new UriBuilder(uri)
            {
                Host = host
            };
            return builder.Uri.AbsoluteUri;
        }

        private static string SerializeJson<T>(T value)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static T DeserializeJson<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return serializer.ReadObject(stream) as T;
            }
        }

        private async Task<FirecrawlPageSnapshot> TryScrapePageByWebView2Async(string url, CancellationToken token)
        {
            try
            {
                string resolvedHtml = null;
                bool solved = false;

                await await Dispatcher.InvokeAsync(async () =>
                {
                    token.ThrowIfCancellationRequested();
                    // Thử chạy ngầm (headless) trước
                    var captchaWin = CreateCaptchaWindow(url, autoDeleteCookiesOnLoad: false, headlessAutomation: true);
                    captchaWin.Owner = this;

                    if (await ShowCaptchaWindowWithFocusHandlingAsync(captchaWin, useNovelFocusStealth: _lightNovelAutoFocusEnabled))
                    {
                        resolvedHtml = captchaWin.ResolvedHtml;
                        solved = true;
                    }
                    else
                    {
                        token.ThrowIfCancellationRequested();
                        // Chạy ngầm thất bại (do Cloudflare chặn gắt hoặc cần tương tác người dùng), thử visible mode
                        if (IsHakoUrl(url))
                        {
                            HakoLog($"[webview2-fetcher] Headless fail cho Hako. Mo captcha window visible...");
                        }
                        else
                        {
                            Log($"[webview2-fetcher] Headless fail. Mo captcha window visible cho {url}...");
                        }
                        
                        var visibleWin = CreateCaptchaWindow(url, autoDeleteCookiesOnLoad: false, headlessAutomation: false);
                        visibleWin.Owner = this;
                        if (await ShowCaptchaWindowWithFocusHandlingAsync(visibleWin, useNovelFocusStealth: _lightNovelAutoFocusEnabled))
                        {
                            resolvedHtml = visibleWin.ResolvedHtml;
                            solved = true;
                        }
                    }
                });

                if (solved && !string.IsNullOrWhiteSpace(resolvedHtml))
                {
                    var links = ExtractLinksFromHtml(resolvedHtml, url);
                    return new FirecrawlPageSnapshot
                    {
                        Html = resolvedHtml,
                        Links = links
                    };
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (IsHakoUrl(url))
                {
                    HakoLog($"[webview2-fetcher] Loi: {ex.Message}");
                }
                else
                {
                    Log($"[webview2-fetcher] Loi: {ex.Message}");
                }
            }
            return null;
        }

        private List<string> ExtractLinksFromHtml(string html, string baseUrl)
        {
            var links = new List<string>();
            if (string.IsNullOrWhiteSpace(html)) return links;

            try
            {
                var matches = Regex.Matches(html, @"href\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                Uri baseUri = Uri.TryCreate(baseUrl, UriKind.Absolute, out var temp) ? temp : null;

                foreach (Match match in matches)
                {
                    string href = match.Groups[1].Value;
                    if (string.IsNullOrWhiteSpace(href)) continue;

                    if (baseUri != null && Uri.TryCreate(baseUri, href, out var absoluteUri))
                    {
                        links.Add(absoluteUri.AbsoluteUri);
                    }
                    else
                    {
                        links.Add(href);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[webview2-fetcher] Extract links error: {ex.Message}");
            }

            return links;
        }
    }
}


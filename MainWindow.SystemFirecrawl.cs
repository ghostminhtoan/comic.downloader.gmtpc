using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace get_link_manga
{
    public partial class MainWindow
    {
        private const string FirecrawlDefaultApiUrl = "https://api.firecrawl.dev";

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

        private async Task<string> TryFetchHakoHtmlByFirecrawlAsync(string normalizedUrl, CancellationToken token)
        {
            FirecrawlPageSnapshot snapshot = await TryFetchHakoPageByFirecrawlAsync(normalizedUrl, token);
            return snapshot?.Html;
        }

        private async Task<FirecrawlPageSnapshot> TryFetchHakoPageByFirecrawlAsync(string normalizedUrl, CancellationToken token, bool preferFastChapterList = false)
        {
            if (!IsHakoUrl(normalizedUrl))
            {
                return null;
            }

            string apiKey = GetFirecrawlApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return null;
            }

            foreach (string candidateUrl in BuildPreferredHakoFirecrawlUrls(normalizedUrl, preferFastChapterList))
            {
                FirecrawlPageSnapshot snapshot = await TryScrapePageByFirecrawlAsync(candidateUrl, apiKey, token, preferFastChapterList);
                if (snapshot != null && (!string.IsNullOrWhiteSpace(snapshot.Html) || (snapshot.Links != null && snapshot.Links.Count > 0)))
                {
                    HakoLog($"Firecrawl da tra ve du lieu cho Hako tu {candidateUrl}.");
                    return snapshot;
                }
            }

            return null;
        }

        private async Task<FirecrawlPageSnapshot> TryScrapePageByFirecrawlAsync(string url, string apiKey, CancellationToken token, bool preferFastChapterList)
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
    }
}

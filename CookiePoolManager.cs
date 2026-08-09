using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;

namespace get_link_manga
{
    internal static class CookiePoolManager
    {
        private const int DefaultSlotsPerDomain = 3;

        private static readonly ConcurrentDictionary<string, int> _activeProfileIndexByDomain =
            new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<int, CookieContainer>> _poolCookies =
            new ConcurrentDictionary<string, ConcurrentDictionary<int, CookieContainer>>(StringComparer.OrdinalIgnoreCase);

        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<int, string>> _poolUserAgents =
            new ConcurrentDictionary<string, ConcurrentDictionary<int, string>>(StringComparer.OrdinalIgnoreCase);

        public static int GetNextProfileIndex(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain)) return 1;

            string key = NormalizeDomain(domain);
            return _activeProfileIndexByDomain.AddOrUpdate(
                key,
                1,
                (k, current) => (current % DefaultSlotsPerDomain) + 1
            );
        }

        public static string GetProfileUserDataFolder(string domain, int profileIndex)
        {
            string baseFolder = PortablePaths.WebView2CaptchaUserDataFolder;
            string domainFolder = GetCaptchaFolderNameFromDomain(domain);
            return Path.Combine(baseFolder, domainFolder, $"profile_{profileIndex}");
        }

        public static CookieContainer GetCookieContainer(string domain, int profileIndex)
        {
            string key = NormalizeDomain(domain);
            var domainDict = _poolCookies.GetOrAdd(key, _ => new ConcurrentDictionary<int, CookieContainer>());
            return domainDict.GetOrAdd(profileIndex, _ => new CookieContainer());
        }

        public static void SetUserAgent(string domain, int profileIndex, string userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent)) return;
            string key = NormalizeDomain(domain);
            var domainDict = _poolUserAgents.GetOrAdd(key, _ => new ConcurrentDictionary<int, string>());
            domainDict[profileIndex] = userAgent;
        }

        public static string GetUserAgent(string domain, int profileIndex)
        {
            string key = NormalizeDomain(domain);
            if (_poolUserAgents.TryGetValue(key, out var domainDict) &&
                domainDict.TryGetValue(profileIndex, out var ua))
            {
                return ua;
            }
            return null;
        }

        public static void ClearDomainPool(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain)) return;
            string key = NormalizeDomain(domain);

            _poolCookies.TryRemove(key, out _);
            _poolUserAgents.TryRemove(key, out _);
            _activeProfileIndexByDomain.TryRemove(key, out _);

            string domainFolder = GetCaptchaFolderNameFromDomain(domain);
            string path = Path.Combine(PortablePaths.WebView2CaptchaUserDataFolder, domainFolder);
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch { }
        }

        private static string NormalizeDomain(string urlOrDomain)
        {
            if (string.IsNullOrWhiteSpace(urlOrDomain)) return "general";
            try
            {
                if (urlOrDomain.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    urlOrDomain.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    return new Uri(urlOrDomain).Host.ToLowerInvariant();
                }
            }
            catch { }
            return urlOrDomain.Trim().ToLowerInvariant();
        }

        private static string GetCaptchaFolderNameFromDomain(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain)) return "general";
            string d = domain.ToLowerInvariant();
            if (d.Contains("truyenqq")) return "truyenqq";
            if (d.Contains("nettruyenviet10.com")) return "nettruyenviet10.com";
            if (d.Contains("nettruyen")) return "nettruyen";
            if (d.Contains("vi-hentai") || d.Contains("hentaivn")) return "hentaivn";
            if (d.Contains("hentai2read")) return "hentai2read";
            if (d.Contains("daomeoden")) return "daomeoden";
            if (d.Contains("nhentai")) return "nhentai.net";
            if (d.Contains("hentaiforce")) return "hentaiforce";
            if (d.Contains("hentaiera")) return "hentaiera";
            if (d.Contains("damconuong")) return "damconuong";
            if (d.Contains("hako") || d.Contains("docln")) return "hako";
            if (d.Contains("truyengg") || d.Contains("sayhentai")) return "truyengg";
            if (d.Contains("mangadex")) return "mangadex";
            if (d.Contains("doctruyen")) return "doctruyen";
            if (d.Contains("dilib") || d.Contains("thuviensach")) return "dilib";
            if (d.Contains("haibaba")) return "haibaba";

            var parts = d.Split('.');
            return parts.Length >= 2 ? parts[parts.Length - 2] : d;
        }
    }
}

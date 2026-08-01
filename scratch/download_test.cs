using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Net;
using System.Collections.Generic;

public class Program
{
    public static void Main()
    {
        string bookUrl = "http://haibabamanga.somee.com/Home/Detail?slug=dieu-thu-cuong-y";
        Console.WriteLine("--- TESTING HAIBABA DOWNFLOW ---");

        // 1. Fetch book page
        string bookHtml = "";
        using (var client = new WebClient())
        {
            client.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            bookHtml = client.DownloadString(bookUrl);
        }
        Console.WriteLine("Fetched book HTML size: " + bookHtml.Length);

        // 2. Extract chapters
        var chapterLinks = new List<string>();
        var seenChapters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = Regex.Matches(bookHtml, @"<a[^>]+href=[""'](?<href>[^""']*?/Home/ReadChapter\?[^""']+)[""'][^>]*>(?<inner>[\s\S]*?)</a>", RegexOptions.IgnoreCase);
        foreach (Match m in matches)
        {
            string rawHref = WebUtility.HtmlDecode(m.Groups["href"].Value);
            string cleanHref = rawHref;
            if (!cleanHref.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !cleanHref.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                cleanHref = "http://haibabamanga.somee.com/" + cleanHref.TrimStart('/');
            }
            if (seenChapters.Add(cleanHref))
            {
                chapterLinks.Add(cleanHref);
            }
        }
        Console.WriteLine("Extracted chapter links count: " + chapterLinks.Count);
        if (chapterLinks.Count == 0)
        {
            Console.WriteLine("FAILED to extract chapters from book detail!");
            return;
        }

        // Test with the first chapter
        string firstChapterUrl = chapterLinks[0];
        Console.WriteLine("First chapter URL (decoded): " + firstChapterUrl);

        // 3. Fetch chapter page
        string chapHtml = "";
        using (var client = new WebClient())
        {
            client.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            chapHtml = client.DownloadString(firstChapterUrl);
        }
        Console.WriteLine("Fetched chapter HTML size: " + chapHtml.Length);

        // 4. Extract images
        var imageUrls = new List<string>();
        var seenImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var imgMatches = Regex.Matches(chapHtml, @"<img[^>]+>", RegexOptions.IgnoreCase);
        foreach (Match match in imgMatches)
        {
            string tag = match.Value;
            string imageUrl = string.Empty;
            var dataSrcMatch = Regex.Match(tag, @"\bdata-src\s*=\s*[""'](?<src>[^""']+)[""']", RegexOptions.IgnoreCase);
            if (dataSrcMatch.Success)
            {
                imageUrl = dataSrcMatch.Groups["src"].Value.Trim();
            }
            else
            {
                var srcMatch = Regex.Match(tag, @"\bsrc\s*=\s*[""'](?<src>[^""']+)[""']", RegexOptions.IgnoreCase);
                if (srcMatch.Success)
                {
                    imageUrl = srcMatch.Groups["src"].Value.Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(imageUrl)) continue;
            imageUrl = WebUtility.HtmlDecode(imageUrl).Replace("\\/", "/").Trim();

            if (imageUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            if (imageUrl.IndexOf("credit", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (imageUrl.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (imageUrl.IndexOf("thumb", StringComparison.OrdinalIgnoreCase) >= 0) continue;

            if (imageUrl.IndexOf("uploads/", StringComparison.OrdinalIgnoreCase) < 0 &&
                imageUrl.IndexOf("otruyencdn", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (imageUrl.StartsWith("//", StringComparison.Ordinal))
            {
                imageUrl = "https:" + imageUrl;
            }
            else if (imageUrl.StartsWith("/", StringComparison.Ordinal))
            {
                imageUrl = "http://haibabamanga.somee.com" + imageUrl;
            }

            if (seenImages.Add(imageUrl))
            {
                imageUrls.Add(imageUrl);
            }
        }

        Console.WriteLine("Extracted images count: " + imageUrls.Count);
        if (imageUrls.Count == 0)
        {
            Console.WriteLine("FAILED to extract images!");
            return;
        }

        // 5. Download the first 3 images to scratch/test_download/
        string destFolder = @"C:\Users\Admin\source\repos\ghostminhtoan\Comic Downloader GMTPC\bin\release\root\haibabamanga.somee.com\Diệu Thủ Cuồng Y\Chương 1";
        Directory.CreateDirectory(destFolder);
        Console.WriteLine("Downloading first 3 images to: " + destFolder);
        int toDownload = Math.Min(3, imageUrls.Count);
        for (int i = 0; i < toDownload; i++)
        {
            string imgUrl = imageUrls[i];
            string filename = Path.Combine(destFolder, string.Format("{0:D4}.jpg", i + 1));
            Console.WriteLine("Downloading: " + imgUrl);
            using (var client = new WebClient())
            {
                client.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                client.DownloadFile(imgUrl, filename);
            }
            Console.WriteLine("Saved to: " + filename);
        }

        Console.WriteLine("SUCCESS!");
    }
}

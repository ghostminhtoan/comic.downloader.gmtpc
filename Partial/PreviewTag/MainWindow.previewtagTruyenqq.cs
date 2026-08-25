using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private void ExtractAndApplyTruyenqqPreviewTags(GalleryItem item, string html)
        {
            try
            {
                var truyenqqTags = new List<string>();
                // Lấy ul.list01 trực tiếp từ HTML để tránh lỗi phân cấp thẻ div của book_other
                Match list01Match = Regex.Match(html, @"<ul[^>]*class=[""'][\s\S]*?\blist01\b[\s\S]*?[""'][^>]*>(?<content>[\s\S]*?)</ul>", RegexOptions.IgnoreCase);
                if (list01Match.Success)
                {
                    string listContent = list01Match.Groups["content"].Value;
                    foreach (Match liMatch in Regex.Matches(listContent, @"<a[^>]*href=[""'][^""']*the-loai/[^""']+[""'][^>]*>(?<tag>[^<]+)</a>", RegexOptions.IgnoreCase))
                    {
                        string tagText = WebUtility.HtmlDecode(liMatch.Groups["tag"].Value).Trim();
                        if (!string.IsNullOrWhiteSpace(tagText))
                        {
                            truyenqqTags.Add(tagText);
                        }
                    }
                }

                if (truyenqqTags.Count > 0)
                {
                    var jArr = new Newtonsoft.Json.Linq.JArray();
                    foreach (var tag in truyenqqTags)
                    {
                        var tObj = new Newtonsoft.Json.Linq.JObject();
                        tObj["tag"] = tag;
                        jArr.Add(tObj);
                    }
                    var jTagsObj = new Newtonsoft.Json.Linq.JObject();
                    jTagsObj["tags"] = jArr;

                    Dispatcher.Invoke(() =>
                    {
                        item.Tag = jTagsObj;
                        RecalculateDuplicates();
                    });
                }
            }
            catch (Exception ex)
            {
                Log($"[Truyenqq Preview Tags] Lỗi cập nhật tags: {ex.Message}");
            }
        }
    }
}

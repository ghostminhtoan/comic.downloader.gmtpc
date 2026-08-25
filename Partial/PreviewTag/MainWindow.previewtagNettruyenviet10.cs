using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private void ExtractAndApplyNettruyenviet10PreviewTags(GalleryItem item, string html)
        {
            try
            {
                var tags = new List<string>();
                Match colInfoMatch = Regex.Match(html, @"<div[^>]*class=[""'][\s\S]*?\bcol-info\b[\s\S]*?[""'][^>]*>(?<content>[\s\S]*?)</div>", RegexOptions.IgnoreCase);
                if (colInfoMatch.Success)
                {
                    string colInfoContent = colInfoMatch.Groups["content"].Value;
                    foreach (Match colXs8Match in Regex.Matches(colInfoContent, @"<p[^>]*class=[""'][\s\S]*?\bcol-xs-8\b[\s\S]*?[""'][^>]*>(?<content>[\s\S]*?)</p>", RegexOptions.IgnoreCase))
                    {
                        string pContent = colXs8Match.Groups["content"].Value;
                        if (pContent.IndexOf("tim-truyen", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            foreach (Match aMatch in Regex.Matches(pContent, @"<a[^>]*href=[""'][^""']*tim-truyen/[^""']+[""'][^>]*>(?<tag>[^<]+)</a>", RegexOptions.IgnoreCase))
                            {
                                string tagText = WebUtility.HtmlDecode(aMatch.Groups["tag"].Value).Trim();
                                if (!string.IsNullOrWhiteSpace(tagText))
                                {
                                    tags.Add(tagText);
                                }
                            }
                        }
                    }
                }

                if (tags.Count > 0)
                {
                    var jArr = new Newtonsoft.Json.Linq.JArray();
                    foreach (var tag in tags)
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
                Log($"[Nettruyenviet10 Preview Tags] Lỗi cập nhật tags: {ex.Message}");
            }
        }
    }
}

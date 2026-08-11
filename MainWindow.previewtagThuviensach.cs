using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private void ExtractAndApplyThuviensachPreviewTags(GalleryItem item, string html)
        {
            try
            {
                var tags = new List<string>();
                // Tìm block fieldset id="pdf"
                Match fieldsetMatch = Regex.Match(html, @"id=""pdf""[\s\S]*?</fieldset>", RegexOptions.IgnoreCase);
                if (fieldsetMatch.Success)
                {
                    string fieldsetBlock = fieldsetMatch.Value;
                    // Tìm tất cả thẻ a class="button2"
                    foreach (Match aMatch in Regex.Matches(fieldsetBlock, @"class=""button2""[^>]*>(?<tag>[^<]+)</a>", RegexOptions.IgnoreCase))
                    {
                        string tagText = WebUtility.HtmlDecode(aMatch.Groups["tag"].Value).Trim();
                        if (!string.IsNullOrWhiteSpace(tagText))
                        {
                            tags.Add(tagText);
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
                Log($"[Thuviensach Preview Tags] Lỗi cập nhật tags: {ex.Message}");
            }
        }
    }
}

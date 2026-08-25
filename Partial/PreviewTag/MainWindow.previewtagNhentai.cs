using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Linq;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private void ExtractAndApplyNhentaiPreviewTags(GalleryItem item, string html)
        {
            try
            {
                var langs = ExtractNhentaiNetLanguages(html);
                var displayLangs = langs.Where(l => l != "translated").ToList();
                string currentName = CleanTranslatedTagFromTitle(item.Name);

                if (displayLangs.Count > 0)
                {
                    string langStr = string.Join(", ", displayLangs.Select(l => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(l)));
                    string suffix = $"[{langStr}]";
                    if (!currentName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        currentName = $"{currentName} {suffix}";
                    }
                }

                var nhentaiTags = ExtractNhentaiNetTags(html);
                Newtonsoft.Json.Linq.JObject jTagsObj = null;
                if (nhentaiTags.Count > 0)
                {
                    var jArr = new Newtonsoft.Json.Linq.JArray();
                    foreach (var tag in nhentaiTags)
                    {
                        var tObj = new Newtonsoft.Json.Linq.JObject();
                        tObj["tag"] = tag;
                        jArr.Add(tObj);
                    }
                    jTagsObj = new Newtonsoft.Json.Linq.JObject();
                    jTagsObj["tags"] = jArr;
                }

                Dispatcher.Invoke(() =>
                {
                    if (item.Name != currentName)
                    {
                        item.Name = currentName;
                    }
                    if (jTagsObj != null)
                    {
                        item.Tag = jTagsObj;
                        RecalculateDuplicates();
                    }
                });
            }
            catch (Exception ex)
            {
                Log($"[nhentai Preview Tags] Lỗi cập nhật tên, ngôn ngữ và tags: {ex.Message}");
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private void ExtractAndApplyEHentaiPreviewTags(GalleryItem item, string html)
        {
            try
            {
                if (item == null || string.IsNullOrWhiteSpace(html)) return;

                // Match whole #gd4 or #taglist block
                var gd4Match = Regex.Match(html, @"<div[^>]*id=""gd4""[^>]*>(?<content>.*?)</div>\s*<div[^>]*id=""gd5""", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                string blockHtml = gd4Match.Success ? gd4Match.Groups["content"].Value : html;

                var jTagsObj = new Newtonsoft.Json.Linq.JObject();
                var aggregatedTagsArray = new Newtonsoft.Json.Linq.JArray();
                var allTagsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Parse table rows: <tr ...><td class="tc">group:</td><td><div ...><a ...>tag</a></div>...</td></tr>
                var rowMatches = Regex.Matches(blockHtml, @"<tr[^>]*>\s*<td[^>]*class=""tc""[^>]*>(?<prefix>[^<:]*):?</td>\s*<td>(?<tags>.*?)</td>\s*</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                foreach (Match row in rowMatches)
                {
                    string prefix = row.Groups["prefix"].Value.Trim().ToLowerInvariant();
                    string tagsContent = row.Groups["tags"].Value;
                    if (string.IsNullOrWhiteSpace(prefix)) continue;

                    var aMatches = Regex.Matches(tagsContent, @"<a[^>]*>(?<tag>[^<]+)</a>", RegexOptions.IgnoreCase);
                    var groupList = new List<string>();

                    foreach (Match a in aMatches)
                    {
                        string rawTag = WebUtility.HtmlDecode(a.Groups["tag"].Value).Trim();
                        if (string.IsNullOrWhiteSpace(rawTag)) continue;

                        if (rawTag.Contains("|"))
                        {
                            rawTag = rawTag.Split('|')[0].Trim();
                        }

                        if (!groupList.Any(x => x.Equals(rawTag, StringComparison.OrdinalIgnoreCase)))
                        {
                            groupList.Add(rawTag);
                        }

                        string qualifiedTag = prefix == "other" || prefix == "misc" ? rawTag : $"{prefix}:{rawTag}";
                        if (!allTagsSet.Contains(qualifiedTag))
                        {
                            allTagsSet.Add(qualifiedTag);

                            var tObj = new Newtonsoft.Json.Linq.JObject();
                            tObj["tag"] = qualifiedTag;
                            if (prefix == "female") tObj["female"] = "1";
                            else if (prefix == "male") tObj["male"] = "1";
                            aggregatedTagsArray.Add(tObj);
                        }
                    }

                    if (groupList.Count > 0)
                    {
                        var groupArr = new Newtonsoft.Json.Linq.JArray();
                        foreach (var g in groupList) groupArr.Add(g);
                        jTagsObj[prefix] = groupArr;
                    }
                }

                // Fallback: search general /tag/ links if table parsing found nothing
                if (aggregatedTagsArray.Count == 0)
                {
                    var fallbackMatches = Regex.Matches(blockHtml, @"/tag/([a-zA-Z0-9%_:+ -]+)[""']", RegexOptions.IgnoreCase);
                    foreach (Match m in fallbackMatches)
                    {
                        string raw = Uri.UnescapeDataString(m.Groups[1].Value).Replace("+", " ").Trim();
                        if (!string.IsNullOrWhiteSpace(raw) && !allTagsSet.Contains(raw))
                        {
                            allTagsSet.Add(raw);
                            var tObj = new Newtonsoft.Json.Linq.JObject();
                            tObj["tag"] = raw;
                            aggregatedTagsArray.Add(tObj);
                        }
                    }
                }

                if (aggregatedTagsArray.Count > 0 || jTagsObj.Count > 0)
                {
                    jTagsObj["tags"] = aggregatedTagsArray;

                    // Extract and normalize language to English (same as hitomi.la)
                    string detectedLanguage = null;
                    if (jTagsObj["language"] is Newtonsoft.Json.Linq.JArray langArr && langArr.Count > 0)
                    {
                        foreach (var l in langArr)
                        {
                            string rawL = l?.ToString()?.Trim();
                            if (string.IsNullOrWhiteSpace(rawL)) continue;
                            if (rawL.Equals("translated", StringComparison.OrdinalIgnoreCase) ||
                                rawL.Equals("rewrite", StringComparison.OrdinalIgnoreCase) ||
                                rawL.Equals("speechless", StringComparison.OrdinalIgnoreCase) ||
                                rawL.Equals("textless", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            // Standard English mapping
                            if (rawL.Equals("japanese", StringComparison.OrdinalIgnoreCase) || rawL.Equals("日本語", StringComparison.OrdinalIgnoreCase)) detectedLanguage = "Japanese";
                            else if (rawL.Equals("chinese", StringComparison.OrdinalIgnoreCase) || rawL.Equals("中文", StringComparison.OrdinalIgnoreCase)) detectedLanguage = "Chinese";
                            else if (rawL.Equals("korean", StringComparison.OrdinalIgnoreCase) || rawL.Equals("한국어", StringComparison.OrdinalIgnoreCase)) detectedLanguage = "Korean";
                            else if (rawL.Equals("english", StringComparison.OrdinalIgnoreCase)) detectedLanguage = "English";
                            else if (rawL.Equals("spanish", StringComparison.OrdinalIgnoreCase) || rawL.Equals("español", StringComparison.OrdinalIgnoreCase)) detectedLanguage = "Spanish";
                            else if (rawL.Equals("french", StringComparison.OrdinalIgnoreCase) || rawL.Equals("français", StringComparison.OrdinalIgnoreCase)) detectedLanguage = "French";
                            else if (rawL.Equals("german", StringComparison.OrdinalIgnoreCase) || rawL.Equals("deutsch", StringComparison.OrdinalIgnoreCase)) detectedLanguage = "German";
                            else if (rawL.Equals("russian", StringComparison.OrdinalIgnoreCase) || rawL.Equals("русский", StringComparison.OrdinalIgnoreCase)) detectedLanguage = "Russian";
                            else if (rawL.Equals("vietnamese", StringComparison.OrdinalIgnoreCase) || rawL.Equals("tiếng việt", StringComparison.OrdinalIgnoreCase)) detectedLanguage = "Vietnamese";
                            else if (rawL.Equals("italian", StringComparison.OrdinalIgnoreCase) || rawL.Equals("italiano", StringComparison.OrdinalIgnoreCase)) detectedLanguage = "Italian";
                            else if (rawL.Equals("portuguese", StringComparison.OrdinalIgnoreCase) || rawL.Equals("português", StringComparison.OrdinalIgnoreCase)) detectedLanguage = "Portuguese";
                            else if (rawL.Equals("thai", StringComparison.OrdinalIgnoreCase) || rawL.Equals("ไทย", StringComparison.OrdinalIgnoreCase)) detectedLanguage = "Thai";
                            else if (rawL.Equals("indonesian", StringComparison.OrdinalIgnoreCase)) detectedLanguage = "Indonesian";
                            else
                            {
                                detectedLanguage = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(rawL);
                            }
                            break;
                        }
                    }

                    Dispatcher.Invoke(() =>
                    {
                        item.Tag = jTagsObj;

                        // Ensure book name has correct [Language] tag if language was detected
                        if (!string.IsNullOrWhiteSpace(detectedLanguage) && !string.IsNullOrWhiteSpace(item.Name))
                        {
                            string curName = item.Name.Trim();
                            var langCheckRegex = new Regex(@"\[(english|japanese|korean|chinese|vietnamese|french|spanish|german|russian|italian|portuguese|thai|indonesian|日本語|中文|한국어)\]", RegexOptions.IgnoreCase);
                            if (langCheckRegex.IsMatch(curName))
                            {
                                // Replace existing language tag if different
                                string updatedName = langCheckRegex.Replace(curName, $"[{detectedLanguage}]");
                                if (updatedName != curName)
                                {
                                    item.Name = updatedName;
                                }
                            }
                            else
                            {
                                item.Name = $"{curName} [{detectedLanguage}]";
                            }
                        }

                        RecalculateDuplicates();
                    });
                }
            }
            catch (Exception ex)
            {
                EHentaiLog($"[E-Hentai Preview Tags] Lỗi phân tích tags: {ex.Message}");
            }
        }
    }
}

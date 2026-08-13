using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace get_link_manga
{
    public class HtmlElementFingerprint
    {
        public string TagName { get; set; }
        public string Id { get; set; }
        public HashSet<string> Classes { get; set; }
        public Dictionary<string, string> Attributes { get; set; }
        public string TextContent { get; set; }
        public int TextLength { get; set; }

        public HtmlElementFingerprint()
        {
            Classes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public static HtmlElementFingerprint Create(string tagName, string id, string classes, string text, Dictionary<string, string> attributes = null)
        {
            var fp = new HtmlElementFingerprint();
            fp.TagName = tagName?.Trim();
            fp.Id = id?.Trim();
            fp.TextContent = text?.Trim() ?? string.Empty;
            fp.TextLength = (text?.Trim() ?? string.Empty).Length;

            if (!string.IsNullOrEmpty(classes))
            {
                var classList = classes.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var c in classList)
                {
                    fp.Classes.Add(c);
                }
            }

            if (attributes != null)
            {
                foreach (var kvp in attributes)
                {
                    fp.Attributes[kvp.Key] = kvp.Value;
                }
            }

            return fp;
        }
    }

    public class HtmlMinNode
    {
        public string TagName { get; set; }
        public string Id { get; set; }
        public HashSet<string> Classes { get; set; }
        public Dictionary<string, string> Attributes { get; set; }
        public string TextContent { get; set; }
        public string OuterHtml { get; set; }

        public HtmlMinNode()
        {
            Classes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            TextContent = string.Empty;
            OuterHtml = string.Empty;
        }

        public double CalculateSimilarity(HtmlElementFingerprint target)
        {
            double score = 0;

            // 1. Tag name match (Max 15)
            if (string.Equals(TagName, target.TagName, StringComparison.OrdinalIgnoreCase))
            {
                score += 15;
            }

            // 2. ID match (Max 30)
            if (!string.IsNullOrEmpty(target.Id) && !string.IsNullOrEmpty(Id))
            {
                if (string.Equals(Id, target.Id, StringComparison.OrdinalIgnoreCase))
                {
                    score += 30;
                }
            }

            // 3. Classes similarity (Jaccard Index) (Max 25)
            if (target.Classes.Count > 0 && Classes.Count > 0)
            {
                int intersection = Classes.Intersect(target.Classes, StringComparer.OrdinalIgnoreCase).Count();
                int union = Classes.Union(target.Classes, StringComparer.OrdinalIgnoreCase).Count();
                if (union > 0)
                {
                    score += 25.0 * ((double)intersection / union);
                }
            }

            // 4. Attributes overlap (Max 20)
            if (target.Attributes.Count > 0)
            {
                int matchedAttrCount = 0;
                foreach (var attr in target.Attributes)
                {
                    if (Attributes.TryGetValue(attr.Key, out string val))
                    {
                        if (string.Equals(val, attr.Value, StringComparison.OrdinalIgnoreCase))
                        {
                            matchedAttrCount++;
                        }
                        else if (val.Contains(attr.Value) || attr.Value.Contains(val))
                        {
                            matchedAttrCount++; // Partially matched attributes
                        }
                    }
                }
                score += 20.0 * ((double)matchedAttrCount / target.Attributes.Count);
            }

            // 5. Text similarity (Max 10)
            if (!string.IsNullOrEmpty(target.TextContent) && !string.IsNullOrEmpty(TextContent))
            {
                if (string.Equals(TextContent, target.TextContent, StringComparison.OrdinalIgnoreCase))
                {
                    score += 10;
                }
                else if (TextContent.Contains(target.TextContent) || target.TextContent.Contains(TextContent))
                {
                    score += 5;
                }
            }

            return score;
        }
    }

    public static class AdaptiveHtmlTracker
    {
        private static readonly Regex TagRegex = new Regex(@"<(?<tag>[a-zA-Z0-9]+)(?<attrs>[^>]*?)(?:/?>|(?<=/)>)", RegexOptions.Compiled);
        private static readonly Regex AttrRegex = new Regex(@"(?<name>[a-zA-Z0-9\-]+)\s*=\s*(?:""(?<val>[^""]*)""|'(?<val>[^']*)'|(?<val>[^\s>]+))", RegexOptions.Compiled);

        public static List<HtmlMinNode> ParseElements(string html)
        {
            var nodes = new List<HtmlMinNode>();
            if (string.IsNullOrEmpty(html)) return nodes;

            var matches = TagRegex.Matches(html);
            foreach (Match m in matches)
            {
                var node = new HtmlMinNode();
                node.TagName = m.Groups["tag"].Value;
                node.OuterHtml = m.Value;

                string attrsStr = m.Groups["attrs"].Value;
                if (!string.IsNullOrWhiteSpace(attrsStr))
                {
                    var attrMatches = AttrRegex.Matches(attrsStr);
                    foreach (Match am in attrMatches)
                    {
                        string name = am.Groups["name"].Value;
                        string val = am.Groups["val"].Value;
                        node.Attributes[name] = val;

                        if (string.Equals(name, "id", StringComparison.OrdinalIgnoreCase))
                        {
                            node.Id = val;
                        }
                        else if (string.Equals(name, "class", StringComparison.OrdinalIgnoreCase))
                        {
                            var classes = val.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var c in classes)
                            {
                                node.Classes.Add(c);
                            }
                        }
                    }
                }

                // Locate basic inner text if it exists (very simple extraction)
                int startIndex = m.Index + m.Length;
                int endIndex = html.IndexOf('<', startIndex);
                if (endIndex > startIndex)
                {
                    string innerText = html.Substring(startIndex, endIndex - startIndex);
                    node.TextContent = WebUtilityDecode(innerText).Trim();
                }

                nodes.Add(node);
            }

            return nodes;
        }

        private static string WebUtilityDecode(string encoded)
        {
            if (string.IsNullOrEmpty(encoded)) return string.Empty;
            // Basic HTML decoding to avoid dependency on System.Web
            return encoded
                .Replace("&amp;", "&")
                .Replace("&lt;", "<")
                .Replace("&gt;", ">")
                .Replace("&quot;", "\"")
                .Replace("&apos;", "'")
                .Replace("&#39;", "'");
        }

        public static HtmlMinNode FindBestMatch(string html, HtmlElementFingerprint targetFingerprint, double minScore = 40.0)
        {
            var nodes = ParseElements(html);
            HtmlMinNode bestNode = null;
            double bestScore = -1;

            foreach (var node in nodes)
            {
                double score = node.CalculateSimilarity(targetFingerprint);
                if (score >= minScore && score > bestScore)
                {
                    bestScore = score;
                    bestNode = node;
                }
            }

            return bestNode;
        }
    }
}

using System;
using System.Collections.Generic;
using get_link_manga;

namespace TestNamespace
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== Testing AdaptiveHtmlTracker ===");

            // 1. Define fingerprint of the original element we want to track
            var attributes = new Dictionary<string, string>();
            attributes["href"] = "https://example.com/chapter-1";
            
            var fingerprint = HtmlElementFingerprint.Create("a", "chapter_1", "chapter-link active", "Chapter 1", attributes);

            // 2. Simulate page update: class name is obfuscated, ID changed slightly, but href and tag remain similar.
            string updatedHtml = " <div class='container'> " +
                                 "   <span class='title'>Manga Title</span> " +
                                 "   <a href='https://example.com/chapter-1' class='xyz-obfuscated-link active' id='chap_1'>Chapter 1</a> " +
                                 "   <a href='https://example.com/chapter-2' class='xyz-obfuscated-link' id='chap_2'>Chapter 2</a> " +
                                 " </div>";

            Console.WriteLine("Target fingerprint: Tag=a, Id=chapter_1, Classes=chapter-link active, Text=Chapter 1");
            Console.WriteLine("Looking in updated HTML...");

            var match = AdaptiveHtmlTracker.FindBestMatch(updatedHtml, fingerprint, 40.0);

            if (match != null)
            {
                Console.WriteLine("MATCH FOUND!");
                Console.WriteLine("Tag: " + match.TagName);
                Console.WriteLine("ID: " + match.Id);
                Console.WriteLine("Classes: " + string.Join(" ", match.Classes));
                Console.WriteLine("Href: " + (match.Attributes.ContainsKey("href") ? match.Attributes["href"] : "null"));
                Console.WriteLine("Text: " + match.TextContent);
                Console.WriteLine("=== Test Passed! ===");
            }
            else
            {
                Console.WriteLine("FAILED: No match found.");
            }
        }
    }
}

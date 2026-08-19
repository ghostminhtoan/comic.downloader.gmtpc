using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace get_link_manga
{
    public static class TextBlockLanguageColorizer
    {
        public static readonly DependencyProperty HighlightedTextProperty =
            DependencyProperty.RegisterAttached(
                "HighlightedText",
                typeof(string),
                typeof(TextBlockLanguageColorizer),
                new PropertyMetadata(null, OnHighlightedTextChanged));

        public static string GetHighlightedText(DependencyObject obj)
        {
            return (string)obj.GetValue(HighlightedTextProperty);
        }

        public static void SetHighlightedText(DependencyObject obj, string value)
        {
            obj.SetValue(HighlightedTextProperty, value);
        }

        private static void OnHighlightedTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBlock textBlock)
            {
                textBlock.Inlines.Clear();
                string text = e.NewValue as string;
                if (string.IsNullOrEmpty(text)) return;

                // Regex khớp với các tag ngôn ngữ như [English], [Japanese], [Korean], [Chinese], [日本語], [中文], [한국어], [Italiano], [French], v.v.
                // Khớp bất kỳ từ nào viết trong dấu ngoặc vuông chứa ký tự chữ/số, kể cả ký tự unicode có dấu và ký tự Cyrillic, Greek, Hebrew, Arabic, Burmese, Thai...
                var regex = new Regex(@"(\[[a-zA-Z0-9\s\u3040-\u30ff\u3400-\u4dbf\u4e00-\u9fff\uf900-\ufaff\uff66-\uff9f\uac00-\ud7af\u1100-\u11ff\u3130-\u318f\u00C0-\u00FF\u0100-\u017F\u0400-\u04FF\u0370-\u03FF\u0590-\u05FF\u0600-\u06FF\u0e00-\u0e7f\u1000-\u109f*]+\])", RegexOptions.IgnoreCase);
                
                var matches = regex.Matches(text);
                int lastIndex = 0;
                
                // Tập hợp các ngôn ngữ cần highlight (để tránh highlight các tag không phải ngôn ngữ)
                var langKeywords = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    // Bản dịch tiếng Anh và ngôn ngữ gốc từ Hitomi.la
                    "english", "japanese", "korean", "chinese", "日本語", "中文", "한국어", 
                    "vietnamese", "tiếng việt", "french", "français", "spanish", "español", 
                    "portuguese", "português", "german", "deutsch", "italian", "italiano", 
                    "russian", "русский", "swedish", "svenska", "turkish", "türkçe", 
                    "polish", "polski", "dutch", "nederlands", "thai", "ไทย", "indonesian", 
                    "bahasa indonesia", "tagalog", "basa jawa", "javanese", "catalan", "català", 
                    "cebuano", "czech", "čeština", "danish", "dansk", "estonian", "eesti", 
                    "esperanto", "hindi", "íslenska", "icelandic", "khmer", "latin", "latina", 
                    "hungarian", "magyar", "norwegian", "norsk", "romanian", "română", 
                    "albanian", "shqip", "slovak", "slovenčina", "serbian", "srpski", 
                    "finnish", "suomi", "textless", "narrative", "greek", "ελληνικά", 
                    "bulgarian", "български", "mongolian", "монгол", "ukrainian", "українська", 
                    "hebrew", "עברית", "arabic", "العربية", "persian", "farsi", "فارسی", 
                    "burmese", "myanmar", "မြန်မာဘာသာ", "japanese*"
                };

                bool anyAdded = false;
                foreach (Match match in matches)
                {
                    string innerVal = match.Value.Trim('[', ']');
                    if (!langKeywords.Contains(innerVal))
                    {
                        continue; // Không phải tag ngôn ngữ cần highlight
                    }

                    // Add text before match
                    if (match.Index > lastIndex)
                    {
                        textBlock.Inlines.Add(new Run(text.Substring(lastIndex, match.Index - lastIndex)));
                        anyAdded = true;
                    }

                    // Add highlighted language run
                    var run = new Run(match.Value);
                    if (innerVal.Equals("japanese*", StringComparison.OrdinalIgnoreCase))
                    {
                        run.Foreground = new SolidColorBrush(Color.FromRgb(255, 234, 0)); // Màu vàng Cyberpunk đặc biệt cho japanese tự gán
                    }
                    else
                    {
                        run.Foreground = new SolidColorBrush(Color.FromRgb(255, 0, 255)); // Cyberpunk Magenta mặc định
                    }
                    run.FontWeight = FontWeights.Bold;
                    textBlock.Inlines.Add(run);
                    anyAdded = true;

                    lastIndex = match.Index + match.Length;
                }

                if (lastIndex < text.Length)
                {
                    textBlock.Inlines.Add(new Run(text.Substring(lastIndex)));
                    anyAdded = true;
                }

                if (!anyAdded && !string.IsNullOrEmpty(text))
                {
                    textBlock.Inlines.Add(new Run(text));
                }
            }
        }
    }
}

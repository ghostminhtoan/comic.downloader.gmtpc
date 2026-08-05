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
                // Khớp bất kỳ từ nào viết trong dấu ngoặc vuông chứa ký tự chữ/số
                var regex = new Regex(@"(\[[a-zA-Z\u3040-\u30ff\u3400-\u4dbf\u4e00-\u9fff\uf900-\ufaff\uff66-\uff9f\uac00-\ud7af\u1100-\u11ff\u3130-\u318f]+\])", RegexOptions.IgnoreCase);
                
                var matches = regex.Matches(text);
                int lastIndex = 0;
                
                // Tập hợp các ngôn ngữ cần highlight (để tránh highlight các tag không phải ngôn ngữ)
                var langKeywords = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "english", "japanese", "korean", "chinese", "日本語", "中文", "한국어", "italiano", "french", "spanish", "russian", "portuguese", "thai", "vietnamese", "tiếng việt"
                };

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
                    }

                    // Add highlighted language run
                    var run = new Run(match.Value);
                    run.Foreground = new SolidColorBrush(Color.FromRgb(255, 0, 255)); // Cyberpunk Magenta làm màu nổi bật
                    run.FontWeight = FontWeights.Bold;
                    textBlock.Inlines.Add(run);

                    lastIndex = match.Index + match.Length;
                }

                if (lastIndex < text.Length)
                {
                    textBlock.Inlines.Add(new Run(text.Substring(lastIndex)));
                }
            }
        }
    }
}

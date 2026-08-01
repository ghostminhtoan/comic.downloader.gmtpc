using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace get_link_manga
{
    public class ChapterFilter
    {
        public List<Tuple<double, double>> Ranges { get; } = new List<Tuple<double, double>>();
        public HashSet<double> Singles { get; } = new HashSet<double>();

        public bool IsMatch(double chapNum)
        {
            foreach (var single in Singles)
            {
                if (Math.Abs(chapNum - single) < 0.0001)
                    return true;
            }

            foreach (var range in Ranges)
            {
                if (chapNum >= range.Item1 - 0.0001 && chapNum <= range.Item2 + 0.0001)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Utility class để parse cú pháp chọn chapter kiểu máy in hỗ trợ số thực.
    /// Ví dụ: "324-328;324.5"
    /// </summary>
    public static class ChapterRangeParser
    {
        public static ChapterFilter Parse(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null; // Tải tất cả

            var filter = new ChapterFilter();
            string cleaned = input.Trim();

            string[] segments = cleaned.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string segment in segments)
            {
                string trimmed = segment.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                if (trimmed.Contains("-"))
                {
                    string[] parts = trimmed.Split(new[] { '-' }, 2);
                    if (parts.Length != 2)
                        throw new FormatException($"Cú pháp range không hợp lệ: '{trimmed}'. Dùng format: 324-328");

                    if (!double.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double start))
                        throw new FormatException($"Số bắt đầu không hợp lệ trong range: '{trimmed}'");

                    if (!double.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double end))
                        throw new FormatException($"Số kết thúc không hợp lệ trong range: '{trimmed}'");

                    if (start > end)
                        throw new FormatException($"Số bắt đầu ({start}) lớn hơn số kết thúc ({end}) trong range: '{trimmed}'");

                    filter.Ranges.Add(new Tuple<double, double>(start, end));
                }
                else
                {
                    if (!double.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out double num))
                        throw new FormatException($"Số không hợp lệ: '{trimmed}'");

                    filter.Singles.Add(num);
                }
            }

            return (filter.Ranges.Count > 0 || filter.Singles.Count > 0) ? filter : null;
        }

        public static bool TryParse(string input, out ChapterFilter result, out string errorMessage)
        {
            try
            {
                result = Parse(input);
                errorMessage = null;
                return true;
            }
            catch (FormatException ex)
            {
                result = null;
                errorMessage = ex.Message;
                return false;
            }
        }

        public static string ToDisplayString(ChapterFilter filter)
        {
            if (filter == null || (filter.Ranges.Count == 0 && filter.Singles.Count == 0))
                return "Tất cả";

            var parts = new List<string>();
            foreach (var r in filter.Ranges)
            {
                parts.Add($"{r.Item1.ToString(CultureInfo.InvariantCulture)}-{r.Item2.ToString(CultureInfo.InvariantCulture)}");
            }
            foreach (var s in filter.Singles)
            {
                parts.Add(s.ToString(CultureInfo.InvariantCulture));
            }
            return string.Join(", ", parts);
        }
    }
}

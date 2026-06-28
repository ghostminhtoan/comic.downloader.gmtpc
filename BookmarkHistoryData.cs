using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace get_link_manga
{
    [DataContract]
    public class BookmarkEntry
    {
        [DataMember] public string Name { get; set; }
        [DataMember] public string Url { get; set; }
        [DataMember] public string SourceDomain { get; set; }
        [DataMember] public DateTime BookmarkedAt { get; set; }
        [DataMember] public string Notes { get; set; }
    }

    [DataContract]
    public class ReaderPageBookmarkEntry
    {
        [DataMember] public string BookmarkId { get; set; }
        [DataMember] public string MangaName { get; set; }
        [DataMember] public string ChapterName { get; set; }
        [DataMember] public string PageName { get; set; }
        [DataMember] public string SourceDomain { get; set; }
        [DataMember] public DateTime BookmarkedAt { get; set; }
        [DataMember] public string LibraryRoot { get; set; }
        [DataMember] public string SeriesFolderPath { get; set; }
        [DataMember] public string MangaFolderPath { get; set; }
        [DataMember] public string ChapterFolderPath { get; set; }
        [DataMember] public string PageFilePath { get; set; }
        [DataMember] public int PageIndex { get; set; }
        [DataMember] public string FitModeKey { get; set; }
        [DataMember] public double ReaderZoom { get; set; }
        [DataMember] public int ViewportPageIndex { get; set; }
        [DataMember] public double ViewportPageXRatio { get; set; }
        [DataMember] public double ViewportPageYRatio { get; set; }
    }

    [DataContract]
    public class HistoryEntry
    {
        [DataMember] public string Name { get; set; }
        [DataMember] public string Url { get; set; }
        [DataMember] public string SourceDomain { get; set; }
        [DataMember] public DateTime DownloadedAt { get; set; }
        [DataMember] public int ChaptersDownloaded { get; set; }
        [DataMember] public string DownloadPath { get; set; }
    }

    [DataContract]
    public class BookmarkData
    {
        [DataMember] public List<BookmarkEntry> Bookmarks { get; set; } = new List<BookmarkEntry>();
    }

    [DataContract]
    public class ReaderPageBookmarkData
    {
        [DataMember] public List<ReaderPageBookmarkEntry> Bookmarks { get; set; } = new List<ReaderPageBookmarkEntry>();
    }

    [DataContract]
    public class HistoryData
    {
        [DataMember] public List<HistoryEntry> Entries { get; set; } = new List<HistoryEntry>();
    }

    public class BookmarkHistoryManager
    {
        private readonly string _dataDir;
        private readonly string _bookmarksPath;
        private readonly string _readerPageBookmarksPath;
        private readonly string _legacyReaderPageBookmarksBinaryPath;
        private readonly string _legacyReaderPageBookmarksJsonPath;
        private readonly string _historyPath;

        public BookmarkHistoryManager()
        {
            _dataDir = AppDomain.CurrentDomain.BaseDirectory;
            _bookmarksPath = Path.Combine(_dataDir, "bookmarks.json");
            _readerPageBookmarksPath = Path.Combine(_dataDir, "reader-page-bookmarks.md");
            _legacyReaderPageBookmarksBinaryPath = Path.Combine(_dataDir, "reader-page-bookmarks.gmrb");
            _legacyReaderPageBookmarksJsonPath = Path.Combine(_dataDir, "reader-page-bookmarks.json");
            _historyPath = Path.Combine(_dataDir, "history.json");

            if (!Directory.Exists(_dataDir))
                Directory.CreateDirectory(_dataDir);
        }

        // ===== BOOKMARKS =====

        public List<BookmarkEntry> GetBookmarks()
        {
            var data = LoadJson<BookmarkData>(_bookmarksPath);
            return data?.Bookmarks ?? new List<BookmarkEntry>();
        }

        public void AddBookmark(BookmarkEntry entry)
        {
            var data = LoadJson<BookmarkData>(_bookmarksPath) ?? new BookmarkData();
            // Prevent duplicates by URL
            if (!data.Bookmarks.Any(b => b.Url.Equals(entry.Url, StringComparison.OrdinalIgnoreCase)))
            {
                data.Bookmarks.Insert(0, entry); // Newest first
                SaveJson(_bookmarksPath, data);
            }
        }

        public void RemoveBookmark(string url)
        {
            var data = LoadJson<BookmarkData>(_bookmarksPath);
            if (data == null) return;
            data.Bookmarks.RemoveAll(b => b.Url.Equals(url, StringComparison.OrdinalIgnoreCase));
            SaveJson(_bookmarksPath, data);
        }

        public bool IsBookmarked(string url)
        {
            var bookmarks = GetBookmarks();
            return bookmarks.Any(b => b.Url.Equals(url, StringComparison.OrdinalIgnoreCase));
        }

        // ===== READER PAGE BOOKMARKS =====

        public List<ReaderPageBookmarkEntry> GetReaderPageBookmarks()
        {
            return LoadReaderPageBookmarks();
        }

        public void AddReaderPageBookmark(ReaderPageBookmarkEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.PageFilePath))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(entry.BookmarkId))
            {
                entry.BookmarkId = Guid.NewGuid().ToString("N");
            }

            var bookmarks = LoadReaderPageBookmarks();
            bookmarks.RemoveAll(b => string.Equals(b.PageFilePath, entry.PageFilePath, StringComparison.OrdinalIgnoreCase));
            bookmarks.Insert(0, entry);
            SaveReaderPageBookmarks(bookmarks);
        }

        public void RemoveReaderPageBookmark(string bookmarkId)
        {
            if (string.IsNullOrWhiteSpace(bookmarkId))
            {
                return;
            }

            var bookmarks = LoadReaderPageBookmarks();
            bookmarks.RemoveAll(b => string.Equals(b.BookmarkId, bookmarkId, StringComparison.OrdinalIgnoreCase));
            SaveReaderPageBookmarks(bookmarks);
        }

        public void ClearReaderPageBookmarks()
        {
            SaveReaderPageBookmarks(new List<ReaderPageBookmarkEntry>());
        }

        // ===== HISTORY =====

        public List<HistoryEntry> GetHistory()
        {
            var data = LoadJson<HistoryData>(_historyPath);
            return data?.Entries ?? new List<HistoryEntry>();
        }

        public void AddHistory(HistoryEntry entry)
        {
            var data = LoadJson<HistoryData>(_historyPath) ?? new HistoryData();
            data.Entries.Insert(0, entry); // Newest first
            // Keep max 500 entries
            if (data.Entries.Count > 500)
                data.Entries = data.Entries.Take(500).ToList();
            SaveJson(_historyPath, data);
        }

        public void ClearHistory()
        {
            SaveJson(_historyPath, new HistoryData());
        }

        public void ExportBookmarks(string destPath)
        {
            var data = LoadJson<BookmarkData>(_bookmarksPath) ?? new BookmarkData();
            SaveJson(destPath, data);
        }

        public bool ImportBookmarks(string srcPath)
        {
            var data = LoadJson<BookmarkData>(srcPath);
            if (data != null && data.Bookmarks != null)
            {
                var current = GetBookmarks();
                foreach (var b in data.Bookmarks)
                {
                    if (!current.Any(x => x.Url.Equals(b.Url, StringComparison.OrdinalIgnoreCase)))
                    {
                        current.Add(b);
                    }
                }
                SaveJson(_bookmarksPath, new BookmarkData { Bookmarks = current });
                return true;
            }
            return false;
        }

        public void ExportHistory(string destPath)
        {
            var data = LoadJson<HistoryData>(_historyPath) ?? new HistoryData();
            SaveJson(destPath, data);
        }

        public bool ImportHistory(string srcPath)
        {
            var data = LoadJson<HistoryData>(srcPath);
            if (data != null && data.Entries != null)
            {
                var current = GetHistory();
                current.InsertRange(0, data.Entries);
                var merged = current.OrderByDescending(x => x.DownloadedAt).Take(500).ToList();
                SaveJson(_historyPath, new HistoryData { Entries = merged });
                return true;
            }
            return false;
        }

        // ===== JSON helpers (DataContractJsonSerializer for .NET Framework 4.8 compatibility) =====

        private T LoadJson<T>(string path) where T : class
        {
            if (!File.Exists(path)) return null;
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) return null;

                var serializer = new DataContractJsonSerializer(typeof(T), new DataContractJsonSerializerSettings
                {
                    DateTimeFormat = new DateTimeFormat("yyyy-MM-ddTHH:mm:ssZ")
                });
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    return serializer.ReadObject(ms) as T;
                }
            }
            catch
            {
                return null;
            }
        }

        private void SaveJson<T>(string path, T data) where T : class
        {
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(T), new DataContractJsonSerializerSettings
                {
                    DateTimeFormat = new DateTimeFormat("yyyy-MM-ddTHH:mm:ssZ")
                });
                using (var ms = new MemoryStream())
                {
                    serializer.WriteObject(ms, data);
                    string json = Encoding.UTF8.GetString(ms.ToArray());
                    File.WriteAllText(path, json, Encoding.UTF8);
                }
            }
            catch
            {
                // Silently fail — bookmark/history is non-critical
            }
        }

        private List<ReaderPageBookmarkEntry> LoadReaderPageBookmarks()
        {
            List<ReaderPageBookmarkEntry> bookmarks = LoadReaderPageBookmarksMarkdown();
            if (bookmarks.Count > 0)
            {
                return bookmarks;
            }

            bookmarks = LoadReaderPageBookmarksBinary();
            if (bookmarks.Count > 0)
            {
                SaveReaderPageBookmarks(bookmarks);
                return bookmarks;
            }

            var legacy = LoadJson<ReaderPageBookmarkData>(_legacyReaderPageBookmarksJsonPath);
            if (legacy?.Bookmarks == null || legacy.Bookmarks.Count == 0)
            {
                return new List<ReaderPageBookmarkEntry>();
            }

            bookmarks = legacy.Bookmarks
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.PageFilePath))
                .Select(NormalizeReaderPageBookmark)
                .ToList();
            SaveReaderPageBookmarks(bookmarks);
            return bookmarks;
        }

        private List<ReaderPageBookmarkEntry> LoadReaderPageBookmarksMarkdown()
        {
            var bookmarks = new List<ReaderPageBookmarkEntry>();
            if (!File.Exists(_readerPageBookmarksPath))
            {
                return bookmarks;
            }

            try
            {
                string[] lines = File.ReadAllLines(_readerPageBookmarksPath, Encoding.UTF8);
                string[] headers = null;

                foreach (string rawLine in lines)
                {
                    string line = rawLine == null ? string.Empty : rawLine.Trim();
                    if (!line.StartsWith("|") || !line.EndsWith("|"))
                    {
                        continue;
                    }

                    string[] cells = SplitMarkdownRow(line);
                    if (cells.Length == 0)
                    {
                        continue;
                    }

                    if (headers == null)
                    {
                        headers = cells;
                        continue;
                    }

                    if (IsMarkdownSeparatorRow(cells))
                    {
                        continue;
                    }

                    ReaderPageBookmarkEntry entry = ParseReaderPageBookmarkMarkdownRow(headers, cells);
                    if (entry != null && !string.IsNullOrWhiteSpace(entry.PageFilePath))
                    {
                        bookmarks.Add(NormalizeReaderPageBookmark(entry));
                    }
                }
            }
            catch
            {
                return new List<ReaderPageBookmarkEntry>();
            }

            return bookmarks;
        }

        private List<ReaderPageBookmarkEntry> LoadReaderPageBookmarksBinary()
        {
            var bookmarks = new List<ReaderPageBookmarkEntry>();
            if (!File.Exists(_legacyReaderPageBookmarksBinaryPath))
            {
                return bookmarks;
            }

            try
            {
                using (var stream = File.Open(_legacyReaderPageBookmarksBinaryPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new BinaryReader(stream, Encoding.UTF8))
                {
                    const int expectedMagic = 0x474D5242;
                    int magic = reader.ReadInt32();
                    if (magic != expectedMagic)
                    {
                        return new List<ReaderPageBookmarkEntry>();
                    }

                    int version = reader.ReadInt32();
                    int count = reader.ReadInt32();
                    for (int i = 0; i < count; i++)
                    {
                        var entry = new ReaderPageBookmarkEntry
                        {
                            BookmarkId = ReadNullableString(reader),
                            MangaName = ReadNullableString(reader),
                            ChapterName = ReadNullableString(reader),
                            PageName = ReadNullableString(reader),
                            SourceDomain = ReadNullableString(reader),
                            BookmarkedAt = DateTime.FromBinary(reader.ReadInt64()),
                            LibraryRoot = ReadNullableString(reader),
                            SeriesFolderPath = ReadNullableString(reader),
                            MangaFolderPath = ReadNullableString(reader),
                            ChapterFolderPath = ReadNullableString(reader),
                            PageFilePath = ReadNullableString(reader),
                            PageIndex = reader.ReadInt32(),
                            FitModeKey = version >= 2 ? ReadNullableString(reader) : null,
                            ReaderZoom = version >= 2 ? reader.ReadDouble() : 1d,
                            ViewportPageIndex = version >= 2 ? reader.ReadInt32() : -1,
                            ViewportPageXRatio = version >= 2 ? reader.ReadDouble() : 0.5d,
                            ViewportPageYRatio = version >= 2 ? reader.ReadDouble() : 0.5d
                        };

                        if (!string.IsNullOrWhiteSpace(entry.PageFilePath))
                        {
                            bookmarks.Add(NormalizeReaderPageBookmark(entry));
                        }
                    }
                }
            }
            catch
            {
                return new List<ReaderPageBookmarkEntry>();
            }

            return bookmarks;
        }

        private void SaveReaderPageBookmarks(List<ReaderPageBookmarkEntry> bookmarks)
        {
            try
            {
                List<ReaderPageBookmarkEntry> normalized = (bookmarks ?? new List<ReaderPageBookmarkEntry>())
                    .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.PageFilePath))
                    .Select(NormalizeReaderPageBookmark)
                    .ToList();

                var sb = new StringBuilder();
                sb.AppendLine("# Reader Page Bookmarks");
                sb.AppendLine();
                sb.AppendLine("| BookmarkId | BookmarkedAt | MangaName | ChapterName | PageIndex | PageName | FitModeKey | ReaderZoom | ViewportPageIndex | ViewportPageXRatio | ViewportPageYRatio | SourceDomain | LibraryRoot | SeriesFolderPath | MangaFolderPath | ChapterFolderPath | PageFilePath |");
                sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");

                foreach (ReaderPageBookmarkEntry entry in normalized)
                {
                    sb.AppendLine(string.Join("",
                        "| ", EscapeMarkdownCell(entry.BookmarkId),
                        " | ", EscapeMarkdownCell(entry.BookmarkedAt.ToString("o", CultureInfo.InvariantCulture)),
                        " | ", EscapeMarkdownCell(entry.MangaName),
                        " | ", EscapeMarkdownCell(entry.ChapterName),
                        " | ", entry.PageIndex.ToString(CultureInfo.InvariantCulture),
                        " | ", EscapeMarkdownCell(entry.PageName),
                        " | ", EscapeMarkdownCell(entry.FitModeKey),
                        " | ", entry.ReaderZoom.ToString("0.#######", CultureInfo.InvariantCulture),
                        " | ", entry.ViewportPageIndex.ToString(CultureInfo.InvariantCulture),
                        " | ", entry.ViewportPageXRatio.ToString("0.#######", CultureInfo.InvariantCulture),
                        " | ", entry.ViewportPageYRatio.ToString("0.#######", CultureInfo.InvariantCulture),
                        " | ", EscapeMarkdownCell(entry.SourceDomain),
                        " | ", EscapeMarkdownCell(entry.LibraryRoot),
                        " | ", EscapeMarkdownCell(entry.SeriesFolderPath),
                        " | ", EscapeMarkdownCell(entry.MangaFolderPath),
                        " | ", EscapeMarkdownCell(entry.ChapterFolderPath),
                        " | ", EscapeMarkdownCell(entry.PageFilePath),
                        " |"));
                }

                File.WriteAllText(_readerPageBookmarksPath, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static ReaderPageBookmarkEntry NormalizeReaderPageBookmark(ReaderPageBookmarkEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(entry.BookmarkId))
            {
                entry.BookmarkId = Guid.NewGuid().ToString("N");
            }

            entry.PageIndex = Math.Max(1, entry.PageIndex);
            entry.ViewportPageIndex = entry.ViewportPageIndex < 0 ? entry.PageIndex - 1 : entry.ViewportPageIndex;
            entry.ViewportPageXRatio = ClampUnit(entry.ViewportPageXRatio, 0.5d);
            entry.ViewportPageYRatio = ClampUnit(entry.ViewportPageYRatio, 0.5d);
            if (entry.ReaderZoom <= 0)
            {
                entry.ReaderZoom = 1d;
            }

            return entry;
        }

        private static string[] SplitMarkdownRow(string line)
        {
            return line.Trim().Trim('|').Split(new[] { '|' }, StringSplitOptions.None)
                .Select(cell => cell.Trim())
                .ToArray();
        }

        private static bool IsMarkdownSeparatorRow(string[] cells)
        {
            return cells.All(cell => cell.Length > 0 && cell.All(ch => ch == '-' || ch == ':' || ch == ' '));
        }

        private static ReaderPageBookmarkEntry ParseReaderPageBookmarkMarkdownRow(string[] headers, string[] cells)
        {
            if (headers == null || cells == null || headers.Length == 0 || cells.Length == 0)
            {
                return null;
            }

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Length; i++)
            {
                string value = i < cells.Length ? UnescapeMarkdownCell(cells[i]) : string.Empty;
                map[headers[i]] = value;
            }

            return new ReaderPageBookmarkEntry
            {
                BookmarkId = GetMapValue(map, "BookmarkId"),
                MangaName = GetMapValue(map, "MangaName"),
                ChapterName = GetMapValue(map, "ChapterName"),
                PageName = GetMapValue(map, "PageName"),
                SourceDomain = GetMapValue(map, "SourceDomain"),
                BookmarkedAt = ParseDateTimeValue(GetMapValue(map, "BookmarkedAt")),
                LibraryRoot = GetMapValue(map, "LibraryRoot"),
                SeriesFolderPath = GetMapValue(map, "SeriesFolderPath"),
                MangaFolderPath = GetMapValue(map, "MangaFolderPath"),
                ChapterFolderPath = GetMapValue(map, "ChapterFolderPath"),
                PageFilePath = GetMapValue(map, "PageFilePath"),
                PageIndex = ParseIntValue(GetMapValue(map, "PageIndex"), 1),
                FitModeKey = GetMapValue(map, "FitModeKey"),
                ReaderZoom = ParseDoubleValue(GetMapValue(map, "ReaderZoom"), 1d),
                ViewportPageIndex = ParseIntValue(GetMapValue(map, "ViewportPageIndex"), -1),
                ViewportPageXRatio = ParseDoubleValue(GetMapValue(map, "ViewportPageXRatio"), 0.5d),
                ViewportPageYRatio = ParseDoubleValue(GetMapValue(map, "ViewportPageYRatio"), 0.5d)
            };
        }

        private static string GetMapValue(Dictionary<string, string> map, string key)
        {
            return map.TryGetValue(key, out string value) ? value : string.Empty;
        }

        private static string EscapeMarkdownCell(string value)
        {
            return (value ?? string.Empty)
                .Replace("|", "&#124;")
                .Replace("\r\n", "<br/>")
                .Replace("\n", "<br/>")
                .Replace("\r", "<br/>");
        }

        private static string UnescapeMarkdownCell(string value)
        {
            return (value ?? string.Empty)
                .Replace("<br/>", Environment.NewLine)
                .Replace("&#124;", "|");
        }

        private static int ParseIntValue(string value, int fallback)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : fallback;
        }

        private static double ParseDoubleValue(string value, double fallback)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                ? parsed
                : fallback;
        }

        private static DateTime ParseDateTimeValue(string value)
        {
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed)
                ? parsed
                : DateTime.Now;
        }

        private static string ReadNullableString(BinaryReader reader)
        {
            return reader.ReadBoolean() ? reader.ReadString() : null;
        }

        private static double ClampUnit(double value, double fallback)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return fallback;
            }

            return Math.Max(0d, Math.Min(1d, value));
        }
    }
}

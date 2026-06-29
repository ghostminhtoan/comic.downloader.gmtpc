using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private enum LogFilterMode
        {
            All,
            Error
        }

        private sealed class LogEntry
        {
            public string Text { get; set; }
            public bool IsError { get; set; }
        }

        private sealed class LogPanelState
        {
            public string Key { get; set; }
            public RichTextBox Box { get; set; }
            public LogFilterMode Mode { get; set; } = LogFilterMode.All;
            public List<LogEntry> Entries { get; } = new List<LogEntry>();
            public Dictionary<string, int> KeyedEntries { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        private readonly Dictionary<string, LogPanelState> _logPanels = new Dictionary<string, LogPanelState>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<RichTextBox, string> _logPanelKeys = new Dictionary<RichTextBox, string>();
        private readonly ObservableCollection<CheckErrorItem> _checkErrors = new ObservableCollection<CheckErrorItem>();
        private readonly Dictionary<string, CheckErrorItem> _checkErrorIndex = new Dictionary<string, CheckErrorItem>(StringComparer.OrdinalIgnoreCase);

        public ObservableCollection<CheckErrorItem> CheckErrors => _checkErrors;

        private void InitializeLogPanels()
        {
            RegisterLogPanel("main", txtLog);
            RegisterLogPanel("nhentai", txtNhentaiLog);
            RegisterLogPanel("vihentai", txtViHentaiLog);
            RegisterLogPanel("truyenqq", txtTruyenqqLog);
            RegisterLogPanel("nettruyen", txtNettruyenLog);
            RegisterLogPanel("nettruyen.tech", txtNettruyenTechLog);
            RegisterLogPanel("hako", txtHakoLog);
            RegisterLogPanel("hentai2read", txtHentai2readLog);
            RegisterLogPanel("hentaiera", txtHentaieraLog);
        }

        private void RegisterLogPanel(string key, RichTextBox box)
        {
            if (box == null || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var state = new LogPanelState
            {
                Key = key,
                Box = box
            };

            _logPanels[key] = state;
            _logPanelKeys[box] = key;
        }

        private LogPanelState GetLogPanelState(RichTextBox box)
        {
            if (box == null)
            {
                return null;
            }

            if (_logPanelKeys.TryGetValue(box, out string key) && _logPanels.TryGetValue(key, out LogPanelState state))
            {
                return state;
            }

            return null;
        }

        private void AppendLogLineToDocument(RichTextBox rtb, string text, bool isError)
        {
            if (rtb == null)
            {
                return;
            }

            var brush = isError ? System.Windows.Media.Brushes.Red : null;

            var paragraph = rtb.Document.Blocks.LastBlock as Paragraph;
            if (paragraph == null)
            {
                paragraph = new Paragraph { Margin = new Thickness(0) };
                rtb.Document.Blocks.Add(paragraph);
            }

            var run = new Run(text);
            if (brush != null)
            {
                run.Foreground = brush;
            }
            paragraph.Inlines.Add(run);
        }

        private void RenderLogPanel(LogPanelState state)
        {
            if (state == null || state.Box == null)
            {
                return;
            }

            state.Box.Document.Blocks.Clear();
            foreach (var entry in state.Entries)
            {
                if (state.Mode == LogFilterMode.Error && !entry.IsError)
                {
                    continue;
                }

                AppendLogLineToDocument(state.Box, entry.Text, entry.IsError);
            }

        }

        internal void AppendLogLineWithFilter(RichTextBox rtb, string text, bool isError)
        {
            if (rtb == null)
            {
                return;
            }

            var state = GetLogPanelState(rtb);
            if (state == null)
            {
                AppendLogLineToDocument(rtb, text, isError);
                return;
            }

            state.Entries.Add(new LogEntry { Text = text, IsError = isError });
            if (state.Mode == LogFilterMode.Error && !isError)
            {
                return;
            }

            AppendLogLineToDocument(rtb, text, isError);
        }

        private static string BuildCheckErrorKey(string source, string bookName, string chapterName, int pageNumber, string errorMessage, string pageName = null)
        {
            return string.Join("||",
                (source ?? string.Empty).Trim().ToUpperInvariant(),
                (bookName ?? string.Empty).Trim().ToUpperInvariant(),
                (chapterName ?? string.Empty).Trim().ToUpperInvariant(),
                pageNumber.ToString(),
                (pageName ?? string.Empty).Trim().ToUpperInvariant(),
                (errorMessage ?? string.Empty).Trim().ToUpperInvariant());
        }

        internal void RecordCheckError(string source, string bookName, string chapterName, int pageNumber, string errorMessage, string imageUrl = null, string pageName = null)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                string normalizedSource = string.IsNullOrWhiteSpace(source) ? "GENERAL" : source.Trim();
                string normalizedBook = string.IsNullOrWhiteSpace(bookName) ? "-" : bookName.Trim();
                string normalizedChapter = string.IsNullOrWhiteSpace(chapterName) ? "-" : chapterName.Trim();
                string key = BuildCheckErrorKey(normalizedSource, normalizedBook, normalizedChapter, pageNumber, errorMessage, pageName);

                if (_checkErrorIndex.TryGetValue(key, out CheckErrorItem existing))
                {
                    existing.OccurrenceCount++;
                    existing.LastSeen = DateTime.Now;
                    existing.ErrorMessage = errorMessage.Trim();
                    if (!string.IsNullOrWhiteSpace(imageUrl))
                    {
                        existing.ImageUrl = imageUrl;
                    }
                    if (string.IsNullOrWhiteSpace(existing.Source))
                    {
                        existing.Source = normalizedSource;
                    }
                    if (string.IsNullOrWhiteSpace(existing.BookName))
                    {
                        existing.BookName = normalizedBook;
                    }
                    if (string.IsNullOrWhiteSpace(existing.ChapterName))
                    {
                        existing.ChapterName = normalizedChapter;
                    }
                    if (existing.PageNumber <= 0 && pageNumber > 0)
                    {
                        existing.PageNumber = pageNumber;
                    }
                    if (string.IsNullOrEmpty(existing.PageName) && !string.IsNullOrEmpty(pageName))
                    {
                        existing.PageName = pageName;
                    }
                    return;
                }

                var entry = new CheckErrorItem
                {
                    LastSeen = DateTime.Now,
                    Source = normalizedSource,
                    BookName = normalizedBook,
                    ChapterName = normalizedChapter,
                    PageNumber = pageNumber,
                    PageName = pageName,
                    ErrorMessage = errorMessage.Trim(),
                    ImageUrl = imageUrl,
                    OccurrenceCount = 1
                };
                _checkErrorIndex[key] = entry;
                _checkErrors.Add(entry);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        internal void ClearCheckErrors()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _checkErrors.Clear();
                _checkErrorIndex.Clear();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        internal void UpsertMainLogLine(string entryKey, string message, bool isError = false)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (txtLog == null)
                {
                    return;
                }

                var state = GetLogPanelState(txtLog);
                if (state == null || string.IsNullOrWhiteSpace(entryKey))
                {
                    AppendLogLineToDocument(txtLog, $"[{DateTime.Now:HH:mm:ss}] {message}\r\n", isError);
                    return;
                }

                string logLine = $"[{DateTime.Now:HH:mm:ss}] {message}\r\n";

                if (state.KeyedEntries.TryGetValue(entryKey, out int entryIndex) &&
                    entryIndex >= 0 &&
                    entryIndex < state.Entries.Count)
                {
                    state.Entries[entryIndex].Text = logLine;
                    state.Entries[entryIndex].IsError = isError;
                }
                else
                {
                    entryIndex = state.Entries.Count;
                    state.Entries.Add(new LogEntry { Text = logLine, IsError = isError });
                    state.KeyedEntries[entryKey] = entryIndex;
                }

                RenderLogPanel(state);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        internal void ClearLogPanel(RichTextBox rtb)
        {
            var state = GetLogPanelState(rtb);
            if (state != null)
            {
                state.Entries.Clear();
                state.KeyedEntries.Clear();
            }

            if (rtb != null)
            {
                rtb.Document.Blocks.Clear();
            }
        }

        private void SetLogFilter(string key, LogFilterMode mode)
        {
            if (string.IsNullOrWhiteSpace(key) || !_logPanels.TryGetValue(key, out LogPanelState state))
            {
                return;
            }

            state.Mode = mode;
            RenderLogPanel(state);
        }

        private LogFilterMode GetLogFilter(string key)
        {
            if (!string.IsNullOrWhiteSpace(key) && _logPanels.TryGetValue(key, out LogPanelState state))
            {
                return state.Mode;
            }

            return LogFilterMode.All;
        }

        private void SyncLogFilterMenu(ContextMenu menu, string key)
        {
            if (menu == null)
            {
                return;
            }

            var mode = GetLogFilter(key);
            foreach (var item in menu.Items.OfType<MenuItem>())
            {
                if (string.Equals(item.Tag as string, "All", StringComparison.OrdinalIgnoreCase))
                {
                    item.IsChecked = mode == LogFilterMode.All;
                }
                else if (string.Equals(item.Tag as string, "Error", StringComparison.OrdinalIgnoreCase))
                {
                    item.IsChecked = mode == LogFilterMode.Error;
                }
            }
        }

        internal void SetMainLogErrorOnly(bool errorOnly)
        {
            SetLogFilter("main", errorOnly ? LogFilterMode.Error : LogFilterMode.All);
        }

        private void ChkErrorOnlyLog_Checked(object sender, RoutedEventArgs e)
        {
            SetMainLogErrorOnly(true);
        }

        private void ChkErrorOnlyLog_Unchecked(object sender, RoutedEventArgs e)
        {
            SetMainLogErrorOnly(false);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows;
using System.Windows.Controls;
namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private const string GalleryStateBeginMarker = "<!-- COMIC_GMTPC_STATE_BEGIN -->";
        private const string GalleryStateEndMarker = "<!-- COMIC_GMTPC_STATE_END -->";
        private const string GallerySettingsBeginMarker = "<!-- COMIC_GMTPC_SETTINGS_BEGIN -->";
        private const string GallerySettingsEndMarker = "<!-- COMIC_GMTPC_SETTINGS_END -->";

        [DataContract]
        private sealed class GalleryItemState
        {
            [DataMember(Order = 1)]
            public int OriginalIndex { get; set; }

            [DataMember(Order = 2)]
            public bool IsChecked { get; set; }

            [DataMember(Order = 3)]
            public bool IsDuplicate { get; set; }

            [DataMember(Order = 4)]
            public bool HasNoChapters { get; set; }

            [DataMember(Order = 5)]
            public string Name { get; set; }

            [DataMember(Order = 6)]
            public string Link { get; set; }

            [DataMember(Order = 7)]
            public string LinkCount { get; set; }

            [DataMember(Order = 8)]
            public string SourceDomain { get; set; }

            [DataMember(Order = 9)]
            public int TotalChapters { get; set; }

            [DataMember(Order = 10)]
            public int CompletedChapters { get; set; }

            [DataMember(Order = 11)]
            public string Status { get; set; }

            [DataMember(Order = 12)]
            public string CurrentProcess { get; set; }

            [DataMember(Order = 13)]
            public int ErrorCount { get; set; }

            [DataMember(Order = 14)]
            public string DownloadPath { get; set; }

            [DataMember(Order = 15)]
            public double ProgressPercent { get; set; }

            [DataMember(Order = 16)]
            public bool IsPaused { get; set; }

            [DataMember(Order = 17)]
            public bool IsStopped { get; set; }

            [DataMember(Order = 18)]
            public string DownloadingChapter { get; set; }

            [DataMember(Order = 19)]
            public string DownloadingPageProgress { get; set; }

            [DataMember(Order = 20)]
            public List<ErrorState> Errors { get; set; } = new List<ErrorState>();

            [DataMember(Order = 21)]
            public int ConnectionCount { get; set; }

            [DataMember(Order = 22)]
            public int MultiDownloadCount { get; set; }
        }

        [DataContract]
        private sealed class ErrorState
        {
            [DataMember(Order = 1)]
            public string ChapterName { get; set; }

            [DataMember(Order = 2)]
            public int PageNumber { get; set; }

            [DataMember(Order = 3)]
            public string ErrorMessage { get; set; }

            [DataMember(Order = 4)]
            public string ImageUrl { get; set; }

            [DataMember(Order = 5)]
            public string PageName { get; set; }
        }

        [DataContract]
        private sealed class GalleryMarkdownSettingsState
        {
            [DataMember(Order = 1)]
            public Dictionary<string, string> CreateSubfolderByDomain { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            [DataMember(Order = 2)]
            public int ConnectionCount { get; set; }

            [DataMember(Order = 3)]
            public int MultiDownloadCount { get; set; }

            [DataMember(Order = 4)]
            public int DownloadFolderType { get; set; }

            [DataMember(Order = 5)]
            public string DownloadPath { get; set; }
        }

        [DataContract]
        private sealed class LightNovelChapterState
        {
            [DataMember(Order = 1)]
            public string ChapterTitle { get; set; }

            [DataMember(Order = 2)]
            public string ChapterLink { get; set; }

            [DataMember(Order = 3)]
            public string VolumeTitle { get; set; }

            [DataMember(Order = 4)]
            public int VolumeOrder { get; set; }

            [DataMember(Order = 5)]
            public int SequenceIndex { get; set; }

            [DataMember(Order = 6)]
            public bool IsChecked { get; set; }

            [DataMember(Order = 7)]
            public string MarkdownFilePath { get; set; }
        }

        [DataContract]
        private sealed class LightNovelBookState
        {
            [DataMember(Order = 1)]
            public GalleryItemState Book { get; set; }

            [DataMember(Order = 2)]
            public List<LightNovelChapterState> Chapters { get; set; } = new List<LightNovelChapterState>();
        }

        [DataContract]
        private sealed class GalleryMarkdownPayload
        {
            [DataMember(Order = 1)]
            public List<GalleryItemState> MangaItems { get; set; } = new List<GalleryItemState>();

            [DataMember(Order = 2)]
            public List<LightNovelBookState> LightNovelBooks { get; set; } = new List<LightNovelBookState>();
        }

        private List<GalleryItem> GetItemsToExport()
        {
            var checkedItems = _scrapedItems.Where(item => item.IsChecked).ToList();
            return checkedItems.Any() ? checkedItems : _scrapedItems.ToList();
        }

        private void SaveGalleryItemsMarkdownFile(string path, IList<GalleryItem> items, string title)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path is required.", nameof(path));
            }

            var safeItems = (items ?? Array.Empty<GalleryItem>()).ToList();
            var mangaStates = safeItems.Select((item, index) => CreateGalleryItemState(item, index)).ToList();
            var lightNovelStates = CreateLightNovelBookStates();
            var payload = new GalleryMarkdownPayload
            {
                MangaItems = mangaStates,
                LightNovelBooks = lightNovelStates
            };

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"# {title}");
            sb.AppendLine();
            sb.AppendLine("## Manga Summary");
            sb.AppendLine("| No. | Checked | Name | Link | Chapter | Page | Status | Process | Error Count |");
            sb.AppendLine("| :--- | :---: | :--- | :--- | :--- | :--- | :--- | :--- | :---: |");

            if (safeItems.Count == 0)
            {
                sb.AppendLine("| - | - | *(empty)* |  |  |  |  |  | 0 |");
            }

            for (int i = 0; i < safeItems.Count; i++)
            {
                var item = safeItems[i];
                string checkedStr = item.IsChecked ? "[x]" : "[ ]";
                string status = EscapeMarkdownCell(item.Status);
                string process = EscapeMarkdownCell(item.CurrentProcess);
                string safeName = EscapeMarkdownCell(item.Name);
                string safeLink = EscapeMarkdownCell(item.Link);
                string chapter = EscapeMarkdownCell(item.DownloadingChapter);
                string page = EscapeMarkdownCell(item.DownloadingPageProgress);
                sb.AppendLine($"| {i + 1} | {checkedStr} | {safeName} | {safeLink} | {chapter} | {page} | {status} | {process} | {item.GetUniqueErrorCount()} |");
            }

            sb.AppendLine();
            sb.AppendLine("## Light Novel Summary");
            sb.AppendLine("| No. | Checked | Book | Link | Chapters | Status | Process |");
            sb.AppendLine("| :--- | :---: | :--- | :--- | :---: | :--- | :--- |");

            if (lightNovelStates.Count == 0)
            {
                sb.AppendLine("| - | - | *(empty)* |  | 0 |  |  |");
            }

            for (int i = 0; i < lightNovelStates.Count; i++)
            {
                GalleryItem book = CreateGalleryItemFromState(lightNovelStates[i].Book);
                int chapterCount = lightNovelStates[i].Chapters?.Count ?? 0;
                string checkedStr = book != null && book.IsChecked ? "[x]" : "[ ]";
                string bookName = EscapeMarkdownCell(book?.Name);
                string bookLink = EscapeMarkdownCell(book?.Link);
                string status = EscapeMarkdownCell(book?.Status);
                string process = EscapeMarkdownCell(book?.CurrentProcess);
                sb.AppendLine($"| {i + 1} | {checkedStr} | {bookName} | {bookLink} | {chapterCount} | {status} | {process} |");
            }

            sb.AppendLine();
            sb.AppendLine("## Download Settings");
            sb.AppendLine(GallerySettingsBeginMarker);
            sb.AppendLine("```json");
            sb.AppendLine(SerializeGalleryMarkdownSettings());
            sb.AppendLine("```");
            sb.AppendLine(GallerySettingsEndMarker);
            sb.AppendLine();
            sb.AppendLine("## Full State");
            sb.AppendLine(GalleryStateBeginMarker);
            sb.AppendLine("```json");
            sb.AppendLine(SerializeGalleryPayload(payload));
            sb.AppendLine("```");
            sb.AppendLine(GalleryStateEndMarker);

            WriteTextFileAtomically(path, sb.ToString(), new UTF8Encoding(true));
        }

        private void WriteTextFileAtomically(string path, string content, Encoding encoding)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path is required.", nameof(path));
            }

            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string fileName = Path.GetFileName(fullPath);
            string tempPath = Path.Combine(directory ?? AppDomain.CurrentDomain.BaseDirectory, $".{fileName}.{Guid.NewGuid():N}.tmp");
            string backupPath = fullPath + ".bak";

            byte[] bytes = (encoding ?? Encoding.UTF8).GetBytes(content ?? string.Empty);
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }

            try
            {
                if (File.Exists(fullPath))
                {
                    File.Replace(tempPath, fullPath, backupPath, true);
                }
                else
                {
                    File.Move(tempPath, fullPath);
                }
            }
            catch
            {
                File.Copy(tempPath, fullPath, true);
                TryDeleteFileIfExists(tempPath);
            }
        }

        private void WriteAllLinesAtomically(string path, IEnumerable<string> lines, Encoding encoding)
        {
            string content = string.Join(Environment.NewLine, lines ?? Enumerable.Empty<string>());
            WriteTextFileAtomically(path, content, encoding ?? Encoding.UTF8);
        }

        private void TryDeleteFileIfExists(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private void BtnCopyLinksOnly_Click(object sender, RoutedEventArgs e)
        {
            var items = GetItemsToExport();
            if (!items.Any())
            {
                MessageBox.Show("No links to copy.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string text = string.Join("\r\n", items.Select(item => item.Link));
            Clipboard.SetText(text);

            bool showingSubset = items.Count < _scrapedItems.Count;
            string scopeText = showingSubset ? $"{items.Count} checked" : "All";
            Log($"{scopeText} links copied to clipboard (URLs only).");
            lblStatus.Text = $"Links copied ({scopeText} URLs only).";
        }

        private void BtnCopyNamesAndLinks_Click(object sender, RoutedEventArgs e)
        {
            var items = GetItemsToExport();
            if (!items.Any())
            {
                MessageBox.Show("No content to copy.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string text = string.Join("\r\n", items.Select(item => $"{item.Name}\t{item.Link}"));
            Clipboard.SetText(text);

            bool showingSubset = items.Count < _scrapedItems.Count;
            string scopeText = showingSubset ? $"{items.Count} checked" : "All";
            Log($"{scopeText} titles + links copied to clipboard in tabular format (ready for Excel/Word).");
            lblStatus.Text = $"Titles + links copied ({scopeText} table format).";
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var items = _scrapedItems.ToList();
            bool hasNovelItems = _lightNovelItems.Any();
            if (!items.Any() && !hasNovelItems)
            {
                MessageBox.Show("No content to save.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                SaveGalleryItemsMarkdownFile(PortablePaths.PortableGalleryListPath, items, "Saved Galleries And Light Novels");

                Log($"All content successfully saved to portable Markdown file: {PortablePaths.PortableGalleryListPath}");
                lblStatus.Text = "Saved portable MD file.";
            }
            catch (Exception ex)
            {
                Log($"Failed to save file: {ex.Message}");
                MessageBox.Show($"Error saving file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnLoad_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(PortablePaths.PortableGalleryListPath))
            {
                MessageBox.Show($"Portable markdown file not found:\n{PortablePaths.PortableGalleryListPath}", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                string content = File.ReadAllText(PortablePaths.PortableGalleryListPath, Encoding.UTF8);
                ApplyGalleryMarkdownSettings(content);
                List<GalleryItem> loadedItems = LoadGalleryItemsFromMarkdown(content);
                List<LightNovelBookState> loadedLightNovels = LoadLightNovelBooksFromMarkdown(content);

                if (loadedItems.Any() || loadedLightNovels.Any())
                {
                    _scrapedItems.Clear();
                    if (chkSelectAll != null)
                    {
                        chkSelectAll.IsChecked = false;
                    }

                    for (int i = 0; i < loadedItems.Count; i++)
                    {
                        var item = loadedItems[i];
                        item.OriginalIndex = i;
                        _scrapedItems.Add(item);
                    }

                    ApplyLoadedLightNovelBooks(loadedLightNovels);

                    RecalculateDuplicates();
                    lblLinkCount.Text = _scrapedItems.Count.ToString();
                    ApplySavedDownloadSettings(loadedItems.FirstOrDefault());
                    Log($"Successfully loaded {_scrapedItems.Count} manga items and {_lightNovelItems.Count} light novel books from portable markdown: {PortablePaths.PortableGalleryListPath}");
                    lblStatus.Text = "Portable markdown file loaded successfully.";
                }
                else
                {
                    MessageBox.Show("No valid entries found in the markdown file.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to load file: {ex.Message}");
                MessageBox.Show($"Error loading file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            _scrapedItems.Clear();
            if (chkSelectAll != null)
            {
                chkSelectAll.IsChecked = false;
            }
            lblLinkCount.Text = "0";
            Log("Extracted results cleared.");
            lblStatus.Text = "Results cleared.";
            RecalculateDuplicates();
        }

        public void UpdateLinkCount()
        {
            Dispatcher.Invoke(() =>
            {
                lblLinkCount.Text = _scrapedItems.Count.ToString();
            });
        }

        private List<GalleryItem> LoadGalleryItemsFromMarkdown(string content)
        {
            var payload = TryLoadGalleryMarkdownPayload(content);
            var stateItems = payload?.MangaItems;
            if (stateItems != null && stateItems.Any())
            {
                return stateItems
                    .OrderBy(item => item.OriginalIndex)
                    .Select(CreateGalleryItemFromState)
                    .Where(item => item != null)
                    .ToList();
            }

            return LoadLegacyGalleryItems(content);
        }

        private List<LightNovelBookState> LoadLightNovelBooksFromMarkdown(string content)
        {
            var payload = TryLoadGalleryMarkdownPayload(content);
            return payload?.LightNovelBooks ?? new List<LightNovelBookState>();
        }

        private List<GalleryItem> LoadLegacyGalleryItems(string content)
        {
            string[] lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            List<GalleryItem> loadedItems = new List<GalleryItem>();

            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("|") && trimmed.EndsWith("|"))
                {
                    var parts = trimmed.Split('|');
                    if (parts.Length >= 4)
                    {
                        string noStr = parts[1].Trim();
                        string name = parts[2].Trim();
                        string link = parts[3].Trim();

                        if (noStr.Equals("No.", StringComparison.OrdinalIgnoreCase) ||
                            noStr.Contains("---") ||
                            noStr.Contains(":") ||
                            string.IsNullOrEmpty(noStr))
                        {
                            continue;
                        }

                        if (!string.IsNullOrEmpty(link) && link.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        {
                            name = name.Replace("\\|", "|");
                            bool isChecked = false;
                            if (parts.Length >= 5)
                            {
                                string selStr = parts[4].Trim();
                                isChecked = selStr.Contains("[x]") || selStr.Equals("Yes", StringComparison.OrdinalIgnoreCase) || selStr.Equals("1");
                            }

                            if (!loadedItems.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                            {
                                loadedItems.Add(new GalleryItem
                                {
                                    Name = name,
                                    Link = link,
                                    OriginalIndex = loadedItems.Count,
                                    IsChecked = isChecked
                                });
                            }
                        }
                    }
                }
            }

            return loadedItems;
        }

        private GalleryMarkdownPayload TryLoadGalleryMarkdownPayload(string content)
        {
            int beginMarkerIndex = content.IndexOf(GalleryStateBeginMarker, StringComparison.Ordinal);
            if (beginMarkerIndex < 0)
            {
                return null;
            }

            int jsonFenceStart = content.IndexOf("```json", beginMarkerIndex, StringComparison.OrdinalIgnoreCase);
            if (jsonFenceStart < 0)
            {
                return null;
            }

            jsonFenceStart = content.IndexOf('\n', jsonFenceStart);
            if (jsonFenceStart < 0)
            {
                return null;
            }

            jsonFenceStart++;
            int jsonFenceEnd = content.IndexOf("```", jsonFenceStart, StringComparison.Ordinal);
            if (jsonFenceEnd < 0)
            {
                return null;
            }

            string json = content.Substring(jsonFenceStart, jsonFenceEnd - jsonFenceStart).Trim();
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                var payloadSerializer = new DataContractJsonSerializer(typeof(GalleryMarkdownPayload));
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var payload = payloadSerializer.ReadObject(ms) as GalleryMarkdownPayload;
                    if (payload != null)
                    {
                        payload.MangaItems = payload.MangaItems ?? new List<GalleryItemState>();
                        payload.LightNovelBooks = payload.LightNovelBooks ?? new List<LightNovelBookState>();
                        return payload;
                    }
                }
            }
            catch
            {
            }

            try
            {
                var legacySerializer = new DataContractJsonSerializer(typeof(List<GalleryItemState>));
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    return new GalleryMarkdownPayload
                    {
                        MangaItems = legacySerializer.ReadObject(ms) as List<GalleryItemState> ?? new List<GalleryItemState>(),
                        LightNovelBooks = new List<LightNovelBookState>()
                    };
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to parse saved state block: {ex.Message}");
                return null;
            }
        }

        private string SerializeGalleryPayload(GalleryMarkdownPayload payload)
        {
            var serializer = new DataContractJsonSerializer(typeof(GalleryMarkdownPayload));
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, payload);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private string SerializeGalleryMarkdownSettings()
        {
            var settings = new GalleryMarkdownSettingsState
            {
                CreateSubfolderByDomain = _createSubfolderByDomain
                    .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
                ConnectionCount = GetComboBoxSelectedInt(cmbConnections, 4),
                MultiDownloadCount = GetComboBoxSelectedInt(cmbMultiDownload, 2),
                DownloadFolderType = cmbDownloadFolderType != null ? cmbDownloadFolderType.SelectedIndex : 0,
                DownloadPath = txtDownloadPath != null ? txtDownloadPath.Text : string.Empty
            };

            var serializer = new DataContractJsonSerializer(typeof(GalleryMarkdownSettingsState));
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, settings);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private GalleryItemState CreateGalleryItemState(GalleryItem item, int index)
        {
            return new GalleryItemState
            {
                OriginalIndex = item.OriginalIndex >= 0 ? item.OriginalIndex : index,
                IsChecked = item.IsChecked,
                IsDuplicate = item.IsDuplicate,
                HasNoChapters = item.HasNoChapters,
                Name = item.Name,
                Link = item.Link,
                LinkCount = item.LinkCount,
                SourceDomain = item.SourceDomain,
                TotalChapters = item.TotalChapters,
                CompletedChapters = item.CompletedChapters,
                Status = item.Status,
                CurrentProcess = item.CurrentProcess,
                ErrorCount = item.GetUniqueErrorCount(),
                DownloadPath = item.DownloadPath,
                ProgressPercent = item.ProgressPercent,
                IsPaused = item.IsPaused,
                IsStopped = item.IsStopped,
                DownloadingChapter = item.DownloadingChapter,
                DownloadingPageProgress = item.DownloadingPageProgress,
                ConnectionCount = item.ConnectionCount,
                MultiDownloadCount = GetComboBoxSelectedInt(cmbMultiDownload, 2),
                Errors = item.GetUniqueErrors().Select(error => new ErrorState
                {
                    ChapterName = error.ChapterName,
                    PageNumber = error.PageNumber,
                    PageName = error.PageName,
                    ErrorMessage = error.ErrorMessage,
                    ImageUrl = error.ImageUrl
                }).ToList()
            };
        }

        private List<LightNovelBookState> CreateLightNovelBookStates()
        {
            return _lightNovelItems
                .Select((item, index) =>
                {
                    _lightNovelChapterMap.TryGetValue(GetLightNovelItemKey(item), out var chapters);
                    return new LightNovelBookState
                    {
                        Book = CreateGalleryItemState(item, index),
                        Chapters = (chapters ?? new System.Collections.ObjectModel.ObservableCollection<LightNovelChapterRecord>())
                            .OrderBy(chapter => chapter.VolumeOrder)
                            .ThenBy(chapter => chapter.SequenceIndex)
                            .ThenBy(chapter => chapter.ChapterTitle, StringComparer.OrdinalIgnoreCase)
                            .Select(chapter => new LightNovelChapterState
                            {
                                ChapterTitle = chapter.ChapterTitle,
                                ChapterLink = chapter.ChapterLink,
                                VolumeTitle = chapter.VolumeTitle,
                                VolumeOrder = chapter.VolumeOrder,
                                SequenceIndex = chapter.SequenceIndex,
                                IsChecked = chapter.IsChecked,
                                MarkdownFilePath = chapter.MarkdownFilePath
                            })
                            .ToList()
                    };
                })
                .ToList();
        }

        private void ApplyLoadedLightNovelBooks(IList<LightNovelBookState> states)
        {
            _lightNovelItems.Clear();
            _lightNovelChapterMap.Clear();
            _lightNovelChapterView.Clear();
            _selectedLightNovelBook = null;
            _selectedLightNovelChapter = null;

            foreach (LightNovelBookState state in (states ?? Array.Empty<LightNovelBookState>()).Where(entry => entry?.Book != null).OrderBy(entry => entry.Book.OriginalIndex))
            {
                GalleryItem book = CreateGalleryItemFromState(state.Book);
                if (book == null)
                {
                    continue;
                }

                _lightNovelItems.Add(book);
                var chapters = new System.Collections.ObjectModel.ObservableCollection<LightNovelChapterRecord>(
                    (state.Chapters ?? new List<LightNovelChapterState>())
                    .OrderBy(chapter => chapter.VolumeOrder)
                    .ThenBy(chapter => chapter.SequenceIndex)
                    .ThenBy(chapter => chapter.ChapterTitle, StringComparer.OrdinalIgnoreCase)
                    .Select(chapter => new LightNovelChapterRecord
                    {
                        ChapterTitle = chapter.ChapterTitle,
                        ChapterLink = chapter.ChapterLink,
                        VolumeTitle = chapter.VolumeTitle,
                        VolumeOrder = chapter.VolumeOrder,
                        SequenceIndex = chapter.SequenceIndex,
                        IsChecked = chapter.IsChecked,
                        MarkdownFilePath = chapter.MarkdownFilePath
                    }));
                _lightNovelChapterMap[GetLightNovelItemKey(book)] = chapters;
                TrackLightNovelChapterAutosave(chapters);
                UpdateLightNovelBookCheckedState(book);
            }

            _selectedLightNovelBook = _lightNovelItems.FirstOrDefault();
            if (_dgLightNovelBooks != null)
            {
                _dgLightNovelBooks.SelectedItem = _selectedLightNovelBook;
            }

            RefreshLightNovelDetail(_selectedLightNovelBook);
            RefreshLightNovelSummary();
        }

        private GalleryItem CreateGalleryItemFromState(GalleryItemState state)
        {
            if (state == null)
            {
                return null;
            }

            string restoredProcess = NormalizeSavedProcess(state.CurrentProcess);
            if (string.IsNullOrWhiteSpace(restoredProcess) &&
                !string.IsNullOrWhiteSpace(state.DownloadingChapter) &&
                !string.IsNullOrWhiteSpace(state.DownloadingPageProgress))
            {
                restoredProcess = $"{state.DownloadingChapter} ({state.DownloadingPageProgress.ToLowerInvariant()})";
            }

            var item = new GalleryItem
            {
                OriginalIndex = state.OriginalIndex,
                IsChecked = state.IsChecked,
                IsDuplicate = state.IsDuplicate,
                HasNoChapters = state.HasNoChapters,
                Name = string.IsNullOrWhiteSpace(state.Name) ? state.Name : FormatGalleryTitle(state.Name),
                Link = state.Link,
                LinkCount = state.LinkCount,
                SourceDomain = state.SourceDomain,
                TotalChapters = state.TotalChapters,
                CompletedChapters = state.CompletedChapters,
                Status = state.Status,
                CurrentProcess = restoredProcess,
                DownloadPath = state.DownloadPath,
                ProgressPercent = state.ProgressPercent,
                ConnectionCount = state.ConnectionCount > 0 ? Math.Min(16, Math.Max(1, state.ConnectionCount)) : GetCurrentConnectionLimit(),
                MultiDownloadCount = state.MultiDownloadCount,
                IsPaused = state.IsPaused,
                IsStopped = state.IsStopped,
                DownloadingChapter = state.DownloadingChapter,
                DownloadingPageProgress = state.DownloadingPageProgress,
                Errors = state.Errors == null
                    ? new List<ErrorDetail>()
                    : state.Errors.Select(error => new ErrorDetail
                    {
                        ChapterName = error.ChapterName,
                        PageNumber = error.PageNumber,
                        PageName = error.PageName,
                        ErrorMessage = error.ErrorMessage,
                        ImageUrl = error.ImageUrl
                    }).ToList()
            };

            item.ErrorCount = item.GetUniqueErrorCount();
            return item;
        }

        private string NormalizeSavedProcess(string process)
        {
            if (string.IsNullOrWhiteSpace(process))
            {
                return string.Empty;
            }

            string trimmed = process.Trim();
            if (trimmed.Equals("Waiting...", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("Starting...", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("Cancelled", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("Failed", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("Done", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("Done with errors", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("Queued", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("Paused", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("Downloading", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return process;
        }

        private int GetComboBoxSelectedInt(ComboBox comboBox, int defaultValue)
        {
            if (comboBox == null)
            {
                return defaultValue;
            }

            if (comboBox.SelectedItem is ComboBoxItem selectedItem && int.TryParse(selectedItem.Content?.ToString(), out int value))
            {
                return value;
            }

            return defaultValue;
        }

        private void SetComboBoxSelectedInt(ComboBox comboBox, int value)
        {
            if (comboBox == null)
            {
                return;
            }

            foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
            {
                if (int.TryParse(item.Content?.ToString(), out int parsed) && parsed == value)
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }
        }

        private void ApplySavedDownloadSettings(GalleryItem stateSource)
        {
            if (stateSource == null)
            {
                return;
            }

            if (stateSource.ConnectionCount > 0)
            {
                SetComboBoxSelectedInt(cmbConnections, Math.Min(16, Math.Max(1, stateSource.ConnectionCount)));
            }

            if (stateSource.MultiDownloadCount > 0)
            {
                SetComboBoxSelectedInt(cmbMultiDownload, stateSource.MultiDownloadCount);
            }
        }

        private void ApplyGalleryMarkdownSettings(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            int beginMarkerIndex = content.IndexOf(GallerySettingsBeginMarker, StringComparison.Ordinal);
            if (beginMarkerIndex < 0)
            {
                return;
            }

            int jsonFenceStart = content.IndexOf("```json", beginMarkerIndex, StringComparison.OrdinalIgnoreCase);
            if (jsonFenceStart < 0)
            {
                return;
            }

            jsonFenceStart = content.IndexOf('\n', jsonFenceStart);
            if (jsonFenceStart < 0)
            {
                return;
            }

            jsonFenceStart++;
            int jsonFenceEnd = content.IndexOf("```", jsonFenceStart, StringComparison.Ordinal);
            if (jsonFenceEnd < 0)
            {
                return;
            }

            string json = content.Substring(jsonFenceStart, jsonFenceEnd - jsonFenceStart).Trim();
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            try
            {
                var serializer = new DataContractJsonSerializer(typeof(GalleryMarkdownSettingsState));
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var settings = serializer.ReadObject(ms) as GalleryMarkdownSettingsState;
                    if (settings != null)
                    {
                        if (settings.CreateSubfolderByDomain != null && settings.CreateSubfolderByDomain.Count > 0)
                        {
                            _createSubfolderByDomain.Clear();
                            foreach (var pair in settings.CreateSubfolderByDomain)
                            {
                                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                                {
                                    continue;
                                }

                                _createSubfolderByDomain[pair.Key.Trim()] = pair.Value.Trim();
                            }

                            SaveCreateSubfolderSettings();
                            if (_createSubfolderUiReady)
                            {
                                UpdateCreateSubfolderFieldsFromSelection();
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(settings.DownloadPath))
                        {
                            Dispatcher.Invoke(() => txtDownloadPath.Text = settings.DownloadPath);
                        }

                        if (settings.ConnectionCount > 0)
                        {
                            Dispatcher.Invoke(() => SetComboBoxSelectedInt(cmbConnections, Math.Min(32, Math.Max(1, settings.ConnectionCount))));
                        }
                        if (settings.MultiDownloadCount > 0)
                        {
                            Dispatcher.Invoke(() => SetComboBoxSelectedInt(cmbMultiDownload, Math.Min(10, Math.Max(1, settings.MultiDownloadCount))));
                        }
                        Dispatcher.Invoke(() => {
                            if (cmbDownloadFolderType != null)
                            {
                                _suppressDownloadFolderTypeEvents = true;
                                try
                                {
                                    int selType = settings.DownloadFolderType;
                                    cmbDownloadFolderType.SelectedIndex = selType;
                                    _isSingleComicFolderType = (selType == 0);
                                    _lightNovelFloatingControlWindow?.UpdateFolderType(selType);
                                }
                                finally
                                {
                                    _suppressDownloadFolderTypeEvents = false;
                                }
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to parse saved markdown settings: {ex.Message}");
            }
        }

        private string EscapeMarkdownCell(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("|", "\\|")
                .Replace("\r", " ")
                .Replace("\n", "<br>");
        }

        private bool _shutdownAfterCompleted = false;

        private void BtnShutdownMenu_Click(object sender, RoutedEventArgs e)
        {
            if (popupShutdownOptions != null)
            {
                popupShutdownOptions.IsOpen = !popupShutdownOptions.IsOpen;
            }
        }

        private void OpenShutdownOptionsPopup()
        {
            if (popupShutdownOptions != null)
            {
                popupShutdownOptions.IsOpen = true;
            }
        }

        private void ChkShutdownAfterCompleted_Checked(object sender, RoutedEventArgs e)
        {
            _shutdownAfterCompleted = true;
            if (tglShutdownAfterDownload != null)
            {
                tglShutdownAfterDownload.IsChecked = true;
            }
        }

        private void ChkShutdownAfterCompleted_Unchecked(object sender, RoutedEventArgs e)
        {
            _shutdownAfterCompleted = false;
            if (tglShutdownAfterDownload != null)
            {
                tglShutdownAfterDownload.IsChecked = false;
            }
        }

        private void TglShutdownAfterDownload_Checked(object sender, RoutedEventArgs e)
        {
            _shutdownAfterCompleted = true;
            if (chkShutdownAfterCompleted != null)
            {
                chkShutdownAfterCompleted.IsChecked = true;
            }
        }

        private void TglShutdownAfterDownload_Unchecked(object sender, RoutedEventArgs e)
        {
            _shutdownAfterCompleted = false;
            if (chkShutdownAfterCompleted != null)
            {
                chkShutdownAfterCompleted.IsChecked = false;
            }
        }

        private void BtnCloseShutdownPopup_Click(object sender, RoutedEventArgs e)
        {
            if (popupShutdownOptions != null)
            {
                popupShutdownOptions.IsOpen = false;
            }
        }

        private void BtnScheduleShutdownTimer_Click(object sender, RoutedEventArgs e)
        {
            int days = 0, hours = 0, minutes = 0, seconds = 0;
            int.TryParse(txtShutdownDays?.Text, out days);
            int.TryParse(txtShutdownHours?.Text, out hours);
            int.TryParse(txtShutdownMinutes?.Text, out minutes);
            int.TryParse(txtShutdownSeconds?.Text, out seconds);

            int totalSeconds = seconds + minutes * 60 + hours * 3600 + days * 86400;
            if (totalSeconds <= 0)
            {
                MessageBox.Show(_isVietnameseUi ? "Vui lòng nhập thời gian lớn hơn 0." : "Please enter a time greater than 0.", "Info", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                System.Diagnostics.Process.Start("shutdown", $"-s -t {totalSeconds}");
                MessageBox.Show(_isVietnameseUi ? $"Đã hẹn giờ tắt máy sau {totalSeconds} giây." : $"Scheduled shutdown in {totalSeconds} seconds.", "Scheduled", MessageBoxButton.OK, MessageBoxImage.Information);
                if (popupShutdownOptions != null) popupShutdownOptions.IsOpen = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error scheduling shutdown: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancelShutdownTimer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start("shutdown", "-a");
                MessageBox.Show(_isVietnameseUi ? "Đã hủy lệnh tắt máy." : "Cancelled scheduled shutdown.", "Cancelled", MessageBoxButton.OK, MessageBoxImage.Information);
                if (popupShutdownOptions != null) popupShutdownOptions.IsOpen = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error cancelling shutdown: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Input;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private readonly Dictionary<string, ObservableCollection<LightNovelChapterRecord>> _lightNovelChapterMap =
            new Dictionary<string, ObservableCollection<LightNovelChapterRecord>>(StringComparer.OrdinalIgnoreCase);
        private readonly ObservableCollection<LightNovelChapterRecord> _lightNovelChapterView =
            new ObservableCollection<LightNovelChapterRecord>();
        private readonly HashSet<string> _lightNovelChapterWarmupInFlight =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource _lightNovelCopyCts;
        private CancellationTokenSource _lightNovelPreviewCts;
        private CancellationTokenSource _lightNovelScrapeCts;
        private CancellationTokenSource _lightNovelWarmupCts;
        private DataGrid _dgLightNovelBooks;
        private ListBox _lbLightNovelChapters;
        private TextBox _txtLightNovelTagUrl;
        private TextBox _txtLightNovelTotalPages;
        private TextBox _txtLightNovelPageFrom;
        private TextBox _txtLightNovelPageTo;
        private TextBox _txtLightNovelSelectedChapter;
        private TextBox _txtLightNovelPlainText;
        private TextBox _txtLightNovelMarkdown;
        private TextBlock _txtLightNovelCount;
        private GalleryItem _selectedLightNovelBook;
        private LightNovelChapterRecord _selectedLightNovelChapter;
        private SystemFloatingControlWindow _lightNovelFloatingControlWindow;
        private bool _lightNovelAutoFocusEnabled = false;
        private bool _lightNovelCopyBackoffActive;
        private bool _lightNovelFocusStealthActive;
        private bool _lightNovelFocusRestoreOverride;
        private bool _lightNovelFocusTrayHidden;
        private bool _lightNovelFloatingWasVisibleBeforeFocusTray;
        private double _lightNovelSavedWindowOpacity = 1d;
        private bool _lightNovelSavedShowInTaskbar = true;
        private System.Windows.Forms.NotifyIcon _lightNovelFocusTrayIcon;
        private bool _lightNovelDeskInitialized;

        private void InitializeLightNovelDesk()
        {
            if (_lightNovelDeskInitialized)
            {
                return;
            }

            _dgLightNovelBooks = dgLightNovelBooks;
            _lbLightNovelChapters = lbLightNovelChapters;
            _txtLightNovelSelectedChapter = txtLightNovelSelectedChapter;
            _txtLightNovelPlainText = txtLightNovelPlainText;
            _txtLightNovelMarkdown = txtLightNovelMarkdown;
            _txtLightNovelTagUrl = txtHakoTagUrl;
            _txtLightNovelTotalPages = txtHakoTotalPages;
            _txtLightNovelPageFrom = txtHakoPageFrom;
            _txtLightNovelPageTo = txtHakoPageTo;
            _txtLightNovelCount = txtLightNovelCount;

            if (_dgLightNovelBooks != null)
            {
                _dgLightNovelBooks.ItemsSource = _lightNovelItems;
                _dgLightNovelBooks.MouseDoubleClick += DgLightNovelBooks_MouseDoubleClick;
                _dgLightNovelBooks.ContextMenu = BuildLightNovelBookContextMenu();
            }

            if (_lbLightNovelChapters != null)
            {
                _lbLightNovelChapters.ItemsSource = _lightNovelChapterView;
                _lbLightNovelChapters.ContextMenu = BuildLightNovelChapterContextMenu();
            }

            RefreshLightNovelSummary();
            _lightNovelDeskInitialized = true;
        }

        private void EnsureLightNovelDeskInitialized()
        {
            if (_lightNovelDeskInitialized)
            {
                return;
            }

            InitializeLightNovelDesk();
        }

        internal bool AddLightNovelQueueItem(GalleryItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Link))
            {
                return false;
            }

            string key = GetLightNovelItemKey(item);
            if (_lightNovelItems.Any(existing => string.Equals(GetLightNovelItemKey(existing), key, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            item.IsChecked = true;
            item.Status = "Queued";
            item.SourceDomain = item.SourceDomain ?? "ln.hako.vn";
            item.OriginalIndex = _lightNovelItems.Count;
            _lightNovelItems.Add(item);
            RefreshLightNovelSummary();
            return true;
        }

        internal int AddLightNovelQueueItems(IEnumerable<GalleryItem> items)
        {
            if (items == null)
            {
                return 0;
            }

            var existingKeys = new HashSet<string>(
                _lightNovelItems.Select(GetLightNovelItemKey),
                StringComparer.OrdinalIgnoreCase);
            int added = 0;

            foreach (GalleryItem item in items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Link))
                {
                    continue;
                }

                string key = GetLightNovelItemKey(item);
                if (!existingKeys.Add(key))
                {
                    continue;
                }

                item.IsChecked = true;
                item.Status = "Queued";
                item.SourceDomain = item.SourceDomain ?? "ln.hako.vn";
                item.OriginalIndex = _lightNovelItems.Count;
                _lightNovelItems.Add(item);
                added++;
            }

            if (added > 0)
            {
                RefreshLightNovelSummary();
            }

            return added;
        }

        internal void ClearLightNovelQueue()
        {
            CancelLightNovelPreviewWork();
            _lightNovelItems.Clear();
            _lightNovelChapterMap.Clear();
            _lightNovelChapterView.Clear();
            _selectedLightNovelBook = null;
            _selectedLightNovelChapter = null;
            if (_txtLightNovelSelectedChapter != null) _txtLightNovelSelectedChapter.Text = string.Empty;
            if (_txtLightNovelPlainText != null) _txtLightNovelPlainText.Text = string.Empty;
            if (_txtLightNovelMarkdown != null) _txtLightNovelMarkdown.Text = string.Empty;
            RefreshLightNovelSummary();
        }

        internal void ResetLightNovelChapters(GalleryItem item)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => ResetLightNovelChapters(item));
                return;
            }

            if (item == null)
            {
                return;
            }

            _lightNovelChapterMap[GetLightNovelItemKey(item)] = new ObservableCollection<LightNovelChapterRecord>();
            TrackLightNovelChapterAutosave(_lightNovelChapterMap[GetLightNovelItemKey(item)]);
            if (ReferenceEquals(_selectedLightNovelBook, item))
            {
                RefreshLightNovelDetail(item);
            }
        }

        internal void RecordLightNovelChapterSnapshot(GalleryItem item, string chapterTitle, string plainText, string markdownText, string markdownFilePath)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => RecordLightNovelChapterSnapshot(item, chapterTitle, plainText, markdownText, markdownFilePath));
                return;
            }

            if (item == null)
            {
                return;
            }

            string key = GetLightNovelItemKey(item);
            if (!_lightNovelChapterMap.TryGetValue(key, out ObservableCollection<LightNovelChapterRecord> chapters))
            {
                chapters = new ObservableCollection<LightNovelChapterRecord>();
                _lightNovelChapterMap[key] = chapters;
                TrackLightNovelChapterAutosave(chapters);
            }

            LightNovelChapterRecord existing = chapters.FirstOrDefault(record =>
                string.Equals(record.ChapterTitle, chapterTitle, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                chapters.Add(new LightNovelChapterRecord
                {
                    ChapterTitle = chapterTitle,
                    ChapterLink = string.Empty,
                    PlainText = plainText,
                    MarkdownText = markdownText,
                    MarkdownFilePath = markdownFilePath
                });
            }
            else
            {
                existing.PlainText = plainText;
                existing.MarkdownText = markdownText;
                existing.MarkdownFilePath = markdownFilePath;
            }

            if (ReferenceEquals(_selectedLightNovelBook, item))
            {
                RefreshLightNovelDetail(item);
            }

            RefreshLightNovelSummary();
        }

        private string GetLightNovelItemKey(GalleryItem item)
        {
            return item?.Link?.Trim() ?? string.Empty;
        }

        private void RefreshLightNovelSummary()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(RefreshLightNovelSummary);
                return;
            }

            if (_txtLightNovelCount != null)
            {
                int chapterCount = _lightNovelChapterMap.Values.Sum(value => value.Count);
                _txtLightNovelCount.Text = $"Books:{_lightNovelItems.Count} | Chapters:{chapterCount}";
            }

            RequestGalleryListAutosave();
        }

        private void RefreshLightNovelDetail(GalleryItem item)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => RefreshLightNovelDetail(item));
                return;
            }

            _lightNovelChapterView.Clear();
            _selectedLightNovelChapter = null;

            if (item == null)
            {
                if (_txtLightNovelSelectedChapter != null) _txtLightNovelSelectedChapter.Text = string.Empty;
                if (_txtLightNovelPlainText != null) _txtLightNovelPlainText.Text = string.Empty;
                if (_txtLightNovelMarkdown != null) _txtLightNovelMarkdown.Text = string.Empty;
                return;
            }

            if (_lightNovelChapterMap.TryGetValue(GetLightNovelItemKey(item), out ObservableCollection<LightNovelChapterRecord> chapters))
            {
                foreach (LightNovelChapterRecord chapter in chapters)
                {
                    _lightNovelChapterView.Add(chapter);
                }
            }

            if (_lightNovelChapterView.Count == 0)
            {
                if (item == null)
                {
                    if (_txtLightNovelSelectedChapter != null) _txtLightNovelSelectedChapter.Text = string.Empty;
                    if (_txtLightNovelPlainText != null) _txtLightNovelPlainText.Text = string.Empty;
                    if (_txtLightNovelMarkdown != null) _txtLightNovelMarkdown.Text = string.Empty;
                }
            }
        }

        private ContextMenu BuildLightNovelBookContextMenu()
        {
            var menu = CreateLightNovelContextMenu();
            menu.Items.Add(CreateMenuItem("Select all", (s, e) => SelectAllLightNovelBooks()));
            menu.Items.Add(CreateMenuItem("Select none", (s, e) => SelectNoLightNovelBooks()));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("Check selected", (s, e) => SetCheckedForSelectedLightNovelBooks(true)));
            menu.Items.Add(CreateMenuItem("Uncheck selected", (s, e) => SetCheckedForSelectedLightNovelBooks(false)));
            menu.Items.Add(CreateMenuItem("Invert checked", (s, e) => InvertCheckedForLightNovelBooks()));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("Open folder", (s, e) => OpenSelectedLightNovelBookFolders()));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("Merge md book", async (s, e) => await MergeSelectedLightNovelBooksAsync()));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("Copy selected links", (s, e) => CopySelectedLightNovelBookLinks()));
            menu.Items.Add(CreateMenuItem("Delete selected", (s, e) => DeleteSelectedLightNovelBooks()));
            return menu;
        }

        private ContextMenu BuildLightNovelChapterContextMenu()
        {
            var menu = CreateLightNovelContextMenu();
            menu.Items.Add(CreateMenuItem("Select all", (s, e) => SelectAllLightNovelChapters()));
            menu.Items.Add(CreateMenuItem("Select none", (s, e) => SelectNoLightNovelChapters()));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("Check selected", (s, e) => SetCheckedForSelectedLightNovelChapters(true)));
            menu.Items.Add(CreateMenuItem("Uncheck selected", (s, e) => SetCheckedForSelectedLightNovelChapters(false)));
            menu.Items.Add(CreateMenuItem("Invert checked", (s, e) => InvertCheckedForLightNovelChapters()));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("Copy selected links", (s, e) => CopySelectedLightNovelChapterLinks()));
            menu.Items.Add(CreateMenuItem("Delete selected", (s, e) => DeleteSelectedLightNovelChapters()));
            return menu;
        }

        private ContextMenu CreateLightNovelContextMenu()
        {
            return new ContextMenu
            {
                Background = new SolidColorBrush(Color.FromRgb(14, 18, 26)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(31, 41, 61)),
                BorderThickness = new Thickness(1),
                Foreground = (Brush)FindResource("CyberpunkYellowBrush")
            };
        }

        private MenuItem CreateMenuItem(string header, RoutedEventHandler onClick)
        {
            var item = new MenuItem
            {
                Header = header,
                Foreground = (Brush)FindResource("CyberpunkYellowBrush"),
                Background = Brushes.Transparent
            };
            item.Click += onClick;
            return item;
        }

        private void OpenSelectedLightNovelBookFolders()
        {
            List<GalleryItem> selected = _dgLightNovelBooks?.SelectedItems.Cast<GalleryItem>().ToList() ?? new List<GalleryItem>();
            if (selected.Count == 0 && _selectedLightNovelBook != null)
            {
                selected.Add(_selectedLightNovelBook);
            }

            if (selected.Count == 0)
            {
                return;
            }

            string rootFolder = txtDownloadPath?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rootFolder))
            {
                rootFolder = PortablePaths.DefaultDownloadRoot;
            }

            foreach (GalleryItem item in selected)
            {
                string targetFolder = GetLightNovelBookFolderPath(item, item?.Name, rootFolder);
                if (!Directory.Exists(targetFolder))
                {
                    continue;
                }

                if (!ShellFolderLauncher.TryOpenFolder(targetFolder, out string error))
                {
                    HakoLog("Open light novel folder lỗi: " + error);
                }
            }
        }

        internal void DgLightNovelBooks_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedLightNovelBook = _dgLightNovelBooks?.SelectedItem as GalleryItem;
            RefreshLightNovelDetail(_selectedLightNovelBook);

            if (_selectedLightNovelBook != null && _lightNovelCopyCts == null)
            {
                _ = WarmLightNovelChapterCacheAsync(_selectedLightNovelBook);
            }
        }

        private void DgLightNovelBooks_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount < 2)
            {
                return;
            }

            var element = e.OriginalSource as DependencyObject;
            while (element != null && !(element is DataGridRow))
            {
                element = VisualTreeHelper.GetParent(element);
            }

            if (element is DataGridRow row && row.Item is GalleryItem item)
            {
                if (!string.IsNullOrEmpty(item.Link))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = NormalizeHakoUrl(item.Link),
                            UseShellExecute = true
                        });
                        lblStatus.Text = $"Đã mở website: {item.Name}";
                        e.Handled = true;
                    }
                    catch (Exception ex)
                    {
                        HakoLog("Mở website book lỗi: " + ex.Message);
                        lblStatus.Text = "Mở website book thất bại.";
                        ShowWarning("Không mở được website book: " + ex.Message, "Thông báo");
                    }
                }
            }
        }

        internal void DgLightNovelBooks_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (IsTypingInEditableTextBox() || _dgLightNovelBooks == null || _dgLightNovelBooks.Items.Count == 0)
            {
                return;
            }

            if (e.Key == Key.Space)
            {
                ToggleSelectedLightNovelBooksChecked();
                e.Handled = true;
            }
            else if (e.Key == Key.Home)
            {
                SelectLightNovelBookRange(toStart: true, extend: (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift);
                e.Handled = true;
            }
            else if (e.Key == Key.End)
            {
                SelectLightNovelBookRange(toStart: false, extend: (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift);
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                DeleteSelectedLightNovelBooks();
                e.Handled = true;
            }
        }

        internal async void LbLightNovelChapters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedLightNovelChapter = _lbLightNovelChapters?.SelectedItem as LightNovelChapterRecord;

            if (_txtLightNovelSelectedChapter != null)
            {
                _txtLightNovelSelectedChapter.Text = _selectedLightNovelChapter?.ChapterTitle ?? string.Empty;
            }

            if (_txtLightNovelPlainText != null)
            {
                _txtLightNovelPlainText.Text = _selectedLightNovelChapter?.PlainText ?? string.Empty;
            }

            if (_txtLightNovelMarkdown != null)
            {
                _txtLightNovelMarkdown.Text = _selectedLightNovelChapter?.MarkdownText ?? string.Empty;
            }

            if (_selectedLightNovelBook == null || _selectedLightNovelChapter == null)
            {
                return;
            }

            if (_lightNovelCopyCts != null)
            {
                if (_txtLightNovelPlainText != null && string.IsNullOrWhiteSpace(_selectedLightNovelChapter.PlainText))
                {
                    _txtLightNovelPlainText.Text = "Auto copy text đang chạy...";
                }

                if (_txtLightNovelMarkdown != null && string.IsNullOrWhiteSpace(_selectedLightNovelChapter.MarkdownText))
                {
                    _txtLightNovelMarkdown.Text = "Auto copy text đang chạy...";
                }

                return;
            }

            _lightNovelPreviewCts?.Cancel();
            _lightNovelPreviewCts?.Dispose();
            _lightNovelPreviewCts = new CancellationTokenSource();

            try
            {
                await EnsureSelectedLightNovelChapterPreviewAsync(_selectedLightNovelBook, _selectedLightNovelChapter, _lightNovelPreviewCts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                HakoLog("Copy preview lỗi: " + ex.Message);
                lblStatus.Text = "Copy chapter thất bại.";
            }
        }

        internal void LbLightNovelChapters_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (IsTypingInEditableTextBox() || _lbLightNovelChapters == null || _lbLightNovelChapters.Items.Count == 0)
            {
                return;
            }

            if (e.Key == Key.Space)
            {
                ToggleSelectedLightNovelChaptersChecked();
                e.Handled = true;
            }
            else if (e.Key == Key.Home)
            {
                SelectLightNovelChapterRange(toStart: true, extend: (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift);
                e.Handled = true;
            }
            else if (e.Key == Key.End)
            {
                SelectLightNovelChapterRange(toStart: false, extend: (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift);
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                DeleteSelectedLightNovelChapters();
                e.Handled = true;
            }
        }

        private void BtnClearLightNovelQueue_Click(object sender, RoutedEventArgs e)
        {
            ClearLightNovelQueue();
            lblStatus.Text = _isVietnameseUi ? "Đã xóa hàng chờ light novel." : "Light novel queue cleared.";
        }

        private void BtnOpenLightNovelFolder_Click(object sender, RoutedEventArgs e)
        {
            string root = txtDownloadPath?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(root))
            {
                return;
            }

            Directory.CreateDirectory(root);
            if (!ShellFolderLauncher.TryOpenFolder(root, out string error))
            {
                ShowWarning("Không mở được thư mục: " + error, "Thông báo");
            }
        }

        private async void BtnMergeLightNovelBookMarkdown_Click(object sender, RoutedEventArgs e)
        {
            await MergeSelectedLightNovelBooksAsync();
        }

        private void BtnLightNovelPasteDirect_Click(object sender, RoutedEventArgs e)
        {
            var window = new DirectDownloadWindow(
                customTitle: "PASTE HAKO LINKS",
                customDescription: "Paste Hako book links or chapter links below. The system will normalize and import them automatically.",
                customExample: "Example:\nhttps://ln.hako.vn/truyen/23391-bi-kip-sinh-ton-tai-hoc-vien/\nhttps://ln.hako.vn/truyen/23391-bi-kip-sinh-ton-tai-hoc-vien/c227326-chuong-01")
            {
                Owner = this
            };

            window.OnImport = links => _ = ImportLightNovelDirectLinksAsync(links);
            window.ShowDialog();
        }

        private void SetLightNovelChapterList(GalleryItem item, IEnumerable<LightNovelChapterRecord> chapters)
        {
            if (item == null)
            {
                return;
            }

            Dictionary<string, bool> checkedStates = _lightNovelChapterMap.TryGetValue(GetLightNovelItemKey(item), out ObservableCollection<LightNovelChapterRecord> existing)
                ? existing.ToDictionary(
                    record => record.ChapterLink ?? record.ChapterTitle ?? string.Empty,
                    record => record.IsChecked,
                    StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            var records = new ObservableCollection<LightNovelChapterRecord>(
                (chapters ?? Enumerable.Empty<LightNovelChapterRecord>())
                .GroupBy(record => record.ChapterLink ?? record.ChapterTitle ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    LightNovelChapterRecord record = group.First();
                    string key = record.ChapterLink ?? record.ChapterTitle ?? string.Empty;
                    if (checkedStates.TryGetValue(key, out bool wasChecked))
                    {
                        record.IsChecked = wasChecked;
                    }

                    return record;
                }));

            _lightNovelChapterMap[GetLightNovelItemKey(item)] = records;
            TrackLightNovelChapterAutosave(records);

            if (ReferenceEquals(_selectedLightNovelBook, item))
            {
                RefreshLightNovelDetail(item);
            }

            RefreshLightNovelSummary();
        }

        private void SetCheckedForSelectedLightNovelBooks(bool isChecked)
        {
            foreach (GalleryItem item in _dgLightNovelBooks?.SelectedItems.Cast<GalleryItem>().ToList() ?? Enumerable.Empty<GalleryItem>())
            {
                item.IsChecked = isChecked;
            }
        }

        private void InvertCheckedForLightNovelBooks()
        {
            foreach (GalleryItem item in _lightNovelItems)
            {
                item.IsChecked = !item.IsChecked;
            }
        }

        private void ToggleSelectedLightNovelBooksChecked()
        {
            List<GalleryItem> selected = _dgLightNovelBooks?.SelectedItems.Cast<GalleryItem>().ToList() ?? new List<GalleryItem>();
            if (selected.Count == 0)
            {
                return;
            }

            bool shouldCheck = selected.Any(item => !item.IsChecked);
            foreach (GalleryItem item in selected)
            {
                item.IsChecked = shouldCheck;
            }
        }

        private void DeleteSelectedLightNovelBooks()
        {
            List<GalleryItem> selected = _dgLightNovelBooks?.SelectedItems.Cast<GalleryItem>().ToList() ?? new List<GalleryItem>();
            foreach (GalleryItem item in selected)
            {
                _lightNovelChapterMap.Remove(GetLightNovelItemKey(item));
                _lightNovelItems.Remove(item);
            }

            RefreshLightNovelDetail(_dgLightNovelBooks?.SelectedItem as GalleryItem);
            RefreshLightNovelSummary();
        }

        private void CopySelectedLightNovelBookLinks()
        {
            List<string> links = _dgLightNovelBooks?.SelectedItems.Cast<GalleryItem>()
                .Select(item => item.Link)
                .Where(link => !string.IsNullOrWhiteSpace(link))
                .ToList() ?? new List<string>();
            if (links.Count > 0)
            {
                Clipboard.SetText(string.Join("\r\n", links));
            }
        }

        private void SelectAllLightNovelBooks()
        {
            if (_dgLightNovelBooks == null)
            {
                return;
            }

            _dgLightNovelBooks.SelectAll();
        }

        private void SelectNoLightNovelBooks()
        {
            _dgLightNovelBooks?.SelectedItems.Clear();
        }

        private void SelectLightNovelBookRange(bool toStart, bool extend)
        {
            if (_dgLightNovelBooks == null || _dgLightNovelBooks.Items.Count == 0)
            {
                return;
            }

            int targetIndex = toStart ? 0 : _dgLightNovelBooks.Items.Count - 1;
            if (extend)
            {
                int anchorIndex = _dgLightNovelBooks.SelectedIndex < 0 ? 0 : _dgLightNovelBooks.SelectedIndex;
                _dgLightNovelBooks.SelectedItems.Clear();
                int start = Math.Min(anchorIndex, targetIndex);
                int end = Math.Max(anchorIndex, targetIndex);
                for (int i = start; i <= end; i++)
                {
                    _dgLightNovelBooks.SelectedItems.Add(_dgLightNovelBooks.Items[i]);
                }
            }
            else
            {
                _dgLightNovelBooks.SelectedIndex = targetIndex;
            }

            _dgLightNovelBooks.ScrollIntoView(_dgLightNovelBooks.Items[targetIndex]);
        }

        private void SetCheckedForSelectedLightNovelChapters(bool isChecked)
        {
            foreach (LightNovelChapterRecord record in _lbLightNovelChapters?.SelectedItems.Cast<LightNovelChapterRecord>().ToList() ?? Enumerable.Empty<LightNovelChapterRecord>())
            {
                record.IsChecked = isChecked;
            }

            UpdateLightNovelBookCheckedState(_selectedLightNovelBook);
        }

        private void InvertCheckedForLightNovelChapters()
        {
            foreach (LightNovelChapterRecord record in _lightNovelChapterView)
            {
                record.IsChecked = !record.IsChecked;
            }

            UpdateLightNovelBookCheckedState(_selectedLightNovelBook);
        }

        private void ToggleSelectedLightNovelChaptersChecked()
        {
            List<LightNovelChapterRecord> selected = _lbLightNovelChapters?.SelectedItems.Cast<LightNovelChapterRecord>().ToList() ?? new List<LightNovelChapterRecord>();
            if (selected.Count == 0)
            {
                return;
            }

            bool shouldCheck = selected.Any(record => !record.IsChecked);
            foreach (LightNovelChapterRecord record in selected)
            {
                record.IsChecked = shouldCheck;
            }

            UpdateLightNovelBookCheckedState(_selectedLightNovelBook);
        }

        private void DeleteSelectedLightNovelChapters()
        {
            if (_selectedLightNovelBook == null ||
                !_lightNovelChapterMap.TryGetValue(GetLightNovelItemKey(_selectedLightNovelBook), out ObservableCollection<LightNovelChapterRecord> records))
            {
                return;
            }

            List<LightNovelChapterRecord> selected = _lbLightNovelChapters?.SelectedItems.Cast<LightNovelChapterRecord>().ToList() ?? new List<LightNovelChapterRecord>();
            foreach (LightNovelChapterRecord record in selected)
            {
                records.Remove(record);
            }

            RefreshLightNovelDetail(_selectedLightNovelBook);
            UpdateLightNovelBookCheckedState(_selectedLightNovelBook);
            RefreshLightNovelSummary();
        }

        private void CopySelectedLightNovelChapterLinks()
        {
            List<string> links = _lbLightNovelChapters?.SelectedItems.Cast<LightNovelChapterRecord>()
                .Select(record => record.ChapterLink)
                .Where(link => !string.IsNullOrWhiteSpace(link))
                .ToList() ?? new List<string>();
            if (links.Count > 0)
            {
                Clipboard.SetText(string.Join("\r\n", links));
            }
        }

        private void SelectAllLightNovelChapters()
        {
            if (_lbLightNovelChapters == null)
            {
                return;
            }

            _lbLightNovelChapters.SelectedItems.Clear();
            foreach (object item in _lbLightNovelChapters.Items)
            {
                _lbLightNovelChapters.SelectedItems.Add(item);
            }
        }

        private void SelectNoLightNovelChapters()
        {
            _lbLightNovelChapters?.SelectedItems.Clear();
        }

        private void SelectLightNovelChapterRange(bool toStart, bool extend)
        {
            if (_lbLightNovelChapters == null || _lbLightNovelChapters.Items.Count == 0)
            {
                return;
            }

            int targetIndex = toStart ? 0 : _lbLightNovelChapters.Items.Count - 1;
            if (extend)
            {
                int anchorIndex = _lbLightNovelChapters.SelectedIndex < 0 ? 0 : _lbLightNovelChapters.SelectedIndex;
                _lbLightNovelChapters.SelectedItems.Clear();
                int start = Math.Min(anchorIndex, targetIndex);
                int end = Math.Max(anchorIndex, targetIndex);
                for (int i = start; i <= end; i++)
                {
                    _lbLightNovelChapters.SelectedItems.Add(_lbLightNovelChapters.Items[i]);
                }
            }
            else
            {
                _lbLightNovelChapters.SelectedIndex = targetIndex;
            }

            _lbLightNovelChapters.ScrollIntoView(_lbLightNovelChapters.Items[targetIndex]);
        }

        private async Task WarmLightNovelChapterCacheAsync(GalleryItem item)
        {
            if (item == null)
            {
                return;
            }

            CancellationToken token = ResetLightNovelWarmupCancellationToken();

            string key = GetLightNovelItemKey(item);
            lock (_lightNovelChapterWarmupInFlight)
            {
                if (_lightNovelChapterWarmupInFlight.Contains(key))
                {
                    return;
                }

                _lightNovelChapterWarmupInFlight.Add(key);
            }

            try
            {
                if (_lightNovelChapterMap.TryGetValue(key, out ObservableCollection<LightNovelChapterRecord> cached) &&
                    cached.Count > 0 &&
                    !string.IsNullOrWhiteSpace(cached[0].ChapterLink))
                {
                    return;
                }

                Dispatcher.Invoke(() =>
                {
                    if (ReferenceEquals(_selectedLightNovelBook, item))
                    {
                        lblStatus.Text = $"Đang tải danh sách chapter cho {item.Name}";
                        if (_txtLightNovelSelectedChapter != null)
                        {
                            _txtLightNovelSelectedChapter.Text = "Đang tải danh sách chapter...";
                        }
                    }
                });

                List<LightNovelChapterRecord> loaded = await BuildLightNovelChapterRecordsAsync(item, token, firecrawlOnly: false);
                if (loaded.Count == 0)
                {
                    loaded = await BuildLightNovelChapterRecordsAsync(item, token, firecrawlOnly: true);
                }
                token.ThrowIfCancellationRequested();
                Dispatcher.Invoke(() => SetLightNovelChapterList(item, loaded));

                Dispatcher.Invoke(() =>
                {
                    if (ReferenceEquals(_selectedLightNovelBook, item))
                    {
                        lblStatus.Text = loaded.Count > 0
                            ? $"Đã tải {loaded.Count} chapter cho {item.Name}"
                            : $"Không tìm thấy chapter cho {item.Name}";
                        if (_txtLightNovelSelectedChapter != null && _lightNovelChapterView.Count == 0)
                        {
                            _txtLightNovelSelectedChapter.Text = string.Empty;
                        }
                    }
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                HakoLog("Warm chapter cache lỗi: " + ex.Message);
            }
            finally
            {
                lock (_lightNovelChapterWarmupInFlight)
                {
                    _lightNovelChapterWarmupInFlight.Remove(key);
                }
            }
        }

        private async Task<List<LightNovelChapterRecord>> BuildLightNovelChapterRecordsAsync(GalleryItem item, CancellationToken token, bool firecrawlOnly = false)
        {
            if (item == null)
            {
                return new List<LightNovelChapterRecord>();
            }

            if (TryParseHakoChapterUrl(item.Link, out _, out _, out _, out _, out string canonicalChapterUrl))
            {
                string chapterTitle = string.IsNullOrWhiteSpace(item.LinkCount) ? item.Name : item.LinkCount;
                return new List<LightNovelChapterRecord>
                {
                    new LightNovelChapterRecord
                    {
                        ChapterTitle = CompactSingleLine(chapterTitle),
                        ChapterLink = canonicalChapterUrl,
                        SequenceIndex = 1,
                        IsChecked = item.IsChecked
                    }
                };
            }

            string html = null;
            List<string> firecrawlLinks = null;
            string normalizedItemLink = NormalizeHakoUrl(item.Link);
            if (firecrawlOnly)
            {
                FirecrawlPageSnapshot page = await TryFetchHakoPageByFirecrawlAsync(normalizedItemLink, token, preferFastChapterList: true);
                html = page?.Html;
                firecrawlLinks = page?.Links;
            }
            else
            {
                html = await FetchHakoHtmlAsync(normalizedItemLink, token);
            }
            if (string.IsNullOrWhiteSpace(html))
            {
                html = string.Empty;
            }
            string detectedTitle = ExtractHakoBookTitle(html);
            if (!string.IsNullOrWhiteSpace(detectedTitle))
            {
                item.Name = FormatGalleryTitle(detectedTitle);
            }

            List<HakoChapterInfo> chapters = ExtractHakoChapterLinks(html, normalizedItemLink);
            if ((chapters == null || chapters.Count == 0) && !firecrawlOnly)
            {
                try
                {
                    string browserHtml = await FetchHakoHtmlViaBrowserAsync(normalizedItemLink, headlessAutomation: true);
                    if (!string.IsNullOrWhiteSpace(browserHtml) && !IsHakoChallengeHtml(browserHtml))
                    {
                        chapters = ExtractHakoChapterLinks(browserHtml, normalizedItemLink);
                        if (chapters != null && chapters.Count > 0)
                        {
                            html = browserHtml;
                        }
                    }
                }
                catch
                {
                }
            }

            if ((chapters == null || chapters.Count == 0) && (firecrawlLinks == null || firecrawlLinks.Count == 0))
            {
                try
                {
                    FirecrawlPageSnapshot page = await TryFetchHakoPageByFirecrawlAsync(normalizedItemLink, token, preferFastChapterList: true);
                    if (page != null)
                    {
                        firecrawlLinks = page.Links;
                        if (!string.IsNullOrWhiteSpace(page.Html))
                        {
                            List<HakoChapterInfo> firecrawlHtmlChapters = ExtractHakoChapterLinks(page.Html, normalizedItemLink);
                            if (firecrawlHtmlChapters.Count > 0)
                            {
                                chapters = firecrawlHtmlChapters;
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            if ((chapters == null || chapters.Count == 0) && firecrawlLinks != null && firecrawlLinks.Count > 0)
            {
                chapters = ExtractHakoChapterLinksFromFirecrawlLinks(firecrawlLinks, normalizedItemLink);
            }

            if (chapters == null)
            {
                return new List<LightNovelChapterRecord>();
            }

            return chapters
                .Select(chapter => new LightNovelChapterRecord
                {
                    ChapterTitle = CompactSingleLine(chapter.Title),
                    ChapterLink = chapter.Link,
                    VolumeTitle = chapter.VolumeTitle,
                    VolumeOrder = chapter.VolumeOrder,
                    SequenceIndex = chapter.SequenceIndex,
                    IsChecked = item.IsChecked
                })
                .ToList();
        }

        private List<HakoChapterInfo> ExtractHakoChapterLinksFromFirecrawlLinks(IEnumerable<string> links, string bookUrl)
        {
            var chapters = new List<HakoChapterInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Uri baseUri = new Uri(NormalizeHakoUrl(bookUrl));
            string basePath = baseUri.AbsolutePath.TrimEnd('/');
            var indexedChapters = new List<(HakoChapterInfo Chapter, long ChapterId, int OriginalIndex)>();
            int originalIndex = 0;

            foreach (string rawLink in links ?? Enumerable.Empty<string>())
            {
                string link = NormalizeHakoUrl(rawLink);
                if (string.IsNullOrWhiteSpace(link))
                {
                    continue;
                }

                if (!TryParseHakoChapterUrl(link, out _, out _, out _, out _, out string canonicalChapterUrl))
                {
                    continue;
                }

                link = canonicalChapterUrl;
                Uri chapterUri = new Uri(link);
                if (!chapterUri.AbsolutePath.StartsWith(basePath + "/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!seen.Add(link))
                {
                    continue;
                }

                string chapterSlug = chapterUri.Segments.LastOrDefault()?.Trim('/');
                string title = HumanizeHakoSlug(chapterSlug);
                var chapter = new HakoChapterInfo
                {
                    BookTitle = ExtractHakoBookTitle(string.Empty),
                    Link = link,
                    Title = string.IsNullOrWhiteSpace(title) ? link : title,
                    ChapterNumber = TryExtractHakoChapterNumber(title, link)
                };

                indexedChapters.Add((chapter, TryExtractHakoChapterId(link), originalIndex++));
            }

            foreach (var entry in indexedChapters
                .OrderBy(entry => entry.ChapterId <= 0 ? long.MaxValue : entry.ChapterId)
                .ThenBy(entry => entry.OriginalIndex))
            {
                entry.Chapter.SequenceIndex = chapters.Count + 1;
                chapters.Add(entry.Chapter);
            }

            return chapters;
        }

        private async Task ImportLightNovelDirectLinksAsync(List<string> links)
        {
            if (links == null || links.Count == 0)
            {
                return;
            }

            int success = 0;
            int failed = 0;
            int total = links.Count;

            try
            {
                for (int i = 0; i < total; i++)
                {
                    string rawLink = links[i].Trim();
                    lblStatus.Text = $"[{i + 1}/{total}] Importing {rawLink}";

                    try
                    {
                        GalleryItem item = await BuildHakoDirectGalleryItemAsync(rawLink);
                        bool added = AddLightNovelQueueItem(item);
                        GalleryItem targetItem = added
                            ? item
                            : _lightNovelItems.FirstOrDefault(existing => string.Equals(existing.Link, item.Link, StringComparison.OrdinalIgnoreCase));

                        if (targetItem != null)
                        {
                            _ = WarmLightNovelChapterCacheAsync(targetItem);
                            if (_dgLightNovelBooks != null)
                            {
                                _dgLightNovelBooks.SelectedItem = targetItem;
                            }
                        }

                        success++;
                        lblStatus.Text = $"[{i + 1}/{total}] Imported {item.Name}";
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        HakoLog($"[Import] Lỗi với '{rawLink}': {ex.Message}");
                    }
                }

                lblStatus.Text = $"Import completed. Success: {success}, Failed: {failed}.";
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Import failed.";
                HakoLog("Import light novel lỗi: " + ex.Message);
            }
        }

        private async Task CopyLightNovelChapterAsync(GalleryItem item, LightNovelChapterRecord record, string rootFolder, CancellationToken token)
        {
            if (item == null || record == null || string.IsNullOrWhiteSpace(record.ChapterLink))
            {
                return;
            }

            string chapterUrl = NormalizeHakoUrl(record.ChapterLink);
            lblStatus.Text = $"Đang lấy chapter {record.ChapterTitle}";
            string chapterHtml = await FetchHakoChapterHtmlAsync(chapterUrl, token, allowWebViewFallback: false);
            token.ThrowIfCancellationRequested();
            if (IsHakoTooManyRequestsHtml(chapterHtml))
            {
                throw new InvalidOperationException("429 too many requests");
            }
            if (IsHakoForbiddenChapterHtml(chapterHtml))
            {
                throw new InvalidOperationException("403 chapter forbidden");
            }
            string chapterTitle = ExtractHakoTitleTopText(chapterHtml);
            if (string.IsNullOrWhiteSpace(chapterTitle))
            {
                chapterTitle = record.ChapterTitle;
            }

            string bookTitle = string.IsNullOrWhiteSpace(item.Name)
                ? string.Empty
                : item.Name.Trim();
            string extractedBookTitle = ExtractHakoBookTitleFromChapterHtml(chapterHtml, chapterUrl);
            if ((string.IsNullOrWhiteSpace(bookTitle) || IsInvalidHakoBookTitle(bookTitle)) &&
                !string.IsNullOrWhiteSpace(extractedBookTitle))
            {
                bookTitle = extractedBookTitle;
                item.Name = FormatGalleryTitle(extractedBookTitle);
            }

            if (string.IsNullOrWhiteSpace(bookTitle) &&
                TryParseHakoChapterUrl(chapterUrl, out _, out string bookSlug, out _, out _, out _))
            {
                bookTitle = HumanizeHakoSlug(bookSlug);
            }

            string plainText = BuildHakoChapterPlainText(chapterHtml);
            EnsureHakoChapterHasText(plainText);
            var chapterInfo = new HakoChapterInfo
            {
                BookTitle = bookTitle,
                Title = chapterTitle,
                Link = chapterUrl,
                ChapterNumber = TryExtractHakoChapterNumber(chapterTitle, chapterUrl)
            };
            string markdown = BuildHakoChapterMarkdown(item, chapterInfo, chapterHtml);
            token.ThrowIfCancellationRequested();

            string targetFolder = GetLightNovelBookFolderPath(item, bookTitle, rootFolder);
            Directory.CreateDirectory(targetFolder);

            string normalizedChapterTitle = CompactSingleLine(chapterTitle);
            string chapterFilePath = GetLightNovelChapterMarkdownPath(item, record, rootFolder, bookTitle, normalizedChapterTitle);
            token.ThrowIfCancellationRequested();
            File.WriteAllText(chapterFilePath, markdown, new System.Text.UTF8Encoding(true));

            record.ChapterTitle = normalizedChapterTitle;
            record.PlainText = plainText;
            record.MarkdownText = markdown;
            record.MarkdownFilePath = chapterFilePath;
            RecordLightNovelChapterSnapshot(item, normalizedChapterTitle, plainText, markdown, chapterFilePath);
        }

        private async Task EnsureSelectedLightNovelChapterPreviewAsync(GalleryItem item, LightNovelChapterRecord record, CancellationToken token)
        {
            if (item == null || record == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(record.ChapterLink))
            {
                if (ReferenceEquals(_selectedLightNovelChapter, record))
                {
                    _txtLightNovelPlainText.Text = string.Empty;
                    _txtLightNovelMarkdown.Text = string.Empty;
                }

                return;
            }

            if (!string.IsNullOrWhiteSpace(record.PlainText) && !string.IsNullOrWhiteSpace(record.MarkdownText))
            {
                if (ReferenceEquals(_selectedLightNovelChapter, record))
                {
                    _txtLightNovelPlainText.Text = record.PlainText;
                    _txtLightNovelMarkdown.Text = record.MarkdownText;
                }
                return;
            }

            if (_txtLightNovelPlainText != null)
            {
                _txtLightNovelPlainText.Text = "Đang copy text chapter...";
            }

            if (_txtLightNovelMarkdown != null)
            {
                _txtLightNovelMarkdown.Text = "Đang convert .md...";
            }

            lblStatus.Text = $"Đang copy {record.ChapterTitle}";

            string rootFolder = txtDownloadPath?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rootFolder))
            {
                rootFolder = PortablePaths.DefaultDownloadRoot;
            }

            try
            {
                await CopyLightNovelChapterAsync(item, record, rootFolder, token);
            }
            catch (Exception ex) when (IsSkippableHakoChapterError(ex))
            {
                if (ReferenceEquals(_selectedLightNovelChapter, record))
                {
                    _txtLightNovelSelectedChapter.Text = record.ChapterTitle ?? string.Empty;
                    _txtLightNovelPlainText.Text = "[SKIP] Chapter không lấy được text, bỏ qua.";
                    _txtLightNovelMarkdown.Text = string.Empty;
                }

                lblStatus.Text = $"Skip {record.ChapterTitle}";
                return;
            }
            token.ThrowIfCancellationRequested();

            if (ReferenceEquals(_selectedLightNovelChapter, record))
            {
                _txtLightNovelSelectedChapter.Text = record.ChapterTitle ?? string.Empty;
                _txtLightNovelPlainText.Text = record.PlainText ?? string.Empty;
                _txtLightNovelMarkdown.Text = record.MarkdownText ?? string.Empty;
                lblStatus.Text = $"Đã copy xong {record.ChapterTitle}";
            }
        }

        private async Task CopyLightNovelItemAsync(GalleryItem item, string rootFolder, CancellationToken token)
        {
            if (item == null)
            {
                return;
            }

            if (!_lightNovelChapterMap.TryGetValue(GetLightNovelItemKey(item), out ObservableCollection<LightNovelChapterRecord> chapters) ||
                chapters.Count == 0)
            {
                List<LightNovelChapterRecord> loaded = await BuildLightNovelChapterRecordsAsync(item, token, firecrawlOnly: false);
                if (loaded.Count == 0)
                {
                    loaded = await BuildLightNovelChapterRecordsAsync(item, token, firecrawlOnly: true);
                }
                SetLightNovelChapterList(item, loaded);
                chapters = _lightNovelChapterMap.TryGetValue(GetLightNovelItemKey(item), out ObservableCollection<LightNovelChapterRecord> created)
                    ? created
                    : new ObservableCollection<LightNovelChapterRecord>();
            }

            List<LightNovelChapterRecord> completedChapters = chapters
                .Where(chapter => HasLightNovelChapterMarkdownOnDisk(item, chapter, rootFolder))
                .ToList();

            List<LightNovelChapterRecord> chaptersToCopy = chapters
                .Where(chapter => chapter.IsChecked && !completedChapters.Contains(chapter))
                .ToList();

            if (chaptersToCopy.Count == 0 && chapters.Count > 0 && completedChapters.Count == 0)
            {
                foreach (LightNovelChapterRecord chapter in chapters)
                {
                    chapter.IsChecked = true;
                }

                item.IsChecked = true;
                chaptersToCopy = chapters.ToList();
                UpdateLightNovelBookCheckedState(item);
            }

            if (chaptersToCopy.Count == 0)
            {
                int completedCount = completedChapters.Count;
                item.TotalChapters = completedCount;
                item.CompletedChapters = completedCount;
                item.CurrentProcess = $"{completedCount}/{completedCount} chapters";
                item.IsChecked = false;
                return;
            }

            int completedBeforeStart = completedChapters.Count;
            int totalChapters = completedBeforeStart + chaptersToCopy.Count;
            item.TotalChapters = totalChapters;
            item.CompletedChapters = completedBeforeStart;
            item.CurrentProcess = $"{completedBeforeStart}/{totalChapters} chapters";

            for (int index = 0; index < chaptersToCopy.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                LightNovelChapterRecord chapter = chaptersToCopy[index];
                item.DownloadingChapter = chapter.ChapterTitle;
                item.DownloadingPageProgress = $"{completedBeforeStart + index + 1}/{totalChapters}";
                item.CurrentProcess = $"{completedBeforeStart + index}/{totalChapters} chapters";

                try
                {
                    await CopyLightNovelChapterAsync(item, chapter, rootFolder, token);
                }
                catch (Exception ex) when (IsHakoRateLimitError(ex))
                {
                    _lightNovelCopyBackoffActive = true;
                    item.Status = "RateLimit";
                    item.CurrentProcess = "429 wait 10s";
                    lblStatus.Text = $"429 ở {chapter.ChapterTitle}. Nghỉ 10 giây rồi thử lại...";
                    HakoLog($"429 chapter: {chapter.ChapterTitle} - {chapter.ChapterLink}. Wait 10s then retry.");
                    UpdateLightNovelFloatingControlState();
                    await Task.Delay(TimeSpan.FromSeconds(10), token);
                    _lightNovelCopyBackoffActive = false;
                    item.Status = "Copying";
                    item.CurrentProcess = $"{completedBeforeStart + index}/{totalChapters} chapters";
                    lblStatus.Text = $"Thử lại {chapter.ChapterTitle} sau 429...";
                    UpdateLightNovelFloatingControlState();
                    index--;
                    continue;
                }
                catch (Exception ex) when (IsSkippableHakoChapterError(ex))
                {
                    item.ErrorCount++;
                    item.AddError(item.Name, 0, $"Skip chapter: {chapter.ChapterTitle}");
                    item.CurrentProcess = $"Skip {completedBeforeStart + index + 1}/{totalChapters}";
                    HakoLog($"Skip chapter: {chapter.ChapterTitle} - {chapter.ChapterLink} - {ex.Message}");
                    chapter.IsChecked = false;
                    UpdateLightNovelBookCheckedState(item);
                    continue;
                }

                item.CompletedChapters = completedBeforeStart + index + 1;
                item.CurrentProcess = $"{completedBeforeStart + index + 1}/{totalChapters} chapters";
                chapter.IsChecked = false;
                UpdateLightNovelBookCheckedState(item);
            }
        }

        private string GetLightNovelBookFolderPath(GalleryItem item, string bookTitle, string rootFolder)
        {
            string resolvedRoot = GetConfiguredDownloadRoot(rootFolder, item);
            string preferredBookTitle = string.IsNullOrWhiteSpace(bookTitle) ? item?.Name : bookTitle;
            string safeBookTitle = GetSafePathName(CompactSingleLine(preferredBookTitle), 72);
            if (string.IsNullOrWhiteSpace(safeBookTitle))
            {
                safeBookTitle = GetCanonicalBookFolderName(item, bookTitle, "hako-book", 72);
            }
            return Path.Combine(resolvedRoot, safeBookTitle);
        }

        private string GetLightNovelChapterMarkdownPath(GalleryItem item, LightNovelChapterRecord record, string rootFolder, string bookTitle = null, string chapterTitle = null)
        {
            if (item == null || record == null)
            {
                return string.Empty;
            }

            string normalizedBookTitle = string.IsNullOrWhiteSpace(bookTitle) ? item.Name : bookTitle;
            string normalizedChapterTitle = CompactSingleLine(string.IsNullOrWhiteSpace(chapterTitle) ? record.ChapterTitle : chapterTitle);
            string targetFolder = GetLightNovelBookFolderPath(item, normalizedBookTitle, rootFolder);
            string chapterFolder = GetHakoVolumeFolderPath(targetFolder, record.VolumeTitle, record.VolumeOrder);
            int displayChapterIndex = BuildHakoSingleChapterDisplayIndex(normalizedChapterTitle, record.ChapterLink, record.SequenceIndex);
            return Path.Combine(chapterFolder, BuildHakoChapterFileName(normalizedChapterTitle, record.SequenceIndex, displayChapterIndex));
        }

        private bool HasLightNovelChapterMarkdownOnDisk(GalleryItem item, LightNovelChapterRecord record, string rootFolder)
        {
            string chapterFilePath = !string.IsNullOrWhiteSpace(record?.MarkdownFilePath)
                ? record.MarkdownFilePath
                : GetLightNovelChapterMarkdownPath(item, record, rootFolder);

            // ponytail: resume trust existing markdown file as completed chapter; upgrade to checksum/content validation if partial-file bugs appear.
            if (string.IsNullOrWhiteSpace(chapterFilePath) || !File.Exists(chapterFilePath))
            {
                return false;
            }

            record.MarkdownFilePath = chapterFilePath;
            return true;
        }

        private async void BtnLightNovelAnalyze_Click(object sender, RoutedEventArgs e)
        {
            string rawUrl = _txtLightNovelTagUrl?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                ShowWarning("Vui lòng nhập URL tag của Hako.", "Thông báo");
                return;
            }

            try
            {
                lblStatus.Text = "Đang phân tích trang Hako...";
                string normalizedUrl = NormalizeHakoUrl(rawUrl);
                string html = await FetchHakoHtmlAsync(normalizedUrl, CancellationToken.None);
                int totalPages = ExtractHakoMaxPage(html, normalizedUrl);

                _txtLightNovelTagUrl.Text = normalizedUrl;
                _txtLightNovelTotalPages.Text = totalPages.ToString(CultureInfo.InvariantCulture);
                _txtLightNovelPageTo.Text = totalPages.ToString(CultureInfo.InvariantCulture);
                lblStatus.Text = $"Phân tích xong. Phát hiện {totalPages} trang.";
            }
            catch (Exception ex)
            {
                _txtLightNovelTotalPages.Text = "1";
                _txtLightNovelPageTo.Text = "1";
                lblStatus.Text = "Phân tích thất bại.";
                HakoLog("Lỗi khi phân tích light novel: " + ex.Message);
            }
        }

        private async void BtnLightNovelGetLink_Click(object sender, RoutedEventArgs e)
        {
            SelectDownloadNovelTab();
            await ScrapeLightNovelAsync(true);
        }

        private async void BtnLightNovelGetMore_Click(object sender, RoutedEventArgs e)
        {
            SelectDownloadNovelTab();
            await ScrapeLightNovelAsync(false);
        }

        private async Task ScrapeLightNovelAsync(bool clearExisting)
        {
            if (_lightNovelScrapeCts != null)
            {
                _lightNovelScrapeCts.Cancel();
                return;
            }

            string rawUrl = _txtLightNovelTagUrl?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                ShowWarning("Vui lòng nhập URL tag của Hako.", "Thông báo");
                return;
            }

            if (!int.TryParse(_txtLightNovelPageFrom?.Text, out int pageFrom) || pageFrom < 1)
            {
                ShowWarning("Trang bắt đầu không hợp lệ.", "Thông báo");
                return;
            }

            if (!int.TryParse(_txtLightNovelPageTo?.Text, out int pageTo) || pageTo < pageFrom)
            {
                ShowWarning("Trang kết thúc không hợp lệ.", "Thông báo");
                return;
            }

            _lightNovelScrapeCts = new CancellationTokenSource();
            CancellationToken token = _lightNovelScrapeCts.Token;

            try
            {
                if (clearExisting)
                {
                    ClearLightNovelQueue();
                }

                string baseUrl = NormalizeHakoUrl(rawUrl);
                _txtLightNovelTagUrl.Text = baseUrl;

                int totalPages = pageTo - pageFrom + 1;
                int totalAdded = 0;
                for (int page = pageFrom; page <= pageTo; page++)
                {
                    token.ThrowIfCancellationRequested();
                    string pageUrl = GetHakoTagPageUrl(baseUrl, page);
                    string html = await FetchHakoHtmlAsync(pageUrl, token);
                    int addedThisPage = AddLightNovelQueueItems(ParseHakoGalleryItemsFromHtml(html));
                    totalAdded += addedThisPage;

                    double progress = ((double)(page - pageFrom + 1) / totalPages) * 100;
                    lblStatus.Text = $"Đang quét trang {page}/{pageTo} ({progress:0}%) - +{totalAdded}";
                    await Task.Yield();
                }

                RefreshLightNovelSummary();
                lblStatus.Text = $"Cào Hako hoàn tất. Đã thêm {totalAdded} item.";
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = "Đã hủy cào Hako.";
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Cào Hako thất bại.";
                HakoLog("Lỗi khi cào light novel: " + ex.Message);
            }
            finally
            {
                _lightNovelScrapeCts.Dispose();
                _lightNovelScrapeCts = null;
            }
        }

        private async void BtnStartLightNovelCopy_Click(object sender, RoutedEventArgs e)
        {
            await StartLightNovelAutoCopyAsync();
        }

        internal void BtnStartLightNovelCopyToggle_Click(object sender, RoutedEventArgs e)
        {
            if (btnStartCopyText?.IsChecked == true)
            {
                _ = StartLightNovelAutoCopyAsync();
                return;
            }

            StopLightNovelAutoCopy();
            UpdateCompactDownloadToolbarState();
        }

        private void BtnShowLightNovelFloatButton_Click(object sender, RoutedEventArgs e)
        {
            if (_lightNovelFocusTrayHidden)
            {
                RestoreMainWindowFromFocusTray(activateWindow: true);
            }

            ToggleLightNovelFloatingControlWindow();
        }

        private void ToggleLightNovelFloatingControlWindow()
        {
            EnsureLightNovelFloatingControlWindow();
            if (_lightNovelFloatingControlWindow == null)
            {
                return;
            }

            if (_lightNovelFloatingControlWindow.IsVisible)
            {
                _lightNovelFloatingControlWindow.PrepareForTrayHide();
                _lightNovelFloatingControlWindow.Hide();
                lblStatus.Text = "Đã tắt float auto copy text.";
            }
            else
            {
                if (_lightNovelFloatingControlWindow.WindowState == WindowState.Minimized)
                {
                    _lightNovelFloatingControlWindow.WindowState = WindowState.Normal;
                }

                _lightNovelFloatingControlWindow.ShowWithoutActivationSafe();
                lblStatus.Text = "Đã mở float auto copy text.";
            }

            UpdateLightNovelFloatingControlState();
        }

        private async System.Threading.Tasks.Task StartLightNovelAutoCopyAsync()
        {
            EnsureLightNovelDeskInitialized();
            if (_lightNovelCopyCts != null)
            {
                lblStatus.Text = _isVietnameseUi
                    ? "Auto copy text đang chạy. Dùng float button để điều khiển."
                    : "Auto copy text already running. Use floating button to control it.";
                UpdateLightNovelFloatingControlState();
                return;
            }

            string rootFolder = txtDownloadPath?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rootFolder))
            {
                ShowWarning("Vui lòng chọn thư mục lưu trước.", "Thông báo");
                return;
            }

            List<GalleryItem> itemsToCopy = _lightNovelItems.Where(item => item.IsChecked).ToList();
            if (itemsToCopy.Count == 0 && _selectedLightNovelBook != null)
            {
                itemsToCopy.Add(_selectedLightNovelBook);
            }

            if (itemsToCopy.Count == 0)
            {
                ShowWarning("Chưa có book light novel nào để copy text.", "Thông báo");
                return;
            }

            CancelLightNovelPreviewWork();
            _lightNovelCopyCts = new CancellationTokenSource();
            CancellationToken token = _lightNovelCopyCts.Token;
            EnsureLightNovelFloatingControlWindow();
            UpdateLightNovelFocusVisibilityState();
            UpdateLightNovelFloatingControlState();

            try
            {
                lblStatus.Text = _isVietnameseUi ? "Đang copy text light novel..." : "Copying light novel text...";

                foreach (GalleryItem item in itemsToCopy)
                {
                    token.ThrowIfCancellationRequested();
                    item.Errors.Clear();
                    item.ErrorCount = 0;
                    item.Status = "Copying";
                    item.CurrentProcess = "Preparing chapters";

                    try
                    {
                        await CopyLightNovelItemAsync(item, rootFolder, token);
                        item.Status = "Completed";
                        item.DownloadPath = ResolveBestFolderForGalleryItem(item);
                        await RefreshReaderNovelLibraryAsync(forceRefresh: true);
                    }
                    catch (OperationCanceledException)
                    {
                        item.Status = "Cancelled";
                        item.CurrentProcess = "Cancelled";
                        throw;
                    }
                    catch (Exception ex)
                    {
                        item.Status = "Error";
                        item.CurrentProcess = ex.Message;
                        item.AddError(item.Name, 0, ex.Message);
                        HakoLog("Copy text lỗi: " + ex.Message);
                    }
                }

                lblStatus.Text = _isVietnameseUi ? "Copy text light novel hoàn tất." : "Light novel text copy completed.";
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = _isVietnameseUi ? "Đã dừng copy text light novel." : "Light novel text copy cancelled.";
            }
            finally
            {
                _lightNovelCopyCts.Dispose();
                _lightNovelCopyCts = null;
                _lightNovelCopyBackoffActive = false;
                UpdateLightNovelFocusVisibilityState();
                UpdateLightNovelFloatingControlState();
                RefreshLightNovelSummary();
            }
        }

        private void BtnStopLightNovelCopy_Click(object sender, RoutedEventArgs e)
        {
            StopLightNovelAutoCopy();
        }

        private void StopLightNovelAutoCopy()
        {
            if (_lightNovelCopyCts == null)
            {
                lblStatus.Text = _isVietnameseUi
                    ? "Chưa có tiến trình auto copy text để dừng."
                    : "No auto copy text process is running.";
                UpdateLightNovelFloatingControlState();
                return;
            }

            CancelLightNovelPreviewWork();
            _lightNovelCopyCts.Cancel();
            lblStatus.Text = _isVietnameseUi
                ? "Đang dừng auto copy text..."
                : "Stopping auto copy text...";
        }

        private void EnsureLightNovelFloatingControlWindow()
        {
            if (_lightNovelFloatingControlWindow != null)
            {
                return;
            }

            _lightNovelFloatingControlWindow = new SystemFloatingControlWindow(
                _isVietnameseUi,
                () => Dispatcher.BeginInvoke(new Action(async () => await StartLightNovelAutoCopyAsync())),
                () => Dispatcher.BeginInvoke(new Action(StopLightNovelAutoCopy)),
                () => Dispatcher.BeginInvoke(new Action(async () => await StartPictureDownloadFromFloatingAsync())),
                () => Dispatcher.BeginInvoke(new Action(StopPictureDownloadFromFloating)),
                enabled => Dispatcher.BeginInvoke(new Action(() => SetPictureAutoRetryFromFloating(enabled))),
                () => Dispatcher.BeginInvoke(new Action(ToggleGlobalAutoPasteClipboard)),
                () => Dispatcher.BeginInvoke(new Action(OpenShutdownOptionsPopup)),
                () => Dispatcher.BeginInvoke(new Action(ToggleLightNovelAutoFocus)),
                () => Dispatcher.BeginInvoke(new Action(() => BtnOpenLightNovelFolder_Click(this, new RoutedEventArgs()))),
                () => Dispatcher.BeginInvoke(new Action(async () => await ResetActiveCaptchaFromFloatingAsync())),
                () => Dispatcher.BeginInvoke(new Action(() => BtnClearTemp_Click(this, new RoutedEventArgs()))),
                () => Dispatcher.BeginInvoke(new Action(ToggleLightNovelGlobalHotkeys)),
                url => Dispatcher.BeginInvoke(new Action(async () => await AppendSupportedInputLinks(url))),
                index => Dispatcher.BeginInvoke(new Action(() => {
                    _suppressDownloadFolderTypeEvents = true;
                    try
                    {
                        cmbDownloadFolderType.SelectedIndex = index;
                        _isSingleComicFolderType = (index == 0);
                        Log($"[Folder Type] Đã đồng bộ từ float: {(_isSingleComicFolderType ? "Single comic" : "Multi-comic")}");
                    }
                    finally
                    {
                        _suppressDownloadFolderTypeEvents = false;
                    }
                })),
                index => Dispatcher.BeginInvoke(new Action(() => {
                    _suppressConnectionEvents = true;
                    try
                    {
                        cmbConnections.SelectedIndex = index;
                    }
                    finally
                    {
                        _suppressConnectionEvents = false;
                    }
                })),
                index => Dispatcher.BeginInvoke(new Action(() => {
                    _suppressMultiDownloadEvents = true;
                    try
                    {
                        cmbMultiDownload.SelectedIndex = index;
                    }
                    finally
                    {
                        _suppressMultiDownloadEvents = false;
                    }
                })));

            _lightNovelFloatingControlWindow.UpdateFolderType(cmbDownloadFolderType.SelectedIndex);
            _lightNovelFloatingControlWindow.UpdateConnections(cmbConnections.SelectedIndex);
            _lightNovelFloatingControlWindow.UpdateMultiDownload(cmbMultiDownload.SelectedIndex);

            _lightNovelFloatingControlWindow.Closed += (sender, args) =>
            {
                _lightNovelFloatingControlWindow = null;
            };
        }

        private void UpdateLightNovelFloatingControlState()
        {
            _lightNovelFloatingControlWindow?.UpdateState(
                _lightNovelCopyCts != null && !_lightNovelCopyBackoffActive,
                _lightNovelAutoFocusEnabled,
                _downloadCts != null,
                btnAutoRetryErrors?.IsChecked == true,
                _shutdownAfterCompleted,
                _globalAutoPasteEnabled,
                _lightNovelGlobalHotkeysEnabled,
                BuildInfo.DisplayText,
                _isVietnameseUi);

            UpdateCompactDownloadToolbarState();
        }

        private void UpdateLightNovelFocusVisibilityState()
        {
            _lightNovelFocusRestoreOverride = false;
            if (_lightNovelFocusTrayHidden)
            {
                RestoreMainWindowFromFocusTray(activateWindow: false);
            }
        }

        private void ToggleLightNovelAutoFocus()
        {
            _lightNovelAutoFocusEnabled = !_lightNovelAutoFocusEnabled;
            lblStatus.Text = _lightNovelAutoFocusEnabled
                ? "Auto focus WebView2: ON"
                : "Auto focus WebView2: OFF";

            _lightNovelFocusRestoreOverride = false;
            if (_lightNovelFocusTrayHidden)
            {
                RestoreMainWindowFromFocusTray(activateWindow: false);
            }

            UpdateLightNovelFocusVisibilityState();
            UpdateLightNovelFloatingControlState();
        }

        private void EnsureLightNovelFocusTrayIcon()
        {
            if (_lightNovelFocusTrayIcon != null)
            {
                return;
            }

            var icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
            var trayMenu = new System.Windows.Forms.ContextMenuStrip();
            trayMenu.Items.Add("Restore", null, (sender, args) => Dispatcher.BeginInvoke(new Action(() => RestoreMainWindowFromFocusTray(activateWindow: true))));
            trayMenu.Items.Add("Show Float", null, (sender, args) => Dispatcher.BeginInvoke(new Action(() =>
            {
                RestoreMainWindowFromFocusTray(activateWindow: true);
                EnsureLightNovelFloatingControlWindow();
                _lightNovelFloatingControlWindow?.Show();
                UpdateLightNovelFloatingControlState();
            })));
            trayMenu.Items.Add("Exit", null, (sender, args) => Dispatcher.BeginInvoke(new Action(Close)));
            _lightNovelFocusTrayIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = icon,
                Text = "Comic-GMTPC focus",
                Visible = false,
                ContextMenuStrip = trayMenu
            };
            _lightNovelFocusTrayIcon.MouseClick += (sender, args) =>
            {
                if (args.Button == System.Windows.Forms.MouseButtons.Left)
                {
                    Dispatcher.BeginInvoke(new Action(() => RestoreMainWindowFromFocusTray(activateWindow: true)));
                }
            };
        }

        private void HideMainWindowToFocusTray()
        {
            if (!_lightNovelAutoFocusEnabled)
            {
                return;
            }

            EnsureLightNovelFocusTrayIcon();
            if (!_lightNovelFocusStealthActive)
            {
                _lightNovelSavedWindowOpacity = Opacity;
                _lightNovelSavedShowInTaskbar = ShowInTaskbar;
            }

            _lightNovelFocusStealthActive = true;
            _lightNovelFocusTrayHidden = true;
            _lightNovelFocusRestoreOverride = false;
            _lightNovelFocusTrayIcon.Visible = true;
            _lightNovelFloatingWasVisibleBeforeFocusTray = _lightNovelFloatingControlWindow != null && _lightNovelFloatingControlWindow.IsVisible;
            if (_lightNovelFloatingControlWindow != null)
            {
                _lightNovelFloatingControlWindow.PrepareForTrayHide();
                _lightNovelFloatingControlWindow.Hide();
            }
            WindowState = WindowState.Minimized;
            ShowInTaskbar = false;
            Opacity = 0d;
            Hide();
        }

        private void RestoreMainWindowFromFocusTray(bool activateWindow)
        {
            _lightNovelFocusTrayHidden = false;
            _lightNovelFocusRestoreOverride = _lightNovelAutoFocusEnabled;
            if (_lightNovelFocusTrayIcon != null)
            {
                _lightNovelFocusTrayIcon.Visible = false;
            }

            _lightNovelFocusStealthActive = false;
            Opacity = _lightNovelSavedWindowOpacity <= 0d ? 1d : _lightNovelSavedWindowOpacity;
            ShowInTaskbar = _lightNovelSavedShowInTaskbar;
            if (!IsVisible)
            {
                Show();
            }

            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            if (activateWindow)
            {
                Activate();
                Focus();
            }

            RestoreLightNovelFloatingControlWindowFromTray();
        }

        private void RestoreLightNovelFloatingControlWindowFromTray()
        {
            EnsureLightNovelFloatingControlWindow();
            if (_lightNovelFloatingControlWindow == null)
            {
                return;
            }

            _lightNovelFloatingControlWindow.RestoreFromTray();
            UpdateLightNovelFloatingControlState();
        }

        private void DisposeLightNovelFocusTrayIcon()
        {
            if (_lightNovelFocusTrayIcon == null)
            {
                return;
            }

            _lightNovelFocusTrayIcon.Visible = false;
            _lightNovelFocusTrayIcon.Dispose();
            _lightNovelFocusTrayIcon = null;
        }

        private void HandleFocusTrayWindowStateChanged()
        {
            if (_lightNovelAutoFocusEnabled && WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
        }

        private async Task StartPictureDownloadFromFloatingAsync(bool suppressWarning = false)
        {
            if (btnStartDownload?.IsChecked == true || _downloadCts != null)
            {
                lblStatus.Text = "Download picture đang chạy.";
                UpdateLightNovelFloatingControlState();
                return;
            }

            SetDownloadToggleState(true);
            await HandleStartDownloadToggleCheckedAsync(suppressWarning);
            UpdateLightNovelFloatingControlState();
        }

        private void StopPictureDownloadFromFloating()
        {
            BtnStopDownload_Click(this, new RoutedEventArgs());
            UpdateLightNovelFloatingControlState();
        }

        private void SetPictureAutoRetryFromFloating(bool enabled)
        {
            if (btnAutoRetryErrors == null)
            {
                return;
            }

            btnAutoRetryErrors.IsChecked = enabled;
            lblStatus.Text = enabled ? "Auto retry: ON" : "Auto retry: OFF";
            UpdateLightNovelFloatingControlState();
        }

        private Task MergeLightNovelBookMarkdownAsync(GalleryItem item)
        {
            if (item == null)
            {
                return Task.CompletedTask;
            }

            string bookTitle = FormatGalleryTitle(item.Name);
            string targetFolder = GetMergedLightNovelBookFolder(item, bookTitle);
            if (!Directory.Exists(targetFolder))
            {
                throw new InvalidOperationException("Folder book chưa tồn tại để merge .md.");
            }

            List<MergedLightNovelChapterSection> mergedSections = LoadMergedLightNovelSectionsFromDisk(item, targetFolder);

            if (mergedSections.Count == 0)
            {
                throw new InvalidOperationException("Không có file .md chapter hợp lệ để merge.");
            }

            string mergedMarkdown = BuildMergedLightNovelMarkdown(bookTitle, item.Link, mergedSections);
            string mergedFileName = Path.GetFileName(targetFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(mergedFileName))
            {
                mergedFileName = "book";
            }
            string mergedPath = Path.Combine(targetFolder, mergedFileName + ".md");
            File.WriteAllText(mergedPath, mergedMarkdown, new System.Text.UTF8Encoding(true));
            return Task.CompletedTask;
        }

        private async Task MergeSelectedLightNovelBooksAsync()
        {
            List<GalleryItem> targetItems = dgResults?.SelectedItems.Cast<GalleryItem>().ToList() ?? new List<GalleryItem>();
            if (targetItems.Count == 0)
            {
                targetItems = _scrapedItems.Where(item => item != null && item.IsChecked).ToList();
            }

            if (targetItems.Count == 0 && dgResults?.SelectedItem is GalleryItem selectedQueueItem)
            {
                targetItems.Add(selectedQueueItem);
            }

            if (targetItems.Count == 0)
            {
                targetItems = _dgLightNovelBooks?.SelectedItems.Cast<GalleryItem>().ToList() ?? new List<GalleryItem>();
            }

            if (targetItems.Count == 0)
            {
                targetItems = _lightNovelItems.Where(item => item.IsChecked).ToList();
            }

            if (targetItems.Count == 0 && _selectedLightNovelBook != null)
            {
                targetItems.Add(_selectedLightNovelBook);
            }

            if (targetItems.Count == 0)
            {
                ShowWarning("Chưa có book nào để merge .md.", "Thông báo");
                return;
            }

            int mergedCount = 0;
            int failedCount = 0;
            foreach (GalleryItem item in targetItems)
            {
                try
                {
                    await MergeLightNovelBookMarkdownAsync(item);
                    mergedCount++;
                }
                catch (Exception ex)
                {
                    failedCount++;
                    HakoLog("Merge md book lỗi: " + ex.Message);
                }
            }

            lblStatus.Text = $"Merge md xong. OK:{mergedCount} | Fail:{failedCount}";
        }

        private void CancelLightNovelPreviewWork()
        {
            _lightNovelPreviewCts?.Cancel();
            _lightNovelPreviewCts?.Dispose();
            _lightNovelPreviewCts = null;

            _lightNovelWarmupCts?.Cancel();
            _lightNovelWarmupCts?.Dispose();
            _lightNovelWarmupCts = null;
        }

        private CancellationToken ResetLightNovelWarmupCancellationToken()
        {
            _lightNovelWarmupCts?.Cancel();
            _lightNovelWarmupCts?.Dispose();
            _lightNovelWarmupCts = new CancellationTokenSource();
            return _lightNovelWarmupCts.Token;
        }

        private string GetMergedLightNovelBookFolder(GalleryItem item, string bookTitle)
        {
            string rootFolder = txtDownloadPath?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rootFolder))
            {
                rootFolder = PortablePaths.DefaultDownloadRoot;
            }

            string resolvedRoot = GetConfiguredDownloadRoot(rootFolder, item);
            string safeBookTitle = GetCanonicalBookFolderName(item, bookTitle, "hako-book", 72);
            return Path.Combine(resolvedRoot, safeBookTitle);
        }

        private List<MergedLightNovelChapterSection> LoadMergedLightNovelSectionsFromDisk(GalleryItem item, string targetFolder)
        {
            var chapterMap = new Dictionary<string, LightNovelChapterRecord>(StringComparer.OrdinalIgnoreCase);
            if (_lightNovelChapterMap.TryGetValue(GetLightNovelItemKey(item), out ObservableCollection<LightNovelChapterRecord> existingChapters))
            {
                string rootFolder = txtDownloadPath?.Text?.Trim() ?? string.Empty;
                foreach (LightNovelChapterRecord chapter in existingChapters)
                {
                    string chapterPath = GetLightNovelChapterMarkdownPath(item, chapter, rootFolder);
                    if (string.IsNullOrWhiteSpace(chapterPath))
                    {
                        continue;
                    }

                    chapter.MarkdownFilePath = chapterPath;
                    chapterMap[Path.GetFullPath(chapterPath)] = chapter;
                }
            }

            List<string> markdownPaths = Directory
                .EnumerateFiles(targetFolder, "*.md", SearchOption.AllDirectories)
                .Where(path =>
                {
                    string fileName = Path.GetFileName(path);
                    string mergedFileName = Path.GetFileName(targetFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) + ".md";
                    return !string.Equals(fileName, "0000 - MERGED BOOK.md", StringComparison.OrdinalIgnoreCase) &&
                           !string.Equals(fileName, mergedFileName, StringComparison.OrdinalIgnoreCase);
                })
                .OrderBy(path => GetMergeRelativePathSafe(targetFolder, path), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var mergedSections = new List<MergedLightNovelChapterSection>();
            foreach (string markdownPath in markdownPaths)
            {
                string fullPath = Path.GetFullPath(markdownPath);
                if (!chapterMap.TryGetValue(fullPath, out LightNovelChapterRecord chapter))
                {
                    chapter = CreateLightNovelChapterRecordFromMarkdownPath(targetFolder, markdownPath);
                }

                string markdown = File.ReadAllText(markdownPath);
                if (string.IsNullOrWhiteSpace(markdown))
                {
                    continue;
                }

                chapter.MarkdownFilePath = markdownPath;
                chapter.MarkdownText = markdown;
                mergedSections.Add(new MergedLightNovelChapterSection
                {
                    Chapter = chapter,
                    Anchor = BuildLightNovelAnchor(chapter),
                    MarkdownBody = ExtractLightNovelMergedBody(markdown)
                });
            }

            return mergedSections;
        }

        private LightNovelChapterRecord CreateLightNovelChapterRecordFromMarkdownPath(string targetFolder, string markdownPath)
        {
            string relativePath = GetMergeRelativePathSafe(targetFolder, markdownPath);
            string relativeDirectory = Path.GetDirectoryName(relativePath) ?? string.Empty;
            string volumeTitle = string.IsNullOrWhiteSpace(relativeDirectory)
                ? string.Empty
                : relativeDirectory.Replace(Path.DirectorySeparatorChar, ' ').Trim();
            string fileName = Path.GetFileNameWithoutExtension(markdownPath);
            string chapterTitle = ExtractLightNovelChapterTitleFromMarkdown(File.ReadAllText(markdownPath));
            if (string.IsNullOrWhiteSpace(chapterTitle))
            {
                chapterTitle = fileName;
            }
            int sequenceIndex = 0;

            int separatorIndex = fileName.IndexOf(" - ", StringComparison.Ordinal);
            if (separatorIndex > 0)
            {
                string sequenceText = fileName.Substring(0, separatorIndex).Trim();
                if (int.TryParse(sequenceText, out int parsedIndex))
                {
                    sequenceIndex = parsedIndex;
                    if (string.IsNullOrWhiteSpace(chapterTitle))
                    {
                        chapterTitle = fileName.Substring(separatorIndex + 3).Trim();
                    }
                }
            }

            return new LightNovelChapterRecord
            {
                ChapterTitle = CompactSingleLine(chapterTitle),
                MarkdownFilePath = markdownPath,
                VolumeTitle = volumeTitle,
                SequenceIndex = sequenceIndex
            };
        }

        private static string ExtractLightNovelChapterTitleFromMarkdown(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return string.Empty;
            }

            Match match = Regex.Match(markdown, @"^\s*##\s+(?<title>.+?)\s*$", RegexOptions.Multiline);
            return match.Success ? match.Groups["title"].Value.Trim() : string.Empty;
        }

        private static string GetMergeRelativePathSafe(string basePath, string fullPath)
        {
            string normalizedBasePath = Path.GetFullPath(basePath ?? string.Empty)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string normalizedFullPath = Path.GetFullPath(fullPath ?? string.Empty);

            if (normalizedFullPath.StartsWith(normalizedBasePath, StringComparison.OrdinalIgnoreCase))
            {
                return normalizedFullPath.Substring(normalizedBasePath.Length);
            }

            return Path.GetFileName(normalizedFullPath);
        }

        private static string BuildMergedLightNovelMarkdown(string bookTitle, string bookLink, List<MergedLightNovelChapterSection> sections)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# " + (bookTitle ?? "Merged Book"));
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(bookLink))
            {
                sb.AppendLine("Nguồn truyện: " + bookLink.Trim());
                sb.AppendLine();
            }

            sb.AppendLine("## Mục lục");
            sb.AppendLine();

            string currentVolume = null;
            bool isFirstVolume = true;
            foreach (MergedLightNovelChapterSection section in sections)
            {
                string volumeTitle = string.IsNullOrWhiteSpace(section.Chapter?.VolumeTitle)
                    ? null
                    : section.Chapter.VolumeTitle.Trim();

                if (!string.Equals(currentVolume, volumeTitle, StringComparison.OrdinalIgnoreCase))
                {
                    currentVolume = volumeTitle;
                    if (!string.IsNullOrWhiteSpace(currentVolume))
                    {
                        if (!isFirstVolume)
                        {
                            sb.AppendLine();
                        }
                        isFirstVolume = false;
                        sb.AppendLine("### " + currentVolume);
                        sb.AppendLine();
                    }
                }

                sb.AppendLine("- " + section.Chapter.ChapterTitle.Trim());
            }

            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            currentVolume = null;
            isFirstVolume = true;
            foreach (MergedLightNovelChapterSection section in sections)
            {
                string volumeTitle = string.IsNullOrWhiteSpace(section.Chapter?.VolumeTitle)
                    ? null
                    : section.Chapter.VolumeTitle.Trim();

                if (!string.Equals(currentVolume, volumeTitle, StringComparison.OrdinalIgnoreCase))
                {
                    currentVolume = volumeTitle;
                    if (!string.IsNullOrWhiteSpace(currentVolume))
                    {
                        if (!isFirstVolume)
                        {
                            sb.AppendLine();
                        }
                        isFirstVolume = false;
                        sb.AppendLine("## " + currentVolume);
                        sb.AppendLine();
                    }
                }

                sb.AppendLine("### " + section.Chapter.ChapterTitle.Trim());
                sb.AppendLine();
                if (!string.IsNullOrWhiteSpace(section.Chapter.ChapterLink))
                {
                    sb.AppendLine("Nguồn chương: " + section.Chapter.ChapterLink.Trim());
                    sb.AppendLine();
                }

                sb.AppendLine(section.MarkdownBody.Trim());
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
            }

            return sb.ToString().Trim() + Environment.NewLine;
        }

        private static string ExtractLightNovelMergedBody(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return string.Empty;
            }

            string normalized = markdown.Replace("\r\n", "\n");
            string[] markerLines =
            {
                "---\n",
                "---\r\n"
            };

            foreach (string marker in markerLines)
            {
                int markerIndex = normalized.IndexOf(marker.Replace("\r\n", "\n"), StringComparison.Ordinal);
                if (markerIndex >= 0)
                {
                    return normalized.Substring(markerIndex + 4).Trim();
                }
            }

            return normalized.Trim();
        }

        private static string BuildLightNovelAnchor(LightNovelChapterRecord chapter)
        {
            string raw = (chapter?.VolumeTitle ?? string.Empty) + "-" + (chapter?.ChapterTitle ?? string.Empty);
            string normalized = raw.ToLowerInvariant().Replace('đ', 'd').Replace('Đ', 'd');
            normalized = normalized.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder();
            foreach (char ch in normalized)
            {
                System.Globalization.UnicodeCategory category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
                if (category == System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(ch);
                }
                else if (sb.Length == 0 || sb[sb.Length - 1] != '-')
                {
                    sb.Append('-');
                }
            }

            string anchor = sb.ToString().Trim('-');
            return string.IsNullOrWhiteSpace(anchor) ? "chapter" : anchor;
        }

        private void UpdateLightNovelBookCheckedState(GalleryItem item)
        {
            if (item == null)
            {
                return;
            }

            if (!_lightNovelChapterMap.TryGetValue(GetLightNovelItemKey(item), out ObservableCollection<LightNovelChapterRecord> records) ||
                records.Count == 0)
            {
                item.IsChecked = false;
                return;
            }

            item.IsChecked = records.Any(record => record.IsChecked);
        }

        private sealed class MergedLightNovelChapterSection
        {
            public LightNovelChapterRecord Chapter { get; set; }

            public string Anchor { get; set; }

            public string MarkdownBody { get; set; }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private bool _isGalleryPopupPreviewEnabled = true;
        private CancellationTokenSource _galleryHoverPreviewCts;
        private FrameworkElement _activeGalleryHoverPreviewHost;
        private GalleryItem _activeGalleryHoverPreviewItem;
        private readonly HashSet<string> _galleryHoverPreviewBitmapMissingCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> _galleryHoverPreviewCandidateCache = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly object _galleryHoverPreviewCandidateCacheLock = new object();
        private SemaphoreSlim _galleryHoverPreviewImageSemaphore = new SemaphoreSlim(4, 4);
        private CancellationTokenSource _prefetchCts;
        // Rate-limit tag fetch cho nhentai/truyenqq: tối đa 2 request cùng lúc
        private readonly SemaphoreSlim _tagFetchSemaphore = new SemaphoreSlim(2, 2);
        private readonly HashSet<string> _tagFetchFailedCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _tagFetchFailedCacheLock = new object();
        private static readonly Random _tagFetchJitter = new Random();

        private void GalleryResultPreviewHost_MouseEnter(object sender, MouseEventArgs e)
        {
            StartGalleryHoverPreview(sender as FrameworkElement);
        }

        private void GalleryResultPreviewHost_MouseMove(object sender, MouseEventArgs e)
        {
            StartGalleryHoverPreview(sender as FrameworkElement);
        }

        private void GalleryResultPreviewHost_MouseLeave(object sender, MouseEventArgs e)
        {
            StopGalleryHoverPreview(sender as FrameworkElement);
        }

        internal void ForwardGalleryPreviewMouseEnter(FrameworkElement host)
        {
            StartGalleryHoverPreview(host);
        }

        internal void ForwardGalleryPreviewMouseMove(FrameworkElement host)
        {
            StartGalleryHoverPreview(host);
        }

        internal void ForwardGalleryPreviewMouseLeave(FrameworkElement host)
        {
            StopGalleryHoverPreview(host);
        }

        internal void PrefetchGalleryHoverPreview(IEnumerable<GalleryItem> items)
        {
            if (items == null)
            {
                return;
            }

            // Fire prefetch có throttle: nhentai/truyenqq chạy tuần tự (đã có semaphore bên trong),
            // các domain khác vẫn fire-and-forget tự do.
            _ = PrefetchGalleryHoverPreviewBatchAsync(items.Where(SupportsHoverPreview).Distinct().ToList());
        }

        internal void PrefetchAllScrapedItemsPreviewCache()
        {
            if (_prefetchCts != null)
            {
                try
                {
                    _prefetchCts.Cancel();
                    _prefetchCts.Dispose();
                }
                catch { }
            }
            _prefetchCts = new CancellationTokenSource();
            CancellationToken token = _prefetchCts.Token;

            var allItems = (ResultsView != null)
                ? ResultsView.Cast<object>().OfType<GalleryItem>().Where(SupportsHoverPreview).ToList()
                : _scrapedItems.Where(SupportsHoverPreview).ToList();
            if (allItems.Count == 0) return;

            // Sắp xếp ưu tiên: item trong danh sách hiển thị (_thumbnailVisibleItems) hoặc item chưa có cache file lên trước
            var visibleSet = new HashSet<GalleryItem>(_thumbnailVisibleItems ?? Enumerable.Empty<GalleryItem>());
            var prioritizedItems = allItems
                .OrderByDescending(x => visibleSet.Contains(x))
                .ThenBy(x => x.HasHoverPreviewThumbnailFile)
                .ToList();

            int workerCount = 4;
            if (cmbThumbCacheConnection != null && cmbThumbCacheConnection.SelectedItem is ComboBoxItem selectedItem &&
                int.TryParse(selectedItem.Content.ToString(), out int val))
            {
                workerCount = val;
            }

            Task.Run(async () =>
            {
                var queue = new System.Collections.Concurrent.ConcurrentQueue<GalleryItem>(prioritizedItems);
                var workers = new List<Task>();

                for (int i = 0; i < workerCount; i++)
                {
                    workers.Add(Task.Run(async () =>
                    {
                        int processedCount = 0;
                        while (queue.TryDequeue(out var item))
                        {
                            if (token.IsCancellationRequested) break;
                            try
                            {
                                // Tải file cache ảnh về ổ cứng (.tmp/preview-cache)
                                await EnsureGalleryHoverPreviewAsync(item, fetchTags: false);
                                await EnsureGalleryHoverPreviewFileAsync(item, token, fetchTags: false);

                                processedCount++;
                                if (processedCount % 4 == 0)
                                {
                                    var _ = Dispatcher.BeginInvoke(new Action(UpdateThumbnailsVirtualizationWindow), System.Windows.Threading.DispatcherPriority.Background);
                                }
                            }
                            catch { }
                        }
                    }, token));
                }

                await Task.WhenAll(workers);
                var __ = Dispatcher.BeginInvoke(new Action(UpdateThumbnailsVirtualizationWindow), System.Windows.Threading.DispatcherPriority.Background);
            }, token);
        }

        private async Task PrefetchGalleryHoverPreviewBatchAsync(List<GalleryItem> items)
        {
            if (items == null || items.Count == 0) return;
            var tasks = new List<Task>();
            foreach (GalleryItem item in items)
            {
                // nhentai và truyenqq: stagger để tránh burst song song
                bool isRateSensitive = (item.Link != null) &&
                    (IsNhentaiUrl(item.Link) || IsTruyenqqUrl(item.Link));
                if (isRateSensitive)
                {
                    // Chờ trước khi thêm task mới để tạo jitter tự nhiên
                    int jitter;
                    lock (_tagFetchJitter) { jitter = _tagFetchJitter.Next(300, 900); }
                    await Task.Delay(jitter);
                }
                tasks.Add(PrefetchGalleryHoverPreviewAsync(item));
            }
            await Task.WhenAll(tasks);
        }

        private void StartGalleryHoverPreview(FrameworkElement host)
        {
            if (!_isGalleryPopupPreviewEnabled || host == null || !(host.DataContext is GalleryItem item) || !SupportsHoverPreview(item))
            {
                return;
            }

            if (ReferenceEquals(_activeGalleryHoverPreviewHost, host) &&
                ReferenceEquals(_activeGalleryHoverPreviewItem, item) &&
                (item.IsHoverPreviewLoading || (host.ToolTip is ToolTip activeToolTip && activeToolTip.IsOpen)))
            {
                return;
            }

            CancelGalleryHoverPreview();
            _activeGalleryHoverPreviewHost = host;
            _activeGalleryHoverPreviewItem = item;
            _galleryHoverPreviewCts = new CancellationTokenSource();
            CancellationToken token = _galleryHoverPreviewCts.Token;

            item.IsHoverPreviewLoading = true;
            _ = OpenGalleryHoverPreviewAsync(host, item, token);
        }

        private async Task OpenGalleryHoverPreviewAsync(FrameworkElement host, GalleryItem item, CancellationToken token)
        {
            try
            {
                await Task.Delay(150, token);
                if (token.IsCancellationRequested || !host.IsMouseOver || !ReferenceEquals(_activeGalleryHoverPreviewHost, host))
                {
                    return;
                }

                if (!item.HasHoverPreviewThumbnailFile)
                {
                    await EnsureGalleryHoverPreviewAsync(item, fetchTags: true);
                    // fetchTags=true: chỉ hover mới get tag, không fetch trong prefetch chạy ngầm
                    await EnsureGalleryHoverPreviewFileAsync(item, token, fetchTags: true);
                }
                else
                {
                    // Đã có file cache nhưng chưa hiển thị lên UI, buộc reset cache để nạp lại
                    if (item.HoverPreviewThumbnailImageSource == null)
                    {
                        item.ResetHoverPreviewCache();
                    }
                }

                if (item.Tag == null)
                {
                    if (IsTruyenqqUrl(item.Link) || IsNettruyenUrl(item.Link) || IsDilibUrl(item.Link) || IsEHentaiUrl(item.Link))
                    {
                        // Đã có ảnh nhưng chưa có tag → cào tag truyenqq/nettruyen/thuviensach/e-hentai on-demand
                        await EnsureGalleryHoverPreviewAsync(item, fetchTags: true);
                    }
                    else
                    {
                        bool isHitomi = string.Equals(item.SourceDomain, "hitomi.la", StringComparison.OrdinalIgnoreCase) ||
                                        (item.Link != null && item.Link.IndexOf("hitomi.la", StringComparison.OrdinalIgnoreCase) >= 0);
                        if (isHitomi)
                        {
                            await EnsureHitomiLaTagAsync(item, token);
                        }
                    }
                }

                if (token.IsCancellationRequested ||
                    !host.IsMouseOver ||
                    !ReferenceEquals(_activeGalleryHoverPreviewHost, host) ||
                    !ReferenceEquals(_activeGalleryHoverPreviewItem, item) ||
                    string.IsNullOrWhiteSpace(item.HoverPreviewLocalPath) ||
                    !File.Exists(item.HoverPreviewLocalPath))
                {
                    return;
                }

                ToolTip toolTip = CreateGalleryHoverPreviewToolTip(item);
                CloseGalleryHoverPreviewToolTip();
                host.ToolTip = toolTip;
                _activeGalleryHoverPreviewHost = host;
                _activeGalleryHoverPreviewItem = item;
                toolTip.IsOpen = true;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log($"Hover preview failed for '{item.DisplayName}': {ex.Message}");
            }
            finally
            {
                item.IsHoverPreviewLoading = false;
            }
        }

        private async Task PrefetchGalleryHoverPreviewAsync(GalleryItem item)
        {
            if (item == null)
            {
                return;
            }

            // Nếu đã có file cache và thuộc tính đường dẫn/ảnh bìa trên giao diện đã nạp đầy đủ thì mới thoát sớm
            if (!string.IsNullOrWhiteSpace(item.HoverPreviewLocalPath) && 
                File.Exists(item.HoverPreviewLocalPath) && 
                item.HoverPreviewThumbnailImageSource != null)
            {
                return;
            }

            try
            {
                item.IsHoverPreviewLoading = true;
                await EnsureGalleryHoverPreviewAsync(item, fetchTags: false);
                await EnsureGalleryHoverPreviewFileAsync(item, CancellationToken.None, fetchTags: false);
            }
            catch
            {
            }
            finally
            {
                item.IsHoverPreviewLoading = false;
                if (!string.IsNullOrWhiteSpace(item.HoverPreviewLocalPath) && 
                    File.Exists(item.HoverPreviewLocalPath) && 
                    item.HoverPreviewThumbnailImageSource == null)
                {
                    item.ResetHoverPreviewCache();
                }
            }
        }

        private void StopGalleryHoverPreview(FrameworkElement host)
        {
            CancelGalleryHoverPreview();
            if (host == null)
            {
                return;
            }

            if (host.DataContext is GalleryItem item)
            {
                item.IsHoverPreviewLoading = false;
            }

            if (host.ToolTip is ToolTip toolTip)
            {
                toolTip.IsOpen = false;
            }

            host.ToolTip = null;

            if (ReferenceEquals(_activeGalleryHoverPreviewHost, host))
            {
                _activeGalleryHoverPreviewHost = null;
                _activeGalleryHoverPreviewItem = null;
            }
        }

        private void CancelGalleryHoverPreview()
        {
            _galleryHoverPreviewCts?.Cancel();
            _galleryHoverPreviewCts?.Dispose();
            _galleryHoverPreviewCts = null;
            CloseGalleryHoverPreviewToolTip();
        }

        private void BtnPopupPreviewToggle_Click(object sender, RoutedEventArgs e)
        {
            _isGalleryPopupPreviewEnabled = btnPopupPreviewToggle?.IsChecked == true;
            if (!_isGalleryPopupPreviewEnabled)
            {
                CancelGalleryHoverPreview();
            }

            RefreshVisibleGalleryHoverPreviewBindings();
            PrefetchAllThumbnailResults();

            UpdateGalleryPopupPreviewButtonState();
        }

        private void UpdateGalleryPopupPreviewButtonState()
        {
            if (btnPopupPreviewToggle == null)
            {
                return;
            }

            btnPopupPreviewToggle.IsChecked = _isGalleryPopupPreviewEnabled;
        }

        private void CloseGalleryHoverPreviewToolTip()
        {
            if (_activeGalleryHoverPreviewHost?.ToolTip is ToolTip activeToolTip)
            {
                activeToolTip.IsOpen = false;
            }

            if (_activeGalleryHoverPreviewHost != null)
            {
                _activeGalleryHoverPreviewHost.ToolTip = null;
            }

            _activeGalleryHoverPreviewHost = null;
            _activeGalleryHoverPreviewItem = null;
        }

        private ToolTip CreateGalleryHoverPreviewToolTip(GalleryItem item)
        {
            var image = new Image
            {
                Stretch = Stretch.Uniform,
                MaxWidth = 340,
                MaxHeight = 340,
                Source = CreatePreviewImageSource(item?.HoverPreviewLocalPath, 340)
            };

            var panel = new StackPanel();
            panel.Children.Add(image);
            string title = item?.DisplayName;
            if (!string.IsNullOrWhiteSpace(title))
            {
                var titleBlock = new TextBlock
                {
                    FontWeight = FontWeights.Bold,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(2, 6, 2, 0)
                };
                TextBlockLanguageColorizer.SetHighlightedText(titleBlock, title);
                if (titleBlock.Inlines.Count == 0)
                {
                    titleBlock.Text = title;
                    titleBlock.Foreground = TryFindResource("CyberpunkYellowBrush") as Brush ?? Brushes.Gold;
                }
                else
                {
                    titleBlock.Foreground = TryFindResource("CyberpunkYellowBrush") as Brush ?? Brushes.Gold;
                }
                panel.Children.Add(titleBlock);
            }

            string latestChapter = item?.MissingChapterLatestChapterText;
            if (!string.IsNullOrWhiteSpace(latestChapter))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "latest chapter: " + latestChapter,
                    Foreground = TryFindResource("CyberpunkCyanBrush") as Brush ?? Brushes.Cyan,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(2, 3, 2, 0)
                });
            }

            string missingStatus = item?.MissingChapterStatusText;
            if (!string.IsNullOrWhiteSpace(missingStatus))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "missing chapter: " + missingStatus,
                    Foreground = item.HasMissingChapterIssue
                        ? (TryFindResource("CyberpunkPinkBrush") as Brush ?? Brushes.DeepPink)
                        : (TryFindResource("CyberpunkCyanBrush") as Brush ?? Brushes.Cyan),
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(2, 2, 2, 0)
                });
            }

            string status = item?.DisplayStatusText;
            if (!string.IsNullOrWhiteSpace(status))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "Status: " + status,
                    Foreground = TryFindResource("CyberpunkCyanBrush") as Brush ?? Brushes.Cyan,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(2, 2, 2, 0)
                });
            }

            bool hasTagsSupport = false;
            if (item != null)
            {
                if (string.Equals(item.SourceDomain, "hitomi.la", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.SourceDomain, "nhentai.net", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.SourceDomain, "e-hentai.org", StringComparison.OrdinalIgnoreCase))
                {
                    hasTagsSupport = true;
                }
                else if (item.Link != null && (item.Link.IndexOf("hitomi.la", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                               item.Link.IndexOf("nhentai.net", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                               IsEHentaiUrl(item.Link) ||
                                               IsTruyenqqUrl(item.Link) ||
                                               IsNettruyenUrl(item.Link) ||
                                               IsDilibUrl(item.Link)))
                {
                    hasTagsSupport = true;
                }
            }

            if (hasTagsSupport && item.Tag != null)
            {
                try
                {
                    Newtonsoft.Json.Linq.JObject galleryInfo = null;
                    if (item.Tag is string tagStr)
                    {
                        if (!string.IsNullOrWhiteSpace(tagStr))
                        {
                            galleryInfo = Newtonsoft.Json.Linq.JObject.Parse(tagStr);
                        }
                    }
                    else if (item.Tag is Newtonsoft.Json.Linq.JObject jObj)
                    {
                        galleryInfo = jObj;
                    }

                    if (galleryInfo != null)
                    {
                        // Helper local function to extract list from JArray of strings or objects
                        List<string> GetCategoryItems(string key)
                        {
                            var list = new List<string>();
                            if (galleryInfo[key] is Newtonsoft.Json.Linq.JArray arr)
                            {
                                foreach (var elem in arr)
                                {
                                    string val = elem is Newtonsoft.Json.Linq.JObject o ? (o["name"]?.ToString() ?? o["tag"]?.ToString()) : elem?.ToString();
                                    if (!string.IsNullOrWhiteSpace(val))
                                    {
                                        list.Add(System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(val.Trim()));
                                    }
                                }
                            }
                            return list;
                        }

                        // Specific metadata lines if available
                        var artists = GetCategoryItems("artist");
                        if (artists.Count > 0)
                        {
                            panel.Children.Add(new TextBlock
                            {
                                Text = "Artist: " + string.Join(", ", artists),
                                Foreground = TryFindResource("CyberpunkCyanBrush") as Brush ?? Brushes.Cyan,
                                FontWeight = FontWeights.SemiBold,
                                FontSize = 11,
                                TextWrapping = TextWrapping.Wrap,
                                Margin = new Thickness(2, 2, 2, 0)
                            });
                        }

                        var groups = GetCategoryItems("group");
                        if (groups.Count > 0)
                        {
                            panel.Children.Add(new TextBlock
                            {
                                Text = "Group: " + string.Join(", ", groups),
                                Foreground = TryFindResource("CyberpunkCyanBrush") as Brush ?? Brushes.Cyan,
                                FontWeight = FontWeights.SemiBold,
                                FontSize = 11,
                                TextWrapping = TextWrapping.Wrap,
                                Margin = new Thickness(2, 2, 2, 0)
                            });
                        }

                        var parodies = GetCategoryItems("parody");
                        if (parodies.Count > 0)
                        {
                            panel.Children.Add(new TextBlock
                            {
                                Text = "Parody: " + string.Join(", ", parodies),
                                Foreground = TryFindResource("CyberpunkYellowBrush") as Brush ?? Brushes.Gold,
                                FontWeight = FontWeights.Normal,
                                FontSize = 11,
                                TextWrapping = TextWrapping.Wrap,
                                Margin = new Thickness(2, 2, 2, 0)
                            });
                        }

                        var characters = GetCategoryItems("character");
                        if (characters.Count > 0)
                        {
                            panel.Children.Add(new TextBlock
                            {
                                Text = "Characters: " + string.Join(", ", characters),
                                Foreground = TryFindResource("CyberpunkYellowBrush") as Brush ?? Brushes.Gold,
                                FontWeight = FontWeights.Normal,
                                FontSize = 11,
                                TextWrapping = TextWrapping.Wrap,
                                Margin = new Thickness(2, 2, 2, 0)
                            });
                        }

                        var languages = GetCategoryItems("language");
                        if (languages.Count > 0)
                        {
                            panel.Children.Add(new TextBlock
                            {
                                Text = "Language: " + string.Join(", ", languages),
                                Foreground = TryFindResource("CyberpunkMutedTextBrush") as Brush ?? Brushes.Gray,
                                FontWeight = FontWeights.Normal,
                                FontSize = 11,
                                TextWrapping = TextWrapping.Wrap,
                                Margin = new Thickness(2, 2, 2, 0)
                            });
                        }

                        // Tags list (filter out language, artist, group, parody, character if already rendered)
                        bool hasRenderedSpecificCategories = artists.Count > 0 || groups.Count > 0 || parodies.Count > 0 || characters.Count > 0 || languages.Count > 0;

                        if (galleryInfo["tags"] is Newtonsoft.Json.Linq.JArray tagsArray)
                        {
                            var tagsList = new List<string>();
                            foreach (var t in tagsArray)
                            {
                                string tagName = t["tag"]?.ToString();
                                if (!string.IsNullOrWhiteSpace(tagName))
                                {
                                    if (hasRenderedSpecificCategories)
                                    {
                                        if (tagName.StartsWith("language:", StringComparison.OrdinalIgnoreCase) ||
                                            tagName.StartsWith("artist:", StringComparison.OrdinalIgnoreCase) ||
                                            tagName.StartsWith("group:", StringComparison.OrdinalIgnoreCase) ||
                                            tagName.StartsWith("parody:", StringComparison.OrdinalIgnoreCase) ||
                                            tagName.StartsWith("character:", StringComparison.OrdinalIgnoreCase))
                                        {
                                            continue;
                                        }
                                    }

                                    tagName = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(tagName);
                                    var femaleProp = t["female"];
                                    var maleProp = t["male"];
                                    if (femaleProp != null && (femaleProp.ToString() == "1" || femaleProp.ToString().ToLower() == "true"))
                                    {
                                        tagName += " ♀";
                                    }
                                    else if (maleProp != null && (maleProp.ToString() == "1" || maleProp.ToString().ToLower() == "true"))
                                    {
                                        tagName += " ♂";
                                    }
                                    tagsList.Add(tagName);
                                }
                            }

                            if (tagsList.Count > 0)
                            {
                                panel.Children.Add(new TextBlock
                                {
                                    Text = "Tags: " + string.Join(", ", tagsList),
                                    Foreground = TryFindResource("CyberpunkMutedTextBrush") as Brush ?? Brushes.Gray,
                                    FontWeight = FontWeights.Normal,
                                    FontSize = 11,
                                    TextWrapping = TextWrapping.Wrap,
                                    Margin = new Thickness(2, 2, 2, 0)
                                });
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            string process = item?.CurrentProcess;
            if (!string.IsNullOrWhiteSpace(process))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "Process: " + process,
                    Foreground = TryFindResource("CyberpunkTextBrush") as Brush ?? Brushes.White,
                    FontWeight = FontWeights.Normal,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(2, 2, 2, 0)
                });
            }

            bool isDetailsGrid = false;
            if (_activeGalleryHoverPreviewHost != null)
            {
                bool isInsideDuplicateWindow = false;
                DependencyObject checkParent = _activeGalleryHoverPreviewHost;
                while (checkParent != null)
                {
                    if (checkParent is Window w && w.GetType().Name == "DuplicateWindow")
                    {
                        isInsideDuplicateWindow = true;
                        break;
                    }
                    checkParent = VisualTreeHelper.GetParent(checkParent);
                }

                if (!isInsideDuplicateWindow)
                {
                    DependencyObject p = _activeGalleryHoverPreviewHost;
                    while (p != null)
                    {
                        if (p is DataGrid)
                        {
                            isDetailsGrid = true;
                            break;
                        }
                        p = VisualTreeHelper.GetParent(p);
                    }
                }
            }

            return new ToolTip
            {
                Placement = isDetailsGrid ? System.Windows.Controls.Primitives.PlacementMode.Right : System.Windows.Controls.Primitives.PlacementMode.Mouse,
                PlacementTarget = isDetailsGrid ? _activeGalleryHoverPreviewHost : null,
                HorizontalOffset = isDetailsGrid ? 10 : 0,
                HasDropShadow = true,
                Background = (Brush)new BrushConverter().ConvertFromString("#DD091018"),
                BorderBrush = TryFindResource("CyberpunkCyanBrush") as Brush,
                BorderThickness = new Thickness(1),
                Content = new Border
                {
                    Padding = new Thickness(4),
                    MaxWidth = 360,
                    Child = panel
                }
            };
        }

        private async Task EnsureHitomiLaTagAsync(GalleryItem item, CancellationToken token)
        {
            if (item == null || item.Tag != null)
            {
                return;
            }

            bool isHitomi = string.Equals(item.SourceDomain, "hitomi.la", StringComparison.OrdinalIgnoreCase) ||
                            (item.Link != null && item.Link.IndexOf("hitomi.la", StringComparison.OrdinalIgnoreCase) >= 0);
            if (!isHitomi)
            {
                return;
            }

            try
            {
                string url = item.Link;
                if (string.IsNullOrWhiteSpace(url)) return;

                var idMatch = Regex.Match(url, @"(\d+)(?:\.html)?(?:#.*)?$");
                if (!idMatch.Success) return;

                string id = idMatch.Groups[1].Value;
                string apiJsonUrl = $"https://ltn.gold-usergeneratedcontent.net/galleries/{id}.js";

                string jsContent = await FetchStringAsync(apiJsonUrl, token);
                if (string.IsNullOrEmpty(jsContent)) return;

                string json = jsContent.Replace("var galleryinfo = ", "").Trim();
                if (json.EndsWith(";"))
                {
                    json = json.Substring(0, json.Length - 1).Trim();
                }

                item.Tag = json;
            }
            catch
            {
            }
        }

        private async Task EnsureGalleryHoverPreviewFileAsync(GalleryItem item, CancellationToken token, bool fetchTags = false)
        {
            if (item == null)
            {
                return;
            }

            bool isHitomi = string.Equals(item.SourceDomain, "hitomi.la", StringComparison.OrdinalIgnoreCase) ||
                            (item.Link != null && item.Link.IndexOf("hitomi.la", StringComparison.OrdinalIgnoreCase) >= 0);
            if (fetchTags && isHitomi && item.Tag == null)
            {
                await EnsureHitomiLaTagAsync(item, token);
                if (item.Tag != null)
                {
                    RecalculateDuplicates();
                }
            }

            await EnsureTruyenqqHoverPreviewUrlAsync(item, token);
            List<string> imageUrls = await GetGalleryHoverPreviewCandidateUrlsAsync(item, token);
            if (imageUrls.Count == 0)
            {
                return;
            }

            foreach (string imageUrl in imageUrls)
            {
                if (await TryEnsureGalleryHoverPreviewFileAsync(item, imageUrl, token))
                {
                    item.HoverPreviewThumbnailUrl = imageUrl;
                    return;
                }
            }
        }

        private async Task EnsureTruyenqqHoverPreviewUrlAsync(GalleryItem item, CancellationToken token)
        {
            if (item == null ||
                !string.IsNullOrWhiteSpace(item.HoverPreviewThumbnailUrl) ||
                string.IsNullOrWhiteSpace(item.Link) ||
                !IsTruyenqqUrl(item.Link))
            {
                return;
            }

            try
            {
                string pageUrl = ResolveTruyenqqRequestUrl(item.Link);
                string html = await FetchStringAsync(pageUrl, token);
                string previewUrl = ExtractTruyenqqPreviewUrlFromHtml(html, pageUrl);
                if (!string.IsNullOrWhiteSpace(previewUrl))
                {
                    item.HoverPreviewThumbnailUrl = previewUrl;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }
        }

        private async Task<List<string>> GetGalleryHoverPreviewCandidateUrlsAsync(GalleryItem item, CancellationToken token)
        {
            var imageUrls = new List<string>();
            if (item == null)
            {
                return imageUrls;
            }

            AddGalleryHoverPreviewCandidate(imageUrls, item.HoverPreviewThumbnailUrl);
            if (imageUrls.Count > 0)
            {
                return imageUrls;
            }

            if (!string.IsNullOrWhiteSpace(item.Link) && IsTruyenqqUrl(item.Link))
            {
                foreach (string candidateUrl in await GetCachedGalleryHoverPreviewCandidatesAsync(item.Link, token))
                {
                    AddGalleryHoverPreviewCandidate(imageUrls, candidateUrl);
                }
            }
            else if (!string.IsNullOrWhiteSpace(item.Link) && IsNettruyenUrl(item.Link))
            {
                await SolveNettruyenCaptchaIfNeededAsync(item.Link);
                string html = await FetchStringAsync(item.Link, token);
                string previewUrl = ExtractNettruyenviet10PreviewUrlFromHtml(html, item.Link);
                if (!string.IsNullOrWhiteSpace(previewUrl))
                {
                    item.HoverPreviewThumbnailUrl = previewUrl;
                    AddGalleryHoverPreviewCandidate(imageUrls, previewUrl);
                }

                foreach (string candidateUrl in GetNettruyenviet10PreviewUrlCandidatesFromHtml(html, item.Link))
                {
                    AddGalleryHoverPreviewCandidate(imageUrls, candidateUrl);
                }
            }
            else if (!string.IsNullOrWhiteSpace(item.Link) &&
                item.Link.IndexOf("hentaiforce.net/view/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string html = await FetchStringAsync(item.Link, token);
                foreach (string candidateUrl in GetHentaiforceCoverUrlCandidates(html, item.Link))
                {
                    AddGalleryHoverPreviewCandidate(imageUrls, candidateUrl);
                }
            }
            else if (!string.IsNullOrWhiteSpace(item.Link) && IsEHentaiUrl(item.Link))
            {
                string html = await FetchStringAsync(item.Link, token);
                if (item.Tag == null)
                {
                    ExtractAndApplyEHentaiPreviewTags(item, html);
                }
                string coverUrl = ExtractEHentaiCoverUrl(html, item.Link);
                if (!string.IsNullOrWhiteSpace(coverUrl))
                {
                    AddGalleryHoverPreviewCandidate(imageUrls, coverUrl);
                }
                foreach (string thumbUrl in ExtractEHentaiPreviewThumbnails(html, item.Link))
                {
                    AddGalleryHoverPreviewCandidate(imageUrls, thumbUrl);
                }
            }
            else if (!string.IsNullOrWhiteSpace(item.Link) && IsDilibUrl(item.Link))
            {
                string html = await FetchStringAsync(item.Link, token);
                foreach (string candidateUrl in GetDilibPreviewUrlCandidatesFromHtml(html, item.Link))
                {
                    AddGalleryHoverPreviewCandidate(imageUrls, candidateUrl);
                }
            }
            else if (!string.IsNullOrWhiteSpace(item.Link) && IsMangadexUrl(item.Link))
            {
                try
                {
                    if (TryParseMangadexMangaId(item.Link, out string mangaId, out _))
                    {
                        MangadexMangaData manga = await GetMangadexMangaAsync(mangaId, token);
                        if (manga != null)
                        {
                            string coverUrl = BuildMangadexCoverUrl(manga.Id, manga.CoverFileName);
                            AddGalleryHoverPreviewCandidate(imageUrls, coverUrl);
                        }
                    }
                    else if (TryParseMangadexChapterId(item.Link, out string chapterId, out _))
                    {
                        MangadexChapterData chapter = await GetMangadexChapterAsync(chapterId, token);
                        MangadexMangaData manga = await ResolveMangadexMangaForChapterAsync(chapter, token);
                        if (manga != null)
                        {
                            string coverUrl = BuildMangadexCoverUrl(manga.Id, manga.CoverFileName);
                            AddGalleryHoverPreviewCandidate(imageUrls, coverUrl);
                        }
                    }
                }
                catch
                {
                }
            }
            else if (!string.IsNullOrWhiteSpace(item.Link) && (string.Equals(item.SourceDomain, "hitomi.la", StringComparison.OrdinalIgnoreCase) || item.Link.IndexOf("hitomi.la", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                try
                {
                    if (item.Tag == null)
                    {
                        await EnsureHitomiLaTagAsync(item, token);
                    }
                    if (item.Tag != null)
                    {
                        Newtonsoft.Json.Linq.JObject galleryInfo = null;
                        if (item.Tag is string tagStr)
                        {
                            galleryInfo = Newtonsoft.Json.Linq.JObject.Parse(tagStr);
                        }
                        else
                        {
                            galleryInfo = Newtonsoft.Json.Linq.JToken.FromObject(item.Tag) as Newtonsoft.Json.Linq.JObject;
                        }

                        if (galleryInfo != null && galleryInfo["files"] is Newtonsoft.Json.Linq.JArray files && files.Count > 0)
                        {
                            string firstHash = files[0]["hash"]?.ToString();
                            string firstName = files[0]["name"]?.ToString();
                            if (!string.IsNullOrEmpty(firstHash) && !string.IsNullOrEmpty(firstName))
                            {
                                string thumbUrl = await ResolveHitomiImageUrlAsync(this, firstHash, firstName, isThumbnail: true);
                                if (!string.IsNullOrEmpty(thumbUrl))
                                {
                                    item.HoverPreviewThumbnailUrl = thumbUrl;
                                    AddGalleryHoverPreviewCandidate(imageUrls, thumbUrl);
                                }
                            }
                        }
                    }
                }
                catch {}
            }

            return imageUrls;
        }

        private async Task<List<string>> GetCachedGalleryHoverPreviewCandidatesAsync(string link, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(link))
            {
                return new List<string>();
            }

            lock (_galleryHoverPreviewCandidateCacheLock)
            {
                if (_galleryHoverPreviewCandidateCache.TryGetValue(link, out List<string> cachedCandidates))
                {
                    return new List<string>(cachedCandidates);
                }
            }

            string pageUrl = ResolveTruyenqqRequestUrl(link);
            string html = await FetchStringAsync(pageUrl, token);
            List<string> candidates = GetTruyenqqPreviewUrlCandidatesFromHtml(html, pageUrl)
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            lock (_galleryHoverPreviewCandidateCacheLock)
            {
                _galleryHoverPreviewCandidateCache[link] = candidates;
            }

            return new List<string>(candidates);
        }

        private void RefreshVisibleGalleryHoverPreviewBindings()
        {
            IEnumerable<GalleryItem> visibleItems = Enumerable.Empty<GalleryItem>();
            if (_isResultsThumbnailViewEnabled)
            {
                visibleItems = _thumbnailVisibleItems;
            }
            else if (ResultsView != null)
            {
                visibleItems = ResultsView.Cast<object>().OfType<GalleryItem>();
            }

            foreach (GalleryItem item in visibleItems.Where(SupportsHoverPreview).Distinct())
            {
                item.RefreshHoverPreviewBindings();
            }
        }

        private static void AddGalleryHoverPreviewCandidate(List<string> imageUrls, string imageUrl)
        {
            string cleanUrl = imageUrl?.Trim();
            if (string.IsNullOrWhiteSpace(cleanUrl) ||
                imageUrls.Contains(cleanUrl, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            imageUrls.Add(cleanUrl);
        }

        private string ExtractNettruyenviet10PreviewUrlFromHtml(string html, string pageUrl)
        {
            return GetNettruyenviet10PreviewUrlCandidatesFromHtml(html, pageUrl).FirstOrDefault() ?? string.Empty;
        }

        private static List<string> GetNettruyenviet10PreviewUrlCandidatesFromHtml(string html, string pageUrl)
        {
            var urls = new List<string>();
            if (string.IsNullOrWhiteSpace(html))
            {
                return urls;
            }

            string imageHtml = string.Empty;
            Match imageBlockMatch = Regex.Match(
                html,
                @"<div[^>]*class=[""'][^""']*\bcol-image\b[^""']*[""'][^>]*>(?<content>[\s\S]*?)</div>",
                RegexOptions.IgnoreCase);
            if (imageBlockMatch.Success)
            {
                imageHtml = imageBlockMatch.Groups["content"].Value;
            }

            CollectNettruyenviet10PreviewUrls(imageHtml, pageUrl, urls);
            if (urls.Count == 0)
            {
                CollectNettruyenviet10PreviewUrls(html, pageUrl, urls);
            }

            ValidateNettruyenviet10PreviewParser();

            return urls;
        }

        private static void CollectNettruyenviet10PreviewUrls(string htmlFragment, string pageUrl, List<string> urls)
        {
            if (string.IsNullOrWhiteSpace(htmlFragment) || urls == null)
            {
                return;
            }

            foreach (Match match in Regex.Matches(
                htmlFragment,
                @"\b(?:data-retries|src|data-src|data-original|data-lazy)=[""'](?<url>https?://[^""']+?\.(?:jpe?g|png|webp)(?:\?[^""']*)?)[""']",
                RegexOptions.IgnoreCase))
            {
                foreach (string candidate in SplitNettruyenviet10PreviewUrlCandidates(match.Groups["url"].Value, pageUrl))
                {
                    if (!string.IsNullOrWhiteSpace(candidate) &&
                        !urls.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                    {
                        urls.Add(candidate);
                    }
                }
            }

            foreach (Match match in Regex.Matches(
                htmlFragment,
                @"https?://[^""'\s>]+?\.(?:jpe?g|png|webp)(?:\?[^""'\s>]*)?",
                RegexOptions.IgnoreCase))
            {
                string normalizedUrl = NormalizeNettruyenviet10PreviewUrl(match.Value, pageUrl);
                if (!string.IsNullOrWhiteSpace(normalizedUrl) &&
                    !urls.Contains(normalizedUrl, StringComparer.OrdinalIgnoreCase))
                {
                    urls.Add(normalizedUrl);
                }
            }
        }

        private static IEnumerable<string> SplitNettruyenviet10PreviewUrlCandidates(string imageUrl, string pageUrl)
        {
            string cleanUrl = WebUtility.HtmlDecode(imageUrl ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(cleanUrl))
            {
                return Enumerable.Empty<string>();
            }

            string[] pieces = cleanUrl.Split(new[] { '\r', '\n', '\t', ' ', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
            if (pieces.Length == 0)
            {
                pieces = new[] { cleanUrl };
            }

            return pieces
                .Select(piece => NormalizeNettruyenviet10PreviewUrl(piece, pageUrl))
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string NormalizeNettruyenviet10PreviewUrl(string imageUrl, string pageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                return string.Empty;
            }

            string cleanUrl = WebUtility.HtmlDecode(imageUrl).Trim();
            if (string.IsNullOrWhiteSpace(cleanUrl))
            {
                return string.Empty;
            }

            if (Uri.TryCreate(cleanUrl, UriKind.Absolute, out Uri absoluteUri))
            {
                return absoluteUri.ToString();
            }

            if (Uri.TryCreate(new Uri(pageUrl), cleanUrl, out Uri resolvedUri))
            {
                return resolvedUri.ToString();
            }

            return cleanUrl;
        }

        [Conditional("DEBUG")]
        private static void ValidateNettruyenviet10PreviewParser()
        {
            var urls = new List<string>();
            CollectNettruyenviet10PreviewUrls(
                @"<div class=""col-xs-4 col-image""><img data-retries=""https://image2.kcgsbok.com/nettruyen/thumb/ryoumin-0-nin-start-no-henkyou-ryoushusama.jpg"" src=""https://image2.kcgsbok.com/nettruyen/thumb/ryoumin-0-nin-start-no-henkyou-ryoushusama.jpg"" data-src=""https://image2.kcgsbok.com/nettruyen/thumb/ryoumin-0-nin-start-no-henkyou-ryoushusama.jpg"" class=""image-thumb""></div>",
                "https://nettruyenviet10.com/truyen-tranh/ryoumin-0-nin-start-no-henkyou-ryoushusama",
                urls);
            Debug.Assert(urls.FirstOrDefault() == "https://image2.kcgsbok.com/nettruyen/thumb/ryoumin-0-nin-start-no-henkyou-ryoushusama.jpg");

            urls.Clear();
            CollectNettruyenviet10PreviewUrls(
                @"<div class=""col-xs-4 col-image""><img src=""https://nettruyenapp.club.org/storage/images/thumbnails/bach-luyen-thanh-than.webp"" alt=""Bách Luyện Thành Thần""></div>",
                "https://nettruyen.tech/truyen-tranh/bach-luyen-thanh-than",
                urls);
            Debug.Assert(urls.FirstOrDefault() == "https://nettruyenapp.club.org/storage/images/thumbnails/bach-luyen-thanh-than.webp");
        }

        private async Task<bool> TryEnsureGalleryHoverPreviewFileAsync(GalleryItem item, string imageUrl, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(imageUrl) || _galleryHoverPreviewBitmapMissingCache.Contains(imageUrl))
            {
                return false;
            }

            string originalPath = item.HoverPreviewLocalPath;
            string thumbnailPath = item.HoverPreviewThumbnailLocalPath;
            if (!string.IsNullOrWhiteSpace(originalPath) &&
                !string.IsNullOrWhiteSpace(thumbnailPath) &&
                File.Exists(originalPath) &&
                File.Exists(thumbnailPath))
            {
                return true;
            }

            string cacheBasePath = GetGalleryHoverPreviewCacheBasePath(item, imageUrl);
            if (TryGetGalleryHoverPreviewCacheFiles(cacheBasePath, out originalPath, out thumbnailPath))
            {
                item.HoverPreviewLocalPath = originalPath;
                item.HoverPreviewThumbnailLocalPath = thumbnailPath;

                // Load tag từ file .txt cùng tên định dạng .json nếu tồn tại
                string txtTagPath = cacheBasePath + ".txt";
                if (item.Tag == null && File.Exists(txtTagPath))
                {
                    try
                    {
                        string content = File.ReadAllText(txtTagPath);
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            item.Tag = Newtonsoft.Json.Linq.JObject.Parse(content);
                        }
                    }
                    catch { }
                }
                return true;
            }

            EnsureCacheSizeLimit();
            await _galleryHoverPreviewImageSemaphore.WaitAsync(token);
            try
            {
                if (TryGetGalleryHoverPreviewCacheFiles(cacheBasePath, out originalPath, out thumbnailPath))
                {
                    item.HoverPreviewLocalPath = originalPath;
                    item.HoverPreviewThumbnailLocalPath = thumbnailPath;
                    return true;
                }

                string domainFolder = GetDomainFolderName(item, imageUrl);
                string previewRoot = Path.Combine(PortablePaths.PortableTempRoot, "preview-cache", domainFolder);
                Directory.CreateDirectory(previewRoot);
                ServicePoint previewServicePoint = ServicePointManager.FindServicePoint(new Uri(imageUrl));
                previewServicePoint.ConnectionLimit = Math.Max(previewServicePoint.ConnectionLimit, 8);

                if (IsMangadexBrowserFetchUrl(imageUrl))
                {
                    string originalExtension = ".webp";
                    originalPath = cacheBasePath + originalExtension;

                    byte[] browserBytes = await FetchMangadexBytesViaBrowserAsync(imageUrl, item?.Link, token);
                    using (FileStream fileStream = new FileStream(originalPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        await fileStream.WriteAsync(browserBytes, 0, browserBytes.Length, token);
                    }

                    item.HoverPreviewLocalPath = originalPath;
                    item.HoverPreviewThumbnailLocalPath = originalPath;
                    return true;
                }

                using (HttpClient client = CreateScopedHttpClient(imageUrl))
                using (var request = new HttpRequestMessage(HttpMethod.Get, imageUrl))
                {
                    if (!string.IsNullOrWhiteSpace(item.Link))
                    {
                        request.Headers.Referrer = new Uri(item.Link);
                    }
                    else if (imageUrl.Contains("thuviensach.vn"))
                    {
                        request.Headers.Referrer = new Uri("https://thuviensach.vn/");
                    }
                    else if (imageUrl.Contains("gold-usergeneratedcontent.net") || imageUrl.Contains("hitomi.la"))
                    {
                        request.Headers.Referrer = new Uri("https://hitomi.la/");
                    }

                    // Tối ưu hóa Keep-Alive để đẩy tốc độ tải ảnh bìa thuviensach
                    request.Headers.ConnectionClose = false;

                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token))
                    {
                        response.EnsureSuccessStatusCode();
                        string originalExtension = ".webp";
                        originalPath = cacheBasePath + originalExtension;

                        using (Stream sourceStream = await response.Content.ReadAsStreamAsync())
                        using (FileStream fileStream = new FileStream(originalPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                        {
                            await sourceStream.CopyToAsync(fileStream, 81920, token);
                        }

                        item.HoverPreviewLocalPath = originalPath;
                        item.HoverPreviewThumbnailLocalPath = originalPath;
                        return true;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                _galleryHoverPreviewBitmapMissingCache.Add(imageUrl);
                return false;
            }
            finally
            {
                _galleryHoverPreviewImageSemaphore.Release();
            }
        }

        private static string GetSanitizedFileName(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            string sanitized = input
                .Replace('?', '？')
                .Replace('¿', '？')
                .Replace('*', '＊')
                .Replace('<', '＜')
                .Replace('>', '＞')
                .Replace('"', '＂')
                .Replace(':', '-')
                .Replace('|', '-')
                .Replace('/', '-')
                .Replace('\\', '-');

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                sanitized = sanitized.Replace(c, '_');
            }
            return sanitized.Trim().TrimEnd('.', '_', '-');
        }

        private static DateTime _lastCacheLimitCheck = DateTime.MinValue;

        private static void EnsureCacheSizeLimit()
        {
            try
            {
                if ((DateTime.UtcNow - _lastCacheLimitCheck).TotalSeconds < 60)
                {
                    return;
                }
                _lastCacheLimitCheck = DateTime.UtcNow;

                string previewRoot = Path.Combine(PortablePaths.PortableTempRoot, "preview-cache");
                if (!Directory.Exists(previewRoot))
                {
                    return;
                }

                DirectoryInfo dir = new DirectoryInfo(previewRoot);
                FileInfo[] files = dir.GetFiles("*", SearchOption.AllDirectories);
                long totalSize = files.Sum(f => f.Length);
                if (totalSize > 209715200) // 200 MB
                {
                    // Xóa LRU: file cũ nhất trước, giảm xuống còn 150 MB
                    foreach (FileInfo file in files.OrderBy(f => f.LastAccessTimeUtc))
                    {
                        try
                        {
                            long len = file.Length;
                            file.Delete();
                            totalSize -= len;
                            if (totalSize <= 157286400) // 150 MB
                            {
                                break;
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
        }

        public static string GetDomainFolderName(GalleryItem item, string imageUrl)
        {
            string domain = item?.SourceDomain;
            if (string.IsNullOrWhiteSpace(domain) && !string.IsNullOrWhiteSpace(item?.Link))
            {
                try
                {
                    var uri = new Uri(item.Link);
                    domain = uri.Host;
                }
                catch {}
            }
            if (string.IsNullOrWhiteSpace(domain) && !string.IsNullOrWhiteSpace(imageUrl))
            {
                try
                {
                    var uri = new Uri(imageUrl);
                    domain = uri.Host;
                }
                catch {}
            }
            if (string.IsNullOrWhiteSpace(domain))
            {
                domain = "unknown";
            }
            
            domain = domain.ToLowerInvariant().Replace("www.", "");
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                domain = domain.Replace(c, '_');
            }
            return domain;
        }

        public static string GetGalleryHoverPreviewCacheBasePath(GalleryItem item, string imageUrl)
        {
            string domainFolder = GetDomainFolderName(item, imageUrl);
            string previewRoot = Path.Combine(PortablePaths.PortableTempRoot, "preview-cache", domainFolder);
            string bookName = item?.Name;
            string sanitizedBook = GetSanitizedFileName(bookName);
            if (!string.IsNullOrWhiteSpace(sanitizedBook))
            {
                return Path.Combine(previewRoot, sanitizedBook);
            }

            using (SHA1 sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(imageUrl ?? string.Empty));
                string fileName = BitConverter.ToString(hash).Replace("-", string.Empty);
                return Path.Combine(previewRoot, fileName);
            }
        }

        public static void RenameGalleryHoverPreviewCache(string oldName, string newName, GalleryItem item)
        {
            if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName) || oldName == newName || item == null)
            {
                return;
            }

            try
            {
                string domainFolder = GetDomainFolderName(item, item.HoverPreviewThumbnailUrl);
                string previewRoot = Path.Combine(PortablePaths.PortableTempRoot, "preview-cache", domainFolder);
                if (!Directory.Exists(previewRoot)) return;

                string oldSanitized = GetSanitizedFileName(oldName);
                string newSanitized = GetSanitizedFileName(newName);
                if (string.IsNullOrWhiteSpace(oldSanitized) || string.IsNullOrWhiteSpace(newSanitized) || oldSanitized == newSanitized)
                {
                    return;
                }

                string oldBasePath = Path.Combine(previewRoot, oldSanitized);
                string newBasePath = Path.Combine(previewRoot, newSanitized);

                string[] extensions = { ".webp", ".txt" };
                foreach (string ext in extensions)
                {
                    string oldPath = oldBasePath + ext;
                    string newPath = newBasePath + ext;
                    if (File.Exists(oldPath))
                    {
                        if (File.Exists(newPath))
                        {
                            try { File.Delete(newPath); } catch {}
                        }
                        File.Move(oldPath, newPath);
                    }
                }

                if (File.Exists(newBasePath + ".webp"))
                {
                    item.HoverPreviewLocalPath = newBasePath + ".webp";
                    item.HoverPreviewThumbnailLocalPath = newBasePath + ".webp";
                }
            }
            catch { }
        }

        private static bool TryGetGalleryHoverPreviewCacheFiles(string cacheBasePath, out string originalPath, out string thumbnailPath)
        {
            originalPath = null;
            thumbnailPath = null;

            string directory = Path.GetDirectoryName(cacheBasePath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return false;
            }

            string fileBaseName = Path.GetFileName(cacheBasePath);
            if (string.IsNullOrWhiteSpace(fileBaseName))
            {
                return false;
            }

            string[] candidateExtensions = { ".webp", ".jpg", ".png", ".jpeg" };
            foreach (string ext in candidateExtensions)
            {
                string testPath = Path.Combine(directory, fileBaseName + ext);
                if (File.Exists(testPath))
                {
                    originalPath = testPath;
                    thumbnailPath = testPath;
                    return true;
                }
            }

            // Fallback: nếu tên file có [Language] mà không thấy, thử tìm file không có [Language]
            var langRegex = new Regex(@"\s*\[(english|japanese|korean|chinese|vietnamese|french|spanish|german|russian|italian|portuguese|thai|indonesian|日本語|中文|한국어)\]", RegexOptions.IgnoreCase);
            if (langRegex.IsMatch(fileBaseName))
            {
                string cleanBaseName = langRegex.Replace(fileBaseName, "").Trim();
                foreach (string ext in candidateExtensions)
                {
                    string testPath = Path.Combine(directory, cleanBaseName + ext);
                    if (File.Exists(testPath))
                    {
                        originalPath = testPath;
                        thumbnailPath = testPath;
                        return true;
                    }
                }
            }

            return false;
        }

        private static string GetGalleryHoverPreviewFileExtension(string imageUrl, string contentType)
        {
            string extension = null;
            if (!string.IsNullOrWhiteSpace(imageUrl))
            {
                try
                {
                    extension = Path.GetExtension(new Uri(imageUrl).AbsolutePath);
                }
                catch
                {
                }
            }

            if (!string.IsNullOrWhiteSpace(extension) && extension.Length <= 5)
            {
                return extension;
            }

            switch ((contentType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "image/png":
                    return ".png";
                case "image/gif":
                    return ".gif";
                case "image/bmp":
                    return ".bmp";
                case "image/webp":
                    return ".webp";
                default:
                    return ".jpg";
            }
        }

        private static void CreateGalleryHoverPreviewThumbnail(string originalPath, string thumbnailPath)
        {
            if (string.IsNullOrWhiteSpace(originalPath) || !File.Exists(originalPath) || string.IsNullOrWhiteSpace(thumbnailPath))
            {
                return;
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.DecodePixelWidth = 220;
                bitmap.UriSource = new Uri(originalPath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();

                var encoder = new JpegBitmapEncoder
                {
                    QualityLevel = 72
                };
                encoder.Frames.Add(BitmapFrame.Create(bitmap));

                using (FileStream thumbStream = new FileStream(thumbnailPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    encoder.Save(thumbStream);
                }
            }
            catch
            {
                if (!File.Exists(thumbnailPath))
                {
                    File.Copy(originalPath, thumbnailPath, true);
                }
            }
        }

        private static ImageSource CreatePreviewImageSource(string localPath, int decodePixelWidth)
        {
            if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
            {
                return null;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            if (decodePixelWidth > 0)
            {
                bitmap.DecodePixelWidth = decodePixelWidth;
            }
            bitmap.UriSource = new Uri(localPath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
    }
}

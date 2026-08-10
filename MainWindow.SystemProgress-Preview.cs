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
        private readonly SemaphoreSlim _galleryHoverPreviewImageSemaphore = new SemaphoreSlim(24, 24);
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
                    await EnsureGalleryHoverPreviewAsync(item);
                    await EnsureGalleryHoverPreviewFileAsync(item, token);
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
            if (item == null || (!string.IsNullOrWhiteSpace(item.HoverPreviewLocalPath) && File.Exists(item.HoverPreviewLocalPath)))
            {
                return;
            }

            try
            {
                item.IsHoverPreviewLoading = true;
                await EnsureGalleryHoverPreviewAsync(item);
                await EnsureGalleryHoverPreviewFileAsync(item, CancellationToken.None);
            }
            catch
            {
            }
            finally
            {
                item.IsHoverPreviewLoading = false;
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
                MaxWidth = 320,
                MaxHeight = 480,
                Source = CreatePreviewImageSource(item?.HoverPreviewLocalPath, 0)
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
                    string.Equals(item.SourceDomain, "nhentai.net", StringComparison.OrdinalIgnoreCase))
                {
                    hasTagsSupport = true;
                }
                else if (item.Link != null && (item.Link.IndexOf("hitomi.la", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                               item.Link.IndexOf("nhentai.net", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                               IsTruyenqqUrl(item.Link)))
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

                    if (galleryInfo != null && galleryInfo["tags"] is Newtonsoft.Json.Linq.JArray tagsArray)
                    {
                        var tagsList = new List<string>();
                        foreach (var t in tagsArray)
                        {
                            string tagName = t["tag"]?.ToString();
                            if (!string.IsNullOrWhiteSpace(tagName))
                            {
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
                    MaxWidth = 340,
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

        private async Task EnsureNhentaiNetTagAsync(GalleryItem item, CancellationToken token)
        {
            if (item == null || item.Tag != null) return;

            string link = item.Link ?? string.Empty;
            lock (_tagFetchFailedCacheLock)
            {
                if (_tagFetchFailedCache.Contains(link)) return;
            }

            await _tagFetchSemaphore.WaitAsync(token);
            try
            {
                // Double-check sau khi qua semaphore
                if (item.Tag != null) return;
                lock (_tagFetchFailedCacheLock)
                {
                    if (_tagFetchFailedCache.Contains(link)) return;
                }

                string galleryId = GetNhentaiGalleryIdFromLink(link);
                if (string.IsNullOrEmpty(galleryId)) return;

                // Jitter nhỏ trước request để tránh burst khi nhiều item hit semaphore cùng lúc
                int jitter;
                lock (_tagFetchJitter) { jitter = _tagFetchJitter.Next(100, 500); }
                await Task.Delay(jitter, token);

                string apiUrl = $"https://nhentai.net/api/gallery/{galleryId}";
                string jsonContent;
                try
                {
                    jsonContent = await FetchStringAsync(apiUrl, token);
                }
                catch (Exception ex) when (ex.Message.Contains("429") || (ex.InnerException?.Message?.Contains("429") == true))
                {
                    // 429: back-off 10s rồi mới cho item khác qua
                    lock (_tagFetchFailedCacheLock) { _tagFetchFailedCache.Add(link); }
                    Log($"[Preview] nhentai 429 – tạm bỏ qua tag cho {galleryId}, retry sau.");
                    try { await Task.Delay(10000, token); } catch { }
                    return;
                }
                catch { return; }

                if (string.IsNullOrEmpty(jsonContent)) return;

                try
                {
                    var galleryInfo = Newtonsoft.Json.Linq.JObject.Parse(jsonContent);
                    if (galleryInfo == null) return;

                    // nhentai API trả error khi 429/404
                    if (galleryInfo["error"] != null)
                    {
                        lock (_tagFetchFailedCacheLock) { _tagFetchFailedCache.Add(link); }
                        return;
                    }

                    if (galleryInfo["tags"] is Newtonsoft.Json.Linq.JArray tagsArray)
                    {
                        var jArr = new Newtonsoft.Json.Linq.JArray();
                        var langsList = new List<string>();

                        foreach (var t in tagsArray)
                        {
                            string type = t["type"]?.ToString();
                            string name = t["name"]?.ToString();
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                if (string.Equals(type, "language", StringComparison.OrdinalIgnoreCase))
                                    langsList.Add(name);
                                var tObj = new Newtonsoft.Json.Linq.JObject();
                                tObj["tag"] = name;
                                jArr.Add(tObj);
                            }
                        }

                        var jTagsObj = new Newtonsoft.Json.Linq.JObject();
                        jTagsObj["tags"] = jArr;

                        var displayLangs = langsList.Where(l => l != "translated").ToList();
                        string currentName = CleanTranslatedTagFromTitle(item.Name);

                        if (displayLangs.Count > 0)
                        {
                            string langStr = string.Join(", ", displayLangs.Select(l => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(l)));
                            string suffix = $"[{langStr}]";
                            if (!currentName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                                currentName = $"{currentName} {suffix}";
                        }

                        Dispatcher.Invoke(() =>
                        {
                            if (item.Name != currentName) item.Name = currentName;
                            item.Tag = jTagsObj;
                            RecalculateDuplicates();
                        });
                    }
                }
                catch { }
            }
            finally
            {
                // Delay nhỏ sau mỗi request để server không bị quá tải
                try { await Task.Delay(300, token); } catch { }
                _tagFetchSemaphore.Release();
            }
        }

        private async Task EnsureGalleryHoverPreviewFileAsync(GalleryItem item, CancellationToken token)
        {
            if (item == null)
            {
                return;
            }

            bool isHitomi = string.Equals(item.SourceDomain, "hitomi.la", StringComparison.OrdinalIgnoreCase) ||
                            (item.Link != null && item.Link.IndexOf("hitomi.la", StringComparison.OrdinalIgnoreCase) >= 0);
            if (isHitomi && item.Tag == null)
            {
                await EnsureHitomiLaTagAsync(item, token);
                if (item.Tag != null)
                {
                    RecalculateDuplicates();
                }
            }

            bool isNhentai = string.Equals(item.SourceDomain, "nhentai.net", StringComparison.OrdinalIgnoreCase) ||
                             (item.Link != null && item.Link.IndexOf("nhentai.net", StringComparison.OrdinalIgnoreCase) >= 0);
            if (isNhentai && item.Tag == null)
            {
                await EnsureNhentaiNetTagAsync(item, token);
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
            if (!string.IsNullOrWhiteSpace(item.Link) && IsTruyenqqUrl(item.Link))
            {
                foreach (string candidateUrl in await GetCachedGalleryHoverPreviewCandidatesAsync(item.Link, token))
                {
                    AddGalleryHoverPreviewCandidate(imageUrls, candidateUrl);
                }
            }
            else if (!string.IsNullOrWhiteSpace(item.Link) && IsNettruyenUrl(item.Link))
            {
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
            else if (!string.IsNullOrWhiteSpace(item.Link) && IsHaibabaUrl(item.Link))
            {
                string html = await FetchStringAsync(item.Link, token);
                foreach (string candidateUrl in GetHaibabaPreviewUrlCandidatesFromHtml(html, item.Link))
                {
                    AddGalleryHoverPreviewCandidate(imageUrls, candidateUrl);
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
            else if (!string.IsNullOrWhiteSpace(item.Link) && IsNhentaiUrl(item.Link))
            {
                try
                {
                    string html = await FetchStringAsync(item.Link, token);
                    if (!string.IsNullOrEmpty(html))
                    {
                        string coverUrl = ExtractNhentaiNetGalleryCover(html);
                        if (!string.IsNullOrWhiteSpace(coverUrl))
                        {
                            item.HoverPreviewThumbnailUrl = coverUrl;
                            AddGalleryHoverPreviewCandidate(imageUrls, coverUrl);
                        }

                        var langs = ExtractNhentaiNetLanguages(html);
                        var displayLangs = langs.Where(l => l != "translated").ToList();
                        string currentName = CleanTranslatedTagFromTitle(item.Name);

                        if (displayLangs.Count > 0)
                        {
                            string langStr = string.Join(", ", displayLangs.Select(l => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(l)));
                            string suffix = $"[{langStr}]";
                            if (!currentName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                            {
                                currentName = $"{currentName} {suffix}";
                            }
                        }

                        var nhentaiTags = ExtractNhentaiNetTags(html);
                        Newtonsoft.Json.Linq.JObject jTagsObj = null;
                        if (nhentaiTags.Count > 0)
                        {
                            var jArr = new Newtonsoft.Json.Linq.JArray();
                            foreach (var tag in nhentaiTags)
                            {
                                var tObj = new Newtonsoft.Json.Linq.JObject();
                                tObj["tag"] = tag;
                                jArr.Add(tObj);
                            }
                            jTagsObj = new Newtonsoft.Json.Linq.JObject();
                            jTagsObj["tags"] = jArr;
                        }

                        Dispatcher.Invoke(() =>
                        {
                            if (item.Name != currentName)
                            {
                                item.Name = currentName;
                            }
                            if (jTagsObj != null)
                            {
                                item.Tag = jTagsObj;
                                RecalculateDuplicates();
                            }
                        });
                    }
                }
                catch
                {
                }
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
                @"(?:data-retries|src|data-src|data-original|data-lazy)=[""'](?<url>[^""']+?\.(?:jpe?g|png|webp)(?:\?[^""']*)?)[""']",
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
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                input = input.Replace(c, '_');
            }
            return input.Trim();
        }

        private static void EnsureCacheSizeLimit()
        {
            try
            {
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

        private static string GetGalleryHoverPreviewCacheBasePath(GalleryItem item, string imageUrl)
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

            originalPath = Path.Combine(directory, fileBaseName + ".webp");

            if (!File.Exists(originalPath))
            {
                originalPath = null;
                return false;
            }

            thumbnailPath = originalPath;
            return true;
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

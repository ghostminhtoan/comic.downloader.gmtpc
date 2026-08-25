using System;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private CancellationTokenSource _downloadCts;
        private volatile bool _isDownloadPaused = false;
        private static readonly ConcurrentDictionary<string, DateTime> _tempLogWriteTimes = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, DateTime> _processWriteTimes = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly object _downloadSessionLock = new object();
        private readonly HashSet<GalleryItem> _scheduledDownloadItems = new HashSet<GalleryItem>();
        private readonly List<Task> _scheduledDownloadTasks = new List<Task>();
        private readonly Dictionary<GalleryItem, int> _scheduledDownloadOrder = new Dictionary<GalleryItem, int>();
        private readonly ConcurrentDictionary<GalleryItem, CancellationTokenSource> _activeItemCancellationSources = new ConcurrentDictionary<GalleryItem, CancellationTokenSource>();
        private string _activeDownloadRoot;
        private int _downloadSessionTotalGalleries;
        private int _downloadSessionCompletedGalleries;
        private int _nextScheduledDownloadOrder = 0;
        private int _nextDownloadStartOrder = 0;
        private bool _suppressDownloadToggleEvent;
        private readonly ConcurrentDictionary<GalleryItem, DateTime> _lastMetricUpdateTimes = new ConcurrentDictionary<GalleryItem, DateTime>();
        private volatile int _cachedConnectionLimit = 4;
        private volatile int _cachedMultiDownloadLimit = 2;

        public static string GetDoneProcessText(GalleryItem item, bool hasErrors)
        {
            string pagesInfo = null;
            if (item != null)
            {
                if (!string.IsNullOrEmpty(item.DownloadingPageProgress))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(item.DownloadingPageProgress, @"(\d+/\d+)");
                    if (match.Success)
                    {
                        pagesInfo = match.Value;
                    }
                }
                if (string.IsNullOrEmpty(pagesInfo) && !string.IsNullOrEmpty(item.CurrentProcess))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(item.CurrentProcess, @"(\d+/\d+)");
                    if (match.Success)
                    {
                        pagesInfo = match.Value;
                    }
                }
            }

            if (!string.IsNullOrEmpty(pagesInfo))
            {
                return hasErrors ? $"Done with errors ({pagesInfo})" : $"Done ({pagesInfo})";
            }
            return hasErrors ? "Done with errors" : "Done";
        }

        public static string GetDoneProcessTextForGroup(IEnumerable<GalleryItem> group, bool hasErrors)
        {
            int totalPagesSum = 0;
            int completedPagesSum = 0;
            bool hasPageInfo = false;
            if (group != null)
            {
                foreach (var child in group)
                {
                    if (child == null) continue;
                    if (!string.IsNullOrEmpty(child.DownloadingPageProgress))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(child.DownloadingPageProgress, @"(\d+)/(\d+)");
                        if (match.Success && int.TryParse(match.Groups[1].Value, out int comp) && int.TryParse(match.Groups[2].Value, out int tot))
                        {
                            completedPagesSum += comp;
                            totalPagesSum += tot;
                            hasPageInfo = true;
                        }
                    }
                    else if (!string.IsNullOrEmpty(child.CurrentProcess))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(child.CurrentProcess, @"(\d+)/(\d+)");
                        if (match.Success && int.TryParse(match.Groups[1].Value, out int comp) && int.TryParse(match.Groups[2].Value, out int tot))
                        {
                            completedPagesSum += comp;
                            totalPagesSum += tot;
                            hasPageInfo = true;
                        }
                    }
                }
            }

            if (hasPageInfo)
            {
                return hasErrors ? $"Done with errors ({completedPagesSum}/{totalPagesSum})" : $"Done ({completedPagesSum}/{totalPagesSum})";
            }
            return hasErrors ? "Done with errors" : "Done";
        }

        private void UpdateTotalDownloadSpeedHeader()
        {
            Dispatcher.Invoke(() =>
            {
                if (txtSpeedHeader == null) return;

                long totalSpeed = 0;
                foreach (var item in _scrapedItems)
                {
                    totalSpeed += item.DownloadSpeedBytesPerSecond;
                }

                if (totalSpeed > 0)
                {
                    txtSpeedHeader.Text = $"SPEED - {GalleryItem.FormatSpeedText(totalSpeed)}";
                }
                else
                {
                    txtSpeedHeader.Text = "SPEED";
                }

                UpdateGlobalDownloadProgress();
            });
        }

        private void UpdateGlobalDownloadProgress()
        {
            if (globalDownloadProgressBar == null) return;
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(UpdateGlobalDownloadProgress));
                return;
            }

            bool isDownloading = btnStartDownload?.IsChecked == true && _scrapedItems.Any(i => i.IsChecked && (string.Equals(i.Status, "Downloading", StringComparison.OrdinalIgnoreCase) || string.Equals(i.Status, "Paused", StringComparison.OrdinalIgnoreCase)));
            if (!isDownloading)
            {
                globalDownloadProgressBar.Visibility = Visibility.Collapsed;
                globalDownloadProgressBar.Value = 0;
                if (grdGlobalProgress != null) grdGlobalProgress.Visibility = Visibility.Collapsed;
                return;
            }

            globalDownloadProgressBar.Visibility = Visibility.Visible;
            if (grdGlobalProgress != null) grdGlobalProgress.Visibility = Visibility.Visible;

            double totalProgress = 0;
            int count = 0;
            foreach (var item in _scrapedItems.Where(i => i.IsChecked))
            {
                if (string.Equals(item.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                {
                    totalProgress += 100;
                }
                else if (string.Equals(item.Status, "Downloading", StringComparison.OrdinalIgnoreCase) || string.Equals(item.Status, "Paused", StringComparison.OrdinalIgnoreCase) || string.Equals(item.Status, "Queued", StringComparison.OrdinalIgnoreCase))
                {
                    totalProgress += item.DownloadProgressPercent;
                }
                count++;
            }

            double overallPercent = 0;
            if (count > 0)
            {
                overallPercent = totalProgress / count;
                globalDownloadProgressBar.Value = overallPercent;
                if (prgGlobalDownload != null) prgGlobalDownload.Value = overallPercent;
            }
            else
            {
                globalDownloadProgressBar.Value = 0;
                if (prgGlobalDownload != null) prgGlobalDownload.Value = 0;
            }

            if (txtGlobalProgressStats != null)
            {
                int completed = _scrapedItems.Count(item => item.IsChecked &&
                    (string.Equals(item.Status, "Completed", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(item.DownloadingPageProgress, "Done", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(item.DownloadingPageProgress, "Complete", StringComparison.OrdinalIgnoreCase)));
                int totalToDownload = _scrapedItems.Count(i => i.IsChecked);
                long totalSpeed = 0;
                foreach (var item in _scrapedItems)
                {
                    totalSpeed += item.DownloadSpeedBytesPerSecond;
                }
                string speedStr = totalSpeed > 0 ? $" | Tốc độ: {GalleryItem.FormatSpeedText(totalSpeed)}" : "";
                txtGlobalProgressStats.Text = $"{completed}/{totalToDownload} truyện ({overallPercent:F0}%){speedStr}";
            }
        }

        private void QueueParallelSplitCollapseIfReady(GalleryItem item)
        {
            if (item == null || !item.IsParallelSplitTask || string.IsNullOrWhiteSpace(item.ChapterSelectionText))
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                TryCollapseParallelSplitTasks(item);
            }));
        }

        private void TryCollapseParallelSplitTasks(GalleryItem item)
        {
            if (item == null || !item.IsParallelSplitTask)
            {
                return;
            }

            string normalizedLink = NormalizeProcessLink(item.Link);
            if (string.IsNullOrWhiteSpace(normalizedLink))
            {
                return;
            }

            List<GalleryItem> group = _scrapedItems
                .Where(candidate =>
                    candidate != null &&
                    candidate.IsParallelSplitTask &&
                    string.Equals(NormalizeProcessLink(candidate.Link), normalizedLink, StringComparison.OrdinalIgnoreCase))
                .OrderBy(candidate => candidate.OriginalIndex)
                .ToList();

            if (group.Count <= 1 || !group.Any(candidate => !string.IsNullOrWhiteSpace(candidate.ChapterSelectionText)))
            {
                return;
            }

            if (group.Any(candidate => !IsParallelSplitTerminalStatus(candidate.Status)))
            {
                return;
            }

            int insertIndex = _scrapedItems.IndexOf(group[0]);
            if (insertIndex < 0)
            {
                return;
            }

            GalleryItem mergedItem = BuildCollapsedParallelSplitItem(group);
            foreach (GalleryItem splitItem in group)
            {
                _scrapedItems.Remove(splitItem);
            }

            _scrapedItems.Insert(insertIndex, mergedItem);
            RenumberResultOrder();
            SafeRefreshResultsView();
            EnsureDownloadMissingChapterRowsFromGallery(forceSyncOrder: true);
            SyncAllGalleryMissingChapterStatuses();
            RecalculateDuplicates();
            UpdateStats();

            Log($"[Split] Gộp {group.Count} task về 1 book: {mergedItem.DisplayName}");
        }

        private GalleryItem BuildCollapsedParallelSplitItem(IList<GalleryItem> group)
        {
            GalleryItem seed = group[0];
            GalleryItem mergedItem = CloneGalleryItemForDuplicatePaste(seed) ?? new GalleryItem();
            bool hasErrors = group.Any(item => item.HasAnyErrors() || string.Equals(item.Status, "Error", StringComparison.OrdinalIgnoreCase));
            List<ErrorDetail> mergedErrors = group
                .SelectMany(item => item.GetUniqueErrors())
                .Where(error => error != null)
                .GroupBy(error => $"{(error.ChapterName ?? string.Empty).Trim()}::{error.PageNumber}::{(error.PageName ?? string.Empty).Trim()}", StringComparer.OrdinalIgnoreCase)
                .Select(grouped => grouped.First())
                .Select(error => new ErrorDetail
                {
                    ChapterName = error.ChapterName,
                    PageNumber = error.PageNumber,
                    PageName = error.PageName,
                    ErrorMessage = error.ErrorMessage,
                    ImageUrl = error.ImageUrl,
                    ChapterUrl = error.ChapterUrl,
                    AttemptCount = error.AttemptCount
                })
                .ToList();

            mergedItem.Link = seed.Link;
            mergedItem.Name = seed.Name;
            mergedItem.LinkCount = seed.LinkCount;
            mergedItem.SourceDomain = seed.SourceDomain;
            mergedItem.HasNoChapters = seed.HasNoChapters;
            mergedItem.IsParallelSplitTask = true;
            mergedItem.NhentaiTotalPagesHint = seed.NhentaiTotalPagesHint;
            mergedItem.ChapterSelectionText = string.Empty;
            mergedItem.ConnectionCount = seed.ConnectionCount;
            mergedItem.MultiDownloadCount = seed.MultiDownloadCount;
            mergedItem.IsChecked = hasErrors && group.Any(item => item.IsChecked);
            mergedItem.TotalChapters = group.Sum(item => Math.Max(0, item.TotalChapters));
            mergedItem.CompletedChapters = group.Sum(item => Math.Max(0, item.CompletedChapters));
            mergedItem.Status = hasErrors ? "Error" : "Completed";
            mergedItem.CurrentProcess = GetDoneProcessTextForGroup(group, hasErrors);
            mergedItem.DownloadingChapter = string.Empty;
            mergedItem.DownloadingPageProgress = string.Empty;
            mergedItem.DownloadPath = group.Select(item => item.DownloadPath).FirstOrDefault(path => !string.IsNullOrWhiteSpace(path)) ?? seed.DownloadPath;
            mergedItem.MissingChapterStatusText = seed.MissingChapterStatusText;
            mergedItem.MissingChapterSortText = seed.MissingChapterSortText;
            mergedItem.HasMissingChapterIssue = group.Any(item => item.HasMissingChapterIssue);
            mergedItem.ProgressPercent = 100d;
            mergedItem.DownloadProgressPercent = 100d;
            mergedItem.DownloadSpeedBytesPerSecond = 0L;
            mergedItem.Errors = mergedErrors;
            mergedItem.ErrorCount = mergedItem.GetUniqueErrorCount();
            mergedItem.IsPaused = false;
            mergedItem.IsStopped = false;
            mergedItem.OriginalIndex = seed.OriginalIndex;
            return mergedItem;
        }

        private static bool IsParallelSplitTerminalStatus(string status)
        {
            return string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status, "Error", StringComparison.OrdinalIgnoreCase);
        }

        internal void PauseAllDownloads()
        {
            _isDownloadPaused = true;
        }

        internal void ResumeAllDownloads()
        {
            _isDownloadPaused = false;
        }

        internal string BuildStableTempFolderPath(string rootFolder, string siteFolder, params string[] identityParts)
        {
            string finalTargetFolder;
            
            bool isChapterSite = siteFolder == "truyenqq" ||
                                 siteFolder == "vi-hentai.pro" ||
                                 siteFolder == "damconuong.shop" ||
                                 siteFolder == "nettruyen" ||
                siteFolder == "loppytoonn.com" ||
                siteFolder == "mangadex.org" ||
                siteFolder == "sayhentai.tv" ||
                siteFolder == "truyenggvn.com" ||
                (siteFolder != null && (
                    siteFolder.IndexOf("truyenqq", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    siteFolder.IndexOf("vi-hentai", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    siteFolder.IndexOf("damconuong", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    siteFolder.IndexOf("nettruyen", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    siteFolder.IndexOf("loppytoonn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    siteFolder.IndexOf("hentai2read", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    siteFolder.IndexOf("dilib", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    siteFolder.IndexOf("thuviensach", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    siteFolder.IndexOf("daomeoden", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    siteFolder.IndexOf("mangadex", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    siteFolder.IndexOf("ggvn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    siteFolder.IndexOf("sayhentai", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    siteFolder.IndexOf("haibaba", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    siteFolder.IndexOf("somee", StringComparison.OrdinalIgnoreCase) >= 0
                ));

            if (isChapterSite && identityParts != null && identityParts.Length >= 2)
            {
                string safeManga = identityParts[0];
                string safeChapter = identityParts[1];
                string unmergedPath = Path.Combine(rootFolder, $"{safeManga}-{safeChapter}");
                string mergedPath = Path.Combine(rootFolder, safeManga, safeChapter);
                finalTargetFolder = _isSingleComicFolderType ? mergedPath : unmergedPath;
            }
            else if (siteFolder == HakoSiteFolder && identityParts != null && identityParts.Length > 0)
            {
                finalTargetFolder = Path.Combine(rootFolder, identityParts[0]);
            }
            else
            {
                string prefixSource = identityParts != null && identityParts.Length > 0 ? identityParts[0] : "item";
                string prefix = GetSafePathName(prefixSource);
                if (string.IsNullOrWhiteSpace(prefix))
                    prefix = "item";
                finalTargetFolder = Path.Combine(rootFolder, prefix);
            }

            finalTargetFolder = ConvertToLongPath(finalTargetFolder);

            // Attempt to resume from .tmp if any partial folder exists
            HandleDownloadResume(finalTargetFolder);

            return finalTargetFolder;
        }

        internal string BuildStableChapterTempFolderPath(string rootFolder, string siteFolder, params string[] identityParts)
        {
            return BuildStableTempFolderPath(rootFolder, siteFolder, identityParts);
        }

        private string GetDownloadSiteKey(GalleryItem item)
        {
            try
            {
                string url = item?.Link ?? string.Empty;
                var uri = new Uri(url);
                string host = (uri.Host ?? string.Empty).ToLowerInvariant();

                if (host.Contains("truyenqq"))
                {
                    return "truyenqq";
                }

                if (host.Contains("nettruyen.tech"))
                {
                    return "nettruyen.tech";
                }

                if (host.Contains("nettruyenviet10.com"))
                {
                    return "nettruyenviet10.com";
                }

                if (host.Contains("nettruyen"))
                {
                    return "nettruyen.tech";
                }

                if (host.Contains("daomeoden"))
                {
                    return "daomeoden.net";
                }

                if (host.Contains("dilib.vn") || host.Contains("thuviensach.vn"))
                {
                    return "thuviensach.vn";
                }

                if (host.Contains("loppytoonn.com"))
                {
                    return "loppytoonn.com";
                }

                if (host.Contains("ln.hako.vn") || host.Contains("docln.net") || host.Contains("hako.re"))
                {
                    return "ln.hako.vn";
                }

                if (host.Contains("truyenggvn"))
                {
                    return "truyenggvn";
                }

                if (host.Contains("sayhentai"))
                {
                    return "sayhentai";
                }

                if (host.Contains("vi-hentai"))
                {
                    return "vi-hentai.pro";
                }

                if (host.Contains("damconuong"))
                {
                    return "damconuong.shop";
                }

                if (host.Contains("nhentai.net"))
                {
                    return "nhentai.net";
                }

                if (host.Contains("nhentai"))
                {
                    return "nhentai.net";
                }

                if (host.Contains("hentaiforce"))
                {
                    return "hentaiforce.net";
                }

                if (host.Contains("hentaiera"))
                {
                    return "hentaiera.com";
                }

                if (host.Contains("hentai2read"))
                {
                    return "hentai2read.com";
                }

                if (host.Contains("haibabamanga"))
                {
                    return "haibabamanga.somee.com";
                }
            }
            catch
            {
            }

            return GetSafePathName(item?.SourceDomain ?? "site");
        }

        private string GetEffectiveDownloadRoot(string rootFolder)
        {
            string effective = rootFolder;
            if (!string.IsNullOrWhiteSpace(_activeDownloadRoot))
            {
                effective = _activeDownloadRoot;
            }

            return ConvertToLongPath(effective);
        }

        private static string ConvertToLongPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            try
            {
                string fullPath = Path.GetFullPath(path);
                if (fullPath.StartsWith(@"\\?\")) return fullPath;
                fullPath = fullPath.Replace('/', '\\');
                if (fullPath.StartsWith(@"\\"))
                {
                    return @"\\?\UNC\" + fullPath.Substring(2);
                }
                return @"\\?\" + fullPath;
            }
            catch
            {
                return path;
            }
        }

        private void UpdateDownloadRowMetrics(GalleryItem item, int completedPages, int totalPages, string processText, long bytesDownloaded = 0, long elapsedMilliseconds = 0, bool isParentQueue = false)
        {
            if (item == null)
            {
                return;
            }

            OptimizeSystemPriorityForBackgroundTasks();

            double percent = totalPages > 0
                ? Math.Min(100d, Math.Max(0d, (double)completedPages * 100d / totalPages))
                : 0d;

            if (bytesDownloaded > 0)
            {
                System.Threading.Interlocked.Add(ref item._downloadedBytesAccumulator, bytesDownloaded);
            }

            // Throttle UI thread updates to prevent freezing (UI thread starvation)
            bool isFinalUpdate = (completedPages == totalPages) || (percent >= 100d) || (completedPages == 0);
            DateTime now = DateTime.UtcNow;
            if (!isFinalUpdate)
            {
                if (_lastMetricUpdateTimes.TryGetValue(item, out DateTime lastUpdate))
                {
                    if ((now - lastUpdate).TotalMilliseconds < 800)
                    {
                        return; // Skip this UI update
                    }
                }
            }
            _lastMetricUpdateTimes[item] = now;

            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                if (!isParentQueue)
                {
                    item.CompletedChapters = completedPages;
                    item.DownloadProgressPercent = percent;
                }
                else
                {
                    double currentChapterProgress = totalPages > 0 ? (double)completedPages / totalPages : 0d;
                    double overallChapters = item.CompletedChapters + currentChapterProgress;
                    double bookPercent = item.TotalChapters > 0
                        ? Math.Min(100d, Math.Max(0d, overallChapters * 100d / item.TotalChapters))
                        : 0d;
                    item.DownloadProgressPercent = bookPercent;
                }
                item.CurrentProcess = processText;
            }));
        }

        private string GetSiteDownloadRoot(string rootFolder, string siteKey)
        {
            string effectiveRoot = GetEffectiveDownloadRoot(rootFolder);
            return GetConfiguredDownloadRoot(effectiveRoot, siteKey);
        }

        private string GetConfiguredDownloadRoot(string rootFolder, GalleryItem item)
        {
            string siteKey = GetDownloadSiteKey(item);
            return GetConfiguredDownloadRoot(rootFolder, siteKey);
        }

        private string GetConfiguredDownloadRoot(string rootFolder, string siteKey)
        {
            rootFolder = GetEffectiveDownloadRoot(rootFolder);
            if (string.IsNullOrWhiteSpace(rootFolder))
            {
                return rootFolder;
            }

            if (string.IsNullOrWhiteSpace(siteKey))
            {
                return rootFolder;
            }

            string lowerKey = siteKey.ToLowerInvariant();
            if (lowerKey.Contains("nettruyen.tech"))
            {
                siteKey = "nettruyen.tech";
            }
            else if (lowerKey.Contains("nettruyenviet10.com"))
            {
                siteKey = "nettruyenviet10.com";
            }
            else if (lowerKey.Contains("nettruyen"))
            {
                siteKey = "nettruyen";
            }
            else if (lowerKey.Contains("truyenqq"))
            {
                siteKey = "truyenqq";
            }
            else if (lowerKey.Contains("daomeoden"))
            {
                siteKey = "daomeoden.net";
            }
            else if (lowerKey.Contains("dilib.vn") || lowerKey.Contains("thuviensach.vn"))
            {
                siteKey = "thuviensach.vn";
            }
            else if (lowerKey.Contains("loppytoonn.com"))
            {
                siteKey = "loppytoonn.com";
            }
            else if (lowerKey.Contains("hako.vn") || lowerKey.Contains("docln.net") || lowerKey.Contains("hako.re"))
            {
                siteKey = "ln.hako.vn";
            }
            else if (lowerKey.Contains("truyenggvn"))
            {
                siteKey = "truyenggvn";
            }
            else if (lowerKey.Contains("sayhentai"))
            {
                siteKey = "sayhentai";
            }
            else if (lowerKey.Contains("vi-hentai"))
            {
                siteKey = "vi-hentai.pro";
            }
            else if (lowerKey.Contains("nhentai.net"))
            {
                siteKey = "nhentai.net";
            }
            else if (lowerKey.Contains("nhentai"))
            {
                siteKey = "nhentai.net";
            }
            else if (lowerKey.Contains("hentaiforce"))
            {
                siteKey = "hentaiforce.net";
            }
            else if (lowerKey.Contains("hentaiera"))
            {
                siteKey = "hentaiera.com";
            }
            else if (lowerKey.Contains("hentai2read"))
            {
                siteKey = "hentai2read.com";
            }
            else if (lowerKey.Contains("haibabamanga"))
            {
                siteKey = "haibabamanga.somee.com";
            }

            string subfolder = GetCreateSubfolderPath(siteKey);
            return string.IsNullOrWhiteSpace(subfolder)
                ? Path.Combine(rootFolder, siteKey)
                : Path.Combine(rootFolder, siteKey, subfolder);
        }

        private string GetCreateSubfolderPath(string domainKey)
        {
            if (string.IsNullOrWhiteSpace(domainKey))
            {
                return string.Empty;
            }

            if (_createSubfolderByDomain.TryGetValue(domainKey, out string subfolder) && !string.IsNullOrWhiteSpace(subfolder))
            {
                return GetSafePathName(subfolder.Trim());
            }

            return string.Empty;
        }

        private string NormalizeChapterLabel(string chapterTitle)
        {
            if (string.IsNullOrWhiteSpace(chapterTitle))
            {
                return chapterTitle;
            }

            Match match = Regex.Match(
                chapterTitle.Trim(),
                @"(?i)(?:chap(?:ter)?|chương|chuong)?\s*(?<num>\d+(?:\.\d+)?)");

            if (match.Success)
            {
                return "chap " + ZeroPadChapterNumberToken(match.Groups["num"].Value);
            }

            return CompactSingleLine(chapterTitle.Trim());
        }

        private string GetSafeChapterPathName(string chapterTitle, int maxLength = 100)
        {
            return GetSafePathName(NormalizeChapterLabel(chapterTitle), maxLength);
        }

        private string NormalizeVolumeLabel(string volumeTitle, int? volumeOrder = null)
        {
            string cleaned = CompactSingleLine(volumeTitle);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return volumeOrder.HasValue && volumeOrder.Value > 0
                    ? "volume " + volumeOrder.Value.ToString("00", CultureInfo.InvariantCulture)
                    : string.Empty;
            }

            Match exactChapterLike = Regex.Match(
                cleaned,
                @"(?i)^(?:chap(?:ter)?|chương|chuong)\s*(?<num>\d+(?:\.\d+)?)$");
            if (exactChapterLike.Success)
            {
                return "volume " + ZeroPadChapterNumberToken(exactChapterLike.Groups["num"].Value);
            }

            return cleaned;
        }

        private string GetSafeVolumePathName(string volumeTitle, int? volumeOrder = null, int maxLength = 100)
        {
            return GetSafePathName(NormalizeVolumeLabel(volumeTitle, volumeOrder), maxLength);
        }

        private string GetSafeChapterPathName(string bookTitle, string chapterTitle, int maxLength = 120)
        {
            string combined = string.IsNullOrWhiteSpace(bookTitle)
                ? NormalizeChapterLabel(chapterTitle)
                : CompactSingleLine(bookTitle) + " - " + NormalizeChapterLabel(chapterTitle);
            return GetSafePathName(combined, maxLength);
        }



        private static string ZeroPadChapterNumberToken(string numberToken)
        {
            if (string.IsNullOrWhiteSpace(numberToken))
            {
                return numberToken;
            }

            string[] parts = numberToken.Split('.');
            if (parts.Length == 0)
            {
                return numberToken;
            }

            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int wholeNumber))
            {
                return numberToken;
            }

            if (wholeNumber >= 1000 || parts[0].Length >= 4)
            {
                return numberToken;
            }

            parts[0] = wholeNumber.ToString("D4", CultureInfo.InvariantCulture);
            return string.Join(".", parts);
        }

        private string GetTempProgressLogPath(string tempFolder, int completedPages, int totalPages)
        {
            int safeCompleted = Math.Max(0, completedPages);
            int safeTotal = Math.Max(0, totalPages);
            return Path.Combine(tempFolder, $"log-{safeCompleted}-{safeTotal}.md");
        }

        private void CleanupTempProgressLogs(string tempFolder)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(tempFolder) || !Directory.Exists(tempFolder))
                {
                    return;
                }

                foreach (string path in Directory.GetFiles(tempFolder, "log*.md"))
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private string GetCanonicalBookFolderName(GalleryItem item, string fallbackTitle, string defaultTitle = "item", int maxLength = 100)
        {
            string preferredTitle = CompactSingleLine(item?.Name);
            if (string.IsNullOrWhiteSpace(preferredTitle))
            {
                preferredTitle = CompactSingleLine(fallbackTitle);
            }

            string safeName = GetSafePathName(preferredTitle, maxLength);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = GetSafePathName(defaultTitle, maxLength);
            }

            return string.IsNullOrWhiteSpace(safeName) ? "item" : safeName;
        }

        private async Task NormalizeBookFolderAliasAsync(string siteRootFolder, string preferredSafeBook, string aliasSafeBook, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(siteRootFolder) ||
                string.IsNullOrWhiteSpace(preferredSafeBook) ||
                string.IsNullOrWhiteSpace(aliasSafeBook) ||
                string.Equals(preferredSafeBook, aliasSafeBook, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string aliasBookFolder = Path.Combine(siteRootFolder, aliasSafeBook);
            string preferredBookFolder = Path.Combine(siteRootFolder, preferredSafeBook);

            if (!Directory.Exists(aliasBookFolder))
            {
                return;
            }

            await _folderStructureSemaphore.WaitAsync(token);
            try
            {
                Directory.CreateDirectory(preferredBookFolder);
                MergeDirectoryContents(aliasBookFolder, preferredBookFolder);
                Log($"[Auto Merge] Đã chuẩn hóa folder book '{aliasSafeBook}' -> '{preferredSafeBook}'");
            }
            finally
            {
                _folderStructureSemaphore.Release();
            }
        }

        private async Task NormalizeChapterFolderAliasAsync(string siteRootFolder, string preferredSafeBook, string aliasSafeBook, string safeChapter, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(siteRootFolder) ||
                string.IsNullOrWhiteSpace(preferredSafeBook) ||
                string.IsNullOrWhiteSpace(aliasSafeBook) ||
                string.IsNullOrWhiteSpace(safeChapter) ||
                string.Equals(preferredSafeBook, aliasSafeBook, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string preferredMergedPath = Path.Combine(siteRootFolder, preferredSafeBook, safeChapter);
            string aliasMergedPath = Path.Combine(siteRootFolder, aliasSafeBook, safeChapter);
            string aliasUnmergedPath = Path.Combine(siteRootFolder, $"{aliasSafeBook}-{safeChapter}");

            await AutoMergeChapterFolderAsync(aliasMergedPath, preferredMergedPath, token);
            await AutoMergeChapterFolderAsync(aliasUnmergedPath, preferredMergedPath, token);
            await NormalizeBookFolderAliasAsync(siteRootFolder, preferredSafeBook, aliasSafeBook, token);
        }

        private void WriteTempProgressLog(string tempFolder, GalleryItem item, string status, int completedPages, int totalPages, string currentProcess, string note = null, string imageUrl = null)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(tempFolder) && Directory.Exists(tempFolder))
                {
                    CleanupTempProgressLogs(tempFolder);
                }
            }
            catch
            {
            }
        }

        private string EscapeMarkdownTableValue(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("|", "\\|")
                .Replace("\r", " ")
                .Replace("\n", " ");
        }

        private void MoveTempFolderToTarget(string tempFolder, string targetFolder, string errorLabel)
        {
            try
            {
                if (string.Equals(tempFolder, targetFolder, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                if (!Directory.Exists(tempFolder))
                {
                    return;
                }

                string targetParent = Path.GetDirectoryName(targetFolder);
                if (!string.IsNullOrWhiteSpace(targetParent))
                {
                    Directory.CreateDirectory(targetParent);
                }

                if (Directory.Exists(targetFolder))
                {
                    MergeDirectoryContents(tempFolder, targetFolder);
                    try
                    {
                        Directory.Delete(tempFolder, true);
                    }
                    catch {}
                }
                else
                {
                    Directory.Move(tempFolder, targetFolder);
                }

                // Auto Zip CBZ Logic
                bool autoZip = false;
                Dispatcher.Invoke(() => {
                    autoZip = chkAutoZipCbz != null && chkAutoZipCbz.IsChecked == true;
                });
                if (autoZip && Directory.Exists(targetFolder))
                {
                    try
                    {
                        string cbzPath = targetFolder.TrimEnd('\\', '/') + ".cbz";
                        if (File.Exists(cbzPath))
                        {
                            try { File.Delete(cbzPath); } catch {}
                        }
                        System.IO.Compression.ZipFile.CreateFromDirectory(targetFolder, cbzPath);
                        Log($"[AutoZip] Đã nén thành công file CBZ: {cbzPath}");
                        try
                        {
                            Directory.Delete(targetFolder, true);
                        }
                        catch (Exception delEx)
                        {
                            Log($"[AutoZip Warning] Không thể xóa thư mục gốc sau khi nén: {delEx.Message}");
                        }
                    }
                    catch (Exception zipEx)
                    {
                        Log($"[AutoZip Error] Lỗi khi tạo file CBZ: {zipEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[Lỗi] Không thể di chuyển thư mục tạm {errorLabel}: {ex.Message}");
            }
            finally
            {
                UnregisterTempFolder(tempFolder);
            }
        }

        internal string GetChapterTempFolder(string finalTargetFolder)
        {
            if (string.IsNullOrWhiteSpace(finalTargetFolder)) return null;
            
            string normalized = Path.GetFullPath(finalTargetFolder).Replace('/', '\\');
            string[] parts = normalized.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            
            string nameSegment = "";
            if (parts.Length >= 3)
            {
                nameSegment = $"{parts[parts.Length - 3]}_{parts[parts.Length - 2]}_{parts[parts.Length - 1]}";
            }
            else if (parts.Length >= 2)
            {
                nameSegment = $"{parts[parts.Length - 2]}_{parts[parts.Length - 1]}";
            }
            else
            {
                nameSegment = parts[parts.Length - 1];
            }
            
            nameSegment = GetSafePathName(nameSegment);
            return Path.Combine(PortablePaths.PortableTempRoot, $"{nameSegment}-tmp");
        }

        internal void HandleDownloadResume(string finalTargetFolder)
        {
            try
            {
                string tempFolder = GetChapterTempFolder(finalTargetFolder);
                if (string.IsNullOrWhiteSpace(tempFolder) || !Directory.Exists(tempFolder))
                {
                    return;
                }

                Log($"[Resume] Khôi phục thư mục tạm từ .tmp về: {finalTargetFolder}");
                if (!Directory.Exists(finalTargetFolder))
                {
                    string parent = Path.GetDirectoryName(finalTargetFolder);
                    if (!string.IsNullOrWhiteSpace(parent))
                    {
                        Directory.CreateDirectory(parent);
                    }
                    Directory.Move(tempFolder, finalTargetFolder);
                }
                else
                {
                    MergeDirectoryContents(tempFolder, finalTargetFolder);
                    try
                    {
                        Directory.Delete(tempFolder, true);
                    }
                    catch {}
                }
            }
            catch (Exception ex)
            {
                Log($"[Warning] Không thể khôi phục thư mục tạm: {ex.Message}");
            }
        }

        internal void HandleDownloadStopOrInterruption(string finalTargetFolder)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(finalTargetFolder) || !Directory.Exists(finalTargetFolder))
                {
                    return;
                }

                string tempFolder = GetChapterTempFolder(finalTargetFolder);
                if (string.IsNullOrWhiteSpace(tempFolder))
                {
                    return;
                }

                Log($"[Stop/Pause] Di chuyển thư mục dở dang về .tmp: {tempFolder}");
                string parent = Path.GetDirectoryName(tempFolder);
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                if (Directory.Exists(tempFolder))
                {
                    MergeDirectoryContents(finalTargetFolder, tempFolder);
                    try
                    {
                        Directory.Delete(finalTargetFolder, true);
                    }
                    catch {}
                }
                else
                {
                    Directory.Move(finalTargetFolder, tempFolder);
                }
            }
            catch (Exception ex)
            {
                Log($"[Warning] Không thể di chuyển thư mục dở dang về .tmp: {ex.Message}");
            }
        }

        private string GetDownloadProcessFilePath(string rootFolder, string siteFolder, GalleryItem item)
        {
            string safeSite = GetSafePathName(siteFolder ?? "site");
            string bookKey = GetDownloadProcessBookKey(item);
            string safeBookKey = GetSafePathName(bookKey.Replace("|", "-"));
            if (safeBookKey.Length > 120)
            {
                safeBookKey = safeBookKey.Substring(0, 120).Trim();
            }

            string effectiveRoot = GetEffectiveDownloadRoot(rootFolder);
            return Path.Combine(effectiveRoot, ".tmp", ".process", safeSite, $"{safeBookKey}.md");
        }

        private string GetConfiguredScopedDownloadProcessFilePath(string rootFolder, string siteFolder, GalleryItem item)
        {
            string effectiveRoot = GetConfiguredDownloadRoot(GetEffectiveDownloadRoot(rootFolder), siteFolder);
            string safeSite = GetSafePathName(siteFolder ?? "site");
            string bookKey = GetDownloadProcessBookKey(item);
            string safeBookKey = GetSafePathName(bookKey.Replace("|", "-"));
            if (safeBookKey.Length > 120)
            {
                safeBookKey = safeBookKey.Substring(0, 120).Trim();
            }

            return Path.Combine(effectiveRoot, ".tmp", ".process", safeSite, $"{safeBookKey}.md");
        }

        private string GetLegacyDownloadProcessFilePath(string rootFolder, string siteFolder, GalleryItem item)
        {
            rootFolder = GetEffectiveDownloadRoot(rootFolder);
            string safeSite = GetSafePathName(siteFolder ?? "site");
            string bookKey = GetDownloadProcessBookKey(item);
            string safeBookKey = GetSafePathName(bookKey.Replace("|", "-"));
            if (safeBookKey.Length > 120)
            {
                safeBookKey = safeBookKey.Substring(0, 120).Trim();
            }

            return Path.Combine(rootFolder, safeSite, ".process", $"{safeBookKey}.md");
        }

        private string GetExistingDownloadProcessFilePath(string rootFolder, string siteFolder, GalleryItem item)
        {
            string processPath = GetDownloadProcessFilePath(rootFolder, siteFolder, item);
            string configuredScopedPath = GetConfiguredScopedDownloadProcessFilePath(rootFolder, siteFolder, item);
            string legacyPath = GetLegacyDownloadProcessFilePath(rootFolder, siteFolder, item);

            foreach (string candidate in new[] { processPath, configuredScopedPath, legacyPath })
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return processPath;
        }

        private static string NormalizeProcessLink(string link)
        {
            return (link ?? string.Empty).Trim().TrimEnd('/');
        }

        private string GetDownloadProcessBookKey(GalleryItem item)
        {
            string baseKey = GetBookIdentifier(item?.Link) ?? item?.Name ?? "item";
            string chapterRange = (item?.ChapterSelectionText ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(chapterRange)
                ? baseKey
                : $"{baseKey}|range:{chapterRange}";
        }

        private string GetProcessSiteFolder(GalleryItem item)
        {
            try
            {
                string url = item?.Link ?? string.Empty;
                var uri = new Uri(url);
                string host = (uri.Host ?? string.Empty).ToLowerInvariant();

                if (host.Contains("truyenqq"))
                {
                    return "truyenqq";
                }

                if (host.Contains("nettruyen.tech"))
                {
                    return "nettruyen.tech";
                }

                if (host.Contains("nettruyenviet10.com"))
                {
                    return "nettruyenviet10.com";
                }

                if (host.Contains("nettruyen"))
                {
                    return "nettruyen";
                }

                if (host.Contains("vi-hentai"))
                {
                    return "vi-hentai.pro";
                }

                if (host.Contains("damconuong"))
                {
                    return "damconuong.shop";
                }

                if (host.Contains("daomeoden"))
                {
                    return "daomeoden.net";
                }

                if (host.Contains("ln.hako.vn") || host.Contains("docln.net") || host.Contains("hako.re"))
                {
                    return "ln.hako.vn";
                }

                if (host.Contains("hentaiera"))
                {
                    return "hentaiera";
                }

                if (host.Contains("hentai2read"))
                {
                    return "hentai2read";
                }

                if (host.Contains("sayhentai.cx"))
                {
                    return "sayhentai";
                }
            }
            catch
            {
            }

            return GetSafePathName(item?.SourceDomain ?? "site");
        }

        private static readonly object _dbLock = new object();

        private string GetProcessDbPath(string rootFolder)
        {
            return Path.Combine(PortablePaths.PortableTempRoot, ".process", "process.db");
        }

        private System.Data.SQLite.SQLiteConnection OpenProcessConnection(string dbPath)
        {
            string dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var conn = new System.Data.SQLite.SQLiteConnection($"Data Source={dbPath};Version=3;Journal Mode=WAL;");
            conn.Open();

            string sql = @"
                CREATE TABLE IF NOT EXISTS download_process (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    site TEXT,
                    book_key TEXT,
                    chapter_link TEXT,
                    chapter_label TEXT,
                    status TEXT,
                    order_index INTEGER,
                    updated_at TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_download_process_site_book ON download_process(site, book_key);
                CREATE INDEX IF NOT EXISTS idx_download_process_lookup ON download_process(site, book_key, chapter_link);
            ";
            using (var cmd = new System.Data.SQLite.SQLiteCommand(sql, conn))
            {
                cmd.ExecuteNonQuery();
            }

            return conn;
        }

        private void MigrateMdProcessToSqliteIfNeeded(string dbPath, string rootFolder, string siteFolder, GalleryItem item, string bookKey)
        {
            string mdPath = GetExistingDownloadProcessFilePath(rootFolder, siteFolder, item);
            if (!File.Exists(mdPath))
            {
                return;
            }

            lock (_dbLock)
            {
                try
                {
                    using (var conn = OpenProcessConnection(dbPath))
                    {
                        // Check if already in SQLite
                        string checkSql = "SELECT COUNT(*) FROM download_process WHERE site = @site AND book_key = @book_key";
                        using (var cmd = new System.Data.SQLite.SQLiteCommand(checkSql, conn))
                        {
                            cmd.Parameters.AddWithValue("@site", siteFolder);
                            cmd.Parameters.AddWithValue("@book_key", bookKey);
                            long count = (long)cmd.ExecuteScalar();
                            if (count > 0)
                            {
                                try { File.Delete(mdPath); } catch {}
                                return;
                            }
                        }

                        // Read MD file lines and migrate
                        var doneLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        var allLinks = new List<string>();
                        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                        foreach (string line in File.ReadAllLines(mdPath, Encoding.UTF8))
                        {
                            if (!line.StartsWith("|", StringComparison.Ordinal) ||
                                line.StartsWith("| No.", StringComparison.OrdinalIgnoreCase) ||
                                line.StartsWith("| :---", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            string[] cells = line.Split('|');
                            if (cells.Length < 5)
                            {
                                continue;
                            }

                            string status = cells[2].Trim();
                            string label = cells[3].Trim();
                            string link = cells[4].Trim();

                            if (!string.IsNullOrWhiteSpace(link) && link.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            {
                                allLinks.Add(link);
                                labels[link] = label;
                                if (string.Equals(status, "Done", StringComparison.OrdinalIgnoreCase))
                                {
                                    doneLinks.Add(link);
                                }
                            }
                        }

                        if (allLinks.Count > 0)
                        {
                            using (var transaction = conn.BeginTransaction())
                            {
                                string insertSql = @"
                                    INSERT INTO download_process (site, book_key, chapter_link, chapter_label, status, order_index, updated_at)
                                    VALUES (@site, @book_key, @chapter_link, @chapter_label, @status, @order_index, @updated_at)
                                ";

                                using (var cmd = new System.Data.SQLite.SQLiteCommand(insertSql, conn, transaction))
                                {
                                    cmd.Parameters.Add("@site", System.Data.DbType.String);
                                    cmd.Parameters.Add("@book_key", System.Data.DbType.String);
                                    cmd.Parameters.Add("@chapter_link", System.Data.DbType.String);
                                    cmd.Parameters.Add("@chapter_label", System.Data.DbType.String);
                                    cmd.Parameters.Add("@status", System.Data.DbType.String);
                                    cmd.Parameters.Add("@order_index", System.Data.DbType.Int32);
                                    cmd.Parameters.Add("@updated_at", System.Data.DbType.String);

                                    for (int i = 0; i < allLinks.Count; i++)
                                    {
                                        string link = allLinks[i];
                                        string status = doneLinks.Contains(link) ? "Done" : "Pending";
                                        string label = labels.ContainsKey(link) ? labels[link] : GetChapterProcessLabel(link);

                                        cmd.Parameters["@site"].Value = siteFolder;
                                        cmd.Parameters["@book_key"].Value = bookKey;
                                        cmd.Parameters["@chapter_link"].Value = link;
                                        cmd.Parameters["@chapter_label"].Value = label;
                                        cmd.Parameters["@status"].Value = status;
                                        cmd.Parameters["@order_index"].Value = i + 1;
                                        cmd.Parameters["@updated_at"].Value = DateTime.UtcNow.ToString("o");

                                        cmd.ExecuteNonQuery();
                                    }
                                }
                                transaction.Commit();
                            }
                        }

                        try { File.Delete(mdPath); } catch {}
                    }
                }
                catch (Exception ex)
                {
                    Log($"[sqlite] Migration error for '{item?.Name}': {ex.Message}");
                }
            }
        }

        private List<string> LoadPendingChapterLinksFromProcess(string rootFolder, string siteFolder, GalleryItem item)
        {
            string dbPath = GetProcessDbPath(rootFolder);
            string bookKey = GetDownloadProcessBookKey(item);
            string safeBookKey = GetSafePathName(bookKey.Replace("|", "-"));
            if (safeBookKey.Length > 120)
            {
                safeBookKey = safeBookKey.Substring(0, 120).Trim();
            }

            MigrateMdProcessToSqliteIfNeeded(dbPath, rootFolder, siteFolder, item, safeBookKey);

            var links = new List<string>();
            lock (_dbLock)
            {
                try
                {
                    using (var conn = OpenProcessConnection(dbPath))
                    {
                        string sql = "SELECT chapter_link FROM download_process WHERE site = @site AND book_key = @book_key AND status != 'Done' ORDER BY order_index ASC";
                        using (var cmd = new System.Data.SQLite.SQLiteCommand(sql, conn))
                        {
                            cmd.Parameters.AddWithValue("@site", siteFolder);
                            cmd.Parameters.AddWithValue("@book_key", safeBookKey);
                            using (var reader = cmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    links.Add(reader.GetString(0));
                                }
                            }
                        }

                        string countSql = "SELECT COUNT(*) FROM download_process WHERE site = @site AND book_key = @book_key";
                        using (var cmd = new System.Data.SQLite.SQLiteCommand(countSql, conn))
                        {
                            cmd.Parameters.AddWithValue("@site", siteFolder);
                            cmd.Parameters.AddWithValue("@book_key", safeBookKey);
                            long count = (long)cmd.ExecuteScalar();
                            if (count == 0)
                            {
                                return null;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"[sqlite] LoadPending error: {ex.Message}");
                    return null;
                }
            }

            return links;
        }

        private string GetChapterProcessLabel(string chapterLink)
        {
            string slug = GetChapterSlugFromLink(chapterLink);
            if (!string.IsNullOrWhiteSpace(slug))
            {
                string normalized = NormalizeChapterProcessSlug(slug);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    return normalized;
                }
            }

            return "chapter";
        }

        private string GetChapterSlugFromLink(string chapterLink)
        {
            if (string.IsNullOrWhiteSpace(chapterLink))
            {
                return string.Empty;
            }

            try
            {
                if (Uri.TryCreate(chapterLink, UriKind.Absolute, out Uri uri))
                {
                    string[] segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    if (segments.Length > 0)
                    {
                        return Path.GetFileNameWithoutExtension(segments[segments.Length - 1]);
                    }
                }
            }
            catch
            {
            }

            try
            {
                string normalized = chapterLink.Trim().TrimEnd('/');
                int slash = normalized.LastIndexOf('/');
                string tail = slash >= 0 ? normalized.Substring(slash + 1) : normalized;
                return Path.GetFileNameWithoutExtension(tail);
            }
            catch
            {
                return string.Empty;
            }
        }

        private string NormalizeChapterProcessSlug(string slug)
        {
            string cleaned = CompactSingleLine((slug ?? string.Empty).Replace("-", " ").Replace("_", " "));
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return string.Empty;
            }

            if (TryGetStrictChapterNumber(cleaned, out double chapterNumber))
            {
                string chapterText = chapterNumber.ToString("0.####", CultureInfo.InvariantCulture);
                return "chap " + ZeroPadChapterNumberToken(chapterText);
            }

            return cleaned;
        }

        private bool TryGetStrictChapterNumber(string rawValue, out double chapterNumber)
        {
            chapterNumber = 0d;
            string cleaned = CompactSingleLine((rawValue ?? string.Empty).Replace("-", " ").Replace("_", " "));
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return false;
            }

            Match match = Regex.Match(
                cleaned,
                @"^(?i)(?:chap(?:ter)?|chương|chuong)\s*(?<num>\d+(?:\.\d+)?)$|^(?<plain>\d+(?:\.\d+)?)$");
            string token = match.Success
                ? (match.Groups["num"].Success ? match.Groups["num"].Value : match.Groups["plain"].Value)
                : string.Empty;

            return !string.IsNullOrWhiteSpace(token) &&
                   double.TryParse(token, NumberStyles.Any, CultureInfo.InvariantCulture, out chapterNumber);
        }

        private void InitializeChapterProcess(string rootFolder, string siteFolder, GalleryItem item, IList<string> chapterLinks, bool preserveExistingDone = true, IDictionary<string, string> chapterLabelsByLink = null)
        {
            string dbPath = GetProcessDbPath(rootFolder);
            string bookKey = GetDownloadProcessBookKey(item);
            string safeBookKey = GetSafePathName(bookKey.Replace("|", "-"));
            if (safeBookKey.Length > 120)
            {
                safeBookKey = safeBookKey.Substring(0, 120).Trim();
            }

            MigrateMdProcessToSqliteIfNeeded(dbPath, rootFolder, siteFolder, item, safeBookKey);

            var doneLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            lock (_dbLock)
            {
                try
                {
                    using (var conn = OpenProcessConnection(dbPath))
                    {
                        if (preserveExistingDone)
                        {
                            string selectDone = "SELECT chapter_link FROM download_process WHERE site = @site AND book_key = @book_key AND status = 'Done'";
                            using (var cmd = new System.Data.SQLite.SQLiteCommand(selectDone, conn))
                            {
                                cmd.Parameters.AddWithValue("@site", siteFolder);
                                cmd.Parameters.AddWithValue("@book_key", safeBookKey);
                                using (var reader = cmd.ExecuteReader())
                                {
                                    while (reader.Read())
                                    {
                                        doneLinks.Add(reader.GetString(0));
                                    }
                                }
                            }
                        }

                        using (var transaction = conn.BeginTransaction())
                        {
                            string deleteSql = "DELETE FROM download_process WHERE site = @site AND book_key = @book_key";
                            using (var cmd = new System.Data.SQLite.SQLiteCommand(deleteSql, conn, transaction))
                            {
                                cmd.Parameters.AddWithValue("@site", siteFolder);
                                cmd.Parameters.AddWithValue("@book_key", safeBookKey);
                                cmd.ExecuteNonQuery();
                            }

                            string insertSql = @"
                                INSERT INTO download_process (site, book_key, chapter_link, chapter_label, status, order_index, updated_at)
                                VALUES (@site, @book_key, @chapter_link, @chapter_label, @status, @order_index, @updated_at)
                            ";
                            using (var cmd = new System.Data.SQLite.SQLiteCommand(insertSql, conn, transaction))
                            {
                                cmd.Parameters.Add("@site", System.Data.DbType.String);
                                cmd.Parameters.Add("@book_key", System.Data.DbType.String);
                                cmd.Parameters.Add("@chapter_link", System.Data.DbType.String);
                                cmd.Parameters.Add("@chapter_label", System.Data.DbType.String);
                                cmd.Parameters.Add("@status", System.Data.DbType.String);
                                cmd.Parameters.Add("@order_index", System.Data.DbType.Int32);
                                cmd.Parameters.Add("@updated_at", System.Data.DbType.String);

                                for (int i = 0; i < chapterLinks.Count; i++)
                                {
                                    string link = chapterLinks[i];
                                    string status = doneLinks.Contains(link) ? "Done" : "Pending";
                                    string label = chapterLabelsByLink != null && chapterLabelsByLink.TryGetValue(link, out string mappedLabel) && !string.IsNullOrWhiteSpace(mappedLabel)
                                        ? mappedLabel.Trim()
                                        : GetChapterProcessLabel(link);

                                    cmd.Parameters["@site"].Value = siteFolder;
                                    cmd.Parameters["@book_key"].Value = safeBookKey;
                                    cmd.Parameters["@chapter_link"].Value = link;
                                    cmd.Parameters["@chapter_label"].Value = label;
                                    cmd.Parameters["@status"].Value = status;
                                    cmd.Parameters["@order_index"].Value = i + 1;
                                    cmd.Parameters["@updated_at"].Value = DateTime.UtcNow.ToString("o");

                                    cmd.ExecuteNonQuery();
                                }
                            }
                            transaction.Commit();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"[sqlite] Initialize error: {ex.Message}");
                }
            }
        }

        private void MarkChapterProcessDone(string rootFolder, string siteFolder, GalleryItem item, string chapterLink)
        {
            if (string.IsNullOrWhiteSpace(chapterLink))
            {
                return;
            }

            string dbPath = GetProcessDbPath(rootFolder);
            string bookKey = GetDownloadProcessBookKey(item);
            string safeBookKey = GetSafePathName(bookKey.Replace("|", "-"));
            if (safeBookKey.Length > 120)
            {
                safeBookKey = safeBookKey.Substring(0, 120).Trim();
            }

            MigrateMdProcessToSqliteIfNeeded(dbPath, rootFolder, siteFolder, item, safeBookKey);

            lock (_dbLock)
            {
                try
                {
                    using (var conn = OpenProcessConnection(dbPath))
                    {
                        string sql = @"
                            UPDATE download_process 
                            SET status = 'Done', updated_at = @updated_at 
                            WHERE site = @site AND book_key = @book_key AND (chapter_link = @chapter_link OR chapter_link = @chapter_link_alt)
                        ";
                        using (var cmd = new System.Data.SQLite.SQLiteCommand(sql, conn))
                        {
                            cmd.Parameters.AddWithValue("@updated_at", DateTime.UtcNow.ToString("o"));
                            cmd.Parameters.AddWithValue("@site", siteFolder);
                            cmd.Parameters.AddWithValue("@book_key", safeBookKey);
                            cmd.Parameters.AddWithValue("@chapter_link", chapterLink);
                            cmd.Parameters.AddWithValue("@chapter_link_alt", NormalizeProcessLink(chapterLink));
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"[sqlite] MarkDone error: {ex.Message}");
                }
            }
        }

        private string GetDownloadBookFolderPath(string rootFolder, string siteFolder, GalleryItem item)
        {
            if (item == null)
            {
                return null;
            }

            string resolvedRoot = GetSiteDownloadRoot(rootFolder, siteFolder);
            if (string.IsNullOrWhiteSpace(resolvedRoot))
            {
                return null;
            }

            string safeTitle = GetSafePathName(item.Name);
            if (string.IsNullOrWhiteSpace(safeTitle))
            {
                return null;
            }

            return Path.Combine(resolvedRoot, safeTitle);
        }

        private bool ShouldPreserveExistingProcessState(string rootFolder, string siteFolder, GalleryItem item)
        {
            string bookFolder = GetDownloadBookFolderPath(rootFolder, siteFolder, item);
            if (string.IsNullOrWhiteSpace(bookFolder) || !Directory.Exists(bookFolder))
            {
                return false;
            }

            try
            {
                return Directory.EnumerateFileSystemEntries(bookFolder).Any();
            }
            catch
            {
                return false;
            }
        }

        private async Task CopyToAsyncWithTimeout(Stream source, Stream destination, int bufferSize, CancellationToken token, int timeoutMs = 25000)
        {
            byte[] buffer = new byte[bufferSize];
            int bytesRead;
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                while (true)
                {
                    linkedCts.CancelAfter(timeoutMs);
                    try
                    {
                        bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, linkedCts.Token);
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        throw new TimeoutException("Đọc dữ liệu mạng bị quá thời gian chờ (Read stream timed out).");
                    }

                    if (bytesRead == 0) break;

                    linkedCts.CancelAfter(timeoutMs);
                    try
                    {
                        await destination.WriteAsync(buffer, 0, bytesRead, linkedCts.Token);
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        throw new TimeoutException("Ghi file bị quá thời gian chờ (Write file timed out).");
                    }
                }
            }
        }

        private string QueryChapterLabelFromProcessDb(string rootFolder, string siteFolder, GalleryItem item, string chapterLink)
        {
            string dbPath = GetProcessDbPath(rootFolder);
            if (!File.Exists(dbPath)) return null;

            string bookKey = GetDownloadProcessBookKey(item);
            string safeBookKey = GetSafePathName(bookKey.Replace("|", "-"));
            if (safeBookKey.Length > 120)
            {
                safeBookKey = safeBookKey.Substring(0, 120).Trim();
            }

            lock (_dbLock)
            {
                try
                {
                    using (var conn = OpenProcessConnection(dbPath))
                    {
                        string sql = "SELECT chapter_label FROM download_process WHERE site = @site AND book_key = @book_key AND (chapter_link = @link OR chapter_link = @link_alt) LIMIT 1";
                        using (var cmd = new System.Data.SQLite.SQLiteCommand(sql, conn))
                        {
                            cmd.Parameters.AddWithValue("@site", siteFolder);
                            cmd.Parameters.AddWithValue("@book_key", safeBookKey);
                            cmd.Parameters.AddWithValue("@link", chapterLink);
                            cmd.Parameters.AddWithValue("@link_alt", NormalizeProcessLink(chapterLink));
                            var result = cmd.ExecuteScalar();
                            if (result != null && result != DBNull.Value)
                            {
                                return result.ToString();
                            }
                        }
                    }
                }
                catch {}
            }
            return null;
        }

        private bool IsChapterFolderAlreadyDownloaded(string rootFolder, string siteFolder, GalleryItem item, string chapterLink)
        {
            if (item == null || string.IsNullOrWhiteSpace(chapterLink))
            {
                return false;
            }

            string bookFolder = GetDownloadBookFolderPath(rootFolder, siteFolder, item);
            if (string.IsNullOrWhiteSpace(bookFolder) || !Directory.Exists(bookFolder))
            {
                return false;
            }

            string chapterLabel = null;
            if (TryGetCachedDownloadChapterItems(item, out List<ReaderChapterItem> cachedItems) && cachedItems != null)
            {
                var found = cachedItems.FirstOrDefault(x => string.Equals(x.FolderPath, chapterLink, StringComparison.OrdinalIgnoreCase));
                if (found != null && !string.IsNullOrWhiteSpace(found.Name))
                {
                    chapterLabel = found.Name.Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(chapterLabel))
            {
                chapterLabel = QueryChapterLabelFromProcessDb(rootFolder, siteFolder, item, chapterLink);
            }

            if (string.IsNullOrWhiteSpace(chapterLabel))
            {
                chapterLabel = GetChapterProcessLabel(chapterLink);
            }

            if (string.IsNullOrWhiteSpace(chapterLabel))
            {
                return false;
            }

            string safeChapter = GetDownloadChapterFolderName(item.Name, chapterLabel);
            string safeBook = GetSafePathName(item.Name);

            string[] candidates = new[]
            {
                Path.Combine(bookFolder, safeChapter),
                Path.Combine(bookFolder, $"{safeBook}-{safeChapter}"),
                Path.Combine(Path.GetDirectoryName(bookFolder) ?? string.Empty, $"{safeBook}-{safeChapter}"),
                Path.Combine(Path.GetDirectoryName(bookFolder) ?? string.Empty, safeBook, safeChapter)
            };

            foreach (string candidate in candidates.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (Directory.Exists(candidate) && Directory.EnumerateFileSystemEntries(candidate).Any())
                    {
                        return true;
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        private List<string> FilterPendingChapterLinksFromProcess(string rootFolder, string siteFolder, GalleryItem item, IList<string> chapterLinks, IDictionary<string, string> chapterLabelsByLink = null)
        {
            bool preserveExistingDone = ShouldPreserveExistingProcessState(rootFolder, siteFolder, item);
            InitializeChapterProcess(rootFolder, siteFolder, item, chapterLinks, preserveExistingDone, chapterLabelsByLink);
            var pending = (LoadPendingChapterLinksFromProcess(rootFolder, siteFolder, item) ?? chapterLinks.ToList())
                .Where(link => !IsChapterFolderAlreadyDownloaded(rootFolder, siteFolder, item, link))
                .ToList();

            return pending;
        }

        internal void DeleteProcessMarkdownForItem(GalleryItem item)
        {
            if (item == null)
            {
                return;
            }

            string rootFolder = item.DownloadPath;
            if (string.IsNullOrWhiteSpace(rootFolder) && !string.IsNullOrWhiteSpace(txtDownloadPath?.Text))
            {
                rootFolder = txtDownloadPath.Text.Trim();
            }

            if (string.IsNullOrWhiteSpace(rootFolder))
            {
                return;
            }

            string siteFolder = GetProcessSiteFolder(item);

            // Delete from SQLite database
            string dbPath = GetProcessDbPath(rootFolder);
            string bookKey = GetDownloadProcessBookKey(item);
            string safeBookKey = GetSafePathName(bookKey.Replace("|", "-"));
            if (safeBookKey.Length > 120)
            {
                safeBookKey = safeBookKey.Substring(0, 120).Trim();
            }

            lock (_dbLock)
            {
                if (File.Exists(dbPath))
                {
                    try
                    {
                        using (var conn = OpenProcessConnection(dbPath))
                        {
                            string sql = "DELETE FROM download_process WHERE site = @site AND book_key = @book_key";
                            using (var cmd = new System.Data.SQLite.SQLiteCommand(sql, conn))
                            {
                                cmd.Parameters.AddWithValue("@site", siteFolder);
                                cmd.Parameters.AddWithValue("@book_key", safeBookKey);
                                cmd.ExecuteNonQuery();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[sqlite] Lỗi xóa process của '{item.Name}': {ex.Message}");
                    }
                }
            }

            foreach (string path in new[]
            {
                GetDownloadProcessFilePath(rootFolder, siteFolder, item),
                GetConfiguredScopedDownloadProcessFilePath(rootFolder, siteFolder, item),
                GetLegacyDownloadProcessFilePath(rootFolder, siteFolder, item)
            })
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch (Exception ex)
                {
                    Log($"[Cleanup Warning] Không thể xóa file process '{path}': {ex.Message}");
                }
            }
        }

        private string GuessChapterNameFromLink(string link)
        {
            return GetChapterProcessLabel(link);
        }

        private async void BtnStartDownloadToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_suppressDownloadToggleEvent)
            {
                return;
            }

            if (btnStartDownload?.IsChecked == true)
            {
                await HandleStartDownloadToggleCheckedAsync();
                return;
            }

            BtnStopDownload_Click(sender, e);
        }

        private async Task HandleStartDownloadToggleCheckedAsync(bool suppressWarning = false)
        {
            string downloadRoot = txtDownloadPath.Text.Trim();
            if (string.IsNullOrEmpty(downloadRoot))
            {
                SetDownloadToggleState(false);
                if (!suppressWarning)
                {
                    MessageBox.Show("Vui lòng chọn thư mục lưu (Please select a download folder).", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return;
            }

            var itemsToDownload = GetGalleryItemsSnapshot().Where(item => item.IsChecked).ToList();
            if (!itemsToDownload.Any())
            {
                SetDownloadToggleState(false);
                if (!suppressWarning)
                {
                    MessageBox.Show("Vui lòng tích chọn ít nhất 1 truyện để tải (Please check at least one gallery to download).", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            if (_downloadCts != null)
            {
                int addedCount = QueueDownloadsForCurrentSession(itemsToDownload, preserveExistingState: true);
                if (addedCount <= 0)
                {
                    if (!suppressWarning)
                    {
                        MessageBox.Show("Không có truyện mới nào để thêm vào hàng tải hiện tại.\nThere are no new checked books to add to the current queue.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    return;
                }

                Log($"[Download] Đã thêm {addedCount} truyện vào hàng tải hiện tại.");
                lblStatus.Text = _isVietnameseUi
                    ? $"Đã thêm {addedCount} truyện vào hàng chờ..."
                    : $"Added {addedCount} books to active queue...";
                return;
            }

            SetDownloadToggleState(true);
            await StartDownloadProcessAsync(itemsToDownload, preserveExistingState: true);
        }

        private async void BtnStartDownload_Click(object sender, RoutedEventArgs e)
        {
            if (_suppressDownloadToggleEvent)
            {
                return;
            }

            string downloadRoot = txtDownloadPath.Text.Trim();
            if (string.IsNullOrEmpty(downloadRoot))
            {
                MessageBox.Show("Vui lòng chọn thư mục lưu (Please select a download folder).", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var itemsToDownload = GetGalleryItemsSnapshot().Where(item => item.IsChecked).ToList();
            if (!itemsToDownload.Any())
            {
                MessageBox.Show("Vui lòng tích chọn ít nhất 1 truyện để tải (Please check at least one gallery to download).", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_downloadCts != null)
            {
                int addedCount = QueueDownloadsForCurrentSession(itemsToDownload, preserveExistingState: true);
                if (addedCount <= 0)
                {
                    MessageBox.Show("Không có truyện mới nào để thêm vào hàng tải hiện tại.\nThere are no new checked books to add to the current queue.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                Log($"[Download] Đã thêm {addedCount} truyện vào hàng tải hiện tại.");
                lblStatus.Text = _isVietnameseUi
                    ? $"Đã thêm {addedCount} truyện vào hàng chờ..."
                    : $"Added {addedCount} books to active queue...";
                return;
            }

            SetDownloadToggleState(true);
            await StartDownloadProcessAsync(itemsToDownload, preserveExistingState: true);
        }

        private async void BtnPauseDownload_Click(object sender, RoutedEventArgs e)
        {
            await Task.CompletedTask;
        }

        private void BtnStopDownload_Click(object sender, RoutedEventArgs e)
        {
            if (_suppressDownloadToggleEvent)
            {
                return;
            }

            if (btnAutoRetryErrors != null && btnAutoRetryErrors.IsChecked == true)
            {
                btnAutoRetryErrors.IsChecked = false;
            }

            _isDownloadPaused = false;
            if (_downloadCts != null)
            {
                try { _downloadCts.Cancel(); } catch {}
                try { _httpClient?.CancelPendingRequests(); } catch {}
                Log("Đang dừng quá trình tải xuống... (Stopping download process...)");

                foreach (var item in _scrapedItems)
                {
                    item.DownloadSpeedBytesPerSecond = 0;
                    if (item.Status == "Downloading" || item.Status == "Paused" || item.Status == "Queued")
                    {
                        item.IsStopped = true;
                        item.Status = "Cancelled";
                    }
                }

                // Best-effort cleanup so a new Start doesn't inherit leftover temp folders.
                CleanupActiveTempFolders();
            }
            else
            {
                foreach (var item in _scrapedItems)
                {
                    item.DownloadSpeedBytesPerSecond = 0;
                    if (item.Status == "Downloading" || item.Status == "Paused" || item.Status == "Queued")
                    {
                        item.IsStopped = true;
                        item.Status = "Cancelled";
                    }
                }
            }

            UpdateTotalDownloadSpeedHeader();
            UpdateLightNovelFloatingControlState();
        }

        internal async Task StartDownloadProcessAsync(System.Collections.Generic.List<GalleryItem> itemsToDownload, bool preserveExistingState = false)
        {
            if (itemsToDownload != null)
            {
                itemsToDownload.RemoveAll(item => item == null || item.IsParallelSplitParent);
            }

            if (itemsToDownload != null && itemsToDownload.Count > 0)
            {
                await EnsureDownloadMissingChapterScanBeforeDownloadAsync();
            }

            if (_downloadCts != null)
            {
                QueueDownloadsForCurrentSession(itemsToDownload, preserveExistingState);
                return;
            }

            string downloadRoot = txtDownloadPath.Text.Trim();
            if (string.IsNullOrEmpty(downloadRoot))
            {
                MessageBox.Show("Vui lòng chọn thư mục lưu (Please select a download folder).", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!Directory.Exists(downloadRoot))
            {
                try
                {
                    Directory.CreateDirectory(downloadRoot);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Không thể tạo thư mục lưu: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            // 17. Kiểm tra dung lượng đĩa trống trước khi tải (DriveInfo)
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(downloadRoot)));
                if (drive.IsReady && drive.AvailableFreeSpace < 1024L * 1024 * 1024) // < 1GB
                {
                    var msgVi = $"Cảnh báo: Dung lượng ổ đĩa {drive.Name} còn trống rất ít ({drive.AvailableFreeSpace / 1024 / 1024} MB, dưới 1GB). Bạn có muốn tiếp tục?";
                    var msgEn = $"Warning: Free space on drive {drive.Name} is very low ({drive.AvailableFreeSpace / 1024 / 1024} MB, under 1GB). Do you want to continue?";
                    if (MessageBox.Show(_isVietnameseUi ? msgVi : msgEn, "Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
                    {
                        SetDownloadToggleState(false);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Không thể kiểm tra dung lượng ổ đĩa: {ex.Message}");
            }

            _downloadCts = new CancellationTokenSource();
            CancellationToken token = _downloadCts.Token;
            _isDownloadPaused = false;
            UpdateLightNovelFloatingControlState();
            _activeDownloadRoot = downloadRoot;
            _downloadSessionTotalGalleries = 0;
            _downloadSessionCompletedGalleries = 0;
            _nextScheduledDownloadOrder = 0;
            _nextDownloadStartOrder = 0;
            lock (_downloadSessionLock)
            {
                _scheduledDownloadItems.Clear();
                _scheduledDownloadTasks.Clear();
                _scheduledDownloadOrder.Clear();
            }

            SetDownloadToggleState(true);

            btnBrowseFolder.IsEnabled = false;
            // Cho phép Get Link / Fetch Info trong khi download
            btnScrape.IsEnabled = true;
            btnFetchInfo.IsEnabled = true;
            if (btnViHentaiScrape != null) btnViHentaiScrape.IsEnabled = true;
            if (btnViHentaiFetchInfo != null) btnViHentaiFetchInfo.IsEnabled = true;
            if (btnTruyenqqScrape != null) btnTruyenqqScrape.IsEnabled = true;
            if (btnTruyenqqFetchInfo != null) btnTruyenqqFetchInfo.IsEnabled = true;
            if (btnNettruyenScrape != null) btnNettruyenScrape.IsEnabled = true;
            if (btnNettruyenFetchInfo != null) btnNettruyenFetchInfo.IsEnabled = true;
            if (btnHentaieraScrape != null) btnHentaieraScrape.IsEnabled = true;
            if (btnHentaieraFetchInfo != null) btnHentaieraFetchInfo.IsEnabled = true;
            // cmbConnections.IsEnabled = false;
            int maxParallelBooks = GetCurrentMultiDownloadLimit();
            _currentMaxParallelBooks = maxParallelBooks;

            Log($"Bắt đầu tải song song với tối đa {maxParallelBooks} truyện cùng lúc...");

            try
            {
                _activeBookSemaphore = new DynamicSemaphore(maxParallelBooks, () => _currentMaxParallelBooks);
                QueueDownloadsForCurrentSession(itemsToDownload, preserveExistingState);
                await WaitForAllScheduledDownloadsAsync(token);

                lblStatus.Text = _isVietnameseUi ? "Tải xuống hoàn tất!" : "Downloads completed!";
                bool allSucceeded = itemsToDownload != null &&
                                    itemsToDownload.Where(item => item != null).All(item => item.IsSuccessfullyCompleted());

                if (allSucceeded)
                {
                    Log("Tải xuống toàn bộ thành công!");
                    PlaySoundResource("download-finish.wav");

                    RunPostDownloadActions();

                    ShowToast(_isVietnameseUi ? "Tải xuống toàn bộ thành công! 🎉" : "All downloads completed successfully! 🎉");

                    if (_shutdownAfterCompleted)
                    {
                        Log("[Shutdown] Tải hoàn tất và tùy chọn tự động tắt máy đang bật. Hệ thống sẽ tắt sau 15 giây.");
                        System.Diagnostics.Process.Start("shutdown", "-s -t 15");
                    }

                    MessageBox.Show("Đã tải xong toàn bộ truyện được chọn!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    Log("Tải xong nhưng còn truyện/chapter lỗi.");
                    PlaySoundResource("error.wav");

                    RunPostDownloadActions();

                    ShowToast(_isVietnameseUi ? "Tải xong nhưng có lỗi xảy ra! ⚠️" : "Downloads completed with errors! ⚠️");

                    if (_shutdownAfterCompleted)
                    {
                        Log("[Shutdown] Tải xong (có lỗi) và tùy chọn tự động tắt máy đang bật. Hệ thống sẽ tắt sau 15 giây.");
                        System.Diagnostics.Process.Start("shutdown", "-s -t 15");
                    }

                    MessageBox.Show("Có truyện chưa tải xong. Xem cột trạng thái.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (OperationCanceledException)
            {
                Log("Quá trình tải xuống đã bị dừng bởi người dùng.");
                lblStatus.Text = _isVietnameseUi ? "Đã dừng tải." : "Download stopped.";
                ShowToast(_isVietnameseUi ? "Đã dừng quá trình tải." : "Download process stopped.");
            }
            catch (Exception ex)
            {
                Log($"Critical download error: {ex.Message}");
                lblStatus.Text = _isVietnameseUi ? "Tải xuống thất bại." : "Download failed.";
                PlaySoundResource("error.wav");
                ShowToast(_isVietnameseUi ? "Lỗi tải xuống nghiêm trọng!" : "Critical download error!");
            }
            finally
            {
                bool wasCancelled = _downloadCts != null && _downloadCts.IsCancellationRequested;
                _activeBookSemaphore = null;
                _activeDownloadRoot = null;
                _downloadSessionTotalGalleries = 0;
                _downloadSessionCompletedGalleries = 0;
                _nextScheduledDownloadOrder = 0;
                _nextDownloadStartOrder = 0;
                lock (_downloadSessionLock)
                {
                    _scheduledDownloadItems.Clear();
                    _scheduledDownloadTasks.Clear();
                    _scheduledDownloadOrder.Clear();
                }
                _downloadCts?.Dispose();
                _downloadCts = null;
                _isDownloadPaused = false;

                if (_currentSection == AppSection.Watch)
                {
                    _ = RefreshReaderLibraryWhenIdleAsync(forceRefresh: true);
                }

                SetDownloadToggleState(false);

                btnBrowseFolder.IsEnabled = true;
                btnOpenFolder.IsEnabled = true;
                btnScrape.IsEnabled = true;
                btnFetchInfo.IsEnabled = true;
                if (btnViHentaiScrape != null) btnViHentaiScrape.IsEnabled = true;
                if (btnViHentaiFetchInfo != null) btnViHentaiFetchInfo.IsEnabled = true;
                if (btnTruyenqqScrape != null) btnTruyenqqScrape.IsEnabled = true;
                if (btnTruyenqqFetchInfo != null) btnTruyenqqFetchInfo.IsEnabled = true;
                if (btnNettruyenScrape != null) btnNettruyenScrape.IsEnabled = true;
                if (btnNettruyenFetchInfo != null) btnNettruyenFetchInfo.IsEnabled = true;
                if (btnHentaieraScrape != null) btnHentaieraScrape.IsEnabled = true;
                if (btnHentaieraFetchInfo != null) btnHentaieraFetchInfo.IsEnabled = true;
                cmbConnections.IsEnabled = true;

                UpdateQueueErrorLabel();
                UpdateLightNovelFloatingControlState();
            }
        }

        private int QueueDownloadsForCurrentSession(IEnumerable<GalleryItem> itemsToDownload, bool preserveExistingState)
        {
            if (_downloadCts == null || _activeBookSemaphore == null || itemsToDownload == null)
            {
                return 0;
            }

            var orderedItems = OrderItemsByDisplayOrder(itemsToDownload);
            int addedCount = 0;
            foreach (var item in orderedItems)
            {
                if (item == null || item.IsParallelSplitParent)
                {
                    continue;
                }

                bool shouldSchedule;
                int scheduledOrder;
                lock (_downloadSessionLock)
                {
                    shouldSchedule = _scheduledDownloadItems.Add(item);
                    scheduledOrder = _nextScheduledDownloadOrder;
                    if (shouldSchedule)
                    {
                        _scheduledDownloadOrder[item] = _nextScheduledDownloadOrder++;
                    }
                }

                if (!shouldSchedule)
                {
                    continue;
                }

                PrepareGalleryItemForDownload(item, _activeDownloadRoot, preserveExistingState);
                Interlocked.Increment(ref _downloadSessionTotalGalleries);

                Task task = RunQueuedGalleryDownloadAsync(item, _activeDownloadRoot, null, _downloadCts.Token, scheduledOrder);
                lock (_downloadSessionLock)
                {
                    _scheduledDownloadTasks.Add(task);
                }

                addedCount++;
            }

            UpdateDownloadProgressLabel();
            return addedCount;
        }

        private void PrepareGalleryItemForDownload(GalleryItem item, string downloadRoot, bool preserveExistingState)
        {
            string domain = "";
            try { domain = new Uri(item.Link).Host; } catch { }

            Dispatcher.Invoke(() =>
            {
                item.Name = FormatGalleryTitle(item.Name);
                item.SourceDomain = domain;
                double num = ExtractNumber(item.LinkCount);
                item.TotalChapters = num > 0 ? (int)Math.Ceiling(num) : 1;
                item.DownloadPath = ConvertToLongPath(downloadRoot);

                if (!preserveExistingState)
                {
                    item.CompletedChapters = 0;
                    item.Status = "Queued";
                    item.CurrentProcess = string.Empty;
                    item.ErrorCount = 0;
                    item.ProgressPercent = 0;
                    item.IsPaused = false;
                    item.IsStopped = false;
                    item.Errors.Clear();
                }
                else
                {
                    item.Status = "Queued";
                    item.IsPaused = false;
                    item.IsStopped = false;
                }
            });
        }

        private bool IsGalleryItemScheduledForDownload(GalleryItem item)
        {
            if (item == null)
            {
                return false;
            }

            lock (_downloadSessionLock)
            {
                return _scheduledDownloadItems.Contains(item);
            }
        }

        private void QueueCheckedDownloadsForActiveSession()
        {
            if (_downloadCts == null)
            {
                return;
            }

            var items = GetGalleryItemsSnapshot()
                .Where(item => item != null && item.IsChecked)
                .Where(item => !IsGalleryItemScheduledForDownload(item))
                .Where(item => !item.IsSuccessfullyCompleted())
                .Where(item => !string.Equals(item.Status, "Error", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (items.Count > 0)
            {
                int addedCount = QueueDownloadsForCurrentSession(items, preserveExistingState: true);
                if (addedCount > 0)
                {
                    Log($"[Download] Tự thêm {addedCount} truyện mới vào hàng tải đang chạy.");
                }
            }
        }

        private void RefreshActiveDownloadConcurrency()
        {
            if (_downloadCts == null)
            {
                return;
            }

            _currentMaxParallelBooks = GetCurrentMultiDownloadLimit();
            _activeBookSemaphore?.AdjustLimit();
            TrimActiveDownloadsToCurrentLimit();
            QueueCheckedDownloadsForActiveSession();
        }

        private void TrimActiveDownloadsToCurrentLimit()
        {
            int limit = Math.Max(1, _currentMaxParallelBooks);
            var orderedItems = GetGalleryItemsSnapshot().ToList();
            var activeItems = _activeItemCancellationSources.Keys
                .Where(item => item != null && item.IsChecked && !item.IsStopped)
                .OrderBy(item =>
                {
                    int index = orderedItems.IndexOf(item);
                    return index >= 0 ? index : int.MaxValue;
                })
                .ToList();

            foreach (var item in activeItems.Skip(limit))
            {
                Dispatcher.Invoke(() =>
                {
                    item.IsPaused = false;
                    item.IsStopped = false;
                    item.Status = "Queued";
                    item.CurrentProcess = _isVietnameseUi
                        ? "Giam so luong tai song song, cho den luot"
                        : "Parallel limit reduced, waiting turn";
                });
                CancelGalleryItemDownload(item);
            }
        }

        private void CancelGalleryItemDownload(GalleryItem item)
        {
            if (item == null)
            {
                return;
            }

            if (_activeItemCancellationSources.TryGetValue(item, out CancellationTokenSource cts))
            {
                try
                {
                    if (!cts.IsCancellationRequested)
                    {
                        cts.Cancel();
                    }
                }
                catch
                {
                }
            }
        }

        private void HandleGalleryItemCheckedStateChanged(GalleryItem item)
        {
            if (item == null)
            {
                return;
            }

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => HandleGalleryItemCheckedStateChanged(item)));
                return;
            }

            if (_downloadCts == null)
            {
                return;
            }

            bool isScheduled = IsGalleryItemScheduledForDownload(item);
            bool isDownloading = string.Equals(item.Status, "Downloading", StringComparison.OrdinalIgnoreCase);
            bool isQueued = string.Equals(item.Status, "Queued", StringComparison.OrdinalIgnoreCase);
            bool isPaused = string.Equals(item.Status, "Paused", StringComparison.OrdinalIgnoreCase);
            bool isCompleted = string.Equals(item.Status, "Completed", StringComparison.OrdinalIgnoreCase);

            if (!item.IsChecked)
            {
                if (isCompleted)
                {
                    return;
                }

                if (!isScheduled && !isDownloading && !isQueued && !isPaused)
                {
                    return;
                }

                item.IsPaused = false;
                item.IsStopped = true;
                CancelGalleryItemDownload(item);
                item.Status = "Stopped";
                item.CurrentProcess = _isVietnameseUi ? "Bo tick, dung tai" : "Unchecked, stop download";
                Log($"[Download] Bo tick, dung book: {item.Name}");
                return;
            }

            if (isCompleted)
            {
                if (item.IsChecked)
                {
                    QueueDownloadMissingChapterRescan(item);
                }
                return;
            }

            if (isDownloading && item.IsStopped)
            {
                item.CurrentProcess = _isVietnameseUi ? "Dang dung de tai lai" : "Stopping before requeue";
                return;
            }

            item.IsStopped = false;
            item.IsPaused = false;
            if (isScheduled || string.Equals(item.Status, "Stopped", StringComparison.OrdinalIgnoreCase))
            {
                item.Status = "Queued";
                item.CurrentProcess = string.Empty;
            }

            System.Diagnostics.Debug.Assert(item.IsChecked, "Gallery item must stay checked before requeue.");

            if (!isScheduled)
            {
                item.Status = "Queued";
                item.CurrentProcess = string.Empty;
                Log($"[Download] Tick lai, them book vao hang tai: {item.Name}");
                QueueDownloadMissingChapterRescan(item);
                _ = StartDownloadProcessAsync(new List<GalleryItem> { item }, preserveExistingState: true);
            }
            else if (item.IsChecked)
            {
                QueueDownloadMissingChapterRescan(item);
            }
        }

        private void SetDownloadToggleState(bool isRunning)
        {
            Dispatcher.Invoke(() =>
            {
                if (btnStartDownload == null)
                {
                    return;
                }

                if (lblDownloadToggleText != null)
                {
                    lblDownloadToggleText.Text = isRunning ? (_isVietnameseUi ? "ĐANG TẢI" : "DOWNLOADING") : (_isVietnameseUi ? "DOWNLOAD" : "DOWNLOAD");
                    lblDownloadToggleText.Foreground = isRunning 
                        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x39, 0xFF, 0x14)) // Neon Green
                        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x00, 0x7F)); // Cyberpunk Pink/Red
                }

                _suppressDownloadToggleEvent = true;
                try
                {
                    btnStartDownload.IsChecked = isRunning;
                    btnStartDownload.ToolTip = _isVietnameseUi
                        ? (isRunning ? "DỪNG TẢI" : "TẢI TẤT CẢ")
                        : (isRunning ? "STOP DOWNLOAD" : "DOWNLOAD ALL");

                    if (!isRunning && tglClearCookieAndRetry != null && tglClearCookieAndRetry.IsChecked == true)
                    {
                        tglClearCookieAndRetry.IsChecked = false;
                    }
                }
                finally
                {
                    _suppressDownloadToggleEvent = false;
                }
            });

            UpdateCompactDownloadToolbarState();
            UpdateGlobalDownloadProgress();
        }

        private Task RunQueuedGalleryDownloadAsync(GalleryItem item, string downloadRoot, ChapterFilter chapterFilter, CancellationToken token, int scheduledOrder)
        {
            return Task.Run(async () =>
            {
                bool hasSemaphoreSlot = false;
                CancellationTokenSource itemCts = null;
                ChapterFilter effectiveChapterFilter = GetChapterSelectionFilterForItem(item);
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    int currentOrder = Volatile.Read(ref _nextDownloadStartOrder);
                    if (currentOrder == scheduledOrder)
                    {
                        if (item.IsStopped || !item.IsChecked || item.IsParallelSplitParent)
                        {
                            Interlocked.CompareExchange(ref _nextDownloadStartOrder, scheduledOrder + 1, scheduledOrder);
                            throw new OperationCanceledException();
                        }

                        break;
                    }

                    await Task.Delay(100, token);
                }

                await _activeBookSemaphore.WaitAsync(token);
                hasSemaphoreSlot = true;
                Interlocked.Increment(ref _nextDownloadStartOrder);
                itemCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                _activeItemCancellationSources.AddOrUpdate(item, itemCts, (_, existingCts) =>
                {
                    try { existingCts.Cancel(); } catch {}
                    try { existingCts.Dispose(); } catch {}
                    return itemCts;
                });
                CancellationToken itemToken = itemCts.Token;
                CancellationTokenSource speedTrackerCts = null;
                Task speedTrackerTask = null;
                bool countAsCompleted = true;
                bool requeueAfterRelease = false;
                try
                {
                    while (_isDownloadPaused || item.IsPaused)
                    {
                        itemToken.ThrowIfCancellationRequested();
                        if (item.IsStopped || item.IsParallelSplitParent) throw new OperationCanceledException();
                        await Task.Delay(200, itemToken);
                    }
                    if (item.IsParallelSplitParent) throw new OperationCanceledException();
                    itemToken.ThrowIfCancellationRequested();

                    Dispatcher.Invoke(() =>
                    {
                        item.Status = "Downloading";
                    });

                    Log($"[Download] Đang tải: {item.Name} ({item.Link})");

                    item.DownloadSpeedBytesPerSecond = 0;
                    item._downloadedBytesAccumulator = 0;
                    UpdateTotalDownloadSpeedHeader();

                    speedTrackerCts = CancellationTokenSource.CreateLinkedTokenSource(itemToken);
                    var capturedCts = speedTrackerCts;
                    speedTrackerTask = Task.Run(async () =>
                    {
                        long lastBytes = 0;
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        try
                        {
                            while (!capturedCts.Token.IsCancellationRequested)
                            {
                                await Task.Delay(1000, capturedCts.Token);
                                long currentBytes = System.Threading.Interlocked.Read(ref item._downloadedBytesAccumulator);
                                long delta = currentBytes - lastBytes;
                                lastBytes = currentBytes;
                                double elapsedSec = sw.Elapsed.TotalSeconds;
                                sw.Restart();
                                
                                long speed = elapsedSec > 0 ? (long)(delta / elapsedSec) : 0L;
                                await Dispatcher.InvokeAsync(() =>
                                {
                                    if (!_isDownloadPaused &&
                                        !item.IsPaused &&
                                        !item.IsStopped &&
                                        item.IsChecked &&
                                        string.Equals(item.Status, "Paused", StringComparison.OrdinalIgnoreCase))
                                    {
                                        item.Status = "Downloading";
                                    }

                                    item.DownloadSpeedBytesPerSecond = speed;
                                    UpdateTotalDownloadSpeedHeader();
                                });
                            }
                        }
                        catch (OperationCanceledException) {}
                        finally
                        {
                            await Dispatcher.InvokeAsync(() =>
                            {
                                item.DownloadSpeedBytesPerSecond = 0;
                                UpdateTotalDownloadSpeedHeader();
                            });
                        }
                    });

                    try
                    {
                        await DownloadGalleryAsync(item, downloadRoot, itemToken, item, effectiveChapterFilter);

                        if (item.GetUniqueErrorCount() > 0)
                        {
                            await RetryAllDownloadQueueItemErrorsAsync(item, itemToken);
                        }

                        await Dispatcher.InvokeAsync(() =>
                        {
                            bool hasErrors = item.HasAnyErrors();
                            item.Status = hasErrors ? "Error" : "Completed";
                            item.CurrentProcess = GetDoneProcessText(item, hasErrors);
                            item.IsChecked = hasErrors ? item.IsChecked : false;
                        });
                        QueueParallelSplitCollapseIfReady(item);
                        
                        // ponytail: Tự động đếm lại số lượng ảnh thực tế trên đĩa
                        try
                        {
                            await RefreshDiskImageCountAsync(item);
                        }
                        catch {}

                        await Dispatcher.InvokeAsync(async () =>
                        {
                            await RefreshReaderLibraryAsync(forceRefresh: true);
                        });

                        Log($"[Download] Hoàn thành truyện: {item.Name}");

                        try
                        {
                            int chapCount = item.CompletedChapters;
                            if (chapCount <= 0) chapCount = 1;
                            string dlPath = GetConfiguredDownloadRoot(item.DownloadPath ?? downloadRoot, item);
                            AddToHistory(item, chapCount, dlPath);
                        }
                        catch
                        {
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        if (token.IsCancellationRequested)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                if (string.Equals(item.Status, "Downloading", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(item.Status, "Paused", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(item.Status, "Queued", StringComparison.OrdinalIgnoreCase))
                                {
                                    item.Status = "Cancelled";
                                }
                            });
                            throw;
                        }

                        bool shouldRequeue = _downloadCts != null && item.IsChecked && !item.IsParallelSplitParent;
                        if (shouldRequeue)
                        {
                            countAsCompleted = false;
                            requeueAfterRelease = true;
                        }

                        Dispatcher.Invoke(() =>
                        {
                            item.Status = shouldRequeue ? "Queued" : "Stopped";
                            item.CurrentProcess = shouldRequeue
                                ? (_isVietnameseUi ? "Cho tai lai" : "Waiting to resume")
                                : (_isVietnameseUi ? "Bo tick, da dung" : "Unchecked, stopped");
                        });

                        return;
                    }
                    catch (Exception ex)
                    {
                        bool is429 = ex.Message != null && (
                            ex.Message.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            ex.Message.IndexOf("too many requests", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            ex.Message.IndexOf("too many request", StringComparison.OrdinalIgnoreCase) >= 0
                        );

                        if (is429)
                        {
                            Log($"[RateLimit 429] Phát hiện 429 cho '{item.Name}'. Tạm dừng tải...");
                            countAsCompleted = false;
                            PauseAllDownloads();
                            Dispatcher.Invoke(() =>
                            {
                                item.Status = "Paused";
                                item.CurrentProcess = "429 pause";
                            });

                            Log("[RateLimit 429] Chờ 5 giây...");
                            await Task.Delay(5000, token);

                            Log("[RateLimit 429] Đang xóa temp/process/webview2 theo book...");
                            Delete429ArtifactsForItem(item, downloadRoot);

                            Log("[RateLimit 429] Đang xóa cookie...");
                            InitializeHttpClientState();

                            Log("[RateLimit 429] Đợi thêm 5 giây trước khi tải lại...");
                            await Task.Delay(5000, token);

                            Dispatcher.Invoke(() =>
                            {
                                item.Status = "Queued";
                                item.Errors.Clear();
                                item.ErrorCount = 0;
                                item.CurrentProcess = "Retrying after 429...";
                                item.CompletedChapters = 0;
                                item.IsChecked = true;
                            });

                            ResumeAllDownloads();
                            Log($"[RateLimit 429] Đang bắt đầu tải lại '{item.Name}'...");
                            _ = StartDownloadProcessAsync(new List<GalleryItem> { item }, preserveExistingState: false);
                            
                            throw new OperationCanceledException("Cancelled due to RateLimit 429", ex);
                        }

                        Log($"[Lỗi] Không thể tải truyện '{item.Name}': {ex.ToString()}");
                        Dispatcher.Invoke(() =>
                        {
                            item.Status = "Error";
                            if (item.HasNoChapters)
                            {
                                return;
                            }
                            string chapterLabel = item.SourceDomain != null && IsNhentaiSource(item.SourceDomain)
                                ? string.Empty
                                : "General";
                            string rootTrace = ex.Message;
                            string rootTraceUrl = null;
                            if (item.SourceDomain != null && IsNhentaiSource(item.SourceDomain))
                            {
                                rootTraceUrl = item.Link;
                                rootTrace = $"Book: {item.Link}{Environment.NewLine}Error: {ex.Message}";
                            }

                            item.AddError(chapterLabel, 0, rootTrace, rootTraceUrl, rootTraceUrl);
                            RecordCheckError(item.SourceDomain ?? "general", item.Name, chapterLabel, 0, rootTrace, rootTraceUrl);
                        });
                        QueueParallelSplitCollapseIfReady(item);
                    }
                    finally
                    {
                        if (speedTrackerCts != null)
                        {
                            speedTrackerCts.Cancel();
                            if (speedTrackerTask != null)
                            {
                                try { await speedTrackerTask; } catch {}
                            }
                            speedTrackerCts.Dispose();
                        }
                        if (countAsCompleted)
                        {
                            Interlocked.Increment(ref _downloadSessionCompletedGalleries);
                        }
                        UpdateDownloadProgressLabel();
                        UpdateQueueErrorLabel();
                    }
                }
                finally
                {
                    if (_activeItemCancellationSources.TryRemove(item, out CancellationTokenSource activeItemCts))
                    {
                        try { activeItemCts.Dispose(); } catch {}
                    }

                    lock (_downloadSessionLock)
                    {
                        _scheduledDownloadItems.Remove(item);
                        _scheduledDownloadOrder.Remove(item);
                    }

                    if (hasSemaphoreSlot)
                    {
                        _activeBookSemaphore?.Release();
                    }

                    if (requeueAfterRelease && _downloadCts != null && item.IsChecked && !token.IsCancellationRequested)
                    {
                        _ = StartDownloadProcessAsync(new List<GalleryItem> { item }, preserveExistingState: true);
                    }
                }
            }, token);
        }

        private async Task RetryAllDownloadQueueItemErrorsAsync(GalleryItem item, CancellationToken token)
        {
            if (item == null)
            {
                return;
            }

            int lastRemainingCount = int.MaxValue;
            bool attemptedRetry = false;

            while (true)
            {
                token.ThrowIfCancellationRequested();

                int currentRemainingCount = item.GetUniqueErrorCount();
                if (currentRemainingCount <= 0)
                {
                    return;
                }

                if (attemptedRetry)
                {
                    if (currentRemainingCount >= lastRemainingCount)
                    {
                        Log($"[Retry] Không còn tiến triển để retry tiếp cho '{item.Name}'.");
                        return;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(2), token);
                }

                attemptedRetry = true;
                lastRemainingCount = currentRemainingCount;
                await RetryDownloadQueueItemErrorsAsync(item, showMessageBox: false);
            }
        }

        private async Task WaitForAllScheduledDownloadsAsync(CancellationToken token)
        {
            while (true)
            {
                Task[] pendingTasks;
                lock (_downloadSessionLock)
                {
                    pendingTasks = _scheduledDownloadTasks.Where(task => !task.IsCompleted).ToArray();
                }

                if (pendingTasks.Length == 0)
                {
                    return;
                }

                Task completedTask = await Task.WhenAny(pendingTasks);
                try
                {
                    await completedTask;
                }
                catch (OperationCanceledException)
                {
                    if (token.IsCancellationRequested)
                    {
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    Log($"[Download Warning] Một task tải bị lỗi nhưng phiên tải vẫn tiếp tục: {ex.Message}");
                }

                token.ThrowIfCancellationRequested();
            }
        }

        private void UpdateDownloadProgressLabel()
        {
            int total = Math.Max(0, _downloadSessionTotalGalleries);
            int completed = Math.Max(0, _downloadSessionCompletedGalleries);

            Dispatcher.Invoke(() =>
            {
                lblStatus.Text = _isVietnameseUi
                    ? $"Đang tải {completed}/{total} truyện..."
                    : $"Downloading {completed}/{total} galleries...";
            });
        }

        private async Task DownloadGalleryAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem = null, ChapterFilter chapterFilter = null)
        {
            string hostName = "hentaiforce.net";
            try
            {
                hostName = new Uri(item.Link).Host;
            }
            catch {}

            if (IsNhentaiUrl(item.Link))
            {
                await DownloadNhentaiGalleryAsync(item, rootFolder, token, queueItem);
                return;
            }

            if (item.Link != null && item.Link.Contains("hitomi.la"))
            {
                await DownloadHitomiLaGalleryAsync(item, rootFolder, token, queueItem);
                return;
            }

            if (hostName.Contains("vi-hentai.pro"))
            {
                await DownloadViHentaiGalleryAsync(item, rootFolder, token, queueItem, chapterFilter);
                return;
            }

            if (hostName.Contains("hentaiera.com"))
            {
                await DownloadHentaieraGalleryAsync(item, rootFolder, token, queueItem, chapterFilter);
                return;
            }

            if (hostName.Contains("hentai2read.com") || hostName.Contains("static.hentaicdn.com"))
            {
                await DownloadHentai2readGalleryAsync(item, rootFolder, token, queueItem, chapterFilter);
                return;
            }

            if (IsDamconuongUrl(item.Link))
            {
                await DownloadDamconuongGalleryAsync(item, rootFolder, token, queueItem, chapterFilter);
                return;
            }

            if (IsDaomeodenUrl(item.Link) || IsDaomeodenImageRedirectUrl(item.Link))
            {
                await DownloadDaomeodenGalleryAsync(item, rootFolder, token, queueItem, chapterFilter);
                return;
            }

            if (IsDilibUrl(item.Link))
            {
                await DownloadDilibGalleryAsync(item, rootFolder, token, queueItem, chapterFilter);
                return;
            }

            if (IsLoppyUrl(item.Link))
            {
                await DownloadLoppyGalleryAsync(item, rootFolder, token, queueItem, chapterFilter);
                return;
            }

            if (IsHaibabaUrl(item.Link))
            {
                await DownloadHaibabaGalleryAsync(item, rootFolder, token, queueItem, chapterFilter);
                return;
            }

            if (IsMangadexUrl(item.Link))
            {
                await DownloadMangadexGalleryAsync(item, rootFolder, token, queueItem, chapterFilter);
                return;
            }

            if (IsHakoUrl(item.Link))
            {
                await DownloadHakoNovelAsync(item, rootFolder, token, queueItem, chapterFilter);
                return;
            }

            if (IsTruyenqqUrl(item.Link))
            {
                await DownloadTruyenqqGalleryAsync(item, rootFolder, token, queueItem, chapterFilter);
                return;
            }

            if (IsTruyenggvnUrl(item.Link) || IsTruyenggvnImageUrl(item.Link))
            {
                await DownloadTruyenggvnGalleryAsync(item, rootFolder, token, queueItem, chapterFilter);
                return;
            }

            if (IsNettruyenUrl(item.Link))
            {
                await DownloadNettruyenGalleryAsync(item, rootFolder, token, queueItem, chapterFilter);
                return;
            }

            string safeTitle = GetSafePathName(item.Name);
            string resolvedRoot = GetConfiguredDownloadRoot(rootFolder, item);
            string targetFolder = Path.Combine(resolvedRoot, safeTitle);
            string tempFolder = BuildStableTempFolderPath(resolvedRoot, hostName, safeTitle, item.Link, item.Name);
            Directory.CreateDirectory(tempFolder);
            RegisterTempFolder(tempFolder);

            // Fetch gallery homepage
            string html = await FetchStringAsync(item.Link, token);

            // 1. Find total pages
            int totalPages = 1;
            var pagesMatch = Regex.Match(html, @"Pages:\s*(\d+)", RegexOptions.IgnoreCase);
            if (pagesMatch.Success)
            {
                totalPages = int.Parse(pagesMatch.Groups[1].Value);
            }
            else
            {
                var thumbMatches = Regex.Matches(html, @"href=""[^""]*?/view/\d+/(\d+)""", RegexOptions.IgnoreCase);
                foreach (Match m in thumbMatches)
                {
                    if (int.TryParse(m.Groups[1].Value, out int pageNum))
                    {
                        if (pageNum > totalPages) totalPages = pageNum;
                    }
                }
            }

            if (queueItem != null)
            {
                Dispatcher.Invoke(() =>
                {
                    queueItem.TotalChapters = totalPages;
                    queueItem.CompletedChapters = 0;
                });
            }

            WriteTempProgressLog(tempFolder, item, "Downloading", 0, totalPages, "0/0 pages", "Bắt đầu tải HentaiForce");

            // 2. Identify path pattern
            string prefix = null;
            string ext = "jpg";
            var patternMatch = Regex.Match(html, @"(?<prefix>https?://[a-zA-Z0-9.-]+/img/\d+-)1t\.(?<ext>[a-zA-Z0-9]+)", RegexOptions.IgnoreCase);
            
            if (patternMatch.Success)
            {
                prefix = patternMatch.Groups["prefix"].Value;
                ext = patternMatch.Groups["ext"].Value;
            }
            else
            {
                var generalMatch = Regex.Match(html, @"(https?://[a-zA-Z0-9.-]+/img/\d+-)\d+t\.(jpg|png|jpeg|webp)", RegexOptions.IgnoreCase);
                if (generalMatch.Success)
                {
                    prefix = generalMatch.Groups[1].Value;
                    ext = generalMatch.Groups[2].Value;
                }
            }

            // Get number of connections
            int maxThreads = GetCurrentConnectionLimit();

            bool isFastPath = !string.IsNullOrEmpty(prefix);
            Log($"[Đa luồng] Bắt đầu tải {totalPages} trang với tối đa {maxThreads} kết nối song song...");

            using (var semaphore = new DynamicSemaphore(maxThreads, GetCurrentConnectionLimit))
            {
                var tasks = new System.Collections.Generic.List<Task>();
                int completedPages = 0;
                object lockObj = new object();

                for (int p = 1; p <= totalPages; p++)
                {
                    int pageNum = p;
                    tasks.Add(Task.Run(async () =>
                    {
                        // Check pause/cancel before waiting on semaphore
                        while (_isDownloadPaused || item.IsPaused)
                        {
                            token.ThrowIfCancellationRequested();
                            if (item.IsStopped) throw new OperationCanceledException();
                            await Task.Delay(200, token);
                        }
                        token.ThrowIfCancellationRequested();

                        await semaphore.WaitAsync(token);
                        try
                        {
                            // Check pause/cancel after acquiring semaphore
                            while (_isDownloadPaused || item.IsPaused)
                            {
                                token.ThrowIfCancellationRequested();
                                if (item.IsStopped) throw new OperationCanceledException();
                                await Task.Delay(200, token);
                            }
                            token.ThrowIfCancellationRequested();

                            string fileName = isFastPath
                                ? BuildOrderedImageFilename(pageNum, $"{prefix}{pageNum}.{ext}", "." + ext, $"page-{pageNum}")
                                : BuildOrderedImageFilename(pageNum, null, ".jpg", $"page-{pageNum}");
                            string localFilePath = Path.Combine(tempFolder, fileName);
                            string finalFilePath = Path.Combine(targetFolder, fileName);
                            string downloadedPath = localFilePath;
                            var pageWatch = Stopwatch.StartNew();

                            // Skip if file already exists in either temp or final folder
                            if ((File.Exists(localFilePath) && new FileInfo(localFilePath).Length > 1024) ||
                                (File.Exists(finalFilePath) && new FileInfo(finalFilePath).Length > 1024))
                            {
                                pageWatch.Stop();
                                lock (lockObj)
                                {
                                    completedPages++;
                                    UpdateDownloadRowMetrics(queueItem, completedPages, totalPages, $"{completedPages}/{totalPages} pages", 0, 0);
                                }
                                return;
                            }

                            if (isFastPath)
                            {
                                string imgUrl = $"{prefix}{pageNum}.{ext}";
                                try
                                {
                                    await DownloadUrlToFileWithRefererAsync(imgUrl, null, localFilePath, token);
                                }
                                catch (Exception ex)
                                {
                                    Log($"[Fast Path] Lỗi trang {pageNum} ({ex.Message}). Thử Slow Path fallback...");
                                    downloadedPath = await DownloadPageSlowPathAsync(item, pageNum, localFilePath, token);
                                }
                            }
                            else
                            {
                                downloadedPath = await DownloadPageSlowPathAsync(item, pageNum, localFilePath, token);
                            }
                            if (isFastPath && string.Equals(downloadedPath, localFilePath, StringComparison.Ordinal))
                            {
                                downloadedPath = localFilePath;
                            }
                            pageWatch.Stop();

                            lock (lockObj)
                            {
                                completedPages++;
                                long downloadedBytes = !string.IsNullOrWhiteSpace(downloadedPath) && File.Exists(downloadedPath) ? new FileInfo(downloadedPath).Length : 0;
                                UpdateDownloadRowMetrics(queueItem, completedPages, totalPages, $"{completedPages}/{totalPages} pages", downloadedBytes, pageWatch.ElapsedMilliseconds);
                                WriteTempProgressLog(tempFolder, item, "Downloading", completedPages, totalPages, $"{completedPages}/{totalPages} pages", $"Page {pageNum} completed");
                            }
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, token));
                }

                await Task.WhenAll(tasks);

                WriteTempProgressLog(tempFolder, item, "Done", totalPages, totalPages, $"{totalPages}/{totalPages} pages", "Download completed");
                MoveTempFolderToTarget(tempFolder, targetFolder, "HentaiForce");

                // Check for missing files
                ValidateDownloadedFiles(targetFolder, totalPages, queueItem, "Pages");
            }
        }

        private async Task<string> DownloadPageSlowPathAsync(GalleryItem item, int pageNum, string targetPath, CancellationToken token)
        {
            // Respect pause check
            while (_isDownloadPaused || item.IsPaused)
            {
                token.ThrowIfCancellationRequested();
                if (item.IsStopped) throw new OperationCanceledException();
                await Task.Delay(200, token);
            }
            token.ThrowIfCancellationRequested();

            string pageUrl = $"{item.Link}/{pageNum}";
            string html = await FetchStringAsync(pageUrl, token);

            string imgUrl = ExtractHentaiforceReaderImageUrl(html);

            if (!string.IsNullOrWhiteSpace(imgUrl))
            {
                // Adjust file extension based on actual source URL
                string actualExt = GetSafeImageExtensionFromUrl(imgUrl);
                string finalPath = targetPath;
                if (!string.IsNullOrEmpty(actualExt) && !targetPath.EndsWith(actualExt, StringComparison.OrdinalIgnoreCase))
                {
                    finalPath = Path.ChangeExtension(targetPath, actualExt);
                }

                // Respect pause check
                while (_isDownloadPaused || item.IsPaused)
                {
                    token.ThrowIfCancellationRequested();
                    if (item.IsStopped) throw new OperationCanceledException();
                    await Task.Delay(200, token);
                }
                token.ThrowIfCancellationRequested();

                await DownloadUrlToFileWithRefererAsync(imgUrl, null, finalPath, token);
                return finalPath;
            }
            throw new Exception($"Không thể trích xuất địa chỉ ảnh từ trang đọc {pageNum}");
        }

        private static string ExtractHentaiforceReaderImageUrl(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return null;
            }

            Match mainImgMatch = Regex.Match(
                html,
                @"<img[^>]+class\s*=\s*""[^""]*\bjs-main-img\b[^""]*""[^>]+(?:src|data-src)\s*=\s*""(?<imgUrl>https?://[^""]+)""",
                RegexOptions.IgnoreCase);
            if (!mainImgMatch.Success)
            {
                mainImgMatch = Regex.Match(
                    html,
                    @"<img[^>]+(?:src|data-src)\s*=\s*""(?<imgUrl>https?://[^""]+)""[^>]+class\s*=\s*""[^""]*\bjs-main-img\b[^""]*""",
                    RegexOptions.IgnoreCase);
            }

            if (mainImgMatch.Success)
            {
                return WebUtility.HtmlDecode(mainImgMatch.Groups["imgUrl"].Value).Trim();
            }

            Match fallbackMatch = Regex.Match(
                html,
                @"(?:src|data-src)\s*=\s*""(?<imgUrl>https?://[^""]+/img/[^""]+)""",
                RegexOptions.IgnoreCase);
            if (fallbackMatch.Success)
            {
                return WebUtility.HtmlDecode(fallbackMatch.Groups["imgUrl"].Value).Trim();
            }

            return null;
        }

        private async Task<byte[]> GetByteArrayWithRefererAsync(string url, string referer)
        {
            using (var httpClient = CreateScopedHttpClient(url))
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.Referrer = new Uri(referer);
                using (var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsByteArrayAsync();
                }
            }
        }

        private bool IsNhentaiSource(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return false;
            }

            return source.IndexOf("nhentai.net", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   source.IndexOf("nhentaimg.com", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsNhentaiUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            try
            {
                return IsNhentaiSource(new Uri(url).Host);
            }
            catch
            {
                return url.IndexOf("nhentai.net", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       url.IndexOf("nhentaimg.com", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        private async Task DownloadNhentaiGalleryAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem = null)
        {
            if (item.Link != null && item.Link.IndexOf("nhentai.xxx", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                item.Link = Regex.Replace(item.Link, @"nhentai\.xxx", "nhentai.net", RegexOptions.IgnoreCase);
            }
            if (item.SourceDomain != null && item.SourceDomain.IndexOf("nhentai.xxx", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                item.SourceDomain = "nhentai.net";
            }

            string safeTitle = GetSafePathName(item.Name);
            string resolvedRoot = GetConfiguredDownloadRoot(rootFolder, item);
            string targetFolder = Path.Combine(resolvedRoot, safeTitle);
            bool isNhentaiNet = true;
            string nhentaiSiteKey = "nhentai.net";
            string tempFolder = BuildStableTempFolderPath(resolvedRoot, nhentaiSiteKey, safeTitle, item.Link, item.Name);
            Directory.CreateDirectory(tempFolder);
            RegisterTempFolder(tempFolder);
            string normalizedBookUrl = NormalizeNhentaiBookUrl(item.Link);

            // nhentai.net: fetch book HTML once → extract all image URLs (avoid N reader page fetches → 429)
            string[] nhentaiNetImageUrls = null;
            if (isNhentaiNet)
            {
                nhentaiNetImageUrls = await ExtractNhentaiNetImageUrlsAsync(item, normalizedBookUrl, token);
                if (nhentaiNetImageUrls != null && nhentaiNetImageUrls.Length > 0)
                {
                    item.NhentaiTotalPagesHint = nhentaiNetImageUrls.Length;
                }
            }

            int totalPages = item.NhentaiTotalPagesHint > 0
                ? item.NhentaiTotalPagesHint
                : await GetNhentaiTotalPagesFromBookAsync(normalizedBookUrl, token);
            if (totalPages <= 0)
            {
                throw new Exception($"Không xác định được tổng số trang {nhentaiSiteKey}. Book: {normalizedBookUrl}");
            }
            item.NhentaiTotalPagesHint = totalPages;
            Log($"[{nhentaiSiteKey}] Book: {normalizedBookUrl} | Pages: {totalPages}");

            // Get number of connections
            int maxThreads = GetCurrentConnectionLimit();

            Log($"[Đa luồng {nhentaiSiteKey}] Bắt đầu tải {totalPages} trang, tối đa {maxThreads} kết nối song song...");

            using (var semaphore = new DynamicSemaphore(maxThreads, GetCurrentConnectionLimit))
            {
                var tasks = new System.Collections.Generic.List<Task>();
                int completedPages = 0;
                object lockObj = new object();

                for (int p = 1; p <= totalPages; p++)
                {
                    int pageNum = p;
                    // Pre-resolved image URL for nhentai.net (0-indexed in array)
                    string preResolvedUrl = (nhentaiNetImageUrls != null && pageNum - 1 < nhentaiNetImageUrls.Length)
                        ? nhentaiNetImageUrls[pageNum - 1] : null;

                    tasks.Add(Task.Run(async () =>
                    {
                        // Check pause/cancel before waiting on semaphore
                        while (_isDownloadPaused)
                        {
                            token.ThrowIfCancellationRequested();
                            await Task.Delay(200, token);
                        }
                        token.ThrowIfCancellationRequested();

                        await semaphore.WaitAsync(token);
                        try
                        {
                            // Check pause/cancel after acquiring semaphore
                            while (_isDownloadPaused)
                            {
                                token.ThrowIfCancellationRequested();
                                await Task.Delay(200, token);
                            }
                            token.ThrowIfCancellationRequested();

                            // Use pre-resolved extension if available, otherwise default to .jpg for checking existence
                            string checkFileName = BuildOrderedImageFilename(pageNum, preResolvedUrl, ".jpg", $"page-{pageNum}");
                            string localFilePath = Path.Combine(tempFolder, checkFileName);
                            string finalFilePath = Path.Combine(targetFolder, checkFileName);
                            string downloadedPath = localFilePath;
                            var pageWatch = Stopwatch.StartNew();

                            // Skip if file already exists in either temp or final folder (with any common image extension)
                            bool alreadyExists = false;
                            string existingFile = null;
                            string[] checkExts = { "jpg", "png", "webp", "gif", "jpeg", "bmp" };
                            foreach (var checkExt in checkExts)
                            {
                                string testPathTemp = Path.ChangeExtension(localFilePath, checkExt);
                                string testPathFinal = Path.ChangeExtension(finalFilePath, checkExt);
                                if (File.Exists(testPathTemp) && new FileInfo(testPathTemp).Length > 1024)
                                {
                                    alreadyExists = true;
                                    existingFile = testPathTemp;
                                    break;
                                }
                                if (File.Exists(testPathFinal) && new FileInfo(testPathFinal).Length > 1024)
                                {
                                    alreadyExists = true;
                                    existingFile = testPathFinal;
                                    break;
                                }
                            }

                            if (alreadyExists)
                            {
                                pageWatch.Stop();
                                lock (lockObj)
                                {
                                    completedPages++;
                                    UpdateDownloadRowMetrics(queueItem, completedPages, totalPages, $"{completedPages}/{totalPages} pages", 0, 0);
                                    WriteTempProgressLog(tempFolder, item, "Downloading", completedPages, totalPages, $"{completedPages}/{totalPages} pages", $"Page {pageNum} existed");
                                    Dispatcher.BeginInvoke(new Action(() =>
                                    {
                                        lblStatus.Text = $"[{completedPages}/{totalPages}] Tải {safeTitle} ({nhentaiSiteKey})";
                                    }));
                                }
                            }
                            else
                            {

                            try
                            {
                                if (!string.IsNullOrWhiteSpace(preResolvedUrl))
                                {
                                    // nhentai.net: download CDN URL directly (no reader page fetch)
                                    // With intelligent fallback for extension (webp -> jpg -> png) if one fails
                                    string actualFileName = BuildOrderedImageFilename(pageNum, preResolvedUrl);
                                    string directPath = Path.Combine(tempFolder, actualFileName);
                                    try
                                    {
                                        await DownloadUrlToFileWithRefererAsync(preResolvedUrl, normalizedBookUrl, directPath, token);
                                        downloadedPath = directPath;
                                        Log($"[nhentai.net] Trang {pageNum} -> {preResolvedUrl}");
                                    }
                                    catch (Exception ex)
                                    {
                                        Log($"[nhentai.net] Lỗi tải direct link trang {pageNum}: {ex.Message}. Đang thử fallback...");
                                        var mMatch = Regex.Match(preResolvedUrl, @"/galleries/(\d+)/");
                                        if (mMatch.Success)
                                        {
                                            string mediaId = mMatch.Groups[1].Value;
                                            string extFound = await FindValidExtensionAsync(mediaId, pageNum, "webp", null, normalizedBookUrl);
                                            if (extFound != null)
                                            {
                                                string fallbackUrl = $"https://i{(pageNum % 4) + 1}.nhentai.net/galleries/{mediaId}/{pageNum}.{extFound}";
                                                string fallbackFileName = BuildOrderedImageFilename(pageNum, fallbackUrl, $".{extFound}");
                                                string fallbackPath = Path.Combine(tempFolder, fallbackFileName);
                                                await DownloadUrlToFileWithRefererAsync(fallbackUrl, normalizedBookUrl, fallbackPath, token);
                                                downloadedPath = fallbackPath;
                                                Log($"[nhentai.net] Trang {pageNum} (Fallback thành công) -> {fallbackUrl}");
                                            }
                                            else
                                            {
                                                string readerPageUrl = normalizedBookUrl.TrimEnd('/') + "/" + pageNum + "/";
                                                Log($"[nhentai.net] Dò tìm extension thất bại. Thử tải qua reader page: {readerPageUrl}");
                                                string readerImgUrl = await ResolveNhentaiReaderImageUrlAsync(readerPageUrl, pageNum, token);
                                                string readerFileName = BuildOrderedImageFilename(pageNum, readerImgUrl);
                                                string readerPath = Path.Combine(tempFolder, readerFileName);
                                                await DownloadUrlToFileWithRefererAsync(readerImgUrl, readerPageUrl, readerPath, token);
                                                downloadedPath = readerPath;
                                                Log($"[nhentai.net] Trang {pageNum} (Reader page fallback thành công) -> {readerImgUrl}");
                                            }
                                        }
                                        else
                                        {
                                            throw;
                                        }
                                    }
                                }
                                else
                                {
                                    throw new Exception("Không tìm thấy link ảnh CDN được giải mã trước đó cho trang " + pageNum);
                                }
                            }
                            catch (Exception pageEx)
                            {
                                Log($"[nhentai] Lỗi trang {pageNum}: {pageEx.Message}");
                                if (queueItem != null)
                                {
                                    string pageUrl = item.Link.TrimEnd('/') + "/" + pageNum + "/";
                                    string directUrl = preResolvedUrl ?? ExtractNhentaiDirectImageUrl(pageEx.Message);
                                    string traceMessage =
                                        $"Book: {item.Link}{Environment.NewLine}" +
                                        $"Reader: {pageUrl}{Environment.NewLine}" +
                                        $"Image: {(string.IsNullOrWhiteSpace(directUrl) ? "N/A" : directUrl)}{Environment.NewLine}" +
                                        $"Error: {pageEx.Message}";

                                    _ = Dispatcher.BeginInvoke(new Action(() =>
                                    {
                                        string pageName = !string.IsNullOrEmpty(directUrl) ? Path.GetFileNameWithoutExtension(directUrl.Split('?')[0]) : pageNum.ToString();
                                        queueItem.AddError(string.Empty, pageNum, traceMessage, directUrl ?? pageUrl, item.Link, pageName);
                                        RecordCheckError(item.SourceDomain ?? "nhentai.net", item.Name, string.Empty, pageNum, traceMessage, directUrl ?? pageUrl, pageName);
                                    }));
                                }
                            }

                            lock (lockObj)
                            {
                                completedPages++;
                                pageWatch.Stop();
                                long downloadedBytes = !string.IsNullOrWhiteSpace(downloadedPath) && File.Exists(downloadedPath) ? new FileInfo(downloadedPath).Length : 0;
                                UpdateDownloadRowMetrics(queueItem, completedPages, totalPages, $"{completedPages}/{totalPages} pages", downloadedBytes, pageWatch.ElapsedMilliseconds);
                                    WriteTempProgressLog(tempFolder, item, "Downloading", completedPages, totalPages, $"{completedPages}/{totalPages} pages", $"Page {pageNum} completed");
                                if (completedPages % 5 == 0 || completedPages == totalPages)
                                {
                                    Dispatcher.BeginInvoke(new Action(() =>
                                    {
                                        lblStatus.Text = $"[{completedPages}/{totalPages}] Tải {safeTitle} ({nhentaiSiteKey})";
                                    }));
                                }
                            }
                            }
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, token));
                }

                await Task.WhenAll(tasks);

                WriteTempProgressLog(tempFolder, item, "Done", totalPages, totalPages, $"{totalPages}/{totalPages} pages", "Download completed");
                MoveTempFolderToTarget(tempFolder, targetFolder, "nhentai");
                ValidateDownloadedFiles(targetFolder, totalPages, queueItem, string.Empty);
            }
        }

        private async Task<string[]> ExtractNhentaiNetImageUrlsAsync(GalleryItem item, string bookUrl, CancellationToken token)
        {
            try
            {
                string html = null;
                try
                {
                    html = await FetchStringAsync(bookUrl, token);
                }
                catch (Exception fetchEx)
                {
                    if (fetchEx.Message.Contains("403") || fetchEx.Message.Contains("503") || fetchEx.Message.Contains("Forbidden"))
                    {
                        bool ok = await SolveNhentaiCaptchaIfNeededAsync(bookUrl);
                        if (!ok)
                        {
                            Log($"[nhentai.net] ExtractNhentaiNetImageUrlsAsync: Cloudflare block — fallback về reader page fetch");
                            return null;
                        }
                        html = await FetchStringAsync(bookUrl, token);
                    }
                    else
                    {
                        throw;
                    }
                }

                if (string.IsNullOrWhiteSpace(html)) return null;

                // Unescape JSON backslash escapes embedded in SvelteKit script
                string unescaped = html.Replace("\\\"", "\"").Replace("\\/", "/");

                // Extract mediaId from gallery CDN thumbnail (e.g. t1.nhentai.net/galleries/4089909/...)
                var mediaIdMatch = Regex.Match(unescaped,
                    @"[it]\d*\.nhentai\.net/galleries/(\d+)/",
                    RegexOptions.IgnoreCase);
                if (!mediaIdMatch.Success)
                {
                    Log($"[nhentai.net] Không tìm thấy mediaId trong HTML — fallback về reader page fetch");
                    return null;
                }
                string mediaId = mediaIdMatch.Groups[1].Value;

                // Derive image subdomain: t1→i1, t2→i2... If "i" subdomain exists, use directly
                string subdomain = "i1";
                var iSubMatch = Regex.Match(unescaped,
                    @"(i\d+)\.nhentai\.net/galleries/" + mediaId,
                    RegexOptions.IgnoreCase);
                if (iSubMatch.Success)
                {
                    subdomain = iSubMatch.Groups[1].Value.ToLowerInvariant();
                }
                else
                {
                    var tSubMatch = Regex.Match(unescaped,
                        @"(t\d+)\.nhentai\.net/galleries/" + mediaId,
                        RegexOptions.IgnoreCase);
                    if (tSubMatch.Success)
                    {
                        string tSub = tSubMatch.Groups[1].Value.ToLowerInvariant();
                        subdomain = "i" + tSub.Substring(1); // t1→i1, t2→i2
                    }
                }

                // Extract total pages
                int totalPages = 0;
                var numPagesMatch = Regex.Match(unescaped, @"""num_pages""\s*:\s*(\d+)", RegexOptions.IgnoreCase);
                if (numPagesMatch.Success && int.TryParse(numPagesMatch.Groups[1].Value, out int parsedPages))
                {
                    totalPages = parsedPages;
                }
                else
                {
                    // Fallback parse pages from href pattern
                    var hrefPagesMatch = Regex.Match(unescaped, @"href=""[^""]*pages(?:%3A|:)(\d+)""", RegexOptions.IgnoreCase);
                    if (hrefPagesMatch.Success && int.TryParse(hrefPagesMatch.Groups[1].Value, out int parsedHrefPages))
                    {
                        totalPages = parsedHrefPages;
                    }
                }

                if (totalPages <= 0)
                {
                    Log($"[nhentai.net] Không tìm thấy total pages trong HTML — fallback về reader page fetch");
                    return null;
                }

                // Extract pages array to detect specific extensions if possible
                var pagesMatch = Regex.Match(unescaped,
                    @"""pages""\s*:\s*(\[.*?\])",
                    RegexOptions.Singleline);
                
                string[] urls = new string[totalPages];
                if (pagesMatch.Success)
                {
                    string pagesJson = pagesMatch.Groups[1].Value;
                    var typeMatches = Regex.Matches(pagesJson, @"\\?""t\\?""\s*:\s*\\?""([a-z]+)\\?""", RegexOptions.IgnoreCase);
                    if (typeMatches.Count >= totalPages)
                    {
                        for (int i = 0; i < totalPages; i++)
                        {
                            string typeChar = typeMatches[i].Groups[1].Value.ToLowerInvariant();
                            string ext = (typeChar == "g" || typeChar == "gif") ? "gif"
                                       : (typeChar == "p" || typeChar == "png") ? "png"
                                       : (typeChar == "w" || typeChar == "webp") ? "webp"
                                       : (typeChar == "b" || typeChar == "bmp") ? "bmp"
                                       : (typeChar == "jpeg") ? "jpeg"
                                       : "jpg";
                            // Phân bổ luân phiên giữa i1, i2, i3, i4 để tăng gấp 4 lần băng thông CDN
                            string activeSubdomain = $"i{(i % 4) + 1}";
                            urls[i] = $"https://{activeSubdomain}.nhentai.net/galleries/{mediaId}/{i + 1}.{ext}";
                        }
                        Log($"[nhentai.net] Đã parse thành công {urls.Length} image URLs với luân phiên CDN mirrors (i1..i4).");
                        return urls;
                    }
                }

                // Nếu không parse được JSON pages chi tiết, mặc định tạo link webp (hàm download sẽ tự động fallback sang jpg/png nếu lỗi 404)
                for (int i = 0; i < totalPages; i++)
                {
                    string activeSubdomain = $"i{(i % 4) + 1}";
                    urls[i] = $"https://{activeSubdomain}.nhentai.net/galleries/{mediaId}/{i + 1}.webp";
                }
                Log($"[nhentai.net] Đã generate {urls.Length} image URLs với luân phiên CDN mirrors (i1..i4).");
                return urls;
            }
            catch (Exception ex)
            {
                Log($"[nhentai.net] ExtractNhentaiNetImageUrlsAsync thất bại: {ex.Message} — fallback về reader page fetch");
                return null;
            }
        }

        private string NormalizeNhentaiBookUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return url;
            }

            var galleryMatch = Regex.Match(url, @"https?://(nhentai\.(?:net|xxx))/g/(?<bookId>\d+)/?", RegexOptions.IgnoreCase);
            if (galleryMatch.Success)
            {
                string domain = galleryMatch.Groups[1].Value.ToLowerInvariant();
                return $"https://{domain}/g/{galleryMatch.Groups["bookId"].Value}/";
            }

            return url.Trim();
        }

        private async Task<int> GetNhentaiTotalPagesFromBookAsync(string bookUrl, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
                string html = await FetchStringAsync(bookUrl, token);

            var patterns = new[]
            {
                // nhentai.net SvelteKit: escaped JSON \"num_pages\":16
                Regex.Match(html, @"\\""num_pages\\""\s*:\s*(\d+)", RegexOptions.IgnoreCase),
                // nhentai.net SvelteKit: unescaped JSON "num_pages":16
                Regex.Match(html, @"""num_pages""\s*:\s*(\d+)", RegexOptions.IgnoreCase),
                // nhentai.net HTML: Pages: <span...>16</span>
                Regex.Match(html, @"Pages:\s*<[^>]*>\s*<[^>]*>\s*<[^>]*[^>]*>\s*<[^>]*>\s*(\d+)\s*<", RegexOptions.IgnoreCase | RegexOptions.Singleline),
                // nhentai.net: href="/search/?q=pages%3A{N}" or href="...pages:N"
                Regex.Match(html, @"href=""[^""]*pages(?:%3A|:)(\d+)""", RegexOptions.IgnoreCase),
                // nhentai.xxx: "N pages"
                Regex.Match(html, @"(\d+)\s+pages", RegexOptions.IgnoreCase),
                // nhentai.xxx: Pages: class="value">N
                Regex.Match(html, @"Pages:.*?class=""value""[^>]*>(\d+)", RegexOptions.IgnoreCase | RegexOptions.Singleline),
                // nhentai.xxx: <span class="num-pages">N</span>
                Regex.Match(html, @"<span[^>]*class=""num-pages""[^>]*>\s*(\d+)\s*</span>", RegexOptions.IgnoreCase),
                // nhentai.xxx: id="load_pages" value="N"
                Regex.Match(html, @"id=""load_pages""\s+value=""(\d+)""", RegexOptions.IgnoreCase),
                // nhentai.xxx: <span class="tag_name pages">N</span>
                Regex.Match(html, @"<span[^>]*class=""tag_name\s+pages""[^>]*>\s*(\d+)\s*</span>", RegexOptions.IgnoreCase)
            };

            foreach (var match in patterns)
            {
                if (match.Success && int.TryParse(match.Groups[1].Value, out int totalPages) && totalPages > 0)
                {
                    return totalPages;
                }
            }

            return 0;
        }



        private sealed class NhentaiReaderImageInfo
        {
            public string Subdomain { get; set; }
            public string MediaId { get; set; }
            public string Extension { get; set; }
        }

        private string BuildNhentaiReaderPageReferer(string galleryUrl, int pageNum)
        {
            if (string.IsNullOrWhiteSpace(galleryUrl))
            {
                return "https://nhentai.net/";
            }

            string cleanGalleryUrl = galleryUrl.Trim();
            var cdnMatch = Regex.Match(
                cleanGalleryUrl,
                @"(?:https?:)?//[it]\d*\.nhentai\.net/galleries/\d+/\d+(?:t)?\.(?:jpg|png|gif|webp|jpeg|bmp)",
                RegexOptions.IgnoreCase);
            if (cdnMatch.Success)
            {
                return "https://nhentai.net/";
            }

            cleanGalleryUrl = cleanGalleryUrl.TrimEnd('/');
            return $"{cleanGalleryUrl}/{Math.Max(1, pageNum)}/";
        }

        private NhentaiReaderImageInfo TryExtractNhentaiGalleryInfoFromBookHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return null;
            }

            html = GetSafeChapterHtml(html);

            html = html.Replace("\\/", "/");

            string[] patterns =
            {
                @"(?<imgUrl>(?:https?:)?//(?<subdomain>[it]\d*)\.nhentai\.net/galleries/(?<mediaId>\d+)/1t?\.(?<ext>jpg|png|gif|webp|jpeg|bmp))",
                @"(?<imgUrl>(?:https?:)?//(?<subdomain>[it]\d*)\.nhentai\.net/galleries/(?<mediaId>\d+)/(?<pageNum>\d+)t?\.(?<ext>jpg|png|gif|webp|jpeg|bmp))"
            };

            foreach (string pattern in patterns)
            {
                var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
                if (!match.Success)
                {
                    continue;
                }

                string subdomain = match.Groups["subdomain"].Value;
                string mediaId = match.Groups["mediaId"].Value;
                string extension = match.Groups["ext"].Value;
                if (!string.IsNullOrWhiteSpace(mediaId))
                {
                    return new NhentaiReaderImageInfo
                    {
                        Subdomain = subdomain,
                        MediaId = mediaId,
                        Extension = extension
                    };
                }
            }

            return null;
        }

        private string NormalizeNhentaiImageSubdomain(string subdomain)
        {
            if (string.IsNullOrWhiteSpace(subdomain))
            {
                return null;
            }

            string trimmed = subdomain.Trim().ToLowerInvariant();
            if (trimmed.StartsWith("t", StringComparison.Ordinal))
            {
                return trimmed.Length > 1 ? "i" + trimmed.Substring(1) : "i";
            }

            return trimmed;
        }

        private async Task<NhentaiReaderImageInfo> GetNhentaiReaderImageInfoAsync(string readerPageUrl)
        {
            if (string.IsNullOrWhiteSpace(readerPageUrl))
            {
                return null;
            }

            string html = null;
            try
            {
                html = await FetchStringAsync(readerPageUrl, _downloadCts?.Token ?? CancellationToken.None);
            }
            catch (HttpRequestException)
            {
                bool ok = await SolveNhentaiCaptchaIfNeededAsync(readerPageUrl);
                if (!ok)
                {
                    return null;
                }

                html = await FetchStringAsync(readerPageUrl, _downloadCts?.Token ?? CancellationToken.None);
            }

            if (string.IsNullOrWhiteSpace(html))
            {
                return null;
            }

            html = html.Replace("\\/", "/");

            var patterns = new[]
            {
                @"(?<imgUrl>(?:https?:)?//(?<subdomain>i\d*)\.nhentai\.net/galleries/(?<mediaId>\d+)/(?<pageNum>\d+)\.(?<ext>jpg|png|gif|webp|jpeg|bmp))"
            };

            foreach (string pattern in patterns)
            {
                var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
                if (!match.Success)
                {
                    continue;
                }

                string subdomain = match.Groups["subdomain"].Value;
                string mediaId = match.Groups["mediaId"].Value;
                string extension = match.Groups["ext"].Value;
                if (!string.IsNullOrWhiteSpace(subdomain) &&
                    !string.IsNullOrWhiteSpace(mediaId) &&
                    !string.IsNullOrWhiteSpace(extension))
                {
                    return new NhentaiReaderImageInfo
                    {
                        Subdomain = subdomain,
                        MediaId = mediaId,
                        Extension = extension
                    };
                }
            }

            return null;
        }

        private async Task<string> ResolveNhentaiReaderImageUrlAsync(string readerPageUrl, int pageNum, CancellationToken token)
        {
            while (_isDownloadPaused)
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(200, token);
            }
            token.ThrowIfCancellationRequested();

            string html = null;
            try
            {
                html = await FetchStringAsync(readerPageUrl, token);
            }
            catch (HttpRequestException)
            {
                bool ok = await SolveNhentaiCaptchaIfNeededAsync(readerPageUrl);
                if (!ok)
                {
                    throw new Exception($"Không thể vượt qua Cloudflare ở trang đọc {pageNum}. Reader: {readerPageUrl}");
                }

                html = await FetchStringAsync(readerPageUrl, token);
            }

            if (string.IsNullOrWhiteSpace(html))
            {
                throw new Exception($"Trang đọc nhentai {pageNum} rỗng. Reader: {readerPageUrl}");
            }

            html = html.Replace("\\/", "/");

            string[] patterns =
            {
                @"(?:src|data-src)=[""'](?<imgUrl>(?:https?:)?//i\d*\.nhentai\.net/galleries/\d+/" + pageNum + @"\.(?:jpg|png|gif|webp|jpeg|bmp)[^""']*)[""']",
                @"(?<imgUrl>(?:https?:)?//i\d*\.nhentai\.net/galleries/\d+/" + pageNum + @"\.(?:jpg|png|gif|webp|jpeg|bmp))",
                @"<(?:section|div)\s+[^>]*?(?:id|class)=[""']image-container[""'][^>]*>.*?<img\s+[^>]*?(?:src|data-src)=[""'](?<imgUrl>[^""']+)[""']",
                @"window\._gallery\s*=\s*(?<json>\{.*?\});"
            };

            foreach (string pattern in patterns)
            {
                var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (!match.Success)
                {
                    continue;
                }

                string imgUrl = match.Groups["imgUrl"].Value;
                if (string.IsNullOrWhiteSpace(imgUrl))
                {
                    continue;
                }

                if (imgUrl.StartsWith("//", StringComparison.Ordinal))
                {
                    imgUrl = "https:" + imgUrl;
                }

                if (imgUrl.IndexOf(".nhentai.net/galleries/", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return imgUrl;
                }
            }

            throw new Exception($"Không thể trích xuất direct image URL ở trang đọc nhentai {pageNum}. Reader: {readerPageUrl}");
        }

        private static string ExtractNhentaiDirectImageUrl(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var match = Regex.Match(
                text,
                @"https?://i\d*\.nhentai\.net/galleries/\d+/\d+\.(?:jpg|png|gif|webp|jpeg|bmp)(?:\?[^\\s\""]*)?",
                RegexOptions.IgnoreCase);

            return match.Success ? match.Value : null;
        }

        private async Task<int> ProbeTotalPagesAsync(string mediaId, string defaultExt, CancellationToken token, string preferredSubdomain = null, Func<int, string> refererFactory = null)
        {
            Log($"[nhentai] Đang dò tìm tổng số trang cho media ID {mediaId}...");
            
            // Check if page 1 exists
            string p1Ext = await FindValidExtensionAsync(mediaId, 1, defaultExt, preferredSubdomain, refererFactory?.Invoke(1));
            if (p1Ext == null)
            {
                Log($"[nhentai] Lỗi: Không thể tìm thấy trang 1 cho media ID {mediaId}");
                return 0;
            }

            int low = 1;
            int high = 1000;
            int detectedPages = 1;

            while (low <= high)
            {
                token.ThrowIfCancellationRequested();
                int mid = (low + high) / 2;
                string ext = await FindValidExtensionAsync(mediaId, mid, defaultExt, preferredSubdomain, refererFactory?.Invoke(mid));
                
                if (ext != null)
                {
                    detectedPages = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            Log($"[nhentai] Đã dò tìm xong. Tổng số trang phát hiện: {detectedPages}");
            return detectedPages;
        }

        private async Task<string> FindValidExtensionAsync(string mediaId, int pageNum, string defaultExt, string preferredSubdomain = null, string referer = null)
        {
            string[] extensions = { "jpg", "png", "webp", "gif", "jpeg", "bmp" };

            foreach (string subdomain in BuildNhentaiImageSubdomainCandidates(preferredSubdomain))
            {
                string url = $"https://{subdomain}.nhentai.net/galleries/{mediaId}/{pageNum}.{defaultExt}";
                if (await CheckPageExistsAsync(url, referer))
                {
                    return defaultExt;
                }

                foreach (var ext in extensions)
                {
                    if (string.Equals(ext, defaultExt, StringComparison.OrdinalIgnoreCase)) continue;
                    url = $"https://{subdomain}.nhentai.net/galleries/{mediaId}/{pageNum}.{ext}";
                    if (await CheckPageExistsAsync(url, referer))
                    {
                        return ext;
                    }
                }
            }

            return null;
        }

        private string[] BuildNhentaiImageSubdomainCandidates(string preferredSubdomain = null)
        {
            var candidates = new List<string>();

            if (!string.IsNullOrWhiteSpace(preferredSubdomain))
            {
                candidates.Add(preferredSubdomain);
            }

            for (int i = 1; i <= 9; i++)
            {
                string candidate = "i" + i;
                if (!candidates.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                {
                    candidates.Add(candidate);
                }
            }

            if (!candidates.Contains("i", StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add("i");
            }

            return candidates.ToArray();
        }

        private async Task<bool> CheckPageExistsAsync(string url, string referer = null)
        {
            try
            {
                using (var httpClient = CreateScopedHttpClient(url))
                using (var request = new HttpRequestMessage(HttpMethod.Head, url))
                {
                    request.Headers.Referrer = new Uri(string.IsNullOrWhiteSpace(referer) ? "https://nhentai.net/" : referer);
                    using (var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                    {
                        if (response.IsSuccessStatusCode) return true;
                    }
                }
                
                // Fallback to GET if HEAD method is not allowed or fails
                using (var httpClient = CreateScopedHttpClient(url))
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.Referrer = new Uri(string.IsNullOrWhiteSpace(referer) ? "https://nhentai.net/" : referer);
                    using (var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                    {
                        return response.IsSuccessStatusCode;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private async Task DownloadNhentaiPageSlowPathAsync(string galleryUrl, int pageNum, string targetPath, CancellationToken token)
        {
            // Respect pause check
            while (_isDownloadPaused)
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(200, token);
            }
            token.ThrowIfCancellationRequested();

            // Reader URL format is galleryUrl/pageNum
            string cleanGalleryUrl = galleryUrl.TrimEnd('/');
            string pageUrl = $"{cleanGalleryUrl}/{pageNum}";
            
            string html = await FetchStringAsync(pageUrl, token);

            // Match image URL on the reader page (quote-independent)
            string imgUrl = null;
            var imgMatch = Regex.Match(html, @"(?<imgUrl>(?:https?:)?//(?<subdomain>i\d*)\.nhentai\.net/galleries/(?<galleryId>\d+)/" + pageNum + @"\.(?<ext>jpg|png|gif|webp|jpeg|bmp))", RegexOptions.IgnoreCase);
            if (imgMatch.Success)
            {
                imgUrl = imgMatch.Groups["imgUrl"].Value;
            }
            else
            {
                // Fallback: search inside section/div with class/id image-container
                var fallbackMatch = Regex.Match(html, @"<(?:section|div)\s+[^>]*?(?:id|class)=[""']image-container[""'][^>]*>.*?<img\s+[^>]*?(?:src|data-src)=[""'](?<imgUrl>[^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (fallbackMatch.Success)
                {
                    imgUrl = fallbackMatch.Groups["imgUrl"].Value;
                }
                else
                {
                    // General fallback for any image in galleries directory
                    var generalMatch = Regex.Match(html, @"(?:src|data-src)=[""'](?<imgUrl>(?:https?:)?//i\d*\.nhentai\.net/galleries/[^""']+)[""']", RegexOptions.IgnoreCase);
                    if (generalMatch.Success)
                    {
                        imgUrl = generalMatch.Groups["imgUrl"].Value;
                    }
                }
            }

            if (!string.IsNullOrEmpty(imgUrl))
            {
                if (imgUrl.StartsWith("//"))
                {
                    imgUrl = "https:" + imgUrl;
                }

                // Adjust file extension based on actual source URL
                string actualExt = GetSafeImageExtensionFromUrl(imgUrl);
                string finalPath = targetPath;
                if (!string.IsNullOrEmpty(actualExt) && !targetPath.EndsWith(actualExt, StringComparison.OrdinalIgnoreCase))
                {
                    finalPath = Path.ChangeExtension(targetPath, actualExt);
                }

                // Respect pause check
                while (_isDownloadPaused)
                {
                    token.ThrowIfCancellationRequested();
                    await Task.Delay(200, token);
                }
                token.ThrowIfCancellationRequested();

                await DownloadUrlToFileWithRefererAsync(imgUrl, pageUrl, finalPath, token);
            }
            else
            {
                throw new Exception($"Không thể trích xuất địa chỉ ảnh từ trang đọc nhentai {pageNum}");
            }
        }

        internal async Task<bool> CheckIfNhentaiBlockedAsync(string testUrl)
        {
            try
            {
                using (var httpClient = CreateScopedHttpClient(testUrl))
                using (var request = new HttpRequestMessage(HttpMethod.Get, testUrl))
                {
                    if (!string.IsNullOrWhiteSpace(testUrl))
                    {
                        if (testUrl.IndexOf("nhentai.net", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            request.Headers.Referrer = new Uri("https://nhentai.net/");
                        }

                    }
                    using (var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                    {
                        if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.ServiceUnavailable)
                        {
                            return true; // Cloudflare blocked (403/503)
                        }

                        // Also read a snippet of the page to check for challenge
                        using (var content = response.Content)
                        {
                            string html = await content.ReadAsStringAsync();
                            if (html.Contains("cf-challenge") || 
                                html.Contains("cf-turnstile") || 
                                html.Contains("Turnstile") || 
                                html.Contains("Just a moment..."))
                            {
                                return true;
                            }
                        }
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("403") || ex.Message.Contains("503"))
                {
                    return true;
                }
                return false;
            }
        }

        internal async Task<bool> SolveNhentaiCaptchaIfNeededAsync(string testUrl)
        {
            bool isListUrl = testUrl.Contains("/tag/") || testUrl.Contains("/artist/") || testUrl.Contains("/parody/") || testUrl.Contains("/group/") || testUrl.Contains("/character/") || testUrl.Contains("/search/") || testUrl.Contains("?q=");
            if (!isListUrl && IsCaptchaCooldownActive(testUrl)) return true;

            bool isBlocked = await CheckIfNhentaiBlockedAsync(testUrl);
            if (!isBlocked)
            {
                return true; // Not blocked, all good!
            }

            if (_isCaptchaWindowActive)
            {
                while (_isCaptchaWindowActive)
                {
                    await Task.Delay(500);
                }
                isBlocked = await CheckIfNhentaiBlockedAsync(testUrl);
                if (!isBlocked)
                {
                    return true;
                }
            }

            await _captchaSemaphore.WaitAsync();
            try
            {
                // Re-check after lock
                isBlocked = await CheckIfNhentaiBlockedAsync(testUrl);
                if (!isBlocked)
                {
                    return true;
                }

                _isCaptchaWindowActive = true;
                _isDownloadPaused = true;
                Log("[nhentai.net] Phát hiện thử thách Cloudflare / Captcha. Tạm dừng tải và đang mở trình duyệt giải tự động...");

                bool solved = false;
                try
                {
                    await await Dispatcher.InvokeAsync(async () =>
                    {
                        // Nếu là URL danh sách (tag, artist, parody, group, character, search) thì luôn dùng headlessAutomation = true để cào ngầm giống tag
                        bool useHeadless = isListUrl ? true : _lightNovelAutoFocusEnabled;

                        var captchaWin = CreateCaptchaWindow(testUrl, autoDeleteCookiesOnLoad: false, headlessAutomation: useHeadless);
                        captchaWin.Owner = this;

                        if (await ShowCaptchaWindowWithFocusHandlingAsync(captchaWin, useNovelFocusStealth: useHeadless))
                        {
                            var originalUri = new Uri(testUrl);
                            var resolvedUri = captchaWin.ResolvedUri ?? originalUri;

                            MergeCookiesIntoScopedContainer(resolvedUri.AbsoluteUri, resolvedUri, captchaWin.ResolvedCookies.GetCookies(resolvedUri).Cast<Cookie>());

                            if (originalUri.Host != resolvedUri.Host)
                            {
                                MergeCookiesIntoScopedContainer(originalUri.AbsoluteUri, originalUri, captchaWin.ResolvedCookies.GetCookies(originalUri).Cast<Cookie>());
                            }

                            if (!string.IsNullOrEmpty(captchaWin.UserAgent))
                            {
                                RememberScopedUserAgent(originalUri.AbsoluteUri, captchaWin.UserAgent);
                                RememberScopedUserAgent(resolvedUri.AbsoluteUri, captchaWin.UserAgent);
                            }

                            _lastNhentaiResolvedHtml = captchaWin.ResolvedHtml;
                            _lastNhentaiResolvedUrl = testUrl;
                            solved = true;
                        }

                        // Lần 2 (Self-Healing): Nếu giải lần 1 với session cũ thất bại, tự động xóa folder cookie và giải lại sạch từ đầu
                        if (!solved)
                        {
                            Log("[nhentai.net] Lần 1 giải captcha thất bại (có thể do session cũ bị block). Tiến hành xóa folder cookie nhentai và giải lại sạch...");
                            string captchaPath = System.IO.Path.Combine(PortablePaths.WebView2CaptchaUserDataFolder, "nhentai.net");
                            try
                            {
                                if (System.IO.Directory.Exists(captchaPath))
                                {
                                    System.IO.Directory.Delete(captchaPath, true);
                                    Log("[nhentai.net] Đã tự động xóa folder cookie nhentai.net");
                                }
                            }
                            catch (Exception ex)
                            {
                                Log($"[nhentai.net] Không thể tự động xóa folder cookie: {ex.Message}");
                            }

                            // Khởi tạo cửa sổ captcha sạch
                            var captchaWinClean = CreateCaptchaWindow(testUrl, autoDeleteCookiesOnLoad: true, headlessAutomation: useHeadless);
                            captchaWinClean.Owner = this;

                            if (await ShowCaptchaWindowWithFocusHandlingAsync(captchaWinClean, useNovelFocusStealth: useHeadless))
                            {
                                var originalUri = new Uri(testUrl);
                                var resolvedUri = captchaWinClean.ResolvedUri ?? originalUri;

                                MergeCookiesIntoScopedContainer(resolvedUri.AbsoluteUri, resolvedUri, captchaWinClean.ResolvedCookies.GetCookies(resolvedUri).Cast<Cookie>());

                                if (originalUri.Host != resolvedUri.Host)
                                {
                                    MergeCookiesIntoScopedContainer(originalUri.AbsoluteUri, originalUri, captchaWinClean.ResolvedCookies.GetCookies(originalUri).Cast<Cookie>());
                                }

                                if (!string.IsNullOrEmpty(captchaWinClean.UserAgent))
                                {
                                    RememberScopedUserAgent(originalUri.AbsoluteUri, captchaWinClean.UserAgent);
                                    RememberScopedUserAgent(resolvedUri.AbsoluteUri, captchaWinClean.UserAgent);
                                }

                                _lastNhentaiResolvedHtml = captchaWinClean.ResolvedHtml;
                                _lastNhentaiResolvedUrl = testUrl;
                                solved = true;
                            }
                        }
                    });
                }
                finally
                {
                    _isCaptchaWindowActive = false;
                }

                if (solved)
                {
                    MarkCaptchaSolved(testUrl);
                    Log("[nhentai.net] Giải captcha thành công. Tiếp tục tải...");
                    _isDownloadPaused = false;
                    return true;
                }
                return false;
            }
            finally
            {
                _captchaSemaphore.Release();
            }
        }

        internal bool IsViHentaiCaptchaChallengeHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return false;
            }

            return html.Contains("cf-challenge") ||
                   html.Contains("cf-turnstile") ||
                   html.Contains("Turnstile") ||
                   html.Contains("Just a moment...") ||
                   html.Contains("Performing security verification") ||
                   html.Contains("thực hiện xác minh bảo mật") ||
                   html.Contains("xác minh bạn không phải là bot");
        }

        internal async Task<bool> CheckIfViHentaiBlockedAsync(string testUrl)
        {
            try
            {
                using (var httpClient = CreateScopedHttpClient(testUrl))
                using (var request = new HttpRequestMessage(HttpMethod.Get, testUrl))
                {
                    using (var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                    {
                        if (response.StatusCode == HttpStatusCode.Forbidden || 
                            response.StatusCode == HttpStatusCode.ServiceUnavailable)
                        {
                            return true; // Cloudflare blocked
                        }

                        using (var content = response.Content)
                        {
                            string html = await content.ReadAsStringAsync();
                            if (IsViHentaiCaptchaChallengeHtml(html))
                            {
                                return true;
                            }
                        }
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("403") || ex.Message.Contains("503"))
                {
                    return true;
                }
                return false;
            }
        }

        internal async Task<bool> SolveViHentaiCaptchaIfNeededAsync(string testUrl)
        {
            if (IsCaptchaCooldownActive(testUrl)) return true;

            bool isBlocked = await CheckIfViHentaiBlockedAsync(testUrl);
            if (!isBlocked)
            {
                return true; // Not blocked
            }

            if (_isCaptchaWindowActive)
            {
                while (_isCaptchaWindowActive)
                {
                    await Task.Delay(500);
                }
                isBlocked = await CheckIfViHentaiBlockedAsync(testUrl);
                if (!isBlocked)
                {
                    return true;
                }
            }

            await _captchaSemaphore.WaitAsync();
            try
            {
                // Re-check after lock
                isBlocked = await CheckIfViHentaiBlockedAsync(testUrl);
                if (!isBlocked)
                {
                    return true;
                }

                _isCaptchaWindowActive = true;
                _isDownloadPaused = true;
                Log("[vi-hentai.pro] Phát hiện thử thách Cloudflare / Captcha. Tạm dừng tải và đang mở trình duyệt giải tự động...");

                bool solved = false;
                try
                {
                    await await Dispatcher.InvokeAsync(async () =>
                    {
                        var captchaWin = CreateCaptchaWindow(testUrl, autoDeleteCookiesOnLoad: true, headlessAutomation: _lightNovelAutoFocusEnabled);
                        captchaWin.Owner = this;

                        if (await ShowCaptchaWindowWithFocusHandlingAsync(captchaWin, useNovelFocusStealth: _lightNovelAutoFocusEnabled))
                        {
                            var uri = new Uri("https://vi-hentai.pro");
                            MergeCookiesIntoScopedContainer(uri.AbsoluteUri, uri, captchaWin.ResolvedCookies.GetCookies(uri).Cast<Cookie>());

                            if (!string.IsNullOrEmpty(captchaWin.UserAgent))
                            {
                                RememberScopedUserAgent(uri.AbsoluteUri, captchaWin.UserAgent);
                            }
                            solved = true;
                            if (!captchaWin.BypassWasNeeded && captchaWin.WindowElapsedSeconds < 2.0)
                            {
                                Log("[vi-hentai.pro] CaptchaWindow đóng nhanh dưới 2 giây. Xem như không có captcha thật.");
                            }
                        }
                    });
                }
                finally
                {
                    _isCaptchaWindowActive = false;
                }

                if (solved)
                {
                    MarkCaptchaSolved(testUrl);
                    Log("[vi-hentai.pro] Xác nhận captcha xong. Tiếp tục tải...");
                    _isDownloadPaused = false;
                    return true;
                }
                return false;
            }
            finally
            {
                _captchaSemaphore.Release();
            }
        }

        private string GetSafePathName(string name, int maxLength = 100)
        {
            if (string.IsNullOrEmpty(name)) return "Unnamed";
            
            string processedName = name.Replace('[', '［').Replace(']', '］');
            
            var invalid = Path.GetInvalidFileNameChars().Concat(Path.GetInvalidPathChars()).Distinct();
            string safeName = Regex.Replace(processedName, @"\s*[:：]\s*", " - ");
            foreach (var c in invalid)
            {
                safeName = safeName.Replace(c, '-');
            }

            // ponytail: only keep safe folder chars; collapse repeat separators before trim.
            safeName = Regex.Replace(safeName, @"-+", "-");
            safeName = Regex.Replace(safeName, @"\s+", " ");
            safeName = safeName.Trim().TrimEnd('.', '-');

            if (maxLength > 8 && safeName.Length > maxLength)
            {
                safeName = safeName.Substring(0, maxLength).TrimEnd(' ', '.', '-');
            }

            return string.IsNullOrWhiteSpace(safeName) ? "Unnamed" : safeName;
        }

        private int GetCurrentConnectionLimit()
        {
            if (Dispatcher.CheckAccess())
            {
                int selected = GetComboBoxSelectedInt(cmbConnections, 4);
                _cachedConnectionLimit = selected;
                return selected;
            }
            else
            {
                int selected = 4;
                Dispatcher.Invoke(() =>
                {
                    selected = GetComboBoxSelectedInt(cmbConnections, 4);
                });
                _cachedConnectionLimit = selected;
                return selected;
            }
        }

        private int GetCurrentMultiDownloadLimit()
        {
            if (Dispatcher.CheckAccess())
            {
                int selectedMulti = GetComboBoxSelectedInt(cmbMultiDownload, 2);
                int selectedConn = GetComboBoxSelectedInt(cmbConnections, 4);
                int limit;
                if (selectedConn == 16)
                {
                    limit = Math.Max(selectedMulti, 4);
                }
                else if (selectedConn == 32)
                {
                    limit = Math.Max(selectedMulti, 8);
                }
                else
                {
                    limit = selectedMulti;
                }
                _cachedMultiDownloadLimit = limit;
                return limit;
            }
            else
            {
                int limit = 2;
                Dispatcher.Invoke(() =>
                {
                    int selectedMulti = GetComboBoxSelectedInt(cmbMultiDownload, 2);
                    int selectedConn = GetComboBoxSelectedInt(cmbConnections, 4);
                    if (selectedConn == 16)
                    {
                        limit = Math.Max(selectedMulti, 4);
                    }
                    else if (selectedConn == 32)
                    {
                        limit = Math.Max(selectedMulti, 8);
                    }
                    else
                    {
                        limit = selectedMulti;
                    }
                });
                _cachedMultiDownloadLimit = limit;
                return limit;
            }
        }

        private int GetBookConnectionLimit(GalleryItem item)
        {
            return GetCurrentConnectionLimit();
        }

        private string GetBookIdentifier(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;
            try
            {
                var uri = new Uri(url);
                string host = uri.Host.ToLower();
                string path = uri.AbsolutePath;

                if (host.Contains("vi-hentai.pro"))
                {
                    var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    if (segments.Length >= 2 && segments[0].Equals("truyen", StringComparison.OrdinalIgnoreCase))
                    {
                        return "vi-hentai.pro|" + segments[1].ToLower();
                    }
                }

                if (host.Contains("damconuong.shop"))
                {
                    var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    if (segments.Length >= 2 && segments[0].Equals("truyen", StringComparison.OrdinalIgnoreCase))
                    {
                        return "damconuong.shop|" + segments[1].ToLowerInvariant();
                    }
                }

                if (host.Contains("nhentai.net"))
                {
                    var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    if (segments.Length >= 2 && segments[0].Equals("g", StringComparison.OrdinalIgnoreCase))
                    {
                        return "nhentai.net|" + segments[1].ToLowerInvariant();
                    }
                }

                if (host.Contains("truyenqq"))
                {
                    var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    if (segments.Length >= 2 && segments[0].Equals("truyen-tranh", StringComparison.OrdinalIgnoreCase))
                    {
                        string rawSlug = segments[1].ToLower();
                        int idx = rawSlug.IndexOf("-chap", StringComparison.OrdinalIgnoreCase);
                        if (idx != -1)
                        {
                            rawSlug = rawSlug.Substring(0, idx);
                        }
                        return "truyenqq|" + rawSlug;
                    }
                }

                if (host.Contains("sayhentai.cx"))
                {
                    var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    if (segments.Length >= 1)
                    {
                        string rawSlug = Path.GetFileNameWithoutExtension(segments[0]).ToLowerInvariant();
                        return "sayhentai|" + rawSlug;
                    }
                }

                if (host.Contains("truyenggvn.com"))
                {
                    var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    if (segments.Length >= 2 && segments[0].Equals("truyen-tranh", StringComparison.OrdinalIgnoreCase))
                    {
                        string rawSlug = segments[1].ToLowerInvariant();
                        int chapIdx = rawSlug.IndexOf("-chap-", StringComparison.OrdinalIgnoreCase);
                        if (chapIdx >= 0)
                        {
                            rawSlug = rawSlug.Substring(0, chapIdx);
                        }
                        return "truyenggvn|" + rawSlug;
                    }
                }

                if (host.Contains("truyenvua.com"))
                {
                    var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    if (segments.Length >= 2)
                    {
                        return "truyenggvn|" + segments[0].ToLowerInvariant();
                    }
                }

                if (host.Contains("daomeoden.net"))
                {
                    var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    if (segments.Length >= 2 && segments[0].Equals("truyen-tranh", StringComparison.OrdinalIgnoreCase))
                    {
                        string bookSlug = Regex.Replace(segments[1].ToLowerInvariant(), @"-\d+-0$", string.Empty);
                        return "daomeoden.net|" + bookSlug;
                    }

                    if (segments.Length >= 3 && segments[0].Equals("doc-truyen-tranh", StringComparison.OrdinalIgnoreCase))
                    {
                        string bookSlug = Regex.Replace(segments[1].ToLowerInvariant(), @"-\d+$", string.Empty);
                        return "daomeoden.net|" + bookSlug;
                    }
                }

                if (host.Contains("dilib.vn") || host.Contains("thuviensach.vn"))
                {
                    var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    if (segments.Length >= 2 && segments[0].Equals("truyen-tranh", StringComparison.OrdinalIgnoreCase))
                    {
                        string bookSlug = GetDilibBookSlugFromUrl(url);
                        return "thuviensach.vn|" + bookSlug;
                    }

                    if (segments.Length >= 1 && segments[0].EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                    {
                        string bookSlug = GetDilibBookSlugFromUrl(url);
                        return "thuviensach.vn|" + bookSlug;
                    }
                }

                if (host.Contains("loppytoonn.com"))
                {
                    var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    if (segments.Length >= 2 && segments[0].Equals("the-loai", StringComparison.OrdinalIgnoreCase))
                    {
                        return "loppytoonn.com|the-loai|" + segments[1].ToLowerInvariant();
                    }

                    if (segments.Length >= 2 && segments[0].Equals("truyen", StringComparison.OrdinalIgnoreCase))
                    {
                        return "loppytoonn.com|" + segments[1].ToLowerInvariant();
                    }
                }

                if (host.Contains("mangadex.org"))
                {
                    var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    if (segments.Length >= 2 && segments[0].Equals("title", StringComparison.OrdinalIgnoreCase))
                    {
                        return "mangadex.org|title|" + segments[1].ToLowerInvariant();
                    }

                    if (segments.Length >= 2 && segments[0].Equals("chapter", StringComparison.OrdinalIgnoreCase))
                    {
                        return "mangadex.org|chapter|" + segments[1].ToLowerInvariant();
                    }
                }

            }
            catch {}
            return url;
        }

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, HttpClient> _sharedClients = new System.Collections.Concurrent.ConcurrentDictionary<string, HttpClient>(StringComparer.OrdinalIgnoreCase);

        private HttpClient GetSharedHttpClient(string urlOrHost)
        {
            string host = "";
            try
            {
                if (Uri.TryCreate(urlOrHost, UriKind.Absolute, out Uri uri))
                    host = uri.Host;
                else
                    host = urlOrHost;
            }
            catch { host = urlOrHost; }

            if (string.IsNullOrWhiteSpace(host)) host = "default";

            return _sharedClients.GetOrAdd(host, h => {
                bool useCookies = true;
                if (h.IndexOf("haibabamanga.somee.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    h.IndexOf("haibaba", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    useCookies = false;
                }

                var handler = new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                    UseCookies = useCookies
                };

                // Limit active connection per server (host) to match user setting
                try
                {
                    int limit = Math.Max(32, GetCurrentConnectionLimit());
                    handler.MaxConnectionsPerServer = limit;
                }
                catch {}

                // Auto Proxy Logic
                try
                {
                    string proxyFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".portable", "proxy.txt");
                    if (System.IO.File.Exists(proxyFile))
                    {
                        string proxyAddr = System.IO.File.ReadAllText(proxyFile).Trim();
                        if (!string.IsNullOrWhiteSpace(proxyAddr))
                        {
                            handler.Proxy = new WebProxy(proxyAddr);
                            handler.UseProxy = true;
                        }
                    }
                }
                catch {}

                if (useCookies)
                {
                    handler.CookieContainer = GetScopedCookieContainer(h);
                }

                var client = new HttpClient(handler);
                client.Timeout = TimeSpan.FromSeconds(30);
                string userAgent = GetScopedUserAgent(h);
                if (!string.IsNullOrWhiteSpace(userAgent))
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
                }
                return client;
            });
        }

        private string GetCurlPath()
        {
            try
            {
                string windir = Environment.GetEnvironmentVariable("windir") ?? @"C:\Windows";
                string sysnative = Path.Combine(windir, "sysnative", "curl.exe");
                if (File.Exists(sysnative))
                {
                    return sysnative;
                }
                string system32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "curl.exe");
                if (File.Exists(system32))
                {
                    return system32;
                }
            }
            catch {}
            return "curl.exe";
        }

        private async Task DownloadMangadexImageWithCurlAsync(string url, string referer, string filePath, CancellationToken token)
        {
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();

                string arguments = "--fail --location --silent --show-error --http1.1 --no-keepalive " +
                                   "--connect-timeout 8 --max-time 30 " +
                                   "--speed-limit 15360 --speed-time 5 " +
                                   "--retry 1 --retry-all-errors --retry-delay 1 " +
                                   "--user-agent " + QuoteWindowsArgument("Mozilla/5.0") + " " +
                                   "--output " + QuoteWindowsArgument(filePath) + " ";
                if (!string.IsNullOrWhiteSpace(referer))
                {
                    arguments += "--referer " + QuoteWindowsArgument(referer) + " ";
                }

                arguments += QuoteWindowsArgument(url);

                var startInfo = new ProcessStartInfo
                {
                    FileName = GetCurlPath(),
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System)
                };

                using (var process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    process.WaitForExit();

                    string stdErr = process.StandardError.ReadToEnd();
                    string stdOut = process.StandardOutput.ReadToEnd();
                    if (process.ExitCode != 0)
                    {
                        throw new HttpRequestException(string.IsNullOrWhiteSpace(stdErr) ? (string.IsNullOrWhiteSpace(stdOut) ? "curl MangaDex lỗi." : stdOut.Trim()) : stdErr.Trim());
                    }
                }
            }, token);
        }

        private static readonly Random _delayRandom = new Random();

        private async Task DownloadUrlToFileWithRefererAsync(string url, string referer, string filePath, CancellationToken token, bool isViHentai = false, bool isTruyenqq = false)
        {
            long minSize = 1024; // Smart Resume: >1KB mới skip, tránh skip ảnh thật dung lượng thấp
            if (File.Exists(filePath) && new FileInfo(filePath).Length > minSize)
            {
                return; // skip duplicate
            }

            // Introduce a short jittered delay to avoid hitting the server with multiple requests simultaneously
            try
            {
                int jitter = _delayRandom.Next(150, 450);
                await Task.Delay(jitter, token);
            }
            catch {}

            int delayMs = isViHentai ? 800 : (isTruyenqq ? 600 : 500);
            int maxAttempts = 3;

            try
            {
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        if (url != null &&
                            (url.IndexOf("mangadex.network", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             url.IndexOf("uploads.mangadex.org", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            await DownloadMangadexImageWithCurlAsync(url, referer, filePath, token);
                            return;
                        }

                        if (IsMangadexBrowserFetchUrl(url))
                        {
                            byte[] browserBytes = await FetchMangadexBytesViaBrowserAsync(url, referer, token);
                            using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                            {
                                await fileStream.WriteAsync(browserBytes, 0, browserBytes.Length, token);
                            }

                            return;
                        }

                        var httpClient = GetSharedHttpClient(url);
                        using (var sendCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                        {
                            sendCts.CancelAfter(20000); // 20 giây timeout cho HTTP response headers
                            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                            {
                                if (!string.IsNullOrEmpty(referer))
                                {
                                    request.Headers.Referrer = new Uri(referer);
                                }

                                using (var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, sendCts.Token))
                                {
                                if (isViHentai && (int)response.StatusCode == 429 && attempt < maxAttempts)
                                {
                                    int retryDelay = GetRetryDelayMilliseconds(response, attempt, delayMs);
                                    Log($"[vi-hentai.pro] 429 khi tải ảnh. Chờ {retryDelay}ms rồi thử lại ({attempt}/{maxAttempts}): {url}");
                                    await Task.Delay(retryDelay, token);
                                    delayMs = Math.Min(delayMs * 2, 8000);
                                    continue;
                                }

                                if ((response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Forbidden) && !string.IsNullOrEmpty(referer))
                                {
                                    // Hotlink protection fallback: Try downloading without referer
                                    using (var fallbackRequest = new HttpRequestMessage(HttpMethod.Get, url))
                                    {
                                        using (var fallbackResponse = await httpClient.SendAsync(fallbackRequest, HttpCompletionOption.ResponseHeadersRead, token))
                                        {
                                            if (fallbackResponse.StatusCode == HttpStatusCode.NotFound)
                                            {
                                                throw new HttpRequestException("404 (Not Found)");
                                            }
                                            if (fallbackResponse.IsSuccessStatusCode)
                                            {
                                                using (var contentStream = await fallbackResponse.Content.ReadAsStreamAsync())
                                                using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                                                {
                                                    await CopyToAsyncWithTimeout(contentStream, fileStream, 81920, token);
                                                }
                                                return;
                                            }
                                        }
                                    }
                                }

                                if (response.StatusCode == HttpStatusCode.NotFound)
                                {
                                    throw new HttpRequestException("404 (Not Found)");
                                }
                                response.EnsureSuccessStatusCode();

                                using (var contentStream = await response.Content.ReadAsStreamAsync())
                                using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                                {
                                    await CopyToAsyncWithTimeout(contentStream, fileStream, 81920, token);
                                }
                                return; // Success!
                            }
                        }
                    }
                    }
                    catch (HttpRequestException ex) when (attempt < maxAttempts)
                    {
                        if (url != null && (url.Contains("nhentai.net") || url.Contains("nhentaimg.com")) && (ex.Message.Contains("404") || (ex.InnerException != null && ex.InnerException.Message.Contains("404"))))
                        {
                            throw;
                        }
                        if (url != null &&
                            (url.IndexOf("mangadex.network", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             url.IndexOf("uploads.mangadex.org", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            Log("[mangadex.org] Lỗi tải ảnh: " + url + " => " + ex.Message);
                        }

                        try { if (File.Exists(filePath)) File.Delete(filePath); } catch {}
                        string label = isViHentai ? "[vi-hentai.pro]" : (isTruyenqq ? "[truyenqq]" : "[network]");
                        Log($"{label} Thử tải lại ảnh do lỗi mạng: {ex.Message}. Chờ {delayMs}ms ({attempt}/{maxAttempts}).");
                        await Task.Delay(delayMs, token);
                        delayMs = Math.Min(delayMs * 2, 8000);
                    }
                    catch (TaskCanceledException) when (!token.IsCancellationRequested && attempt < maxAttempts)
                    {
                        try { if (File.Exists(filePath)) File.Delete(filePath); } catch {}
                        string label = isViHentai ? "[vi-hentai.pro]" : (isTruyenqq ? "[truyenqq]" : "[network]");
                        Log($"{label} Thử tải lại ảnh do timeout. Chờ {delayMs}ms ({attempt}/{maxAttempts}).");
                        await Task.Delay(delayMs, token);
                        delayMs = Math.Min(delayMs * 2, 8000);
                    }
                }
            }
            catch
            {
                try { if (File.Exists(filePath)) File.Delete(filePath); } catch {}
                throw;
            }

            throw new Exception($"Không thể tải ảnh sau {maxAttempts} lần thử: {url}");
        }

        private static string SanitizeImageBaseName(string value, string fallback = "page")
        {
            string name = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = fallback;
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(name.Length);
            foreach (char ch in name)
            {
                builder.Append(invalidChars.Contains(ch) ? '-' : ch);
            }

            name = builder.ToString().Trim(' ', '.');
            while (name.Contains("--"))
            {
                name = name.Replace("--", "-");
            }

            return string.IsNullOrWhiteSpace(name) ? fallback : name;
        }

        private static string GetImageBaseNameFromUrl(string url, string fallback = "page")
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return fallback;
            }

            try
            {
                string path = url;
                if (Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
                {
                    path = uri.AbsolutePath;
                }
                else
                {
                    int queryIndex = url.IndexOf('?');
                    if (queryIndex >= 0)
                    {
                        path = url.Substring(0, queryIndex);
                    }
                }

                return SanitizeImageBaseName(Path.GetFileNameWithoutExtension(path), fallback);
            }
            catch
            {
                return fallback;
            }
        }

        private static string BuildOrderedImageFilename(int pageNumber, string imageUrl, string fallbackExtension = ".jpg", string fallbackBaseName = null)
        {
            int safePageNumber = Math.Max(1, pageNumber);
            string baseName = GetImageBaseNameFromUrl(imageUrl, fallbackBaseName ?? $"page-{safePageNumber}");
            string extension = GetSafeImageExtensionFromUrl(imageUrl);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = string.IsNullOrWhiteSpace(fallbackExtension) ? ".jpg" : fallbackExtension;
            }

            if (!extension.StartsWith(".", StringComparison.Ordinal))
            {
                extension = "." + extension;
            }

            return $"{safePageNumber:D4}-{baseName}{extension}";
        }

        public static List<string> DetermineImageFilenames(IList<string> imageUrls)
        {
            var filenames = new List<string>();
            if (imageUrls == null || imageUrls.Count == 0) return filenames;

            for (int i = 0; i < imageUrls.Count; i++)
            {
                filenames.Add(BuildOrderedImageFilename(i + 1, imageUrls[i]));
            }

            return filenames;
        }

        private static string GetSafeImageExtensionFromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return ".jpg";
            }

            // ponytail: use URL path only; query string can poison Path.GetExtension().
            string path = url;
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
            {
                path = uri.AbsolutePath;
            }
            else
            {
                int queryIndex = url.IndexOf('?');
                if (queryIndex >= 0)
                {
                    path = url.Substring(0, queryIndex);
                }
            }

            string ext = Path.GetExtension(path);
            switch ((ext ?? string.Empty).ToLowerInvariant())
            {
                case ".jpg":
                case ".jpeg":
                case ".png":
                case ".gif":
                case ".bmp":
                case ".webp":
                    return ext;
                default:
                    return ".jpg";
            }
        }

        public static int ExtractPageNumberFromFilename(string filenameWithoutExt)
        {
            return ExtractPageNumberFromFilename(filenameWithoutExt, false);
        }

        public static int ExtractPageNumberFromFilename(string filenameWithoutExt, bool isZeroBased)
        {
            if (string.IsNullOrEmpty(filenameWithoutExt)) return -1;
            
            if (filenameWithoutExt.StartsWith("page-", StringComparison.OrdinalIgnoreCase))
            {
                string part = filenameWithoutExt.Substring(5);
                if (int.TryParse(part, out int num)) return num;
            }
            int rawNum = -1;
            if (filenameWithoutExt.Contains("-"))
            {
                string firstPart = filenameWithoutExt.Split('-')[0];
                if (int.TryParse(firstPart, out int parsedFirst))
                {
                    rawNum = parsedFirst;
                }
            }
            
            if (rawNum < 0 && filenameWithoutExt.Contains("_"))
            {
                string firstPart = filenameWithoutExt.Split('_')[0];
                if (int.TryParse(firstPart, out rawNum))
                {
                    // Success
                }
                else
                {
                    rawNum = -1;
                }
            }

            if (rawNum >= 0)
            {
                return isZeroBased ? rawNum + 1 : rawNum;
            }
            else
            {
                var matchStart = Regex.Match(filenameWithoutExt, @"^\d+");
                if (matchStart.Success && int.TryParse(matchStart.Value, out int resultStart))
                {
                    rawNum = resultStart;
                }
                else
                {
                    var matchEnd = Regex.Match(filenameWithoutExt, @"\d+$");
                    if (matchEnd.Success && int.TryParse(matchEnd.Value, out int resultEnd))
                    {
                        rawNum = resultEnd;
                    }
                    else if (int.TryParse(filenameWithoutExt, out int parsed))
                    {
                        rawNum = parsed;
                    }
                }
            }

            if (rawNum >= 0)
            {
                return isZeroBased ? rawNum + 1 : rawNum;
            }
            
            return -1;
        }

        private bool ValidateDownloadedFiles(string folderPath, int expectedCount, GalleryItem queueItem, string chapterName = "General", IDictionary<int, string> pageImageUrls = null, string chapterUrl = null)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return false;

            try
            {
                var imageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif" };
                var files = Directory.GetFiles(folderPath)
                                     .Where(f => imageExtensions.Contains(Path.GetExtension(f)) && new FileInfo(f).Length > 0)
                                     .ToArray();

                if (expectedCount > 0 && files.Length >= expectedCount)
                {
                    ClearResolvedErrors(queueItem, chapterName);
                    ClearResolvedErrors(queueItem, string.Empty);
                    ClearResolvedErrors(queueItem, "General");
                    ClearResolvedErrors(queueItem, "Pages");
                    return true;
                }

                // Tự động phát hiện thư mục đặt tên dạng 0-based
                bool isZeroBased = false;
                foreach (var file in files)
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    string prefix = name;
                    if (name.Contains("_")) prefix = name.Split('_')[0];
                    else if (name.Contains("-")) prefix = name.Split('-')[0];
                    
                    if (prefix == "0" || prefix == "00" || prefix == "000")
                    {
                        isZeroBased = true;
                        break;
                    }
                }

                var existingPageNumbers = new HashSet<int>();
                foreach (var file in files)
                {
                    string nameWithoutExt = Path.GetFileNameWithoutExtension(file);
                    int pageNum = ExtractPageNumberFromFilename(nameWithoutExt, isZeroBased);
                    if (pageNum >= 0)
                    {
                        existingPageNumbers.Add(pageNum);
                    }
                }

                var missingPages = new List<int>();
                for (int i = 1; i <= expectedCount; i++)
                {
                    if (!existingPageNumbers.Contains(i))
                    {
                        missingPages.Add(i);
                    }
                }

                if (missingPages.Count > 0)
                {
                    string missingMsg = $"Thiếu các trang: {string.Join(", ", missingPages.Select(p => p.ToString("D3")))}";
                    Log($"[Cảnh báo] Thư mục '{Path.GetFileName(folderPath)}' bị thiếu {missingPages.Count} trang!");
                    if (queueItem != null)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            foreach (var p in missingPages)
                            {
                                string imageUrl = null;
                                pageImageUrls?.TryGetValue(p, out imageUrl);
                                string pageName = null;
                                if (!string.IsNullOrEmpty(imageUrl))
                                {
                                    try
                                    {
                                        pageName = Path.GetFileNameWithoutExtension(imageUrl.Split('?')[0]);
                                    }
                                    catch {}
                                }
                                queueItem.AddError(chapterName, p, "Trang bị thiếu (Missing page)", imageUrl, chapterUrl, pageName);
                            }
                        });
                    }
                    return false;
                }
                ClearResolvedErrors(queueItem, chapterName);
                ClearResolvedErrors(queueItem, string.Empty);
                ClearResolvedErrors(queueItem, "General");
                ClearResolvedErrors(queueItem, "Pages");
                return true;
            }
            catch (Exception ex)
            {
                Log($"[Lỗi] Không thể kiểm tra tính toàn vẹn của thư mục '{folderPath}': {ex.Message}");
                return false;
            }
        }

        private void ClearResolvedErrors(GalleryItem queueItem, string chapterName)
        {
            if (queueItem == null) return;

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => ClearResolvedErrors(queueItem, chapterName));
                return;
            }

            string searchChapter = string.IsNullOrWhiteSpace(chapterName) ? "-" : chapterName.Trim();

            var errorsToRemove = queueItem.Errors
                .Where(e => {
                    string eCh = string.IsNullOrWhiteSpace(e.ChapterName) ? "-" : e.ChapterName.Trim();
                    return string.Equals(eCh, searchChapter, StringComparison.OrdinalIgnoreCase);
                })
                .ToList();
            
            foreach (var err in errorsToRemove)
            {
                queueItem.Errors.Remove(err);
            }
            queueItem.ErrorCount = queueItem.Errors.Count;

            var keysToRemove = _checkErrorIndex.Keys.Where(k => {
                if (_checkErrorIndex.TryGetValue(k, out var val))
                {
                    string vCh = string.IsNullOrWhiteSpace(val.ChapterName) ? "-" : val.ChapterName.Trim();
                    return string.Equals(val.BookName, queueItem.Name, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(vCh, searchChapter, StringComparison.OrdinalIgnoreCase);
                }
                return false;
            }).ToList();

            foreach (var key in keysToRemove)
            {
                if (_checkErrorIndex.TryGetValue(key, out var itemToRemove))
                {
                    _checkErrors.Remove(itemToRemove);
                }
                _checkErrorIndex.Remove(key);
            }

            UpdateStats();
        }

        private void ClearResolvedPageError(GalleryItem queueItem, string chapterName, int pageNumber)
        {
            if (queueItem == null) return;

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => ClearResolvedPageError(queueItem, chapterName, pageNumber));
                return;
            }

            string searchChapter = string.IsNullOrWhiteSpace(chapterName) ? "-" : chapterName.Trim();

            // 1. Remove from queueItem.Errors
            var errorsToRemove = queueItem.Errors
                .Where(e => {
                    string eCh = string.IsNullOrWhiteSpace(e.ChapterName) ? "-" : e.ChapterName.Trim();
                    return string.Equals(eCh, searchChapter, StringComparison.OrdinalIgnoreCase) && e.PageNumber == pageNumber;
                })
                .ToList();

            foreach (var err in errorsToRemove)
            {
                queueItem.Errors.Remove(err);
            }
            queueItem.ErrorCount = queueItem.Errors.Count;

            // 2. Remove from _checkErrors and _checkErrorIndex
            var keysToRemove = _checkErrorIndex.Keys.Where(k => {
                if (_checkErrorIndex.TryGetValue(k, out var val))
                {
                    string vCh = string.IsNullOrWhiteSpace(val.ChapterName) ? "-" : val.ChapterName.Trim();
                    return string.Equals(val.BookName, queueItem.Name, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(vCh, searchChapter, StringComparison.OrdinalIgnoreCase) &&
                           val.PageNumber == pageNumber;
                }
                return false;
            }).ToList();

            foreach (var key in keysToRemove)
            {
                if (_checkErrorIndex.TryGetValue(key, out var itemToRemove))
                {
                    _checkErrors.Remove(itemToRemove);
                }
                _checkErrorIndex.Remove(key);
            }

            UpdateStats();
        }

        private void Delete429ArtifactsForItem(GalleryItem item, string downloadRoot)
        {
            DeleteBookTempFolderForItem(item, downloadRoot);
            DeleteProcessMarkdownForItem(item);
            DeleteWebView2RuntimeDirectoryWithRetry(GetWebView2DomainFolderName(item));
        }

        private void DeleteBookTempFolderForItem(GalleryItem item, string downloadRoot)
        {
            string tempFolder = GetBookTempFolderForItem(item, downloadRoot);
            if (string.IsNullOrWhiteSpace(tempFolder) || !Directory.Exists(tempFolder))
            {
                return;
            }

            TryDeleteDirectoryWithRetry(tempFolder, $"[RateLimit 429] Đã xóa temp book: {tempFolder}");
        }

        private string GetBookTempFolderForItem(GalleryItem item, string downloadRoot)
        {
            if (item == null)
            {
                return null;
            }

            string root = item.DownloadPath;
            if (string.IsNullOrWhiteSpace(root))
            {
                root = downloadRoot;
            }

            if (string.IsNullOrWhiteSpace(root))
            {
                return null;
            }

            string safeTitle = GetSafePathName(item.Name);
            return Path.Combine(GetEffectiveDownloadRoot(root), ".tmp", $"{safeTitle}-tmp");
        }

        private string GetWebView2DomainFolderName(GalleryItem item)
        {
            try
            {
                string url = item?.Link ?? string.Empty;
                var uri = new Uri(url);
                string host = (uri.Host ?? string.Empty).ToLowerInvariant();

                if (host.Contains("truyenqq")) return "truyenqq";
                if (host.Contains("nettruyen.tech")) return "nettruyen.tech";
                if (host.Contains("nettruyenviet10.com")) return "nettruyenviet10.com";
                if (host.Contains("nettruyen")) return "nettruyen";
                if (host.Contains("vi-hentai") || host.Contains("hentaivn")) return "hentaivn";
                if (host.Contains("damconuong")) return "damconuong";
                if (host.Contains("hentai2read")) return "hentai2read";
                if (host.Contains("daomeoden")) return "daomeoden";
                if (host.Contains("dilib.vn") || host.Contains("thuviensach.vn")) return "thuviensach.vn";
                if (host.Contains("loppytoonn.com")) return "loppytoonn.com";

                var parts = host.Split('.');
                if (parts.Length >= 2)
                {
                    return parts[parts.Length - 2];
                }

                return host;
            }
            catch
            {
                return GetSafePathName(item?.SourceDomain ?? "general");
            }
        }

        private void DeleteWebView2RuntimeDirectoryWithRetry(string domainFolder)
        {
            if (string.IsNullOrWhiteSpace(domainFolder))
            {
                return;
            }

            string path = Path.Combine(PortablePaths.WebView2RuntimeRoot, domainFolder);
            if (!Directory.Exists(path)) return;
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    Directory.Delete(path, true);
                    Log($"[RateLimit 429] Đã xóa WebView2 domain: {path}");
                    break;
                }
                catch
                {
                    System.Threading.Thread.Sleep(500);
                }
            }
        }

        private void TryDeleteDirectoryWithRetry(string path, string logLabel)
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    Directory.Delete(path, true);
                    Log(logLabel);
                    return;
                }
                catch
                {
                    System.Threading.Thread.Sleep(500);
                }
            }
        }
    }


    public class VistaFolderBrowser
    {
        public string SelectedPath { get; set; }
        public string Title { get; set; }
        public string InitialFolder { get; set; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);

        public bool ShowDialog(IntPtr owner)
        {
            var dialog = (IFileOpenDialog)new FileOpenDialog();
            try
            {
                // FOS_PICKFOLDERS (0x20) | FOS_FORCEFILESYSTEM (0x40)
                dialog.SetOptions(0x00000020 | 0x00000040);
                
                if (!string.IsNullOrEmpty(Title))
                {
                    dialog.SetTitle(Title);
                }

                if (!string.IsNullOrEmpty(InitialFolder) && Directory.Exists(InitialFolder))
                {
                    try
                    {
                        Guid riid = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
                        IShellItem initialFolderItem;
                        SHCreateItemFromParsingName(InitialFolder, IntPtr.Zero, ref riid, out initialFolderItem);
                        if (initialFolderItem != null)
                        {
                            dialog.SetFolder(initialFolderItem);
                        }
                    }
                    catch
                    {
                    }
                }

                int hr = dialog.Show(owner);
                if (hr == 0) // S_OK
                {
                    IShellItem item;
                    dialog.GetResult(out item);
                    string path;
                    item.GetDisplayName(SIGDN.FILESYSPATH, out path);
                    SelectedPath = path;
                    return true;
                }
                return false;
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.ReleaseComObject(dialog);
            }
        }

        [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
        private class FileOpenDialog { }

        [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, uint fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close([MarshalAs(UnmanagedType.Error)] int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void FilterShowEvent(IntPtr pfde);
            void GetResults(out IntPtr ppenum);
            void GetSelectedItems(out IntPtr ppssa);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(SIGDN sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string name);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        private enum SIGDN : uint
        {
            FILESYSPATH = 0x80058000
        }
    }

    public class DynamicSemaphore : IDisposable
    {
        private readonly object _syncLock = new object();
        private int _currentLimit;
        private int _activeCount;
        private readonly Func<int> _limitProvider;

        public DynamicSemaphore(int initialLimit, Func<int> limitProvider)
        {
            _currentLimit = Math.Max(1, initialLimit);
            _limitProvider = limitProvider;
        }

        public async Task WaitAsync(CancellationToken token)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();

                lock (_syncLock)
                {
                    RefreshLimitUnsafe();
                    if (_activeCount < _currentLimit)
                    {
                        _activeCount++;
                        return;
                    }
                }

                await Task.Delay(150, token);
            }
        }

        public void Release()
        {
            lock (_syncLock)
            {
                if (_activeCount > 0)
                {
                    _activeCount--;
                }

                RefreshLimitUnsafe();
            }
        }

        public void AdjustLimit()
        {
            lock (_syncLock)
            {
                RefreshLimitUnsafe();
            }
        }

        private void RefreshLimitUnsafe()
        {
            _currentLimit = Math.Max(1, _limitProvider?.Invoke() ?? _currentLimit);
        }

        public void Dispose()
        {
        }
    }

    public partial class MainWindow : Window
    {
        private async Task DownloadHentaieraGalleryAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem = null, ChapterFilter chapterFilter = null)
        {
            item.Link = NormalizeHentaieraUrl(item.Link);
            string safeTitle = GetSafePathName(item.Name);
            string resolvedRoot = GetConfiguredDownloadRoot(rootFolder, item);
            string targetFolder = Path.Combine(resolvedRoot, safeTitle);
            string tempFolder = BuildStableTempFolderPath(resolvedRoot, "hentaiera.com", safeTitle, item.Link, item.Name);
            Directory.CreateDirectory(tempFolder);
            RegisterTempFolder(tempFolder);

            try
            {
                // Fetch gallery homepage
                string html = null;
                try
                {
                    html = await FetchStringAsync(item.Link, token);
                    if (html.Contains("Just a moment...") || html.Contains("cloudflare-challenge") || html.Contains("cf-challenge"))
                    {
                        throw new HttpRequestException("Cloudflare challenge detected");
                    }
                }
                catch (HttpRequestException)
                {
                    bool ok = await SolveHentaieraCaptchaIfNeededAsync(item.Link);
                    if (!ok)
                        throw new Exception("Không thể vượt qua Cloudflare của hentaiera.com. Tải xuống bị hủy.");
                    html = await FetchStringAsync(item.Link, token);
                }

                // 1. Find total pages of the book (similar to nhentai search)
                int totalPages = 1;
                var pagesMatch = Regex.Match(html, @"(\d+)\s+pages", RegexOptions.IgnoreCase);
                if (pagesMatch.Success)
                {
                    totalPages = int.Parse(pagesMatch.Groups[1].Value);
                }
                else
                {
                    var pagesValueMatch = Regex.Match(html, @"Pages:.*?class=""value""[^>]*>(\d+)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    if (pagesValueMatch.Success)
                    {
                        totalPages = int.Parse(pagesValueMatch.Groups[1].Value);
                    }
                    else
                    {
                        var pagesLabelMatch = Regex.Match(html, @"Pages:\s*(\d+)", RegexOptions.IgnoreCase);
                        if (pagesLabelMatch.Success)
                        {
                            totalPages = int.Parse(pagesLabelMatch.Groups[1].Value);
                        }
                    }
                }

                if (queueItem != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        queueItem.TotalChapters = totalPages;
                        queueItem.CompletedChapters = 0;
                    });
                }

                WriteTempProgressLog(tempFolder, item, "Downloading", 0, totalPages, "0/0 pages", "Bắt đầu tải hentaiera");

                // Get number of connections limit
            int maxThreads = GetCurrentConnectionLimit();

                Log($"[Hentaiera] Bắt đầu tải {totalPages} trang với tối đa {maxThreads} kết nối song song...");

                using (var semaphore = new DynamicSemaphore(maxThreads, GetCurrentConnectionLimit))
                {
                    var tasks = new System.Collections.Generic.List<Task>();
                    int completedPages = 0;
                    object lockObj = new object();

                    // Gallery ID from link
                    string galleryId = GetHentaieraGalleryIdFromLink(item.Link);

                    for (int p = 1; p <= totalPages; p++)
                    {
                        int pageNum = p;
                        tasks.Add(Task.Run(async () =>
                        {
                            while (_isDownloadPaused || item.IsPaused)
                            {
                                token.ThrowIfCancellationRequested();
                                if (item.IsStopped) throw new OperationCanceledException();
                                await Task.Delay(200, token);
                            }
                            token.ThrowIfCancellationRequested();

                            await semaphore.WaitAsync(token);
                            try
                            {
                                while (_isDownloadPaused || item.IsPaused)
                                {
                                    token.ThrowIfCancellationRequested();
                                    if (item.IsStopped) throw new OperationCanceledException();
                                    await Task.Delay(200, token);
                                }
                                token.ThrowIfCancellationRequested();

                                string localFileWithoutExt = Path.Combine(tempFolder, $"{pageNum:D4}-");
                                string finalFileWithoutExt = Path.Combine(targetFolder, $"{pageNum:D4}-");
                                string downloadedPath = localFileWithoutExt;
                                var pageWatch = Stopwatch.StartNew();

                                bool exists = false;
                                string[] extensions = new string[] { ".jpg", ".png", ".jpeg", ".webp", ".gif", ".bmp" };
                                string[] searchPatterns = new[]
                                {
                                    $"{pageNum:D4}-*"
                                };
                                foreach (string pattern in searchPatterns)
                                {
                                    foreach (string folder in new[] { tempFolder, targetFolder })
                                    {                                        foreach (string existingPath in Directory.GetFiles(folder, pattern))
                                        {
                                            if (extensions.Contains(Path.GetExtension(existingPath), StringComparer.OrdinalIgnoreCase) &&
                                                new FileInfo(existingPath).Length > 1024)
                                            {
                                                exists = true;
                                                downloadedPath = existingPath;
                                                break;
                                            }
                                        }

                                        if (exists)
                                        {
                                            break;
                                        }
                                    }

                                    if (exists)
                                    {
                                        break;
                                    }
                                }

                                if (exists)
                                {
                                    pageWatch.Stop();
                                    lock (lockObj)
                                    {
                                        completedPages++;
                                        UpdateDownloadRowMetrics(queueItem, completedPages, totalPages, $"{completedPages}/{totalPages} pages", 0, 0);
                                        WriteTempProgressLog(tempFolder, item, "Downloading", completedPages, totalPages, $"{completedPages}/{totalPages} pages", $"Page {pageNum} existed");
                                    }
                                    return;
                                }

                                string localFilePath = Path.Combine(tempFolder, BuildOrderedImageFilename(pageNum, null, ".jpg", $"page-{pageNum}"));

                                // Fetch hentaiera viewer page to extract image source
                                // e.g. https://hentaiera.com/view/315003/1
                                downloadedPath = await DownloadHentaieraPageAsync(item, galleryId, pageNum, tempFolder, token);

                                lock (lockObj)
                                {
                                    completedPages++;
                                    pageWatch.Stop();
                                    long downloadedBytes = !string.IsNullOrWhiteSpace(downloadedPath) && File.Exists(downloadedPath) ? new FileInfo(downloadedPath).Length : 0;
                                    UpdateDownloadRowMetrics(queueItem, completedPages, totalPages, $"{completedPages}/{totalPages} pages", downloadedBytes, pageWatch.ElapsedMilliseconds);
                                    WriteTempProgressLog(tempFolder, item, "Downloading", completedPages, totalPages, $"{completedPages}/{totalPages} pages", $"Page {pageNum} completed");
                                }
                            }
                            finally
                            {
                                semaphore.Release();
                            }
                        }, token));
                    }

                    await Task.WhenAll(tasks);

                    WriteTempProgressLog(tempFolder, item, "Done", totalPages, totalPages, $"{totalPages}/{totalPages} pages", "Download completed");
                    MoveTempFolderToTarget(tempFolder, targetFolder, "Hentaiera");
                }

                // Check for missing files
                ValidateDownloadedFiles(targetFolder, totalPages, queueItem, "Pages");
            }
            finally
            {
                if (token.IsCancellationRequested && Directory.Exists(tempFolder))
                {
                    try
                    {
                        Directory.Delete(tempFolder, true);
                        Log($"[Cleanup] Đã xóa thư mục tạm tải dở: {tempFolder}");
                    }
                    catch (Exception ex)
                    {
                        Log($"[Cleanup Warning] Không thể xóa thư mục tạm '{tempFolder}': {ex.Message}");
                    }
                }

                UnregisterTempFolder(tempFolder);
            }
        }

        private async Task<string> DownloadHentaieraPageAsync(GalleryItem item, string galleryId, int pageNum, string targetFolder, CancellationToken token)
        {
            while (_isDownloadPaused || item.IsPaused)
            {
                token.ThrowIfCancellationRequested();
                if (item.IsStopped) throw new OperationCanceledException();
                await Task.Delay(200, token);
            }
            token.ThrowIfCancellationRequested();

            string viewUrl = $"https://hentaiera.com/view/{galleryId}/{pageNum}/";
            string viewHtml = null;
            try
            {
                viewHtml = await FetchStringAsync(viewUrl, token);
                if (viewHtml.Contains("Just a moment...") || viewHtml.Contains("cloudflare-challenge") || viewHtml.Contains("cf-challenge"))
                {
                    throw new HttpRequestException("Cloudflare challenge detected");
                }
            }
            catch (HttpRequestException)
            {
                bool ok = await SolveHentaieraCaptchaIfNeededAsync(viewUrl);
                if (!ok)
                    throw new Exception("Bị chặn bởi Captcha khi tải trang xem.");
                viewHtml = await FetchStringAsync(viewUrl, token);
            }

            // Extract image container source using gimg first, handling any attribute order
            string imgUrl = null;
            var tagMatch = Regex.Match(viewHtml, @"<img[^>]+id=""gimg""[^>]*>", RegexOptions.IgnoreCase);
            if (tagMatch.Success)
            {
                string imgTag = tagMatch.Value;
                var dataSrcMatch = Regex.Match(imgTag, @"data-src=['""](?<url>[^'""]+?)['""]", RegexOptions.IgnoreCase);
                if (dataSrcMatch.Success && !dataSrcMatch.Groups["url"].Value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    imgUrl = dataSrcMatch.Groups["url"].Value;
                }
                else
                {
                    var srcMatch = Regex.Match(imgTag, @"src=['""](?<url>[^'""]+?)['""]", RegexOptions.IgnoreCase);
                    if (srcMatch.Success && !srcMatch.Groups["url"].Value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        imgUrl = srcMatch.Groups["url"].Value;
                    }
                }
            }

            if (string.IsNullOrEmpty(imgUrl))
            {
                // Fallback to class containing image_ or lazy preloader
                var lazyMatch = Regex.Match(viewHtml, @"<img[^>]+class=['""][^'""]*?(?:lazy|image_)[^'""]*['""][^>]*>", RegexOptions.IgnoreCase);
                if (lazyMatch.Success)
                {
                    string imgTag = lazyMatch.Value;
                    var dataSrcMatch = Regex.Match(imgTag, @"data-src=['""](?<url>[^'""]+?)['""]", RegexOptions.IgnoreCase);
                    if (dataSrcMatch.Success && !dataSrcMatch.Groups["url"].Value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        imgUrl = dataSrcMatch.Groups["url"].Value;
                    }
                    else
                    {
                        var srcMatch = Regex.Match(imgTag, @"src=['""](?<url>[^'""]+?)['""]", RegexOptions.IgnoreCase);
                        if (srcMatch.Success && !srcMatch.Groups["url"].Value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                        {
                            imgUrl = srcMatch.Groups["url"].Value;
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(imgUrl))
            {
                // General match of src/data-src for hentaiera CDN images: https://*.hentaiera.com/.../page.ext
                var genMatch = Regex.Match(viewHtml, @"(?:src|data-src)\s*=\s*['""](?<imgUrl>https?://[^'""]*?\.hentaiera\.com/[^'""]+?\.(?:jpg|png|jpeg|webp|gif|bmp))['""]", RegexOptions.IgnoreCase);
                if (genMatch.Success)
                {
                    imgUrl = genMatch.Groups["imgUrl"].Value;
                }
            }

            if (!string.IsNullOrEmpty(imgUrl))
            {
                if (imgUrl.StartsWith("//"))
                {
                    imgUrl = "https:" + imgUrl;
                }

                string actualExt = GetSafeImageExtensionFromUrl(imgUrl);
                
                // Ensure extension is strictly allowed
                string[] allowedExts = new string[] { ".jpg", ".png", ".jpeg", ".bmp", ".gif", ".webp" };
                bool isAllowed = false;
                foreach (var ext in allowedExts)
                {
                    if (actualExt.Equals(ext, StringComparison.OrdinalIgnoreCase))
                    {
                        isAllowed = true;
                        break;
                    }
                }
                if (!isAllowed)
                {
                    actualExt = ".jpg";
                }

                string finalPath = Path.Combine(targetFolder, BuildOrderedImageFilename(pageNum, imgUrl, actualExt, $"page-{pageNum}"));

                while (_isDownloadPaused || item.IsPaused)
                {
                    token.ThrowIfCancellationRequested();
                    if (item.IsStopped) throw new OperationCanceledException();
                    await Task.Delay(200, token);
                }
                token.ThrowIfCancellationRequested();

                // Download using referer to avoid hotlinking protection
                await DownloadUrlToFileWithRefererAsync(imgUrl, viewUrl, finalPath, token);
                return finalPath;
            }
            else
            {
                throw new Exception($"Không thể trích xuất địa chỉ ảnh từ trang đọc Hentaiera {pageNum}");
            }
        }

        private async Task<string> FetchStringAsync(string url, CancellationToken token)
        {
            using (var httpClient = CreateScopedHttpClient(url))
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                if (!string.IsNullOrWhiteSpace(url))
                {
                    if (url.IndexOf("nhentai.net", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        request.Headers.Referrer = new Uri("https://nhentai.net/");
                    }
                    else if (url.IndexOf("hentai2read.com", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        request.Headers.Referrer = new Uri("https://hentai2read.com/");
                    }
                }
                using (var response = await httpClient.SendAsync(request, token))
                {
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync();
                }
            }
        }

        private static DateTime _lastPriorityOptimizationTime = DateTime.MinValue;
        private static readonly object _priorityLock = new object();

        private static void OptimizeSystemPriorityForBackgroundTasks()
        {
            DateTime now = DateTime.UtcNow;
            if ((now - _lastPriorityOptimizationTime).TotalSeconds < 5)
            {
                return;
            }

            lock (_priorityLock)
            {
                if ((now - _lastPriorityOptimizationTime).TotalSeconds < 5)
                {
                    return;
                }
                _lastPriorityOptimizationTime = now;
            }

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    ApplyCurrentCpuRestrictions();

                    foreach (var p in System.Diagnostics.Process.GetProcessesByName("chrome"))
                    {
                        try
                        {
                            if (p.PriorityClass != System.Diagnostics.ProcessPriorityClass.BelowNormal && p.PriorityClass != System.Diagnostics.ProcessPriorityClass.Idle)
                            {
                                p.PriorityClass = System.Diagnostics.ProcessPriorityClass.BelowNormal;
                            }
                        }
                        catch { }
                    }
                    foreach (var p in System.Diagnostics.Process.GetProcessesByName("chromedriver"))
                    {
                        try
                        {
                            if (p.PriorityClass != System.Diagnostics.ProcessPriorityClass.BelowNormal && p.PriorityClass != System.Diagnostics.ProcessPriorityClass.Idle)
                            {
                                p.PriorityClass = System.Diagnostics.ProcessPriorityClass.BelowNormal;
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            });
        }

        private async Task DownloadHitomiLaGalleryAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem = null)
        {
            string safeTitle = GetSafePathName(item.Name);
            string resolvedRoot = GetConfiguredDownloadRoot(rootFolder, item);
            string targetFolder = Path.Combine(resolvedRoot, safeTitle);
            string tempFolder = BuildStableTempFolderPath(resolvedRoot, "hitomi.la", safeTitle, item.Link, item.Name);
            Directory.CreateDirectory(tempFolder);
            RegisterTempFolder(tempFolder);

            // Đọc metadata JSON đã lưu trong Tag
            string serializedInfo = item.Tag as string;
            if (string.IsNullOrEmpty(serializedInfo))
            {
                // Fallback nếu tag trống (fetch lại)
                var idMatch = Regex.Match(item.Link, @"(\d+)(?:\.html)?$");
                if (idMatch.Success)
                {
                    string id = idMatch.Groups[1].Value;
                    string jsContent = await FetchStringAsync($"https://ltn.gold-usergeneratedcontent.net/galleries/{id}.js", token);
                    if (!string.IsNullOrEmpty(jsContent))
                    {
                        serializedInfo = jsContent.Replace("var galleryinfo = ", "").Trim();
                    }
                }
            }

            if (string.IsNullOrEmpty(serializedInfo))
            {
                throw new Exception("Không thể lấy thông tin metadata của Hitomi gallery.");
            }

            dynamic galleryInfo = JsonConvert.DeserializeObject(serializedInfo);
            string galleryId = galleryInfo.id != null ? galleryInfo.id.ToString() : string.Empty;
            var files = galleryInfo.files;
            if (files == null || files.Count == 0)
            {
                throw new Exception("Không có file ảnh nào trong gallery này.");
            }

            int totalPages = files.Count;
            if (queueItem != null)
            {
                Dispatcher.Invoke(() =>
                {
                    queueItem.TotalChapters = totalPages;
                    queueItem.CompletedChapters = 0;
                    queueItem.DownloadingChapter = "Trang 0/" + totalPages;
                    queueItem.CurrentProcess = "Trang 0/" + totalPages;
                });
            }

            // Làm mới gg.js 1 lần duy nhất cho toàn bộ gallery
            await _hitomiGG.RefreshAsync(this);

            int maxThreads = GetCurrentConnectionLimit();
            Log($"[hitomi.la] Bắt đầu tải {totalPages} trang với tối đa {maxThreads} kết nối...");

            using (var semaphore = new DynamicSemaphore(maxThreads, GetCurrentConnectionLimit))
            {
                var tasks = new System.Collections.Generic.List<Task>();
                int completedPages = 0;
                object lockObj = new object();

                for (int p = 1; p <= totalPages; p++)
                {
                    int pageNum = p;
                    var fileItem = files[pageNum - 1];
                    string hash = fileItem.hash;
                    string name = fileItem.name;

                    tasks.Add(Task.Run(async () =>
                    {
                        while (_isDownloadPaused)
                        {
                            token.ThrowIfCancellationRequested();
                            await Task.Delay(200, token);
                        }
                        token.ThrowIfCancellationRequested();

                        await semaphore.WaitAsync(token);
                        string imgUrl = null;
                        try
                        {
                            while (_isDownloadPaused)
                            {
                                token.ThrowIfCancellationRequested();
                                await Task.Delay(200, token);
                            }
                            token.ThrowIfCancellationRequested();

                            string checkFileName = BuildOrderedImageFilename(pageNum, name, ".jpg", $"page-{pageNum}");
                            string localFilePath = Path.Combine(tempFolder, checkFileName);
                            string finalFilePath = Path.Combine(targetFolder, checkFileName);
                            string downloadedPath = localFilePath;
                            var pageWatch = Stopwatch.StartNew();

                            bool alreadyExists = false;
                            string[] checkExts = { "jpg", "png", "webp", "gif", "jpeg", "bmp" };
                            foreach (var checkExt in checkExts)
                            {
                                string testPathTemp = Path.ChangeExtension(localFilePath, checkExt);
                                string testPathFinal = Path.ChangeExtension(finalFilePath, checkExt);
                                if (File.Exists(testPathTemp) && new FileInfo(testPathTemp).Length > 1024)
                                {
                                    alreadyExists = true;
                                    break;
                                }
                                if (File.Exists(testPathFinal) && new FileInfo(testPathFinal).Length > 1024)
                                {
                                    alreadyExists = true;
                                    break;
                                }
                            }

                            if (alreadyExists)
                            {
                                pageWatch.Stop();
                                lock (lockObj)
                                {
                                    completedPages++;
                                    string processText = $"Trang {completedPages}/{totalPages}";
                                    UpdateDownloadRowMetrics(queueItem, completedPages, totalPages, processText, 0, 0);
                                    WriteTempProgressLog(tempFolder, item, "Downloading", completedPages, totalPages, processText, $"Page {pageNum} existed");
                                }
                                ClearResolvedPageError(queueItem, string.Empty, pageNum);
                                return;
                            }

                            // Giải mã URL ảnh từ metadata đã lưu
                            string b = _hitomiGG.B;
                            string s = _hitomiGG.GetS(hash);
                            string fullUrl = $"https://a.gold-usergeneratedcontent.net/{b}{s}/{hash}.webp";
                            string fullSub = GetHitomiSubdomainAsync(fullUrl, null, "webp");
                            imgUrl = fullUrl.Replace("//a.gold-usergeneratedcontent.net/", $"//{fullSub}.gold-usergeneratedcontent.net/");

                            string fileExt = Path.GetExtension(imgUrl) ?? ".webp";
                            string directPath = Path.ChangeExtension(localFilePath, fileExt);

                            string hitomiReferer = !string.IsNullOrEmpty(galleryId)
                                ? $"https://hitomi.la/reader/{galleryId}.html"
                                : (item.Link != null ? Regex.Replace(item.Link, @"^https?://hitomi\.la/(?:doujinshi|manga|gamecg|cg|gallery)/", "https://hitomi.la/reader/") : "https://hitomi.la/");

                            // Tải ảnh trực tiếp qua Shared HttpClient không bị chặn jitter delay
                            var httpClient = GetSharedHttpClient(imgUrl);
                            using (var sendCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                            {
                                sendCts.CancelAfter(30000);
                                using (var request = new HttpRequestMessage(HttpMethod.Get, imgUrl))
                                {
                                    if (!string.IsNullOrEmpty(hitomiReferer))
                                    {
                                        request.Headers.Referrer = new Uri(hitomiReferer);
                                    }
                                    using (var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, sendCts.Token))
                                    {
                                        response.EnsureSuccessStatusCode();
                                        using (var contentStream = await response.Content.ReadAsStreamAsync())
                                        using (var fileStream = new FileStream(directPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                                        {
                                            await CopyToAsyncWithTimeout(contentStream, fileStream, 81920, token);
                                        }
                                    }
                                }
                            }
                            downloadedPath = directPath;

                            lock (lockObj)
                            {
                                completedPages++;
                                pageWatch.Stop();
                                long downloadedBytes = File.Exists(downloadedPath) ? new FileInfo(downloadedPath).Length : 0;
                                string processText = $"Trang {completedPages}/{totalPages}";
                                UpdateDownloadRowMetrics(queueItem, completedPages, totalPages, processText, downloadedBytes, pageWatch.ElapsedMilliseconds);
                                WriteTempProgressLog(tempFolder, item, "Downloading", completedPages, totalPages, processText, $"Page {pageNum} completed");
                            }
                            ClearResolvedPageError(queueItem, string.Empty, pageNum);
                        }
                        catch (Exception ex)
                        {
                            Log($"[hitomi.la] Lỗi tải trang {pageNum}: {ex.Message}");
                            if (queueItem != null)
                            {
                                string traceMessage = $"Book: {item.Link}{Environment.NewLine}Page: {pageNum}{Environment.NewLine}Error: {ex.Message}";
                                string errorImgUrl = imgUrl ?? item.Link;
                                string pageName = Path.GetFileNameWithoutExtension(name);
                                Dispatcher.Invoke(new Action(() =>
                                {
                                    queueItem.AddError(string.Empty, pageNum, traceMessage, errorImgUrl, item.Link, pageName);
                                    RecordCheckError("hitomi.la", item.Name, string.Empty, pageNum, traceMessage, errorImgUrl, pageName);
                                }));
                            }
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, token));
                }

                await Task.WhenAll(tasks);

                WriteTempProgressLog(tempFolder, item, "Done", totalPages, totalPages, $"{totalPages}/{totalPages} pages", "Download completed");
                MoveTempFolderToTarget(tempFolder, targetFolder, "hitomi.la");
                ValidateDownloadedFiles(targetFolder, totalPages, queueItem, string.Empty);
            }
        }
    }
}

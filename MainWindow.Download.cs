using System;
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
        private string _activeDownloadRoot;
        private int _downloadSessionTotalGalleries;
        private int _downloadSessionCompletedGalleries;
        private int _nextScheduledDownloadOrder = 0;
        private int _nextDownloadStartOrder = 0;
        private bool _suppressDownloadToggleEvent;

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
            });
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
                                 siteFolder == "sayhentai.tv" ||
                                 siteFolder == "truyenggvn.com" ||
                                 (siteFolder != null && (
                                     siteFolder.IndexOf("truyenqq", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     siteFolder.IndexOf("vi-hentai", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     siteFolder.IndexOf("damconuong", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     siteFolder.IndexOf("nettruyen", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     siteFolder.IndexOf("hentai2read", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     siteFolder.IndexOf("dilib", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     siteFolder.IndexOf("daomeoden", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     siteFolder.IndexOf("ggvn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     siteFolder.IndexOf("sayhentai", StringComparison.OrdinalIgnoreCase) >= 0
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
                    return "nettruyen";
                }

                if (host.Contains("daomeoden"))
                {
                    return "daomeoden.net";
                }

                if (host.Contains("dilib.vn"))
                {
                    return "dilib.vn";
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

                if (host.Contains("nhentai"))
                {
                    return "nhentai.xxx";
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

            double percent = totalPages > 0
                ? Math.Min(100d, Math.Max(0d, (double)completedPages * 100d / totalPages))
                : 0d;

            if (bytesDownloaded > 0)
            {
                System.Threading.Interlocked.Add(ref item._downloadedBytesAccumulator, bytesDownloaded);
            }

            Dispatcher.BeginInvoke(new Action(() =>
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
            else if (lowerKey.Contains("dilib.vn"))
            {
                siteKey = "dilib.vn";
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
            else if (lowerKey.Contains("nhentai"))
            {
                siteKey = "nhentai.xxx";
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

        private string GetSafeChapterPathName(string bookTitle, string chapterTitle, int maxLength = 120)
        {
            string combined = string.IsNullOrWhiteSpace(bookTitle)
                ? NormalizeChapterLabel(chapterTitle)
                : CompactSingleLine(bookTitle) + " - " + NormalizeChapterLabel(chapterTitle);
            return GetSafePathName(combined, maxLength);
        }

        private string GetDownloadChapterFolderName(string bookTitle, string chapterTitle)
        {
            return _isSingleComicFolderType
                ? GetSafeChapterPathName(chapterTitle)
                : GetSafeChapterPathName(bookTitle, chapterTitle);
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
            string bookKey = GetBookIdentifier(item?.Link) ?? item?.Name ?? "item";
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
            string bookKey = GetBookIdentifier(item?.Link) ?? item?.Name ?? "item";
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
            string bookKey = GetBookIdentifier(item?.Link) ?? item?.Name ?? "item";
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
            string bookKey = GetBookIdentifier(item?.Link) ?? item?.Name ?? "item";
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
            double chapterNumber = ParseChapterNumber(chapterLink);
            if (chapterNumber > 0)
            {
                string chapterText = chapterNumber.ToString("0.####", CultureInfo.InvariantCulture);
                string[] parts = chapterText.Split('.');
                if (parts.Length > 0 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int wholeNumber))
                {
                    parts[0] = wholeNumber.ToString("D4", CultureInfo.InvariantCulture);
                    return "chap " + string.Join(".", parts);
                }
            }

            string slug = GetChapterSlugFromLink(chapterLink);
            if (!string.IsNullOrWhiteSpace(slug))
            {
                return CompactSingleLine(slug.Replace("-", " "));
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

        private bool HasNumericChapterIdentity(string chapterLink)
        {
            return ParseChapterNumber(chapterLink) > 0d;
        }

        private void InitializeChapterProcess(string rootFolder, string siteFolder, GalleryItem item, IList<string> chapterLinks, bool preserveExistingDone = true)
        {
            string dbPath = GetProcessDbPath(rootFolder);
            string bookKey = GetBookIdentifier(item?.Link) ?? item?.Name ?? "item";
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
                                    string label = GetChapterProcessLabel(link);

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
            string bookKey = GetBookIdentifier(item?.Link) ?? item?.Name ?? "item";
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

            string chapterLabel = GetChapterProcessLabel(chapterLink);
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

        private int GetHighestDownloadedChapterNumberFromDisk(string rootFolder, string siteFolder, GalleryItem item)
        {
            string bookFolder = GetDownloadBookFolderPath(rootFolder, siteFolder, item);
            if (string.IsNullOrWhiteSpace(bookFolder) || !Directory.Exists(bookFolder))
            {
                return 0;
            }

            int highest = 0;
            try
            {
                foreach (string chapterFolder in Directory.GetDirectories(bookFolder))
                {
                    if (!Directory.EnumerateFileSystemEntries(chapterFolder).Any())
                    {
                        continue;
                    }

                    double chapterNumber = ParseChapterNumber(Path.GetFileName(chapterFolder));
                    if (chapterNumber <= 0)
                    {
                        continue;
                    }

                    int wholeNumber = Math.Max(1, (int)Math.Floor(chapterNumber));
                    if (wholeNumber > highest)
                    {
                        highest = wholeNumber;
                    }
                }
            }
            catch
            {
            }

            return highest;
        }

        private List<string> FilterPendingChapterLinksFromProcess(string rootFolder, string siteFolder, GalleryItem item, IList<string> chapterLinks)
        {
            bool preserveExistingDone = ShouldPreserveExistingProcessState(rootFolder, siteFolder, item);
            InitializeChapterProcess(rootFolder, siteFolder, item, chapterLinks, preserveExistingDone);
            var pending = (LoadPendingChapterLinksFromProcess(rootFolder, siteFolder, item) ?? chapterLinks.ToList())
                .Where(link => !IsChapterFolderAlreadyDownloaded(rootFolder, siteFolder, item, link))
                .ToList();

            bool allNumeric = chapterLinks != null && chapterLinks.Count > 0 && chapterLinks.All(HasNumericChapterIdentity);
            int highestDownloadedChapter = allNumeric ? GetHighestDownloadedChapterNumberFromDisk(rootFolder, siteFolder, item) : 0;
            if (highestDownloadedChapter > 0)
            {
                var afterHighest = pending
                    .Where(link => ParseChapterNumber(link) > highestDownloadedChapter)
                    .ToList();

                if (afterHighest.Count > 0)
                {
                    // ponytail: cutoff theo chap lớn nhất chỉ heuristic; validate pages vẫn chạy sau download.
                    return afterHighest;
                }
            }

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
            string bookKey = GetBookIdentifier(item?.Link) ?? item?.Name ?? "item";
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

        private async Task HandleStartDownloadToggleCheckedAsync()
        {
            string downloadRoot = txtDownloadPath.Text.Trim();
            if (string.IsNullOrEmpty(downloadRoot))
            {
                SetDownloadToggleState(false);
                MessageBox.Show("Vui lòng chọn thư mục lưu (Please select a download folder).", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var itemsToDownload = dgResults.Items.Cast<GalleryItem>().Where(item => item.IsChecked).ToList();
            if (!itemsToDownload.Any())
            {
                SetDownloadToggleState(false);
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

            var itemsToDownload = dgResults.Items.Cast<GalleryItem>().Where(item => item.IsChecked).ToList();
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

            if (_downloadCts != null)
            {
                _downloadCts.Cancel();
                _httpClient.CancelPendingRequests();
                _isDownloadPaused = false;
                Log("Đang dừng quá trình tải xuống... (Stopping download process...)");

                foreach (var item in _scrapedItems)
                {
                    if (item.Status == "Downloading" || item.Status == "Paused" || item.Status == "Queued")
                    {
                        item.IsStopped = true;
                        item.Status = "Cancelled";
                    }
                }

                // Best-effort cleanup so a new Start doesn't inherit leftover temp folders.
                CleanupActiveTempFolders();
            }

            UpdateLightNovelFloatingControlState();
        }

        internal async Task StartDownloadProcessAsync(System.Collections.Generic.List<GalleryItem> itemsToDownload, bool preserveExistingState = false)
        {
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
            btnScrape.IsEnabled = false;
            btnFetchInfo.IsEnabled = false;
            if (btnNhentaiScrape != null) btnNhentaiScrape.IsEnabled = false;
            if (btnNhentaiFetchInfo != null) btnNhentaiFetchInfo.IsEnabled = false;
            if (btnViHentaiScrape != null) btnViHentaiScrape.IsEnabled = false;
            if (btnViHentaiFetchInfo != null) btnViHentaiFetchInfo.IsEnabled = false;
            if (btnTruyenqqScrape != null) btnTruyenqqScrape.IsEnabled = false;
            if (btnTruyenqqFetchInfo != null) btnTruyenqqFetchInfo.IsEnabled = false;
            if (btnNettruyenScrape != null) btnNettruyenScrape.IsEnabled = false;
            if (btnNettruyenFetchInfo != null) btnNettruyenFetchInfo.IsEnabled = false;
            if (btnHentaieraScrape != null) btnHentaieraScrape.IsEnabled = false;
            if (btnHentaieraFetchInfo != null) btnHentaieraFetchInfo.IsEnabled = false;
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
                    MessageBox.Show("Có truyện chưa tải xong. Xem cột trạng thái.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (OperationCanceledException)
            {
                Log("Quá trình tải xuống đã bị dừng bởi người dùng.");
                lblStatus.Text = _isVietnameseUi ? "Đã dừng tải." : "Download stopped.";
            }
            catch (Exception ex)
            {
                Log($"Critical download error: {ex.Message}");
                lblStatus.Text = _isVietnameseUi ? "Tải xuống thất bại." : "Download failed.";
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
                if (btnNhentaiScrape != null) btnNhentaiScrape.IsEnabled = true;
                if (btnNhentaiFetchInfo != null) btnNhentaiFetchInfo.IsEnabled = true;
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
                if (item == null)
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

        private void SetDownloadToggleState(bool isRunning)
        {
            Dispatcher.Invoke(() =>
            {
                if (btnStartDownload == null)
                {
                    return;
                }

                _suppressDownloadToggleEvent = true;
                try
                {
                    btnStartDownload.IsChecked = isRunning;
                    btnStartDownload.ToolTip = _isVietnameseUi
                        ? (isRunning ? "DỪNG TẢI" : "TẢI TẤT CẢ")
                        : (isRunning ? "STOP DOWNLOAD" : "DOWNLOAD ALL");
                }
                finally
                {
                    _suppressDownloadToggleEvent = false;
                }
            });

            UpdateCompactDownloadToolbarState();
        }

        private Task RunQueuedGalleryDownloadAsync(GalleryItem item, string downloadRoot, ChapterFilter chapterFilter, CancellationToken token, int scheduledOrder)
        {
            return Task.Run(async () =>
            {
                bool hasSemaphoreSlot = false;
                ChapterFilter effectiveChapterFilter = GetChapterSelectionFilterForItem(item);
                while (Volatile.Read(ref _nextDownloadStartOrder) != scheduledOrder)
                {
                    token.ThrowIfCancellationRequested();
                    if (item.IsStopped)
                    {
                        throw new OperationCanceledException();
                    }

                    await Task.Delay(100, token);
                }

                await _activeBookSemaphore.WaitAsync(token);
                hasSemaphoreSlot = true;
                Interlocked.Increment(ref _nextDownloadStartOrder);
                CancellationTokenSource speedTrackerCts = null;
                Task speedTrackerTask = null;
                bool countAsCompleted = true;
                try
                {
                    while (_isDownloadPaused || item.IsPaused)
                    {
                        token.ThrowIfCancellationRequested();
                        if (item.IsStopped) throw new OperationCanceledException();
                        await Task.Delay(200, token);
                    }
                    token.ThrowIfCancellationRequested();

                    Dispatcher.Invoke(() =>
                    {
                        item.Status = "Downloading";
                    });

                    Log($"[Download] Đang tải: {item.Name} ({item.Link})");

                    item.DownloadSpeedBytesPerSecond = 0;
                    item._downloadedBytesAccumulator = 0;
                    UpdateTotalDownloadSpeedHeader();

                    speedTrackerCts = CancellationTokenSource.CreateLinkedTokenSource(token);
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
                        await DownloadGalleryAsync(item, downloadRoot, token, item, effectiveChapterFilter);

                        if (item.GetUniqueErrorCount() > 0)
                        {
                            await RetryAllDownloadQueueItemErrorsAsync(item, token);
                        }

                        await Dispatcher.InvokeAsync(() =>
                        {
                            bool hasErrors = item.HasAnyErrors();
                            item.Status = hasErrors ? "Error" : "Completed";
                            item.CurrentProcess = hasErrors ? "Done with errors" : "Done";
                            item.IsChecked = hasErrors ? item.IsChecked : false;
                        });
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
                        Dispatcher.Invoke(() => { item.Status = "Paused"; });
                        throw;
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

                        Log($"[Lỗi] Không thể tải truyện '{item.Name}': {ex.Message}");
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
                    lock (_downloadSessionLock)
                    {
                        _scheduledDownloadItems.Remove(item);
                        _scheduledDownloadOrder.Remove(item);
                    }

                    if (hasSemaphoreSlot)
                    {
                        _activeBookSemaphore?.Release();
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

                await Task.WhenAll(pendingTasks);
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

                            string fileName = isFastPath ? $"{pageNum:D3}.{ext}" : $"{pageNum:D3}.jpg";
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

            // Match image src/data-src in Hentaiforce reader page
            var imgMatch = Regex.Match(html, @"(?:src|data-src)\s*=\s*""(?<imgUrl>https?://[a-zA-Z0-9.-]+/img/\d+-\d+\.(?:jpg|png|jpeg|webp))""", RegexOptions.IgnoreCase);
            
            if (imgMatch.Success)
            {
                string imgUrl = imgMatch.Groups["imgUrl"].Value;
                
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
            else
            {
throw new Exception($"Không thể trích xuất địa chỉ ảnh từ trang đọc {pageNum}");
        }

        }

        private async Task<byte[]> GetByteArrayWithRefererAsync(string url, string referer)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.Referrer = new Uri(referer);
                using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
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
                   source.IndexOf("nhentai.xxx", StringComparison.OrdinalIgnoreCase) >= 0 ||
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
                       url.IndexOf("nhentai.xxx", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       url.IndexOf("nhentaimg.com", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        private async Task DownloadNhentaiGalleryAsync(GalleryItem item, string rootFolder, CancellationToken token, GalleryItem queueItem = null)
        {
            string safeTitle = GetSafePathName(item.Name);
            string resolvedRoot = GetConfiguredDownloadRoot(rootFolder, item);
            string targetFolder = Path.Combine(resolvedRoot, safeTitle);
            string tempFolder = BuildStableTempFolderPath(resolvedRoot, "nhentai.xxx", safeTitle, item.Link, item.Name);
            Directory.CreateDirectory(tempFolder);
            RegisterTempFolder(tempFolder);
            string normalizedBookUrl = NormalizeNhentaiBookUrl(item.Link);
            int totalPages = item.NhentaiTotalPagesHint > 0
                ? item.NhentaiTotalPagesHint
                : await GetNhentaiTotalPagesFromBookAsync(normalizedBookUrl, token);
            if (totalPages <= 0)
            {
                throw new Exception($"Không xác định được tổng số trang nhentai.xxx. Book: {normalizedBookUrl}");
            }
            item.NhentaiTotalPagesHint = totalPages;
            Log($"[nhentai.xxx] Book: {normalizedBookUrl} | Pages: {totalPages}");

            // Get number of connections
            int maxThreads = GetCurrentConnectionLimit();

            Log($"[Đa luồng nhentai.xxx] Bắt đầu tải {totalPages} trang qua redirect page-image, tối đa {maxThreads} kết nối song song...");

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

                            string fileName = $"{pageNum:D3}.jpg";
                            string localFilePath = Path.Combine(tempFolder, fileName);
                            string finalFilePath = Path.Combine(targetFolder, fileName);
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
                                if (File.Exists(testPathTemp) && new FileInfo(testPathTemp).Length > 0)
                                {
                                    alreadyExists = true;
                                    existingFile = testPathTemp;
                                    break;
                                }
                                if (File.Exists(testPathFinal) && new FileInfo(testPathFinal).Length > 0)
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
                                        lblStatus.Text = $"[{completedPages}/{totalPages}] Tải {safeTitle} (nhentai.xxx)";
                                    }));
                                }
                                return;
                            }

                            try
                            {
                                downloadedPath = await DownloadNhentaiRedirectPageAsync(normalizedBookUrl, pageNum, localFilePath, token);
                            }
                            catch (Exception pageEx)
                            {
                                Log($"[nhentai] Lỗi trang {pageNum}: {pageEx.Message}");
                                if (queueItem != null)
                                {
                                    string pageUrl = item.Link.TrimEnd('/') + "/" + pageNum + "/";
                                    string directUrl = ExtractNhentaiDirectImageUrl(pageEx.Message);
                                    string traceMessage =
                                        $"Book: {item.Link}{Environment.NewLine}" +
                                        $"Reader: {pageUrl}{Environment.NewLine}" +
                                        $"Image: {(string.IsNullOrWhiteSpace(directUrl) ? "N/A" : directUrl)}{Environment.NewLine}" +
                                        $"Error: {pageEx.Message}";

                                    _ = Dispatcher.BeginInvoke(new Action(() =>
                                    {
                                        string pageName = !string.IsNullOrEmpty(directUrl) ? Path.GetFileNameWithoutExtension(directUrl.Split('?')[0]) : pageNum.ToString();
                                        queueItem.AddError(string.Empty, pageNum, traceMessage, directUrl ?? pageUrl, item.Link, pageName);
                                        RecordCheckError(item.SourceDomain ?? "nhentai.xxx", item.Name, string.Empty, pageNum, traceMessage, directUrl ?? pageUrl, pageName);
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
                                        lblStatus.Text = $"[{completedPages}/{totalPages}] Tải {safeTitle} (nhentai.xxx)";
                                    }));
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

        private string NormalizeNhentaiBookUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return url;
            }

            var galleryMatch = Regex.Match(url, @"https?://nhentai\.(?:net|xxx)/g/(?<bookId>\d+)/?", RegexOptions.IgnoreCase);
            if (galleryMatch.Success)
            {
                return $"https://nhentai.xxx/g/{galleryMatch.Groups["bookId"].Value}/";
            }

            return url.Trim();
        }

        private async Task<int> GetNhentaiTotalPagesFromBookAsync(string bookUrl, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
                string html = await FetchStringAsync(bookUrl, token);

            var patterns = new[]
            {
                Regex.Match(html, @"(\d+)\s+pages", RegexOptions.IgnoreCase),
                Regex.Match(html, @"Pages:.*?class=""value""[^>]*>(\d+)", RegexOptions.IgnoreCase | RegexOptions.Singleline),
                Regex.Match(html, @"""num_pages""\s*:\s*(\d+)", RegexOptions.IgnoreCase),
                Regex.Match(html, @"<span[^>]*class=""num-pages""[^>]*>\s*(\d+)\s*</span>", RegexOptions.IgnoreCase),
                Regex.Match(html, @"id=""load_pages""\s+value=""(\d+)""", RegexOptions.IgnoreCase),
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

        private async Task<string> DownloadNhentaiRedirectPageAsync(string bookUrl, int pageNum, string targetPath, CancellationToken token)
        {
            while (_isDownloadPaused)
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(200, token);
            }
            token.ThrowIfCancellationRequested();

            string pageUrl = $"{bookUrl.TrimEnd('/')}/{pageNum}/";
                string html = await FetchStringAsync(pageUrl, token);
            string imageUrl = ExtractNhentaiXxxImageUrl(html, pageNum);
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                throw new Exception($"Không trích xuất được ảnh thật từ reader page. Reader: {pageUrl}");
            }

            string actualExt = Path.GetExtension(new Uri(imageUrl).AbsolutePath);
            if (string.IsNullOrWhiteSpace(actualExt))
            {
                actualExt = ".jpg";
            }

            string finalPath = Path.ChangeExtension(targetPath, actualExt.TrimStart('.'));
            await DownloadUrlToFileWithRefererAsync(imageUrl, pageUrl, finalPath, token);
            Log($"[nhentai.xxx] Trang {pageNum} -> {imageUrl}");
            return finalPath;
        }

        private string GuessImageExtensionFromContentType(string mediaType)
        {
            switch ((mediaType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "image/webp":
                    return ".webp";
                case "image/png":
                    return ".png";
                case "image/gif":
                    return ".gif";
                case "image/jpeg":
                case "image/jpg":
                    return ".jpg";
                case "image/bmp":
                    return ".bmp";
                default:
                    return null;
            }
        }

        private string ExtractNhentaiXxxImageUrl(string html, int pageNum)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return null;
            }

            html = GetSafeChapterHtml(html);

            string[] patterns =
            {
                @"(?:data-src|src)=[""'](?<imgUrl>https?://[a-z0-9-]+\.nhentaimg\.com/[^""']+/" + pageNum + @"\.(?:jpg|png|gif|webp|jpeg|bmp))[""']",
                @"(?<imgUrl>https?://[a-z0-9-]+\.nhentaimg\.com/[^""']+/" + pageNum + @"\.(?:jpg|png|gif|webp|jpeg|bmp))"
            };

            foreach (string pattern in patterns)
            {
                var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (match.Success)
                {
                    return match.Groups["imgUrl"].Value;
                }
            }

            return null;
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
                using (var request = new HttpRequestMessage(HttpMethod.Head, url))
                {
                    request.Headers.Referrer = new Uri(string.IsNullOrWhiteSpace(referer) ? "https://nhentai.net/" : referer);
                    using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                    {
                        if (response.IsSuccessStatusCode) return true;
                    }
                }
                
                // Fallback to GET if HEAD method is not allowed or fails
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.Referrer = new Uri(string.IsNullOrWhiteSpace(referer) ? "https://nhentai.net/" : referer);
                    using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
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
                using (var request = new HttpRequestMessage(HttpMethod.Get, testUrl))
                {
                    using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
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
                        var captchaWin = CreateCaptchaWindow(testUrl, autoDeleteCookiesOnLoad: true, headlessAutomation: _lightNovelAutoFocusEnabled);
                        captchaWin.Owner = this;

                        if (await captchaWin.ShowNonBlockingAsync())
                        {
                            var originalUri = new Uri(testUrl);
                            var resolvedUri = captchaWin.ResolvedUri ?? originalUri;

                            var resolvedCookies = captchaWin.ResolvedCookies.GetCookies(resolvedUri);
                            foreach (Cookie cookie in resolvedCookies)
                            {
                                _cookieContainer.Add(resolvedUri, cookie);
                            }

                            if (originalUri.Host != resolvedUri.Host)
                            {
                                var originalCookies = captchaWin.ResolvedCookies.GetCookies(originalUri);
                                foreach (Cookie cookie in originalCookies)
                                {
                                    _cookieContainer.Add(originalUri, cookie);
                                }
                            }

                            if (!string.IsNullOrEmpty(captchaWin.UserAgent))
                            {
                                _httpClient.DefaultRequestHeaders.UserAgent.Clear();
                                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(captchaWin.UserAgent);
                            }

                            _lastNhentaiResolvedHtml = captchaWin.ResolvedHtml;
                            solved = true;
                        }
                    });
                }
                finally
                {
                    _isCaptchaWindowActive = false;
                }

                if (solved)
                {
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
                using (var request = new HttpRequestMessage(HttpMethod.Get, testUrl))
                {
                    using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
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

                        if (await captchaWin.ShowNonBlockingAsync())
                        {
                            var uri = new Uri("https://vi-hentai.pro");
                            var cookies = captchaWin.ResolvedCookies.GetCookies(uri);
                            foreach (Cookie cookie in cookies)
                            {
                                _cookieContainer.Add(uri, cookie);
                            }

                            if (!string.IsNullOrEmpty(captchaWin.UserAgent))
                            {
                                _httpClient.DefaultRequestHeaders.UserAgent.Clear();
                                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(captchaWin.UserAgent);
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
                return GetComboBoxSelectedInt(cmbConnections, 4);
            }

            int val = 4;
            try
            {
                Dispatcher.Invoke(() =>
                {
                    val = GetComboBoxSelectedInt(cmbConnections, 4);
                });
            }
            catch
            {
            }
            return val;
        }

        private int GetCurrentMultiDownloadLimit()
        {
            if (Dispatcher.CheckAccess())
            {
                return GetComboBoxSelectedInt(cmbMultiDownload, 2);
            }

            int val = 2;
            try
            {
                Dispatcher.Invoke(() =>
                {
                    val = GetComboBoxSelectedInt(cmbMultiDownload, 2);
                });
            }
            catch
            {
            }
            return val;
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

                if (host.Contains("nhentai.xxx") || host.Contains("nhentai.net"))
                {
                    var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    if (segments.Length >= 2 && segments[0].Equals("g", StringComparison.OrdinalIgnoreCase))
                    {
                        return "nhentai.xxx|" + segments[1].ToLowerInvariant();
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

                if (host.Contains("dilib.vn"))
                {
                    var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    if (segments.Length >= 2 && segments[0].Equals("truyen-tranh", StringComparison.OrdinalIgnoreCase))
                    {
                        string bookSlug = Regex.Replace(segments[1].ToLowerInvariant(), @"-chap-\d+$", string.Empty);
                        bookSlug = Regex.Replace(bookSlug, @"-\d+$", string.Empty);
                        return "dilib.vn|" + bookSlug;
                    }

                    if (segments.Length >= 1 && segments[0].EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                    {
                        string bookSlug = Regex.Replace(Path.GetFileNameWithoutExtension(segments[0]).ToLowerInvariant(), @"-\d+$", string.Empty);
                        return "dilib.vn|" + bookSlug;
                    }
                }
            }
            catch {}
            return url;
        }

        private async Task DownloadUrlToFileWithRefererAsync(string url, string referer, string filePath, CancellationToken token, bool isViHentai = false, bool isTruyenqq = false)
        {
            long minSize = (isTruyenqq || (url != null && (url.Contains("nhentai.xxx") || url.Contains("nhentai.net") || url.Contains("nhentaimg.com")))) ? 0 : 1024;
            if (File.Exists(filePath) && new FileInfo(filePath).Length > minSize)
            {
                return; // skip duplicate
            }

            int delayMs = isViHentai ? 800 : (isTruyenqq ? 600 : 500);
            int maxAttempts = 3;

            try
            {
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                        {
                            if (!string.IsNullOrEmpty(referer))
                            {
                                request.Headers.Referrer = new Uri(referer);
                            }

                            using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token))
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
                                        using (var fallbackResponse = await _httpClient.SendAsync(fallbackRequest, HttpCompletionOption.ResponseHeadersRead, token))
                                        {
                                            if (fallbackResponse.IsSuccessStatusCode)
                                            {
                                                using (var contentStream = await fallbackResponse.Content.ReadAsStreamAsync())
                                                using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                                                {
                                                    await contentStream.CopyToAsync(fileStream, 81920, token);
                                                }
                                                return;
                                            }
                                        }
                                    }
                                }

                                response.EnsureSuccessStatusCode();

                                using (var contentStream = await response.Content.ReadAsStreamAsync())
                                using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                                {
                                    await contentStream.CopyToAsync(fileStream, 81920, token);
                                }
                                return; // Success!
                            }
                        }
                    }
                    catch (HttpRequestException ex) when (attempt < maxAttempts)
                    {
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

        public static List<string> DetermineImageFilenames(IList<string> imageUrls)
        {
            var filenames = new List<string>();
            if (imageUrls == null || imageUrls.Count == 0) return filenames;

            var origNames = new List<string>();
            var extensions = new List<string>();
            foreach (var url in imageUrls)
            {
                string cleanUrl = url.Split('?')[0];
                string nameWithoutExt = Path.GetFileNameWithoutExtension(cleanUrl);
                string ext = Path.GetExtension(cleanUrl);
                if (string.IsNullOrEmpty(ext)) ext = ".jpg";
                origNames.Add(nameWithoutExt);
                extensions.Add(ext);
            }

            bool useOriginal = true;
            var nums = new List<long>();
            foreach (var name in origNames)
            {
                if (long.TryParse(name, out long val))
                {
                    nums.Add(val);
                }
                else
                {
                    useOriginal = false;
                    break;
                }
            }

            if (useOriginal && nums.Count > 1)
            {
                for (int i = 1; i < nums.Count; i++)
                {
                    if (nums[i] <= nums[i - 1])
                    {
                        useOriginal = false;
                        break;
                    }
                }

                if (useOriginal)
                {
                    long range = nums[nums.Count - 1] - nums[0];
                    if (range >= nums.Count * 2)
                    {
                        useOriginal = false;
                    }
                }
            }
            else if (nums.Count <= 1)
            {
                useOriginal = true;
            }

            for (int i = 0; i < imageUrls.Count; i++)
            {
                if (useOriginal)
                {
                    filenames.Add(origNames[i] + extensions[i]);
                }
                else
                {
                    filenames.Add($"page-{(i + 1):D3}{extensions[i]}");
                }
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
                if (int.TryParse(firstPart, out rawNum))
                {
                    // Success
                }
                else
                {
                    rawNum = -1;
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
                                     .Where(f => imageExtensions.Contains(Path.GetExtension(f)))
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

            var errorsToRemove = queueItem.Errors
                .Where(e => string.Equals(e.ChapterName, chapterName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            
            foreach (var err in errorsToRemove)
            {
                queueItem.Errors.Remove(err);
            }
            queueItem.ErrorCount = queueItem.Errors.Count;

            var keysToRemove = _checkErrorIndex.Keys.Where(k => {
                if (_checkErrorIndex.TryGetValue(k, out var val))
                {
                    return string.Equals(val.BookName, queueItem.Name, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(val.ChapterName, chapterName, StringComparison.OrdinalIgnoreCase);
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
                if (host.Contains("dilib.vn")) return "dilib.vn";

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

                                string localFileWithoutExt = Path.Combine(tempFolder, $"{pageNum:D3}");
                                string finalFileWithoutExt = Path.Combine(targetFolder, $"{pageNum:D3}");
                                string downloadedPath = localFileWithoutExt;
                                var pageWatch = Stopwatch.StartNew();

                                bool exists = false;
                                string[] extensions = new string[] { ".jpg", ".png", ".jpeg", ".webp" };
                                foreach (var ext in extensions)
                                {
                                    if ((File.Exists(localFileWithoutExt + ext) && new FileInfo(localFileWithoutExt + ext).Length > 1024) ||
                                        (File.Exists(finalFileWithoutExt + ext) && new FileInfo(finalFileWithoutExt + ext).Length > 1024))
                                    {
                                        exists = true;
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

                                string localFilePath = Path.Combine(tempFolder, $"{pageNum:D3}.jpg");

                                // Fetch hentaiera viewer page to extract image source
                                // e.g. https://hentaiera.com/view/315003/1
                                downloadedPath = await DownloadHentaieraPageAsync(item, galleryId, pageNum, localFilePath, token);

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

        private async Task<string> DownloadHentaieraPageAsync(GalleryItem item, string galleryId, int pageNum, string targetPath, CancellationToken token)
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

                string finalPath = targetPath;
                if (!string.IsNullOrEmpty(actualExt) && !targetPath.EndsWith(actualExt, StringComparison.OrdinalIgnoreCase))
                {
                    finalPath = Path.ChangeExtension(targetPath, actualExt);
                }

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
            using (var response = await _httpClient.GetAsync(url, token))
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
        }
    }
}

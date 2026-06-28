using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace get_link_manga
{
    public class ErrorDetail
    {
        public string ChapterName { get; set; }
        public int PageNumber { get; set; }
        public string PageName { get; set; }
        public string ErrorMessage { get; set; }
        public string ImageUrl { get; set; }
        public string ChapterUrl { get; set; }
        public int AttemptCount { get; set; } = 0;

        public override string ToString()
        {
            string pageStr = !string.IsNullOrEmpty(PageName) ? PageName : PageNumber.ToString();
            return $"{ChapterName}, Trang {pageStr} — {ErrorMessage}";
        }
    }

    public class CheckErrorItem : INotifyPropertyChanged
    {
        private DateTime _lastSeen;
        private string _source;
        private string _bookName;
        private string _chapterName;
        private int _pageNumber;
        private string _errorMessage;
        private string _imageUrl;
        private int _occurrenceCount = 1;

        public DateTime LastSeen
        {
            get => _lastSeen;
            set
            {
                if (_lastSeen != value)
                {
                    _lastSeen = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Source
        {
            get => _source;
            set { if (_source != value) { _source = value; OnPropertyChanged(); } }
        }

        public string BookName
        {
            get => _bookName;
            set
            {
                if (_bookName != value)
                {
                    _bookName = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayBook));
                }
            }
        }

        public string ChapterName
        {
            get => _chapterName;
            set
            {
                if (_chapterName != value)
                {
                    _chapterName = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayBook));
                }
            }
        }

        public int PageNumber
        {
            get => _pageNumber;
            set
            {
                if (_pageNumber != value)
                {
                    _pageNumber = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PageDisplay));
                    OnPropertyChanged(nameof(DisplayBook));
                }
            }
        }

        private string _pageName;
        public string PageName
        {
            get => _pageName;
            set
            {
                if (_pageName != value)
                {
                    _pageName = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PageDisplay));
                    OnPropertyChanged(nameof(DisplayBook));
                }
            }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set { if (_errorMessage != value) { _errorMessage = value; OnPropertyChanged(); } }
        }

        public string ImageUrl
        {
            get => _imageUrl;
            set { if (_imageUrl != value) { _imageUrl = value; OnPropertyChanged(); } }
        }

        public int OccurrenceCount
        {
            get => _occurrenceCount;
            set { if (_occurrenceCount != value) { _occurrenceCount = value; OnPropertyChanged(); } }
        }

        public string PageDisplay => !string.IsNullOrEmpty(PageName) ? PageName : (PageNumber > 0 ? PageNumber.ToString() : "-");

        public string DisplayBook
        {
            get
            {
                var parts = new List<string>();

                if (!string.IsNullOrWhiteSpace(BookName) && BookName != "-")
                {
                    parts.Add(BookName.Trim());
                }

                if (!string.IsNullOrWhiteSpace(ChapterName) && ChapterName != "-")
                {
                    parts.Add(ChapterName.Trim());
                }

                string pageStr = !string.IsNullOrEmpty(PageName) ? PageName : (PageNumber > 0 ? PageNumber.ToString() : null);
                if (pageStr != null)
                {
                    parts.Add($"trang {pageStr}");
                }

                return parts.Count == 0 ? "-" : string.Join(" - ", parts);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class GalleryItem : INotifyPropertyChanged
    {
        private bool _isChecked;
        private string _link;
        private string _name;
        private int _originalIndex;
        private string _linkCount = "";
        private bool _isDuplicate;
        private bool _hasNoChapters;

        // Downloading status fields
        private string _sourceDomain;
        private int _totalChapters;
        private int _completedChapters;
        private string _status;
        private string _currentProcess;
        private int _errorCount;
        private string _downloadPath;
        private string _chapterSelectionText;
        private double _progressPercent;
        private double _downloadProgressPercent;
        private long _downloadSpeedBytesPerSecond;
        internal long _downloadedBytesAccumulator;
        private int _connectionCount = 1;
        private int _multiDownloadCount = 2;
        private List<ErrorDetail> _errors = new List<ErrorDetail>();
        private bool _isPaused;
        private bool _isStopped;
        private string _downloadingChapter;
        private string _downloadingPageProgress;
        private string _downloadingPageLink;
        private int _nhentaiTotalPagesHint;

        public bool HasNoChapters
        {
            get => _hasNoChapters;
            set
            {
                if (_hasNoChapters != value)
                {
                    _hasNoChapters = value;
                    OnPropertyChanged();
                }
            }
        }

        public int NhentaiTotalPagesHint
        {
            get => _nhentaiTotalPagesHint;
            set
            {
                if (_nhentaiTotalPagesHint != value)
                {
                    _nhentaiTotalPagesHint = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsDuplicate
        {
            get => _isDuplicate;
            set
            {
                if (_isDuplicate != value)
                {
                    _isDuplicate = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayStatusText));
                    OnPropertyChanged(nameof(ShouldShowStatus));
                    OnPropertyChanged(nameof(DisplayDownloadingChapter));
                    OnPropertyChanged(nameof(DisplayDownloadingPageProgress));
                    OnPropertyChanged(nameof(ShouldShowProcess));
                }
            }
        }

        public string Link
        {
            get => _link;
            set
            {
                if (_link != value)
                {
                    _link = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }

        public string DisplayName => NormalizeDisplayText(Name);

        public string DisplayLink => NormalizeDisplayText(Link);

        public int OriginalIndex
        {
            get => _originalIndex;
            set
            {
                if (_originalIndex != value)
                {
                    _originalIndex = value;
                    OnPropertyChanged();
                }
            }
        }

        public string LinkCount
        {
            get => _linkCount;
            set
            {
                if (_linkCount != value)
                {
                    _linkCount = value;
                    OnPropertyChanged();
                }
            }
        }

        public string LatestChapterDisplay
        {
            get
            {
                string value = NormalizeDisplayText(LinkCount);
                return string.IsNullOrWhiteSpace(value) ? string.Empty : $"{GetLocalizedLabel("latest", "moi nhat")}: {value}";
            }
        }

        // New properties for downloading
        public string SourceDomain
        {
            get => _sourceDomain;
            set { if (_sourceDomain != value) { _sourceDomain = value; OnPropertyChanged(); } }
        }

        public int TotalChapters
        {
            get => _totalChapters;
            set { if (_totalChapters != value) { _totalChapters = value; OnPropertyChanged(); } }
        }

        public int CompletedChapters
        {
            get => _completedChapters;
            set
            {
                if (_completedChapters != value)
                {
                    _completedChapters = value;
                    OnPropertyChanged();
                    if (_totalChapters > 0)
                    {
                        ProgressPercent = ((double)_completedChapters / _totalChapters) * 100;
                    }
                }
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusSortOrder));
                    OnPropertyChanged(nameof(DisplayStatusText));
                    OnPropertyChanged(nameof(ShouldShowStatus));
                    OnPropertyChanged(nameof(DisplayDownloadingChapter));
                    OnPropertyChanged(nameof(DisplayDownloadingPageProgress));
                    OnPropertyChanged(nameof(ShouldShowProcess));
                }
            }
        }

        public string CurrentProcess
        {
            get => _currentProcess;
            set
            {
                if (_currentProcess != value)
                {
                    _currentProcess = value;
                    OnPropertyChanged();

                    if (string.IsNullOrEmpty(value))
                    {
                        DownloadingChapter = "";
                        DownloadingPageProgress = "";
                    }
                    else if (value.Contains(" (trang "))
                    {
                        int idx = value.IndexOf(" (trang ");
                        DownloadingChapter = value.Substring(0, idx).Trim();
                        string p = value.Substring(idx + " (trang ".Length).Replace(")", "").Trim();
                        DownloadingPageProgress = $"Trang {p}";
                        _currentProcess = string.IsNullOrWhiteSpace(DownloadingPageProgress)
                            ? DownloadingChapter
                            : $"{DownloadingChapter} | {DownloadingPageProgress}";
                    }
                    else if (value.StartsWith("Trang ") || value.Contains("/"))
                    {
                        if (value.EndsWith(" pages"))
                        {
                            DownloadingPageProgress = $"Trang {value.Replace(" pages", "").Trim()}";
                        }
                        else
                        {
                            DownloadingPageProgress = value;
                        }
                        if (string.IsNullOrWhiteSpace(DownloadingChapter))
                        {
                            DownloadingChapter = "General";
                        }
                        _currentProcess = string.IsNullOrWhiteSpace(DownloadingChapter)
                            ? DownloadingPageProgress
                            : $"{DownloadingChapter} | {DownloadingPageProgress}";
                    }
                    else if (value.StartsWith("Retrying", StringComparison.OrdinalIgnoreCase) ||
                             value.StartsWith("Retry:", StringComparison.OrdinalIgnoreCase))
                    {
                        DownloadingChapter = "Retrying errors";
                        if (value.StartsWith("Retry:", StringComparison.OrdinalIgnoreCase))
                        {
                            DownloadingPageProgress = value.Replace("Retry:", "").Trim();
                        }
                        else if (value.StartsWith("Retrying errors", StringComparison.OrdinalIgnoreCase))
                        {
                            DownloadingPageProgress = value.Substring("Retrying errors".Length).Trim();
                        }
                        else
                        {
                            DownloadingPageProgress = value.Substring("Retrying".Length).Trim();
                        }
                        _currentProcess = $"{DownloadingChapter} | {DownloadingPageProgress}";
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(DownloadingChapter))
                        {
                            DownloadingChapter = value;
                        }
                        DownloadingPageProgress = value;
                    }

                    OnPropertyChanged(nameof(DisplayDownloadingChapter));
                    OnPropertyChanged(nameof(DisplayDownloadingPageProgress));
                    OnPropertyChanged(nameof(ProcessSortText));
                    OnPropertyChanged(nameof(ShouldShowProcess));
                }
            }
        }

        public int ErrorCount
        {
            get => _errorCount;
            set { if (_errorCount != value) { _errorCount = value; OnPropertyChanged(); } }
        }

        public string DownloadPath
        {
            get => _downloadPath;
            set { if (_downloadPath != value) { _downloadPath = value; OnPropertyChanged(); } }
        }

        public string ChapterSelectionText
        {
            get => _chapterSelectionText;
            set
            {
                if (_chapterSelectionText != value)
                {
                    _chapterSelectionText = value;
                    OnPropertyChanged();
                }
            }
        }

        public double ProgressPercent
        {
            get => _progressPercent;
            set { if (_progressPercent != value) { _progressPercent = value; OnPropertyChanged(); } }
        }

        public double DownloadProgressPercent
        {
            get => _downloadProgressPercent;
            set { if (_downloadProgressPercent != value) { _downloadProgressPercent = value; OnPropertyChanged(); OnPropertyChanged(nameof(DownloadProgressText)); } }
        }

        public long DownloadSpeedBytesPerSecond
        {
            get => _downloadSpeedBytesPerSecond;
            set
            {
                if (_downloadSpeedBytesPerSecond != value)
                {
                    _downloadSpeedBytesPerSecond = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DownloadSpeedText));
                    OnPropertyChanged(nameof(DownloadSpeedSortValue));
                }
            }
        }

        public long DownloadSpeedSortValue => _downloadSpeedBytesPerSecond;

        public string DownloadProgressText => $"{DownloadProgressPercent:F0}%";

        public string DownloadSpeedText => FormatSpeedText(_downloadSpeedBytesPerSecond);

        public int ConnectionCount
        {
            get => _connectionCount;
            set
            {
                if (_connectionCount != value)
                {
                    _connectionCount = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ConnectionDisplayText));
                }
            }
        }

        public int MultiDownloadCount
        {
            get => _multiDownloadCount;
            set { if (_multiDownloadCount != value) { _multiDownloadCount = value; OnPropertyChanged(); } }
        }

        public List<ErrorDetail> Errors
        {
            get => _errors;
            set { if (_errors != value) { _errors = value; OnPropertyChanged(); } }
        }

        public string ConnectionDisplayText => $"{ConnectionCount} conn";

        public static string FormatSpeedText(long bytesPerSecond)
        {
            if (bytesPerSecond <= 0)
            {
                return "-";
            }

            double value = bytesPerSecond;
            string unit = "B/s";
            if (value >= 1024)
            {
                value /= 1024;
                unit = "KB/s";
            }
            if (value >= 1024)
            {
                value /= 1024;
                unit = "MB/s";
            }
            if (value >= 1024)
            {
                value /= 1024;
                unit = "GB/s";
            }

            return value >= 10 ? $"{value:0} {unit}" : $"{value:0.0} {unit}";
        }

        public bool IsPaused
        {
            get => _isPaused;
            set { if (_isPaused != value) { _isPaused = value; OnPropertyChanged(); } }
        }

        public bool IsStopped
        {
            get => _isStopped;
            set { if (_isStopped != value) { _isStopped = value; OnPropertyChanged(); } }
        }

        public string DownloadingChapter
        {
            get => _downloadingChapter;
            set
            {
                if (_downloadingChapter != value)
                {
                    _downloadingChapter = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayDownloadingChapter));
                    OnPropertyChanged(nameof(ProcessSortText));
                    OnPropertyChanged(nameof(ShouldShowProcess));
                }
            }
        }

        public string DownloadingPageProgress
        {
            get => _downloadingPageProgress;
            set
            {
                if (_downloadingPageProgress != value)
                {
                    _downloadingPageProgress = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayDownloadingPageProgress));
                    OnPropertyChanged(nameof(ProcessSortText));
                    OnPropertyChanged(nameof(ShouldShowProcess));
                }
            }
        }

        private bool ShouldHideQueuedDisplay =>
            !_isChecked &&
            string.Equals(_status, "Queued", StringComparison.OrdinalIgnoreCase);

        private static bool IsVietnameseUiEnabled()
        {
            try
            {
                return System.Windows.Application.Current?.Properties["IsVietnameseUi"] is bool isVietnamese && isVietnamese;
            }
            catch
            {
                return false;
            }
        }

        private static string GetLocalizedLabel(string english, string vietnamese)
        {
            return IsVietnameseUiEnabled() ? vietnamese : english;
        }

        private static string NormalizeDisplayText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value ?? string.Empty;
            }

            if (!LooksMojibake(value))
            {
                return value;
            }

            try
            {
                string fixedValue = Encoding.UTF8.GetString(Encoding.GetEncoding(1252).GetBytes(value));
                return CountMojibakeMarkers(fixedValue) < CountMojibakeMarkers(value) ? fixedValue : value;
            }
            catch
            {
                return value;
            }
        }

        private static int CountMojibakeMarkers(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return 0;
            }

            return CountMarker(value, "Ã") +
                   CountMarker(value, "Â") +
                   CountMarker(value, "â") +
                   CountMarker(value, "ð") +
                   CountMarker(value, "ï¿½") +
                   CountMarker(value, "�");
        }

        private static bool LooksMojibake(string value)
        {
            return value.Contains("Ã") ||
                   value.Contains("Â") ||
                   value.Contains("â") ||
                   value.Contains("ð") ||
                   value.Contains("ï¿½") ||
                   value.Contains("�");
        }

        private static int CountMarker(string value, string marker)
        {
            int score = 0;
            int index = 0;
            while ((index = value.IndexOf(marker, index, StringComparison.Ordinal)) >= 0)
            {
                score++;
                index += marker.Length;
            }

            return score;
        }

        private static string NormalizePageProgressText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string trimmed = value.Trim();
            string lower = trimmed.ToLowerInvariant();

            if (lower.StartsWith("trang "))
            {
                string pageValue = trimmed.Substring("trang ".Length).Trim();
                return $"{GetLocalizedLabel("Page", "Trang")} {pageValue}";
            }

            if (lower.StartsWith("page "))
            {
                string pageValue = trimmed.Substring("page ".Length).Trim();
                return $"{GetLocalizedLabel("Page", "Trang")} {pageValue}";
            }

            if (trimmed.Contains("/"))
            {
                return $"{GetLocalizedLabel("Page", "Trang")} {trimmed}";
            }

            return trimmed;
        }

        public bool ShouldShowStatus =>
            !string.IsNullOrWhiteSpace(DisplayStatusText);

        public string DisplayStatusText
        {
            get
            {
                if (ShouldHideQueuedDisplay)
                {
                    return string.Empty;
                }

                switch (_status)
                {
                    case "Queued":
                        return IsVietnameseUiEnabled() ? "Cho tai" : "Waiting";
                    case "Downloading":
                        return IsVietnameseUiEnabled() ? "Dang tai" : "Downloading";
                    case "Completed":
                        return IsVietnameseUiEnabled() ? "Hoan tat" : "Done";
                    case "Error":
                        return IsVietnameseUiEnabled() ? "Loi" : "Error";
                    case "Paused":
                        return IsVietnameseUiEnabled() ? "Tam dung" : "Paused";
                    case "Stopping":
                        return IsVietnameseUiEnabled() ? "Dang dung" : "Stopping";
                    case "Cancelled":
                        return IsVietnameseUiEnabled() ? "Da dung" : "Stopped";
                    default:
                        return string.IsNullOrWhiteSpace(_status) ? string.Empty : _status;
                }
            }
        }

        public string DownloadingPageLink
        {
            get => _downloadingPageLink;
            set
            {
                if (_downloadingPageLink != value)
                {
                    _downloadingPageLink = value;
                    OnPropertyChanged();
                }
            }
        }

        public int StatusSortOrder
        {
            get
            {
                switch ((_status ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "downloading":
                        return 1;
                    case "queued":
                        return 2;
                    case "paused":
                        return 3;
                    case "error":
                        return 4;
                    case "completed":
                        return 5;
                    case "cancelled":
                        return 6;
                    case "stopping":
                        return 7;
                    default:
                        return 99;
                }
            }
        }

        public string DisplayDownloadingChapter =>
            ShouldHideQueuedDisplay ? string.Empty : _downloadingChapter;

        public string DisplayDownloadingPageProgress =>
            ShouldHideQueuedDisplay ? string.Empty : NormalizePageProgressText(_downloadingPageProgress);

        public string ProcessSortText =>
            $"{DisplayDownloadingChapter ?? string.Empty}|{DisplayDownloadingPageProgress ?? string.Empty}";

        public bool ShouldShowProcess =>
            !string.IsNullOrWhiteSpace(DisplayDownloadingChapter) ||
            !string.IsNullOrWhiteSpace(DisplayDownloadingPageProgress);

        public void RefreshDisplayText()
        {
            OnPropertyChanged(nameof(DisplayStatusText));
            OnPropertyChanged(nameof(ShouldShowStatus));
            OnPropertyChanged(nameof(DisplayDownloadingChapter));
            OnPropertyChanged(nameof(DisplayDownloadingPageProgress));
            OnPropertyChanged(nameof(ShouldShowProcess));
        }

        public List<ErrorDetail> GetUniqueErrors()
        {
            var uniqueErrors = new List<ErrorDetail>();
            if (Errors == null || Errors.Count == 0)
            {
                return uniqueErrors;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var error in Errors)
            {
                if (error == null)
                {
                    continue;
                }

                string domainVal = SourceDomain ?? "";
                if (string.IsNullOrEmpty(domainVal) && !string.IsNullOrEmpty(Link))
                {
                    try
                    {
                        domainVal = new Uri(Link).Host;
                    }
                    catch {}
                }

                if ((domainVal.Contains("truyenqq") || domainVal.Contains("hentai2read") || domainVal.Contains("nhentai")) &&
                    string.Equals(error.ChapterName, "general", StringComparison.OrdinalIgnoreCase) &&
                    error.PageNumber == 0)
                {
                    continue;
                }

                string key = BuildErrorKey(error.ChapterName, error.PageNumber);
                if (seen.Add(key))
                {
                    uniqueErrors.Add(error);
                }
            }

            return uniqueErrors;
        }

        public int GetUniqueErrorCount()
        {
            return GetUniqueErrors().Count;
        }

        public bool HasAnyErrors()
        {
            return GetUniqueErrorCount() > 0;
        }

        public bool IsSuccessfullyCompleted()
        {
            return string.Equals(Status, "Completed", StringComparison.OrdinalIgnoreCase) &&
                   !HasAnyErrors() &&
                   !string.Equals(CurrentProcess, "Done with errors", StringComparison.OrdinalIgnoreCase);
        }

        public void AddError(string chapterName, int pageNumber, string errorMessage, string imageUrl = null, string chapterUrl = null, string pageName = null, int attemptCount = 0)
        {
            string domainVal = SourceDomain ?? "";
            if (string.IsNullOrEmpty(domainVal) && !string.IsNullOrEmpty(Link))
            {
                try
                {
                    domainVal = new Uri(Link).Host;
                }
                catch {}
            }

            if ((domainVal.Contains("truyenqq") || domainVal.Contains("hentai2read") || domainVal.Contains("nhentai")) &&
                string.Equals((chapterName ?? string.Empty).Trim(), "general", StringComparison.OrdinalIgnoreCase) &&
                pageNumber == 0)
            {
                return;
            }

            if (Errors == null)
            {
                IdentityErrorsInitialization();
            }

            var existing = Errors.FirstOrDefault(e =>
                e != null &&
                string.Equals((e.ChapterName ?? string.Empty).Trim(), (chapterName ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase) &&
                e.PageNumber == pageNumber);

            if (existing != null)
            {
                existing.ErrorMessage = errorMessage;
                existing.ImageUrl = imageUrl;
                existing.ChapterUrl = chapterUrl;
                existing.AttemptCount = attemptCount;
                if (!string.IsNullOrEmpty(pageName))
                {
                    existing.PageName = pageName;
                }
            }
            else
            {
                Errors.Add(new ErrorDetail
                {
                    ChapterName = chapterName,
                    PageNumber = pageNumber,
                    PageName = pageName,
                    ErrorMessage = errorMessage,
                    ImageUrl = imageUrl,
                    ChapterUrl = chapterUrl,
                    AttemptCount = attemptCount
                });
            }

            ErrorCount = GetUniqueErrorCount();
        }

        private void IdentityErrorsInitialization()
        {
            Errors = new List<ErrorDetail>();
        }

        private static string BuildErrorKey(string chapterName, int pageNumber)
        {
            return $"{(chapterName ?? string.Empty).Trim().ToUpperInvariant()}::{pageNumber}";
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

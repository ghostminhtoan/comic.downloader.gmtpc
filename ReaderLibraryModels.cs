using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace get_link_manga
{
    internal interface IReaderWatchCheckable
    {
        bool IsChecked { get; set; }
    }

    internal sealed class UiZoomPreset
    {
        public UiZoomPreset(int percent)
        {
            Percent = percent;
        }

        public int Percent { get; }

        public override string ToString()
        {
            return Percent + "%";
        }
    }

    internal sealed class ReaderPageItem : IReaderWatchCheckable
    {
        public int Index { get; set; }

        public string Name { get; set; }

        public string FilePath { get; set; }

        public DateTime LastModifiedUtc { get; set; }

        public bool IsChecked { get; set; }

        public string DisplayLabel => $"Page {Index + 1:000} - {Name}";

        public override string ToString()
        {
            return DisplayLabel;
        }
    }

    internal sealed class ReaderMarkdownItem : IReaderWatchCheckable
    {
        public int Index { get; set; }

        public string Name { get; set; }

        public string FilePath { get; set; }

        public DateTime LastModifiedUtc { get; set; }

        public bool IsChecked { get; set; }

        public string DisplayLabel => $"MD {Index + 1:000} - {Name}";

        public override string ToString()
        {
            return DisplayLabel;
        }
    }

    internal sealed class ReaderChapterItem : IReaderWatchCheckable
    {
        public string Name { get; set; }

        public string FolderPath { get; set; }

        public int FolderDepth { get; set; }

        public DateTime LastModifiedUtc { get; set; }

        public List<ReaderPageItem> Pages { get; set; } = new List<ReaderPageItem>();

        public bool IsChecked { get; set; }

        public bool IsCompleted { get; set; }

        public double? ParsedChapterNumber { get; set; }

        public bool IsDecimalChapter { get; set; }

        public bool HasMissingIntegerGap { get; set; }

        public Brush DisplayForeground { get; set; }

        public string DisplayLabel
        {
            get
            {
                return $"{Name} ({Pages.Count} page{(Pages.Count == 1 ? string.Empty : "s")})";
            }
        }

        public override string ToString()
        {
            return DisplayLabel;
        }
    }

    internal sealed class ReaderMangaItem : IReaderWatchCheckable
    {
        public string Name { get; set; }

        public string SourceGroup { get; set; }

        public string FolderPath { get; set; }

        public int FolderDepth { get; set; }

        public bool IsNavigationItem { get; set; }

        public string NavigationTargetFolderPath { get; set; }

        public DateTime LastModifiedUtc { get; set; }

        public List<ReaderChapterItem> Chapters { get; set; } = new List<ReaderChapterItem>();

        public bool IsChecked { get; set; }

        public bool IsCompleted { get; set; }

        public string DownloadStateText { get; set; }

        public bool HasMissingIntegerGap { get; set; }

        public Brush DisplayForeground => IsNavigationItem ? Brushes.Yellow : (HasMissingIntegerGap ? Brushes.Cyan : null);

        public string DisplayLabel
        {
            get
            {
                if (IsNavigationItem)
                {
                    return "..";
                }

                string prefix = string.IsNullOrWhiteSpace(SourceGroup) ? string.Empty : SourceGroup + " - ";
                return $"{prefix}{Name} ({Chapters.Count} chap{(Chapters.Count == 1 ? string.Empty : "ters")})";
            }
        }

        public override string ToString()
        {
            return DisplayLabel;
        }
    }

    internal sealed class ReaderDomainItem : IReaderWatchCheckable
    {
        public string Name { get; set; }

        public string FolderPath { get; set; }

        public DateTime LastModifiedUtc { get; set; }

        public List<ReaderMangaItem> Books { get; set; } = new List<ReaderMangaItem>();

        public bool IsChecked { get; set; }

        public string DownloadStateText { get; set; }

        public string DisplayLabel
        {
            get
            {
                return $"{Name} ({Books.Count} book{(Books.Count == 1 ? string.Empty : "s")})";
            }
        }

        public override string ToString()
        {
            return DisplayLabel;
        }
    }

    internal sealed class ReaderNovelChapterItem : IReaderWatchCheckable
    {
        public string Name { get; set; }

        public string FolderPath { get; set; }

        public int FolderDepth { get; set; }

        public DateTime LastModifiedUtc { get; set; }

        public List<ReaderMarkdownItem> Files { get; set; } = new List<ReaderMarkdownItem>();

        public bool IsChecked { get; set; }

        public string DisplayLabel => $"{Name} ({Files.Count} md)";

        public override string ToString()
        {
            return DisplayLabel;
        }
    }

    internal sealed class ReaderNovelBookItem : IReaderWatchCheckable
    {
        public string Name { get; set; }

        public string SourceGroup { get; set; }

        public string FolderPath { get; set; }

        public int FolderDepth { get; set; }

        public bool IsNavigationItem { get; set; }

        public string NavigationTargetFolderPath { get; set; }

        public DateTime LastModifiedUtc { get; set; }

        public List<ReaderNovelChapterItem> Chapters { get; set; } = new List<ReaderNovelChapterItem>();

        public bool IsChecked { get; set; }

        public Brush DisplayForeground => IsNavigationItem ? Brushes.Yellow : null;

        public string DisplayLabel
        {
            get
            {
                if (IsNavigationItem)
                {
                    return "..";
                }

                string prefix = string.IsNullOrWhiteSpace(SourceGroup) ? string.Empty : SourceGroup + " - ";
                return $"{prefix}{Name} ({Chapters.Count} chapter{(Chapters.Count == 1 ? string.Empty : "s")})";
            }
        }

        public override string ToString()
        {
            return DisplayLabel;
        }
    }

    internal sealed class ReaderNovelDomainItem : IReaderWatchCheckable
    {
        public string Name { get; set; }

        public string FolderPath { get; set; }

        public DateTime LastModifiedUtc { get; set; }

        public List<ReaderNovelBookItem> Books { get; set; } = new List<ReaderNovelBookItem>();

        public bool IsChecked { get; set; }

        public string DisplayLabel => $"{Name} ({Books.Count} book{(Books.Count == 1 ? string.Empty : "s")})";

        public override string ToString()
        {
            return DisplayLabel;
        }
    }

    internal sealed class ReaderChapterIssueItem : IReaderWatchCheckable
    {
        public string BookName { get; set; }

        public string ChapterLabel { get; set; }

        public string MissingChapterLabel { get; set; }

        public string DecimalChapterLabel { get; set; }

        public ReaderChapterItem ChapterTarget { get; set; }

        public ReaderChapterItem MissingTarget { get; set; }

        public ReaderChapterItem DecimalTarget { get; set; }

        public bool IsChecked { get; set; }

        public bool HasMissingChapter => !string.IsNullOrWhiteSpace(MissingChapterLabel);

        public bool HasDecimalChapter => !string.IsNullOrWhiteSpace(DecimalChapterLabel);
    }
}

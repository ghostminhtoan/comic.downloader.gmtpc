using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private Border CreateReaderWatchPanel(string title, UIElement content, params Button[] sortButtons)
        {
            if (content is FrameworkElement element)
            {
                element.HorizontalAlignment = HorizontalAlignment.Stretch;
                element.VerticalAlignment = VerticalAlignment.Stretch;
            }

            var panel = new Border
            {
                Background = (Brush)TryFindResource("CyberpunkCardBrush") ?? new SolidColorBrush(Color.FromRgb(0x0D, 0x12, 0x1F)),
                BorderBrush = (Brush)TryFindResource("CyberpunkBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 10),
                Child = new Grid
                {
                    RowDefinitions =
                    {
                        new RowDefinition { Height = GridLength.Auto },
                        new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }
                    },
                    Children =
                    {
                        new Grid
                        {
                            Margin = new Thickness(0, 0, 0, 8),
                            ColumnDefinitions =
                            {
                                new ColumnDefinition { Width = GridLength.Auto },
                                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                            },
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = title,
                                    Foreground = (Brush)TryFindResource("CyberpunkYellowBrush") ?? Brushes.Yellow,
                                    FontSize = 13,
                                    FontWeight = FontWeights.ExtraBold,
                                    VerticalAlignment = VerticalAlignment.Center,
                                    Margin = new Thickness(0, 0, 8, 0)
                                }
                            }
                        },
                        content
                    }
                }
            };

            if (sortButtons != null && sortButtons.Length > 0)
            {
                var headerGrid = panel.Child as Grid;
                var header = headerGrid != null && headerGrid.Children.Count > 0 ? headerGrid.Children[0] as Grid : null;
                if (header != null)
                {
                    var sortHost = new WrapPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right
                    };
                    Grid.SetColumn(sortHost, 1);

                    foreach (Button sortButton in sortButtons)
                    {
                        if (sortButton == null)
                        {
                            continue;
                        }

                        sortHost.Children.Add(sortButton);
                    }

                    header.Children.Add(sortHost);
                }
            }

            if (content != null)
            {
                Grid.SetRow(content, 1);
            }

            return panel;
        }

        private List<ReaderDomainItem> BuildReaderDomainItems(IReadOnlyList<ReaderMangaItem> books)
        {
            var domains = new List<ReaderDomainItem>();
            if (books == null || books.Count == 0)
            {
                return domains;
            }

            IEnumerable<IGrouping<string, ReaderMangaItem>> groupedBooks = books
                .GroupBy(item => string.IsNullOrWhiteSpace(item.SourceGroup) ? "root" : item.SourceGroup, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

            foreach (IGrouping<string, ReaderMangaItem> group in groupedBooks)
            {
                List<ReaderMangaItem> domainBooks = group.ToList();
                domains.Add(new ReaderDomainItem
                {
                    Name = group.Key,
                    FolderPath = GetReaderCommonParentFolder(domainBooks.Select(item => item.FolderPath)),
                    LastModifiedUtc = domainBooks.Count == 0 ? DateTime.MinValue : domainBooks.Max(item => item.LastModifiedUtc),
                    Books = domainBooks,
                    DownloadStateText = GetReaderDomainDownloadStateText(domainBooks)
                });
            }

            return domains;
        }

        private static string GetReaderDomainDownloadStateText(IEnumerable<ReaderMangaItem> books)
        {
            if (books == null)
            {
                return null;
            }

            bool hasDownloading = false;
            bool hasCompleted = false;

            foreach (ReaderMangaItem book in books)
            {
                string stateText = (book?.DownloadStateText ?? string.Empty).Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(stateText))
                {
                    if (book != null && book.IsCompleted)
                    {
                        hasCompleted = true;
                    }

                    continue;
                }

                if (stateText == "downloading")
                {
                    hasDownloading = true;
                    continue;
                }

                if (stateText == "completed")
                {
                    hasCompleted = true;
                }
            }

            if (hasDownloading)
            {
                return "downloading";
            }

            return hasCompleted ? "completed" : null;
        }

        private void UpdateReaderDomainListItems(IList<ReaderDomainItem> domains)
        {
            if (_readerDomainList == null)
            {
                return;
            }

            _readerDomainList.ItemsSource = domains;
        }

        private void SyncReaderDomainListSelection(ReaderDomainItem domain)
        {
            if (_readerDomainList == null)
            {
                return;
            }

            _readerSelectionGuard = true;
            _readerDomainList.SelectedItem = domain;
            _readerSelectionGuard = false;
        }

        private void ClearReaderDomainList()
        {
            if (_readerDomainList == null)
            {
                return;
            }

            _readerDomainList.ItemsSource = null;
            _readerDomainList.SelectedItem = null;
        }

        private void UpdateReaderBookListItems(IList<ReaderMangaItem> books)
        {
            if (_readerMangaList == null)
            {
                return;
            }

            _readerMangaList.ItemsSource = books;
        }

        private void SyncReaderBookListSelection(ReaderMangaItem book)
        {
            if (_readerMangaList == null)
            {
                return;
            }

            _readerSelectionGuard = true;
            _readerMangaList.SelectedItem = book;
            _readerSelectionGuard = false;
        }

        private void ClearReaderBookList()
        {
            if (_readerMangaList == null)
            {
                return;
            }

            _readerMangaList.ItemsSource = null;
            _readerMangaList.SelectedItem = null;
        }

        private void UpdateReaderChapterListItems(IList<ReaderChapterItem> chapters)
        {
            if (_readerChapterList == null)
            {
                return;
            }

            _readerChapterList.ItemsSource = chapters;
        }

        private void SyncReaderChapterListSelection(ReaderChapterItem chapter)
        {
            if (_readerChapterList == null)
            {
                return;
            }

            _readerSelectionGuard = true;
            _readerChapterList.SelectedItem = chapter;
            _readerSelectionGuard = false;
        }

        private void ClearReaderChapterList()
        {
            if (_readerChapterList == null)
            {
                return;
            }

            _readerChapterList.ItemsSource = null;
            _readerChapterList.SelectedItem = null;
            if (_readerChapterStatsText != null)
            {
                _readerChapterStatsText.Text = string.Empty;
            }
            if (_readerChapterIssueGrid != null)
            {
                _readerChapterIssueGrid.ItemsSource = null;
            }
            if (_readerChapterContentHost != null)
            {
                _readerChapterContentHost.Content = _readerChapterList;
            }
        }

        private void UpdateReaderFileListItems(IList<ReaderPageItem> pages)
        {
            if (_readerFileList == null)
            {
                return;
            }

            _readerFileList.ItemsSource = pages;
        }

        private void SyncReaderFileListSelection(ReaderPageItem page)
        {
            if (_readerFileList == null)
            {
                return;
            }

            _readerSelectionGuard = true;
            _readerFileList.SelectedItem = page;
            _readerSelectionGuard = false;
        }

        private void ClearReaderFileList()
        {
            if (_readerFileList == null)
            {
                return;
            }

            _readerFileList.ItemsSource = null;
            _readerFileList.SelectedItem = null;
        }

        private void UpdateReaderNovelDomainListItems(IList<ReaderNovelDomainItem> domains)
        {
            if (_readerNovelDomainList == null)
            {
                return;
            }

            _readerNovelDomainList.ItemsSource = domains;
        }

        private static string GetReaderCommonParentFolder(IEnumerable<string> paths)
        {
            if (paths == null)
            {
                return string.Empty;
            }

            foreach (string path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                try
                {
                    DirectoryInfo parent = Directory.GetParent(path);
                    if (parent != null)
                    {
                        return parent.FullName;
                    }
                }
                catch
                {
                }
            }

            return string.Empty;
        }

        private void SyncReaderNovelDomainListSelection(ReaderNovelDomainItem domain)
        {
            if (_readerNovelDomainList == null)
            {
                return;
            }

            _readerSelectionGuard = true;
            _readerNovelDomainList.SelectedItem = domain;
            _readerSelectionGuard = false;
        }

        private void ClearReaderNovelDomainList()
        {
            if (_readerNovelDomainList == null)
            {
                return;
            }

            _readerNovelDomainList.ItemsSource = null;
            _readerNovelDomainList.SelectedItem = null;
        }

        private void UpdateReaderNovelBookListItems(IList<ReaderNovelBookItem> books)
        {
            if (_readerNovelBookList == null)
            {
                return;
            }

            _readerNovelBookList.ItemsSource = books;
        }

        private void SyncReaderNovelBookListSelection(ReaderNovelBookItem book)
        {
            if (_readerNovelBookList == null)
            {
                return;
            }

            _readerSelectionGuard = true;
            _readerNovelBookList.SelectedItem = book;
            _readerSelectionGuard = false;
        }

        private void ClearReaderNovelBookList()
        {
            if (_readerNovelBookList == null)
            {
                return;
            }

            _readerNovelBookList.ItemsSource = null;
            _readerNovelBookList.SelectedItem = null;
        }

        private void UpdateReaderNovelChapterListItems(IList<ReaderNovelChapterItem> chapters)
        {
            if (_readerNovelChapterList == null)
            {
                return;
            }

            _readerNovelChapterList.ItemsSource = chapters;
        }

        private void SyncReaderNovelChapterListSelection(ReaderNovelChapterItem chapter)
        {
            if (_readerNovelChapterList == null)
            {
                return;
            }

            _readerSelectionGuard = true;
            _readerNovelChapterList.SelectedItem = chapter;
            _readerSelectionGuard = false;
        }

        private void ClearReaderNovelChapterList()
        {
            if (_readerNovelChapterList == null)
            {
                return;
            }

            _readerNovelChapterList.ItemsSource = null;
            _readerNovelChapterList.SelectedItem = null;
        }

        private void UpdateReaderNovelFileListItems(IList<ReaderMarkdownItem> files)
        {
            if (_readerNovelFileList == null)
            {
                return;
            }

            _readerNovelFileList.ItemsSource = files;
        }

        private void SyncReaderNovelFileListSelection(ReaderMarkdownItem file)
        {
            if (_readerNovelFileList == null)
            {
                return;
            }

            _readerSelectionGuard = true;
            _readerNovelFileList.SelectedItem = file;
            _readerSelectionGuard = false;
        }

        private void ClearReaderNovelFileList()
        {
            if (_readerNovelFileList == null)
            {
                return;
            }

            _readerNovelFileList.ItemsSource = null;
            _readerNovelFileList.SelectedItem = null;
            if (_readerNovelPreviewTextBox != null)
            {
                _readerNovelPreviewTextBox.Text = string.Empty;
            }
        }
    }
}

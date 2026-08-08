using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;

namespace get_link_manga
{
    public partial class DuplicateWindow : Window
    {
        private string _localFolderPath = "";
        public ObservableCollection<GalleryItem> LocalItems { get; } = new ObservableCollection<GalleryItem>();
        private ListCollectionView _localDuplicatesView;

        // Thuộc tính phục vụ liên kết XAML
        public string LocalFolderPath
        {
            get => _localFolderPath;
            set
            {
                if (_localFolderPath != value)
                {
                    _localFolderPath = value;
                    txtLocalPath.Text = value;
                }
            }
        }

        private void InitLocalTab()
        {
            // Thiết lập View cho Local list
            _localDuplicatesView = new ListCollectionView(LocalItems);
            _localDuplicatesView.Filter = item =>
            {
                if (item is GalleryItem galleryItem)
                {
                    return (galleryItem.IsDuplicate || galleryItem.IsCensorshipColorDuplicate) && MatchesLocalFilter(galleryItem);
                }
                return false;
            };

            dgLocalDuplicates.ItemsSource = _localDuplicatesView;
            lbLocalDuplicatesThumbnail.ItemsSource = _localDuplicatesView;

            // Đăng ký sự kiện scroll cho prefetch preview
            dgLocalDuplicates.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(DgLocalDuplicates_ScrollChanged));
            lbLocalDuplicatesThumbnail.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(DgLocalDuplicates_ScrollChanged));

            // Sync sorting từ main window sang local view
            var mainView = CollectionViewSource.GetDefaultView(_mainWindow._scrapedItems);
            if (mainView != null)
            {
                ((System.Collections.Specialized.INotifyCollectionChanged)mainView.SortDescriptions).CollectionChanged += LocalSortDescriptions_CollectionChanged;
                SyncLocalSortFromMain();
            }
        }

        private bool MatchesLocalFilter(GalleryItem galleryItem)
        {
            string filterText = txtLocalFilter.Text.Trim();
            return string.IsNullOrEmpty(filterText) ||
                   galleryItem.Name.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   galleryItem.Link.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void BtnBrowseLocal_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Folder Selection.",
                Title = "Chọn thư mục để quét trùng lặp"
            };

            try
            {
                var type = dialog.GetType();
                var setOptionMethod = type.GetMethod("SetOption", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (setOptionMethod != null)
                {
                    setOptionMethod.Invoke(dialog, new object[] { 0x00000020, true }); // Vista Folder selection option
                }
            }
            catch { }

            if (dialog.ShowDialog() == true)
            {
                string path = Path.GetDirectoryName(dialog.FileName);
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    LocalFolderPath = path;
                    ScanLocalFolder(path);
                }
            }
        }

        private void ScanLocalFolder(string rootPath)
        {
            LocalItems.Clear();
            try
            {
                var directories = Directory.GetDirectories(rootPath);
                int index = 0;
                foreach (var dir in directories)
                {
                    string dirName = Path.GetFileName(dir);
                    var item = new GalleryItem
                    {
                        Name = dirName,
                        Link = dir, // Dùng link làm path thư mục local
                        OriginalIndex = index++,
                        IsChecked = false
                    };
                    LocalItems.Add(item);
                }

                RecalculateLocalDuplicates();
                UpdateLocalStatus();
                ScheduleLocalDuplicatePreviewPrefetch();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi duyệt thư mục: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RecalculateLocalDuplicates()
        {
            // Reset duplicate state của LocalItems
            foreach (var item in LocalItems)
            {
                item.IsDuplicate = false;
                item.IsCensorshipColorDuplicate = false;
                item.IsCensorshipUncensoredVariant = false;
                item.IsCensorshipFullColorVariant = false;
                item.IsNumberedVariantDuplicate = false;
            }

            var groups = LocalItems
                .GroupBy(item => MainWindow.GetSimilarityCore(item.Name, false))
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .ToList();

            foreach (var group in groups)
            {
                if (group.Count() > 1)
                {
                    // Lấy suffix core
                    var suffixCores = group.Select(item => item.Name)
                        .Where(name => Regex.IsMatch(MainWindow.GetSimilarityCore(name, false), @"\b(?:chapter|chap|ch|book|vol|volume|part|pt)\s*\d+(?:[.,]\d+)?\b", RegexOptions.IgnoreCase) || Regex.IsMatch(MainWindow.GetSimilarityCore(name, false), @"(?:^|[\s_-])\d+(?:[.,]\d+)?\s*$", RegexOptions.IgnoreCase))
                        .Select(name => {
                            string core = MainWindow.GetSimilarityCore(name, false);
                            core = Regex.Replace(core, @"\b(chapter|chap|ch|book|vol|volume|part|pt)\s*\d+([.,]\d+)?\b", " ");
                            core = Regex.Replace(core, @"\b\d+([.,]\d+)?\s*$", " ");
                            core = Regex.Replace(core, @"[^a-z0-9]", string.Empty);
                            return core.Trim();
                        })
                        .Where(core => !string.IsNullOrWhiteSpace(core))
                        .Distinct(StringComparer.Ordinal)
                        .ToList();

                    string suffixVariantCore = suffixCores.Count == 1 ? suffixCores[0] : string.Empty;

                    foreach (var item in group)
                    {
                        item.IsDuplicate = true;
                        item.IsNumberedVariantDuplicate = MainWindow.IsNumberedTitleVariant(item.Name, suffixVariantCore);
                    }
                }
            }

            var censorshipColorGroups = LocalItems
                .Where(item => {
                    string link = (item.Link ?? string.Empty).ToLowerInvariant();
                    string[] hentaiDomains = { "hentaiforce", "nhentai", "hentai2read", "hentaiera", "vi-hentai", "daomeoden", "damconuong", "truyengg", "sayhentai", "hitomi" };
                    return hentaiDomains.Any(domain => link.Contains(domain)) || hentaiDomains.Any(domain => item.Name.ToLowerInvariant().Contains(domain));
                })
                .GroupBy(item => MainWindow.GetSimilarityCore(item.Name, true))
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .ToList();

            foreach (var group in censorshipColorGroups)
            {
                if (group.Count() <= 1 || !group.Any(item => MainWindow.HasCensorshipColorVariant(item.Name)))
                {
                    continue;
                }

                foreach (var item in group)
                {
                    item.IsCensorshipColorDuplicate = true;
                    item.IsCensorshipFullColorVariant = MainWindow.HasFullColorVariant(item.Name);
                    item.IsCensorshipUncensoredVariant = MainWindow.HasUncensoredVariant(item.Name);
                }
            }
        }

        private void TxtLocalFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            _localDuplicatesView.Refresh();
            UpdateLocalStatus();
            ScheduleLocalDuplicatePreviewPrefetch();
        }

        private void UpdateLocalStatus()
        {
            int totalDups = _localDuplicatesView.Cast<GalleryItem>().Count();
            int checkedDups = _localDuplicatesView.Cast<GalleryItem>().Count(item => item.IsChecked);

            lblLocalDupCount.Text = $"{checkedDups}/{totalDups}";
            lblStatus.Text = $"Local Duplicate groups active. {checkedDups} of {totalDups} duplicate items selected.";

            SetSelectAllState(chkLocalSelectAll, totalDups, checkedDups);
        }

        private void BtnLocalCheckAll_Click(object sender, RoutedEventArgs e)
        {
            var visibleItems = _localDuplicatesView.Cast<GalleryItem>().ToList();
            foreach (var item in visibleItems)
            {
                item.IsChecked = true;
            }
            _mainWindow.Log("Checked all visible local duplicates.");
            UpdateLocalStatus();
        }

        private void BtnLocalUncheckAll_Click(object sender, RoutedEventArgs e)
        {
            var visibleItems = _localDuplicatesView.Cast<GalleryItem>().ToList();
            foreach (var item in visibleItems)
            {
                item.IsChecked = false;
            }
            _mainWindow.Log("Unchecked all visible local duplicates.");
            UpdateLocalStatus();
        }

        private void ChkLocalSelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox chk)
            {
                bool isChecked = chk.IsChecked ?? false;
                var visibleItems = _localDuplicatesView.Cast<GalleryItem>().ToList();
                foreach (var item in visibleItems)
                {
                    item.IsChecked = isChecked;
                }
                _mainWindow.Log($"{(isChecked ? "Checked" : "Unchecked")} all visible local duplicates via checkbox.");
                UpdateLocalStatus();
            }
        }

        private void DgLocalDuplicates_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange == 0 && e.HorizontalChange == 0 && e.ExtentHeightChange == 0) return;
            ScheduleLocalDuplicatePreviewPrefetch();
        }

        private void ScheduleLocalDuplicatePreviewPrefetch()
        {
            _previewPrefetchTimer.Stop();
            _previewPrefetchTimer.Start();
        }

        private void PrefetchVisibleLocalDuplicatePreviews()
        {
            if (_mainWindow == null) return;
            System.Collections.Generic.List<GalleryItem> visibleItems;
            if (chkResultsPresentation?.IsChecked == true)
            {
                visibleItems = lbLocalDuplicatesThumbnail.Items
                    .Cast<GalleryItem>()
                    .Where(item => lbLocalDuplicatesThumbnail.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem)
                    .Take(40)
                    .ToList();
            }
            else
            {
                visibleItems = dgLocalDuplicates.Items
                    .Cast<GalleryItem>()
                    .Where(item => dgLocalDuplicates.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow)
                    .Take(20)
                    .ToList();
            }
            _mainWindow.PrefetchGalleryHoverPreview(visibleItems);
        }

        private void LocalSortDescriptions_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            SyncLocalSortFromMain();
            ScheduleLocalDuplicatePreviewPrefetch();
        }

        private void SyncLocalSortFromMain()
        {
            var mainView = CollectionViewSource.GetDefaultView(_mainWindow._scrapedItems);
            if (mainView != null && _localDuplicatesView != null)
            {
                _localDuplicatesView.SortDescriptions.Clear();
                foreach (SortDescription sd in mainView.SortDescriptions)
                {
                    _localDuplicatesView.SortDescriptions.Add(new SortDescription(sd.PropertyName, sd.Direction));
                }
            }
        }

        private void DeleteSelectedLocalItems()
        {
            bool isThumbnail = chkResultsPresentation?.IsChecked == true;
            var itemsToRemove = isThumbnail
                ? lbLocalDuplicatesThumbnail.SelectedItems.Cast<GalleryItem>().ToList()
                : dgLocalDuplicates.SelectedItems.Cast<GalleryItem>().ToList();

            if (itemsToRemove.Count == 0) return;

            var result = MessageBox.Show($"Bạn có chắc chắn muốn xóa {itemsToRemove.Count} thư mục cục bộ này khỏi ổ đĩa không? Hành động này không thể hoàn tác!", "Cảnh báo bảo mật", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            foreach (var item in itemsToRemove)
            {
                try
                {
                    if (Directory.Exists(item.Link))
                    {
                        Directory.Delete(item.Link, true);
                        _mainWindow.Log($"[Local Duplicates] Đã xóa thư mục cục bộ: {item.Link}");
                    }
                    LocalItems.Remove(item);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Không thể xóa {item.Link}: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            RecalculateLocalDuplicates();
            UpdateLocalStatus();
        }

        private void DeleteCheckedLocalItems()
        {
            var visibleSet = _localDuplicatesView.Cast<GalleryItem>().ToHashSet();
            var itemsToRemove = LocalItems.Where(item => item.IsChecked && visibleSet.Contains(item)).ToList();
            if (!itemsToRemove.Any()) return;

            var result = MessageBox.Show($"Bạn có chắc chắn muốn xóa {itemsToRemove.Count} thư mục cục bộ đã tích chọn khỏi ổ đĩa không? Hành động này không thể hoàn tác!", "Cảnh báo bảo mật", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            foreach (var item in itemsToRemove)
            {
                try
                {
                    if (Directory.Exists(item.Link))
                    {
                        Directory.Delete(item.Link, true);
                        _mainWindow.Log($"[Local Duplicates] Đã xóa thư mục cục bộ: {item.Link}");
                    }
                    LocalItems.Remove(item);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Không thể xóa {item.Link}: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            RecalculateLocalDuplicates();
            UpdateLocalStatus();
        }

        private void LocalPreviewRow_MouseEnter(object sender, MouseEventArgs e)
        {
            _mainWindow?.ForwardGalleryPreviewMouseEnter(sender as FrameworkElement);
        }

        private void LocalPreviewRow_MouseMove(object sender, MouseEventArgs e)
        {
            _mainWindow?.ForwardGalleryPreviewMouseMove(sender as FrameworkElement);
        }

        private void LocalPreviewRow_MouseLeave(object sender, MouseEventArgs e)
        {
            _mainWindow?.ForwardGalleryPreviewMouseLeave(sender as FrameworkElement);
        }

        private void DgLocalDuplicates_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingSelection || dgLocalDuplicates == null || lbLocalDuplicatesThumbnail == null) return;
            _isSyncingSelection = true;
            try
            {
                lbLocalDuplicatesThumbnail.SelectedItems.Clear();
                foreach (var item in dgLocalDuplicates.SelectedItems)
                {
                    lbLocalDuplicatesThumbnail.SelectedItems.Add(item);
                }
            }
            finally
            {
                _isSyncingSelection = false;
            }
        }

        private void LbLocalDuplicatesThumbnail_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingSelection || dgLocalDuplicates == null || lbLocalDuplicatesThumbnail == null) return;
            _isSyncingSelection = true;
            try
            {
                dgLocalDuplicates.SelectedItems.Clear();
                foreach (var item in lbLocalDuplicatesThumbnail.SelectedItems)
                {
                    dgLocalDuplicates.SelectedItems.Add(item);
                }
            }
            finally
            {
                _isSyncingSelection = false;
            }
        }
    }
}

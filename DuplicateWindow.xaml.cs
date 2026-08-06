using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace get_link_manga
{
    /// <summary>
    /// Interaction logic for DuplicateWindow.xaml
    /// </summary>
    public partial class DuplicateWindow : Window
    {
        private readonly MainWindow _mainWindow;
        private readonly ListCollectionView _duplicatesView;
        private string _searchBuffer = "";
        private DateTime _lastKeyPressTime = DateTime.MinValue;
        private readonly DispatcherTimer _previewPrefetchTimer;

        public DuplicateWindow(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            _previewPrefetchTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };
            _previewPrefetchTimer.Tick += PreviewPrefetchTimer_Tick;

            // Bind dgDuplicates to the main window's scraped items with a filter
            _duplicatesView = new ListCollectionView(_mainWindow._scrapedItems);
            _duplicatesView.Filter = item =>
            {
                if (item is GalleryItem galleryItem)
                {
                    return (galleryItem.IsDuplicate || galleryItem.IsCensorshipColorDuplicate) && MatchesFilter(galleryItem);
                }
                return false;
            };

            dgDuplicates.ItemsSource = _duplicatesView;
            dgDuplicates.Loaded += DgDuplicates_Loaded;
            dgDuplicates.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(DgDuplicates_ScrollChanged));
            lbDuplicatesThumbnail.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(DgDuplicates_ScrollChanged));
            UpdateStatus();

            // Sync sorting and subscribe to sort changes of the main window's view
            var mainView = CollectionViewSource.GetDefaultView(_mainWindow._scrapedItems);
            if (mainView != null)
            {
                ((System.Collections.Specialized.INotifyCollectionChanged)mainView.SortDescriptions).CollectionChanged += MainSortDescriptions_CollectionChanged;
                SyncSortFromMain();
            }

            // Hook PropertyChanged of each item to update counts if Checked changes
            foreach (var item in _mainWindow._scrapedItems)
            {
                item.PropertyChanged += GalleryItem_PropertyChanged;
            }
            
            // Also listen to list changes to hook/unhook and update counts
            _mainWindow._scrapedItems.CollectionChanged += ScrapedItems_CollectionChanged;
        }

        private void GalleryItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(GalleryItem.IsChecked) ||
                e.PropertyName == nameof(GalleryItem.IsDuplicate) ||
                e.PropertyName == nameof(GalleryItem.IsCensorshipColorDuplicate))
            {
                Dispatcher.InvokeAsync(UpdateStatus);
            }
        }

        private bool MatchesFilter(GalleryItem galleryItem)
        {
            string filterText = txtFilter.Text.Trim();
            return string.IsNullOrEmpty(filterText) ||
                   galleryItem.Name.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   galleryItem.Link.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ScrapedItems_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (GalleryItem item in e.NewItems)
                {
                    item.PropertyChanged += GalleryItem_PropertyChanged;
                }
            }
            if (e.OldItems != null)
            {
                foreach (GalleryItem item in e.OldItems)
                {
                    item.PropertyChanged -= GalleryItem_PropertyChanged;
                }
            }
            Dispatcher.InvokeAsync(UpdateStatus);
        }

        private void UpdateStatus()
        {
            int totalDups = _duplicatesView.Cast<GalleryItem>().Count();
            int checkedDups = _duplicatesView.Cast<GalleryItem>().Count(item => item.IsChecked);

            lblDupCount.Text = $"{checkedDups}/{totalDups}";
            lblStatus.Text = $"Duplicate groups active. {checkedDups} of {totalDups} duplicate items selected.";

            SetSelectAllState(chkSelectAll, totalDups, checkedDups);
        }

        private void TxtFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            _duplicatesView.Refresh();
            UpdateStatus();
            ScheduleDuplicatePreviewPrefetch();
        }

        private void BtnCheckAll_Click(object sender, RoutedEventArgs e)
        {
            var visibleItems = _duplicatesView.Cast<GalleryItem>().ToList();
            foreach (var item in visibleItems)
            {
                item.IsChecked = true;
            }
            _mainWindow.Log("Checked all visible duplicates.");
        }

        private void BtnUncheckAll_Click(object sender, RoutedEventArgs e)
        {
            var visibleItems = _duplicatesView.Cast<GalleryItem>().ToList();
            foreach (var item in visibleItems)
            {
                item.IsChecked = false;
            }
            _mainWindow.Log("Unchecked all visible duplicates.");
        }

        private void ChkSelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox chk)
            {
                bool isChecked = chk.IsChecked ?? false;
                var visibleItems = _duplicatesView.Cast<GalleryItem>().ToList();
                foreach (var item in visibleItems)
                {
                    item.IsChecked = isChecked;
                }
                _mainWindow.Log($"{(isChecked ? "Checked" : "Unchecked")} all visible duplicates via checkbox.");
            }
        }

        private void DgDuplicates_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!(sender is DataGrid grid) || grid.Items.Count == 0) return;

            if (e.Key == Key.Home)
            {
                grid.SelectedIndex = 0;
                grid.ScrollIntoView(grid.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.End)
            {
                grid.SelectedIndex = grid.Items.Count - 1;
                grid.ScrollIntoView(grid.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                DeleteSelectedItems(grid);
                e.Handled = true;
            }
        }

        private void DeleteSelectedItems(DataGrid grid = null)
        {
            DataGrid activeGrid = grid ?? dgDuplicates;
            if (activeGrid.SelectedItems.Count == 0) return;

            int selectedIndex = activeGrid.SelectedIndex;
            var itemsToRemove = activeGrid.SelectedItems.Cast<GalleryItem>().ToList();
            foreach (var item in itemsToRemove)
            {
                _mainWindow._scrapedItems.Remove(item);
            }

            _mainWindow.RecalculateDuplicates();
            _mainWindow.UpdateLinkCount();
            
            // Note: because items are removed from _mainWindow._scrapedItems,
            // they automatically trigger ScrapedItems_CollectionChanged which calls UpdateStatus.
            _mainWindow.Log($"Deleted {itemsToRemove.Count} duplicate item(s) from duplicates review.");
            lblStatus.Text = $"Deleted {itemsToRemove.Count} item(s).";

            if (activeGrid.Items.Count > 0)
            {
                int newIndex = Math.Min(selectedIndex, activeGrid.Items.Count - 1);
                if (newIndex >= 0)
                {
                    activeGrid.SelectedIndex = newIndex;
                    var item = activeGrid.SelectedItem;
                    if (item != null)
                    {
                        activeGrid.ScrollIntoView(item);
                        // Delay focus slightly to let the visual tree update after removal
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            var row = (DataGridRow)activeGrid.ItemContainerGenerator.ContainerFromItem(item);
                            if (row != null)
                            {
                                row.Focus();
                            }
                        }), System.Windows.Threading.DispatcherPriority.Background);
                    }
                }
            }
        }

        private void DgDuplicates_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text)) return;
            if (!(sender is DataGrid grid)) return;

            DateTime now = DateTime.Now;
            if ((now - _lastKeyPressTime).TotalMilliseconds > 1000)
            {
                _searchBuffer = "";
            }
            _lastKeyPressTime = now;
            _searchBuffer += e.Text;

            var items = grid.Items.Cast<GalleryItem>().ToList();
            var match = items.FirstOrDefault(item => item.Name.StartsWith(_searchBuffer, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                match = items.FirstOrDefault(item => item.Name.IndexOf(_searchBuffer, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (match != null)
            {
                grid.SelectedItem = match;
                grid.ScrollIntoView(match);

                var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(match);
                if (row != null)
                {
                    row.Focus();
                }
            }

            e.Handled = true;
        }

        private void DgDuplicates_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
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
                            FileName = item.Link,
                            UseShellExecute = true
                        });
                        _mainWindow.Log($"Opened duplicate link: {item.Link}");
                    }
                    catch (Exception ex)
                    {
                        _mainWindow.Log($"Failed to open duplicate link: {ex.Message}");
                    }
                }
            }
        }

        private void MenuCheckSelected_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in GetActiveGrid().SelectedItems.Cast<GalleryItem>())
            {
                item.IsChecked = true;
            }
        }

        private void MenuUncheckSelected_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in GetActiveGrid().SelectedItems.Cast<GalleryItem>())
            {
                item.IsChecked = false;
            }
        }

        private void MenuInvertChecked_Click(object sender, RoutedEventArgs e)
        {
            var visibleItems = _duplicatesView.Cast<GalleryItem>().ToList();
            foreach (var item in visibleItems)
            {
                item.IsChecked = !item.IsChecked;
            }
        }

        private void MenuCopySelectedLinks_Click(object sender, RoutedEventArgs e)
        {
            if (dgDuplicates.SelectedItems.Count == 0) return;
            var items = dgDuplicates.SelectedItems.Cast<GalleryItem>().ToList();
            string text = string.Join("\r\n", items.Select(item => item.Link));
            Clipboard.SetText(text);
            _mainWindow.Log($"Copied {items.Count} selected duplicate link(s) to clipboard.");
        }

        private void MenuDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelectedItems();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _previewPrefetchTimer.Stop();
            _previewPrefetchTimer.Tick -= PreviewPrefetchTimer_Tick;
            dgDuplicates.Loaded -= DgDuplicates_Loaded;
            dgDuplicates.RemoveHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(DgDuplicates_ScrollChanged));

            // Unhook collection changes to avoid memory leaks
            _mainWindow._scrapedItems.CollectionChanged -= ScrapedItems_CollectionChanged;
            foreach (var item in _mainWindow._scrapedItems)
            {
                item.PropertyChanged -= GalleryItem_PropertyChanged;
            }

            var mainView = CollectionViewSource.GetDefaultView(_mainWindow._scrapedItems);
            if (mainView != null)
            {
                ((System.Collections.Specialized.INotifyCollectionChanged)mainView.SortDescriptions).CollectionChanged -= MainSortDescriptions_CollectionChanged;
            }

            base.OnClosed(e);
        }

        private void MainSortDescriptions_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            SyncSortFromMain();
            ScheduleDuplicatePreviewPrefetch();
        }

        private void SyncSortFromMain()
        {
            var mainView = CollectionViewSource.GetDefaultView(_mainWindow._scrapedItems);
            if (mainView != null && _duplicatesView != null)
            {
                _duplicatesView.SortDescriptions.Clear();
                foreach (SortDescription sd in mainView.SortDescriptions)
                {
                    _duplicatesView.SortDescriptions.Add(new SortDescription(sd.PropertyName, sd.Direction));
                }
            }
        }

        private void MenuDeleteChecked_Click(object sender, RoutedEventArgs e)
        {
            DeleteCheckedItems();
        }

        private void DeleteCheckedItems()
        {
            var visibleSet = _duplicatesView.Cast<GalleryItem>().ToHashSet();
            var itemsToRemove = _mainWindow._scrapedItems.Where(item => item.IsChecked && visibleSet.Contains(item)).ToList();
            if (!itemsToRemove.Any()) return;

            foreach (var item in itemsToRemove)
            {
                _mainWindow._scrapedItems.Remove(item);
            }

            _mainWindow.RecalculateDuplicates();
            _mainWindow.UpdateLinkCount();
            
            _mainWindow.Log($"Deleted {itemsToRemove.Count} checked duplicate item(s) from duplicates review.");
            lblStatus.Text = $"Deleted {itemsToRemove.Count} checked item(s).";
        }

        private async void MenuDownloadSelected_Click(object sender, RoutedEventArgs e)
        {
            var items = dgDuplicates.SelectedItems.Cast<GalleryItem>().ToList();
            if (!items.Any())
            {
                MessageBox.Show("Vui lòng bôi đen chọn ít nhất 1 dòng để tải (Please select at least one highlighted line to download).", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            await _mainWindow.StartDownloadProcessAsync(items);
        }

        private async void MenuDownloadChecked_Click(object sender, RoutedEventArgs e)
        {
            var visibleSet = _duplicatesView.Cast<GalleryItem>().ToHashSet();
            var items = _mainWindow._scrapedItems.Where(item => item.IsChecked && visibleSet.Contains(item)).ToList();
            if (!items.Any())
            {
                MessageBox.Show("Vui lòng tích chọn ít nhất 1 truyện để tải (Please check at least one gallery to download).", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            await _mainWindow.StartDownloadProcessAsync(items);
        }

        private void SetSelectAllState(CheckBox checkBox, int totalItems, int checkedItems)
        {
            if (checkBox == null)
            {
                return;
            }

            if (totalItems == 0 || checkedItems == 0)
            {
                checkBox.IsChecked = false;
                return;
            }

            checkBox.IsChecked = checkedItems == totalItems ? true : (bool?)null;
        }

        private DataGrid GetActiveGrid() => dgDuplicates;

        private void DuplicatePreviewRow_MouseEnter(object sender, MouseEventArgs e)
        {
            _mainWindow?.ForwardGalleryPreviewMouseEnter(sender as FrameworkElement);
        }

        private void DuplicatePreviewRow_MouseMove(object sender, MouseEventArgs e)
        {
            _mainWindow?.ForwardGalleryPreviewMouseMove(sender as FrameworkElement);
        }

        private void DuplicatePreviewRow_MouseLeave(object sender, MouseEventArgs e)
        {
            _mainWindow?.ForwardGalleryPreviewMouseLeave(sender as FrameworkElement);
        }

        private void DgDuplicates_Loaded(object sender, RoutedEventArgs e)
        {
            ScheduleDuplicatePreviewPrefetch();
        }

        private void DgDuplicates_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange == 0 && e.HorizontalChange == 0 && e.ExtentHeightChange == 0)
            {
                return;
            }

            ScheduleDuplicatePreviewPrefetch();
        }

        private void ScheduleDuplicatePreviewPrefetch()
        {
            _previewPrefetchTimer.Stop();
            _previewPrefetchTimer.Start();
        }

        private void PreviewPrefetchTimer_Tick(object sender, EventArgs e)
        {
            _previewPrefetchTimer.Stop();
            PrefetchVisibleDuplicatePreviews();
        }

        private void PrefetchVisibleDuplicatePreviews()
        {
            if (_mainWindow == null) return;

            System.Collections.Generic.List<GalleryItem> visibleItems;
            if (chkResultsPresentation?.IsChecked == true)
            {
                visibleItems = lbDuplicatesThumbnail.Items
                    .Cast<GalleryItem>()
                    .Where(item => lbDuplicatesThumbnail.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem)
                    .Take(40)
                    .ToList();
            }
            else
            {
                visibleItems = dgDuplicates.Items
                    .Cast<GalleryItem>()
                    .Where(item => dgDuplicates.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow)
                    .Take(20)
                    .ToList();
            }

            _mainWindow.PrefetchGalleryHoverPreview(visibleItems);
        }

        private void ChkResultsPresentation_Click(object sender, RoutedEventArgs e)
        {
            if (chkResultsPresentation == null) return;
            bool isThumbnail = chkResultsPresentation.IsChecked == true;
            dgDuplicates.Visibility = isThumbnail ? Visibility.Collapsed : Visibility.Visible;
            lbDuplicatesThumbnail.Visibility = isThumbnail ? Visibility.Visible : Visibility.Collapsed;
            ScheduleDuplicatePreviewPrefetch();
        }

        private bool _isSyncingSelection = false;
        private void DgDuplicates_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingSelection || dgDuplicates == null || lbDuplicatesThumbnail == null) return;
            _isSyncingSelection = true;
            try
            {
                lbDuplicatesThumbnail.SelectedItems.Clear();
                foreach (var item in dgDuplicates.SelectedItems)
                {
                    lbDuplicatesThumbnail.SelectedItems.Add(item);
                }
            }
            finally
            {
                _isSyncingSelection = false;
            }
        }

        private void LbDuplicatesThumbnail_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingSelection || dgDuplicates == null || lbDuplicatesThumbnail == null) return;
            _isSyncingSelection = true;
            try
            {
                dgDuplicates.SelectedItems.Clear();
                foreach (var item in lbDuplicatesThumbnail.SelectedItems)
                {
                    dgDuplicates.SelectedItems.Add(item);
                }
            }
            finally
            {
                _isSyncingSelection = false;
            }
        }
    }
}

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace get_link_manga
{
    public partial class DuplicateWindow : Window
    {
        private readonly MainWindow _mainWindow;
        private readonly ListCollectionView _duplicatesView;
        private string _searchBuffer = "";
        private DateTime _lastKeyPressTime = DateTime.MinValue;
        private readonly DispatcherTimer _previewPrefetchTimer;

        // Local duplicate variables
        private readonly ObservableCollection<GalleryItem> _localItems = new ObservableCollection<GalleryItem>();
        private readonly ListCollectionView _localView;
        private string _localSearchBuffer = "";
        private DateTime _localLastKeyPressTime = DateTime.MinValue;

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
            if (_duplicatesView.CanChangeLiveFiltering)
            {
                _duplicatesView.LiveFilteringProperties.Add(nameof(GalleryItem.IsDuplicate));
                _duplicatesView.LiveFilteringProperties.Add(nameof(GalleryItem.IsCensorshipColorDuplicate));
                _duplicatesView.IsLiveFiltering = true;
            }

            dgDuplicates.ItemsSource = _duplicatesView;
            dgDuplicates.Loaded += DgDuplicates_Loaded;
            dgDuplicates.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(DgDuplicates_ScrollChanged));
            lbDuplicatesThumbnail.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(DgDuplicates_ScrollChanged));

            // Local items binding
            _localView = new ListCollectionView(_localItems);
            _localView.Filter = item =>
            {
                if (item is GalleryItem galleryItem)
                {
                    return (galleryItem.IsDuplicate || galleryItem.IsCensorshipColorDuplicate) && MatchesLocalFilter(galleryItem);
                }
                return false;
            };
            dgDuplicatesLocal.ItemsSource = _localView;

            UpdateStatus();

            // Sync sorting and subscribe to sort changes of the main window's view
            var mainView = CollectionViewSource.GetDefaultView(_mainWindow._scrapedItems);
            if (mainView != null)
            {
                ((System.Collections.Specialized.INotifyCollectionChanged)mainView.SortDescriptions).CollectionChanged += MainSortDescriptions_CollectionChanged;
                SyncSortFromMain();
            }

            // Synchronize selection changes
            dgDuplicates.SelectionChanged += DgDuplicates_SelectionChanged;
            lbDuplicatesThumbnail.SelectionChanged += LbDuplicatesThumbnail_SelectionChanged;

            if (_mainWindow.dgResults != null)
            {
                _mainWindow.dgResults.SelectionChanged += MainWindow_SelectionChanged;
            }
            if (_mainWindow.lbResultsThumbnail != null)
            {
                _mainWindow.lbResultsThumbnail.SelectionChanged += MainWindow_SelectionChanged;
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

        private bool MatchesLocalFilter(GalleryItem galleryItem)
        {
            string filterText = txtFilterLocal.Text.Trim();
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

        private void TabMain_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is TabControl)
            {
                UpdateStatus();
            }
        }

        private void UpdateStatus()
        {
            if (tabMain == null) return;
            bool isLocalTabActive = tabMain.SelectedIndex == 1;

            if (isLocalTabActive)
            {
                int totalDups = _localView.Cast<GalleryItem>().Count();
                int checkedDups = _localView.Cast<GalleryItem>().Count(item => item.IsChecked);
                lblDupCount.Text = $"{checkedDups}/{totalDups}";
                lblStatus.Text = $"[Local] Duplicate groups active. {checkedDups} of {totalDups} items selected.";
                SetSelectAllState(chkSelectAllLocal, totalDups, checkedDups);
            }
            else
            {
                int totalDups = _duplicatesView.Cast<GalleryItem>().Count();
                int checkedDups = _duplicatesView.Cast<GalleryItem>().Count(item => item.IsChecked);
                lblDupCount.Text = $"{checkedDups}/{totalDups}";
                lblStatus.Text = $"Duplicate groups active. {checkedDups} of {totalDups} duplicate items selected.";
                SetSelectAllState(chkSelectAll, totalDups, checkedDups);
            }
        }

        private void TxtFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            _duplicatesView.Refresh();
            UpdateStatus();
            ScheduleDuplicatePreviewPrefetch();
        }

        private void TxtFilterLocal_TextChanged(object sender, TextChangedEventArgs e)
        {
            _localView.Refresh();
            UpdateStatus();
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
            var isThumbnail = chkResultsPresentation?.IsChecked == true;
            var itemsCount = isThumbnail ? lbDuplicatesThumbnail.Items.Count : dgDuplicates.Items.Count;
            if (itemsCount == 0) return;

            if (e.Key == Key.Home)
            {
                if (isThumbnail)
                {
                    lbDuplicatesThumbnail.SelectedIndex = 0;
                    lbDuplicatesThumbnail.ScrollIntoView(lbDuplicatesThumbnail.SelectedItem);
                }
                else
                {
                    dgDuplicates.SelectedIndex = 0;
                    dgDuplicates.ScrollIntoView(dgDuplicates.SelectedItem);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.End)
            {
                if (isThumbnail)
                {
                    lbDuplicatesThumbnail.SelectedIndex = lbDuplicatesThumbnail.Items.Count - 1;
                    lbDuplicatesThumbnail.ScrollIntoView(lbDuplicatesThumbnail.SelectedItem);
                }
                else
                {
                    dgDuplicates.SelectedIndex = dgDuplicates.Items.Count - 1;
                    dgDuplicates.ScrollIntoView(dgDuplicates.SelectedItem);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                DeleteSelectedItems();
                e.Handled = true;
            }
            else if (e.Key == Key.Space)
            {
                var selected = isThumbnail 
                    ? lbDuplicatesThumbnail.SelectedItems.Cast<GalleryItem>().ToList()
                    : dgDuplicates.SelectedItems.Cast<GalleryItem>().ToList();

                if (selected.Count > 0)
                {
                    bool targetState = !selected[0].IsChecked;
                    foreach (var item in selected)
                    {
                        item.IsChecked = targetState;
                    }
                    e.Handled = true;
                }
            }
        }

        private void DeleteSelectedItems()
        {
            bool isThumbnail = chkResultsPresentation?.IsChecked == true;
            int selectedIndex = isThumbnail ? lbDuplicatesThumbnail.SelectedIndex : dgDuplicates.SelectedIndex;

            var itemsToRemove = GetSelectedGalleryItems();

            if (itemsToRemove.Count == 0) return;

            _isSyncingSelection = true;
            try
            {
                foreach (var item in itemsToRemove)
                {
                    _mainWindow._scrapedItems.Remove(item);
                }
            }
            finally
            {
                _isSyncingSelection = false;
            }

            _mainWindow.RecalculateDuplicates();
            _mainWindow.UpdateLinkCount();
            
            _mainWindow.Log($"Deleted {itemsToRemove.Count} duplicate item(s) from duplicates review.");
            lblStatus.Text = $"Deleted {itemsToRemove.Count} item(s).";

            if (isThumbnail)
            {
                if (lbDuplicatesThumbnail.Items.Count > 0)
                {
                    int newIndex = Math.Min(selectedIndex, lbDuplicatesThumbnail.Items.Count - 1);
                    if (newIndex >= 0)
                    {
                        lbDuplicatesThumbnail.SelectedIndex = newIndex;
                        var item = lbDuplicatesThumbnail.SelectedItem;
                        if (item != null)
                        {
                            lbDuplicatesThumbnail.ScrollIntoView(item);
                            Dispatcher.BeginInvoke(new Action(() => {
                                var container = lbDuplicatesThumbnail.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;
                                container?.Focus();
                            }), System.Windows.Threading.DispatcherPriority.Background);
                        }
                    }
                }
            }
            else
            {
                if (dgDuplicates.Items.Count > 0)
                {
                    int newIndex = Math.Min(selectedIndex, dgDuplicates.Items.Count - 1);
                    if (newIndex >= 0)
                    {
                        dgDuplicates.SelectedIndex = newIndex;
                        var item = dgDuplicates.SelectedItem;
                        if (item != null)
                        {
                            dgDuplicates.ScrollIntoView(item);
                            Dispatcher.BeginInvoke(new Action(() => {
                                var row = dgDuplicates.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
                                row?.Focus();
                            }), System.Windows.Threading.DispatcherPriority.Background);
                        }
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
            foreach (var item in GetSelectedGalleryItems())
            {
                item.IsChecked = true;
            }
        }

        private void MenuUncheckSelected_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in GetSelectedGalleryItems())
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
            var items = GetSelectedGalleryItems();
            if (items.Count == 0) return;
            string text = string.Join("\r\n", items.Select(item => item.Link));
            Clipboard.SetText(text);
            _mainWindow.Log($"Copied {items.Count} selected duplicate link(s) to clipboard.");
        }

        private void MenuDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelectedItems();
        }

        private void BtnDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (tabMain.SelectedIndex == 0)
            {
                DeleteSelectedItems();
            }
            else
            {
                DeleteSelectedItemsLocal();
            }
        }

        private void BtnDeleteChecked_Click(object sender, RoutedEventArgs e)
        {
            if (tabMain.SelectedIndex == 0)
            {
                DeleteCheckedItems();
            }
            else
            {
                DeleteCheckedItemsLocal();
            }
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

            // Unregister selection changes
            dgDuplicates.SelectionChanged -= DgDuplicates_SelectionChanged;
            lbDuplicatesThumbnail.SelectionChanged -= LbDuplicatesThumbnail_SelectionChanged;

            if (_mainWindow.dgResults != null)
            {
                _mainWindow.dgResults.SelectionChanged -= MainWindow_SelectionChanged;
            }
            if (_mainWindow.lbResultsThumbnail != null)
            {
                _mainWindow.lbResultsThumbnail.SelectionChanged -= MainWindow_SelectionChanged;
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

            _isSyncingSelection = true;
            try
            {
                foreach (var item in itemsToRemove)
                {
                    _mainWindow._scrapedItems.Remove(item);
                }
            }
            finally
            {
                _isSyncingSelection = false;
            }

            _mainWindow.RecalculateDuplicates();
            _mainWindow.UpdateLinkCount();
            
            _mainWindow.Log($"Deleted {itemsToRemove.Count} checked duplicate item(s) from duplicates review.");
            lblStatus.Text = $"Deleted {itemsToRemove.Count} checked item(s).";
        }

        private void MenuDeleteUnchecked_Click(object sender, RoutedEventArgs e)
        {
            DeleteUncheckedItems();
        }

        private void DeleteUncheckedItems()
        {
            var visibleSet = _duplicatesView.Cast<GalleryItem>().ToHashSet();
            var itemsToRemove = _mainWindow._scrapedItems.Where(item => !item.IsChecked && visibleSet.Contains(item)).ToList();
            if (!itemsToRemove.Any()) return;

            _isSyncingSelection = true;
            try
            {
                foreach (var item in itemsToRemove)
                {
                    _mainWindow._scrapedItems.Remove(item);
                }
            }
            finally
            {
                _isSyncingSelection = false;
            }

            _mainWindow.RecalculateDuplicates();
            _mainWindow.UpdateLinkCount();
            
            _mainWindow.Log($"Deleted {itemsToRemove.Count} unchecked duplicate item(s) from duplicates review.");
            lblStatus.Text = $"Deleted {itemsToRemove.Count} unchecked item(s).";
        }

        private async void MenuDownloadSelected_Click(object sender, RoutedEventArgs e)
        {
            var items = GetSelectedGalleryItems();
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

        private void DuplicatePreviewRow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle && sender is DataGridRow row && row.DataContext is GalleryItem clickedItem)
            {
                e.Handled = true;

                DataGrid parentGrid = null;
                DependencyObject parent = VisualTreeHelper.GetParent(row);
                while (parent != null)
                {
                    if (parent is DataGrid dg)
                    {
                        parentGrid = dg;
                        break;
                    }
                    parent = VisualTreeHelper.GetParent(parent);
                }

                if (parentGrid == null) return;

                var itemsSource = parentGrid.ItemsSource;
                if (itemsSource == null) return;

                string clickedCore = MainWindow.GetSimilarityCore(clickedItem.Name, false);
                if (string.IsNullOrEmpty(clickedCore)) return;

                var listItems = itemsSource.Cast<GalleryItem>().ToList();
                var matchingItems = listItems
                    .Where(item => MainWindow.GetSimilarityCore(item.Name, false) == clickedCore)
                    .ToList();

                parentGrid.SelectedItems.Clear();
                foreach (var item in matchingItems)
                {
                    parentGrid.SelectedItems.Add(item);
                }
            }
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

                // Đồng bộ chọn dòng sang MainWindow
                SyncSelectionToMainWindow(dgDuplicates.SelectedItems.Cast<GalleryItem>().ToList());
            }
            finally
            {
                _isSyncingSelection = false;
            }

            // Tự động load thumbnail cho item duplicate được chọn
            if (dgDuplicates.SelectedItems.Count > 0 && _mainWindow != null)
            {
                var itemsToPrefetch = new System.Collections.Generic.List<GalleryItem>();
                foreach (var item in dgDuplicates.SelectedItems.OfType<GalleryItem>())
                {
                    if (_mainWindow.SupportsHoverPreview(item) && !item.HasHoverPreviewThumbnailFile)
                    {
                        itemsToPrefetch.Add(item);
                    }
                }
                if (itemsToPrefetch.Count > 0)
                {
                    _mainWindow.PrefetchGalleryHoverPreview(itemsToPrefetch);
                }
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

                // Đồng bộ chọn dòng sang MainWindow
                SyncSelectionToMainWindow(lbDuplicatesThumbnail.SelectedItems.Cast<GalleryItem>().ToList());
            }
            finally
            {
                _isSyncingSelection = false;
            }

            // Tự động load thumbnail khi click/chọn item trong duplicate thumbnail view
            if (lbDuplicatesThumbnail.SelectedItems.Count > 0 && _mainWindow != null)
            {
                var itemsToPrefetch = new System.Collections.Generic.List<GalleryItem>();
                foreach (var item in lbDuplicatesThumbnail.SelectedItems.OfType<GalleryItem>())
                {
                    if (_mainWindow.SupportsHoverPreview(item) && !item.HasHoverPreviewThumbnailFile)
                    {
                        itemsToPrefetch.Add(item);
                    }
                }
                if (itemsToPrefetch.Count > 0)
                {
                    _mainWindow.PrefetchGalleryHoverPreview(itemsToPrefetch);
                }
            }
        }


        // ==========================================
        // LOCAL DUPLICATES LOGIC & EVENT HANDLERS
        // ==========================================

        private void BtnBrowseLocal_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new VistaFolderBrowser
            {
                Title = "Chọn thư mục chứa truyện để quét trùng lặp",
                InitialFolder = txtLocalPath.Text.Trim()
            };
            if (string.IsNullOrEmpty(dialog.InitialFolder))
            {
                dialog.InitialFolder = PortablePaths.DefaultDownloadRoot;
            }

            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (dialog.ShowDialog(hwnd))
            {
                txtLocalPath.Text = dialog.SelectedPath;
                LoadLocalFolders(dialog.SelectedPath);
            }
        }

        private static string GetFirstImagePath(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return null;
            
            string searchPath = folderPath;
            if (!searchPath.StartsWith(@"\\?\"))
            {
                searchPath = @"\\?\" + searchPath;
            }

            string result = GetFirstImagePathInternal(searchPath);
            if (result != null && result.StartsWith(@"\\?\"))
            {
                result = result.Substring(4);
            }
            return result;
        }

        private static string GetFirstImagePathInternal(string folderPath)
        {
            if (!Directory.Exists(folderPath)) return null;

            string[] imageExtensions = { "*.jpg", "*.jpeg", "*.png", "*.webp", "*.gif", "*.bmp" };
            foreach (var ext in imageExtensions)
            {
                try
                {
                    var files = Directory.GetFiles(folderPath, ext);
                    if (files.Length > 0)
                    {
                        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                        return files[0];
                    }
                }
                catch { }
            }

            try
            {
                var subdirs = Directory.GetDirectories(folderPath);
                Array.Sort(subdirs, StringComparer.OrdinalIgnoreCase);
                foreach (var subdir in subdirs)
                {
                    string res = GetFirstImagePathInternal(subdir);
                    if (res != null) return res;
                }
            }
            catch { }

            return null;
        }

        private void LoadLocalFolders(string path)
        {
            if (!Directory.Exists(path)) return;

            _localItems.Clear();
            try
            {
                string[] subfolders = Directory.GetDirectories(path);
                int index = 0;
                foreach (string folderPath in subfolders)
                {
                    string folderName = Path.GetFileName(folderPath);
                    string firstImage = GetFirstImagePath(folderPath);
                    _mainWindow.Log($"[Local Dup Scan] Folder: {folderName} -> Image: {firstImage ?? "NONE"}");
                    
                    var item = new GalleryItem
                    {
                        Name = folderName,
                        Link = folderPath,
                        OriginalIndex = index++,
                        HoverPreviewLocalPath = firstImage,
                        HoverPreviewThumbnailLocalPath = firstImage
                    };
                    item.RefreshHoverPreviewBindings();
                    item.PropertyChanged += GalleryItem_PropertyChanged;
                    _localItems.Add(item);
                }
                RecalculateLocalDuplicates();
                _localView.Refresh();
                UpdateStatus();
                _mainWindow.Log($"Loaded {subfolders.Length} folders from {path} to check duplicates.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi đọc thư mục: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RecalculateLocalDuplicates()
        {
            MainWindow.RunDuplicateDetection(_localItems);
        }

        private void BtnCheckAllLocal_Click(object sender, RoutedEventArgs e)
        {
            var visibleItems = _localView.Cast<GalleryItem>().ToList();
            foreach (var item in visibleItems)
            {
                item.IsChecked = true;
            }
            UpdateStatus();
        }

        private void BtnUncheckAllLocal_Click(object sender, RoutedEventArgs e)
        {
            var visibleItems = _localView.Cast<GalleryItem>().ToList();
            foreach (var item in visibleItems)
            {
                item.IsChecked = false;
            }
            UpdateStatus();
        }

        private void ChkSelectAllLocal_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox chk)
            {
                bool isChecked = chk.IsChecked ?? false;
                var visibleItems = _localView.Cast<GalleryItem>().ToList();
                foreach (var item in visibleItems)
                {
                    item.IsChecked = isChecked;
                }
                UpdateStatus();
            }
        }

        private void ChkResultsPresentationLocal_Click(object sender, RoutedEventArgs e)
        {
            if (chkResultsPresentationLocal == null) return;
            bool isThumbnail = chkResultsPresentationLocal.IsChecked == true;
            dgDuplicatesLocal.Visibility = isThumbnail ? Visibility.Collapsed : Visibility.Visible;
            lbDuplicatesThumbnailLocal.Visibility = isThumbnail ? Visibility.Visible : Visibility.Collapsed;
        }

        private void DgDuplicatesLocal_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingSelection || dgDuplicatesLocal == null || lbDuplicatesThumbnailLocal == null) return;
            _isSyncingSelection = true;
            try
            {
                lbDuplicatesThumbnailLocal.SelectedItems.Clear();
                foreach (var item in dgDuplicatesLocal.SelectedItems)
                {
                    lbDuplicatesThumbnailLocal.SelectedItems.Add(item);
                }
            }
            finally
            {
                _isSyncingSelection = false;
            }
        }

        private void LbDuplicatesThumbnailLocal_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingSelection || dgDuplicatesLocal == null || lbDuplicatesThumbnailLocal == null) return;
            _isSyncingSelection = true;
            try
            {
                dgDuplicatesLocal.SelectedItems.Clear();
                foreach (var item in lbDuplicatesThumbnailLocal.SelectedItems)
                {
                    dgDuplicatesLocal.SelectedItems.Add(item);
                }
            }
            finally
            {
                _isSyncingSelection = false;
            }
        }

        private void DgDuplicatesLocal_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var isThumbnail = chkResultsPresentationLocal?.IsChecked == true;
            var itemsCount = isThumbnail ? lbDuplicatesThumbnailLocal.Items.Count : dgDuplicatesLocal.Items.Count;
            if (itemsCount == 0) return;

            if (e.Key == Key.Home)
            {
                if (isThumbnail)
                {
                    lbDuplicatesThumbnailLocal.SelectedIndex = 0;
                    lbDuplicatesThumbnailLocal.ScrollIntoView(lbDuplicatesThumbnailLocal.SelectedItem);
                }
                else
                {
                    dgDuplicatesLocal.SelectedIndex = 0;
                    dgDuplicatesLocal.ScrollIntoView(dgDuplicatesLocal.SelectedItem);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.End)
            {
                if (isThumbnail)
                {
                    lbDuplicatesThumbnailLocal.SelectedIndex = lbDuplicatesThumbnailLocal.Items.Count - 1;
                    lbDuplicatesThumbnailLocal.ScrollIntoView(lbDuplicatesThumbnailLocal.SelectedItem);
                }
                else
                {
                    dgDuplicatesLocal.SelectedIndex = dgDuplicatesLocal.Items.Count - 1;
                    dgDuplicatesLocal.ScrollIntoView(dgDuplicatesLocal.SelectedItem);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                DeleteSelectedItemsLocal();
                e.Handled = true;
            }
            else if (e.Key == Key.Space)
            {
                var selected = isThumbnail 
                    ? lbDuplicatesThumbnailLocal.SelectedItems.Cast<GalleryItem>().ToList()
                    : dgDuplicatesLocal.SelectedItems.Cast<GalleryItem>().ToList();

                if (selected.Count > 0)
                {
                    bool targetState = !selected[0].IsChecked;
                    foreach (var item in selected)
                    {
                        item.IsChecked = targetState;
                    }
                    e.Handled = true;
                }
            }
        }

        private void DgDuplicatesLocal_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text)) return;
            if (!(sender is DataGrid grid)) return;

            DateTime now = DateTime.Now;
            if ((now - _localLastKeyPressTime).TotalMilliseconds > 1000)
            {
                _localSearchBuffer = "";
            }
            _localLastKeyPressTime = now;
            _localSearchBuffer += e.Text;

            var items = grid.Items.Cast<GalleryItem>().ToList();
            var match = items.FirstOrDefault(item => item.Name.StartsWith(_localSearchBuffer, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                match = items.FirstOrDefault(item => item.Name.IndexOf(_localSearchBuffer, StringComparison.OrdinalIgnoreCase) >= 0);
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

        private void DgDuplicatesLocal_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var element = e.OriginalSource as DependencyObject;
            while (element != null && !(element is DataGridRow))
            {
                element = VisualTreeHelper.GetParent(element);
            }

            if (element is DataGridRow row && row.Item is GalleryItem item)
            {
                if (!string.IsNullOrEmpty(item.Link) && Directory.Exists(item.Link))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = item.Link,
                            UseShellExecute = true
                        });
                        _mainWindow.Log($"Opened duplicate local folder: {item.Link}");
                    }
                    catch (Exception ex)
                    {
                        _mainWindow.Log($"Failed to open local folder: {ex.Message}");
                    }
                }
            }
        }

        private void MenuCheckSelectedLocal_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in GetSelectedGalleryItems())
            {
                item.IsChecked = true;
            }
            UpdateStatus();
        }

        private void MenuUncheckSelectedLocal_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in GetSelectedGalleryItems())
            {
                item.IsChecked = false;
            }
            UpdateStatus();
        }

        private void MenuInvertCheckedLocal_Click(object sender, RoutedEventArgs e)
        {
            var visibleItems = _localView.Cast<GalleryItem>().ToList();
            foreach (var item in visibleItems)
            {
                item.IsChecked = !item.IsChecked;
            }
            UpdateStatus();
        }

        private void MenuCopySelectedPathsLocal_Click(object sender, RoutedEventArgs e)
        {
            var items = GetSelectedGalleryItems();
            if (items.Count == 0) return;
            string text = string.Join("\r\n", items.Select(item => item.Link));
            Clipboard.SetText(text);
            _mainWindow.Log($"Copied {items.Count} selected local path(s) to clipboard.");
        }

        private void MenuDeleteSelectedLocal_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelectedItemsLocal();
        }

        private void MenuDeleteCheckedLocal_Click(object sender, RoutedEventArgs e)
        {
            DeleteCheckedItemsLocal();
        }

        private void DeleteSelectedItemsLocal()
        {
            bool isThumbnail = chkResultsPresentationLocal?.IsChecked == true;
            int selectedIndex = isThumbnail ? lbDuplicatesThumbnailLocal.SelectedIndex : dgDuplicatesLocal.SelectedIndex;

            var itemsToRemove = isThumbnail
                ? lbDuplicatesThumbnailLocal.SelectedItems.Cast<GalleryItem>().ToList()
                : dgDuplicatesLocal.SelectedItems.Cast<GalleryItem>().ToList();

            if (itemsToRemove.Count == 0) return;

            var confirm = MessageBox.Show(
                $"Bạn có chắc chắn muốn xóa {itemsToRemove.Count} thư mục này khỏi đĩa vĩnh viễn không?",
                "Xác nhận xóa thư mục",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            int successCount = 0;
            foreach (var item in itemsToRemove)
            {
                try
                {
                    if (Directory.Exists(item.Link))
                    {
                        Directory.Delete(item.Link, true);
                    }
                    _localItems.Remove(item);
                    successCount++;
                }
                catch (Exception ex)
                {
                    _mainWindow.Log($"Lỗi khi xóa thư mục {item.Link}: {ex.Message}");
                }
            }

            RecalculateLocalDuplicates();
            UpdateStatus();
            
            _mainWindow.Log($"Deleted {successCount} local duplicate folder(s) from disk.");
            lblStatus.Text = $"Deleted {successCount} local folder(s).";

            if (isThumbnail)
            {
                if (lbDuplicatesThumbnailLocal.Items.Count > 0)
                {
                    int newIndex = Math.Min(selectedIndex, lbDuplicatesThumbnailLocal.Items.Count - 1);
                    if (newIndex >= 0)
                    {
                        lbDuplicatesThumbnailLocal.SelectedIndex = newIndex;
                    }
                }
            }
            else
            {
                if (dgDuplicatesLocal.Items.Count > 0)
                {
                    int newIndex = Math.Min(selectedIndex, dgDuplicatesLocal.Items.Count - 1);
                    if (newIndex >= 0)
                    {
                        dgDuplicatesLocal.SelectedIndex = newIndex;
                    }
                }
            }
        }

        private void DeleteCheckedItemsLocal()
        {
            var visibleSet = _localView.Cast<GalleryItem>().ToHashSet();
            var itemsToRemove = _localItems.Where(item => item.IsChecked && visibleSet.Contains(item)).ToList();
            if (!itemsToRemove.Any()) return;

            var confirm = MessageBox.Show(
                $"Bạn có chắc chắn muốn xóa {itemsToRemove.Count} thư mục đã tích chọn khỏi đĩa vĩnh viễn không?",
                "Xác nhận xóa thư mục",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            int successCount = 0;
            foreach (var item in itemsToRemove)
            {
                try
                {
                    if (Directory.Exists(item.Link))
                    {
                        Directory.Delete(item.Link, true);
                    }
                    _localItems.Remove(item);
                    successCount++;
                }
                catch (Exception ex)
                {
                    _mainWindow.Log($"Lỗi khi xóa thư mục {item.Link}: {ex.Message}");
                }
            }

            RecalculateLocalDuplicates();
            UpdateStatus();
            
            _mainWindow.Log($"Deleted {successCount} checked local duplicate folder(s) from disk.");
            lblStatus.Text = $"Deleted {successCount} checked local folder(s).";
        }

        private double _zoomScale = 1.0;

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Key == Key.OemPlus || e.Key == Key.Add)
                {
                    Zoom(0.1);
                    e.Handled = true;
                }
                else if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
                {
                    Zoom(-0.1);
                    e.Handled = true;
                }
                else if (e.Key == Key.D0 || e.Key == Key.NumPad0)
                {
                    ResetZoom();
                    e.Handled = true;
                }
            }
        }

        private void Window_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                Zoom(e.Delta > 0 ? 0.1 : -0.1);
                e.Handled = true;
            }
        }

        private void Zoom(double delta)
        {
            _zoomScale = Math.Max(0.5, Math.Min(3.0, _zoomScale + delta));
            ApplyZoom();
        }

        private void ResetZoom()
        {
            _zoomScale = 1.0;
            ApplyZoom();
        }

        private void ApplyZoom()
        {
            if (gridRoot.LayoutTransform is ScaleTransform scaleTransform)
            {
                scaleTransform.ScaleX = _zoomScale;
                scaleTransform.ScaleY = _zoomScale;
            }
            else
            {
                gridRoot.LayoutTransform = new ScaleTransform(_zoomScale, _zoomScale);
            }
        }

        private System.Collections.Generic.List<GalleryItem> GetSelectedGalleryItems()
        {
            if (tabMain.SelectedIndex == 0) // Online
            {
                if (chkResultsPresentation?.IsChecked == true)
                {
                    return lbDuplicatesThumbnail.SelectedItems.Cast<GalleryItem>().Where(x => x != null).ToList();
                }
                else
                {
                    return dgDuplicates.SelectedItems.Cast<GalleryItem>().Where(x => x != null).ToList();
                }
            }
            else // Local
            {
                if (chkResultsPresentationLocal?.IsChecked == true)
                {
                    return lbDuplicatesThumbnailLocal.SelectedItems.Cast<GalleryItem>().Where(x => x != null).ToList();
                }
                else
                {
                    return dgDuplicatesLocal.SelectedItems.Cast<GalleryItem>().Where(x => x != null).ToList();
                }
            }
        }

        private void MainWindow_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingSelection) return;
            _isSyncingSelection = true;
            try
            {
                System.Collections.Generic.List<GalleryItem> selectedItems = null;
                if (sender is DataGrid dg)
                {
                    selectedItems = dg.SelectedItems.Cast<GalleryItem>().Where(x => x != null).ToList();
                }
                else if (sender is ListBox lb)
                {
                    selectedItems = lb.SelectedItems.Cast<GalleryItem>().Where(x => x != null).ToList();
                }

                if (selectedItems != null)
                {
                    SyncSelectionToDuplicateWindow(selectedItems);
                }
            }
            finally
            {
                _isSyncingSelection = false;
            }
        }

        private void SyncSelectionToMainWindow(System.Collections.Generic.List<GalleryItem> items)
        {
            if (_mainWindow.dgResults != null)
            {
                _mainWindow.dgResults.SelectedItems.Clear();
                foreach (var item in items)
                {
                    _mainWindow.dgResults.SelectedItems.Add(item);
                }
                if (items.Count > 0)
                {
                    _mainWindow.dgResults.ScrollIntoView(items[0]);
                }
            }

            if (_mainWindow.lbResultsThumbnail != null)
            {
                _mainWindow.lbResultsThumbnail.SelectedItems.Clear();
                foreach (var item in items)
                {
                    _mainWindow.lbResultsThumbnail.SelectedItems.Add(item);
                }
                if (items.Count > 0)
                {
                    _mainWindow.lbResultsThumbnail.ScrollIntoView(items[0]);
                }
            }
        }

        private void SyncSelectionToDuplicateWindow(System.Collections.Generic.List<GalleryItem> items)
        {
            dgDuplicates.SelectedItems.Clear();
            foreach (var item in items)
            {
                dgDuplicates.SelectedItems.Add(item);
            }
            if (items.Count > 0 && dgDuplicates.Items.Contains(items[0]))
            {
                dgDuplicates.ScrollIntoView(items[0]);
            }

            lbDuplicatesThumbnail.SelectedItems.Clear();
            foreach (var item in items)
            {
                lbDuplicatesThumbnail.SelectedItems.Add(item);
            }
            if (items.Count > 0 && lbDuplicatesThumbnail.Items.Contains(items[0]))
            {
                lbDuplicatesThumbnail.ScrollIntoView(items[0]);
            }
        }
    }
}

using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

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

        public DuplicateWindow(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;

            // Bind dgDuplicates to the main window's scraped items with a filter
            _duplicatesView = new ListCollectionView(_mainWindow._scrapedItems);
            _duplicatesView.Filter = item =>
            {
                if (item is GalleryItem galleryItem)
                {
                    string filterText = txtFilter.Text.Trim();
                    bool matchesFilter = string.IsNullOrEmpty(filterText) ||
                                         galleryItem.Name.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         galleryItem.Link.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0;

                    return galleryItem.IsDuplicate && matchesFilter;
                }
                return false;
            };

            dgDuplicates.ItemsSource = _duplicatesView;
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
            if (e.PropertyName == nameof(GalleryItem.IsChecked) || e.PropertyName == nameof(GalleryItem.IsDuplicate))
            {
                Dispatcher.InvokeAsync(UpdateStatus);
            }
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
            int totalDups = _mainWindow._scrapedItems.Count(item => item.IsDuplicate);
            int checkedDups = _mainWindow._scrapedItems.Count(item => item.IsDuplicate && item.IsChecked);

            lblDupCount.Text = $"{checkedDups}/{totalDups}";
            lblStatus.Text = $"Duplicate groups active. {checkedDups} of {totalDups} duplicate items selected.";

            if (chkSelectAll != null)
            {
                // Set main checkbox state
                if (totalDups == 0)
                    chkSelectAll.IsChecked = false;
                else if (checkedDups == totalDups)
                    chkSelectAll.IsChecked = true;
                else if (checkedDups == 0)
                    chkSelectAll.IsChecked = false;
                else
                    chkSelectAll.IsChecked = null; // Indeterminate
            }
        }

        private void TxtFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            _duplicatesView.Refresh();
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
            if (dgDuplicates.Items.Count == 0) return;

            if (e.Key == Key.Home)
            {
                dgDuplicates.SelectedIndex = 0;
                dgDuplicates.ScrollIntoView(dgDuplicates.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.End)
            {
                dgDuplicates.SelectedIndex = dgDuplicates.Items.Count - 1;
                dgDuplicates.ScrollIntoView(dgDuplicates.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                DeleteSelectedItems();
                e.Handled = true;
            }
        }

        private void DeleteSelectedItems()
        {
            if (dgDuplicates.SelectedItems.Count == 0) return;

            var itemsToRemove = dgDuplicates.SelectedItems.Cast<GalleryItem>().ToList();
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
        }

        private void DgDuplicates_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text)) return;

            DateTime now = DateTime.Now;
            if ((now - _lastKeyPressTime).TotalMilliseconds > 1000)
            {
                _searchBuffer = "";
            }
            _lastKeyPressTime = now;
            _searchBuffer += e.Text;

            var items = dgDuplicates.Items.Cast<GalleryItem>().ToList();
            var match = items.FirstOrDefault(item => item.Name.StartsWith(_searchBuffer, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                match = items.FirstOrDefault(item => item.Name.IndexOf(_searchBuffer, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (match != null)
            {
                dgDuplicates.SelectedItem = match;
                dgDuplicates.ScrollIntoView(match);

                var row = (DataGridRow)dgDuplicates.ItemContainerGenerator.ContainerFromItem(match);
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
            foreach (var item in dgDuplicates.SelectedItems.Cast<GalleryItem>())
            {
                item.IsChecked = true;
            }
        }

        private void MenuUncheckSelected_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in dgDuplicates.SelectedItems.Cast<GalleryItem>())
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
            var itemsToRemove = _mainWindow._scrapedItems.Where(item => item.IsChecked && item.IsDuplicate).ToList();
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
            var items = _mainWindow._scrapedItems.Where(item => item.IsDuplicate && item.IsChecked).ToList();
            if (!items.Any())
            {
                MessageBox.Show("Vui lòng tích chọn ít nhất 1 truyện để tải (Please check at least one gallery to download).", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            await _mainWindow.StartDownloadProcessAsync(items);
        }
    }
}

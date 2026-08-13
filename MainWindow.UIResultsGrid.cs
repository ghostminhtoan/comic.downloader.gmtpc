using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private ICollectionView ResultsView => CollectionViewSource.GetDefaultView(_scrapedItems);
        private bool _isNameSortAscending = true;
        private bool _isStatusSortAscending = true;
        private bool _isProcessSortAscending = true;
        private bool _isSpeedSortAscending = false;
        private Point _resultsDragStartPoint;
        private GalleryItem _resultsDragItem;
        private bool _isResultsThumbnailViewEnabled;
        private DateTime _lastResultsRefreshTime = DateTime.MinValue;
        private bool _isResultsRefreshPending = false;
        private bool _isFreeArrangementActive = true;
        private bool _isSyncingResultsThumbnailSelection;
        private readonly ObservableCollection<GalleryItem> _thumbnailVisibleItems = new ObservableCollection<GalleryItem>();
        private ScrollViewer _resultsThumbnailScrollViewer;
        private System.Windows.Threading.DispatcherTimer _thumbnailRefreshTimer;
        private const int ThumbnailColumns = 7;
        private const int CompactThumbnailColumns = 9;
        private const int ThumbnailInitialRows = 8;
        private const int ThumbnailLoadMoreRows = 6;

        private void ChkCompactRows_Click(object sender, RoutedEventArgs e)
        {
            ApplyResultsCompactRows();
        }

        private void ChkHideSettings_Click(object sender, RoutedEventArgs e)
        {
            if (borderDownloadSection != null)
            {
                borderDownloadSection.Visibility = chkHideSettings?.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
            }
            UpdateLayout();
            ApplyThumbnailDensity();
            if (_isResultsThumbnailViewEnabled)
            {
                RebuildThumbnailResultsView();
            }
        }

        private void ApplyResultsCompactRows()
        {
            if (dgResults == null)
            {
                return;
            }

            dgResults.RowHeight = double.NaN;
            UpdateLayout();
            ApplyThumbnailDensity();
            if (_isResultsThumbnailViewEnabled)
            {
                RebuildThumbnailResultsView();
            }
            SafeRefreshResultsView();
        }

        private bool IsCompactRowsEnabled()
        {
            return chkCompactRows?.IsChecked == true;
        }

        private int GetThumbnailColumnCount()
        {
            if (IsPortraitMode)
            {
                return 6;
            }
            if (_isRailHidden)
            {
                return 7;
            }
            return IsCompactRowsEnabled() ? CompactThumbnailColumns : ThumbnailColumns;
        }

        private void ApplyThumbnailDensity()
        {
            if (lbResultsThumbnail == null)
            {
                return;
            }

            lbResultsThumbnail.AlternationCount = GetThumbnailColumnCount();
            lbResultsThumbnail.Tag = IsCompactRowsEnabled() ? GetCompactThumbnailTileHeight() : double.NaN;
        }

        private void LbResultsThumbnail_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyThumbnailDensity();
        }

        private double GetCompactThumbnailTileHeight()
        {
            double available = lbResultsThumbnail?.ActualHeight ?? 0;

            // Chế độ ẩn ảnh bìa khi Portrait, Compact ON, Hide Settings OFF
            bool isNoImageMode = IsPortraitMode && (chkHideSettings?.IsChecked == false);

            double baseMinHeight = IsPortraitMode ? 165d : 125d;
            if (isNoImageMode)
            {
                baseMinHeight = 71d; // 68px cũ + 3px tăng thêm
            }
            else if (chkHideSettings?.IsChecked == false)
            {
                baseMinHeight += 10d;
            }

            if (available <= 0)
            {
                return baseMinHeight;
            }

            int targetRows = 4;
            double verticalPaddingTotal = targetRows * 6d + 8d;
            double calculatedHeight = Math.Floor((available - verticalPaddingTotal) / targetRows);

            return Math.Max(baseMinHeight, calculatedHeight);
        }

        private void ApplyResultsSort(string propertyName, ListSortDirection direction, string logMessage = null)
        {
            var view = ResultsView;
            if (view == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return;
            }

            view.SortDescriptions.Clear();

            // Nếu đang bật Free Arrangement và không sắp xếp cột cụ thể nào (hoặc sắp xếp theo OriginalIndex)
            // thì xóa sạch SortDescriptions để WPF hiển thị hoàn toàn tự do theo thứ tự trong _scrapedItems
            if (_isFreeArrangementActive && string.Equals(propertyName, "OriginalIndex", StringComparison.Ordinal))
            {
                // Không thêm bất kỳ SortDescriptions nào
            }
            else
            {
                // Chỉ nhóm split khi thực sự có item split
                bool hasSplit = _scrapedItems.Any(item => item.IsParallelSplitTask || item.IsParallelSplitParent);
                if (hasSplit)
                {
                    view.SortDescriptions.Add(new SortDescription("ParallelSplitGroupKey", ListSortDirection.Ascending));
                    view.SortDescriptions.Add(new SortDescription("IsParallelSplitParent", ListSortDirection.Descending));
                }

                if (!string.Equals(propertyName, "ParallelSplitGroupKey", StringComparison.Ordinal) && 
                    !string.Equals(propertyName, "IsParallelSplitParent", StringComparison.Ordinal))
                {
                    view.SortDescriptions.Add(new SortDescription(propertyName, direction));
                }

                if (!string.Equals(propertyName, "OriginalIndex", StringComparison.Ordinal))
                {
                    view.SortDescriptions.Add(new SortDescription("OriginalIndex", ListSortDirection.Ascending));
                }
            }

            if (!string.IsNullOrWhiteSpace(logMessage))
            {
                Log(logMessage);
            }

            SyncDownloadMissingChapterRowsToResultsOrder();
            PrefetchAllThumbnailResults();
            if (_isResultsThumbnailViewEnabled)
            {
                RebuildThumbnailResultsView();
            }
            SafeRefreshResultsView();
        }

        private void ApplyResultsSort(DataGridColumn column, string propertyName, ref bool ascendingFlag, string label)
        {
            ListSortDirection direction = ascendingFlag ? ListSortDirection.Ascending : ListSortDirection.Descending;
            ascendingFlag = !ascendingFlag;

            ClearResultsColumnSortDirections(column);
            if (column != null)
            {
                column.SortDirection = direction;
            }

            ApplyResultsSort(propertyName, direction, $"Sorted {label} {(direction == ListSortDirection.Ascending ? "ascending" : "descending")}.");
        }

        private void ClearResultsColumnSortDirections(DataGridColumn activeColumn = null)
        {
            if (dgResults?.Columns == null)
            {
                return;
            }

            foreach (DataGridColumn column in dgResults.Columns)
            {
                if (!ReferenceEquals(column, activeColumn))
                {
                    column.SortDirection = null;
                }
            }
        }

        private void DgResults_Sorting(object sender, DataGridSortingEventArgs e)
        {
            if (e?.Column == null || string.IsNullOrWhiteSpace(e.Column.SortMemberPath))
            {
                return;
            }

            e.Handled = true;

            ListSortDirection direction;
            if (ReferenceEquals(e.Column, colSpeed))
            {
                direction = e.Column.SortDirection == ListSortDirection.Descending
                    ? ListSortDirection.Ascending
                    : ListSortDirection.Descending;
            }
            else
            {
                direction = e.Column.SortDirection == ListSortDirection.Ascending
                    ? ListSortDirection.Descending
                    : ListSortDirection.Ascending;
            }

            ClearResultsColumnSortDirections(e.Column);
            e.Column.SortDirection = direction;
            ApplyResultsSort(e.Column.SortMemberPath, direction, $"Sorted '{e.Column.Header}' {(direction == ListSortDirection.Ascending ? "ascending" : "descending")}.");

            if (ReferenceEquals(e.Column, colGalleryDetails))
            {
                _isNameSortAscending = direction != ListSortDirection.Ascending;
            }
            else if (ReferenceEquals(e.Column, colStatus))
            {
                _isStatusSortAscending = direction != ListSortDirection.Ascending;
            }
            else if (ReferenceEquals(e.Column, colProcess))
            {
                _isProcessSortAscending = direction != ListSortDirection.Ascending;
            }
            else if (ReferenceEquals(e.Column, colSpeed))
            {
                _isSpeedSortAscending = direction != ListSortDirection.Ascending;
            }
        }

        private void TxtFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyResultsFilter();
        }

        private void ApplyResultsFilter()
        {
            ApplyResultsFilter(ResultsView, txtFilter?.Text?.Trim() ?? string.Empty);
            ApplyResultsFilter(CollectionViewSource.GetDefaultView(_lightNovelItems), string.Empty);
            PrefetchAllThumbnailResults();
        }

        private bool IsGalleryItemMatchKeyword(GalleryItem galleryItem, string keyword)
        {
            if (galleryItem == null || string.IsNullOrEmpty(keyword)) return false;
            return (galleryItem.Name != null && galleryItem.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (galleryItem.Link != null && galleryItem.Link.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (galleryItem.MissingChapterStatusText != null && galleryItem.MissingChapterStatusText.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (galleryItem.Status != null && galleryItem.Status.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (galleryItem.CurrentProcess != null && galleryItem.CurrentProcess.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (galleryItem.DownloadingChapter != null && galleryItem.DownloadingChapter.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (galleryItem.DownloadingPageProgress != null && galleryItem.DownloadingPageProgress.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void ApplyResultsFilter(ICollectionView view, string filterText)
        {
            if (view == null)
            {
                return;
            }

            var posKeywords = AdvancedSearch?.GetActivePositiveKeywords() ?? new System.Collections.Generic.List<string>();
            var nonNegKeywords = AdvancedSearch?.GetActiveNonNegativeKeywords() ?? new System.Collections.Generic.List<string>();
            var nonPosKeywords = AdvancedSearch?.GetActiveNonPositiveKeywords() ?? new System.Collections.Generic.List<string>();
            var negKeywords = AdvancedSearch?.GetActiveNegativeKeywords() ?? new System.Collections.Generic.List<string>();

            view.Filter = item =>
            {
                if (!(item is GalleryItem galleryItem))
                {
                    return false;
                }

                // 1. Kiểm tra bộ lọc tìm kiếm nhanh chính (txtFilter)
                if (!string.IsNullOrEmpty(filterText))
                {
                    if (!IsGalleryItemMatchKeyword(galleryItem, filterText))
                    {
                        return false;
                    }
                }

                // 2. Kiểm tra Positive Keywords (Tất cả đều phải chứa - AND logic)
                if (posKeywords.Count > 0)
                {
                    foreach (var k in posKeywords)
                    {
                        if (!IsGalleryItemMatchKeyword(galleryItem, k))
                        {
                            return false;
                        }
                    }
                }

                // 3. Kiểm tra Non-Negative Keywords (Phải chứa ít nhất một từ khóa - OR logic)
                if (nonNegKeywords.Count > 0)
                {
                    bool matchAny = false;
                    foreach (var k in nonNegKeywords)
                    {
                        if (IsGalleryItemMatchKeyword(galleryItem, k))
                        {
                            matchAny = true;
                            break;
                        }
                    }
                    if (!matchAny)
                    {
                        return false;
                    }
                }

                // 4. Kiểm tra Non-Positive Keywords (Không được chứa bất kỳ từ khóa nào - Exclude logic)
                if (nonPosKeywords.Count > 0)
                {
                    foreach (var k in nonPosKeywords)
                    {
                        if (IsGalleryItemMatchKeyword(galleryItem, k))
                        {
                            return false;
                        }
                    }
                }

                // 5. Kiểm tra Negative Keywords (Không được chứa bất kỳ từ khóa nào)
                if (negKeywords.Count > 0)
                {
                    foreach (var k in negKeywords)
                    {
                        if (IsGalleryItemMatchKeyword(galleryItem, k))
                        {
                            return false;
                        }
                    }
                }

                return true;
            };
        }

        private void BtnSortByName_Click(object sender, RoutedEventArgs e)
        {
            ApplyResultsSort(colGalleryDetails, "Name", ref _isNameSortAscending, "comic books");
        }

        private void BtnSortBySpeed_Click(object sender, RoutedEventArgs e)
        {
            ApplyResultsSort(colSpeed, "DownloadSpeedSortValue", ref _isSpeedSortAscending, "download speed");
        }

        private void BtnFreeArrangement_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton toggleBtn)
            {
                _isFreeArrangementActive = toggleBtn.IsChecked == true;
                RestoreResultsOrder(_isFreeArrangementActive ? "Free arrangement mode enabled." : "Free arrangement mode disabled.");
            }
        }

        internal void RestoreResultsOrder(string logMessage)
        {
            _isNameSortAscending = true;
            _isStatusSortAscending = true;
            _isProcessSortAscending = true;
            _isSpeedSortAscending = false;
            ClearResultsColumnSortDirections();
            ApplyResultsSort("OriginalIndex", ListSortDirection.Ascending, logMessage);
        }

        internal void RenumberResultOrder()
        {
            for (int i = 0; i < _scrapedItems.Count; i++)
            {
                _scrapedItems[i].OriginalIndex = i;
            }

            Debug.Assert(_scrapedItems.Select((item, index) => item.OriginalIndex == index).All(match => match));
        }

        private void MoveResultItem(GalleryItem item, int targetIndex, string logMessage)
        {
            MoveResultItems(new List<GalleryItem> { item }, targetIndex, logMessage);
        }

        private void MoveResultItems(List<GalleryItem> items, int targetIndex, string logMessage)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            var validItems = items.Where(x => x != null && _scrapedItems.Contains(x)).ToList();
            if (validItems.Count == 0)
            {
                return;
            }

            GalleryItem targetItem = null;
            if (targetIndex >= 0 && targetIndex < _scrapedItems.Count)
            {
                targetItem = _scrapedItems[targetIndex];
            }

            foreach (var item in validItems)
            {
                _scrapedItems.Remove(item);
            }

            int insertIndex = targetItem != null ? _scrapedItems.IndexOf(targetItem) : _scrapedItems.Count;
            if (insertIndex < 0)
            {
                insertIndex = _scrapedItems.Count;
            }

            for (int i = 0; i < validItems.Count; i++)
            {
                _scrapedItems.Insert(insertIndex + i, validItems[i]);
            }

            RenumberResultOrder();
            if (_isFreeArrangementActive)
            {
                ApplyResultsSort("OriginalIndex", ListSortDirection.Ascending, logMessage);
            }
            else
            {
                RestoreResultsOrder(logMessage);
            }
        }

        private static bool IsDragCandidate(DependencyObject source)
        {
            while (source != null)
            {
                if (source is FrameworkElement fe && string.Equals(fe.Tag as string, "drag_handle", StringComparison.Ordinal))
                {
                    return true;
                }
                DependencyObject parent = null;
                if (source is Visual || source is System.Windows.Media.Media3D.Visual3D)
                {
                    parent = VisualTreeHelper.GetParent(source);
                }
                if (parent == null && source is FrameworkContentElement fce)
                {
                    parent = fce.Parent;
                }
                source = parent;
            }
            return false;
        }

        private DataGridRow GetResultsRow(DependencyObject source)
        {
            while (source != null)
            {
                if (source is DataGridRow row)
                {
                    return row;
                }
                source = VisualTreeHelper.GetParent(source);
            }
            return null;
        }

        private void DgResultsRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed || !IsDragCandidate(e.OriginalSource as DependencyObject))
            {
                _resultsDragItem = null;
                return;
            }

            if (sender is DataGridRow row && row.Item is GalleryItem item)
            {
                _resultsDragStartPoint = e.GetPosition(null);
                _resultsDragItem = item;

                // Nếu item đang kéo nằm trong danh sách bôi đen nhiều hàng (SelectedItems), giữ nguyên lựa chọn
                if (dgResults != null && dgResults.SelectedItems.Contains(item) && dgResults.SelectedItems.Count > 1)
                {
                    e.Handled = true;
                }
            }
        }

        private void DgResultsRow_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _resultsDragItem == null || !(sender is DataGridRow row))
            {
                return;
            }

            Point currentPosition = e.GetPosition(null);
            if (Math.Abs(currentPosition.X - _resultsDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(currentPosition.Y - _resultsDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            try
            {
                DragDrop.DoDragDrop(row, _resultsDragItem, DragDropEffects.Move);
            }
            finally
            {
                _resultsDragItem = null;
            }
        }

        private void MenuMoveSelectedToTop_Click(object sender, RoutedEventArgs e)
        {
            List<GalleryItem> items = GetMovableSelectedItems();
            if (items.Count == 0)
            {
                ShowNoSelectedItemsError();
                return;
            }

            int insertIndex = 0;
            foreach (GalleryItem item in items)
            {
                _scrapedItems.Remove(item);
                _scrapedItems.Insert(insertIndex++, item);
            }

            RenumberResultOrder();
            RestoreResultsOrder(items.Count == 1
                ? $"Moved '{items[0].DisplayName}' to top."
                : $"Moved {items.Count} selected items to top.");
        }

        private void MenuMoveSelectedToBottom_Click(object sender, RoutedEventArgs e)
        {
            List<GalleryItem> items = GetMovableSelectedItems();
            if (items.Count == 0)
            {
                ShowNoSelectedItemsError();
                return;
            }

            foreach (GalleryItem item in items)
            {
                _scrapedItems.Remove(item);
            }

            foreach (GalleryItem item in items)
            {
                _scrapedItems.Add(item);
            }

            RenumberResultOrder();
            RestoreResultsOrder(items.Count == 1
                ? $"Moved '{items[0].DisplayName}' to bottom."
                : $"Moved {items.Count} selected items to bottom.");
        }

        private List<GalleryItem> GetMovableSelectedItems()
        {
            List<GalleryItem> items = dgResults?.SelectedItems?.Cast<GalleryItem>()
                .Where(item => item != null && _scrapedItems.Contains(item))
                .ToList() ?? new List<GalleryItem>();

            if (items.Count > 0)
            {
                return items
                    .OrderBy(item => _scrapedItems.IndexOf(item))
                    .ToList();
            }

            if (dgResults?.CurrentItem is GalleryItem currentItem && _scrapedItems.Contains(currentItem))
            {
                return new List<GalleryItem> { currentItem };
            }

            return new List<GalleryItem>();
        }

        private DataGridRow _lastDragOverRow = null;
        private ListBoxItem _lastDragOverThumbnailContainer = null;

        private void DgResults_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(GalleryItem)))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.Effects = DragDropEffects.Move;
            e.Handled = true;

            // Auto Scroll DataGrid khi kéo chuột lên mép trên / mép dưới
            if (sender is FrameworkElement element)
            {
                Point pos = e.GetPosition(element);
                double tolerance = 35;
                ScrollViewer scrollViewer = FindVisualChild<ScrollViewer>(dgResults);
                if (scrollViewer != null)
                {
                    if (pos.Y < tolerance)
                    {
                        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - 2);
                    }
                    else if (pos.Y > element.ActualHeight - tolerance)
                    {
                        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + 2);
                    }
                }
            }

            // Target Highlight Effect khi kéo qua hàng
            var targetRow = GetResultsRow(e.OriginalSource as DependencyObject);
            if (_lastDragOverRow != targetRow)
            {
                ClearDragOverHighlight();
                _lastDragOverRow = targetRow;
                if (_lastDragOverRow != null)
                {
                    _lastDragOverRow.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00F0FF"));
                    _lastDragOverRow.BorderThickness = new Thickness(0, 0, 0, 2);
                }
            }
        }

        private void DgResults_DragLeave(object sender, DragEventArgs e)
        {
            ClearDragOverHighlight();
        }

        private void ClearDragOverHighlight()
        {
            if (_lastDragOverRow != null)
            {
                _lastDragOverRow.BorderBrush = null;
                _lastDragOverRow.BorderThickness = new Thickness(0);
                _lastDragOverRow = null;
            }
            if (_lastDragOverThumbnailContainer != null)
            {
                _lastDragOverThumbnailContainer.BorderBrush = null;
                _lastDragOverThumbnailContainer.BorderThickness = new Thickness(0);
                _lastDragOverThumbnailContainer = null;
            }
        }

        private void DgResults_Drop(object sender, DragEventArgs e)
        {
            ClearDragOverHighlight();

            if (!e.Data.GetDataPresent(typeof(GalleryItem)))
            {
                return;
            }

            var sourceItem = e.Data.GetData(typeof(GalleryItem)) as GalleryItem;
            var targetRow = GetResultsRow(e.OriginalSource as DependencyObject);
            var targetItem = targetRow?.Item as GalleryItem;

            if (sourceItem == null)
            {
                return;
            }

            List<GalleryItem> dragItems = new List<GalleryItem>();
            if (dgResults != null && dgResults.SelectedItems.Contains(sourceItem))
            {
                dragItems = dgResults.SelectedItems.Cast<GalleryItem>()
                    .Where(x => x != null)
                    .OrderBy(x => _scrapedItems.IndexOf(x))
                    .ToList();
            }
            else
            {
                dragItems.Add(sourceItem);
            }

            int targetIndex = targetItem != null ? _scrapedItems.IndexOf(targetItem) : _scrapedItems.Count - 1;
            string message = dragItems.Count == 1 
                ? $"Moved '{sourceItem.DisplayName}' in gallery list."
                : $"Moved {dragItems.Count} items in gallery list.";

            MoveResultItems(dragItems, targetIndex, message);

            // Khôi phục selection và focus
            dgResults.SelectedItems.Clear();
            foreach (var item in dragItems)
            {
                dgResults.SelectedItems.Add(item);
            }
            if (sourceItem != null)
            {
                dgResults.SelectedItem = sourceItem;
                dgResults.ScrollIntoView(sourceItem);
                Dispatcher.BeginInvoke(new Action(() => {
                    var row = dgResults.ItemContainerGenerator.ContainerFromItem(sourceItem) as DataGridRow;
                    row?.Focus();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        // private void BtnNoLinkViHentai_Click(object sender, RoutedEventArgs e)
        /*
            var view = ResultsView;
            if (view != null)
            {
                _isNameSortAscending = true;
                _isStatusSortAscending = true;
                _isProcessSortAscending = true;
                ClearResultsColumnSortDirections();
                view.SortDescriptions.Clear();
                view.SortDescriptions.Add(new SortDescription("HasNoChapters", ListSortDirection.Descending));
                view.SortDescriptions.Add(new SortDescription("OriginalIndex", ListSortDirection.Ascending));
                Log("Results sorted to show vi-hentai galleries with no chapters first.");
            }
        */

        private void ChkSelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox chk)
            {
                bool isChecked = chk.IsChecked ?? false;
                foreach (var item in _scrapedItems)
                {
                    item.IsChecked = isChecked;
                }
                Log($"{(isChecked ? "Checked" : "Unchecked")} all items via header checkbox.");
            }
        }

        internal readonly System.Collections.Generic.Stack<System.Collections.Generic.List<GalleryItem>> _undoDeleteStack = new System.Collections.Generic.Stack<System.Collections.Generic.List<GalleryItem>>();
        internal readonly System.Collections.Generic.Stack<System.Collections.Generic.List<GalleryItem>> _redoDeleteStack = new System.Collections.Generic.Stack<System.Collections.Generic.List<GalleryItem>>();

        private void DgResults_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (IsTypingInEditableTextBox())
            {
                return;
            }

            if (dgResults.Items.Count == 0 && e.Key != Key.Z && e.Key != Key.Y) return;

            if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                _isSyncingResultsThumbnailSelection = true;
                try
                {
                    if (_isResultsThumbnailViewEnabled)
                    {
                        lbResultsThumbnail.SelectAll();
                    }
                    else
                    {
                        dgResults.SelectAll();
                    }
                }
                finally
                {
                    _isSyncingResultsThumbnailSelection = false;
                }
                SyncThumbnailSelectionFromResults();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                MenuCopySelectedLinks_Click(null, null);
                e.Handled = true;
            }
            else if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                // Undo
                if (_undoDeleteStack.Count > 0)
                {
                    var itemsToRestore = _undoDeleteStack.Pop();
                    _redoDeleteStack.Push(itemsToRestore);
                    foreach (var item in itemsToRestore)
                    {
                        if (!_scrapedItems.Contains(item))
                        {
                            _scrapedItems.Add(item);
                        }
                    }
                    RenumberResultOrder();
                    lblLinkCount.Text = _scrapedItems.Count.ToString();
                    Log($"Undo: Khôi phục {itemsToRestore.Count} truyện đã xóa.");
                    lblStatus.Text = $"Undo: Khôi phục {itemsToRestore.Count} truyện.";
                    RecalculateDuplicates();
                    if (_isResultsThumbnailViewEnabled)
                    {
                        RebuildThumbnailResultsView();
                    }

                    // Giữ focus và chọn item được khôi phục đầu tiên
                    var firstRestored = itemsToRestore.FirstOrDefault();
                    if (firstRestored != null)
                    {
                        if (_isResultsThumbnailViewEnabled)
                        {
                            lbResultsThumbnail.SelectedItem = firstRestored;
                            lbResultsThumbnail.ScrollIntoView(firstRestored);
                            Dispatcher.BeginInvoke(new Action(() => {
                                var container = lbResultsThumbnail.ItemContainerGenerator.ContainerFromItem(firstRestored) as ListBoxItem;
                                container?.Focus();
                            }), System.Windows.Threading.DispatcherPriority.Background);
                        }
                        else
                        {
                            dgResults.SelectedItem = firstRestored;
                            dgResults.ScrollIntoView(firstRestored);
                            Dispatcher.BeginInvoke(new Action(() => {
                                var row = dgResults.ItemContainerGenerator.ContainerFromItem(firstRestored) as DataGridRow;
                                row?.Focus();
                            }), System.Windows.Threading.DispatcherPriority.Background);
                        }
                    }
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Y && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                // Redo
                if (_redoDeleteStack.Count > 0)
                {
                    var itemsToRemove = _redoDeleteStack.Pop();
                    _undoDeleteStack.Push(itemsToRemove);
                    RemoveDownloadMissingChapterRows(itemsToRemove);
                    foreach (var item in itemsToRemove)
                    {
                        _scrapedItems.Remove(item);
                    }
                    RenumberResultOrder();
                    lblLinkCount.Text = _scrapedItems.Count.ToString();
                    Log($"Redo: Xóa lại {itemsToRemove.Count} truyện.");
                    lblStatus.Text = $"Redo: Xóa {itemsToRemove.Count} truyện.";
                    RecalculateDuplicates();
                    if (_isResultsThumbnailViewEnabled)
                    {
                        RebuildThumbnailResultsView();
                    }

                    // Giữ focus vào item gần nhất
                    if (_isResultsThumbnailViewEnabled)
                    {
                        if (lbResultsThumbnail.Items.Count > 0)
                        {
                            lbResultsThumbnail.SelectedIndex = Math.Max(0, lbResultsThumbnail.SelectedIndex);
                            var sel = lbResultsThumbnail.SelectedItem;
                            if (sel != null)
                            {
                                lbResultsThumbnail.ScrollIntoView(sel);
                                Dispatcher.BeginInvoke(new Action(() => {
                                    var container = lbResultsThumbnail.ItemContainerGenerator.ContainerFromItem(sel) as ListBoxItem;
                                    container?.Focus();
                                }), System.Windows.Threading.DispatcherPriority.Background);
                            }
                        }
                    }
                    else
                    {
                        if (dgResults.Items.Count > 0)
                        {
                            dgResults.SelectedIndex = Math.Max(0, dgResults.SelectedIndex);
                            var sel = dgResults.SelectedItem;
                            if (sel != null)
                            {
                                dgResults.ScrollIntoView(sel);
                                Dispatcher.BeginInvoke(new Action(() => {
                                    var row = dgResults.ItemContainerGenerator.ContainerFromItem(sel) as DataGridRow;
                                    row?.Focus();
                                }), System.Windows.Threading.DispatcherPriority.Background);
                            }
                        }
                    }
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Home)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ||
                    (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    return;
                }
                dgResults.SelectedIndex = 0;
                dgResults.ScrollIntoView(dgResults.Items[0]);
                e.Handled = true;
            }
            else if (e.Key == Key.End)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ||
                    (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    return;
                }
                int lastIndex = dgResults.Items.Count - 1;
                dgResults.SelectedIndex = lastIndex;
                dgResults.ScrollIntoView(dgResults.Items[lastIndex]);
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                DeleteSelectedItems();
                e.Handled = true;
            }
            else if (e.Key == Key.Space)
            {
                if (dgResults.SelectedItems.Count > 0)
                {
                    var firstItem = dgResults.SelectedItems.Cast<GalleryItem>().FirstOrDefault();
                    if (firstItem != null)
                    {
                        bool targetState = !firstItem.IsChecked;
                        foreach (var item in dgResults.SelectedItems.Cast<GalleryItem>())
                        {
                            item.IsChecked = targetState;
                        }
                    }
                    e.Handled = true;
                }
            }
        }

        private void DeleteSelectedItems()
        {
            var activeGrid = dgResults;
            var activeListBox = lbResultsThumbnail;
            bool isThumbnail = _isResultsThumbnailViewEnabled;

            int selectedIndex = -1;
            if (isThumbnail && activeListBox != null)
            {
                selectedIndex = activeListBox.SelectedIndex;
            }
            else if (activeGrid != null)
            {
                selectedIndex = activeGrid.SelectedIndex;
            }

            var itemsToRemove = isThumbnail 
                ? activeListBox.SelectedItems.Cast<GalleryItem>().ToList()
                : activeGrid.SelectedItems.Cast<GalleryItem>().ToList();

            if (itemsToRemove.Count == 0) return;
            
            // Push to Undo Stack
            _undoDeleteStack.Push(itemsToRemove);
            _redoDeleteStack.Clear(); // Clear Redo since new action performed

            RemoveDownloadMissingChapterRows(itemsToRemove);
            foreach (var item in itemsToRemove)
            {
                _scrapedItems.Remove(item);
            }
            
            lblLinkCount.Text = _scrapedItems.Count.ToString();
            Log($"Deleted {itemsToRemove.Count} selected item(s).");
            lblStatus.Text = $"Deleted {itemsToRemove.Count} item(s).";
            
            RecalculateDuplicates();

            if (isThumbnail && activeListBox != null)
            {
                RebuildThumbnailResultsView();
                if (activeListBox.Items.Count > 0)
                {
                    int newIndex = Math.Min(selectedIndex, activeListBox.Items.Count - 1);
                    if (newIndex >= 0)
                    {
                        activeListBox.SelectedIndex = newIndex;
                        var item = activeListBox.SelectedItem;
                        if (item != null)
                        {
                            activeListBox.ScrollIntoView(item);
                            Dispatcher.BeginInvoke(new Action(() => {
                                var container = activeListBox.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;
                                container?.Focus();
                            }), System.Windows.Threading.DispatcherPriority.Background);
                        }
                    }
                }
            }
            else if (activeGrid != null)
            {
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
                            Dispatcher.BeginInvoke(new Action(() => {
                                var row = activeGrid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
                                row?.Focus();
                            }), System.Windows.Threading.DispatcherPriority.Background);
                        }
                    }
                }
            }
        }

        private void DgResults_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Chỉ xử lý double-click
            if (e.ClickCount < 2) return;

            DependencyObject dep = e.OriginalSource as DependencyObject;
            if (dep == null)
            {
                Log("[DoubleClick] OriginalSource null");
                return;
            }

            Log($"[DoubleClick] OriginalSource type={dep.GetType().Name}");

            // Tìm DataGridCell trong visual tree
            DataGridCell cell = FindVisualParent<DataGridCell>(dep);
            if (cell == null)
            {
                Log("[DoubleClick] Không tìm thấy DataGridCell trong visual tree");
                return;
            }

            if (cell.Column == null)
            {
                Log("[DoubleClick] cell.Column null");
                return;
            }

            GalleryItem item = GetGalleryItemFromDependencyObject(dep);
            if (item == null)
            {
                Log("[DoubleClick] Không tìm thấy GalleryItem");
                return;
            }

            string sortPath = cell.Column.SortMemberPath;
            Log($"[DoubleClick] Column Type={cell.Column.GetType().Name}, SortMemberPath={sortPath}");

            bool isDetails = cell.Column == colGalleryDetails || sortPath == "Name";
            bool isProcess = cell.Column == colProcess || sortPath == "ProcessSortText";

            if (isDetails)
            {
                Log($"[DoubleClick] Mở link: {item.Link}");
                OpenGalleryItemLink(item, e);
            }
            else if (isProcess)
            {
                string targetFolder = ResolveBestFolderForGalleryItem(item);
                Log($"[DoubleClick] Mở folder: {targetFolder}");
                if (!string.IsNullOrWhiteSpace(targetFolder) && System.IO.Directory.Exists(targetFolder))
                {
                    ShellFolderLauncher.TryOpenFolder(targetFolder, out _);
                    e.Handled = true;
                }
            }
        }

        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = child;
            while (parentObject != null)
            {
                if (parentObject is T parent)
                    return parent;
                parentObject = VisualTreeHelper.GetParent(parentObject);
            }
            return null;
        }

        private void ChkResultsPresentation_Click(object sender, RoutedEventArgs e)
        {
            // Giữ lại để tránh lỗi compile nếu có chỗ khác gọi
        }

        private void CmbPresentationMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var cmb = sender as ComboBox;
            if (cmb == null) cmb = cmbPresentationMode ?? cmbSubPresentationMode;
            if (cmb == null) return;

            int mode = cmb.SelectedIndex;
            if (mode < 0) return;

            if (mode == 0) // List view
            {
                if (colThumb != null) colThumb.Visibility = Visibility.Collapsed;
                if (dgResults != null) dgResults.MinRowHeight = 32;
                SetResultsPresentationMode(false, false);
            }
            else if (mode == 1) // Thumbnail view
            {
                SetResultsPresentationMode(true, true);
            }
            else if (mode == 2) // List + thumbnail column
            {
                if (colThumb != null) colThumb.Visibility = Visibility.Visible;
                if (dgResults != null) dgResults.MinRowHeight = 60;
                SetResultsPresentationMode(false, false);
            }
        }

        private void DgResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingResultsThumbnailSelection || dgResults == null || lbResultsThumbnail == null)
            {
                return;
            }

            SyncThumbnailSelectionFromResults();
            if (ShouldAutoScrollThumbnailSelection())
            {
                ScrollThumbnailSelectionIntoView();
            }

            // Tự động load thumbnail cho các item được chọn nếu chưa có
            if (dgResults.SelectedItems.Count > 0)
            {
                var itemsToPrefetch = new System.Collections.Generic.List<GalleryItem>();
                foreach (var item in dgResults.SelectedItems.OfType<GalleryItem>())
                {
                    if (SupportsHoverPreview(item) && !item.HasHoverPreviewThumbnailFile)
                    {
                        itemsToPrefetch.Add(item);
                    }
                }
                if (itemsToPrefetch.Count > 0)
                {
                    PrefetchGalleryHoverPreview(itemsToPrefetch);
                }
            }
        }

        private void SetResultsPresentationMode(bool showThumbnailView, bool shouldPrefetch)
        {
            _isResultsThumbnailViewEnabled = showThumbnailView;

            if (dgResults != null)
            {
                dgResults.Visibility = showThumbnailView ? Visibility.Collapsed : Visibility.Visible;
            }

            if (lbResultsThumbnail != null)
            {
                lbResultsThumbnail.Visibility = showThumbnailView ? Visibility.Visible : Visibility.Collapsed;
                ApplyThumbnailDensity();
            }

            UpdateResultsPresentationButtons();

            if (showThumbnailView)
            {
                EnsureThumbnailResultsViewInitialized();
                
                // Sử dụng Dispatcher.BeginInvoke ưu tiên Background để tránh khóa/treo UI Thread 
                // khi switch view trong cửa sổ mới lúc đang download
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        RebuildThumbnailResultsView();
                        SyncThumbnailSelectionFromResults();
                        ScrollThumbnailSelectionIntoView();
                        if (shouldPrefetch)
                        {
                            System.Threading.Tasks.Task.Run(() =>
                            {
                                try
                                {
                                    PrefetchAllThumbnailResults();
                                }
                                catch { }
                            });
                        }
                        lbResultsThumbnail?.Focus();
                    }
                    catch { }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            else
            {
                CancelGalleryHoverPreview();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (dgResults != null)
                        {
                            dgResults.ApplyTemplate();
                            var dgScrollViewer = FindVisualChild<ScrollViewer>(dgResults);
                            if (dgScrollViewer != null)
                            {
                                dgScrollViewer.ScrollChanged -= DgResults_ScrollChanged;
                                dgScrollViewer.ScrollChanged += DgResults_ScrollChanged;
                            }
                        }
                        ScrollResultsSelectionIntoView();
                        dgResults?.Focus();
                    }
                    catch { }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void UpdateResultsPresentationButtons()
        {
            UpdateGalleryPopupPreviewButtonState();
        }

        private void PrefetchAllThumbnailResults()
        {
            PrefetchAllScrapedItemsPreviewCache();
            UpdateThumbnailsVirtualizationWindow();
        }

        private void DgResults_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange == 0) return;
            UpdateThumbnailsVirtualizationWindow();
        }

        private void ResultsThumbnailItems_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (_scrapedItems == null || _scrapedItems.Count == 0)
            {
                if (tglClearCookieAndRetry != null && tglClearCookieAndRetry.IsChecked == true)
                {
                    tglClearCookieAndRetry.IsChecked = false;
                }
            }

            QueueCheckedDownloadsForActiveSession();
            UpdateEmptyStateVisibility();
            UpdateGlobalProgressBar();

            PrefetchAllScrapedItemsPreviewCache();
            UpdateThumbnailsVirtualizationWindow();

            if (!_isResultsThumbnailViewEnabled)
            {
                return;
            }

            if (_thumbnailRefreshTimer == null)
            {
                _thumbnailRefreshTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background);
                _thumbnailRefreshTimer.Interval = TimeSpan.FromMilliseconds(300);
                _thumbnailRefreshTimer.Tick += (s, ev) =>
                {
                    _thumbnailRefreshTimer.Stop();
                    RebuildThumbnailResultsView();
                };
            }
            _thumbnailRefreshTimer.Stop();
            _thumbnailRefreshTimer.Start();
        }

        private void LbResultsThumbnail_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingResultsThumbnailSelection || dgResults == null || lbResultsThumbnail == null)
            {
                return;
            }

            SyncResultsSelectionFromThumbnail();
        }

        private void LbResultsThumbnail_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_isResultsThumbnailViewEnabled || lbResultsThumbnail == null)
            {
                return;
            }

            bool shouldAutoScroll = !IsRangeSelectionModifierActive();
            SyncResultsSelectionFromThumbnail();
            DgResults_PreviewKeyDown(sender, e);
            SyncThumbnailSelectionFromResults();
            if (shouldAutoScroll)
            {
                ScrollThumbnailSelectionIntoView();
            }
        }

        private void LbResultsThumbnail_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (!_isResultsThumbnailViewEnabled || lbResultsThumbnail == null)
            {
                return;
            }

            SyncResultsSelectionFromThumbnail();
            DgResults_PreviewTextInput(sender, e);
            SyncThumbnailSelectionFromResults();
            ScrollThumbnailSelectionIntoView();
        }

        private void LbResultsThumbnail_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            GalleryItem item = GetGalleryItemFromDependencyObject(e.OriginalSource as DependencyObject);
            if (item == null)
            {
                return;
            }

            OpenGalleryItemLink(item, e);
        }

        private void ResultsThumbnailItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is ListBoxItem itemContainer))
            {
                return;
            }

            itemContainer.Focus();

            if (lbResultsThumbnail != null && itemContainer.DataContext is GalleryItem clickedItem)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    if (lbResultsThumbnail.SelectedItems.Contains(clickedItem))
                    {
                        lbResultsThumbnail.SelectedItems.Remove(clickedItem);
                    }
                    else
                    {
                        lbResultsThumbnail.SelectedItems.Add(clickedItem);
                        lbResultsThumbnail.SelectedItem = clickedItem;
                    }

                    SyncResultsSelectionFromThumbnail();
                    e.Handled = true;
                    return;
                }

                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                {
                    int currentIndex = lbResultsThumbnail.Items.IndexOf(clickedItem);
                    int anchorIndex = lbResultsThumbnail.SelectedIndex;
                    if (anchorIndex < 0)
                    {
                        anchorIndex = currentIndex;
                    }

                    lbResultsThumbnail.SelectedItems.Clear();
                    for (int i = Math.Min(anchorIndex, currentIndex); i <= Math.Max(anchorIndex, currentIndex); i++)
                    {
                        if (lbResultsThumbnail.Items[i] is GalleryItem rangeItem)
                        {
                            lbResultsThumbnail.SelectedItems.Add(rangeItem);
                        }
                    }

                    lbResultsThumbnail.SelectedItem = clickedItem;
                    SyncResultsSelectionFromThumbnail();
                    e.Handled = true;
                    return;
                }

                if (!lbResultsThumbnail.SelectedItems.Contains(clickedItem) || lbResultsThumbnail.SelectedItems.Count > 1)
                {
                    lbResultsThumbnail.SelectedItems.Clear();
                    lbResultsThumbnail.SelectedItem = clickedItem;
                    SyncResultsSelectionFromThumbnail();
                }

                // Tự động load thumbnail khi click chuột trái
                if (SupportsHoverPreview(clickedItem) && !clickedItem.HasHoverPreviewThumbnailFile)
                {
                    PrefetchGalleryHoverPreview(new System.Collections.Generic.List<GalleryItem> { clickedItem });
                }
            }

            if (e.ButtonState != MouseButtonState.Pressed || !IsThumbnailDragCandidate(e.OriginalSource as DependencyObject))
            {
                _resultsDragItem = null;
                return;
            }

            _resultsDragStartPoint = e.GetPosition(null);
            _resultsDragItem = itemContainer.DataContext as GalleryItem;

            if (lbResultsThumbnail != null && lbResultsThumbnail.SelectedItems.Contains(_resultsDragItem) && lbResultsThumbnail.SelectedItems.Count > 1)
            {
                e.Handled = true;
            }
        }

        private void ResultsThumbnailItem_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _resultsDragItem == null || !(sender is ListBoxItem itemContainer))
            {
                return;
            }

            Point currentPosition = e.GetPosition(null);
            if (Math.Abs(currentPosition.X - _resultsDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(currentPosition.Y - _resultsDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            try
            {
                DragDrop.DoDragDrop(itemContainer, _resultsDragItem, DragDropEffects.Move);
            }
            finally
            {
                _resultsDragItem = null;
            }
        }

        private void ResultsThumbnailItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is ListBoxItem itemContainer) || !(itemContainer.DataContext is GalleryItem item) || lbResultsThumbnail == null)
            {
                return;
            }

            if (!lbResultsThumbnail.SelectedItems.Contains(item))
            {
                lbResultsThumbnail.SelectedItems.Clear();
                lbResultsThumbnail.SelectedItem = item;
                SyncResultsSelectionFromThumbnail();
            }
        }

        private void LbResultsThumbnail_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(GalleryItem)))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.Effects = DragDropEffects.Move;
            e.Handled = true;

            // Auto scroll Thumbnail view khi kéo lên/xuống mép
            if (sender is FrameworkElement element)
            {
                Point pos = e.GetPosition(element);
                double tolerance = 35;
                ScrollViewer scrollViewer = FindVisualChild<ScrollViewer>(lbResultsThumbnail);
                if (scrollViewer != null)
                {
                    if (pos.Y < tolerance)
                    {
                        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - 3);
                    }
                    else if (pos.Y > element.ActualHeight - tolerance)
                    {
                        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + 3);
                    }
                }
            }

            var targetContainer = GetResultsThumbnailItemContainer(e.OriginalSource as DependencyObject);
            if (_lastDragOverThumbnailContainer != targetContainer)
            {
                ClearDragOverHighlight();
                _lastDragOverThumbnailContainer = targetContainer;
                if (_lastDragOverThumbnailContainer != null)
                {
                    _lastDragOverThumbnailContainer.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00F0FF"));
                    _lastDragOverThumbnailContainer.BorderThickness = new Thickness(2);
                }
            }
        }

        private void LbResultsThumbnail_DragLeave(object sender, DragEventArgs e)
        {
            ClearDragOverHighlight();
        }

        private void LbResultsThumbnail_Drop(object sender, DragEventArgs e)
        {
            ClearDragOverHighlight();

            if (!e.Data.GetDataPresent(typeof(GalleryItem)))
            {
                return;
            }

            GalleryItem sourceItem = e.Data.GetData(typeof(GalleryItem)) as GalleryItem;
            ListBoxItem targetContainer = GetResultsThumbnailItemContainer(e.OriginalSource as DependencyObject);
            GalleryItem targetItem = targetContainer?.DataContext as GalleryItem;

            if (sourceItem == null)
            {
                return;
            }

            List<GalleryItem> dragItems = new List<GalleryItem>();
            if (lbResultsThumbnail != null && lbResultsThumbnail.SelectedItems.Contains(sourceItem))
            {
                dragItems = lbResultsThumbnail.SelectedItems.Cast<GalleryItem>()
                    .Where(x => x != null)
                    .OrderBy(x => _scrapedItems.IndexOf(x))
                    .ToList();
            }
            else
            {
                dragItems.Add(sourceItem);
            }

            int targetIndex = targetItem != null ? _scrapedItems.IndexOf(targetItem) : _scrapedItems.Count - 1;
            string message = dragItems.Count == 1
                ? $"Moved '{sourceItem.DisplayName}' in gallery list."
                : $"Moved {dragItems.Count} items in gallery list.";

            MoveResultItems(dragItems, targetIndex, message);

            SyncThumbnailSelectionFromResults();
            ScrollThumbnailSelectionIntoView();

            if (sourceItem != null)
            {
                Dispatcher.BeginInvoke(new Action(() => {
                    var container = lbResultsThumbnail.ItemContainerGenerator.ContainerFromItem(sourceItem) as ListBoxItem;
                    container?.Focus();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void SyncResultsSelectionFromThumbnail()
        {
            if (dgResults == null || lbResultsThumbnail == null)
            {
                return;
            }

            _isSyncingResultsThumbnailSelection = true;
            try
            {
                dgResults.SelectedItems.Clear();
                foreach (GalleryItem item in lbResultsThumbnail.SelectedItems.Cast<GalleryItem>())
                {
                    dgResults.SelectedItems.Add(item);
                }

                if (lbResultsThumbnail.SelectedItem is GalleryItem selectedItem)
                {
                    dgResults.SelectedItem = selectedItem;
                    dgResults.CurrentItem = selectedItem;
                }
            }
            finally
            {
                _isSyncingResultsThumbnailSelection = false;
            }
        }

        private void SyncThumbnailSelectionFromResults()
        {
            if (dgResults == null || lbResultsThumbnail == null)
            {
                return;
            }

            EnsureThumbnailSelectionVisible();

            _isSyncingResultsThumbnailSelection = true;
            try
            {
                lbResultsThumbnail.SelectedItems.Clear();
                foreach (GalleryItem item in dgResults.SelectedItems.Cast<GalleryItem>())
                {
                    lbResultsThumbnail.SelectedItems.Add(item);
                }

                lbResultsThumbnail.SelectedItem = dgResults.SelectedItem;
            }
            finally
            {
                _isSyncingResultsThumbnailSelection = false;
            }
        }

        private void ScrollThumbnailSelectionIntoView()
        {
            if (lbResultsThumbnail?.SelectedItem != null)
            {
                EnsureThumbnailSelectionVisible();
                lbResultsThumbnail.ScrollIntoView(lbResultsThumbnail.SelectedItem);
            }
        }

        private void ScrollResultsSelectionIntoView()
        {
            if (dgResults?.SelectedItem == null)
            {
                return;
            }

            dgResults.ScrollIntoView(dgResults.SelectedItem);
        }

        private bool ShouldAutoScrollThumbnailSelection()
        {
            if (!_isResultsThumbnailViewEnabled)
            {
                return false;
            }

            return !IsRangeSelectionModifierActive();
        }

        private static bool IsRangeSelectionModifierActive()
        {
            ModifierKeys modifiers = Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift);
            return modifiers != ModifierKeys.None;
        }

        private void EnsureThumbnailResultsViewInitialized()
        {
            if (lbResultsThumbnail == null)
            {
                return;
            }

            if (!ReferenceEquals(lbResultsThumbnail.ItemsSource, _thumbnailVisibleItems))
            {
                lbResultsThumbnail.ItemsSource = _thumbnailVisibleItems;
            }

            if (_resultsThumbnailScrollViewer != null)
            {
                return;
            }

            lbResultsThumbnail.ApplyTemplate();
            _resultsThumbnailScrollViewer = FindVisualChild<ScrollViewer>(lbResultsThumbnail);
            if (_resultsThumbnailScrollViewer != null)
            {
                _resultsThumbnailScrollViewer.ScrollChanged -= ResultsThumbnailScrollViewer_ScrollChanged;
                _resultsThumbnailScrollViewer.ScrollChanged += ResultsThumbnailScrollViewer_ScrollChanged;
            }
        }

        private void RebuildThumbnailResultsView()
        {
            if (!_isResultsThumbnailViewEnabled || lbResultsThumbnail == null)
            {
                return;
            }

            EnsureThumbnailResultsViewInitialized();
            ApplyThumbnailDensity();
            List<GalleryItem> orderedItems = GetThumbnailSourceItems();
            int targetCount = Math.Min(orderedItems.Count, GetThumbnailColumnCount() * ThumbnailInitialRows);

            _thumbnailVisibleItems.Clear();
            for (int i = 0; i < targetCount; i++)
            {
                _thumbnailVisibleItems.Add(orderedItems[i]);
            }

            // Chạy Prefetch bất đồng bộ ở background thread để tránh Task.Delay/Jitter chặn UI Thread gây Not Responding
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    PrefetchAllThumbnailResults();
                }
                catch { }
            });
            EnsureThumbnailSelectionVisible();
        }

        private List<GalleryItem> GetThumbnailSourceItems()
        {
            ICollectionView view = ResultsView;
            if (view == null)
            {
                return new List<GalleryItem>();
            }

            return view.Cast<object>()
                .OfType<GalleryItem>()
                .ToList();
        }

        private void LoadMoreThumbnailResults(int additionalRows)
        {
            if (!_isResultsThumbnailViewEnabled)
            {
                return;
            }

            List<GalleryItem> orderedItems = GetThumbnailSourceItems();
            if (_thumbnailVisibleItems.Count >= orderedItems.Count)
            {
                return;
            }

            int targetCount = Math.Min(orderedItems.Count, _thumbnailVisibleItems.Count + (GetThumbnailColumnCount() * additionalRows));
            List<GalleryItem> newItems = new List<GalleryItem>();
            for (int i = _thumbnailVisibleItems.Count; i < targetCount; i++)
            {
                GalleryItem item = orderedItems[i];
                _thumbnailVisibleItems.Add(item);
                newItems.Add(item);
            }

            if (newItems.Count > 0)
            {
                PrefetchGalleryHoverPreview(newItems.Where(SupportsHoverPreview));
            }
        }

        private void EnsureThumbnailSelectionVisible()
        {
            if (!_isResultsThumbnailViewEnabled || dgResults?.SelectedItem == null)
            {
                return;
            }

            GalleryItem selectedItem = dgResults.SelectedItem as GalleryItem;
            if (selectedItem == null)
            {
                return;
            }

            List<GalleryItem> orderedItems = GetThumbnailSourceItems();
            int selectedIndex = orderedItems.IndexOf(selectedItem);
            if (selectedIndex < 0)
            {
                return;
            }

            int requiredCount = selectedIndex + 1;
            while (_thumbnailVisibleItems.Count < requiredCount)
            {
                int beforeCount = _thumbnailVisibleItems.Count;
                LoadMoreThumbnailResults(ThumbnailLoadMoreRows);
                if (_thumbnailVisibleItems.Count == beforeCount)
                {
                    break;
                }
            }
        }

        private void ResultsThumbnailScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (!_isResultsThumbnailViewEnabled)
            {
                return;
            }

            if (e.VerticalChange == 0)
            {
                return;
            }

            if (e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 2)
            {
                LoadMoreThumbnailResults(ThumbnailLoadMoreRows);
            }

            UpdateThumbnailsVirtualizationWindow();
        }

        private void UpdateThumbnailsVirtualizationWindow()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(UpdateThumbnailsVirtualizationWindow));
                return;
            }

            try
            {
                var sortedItems = GetThumbnailSourceItems();
                int totalCount = sortedItems.Count;
                if (totalCount == 0) return;

                int firstVisibleIndex = 0;
                int lastVisibleIndex = 30; // Mặc định nạp 30 item đầu nếu chưa lấy được ScrollViewer

                if (_isResultsThumbnailViewEnabled && lbResultsThumbnail != null)
                {
                    var scrollViewer = FindVisualChild<ScrollViewer>(lbResultsThumbnail);
                    if (scrollViewer != null)
                    {
                        int columns = GetThumbnailColumnCount();
                        int firstRow = (int)Math.Max(0, scrollViewer.VerticalOffset);
                        int lastRow = firstRow + (int)Math.Ceiling(scrollViewer.ViewportHeight);

                        firstVisibleIndex = firstRow * columns;
                        lastVisibleIndex = (lastRow + 1) * columns;
                    }
                }
                else if (dgResults != null && dgResults.Visibility == Visibility.Visible)
                {
                    var scrollViewer = FindVisualChild<ScrollViewer>(dgResults);
                    if (scrollViewer != null)
                    {
                        firstVisibleIndex = (int)Math.Max(0, scrollViewer.VerticalOffset);
                        lastVisibleIndex = firstVisibleIndex + (int)Math.Ceiling(scrollViewer.ViewportHeight);
                    }
                }

                int minIndex = Math.Max(0, firstVisibleIndex - 30);
                int maxIndex = Math.Min(totalCount - 1, lastVisibleIndex + 30);

                for (int i = 0; i < totalCount; i++)
                {
                    var item = sortedItems[i];
                    if (item == null) continue;

                    if (i >= minIndex && i <= maxIndex)
                    {
                        if (item.HoverPreviewThumbnailImageSource == null && System.IO.File.Exists(item.HoverPreviewThumbnailLocalPath ?? item.HoverPreviewLocalPath))
                        {
                            var img = item.HoverPreviewThumbnailImageSource;
                        }
                    }
                    else
                    {
                        if (item.HoverPreviewThumbnailImageSource != null)
                        {
                            item.ResetHoverPreviewCache();
                        }
                    }
                }
            }
            catch { }
        }

        private static bool IsThumbnailDragCandidate(DependencyObject source)
        {
            while (source != null)
            {
                if (source is FrameworkElement fe && string.Equals(fe.Tag as string, "drag_handle", StringComparison.Ordinal))
                {
                    return true;
                }
                source = VisualTreeHelper.GetParent(source);
            }
            return false;
        }

        private ListBoxItem GetResultsThumbnailItemContainer(DependencyObject source)
        {
            while (source != null && !(source is ListBoxItem))
            {
                source = VisualTreeHelper.GetParent(source);
            }

            return source as ListBoxItem;
        }

        private GalleryItem GetGalleryItemFromDependencyObject(DependencyObject source)
        {
            while (source != null)
            {
                if (source is FrameworkElement element && element.DataContext is GalleryItem item)
                {
                    return item;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return null;
        }

        private void OpenGalleryItemLink(GalleryItem item, RoutedEventArgs eventArgs = null)
        {
            if (item == null || string.IsNullOrEmpty(item.Link))
            {
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = item.Link,
                    UseShellExecute = true
                });
                Log($"Opened link: {item.Link}");
                if (eventArgs != null)
                {
                    eventArgs.Handled = true;
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to open link: {ex.Message}");
            }
        }

        private void MissingChapterStatus_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is FrameworkElement element) || !(element.DataContext is GalleryItem item) || !item.HasMissingChapterIssue)
            {
                return;
            }

            if (JumpToDownloadMissingChapterRow(item))
            {
                e.Handled = true;
            }
        }

        private void MenuCheckSelected_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in dgResults.SelectedItems.Cast<GalleryItem>())
            {
                item.IsChecked = true;
            }
            Log("Checked selected items.");
        }

        private void MenuUncheckSelected_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in dgResults.SelectedItems.Cast<GalleryItem>())
            {
                item.IsChecked = false;
            }
            Log("Unchecked selected items.");
        }

        private void MenuInvertChecked_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _scrapedItems)
            {
                item.IsChecked = !item.IsChecked;
            }
            Log("Inverted checked status for all items.");
        }

        private void MenuDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelectedItems();
        }

        private void MenuSearchWithOtherWebsites_Click(object sender, RoutedEventArgs e)
        {
            List<GalleryItem> selectedItems = new List<GalleryItem>();
            if (_isResultsThumbnailViewEnabled && lbResultsThumbnail != null)
            {
                selectedItems = lbResultsThumbnail.SelectedItems.Cast<GalleryItem>().Where(x => x != null).ToList();
            }
            else if (dgResults != null)
            {
                selectedItems = dgResults.SelectedItems.Cast<GalleryItem>().Where(x => x != null).ToList();
            }

            if (selectedItems.Count == 0)
            {
                return;
            }

            string namesText = string.Join(Environment.NewLine, selectedItems.Select(item => item.DisplayName ?? item.Name).Where(name => !string.IsNullOrWhiteSpace(name)));
            if (txtSourceSearchBook != null)
            {
                txtSourceSearchBook.Text = namesText;
            }

            SelectAppSection(AppSection.ChooseSource);

            if (tabLeftPanel != null && tabSourceSearchRootItem != null)
            {
                tabLeftPanel.SelectedItem = tabSourceSearchRootItem;
            }
            Log($"Moved {selectedItems.Count} selected book name(s) to Search tab.");
        }

        private void MenuCopySelectedLinks_Click(object sender, RoutedEventArgs e)
        {
            if (dgResults.SelectedItems.Count == 0) return;
            var items = dgResults.SelectedItems.Cast<GalleryItem>().ToList();
            string text = string.Join("\r\n", items.Select(item => item.Link));
            Clipboard.SetText(text);
            Log($"Copied {items.Count} selected link(s) to clipboard.");
        }

        private string _searchBuffer = "";
        private DateTime _lastKeyPressTime = DateTime.MinValue;

        private void DgResults_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (IsTypingInEditableTextBox())
            {
                return;
            }

            if (string.IsNullOrEmpty(e.Text)) return;

            DateTime now = DateTime.Now;
            if ((now - _lastKeyPressTime).TotalMilliseconds > 1000)
            {
                _searchBuffer = "";
            }
            _lastKeyPressTime = now;
            _searchBuffer += e.Text;

            var items = GetGalleryItemsSnapshot();
            var match = items.FirstOrDefault(item => item.Name.StartsWith(_searchBuffer, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                match = items.FirstOrDefault(item => item.Name.IndexOf(_searchBuffer, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (match != null)
            {
                dgResults.SelectedItem = match;
                dgResults.ScrollIntoView(match);
                
                var row = (DataGridRow)dgResults.ItemContainerGenerator.ContainerFromItem(match);
                if (row != null)
                {
                    row.Focus();
                }
            }

            e.Handled = true;
        }

        private bool IsTypingInEditableTextBox()
        {
            DependencyObject focused = Keyboard.FocusedElement as DependencyObject;
            while (focused != null)
            {
                if (focused is TextBox textBox)
                {
                    return !ReferenceEquals(textBox, txtFilter);
                }

                if (focused is PasswordBox)
                {
                    return true;
                }

                if (focused is ComboBox comboBox && comboBox.IsEditable)
                {
                    return true;
                }

                focused = VisualTreeHelper.GetParent(focused);
            }

            return false;
        }

        private static readonly Regex BracketRegex1 = new Regex(@"\[[^\]]*\]", RegexOptions.Compiled);
        private static readonly Regex BracketRegex2 = new Regex(@"\{[^\}]*\}", RegexOptions.Compiled);
        private static readonly Regex BracketRegex3 = new Regex(@"\([^\)]*\)", RegexOptions.Compiled);

        private static readonly Regex CommonKeywordsRegex = new Regex(
            @"(?:extra\s+version|copy\s+of|part\s+\d+|part\d+|pt\s+\d+|pt\d+|vol\s+\d+|vol\d+|ch\s+\d+|ch\d+|chap\s+\d+|chap\d+|chapter\s+\d+|chapter\d+|extra|extras|version|rewrite|copy|doujin|dj|\bch\b|\bchap\b|\bpart\b|\bpt\b|\bvol\b)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex VariantKeywordsRegex = new Regex(
            @"(?:minidoujin|doujinshi|decensored|uncensored|censored|full\s*color|fullcolor|colorized|colored|color)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex NumberBoundaryRegex = new Regex(@"\b\d+\b", RegexOptions.Compiled);
        private static readonly Regex NonAlphaNumericRegex = new Regex(@"[^a-z0-9]", RegexOptions.Compiled);

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, System.Collections.Generic.HashSet<string>> _hitomiTagsCache =
            new System.Runtime.CompilerServices.ConditionalWeakTable<object, System.Collections.Generic.HashSet<string>>();

        public static string GetSimilarityCore(string name)
        {
            return GetSimilarityCore(name, false);
        }

        public static string GetSimilarityCore(string name, bool mergeCensorshipColorVariants)
        {
            if (string.IsNullOrEmpty(name)) return "";

            string core = name.ToLowerInvariant();
            core = BracketRegex1.Replace(core, "");
            core = BracketRegex2.Replace(core, "");
            core = BracketRegex3.Replace(core, "");

            core = CommonKeywordsRegex.Replace(core, "");

            if (mergeCensorshipColorVariants)
            {
                core = VariantKeywordsRegex.Replace(core, "");
            }

            core = NumberBoundaryRegex.Replace(core, "");
            core = NonAlphaNumericRegex.Replace(core, "");
            return core.Trim();
        }

        private static System.Collections.Generic.HashSet<string> GetHitomiTags(GalleryItem item)
        {
            if (item?.Tag == null)
            {
                return new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            if (_hitomiTagsCache.TryGetValue(item.Tag, out var cachedTags))
            {
                return cachedTags;
            }

            var tagSet = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                    foreach (var t in tagsArray)
                    {
                        string tagName = t["tag"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(tagName))
                        {
                            tagSet.Add(tagName.Trim());
                        }
                    }
                }
            }
            catch { }

            // Đảm bảo không ghi đè trùng lặp và ghi vào cache
            _hitomiTagsCache.Remove(item.Tag);
            _hitomiTagsCache.Add(item.Tag, tagSet);

            return tagSet;
        }

        public static bool HasCensorshipColorInTags(GalleryItem item)
        {
            var tags = GetHitomiTags(item);
            return tags.Contains("uncensored") || tags.Contains("full color");
        }

        public static bool HasUncensoredInTags(GalleryItem item)
        {
            var tags = GetHitomiTags(item);
            return tags.Contains("uncensored");
        }

        public static bool HasFullColorInTags(GalleryItem item)
        {
            var tags = GetHitomiTags(item);
            return tags.Contains("full color");
        }

        public static string AppendMetadataToName(string name, string metadata)
        {
            if (string.IsNullOrWhiteSpace(name)) return name;
            if (name.IndexOf(metadata, StringComparison.OrdinalIgnoreCase) >= 0) return name;

            int squareBracketIndex = name.IndexOf('[');
            if (squareBracketIndex >= 0)
            {
                string prefix = name.Substring(0, squareBracketIndex).Trim();
                string suffix = name.Substring(squareBracketIndex);
                return $"{prefix} ({metadata}) {suffix}";
            }
            return $"{name.Trim()} ({metadata})";
        }

        public static void RunDuplicateDetection(IEnumerable<GalleryItem> items)
        {
            if (items == null) return;
            var itemList = items.ToList();

            // 1. Tự động chèn metadata vào tên truyện dựa trên tag Hitomi/Nhentai trước
            foreach (var item in itemList)
            {
                bool isHitomi = string.Equals(item.SourceDomain, "hitomi.la", StringComparison.OrdinalIgnoreCase) ||
                                (item.Link != null && item.Link.IndexOf("hitomi.la", StringComparison.OrdinalIgnoreCase) >= 0);
                bool isNhentai = string.Equals(item.SourceDomain, "nhentai.net", StringComparison.OrdinalIgnoreCase) ||
                                 (item.Link != null && item.Link.IndexOf("nhentai.net", StringComparison.OrdinalIgnoreCase) >= 0);
                if (isHitomi || isNhentai)
                {
                    if (HasUncensoredInTags(item) && !HasUncensoredVariant(item.Name))
                    {
                        item.Name = AppendMetadataToName(item.Name, "uncensored");
                    }
                    if (HasFullColorInTags(item) && !HasFullColorVariant(item.Name))
                    {
                        item.Name = AppendMetadataToName(item.Name, "full color");
                    }
                }
            }

            var groups = itemList
                .GroupBy(item => GetSimilarityCore(item.Name, false))
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .ToList();

            foreach (var item in itemList)
            {
                item.IsDuplicate = false;
                item.IsCensorshipColorDuplicate = false;
                item.IsCensorshipUncensoredVariant = false;
                item.IsCensorshipFullColorVariant = false;
                item.IsNumberedVariantDuplicate = false;
            }

            foreach (var group in groups)
            {
                if (group.Count() > 1)
                {
                    string suffixVariantCore = GetSharedSuffixVariantCore(group.Select(item => item.Name));
                    foreach (var item in group)
                    {
                        item.IsDuplicate = true;
                        item.IsNumberedVariantDuplicate = IsNumberedTitleVariant(item.Name, suffixVariantCore);
                    }
                }
            }

            var censorshipColorGroups = itemList
                .Where(IsHentaiDuplicateCandidate)
                .GroupBy(item => GetSimilarityCore(item.Name, true))
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .ToList();

            foreach (var group in censorshipColorGroups)
            {
                if (group.Count() <= 1 || !group.Any(item => HasCensorshipColorVariant(item.Name) || HasCensorshipColorInTags(item)))
                {
                    continue;
                }

                foreach (var item in group)
                {
                    item.IsCensorshipColorDuplicate = true;
                    item.IsCensorshipFullColorVariant = HasFullColorVariant(item.Name) || HasFullColorInTags(item);
                    item.IsCensorshipUncensoredVariant = HasUncensoredVariant(item.Name) || HasUncensoredInTags(item);
                }
            }
        }

        public void RecalculateDuplicates()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(RecalculateDuplicates));
                return;
            }

            RunDuplicateDetection(_scrapedItems);
        }

        public static bool IsHentaiDuplicateCandidate(GalleryItem item)
        {
            string source = (item?.SourceDomain ?? string.Empty).Trim();
            string link = (item?.Link ?? string.Empty).Trim();
            string haystack = (source + " " + link).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(haystack))
            {
                return false;
            }

            string[] hentaiDomains =
            {
                "hentaiforce", "nhentai", "hentai2read", "hentaiera",
                "vi-hentai", "daomeoden", "damconuong", "truyengg", "sayhentai", "hitomi"
            };

            return hentaiDomains.Any(domain => haystack.Contains(domain));
        }

        internal static bool IsNumberedTitleVariant(string name, string suffixVariantCore)
        {
            return HasKeywordNumberVariantPattern(name) ||
                   HasSuffixNumberVariantPattern(name) ||
                   IsSuffixVariantBaseTitle(name, suffixVariantCore);
        }

        public static string GetSharedSuffixVariantCore(IEnumerable<string> names)
        {
            var suffixCores = (names ?? Enumerable.Empty<string>())
                .Where(HasSuffixNumberVariantPattern)
                .Select(GetNumberedVariantCore)
                .Where(core => !string.IsNullOrWhiteSpace(core))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (suffixCores.Count != 1)
            {
                return string.Empty;
            }

            return suffixCores[0];
        }

        private static readonly Regex KeywordNumberVariantRegex = new Regex(
            @"\b(?:chapter|chap|ch|book|vol|volume|part|pt)\s*\d+(?:[.,]\d+)?\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex SuffixNumberVariantRegex = new Regex(
            @"(?:^|[\s_-])\d+(?:[.,]\d+)?\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex GetNumberedVariantCoreRegex1 = new Regex(
            @"\b(?:chapter|chap|ch|book|vol|volume|part|pt)\s*\d+(?:[.,]\d+)?\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex GetNumberedVariantCoreRegex2 = new Regex(
            @"\b\d+(?:[.,]\d+)?\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex HasUncensoredVariantRegex = new Regex(
            @"(?:^|[^a-zA-Z])(?:decensored|uncensored)(?:$|[^a-zA-Z])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex HasFullColorVariantRegex = new Regex(
            @"(?:^|[^a-zA-Z])(?:full\s*color|fullcolor|colorized|colored)(?:$|[^a-zA-Z])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static bool HasKeywordNumberVariantPattern(string name)
        {
            return KeywordNumberVariantRegex.IsMatch(StripDuplicateMetadata(name));
        }

        private static bool HasSuffixNumberVariantPattern(string name)
        {
            return SuffixNumberVariantRegex.IsMatch(StripDuplicateMetadata(name));
        }

        private static bool HasAnyNumberVariantPattern(string name)
        {
            return HasKeywordNumberVariantPattern(name) || HasSuffixNumberVariantPattern(name);
        }

        private static bool IsSuffixVariantBaseTitle(string name, string suffixVariantCore)
        {
            if (string.IsNullOrWhiteSpace(suffixVariantCore) || HasAnyNumberVariantPattern(name))
            {
                return false;
            }

            return string.Equals(GetNumberedVariantCore(name), suffixVariantCore, StringComparison.Ordinal);
        }

        private static string StripDuplicateMetadata(string name)
        {
            string core = (name ?? string.Empty).ToLowerInvariant();
            core = BracketRegex1.Replace(core, " ");
            core = BracketRegex2.Replace(core, " ");
            core = BracketRegex3.Replace(core, " ");
            return core;
        }

        private static string GetNumberedVariantCore(string name)
        {
            string core = StripDuplicateMetadata(name);
            core = GetNumberedVariantCoreRegex1.Replace(core, " ");
            core = GetNumberedVariantCoreRegex2.Replace(core, " ");
            core = NonAlphaNumericRegex.Replace(core, string.Empty);
            return core.Trim();
        }

        internal static bool HasCensorshipColorVariant(string name)
        {
            return HasUncensoredVariant(name) || HasFullColorVariant(name);
        }

        internal static bool HasUncensoredVariant(string name)
        {
            return HasUncensoredVariantRegex.IsMatch(name ?? string.Empty);
        }

        internal static bool HasFullColorVariant(string name)
        {
            return HasFullColorVariantRegex.IsMatch(name ?? string.Empty);
        }

        private void BtnDuplicateName_Click(object sender, RoutedEventArgs e)
        {
            RecalculateDuplicates();
            
            if (_duplicateWindowInstance != null)
            {
                if (_duplicateWindowInstance.WindowState == WindowState.Minimized)
                {
                    _duplicateWindowInstance.WindowState = WindowState.Normal;
                }
                _duplicateWindowInstance.Activate();
            }
            else
            {
                _duplicateWindowInstance = new DuplicateWindow(this);
                _duplicateWindowInstance.Owner = this;
                _duplicateWindowInstance.Closed += (s, args) => 
                { 
                    _duplicateWindowInstance = null; 
                    this.Activate();
                };
                _duplicateWindowInstance.Show();
            }
        }

        private void MenuDeleteChecked_Click(object sender, RoutedEventArgs e)
        {
            DeleteCheckedItems();
        }

        private void DeleteCheckedItems()
        {
            var itemsToRemove = _scrapedItems.Where(item => item.IsChecked).ToList();
            if (!itemsToRemove.Any()) return;

            // Push to Undo Stack
            _undoDeleteStack.Push(itemsToRemove);
            _redoDeleteStack.Clear();

            RemoveDownloadMissingChapterRows(itemsToRemove);
            foreach (var item in itemsToRemove)
            {
                _scrapedItems.Remove(item);
            }

            lblLinkCount.Text = _scrapedItems.Count.ToString();
            Log($"Deleted {itemsToRemove.Count} checked item(s).");
            lblStatus.Text = $"Deleted {itemsToRemove.Count} checked item(s).";

            RecalculateDuplicates();
        }

        private void MenuDeleteUnchecked_Click(object sender, RoutedEventArgs e)
        {
            DeleteUncheckedItems();
        }

        private void DeleteUncheckedItems()
        {
            var itemsToRemove = _scrapedItems.Where(item => !item.IsChecked).ToList();
            if (!itemsToRemove.Any()) return;

            // Push to Undo Stack
            _undoDeleteStack.Push(itemsToRemove);
            _redoDeleteStack.Clear();

            RemoveDownloadMissingChapterRows(itemsToRemove);
            foreach (var item in itemsToRemove)
            {
                _scrapedItems.Remove(item);
            }

            lblLinkCount.Text = _scrapedItems.Count.ToString();
            Log($"Deleted {itemsToRemove.Count} unchecked item(s).");
            lblStatus.Text = $"Deleted {itemsToRemove.Count} unchecked item(s).";

            RecalculateDuplicates();
        }

        private async void MenuDownloadSelected_Click(object sender, RoutedEventArgs e)
        {
            var items = dgResults.SelectedItems.Cast<GalleryItem>().ToList();
            if (!items.Any())
            {
                ShowNoSelectedItemsError();
                return;
            }
            await StartDownloadProcessAsync(items);
        }
 
        private async void MenuDownloadChecked_Click(object sender, RoutedEventArgs e)
        {
            var items = GetGalleryItemsSnapshot().Where(item => item.IsChecked).ToList();
            if (!items.Any())
            {
                ShowNoCheckedItemsError();
                return;
            }
            await StartDownloadProcessAsync(items);
        }

        private async void MenuSplitChaptersToParallelTasks_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is MenuItem menuItem) || !int.TryParse(menuItem.Tag?.ToString(), out int bucketSize) || bucketSize <= 0)
            {
                return;
            }

            List<GalleryItem> selectedItems = dgResults?.SelectedItems?.Cast<GalleryItem>().Distinct().ToList() ?? new List<GalleryItem>();
            if (selectedItems.Count == 0)
            {
                ShowNoSelectedItemsError();
                return;
            }

            int splitCount = 0;
            int skippedCount = 0;

            bool wasDownloading = _downloadCts != null;
            if (wasDownloading)
            {
                // Gọi stop download tạm thời
                BtnStopDownload_Click(null, new RoutedEventArgs());
            }

            foreach (GalleryItem source in selectedItems)
            {
                if (source == null || !_scrapedItems.Contains(source))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(source.ChapterSelectionText))
                {
                    skippedCount++;
                    Log($"Split skipped for '{source.DisplayName}': existing chapter filter '{source.ChapterSelectionText}'.");
                    continue;
                }

                int completedChapters = source.CompletedChapters;
                List<string> ranges = await BuildParallelSplitRangesAsync(source, bucketSize, completedChapters);
                if (ranges.Count == 0)
                {
                    skippedCount++;
                    Log($"Split skipped for '{source.DisplayName}': no chapter numbers found.");
                    continue;
                }

                int insertIndex = _scrapedItems.IndexOf(source);
                if (insertIndex < 0) continue;

                source.IsParallelSplitParent = true;
                source.IsParallelSplitCollapsed = true;

                List<GalleryItem> clones = ranges
                    .Select(range => CreateParallelSplitTask(source, range))
                    .ToList();

                // Đánh dấu các task đã tải xong ở quá khứ
                if (completedChapters > 0)
                {
                    foreach (var clone in clones)
                    {
                        // Phân tích range để biết dải chap
                        var parts = clone.ChapterSelectionText.Split('-');
                        if (parts.Length == 2 && double.TryParse(parts[1], out double endVal))
                        {
                            if (endVal <= completedChapters)
                            {
                                clone.Status = "Completed";
                                clone.CurrentProcess = "Done";
                                clone.IsChecked = false;
                                clone.CompletedChapters = clone.TotalChapters;
                            }
                        }
                    }
                }

                source.ParallelSplitChildren = clones;
                source.ChapterSelectionText = "";
                splitCount += clones.Count;

                // Parent task tuyệt đối không được tải nữa
                source.IsChecked = false;
                source.IsStopped = true;
                source.Status = "Stopped";
                source.CurrentProcess = "Split to parallel tasks";
                
                // Cập nhật lại thuộc tính hiển thị parent dựa trên con
                source.RecalculateParentProgress();
            }

            if (wasDownloading)
            {
                // Trực tiếp reset flag cancellation và bắt đầu tải lại sau khi dọn dẹp xong
                _ = Task.Run(async () =>
                {
                    // Chờ tối đa 5 giây để luồng cũ dừng hẳn
                    int timeoutCount = 0;
                    while (_downloadCts != null && timeoutCount < 50)
                    {
                        await Task.Delay(100);
                        timeoutCount++;
                    }
                    await Task.Delay(500); // Thêm buffer nhỏ
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        var itemsToStart = GetGalleryItemsSnapshot().Where(item => item.IsChecked).ToList();
                        if (itemsToStart.Count > 0)
                        {
                            await StartDownloadProcessAsync(itemsToStart, preserveExistingState: true);
                        }
                    });
                });
            }

            RenumberResultOrder();
            SafeRefreshResultsView();
            SyncDownloadMissingChapterRowsToResultsOrder();
            RecalculateDuplicates();
            UpdateStats();

            string summary = $"Split xong {splitCount} task";
            if (skippedCount > 0)
            {
                summary += $", bỏ qua {skippedCount} item";
            }

            summary += $", bucket {bucketSize}.";
            Log(summary);
            lblStatus.Text = summary;
        }
        private async Task<List<string>> BuildParallelSplitRangesAsync(GalleryItem item, int bucketSize, int completedChapters = 0)
        {
            List<ReaderChapterItem> chapters = await ExtractChapterItemsFromBookAsync(item, CancellationToken.None);
            List<double> chapterNumbers = chapters
                .Select(GetParallelSplitChapterNumber)
                .Where(number => number.HasValue)
                .Select(number => number.Value)
                .Distinct()
                .OrderBy(number => number)
                .ToList();

            if (chapterNumbers.Count == 0)
            {
                return new List<string>();
            }

            bool startsAtZero = chapterNumbers[0] < 1d;
            int lastBucketIndex = GetParallelSplitBucketIndex(chapterNumbers[chapterNumbers.Count - 1], bucketSize, startsAtZero);
            List<int> bucketIndices = chapterNumbers
                .Select(number => GetParallelSplitBucketIndex(number, bucketSize, startsAtZero))
                .Distinct()
                .OrderBy(index => index)
                .ToList();

            double actualLastChapter = chapterNumbers[chapterNumbers.Count - 1];
            var ranges = new List<string>(bucketIndices.Count);

            foreach (int bucketIndex in bucketIndices)
            {
                double start = startsAtZero && bucketIndex == 0 ? 0d : (bucketIndex * bucketSize) + 1d;
                double end = bucketIndex == lastBucketIndex
                    ? actualLastChapter
                    : (startsAtZero && bucketIndex == 0 ? bucketSize : (bucketIndex + 1) * bucketSize);

                if (completedChapters > 0)
                {
                    if (end <= completedChapters)
                    {
                        start = end = completedChapters;
                    }
                    else if (completedChapters >= start && completedChapters < end)
                    {
                        start = completedChapters;
                    }
                }

                ranges.Add($"{FormatParallelSplitNumber(start)}-{FormatParallelSplitNumber(end)}");
            }

            return ranges;
        }
        private GalleryItem CreateParallelSplitTask(GalleryItem source, string chapterRange)
        {
            GalleryItem clone = CloneGalleryItemForDuplicatePaste(source) ?? new GalleryItem();
            clone.IsChecked = source.IsChecked;
            clone.ChapterSelectionText = chapterRange;
            clone.IsParallelSplitTask = true;
            clone.Status = string.Empty;
            clone.CurrentProcess = string.Empty;
            clone.DownloadingChapter = string.Empty;
            clone.DownloadingPageProgress = string.Empty;
            clone.ErrorCount = 0;
            clone.ProgressPercent = 0d;
            clone.DownloadProgressPercent = 0d;
            clone.DownloadSpeedBytesPerSecond = 0L;
            clone.IsPaused = false;
            clone.IsStopped = false;
            clone.OriginalIndex = _scrapedItems.Count;
            return clone;
        }

        private double? GetParallelSplitChapterNumber(ReaderChapterItem chapter)
        {
            if (chapter?.ParsedChapterNumber.HasValue == true && chapter.ParsedChapterNumber.Value >= 0d)
            {
                return chapter.ParsedChapterNumber.Value;
            }

            if (TryParseReaderChapterNumber(chapter?.Name, out double parsedFromName, out _))
            {
                return parsedFromName >= 0d ? parsedFromName : (double?)null;
            }

            string folderPath = chapter?.FolderPath;
            if (!string.IsNullOrWhiteSpace(folderPath) &&
                folderPath.IndexOf("mangadex.org/chapter/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return null;
            }

            if (TryParseDownloadChapterNumberFromLink(folderPath, out double parsedFromLink))
            {
                return parsedFromLink;
            }

            if (!string.IsNullOrWhiteSpace(folderPath) &&
                folderPath.IndexOf("://", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return null;
            }

            if (TryParseParallelSplitChapterNumberAllowZero(folderPath, out parsedFromLink))
            {
                return parsedFromLink;
            }

            return null;
        }

        private static bool TryParseParallelSplitChapterNumberAllowZero(string value, out double number)
        {
            number = 0d;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            Match match = Regex.Match(
                value,
                @"(?:^|[/-])(?:chap|chapter|chuong|trang)(?:[-_/ ]+)?(?<num>\d+(?:[.,]\d+)?)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (!match.Success)
            {
                match = Regex.Match(
                    value,
                    @"(?<!\d)(?<num>\d+(?:[.,]\d+)?)",
                    RegexOptions.CultureInvariant);
            }

            if (!match.Success)
            {
                return false;
            }

            return double.TryParse(
                match.Groups["num"].Value.Replace(',', '.'),
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out number) && number >= 0d;
        }

        private static int GetParallelSplitBucketIndex(double chapterNumber, int bucketSize, bool startsAtZero)
        {
            const double epsilon = 0.0001d;

            if (startsAtZero)
            {
                if (chapterNumber <= bucketSize + epsilon)
                {
                    return 0;
                }

                return (int)Math.Floor(((chapterNumber - (bucketSize + 1d)) / bucketSize) + epsilon) + 1;
            }

            return (int)Math.Floor(((Math.Max(chapterNumber, 1d) - 1d) / bucketSize) + epsilon);
        }

        private static string FormatParallelSplitNumber(double number)
        {
            double rounded = Math.Round(number);
            return Math.Abs(number - rounded) < 0.0001d
                ? ((int)rounded).ToString(CultureInfo.InvariantCulture)
                : number.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private void StatusCell_Click(object sender, MouseButtonEventArgs e)
        {
            // Intentionally left blank: clicking status no longer filters chapters/pages.
        }

        private void ProcessCell_Click(object sender, MouseButtonEventArgs e)
        {
            // Intentionally left blank: clicking process text no longer auto-filters by chapter/page.
        }

        internal void ScrollResultsItemIntoView(GalleryItem item)
        {
            if (item == null || dgResults == null)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (dgResults == null || !_scrapedItems.Contains(item))
                {
                    return;
                }

                dgResults.SelectedItem = item;
                dgResults.ScrollIntoView(item);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        internal void DisableDownloadQueueAutoScrollFromStop()
        {
            // Auto-scroll removed from queue UI.
        }

        internal void TryAutoScrollDownloadQueue(GalleryItem updatedItem)
        {
            // Auto scroll disabled.
        }

        internal void UpdateStats()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(UpdateStats));
                return;
            }

            if (lblLinkCount == null || lblBooksCompleteCount == null || lblErrorBooksCount == null)
            {
                return;
            }

            int total = _scrapedItems.Count;
            int complete = 0;
            int error = 0;

            foreach (var item in _scrapedItems)
            {
                if (item == null) continue;
                string status = (item.Status ?? "").Trim().ToLowerInvariant();
                if (status == "completed" || status == "done" || status == "hoan tat")
                {
                    complete++;
                }
                else if (status == "error" || status == "loi")
                {
                    error++;
                }
            }

            lblLinkCount.Text = total.ToString();
            lblBooksCompleteCount.Text = complete.ToString();
            lblErrorBooksCount.Text = error.ToString();
        }

        private void BtnMergeParallelSplitChapters_Click(object sender, RoutedEventArgs e)
        {
            var groups = _scrapedItems
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Link))
                .GroupBy(item => NormalizeProcessLink(item.Link), StringComparer.OrdinalIgnoreCase)
                .ToList();

            int mergeCount = 0;
            foreach (var group in groups)
            {
                var parent = group.FirstOrDefault(item => item.IsParallelSplitParent);
                var childrenInGrid = group.Where(item => item.IsParallelSplitTask || (!item.IsParallelSplitParent && !string.IsNullOrWhiteSpace(item.ChapterSelectionText))).ToList();

                if (parent != null)
                {
                    // Case 1: Parent exists in the grid
                    var allChildren = new List<GalleryItem>();
                    if (parent.ParallelSplitChildren != null)
                    {
                        allChildren.AddRange(parent.ParallelSplitChildren);
                    }
                    foreach (var child in childrenInGrid)
                    {
                        if (!allChildren.Contains(child))
                        {
                            allChildren.Add(child);
                        }
                    }

                    if (allChildren.Count > 0)
                    {
                        // Remove children from active list (collapse them)
                        foreach (var child in allChildren)
                        {
                            _scrapedItems.Remove(child);
                        }

                        parent.ParallelSplitChildren = allChildren;
                        parent.IsParallelSplitCollapsed = true;

                        // Update parent's progress & status as a merged summary of children
                        UpdateParentProgressFromChildren(parent);
                        mergeCount++;
                    }
                }
                else if (childrenInGrid.Count > 1)
                {
                    // Case 2: No parent exists, but we have multiple child/split rows
                    int insertIndex = _scrapedItems.IndexOf(childrenInGrid[0]);
                    if (insertIndex >= 0)
                    {
                        // Create a new parent from the first child as seed
                        GalleryItem seed = childrenInGrid[0];
                        GalleryItem newParent = CloneGalleryItemForDuplicatePaste(seed) ?? new GalleryItem();
                        
                        newParent.Link = seed.Link;
                        newParent.Name = seed.Name;
                        newParent.LinkCount = seed.LinkCount;
                        newParent.SourceDomain = seed.SourceDomain;
                        newParent.HasNoChapters = seed.HasNoChapters;
                        newParent.NhentaiTotalPagesHint = seed.NhentaiTotalPagesHint;
                        newParent.ConnectionCount = seed.ConnectionCount;
                        newParent.MultiDownloadCount = seed.MultiDownloadCount;
                        newParent.DownloadPath = seed.DownloadPath;
                        newParent.OriginalIndex = seed.OriginalIndex;

                        newParent.IsParallelSplitParent = true;
                        newParent.IsParallelSplitCollapsed = true;
                        newParent.ChapterSelectionText = "";

                        // Remove children from grid
                        foreach (var child in childrenInGrid)
                        {
                            _scrapedItems.Remove(child);
                            child.IsParallelSplitTask = true; // Ensure they are marked as split tasks
                        }

                        newParent.ParallelSplitChildren = childrenInGrid;
                        _scrapedItems.Insert(insertIndex, newParent);

                        // Update progress
                        UpdateParentProgressFromChildren(newParent);
                        mergeCount++;
                    }
                }
            }

            if (mergeCount > 0)
            {
                RenumberResultOrder();
                SafeRefreshResultsView();
                EnsureDownloadMissingChapterRowsFromGallery(forceSyncOrder: true);
                SyncAllGalleryMissingChapterStatuses();
                RecalculateDuplicates();
                UpdateStats();

                string msg = _isVietnameseUi ? $"Đã gộp thành công {mergeCount} nhóm truyện split." : $"Successfully merged {mergeCount} groups of split books.";
                Log(msg);
                lblStatus.Text = msg;
                MessageBox.Show(msg, "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                string msg = _isVietnameseUi ? "Không tìm thấy truyện split nào cần gộp." : "No split books found to merge.";
                lblStatus.Text = msg;
                MessageBox.Show(msg, "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void UpdateParentProgressFromChildren(GalleryItem parent)
        {
            if (parent.ParallelSplitChildren == null || parent.ParallelSplitChildren.Count == 0)
            {
                return;
            }

            bool hasErrors = parent.ParallelSplitChildren.Any(item => 
                item.HasAnyErrors() || string.Equals(item.Status, "Error", StringComparison.OrdinalIgnoreCase));
            
            parent.TotalChapters = parent.ParallelSplitChildren.Sum(item => Math.Max(0, item.TotalChapters));
            parent.CompletedChapters = parent.ParallelSplitChildren.Sum(item => Math.Max(0, item.CompletedChapters));
            parent.Status = hasErrors ? "Error" : "Completed";
            parent.CurrentProcess = MainWindow.GetDoneProcessTextForGroup(parent.ParallelSplitChildren, hasErrors);
            parent.ProgressPercent = 100d;
            parent.DownloadProgressPercent = 100d;
            parent.HasMissingChapterIssue = parent.ParallelSplitChildren.Any(item => item.HasMissingChapterIssue);

            // Re-calculate parent's unique errors from children
            var mergedErrors = parent.ParallelSplitChildren
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
            parent.Errors = mergedErrors;
            parent.ErrorCount = parent.GetUniqueErrorCount();
        }

        private void SafeRefreshResultsView()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(SafeRefreshResultsView));
                return;
            }

            if (dgResults == null) return;

            var now = DateTime.Now;
            double elapsed = (now - _lastResultsRefreshTime).TotalMilliseconds;
            if (elapsed < 333)
            {
                if (!_isResultsRefreshPending)
                {
                    _isResultsRefreshPending = true;
                    int delay = 333 - (int)elapsed;
                    System.Threading.Tasks.Task.Delay(delay).ContinueWith(t =>
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            _isResultsRefreshPending = false;
                            SafeRefreshResultsView();
                        }));
                    });
                }
                return;
            }

            _lastResultsRefreshTime = now;

            try
            {
                dgResults.Items.Refresh();
            }
            catch
            {
                // Fallback nếu ItemsSource đang binding trực tiếp và không thể dùng Items.Refresh
                ResultsView?.Refresh();
            }
        }

        private void SafeRefreshMissingChaptersView()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(SafeRefreshMissingChaptersView));
                return;
            }

            if (_downloadMissingChapterGrid == null) return;

            try
            {
                _downloadMissingChapterGrid.Items.Refresh();
            }
            catch
            {
                var missingView = CollectionViewSource.GetDefaultView(_downloadMissingChapterGrid.ItemsSource);
                missingView?.Refresh();
            }
        }

        private void BtnToggleParallelSplit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.DataContext is GalleryItem parent)
            {
                if (!parent.HasParallelSplitChildren) return;

                int parentIndex = _scrapedItems.IndexOf(parent);
                if (parentIndex < 0) return;

                parent.IsParallelSplitCollapsed = !parent.IsParallelSplitCollapsed;

                if (parent.IsParallelSplitCollapsed)
                {
                    foreach (var child in parent.ParallelSplitChildren)
                    {
                        _scrapedItems.Remove(child);
                    }
                }
                else
                {
                    for (int i = 0; i < parent.ParallelSplitChildren.Count; i++)
                    {
                        _scrapedItems.Insert(parentIndex + 1 + i, parent.ParallelSplitChildren[i]);
                    }
                }

                RenumberResultOrder();
                SafeRefreshResultsView();
                UpdateStats();
            }
        }

        private void AutoCollapseAllSplitTasks()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(AutoCollapseAllSplitTasks));
                return;
            }

            var items = _scrapedItems.ToList();
            var newItems = new List<GalleryItem>();

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.IsParallelSplitTask && !string.IsNullOrWhiteSpace(item.Link))
                {
                    var children = new List<GalleryItem>();
                    int j = i + 1;
                    while (j < items.Count && 
                           items[j].IsParallelSplitTask && 
                           string.Equals(items[j].Link, item.Link, StringComparison.OrdinalIgnoreCase))
                    {
                        children.Add(items[j]);
                        j++;
                    }

                    if (children.Count > 0)
                    {
                        item.ParallelSplitChildren = children;
                        item.IsParallelSplitCollapsed = true;
                        i = j - 1;
                    }
                    else
                    {
                        item.ParallelSplitChildren = new List<GalleryItem>();
                    }
                }
                newItems.Add(item);
            }

            _scrapedItems.Clear();
            foreach (var item in newItems)
            {
                _scrapedItems.Add(item);
            }
        }

        private List<GalleryItem> GetFlattenedScrapedItems()
        {
            var list = new List<GalleryItem>();
            foreach (var item in _scrapedItems)
            {
                if (item == null) continue;
                list.Add(item);
                if (item.HasParallelSplitChildren && item.IsParallelSplitCollapsed)
                {
                    list.AddRange(item.ParallelSplitChildren);
                }
            }
            return list;
        }

        private double _booksTextScaleFactor = 1.0;

        private void CmbBooksTextScale_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbBooksTextScale?.SelectedItem is ComboBoxItem item)
            {
                string content = item.Content?.ToString() ?? "100%";
                if (double.TryParse(content.Replace("%", "").Trim(), out double val))
                {
                    _booksTextScaleFactor = val / 100.0;
                    ApplyBooksTextScale();
                }
            }
        }

        private void DgResults_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (e.Row != null)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ApplyBooksTextScaleToVisualTree(e.Row);
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        private void ApplyBooksTextScale()
        {
            if (dgResults == null) return;
            dgResults.FontSize = 11.0 * _booksTextScaleFactor;
            
            // Duyệt toàn bộ visual tree của dgResults để scale cả Headers và các phần tử đang hiển thị
            ApplyBooksTextScaleToVisualTree(dgResults);
        }

        private void ApplyBooksTextScaleToVisualTree(DependencyObject root)
        {
            if (root == null) return;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                ScaleBookElementFont(child);
                ApplyBooksTextScaleToVisualTree(child);
            }
        }

        private void ScaleBookElementFont(DependencyObject element)
        {
            if (element == dgResults) return;

            if (element is Control ctrl)
            {
                double baseVal = (double)ctrl.GetValue(BaseFontSizeProperty);
                if (double.IsNaN(baseVal))
                {
                    // Lấy FontSize hiện tại. Nếu _booksTextScaleFactor khác 1.0, chia ngược lại để có base đúng
                    baseVal = ctrl.FontSize / _booksTextScaleFactor;
                    ctrl.SetValue(BaseFontSizeProperty, baseVal);
                }
                ctrl.FontSize = Math.Round(baseVal * _booksTextScaleFactor, 1);
            }
            else if (element is TextBlock tb)
            {
                double baseVal = (double)tb.GetValue(BaseFontSizeProperty);
                if (double.IsNaN(baseVal))
                {
                    baseVal = tb.FontSize / _booksTextScaleFactor;
                    tb.SetValue(BaseFontSizeProperty, baseVal);
                }
                tb.FontSize = Math.Round(baseVal * _booksTextScaleFactor, 1);
            }
        }

        private async void MenuRetryErrors_Click(object sender, RoutedEventArgs e)
        {
            var errorItems = dgResults.SelectedItems.Cast<GalleryItem>()
                .Where(item => item.ErrorCount > 0 || string.Equals(item.Status, "Error", StringComparison.OrdinalIgnoreCase) || string.Equals(item.DownloadingPageProgress, "Error", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (errorItems.Count == 0)
            {
                errorItems = _scrapedItems
                    .Where(item => item.ErrorCount > 0 || string.Equals(item.Status, "Error", StringComparison.OrdinalIgnoreCase) || string.Equals(item.DownloadingPageProgress, "Error", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (errorItems.Count == 0)
            {
                MessageBox.Show(_isVietnameseUi ? "Không tìm thấy truyện lỗi nào." : "No error items found.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            foreach (var item in errorItems)
            {
                item.Status = "Ready";
                item.ErrorCount = 0;
                item.Errors?.Clear();
            }
            await StartDownloadProcessAsync(errorItems, preserveExistingState: true);
        }

        private void MenuOpenDownloadFolderInRow_Click(object sender, RoutedEventArgs e)
        {
            if (dgResults.SelectedItem is GalleryItem item)
            {
                string downloadRoot = txtDownloadPath.Text.Trim();
                if (!string.IsNullOrEmpty(downloadRoot))
                {
                    string path = System.IO.Path.Combine(downloadRoot, item.Name);
                    if (System.IO.Directory.Exists(path))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", $"\"{path}\"");
                    }
                    else
                    {
                        System.Diagnostics.Process.Start("explorer.exe", $"\"{downloadRoot}\"");
                    }
                }
            }
        }

        private void MenuExportErrors_Click(object sender, RoutedEventArgs e)
        {
            var errorItems = _scrapedItems.Where(item => item.ErrorCount > 0 || string.Equals(item.Status, "Error", StringComparison.OrdinalIgnoreCase)).ToList();
            if (errorItems.Count == 0)
            {
                MessageBox.Show(_isVietnameseUi ? "Không có truyện nào bị lỗi để xuất." : "No error items to export.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = "error_books.txt"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var lines = errorItems.Select(item => $"Tên: {item.Name}\nLink: {item.Link}\nLỗi: {item.DetailedErrorToolTip ?? "Không rõ chi tiết"}\n------------------------");
                    System.IO.File.WriteAllLines(dialog.FileName, lines);
                    Log($"Đã xuất danh sách truyện lỗi sang {dialog.FileName}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi khi xuất file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void BtnRescanThumbnail_Click(object sender, RoutedEventArgs e)
        {
            List<GalleryItem> selectedItems;
            if (_isResultsThumbnailViewEnabled)
            {
                selectedItems = lbResultsThumbnail.SelectedItems.Cast<GalleryItem>().ToList();
            }
            else
            {
                selectedItems = dgResults.SelectedItems.Cast<GalleryItem>().ToList();
            }

            if (!selectedItems.Any())
            {
                selectedItems = _scrapedItems.ToList();
            }

            if (!selectedItems.Any()) return;

            var tasks = new List<System.Threading.Tasks.Task>();
            foreach (var item in selectedItems)
            {
                item.HoverPreviewLocalPath = null;
                item.HoverPreviewThumbnailLocalPath = null;

                if (item.Link != null)
                {
                    _galleryHoverPreviewBitmapMissingCache.Remove(item.Link);
                    _galleryHoverPreviewMissingCache.Remove(item.Link);
                    _galleryHoverPreviewUrlCache.Remove(item.Link);
                }

                if (item.HoverPreviewThumbnailUrl != null)
                {
                    _galleryHoverPreviewBitmapMissingCache.Remove(item.HoverPreviewThumbnailUrl);
                }

                string domainFolder = GetDomainFolderName(item, item.HoverPreviewThumbnailUrl);
                string previewRoot = System.IO.Path.Combine(PortablePaths.PortableTempRoot, "preview-cache", domainFolder);



                string sanitizedBook = GetSanitizedFileName(item.Name);
                if (!string.IsNullOrWhiteSpace(sanitizedBook))
                {
                    if (System.IO.Directory.Exists(previewRoot))
                    {
                        try
                        {
                            foreach (var file in System.IO.Directory.GetFiles(previewRoot, sanitizedBook + "*"))
                            {
                                System.IO.File.Delete(file);
                            }
                        }
                        catch { }
                    }

                    try
                    {
                        System.IO.Directory.CreateDirectory(previewRoot);
                    }
                    catch { }
                }

                tasks.Add(PrefetchGalleryHoverPreviewAsync(item));
            }

            await System.Threading.Tasks.Task.WhenAll(tasks);
            UpdateThumbnailsVirtualizationWindow();

            Log($"Rescanned thumbnail for {selectedItems.Count} book(s).");
            lblStatus.Text = $"Rescanned thumbnail for {selectedItems.Count} book(s).";
        }

        private async void BtnWebpCodec_Click(object sender, RoutedEventArgs e)
        {
            string url = "https://storage.googleapis.com/downloads.webmproject.org/releases/webp/WebpCodecSetup.exe";
            string tempDir = System.IO.Path.Combine(PortablePaths.PortableTempRoot, "installers");
            string destFile = System.IO.Path.Combine(tempDir, "WebpCodecSetup.exe");

            try
            {
                if (!System.IO.Directory.Exists(tempDir))
                {
                    System.IO.Directory.CreateDirectory(tempDir);
                }

                lblStatus.Text = _isVietnameseUi ? "Đang tải bộ cài WebP Codec..." : "Downloading WebP Codec installer...";
                Log($"[WebP Codec] Downloading installer from {url} to {destFile}");

                using (var client = CreateScopedHttpClient(url))
                using (var response = await client.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    using (var fs = new System.IO.FileStream(destFile, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                    {
                        await response.Content.CopyToAsync(fs);
                    }
                }

                Log($"[WebP Codec] Running WebpCodecSetup.exe");
                lblStatus.Text = _isVietnameseUi ? "Đang chạy bộ cài đặt WebP..." : "Running WebP installer...";

                var proc = System.Diagnostics.Process.Start(destFile);
                if (proc != null)
                {
                    await Task.Run(() => proc.WaitForExit());
                }

                MessageBox.Show(
                    _isVietnameseUi 
                        ? "Đã cài đặt xong WebP Codec! Ứng dụng sẽ tự động khởi động lại để kích hoạt giải mã ảnh WebP." 
                        : "WebP Codec installation completed! The application will restart to apply changes.",
                    "Information",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                string currentExe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                System.Diagnostics.Process.Start(currentExe);
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Log($"[WebP Codec] Lỗi cài đặt: {ex.Message}");
                MessageBox.Show(
                    _isVietnameseUi 
                        ? $"Không thể tải bộ cài đặt: {ex.Message}" 
                        : $"Failed to download installer: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private int CountDiskImagesForGalleryItem(GalleryItem item)
        {
            if (item == null) return 0;
            string dlRoot = string.IsNullOrWhiteSpace(item.DownloadPath) ? (txtDownloadPath?.Text ?? string.Empty) : item.DownloadPath;
            if (string.IsNullOrWhiteSpace(dlRoot)) return 0;

            try
            {
                string siteFolder = GetDownloadSiteKey(item);
                string siteRoot = GetConfiguredDownloadRoot(dlRoot, siteFolder);
                string safeBook = GetCanonicalBookFolderName(item, string.Empty);
                string bookFolder = System.IO.Path.Combine(siteRoot, safeBook);

                if (!System.IO.Directory.Exists(bookFolder)) return 0;

                string[] searchPatterns = { "*.jpg", "*.jpeg", "*.png", "*.webp", "*.gif", "*.bmp" };
                int count = 0;
                
                // Đếm tất cả file ảnh trong thư mục book (bao gồm cả thư mục con như chapter)
                foreach (var pattern in searchPatterns)
                {
                    try
                    {
                        count += System.IO.Directory.GetFiles(bookFolder, pattern, System.IO.SearchOption.AllDirectories).Length;
                    }
                    catch {}
                }

                return count;
            }
            catch (Exception ex)
            {
                Log($"[CountDiskImages Error] {item.Name}: {ex.Message}");
                return 0;
            }
        }

        private async Task RefreshDiskImageCountAsync(GalleryItem item)
        {
            if (item == null) return;
            await Task.Run(() =>
            {
                int count = CountDiskImagesForGalleryItem(item);
                Dispatcher.Invoke(() =>
                {
                    item.ActualDiskImageCount = count > 0 ? (int?)count : null;
                });
            });
        }

        private async void MenuRefreshDiskImageCount_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = new List<GalleryItem>();
            if (_isResultsThumbnailViewEnabled && lbResultsThumbnail != null)
            {
                selectedItems = lbResultsThumbnail.SelectedItems.Cast<GalleryItem>().Where(x => x != null).ToList();
            }
            else if (dgResults != null)
            {
                selectedItems = dgResults.SelectedItems.Cast<GalleryItem>().Where(x => x != null).ToList();
            }

            if (selectedItems.Count == 0) return;

            lblStatus.Text = _isVietnameseUi ? $"Đang đếm số lượng ảnh cho {selectedItems.Count} truyện..." : $"Counting disk images for {selectedItems.Count} book(s)...";
            int successCount = 0;

            foreach (var item in selectedItems)
            {
                await RefreshDiskImageCountAsync(item);
                successCount++;
            }

            lblStatus.Text = _isVietnameseUi 
                ? $"Đã đếm xong số lượng ảnh cho {successCount} truyện." 
                : $"Finished counting disk images for {successCount} book(s).";
            Log(lblStatus.Text);
        }

        private void DgResults_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                var dep = (DependencyObject)e.OriginalSource;
                while (dep != null && !(dep is DataGridRow))
                {
                    dep = VisualTreeHelper.GetParent(dep);
                }

                if (dep is DataGridRow row && row.DataContext is GalleryItem clickedItem)
                {
                    e.Handled = true;
                    string clickedCore = GetSimilarityCore(clickedItem.Name, false);
                    if (string.IsNullOrEmpty(clickedCore)) return;

                    var listItems = dgResults.Items.Cast<GalleryItem>().ToList();
                    var matchingItems = listItems
                        .Where(item => GetSimilarityCore(item.Name, false) == clickedCore)
                        .ToList();

                    bool isCtrl = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control;
                    bool isShift = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) == System.Windows.Input.ModifierKeys.Shift;

                    if (isShift)
                    {
                        var anchor = dgResults.SelectedItem as GalleryItem;
                        if (anchor == null && dgResults.SelectedItems.Count > 0)
                        {
                            anchor = dgResults.SelectedItems[0] as GalleryItem;
                        }

                        if (anchor != null)
                        {
                            int idx1 = listItems.IndexOf(anchor);
                            int idx2 = listItems.IndexOf(clickedItem);
                            int start = Math.Min(idx1, idx2);
                            int end = Math.Max(idx1, idx2);

                            var rangeItems = new System.Collections.Generic.List<GalleryItem>();
                            for (int i = start; i <= end; i++)
                            {
                                rangeItems.Add(listItems[i]);
                            }

                            // Gộp thêm toàn bộ nhóm tương tự của dòng click hiện tại
                            foreach (var item in matchingItems)
                            {
                                if (!rangeItems.Contains(item))
                                {
                                    rangeItems.Add(item);
                                }
                            }

                            if (!isCtrl)
                            {
                                dgResults.SelectedItems.Clear();
                            }
                            foreach (var item in rangeItems)
                            {
                                if (!dgResults.SelectedItems.Contains(item))
                                {
                                    dgResults.SelectedItems.Add(item);
                                }
                            }
                        }
                        else
                        {
                            dgResults.SelectedItems.Clear();
                            foreach (var item in matchingItems)
                            {
                                dgResults.SelectedItems.Add(item);
                            }
                        }
                    }
                    else if (isCtrl)
                    {
                        bool allSelected = matchingItems.All(item => dgResults.SelectedItems.Contains(item));
                        if (allSelected)
                        {
                            foreach (var item in matchingItems)
                            {
                                dgResults.SelectedItems.Remove(item);
                            }
                        }
                        else
                        {
                            foreach (var item in matchingItems)
                            {
                                if (!dgResults.SelectedItems.Contains(item))
                                {
                                    dgResults.SelectedItems.Add(item);
                                }
                            }
                        }
                    }
                    else
                    {
                        dgResults.SelectedItems.Clear();
                        foreach (var item in matchingItems)
                        {
                            dgResults.SelectedItems.Add(item);
                        }
                    }
                }
            }
        }

        private void LbResultsThumbnail_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                var dep = (DependencyObject)e.OriginalSource;
                while (dep != null && !(dep is ListBoxItem))
                {
                    dep = VisualTreeHelper.GetParent(dep);
                }

                if (dep is ListBoxItem item && item.DataContext is GalleryItem clickedItem)
                {
                    e.Handled = true;
                    string clickedCore = GetSimilarityCore(clickedItem.Name, false);
                    if (string.IsNullOrEmpty(clickedCore)) return;

                    var listItems = lbResultsThumbnail.Items.Cast<GalleryItem>().ToList();
                    var matchingItems = listItems
                        .Where(g => GetSimilarityCore(g.Name, false) == clickedCore)
                        .ToList();

                    bool isCtrl = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control;
                    bool isShift = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) == System.Windows.Input.ModifierKeys.Shift;

                    if (isShift)
                    {
                        var anchor = lbResultsThumbnail.SelectedItem as GalleryItem;
                        if (anchor == null && lbResultsThumbnail.SelectedItems.Count > 0)
                        {
                            anchor = lbResultsThumbnail.SelectedItems[0] as GalleryItem;
                        }

                        if (anchor != null)
                        {
                            int idx1 = listItems.IndexOf(anchor);
                            int idx2 = listItems.IndexOf(clickedItem);
                            int start = Math.Min(idx1, idx2);
                            int end = Math.Max(idx1, idx2);

                            var rangeItems = new System.Collections.Generic.List<GalleryItem>();
                            for (int i = start; i <= end; i++)
                            {
                                rangeItems.Add(listItems[i]);
                            }

                            // Gộp thêm toàn bộ nhóm tương tự của dòng click hiện tại
                            foreach (var m in matchingItems)
                            {
                                if (!rangeItems.Contains(m))
                                {
                                    rangeItems.Add(m);
                                }
                            }

                            if (!isCtrl)
                            {
                                lbResultsThumbnail.SelectedItems.Clear();
                            }
                            foreach (var m in rangeItems)
                            {
                                if (!lbResultsThumbnail.SelectedItems.Contains(m))
                                {
                                    lbResultsThumbnail.SelectedItems.Add(m);
                                }
                            }
                        }
                        else
                        {
                            lbResultsThumbnail.SelectedItems.Clear();
                            foreach (var m in matchingItems)
                            {
                                lbResultsThumbnail.SelectedItems.Add(m);
                            }
                        }
                    }
                    else if (isCtrl)
                    {
                        bool allSelected = matchingItems.All(m => lbResultsThumbnail.SelectedItems.Contains(m));
                        if (allSelected)
                        {
                            foreach (var m in matchingItems)
                            {
                                lbResultsThumbnail.SelectedItems.Remove(m);
                            }
                        }
                        else
                        {
                            foreach (var m in matchingItems)
                            {
                                if (!lbResultsThumbnail.SelectedItems.Contains(m))
                                {
                                    lbResultsThumbnail.SelectedItems.Add(m);
                                }
                            }
                        }
                    }
                    else
                    {
                        lbResultsThumbnail.SelectedItems.Clear();
                        foreach (var m in matchingItems)
                        {
                            lbResultsThumbnail.SelectedItems.Add(m);
                        }
                    }
                }
            }
        }

        private Point _dragStartPoint;
        private double _originalHorizontalOffset;
        private double _originalVerticalOffset;
        private bool _isDraggingPopup;

        private void DragPopup_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var element = sender as FrameworkElement;
            if (element != null)
            {
                _isDraggingPopup = true;
                _dragStartPoint = e.GetPosition(this);
                _originalHorizontalOffset = popAdvancedFilter.HorizontalOffset;
                _originalVerticalOffset = popAdvancedFilter.VerticalOffset;
                element.CaptureMouse();
            }
        }

        private void DragPopup_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingPopup)
            {
                Point currentPoint = e.GetPosition(this);
                double deltaX = currentPoint.X - _dragStartPoint.X;
                double deltaY = currentPoint.Y - _dragStartPoint.Y;

                popAdvancedFilter.HorizontalOffset = _originalHorizontalOffset + deltaX;
                popAdvancedFilter.VerticalOffset = _originalVerticalOffset + deltaY;
            }
        }

        private void DragPopup_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingPopup)
            {
                _isDraggingPopup = false;
                var element = sender as FrameworkElement;
                element?.ReleaseMouseCapture();
            }
        }
    }
}

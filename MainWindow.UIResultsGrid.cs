using System;
using System.Diagnostics;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
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
        private Point _resultsDragStartPoint;
        private GalleryItem _resultsDragItem;

        private void ApplyResultsSort(string propertyName, ListSortDirection direction, string logMessage = null)
        {
            var view = ResultsView;
            if (view == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return;
            }

            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(propertyName, direction));
            if (!string.Equals(propertyName, "OriginalIndex", StringComparison.Ordinal))
            {
                view.SortDescriptions.Add(new SortDescription("OriginalIndex", ListSortDirection.Ascending));
            }

            if (!string.IsNullOrWhiteSpace(logMessage))
            {
                Log(logMessage);
            }
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
                direction = ListSortDirection.Descending;
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
        }

        private void TxtFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyResultsFilter();
        }

        private void ApplyResultsFilter()
        {
            ApplyResultsFilter(ResultsView, txtFilter?.Text?.Trim() ?? string.Empty);
            ApplyResultsFilter(CollectionViewSource.GetDefaultView(_lightNovelItems), string.Empty);
        }

        private void ApplyResultsFilter(ICollectionView view, string filterText)
        {
            if (view == null)
            {
                return;
            }

            view.Filter = item =>
            {
                if (!(item is GalleryItem galleryItem))
                {
                    return false;
                }

                if (string.IsNullOrEmpty(filterText))
                {
                    return true;
                }

                return (galleryItem.Name != null && galleryItem.Name.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                       (galleryItem.Link != null && galleryItem.Link.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                       (galleryItem.Status != null && galleryItem.Status.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                       (galleryItem.CurrentProcess != null && galleryItem.CurrentProcess.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                       (galleryItem.DownloadingChapter != null && galleryItem.DownloadingChapter.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                       (galleryItem.DownloadingPageProgress != null && galleryItem.DownloadingPageProgress.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0);
            };
        }

        private void BtnSortByName_Click(object sender, RoutedEventArgs e)
        {
            ApplyResultsSort(colGalleryDetails, "Name", ref _isNameSortAscending, "comic books");
        }

        private void BtnSortBySpeed_Click(object sender, RoutedEventArgs e)
        {
            ClearResultsColumnSortDirections(colSpeed);
            if (colSpeed != null)
            {
                colSpeed.SortDirection = ListSortDirection.Descending;
            }
            ApplyResultsSort("DownloadSpeedSortValue", ListSortDirection.Descending, "Sorted download speed descending.");
        }

        private void BtnRestoreOrder_Click(object sender, RoutedEventArgs e)
        {
            RestoreResultsOrder("Original order restored.");
        }

        private void RestoreResultsOrder(string logMessage)
        {
            _isNameSortAscending = true;
            _isStatusSortAscending = true;
            _isProcessSortAscending = true;
            ClearResultsColumnSortDirections();
            ApplyResultsSort("OriginalIndex", ListSortDirection.Ascending, logMessage);
        }

        private void RenumberResultOrder()
        {
            for (int i = 0; i < _scrapedItems.Count; i++)
            {
                _scrapedItems[i].OriginalIndex = i;
            }

            Debug.Assert(_scrapedItems.Select((item, index) => item.OriginalIndex == index).All(match => match));
        }

        private void MoveResultItem(GalleryItem item, int targetIndex, string logMessage)
        {
            if (item == null || !_scrapedItems.Contains(item))
            {
                return;
            }

            int currentIndex = _scrapedItems.IndexOf(item);
            if (currentIndex < 0)
            {
                return;
            }

            targetIndex = Math.Max(0, Math.Min(targetIndex, _scrapedItems.Count - 1));
            if (targetIndex == currentIndex)
            {
                return;
            }

            _scrapedItems.RemoveAt(currentIndex);
            if (targetIndex > currentIndex)
            {
                targetIndex--;
            }
            _scrapedItems.Insert(targetIndex, item);
            RenumberResultOrder();
            RestoreResultsOrder(logMessage);
        }

        private static bool IsDragCandidate(DependencyObject source)
        {
            while (source != null)
            {
                if (source is ButtonBase || source is TextBoxBase || source is PasswordBox || source is ComboBox || source is ToggleButton || source is ScrollBar || source is Thumb || source is MenuItem)
                {
                    return false;
                }

                if (source is DataGridRow || source is DataGridCell)
                {
                    return true;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return false;
        }

        private DataGridRow GetResultsRow(DependencyObject source)
        {
            while (source != null && !(source is DataGridRow))
            {
                source = VisualTreeHelper.GetParent(source);
            }

            return source as DataGridRow;
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

        private void DgResultsRow_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (!(sender is DataGridRow row))
            {
                return;
            }

            if (row.ContextMenu == null)
            {
                row.ContextMenu = new ContextMenu();
                var moveTopItem = new MenuItem { Header = _isVietnameseUi ? "Đưa lên đầu" : "Move to top", Tag = "top" };
                var moveBottomItem = new MenuItem { Header = _isVietnameseUi ? "Đưa xuống cuối" : "Move to bottom", Tag = "bottom" };
                moveTopItem.Click += RowContextMenuItem_Click;
                moveBottomItem.Click += RowContextMenuItem_Click;
                row.ContextMenu.Items.Add(moveTopItem);
                row.ContextMenu.Items.Add(moveBottomItem);
            }
            else if (row.ContextMenu.Items.Count >= 2)
            {
                if (row.ContextMenu.Items[0] is MenuItem moveTopItem)
                {
                    moveTopItem.Header = _isVietnameseUi ? "Đưa lên đầu" : "Move to top";
                }

                if (row.ContextMenu.Items[1] is MenuItem moveBottomItem)
                {
                    moveBottomItem.Header = _isVietnameseUi ? "Đưa xuống cuối" : "Move to bottom";
                }
            }
        }

        private void RowContextMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is MenuItem menuItem) || !(menuItem.Parent is ContextMenu contextMenu) || !(contextMenu.PlacementTarget is DataGridRow row) || !(row.Item is GalleryItem item))
            {
                return;
            }

            string action = menuItem.Tag as string;
            if (string.Equals(action, "top", StringComparison.Ordinal))
            {
                MoveResultItem(item, 0, $"Moved '{item.DisplayName}' to top.");
            }
            else if (string.Equals(action, "bottom", StringComparison.Ordinal))
            {
                MoveResultItem(item, _scrapedItems.Count - 1, $"Moved '{item.DisplayName}' to bottom.");
            }
        }

        private void DgResults_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(typeof(GalleryItem)) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void DgResults_Drop(object sender, DragEventArgs e)
        {
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

            if (targetItem == null)
            {
                MoveResultItem(sourceItem, _scrapedItems.Count - 1, $"Moved '{sourceItem.DisplayName}' in gallery list.");
                return;
            }

            if (ReferenceEquals(sourceItem, targetItem))
            {
                return;
            }

            int targetIndex = _scrapedItems.IndexOf(targetItem);
            if (targetIndex < 0)
            {
                return;
            }

            MoveResultItem(sourceItem, targetIndex, $"Moved '{sourceItem.DisplayName}' in gallery list.");
        }

        private void BtnNoLinkViHentai_Click(object sender, RoutedEventArgs e)
        {
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
        }

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

        private void DgResults_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (IsTypingInEditableTextBox())
            {
                return;
            }

            if (dgResults.Items.Count == 0) return;

            if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                MenuCopySelectedLinks_Click(null, null);
                e.Handled = true;
            }
            else if (e.Key == Key.Home)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                {
                    int anchorIndex = dgResults.SelectedIndex;
                    if (anchorIndex < 0) anchorIndex = 0;
                    dgResults.SelectedItems.Clear();
                    for (int i = 0; i <= anchorIndex; i++)
                    {
                        dgResults.SelectedItems.Add(dgResults.Items[i]);
                    }
                }
                else
                {
                    dgResults.SelectedIndex = 0;
                }
                dgResults.ScrollIntoView(dgResults.Items[0]);
                e.Handled = true;
            }
            else if (e.Key == Key.End)
            {
                int lastIndex = dgResults.Items.Count - 1;
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                {
                    int anchorIndex = dgResults.SelectedIndex;
                    if (anchorIndex < 0) anchorIndex = 0;
                    dgResults.SelectedItems.Clear();
                    for (int i = anchorIndex; i <= lastIndex; i++)
                    {
                        dgResults.SelectedItems.Add(dgResults.Items[i]);
                    }
                }
                else
                {
                    dgResults.SelectedIndex = lastIndex;
                }
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
            if (dgResults.SelectedItems.Count == 0) return;

            var itemsToRemove = dgResults.SelectedItems.Cast<GalleryItem>().ToList();
            dgResults.ItemsSource = null;
            foreach (var item in itemsToRemove)
            {
                _scrapedItems.Remove(item);
            }
            dgResults.ItemsSource = _scrapedItems;
            
            lblLinkCount.Text = _scrapedItems.Count.ToString();
            Log($"Deleted {itemsToRemove.Count} selected item(s).");
            lblStatus.Text = $"Deleted {itemsToRemove.Count} item(s).";
            
            RecalculateDuplicates();
        }

        private void DgResults_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount < 2)
            {
                return;
            }

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
                        Log($"Opened link: {item.Link}");
                        e.Handled = true;
                    }
                    catch (Exception ex)
                    {
                        Log($"Failed to open link: {ex.Message}");
                    }
                }
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

            var items = dgResults.Items.Cast<GalleryItem>().ToList();
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

        public static string GetSimilarityCore(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";

            string core = name.ToLower();

            // 1. Remove brackets [...] and their contents
            core = Regex.Replace(core, @"\[[^\]]*\]", "");

            // 2. Remove curly braces {...} and their contents
            core = Regex.Replace(core, @"\{[^\}]*\}", "");

            // 3. Remove parentheses (...) and their contents
            core = Regex.Replace(core, @"\([^\)]*\)", "");

            // 4. Remove common variation/part keywords in proper order (longest first)
            string[] keywords = new string[]
            {
                @"extra\s+version", @"copy\s+of",
                @"part\s+\d+", @"part\d+", @"pt\s+\d+", @"pt\d+", @"vol\s+\d+", @"vol\d+",
                @"ch\s+\d+", @"ch\d+", @"chap\s+\d+", @"chap\d+", @"chapter\s+\d+", @"chapter\d+",
                @"minidoujin", @"doujinshi", @"decensored", @"uncensored",
                @"colorized", @"censored", @"colored",
                @"extra", @"extras", @"version",
                @"rewrite", @"copy", @"doujin", @"dj",
                @"\bch\b", @"\bchap\b", @"\bpart\b", @"\bpt\b", @"\bvol\b"
            };

            foreach (var kw in keywords)
            {
                core = Regex.Replace(core, kw, "");
            }

            // 5. Remove numbers at the end of words or standalone
            core = Regex.Replace(core, @"\b\d+\b", "");

            // 6. Remove non-alphanumeric characters
            core = Regex.Replace(core, @"[^a-z0-9]", "");

            return core.Trim();
        }

        public void RecalculateDuplicates()
        {
            var groups = _scrapedItems
                .GroupBy(item => GetSimilarityCore(item.Name))
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .ToList();

            foreach (var item in _scrapedItems)
            {
                item.IsDuplicate = false;
            }

            foreach (var group in groups)
            {
                if (group.Count() > 1)
                {
                    foreach (var item in group)
                    {
                        item.IsDuplicate = true;
                    }
                }
            }
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
                _duplicateWindowInstance.Closed += (s, args) => { _duplicateWindowInstance = null; };
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

            dgResults.ItemsSource = null;
            foreach (var item in itemsToRemove)
            {
                _scrapedItems.Remove(item);
            }
            dgResults.ItemsSource = _scrapedItems;

            lblLinkCount.Text = _scrapedItems.Count.ToString();
            Log($"Deleted {itemsToRemove.Count} checked item(s).");
            lblStatus.Text = $"Deleted {itemsToRemove.Count} checked item(s).";

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
            var items = dgResults.Items.Cast<GalleryItem>().Where(item => item.IsChecked).ToList();
            if (!items.Any())
            {
                ShowNoCheckedItemsError();
                return;
            }
            await StartDownloadProcessAsync(items);
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
                Dispatcher.BeginInvoke(new Action(UpdateStats));
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
    }
}

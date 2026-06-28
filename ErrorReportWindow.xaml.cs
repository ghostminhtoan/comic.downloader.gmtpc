using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace get_link_manga
{
    public partial class ErrorReportWindow : Window
    {
        private readonly GalleryItem _queueItem;
        private readonly MainWindow _mainWindow;
        private readonly List<ErrorDisplayItem> _displayItems;

        public ErrorReportWindow(GalleryItem queueItem, MainWindow mainWindow)
        {
            InitializeComponent();
            _queueItem = queueItem;
            _mainWindow = mainWindow;

            var uniqueErrors = queueItem.GetUniqueErrors();
            bool isVi = _mainWindow._isVietnameseUi;

            Title = isVi ? "BÁO CÁO LỖI" : "ERROR REPORT";
            lblErrorTitle.Text = isVi ? $"BÁO CÁO LỖI — {queueItem.Name}" : $"ERROR REPORT — {queueItem.Name}";
            lblErrorSubtitle.Text = isVi
                ? $"Nguồn: {queueItem.SourceDomain} | Tổng lỗi theo trang: {uniqueErrors.Count}"
                : $"Source: {queueItem.SourceDomain} | Total errors by page: {uniqueErrors.Count}";

            btnRetryFailed.Content = isVi ? "🔄 THỬ LẠI LỖI" : "🔄 RETRY FAILED";
            btnCopyErrors.Content = isVi ? "📋 SAO CHÉP LỖI" : "📋 COPY ERRORS";
            btnClose.Content = isVi ? "ĐÓNG" : "CLOSE";

            _displayItems = uniqueErrors.Select(e => CreateDisplayItem(queueItem, e, isVi)).ToList();
            dgErrors.ItemsSource = _displayItems;
        }

        private static ErrorDisplayItem CreateDisplayItem(GalleryItem queueItem, ErrorDetail error, bool isVi)
        {
            string rawMsg = error?.ErrorMessage ?? "Unknown error";
            string localizedMsg = LocalizeErrorMessage(rawMsg, isVi);

            return new ErrorDisplayItem
            {
                Icon = "❌",
                ComicName = queueItem?.Name ?? "N/A",
                ComicUrl = queueItem?.Link ?? string.Empty,
                ChapterName = error?.ChapterName ?? "N/A",
                PageNumber = error != null && !string.IsNullOrEmpty(error.PageName) ? error.PageName : (error?.PageNumber ?? 0).ToString(),
                ErrorMessage = localizedMsg,
                ImageUrl = error?.ImageUrl,
                ChapterUrl = error?.ChapterUrl
            };
        }

        private static string LocalizeErrorMessage(string rawMsg, bool isVi)
        {
            if (isVi)
            {
                if (rawMsg.Contains("Missing page"))
                    return "Trang bị thiếu";
                if (rawMsg.Contains("Image corrupt or too small"))
                    return "File ảnh hỏng hoặc quá nhỏ";
                return rawMsg;
            }

            if (rawMsg.Contains("Trang bị thiếu (Missing page)"))
                return "Missing page";
            if (rawMsg.Contains("File ảnh hỏng hoặc quá nhỏ"))
                return "Image corrupt or too small";
            return rawMsg;
        }

        private void BtnOpenComicLink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe)
            {
                string url = fe.Tag as string;
                if (string.IsNullOrWhiteSpace(url) && fe.DataContext is ErrorDisplayItem item)
                {
                    url = item.ComicUrl;
                }

                if (!string.IsNullOrWhiteSpace(url))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = url,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void BtnClickErrorText_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ErrorDisplayItem item)
            {
                var galleryItem = _mainWindow._scrapedItems.FirstOrDefault(x => string.Equals(x.Name, item.ComicName, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Link, item.ComicUrl, StringComparison.OrdinalIgnoreCase));
                if (galleryItem != null)
                {
                    _mainWindow.ScrollResultsItemIntoView(galleryItem);
                }
            }
        }

        private async void BtnRetryFailed_Click(object sender, RoutedEventArgs e)
        {
            bool isVi = _mainWindow._isVietnameseUi;
            if (_queueItem == null || !_queueItem.GetUniqueErrors().Any())
            {
                MessageBox.Show(
                    isVi ? "Không có lỗi nào để thử lại." : "No errors to retry.",
                    isVi ? "Thông tin" : "Information",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            Close();
            await _mainWindow.RetryDownloadQueueItemErrorsAsync(_queueItem);
        }

        private void BtnCopyErrors_Click(object sender, RoutedEventArgs e)
        {
            bool isVi = _mainWindow._isVietnameseUi;
            if (_displayItems.Count == 0)
            {
                MessageBox.Show(
                    isVi ? "Không có lỗi nào để sao chép." : "No errors to copy.",
                    isVi ? "Thông tin" : "Information",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine(isVi ? $"Báo cáo lỗi — {_queueItem.Name}" : $"Error Report — {_queueItem.Name}");
            sb.AppendLine(isVi ? $"Nguồn: {_queueItem.Link}" : $"Source: {_queueItem.Link}");
            sb.AppendLine(isVi ? $"Tổng số lỗi: {_displayItems.Count}" : $"Total errors: {_displayItems.Count}");
            sb.AppendLine(new string('-', 60));

            foreach (var error in _displayItems)
            {
                sb.AppendLine(isVi
                    ? $"❌ {error.ComicName} | {error.ChapterName}, Trang {error.PageNumber} — {error.ErrorMessage}"
                    : $"❌ {error.ComicName} | {error.ChapterName}, Page {error.PageNumber} — {error.ErrorMessage}");

                if (!string.IsNullOrWhiteSpace(error.ComicUrl))
                {
                    sb.AppendLine($"   Comic: {error.ComicUrl}");
                }

                if (!string.IsNullOrWhiteSpace(error.ImageUrl))
                {
                    sb.AppendLine($"   URL: {error.ImageUrl}");
                }
            }

            Clipboard.SetText(sb.ToString());
            MessageBox.Show(
                isVi ? $"Đã sao chép {_displayItems.Count} lỗi vào clipboard." : $"Copied {_displayItems.Count} errors to clipboard.",
                isVi ? "Đã sao chép" : "Copied",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ChkSelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox chk)
            {
                bool isChecked = chk.IsChecked ?? false;
                foreach (var item in _displayItems)
                {
                    item.IsChecked = isChecked;
                }
            }
        }

        private void DgErrors_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Space)
            {
                if (dgErrors.SelectedItems.Count > 0)
                {
                    var firstItem = dgErrors.SelectedItems.Cast<ErrorDisplayItem>().FirstOrDefault();
                    if (firstItem != null)
                    {
                        bool targetState = !firstItem.IsChecked;
                        foreach (var item in dgErrors.SelectedItems.Cast<ErrorDisplayItem>())
                        {
                            item.IsChecked = targetState;
                        }
                    }
                    e.Handled = true;
                }
            }
        }

        private void MenuCheckSelected_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in dgErrors.SelectedItems.Cast<ErrorDisplayItem>())
            {
                item.IsChecked = true;
            }
        }

        private void MenuUncheckSelected_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in dgErrors.SelectedItems.Cast<ErrorDisplayItem>())
            {
                item.IsChecked = false;
            }
        }

        private void MenuInvertChecked_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _displayItems)
            {
                item.IsChecked = !item.IsChecked;
            }
        }

        private void MenuOpenInBrowser_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in dgErrors.SelectedItems.Cast<ErrorDisplayItem>())
            {
                string url = !string.IsNullOrEmpty(item.ComicUrl) ? item.ComicUrl : item.ImageUrl;
                if (!string.IsNullOrEmpty(url))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = url,
                            UseShellExecute = true
                        });
                    }
                    catch { }
                }
            }
        }

        private void MenuCopySelected_Click(object sender, RoutedEventArgs e)
        {
            if (dgErrors.SelectedItems.Count == 0) return;
            bool isVi = _mainWindow._isVietnameseUi;
            var sb = new StringBuilder();
            foreach (var error in dgErrors.SelectedItems.Cast<ErrorDisplayItem>())
            {
                sb.AppendLine(isVi
                    ? $"❌ {error.ComicName} | {error.ChapterName}, Trang {error.PageNumber} — {error.ErrorMessage}"
                    : $"❌ {error.ComicName} | {error.ChapterName}, Page {error.PageNumber} — {error.ErrorMessage}");

                if (!string.IsNullOrWhiteSpace(error.ComicUrl))
                {
                    sb.AppendLine($"   Comic: {error.ComicUrl}");
                }
            }
            Clipboard.SetText(sb.ToString());
        }

        private void MenuDeleteErrorsAndBooks_Click(object sender, RoutedEventArgs e)
        {
            if (dgErrors.SelectedItems.Count == 0) return;

            var itemsToRemove = dgErrors.SelectedItems.Cast<ErrorDisplayItem>().ToList();
            foreach (var item in itemsToRemove)
            {
                _mainWindow.RemoveErrorFromGlobalAndQueue(item.ComicName, item.ChapterName, item.PageNumber, item.ErrorMessage);
                _displayItems.Remove(item);
            }

            dgErrors.Items.Refresh();

            var uniqueErrors = _queueItem.GetUniqueErrors();
            bool isVi = _mainWindow._isVietnameseUi;
            lblErrorSubtitle.Text = isVi
                ? $"Nguồn: {_queueItem.SourceDomain} | Tổng lỗi theo trang: {uniqueErrors.Count}"
                : $"Source: {_queueItem.SourceDomain} | Total errors by page: {uniqueErrors.Count}";
        }

        private void DgErrors_Sorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;
            var column = e.Column;
            var sortMember = column.SortMemberPath;
            if (string.IsNullOrEmpty(sortMember)) return;

            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(dgErrors.ItemsSource) as System.Windows.Data.ListCollectionView;
            if (view == null) return;

            ListSortDirection direction = (column.SortDirection == ListSortDirection.Ascending)
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;

            foreach (var col in dgErrors.Columns)
            {
                col.SortDirection = null;
            }
            column.SortDirection = direction;

            view.CustomSort = new ErrorDisplayItemComparer(sortMember, direction);
        }
    }

    public class ErrorDisplayItem : INotifyPropertyChanged
    {
        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Icon { get; set; }
        public string ComicName { get; set; }
        public string ComicUrl { get; set; }
        public string ChapterName { get; set; }
        public string PageNumber { get; set; }
        public string ErrorMessage { get; set; }
        public string ImageUrl { get; set; }
        public string ChapterUrl { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ErrorDisplayItemComparer : System.Collections.IComparer
    {
        private readonly string _property;
        private readonly ListSortDirection _direction;

        public ErrorDisplayItemComparer(string property, ListSortDirection direction)
        {
            _property = property;
            _direction = direction;
        }

        public int Compare(object x, object y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return _direction == ListSortDirection.Ascending ? -1 : 1;
            if (y == null) return _direction == ListSortDirection.Ascending ? 1 : -1;

            var itemX = (ErrorDisplayItem)x;
            var itemY = (ErrorDisplayItem)y;

            int compareResult = 0;

            switch (_property)
            {
                case "ComicName":
                    compareResult = string.Compare(itemX.ComicName, itemY.ComicName, StringComparison.OrdinalIgnoreCase);
                    if (compareResult == 0)
                    {
                        compareResult = CompareChapters(itemX.ChapterName, itemY.ChapterName);
                    }
                    if (compareResult == 0)
                    {
                        compareResult = ComparePages(itemX.PageNumber, itemY.PageNumber);
                    }
                    break;

                case "ChapterName":
                    compareResult = CompareChapters(itemX.ChapterName, itemY.ChapterName);
                    if (compareResult == 0)
                    {
                        compareResult = ComparePages(itemX.PageNumber, itemY.PageNumber);
                    }
                    break;

                case "PageNumber":
                    compareResult = ComparePages(itemX.PageNumber, itemY.PageNumber);
                    break;

                case "ErrorMessage":
                    compareResult = string.Compare(itemX.ErrorMessage, itemY.ErrorMessage, StringComparison.OrdinalIgnoreCase);
                    if (compareResult == 0)
                    {
                        compareResult = CompareChapters(itemX.ChapterName, itemY.ChapterName);
                    }
                    if (compareResult == 0)
                    {
                        compareResult = ComparePages(itemX.PageNumber, itemY.PageNumber);
                    }
                    break;

                default:
                    compareResult = 0;
                    break;
            }

            return _direction == ListSortDirection.Ascending ? compareResult : -compareResult;
        }

        private int ComparePages(string pageA, string pageB)
        {
            if (pageA == pageB) return 0;
            if (pageA == null) return -1;
            if (pageB == null) return 1;

            var matchA = System.Text.RegularExpressions.Regex.Match(pageA, @"\d+");
            var matchB = System.Text.RegularExpressions.Regex.Match(pageB, @"\d+");

            if (matchA.Success && matchB.Success)
            {
                if (int.TryParse(matchA.Value, out int valA) && int.TryParse(matchB.Value, out int valB))
                {
                    int numCompare = valA.CompareTo(valB);
                    if (numCompare != 0) return numCompare;
                }
            }

            return string.Compare(pageA, pageB, StringComparison.OrdinalIgnoreCase);
        }

        private int CompareChapters(string chapA, string chapB)
        {
            if (chapA == chapB) return 0;
            if (chapA == null) return -1;
            if (chapB == null) return 1;

            var matchA = System.Text.RegularExpressions.Regex.Match(chapA, @"\d+(\.\d+)?");
            var matchB = System.Text.RegularExpressions.Regex.Match(chapB, @"\d+(\.\d+)?");

            if (matchA.Success && matchB.Success)
            {
                string prefixA = chapA.Substring(0, matchA.Index);
                string prefixB = chapB.Substring(0, matchB.Index);
                int prefixCompare = string.Compare(prefixA, prefixB, StringComparison.OrdinalIgnoreCase);
                if (prefixCompare != 0) return prefixCompare;

                if (double.TryParse(matchA.Value, out double valA) && double.TryParse(matchB.Value, out double valB))
                {
                    int numCompare = valA.CompareTo(valB);
                    if (numCompare != 0) return numCompare;
                }
            }

            return string.Compare(chapA, chapB, StringComparison.OrdinalIgnoreCase);
        }
    }
}

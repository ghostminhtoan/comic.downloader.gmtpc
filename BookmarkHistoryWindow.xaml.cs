using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace get_link_manga
{
    public partial class BookmarkHistoryWindow : Window
    {
        private readonly BookmarkHistoryManager _manager;

        public BookmarkHistoryWindow()
        {
            InitializeComponent();
            _manager = new BookmarkHistoryManager();
            RefreshBookmarks();
            RefreshReaderPageBookmarks();
            RefreshHistory();
        }

        public void RefreshBookmarks()
        {
            var bookmarks = _manager.GetBookmarks();
            lstBookmarks.ItemsSource = bookmarks;
            lblBookmarkCount.Text = $"Tổng: {bookmarks.Count} bookmark(s)";
        }

        public void RefreshHistory()
        {
            var history = _manager.GetHistory();
            lstHistory.ItemsSource = history;
            lblHistoryCount.Text = $"Tổng: {history.Count} mục";
        }

        public void RefreshReaderPageBookmarks()
        {
            var bookmarks = _manager.GetReaderPageBookmarks();
            lstReaderPageBookmarks.ItemsSource = bookmarks;
            lblReaderPageBookmarkCount.Text = $"Tổng: {bookmarks.Count} bookmark(s)";
        }

        // ===== BOOKMARKS =====

        private void BtnOpenBookmarkLink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string url && !string.IsNullOrEmpty(url))
            {
                OpenUrl(url);
            }
        }

        private void BtnDeleteBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string url && !string.IsNullOrEmpty(url))
            {
                _manager.RemoveBookmark(url);
                RefreshBookmarks();
            }
        }

        private void LstBookmarks_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (lstBookmarks.SelectedItem is BookmarkEntry entry)
            {
                OpenUrl(entry.Url);
            }
        }

        private void BtnOpenReaderPageBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn &&
                btn.DataContext is ReaderPageBookmarkEntry entry &&
                Owner is MainWindow mainWindow)
            {
                mainWindow.OpenReaderPageBookmark(entry);
            }
        }

        private void BtnDeleteReaderPageBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string bookmarkId && !string.IsNullOrEmpty(bookmarkId))
            {
                _manager.RemoveReaderPageBookmark(bookmarkId);
                RefreshReaderPageBookmarks();
            }
        }

        private void BtnClearReaderPageBookmarks_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Bạn có chắc muốn xóa toàn bộ bookmark trang?", "Xác nhận",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _manager.ClearReaderPageBookmarks();
                RefreshReaderPageBookmarks();
            }
        }

        private void LstReaderPageBookmarks_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (lstReaderPageBookmarks.SelectedItem is ReaderPageBookmarkEntry entry &&
                Owner is MainWindow mainWindow)
            {
                mainWindow.OpenReaderPageBookmark(entry);
            }
        }

        // ===== HISTORY =====

        private void BtnOpenHistoryLink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string url && !string.IsNullOrEmpty(url))
            {
                OpenUrl(url);
            }
        }

        private void BtnOpenHistoryFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path && !string.IsNullOrEmpty(path))
            {
                try
                {
                    if (System.IO.Directory.Exists(path))
                    {
                        if (!ShellFolderLauncher.TryOpenFolder(path, out string openError))
                        {
                            MessageBox.Show($"Không thể mở thư mục: {openError}", "Warning",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                    else
                    {
                        MessageBox.Show($"Thư mục không tồn tại:\n{path}", "Warning",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Không thể mở thư mục: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnClearHistory_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Bạn có chắc muốn xóa toàn bộ lịch sử tải?", "Xác nhận",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _manager.ClearHistory();
                RefreshHistory();
            }
        }

        private void BtnSaveHistory_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                Filter = "JSON Files (*.json)|*.json",
                FileName = "history_backup.json"
            };
            if (sfd.ShowDialog(this) == true)
            {
                try
                {
                    _manager.ExportHistory(sfd.FileName);
                    MessageBox.Show("Lưu lịch sử thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi khi lưu lịch sử: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnLoadHistory_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "JSON Files (*.json)|*.json"
            };
            if (ofd.ShowDialog(this) == true)
            {
                try
                {
                    bool ok = _manager.ImportHistory(ofd.FileName);
                    if (ok)
                    {
                        RefreshHistory();
                        MessageBox.Show("Tải lịch sử thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("Định dạng file lịch sử không hợp lệ.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi khi tải lịch sử: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnSaveBookmarks_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                Filter = "JSON Files (*.json)|*.json",
                FileName = "bookmarks_backup.json"
            };
            if (sfd.ShowDialog(this) == true)
            {
                try
                {
                    _manager.ExportBookmarks(sfd.FileName);
                    MessageBox.Show("Lưu bookmarks thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi khi lưu bookmarks: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnLoadBookmarks_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "JSON Files (*.json)|*.json"
            };
            if (ofd.ShowDialog(this) == true)
            {
                try
                {
                    bool ok = _manager.ImportBookmarks(ofd.FileName);
                    if (ok)
                    {
                        RefreshBookmarks();
                        MessageBox.Show("Tải bookmarks thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("Định dạng file bookmarks không hợp lệ.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi khi tải bookmarks: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // ===== HELPERS =====

        private void OpenUrl(string url)
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
                MessageBox.Show($"Không thể mở link: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void SelectTab(int index)
        {
            if (tabMenu != null && index >= 0 && index < tabMenu.Items.Count)
            {
                tabMenu.SelectedIndex = index;
            }
        }

        public void ApplyLanguage(bool isVietnamese)
        {
            Title = isVietnamese ? "LỊCH SỬ, DOWNLOAD BOOKMARKS & COMIC BOOKMARKS" : "HISTORY, DOWNLOAD BOOKMARKS & COMIC BOOKMARKS";
        }
    }
}

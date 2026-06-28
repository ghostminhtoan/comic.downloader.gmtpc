using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private void CmbConnections_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (cmbConnections == null || cmbConnections.SelectedItem == null)
                {
                    return;
                }

                int newLimit = GetCurrentConnectionLimit();

                foreach (var item in _scrapedItems)
                {
                    item.ConnectionCount = newLimit;
                }

                _activeBookSemaphore?.AdjustLimit();
                Log($"[Connection] Đã cập nhật số trang song song mỗi book thành {newLimit}.");
                RequestGalleryListAutosave(500);
            }
            catch
            {
            }
        }

        private void CmbMultiDownload_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (cmbMultiDownload == null || cmbMultiDownload.SelectedItem == null) return;
                var selectedItem = cmbMultiDownload.SelectedItem as ComboBoxItem;
                if (selectedItem == null) return;
                if (!int.TryParse(selectedItem.Content.ToString(), out int newVal)) return;

                _currentMaxParallelBooks = newVal;
                Log($"[Multi Download] Số luồng tải song song được chỉnh thành {newVal}.");
                _activeBookSemaphore?.AdjustLimit();
                RequestGalleryListAutosave(500);
            }
            catch
            {
            }
        }

        private void CmbCreateSubfolderDomain_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressCreateSubfolderEvents || !_createSubfolderUiReady)
            {
                return;
            }

            string previousDomainKey = _createSubfolderSelectedDomainKey;
            string newDomainKey = GetSelectedCreateSubfolderDomainKey();
            if (!string.IsNullOrWhiteSpace(previousDomainKey))
            {
                PersistCreateSubfolderForDomain(previousDomainKey);
            }

            _createSubfolderSelectedDomainKey = newDomainKey;
            UpdateCreateSubfolderFieldsFromSelection();
        }

        private void CmbNhentaiSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingNhentaiUrl) return;
            if (txtNhentaiTagUrl == null) return;

            if (cmbNhentaiSort.SelectedItem is ComboBoxItem selectedItem)
            {
                string sortVal = selectedItem.Tag?.ToString();
                if (!string.IsNullOrEmpty(sortVal))
                {
                    _isUpdatingNhentaiUrl = true;
                    try
                    {
                        txtNhentaiTagUrl.Text = UpdateNhentaiUrlSort(txtNhentaiTagUrl.Text, sortVal);
                    }
                    finally
                    {
                        _isUpdatingNhentaiUrl = false;
                    }
                }
            }
        }

        private bool _isSingleComicFolderType = true;
        private bool _suppressDownloadFolderTypeEvents = false;

        private void CmbDownloadFolderType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressDownloadFolderTypeEvents) return;

            try
            {
                if (cmbDownloadFolderType == null || cmbDownloadFolderType.SelectedItem == null) return;
                var selectedItem = cmbDownloadFolderType.SelectedItem as ComboBoxItem;
                if (selectedItem == null) return;
                
                string content = selectedItem.Content.ToString();
                bool newMode = content.Equals("Single comic", StringComparison.OrdinalIgnoreCase);
                if (_isSingleComicFolderType != newMode)
                {
                    _isSingleComicFolderType = newMode;
                    Log($"[Folder Type] Đã chuyển đổi chế độ download sang: {content}");
                    
                    // Sync with float button window
                    _lightNovelFloatingControlWindow?.UpdateFolderType(newMode ? 0 : 1);
                    
                    // Persist settings
                    RequestGalleryListAutosave(500);
                }
            }
            catch {}
        }
    }
}

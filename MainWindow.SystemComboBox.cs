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

                RefreshActiveDownloadConcurrency();
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

                _currentMaxParallelBooks = GetCurrentMultiDownloadLimit();
                Log($"[Multi Download] Số luồng tải song song được chỉnh thành {newVal}.");
                RefreshActiveDownloadConcurrency();
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


    }
}

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
            if (_suppressConnectionEvents) return;

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

                _lightNovelFloatingControlWindow?.UpdateConnections(cmbConnections.SelectedIndex);
                
                try
                {
                    string configPath = System.IO.Path.Combine(PortablePaths.PortableDataRoot, "connection_limit.txt");
                    System.IO.File.WriteAllText(configPath, newLimit.ToString());
                }
                catch { }

                RequestGalleryListAutosave(500);
            }
            catch
            {
            }
        }

        private void CmbMultiDownload_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressMultiDownloadEvents) return;

            try
            {
                if (cmbMultiDownload == null || cmbMultiDownload.SelectedItem == null) return;
                var selectedItem = cmbMultiDownload.SelectedItem as ComboBoxItem;
                if (selectedItem == null) return;
                if (!int.TryParse(selectedItem.Content.ToString(), out int newVal)) return;

                _currentMaxParallelBooks = GetCurrentMultiDownloadLimit();
                Log($"[Multi Download] Số luồng tải song song được chỉnh thành {newVal}.");
                RefreshActiveDownloadConcurrency();

                _lightNovelFloatingControlWindow?.UpdateMultiDownload(cmbMultiDownload.SelectedIndex);

                RequestGalleryListAutosave(500);
            }
            catch
            {
            }
        }

        private void CmbThumbCacheConnection_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (cmbThumbCacheConnection == null || cmbThumbCacheConnection.SelectedItem == null) return;
                var selectedItem = cmbThumbCacheConnection.SelectedItem as ComboBoxItem;
                if (selectedItem == null) return;
                if (!int.TryParse(selectedItem.Content.ToString(), out int newVal)) return;

                _galleryHoverPreviewImageSemaphore = new System.Threading.SemaphoreSlim(newVal, newVal);
                Log($"[Thumb Connection] Số luồng kết nối tải thumbnail song song chỉnh thành {newVal}.");
                PrefetchAllScrapedItemsPreviewCache();
                try
                {
                    string configPath = System.IO.Path.Combine(PortablePaths.PortableDataRoot, "thumb_connection_limit.txt");
                    System.IO.File.WriteAllText(configPath, newVal.ToString());
                }
                catch { }
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

using System;
using System.Windows;
using System.Windows.Controls;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
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

        private string GetDownloadChapterFolderName(string bookTitle, string chapterTitle)
        {
            // ponytail: helper này luôn trả folder tên chapter; caller tự ghép book cho multi-comic.
            return GetSafeChapterPathName(chapterTitle);
        }
    }
}

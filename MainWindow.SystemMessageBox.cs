using System;
using System.Windows;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        public MessageBoxResult ShowMessageBox(string message, string title, MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
        {
            if (Dispatcher.CheckAccess())
            {
                return MessageBox.Show(this, message, title, button, icon);
            }
            else
            {
                return (MessageBoxResult)Dispatcher.Invoke(() => MessageBox.Show(this, message, title, button, icon));
            }
        }

        public void ShowError(string message, string title = "Error")
        {
            string t = _isVietnameseUi ? "Lỗi" : title;
            ShowMessageBox(message, t, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public void ShowWarning(string message, string title = "Warning")
        {
            string t = _isVietnameseUi ? "Cảnh báo" : title;
            ShowMessageBox(message, t, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        public void ShowInfo(string message, string title = "Information")
        {
            string t = _isVietnameseUi ? "Thông tin" : title;
            ShowMessageBox(message, t, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public bool ShowConfirm(string message, string title = "Confirmation")
        {
            string t = _isVietnameseUi ? "Xác nhận" : title;
            return ShowMessageBox(message, t, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        public void ShowNoCheckedItemsError()
        {
            ShowInfo("Vui lòng tích chọn ít nhất 1 truyện để tải (Please check at least one gallery to download).", "Information");
        }

        public void ShowNoSelectedItemsError()
        {
            ShowInfo("Vui lòng bôi đen chọn ít nhất 1 dòng để tải (Please select at least one highlighted line to download).", "Information");
        }
    }
}

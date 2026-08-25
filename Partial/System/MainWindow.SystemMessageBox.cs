using System;
using System.Windows;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        public MessageBoxResult ShowMessageBox(string message, string title, MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
        {
            Window activeWin = null;
            if (Dispatcher.CheckAccess())
            {
                foreach (Window win in Application.Current.Windows)
                {
                    if (win.IsActive || (win.Name == "_externalBookListWindow" || win.Title.Contains("Danh sách truyện chờ tải") || win.Title.Contains("Extracted Gallery Links")))
                    {
                        activeWin = win;
                        break;
                    }
                }
                if (activeWin == null) activeWin = this;
                return MessageBox.Show(activeWin, message, title, button, icon);
            }
            else
            {
                return (MessageBoxResult)Dispatcher.Invoke(() =>
                {
                    foreach (Window win in Application.Current.Windows)
                    {
                        if (win.IsActive || (win.Name == "_externalBookListWindow" || win.Title.Contains("Danh sách truyện chờ tải") || win.Title.Contains("Extracted Gallery Links")))
                        {
                            activeWin = win;
                            break;
                        }
                    }
                    if (activeWin == null) activeWin = this;
                    return MessageBox.Show(activeWin, message, title, button, icon);
                });
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

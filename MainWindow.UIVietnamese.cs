using System.Windows;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private void ApplyVietnameseUiText()
        {
            Title = "Comic-GMTPC v1.0 - Tiếng Việt";

            if (txtHeaderTitle != null) txtHeaderTitle.Text = string.Empty;
            if (txtHeaderSubtitle != null) txtHeaderSubtitle.Text = string.Empty;
            if (txtHeaderStepPrimary != null) txtHeaderStepPrimary.Text = "BƯỚC 1";
            if (txtHeaderStepPrimaryTitle != null) txtHeaderStepPrimaryTitle.Text = "Source";
            if (txtHeaderStepSecondary != null) txtHeaderStepSecondary.Text = "BƯỚC 2";
            if (txtHeaderStepSecondaryTitle != null) txtHeaderStepSecondaryTitle.Text = "TẢI VỀ";
            if (txtLanguageLabel != null) txtLanguageLabel.Text = "ENG";
            if (txtLanguageTarget != null) txtLanguageTarget.Text = "VI";
            if (txtTotalBooksLabel != null) txtTotalBooksLabel.Text = "Tổng truyện: ";
            if (txtBooksCompleteLabel != null) txtBooksCompleteLabel.Text = "Hoàn tất: ";
            if (txtErrorBooksLabel != null) txtErrorBooksLabel.Text = "Lỗi: ";
            if (txtResultsHeader != null) txtResultsHeader.Text = "DANH SÁCH CHỜ TẢI";

            if (btnShutdownMenu != null) btnShutdownMenu.Content = "⏰";
            if (btnShutdownMenu != null) btnShutdownMenu.ToolTip = "Tùy chọn tắt máy";
            if (txtShutdownPopupHeader != null) txtShutdownPopupHeader.Text = "TÙY CHỌN TẮT MÁY";
            if (chkShutdownAfterCompleted != null) chkShutdownAfterCompleted.Content = "tắt máy sau khi tải xong";
            if (txtShutdownCountdownLabel != null) txtShutdownCountdownLabel.Text = "tắt máy sau ngày:giờ:phút:giây";
            if (txtShutdownDaysLabel != null) txtShutdownDaysLabel.Text = "NGÀY";
            if (txtShutdownHoursLabel != null) txtShutdownHoursLabel.Text = "GIỜ";
            if (txtShutdownMinutesLabel != null) txtShutdownMinutesLabel.Text = "PHÚT";
            if (txtShutdownSecondsLabel != null) txtShutdownSecondsLabel.Text = "GIÂY";
            if (txtShutdownPopupHint != null) txtShutdownPopupHint.Text = "0:1:30:0 = 1 giờ 30 phút. App sẽ dùng lệnh shutdown của Windows.";
            if (btnScheduleShutdownTimer != null) btnScheduleShutdownTimer.Content = "HẸN GIỜ";
            if (btnCancelShutdownTimer != null) btnCancelShutdownTimer.Content = "HỦY";
            if (btnCloseShutdownPopup != null) btnCloseShutdownPopup.Content = "ĐÓNG";

            ApplyUiTextMappings(true);
            ApplyResultsGridHeaderLanguage("CHI TIẾT TRUYỆN", "TRẠNG THÁI", "TIẾN TRÌNH", "XEM");

            if (btnSaveList != null) btnSaveList.Content = "Lưu mặc định";
            if (btnLoadList != null) btnLoadList.Content = "Mở file mặc định";
            if (btnNewList != null) btnNewList.Content = "Danh sách mới";
            if (btnSaveCustomList != null) btnSaveCustomList.Content = "Lưu file khác";
            if (btnLoadCustomList != null) btnLoadCustomList.Content = "Mở file khác";
            if (btnRestoreOrder != null) btnRestoreOrder.Content = "Trả về thứ tự gốc";
            if (btnDuplicateName != null) btnDuplicateName.Content = "Tên trùng";
            if (btnNoLinkViHentai != null) btnNoLinkViHentai.Content = "Không có chapter";
            if (btnReverseOrder != null) btnReverseOrder.Content = "Đảo thứ tự";
            if (btnClearComplete != null) btnClearComplete.Content = "Ẩn truyện đã xong";

            UpdateLatestChapterButtonLabel();
        }
    }
}

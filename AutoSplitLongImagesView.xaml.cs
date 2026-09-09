using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace get_link_manga
{
    public partial class AutoSplitLongImagesView : UserControl
    {
        public AutoSplitLongImagesView()
        {
            InitializeComponent();
        }

        private void BtnBrowseSplitFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new VistaFolderBrowser
            {
                Title = "Chọn thư mục chứa ảnh dài để tự động cắt",
                SelectedPath = txtSplitFolderPath.Text
            };
            var window = Window.GetWindow(this);
            if (window != null && dialog.ShowDialog(new System.Windows.Interop.WindowInteropHelper(window).Handle))
            {
                txtSplitFolderPath.Text = dialog.SelectedPath;
            }
        }

        private async void BtnStartSplit_Click(object sender, RoutedEventArgs e)
        {
            string folder = txtSplitFolderPath.Text.Trim();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                MessageBox.Show("Vui lòng chọn thư mục hợp lệ!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!int.TryParse(txtSplitHeight.Text, out int splitHeight) || splitHeight < 100)
            {
                MessageBox.Show("Chiều cao cắt không hợp lệ (phải >= 100).", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!int.TryParse(txtSplitQuality.Text, out int splitQuality) || splitQuality < 10 || splitQuality > 100)
            {
                MessageBox.Show("Quality không hợp lệ (từ 10 - 100).", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            btnStartSplit.IsEnabled = false;
            txtSplitLog.Clear();
            pbSplitProgress.Value = 0;
            Log($"Đang quét thư mục: {folder}...");

            try
            {
                await Task.Run(() => ProcessFolder(folder, splitHeight, splitQuality));
                Log("Hoàn tất!");
            }
            catch (Exception ex)
            {
                Log($"Lỗi: {ex.Message}");
            }
            finally
            {
                btnStartSplit.IsEnabled = true;
                pbSplitProgress.Value = 100;
            }
        }

        private void ProcessFolder(string folder, int height, int quality)
        {
            var extensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var files = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)
                                 .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                                 .ToList();

            if (files.Count == 0)
            {
                Log("Không tìm thấy ảnh nào trong thư mục này.");
                return;
            }

            Log($"Tìm thấy {files.Count} ảnh. Bắt đầu kiểm tra và cắt...");
            int splitCount = 0;

            for (int i = 0; i < files.Count; i++)
            {
                string file = files[i];
                try
                {
                    bool wasSplit = MainWindow.TrySplitImageFile(file, height, quality);
                    if (wasSplit)
                    {
                        splitCount++;
                        Log($"[Đã cắt] {Path.GetFileName(file)}");
                    }
                }
                catch (Exception ex)
                {
                    Log($"[Lỗi] {Path.GetFileName(file)}: {ex.Message}");
                }

                UpdateProgress((double)(i + 1) / files.Count * 100);
            }

            Log($"Đã kiểm tra {files.Count} ảnh. Tổng cộng cắt thành công {splitCount} ảnh dài.");
        }

        private void Log(string message)
        {
            Dispatcher.InvokeAsync(() =>
            {
                txtSplitLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
                txtSplitLog.ScrollToEnd();
            });
        }

        private void UpdateProgress(double value)
        {
            Dispatcher.InvokeAsync(() =>
            {
                pbSplitProgress.Value = value;
            });
        }
    }
}

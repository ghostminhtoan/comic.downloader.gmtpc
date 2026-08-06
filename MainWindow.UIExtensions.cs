using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        // 3. Toast Notification
        public void ShowToast(string message, int durationMs = 3000)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (toastContainer == null) return;

                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x0D, 0x12, 0x1F)),
                    BorderBrush = (Brush)TryFindResource("CyberpunkCyanBrush") ?? new SolidColorBrush(Color.FromRgb(0x00, 0xF0, 0xFF)),
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(16, 12, 16, 12),
                    Margin = new Thickness(0, 0, 0, 10),
                    MaxWidth = 350,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = Color.FromRgb(0x00, 0xF0, 0xFF),
                        BlurRadius = 10,
                        ShadowDepth = 0,
                        Opacity = 0.5
                    }
                };

                var textBlock = new TextBlock
                {
                    Text = message,
                    Foreground = Brushes.White,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                };

                border.Child = textBlock;
                toastContainer.Items.Add(border);

                var timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(durationMs)
                };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    toastContainer.Items.Remove(border);
                };
                timer.Start();
            }));
        }

        // 7. Nhóm nút action ContextMenu
        private void BtnContextMenuOpen_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                btn.ContextMenu.IsOpen = true;
            }
        }

        // 5. Empty State Visibility
        public void UpdateEmptyStateVisibility()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (grdEmptyState == null) return;
                grdEmptyState.Visibility = (_scrapedItems == null || _scrapedItems.Count == 0)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }));
        }

        // 1. Global Progress Bar & Stats
        public void UpdateGlobalProgressBar()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (grdGlobalProgress == null || prgGlobalDownload == null || txtGlobalProgressStats == null) return;

                var items = _scrapedItems.ToList();
                if (items.Count == 0 || _downloadCts == null)
                {
                    grdGlobalProgress.Visibility = Visibility.Collapsed;
                    return;
                }

                int totalToDownload = items.Count(item => item.IsChecked);
                if (totalToDownload == 0)
                {
                    grdGlobalProgress.Visibility = Visibility.Collapsed;
                    return;
                }

                grdGlobalProgress.Visibility = Visibility.Visible;

                int completed = items.Count(item => item.IsChecked &&
                    (string.Equals(item.Status, "Completed", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(item.DownloadingPageProgress, "Done", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(item.DownloadingPageProgress, "Complete", StringComparison.OrdinalIgnoreCase)));

                double progressSum = 0;
                foreach (var item in items.Where(i => i.IsChecked))
                {
                    progressSum += item.DownloadProgressPercent;
                }

                double overallPercent = progressSum / totalToDownload;
                prgGlobalDownload.Value = overallPercent;

                long totalSpeed = 0;
                foreach (var item in items)
                {
                    totalSpeed += item.DownloadSpeedBytesPerSecond;
                }

                string speedStr = totalSpeed > 0 ? $" | Tốc độ: {GalleryItem.FormatSpeedText(totalSpeed)}" : "";
                txtGlobalProgressStats.Text = $"{completed}/{totalToDownload} truyện ({overallPercent:F0}%){speedStr}";
            }));
        }
    }
}

using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private void WirePauseButtonToggle()
        {
            void TogglePauseResume(Button button)
            {
                _isDownloadPaused = !_isDownloadPaused;
                button.Content = _isDownloadPaused ? "Resume all" : "Pause all";
                button.Tag = _isDownloadPaused ? "resume" : "pause";
            }

            Button FindPauseButton()
            {
                Button found = null;

                void Walk(DependencyObject node)
                {
                    if (node == null || found != null)
                    {
                        return;
                    }

                    var button = node as Button;
                    if (button != null)
                    {
                        var contentBlock = button.Content as TextBlock;
                        var contentText = contentBlock != null ? contentBlock.Text : button.Content?.ToString();
                        var tagText = button.Tag?.ToString();
                        var nameText = button.Name;

                        if (string.Equals(contentText, "Pause all", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(contentText, "Resume all", StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrEmpty(contentText) && contentText.IndexOf("pause", StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (!string.IsNullOrEmpty(contentText) && contentText.IndexOf("resume", StringComparison.OrdinalIgnoreCase) >= 0) ||
                            string.Equals(tagText, "pause", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(tagText, "resume", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(nameText, "BtnPauseDownload", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(nameText, "BtnPauseAll", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(nameText, "BtnResumeDownload", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(nameText, "BtnResumeAll", StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrEmpty(nameText) && nameText.IndexOf("pause", StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (!string.IsNullOrEmpty(nameText) && nameText.IndexOf("resume", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            found = button;
                            return;
                        }
                    }

                    var childCount = VisualTreeHelper.GetChildrenCount(node);
                    for (var i = 0; i < childCount; i++)
                    {
                        Walk(VisualTreeHelper.GetChild(node, i));
                        if (found != null)
                        {
                            return;
                        }
                    }
                }

                Walk(this);
                return found;
            }

            Loaded += (sender, args) =>
            {
                var pauseButton = FindPauseButton();
                if (pauseButton == null)
                {
                    return;
                }

                pauseButton.PreviewMouseLeftButtonDown += (buttonSender, mouseArgs) =>
                {
                    mouseArgs.Handled = true;
                    TogglePauseResume((Button)buttonSender);
                };

                pauseButton.PreviewKeyDown += (buttonSender, keyArgs) =>
                {
                    if (keyArgs.Key != Key.Enter && keyArgs.Key != Key.Space)
                    {
                        return;
                    }

                    keyArgs.Handled = true;
                    TogglePauseResume((Button)buttonSender);
                };
            };
        }

        private void BtnMainTheme_Click(object sender, RoutedEventArgs e)
        {
            Topmost = !Topmost;
            UpdateThemeText();
            UpdateMainWindowChromeButtons();
        }

        private void BtnMainMove_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed)
            {
                return;
            }

            e.Handled = true;

            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                UpdateMainWindowChromeButtons();
                return;
            }

            try
            {
                DragMove();
            }
            catch
            {
            }
        }

        private void RootLayout_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed || IsWindowDragBlocked(e.OriginalSource as DependencyObject))
            {
                return;
            }

            // Giới hạn phần move ở header trên (Y < 85 trên rootLayout)
            var pos = e.GetPosition(rootLayout);
            if (pos.Y > 85)
            {
                return;
            }

            // Click đúp để maximize / restore
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                UpdateMainWindowChromeButtons();
                e.Handled = true;
                return;
            }

            try
            {
                DragMove();
                e.Handled = true;
            }
            catch
            {
            }
        }

        private static bool IsWindowDragBlocked(DependencyObject source)
        {
            while (source != null)
            {
                if (source is DataGrid || source is DataGridRow || source is DataGridCell || source is ButtonBase || source is TextBoxBase || source is PasswordBox || source is ComboBox || source is ToggleButton || source is ScrollBar || source is Thumb || source is Slider || source is MenuItem)
                {
                    return true;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return false;
        }

        private void BtnMainMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnMainMaximize_Click(object sender, RoutedEventArgs e)
        {
            Rect workArea = SystemParameters.WorkArea;

            if (_windowPseudoMaximized)
            {
                _windowPseudoMaximized = false;
                WindowState = WindowState.Normal;
                Left = _restoreWindowLeft;
                Top = _restoreWindowTop;
                Width = _restoreWindowWidth;
                Height = _restoreWindowHeight;
            }
            else
            {
                _restoreWindowLeft = Left;
                _restoreWindowTop = Top;
                _restoreWindowWidth = Width;
                _restoreWindowHeight = Height;
                _windowPseudoMaximized = true;
                WindowState = WindowState.Normal;
                Left = workArea.Left;
                Top = workArea.Top;
                Width = workArea.Width;
                Height = workArea.Height;
            }

            UpdateMainWindowChromeButtons();
        }

        private void BtnMainClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void UpdateMainWindowChromeButtons()
        {
            if (btnMainMaximize != null)
            {
                btnMainMaximize.Content = (_windowPseudoMaximized || WindowState == WindowState.Maximized) ? "\uE923" : "\uE922";
                btnMainMaximize.ToolTip = (_windowPseudoMaximized || WindowState == WindowState.Maximized) ? "Restore" : "Maximize";
            }

            if (btnPinWindow != null)
            {
                btnPinWindow.Content = Topmost ? "PIN ON" : "PIN OFF";
                btnPinWindow.ToolTip = Topmost ? "Unpin window" : "Pin window";
            }
        }

        private void StyleComboBoxPopup(ComboBox comboBox)
        {
            if (comboBox == null)
            {
                return;
            }

            comboBox.ApplyTemplate();
            if (comboBox.Template == null)
            {
                return;
            }

            var popup = comboBox.Template.FindName("Popup", comboBox) as Popup;
            if (popup != null)
            {
                popup.Opened += (sender, args) =>
                {
                    if (popup.Child is Border border)
                    {
                        border.Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x12, 0x1F));
                    }
                };
            }
        }

        private static void ScrollTextBoxToEnd(TextBoxBase textBox)
        {
            // ponytail: auto-scroll disabled; re-enable with textBox.ScrollToEnd() if UX asks.
        }

        public static double ExtractNumber(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return 0.0;
            }

            var matches = Regex.Matches(input, @"\d+(?:\.\d+)?");
            if (matches.Count == 0)
            {
                return 0.0;
            }

            return double.TryParse(matches[matches.Count - 1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double result)
                ? result
                : 0.0;
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
            {
                return null;
            }

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed)
                {
                    return typed;
                }

                var result = FindVisualChild<T>(child);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private void BtnReverseOrder_Click(object sender, RoutedEventArgs e)
        {
            var view = ResultsView;
            if (view == null)
            {
                return;
            }

            if (view.SortDescriptions.Count > 0)
            {
                var currentSort = view.SortDescriptions[0];
                string propertyName = currentSort.PropertyName;
                var newDirection = currentSort.Direction == ListSortDirection.Ascending
                    ? ListSortDirection.Descending
                    : ListSortDirection.Ascending;

                view.SortDescriptions.Clear();
                view.SortDescriptions.Add(new SortDescription(propertyName, newDirection));
                if (propertyName == "HasNoChapters")
                {
                    view.SortDescriptions.Add(new SortDescription("OriginalIndex", newDirection));
                }

                Log($"Đảo ngược chiều sắp xếp cho {propertyName} ({newDirection}).");
                return;
            }

            view.SortDescriptions.Add(new SortDescription("OriginalIndex", ListSortDirection.Descending));
            Log("Đảo ngược chiều sắp xếp cho OriginalIndex (Descending).");
        }

        private void BtnClearComplete_Click(object sender, RoutedEventArgs e)
        {
            var toRemove = _scrapedItems
                .Where(item => item != null && item.IsSuccessfullyCompleted())
                .ToList();

            if (toRemove.Count == 0)
            {
                ShowInfo("Không có truyện nào hoàn thành để xóa.", "Thông báo");
                return;
            }

            dgResults.ItemsSource = null;
            foreach (var item in toRemove)
            {
                DeleteProcessMarkdownForItem(item);
                _scrapedItems.Remove(item);
            }
            dgResults.ItemsSource = _scrapedItems;

            Log($"Đã xóa {toRemove.Count} truyện hoàn thành khỏi danh sách.");
            lblLinkCount.Text = _scrapedItems.Count.ToString();
        }
    }
}

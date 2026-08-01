using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private void InitializeWebviewCpuControls()
        {
            if (cmbWebviewCpuAffinity == null || cmbWebviewCpuPriority == null)
            {
                return;
            }

            // Webview CPU Affinity population
            cmbWebviewCpuAffinity.Items.Add("All");
            int maxCores = Environment.ProcessorCount;
            for (int i = 1; i <= maxCores; i++)
            {
                cmbWebviewCpuAffinity.Items.Add(i.ToString());
            }
            cmbWebviewCpuAffinity.SelectedIndex = Math.Min(2, maxCores);
            cmbWebviewCpuAffinity.SelectionChanged += CmbWebviewCpuAffinity_SelectionChanged;

            // Webview CPU Priority population
            cmbWebviewCpuPriority.Items.Add(new ComboBoxItem { Content = "below normal" });
            cmbWebviewCpuPriority.Items.Add(new ComboBoxItem { Content = "normal" });
            cmbWebviewCpuPriority.Items.Add(new ComboBoxItem { Content = "above normal" });
            cmbWebviewCpuPriority.Items.Add(new ComboBoxItem { Content = "high" });
            cmbWebviewCpuPriority.SelectedIndex = 2; // Default: above normal
            cmbWebviewCpuPriority.SelectionChanged += CmbWebviewCpuPriority_SelectionChanged;

            // Update backing fields
            _targetChildCpuAffinityCores = Math.Min(2, maxCores);
            _targetChildCpuPriority = System.Diagnostics.ProcessPriorityClass.AboveNormal;
            ApplyCurrentCpuRestrictions();

            // Link values from scan chapter tab to these comboboxes
            if (_downloadMissingChapterCpuAffinityComboBox != null)
            {
                _downloadMissingChapterCpuAffinityComboBox.SelectedIndex = cmbWebviewCpuAffinity.SelectedIndex;
                _downloadMissingChapterCpuAffinityComboBox.SelectionChanged += (s, e) =>
                {
                    if (cmbWebviewCpuAffinity.SelectedIndex != _downloadMissingChapterCpuAffinityComboBox.SelectedIndex)
                    {
                        cmbWebviewCpuAffinity.SelectedIndex = _downloadMissingChapterCpuAffinityComboBox.SelectedIndex;
                    }
                };
            }

            if (_downloadMissingChapterCpuPriorityComboBox != null)
            {
                _downloadMissingChapterCpuPriorityComboBox.SelectedIndex = cmbWebviewCpuPriority.SelectedIndex;
                _downloadMissingChapterCpuPriorityComboBox.SelectionChanged += (s, e) =>
                {
                    if (cmbWebviewCpuPriority.SelectedIndex != _downloadMissingChapterCpuPriorityComboBox.SelectedIndex)
                    {
                        cmbWebviewCpuPriority.SelectedIndex = _downloadMissingChapterCpuPriorityComboBox.SelectedIndex;
                    }
                };
            }
        }

        private void CmbWebviewCpuAffinity_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbWebviewCpuAffinity == null) return;
            string selected = cmbWebviewCpuAffinity.SelectedItem as string;
            if (string.IsNullOrEmpty(selected)) return;

            try
            {
                int cores = Environment.ProcessorCount;
                if (selected == "All")
                {
                    _targetChildCpuAffinityCores = cores;
                }
                else if (int.TryParse(selected, out int n))
                {
                    _targetChildCpuAffinityCores = Math.Min(cores, Math.Max(1, n));
                }

                if (_downloadMissingChapterCpuAffinityComboBox != null && _downloadMissingChapterCpuAffinityComboBox.SelectedIndex != cmbWebviewCpuAffinity.SelectedIndex)
                {
                    _downloadMissingChapterCpuAffinityComboBox.SelectedIndex = cmbWebviewCpuAffinity.SelectedIndex;
                }

                ApplyCurrentCpuRestrictions();
                Log($"Đã giới hạn CPU Affinity cho WebView2 thành: {selected} core(s).");
            }
            catch (Exception ex)
            {
                Log($"Lỗi khi cài đặt CPU Affinity cho WebView2: {ex.Message}");
            }
        }

        private void CmbWebviewCpuPriority_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbWebviewCpuPriority == null) return;
            var selectedItem = cmbWebviewCpuPriority.SelectedItem as ComboBoxItem;
            if (selectedItem == null) return;
            string content = selectedItem.Content as string;
            if (string.IsNullOrEmpty(content)) return;

            try
            {
                var priority = System.Diagnostics.ProcessPriorityClass.Normal;
                if (content.Equals("below normal", StringComparison.OrdinalIgnoreCase))
                {
                    priority = System.Diagnostics.ProcessPriorityClass.BelowNormal;
                }
                else if (content.Equals("normal", StringComparison.OrdinalIgnoreCase))
                {
                    priority = System.Diagnostics.ProcessPriorityClass.Normal;
                }
                else if (content.Equals("above normal", StringComparison.OrdinalIgnoreCase))
                {
                    priority = System.Diagnostics.ProcessPriorityClass.AboveNormal;
                }
                else if (content.Equals("high", StringComparison.OrdinalIgnoreCase))
                {
                    priority = System.Diagnostics.ProcessPriorityClass.High;
                }

                _targetChildCpuPriority = priority;

                if (_downloadMissingChapterCpuPriorityComboBox != null && _downloadMissingChapterCpuPriorityComboBox.SelectedIndex != cmbWebviewCpuPriority.SelectedIndex)
                {
                    _downloadMissingChapterCpuPriorityComboBox.SelectedIndex = cmbWebviewCpuPriority.SelectedIndex;
                }

                ApplyCurrentCpuRestrictions();
                Log($"Đã giới hạn CPU Priority cho WebView2 thành: {content}.");
            }
            catch (Exception ex)
            {
                Log($"Lỗi khi cài đặt CPU Priority cho WebView2: {ex.Message}");
            }
        }
    }
}

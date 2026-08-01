using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace get_link_manga
{
    /// <summary>
    /// Interaction logic for DirectDownloadWindow.xaml
    /// </summary>
    public partial class DirectDownloadWindow : Window
    {
        private readonly bool _isNhentai;
        public List<string> ImportedLinks { get; private set; } = new List<string>();
        public Action<List<string>> OnImport { get; set; }

        public DirectDownloadWindow(
            bool isNhentai = false,
            string customTitle = null,
            string customDescription = null,
            string customExample = null)
        {
            _isNhentai = isNhentai;
            InitializeComponent();

            if (!string.IsNullOrWhiteSpace(customTitle))
            {
                Title = customTitle;
            }

            if (!string.IsNullOrWhiteSpace(customDescription))
            {
                lblDescription.Text = customDescription;
            }

            if (!string.IsNullOrWhiteSpace(customExample))
            {
                txtLinksInput.Tag = customExample;
            }
            else if (_isNhentai)
            {
                Title = "PASTE NHENTAI DIRECT LINKS";
                lblDescription.Text = "Paste nhentai.xxx gallery links below (one link per line). The system will fetch and analyze titles automatically.";
                // Placeholder set manually since we no longer use HandyControl InfoElement
                txtLinksInput.Tag = "Example:\nhttps://nhentai.xxx/g/123456\nhttps://nhentai.xxx/g/456789";
            }
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            string inputText = txtLinksInput.Text;
            if (string.IsNullOrEmpty(inputText))
            {
                MessageBox.Show("Vui lòng nhập ít nhất 1 đường dẫn để tiếp tục (Please enter at least one link).", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var rawLinks = inputText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var filteredLinks = new List<string>();

            foreach (var rawLink in rawLinks)
            {
                string clean = rawLink.Trim();
                if (string.IsNullOrEmpty(clean)) continue;

                // Simple check for URL scheme
                if (!clean.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                    !clean.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    // Attempt to pre-pend standard view URL if user just enters the ID number
                    if (int.TryParse(clean, out _))
                    {
                        clean = _isNhentai ? "https://nhentai.xxx/g/" + clean : "https://hentaiforce.net/view/" + clean;
                    }
                    else
                    {
                        clean = "https://" + clean;
                    }
                }

                if (Uri.TryCreate(clean, UriKind.Absolute, out Uri resultUri))
                {
                    filteredLinks.Add(clean);
                }
            }

            if (!filteredLinks.Any())
            {
                MessageBox.Show("Không tìm thấy đường dẫn hợp lệ. Vui lòng kiểm tra lại (No valid links found).", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            ImportedLinks = filteredLinks;
            OnImport?.Invoke(ImportedLinks);
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

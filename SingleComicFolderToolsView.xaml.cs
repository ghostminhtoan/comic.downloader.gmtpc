using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace get_link_manga
{
    public partial class SingleComicFolderToolsView : UserControl
    {
        private bool _isRangesLoaded;

        public SingleComicFolderToolsView()
        {
            InitializeComponent();
            Loaded += SingleComicFolderToolsView_Loaded;
        }

        internal TextBox SplitSingleComicRootTextBox => txtSplitSingleComicRoot;
        internal ComboBox SplitChapterGroupSizeComboBox => cmbSplitChapterGroupSize;
        internal ComboBox SplitSingleComicFolderTypeComboBox => cmbSplitSingleComicFolderType;
        internal CheckBox MergeRemainderFolderCheckBox => chkMergeRemainderFolder;
        internal TextBox MergeSingleComicRootTextBox => txtSplitSingleComicRoot;
        internal TextBox AlphabetSplitRootTextBox => txtAlphabetSplitRoot;
        internal CheckBox AlphabetIgnoreLeadingTagsCheckBox => chkAlphabetIgnoreLeadingTags;
        internal WrapPanel AlphabetRangesPanel => pnlAlphabetRanges;

        private MainWindow GetOwnerWindow()
        {
            return Window.GetWindow(this) as MainWindow;
        }

        private void SingleComicFolderToolsView_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_isRangesLoaded)
            {
                LoadAlphabetRanges();
                _isRangesLoaded = true;
            }

            var owner = GetOwnerWindow();
            if (owner != null)
            {
                ApplyLanguage(owner._isVietnameseUi);
            }
        }

        internal void ApplyLanguage(bool isVietnamese)
        {
            if (txtPanel1Header != null)
                txtPanel1Header.Text = isVietnamese ? "TÁCH / GỘP THƯ MỤC THEO SỐ CHƯƠNG" : "SPLIT / MERGE FOLDERS BY CHAPTER COUNT";

            if (txtPanel1Desc != null)
                txtPanel1Desc.Text = isVietnamese
                    ? "Chia book\\chapter thành folder con theo khoảng chap tròn, hoặc gộp chapter trong các subfolder về folder đã chọn."
                    : "Split book\\chapters into subfolders by chapter count, or merge chapters back into the selected root folder.";

            if (lblPanel1Root != null)
                lblPanel1Root.Text = isVietnamese ? "THƯ MỤC GỐC" : "ROOT FOLDER";

            if (btnBrowseSplitSingleComicRoot != null)
                btnBrowseSplitSingleComicRoot.Content = isVietnamese ? "CHỌN" : "BROWSE";

            if (lblPanel1Group != null)
                lblPanel1Group.Text = isVietnamese ? "NHÓM CHAP" : "CHAP GROUP";

            if (btnSplitSingleComicFolders != null)
                btnSplitSingleComicFolders.Content = isVietnamese ? "TÁCH THEO CHAP" : "SPLIT BY CHAPTERS";

            if (btnMergeSingleComicFolders != null)
                btnMergeSingleComicFolders.Content = isVietnamese ? "GỘP CHAPTER" : "MERGE CHAPTERS";

            if (chkMergeRemainderFolder != null)
            {
                chkMergeRemainderFolder.Content = isVietnamese
                    ? "Gộp folder thừa vào folder trước đó (Merge remainder into previous folder)"
                    : "Merge remainder into previous folder";
                chkMergeRemainderFolder.ToolTip = isVietnamese
                    ? "Khi nhóm chapter cuối cùng không đủ số lượng chap quy định, tự động gộp vào nhóm liền kề trước đó thay vì tạo folder riêng."
                    : "When the last chapter group does not have enough chapters, automatically merge it into the previous group.";
            }

            if (txtPanel2Header != null)
                txtPanel2Header.Text = isVietnamese ? "TÁCH / GỘP THƯ MỤC THEO ALPHABET" : "SPLIT / MERGE FOLDERS BY ALPHABET";

            if (txtPanel2Desc != null)
                txtPanel2Desc.Text = isVietnamese
                    ? "Chia các thư mục trong folder đã chọn vào các thư mục con theo chữ cái đầu (A-G, H-P, Q-Z...). Bắt đầu bằng số -> [Number], ngôn ngữ khác Latin -> other language; hoặc gộp tất cả về thư mục gốc."
                    : "Split folders by initial letters (A-G, H-P, Q-Z...), number -> [Number], non-Latin -> other language; or merge all back to root folder.";

            if (lblPanel2Root != null)
                lblPanel2Root.Text = isVietnamese ? "THƯ MỤC GỐC" : "ROOT FOLDER";

            if (btnBrowseAlphabetSplitRoot != null)
                btnBrowseAlphabetSplitRoot.Content = isVietnamese ? "CHỌN" : "BROWSE";

            if (lblPanel2Ranges != null)
                lblPanel2Ranges.Text = isVietnamese ? "KHOẢNG CHỮ" : "RANGES";

            if (btnAddAlphabetRange != null)
            {
                btnAddAlphabetRange.Content = isVietnamese ? "+ THÊM" : "+ ADD";
                btnAddAlphabetRange.ToolTip = isVietnamese ? "Thêm khoảng chữ cái mới (ví dụ: A-G)" : "Add new range (e.g. A-G)";
            }

            if (btnResetAlphabetRanges != null)
            {
                btnResetAlphabetRanges.Content = isVietnamese ? "MẶC ĐỊNH" : "RESET";
                btnResetAlphabetRanges.ToolTip = isVietnamese ? "Khôi phục mặc định: A-G, H-P, Q-Z" : "Reset default ranges: A-G, H-P, Q-Z";
            }

            if (chkAlphabetIgnoreLeadingTags != null)
            {
                chkAlphabetIgnoreLeadingTags.Content = isVietnamese
                    ? "Bỏ qua tag tiền tố [..] hoặc (..) khi lấy chữ cái đầu (Ignore leading bracket tags)"
                    : "Ignore leading bracket tags like [Artist] Title when extracting initial letter";
                chkAlphabetIgnoreLeadingTags.ToolTip = isVietnamese
                    ? "Nếu thư mục có dạng '[Nhóm] Tên' hoặc '(C100) Tên', sẽ lấy chữ cái đầu của Tên thay vì lấy từ trong ngoặc."
                    : "If folder has format like '[Group] Name' or '(C100) Name', use initial letter of Name instead of tag.";
            }

            if (btnSplitFoldersByAlphabet != null)
                btnSplitFoldersByAlphabet.Content = isVietnamese ? "TÁCH THEO CHỮ CÁI" : "SPLIT BY ALPHABET";

            if (btnMergeFoldersByAlphabet != null)
                btnMergeFoldersByAlphabet.Content = isVietnamese ? "GỘP THEO CHỮ CÁI" : "MERGE BY ALPHABET";
        }

        private string GetAlphabetRangesConfigPath()
        {
            try
            {
                string dataRoot = PortablePaths.PortableDataRoot;
                if (!Directory.Exists(dataRoot))
                {
                    Directory.CreateDirectory(dataRoot);
                }
                return Path.Combine(dataRoot, "alphabet-split-ranges.txt");
            }
            catch
            {
                return "alphabet-split-ranges.txt";
            }
        }

        internal void LoadAlphabetRanges()
        {
            pnlAlphabetRanges.Children.Clear();
            string configPath = GetAlphabetRangesConfigPath();

            List<string> ranges = new List<string>();
            try
            {
                if (File.Exists(configPath))
                {
                    string content = File.ReadAllText(configPath);
                    string[] tokens = content.Split(new[] { ',', '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string token in tokens)
                    {
                        string trimmed = token.Trim().ToUpperInvariant();
                        if (!string.IsNullOrEmpty(trimmed) && !ranges.Contains(trimmed))
                        {
                            ranges.Add(trimmed);
                        }
                    }
                }
            }
            catch
            {
                // Fallback mặc định nếu đọc file lỗi
            }

            if (ranges.Count == 0)
            {
                ranges.Add("A-G");
                ranges.Add("H-P");
                ranges.Add("Q-Z");
            }

            foreach (string range in ranges)
            {
                AddRangeBadge(range, saveConfig: false);
            }
        }

        internal void SaveAlphabetRangesConfig()
        {
            try
            {
                var ranges = GetAlphabetRanges();
                string configPath = GetAlphabetRangesConfigPath();
                File.WriteAllText(configPath, string.Join(Environment.NewLine, ranges));
            }
            catch
            {
                // Tránh throw exception làm crash app khi ghi config
            }
        }

        internal List<string> GetAlphabetRanges()
        {
            var ranges = new List<string>();
            foreach (UIElement child in pnlAlphabetRanges.Children)
            {
                if (child is Border border && border.Child is StackPanel stack)
                {
                    TextBox txt = stack.Children.OfType<TextBox>().FirstOrDefault();
                    if (txt != null)
                    {
                        string val = txt.Text?.Trim()?.ToUpperInvariant() ?? "";
                        if (!string.IsNullOrEmpty(val) && !ranges.Contains(val))
                        {
                            ranges.Add(val);
                        }
                    }
                }
            }
            return ranges;
        }

        private void AddRangeBadge(string rangeText, bool saveConfig = true)
        {
            var badgeBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x11, 0x16, 0x22)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x25, 0x36, 0x4E)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 8, 6),
                Padding = new Thickness(4, 2, 4, 2)
            };

            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var txt = new TextBox
            {
                Text = rangeText?.Trim()?.ToUpperInvariant() ?? "",
                Width = 54,
                Height = 22,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Application.Current.TryFindResource("CyberpunkCyanBrush") ?? Brushes.Cyan,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                TextAlignment = TextAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                CharacterCasing = CharacterCasing.Upper
            };

            txt.TextChanged += (s, e) => SaveAlphabetRangesConfig();
            txt.LostFocus += (s, e) => SaveAlphabetRangesConfig();

            var btnRemove = new Button
            {
                Content = "—",
                Width = 20,
                Height = 20,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(0),
                Margin = new Thickness(4, 0, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Xóa khoảng này (Remove range)"
            };

            if (Application.Current.TryFindResource("CompactPinkButton") is Style pinkStyle)
            {
                btnRemove.Style = pinkStyle;
            }

            btnRemove.Click += (s, e) =>
            {
                pnlAlphabetRanges.Children.Remove(badgeBorder);
                SaveAlphabetRangesConfig();
            };

            stack.Children.Add(txt);
            stack.Children.Add(btnRemove);
            badgeBorder.Child = stack;

            pnlAlphabetRanges.Children.Add(badgeBorder);

            if (saveConfig)
            {
                SaveAlphabetRangesConfig();
            }
        }

        private void BtnAddAlphabetRange_Click(object sender, RoutedEventArgs e)
        {
            AddRangeBadge("", saveConfig: true);
        }

        private void BtnResetAlphabetRanges_Click(object sender, RoutedEventArgs e)
        {
            pnlAlphabetRanges.Children.Clear();
            AddRangeBadge("A-G", saveConfig: false);
            AddRangeBadge("H-P", saveConfig: false);
            AddRangeBadge("Q-Z", saveConfig: true);
        }

        private void BtnBrowseSplitSingleComicRoot_Click(object sender, RoutedEventArgs e)
        {
            GetOwnerWindow()?.BtnBrowseSplitSingleComicRoot_Click(sender, e);
        }

        private void BtnSplitSingleComicFolders_Click(object sender, RoutedEventArgs e)
        {
            GetOwnerWindow()?.BtnSplitSingleComicFolders_Click(sender, e);
        }

        private void BtnBrowseMergeSingleComicRoot_Click(object sender, RoutedEventArgs e)
        {
            GetOwnerWindow()?.BtnBrowseMergeSingleComicRoot_Click(sender, e);
        }

        private void BtnMergeSingleComicFolders_Click(object sender, RoutedEventArgs e)
        {
            GetOwnerWindow()?.BtnMergeSingleComicFolders_Click(sender, e);
        }

        private void BtnBrowseAlphabetSplitRoot_Click(object sender, RoutedEventArgs e)
        {
            GetOwnerWindow()?.BtnBrowseAlphabetSplitRoot_Click(sender, e);
        }

        private void BtnSplitFoldersByAlphabet_Click(object sender, RoutedEventArgs e)
        {
            GetOwnerWindow()?.BtnSplitFoldersByAlphabet_Click(sender, e);
        }

        private void BtnMergeFoldersByAlphabet_Click(object sender, RoutedEventArgs e)
        {
            GetOwnerWindow()?.BtnMergeFoldersByAlphabet_Click(sender, e);
        }
    }
}

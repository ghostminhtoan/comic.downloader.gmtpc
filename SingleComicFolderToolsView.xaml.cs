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
        internal TextBox MergeSingleComicRootTextBox => txtMergeSingleComicRoot;
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
    }
}

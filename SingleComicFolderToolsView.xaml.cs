using System.Windows;
using System.Windows.Controls;

namespace get_link_manga
{
    public partial class SingleComicFolderToolsView : UserControl
    {
        public SingleComicFolderToolsView()
        {
            InitializeComponent();
        }

        internal TextBox SplitSingleComicRootTextBox => txtSplitSingleComicRoot;
        internal ComboBox SplitChapterGroupSizeComboBox => cmbSplitChapterGroupSize;
        internal ComboBox SplitSingleComicFolderTypeComboBox => cmbSplitSingleComicFolderType;
        internal TextBox MergeSingleComicRootTextBox => txtMergeSingleComicRoot;

        private MainWindow GetOwnerWindow()
        {
            return Window.GetWindow(this) as MainWindow;
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
    }
}

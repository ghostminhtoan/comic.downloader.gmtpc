using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;

namespace get_link_manga
{
    public partial class LightNovelPreviewPanel : UserControl
    {
        public LightNovelPreviewPanel()
        {
            InitializeComponent();
        }

        internal DataGrid LightNovelBooksGrid => dgLightNovelBooks;
        internal ListBox LightNovelChaptersList => lbLightNovelChapters;
        internal TextBox LightNovelSelectedChapterTextBox => txtLightNovelSelectedChapter;
        internal TextBox LightNovelPlainTextTextBox => txtLightNovelPlainText;
        internal TextBox LightNovelMarkdownTextBox => txtLightNovelMarkdown;
        internal ToggleButton StartCopyTextToggleButton => btnStartCopyText;

        private MainWindow GetOwnerWindow()
        {
            return Window.GetWindow(this) as MainWindow;
        }

        private void DgLightNovelBooks_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            GetOwnerWindow()?.DgLightNovelBooks_SelectionChanged(sender, e);
        }

        private void DgLightNovelBooks_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            GetOwnerWindow()?.DgLightNovelBooks_PreviewKeyDown(sender, e);
        }

        private void LbLightNovelChapters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            GetOwnerWindow()?.LbLightNovelChapters_SelectionChanged(sender, e);
        }

        private void LbLightNovelChapters_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            GetOwnerWindow()?.LbLightNovelChapters_PreviewKeyDown(sender, e);
        }

        private void BtnStartLightNovelCopyToggle_Click(object sender, RoutedEventArgs e)
        {
            GetOwnerWindow()?.BtnStartLightNovelCopyToggle_Click(sender, e);
        }
    }
}

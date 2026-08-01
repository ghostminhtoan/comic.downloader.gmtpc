namespace get_link_manga
{
    public partial class MainWindow
    {
        private void ApplyBuildInfoText()
        {
            if (txtBuildInfo == null)
            {
                RefreshUpdateSectionContent();
                return;
            }

            txtBuildInfo.Text = BuildInfo.DisplayText;
            RefreshUpdateSectionContent();
        }
    }
}

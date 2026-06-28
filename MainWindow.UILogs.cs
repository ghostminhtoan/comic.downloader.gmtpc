using System;
using System.Windows;
using System.Windows.Controls;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private bool IsErrorMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return false;
            }

            string lower = message.ToLowerInvariant();
            return lower.Contains("lỗi") ||
                   lower.Contains("error") ||
                   lower.Contains("exception") ||
                   lower.Contains("failed") ||
                   lower.Contains("timeout") ||
                   lower.Contains("forbidden") ||
                   lower.Contains("too many request") ||
                   lower.Contains("thất bại") ||
                   lower.Contains("không thể") ||
                   lower.Contains("403") ||
                   lower.Contains("503") ||
                   lower.Contains("429");
        }

        private void AppendLogLine(RichTextBox rtb, string text, bool isError)
        {
            AppendLogLineWithFilter(rtb, text, isError);
        }

        internal void Log(string message)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                string logLine = $"[{DateTime.Now:HH:mm:ss}] {message}\r\n";
                bool isError = IsErrorMessage(message);
                if (txtLog != null)
                {
                    AppendLogLine(txtLog, logLine, isError);
                    if (chkAutoScrollLog?.IsChecked == true)
                    {
                        ScrollTextBoxToEnd(txtLog);
                    }
                }

                if (txtNhentaiLog != null)
                {
                    AppendLogLine(txtNhentaiLog, logLine, isError);
                    if (chkAutoScrollNhentaiLog?.IsChecked == true)
                    {
                        ScrollTextBoxToEnd(txtNhentaiLog);
                    }
                }

                if (txtTruyenqqLog != null)
                {
                    AppendLogLine(txtTruyenqqLog, logLine, isError);
                    if (chkAutoScrollTruyenqqLog?.IsChecked == true)
                    {
                        ScrollTextBoxToEnd(txtTruyenqqLog);
                    }
                }

                if (txtNettruyenLog != null)
                {
                    AppendLogLine(txtNettruyenLog, logLine, isError);
                    if (chkAutoScrollNettruyenLog?.IsChecked == true)
                    {
                        ScrollTextBoxToEnd(txtNettruyenLog);
                    }
                }

                if (txtHakoLog != null)
                {
                    AppendLogLine(txtHakoLog, logLine, isError);
                }

                if (txtTruyenggvnLog != null)
                {
                    AppendLogLine(txtTruyenggvnLog, logLine, isError);
                    if (chkAutoScrollTruyenggvnLog?.IsChecked == true)
                    {
                        ScrollTextBoxToEnd(txtTruyenggvnLog);
                    }
                }

                if (txtHentaieraLog != null)
                {
                    AppendLogLine(txtHentaieraLog, logLine, isError);
                    if (chkAutoScrollHentaieraLog?.IsChecked == true)
                    {
                        ScrollTextBoxToEnd(txtHentaieraLog);
                    }
                }

                if (txtHentai2readLog != null)
                {
                    AppendLogLine(txtHentai2readLog, logLine, isError);
                    if (chkAutoScrollHentai2readLog?.IsChecked == true)
                    {
                        ScrollTextBoxToEnd(txtHentai2readLog);
                    }
                }

                if (isError)
                {
                    RecordCheckError("GENERAL", "-", "-", 0, message, null);
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            ClearLogPanel(txtLog);
        }

        private void BtnClearCheckErrors_Click(object sender, RoutedEventArgs e)
        {
            ClearCheckErrors();
        }

        private void BtnClearNhentaiLog_Click(object sender, RoutedEventArgs e)
        {
            ClearLogPanel(txtNhentaiLog);
        }

        private void BtnClearViHentaiLog_Click(object sender, RoutedEventArgs e)
        {
            ClearLogPanel(txtViHentaiLog);
        }

        private void BtnClearTruyenqqLog_Click(object sender, RoutedEventArgs e)
        {
            ClearLogPanel(txtTruyenqqLog);
        }

        private void BtnClearNettruyenLog_Click(object sender, RoutedEventArgs e)
        {
            ClearLogPanel(txtNettruyenLog);
        }

        private void BtnClearTruyenggvnLog_Click(object sender, RoutedEventArgs e)
        {
            ClearLogPanel(txtTruyenggvnLog);
        }

        private void BtnClearHentaieraLog_Click(object sender, RoutedEventArgs e)
        {
            ClearLogPanel(txtHentaieraLog);
        }

        private void BtnClearHentai2readLog_Click(object sender, RoutedEventArgs e)
        {
            ClearLogPanel(txtHentai2readLog);
        }
    }
}

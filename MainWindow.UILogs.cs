using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private enum LogSeverity
        {
            Trace,
            Info,
            Warning,
            Error
        }

        private static readonly object _portableLogFileLock = new object();

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
            Log(message, null, null);
        }

        private void Log(string message, LogSeverity? severity, string source)
        {
            LogSeverity effectiveSeverity = severity ?? InferLogSeverity(message);
            string logLine = BuildLogLine(message, effectiveSeverity, source);
            WritePortableLogLine(logLine);

            Dispatcher.BeginInvoke(new Action(() =>
            {
                bool isError = effectiveSeverity == LogSeverity.Error;
                
                // ponytail: skip progress logs during active downloads to prevent GUI lag/crash
                if (_downloadCts != null && !isError)
                {
                    string lowerMsg = (message ?? string.Empty).ToLowerInvariant();
                    bool isFinalStatus = lowerMsg.Contains("hoàn thành") || 
                                         lowerMsg.Contains("done") || 
                                         lowerMsg.Contains("thành công") || 
                                         lowerMsg.Contains("hủy") || 
                                         lowerMsg.Contains("cancel") || 
                                         lowerMsg.Contains("stop") ||
                                         lowerMsg.Contains("bắt đầu") ||
                                         lowerMsg.Contains("start");
                    if (!isFinalStatus)
                    {
                        return;
                    }
                }
                if (txtLog != null)
                {
                    AppendLogLine(txtLog, logLine, isError);
                    AppendMarkdownLogCard(logLine, effectiveSeverity);
                    if (chkAutoScrollLog?.IsChecked == true)
                    {
                        ScrollTextBoxToEnd(txtLog);
                        _scrollLogHost?.ScrollToBottom();
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

                if (txtNettruyenTechLog != null)
                {
                    AppendLogLine(txtNettruyenTechLog, logLine, isError);
                    if (chkAutoScrollNettruyenTechLog?.IsChecked == true)
                    {
                        ScrollTextBoxToEnd(txtNettruyenTechLog);
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

                if (txtLog != null)
                {
                    AppendLogLine(txtLog, logLine, isError);
                    if (chkAutoScrollLog?.IsChecked == true)
                    {
                        ScrollTextBoxToEnd(txtLog);
                    }
                }

                if (isError)
                {
                    RecordCheckError("GENERAL", "-", "-", 0, message, null);
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private static string BuildLogLine(string message, LogSeverity severity, string source)
        {
            string level = severity.ToString().ToUpperInvariant();
            string sourceTag = string.IsNullOrWhiteSpace(source)
                ? string.Empty
                : $"[{source.Trim()}] ";
            return $"[{DateTime.Now:HH:mm:ss}] [{level}] {sourceTag}{message}\r\n";
        }

        private LogSeverity InferLogSeverity(string message)
        {
            if (IsErrorMessage(message))
            {
                return LogSeverity.Error;
            }

            string lower = (message ?? string.Empty).ToLowerInvariant();
            if (lower.Contains("warn") ||
                lower.Contains("retry") ||
                lower.Contains("fallback") ||
                lower.Contains("cảnh báo"))
            {
                return LogSeverity.Warning;
            }

            if (lower.Contains("trace") || lower.Contains("debug"))
            {
                return LogSeverity.Trace;
            }

            return LogSeverity.Info;
        }

        private static string GetPortableLogFilePath()
        {
            return Path.Combine(
                PortablePaths.PortableDataRoot,
                "logs",
                $"{DateTime.Now:yyyy-MM-dd}.log");
        }

        private static void WritePortableLogLine(string logLine)
        {
            if (string.IsNullOrWhiteSpace(logLine))
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string logPath = GetPortableLogFilePath();
                    Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                    lock (_portableLogFileLock)
                    {
                        File.AppendAllText(logPath, logLine);
                    }
                }
                catch
                {
                }
            });
        }

        private void AppendMarkdownLogCard(string logLine, LogSeverity severity)
        {
            if (_mdLogStackPanel == null || string.IsNullOrWhiteSpace(logLine))
            {
                return;
            }

            bool isError = severity == LogSeverity.Error;
            bool isWarning = severity == LogSeverity.Warning;
            bool isTrace = severity == LogSeverity.Trace;

            var cardBorder = new Border
            {
                Margin = new Thickness(0, 2, 0, 2),
                Padding = new Thickness(8, 4, 8, 4),
                CornerRadius = new CornerRadius(4),
                Background = isError
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x28, 0xff, 0x17, 0x44))
                    : (isWarning
                        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x20, 0xff, 0xea, 0x00))
                        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x14, 0x0e, 0x1d, 0x2e))),
                BorderBrush = isError
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xff, 0x17, 0x44))
                    : (isWarning
                        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xff, 0xea, 0x00))
                        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x30, 0x00, 0xb0, 0xff))),
                BorderThickness = new Thickness(1),
                Tag = isError
            };

            if (chkErrorOnlyLog?.IsChecked == true && !isError)
            {
                cardBorder.Visibility = Visibility.Collapsed;
            }

            var textBlock = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new System.Windows.Media.FontFamily("Consolas, Segoe UI, sans-serif"),
                FontSize = 11.5
            };

            // Format timestamp, level badge, and text
            string raw = logLine.TrimEnd('\r', '\n');
            int levelEndPos = raw.IndexOf(']');
            if (levelEndPos > 0 && raw.StartsWith("["))
            {
                string timeStamp = raw.Substring(0, levelEndPos + 1);
                string rest = raw.Substring(levelEndPos + 1).TrimStart();

                textBlock.Inlines.Add(new System.Windows.Documents.Run(timeStamp + " ")
                {
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x7c, 0xe8, 0xff)),
                    FontWeight = FontWeights.Bold
                });

                if (rest.StartsWith("["))
                {
                    int tagEnd = rest.IndexOf(']');
                    if (tagEnd > 0)
                    {
                        string badge = rest.Substring(0, tagEnd + 1);
                        string body = rest.Substring(tagEnd + 1);

                        var badgeBrush = isError
                            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xff, 0x52, 0x52))
                            : (isWarning
                                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xff, 0xd7, 0x40))
                                : (isTrace
                                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x40, 0xc4, 0xff))
                                    : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x69, 0xf0, 0xae))));

                        textBlock.Inlines.Add(new System.Windows.Documents.Run(badge + " ")
                        {
                            Foreground = badgeBrush,
                            FontWeight = FontWeights.Bold
                        });

                        textBlock.Inlines.Add(new System.Windows.Documents.Run(body)
                        {
                            Foreground = isError
                                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xff, 0x8a, 0x80))
                                : (isWarning
                                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xff, 0xe0, 0x82))
                                    : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xd1, 0xd5, 0xdb)))
                        });
                    }
                    else
                    {
                        textBlock.Inlines.Add(new System.Windows.Documents.Run(rest)
                        {
                            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xd1, 0xd5, 0xdb))
                        });
                    }
                }
                else
                {
                    textBlock.Inlines.Add(new System.Windows.Documents.Run(rest)
                    {
                        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xd1, 0xd5, 0xdb))
                    });
                }
            }
            else
            {
                textBlock.Inlines.Add(new System.Windows.Documents.Run(raw)
                {
                    Foreground = isError
                        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xff, 0x8a, 0x80))
                        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xd1, 0xd5, 0xdb))
                });
            }

            cardBorder.Child = textBlock;
            _mdLogStackPanel.Children.Add(cardBorder);

            // Limit max rows to 500
            while (_mdLogStackPanel.Children.Count > 500)
            {
                _mdLogStackPanel.Children.RemoveAt(0);
            }
        }

        private void ApplyLogFilter()
        {
            if (_mdLogStackPanel == null)
            {
                return;
            }

            bool errorOnly = chkErrorOnlyLog?.IsChecked == true;
            foreach (UIElement child in _mdLogStackPanel.Children)
            {
                if (child is Border b)
                {
                    bool isError = b.Tag is bool && (bool)b.Tag;
                    b.Visibility = (errorOnly && !isError) ? Visibility.Collapsed : Visibility.Visible;
                }
            }
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            ClearLogPanel(txtLog);
            _mdLogStackPanel?.Children.Clear();
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

        private void BtnClearNettruyenTechLog_Click(object sender, RoutedEventArgs e)
        {
            ClearLogPanel(txtNettruyenTechLog);
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
            ClearLogPanel(txtLog);
        }
    }
}

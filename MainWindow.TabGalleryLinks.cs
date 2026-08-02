using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.ComponentModel;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private readonly Dictionary<string, List<ReaderChapterItem>> _downloadChapterItemCache = new Dictionary<string, List<ReaderChapterItem>>(StringComparer.OrdinalIgnoreCase);
        private readonly ObservableCollection<ReaderChapterIssueItem> _downloadMissingChapterRows = new ObservableCollection<ReaderChapterIssueItem>();
        private TabItem _downloadMissingChapterTab;
        private DataGrid _downloadMissingChapterGrid;
        private TextBlock _downloadMissingChapterStatusText;
        private Button _downloadMissingChapterCheckButton;
        private TextBlock _downloadMissingChapterParallelLabel;
        private ComboBox _downloadMissingChapterParallelComboBox;
        private TextBlock _downloadMissingChapterCpuAffinityLabel;
        private ComboBox _downloadMissingChapterCpuAffinityComboBox;
        private TextBlock _downloadMissingChapterCpuPriorityLabel;
        private ComboBox _downloadMissingChapterCpuPriorityComboBox;
        private Button _downloadMissingChapterStopButton;
        private Button _downloadMissingChapterRescanButton;
        private Button _downloadMissingChapterPauseButton;
        private Button _downloadMissingChapterOpenCacheButton;
        private Button _downloadMissingChapterClearCacheButton;
        private Button _downloadMissingChapterCopyButton;
        private Button _downloadMissingChapterCopyAllButton;
        private Button _downloadMissingChapterClearButton;
        private ToggleButton _downloadMissingChapterDecimalWrapToggle;
        private TextBlock _downloadMissingChapterDecimalHeaderText;
        private bool _downloadMissingChapterDecimalWrapEnabled;
        private CancellationTokenSource _downloadMissingChapterScanCts;
        private bool _downloadMissingChapterScanInProgress;
        private bool _downloadMissingChapterScanPaused;
        private DispatcherTimer _downloadMissingChapterAutoScanTimer;
        private DispatcherTimer _downloadMissingChapterRescanTimer;
        private DispatcherTimer _downloadMissingChapterRowSyncTimer;
        private string _downloadMissingChapterSearchBuffer = string.Empty;
        private DateTime _downloadMissingChapterLastKeyPressTime = DateTime.MinValue;
        private int _downloadMissingChapterLastProgressUiTick;
        private readonly HashSet<string> _downloadMissingChapterPendingRescanKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _downloadMissingChapterManualSortActive;
        internal bool _downloadMissingChapterBulkRefreshing;
        private bool _downloadMissingChapterPendingForceOrderSync;
        private bool _downloadMissingChapterRestartAfterCurrentScan;

        private sealed class DownloadMissingChapterScanResult
        {
            public GalleryItem Item { get; set; }
            public int RowNumber { get; set; }
            public List<ReaderChapterItem> ChapterItems { get; set; }
            public ReaderChapterIssueItem SummaryRow { get; set; }
            public bool TimedOut { get; set; }
            public int Attempt { get; set; }
        }

        private sealed class DownloadMissingChapterScanWork
        {
            public GalleryItem Item { get; set; }
            public int RowNumber { get; set; }
            public int Attempt { get; set; }
            public string Domain { get; set; }
            public CancellationTokenSource Cts { get; set; }
            public Task<DownloadMissingChapterScanResult> Task { get; set; }
        }

        private int GetDownloadMangaTabIndex()
        {
            return 0;
        }

        private int GetDownloadMissingChapterTabIndex()
        {
            if (tabDownloadRoot == null || _downloadMissingChapterTab == null)
            {
                return -1;
            }

            return tabDownloadRoot.Items.IndexOf(_downloadMissingChapterTab);
        }

        private int GetDownloadNovelTabIndex()
        {
            if (tabDownloadRoot == null || tabDownloadRoot.Items.Count == 0)
            {
                return 0;
            }

            if (_downloadMissingChapterTab != null)
            {
                int missingIndex = tabDownloadRoot.Items.IndexOf(_downloadMissingChapterTab);
                if (missingIndex >= 0 && missingIndex + 1 < tabDownloadRoot.Items.Count)
                {
                    return missingIndex + 1;
                }
            }

            return Math.Min(1, tabDownloadRoot.Items.Count - 1);
        }

        private string GetDownloadMissingChapterCachePath(string listPath)
        {
            string fallbackDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "autosave");
            string directory = Path.GetDirectoryName(listPath ?? string.Empty);
            if (string.IsNullOrWhiteSpace(directory))
            {
                directory = fallbackDir;
            }

            string fileName = Path.GetFileNameWithoutExtension(listPath ?? string.Empty);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "gallery-list-autosave";
            }

            return Path.Combine(directory, fileName + ".missing-chapter-cache.json");
        }

        private void InitializeDownloadMissingChapterTab()
        {
            if (tabDownloadRoot == null || _downloadMissingChapterTab != null || tabDownloadRoot.Items.Count < 2)
            {
                return;
            }

            _downloadMissingChapterGrid = CreateDownloadMissingChapterIssueGrid();
            _downloadMissingChapterGrid.ItemsSource = _downloadMissingChapterRows;

            _downloadMissingChapterStatusText = new TextBlock
            {
                Foreground = (Brush)TryFindResource("CyberpunkTextBrush") ?? Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };

            _downloadMissingChapterCheckButton = new Button
            {
                MinWidth = 132,
                Padding = new Thickness(10, 2, 10, 2),
                FontWeight = FontWeights.Bold,
                Style = TryFindResource("CompactCyanButton") as Style
            };
            _downloadMissingChapterCheckButton.Click += async (s, e) => await ScanDownloadMissingChaptersAsync(forceRefresh: false);

            _downloadMissingChapterParallelComboBox = CreateDownloadMissingChapterParallelComboBox();

            _downloadMissingChapterStopButton = new Button
            {
                MinWidth = 82,
                Padding = new Thickness(10, 2, 10, 2),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(4, 0, 0, 0),
                Style = TryFindResource("CompactPinkButton") as Style,
                IsEnabled = false
            };
            _downloadMissingChapterStopButton.Click += BtnStopDownloadMissingChapters_Click;

            _downloadMissingChapterRescanButton = new Button
            {
                MinWidth = 160,
                Padding = new Thickness(10, 2, 10, 2),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(4, 0, 0, 0),
                Style = TryFindResource("CompactCyanButton") as Style
            };
            _downloadMissingChapterRescanButton.Click += BtnRescanDownloadMissingChapters_Click;

            _downloadMissingChapterPauseButton = new Button
            {
                MinWidth = 178,
                Padding = new Thickness(10, 2, 10, 2),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(4, 0, 0, 0),
                Style = TryFindResource("CompactPinkButton") as Style
            };
            _downloadMissingChapterPauseButton.Click += BtnPauseResumeDownloadMissingChapters_Click;

            _downloadMissingChapterOpenCacheButton = new Button
            {
                MinWidth = 168,
                Padding = new Thickness(10, 2, 10, 2),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(4, 0, 0, 0),
                Style = TryFindResource("CompactCyanButton") as Style
            };
            _downloadMissingChapterOpenCacheButton.Click += BtnOpenDownloadMissingChapterCache_Click;

            _downloadMissingChapterClearCacheButton = new Button
            {
                MinWidth = 170,
                Padding = new Thickness(10, 2, 10, 2),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(4, 0, 0, 0),
                Style = TryFindResource("CompactPinkButton") as Style
            };
            _downloadMissingChapterClearCacheButton.Click += BtnClearDownloadMissingChapterCache_Click;

            _downloadMissingChapterCopyButton = new Button
            {
                MinWidth = 150,
                Padding = new Thickness(10, 2, 10, 2),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(4, 0, 0, 0),
                Style = TryFindResource("CompactPinkButton") as Style
            };
            _downloadMissingChapterCopyButton.Click += BtnCopyDownloadMissingChapters_Click;

            _downloadMissingChapterCopyAllButton = new Button
            {
                MinWidth = 228,
                Padding = new Thickness(10, 2, 10, 2),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(4, 0, 0, 0),
                Style = TryFindResource("CompactPinkButton") as Style
            };
            _downloadMissingChapterCopyAllButton.Click += BtnCopyAllDownloadMissingChapters_Click;

            _downloadMissingChapterClearButton = new Button
            {
                MinWidth = 82,
                Padding = new Thickness(10, 2, 10, 2),
                FontWeight = FontWeights.Bold,
                Style = TryFindResource("CompactCyanButton") as Style
            };
            _downloadMissingChapterClearButton.Click += BtnClearDownloadMissingChapters_Click;

            var rootGrid = new Grid();
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var toolbar = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 12)
            };
            toolbar.Children.Add(_downloadMissingChapterCheckButton);
            toolbar.Children.Add(CreateDownloadMissingChapterParallelPanel());
            toolbar.Children.Add(_downloadMissingChapterStopButton);
            toolbar.Children.Add(_downloadMissingChapterRescanButton);
            toolbar.Children.Add(_downloadMissingChapterPauseButton);
            toolbar.Children.Add(_downloadMissingChapterOpenCacheButton);
            toolbar.Children.Add(_downloadMissingChapterClearCacheButton);
            toolbar.Children.Add(_downloadMissingChapterCopyButton);
            toolbar.Children.Add(_downloadMissingChapterClearButton);
            toolbar.Children.Add(_downloadMissingChapterCopyAllButton);
            Grid.SetRow(toolbar, 0);
            rootGrid.Children.Add(toolbar);

            var statusBorder = new Border
            {
                Style = TryFindResource("WorkspaceSectionCard") as Style,
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 12),
                Child = _downloadMissingChapterStatusText
            };
            Grid.SetRow(statusBorder, 1);
            rootGrid.Children.Add(statusBorder);

            Grid.SetRow(_downloadMissingChapterGrid, 2);
            rootGrid.Children.Add(_downloadMissingChapterGrid);

            _downloadMissingChapterTab = new TabItem
            {
                Style = TryFindResource("CyberpunkTabItem") as Style,
                Tag = TryFindResource("CyberpunkCyanBrush"),
                Content = rootGrid
            };

            tabDownloadRoot.Items.Insert(1, _downloadMissingChapterTab);
            UpdateDownloadMissingChapterLanguage();
        }

        private ComboBox CreateDownloadMissingChapterParallelComboBox()
        {
            var comboBox = new ComboBox
            {
                Style = TryFindResource("CyberpunkComboBox") as Style,
                Height = 26,
                Width = 62,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                SelectedIndex = 7
            };
            comboBox.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
            comboBox.ItemContainerStyle = TryFindResource("CyberpunkComboBoxItemStyle") as Style;
            for (int i = 1; i <= 16; i++)
            {
                comboBox.Items.Add(new ComboBoxItem { Content = i.ToString() });
            }
            return comboBox;
        }

        private FrameworkElement CreateDownloadMissingChapterParallelPanel()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _downloadMissingChapterParallelLabel = new TextBlock
            {
                Text = "MULTIPLE CHECK",
                Style = TryFindResource("InputLabelStyle") as Style,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            panel.Children.Add(_downloadMissingChapterParallelLabel);
            panel.Children.Add(_downloadMissingChapterParallelComboBox);

            // CPU Affinity
            _downloadMissingChapterCpuAffinityLabel = new TextBlock
            {
                Text = "CPU AFFINITY",
                Style = TryFindResource("InputLabelStyle") as Style,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 6, 0)
            };
            panel.Children.Add(_downloadMissingChapterCpuAffinityLabel);

            _downloadMissingChapterCpuAffinityComboBox = new ComboBox
            {
                Style = TryFindResource("CyberpunkComboBox") as Style,
                Height = 26,
                Width = 62,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            _downloadMissingChapterCpuAffinityComboBox.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
            _downloadMissingChapterCpuAffinityComboBox.ItemContainerStyle = TryFindResource("CyberpunkComboBoxItemStyle") as Style;
            _downloadMissingChapterCpuAffinityComboBox.Items.Add("All");
            int maxCores = Environment.ProcessorCount;
            for (int i = 1; i <= maxCores; i++)
            {
                _downloadMissingChapterCpuAffinityComboBox.Items.Add(i.ToString());
            }
            _downloadMissingChapterCpuAffinityComboBox.SelectedIndex = Math.Min(2, maxCores);
            _downloadMissingChapterCpuAffinityComboBox.SelectionChanged += CmbCpuAffinity_SelectionChanged;
            panel.Children.Add(_downloadMissingChapterCpuAffinityComboBox);

            // CPU Priority
            _downloadMissingChapterCpuPriorityLabel = new TextBlock
            {
                Text = "CPU PRIORITY",
                Style = TryFindResource("InputLabelStyle") as Style,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 6, 0)
            };
            panel.Children.Add(_downloadMissingChapterCpuPriorityLabel);

            _downloadMissingChapterCpuPriorityComboBox = new ComboBox
            {
                Style = TryFindResource("CyberpunkComboBox") as Style,
                Height = 26,
                Width = 110,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            _downloadMissingChapterCpuPriorityComboBox.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
            _downloadMissingChapterCpuPriorityComboBox.ItemContainerStyle = TryFindResource("CyberpunkComboBoxItemStyle") as Style;
            _downloadMissingChapterCpuPriorityComboBox.Items.Add(new ComboBoxItem { Content = "below normal" });
            _downloadMissingChapterCpuPriorityComboBox.Items.Add(new ComboBoxItem { Content = "normal" });
            _downloadMissingChapterCpuPriorityComboBox.Items.Add(new ComboBoxItem { Content = "above normal" });
            _downloadMissingChapterCpuPriorityComboBox.Items.Add(new ComboBoxItem { Content = "high" });
            _downloadMissingChapterCpuPriorityComboBox.SelectedIndex = 2; // Default to above normal
            _downloadMissingChapterCpuPriorityComboBox.SelectionChanged += CmbCpuPriority_SelectionChanged;
            panel.Children.Add(_downloadMissingChapterCpuPriorityComboBox);

            return panel;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        private static List<int> GetChildProcessIds(int parentPid)
        {
            var results = new List<int>();
            IntPtr handle = CreateToolhelp32Snapshot(0x00000002, 0); // TH32CS_SNAPPROCESS = 2
            if (handle == IntPtr.Zero) return results;

            try
            {
                var entry = new PROCESSENTRY32();
                entry.dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(PROCESSENTRY32));

                if (Process32First(handle, ref entry))
                {
                    do
                    {
                        if (entry.th32ParentProcessID == parentPid)
                        {
                            results.Add((int)entry.th32ProcessID);
                        }
                    } while (Process32Next(handle, ref entry));
                }
            }
            catch { }
            finally
            {
                CloseHandle(handle);
            }
            return results;
        }

        private static void GetDescendantProcessIds(int parentPid, HashSet<int> descendants)
        {
            var children = GetChildProcessIds(parentPid);
            foreach (var child in children)
            {
                if (descendants.Add(child))
                {
                    GetDescendantProcessIds(child, descendants);
                }
            }
        }

        private static int _targetChildCpuAffinityCores = 2;
        private static System.Diagnostics.ProcessPriorityClass _targetChildCpuPriority = System.Diagnostics.ProcessPriorityClass.AboveNormal;

        private static void ApplyCurrentCpuRestrictions()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    int cores = Environment.ProcessorCount;
                    long mask = (1L << cores) - 1; // Default to all cores
                    if (_targetChildCpuAffinityCores < cores && _targetChildCpuAffinityCores > 0)
                    {
                        mask = (1L << _targetChildCpuAffinityCores) - 1;
                    }

                    IntPtr affinity = (IntPtr)mask;
                    var priority = _targetChildCpuPriority;

                    var currentProc = System.Diagnostics.Process.GetCurrentProcess();
                    int currentPid = currentProc.Id;
                    var descendants = new System.Collections.Generic.HashSet<int>();
                    GetDescendantProcessIds(currentPid, descendants);

                    foreach (int pid in descendants)
                    {
                        try
                        {
                            using (var proc = System.Diagnostics.Process.GetProcessById(pid))
                            {
                                if (proc.ProcessorAffinity != affinity)
                                {
                                    proc.ProcessorAffinity = affinity;
                                }
                                if (proc.PriorityClass != priority)
                                {
                                    proc.PriorityClass = priority;
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            });
        }

        private void CmbCpuAffinity_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_downloadMissingChapterCpuAffinityComboBox == null) return;
            string selected = _downloadMissingChapterCpuAffinityComboBox.SelectedItem as string;
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

                if (cmbWebviewCpuAffinity != null && cmbWebviewCpuAffinity.SelectedIndex != _downloadMissingChapterCpuAffinityComboBox.SelectedIndex)
                {
                    cmbWebviewCpuAffinity.SelectedIndex = _downloadMissingChapterCpuAffinityComboBox.SelectedIndex;
                }

                ApplyCurrentCpuRestrictions();
                Log($"Đã giới hạn CPU Affinity cho tiến trình con thành: {selected} core(s).");
            }
            catch (Exception ex)
            {
                Log($"Lỗi khi cài đặt CPU Affinity: {ex.Message}");
            }
        }

        private void CmbCpuPriority_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_downloadMissingChapterCpuPriorityComboBox == null) return;
            var selectedItem = _downloadMissingChapterCpuPriorityComboBox.SelectedItem as ComboBoxItem;
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

                if (cmbWebviewCpuPriority != null && cmbWebviewCpuPriority.SelectedIndex != _downloadMissingChapterCpuPriorityComboBox.SelectedIndex)
                {
                    cmbWebviewCpuPriority.SelectedIndex = _downloadMissingChapterCpuPriorityComboBox.SelectedIndex;
                }

                ApplyCurrentCpuRestrictions();
                Log($"Đã giới hạn CPU Priority cho tiến trình con thành: {content}.");
            }
            catch (Exception ex)
            {
                Log($"Lỗi khi cài đặt CPU Priority: {ex.Message}");
            }
        }

        private int GetDownloadMissingChapterParallelLimit()
        {
            if (!Dispatcher.CheckAccess())
            {
                return Dispatcher.Invoke(GetDownloadMissingChapterParallelLimit);
            }

            if (_downloadMissingChapterParallelComboBox == null)
            {
                return 1;
            }

            string text =
                (_downloadMissingChapterParallelComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ??
                _downloadMissingChapterParallelComboBox.SelectionBoxItem?.ToString() ??
                _downloadMissingChapterParallelComboBox.Text;
            return int.TryParse(text, out int value)
                ? Math.Min(16, Math.Max(1, value))
                : 8;
        }

        private DataGrid CreateDownloadMissingChapterIssueGrid()
        {
            var grid = new DataGrid
            {
                Background = new SolidColorBrush(Color.FromRgb(0x09, 0x0D, 0x16)),
                BorderBrush = (Brush)TryFindResource("CyberpunkBorderBrush"),
                BorderThickness = new Thickness(1),
                Foreground = (Brush)TryFindResource("CyberpunkTextBrush"),
                Style = TryFindResource("CyberpunkDataGrid") as Style,
                FontSize = 14,
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserResizeRows = false,
                CanUserReorderColumns = false,
                EnableRowVirtualization = true,
                EnableColumnVirtualization = true,
                IsReadOnly = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                SelectionMode = DataGridSelectionMode.Extended,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                GridLinesVisibility = DataGridGridLinesVisibility.None,
                RowHeaderWidth = 0
            };
            VirtualizingPanel.SetIsVirtualizing(grid, true);
            VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
            VirtualizingPanel.SetScrollUnit(grid, ScrollUnit.Item);
            ScrollViewer.SetCanContentScroll(grid, true);
            ScrollViewer.SetIsDeferredScrollingEnabled(grid, false);

            Style baseHeaderStyle = TryFindResource("CyberpunkDataGridColumnHeader") as Style;
            if (baseHeaderStyle != null)
            {
                Style customHeaderStyle = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader), baseHeaderStyle);
                customHeaderStyle.Setters.Add(new Setter(System.Windows.Controls.Primitives.DataGridColumnHeader.FontSizeProperty, 13.0));
                grid.ColumnHeaderStyle = customHeaderStyle;
            }
            else
            {
                grid.ColumnHeaderStyle = TryFindResource("CyberpunkDataGridColumnHeader") as Style;
            }

            Style baseRowStyle = TryFindResource("CyberpunkDataGridRow") as Style;
            if (baseRowStyle != null)
            {
                Style customRowStyle = new Style(typeof(DataGridRow), baseRowStyle);
                var trigger = new DataTrigger
                {
                    Binding = new Binding("IsMissingChapters"),
                    Value = true
                };
                trigger.Setters.Add(new Setter(DataGridRow.ForegroundProperty, new SolidColorBrush(Colors.Cyan)));
                customRowStyle.Triggers.Add(trigger);
                grid.RowStyle = customRowStyle;
            }
            else
            {
                grid.RowStyle = TryFindResource("CyberpunkDataGridRow") as Style;
            }

            grid.CellStyle = TryFindResource("CyberpunkDataGridCell") as Style;
            grid.MouseDoubleClick += DownloadMissingChapterGrid_MouseDoubleClick;
            grid.ContextMenu = CreateDownloadMissingChapterContextMenu();
            grid.PreviewMouseRightButtonDown += DownloadMissingChapterGrid_PreviewMouseRightButtonDown;
            grid.PreviewKeyDown += DownloadMissingChapterGrid_PreviewKeyDown;
            grid.PreviewTextInput += DownloadMissingChapterGrid_PreviewTextInput;
            grid.Sorting += DownloadMissingChapterGrid_Sorting;

            grid.Columns.Add(CreateDownloadMissingChapterCheckColumn());
            grid.Columns.Add(CreateDownloadMissingChapterTextColumn("Domain", nameof(ReaderChapterIssueItem.DomainLabel), 120));
            grid.Columns.Add(CreateDownloadMissingChapterTextColumn("Book", nameof(ReaderChapterIssueItem.BookName), 300));
            grid.Columns.Add(CreateDownloadMissingChapterTextColumn("Chapter", nameof(ReaderChapterIssueItem.ChapterLabel), 120));
            grid.Columns.Add(CreateDownloadMissingChapterColoredTextColumn("Missing integer chapter", 300));
            grid.Columns.Add(CreateDownloadMissingChapterDecimalColumn(new DataGridLength(1, DataGridLengthUnitType.Star)));

            return grid;
        }

        private DataGridTextColumn CreateDownloadMissingChapterTextColumn(string header, string propertyName, object width)
        {
            var column = new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(propertyName),
                IsReadOnly = true,
                ElementStyle = TryFindResource("CheckErrorWrapTextStyle") as Style
            };

            if (width is int fixedIntWidth)
            {
                column.Width = fixedIntWidth;
            }
            else if (width is double fixedWidth)
            {
                column.Width = fixedWidth;
            }
            else if (width is DataGridLength gridLength)
            {
                column.Width = gridLength;
            }

            return column;
        }

        private DataGridTemplateColumn CreateDownloadMissingChapterDecimalColumn(object width)
        {
            var textBlock = new FrameworkElementFactory(typeof(TextBlock));
            textBlock.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Top);
            textBlock.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler(DownloadMissingChapterDecimalCell_Loaded));

            var column = new DataGridTemplateColumn
            {
                Header = CreateDownloadMissingChapterDecimalHeader(),
                CellTemplate = new DataTemplate { VisualTree = textBlock },
                IsReadOnly = true,
                SortMemberPath = nameof(ReaderChapterIssueItem.DecimalChapterLabel)
            };

            if (width is int fixedIntWidth)
            {
                column.Width = fixedIntWidth;
            }
            else if (width is double fixedWidth)
            {
                column.Width = fixedWidth;
            }
            else if (width is DataGridLength gridLength)
            {
                column.Width = gridLength;
            }

            return column;
        }

        private object CreateDownloadMissingChapterDecimalHeader()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            _downloadMissingChapterDecimalHeaderText = new TextBlock
            {
                Text = _isVietnameseUi ? "Chap thập phân" : "Decimal chapter",
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 8, 0)
            };
            panel.Children.Add(_downloadMissingChapterDecimalHeaderText);

            _downloadMissingChapterDecimalWrapToggle = new ToggleButton
            {
                Content = "WRAP",
                IsChecked = _downloadMissingChapterDecimalWrapEnabled,
                Height = 20,
                MinWidth = 48,
                Padding = new Thickness(8, 0, 8, 0),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Style = TryFindResource("CyberpunkToggleButtonPink") as Style
            };
            _downloadMissingChapterDecimalWrapToggle.Checked += DownloadMissingChapterDecimalWrapToggle_Changed;
            _downloadMissingChapterDecimalWrapToggle.Unchecked += DownloadMissingChapterDecimalWrapToggle_Changed;
            panel.Children.Add(_downloadMissingChapterDecimalWrapToggle);
            UpdateDownloadMissingChapterDecimalWrapToggleVisual();

            return panel;
        }

        private void DownloadMissingChapterDecimalCell_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBlock textBlock && textBlock.DataContext is ReaderChapterIssueItem row)
            {
                textBlock.TextWrapping = _downloadMissingChapterDecimalWrapEnabled ? TextWrapping.Wrap : TextWrapping.NoWrap;
                RenderDownloadMissingChapterDecimalCell(textBlock, row);
            }
        }

        private void RenderDownloadMissingChapterDecimalCell(TextBlock textBlock, ReaderChapterIssueItem row)
        {
            textBlock.Inlines.Clear();
            string label = row?.DecimalChapterLabel ?? string.Empty;
            if (string.IsNullOrWhiteSpace(label))
            {
                return;
            }

            string[] parts = label.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                textBlock.Inlines.Add(new System.Windows.Documents.Run(label) { Foreground = Brushes.White });
                return;
            }

            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Length == 0)
                {
                    continue;
                }

                if (textBlock.Inlines.Count > 0)
                {
                    textBlock.Inlines.Add(new System.Windows.Documents.Run(", ") { Foreground = Brushes.White });
                }

                textBlock.Inlines.Add(new System.Windows.Documents.Run(part)
                {
                    Foreground = IsSharedDownloadDecimalChapterToken(row, part)
                        ? Brushes.Yellow
                        : Brushes.White
                });
            }
        }

        private bool IsSharedDownloadDecimalChapterToken(ReaderChapterIssueItem row, string token)
        {
            string name = NormalizeDownloadMissingChapterBookName(row?.BookName);
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            string normalizedToken = token.Trim();
            return _downloadMissingChapterRows
                .Where(candidate => candidate != null &&
                                    !ReferenceEquals(candidate, row) &&
                                    string.Equals(NormalizeDownloadMissingChapterBookName(candidate.BookName), name, StringComparison.OrdinalIgnoreCase) &&
                                    !string.Equals((candidate.DomainLabel ?? string.Empty).Trim(), (row.DomainLabel ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
                .Any(candidate => SplitDownloadMissingChapterLabel(candidate.DecimalChapterLabel)
                    .Any(candidateToken => string.Equals(candidateToken, normalizedToken, StringComparison.OrdinalIgnoreCase)));
        }

        private void DownloadMissingChapterDecimalWrapToggle_Changed(object sender, RoutedEventArgs e)
        {
            _downloadMissingChapterDecimalWrapEnabled = _downloadMissingChapterDecimalWrapToggle?.IsChecked == true;
            UpdateDownloadMissingChapterDecimalWrapToggleVisual();
            SafeRefreshMissingChaptersView();
        }

        private void UpdateDownloadMissingChapterDecimalWrapToggleVisual()
        {
            if (_downloadMissingChapterDecimalWrapToggle == null)
            {
                return;
            }

            _downloadMissingChapterDecimalWrapToggle.Style = TryFindResource(_downloadMissingChapterDecimalWrapEnabled ? "CyberpunkToggleButtonCyan" : "CyberpunkToggleButtonPink") as Style;
            _downloadMissingChapterDecimalWrapToggle.Background = new SolidColorBrush(_downloadMissingChapterDecimalWrapEnabled ? Color.FromRgb(0x11, 0x35, 0x24) : Color.FromRgb(0x35, 0x11, 0x17));
            _downloadMissingChapterDecimalWrapToggle.BorderBrush = new SolidColorBrush(_downloadMissingChapterDecimalWrapEnabled ? Color.FromRgb(0x53, 0xFF, 0x9A) : Color.FromRgb(0xFF, 0x5E, 0x6A));
            _downloadMissingChapterDecimalWrapToggle.Foreground = new SolidColorBrush(_downloadMissingChapterDecimalWrapEnabled ? Color.FromRgb(0xDF, 0xFF, 0xEF) : Color.FromRgb(0xFF, 0xE9, 0xEC));
            _downloadMissingChapterDecimalWrapToggle.Content = "WRAP";
            _downloadMissingChapterDecimalWrapToggle.ToolTip = _downloadMissingChapterDecimalWrapEnabled
                ? (_isVietnameseUi ? "Chap thập phân đang bật xuống dòng" : "Decimal chapter word wrap is on")
                : (_isVietnameseUi ? "Chap thập phân đang tắt xuống dòng" : "Decimal chapter word wrap is off");
        }

        private DataGridTemplateColumn CreateDownloadMissingChapterColoredTextColumn(string header, object width)
        {
            var textBlock = new FrameworkElementFactory(typeof(TextBlock));
            textBlock.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            textBlock.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Top);
            textBlock.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler(DownloadMissingChapterMissingCell_Loaded));

            var column = new DataGridTemplateColumn
            {
                Header = header,
                CellTemplate = new DataTemplate { VisualTree = textBlock },
                IsReadOnly = true,
                SortMemberPath = nameof(ReaderChapterIssueItem.MissingChapterLabel)
            };

            if (width is int fixedIntWidth)
            {
                column.Width = fixedIntWidth;
            }
            else if (width is double fixedWidth)
            {
                column.Width = fixedWidth;
            }
            else if (width is DataGridLength gridLength)
            {
                column.Width = gridLength;
            }

            return column;
        }

        private void DownloadMissingChapterMissingCell_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBlock textBlock && textBlock.DataContext is ReaderChapterIssueItem row)
            {
                RenderDownloadMissingChapterMissingCell(textBlock, row);
            }
        }

        private void RenderDownloadMissingChapterMissingCell(TextBlock textBlock, ReaderChapterIssueItem row)
        {
            textBlock.Inlines.Clear();
            string label = row?.MissingChapterLabel ?? string.Empty;
            if (string.IsNullOrWhiteSpace(label))
            {
                return;
            }

            string[] parts = label.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                textBlock.Inlines.Add(new System.Windows.Documents.Run(label) { Foreground = Brushes.White });
                return;
            }

            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Length == 0)
                {
                    continue;
                }

                if (textBlock.Inlines.Count > 0)
                {
                    textBlock.Inlines.Add(new System.Windows.Documents.Run(", ") { Foreground = Brushes.White });
                }

                textBlock.Inlines.Add(new System.Windows.Documents.Run(part)
                {
                    Foreground = IsSharedDownloadMissingChapterToken(row, part)
                        ? Brushes.Yellow
                        : Brushes.White
                });
            }
        }

        private bool IsSharedDownloadMissingChapterToken(ReaderChapterIssueItem row, string token)
        {
            string name = NormalizeDownloadMissingChapterBookName(row?.BookName);
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            string normalizedToken = token.Trim();
            return _downloadMissingChapterRows
                .Where(candidate => candidate != null &&
                                    !ReferenceEquals(candidate, row) &&
                                    string.Equals(NormalizeDownloadMissingChapterBookName(candidate.BookName), name, StringComparison.OrdinalIgnoreCase) &&
                                    !string.Equals((candidate.DomainLabel ?? string.Empty).Trim(), (row.DomainLabel ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
                .Any(candidate => SplitDownloadMissingChapterLabel(candidate.MissingChapterLabel)
                    .Any(candidateToken => string.Equals(candidateToken, normalizedToken, StringComparison.OrdinalIgnoreCase)));
        }

        private static IEnumerable<string> SplitDownloadMissingChapterLabel(string label)
        {
            return (label ?? string.Empty)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => part.Length > 0);
        }

        private static string NormalizeDownloadMissingChapterBookName(string name)
        {
            return Regex.Replace((name ?? string.Empty).Trim(), @"\s+", " ");
        }

        private DataGridTemplateColumn CreateDownloadMissingChapterCheckColumn()
        {
            var template = new DataTemplate();
            var panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            panel.SetValue(StackPanel.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            panel.SetValue(StackPanel.VerticalAlignmentProperty, VerticalAlignment.Center);

            var checkBox = new FrameworkElementFactory(typeof(CheckBox));
            checkBox.SetValue(CheckBox.MarginProperty, new Thickness(0, 0, 6, 0));
            checkBox.SetValue(CheckBox.VerticalAlignmentProperty, VerticalAlignment.Center);
            checkBox.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(ReaderChapterIssueItem.IsChecked))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            panel.AppendChild(checkBox);

            var rowNumber = new FrameworkElementFactory(typeof(TextBlock));
            rowNumber.SetValue(TextBlock.ForegroundProperty, (Brush)TryFindResource("CyberpunkTextBrush") ?? Brushes.White);
            rowNumber.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            rowNumber.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            rowNumber.SetBinding(TextBlock.TextProperty, new Binding(nameof(ReaderChapterIssueItem.RowNumber)));
            panel.AppendChild(rowNumber);
            template.VisualTree = panel;

            return new DataGridTemplateColumn
            {
                Header = "#",
                Width = 58,
                CellTemplate = template,
                SortMemberPath = nameof(ReaderChapterIssueItem.RowNumber)
            };
        }

        private ContextMenu CreateDownloadMissingChapterContextMenu()
        {
            Brush yellowBrush = (Brush)TryFindResource("CyberpunkYellowBrush") ?? Brushes.Yellow;
            Brush borderBrush = (Brush)TryFindResource("CyberpunkBorderBrush") ?? yellowBrush;
            Brush backgroundBrush = new SolidColorBrush(Color.FromRgb(0x09, 0x0D, 0x16));
            Brush highlightBrush = new SolidColorBrush(Color.FromRgb(0x12, 0x25, 0x38));
            var menu = new ContextMenu
            {
                Background = backgroundBrush,
                Foreground = yellowBrush,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                HasDropShadow = false,
                Template = BuildDownloadMissingChapterContextMenuTemplate()
            };
            menu.Resources[SystemColors.MenuBrushKey] = menu.Background;
            menu.Resources[SystemColors.MenuTextBrushKey] = menu.Foreground;
            menu.Resources[SystemColors.HighlightBrushKey] = highlightBrush;
            menu.Resources[SystemColors.HighlightTextBrushKey] = menu.Foreground;
            menu.Resources["Menu.Static.Background"] = backgroundBrush;
            menu.Resources["Menu.Static.Foreground"] = yellowBrush;
            menu.Resources["MenuItem.Highlight.Background"] = highlightBrush;
            menu.Resources["MenuItem.Highlight.Border"] = yellowBrush;
            menu.Resources["MenuItem.Highlight.Foreground"] = yellowBrush;
            menu.Resources["MenuItem.Selected.Background"] = highlightBrush;
            menu.Resources["MenuItem.Selected.Border"] = yellowBrush;
            menu.Resources["MenuItem.Selected.Foreground"] = yellowBrush;
            menu.Resources["MenuPopupBrush"] = backgroundBrush;
            menu.Resources[typeof(Separator)] = BuildDownloadMissingChapterSeparatorStyle();
            menu.Resources[typeof(MenuItem)] = BuildDownloadMissingChapterMenuItemStyle();
            menu.Items.Add(CreateDownloadMissingChapterMenuItem(_isVietnameseUi ? "Mở link truyện" : "Open book link", DownloadMissingChapterOpenLink_Click));
            menu.Items.Add(CreateDownloadMissingChapterMenuItem(_isVietnameseUi ? "Copy link truyện" : "Copy book link", DownloadMissingChapterCopyLink_Click));
            menu.Items.Add(CreateDownloadMissingChapterMenuItem(_isVietnameseUi ? "Copy chap số nguyên thiếu" : "Copy missing integer chapter", DownloadMissingChapterCopyInteger_Click));
            menu.Items.Add(CreateDownloadMissingChapterMenuItem(_isVietnameseUi ? "Copy chap thập phân" : "Copy decimal chapter", DownloadMissingChapterCopyDecimal_Click));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateDownloadMissingChapterMenuItem(_isVietnameseUi ? "Chọn" : "Check", DownloadMissingChapterCheckSelected_Click));
            menu.Items.Add(CreateDownloadMissingChapterMenuItem(_isVietnameseUi ? "Bỏ chọn" : "Uncheck", DownloadMissingChapterUncheckSelected_Click));
            menu.Items.Add(CreateDownloadMissingChapterMenuItem(_isVietnameseUi ? "Đảo chọn" : "Toggle", DownloadMissingChapterToggleSelected_Click));
            menu.Items.Add(CreateDownloadMissingChapterMenuItem(_isVietnameseUi ? "Chọn tất cả" : "Check all", DownloadMissingChapterCheckAll_Click));
            menu.Items.Add(CreateDownloadMissingChapterMenuItem(_isVietnameseUi ? "Bỏ chọn tất cả" : "Uncheck all", DownloadMissingChapterUncheckAll_Click));
            return menu;
        }

        private MenuItem CreateDownloadMissingChapterMenuItem(string text, RoutedEventHandler clickHandler)
        {
            var item = new MenuItem
            {
                Header = text,
                Foreground = (Brush)TryFindResource("CyberpunkYellowBrush") ?? Brushes.Yellow,
                Background = Brushes.Transparent
            };
            item.Click += clickHandler;
            return item;
        }

        private ControlTemplate BuildDownloadMissingChapterContextMenuTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new Thickness(0));

            var presenter = new FrameworkElementFactory(typeof(StackPanel));
            presenter.SetValue(StackPanel.IsItemsHostProperty, true);
            border.AppendChild(presenter);

            return new ControlTemplate(typeof(ContextMenu))
            {
                VisualTree = border
            };
        }

        private Style BuildDownloadMissingChapterMenuItemStyle()
        {
            var style = new Style(typeof(MenuItem));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.ForegroundProperty, (Brush)TryFindResource("CyberpunkYellowBrush") ?? Brushes.Yellow));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 6, 10, 6)));
            style.Setters.Add(new Setter(MenuItem.IconProperty, null));
            style.Setters.Add(new Setter(Control.TemplateProperty, BuildDownloadMissingChapterMenuItemTemplate()));

            var highlightedTrigger = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
            highlightedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x12, 0x25, 0x38))));
            highlightedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, (Brush)TryFindResource("CyberpunkYellowBrush") ?? Brushes.Yellow));
            style.Triggers.Add(highlightedTrigger);

            var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45d));
            style.Triggers.Add(disabledTrigger);

            return style;
        }

        private ControlTemplate BuildDownloadMissingChapterMenuItemTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            presenter.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            presenter.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);

            return new ControlTemplate(typeof(MenuItem))
            {
                VisualTree = border
            };
        }

        private Style BuildDownloadMissingChapterSeparatorStyle()
        {
            var style = new Style(typeof(Separator));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 3, 4, 3));
            border.SetValue(Border.HeightProperty, 1d);
            border.SetValue(Border.BackgroundProperty, (Brush)TryFindResource("CyberpunkBorderBrush") ?? Brushes.DimGray);
            style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Separator))
            {
                VisualTree = border
            }));
            return style;
        }

        private void UpdateDownloadMissingChapterLanguage()
        {
            if (_downloadMissingChapterTab != null)
            {
                _downloadMissingChapterTab.Header = _isVietnameseUi ? "Scan chap số nguyên thiếu" : "Scan missing integer chapter";
            }

            if (_downloadMissingChapterCheckButton != null)
            {
                _downloadMissingChapterCheckButton.Content = _isVietnameseUi ? "SCAN CHAP SỐ NGUYÊN THIẾU" : "SCAN MISSING INTEGER CHAPTER";
            }

            if (_downloadMissingChapterParallelLabel != null)
            {
                _downloadMissingChapterParallelLabel.Text = _isVietnameseUi ? "CHECK SONG SONG" : "MULTIPLE CHECK";
            }

            if (_downloadMissingChapterCpuAffinityLabel != null)
            {
                _downloadMissingChapterCpuAffinityLabel.Text = _isVietnameseUi ? "GIỚI HẠN CPU" : "CPU AFFINITY";
            }

            if (_downloadMissingChapterCpuPriorityLabel != null)
            {
                _downloadMissingChapterCpuPriorityLabel.Text = _isVietnameseUi ? "ĐỘ ƯU TIÊN CPU" : "CPU PRIORITY";
            }

            if (_downloadMissingChapterDecimalHeaderText != null)
            {
                _downloadMissingChapterDecimalHeaderText.Text = _isVietnameseUi ? "Chap thập phân" : "Decimal chapter";
            }
            UpdateDownloadMissingChapterDecimalWrapToggleVisual();

            if (_downloadMissingChapterStopButton != null)
            {
                _downloadMissingChapterStopButton.Content = _isVietnameseUi ? "STOP" : "STOP";
            }

            if (_downloadMissingChapterRescanButton != null)
            {
                _downloadMissingChapterRescanButton.Content = _isVietnameseUi ? "SCAN LẠI CHAP SỐ NGUYÊN THIẾU" : "RESCAN MISSING INTEGER CHAPTER";
            }

            UpdateDownloadMissingChapterPauseUi();

            if (_downloadMissingChapterOpenCacheButton != null)
            {
                _downloadMissingChapterOpenCacheButton.Content = _isVietnameseUi ? "MỞ CACHE JSON" : "OPEN CACHE JSON";
            }

            if (_downloadMissingChapterClearCacheButton != null)
            {
                _downloadMissingChapterClearCacheButton.Content = _isVietnameseUi ? "XÓA CACHE JSON" : "CLEAR CACHE JSON";
            }

            if (_downloadMissingChapterCopyButton != null)
            {
                _downloadMissingChapterCopyButton.Content = _isVietnameseUi ? "COPY SELECTED CHAP SỐ NGUYÊN THIẾU" : "COPY SELECTED MISSING INTEGER CHAPTER";
            }

            if (_downloadMissingChapterCopyAllButton != null)
            {
                _downloadMissingChapterCopyAllButton.Content = _isVietnameseUi ? "COPY CHAP SỐ NGUYÊN THIẾU CỦA MỌI TRUYỆN" : "COPY ALL BOOK'S MISSING INTEGER CHAPTER";
            }

            if (_downloadMissingChapterClearButton != null)
            {
                _downloadMissingChapterClearButton.Content = _isVietnameseUi ? "CLEAR" : "CLEAR";
            }

            if (_downloadMissingChapterGrid != null && _downloadMissingChapterGrid.Columns.Count >= 6)
            {
                _downloadMissingChapterGrid.Columns[0].Header = "#";
                _downloadMissingChapterGrid.Columns[1].Header = _isVietnameseUi ? "Miền" : "Domain";
                _downloadMissingChapterGrid.Columns[2].Header = _isVietnameseUi ? "Truyện" : "Book";
                _downloadMissingChapterGrid.Columns[3].Header = _isVietnameseUi ? "Chương" : "Chapter";
                _downloadMissingChapterGrid.Columns[4].Header = _isVietnameseUi ? "Chương số nguyên thiếu" : "Missing integer chapter";
                _downloadMissingChapterGrid.ContextMenu = CreateDownloadMissingChapterContextMenu();
            }

            if (_downloadMissingChapterStatusText != null && string.IsNullOrWhiteSpace(_downloadMissingChapterStatusText.Text))
            {
                _downloadMissingChapterStatusText.Text = _isVietnameseUi
                    ? "Chưa scan chap số nguyên thiếu."
                    : "Missing integer chapter scan has not run yet.";
            }

            NormalizeDownloadMissingChapterLanguageRows();
            SyncAllGalleryMissingChapterStatuses();
            EnsureDownloadMissingChapterRowsFromGallery(forceSyncOrder: true);
        }

        private void NormalizeDownloadMissingChapterLanguageRows()
        {
            string completeText = GetDownloadCompleteChapterText();
            foreach (ReaderChapterIssueItem row in _downloadMissingChapterRows)
            {
                if (IsDownloadMissingChapterCompleteLabel(row?.MissingChapterLabel))
                {
                    row.MissingChapterLabel = completeText;
                }
            }
        }

        private void HandleDownloadMissingChapterTabSelection()
        {
            if (tabDownloadRoot == null || _downloadMissingChapterTab == null)
            {
                return;
            }

            EnsureDownloadMissingChapterRowsFromGallery(forceSyncOrder: true);

            if (ReferenceEquals(tabDownloadRoot.SelectedItem, _downloadMissingChapterTab) &&
                !_downloadMissingChapterScanInProgress &&
                GetDownloadMissingChapterPendingScanItems().Count > 0)
            {
                _ = ScanDownloadMissingChaptersAsync(forceRefresh: false);
            }
        }

        internal void SyncDownloadMissingChapterRowsToResultsOrder()
        {
            RequestDownloadMissingChapterRowSync(forceSyncOrder: true);
        }

        internal void RequestDownloadMissingChapterRowSync(bool forceSyncOrder)
        {
            _downloadMissingChapterPendingForceOrderSync |= forceSyncOrder;

            if (_downloadMissingChapterRowSyncTimer == null)
            {
                _downloadMissingChapterRowSyncTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(120)
                };
                _downloadMissingChapterRowSyncTimer.Tick += (s, e) =>
                {
                    _downloadMissingChapterRowSyncTimer.Stop();
                    bool shouldForce = _downloadMissingChapterPendingForceOrderSync;
                    _downloadMissingChapterPendingForceOrderSync = false;
                    EnsureDownloadMissingChapterRowsFromGallery(shouldForce);
                };
            }

            _downloadMissingChapterRowSyncTimer.Stop();
            _downloadMissingChapterRowSyncTimer.Start();
        }

        internal void RequestAutoScanDownloadMissingChapters(bool immediate = false)
        {
            if (_isRestoringGalleryListState)
            {
                return;
            }

            if (immediate)
            {
                Dispatcher.BeginInvoke(new Action(async () =>
                {
                    if (_scrapedItems.Count == 0)
                    {
                        return;
                    }

                    if (_downloadMissingChapterScanInProgress)
                    {
                        try 
                        { 
                            _downloadMissingChapterScanCts?.Cancel(); 
                        } 
                        catch {}
                        
                        int waitAttempts = 0;
                        while (_downloadMissingChapterScanInProgress && waitAttempts < 20)
                        {
                            await Task.Delay(25);
                            waitAttempts++;
                        }
                        _downloadMissingChapterScanInProgress = false;
                    }

                    EnsureDownloadMissingChapterRowsFromGallery(forceSyncOrder: true);
                    _ = ScanDownloadMissingChaptersAsync(forceRefresh: false);
                }), DispatcherPriority.Background);
                return;
            }

            if (_downloadMissingChapterAutoScanTimer == null)
            {
                _downloadMissingChapterAutoScanTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(300)
                };
                _downloadMissingChapterAutoScanTimer.Tick += async (s, e) =>
                {
                    _downloadMissingChapterAutoScanTimer.Stop();
                    if (_scrapedItems.Count == 0)
                    {
                        return;
                    }

                    if (_downloadMissingChapterScanInProgress)
                    {
                        try 
                        { 
                            _downloadMissingChapterScanCts?.Cancel(); 
                        } 
                        catch {}
                        
                        int waitAttempts = 0;
                        while (_downloadMissingChapterScanInProgress && waitAttempts < 20)
                        {
                            await Task.Delay(25);
                            waitAttempts++;
                        }
                        _downloadMissingChapterScanInProgress = false;
                    }

                    EnsureDownloadMissingChapterRowsFromGallery(forceSyncOrder: true);
                    _ = ScanDownloadMissingChaptersAsync(forceRefresh: false);
                };
            }

            _downloadMissingChapterAutoScanTimer.Stop();
            _downloadMissingChapterAutoScanTimer.Start();
        }

        internal bool HasDownloadMissingChapterRow(GalleryItem item)
        {
            return FindDownloadMissingChapterRow(item) != null;
        }

        private ReaderChapterIssueItem FindDownloadMissingChapterRow(GalleryItem item)
        {
            if (item == null)
            {
                return null;
            }

            string itemLink = (item.Link ?? string.Empty).Trim();
            string itemName = (item.Name ?? string.Empty).Trim();
            return _downloadMissingChapterRows.FirstOrDefault(row =>
                row != null && IsSameDownloadMissingChapterBook(item, row));
        }

        private bool IsDownloadMissingChapterRowScanned(ReaderChapterIssueItem row)
        {
            return row != null &&
                   (!string.IsNullOrWhiteSpace(row.ChapterLabel) ||
                    !string.IsNullOrWhiteSpace(row.MissingChapterLabel) ||
                    !string.IsNullOrWhiteSpace(row.DecimalChapterLabel));
        }

        private bool HasCachedDownloadChapterState(GalleryItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Link))
            {
                return false;
            }

            string itemLink = item.Link.Trim();
            if (_downloadChapterItemCache.ContainsKey(itemLink))
            {
                return true;
            }

            return _downloadChapterItemCache.Keys.Any(key =>
                string.Equals((key ?? string.Empty).Trim().TrimEnd('/'), itemLink.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
        }

        internal async Task EnsureDownloadMissingChapterScanBeforeDownloadAsync()
        {
            if (_downloadMissingChapterScanInProgress)
            {
                return;
            }

            // Perform scan for unscanned items first
            if (GetDownloadMissingChapterPendingScanItems().Count > 0 || _downloadMissingChapterRows.Count == 0)
            {
                await ScanDownloadMissingChaptersAsync(forceRefresh: false);
            }

            // Always check and apply auto split before start download
            if (cmbAutoSplitChapters != null && Dispatcher.CheckAccess())
            {
                var selectedVal = (cmbAutoSplitChapters.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (int.TryParse(selectedVal, out int bucketSize) && bucketSize > 0)
                {
                    var itemsToSplit = _scrapedItems
                        .Where(item => item != null && 
                                       !item.IsParallelSplitParent && 
                                       !item.IsParallelSplitTask && 
                                       string.IsNullOrWhiteSpace(item.ChapterSelectionText))
                        .ToList();

                    foreach (var item in itemsToSplit)
                    {
                        List<ReaderChapterItem> chapterItems = GetCachedDownloadChapterItems(item);
                        if (chapterItems == null || chapterItems.Count == 0)
                        {
                            // If not cached, extract them first
                            chapterItems = await ExtractChapterItemsFromBookAsync(item, CancellationToken.None);
                        }

                        if (chapterItems != null && chapterItems.Count > bucketSize)
                        {
                            List<string> ranges = await BuildParallelSplitRangesAsync(item, bucketSize);
                            if (ranges.Count > 0)
                            {
                                int insertIndex = _scrapedItems.IndexOf(item);
                                if (insertIndex >= 0)
                                {
                                    item.IsParallelSplitParent = true;
                                    item.IsParallelSplitCollapsed = true;
                                    item.IsChecked = false;
                                    item.IsStopped = true;
                                    item.Status = "Stopped";
                                    item.CurrentProcess = "Split to parallel tasks";

                                    List<GalleryItem> clones = ranges.Select(range => CreateParallelSplitTask(item, range)).ToList();
                                    item.ParallelSplitChildren = clones;
                                    item.ChapterSelectionText = "";

                                    item.RecalculateParentProgress();
                                    Log($"Auto split '{item.DisplayName}' into {clones.Count} tasks with bucket size {bucketSize} before download.");
                                }
                            }
                        }
                    }

                    RenumberResultOrder();
                    SafeRefreshResultsView();
                    RecalculateDuplicates();
                    UpdateStats();
                }
            }
        }

        private List<GalleryItem> GetGalleryItemsSnapshot(bool preferGridOrder = true)
        {
            if (!Dispatcher.CheckAccess())
            {
                return Dispatcher.Invoke(() => GetGalleryItemsSnapshot(preferGridOrder));
            }

            List<GalleryItem> baseList;
            if (preferGridOrder && dgResults != null && dgResults.Items.Count > 0)
            {
                baseList = dgResults.Items
                    .OfType<GalleryItem>()
                    .Where(item => item != null)
                    .ToList();
            }
            else
            {
                baseList = _scrapedItems
                    .Where(item => item != null)
                    .ToList();
            }

            var flattened = new List<GalleryItem>();
            foreach (var item in baseList)
            {
                if (!item.IsParallelSplitParent)
                {
                    flattened.Add(item);
                }
                else
                {
                    // Luôn luôn phải chứa parent
                    flattened.Add(item);
                    if (item.IsParallelSplitCollapsed)
                    {
                        flattened.AddRange(item.ParallelSplitChildren);
                    }
                }
            }
            return flattened;
        }

        private List<GalleryItem> GetDownloadMissingChapterSourceItems()
        {
            if (!Dispatcher.CheckAccess())
            {
                return Dispatcher.Invoke(GetDownloadMissingChapterSourceItems);
            }

            return GetGalleryItemsSnapshot(preferGridOrder: true)
                .Where(item => !ShouldSkipDownloadMissingChapterScan(item))
                .GroupBy(item => BuildDownloadMissingChapterItemKey(item?.Link, item?.Name, GetDownloadMissingChapterDomainLabel(item)), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        private bool ShouldSkipDownloadMissingChapterScan(GalleryItem item)
        {
            string domain = GetDownloadMissingChapterDomainLabel(item);
            return domain.IndexOf("hentaiforce", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private List<GalleryItem> GetDownloadMissingChapterPendingScanItems()
        {
            return GetDownloadMissingChapterSourceItems()
                .Where(item => item != null && !IsDownloadMissingChapterRowScanned(FindDownloadMissingChapterRow(item)))
                .ToList();
        }

        private bool HasDownloadMissingChapterUnscannedItems()
        {
            return GetDownloadMissingChapterPendingScanItems().Count > 0;
        }

        private bool ShouldUpdateDownloadMissingChapterProgressUi(int scannedBooks, int displayIndex, int displayTotal)
        {
            if (displayIndex <= 1 || displayIndex >= displayTotal)
            {
                _downloadMissingChapterLastProgressUiTick = Environment.TickCount;
                return true;
            }

            if ((scannedBooks % 25) == 0)
            {
                _downloadMissingChapterLastProgressUiTick = Environment.TickCount;
                return true;
            }

            int now = Environment.TickCount;
            if (unchecked(now - _downloadMissingChapterLastProgressUiTick) < 250)
            {
                return false;
            }

            _downloadMissingChapterLastProgressUiTick = now;
            return true;
        }

        private static bool IsSlowDownloadMissingChapterDomain(string domain)
        {
            return !string.IsNullOrWhiteSpace(domain) &&
                   domain.IndexOf("nettruyen", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int GetDownloadMissingChapterDomainLimit(string domain, int parallelLimit)
        {
            if (!IsSlowDownloadMissingChapterDomain(domain))
            {
                return parallelLimit;
            }

            return Math.Max(1, Math.Min(3, parallelLimit));
        }

        private DownloadMissingChapterScanWork StartDownloadMissingChapterScanWork(GalleryItem item, int rowNumber, int attempt, CancellationToken token, bool forceRefresh)
        {
            string domain = GetDownloadMissingChapterDomainLabel(item);
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            linkedCts.CancelAfter(TimeSpan.FromSeconds(25));

            var work = new DownloadMissingChapterScanWork
            {
                Item = item,
                RowNumber = rowNumber,
                Attempt = attempt,
                Domain = domain,
                Cts = linkedCts
            };

            work.Task = Task.Run(async () =>
            {
                try
                {
                    DownloadMissingChapterScanResult result = await ScanDownloadMissingChapterItemAsync(item, rowNumber, linkedCts.Token, forceRefresh || attempt > 1);
                    result.Attempt = attempt;
                    return result;
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested && linkedCts.IsCancellationRequested)
                {
                    return new DownloadMissingChapterScanResult
                    {
                        Item = item,
                        RowNumber = rowNumber,
                        ChapterItems = new List<ReaderChapterItem>(),
                        SummaryRow = null,
                        TimedOut = true,
                        Attempt = attempt
                    };
                }
                finally
                {
                    linkedCts.Dispose();
                }
            }, token);

            return work;
        }

        private async Task WarmUpDownloadMissingChapterDomainsAsync(IEnumerable<GalleryItem> items, CancellationToken token)
        {
            var urls = (items ?? Enumerable.Empty<GalleryItem>())
                .Select(item => item?.Link)
                .Where(link => !string.IsNullOrWhiteSpace(link))
                .Select(link =>
                {
                    try
                    {
                        var uri = new Uri(link);
                        return uri.GetLeftPart(UriPartial.Authority) + "/";
                    }
                    catch
                    {
                        return null;
                    }
                })
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToList();

            foreach (string url in urls)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using (var cts = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        cts.CancelAfter(TimeSpan.FromSeconds(4));
                        using (HttpResponseMessage _ = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token))
                        {
                        }
                    }
                }
                catch
                {
                }
            }
        }

        private async Task ScanDownloadMissingChaptersAsync(bool forceRefresh, IEnumerable<GalleryItem> explicitItems = null)
        {
            if (_downloadMissingChapterScanInProgress)
            {
                return;
            }

            EnsureDownloadMissingChapterRowsFromGallery(forceSyncOrder: true);
            List<GalleryItem> sourceItems = GetDownloadMissingChapterSourceItems();
            if (sourceItems.Count == 0)
            {
                if (_downloadMissingChapterStatusText != null)
                {
                    _downloadMissingChapterStatusText.Text = _isVietnameseUi ? "Không có truyện để quét." : "No books to scan.";
                }
                return;
            }

            List<GalleryItem> items = explicitItems == null
                ? GetDownloadMissingChapterPendingScanItems()
                : explicitItems
                    .Where(item => item != null)
                    .GroupBy(item => BuildDownloadMissingChapterItemKey(item.Link, item.Name, GetDownloadMissingChapterDomainLabel(item)))
                    .Select(group => group.First())
                    .ToList();
            if (items.Count == 0)
            {
                if (_downloadMissingChapterStatusText != null)
                {
                    _downloadMissingChapterStatusText.Text = explicitItems == null
                        ? (_isVietnameseUi
                            ? $"Không có truyện mới cần quét. Đã có kết quả cho {sourceItems.Count} truyện."
                            : $"No new books need scanning. Results already exist for {sourceItems.Count} books.")
                        : (_isVietnameseUi ? "Không có truyện nào được chọn để quét lại." : "No selected books to rescan.");
                }
                return;
            }

            _downloadMissingChapterScanInProgress = true;
            _downloadMissingChapterScanCts?.Cancel();
            _downloadMissingChapterScanCts?.Dispose();
            _downloadMissingChapterScanCts = new CancellationTokenSource();
            CancellationToken token = _downloadMissingChapterScanCts.Token;

            progressBar.IsIndeterminate = true;
            ShowResultsMissingChapterScanningIndicator();
            if (_downloadMissingChapterCheckButton != null) _downloadMissingChapterCheckButton.IsEnabled = false;
            if (_downloadMissingChapterStopButton != null) _downloadMissingChapterStopButton.IsEnabled = true;
            if (_downloadMissingChapterCopyButton != null) _downloadMissingChapterCopyButton.IsEnabled = false;
            if (_downloadMissingChapterCopyAllButton != null) _downloadMissingChapterCopyAllButton.IsEnabled = false;
            UpdateDownloadMissingChapterPauseUi();
            _downloadMissingChapterLastProgressUiTick = Environment.TickCount;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await WarmUpDownloadMissingChapterDomainsAsync(items, token);

            try
            {
                int booksWithIssues = 0;
                int booksWithoutIssues = 0;
                int scannedBooks = 0;
                int pendingUiFlushCount = 0;
                int lastWpfRefreshTick = Environment.TickCount;
                _downloadMissingChapterBulkRefreshing = true;
                int totalScanItems = items.Count;

                var pendingScans = new Queue<DownloadMissingChapterScanWork>(
                    items.Select((item, index) => new DownloadMissingChapterScanWork
                    {
                        Item = item,
                        RowNumber = index + 1,
                        Attempt = 1,
                        Domain = GetDownloadMissingChapterDomainLabel(item)
                    }));
                var runningScans = new List<DownloadMissingChapterScanWork>();

                while (scannedBooks < totalScanItems)
                {
                    token.ThrowIfCancellationRequested();
                    await WaitWhileDownloadMissingChapterScanPausedAsync(token);
                    int parallelLimit = GetDownloadMissingChapterParallelLimit();
                    int skippedPending = 0;
                    while (pendingScans.Count > 0 && runningScans.Count < parallelLimit && skippedPending < pendingScans.Count)
                    {
                        DownloadMissingChapterScanWork next = pendingScans.Dequeue();
                        int activeInDomain = runningScans.Count(work => string.Equals(work.Domain, next.Domain, StringComparison.OrdinalIgnoreCase));
                        int domainLimit = GetDownloadMissingChapterDomainLimit(next.Domain, parallelLimit);
                        if (activeInDomain >= domainLimit)
                        {
                            pendingScans.Enqueue(next);
                            skippedPending++;
                            continue;
                        }

                        runningScans.Add(StartDownloadMissingChapterScanWork(next.Item, next.RowNumber, next.Attempt, token, forceRefresh));
                        skippedPending = 0;
                    }

                    if (runningScans.Count == 0)
                    {
                        break;
                    }

                    DownloadMissingChapterScanWork completedWork = runningScans.FirstOrDefault(work => work.Task.IsCompleted);
                    if (completedWork == null)
                    {
                        Task<DownloadMissingChapterScanResult>[] activeTasks = runningScans.Select(work => work.Task).ToArray();
                        Task<Task<DownloadMissingChapterScanResult>> scanReadyTask = Task.WhenAny(activeTasks);
                        Task delayTask = Task.Delay(200, token);
                        Task firstTask = await Task.WhenAny(scanReadyTask, delayTask);
                        if (ReferenceEquals(firstTask, delayTask))
                        {
                            continue;
                        }

                        Task<DownloadMissingChapterScanResult> finishedTask = await scanReadyTask;
                        completedWork = runningScans.First(work => ReferenceEquals(work.Task, finishedTask));
                    }

                    runningScans.Remove(completedWork);
                    DownloadMissingChapterScanResult result;
                    try
                    {
                        result = await completedWork.Task;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        scannedBooks++;
                        Log($"[Missing Chapter] Lỗi task quét: {ex}");
                        continue;
                    }

                    if (result.TimedOut)
                    {
                        result.SummaryRow = new ReaderChapterIssueItem
                        {
                            DomainLabel = GetDownloadMissingChapterDomainLabel(result.Item),
                            BookName = result.Item?.Name,
                            BookLink = result.Item?.Link,
                            ChapterLabel = "timeout",
                            MissingChapterLabel = _isVietnameseUi ? "lỗi quét" : "scan error",
                            DecimalChapterLabel = string.Empty,
                            IsChecked = true
                        };
                    }

                    GalleryItem item = result.Item;
                    if (_downloadMissingChapterStatusText != null &&
                        ShouldUpdateDownloadMissingChapterProgressUi(scannedBooks, scannedBooks + 1, totalScanItems))
                    {
                        _downloadMissingChapterStatusText.Text = _isVietnameseUi
                            ? $"Đã xong {scannedBooks + 1}/{totalScanItems}, vừa xong hàng {result.RowNumber} ({parallelLimit} song song): {item.Name}"
                            : $"Done {scannedBooks + 1}/{totalScanItems}, finished row {result.RowNumber} ({parallelLimit} parallel): {item.Name}";
                    }
                    if ((scannedBooks & 15) == 0)
                    {
                        await Dispatcher.Yield(DispatcherPriority.Background);
                    }

                    if (!ContainsGalleryItem(item) || result.SummaryRow == null)
                    {
                        await Dispatcher.Yield(DispatcherPriority.Background);
                        scannedBooks++;
                        continue;
                    }

                    if (IsDownloadMissingChapterIssueRow(result.SummaryRow))
                    {
                        booksWithIssues++;
                    }
                    else
                    {
                        booksWithoutIssues++;
                    }

                    RefreshDownloadMissingChapterRow(item, result.ChapterItems, persist: false, reorder: false);
                    SyncGalleryMissingChapterStatus(item, result.SummaryRow);
                    
                    // Throttle heavy WPF layout refreshes to avoid UI thread starvation
                    int nowTicks = Environment.TickCount;
                    if (scannedBooks + 1 >= totalScanItems || unchecked(nowTicks - lastWpfRefreshTick) >= 2000)
                    {
                        lastWpfRefreshTick = nowTicks;
                        SafeRefreshMissingChaptersView();
                        SafeRefreshResultsView();
                    }
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    pendingUiFlushCount++;

                    // Auto split chapters to parallel tasks if configured
                    if (cmbAutoSplitChapters != null && Dispatcher.CheckAccess())
                    {
                        var selectedVal = (cmbAutoSplitChapters.SelectedItem as ComboBoxItem)?.Content?.ToString();
                        if (int.TryParse(selectedVal, out int bucketSize) && bucketSize > 0)
                        {
                            if (string.IsNullOrWhiteSpace(item.ChapterSelectionText))
                            {
                                int totalChapters = result.ChapterItems.Count;
                                if (totalChapters > bucketSize)
                                {
                                    List<string> ranges = await BuildParallelSplitRangesAsync(item, bucketSize);
                                    if (ranges.Count > 0)
                                    {
                                        int insertIndex = _scrapedItems.IndexOf(item);
                                        if (insertIndex >= 0)
                                        {
                                            item.IsParallelSplitParent = true;
                                            item.IsParallelSplitCollapsed = true;
                                            List<GalleryItem> clones = ranges.Select(range => CreateParallelSplitTask(item, range)).ToList();
                                            item.ParallelSplitChildren = clones;
                                            item.ChapterSelectionText = "";
                                            RenumberResultOrder();
                                            SafeRefreshResultsView();
                                            RecalculateDuplicates();
                                            UpdateStats();
                                            Log($"Auto split '{item.DisplayName}' into {clones.Count} tasks with bucket size {bucketSize}.");
                                        }
                                    }
                                }
                            }
                        }
                    }

                    scannedBooks++;
                    if (pendingUiFlushCount >= 32)
                    {
                        pendingUiFlushCount = 0;
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    }
                }

                _downloadMissingChapterBulkRefreshing = false;

                int missingCount = _downloadMissingChapterRows.Count(row =>
                    row != null &&
                    !string.IsNullOrWhiteSpace(row.MissingChapterLabel) &&
                    !IsDownloadMissingChapterCompleteLabel(row.MissingChapterLabel));
                int decimalCount = _downloadMissingChapterRows.Count(row => !string.IsNullOrWhiteSpace(row.DecimalChapterLabel));
                int finalSourceCount = explicitItems == null ? GetDownloadMissingChapterSourceItems().Count : items.Count;
                int skippedBooks = explicitItems == null ? Math.Max(0, finalSourceCount - scannedBooks) : 0;
                if (_downloadMissingChapterStatusText != null)
                {
                    _downloadMissingChapterStatusText.Text = _isVietnameseUi
                        ? (explicitItems == null
                            ? $"Đã quét thêm {scannedBooks} truyện, bỏ qua {skippedBooks} truyện đã có kết quả. Có vấn đề: {booksWithIssues}. Đủ chap số nguyên: {booksWithoutIssues}. Thiếu chap số nguyên tổng: {missingCount}. Chap thập phân tổng: {decimalCount}."
                            : $"Đã quét lại {scannedBooks} truyện. Có vấn đề: {booksWithIssues}. Đủ chap số nguyên: {booksWithoutIssues}. Thiếu chap số nguyên tổng: {missingCount}. Chap thập phân tổng: {decimalCount}.")
                        : (explicitItems == null
                            ? $"Scanned {scannedBooks} more books and skipped {skippedBooks} already scanned books. With issues: {booksWithIssues}. Complete: {booksWithoutIssues}. Total missing integer chapters: {missingCount}. Total decimal chapters: {decimalCount}."
                            : $"Rescanned {scannedBooks} books. With issues: {booksWithIssues}. Complete: {booksWithoutIssues}. Total missing integer chapters: {missingCount}. Total decimal chapters: {decimalCount}.");
                }

                ReorderDownloadMissingChapterRowsToMatchGallery();
                PersistDownloadMissingChapterCacheNow();
                RequestGalleryListAutosave(0);
            }
            catch (OperationCanceledException)
            {
                PersistDownloadMissingChapterCacheNow();
                if (_downloadMissingChapterStatusText != null)
                {
                    _downloadMissingChapterStatusText.Text = _isVietnameseUi ? "Đã dừng quét chap số nguyên thiếu." : "Missing integer chapter scan stopped.";
                }
            }
            catch (Exception ex)
            {
                Log($"[Missing Chapter] {ex.Message}");
                if (_downloadMissingChapterStatusText != null)
                {
                    _downloadMissingChapterStatusText.Text = _isVietnameseUi
                        ? "Quét chap số nguyên thiếu lỗi: " + ex.Message
                        : "Missing integer chapter scan failed: " + ex.Message;
                }
            }
            finally
            {
                _downloadMissingChapterBulkRefreshing = false;
                progressBar.IsIndeterminate = false;
                HideResultsMissingChapterScanningIndicator();
                _downloadMissingChapterScanPaused = false;
                if (_downloadMissingChapterCheckButton != null) _downloadMissingChapterCheckButton.IsEnabled = true;
                if (_downloadMissingChapterStopButton != null) _downloadMissingChapterStopButton.IsEnabled = false;
                if (_downloadMissingChapterCopyButton != null) _downloadMissingChapterCopyButton.IsEnabled = true;
                if (_downloadMissingChapterCopyAllButton != null) _downloadMissingChapterCopyAllButton.IsEnabled = true;
                _downloadMissingChapterScanInProgress = false;
                UpdateDownloadMissingChapterPauseUi();
                if (_downloadMissingChapterRestartAfterCurrentScan)
                {
                    _downloadMissingChapterRestartAfterCurrentScan = false;
                    RequestAutoScanDownloadMissingChapters(immediate: true);
                }
            }
        }

        internal void RestartDownloadMissingChapterScanForListChange()
        {
            if (_downloadMissingChapterScanInProgress)
            {
                _downloadMissingChapterRestartAfterCurrentScan = true;
            }
        }

        private async Task<DownloadMissingChapterScanResult> ScanDownloadMissingChapterItemAsync(GalleryItem item, int rowNumber, CancellationToken token, bool forceRefresh)
        {
            try
            {
                List<ReaderChapterItem> chapterItems = await ExtractChapterItemsFromBookAsync(item, token, forceRefresh);
                for (int attempt = 1; attempt <= 3 && HasMissingEarlyIntegerChapter(chapterItems); attempt++)
                {
                    token.ThrowIfCancellationRequested();
                    if (!string.IsNullOrWhiteSpace(item?.Link))
                    {
                        _downloadChapterItemCache.Remove(item.Link.TrimEnd('/'));
                        _downloadChapterItemCache.Remove(item.Link);
                    }
                    Log($"[Missing Chapter] '{item?.Name}' thiếu chap 1-3, quét lại lần {attempt}/3.");
                    await Task.Delay(350 * attempt, token);
                    chapterItems = await ExtractChapterItemsFromBookAsync(item, token, forceRefresh: true);
                }
                var manga = new ReaderMangaItem
                {
                    Name = item?.Name,
                    Chapters = chapterItems
                };
                return new DownloadMissingChapterScanResult
                {
                    Item = item,
                    RowNumber = rowNumber,
                    ChapterItems = chapterItems,
                    SummaryRow = BuildDownloadMissingChapterSummaryRow(item, manga)
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log($"[Missing Chapter] Lỗi quét hàng {rowNumber} '{item?.Name}': {ex.Message}");
                return new DownloadMissingChapterScanResult
                {
                    Item = item,
                    RowNumber = rowNumber,
                    ChapterItems = new List<ReaderChapterItem>(),
                    SummaryRow = new ReaderChapterIssueItem
                    {
                        DomainLabel = GetDownloadMissingChapterDomainLabel(item),
                        BookName = item?.Name,
                        BookLink = item?.Link,
                        ChapterLabel = "error",
                        MissingChapterLabel = _isVietnameseUi ? "lỗi quét" : "scan error",
                        DecimalChapterLabel = string.Empty,
                        IsChecked = true
                    }
                };
            }
        }

        private ReaderChapterIssueItem BuildDownloadMissingChapterSummaryRow(GalleryItem item, ReaderMangaItem manga)
        {
            if (item == null)
            {
                return null;
            }

            IList<ReaderChapterItem> chapters = manga != null ? manga.Chapters : null;
            if (chapters == null || chapters.Count == 0)
            {
                return new ReaderChapterIssueItem
                {
                    DomainLabel = GetDownloadMissingChapterDomainLabel(item),
                    BookName = item.Name,
                    BookLink = item.Link,
                    ChapterLabel = "0",
                    MissingChapterLabel = _isVietnameseUi ? "không có chapter" : "no chapters",
                    DecimalChapterLabel = string.Empty,
                    IsChecked = true
                };
            }

            string domain = GetDownloadMissingChapterDomainLabel(item);
            if (domain.IndexOf("mangadex.org", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Tách riêng các chapter tiếng Việt và tiếng Anh dựa vào hậu tố [VI] và [EN] trong Name
                var viChapters = chapters.Where(c => c != null && (c.Name ?? string.Empty).IndexOf("[VI]", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                var enChapters = chapters.Where(c => c != null && (c.Name ?? string.Empty).IndexOf("[EN]", StringComparison.OrdinalIgnoreCase) >= 0).ToList();

                ReaderChapterAnalysis viAnalysis = AnalyzeReaderChapterNumbers(viChapters);
                ReaderChapterAnalysis enAnalysis = AnalyzeReaderChapterNumbers(enChapters);

                string viMissing;
                if (viChapters.Count == 0)
                {
                    viMissing = _isVietnameseUi ? "không có chapter" : "no chapters";
                }
                else
                {
                    viMissing = viAnalysis.MissingRanges.Count == 0
                        ? (_isVietnameseUi ? "đủ chapter" : "complete")
                        : string.Join(", ", viAnalysis.MissingRanges);
                }

                string enMissing;
                if (enChapters.Count == 0)
                {
                    enMissing = _isVietnameseUi ? "không có chapter" : "no chapters";
                }
                else
                {
                    enMissing = enAnalysis.MissingRanges.Count == 0
                        ? (_isVietnameseUi ? "đủ chapter" : "complete")
                        : string.Join(", ", enAnalysis.MissingRanges);
                }

                string missingLabel = $"VI: {viMissing} | EN: {enMissing}";

                item.MissingChapterLatestChapterText = GetDownloadLatestChapterText(chapters);
                List<string> decimalChapters = chapters
                    .Where(chapter => chapter != null && chapter.IsDecimalChapter)
                    .OrderBy(chapter => chapter.Name, _readerSortComparer)
                    .Select(chapter => chapter.Name)
                    .ToList();

                string viCoverage = BuildDownloadChapterCoverageLabel(viChapters, viAnalysis);
                string enCoverage = BuildDownloadChapterCoverageLabel(enChapters, enAnalysis);
                string coverageLabel = $"VI: {viCoverage} | EN: {enCoverage}";

                return new ReaderChapterIssueItem
                {
                    DomainLabel = domain,
                    BookName = item.Name,
                    BookLink = item.Link,
                    ChapterLabel = coverageLabel,
                    MissingChapterLabel = missingLabel,
                    DecimalChapterLabel = decimalChapters.Count == 0 ? string.Empty : string.Join(", ", decimalChapters),
                    IsChecked = true
                };
            }

            ReaderChapterAnalysis analysis = AnalyzeReaderChapterNumbers(chapters);
            item.MissingChapterLatestChapterText = GetDownloadLatestChapterText(chapters);
            List<string> decimalChaptersGeneral = chapters
                .Where(chapter => chapter != null && chapter.IsDecimalChapter)
                .OrderBy(chapter => chapter.Name, _readerSortComparer)
                .Select(chapter => chapter.Name)
                .ToList();

            string missingLabelGeneral = analysis.MissingRanges.Count == 0
                ? GetDownloadCompleteChapterText()
                : string.Join(", ", analysis.MissingRanges);

            return new ReaderChapterIssueItem
            {
                DomainLabel = domain,
                BookName = item.Name,
                BookLink = item.Link,
                ChapterLabel = BuildDownloadChapterCoverageLabel(chapters, analysis),
                MissingChapterLabel = missingLabelGeneral,
                DecimalChapterLabel = decimalChaptersGeneral.Count == 0 ? string.Empty : string.Join(", ", decimalChaptersGeneral),
                IsChecked = true
            };
        }

        private string GetDownloadMissingChapterDomainLabel(GalleryItem item)
        {
            string url = item?.Link;
            if (IsNettruyenTechUrl(url))
            {
                url = ApplyNettruyenTechRedirectDomain(url);
            }

            string domain = null;
            if (!string.IsNullOrWhiteSpace(url))
            {
                try
                {
                    domain = new Uri(url).Host;
                }
                catch
                {
                }
            }

            if (string.IsNullOrWhiteSpace(domain))
            {
                domain = item?.SourceDomain;
            }

            domain = (domain ?? string.Empty).Trim().ToLowerInvariant();
            return domain.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                ? domain.Substring(4)
                : domain;
        }

        private static string BuildDownloadChapterCoverageLabel(IList<ReaderChapterItem> chapters, ReaderChapterAnalysis analysis)
        {
            if (chapters == null || chapters.Count == 0)
            {
                return "0";
            }

            List<int> integers = chapters
                .Where(chapter => chapter != null && !chapter.IsDecimalChapter && chapter.ParsedChapterNumber.HasValue)
                .SelectMany(chapter => EnumerateDownloadMissingChapterIntegers(chapter))
                .Distinct()
                .OrderBy(value => value)
                .ToList();

            if (integers.Count == 0)
            {
                return analysis != null ? analysis.UnknownCount.ToString() : chapters.Count.ToString();
            }

            string first = FormatReaderChapterNumber(integers.First());
            string last = FormatReaderChapterNumber(integers.Last());
            return first == last
                ? $"{first} ({integers.Count})"
                : $"{first}-{last} ({integers.Count})";
        }

        private static IEnumerable<int> EnumerateDownloadMissingChapterIntegers(ReaderChapterItem chapter)
        {
            if (chapter == null)
            {
                yield break;
            }

            if (TryParseReaderChapterIntegerRange(chapter.Name, out int start, out int end))
            {
                for (int number = start; number <= end; number++)
                {
                    yield return number;
                }
                yield break;
            }

            if (chapter.ParsedChapterNumber.HasValue)
            {
                yield return (int)Math.Round(chapter.ParsedChapterNumber.Value);
            }
        }

        private string GetDownloadLatestChapterText(IList<ReaderChapterItem> chapters)
        {
            if (chapters == null || chapters.Count == 0)
            {
                return string.Empty;
            }

            ReaderChapterItem latestNumbered = chapters
                .Where(chapter => chapter != null && chapter.ParsedChapterNumber.HasValue)
                .OrderBy(chapter => chapter.ParsedChapterNumber.Value)
                .LastOrDefault();
            if (!string.IsNullOrWhiteSpace(latestNumbered?.Name))
            {
                return latestNumbered.Name;
            }

            return chapters
                .Where(chapter => chapter != null && !string.IsNullOrWhiteSpace(chapter.Name))
                .OrderBy(chapter => chapter.Name, _readerSortComparer)
                .Select(chapter => chapter.Name)
                .LastOrDefault() ?? string.Empty;
        }

        private string GetDownloadCompleteChapterText()
        {
            return _isVietnameseUi ? "đủ chapter" : "complete";
        }

        private static bool IsDownloadMissingChapterCompleteLabel(string label)
        {
            return string.Equals(label, "đủ chapter", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(label, "complete", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsDownloadMissingChapterIssueRow(ReaderChapterIssueItem row)
        {
            if (row == null)
            {
                return false;
            }

            bool hasMissingInteger = !string.IsNullOrWhiteSpace(row.MissingChapterLabel) &&
                                     !IsDownloadMissingChapterCompleteLabel(row.MissingChapterLabel);
            bool hasDecimal = !string.IsNullOrWhiteSpace(row.DecimalChapterLabel);
            return hasMissingInteger || hasDecimal;
        }

        private void BtnCopyDownloadMissingChapters_Click(object sender, RoutedEventArgs e)
        {
            CopyDownloadMissingChapterRows(GetDownloadMissingChapterCheckedRows(includeAllWhenNoneChecked: false), _isVietnameseUi ? "Đã copy chap số nguyên thiếu đã chọn." : "Copied selected missing integer chapters.");
        }

        private void BtnCopyAllDownloadMissingChapters_Click(object sender, RoutedEventArgs e)
        {
            CopyDownloadMissingChapterRows(_downloadMissingChapterRows, _isVietnameseUi ? "Đã copy chap số nguyên thiếu của mọi truyện." : "Copied all book's missing integer chapter.");
        }

        private void BtnStopDownloadMissingChapters_Click(object sender, RoutedEventArgs e)
        {
            _downloadMissingChapterScanPaused = false;
            UpdateDownloadMissingChapterPauseUi();
            _downloadMissingChapterScanCts?.Cancel();
        }

        private async void BtnRescanDownloadMissingChapters_Click(object sender, RoutedEventArgs e)
        {
            await RescanDownloadMissingChapterRowsAsync(GetDownloadMissingChapterCheckedRows(includeAllWhenNoneChecked: false));
        }

        private void BtnPauseResumeDownloadMissingChapters_Click(object sender, RoutedEventArgs e)
        {
            _downloadMissingChapterScanPaused = !_downloadMissingChapterScanPaused;
            if (_downloadMissingChapterStatusText != null)
            {
                _downloadMissingChapterStatusText.Text = _downloadMissingChapterScanPaused
                    ? (_isVietnameseUi ? "Đã tạm dừng quét chap số nguyên thiếu." : "Missing integer chapter scan paused.")
                    : (_isVietnameseUi ? "Đã tiếp tục quét chap số nguyên thiếu." : "Missing integer chapter scan resumed.");
            }
            UpdateDownloadMissingChapterPauseUi();
        }

        private void UpdateDownloadMissingChapterPauseUi()
        {
            string text = _downloadMissingChapterScanPaused
                ? (_isVietnameseUi ? "RESUME CHAP SỐ NGUYÊN THIẾU" : "RESUME MISSING INTEGER CHAPTER")
                : (_isVietnameseUi ? "PAUSE CHAP SỐ NGUYÊN THIẾU" : "PAUSE MISSING INTEGER CHAPTER");
            bool enabled = _downloadMissingChapterScanInProgress || _downloadMissingChapterScanPaused;

            if (_downloadMissingChapterPauseButton != null)
            {
                _downloadMissingChapterPauseButton.Content = text;
                _downloadMissingChapterPauseButton.IsEnabled = enabled;
            }

            if (btnPauseMissingChapterScan != null)
            {
                btnPauseMissingChapterScan.Content = text;
                btnPauseMissingChapterScan.IsEnabled = enabled;
            }
        }

        private async Task WaitWhileDownloadMissingChapterScanPausedAsync(CancellationToken token)
        {
            while (_downloadMissingChapterScanPaused)
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(150, token);
            }
        }

        private void BtnOpenDownloadMissingChapterCache_Click(object sender, RoutedEventArgs e)
        {
            string cachePath = GetDownloadMissingChapterCachePath(CurrentGalleryListPath);
            string folderPath = Path.GetDirectoryName(cachePath);
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return;
            }

            Directory.CreateDirectory(folderPath);
            try
            {
                if (File.Exists(cachePath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = "/select,\"" + cachePath + "\"",
                        UseShellExecute = true
                    });
                }
                else if (!ShellFolderLauncher.TryOpenFolder(folderPath, out string error))
                {
                    MessageBox.Show(_isVietnameseUi ? $"Không thể mở thư mục cache: {error}" : $"Cannot open cache folder: {error}", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (_downloadMissingChapterStatusText != null)
                {
                    _downloadMissingChapterStatusText.Text = _isVietnameseUi ? "Đã mở cache JSON." : "Opened cache JSON.";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(_isVietnameseUi ? "Mở cache JSON lỗi: " + ex.Message : "Open cache JSON failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClearDownloadMissingChapterCache_Click(object sender, RoutedEventArgs e)
        {
            string cachePath = GetDownloadMissingChapterCachePath(CurrentGalleryListPath);
            TryDeleteFileIfExists(cachePath);
            ClearDownloadMissingChapterCacheState();
            _downloadMissingChapterRows.Clear();
            ClearGalleryMissingChapterStatuses();
            if (_downloadMissingChapterStatusText != null)
            {
                _downloadMissingChapterStatusText.Text = _isVietnameseUi ? "Đã xóa cache JSON chap số nguyên thiếu." : "Cleared missing integer chapter JSON cache.";
            }
            PersistDownloadMissingChapterCacheNow();
            RequestGalleryListAutosave(0);
        }

        private void BtnClearDownloadMissingChapters_Click(object sender, RoutedEventArgs e)
        {
            ClearDownloadMissingChapterCacheState();
            _downloadMissingChapterRows.Clear();
            ClearGalleryMissingChapterStatuses();
            if (_downloadMissingChapterStatusText != null)
            {
                _downloadMissingChapterStatusText.Text = _isVietnameseUi ? "Đã xóa kết quả quét chap số nguyên thiếu." : "Cleared missing integer chapter scan result.";
            }
            PersistDownloadMissingChapterCacheNow();
            RequestGalleryListAutosave(0);
        }

        private async Task RescanDownloadMissingChapterRowsAsync(IEnumerable<ReaderChapterIssueItem> rows)
        {
            List<GalleryItem> items = (rows ?? Enumerable.Empty<ReaderChapterIssueItem>())
                .Select(row => FindGalleryItemForMissingChapterRow(null, row))
                .Where(item => item != null)
                .Distinct()
                .ToList();
            if (items.Count == 0)
            {
                if (_downloadMissingChapterStatusText != null)
                {
                    _downloadMissingChapterStatusText.Text = _isVietnameseUi ? "Không có truyện nào được tick để quét lại." : "No checked books to rescan.";
                }
                return;
            }

            await ScanDownloadMissingChaptersAsync(forceRefresh: true, explicitItems: items);
        }

        private async Task<List<ReaderChapterItem>> ExtractChapterItemsFromBookAsync(GalleryItem item, CancellationToken token, bool forceRefresh = false)
        {
            List<ReaderChapterItem> chapterItems = await ExtractChapterItemsFromBookAsyncCore(item, token, forceRefresh);
            return chapterItems
                .OrderBy(chapter => chapter.Name, _readerSortComparer)
                .ToList();
        }

        private static List<ReaderChapterItem> CloneReaderChapterItems(IEnumerable<ReaderChapterItem> chapterItems)
        {
            return (chapterItems ?? Enumerable.Empty<ReaderChapterItem>())
                .Where(chapter => chapter != null)
                .Select(chapter => new ReaderChapterItem
                {
                    Name = chapter.Name,
                    FolderPath = chapter.FolderPath,
                    FolderDepth = chapter.FolderDepth,
                    LastModifiedUtc = chapter.LastModifiedUtc,
                    Pages = new List<ReaderPageItem>()
                })
                .ToList();
        }

        private void CacheDownloadMissingChapterLinks(GalleryItem item, IEnumerable<string> chapterLinks)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Link) || chapterLinks == null)
            {
                return;
            }

            var nameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (_downloadChapterItemCache.TryGetValue(item.Link, out List<ReaderChapterItem> cached) && cached != null)
            {
                foreach (var ch in cached)
                {
                    if (ch != null && !string.IsNullOrWhiteSpace(ch.FolderPath) && !string.IsNullOrWhiteSpace(ch.Name))
                    {
                        nameMap[ch.FolderPath.Trim()] = ch.Name;
                    }
                }
            }

            List<ReaderChapterItem> chapterItems = chapterLinks
                .Where(link => !string.IsNullOrWhiteSpace(link))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(link => new ReaderChapterItem
                {
                    Name = nameMap.TryGetValue(link.Trim(), out string name) ? name : BuildDownloadChapterLabel(link),
                    FolderPath = link.Trim(),
                    Pages = new List<ReaderPageItem>()
                })
                .ToList();

            CacheDownloadMissingChapterItems(item, chapterItems);
        }

        private void CacheDownloadMissingChapterItems(GalleryItem item, IEnumerable<GalleryItem> chapters)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Link) || chapters == null)
            {
                return;
            }

            List<ReaderChapterItem> chapterItems = chapters
                .Where(chapter => chapter != null && !string.IsNullOrWhiteSpace(chapter.Link))
                .Select(chapter => new ReaderChapterItem
                {
                    Name = BuildDownloadChapterItemName(chapter.Link, chapter.Name),
                    FolderPath = chapter.Link.Trim(),
                    Pages = new List<ReaderPageItem>()
                })
                .ToList();

            CacheDownloadMissingChapterItems(item, chapterItems);
        }

        private void CacheDownloadMissingChapterItems(GalleryItem item, IEnumerable<ReaderChapterItem> chapterItems)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Link) || chapterItems == null)
            {
                return;
            }

            List<ReaderChapterItem> cachedItems = CloneReaderChapterItems(chapterItems
                .Where(chapter => chapter != null && !string.IsNullOrWhiteSpace(chapter.FolderPath))
                .ToList());
            _downloadChapterItemCache[item.Link] = cachedItems;

            if (_downloadMissingChapterRows.Count > 0)
            {
                if (Dispatcher.CheckAccess())
                {
                    RefreshDownloadMissingChapterRow(item, cachedItems);
                }
                else
                {
                    Dispatcher.BeginInvoke((Action)(() => RefreshDownloadMissingChapterRow(item, cachedItems)));
                }
            }

            PersistDownloadMissingChapterCacheNow();
            RequestGalleryListAutosave();
        }

        internal bool TryGetCachedDownloadChapterLinks(GalleryItem item, out List<string> chapterLinks)
        {
            chapterLinks = GetCachedDownloadChapterItems(item)
                .Select(chapter => chapter?.FolderPath?.Trim())
                .Where(link => !string.IsNullOrWhiteSpace(link))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return chapterLinks.Count > 0;
        }

        internal bool TryGetCachedDownloadChapterItems(GalleryItem item, out List<ReaderChapterItem> chapterItems)
        {
            chapterItems = GetCachedDownloadChapterItems(item);
            return chapterItems.Count > 0;
        }

        private List<ReaderChapterItem> GetCachedDownloadChapterItems(GalleryItem item)
        {
            if (item == null)
            {
                return new List<ReaderChapterItem>();
            }

            string itemLink = (item.Link ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(itemLink) &&
                _downloadChapterItemCache.TryGetValue(itemLink, out List<ReaderChapterItem> exactMatch) &&
                exactMatch != null &&
                exactMatch.Count > 0)
            {
                return CloneReaderChapterItems(exactMatch);
            }

            foreach (var entry in _downloadChapterItemCache)
            {
                string cachedLink = (entry.Key ?? string.Empty).Trim().TrimEnd('/');
                if (!string.IsNullOrWhiteSpace(itemLink) &&
                    string.Equals(cachedLink, itemLink.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) &&
                    entry.Value != null &&
                    entry.Value.Count > 0)
                {
                    return CloneReaderChapterItems(entry.Value);
                }
            }

            return new List<ReaderChapterItem>();
        }

        private void SubscribeDownloadMissingChapterRow(ReaderChapterIssueItem row)
        {
            if (row == null)
            {
                return;
            }

            row.PropertyChanged -= DownloadMissingChapterRow_PropertyChanged;
            row.PropertyChanged += DownloadMissingChapterRow_PropertyChanged;
        }

        private void UnsubscribeDownloadMissingChapterRow(ReaderChapterIssueItem row)
        {
            if (row == null)
            {
                return;
            }

            row.PropertyChanged -= DownloadMissingChapterRow_PropertyChanged;
        }

        private void DownloadMissingChapterRow_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (!(sender is ReaderChapterIssueItem row) || !string.Equals(e?.PropertyName, nameof(ReaderChapterIssueItem.IsChecked), StringComparison.Ordinal))
            {
                return;
            }

            GalleryItem item = FindGalleryItemForMissingChapterRow(null, row);
            if (item != null && item.IsChecked != row.IsChecked)
            {
                item.IsChecked = row.IsChecked;
            }

            if (!row.IsChecked)
            {
                return;
            }

            if (item != null)
            {
                QueueDownloadMissingChapterRescan(item);
            }
        }

        private void ClearDownloadMissingChapterCacheState()
        {
            foreach (ReaderChapterIssueItem row in _downloadMissingChapterRows.Where(row => row != null).ToList())
            {
                UnsubscribeDownloadMissingChapterRow(row);
            }

            _downloadChapterItemCache.Clear();
            _downloadMissingChapterPendingRescanKeys.Clear();
        }

        private void QueueDownloadMissingChapterRescan(GalleryItem item)
        {
            if (item == null)
            {
                return;
            }

            string key = BuildDownloadMissingChapterItemKey(item.Link, item.Name, GetDownloadMissingChapterDomainLabel(item));
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            _downloadMissingChapterPendingRescanKeys.Add(key);

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    EnsureDownloadMissingChapterRescanTimer();
                    _downloadMissingChapterRescanTimer?.Stop();
                    _downloadMissingChapterRescanTimer?.Start();
                }));
            }
            else
            {
                EnsureDownloadMissingChapterRescanTimer();
                _downloadMissingChapterRescanTimer?.Stop();
                _downloadMissingChapterRescanTimer?.Start();
            }
        }

        private void EnsureDownloadMissingChapterRescanTimer()
        {
            if (_downloadMissingChapterRescanTimer != null)
            {
                return;
            }

            _downloadMissingChapterRescanTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(650)
            };
            _downloadMissingChapterRescanTimer.Tick += async (s, e) =>
            {
                _downloadMissingChapterRescanTimer.Stop();
                if (_downloadMissingChapterScanInProgress || _downloadMissingChapterPendingRescanKeys.Count == 0)
                {
                    if (_downloadMissingChapterPendingRescanKeys.Count > 0)
                    {
                        _downloadMissingChapterRescanTimer.Start();
                    }
                    return;
                }

                List<GalleryItem> items = GetDownloadMissingChapterSourceItems()
                    .Where(item => item != null && _downloadMissingChapterPendingRescanKeys.Contains(BuildDownloadMissingChapterItemKey(item.Link, item.Name, GetDownloadMissingChapterDomainLabel(item))))
                    .ToList();
                _downloadMissingChapterPendingRescanKeys.Clear();
                if (items.Count > 0)
                {
                    await ScanDownloadMissingChaptersAsync(forceRefresh: true, explicitItems: items);
                }
            };
        }

        private static string BuildDownloadMissingChapterItemKey(string link, string name, string domain = null)
        {
            if (!string.IsNullOrWhiteSpace(link))
            {
                return "link:" + link.Trim().TrimEnd('/').ToLowerInvariant();
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                string cleanDomain = string.IsNullOrWhiteSpace(domain) ? string.Empty : domain.Trim().ToLowerInvariant();
                return "name:" + cleanDomain + "|" + name.Trim().ToLowerInvariant();
            }

            return string.Empty;
        }

        private bool IsSameDownloadMissingChapterBook(GalleryItem item, ReaderChapterIssueItem row)
        {
            string itemLink = (item?.Link ?? string.Empty).Trim();
            string rowLink = (row?.BookLink ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(itemLink) && !string.IsNullOrWhiteSpace(rowLink))
            {
                return string.Equals(itemLink.TrimEnd('/'), rowLink.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
            }

            if (!string.IsNullOrWhiteSpace(itemLink) || !string.IsNullOrWhiteSpace(rowLink))
            {
                return false;
            }

            string itemName = (item?.Name ?? string.Empty).Trim();
            string rowName = (row?.BookName ?? string.Empty).Trim();
            return !string.IsNullOrWhiteSpace(itemName) &&
                   string.Equals(itemName, rowName, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(GetDownloadMissingChapterDomainLabel(item), (row?.DomainLabel ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
        }

        internal void SaveDownloadMissingChapterCacheToFile(string path)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SaveDownloadMissingChapterCacheToFile(path));
                return;
            }

            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var payload = new DownloadMissingChapterCachePayload
                {
                    Books = GetDownloadMissingChapterSourceItems()
                        .Where(item => item != null)
                        .Select(BuildDownloadMissingChapterCacheBookState)
                        .Where(state => state != null)
                        .ToList()
                };

                var serializer = new DataContractJsonSerializer(typeof(DownloadMissingChapterCachePayload));
                using (var ms = new MemoryStream())
                {
                    serializer.WriteObject(ms, payload);
                    WriteTextFileAtomically(path, Encoding.UTF8.GetString(ms.ToArray()), Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Log($"Missing chapter cache save failed: {ex.Message}");
            }
        }

        internal void TryLoadDownloadMissingChapterCacheFromFile(string path)
        {
            try
            {
                ClearDownloadMissingChapterCacheState();
                _downloadMissingChapterRows.Clear();
                ClearGalleryMissingChapterStatuses();

                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return;
                }

                var serializer = new DataContractJsonSerializer(typeof(DownloadMissingChapterCachePayload));
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(File.ReadAllText(path, Encoding.UTF8))))
                {
                    var payload = serializer.ReadObject(ms) as DownloadMissingChapterCachePayload;
                    ApplyDownloadMissingChapterCachePayload(payload);
                }
            }
            catch (Exception ex)
            {
                Log($"Missing chapter cache load failed: {ex.Message}");
            }
        }

        private void MergeDownloadMissingChapterRowsFromGalleryStates(IEnumerable<GalleryItemState> states)
        {
            foreach (GalleryItemState state in states ?? Enumerable.Empty<GalleryItemState>())
            {
                if (state == null)
                {
                    continue;
                }

                GalleryItem item = FindGalleryItemForMissingChapterState(state.Link, state.Name, state.SourceDomain);
                if (item == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(item.MissingChapterStatusText) &&
                    !string.IsNullOrWhiteSpace(state.MissingChapterStatusText))
                {
                    item.MissingChapterStatusText = state.MissingChapterStatusText;
                    item.MissingChapterSortText = string.IsNullOrWhiteSpace(state.MissingChapterSortText) ? state.MissingChapterStatusText : state.MissingChapterSortText;
                    item.HasMissingChapterIssue = state.HasMissingChapterIssue;
                }

                bool hasSavedRow = !string.IsNullOrWhiteSpace(state.MissingChapterChapterLabel) ||
                                   !string.IsNullOrWhiteSpace(state.MissingChapterLabel) ||
                                   !string.IsNullOrWhiteSpace(state.MissingChapterDecimalLabel) ||
                                   !string.IsNullOrWhiteSpace(state.MissingChapterStatusText);
                if (!hasSavedRow)
                {
                    continue;
                }

                ReaderChapterIssueItem row = FindDownloadMissingChapterRow(item) ?? CreateDownloadMissingChapterPlaceholderRow(item);
                row.DomainLabel = GetDownloadMissingChapterDomainLabel(item);
                row.BookName = item.Name;
                row.BookLink = item.Link;
                row.ChapterLabel = state.MissingChapterChapterLabel ?? string.Empty;
                row.MissingChapterLabel = !string.IsNullOrWhiteSpace(state.MissingChapterLabel)
                    ? state.MissingChapterLabel
                    : (!string.IsNullOrWhiteSpace(state.MissingChapterStatusText) ? state.MissingChapterStatusText : string.Empty);
                row.DecimalChapterLabel = state.MissingChapterDecimalLabel ?? string.Empty;
                row.IsChecked = state.MissingChapterIsChecked;
                AddOrAttachDownloadMissingChapterRow(row, item);
                SyncGalleryMissingChapterStatus(item, row);
            }

            EnsureDownloadMissingChapterRowsFromGallery(forceSyncOrder: true);
        }

        private DownloadMissingChapterCacheBookState BuildDownloadMissingChapterCacheBookState(GalleryItem item)
        {
            if (item == null)
            {
                return null;
            }

            List<ReaderChapterItem> cachedItems = GetCachedDownloadChapterItems(item);
            ReaderChapterIssueItem row = FindDownloadMissingChapterRow(item);
            if ((row == null || !IsDownloadMissingChapterRowScanned(row)) && cachedItems.Count == 0)
            {
                return null;
            }

            row = row ?? BuildDownloadMissingChapterSummaryRow(item, new ReaderMangaItem
            {
                Name = item.Name,
                Chapters = cachedItems
            });

            return new DownloadMissingChapterCacheBookState
            {
                DomainLabel = row?.DomainLabel ?? GetDownloadMissingChapterDomainLabel(item),
                BookName = row?.BookName ?? item.Name,
                BookLink = row?.BookLink ?? item.Link,
                ChapterLabel = row?.ChapterLabel ?? string.Empty,
                MissingChapterLabel = row?.MissingChapterLabel ?? string.Empty,
                DecimalChapterLabel = row?.DecimalChapterLabel ?? string.Empty,
                IsChecked = row?.IsChecked ?? item.IsChecked,
                Chapters = cachedItems
                    .Where(chapter => chapter != null && !string.IsNullOrWhiteSpace(chapter.FolderPath))
                    .Select(chapter => new DownloadMissingChapterCacheChapterState
                    {
                        Name = chapter.Name,
                        Link = chapter.FolderPath
                    })
                    .ToList()
            };
        }

        private void ApplyDownloadMissingChapterCachePayload(DownloadMissingChapterCachePayload payload)
        {
            foreach (DownloadMissingChapterCacheBookState state in payload?.Books ?? Enumerable.Empty<DownloadMissingChapterCacheBookState>())
            {
                GalleryItem item = FindGalleryItemForMissingChapterCacheState(state);
                if (item == null)
                {
                    continue;
                }

                List<ReaderChapterItem> cachedItems = CloneReaderChapterItems((state.Chapters ?? new List<DownloadMissingChapterCacheChapterState>())
                    .Where(chapter => chapter != null && !string.IsNullOrWhiteSpace(chapter.Link))
                    .Select(chapter => new ReaderChapterItem
                    {
                        Name = string.IsNullOrWhiteSpace(chapter.Name) ? BuildDownloadChapterLabel(chapter.Link) : chapter.Name,
                        FolderPath = chapter.Link.Trim(),
                        Pages = new List<ReaderPageItem>()
                    })
                    .ToList());
                _downloadChapterItemCache[item.Link] = cachedItems;

                ReaderChapterIssueItem row = FindDownloadMissingChapterRow(item) ?? CreateDownloadMissingChapterPlaceholderRow(item);
                row.DomainLabel = state.DomainLabel;
                row.BookName = string.IsNullOrWhiteSpace(state.BookName) ? item.Name : state.BookName;
                row.BookLink = string.IsNullOrWhiteSpace(state.BookLink) ? item.Link : state.BookLink;
                row.ChapterLabel = state.ChapterLabel ?? string.Empty;
                row.MissingChapterLabel = state.MissingChapterLabel ?? string.Empty;
                row.DecimalChapterLabel = state.DecimalChapterLabel ?? string.Empty;
                row.IsChecked = state.IsChecked;
                AddOrAttachDownloadMissingChapterRow(row, item);
                SyncGalleryMissingChapterStatus(item, row);
            }

            EnsureDownloadMissingChapterRowsFromGallery(forceSyncOrder: true);
        }

        private GalleryItem FindGalleryItemForMissingChapterCacheState(DownloadMissingChapterCacheBookState state)
        {
            if (!Dispatcher.CheckAccess())
            {
                return Dispatcher.Invoke(() => FindGalleryItemForMissingChapterCacheState(state));
            }

            return FindGalleryItemForMissingChapterState(state?.BookLink, state?.BookName, state?.DomainLabel);
        }

        private GalleryItem FindGalleryItemForMissingChapterState(string link, string name, string domain)
        {
            string cleanLink = (link ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(cleanLink))
            {
                return _scrapedItems.FirstOrDefault(item =>
                    item != null &&
                    string.Equals((item.Link ?? string.Empty).Trim().TrimEnd('/'), cleanLink.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
            }

            string cleanName = (name ?? string.Empty).Trim();
            string cleanDomain = (domain ?? string.Empty).Trim();
            return _scrapedItems.FirstOrDefault(item =>
                item != null &&
                !string.IsNullOrWhiteSpace(cleanName) &&
                string.Equals((item.Name ?? string.Empty).Trim(), cleanName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(GetDownloadMissingChapterDomainLabel(item), cleanDomain, StringComparison.OrdinalIgnoreCase));
        }

        private void RefreshDownloadMissingChapterRow(GalleryItem item, IList<ReaderChapterItem> chapterItems, bool persist = true, bool reorder = true)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => RefreshDownloadMissingChapterRow(item, chapterItems, persist, reorder));
                return;
            }

            if (item == null)
            {
                return;
            }

            ReaderChapterIssueItem summaryRow = BuildDownloadMissingChapterSummaryRow(item, new ReaderMangaItem
            {
                Name = item.Name,
                Chapters = chapterItems != null ? CloneReaderChapterItems(chapterItems) : new List<ReaderChapterItem>()
            });
            if (summaryRow != null)
            {
                ReaderChapterIssueItem row = FindDownloadMissingChapterRow(item) ?? CreateDownloadMissingChapterPlaceholderRow(item);
                row.DomainLabel = summaryRow.DomainLabel;
                row.BookName = summaryRow.BookName;
                row.BookLink = summaryRow.BookLink;
                row.ChapterLabel = summaryRow.ChapterLabel;
                row.MissingChapterLabel = summaryRow.MissingChapterLabel;
                row.DecimalChapterLabel = summaryRow.DecimalChapterLabel;
                row.IsChecked = summaryRow.IsChecked;
                AddOrAttachDownloadMissingChapterRow(row, item);
                SyncGalleryMissingChapterStatus(item, row);
                if (reorder)
                {
                    ReorderDownloadMissingChapterRowsToMatchGallery();
                }
            }

            if (persist && !_downloadMissingChapterBulkRefreshing)
            {
                PersistDownloadMissingChapterCacheNow();
                RequestGalleryListAutosave();
            }
        }

        private void PersistDownloadMissingChapterCacheNow()
        {
            SaveDownloadMissingChapterCacheToFile(GetDownloadMissingChapterCachePath(CurrentGalleryListPath));
        }

        private ReaderChapterIssueItem CreateDownloadMissingChapterPlaceholderRow(GalleryItem item)
        {
            return new ReaderChapterIssueItem
            {
                DomainLabel = GetDownloadMissingChapterDomainLabel(item),
                BookName = item?.Name ?? string.Empty,
                BookLink = item?.Link ?? string.Empty,
                ChapterLabel = string.Empty,
                MissingChapterLabel = string.Empty,
                DecimalChapterLabel = string.Empty,
                IsChecked = item?.IsChecked ?? true
            };
        }

        private void AddOrAttachDownloadMissingChapterRow(ReaderChapterIssueItem row, GalleryItem item)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => AddOrAttachDownloadMissingChapterRow(row, item));
                return;
            }

            if (row == null)
            {
                return;
            }

            if (!_downloadMissingChapterRows.Contains(row))
            {
                SubscribeDownloadMissingChapterRow(row);
                _downloadMissingChapterRows.Add(row);
                row.RowNumber = _downloadMissingChapterRows.Count;
            }

            if (item != null)
            {
                row.DomainLabel = string.IsNullOrWhiteSpace(row.DomainLabel) ? GetDownloadMissingChapterDomainLabel(item) : row.DomainLabel;
                row.BookName = string.IsNullOrWhiteSpace(row.BookName) ? item.Name : row.BookName;
                row.BookLink = string.IsNullOrWhiteSpace(row.BookLink) ? item.Link : row.BookLink;
            }
        }

        internal void EnsureDownloadMissingChapterRowsFromGallery(bool forceSyncOrder)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => EnsureDownloadMissingChapterRowsFromGallery(forceSyncOrder));
                return;
            }

            List<GalleryItem> sourceItems = GetDownloadMissingChapterSourceItems();
            var validKeys = new HashSet<string>(sourceItems.Select(item => BuildDownloadMissingChapterItemKey(item?.Link, item?.Name, GetDownloadMissingChapterDomainLabel(item))).Where(key => !string.IsNullOrWhiteSpace(key)), StringComparer.OrdinalIgnoreCase);

            for (int i = _downloadMissingChapterRows.Count - 1; i >= 0; i--)
            {
                ReaderChapterIssueItem row = _downloadMissingChapterRows[i];
                string rowKey = BuildDownloadMissingChapterItemKey(row?.BookLink, row?.BookName, row?.DomainLabel);
                if (string.IsNullOrWhiteSpace(rowKey) || !validKeys.Contains(rowKey))
                {
                    UnsubscribeDownloadMissingChapterRow(row);
                    _downloadMissingChapterRows.RemoveAt(i);
                }
            }

            foreach (GalleryItem item in sourceItems.Where(item => item != null))
            {
                ReaderChapterIssueItem row = FindDownloadMissingChapterRow(item);
                if (row == null)
                {
                    row = CreateDownloadMissingChapterPlaceholderRow(item);
                    AddOrAttachDownloadMissingChapterRow(row, item);
                }
                else
                {
                    row.DomainLabel = GetDownloadMissingChapterDomainLabel(item);
                    row.BookName = item.Name;
                    row.BookLink = item.Link;
                }
            }

            ReorderDownloadMissingChapterRowsToMatchGallery(forceSyncOrder);
        }

        private void ClearDownloadMissingChapterGridSortState()
        {
            if (_downloadMissingChapterGrid == null)
            {
                return;
            }

            ICollectionView view = CollectionViewSource.GetDefaultView(_downloadMissingChapterGrid.ItemsSource);
            view?.SortDescriptions.Clear();
            foreach (DataGridColumn column in _downloadMissingChapterGrid.Columns)
            {
                column.SortDirection = null;
            }
        }

        private void ReorderDownloadMissingChapterRowsToMatchGallery(bool force = false)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => ReorderDownloadMissingChapterRowsToMatchGallery(force));
                return;
            }

            if (_downloadMissingChapterRows.Count <= 1)
            {
                ApplyDownloadMissingChapterRowNumbers();
                return;
            }

            if (_downloadMissingChapterManualSortActive && !force)
            {
                return;
            }

            if (force)
            {
                _downloadMissingChapterManualSortActive = false;
                ClearDownloadMissingChapterGridSortState();
            }

            List<ReaderChapterIssueItem> currentRows = _downloadMissingChapterRows.Where(row => row != null).ToList();
            var orderedRows = new List<ReaderChapterIssueItem>(currentRows.Count);

            foreach (GalleryItem item in GetDownloadMissingChapterSourceItems().Where(candidate => candidate != null))
            {
                ReaderChapterIssueItem match = currentRows.FirstOrDefault(row =>
                    row != null && IsSameDownloadMissingChapterBook(item, row));
                if (match != null && !orderedRows.Contains(match))
                {
                    orderedRows.Add(match);
                }
            }

            foreach (ReaderChapterIssueItem row in currentRows)
            {
                if (!orderedRows.Contains(row))
                {
                    orderedRows.Add(row);
                }
            }

            bool sameOrder = orderedRows.Count == _downloadMissingChapterRows.Count;
            if (sameOrder)
            {
                for (int i = 0; i < orderedRows.Count; i++)
                {
                    if (!ReferenceEquals(orderedRows[i], _downloadMissingChapterRows[i]))
                    {
                        sameOrder = false;
                        break;
                    }
                }
            }

            if (sameOrder)
            {
                ApplyDownloadMissingChapterRowNumbers();
                return;
            }

            _downloadMissingChapterRows.Clear();
            foreach (ReaderChapterIssueItem row in orderedRows)
            {
                _downloadMissingChapterRows.Add(row);
            }
            ApplyDownloadMissingChapterRowNumbers();
        }

        private void ApplyDownloadMissingChapterRowNumbers()
        {
            for (int i = 0; i < _downloadMissingChapterRows.Count; i++)
            {
                if (_downloadMissingChapterRows[i] != null)
                {
                    _downloadMissingChapterRows[i].RowNumber = i + 1;
                }
            }

            SafeRefreshMissingChaptersView();
        }

        private void ClearGalleryMissingChapterStatuses()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(ClearGalleryMissingChapterStatuses);
                return;
            }

            foreach (GalleryItem item in _scrapedItems.Where(item => item != null))
            {
                item.MissingChapterStatusText = string.Empty;
                item.MissingChapterSortText = string.Empty;
                item.MissingChapterLatestChapterText = string.Empty;
                item.HasMissingChapterIssue = false;
            }
        }

        private void SyncAllGalleryMissingChapterStatuses()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(SyncAllGalleryMissingChapterStatuses);
                return;
            }

            if (_downloadMissingChapterRows.Count == 0)
            {
                ClearGalleryMissingChapterStatuses();
                return;
            }

            foreach (GalleryItem item in _scrapedItems.Where(item => item != null))
            {
                ReaderChapterIssueItem row = _downloadMissingChapterRows.FirstOrDefault(candidate =>
                    candidate != null && IsSameDownloadMissingChapterBook(item, candidate));
                SyncGalleryMissingChapterStatus(item, row);
            }
        }

        private void SyncGalleryMissingChapterStatus(GalleryItem item, ReaderChapterIssueItem row)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SyncGalleryMissingChapterStatus(item, row));
                return;
            }

            GalleryItem target = FindGalleryItemForMissingChapterRow(item, row);
            if (target == null)
            {
                return;
            }

            bool hasIssue = row != null &&
                            !string.IsNullOrWhiteSpace(row.MissingChapterLabel) &&
                            !IsDownloadMissingChapterCompleteLabel(row.MissingChapterLabel);

            target.MissingChapterStatusText = row == null
                ? string.Empty
                : hasIssue
                    ? row.MissingChapterLabel
                    : GetDownloadCompleteChapterText();
            target.MissingChapterSortText = target.MissingChapterStatusText;
            if (string.IsNullOrWhiteSpace(target.MissingChapterLatestChapterText))
            {
                target.MissingChapterLatestChapterText = row?.ChapterLabel ?? string.Empty;
            }
            target.HasMissingChapterIssue = hasIssue;
        }

        private GalleryItem FindGalleryItemForMissingChapterRow(GalleryItem item, ReaderChapterIssueItem row)
        {
            if (!Dispatcher.CheckAccess())
            {
                return Dispatcher.Invoke(() => FindGalleryItemForMissingChapterRow(item, row));
            }

            if (item != null)
            {
                return _scrapedItems.FirstOrDefault(candidate => ReferenceEquals(candidate, item) || IsSameDownloadMissingChapterBook(candidate, row));
            }

            return _scrapedItems.FirstOrDefault(candidate => IsSameDownloadMissingChapterBook(candidate, row));
        }

        internal bool JumpToDownloadMissingChapterRow(GalleryItem item)
        {
            if (item == null || _downloadMissingChapterGrid == null)
            {
                return false;
            }

            ReaderChapterIssueItem target = _downloadMissingChapterRows.FirstOrDefault(row =>
                row != null && IsSameDownloadMissingChapterBook(item, row));
            if (target == null)
            {
                return false;
            }

            SelectAppSection(AppSection.Download);
            int missingTabIndex = GetDownloadMissingChapterTabIndex();
            if (missingTabIndex >= 0)
            {
                tabDownloadRoot.SelectedIndex = missingTabIndex;
            }

            _downloadMissingChapterGrid.SelectedItems.Clear();
            _downloadMissingChapterGrid.SelectedItem = target;
            _downloadMissingChapterGrid.CurrentItem = target;
            _downloadMissingChapterGrid.ScrollIntoView(target);
            _downloadMissingChapterGrid.Focus();
            return true;
        }

        private IEnumerable<ReaderChapterIssueItem> GetDownloadMissingChapterSelectedRows()
        {
            if (_downloadMissingChapterGrid == null)
            {
                return Enumerable.Empty<ReaderChapterIssueItem>();
            }

            return _downloadMissingChapterGrid.SelectedItems.Cast<object>().OfType<ReaderChapterIssueItem>();
        }

        private IEnumerable<ReaderChapterIssueItem> GetDownloadMissingChapterCheckedRows(bool includeAllWhenNoneChecked)
        {
            List<ReaderChapterIssueItem> checkedRows = _downloadMissingChapterRows
                .Where(row => row != null && row.IsChecked)
                .ToList();
            if (checkedRows.Count > 0 || !includeAllWhenNoneChecked)
            {
                return checkedRows;
            }

            return _downloadMissingChapterRows.Where(row => row != null).ToList();
        }

        private void CopyDownloadMissingChapterRows(IEnumerable<ReaderChapterIssueItem> rows, string successText)
        {
            List<string> lines = BuildDownloadMissingChapterClipboardLines(rows).ToList();
            if (lines.Count == 0)
            {
                if (_downloadMissingChapterStatusText != null)
                {
                    _downloadMissingChapterStatusText.Text = _isVietnameseUi ? "Không có chap số nguyên thiếu để copy." : "No missing integer chapters to copy.";
                }
                return;
            }

            Clipboard.SetText(string.Join(Environment.NewLine, lines));
            if (_downloadMissingChapterStatusText != null)
            {
                _downloadMissingChapterStatusText.Text = successText;
            }
        }

        private IEnumerable<string> BuildDownloadMissingChapterClipboardLines(IEnumerable<ReaderChapterIssueItem> rows)
        {
            System.Diagnostics.Debug.Assert(JoinDownloadMissingChapterRanges(new[] { "182-183", "195-197", "200" }) == "182-183;195-197;200");

            return (rows ?? Enumerable.Empty<ReaderChapterIssueItem>())
                .Where(row => row != null && IsDownloadMissingChapterIssueRow(row))
                .Select(row =>
                {
                    string text = $"{row.BookName}: {BuildDownloadMissingChapterCopyLabel(row)}";
                    if (!string.IsNullOrWhiteSpace(row.DecimalChapterLabel))
                    {
                        text += $" | decimal: {row.DecimalChapterLabel}";
                    }

                    return text;
                })
                .Where(text => !string.IsNullOrWhiteSpace(text));
        }

        private string BuildDownloadMissingChapterCopyLabel(ReaderChapterIssueItem row)
        {
            if (row == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(row.MissingChapterLabel) &&
                !IsDownloadMissingChapterCompleteLabel(row.MissingChapterLabel))
            {
                string[] ranges = row.MissingChapterLabel
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => value.Trim())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray();
                if (ranges.Length > 0)
                {
                    return JoinDownloadMissingChapterRanges(ranges);
                }
            }

            return row.MissingChapterLabel ?? string.Empty;
        }

        private static string JoinDownloadMissingChapterRanges(IEnumerable<string> ranges)
        {
            return string.Join(";", (ranges ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim()));
        }

        private void DownloadMissingChapterGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (TryGetDownloadMissingChapterCurrentRow(out ReaderChapterIssueItem row))
            {
                int? columnIndex = GetDownloadMissingChapterColumnDisplayIndex(e.OriginalSource as DependencyObject);
                if (columnIndex == 1 && TryJumpToExtractedGalleryRowFromMissingChapter(row))
                {
                    e.Handled = true;
                    return;
                }

                if (columnIndex == 2)
                {
                    OpenDownloadMissingChapterBookLink(row);
                    e.Handled = true;
                }
            }
        }

        private int? GetDownloadMissingChapterColumnDisplayIndex(DependencyObject source)
        {
            while (source != null && !(source is DataGridCell))
            {
                source = VisualTreeHelper.GetParent(source);
            }

            if (source is DataGridCell cell && cell.Column != null)
            {
                return cell.Column.DisplayIndex;
            }

            return null;
        }

        private bool TryJumpToExtractedGalleryRowFromMissingChapter(ReaderChapterIssueItem row)
        {
            if (row == null)
            {
                return false;
            }

            GalleryItem target = _scrapedItems.FirstOrDefault(item =>
                item != null && IsSameDownloadMissingChapterBook(item, row));
            if (target == null)
            {
                return false;
            }

            SelectAppSection(AppSection.Download);
            if (tabDownloadRoot != null)
            {
                tabDownloadRoot.SelectedIndex = GetDownloadMangaTabIndex();
            }

            dgResults.SelectedItems.Clear();
            dgResults.SelectedItem = target;
            dgResults.CurrentItem = target;
            dgResults.ScrollIntoView(target);
            dgResults.Focus();
            return true;
        }

        private void DownloadMissingChapterGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject source = e.OriginalSource as DependencyObject;
            while (source != null && !(source is DataGridRow))
            {
                source = VisualTreeHelper.GetParent(source);
            }

            if (source is DataGridRow row)
            {
                if (!row.IsSelected)
                {
                    _downloadMissingChapterGrid?.SelectedItems.Clear();
                }
                row.IsSelected = true;
                row.Focus();
            }
        }

        private void DownloadMissingChapterGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            _downloadMissingChapterManualSortActive = true;
        }

        private void DownloadMissingChapterGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (IsTypingInEditableTextBox() || _downloadMissingChapterGrid == null || _downloadMissingChapterGrid.Items.Count == 0)
            {
                return;
            }

            if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                _downloadMissingChapterGrid.SelectAll();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                CopyDownloadMissingChapterLinks(GetDownloadMissingChapterSelectedRows());
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Delete)
            {
                var selectedRows = _downloadMissingChapterGrid.SelectedItems.Cast<ReaderChapterIssueItem>().ToList();
                if (selectedRows.Count > 0)
                {
                    var itemsToRemove = new List<GalleryItem>();
                    foreach (var row in selectedRows)
                    {
                        var item = FindGalleryItemForMissingChapterRow(null, row);
                        if (item != null)
                        {
                            itemsToRemove.Add(item);
                        }
                        
                        UnsubscribeDownloadMissingChapterRow(row);
                        _downloadMissingChapterRows.Remove(row);
                    }
                    
                    foreach (var item in itemsToRemove)
                    {
                        _scrapedItems.Remove(item);
                    }
                    
                    lblLinkCount.Text = _scrapedItems.Count.ToString();
                    Log($"Deleted {selectedRows.Count} item(s) from check missing chapter tab.");
                    lblStatus.Text = $"Deleted {selectedRows.Count} item(s).";
                    
                    RecalculateDuplicates();
                    PersistDownloadMissingChapterCacheNow();
                    RequestGalleryListAutosave();
                    e.Handled = true;
                }
                return;
            }

            if (e.Key == Key.Home || e.Key == Key.End)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ||
                    (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    return;
                }

                int targetIndex = e.Key == Key.Home ? 0 : _downloadMissingChapterGrid.Items.Count - 1;
                _downloadMissingChapterGrid.SelectedIndex = targetIndex;
                _downloadMissingChapterGrid.ScrollIntoView(_downloadMissingChapterGrid.Items[targetIndex]);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Space)
            {
                List<ReaderChapterIssueItem> rows = GetDownloadMissingChapterSelectedRows().ToList();
                if (rows.Count == 0 && TryGetDownloadMissingChapterCurrentRow(out ReaderChapterIssueItem currentRow))
                {
                    rows.Add(currentRow);
                }

                ToggleDownloadMissingChapterRowsChecked(rows);
                e.Handled = true;
            }
        }

        private void DownloadMissingChapterGrid_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (_downloadMissingChapterGrid == null || string.IsNullOrEmpty(e.Text) || IsTypingInEditableTextBox())
            {
                return;
            }

            DateTime now = DateTime.Now;
            if ((now - _downloadMissingChapterLastKeyPressTime).TotalMilliseconds > 1000)
            {
                _downloadMissingChapterSearchBuffer = string.Empty;
            }

            _downloadMissingChapterLastKeyPressTime = now;
            _downloadMissingChapterSearchBuffer += e.Text;

            List<ReaderChapterIssueItem> rows = _downloadMissingChapterGrid.Items.Cast<object>().OfType<ReaderChapterIssueItem>().ToList();
            ReaderChapterIssueItem match = rows.FirstOrDefault(row =>
                !string.IsNullOrWhiteSpace(row?.BookName) &&
                row.BookName.StartsWith(_downloadMissingChapterSearchBuffer, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                match = rows.FirstOrDefault(row =>
                    (!string.IsNullOrWhiteSpace(row?.BookName) && row.BookName.IndexOf(_downloadMissingChapterSearchBuffer, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrWhiteSpace(row?.DomainLabel) && row.DomainLabel.IndexOf(_downloadMissingChapterSearchBuffer, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            if (match != null)
            {
                _downloadMissingChapterGrid.SelectedItems.Clear();
                _downloadMissingChapterGrid.SelectedItem = match;
                _downloadMissingChapterGrid.CurrentItem = match;
                _downloadMissingChapterGrid.ScrollIntoView(match);
            }

            e.Handled = true;
        }

        private bool TryGetDownloadMissingChapterCurrentRow(out ReaderChapterIssueItem row)
        {
            row = _downloadMissingChapterGrid?.CurrentItem as ReaderChapterIssueItem;
            if (row == null)
            {
                row = _downloadMissingChapterGrid?.SelectedItem as ReaderChapterIssueItem;
            }

            return row != null;
        }

        private void OpenDownloadMissingChapterBookLink(ReaderChapterIssueItem row)
        {
            if (row == null || string.IsNullOrWhiteSpace(row.BookLink))
            {
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = row.BookLink,
                    UseShellExecute = true
                });
                if (_downloadMissingChapterStatusText != null)
                {
                    _downloadMissingChapterStatusText.Text = _isVietnameseUi ? "Đã mở link truyện." : "Opened book link.";
                }
            }
            catch (Exception ex)
            {
                if (_downloadMissingChapterStatusText != null)
                {
                    _downloadMissingChapterStatusText.Text = _isVietnameseUi ? "Mở link lỗi: " + ex.Message : "Open link failed: " + ex.Message;
                }
            }
        }

        private void SetDownloadMissingChapterRowsChecked(IEnumerable<ReaderChapterIssueItem> rows, bool value)
        {
            foreach (ReaderChapterIssueItem row in rows ?? Enumerable.Empty<ReaderChapterIssueItem>())
            {
                row.IsChecked = value;
            }
        }

        private void ToggleDownloadMissingChapterRowsChecked(IEnumerable<ReaderChapterIssueItem> rows)
        {
            List<ReaderChapterIssueItem> rowList = (rows ?? Enumerable.Empty<ReaderChapterIssueItem>()).Where(row => row != null).ToList();
            if (rowList.Count == 0)
            {
                return;
            }

            bool target = !rowList[0].IsChecked;
            foreach (ReaderChapterIssueItem row in rowList)
            {
                row.IsChecked = target;
            }
        }

        private void DownloadMissingChapterOpenLink_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetDownloadMissingChapterCurrentRow(out ReaderChapterIssueItem row))
            {
                OpenDownloadMissingChapterBookLink(row);
            }
        }

        private static bool HasMissingEarlyIntegerChapter(IList<ReaderChapterItem> chapters)
        {
            if (chapters == null || chapters.Count == 0)
            {
                return true;
            }

            HashSet<int> numbers = new HashSet<int>(chapters
                .Where(chapter => chapter != null && !chapter.IsDecimalChapter && chapter.ParsedChapterNumber.HasValue)
                .SelectMany(chapter => EnumerateDownloadMissingChapterIntegers(chapter)));
            return !numbers.Contains(1) || !numbers.Contains(2) || !numbers.Contains(3);
        }

        private void DownloadMissingChapterCopyLink_Click(object sender, RoutedEventArgs e)
        {
            CopyDownloadMissingChapterLinks(GetDownloadMissingChapterSelectedRows());
        }

        private void DownloadMissingChapterCopyInteger_Click(object sender, RoutedEventArgs e)
        {
            CopyDownloadMissingChapterField(GetDownloadMissingChapterSelectedRows(), row => row.MissingChapterLabel, _isVietnameseUi ? "Đã copy chap số nguyên thiếu." : "Copied missing integer chapters.");
        }

        private void DownloadMissingChapterCopyDecimal_Click(object sender, RoutedEventArgs e)
        {
            CopyDownloadMissingChapterField(GetDownloadMissingChapterSelectedRows(), row => row.DecimalChapterLabel, _isVietnameseUi ? "Đã copy chap thập phân." : "Copied decimal chapters.");
        }

        private void DownloadMissingChapterCheckSelected_Click(object sender, RoutedEventArgs e)
        {
            SetDownloadMissingChapterRowsChecked(GetDownloadMissingChapterSelectedRows(), true);
        }

        private void DownloadMissingChapterUncheckSelected_Click(object sender, RoutedEventArgs e)
        {
            SetDownloadMissingChapterRowsChecked(GetDownloadMissingChapterSelectedRows(), false);
        }

        private void DownloadMissingChapterToggleSelected_Click(object sender, RoutedEventArgs e)
        {
            ToggleDownloadMissingChapterRowsChecked(GetDownloadMissingChapterSelectedRows());
        }

        private void DownloadMissingChapterCheckAll_Click(object sender, RoutedEventArgs e)
        {
            SetDownloadMissingChapterRowsChecked(_downloadMissingChapterRows, true);
        }

        private void DownloadMissingChapterUncheckAll_Click(object sender, RoutedEventArgs e)
        {
            SetDownloadMissingChapterRowsChecked(_downloadMissingChapterRows, false);
        }

        private void DownloadMissingChapterCopySelected_Click(object sender, RoutedEventArgs e)
        {
            CopyDownloadMissingChapterRows(GetDownloadMissingChapterCheckedRows(includeAllWhenNoneChecked: false), _isVietnameseUi ? "Đã copy chap số nguyên thiếu đã chọn." : "Copied selected missing integer chapters.");
        }

        private void DownloadMissingChapterCopyAllChecked_Click(object sender, RoutedEventArgs e)
        {
            CopyDownloadMissingChapterRows(_downloadMissingChapterRows, _isVietnameseUi ? "Đã copy chap số nguyên thiếu của mọi truyện." : "Copied all book's missing integer chapter.");
        }

        private void CopyDownloadMissingChapterLinks(IEnumerable<ReaderChapterIssueItem> rows)
        {
            CopyDownloadMissingChapterField(rows, row => row.BookLink, _isVietnameseUi ? "Đã copy link truyện." : "Copied book links.");
        }

        private void CopyDownloadMissingChapterField(IEnumerable<ReaderChapterIssueItem> rows, Func<ReaderChapterIssueItem, string> selector, string successMessage)
        {
            string text = string.Join(Environment.NewLine, (rows ?? Enumerable.Empty<ReaderChapterIssueItem>())
                .Where(row => row != null)
                .Select(selector)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(text))
            {
                if (_downloadMissingChapterStatusText != null)
                {
                    _downloadMissingChapterStatusText.Text = _isVietnameseUi ? "Không có dữ liệu để copy." : "Nothing to copy.";
                }
                return;
            }

            Clipboard.SetText(text);
            if (_downloadMissingChapterStatusText != null)
            {
                _downloadMissingChapterStatusText.Text = successMessage;
            }
        }

        internal void RemoveDownloadMissingChapterRow(GalleryItem item)
        {
            RemoveDownloadMissingChapterRows(item == null ? Enumerable.Empty<GalleryItem>() : new[] { item });
        }

        internal void RemoveDownloadMissingChapterRows(IEnumerable<GalleryItem> items)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => RemoveDownloadMissingChapterRows(items));
                return;
            }

            List<GalleryItem> targets = (items ?? Enumerable.Empty<GalleryItem>())
                .Where(item => item != null)
                .Distinct()
                .ToList();
            if (targets.Count == 0)
            {
                return;
            }

            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (GalleryItem item in targets)
            {
                string itemLink = (item.Link ?? string.Empty).Trim();
                string itemName = (item.Name ?? string.Empty).Trim();
                string itemKey = BuildDownloadMissingChapterItemKey(itemLink, itemName, GetDownloadMissingChapterDomainLabel(item));
                if (!string.IsNullOrWhiteSpace(itemKey))
                {
                    keys.Add(itemKey);
                    _downloadMissingChapterPendingRescanKeys.Remove(itemKey);
                }

                if (!string.IsNullOrWhiteSpace(itemLink))
                {
                    _downloadChapterItemCache.Remove(itemLink);
                }
            }

            for (int i = _downloadMissingChapterRows.Count - 1; i >= 0; i--)
            {
                ReaderChapterIssueItem row = _downloadMissingChapterRows[i];
                if (row == null)
                {
                    continue;
                }

                string rowKey = BuildDownloadMissingChapterItemKey(row.BookLink, row.BookName, row.DomainLabel);
                if (!string.IsNullOrWhiteSpace(rowKey) && keys.Contains(rowKey))
                {
                    UnsubscribeDownloadMissingChapterRow(row);
                    _downloadMissingChapterRows.RemoveAt(i);
                }
            }

            ReorderDownloadMissingChapterRowsToMatchGallery();
            foreach (GalleryItem item in targets)
            {
                item.MissingChapterStatusText = string.Empty;
                item.MissingChapterSortText = string.Empty;
                item.MissingChapterLatestChapterText = string.Empty;
                item.HasMissingChapterIssue = false;
            }
            PersistDownloadMissingChapterCacheNow();
            RequestGalleryListAutosave(0);
        }

        private bool ContainsGalleryItem(GalleryItem item)
        {
            return item != null && GetDownloadMissingChapterSourceItems().Any(candidate => ReferenceEquals(candidate, item));
        }

        private static bool IsDirectChapterLink(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            string lowerUrl = url.Trim().ToLowerInvariant();
            return lowerUrl.Contains("/chuong-") ||
                   lowerUrl.Contains("/chap-") ||
                   lowerUrl.Contains("/doc-truyen-tranh/") ||
                   lowerUrl.Contains("/chapter-");
        }

        private string BuildDownloadChapterLabel(string link)
        {
            if (string.IsNullOrWhiteSpace(link))
            {
                return string.Empty;
            }

            double parsedNumber = 0d;
            bool hasParsedNumber = TryParseDownloadChapterNumberFromLink(link, out parsedNumber);
            try
            {
                // keep flow below
            }
            catch
            {
                hasParsedNumber = false;
            }

            if (hasParsedNumber)
            {
                double rounded = Math.Round(parsedNumber);
                bool isInteger = Math.Abs(parsedNumber - rounded) < 0.0001d;
                return isInteger
                    ? "chap " + ((int)rounded).ToString("00")
                    : "chap " + parsedNumber.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            }

            if (link.IndexOf("truyenqq", StringComparison.OrdinalIgnoreCase) >= 0 ||
                link.IndexOf("qquyen", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // ponytail: truyenqq book slug carries book-id at tail; when chap parse fails, never fall back to that id.
                return "chap ?";
            }

            string candidate = link.Trim();
            if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri uri))
            {
                string[] segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length > 0)
                {
                    candidate = segments[segments.Length - 1];
                }
            }

            candidate = WebUtility.UrlDecode(candidate);
            candidate = Path.GetFileNameWithoutExtension(candidate);
            candidate = candidate.Replace('-', ' ').Replace('_', ' ').Trim();
            return string.IsNullOrWhiteSpace(candidate) ? link.Trim() : candidate;
        }

        private string BuildDownloadChapterItemName(string link, string fallbackName)
        {
            string cleanFallback = string.IsNullOrWhiteSpace(fallbackName) ? string.Empty : CompactSingleLine(fallbackName);
            if (!string.IsNullOrWhiteSpace(cleanFallback) &&
                TryParseReaderChapterNumber(cleanFallback, out _, out _))
            {
                return cleanFallback;
            }

            string chapterLabel = BuildDownloadChapterLabel(link);
            if (TryParseDownloadChapterNumberFromLink(link, out _))
            {
                return chapterLabel;
            }

            return string.IsNullOrWhiteSpace(cleanFallback) ? chapterLabel : cleanFallback;
        }

        private bool TryParseChapterNumberFromChapterToken(string link, out double number)
        {
            number = 0d;
            if (string.IsNullOrWhiteSpace(link))
            {
                return false;
            }

            string normalizedLink = link.Trim();
            if (Uri.TryCreate(normalizedLink, UriKind.Absolute, out Uri uri))
            {
                normalizedLink = uri.AbsolutePath;
            }

            Match strictMatch = Regex.Match(
                normalizedLink,
                @"(?:^|[/-])(?:chap|chapter|chuong|trang)(?:[-_/ ]+)?(?<num>\d+(?:[.,]\d+)?)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (strictMatch.Success)
            {
                string strictToken = strictMatch.Groups["num"].Value.Replace(',', '.');
                if (double.TryParse(strictToken, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out number))
                {
                    return number > 0d;
                }
            }

            MatchCollection matches = Regex.Matches(
                normalizedLink,
                @"(?:chap|chapter|chuong|trang)[^\d]*(?<num>\d+(?:[.,]\d+)?)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            for (int i = matches.Count - 1; i >= 0; i--)
            {
                string token = matches[i].Groups["num"].Value.Replace(',', '.');
                if (double.TryParse(token, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out number))
                {
                    return number > 0d;
                }
            }

            return false;
        }

        private bool TryParseDownloadChapterNumberFromLink(string link, out double number)
        {
            number = 0d;
            if (string.IsNullOrWhiteSpace(link))
            {
                return false;
            }

            try
            {
                if (link.IndexOf("doctruyen.us", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    number = ParseDoctruyenChapterNumber(link);
                    return number > 0d;
                }

                if (link.IndexOf("loppytoonn.com", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    number = ParseLoppyChapterNumber(link);
                    return number > 0d;
                }

                if (link.IndexOf("damconuong", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    number = ParseDamconuongChapterNumber(link);
                    return number > 0d;
                }

                if (link.IndexOf("dilib.vn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    link.IndexOf("thuviensach.vn", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    number = TryParseDilibChapterNumber(link, out double dilibNumber) ? dilibNumber : 0d;
                    return number > 0d;
                }

                if (link.IndexOf("hentai2read", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    number = ParseHentai2readChapterNumber(link);
                    return number > 0d;
                }

                if (link.IndexOf("truyengg", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    link.IndexOf("truyenvua", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    number = ParseTruyenggvnChapterNumber(link);
                    return number > 0d;
                }
            }
            catch
            {
                number = 0d;
            }

            return TryParseChapterNumberFromChapterToken(link, out number);
        }

        private async void BtnCopyChaptersLink_Click(object sender, RoutedEventArgs e)
        {
            var items = GetItemsToExport();
            if (!items.Any())
            {
                MessageBox.Show(_isVietnameseUi ? "Không có truyện nào để lấy link chapter." : "No books to copy chapter links.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            progressBar.IsIndeterminate = true;
            lblStatus.Text = _isVietnameseUi ? "Đang lấy link các chapter..." : "Extracting chapter links...";
            
            try
            {
                var allChapterLinks = new List<string>();

                foreach (var item in items)
                {
                    var links = await ExtractChapterLinksFromBookAsync(item, CancellationToken.None, forceRefresh: true);
                    allChapterLinks.AddRange(links);
                }

                if (allChapterLinks.Any())
                {
                    Clipboard.SetText(string.Join("\r\n", allChapterLinks));
                    Log($"Copied {allChapterLinks.Count} chapter link(s) to clipboard.");
                    lblStatus.Text = _isVietnameseUi
                        ? $"Đã copy {allChapterLinks.Count} link chapter."
                        : $"Copied {allChapterLinks.Count} chapter links.";

                    if (_downloadMissingChapterTab != null)
                    {
                        await ScanDownloadMissingChaptersAsync(forceRefresh: false);
                    }
                }
                else
                {
                    MessageBox.Show(_isVietnameseUi ? "Không tìm thấy link chapter nào." : "No chapter links found.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Log($"Error copying chapter links: {ex.Message}");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                progressBar.IsIndeterminate = false;
            }
        }

        private async Task<List<string>> ExtractChapterLinksFromBookAsync(GalleryItem item, CancellationToken token, bool forceRefresh = false)
        {
            List<ReaderChapterItem> chapterItems = await ExtractChapterItemsFromBookAsyncCore(item, token, forceRefresh);
            return chapterItems
                .Select(chapter => chapter?.FolderPath)
                .Where(link => !string.IsNullOrWhiteSpace(link))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task<List<ReaderChapterItem>> ExtractChapterItemsFromBookAsyncCore(GalleryItem item, CancellationToken token, bool forceRefresh)
        {
            string url = item.Link;
            if (string.IsNullOrWhiteSpace(url)) return new List<ReaderChapterItem>();

            if (!forceRefresh && _downloadChapterItemCache.TryGetValue(url, out List<ReaderChapterItem> cachedItems))
            {
                return CloneReaderChapterItems(cachedItems);
            }

            if (IsDamconuongUrl(url))
            {
                string normalized = NormalizeDamconuongUrl(url);
                string bookUrl = GetDamconuongBookUrl(normalized);
                string html = await FetchStringAsync(bookUrl, token);
                if (IsDamconuongLoginRequiredHtml(html))
                {
                    _downloadChapterItemCache[url] = new List<ReaderChapterItem>();
                    return new List<ReaderChapterItem>();
                }

                string bookTitle = ExtractDamconuongTitleFromHtml(html);
                if (string.IsNullOrWhiteSpace(bookTitle))
                {
                    bookTitle = CleanDamconuongTitle(item.Name);
                }
                if (string.IsNullOrWhiteSpace(bookTitle))
                {
                    bookTitle = CleanDamconuongTitle(GetDamconuongSlugFromLink(bookUrl).Replace('-', ' '));
                }
                if (!string.IsNullOrWhiteSpace(bookTitle))
                {
                    if (Dispatcher.CheckAccess())
                    {
                        item.Name = bookTitle;
                    }
                    else
                    {
                        await Dispatcher.InvokeAsync(() => item.Name = bookTitle);
                    }
                }

                List<string> chapterLinks = ExtractDamconuongChapterLinks(html, bookUrl);
                if (chapterLinks.Count > 0)
                {
                    List<ReaderChapterItem> result = chapterLinks
                        .Select(link => new ReaderChapterItem
                        {
                            Name = BuildDownloadChapterLabel(link),
                            FolderPath = link,
                            Pages = new List<ReaderPageItem>()
                        })
                        .ToList();
                    _downloadChapterItemCache[url] = CloneReaderChapterItems(result);
                    if (!string.Equals(bookUrl, url, StringComparison.OrdinalIgnoreCase))
                    {
                        _downloadChapterItemCache[bookUrl] = CloneReaderChapterItems(result);
                    }
                    return result;
                }

                _downloadChapterItemCache[url] = new List<ReaderChapterItem>();
                return new List<ReaderChapterItem>();
            }

            if (IsDirectChapterLink(url))
            {
                var directItems = new List<ReaderChapterItem>
                {
                    new ReaderChapterItem
                    {
                        Name = BuildDownloadChapterLabel(url),
                        FolderPath = url.Trim(),
                        Pages = new List<ReaderPageItem>()
                    }
                };
                _downloadChapterItemCache[url] = CloneReaderChapterItems(directItems);
                return directItems;
            }

            try
            {
                if (url.IndexOf("haibabamanga.somee.com", StringComparison.OrdinalIgnoreCase) >= 0 || url.IndexOf("haibaba", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string normalized = NormalizeHaibabaUrl(url);
                    string html = await FetchStringAsync(normalized, token);
                    List<GalleryItem> chapGalleryItems = ExtractHaibabaChapters(html, normalized);
                    if (chapGalleryItems != null && chapGalleryItems.Count > 0)
                    {
                        List<ReaderChapterItem> result = chapGalleryItems
                            .Select(chap => new ReaderChapterItem
                            {
                                Name = chap.Name,
                                FolderPath = chap.Link,
                                Pages = new List<ReaderPageItem>()
                            })
                            .ToList();
                        _downloadChapterItemCache[url] = CloneReaderChapterItems(result);
                        return result;
                    }
                }

                if (IsHakoUrl(url) && TryParseHakoBookUrl(url, out _, out _, out string canonicalBookUrl))
                {
                    string html = await FetchHakoHtmlAsync(canonicalBookUrl, token);
                    string detectedTitle = ExtractHakoBookTitle(html);
                    if (!string.IsNullOrWhiteSpace(detectedTitle))
                    {
                        string formattedTitle = FormatGalleryTitle(detectedTitle);
                        if (Dispatcher.CheckAccess())
                        {
                            item.Name = formattedTitle;
                        }
                        else
                        {
                            await Dispatcher.InvokeAsync(() => item.Name = formattedTitle);
                        }
                    }

                    List<HakoChapterInfo> chapters = ExtractHakoChapterLinks(html, canonicalBookUrl);
                    if (chapters.Count > 0)
                    {
                        List<ReaderChapterItem> result = chapters
                            .OrderBy(chapter => chapter.SequenceIndex)
                            .ThenBy(chapter => chapter.ChapterNumber ?? double.MaxValue)
                            .ThenBy(chapter => chapter.Title, StringComparer.OrdinalIgnoreCase)
                            .Select(chapter => new ReaderChapterItem
                            {
                                Name = BuildDownloadChapterItemName(chapter.Link, chapter.Title),
                                FolderPath = chapter.Link,
                                Pages = new List<ReaderPageItem>()
                            })
                            .ToList();
                        _downloadChapterItemCache[url] = CloneReaderChapterItems(result);
                        if (!string.Equals(canonicalBookUrl, url, StringComparison.OrdinalIgnoreCase))
                        {
                            _downloadChapterItemCache[canonicalBookUrl] = CloneReaderChapterItems(result);
                        }
                        return result;
                    }

                    _downloadChapterItemCache[url] = new List<ReaderChapterItem>();
                    return new List<ReaderChapterItem>();
                }

                if (IsDilibUrl(url))
                {
                    string normalized = NormalizeDilibUrl(url);
                    string html = await FetchStringAsync(normalized, token);
                    List<ReaderChapterItem> result = ExtractDilibChapterLinksFromBookHtml(html, normalized)
                        .Where(chapter => chapter != null && !string.IsNullOrWhiteSpace(chapter.Link))
                        .Select(chapter => new ReaderChapterItem
                        {
                            Name = BuildDownloadChapterLabel(chapter.Link),
                            FolderPath = chapter.Link.Trim(),
                            Pages = new List<ReaderPageItem>()
                        })
                        .ToList();
                    if (result.Count > 0)
                    {
                        _downloadChapterItemCache[url] = CloneReaderChapterItems(result);
                        return result;
                    }

                    _downloadChapterItemCache[url] = new List<ReaderChapterItem>();
                    return new List<ReaderChapterItem>();
                }
                else if (url.IndexOf("mangadex.org", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    List<ReaderChapterItem> result = await GetMangadexReaderChapterItemsAsync(item, url, token);
                    if (result.Count > 0)
                    {
                        _downloadChapterItemCache[url] = CloneReaderChapterItems(result);
                        return result;
                    }

                    _downloadChapterItemCache[url] = new List<ReaderChapterItem>();
                    return new List<ReaderChapterItem>();
                }
                else if (url.IndexOf("doctruyen.us", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string normalized = NormalizeDoctruyenUrl(url);
                    string html = await FetchStringAsync(normalized, token);
                    List<string> chapterLinks = ExtractDoctruyenChapterLinks(html, normalized);
                    if (chapterLinks.Count > 0)
                    {
                        List<ReaderChapterItem> result = chapterLinks
                            .OrderBy(ParseChapterNumber)
                            .Select(link => new ReaderChapterItem
                            {
                                Name = BuildDownloadChapterLabel(link),
                                FolderPath = link,
                                Pages = new List<ReaderPageItem>()
                            })
                            .ToList();
                        _downloadChapterItemCache[url] = CloneReaderChapterItems(result);
                        return result;
                    }
                }
                else if (url.IndexOf("loppytoonn.com", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string normalized = NormalizeLoppyUrl(url);
                    string html = await FetchStringAsync(normalized, token);
                    List<string> chapterLinks = ExtractLoppyChapterLinks(html, normalized);
                    if (chapterLinks.Count > 0)
                    {
                        List<ReaderChapterItem> result = chapterLinks
                            .OrderBy(ParseChapterNumber)
                            .Select(link => new ReaderChapterItem
                            {
                                Name = BuildDownloadChapterLabel(link),
                                FolderPath = link,
                                Pages = new List<ReaderPageItem>()
                            })
                            .ToList();
                        _downloadChapterItemCache[url] = CloneReaderChapterItems(result);
                        return result;
                    }
                }
                else if (IsTruyenqqUrl(url))
                {
                    string cleanLink = ResolveTruyenqqRequestUrl(url);
                    string activeDomain = ExtractTruyenqqBaseUrl(cleanLink);
                    var uri = new Uri(cleanLink);
                    var segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    if (segments.Length >= 2 && segments[0].Equals("truyen-tranh", StringComparison.OrdinalIgnoreCase))
                    {
                        bool captchaOk = await SolveTruyenqqCaptchaIfNeededAsync(cleanLink);
                        cleanLink = ResolveTruyenqqRequestUrl(cleanLink);
                        string html = await FetchStringAsync(cleanLink, token);
                        string parentPath = uri.AbsolutePath.TrimEnd('/');
                        string escapedPath = Regex.Escape(parentPath);
                        string pattern = @"href=[""'](?<link>[^""']*?" + escapedPath + @"-chap(?:[^""'\s?#]*)?)[""']";
                        var matches = Regex.Matches(html, pattern, RegexOptions.IgnoreCase);
                        if (matches.Count == 0)
                        {
                            string fallbackPattern = @"href=[""'](?<link>[^""']*?" + escapedPath + @"-(?:chap|chapter|chuong)(?:[^""'\s?#]*)?)[""']";
                            matches = Regex.Matches(html, fallbackPattern, RegexOptions.IgnoreCase);
                        }
                        var chapterLinks = new List<string>();
                        var seenChapters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (Match m in matches)
                        {
                            string link = m.Groups["link"].Value.Trim();
                            if (!link.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                                !link.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                            {
                                link = activeDomain + (link.StartsWith("/") ? "" : "/") + link;
                            }
                            link = link.TrimEnd('/');
                            if (seenChapters.Add(link))
                            {
                                chapterLinks.Add(link);
                            }
                        }
                        if (chapterLinks.Count > 0)
                        {
                            List<ReaderChapterItem> result = chapterLinks
                                .OrderBy(ParseChapterNumber)
                                .Select(link => new ReaderChapterItem
                                {
                                    Name = BuildDownloadChapterLabel(link),
                                    FolderPath = link,
                                    Pages = new List<ReaderPageItem>()
                                })
                                .ToList();
                            _downloadChapterItemCache[url] = CloneReaderChapterItems(result);
                            return result;
                        }
                    }
                }
                else if (IsNettruyenUrl(url))
                {
                    string cleanLink = url.TrimEnd('/');
                    var links = await GetNettruyenChapterLinksInternalAsync(item, cleanLink, token, forceRefresh);
                    if (links != null && links.Count > 0)
                    {
                        if (_downloadChapterItemCache.TryGetValue(url, out List<ReaderChapterItem> cached) && cached != null && cached.Count > 0)
                        {
                            return CloneReaderChapterItems(cached);
                        }

                        var chapterItems = links.Select(link => new ReaderChapterItem
                        {
                            Name = BuildDownloadChapterLabel(link),
                            FolderPath = link,
                            Pages = new List<ReaderPageItem>()
                        }).ToList();
                        _downloadChapterItemCache[url] = CloneReaderChapterItems(chapterItems);
                        return chapterItems;
                    }
                    return new List<ReaderChapterItem>();
                }
                else if (IsDaomeodenUrl(url))
                {
                    string normalizedLink = NormalizeDaomeodenUrl(url);
                    string html = await FetchStringAsync(normalizedLink, token);
                    var chapterMatches = Regex.Matches(html, @"(?:href|openUrl\()\s*(?:=\s*|['""])(?<link>/doc-truyen-tranh/[^'"")\s>]+)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    var chapterLinks = new List<string>();
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (Match match in chapterMatches)
                    {
                        string link = NormalizeDaomeodenUrl(match.Groups["link"].Value);
                        if (seen.Add(link))
                        {
                            chapterLinks.Add(link);
                        }
                    }
                    if (chapterLinks.Count > 0)
                    {
                        List<ReaderChapterItem> result = chapterLinks
                            .OrderBy(ParseChapterNumber)
                            .Select(link => new ReaderChapterItem
                            {
                                Name = BuildDownloadChapterLabel(link),
                                FolderPath = link,
                                Pages = new List<ReaderPageItem>()
                            })
                            .ToList();
                        _downloadChapterItemCache[url] = CloneReaderChapterItems(result);
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[Copy Chapters] Lỗi lấy link chapter cho {item.Name}: {ex.Message}");
            }

            if (url.IndexOf("mangadex.org", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _downloadChapterItemCache[url] = new List<ReaderChapterItem>();
                return new List<ReaderChapterItem>();
            }

            var fallbackItems = new List<ReaderChapterItem>
            {
                new ReaderChapterItem
                {
                    Name = BuildDownloadChapterLabel(url),
                    FolderPath = url.Trim(),
                    Pages = new List<ReaderPageItem>()
                }
            };
            _downloadChapterItemCache[url] = CloneReaderChapterItems(fallbackItems);
            return fallbackItems;
        }

        private void BtnRefreshStatus_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = dgResults.SelectedItems.Cast<GalleryItem>().ToList();
            if (!selectedItems.Any())
            {
                MessageBox.Show(_isVietnameseUi ? "Vui lòng chọn ít nhất một truyện để làm mới." : "Please select at least one book to refresh.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int count = 0;
            foreach (var item in selectedItems)
            {
                if (item == null) continue;

                item.Status = null;
                item.CurrentProcess = "";
                item.CompletedChapters = 0;
                item.TotalChapters = 0;
                item.ProgressPercent = 0;
                item.DownloadProgressPercent = 0;
                item.DownloadSpeedBytesPerSecond = 0;
                item._downloadedBytesAccumulator = 0;
                item.IsPaused = false;
                item.IsStopped = false;
                item.DownloadingChapter = "";
                item.DownloadingPageProgress = "";
                item.DownloadingPageLink = "";

                if (item.Errors != null)
                {
                    item.Errors.Clear();
                }
                else
                {
                    item.Errors = new List<ErrorDetail>();
                }
                item.ErrorCount = 0;
                if (!string.IsNullOrWhiteSpace(item.Link))
                {
                    _downloadChapterItemCache.Remove(item.Link);
                }

                DeleteProcessMarkdownForItem(item);
                count++;
            }

            UpdateStats();

            lblStatus.Text = _isVietnameseUi ? $"Đã làm mới trạng thái cho {count} truyện." : $"Refreshed status for {count} books.";
            Log(_isVietnameseUi ? $"Đã làm mới trạng thái và xóa process file cho {count} truyện." : $"Refreshed status and deleted process files for {count} books.");
            RequestGalleryListAutosave(0);
        }

        private async void BtnRescanMissingChapter_Click(object sender, RoutedEventArgs e)
        {
            List<GalleryItem> items = GetDownloadMissingChapterSourceItems();
            if (items.Count == 0)
            {
                MessageBox.Show(_isVietnameseUi ? "Không có truyện để scan lại chap số nguyên thiếu." : "No books to rescan missing integer chapters.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await ScanDownloadMissingChaptersAsync(forceRefresh: true, explicitItems: items);
        }

        private async void BtnApplyAutoSplitChapters_Click(object sender, RoutedEventArgs e)
        {
            if (cmbAutoSplitChapters == null) return;
            var selectedVal = (cmbAutoSplitChapters.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (!int.TryParse(selectedVal, out int bucketSize) || bucketSize <= 0)
            {
                string msgOff = _isVietnameseUi ? "Vui lòng chọn một ngưỡng chia chương cụ thể (50, 100,...) thay vì OFF." : "Please select a specific split threshold (50, 100,...) instead of OFF.";
                MessageBox.Show(msgOff, "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            btnApplyAutoSplitChapters.IsEnabled = false;
            try
            {
                var itemsToSplit = _scrapedItems
                    .Where(item => item != null && string.IsNullOrWhiteSpace(item.ChapterSelectionText))
                    .ToList();

                int totalSplitCount = 0;
                foreach (var item in itemsToSplit)
                {
                    List<ReaderChapterItem> chapterItems = GetCachedDownloadChapterItems(item);
                    if (chapterItems == null || chapterItems.Count == 0)
                    {
                        chapterItems = await ExtractChapterItemsFromBookAsync(item, CancellationToken.None);
                    }

                    if (chapterItems != null && chapterItems.Count > bucketSize)
                    {
                        List<string> ranges = await BuildParallelSplitRangesAsync(item, bucketSize);
                        if (ranges.Count > 0)
                        {
                            int insertIndex = _scrapedItems.IndexOf(item);
                            if (insertIndex >= 0)
                            {
                                item.IsParallelSplitParent = true;
                                item.IsParallelSplitCollapsed = true;
                                List<GalleryItem> clones = ranges.Select(range => CreateParallelSplitTask(item, range)).ToList();
                                item.ParallelSplitChildren = clones;
                                item.ChapterSelectionText = "";
                                totalSplitCount++;
                                Log($"Manual Auto split '{item.DisplayName}' into {clones.Count} tasks with bucket size {bucketSize}.");
                            }
                        }
                    }
                }

                if (totalSplitCount > 0)
                {
                    RenumberResultOrder();
                    SafeRefreshResultsView();
                    RecalculateDuplicates();
                    UpdateStats();
                    EnsureDownloadMissingChapterRowsFromGallery(forceSyncOrder: true);
                    SyncAllGalleryMissingChapterStatuses();

                    string successMsg = _isVietnameseUi ? $"Đã tự động chia nhỏ {totalSplitCount} bộ truyện phù hợp." : $"Automatically split {totalSplitCount} matching books.";
                    lblStatus.Text = successMsg;
                    MessageBox.Show(successMsg, "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    string infoMsg = _isVietnameseUi ? "Không tìm thấy bộ truyện nào đủ số chương cần chia nhỏ." : "No matching books found with enough chapters to split.";
                    lblStatus.Text = infoMsg;
                    MessageBox.Show(infoMsg, "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Log($"Apply auto split error: {ex.Message}");
            }
            finally
            {
                btnApplyAutoSplitChapters.IsEnabled = true;
            }
        }
    }
}

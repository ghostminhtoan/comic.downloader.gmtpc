using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace get_link_manga
{
    internal sealed class SystemFloatingControlWindow : Window
    {
        private readonly Action _startCopyAction;
        private readonly Action _stopCopyAction;
        private readonly Action _startDownloadPictureAction;
        private readonly Action _stopDownloadPictureAction;
        private readonly Action<bool> _setRetryAction;
        private readonly Action _toggleAutoPasteAction;
        private readonly Action _openShutdownOptionsAction;
        private readonly Action _toggleAutoFocusAction;
        private readonly Action _openFolderAction;
        private readonly Action _deleteCookiesAction;
        private readonly Action _clearTempAction;
        private readonly Action<string> _pasteDirectLinkAction;
        private readonly Action<int> _folderTypeChangedAction;
        private ComboBox _folderTypeComboBox;
        private readonly TextBlock _statusText;
        private readonly TextBlock _buildInfoText;
        private readonly Button _pinToggleButton;
        private readonly Button _autoPasteToggleButton;
        private readonly Button _focusToggleButton;
        private readonly Button _downloadToggleButton;
        private readonly Button _retryToggleButton;
        private readonly Button _shutdownToggleButton;
        private readonly Button _moveButton;
        private Button _copyToggleButton;
        private readonly Border _shellBorder;
        private readonly Slider _opacitySlider;
        private readonly TextBlock _opacityValueText;
        private readonly Slider _sizeSlider;
        private readonly TextBlock _sizeValueText;
        private readonly ScaleTransform _contentScaleTransform;
        private bool _isPinned = true;
        private bool _hasSavedBounds;
        private double _savedLeft;
        private double _savedTop;
        private double _savedWidth;
        private double _savedHeight;

        private const double BaseWindowWidth = 442;
        private const double BaseWindowHeight = 286;
        private const double BaseWindowMinWidth = 392;
        private const double BaseWindowMinHeight = 252;
        private const int GwlExStyle = -20;
        private const int WsExNoActivate = 0x08000000;
        private const int WsExToolWindow = 0x00000080;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpShowWindow = 0x0040;
        private static readonly IntPtr HwndTopmost = new IntPtr(-1);

        private static readonly Color OnColor = Color.FromRgb(0x58, 0xD6, 0x00);
        private static readonly Color OnBorderColor = Color.FromRgb(0x7F, 0xE6, 0x27);
        private static readonly Color OffColor = Color.FromRgb(0xD8, 0x21, 0x2A);
        private static readonly Color OffBorderColor = Color.FromRgb(0xF4, 0x62, 0x6B);

        internal SystemFloatingControlWindow(
            bool isVietnamese,
            Action startCopyAction,
            Action stopCopyAction,
            Action startDownloadPictureAction,
            Action stopDownloadPictureAction,
            Action<bool> setRetryAction,
            Action toggleAutoPasteAction,
            Action openShutdownOptionsAction,
            Action toggleAutoFocusAction,
            Action openFolderAction,
            Action deleteCookiesAction,
            Action clearTempAction,
            Action<string> pasteDirectLinkAction,
            Action<int> folderTypeChangedAction)
        {
            _startCopyAction = startCopyAction;
            _stopCopyAction = stopCopyAction;
            _startDownloadPictureAction = startDownloadPictureAction;
            _stopDownloadPictureAction = stopDownloadPictureAction;
            _setRetryAction = setRetryAction;
            _toggleAutoPasteAction = toggleAutoPasteAction;
            _openShutdownOptionsAction = openShutdownOptionsAction;
            _toggleAutoFocusAction = toggleAutoFocusAction;
            _openFolderAction = openFolderAction;
            _deleteCookiesAction = deleteCookiesAction;
            _clearTempAction = clearTempAction;
            _pasteDirectLinkAction = pasteDirectLinkAction;
            _folderTypeChangedAction = folderTypeChangedAction;

            Width = BaseWindowWidth;
            Height = BaseWindowHeight;
            MinWidth = BaseWindowMinWidth;
            MinHeight = BaseWindowMinHeight;
            Left = SystemParameters.WorkArea.Left + 16;
            Top = SystemParameters.WorkArea.Bottom - Height - 16;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowActivated = false;
            Opacity = 0.92;

            AllowDrop = true;
            DragOver += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.Text) || 
                    e.Data.GetDataPresent(DataFormats.UnicodeText) || 
                    e.Data.GetDataPresent("UniformResourceLocator") || 
                    e.Data.GetDataPresent("UniformResourceLocatorW"))
                {
                    e.Effects = DragDropEffects.Copy;
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
                e.Handled = true;
            };
            Drop += (s, e) =>
            {
                string droppedText = null;
                try
                {
                    if (e.Data.GetDataPresent(DataFormats.UnicodeText))
                    {
                        droppedText = e.Data.GetData(DataFormats.UnicodeText) as string;
                    }
                    else if (e.Data.GetDataPresent(DataFormats.Text))
                    {
                        droppedText = e.Data.GetData(DataFormats.Text) as string;
                    }
                    else if (e.Data.GetDataPresent("UniformResourceLocatorW"))
                    {
                        droppedText = e.Data.GetData("UniformResourceLocatorW")?.ToString();
                    }
                    else if (e.Data.GetDataPresent("UniformResourceLocator"))
                    {
                        droppedText = e.Data.GetData("UniformResourceLocator")?.ToString();
                    }
                }
                catch {}

                if (!string.IsNullOrWhiteSpace(droppedText))
                {
                    _pasteDirectLinkAction?.Invoke(droppedText);
                }
            };

            var host = new Grid();

            _shellBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(242, 13, 18, 31)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)),
                BorderThickness = new Thickness(1.6),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(8, 7, 8, 8)
            };
            host.Children.Add(_shellBorder);

            var root = new Grid();
            _contentScaleTransform = new ScaleTransform(1d, 1d);
            root.LayoutTransform = _contentScaleTransform;
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.MouseLeftButtonDown += (sender, args) =>
            {
                if (args.ButtonState == MouseButtonState.Pressed)
                {
                    DragMove();
                }
            };

            var topBar = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusText = new TextBlock
            {
                Text = "STOPPED",
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x2A, 0x85)),
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 6, 0)
            };
            Grid.SetColumn(_statusText, 0);
            topBar.Children.Add(_statusText);

            _moveButton = CreateWindowButton("MOVE", Color.FromRgb(0xFF, 0xD4, 0x6A), null);
            _moveButton.PreviewMouseLeftButtonDown += MoveButton_PreviewMouseLeftButtonDown;
            _moveButton.MinWidth = 54;
            Grid.SetColumn(_moveButton, 1);
            topBar.Children.Add(_moveButton);

            var minimizeButton = CreateWindowButton("_", Color.FromRgb(0xB2, 0xEB, 0xF2), (sender, args) => Hide());
            Grid.SetColumn(minimizeButton, 3);
            topBar.Children.Add(minimizeButton);

            var closeButton = CreateWindowButton("X", Color.FromRgb(0xFF, 0x79, 0xC6), (sender, args) => Hide());
            Grid.SetColumn(closeButton, 5);
            topBar.Children.Add(closeButton);

            Grid.SetRow(topBar, 0);
            root.Children.Add(topBar);

            var topToggleRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            topToggleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topToggleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            topToggleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topToggleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            topToggleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var pinGroup = CreateToggleGroup("Pin", out _pinToggleButton, (sender, args) => TogglePin());
            Grid.SetColumn(pinGroup, 0);
            topToggleRow.Children.Add(pinGroup);

            var autoPasteGroup = CreateToggleGroup("Auto Paste", out _autoPasteToggleButton, (sender, args) => ToggleAutoPaste());
            Grid.SetColumn(autoPasteGroup, 2);
            topToggleRow.Children.Add(autoPasteGroup);

            var focusGroup = CreateToggleGroup("Focus", out _focusToggleButton, (sender, args) => _toggleAutoFocusAction?.Invoke());
            Grid.SetColumn(focusGroup, 4);
            topToggleRow.Children.Add(focusGroup);

            Grid.SetRow(topToggleRow, 1);
            root.Children.Add(topToggleRow);

            var middleToggleRow = CreateDoubleToggleRow(
                "Download", out _downloadToggleButton, (sender, args) => ToggleDownload(),
                "Retry", out _retryToggleButton, (sender, args) => ToggleRetry());
            Grid.SetRow(middleToggleRow, 2);
            root.Children.Add(middleToggleRow);

            var bottomToggleRow = CreateCopyRow();
            Grid.SetRow(bottomToggleRow, 3);
            root.Children.Add(bottomToggleRow);

            var systemRow = CreateSystemRow(out _shutdownToggleButton, (sender, args) => _openShutdownOptionsAction?.Invoke());
            Grid.SetRow(systemRow, 4);
            root.Children.Add(systemRow);

            var buildRow = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            buildRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            buildRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            buildRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var buildLabel = CreateRowLabel("Build");
            Grid.SetColumn(buildLabel, 0);
            buildRow.Children.Add(buildLabel);

            _buildInfoText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(_buildInfoText, 2);
            buildRow.Children.Add(_buildInfoText);
            Grid.SetRow(buildRow, 5);
            root.Children.Add(buildRow);

            var opacityRow = new Grid { Margin = new Thickness(0, 5, 0, 0) };
            opacityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            opacityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            opacityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            opacityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            opacityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var opacityLabel = new TextBlock
            {
                Text = "OPACITY",
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)),
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(opacityLabel, 0);
            opacityRow.Children.Add(opacityLabel);

            _opacitySlider = new Slider
            {
                Minimum = 35,
                Maximum = 100,
                Value = 92,
                TickFrequency = 5,
                IsSnapToTickEnabled = false,
                VerticalAlignment = VerticalAlignment.Center,
                Height = 18
            };
            _opacitySlider.ValueChanged += OpacitySlider_ValueChanged;
            Grid.SetColumn(_opacitySlider, 2);
            opacityRow.Children.Add(_opacitySlider);

            _opacityValueText = new TextBlock
            {
                Text = "92",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 24,
                TextAlignment = TextAlignment.Center
            };
            var opacityBubble = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x18, 0x18)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x6C, 0x6C)),
                BorderThickness = new Thickness(1.2),
                Child = _opacityValueText
            };
            Grid.SetColumn(opacityBubble, 4);
            opacityRow.Children.Add(opacityBubble);

            Grid.SetRow(opacityRow, 6);
            root.Children.Add(opacityRow);

            var sizeRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            sizeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sizeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            sizeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sizeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            sizeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var sizeLabel = new TextBlock
            {
                Text = "SIZE",
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)),
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(sizeLabel, 0);
            sizeRow.Children.Add(sizeLabel);

            _sizeSlider = new Slider
            {
                Minimum = 50,
                Maximum = 150,
                Value = 100,
                TickFrequency = 5,
                IsSnapToTickEnabled = false,
                VerticalAlignment = VerticalAlignment.Center,
                Height = 18
            };
            _sizeSlider.ValueChanged += SizeSlider_ValueChanged;
            Grid.SetColumn(_sizeSlider, 2);
            sizeRow.Children.Add(_sizeSlider);

            _sizeValueText = new TextBlock
            {
                Text = "100%",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 34,
                TextAlignment = TextAlignment.Center
            };
            var sizeBubble = new Border
            {
                Width = 38,
                Height = 28,
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0x00)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xB1, 0x4A)),
                BorderThickness = new Thickness(1.1),
                Child = _sizeValueText
            };
            Grid.SetColumn(sizeBubble, 4);
            sizeRow.Children.Add(sizeBubble);

            Grid.SetRow(sizeRow, 7);
            root.Children.Add(sizeRow);

            _shellBorder.Child = root;
            Content = host;

            LocationChanged += (_, __) => CacheWindowBounds();
            SizeChanged += (_, __) => CacheWindowBounds();

            AddResizeThumb(host, HorizontalAlignment.Right, VerticalAlignment.Stretch, Cursors.SizeWE, 6, double.NaN, ResizeRight);
            AddResizeThumb(host, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, Cursors.SizeNS, double.NaN, 6, ResizeBottom);
            AddResizeThumb(host, HorizontalAlignment.Right, VerticalAlignment.Bottom, Cursors.SizeNWSE, 12, 12, ResizeCorner);

            UpdateOpacityVisual();
            UpdateSizeVisual();
            UpdateState(false, true, false, false, false, false, BuildInfo.DisplayText, isVietnamese);
        }

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            int exStyle = GetWindowLong(handle, GwlExStyle);
            SetWindowLong(handle, GwlExStyle, exStyle | WsExNoActivate | WsExToolWindow);
        }

        internal void UpdateState(bool isCopyRunning, bool autoFocusEnabled, bool isDownloadRunning, bool isRetryEnabled, bool isShutdownEnabled, bool isAutoPasteEnabled, string buildText, bool isVietnamese)
        {
            bool isRunning = isCopyRunning || isDownloadRunning;
            _statusText.Text = isRunning ? "RUNNING" : "STOPPED";
            _statusText.Foreground = new SolidColorBrush(isRunning
                ? Color.FromRgb(0x00, 0xE5, 0xFF)
                : Color.FromRgb(0xFF, 0x2A, 0x85));

            SetToggleVisual(_pinToggleButton, _isPinned);
            SetToggleVisual(_focusToggleButton, autoFocusEnabled);
            SetToggleVisual(_downloadToggleButton, isDownloadRunning);
            SetToggleVisual(_retryToggleButton, isRetryEnabled);
            SetToggleVisual(_shutdownToggleButton, isShutdownEnabled);
            SetToggleVisual(_copyToggleButton, isCopyRunning);
            SetToggleVisual(_autoPasteToggleButton, isAutoPasteEnabled);
            _buildInfoText.Text = string.IsNullOrWhiteSpace(buildText) ? "-" : buildText;

            _shellBorder.BorderBrush = new SolidColorBrush(isRunning
                ? Color.FromRgb(0x00, 0xE5, 0xFF)
                : Color.FromRgb(0xFF, 0x2A, 0x85));
        }

        private Grid CreateDoubleToggleRow(string leftLabel, out Button leftToggle, RoutedEventHandler leftClick, string rightLabel, out Button rightToggle, RoutedEventHandler rightClick)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var leftGroup = CreateToggleGroup(leftLabel, out leftToggle, leftClick);
            Grid.SetColumn(leftGroup, 0);
            row.Children.Add(leftGroup);

            var rightGroup = CreateToggleGroup(rightLabel, out rightToggle, rightClick);
            Grid.SetColumn(rightGroup, 2);
            row.Children.Add(rightGroup);

            return row;
        }

        private Grid CreateSystemRow(out Button shutdownToggleButton, RoutedEventHandler shutdownClick)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(75) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var labelText = CreateRowLabel("Shutdown");
            Grid.SetColumn(labelText, 0);
            row.Children.Add(labelText);

            shutdownToggleButton = CreateWindowButton("⏰", Color.FromRgb(0xFF, 0x79, 0xC6), shutdownClick);
            shutdownToggleButton.Width = 70;
            shutdownToggleButton.MinHeight = 22;
            shutdownToggleButton.FontSize = 15;
            Grid.SetColumn(shutdownToggleButton, 2);
            row.Children.Add(shutdownToggleButton);

            var deleteCookieButton = CreateWindowButton("DEL COOKIE", Color.FromRgb(0xFF, 0xD2, 0x6A), (sender, args) => _deleteCookiesAction?.Invoke());
            deleteCookieButton.Width = 86;
            deleteCookieButton.MinHeight = 22;
            Grid.SetColumn(deleteCookieButton, 4);
            row.Children.Add(deleteCookieButton);

            var cleanTempButton = CreateWindowButton("CLEAN TEMP", Color.FromRgb(0x00, 0xE5, 0xFF), (sender, args) => _clearTempAction?.Invoke());
            cleanTempButton.Width = 96;
            cleanTempButton.MinHeight = 22;
            Grid.SetColumn(cleanTempButton, 6);
            row.Children.Add(cleanTempButton);

            var tweakButton = CreateWindowButton("TWEAK", Color.FromRgb(0xFF, 0x79, 0xC6), TweakButton_Click);
            tweakButton.Width = 70;
            tweakButton.MinHeight = 22;
            Grid.SetColumn(tweakButton, 8);
            row.Children.Add(tweakButton);

            return row;
        }

        private Grid CreateCopyRow()
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(75) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var labelText = CreateRowLabel("Copy text");
            Grid.SetColumn(labelText, 0);
            row.Children.Add(labelText);

            _copyToggleButton = CreateToggleButton((sender, args) => ToggleCopy());
            Grid.SetColumn(_copyToggleButton, 2);
            row.Children.Add(_copyToggleButton);

            var openFolderButton = CreateWindowButton("OPEN FOLDER", Color.FromRgb(0x00, 0xE5, 0xFF), (sender, args) => _openFolderAction?.Invoke());
            openFolderButton.Width = 86;
            openFolderButton.MinHeight = 22;
            Grid.SetColumn(openFolderButton, 4);
            row.Children.Add(openFolderButton);

            _folderTypeComboBox = new ComboBox
            {
                Style = TryFindResource("CyberpunkComboBox") as Style,
                Height = 22,
                Width = 96,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            
            var itemStyle = TryFindResource("CyberpunkComboBoxItemStyle") as Style;
            var item1 = new ComboBoxItem { Content = "Single comic", Style = itemStyle };
            var item2 = new ComboBoxItem { Content = "Multi-comic", Style = itemStyle };
            _folderTypeComboBox.Items.Add(item1);
            _folderTypeComboBox.Items.Add(item2);
            _folderTypeComboBox.SelectedIndex = 0;
            _folderTypeComboBox.SelectionChanged += (sender, args) =>
            {
                _folderTypeChangedAction?.Invoke(_folderTypeComboBox.SelectedIndex);
            };

            Grid.SetColumn(_folderTypeComboBox, 6);
            row.Children.Add(_folderTypeComboBox);

            return row;
        }

        internal void UpdateFolderType(int index)
        {
            if (_folderTypeComboBox != null && _folderTypeComboBox.SelectedIndex != index)
            {
                _folderTypeComboBox.SelectedIndex = index;
            }
        }

        private void TweakButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button))
            {
                return;
            }

            string tweakRoot = FindFolderUpward("regedit");
            if (!Directory.Exists(tweakRoot))
            {
                return;
            }

            string[] regFiles = Directory.GetFiles(tweakRoot, "*.reg");
            if (regFiles.Length == 0)
            {
                return;
            }

            var menu = new ContextMenu();
            foreach (string regFile in regFiles)
            {
                string localPath = regFile;
                var item = new MenuItem
                {
                    Header = Path.GetFileNameWithoutExtension(localPath)
                };
                item.Click += (menuSender, menuArgs) =>
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = localPath,
                        UseShellExecute = true
                    });
                };
                menu.Items.Add(item);
            }

            button.ContextMenu = menu;
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }

        private static string FindFolderUpward(string folderName)
        {
            string[] roots =
            {
                AppDomain.CurrentDomain.BaseDirectory,
                Environment.CurrentDirectory
            };

            foreach (string root in roots)
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                var current = new DirectoryInfo(root);
                while (current != null)
                {
                    string candidate = Path.Combine(current.FullName, folderName);
                    if (Directory.Exists(candidate))
                    {
                        return candidate;
                    }

                    current = current.Parent;
                }
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, folderName);
        }

        private Grid CreateToggleGroup(string label, out Button toggleButton, RoutedEventHandler onClick)
        {
            var group = new Grid();
            group.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            group.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            group.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var labelText = CreateRowLabel(label);
            Grid.SetColumn(labelText, 0);
            group.Children.Add(labelText);

            toggleButton = CreateToggleButton(onClick);
            Grid.SetColumn(toggleButton, 2);
            group.Children.Add(toggleButton);

            return group;
        }

        private static TextBlock CreateRowLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private static Button CreateToggleButton(RoutedEventHandler onClick)
        {
            var track = new Border
            {
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(1.3),
                Width = 70,
                Height = 24,
                Padding = new Thickness(3)
            };

            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var stateText = new TextBlock
            {
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextDecorations = TextDecorations.Underline
            };
            layout.Children.Add(stateText);

            var thumb = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromRgb(0xDF, 0xE5, 0xFA)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xC6, 0xCF, 0xF1)),
                BorderThickness = new Thickness(1.1)
            };
            Grid.SetColumn(thumb, 1);
            layout.Children.Add(thumb);

            track.Child = layout;

            var button = new Button
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Cursor = Cursors.Hand,
                Content = track,
                Tag = new FloatingToggleVisual(track, thumb, stateText)
            };
            button.Click += onClick;
            return button;
        }

        private static void SetToggleVisual(Button button, bool isOn)

        {
            if (!(button?.Tag is FloatingToggleVisual visual))
            {
                return;
            }

            visual.IsOn = isOn;
            visual.Track.Background = new SolidColorBrush(isOn ? OnColor : OffColor);
            visual.Track.BorderBrush = new SolidColorBrush(isOn ? OnBorderColor : OffBorderColor);
            visual.StateText.Text = isOn ? "ON" : "OFF";
            visual.StateText.Foreground = Brushes.White;
            Grid.SetColumn(visual.StateText, isOn ? 0 : 1);
            Grid.SetColumn(visual.Thumb, isOn ? 1 : 0);
            visual.Thumb.HorizontalAlignment = isOn ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }

        private void ToggleCopy()
        {
            if (IsToggleOn(_copyToggleButton))
            {
                _stopCopyAction?.Invoke();
            }
            else
            {
                _startCopyAction?.Invoke();
            }
        }

        private void ToggleAutoPaste()
        {
            _toggleAutoPasteAction?.Invoke();
        }

        private void ToggleDownload()
        {
            if (IsToggleOn(_downloadToggleButton))
            {
                _stopDownloadPictureAction?.Invoke();
            }
            else
            {
                _startDownloadPictureAction?.Invoke();
            }
        }

        private void ToggleRetry()
        {
            _setRetryAction?.Invoke(!IsToggleOn(_retryToggleButton));
        }

        private void ToggleShutdown()
        {
            _openShutdownOptionsAction?.Invoke();
        }

        private void TogglePin()
        {
            _isPinned = !_isPinned;
            Topmost = _isPinned;
            SetToggleVisual(_pinToggleButton, _isPinned);
        }

        private void MoveButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed)
            {
                return;
            }

            e.Handled = true;
            try
            {
                DragMove();
            }
            catch
            {
            }
        }

        private static bool IsToggleOn(Button button)
        {
            return button?.Tag is FloatingToggleVisual visual && visual.IsOn;
        }

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateOpacityVisual();
        }

        private void SizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateSizeVisual();
        }

        private void UpdateOpacityVisual()
        {
            if (_opacitySlider == null || _opacityValueText == null)
            {
                return;
            }

            double opacityValue = _opacitySlider.Value / 100.0;
            Opacity = opacityValue;
            _opacityValueText.Text = Math.Round(_opacitySlider.Value).ToString("0");
        }

        private void UpdateSizeVisual()
        {
            if (_sizeSlider == null || _sizeValueText == null || _contentScaleTransform == null)
            {
                return;
            }

            double scale = _sizeSlider.Value / 100d;
            _contentScaleTransform.ScaleX = scale;
            _contentScaleTransform.ScaleY = scale;
            MinWidth = Math.Max(160d, BaseWindowMinWidth * scale);
            MinHeight = Math.Max(130d, BaseWindowMinHeight * scale);
            Width = Math.Max(BaseWindowMinWidth * scale, BaseWindowWidth * scale);
            Height = Math.Max(BaseWindowMinHeight * scale, BaseWindowHeight * scale);
            _sizeValueText.Text = Math.Round(_sizeSlider.Value).ToString("0") + "%";
        }

        internal void RestoreFromTray()
        {
            if (!IsVisible)
            {
                Show();
            }

            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            ShowInTaskbar = false;
            Topmost = _isPinned;

            if (_hasSavedBounds)
            {
                Width = _savedWidth;
                Height = _savedHeight;
                Left = _savedLeft;
                Top = _savedTop;
            }

            BringToFrontWithoutActivation();

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_hasSavedBounds)
                {
                    Width = _savedWidth;
                    Height = _savedHeight;
                    Left = _savedLeft;
                    Top = _savedTop;
                }

                ShowInTaskbar = false;
                Topmost = _isPinned;
                if (WindowState == WindowState.Minimized)
                {
                    WindowState = WindowState.Normal;
                }
                BringToFrontWithoutActivation();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        internal void ShowWithoutActivationSafe()
        {
            if (!IsVisible)
            {
                Show();
            }

            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            BringToFrontWithoutActivation();
        }

        internal void PrepareForTrayHide()
        {
            CacheWindowBounds();
            Topmost = false;
            ShowInTaskbar = false;
        }

        private void CacheWindowBounds()
        {
            if (WindowState != WindowState.Normal)
            {
                return;
            }

            if (double.IsNaN(Left) || double.IsNaN(Top) || double.IsNaN(ActualWidth) || double.IsNaN(ActualHeight))
            {
                return;
            }

            _savedLeft = Left;
            _savedTop = Top;
            _savedWidth = Width;
            _savedHeight = Height;
            _hasSavedBounds = true;
        }

        private void ResizeRight(object sender, DragDeltaEventArgs e)
        {
            Width = Math.Max(MinWidth, Width + e.HorizontalChange);
        }

        private void ResizeBottom(object sender, DragDeltaEventArgs e)
        {
            Height = Math.Max(MinHeight, Height + e.VerticalChange);
        }

        private void ResizeCorner(object sender, DragDeltaEventArgs e)
        {
            ResizeRight(sender, e);
            ResizeBottom(sender, e);
        }

        private static void AddResizeThumb(Grid host, HorizontalAlignment horizontalAlignment, VerticalAlignment verticalAlignment, Cursor cursor, double width, double height, DragDeltaEventHandler onDrag)
        {
            var thumb = new Thumb
            {
                HorizontalAlignment = horizontalAlignment,
                VerticalAlignment = verticalAlignment,
                Background = Brushes.Transparent,
                Cursor = cursor
            };

            if (!double.IsNaN(width))
            {
                thumb.Width = width;
            }

            if (!double.IsNaN(height))
            {
                thumb.Height = height;
            }

            thumb.DragDelta += onDrag;
            host.Children.Add(thumb);
        }

        private void BringToFrontWithoutActivation()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoActivate | SwpNoMove | SwpNoSize | SwpShowWindow);
        }

        private static Button CreateWindowButton(string text, Color accent, RoutedEventHandler onClick)
        {
            var button = new Button
            {
                MinWidth = 38,
                MinHeight = 24,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromArgb(255, 18, 25, 40)),
                Foreground = new SolidColorBrush(accent),
                BorderBrush = new SolidColorBrush(accent),
                BorderThickness = new Thickness(1.1),
                Cursor = Cursors.Hand,
                Padding = new Thickness(4, 0, 4, 0)
            };
            button.Content = new TextBlock
            {
                Text = text,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (onClick != null)
            {
                button.Click += onClick;
            }
            return button;
        }

        private sealed class FloatingToggleVisual
        {
            internal FloatingToggleVisual(Border track, Border thumb, TextBlock stateText)
            {
                Track = track;
                Thumb = thumb;
                StateText = stateText;
            }

            internal Border Track { get; }

            internal Border Thumb { get; }

            internal TextBlock StateText { get; }

            internal bool IsOn { get; set; }
        }
    }
}

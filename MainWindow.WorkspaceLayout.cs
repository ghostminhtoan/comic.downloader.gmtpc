using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private enum AppSection
        {
            ChooseSource,
            Download,
            Watch,
            About,
            Update
        }

        private Grid _shellRootGrid;
        private Border _sectionContentBorder;
        private ContentControl _sectionContentHost;
        private StackPanel _sidebarToolsHost;
        private StackPanel _globalDownloadActionPanel;
        private StackPanel _headerCommandHost;
        private StackPanel _navigationButtonHost;
        private readonly Dictionary<AppSection, Button> _navigationButtons = new Dictionary<AppSection, Button>();
        private FrameworkElement _chooseSourceSection;
        private FrameworkElement _downloadSection;
        private FrameworkElement _watchSection;
        private FrameworkElement _aboutSection;
        private FrameworkElement _updateSection;
        private TextBlock _aboutContentText;
        private TextBlock _updateContentText;
        private TextBlock _updateStatusText;
        private Button _btnCheckUpdates;
        private Button _btnInstallLatest;
        private readonly Dictionary<string, string> _createSubfolderByDomain = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private AppSection _currentSection = AppSection.ChooseSource;
        private bool _workspaceShellInitialized;
        private Button _showFloatRailButton;
        private bool _createSubfolderUiReady;
        private bool _suppressCreateSubfolderEvents;
        private string _createSubfolderSelectedDomainKey;
        private Border _sectionHeaderBorder;
        private Border _navigationRailBorder;
        private Button _toolbarClearTempButton;

        private void InitializeWorkspaceShell()
        {
            if (_workspaceShellInitialized || gridMainContent == null || headerPanel == null || leftPanelHost == null || borderRightPanel == null)
            {
                return;
            }

            ConfigureHeaderPanelLayout();
            RelocateDaomeodenToHentaiTab();
            BuildScalePresetCard();
            BuildGlobalDownloadToolbar();
            BuildModernShell();
            InitializeCreateSubfolderControls();
            ApplyInitialWindowSizing();

            _workspaceShellInitialized = true;
            UpdateWorkspaceShellLanguage();
            SelectAppSection(AppSection.Download);
        }

        private void ConfigureHeaderPanelLayout()
        {
            if (!(headerPanel.Child is Grid headerGrid))
            {
                return;
            }

            txtHeaderTitle.Visibility = Visibility.Visible;

            if (txtHeaderSubtitle != null)
            {
                txtHeaderSubtitle.Visibility = Visibility.Visible;
            }

            if (headerStepsPanel != null)
            {
                headerStepsPanel.Visibility = Visibility.Collapsed;
            }

            while (headerGrid.RowDefinitions.Count < 3)
            {
                headerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            if (_headerCommandHost == null)
            {
                _headerCommandHost = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Margin = new Thickness(0, 12, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                };
            }
        }

        private void BuildScalePresetCard()
        {
            if (scaleCard == null)
            {
                return;
            }

            var stack = new StackPanel();

            var titleText = new TextBlock
            {
                Text = _isVietnameseUi ? "TỶ LỆ HIỂN THỊ" : "DISPLAY SCALE",
                Foreground = (Brush)TryFindResource("CyberpunkMutedTextBrush"),
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            };
            stack.Children.Add(titleText);

            _dpiPresetCombo = new ComboBox
            {
                Name = "cmbDisplayDpi",
                Style = TryFindResource("CyberpunkComboBox") as Style,
                ItemContainerStyle = TryFindResource("CyberpunkComboBoxItemStyle") as Style,
                Height = 22,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                ItemsSource = UiZoomPresets.Select(percent => new UiZoomPreset(percent)).ToList()
            };

            _dpiPresetCombo.SelectionChanged += DpiPresetCombo_SelectionChanged;
            stack.Children.Add(_dpiPresetCombo);

            scaleCard.Child = stack;
            UpdateZoomDisplay();
        }

        private void BuildGlobalDownloadToolbar()
        {
            if (_globalDownloadActionPanel == null)
            {
                _globalDownloadActionPanel = new StackPanel
                {
                    Name = "headerDownloadActionsPanel",
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
            }

            if (floatingDownloadActionsHost != null)
            {
                RemoveFromParent(_globalDownloadActionPanel);
                if (!floatingDownloadActionsHost.Children.Contains(_globalDownloadActionPanel))
                {
                    floatingDownloadActionsHost.Children.Add(_globalDownloadActionPanel);
                }
            }

            if (grdStartDownloadToggle != null)
            {
                RemoveFromParent(grdStartDownloadToggle);
                grdStartDownloadToggle.Visibility = Visibility.Visible;
                grdStartDownloadToggle.Margin = new Thickness(0, 0, 12, 0);
                if (!_globalDownloadActionPanel.Children.Contains(grdStartDownloadToggle))
                {
                    _globalDownloadActionPanel.Children.Insert(0, grdStartDownloadToggle);
                }
            }

            if (grdAutoRetryErrorsToggle != null)
            {
                RemoveFromParent(grdAutoRetryErrorsToggle);
                grdAutoRetryErrorsToggle.Visibility = Visibility.Visible;
                grdAutoRetryErrorsToggle.Margin = new Thickness(0, 0, 12, 0);
                int insertIndex = _globalDownloadActionPanel.Children.Contains(grdStartDownloadToggle) ? 1 : 0;
                if (!_globalDownloadActionPanel.Children.Contains(grdAutoRetryErrorsToggle))
                {
                    _globalDownloadActionPanel.Children.Insert(insertIndex, grdAutoRetryErrorsToggle);
                }
            }

            if (_toolbarClearTempButton == null)
            {
                _toolbarClearTempButton = CreateCompactToolbarToggleButton("CLEAR TEMP", BtnClearTempFloating_Click);
                _toolbarClearTempButton.Content = "CLEAR TEMP";
                _toolbarClearTempButton.ToolTip = "CLEAR TEMP";
            }
            if (_toolbarClearTempButton != null)
            {
                RemoveFromParent(_toolbarClearTempButton);
                if (!_globalDownloadActionPanel.Children.Contains(_toolbarClearTempButton))
                {
                    int insertIndex = 0;
                    if (_globalDownloadActionPanel.Children.Contains(grdStartDownloadToggle)) insertIndex++;
                    if (_globalDownloadActionPanel.Children.Contains(grdAutoRetryErrorsToggle)) insertIndex++;
                    _globalDownloadActionPanel.Children.Insert(insertIndex, _toolbarClearTempButton);
                }
            }

            MoveToolbarElement(txtBuildInfo, new Thickness(8, 0, 12, 0));
            MoveToolbarElement(btnRetryErrorLog, new Thickness(0, 0, 6, 0));
            MoveToolbarElement(btnShutdownMenu?.Parent as UIElement ?? btnShutdownMenu, new Thickness(0, 0, 6, 0));

            UpdateCompactDownloadToolbarState();
        }

        private void EnsureCompactDownloadToolbarButtons()
        {
        }

        private Button CreateCompactToolbarToggleButton(string label, RoutedEventHandler onClick)
        {
            var button = new Button
            {
                MinWidth = 74,
                Height = 20,
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(6, 0, 6, 0),
                FontSize = 9.0,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(1.1),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            button.Click += onClick;
            SetCompactToolbarToggleVisual(button, label, false);
            return button;
        }

        private void SetCompactToolbarToggleVisual(Button button, string label, bool isOn)
        {
            if (button == null)
            {
                return;
            }

            Color accent = isOn ? Color.FromRgb(0x00, 0xE5, 0xFF) : Color.FromRgb(0xFF, 0x6C, 0x6C);
            button.Background = new SolidColorBrush(Color.FromArgb(255, 18, 25, 40));
            button.Foreground = new SolidColorBrush(accent);
            button.BorderBrush = new SolidColorBrush(accent);
            button.Content = $"{label} {(isOn ? "ON" : "OFF")}";
            button.ToolTip = button.Content;
        }

        internal void UpdateCompactDownloadToolbarState()
        {
            if (btnStartDownload != null)
            {
                _suppressDownloadToggleEvent = true;
                try
                {
                    btnStartDownload.IsChecked = _downloadCts != null;
                }
                finally
                {
                    _suppressDownloadToggleEvent = false;
                }
            }
        }

        private void BtnClearTempFloating_Click(object sender, RoutedEventArgs e)
        {
            ClearTempRootFolder(PortablePaths.PortableTempRoot);

            string downloadRoot = txtDownloadPath?.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(downloadRoot))
            {
                BtnClearTemp_Click(sender, e);
                return;
            }

            lblStatus.Text = _isVietnameseUi ? "Đã xóa .tmp." : "Cleared .tmp.";
        }

        private void RelocateDaomeodenToHentaiTab()
        {
            if (tabManga == null || tabHentai == null)
            {
                return;
            }

            TabItem daomeodenTab = null;
            foreach (object item in tabManga.Items)
            {
                if (item is TabItem tabItem &&
                    string.Equals(tabItem.Header?.ToString(), "daomeoden", StringComparison.OrdinalIgnoreCase))
                {
                    daomeodenTab = tabItem;
                    break;
                }
            }

            if (daomeodenTab == null || tabHentai.Items.Contains(daomeodenTab))
            {
                return;
            }

            tabManga.Items.Remove(daomeodenTab);
            tabHentai.Items.Add(daomeodenTab);
        }

        private void CompactHeaderPanelButtons(Panel panel, bool isPrimaryRow)
        {
            // No-op or unused now
        }

        private void MoveToolbarElement(UIElement element, Thickness margin)
        {
            if (element == null || _globalDownloadActionPanel == null)
            {
                return;
            }

            if (element is FrameworkElement frameworkElement)
            {
                RemoveFromParent(frameworkElement);
                frameworkElement.Margin = margin;
                frameworkElement.VerticalAlignment = VerticalAlignment.Center;
                frameworkElement.HorizontalAlignment = HorizontalAlignment.Left;

                Button button = null;
                if (frameworkElement is Button btn)
                {
                    button = btn;
                }
                else if (frameworkElement is Panel p)
                {
                    button = p.Children.OfType<Button>().FirstOrDefault();
                }

                if (button != null)
                {
                    button.MinWidth = ReferenceEquals(button, btnShutdownMenu) ? 32 : 56;
                    button.Height = 20;
                    button.FontSize = ReferenceEquals(button, btnShutdownMenu) ? 12 : 9.0;
                    button.Padding = new Thickness(4, 0, 4, 0);
                    button.VerticalAlignment = VerticalAlignment.Center;
                }

                if (!_globalDownloadActionPanel.Children.Contains(frameworkElement))
                {
                    _globalDownloadActionPanel.Children.Add(frameworkElement);
                }
            }
        }

        private void BuildModernShell()
        {
            gridMainContent.Children.Clear();
            gridMainContent.RowDefinitions.Clear();
            gridMainContent.ColumnDefinitions.Clear();

            gridMainContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            gridMainContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            gridMainContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            gridMainContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            gridMainContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            headerPanel.Visibility = Visibility.Collapsed;

            _shellRootGrid = new Grid();
            _shellRootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _shellRootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            _sectionHeaderBorder = new Border
            {
                Background = (Brush)TryFindResource("CyberpunkCardBrush") ?? new SolidColorBrush(Color.FromRgb(0x0D, 0x12, 0x1F)),
                BorderBrush = (Brush)TryFindResource("CyberpunkBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16, 10, 16, 10),
                Margin = new Thickness(0, 0, 0, 10)
            };

            var sectionHeaderStack = new StackPanel();
            _sectionTitleText = new TextBlock
            {
                Foreground = (Brush)TryFindResource("CyberpunkTextBrush"),
                FontSize = 18,
                FontWeight = FontWeights.Bold
            };
            _sectionHintText = new TextBlock
            {
                Foreground = (Brush)TryFindResource("CyberpunkMutedTextBrush"),
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            sectionHeaderStack.Children.Add(_sectionTitleText);
            sectionHeaderStack.Children.Add(_sectionHintText);
            _sectionHeaderBorder.Child = sectionHeaderStack;

            _sectionContentBorder = new Border
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0)
            };
            _sectionContentHost = new ContentControl();
            _sectionContentBorder.Child = _sectionContentHost;

            Grid.SetRow(_sectionHeaderBorder, 0);
            Grid.SetRow(_sectionContentBorder, 1);
            _shellRootGrid.Children.Add(_sectionHeaderBorder);
            _shellRootGrid.Children.Add(_sectionContentBorder);

            Grid.SetColumn(_shellRootGrid, 2);
            Grid.SetRow(_shellRootGrid, 1);
            gridMainContent.Children.Add(_shellRootGrid);

            BuildNavigationRail();
            BuildSectionViews();

            if (floatingDownloadActionsHost != null)
            {
                RemoveFromParent(floatingDownloadActionsHost);
                Grid.SetColumn(floatingDownloadActionsHost, 0);
                Grid.SetRow(floatingDownloadActionsHost, 1);
                Panel.SetZIndex(floatingDownloadActionsHost, 99);
                floatingDownloadActionsHost.VerticalAlignment = VerticalAlignment.Bottom;
                floatingDownloadActionsHost.HorizontalAlignment = HorizontalAlignment.Right;
                floatingDownloadActionsHost.Margin = new Thickness(0, 0, 18, 12);
                _shellRootGrid.Children.Add(floatingDownloadActionsHost);
            }
        }

        private void BuildNavigationRail()
        {
            _navigationRailBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x06, 0x09, 0x0F)),
                BorderBrush = (Brush)TryFindResource("CyberpunkBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 10, 12, 10),
                Margin = new Thickness(0)
            };

            var navStack = new StackPanel();
            _navigationRailBorder.Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                CanContentScroll = false,
                Padding = new Thickness(0, 0, 4, 0),
                Content = navStack
            };

            var brandTitle = new TextBlock
            {
                Text = "MANGA DESK",
                Foreground = (Brush)TryFindResource("CyberpunkTextBrush"),
                FontSize = 15,
                FontWeight = FontWeights.Bold
            };

            _showFloatRailButton = new Button
            {
                Width = 116,
                MinHeight = 32,
                Margin = new Thickness(0, 8, 0, 8),
                Style = TryFindResource("SidebarMenuButton") as Style
            };
            _showFloatRailButton.Click += BtnShowLightNovelFloatButton_Click;

            _navigationButtonHost = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
            _sidebarToolsHost = new StackPanel { Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };

            navStack.Children.Add(brandTitle);
            navStack.Children.Add(_showFloatRailButton);
            navStack.Children.Add(_navigationButtonHost);
            navStack.Children.Add(_sidebarToolsHost);

            AddNavigationButton(AppSection.ChooseSource, "Source", "Ctrl+Shift+S");
            AddNavigationButton(AppSection.Download, "Download", "Ctrl+Shift+D");
            AddNavigationButton(AppSection.Watch, "Watch", "Ctrl+Shift+W");
            AddNavigationButton(AppSection.About, "About", "Ctrl+Shift+A");
            AddNavigationButton(AppSection.Update, "Update", "Ctrl+Shift+U");

            BuildSidebarToolSections();

            Grid.SetColumn(_navigationRailBorder, 0);
            Grid.SetRow(_navigationRailBorder, 0);
            Grid.SetRowSpan(_navigationRailBorder, 2);
            gridMainContent.Children.Add(_navigationRailBorder);
        }

        private void AddNavigationButton(AppSection section, string text, string shortcut)
        {
            var button = new Button
            {
                Width = 116,
                MinHeight = 58,
                HorizontalAlignment = HorizontalAlignment.Center,
                Style = TryFindResource("SidebarMenuButton") as Style
            };

            button.Content = CreateNavigationButtonContent(text, shortcut);

            button.Click += (sender, args) => SelectAppSection(section);
            _navigationButtons[section] = button;
            _navigationButtonHost.Children.Add(button);
        }

        private static UIElement CreateNavigationButtonContent(string text, string shortcut)
        {
            return new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = text,
                        TextWrapping = TextWrapping.NoWrap,
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        FontSize = 10.2,
                        FontWeight = FontWeights.SemiBold
                    },
                    new TextBlock
                    {
                        Text = shortcut,
                        TextWrapping = TextWrapping.NoWrap,
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0x9E, 0xB2)),
                        FontSize = 9.2,
                        Margin = new Thickness(0, 2, 0, 0)
                    }
                }
            };
        }

        private void BuildSidebarToolSections()
        {
            if (_sidebarToolsHost == null)
            {
                return;
            }

            _sidebarToolsHost.Children.Clear();

            if (headerUtilityPanel != null)
            {
                RemoveFromParent(headerUtilityPanel);
                headerUtilityPanel.Margin = new Thickness(0, 0, 0, 8);
                headerUtilityPanel.MinWidth = 0;
                headerUtilityPanel.MaxWidth = double.PositiveInfinity;
                headerUtilityPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
                _sidebarToolsHost.Children.Add(headerUtilityPanel);
            }

            if (languageCard != null)
            {
                languageCard.Width = double.NaN;
                languageCard.MinWidth = 0;
                languageCard.MaxWidth = double.PositiveInfinity;
                languageCard.HorizontalAlignment = HorizontalAlignment.Left;
            }

            if (scaleCard != null)
            {
                scaleCard.Width = double.NaN;
                scaleCard.MinWidth = 0;
                scaleCard.MaxWidth = double.PositiveInfinity;
                scaleCard.HorizontalAlignment = HorizontalAlignment.Left;
            }

            if (headerActionsPanel != null)
            {
                RemoveFromParent(headerActionsPanel);
                headerActionsPanel.Margin = new Thickness(0, 0, 0, 8);
                headerActionsPanel.Orientation = Orientation.Vertical;
                headerActionsPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
                CompactHeaderPanelButtons(headerActionsPanel, true);
                _sidebarToolsHost.Children.Add(headerActionsPanel);
            }
        }

        private void BuildSectionViews()
        {
            _chooseSourceSection = CreateChooseSourceSection();
            _downloadSection = borderRightPanel;
            InitializeLightNovelDesk();
            _watchSection = CreateWatchSection();
            _aboutSection = CreateAboutSection();
            _updateSection = CreateUpdateSection();
        }

        private FrameworkElement CreateChooseSourceSection()
        {
            RemoveFromParent(leftPanelHost);
            return leftPanelHost;
        }

        private string GetCreateSubfolderSettingsPath()
        {
            return Path.Combine(PortablePaths.PortableDataRoot, "create-subfolders.txt");
        }

        private void InitializeCreateSubfolderControls()
        {
            if (_createSubfolderUiReady || cmbCreateSubfolderDomain == null || txtCreateSubfolderName == null)
            {
                return;
            }

            PopulateCreateSubfolderDomainCombo();
            LoadCreateSubfolderSettings();

            _createSubfolderUiReady = true;
            SyncCreateSubfolderDomainSelection();
            UpdateCreateSubfolderFieldsFromSelection();
            UpdateCreateSubfolderLanguage();
        }

        private void PopulateCreateSubfolderDomainCombo()
        {
            if (cmbCreateSubfolderDomain == null || cmbCreateSubfolderDomain.Items.Count > 0)
            {
                return;
            }

            AddCreateSubfolderDomainItem("truyenqq");
            AddCreateSubfolderDomainItem("nettruyen");
            AddCreateSubfolderDomainItem("daomeoden.net");
            AddCreateSubfolderDomainItem("ln.hako.vn");
            AddCreateSubfolderDomainItem("truyenggvn");
            AddCreateSubfolderDomainItem("sayhentai");
            AddCreateSubfolderDomainItem("vi-hentai.pro");
            AddCreateSubfolderDomainItem("nhentai.xxx");
            AddCreateSubfolderDomainItem("hentaiforce.net");
            AddCreateSubfolderDomainItem("hentaiera.com");
            AddCreateSubfolderDomainItem("hentai2read.com");
        }

        private void AddCreateSubfolderDomainItem(string domainKey)
        {
            var item = new ComboBoxItem
            {
                Tag = domainKey,
                Content = domainKey,
                Style = FindResource("CyberpunkComboBoxItemStyle") as Style
            };

            cmbCreateSubfolderDomain.Items.Add(item);
        }

        private void LoadCreateSubfolderSettings()
        {
            string settingsPath = GetCreateSubfolderSettingsPath();
            if (!File.Exists(settingsPath))
            {
                return;
            }

            var loadedSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string line in File.ReadAllLines(settingsPath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                int split = line.IndexOf('|');
                if (split <= 0)
                {
                    continue;
                }

                string domainKey = line.Substring(0, split).Trim();
                string encodedSubfolder = line.Substring(split + 1).Trim();
                if (string.IsNullOrWhiteSpace(domainKey))
                {
                    continue;
                }

                string subfolder = string.Empty;
                if (!string.IsNullOrWhiteSpace(encodedSubfolder))
                {
                    try
                    {
                        subfolder = Uri.UnescapeDataString(encodedSubfolder);
                    }
                    catch
                    {
                        subfolder = encodedSubfolder;
                    }
                }

                loadedSettings[domainKey] = subfolder;
            }

            foreach (var pair in loadedSettings)
            {
                if (!_createSubfolderByDomain.ContainsKey(pair.Key))
                {
                    _createSubfolderByDomain[pair.Key] = pair.Value;
                }
            }
        }

        private void SaveCreateSubfolderSettings()
        {
            string settingsPath = GetCreateSubfolderSettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));

            var lines = _createSubfolderByDomain
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .Select(pair => $"{pair.Key}|{Uri.EscapeDataString(pair.Value ?? string.Empty)}")
                .ToArray();

            File.WriteAllLines(settingsPath, lines, Encoding.UTF8);
        }

        private string GetSelectedCreateSubfolderDomainKey()
        {
            if (cmbCreateSubfolderDomain?.SelectedItem is ComboBoxItem selectedItem)
            {
                return selectedItem.Tag as string ?? selectedItem.Content?.ToString();
            }

            return null;
        }

        private void SyncCreateSubfolderDomainSelection()
        {
            if (cmbCreateSubfolderDomain == null || cmbCreateSubfolderDomain.Items.Count == 0)
            {
                return;
            }

            string currentDomainKey = GetSelectedCreateSubfolderDomainKey();
            if (string.IsNullOrWhiteSpace(currentDomainKey))
            {
                currentDomainKey = "truyenqq";
            }

            foreach (ComboBoxItem item in cmbCreateSubfolderDomain.Items)
            {
                if (string.Equals(item.Tag as string, currentDomainKey, StringComparison.OrdinalIgnoreCase))
                {
                    _suppressCreateSubfolderEvents = true;
                    try
                    {
                        cmbCreateSubfolderDomain.SelectedItem = item;
                    }
                    finally
                    {
                        _suppressCreateSubfolderEvents = false;
                    }
                    return;
                }
            }

            _suppressCreateSubfolderEvents = true;
            try
            {
                cmbCreateSubfolderDomain.SelectedIndex = 0;
            }
            finally
            {
                _suppressCreateSubfolderEvents = false;
            }
        }

        private void UpdateCreateSubfolderFieldsFromSelection()
        {
            if (!_createSubfolderUiReady || cmbCreateSubfolderDomain == null || txtCreateSubfolderName == null)
            {
                return;
            }

            string domainKey = GetSelectedCreateSubfolderDomainKey();
            if (string.IsNullOrWhiteSpace(domainKey))
            {
                return;
            }

            _createSubfolderSelectedDomainKey = domainKey;

            string subfolder = string.Empty;
            _createSubfolderByDomain.TryGetValue(domainKey, out subfolder);

            _suppressCreateSubfolderEvents = true;
            try
            {
                txtCreateSubfolderName.Text = subfolder ?? string.Empty;
            }
            finally
            {
                _suppressCreateSubfolderEvents = false;
            }
        }

        private void PersistCreateSubfolderForDomain(string domainKey)
        {
            if (!_createSubfolderUiReady || string.IsNullOrWhiteSpace(domainKey))
            {
                return;
            }

            string subfolder = txtCreateSubfolderName?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(subfolder))
            {
                _createSubfolderByDomain.Remove(domainKey);
            }
            else
            {
                _createSubfolderByDomain[domainKey] = subfolder;
            }

            SaveCreateSubfolderSettings();
        }

        private void UpdateCreateSubfolderLanguage()
        {
            if (txtCreateSubfolderTitle != null)
            {
                txtCreateSubfolderTitle.Text = _isVietnameseUi ? "TẠO THƯ MỤC CON" : "CREATE SUBFOLDER";
            }

            if (txtCreateSubfolderDomainLabel != null)
            {
                txtCreateSubfolderDomainLabel.Text = _isVietnameseUi ? "MIỀN" : "DOMAIN";
            }

            if (txtCreateSubfolderNameLabel != null)
            {
                txtCreateSubfolderNameLabel.Text = _isVietnameseUi ? "TÊN THƯ MỤC CON" : "SUBFOLDER NAME";
            }

            if (btnApplyCreateSubfolder != null)
            {
                btnApplyCreateSubfolder.Content = _isVietnameseUi ? "ÁP DỤNG" : "APPLY";
            }
        }


        private void TxtCreateSubfolderName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressCreateSubfolderEvents || !_createSubfolderUiReady)
            {
                return;
            }

            PersistCreateSubfolderForDomain(_createSubfolderSelectedDomainKey ?? GetSelectedCreateSubfolderDomainKey());
        }

        private void BtnApplyCreateSubfolder_Click(object sender, RoutedEventArgs e)
        {
            string domainKey = _createSubfolderSelectedDomainKey ?? GetSelectedCreateSubfolderDomainKey();
            if (string.IsNullOrWhiteSpace(domainKey))
            {
                return;
            }

            PersistCreateSubfolderForDomain(domainKey);

            string downloadRoot = txtDownloadPath?.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(downloadRoot))
            {
                string targetFolder = GetConfiguredDownloadRoot(downloadRoot, domainKey);
                if (!string.IsNullOrWhiteSpace(targetFolder))
                {
                    Directory.CreateDirectory(targetFolder);
                }
            }

            string appliedSubfolder = GetCreateSubfolderPath(domainKey);
            string suffix = string.IsNullOrWhiteSpace(appliedSubfolder) ? "(root site folder)" : appliedSubfolder;
            Log($"[Subfolder] Applied for {domainKey}: {suffix}");
            lblStatus.Text = _isVietnameseUi
                ? $"Đã áp dụng subfolder cho {domainKey}: {suffix}"
                : $"Applied subfolder for {domainKey}: {suffix}";
        }

        private FrameworkElement CreateAboutSection()
        {
            var border = new Border
            {
                Background = (Brush)TryFindResource("CyberpunkCardBrush") ?? new SolidColorBrush(Color.FromRgb(0x0D, 0x12, 0x1F)),
                BorderBrush = (Brush)TryFindResource("CyberpunkBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(18)
            };

            _aboutContentText = new TextBlock
            {
                Foreground = (Brush)TryFindResource("CyberpunkTextBrush"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };

            border.Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _aboutContentText
            };

            return border;
        }

        private FrameworkElement CreateUpdateSection()
        {
            var root = new StackPanel();

            var card = new Border
            {
                Background = (Brush)TryFindResource("CyberpunkCardBrush") ?? new SolidColorBrush(Color.FromRgb(0x0D, 0x12, 0x1F)),
                BorderBrush = (Brush)TryFindResource("CyberpunkBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(18)
            };

            _updateContentText = new TextBlock
            {
                Foreground = (Brush)TryFindResource("CyberpunkTextBrush"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            var cardStack = new StackPanel();
            cardStack.Children.Add(_updateContentText);

            _updateStatusText = new TextBlock
            {
                Foreground = (Brush)TryFindResource("CyberpunkMutedTextBrush") ?? (Brush)TryFindResource("CyberpunkTextBrush"),
                FontSize = 11,
                Margin = new Thickness(0, 12, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            cardStack.Children.Add(_updateStatusText);

            card.Child = cardStack;

            var buttonRow = new WrapPanel
            {
                Margin = new Thickness(0, 12, 0, 0)
            };
            _btnCheckUpdates = new Button
            {
                Style = TryFindResource("CompactCyanButton") as Style,
                MinWidth = 168
            };
            _btnCheckUpdates.Click += BtnCheckUpdates_Click;
            buttonRow.Children.Add(_btnCheckUpdates);

            _btnInstallLatest = new Button
            {
                Style = TryFindResource("CompactPinkButton") as Style,
                MinWidth = 168
            };
            _btnInstallLatest.Click += BtnInstallLatest_Click;
            buttonRow.Children.Add(_btnInstallLatest);

            buttonRow.Children.Add(CreatePathButton("Open app root", PortablePaths.AppRoot));
            buttonRow.Children.Add(CreatePathButton("Open download root", PortablePaths.DefaultDownloadRoot));

            root.Children.Add(card);
            root.Children.Add(buttonRow);

            RefreshUpdateSectionContent();

            return root;
        }

        private Button CreatePathButton(string text, string path)
        {
            var button = new Button
            {
                Content = text,
                Style = TryFindResource("CompactCyanButton") as Style,
                MinWidth = 136,
                Tag = path
            };

            button.Click += (sender, args) =>
            {
                string targetPath = button.Tag as string;
                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    return;
                }

                if (!Directory.Exists(targetPath))
                {
                    Directory.CreateDirectory(targetPath);
                }

                if (!ShellFolderLauncher.TryOpenFolder(targetPath, out string error))
                {
                    MessageBox.Show($"Cannot open folder: {error}", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };

            return button;
        }

        private void SelectAppSection(AppSection section)
        {
            if (section != AppSection.Watch && _isReaderFullscreen)
            {
                ToggleReaderFullscreen();
            }

            StopReaderAutoRefresh();
            _currentSection = section;
            UpdateNavigationSelection();
            UpdateSectionHeader();
            PrepareSectionLayout(section);

            switch (section)
            {
                case AppSection.ChooseSource:
                    _sectionContentHost.Content = _chooseSourceSection;
                    break;
                case AppSection.Download:
                    _sectionContentHost.Content = _downloadSection;
                    break;
                case AppSection.Watch:
                    _sectionContentHost.Content = _watchSection;
                    _readerHasUserClickedInWatch = false;
                    EnsureReaderReady();
                    RefreshReaderLibraryIfNeeded(forceRefresh: false);
                    PromptReaderWatchAppSelectionIfNeeded();
                    break;
                case AppSection.About:
                    _sectionContentHost.Content = _aboutSection;
                    StopReaderAutoRefresh();
                    break;
                case AppSection.Update:
                    _sectionContentHost.Content = _updateSection;
                    StopReaderAutoRefresh();
                    break;
            }
        }

        private void PrepareSectionLayout(AppSection section)
        {
            if (floatingDownloadActionsHost != null)
            {
                floatingDownloadActionsHost.Visibility = section == AppSection.Download
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (section == AppSection.ChooseSource)
            {
                return;
            }

            RemoveFromParent(borderRightPanel);
        }

        private void UpdateNavigationSelection()
        {
            foreach (var pair in _navigationButtons)
            {
                bool isActive = pair.Key == _currentSection;
                pair.Value.Background = isActive
                    ? new SolidColorBrush(Color.FromRgb(0x12, 0x22, 0x38))
                    : Brushes.Transparent;
                pair.Value.BorderBrush = isActive
                    ? (Brush)TryFindResource("CyberpunkCyanBrush")
                    : (Brush)TryFindResource("CyberpunkBorderBrush");
                pair.Value.Foreground = isActive
                    ? (Brush)TryFindResource("CyberpunkCyanBrush")
                    : (Brush)TryFindResource("CyberpunkTextBrush");
            }
        }

        private void UpdateSectionHeader()
        {
            if (_sectionTitleText == null || _sectionHintText == null)
            {
                return;
            }

            bool isVietnamese = _isVietnameseUi;

            switch (_currentSection)
            {
                case AppSection.ChooseSource:
                    _sectionTitleText.Text = "Source";
                    _sectionHintText.Text = isVietnamese
                        ? "Chọn web nguồn bằng thẻ nhanh hoặc dùng form site cũ bên dưới. Toàn bộ parser và paste flow hiện tại được giữ nguyên."
                        : "Pick a source with quick cards or keep using the proven site forms below. Existing parsers and direct-paste flows stay intact.";
                    break;
                case AppSection.Download:
                    _sectionTitleText.Text = isVietnamese ? "Hàng chờ tải" : "Download queue";
                    _sectionHintText.Text = isVietnamese
                        ? "Kiểm tra danh sách, chọn chapter, theo dõi trạng thái, rồi tải hàng loạt với cơ chế resume hiện có."
                        : "Review queue, set chapter filters, track status, and download in bulk with the existing resume-safe pipeline.";
                    break;
                case AppSection.Watch:
                    _sectionTitleText.Text = isVietnamese ? "Đọc truyện offline" : "Watch offline";
                    _sectionHintText.Text = isVietnamese
                        ? "Quét thư mục tải, đọc ảnh ngay trong app, và tự động nhảy qua chapter kế tiếp hoặc trước đó."
                        : "Scan your download root, read images inside the app, and auto-bridge to the next or previous chapter.";
                    break;
                case AppSection.About:
                    _sectionTitleText.Text = isVietnamese ? "Giới thiệu" : "About";
                    _sectionHintText.Text = isVietnamese
                        ? "Tóm tắt luồng dùng app, định dạng hỗ trợ, và các phím tắt chính."
                        : "Quick guide for workflow, supported formats, and main shortcuts.";
                    break;
                case AppSection.Update:
                    _sectionTitleText.Text = isVietnamese ? "Cập nhật" : "Update";
                    _sectionHintText.Text = isVietnamese
                        ? "Xem build hiện tại, thư mục app, và điểm kiểm tra trước khi đóng gói."
                        : "See current build info, app paths, and quick package-check details.";
                    break;
            }
        }
        private static void RemoveFromParent(FrameworkElement element)
        {
            if (element == null)
            {
                return;
            }

            switch (element.Parent)
            {
                case Panel panel:
                    panel.Children.Remove(element);
                    break;
                case Decorator decorator when decorator.Child == element:
                    decorator.Child = null;
                    break;
                case ContentControl contentControl when ReferenceEquals(contentControl.Content, element):
                    contentControl.Content = null;
                    break;
            }
        }

        private void ApplyInitialWindowSizing()
        {
            ApplyPreferredWindowSize();
        }

        private void ApplyPreferredWindowSize()
        {
            Rect workArea = SystemParameters.WorkArea;
            bool portrait = workArea.Height > workArea.Width;

            MinWidth = portrait ? 860 : 1180;
            MinHeight = 720;
            MaxWidth = workArea.Width;
            MaxHeight = workArea.Height;

            double targetWidth;
            double targetHeight;

            if (portrait)
            {
                targetWidth = Math.Min(workArea.Width - 24, 980);
                targetHeight = Math.Min(workArea.Height - 24, 1380);
            }
            else
            {
                targetWidth = Math.Min(workArea.Width - 40, 1440);
                targetHeight = Math.Min(workArea.Height - 30, 920);
            }

            WindowState = WindowState.Normal;
            Width = Math.Max(MinWidth, targetWidth);
            Height = Math.Max(MinHeight, targetHeight);
            Left = workArea.Left + Math.Max(0, (workArea.Width - Width) / 2.0);
            Top = workArea.Top + Math.Max(0, (workArea.Height - Height) / 2.0);
        }

        private void UpdateWorkspaceShellLanguage()
        {
            if (tabMangaSourceRootItem != null)
            {
                tabMangaSourceRootItem.Header = _isVietnameseUi ? "Nguồn Manga" : "Manga Source";
            }
            if (tabLightNovelRootItem != null)
            {
                tabLightNovelRootItem.Header = _isVietnameseUi ? "Nguồn Novel" : "Novel Source";
            }
            if (tabDownloadRoot != null && tabDownloadRoot.Items.Count >= 2)
            {
                if (tabDownloadRoot.Items[0] is TabItem mangaTab)
                {
                    mangaTab.Header = _isVietnameseUi ? "Tải Manga" : "Download Manga";
                }
                if (tabDownloadRoot.Items[1] is TabItem novelTab)
                {
                    novelTab.Header = _isVietnameseUi ? "Tải Novel" : "Download Novel";
                }
            }

            if (_navigationButtons.Count > 0)
            {
                _navigationButtons[AppSection.ChooseSource].Content = CreateNavigationButtonContent("Source", "Ctrl+Shift+S");
                _navigationButtons[AppSection.Download].Content = CreateNavigationButtonContent(_isVietnameseUi ? "Tải về" : "Download", "Ctrl+Shift+D");
                _navigationButtons[AppSection.Watch].Content = CreateNavigationButtonContent(_isVietnameseUi ? "Xem truyện" : "Watch", "Ctrl+Shift+W");
                _navigationButtons[AppSection.About].Content = CreateNavigationButtonContent(_isVietnameseUi ? "Giới thiệu" : "About", "Ctrl+Shift+A");
                _navigationButtons[AppSection.Update].Content = CreateNavigationButtonContent(_isVietnameseUi ? "Cập nhật" : "Update", "Ctrl+Shift+U");
            }

            if (_showFloatRailButton != null)
            {
                _showFloatRailButton.Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x1A, 0x00));
                _showFloatRailButton.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xA6, 0x00));
                _showFloatRailButton.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD4, 0x6A));
                _showFloatRailButton.Content = CreateNavigationButtonContent("Float button", "Ctrl+Shift+F");
            }



            if (txtHeaderTitle != null)
            {
                txtHeaderTitle.Text = _isVietnameseUi ? "Manga Offline Desk" : "Manga Offline Desk";
            }

            if (txtHeaderSubtitle != null)
            {
                txtHeaderSubtitle.Text = _isVietnameseUi
                    ? "Giao diện mới tập trung vào dán link, tải truyện và đọc offline ngay trong app."
                    : "A rebuilt shell focused on paste-link workflows, bulk downloads, and offline reading inside the app.";
            }

            if (_aboutContentText != null)
            {
                _aboutContentText.Text = _isVietnameseUi
                    ? "Ứng dụng Manga Desk hỗ trợ cào, quản lý hàng chờ và đọc offline truyện tranh/tiểu thuyết:\n\n" +
                      "1. CHỌN NGUỒN / DÁN LINK:\n" +
                      "   - Hỗ trợ parser phong phú cho Manga & Hentai (TruyenQQ, NetTruyen, SayHentai, Vi-Hentai, NHentai, HentaiForce, HentaiEra, Hentai2Read, Daomeoden, v.v.).\n" +
                      "   - Nguồn Novel riêng để lấy text từ các trang như Hako (docln.net / ln.hako.vn) và tự động chuyển đổi sang định dạng Markdown (.md).\n\n" +
                      "2. HÀNG CHỜ TẢI VỀ:\n" +
                      "   - Quản lý tải song song đa luồng an toàn, hỗ trợ tạm dừng, tiếp tục tải (resume-safe) và tự động tải lại khi lỗi (auto-retry).\n" +
                      "   - Giao diện floating control window tiện ích (Pin topmost, chế độ Focus ẩn, tùy chỉnh Opacity, kích thước và điều khiển nhanh quá trình tải/copy).\n\n" +
                      "3. XEM TRUYỆN OFFLINE:\n" +
                      "   - Watch Manga: Đọc ảnh offline tiện lợi, hỗ trợ nhiều chế độ hiển thị (Fit Width, Fit Height, Original), cuộn dọc/ngang mượt mà, tự động chuyển chapter khi đọc đến trang cuối/đầu.\n" +
                      "   - Watch Novel: Trình xem trước file Markdown tích hợp, hỗ trợ mở nhanh trình đọc MD Reader bên thứ ba.\n" +
                      "   - Cho phép tải thư mục gốc (Root Folder) mặc định hoặc chọn thư mục tùy ý (Other Folder) để quét truyện.\n\n" +
                      "Phím tắt chính:\n" +
                      "   - Ctrl + Mouse Wheel (ở màn hình chính): Phóng to / thu nhỏ toàn bộ giao diện phần mềm.\n" +
                      "   - Các phím mũi tên Trái / Phải hoặc A / D (khi đang đọc truyện): Chuyển trang/chuyển chapter.\n\n" +
                      "Lưu ý:\n" +
                      "   - Giao diện được thiết kế tối ưu hóa hiển thị ở độ phân giải cơ bản 1360x768 (DPI 100%).\n" +
                      "   - Watch lists sẽ tự động cập nhật danh sách truyện mới ngay sau khi bạn tải xong."
                    : "Manga Desk provides advanced scraping, queue management, and offline reading/rendering for both manga and novels:\n\n" +
                      "1. CHOOSE SOURCE / PASTE LINK:\n" +
                      "   - High-fidelity parsers for Manga & Hentai sites (TruyenQQ, NetTruyen, SayHentai, Vi-Hentai, NHentai, HentaiForce, HentaiEra, Hentai2Read, etc.).\n" +
                      "   - Novel Source allows crawling text from Hako (docln.net / ln.hako.vn) and auto-converting chapters to Markdown (.md) format.\n\n" +
                      "2. DOWNLOAD QUEUE:\n" +
                      "   - Parallel multi-threaded downloading with resume-safe capabilities and auto-retry error handling.\n" +
                      "   - Companion floating control window (Pin topmost, Stealth Focus mode, customizable Opacity & Size, quick start/stop controls).\n\n" +
                      "3. WATCH OFFLINE:\n" +
                      "   - Watch Manga: In-app image viewer supporting Fit Width, Fit Height, and Original sizing. Smooth vertical/horizontal scroll. Auto chapter-bridge transitions at document edges.\n" +
                      "   - Watch Novel: Integrated Markdown previewer with quick options to install and open a standard MD Reader.\n" +
                      "   - Supports loading default Root Download Folder or browsing to custom library directories (Other Folder).\n\n" +
                      "Hotkeys:\n" +
                      "   - Ctrl + Mouse Wheel (main screen): Scale and zoom the entire application window.\n" +
                      "   - Left / Right or A / D keys (inside reader): Navigate pages or transition chapters.\n\n" +
                      "Notes:\n" +
                      "   - UI is optimized for a baseline resolution of 1360x768 (100% scale).\n" +
                      "   - Libraries in the Watch section automatically refresh upon successful download completions.";
            }

            if (_updateContentText != null)
            {
                _updateContentText.Text = (_isVietnameseUi ? "Build hiện tại" : "Current build") + $": {BuildInfo.DisplayText}\n\n" +
                                          (_isVietnameseUi ? "App root" : "App root") + $": {PortablePaths.AppRoot}\n" +
                                          (_isVietnameseUi ? "Download root mặc định" : "Default download root") + $": {PortablePaths.DefaultDownloadRoot}\n" +
                                          (_isVietnameseUi ? "WebView2 portable data" : "Portable WebView2 data") + $": {PortablePaths.WebView2UserDataFolder}\n\n" +
                                          (_isVietnameseUi
                                                ? "Checklist nhanh: build xong, quét Watch, mở thử vài chapter, rồi mới đóng gói."
                                              : "Quick checklist: build clean, refresh Watch, test a few chapter transitions, then package.");
            }

            UpdateReaderLanguage();
            UpdateCreateSubfolderLanguage();
            UpdateSectionHeader();
        }

        private void RefreshWindowBoundsForCurrentDisplay(bool preserveWindowState)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => RefreshWindowBoundsForCurrentDisplay(preserveWindowState));
                return;
            }

            Rect workArea = SystemParameters.WorkArea;
            bool portrait = workArea.Height > workArea.Width;

            bool wasMaximized = WindowState == WindowState.Maximized;
            if (wasMaximized)
            {
                MaxWidth = workArea.Width;
                MaxHeight = workArea.Height;

                if (preserveWindowState)
                {
                    WindowState = WindowState.Normal;
                    Width = Math.Max(MinWidth, Math.Min(workArea.Width, workArea.Width - 16));
                    Height = Math.Max(MinHeight, Math.Min(workArea.Height, workArea.Height - 16));
                    Left = workArea.Left;
                    Top = workArea.Top;
                    WindowState = WindowState.Maximized;
                }

                ApplyAdaptiveLayout(new Size(workArea.Width, workArea.Height));
                return;
            }

            MinWidth = portrait ? 860 : 1180;
            MinHeight = 720;
            MaxWidth = workArea.Width;
            MaxHeight = workArea.Height;

            if (WindowState != WindowState.Normal)
            {
                return;
            }

            Width = Math.Max(MinWidth, Math.Min(Width, workArea.Width));
            Height = Math.Max(MinHeight, Math.Min(Height, workArea.Height));
            Left = Math.Min(Math.Max(Left, workArea.Left), Math.Max(workArea.Left, workArea.Right - Width));
            Top = Math.Min(Math.Max(Top, workArea.Top), Math.Max(workArea.Top, workArea.Bottom - Height));
            ApplyAdaptiveLayout(new Size(ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height));
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            HandleFocusTrayWindowStateChanged();
            if (WindowState == WindowState.Maximized || WindowState == WindowState.Normal)
            {
                RefreshWindowBoundsForCurrentDisplay(preserveWindowState: false);
                ApplyAdaptiveLayout(new Size(ActualWidth, ActualHeight));
            }

            UpdateMainWindowChromeButtons();
        }

        private void SystemEvents_DisplaySettingsChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() => RefreshWindowBoundsForCurrentDisplay(preserveWindowState: true)));
        }
    }
}


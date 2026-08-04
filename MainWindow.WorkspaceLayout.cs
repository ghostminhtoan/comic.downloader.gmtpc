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
            TraceLog,
            Update,
            FinishOptions
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
        private FrameworkElement _traceLogSection;
        private FrameworkElement _updateSection;
        private FrameworkElement _finishOptionsSection;
        private RichTextBox txtLog;
        private ScrollViewer _scrollLogHost;
        private StackPanel _mdLogStackPanel;
        private System.Windows.Controls.Primitives.ToggleButton chkAutoScrollLog;
        private System.Windows.Controls.Primitives.ToggleButton chkErrorOnlyLog;
        private Button btnClearLog;
        private TextBlock _updateContentText;
        private TextBlock _updateStatusText;
        private Button _btnCheckUpdates;
        private Button _btnInstallLatest;
        private readonly Dictionary<string, string> _createSubfolderByDomain = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private AppSection _currentSection = AppSection.ChooseSource;
        private bool _workspaceShellInitialized;
        private Button _showFloatRailButton;
        private Button _h2rLogButton;
        private TextBlock _brandTitleText;
        private bool _createSubfolderUiReady;
        private bool _suppressCreateSubfolderEvents;
        private string _createSubfolderSelectedDomainKey;
        private Border _sectionHeaderBorder;
        private Border _navigationRailBorder;
        private Button _toolbarClearTempButton;

        private bool IsNovelDownloadTabSelected()
        {
            return tabDownloadRoot?.SelectedIndex == 2;
        }

        private bool IsSplitMergeFolderTabSelected()
        {
            return tabDownloadRoot?.SelectedIndex == 3;
        }

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

             var grid = new Grid();
             grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

             // Cột 1: Display Scale
             var leftStack = new StackPanel();
             var titleText = new TextBlock
             {
                 Text = _isVietnameseUi ? "TỶ LỆ HIỂN THỊ" : "DISPLAY SCALE",
                 Foreground = (Brush)TryFindResource("CyberpunkMutedTextBrush"),
                 FontSize = 9,
                 FontWeight = FontWeights.Bold,
                 TextWrapping = TextWrapping.Wrap,
                 Margin = new Thickness(0, 0, 0, 4)
             };
             leftStack.Children.Add(titleText);

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
             leftStack.Children.Add(_dpiPresetCombo);
             Grid.SetColumn(leftStack, 0);
             grid.Children.Add(leftStack);

             scaleCard.Child = grid;
             UpdateZoomDisplay();
         }

         private void SetTextScalePercent(double percent)
         {
             // No-op
         }

         internal static readonly DependencyProperty BaseFontSizeProperty =
             DependencyProperty.RegisterAttached("_BaseFontSize", typeof(double), typeof(MainWindow),
                 new PropertyMetadata(double.NaN));

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

            bool isNovelTab = IsNovelDownloadTabSelected();
            bool showDownloadActions = !isNovelTab && !IsSplitMergeFolderTabSelected();

            if (grdStartDownloadToggle != null)
            {
                RemoveFromParent(grdStartDownloadToggle);
                grdStartDownloadToggle.Visibility = showDownloadActions ? Visibility.Visible : Visibility.Collapsed;
                grdStartDownloadToggle.Margin = new Thickness(0, 0, 12, 0);
                if (showDownloadActions && !_globalDownloadActionPanel.Children.Contains(grdStartDownloadToggle))
                {
                    _globalDownloadActionPanel.Children.Insert(0, grdStartDownloadToggle);
                }
            }

            if (grdAutoRetryErrorsToggle != null)
            {
                RemoveFromParent(grdAutoRetryErrorsToggle);
                grdAutoRetryErrorsToggle.Visibility = showDownloadActions ? Visibility.Visible : Visibility.Collapsed;
                grdAutoRetryErrorsToggle.Margin = new Thickness(0, 0, 12, 0);
                int insertIndex = _globalDownloadActionPanel.Children.Contains(grdStartDownloadToggle) ? 1 : 0;
                if (showDownloadActions && !_globalDownloadActionPanel.Children.Contains(grdAutoRetryErrorsToggle))
                {
                    _globalDownloadActionPanel.Children.Insert(insertIndex, grdAutoRetryErrorsToggle);
                }
            }

            FrameworkElement copyTextContainer = btnStartCopyText?.Parent as FrameworkElement;
            if (copyTextContainer != null)
            {
                RemoveFromParent(copyTextContainer);
                copyTextContainer.Visibility = isNovelTab ? Visibility.Visible : Visibility.Collapsed;
                copyTextContainer.Margin = new Thickness(0, 0, 12, 0);
                if (isNovelTab && !_globalDownloadActionPanel.Children.Contains(copyTextContainer))
                {
                    _globalDownloadActionPanel.Children.Insert(0, copyTextContainer);
                }
            }

            if (_toolbarClearTempButton == null)
            {
                string clearTempText = _isVietnameseUi ? "XÓA TẠM" : "CLEAR TEMP";
                _toolbarClearTempButton = CreateCompactToolbarToggleButton(clearTempText, BtnClearTempFloating_Click);
                _toolbarClearTempButton.Content = clearTempText;
                _toolbarClearTempButton.ToolTip = clearTempText;
            }
            if (_toolbarClearTempButton != null)
            {
                RemoveFromParent(_toolbarClearTempButton);
                if (!_globalDownloadActionPanel.Children.Contains(_toolbarClearTempButton))
                {
                    int insertIndex = 0;
                    if (isNovelTab)
                    {
                        if (_globalDownloadActionPanel.Children.Contains(copyTextContainer)) insertIndex++;
                    }
                    else if (showDownloadActions)
                    {
                        if (_globalDownloadActionPanel.Children.Contains(grdStartDownloadToggle)) insertIndex++;
                        if (_globalDownloadActionPanel.Children.Contains(grdAutoRetryErrorsToggle)) insertIndex++;
                    }
                    _globalDownloadActionPanel.Children.Insert(insertIndex, _toolbarClearTempButton);
                }
            }

            MoveToolbarElement(txtBuildInfo, new Thickness(8, 0, 12, 0));
            MoveToolbarElement(btnRetryErrorLog, new Thickness(0, 0, 6, 0));

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

            if (btnStartCopyText != null)
            {
                btnStartCopyText.IsChecked = _lightNovelCopyCts != null && !_lightNovelCopyBackoffActive;
                btnStartCopyText.ToolTip = _isVietnameseUi
                    ? ((btnStartCopyText.IsChecked == true) ? "DỪNG COPY TEXT" : "COPY TEXT LIGHT NOVEL")
                    : ((btnStartCopyText.IsChecked == true) ? "STOP COPY TEXT" : "COPY LIGHT NOVEL TEXT");
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

            _brandTitleText = new TextBlock
            {
                Text = "COMIC DOWNLOADER GMTPC",
                Foreground = (Brush)TryFindResource("CyberpunkTextBrush"),
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap
            };

            _showFloatRailButton = new Button
            {
                Width = 56,
                MinHeight = 32,
                Margin = new Thickness(0, 0, 4, 0),
                Style = TryFindResource("SidebarMenuButton") as Style,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _showFloatRailButton.Click += BtnShowLightNovelFloatButton_Click;

            if (grdShutdownMenu != null)
            {
                var parent = grdShutdownMenu.Parent as Panel;
                if (parent != null)
                {
                    parent.Children.Remove(grdShutdownMenu);
                }
                grdShutdownMenu.Margin = new Thickness(4, 0, 0, 0);
                grdShutdownMenu.Width = 56;
                grdShutdownMenu.Height = 32;

                if (btnShutdownMenu != null)
                {
                    _navigationButtons[AppSection.FinishOptions] = btnShutdownMenu;
                    btnShutdownMenu.Style = TryFindResource("SidebarMenuButton") as Style;
                    btnShutdownMenu.Width = 56;
                    btnShutdownMenu.Height = 32;
                    btnShutdownMenu.MinHeight = 32;
                    btnShutdownMenu.Margin = new Thickness(0);
                    btnShutdownMenu.Padding = new Thickness(0);
                    btnShutdownMenu.HorizontalContentAlignment = HorizontalAlignment.Center;
                    btnShutdownMenu.VerticalContentAlignment = VerticalAlignment.Center;
                }
            }

            var floatAndFinishRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 8)
            };
            floatAndFinishRow.Children.Add(_showFloatRailButton);
            if (grdShutdownMenu != null)
            {
                floatAndFinishRow.Children.Add(grdShutdownMenu);
            }

            _h2rLogButton = new Button
            {
                Width = 116,
                MinHeight = 32,
                Margin = new Thickness(0, 0, 0, 8),
                Style = TryFindResource("SidebarMenuButton") as Style
            };
            _h2rLogButton.Click += BtnHentai2readShowLog_Click;

            _navigationButtonHost = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
            _sidebarToolsHost = new StackPanel { Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };

            navStack.Children.Add(_brandTitleText);
            navStack.Children.Add(floatAndFinishRow);
            navStack.Children.Add(_navigationButtonHost);
            navStack.Children.Add(_sidebarToolsHost);

            AddNavigationButton(AppSection.ChooseSource, _isVietnameseUi ? "Nguồn" : "Source", "Ctrl+Shift+S");
            AddNavigationButton(AppSection.Download, "Download", "Ctrl+Shift+D");
            AddNavigationButton(AppSection.Watch, "Watch", "Ctrl+Shift+W");
            AddNavigationButton(AppSection.About, _isVietnameseUi ? "Hướng dẫn" : "Tutorial", "Ctrl+Shift+A");
            AddNavigationButton(AppSection.TraceLog, "Trace Log", "Ctrl+Shift+L");
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

        private static UIElement CreateIconContent(string geometryData, Brush brush)
        {
            return new System.Windows.Shapes.Path
            {
                Data = System.Windows.Media.Geometry.Parse(geometryData),
                Fill = brush,
                Stretch = Stretch.Uniform,
                Width = 18,
                Height = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
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
        }

        private FrameworkElement EnsureWatchSection()
        {
            if (_watchSection == null)
            {
                _watchSection = CreateWatchSection();
            }

            return _watchSection;
        }

        private FrameworkElement EnsureAboutSection()
        {
            if (_aboutSection == null)
            {
                _aboutSection = CreateAboutSection();
            }

            return _aboutSection;
        }

        private FrameworkElement EnsureTraceLogSection()
        {
            if (_traceLogSection == null)
            {
                _traceLogSection = CreateTraceLogSection();
            }

            return _traceLogSection;
        }

        private FrameworkElement EnsureUpdateSection()
        {
            if (_updateSection == null)
            {
                _updateSection = CreateUpdateSection();
            }

            return _updateSection;
        }

        private FrameworkElement EnsureFinishOptionsSection()
        {
            if (_finishOptionsSection == null)
            {
                if (popupShutdownOptions != null)
                {
                    var child = popupShutdownOptions.Child;
                    if (child != null)
                    {
                        popupShutdownOptions.Child = null; // Detach from popup
                        popupShutdownOptions.IsOpen = false;

                        var grid = new Grid
                        {
                            Background = new SolidColorBrush(Color.FromRgb(0x06, 0x0C, 0x14))
                        };

                        var border = child as FrameworkElement;
                        if (border != null)
                        {
                            border.HorizontalAlignment = HorizontalAlignment.Center;
                            border.VerticalAlignment = VerticalAlignment.Center;
                            border.Margin = new Thickness(20);
                        }

                        if (btnCloseShutdownPopup != null)
                        {
                            btnCloseShutdownPopup.Visibility = Visibility.Collapsed;
                        }

                        grid.Children.Add(border);
                        _finishOptionsSection = grid;
                    }
                }
            }
            return _finishOptionsSection;
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
            AddCreateSubfolderDomainItem("nettruyen.tech");
            AddCreateSubfolderDomainItem("nettruyenviet10.com");
            AddCreateSubfolderDomainItem("daomeoden.net");
            AddCreateSubfolderDomainItem("ln.hako.vn");
            AddCreateSubfolderDomainItem("truyenggvn");
            AddCreateSubfolderDomainItem("sayhentai");
            AddCreateSubfolderDomainItem("vi-hentai.pro");
            AddCreateSubfolderDomainItem("nhentai.xxx");
            AddCreateSubfolderDomainItem("hentaiforce.net");
            AddCreateSubfolderDomainItem("hentaiera.com");
            AddCreateSubfolderDomainItem("hentai2read.com");
            AddCreateSubfolderDomainItem("haibabamanga.somee.com");
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
            var tabControl = new TabControl
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0)
            };

            string tutorialsDir = Path.Combine(PortablePaths.AppRoot, "tutorials");
            if (Directory.Exists(tutorialsDir))
            {
                var mdFiles = Directory.GetFiles(tutorialsDir, "*.md").OrderBy(f => Path.GetFileName(f)).ToArray();
                foreach (var mdFile in mdFiles)
                {
                    try
                    {
                        string raw = File.ReadAllText(mdFile, Encoding.UTF8);
                        ParseTutorialFrontmatter(raw, out string viTitle, out string enTitle, out string viBody, out string enBody);
                        string tabHeader = _isVietnameseUi ? viTitle : enTitle;
                        string body = _isVietnameseUi ? viBody : enBody;
                        if (string.IsNullOrWhiteSpace(tabHeader)) tabHeader = Path.GetFileNameWithoutExtension(mdFile);
                        tabControl.Items.Add(CreateTutorialTab(tabHeader, RenderMarkdownToUI(body)));
                    }
                    catch { /* skip broken md files */ }
                }
            }

            if (tabControl.Items.Count == 0)
            {
                var fallback = new TextBlock
                {
                    Text = _isVietnameseUi ? "Không tìm thấy file hướng dẫn trong folder tutorials/" : "No tutorial files found in tutorials/ folder",
                    Foreground = (Brush)TryFindResource("CyberpunkTextBrush") ?? Brushes.White,
                    FontSize = 12,
                    Margin = new Thickness(12)
                };
                return fallback;
            }

            var border = new Border
            {
                Background = (Brush)TryFindResource("CyberpunkCardBrush") ?? new SolidColorBrush(Color.FromRgb(0x0D, 0x12, 0x1F)),
                BorderBrush = (Brush)TryFindResource("CyberpunkBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14)
            };

            border.Child = tabControl;
            return border;
        }

        private void ParseTutorialFrontmatter(string raw, out string viTitle, out string enTitle, out string viBody, out string enBody)
        {
            viTitle = ""; enTitle = ""; viBody = ""; enBody = "";
            string content = raw;

            // Parse YAML frontmatter between --- lines
            if (raw.StartsWith("---"))
            {
                int endFm = raw.IndexOf("\n---", 3);
                if (endFm < 0) endFm = raw.IndexOf("\r\n---", 3);
                if (endFm > 0)
                {
                    string fm = raw.Substring(3, endFm - 3);
                    content = raw.Substring(raw.IndexOf('\n', endFm + 3) + 1);
                    foreach (var line in fm.Split('\n'))
                    {
                        var trimmed = line.Trim().TrimEnd('\r');
                        if (trimmed.StartsWith("vi:")) viTitle = trimmed.Substring(3).Trim();
                        else if (trimmed.StartsWith("en:")) enTitle = trimmed.Substring(3).Trim();
                    }
                }
            }

            // Split by <!-- VI --> and <!-- EN --> markers
            int viIdx = content.IndexOf("<!-- VI -->", StringComparison.OrdinalIgnoreCase);
            int enIdx = content.IndexOf("<!-- EN -->", StringComparison.OrdinalIgnoreCase);

            if (viIdx >= 0 && enIdx >= 0)
            {
                if (viIdx < enIdx)
                {
                    viBody = content.Substring(viIdx + 11, enIdx - viIdx - 11).Trim();
                    enBody = content.Substring(enIdx + 11).Trim();
                }
                else
                {
                    enBody = content.Substring(enIdx + 11, viIdx - enIdx - 11).Trim();
                    viBody = content.Substring(viIdx + 11).Trim();
                }
            }
            else
            {
                // No language markers — use entire content for both
                viBody = content.Trim();
                enBody = content.Trim();
            }
        }

        private UIElement RenderMarkdownToUI(string markdown)
        {
            var stack = new StackPanel();
            if (string.IsNullOrWhiteSpace(markdown)) return stack;

            var lines = markdown.Split('\n');
            var cardBodyLines = new List<string>();
            string currentCardTitle = null;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');

                // ## heading = new card
                if (line.StartsWith("## "))
                {
                    // Flush previous card
                    if (currentCardTitle != null)
                    {
                        AddTutorialCard(stack, currentCardTitle, string.Join("\n", cardBodyLines).Trim());
                        cardBodyLines.Clear();
                    }
                    currentCardTitle = line.Substring(3).Trim();
                }
                else if (currentCardTitle != null)
                {
                    cardBodyLines.Add(line);
                }
                else
                {
                    // Lines before any ## heading — add as plain text
                    if (!string.IsNullOrWhiteSpace(line))
                        cardBodyLines.Add(line);
                }
            }

            // Flush last card
            if (currentCardTitle != null)
                AddTutorialCard(stack, currentCardTitle, string.Join("\n", cardBodyLines).Trim());
            else if (cardBodyLines.Count > 0)
                AddTutorialCard(stack, "", string.Join("\n", cardBodyLines).Trim());

            return stack;
        }

        private TabItem CreateTutorialTab(string headerText, UIElement content)
        {
            var headerBlock = new TextBlock
            {
                Text = headerText,
                FontWeight = FontWeights.Bold,
                FontSize = 11.5,
                Margin = new Thickness(12, 6, 12, 6),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var border = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(4, 2, 4, 2),
                Margin = new Thickness(0, 0, 6, 6),
                Child = headerBlock
            };

            var tabItem = new TabItem
            {
                Header = border,
                Content = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Padding = new Thickness(12, 14, 12, 14),
                    Content = content
                }
            };

            // Custom Template to eliminate default white background and active highlights
            var template = new ControlTemplate(typeof(TabItem));
            var rootBorder = new FrameworkElementFactory(typeof(Border));
            rootBorder.Name = "TabBorder";
            rootBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            rootBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            rootBorder.SetValue(Border.PaddingProperty, new Thickness(4, 2, 4, 2));
            rootBorder.SetValue(Border.MarginProperty, new Thickness(0, 0, 6, 6));

            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            contentPresenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);

            rootBorder.AppendChild(contentPresenter);
            template.VisualTree = rootBorder;

            // Trigger for Selected State
            var selectedTrigger = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x12, 0x2A, 0x42)), "TabBorder"));
            selectedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, (Brush)TryFindResource("CyberpunkCyanBrush") ?? Brushes.Cyan, "TabBorder"));
            selectedTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, (Brush)TryFindResource("CyberpunkCyanBrush") ?? Brushes.Cyan));
            template.Triggers.Add(selectedTrigger);

            // Trigger for Unselected State
            var unselectedTrigger = new Trigger { Property = TabItem.IsSelectedProperty, Value = false };
            unselectedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x0A, 0x10, 0x1C)), "TabBorder"));
            unselectedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, (Brush)TryFindResource("CyberpunkBorderBrush") ?? Brushes.DarkGray, "TabBorder"));
            unselectedTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, (Brush)TryFindResource("CyberpunkMutedTextBrush") ?? Brushes.Gray));
            template.Triggers.Add(unselectedTrigger);

            tabItem.Template = template;
            return tabItem;
        }

        private void AddTutorialCard(StackPanel parent, string title, string body)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x06, 0x0B, 0x14)),
                BorderBrush = (Brush)TryFindResource("CyberpunkBorderBrush") ?? Brushes.DarkGray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 10)
            };

            var stack = new StackPanel();

            if (!string.IsNullOrWhiteSpace(title))
            {
                var titleBlock = new TextBlock
                {
                    Text = title,
                    Foreground = (Brush)TryFindResource("CyberpunkYellowBrush") ?? Brushes.Gold,
                    FontWeight = FontWeights.Bold,
                    FontSize = 12.5,
                    Margin = new Thickness(0, 0, 0, 6)
                };
                stack.Children.Add(titleBlock);
            }

            if (!string.IsNullOrWhiteSpace(body))
            {
                var bodyBlock = new TextBlock
                {
                    Text = body,
                    Foreground = (Brush)TryFindResource("CyberpunkTextBrush") ?? Brushes.White,
                    FontSize = 11.5,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 18
                };
                stack.Children.Add(bodyBlock);
            }

            card.Child = stack;
            parent.Children.Add(card);
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

        private FrameworkElement CreateTraceLogSection()
        {
            var root = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var controlBar = new DockPanel { LastChildFill = false, Margin = new Thickness(10, 8, 10, 6) };
            
            var textBlock = new TextBlock
            {
                Text = "TRACE LOG",
                Foreground = (Brush)TryFindResource("CyberpunkCyanBrush"),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(textBlock, Dock.Left);
            controlBar.Children.Add(textBlock);

            var toggleAutoScroll = new System.Windows.Controls.Primitives.ToggleButton
            {
                Style = (Style)TryFindResource("ToggleButtonSwitch"),
                IsChecked = true,
                Height = 16,
                Width = 32,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            chkAutoScrollLog = toggleAutoScroll;
            DockPanel.SetDock(toggleAutoScroll, Dock.Left);
            controlBar.Children.Add(toggleAutoScroll);

            var toggleErrorOnly = new System.Windows.Controls.Primitives.ToggleButton
            {
                Style = (Style)TryFindResource("ToggleButtonSwitch"),
                IsChecked = false,
                Height = 16,
                Width = 32,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Show error only"
            };
            toggleErrorOnly.Checked += ChkErrorOnlyLog_Checked;
            toggleErrorOnly.Unchecked += ChkErrorOnlyLog_Unchecked;
            chkErrorOnlyLog = toggleErrorOnly;
            DockPanel.SetDock(toggleErrorOnly, Dock.Left);
            controlBar.Children.Add(toggleErrorOnly);

            var buttonClear = new Button
            {
                Content = "CLEAR",
                Style = (Style)TryFindResource("CyberpunkButtonCyan"),
                Height = 20,
                Width = 60,
                FontSize = 9,
                Padding = new Thickness(0),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            buttonClear.Click += BtnClearLog_Click;
            btnClearLog = buttonClear;
            DockPanel.SetDock(buttonClear, Dock.Left);
            controlBar.Children.Add(buttonClear);

            Grid.SetRow(controlBar, 0);
            root.Children.Add(controlBar);

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x06, 0x0b, 0x14)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x1d, 0x2b, 0x3d)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(6)
            };

            _mdLogStackPanel = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            _scrollLogHost = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6),
                Content = _mdLogStackPanel
            };

            txtLog = new RichTextBox
            {
                Visibility = Visibility.Collapsed
            };

            var containerGrid = new Grid();
            containerGrid.Children.Add(_scrollLogHost);
            containerGrid.Children.Add(txtLog);

            border.Child = containerGrid;

            Grid.SetRow(border, 1);
            root.Children.Add(border);

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
                    if (IsNovelDownloadTabSelected())
                    {
                        EnsureLightNovelDeskInitialized();
                    }
                    break;
                case AppSection.Watch:
                    _sectionContentHost.Content = EnsureWatchSection();
                    _readerHasUserClickedInWatch = false;
                    EnsureReaderReady();
                    RefreshReaderLibraryIfNeeded(forceRefresh: false);
                    StartReaderAutoRefresh();
                    PromptReaderWatchAppSelectionIfNeeded();
                    break;
                case AppSection.About:
                    _sectionContentHost.Content = EnsureAboutSection();
                    StopReaderAutoRefresh();
                    break;
                case AppSection.TraceLog:
                    _sectionContentHost.Content = EnsureTraceLogSection();
                    StopReaderAutoRefresh();
                    break;
                case AppSection.Update:
                    _sectionContentHost.Content = EnsureUpdateSection();
                    StopReaderAutoRefresh();
                    break;
                case AppSection.FinishOptions:
                    _sectionContentHost.Content = EnsureFinishOptionsSection();
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
                if (pair.Key == AppSection.FinishOptions)
                {
                    pair.Value.Background = isActive
                        ? new SolidColorBrush(Color.FromRgb(0x42, 0x0f, 0x2b))
                        : new SolidColorBrush(Color.FromRgb(0x2d, 0x0a, 0x1d));
                    pair.Value.BorderBrush = isActive
                        ? new SolidColorBrush(Color.FromRgb(0xff, 0x00, 0x7f))
                        : new SolidColorBrush(Color.FromRgb(0xff, 0x00, 0x7f));
                    pair.Value.Foreground = isActive
                        ? new SolidColorBrush(Color.FromRgb(0xff, 0x7c, 0xc1))
                        : new SolidColorBrush(Color.FromRgb(0xff, 0x7c, 0xc1));
                }
                else
                {
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
                    _sectionTitleText.Text = _isVietnameseUi ? "Nguồn" : "Source";
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
                    _sectionTitleText.Text = isVietnamese ? "Hướng dẫn & Cấu hình" : "Tutorial & Config";
                    _sectionHintText.Text = isVietnamese
                        ? "Cấu hình hệ thống DNS & Traffic, hướng dẫn tải truyện, thao tác download, quét chap thiếu và công cụ."
                        : "System DNS & Traffic settings, download instructions, operations, scan missing chapters, and utilities.";
                    break;
                case AppSection.TraceLog:
                    _sectionTitleText.Text = isVietnamese ? "Lịch sử log hệ thống" : "System trace log";
                    _sectionHintText.Text = isVietnamese
                        ? "Theo dõi log toàn hệ thống theo thời gian thực."
                        : "Monitor global system log statements in real-time.";
                    break;
                case AppSection.Update:
                    _sectionTitleText.Text = isVietnamese ? "Cập nhật" : "Update";
                    _sectionHintText.Text = isVietnamese
                        ? "Xem build hiện tại, thư mục app, và điểm kiểm tra trước khi đóng gói."
                        : "See current build info, app paths, and quick package-check details.";
                    break;
                case AppSection.FinishOptions:
                    _sectionTitleText.Text = isVietnamese ? "Tùy chọn hoàn thành" : "Finish options";
                    _sectionHintText.Text = isVietnamese
                        ? "Thiết lập hành động chạy lệnh, phím tắt hoặc hẹn giờ tắt máy tự động sau khi hoàn tất tải về."
                        : "Configure command execution, hotkeys, or auto-shutdown timer after downloads finish.";
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

            MinWidth = 550;
            MinHeight = 560;
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
            if (tabHentaiSourceRootItem != null)
            {
                tabHentaiSourceRootItem.Header = _isVietnameseUi ? "Nguồn Hentai" : "Source Hentai";
            }
            if (tabPasswordRootItem != null)
            {
                tabPasswordRootItem.Header = _isVietnameseUi ? "Mật khẩu" : "Password";
            }
            if (tabDownloadRoot != null && tabDownloadRoot.Items.Count >= 2)
            {
                if (tabDownloadRoot.Items[0] is TabItem mangaTab)
                {
                    mangaTab.Header = _isVietnameseUi ? "Tải Manga" : "Download Manga";
                }

                if (tabDownloadRoot.Items.Count >= 3)
                {
                    if (tabDownloadRoot.Items[1] is TabItem missingTab)
                    {
                        missingTab.Header = _isVietnameseUi ? "Check thiếu chap tải" : "Check download missing chapter";
                    }
                    if (tabDownloadRoot.Items[2] is TabItem novelTab)
                    {
                        novelTab.Header = _isVietnameseUi ? "Tải Novel" : "Download Novel";
                    }
                    if (tabDownloadRoot.Items.Count >= 4 && tabDownloadRoot.Items[3] is TabItem splitMergeTab)
                    {
                        splitMergeTab.Header = _isVietnameseUi ? "split / merge folder" : "split / merge folder";
                    }
                }
                else if (tabDownloadRoot.Items[1] is TabItem novelTab)
                {
                    novelTab.Header = _isVietnameseUi ? "Tải Novel" : "Download Novel";
                }
            }

            if (_navigationButtons.Count > 0)
            {
                _navigationButtons[AppSection.ChooseSource].Content = CreateNavigationButtonContent(_isVietnameseUi ? "Nguồn" : "Source", "Ctrl+Shift+S");
                _navigationButtons[AppSection.Download].Content = CreateNavigationButtonContent(_isVietnameseUi ? "Tải về" : "Download", "Ctrl+Shift+D");
                _navigationButtons[AppSection.Watch].Content = CreateNavigationButtonContent(_isVietnameseUi ? "Xem truyện" : "Watch", "Ctrl+Shift+W");
                _navigationButtons[AppSection.About].Content = CreateNavigationButtonContent(_isVietnameseUi ? "Giới thiệu" : "About", "Ctrl+Shift+A");
                _navigationButtons[AppSection.TraceLog].Content = CreateNavigationButtonContent(_isVietnameseUi ? "Trace Log" : "Trace Log", "Ctrl+Shift+L");
                _navigationButtons[AppSection.Update].Content = CreateNavigationButtonContent(_isVietnameseUi ? "Cập nhật" : "Update", "Ctrl+Shift+U");
            }

            if (_showFloatRailButton != null)
            {
                var fgBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD4, 0x6A));
                _showFloatRailButton.Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x1A, 0x00));
                _showFloatRailButton.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xA6, 0x00));
                _showFloatRailButton.Foreground = fgBrush;
                _showFloatRailButton.Content = CreateIconContent("M4,5h16a1,1 0 011,1v1a1,1 0 01-1,1H4a1,1 0 01-1-1V6a1,1 0 011-1zm6,6h10a1,1 0 011,1v1a1,1 0 01-1,1H10a1,1 0 01-1-1v-1a1,1 0 011-1zm-4,6h14a1,1 0 011,1v1a1,1 0 01-1,1H6a1,1 0 01-1-1v-1a1,1 0 011-1z", fgBrush);
                _showFloatRailButton.ToolTip = _isVietnameseUi ? "Nút nổi (Ctrl+Shift+F)" : "Float button (Ctrl+Shift+F)";
            }

            if (btnShutdownMenu != null)
            {
                var fgBrush = new SolidColorBrush(Color.FromRgb(0xff, 0x7c, 0xc1));
                btnShutdownMenu.Background = new SolidColorBrush(Color.FromRgb(0x2d, 0x0a, 0x1d));
                btnShutdownMenu.BorderBrush = new SolidColorBrush(Color.FromRgb(0xff, 0x00, 0x7f));
                btnShutdownMenu.Foreground = fgBrush;
                btnShutdownMenu.Content = CreateIconContent("M10,2h4v2h-4z M11,4h2v1h-2z M12,5c-4.42,0-8,3.58-8,8s3.58,8,8,8s8-3.58,8-8S16.42,5,12,5z M12,19c-3.31,0-6-2.69-6-6s2.69-6,6-6s6,2.69,6,6S15.31,19,12,19z M10.5,15L8,12.5l1.4-1.4l1.1,1.1l3.6-3.6l1.4,1.4L10.5,15z", fgBrush);
                btnShutdownMenu.ToolTip = _isVietnameseUi ? "Tùy chọn hoàn thành" : "Finish options";
            }

            if (_h2rLogButton != null)
            {
                _h2rLogButton.Background = new SolidColorBrush(Color.FromRgb(0x0e, 0x22, 0x30));
                _h2rLogButton.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xa8, 0xff));
                _h2rLogButton.Foreground = new SolidColorBrush(Color.FromRgb(0x7c, 0xe8, 0xff));
                _h2rLogButton.Content = CreateNavigationButtonContent(_isVietnameseUi ? "Trace Log" : "Trace Log", "");
            }

            if (_brandTitleText != null)
            {
                _brandTitleText.Text = "COMIC DOWNLOADER GMTPC";
            }



            if (_toolbarClearTempButton != null)
            {
                string clearTempText = _isVietnameseUi ? "XÓA TẠM" : "CLEAR TEMP";
                _toolbarClearTempButton.Content = clearTempText;
                _toolbarClearTempButton.ToolTip = clearTempText;
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

            if (_aboutSection != null)
            {
                _aboutSection = CreateAboutSection();
                if (_currentSection == AppSection.About && _sectionContentHost != null)
                {
                    _sectionContentHost.Content = _aboutSection;
                }
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

            MinWidth = 550;
            MinHeight = 560;
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


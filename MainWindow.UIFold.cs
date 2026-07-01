using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private const double AutoCollapseWidthThreshold = 1280;

        private enum PanelViewMode
        {
            ShowAll,
            ShowSearch,
            ShowList
        }

        private PanelViewMode _requestedPanelViewMode = PanelViewMode.ShowSearch;
        private PanelViewMode _effectivePanelViewMode = PanelViewMode.ShowSearch;

        private void BtnShowSearch_Click(object sender, RoutedEventArgs e)
        {
            SetRequestedPanelViewMode(PanelViewMode.ShowSearch);
        }

        private void BtnToggleLeftPanel_Click(object sender, RoutedEventArgs e)
        {
            if (_effectivePanelViewMode == PanelViewMode.ShowList)
            {
                SetRequestedPanelViewMode(PanelViewMode.ShowSearch);
                return;
            }

            SetRequestedPanelViewMode(PanelViewMode.ShowList);
        }

        private void BtnShowList_Click(object sender, RoutedEventArgs e)
        {
            SetRequestedPanelViewMode(PanelViewMode.ShowList);
        }

        private void BtnShowAll_Click(object sender, RoutedEventArgs e)
        {
            SetRequestedPanelViewMode(PanelViewMode.ShowList);
        }

        internal void UpdateLeftPanelResponsiveState(double windowWidth)
        {
            if (_navigationButtonHost == null)
            {
                return;
            }

            bool compact = windowWidth < AutoCollapseWidthThreshold;
            foreach (System.Windows.Controls.Button button in _navigationButtonHost.Children)
            {
                button.HorizontalContentAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            }
        }

        private void SetRequestedPanelViewMode(PanelViewMode mode)
        {
            _requestedPanelViewMode = mode;
            ApplyPanelViewMode(mode);
        }

        private void ApplyPanelViewMode(PanelViewMode mode)
        {
            _effectivePanelViewMode = mode;

            switch (mode)
            {
                case PanelViewMode.ShowSearch:
                    SelectAppSection(AppSection.ChooseSource);
                    break;
                case PanelViewMode.ShowList:
                case PanelViewMode.ShowAll:
                    SelectAppSection(AppSection.Download);
                    break;
            }

            UpdateLayout();
            UpdateFoldButtonUi();
        }

        private void UpdateFoldButtonUi()
        {
            if (btnToggleLeftPanelHeaderLegacy == null)
            {
                return;
            }

            btnToggleLeftPanelHeaderLegacy.Visibility = Visibility.Collapsed;
            btnToggleLeftPanelHeaderLegacy.ToolTip = _isVietnameseUi
                ? "Chuyển nhanh giữa nguồn và tải"
                : "Quick switch between source and download";
        }

        private void MainWindow_PreviewPanelHotkeys(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (HandleReaderHotkeys(e))
            {
                return;
            }

            var focusedElement = FocusManager.GetFocusedElement(this);
            bool isTextInputFocused =
                focusedElement is System.Windows.Controls.TextBox ||
                focusedElement is System.Windows.Controls.Primitives.TextBoxBase ||
                focusedElement is System.Windows.Controls.PasswordBox ||
                focusedElement is System.Windows.Controls.ComboBox;

            if (e.Key == Key.Enter)
            {
                if (_currentSection == AppSection.Download || _currentSection == AppSection.ChooseSource)
                {
                    if (isTextInputFocused)
                    {
                        return;
                    }

                    try
                    {
                        if (Clipboard.ContainsText())
                        {
                            string text = Clipboard.GetText()?.Trim();
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                _ = AppendSupportedInputLinks(text);
                                e.Handled = true;
                                return;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }

            ModifierKeys modifiers = Keyboard.Modifiers;
            bool hasControl = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            bool hasShift = (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

            if (!hasControl)
            {
                return;
            }

            if (!hasShift && e.Key == Key.V)
            {
                if (!isTextInputFocused && (_currentSection == AppSection.Download || _currentSection == AppSection.ChooseSource))
                {
                    try
                    {
                        if (Clipboard.ContainsText())
                        {
                            string text = Clipboard.GetText()?.Trim();
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                _ = AppendSupportedInputLinks(text);
                                e.Handled = true;
                                return;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }

            if (!hasShift && e.Key == Key.O)
            {
                BtnLoadCustom_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            if (!hasShift)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.F:
                    ToggleLightNovelFloatingControlWindow();
                    e.Handled = true;
                    return;
                case Key.O:
                    BtnOpenFolder_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                case Key.C:
                    if (_lightNovelCopyCts == null)
                    {
                        _ = StartLightNovelAutoCopyAsync();
                    }
                    else
                    {
                        StopLightNovelAutoCopy();
                    }
                    e.Handled = true;
                    return;
                case Key.T:
                    if (_downloadCts == null && btnStartDownload?.IsChecked != true)
                    {
                        _ = StartPictureDownloadFromFloatingAsync();
                    }
                    else
                    {
                        StopPictureDownloadFromFloating();
                    }
                    e.Handled = true;
                    return;
                case Key.R:
                    BtnRetryErrors_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                case Key.S:
                    SelectAppSection(AppSection.ChooseSource);
                    e.Handled = true;
                    return;
                case Key.D:
                    SelectAppSection(AppSection.Download);
                    e.Handled = true;
                    return;
                case Key.W:
                    SelectAppSection(AppSection.Watch);
                    e.Handled = true;
                    return;
                case Key.A:
                    SelectAppSection(AppSection.About);
                    e.Handled = true;
                    return;
                case Key.U:
                    SelectAppSection(AppSection.Update);
                    e.Handled = true;
                    return;
            }
        }

        private void SnapWindowToHalf(bool leftSide)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            System.Windows.Forms.Screen screen = handle != IntPtr.Zero ? System.Windows.Forms.Screen.FromHandle(handle) : System.Windows.Forms.Screen.PrimaryScreen;
            var workArea = screen.WorkingArea;

            WindowState = WindowState.Normal;
            Left = leftSide ? workArea.Left : workArea.Left + (workArea.Width / 2.0);
            Top = workArea.Top;
            Width = workArea.Width / 2.0;
            Height = workArea.Height;
            Activate();
        }
    }
}

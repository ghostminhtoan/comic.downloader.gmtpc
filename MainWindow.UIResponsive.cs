using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private static readonly int[] UiZoomPresets = { 90, 100, 110, 120, 130, 140, 150, 160, 175 };
        private const double DefaultUiZoomPercent = 100.0;
        private double _uiZoomPercent = DefaultUiZoomPercent;
        private ComboBox _dpiPresetCombo;
        private TextBlock _sectionTitleText;
        private TextBlock _sectionHintText;
        private bool _windowPseudoMaximized;
        private double _restoreWindowLeft;
        private double _restoreWindowTop;
        private double _restoreWindowWidth;
        private double _restoreWindowHeight;
        private bool _isRailHidden = false;

        public static readonly DependencyProperty IsPortraitModeProperty =
            DependencyProperty.Register("IsPortraitMode", typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

        public bool IsPortraitMode
        {
            get { return (bool)GetValue(IsPortraitModeProperty); }
            set { SetValue(IsPortraitModeProperty, value); }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            EnsureMainGridShell();
            ApplyAdaptiveLayout(e.NewSize);
            UpdateLeftPanelResponsiveState(e.NewSize.Width);

            Rect workArea = SystemParameters.WorkArea;
            IsPortraitMode = workArea.Height > workArea.Width || e.NewSize.Height > e.NewSize.Width;

            ApplyThumbnailDensity();
            if (_isResultsThumbnailViewEnabled)
            {
                RebuildThumbnailResultsView();
            }
        }

        private void EnsureMainGridShell()
        {
            if (gridMainContent == null)
            {
                return;
            }

            if (!_workspaceShellInitialized)
            {
                return;
            }

            if (gridMainContent.ColumnDefinitions.Count != 3)
            {
                gridMainContent.ColumnDefinitions.Clear();
                gridMainContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(108) });
                gridMainContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                gridMainContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            if (gridMainContent.RowDefinitions.Count != 2)
            {
                gridMainContent.RowDefinitions.Clear();
                gridMainContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                gridMainContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            }
        }

        private void ApplyAdaptiveLayout(Size size)
        {
            bool compactWidth = size.Width < 1280;
            bool compactHeight = size.Height < 820;
            bool compactMode = compactWidth || compactHeight;
            bool ultraCompact = size.Width < 1100 || size.Height < 720;

            if (rootLayout != null)
            {
                rootLayout.Margin = (_windowPseudoMaximized || WindowState == WindowState.Maximized)
                    ? new Thickness(0)
                    : ultraCompact
                        ? new Thickness(8)
                        : compactMode
                            ? new Thickness(12)
                            : new Thickness(18);
            }

            if (headerPanel != null)
            {
                headerPanel.Padding = ultraCompact
                    ? new Thickness(10)
                    : compactMode
                        ? new Thickness(12)
                        : new Thickness(16);
            }

            if (gridMainContent != null)
            {
                SetLayoutScale(gridMainContent, _uiZoomPercent / 100.0);
            }

            if (_isRailHidden)
            {
                if (gridMainContent != null && gridMainContent.ColumnDefinitions.Count >= 3)
                {
                    gridMainContent.ColumnDefinitions[0].Width = new GridLength(0);
                    gridMainContent.ColumnDefinitions[1].Width = new GridLength(0);
                }
                if (_navigationRailBorder != null)
                {
                    _navigationRailBorder.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                if (_navigationRailBorder != null)
                {
                    _navigationRailBorder.Visibility = Visibility.Visible;
                }
                UpdateWorkspaceCompactLayout(size.Width, compactMode, ultraCompact);

                if (gridMainContent != null && gridMainContent.ColumnDefinitions.Count >= 3)
                {
                    gridMainContent.ColumnDefinitions[0].Width = new GridLength(ultraCompact ? 96 : compactMode ? 108 : 128);
                    gridMainContent.ColumnDefinitions[1].Width = new GridLength(compactMode ? 10 : 18);
                }
            }

            if (_navigationButtonHost != null)
            {
                foreach (Button button in _navigationButtonHost.Children.OfType<Button>())
                {
                    button.FontSize = ultraCompact ? 9 : 10;
                    button.Padding = ultraCompact ? new Thickness(5, 6, 5, 6) : new Thickness(8, 8, 8, 8);
                    button.MinHeight = ultraCompact ? 36 : 42;
                    button.HorizontalAlignment = HorizontalAlignment.Stretch;
                    button.Width = double.NaN;

                    StackPanel content = button.Content as StackPanel;
                    if (content != null && content.Children.Count > 1 && content.Children[1] is TextBlock shortcutText)
                    {
                        shortcutText.Visibility = ultraCompact ? Visibility.Collapsed : Visibility.Visible;
                    }
                }
            }

            if (_sectionTitleText != null)
            {
                _sectionTitleText.FontSize = ultraCompact ? 16 : 18;
            }

            if (_sectionHintText != null)
            {
                _sectionHintText.FontSize = ultraCompact ? 10 : 11;
            }

            if (headerUtilityPanel != null)
            {
                headerUtilityPanel.MinWidth = 0;
                headerUtilityPanel.MaxWidth = double.PositiveInfinity;
            }

            if (languageCard != null)
            {
                languageCard.Width = double.NaN;
                languageCard.MinWidth = 0;
                languageCard.MaxWidth = double.PositiveInfinity;
                languageCard.HorizontalAlignment = HorizontalAlignment.Stretch;
            }

            if (scaleCard != null)
            {
                scaleCard.Width = double.NaN;
                scaleCard.MinWidth = 0;
                scaleCard.MaxWidth = double.PositiveInfinity;
                scaleCard.HorizontalAlignment = HorizontalAlignment.Stretch;
            }

            if (windowControlsHost != null)
            {
                windowControlsHost.Margin = (_windowPseudoMaximized || WindowState == WindowState.Maximized)
                    ? new Thickness(0, 0, -4, 0)
                    : new Thickness(0);
                windowControlsHost.Padding = (_windowPseudoMaximized || WindowState == WindowState.Maximized)
                    ? new Thickness(4, 5, 2, 5)
                    : new Thickness(5);
            }
        }

        private void UpdateWorkspaceCompactLayout(double width, bool compactMode, bool ultraCompact)
        {
            if (gridMainContent == null || !_workspaceShellInitialized || _navigationRailBorder == null || _shellRootGrid == null)
            {
                return;
            }

            if (gridMainContent.ColumnDefinitions.Count != 3)
            {
                gridMainContent.ColumnDefinitions.Clear();
                gridMainContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(108) });
                gridMainContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                gridMainContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            if (gridMainContent.RowDefinitions.Count != 2)
            {
                gridMainContent.RowDefinitions.Clear();
                gridMainContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                gridMainContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            }

            Grid.SetColumn(_navigationRailBorder, 0);
            Grid.SetRow(_navigationRailBorder, 0);
            Grid.SetRowSpan(_navigationRailBorder, 2);
            Grid.SetColumn(_shellRootGrid, 2);
            Grid.SetRow(_shellRootGrid, 1);

            _navigationRailBorder.Padding = ultraCompact ? new Thickness(6) : new Thickness(8, 10, 8, 10);

            if (_isRailHidden)
            {
                gridMainContent.ColumnDefinitions[0].Width = new GridLength(0);
                gridMainContent.ColumnDefinitions[1].Width = new GridLength(0);
                _navigationRailBorder.Visibility = Visibility.Collapsed;
            }
            else
            {
                _navigationRailBorder.Visibility = Visibility.Visible;
                if (gridMainContent.ColumnDefinitions.Count >= 3)
                {
                    double railWidth = width < 920 ? 96 : width < 1080 ? 108 : compactMode ? 108 : 128;
                    gridMainContent.ColumnDefinitions[0].Width = new GridLength(railWidth);
                    gridMainContent.ColumnDefinitions[1].Width = new GridLength(width < 920 ? 8 : compactMode ? 10 : 18);
                }
            }
        }

        private static void SetLayoutScale(FrameworkElement element, double scale)
        {
            if (element == null)
            {
                return;
            }

            if (Math.Abs(scale - 1.0) < 0.001)
            {
                element.LayoutTransform = Transform.Identity;
                return;
            }

            element.LayoutTransform = new ScaleTransform(scale, scale);
        }

        private void DpiPresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_dpiPresetCombo?.SelectedItem is UiZoomPreset preset)
            {
                SetUiZoomPercent(preset.Percent);
            }
        }

        private void SetUiZoomPercent(double zoomPercent)
        {
            int snapped = SnapToZoomPreset(zoomPercent);
            if (Math.Abs(_uiZoomPercent - snapped) < 0.001)
            {
                UpdateZoomDisplay();
                return;
            }

            _uiZoomPercent = snapped;
            UpdateZoomDisplay();
            ApplyAdaptiveLayout(new Size(ActualWidth, ActualHeight));
        }

        private static int SnapToZoomPreset(double zoomPercent)
        {
            return UiZoomPresets
                .OrderBy(percent => Math.Abs(percent - zoomPercent))
                .ThenBy(percent => percent)
                .First();
        }

        private void UpdateZoomDisplay()
        {
            int snapped = SnapToZoomPreset(_uiZoomPercent);
            if (txtScaleValue != null)
            {
                txtScaleValue.Text = snapped + "%";
            }

            if (_dpiPresetCombo != null)
            {
                var selected = _dpiPresetCombo.Items.OfType<UiZoomPreset>().FirstOrDefault(item => item.Percent == snapped);
                if (!ReferenceEquals(_dpiPresetCombo.SelectedItem, selected))
                {
                    _dpiPresetCombo.SelectedItem = selected;
                }
            }
        }

        private void ChangeUiZoomPreset(int direction)
        {
            int snapped = SnapToZoomPreset(_uiZoomPercent);
            int index = Array.IndexOf(UiZoomPresets, snapped);
            if (index < 0)
            {
                index = 1;
            }

            int nextIndex = Math.Max(0, Math.Min(UiZoomPresets.Length - 1, index + direction));
            SetUiZoomPercent(UiZoomPresets[nextIndex]);
        }

        private void MainWindow_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
            {
                return;
            }

            if (_currentSection == AppSection.Watch)
            {
                return;
            }

            ChangeUiZoomPreset(e.Delta > 0 ? 1 : -1);
            e.Handled = true;
        }

        private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
        {
            ChangeUiZoomPreset(-1);
        }

        private void BtnZoomReset_Click(object sender, RoutedEventArgs e)
        {
            SetUiZoomPercent(DefaultUiZoomPercent);
        }

        private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
        {
            ChangeUiZoomPreset(1);
        }

        private void SldUiScale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            SetUiZoomPercent(e.NewValue);
        }
    }
}

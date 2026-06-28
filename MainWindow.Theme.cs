using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private bool _isDayTheme = false;

        private void UnfreezeApplicationBrushes()
        {
            try
            {
                UnfreezeResourceDictionary(Application.Current.Resources);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Theme] Unfreeze resources failed: {ex.Message}");
            }
        }

        private void UnfreezeResourceDictionary(ResourceDictionary dict)
        {
            if (dict == null) return;

            var keys = new List<object>();
            foreach (var key in dict.Keys)
            {
                keys.Add(key);
            }

            foreach (var key in keys)
            {
                if (dict[key] is Brush brush && brush.IsFrozen)
                {
                    dict[key] = brush.Clone();
                }
            }

            foreach (var merged in dict.MergedDictionaries)
            {
                UnfreezeResourceDictionary(merged);
            }
        }

        private void ApplyCurrentTheme()
        {
            try
            {
                if (_isDayTheme)
                {
                    ApplyDayThemePalette();
                }
                else
                {
                    ApplyNightThemePalette();
                }

                UpdateThemeText();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Theme] ApplyTheme failed: {ex.Message}");
            }
        }

        private void ApplyDayThemePalette()
        {
            SetSolidBrushColor("CyberpunkCardBrush", "#F7FAFF");
            SetSolidBrushColor("CyberpunkCyanBrush", "#0A84C6");
            SetSolidBrushColor("CyberpunkPinkBrush", "#C44569");
            SetSolidBrushColor("CyberpunkYellowBrush", "#D49A00");
            SetSolidBrushColor("CyberpunkGreenBrush", "#2E9F5B");
            SetSolidBrushColor("CyberpunkBorderBrush", "#C7D6E7");
            SetSolidBrushColor("CyberpunkSurfaceAltBrush", "#EAF1F8");
            SetSolidBrushColor("CyberpunkSurfaceEdgeBrush", "#D6E1EC");
            SetSolidBrushColor("CyberpunkDangerBrush", "#D95D77");
            SetSolidBrushColor("CyberpunkTextBrush", "#102033");
            SetSolidBrushColor("CyberpunkMutedTextBrush", "#5B7088");
            SetSolidBrushColor("ThemeButtonCyanBackgroundBrush", "#E6F3FF");
            SetSolidBrushColor("ThemeButtonCyanHoverBrush", "#D5EBFF");
            SetSolidBrushColor("ThemeButtonPinkBackgroundBrush", "#FDECF3");
            SetSolidBrushColor("ThemeButtonPinkHoverBrush", "#F9DCE8");
            SetSolidBrushColor("ThemeButtonImportantBackgroundBrush", "#FFF4D6");
            SetSolidBrushColor("ThemeButtonImportantHoverBrush", "#FFE8AE");
            SetSolidBrushColor("ThemeSwitchTrackBrush", "#DCE7F2");
            SetSolidBrushColor("ThemeSwitchLeftOffBrush", "#F6C8D6");
            SetSolidBrushColor("ThemeSwitchLeftOnBrush", "#EEF4FA");
            SetSolidBrushColor("ThemeSwitchLeftBorderBrush", "#D68BA5");
            SetSolidBrushColor("ThemeSwitchCenterBorderBrush", "#B8CCDF");
            SetSolidBrushColor("ThemeSwitchRightOnBrush", "#CFEFDD");
            SetSolidBrushColor("ThemeSwitchThumbBorderOffBrush", "#D48CA4");
            SetSolidBrushColor("ThemeSwitchThumbBorderOnBrush", "#87B8D8");
            SetSolidBrushColor("ThemeSidebarButtonBackgroundBrush", "#F5F9FD");
            SetSolidBrushColor("ThemeSidebarButtonHoverBrush", "#EAF2FA");
            SetSolidBrushColor("ThemeSidebarButtonHoverBorderBrush", "#9AB9D4");
            SetSolidBrushColor("ThemeSidebarButtonPressedBrush", "#E2EDF8");
            SetSolidBrushColor("ThemeOuterGlowHoverBrush", "#7FA7C940");
            SetGradientBrushStops("CyberpunkDarkBrush", "#F8FBFF", "#EEF4FA", "#E6EEF8");
            SetGradientBrushStops("CyberpunkHeroBrush", "#FFFFFF", "#EEF5FB", "#E3EDF8");
        }

        private void ApplyNightThemePalette()
        {
            SetSolidBrushColor("CyberpunkCardBrush", "#0D121F");
            SetSolidBrushColor("CyberpunkCyanBrush", "#00E5FF");
            SetSolidBrushColor("CyberpunkPinkBrush", "#FF2A85");
            SetSolidBrushColor("CyberpunkYellowBrush", "#FFB800");
            SetSolidBrushColor("CyberpunkGreenBrush", "#28FF7A");
            SetSolidBrushColor("CyberpunkBorderBrush", "#1A2436");
            SetSolidBrushColor("CyberpunkSurfaceAltBrush", "#111827");
            SetSolidBrushColor("CyberpunkSurfaceEdgeBrush", "#1F293D");
            SetSolidBrushColor("CyberpunkDangerBrush", "#FF4D6D");
            SetSolidBrushColor("CyberpunkTextBrush", "#F8FAFC");
            SetSolidBrushColor("CyberpunkMutedTextBrush", "#8F9EB2");
            SetSolidBrushColor("ThemeButtonCyanBackgroundBrush", "#122538");
            SetSolidBrushColor("ThemeButtonCyanHoverBrush", "#183754");
            SetSolidBrushColor("ThemeButtonPinkBackgroundBrush", "#2F1823");
            SetSolidBrushColor("ThemeButtonPinkHoverBrush", "#451F33");
            SetSolidBrushColor("ThemeButtonImportantBackgroundBrush", "#3A1A00");
            SetSolidBrushColor("ThemeButtonImportantHoverBrush", "#5B2A00");
            SetSolidBrushColor("ThemeSwitchTrackBrush", "#11131B");
            SetSolidBrushColor("ThemeSwitchLeftOffBrush", "#4A1423");
            SetSolidBrushColor("ThemeSwitchLeftOnBrush", "#0E1723");
            SetSolidBrushColor("ThemeSwitchLeftBorderBrush", "#6E2740");
            SetSolidBrushColor("ThemeSwitchCenterBorderBrush", "#243A56");
            SetSolidBrushColor("ThemeSwitchRightOnBrush", "#1A5A34");
            SetSolidBrushColor("ThemeSwitchThumbBorderOffBrush", "#FFD9DF");
            SetSolidBrushColor("ThemeSwitchThumbBorderOnBrush", "#E8FDFF");
            SetSolidBrushColor("ThemeSidebarButtonBackgroundBrush", "#142437");
            SetSolidBrushColor("ThemeSidebarButtonHoverBrush", "#0E1726");
            SetSolidBrushColor("ThemeSidebarButtonHoverBorderBrush", "#4F7B97");
            SetSolidBrushColor("ThemeSidebarButtonPressedBrush", "#0A1320");
            SetSolidBrushColor("ThemeOuterGlowHoverBrush", "#26FFFFFF");
            SetGradientBrushStops("CyberpunkDarkBrush", "#06090F", "#080D18", "#0A1120");
            SetGradientBrushStops("CyberpunkHeroBrush", "#10172A", "#1A253E", "#0A0E1A");
        }

        private void SetSolidBrushColor(string key, string colorHex)
        {
            var brush = Application.Current.TryFindResource(key) as SolidColorBrush;
            if (brush != null)
            {
                brush.Color = (Color)ColorConverter.ConvertFromString(colorHex);
            }
        }

        private void SetGradientBrushStops(string key, params string[] colorHexes)
        {
            var brush = Application.Current.TryFindResource(key) as GradientBrush;
            if (brush != null)
            {
                for (int i = 0; i < brush.GradientStops.Count && i < colorHexes.Length; i++)
                {
                    brush.GradientStops[i].Color = (Color)ColorConverter.ConvertFromString(colorHexes[i]);
                }
            }
        }

        private void UpdateThemeText()
        {
            var pinButton = FindName("btnPinWindow") as Button
                ?? FindName("btnMainTheme") as Button
                ?? FindName("btnMainThemeLegacy") as Button;
            if (pinButton != null)
            {
                bool isPinned = Topmost;
                var accentBrush = Application.Current.TryFindResource(isPinned ? "CyberpunkYellowBrush" : "CyberpunkCyanBrush") as Brush;

                pinButton.Content = isPinned ? "📌" : "📍";
                pinButton.ToolTip = _isVietnameseUi
                    ? (isPinned ? "Bỏ ghim cửa sổ" : "Ghim cửa sổ")
                    : (isPinned ? "Unpin window" : "Pin window");
                pinButton.Foreground = accentBrush;
                pinButton.BorderBrush = accentBrush;
            }
        }
    }
}
